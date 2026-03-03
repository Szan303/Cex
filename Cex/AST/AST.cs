using System.Text;

namespace Cex.AST;

public abstract class Expression { }

public class VariableDeclaration : Expression
{
    public string Type { get; set; }
    public string Name { get; set; }
    public string Value { get; set; }
}

public class CheckpointStatement : Expression
{
    public string Name { get; set; }
}

public class GotoStatement : Expression
{
    public string TargetName { get; set; }
}

public class AsmBlock : Expression
{
    public string RawCode { get; set; }
}

public class PrintStatement : Expression
{
    public string Text { get; set; }
    public int Length => Encoding.ASCII.GetByteCount(Text);  // długość w bajtach
}