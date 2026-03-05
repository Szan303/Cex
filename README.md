# C! Compiler — Documentation

This README documents the current C! compiler: how to use it, language features (expressions, return expressions, arrays, nested calls, string interpolation), tooling/CLI, and migration notes for the updated parser/lexer/codegen.

Contents
- Overview
- Quick start (build & run)
- CLI and project layout
- Language reference
  - Files & multi-file projects
  - Lexical notes
  - Expressions (Pratt-style)
  - Functions & return expressions
  - Local variables & stack allocation
  - Arrays (heap-allocated)
  - Function calls and nested calls
  - Strings, interpolation and concatenation
  - Classes & access modifiers (public/protected/private)
  - Standard library (import System)
- Examples
- Migration notes (from previous compiler versions)
- Debugging & common errors
- Limitations & roadmap

---

## Overview

This C! compiler reads `.ce` source files under a project directory, parses them into an AST, validates calls and access modifiers, and emits a single assembly file which is assembled and linked into an executable. The compiler now includes:

- Full expression support (operator precedence, unary ops, boolean operators)
- Return expressions (any expression can be returned)
- Arrays with `new` syntax (heap-allocated, simple arena allocator)
- Nested and recursive function calls (argument evaluation and calling conventions)
- String interpolation using `${...}` inside string literals and string concatenation (`+`)
- Local variables allocated in function stack frames

---

## Quick start (build & run)

1. Build the compiler (your existing .NET / dotnet or IDE configuration).
2. Place your project in a folder (see "Project layout" below).
3. Run the compiler and pass the project's folder path:

```
Cex.exe C:\path\to\YourProject
```

If no path is passed, the default project root constant in `Program.cs` is used.

The compiler will:
- Scan `.ce` files recursively in the project folder
- Parse and validate them
- Emit `output.asm` to the project folder
- Assemble (NASM) and link (gcc/mingw) into `ProjectName.exe`

Paths to NASM and GCC are determined relative to the compiler binary; see `Program.cs` for configuration.

---

## CLI and project layout

Recommended project layout:

```
MyGame/
├── main.ce
├── math.ce
├── strings.ce
└── models/
    └── player.ce
```

Usage:

- Build the compiler and place NASM/GCC toolchains where `Program.cs` expects them, or adjust paths in `Program.cs`.
- Run: `Cex.exe <path-to-project-folder>`

Output: `output.asm`, `output.obj`, and `ProjectName.exe` are produced in the project folder.

---

## Language reference

This section summarizes syntax and semantics.

### Files & multi-file projects
- All `.ce` files under the project root are parsed and compiled together.
- Functions and classes declared in any file are available across the project (subject to access modifiers).
- Imports like `import System` enable stdlib functions; you do not need to `import` your own project files (the compiler finds them automatically).

### Lexical notes
- Indentation-based block structure (like Python).
- Strings are double-quoted.
- Identifiers: letters, digits, underscores; must not start with digits.
- New tokens:
  - `new` for array allocation
  - `[` and `]` for array indexing
  - `%=` (mod-assign) supported
- Comments: `//` to end of line.

### Expressions
- Full expression support with operator precedence and associativity:
  - Unary: `-`, `not`
  - Binary arithmetic: `* / %`, then `+ -`
  - Comparison: `< <= > >= == !=`
  - Boolean: `and` (`&&`), `or` (`||`)
- Parentheses allowed: `(a + b) * c`
- Expression nodes evaluate to `int` (for now) or `string` where appropriate.

Examples:
```
int a = (x + 2) * y
int ok = (x > 0) and (y < 10)
```

### Functions & return expressions
- Function declaration:
```
public static int Add(int x, int y):
    return x + y
```
- Any expression can be returned:
```
return (a + b) * 2
```
- Functions may be top-level or methods inside classes. Methods inside classes are registered with owner class info for access control.

### Local variables & stack allocation
- Local variables are created with `int`, `string`, etc. inside functions; they are allocated in the function's stack frame:
```
int x = 10
```
- The compiler computes a stack frame large enough for the function's locals (aligned to 16 bytes). Locals live in `[rbp - offset]`.

### Arrays
- Arrays are created with `new` and are heap-allocated via a simple arena allocator:
```
int[] arr = new int[10]
arr[0] = 5
int v = arr[1]
```
- The compiler stores an array pointer in a variable; elements are 8-byte integers.
- The allocator is a bump allocator (arena) exposed via internal helper labels (`__heap_alloc`, `__heap_reset`). This is currently simple and not GC’d.

### Nested function calls
- Arguments are evaluated and passed to the first four integer parameters using `rcx`, `rdx`, `r8`, `r9` (Win64 calling convention). Additional support for >4 args via stack can be added later.
- Nested and recursive calls are supported:
```
int z = Add(Mul(x, 2), y)
```

