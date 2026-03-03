// using System.Text;
// using Cex.AST;
// using System.Collections.Generic;
//
// namespace Cex.Compiler;
//
// public class CodeGenerator
// {
//     private int _stringCounter = 0;
//
//     public string Generate(List<Expression> expressions)
//     {
//         var asm = new StringBuilder();
//
//         // Deklaracje Windows API
//         asm.AppendLine("extern ExitProcess");
//         asm.AppendLine("extern WriteConsoleA");
//         asm.AppendLine("extern GetStdHandle");
//         asm.AppendLine("extern ReadConsoleA");
//
//         // Sekcja danych
//         asm.AppendLine("section .data");
//         var stringDefs = new StringBuilder();
//         var stringMap = new Dictionary<string, (string label, int length)>();
//
//         // Sekcja bss
//         asm.AppendLine("section .bss");
//         foreach (var expr in expressions)
//         {
//             if (expr is VariableDeclaration v)
//             {
//                 asm.AppendLine($"{v.Name}: resq 1"); // 8 bajtów dla int64
//             }
//         }
//         asm.AppendLine("written resd 1");
//         asm.AppendLine("inputChar resb 1"); // bufor dla ReadConsoleA
//
//         // Sekcja text
//         asm.AppendLine("section .text");
//         asm.AppendLine("global start");
//         asm.AppendLine("start:");
//
//         foreach (var expr in expressions)
//         {
//             switch (expr)
//             {
//                 case VariableDeclaration v:
//                     asm.AppendLine($"    mov qword [rel {v.Name}], {v.Value}");
//                     break;
//
//                 case CheckpointStatement c:
//                     asm.AppendLine($"{c.Name}:");
//                     break;
//
//                 case GotoStatement g:
//                     asm.AppendLine($"    jmp {g.TargetName}");
//                     break;
//
//                 case PrintStatement p:
//                     string strLabel;
//                     int len;
//                     if (!stringMap.ContainsKey(p.Text))
//                     {
//                         _stringCounter++;
//                         strLabel = $"msg{_stringCounter}";
//                         len = p.Text.Length;
//                         stringDefs.AppendLine($"{strLabel}: db \"{p.Text}\",0");
//                         stringMap[p.Text] = (strLabel, len);
//                     }
//                     else
//                     {
//                         (strLabel, len) = stringMap[p.Text];
//                     }
//
//                     asm.AppendLine("    ; --- print start ---");
//                     asm.AppendLine("    mov rcx, -11");        // STD_OUTPUT_HANDLE
//                     asm.AppendLine("    call GetStdHandle");   // rcx = handle
//                     asm.AppendLine($"    lea rdx, [rel {strLabel}]"); // buffer
//                     asm.AppendLine($"    mov r8d, {len}");           // liczba znaków
//                     asm.AppendLine("    lea r9, [rel written]");     // wskaźnik DWORD written
//                     asm.AppendLine("    call WriteConsoleA");
//                     asm.AppendLine("    ; --- print end ---");
//                     break;
//
//                 case AsmBlock a:
//                     asm.AppendLine($"    {a.RawCode}");
//                     break;
//             }
//         }
//
//         // Pauza na końcu programu
//         stringDefs.AppendLine("pauseMsg: db \"Press Enter to exit\",0xA,0");
//         asm.AppendLine("    ; --- pause start ---");
//         asm.AppendLine("    mov rcx, -11");          // STD_OUTPUT_HANDLE
//         asm.AppendLine("    call GetStdHandle");
//         asm.AppendLine("    lea rdx, [rel pauseMsg]");
//         asm.AppendLine("    mov r8d, 20");           // długość komunikatu
//         asm.AppendLine("    lea r9, [rel written]");
//         asm.AppendLine("    call WriteConsoleA");
//
//         asm.AppendLine("    mov rcx, -10");          // STD_INPUT_HANDLE
//         asm.AppendLine("    call GetStdHandle");
//         asm.AppendLine("    lea rdx, [rel inputChar]");
//         asm.AppendLine("    mov r8d, 1");
//         asm.AppendLine("    lea r9, [rel written]");
//         asm.AppendLine("    call ReadConsoleA");
//         asm.AppendLine("    ; --- pause end ---");
//
//         // Wyjście
//         asm.AppendLine("    mov rcx, 0");
//         asm.AppendLine("    call ExitProcess");
//
//         // Dodajemy stringi do sekcji .data
//         if (stringDefs.Length > 0)
//         {
//             asm.Insert(asm.ToString().IndexOf("section .data") + 13, "\n" + stringDefs.ToString());
//         }
//
//         return asm.ToString();
//     }
// }
using System.Text;
using Cex.AST;
using System.Collections.Generic;

