using System;
using System.Collections.Generic;
using Cex.AST;

namespace Cex.Compiler;

public class SymbolTable
{
    public Dictionary<string, FunctionDeclaration> Functions     { get; } = new();
    public Dictionary<string, ClassDeclaration>    Classes       { get; } = new();
    public Dictionary<string, string>              GlobalVars    { get; } = new();
    public Dictionary<string, string>              SymbolOrigin  { get; } = new();

    // functionName → class it belongs to (null if top-level)
    public Dictionary<string, string?>             FunctionClass { get; } = new();

    // functionName → access modifier
    public Dictionary<string, string>              FunctionAccess { get; } = new();

    public void RegisterFunction(FunctionDeclaration fn, string sourceFile, string? ownerClass = null)
    {
        if (Functions.ContainsKey(fn.Name))
            throw new Exception(
                $"Function '{fn.Name}' already defined in {SymbolOrigin[fn.Name]}. " +
                $"Redefinition in {sourceFile}.");

        Functions[fn.Name]      = fn;
        SymbolOrigin[fn.Name]   = sourceFile;
        FunctionClass[fn.Name]  = ownerClass;
        FunctionAccess[fn.Name] = fn.Access ?? "public";
    }

    public void RegisterClass(ClassDeclaration cls, string sourceFile)
    {
        if (Classes.ContainsKey(cls.Name))
            throw new Exception(
                $"Class '{cls.Name}' already defined in {SymbolOrigin[cls.Name]}. " +
                $"Redefinition in {sourceFile}.");

        Classes[cls.Name]      = cls;
        SymbolOrigin[cls.Name] = sourceFile;
    }

    public void RegisterGlobal(string name, string type, string sourceFile)
    {
        if (GlobalVars.ContainsKey(name)) return;
        GlobalVars[name]     = type;
        SymbolOrigin[name]   = sourceFile;
    }

    public bool FunctionExists(string name) => Functions.ContainsKey(name);
    public bool ClassExists(string name)    => Classes.ContainsKey(name);
    public bool GlobalExists(string name)   => GlobalVars.ContainsKey(name);

    /// <summary>
    /// Check if callerFunction can call targetFunction.
    /// private   → only callable from same class
    /// protected → only callable from same class or subclass
    /// public    → callable from anywhere
    /// </summary>
    public bool CanCall(string callerName, string targetName)
    {
        if (!FunctionAccess.ContainsKey(targetName)) return true;

        string access = FunctionAccess[targetName];

        // public is always callable
        if (access == "public") return true;

        // get the class that owns the target
        string? targetClass = FunctionClass.GetValueOrDefault(targetName);

        // get the class that owns the caller
        string? callerClass = FunctionClass.GetValueOrDefault(callerName);

        // private/protected: only callable from same class
        if (targetClass != null && callerClass != null && targetClass == callerClass)
            return true;

        // static methods in same class can call each other
        if (targetClass != null && callerClass == null)
        {
            // top-level caller trying to call class method — check if public
            return access == "public";
        }

        return access != "private";
    }
}