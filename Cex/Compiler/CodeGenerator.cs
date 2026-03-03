using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Cex.AST;

namespace Cex.Compiler;

public class CodeGenerator
{
    private int _strCount   = 0;
    private int _labelCount = 0;

    private readonly StringBuilder _data = new();
    private readonly StringBuilder _bss  = new();
    private readonly StringBuilder _text = new();
    private readonly StringBuilder _helpers = new();   // subroutines appended after main code

    private readonly Dictionary<string, string> _strMap  = new();
    private readonly Dictionary<string, string> _varType = new();

    private readonly Stack<(string breakLbl, string continueLbl)> _loopLabels = new();

    private bool _convBufAdded   = false;
    private bool _intToStrAdded  = false;

    // ================================================================= public
    public string Generate(List<Expression> expressions)
    {
        _bss.AppendLine("written   resd 1");
        _bss.AppendLine("inputChar resb 256");

        ReserveVariables(expressions);

        foreach (var e in expressions)
            Emit(e);

        EmitPause();
        _text.AppendLine("    mov  rcx, 0");
        _text.AppendLine("    call ExitProcess");

        return BuildOutput();
    }

    // ============================================================= emit switch
    private void Emit(Expression expr)
    {
        switch (expr)
        {
            case VariableDeclaration v:  EmitVarDecl(v);  break;
            case AssignmentStatement a:  EmitAssignment(a); break;
            case CheckpointStatement c:  _text.AppendLine($"{c.Name}:"); break;
            case GotoStatement g:        _text.AppendLine($"    jmp {g.TargetName}"); break;
            case PrintStatement p:       EmitPrint(p);    break;
            case InputStatement i:       EmitInput(i);    break;
            case IfStatement i:          EmitIf(i);       break;
            case WhileStatement w:       EmitWhile(w);    break;
            case ForStatement f:         EmitFor(f);      break;
            case BreakStatement:         EmitBreak();     break;
            case ContinueStatement:      EmitContinue();  break;
            case AsmBlock a:
                foreach (var line in a.Lines)
                    _text.AppendLine($"    {line}");
                break;
            case ClassDeclaration cls:
                foreach (var e in cls.Body) Emit(e);
                break;
        }
    }

    // ---------------------------------------------------------------- var decl
    private void EmitVarDecl(VariableDeclaration v)
    {
        _varType[v.Name] = v.Type;
        if (v.Value == null) return;

        if (v.Type == "string")
        {
            string raw   = v.Value.Trim('"');
            string label = GetOrAddString(raw, newline: false);
            _text.AppendLine($"    lea  rax, [rel {label}]");
            _text.AppendLine($"    mov  [rel {v.Name}], rax");
        }
        else
        {
            _text.AppendLine($"    mov qword [rel {v.Name}], {v.Value}");
        }
    }

    // ------------------------------------------------------------------ print
    private void EmitPrint(PrintStatement p)
    {
        if (p.Literal != null)
        {
            string label = GetOrAddString(p.Literal, newline: true);
            int    len   = Encoding.ASCII.GetByteCount(p.Literal) + 1;
            EmitWriteConsole(label, len);
        }
        else if (p.VarName != null)
        {
            string type = _varType.TryGetValue(p.VarName, out var t) ? t : "int";
            if (type == "string")
            {
                _text.AppendLine($"    mov  rdx, [rel {p.VarName}]");
                EmitWriteConsoleRdx();
            }
            else
            {
                EmitIntToStr(p.VarName);
            }
        }
        else if (p.Format != null)
        {
            EmitFormatPrint(p.Format, p.FormatArgs);
        }
    }

