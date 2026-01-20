using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace XmppClient;

// ============================================================================
// XEP-0199: XMPP Ping
// ============================================================================

public sealed class PingManager
{
    private readonly Func<string, Task> _sendStanza;
    private readonly Dictionary<string, (TaskCompletionSource<TimeSpan> Tcs, DateTime Sent)> _pending = new();
    private readonly object _lock = new();
    private int _counter;
    
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    
    public event Action<string, TimeSpan>? OnPong;
    public event Action<string>? OnPingTimeout;

    public PingManager(Func<string, Task> sendStanza)
    {
        _sendStanza = sendStanza;
    }

    /// <summary>
    /// Sendet einen Ping und misst die Antwortzeit
    /// </summary>
    public async Task<TimeSpan?> PingAsync(string? to = null, CancellationToken ct = default)
    {
        var id = $"ping-{Interlocked.Increment(ref _counter)}";
        var tcs = new TaskCompletionSource<TimeSpan>();
        var sent = DateTime.UtcNow;
        
        lock (_lock)
        {
            _pending[id] = (tcs, sent);
        }
        
        var toAttr = to != null ? $" to='{XmlEscape(to)}'" : "";
        await _sendStanza($"<iq type='get' id='{id}'{toAttr}><ping xmlns='urn:xmpp:ping'/></iq>");
        
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            lock (_lock) _pending.Remove(id);
            OnPingTimeout?.Invoke(to ?? "server");
            return null;
        }
    }

    /// <summary>
    /// Verarbeitet eine Ping-Antwort
    /// </summary>
    public bool ProcessPong(string id)
    {
        lock (_lock)
        {
            if (!_pending.TryGetValue(id, out var entry))
                return false;
            
            _pending.Remove(id);
            var rtt = DateTime.UtcNow - entry.Sent;
            entry.Tcs.TrySetResult(rtt);
            OnPong?.Invoke(id, rtt);
            return true;
        }
    }

    /// <summary>
    /// Beantwortet einen Ping
    /// </summary>
    public Task RespondAsync(string id, string from) =>
        _sendStanza($"<iq type='result' id='{id}' to='{XmlEscape(from)}'/>");

    /// <summary>
    /// Prüft ob ein IQ ein Ping ist
    /// </summary>
    public static bool IsPing(string xml) =>
        xml.Contains("urn:xmpp:ping") && xml.Contains("type='get'");

    private static string XmlEscape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;")
        .Replace(">", "&gt;").Replace("'", "&apos;");
}

// ============================================================================
// XEP-0030: Service Discovery
// ============================================================================

public sealed record DiscoIdentity(string Category, string Type, string? Name = null)
{
    public override string ToString() => Name != null ? $"{Category}/{Type} ({Name})" : $"{Category}/{Type}";
}

public sealed record DiscoItem(string Jid, string? Node = null, string? Name = null)
{
    public override string ToString() => Name != null ? $"{Jid} ({Name})" : Jid;
}

public sealed class DiscoInfo
{
    public string From { get; init; } = "";
    public string? Node { get; init; }
    public List<DiscoIdentity> Identities { get; } = [];
    public List<string> Features { get; } = [];
    
    public bool HasFeature(string feature) => Features.Contains(feature);
    public bool SupportsReceipts => HasFeature("urn:xmpp:receipts");
    public bool SupportsCarbons => HasFeature("urn:xmpp:carbons:2");
    public bool SupportsChatStates => HasFeature("http://jabber.org/protocol/chatstates");
    public bool SupportsChatMarkers => HasFeature("urn:xmpp:chat-markers:0");
    public bool SupportsStreamManagement => HasFeature("urn:xmpp:sm:3");
}

public sealed class DiscoItems
{
    public string From { get; init; } = "";
    public string? Node { get; init; }
    public List<DiscoItem> Items { get; } = [];
}

public sealed class DiscoManager
{
    private readonly Func<string, Task> _sendStanza;
    private readonly Dictionary<string, TaskCompletionSource<DiscoInfo>> _infoQueries = new();
    private readonly Dictionary<string, TaskCompletionSource<DiscoItems>> _itemsQueries = new();
    private readonly object _lock = new();
    private int _counter;
    
    // Lokale Features die wir unterstützen
    public List<DiscoIdentity> LocalIdentities { get; } = [
        new("client", "console", "XMPP Console Client")
    ];
    
