// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
using System.IO;
using System.Media;
using System.Windows.Media;
using System.Windows.Threading;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>Alert sounds and spoken alerts. (The border flash and the Windows notification are done by the manager and tray icon.)</summary>
internal sealed class NotificationService
{
    private readonly List<MediaPlayer> _playing = new();
    private SoundPlayer? _fallback;
    private object? _voice;

    /// <summary>Plays a sound at 0–100% volume, for up to 10 s. If it can't play, the default sound plays and the reason is logged.</summary>
    public void Play(string id, int volume, string legacyFile = "")
    {
        if (id == SoundLibrary.NoneId) return;

        string? path;
        string what;
        if (id.Equals("Custom", StringComparison.OrdinalIgnoreCase) && File.Exists(legacyFile))
        {
            path = legacyFile;
            what = "old custom sound";
        }
        else
        {
            id = SoundLibrary.Normalise(id);
            path = SoundLibrary.PathOf(id);
            what = id.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) ? "custom sound" : id;
        }

        if (path is null)
        {
            Log.Write($"Alert sound not found ({what}). Played the default sound.");
            PlayDefault();
            return;
        }

        try
        {
            while (_playing.Count >= 6) Stop(_playing[0]);        // never pile up players

            var player = new MediaPlayer { Volume = Math.Clamp(volume, 0, 100) / 100.0 };
            var limit = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SoundLibrary.MaxSeconds) };
            bool done = false;
            void Finish()
            {
                if (done) return;
                done = true;
                limit.Stop();
                Stop(player);
            }
            player.MediaEnded += (_, _) => Finish();
            player.MediaFailed += (_, e) =>
            {
                Finish();
                Log.Write($"Alert sound couldn't play ({what}: {e.ErrorException?.Message}). Played the default sound.");
                PlayDefault();
            };
            limit.Tick += (_, _) => Finish();
            _playing.Add(player);
            player.Open(new Uri(path));
            player.Play();
            limit.Start();
            Log.Debug("ALERT", $"sound '{what}' at {Math.Clamp(volume, 0, 100)}%");
        }
        catch (Exception ex)
        {
            Log.Write($"Alert sound couldn't play ({what}: {ex.Message}). Played the default sound.");
            PlayDefault();
        }
    }

    private void Stop(MediaPlayer player)
    {
        _playing.Remove(player);
        try
        {
            player.Stop();
            player.Close();
        }
        catch { }
    }

    /// <summary>The built-in chime, played straight from the exe (works even where the media player is missing).</summary>
    private void PlayDefault()
    {
        try
        {
            var stream = SoundLibrary.OpenBuiltIn("Chime");
            if (stream is null)
            {
                SystemSounds.Exclamation.Play();
                return;
            }
            _fallback?.Dispose();
            _fallback = new SoundPlayer(stream);
            _fallback.Play();
        }
        catch
        {
            SystemSounds.Exclamation.Play();
        }
    }

    /// <summary>Text-to-speech through Windows' built-in voice.</summary>
    public void Speak(string text)
    {
        try
        {
            if (_voice is null)
            {
                var type = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (type is null) return;
                _voice = Activator.CreateInstance(type);
            }
            // 1 = speak in the background, 2 = cancel anything still being spoken
            ((dynamic)_voice!).Speak(text, 3);
        }
        catch (Exception ex)
        {
            Log.Write("Speech failed: " + ex.Message);
        }
    }
}


