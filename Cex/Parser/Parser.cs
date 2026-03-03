using Cex.AST;
using Cex.Tokens;
using System;
using System.Collections.Generic;

namespace Cex.Parser;

public class Parser
{
    private readonly List<Token> _tokens;
    private int _current = 0;

    public Parser(List<Token> tokens) => _tokens = tokens;

    public List<Expression> Parse()
    {
        var expressions = new List<Expression>();

        while (!IsAtEnd())
        {
            var expr = ParseStatement();
            if (expr != null)
                expressions.Add(expr);
        }

        return expressions;
    }

    private Expression ParseStatement()
    {
        // Ignorujemy import
        if (Match(TokenType.Import))
        {
            Consume(TokenType.Identifier, "Oczekiwano nazwy po import");
            return null;
        }

        // Ignorujemy klasy, metody, nagłówki
        if (Match(TokenType.Class, TokenType.Public, TokenType.Private, TokenType.Protected, TokenType.Static, TokenType.Void))
        {
            while (!Check(TokenType.Colon) && !IsAtEnd()) Advance();
            if (Check(TokenType.Colon)) Advance();
            return null;
        }

        if (Match(TokenType.Type))
        {
            string type = Previous().Value;
            string name = Consume(TokenType.Identifier, "Oczekiwano nazwy zmiennej").Value;
            Consume(TokenType.Equals, "Oczekiwano '='");
            string value = Consume(TokenType.Number, "Oczekiwano wartości").Value;

            return new VariableDeclaration { Type = type, Name = name, Value = value };
        }

        if (Match(TokenType.Checkpoint))
        {
            string name = Consume(TokenType.Identifier, "Oczekiwano nazwy checkpointu").Value;
            return new CheckpointStatement { Name = name };
        }

        if (Match(TokenType.Goto))
        {
            string target = Consume(TokenType.Identifier, "Oczekiwano nazwy checkpointu").Value;
            return new GotoStatement { TargetName = target };
        }

        if (Match(TokenType.Print))
        {
            string text = Consume(TokenType.StringLiteral, "Oczekiwano tekstu").Value;
            return new PrintStatement { Text = text };
        }

        if (Match(TokenType.ASM))
        {
            Consume(TokenType.Colon, "Oczekiwano ':' po ASM");
            return new AsmBlock { RawCode = "; ASM BLOCK" };
        }

        Advance(); // ignorujemy nieznane tokeny
        return null;
    }

    private bool Match(params TokenType[] types)
    {
        foreach (var t in types)
            if (Check(t)) { Advance(); return true; }
        return false;
    }

    private bool Check(TokenType type) => !IsAtEnd() && Peek().Type == type;
    private Token Advance() { if (!IsAtEnd()) _current++; return Previous(); }
    private Token Consume(TokenType type, string message) { if (Check(type)) return Advance(); throw new Exception($"{message} w linii {Peek().Line}"); }
    private bool IsAtEnd() => _current >= _tokens.Count || _tokens[_current].Type == TokenType.EOF;
    private Token Peek() => _tokens[_current];
    private Token Previous() => _tokens[_current - 1];
}