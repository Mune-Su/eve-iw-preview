using System.IO;
using System.Media;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>Sound and spoken alerts. (The border flash and the Windows notification are handled by the manager and tray icon.)</summary>
internal sealed class NotificationService
{
    private SoundPlayer? _player;
    private object? _voice;

    public static readonly string[] SoundNames = { "Exclamation", "Asterisk", "Beep", "Hand", "Question", "Custom" };

    public void PlaySound(string name, string file)
    {
        try
        {
            if (name.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                {
                    SystemSounds.Exclamation.Play();
                    return;
                }
                _player?.Dispose();
                _player = new SoundPlayer(file);
                _player.Play();
                return;
            }

            switch (name)
            {
                case "Asterisk": SystemSounds.Asterisk.Play(); break;
                case "Beep": SystemSounds.Beep.Play(); break;
                case "Hand": SystemSounds.Hand.Play(); break;
                case "Question": SystemSounds.Question.Play(); break;
                default: SystemSounds.Exclamation.Play(); break;
            }
        }
        catch (Exception ex)
        {
            Log.Write("Sound failed: " + ex.Message);
        }
    }

    /// <summary>Text-to-speech through Windows' built-in SAPI voice.</summary>
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
            // 1 = speak asynchronously, 2 = cancel anything still being spoken
            ((dynamic)_voice!).Speak(text, 3);
        }
        catch (Exception ex)
        {
            Log.Write("Speech failed: " + ex.Message);
        }
    }
}



