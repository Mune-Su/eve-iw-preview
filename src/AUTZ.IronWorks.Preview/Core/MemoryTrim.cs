using System.Runtime.InteropServices;

namespace AUTZ.IronWorks.Preview.Core;

/// <summary>
/// Gives memory back to Windows. A tray app spends nearly all its time idle, so after start-up and after the Configuration
/// window has been closed (the two moments memory peaks) the heap is compacted and the working set emptied: the pages the app
/// needs come back by themselves, the ones it doesn't stay out of RAM.
/// </summary>
internal static class MemoryTrim
{
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessWorkingSetSize(nint process, nint minimum, nint maximum);

    public static void Trim()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
        }
        catch { }
    }
}

