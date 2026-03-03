# C! — Language & Compiler Documentation

C! is a simple high-level language that combines Python-like syntax with low-level control and inline assembly. It is an experimental educational language and compiler that compiles C!-source (.ce) to NASM assembly and links to a Windows executable.

This documentation covers:
- language overview and goals
- syntax and examples
- supported features and limitations
- how to build/compile programs with the current compiler
- architecture notes and important implementation details
- how to extend the compiler and debugging tips

---

## Quick example

main.ce
```c!
import System

class Program:
    public static void Main:

        checkpoint a
        print "hello world!"
        int x = 1

        if x == 20:
            print "x equals 20"
        else:
            print "nuh uh"
            x = x + 1
            goto a
```

Compile with the compiler executable (example pipeline implemented in the project):
1. The compiler (Cex.exe) reads `main.ce`, lexes, parses, generates `output.asm`.
2. Assemble with NASM: `nasm -f win64 output.asm -o output.obj`
3. Link with mingw-w64 GCC: `gcc output.obj -o output.exe -nostartfiles -lkernel32`

---

## Design goals

- Easy to learn (syntax inspired by Python)
- Statically typed variables for simple safety (`int`, `string`, `float`, `bool`)
- Low-level capabilities via inline assembly (`ASM:` block) and direct memory-level control
- Simple control flow primitives (if/else, while, for, goto/checkpoints)
- Simple object/class declarations with optional inheritance (structure only; methods are not fully supported yet)

---

## What the language currently supports

- Basic types: `int`, `string`, `float`, `bool`
- Variable declaration:
  - `int x = 5`
  - `int x` (declaration without initialization)
  - `string s = "hello"`
- Assignments:
  - `x = 5`
  - `x = x + 1`
  - compound: `x += 1`, `x -= 1`, `x *= 2`, `x /= 3`
  - increment/decrement: `x++`, `++x`, `x--`, `--x`
- Control flow:
  - `if <cond>:` ... `else if <cond>:` ... `else:`
  - `while <cond>:` ... 
  - `for i = 0 to 10:` ... (`step` optional)
  - `break` / `continue` within loops
- Checkpoint/goto:
  - `checkpoint a`
  - `goto a` — implemented via labels and unconditional jumps
- Input / Output:
  - `print "text"` (string literal)
  - `print x` (variable)
  - `print "x = {x}"` (simple format placeholders)
  - `input x` (reads a line, parses to `int` if variable type is `int`, otherwise stores pointer to buffer for strings)
- Inline assembly:
  - `ASM:` block with indented raw lines that are placed verbatim into the generated assembly
- Basic classes and inheritance:
  - `class Foo:` and `class Foo <Bar>:` are parsed and stored (class bodies are emitted for now as top-level statements)

---

## What the language does NOT (yet) support

- Full method/function definitions and calls (function parsing/emission not implemented)
- Method dispatch, fields with managed memory, objects beyond flat storage
- Rich standard library (only console IO and simple numeric conversions)
- Full type checking and conversions (some implicit conversions are assumed)
- Advanced expressions parsing (no operator precedence tree beyond simple `x = a op b` and unary forms)
- Multi-file compilation and linking of C! modules (single-file programs only)
- Full string/formatting capabilities (only simple placeholder replacement)

---

## Lexical & Syntax notes

- Syntax is indentation-based (like Python). The lexer emits `Indent` / `Dedent` tokens.
- Lines end with newline tokens; blocks require `:` after control/definition header and an indented block below.
- Keywords are case-sensitive and include: `import`, `class`, `public`, `static`, `void`, `if`, `else`, `while`, `for`, `to`, `break`, `continue`, `print`, `input`, `checkpoint`, `goto`, `ASM`, `int`, `string`, `float`, `bool`, `return`.
- Identifiers: letters, digits and underscore, must not start with a digit.
- Strings support escape sequences (`\n`, `\t`, `\\`, `\"`).

---

## Generated assembly (Win64) — important ABI details

- Console output uses `WriteConsoleA`, input uses `ReadConsoleA`. Both are called following the Windows x64 calling convention:
  - Registers: rcx, rdx, r8, r9 for first 4 parameters; additional parameter passed on the stack slot (shadow space).
  - The code generator reserves shadow space (`sub rsp, 40`) before calls and restores it afterwards.
- All strings are placed into the `.data` section with `db "..."` and a trailing `0xA` (newline) when appropriate.
- Variables are reserved in `.bss` as `resq 1` (8 bytes) and accessed as `[rel varName]`.
- Label naming:
  - Do not use local labels starting with `.` for code generated across scopes because NASM interprets `.label` as local to the previous non-dot label; this caused symbol-scoping issues when hosts (like checkpoints) introduced new labels. The generator therefore uses global or prefixed labels (e.g., `__endif_0`, `__while_1`) to avoid collisions and scoping surprises.

---

## Common assembler/linker problems and fixes

