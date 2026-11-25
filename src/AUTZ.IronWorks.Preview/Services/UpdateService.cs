// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Views;
using Microsoft.Win32;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Silent updates.
/// At every start, before anything shows, the app checks GitHub's Latest release for up to a few seconds.
/// A newer Setup is downloaded (the crucible window shows the progress), checked, installed silently, and the new version opens.
/// While the app keeps running it checks again every 6 hours and leaves the download for the next start.
/// </summary>
internal static class UpdateService
{
    private const string LatestApi = "https://api.github.com/repos/Mune-Su/eve-iw-preview/releases/latest";
    private const string SetupName = "EVE-IW-Preview-Setup.exe";
    private const string Prefix = "EVE-IW-Preview-Setup-";
    private const string InstallerKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B6D8F1E4-2A7C-4C0E-9E55-6C4F7A1D2B93}_is1";

    /// <summary>What the start-up check waits for GitHub before it gives up and opens the app normally.</summary>
    private static readonly TimeSpan StartupWait = TimeSpan.FromSeconds(6);

    /// <summary>A newer release: its version, the Setup's address and the Setup's expected checksum.</summary>
    private sealed record Release(Version Version, string SetupUrl, string Sha256);

    public static string Folder => Path.Combine(AppInfo.DataFolder, "updates");

    private static Timer? _timer;
    private static int _checking;

    // ---------- start-up ----------

    /// <summary>
    /// Called at every start, before anything shows. Checks GitHub; if a newer version is there it downloads it (with the
    /// progress window), installs it silently and reopens the app. True = close now. False = carry on starting.
    /// </summary>
    public static bool UpdateAtStartup(bool enabled)
    {
        try
        {
            if (!enabled) return false;
            var current = Parse(AppInfo.Version);
            string? exe = Environment.ProcessPath;
            if (current is null || string.IsNullOrEmpty(exe) || !IsInstalledCopy(exe)) return false;
            if (OtherCopyRunning()) return false;

            // the quick check. Any problem (no internet, GitHub slow) just means the app opens as normal.
            using var cts = new CancellationTokenSource(StartupWait);
            var look = Task.Run(() => FindNewerAsync(current, cts.Token));
            if (!look.Wait(StartupWait + TimeSpan.FromSeconds(1))) { Log.Write("Update check: GitHub didn't answer in time."); return false; }
            var release = look.Result;
            if (release is null) return false;

            // one try per version: if the last try left the old version running, don't loop
            if (File.Exists(Path.Combine(Folder, "tried-" + release.Version)))
            {
                Log.Write($"Update to v{release.Version} didn't work last time, so it isn't tried again.");
                return false;
            }

            // already downloaded by the background check: install it now
            if (PendingSetups().Any(p => p.Version >= release.Version)) return InstallPendingUpdate(true);

            bool downloaded = false;
            var window = new UpdateWindow(release.Version.ToString());
            window.Loaded += async (_, _) =>
            {
                try
                {
                    using var downloadCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    downloaded = await DownloadAsync(release, new Progress<double>(window.SetProgress), downloadCts.Token);
                    if (downloaded)
                    {
                        window.SetInstalling();
                        await Task.Delay(900);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Update download failed: " + ex.Message);
                }
                window.Finish();
            };
            window.ShowDialog();

            return downloaded && InstallPendingUpdate(true);
        }
        catch (Exception ex)
        {
            Log.Write("Update at start-up failed: " + ex.Message);
            return false;
        }
    }

    // ---------- install a Setup that is waiting ----------

    /// <summary>
    /// If a newer Setup is waiting, starts a hidden helper that waits for this app to close, installs the Setup silently
    /// and opens the new version. Returns true when the app should close now.
    /// </summary>
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
            if (string.IsNullOrEmpty(exe) || !IsInstalledCopy(exe))
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

    // ---------- while the app runs ----------

    /// <summary>Checks again while the app stays open: after 10 minutes, then every 6 hours. A newer Setup is downloaded for the next start.</summary>
    public static void StartChecks(Func<bool> enabled)
    {
        _timer?.Dispose();
        _timer = new Timer(_ => _ = CheckAsync(enabled), null, TimeSpan.FromMinutes(10), TimeSpan.FromHours(6));
    }

    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private static async Task CheckAsync(Func<bool> enabled)
    {
        if (!enabled() || Interlocked.Exchange(ref _checking, 1) == 1) return;
        try
        {
            var current = Parse(AppInfo.Version);
            if (current is null) return;

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var release = await FindNewerAsync(current, cts.Token);
            if (release is null) { Log.Debug("UPDATE", $"up to date (v{current})"); return; }
            if (PendingSetups().Any(p => p.Version >= release.Version)) { Log.Debug("UPDATE", $"v{release.Version} already downloaded"); return; }

            if (await DownloadAsync(release, null, cts.Token))
                Log.Write($"Update v{release.Version} downloaded. It installs the next time the app starts.");
        }
        catch (Exception ex)
        {
            Log.Debug("UPDATE", "check failed: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    // ---------- GitHub ----------

    /// <summary>The Latest release if it is newer than this version and has a Setup with a checksum. Null otherwise (never throws).</summary>
    private static async Task<Release?> FindNewerAsync(Version current, CancellationToken token)
    {
        try
        {
            using var http = NewClient(TimeSpan.FromSeconds(5));
            using var doc = JsonDocument.Parse(await http.GetStringAsync(LatestApi, token));
            var root = doc.RootElement;
            var latest = Parse((root.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V'));
            if (latest is null || latest <= current) return null;

            string? setupUrl = null, sumsUrl = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (name.Equals(SetupName, StringComparison.OrdinalIgnoreCase)) setupUrl = url;
                else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sumsUrl = url;
            }
            if (setupUrl is null || sumsUrl is null)
            {
                Log.Debug("UPDATE", $"v{latest} has no Setup or checksum file");
                return null;
            }

            string sums = await http.GetStringAsync(sumsUrl, token);
            string? expected = sums.Split('\n')
                .Select(l => l.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length >= 2 && p[^1].TrimStart('*').Equals(SetupName, StringComparison.OrdinalIgnoreCase))
                .Select(p => p[0]).FirstOrDefault();
            if (expected is null)
            {
                Log.Write($"Update v{latest}: the checksum file doesn't list the Setup. Skipped.");
                return null;
            }
            return new Release(latest, setupUrl, expected);
        }
        catch (Exception ex)
        {
            Log.Debug("UPDATE", "couldn't check GitHub: " + ex.Message);
            return null;
        }
    }

    /// <summary>Downloads a release's Setup, checks it against its checksum and keeps it in the updates folder. Progress is 0 to 1.</summary>
    private static async Task<bool> DownloadAsync(Release release, IProgress<double>? progress, CancellationToken token)
    {
        string temp = Path.Combine(Folder, "download.tmp");
        try
        {
            Directory.CreateDirectory(Folder);
            using var http = NewClient(TimeSpan.FromMinutes(2));
            using var response = await http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0;

            await using (var stream = await response.Content.ReadAsStreamAsync(token))
            await using (var file = File.Create(temp))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    done += read;
                    if (total > 0) progress?.Report(Math.Min(1.0, done / (double)total));
                }
            }

            string actual;
            await using (var file = File.OpenRead(temp)) actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
            if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write($"Update v{release.Version}: the download didn't match its checksum. Skipped.");
                return false;
            }

            File.Move(temp, Path.Combine(Folder, Prefix + release.Version + ".exe"), overwrite: true);
            foreach (var old in PendingSetups().Where(p => p.Version < release.Version)) Remove(old.Path, old.Version);
            progress?.Report(1.0);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"Update v{release.Version}: the download failed ({ex.Message}).");
            return false;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static HttpClient NewClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("EVE-IW-Preview/" + AppInfo.Version);
        return http;
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

    /// <summary>"0.19.22" (a "-rc.1" ending is ignored). Null if it isn't a version.</summary>
    private static Version? Parse(string text)
    {
        int dash = text.IndexOf('-');
        if (dash >= 0) text = text[..dash];
        return Version.TryParse(text.Trim(), out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }

    /// <summary>
    /// True only for the copy the Setup installed. A copy run from anywhere else (the repo's build folders, Downloads) is never
    /// replaced: the update would install somewhere else and reopen the old copy, over and over.
    /// </summary>
    private static bool IsInstalledCopy(string exe)
    {
        try
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = hive.OpenSubKey(InstallerKey);
                string? location = key?.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(location)) continue;
                string folder = Path.GetFullPath(location).TrimEnd('\\');
                string mine = Path.GetDirectoryName(Path.GetFullPath(exe))?.TrimEnd('\\') ?? "";
                if (string.Equals(folder, mine, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch
        {
            // can't tell: treat it as not installed
        }
        return false;
    }

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