    public List<string> LocalFeatures { get; } = [
        "http://jabber.org/protocol/disco#info",
        "http://jabber.org/protocol/disco#items",
        "urn:xmpp:ping",
        "urn:xmpp:receipts",
        "urn:xmpp:carbons:2",
        "urn:xmpp:chat-markers:0",
        "http://jabber.org/protocol/chatstates",
        "http://jabber.org/protocol/caps"
    ];

    public DiscoManager(Func<string, Task> sendStanza)
    {
        _sendStanza = sendStanza;
    }

    /// <summary>
    /// Fragt disco#info ab
    /// </summary>
    public async Task<DiscoInfo?> QueryInfoAsync(string jid, string? node = null, 
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var id = $"disco-info-{Interlocked.Increment(ref _counter)}";
        var tcs = new TaskCompletionSource<DiscoInfo>();
        
        lock (_lock) _infoQueries[id] = tcs;
        
        var nodeAttr = node != null ? $" node='{XmlEscape(node)}'" : "";
        await _sendStanza(
            $"<iq type='get' to='{XmlEscape(jid)}' id='{id}'>" +
            $"<query xmlns='http://jabber.org/protocol/disco#info'{nodeAttr}/></iq>");
        
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            lock (_lock) _infoQueries.Remove(id);
            return null;
        }
    }

    /// <summary>
    /// Fragt disco#items ab
    /// </summary>
    public async Task<DiscoItems?> QueryItemsAsync(string jid, string? node = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var id = $"disco-items-{Interlocked.Increment(ref _counter)}";
        var tcs = new TaskCompletionSource<DiscoItems>();
        
        lock (_lock) _itemsQueries[id] = tcs;
        
        var nodeAttr = node != null ? $" node='{XmlEscape(node)}'" : "";
        await _sendStanza(
            $"<iq type='get' to='{XmlEscape(jid)}' id='{id}'>" +
            $"<query xmlns='http://jabber.org/protocol/disco#items'{nodeAttr}/></iq>");
        
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            lock (_lock) _itemsQueries.Remove(id);
            return null;
        }
    }

    /// <summary>
    /// Verarbeitet eine disco#info Antwort
    /// </summary>
    public bool ProcessInfoResult(string id, string xml, string from)
    {
        TaskCompletionSource<DiscoInfo>? tcs;
        lock (_lock)
        {
            if (!_infoQueries.TryGetValue(id, out tcs))
                return false;
            _infoQueries.Remove(id);
        }
        
        var info = new DiscoInfo { From = from };
        
        // Parse identities
        foreach (Match m in Regex.Matches(xml, @"<identity([^/>]+)/?>" ))
        {
            var attrs = m.Groups[1].Value;
            info.Identities.Add(new DiscoIdentity(
                ExtractAttr(attrs, "category") ?? "",
                ExtractAttr(attrs, "type") ?? "",
                ExtractAttr(attrs, "name")
            ));
        }
        
        // Parse features
        foreach (Match m in Regex.Matches(xml, @"<feature\s+var=['""]([^'""]+)['""]"))
        {
            info.Features.Add(m.Groups[1].Value);
        }
        
        tcs.TrySetResult(info);
        return true;
    }

    /// <summary>
    /// Verarbeitet eine disco#items Antwort
    /// </summary>
    public bool ProcessItemsResult(string id, string xml, string from)
    {
        TaskCompletionSource<DiscoItems>? tcs;
        lock (_lock)
        {
            if (!_itemsQueries.TryGetValue(id, out tcs))
                return false;
            _itemsQueries.Remove(id);
        }
        
        var items = new DiscoItems { From = from };
        
        foreach (Match m in Regex.Matches(xml, @"<item([^/>]+)/?>" ))
        {
            var attrs = m.Groups[1].Value;
            var jid = ExtractAttr(attrs, "jid");
            if (jid != null)
            {
                items.Items.Add(new DiscoItem(jid, ExtractAttr(attrs, "node"), ExtractAttr(attrs, "name")));
            }
        }
        
        tcs.TrySetResult(items);
        return true;
    }

    /// <summary>
    /// Beantwortet eine disco#info Anfrage
    /// </summary>
    public Task RespondInfoAsync(string id, string from, string? node = null)
    {
        var sb = new StringBuilder();
        sb.Append($"<iq type='result' id='{id}' to='{XmlEscape(from)}'>");
        
        var nodeAttr = node != null ? $" node='{XmlEscape(node)}'" : "";
        sb.Append($"<query xmlns='http://jabber.org/protocol/disco#info'{nodeAttr}>");
        
        foreach (var identity in LocalIdentities)
        {
            sb.Append($"<identity category='{identity.Category}' type='{identity.Type}'");
            if (identity.Name != null)
                sb.Append($" name='{XmlEscape(identity.Name)}'");
            sb.Append("/>");
        }
        
        foreach (var feature in LocalFeatures)
        {
            sb.Append($"<feature var='{feature}'/>");
        }
        
        sb.Append("</query></iq>");
        return _sendStanza(sb.ToString());
    }

    private static string? ExtractAttr(string attrs, string name)
    {
        var match = Regex.Match(attrs, $@"{name}\s*=\s*['""]([^'""]*)['""]");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string XmlEscape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;")
        .Replace(">", "&gt;").Replace("'", "&apos;");
}

