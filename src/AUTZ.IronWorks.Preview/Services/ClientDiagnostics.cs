// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Detailed log only: what each EVE client was started with.
/// Argument names are written as they are. Values only as a short code and length, never the value itself.
/// </summary>
internal static class ClientDiagnostics
{
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;
    private const int MaxArguments = 40;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public nint ExitStatus;
        public nint PebBaseAddress;
        public nint AffinityMask;
        public nint BasePriority;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(nint process, nint address, byte[] buffer, nint size, out nint read);
    [DllImport("kernel32.dll")] private static extern bool IsWow64Process(nint process, out bool wow64);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(nint process, int infoClass, ref PROCESS_BASIC_INFORMATION info, int size, out int returned);

    /// <summary>A private code for the EVE account behind a client. Same for every character on that account. The real value is never kept.</summary>
    public static string AccountSignature(uint pid)
    {
        nint handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
        if (handle == 0) return "";
        try
        {
            string? commandLine = ReadCommandLine(handle, out _, out _);
            if (commandLine is null) return "";
            foreach (string arg in SplitArguments(commandLine))
            {
                int cut = arg.IndexOfAny(new[] { '=', ':' });
                if (cut <= 0) continue;
                string name = arg[..cut].TrimStart('-', '/');
                if (!name.Equals("LauncherData", StringComparison.OrdinalIgnoreCase)) continue;
                string value = arg[(cut + 1)..];
                return value.Length == 0 ? "" : Log.ArgCode(value);
            }
        }
        catch { }
        finally { CloseHandle(handle); }
        return "";
    }

    /// <summary>One CLIENTINFO line in the detailed log for this client. Does nothing while detailed logging is off.</summary>
    public static void LogClient(uint pid, nint hwnd, string why)
    {
        if (!Log.Verbose) return;
        string text;
        try
        {
            text = Describe(pid, hwnd, why);
        }
        catch (Exception ex)
        {
            text = $"window 0x{hwnd:X} pid {pid} ({why}): couldn't be read ({ex.Message})";
        }
        Log.Debug("CLIENTINFO", text);
    }

    private static string Describe(uint pid, nint hwnd, string why)
    {
        var sb = new StringBuilder();
        sb.Append($"window 0x{hwnd:X} pid {pid} ({why})");

        try
        {
            using var process = Process.GetProcessById((int)pid);
            sb.Append($"; started {process.StartTime:HH:mm:ss.fff}");
        }
        catch { }

        string? error = null;
        string? commandLine = null;
        uint parent = 0;
        nint handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
        if (handle == 0)
        {
            error = $"couldn't open the client (error {Marshal.GetLastWin32Error()}; if this is 5, start this app as administrator)";
        }
        else
        {
            try { commandLine = ReadCommandLine(handle, out parent, out error); }
            finally { CloseHandle(handle); }
        }

        if (parent != 0)
        {
            string parentName = "gone";
            try
            {
                using var p = Process.GetProcessById((int)parent);
                parentName = p.ProcessName;
            }
            catch { }
            sb.Append($"; started by {parentName} (pid {parent})");
        }

        if (commandLine is null)
        {
            sb.Append($"; arguments: not available ({error ?? "unknown"})");
            return sb.ToString();
        }

        var args = SplitArguments(commandLine);
        sb.Append($"; program '{Path.GetFileName(args.Count > 0 ? args[0] : "")}', {Math.Max(0, args.Count - 1)} argument(s)");
        for (int i = 1; i < args.Count && i <= MaxArguments; i++) sb.Append("; ").Append(DescribeArgument(args[i]));
        if (args.Count - 1 > MaxArguments) sb.Append($"; ... {args.Count - 1 - MaxArguments} more");
        return sb.ToString();
    }

    /// <summary>"--name=value" / "/name:value" / "-name value-less" / a bare value: the name is kept, a value becomes a code.</summary>
    private static string DescribeArgument(string arg)
    {
        string name = "";
        string value = arg;
        if (arg.Length > 1 && (arg[0] == '-' || arg[0] == '/'))
        {
            int cut = arg.IndexOfAny(new[] { '=', ':' });
            string candidate = cut < 0 ? arg : arg[..cut];
            if (candidate.Length <= 40 && candidate.IndexOfAny(new[] { '\\', '/', ' ', '"' }, 1) < 0)
            {
                name = candidate;
                value = cut < 0 ? "" : arg[(cut + 1)..];
            }
        }
        if (name.Length == 0) return $"(value)=<{Log.ArgCode(value)}, {value.Length} chars>";
        return value.Length == 0 ? name : $"{name}=<{Log.ArgCode(value)}, {value.Length} chars>";
    }

    /// <summary>Splits a Windows command line the way programs do: spaces separate, double quotes group.</summary>
    private static List<string> SplitArguments(string text)
    {
        var list = new List<string>();
        var current = new StringBuilder();
        bool quoted = false, any = false;
        foreach (char c in text)
        {
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any) { list.Add(current.ToString()); current.Clear(); any = false; }
                continue;
            }
            current.Append(c);
            any = true;
        }
        if (any) list.Add(current.ToString());
        return list;
    }

    private static string? ReadCommandLine(nint process, out uint parentPid, out string? error)
    {
        parentPid = 0;
        error = null;
        if (!Environment.Is64BitProcess) { error = "this app isn't 64-bit"; return null; }
        if (IsWow64Process(process, out bool wow) && wow) { error = "the client is a 32-bit process"; return null; }

        var info = new PROCESS_BASIC_INFORMATION();
        int status = NtQueryInformationProcess(process, 0, ref info, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _);
        if (status != 0 || info.PebBaseAddress == 0) { error = $"Windows refused the query (status 0x{status:X})"; return null; }
        parentPid = (uint)info.InheritedFromUniqueProcessId;

        // PEB (64-bit): ProcessParameters at +0x20. RTL_USER_PROCESS_PARAMETERS: CommandLine (UNICODE_STRING) at +0x70: Length (ushort), buffer pointer at +0x78
        nint parameters = ReadPointer(process, info.PebBaseAddress + 0x20);
        if (parameters == 0) { error = "couldn't read the client's start-up data"; return null; }
        var header = ReadBytes(process, parameters + 0x70, 16);
        if (header is null) { error = "couldn't read the client's command line"; return null; }
        int length = BitConverter.ToUInt16(header, 0);
        nint buffer = (nint)BitConverter.ToInt64(header, 8);
        if (length <= 0 || buffer == 0) { error = "the client has no command line"; return null; }
        var raw = ReadBytes(process, buffer, Math.Min(length, 32768));
        if (raw is null) { error = "couldn't read the client's command line"; return null; }
        return Encoding.Unicode.GetString(raw);
    }

    private static nint ReadPointer(nint process, nint address)
    {
        var bytes = ReadBytes(process, address, 8);
        return bytes is null ? 0 : (nint)BitConverter.ToInt64(bytes, 0);
    }

    private static byte[]? ReadBytes(nint process, nint address, int count)
    {
        var buffer = new byte[count];
        return ReadProcessMemory(process, address, buffer, count, out nint read) && read == count ? buffer : null;
    }
}


