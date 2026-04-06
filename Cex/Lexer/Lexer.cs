using System;
using System.Collections.Generic;
using System.Text;
using Cex.Tokens;

namespace Cex.Lexer;

public class Lexer
{
    private readonly string _src;
    private int  _pos  = 0;
    private int  _line = 1;

    private readonly Stack<int> _indentStack = new();
    private bool _lineStart = true;

    public Lexer(string src)
    {
        _src = src;
        _indentStack.Push(0);
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (_pos < _src.Length)
        {
            if (_lineStart)
            {
                EmitIndentTokens(tokens);
                _lineStart = false;
            }

            if (_pos >= _src.Length) break;
            char c = _src[_pos];

            // carriage return — skip
            if (c == '\r') { _pos++; continue; }

            // newline
            if (c == '\n')
            {
                tokens.Add(new Token(TokenType.Newline, "\\n", _line));
                _line++;
                _pos++;
                _lineStart = true;
                continue;
            }

            // single-line comment  //
            if (c == '/' && Peek() == '/')
            {
                while (_pos < _src.Length && _src[_pos] != '\n') _pos++;
                continue;
            }

            // whitespace
            if (c == ' ' || c == '\t') { _pos++; continue; }

            // char literal  'x'
            if (c == '\'')
            {
                tokens.Add(ReadChar());
                continue;
            }

            // string literal  "..."  or smart quotes
            if (c == '"' || c == '\u201C' || c == '\u201D')
            {
                tokens.Add(ReadString());
                continue;
            }

            // identifier or keyword
            if (char.IsLetter(c) || c == '_')
            {
                tokens.Add(ReadWord());
                continue;
            }

            // number
            if (char.IsDigit(c))
            {
                tokens.Add(ReadNumber());
                continue;
            }

            // operators and punctuation
            switch (c)
            {
                case '=':
                    if (Peek() == '=') { _pos += 2; tokens.Add(T(TokenType.DoubleEquals, "==")); }
                    else               { _pos++;     tokens.Add(T(TokenType.Equals,       "=")); }
                    break;

                case '!':
                    if (Peek() == '=') { _pos += 2; tokens.Add(T(TokenType.NotEquals, "!=")); }
                    else               { _pos++; }
                    break;

                case '<':
                    if (Peek() == '=') { _pos += 2; tokens.Add(T(TokenType.LessEq, "<=")); }
                    else               { _pos++;     tokens.Add(T(TokenType.Less,    "<")); }
                    break;

                case '>':
                    if (Peek() == '=') { _pos += 2; tokens.Add(T(TokenType.GreaterEq, ">=")); }
                    else               { _pos++;     tokens.Add(T(TokenType.Greater,    ">")); }
                    break;

                case '+':
                    if      (Peek() == '+') { _pos += 2; tokens.Add(T(TokenType.PlusPlus,   "++")); }
                    else if (Peek() == '=') { _pos += 2; tokens.Add(T(TokenType.PlusEquals, "+=")); }
                    else                    { _pos++;     tokens.Add(T(TokenType.Plus,        "+")); }
                    break;

                case '-':
                    if      (Peek() == '-') { _pos += 2; tokens.Add(T(TokenType.MinusMinus,   "--")); }
                    else if (Peek() == '=') { _pos += 2; tokens.Add(T(TokenType.MinusEquals,  "-=")); }
                    else                    { _pos++;     tokens.Add(T(TokenType.Minus,         "-")); }
                    break;

                case '*':
                    if (Peek() == '=') { _pos += 2; tokens.Add(T(TokenType.StarEquals, "*=")); }
                    else               { _pos++;     tokens.Add(T(TokenType.Star,        "*")); }
                    break;

                case '/':
                    if (Peek() == '=') { _pos += 2; tokens.Add(T(TokenType.SlashEquals, "/=")); }
                    else               { _pos++;     tokens.Add(T(TokenType.Slash,        "/")); }
                    break;

                case '%':
                    if (Peek() == '=') { _pos += 2; tokens.Add(T(TokenType.PercentEquals, "%=")); }
                    else               { _pos++;     tokens.Add(T(TokenType.Percent,        "%")); }
                    break;

                case ':': _pos++; tokens.Add(T(TokenType.Colon,           ":")); break;
                case ',': _pos++; tokens.Add(T(TokenType.Comma,           ",")); break;
                case '.': _pos++; tokens.Add(T(TokenType.Dot,             ".")); break;
                case '(': _pos++; tokens.Add(T(TokenType.ParenthesisOpen, "(")); break;
                case ')': _pos++; tokens.Add(T(TokenType.ParenthesisClose,")")); break;
                case '[': _pos++; tokens.Add(T(TokenType.BracketOpen,     "[")); break;
                case ']': _pos++; tokens.Add(T(TokenType.BracketClose,    "]")); break;
                case '{': _pos++; tokens.Add(T(TokenType.BraceOpen,       "{")); break;
                case '}': _pos++; tokens.Add(T(TokenType.BraceClose,      "}")); break;

                default:  _pos++; break;
            }
        }

        // close any remaining indent levels
        while (_indentStack.Count > 1)
        {
            _indentStack.Pop();
            tokens.Add(new Token(TokenType.Dedent, "", _line));
        }

        tokens.Add(new Token(TokenType.EOF, "", _line));
        return tokens;
    }