// ============================================================================
// XEP-0115: Entity Capabilities
// ============================================================================

public sealed class EntityCapsManager
{
    private readonly DiscoManager _disco;
    private readonly Dictionary<string, DiscoInfo> _cache = new();
    private readonly object _lock = new();
    
    public string Node { get; set; } = "https://github.com/xmpp-console";
    
    public event Action<string, DiscoInfo>? OnCapsDiscovered;

    public EntityCapsManager(DiscoManager disco)
    {
        _disco = disco;
    }

    /// <summary>
    /// Berechnet den Verification String (SHA-1 Hash der Features)
    /// </summary>
    public string CalculateVerificationString()
    {
        var sb = new StringBuilder();
        
        // Identities sortiert: category/type/xml:lang/name
        foreach (var id in _disco.LocalIdentities.OrderBy(i => $"{i.Category}/{i.Type}"))
        {
            sb.Append($"{id.Category}/{id.Type}//{id.Name ?? ""}<");
        }
        
        // Features sortiert
        foreach (var feature in _disco.LocalFeatures.Order())
        {
            sb.Append($"{feature}<");
        }
        
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Erzeugt das <c> Element für Presence
    /// </summary>
    public string GetCapsElement()
    {
        var ver = CalculateVerificationString();
        return $"<c xmlns='http://jabber.org/protocol/caps' hash='sha-1' node='{Node}' ver='{ver}'/>";
    }

    /// <summary>
    /// Verarbeitet ein caps-Element aus Presence
    /// </summary>
    public async Task ProcessCapsAsync(string from, string node, string ver, CancellationToken ct = default)
    {
        var cacheKey = $"{node}#{ver}";
        
        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                OnCapsDiscovered?.Invoke(from, cached);
                return;
            }
        }
        
        // Noch nicht im Cache - disco#info abfragen
        var info = await _disco.QueryInfoAsync(from, cacheKey, ct: ct);
        
        if (info != null)
        {
            lock (_lock)
            {
                _cache[cacheKey] = info;
            }
            OnCapsDiscovered?.Invoke(from, info);
        }
    }

    /// <summary>
    /// Prüft ob ein JID ein Feature unterstützt (aus Cache)
    /// </summary>
    public DiscoInfo? GetCachedInfo(string verString)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(verString, out var info) ? info : null;
        }
    }

    /// <summary>
    /// Extrahiert Caps aus einer Presence
    /// </summary>
    public static (string Node, string Ver, string? Hash)? ParseCaps(string presenceXml)
    {
        var match = Regex.Match(presenceXml,
            @"<c\s+[^>]*xmlns=['""]http://jabber\.org/protocol/caps['""][^>]*>",
            RegexOptions.Singleline);
        
        if (!match.Success) return null;
        
        var elem = match.Value;
        var nodeMatch = Regex.Match(elem, @"node=['""]([^'""]+)['""]");
        var verMatch = Regex.Match(elem, @"ver=['""]([^'""]+)['""]");
        var hashMatch = Regex.Match(elem, @"hash=['""]([^'""]+)['""]");
        
        if (nodeMatch.Success && verMatch.Success)
        {
            return (nodeMatch.Groups[1].Value, verMatch.Groups[1].Value,
                    hashMatch.Success ? hashMatch.Groups[1].Value : null);
        }
        
        return null;
    }
}

