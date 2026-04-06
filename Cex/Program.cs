using System;
using System.IO;
using System.Diagnostics;
using Cex.Compiler;

class Program
{
    // =====================================================================
    // SET YOUR PROJECT FOLDER HERE
    // =====================================================================
    private const string DefaultProjectRoot = @"C:\Users\weter\Projects\MyProject";
    // =====================================================================

    static void Main(string[] args)
    {
        string projectRoot;

        if (args.Length > 0)
        {
            // passed as command line argument: Cex.exe "C:\path\to\project"
            projectRoot = args[0];
        }
        else
        {
            // use the constant above
            // projectRoot = DefaultProjectRoot;
            projectRoot = @"C:\Users\weter\RiderProjects\Cex\Cex";
        }

        // validate
        if (!Directory.Exists(projectRoot))
        {
            Error($"Project folder not found: {projectRoot}");
            Error("Usage: Cex.exe <path-to-project-folder>");
            Error($"   or: set DefaultProjectRoot in Program.cs");
            return;
        }

        // paths to tools — always relative to Cex.exe location
        // string compilerDir = AppContext.BaseDirectory;
        string nasmPath    = Path.Combine(projectRoot, "NASM", "nasm.exe");
        string gccPath     = Path.Combine(projectRoot,
            @"winlibs-x86_64-posix-seh-gcc-15.2.0-mingw-w64msvcrt-13.0.0-r1\mingw64\bin\gcc.exe");

        // output files go into the project folder
        string projectName = Path.GetFileName(projectRoot.TrimEnd('\\', '/'));
        string asmFile     = Path.Combine(projectRoot, "output.asm");
        string objFile     = Path.Combine(projectRoot, "output.obj");
        string exeFile     = Path.Combine(projectRoot, $"{projectName}.exe");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("===========================================");
        Console.WriteLine("         C! Compiler");
        Console.WriteLine("===========================================");
        Console.ResetColor();
        Console.WriteLine($"Project : {projectRoot}");
        Console.WriteLine($"Output  : {exeFile}");
        Console.WriteLine();

        try
        {
            // ---- 1. compile .ce → .asm ----
            Console.Write("Compiling... ");
            var cex   = new CexCompiler(projectRoot);
            string asm = cex.Compile();
            File.WriteAllText(asmFile, asm);
            Ok("^^^^^ done ^^^^^");

            // ---- 2. assemble .asm → .obj ----
            Console.Write("Assembling... ");
            if (!File.Exists(nasmPath))
            {
                Error($"NASM not found at {nasmPath}");
                return;
            }
            bool nasmOk = RunProcess(nasmPath, $"-f win64 \"{asmFile}\" -o \"{objFile}\"");
            if (!nasmOk) { Error("NASM failed — check output.asm for errors"); return; }
            Ok("done");

            // ---- 3. link .obj → .exe ----
            Console.Write("Linking...    ");
            if (!File.Exists(gccPath))
            {
                Error($"GCC not found at {gccPath}");
                return;
            }
            bool gccOk = RunProcess(gccPath,
                $"\"{objFile}\" -o \"{exeFile}\" -nostartfiles -lkernel32");
            if (!gccOk) { Error("GCC linker failed"); return; }
            Ok("done");

            // ---- success ----
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Build succeeded → {exeFile}");
            Console.ResetColor();
        }
        catch (Cex.CompilerError e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(e.ToString());
            Console.ResetColor();
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Internal error] " + e.Message);
            Console.ResetColor();
        }
    }

    // ----------------------------------------------------------------- helpers
    static bool RunProcess(string exe, string arguments)
    {
        var p = new Process();
        p.StartInfo.FileName               = exe;
        p.StartInfo.Arguments              = arguments;
        p.StartInfo.UseShellExecute        = false;
        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.RedirectStandardError  = true;
        p.Start();

        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine(stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(stderr);
            Console.ResetColor();
        }

        return p.ExitCode == 0;
    }

    static void Ok(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    static void Error(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {msg}");
        Console.ResetColor();
    }
}