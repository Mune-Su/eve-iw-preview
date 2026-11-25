using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// The thread the keyboard and mouse hooks live on. Every key and click on the PC waits on these hooks, so they must never
/// wait behind thumbnail drawing or anything else the app is doing. The hooks only match a press and hand it on.
/// </summary>
internal static class HookThread
{
    private const uint WM_RUN = 0x8001;          // WM_APP + 1

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public nint hwnd; public uint message; public nint wParam; public nint lParam; public uint time; public int x; public int y; }

    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private static readonly ConcurrentQueue<(Action Work, ManualResetEventSlim Done)> Queue = new();
    private static readonly object StartLock = new();
    private static Thread? _thread;
    private static volatile uint _id;

    /// <summary>Runs the action on the hook thread and waits for it. Installing or removing a hook must happen there.</summary>
    public static void Run(Action work)
    {
        Start();
        if (Thread.CurrentThread == _thread) { work(); return; }
        // not disposed: if the thread is late, it still signals this after the wait has given up
        var done = new ManualResetEventSlim(false);
        Queue.Enqueue((work, done));
        if (!PostThreadMessage(_id, WM_RUN, 0, 0))
        {
            Log.Write("Hotkey thread couldn't be reached (error " + Marshal.GetLastWin32Error() + ").");
            return;
        }
        if (!done.Wait(5000))
        {
            Log.Write("Hotkey thread is slow to answer. Still waiting.");
            done.Wait();
        }
        done.Dispose();
    }

    private static void Start()
    {
        if (_thread is not null) return;
        lock (StartLock)
        {
            if (_thread is not null) return;
            using var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "hotkey-hooks", Priority = ThreadPriority.Highest };
            thread.Start();
            ready.Wait();
            _thread = thread;
        }
    }

    private static void Loop(ManualResetEventSlim ready)
    {
        PeekMessage(out _, 0, 0, 0, 0);          // makes this thread's message queue before anyone posts to it
        _id = GetCurrentThreadId();
        Log.Debug("HOTKEY", $"hotkey thread started (tid {_id})");
        ready.Set();
        // the hooks are called from inside GetMessage
        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message != WM_RUN) continue;
            while (Queue.TryDequeue(out var item))
            {
                try { item.Work(); }
                catch (Exception ex) { Log.Write("Hotkey thread: " + ex.Message); }
                finally { try { item.Done.Set(); } catch (ObjectDisposedException) { } }
            }
        }
    }
}