// ============================================================================
// XEP-0198: Stream Management
// ============================================================================

public sealed class StreamManagementManager
{
    private readonly Func<string, Task> _sendStanza;
    
    private bool _enabled;
    private uint _inbound;       // Empfangene Stanzas
    private uint _outbound;      // Gesendete Stanzas
    private uint _lastAcked;     // Letzte bestätigte
    
    private readonly Queue<(uint Seq, string Stanza, DateTime Sent)> _unacked = new();
    private readonly object _lock = new();
    
    private string? _resumeId;
    private bool _resumable;
    
    public bool IsEnabled => _enabled;
    public bool CanResume => _resumable && _resumeId != null;
    public string? ResumeId => _resumeId;
    public uint InboundCount => _inbound;
    public uint OutboundCount => _outbound;
    public int UnackedCount { get { lock (_lock) return _unacked.Count; } }
    
    public event Action<uint>? OnAckReceived;
    public event Action<List<string>>? OnStanzasLost;
    public event Action? OnResumed;

    public StreamManagementManager(Func<string, Task> sendStanza)
    {
        _sendStanza = sendStanza;
    }

    /// <summary>
    /// Aktiviert Stream Management nach Bind
    /// </summary>
    public Task EnableAsync(bool requestResume = true)
    {
        var resume = requestResume ? " resume='true'" : "";
        return _sendStanza($"<enable xmlns='urn:xmpp:sm:3'{resume}/>");
    }

    /// <summary>
    /// Verarbeitet <enabled> vom Server
    /// </summary>
    public void ProcessEnabled(string xml)
    {
        _enabled = true;
        _inbound = 0;
        _outbound = 0;
        _lastAcked = 0;
        
        lock (_lock) _unacked.Clear();
        
        var idMatch = Regex.Match(xml, @"id=['""]([^'""]+)['""]");
        _resumeId = idMatch.Success ? idMatch.Groups[1].Value : null;
        _resumable = xml.Contains("resume='true'") || xml.Contains("resume=\"true\"");
        
        Console.WriteLine($"[+] Stream Management aktiviert" +
                         (_resumable ? $" (Resume-ID: {_resumeId?[..Math.Min(8, _resumeId?.Length ?? 0)]}...)" : ""));
    }

    /// <summary>
    /// Versucht Stream zu resumen
    /// </summary>
    public Task ResumeAsync()
    {
        if (!CanResume)
            throw new InvalidOperationException("Stream kann nicht resumed werden");
        
        return _sendStanza($"<resume xmlns='urn:xmpp:sm:3' h='{_inbound}' previd='{_resumeId}'/>");
    }

    /// <summary>
    /// Verarbeitet <resumed>
    /// </summary>
    public void ProcessResumed(string xml)
    {
        var hMatch = Regex.Match(xml, @"h=['""](\d+)['""]");
        if (hMatch.Success)
        {
            ProcessAck(uint.Parse(hMatch.Groups[1].Value));
        }
        
        _enabled = true;
        Console.WriteLine("[+] Stream resumed!");
        OnResumed?.Invoke();
    }

    /// <summary>
    /// Verarbeitet <failed>
    /// </summary>
    public void ProcessFailed()
    {
        List<string> lost;
        lock (_lock)
        {
            lost = _unacked.Select(x => x.Stanza).ToList();
            _unacked.Clear();
        }
        
        _resumable = false;
        _resumeId = null;
        
        if (lost.Count > 0)
        {
            Console.WriteLine($"[!] Stream Resume fehlgeschlagen - {lost.Count} Stanzas verloren");
            OnStanzasLost?.Invoke(lost);
        }
    }

    /// <summary>
    /// Trackt eine ausgehende Stanza
    /// </summary>
    public void TrackOutgoing(string stanza)
    {
        if (!_enabled) return;
        
        lock (_lock)
        {
            _outbound++;
            _unacked.Enqueue((_outbound, stanza, DateTime.UtcNow));
        }
    }

    /// <summary>
    /// Trackt eine eingehende Stanza
    /// </summary>
    public void TrackIncoming()
    {
        if (!_enabled) return;
        Interlocked.Increment(ref _inbound);
    }

    /// <summary>
    /// Fordert Ack vom Server an
    /// </summary>
    public Task RequestAckAsync()
    {
        if (!_enabled) return Task.CompletedTask;
        return _sendStanza("<r xmlns='urn:xmpp:sm:3'/>");
    }

