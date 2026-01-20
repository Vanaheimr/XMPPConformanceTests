namespace XmppClient;

class Program
{
    private static XmppConnection? _connection;
    private static string? _currentRecipient;
    private static bool _showRawXml = false;
    private static bool _running = true;
    private static readonly List<string> _pendingSubscriptions = [];

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "XMPP Console Client";
        
        PrintHeader();
        
        var (jid, password, wsUri) = GetCredentials(args);
        
        if (string.IsNullOrEmpty(jid) || string.IsNullOrEmpty(password))
        {
            Console.WriteLine("Fehler: JID und Passwort erforderlich");
            return;
        }
        
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _running = false;
            cts.Cancel();
        };
        
        try
        {
            _connection = new XmppConnection(jid, password, wsUri);
            
            // Core Event-Handler
            _connection.OnMessage += HandleMessage;
            _connection.OnPresence += HandlePresence;
            _connection.OnError += HandleError;
            _connection.OnRawXml += HandleRawXml;
            
            // Connection State (für Reconnect-Anzeige)
            _connection.OnStateChanged += (oldState, newState) =>
            {
                switch (newState)
                {
                    case ConnectionState.Reconnecting:
                        WriteSystemMessage("🔄 Verbindung verloren, versuche Reconnect...");
                        break;
                    case ConnectionState.Connected when oldState == ConnectionState.Reconnecting:
                        WriteSystemMessage("✅ Reconnect erfolgreich!");
                        break;
                    case ConnectionState.Disconnected when oldState == ConnectionState.Reconnecting:
                        WriteWarning("❌ Reconnect fehlgeschlagen");
                        break;
                }
            };
            
            // XEP-0085: Chat State
            _connection.OnChatState += HandleChatState;
            
            // XEP-0184: Receipts
            _connection.OnReceiptReceived += HandleReceipt;
            
            // XEP-0280: Carbons
            _connection.OnCarbonMessage += HandleCarbon;
            
            // XEP-0060: PubSub
            _connection.OnPubSubEvent += HandlePubSubEvent;
            
            // XEP-0333: Chat Markers
            _connection.OnChatMarker += HandleChatMarker;
            
            // XEP-0115: Entity Caps
            _connection.OnCapsDiscovered += (from, info) =>
            {
                if (_showRawXml)
                {
                    WriteSystemMessage($"[Caps] {from}: {string.Join(", ", info.Identities)}");
                }
            };
            
            // Security
            _connection.OnSpoofingAttempt += msg => 
                WriteWarning($"⚠️ SPOOFING: {msg}");
            
            // Roster-Events
            _connection.Roster.OnItemAdded += item => 
                WriteSystemMessage($"Kontakt hinzugefügt: {item.DisplayName}");
            _connection.Roster.OnItemRemoved += jid => 
                WriteSystemMessage($"Kontakt entfernt: {jid}");
            _connection.Roster.OnSubscriptionRequest += (from, status) =>
            {
                _pendingSubscriptions.Add(from);
                WriteSystemMessage($"📩 Kontaktanfrage von {from}: {status}");
                WriteSystemMessage($"   Nutze /accept {from} oder /deny {from}");
            };
            
            // Verbinden (WebSocket + Receive-Loop starten automatisch)
            await _connection.ConnectAsync(cts.Token);
            
            PrintHelp();
            
            // Konsolen-Input verarbeiten (blockiert bis Ctrl+C)
            await ProcessConsoleInputAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n[*] Beendet.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[!] Fehler: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"    Inner: {ex.InnerException.Message}");
        }
        finally
        {
            if (_connection != null)
            {
                await _connection.DisposeAsync();
            }
        }
    }

    private static (string jid, string password, string? wsUri) GetCredentials(string[] args)
    {
        string? jid = null;
        string? password = null;
        string? wsUri = null;
        
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-j" or "--jid" when i + 1 < args.Length:
                    jid = args[++i];
                    break;
                case "-p" or "--password" when i + 1 < args.Length:
                    password = args[++i];
                    break;
                case "-w" or "--ws" or "--websocket" when i + 1 < args.Length:
                    wsUri = args[++i];
                    break;
                case "-h" or "--help":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
            }
        }
        
        if (string.IsNullOrEmpty(jid))
        {
            Console.Write("JID (user@domain): ");
            jid = Console.ReadLine()?.Trim() ?? "";
        }
        
        if (string.IsNullOrEmpty(password))
        {
            Console.Write("Passwort: ");
            password = ReadPassword();
            Console.WriteLine();
        }
        
        if (string.IsNullOrEmpty(wsUri))
        {
            Console.Write($"WebSocket URI (Enter für wss://{{domain}}:5443/ws): ");
            var input = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(input))
                wsUri = input;
        }
        
        return (jid, password, wsUri);
    }

    private static string ReadPassword()
    {
        var password = new System.Text.StringBuilder();
        
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            
            if (key.Key == ConsoleKey.Enter)
                break;
            
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
        }
        
        return password.ToString();
    }

    private static async Task ProcessConsoleInputAsync(CancellationToken ct)
    {
        while (_running && !ct.IsCancellationRequested)
        {
            var prompt = _currentRecipient != null 
                ? $"[{GetShortJid(_currentRecipient)}] > " 
                : "> ";
            
            Console.Write(prompt);
            
            string? input;
            try
            {
                input = await Task.Run(Console.ReadLine, ct);
                input = input?.Trim();  // Führende/abschließende Leerzeichen entfernen
            }
            catch (OperationCanceledException)
            {
                break;
            }
            
            if (string.IsNullOrEmpty(input))
                continue;
            
            if (input.StartsWith('/'))
            {
                await ProcessCommandAsync(input, ct);
            }
            else if (_currentRecipient != null)
            {
                await _connection!.SendMessageAsync(_currentRecipient, input);
                Console.WriteLine($"  → Gesendet an {GetShortJid(_currentRecipient)}");
            }
            else
            {
                Console.WriteLine("Kein Empfänger gesetzt. Nutze /msg <jid> <nachricht> oder /to <jid>");
            }
        }
    }

    private static async Task ProcessCommandAsync(string input, CancellationToken ct)
    {
        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLower();
        var args = parts.Length > 1 ? parts[1] : "";
        
        switch (command)
        {
            case "/help" or "/h" or "/?":
                PrintHelp();
                break;
                
            case "/quit" or "/q" or "/exit":
                _running = false;
                break;
                
            case "/to" or "/chat":
                if (string.IsNullOrEmpty(args))
                {
                    _currentRecipient = null;
                    Console.WriteLine("Chat-Empfänger zurückgesetzt");
                }
                else
                {
                    _currentRecipient = args.Trim();
                    Console.WriteLine($"Chat mit: {_currentRecipient}");
                }
                break;
                
            case "/msg" or "/m":
                var msgParts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (msgParts.Length < 2)
                {
                    Console.WriteLine("Syntax: /msg <jid> <nachricht>");
                }
                else
                {
                    await _connection!.SendMessageAsync(msgParts[0], msgParts[1]);
                    Console.WriteLine($"  → Gesendet an {GetShortJid(msgParts[0])}");
                }
                break;
                
            case "/status" or "/s":
                var statusParts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var show = statusParts.Length > 0 ? statusParts[0] : null;
                var statusText = statusParts.Length > 1 ? statusParts[1] : null;
                
                if (show is "available" or "away" or "chat" or "dnd" or "xa" || string.IsNullOrEmpty(show))
                {
                    await _connection!.SendPresenceAsync(show, statusText);
                    Console.WriteLine($"Status: {show ?? "available"} {statusText ?? ""}");
                }
                else
                {
                    Console.WriteLine("Status muss sein: available, away, chat, dnd, xa");
                }
                break;
                
            // === ROSTER-BEFEHLE ===
                
            case "/roster" or "/list" or "/contacts":
                PrintRoster(args);
                break;
                
            case "/online":
                PrintOnlineContacts();
                break;
                
            case "/add":
                await AddContactAsync(args);
                break;
                
            case "/remove" or "/del":
                await RemoveContactAsync(args);
                break;
                
            case "/accept":
                await AcceptSubscriptionAsync(args);
                break;
                
            case "/deny":
                await DenySubscriptionAsync(args);
                break;
                
            case "/info":
                ShowContactInfo(args);
                break;
                
            case "/groups":
                PrintGroups();
                break;
                
            case "/pending":
                PrintPendingSubscriptions();
                break;
                
            // === XEP-0085: CHAT STATE ===
                
            case "/typing":
                if (_currentRecipient == null)
                {
                    Console.WriteLine("Kein Empfänger gesetzt. Nutze /to <jid>");
                }
                else
                {
                    await _connection!.SendChatStateAsync(_currentRecipient, ChatState.Composing);
                    Console.WriteLine($"⌨️ Typing-Indicator gesendet an {GetShortJid(_currentRecipient)}");
                }
                break;
                
            case "/paused":
                if (_currentRecipient == null)
                {
                    Console.WriteLine("Kein Empfänger gesetzt. Nutze /to <jid>");
                }
                else
                {
                    await _connection!.SendChatStateAsync(_currentRecipient, ChatState.Paused);
                }
                break;
                
            case "/gone":
                if (_currentRecipient != null)
                {
                    await _connection!.SendChatStateAsync(_currentRecipient, ChatState.Gone);
                    _currentRecipient = null;
                    Console.WriteLine("Chat beendet");
                }
                break;
                
            // === XEP-0060: PUBSUB ===
                
            case "/pubsub":
                await ProcessPubSubCommandAsync(args);
                break;
                
            // === SONSTIGE ===
                
            case "/carbons":
                if (_connection!.Carbons?.IsEnabled == true)
                {
                    Console.WriteLine("✓ Message Carbons sind AKTIVIERT");
                }
                else
                {
                    Console.WriteLine("✗ Message Carbons sind NICHT aktiviert");
                }
                break;
                
            case "/raw":
                _showRawXml = !_showRawXml;
                Console.WriteLine($"Raw XML Anzeige: {(_showRawXml ? "AN" : "AUS")}");
                break;
                
            case "/who":
                Console.WriteLine($"Angemeldet als: {_connection!.FullJid}");
                Console.WriteLine($"Bare JID: {_connection.BareJid}");
                Console.WriteLine($"Status: {_connection.State}");
                if (_currentRecipient != null)
                    Console.WriteLine($"Chat mit: {_currentRecipient}");
                Console.WriteLine($"Carbons: {(_connection.Carbons?.IsEnabled == true ? "aktiviert" : "deaktiviert")}");
                Console.WriteLine($"Stream Mgmt: {(_connection.StreamManagement?.IsEnabled == true ? "aktiviert" : "deaktiviert")}");
                Console.WriteLine($"Keepalive: {(_connection.KeepaliveEnabled ? $"alle {_connection.KeepaliveInterval.TotalSeconds}s" : "deaktiviert")}");
                Console.WriteLine($"Transport: WebSocket (RFC 7395)");
                break;
                
            case "/ping":
                await ProcessPingCommandAsync(args, ct);
                break;
                
            case "/disco":
                await ProcessDiscoCommandAsync(args, ct);
                break;
                
            case "/features":
                Console.WriteLine("Server-Features:");
                foreach (var feature in _connection!.ServerFeatures)
                {
                    Console.WriteLine($"  {feature}");
                }
                Console.WriteLine($"\nLokal unterstützte Features:");
                foreach (var feature in _connection.Disco?.LocalFeatures ?? [])
                {
                    Console.WriteLine($"  {feature}");
                }
                break;
                
            case "/mark":
                if (string.IsNullOrEmpty(args))
                {
                    Console.WriteLine("Verwendung: /mark <received|displayed|ack> <message-id>");
                    Console.WriteLine("            /mark displayed (für letzte Nachricht an aktuellen Empfänger)");
                }
                else
                {
                    await ProcessMarkCommandAsync(args);
                }
                break;
                
            case "/sm":
                Console.WriteLine($"Stream Management:");
                Console.WriteLine($"  Konfiguriert: {_connection!.StreamManagementEnabled}");
                Console.WriteLine($"  Aktiv: {_connection.StreamManagement?.IsEnabled == true}");
                
                if (_connection.StreamManagement?.IsEnabled == true)
                {
                    var sm = _connection.StreamManagement;
                    Console.WriteLine($"  Eingehend: {sm.InboundCount}");
                    Console.WriteLine($"  Ausgehend: {sm.OutboundCount}");
                    Console.WriteLine($"  Unbestätigt: {sm.UnackedCount}");
                    
                    Console.WriteLine("[*] Fordere Ack an...");
                    await _connection.RequestAckAsync();
                }
                
                if (!string.IsNullOrEmpty(args))
                {
                    if (args.ToLower() == "on")
                    {
                        _connection.StreamManagementEnabled = true;
                        Console.WriteLine("[*] SM aktiviert (wirkt nach Reconnect)");
                        Console.WriteLine("[!] WARNUNG: SM kann bei einigen Servern (ejabberd) zu Disconnects führen!");
                    }
                    else if (args.ToLower() == "off")
                    {
                        _connection.StreamManagementEnabled = false;
                        Console.WriteLine("[*] SM deaktiviert (wirkt nach Reconnect)");
                    }
                }
                break;
                
            case "/keepalive":
                Console.WriteLine($"Keepalive Status:");
                Console.WriteLine($"  Aktiviert: {_connection!.KeepaliveEnabled}");
                Console.WriteLine($"  Interval: {_connection.KeepaliveInterval.TotalSeconds}s");
                Console.WriteLine($"  Methode: {(_connection.StreamManagement?.IsEnabled == true ? "Stream Management <r/>" : "XEP-0199 Ping")}");
                
                if (!string.IsNullOrEmpty(args))
                {
                    if (args.ToLower() == "off" || args == "0")
                    {
                        _connection.KeepaliveEnabled = false;
                        Console.WriteLine("[*] Keepalive deaktiviert");
                    }
                    else if (args.ToLower() == "on" || args == "1")
                    {
                        _connection.KeepaliveEnabled = true;
                        Console.WriteLine("[*] Keepalive aktiviert (wirkt nach Reconnect)");
                    }
                    else if (int.TryParse(args, out var seconds) && seconds > 0)
                    {
                        _connection.KeepaliveInterval = TimeSpan.FromSeconds(seconds);
                        Console.WriteLine($"[*] Keepalive-Interval auf {seconds}s gesetzt (wirkt nach Reconnect)");
                    }
                }
                break;
                
            case "/reconnect":
                if (_connection!.State == ConnectionState.Connected)
                {
                    Console.WriteLine("[*] Bereits verbunden. Trenne erst mit /disconnect");
                }
                else
                {
                    Console.WriteLine("[*] Manueller Reconnect...");
                    await _connection.ConnectAsync(ct);
                }
                break;
                
            case "/disconnect":
                Console.WriteLine("[*] Trenne Verbindung...");
                await _connection!.DisconnectAsync();
                Console.WriteLine("[+] Getrennt");
                break;
                
            default:
                Console.WriteLine($"Unbekannter Befehl: {command}. Tippe /help für Hilfe.");
                break;
        }
    }

    private static async Task ProcessPingCommandAsync(string args, CancellationToken ct)
    {
        var target = string.IsNullOrEmpty(args) ? null : args.Trim();
        var targetDisplay = target ?? "Server";
        
        Console.WriteLine($"[*] Ping an {targetDisplay}...");
        var rtt = await _connection!.PingAsync(target, ct);
        
        if (rtt.HasValue)
        {
            Console.WriteLine($"[+] Pong von {targetDisplay}: {rtt.Value.TotalMilliseconds:F1}ms");
        }
        else
        {
            Console.WriteLine($"[!] Timeout - keine Antwort von {targetDisplay}");
        }
    }

    private static async Task ProcessDiscoCommandAsync(string args, CancellationToken ct)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length == 0)
        {
            Console.WriteLine("Disco-Befehle:");
            Console.WriteLine("  /disco info <jid>    Features abfragen");
            Console.WriteLine("  /disco items <jid>   Services/Items abfragen");
            Console.WriteLine("  /disco server        Server-Features abfragen");
            return;
        }
        
        var subCommand = parts[0].ToLower();
        var jid = parts.Length > 1 ? parts[1] : _connection!.BareJid.Split('@')[1];
        
        switch (subCommand)
        {
            case "info":
            case "server":
                if (subCommand == "server")
                    jid = _connection!.BareJid.Split('@')[1];
                
                Console.WriteLine($"[*] Disco#info für {jid}...");
                var info = await _connection!.DiscoverInfoAsync(jid, ct);
                
                if (info != null)
                {
                    Console.WriteLine($"Identities:");
                    foreach (var id in info.Identities)
                        Console.WriteLine($"  {id}");
                    
                    Console.WriteLine($"Features ({info.Features.Count}):");
                    foreach (var feature in info.Features.Take(20))
                        Console.WriteLine($"  {feature}");
                    
                    if (info.Features.Count > 20)
                        Console.WriteLine($"  ... und {info.Features.Count - 20} weitere");
                }
                else
                {
                    Console.WriteLine("[!] Keine Antwort oder Timeout");
                }
                break;
                
            case "items":
                Console.WriteLine($"[*] Disco#items für {jid}...");
                var items = await _connection!.DiscoverItemsAsync(jid, ct);
                
                if (items != null)
                {
                    Console.WriteLine($"Items ({items.Items.Count}):");
                    foreach (var item in items.Items)
                        Console.WriteLine($"  {item}");
                }
                else
                {
                    Console.WriteLine("[!] Keine Antwort oder Timeout");
                }
                break;
                
            default:
                Console.WriteLine($"Unbekannter Disco-Befehl: {subCommand}");
                break;
        }
    }

    private static async Task ProcessMarkCommandAsync(string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length == 0 || _currentRecipient == null)
        {
            Console.WriteLine("Verwendung: /mark <received|displayed|ack> [message-id]");
            return;
        }
        
        var typeStr = parts[0].ToLower();
        var msgId = parts.Length > 1 ? parts[1] : _lastReceivedMessageId;
        
        if (string.IsNullOrEmpty(msgId))
        {
            Console.WriteLine("[!] Keine Message-ID angegeben und keine letzte Nachricht bekannt");
            return;
        }
        
        var markerType = typeStr switch
        {
            "received" or "r" => ChatMarkerType.Received,
            "displayed" or "d" or "read" => ChatMarkerType.Displayed,
            "acknowledged" or "ack" or "a" => ChatMarkerType.Acknowledged,
            _ => (ChatMarkerType?)null
        };
        
        if (!markerType.HasValue)
        {
            Console.WriteLine($"[!] Unbekannter Marker-Typ: {typeStr}");
            return;
        }
        
        await _connection!.SendChatMarkerAsync(_currentRecipient, msgId, markerType.Value);
        Console.WriteLine($"[+] {ChatMarkers.GetSymbol(markerType.Value)} Marker gesendet");
    }

    private static string? _lastReceivedMessageId;

    private static async Task ProcessPubSubCommandAsync(string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length == 0)
        {
            Console.WriteLine("PubSub-Befehle:");
            Console.WriteLine("  /pubsub sub <node>           Node abonnieren");
            Console.WriteLine("  /pubsub unsub <node>         Abo beenden");
            Console.WriteLine("  /pubsub pub <node> <id> <data>  Item veröffentlichen");
            Console.WriteLine("  /pubsub get <node> [max]     Items abrufen");
            Console.WriteLine("  /pubsub create <node>        Node erstellen");
            Console.WriteLine("  /pubsub delete <node>        Node löschen");
            return;
        }
        
        var subCmd = parts[0].ToLower();
        var nodeId = parts.Length > 1 ? parts[1] : "";
        
        switch (subCmd)
        {
            case "sub" or "subscribe":
                if (string.IsNullOrEmpty(nodeId))
                {
                    Console.WriteLine("Syntax: /pubsub sub <node>");
                    return;
                }
                await _connection!.PubSubSubscribeAsync(nodeId);
                Console.WriteLine($"📢 Abonniert: {nodeId}");
                break;
                
            case "unsub" or "unsubscribe":
                if (string.IsNullOrEmpty(nodeId))
                {
                    Console.WriteLine("Syntax: /pubsub unsub <node>");
                    return;
                }
                await _connection!.PubSubUnsubscribeAsync(nodeId);
                Console.WriteLine($"🔕 Abo beendet: {nodeId}");
                break;
                
            case "pub" or "publish":
                if (parts.Length < 4)
                {
                    Console.WriteLine("Syntax: /pubsub pub <node> <itemId> <payload>");
                    return;
                }
                var itemId = parts[2];
                var payload = string.Join(' ', parts.Skip(3));
                await _connection!.PubSubPublishAsync(nodeId, itemId, $"<data>{payload}</data>");
                Console.WriteLine($"📤 Veröffentlicht: {nodeId}/{itemId}");
                break;
                
            case "get" or "items":
                if (string.IsNullOrEmpty(nodeId))
                {
                    Console.WriteLine("Syntax: /pubsub get <node> [max]");
                    return;
                }
                int? max = parts.Length > 2 && int.TryParse(parts[2], out var m) ? m : null;
                await _connection!.PubSubGetItemsAsync(nodeId, max);
                Console.WriteLine($"📥 Items angefordert von: {nodeId}");
                break;
                
            case "create":
                if (string.IsNullOrEmpty(nodeId))
                {
                    Console.WriteLine("Syntax: /pubsub create <node>");
                    return;
                }
                await _connection!.PubSubCreateNodeAsync(nodeId);
                Console.WriteLine($"➕ Node erstellt: {nodeId}");
                break;
                
            case "delete":
                if (string.IsNullOrEmpty(nodeId))
                {
                    Console.WriteLine("Syntax: /pubsub delete <node>");
                    return;
                }
                await _connection!.PubSubDeleteNodeAsync(nodeId);
                Console.WriteLine($"➖ Node gelöscht: {nodeId}");
                break;
                
            default:
                Console.WriteLine($"Unbekannter PubSub-Befehl: {subCmd}");
                break;
        }
    }

    // === ROSTER-FUNKTIONEN ===

    private static void PrintRoster(string filter)
    {
        var items = _connection!.Roster.Items;
        
        if (!string.IsNullOrEmpty(filter))
        {
            items = items.Where(i => 
                i.Jid.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (i.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                i.Groups.Any(g => g.Contains(filter, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }
        
        if (!items.Any())
        {
            Console.WriteLine("Keine Kontakte gefunden.");
            return;
        }
        
        Console.WriteLine($"\n╔═══ Kontakte ({items.Count}) ═══");
        
        var grouped = items
            .SelectMany(i => i.Groups.DefaultIfEmpty("(Keine Gruppe)"), (item, group) => (item, group))
            .GroupBy(x => x.group)
            .OrderBy(g => g.Key);
        
        foreach (var group in grouped)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"║ [{group.Key}]");
            Console.ResetColor();
            
            foreach (var (item, _) in group.DistinctBy(x => x.item.Jid))
            {
                Console.WriteLine($"║   {item}");
            }
        }
        
        Console.WriteLine("╚" + new string('═', 30));
    }

    private static void PrintOnlineContacts()
    {
        var online = _connection!.Roster.GetOnlineContacts().ToList();
        
        if (!online.Any())
        {
            Console.WriteLine("Keine Kontakte online.");
            return;
        }
        
        Console.WriteLine($"\n● Online ({online.Count}):");
        foreach (var item in online)
        {
            var status = !string.IsNullOrEmpty(item.PresenceStatus) 
                ? $" - {item.PresenceStatus}" 
                : "";
            Console.WriteLine($"  {item}{status}");
        }
    }

    private static async Task AddContactAsync(string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            Console.WriteLine("Syntax: /add <jid> [name] [gruppe1,gruppe2,...]");
            return;
        }
        
        var jid = parts[0];
        var name = parts.Length > 1 ? parts[1] : null;
        var groups = parts.Length > 2 
            ? parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries) 
            : null;
        
        await _connection!.AddContactAsync(jid, name, groups);
        Console.WriteLine($"Kontaktanfrage gesendet an: {jid}");
    }

    private static async Task RemoveContactAsync(string jid)
    {
        if (string.IsNullOrEmpty(jid))
        {
            Console.WriteLine("Syntax: /remove <jid>");
            return;
        }
        
        await _connection!.RemoveContactAsync(jid.Trim());
        Console.WriteLine($"Kontakt entfernt: {jid}");
    }

    private static async Task AcceptSubscriptionAsync(string jid)
    {
        if (string.IsNullOrEmpty(jid))
        {
            if (_pendingSubscriptions.Count == 0)
            {
                Console.WriteLine("Keine ausstehenden Kontaktanfragen.");
                return;
            }
            jid = _pendingSubscriptions[0];
        }
        
        jid = jid.Trim();
        await _connection!.AcceptSubscriptionAsync(jid);
        _pendingSubscriptions.Remove(jid);
        
        // Auch zurück-subscriben für gegenseitige Subscription
        await _connection.AddContactAsync(jid);
        
        Console.WriteLine($"Kontaktanfrage akzeptiert: {jid}");
    }

    private static async Task DenySubscriptionAsync(string jid)
    {
        if (string.IsNullOrEmpty(jid))
        {
            if (_pendingSubscriptions.Count == 0)
            {
                Console.WriteLine("Keine ausstehenden Kontaktanfragen.");
                return;
            }
            jid = _pendingSubscriptions[0];
        }
        
        jid = jid.Trim();
        await _connection!.DenySubscriptionAsync(jid);
        _pendingSubscriptions.Remove(jid);
        
        Console.WriteLine($"Kontaktanfrage abgelehnt: {jid}");
    }

    private static void ShowContactInfo(string jid)
    {
        if (string.IsNullOrEmpty(jid))
        {
            Console.WriteLine("Syntax: /info <jid>");
            return;
        }
        
        var item = _connection!.Roster.GetItem(jid.Trim());
        if (item == null)
        {
            Console.WriteLine($"Kontakt nicht gefunden: {jid}");
            return;
        }
        
        Console.WriteLine($"\n╔═══ Kontakt-Info ═══");
        Console.WriteLine($"║ JID:          {item.Jid}");
        Console.WriteLine($"║ Name:         {item.Name ?? "(nicht gesetzt)"}");
        Console.WriteLine($"║ Subscription: {item.Subscription}");
        Console.WriteLine($"║ Gruppen:      {(item.Groups.Any() ? string.Join(", ", item.Groups) : "(keine)")}");
        Console.WriteLine($"║ Status:       {item.Presence}");
        if (!string.IsNullOrEmpty(item.PresenceStatus))
            Console.WriteLine($"║ Status-Text:  {item.PresenceStatus}");
        if (item.LastSeen != default)
            Console.WriteLine($"║ Zuletzt:      {item.LastSeen:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine("╚" + new string('═', 25));
    }

    private static void PrintGroups()
    {
        var groups = _connection!.Roster.GetGroups().ToList();
        
        if (!groups.Any())
        {
            Console.WriteLine("Keine Gruppen definiert.");
            return;
        }
        
        Console.WriteLine("\nGruppen:");
        foreach (var group in groups)
        {
            var count = _connection.Roster.GetByGroup(group).Count();
            Console.WriteLine($"  [{group}] - {count} Kontakte");
        }
    }

    private static void PrintPendingSubscriptions()
    {
        if (_pendingSubscriptions.Count == 0)
        {
            Console.WriteLine("Keine ausstehenden Kontaktanfragen.");
            return;
        }
        
        Console.WriteLine("\n📩 Ausstehende Kontaktanfragen:");
        for (int i = 0; i < _pendingSubscriptions.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {_pendingSubscriptions[i]}");
        }
        Console.WriteLine("\nNutze /accept <jid> oder /deny <jid>");
    }

    // === EVENT-HANDLER ===

    private static void HandleMessage(string from, string to, string body, string? messageId)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var shortFrom = GetShortJid(from);
        
        // Store last message ID for /mark command
        if (!string.IsNullOrEmpty(messageId))
        {
            _lastReceivedMessageId = messageId;
        }
        
        ClearCurrentLine();
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"[{timestamp}] ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"{shortFrom}: ");
        Console.ResetColor();
        Console.WriteLine(body);
        
        WritePrompt();
    }

    private static void HandleChatState(string from, ChatState state)
    {
        var shortFrom = GetShortJid(from);
        
        // Nur bestimmte States anzeigen
        if (state == ChatState.Composing)
        {
            ClearCurrentLine();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"✏️ {shortFrom} tippt...");
            Console.ResetColor();
            WritePrompt();
        }
        else if (state == ChatState.Paused)
        {
            ClearCurrentLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"⏸️ {shortFrom} hat aufgehört zu tippen");
            Console.ResetColor();
            WritePrompt();
        }
    }

    private static void HandleReceipt(string from, string messageId)
    {
        var shortFrom = GetShortJid(from);
        
        ClearCurrentLine();
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"✓ Zugestellt an {shortFrom}");
        Console.ResetColor();
        WritePrompt();
    }

    private static void HandleCarbon(CarbonMessage carbon)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        
        ClearCurrentLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        
        if (carbon.IsSent)
        {
            // Von mir auf anderem Gerät gesendet
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.Write($"📤 Ich → {GetShortJid(carbon.OriginalTo)}: ");
        }
        else
        {
            // Auf anderem Gerät empfangen
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write($"📥 {GetShortJid(carbon.OriginalFrom)} (Carbon): ");
        }
        
        Console.ResetColor();
        Console.WriteLine(carbon.Body ?? "(kein Inhalt)");
        WritePrompt();
    }

    private static void HandleChatMarker(ChatMarker marker)
    {
        ClearCurrentLine();
        var shortFrom = GetShortJid(marker.From);
        var symbol = ChatMarkers.GetSymbol(marker.Type);
        
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"{symbol} {shortFrom}: {marker.Type} (Msg: {marker.MessageId[..Math.Min(12, marker.MessageId.Length)]}...)");
        Console.ResetColor();
        WritePrompt();
    }

    private static void HandlePubSubEvent(PubSubEvent evt)
    {
        ClearCurrentLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        
        switch (evt.Type)
        {
            case PubSubEventType.Items:
                Console.WriteLine($"📢 PubSub [{evt.NodeId}]: {evt.Items.Count} Item(s)");
                foreach (var item in evt.Items)
                {
                    Console.WriteLine($"   - {item.Id}: {item.Payload[..Math.Min(50, item.Payload.Length)]}...");
                }
                break;
                
            case PubSubEventType.Retract:
                Console.WriteLine($"🗑️ PubSub [{evt.NodeId}]: Item(s) entfernt: {string.Join(", ", evt.RetractedIds)}");
                break;
                
            case PubSubEventType.Purge:
                Console.WriteLine($"🧹 PubSub [{evt.NodeId}]: Node geleert");
                break;
                
            case PubSubEventType.Delete:
                Console.WriteLine($"❌ PubSub [{evt.NodeId}]: Node gelöscht");
                break;
        }
        
        Console.ResetColor();
        WritePrompt();
    }

    private static void HandlePresence(string from, string type)
    {
        if (_showRawXml) return; // Bei Raw-Mode wird das schon angezeigt
        
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var shortFrom = GetShortJid(from);
        
        // Nur eigene Presence ignorieren
        if (from.StartsWith(_connection!.FullJid.Split('/')[0]))
            return;
        
        ClearCurrentLine();
        
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[{timestamp}] {shortFrom} → {type}");
        Console.ResetColor();
        
        WritePrompt();
    }

    private static void HandleError(string error)
    {
        ClearCurrentLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[!] {error}");
        Console.ResetColor();
        WritePrompt();
    }

    private static void HandleRawXml(string xml)
    {
        if (!_showRawXml) return;
        
        ClearCurrentLine();
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine($"[XML] {xml.Trim()}");
        Console.ResetColor();
        WritePrompt();
    }

    private static void WriteSystemMessage(string message)
    {
        ClearCurrentLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[*] {message}");
        Console.ResetColor();
        WritePrompt();
    }

    private static void WriteWarning(string message)
    {
        ClearCurrentLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[!] {message}");
        Console.ResetColor();
        WritePrompt();
    }

    private static void ClearCurrentLine()
    {
        try
        {
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
        }
        catch
        {
            Console.WriteLine();
        }
    }

    private static void WritePrompt()
    {
        var prompt = _currentRecipient != null 
            ? $"[{GetShortJid(_currentRecipient)}] > " 
            : "> ";
        Console.Write(prompt);
    }

    private static string GetShortJid(string jid)
    {
        var slashIndex = jid.IndexOf('/');
        return slashIndex > 0 ? jid[..slashIndex] : jid;
    }

    private static void PrintHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
  ╔═══════════════════════════════════════════╗
  ║      XMPP Console Client (.NET 10)        ║
  ║   WebSocket (RFC 7395) + Auto-Reconnect   ║
  ╚═══════════════════════════════════════════╝