    // ------------------------------------------------------------------ input
    private void EmitInput(InputStatement inp)
    {
        string type = _varType.TryGetValue(inp.VarName, out var t) ? t : "int";

        _text.AppendLine("    ; --- input ---");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    mov  rcx, -10");
        _text.AppendLine("    call GetStdHandle");
        _text.AppendLine("    mov  rcx, rax");
        _text.AppendLine("    lea  rdx, [rel inputChar]");
        _text.AppendLine("    mov  r8d, 255");
        _text.AppendLine("    lea  r9,  [rel written]");
        _text.AppendLine("    mov  qword [rsp+32], 0");
        _text.AppendLine("    call ReadConsoleA");
        _text.AppendLine("    add  rsp, 40");

        if (type == "int" || type == "float")
        {
            EmitStrToInt();
            _text.AppendLine($"    mov  [rel {inp.VarName}], rax");
        }
        else
        {
            _text.AppendLine($"    lea  rax, [rel inputChar]");
            _text.AppendLine($"    mov  [rel {inp.VarName}], rax");
        }
    }

    // ------------------------------------------------------------------ if
    private void EmitIf(IfStatement i)
    {
        int    id     = _labelCount++;
        string lblEnd = $"__endif_{id}";

        string firstFalse = i.ElseIfs.Count > 0
            ? $"__elseif_{id}_0"
            : (i.ElseBranch.Count > 0 ? $"__else_{id}" : lblEnd);

        EmitConditionJump(i.Condition, firstFalse);
        foreach (var e in i.ThenBranch) Emit(e);
        _text.AppendLine($"    jmp {lblEnd}");

        for (int k = 0; k < i.ElseIfs.Count; k++)
        {
            string thisLbl = $"__elseif_{id}_{k}";
            string nextLbl = k + 1 < i.ElseIfs.Count
                ? $"__elseif_{id}_{k + 1}"
                : (i.ElseBranch.Count > 0 ? $"__else_{id}" : lblEnd);

            _text.AppendLine($"{thisLbl}:");
            EmitConditionJump(i.ElseIfs[k].Condition, nextLbl);
            foreach (var e in i.ElseIfs[k].Body) Emit(e);
            _text.AppendLine($"    jmp {lblEnd}");
        }

        if (i.ElseBranch.Count > 0)
        {
            _text.AppendLine($"__else_{id}:");
            foreach (var e in i.ElseBranch) Emit(e);
        }

        _text.AppendLine($"__endif_{id}:");
    }

    // ------------------------------------------------------------------ while
    private void EmitWhile(WhileStatement w)
    {
        int    id       = _labelCount++;
        string lblStart = $"__while_{id}";
        string lblEnd   = $"__endwhile_{id}";

        _loopLabels.Push((lblEnd, lblStart));

        _text.AppendLine($"{lblStart}:");
        EmitConditionJump(w.Condition, lblEnd);
        foreach (var e in w.Body) Emit(e);
        _text.AppendLine($"    jmp {lblStart}");
        _text.AppendLine($"{lblEnd}:");

        _loopLabels.Pop();
    }

    // ------------------------------------------------------------------ for
    private void EmitFor(ForStatement f)
    {
        int    id          = _labelCount++;
        string lblStart    = $"__for_{id}";
        string lblContinue = $"__forcont_{id}";
        string lblEnd      = $"__endfor_{id}";

        // initialise loop variable
        if (long.TryParse(f.From, out long fromVal))
            _text.AppendLine($"    mov qword [rel {f.VarName}], {fromVal}");
        else
        {
            _text.AppendLine($"    mov rax, [rel {f.From}]");
            _text.AppendLine($"    mov [rel {f.VarName}], rax");
        }

        _loopLabels.Push((lblEnd, lblContinue));

        _text.AppendLine($"{lblStart}:");
        _text.AppendLine($"    mov  rax, [rel {f.VarName}]");

        if (long.TryParse(f.To, out long toVal))
            _text.AppendLine($"    cmp  rax, {toVal}");
        else
            _text.AppendLine($"    cmp  rax, [rel {f.To}]");

        _text.AppendLine($"    jge  {lblEnd}");

        foreach (var e in f.Body) Emit(e);

        _text.AppendLine($"{lblContinue}:");
        _text.AppendLine($"    mov  rax, [rel {f.VarName}]");

        if (long.TryParse(f.Step, out long stepVal))
            _text.AppendLine($"    add  rax, {stepVal}");
        else
            _text.AppendLine($"    add  rax, [rel {f.Step}]");

        _text.AppendLine($"    mov  [rel {f.VarName}], rax");
        _text.AppendLine($"    jmp  {lblStart}");
        _text.AppendLine($"{lblEnd}:");

        _loopLabels.Pop();
    }

