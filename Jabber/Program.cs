/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Hermod <https://www.github.com/Vanaheimr/Hermod>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using Microsoft.Extensions.Logging;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Konsolen-Frontend für den <see cref="XMPPClient"/>.
///
/// Diese Klasse enthält ausschließlich Benutzeroberfläche: Kommandozeilen-
/// Parsing, Kommando-Dispatch und Darstellung. Die gesamte Sitzungslogik
/// (Chatpartner, Kontaktanfragen, zusammengesetzte Operationen) liegt im
/// <see cref="XMPPClient"/>.
/// </summary>
class Program
{

    #region Data

    private static XMPPClient? _client;
    private static bool _showRawXml;
    private static volatile bool _running = true;

    #endregion

    static async Task Main(string[] args)
    {

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "XMPP Console Client";

        PrintHeader();

        var options = ParseArguments(args);
        if (options is null)
            return;

        var (jid, password, wsUri, verbose) = options.Value;

        if (string.IsNullOrEmpty(jid) || string.IsNullOrEmpty(password))
        {
            Console.WriteLine("Fehler: JID und Passwort erforderlich");
            return;
        }

        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(o =>
            {
                o.SingleLine       = true;
                o.TimestampFormat  = "HH:mm:ss ";
                o.IncludeScopes    = false;
            });
            builder.SetMinimumLevel(verbose ? LogLevel.Trace : LogLevel.Information);
        });

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _running = false;
            cts.Cancel();
        };

        try
        {
            _client = new XMPPClient(jid, password, wsUri, loggerFactory);

            WireUpUserInterface(_client);

            await _client.ConnectAsync(cts.Token);

            PrintHelp();

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
            if (_client != null)
                await _client.DisposeAsync();
        }

    }

    #region Verdrahtung der Anzeige

    private static void WireUpUserInterface(XMPPClient client)
    {

        client.OnMessage           += HandleMessage;
        client.OnCarbonMessage     += HandleCarbon;
        client.OnChatState         += HandleChatState;
        client.OnChatMarker        += HandleChatMarker;
        client.OnReceiptReceived   += HandleReceipt;
        client.OnPresenceChanged   += HandlePresence;
        client.OnPubSubEvent       += HandlePubSubEvent;
        client.OnError             += HandleError;
        client.OnRawXml            += HandleRawXml;

        client.OnSpoofingAttempt   += msg => WriteWarning($"⚠️ SPOOFING: {msg}");

        client.OnStateChanged += (oldState, newState) =>
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

        client.OnCapsDiscovered += (from, info) =>
        {
            if (_showRawXml)
                WriteSystemMessage($"[Caps] {from}: {string.Join(", ", info.Identities)}");
        };

        client.OnRosterItemAdded   += item => WriteSystemMessage($"Kontakt hinzugefügt: {item.DisplayName}");
        client.OnRosterItemRemoved += jid  => WriteSystemMessage($"Kontakt entfernt: {jid}");

        client.OnSubscriptionRequest += (from, status) =>
        {
            WriteSystemMessage($"📩 Kontaktanfrage von {from}: {status}");
            WriteSystemMessage($"   Nutze /accept {from} oder /deny {from}");
        };

    }

    #endregion

    #region Kommandozeilen-Parsing

    private static (string jid, string password, string? wsUri, bool verbose)? ParseArguments(string[] args)
    {

        string? jid = null;
        string? password = null;
        string? wsUri = null;
        var verbose = false;

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
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                case "-h" or "--help":
                    PrintUsage();
                    return null;
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

        return (jid, password, wsUri, verbose);

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

    #endregion

    #region Eingabeschleife und Kommandos

    private static async Task ProcessConsoleInputAsync(CancellationToken ct)
    {

        while (_running && !ct.IsCancellationRequested)
        {
            Console.Write(BuildPrompt());

            string? input;
            try
            {
                input = await Task.Run(Console.ReadLine, ct);
                input = input?.Trim();
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
            else
            {
                var messageId = await _client!.SendMessageAsync(input);
                if (messageId == null)
                    Console.WriteLine("Kein Empfänger gesetzt. Nutze /msg <jid> <nachricht> oder /to <jid>");
                else
                    Console.WriteLine($"  → Gesendet an {GetShortJid(_client.CurrentChatPartner!)}");
            }
        }

    }

    private static async Task ProcessCommandAsync(string input, CancellationToken ct)
    {

        var parts   = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLower();
        var args    = parts.Length > 1 ? parts[1] : "";
        var client  = _client!;

        switch (command)
        {
            case "/help" or "/h" or "/?":
                PrintHelp();
                break;

            case "/quit" or "/q" or "/exit":
                _running = false;
                break;

            case "/to" or "/chat":
                client.SetChatPartner(string.IsNullOrEmpty(args) ? null : args);
                Console.WriteLine(client.CurrentChatPartner == null
                                      ? "Chat-Empfänger zurückgesetzt"
                                      : $"Chat mit: {client.CurrentChatPartner}");
                break;

            case "/msg" or "/m":
                var msgParts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (msgParts.Length < 2)
                {
                    Console.WriteLine("Syntax: /msg <jid> <nachricht>");
                }
                else
                {
                    await client.SendMessageAsync(msgParts[0], msgParts[1]);
                    Console.WriteLine($"  → Gesendet an {GetShortJid(msgParts[0])}");
                }
                break;

            case "/status" or "/s":
                await ProcessStatusCommandAsync(args);
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
                if (string.IsNullOrEmpty(args))
                {
                    Console.WriteLine("Syntax: /remove <jid>");
                }
                else
                {
                    await client.RemoveContactAsync(args);
                    Console.WriteLine($"Kontakt entfernt: {args.Trim()}");
                }
                break;

            case "/accept":
                var accepted = await client.AcceptSubscriptionAsync(args);
                Console.WriteLine(accepted == null
                                      ? "Keine ausstehenden Kontaktanfragen."
                                      : $"Kontaktanfrage akzeptiert: {accepted}");
                break;

            case "/deny":
                var denied = await client.DenySubscriptionAsync(args);
                Console.WriteLine(denied == null
                                      ? "Keine ausstehenden Kontaktanfragen."
                                      : $"Kontaktanfrage abgelehnt: {denied}");
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
                if (!await client.SendChatStateAsync(ChatState.Composing))
                    Console.WriteLine("Kein Empfänger gesetzt. Nutze /to <jid>");
                else
                    Console.WriteLine($"⌨️ Typing-Indicator gesendet an {GetShortJid(client.CurrentChatPartner!)}");
                break;

            case "/paused":
                if (!await client.SendChatStateAsync(ChatState.Paused))
                    Console.WriteLine("Kein Empfänger gesetzt. Nutze /to <jid>");
                break;

            case "/gone":
                var left = await client.LeaveChatAsync();
                Console.WriteLine(left == null ? "Kein Chat aktiv." : "Chat beendet");
                break;

            // === XEP-0060: PUBSUB ===

            case "/pubsub":
                await ProcessPubSubCommandAsync(args);
                break;

            // === SONSTIGE ===

            case "/carbons":
                Console.WriteLine(client.CarbonsEnabled
                                      ? "✓ Message Carbons sind AKTIVIERT"
                                      : "✗ Message Carbons sind NICHT aktiviert");
                break;

            case "/raw":
                _showRawXml = !_showRawXml;
                Console.WriteLine($"Raw XML Anzeige: {(_showRawXml ? "AN" : "AUS")}");
                break;

            case "/who":
                PrintOwnStatus();
                break;

            case "/ping":
                await ProcessPingCommandAsync(args, ct);
                break;

            case "/disco":
                await ProcessDiscoCommandAsync(args, ct);
                break;

            case "/features":
                Console.WriteLine("Server-Features:");
                foreach (var feature in client.ServerFeatures)
                    Console.WriteLine($"  {feature}");

                Console.WriteLine("\nLokal unterstützte Features:");
                foreach (var feature in client.LocalFeatures)
                    Console.WriteLine($"  {feature}");
                break;

            case "/mark":
                await ProcessMarkCommandAsync(args);
                break;

            case "/sm":
                await ProcessStreamManagementCommandAsync(args);
                break;

            case "/keepalive":
                ProcessKeepaliveCommand(args);
                break;

            case "/reconnect":
                if (client.IsConnected)
                {
                    Console.WriteLine("[*] Bereits verbunden. Trenne erst mit /disconnect");
                }
                else
                {
                    Console.WriteLine("[*] Manueller Reconnect...");
                    await client.ConnectAsync(ct);
                }
                break;

            case "/disconnect":
                Console.WriteLine("[*] Trenne Verbindung...");
                await client.DisconnectAsync();
                Console.WriteLine("[+] Getrennt");
                break;

            default:
                Console.WriteLine($"Unbekannter Befehl: {command}. Tippe /help für Hilfe.");
                break;
        }

    }

    private static async Task ProcessStatusCommandAsync(string args)
    {

        var statusParts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var show        = statusParts.Length > 0 ? statusParts[0] : null;
        var statusText  = statusParts.Length > 1 ? statusParts[1] : null;

        if (!XMPPClient.IsValidShow(show))
        {
            Console.WriteLine("Status muss sein: available, away, chat, dnd, xa");
            return;
        }

        await _client!.SetPresenceAsync(show, statusText);
        Console.WriteLine($"Status: {show ?? "available"} {statusText ?? ""}");

    }

    private static async Task ProcessPingCommandAsync(string args, CancellationToken ct)
    {

        var target        = string.IsNullOrEmpty(args) ? null : args.Trim();
        var targetDisplay = target ?? "Server";

        Console.WriteLine($"[*] Ping an {targetDisplay}...");
        var rtt = await _client!.PingAsync(target, ct);

        Console.WriteLine(rtt.HasValue
                              ? $"[+] Pong von {targetDisplay}: {rtt.Value.TotalMilliseconds:F1}ms"
                              : $"[!] Timeout - keine Antwort von {targetDisplay}");

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
        var jid        = parts.Length > 1 ? parts[1] : _client!.Domain;

        switch (subCommand)
        {
            case "info":
            case "server":
                if (subCommand == "server")
                    jid = _client!.Domain;

                Console.WriteLine($"[*] Disco#info für {jid}...");
                var info = await _client!.DiscoverInfoAsync(jid, ct);

                if (info != null)
                {
                    Console.WriteLine("Identities:");
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
                var items = await _client!.DiscoverItemsAsync(jid, ct);

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

        if (parts.Length == 0)
        {
            Console.WriteLine("Verwendung: /mark <received|displayed|ack> [message-id]");
            Console.WriteLine("            /mark displayed (für letzte Nachricht an aktuellen Empfänger)");
            return;
        }

        var markerType = parts[0].ToLower() switch
        {
            "received" or "r"               => ChatMarkerType.Received,
            "displayed" or "d" or "read"    => ChatMarkerType.Displayed,
            "acknowledged" or "ack" or "a"  => ChatMarkerType.Acknowledged,
            _                               => (ChatMarkerType?) null
        };

        if (!markerType.HasValue)
        {
            Console.WriteLine($"[!] Unbekannter Marker-Typ: {parts[0]}");
            return;
        }

        if (_client!.CurrentChatPartner == null)
        {
            Console.WriteLine("Kein Empfänger gesetzt. Nutze /to <jid>");
            return;
        }

        var messageId = parts.Length > 1 ? parts[1] : null;
        var marked    = await _client.SendMarkerAsync(markerType.Value, messageId);

        Console.WriteLine(marked == null
                              ? "[!] Keine Message-ID angegeben und keine letzte Nachricht bekannt"
                              : $"[+] {ChatMarkers.GetSymbol(markerType.Value)} Marker gesendet");

    }

    private static async Task ProcessStreamManagementCommandAsync(string args)
    {

        var client = _client!;

        Console.WriteLine("Stream Management:");
        Console.WriteLine($"  Konfiguriert: {client.StreamManagementEnabled}");
        Console.WriteLine($"  Aktiv: {client.StreamManagement?.IsEnabled == true}");

        if (client.StreamManagement?.IsEnabled == true)
        {
            var sm = client.StreamManagement;
            Console.WriteLine($"  Eingehend: {sm.InboundCount}");
            Console.WriteLine($"  Ausgehend: {sm.OutboundCount}");
            Console.WriteLine($"  Unbestätigt: {sm.UnackedCount}");

            Console.WriteLine("[*] Fordere Ack an...");
            await client.RequestAckAsync();
        }

        if (string.IsNullOrEmpty(args))
            return;

        switch (args.ToLower())
        {
            // Hier stand eine Warnung, SM führe "bei einigen Servern
            // (ejabberd) zu Disconnects". Ursache war die eigene fehlerhafte
            // Zählung, nicht der Server; sie ist behoben und gegen Prosody 13
            // belegt. SM ist inzwischen der Vorgabewert.
            case "on":
                client.StreamManagementEnabled = true;
                Console.WriteLine("[*] SM aktiviert (wirkt nach Reconnect)");
                break;

            case "off":
                client.StreamManagementEnabled = false;
                Console.WriteLine("[*] SM deaktiviert (wirkt nach Reconnect)");
                break;
        }

    }

    private static void ProcessKeepaliveCommand(string args)
    {

        var client = _client!;

        Console.WriteLine("Keepalive Status:");
        Console.WriteLine($"  Aktiviert: {client.KeepaliveEnabled}");
        Console.WriteLine($"  Interval: {client.KeepaliveInterval.TotalSeconds}s");
        Console.WriteLine($"  Methode: {(client.StreamManagement?.IsEnabled == true ? "Stream Management <r/>" : "XEP-0199 Ping")}");

        if (string.IsNullOrEmpty(args))
            return;

        if (args.ToLower() is "off" or "0")
        {
            client.KeepaliveEnabled = false;
            Console.WriteLine("[*] Keepalive deaktiviert");
        }
        else if (args.ToLower() is "on" or "1")
        {
            client.KeepaliveEnabled = true;
            Console.WriteLine("[*] Keepalive aktiviert (wirkt nach Reconnect)");
        }
        else if (int.TryParse(args, out var seconds) && seconds > 0)
        {
            client.KeepaliveInterval = TimeSpan.FromSeconds(seconds);
            Console.WriteLine($"[*] Keepalive-Interval auf {seconds}s gesetzt (wirkt nach Reconnect)");
        }

    }

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

        string[] nodeCommands = ["sub", "subscribe", "unsub", "unsubscribe",
                                 "pub", "publish", "get", "items", "create", "delete"];

        if (nodeCommands.Contains(subCmd) && string.IsNullOrEmpty(nodeId))
        {
            Console.WriteLine($"Syntax: /pubsub {subCmd} <node>");
            return;
        }

        switch (subCmd)
        {
            case "sub" or "subscribe":
                await _client!.PubSubSubscribeAsync(nodeId);
                Console.WriteLine($"📢 Abonniert: {nodeId}");
                break;

            case "unsub" or "unsubscribe":
                await _client!.PubSubUnsubscribeAsync(nodeId);
                Console.WriteLine($"🔕 Abo beendet: {nodeId}");
                break;

            case "pub" or "publish":
                if (parts.Length < 4)
                {
                    Console.WriteLine("Syntax: /pubsub pub <node> <itemId> <payload>");
                    return;
                }
                var itemId  = parts[2];
                var payload = string.Join(' ', parts.Skip(3));
                await _client!.PubSubPublishAsync(nodeId, itemId, $"<data>{XmlEscaping.Escape(payload)}</data>");
                Console.WriteLine($"📤 Veröffentlicht: {nodeId}/{itemId}");
                break;

            case "get" or "items":
                int? max = parts.Length > 2 && int.TryParse(parts[2], out var m) ? m : null;
                await _client!.PubSubGetItemsAsync(nodeId, max);
                Console.WriteLine($"📥 Items angefordert von: {nodeId}");
                break;

            case "create":
                await _client!.PubSubCreateNodeAsync(nodeId);
                Console.WriteLine($"➕ Node erstellt: {nodeId}");
                break;

            case "delete":
                await _client!.PubSubDeleteNodeAsync(nodeId);
                Console.WriteLine($"➖ Node gelöscht: {nodeId}");
                break;

            default:
                Console.WriteLine($"Unbekannter PubSub-Befehl: {subCmd}");
                break;
        }

    }

    #endregion

    #region Roster-Anzeige

    private static void PrintRoster(string filter)
    {

        var items = _client!.GetContacts(filter);

        if (items.Count == 0)
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
                Console.WriteLine($"║   {item}");
        }

        Console.WriteLine("╚" + new string('═', 30));

    }

    private static void PrintOnlineContacts()
    {

        var online = _client!.GetOnlineContacts().ToList();

        if (online.Count == 0)
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

        var jid    = parts[0];
        var name   = parts.Length > 1 ? parts[1] : null;
        var groups = parts.Length > 2
                         ? parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries)
                         : null;

        await _client!.AddContactAsync(jid, name, groups);
        Console.WriteLine($"Kontaktanfrage gesendet an: {jid}");

    }

    private static void ShowContactInfo(string jid)
    {

        if (string.IsNullOrEmpty(jid))
        {
            Console.WriteLine("Syntax: /info <jid>");
            return;
        }

        var item = _client!.GetContact(jid);
        if (item == null)
        {
            Console.WriteLine($"Kontakt nicht gefunden: {jid}");
            return;
        }

        Console.WriteLine("\n╔═══ Kontakt-Info ═══");
        Console.WriteLine($"║ JID:          {item.Jid}");
        Console.WriteLine($"║ Name:         {item.Name ?? "(nicht gesetzt)"}");
        Console.WriteLine($"║ Subscription: {item.Subscription}");
        Console.WriteLine($"║ Gruppen:      {(item.Groups.Count > 0 ? string.Join(", ", item.Groups) : "(keine)")}");
        Console.WriteLine($"║ Status:       {item.Presence}");
        if (!string.IsNullOrEmpty(item.PresenceStatus))
            Console.WriteLine($"║ Status-Text:  {item.PresenceStatus}");
        if (item.LastSeen != default)
            Console.WriteLine($"║ Zuletzt:      {item.LastSeen:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine("╚" + new string('═', 25));

    }

    private static void PrintGroups()
    {

        var groups = _client!.GetGroups().ToList();

        if (groups.Count == 0)
        {
            Console.WriteLine("Keine Gruppen definiert.");
            return;
        }

        Console.WriteLine("\nGruppen:");
        foreach (var group in groups)
        {
            var count = _client.GetContactsByGroup(group).Count();
            Console.WriteLine($"  [{group}] - {count} Kontakte");
        }

    }

    private static void PrintPendingSubscriptions()
    {

        var pending = _client!.PendingSubscriptions;

        if (pending.Count == 0)
        {
            Console.WriteLine("Keine ausstehenden Kontaktanfragen.");
            return;
        }

        Console.WriteLine("\n📩 Ausstehende Kontaktanfragen:");
        for (int i = 0; i < pending.Count; i++)
            Console.WriteLine($"  {i + 1}. {pending[i]}");

        Console.WriteLine("\nNutze /accept <jid> oder /deny <jid>");

    }

    private static void PrintOwnStatus()
    {

        var client = _client!;

        Console.WriteLine($"Angemeldet als: {client.FullJid}");
        Console.WriteLine($"Bare JID: {client.BareJid}");
        Console.WriteLine($"Status: {client.State}");
        if (client.CurrentChatPartner != null)
            Console.WriteLine($"Chat mit: {client.CurrentChatPartner}");
        Console.WriteLine($"Carbons: {(client.CarbonsEnabled ? "aktiviert" : "deaktiviert")}");
        Console.WriteLine($"Stream Mgmt: {(client.StreamManagement?.IsEnabled == true ? "aktiviert" : "deaktiviert")}");
        Console.WriteLine($"Keepalive: {(client.KeepaliveEnabled ? $"alle {client.KeepaliveInterval.TotalSeconds}s" : "deaktiviert")}");
        Console.WriteLine($"Transport: WebSocket (RFC 7395) - {client.WebSocketUri}");

    }

    #endregion

    #region Event-Handler (Darstellung)

    private static void HandleMessage(XMPPMessage message)
    {

        ClearCurrentLine();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"[{message.Timestamp:HH:mm:ss}] ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"{GetShortJid(message.From)}: ");
        Console.ResetColor();
        Console.WriteLine(message.Body);

        WritePrompt();

    }

    private static void HandleChatState(string from, ChatState state)
    {

        var shortFrom = GetShortJid(from);

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

        ClearCurrentLine();
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"✓ Zugestellt an {GetShortJid(from)}");
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
        var symbol    = ChatMarkers.GetSymbol(marker.Type);

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
                    Console.WriteLine($"   - {item.Id}: {item.Payload[..Math.Min(50, item.Payload.Length)]}...");
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

        // Eigene Presence ignorieren
        if (JidUtilities.Bare(from).Equals(_client!.BareJid, StringComparison.OrdinalIgnoreCase))
            return;

        ClearCurrentLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {GetShortJid(from)} → {type}");
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

    #endregion

    #region Ausgabe-Hilfsfunktionen

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

    private static string BuildPrompt()
        => _client?.CurrentChatPartner != null
               ? $"[{GetShortJid(_client.CurrentChatPartner)}] > "
               : "> ";

    private static void WritePrompt() => Console.Write(BuildPrompt());

    private static string GetShortJid(string jid)
    {
        var slashIndex = jid.IndexOf('/');
        return slashIndex > 0 ? jid[..slashIndex] : jid;
    }

    #endregion

    #region Hilfetexte

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
  /to                Chat-Partner zurücksetzen
  /msg <jid> <text>  Einzelne Nachricht senden
  /status [show] [text]  Status ändern (available/away/chat/dnd/xa)

Kontakte (Roster):
  /roster [filter]   Alle Kontakte anzeigen
  /online            Nur Online-Kontakte
  /add <jid> [name] [g1,g2]  Kontakt hinzufügen
  /remove <jid>      Kontakt entfernen
  /info <jid>        Kontakt-Details anzeigen
  /groups            Gruppen mit Kontaktanzahl
  /pending           Offene Kontaktanfragen
  /accept [jid]      Kontaktanfrage annehmen
  /deny [jid]        Kontaktanfrage ablehnen

Chat-Status (XEP-0085):
  /typing    'Tippt gerade...' senden
  /paused    'Hat aufgehört zu tippen'
  /gone      Chat verlassen

Chat Markers (XEP-0333):
  /mark received [id]   Als empfangen markieren
  /mark displayed [id]  Als gelesen markieren
  /mark ack [id]        Nachricht bestätigen

Service Discovery (XEP-0030):
  /disco server      Server-Features abfragen
  /disco info <jid>  Features eines JIDs abfragen
  /disco items <jid> Services auflisten
  /features          Server- und eigene Features anzeigen

PubSub (XEP-0060):
  /pubsub sub <node>              Node abonnieren
  /pubsub unsub <node>            Abo beenden
  /pubsub pub <node> <id> <data>  Item veröffentlichen
  /pubsub get <node> [max]        Items abrufen
  /pubsub create|delete <node>    Node anlegen/löschen

Verbindung:
  /ping [jid]     Ping senden (XEP-0199)
  /sm [on|off]    Stream Management (XEP-0198, experimentell)
  /keepalive [s]  Keepalive Status/Interval setzen
  /who            Status anzeigen
  /carbons        Message-Carbons-Status
  /raw            XML-Debug-Anzeige umschalten
  /reconnect      Neu verbinden
  /disconnect     Trennen
  /quit           Beenden

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
  -v, --verbose           Ausführliches Logging (Trace-Level)
  -h, --help              Diese Hilfe anzeigen

Beispiele:
  XmppClient -j user@jabber.org -p geheim
  XmppClient -j user@example.com -p pw -w wss://xmpp.example.com/ws
");
    }

    #endregion

}