### Strings, interpolation and concatenation
- Strings: `"Hello world"`
- Interpolation: use `${expression}` inside a string literal. Example:
```
print "x=${x} result=${Add(x, 2)}"
```
- The compiler parses `${...}` as a mini-expression using the same expression parser, so any expression is allowed inside.
- Concatenation: `+` between strings or string variables performs allocation on the heap and concatenates both sides:
```
string s = "Hello" + " World"
```
- `print` behavior:
  - `print "hello"` — literal with newline
  - `print varName` — prints a variable (string or converted int)
  - `print "x=${x}"` — prints interpolation
- To include literal `${` or `$` or `{` in a string, escape or use `\{` or `\$` sequences in the source — the lexer handles escapes.

### Classes & access modifiers
- Class declaration:
```
class Program:
    private static int Add(int x):
        return x + 1
```
- Methods are registered with their owner class. Access modifiers:
  - `public`: callable from anywhere.
  - `protected` / `private`: callable only from within the owning class (and protected could later be extended for subclasses).
- `Main` inside any class is treated as the program entry `Main`.

### Standard library (import System)
Import `System` enables the following helpers:
- `exit(code)` — terminates the program (calls ExitProcess).
- `str(expr)` — returns a pointer to a heap string representing an integer (wrapper around integer-to-string helper).
- `math_abs(x)`, `math_max(a,b)`, `math_min(a,b)` — helper functions emitted inline.
- `str_len(s)` — returns length of a string (stdlib helper).

---

## Examples

Hello world + interpolation:
```ce
import System

public static void Main():
    int x = 5
    print "hello ${x}"   // prints "hello 5" + newline
```

Function, nested calls, local variables:
```ce
class Program:

    private static int Add(int a, int b):
        return a + b

    public static void Main():
        int x = 2
        int y = 3
        int z = Add(x, Add(y, 4))
        print "z=${z}"
```

Arrays:
```ce
public static void Main():
    int[] arr = new int[3]
    arr[0] = 10
    arr[1] = 20
    print "arr[1]=${arr[1]}"
```

String concatenation:
```ce
public static void Main():
    string a = "Hello"
    string b = "World"
    string c = a + " " + b
    print "${c}"
```

---

## Migration notes (important)

The parser/AST/lexer changed significantly. If you're porting old `.ce` files or compiler extensions, note these changes:

- AST:
  - Expressions are now first-class (NumberLiteral, StringLiteralExpr, VariableExpr, BinaryExpr, UnaryExpr, CallExpr).
  - VariableDeclaration.Init is an Expression (replaces old `Value` string + `InitCall`).
  - AssignmentStatement.Value is an Expression.
  - PrintStatement supports `Segments` (interpolation) instead of format strings using `{}`.
  - ArrayDeclaration, ArrayAccess, ArrayAssignment nodes are new.

- Parser:
  - Expression parser (Pratt-style) is implemented. You can write full expressions in assignments, returns, conditionals, array sizes, and `${...}` interpolation.
  - `print` interpolation uses `${expr}` to avoid accidental `{}` use. You requested `${x}` rather than `{x}` to avoid conflicts: use `${...}`.

- Lexer:
  - New tokens: `new`, `[`, `]`, `PercentEquals`, and improved string escapes.
  - String interpolation parsing occurs in the parser by feeding the `${...}` substring back into the lexer+parser.

- Code generation:
  - Locals are stack-allocated; function frames are pre-sized using `CountLocals` and aligned.
  - A simple heap arena is provided via `heapBuf` / `__heap_alloc`. Strings and arrays use it.
  - The integer-to-string helper and other helpers are emitted into the helpers section (below code) to avoid inline helper insertion.

If you have existing code that used old syntax (e.g., `print "x={x}"`), change to `print "x=${x}"`.

---

## Debugging & common errors

- "Call to undefined function 'foo'": either the function doesn't exist or access rules block it. Remember that `private` methods are only callable from the same owner class.
- "Undefined variable 'name'": variable used before declared or wrong scope (parameters of a different function).
- NASM label relocation or "label changed during code generation": this likely indicates frame-size computation issues — ensure `CountLocals` logic matches your AST (we take maximum of branches).
- If the generated assembly prints only part of expected output or prematurely jumps into helper code, ensure helpers are emitted into the `_helpers` section (the compiler emits helpers before function bodies).

For better error messages, line numbers are present on tokens but not yet tied into AST nodes in all places. Adding line numbers to AST nodes is a recommended next change.

---

## Limitations & roadmap

Current limitations:
- Strings are stored as pointers (no managed GC). The heap is a bump allocator (arena) and is reset only by explicit helpers. This is safe for small programs but not a full GC.
- Only integer arithmetic (and pseudo-floating via casted floats) — no IEEE floating-point register support yet.
- Arrays store 8-byte values (ints/pointers). No typed runtime checks.
- No method dispatch or instances: class fields are not per-instance—class bodies still map fields to global variables (future work).
- Only first 4 function arguments are passed in registers; >4 not implemented.
- Error messages could be more precise (line/column); this is planned.

Planned improvements (next):
- Real per-instance objects and member access
- Proper GC (mark & sweep or generational)
- Better error reporting with AST-located line/column
- Full support for >4 arguments (stack-based)
- Float support with XMM registers

---
