using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using AUTZ.IronWorks.Preview.Core;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Silent updates.
/// Checks GitHub's Latest release at start and every 6 hours, downloads a newer Setup and checks it.
/// update-preview.ps1 can also leave a Setup here. The next start installs it with no windows.
/// </summary>
internal static class UpdateService
{
    private const string LatestApi = "https://api.github.com/repos/Mune-Su/eve-iw-preview/releases/latest";
    private const string SetupName = "EVE-IW-Preview-Setup.exe";
    private const string Prefix = "EVE-IW-Preview-Setup-";

    public static string Folder => Path.Combine(AppInfo.DataFolder, "updates");

    private static Timer? _timer;
    private static int _checking;

    // ---------- install at start ----------

    /// <summary>Called first at start. If a newer Setup is waiting, installs it silently and reopens the app. True = close now.</summary>
    public static bool InstallPendingUpdate(bool enabled)
    {
        try
        {
            var current = Parse(AppInfo.Version);
            var pending = PendingSetups();
            if (pending.Count == 0 || current is null) return false;

            // anything not newer than this version is done with
            foreach (var old in pending.Where(p => p.Version <= current)) Remove(old.Path, old.Version);
            var newest = pending.Where(p => p.Version > current).OrderByDescending(p => p.Version).FirstOrDefault();
            if (newest.Path is null) return false;
            if (!enabled)
            {
                Log.Debug("UPDATE", $"v{newest.Version} is waiting, but automatic updates are off");
                return false;
            }

            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || IsDevBuild(exe))
            {
                Log.Debug("UPDATE", "not an installed copy: update left waiting");
                return false;
            }
            if (OtherCopyRunning())
            {
                Log.Debug("UPDATE", "another copy is running: update left for the next start");
                return false;
            }

            // one try per version: if it is still the old version next time, give up on that Setup
            string marker = Path.Combine(Folder, "tried-" + newest.Version);
            if (File.Exists(marker))
            {
                Log.Write($"Update to v{newest.Version} didn't install. It was removed; the next release will try again.");
                Remove(newest.Path, newest.Version);
                return false;
            }
            File.WriteAllText(marker, DateTime.Now.ToString("s"));

            string script =
                $"Wait-Process -Id {Environment.ProcessId} -Timeout 30 -ErrorAction SilentlyContinue; " +
                $"Start-Process -FilePath '{Quote(newest.Path)}' -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' -Wait; " +
                $"Start-Process -FilePath '{Quote(exe)}'";
            var start = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"" + script + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(start)?.Dispose();
            Log.Write($"Installing update v{newest.Version}.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("Update couldn't be installed: " + ex.Message);
            return false;
        }
    }

    // ---------- download in the background ----------

    /// <summary>Starts the background checks: now (after a short wait), then every 6 hours.</summary>
    public static void StartChecks(Func<bool> enabled)
    {
        _timer?.Dispose();
        _timer = new Timer(_ => _ = CheckAsync(enabled), null, TimeSpan.FromSeconds(30), TimeSpan.FromHours(6));
    }

    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private static async Task CheckAsync(Func<bool> enabled)
    {
        if (!enabled() || Interlocked.Exchange(ref _checking, 1) == 1) return;
        string temp = "";
        try
        {
            var current = Parse(AppInfo.Version);
            if (current is null) return;

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("EVE-IW-Preview/" + AppInfo.Version);

            using var doc = JsonDocument.Parse(await http.GetStringAsync(LatestApi));
            var root = doc.RootElement;
            var latest = Parse((root.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V'));
            if (latest is null || latest <= current) { Log.Debug("UPDATE", $"up to date (v{current})"); return; }
            if (PendingSetups().Any(p => p.Version >= latest)) { Log.Debug("UPDATE", $"v{latest} already downloaded"); return; }

            string? setupUrl = null, sumsUrl = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (name.Equals(SetupName, StringComparison.OrdinalIgnoreCase)) setupUrl = url;
                else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sumsUrl = url;
            }
            if (setupUrl is null || sumsUrl is null) { Log.Debug("UPDATE", $"v{latest} has no Setup or checksum file"); return; }

            string sums = await http.GetStringAsync(sumsUrl);
            string? expected = sums.Split('\n')
                .Select(l => l.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length >= 2 && p[^1].TrimStart('*').Equals(SetupName, StringComparison.OrdinalIgnoreCase))
                .Select(p => p[0]).FirstOrDefault();
            if (expected is null) { Log.Write($"Update v{latest}: the checksum file doesn't list the Setup. Skipped."); return; }

            Directory.CreateDirectory(Folder);
            temp = Path.Combine(Folder, "download.tmp");
            await using (var stream = await http.GetStreamAsync(setupUrl))
            await using (var file = File.Create(temp))
                await stream.CopyToAsync(file);

            string actual;
            await using (var file = File.OpenRead(temp)) actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write($"Update v{latest}: the download didn't match its checksum. Skipped.");
                return;
            }

            File.Move(temp, Path.Combine(Folder, Prefix + latest + ".exe"), overwrite: true);
            temp = "";
            foreach (var old in PendingSetups().Where(p => p.Version < latest)) Remove(old.Path, old.Version);
            Log.Write($"Update v{latest} downloaded. It installs the next time the app starts.");
        }
        catch (Exception ex)
        {
            Log.Debug("UPDATE", "check failed: " + ex.Message);
        }
        finally
        {
            if (temp.Length > 0) try { File.Delete(temp); } catch { }
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    // ---------- helpers ----------

    private static List<(string Path, Version Version)> PendingSetups()
    {
        var list = new List<(string, Version)>();
        if (!Directory.Exists(Folder)) return list;
        foreach (string file in Directory.GetFiles(Folder, Prefix + "*.exe"))
        {
            var version = Parse(Path.GetFileNameWithoutExtension(file)[Prefix.Length..]);
            if (version is not null) list.Add((file, version));
        }
        return list;
    }

    private static void Remove(string path, Version version)
    {
        try { File.Delete(path); } catch { }
        try { File.Delete(Path.Combine(Folder, "tried-" + version)); } catch { }
    }

    /// <summary>"0.19.19" (a "-rc.1" ending is ignored). Null if it isn't a version.</summary>
    private static Version? Parse(string text)
    {
        int dash = text.IndexOf('-');
        if (dash >= 0) text = text[..dash];
        return Version.TryParse(text.Trim(), out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }

    /// <summary>A copy run from the repo's build folders is never replaced by the installer.</summary>
    private static bool IsDevBuild(string exe) =>
        exe.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase) ||
        exe.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase) ||
        exe.Contains(@"\publish", StringComparison.OrdinalIgnoreCase);

    private static bool OtherCopyRunning()
    {
        string name = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "EVE-IW-Preview");
        var others = Process.GetProcessesByName(name);
        bool any = others.Any(p => p.Id != Environment.ProcessId);
        foreach (var p in others) p.Dispose();
        return any;
    }

    private static string Quote(string path) => path.Replace("'", "''");
}
