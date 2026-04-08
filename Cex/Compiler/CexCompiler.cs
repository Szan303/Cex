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
        // ── find main.ce first, then everything else ─────────────────────���
        string? mainFile = Directory
            .GetFiles(_projectRoot, "main.ce", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (mainFile == null)
            throw new Exception($"No main.ce found in {_projectRoot}");

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var units   = new List<CompilationUnit>();

        // load main + all imports recursively
        LoadFile(mainFile, units, visited);

        Console.WriteLine($"Found {units.Count} source file(s):");
        foreach (var u in units)
            Console.WriteLine($"  {Path.GetRelativePath(_projectRoot, u.SourceFile)}");

        // register symbols
        foreach (var unit in units)
            RegisterSymbols(unit);

        // NEW: compute class metadata (fields/offsets/static fields/ctors)
        _symbols.BuildClassInfo();

        // validate calls
        foreach (var unit in units)
            ValidateCalls(unit.Expressions, unit.SourceFile, callerFunction: null);

        FindEntryPoint();

        var generator = new CodeGenerator(_symbols);
        return generator.Generate(units);
    }

    // ================================================================= file loading

    private void LoadFile(string filePath, List<CompilationUnit> units, HashSet<string> visited)
    {
        string fullPath = Path.GetFullPath(filePath);
        if (visited.Contains(fullPath)) return;
        visited.Add(fullPath);

        if (!File.Exists(fullPath))
        {
            Console.WriteLine($"  WARNING: file not found: {fullPath}");
            return;
        }

        Console.WriteLine($"Parsing {Path.GetFileName(fullPath)}...");

        string source = File.ReadAllText(fullPath);
        var tokens    = new CexLexer(source).Tokenize();
        var exprs     = new CexParser(tokens).Parse();

        var unit = new CompilationUnit
        {
            SourceFile  = fullPath,
            Expressions = exprs
        };

        // resolve imports before adding this unit so dependencies come first
        foreach (var e in exprs)
        {
            if (e is not ImportStatement imp) continue;
            if (imp.Module == "System") continue;

            string dir = Path.GetDirectoryName(fullPath)!;

            // search order: same dir → ./libs/ → exe/libs/
            string[] candidates =
            {
                Path.Combine(dir,                              imp.Module + ".ce"),
                Path.Combine(dir,                    "libs",   imp.Module + ".ce"),
                Path.Combine(_projectRoot,                     imp.Module + ".ce"),
                Path.Combine(_projectRoot,           "libs",   imp.Module + ".ce"),
                Path.Combine(AppContext.BaseDirectory,         imp.Module + ".ce"),
                Path.Combine(AppContext.BaseDirectory, "libs", imp.Module + ".ce"),
            };

            bool found = false;
            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                LoadFile(candidate, units, visited);
                unit.Imports.Add(imp.Module);
                found = true;
                break;
            }

            if (!found)
                Console.WriteLine($"  WARNING: import '{imp.Module}' not found");
        }

        units.Add(unit);
    }

    // ================================================================= symbol registration

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

            // Main inside any class = entry point
            if (method.Name == "Main")
            {
                if (_symbols.FunctionExists("Main")) continue;
                var main = new FunctionDeclaration
                {
                    Access     = method.Access,
                    ReturnType = method.ReturnType,
                    Name       = "Main",
                    Parameters = method.Parameters,
                    Body       = method.Body
                };
                _symbols.RegisterFunction(main, sourceFile, ownerClass: cls.Name);
                continue;
            }

            // for lib classes like Math → register as "Math.sqrt"
            // for program classes like Program → register as plain "Factorial"
            // so both styles work
            string plainName = method.Name;
            string dotName   = cls.Name + "." + method.Name;

            // register plain name (always)
            if (!_symbols.FunctionExists(plainName))
            {
                var fn = new FunctionDeclaration
                {
                    Access     = method.Access,
                    ReturnType = method.ReturnType,
                    Name       = plainName,
                    Parameters = method.Parameters,
                    Body       = method.Body
                };
                _symbols.RegisterFunction(fn, sourceFile, ownerClass: cls.Name);
            }

            // register dot name (for Math.sqrt style calls)
            if (!_symbols.FunctionExists(dotName))
            {
                var fn = new FunctionDeclaration
                {
                    Access     = method.Access,
                    ReturnType = method.ReturnType,
                    Name       = dotName,
                    Parameters = method.Parameters,
                    Body       = method.Body
                };
                _symbols.RegisterFunction(fn, sourceFile, ownerClass: cls.Name);
            }
        }
    }

    // ================================================================= validation

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

                case ExpressionStatement es:
                    ValidateExpr(es.Expr, sourceFile, callerFunction);
                    break;
            }
        }
    }

    private void ValidateExpr(Expression expr, string sourceFile, string? callerFunction)
    {
        switch (expr)
        {
            case CallExpr c:
                CheckCall(c.Name, callerFunction, sourceFile);
                foreach (var arg in c.Args)
                    ValidateExpr(arg, sourceFile, callerFunction);
                break;

            case CreateExpr ce:
                foreach (var arg in ce.Args)
                    ValidateExpr(arg, sourceFile, callerFunction);
                break;

            case MemberAccessExpr ma:
                ValidateExpr(ma.Target, sourceFile, callerFunction);
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

            case NumberLiteral:
            case StringLiteralExpr:
            case BoolLiteral:
            case CharLiteral:
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
        if (IsBuiltIn(targetName)) return;

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

    // built-in functions that live in the compiler, not in .ce lib files
    private static bool IsBuiltIn(string name) => name is
        "exit"   or
        "length";
}