namespace Cex.Compiler;

public class CodeGenerator
{
    private int _stringCounter = 0;

    public string Generate(List<Expression> expressions)
    {
        var asm = new StringBuilder();

        // Deklaracje Windows API
        asm.AppendLine("extern ExitProcess");
        asm.AppendLine("extern WriteConsoleA");
        asm.AppendLine("extern GetStdHandle");
        asm.AppendLine("extern ReadConsoleA"); // potrzebne do pauzy

        // Sekcja danych
        asm.AppendLine("section .data");
        var stringDefs = new StringBuilder();
        var stringMap = new Dictionary<string, string>();

        // Sekcja bss
        asm.AppendLine("section .bss");
        foreach (var expr in expressions)
        {
            if (expr is VariableDeclaration v)
                asm.AppendLine($"{v.Name}: resq 1"); // 8 bajtów dla int64
        }
        asm.AppendLine("written resd 1");
        asm.AppendLine("inputChar resb 1"); // na Enter

        // Sekcja text
        asm.AppendLine("section .text");
        asm.AppendLine("global start");
        asm.AppendLine("start:");

        foreach (var expr in expressions)
        {
            switch (expr)
            {
                case VariableDeclaration v:
                    asm.AppendLine($"    mov qword [rel {v.Name}], {v.Value}");
                    break;

                case CheckpointStatement c:
                    asm.AppendLine($"{c.Name}:");
                    break;

                case GotoStatement g:
                    asm.AppendLine($"    jmp {g.TargetName}");
                    break;

                case PrintStatement p:
                    string strLabel;
                    if (!stringMap.ContainsKey(p.Text))
                    {
                        _stringCounter++;
                        strLabel = $"msg{_stringCounter}";
                        stringDefs.AppendLine($"{strLabel}: db \"{p.Text}\",0");
                        stringMap[p.Text] = strLabel;
                    }
                    else strLabel = stringMap[p.Text];

                    asm.AppendLine("    ; --- print start ---");
                    asm.AppendLine("    mov rcx, -11");   // STD_OUTPUT_HANDLE
                    asm.AppendLine("    call GetStdHandle");
                    asm.AppendLine("    mov rdx, rax");   // handle konsoli
                    asm.AppendLine($"    lea r8, [rel {strLabel}]");
                    asm.AppendLine($"    mov r9d, {p.Text.Length}");
                    asm.AppendLine("    lea r10, [rel written]");
                    asm.AppendLine("    call WriteConsoleA");
                    asm.AppendLine("    ; --- print end ---");
                    break;

                case AsmBlock a:
                    asm.AppendLine($"    {a.RawCode}");
                    break;
            }
        }

        // --- DODAJEMY PAUZĘ NA KONIEC ---
        string pauseLabel = "msgPause";
        stringDefs.AppendLine($"{pauseLabel}: db \"Press Enter to exit\",0xA,0");

        asm.AppendLine("    ; --- pause start ---");
        asm.AppendLine("    mov rcx, -11");                  // STD_OUTPUT_HANDLE
        asm.AppendLine("    call GetStdHandle");
        asm.AppendLine("    mov rdx, rax");                  // handle konsoli
        asm.AppendLine($"    lea r8, [rel {pauseLabel}]");
        asm.AppendLine("    mov r9d, 20");                   // liczba znaków
        asm.AppendLine("    lea r10, [rel written]");
        asm.AppendLine("    call WriteConsoleA");

        asm.AppendLine("    mov rcx, -10");                  // STD_INPUT_HANDLE
        asm.AppendLine("    call GetStdHandle");
        asm.AppendLine("    lea rdx, [rel inputChar]");       // wskaźnik na Enter
        asm.AppendLine("    mov r8d, 1");                    // wczytaj 1 znak
        asm.AppendLine("    lea r9, [rel written]");
        asm.AppendLine("    call ReadConsoleA");
        asm.AppendLine("    ; --- pause end ---");

        // --- KONIEC PROGRAMU ---
        asm.AppendLine("    mov rcx, 0");
        asm.AppendLine("    call ExitProcess");

        // Dodajemy stringi do sekcji .data na początku
        if (stringDefs.Length > 0)
            asm.Insert(asm.ToString().IndexOf("section .data") + 13, "\n" + stringDefs.ToString());

        return asm.ToString();
    }
}