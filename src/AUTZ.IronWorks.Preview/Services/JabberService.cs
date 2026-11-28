using System.IO;
using System.Reactive.Linq;
using XmppDotNet.Transport.Socket;
using XmppDotNet.Extensions.Client.Presence;
using XmppDotNet.Xmpp;
using XmppDotNet.Xmpp.Base;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using AUTZ.IronWorks.Preview.Core;
namespace AUTZ.IronWorks.Preview.Services;
internal sealed record JabberPing(DateTime Time, string Sender, string Channel, string Body, bool Read = false);
internal sealed record ParticipationStats(int Strat, int Pct, string LastPap);
internal sealed record JabberContact(string Jid, string Name, bool Online, string Group = "Contacts");
internal sealed record JabberChatMessage(DateTime Time, string Peer, string Body, bool Outgoing = false);
internal sealed class JabberConfig
{
    public string Login { get; set; } = "";
    public string Resource { get; set; } = "";
    public bool RememberPassword { get; set; } = true;
    public bool AutoConnect { get; set; }
    public bool Notify { get; set; } = true;
    public string PasswordProtected { get; set; } = "";
}
internal sealed class JabberService : IDisposable
{
    private static readonly string Folder = Path.Combine(AppInfo.DataFolder, "jabber");
    private static readonly string ConfigFile = Path.Combine(Folder, "config.json");
    private static readonly string HistoryFile = Path.Combine(Folder, "history.json");
    private static readonly string ParticipationFile = Path.Combine(Folder, "participation.json");
    public ParticipationStats? Participation { get; private set; }
    private static readonly System.Text.RegularExpressions.Regex MonthPattern = new(
        @"(?im)^\s*(?:->\s*)?This\s+Month\s*:\s*(\d+)\s*STR\s*\+\s*(\d+)\s*PCT\s*$");
    private static readonly System.Text.RegularExpressions.Regex LastPattern = new(
        @"(?im)^\s*(?:->\s*)?Last\s+strat[- ](?:op|pap)\s*:\s*(.+?)\s*$");
    private static readonly System.Text.RegularExpressions.Regex DatePattern = new(
        @"(?i)\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\s+\d{1,2}\b");
    private DateTime _participationAt = DateTime.MinValue;
    private string? _latestAccountInfo;
    private bool ReadParticipation(string body, DateTime receivedAt)
    {
        // Only account reports are authoritative; fleet broadcasts must never change PAP totals.
        if (!body.Contains("This Month", StringComparison.OrdinalIgnoreCase) ||
            !body.Contains("STR", StringComparison.OrdinalIgnoreCase) ||
            !body.Contains("PCT", StringComparison.OrdinalIgnoreCase)) return false;
        var counts = MonthPattern.Match(body);
        if (!counts.Success || !int.TryParse(counts.Groups[1].Value, out int strat) ||
            !int.TryParse(counts.Groups[2].Value, out int pct))
        {
            Log.Debug("JABBER", "account info found but monthly participation format not recognised");
            return false;
        }
        if (receivedAt < _participationAt) return true;
        var last = LastPattern.Match(body);
        var date = last.Success ? DatePattern.Match(last.Groups[1].Value) : System.Text.RegularExpressions.Match.Empty;
        string lastText = date.Success ? date.Value : "--";
        Participation = new ParticipationStats(strat, pct, lastText);
        _participationAt = receivedAt;
        _latestAccountInfo = body;
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(ParticipationFile, JsonSerializer.Serialize(Participation));
            File.WriteAllText(Path.Combine(Folder, "latest-account-info.json"),
                JsonSerializer.Serialize(new AccountInfoCache(receivedAt, body)));
        }
        catch (Exception ex) { Log.Debug("JABBER", "participation save failed: " + ex.GetType().Name); }
        Log.Debug("JABBER", "participation report updated from received account info");
        try { Changed?.Invoke(); } catch { }
        return true;
    }
    private sealed record AccountInfoCache(DateTime ReceivedAt, string Body);
    public void RefreshParticipation()
    {
        // Refresh only locally available account information. Never send a directorbot command.
        AccountInfoCache? cached = null;
        try
        {
            string path = Path.Combine(Folder, "latest-account-info.json");
            if (File.Exists(path)) cached = JsonSerializer.Deserialize<AccountInfoCache>(File.ReadAllText(path));
        }
        catch { }
        var messages = Chats.Where(x => !x.Outgoing &&
            x.Peer.Split('@')[0].Equals("directorbot", StringComparison.OrdinalIgnoreCase) &&
            x.Body.Contains("Participation", StringComparison.OrdinalIgnoreCase))
            .Select(x => new AccountInfoCache(x.Time, x.Body));
        var latest = messages.Concat(cached is null ? Array.Empty<AccountInfoCache>() : new[] { cached })
            .OrderByDescending(x => x.ReceivedAt).FirstOrDefault();
        if (latest is not null) ReadParticipation(latest.Body, latest.ReceivedAt);
        else Log.Write("Jabber PAPs: no Account Info received or saved by IronWorks. The server archive is not available through this reader.");
        try { Changed?.Invoke(); } catch { }
    }

    private CancellationTokenSource _stop = new();
    private Task? _runner;
    private string _sessionPassword = "";
    private readonly object _gate = new();
    private List<JabberPing> _history = new();
    private readonly List<JabberChatMessage> _chats = new();
    private readonly Dictionary<string,JabberContact> _contacts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string ChatFile = Path.Combine(Folder, "chats.json");
    private static readonly string ContactsFile = Path.Combine(Folder, "contacts.json");
    private static readonly string RoomsFile = Path.Combine(Folder, "rooms.json");
    public IReadOnlyList<JabberContact> Contacts { get { lock (_gate) return _contacts.Values.OrderBy(x => x.Name).ToArray(); } }
    private void ReadRoster(string xml)
    {
        try
        {
            var document = System.Xml.Linq.XElement.Parse(xml);
            var query = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "query" && x.Name.NamespaceName == "jabber:iq:roster");
            if (query is null) { Log.Debug("JABBER", "roster response did not contain roster query"); return; }
            var received = new Dictionary<string,JabberContact>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in query.Elements().Where(x => x.Name.LocalName == "item"))
            {
                string jid = ((string?)item.Attribute("jid") ?? "").Trim();
                if (!jid.Contains('@') || (string?)item.Attribute("subscription") == "remove") continue;
                string name = ((string?)item.Attribute("name") ?? "").Trim();
                if (name.Length == 0) name = jid.Split('@')[0];
                string group = item.Elements().FirstOrDefault(x => x.Name.LocalName == "group")?.Value?.Trim() ?? "Contacts";
                if (group.Length == 0) group = "Contacts";
                received[jid] = new JabberContact(jid, name, false, group);
            }
            lock (_gate)
            {
                // The server roster is authoritative. Preserve the last known presence while updating it.
                foreach (var contact in received.Values)
                    _contacts[contact.Jid] = contact with { Online = _contacts.TryGetValue(contact.Jid, out var old) && old.Online };
            }
            SaveChatData(); ChatChanged?.Invoke();
            Log.Debug("JABBER", "roster loaded: " + received.Count + " contacts");
        }
        catch (Exception ex) { Log.Debug("JABBER", "roster parsing failed: " + ex.GetType().Name); }
    }
    private void ReadPresence(System.Xml.Linq.XElement presence)
    {
        string jid = ((string?)presence.Attribute("from") ?? "").Split('/')[0];
        if (jid.Length == 0) return;
        bool online = !string.Equals((string?)presence.Attribute("type"), "unavailable", StringComparison.OrdinalIgnoreCase);
        lock (_gate)
        {
            if (!_contacts.TryGetValue(jid, out var contact)) return;
            _contacts[jid] = contact with { Online = online };
        }
        ChatChanged?.Invoke();
    }

    public IReadOnlyList<string> Rooms { get { lock (_gate) return _rooms.OrderBy(x => x).ToArray(); } }
    private void SaveChatData()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            lock (_gate)
            {
                File.WriteAllText(ChatFile, JsonSerializer.Serialize(_chats));
                File.WriteAllText(ContactsFile, JsonSerializer.Serialize(_contacts.Values.ToArray()));
                File.WriteAllText(RoomsFile, JsonSerializer.Serialize(_rooms.ToArray()));
            }
        }
        catch (Exception ex) { Log.Debug("JABBER", "chat save error=" + ex.GetType().Name); }
    }
    public void AddContact(string jid)
    {
        jid = jid.Trim(); if (!jid.Contains('@')) throw new InvalidOperationException("Enter a full Jabber address.");
        lock (_gate) _contacts.TryAdd(jid, new JabberContact(jid, jid.Split('@')[0], false));
        SaveChatData(); ChatChanged?.Invoke();
    }
    public async Task JoinRoom(string room)
    {
        room = room.Trim(); if (!room.Contains('@')) throw new InvalidOperationException("Enter a full room address.");
        lock (_gate) _rooms.Add(room);
        SaveChatData(); ChatChanged?.Invoke();
        await JoinRoomPresence(room);
    }
    private async Task JoinRoomPresence(string room)
    {
        XmppDotNet.XmppClient? client; lock (_connectionGate) client = _client;
        if (client is null || Status != "Connected") return;
        string nickname = Config.Login.Split('@')[0];
        await client.SendAsync(new XmppDotNet.Xmpp.Client.Presence { To = room + "/" + nickname });
    }
    public void LeaveRoom(string room)
    {
        lock (_gate) _rooms.Remove(room);
        SaveChatData(); ChatChanged?.Invoke();
    }
    public async Task SendRoom(string room, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        XmppDotNet.XmppClient? client; lock (_connectionGate) client = _client;
        if (client is null || Status != "Connected") throw new InvalidOperationException("Jabber is disconnected.");
        await client.SendAsync(new XmppDotNet.Xmpp.Client.Message { To = room, Type = MessageType.GroupChat, Body = body });
        lock (_gate) { _chats.Add(new JabberChatMessage(DateTime.Now, room, body, true)); TrimChats(); }
        SaveChatData(); ChatChanged?.Invoke();
    }
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    public event Action? ChatChanged;
    public IReadOnlyList<JabberChatMessage> Chats { get { lock (_gate) return _chats.ToArray(); } }
    public async Task SendChat(string peer, string body)
    {
        peer = peer.Trim();
        if (!peer.Contains('@') || peer.Any(c => char.IsWhiteSpace(c) || c == '<' || c == '>' || c == '\'' || c == '"'))
            throw new InvalidOperationException("Enter a full Jabber address (name@domain).");
        if (string.IsNullOrWhiteSpace(body)) return;
        XmppDotNet.XmppClient? client;
        lock (_connectionGate) client = _client;
        if (client is null || Status != "Connected") throw new InvalidOperationException("Jabber is disconnected.");
        await client.SendAsync(new XmppDotNet.Xmpp.Client.Message
        {
            To = peer,
            Type = MessageType.Chat,
            Body = body
        });
        lock (_gate) { _chats.Add(new JabberChatMessage(DateTime.Now, peer, body, true)); TrimChats(); }
        SaveChatData(); ChatChanged?.Invoke();
    }
    private void TrimChats() { if (_chats.Count > 1000) _chats.RemoveRange(0, _chats.Count - 1000); }
    public JabberConfig Config { get; private set; } = new();
    public string Status { get; private set; } = "Disconnected";
    public event Action? Changed;
    public event Action<JabberPing>? Received;
    public IReadOnlyList<JabberPing> History { get { lock (_gate) return _history.ToArray(); } }
    public JabberService()
    {
        try { if (File.Exists(ChatFile)) _chats.AddRange(JsonSerializer.Deserialize<List<JabberChatMessage>>(File.ReadAllText(ChatFile)) ?? new()); } catch { }
        try { if (File.Exists(ContactsFile)) foreach (var c in JsonSerializer.Deserialize<List<JabberContact>>(File.ReadAllText(ContactsFile)) ?? new()) _contacts[c.Jid] = c; } catch { }
        try { if (File.Exists(RoomsFile)) foreach (var room in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RoomsFile)) ?? new()) _rooms.Add(room); } catch { }
        try { if (File.Exists(ConfigFile)) Config = JsonSerializer.Deserialize<JabberConfig>(File.ReadAllText(ConfigFile)) ?? new(); } catch { }
        try { if (File.Exists(HistoryFile)) _history = JsonSerializer.Deserialize<List<JabberPing>>(File.ReadAllText(HistoryFile)) ?? new(); } catch { }
        try { if (File.Exists(ParticipationFile)) Participation = JsonSerializer.Deserialize<ParticipationStats>(File.ReadAllText(ParticipationFile)); } catch { }
        try
        {
            string path = Path.Combine(Folder, "latest-account-info.json");
            if (File.Exists(path))
            {
                var saved = JsonSerializer.Deserialize<AccountInfoCache>(File.ReadAllText(path));
                if (saved is not null) ReadParticipation(saved.Body, saved.ReceivedAt);
            }
        }
        catch { }

        if (Config.AutoConnect) Connect();
    }
    public void Save(JabberConfig config, string? password)
    {
        if (password is { Length: > 0 }) _sessionPassword = password;
        config.PasswordProtected = config.RememberPassword
            ? (password is { Length: > 0 }
                ? Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser))
                : Config.PasswordProtected)
            : "";
        Directory.CreateDirectory(Folder);
        File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config));
        Config = config;
        Changed?.Invoke();
    }
    private string Password()
    {
        if (_sessionPassword.Length > 0) return _sessionPassword;
        if (Config.PasswordProtected.Length == 0) return "";
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(Config.PasswordProtected), null, DataProtectionScope.CurrentUser));
    }
    public void Connect()
    {
        if (_runner is { IsCompleted: false }) { SetStatus("Already connecting or connected"); return; }
        if (_stop.IsCancellationRequested) { _stop.Dispose(); _stop = new CancellationTokenSource(); }
        _runner = Task.Run(() => Run(_stop.Token));
    }
    public void Disconnect() { _stop.Cancel(); Status = "Disconnected"; Changed?.Invoke(); }
    public void MarkRead(bool all, int index = -1)
    {
        lock (_gate)
        {
            if (all) _history = _history.Select(p => p with { Read = true }).ToList();
            else if (index >= 0 && index < _history.Count) _history[index] = _history[index] with { Read = true };
            SaveHistory();
        }
        Changed?.Invoke();
    }
    public void Clear() { lock (_gate) { _history.Clear(); SaveHistory(); } Changed?.Invoke(); }
    private void SaveHistory()
    {
        try { Directory.CreateDirectory(Folder); File.WriteAllText(HistoryFile, JsonSerializer.Serialize(_history)); } catch { }
    }
    private void SetStatus(string value)
    {
        Status = value;
        Log.Debug("JABBER", "state=" + (value.StartsWith("Disconnected:") ? "connection failed" : value));
        if (value.StartsWith("Disconnected:", StringComparison.Ordinal)) Log.Write("Jabber connection failed (see connection status).");
        Changed?.Invoke();
    }
      // XmppDotNet owns the XML stream, TLS, SASL, resource binding and reconnection.
      // Never replace XmlReader instances on the live XMPP socket.
      private XmppDotNet.XmppClient? _client;
      private readonly object _connectionGate = new();
      private async Task Run(CancellationToken token)
      {
          XmppDotNet.XmppClient? client = null;
          IDisposable? messageSubscription = null;
          IDisposable? stateSubscription = null;
          try
          {
              string login = Config.Login.Trim();
              if (login.Split('@').Length != 2) throw new InvalidOperationException("Enter a Jabber login (name@domain).");
              string password = Password();
              if (password.Length == 0) throw new InvalidOperationException("Enter a Jabber password.");
              SetStatus("Connecting...");
              client = new XmppDotNet.XmppClient(conf =>
              {
                  conf.UseSocketTransport();
                  conf.AutoReconnect = true;
              })
              {
                  Jid = login,
                  Password = password,
                  Resource = string.IsNullOrWhiteSpace(Config.Resource) ? null : Config.Resource,
                  Tls = true
              };
              var session = client;
              lock (_connectionGate) _client = session;
              stateSubscription = session.StateChanged.Subscribe(state =>
              {
                  string name = state.ToString();
                  Log.Debug("JABBER", "session=" + name);
                  if (name.Equals("Binded", StringComparison.OrdinalIgnoreCase))
                  {
                      SetStatus("Connected");
                      _ = Task.Run(async () =>
                      {
                          try { await session.SendPresenceAsync(Show.Chat, "Available");
                                Log.Debug("JABBER", "presence sent; message listener active");
                                foreach (var room in Rooms) { try { await JoinRoomPresence(room); } catch { } } }
                          catch (Exception ex) { Log.Debug("JABBER", "presence error=" + ex.GetType().Name); }
                      });
                  }
                  else if (name.Contains("Disconnect", StringComparison.OrdinalIgnoreCase)) SetStatus("Disconnected");
              });
              // Subscribe before ConnectAsync. All message types arrive here, including normal and headline.
              messageSubscription = session.XmppXElementReceived
                  .Where(element => element is Message)
                  .Subscribe(element =>
                  {
                      try { ProcessIncoming(System.Xml.Linq.XElement.Parse(element.ToString())); }
                      catch (Exception ex) { Log.Debug("JABBER", "message processing error=" + ex.GetType().Name); }
                  });
              Log.Debug("JABBER", "message handler registered before connect");
              await session.ConnectAsync();
              // Keep the service alive while the library manages the connection and reconnects.
              await Task.Delay(Timeout.Infinite, token);
          }
          catch (OperationCanceledException) when (token.IsCancellationRequested) { }
          catch (Exception ex)
          {
              Log.Debug("JABBER", "connection error=" + ex.GetType().Name);
              SetStatus("Disconnected: " + ex.Message);
          }
          finally
          {
              messageSubscription?.Dispose();
              stateSubscription?.Dispose();
              lock (_connectionGate) { if (ReferenceEquals(_client, client)) _client = null; }
              if (client is not null)
              {
                  try { await client.DisconnectAsync(); } catch { }
              }
              if (token.IsCancellationRequested) SetStatus("Disconnected");
          }
      }
      private void ProcessIncoming(System.Xml.Linq.XElement message)
    {
        string type = (string?)message.Attribute("type") ?? "normal";
        string sender = (string?)message.Attribute("from") ?? "Unknown";
        string peer = sender.Split('/')[0];
        var inner = message.Descendants().FirstOrDefault(x => x.Name.LocalName == "forwarded")?
            .Descendants().FirstOrDefault(x => x.Name.LocalName == "message");
        if (inner is not null) { message = inner; sender = (string?)inner.Attribute("from") ?? sender; peer = sender.Split('/')[0]; }
        string body = message.Elements().FirstOrDefault(x => x.Name.LocalName == "body")?.Value ?? "";
        if (string.IsNullOrWhiteSpace(body)) return;
        bool director = peer.Split('@')[0].Equals("directorbot", StringComparison.OrdinalIgnoreCase);
        bool broadcast = director && System.Text.RegularExpressions.Regex.IsMatch(body,
            @"(?im)\bthis\s+was\s+(?:an?\s+)?(?:[\w-]+\s+)*broadcast\s+from\b");
        if (!director)
        {
            lock (_gate)
            {
                _chats.Add(new JabberChatMessage(DateTime.Now, peer, body)); TrimChats();
                if (!type.Equals("groupchat", StringComparison.OrdinalIgnoreCase))
                    _contacts.TryAdd(peer, new JabberContact(peer, peer.Split('@')[0], false));
                else _rooms.Add(peer);
            }
            SaveChatData();
            try { ChatChanged?.Invoke(); } catch { }
        }
        else
        {
            // All directorbot messages are examined independently of the normal Jabber chat list.
            ReadParticipation(body, DateTime.Now);
        }
        if (!broadcast) return;
        var ping = new JabberPing(DateTime.Now, "directorbot", peer, body);
        lock (_gate) { _history.Insert(0, ping); if (_history.Count > 500) _history.RemoveRange(500, _history.Count - 500); SaveHistory(); }
        try { Received?.Invoke(ping); } catch { }
        try { Changed?.Invoke(); } catch { }
    }
    public void Dispose() { _stop.Cancel(); _stop.Dispose(); }
}
