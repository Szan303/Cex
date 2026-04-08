using System;
using System.Collections.Generic;
using System.Linq;
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

    // NEW: computed class metadata for codegen
    public Dictionary<string, ClassInfo> ClassInfoByName { get; } = new();

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
    /// Build field offsets / ctor info for all registered classes.
    /// Call this after RegisterSymbols(...) finished.
    /// </summary>
    public void BuildClassInfo()
    {
        ClassInfoByName.Clear();

        foreach (var cls in Classes.Values)
        {
            var info = new ClassInfo { Name = cls.Name };

            int offset = 0;

            foreach (var member in cls.Body)
            {
                if (member is FieldDeclaration fd)
                {
                    if (fd.IsStatic)
                    {
                        info.StaticFields[fd.Name] = fd.Type;
                    }
                    else
                    {
                        // simple layout: every field is 8 bytes for now
                        info.InstanceFields[fd.Name] = (fd.Type, offset);
                        offset += 8;
                    }
                }
                else if (member is ConstructorDeclaration cd)
                {
                    // one ctor for now
                    info.Constructor = cd;
                }
            }

            info.InstanceSizeBytes = offset == 0 ? 8 : offset; // allocate at least 8 bytes (optional)
            ClassInfoByName[cls.Name] = info;
        }
    }

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

        if (access == "public") return true;

        string? targetClass = FunctionClass.GetValueOrDefault(targetName);
        string? callerClass = FunctionClass.GetValueOrDefault(callerName);

        if (targetClass != null && callerClass != null && targetClass == callerClass)
            return true;

        if (targetClass != null && callerClass == null)
            return access == "public";

        return access != "private";
    }
}