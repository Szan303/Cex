# C! Compiler — Documentation (version 0.6.10)

This document describes the C! compiler v0.6.10: how to use it, language features supported, how to write libraries in C!, and implementation/interop notes relevant to this release.

Contents
- Overview
- What's new in 0.6.8
- Project layout and build
- Language reference (high-level)
  - Files & imports
  - Classes & functions
  - Function naming and dotted names
  - Statements and expressions
  - String interpolation
  - For/while/if
  - ASM blocks (inline assembly)
  - Float support
  - Printing and IO
  - Built-ins
- Writing libraries (pure C! libs)
- Writing native/ASM-backed functions
- Calling convention and codegen notes
- Symbol resolution rules
- Known limitations & migration notes
- Examples

---

## Overview

C! is a small compiled language that emits x86-64 assembly (NASM style) and links to a small runtime. Version 0.6.8 focuses on moving the standard library into pure C! library files, better module/class function registration, and initial float support (printing and returned values from ASM-backed functions). The compiler now registers class methods both as plain names and dot-qualified names (e.g. `sqrt` and `Math.sqrt`).

---

## What's new in 0.6.8

- Standard library functions removed from the compiler core and implemented as .ce library files (e.g., `Math.ce`).
- Imports search path expanded (same dir, `libs/`, project root, exe `libs/`).
- Class methods are registered under both `Name` and `ClassName.Name` to make calls flexible.
- Parser and symbol handling extended to support dotted function names and dotted function definitions via classes.
- Initial float support:
  - `float` type stored as 8 bytes (IEEE-754 double).
  - `Math.sqrt` example implemented via an ASM block returning a float (xmm -> rax via movq).
  - `__printFloat` helper prints floats with 3 decimal places.
- ASM blocks in functions implicitly return: when an `AsmBlock` is the last statement in a function, the compiler inserts a jump to the function return epilogue so the value in `rax` (or xmm0 converted to `rax` via movq) is returned.
- String equality compares contents (byte-by-byte) rather than pointer equality.
- For-loop `step` supports negative values (loop direction determined at runtime).
- `print` prints booleans as `true` / `false`.
- `length(x)` available as compiler intrinsic for strings and ints.

---

## Project layout and build

Recommended layout:

project/
- main.ce
- Math.ce
- libs/
  - String.ce
  - OtherLibs.ce

The compiler recursively finds `.ce` files starting from a specified `main.ce` or project root. Imports are resolved in this order:

1. Same directory as the importing file: `<dir>/<Module>.ce`
2. `<dir>/libs/<Module>.ce`
3. Project root: `<projectRoot>/<Module>.ce`
4. Project root libs: `<projectRoot>/libs/<Module>.ce`
5. Executable `libs` folder: `<exeDir>/libs/<Module>.ce`

How to compile:
- Use the provided driver (e.g. CLI or IDE run configuration). The compiler entry is `CexCompiler` and expects a project root containing `main.ce`.
- Example: place `main.ce` and `Math.ce` in the same folder and run the compiler; it will parse both and generate assembly/output.

(Exact invocation depends on your build/run wrapper; the library offers `new CexCompiler(projectRoot).Compile()` programmatically.)

---

## Language reference (concise)

### Files & imports
- Top of file: `import Math` — imports module `Math` (searches for `Math.ce` using search order above).
- `import System` is a special built-in import that refers to the runtime and is ignored by the import resolver.

### Classes & functions
- Functions may be declared at top level or inside a `class` block.
- Inside a class, functions are declared without the `Class.` prefix:
  ```ce
  class Math:
      float sqrt(int x):
          ASM:
              cvtsi2sd xmm0, rcx
              sqrtsd xmm0, xmm0
              movq rax, xmm0
  ```
- Functions inside a class are registered two ways:
  - Plain name: `sqrt`
  - Dot-qualified name: `Math.sqrt`
  This lets callers use either `sqrt(...)` or `Math.sqrt(...)` depending on preference/context.

### Function naming and dotted names
- Function calling syntax supports dotted calls: `Math.sqrt(x)` and `sqrt(x)` (if `sqrt` was registered).
- Parser supports dot-call tokens and function declarations where the name may be dotted (via class registration).

### Statements & expressions
- Common statements: `if`, `for`, `while`, `return`, `break`, `continue`, `print`, `input`, `goto`, `checkpoint`.
- Expressions support `+`, `-`, `*`, `/`, `%`, `==`, `!=`, `<`, `>`, `<=`, `>=`, logical `&&`, `||`, unary `-`, `not`.
- String concatenation uses `+`. If the left operand of `+` is a string, string concatenation is performed.

