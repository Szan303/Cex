using System;
using System.Collections.Generic;
using System.Text;
using Cex.AST;

namespace Cex.Compiler;

public class CodeGenerator
{
    private int _strCount   = 0;
    private int _labelCount = 0;

    private readonly StringBuilder _data    = new();
    private readonly StringBuilder _bss     = new();
    private readonly StringBuilder _helpers = new();
    private readonly StringBuilder _main    = new();
    private StringBuilder          _text;

    private readonly Dictionary<string, string> _strMap        = new();
    private readonly Dictionary<string, string> _globalVarType = new();
    private readonly Dictionary<string, int>    _arraySize     = new();  // name → element count

    private readonly SymbolTable _symbols;
    private readonly ScopeStack  _scope = new();
    private readonly Stack<(string breakLbl, string continueLbl)> _loopLabels = new();

    private string? _currentFuncRetLabel = null;

    public CodeGenerator(SymbolTable symbols)
    {
        _symbols = symbols;
        _text    = _main;
    }

    // ================================================================= public
    public string Generate(List<CompilationUnit> units)
    {
        _bss.AppendLine("written   resd 1");
        _bss.AppendLine("inputChar resb 256");
        _bss.AppendLine("convBuf   resb 32");
        ArenaAllocator.EmitBss(_bss);

        foreach (var unit in units)
            ReserveGlobals(unit.Expressions);

        EmitIntToStrHelper();
        EmitStrCatHelper();
        ArenaAllocator.EmitHelpers(_helpers);

        _text = _main;
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    call __heap_init");
        _text.AppendLine("    add  rsp, 40");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    call __fn_Main");
        _text.AppendLine("    add  rsp, 40");
        EmitPause();
        _text.AppendLine("    mov  rcx, 0");
        _text.AppendLine("    call ExitProcess");

        foreach (var fn in _symbols.Functions.Values)
            EmitFunctionBody(fn);

        return BuildOutput();
    }

    // ============================================================= emit switch
    private void Emit(Expression expr)
    {
        switch (expr)
        {
            case FunctionDeclaration:      break;
            case ImportStatement:          break;
            case VariableDeclaration v:    EmitVarDecl(v);       break;
            case ArrayDeclaration a:       EmitArrayDecl(a);     break;
            case ArrayAssignment a:        EmitArrayAssign(a);   break;
            case AssignmentStatement a:    EmitAssignment(a);    break;
            case CheckpointStatement c:    _text.AppendLine($"{c.Name}:"); break;
            case GotoStatement g:          _text.AppendLine($"    jmp {g.TargetName}"); break;
            case PrintStatement p:         EmitPrint(p);         break;
            case InputStatement i:         EmitInput(i);         break;
            case IfStatement i:            EmitIf(i);            break;
            case WhileStatement w:         EmitWhile(w);         break;
            case ForStatement f:           EmitFor(f);           break;
            case BreakStatement:           EmitBreak();          break;
            case ContinueStatement:        EmitContinue();       break;
            case ReturnStatement r:        EmitReturn(r);        break;
            case TryStatement t:           EmitTry(t);           break;
            case FunctionCall fc:
                EmitCallExpr(new CallExpr { Name = fc.Name, Args = fc.Arguments }, "rax");
                break;
            case AsmBlock a:
                foreach (var line in a.Lines) _text.AppendLine($"    {line}");
                break;
            case ClassDeclaration cls:
                foreach (var e in cls.Body)
                    if (e is not FunctionDeclaration) Emit(e);
                break;
        }
    }

