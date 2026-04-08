namespace Cex.Tokens;

public enum TokenType
{
    // Keywords
    Import, Class, Extends,
    Public, Private, Protected, Static,
    Void, Return,
    If, Else, ElseIf,
    While, For, To, Break, Continue,
    Print, Input,
    Checkpoint, Goto,
    ASM,
    True, False, Null,
    Try, Catch, Finally,
    And, Or, Not, Create,

    // Types
    Type,           // int, float, string, bool, char

    // Literals & names
    Identifier,
    Number,
    FloatNumber,
    StringLiteral,
    BoolLiteral,
    CharLiteral,    // 'x'

    // Operators
    Equals,         // =
    DoubleEquals,   // ==
    NotEquals,      // !=
    Less,           // <
    Greater,        // >
    LessEq,         // <=
    GreaterEq,      // >=
    PlusEquals,     // +=
    MinusEquals,    // -=
    StarEquals,     // *=
    SlashEquals,    // /=
    PlusPlus,       // ++
    MinusMinus,     // --
    Plus,           // +
    Minus,          // -
    Star,           // *
    Slash,          // /
    Percent,        // %
    New,
    PercentEquals,

    // Punctuation
    Colon,
    Comma,
    Dot,
    ParenthesisOpen,
    ParenthesisClose,
    BracketOpen,
    BracketClose,
    BraceOpen,
    BraceClose,

    // Layout
    Indent,
    Dedent,
    Newline,
    EOF,
}