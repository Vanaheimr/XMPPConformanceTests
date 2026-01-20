using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;

namespace XmppClient;

/// <summary>
/// XMPP over WebSocket (RFC 7395) mit Auto-Reconnect
/// 
/// Features:
/// - SCRAM-SHA-1/256 und SASL PLAIN Authentifizierung
/// - XEP-0198 Stream Management
/// - XEP-0199 Ping
/// - XEP-0030 Service Discovery
/// - XEP-0115 Entity Capabilities
/// - XEP-0333 Chat Markers
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
    private Task? _keepaliveTask;
    
    private int _messageIdCounter;
    private int _reconnectAttempts;
    private bool _intentionalDisconnect;
    
    // Reconnect-Einstellungen
    public int MaxReconnectAttempts { get; set; } = 5;
    public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);
    
    // Keepalive - verhindert Inactivity Timeout vom Server
    public TimeSpan KeepaliveInterval { get; set; } = TimeSpan.FromSeconds(25);
    public bool KeepaliveEnabled { get; set; } = true;
    
    // XEP-0198: Stream Management - DEAKTIVIERT wegen ejabberd-Kompatibilitätsproblemen
    // Kann mit /sm on aktiviert werden (experimentell)
    public bool StreamManagementEnabled { get; set; } = false;
    
    // State
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string FullJid { get; private set; } = string.Empty;
    public string BareJid => GetBareJid(FullJid);
    public List<string> ServerFeatures { get; } = [];
    
    // Core Managers
    public Roster Roster { get; } = new();
    public ReceiptTracker Receipts { get; } = new();
    public CarbonManager? Carbons { get; private set; }
    public PubSubManager? PubSub { get; private set; }
    
    // Advanced Managers (XEP-0030, 0115, 0198, 0199)
    public PingManager? Ping { get; private set; }
    public DiscoManager? Disco { get; private set; }
    public EntityCapsManager? EntityCaps { get; private set; }
    public StreamManagementManager? StreamManagement { get; private set; }
    
    // Events - Core
    public event Action<string, string, string, string?>? OnMessage;  // from, to, body, id
    public event Action<string, string>? OnPresence;
    public event Action<string, ChatState>? OnChatState;
    public event Action<string, string>? OnReceiptReceived;
    public event Action<CarbonMessage>? OnCarbonMessage;
    public event Action<PubSubEvent>? OnPubSubEvent;
    public event Action<string>? OnRawXml;
    public event Action<string>? OnError;
    public event Action<string>? OnSpoofingAttempt;
    public event Action<ConnectionState, ConnectionState>? OnStateChanged;
    
    // Events - Advanced
    public event Action<ChatMarker>? OnChatMarker;
    public event Action<string, DiscoInfo>? OnCapsDiscovered;

    /// <summary>
    /// Erstellt eine neue WebSocket-basierte XMPP-Verbindung
    /// </summary>
    public XmppConnection(string jid, string password, string? wsUri = null)
    {
        _jid = jid;
        _password = password;
        
        var parts = jid.Split('@');
        if (parts.Length != 2)
            throw new ArgumentException("JID muss im Format 'user@domain' sein", nameof(jid));
        
        _username = parts[0];
        _domain = parts[1];
        
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
            
            // SASL Auth - Präferenz: SCRAM-SHA-256 > SCRAM-SHA-1 > PLAIN
            if (mechanisms.Contains("SCRAM-SHA-256"))
            {
                Console.WriteLine("[*] SCRAM-SHA-256 Authentifizierung...");
                await PerformScramAsync(ScramAuthenticator.ScramMechanism.ScramSha256, ct);
            }
            else if (mechanisms.Contains("SCRAM-SHA-1"))
            {
                Console.WriteLine("[*] SCRAM-SHA-1 Authentifizierung...");
                await PerformScramAsync(ScramAuthenticator.ScramMechanism.ScramSha1, ct);
            }
            else if (mechanisms.Contains("PLAIN"))
            {
                Console.WriteLine("[*] SASL PLAIN Authentifizierung...");
                await PerformSaslPlainAsync(ct);
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
            
            // Server-Features speichern
            ServerFeatures.Clear();
            foreach (Match m in Regex.Matches(featuresXml, @"<(\w+)\s+xmlns=['""]([^'""]+)['""]"))
            {
                ServerFeatures.Add(m.Groups[2].Value);
            }
            
            // Bind
            if (featuresXml.Contains("<bind"))
            {
                Console.WriteLine("[*] Resource Binding...");
                FullJid = await PerformBindAsync(ct);
                Console.WriteLine($"[+] Verbunden als: {FullJid}");
            }
            
            // Session (falls nötig - deprecated in RFC 6121)
            if (featuresXml.Contains("<session") && !featuresXml.Contains("optional"))
            {
                await PerformSessionAsync(ct);
            }
            
            // XEP-0198: Stream Management (optional - deaktiviert wegen ejabberd-Problemen)
            StreamManagement = new StreamManagementManager(SendAsync);
            StreamManagement.OnAckReceived += count => 
                Console.WriteLine($"[SM] {count} Stanzas bestätigt");
            
            if (StreamManagementEnabled && featuresXml.Contains("urn:xmpp:sm:3"))
            {
                Console.WriteLine("[*] Aktiviere Stream Management...");
                await StreamManagement.EnableAsync(requestResume: false);
                
                // Warte auf <enabled> oder <failed>
                for (int i = 0; i < 10; i++)
                {
                    var smResponse = await ReceiveStanzaAsync(ct);
                    
                    if (smResponse.Contains("<enabled") && smResponse.Contains("urn:xmpp:sm:3"))
                    {
                        StreamManagement.ProcessEnabled(smResponse);
                        break;
                    }
                    else if (smResponse.Contains("<failed") && smResponse.Contains("urn:xmpp:sm"))
                    {
                        Console.WriteLine("[!] Stream Management vom Server abgelehnt");
                        break;
                    }
                    else
                    {
                        Console.WriteLine($"[*] SM: Überspringe Stanza: {smResponse[..Math.Min(60, smResponse.Length)]}...");
                    }
                }
                
                if (StreamManagement.IsEnabled)
                {
                    Console.WriteLine($"[+] Stream Management aktiviert");
                }
            }
            
            // Manager initialisieren
            Carbons = new CarbonManager(BareJid);
            Carbons.OnCarbonReceived += c => OnCarbonMessage?.Invoke(c);
            Carbons.OnParseError += msg => OnError?.Invoke($"[Carbon] {msg}");
            
            PubSub = new PubSubManager($"pubsub.{_domain}");
            PubSub.OnEvent += e => OnPubSubEvent?.Invoke(e);
            
            // XEP-0199: Ping Manager
            Ping = new PingManager(SendAsync);
            Ping.OnPingTimeout += target => OnError?.Invoke($"Ping Timeout: {target}");
            
            // XEP-0030: Service Discovery
            Disco = new DiscoManager(SendAsync);
            
            // XEP-0115: Entity Capabilities
            EntityCaps = new EntityCapsManager(Disco);
            EntityCaps.OnCapsDiscovered += (from, info) => OnCapsDiscovered?.Invoke(from, info);
            
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
            
            // Keepalive-Loop starten (verhindert Server-Timeout)
            if (KeepaliveEnabled)
            {
                Console.WriteLine($"[*] Starte Keepalive (Interval: {KeepaliveInterval.TotalSeconds}s)...");
                _keepaliveTask = KeepaliveLoopAsync(_cts.Token);
            }
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

    private async Task KeepaliveLoopAsync(CancellationToken ct)
    {
        Console.WriteLine($"[Keepalive] Loop gestartet (Interval: {KeepaliveInterval.TotalSeconds}s)");
        
        try
        {
            while (!ct.IsCancellationRequested && State == ConnectionState.Connected)
            {
                await Task.Delay(KeepaliveInterval, ct);
                
                if (State != ConnectionState.Connected)
                {
                    Console.WriteLine("[Keepalive] Abbruch - nicht mehr verbunden");
                    break;
                }
                
                // Bevorzugt: Stream Management <r/> (weniger Overhead)
                if (StreamManagement?.IsEnabled == true)
                {
                    Console.WriteLine("[Keepalive] Sende SM <r/>...");
                    await StreamManagement.RequestAckAsync();
                }
                // Fallback: XEP-0199 Ping
                else if (Ping != null)
                {
                    Console.WriteLine("[Keepalive] Sende Ping...");
                    var rtt = await Ping.PingAsync(ct: ct);
                    if (rtt.HasValue)
                        Console.WriteLine($"[Keepalive] Pong: {rtt.Value.TotalMilliseconds:F0}ms");
                    else
                        Console.WriteLine("[Keepalive] Ping Timeout!");
                }
                else
                {
                    Console.WriteLine("[Keepalive] Weder SM noch Ping verfügbar!");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Keepalive] Loop beendet (cancelled)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Keepalive] Fehler: {ex.Message}");
            OnError?.Invoke($"Keepalive-Fehler: {ex.Message}");
        }
    }

    // ===== STANZA PROCESSING =====

    private void ProcessStanza(string stanza)
    {
        try
        {
            // XEP-0198: Stream Management Tracking
            if (StreamManagement?.IsEnabled == true && 
                (stanza.StartsWith("<message") || stanza.StartsWith("<presence") || stanza.StartsWith("<iq")))
            {
                StreamManagement.TrackIncoming();
            }
            
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
                OnError?.Invoke("Stream vom Server geschlossen");
            }
            // XEP-0198: Stream Management
            else if (stanza.StartsWith("<a") && stanza.Contains("urn:xmpp:sm:3"))
            {
                StreamManagement?.ProcessAck(stanza);
            }
            else if (stanza.StartsWith("<r") && stanza.Contains("urn:xmpp:sm:3"))
            {
                _ = StreamManagement?.ProcessRequestAsync();
            }
            else if (stanza.StartsWith("<resumed"))
            {
                StreamManagement?.ProcessResumed(stanza);
            }
            else if (stanza.StartsWith("<failed") && stanza.Contains("urn:xmpp:sm"))
            {
                StreamManagement?.ProcessFailed();
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
        
        // XEP-0333: Chat Markers
        var chatMarker = ChatMarkers.Parse(stanza, from);
        if (chatMarker != null)
        {
            OnChatMarker?.Invoke(chatMarker);
            return;
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
            
            // Auto-Receipt (XEP-0184)
            if (ReceiptBuilder.HasReceiptRequest(stanza) && msgId != null)
            {
                _ = SendReceiptAsync(from, msgId);
            }
            
            // Auto-Received Marker (XEP-0333)
            if (ChatMarkers.IsMarkable(stanza) && msgId != null)
            {
                _ = SendChatMarkerAsync(from, msgId, ChatMarkerType.Received);
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
            
            // XEP-0115: Entity Capabilities
            // WICHTIG: Eigene Presences überspringen - wir kennen unsere Caps bereits!
            // Sonst Query-Loop an uns selbst → Server-Error → Disconnect
            var fromBareJid = GetBareJid(from);
            var isOwnPresence = fromBareJid.Equals(BareJid, StringComparison.OrdinalIgnoreCase);
            
            if (!isOwnPresence && (type == "available" || string.IsNullOrEmpty(type)))
            {
                var caps = EntityCapsManager.ParseCaps(stanza);
                if (caps.HasValue && EntityCaps != null)
                {
                    // Query an Full-JID (für korrekte Resource), nicht Bare-JID
                    // Server routet die Antwort korrekt
                    _ = EntityCaps.ProcessCapsAsync(from, caps.Value.Node, caps.Value.Ver);
                }
            }
        }
        
        OnPresence?.Invoke(from, type);
    }

    private void ProcessIq(string stanza)
    {
        var type = ExtractAttribute(stanza, "type");
        var id = ExtractAttribute(stanza, "id");
        var from = ExtractAttribute(stanza, "from");
        
        // IQ Result/Error für pending queries
        if (type == "result" || type == "error")
        {
            if (id != null)
            {
                // XEP-0199: Ping Antwort
                if (id.StartsWith("ping-"))
                {
                    Ping?.ProcessPong(id);
                    return;
                }
                
                // XEP-0030: Disco Info Antwort
                if (id.StartsWith("disco-info-") && from != null)
                {
                    Disco?.ProcessInfoResult(id, stanza, from);
                    return;
                }
                
                // XEP-0030: Disco Items Antwort
                if (id.StartsWith("disco-items-") && from != null)
                {
                    Disco?.ProcessItemsResult(id, stanza, from);
                    return;
                }
            }
        }
        
        // IQ Get - Anfragen
        if (type == "get" && id != null && from != null)
        {
            // XEP-0199: Ping Anfrage
            if (PingManager.IsPing(stanza))
            {
                _ = Ping?.RespondAsync(id, from);
                return;
            }
            
            // XEP-0030: Disco Info Anfrage
            if (stanza.Contains("http://jabber.org/protocol/disco#info"))
            {
                _ = Disco?.RespondInfoAsync(id, from);
                return;
            }
        }
        
        // IQ Set
        if (type == "set")
        {
            // Roster-Push
            if (stanza.Contains("jabber:iq:roster"))
            {
                ProcessRosterPush(stanza);
                _ = SendAsync($"<iq type='result' id='{id}'/>");
                return;
            }
        }
        
        // PubSub Event (kann als message oder iq kommen)
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

    private async Task PerformScramAsync(ScramAuthenticator.ScramMechanism mechanism, CancellationToken ct)
    {
        var scram = new ScramAuthenticator(_username, _password, mechanism);
        
        // Schritt 1: client-first-message
        var clientFirst = scram.CreateClientFirstMessage();
        await SendAsync($"<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='{scram.MechanismName}'>{clientFirst}</auth>");
        
        // Schritt 2: server-first-message (challenge)
        var challengeResponse = await ReceiveStanzaAsync(ct);
        
        if (!challengeResponse.Contains("<challenge"))
        {
            if (challengeResponse.Contains("<failure"))
            {
                throw new AuthenticationException($"SCRAM fehlgeschlagen: {challengeResponse}");
            }
            throw new AuthenticationException($"Unerwartete Antwort: {challengeResponse}");
        }
        
        // Base64-Inhalt extrahieren
        var challengeMatch = Regex.Match(challengeResponse, @"<challenge[^>]*>([^<]+)</challenge>");
        if (!challengeMatch.Success)
            throw new AuthenticationException("Konnte Challenge nicht parsen");
        
        var serverFirst = challengeMatch.Groups[1].Value;
        
        // Schritt 3: client-final-message
        var clientFinal = scram.ProcessServerFirstMessage(serverFirst);
        await SendAsync($"<response xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>{clientFinal}</response>");
        
        // Schritt 4: server-final-message (success oder failure)
        var finalResponse = await ReceiveStanzaAsync(ct);
        
        if (finalResponse.Contains("<success"))
        {
            // Optional: Server-Signatur verifizieren
            var successMatch = Regex.Match(finalResponse, @"<success[^>]*>([^<]*)</success>");
            if (successMatch.Success && !string.IsNullOrEmpty(successMatch.Groups[1].Value))
            {
                if (!scram.VerifyServerFinalMessage(successMatch.Groups[1].Value))
                {
                    throw new AuthenticationException("Server-Signatur ungültig - möglicher MITM-Angriff!");
                }
            }
            Console.WriteLine("[+] Authentifizierung erfolgreich");
        }
        else if (finalResponse.Contains("<failure"))
        {
            var errorMatch = Regex.Match(finalResponse, @"<(\w+)\s+xmlns=['""]urn:ietf:params:xml:ns:xmpp-sasl['""]");
            var error = errorMatch.Success ? errorMatch.Groups[1].Value : finalResponse;
            throw new AuthenticationException($"SCRAM fehlgeschlagen: {error}");
        }
        else
        {
            throw new AuthenticationException($"Unerwartete Antwort: {finalResponse}");
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
            
            // Warte auf IQ-Antwort mit id='carbons-enable' - ignoriere andere Stanzas
            for (int i = 0; i < 10; i++)
            {
                var response = await ReceiveStanzaAsync(ct);
                
                if (response.Contains("id='carbons-enable'") || response.Contains("id=\"carbons-enable\""))
                {
                    if (response.Contains("type='result'") || response.Contains("type=\"result\""))
                    {
                        Carbons!.SetEnabled(true);
                        Console.WriteLine("[+] Message Carbons aktiviert");
                    }
                    else
                    {
                        Console.WriteLine("[!] Message Carbons nicht verfügbar (Server-Fehler)");
                    }
                    break;
                }
                else
                {
                    // Andere Stanza - ignorieren und weiter warten
                    Console.WriteLine($"[*] Carbons: Überspringe Stanza: {response[..Math.Min(80, response.Length)]}...");
                }
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
        
        // Warte auf Roster-Antwort - ignoriere andere Stanzas
        string response = "";
        for (int i = 0; i < 10; i++)
        {
            response = await ReceiveStanzaAsync(ct);
            
            if (response.Contains("id='roster1'") || response.Contains("id=\"roster1\""))
            {
                break;
            }
            else
            {
                // Andere Stanza - ignorieren
                Console.WriteLine($"[*] Roster: Überspringe Stanza: {response[..Math.Min(80, response.Length)]}...");
            }
        }
        
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
        
        // XEP-0115: Entity Capabilities
        if (EntityCaps != null)
        {
            sb.Append(EntityCaps.GetCapsElement());
        }
        
        sb.Append("</presence>");
        
        await SendAsync(sb.ToString());
    }

    public async Task<string> SendMessageAsync(string to, string body, bool requestReceipt = true, bool markable = true)
    {
        var messageId = GenerateMessageId();
        
        var sb = new StringBuilder();
        sb.Append($"<message to='{XmlEscape(to)}' type='chat' id='{messageId}'>");
        sb.Append($"<body>{XmlEscape(body)}</body>");
        
        // XEP-0184: Receipt Request
        if (requestReceipt)
        {
            sb.Append(ReceiptBuilder.RequestXml);
            Receipts.TrackMessage(messageId, to);
        }
        
        // XEP-0333: Chat Markers - markable
        if (markable)
        {
            sb.Append(ChatMarkers.Markable);
        }
        
        // XEP-0085: Chat State
        sb.Append(ChatState.Active.ToXml());
        sb.Append("</message>");
        
        var xml = sb.ToString();
        
        // XEP-0198: Track outgoing
        StreamManagement?.TrackOutgoing(xml);
        
        await SendAsync(xml);
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

    /// <summary>
    /// XEP-0333: Sendet einen Chat Marker
    /// </summary>
    public async Task SendChatMarkerAsync(string to, string refMessageId, ChatMarkerType type)
    {
        await SendAsync(ChatMarkers.CreateMarker(to, refMessageId, type));
    }

    /// <summary>
    /// XEP-0199: Sendet einen Ping
    /// </summary>
    public Task<TimeSpan?> PingAsync(string? to = null, CancellationToken ct = default)
    {
        return Ping?.PingAsync(to, ct) ?? Task.FromResult<TimeSpan?>(null);
    }

    /// <summary>
    /// XEP-0030: Fragt Service Discovery Info ab
    /// </summary>
    public Task<DiscoInfo?> DiscoverInfoAsync(string jid, CancellationToken ct = default)
    {
        return Disco?.QueryInfoAsync(jid, ct: ct) ?? Task.FromResult<DiscoInfo?>(null);
    }

    /// <summary>
    /// XEP-0030: Fragt Service Discovery Items ab
    /// </summary>
    public Task<DiscoItems?> DiscoverItemsAsync(string jid, CancellationToken ct = default)
    {
        return Disco?.QueryItemsAsync(jid, ct: ct) ?? Task.FromResult<DiscoItems?>(null);
    }

    /// <summary>
    /// XEP-0198: Fordert Ack vom Server an
    /// </summary>
    public Task RequestAckAsync()
    {
        return StreamManagement?.RequestAckAsync() ?? Task.CompletedTask;
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
