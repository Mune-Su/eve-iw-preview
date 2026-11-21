using AUTZ.IronWorks.Preview.Models;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// Puts the EVE clients in the order the hotkeys, the numbered positions and the client hotkeys use.
/// Automatic: the order the clients were opened. Saved / Manual: the order of the character list in the profile.
/// </summary>
internal static class ClientOrdering
{
    /// <summary>
    /// Adds a character that isn't in the saved order yet. It goes between the clients that were opened around it
    /// (last if it was opened after all of them), so a list that builds up as clients log in ends up in the order the
    /// clients were opened, whichever logs in first.
    /// </summary>
    public static void AddInPlace(List<string> order, string name, IReadOnlyList<EveClientWindow> openOrder)
    {
        static int IndexOf(List<string> list, string n)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Equals(n, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        if (IndexOf(order, name) >= 0) return;

        int self = -1;
        for (int i = 0; i < openOrder.Count; i++)
        {
            if (!openOrder[i].IsLoginScreen && openOrder[i].CharacterName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                self = i;
                break;
            }
        }

        int at = order.Count;
        if (self >= 0)
        {
            int prev = -1;                 // just after the nearest earlier client that is already in the list
            for (int j = self - 1; j >= 0 && prev < 0; j--)
            {
                if (openOrder[j].IsLoginScreen) continue;
                int k = IndexOf(order, openOrder[j].CharacterName);
                if (k >= 0) prev = k + 1;
            }
            int next = -1;                 // just before the nearest later client that is already in the list
            for (int j = self + 1; j < openOrder.Count && next < 0; j++)
            {
                if (openOrder[j].IsLoginScreen) continue;
                int k = IndexOf(order, openOrder[j].CharacterName);
                if (k >= 0) next = k;
            }

            if (next < 0) at = order.Count;          // opened after every client that is in the list: it goes last
            else if (prev >= 0) at = prev;
            else at = next;
        }
        order.Insert(at, name);
    }

    /// <param name="openOrder">Every client, oldest first.</param>
    /// <param name="loginAssign">For each client still on the login screen: the saved character whose place it takes.</param>
    /// <param name="slots">
    /// The numbered place of each client (1 = first). In Saved / Manual order a place belongs to its character, so a client
    /// that is closed leaves its place empty instead of the clients after it moving up. Empty for Automatic: the place is
    /// then just the client's position in the list.
    /// </param>
    public static List<EveClientWindow> Apply(IReadOnlyList<EveClientWindow> openOrder, AppSettings s,
        out Dictionary<nint, string> loginAssign, out Dictionary<nint, int> slots)
    {
        // built in normal locals: an out parameter can't be used inside the local function below
        var assign = new Dictionary<nint, string>();
        loginAssign = assign;
        var slotMap = new Dictionary<nint, int>();
        slots = slotMap;
        var plain = openOrder.ToList();
        if (s.ClientOrderMode == AppSettings.OrderAutomatic) return plain;
        bool hasSavedAccounts = s.ClientOrderMode == AppSettings.OrderSaved && s.SavedAccountSlots.Count > 0;
        if (s.ClientOrder.Count == 0 && !hasSavedAccounts) return plain;

        var order = s.ClientOrder;
        int IndexOfName(string name)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        // saved characters that aren't logged in right now leave their place free
        var loggedIn = new HashSet<string>(
            openOrder.Where(c => !c.IsLoginScreen).Select(c => c.CharacterName), StringComparer.OrdinalIgnoreCase);
        var occupiedSavedSlots = hasSavedAccounts
            ? new HashSet<int>(openOrder
                .Where(c => c.AccountSignature.Length > 0 && s.SavedAccountSlots.ContainsKey(c.AccountSignature))
                .Select(c => s.SavedAccountSlots[c.AccountSignature]))
            : new HashSet<int>();
        var vacant = new Queue<string>(order.Where((n, i) => !loggedIn.Contains(n) && !occupiedSavedSlots.Contains(i + 1)));

        // a client on the login screen has no name yet: it takes the first free place, so it is already where the
        // character will be when they log in
        foreach (var client in openOrder.Where(c => c.IsLoginScreen))
        {
            if (vacant.Count == 0) break;
            assign[client.Handle] = vacant.Dequeue();
        }

        int Key(EveClientWindow client, int openIndex)
        {
            int slot = -1;
            // Saved order: the launcher signature identifies the account while it is still on the login screen.
            // It also keeps the same slot if another character on that account is selected.
            if (hasSavedAccounts && client.AccountSignature.Length > 0 &&
                s.SavedAccountSlots.TryGetValue(client.AccountSignature, out int savedSlot) && savedSlot > 0)
                slot = savedSlot - 1;
            else if (!client.IsLoginScreen) slot = IndexOfName(client.CharacterName);
            else if (assign.TryGetValue(client.Handle, out var name)) slot = IndexOfName(name);
            int reserved = Math.Max(order.Count, s.SavedAccountSlots.Values.DefaultIfEmpty(0).Max());
            return slot >= 0 ? slot : reserved + openIndex;       // not saved: after the reserved places, in opening order
        }

        var keyed = openOrder.Select((c, i) => (Client: c, Key: Key(c, i), Open: i));
        var sorted = keyed.OrderBy(k => k.Key).ThenBy(k => k.Open).ToList();

        // a client in the list keeps its place number whether or not the clients before it are open;
        // clients that aren't in the list come after the list's places, in opening order
        int extra = 0;
        foreach (var k in sorted)
        {
            int reserved = Math.Max(order.Count, s.SavedAccountSlots.Values.DefaultIfEmpty(0).Max());
            slotMap[k.Client.Handle] = k.Key < reserved ? k.Key + 1 : reserved + (++extra);
        }
        return sorted.Select(k => k.Client).ToList();
    }
}

