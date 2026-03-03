using Cex.Tokens;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cex.Lexer;

public class Lexer
{
    private readonly string _source;
    private int _position = 0;
    private int _line = 1;

    public Lexer(string source) => _source = source;

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (_position < _source.Length)
        {
            char current = _source[_position];

            if (current == '\n')
            {
                _line++;
                _position++;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                _position++;
                continue;
            }

            if (current == '\"' || current == '“' || current == '”')
            {
                tokens.Add(ReadString());
                continue;
            }

            if (char.IsLetter(current))
            {
                string word = ReadWord();
                tokens.Add(MatchKeywordOrIdentifier(word));
                continue;
            }

            if (char.IsDigit(current))
            {
                string number = ReadNumber();
                tokens.Add(new Token(TokenType.Number, number, _line));
                continue;
            }

            if (current == '=')
            {
                if (Peek() == '=')
                {
                    _position += 2;
                    tokens.Add(new Token(TokenType.DoubleEquals, "==", _line));
                }
                else
                {
                    _position++;
                    tokens.Add(new Token(TokenType.Equals, "=", _line));
                }
                continue;
            }

            if (current == ':')
            {
                tokens.Add(new Token(TokenType.Colon, ":", _line));
                _position++;
                continue;
            }

            _position++; // ignorujemy nieznane znaki
        }

        tokens.Add(new Token(TokenType.EOF, "", _line));
        return tokens;
    }

    private string ReadWord()
    {
        int start = _position;
        while (_position < _source.Length && (char.IsLetterOrDigit(_source[_position]) || _source[_position] == '[' || _source[_position] == ']'))
            _position++;
        return _source.Substring(start, _position - start);
    }

    private string ReadNumber()
    {
        int start = _position;
        while (_position < _source.Length && char.IsDigit(_source[_position]))
            _position++;
        return _source.Substring(start, _position - start);
    }

    private Token ReadString()
    {
        char quote = _source[_position];
        _position++;
        var sb = new StringBuilder();
        while (_position < _source.Length && _source[_position] != '\"' && _source[_position] != '”')
        {
            sb.Append(_source[_position]);
            _position++;
        }
        _position++;
        return new Token(TokenType.StringLiteral, sb.ToString(), _line);
    }

    private char Peek()
    {
        return _position + 1 < _source.Length ? _source[_position + 1] : '\0';
    }

    private Token MatchKeywordOrIdentifier(string word)
    {
        return word switch
        {
            "class" => new Token(TokenType.Class, word, _line),
            "import" => new Token(TokenType.Import, word, _line),
            "public" => new Token(TokenType.Public, word, _line),
            "private" => new Token(TokenType.Private, word, _line),
            "protected" => new Token(TokenType.Protected, word, _line),
            "static" => new Token(TokenType.Static, word, _line),
            "void" => new Token(TokenType.Void, word, _line),
            "if" => new Token(TokenType.If, word, _line),
            "else" => new Token(TokenType.Else, word, _line),
            "print" => new Token(TokenType.Print, word, _line),
            "int" => new Token(TokenType.Type, word, _line),
            "String[]" => new Token(TokenType.Type, word, _line),
            "ASM" => new Token(TokenType.ASM, word, _line),
            "checkpoint" => new Token(TokenType.Checkpoint, word, _line),
            "goto" => new Token(TokenType.Goto, word, _line),
            _ => new Token(TokenType.Identifier, word, _line)
        };
    }
}