### String interpolation
- Strings support `${expr}` interpolation inside `"` string literals, e.g. `"value=${x}"`.
- `print` accepts interpolation and concatenation chains.

### For / While / If
- For loop syntax: `for i = start to end:` optional `step` value:
  ```
  for i = 10 to 0 step -1:
      print "${i}"
  ```
- Step can be negative. The compiler checks step sign at runtime and uses appropriate comparison (>=/<=).

### ASM blocks (inline assembly)
- `ASM:` introduces a block of assembly lines (NASM syntax). Example:
  ```ce
  float sqrt(int x):
      ASM:
          cvtsi2sd xmm0, rcx
          sqrtsd xmm0, xmm0
          movq rax, xmm0
  ```
- ASM blocks emit the assembly lines verbatim (prefixed with indentation) into the function body.
- If an `AsmBlock` is the last statement in a function, the compiler automatically appends a jump to the function return epilogue (`jmp __ret_<func>`). This allows ASM blocks to act like the return value provider — `rax` (or xmm0 moved to rax) becomes the function return.
- Use `rcx, rdx, r8, r9` for the first four integer/pointer parameters (Windows x64 convention used by this compiler). For float arguments you should convert/store according to conventions used in the assembly you write (see "Calling convention" below).

### Float support
- `float` type stores a 64-bit IEEE754 value (double) in memory (8 bytes).
- Function returning `float`: either implement in C! (pure C! math algorithm) or use an ASM block to produce `xmm0` and move it to `rax` (`movq rax, xmm0`) — the compiler will treat `rax` as the raw bits and store/return them.
- Printing floats: compiler provides `__printFloat` helper. It expects `xmm0` loaded with the double value. It prints the value as `integer.fraction` with 3 decimal places. Example printing in codegen will call `__printFloat`.
- Note: full floating point arithmetic is partial — arithmetic operators currently operate on integers; functions returning floats should be created or implemented via ASM or pure-C! algorithms.

### Printing and IO
- `print` supports:
  - string literals, interpolated strings, boolean, integers, and floats
  - booleans printed as `true` / `false`
  - `print` of `float` goes through `__printFloat`.
- `input` reads a line into a buffer accessible as a string var or parsed numeric value.

### Built-ins
- `exit(code)` — exits the process (compiler emits call to ExitProcess with code).
- `length(x)` — universal length:
  - If `x` is a string: returns number of characters.
  - If `x` is an int: returns the number of digits in its decimal representation (negative sign excluded).
  - Arrays and other types: planned / limited support (platform-specific).
- The large standard library was moved into .ce files (e.g., `Math.ce`, `String.ce`), not baked into the compiler.

---

## Writing libraries in C!

C! libraries are plain `.ce` files. Best practice: wrap related functions in a `class` so the compiler registers dotted names automatically.

Example `Math.ce`:
```ce
class Math:

    int abs(int n):
        if n < 0:
            return -n
        return n

    float sqrt(int x):
        ASM:
            cvtsi2sd xmm0, rcx
            sqrtsd   xmm0, xmm0
            movq     rax,  xmm0
```

Save `Math.ce` in the same folder as `main.ce` or in `libs/Math.ce`. Then in `main.ce`:

```ce
import Math

public static void Main():
    float f = Math.sqrt(2)
    print "sqrt(2) = " + f
```

Notes:
- Inside `class Math:` functions are declared by name (no `Math.` prefix).
- The compiler will register both `sqrt` and `Math.sqrt` so callers may use either.
- Prefer class-based libs for organization and to avoid parse awkwardness.

---

## Writing native/ASM-backed functions

- Use `ASM:` blocks inside a function to emit raw assembly.
- If the function's return type is `float`, you should produce the value in `xmm0` and then do `movq rax, xmm0` (or otherwise move the raw bits into `rax`). The compiler will treat the value in `rax` as the return bits and will place/stash them in the caller as appropriate.
- The compiler automatically appends `jmp __ret_<func>` after an ASM block if it's the last statement in the function, so you don't need to write `return` in that case — the ASM block acts as the provider of the return value.
- For integer returns, place the return value in `rax` as usual.

Example:
```ce
class Fast:

    float hypot(int x, int y):
        ASM:
            ; rcx = x, rdx = y
            cvtsi2sd xmm0, rcx
            cvtsi2sd xmm1, rdx
            mulsd xmm0, xmm0
            mulsd xmm1, xmm1
            addsd xmm0, xmm1
            sqrtsd xmm0, xmm0
            movq rax, xmm0
```

