using System;
using System.Collections.Generic;
using Cex.AST;

namespace Cex;

public partial class CodeGenerator
{
    private void LoadVar(string name, string reg, int line = 0)
    {
        if (_currentClassName != null && _symbols.ClassInfoByName.TryGetValue(_currentClassName, out var cls))
        {
            // instance field?
            if (_currentThisReg != null && cls.InstanceFields.TryGetValue(name, out var inst))
            {
                _text.AppendLine($"    mov  {reg}, [{_currentThisReg}+{inst.offset}]");
                return;
            }

            // static field?
            if (cls.StaticFields.ContainsKey(name))
            {
                string label = $"{_currentClassName}_{name}";
                _text.AppendLine($"    mov  {reg}, [rel {label}]");
                return;
            }
        }
        var local = _scope.Lookup(name);
        if (local.HasValue)
        {
            _text.AppendLine($"    mov  {reg}, [rbp{local.Value.offset}]");
            return;
        }

        if (_globalVarType.ContainsKey(name))
        {
            _text.AppendLine($"    mov  {reg}, [rel {name}]");
            return;
        }

        throw new Cex.CompilerError(Cex.ErrorKind.Codegen, line == 0 ? 1 : line, $"Undefined variable '{name}'");
    }

    private void StoreVar(string name, string reg, int line = 0)
    {
        if (_currentClassName != null && _symbols.ClassInfoByName.TryGetValue(_currentClassName, out var cls))
        {
            if (_currentThisReg != null && cls.InstanceFields.TryGetValue(name, out var inst))
            {
                _text.AppendLine($"    mov  [{_currentThisReg}+{inst.offset}], {reg}");
                return;
            }

            if (cls.StaticFields.ContainsKey(name))
            {
                string label = $"{_currentClassName}_{name}";
                _text.AppendLine($"    mov  [rel {label}], {reg}");
                return;
            }
        }
        var local = _scope.Lookup(name);
        if (local.HasValue)
        {
            _text.AppendLine($"    mov  [rbp{local.Value.offset}], {reg}");
            return;
        }

        if (_globalVarType.ContainsKey(name))
        {
            _text.AppendLine($"    mov  [rel {name}], {reg}");
            return;
        }

        throw new Cex.CompilerError(Cex.ErrorKind.Codegen, line == 0 ? 1 : line, $"Undefined variable '{name}'");
    }

    private string GetVarType(string name, int line = 0)
    {
        if (_currentClassName != null && _symbols.ClassInfoByName.TryGetValue(_currentClassName, out var cls))
        {
            if (cls.InstanceFields.TryGetValue(name, out var inst))
                return inst.type;

            if (cls.StaticFields.TryGetValue(name, out var stType))
                return stType;
        }
        var local = _scope.Lookup(name);
        if (local.HasValue) return local.Value.type;

        if (_globalVarType.TryGetValue(name, out var gt)) return gt;

        throw new Cex.CompilerError(Cex.ErrorKind.Codegen, line == 0 ? 1 : line, $"Undefined variable '{name}'");
    }

    private void ReserveGlobals(List<Expression> exprs)
    {
        foreach (var e in exprs)
        {
            switch (e)
            {
                case VariableDeclaration v:
                    _bss.AppendLine($"{v.Name}: resq 1");
                    _globalVarType[v.Name] = v.Type;
                    break;
                case ArrayDeclaration a:
                    _bss.AppendLine($"{a.Name}: resq 1");
                    _globalVarType[a.Name] = a.ElementType + "[]";
                    break;
                case ClassDeclaration cls:
                    foreach (var member in cls.Body)
                    {
                        if (member is FieldDeclaration fd && fd.IsStatic)
                        {
                            string label = $"{cls.Name}_{fd.Name}";   // Foo_nextId
                            _bss.AppendLine($"{label}: resq 1");
                            _globalVarType[label] = fd.Type;
                        }
                    }
                    break;
            }
        }
    }

    private int CountLocals(List<Expression> exprs)
    {
        int count = 0;
        foreach (var e in exprs)
        {
            switch (e)
            {
                case VariableDeclaration: count++; break;
                case ArrayDeclaration:    count++; break;
                case ForStatement f:
                    count += 2;
                    count += CountLocals(f.Body);
                    break;
                case IfStatement i:
                    int thenC = CountLocals(i.ThenBranch);
                    int elseC = CountLocals(i.ElseBranch);
                    foreach (var ei in i.ElseIfs) elseC = Math.Max(elseC, CountLocals(ei.Body));
                    count += Math.Max(thenC, elseC);
                    break;
                case WhileStatement w: count += CountLocals(w.Body); break;
                case TryStatement t:
                    count += Math.Max(CountLocals(t.TryBody), CountLocals(t.CatchBody));
                    count += CountLocals(t.FinallyBody);
                    break;
            }
        }
        return count;
    }
}