    // ================================================================= expression codegen
    // Evaluates any expression, result ends up in rax
    private void EmitExpr(Expression expr)
    {
        switch (expr)
        {
            case NumberLiteral n:
                _text.AppendLine($"    mov  rax, {n.Value}");
                break;

            case BoolLiteral b:
                _text.AppendLine($"    mov  rax, {(b.Value ? 1 : 0)}");
                break;

            case StringLiteralExpr s:
                string lbl = GetOrAddString(s.Value, newline: false);
                _text.AppendLine($"    lea  rax, [rel {lbl}]");
                break;

            case VariableExpr v:
                LoadVar(v.Name, "rax");
                break;

            case ArrayAccess a:
                EmitArrayLoad(a);
                break;

            case CallExpr c:
                EmitCallExpr(c, "rax");
                break;

            case UnaryExpr u:
                EmitExpr(u.Operand);
                if (u.Op == "-")
                    _text.AppendLine("    neg  rax");
                else if (u.Op == "not")
                {
                    _text.AppendLine("    test rax, rax");
                    _text.AppendLine("    setz al");
                    _text.AppendLine("    movzx rax, al");
                }
                break;

            case BinaryExpr b:
                EmitBinary(b);
                break;

            default:
                throw new Exception(
                    $"Cannot evaluate expression '{expr.GetType().Name}' at line {expr.Line}");
        }
    }

    // emit binary expression — result in rax
    private void EmitBinary(BinaryExpr b)
    {
        // string concatenation:  "a" + "b"  or  str + str
        if (b.Op == "+" && IsStringExpr(b.Left))
        {
            EmitStringConcat(b);
            return;
        }

        // evaluate left → push, evaluate right → rbx, pop left → rax
        EmitExpr(b.Left);
        _text.AppendLine("    push rax");
        EmitExpr(b.Right);
        _text.AppendLine("    mov  rbx, rax");
        _text.AppendLine("    pop  rax");

        switch (b.Op)
        {
            case "+":  _text.AppendLine("    add  rax, rbx"); break;
            case "-":  _text.AppendLine("    sub  rax, rbx"); break;
            case "*":  _text.AppendLine("    imul rax, rbx"); break;
            case "/":  _text.AppendLine("    cqo");  _text.AppendLine("    idiv rbx"); break;
            case "%":  _text.AppendLine("    cqo");  _text.AppendLine("    idiv rbx");
                       _text.AppendLine("    mov  rax, rdx"); break;
            case "==": EmitCmpResult("sete");  break;
            case "!=": EmitCmpResult("setne"); break;
            case "<":  EmitCmpResult("setl");  break;
            case ">":  EmitCmpResult("setg");  break;
            case "<=": EmitCmpResult("setle"); break;
            case ">=": EmitCmpResult("setge"); break;
            case "&&":
                _text.AppendLine("    test rax, rax");
                _text.AppendLine("    setnz al");
                _text.AppendLine("    test rbx, rbx");
                _text.AppendLine("    setnz bl");
                _text.AppendLine("    and  al, bl");
                _text.AppendLine("    movzx rax, al");
                break;
            case "||":
                _text.AppendLine("    or   rax, rbx");
                _text.AppendLine("    setnz al");
                _text.AppendLine("    movzx rax, al");
                break;
            default:
                throw new Exception($"Unknown binary operator: {b.Op}");
        }
    }

    private void EmitCmpResult(string setInstr)
    {
        _text.AppendLine("    cmp  rax, rbx");
        _text.AppendLine($"    {setInstr} al");
        _text.AppendLine("    movzx rax, al");
    }

    private bool IsStringExpr(Expression e) =>
        e is StringLiteralExpr ||
        (e is VariableExpr ve && GetVarType(ve.Name) == "string");

