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

    // these three are always independent — never swapped
    private readonly StringBuilder _data    = new();
    private readonly StringBuilder _bss     = new();
    private readonly StringBuilder _helpers = new();

    // _main   = start: entry code
    // _fnText = current function body being built
    // _text   = whichever is active (points to _main or _fnText)
    private readonly StringBuilder _main = new();
    private StringBuilder          _fnText = new();
    private StringBuilder          _text;   // active target

    private readonly Dictionary<string, string>              _strMap    = new();
    private readonly Dictionary<string, string>              _varType   = new();
    private readonly SymbolTable                             _symbols;
    private readonly Stack<(string breakLbl, string continueLbl)> _loopLabels = new();

    private string? _currentFuncRetLabel = null;

    public CodeGenerator(SymbolTable symbols)
    {
        _symbols = symbols;
        _text    = _main;   // default target is main entry code
    }

    // ================================================================= public
    public string Generate(List<CompilationUnit> units)
    {
        // 1. bss reservations
        _bss.AppendLine("written   resd 1");
        _bss.AppendLine("inputChar resb 256");
        _bss.AppendLine("convBuf   resb 32");

        foreach (var unit in units)
            ReserveVariables(unit.Expressions);

        // 2. emit __intToStr helper into _helpers unconditionally and first
        EmitIntToStrHelper();

        // 3. entry point in _main
        _text = _main;
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    call __fn_Main");
        _text.AppendLine("    add  rsp, 40");
        EmitPause();
        _text.AppendLine("    mov  rcx, 0");
        _text.AppendLine("    call ExitProcess");

        // 4. emit every function body into _helpers
        foreach (var fn in _symbols.Functions.Values)
            EmitFunctionBody(fn);

        return BuildOutput();
    }

    // ============================================================= emit switch
    private void Emit(Expression expr)
    {
        switch (expr)
        {
            case FunctionDeclaration:    break;
            case ImportStatement:        break;
            case VariableDeclaration v:  EmitVarDecl(v);    break;
            case AssignmentStatement a:  EmitAssignment(a); break;
            case CheckpointStatement c:  _text.AppendLine($"{c.Name}:"); break;
            case GotoStatement g:        _text.AppendLine($"    jmp {g.TargetName}"); break;
            case PrintStatement p:       EmitPrint(p);      break;
            case InputStatement i:       EmitInput(i);      break;
            case IfStatement i:          EmitIf(i);         break;
            case WhileStatement w:       EmitWhile(w);      break;
            case ForStatement f:         EmitFor(f);        break;
            case BreakStatement:         EmitBreak();       break;
            case ContinueStatement:      EmitContinue();    break;
            case ReturnStatement r:      EmitReturn(r);     break;
            case TryStatement t:         EmitTry(t);        break;
            case FunctionCall fc:        EmitFunctionCall(fc, "rax"); break;
            case AsmBlock a:
                foreach (var line in a.Lines)
                    _text.AppendLine($"    {line}");
                break;
            case ClassDeclaration cls:
                foreach (var e in cls.Body)
                    if (e is not FunctionDeclaration)
                        Emit(e);
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
            _text.AppendLine($"    mov  qword [rel {v.Name}], {v.Value}");
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
                EmitPrintInt(p.VarName);
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

        if (type is "int" or "float")
        {
            EmitStrToInt();
            _text.AppendLine($"    mov  [rel {inp.VarName}], rax");
        }
        else
        {
            _text.AppendLine("    lea  rax, [rel inputChar]");
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

        if (long.TryParse(f.From, out long fromVal))
            _text.AppendLine($"    mov  qword [rel {f.VarName}], {fromVal}");
        else
        {
            _text.AppendLine($"    mov  rax, [rel {f.From}]");
            _text.AppendLine($"    mov  [rel {f.VarName}], rax");
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

    // ------------------------------------------------------------------ try
    private void EmitTry(TryStatement t)
    {
        int    id         = _labelCount++;
        string lblFinally = $"__finally_{id}";
        string lblCatch   = $"__catch_{id}";

        foreach (var e in t.TryBody) Emit(e);
        _text.AppendLine($"    jmp {lblFinally}");
        _text.AppendLine($"{lblCatch}:");
        foreach (var e in t.CatchBody) Emit(e);
        _text.AppendLine($"{lblFinally}:");
        foreach (var e in t.FinallyBody) Emit(e);
    }

    // ---------------------------------------------------------------- functions
    private void EmitFunctionBody(FunctionDeclaration fn)
    {
        string retLabel = $"__ret_{fn.Name}";
        _currentFuncRetLabel = retLabel;

        // all function code goes into _helpers
        _text = _helpers;

        _text.AppendLine($"; ---- {fn.Access} {fn.ReturnType} {fn.Name} ----");
        _text.AppendLine($"__fn_{fn.Name}:");
        _text.AppendLine("    push rbp");
        _text.AppendLine("    mov  rbp, rsp");

        string[] paramRegs = { "rcx", "rdx", "r8", "r9" };
        for (int i = 0; i < fn.Parameters.Count && i < 4; i++)
        {
            _text.AppendLine($"    mov  [rel {fn.Parameters[i].Name}], {paramRegs[i]}");
            _varType[fn.Parameters[i].Name] = fn.Parameters[i].Type;
        }

        foreach (var e in fn.Body) Emit(e);

        _text.AppendLine($"{retLabel}:");
        _text.AppendLine("    pop  rbp");
        _text.AppendLine("    ret");
        _text.AppendLine();

        // restore to main (though after all fns this doesn't matter)
        _text = _main;
        _currentFuncRetLabel = null;
    }

    private void EmitFunctionCall(FunctionCall fc, string resultReg)
    {
        if (EmitStdLibCall(fc, resultReg)) return;

        if (!_symbols.FunctionExists(fc.Name))
            throw new Exception($"Call to undefined function '{fc.Name}'");

        string[] paramRegs = { "rcx", "rdx", "r8", "r9" };
        for (int i = 0; i < fc.Arguments.Count && i < 4; i++)
            LoadValue(fc.Arguments[i], paramRegs[i]);

        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine($"    call __fn_{fc.Name}");
        _text.AppendLine("    add  rsp, 40");

        if (resultReg != "rax")
            _text.AppendLine($"    mov  {resultReg}, rax");
    }

    // ------------------------------------------------------------------ stdlib
    private bool EmitStdLibCall(FunctionCall fc, string resultReg)
    {
        switch (fc.Name)
        {
            case "math_abs":
            {
                int id = _labelCount++;
                LoadValue(fc.Arguments[0], "rax");
                _text.AppendLine("    test rax, rax");
                _text.AppendLine($"    jns  __abs_pos_{id}");
                _text.AppendLine("    neg  rax");
                _text.AppendLine($"__abs_pos_{id}:");
                if (resultReg != "rax") _text.AppendLine($"    mov  {resultReg}, rax");
                return true;
            }
            case "math_max":
            {
                int id = _labelCount++;
                LoadValue(fc.Arguments[0], "rax");
                LoadValue(fc.Arguments[1], "rbx");
                _text.AppendLine("    cmp  rax, rbx");
                _text.AppendLine($"    jge  __max_done_{id}");
                _text.AppendLine("    mov  rax, rbx");
                _text.AppendLine($"__max_done_{id}:");
                if (resultReg != "rax") _text.AppendLine($"    mov  {resultReg}, rax");
                return true;
            }
            case "math_min":
            {
                int id = _labelCount++;
                LoadValue(fc.Arguments[0], "rax");
                LoadValue(fc.Arguments[1], "rbx");
                _text.AppendLine("    cmp  rax, rbx");
                _text.AppendLine($"    jle  __min_done_{id}");
                _text.AppendLine("    mov  rax, rbx");
                _text.AppendLine($"__min_done_{id}:");
                if (resultReg != "rax") _text.AppendLine($"    mov  {resultReg}, rax");
                return true;
            }
            case "str_len":
            {
                int sid = _labelCount++;
                _text.AppendLine($"    mov  rsi, [rel {fc.Arguments[0]}]");
                _text.AppendLine("    xor  ecx, ecx");
                _text.AppendLine($"__strlen_loop_{sid}:");
                _text.AppendLine("    cmp  byte [rsi+rcx], 0");
                _text.AppendLine($"    je   __strlen_done_{sid}");
                _text.AppendLine("    inc  ecx");
                _text.AppendLine($"    jmp  __strlen_loop_{sid}");
                _text.AppendLine($"__strlen_done_{sid}:");
                _text.AppendLine("    mov  rax, rcx");
                if (resultReg != "rax") _text.AppendLine($"    mov  {resultReg}, rax");
                return true;
            }
            default: return false;
        }
    }

    // ------------------------------------------------------------------ return
    private void EmitReturn(ReturnStatement r)
    {
        if (r.Value != null) LoadValue(r.Value, "rax");
        if (_currentFuncRetLabel != null)
            _text.AppendLine($"    jmp {_currentFuncRetLabel}");
        else
        {
            _text.AppendLine("    mov  rcx, 0");
            _text.AppendLine("    call ExitProcess");
        }
    }

    private void EmitBreak()
    {
        if (_loopLabels.Count == 0) throw new Exception("break outside loop");
        _text.AppendLine($"    jmp {_loopLabels.Peek().breakLbl}");
    }

    private void EmitContinue()
    {
        if (_loopLabels.Count == 0) throw new Exception("continue outside loop");
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
                if (a.RhsOp != null) { LoadValue(a.Operand!, "rbx"); EmitArith(a.RhsOp); }
                _text.AppendLine($"    mov  [rel {a.VarName}], rax");
                break;
            default:
                _text.AppendLine($"    mov  rax, [rel {a.VarName}]");
                LoadValue(a.Operand!, "rbx");
                EmitArith(a.Operator.TrimEnd('=') switch
                {
                    "+" => "+", "-" => "-", "*" => "*", "/" => "/", "%" => "%",
                    _   => throw new Exception($"Unknown operator: {a.Operator}")
                });
                _text.AppendLine($"    mov  [rel {a.VarName}], rax");
                break;
        }
    }

    // ------------------------------------------------------------------ pause
    private void EmitPause()
    {
        const string pauseText = "Press Enter to exit...";
        string label = GetOrAddString(pauseText, newline: true);
        int    len   = Encoding.ASCII.GetByteCount(pauseText) + 1;

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

    // ================================================================= write helpers
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

    // print integer variable — calls __intToStr then WriteConsoleA
    private void EmitPrintInt(string varName)
    {
        _text.AppendLine($"    mov  rax, [rel {varName}]");
        _text.AppendLine("    call __intToStr");
        // __intToStr returns: rdx = ptr, ecx = length
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

    // emit __intToStr subroutine into _helpers — called once at start of Generate()
    private void EmitIntToStrHelper()
    {
        _helpers.AppendLine("""
                            ; ================================================================= __intToStr
                            ; in:  rax = int64
                            ; out: rdx = pointer to ASCII string, ecx = length (includes newline)
                            __intToStr:
                                push rbx
                                push rdi
                                push rsi

                                ; rsi = write cursor starting at end of buffer
                                lea  rsi, [rel convBuf]
                                add  rsi, 29             ; point to last usable byte (leave room for \n)

                                ; store newline AFTER the number
                                mov  byte [rsi+1], 0xA
                                mov  byte [rsi+2], 0

                                ; is number negative?
                                xor  rdi, rdi            ; rdi = 0 means positive, 1 means negative
                                test rax, rax
                                jns  __its_pos
                                neg  rax
                                mov  rdi, 1              ; flag negative
                            __its_pos:
                                mov  rbx, 10
                            __its_digit:
                                xor  rdx, rdx
                                div  rbx                 ; rax = quotient, rdx = remainder
                                add  dl, '0'
                                mov  [rsi], dl           ; write digit
                                dec  rsi
                                test rax, rax
                                jnz  __its_digit

                                ; if negative, write '-'
                                test rdi, rdi
                                jz   __its_no_minus
                                mov  byte [rsi], '-'
                                dec  rsi
                            __its_no_minus:
                                inc  rsi                 ; rsi now points to first character

                                ; rdx = start of string
                                mov  rdx, rsi

                                ; ecx = length including newline
                                lea  rcx, [rel convBuf]
                                add  rcx, 31             ; points to the 0xA byte
                                sub  rcx, rsi
                                inc  ecx                 ; include the newline itself

                                pop  rsi
                                pop  rdi
                                pop  rbx
                                ret
                            ; =================================================================
                            """);
    }

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

    private void EmitFormatPrint(string fmt, List<string> args)
    {
        int last = 0;
        foreach (Match m in Regex.Matches(fmt, @"\{(\w+)\}"))
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
                EmitPrintInt(varN);
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

    private void EmitConditionJump(Condition c, string falseLabel)
    {
        LoadValue(c.Left, "rax");

        if (long.TryParse(c.Right, out long imm))
            _text.AppendLine($"    cmp  rax, {imm}");
        else
            _text.AppendLine($"    cmp  rax, [rel {c.Right}]");

        string baseOp = c.Op.Contains("&&") ? c.Op.Split("&&")[0]
                      : c.Op.Contains("||") ? c.Op.Split("||")[0]
                      : c.Op;

        string j = baseOp switch
        {
            "==" => "jne", "!=" => "je",
            "<"  => "jge", ">"  => "jle",
            "<=" => "jg",  ">=" => "jl",
            _    => throw new Exception($"Unknown op: {baseOp}")
        };

        if (c.Negated) j = j switch
        {
            "jne" => "je",  "je"  => "jne",
            "jge" => "jl",  "jl"  => "jge",
            "jle" => "jg",  "jg"  => "jle",
            _     => j
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
            case "%":
                _text.AppendLine("    cqo");
                _text.AppendLine("    idiv rbx");
                _text.AppendLine("    mov  rax, rdx");
                break;
            case "/":
                _text.AppendLine("    cqo");
                _text.AppendLine("    idiv rbx");
                break;
        }
    }

    // ---------------------------------------------------------------- reserve
    private void ReserveVariables(List<Expression> exprs)
    {
        foreach (var e in exprs)
        {
            switch (e)
            {
                case VariableDeclaration v:
                    _bss.AppendLine($"{v.Name}: resq 1");
                    _varType[v.Name] = v.Type;
                    break;
                case FunctionDeclaration fn:
                    foreach (var p in fn.Parameters)
                    {
                        _bss.AppendLine($"{p.Name}: resq 1");
                        _varType[p.Name] = p.Type;
                    }
                    ReserveVariables(fn.Body);
                    break;
                case ClassDeclaration cls:
                    ReserveVariables(cls.Body);
                    break;
                case IfStatement i:
                    ReserveVariables(i.ThenBranch);
                    foreach (var ei in i.ElseIfs) ReserveVariables(ei.Body);
                    ReserveVariables(i.ElseBranch);
                    break;
                case WhileStatement w:
                    ReserveVariables(w.Body);
                    break;
                case ForStatement f:
                    _bss.AppendLine($"{f.VarName}: resq 1");
                    ReserveVariables(f.Body);
                    break;
                case TryStatement t:
                    ReserveVariables(t.TryBody);
                    ReserveVariables(t.CatchBody);
                    ReserveVariables(t.FinallyBody);
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- strings
    private string GetOrAddString(string text, bool newline)
    {
        string key = text + (newline ? "\n" : "");
        if (_strMap.TryGetValue(key, out string? existing)) return existing;

        _strCount++;
        string label   = $"msg{_strCount}";
        string escaped = text.Replace("\\n", "\",0xA,\"")
                             .Replace("\\t", "\",0x9,\"");
        _data.AppendLine(newline
            ? $"{label}: db \"{escaped}\",0xA,0"
            : $"{label}: db \"{escaped}\",0");
        _strMap[key] = label;
        return label;
    }

    // ---------------------------------------------------------------- output
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
        sb.Append(_main);      // entry point code
        sb.AppendLine();
        sb.Append(_helpers);   // __intToStr + all function bodies
        return sb.ToString();
    }
}