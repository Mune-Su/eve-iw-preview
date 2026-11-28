// EVE-IW-Preview: owner-only, on-demand Windows Performance Recorder capture.
// The ETL contains system-wide kernel events and is private: never upload it without review.
using System.Diagnostics;
using System.IO;
using System.Text;
using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Services;

internal static class OwnerMemoryTrace
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static bool Running { get; private set; }
    public static string LastFolder { get; private set; } = "";
    private static string? _folder;
    private static CancellationTokenSource? _cancel;
    private static Task? _capture;

    private static async Task<(bool Ok, string Text)> Wpr(string args)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("wpr.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true
        }};
        try
        {
            if (!process.Start()) return (false, "Windows Performance Recorder did not start.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return (false, "Windows Performance Recorder timed out.");
            }
            string output = (await process.StandardOutput.ReadToEndAsync() + " " +
                             await process.StandardError.ReadToEndAsync()).Trim();
            return (process.ExitCode == 0, output.Length > 300 ? output[..300] : output);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static async Task<string> StartAsync()
    {
        if (!AdminAccess.IsOwner) return "Owner access is required.";
        if (!MemoryCleaner.IsAdministrator) return "Restart as administrator first.";
        await Gate.WaitAsync();
        try
        {
            if (Running) return "A memory trace is already running.";
            string folder = Path.Combine(AppInfo.DataFolder, "memory-traces",
                DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(folder);
            // WPR's GeneralProfile is a Microsoft-defined ETW profile. It records system-wide
            // kernel activity; precise event coverage varies with the installed WPR version.
            var started = await Wpr("-start GeneralProfile -filemode");
            if (!started.Ok) return "ETW could not start: " + started.Text;
            _folder = folder;
            LastFolder = folder;
            _cancel = new CancellationTokenSource();
            Running = true;
            File.WriteAllText(Path.Combine(folder, "readme.txt"),
                "Owner-only ETW capture. The ETL may contain private system paths and process information. " +
                "Open with Windows Performance Analyzer. Do not share without reviewing it.\r\n" +
                "Captured with WPR GeneralProfile; exact kernel events depend on Windows/WPR.\r\n");
            _capture = Task.Run(() => SampleAsync(folder, _cancel.Token));
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(60));
                if (Running) await StopAsync();
            });
            return "Windows trace started for 60 seconds. Switch between EVE clients now.";
        }
        finally { Gate.Release(); }
    }

    private static async Task SampleAsync(string folder, CancellationToken token)
    {
        string file = Path.Combine(folder, "eve-memory.csv");
        try
        {
            await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteLineAsync("utc,pid,working_set_bytes,private_bytes,read_bytes,read_operations,available_ram_bytes");
            while (!token.IsCancellationRequested)
            {
                var ram = MemoryCleaner.Read();
                foreach (var p in Process.GetProcessesByName("exefile"))
                {
                    using (p)
                    {
                        try
                        {
                            // Process I/O counters are not exposed reliably by all .NET versions.
                            // ETL is the authoritative source for per-process disk reads and hard faults.
                            await writer.WriteLineAsync($"{DateTime.UtcNow:O},{p.Id},{p.WorkingSet64},{p.PrivateMemorySize64},,,{ram.AvailPhys}");
                        }
                        catch { } // Process may have exited.
                    }
                }
                await writer.FlushAsync();
                try { await Task.Delay(1000, token); } catch (OperationCanceledException) { break; }
            }
        }
        catch (Exception ex) { Log.Write("Memory trace sampling: " + ex.Message); }
    }

    public static async Task<string> StopAsync()
    {
        if (!AdminAccess.IsOwner) return "Owner access is required.";
        await Gate.WaitAsync();
        try
        {
            if (!Running || _folder is null) return "No memory trace is running.";
            Running = false;
            _cancel?.Cancel();
            if (_capture is not null) await _capture;
            string etl = Path.Combine(_folder, "windows-memory.etl");
            var stopped = await Wpr("-stop \"" + etl + "\"");
            _cancel?.Dispose(); _cancel = null; _capture = null; _folder = null;
            return stopped.Ok ? "Trace saved locally. Open the trace folder to view it." :
                "ETW stop failed: " + stopped.Text + ". Try 'wpr -cancel' in an elevated terminal.";
        }
        finally { Gate.Release(); }
    }
}
