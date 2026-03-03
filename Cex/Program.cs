using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using Cex.Lexer;
using Cex.Parser;
using Cex.Compiler;
using Cex.Tokens;

class Program
{
    static void Main()
    {
        string projectRoot = @"C:\Users\weter\RiderProjects\Cex\Cex";

        string inputFileName = Path.Combine(projectRoot, "main.ce");
        string asmFileName = Path.Combine(projectRoot, "output.asm");
        string objFileName = Path.Combine(projectRoot, "output.obj");
        string exeFileName = Path.Combine(projectRoot, "output.exe");

        // Ścieżki do NASM i GCC w projekcie
        string nasmPath = Path.Combine(projectRoot, "NASM", "nasm.exe");
        string gccPath = Path.Combine(projectRoot,
            @"winlibs-x86_64-posix-seh-gcc-15.2.0-mingw-w64msvcrt-13.0.0-r1\mingw64\bin\gcc.exe");

        if (!File.Exists(inputFileName))
        {
            Console.WriteLine($"Nie znaleziono pliku {inputFileName}");
            return;
        }

        string source = File.ReadAllText(inputFileName);

        // Lexer + Parser
        var lexer = new Lexer(source);
        List<Token> tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Generacja ASM
        var generator = new CodeGenerator();
        string asm = generator.Generate(ast);
        File.WriteAllText(asmFileName, asm);
        Console.WriteLine("Wygenerowano ASM.");

        // Wywołanie NASM
        RunProcess(nasmPath, $"-f win64 \"{asmFileName}\" -o \"{objFileName}\"");

        // Wywołanie GCC (linker)
        RunProcess(gccPath, $"\"{objFileName}\" -o \"{exeFileName}\" -nostartfiles -lkernel32");

        Console.WriteLine($"Gotowe! Plik exe: {exeFileName}");

    }

    static void RunProcess(string exe, string args)
    {
        if (!File.Exists(exe))
        {
            Console.WriteLine($"Nie znaleziono {exe}");
            return;
        }

        var p = new Process();
        p.StartInfo.FileName = exe;
        p.StartInfo.Arguments = args;
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.RedirectStandardError = true;
        p.Start();

        string output = p.StandardOutput.ReadToEnd();
        string err = p.StandardError.ReadToEnd();

        p.WaitForExit();

        if (!string.IsNullOrEmpty(output))
            Console.WriteLine(output);
        if (!string.IsNullOrEmpty(err))
            Console.WriteLine(err);
    }
}