");
        Console.ResetColor();
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"
Nachrichten:
  /to <jid>          Chat-Partner setzen (dann direkt tippen)
  /msg <jid> <text>  Einzelne Nachricht senden
  /status [show] [text]  Status ändern (away/chat/dnd/xa)

Kontakte (Roster):
  /roster [filter]   Alle Kontakte anzeigen
  /online            Nur Online-Kontakte
  /add <jid> [name]  Kontakt hinzufügen
  /remove <jid>      Kontakt entfernen
  /info <jid>        Kontakt-Details anzeigen

Chat-Status (XEP-0085):
  /typing    'Tippt gerade...' senden
  /paused    'Hat aufgehört zu tippen'
  /gone      Chat verlassen

Chat Markers (XEP-0333):
  /mark displayed    Nachricht als gelesen markieren
  /mark ack          Nachricht bestätigen

Service Discovery (XEP-0030):
  /disco server      Server-Features abfragen
  /disco info <jid>  Features eines JIDs abfragen
  /disco items <jid> Services auflisten
  /features          Eigene Features anzeigen

Verbindung:
  /ping [jid]     Ping senden (XEP-0199)
  /sm [on|off]    Stream Management (XEP-0198, experimentell)
  /keepalive [s]  Keepalive Status/Interval setzen
  /who            Status anzeigen
  /reconnect      Neu verbinden
  /disconnect     Trennen
  /quit           Beenden

