using System.Collections.Generic;

namespace Cex.AST;

public abstract class Expression
{
    public int Line { get; set; } = 0;   // ← line number on every node
}

// ------------------------------------------------------------------ literals
public class NumberLiteral : Expression
{
    public long Value { get; set; }
}

public class StringLiteralExpr : Expression
{
    public string Value { get; set; } = "";
}

public class BoolLiteral : Expression
{
    public bool Value { get; set; }
}

public class VariableExpr : Expression
{
    public string Name { get; set; } = "";
}

public class BinaryExpr : Expression
{
    public Expression Left  { get; set; } = null!;
    public string     Op    { get; set; } = "";
    public Expression Right { get; set; } = null!;
}

public class UnaryExpr : Expression
{
    public string     Op      { get; set; } = "";
    public Expression Operand { get; set; } = null!;
}

public class CallExpr : Expression
{
    public string           Name { get; set; } = "";
    public List<Expression> Args { get; set; } = new();
}

// ------------------------------------------------------------------ variables
public class VariableDeclaration : Expression
{
    public string      Type { get; set; } = "";
    public string      Name { get; set; } = "";
    public Expression? Init { get; set; }
}

public class AssignmentStatement : Expression
{
    public string      VarName  { get; set; } = "";
    public string      Operator { get; set; } = "";
    public Expression? Value    { get; set; }
}

// ------------------------------------------------------------------ flow
public class CheckpointStatement : Expression { public string Name       { get; set; } = ""; }
public class GotoStatement       : Expression { public string TargetName { get; set; } = ""; }
public class BreakStatement      : Expression { }
public class ContinueStatement   : Expression { }

public class ReturnStatement : Expression
{
    public Expression? Value { get; set; }
}

// ------------------------------------------------------------------ condition
public class Condition
{
    public Expression Left    { get; set; } = null!;
    public string     Op      { get; set; } = "";
    public Expression? Right  { get; set; }        // null for boolean conditions (if b:)
    public bool       Negated { get; set; }
    public int        Line    { get; set; }
}

// ------------------------------------------------------------------ if
public class IfStatement : Expression
{
    public Condition          Condition  { get; set; } = null!;
    public List<Expression>   ThenBranch { get; set; } = new();
    public List<ElseIfClause> ElseIfs    { get; set; } = new();
    public List<Expression>   ElseBranch { get; set; } = new();
}

public class ElseIfClause
{
    public Condition        Condition { get; set; } = null!;
    public List<Expression> Body      { get; set; } = new();
}

// ------------------------------------------------------------------ loops
public class WhileStatement : Expression
{
    public Condition        Condition { get; set; } = null!;
    public List<Expression> Body      { get; set; } = new();
}

public class ForStatement : Expression
{
    public string           VarName { get; set; } = "";
    public Expression       From    { get; set; } = null!;
    public Expression       To      { get; set; } = null!;
    public Expression       Step    { get; set; } = null!;
    public List<Expression> Body    { get; set; } = new();
}

// ------------------------------------------------------------------ try
public class TryStatement : Expression
{
    public List<Expression> TryBody      { get; set; } = new();
    public List<Expression> CatchBody    { get; set; } = new();
    public List<Expression> FinallyBody  { get; set; } = new();
    public string?          ExceptionVar { get; set; }
}

// ------------------------------------------------------------------ functions
public class Parameter
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
}

public class FunctionDeclaration : Expression
{
    public string           Access     { get; set; } = "public";
    public string           ReturnType { get; set; } = "void";
    public string           Name       { get; set; } = "";
    public List<Parameter>  Parameters { get; set; } = new();
    public List<Expression> Body       { get; set; } = new();
}

public class FunctionCall : Expression
{
    public string           Name      { get; set; } = "";
    public List<Expression> Arguments { get; set; } = new();
}

// ------------------------------------------------------------------ print
public class PrintStatement : Expression
{
    public string?             Literal  { get; set; }
    public string?             VarName  { get; set; }
    public List<PrintSegment>? Segments { get; set; }
}

public class PrintSegment
{
    public string?     Text { get; set; }
    public Expression? Expr { get; set; }
}

// ------------------------------------------------------------------ input
public class InputStatement : Expression
{
    public string VarName { get; set; } = "";
}

// ------------------------------------------------------------------ arrays
public class ArrayDeclaration : Expression
{
    public string     ElementType { get; set; } = "";
    public string     Name        { get; set; } = "";
    public Expression Size        { get; set; } = null!;
}

public class ArrayAccess : Expression
{
    public string     Name  { get; set; } = "";
    public Expression Index { get; set; } = null!;
}

public class ArrayAssignment : Expression
{
    public string     Name  { get; set; } = "";
    public Expression Index { get; set; } = null!;
    public Expression Value { get; set; } = null!;
}

// ------------------------------------------------------------------ misc
public class AsmBlock : Expression
{
    public List<string> Lines { get; set; } = new();
}

public class ClassDeclaration : Expression
{
    public string           Name      { get; set; } = "";
    public string?          BaseClass { get; set; }
    public List<Expression> Body      { get; set; } = new();
}

public class ImportStatement : Expression
{
    public string Module { get; set; } = "";
}