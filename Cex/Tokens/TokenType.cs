namespace Cex.Tokens;

public enum TokenType
{
    // Keywords
    Import, Class, Extends,
    Public, Private, Protected, Static, Void,
    If, Else, ElseIf,
    While, For, To, Break, Continue,
    Print, Input,
    Checkpoint, Goto,
    Return,
    ASM,
    True, False, Null,

    // Types
    Type,           // int, string, float, bool

    // Literals & names
    Identifier,
    Number,         // integer: 42
    FloatNumber,    // float:   3.14
    StringLiteral,
    BoolLiteral,    // true / false

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
    And,            // and
    Or,             // or
    Not,            // not

    // Punctuation
    Colon,
    Comma,
    Dot,
    ParenthesisOpen,
    ParenthesisClose,
    BracketOpen,    // [
    BracketClose,   // ]
    BraceOpen,      // {
    BraceClose,     // }

    // Layout
    Indent,
    Dedent,
    Newline,
    EOF,
}