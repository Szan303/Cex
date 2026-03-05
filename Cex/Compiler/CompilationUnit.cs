using System.Collections.Generic;
using Cex.AST;

namespace Cex.Compiler;

/// <summary>
/// The result of lexing + parsing a single .ce source file.
/// </summary>
public class CompilationUnit
{
    /// <summary>Full path to the source file.</summary>
    public string SourceFile { get; set; }

    /// <summary>Top-level AST nodes from this file.</summary>
    public List<Expression> Expressions { get; set; } = new();

    /// <summary>Modules imported by this file (e.g. "Math", "Strings").</summary>
    public List<string> Imports { get; set; } = new();
}