---

## Calling convention & codegen notes

- Parameter passing:
  - Integers/pointers: `rcx`, `rdx`, `r8`, `r9` for first four arguments (Windows x64 convention used here).
  - Additional arguments are pushed onto the stack by the compiler (the compiler pushes evaluated args into `rax` then `push` and then pops into the register order).
- Return values:
  - Integers: raw integer in `rax`.
  - Floats: expected in `xmm0` by `__printFloat`, but the compiler uses `movq rax, xmm0` between ASM and return so that `rax` holds the float bits for storage/return. The internal calling convention in generated code returns a float as raw bits in `rax` and the caller's code converts it to `xmm0` when needed for printing (the code generator handles this).
- Labels: function labels are sanitized: `.` in function names is replaced by `_` in label names (e.g. `Math.sqrt` → `__fn_Math_sqrt`).
- Heap: compiler uses an arena allocator exposed as `__heap_allocate` and `__heap_init`. Strings created by the compiler are allocated on the heap.

---

## Symbol resolution rules

- Files are parsed and imports resolved recursively; each file produces a `CompilationUnit`.
- Top-level functions are registered by name.
- Class methods are registered as both `MethodName` and `ClassName.MethodName`.
  - This allows `Factorial` declared inside `class Program` to be called by `Factorial(...)` or `Program.Factorial(...)`.
- Access control:
  - `public`, `private`, `protected` modifiers are tracked. Calls are validated at compile time; methods in the **same class** can call each other regardless of `private`/`protected` (this release relaxes access checks for same-class calls).
- The compiler considers the following built-ins intrinsic and handled by the compiler: `exit`, `length`.

---

## Known limitations & migration notes

- Float arithmetic operators (`+`, `-`, `*`, `/`) are not fully integrated into expression code emission. To produce floats you may rely on:
  - Pure-C! implementations using integer arithmetic (slow/approximate), or
  - ASM-backed functions that use SSE2 instructions.
- `print` float formatting is rudimentary: prints with 3 decimal places (rounded/truncated based on `cvttsd2si` used in helper).
- `AsmBlock` should be used with care: the code is emitted verbatim. Ensure your assembly preserves callee-saved registers (or rely on the function prologue/epilogue layout).
- `length(...)` for arrays is limited; strings and ints supported.
- No automatic floating-point promotion/conversions for arithmetic expressions — using float-returning functions in expressions will be detected by codegen and printed correctly, but mixing float/int arithmetic is limited.
- Error messages have improved but may still be terse for complex type errors.

---

## Examples

Math library (`Math.ce`):
```ce
class Math:

    int abs(int n):
        if n < 0:
            return -n
        return n

    float sqrt(int x):
        ASM:
            cvtsi2sd xmm0, rcx
            sqrtsd   xmm0, xmm0
            movq     rax, xmm0
```

Main program (`main.ce`):
```ce
import Math

class Program:

    private static int Factorial(int n):
        if n <= 1:
            return 1
        return n * Factorial(n - 1)

    int Add(int o):
        o += 5
        o = Math.sqrt(o)      ; returns float, assignment will require float variable
        return o              ; if function return type is float, ensure variable types match

    public static void Main():
        float f = Math.sqrt(2)
        print "sqrt(2) = " + f
```

Note: assign `Math.sqrt` result to a `float` variable (not shown above for `Add`, ensure types match).

---

## Developer notes (implementation pointers)

- Parser:
  - Supports dotted identifier parsing for `Math.sqrt` calls and function declarations (via class registration).
  - Lookaheads adjusted to accept `Type Identifier . Identifier` as a valid function header when inside class contexts.
- Symbol table:
  - Class members are entered both as plain names and `Class.Member` dotted names to support flexible calling.
- Code generation:
  - Functions labeled using sanitized names (`.` → `_`).
  - `AsmBlock` last-statement handling: compiler automatically emits `jmp __ret_<safeLabel>` so ASM blocks can act as return providers.
  - `__printFloat` helper implemented in assembly and emitted into the helpers section.
  - String equality implemented as byte-by-byte compare in generated assembly.
  - For-loops support negative step determined at runtime.
- Built-in/Intrinsic functions are minimal: `exit`, `length`. Everything else should be implemented in library `.ce` files.

---

## Roadmap (next items)
- Integrate full float arithmetic and expression emission (SSE2 path).
- Improve float formatting (configurable precision, rounding).
- Expand `length` to arrays and generic collections.
- Add `var` type inference and richer type checking.
- Better error messages and source mapping for ASM blocks.

---
