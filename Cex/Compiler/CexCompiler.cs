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
            ValidateCalls(unit.Expressions, unit.SourceFile, callerFunction: null);

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
                    _symbols.RegisterFunction(fn, unit.SourceFile, ownerClass: null);
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

    private void RegisterClassMembers(ClassDeclaration cls, string sourceFile)
    {
        foreach (var member in cls.Body)
        {
            if (member is not FunctionDeclaration method) continue;

            // Main inside any class = entry point — register as plain "Main"
            string registeredName = method.Name == "Main" ? "Main" : method.Name;

            if (_symbols.FunctionExists(registeredName)) continue;

            var fn = new FunctionDeclaration
            {
                Access     = method.Access,
                ReturnType = method.ReturnType,
                Name       = registeredName,
                Parameters = method.Parameters,
                Body       = method.Body
            };

            _symbols.RegisterFunction(fn, sourceFile, ownerClass: cls.Name);
        }
    }

    // callerFunction = the function we are currently validating inside (for access checks)
    private void ValidateCalls(List<Expression> exprs, string sourceFile, string? callerFunction)
    {
        foreach (var e in exprs)
        {
            switch (e)
            {
                case FunctionCall fc:
                    CheckCall(fc.Name, callerFunction, sourceFile);
                    ValidateExprList(fc.Arguments, sourceFile, callerFunction);
                    break;

                // new AST: VariableDeclaration.Init may contain a CallExpr
                case VariableDeclaration v:
                    if (v.Init != null)
                        ValidateExpr(v.Init, sourceFile, callerFunction);
                    break;

                case AssignmentStatement a:
                    if (a.Value != null)
                        ValidateExpr(a.Value, sourceFile, callerFunction);
                    break;

                case ReturnStatement r:
                    if (r.Value != null)
                        ValidateExpr(r.Value, sourceFile, callerFunction);
                    break;

                case PrintStatement p:
                    if (p.Segments != null)
                        foreach (var seg in p.Segments)
                            if (seg.Expr != null)
                                ValidateExpr(seg.Expr, sourceFile, callerFunction);
                    break;

                case IfStatement i:
                    ValidateCalls(i.ThenBranch, sourceFile, callerFunction);
                    foreach (var ei in i.ElseIfs)
                        ValidateCalls(ei.Body, sourceFile, callerFunction);
                    ValidateCalls(i.ElseBranch, sourceFile, callerFunction);
                    break;

                case WhileStatement w:
                    ValidateCalls(w.Body, sourceFile, callerFunction);
                    break;

                case ForStatement f:
                    ValidateCalls(f.Body, sourceFile, callerFunction);
                    break;

                case FunctionDeclaration fn:
                    ValidateCalls(fn.Body, sourceFile, callerFunction: fn.Name);
                    break;

                case ClassDeclaration cls:
                    ValidateCalls(cls.Body, sourceFile, callerFunction);
                    break;

                case TryStatement t:
                    ValidateCalls(t.TryBody,     sourceFile, callerFunction);
                    ValidateCalls(t.CatchBody,   sourceFile, callerFunction);
                    ValidateCalls(t.FinallyBody, sourceFile, callerFunction);
                    break;
            }
        }
    }

    // recursively validate all function calls inside an expression
    private void ValidateExpr(Expression expr, string sourceFile, string? callerFunction)
    {
        switch (expr)
        {
            case CallExpr c:
                CheckCall(c.Name, callerFunction, sourceFile);
                foreach (var arg in c.Args)
                    ValidateExpr(arg, sourceFile, callerFunction);
                break;

            case BinaryExpr b:
                ValidateExpr(b.Left,  sourceFile, callerFunction);
                ValidateExpr(b.Right, sourceFile, callerFunction);
                break;

            case UnaryExpr u:
                ValidateExpr(u.Operand, sourceFile, callerFunction);
                break;

            case ArrayAccess a:
                ValidateExpr(a.Index, sourceFile, callerFunction);
                break;

            // literals and variable references need no validation
            case NumberLiteral:
            case StringLiteralExpr:
            case VariableExpr:
                break;
        }
    }

    private void ValidateExprList(List<Expression> exprs, string sourceFile, string? callerFunction)
    {
        foreach (var e in exprs)
            ValidateExpr(e, sourceFile, callerFunction);
    }

    private void CheckCall(string targetName, string? callerFunction, string sourceFile)
    {
        if (IsStdLib(targetName)) return;

        if (!_symbols.FunctionExists(targetName))
            throw new Exception(
                $"[{Path.GetFileName(sourceFile)}] " +
                $"Call to undefined function '{targetName}'");

        if (callerFunction != null && !_symbols.CanCall(callerFunction, targetName))
        {
            string  access     = _symbols.FunctionAccess[targetName];
            string? ownerClass = _symbols.FunctionClass[targetName];
            throw new Exception(
                $"[{Path.GetFileName(sourceFile)}] " +
                $"Cannot call {access} function '{targetName}' " +
                $"(defined in class '{ownerClass}') from '{callerFunction}'");
        }
    }

    private void FindEntryPoint()
    {
        if (!_symbols.FunctionExists("Main"))
            throw new Exception(
                "No entry point found. " +
                "Define 'void Main():' as a top-level function or inside any class.");
    }

    private static bool IsStdLib(string name) => name is
        "exit"      or
        "str"       or
        "math_abs"  or "math_max" or "math_min" or
        "str_len"   or "str_upper" or "str_lower";
}