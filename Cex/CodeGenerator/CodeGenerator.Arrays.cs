using System;
using Cex.AST;

namespace Cex;

public partial class CodeGenerator
{
    private bool IsArrayType(string type) => type.EndsWith("[]");

    private string GetArrayElemType(string arrType)
    {
        if (!arrType.EndsWith("[]")) throw new Exception($"Not an array type: {arrType}");
        return arrType.Substring(0, arrType.Length - 2);
    }

    private bool IsArrayArg(Expression e) =>
        e is VariableExpr ve && GetVarType(ve.Name).EndsWith("[]");

    private void EmitPrintArrayVar(string varName, bool addNewline)
    {
        int id = _labelCount++;

        LoadVar(varName, "rbx");

        _text.AppendLine("    test rbx, rbx");
        _text.AppendLine($"    jnz  __arr_notnull_{id}");
        EmitWriteConsole(GetOrAddString("null", newline: addNewline), addNewline ? 5 : 4);

        _text.AppendLine($"__arr_notnull_{id}:");
        EmitWriteConsole(GetOrAddString("[", newline: false), 1);

        _text.AppendLine("    mov  r12, [rbx]");   // len
        _text.AppendLine("    xor  rdi, rdi");      // i=0

        string arrType   = GetVarType(varName);
        string elemType  = GetArrayElemType(arrType);
        bool elemIsString = (elemType == "string");

        _text.AppendLine($"__arr_loop_{id}:");
        _text.AppendLine("    cmp  rdi, r12");
        _text.AppendLine($"    jge  __arr_end_{id}");

        _text.AppendLine("    test rdi, rdi");
        _text.AppendLine($"    jz   __arr_nocomma_{id}");
        EmitWriteConsole(GetOrAddString(",", newline: false), 1);
        _text.AppendLine($"__arr_nocomma_{id}:");

        _text.AppendLine("    mov  rax, [rbx + 16 + rdi*8]");

        if (elemIsString)
        {
            _text.AppendLine("    mov  rdx, rax");
            EmitWriteConsoleRdx();
        }
        else
        {
            EmitWriteInt(addNewline: false);
        }

        _text.AppendLine("    inc  rdi");
        _text.AppendLine($"    jmp  __arr_loop_{id}");

        _text.AppendLine($"__arr_end_{id}:");
        EmitWriteConsole(GetOrAddString("]", newline: addNewline), addNewline ? 2 : 1);
    }

    private void EmitArrayAdd(ArrayAddStatement s)
    {
        int id = _labelCount++;

        LoadVar(s.Name, "rbx");
        EmitNullTrapIfZero("rbx");

        _text.AppendLine("    mov  rdi, [rbx]");    // len
        _text.AppendLine("    mov  rsi, [rbx+8]");  // cap

        _text.AppendLine("    cmp  rdi, rsi");
        _text.AppendLine($"    jl   __add_nogrow_{id}");

        _text.AppendLine("    mov  rax, rsi");
        _text.AppendLine("    test rax, rax");
        _text.AppendLine($"    jnz  __add_cap_nonzero_{id}");
        _text.AppendLine("    mov  rax, 4");
        _text.AppendLine($"    jmp  __add_newcap_ready_{id}");
        _text.AppendLine($"__add_cap_nonzero_{id}:");
        _text.AppendLine("    shl  rax, 1");
        _text.AppendLine($"__add_newcap_ready_{id}:");

        _text.AppendLine("    push rbx");   // oldPtr
        _text.AppendLine("    push rdi");   // len
        _text.AppendLine("    push rax");   // newCap

        _text.AppendLine("    imul rax, 8");
        _text.AppendLine("    add  rax, 16");
        _text.AppendLine("    mov  rcx, rax");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    call __heap_alloc");
        _text.AppendLine("    add  rsp, 40");

        _text.AppendLine("    pop  rsi");   // newCap
        _text.AppendLine("    pop  rdi");   // len
        _text.AppendLine("    pop  rbx");   // oldPtr

        _text.AppendLine("    mov  [rax], rdi");
        _text.AppendLine("    mov  [rax+8], rsi");

        _text.AppendLine("    xor  r8, r8");
        _text.AppendLine($"__add_copy_{id}:");
        _text.AppendLine("    cmp  r8, rdi");
        _text.AppendLine($"    jge  __add_copy_done_{id}");
        _text.AppendLine("    mov  r9, [rbx + 16 + r8*8]");
        _text.AppendLine("    mov  [rax + 16 + r8*8], r9");
        _text.AppendLine("    inc  r8");
        _text.AppendLine($"    jmp  __add_copy_{id}");
        _text.AppendLine($"__add_copy_done_{id}:");

        StoreVar(s.Name, "rax");

        _text.AppendLine("    mov  rbx, rax");
        _text.AppendLine("    mov  rdi, [rbx]");
        _text.AppendLine("    mov  rsi, [rbx+8]");

        _text.AppendLine($"__add_nogrow_{id}:");

        _text.AppendLine("    lea  r8, [rbx + 16 + rdi*8]");
        EmitExpr(s.Value);
        _text.AppendLine("    mov  [r8], rax");

        _text.AppendLine("    inc  qword [rbx]");
    }

