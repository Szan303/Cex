namespace Cex.Tokens;

public enum TokenType
{
    Import,
    Class,
    Public,
    Private,
    Protected,
    Static,
    Void,
    If,
    Else,
    Print,
        
    Type,
    Identifier,
    Number,
    StringLiteral,
        
    ASM, // Assembler 
    Checkpoint, // checkpoint
    Goto, // goto
        
    Colon, // :
    Equals, // =
    DoubleEquals, // ==
    Comma, // ,
    ParenthesisOpen, // (
    ParenthesisClose, // )
        
    Indent,
    Dedent,
    EOF,
}
public class Token
{
    public TokenType Type { get; set; }
    public string Value { get; set; }
    public int Line { get; set; }

    public Token(TokenType type, string value, int line)
    {
        Type = type;
        Value = value;
        Line = line;
    }

    public override string ToString()
    {
        return $"Token({Type}, '{Value}', Line: {Line})";
    }
}