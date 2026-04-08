using System.Collections.Generic;
using System.Text;
using Cex.AST;

namespace Cex;

public partial class CodeGenerator
{
    private void EmitWriteChar(bool addNewline)
    {
        _text.AppendLine("    mov  [rel charBuf], al");
        _text.AppendLine("    sub  rsp, 40");
        _text.AppendLine("    mov  rcx, -11");
        _text.AppendLine("    call GetStdHandle");
        _text.AppendLine("    mov  rcx, rax");
        _text.AppendLine("    lea  rdx, [rel charBuf]");
        _text.AppendLine("    mov  r8d, 1");
        _text.AppendLine("    lea  r9,  [rel written]");
        _text.AppendLine("    mov  qword [rsp+32], 0");
        _text.AppendLine("    call WriteConsoleA");
        _text.AppendLine("    add  rsp, 40");
        if (addNewline)
            EmitWriteConsole(GetOrAddString("", newline: true), 1);
    }

    private void EmitPrint(PrintStatement p)
    {
        if (p.Literal != null)
        {
            string text = p.Literal.Replace("\\n", "\n").Replace("\\t", "\t");
            string lbl  = GetOrAddString(text, newline: true);
            EmitWriteConsole(lbl, Encoding.ASCII.GetByteCount(text) + 1);
            return;
        }

        if (p.VarName != null)
        {
            // ------------------------------------------------------------
            // NEW: default object printing
            // print person  => prints person.id if 'person' is a class instance with field 'id'
            // otherwise => error (forces user to print fields explicitly)
            // ------------------------------------------------------------
            string vt = GetVarType(p.VarName);
            if (_symbols.ClassInfoByName.ContainsKey(vt))
            {
                if (TryGetDefaultPrintableField(vt, out var df))
                {
                    // load object pointer -> rax
                    LoadVar(p.VarName, "rax", p.Line);

                    // load default field value into rax
                    _text.AppendLine($"    mov  rax, [rax+{df.offset}]");

                    // print based on the field type
                    if (IsArrayType(df.fieldType))
                    {
                        // not supported as a default print target (yet)
                        throw new Cex.CompilerError(Cex.ErrorKind.Codegen, p.Line,
                            $"Default print for object '{p.VarName}' resolved to array field '{df.fieldName}', which is not supported.");
                    }

                    if (IsFloatType(df.fieldType))
                    {
                        _text.AppendLine("    movq xmm0, rax");
                        EmitCallPrintFloat(addNewline: true);
                        return;
                    }

                    if (df.fieldType == "string")
                    {
                        _text.AppendLine("    mov  rdx, rax");
                        EmitWriteStringRdxNewline();
                        return;
                    }

                    if (df.fieldType == "bool")
                    {
                        EmitBoolPrint(null, addNewline: true, alreadyInRax: true);
                        return;
                    }

                    if (df.fieldType == "char")
                    {
                        EmitWriteChar(addNewline: true);
                        return;
                    }

                    EmitWriteInt(addNewline: true);
                    return;
                }

                throw new Cex.CompilerError(Cex.ErrorKind.Codegen, p.Line,
                    $"Cannot print object '{p.VarName}' of type '{vt}'. Print fields instead (e.g. {p.VarName}.id).");
            }

            // ------------------------------------------------------------
            // existing variable printing
            // ------------------------------------------------------------
            string type = vt;

            if (IsArrayType(type))
            {
                EmitPrintArrayVar(p.VarName, addNewline: true);
                return;
            }

            if (IsFloatType(type))
            {
                EmitLoadFloat(p.VarName);
                EmitCallPrintFloat(addNewline: true);
                return;
            }

            if (type == "string")
            {
                LoadVar(p.VarName, "rdx");
                EmitWriteStringRdxNewline();
                return;
            }

            if (type == "bool")
            {
                LoadVar(p.VarName, "rax");
                EmitBoolPrint(null, addNewline: true, alreadyInRax: true);
                return;
            }

            if (type == "char")
            {
                LoadVar(p.VarName, "rax");
                EmitWriteChar(addNewline: true);
                return;
            }

            LoadVar(p.VarName, "rax");
            EmitWriteInt(addNewline: true);
            return;
        }

        if (p.Segments != null)
        {
            var flat = new List<PrintSegment>();
            foreach (var seg in p.Segments)
            {
                if (seg.Expr != null) FlattenToSegments(seg.Expr, flat);
                else                  flat.Add(seg);
            }

            for (int i = 0; i < flat.Count; i++)
            {
                var seg = flat[i];
                bool isLast = (i == flat.Count - 1);

                if (seg.Text != null)
                {
                    string lbl = GetOrAddString(seg.Text, newline: isLast);
                    int len = Encoding.ASCII.GetByteCount(seg.Text) + (isLast ? 1 : 0);
                    if (len > 0) EmitWriteConsole(lbl, len);
                    continue;
                }

                if (seg.Expr == null) continue;

                if (seg.Expr is VariableExpr ve && IsArrayType(GetVarType(ve.Name)))
                {
                    EmitPrintArrayVar(ve.Name, addNewline: isLast);
                    continue;
                }

                if (IsFloatExpr(seg.Expr))
                {
                    EmitExpr(seg.Expr);
                    _text.AppendLine("    movq xmm0, rax");
                    EmitCallPrintFloat(addNewline: isLast);
                }
                else if (IsStringExpr(seg.Expr))
                {
                    EmitExpr(seg.Expr);
                    _text.AppendLine("    mov  rdx, rax");
                    if (isLast) EmitWriteStringRdxNewline();
                    else        EmitWriteConsoleRdx();
                }
                else if (IsBoolExpr(seg.Expr))
                {
                    EmitBoolPrint(seg.Expr, addNewline: isLast);
                }
                else if (IsCharExpr(seg.Expr))
                {
                    EmitExpr(seg.Expr);
                    EmitWriteChar(addNewline: isLast);
                }
                else
                {
                    EmitExpr(seg.Expr);
                    EmitWriteInt(addNewline: isLast);
                }
            }

            if (flat.Count == 0)
                EmitWriteConsole(GetOrAddString("", newline: true), 1);

            return;
        }

        EmitWriteConsole(GetOrAddString("", newline: true), 1);
    }