    // ================================================================= indent
    private void EmitIndentTokens(List<Token> tokens)
    {
        int col = 0;
        while (_pos < _src.Length && (_src[_pos] == ' ' || _src[_pos] == '\t'))
        {
            col += _src[_pos] == '\t' ? 4 : 1;
            _pos++;
        }

        // blank line or comment-only line — ignore indentation
        if (_pos >= _src.Length) return;
        if (_src[_pos] == '\n' || _src[_pos] == '\r') return;
        if (_src[_pos] == '/' && Peek() == '/') return;

        int prev = _indentStack.Peek();

        if (col > prev)
        {
            _indentStack.Push(col);
            tokens.Add(new Token(TokenType.Indent, "", _line));
        }
        else if (col < prev)
        {
            while (_indentStack.Count > 1 && _indentStack.Peek() > col)
            {
                _indentStack.Pop();
                tokens.Add(new Token(TokenType.Dedent, "", _line));
            }
        }
        // col == prev → same level, no token needed
    }

    // ================================================================= readers
    private Token ReadWord()
    {
        int start = _pos;
        while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
            _pos++;
        return Keyword(_src.Substring(start, _pos - start));
    }

    private Token ReadNumber()
    {
        int  start   = _pos;
        bool isFloat = false;

        while (_pos < _src.Length && (char.IsDigit(_src[_pos]) || _src[_pos] == '.'))
        {
            if (_src[_pos] == '.') isFloat = true;
            _pos++;
        }

        string val = _src.Substring(start, _pos - start);
        return new Token(isFloat ? TokenType.FloatNumber : TokenType.Number, val, _line);
    }

    private Token ReadString()
    {
        _pos++; // skip opening quote
        var sb = new StringBuilder();

        while (_pos < _src.Length &&
               _src[_pos] != '"' && _src[_pos] != '\u201D' && _src[_pos] != '\n')
        {
            if (_src[_pos] == '\\' && _pos + 1 < _src.Length)
            {
                _pos++;
                sb.Append(_src[_pos] switch
                {
                    'n'  => "\n",
                    't'  => "\t",
                    '\\' => "\\",
                    '"'  => "\"",
                    '{'  => "{",
                    '$'  => "$",
                    _    => _src[_pos].ToString()
                });
            }
            else
            {
                sb.Append(_src[_pos]);
            }
            _pos++;
        }

        if (_pos >= _src.Length || _src[_pos] == '\n')
            throw new Cex.CompilerError(Cex.ErrorKind.Lexer, _line, "Unterminated string literal (missing closing quote)");

        _pos++; // skip closing quote
        return new Token(TokenType.StringLiteral, sb.ToString(), _line);
    }

    private Token ReadChar()
    {
        _pos++; // skip opening '
        char ch = '\0';

        if (_pos < _src.Length)
        {
            if (_src[_pos] == '\\' && _pos + 1 < _src.Length)
            {
                _pos++;
                ch = _src[_pos] switch
                {
                    'n'  => '\n',
                    't'  => '\t',
                    '\\' => '\\',
                    '\'' => '\'',
                    '0'  => '\0',
                    _    => _src[_pos]
                };
            }
            else
            {
                ch = _src[_pos];
            }
            _pos++;
        }

        if (_pos < _src.Length && _src[_pos] == '\'') _pos++; // skip closing '
        return new Token(TokenType.CharLiteral, ((int)ch).ToString(), _line);
    }

    // ================================================================= helpers
    private char Peek(int offset = 1) =>
        _pos + offset < _src.Length ? _src[_pos + offset] : '\0';

    private Token T(TokenType type, string value) =>
        new(type, value, _line);

    private Token Keyword(string w) => w switch
    {
        "import"    => T(TokenType.Import,      w),
        "class"     => T(TokenType.Class,       w),
        "extends"   => T(TokenType.Extends,     w),
        "new"       => T(TokenType.New,         w),
        "public"    => T(TokenType.Public,      w),
        "private"   => T(TokenType.Private,     w),
        "protected" => T(TokenType.Protected,   w),
        "static"    => T(TokenType.Static,      w),
        "void"      => T(TokenType.Void,        w),
        "return"    => T(TokenType.Return,      w),
        "if"        => T(TokenType.If,          w),
        "else"      => T(TokenType.Else,        w),
        "while"     => T(TokenType.While,       w),
        "for"       => T(TokenType.For,         w),
        "to"        => T(TokenType.To,          w),
        "break"     => T(TokenType.Break,       w),
        "continue"  => T(TokenType.Continue,    w),
        "print"     => T(TokenType.Print,       w),
        "input"     => T(TokenType.Input,       w),
        "try"       => T(TokenType.Try,         w),
        "catch"     => T(TokenType.Catch,       w),
        "finally"   => T(TokenType.Finally,     w),
        "and"       => T(TokenType.And,         w),
        "or"        => T(TokenType.Or,          w),
        "not"       => T(TokenType.Not,         w),
        "int"       => T(TokenType.Type,        w),
        "float"     => T(TokenType.Type,        w),
        "string"    => T(TokenType.Type,        w),
        "bool"      => T(TokenType.Type,        w),
        "char"      => T(TokenType.Type,        w),
        "long"      => T(TokenType.Type,        w),
        "byte"      => T(TokenType.Type,        w),
        "short"     => T(TokenType.Type,        w),
        "true"      => T(TokenType.BoolLiteral, w),
        "false"     => T(TokenType.BoolLiteral, w),
        "null"      => T(TokenType.Null,        w),
        "ASM"       => T(TokenType.ASM,         w),
        "checkpoint"=> T(TokenType.Checkpoint,  w),
        "goto"      => T(TokenType.Goto,        w),
        _           => T(TokenType.Identifier,  w),
    };
}