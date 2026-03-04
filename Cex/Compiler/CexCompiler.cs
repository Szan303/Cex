using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cex.AST;
using CexLexer = Cex.Lexer.Lexer;
using CexParser = Cex.Parser.Parser;

namespace Cex.Compiler;

public class CexCompiler
{
    private readonly string      _projectRoot;
    private readonly SymbolTable _symbols = new();

    public CexCompiler(string projectRoot)
    {
        _projectRoot = projectRoot;
    }

    // ================================================================= public
    public string Compile()
    {
        var sourceFiles = Directory
            .GetFiles(_projectRoot, "*.ce", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();

        if (sourceFiles.Count == 0)
            throw new Exception($"No .ce source files found in {_projectRoot}");

        Console.WriteLine($"Found {sourceFiles.Count} source file(s):");
        foreach (var f in sourceFiles)
            Console.WriteLine($"  {Path.GetRelativePath(_projectRoot, f)}");

        var units = new List<CompilationUnit>();
        foreach (var file in sourceFiles)
        {
            Console.WriteLine($"Parsing {Path.GetFileName(file)}...");
            units.Add(ParseFile(file));
        }

        foreach (var unit in units)
            RegisterSymbols(unit);

        foreach (var unit in units)
            ValidateCalls(unit.Expressions, unit.SourceFile);

        FindEntryPoint();

        var generator = new CodeGenerator(_symbols);
        return generator.Generate(units);
    }

    // ================================================================= private
    private CompilationUnit ParseFile(string filePath)
    {
        string source = File.ReadAllText(filePath);
        var tokens    = new CexLexer(source).Tokenize();
        var exprs     = new CexParser(tokens).Parse();

        var unit = new CompilationUnit
        {
            SourceFile  = filePath,
            Expressions = exprs
        };

        foreach (var e in exprs)
            if (e is ImportStatement imp)
                unit.Imports.Add(imp.Module);

        return unit;
    }

    private void RegisterSymbols(CompilationUnit unit)
    {
        foreach (var e in unit.Expressions)
        {
            switch (e)
            {
                case FunctionDeclaration fn:
                    _symbols.RegisterFunction(fn, unit.SourceFile);
                    break;

                case ClassDeclaration cls:
                    _symbols.RegisterClass(cls, unit.SourceFile);
                    RegisterClassMembers(cls, unit.SourceFile);
                    break;

                case VariableDeclaration v:
                    _symbols.RegisterGlobal(v.Name, v.Type, unit.SourceFile);
                    break;
            }
        }
    }

    // Register all methods inside a class body as callable functions.
    // A method called 'Main' inside any class becomes the program entry point.
    private void RegisterClassMembers(ClassDeclaration cls, string sourceFile)
    {
        foreach (var member in cls.Body)
        {
            if (member is not FunctionDeclaration method) continue;

            // 'Main' inside any class is THE entry point — register as plain "Main"
            bool isEntryPoint = method.Name == "Main";

            string registeredName = isEntryPoint
                ? "Main"
                : $"{cls.Name}.{method.Name}";

            var fn = new FunctionDeclaration
            {
                Access     = method.Access,
                ReturnType = method.ReturnType,
                Name       = registeredName,
                Parameters = method.Parameters,
                Body       = method.Body
            };

            // avoid duplicate registration if already registered
            if (!_symbols.FunctionExists(registeredName))
                _symbols.RegisterFunction(fn, sourceFile);
        }
    }

    private void ValidateCalls(List<Expression> exprs, string sourceFile)
    {
        foreach (var e in exprs)
        {
            switch (e)
            {
                case FunctionCall fc:
                    if (!IsStdLib(fc.Name) && !_symbols.FunctionExists(fc.Name))
                        throw new Exception(
                            $"[{Path.GetFileName(sourceFile)}] " +
                            $"Call to undefined function '{fc.Name}'");
                    break;

                case IfStatement i:
                    ValidateCalls(i.ThenBranch, sourceFile);
                    foreach (var ei in i.ElseIfs)
                        ValidateCalls(ei.Body, sourceFile);
                    ValidateCalls(i.ElseBranch, sourceFile);
                    break;

                case WhileStatement w:
                    ValidateCalls(w.Body, sourceFile);
                    break;

                case ForStatement f:
                    ValidateCalls(f.Body, sourceFile);
                    break;

                case FunctionDeclaration fn:
                    ValidateCalls(fn.Body, sourceFile);
                    break;

                case ClassDeclaration cls:
                    ValidateCalls(cls.Body, sourceFile);
                    break;

                case TryStatement t:
                    ValidateCalls(t.TryBody,     sourceFile);
                    ValidateCalls(t.CatchBody,   sourceFile);
                    ValidateCalls(t.FinallyBody, sourceFile);
                    break;
            }
        }
    }

    private void FindEntryPoint()
    {
        if (!_symbols.FunctionExists("Main"))
            throw new Exception(
                "No entry point found. " +
                "Define 'void Main():' as a top-level function " +
                "or inside any class in one of your .ce files.");
    }

    private static bool IsStdLib(string name) => name is
        "math_abs" or "math_max" or "math_min" or
        "str_len"  or "str_upper" or "str_lower";
}