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
    private static readonly System.Text.RegularExpressions.Regex MonthPattern = new(@"(?im)^\s*(?:->\s*)?This Month:\s*(\d+)\s+STR\s*\+\s*(\d+)\s+PCT\s*$");
    private static readonly System.Text.RegularExpressions.Regex LastPattern = new(@"(?im)^\s*(?:->\s*)?Last strat[- ]pap:\s*(.+?)\s*$");
    private bool ReadParticipation(string body)
    {
        var counts = MonthPattern.Match(body);
        if (!counts.Success || !int.TryParse(counts.Groups[1].Value, out int strat) || !int.TryParse(counts.Groups[2].Value, out int pct)) return false;
        var last = LastPattern.Match(body);
        string lastText = last.Success ? last.Groups[1].Value.Trim() : "--";
        Participation = new ParticipationStats(strat, pct, lastText);
        try { Directory.CreateDirectory(Folder); File.WriteAllText(ParticipationFile, JsonSerializer.Serialize(Participation)); }
        catch (Exception ex) { Log.Debug("JABBER", "participation save failed: " + ex.GetType().Name); }
        Log.Debug("JABBER", "participation report updated");
        try { Changed?.Invoke(); } catch { }
        return true;
    }

    private CancellationTokenSource _stop = new();
    private Task? _runner;
    private string _sessionPassword = "";
    private readonly object _gate = new();
    private List<JabberPing> _history = new();
    private readonly List<JabberChatMessage> _chats = new();
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
        ChatChanged?.Invoke();
    }
    private void TrimChats() { if (_chats.Count > 1000) _chats.RemoveRange(0, _chats.Count - 1000); }
    public JabberConfig Config { get; private set; } = new();
    public string Status { get; private set; } = "Disconnected";
    public event Action? Changed;
    public event Action<JabberPing>? Received;
    public IReadOnlyList<JabberPing> History { get { lock (_gate) return _history.ToArray(); } }
    public JabberService()
    {
        try { if (File.Exists(ConfigFile)) Config = JsonSerializer.Deserialize<JabberConfig>(File.ReadAllText(ConfigFile)) ?? new(); } catch { }
        try { if (File.Exists(HistoryFile)) _history = JsonSerializer.Deserialize<List<JabberPing>>(File.ReadAllText(HistoryFile)) ?? new(); } catch { }
        try { if (File.Exists(ParticipationFile)) Participation = JsonSerializer.Deserialize<ParticipationStats>(File.ReadAllText(ParticipationFile)); } catch { }
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
                                Log.Debug("JABBER", "presence sent; message listener active"); }
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
          string body = message.Elements().FirstOrDefault(x => x.Name.LocalName == "body")?.Value ?? "";
          Log.Debug("JABBER", "incoming stanza type=" + type + "; body=" + (body.Length > 0 ? "yes" : "no"));
          if (string.IsNullOrWhiteSpace(body)) return;
          if (peer.Split('@')[0].Equals("directorbot", StringComparison.OrdinalIgnoreCase) && ReadParticipation(body)) return;
          bool director = peer.Split('@')[0].Equals("directorbot", StringComparison.OrdinalIgnoreCase);
                      // Directorbot broadcasts carry a footer naming their broadcast audience/type.
            // Never require fleet fields: GICE, Locusts and other announcements can omit them.
            bool broadcast = director && System.Text.RegularExpressions.Regex.IsMatch(body,
                @"(?im)\bthis\s+was\s+(?:an?\s+)?(?:[\w-]+\s+)*broadcast\s+from\b");
            if (director && !broadcast)
                Log.Debug("JABBER", "directorbot message without broadcast footer routed to Chat");
if (!broadcast)
          {
              lock (_gate) { _chats.Add(new JabberChatMessage(DateTime.Now, peer, body)); TrimChats(); }
              Log.Debug("JABBER", "routed to Chat");
              try { ChatChanged?.Invoke(); } catch (Exception ex) { Log.Debug("JABBER", "chat view error=" + ex.GetType().Name); }
              return;
          }
          var ping = new JabberPing(DateTime.Now, "directorbot", peer, body);
          lock (_gate) { _history.Insert(0, ping); if (_history.Count > 500) _history.RemoveRange(500, _history.Count - 500); SaveHistory(); }
          Log.Debug("JABBER", "routed to Pings");
          try { Received?.Invoke(ping); } catch (Exception ex) { Log.Debug("JABBER", "ping notification error=" + ex.GetType().Name); }
          try { Changed?.Invoke(); } catch (Exception ex) { Log.Debug("JABBER", "ping view error=" + ex.GetType().Name); }
      }
    public void Dispose() { _stop.Cancel(); _stop.Dispose(); }
}
