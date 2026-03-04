using System.Collections.Generic;
using Cex.AST;

namespace Cex.Compiler;

/// <summary>
/// Holds all known symbols (functions, classes, global variables)
/// across every .ce file in the project.
/// </summary>
public class SymbolTable
{
    // function name → declaration
    public Dictionary<string, FunctionDeclaration> Functions { get; } = new();

    // class name → declaration
    public Dictionary<string, ClassDeclaration> Classes { get; } = new();

    // global variable name → type
    public Dictionary<string, string> GlobalVars { get; } = new();

    // which file each symbol came from (for error messages)
    public Dictionary<string, string> SymbolOrigin { get; } = new();

    public void RegisterFunction(FunctionDeclaration fn, string sourceFile)
    {
        if (Functions.ContainsKey(fn.Name))
            throw new System.Exception(
                $"Function '{fn.Name}' already defined in {SymbolOrigin[fn.Name]}. " +
                $"Redefinition attempted in {sourceFile}.");

        Functions[fn.Name]      = fn;
        SymbolOrigin[fn.Name]   = sourceFile;
    }

    public void RegisterClass(ClassDeclaration cls, string sourceFile)
    {
        if (Classes.ContainsKey(cls.Name))
            throw new System.Exception(
                $"Class '{cls.Name}' already defined in {SymbolOrigin[cls.Name]}. " +
                $"Redefinition attempted in {sourceFile}.");

        Classes[cls.Name]      = cls;
        SymbolOrigin[cls.Name] = sourceFile;
    }

    public void RegisterGlobal(string name, string type, string sourceFile)
    {
        if (GlobalVars.ContainsKey(name))
            throw new System.Exception(
                $"Global variable '{name}' already defined in {SymbolOrigin[name]}. " +
                $"Redefinition attempted in {sourceFile}.");

        GlobalVars[name]      = type;
        SymbolOrigin[name]    = sourceFile;
    }

    public bool FunctionExists(string name)  => Functions.ContainsKey(name);
    public bool ClassExists(string name)     => Classes.ContainsKey(name);
    public bool GlobalExists(string name)    => GlobalVars.ContainsKey(name);
}