    private void EmitArrayDelete(ArrayDeleteStatement s)
    {
        int id = _labelCount++;

        LoadVar(s.Name, "rbx");
        EmitNullTrapIfZero("rbx");

        EmitExpr(s.Index); // rax=index

        _text.AppendLine("    test rax, rax");
        _text.AppendLine($"    js   __del_oob_{id}");

        // NOTE: rcx is fine here because we do no console calls in this loop.
        _text.AppendLine("    mov  rcx, [rbx]");   // len
        _text.AppendLine("    cmp  rax, rcx");
        _text.AppendLine($"    jge  __del_oob_{id}");

        _text.AppendLine("    mov  rdi, rax"); // i=index
        _text.AppendLine("    dec  rcx");      // lastValidIndex=len-1

        _text.AppendLine($"__del_shift_{id}:");
        _text.AppendLine("    cmp  rdi, rcx");
        _text.AppendLine($"    jge  __del_shift_done_{id}");

        _text.AppendLine("    mov  r8,  [rbx + 16 + (rdi+1)*8]");
        _text.AppendLine("    mov  [rbx + 16 + rdi*8], r8");
        _text.AppendLine("    inc  rdi");
        _text.AppendLine($"    jmp  __del_shift_{id}");

        _text.AppendLine($"__del_shift_done_{id}:");
        _text.AppendLine("    dec  qword [rbx]");

        _text.AppendLine($"    jmp  __del_ok_{id}");
        _text.AppendLine($"__del_oob_{id}:");
        EmitTrapExit1();
        _text.AppendLine($"__del_ok_{id}:");
    }

    private void EmitArrayDecl(ArrayDeclaration a)
    {
        EmitExpr(a.Size);

        int id = _labelCount++;
        _text.AppendLine("    test rax, rax");
        _text.AppendLine($"    jns  __cap_ok_{id}");
        EmitTrapExit1();
        _text.AppendLine($"__cap_ok_{id}:");

        _text.AppendLine("    push rax");
        _text.AppendLine("    imul rax, 8");
        _text.AppendLine("    add  rax, 16");
        _text.AppendLine("    mov  rcx, rax");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    call __heap_alloc");
        _text.AppendLine("    add  rsp, 40");
        _text.AppendLine("    pop  rcx");

        _text.AppendLine("    mov  qword [rax], 0");
        _text.AppendLine("    mov  [rax+8], rcx");

        if (!_globalVarType.ContainsKey(a.Name) && !_scope.Lookup(a.Name).HasValue)
            _scope.Declare(a.Name, a.ElementType + "[]");

        StoreVar(a.Name, "rax");
        if (_globalVarType.ContainsKey(a.Name))
            _globalVarType[a.Name] = a.ElementType + "[]";
    }

    private void EmitArrayLoad(ArrayAccess a)
    {
        int id = _labelCount++;

        LoadVar(a.Name, "rbx");
        EmitNullTrapIfZero("rbx");

        EmitExpr(a.Index);

        _text.AppendLine("    test rax, rax");
        _text.AppendLine($"    js   __oob_{id}");

        _text.AppendLine("    mov  rcx, [rbx]");
        _text.AppendLine("    cmp  rax, rcx");
        _text.AppendLine($"    jge  __oob_{id}");

        _text.AppendLine("    mov  rax, [rbx + 16 + rax*8]");
        _text.AppendLine($"    jmp  __arr_ok_{id}");

        _text.AppendLine($"__oob_{id}:");
        EmitTrapExit1();
        _text.AppendLine($"__arr_ok_{id}:");
    }

    private void EmitArrayAssign(ArrayAssignment a)
    {
        int id = _labelCount++;

        LoadVar(a.Name, "rbx");
        EmitNullTrapIfZero("rbx");

        EmitExpr(a.Index);

        _text.AppendLine("    test rax, rax");
        _text.AppendLine($"    js   __oob_set_{id}");

        _text.AppendLine("    mov  rcx, [rbx]");
        _text.AppendLine("    cmp  rax, rcx");
        _text.AppendLine($"    jge  __oob_set_{id}");

        _text.AppendLine("    lea  rbx, [rbx + 16 + rax*8]"); // element addr

        _text.AppendLine("    push rbx");
        EmitExpr(a.Value);
        _text.AppendLine("    pop  rbx");
        _text.AppendLine("    mov  [rbx], rax");
        _text.AppendLine($"    jmp  __arr_set_ok_{id}");

        _text.AppendLine($"__oob_set_{id}:");
        EmitTrapExit1();
        _text.AppendLine($"__arr_set_ok_{id}:");
    }
}