// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
using System.Runtime.InteropServices;

namespace AUTZ.IronWorks.Preview.Core;

/// <summary>Gives the app's unused memory back to Windows (after start-up and after Configuration closes).</summary>
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