    /// <summary>
    /// Sendet Ack an Server
    /// </summary>
    public Task SendAckAsync()
    {
        if (!_enabled) return Task.CompletedTask;
        return _sendStanza($"<a xmlns='urn:xmpp:sm:3' h='{_inbound}'/>");
    }

    /// <summary>
    /// Verarbeitet <a> (Ack) vom Server
    /// </summary>
    public void ProcessAck(string xml)
    {
        var hMatch = Regex.Match(xml, @"h=['""](\d+)['""]");
        if (hMatch.Success)
        {
            ProcessAck(uint.Parse(hMatch.Groups[1].Value));
        }
    }

    private void ProcessAck(uint h)
    {
        uint acked = 0;
        
        lock (_lock)
        {
            while (_unacked.Count > 0 && _unacked.Peek().Seq <= h)
            {
                _unacked.Dequeue();
                acked++;
            }
            _lastAcked = h;
        }
        
        if (acked > 0)
        {
            OnAckReceived?.Invoke(acked);
        }
    }

    /// <summary>
    /// Verarbeitet <r> (Ack-Request) vom Server
    /// </summary>
    public Task ProcessRequestAsync() => SendAckAsync();

    /// <summary>
    /// Gibt unbestätigte Stanzas zurück (für Resend nach Failed)
    /// </summary>
    public List<string> GetUnackedStanzas()
    {
        lock (_lock)
        {
            return _unacked.Select(x => x.Stanza).ToList();
        }
    }
}

// ============================================================================
// XEP-0333: Chat Markers
// ============================================================================

public enum ChatMarkerType
{
    Received,     // Nachricht empfangen
    Displayed,    // Nachricht angezeigt/gelesen
    Acknowledged  // Nachricht bestätigt
}

public sealed record ChatMarker(ChatMarkerType Type, string From, string MessageId, DateTime ReceivedAt);

public static class ChatMarkers
{
    public const string Namespace = "urn:xmpp:chat-markers:0";

    /// <summary>
    /// Erzeugt <markable/> Element für ausgehende Nachrichten
    /// </summary>
    public static string Markable => $"<markable xmlns='{Namespace}'/>";

    /// <summary>
    /// Erzeugt eine Marker-Nachricht
    /// </summary>
    public static string CreateMarker(string to, string refId, ChatMarkerType type)
    {
        var element = type switch
        {
            ChatMarkerType.Received => "received",
            ChatMarkerType.Displayed => "displayed",
            ChatMarkerType.Acknowledged => "acknowledged",
            _ => "received"
        };
        
        return $"<message to='{XmlEscape(to)}'>" +
               $"<{element} xmlns='{Namespace}' id='{XmlEscape(refId)}'/>" +
               $"</message>";
    }

    /// <summary>
    /// Prüft ob eine Nachricht markierbar ist
    /// </summary>
    public static bool IsMarkable(string xml) => 
        xml.Contains("<markable") && xml.Contains(Namespace);

    /// <summary>
    /// Extrahiert einen Marker aus einer Nachricht
    /// </summary>
    public static ChatMarker? Parse(string xml, string from)
    {
        if (!xml.Contains(Namespace))
            return null;
        
        var types = new[] {
            ("received", ChatMarkerType.Received),
            ("displayed", ChatMarkerType.Displayed),
            ("acknowledged", ChatMarkerType.Acknowledged)
        };
        
        foreach (var (name, type) in types)
        {
            var pattern = $@"<{name}\s+xmlns=['""]urn:xmpp:chat-markers:0['""]\s+id=['""]([^'""]+)['""]";
            var match = Regex.Match(xml, pattern);
            
            if (match.Success)
            {
                return new ChatMarker(type, from, match.Groups[1].Value, DateTime.UtcNow);
            }
        }
        
        return null;
    }

    /// <summary>
    /// Symbol für Marker-Typ
    /// </summary>
    public static string GetSymbol(ChatMarkerType type) => type switch
    {
        ChatMarkerType.Received => "✓",
        ChatMarkerType.Displayed => "👁",
        ChatMarkerType.Acknowledged => "✓✓",
        _ => "?"
    };

    private static string XmlEscape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;")
        .Replace(">", "&gt;").Replace("'", "&apos;");
}