PubSub (XEP-0060):
  /pubsub sub <node>   Node abonnieren
  /pubsub pub <n> <id> Item veröffentlichen

Sonstiges:
  /carbons  Message Carbons Status
  /raw      XML-Debug-Anzeige

Features:
  ✓ SCRAM-SHA-1/256 + SASL PLAIN Authentifizierung
  ✓ WebSocket Transport (RFC 7395)
  ✓ Auto-Reconnect mit Exponential Backoff
  ✓ Keepalive (verhindert Server-Timeout)
  ✓ Service Discovery (XEP-0030)
  ✓ Entity Capabilities (XEP-0115)
  ✓ Ping (XEP-0199)
  ✓ Chat Markers (XEP-0333)
  ✓ Receipts (XEP-0184) mit Spoofing-Schutz
  ✓ Carbons (XEP-0280) mit Spoofing-Schutz
");
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"
Verwendung: XmppClient [Optionen]

Optionen:
  -j, --jid <jid>         JID (z.B. user@jabber.org)
  -p, --password <pw>     Passwort
  -w, --websocket <uri>   WebSocket URI (z.B. wss://jabber.org:5443/ws)
  -h, --help              Diese Hilfe anzeigen

Beispiele:
  XmppClient -j user@jabber.org -p geheim
  XmppClient -j user@example.com -p pw -w wss://xmpp.example.com/ws
");
    }
}
