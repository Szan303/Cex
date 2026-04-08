using System;
using System.Collections.Generic;
using System.Text;
using Cex.AST;
using Cex.Compiler;

namespace Cex;

public partial class CodeGenerator
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

    private readonly SymbolTable _symbols;
    private readonly ScopeStack  _scope      = new();
    private readonly Stack<(string breakLbl, string continueLbl)> _loopLabels = new();

    private string? _currentFuncRetLabel = null;
    private string? _currentFuncRetType  = null;

    private string? _currentClassName = null;
    private string? _currentThisReg   = null;

    public CodeGenerator(SymbolTable symbols)
    {
        _symbols = symbols;
        _text    = _main;
    }

    // ================================================================= Generate
    public string Generate(List<CompilationUnit> units)
    {
        _bss.AppendLine("written   resd 1");
        _bss.AppendLine("inputChar resb 256");
        _bss.AppendLine("convBuf   resb 32");
        _bss.AppendLine("charBuf   resb 2");
        ArenaAllocator.EmitBss(_bss);

        foreach (var unit in units)
            ReserveGlobals(unit.Expressions);

        EmitIntToStrHelper();
        EmitFloatPrintHelper();
        ArenaAllocator.EmitHelpers(_helpers);

        GetOrAddString("null", newline: false);

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
        foreach (var cls in _symbols.ClassInfoByName.Values)
            if (cls.Constructor != null)
                EmitConstructor(cls);
        return BuildOutput();
    }
    private void Emit(Expression expr)
    {
        switch (expr)
        {
            case FunctionDeclaration:    break;
            case ImportStatement:        break;
            case FieldDeclaration:       break;
            case ConstructorDeclaration: break;
            case ArrayAddStatement a:    EmitArrayAdd(a);    break;
            case ArrayDeleteStatement d: EmitArrayDelete(d); break;
            case VariableDeclaration v: EmitVarDecl(v);     break;
            case ArrayDeclaration a:    EmitArrayDecl(a);   break;
            case ArrayAssignment a:     EmitArrayAssign(a); break;
            case AssignmentStatement a: EmitAssignment(a);  break;
            case CheckpointStatement c: _text.AppendLine($"{c.Name}:"); break;
            case GotoStatement g:       _text.AppendLine($"    jmp {g.TargetName}"); break;
            case PrintStatement p:      EmitPrint(p);       break;
            case InputStatement i:      EmitInput(i);       break;
            case IfStatement i:         EmitIf(i);          break;
            case WhileStatement w:      EmitWhile(w);       break;
            case ForStatement f:        EmitFor(f);         break;
            case BreakStatement:        EmitBreak();        break;
            case ContinueStatement:     EmitContinue();     break;
            case ReturnStatement r:     EmitReturn(r);      break;
            case TryStatement t:        EmitTry(t);         break;
            case FunctionCall fc:
                EmitCallExpr(new CallExpr { Name = fc.Name, Args = fc.Arguments }, "rax");
                break;
            case AsmBlock a:
                foreach (var line in a.Lines)
                    _text.AppendLine($"    {line}");
                if (_currentFuncRetLabel != null)
                    _text.AppendLine($"    jmp {_currentFuncRetLabel}");
                break;
            case ClassDeclaration cls:
                foreach (var e in cls.Body)
                    if (e is not FunctionDeclaration) Emit(e);
                break;
        }
    }
    private void EmitMemberAccess(MemberAccessExpr ma)
    {
        // Only support "VariableExpr.field" for now
        if (ma.Target is not VariableExpr ve)
            throw new Exception($"Unsupported member target '{ma.Target.GetType().Name}' at line {ma.Line}");

        // Load object pointer into rax
        LoadVar(ve.Name, "rax", ma.Line);

        // Determine class type of the variable
        string varType = GetVarType(ve.Name, ma.Line);

        if (!_symbols.ClassInfoByName.TryGetValue(varType, out var cls))
            throw new Exception($"Type '{varType}' is not a class (cannot access .{ma.MemberName}) at line {ma.Line}");

        if (!cls.InstanceFields.TryGetValue(ma.MemberName, out var field))
            throw new Exception($"Class '{varType}' has no field '{ma.MemberName}' at line {ma.Line}");

        // rax = *(obj + offset)
        _text.AppendLine($"    mov  rax, [rax+{field.offset}]");
    }
    // ================================================================= EmitExpr
    private void EmitExpr(Expression expr)
    {
        switch (expr)
        {
            case NumberLiteral n:
                _text.AppendLine($"    mov  rax, {n.Value}");
                break;
            case CreateExpr c:
                EmitCreateExpr(c);
                break;
            case MemberAccessExpr ma:
                EmitMemberAccess(ma);
                break;
            case BoolLiteral b:
                _text.AppendLine($"    mov  rax, {(b.Value ? 1 : 0)}");
                break;
            case CharLiteral ch:
                _text.AppendLine($"    mov  rax, {ch.Value}");
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
                throw new Exception($"Cannot evaluate expression '{expr.GetType().Name}' at line {expr.Line}");
        }
    }

    // ================================================================= type helpers
    // Returns the byte size for a given type name
    private static int SizeOf(string type) => type switch
    {
        "byte"  => 1,
        "short" => 2,
        "int"   => 4,
        _       => 8,   // long, float, double, string, bool, char, etc.
    };

    // Truncate rax to the correct signed range for the given type
    private void EmitTruncate(string type)
    {
        switch (type)
        {
            case "byte":  _text.AppendLine("    movsx rax, al");   break; // sign-extend 8-bit
            case "short": _text.AppendLine("    movsx rax, ax");   break; // sign-extend 16-bit
            case "int":   _text.AppendLine("    movsxd rax, eax"); break; // sign-extend 32-bit
            // long and everything else — no truncation needed, already 64-bit
        }
    }

    // ================================================================= EmitBinary
    private void EmitBinary(BinaryExpr b)
    {
        if (b.Op == "+" && (IsStringExpr(b.Left) || IsStringExpr(b.Right)))
        {
            EmitStringConcat(b);
            return;
        }

        if ((b.Op == "==" || b.Op == "!=") && (IsStringExpr(b.Left) || IsStringExpr(b.Right)))
        {
            EmitStringEquals(b);
            if (b.Op == "!=")
            {
                _text.AppendLine("    test rax, rax");
                _text.AppendLine("    setz al");
                _text.AppendLine("    movzx rax, al");
            }
            return;
        }

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
            case "/":  _text.AppendLine("    cqo"); _text.AppendLine("    idiv rbx"); break;
            case "%":  _text.AppendLine("    cqo"); _text.AppendLine("    idiv rbx"); _text.AppendLine("    mov  rax, rdx"); break;
            case "==": EmitCmpResult("sete");  break;
            case "!=": EmitCmpResult("setne"); break;
            case "<":  EmitCmpResult("setl");  break;
            case ">":  EmitCmpResult("setg");  break;
            case "<=": EmitCmpResult("setle"); break;
            case ">=": EmitCmpResult("setge"); break;
            case "&&":
                _text.AppendLine("    test rax, rax"); _text.AppendLine("    setnz al");
                _text.AppendLine("    test rbx, rbx"); _text.AppendLine("    setnz bl");
                _text.AppendLine("    and  al, bl");   _text.AppendLine("    movzx rax, al");
                break;
            case "||":
                _text.AppendLine("    or   rax, rbx");
                _text.AppendLine("    setnz al");
                _text.AppendLine("    movzx rax, al");
                break;
            default:
                throw new Exception($"Unknown binary operator '{b.Op}' at line {b.Line}");
        }
    }

    private void EmitCmpResult(string setInstr)
    {
        _text.AppendLine("    cmp  rax, rbx");
        _text.AppendLine($"    {setInstr} al");
        _text.AppendLine("    movzx rax, al");
    }

    // ================================================================= string helpers
    private string GetMemberAccessType(MemberAccessExpr ma)
    {
        if (ma.Target is not VariableExpr ve)
            return "long"; // fallback

        string objType = GetVarType(ve.Name, ma.Line);

        if (!_symbols.ClassInfoByName.TryGetValue(objType, out var cls))
            return "long";

        if (cls.InstanceFields.TryGetValue(ma.MemberName, out var field))
            return field.type;

        // could also allow static fields later
        return "long";
    }
    private bool IsStringExpr(Expression e) =>
        e is StringLiteralExpr ||
        (e is VariableExpr ve && GetVarType(ve.Name) == "string") ||
        (e is MemberAccessExpr ma && GetMemberAccessType(ma) == "string");

    private bool IsStringArg(Expression e) =>
        e is StringLiteralExpr ||
        (e is VariableExpr ve && GetVarType(ve.Name) == "string");

    private bool IsCharExpr(Expression e) =>
        e is CharLiteral ||
        (e is VariableExpr ve && GetVarType(ve.Name) == "char");

    private void EmitStringEquals(BinaryExpr b)
    {
        int id = _labelCount++;
        EmitToStringPtr(b.Left);
        _text.AppendLine("    push rax");
        EmitToStringPtr(b.Right);
        _text.AppendLine("    mov  rbx, rax");
        _text.AppendLine("    pop  rsi");
        _text.AppendLine($"__streq_loop_{id}:");
        _text.AppendLine("    mov  al,  [rsi]");
        _text.AppendLine("    mov  cl,  [rbx]");
        _text.AppendLine("    cmp  al,  cl");
        _text.AppendLine($"    jne  __streq_no_{id}");
        _text.AppendLine("    test al,  al");
        _text.AppendLine($"    jz   __streq_yes_{id}");
        _text.AppendLine("    inc  rsi");
        _text.AppendLine("    inc  rbx");
        _text.AppendLine($"    jmp  __streq_loop_{id}");
        _text.AppendLine($"__streq_yes_{id}:");
        _text.AppendLine("    mov  rax, 1");
        _text.AppendLine($"    jmp  __streq_done_{id}");
        _text.AppendLine($"__streq_no_{id}:");
        _text.AppendLine("    xor  rax, rax");
        _text.AppendLine($"__streq_done_{id}:");
    }

    private void EmitToStringPtr(Expression expr)
    {
        if (IsStringExpr(expr)) { EmitExpr(expr); return; }

        int id = _labelCount++;
        EmitExpr(expr);
        _text.AppendLine("    call __intToStr");
        _text.AppendLine("    push rdx");
        _text.AppendLine("    movzx rax, cx");
        _text.AppendLine("    inc  rax");
        _text.AppendLine("    mov  rcx, rax");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    call __heap_alloc");
        _text.AppendLine("    add  rsp, 40");
        _text.AppendLine("    push rax");
        _text.AppendLine("    mov  rdi, rax");
        _text.AppendLine("    mov  rsi, [rsp+8]");
        _text.AppendLine($"__icpy_{id}:");
        _text.AppendLine("    mov  al, [rsi]");
        _text.AppendLine("    mov  [rdi], al");
        _text.AppendLine("    test al, al");
        _text.AppendLine($"    jz   __icpy_done_{id}");
        _text.AppendLine("    inc  rsi");
        _text.AppendLine("    inc  rdi");
        _text.AppendLine($"    jmp  __icpy_{id}");
        _text.AppendLine($"__icpy_done_{id}:");
        _text.AppendLine("    pop  rax");
        _text.AppendLine("    add  rsp, 8");
    }

    private void EmitStringConcat(BinaryExpr b)
    {
        int id = _labelCount++;
        EmitToStringPtr(b.Left);
        _text.AppendLine("    push rax");
        EmitToStringPtr(b.Right);
        _text.AppendLine("    push rax");
        _text.AppendLine("    pop  rbx");
        _text.AppendLine("    pop  rax");
        _text.AppendLine("    mov  rsi, rax");
        _text.AppendLine("    xor  rcx, rcx");
        _text.AppendLine($"__slen_l_{id}:");
        _text.AppendLine("    cmp  byte [rsi+rcx], 0");
        _text.AppendLine($"    je   __slen_l_done_{id}");
        _text.AppendLine("    inc  rcx");
        _text.AppendLine($"    jmp  __slen_l_{id}");
        _text.AppendLine($"__slen_l_done_{id}:");
        _text.AppendLine("    mov  rsi, rbx");
        _text.AppendLine("    xor  rdx, rdx");
        _text.AppendLine($"__slen_r_{id}:");
        _text.AppendLine("    cmp  byte [rsi+rdx], 0");
        _text.AppendLine($"    je   __slen_r_done_{id}");
        _text.AppendLine("    inc  rdx");
        _text.AppendLine($"    jmp  __slen_r_{id}");
        _text.AppendLine($"__slen_r_done_{id}:");
        _text.AppendLine("    push rax");
        _text.AppendLine("    push rbx");
        _text.AppendLine("    push rcx");
        _text.AppendLine("    push rdx");
        _text.AppendLine("    mov  rcx, [rsp]");
        _text.AppendLine("    add  rcx, [rsp+8]");
        _text.AppendLine("    inc  rcx");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    call __heap_alloc");
        _text.AppendLine("    add  rsp, 40");
        _text.AppendLine("    push rax");
        _text.AppendLine("    mov  rdi, rax");
        _text.AppendLine("    mov  rsi, [rsp+32]");
        _text.AppendLine($"__scat_l_{id}:");
        _text.AppendLine("    mov  al, [rsi]");
        _text.AppendLine("    test al, al");
        _text.AppendLine($"    jz   __scat_m_{id}");
        _text.AppendLine("    mov  [rdi], al");
        _text.AppendLine("    inc  rsi");
        _text.AppendLine("    inc  rdi");
        _text.AppendLine($"    jmp  __scat_l_{id}");
        _text.AppendLine($"__scat_m_{id}:");
        _text.AppendLine("    mov  rsi, [rsp+24]");
        _text.AppendLine($"__scat_r_{id}:");
        _text.AppendLine("    mov  al, [rsi]");
        _text.AppendLine("    mov  [rdi], al");
        _text.AppendLine("    test al, al");
        _text.AppendLine($"    jz   __scat_done_{id}");
        _text.AppendLine("    inc  rsi");
        _text.AppendLine("    inc  rdi");
        _text.AppendLine($"    jmp  __scat_r_{id}");
        _text.AppendLine($"__scat_done_{id}:");
        _text.AppendLine("    pop  rax");
        _text.AppendLine("    add  rsp, 32");
    }

    // ================================================================= float helpers
    private bool IsFloatType(string type) => type is "float" or "double";

    private bool IsFloatVar(string name)
    {
        var local = _scope.Lookup(name);
        if (local.HasValue) return IsFloatType(local.Value.type);
        if (_globalVarType.TryGetValue(name, out var gt)) return IsFloatType(gt);
        return false;
    }

    private bool IsFloatExpr(Expression e)
    {
        if (e is VariableExpr ve) return IsFloatVar(ve.Name);
        if (e is CallExpr ce && _symbols.FunctionExists(ce.Name))
            return IsFloatType(_symbols.Functions[ce.Name].ReturnType);
        return false;
    }

    private void EmitFloatPrintHelper()
    {
        _helpers.AppendLine("""
; ================================================================= __printFloat
; in: xmm0 = double
; prints value with 3 decimal places e.g. 1.414
__printFloat:
    push rbx
    push rdi
    sub  rsp, 40

    ; handle negative
    xorpd   xmm1, xmm1
    ucomisd xmm0, xmm1
    jae     __pf_pos
    ; print '-'
    mov  rcx, -11
    call GetStdHandle
    mov  rcx, rax
    lea  rdx, [rel __pf_minus]
    mov  r8d, 1
    lea  r9,  [rel written]
    mov  qword [rsp+32], 0
    call WriteConsoleA
    ; negate xmm0
    mov  rax, 0x8000000000000000
    movq xmm1, rax
    xorpd xmm0, xmm1

__pf_pos:
    cvttsd2si rax, xmm0
    push rax

    cvtsi2sd  xmm1, rax
    subsd     xmm0, xmm1
    mov       rax, 1000
    cvtsi2sd  xmm1, rax
    mulsd     xmm0, xmm1
    cvttsd2si rax, xmm0
    push rax

    mov  rax, [rsp+8]
    call __intToStr
    push rdx
    push rcx
    mov  rcx, -11
    call GetStdHandle
    pop  r8
    pop  rdx
    mov  rcx, rax
    lea  r9,  [rel written]
    mov  qword [rsp+32], 0
    call WriteConsoleA

    mov  rcx, -11
    call GetStdHandle
    mov  rcx, rax
    lea  rdx, [rel __pf_dot]
    mov  r8d, 1
    lea  r9,  [rel written]
    mov  qword [rsp+32], 0
    call WriteConsoleA

    pop  rax
    pop  rbx

    lea  rdi, [rel convBuf]
    mov  rbx, 100
    xor  rcx, rcx

__pf_digit:
    cqo
    idiv rbx
    add  al, '0'
    mov  [rdi+rcx], al
    inc  rcx
    mov  rax, rdx
    mov  rdx, 0
    cmp  rbx, 1
    je   __pf_digit_done
    mov  rbx, 10
    cmp  rcx, 2
    jl   __pf_digit
    add  al, '0'
    mov  [rdi+rcx], al
    inc  rcx
__pf_digit_done:
    mov  byte [rdi+3], 0

    mov  rcx, -11
    call GetStdHandle
    mov  rcx, rax
    lea  rdx, [rel convBuf]
    mov  r8d, 3
    lea  r9,  [rel written]
    mov  qword [rsp+32], 0
    call WriteConsoleA

    add  rsp, 40
    pop  rdi
    pop  rbx
    ret
; =================================================================
""");
        _data.AppendLine("__pf_dot:   db \".\",0");
        _data.AppendLine("__pf_minus: db \"-\",0");
    }

    private void EmitLoadFloat(string name)
    {
        LoadVar(name, "rax");
        _text.AppendLine("    movq xmm0, rax");
    }

    private void EmitCallPrintFloat(bool addNewline)
    {
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    call __printFloat");
        _text.AppendLine("    add  rsp, 40");
        if (addNewline)
            EmitWriteConsole(GetOrAddString("", newline: true), 1);
    }

    // ================================================================= variable decl
    private void EmitVarDecl(VariableDeclaration v)
    {
        int offset = _scope.Declare(v.Name, v.Type);

        if (v.Init == null)
        {
            _text.AppendLine($"    mov  qword [rbp{offset}], 0");
            return;
        }

        EmitExpr(v.Init);
        EmitTruncate(v.Type);
        _text.AppendLine($"    mov  qword [rbp{offset}], rax");
    }
    // ================================================================= input
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
        EmitTrimInputBuffer();
        string type = GetVarType(inp.VarName);
        if (type is "byte" or "short" or "int" or "long" || IsFloatType(type))
        {
            EmitStrToInt();
            EmitTruncate(type);
            StoreVar(inp.VarName, "rax");
        }
        else if (type == "char")
        {
            _text.AppendLine("    movzx rax, byte [rel inputChar]");
            StoreVar(inp.VarName, "rax");
        }
        else
        {
            _text.AppendLine("    lea  rax, [rel inputChar]");
            StoreVar(inp.VarName, "rax");
        }
    }

    private void EmitTrimInputBuffer()
    {
        int id = _labelCount++;
        _text.AppendLine("    lea  rsi, [rel inputChar]");
        _text.AppendLine("    xor  ecx, ecx");
        _text.AppendLine($"__trim_find_{id}:");
        _text.AppendLine("    cmp  byte [rsi+rcx], 0");
        _text.AppendLine($"    je   __trim_do_{id}");
        _text.AppendLine("    cmp  ecx, 254");
        _text.AppendLine($"    jge  __trim_do_{id}");
        _text.AppendLine("    inc  ecx");
        _text.AppendLine($"    jmp  __trim_find_{id}");
        _text.AppendLine($"__trim_do_{id}:");
        _text.AppendLine($"__trim_loop_{id}:");
        _text.AppendLine("    test ecx, ecx");
        _text.AppendLine($"    jz   __trim_done_{id}");
        _text.AppendLine("    dec  ecx");
        _text.AppendLine("    movzx eax, byte [rsi+rcx]");
        _text.AppendLine("    cmp  eax, 0xA");
        _text.AppendLine($"    je   __trim_zero_{id}");
        _text.AppendLine("    cmp  eax, 0xD");
        _text.AppendLine($"    je   __trim_zero_{id}");
        _text.AppendLine($"    jmp  __trim_done_{id}");
        _text.AppendLine($"__trim_zero_{id}:");
        _text.AppendLine("    mov  byte [rsi+rcx], 0");
        _text.AppendLine($"    jmp  __trim_loop_{id}");
        _text.AppendLine($"__trim_done_{id}:");
    }

    // ================================================================= control flow
    private void EmitIf(IfStatement i)
    {
        int    id         = _labelCount++;
        string lblEnd     = $"__endif_{id}";
        string firstFalse = i.ElseIfs.Count > 0    ? $"__elseif_{id}_0"
                          : i.ElseBranch.Count > 0 ? $"__else_{id}" : lblEnd;

        EmitConditionJump(i.Condition, firstFalse);
        _scope.EnterBlock(); foreach (var e in i.ThenBranch) Emit(e); _scope.ExitBlock();
        _text.AppendLine($"    jmp {lblEnd}");

        for (int k = 0; k < i.ElseIfs.Count; k++)
        {
            string nextLbl = k + 1 < i.ElseIfs.Count ? $"__elseif_{id}_{k + 1}"
                           : i.ElseBranch.Count > 0  ? $"__else_{id}" : lblEnd;
            _text.AppendLine($"__elseif_{id}_{k}:");
            EmitConditionJump(i.ElseIfs[k].Condition, nextLbl);
            _scope.EnterBlock(); foreach (var e in i.ElseIfs[k].Body) Emit(e); _scope.ExitBlock();
            _text.AppendLine($"    jmp {lblEnd}");
        }

        if (i.ElseBranch.Count > 0)
        {
            _text.AppendLine($"__else_{id}:");
            _scope.EnterBlock(); foreach (var e in i.ElseBranch) Emit(e); _scope.ExitBlock();
        }

        _text.AppendLine($"__endif_{id}:");
    }

    private void EmitWhile(WhileStatement w)
    {
        int    id       = _labelCount++;
        string lblStart = $"__while_{id}";
        string lblEnd   = $"__endwhile_{id}";
        _loopLabels.Push((lblEnd, lblStart));
        _text.AppendLine($"{lblStart}:");
        EmitConditionJump(w.Condition, lblEnd);
        _scope.EnterBlock(); foreach (var e in w.Body) Emit(e); _scope.ExitBlock();
        _text.AppendLine($"    jmp {lblStart}");
        _text.AppendLine($"{lblEnd}:");
        _loopLabels.Pop();
    }

    private void EmitFor(ForStatement f)
    {
        int    id          = _labelCount++;
        string lblStart    = $"__for_{id}";
        string lblContinue = $"__forcont_{id}";
        string lblEnd      = $"__endfor_{id}";

        int offset = _scope.Declare(f.VarName, "int");
        EmitExpr(f.From);
        _text.AppendLine($"    mov  qword [rbp{offset}], rax");

        int stepOffset = _scope.Declare($"__step_{id}", "int");
        EmitExpr(f.Step);
        _text.AppendLine($"    mov  qword [rbp{stepOffset}], rax");

        _loopLabels.Push((lblEnd, lblContinue));
        _text.AppendLine($"{lblStart}:");

        EmitExpr(f.To);
        _text.AppendLine("    mov  rbx, rax");

        _text.AppendLine($"    cmp  qword [rbp{stepOffset}], 0");
        _text.AppendLine($"    jl   __for_neg_{id}");

        _text.AppendLine($"    cmp  qword [rbp{offset}], rbx");
        _text.AppendLine($"    jg   {lblEnd}");
        _text.AppendLine($"    jmp  __for_body_{id}");

        _text.AppendLine($"__for_neg_{id}:");
        _text.AppendLine($"    cmp  qword [rbp{offset}], rbx");
        _text.AppendLine($"    jl   {lblEnd}");

        _text.AppendLine($"__for_body_{id}:");
        _scope.EnterBlock();
        foreach (var e in f.Body) Emit(e);
        _scope.ExitBlock();

        _text.AppendLine($"{lblContinue}:");
        _text.AppendLine($"    mov  rax, [rbp{stepOffset}]");
        _text.AppendLine($"    add  qword [rbp{offset}], rax");
        _text.AppendLine($"    jmp  {lblStart}");
        _text.AppendLine($"{lblEnd}:");
        _loopLabels.Pop();
    }

    private void EmitTry(TryStatement t)
    {
        int    id         = _labelCount++;
        string lblFinally = $"__finally_{id}";
        _scope.EnterBlock(); foreach (var e in t.TryBody)     Emit(e); _scope.ExitBlock();
        _text.AppendLine($"    jmp {lblFinally}");
        _text.AppendLine($"__catch_{id}:");
        _scope.EnterBlock(); foreach (var e in t.CatchBody)   Emit(e); _scope.ExitBlock();
        _text.AppendLine($"{lblFinally}:");
        _scope.EnterBlock(); foreach (var e in t.FinallyBody) Emit(e); _scope.ExitBlock();
    }
    private bool TryGetDefaultPrintableField(string classType, out (string fieldName, string fieldType, int offset) field)
    {
        field = default;

        if (!_symbols.ClassInfoByName.TryGetValue(classType, out var cls))
            return false;

        // Convention: default printable field is "id"
        if (!cls.InstanceFields.TryGetValue("id", out var f))
            return false;

        field = ("id", f.type, f.offset);
        return true;
    }
    private void EmitConditionJump(Condition c, string falseLabel)
    {
        if (c.Op == "bool")
        {
            EmitExpr(c.Left);
            _text.AppendLine("    test rax, rax");
            _text.AppendLine($"    {(c.Negated ? "jnz" : "jz")}  {falseLabel}");
            return;
        }

        if ((c.Op == "==" || c.Op == "!=") && (IsStringExpr(c.Left) || IsStringExpr(c.Right!)))
        {
            var fakeB = new BinaryExpr { Left = c.Left, Op = "==", Right = c.Right!, Line = c.Line };
            EmitStringEquals(fakeB);
            bool jumpWhenFalse = (c.Op == "==") != c.Negated;
            _text.AppendLine("    test rax, rax");
            _text.AppendLine($"    {(jumpWhenFalse ? "jz" : "jnz")}  {falseLabel}");
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
            _ => throw new Exception($"Unknown op '{baseOp}' at line {c.Line}")
        };

        if (c.Negated) jmp = jmp switch
        {
            "jne" => "je",  "je"  => "jne",
            "jge" => "jl",  "jl"  => "jge",
            "jle" => "jg",  "jg"  => "jle",
            _ => jmp
        };

        _text.AppendLine($"    {jmp}  {falseLabel}");
    }

    // ================================================================= functions
    private void EmitFunctionBody(FunctionDeclaration fn)
    {
        string safeLabel = fn.Name.Replace(".", "_");
        string retLabel  = $"__ret_{safeLabel}";
        _currentFuncRetLabel = retLabel;
        _currentFuncRetType  = fn.ReturnType;
        _scope.EnterFunction();
        _text = _helpers;

        _text.AppendLine($"; ---- {fn.Access} {fn.ReturnType} {fn.Name} ----");
        _text.AppendLine($"__fn_{safeLabel}:");
        _text.AppendLine("    push rbp");
        _text.AppendLine("    mov  rbp, rsp");

        int localCount = Math.Max(fn.Parameters.Count + CountLocals(fn.Body) + 2, 4);
        int frameSize  = (localCount * 8 + 15) & ~15;
        _text.AppendLine($"    sub  rsp, {frameSize}");

        string[] paramRegs = { "rcx", "rdx", "r8", "r9" };
        for (int i = 0; i < fn.Parameters.Count && i < 4; i++)
        {
            int off = _scope.Declare(fn.Parameters[i].Name, fn.Parameters[i].Type);
            _text.AppendLine($"    mov  [rbp{off}], {paramRegs[i]}");
        }

        foreach (var e in fn.Body) Emit(e);

        _text.AppendLine($"{retLabel}:");
        _text.AppendLine($"    add  rsp, {frameSize}");
        _text.AppendLine("    pop  rbp");
        _text.AppendLine("    ret");
        _text.AppendLine();

        _scope.ExitFunction();
        _text = _main;
        _currentFuncRetLabel = null;
        _currentFuncRetType  = null;
    }

    private void EmitCallExpr(CallExpr fc, string resultReg)
    {
        if (EmitStdLibCall(fc, resultReg)) return;

        string safeLabel = fc.Name.Replace(".", "_");

        if (!_symbols.FunctionExists(fc.Name))
            throw new Exception($"Call to undefined function '{fc.Name}' at line {fc.Line}");

        string[] paramRegs = { "rcx", "rdx", "r8", "r9" };
        for (int i = fc.Args.Count - 1; i >= 0; i--)
        {
            EmitExpr(fc.Args[i]);
            _text.AppendLine("    push rax");
        }
        for (int i = 0; i < fc.Args.Count && i < 4; i++)
            _text.AppendLine($"    pop  {paramRegs[i]}");

        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine($"    call __fn_{safeLabel}");
        _text.AppendLine("    add  rsp, 40");

        if (resultReg != "rax") _text.AppendLine($"    mov  {resultReg}, rax");
    }

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

            case "length":
            {
                int id = _labelCount++;
                if (fc.Args.Count > 0 && IsArrayArg(fc.Args[0]))
                {
                    // array — read size from first 8 bytes
                    EmitExpr(fc.Args[0]);
                    _text.AppendLine("    mov  rax, [rax]");
                }
                else if (fc.Args.Count > 0 && IsStringArg(fc.Args[0]))
                {
                    // string — strlen
                    EmitExpr(fc.Args[0]);
                    _text.AppendLine("    test rax, rax");
                    _text.AppendLine($"    jnz  __len_str_notnull_{id}");
                    _text.AppendLine("    xor  rax, rax"); // length(null)=0
                    _text.AppendLine($"    jmp  __len_s_done_{id}");
                    _text.AppendLine($"__len_str_notnull_{id}:");
                    _text.AppendLine("    mov  rsi, rax");
                    _text.AppendLine("    xor  rax, rax");
                    _text.AppendLine($"__len_s_{id}:");
                    _text.AppendLine("    cmp  byte [rsi+rax], 0");
                    _text.AppendLine($"    je   __len_s_done_{id}");
                    _text.AppendLine("    inc  rax");
                    _text.AppendLine($"    jmp  __len_s_{id}");
                    _text.AppendLine($"__len_s_done_{id}:");
                }
                else
                {
                    // int/long/byte/short — digit count
                    EmitExpr(fc.Args[0]);
                    _text.AppendLine("    test rax, rax");
                    _text.AppendLine($"    jns  __len_pos_{id}");
                    _text.AppendLine("    neg  rax");
                    _text.AppendLine($"__len_pos_{id}:");
                    _text.AppendLine("    push rbx");
                    _text.AppendLine("    mov  rbx, 10");
                    _text.AppendLine("    xor  rcx, rcx");
                    _text.AppendLine($"__len_i_{id}:");
                    _text.AppendLine("    inc  rcx");
                    _text.AppendLine("    cqo");
                    _text.AppendLine("    idiv rbx");
                    _text.AppendLine("    test rax, rax");
                    _text.AppendLine($"    jnz  __len_i_{id}");
                    _text.AppendLine("    mov  rax, rcx");
                    _text.AppendLine("    pop  rbx");
                }
                if (resultReg != "rax") _text.AppendLine($"    mov  {resultReg}, rax");
                return true;
            }

            default: return false;
        }
    }
    // private bool IsArrayArg(Expression e) =>
    //     e is VariableExpr ve && GetVarType(ve.Name).EndsWith("[]");

    // ================================================================= return / break / continue
    private void EmitReturn(ReturnStatement r)
    {
        if (r.Value != null) EmitExpr(r.Value);
        if (_currentFuncRetLabel != null) _text.AppendLine($"    jmp {_currentFuncRetLabel}");
        else { _text.AppendLine("    mov  rcx, 0"); _text.AppendLine("    call ExitProcess"); }
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

    // ================================================================= assignment
    private void EmitAssignment(AssignmentStatement a)
    {
        switch (a.Operator)
        {
            case "++":
                LoadVar(a.VarName, "rax");
                _text.AppendLine("    inc  rax");
                EmitTruncate(GetVarType(a.VarName));
                StoreVar(a.VarName, "rax");
                break;
            case "--":
                LoadVar(a.VarName, "rax");
                _text.AppendLine("    dec  rax");
                EmitTruncate(GetVarType(a.VarName));
                StoreVar(a.VarName, "rax");
                break;
            case "=":
                EmitExpr(a.Value!);
                EmitTruncate(GetVarType(a.VarName));
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
                    "+" => "+", "-" => "-", "*" => "*", "/" => "/", "%" => "%",
                    _ => throw new Exception($"Unknown operator: {a.Operator}")
                });
                EmitTruncate(GetVarType(a.VarName));
                StoreVar(a.VarName, "rax");
                break;
        }
    }

    // ================================================================= helpers
    private void EmitIntToStrHelper()
    {
        _helpers.AppendLine("""
; ================================================================= __intToStr
; in:  rax = int64
; out: rdx = ptr to string (NO newline), ecx = byte count
__intToStr:
    push rbx
    push rdi
    push rsi
    lea  rsi, [rel convBuf]
    add  rsi, 29
    mov  byte [rsi+1], 0
    xor  rdi, rdi
    test rax, rax
    jns  __its_pos
    neg  rax
    mov  rdi, 1
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
    inc  rsi
    mov  rdx, rsi
    lea  rcx, [rel convBuf]
    add  rcx, 30
    sub  rcx, rsi
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

    private void EmitArith(string op)
    {
        switch (op)
        {
            case "+": _text.AppendLine("    add  rax, rbx"); break;
            case "-": _text.AppendLine("    sub  rax, rbx"); break;
            case "*": _text.AppendLine("    imul rax, rbx"); break;
            case "/": _text.AppendLine("    cqo"); _text.AppendLine("    idiv rbx"); break;
            case "%": _text.AppendLine("    cqo"); _text.AppendLine("    idiv rbx"); _text.AppendLine("    mov  rax, rdx"); break;
        }
    }
}