    private void EmitBreak()
    {
        if (_loopLabels.Count == 0) throw new Exception("break used outside of a loop");
        _text.AppendLine($"    jmp {_loopLabels.Peek().breakLbl}");
    }

    private void EmitContinue()
    {
        if (_loopLabels.Count == 0) throw new Exception("continue used outside of a loop");
        _text.AppendLine($"    jmp {_loopLabels.Peek().continueLbl}");
    }

    // ---------------------------------------------------------------- assignment
    private void EmitAssignment(AssignmentStatement a)
    {
        switch (a.Operator)
        {
            case "++":
                _text.AppendLine($"    mov  rax, [rel {a.VarName}]");
                _text.AppendLine("    inc  rax");
                _text.AppendLine($"    mov  [rel {a.VarName}], rax");
                break;

            case "--":
                _text.AppendLine($"    mov  rax, [rel {a.VarName}]");
                _text.AppendLine("    dec  rax");
                _text.AppendLine($"    mov  [rel {a.VarName}], rax");
                break;

            case "=":
                LoadValue(a.Left ?? a.Operand!, "rax");
                if (a.RhsOp != null)
                {
                    LoadValue(a.Operand!, "rbx");
                    EmitArith(a.RhsOp);
                }
                _text.AppendLine($"    mov  [rel {a.VarName}], rax");
                break;

            default: // += -= *= /=
                _text.AppendLine($"    mov  rax, [rel {a.VarName}]");
                LoadValue(a.Operand!, "rbx");
                EmitArith(a.Operator.TrimEnd('=') switch
                {
                    "+" => "+", "-" => "-", "*" => "*", "/" => "/",
                    _   => throw new Exception($"Unknown operator: {a.Operator}")
                });
                _text.AppendLine($"    mov  [rel {a.VarName}], rax");
                break;
        }
    }

