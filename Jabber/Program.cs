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

using org.GraphDefined.Vanaheimr.Hermod.XMPP.ConsoleUI;

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
    /// <summary>
    /// Die gemeinsame Ausgabe: Ereignisse, Systemmeldungen und das Protokoll
    /// gehen durch dieselbe Sperre und lassen die Eingabezeile heil.
    /// </summary>
    private static ConsoleOutput? _ausgabe;

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

        // Alles, was auf die Konsole geht, geht durch dieselbe Tuer - auch das
        // Protokoll. Ein AddSimpleConsole schriebe mitten in die halb getippte
        // Eingabezeile und liesse den Anwender ohne Eingabeaufforderung zurueck
        // (siehe ConsoleOutput).
        _ausgabe = new ConsoleOutput(BuildPrompt);

        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddProvider(
                new ConsoleOutputLoggerProvider(
                    _ausgabe,
                    verbose ? LogLevel.Trace : LogLevel.Information));

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
            Console.Write("WebSocket URI (Enter: host-meta der Domain, sonst wss://{domain}:5443/ws): ");
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

            // XEP-0308: Berichtigt die letzte Nachricht an den aktuellen
            // Gesprächspartner. Was hier steht, ist der vollständige neue Text
            // und nicht die Änderung daran.
            case "/fix" or "/korr":
                if (args.Length == 0)
                {
                    Console.WriteLine("Syntax: /fix <richtiger text>");
                }
                else if (await client.CorrectLastMessageAsync(args) is null)
                {
                    Console.WriteLine(client.CurrentChatPartner is null
                                          ? "Kein Empfänger gesetzt. Nutze /to <jid>"
                                          : "An diesen Empfänger ist noch nichts hinausgegangen.");
                }
                else
                {
                    Console.WriteLine($"  ✎ Berichtigt an {GetShortJid(client.CurrentChatPartner!)}");
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

            case "/csi":
                await ProcessClientStateCommandAsync(args);
                break;

            case "/omemo":
                await ProcessOmemoCommandAsync(args);
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

    /// <summary>
    /// XEP-0384: <c>/omemo an</c>, <c>/omemo fingerabdruecke</c>,
    /// <c>/omemo vertrauen &lt;jid&gt; &lt;geraet&gt;</c> und
    /// <c>/omemo an &lt;jid&gt; &lt;text&gt;</c>.
    /// </summary>
    private static async Task ProcessOmemoCommandAsync(String args)
    {

        var client = _client!;
        var teile  = args.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var befehl = teile.Length > 0 ? teile[0].ToLowerInvariant() : "";

        switch (befehl)
        {

            case "an" when teile.Length == 1:
            {

                // Der Speicher liegt neben der Anwendung und trägt den JID im
                // Namen: Zwei Konten auf demselben Rechner sind zwei Geräte
                // und dürfen sich keinen Fingerabdruck teilen.
                var datei = Path.Combine(AppContext.BaseDirectory,
                                         $"omemo-{client.BareJid.Replace('@', '_')}.json");

                if (await client.EnableOmemoAsync(new OmemoFileStore(datei)))
                {
                    Console.WriteLine($"[+] OMEMO an. Gerät {client.Omemo!.Identity.DeviceId}");
                    Console.WriteLine($"    Eigener Fingerabdruck: {Gruppiert(client.Omemo.Fingerprint)}");
                    Console.WriteLine($"    Speicher: {datei}");
                    Console.WriteLine("    Die Datei ist NICHT verschlüsselt - wer sie liest, liest mit.");
                }
                else
                    Console.WriteLine("[!] OMEMO liess sich nicht einschalten - der Server nimmt " +
                                      "die Geräteliste nicht an.");

                return;

            }

            case "an" when teile.Length >= 3:
            {

                if (!client.OmemoEnabled)
                {
                    Console.WriteLine("[!] Erst /omemo an.");
                    return;
                }

                var uebersprungen = await client.SendEncryptedMessageAsync(teile[1], teile[2]);

                Console.WriteLine($"[→] verschlüsselt an {teile[1]}");

                // Wer nicht mitlesen kann, wird genannt. Ein Absender, der das
                // nicht erfährt, hält sein Gespräch für geführt.
                foreach (var u in uebersprungen)
                    Console.WriteLine($"    ✗ {u.Jid}/{u.DeviceId}: {u.Reason}");

                return;

            }

            case "fingerabdruecke" or "fp":
            {

                if (!client.OmemoEnabled)
                {
                    Console.WriteLine("[!] Erst /omemo an.");
                    return;
                }

                Console.WriteLine($"Eigener Fingerabdruck ({client.Omemo!.Identity.DeviceId}):");
                Console.WriteLine($"  {Gruppiert(client.Omemo.Fingerprint)}");

                var bekannte = client.Omemo.KnownDevices();

                if (bekannte.Count == 0)
                {
                    Console.WriteLine("Bekannte Geräte: noch keine.");
                    return;
                }

                Console.WriteLine("Bekannte Geräte:");

                foreach (var d in bekannte.OrderBy(d => d.BareJid).ThenBy(d => d.DeviceId))
                    Console.WriteLine($"  {Zeichen(d.Trust)} {d.BareJid}/{d.DeviceId}\n" +
                                      $"      {Gruppiert(d.Fingerprint)}");

                Console.WriteLine("\n  ✓ bestätigt   ? unentschieden   ✗ abgelehnt");
                Console.WriteLine("  Vergleiche den Fingerabdruck über einen anderen Weg, nicht hier.");

                return;

            }

            case "vertrauen" or "ablehnen" when teile.Length >= 3:
            {

                if (!client.OmemoEnabled)
                {
                    Console.WriteLine("[!] Erst /omemo an.");
                    return;
                }

                if (!UInt32.TryParse(teile[2], out var geraet))
                {
                    Console.WriteLine("[!] Die Gerätekennung ist eine Zahl.");
                    return;
                }

                var entscheidung = befehl == "vertrauen" ? OmemoTrust.Trusted : OmemoTrust.Distrusted;

                Console.WriteLine(client.Omemo!.SetTrust(teile[1], geraet, entscheidung)
                                      ? $"[*] {teile[1]}/{geraet}: {entscheidung}"
                                      : "[!] Dieses Gerät ist unbekannt - über einen Schlüssel, den " +
                                        "man nie gesehen hat, lässt sich nicht entscheiden.");

                return;

            }

            default:
                Console.WriteLine("/omemo an                        OMEMO einschalten");
                Console.WriteLine("/omemo an <jid> <text>           verschlüsselt senden");
                Console.WriteLine("/omemo fingerabdruecke           eigenen und bekannte anzeigen");
                Console.WriteLine("/omemo vertrauen <jid> <geraet>  Gerät bestätigen");
                Console.WriteLine("/omemo ablehnen <jid> <geraet>   Gerät ablehnen");
                return;

        }

    }

    /// <summary>
    /// Ein Fingerabdruck in Achtergruppen - so vergleicht ihn ein Mensch, ohne
    /// die Stelle zu verlieren.
    /// </summary>
    private static String Gruppiert(String fingerprint)
        => String.Join(" ", Enumerable.Range(0, fingerprint.Length / 8)
                                      .Select(i => fingerprint.Substring(i * 8, 8)));

    private static String Zeichen(OmemoTrust trust)
        => trust switch {
               OmemoTrust.Trusted     => "✓",
               OmemoTrust.Distrusted  => "✗",
               _                      => "?"
           };

    /// <summary>
    /// XEP-0352: <c>/csi</c> zeigt den Zustand, <c>/csi aktiv|inaktiv</c>
    /// meldet ihn dem Server.
    /// </summary>
    private static async Task ProcessClientStateCommandAsync(string args)
    {

        var client = _client!;

        var gewuenscht = args.Trim().ToLowerInvariant() switch {
                             "inaktiv" or "inactive" or "off"  => (bool?) false,
                             "aktiv"   or "active"   or "on"   => true,
                             _                                 => null
                         };

        if (gewuenscht is null)
        {

            Console.WriteLine("Client State Indication (XEP-0352):");
            Console.WriteLine($"  Vom Server angekündigt: {(client.SupportsClientStateIndication ? "ja" : "nein")}");
            Console.WriteLine($"  Zustand: {(client.IsActive ? "aktiv" : "inaktiv")}");

            if (args.Trim().Length > 0)
                Console.WriteLine("  Verwendung: /csi [aktiv|inaktiv]");

            return;

        }

        if (!await client.SetActiveAsync(gewuenscht.Value))
        {
            Console.WriteLine("[!] Der Server bietet keine Client State Indication an.");
            return;
        }

        Console.WriteLine(gewuenscht.Value
                              ? "[*] Aktiv - der Server schickt wieder alles."
                              : "[*] Inaktiv - der Server hält zurück, was warten kann.");

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

        using var sperre = Ausgabe();

        Console.ForegroundColor = ConsoleColor.Cyan;

        // Eine nachgereichte Nachricht bekommt ihr Datum dazu (XEP-0203): Sie
        // kann von gestern sein, und eine blosse Uhrzeit sähe aus wie heute.
        Console.Write(message.IsDelayed
                          ? $"[{message.Timestamp:dd.MM. HH:mm:ss}] "
                          : $"[{message.Timestamp:HH:mm:ss}] ");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"{GetShortJid(message.From)}");

        // XEP-0308: Eine Konsole kann Geschriebenes nicht zurücknehmen - die
        // Korrektur erscheint deshalb als eigene Zeile und sagt dazu, dass sie
        // eine ist. Das ist ehrlicher als sie zu verschweigen: Der Empfänger
        // sieht beide Fassungen und weiss, welche gilt.
        if (message.IsCorrection)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write(" ✎");
            Console.ForegroundColor = ConsoleColor.Green;
        }

        Console.Write(": ");
        Console.ResetColor();
        Console.Write(message.Body);

        if (message.IsDelayed)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  (nachgereicht)");
            Console.ResetColor();
        }

        Console.WriteLine();


    }

    private static void HandleChatState(string from, ChatState state)
    {

        var shortFrom = GetShortJid(from);

        if (state == ChatState.Composing)
        {
            using var sperre = Ausgabe();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"✏️ {shortFrom} tippt...");
            Console.ResetColor();
        }
        else if (state == ChatState.Paused)
        {
            using var sperre = Ausgabe();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"⏸️ {shortFrom} hat aufgehört zu tippen");
            Console.ResetColor();
        }

    }

    private static void HandleReceipt(string from, string messageId)
    {

        using var sperre = Ausgabe();
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"✓ Zugestellt an {GetShortJid(from)}");
        Console.ResetColor();

    }

    private static void HandleCarbon(CarbonMessage carbon)
    {

        var timestamp = DateTime.Now.ToString("HH:mm:ss");

        using var sperre = Ausgabe();
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

    }

    private static void HandleChatMarker(ChatMarker marker)
    {

        using var sperre = Ausgabe();
        var shortFrom = GetShortJid(marker.From);
        var symbol    = ChatMarkers.GetSymbol(marker.Type);

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"{symbol} {shortFrom}: {marker.Type} (Msg: {marker.MessageId[..Math.Min(12, marker.MessageId.Length)]}...)");
        Console.ResetColor();

    }

    private static void HandlePubSubEvent(PubSubEvent evt)
    {

        using var sperre = Ausgabe();
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

    }

    private static void HandlePresence(string from, string type)
    {

        if (_showRawXml) return; // Bei Raw-Mode wird das schon angezeigt

        // Eigene Presence ignorieren
        if (JidUtilities.Bare(from).Equals(_client!.BareJid, StringComparison.OrdinalIgnoreCase))
            return;

        using var sperre = Ausgabe();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {GetShortJid(from)} → {type}");
        Console.ResetColor();

    }

    private static void HandleError(string error)
    {

        using var sperre = Ausgabe();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[!] {error}");
        Console.ResetColor();

    }

    private static void HandleRawXml(string xml)
    {

        if (!_showRawXml) return;

        using var sperre = Ausgabe();
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine($"[XML] {xml.Trim()}");
        Console.ResetColor();

    }

    #endregion

    #region Ausgabe-Hilfsfunktionen

    private static void WriteSystemMessage(string message)
    {
        using var sperre = Ausgabe();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[*] {message}");
        Console.ResetColor();
    }

    private static void WriteWarning(string message)
    {
        using var sperre = Ausgabe();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[!] {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Eröffnet einen Ausgabebereich: Die angefangene Eingabezeile weicht,
    /// beim Verlassen steht die Eingabeaufforderung wieder da - und dazwischen
    /// gehört die Konsole dem Aufrufer allein.
    /// </summary>
    /// <remarks>
    /// Hier standen bis D58 zwei getrennte Aufrufe, die jede
    /// Ereignisbehandlung selbst um ihre Ausgabe legen musste: Zeile löschen,
    /// schreiben, Eingabeaufforderung nachziehen. Elfmal dieselben zwei Zeilen,
    /// und wer eine davon vergass, merkte es erst im Betrieb. Vor allem aber
    /// fehlte die Sperre dazwischen - und der Logger schrieb ohnehin daran
    /// vorbei.
    ///
    /// Vor dem Verbinden gibt es die gemeinsame Ausgabe noch nicht; dann ist
    /// auch keine Eingabezeile zu retten, und der Bereich tut nichts.
    /// </remarks>
    private static IDisposable Ausgabe()
        => _ausgabe?.Begin() ?? Nichts.Instanz;

    /// <summary>Ein Ausgabebereich, der nichts tut.</summary>
    private sealed class Nichts : IDisposable
    {
        internal static readonly Nichts Instanz = new();
        public void Dispose() { }
    }

    private static string BuildPrompt()
        => _client?.CurrentChatPartner != null
               ? $"[{GetShortJid(_client.CurrentChatPartner)}] > "
               : "> ";

    /// <summary>
    /// Schreibt etwas, das ungefragt kommt, und stellt die Eingabezeile wieder
    /// her. Vor <see cref="_ausgabe"/> - also vor dem Verbinden - gibt es noch
    /// keine Eingabeaufforderung, die zu retten waere.
    /// </summary>
    private static void Melden(Action<TextWriter> ausgabe)
    {

        if (_ausgabe is not null)
            _ausgabe.Write(ausgabe);

        else
            ausgabe(Console.Out);

    }

    private static void WritePrompt() => _ausgabe?.WritePrompt();

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
  /fix <text>        Letzte Nachricht berichtigen (XEP-0308)
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
  /csi [aktiv|inaktiv]  Client State Indication (XEP-0352)
  /omemo …        Ende-zu-Ende-Verschlüsselung (XEP-0384), /omemo für die Hilfe
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
