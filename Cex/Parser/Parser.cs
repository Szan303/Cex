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
        if (Match(TokenType.Import)) return ParseImport();
        if (Match(TokenType.Class))  return ParseClassDeclaration();

        // access modifiers: public / private / protected / static
        if (Check(TokenType.Public)    || Check(TokenType.Private) ||
            Check(TokenType.Protected) || Check(TokenType.Static))
        {
            if (IsFunctionDeclarationWithModifiers())
                return ParseFunctionDeclaration();
            SkipToNewline();
            return null;
        }

        // unqualified function:  void foo():  or  int add(int x, int y):
        if (IsFunctionDeclaration())
            return ParseFunctionDeclaration();

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
        if (Match(TokenType.Try))        return ParseTry();
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

        // identifier-based: assignment or function call
        if (Check(TokenType.Identifier))
        {
            string name  = Peek().Value;
            int    saved = _cur;
            Advance();

            if (Check(TokenType.ParenthesisOpen))
                return ParseFunctionCallStatement(name);

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
                    Check(TokenType.Star)  || Check(TokenType.Slash) ||
                    Check(TokenType.Percent))
                {
                    string rhsOp  = Advance().Value;
                    string rhsVal = ConsumeValue();
                    return new AssignmentStatement
                    {
                        VarName  = name, Operator = "=",
                        Left     = lhs,  RhsOp    = rhsOp, Operand = rhsVal
                    };
                }
                return new AssignmentStatement { VarName = name, Operator = "=", Operand = lhs };
            }

            _cur = saved;
        }

        Advance();
        return null;
    }

    // ---------------------------------------------------------------- lookahead helpers

    // Detects: [public|private|protected|static]* TYPE|void NAME (|:
    private bool IsFunctionDeclarationWithModifiers()
    {
        int saved = _cur;
        bool result = false;

        while (_cur < _tokens.Count &&
               (_tokens[_cur].Type is TokenType.Public or TokenType.Private
                                   or TokenType.Protected or TokenType.Static))
            _cur++;

        if (_cur < _tokens.Count &&
            (_tokens[_cur].Type == TokenType.Type || _tokens[_cur].Type == TokenType.Void))
        {
            _cur++;
            if (_cur < _tokens.Count && _tokens[_cur].Type == TokenType.Identifier)
            {
                _cur++;
                if (_cur < _tokens.Count &&
                    (_tokens[_cur].Type == TokenType.ParenthesisOpen ||
                     _tokens[_cur].Type == TokenType.Colon))
                    result = true;
            }
        }

        _cur = saved;
        return result;
    }

    // Detects: TYPE|void NAME (|:   (no leading modifiers)
    private bool IsFunctionDeclaration()
    {
        int  saved  = _cur;
        bool result = false;

        if (_cur < _tokens.Count &&
            (_tokens[_cur].Type == TokenType.Type || _tokens[_cur].Type == TokenType.Void))
        {
            _cur++;
            if (_cur < _tokens.Count && _tokens[_cur].Type == TokenType.Identifier)
            {
                _cur++;
                if (_cur < _tokens.Count &&
                    (_tokens[_cur].Type == TokenType.ParenthesisOpen ||
                     _tokens[_cur].Type == TokenType.Colon))
                    result = true;
            }
        }

        _cur = saved;
        return result;
    }

    // ------------------------------------------------------------------ import
    private ImportStatement ParseImport()
    {
        string module = "";
        while (!Check(TokenType.Newline) && !IsAtEnd())
            module += Advance().Value;
        return new ImportStatement { Module = module.Trim() };
    }

    // ------------------------------------------------------------------ function
    private FunctionDeclaration ParseFunctionDeclaration()
    {
        string access = "public";
        while (Check(TokenType.Public)    || Check(TokenType.Private) ||
               Check(TokenType.Protected) || Check(TokenType.Static))
        {
            string mod = Advance().Value;
            if (mod is "public" or "private" or "protected")
                access = mod;
        }

        string retType = Advance().Value;
        string name    = Consume(TokenType.Identifier, "Expected function name").Value;

        var parameters = new List<Parameter>();

        if (Match(TokenType.ParenthesisOpen))
        {
            while (!Check(TokenType.ParenthesisClose) && !IsAtEnd())
            {
                string pType = Consume(TokenType.Type, "Expected parameter type").Value;
                string pName = Consume(TokenType.Identifier, "Expected parameter name").Value;
                parameters.Add(new Parameter { Type = pType, Name = pName });
                Match(TokenType.Comma);
            }
            Consume(TokenType.ParenthesisClose, "Expected ')'");
        }

        Consume(TokenType.Colon, "Expected ':' after function signature");
        SkipNewlines();

        var body = ParseBlock();
        return new FunctionDeclaration
        {
            Access     = access,
            ReturnType = retType,
            Name       = name,
            Parameters = parameters,
            Body       = body
        };
    }

    private FunctionCall ParseFunctionCallStatement(string name)
    {
        Consume(TokenType.ParenthesisOpen, "Expected '('");
        var args = new List<string>();
        while (!Check(TokenType.ParenthesisClose) && !IsAtEnd())
        {
            args.Add(ConsumeValue());
            Match(TokenType.Comma);
        }
        Consume(TokenType.ParenthesisClose, "Expected ')'");
        return new FunctionCall { Name = name, Arguments = args };
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
            Advance();
            SkipNewlines();

            if (Match(TokenType.If))
            {
                var eic = ParseCondition();
                Consume(TokenType.Colon, "Expected ':' after else if");
                SkipNewlines();
                elseIfs.Add(new ElseIfClause { Condition = eic, Body = ParseBlock() });
                continue;
            }

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
        Consume(TokenType.Colon, "Expected ':' after while");
        SkipNewlines();
        return new WhileStatement { Condition = cond, Body = ParseBlock() };
    }

    // ------------------------------------------------------------------ for
    private ForStatement ParseFor()
    {
        string varName = Consume(TokenType.Identifier, "Expected loop variable").Value;
        Consume(TokenType.Equals, "Expected '='");
        string from = ConsumeValue();
        Consume(TokenType.To, "Expected 'to'");
        string to   = ConsumeValue();
        string step = "1";
        if (Check(TokenType.Identifier) && Peek().Value == "step")
        { Advance(); step = ConsumeValue(); }
        Consume(TokenType.Colon, "Expected ':'");
        SkipNewlines();
        return new ForStatement
        {
            VarName = varName, From = from, To = to,
            Step    = step,    Body = ParseBlock()
        };
    }

    // ------------------------------------------------------------------ try/catch
    private TryStatement ParseTry()
    {
        Consume(TokenType.Colon, "Expected ':' after try");
        SkipNewlines();
        var tryBody = ParseBlock();

        var     catchBody   = new List<Expression>();
        var     finallyBody = new List<Expression>();
        string? excVar      = null;

        SkipNewlines();
        if (Match(TokenType.Catch))
        {
            if (Match(TokenType.ParenthesisOpen))
            {
                excVar = Consume(TokenType.Identifier, "Expected exception variable").Value;
                Consume(TokenType.ParenthesisClose, "Expected ')'");
            }
            Consume(TokenType.Colon, "Expected ':' after catch");
            SkipNewlines();
            catchBody = ParseBlock();
        }

        SkipNewlines();
        if (Match(TokenType.Finally))
        {
            Consume(TokenType.Colon, "Expected ':' after finally");
            SkipNewlines();
            finallyBody = ParseBlock();
        }

        return new TryStatement
        {
            TryBody      = tryBody,
            CatchBody    = catchBody,
            FinallyBody  = finallyBody,
            ExceptionVar = excVar
        };
    }

    // ------------------------------------------------------------------ return
    private ReturnStatement ParseReturn()
    {
        if (Check(TokenType.Newline) || IsAtEnd())
            return new ReturnStatement { Value = null };
        return new ReturnStatement { Value = ConsumeValue() };
    }

    // ------------------------------------------------------------------ print
    private PrintStatement ParsePrint()
    {
        if (Check(TokenType.Identifier))
            return new PrintStatement { VarName = Advance().Value };

        string text = Consume(TokenType.StringLiteral, "Expected string or variable after print").Value;

        var args = new List<string>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                int end = text.IndexOf('}', i);
                if (end != -1) args.Add(text.Substring(i + 1, end - i - 1));
            }
        }

        if (args.Count > 0) return new PrintStatement { Format = text, FormatArgs = args };
        return new PrintStatement { Literal = text };
    }

    // ------------------------------------------------------------------ input
    private InputStatement ParseInput() =>
        new() { VarName = Consume(TokenType.Identifier, "Expected variable name after input").Value };

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
            { sb.Append(Advance().Value); sb.Append(' '); }
            string raw = sb.ToString().Trim();
            if (raw.Length > 0) lines.Add(raw);
        }
        Match(TokenType.Dedent);
        return new AsmBlock { Lines = lines };
    }

    // ------------------------------------------------------------------ var
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

        return new VariableDeclaration { Type = type, Name = name, Value = ConsumeValue() };
    }

    private CheckpointStatement ParseCheckpoint() =>
        new() { Name = Consume(TokenType.Identifier, "Expected checkpoint name").Value };

    private GotoStatement ParseGoto() =>
        new() { TargetName = Consume(TokenType.Identifier, "Expected checkpoint name").Value };

    // ================================================================= helpers
    private Condition ParseCondition()
    {
        bool   negated = Match(TokenType.Not);
        string left    = ConsumeValue();
        string op      = ConsumeOperator();
        string right   = ConsumeValue();

        if (Match(TokenType.And))
        {
            string left2  = ConsumeValue();
            string op2    = ConsumeOperator();
            string right2 = ConsumeValue();
            return new Condition { Left = left, Op = op + "&&" + op2, Right = right, Negated = negated };
        }
        if (Match(TokenType.Or))
        {
            string left2  = ConsumeValue();
            string op2    = ConsumeOperator();
            string right2 = ConsumeValue();
            return new Condition { Left = left, Op = op + "||" + op2, Right = right, Negated = negated };
        }

        return new Condition { Left = left, Op = op, Right = right, Negated = negated };
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
        if (Check(TokenType.Null))          { Advance(); return "0"; }
        throw new Exception($"Expected value at line {Peek().Line}");
    }

    private void SkipToNewline() { while (!IsAtEnd() && !Check(TokenType.Newline)) Advance(); }
    private void SkipNewlines()  { while (Check(TokenType.Newline)) Advance(); }

    private bool  Match(params TokenType[] types)
    {
        foreach (var t in types) if (Check(t)) { Advance(); return true; }
        return false;
    }

    private bool  Check(TokenType t)            => !IsAtEnd() && Peek().Type == t;
    private Token Advance()                      { if (!IsAtEnd()) _cur++; return Previous(); }
    private Token Consume(TokenType t, string m) { if (Check(t)) return Advance(); throw new Exception($"{m} at line {Peek().Line}"); }
    private bool  IsAtEnd()                      => _cur >= _tokens.Count || _tokens[_cur].Type == TokenType.EOF;
    private Token Peek()                         => _tokens[_cur];
    private Token Previous()                     => _tokens[_cur - 1];
}