    private bool IsBoolExpr(Expression e) =>
        e is BoolLiteral ||
        (e is VariableExpr ve && GetVarType(ve.Name) == "bool");

    private void EmitBoolPrint(Expression? expr, bool addNewline, bool alreadyInRax = false)
    {
        int id = _labelCount++;
        if (!alreadyInRax) EmitExpr(expr!);

        string lblTrue = $"__bool_true_{id}";
        string lblDone = $"__bool_done_{id}";
        string lblT    = GetOrAddString("true",  newline: addNewline);
        string lblF    = GetOrAddString("false", newline: addNewline);
        int lenT = 4 + (addNewline ? 1 : 0);
        int lenF = 5 + (addNewline ? 1 : 0);

        _text.AppendLine("    test rax, rax");
        _text.AppendLine($"    jnz  {lblTrue}");
        EmitWriteConsole(lblF, lenF);
        _text.AppendLine($"    jmp  {lblDone}");
        _text.AppendLine($"{lblTrue}:");
        EmitWriteConsole(lblT, lenT);
        _text.AppendLine($"{lblDone}:");
    }

    private void FlattenToSegments(Expression expr, List<PrintSegment> outSegs)
    {
        if (expr is BinaryExpr { Op: "+" } b)
        {
            FlattenToSegments(b.Left, outSegs);
            FlattenToSegments(b.Right, outSegs);
            return;
        }
        if (expr is StringLiteralExpr s)
        {
            if (s.Value.Length > 0) outSegs.Add(new PrintSegment { Text = s.Value });
            return;
        }
        outSegs.Add(new PrintSegment { Expr = expr });
    }
}