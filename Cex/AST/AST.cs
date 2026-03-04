using System.Collections.Generic;
using System.Text;

namespace Cex.AST;

public abstract class Expression { }

// ------------------------------------------------------------------ variables
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

// ------------------------------------------------------------------ flow
public class CheckpointStatement : Expression { public string Name       { get; set; } }
public class GotoStatement       : Expression { public string TargetName { get; set; } }
public class BreakStatement      : Expression { }
public class ContinueStatement   : Expression { }

public class ReturnStatement : Expression
{
    public string? Value { get; set; }
}

// ------------------------------------------------------------------ condition
public class Condition
{
    public string Left    { get; set; }
    public string Op      { get; set; }
    public string Right   { get; set; }
    public bool   Negated { get; set; }
}

// ------------------------------------------------------------------ if
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

// ------------------------------------------------------------------ loops
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

// ------------------------------------------------------------------ try/catch
public class TryStatement : Expression
{
    public List<Expression> TryBody     { get; set; } = new();
    public List<Expression> CatchBody   { get; set; } = new();
    public List<Expression> FinallyBody { get; set; } = new();
    public string?          ExceptionVar { get; set; }
}

// ------------------------------------------------------------------ functions
public class FunctionDeclaration : Expression
{
    public string           Access     { get; set; } = "public";   // ← ADDED
    public string           ReturnType { get; set; }
    public string           Name       { get; set; }
    public List<Parameter>  Parameters { get; set; } = new();
    public List<Expression> Body       { get; set; } = new();
}

public class Parameter
{
    public string Type { get; set; }
    public string Name { get; set; }
}

public class FunctionCall : Expression
{
    public string       Name      { get; set; }
    public List<string> Arguments { get; set; } = new();
}

// ------------------------------------------------------------------ IO
public class PrintStatement : Expression
{
    public string?      Literal    { get; set; }
    public string?      VarName    { get; set; }
    public string?      Format     { get; set; }
    public List<string> FormatArgs { get; set; } = new();
    public int ByteLen => Literal != null
        ? Encoding.ASCII.GetByteCount(Literal) + 1
        : 0;
}

public class InputStatement : Expression
{
    public string VarName { get; set; }
}

// ------------------------------------------------------------------ asm
public class AsmBlock : Expression
{
    public List<string> Lines { get; set; } = new();
}

// ------------------------------------------------------------------ class
public class ClassDeclaration : Expression
{
    public string           Name      { get; set; }
    public string?          BaseClass { get; set; }
    public List<Expression> Body      { get; set; } = new();
}

// ------------------------------------------------------------------ import
public class ImportStatement : Expression
{
    public string Module { get; set; }
}