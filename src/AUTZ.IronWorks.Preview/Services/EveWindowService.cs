using System.Text;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Services;

public sealed class EveWindowService
{
    public IReadOnlyList<EveClientWindow> FindClients()
    {
        var found = new List<EveClientWindow>();
        NativeMethods.EnumWindows((h, _) =>
        {
            if (!NativeMethods.IsWindowVisible(h)) return true;
            int len = NativeMethods.GetWindowTextLength(h);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1);
            NativeMethods.GetWindowText(h, sb, sb.Capacity);
            string title = sb.ToString().Trim();
            if (!IsEveTitle(title)) return true;
            found.Add(new EveClientWindow(h, title, ParseCharacter(title)));
            return true;
        }, 0);
        return found.OrderBy(x => x.CharacterName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsEveTitle(string title) =>
        title.Equals("EVE", StringComparison.OrdinalIgnoreCase) ||
        title.StartsWith("EVE - ", StringComparison.OrdinalIgnoreCase);

    private static string ParseCharacter(string title) =>
        title.StartsWith("EVE - ", StringComparison.OrdinalIgnoreCase) ? title[6..].Trim() : title;

    /// <summary>Brings an EVE client to the front. Attaches to the current foreground thread so Windows allows it.</summary>
    public void Activate(nint handle)
    {
        if (NativeMethods.IsIconic(handle)) NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);

        uint foregroundThread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        uint thisThread = NativeMethods.GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != thisThread &&
                        NativeMethods.AttachThreadInput(thisThread, foregroundThread, true);
        NativeMethods.SetForegroundWindow(handle);
        if (attached) NativeMethods.AttachThreadInput(thisThread, foregroundThread, false);
    }
}