- Error: `symbol 'a.endif_0' not defined` and `label changed during code generation`  
  Cause: using leading-dot labels (e.g. `.endif_0`) when a prior global or named label (like `a:` checkpoint) is present. NASM makes `.endif_0` local to `a:` (i.e., `a.endif_0`) — then references from other places mismatch.  
  Fix: use globally-unique label names without a leading dot (for example `__endif_0`) or ensure you generate labels that are unique and global.

- Error: `_intToStr label changed during code generation`  
  Cause: helper subroutine used local labels that collided with other local labels, or labels emitted before/after changing scopes.  
  Fix: use globally-named helper labels (`__intToStr`, `__its_digit`, etc.) and never rely on leading-dot local labels inside code that may be placed after other labels.

- Linker error `cannot find output.obj`  
  Cause: NASM assembly failed (previous errors) so `output.obj` was never created. Fix the assembler errors first, then re-run the pipeline.

---

## Project structure (current implementation)

- Cex.Lexer — lexer producing Token sequence with Indent/Dedent/Newline tokens
- Cex.Tokens — token definitions (TokenType, Token)
- Cex.Parser — parser producing a simple AST (Expression nodes)
- Cex.AST — AST node definitions (VariableDeclaration, IfStatement, PrintStatement, etc.)
- Cex.Compiler — code generator that converts AST to NASM assembly (Windows x64 style)
- Program.cs — small frontend that ties the pipeline together and invokes nasm/gcc to produce an executable

---

## How to use the compiler (development version)

The repository includes a small driver program that:
1. Reads `main.ce` (project-root)
2. Runs the lexer → parser → code generator to produce `output.asm`
3. Invokes NASM and then gcc (mingw-w64) to produce `output.exe`

Configurable items in `Program.cs`:
- `nasmPath` — path to `nasm.exe`
- `gccPath` — path to `gcc.exe` (mingw-w64)
- `inputFileName` — path to `.ce` input file
- `asmFileName`, `objFileName`, `exeFileName` — output paths

Example run (Windows; sample paths in project):
```
Cex.exe         # reads main.ce -> output.asm, assembles and links -> output.exe
```

If `nasm` or `gcc` are missing or misconfigured, the driver prints "Nie znaleziono <path>" (not found); ensure paths are correct.

---

## Extending the language / compiler: where to start

1. AST & Parser:
   - Add new AST node types to `Cex.AST` for the feature.
   - Update `Cex.Lexer` to emit any new tokens.
   - Update `Cex.Parser` to parse the new syntax into AST nodes.

2. Code generation:
   - Update `Cex.Compiler.CodeGenerator` to handle the new AST node and emit appropriate assembly.
   - Remember Windows x64 calling convention for any helper calls and maintain proper stack alignment (shadow space) around calls.

3. Testing:
   - Create small `.ce` test inputs that exercise the new syntax.
   - Run the driver to generate assembly and check NASM and linker stage output.

4. Debugging tips:
   - Inspect generated `output.asm` carefully; NASM errors often point to label scoping or invalid instructions.
   - If assembler reports "label changed during code generation" — search for leading-dot labels or label collisions; make labels globally unique.
   - If the generated exe runs and immediately exits, check the pause/input code: `ReadConsoleA` must get `STD_INPUT_HANDLE` (-10) not `-11`.

---

## Examples

Print, variables, if/else:
```c!
int x = 0
print "Start"
while x < 3:
    print "x = {x}"
    x = x + 1
print "Done"
```

Input:
```c!
print "Enter a number: "
input x
print "You entered {x}"
```

Inline assembly example:
```c!
ASM:
    mov rax, 5
    ; ... raw assembly placed into output.asm as-is
```

Checkpoint / goto:
```c!
checkpoint loopStart
print "looping"
goto loopStart
```

Class (declaration only at the moment):
```c!
class Base:
    int baseVal = 1

class Derived <Base>:
    int derivedVal = 2
```

---

## Roadmap / Next steps

Short term:
- Better expression parsing with operator precedence
- Function definitions & calls, return values
- Improved type system & type checking
- More robust ASM block handling (preserve edge characters `[]`, `,`, etc.)

Medium term:
- Standard library (strings, math)
- Multi-file compilation and simple linking
- Structured classes with fields & methods

Long term:
- Better optimization in generator
- Cross-platform support (Linux/macOS assembler/linker paths & syscalls)
- Package manager / standard library modules

---

## Contribution & development notes

- The codebase is structured to be small and understandable — contributions welcome.
- When adding labels from the code generator, always ensure labels are globally-unique (use a counter and a prefix like `__lbl_{n}`) — do not rely on `.local` labels when code spans scopes and functions.
- Keep helper routines (like `__intToStr`) in a helper area appended after main code to avoid interfering with label scoping and to keep the `start:` code contiguous.

---

## Contact & support

If you run into issues:
- Inspect `output.asm` and run `nasm -f win64 output.asm -o output.obj` manually to get assembler errors and line numbers.
- Fix label naming or missing symbols first; they commonly cause downstream linker failures.

---

## License

MIT.

---

Thank you — this README should help you use, debug and extend the C! language and its compiler. 
