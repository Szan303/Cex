using System;

namespace Cex
{
    public enum ErrorKind
    {
        Lexer,
        Parser,
        Semantic,
        Codegen,
        Runtime,
    }

    public sealed class CompilerError : Exception
    {
        public ErrorKind Kind { get; }
        public int Line { get; }

        public CompilerError(ErrorKind kind, int line, string message)
            : base(message)
        {
            Kind = kind;
            Line = line;
        }

        public override string ToString()
            => $"[{Kind} error] on line {Line}: {Message}";
    }
}

