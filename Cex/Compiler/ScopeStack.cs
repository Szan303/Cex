using System.Collections.Generic;

namespace Cex.Compiler;

/// <summary>
/// Tracks local variable offsets within a function's stack frame.
/// Each function call creates a new scope; nested blocks push/pop child scopes.
/// </summary>
public class ScopeStack
{
    // each entry: varName → (type, rbp offset)  e.g. x → ("int", -8)
    private readonly Stack<Dictionary<string, (string type, int offset)>> _scopes = new();
    private int _currentOffset = 0;   // grows negative from rbp

    // ---------------------------------------------------------------- lifetime
    public void EnterFunction()
    {
        _scopes.Clear();
        _currentOffset = 0;
        _scopes.Push(new Dictionary<string, (string, int)>());
    }

    public void ExitFunction() => _scopes.Clear();

    public void EnterBlock() =>
        _scopes.Push(new Dictionary<string, (string, int)>());

    public void ExitBlock()
    {
        if (_scopes.Count > 1) _scopes.Pop();
    }

    // ---------------------------------------------------------------- declare
    /// <summary>Declare a local variable, returns its rbp offset (negative).</summary>
    public int Declare(string name, string type)
    {
        _currentOffset -= 8;   // all vars are 8 bytes (qword)
        _scopes.Peek()[name] = (type, _currentOffset);
        return _currentOffset;
    }

    // ---------------------------------------------------------------- lookup
    /// <summary>Returns (type, offset) or null if not found in any scope.</summary>
    public (string type, int offset)? Lookup(string name)
    {
        foreach (var scope in _scopes)
            if (scope.TryGetValue(name, out var info))
                return info;
        return null;
    }

    public bool IsLocal(string name) => Lookup(name) != null;

    /// <summary>Total bytes needed for all locals in current function.</summary>
    public int FrameSize => -_currentOffset;
}