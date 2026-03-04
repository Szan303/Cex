using System;
using System.IO;
using System.Diagnostics;
using Cex.Compiler;

class Program
{
    static void Main(string[] args)
    {
        // usage: Cex.exe <path-to-project-folder>
        // example: Cex.exe C:\Users\weter\Projects\MyGame
        string projectRoot;

        if (args.Length > 0 && Directory.Exists(args[0]))
        {
            projectRoot = args[0];
        }
        else if (args.Length > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: folder not found: {args[0]}");
            Console.ResetColor();
            return;
        }
        else
        {
            // fallback: use current working directory
            projectRoot = Directory.GetCurrentDirectory();
            Console.WriteLine($"No path given — using current directory: {projectRoot}");
        }

        // output files go next to the project folder, not inside it
        string projectName = Path.GetFileName(projectRoot.TrimEnd('\\', '/'));
        string outputDir   = projectRoot;
        string asmFile     = Path.Combine(outputDir, "output.asm");
        string objFile     = Path.Combine(outputDir, "output.obj");
        string exeFile     = Path.Combine(outputDir, $"{projectName}.exe");

        // paths to NASM and GCC — stored next to Cex.exe
        string compilerDir = AppContext.BaseDirectory;
        string nasmPath    = Path.Combine(compilerDir, "NASM", "nasm.exe");
        string gccPath     = Path.Combine(compilerDir,
            @"winlibs-x86_64-posix-seh-gcc-15.2.0-mingw-w64msvcrt-13.0.0-r1\mingw64\bin\gcc.exe");

        try
        {
            Console.WriteLine($"Compiling project: {projectRoot}");
            Console.WriteLine();

            var compiler = new CexCompiler(projectRoot);
            string asm   = compiler.Compile();

            File.WriteAllText(asmFile, asm);
            Console.WriteLine("ASM generated.");

            if (!File.Exists(nasmPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"NASM not found at: {nasmPath}");
                Console.ResetColor();
                return;
            }

            if (!File.Exists(gccPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"GCC not found at: {gccPath}");
                Console.ResetColor();
                return;
            }

            Console.WriteLine("Assembling...");
            RunProcess(nasmPath, $"-f win64 \"{asmFile}\" -o \"{objFile}\"");

            Console.WriteLine("Linking...");
            RunProcess(gccPath, $"\"{objFile}\" -o \"{exeFile}\" -nostartfiles -lkernel32");

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Done! Output: {exeFile}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Compile error: {ex.Message}");
            Console.ResetColor();
        }
    }

    static void RunProcess(string exe, string args)
    {
        var p = new Process();
        p.StartInfo.FileName               = exe;
        p.StartInfo.Arguments              = args;
        p.StartInfo.UseShellExecute        = false;
        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.RedirectStandardError  = true;
        p.Start();

        string output = p.StandardOutput.ReadToEnd();
        string err    = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
        if (!string.IsNullOrWhiteSpace(err))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(err);
            Console.ResetColor();
        }
    }
}