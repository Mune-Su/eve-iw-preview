// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Times how long a minimised EVE client takes to come back after a switch: restored, in front, and answering again.
/// Results go to the detailed log (RESTORE lines), with a running average.
/// Also holds the cycle keys until that client is back (see PreviewManager.Cycle).
/// </summary>
internal sealed class RestoreWatch
{
    /// <summary>After a switch to a minimised client the cycle keys always wait at least this long. Not a setting.</summary>
    public const int HardMinMs = 150;

    /// <summary>...and never longer than this, even if the client hasn't answered yet.</summary>
    public const int MaxWaitMs = 1000;

    private const int GiveUpMs = 3000;           // the timing itself stops here
    private const int SamplesKept = 20;

    private readonly object _lock = new();
    private readonly Queue<long> _samples = new();     // recent "answering" times, all clients
    private nint _target;
    private long _start;
    private long _frontAt;
    private long _readyAt;
    private int _generation;

    /// <summary>A switch to a minimised client was just accepted: start timing it, and start the cycle wait.</summary>
    public void Begin(nint handle)
    {
        int generation;
        long start = Log.Ms;
        lock (_lock)
        {
            generation = ++_generation;
            _target = handle;
            _start = start;
            _frontAt = 0;
            _readyAt = 0;
        }
        var thread = new Thread(() => Measure(handle, start, generation))
        {
            IsBackground = true,
            Name = "restore-watch",
            Priority = ThreadPriority.AboveNormal
        };
        thread.Start();
    }

    /// <summary>Windows reported a new window in front (called from the focus watcher).</summary>
    public void NoteForeground(nint hwnd)
    {
        lock (_lock)
        {
            if (hwnd == _target && _frontAt == 0) _frontAt = Log.Ms;
        }
    }

    /// <summary>How long the cycle keys must still wait, in ms. 0 = go.</summary>
    public long CycleWaitMs()
    {
        lock (_lock)
        {
            if (_target == 0) return 0;
            long since = Log.Ms - _start;
            if (since >= MaxWaitMs) return 0;
            if (since < HardMinMs) return HardMinMs - since;
            return _readyAt > 0 ? 0 : MaxWaitMs - since;
        }
    }

    /// <summary>
    /// Background thread: waits for the window to stop being minimised, then sends it an empty message.
    /// The reply comes once the client has handled the restore and is taking input again.
    /// </summary>
    private void Measure(nint handle, long start, int generation)
    {
        long restoredAt = 0, readyAt = 0;
        try
        {
            while (Log.Ms - start < GiveUpMs)
            {
                if (!NativeMethods.IsWindow(handle)) break;
                if (!NativeMethods.IsIconic(handle)) { restoredAt = Log.Ms; break; }
                Thread.Sleep(1);
            }
            if (restoredAt > 0)
            {
                uint left = (uint)Math.Max(1, GiveUpMs - (Log.Ms - start));
                if (NativeMethods.SendMessageTimeout(handle, NativeMethods.WM_NULL, 0, 0, NativeMethods.SMTO_ABORTIFHUNG, left, out _) != 0)
                    readyAt = Log.Ms;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("RESTORE", "timing failed: " + ex.Message);
        }

        long frontAt;
        double average;
        int count;
        lock (_lock)
        {
            if (generation == _generation) _readyAt = readyAt > 0 ? readyAt : Log.Ms;     // gave up: let the cycle keys go
            frontAt = handle == _target ? _frontAt : 0;
            if (readyAt > 0)
            {
                _samples.Enqueue(readyAt - start);
                while (_samples.Count > SamplesKept) _samples.Dequeue();
            }
            count = _samples.Count;
            average = count == 0 ? 0 : _samples.Average();
        }

        string Part(long at) => at > 0 ? (at - start) + " ms" : "not seen";
        Log.Debug("RESTORE", $"{Log.Win(handle)} back from minimised: restored {Part(restoredAt)}, in front {Part(frontAt)}, answering {Part(readyAt)}" +
                             (count > 0 ? $" (average answering {average:0} ms over {count})" : ""));
    }
}

