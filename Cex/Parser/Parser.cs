using System;
using System.Collections.Generic;
using Cex.AST;
using Cex.Tokens;

namespace Cex.Parser;

public class Parser
{
    private readonly List<Token> _tokens;
    private int _cur = 0;

    public Parser(List<Token> tokens) => _tokens = tokens;

    // ================================================================= public
    public List<Expression> Parse()
    {
        var list = new List<Expression>();
        SkipNewlines();
        while (!IsAtEnd())
        {
            var e = ParseStatement();
            if (e != null) list.Add(e);
            SkipNewlines();
        }
        return list;
    }

    // ============================================================== statements
    private Expression? ParseStatement()
    {
        if (Match(TokenType.Import))  { SkipToNewline(); return null; }
        if (Match(TokenType.Class))   return ParseClassDeclaration();
        if (Match(TokenType.Public, TokenType.Private, TokenType.Protected,
                  TokenType.Static,  TokenType.Void))
        { SkipToNewline(); return null; }

        if (Match(TokenType.Type))       return ParseVarDecl();
        if (Match(TokenType.Checkpoint)) return ParseCheckpoint();
        if (Match(TokenType.Goto))       return ParseGoto();
        if (Match(TokenType.Print))      return ParsePrint();
        if (Match(TokenType.Input))      return ParseInput();
        if (Match(TokenType.If))         return ParseIf();
        if (Match(TokenType.While))      return ParseWhile();
        if (Match(TokenType.For))        return ParseFor();
        if (Match(TokenType.Break))      return new BreakStatement();
        if (Match(TokenType.Continue))   return new ContinueStatement();
        if (Match(TokenType.Return))     return ParseReturn();
        if (Match(TokenType.ASM))        return ParseAsm();

        if (Match(TokenType.Indent, TokenType.Dedent, TokenType.Newline))
            return null;

        // prefix: ++x  --x
        if (Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
        {
            string op  = Advance().Value;
            string var = Consume(TokenType.Identifier, "Expected variable name").Value;
            return new AssignmentStatement { VarName = var, Operator = op };
        }

        // identifier-based: x = ...  x += ...  x++  etc.
        if (Check(TokenType.Identifier))
        {
            string name  = Peek().Value;
            int    saved = _cur;
            Advance();

            if (Match(TokenType.PlusPlus))
                return new AssignmentStatement { VarName = name, Operator = "++" };
            if (Match(TokenType.MinusMinus))
                return new AssignmentStatement { VarName = name, Operator = "--" };

            if (Match(TokenType.PlusEquals, TokenType.MinusEquals,
                      TokenType.StarEquals,  TokenType.SlashEquals))
            {
                string op  = Previous().Value;
                string rhs = ConsumeValue();
                return new AssignmentStatement { VarName = name, Operator = op, Operand = rhs };
            }

            if (Match(TokenType.Equals))
            {
                string lhs = ConsumeValue();

                if (Check(TokenType.Plus)  || Check(TokenType.Minus) ||
                    Check(TokenType.Star)  || Check(TokenType.Slash))
                {
                    string rhsOp  = Advance().Value;
                    string rhsVal = ConsumeValue();
                    return new AssignmentStatement
                    {
                        VarName  = name,
                        Operator = "=",
                        Left     = lhs,
                        RhsOp    = rhsOp,
                        Operand  = rhsVal
                    };
                }

                return new AssignmentStatement { VarName = name, Operator = "=", Operand = lhs };
            }

            _cur = saved;
        }

        Advance();
        return null;
    }

    // ------------------------------------------------------------------ class
    private ClassDeclaration ParseClassDeclaration()
    {
        string name = Consume(TokenType.Identifier, "Expected class name").Value;
        string? baseClass = null;

        if (Check(TokenType.Less))
        {
            Advance();
            baseClass = Consume(TokenType.Identifier, "Expected base class name").Value;
            Consume(TokenType.Greater, "Expected '>'");
        }

        Consume(TokenType.Colon, "Expected ':' after class");
        SkipNewlines();
        var body = ParseBlock();
        return new ClassDeclaration { Name = name, BaseClass = baseClass, Body = body };
    }

    // ------------------------------------------------------------------ if
    private IfStatement ParseIf()
    {
        var cond = ParseCondition();
        Consume(TokenType.Colon, "Expected ':' after if condition");
        SkipNewlines();
        var thenBranch = ParseBlock();

        var elseIfs    = new List<ElseIfClause>();
        var elseBranch = new List<Expression>();

        while (true)
        {
            SkipNewlines();
            if (!Check(TokenType.Else)) break;

            Advance(); // consume 'else'
            SkipNewlines();

            if (Match(TokenType.If))
            {
                var eic = ParseCondition();
                Consume(TokenType.Colon, "Expected ':' after else if");
                SkipNewlines();
                var eib = ParseBlock();
                elseIfs.Add(new ElseIfClause { Condition = eic, Body = eib });
                continue;
            }

            // plain else
            Consume(TokenType.Colon, "Expected ':' after else");
            SkipNewlines();
            elseBranch = ParseBlock();
            break;
        }

        return new IfStatement
        {
            Condition  = cond,
            ThenBranch = thenBranch,
            ElseIfs    = elseIfs,
            ElseBranch = elseBranch
        };
    }

    // ------------------------------------------------------------------ while
    private WhileStatement ParseWhile()
    {
        var cond = ParseCondition();
        Consume(TokenType.Colon, "Expected ':' after while condition");
        SkipNewlines();
        var body = ParseBlock();
        return new WhileStatement { Condition = cond, Body = body };
    }

    // ------------------------------------------------------------------ for
    private ForStatement ParseFor()
    {
        string varName = Consume(TokenType.Identifier, "Expected loop variable").Value;
        Consume(TokenType.Equals, "Expected '=' in for");
        string from = ConsumeValue();
        Consume(TokenType.To, "Expected 'to' in for");
        string to = ConsumeValue();

        string step = "1";
        if (Check(TokenType.Identifier) && Peek().Value == "step")
        {
            Advance();
            step = ConsumeValue();
        }

        Consume(TokenType.Colon, "Expected ':' after for");
        SkipNewlines();
        var body = ParseBlock();

        return new ForStatement
        {
            VarName = varName,
            From    = from,
            To      = to,
            Step    = step,
            Body    = body
        };
    }

    // ------------------------------------------------------------------ return
    private ReturnStatement ParseReturn()
    {
        if (Check(TokenType.Newline) || IsAtEnd())
            return new ReturnStatement { Value = null };
        string val = ConsumeValue();
        return new ReturnStatement { Value = val };
    }

    // ------------------------------------------------------------------ print
    private PrintStatement ParsePrint()
    {
        // print variable
        if (Check(TokenType.Identifier))
        {
            string v = Advance().Value;
            return new PrintStatement { VarName = v };
        }

        string text = Consume(TokenType.StringLiteral, "Expected string or variable after print").Value;

        // detect {varName} format placeholders
        var args = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '{')
            {
                int end = text.IndexOf('}', i);
                if (end != -1)
                    args.Add(text.Substring(i + 1, end - i - 1));
            }
            i++;
        }

