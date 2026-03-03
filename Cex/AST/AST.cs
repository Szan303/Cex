using System.Collections.Generic;
using System.Text;

namespace Cex.AST;

public abstract class Expression { }

// ------------------------------------------------------------------ existing
public class VariableDeclaration : Expression
{
    public string  Type  { get; set; }
    public string  Name  { get; set; }
    public string? Value { get; set; }
}

public class AssignmentStatement : Expression
{
    public string  VarName  { get; set; }
    public string  Operator { get; set; }
    public string? Left     { get; set; }
    public string? RhsOp    { get; set; }
    public string? Operand  { get; set; }
}

public class CheckpointStatement : Expression
{
    public string Name { get; set; }
}

public class GotoStatement : Expression
{
    public string TargetName { get; set; }
}

public class AsmBlock : Expression
{
    public List<string> Lines { get; set; } = new();
}

public class PrintStatement : Expression
{
    public string?      Literal    { get; set; }   // print "hello"
    public string?      VarName    { get; set; }   // print x
    public string?      Format     { get; set; }   // print "x = {x}"
    public List<string> FormatArgs { get; set; } = new();
    public int ByteLen => Literal != null
        ? Encoding.ASCII.GetByteCount(Literal) + 1
        : 0;
}

public class InputStatement : Expression
{
    public string VarName { get; set; }
}

public class Condition
{
    public string Left  { get; set; }
    public string Op    { get; set; }
    public string Right { get; set; }
}

public class IfStatement : Expression
{
    public Condition          Condition  { get; set; }
    public List<Expression>   ThenBranch { get; set; } = new();
    public List<ElseIfClause> ElseIfs    { get; set; } = new();
    public List<Expression>   ElseBranch { get; set; } = new();
}

public class ElseIfClause
{
    public Condition        Condition { get; set; }
    public List<Expression> Body      { get; set; } = new();
}

public class WhileStatement : Expression
{
    public Condition        Condition { get; set; }
    public List<Expression> Body      { get; set; } = new();
}

public class ForStatement : Expression
{
    public string           VarName { get; set; }
    public string           From    { get; set; }
    public string           To      { get; set; }
    public string           Step    { get; set; } = "1";
    public List<Expression> Body    { get; set; } = new();
}

public class BreakStatement    : Expression { }
public class ContinueStatement : Expression { }

public class ReturnStatement : Expression
{
    public string? Value { get; set; }
}

public class ClassDeclaration : Expression
{
    public string           Name      { get; set; }
    public string?          BaseClass { get; set; }
    public List<Expression> Body      { get; set; } = new();
}