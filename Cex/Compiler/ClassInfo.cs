namespace Cex.Compiler;

public sealed class ClassInfo
{
    public string Name { get; init; } = "";

    // name -> (type, offset)
    public Dictionary<string, (string type, int offset)> InstanceFields { get; } = new();

    // static field name -> type (label is Name + "_" + fieldName)
    public Dictionary<string, string> StaticFields { get; } = new();

    public Cex.AST.ConstructorDeclaration? Constructor { get; set; }

    public int InstanceSizeBytes { get; set; }
}