        if (args.Count > 0)
            return new PrintStatement { Format = text, FormatArgs = args };

        return new PrintStatement { Literal = text };
    }

    // ------------------------------------------------------------------ input
    private InputStatement ParseInput()
    {
        string varName = Consume(TokenType.Identifier, "Expected variable name after input").Value;
        return new InputStatement { VarName = varName };
    }

    // ------------------------------------------------------------------ block
    private List<Expression> ParseBlock()
    {
        var stmts = new List<Expression>();
        if (!Match(TokenType.Indent)) return stmts;

        while (!Check(TokenType.Dedent) && !IsAtEnd())
        {
            SkipNewlines();
            if (Check(TokenType.Dedent)) break;
            var s = ParseStatement();
            if (s != null) stmts.Add(s);
        }
        Match(TokenType.Dedent);
        return stmts;
    }

    // ------------------------------------------------------------------ ASM
    private AsmBlock ParseAsm()
    {
        Consume(TokenType.Colon, "Expected ':' after ASM");
        SkipNewlines();

        var lines = new List<string>();
        if (!Match(TokenType.Indent)) return new AsmBlock { Lines = lines };

        while (!Check(TokenType.Dedent) && !IsAtEnd())
        {
            SkipNewlines();
            if (Check(TokenType.Dedent)) break;

            var sb = new System.Text.StringBuilder();
            while (!Check(TokenType.Newline) && !Check(TokenType.Dedent) && !IsAtEnd())
            {
                sb.Append(Advance().Value);
                sb.Append(' ');
            }
            string raw = sb.ToString().Trim();
            if (raw.Length > 0) lines.Add(raw);
        }
        Match(TokenType.Dedent);
        return new AsmBlock { Lines = lines };
    }

    // ------------------------------------------------------------------ simple
    private VariableDeclaration ParseVarDecl()
    {
        string type = Previous().Value;
        string name = Consume(TokenType.Identifier, "Expected variable name").Value;

        if (!Check(TokenType.Equals))
            return new VariableDeclaration { Type = type, Name = name, Value = null };

        Advance(); // consume '='

        if (Check(TokenType.StringLiteral))
            return new VariableDeclaration { Type = type, Name = name, Value = $"\"{Advance().Value}\"" };

        if (Check(TokenType.BoolLiteral))
            return new VariableDeclaration { Type = type, Name = name, Value = Advance().Value == "true" ? "1" : "0" };

        if (Match(TokenType.Null))
            return new VariableDeclaration { Type = type, Name = name, Value = "0" };

        string value = ConsumeValue();
        return new VariableDeclaration { Type = type, Name = name, Value = value };
    }

    private CheckpointStatement ParseCheckpoint()
    {
        string name = Consume(TokenType.Identifier, "Expected checkpoint name").Value;
        return new CheckpointStatement { Name = name };
    }

    private GotoStatement ParseGoto()
    {
        string name = Consume(TokenType.Identifier, "Expected checkpoint name").Value;
        return new GotoStatement { TargetName = name };
    }

    // ================================================================= helpers
    private Condition ParseCondition()
    {
        string left  = ConsumeValue();
        string op    = ConsumeOperator();
        string right = ConsumeValue();
        return new Condition { Left = left, Op = op, Right = right };
    }

    private string ConsumeOperator()
    {
        if (Match(TokenType.DoubleEquals)) return "==";
        if (Match(TokenType.NotEquals))    return "!=";
        if (Match(TokenType.LessEq))       return "<=";
        if (Match(TokenType.GreaterEq))    return ">=";
        if (Match(TokenType.Less))         return "<";
        if (Match(TokenType.Greater))      return ">";
        throw new Exception($"Expected comparison operator at line {Peek().Line}");
    }

    private string ConsumeValue()
    {
        if (Check(TokenType.Number))        return Advance().Value;
        if (Check(TokenType.FloatNumber))   return Advance().Value;
        if (Check(TokenType.Identifier))    return Advance().Value;
        if (Check(TokenType.BoolLiteral))   return Advance().Value == "true" ? "1" : "0";
        if (Check(TokenType.StringLiteral)) return $"\"{Advance().Value}\"";
        throw new Exception($"Expected value at line {Peek().Line}");
    }

    private void SkipToNewline()
    {
        while (!IsAtEnd() && !Check(TokenType.Newline)) Advance();
    }

    private void SkipNewlines()
    {
        while (Check(TokenType.Newline)) Advance();
    }

    private bool  Match(params TokenType[] types)
    {
        foreach (var t in types)
            if (Check(t)) { Advance(); return true; }
        return false;
    }

    private bool  Check(TokenType t)            => !IsAtEnd() && Peek().Type == t;
    private Token Advance()                      { if (!IsAtEnd()) _cur++; return Previous(); }
    private Token Consume(TokenType t, string m) { if (Check(t)) return Advance(); throw new Exception($"{m} at line {Peek().Line}"); }
    private bool  IsAtEnd()                      => _cur >= _tokens.Count || _tokens[_cur].Type == TokenType.EOF;
    private Token Peek()                         => _tokens[_cur];
    private Token Previous()                     => _tokens[_cur - 1];
}