    // ---------------------------------------------------------------- string concat
    private void EmitStringConcat(BinaryExpr b)
    {
        // allocate via heap, copy left then right
        // result pointer in rax
        int id = _labelCount++;

        // get left ptr → push
        EmitExpr(b.Left);
        _text.AppendLine("    push rax");

        // get right ptr → push
        EmitExpr(b.Right);
        _text.AppendLine("    push rax");

        // strlen(right) → r13
        _text.AppendLine("    mov  rsi, rax");
        EmitStrLen("rsi", "r13");

        // strlen(left) → r14
        _text.AppendLine("    mov  rsi, [rsp+8]");
        EmitStrLen("rsi", "r14");

        // total = r13 + r14 + 1 (null terminator) → rcx for alloc
        _text.AppendLine("    lea  rcx, [r13+r14+1]");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    call __heap_alloc");
        _text.AppendLine("    add  rsp, 40");

        // rax = dest buffer
        _text.AppendLine("    mov  r15, rax");   // save dest

        // copy left string into dest
        _text.AppendLine("    pop  rbx");         // right ptr (pushed last)
        _text.AppendLine("    pop  rsi");         // left ptr
        _text.AppendLine($"__strcat_left_{id}:");
        _text.AppendLine("    mov  al, [rsi]");
        _text.AppendLine("    test al, al");
        _text.AppendLine($"    jz   __strcat_mid_{id}");
        _text.AppendLine("    mov  [r15], al");
        _text.AppendLine("    inc  rsi");
        _text.AppendLine("    inc  r15");
        _text.AppendLine($"    jmp  __strcat_left_{id}");

        // copy right string
        _text.AppendLine($"__strcat_mid_{id}:");
        _text.AppendLine($"__strcat_right_{id}:");
        _text.AppendLine("    mov  al, [rbx]");
        _text.AppendLine("    mov  [r15], al");
        _text.AppendLine("    test al, al");
        _text.AppendLine($"    jz   __strcat_done_{id}");
        _text.AppendLine("    inc  rbx");
        _text.AppendLine("    inc  r15");
        _text.AppendLine($"    jmp  __strcat_right_{id}");

        _text.AppendLine($"__strcat_done_{id}:");

        // restore dest start into rax
        _text.AppendLine("    lea  rcx, [r13+r14+1]");
        _text.AppendLine("    mov  r15, rax");
        // recalculate: dest = heapPtr - total
        // easier: just re-emit alloc and use saved pointer
        // Actually: save dest before copy
        // Fix: save r15 before copies above — let's use a local label trick
        // The result pointer was saved early; restore from r15 by subtracting length
        _text.AppendLine("    sub  r15, r14");
        _text.AppendLine("    sub  r15, r13");
        _text.AppendLine("    mov  rax, r15");
    }

    private void EmitStrLen(string ptrReg, string outReg)
    {
        int id = _labelCount++;
        _text.AppendLine($"    xor  {outReg}, {outReg}");
        _text.AppendLine($"__slen_{id}:");
        _text.AppendLine($"    cmp  byte [{ptrReg}+{outReg}], 0");
        _text.AppendLine($"    je   __slen_done_{id}");
        _text.AppendLine($"    inc  {outReg}");
        _text.AppendLine($"    jmp  __slen_{id}");
        _text.AppendLine($"__slen_done_{id}:");
    }

    // ---------------------------------------------------------------- array
    private void EmitArrayDecl(ArrayDeclaration a)
    {
        // allocate size * 8 bytes on heap
        EmitExpr(a.Size);
        _text.AppendLine("    imul rax, 8");
        _text.AppendLine("    mov  rcx, rax");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    call __heap_alloc");
        _text.AppendLine("    add  rsp, 40");
        StoreVar(a.Name, "rax");

        _globalVarType[a.Name] = a.ElementType + "[]";
    }

    private void EmitArrayLoad(ArrayAccess a)
    {
        LoadVar(a.Name, "rax");          // base ptr
        _text.AppendLine("    push rax");
        EmitExpr(a.Index);               // index
        _text.AppendLine("    imul rax, 8");
        _text.AppendLine("    pop  rbx");
        _text.AppendLine("    add  rbx, rax");
        _text.AppendLine("    mov  rax, [rbx]");
    }

    private void EmitArrayAssign(ArrayAssignment a)
    {
        LoadVar(a.Name, "rcx");          // base ptr
        _text.AppendLine("    push rcx");
        EmitExpr(a.Index);
        _text.AppendLine("    imul rax, 8");
        _text.AppendLine("    pop  rbx");
        _text.AppendLine("    add  rbx, rax");   // address of element
        _text.AppendLine("    push rbx");
        EmitExpr(a.Value);               // value to store
        _text.AppendLine("    pop  rbx");
        _text.AppendLine("    mov  [rbx], rax");
    }

