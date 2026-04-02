# Cex (C!) — v0.6.10

Cex is a small indentation-based language that compiles to x86_64 NASM assembly (Windows) and runs via WinAPI (`WriteConsoleA`, `ReadConsoleA`, `ExitProcess`).

This document describes the language features available in **v0.6.10**.

---

## Table of contents
- [Basics](#basics)
- [Types](#types)
- [Variables](#variables)
- [Print](#print)
- [Input](#input)
- [Operators](#operators)
- [Control flow](#control-flow)
- [Functions](#functions)
- [Arrays (dynamic)](#arrays-dynamic)
- [Standard library](#standard-library)
- [Comments](#comments)
- [ASM blocks](#asm-blocks)
- [Notes / gotchas](#notes--gotchas)

---

## Basics

Cex uses indentation to define blocks (similar to Python):

```ce
if x == 10:
    print "x is 10"
print "done"
```

Statements are separated by newlines.

---

## Types

Supported built-in types in v0.6.10:

- `byte`
- `short`
- `int`
- `long`
- `float` (prints with 3 decimals)
- `string`
- `bool`
- `char`
- `void` (function return type)

`null` exists and is represented as `0` internally.

---

## Variables

Declare variables with:

```ce
int a = 10
string name = "abc"
bool ok = true
char c = 'x'
```

Uninitialized variables default to `0` / `null`:

```ce
string s
print s     # prints null
```

Assignments:

```ce
a = 5
a += 2
a -= 1
a *= 3
a /= 2
a %= 2

a++
a--
++a
--a
```

---

## Print

### Print string literals
```ce
print "hello"
print "line1\nline2"
print "tab:\tX"
```

### Print variables
```ce
print a
print name
print ok
print c
```

### String concatenation with `+`
```ce
print "value = " + a
print "name = " + name
```

### String interpolation: `${ ... }`
You can embed expressions inside strings:

```ce
print "a = ${a}"
print "sum = ${a + 10}"
print "len = ${length(name)}"
```

Interpolation is compiled into concatenation under the hood.

---

## Input

Read input into an existing variable:

```ce
string x = ""
input x
print "you typed: " + x
```

If the target is numeric, Cex parses digits from the input:

```ce
int n
input n
print n
```

---

## Operators

Arithmetic:
- `+`, `-`, `*`, `/`, `%`

Comparisons:
- `==`, `!=`, `<`, `<=`, `>`, `>=`

Boolean:
- `and`, `or`, `not`
- Also supports `&&` / `||` internally (produced by parser)

---

## Control flow

### If / else if / else
```ce
if x > 10:
    print "big"
else if x == 10:
    print "equal"
else:
    print "small"
```

### While
```ce
while x < 10:
    x++
```

### For
```ce
for i = 0 to 10:
    print i
```

Notes:
- `to` is **inclusive** (0 to 10 runs 11 iterations).
- Optional step:
```ce
for i = 0 to 10 step 2:
    print i
```

### break / continue
```ce
while true:
    break
```

---

## Functions

Define functions with `:` and an indented block:

```ce
public static void Main():
    print "Hello"
```

Parameters:

```ce
public int add(int a, int b):
    return a + b
```

Return:

```ce
return
return expr
```

---

## Arrays (dynamic)

v0.6.10 introduces dynamic arrays (vector-like):

### Declaration

Create an empty dynamic array with an initial capacity:

```ce
int[10] table
string[4] names
```

- The array starts with **length = 0**
- The initial number (`10`, `4`, etc.) is the **initial capacity**
- Use `.add(...)` to append values

### Add
```ce
table.add(123)
table.add(999)
```

`.add` appends to the end. Capacity grows automatically when needed.

### Delete
```ce
table.delete(0)   # removes element at index 0 and shifts the rest left
```

Bounds:
- `delete(i)` traps if `i < 0` or `i >= length(table)`.

### Indexing
Read:
```ce
print table[0]
```

Write:
```ce
table[0] = 777
```

Bounds:
- `table[i]` traps if `i < 0` or `i >= length(table)`.

### Printing arrays
Printing an array variable shows:

```ce
print table
```

Output format:
- integers: `[1,2,3]`
- string arrays print `null` for null elements
- null array pointer prints `null`

---

## Standard library

### `exit([code])`
```ce
exit()
exit(1)
```

### `length(x)`
Works for:
- arrays: returns number of elements currently in the array
- strings: returns string length (`length(null)` = 0)
- numbers: returns digit count (negative sign not counted)

Examples:
```ce
print length("abcd")   # 4
print length(table)    # array length
```

---

## Comments

Single-line comments use `//`:

```ce
// this is a comment
print "hi"
```

---

## ASM blocks

Inline assembly block:

```ce
ASM:
    mov rax, 123
    ; ...
```

ASM blocks can jump to function return label in some contexts (implementation detail).

---

## Notes / gotchas

- Blocks are indentation-based. Incorrect indentation changes program meaning.
- `for i = 0 to N` is inclusive.
- `null` is represented as `0` internally.
- Arrays and strings are pointers under the hood; printing a pointer as an integer is usually a bug in codegen or type tracking.

---

## Changelog (v0.6.10)
- Added dynamic arrays with `TYPE[initSize] name`
- Added `name.add(value)` and `name.delete(index)`
- `name[index] = value` assignment supported
- `print arrayVar` prints `[a,b,c]` format
