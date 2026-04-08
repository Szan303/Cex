using System;
using Cex.AST;
using Cex.Compiler;

namespace Cex;

public partial class CodeGenerator
{
    private void EmitCreateExpr(CreateExpr c)
    {
        if (!_symbols.ClassInfoByName.TryGetValue(c.ClassName, out var cls))
            throw new Exception($"Unknown class '{c.ClassName}' at line {c.Line}");

        // Allocate instance
        _text.AppendLine($"    mov  rcx, {cls.InstanceSizeBytes}");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    call __heap_alloc");
        _text.AppendLine("    add  rsp, 40");
        // rax = this

        // If class has a ctor, call it
        if (cls.Constructor != null)
        {
            // IMPORTANT:
            // Preserve the allocated 'this' pointer before evaluating args,
            // because EmitExpr(args[i]) will overwrite rax.
            _text.AppendLine("    push rax"); // save this at bottom of stack

            // push args (right-to-left) like normal calls
            for (int i = c.Args.Count - 1; i >= 0; i--)
            {
                EmitExpr(c.Args[i]);          // result in rax
                _text.AppendLine("    push rax");
            }

            // pop args into rdx, r8, r9 (in left-to-right order)
            string[] regs = { "rdx", "r8", "r9" };
            for (int i = 0; i < c.Args.Count && i < regs.Length; i++)
                _text.AppendLine($"    pop  {regs[i]}");

            // restore 'this' into rcx
            _text.AppendLine("    pop  rcx");

            _text.AppendLine("    sub  rsp, 40");
            _text.AppendLine($"    call __ctor_{c.ClassName}");
            _text.AppendLine("    add  rsp, 40");

            // expression result = this
            _text.AppendLine("    mov  rax, rcx");
        }

        // If no ctor: expression result is already in rax (the allocated pointer)
    }

    private void EmitConstructor(ClassInfo cls)
    {
        if (cls.Constructor == null) return;

        var ctor = cls.Constructor;

        _text = _helpers;

        _text.AppendLine($"; ---- ctor {cls.Name} ----");
        _text.AppendLine($"__ctor_{cls.Name}:");
        _text.AppendLine("    push rbp");
        _text.AppendLine("    mov  rbp, rsp");

        _scope.EnterFunction();

        // pin this pointer (rcx) in r15 for the whole ctor
        _text.AppendLine("    mov  r15, rcx");

        int localCount = Math.Max(ctor.Parameters.Count + CountLocals(ctor.Body) + 4, 4);
        int frameSize  = (localCount * 8 + 15) & ~15;
        _text.AppendLine($"    sub  rsp, {frameSize}");

        // Declare ctor params as locals (so ctor body can use them by name)
        // and remember their stack offsets for auto-assign (avoid field shadowing).
        var paramOffsets = new Dictionary<string, int>(StringComparer.Ordinal);

        string[] paramRegs = { "rdx", "r8", "r9" }; // after rcx=this
        for (int i = 0; i < ctor.Parameters.Count && i < 3; i++)
        {
            int off = _scope.Declare(ctor.Parameters[i].Name, ctor.Parameters[i].Type);
            paramOffsets[ctor.Parameters[i].Name] = off;
            _text.AppendLine($"    mov  [rbp{off}], {paramRegs[i]}");
        }

        // Enable field/static resolution in LoadVar/StoreVar/GetVarType
        _currentClassName = cls.Name;
        _currentThisReg   = "r15";

        // Emit ctor body (nextId++, id=nextId, etc)
        foreach (var e in ctor.Body)
            Emit(e);

        // Auto-assign params to same-named instance fields (Option C)
        // IMPORTANT: load params from their local stack slots, not via LoadVar(name),
        // because inside ctor context "name" may resolve to a FIELD, not the PARAM.
        foreach (var p in ctor.Parameters)
        {
            if (!cls.InstanceFields.TryGetValue(p.Name, out var field))
                continue;

            if (!paramOffsets.TryGetValue(p.Name, out var off))
                continue;

            _text.AppendLine($"    mov  rax, [rbp{off}]");
            _text.AppendLine($"    mov  [r15+{field.offset}], rax");
        }

        // Disable ctor context
        _currentClassName = null;
        _currentThisReg   = null;

        _text.AppendLine($"    add  rsp, {frameSize}");
        _text.AppendLine("    pop  rbp");
        _text.AppendLine("    ret");
        _text.AppendLine();

        _scope.ExitFunction();
        _text = _main;
    }
}