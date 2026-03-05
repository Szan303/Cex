namespace Cex.Compiler;

/// <summary>
/// Emits a simple bump-pointer heap allocator into the BSS and helpers sections.
/// Allocation: call __heap_alloc with rcx = byte count, returns ptr in rax.
/// Reset: call __heap_reset (e.g. between major operations).
/// </summary>
public static class ArenaAllocator
{
    public const int HeapSize = 1024 * 1024; // 1 MB

    public static void EmitBss(System.Text.StringBuilder bss)
    {
        bss.AppendLine($"heapBuf  resb {HeapSize}");
        bss.AppendLine("heapPtr  resq 1");
    }

    public static void EmitHelpers(System.Text.StringBuilder helpers)
    {
        helpers.AppendLine("""
                           ; ================================================================= heap
                           ; __heap_init: call once at program start
                           __heap_init:
                               lea  rax, [rel heapBuf]
                               mov  [rel heapPtr], rax
                               ret

                           ; __heap_alloc: rcx = bytes to allocate, returns ptr in rax
                           ;               returns 0 if out of memory
                           __heap_alloc:
                               mov  rax, [rel heapPtr]
                               lea  rdx, [rel heapBuf]
                               add  rdx, heapSize
                               mov  r8,  rax
                               add  r8,  rcx
                               cmp  r8,  rdx
                               jg   __heap_oom
                               mov  [rel heapPtr], r8
                               ret
                           __heap_oom:
                               xor  rax, rax
                               ret

                           ; __heap_reset: free all heap memory (arena reset)
                           __heap_reset:
                               lea  rax, [rel heapBuf]
                               mov  [rel heapPtr], rax
                               ret
                           ; =================================================================
                           """);
        // emit heapSize as a constant
        helpers.AppendLine($"heapSize equ {HeapSize}");
    }
}