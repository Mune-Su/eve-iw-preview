using System.IO;
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
    private CancellationTokenSource _stop = new();
    private Task? _runner;
    private string _sessionPassword = "";
    private readonly object _gate = new();
    private List<JabberPing> _history = new();
    public JabberConfig Config { get; private set; } = new();
    public string Status { get; private set; } = "Disconnected";
    public event Action? Changed;
    public event Action<JabberPing>? Received;
    public IReadOnlyList<JabberPing> History { get { lock (_gate) return _history.ToArray(); } }
    public JabberService()
    {
        try { if (File.Exists(ConfigFile)) Config = JsonSerializer.Deserialize<JabberConfig>(File.ReadAllText(ConfigFile)) ?? new(); } catch { }
        try { if (File.Exists(HistoryFile)) _history = JsonSerializer.Deserialize<List<JabberPing>>(File.ReadAllText(HistoryFile)) ?? new(); } catch { }
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
    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? "";
    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode, EntryPoint = "DnsQuery_W", SetLastError = true)]
    private static extern int DnsQuery(string name, ushort type, uint options, nint extra, out nint records, nint reserved);
    [DllImport("dnsapi.dll")] private static extern void DnsRecordListFree(nint records, int freeType);
    [StructLayout(LayoutKind.Sequential)] private struct DnsSrvData
    {
        public nint Target;
        public ushort Priority;
        public ushort Weight;
        public ushort Port;
        public ushort Pad;
    }
    private static (string Host, int Port) DiscoverServer(string domain)
    {
        nint records = 0;
        try
        {
            // DNS_TYPE_SRV = 33; DnsRecord layout is pointer-sized on x64.
            if (DnsQuery("_xmpp-client._tcp." + domain, 33, 0, 0, out records, 0) == 0 && records != 0)
            {
                nint data = records + 32;
                nint targetPtr = Marshal.ReadIntPtr(data);
                int port = (ushort)Marshal.ReadInt16(data, 12);
                string? target = Marshal.PtrToStringUni(targetPtr)?.TrimEnd('.');
                if (!string.IsNullOrWhiteSpace(target) && port is > 0 and <= 65535) return (target, port);
            }
        }
        catch { }
        finally { if (records != 0) DnsRecordListFree(records, 1); }
        return (domain, 5222);
    }
    private async Task Run(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                string login = Config.Login.Trim();
                string[] parts = login.Split('@', 2);
                if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) throw new InvalidOperationException("Enter a Jabber login (name@domain).");
                var (host, port) = DiscoverServer(parts[1]);
                string password = Password();
                if (password.Length == 0) throw new InvalidOperationException("Enter a Jabber password.");
                SetStatus("Connecting...");
                // The alliance documents the domain and an explicit fallback host.
                // Only retry the fallback for transport failures, never for rejected credentials.
                TcpClient tcp = new();
                try
                {
                    try { await tcp.ConnectAsync(host, port, token); }
                    catch (SocketException) when (parts[1].Equals("goonfleet.com", StringComparison.OrdinalIgnoreCase)
                                                  && !host.Equals("jabber-server.goonfleet.com", StringComparison.OrdinalIgnoreCase))
                    {
                        tcp.Dispose();
                        tcp = new TcpClient();
                        host = "jabber-server.goonfleet.com";
                        port = 5222;
                        await tcp.ConnectAsync(host, port, token);
                    }
                    using var raw = tcp.GetStream();
                using var ssl = new SslStream(raw, false);
                async Task Send(Stream stream, string xml)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(xml);
                    await stream.WriteAsync(bytes, token);
                    await stream.FlushAsync(token);
                }
                string opening = "<stream:stream to='" + Escape(parts[1]) + "' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams' version='1.0'>";
                await Send(raw, opening);
                // Read until STARTTLS is advertised; no authentication is sent over plaintext.
                var reader = XmlReader.Create(raw, new XmlReaderSettings { Async = true, ConformanceLevel = ConformanceLevel.Fragment, IgnoreWhitespace = true, CloseInput = false });
                bool tls = false;
                while (await reader.ReadAsync())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "starttls") { tls = true; break; }
                }
                if (!tls) throw new InvalidOperationException("The Jabber server did not offer STARTTLS.");
                await Send(raw, "<starttls xmlns='urn:ietf:params:xml:ns:xmpp-tls'/>");
                while (await reader.ReadAsync()) if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "proceed" or "failure") { if (reader.LocalName == "failure") throw new InvalidOperationException("Server refused STARTTLS."); break; }
                reader.Dispose();
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, token);
                await Send(ssl, opening);
                // SASL PLAIN is permitted only inside verified TLS.
                string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("\0" + parts[0] + "\0" + password));
                using var secureReader = XmlReader.Create(ssl, new XmlReaderSettings { Async = true, ConformanceLevel = ConformanceLevel.Fragment, IgnoreWhitespace = true, CloseInput = false });
                bool plain = false;
                while (await secureReader.ReadAsync())
                {
                    if (secureReader.NodeType == XmlNodeType.Element && secureReader.LocalName == "mechanism" && await secureReader.ReadElementContentAsStringAsync() == "PLAIN") { plain = true; break; }
                }
                if (!plain) throw new InvalidOperationException("Server requires an unsupported authentication method.");
                await Send(ssl, "<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='PLAIN'>" + payload + "</auth>");
                while (await secureReader.ReadAsync()) if (secureReader.NodeType == XmlNodeType.Element && secureReader.LocalName is "success" or "failure") { if (secureReader.LocalName == "failure")
                    {
                        string detail = await secureReader.ReadInnerXmlAsync();
                        string reason = detail.Contains("not-authorized", StringComparison.OrdinalIgnoreCase) ? "Server refused the account credentials or authentication method" :
                            detail.Contains("temporary-auth-failure", StringComparison.OrdinalIgnoreCase) ? "Server reported a temporary authentication failure" :
                            "Server rejected SASL PLAIN authentication";
                        throw new InvalidOperationException(reason + ".");
                    } break; }
                // Restart stream and bind a resource.
                secureReader.Dispose();
                await Send(ssl, opening);
                using var messages = XmlReader.Create(ssl, new XmlReaderSettings { Async = true, ConformanceLevel = ConformanceLevel.Fragment, IgnoreWhitespace = true, CloseInput = false });
                while (await messages.ReadAsync()) if (messages.NodeType == XmlNodeType.Element && messages.LocalName == "features") break;
                await Send(ssl, "<iq type='set' id='bind1'><bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'><resource>" + Escape(string.IsNullOrWhiteSpace(Config.Resource) ? "EVE-IW-Preview" : Config.Resource) + "</resource></bind></iq>");
                bool bound = false;
                while (await messages.ReadAsync()) if (messages.NodeType == XmlNodeType.Element && messages.LocalName == "iq" && messages.GetAttribute("id") == "bind1")
                {
                    bound = messages.GetAttribute("type") == "result";
                    break;
                }
                if (!bound) throw new InvalidOperationException("Jabber resource binding failed.");
                await Send(ssl, "<presence/>");
                SetStatus("Connected");
                Log.Debug("JABBER", "resource bound, presence sent; receive loop started");
                Log.Debug("JABBER", "direct-message listener active");
                while (!token.IsCancellationRequested && await messages.ReadAsync())
                {
                    if (messages.NodeType != XmlNodeType.Element || messages.LocalName != "message") continue;
                    Log.Debug("JABBER", "incoming message stanza type=" + (messages.GetAttribute("type") ?? "normal"));
                    var element = await XNode.ReadFromAsync(messages, token) as XElement;
                    string body = element?.Elements().FirstOrDefault(e => e.Name.LocalName == "body")?.Value ?? "";
                    if (string.IsNullOrWhiteSpace(body)) { Log.Debug("JABBER", "message ignored: no body"); continue; }
                    string sender = element?.Attribute("from")?.Value ?? "Unknown";
                    var ping = new JabberPing(DateTime.Now, sender.Split('/').Last(), sender.Split('/')[0], body);
                    lock (_gate) { _history.Insert(0, ping); if (_history.Count > 500) _history.RemoveRange(500, _history.Count - 500); SaveHistory(); }
                    Log.Debug("JABBER", "message saved; notification subscribers=" + (Received?.GetInvocationList().Length ?? 0));
                    try { Received?.Invoke(ping); } catch (Exception ex) { Log.Debug("JABBER", "notification subscriber error: " + ex.GetType().Name); }
                    try { Changed?.Invoke(); } catch (Exception ex) { Log.Debug("JABBER", "viewer refresh error: " + ex.GetType().Name); }
                }
                }
                finally { tcp.Dispose(); }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Debug("JABBER", "receive/connection exception: " + ex.GetType().Name); SetStatus("Disconnected: " + ex.Message); }
            if (!Config.AutoConnect) break;
            try { await Task.Delay(10000, token); } catch (OperationCanceledException) { break; }
        }
        if (token.IsCancellationRequested) SetStatus("Disconnected");
    }
    public void Dispose() { _stop.Cancel(); _stop.Dispose(); }
}