    // ---------------------------------------------------------------- var decl
    private void EmitVarDecl(VariableDeclaration v)
    {
        bool isLocal = _currentFuncRetLabel != null;

        if (isLocal)
        {
            int offset = _scope.Declare(v.Name, v.Type);
            if (v.Init == null)
            {
                _text.AppendLine($"    mov  qword [rbp{offset}], 0");
                return;
            }
            EmitExpr(v.Init);
            _text.AppendLine($"    mov  qword [rbp{offset}], rax  ; {v.Name}");
        }
        else
        {
            _globalVarType[v.Name] = v.Type;
            if (v.Init == null) return;
            EmitExpr(v.Init);
            _text.AppendLine($"    mov  [rel {v.Name}], rax");
        }
    }

    // ------------------------------------------------------------------ print
    private void EmitPrint(PrintStatement p)
    {
        if (p.Segments != null)
        {
            // interpolated — emit each segment, newline only after the last one
            for (int i = 0; i < p.Segments.Count; i++)
            {
                var seg      = p.Segments[i];
                bool isLast  = i == p.Segments.Count - 1;

                if (seg.Text != null)
                {
                    // emit text segment; if last add newline
                    string lbl = GetOrAddString(seg.Text, newline: isLast);
                    int    len = Encoding.ASCII.GetByteCount(seg.Text) + (isLast ? 1 : 0);
                    if (len > 0) EmitWriteConsole(lbl, len);
                }
                else if (seg.Expr != null)
                {
                    EmitExpr(seg.Expr);
                    bool isString = IsStringExpr(seg.Expr);
                    if (isString)
                    {
                        _text.AppendLine("    mov  rdx, rax");
                        if (isLast) EmitWriteStringRdxWithNewline();
                        else        EmitWriteConsoleRdx();
                    }
                    else
                    {
                        // int — pass addNewline only if last segment
                        EmitWriteInt(addNewline: isLast);
                    }
                }
            }

            // if last segment was an expression there's already a newline above
            // if segments is empty, just print newline
            if (p.Segments.Count == 0)
            {
                string lbl = GetOrAddString("", newline: true);
                EmitWriteConsole(lbl, 1);
            }
            return;
        }

        if (p.Literal != null)
        {
            // handle \n and \t escape sequences in the literal
            string escaped = p.Literal
                .Replace("\\n", "\n")
                .Replace("\\t", "\t");
            string lbl = GetOrAddString(escaped, newline: true);
            int    len = Encoding.ASCII.GetByteCount(escaped) + 1;
            EmitWriteConsole(lbl, len);
            return;
        }

        if (p.VarName != null)
        {
            string type = GetVarType(p.VarName);
            if (type == "string")
            {
                LoadVar(p.VarName, "rdx");
                EmitWriteStringRdxWithNewline();
            }
            else
            {
                LoadVar(p.VarName, "rax");
                EmitWriteInt(addNewline: true);
            }
        }
    }
    private void EmitWriteStringRdxWithNewline()
    {
        EmitWriteConsoleRdx();                              // write the string
        string nlLabel = GetOrAddString("", newline: true);
        EmitWriteConsole(nlLabel, 1);                       // write \n separately
    }
    // ------------------------------------------------------------------ input
    private void EmitInput(InputStatement inp)
    {
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

        string type = GetVarType(inp.VarName);
        if (type is "int" or "float")
        {
            EmitStrToInt();
            StoreVar(inp.VarName, "rax");
        }
        else
        {
            _text.AppendLine("    lea  rax, [rel inputChar]");
            StoreVar(inp.VarName, "rax");
        }
    }

