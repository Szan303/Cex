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

        if (Check(TokenType.Public)    || Check(TokenType.Private) ||
            Check(TokenType.Protected) || Check(TokenType.Static))
        {
            if (IsFunctionDeclarationWithModifiers())
                return ParseFunctionDeclaration();
            SkipToNewline();
            return null;
        }

        if (IsFunctionDeclaration()) return ParseFunctionDeclaration();
        if (IsArrayDeclaration())    return ParseArrayDeclaration();
        if (Match(TokenType.Type))   return ParseVarDecl();

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

        // prefix ++ / --
        if (Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
        {
            int    line = Peek().Line;
            string op   = Advance().Value;
            string var  = Consume(TokenType.Identifier, "Expected variable name").Value;
            return new AssignmentStatement { VarName = var, Operator = op, Line = line };
        }

        if (Check(TokenType.Identifier))
        {
            string name  = Peek().Value;
            int    line  = Peek().Line;
            int    saved = _cur;
            Advance();

            if (Match(TokenType.Dot))
            {
                string method = Consume(TokenType.Identifier, "Expected method name after '.'").Value;

                Consume(TokenType.ParenthesisOpen, "Expected '(' after method name");
                var arg = ParseExpression();
                Consume(TokenType.ParenthesisClose, "Expected ')'");

                if (method == "add")
                    return new ArrayAddStatement { Name = name, Value = arg, Line = line };

                if (method == "delete")
                    return new ArrayDeleteStatement { Name = name, Index = arg, Line = line };

                throw new Exception($"Unknown method '{method}' at line {line}");
            }

            // array element assignment
            if (Check(TokenType.BracketOpen))
            {
                Advance();
                var index = ParseExpression();
                Consume(TokenType.BracketClose, "Expected ']'");
                Consume(TokenType.Equals, "Expected '='");
                var val = ParseExpression();
                return new ArrayAssignment { Name = name, Index = index, Value = val, Line = line };
            }

            // function call statement
            if (Check(TokenType.ParenthesisOpen))
            {
                _cur = saved;
                var callExpr = (CallExpr)ParsePrimaryCall();
                return new FunctionCall { Name = callExpr.Name, Arguments = callExpr.Args, Line = line };
            }

            if (Match(TokenType.PlusPlus))
                return new AssignmentStatement { VarName = name, Operator = "++", Line = line };
            if (Match(TokenType.MinusMinus))
                return new AssignmentStatement { VarName = name, Operator = "--", Line = line };

            if (Check(TokenType.PlusEquals)   || Check(TokenType.MinusEquals) ||
                Check(TokenType.StarEquals)    || Check(TokenType.SlashEquals) ||
                Check(TokenType.PercentEquals))
            {
                string op = Advance().Value;
                var rhs   = ParseExpression();
                return new AssignmentStatement { VarName = name, Operator = op, Value = rhs, Line = line };
            }

            if (Match(TokenType.Equals))
            {
                var rhs = ParseExpression();
                return new AssignmentStatement { VarName = name, Operator = "=", Value = rhs, Line = line };
            }

            _cur = saved;
        }

        Advance();
        return null;
    }

    // ================================================================= expressions (Pratt)
    public Expression ParseExpression() => ParseOr();

    private Expression ParseOr()
    {
        var left = ParseAnd();
        while (Match(TokenType.Or))
        {
            int line  = Previous().Line;
            var right = ParseAnd();
            left = new BinaryExpr { Left = left, Op = "||", Right = right, Line = line };
        }
        return left;
    }

    private Expression ParseAnd()
    {
        var left = ParseEquality();
        while (Match(TokenType.And))
        {
            int line  = Previous().Line;
            var right = ParseEquality();
            left = new BinaryExpr { Left = left, Op = "&&", Right = right, Line = line };
        }
        return left;
    }

    private Expression ParseEquality()
    {
        var left = ParseComparison();
        while (Check(TokenType.DoubleEquals) || Check(TokenType.NotEquals))
        {
            int    line  = Peek().Line;
            string op    = Advance().Value;
            var    right = ParseComparison();
            left = new BinaryExpr { Left = left, Op = op, Right = right, Line = line };
        }
        return left;
    }

    private Expression ParseComparison()
    {
        var left = ParseAddSub();
        while (Check(TokenType.Less)   || Check(TokenType.Greater) ||
               Check(TokenType.LessEq) || Check(TokenType.GreaterEq))
        {
            int    line  = Peek().Line;
            string op    = Advance().Value;
            var    right = ParseAddSub();
            left = new BinaryExpr { Left = left, Op = op, Right = right, Line = line };
        }
        return left;
    }

    private Expression ParseAddSub()
    {
        var left = ParseMulDiv();
        while (Check(TokenType.Plus) || Check(TokenType.Minus))
        {
            int    line  = Peek().Line;
            string op    = Advance().Value;
            var    right = ParseMulDiv();
            left = new BinaryExpr { Left = left, Op = op, Right = right, Line = line };
        }
        return left;
    }

    private Expression ParseMulDiv()
    {
        var left = ParseUnary();
        while (Check(TokenType.Star) || Check(TokenType.Slash) || Check(TokenType.Percent))
        {
            int    line  = Peek().Line;
            string op    = Advance().Value;
            var    right = ParseUnary();
            left = new BinaryExpr { Left = left, Op = op, Right = right, Line = line };
        }
        return left;
    }

    private Expression ParseUnary()
    {
        if (Match(TokenType.Minus))
        {
            int line = Previous().Line;
            return new UnaryExpr { Op = "-", Operand = ParseUnary(), Line = line };
        }
        if (Match(TokenType.Not))
        {
            int line = Previous().Line;
            return new UnaryExpr { Op = "not", Operand = ParseUnary(), Line = line };
        }
        return ParsePrimary();
    }

    private Expression ParsePrimary()
    {
        int line = Peek().Line;

        if (Check(TokenType.Number))
            return new NumberLiteral { Value = long.Parse(Advance().Value), Line = line };

        if (Check(TokenType.FloatNumber))
            return new NumberLiteral { Value = (long)double.Parse(Advance().Value), Line = line };

        if (Check(TokenType.BoolLiteral))
        {
            bool val = Advance().Value == "true";
            return new BoolLiteral { Value = val, Line = line };
        }

        if (Check(TokenType.CharLiteral))
        {
            int val = int.Parse(Advance().Value);
            return new CharLiteral { Value = val, Line = line };
        }

        if (Match(TokenType.Null))
            return new NumberLiteral { Value = 0, Line = line };

        if (Check(TokenType.StringLiteral))
            return new StringLiteralExpr { Value = Advance().Value, Line = line };

        if (Match(TokenType.ParenthesisOpen))
        {
            var expr = ParseExpression();
            Consume(TokenType.ParenthesisClose, "Expected ')'");
            return expr;
        }

        if (Check(TokenType.Identifier))
            return ParsePrimaryCall();

        throw new Exception($"Expected expression at line {line}");
    }

    private Expression ParsePrimaryCall()
    {
        int    line = Peek().Line;
        string name = Advance().Value;

        // handle dot notation: Math.power(x, n)  etc.
        if (Check(TokenType.Dot))
        {
            Advance();
            string method = Consume(TokenType.Identifier, "Expected method name after '.'").Value;
            name = name + "." + method;
        }

        if (Match(TokenType.ParenthesisOpen))
        {
            var args = new List<Expression>();
            while (!Check(TokenType.ParenthesisClose) && !IsAtEnd())
            {
                args.Add(ParseExpression());
                Match(TokenType.Comma);
            }
            Consume(TokenType.ParenthesisClose, "Expected ')'");
            return new CallExpr { Name = name, Args = args, Line = line };
        }

        if (Match(TokenType.BracketOpen))
        {
            var index = ParseExpression();
            Consume(TokenType.BracketClose, "Expected ']'");
            return new ArrayAccess { Name = name, Index = index, Line = line };
        }

        return new VariableExpr { Name = name, Line = line };
    }

    // ================================================================= condition
    private Condition ParseCondition()
    {
        int  line    = Peek().Line;
        bool negated = Match(TokenType.Not);

        var left = ParseAddSub();

        // boolean condition — no comparison operator
        if (Check(TokenType.Colon) || Check(TokenType.Newline) || IsAtEnd())
            return new Condition { Left = left, Op = "bool", Right = null, Negated = negated, Line = line };

        string op    = ConsumeOperator();
        var    right = ParseAddSub();

        if (Match(TokenType.And))
        {
            var    left2  = ParseAddSub();
            string op2    = ConsumeOperator();
            var    right2 = ParseAddSub();
            return new Condition { Left = left, Op = op + "&&" + op2, Right = right, Negated = negated, Line = line };
        }

        if (Match(TokenType.Or))
        {
            var    left2  = ParseAddSub();
            string op2    = ConsumeOperator();
            var    right2 = ParseAddSub();
            return new Condition { Left = left, Op = op + "||" + op2, Right = right, Negated = negated, Line = line };
        }

        return new Condition { Left = left, Op = op, Right = right, Negated = negated, Line = line };
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

    // ================================================================= print
    private PrintStatement ParsePrint()
    {
        int line = Previous().Line;

        // print varName  (bare identifier, no quotes)
        if (Check(TokenType.Identifier))
        {
            // if it's a function call like length(arr), parse as full expression
            if (_cur + 1 < _tokens.Count && _tokens[_cur + 1].Type == TokenType.ParenthesisOpen)
            {
                var expr = ParseExpression();
                return new PrintStatement
                {
                    Segments = new List<PrintSegment> { new PrintSegment { Expr = expr } },
                    Line     = line
                };
            }
            return new PrintStatement { VarName = Advance().Value, Line = line };
        }

        // print 'x'  (char literal)
        if (Check(TokenType.CharLiteral))
        {
            int val = int.Parse(Advance().Value);
            var charExpr = new CharLiteral { Value = val, Line = line };
            return new PrintStatement
            {
                Segments = new List<PrintSegment> { new PrintSegment { Expr = charExpr } },
                Line     = line
            };
        }

        if (!Check(TokenType.StringLiteral))
            throw new Exception($"Expected string literal or variable after print at line {line}");

        string raw = Advance().Value;

        // pure interpolation with no concat after — fast path
        if (raw.Contains("${") && !Check(TokenType.Plus))
            return new PrintStatement { Segments = ParseInterpolation(raw), Line = line };

        // build expression from the string (handles interpolation internally)
        Expression printExpr = BuildStringExpr(raw, line);

        // consume any chained  + expr
        while (Check(TokenType.Plus))
        {
            int opLine = Peek().Line;
            Advance();
            var right = ParseMulDiv();
            printExpr = new BinaryExpr { Left = printExpr, Op = "+", Right = right, Line = opLine };
        }

        // plain string with no interpolation and no concat — Literal fast path
        if (printExpr is StringLiteralExpr sle && !sle.Value.Contains("${"))
            return new PrintStatement { Literal = sle.Value, Line = line };

        return new PrintStatement
        {
            Segments = new List<PrintSegment> { new PrintSegment { Expr = printExpr } },
            Line     = line
        };
    }

    private Expression BuildStringExpr(string raw, int line)
    {
        if (!raw.Contains("${"))
            return new StringLiteralExpr { Value = raw, Line = line };

        var segs = ParseInterpolation(raw);
        if (segs.Count == 0)
            return new StringLiteralExpr { Value = "", Line = line };

        Expression result = SegmentToExpr(segs[0], line);
        for (int i = 1; i < segs.Count; i++)
            result = new BinaryExpr
            {
                Left  = result,
                Op    = "+",
                Right = SegmentToExpr(segs[i], line),
                Line  = line
            };
        return result;
    }

    private Expression SegmentToExpr(PrintSegment seg, int line) =>
        seg.Expr != null
            ? seg.Expr
            : new StringLiteralExpr { Value = seg.Text ?? "", Line = line };

    private List<PrintSegment> ParseInterpolation(string raw)
    {
        var segments = new List<PrintSegment>();
        int i = 0;

        while (i < raw.Length)
        {
            int start = raw.IndexOf("${", i);
            if (start == -1)
            {
                if (i < raw.Length)
                    segments.Add(new PrintSegment { Text = raw.Substring(i) });
                break;
            }

            if (start > i)
                segments.Add(new PrintSegment { Text = raw.Substring(i, start - i) });

            int end = raw.IndexOf('}', start + 2);
            if (end == -1)
                throw new Exception("Unterminated ${ in string interpolation");

            string exprSrc = raw.Substring(start + 2, end - start - 2).Trim();
            var exprTokens = new Cex.Lexer.Lexer(exprSrc).Tokenize();
            var exprParser = new Parser(exprTokens);
            segments.Add(new PrintSegment { Expr = exprParser.ParseExpression() });

            i = end + 1;
        }

        return segments;
    }

    // ================================================================= lookaheads
    private bool IsFunctionDeclarationWithModifiers()
    {
        int  saved  = _cur;
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

    private bool IsArrayDeclaration()
    {
        int saved = _cur;
        bool result = false;

        // TYPE '[' <expr> ']' IDENT
        if (_cur < _tokens.Count && _tokens[_cur].Type == TokenType.Type)
        {
            _cur++;
            if (_cur < _tokens.Count && _tokens[_cur].Type == TokenType.BracketOpen)
            {
                _cur++; // after '['

                // must NOT be ']' immediately (we require initSize for now)
                if (_cur < _tokens.Count && _tokens[_cur].Type != TokenType.BracketClose)
                {
                    // skip tokens until matching ']' (simple scan is ok here)
                    while (_cur < _tokens.Count && _tokens[_cur].Type != TokenType.BracketClose)
                        _cur++;

                    if (_cur < _tokens.Count && _tokens[_cur].Type == TokenType.BracketClose)
                    {
                        _cur++; // after ']'
                        if (_cur < _tokens.Count && _tokens[_cur].Type == TokenType.Identifier)
                            result = true;
                    }
                }
            }
        }

        _cur = saved;
        return result;
    }

    // ================================================================= statement parsers
    private ImportStatement ParseImport()
    {
        int    line   = Previous().Line;
        string module = "";
        while (!Check(TokenType.Newline) && !IsAtEnd())
            module += Advance().Value;
        return new ImportStatement { Module = module.Trim(), Line = line };
    }

    private FunctionDeclaration ParseFunctionDeclaration()
    {
        int    line   = Peek().Line;
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
            Body       = body,
            Line       = line
        };
    }

    private ArrayDeclaration ParseArrayDeclaration()
    {
        int    line     = Peek().Line;
        string elemType = Advance().Value; // TokenType.Type already confirmed by IsArrayDeclaration()

        Consume(TokenType.BracketOpen, "Expected '[' after type");
        var size = ParseExpression();
        Consume(TokenType.BracketClose, "Expected ']' after init size");

        string name = Consume(TokenType.Identifier, "Expected array name").Value;

        // No "= new ..." anymore
        return new ArrayDeclaration
        {
            ElementType = elemType,
            Name        = name,
            Size        = size,
            Line        = line
        };
    }

    private VariableDeclaration ParseVarDecl()
    {
        int    line = Previous().Line;
        string type = Previous().Value;
        string name = Consume(TokenType.Identifier, "Expected variable name").Value;

        if (!Check(TokenType.Equals))
            return new VariableDeclaration { Type = type, Name = name, Init = null, Line = line };

        Advance();
        var init = ParseExpression();
        return new VariableDeclaration { Type = type, Name = name, Init = init, Line = line };
    }

    private ClassDeclaration ParseClassDeclaration()
    {
        int     line      = Previous().Line;
        string  name      = Consume(TokenType.Identifier, "Expected class name").Value;
        string? baseClass = null;
        if (Check(TokenType.Less))
        {
            Advance();
            baseClass = Consume(TokenType.Identifier, "Expected base class").Value;
            Consume(TokenType.Greater, "Expected '>'");
        }
        Consume(TokenType.Colon, "Expected ':' after class name");
        SkipNewlines();
        var body = ParseBlock();
        return new ClassDeclaration { Name = name, BaseClass = baseClass, Body = body, Line = line };
    }

    private IfStatement ParseIf()
    {
        int line = Previous().Line;
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
            ElseBranch = elseBranch,
            Line       = line
        };
    }

    private WhileStatement ParseWhile()
    {
        int line = Previous().Line;
        var cond = ParseCondition();
        Consume(TokenType.Colon, "Expected ':' after while");
        SkipNewlines();
        return new WhileStatement { Condition = cond, Body = ParseBlock(), Line = line };
    }

    private ForStatement ParseFor()
    {
        int    line    = Previous().Line;
        string varName = Consume(TokenType.Identifier, "Expected loop variable").Value;
        Consume(TokenType.Equals, "Expected '='");
        var from = ParseExpression();
        Consume(TokenType.To, "Expected 'to'");
        var to   = ParseExpression();
        Expression step = new NumberLiteral { Value = 1 };
        if (Check(TokenType.Identifier) && Peek().Value == "step")
        {
            Advance();
            step = ParseExpression();
        }
        Consume(TokenType.Colon, "Expected ':'");
        SkipNewlines();
        return new ForStatement
        {
            VarName = varName,
            From    = from,
            To      = to,
            Step    = step,
            Body    = ParseBlock(),
            Line    = line
        };
    }

    private TryStatement ParseTry()
    {
        int line = Previous().Line;
        Consume(TokenType.Colon, "Expected ':' after try");
        SkipNewlines();
        var tryBody     = ParseBlock();
        var catchBody   = new List<Expression>();
        var finallyBody = new List<Expression>();
        string? excVar  = null;

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
            ExceptionVar = excVar,
            Line         = line
        };
    }

    private ReturnStatement ParseReturn()
    {
        int line = Previous().Line;
        if (Check(TokenType.Newline) || IsAtEnd())
            return new ReturnStatement { Value = null, Line = line };
        return new ReturnStatement { Value = ParseExpression(), Line = line };
    }

    private InputStatement ParseInput()
    {
        int line = Previous().Line;
        return new InputStatement
        {
            VarName = Consume(TokenType.Identifier, "Expected variable name after input").Value,
            Line    = line
        };
    }

    private CheckpointStatement ParseCheckpoint()
    {
        int line = Previous().Line;
        return new CheckpointStatement
        {
            Name = Consume(TokenType.Identifier, "Expected checkpoint name").Value,
            Line = line
        };
    }

    private GotoStatement ParseGoto()
    {
        int line = Previous().Line;
        return new GotoStatement
        {
            TargetName = Consume(TokenType.Identifier, "Expected target name").Value,
            Line       = line
        };
    }

    // ================================================================= block / asm
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

    private AsmBlock ParseAsm()
    {
        int line = Previous().Line;
        Consume(TokenType.Colon, "Expected ':' after ASM");
        SkipNewlines();
        var lines = new List<string>();
        if (!Match(TokenType.Indent)) return new AsmBlock { Lines = lines, Line = line };

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
        return new AsmBlock { Lines = lines, Line = line };
    }

    // ================================================================= helpers
    private void SkipToNewline() { while (!IsAtEnd() && !Check(TokenType.Newline)) Advance(); }
    private void SkipNewlines()  { while (Check(TokenType.Newline)) Advance(); }

    private bool Match(params TokenType[] types)
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