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
        
        var (jid, password, server, port) = GetCredentials(args);
        
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
            _connection = new XmppConnection(jid, password, server, port);
            
            // Event-Handler registrieren
            _connection.OnMessage += HandleMessage;
            _connection.OnPresence += HandlePresence;
            _connection.OnError += HandleError;
            _connection.OnRawXml += HandleRawXml;
            
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
            
            // Verbinden
            await _connection.ConnectAsync(cts.Token);
            
            // Empfangs-Task starten
            var receiveTask = _connection.StartReceivingAsync(cts.Token);
            
            PrintHelp();
            
            // Konsolen-Input verarbeiten
            await ProcessConsoleInputAsync(cts.Token);
            
            await receiveTask;
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

    private static (string jid, string password, string? server, int port) GetCredentials(string[] args)
    {
        string? jid = null;
        string? password = null;
        string? server = null;
        int port = 5222;
        
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
                case "-s" or "--server" when i + 1 < args.Length:
                    server = args[++i];
                    break;
                case "--port" when i + 1 < args.Length:
                    port = int.Parse(args[++i]);
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
        
        if (string.IsNullOrEmpty(server))
        {
            Console.Write($"Server (Enter für Domain aus JID): ");
            var input = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(input))
                server = input;
        }
        
        return (jid, password, server, port);
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
                
            // === SONSTIGE ===
                
            case "/raw":
                _showRawXml = !_showRawXml;
                Console.WriteLine($"Raw XML Anzeige: {(_showRawXml ? "AN" : "AUS")}");
                break;
                
            case "/who":
                Console.WriteLine($"Angemeldet als: {_connection!.FullJid}");
                if (_currentRecipient != null)
                    Console.WriteLine($"Chat mit: {_currentRecipient}");
                break;
                
            default:
                Console.WriteLine($"Unbekannter Befehl: {command}. Tippe /help für Hilfe.");
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

    private static void HandleMessage(string from, string to, string body)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var shortFrom = GetShortJid(from);
        
        ClearCurrentLine();
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"[{timestamp}] ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"{shortFrom}: ");
        Console.ResetColor();
        Console.WriteLine(body);
        
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
  ╔═══════════════════════════════════════╗
  ║     XMPP Console Client (.NET 10)     ║
  ║        TLS + SASL + Roster            ║
  ╚═══════════════════════════════════════╝
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
  /add <jid> [name] [gruppen]  Kontakt hinzufügen
  /remove <jid>      Kontakt entfernen
  /info <jid>        Kontakt-Details anzeigen
  /groups            Alle Gruppen anzeigen

Kontaktanfragen:
  /pending           Ausstehende Anfragen anzeigen
  /accept [jid]      Anfrage akzeptieren
  /deny [jid]        Anfrage ablehnen

Sonstiges:
  /who    Eigene JID anzeigen
  /raw    XML-Debug-Anzeige
  /quit   Beenden
");
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"
Verwendung: XmppClient [Optionen]

Optionen:
  -j, --jid <jid>       JID (z.B. user@jabber.org)
  -p, --password <pw>   Passwort
  -s, --server <host>   Server (falls anders als Domain)
      --port <port>     Port (Standard: 5222)
  -h, --help            Diese Hilfe anzeigen
");
    }
}