    // ------------------------------------------------------------------ if
    private void EmitIf(IfStatement i)
    {
        int    id     = _labelCount++;
        string lblEnd = $"__endif_{id}";
        string firstFalse = i.ElseIfs.Count > 0 ? $"__elseif_{id}_0"
                          : i.ElseBranch.Count > 0 ? $"__else_{id}" : lblEnd;

        EmitConditionJump(i.Condition, firstFalse);
        _scope.EnterBlock();
        foreach (var e in i.ThenBranch) Emit(e);
        _scope.ExitBlock();
        _text.AppendLine($"    jmp {lblEnd}");

        for (int k = 0; k < i.ElseIfs.Count; k++)
        {
            string nextLbl = k + 1 < i.ElseIfs.Count ? $"__elseif_{id}_{k + 1}"
                           : i.ElseBranch.Count > 0 ? $"__else_{id}" : lblEnd;
            _text.AppendLine($"__elseif_{id}_{k}:");
            EmitConditionJump(i.ElseIfs[k].Condition, nextLbl);
            _scope.EnterBlock();
            foreach (var e in i.ElseIfs[k].Body) Emit(e);
            _scope.ExitBlock();
            _text.AppendLine($"    jmp {lblEnd}");
        }

        if (i.ElseBranch.Count > 0)
        {
            _text.AppendLine($"__else_{id}:");
            _scope.EnterBlock();
            foreach (var e in i.ElseBranch) Emit(e);
            _scope.ExitBlock();
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
        _scope.EnterBlock();
        foreach (var e in w.Body) Emit(e);
        _scope.ExitBlock();
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

        int offset = _scope.Declare(f.VarName, "int");
        EmitExpr(f.From);
        _text.AppendLine($"    mov  qword [rbp{offset}], rax");

        _loopLabels.Push((lblEnd, lblContinue));
        _text.AppendLine($"{lblStart}:");
        _text.AppendLine($"    mov  rax, [rbp{offset}]");
        EmitExpr(f.To);
        _text.AppendLine("    mov  rbx, rax");
        _text.AppendLine($"    cmp  qword [rbp{offset}], rbx");
        _text.AppendLine($"    jge  {lblEnd}");

        _scope.EnterBlock();
        foreach (var e in f.Body) Emit(e);
        _scope.ExitBlock();

        _text.AppendLine($"{lblContinue}:");
        EmitExpr(f.Step);
        _text.AppendLine($"    add  qword [rbp{offset}], rax");
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

        _scope.EnterBlock();
        foreach (var e in t.TryBody) Emit(e);
        _scope.ExitBlock();
        _text.AppendLine($"    jmp {lblFinally}");
        _text.AppendLine($"{lblCatch}:");
        _scope.EnterBlock();
        foreach (var e in t.CatchBody) Emit(e);
        _scope.ExitBlock();
        _text.AppendLine($"{lblFinally}:");
        _scope.EnterBlock();
        foreach (var e in t.FinallyBody) Emit(e);
        _scope.ExitBlock();
    }

    // ---------------------------------------------------------------- functions
    private void EmitFunctionBody(FunctionDeclaration fn)
    {
        string retLabel = $"__ret_{fn.Name}";
        _currentFuncRetLabel = retLabel;
        _scope.EnterFunction();
        _text = _helpers;

        _text.AppendLine($"; ---- {fn.Access} {fn.ReturnType} {fn.Name} ----");
        _text.AppendLine($"__fn_{fn.Name}:");
        _text.AppendLine("    push rbp");
        _text.AppendLine("    mov  rbp, rsp");

        int localCount = fn.Parameters.Count + CountLocals(fn.Body);
        localCount     = Math.Max(localCount, 4);
        int frameSize  = (localCount * 8 + 15) & ~15;
        _text.AppendLine($"    sub  rsp, dword {frameSize}");

        string[] paramRegs = { "rcx", "rdx", "r8", "r9" };
        for (int i = 0; i < fn.Parameters.Count && i < 4; i++)
        {
            int offset = _scope.Declare(fn.Parameters[i].Name, fn.Parameters[i].Type);
            _text.AppendLine($"    mov  [rbp{offset}], {paramRegs[i]}");
        }

        foreach (var e in fn.Body) Emit(e);

        _text.AppendLine($"{retLabel}:");
        _text.AppendLine($"    add  rsp, dword {frameSize}");
        _text.AppendLine("    pop  rbp");
        _text.AppendLine("    ret");
        _text.AppendLine();

        _scope.ExitFunction();
        _text = _main;
        _currentFuncRetLabel = null;
    }

    private void EmitCallExpr(CallExpr fc, string resultReg)
    {
        if (EmitStdLibCall(fc, resultReg)) return;

        if (!_symbols.FunctionExists(fc.Name))
            throw new Exception($"Call to undefined function '{fc.Name}'");

        string actualName  = _symbols.Functions[fc.Name].Name;
        string[] paramRegs = { "rcx", "rdx", "r8", "r9" };

        // push args onto stack first (they may reference locals which use rbp)
        // evaluate each arg and push, then pop into param registers
        for (int i = fc.Args.Count - 1; i >= 0; i--)
        {
            EmitExpr(fc.Args[i]);
            _text.AppendLine("    push rax");
        }
        for (int i = 0; i < fc.Args.Count && i < 4; i++)
            _text.AppendLine($"    pop  {paramRegs[i]}");

        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine($"    call __fn_{actualName}");
        _text.AppendLine("    add  rsp, 40");

        if (resultReg != "rax")
            _text.AppendLine($"    mov  {resultReg}, rax");
    }

    // ------------------------------------------------------------------ stdlib
    private bool EmitStdLibCall(CallExpr fc, string resultReg)
    {
        switch (fc.Name)
        {
            case "exit":
                if (fc.Args.Count > 0) EmitExpr(fc.Args[0]);
                else _text.AppendLine("    mov  rax, 0");
                _text.AppendLine("    mov  rcx, rax");
                _text.AppendLine("    sub  rsp, 40");
                _text.AppendLine("    call ExitProcess");
                _text.AppendLine("    add  rsp, 40");
                return true;

            case "math_abs":
            {
                int id = _labelCount++;
                EmitExpr(fc.Args[0]);
                _text.AppendLine("    test rax, rax");
                _text.AppendLine($"    jns  __abs_{id}");
                _text.AppendLine("    neg  rax");
                _text.AppendLine($"__abs_{id}:");
                if (resultReg != "rax") _text.AppendLine($"    mov  {resultReg}, rax");
                return true;
            }
            case "math_max":
            {
                int id = _labelCount++;
                EmitExpr(fc.Args[0]); _text.AppendLine("    push rax");
                EmitExpr(fc.Args[1]); _text.AppendLine("    mov  rbx, rax");
                _text.AppendLine("    pop  rax");
                _text.AppendLine("    cmp  rax, rbx");
                _text.AppendLine($"    jge  __max_{id}");
                _text.AppendLine("    mov  rax, rbx");
                _text.AppendLine($"__max_{id}:");
                if (resultReg != "rax") _text.AppendLine($"    mov  {resultReg}, rax");
                return true;
            }
            case "math_min":
            {
                int id = _labelCount++;
                EmitExpr(fc.Args[0]); _text.AppendLine("    push rax");
                EmitExpr(fc.Args[1]); _text.AppendLine("    mov  rbx, rax");
                _text.AppendLine("    pop  rax");
                _text.AppendLine("    cmp  rax, rbx");
                _text.AppendLine($"    jle  __min_{id}");
                _text.AppendLine("    mov  rax, rbx");
                _text.AppendLine($"__min_{id}:");
                if (resultReg != "rax") _text.AppendLine($"    mov  {resultReg}, rax");
                return true;
            }
            case "str_len":
            {
                EmitExpr(fc.Args[0]);
                _text.AppendLine("    mov  rsi, rax");
                EmitStrLen("rsi", "rax");
                if (resultReg != "rax") _text.AppendLine($"    mov  {resultReg}, rax");
                return true;
            }
            case "str":
            {
                // str(x) → convert int to string, return pointer
                EmitExpr(fc.Args[0]);
                _text.AppendLine("    call __intToStr");
                _text.AppendLine("    mov  rax, rdx");   // return the pointer
                if (resultReg != "rax") _text.AppendLine($"    mov  {resultReg}, rax");
                return true;
            }
            default: return false;
        }
    }

    // ------------------------------------------------------------------ return
    private void EmitReturn(ReturnStatement r)
    {
        if (r.Value != null)
            EmitExpr(r.Value);
        // if void (Value == null), rax is whatever it was — caller ignores it for void fns

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
            case "++": LoadVar(a.VarName, "rax"); _text.AppendLine("    inc  rax"); StoreVar(a.VarName, "rax"); break;
            case "--": LoadVar(a.VarName, "rax"); _text.AppendLine("    dec  rax"); StoreVar(a.VarName, "rax"); break;
            case "=":
                EmitExpr(a.Value!);
                StoreVar(a.VarName, "rax");
                break;
            default:
                LoadVar(a.VarName, "rax");
                _text.AppendLine("    push rax");
                EmitExpr(a.Value!);
                _text.AppendLine("    mov  rbx, rax");
                _text.AppendLine("    pop  rax");
                EmitArith(a.Operator.TrimEnd('=') switch
                {
                    "+" => "+", "-" => "-", "*" => "*",
                    "/" => "/", "%" => "%",
                    _   => throw new Exception($"Unknown op: {a.Operator}")
                });
                StoreVar(a.VarName, "rax");
                break;
        }
    }

