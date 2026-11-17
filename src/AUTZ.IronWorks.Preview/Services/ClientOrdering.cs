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
    public static List<EveClientWindow> Apply(IReadOnlyList<EveClientWindow> openOrder, AppSettings s, out Dictionary<nint, string> loginAssign)
    {
        // built in a normal local: an out parameter can't be used inside the local function below
        var assign = new Dictionary<nint, string>();
        loginAssign = assign;
        var plain = openOrder.ToList();
        if (s.ClientOrderMode == AppSettings.OrderAutomatic || s.ClientOrder.Count == 0) return plain;

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
        var vacant = new Queue<string>(order.Where(n => !loggedIn.Contains(n)));

        // a client on the login screen has no name yet: it takes the first free place, so it is already where the
        // character will be when they log in
        foreach (var client in openOrder.Where(c => c.IsLoginScreen))
        {
            if (vacant.Count == 0) break;
            assign[client.Handle] = vacant.Dequeue();
        }

        long Key(EveClientWindow client, int openIndex)
        {
            int slot = -1;
            if (!client.IsLoginScreen) slot = IndexOfName(client.CharacterName);
            else if (assign.TryGetValue(client.Handle, out var name)) slot = IndexOfName(name);
            return slot >= 0 ? slot : order.Count + openIndex;       // not in the list: after the ones that are, in opening order
        }

        var keyed = openOrder.Select((c, i) => (Client: c, Key: Key(c, i), Open: i));
        return keyed.OrderBy(k => k.Key).ThenBy(k => k.Open).Select(k => k.Client).ToList();
    }
}
