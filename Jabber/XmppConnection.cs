using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;

namespace XmppClient;

/// <summary>
/// XMPP over WebSocket (RFC 7395) mit Auto-Reconnect
/// </summary>
public sealed class XmppConnection : IAsyncDisposable
{
    private readonly string _wsUri;
    private readonly string _jid;
    private readonly string _password;
    private readonly string _username;
    private readonly string _domain;
    
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    
    private int _messageIdCounter;
    private int _reconnectAttempts;
    private bool _intentionalDisconnect;
    
    // Reconnect-Einstellungen
    public int MaxReconnectAttempts { get; set; } = 5;
    public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);
    
    // State
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string FullJid { get; private set; } = string.Empty;
    public string BareJid => GetBareJid(FullJid);
    
    // Managers
    public Roster Roster { get; } = new();
    public ReceiptTracker Receipts { get; } = new();
    public CarbonManager? Carbons { get; private set; }
    public PubSubManager? PubSub { get; private set; }
    
    // Events
    public event Action<string, string, string, string?>? OnMessage;  // from, to, body, id
    public event Action<string, string>? OnPresence;
    public event Action<string, ChatState>? OnChatState;
    public event Action<string, string>? OnReceiptReceived;
    public event Action<CarbonMessage>? OnCarbonMessage;
    public event Action<PubSubEvent>? OnPubSubEvent;
    public event Action<string>? OnRawXml;
    public event Action<string>? OnError;
    public event Action<string>? OnSpoofingAttempt;
    public event Action<ConnectionState, ConnectionState>? OnStateChanged;  // old, new

    /// <summary>
    /// Erstellt eine neue WebSocket-basierte XMPP-Verbindung
    /// </summary>
    /// <param name="jid">JID (user@domain)</param>
    /// <param name="password">Passwort</param>
    /// <param name="wsUri">WebSocket URI (wss://domain:5443/ws oder null für Auto-Discovery)</param>
    public XmppConnection(string jid, string password, string? wsUri = null)
    {
        _jid = jid;
        _password = password;
        
        var parts = jid.Split('@');
        if (parts.Length != 2)
            throw new ArgumentException("JID muss im Format 'user@domain' sein", nameof(jid));
        
        _username = parts[0];
        _domain = parts[1];
        
        // Standard WebSocket-Endpunkte für bekannte Server
        _wsUri = wsUri ?? $"wss://{_domain}:5443/ws";
        
        Receipts.OnReceiptReceived += (msgId, from) => OnReceiptReceived?.Invoke(from, msgId);
    }

    /// <summary>
    /// Alternative: Verbindung über klassischen TCP mit STARTTLS
    /// </summary>
    public static XmppConnection CreateTcp(string jid, string password, string? server = null, int port = 5222)
    {
        // Fallback auf TCP - wird intern anders behandelt
        var conn = new XmppConnection(jid, password, $"tcp://{server ?? jid.Split('@')[1]}:{port}");
        return conn;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _intentionalDisconnect = false;
        _reconnectAttempts = 0;
        
        await ConnectInternalAsync(ct);
    }

    private async Task ConnectInternalAsync(CancellationToken ct)
    {
        SetState(ConnectionState.Connecting);
        
        try
        {
            // WebSocket verbinden
            _webSocket = new ClientWebSocket();
            _webSocket.Options.AddSubProtocol("xmpp");  // RFC 7395
            
            Console.WriteLine($"[*] Verbinde zu {_wsUri}...");
            await _webSocket.ConnectAsync(new Uri(_wsUri), ct);
            Console.WriteLine("[+] WebSocket verbunden");
            
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            
            // XMPP Stream öffnen
            await SendAsync($"<open xmlns='urn:ietf:params:xml:ns:xmpp-framing' to='{_domain}' version='1.0'/>");
            
            // Features empfangen (kann mehrere Stanzas sein: <open> + <features>)
            var featuresXml = await ReceiveStanzaAsync(ct);
            
            // Manchmal kommt <open> separat
            if (featuresXml.StartsWith("<open"))
            {
                featuresXml = await ReceiveStanzaAsync(ct);
            }
            
            // SASL Mechanismen extrahieren
            var mechanisms = ExtractSaslMechanisms(featuresXml);
            
            if (mechanisms.Count > 0)
            {
                Console.WriteLine($"[*] Verfügbare SASL-Mechanismen: {string.Join(", ", mechanisms)}");
            }
            
            // SASL Auth
            if (mechanisms.Contains("PLAIN"))
            {
                Console.WriteLine("[*] SASL PLAIN Authentifizierung...");
                await PerformSaslPlainAsync(ct);
            }
            else if (mechanisms.Contains("SCRAM-SHA-1"))
            {
                throw new AuthenticationException(
                    $"Server bietet nur SCRAM-SHA-1 an (noch nicht implementiert). " +
                    $"Verfügbar: {string.Join(", ", mechanisms)}");
            }
            else if (mechanisms.Count > 0)
            {
                throw new AuthenticationException(
                    $"Keine unterstützten SASL-Mechanismen. Verfügbar: {string.Join(", ", mechanisms)}");
            }
            else
            {
                throw new AuthenticationException(
                    "Server bietet keine SASL-Mechanismen an. Features: " + 
                    featuresXml[..Math.Min(200, featuresXml.Length)]);
            }
            
            // Neuen Stream öffnen nach Auth
            await SendAsync($"<open xmlns='urn:ietf:params:xml:ns:xmpp-framing' to='{_domain}' version='1.0'/>");
            featuresXml = await ReceiveStanzaAsync(ct);
            
            // Manchmal kommt <open> separat
            if (featuresXml.StartsWith("<open"))
            {
                featuresXml = await ReceiveStanzaAsync(ct);
            }
            
            // Bind
            if (featuresXml.Contains("<bind"))
            {
                Console.WriteLine("[*] Resource Binding...");
                FullJid = await PerformBindAsync(ct);
                Console.WriteLine($"[+] Verbunden als: {FullJid}");
            }
            
            // Session (falls nötig)
            if (featuresXml.Contains("<session"))
            {
                await PerformSessionAsync(ct);
            }
            
            // XEP Manager initialisieren
            Carbons = new CarbonManager(BareJid);
            Carbons.OnCarbonReceived += c => OnCarbonMessage?.Invoke(c);
            Carbons.OnParseError += msg => OnError?.Invoke($"[Carbon] {msg}");
            
            PubSub = new PubSubManager($"pubsub.{_domain}");
            PubSub.OnEvent += e => OnPubSubEvent?.Invoke(e);
            
            // Carbons aktivieren
            Console.WriteLine("[*] Aktiviere Message Carbons...");
            await EnableCarbonsAsync(ct);
            
            // Roster laden
            Console.WriteLine("[*] Lade Roster...");
            await RequestRosterAsync(ct);
            
            // Online gehen
            await SendPresenceAsync();
            
            SetState(ConnectionState.Connected);
            _reconnectAttempts = 0;
            Console.WriteLine("[+] Online!");
            
            // Empfangs-Loop starten
            _receiveTask = ReceiveLoopAsync(_cts.Token);
        }
        catch (AuthenticationException ex)
        {
            // Auth-Fehler sind permanent - kein Reconnect sinnvoll
            SetState(ConnectionState.Disconnected);
            OnError?.Invoke($"Authentifizierungsfehler: {ex.Message}");
            // KEIN Reconnect bei Auth-Fehlern!
        }
        catch (Exception ex)
        {
            SetState(ConnectionState.Disconnected);
            OnError?.Invoke($"Verbindungsfehler: {ex.Message}");
            
            if (!_intentionalDisconnect)
            {
                await TryReconnectAsync(ct);
            }
        }
    }

    private async Task TryReconnectAsync(CancellationToken ct)
    {
        if (_intentionalDisconnect || _reconnectAttempts >= MaxReconnectAttempts)
        {
            Console.WriteLine($"[!] Reconnect aufgegeben nach {_reconnectAttempts} Versuchen");
            return;
        }
        
        _reconnectAttempts++;
        
        // Exponential Backoff
        var delay = TimeSpan.FromMilliseconds(
            Math.Min(
                InitialReconnectDelay.TotalMilliseconds * Math.Pow(2, _reconnectAttempts - 1),
                MaxReconnectDelay.TotalMilliseconds
            )
        );
        
        SetState(ConnectionState.Reconnecting);
        Console.WriteLine($"[*] Reconnect-Versuch {_reconnectAttempts}/{MaxReconnectAttempts} in {delay.TotalSeconds:F1}s...");
        
        try
        {
            await Task.Delay(delay, ct);
            await ConnectInternalAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Abgebrochen
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Reconnect fehlgeschlagen: {ex.Message}");
        }
    }

    private void SetState(ConnectionState newState)
    {
        var oldState = State;
        if (oldState != newState)
        {
            State = newState;
            OnStateChanged?.Invoke(oldState, newState);
        }
    }

    // ===== WEBSOCKET I/O =====

    private async Task SendAsync(string xml)
    {
        if (_webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket nicht verbunden");
        
        var bytes = Encoding.UTF8.GetBytes(xml);
        await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? default);
        
        OnRawXml?.Invoke($">>> {xml}");
    }

    private async Task<string> ReceiveStanzaAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        
        WebSocketReceiveResult result;
        do
        {
            result = await _webSocket!.ReceiveAsync(buffer, ct);
            
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new IOException("WebSocket geschlossen");
            }
            
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        while (!result.EndOfMessage);
        
        var xml = sb.ToString();
        OnRawXml?.Invoke($"<<< {xml}");
        return xml;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        
        try
        {
            while (!ct.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult result;
                
                do
                {
                    result = await _webSocket.ReceiveAsync(buffer, ct);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("[!] Server hat Verbindung geschlossen");
                        break;
                    }
                    
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);
                
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                
                var stanza = sb.ToString();
                if (!string.IsNullOrEmpty(stanza))
                {
                    OnRawXml?.Invoke($"<<< {stanza}");
                    ProcessStanza(stanza);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal
        }
        catch (WebSocketException ex)
        {
            OnError?.Invoke($"WebSocket-Fehler: {ex.Message}");
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Empfangsfehler: {ex.Message}");
        }
        
        // Verbindung verloren - Reconnect versuchen
        if (!_intentionalDisconnect && State == ConnectionState.Connected)
        {
            SetState(ConnectionState.Disconnected);
            _ = TryReconnectAsync(default);
        }
    }

    // ===== STANZA PROCESSING =====

    private void ProcessStanza(string stanza)
    {
        try
        {
            if (stanza.StartsWith("<message"))
            {
                ProcessMessage(stanza);
            }
            else if (stanza.StartsWith("<presence"))
            {
                ProcessPresence(stanza);
            }
            else if (stanza.StartsWith("<iq"))
            {
                ProcessIq(stanza);
            }
            else if (stanza.StartsWith("<close"))
            {
                // Stream geschlossen
                OnError?.Invoke("Stream vom Server geschlossen");
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Stanza-Verarbeitung fehlgeschlagen: {ex.Message}");
        }
    }

    private void ProcessMessage(string stanza)
    {
        var from = ExtractAttribute(stanza, "from") ?? "unknown";
        var to = ExtractAttribute(stanza, "to") ?? FullJid;
        var msgId = ExtractAttribute(stanza, "id");
        
        // XEP-0280: Carbon Check
        if (stanza.Contains("urn:xmpp:carbons:2"))
        {
            if (Carbons != null)
            {
                var result = Carbons.ProcessCarbon(stanza, from);
                
                switch (result)
                {
                    case CarbonResult.Success:
                        return; // Carbon wurde verarbeitet
                        
                    case CarbonResult.SpoofingDetected:
                        OnSpoofingAttempt?.Invoke($"Carbon-Spoofing von {from}");
                        return;
                        
                    case CarbonResult.ParseError:
                        OnError?.Invoke($"Carbon-Parse-Fehler von {from}");
                        return;
                        
                    case CarbonResult.NotACarbon:
                        // Kein Carbon, weiter verarbeiten als normale Nachricht
                        break;
                }
            }
        }
        
        // XEP-0184: Receipt
        var receiptId = ReceiptBuilder.ExtractReceiptId(stanza);
        if (receiptId != null)
        {
            if (!Receipts.ProcessReceipt(receiptId, from))
                OnSpoofingAttempt?.Invoke($"Receipt-Spoofing: {receiptId} von {from}");
            return;
        }
        
        // XEP-0085: Chat State
        var chatState = ChatStateExtensions.ParseChatState(stanza);
        if (chatState.HasValue)
        {
            OnChatState?.Invoke(from, chatState.Value);
        }
        
        // Normale Nachricht
        var body = ExtractElement(stanza, "body");
        if (!string.IsNullOrEmpty(body))
        {
            OnMessage?.Invoke(from, to, body, msgId);
            
            // Auto-Receipt
            if (ReceiptBuilder.HasReceiptRequest(stanza) && msgId != null)
            {
                _ = SendReceiptAsync(from, msgId);
            }
        }
    }

    private void ProcessPresence(string stanza)
    {
        var from = ExtractAttribute(stanza, "from") ?? "unknown";
        var type = ExtractAttribute(stanza, "type") ?? "available";
        
        if (type == "subscribe")
        {
            Roster.RaiseSubscriptionRequest(from, ExtractElement(stanza, "status") ?? "");
        }
        else
        {
            var show = ExtractElement(stanza, "show");
            var status = ExtractElement(stanza, "status");
            Roster.UpdatePresence(from, type, show, status);
        }
        
        OnPresence?.Invoke(from, type);
    }

    private void ProcessIq(string stanza)
    {
        var type = ExtractAttribute(stanza, "type");
        var id = ExtractAttribute(stanza, "id");
        var from = ExtractAttribute(stanza, "from");
        
        // Roster-Push
        if (type == "set" && stanza.Contains("jabber:iq:roster"))
        {
            ProcessRosterPush(stanza);
            _ = SendAsync($"<iq type='result' id='{id}'/>");
        }
        
        // PubSub Event
        if (stanza.Contains("http://jabber.org/protocol/pubsub#event") && from != null)
        {
            PubSub?.ProcessEvent(stanza, from, PubSub.PubSubService);
        }
    }

    private void ProcessRosterPush(string stanza)
    {
        var match = Regex.Match(stanza, 
            @"<item\s+jid=['""]([^'""]+)['""](?:\s+name=['""]([^'""]*)['""])?(?:\s+subscription=['""]([^'""]*)['""])?");
        
        if (match.Success)
        {
            var jid = match.Groups[1].Value;
            var sub = match.Groups[3].Value;
            
            if (sub == "remove")
            {
                Roster.RemoveItem(jid);
            }
            else
            {
                var item = new RosterItem(jid)
                {
                    Name = match.Groups[2].Success ? match.Groups[2].Value : null,
                    Subscription = ParseSubscription(sub)
                };
                Roster.ProcessRosterItem(item);
            }
        }
    }

    // ===== AUTH & SETUP =====

    private async Task PerformSaslPlainAsync(CancellationToken ct)
    {
        var authData = $"\0{_username}\0{_password}";
        var authBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(authData));
        
        await SendAsync($"<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='PLAIN'>{authBase64}</auth>");
        
        var response = await ReceiveStanzaAsync(ct);
        
        if (response.Contains("<success"))
        {
            Console.WriteLine("[+] Authentifizierung erfolgreich");
        }
        else
        {
            throw new AuthenticationException($"SASL fehlgeschlagen: {response}");
        }
    }

    private async Task<string> PerformBindAsync(CancellationToken ct)
    {
        var resource = $"console-{Environment.ProcessId}";
        
        await SendAsync(
            $"<iq type='set' id='bind1'>" +
            $"<bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'>" +
            $"<resource>{resource}</resource>" +
            $"</bind></iq>");
        
        var response = await ReceiveStanzaAsync(ct);
        
        var jidMatch = Regex.Match(response, @"<jid>([^<]+)</jid>");
        return jidMatch.Success ? jidMatch.Groups[1].Value : $"{_jid}/{resource}";
    }

    private async Task PerformSessionAsync(CancellationToken ct)
    {
        await SendAsync(
            "<iq type='set' id='sess1'>" +
            "<session xmlns='urn:ietf:params:xml:ns:xmpp-session'/>" +
            "</iq>");
        
        await ReceiveStanzaAsync(ct);
    }

    private async Task EnableCarbonsAsync(CancellationToken ct)
    {
        try
        {
            await SendAsync(CarbonManager.EnableIq());
            var response = await ReceiveStanzaAsync(ct);
            
            if (response.Contains("type='result'") || response.Contains("type=\"result\""))
            {
                Carbons!.SetEnabled(true);
                Console.WriteLine("[+] Message Carbons aktiviert");
            }
            else
            {
                Console.WriteLine("[!] Message Carbons nicht verfügbar");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Carbons-Fehler: {ex.Message}");
        }
    }

    private async Task RequestRosterAsync(CancellationToken ct)
    {
        await SendAsync(
            "<iq type='get' id='roster1'>" +
            "<query xmlns='jabber:iq:roster'/>" +
            "</iq>");
        
        var response = await ReceiveStanzaAsync(ct);
        
        var items = Regex.Matches(response, @"<item\s+([^>]+?)(?:/>|>(.*?)</item>)", RegexOptions.Singleline);
        
        foreach (Match m in items)
        {
            var attrs = m.Groups[1].Value;
            var content = m.Groups[2].Success ? m.Groups[2].Value : "";
            
            var jid = ExtractAttributeValue(attrs, "jid");
            if (string.IsNullOrEmpty(jid)) continue;
            
            var item = new RosterItem(jid)
            {
                Name = ExtractAttributeValue(attrs, "name"),
                Subscription = ParseSubscription(ExtractAttributeValue(attrs, "subscription"))
            };
            
            var groups = Regex.Matches(content, @"<group>([^<]+)</group>");
            foreach (Match g in groups)
                item.Groups.Add(g.Groups[1].Value);
            
            Roster.ProcessRosterItem(item);
        }
        
        Console.WriteLine($"[+] Roster geladen: {Roster.Items.Count} Kontakte");
    }

    // ===== PUBLIC API =====

    public async Task SendPresenceAsync(string? show = null, string? status = null)
    {
        var sb = new StringBuilder("<presence>");
        if (!string.IsNullOrEmpty(show))
            sb.Append($"<show>{XmlEscape(show)}</show>");
        if (!string.IsNullOrEmpty(status))
            sb.Append($"<status>{XmlEscape(status)}</status>");
        sb.Append("</presence>");
        
        await SendAsync(sb.ToString());
    }

    public async Task<string> SendMessageAsync(string to, string body, bool requestReceipt = true)
    {
        var messageId = GenerateMessageId();
        
        var sb = new StringBuilder();
        sb.Append($"<message to='{XmlEscape(to)}' type='chat' id='{messageId}'>");
        sb.Append($"<body>{XmlEscape(body)}</body>");
        
        if (requestReceipt)
        {
            sb.Append(ReceiptBuilder.RequestXml);
            Receipts.TrackMessage(messageId, to);
        }
        
        sb.Append(ChatState.Active.ToXml());
        sb.Append("</message>");
        
        await SendAsync(sb.ToString());
        return messageId;
    }

    public async Task SendChatStateAsync(string to, ChatState state)
    {
        await SendAsync($"<message to='{XmlEscape(to)}' type='chat'>{state.ToXml()}</message>");
    }

    public async Task SendReceiptAsync(string to, string messageId)
    {
        await SendAsync(ReceiptBuilder.CreateReceipt(to, messageId));
    }

    public async Task SendRawAsync(string xml) => await SendAsync(xml);

    // Roster Operations
    public async Task AddContactAsync(string jid, string? name = null, IEnumerable<string>? groups = null)
    {
        await SendAsync(RosterStanzaBuilder.SetItem(jid, name, groups));
        await SendAsync(RosterStanzaBuilder.Subscribe(jid));
    }

    public async Task RemoveContactAsync(string jid) => await SendAsync(RosterStanzaBuilder.RemoveItem(jid));
    public async Task AcceptSubscriptionAsync(string jid) => await SendAsync(RosterStanzaBuilder.Subscribed(jid));
    public async Task DenySubscriptionAsync(string jid) => await SendAsync(RosterStanzaBuilder.Unsubscribed(jid));

    // PubSub Operations
    public async Task PubSubSubscribeAsync(string nodeId, string? service = null)
    {
        await SendAsync(PubSubBuilder.Subscribe(service ?? PubSub!.PubSubService, nodeId, BareJid));
        PubSub!.AddSubscription(nodeId);
    }

    public async Task PubSubUnsubscribeAsync(string nodeId, string? service = null)
    {
        await SendAsync(PubSubBuilder.Unsubscribe(service ?? PubSub!.PubSubService, nodeId, BareJid));
        PubSub!.RemoveSubscription(nodeId);
    }

    public async Task PubSubPublishAsync(string nodeId, string itemId, string payload, string? service = null)
    {
        await SendAsync(PubSubBuilder.Publish(service ?? PubSub!.PubSubService, nodeId, itemId, payload));
    }

    public async Task PubSubCreateNodeAsync(string nodeId, string? service = null)
    {
        await SendAsync(PubSubBuilder.CreateNode(service ?? PubSub!.PubSubService, nodeId));
    }

    public async Task PubSubDeleteNodeAsync(string nodeId, string? service = null)
    {
        await SendAsync(PubSubBuilder.DeleteNode(service ?? PubSub!.PubSubService, nodeId));
    }

    public async Task PubSubGetItemsAsync(string nodeId, int? maxItems = null, string? service = null)
    {
        await SendAsync(PubSubBuilder.GetItems(service ?? PubSub!.PubSubService, nodeId, maxItems));
    }

    // ===== HELPERS =====

    private string GenerateMessageId() => $"msg-{Interlocked.Increment(ref _messageIdCounter)}-{Guid.NewGuid():N}";

    private static string? ExtractAttribute(string xml, string name)
    {
        var match = Regex.Match(xml, $@"{name}=['""]([^'""]*)['""]");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractAttributeValue(string attrs, string name)
    {
        var match = Regex.Match(attrs, $@"{name}\s*=\s*['""]([^'""]*)['""]", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractElement(string xml, string name)
    {
        var match = Regex.Match(xml, $@"<{name}>([^<]*)</{name}>");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static List<string> ExtractSaslMechanisms(string xml)
    {
        var mechanisms = new List<string>();
        
        // Finde alle <mechanism>...</mechanism> Elemente
        var matches = Regex.Matches(xml, @"<mechanism>([^<]+)</mechanism>");
        
        foreach (Match match in matches)
        {
            mechanisms.Add(match.Groups[1].Value);
        }
        
        return mechanisms;
    }

    private static string GetBareJid(string jid)
    {
        var slash = jid.IndexOf('/');
        return (slash > 0 ? jid[..slash] : jid).ToLowerInvariant();
    }

    private static SubscriptionState ParseSubscription(string? sub) => sub switch
    {
        "to" => SubscriptionState.To,
        "from" => SubscriptionState.From,
        "both" => SubscriptionState.Both,
        "remove" => SubscriptionState.Remove,
        _ => SubscriptionState.None
    };

    private static string XmlEscape(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("'", "&apos;")
            .Replace("\"", "&quot;");

    public async Task DisconnectAsync()
    {
        _intentionalDisconnect = true;
        _cts?.Cancel();
        
        try
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                await SendAsync("<close xmlns='urn:ietf:params:xml:ns:xmpp-framing'/>");
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Goodbye", default);
            }
        }
        catch { }
        
        SetState(ConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        
        _webSocket?.Dispose();
        _cts?.Dispose();
    }
}

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}