    // ------------------------------------------------------------------ condition
    private void EmitConditionJump(Condition c, string falseLabel)
    {
        // boolean condition: if b:  or  if not b:
        if (c.Op == "bool")
        {
            EmitExpr(c.Left);
            _text.AppendLine("    test rax, rax");
            string j = c.Negated ? "jnz" : "jz";
            _text.AppendLine($"    {j}  {falseLabel}");
            return;
        }

        EmitExpr(c.Left);
        _text.AppendLine("    push rax");
        EmitExpr(c.Right!);
        _text.AppendLine("    mov  rbx, rax");
        _text.AppendLine("    pop  rax");
        _text.AppendLine("    cmp  rax, rbx");

        string baseOp = c.Op.Contains("&&") ? c.Op.Split("&&")[0]
            : c.Op.Contains("||") ? c.Op.Split("||")[0]
            : c.Op;

        string jmp = baseOp switch
        {
            "==" => "jne", "!=" => "je",
            "<"  => "jge", ">"  => "jle",
            "<=" => "jg",  ">=" => "jl",
            _    => throw new Exception($"Unknown condition op: {baseOp} at line {c.Line}")
        };

        if (c.Negated) jmp = jmp switch
        {
            "jne" => "je",  "je"  => "jne",
            "jge" => "jl",  "jl"  => "jge",
            "jle" => "jg",  "jg"  => "jle",
            _     => jmp
        };

        _text.AppendLine($"    {jmp}  {falseLabel}");
    }