    // ------------------------------------------------------------------ pause
    private void EmitPause()
    {
        string label = GetOrAddString("Press Enter to exit...", newline: true);
        int    len   = Encoding.ASCII.GetByteCount("Press Enter to exit...") + 1;

        _text.AppendLine("    ; --- pause ---");
        EmitWriteConsole(label, len);

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

    // ================================================================= helpers

    // WriteConsoleA with known label + byte length
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

    // WriteConsoleA when rdx already contains the string pointer — uses inline strlen
    private void EmitWriteConsoleRdx()
    {
        int id = _labelCount++;
        _text.AppendLine("    mov  rsi, rdx");
        _text.AppendLine("    xor  ecx, ecx");
        _text.AppendLine($"__strlen_loop_{id}:");
        _text.AppendLine("    cmp  byte [rsi+rcx], 0");
        _text.AppendLine($"    je   __strlen_done_{id}");
        _text.AppendLine("    inc  ecx");
        _text.AppendLine($"    jmp  __strlen_loop_{id}");
        _text.AppendLine($"__strlen_done_{id}:");
        _text.AppendLine("    mov  r8d, ecx");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    mov  rcx, -11");
        _text.AppendLine("    call GetStdHandle");
        _text.AppendLine("    mov  rcx, rax");
        _text.AppendLine("    mov  rdx, rsi");
        _text.AppendLine("    lea  r9,  [rel written]");
        _text.AppendLine("    mov  qword [rsp+32], 0");
        _text.AppendLine("    call WriteConsoleA");
        _text.AppendLine("    add  rsp, 40");
    }

    // convert integer variable to string and print it
    private void EmitIntToStr(string varName)
    {
        EnsureConvBuf();
        _text.AppendLine($"    mov  rax, [rel {varName}]");
        _text.AppendLine("    call __intToStr");
        // __intToStr leaves: rdx = ptr to string, rcx = length
        _text.AppendLine("    mov  r8d, ecx");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    mov  rsi, rdx");
        _text.AppendLine("    mov  rcx, -11");
        _text.AppendLine("    call GetStdHandle");
        _text.AppendLine("    mov  rcx, rax");
        _text.AppendLine("    mov  rdx, rsi");
        _text.AppendLine("    lea  r9,  [rel written]");
        _text.AppendLine("    mov  qword [rsp+32], 0");
        _text.AppendLine("    call WriteConsoleA");
        _text.AppendLine("    add  rsp, 40");
    }

    private void EnsureConvBuf()
    {
        if (!_convBufAdded)
        {
            _bss.AppendLine("convBuf resb 32");
            _convBufAdded = true;
        }
        EnsureIntToStrHelper();
    }

    // emit __intToStr subroutine once into _helpers
    private void EnsureIntToStrHelper()
    {
        if (_intToStrAdded) return;
        _intToStrAdded = true;

        _helpers.AppendLine("""
; ---- __intToStr: rax = int64 in, rdx = string ptr out, ecx = length out ----
__intToStr:
    push rbx
    push rdi
    push rsi
    lea  rdi, [rel convBuf]
    ; point rsi to end of buffer
    lea  rsi, [rel convBuf]
    add  rsi, 31
    mov  byte [rsi], 0xA       ; newline at end
    dec  rsi
    ; handle negative
    test rax, rax
    jns  __its_pos
    neg  rax
    mov  byte [rdi], '-'
    inc  rdi
__its_pos:
    mov  rbx, 10
__its_digit:
    xor  rdx, rdx
    div  rbx
    add  dl, '0'
    mov  [rsi], dl
    dec  rsi
    test rax, rax
    jnz  __its_digit
    inc  rsi
    ; rdx = pointer to first digit, ecx = length
    mov  rdx, rsi
    lea  rcx, [rel convBuf]
    add  rcx, 32               ; one past end (including newline)
    sub  rcx, rsi
    ; if negative, adjust pointer to include '-'
    cmp  byte [rdi-1], '-'
    jne  __its_done
    dec  rdx
    inc  ecx
__its_done:
    pop  rsi
    pop  rdi
    pop  rbx
    ret
""");
    }

    // inline strToInt: reads inputChar buffer, result in rax
    private void EmitStrToInt()
    {
        int id = _labelCount++;
        _text.AppendLine("    lea  rsi, [rel inputChar]");
        _text.AppendLine("    xor  rax, rax");
        _text.AppendLine("    xor  rbx, rbx");
        _text.AppendLine($"__stoi_loop_{id}:");
        _text.AppendLine("    movzx rbx, byte [rsi]");
        _text.AppendLine("    cmp  rbx, '0'");
        _text.AppendLine($"    jl   __stoi_done_{id}");
        _text.AppendLine("    cmp  rbx, '9'");
        _text.AppendLine($"    jg   __stoi_done_{id}");
        _text.AppendLine("    sub  rbx, '0'");
        _text.AppendLine("    imul rax, rax, 10");
        _text.AppendLine("    add  rax, rbx");
        _text.AppendLine("    inc  rsi");
        _text.AppendLine($"    jmp  __stoi_loop_{id}");
        _text.AppendLine($"__stoi_done_{id}:");
    }

    // format print: "hello {x} world"
    private void EmitFormatPrint(string fmt, List<string> args)
    {
        string pattern = @"\{(\w+)\}";
        int last = 0;

        foreach (Match m in Regex.Matches(fmt, pattern))
        {
            string before = fmt.Substring(last, m.Index - last);
            if (before.Length > 0)
            {
                string lbl = GetOrAddString(before, newline: false);
                EmitWriteConsole(lbl, Encoding.ASCII.GetByteCount(before));
            }

            string varN = m.Groups[1].Value;
            string type = _varType.TryGetValue(varN, out var vt) ? vt : "int";
            if (type == "string")
            {
                _text.AppendLine($"    mov  rdx, [rel {varN}]");
                EmitWriteConsoleRdx();
            }
            else
            {
                EmitIntToStr(varN);
            }

            last = m.Index + m.Length;
        }

        string tail = fmt.Substring(last);
        if (tail.Length > 0)
        {
            string lbl = GetOrAddString(tail, newline: true);
            EmitWriteConsole(lbl, Encoding.ASCII.GetByteCount(tail) + 1);
        }
    }

    // emit cmp + conditional jump to falseLabel when condition is false
    private void EmitConditionJump(Condition c, string falseLabel)
    {
        LoadValue(c.Left, "rax");

        if (long.TryParse(c.Right, out long imm))
            _text.AppendLine($"    cmp  rax, {imm}");
        else
            _text.AppendLine($"    cmp  rax, [rel {c.Right}]");

        string j = c.Op switch
        {
            "==" => "jne",
            "!=" => "je",
            "<"  => "jge",
            ">"  => "jle",
            "<=" => "jg",
            ">=" => "jl",
            _    => throw new Exception($"Unknown operator: {c.Op}")
        };
        _text.AppendLine($"    {j}  {falseLabel}");
    }

    private void LoadValue(string v, string reg)
    {
        if (long.TryParse(v, out long n))
            _text.AppendLine($"    mov  {reg}, {n}");
        else
            _text.AppendLine($"    mov  {reg}, [rel {v}]");
    }

    private void EmitArith(string op)
    {
        switch (op)
        {
            case "+": _text.AppendLine("    add  rax, rbx"); break;
            case "-": _text.AppendLine("    sub  rax, rbx"); break;
            case "*": _text.AppendLine("    imul rax, rbx"); break;
            case "/":
                _text.AppendLine("    cqo");
                _text.AppendLine("    idiv rbx");
                break;
        }
    }

    private void ReserveVariables(List<Expression> exprs)
    {
        foreach (var e in exprs)
        {
            if (e is VariableDeclaration v)
            {
                _bss.AppendLine($"{v.Name}: resq 1");
                _varType[v.Name] = v.Type;
            }
            else if (e is ClassDeclaration cls) ReserveVariables(cls.Body);
            else if (e is IfStatement i)
            {
                ReserveVariables(i.ThenBranch);
                foreach (var ei in i.ElseIfs) ReserveVariables(ei.Body);
                ReserveVariables(i.ElseBranch);
            }
            else if (e is WhileStatement w) ReserveVariables(w.Body);
            else if (e is ForStatement f)
            {
                _bss.AppendLine($"{f.VarName}: resq 1");
                ReserveVariables(f.Body);
            }
        }
    }

    private string GetOrAddString(string text, bool newline)
    {
        string key = text + (newline ? "\\n" : "");
        if (_strMap.TryGetValue(key, out string? existing)) return existing;
        _strCount++;
        string label   = $"msg{_strCount}";
        string escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _data.AppendLine(newline
            ? $"{label}: db \"{escaped}\",0xA,0"
            : $"{label}: db \"{escaped}\",0");
        _strMap[key] = label;
        return label;
    }

    private string BuildOutput()
    {
        var sb = new StringBuilder();
        sb.AppendLine("; Generated by C! compiler");
        sb.AppendLine("extern ExitProcess");
        sb.AppendLine("extern WriteConsoleA");
        sb.AppendLine("extern GetStdHandle");
        sb.AppendLine("extern ReadConsoleA");
        sb.AppendLine();
        sb.AppendLine("section .data");
        sb.Append(_data);
        sb.AppendLine();
        sb.AppendLine("section .bss");
        sb.Append(_bss);
        sb.AppendLine();
        sb.AppendLine("section .text");
        sb.AppendLine("global start");
        sb.AppendLine("start:");
        sb.Append(_text);
        // helpers (subroutines) go after main code flow
        sb.AppendLine();
        sb.Append(_helpers);
        return sb.ToString();
    }
}