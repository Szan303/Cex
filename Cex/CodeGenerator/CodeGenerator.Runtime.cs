namespace Cex;

public partial class CodeGenerator
{
    private void EmitTrapExit1()
    {
        _text.AppendLine("    mov  rcx, 1");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    call ExitProcess");
        _text.AppendLine("    add  rsp, 40");
    }

    private void EmitNullTrapIfZero(string reg)
    {
        int id = _labelCount++;
        _text.AppendLine($"    test {reg}, {reg}");
        _text.AppendLine($"    jnz  __null_ok_{id}");
        EmitTrapExit1();
        _text.AppendLine($"__null_ok_{id}:");
    }

    private void EmitPause()
    {
        const string txt = "Press Enter to exit...";
        EmitWriteConsole(GetOrAddString(txt, newline: true), System.Text.Encoding.ASCII.GetByteCount(txt) + 1);
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    mov  rcx, -10");
        _text.AppendLine("    call GetStdHandle");
        _text.AppendLine("    mov  rcx, rax");
        _text.AppendLine("    lea  rdx, [rel inputChar]");
        _text.AppendLine("    mov  r8d, 2");
        _text.AppendLine("    lea  r9,  [rel written]");
        _text.AppendLine("    mov  qword [rsp+32], 0");
        _text.AppendLine("    call ReadConsoleA");
        _text.AppendLine("    add  rsp, 40");
    }

    private void EmitWriteConsole(string label, int len)
    {
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    mov  rcx, -11");
        _text.AppendLine("    call GetStdHandle");
        _text.AppendLine("    mov  rcx, rax");
        _text.AppendLine($"    lea  rdx, [rel {label}]");
        _text.AppendLine($"    mov  r8d, {len}");
        _text.AppendLine("    lea  r9,  [rel written]");
        _text.AppendLine("    mov  qword [rsp+32], 0");
        _text.AppendLine("    call WriteConsoleA");
        _text.AppendLine("    add  rsp, 40");
    }

    private void EmitWriteConsoleRdx()
    {
        int id = _labelCount++;
        string nullLbl = GetOrAddString("null", newline: false);

        _text.AppendLine("    test rdx, rdx");
        _text.AppendLine($"    jnz  __wcrdx_notnull_{id}");
        EmitWriteConsole(nullLbl, 4);
        _text.AppendLine($"    jmp  __wcrdx_done_{id}");
        _text.AppendLine($"__wcrdx_notnull_{id}:");

        _text.AppendLine("    push rdx");
        _text.AppendLine("    mov  rsi, rdx");
        _text.AppendLine("    xor  ecx, ecx");
        _text.AppendLine($"__strlen_loop_{id}:");
        _text.AppendLine("    cmp  byte [rsi+rcx], 0");
        _text.AppendLine($"    je   __strlen_done_{id}");
        _text.AppendLine("    inc  ecx");
        _text.AppendLine($"    jmp  __strlen_loop_{id}");
        _text.AppendLine($"__strlen_done_{id}:");

        _text.AppendLine("    test ecx, ecx");
        _text.AppendLine($"    jz   __wcrdx_empty_{id}");

        _text.AppendLine("    push rcx");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    mov  rcx, -11");
        _text.AppendLine("    call GetStdHandle");
        _text.AppendLine("    add  rsp, 40");
        _text.AppendLine("    pop  r8");
        _text.AppendLine("    pop  rdx");
        _text.AppendLine("    mov  rcx, rax");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    lea  r9,  [rel written]");
        _text.AppendLine("    mov  qword [rsp+32], 0");
        _text.AppendLine("    call WriteConsoleA");
        _text.AppendLine("    add  rsp, 40");
        _text.AppendLine($"    jmp  __wcrdx_done_{id}");

        _text.AppendLine($"__wcrdx_empty_{id}:");
        _text.AppendLine("    pop  rdx");

        _text.AppendLine($"__wcrdx_done_{id}:");
    }

    private void EmitWriteStringRdxNewline()
    {
        EmitWriteConsoleRdx();
        EmitWriteConsole(GetOrAddString("", newline: true), 1);
    }

    private void EmitWriteInt(bool addNewline)
    {
        _text.AppendLine("    call __intToStr");
        _text.AppendLine("    push rdx");
        _text.AppendLine("    push rcx");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    mov  rcx, -11");
        _text.AppendLine("    call GetStdHandle");
        _text.AppendLine("    add  rsp, 40");
        _text.AppendLine("    pop  r8");
        _text.AppendLine("    pop  rdx");
        _text.AppendLine("    mov  rcx, rax");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    lea  r9,  [rel written]");
        _text.AppendLine("    mov  qword [rsp+32], 0");
        _text.AppendLine("    call WriteConsoleA");
        _text.AppendLine("    add  rsp, 40");
        if (addNewline) EmitWriteConsole(GetOrAddString("", newline: true), 1);
    }
}