    // ------------------------------------------------------------------ pause
    private void EmitPause()
    {
        const string txt = "Press Enter to exit...";
        string lbl = GetOrAddString(txt, newline: true);
        EmitWriteConsole(lbl, Encoding.ASCII.GetByteCount(txt) + 1);
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
    private void EmitWriteInt(bool addNewline)
    {
        _text.AppendLine("    call __intToStr");
        // rdx = ptr, ecx = len (no newline)
        if (addNewline)
        {
            // write digits
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
            // write newline
            string nlLabel = GetOrAddString("", newline: true);
            EmitWriteConsole(nlLabel, 1);
        }
        else
        {
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

    private void EmitCallIntToStr()
    {
        _text.AppendLine("    call __intToStr");
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

    private void EmitIntToStrHelper()
    {
        _helpers.AppendLine("""
                            ; ================================================================= __intToStr
                            ; in:  rax = int64
                            ; out: rdx = pointer to digit string (NO newline), ecx = byte count
                            __intToStr:
                                push rbx
                                push rdi
                                push rsi
                                lea  rsi, [rel convBuf]
                                add  rsi, 29              ; point near end of buffer
                                mov  byte [rsi+1], 0      ; null terminator (no newline)
                                xor  rdi, rdi             ; rdi = 0 (positive flag)
                                test rax, rax
                                jns  __its_pos
                                neg  rax
                                mov  rdi, 1               ; negative
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
                                test rdi, rdi
                                jz   __its_no_minus
                                mov  byte [rsi], '-'
                                dec  rsi
                            __its_no_minus:
                                inc  rsi                  ; rsi = first char
                                mov  rdx, rsi             ; rdx = pointer to string
                                lea  rcx, [rel convBuf]
                                add  rcx, 30              ; one past last digit slot
                                sub  rcx, rsi             ; ecx = length (no newline)
                                pop  rsi
                                pop  rdi
                                pop  rbx
                                ret
                            ; =================================================================
                            """);
    }

    private void EmitStrCatHelper()
    {
        // nothing needed — inline emit in EmitStringConcat
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

    // ================================================================= var helpers
    private void LoadVar(string name, string reg)
    {
        var local = _scope.Lookup(name);
        if (local.HasValue)
        { _text.AppendLine($"    mov  {reg}, [rbp{local.Value.offset}]"); return; }
        if (_globalVarType.ContainsKey(name))
        { _text.AppendLine($"    mov  {reg}, [rel {name}]"); return; }
        throw new Exception($"Undefined variable '{name}'");
    }

    private void StoreVar(string name, string reg)
    {
        var local = _scope.Lookup(name);
        if (local.HasValue)
        { _text.AppendLine($"    mov  [rbp{local.Value.offset}], {reg}"); return; }
        if (_globalVarType.ContainsKey(name))
        { _text.AppendLine($"    mov  [rel {name}], {reg}"); return; }
        throw new Exception($"Undefined variable '{name}'");
    }

    private string GetVarType(string name)
    {
        var local = _scope.Lookup(name);
        if (local.HasValue) return local.Value.type;
        if (_globalVarType.TryGetValue(name, out var t)) return t;
        return "int";
    }

    private void EmitArith(string op)
    {
        switch (op)
        {
            case "+": _text.AppendLine("    add  rax, rbx"); break;
            case "-": _text.AppendLine("    sub  rax, rbx"); break;
            case "*": _text.AppendLine("    imul rax, rbx"); break;
            case "/": _text.AppendLine("    cqo"); _text.AppendLine("    idiv rbx"); break;
            case "%": _text.AppendLine("    cqo"); _text.AppendLine("    idiv rbx");
                      _text.AppendLine("    mov  rax, rdx"); break;
        }
    }

    // ---------------------------------------------------------------- reserve globals
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
                    _bss.AppendLine($"{a.Name}: resq 1");   // stores heap pointer
                    _globalVarType[a.Name] = a.ElementType + "[]";
                    break;
                case ClassDeclaration cls:
                    ReserveGlobals(cls.Body);
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
                case IfStatement i:
                    int thenC = CountLocals(i.ThenBranch);
                    int elseC = CountLocals(i.ElseBranch);
                    foreach (var ei in i.ElseIfs) elseC = Math.Max(elseC, CountLocals(ei.Body));
                    count += Math.Max(thenC, elseC);
                    break;
                case WhileStatement w: count += CountLocals(w.Body); break;
                case ForStatement f:
                    count++;
                    count += CountLocals(f.Body);
                    break;
                case TryStatement t:
                    count += Math.Max(CountLocals(t.TryBody), CountLocals(t.CatchBody));
                    count += CountLocals(t.FinallyBody);
                    break;
            }
        }
        return count;
    }

    // ---------------------------------------------------------------- strings
    private string GetOrAddString(string text, bool newline)
    {
        string key = text + (newline ? "\n" : "");
        if (_strMap.TryGetValue(key, out string? existing)) return existing;

        _strCount++;
        string label = $"msg{_strCount}";

        // build NASM db directive handling special chars
        var sb      = new System.Text.StringBuilder();
        sb.Append($"{label}: db ");

        bool inString = false;
        foreach (char ch in text)
        {
            if (ch == '\n' || ch == '\t' || ch == '"')
            {
                if (inString) { sb.Append("\","); inString = false; }
                string code = ch == '\n' ? "0xA" : ch == '\t' ? "0x9" : "0x22";
                sb.Append($"{code},");
            }
            else
            {
                if (!inString) { sb.Append('"'); inString = true; }
                sb.Append(ch);
            }
        }

        if (inString) sb.Append('"');
        else if (text.Length > 0) sb.Length--;  // remove trailing comma if no open string

        if (newline)
        {
            if (text.Length > 0) sb.Append(",0xA,0");
            else                 sb.Append("0xA,0");
        }
        else
        {
            if (text.Length > 0) sb.Append(",0");
            else                 sb.Append("0");
        }

        _data.AppendLine(sb.ToString());
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
        sb.Append(_main);
        sb.AppendLine();
        sb.Append(_helpers);
        return sb.ToString();
    }
}