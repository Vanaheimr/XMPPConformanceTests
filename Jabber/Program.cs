/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Ratatoskr <https://www.github.com/Vanaheimr/Ratatoskr>
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

using org.GraphDefined.Vanaheimr.Ratatoskr.ConsoleUI;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr;

/// <summary>
/// Console front end for the <see cref="XMPPClient"/>.
///
/// This class holds nothing but user interface: command line parsing,
/// command dispatch and presentation. The whole session logic (chat
/// partners, contact requests, composite operations) lies in the
/// <see cref="XMPPClient"/>.
/// </summary>
class Program
{

    #region Data

    private static XMPPClient? _client;
    /// <summary>
    /// The shared output: events, system notices and the log all go through
    /// the same lock and leave the input line whole.
    /// </summary>
    private static ConsoleOutput? _output;

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
            Console.WriteLine("Error: JID and password required");
            return;
        }

        // Everything that goes to the console goes through the same door - the
        // log as well. An AddSimpleConsole would write into the middle of the
        // half-typed input line and leave the user without a prompt (see
        // ConsoleOutput).
        _output = new ConsoleOutput(BuildPrompt);

        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddProvider(
                new ConsoleOutputLoggerProvider(
                    _output,
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
            Console.WriteLine("\n[*] Ended.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[!] Error: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"    Inner: {ex.InnerException.Message}");
        }
        finally
        {
            if (_client != null)
                await _client.DisposeAsync();
        }

    }

    #region Wiring up the display

    private static void WireUpUserInterface(XMPPClient client)
    {

        client.OnMessage           += HandleMessage;
        client.OnCarbonMessage     += HandleCarbon;
        client.OnChatState         += HandleChatState;
        client.OnChatMarker        += HandleChatMarker;
        client.OnReceiptReceived   += HandleReceipt;
        client.OnPresenceChanged   += HandlePresence;
        client.OnPubSubEvent       += HandlePubSubEvent;
        client.OnPubSubSubscriptionRequest += HandlePubSubRequest;
        client.OnError             += HandleError;
        client.OnRawXml            += HandleRawXml;

        client.OnSpoofingAttempt   += msg => WriteWarning($"⚠️ SPOOFING: {msg}");

        client.OnStateChanged += (oldState, newState) =>
        {
            switch (newState)
            {
                case ConnectionState.Reconnecting:
                    WriteSystemMessage("🔄 Connection lost, trying to reconnect...");
                    break;
                case ConnectionState.Connected when oldState == ConnectionState.Reconnecting:
                    WriteSystemMessage("✅ Reconnect succeeded!");
                    break;
                case ConnectionState.Disconnected when oldState == ConnectionState.Reconnecting:
                    WriteWarning("❌ Reconnect failed");
                    break;
            }
        };

        client.OnCapsDiscovered += (from, info) =>
        {
            if (_showRawXml)
                WriteSystemMessage($"[Caps] {from}: {string.Join(", ", info.Identities)}");
        };

        client.OnRosterItemAdded   += item => WriteSystemMessage($"Contact added: {item.DisplayName}");
        client.OnRosterItemRemoved += jid  => WriteSystemMessage($"Contact removed: {jid}");

        client.OnSubscriptionRequest += (from, status) =>
        {
            WriteSystemMessage($"📩 Contact request from {from}: {status}");
            WriteSystemMessage($"   Use /accept {from} or /deny {from}");
        };

    }

    #endregion

    #region Command line parsing

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
            Console.Write("Password: ");
            password = ReadPassword();
            Console.WriteLine();
        }

        if (string.IsNullOrEmpty(wsUri))
        {
            Console.Write("WebSocket URI (Enter: host-meta of the domain, otherwise wss://{domain}:5443/ws): ");
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

    #region Input loop and commands

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
                    Console.WriteLine("No recipient set. Use /msg <jid> <message> or /to <jid>");
                else
                    Console.WriteLine($"  → Sent to {GetShortJid(_client.CurrentChatPartner!)}");
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
                                      ? "Chat recipient reset"
                                      : $"Chatting with: {client.CurrentChatPartner}");
                break;

            case "/msg" or "/m":
                var msgParts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (msgParts.Length < 2)
                {
                    Console.WriteLine("Syntax: /msg <jid> <message>");
                }
                else
                {
                    await client.SendMessageAsync(msgParts[0], msgParts[1]);
                    Console.WriteLine($"  → Sent to {GetShortJid(msgParts[0])}");
                }
                break;

            // XEP-0308: corrects the last message to the current conversation
            // partner. What stands here is the complete new text and not the
            // change to it.
            case "/fix" or "/corr":
                if (args.Length == 0)
                {
                    Console.WriteLine("Syntax: /fix <correct text>");
                }
                else if (await client.CorrectLastMessageAsync(args) is null)
                {
                    Console.WriteLine(client.CurrentChatPartner is null
                                          ? "No recipient set. Use /to <jid>"
                                          : "Nothing has gone out to this recipient yet.");
                }
                else
                {
                    Console.WriteLine($"  ✎ Corrected to {GetShortJid(client.CurrentChatPartner!)}");
                }
                break;

            case "/status" or "/s":
                await ProcessStatusCommandAsync(args);
                break;

            // === ROSTER COMMANDS ===

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
                    Console.WriteLine($"Contact removed: {args.Trim()}");
                }
                break;

            case "/accept":
                var accepted = await client.AcceptSubscriptionAsync(args);
                Console.WriteLine(accepted == null
                                      ? "No pending contact requests."
                                      : $"Contact request accepted: {accepted}");
                break;

            case "/deny":
                var denied = await client.DenySubscriptionAsync(args);
                Console.WriteLine(denied == null
                                      ? "No pending contact requests."
                                      : $"Contact request denied: {denied}");
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
                    Console.WriteLine("No recipient set. Use /to <jid>");
                else
                    Console.WriteLine($"⌨️ Typing indicator sent to {GetShortJid(client.CurrentChatPartner!)}");
                break;

            case "/paused":
                if (!await client.SendChatStateAsync(ChatState.Paused))
                    Console.WriteLine("No recipient set. Use /to <jid>");
                break;

            case "/gone":
                var left = await client.LeaveChatAsync();
                Console.WriteLine(left == null ? "No chat active." : "Chat ended");
                break;

            // === XEP-0060: PUBSUB ===

            case "/pubsub":
                await ProcessPubSubCommandAsync(args);
                break;

            // === OTHERS ===

            case "/carbons":
                Console.WriteLine(client.CarbonsEnabled
                                      ? "✓ Message Carbons are ENABLED"
                                      : "✗ Message Carbons are NOT enabled");
                break;

            case "/raw":
                _showRawXml = !_showRawXml;
                Console.WriteLine($"Raw XML display: {(_showRawXml ? "ON" : "OFF")}");
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
                Console.WriteLine("Server features:");
                foreach (var feature in client.ServerFeatures)
                    Console.WriteLine($"  {feature}");

                Console.WriteLine("\nLocally supported features:");
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
                    Console.WriteLine("[*] Already connected. Disconnect first with /disconnect");
                }
                else
                {
                    Console.WriteLine("[*] Manual reconnect...");
                    await client.ConnectAsync(ct);
                }
                break;

            case "/disconnect":
                Console.WriteLine("[*] Disconnecting...");
                await client.DisconnectAsync();
                Console.WriteLine("[+] Disconnected");
                break;

            default:
                Console.WriteLine($"Unknown command: {command}. Type /help for help.");
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
            Console.WriteLine("Status has to be: available, away, chat, dnd, xa");
            return;
        }

        await _client!.SetPresenceAsync(show, statusText);
        Console.WriteLine($"Status: {show ?? "available"} {statusText ?? ""}");

    }

    private static async Task ProcessPingCommandAsync(string args, CancellationToken ct)
    {

        var target        = string.IsNullOrEmpty(args) ? null : args.Trim();
        var targetDisplay = target ?? "Server";

        Console.WriteLine($"[*] Ping to {targetDisplay}...");
        var rtt = await _client!.PingAsync(target, ct);

        Console.WriteLine(rtt.HasValue
                              ? $"[+] Pong from {targetDisplay}: {rtt.Value.TotalMilliseconds:F1}ms"
                              : $"[!] Timeout - no answer from {targetDisplay}");

    }

    private static async Task ProcessDiscoCommandAsync(string args, CancellationToken ct)
    {

        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            Console.WriteLine("Disco commands:");
            Console.WriteLine("  /disco info <jid>    query features");
            Console.WriteLine("  /disco items <jid>   query services/items");
            Console.WriteLine("  /disco server        query the server features");
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

                Console.WriteLine($"[*] Disco#info for {jid}...");
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
                        Console.WriteLine($"  ... and {info.Features.Count - 20} more");
                }
                else
                {
                    Console.WriteLine("[!] No answer or timeout");
                }
                break;

            case "items":
                Console.WriteLine($"[*] Disco#items for {jid}...");
                var items = await _client!.DiscoverItemsAsync(jid, ct);

                if (items != null)
                {
                    Console.WriteLine($"Items ({items.Items.Count}):");
                    foreach (var item in items.Items)
                        Console.WriteLine($"  {item}");
                }
                else
                {
                    Console.WriteLine("[!] No answer or timeout");
                }
                break;

            default:
                Console.WriteLine($"Unknown disco command: {subCommand}");
                break;
        }

    }

    private static async Task ProcessMarkCommandAsync(string args)
    {

        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            Console.WriteLine("Usage: /mark <received|displayed|ack> [message-id]");
            Console.WriteLine("       /mark displayed (for the last message to the current recipient)");
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
            Console.WriteLine($"[!] Unknown marker type: {parts[0]}");
            return;
        }

        if (_client!.CurrentChatPartner == null)
        {
            Console.WriteLine("No recipient set. Use /to <jid>");
            return;
        }

        var messageId = parts.Length > 1 ? parts[1] : null;
        var marked    = await _client.SendMarkerAsync(markerType.Value, messageId);

        Console.WriteLine(marked == null
                              ? "[!] No message id given and no last message known"
                              : $"[+] {ChatMarkers.GetSymbol(markerType.Value)} marker sent");

    }

    /// <summary>
    /// XEP-0384: <c>/omemo on</c>, <c>/omemo fingerprints</c>,
    /// <c>/omemo trust &lt;jid&gt; &lt;device&gt;</c> and
    /// <c>/omemo on &lt;jid&gt; &lt;text&gt;</c>.
    /// </summary>
    private static async Task ProcessOmemoCommandAsync(String args)
    {

        var client  = _client!;
        var parts   = args.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var command = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";

        switch (command)
        {

            case "on" when parts.Length == 1:
            {

                // The store lies next to the application and carries the JID in
                // its name: two accounts on the same machine are two devices
                // and must not share a fingerprint.
                var file = Path.Combine(AppContext.BaseDirectory,
                                        $"omemo-{client.BareJid.Replace('@', '_')}.json");

                if (await client.EnableOmemoAsync(new OmemoFileStore(file)))
                {
                    Console.WriteLine($"[+] OMEMO on. Device {client.Omemo!.Identity.DeviceId}");
                    Console.WriteLine($"    Own fingerprint: {Grouped(client.Omemo.Fingerprint)}");
                    Console.WriteLine($"    Store: {file}");
                    Console.WriteLine("    The file is NOT encrypted - whoever reads it reads along.");
                }
                else
                    Console.WriteLine("[!] OMEMO could not be switched on - the server does not " +
                                      "accept the device list.");

                return;

            }

            case "on" when parts.Length >= 3:
            {

                if (!client.OmemoEnabled)
                {
                    Console.WriteLine("[!] /omemo on first.");
                    return;
                }

                var skipped = await client.SendEncryptedMessageAsync(parts[1], parts[2]);

                Console.WriteLine($"[→] encrypted to {parts[1]}");

                // Whoever cannot read along is named. A sender who does not
                // learn of it takes their conversation for held.
                foreach (var u in skipped)
                    Console.WriteLine($"    ✗ {u.Jid}/{u.DeviceId}: {u.Reason}");

                return;

            }

            case "fingerprints" or "fp":
            {

                if (!client.OmemoEnabled)
                {
                    Console.WriteLine("[!] /omemo on first.");
                    return;
                }

                Console.WriteLine($"Own fingerprint ({client.Omemo!.Identity.DeviceId}):");
                Console.WriteLine($"  {Grouped(client.Omemo.Fingerprint)}");

                var known = client.Omemo.KnownDevices();

                if (known.Count == 0)
                {
                    Console.WriteLine("Known devices: none yet.");
                    return;
                }

                Console.WriteLine("Known devices:");

                foreach (var d in known.OrderBy(d => d.BareJid).ThenBy(d => d.DeviceId))
                    Console.WriteLine($"  {Symbol(d.Trust)} {d.BareJid}/{d.DeviceId}\n" +
                                      $"      {Grouped(d.Fingerprint)}");

                Console.WriteLine("\n  ✓ confirmed   ? undecided   ✗ refused");
                Console.WriteLine("  Compare the fingerprint over another way, not here.");

                return;

            }

            case "trust" or "distrust" when parts.Length >= 3:
            {

                if (!client.OmemoEnabled)
                {
                    Console.WriteLine("[!] /omemo on first.");
                    return;
                }

                if (!UInt32.TryParse(parts[2], out var device))
                {
                    Console.WriteLine("[!] The device id is a number.");
                    return;
                }

                var decision = command == "trust" ? OmemoTrust.Trusted : OmemoTrust.Distrusted;

                Console.WriteLine(client.Omemo!.SetTrust(parts[1], device, decision)
                                      ? $"[*] {parts[1]}/{device}: {decision}"
                                      : "[!] This device is unknown - about a key one has never " +
                                        "seen no decision can be made.");

                return;

            }

            default:
                Console.WriteLine("/omemo on                        switch OMEMO on");
                Console.WriteLine("/omemo on <jid> <text>           send encrypted");
                Console.WriteLine("/omemo fingerprints              show the own and the known ones");
                Console.WriteLine("/omemo trust <jid> <device>      confirm a device");
                Console.WriteLine("/omemo distrust <jid> <device>   refuse a device");
                return;

        }

    }

    /// <summary>
    /// A fingerprint in groups of eight - that is how a human compares it
    /// without losing their place.
    /// </summary>
    private static String Grouped(String fingerprint)
        => String.Join(" ", Enumerable.Range(0, fingerprint.Length / 8)
                                      .Select(i => fingerprint.Substring(i * 8, 8)));

    private static String Symbol(OmemoTrust trust)
        => trust switch {
               OmemoTrust.Trusted     => "✓",
               OmemoTrust.Distrusted  => "✗",
               _                      => "?"
           };

    /// <summary>
    /// XEP-0352: <c>/csi</c> shows the state, <c>/csi active|inactive</c>
    /// reports it to the server.
    /// </summary>
    private static async Task ProcessClientStateCommandAsync(string args)
    {

        var client = _client!;

        var wanted = args.Trim().ToLowerInvariant() switch {
                         "inactive" or "i" or "off"  => (bool?) false,
                         "active"   or "a" or "on"   => true,
                         _                           => null
                     };

        if (wanted is null)
        {

            Console.WriteLine("Client State Indication (XEP-0352):");
            Console.WriteLine($"  Announced by the server: {(client.SupportsClientStateIndication ? "yes" : "no")}");
            Console.WriteLine($"  State: {(client.IsActive ? "active" : "inactive")}");

            if (args.Trim().Length > 0)
                Console.WriteLine("  Usage: /csi [active|inactive]");

            return;

        }

        if (!await client.SetActiveAsync(wanted.Value))
        {
            Console.WriteLine("[!] The server does not offer Client State Indication.");
            return;
        }

        Console.WriteLine(wanted.Value
                              ? "[*] Active - the server sends everything again."
                              : "[*] Inactive - the server holds back what can wait.");

    }

    private static async Task ProcessStreamManagementCommandAsync(string args)
    {

        var client = _client!;

        Console.WriteLine("Stream Management:");
        Console.WriteLine($"  Configured: {client.StreamManagementEnabled}");
        Console.WriteLine($"  Active: {client.StreamManagement?.IsEnabled == true}");

        if (client.StreamManagement?.IsEnabled == true)
        {
            var sm = client.StreamManagement;
            Console.WriteLine($"  Inbound: {sm.InboundCount}");
            Console.WriteLine($"  Outbound: {sm.OutboundCount}");
            Console.WriteLine($"  Unacknowledged: {sm.UnackedCount}");

            Console.WriteLine("[*] Requesting an ack...");
            await client.RequestAckAsync();
        }

        if (string.IsNullOrEmpty(args))
            return;

        switch (args.ToLower())
        {
            // A warning stood here that SM leads "to disconnects with some
            // servers (ejabberd)". The cause was our own faulty counting, not
            // the server; it is fixed and shown against Prosody 13. SM is the
            // default by now.
            case "on":
                client.StreamManagementEnabled = true;
                Console.WriteLine("[*] SM enabled (takes effect after a reconnect)");
                break;

            case "off":
                client.StreamManagementEnabled = false;
                Console.WriteLine("[*] SM disabled (takes effect after a reconnect)");
                break;
        }

    }

    private static void ProcessKeepaliveCommand(string args)
    {

        var client = _client!;

        Console.WriteLine("Keepalive status:");
        Console.WriteLine($"  Enabled: {client.KeepaliveEnabled}");
        Console.WriteLine($"  Interval: {client.KeepaliveInterval.TotalSeconds}s");
        Console.WriteLine($"  Method: {(client.StreamManagement?.IsEnabled == true ? "Stream Management <r/>" : "XEP-0199 Ping")}");

        if (string.IsNullOrEmpty(args))
            return;

        if (args.ToLower() is "off" or "0")
        {
            client.KeepaliveEnabled = false;
            Console.WriteLine("[*] Keepalive disabled");
        }
        else if (args.ToLower() is "on" or "1")
        {
            client.KeepaliveEnabled = true;
            Console.WriteLine("[*] Keepalive enabled (takes effect after a reconnect)");
        }
        else if (int.TryParse(args, out var seconds) && seconds > 0)
        {
            client.KeepaliveInterval = TimeSpan.FromSeconds(seconds);
            Console.WriteLine($"[*] Keepalive interval set to {seconds}s (takes effect after a reconnect)");
        }

    }

    private static async Task ProcessPubSubCommandAsync(string args)
    {

        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            Console.WriteLine("PubSub commands:");
            Console.WriteLine("  /pubsub sub <node> [jid]     subscribe to a node");
            Console.WriteLine("  /pubsub unsub <node> [jid] [subid]  end a subscription");
            Console.WriteLine("  /pubsub subs                 own subscriptions with subid (what we know)");
            Console.WriteLine("  /pubsub sync [jid]           fetch the subscriptions from the service (what it says)");
            Console.WriteLine("  /pubsub opts <node> [subid]  options of the subscription");
            Console.WriteLine("  /pubsub deliver <node> <on|off> [subid]  delivery on/off");
            Console.WriteLine("  /pubsub pub <node> <id> <data>  publish an item");
            Console.WriteLine("  /pubsub get <node> [max]     fetch items");
            Console.WriteLine("  /pubsub create <node> [access]  create a node");
            Console.WriteLine("  /pubsub cfg <node>           node settings");
            Console.WriteLine("  /pubsub access <node> <open|presence|whitelist|roster>  change the access");
            Console.WriteLine("  /pubsub groups <node> [group...]  roster groups for 'roster' (empty: all)");
            Console.WriteLine("  /pubsub request <node> <jid> <yes|no>  answer a subscription request");
            Console.WriteLine("  /pubsub roles <node>         who is what at this node");
            Console.WriteLine("  /pubsub role <node> <jid> <role>  grant or take back a role");
            Console.WriteLine("  /pubsub subscribers <node>   who hangs on this node (alias: who)");
            Console.WriteLine("  /pubsub kick <node> <jid> [subid]  remove a subscriber (alias: remove)");
            Console.WriteLine("  /pubsub retract <node> <id>  take back a single item");
            Console.WriteLine("  /pubsub purge <node>         empty a node, keep the subscribers");
            Console.WriteLine("  /pubsub delete <node>        delete a node");
            Console.WriteLine();
            Console.WriteLine("  Without a <jid> the request goes to pubsub.<domain>. A PEP node");
            Console.WriteLine("  belongs to an account - then its bare JID stands there.");
            return;
        }

        var subCmd = parts[0].ToLower();
        var nodeId = parts.Length > 1 ? parts[1] : "";

        string[] nodeCommands = ["sub", "subscribe", "unsub", "unsubscribe",
                                 "pub", "publish", "get", "items", "create", "delete",
                                 "opts", "options", "deliver", "cfg", "nodecfg", "access",
                                 "roles", "affiliations", "role",
                                 "subscribers", "who", "kick", "remove",
                                 "purge", "empty", "retract", "undo",
                                 "groups", "rostergroups", "request", "authorize"];

        if (nodeCommands.Contains(subCmd) && string.IsNullOrEmpty(nodeId))
        {
            Console.WriteLine($"Syntax: /pubsub {subCmd} <node>");
            return;
        }

        switch (subCmd)
        {
            // A target can be given, because a PEP node belongs to an account
            // and not to the PubSub component of the domain.
            case "sub" or "subscribe":
                var sub = await _client!.PubSubSubscribeAsync(nodeId, parts.Length > 2 ? parts[2] : null);
                Console.WriteLine(sub is not null
                                      ? $"📢 Subscribed: {nodeId}" +
                                        (sub.SubId is not null ? $" (subid {sub.SubId})" : "")
                                      : $"⚠️ Not subscribed: {nodeId} - see the log");
                break;

            case "unsub" or "unsubscribe":
                Console.WriteLine(await _client!.PubSubUnsubscribeAsync(nodeId,
                                                                        parts.Length > 2 ? parts[2] : null,
                                                                        parts.Length > 3 ? parts[3] : null)
                                      ? $"🔕 Subscription ended: {nodeId}"
                                      : $"⚠️ Subscription not ended: {nodeId} - see the log");
                break;

            // The target comes from the subscription itself: whoever has set
            // where they subscribed does not have to say it a second time.
            case "opts" or "options":
                var options = await _client!.PubSubGetOptionsAsync(nodeId,
                                                                   subId: parts.Length > 2 ? parts[2] : null);
                Console.WriteLine(options is not null
                                      ? $"⚙️ {nodeId}: delivery {(options.Deliver ? "on" : "off")}"
                                      : $"⚠️ Options not read: {nodeId} - see the log");
                break;

            case "deliver":
                if (parts.Length < 3 || parts[2].ToLower() is not ("on" or "off"))
                {
                    Console.WriteLine("Syntax: /pubsub deliver <node> <on|off> [subid]");
                    return;
                }
                var on = parts[2].ToLower() == "on";
                Console.WriteLine(await _client!.PubSubSetOptionsAsync(nodeId,
                                                                       new PubSubSubscriptionOptions(on),
                                                                       subId: parts.Length > 3 ? parts[3] : null)
                                      ? $"⚙️ {nodeId}: delivery {(on ? "on" : "off")}"
                                      : $"⚠️ Option not set: {nodeId} - see the log");
                break;

            // Two different questions, so two commands: 'subs' shows what this
            // client believes it knows, 'sync' asks the service. After a
            // connection break the first is empty and the second the only way
            // back to the ids.
            case "sync":
                var fetched = await _client!.PubSubGetSubscriptionsAsync(parts.Length > 1 ? parts[1] : null);

                if (fetched is null)
                    Console.WriteLine("⚠️ Subscriptions not fetched - see the log");

                else
                {
                    Console.WriteLine($"🔄 {fetched.Count} subscription(s) taken over:");
                    foreach (var entry in fetched)
                        Console.WriteLine($"   {entry.NodeId}" +
                                          (entry.SubId is not null ? $" (subid {entry.SubId})" : ""));
                }
                break;

            // With several subscriptions to the same node the id is the only
            // thing telling them apart - whoever wants to unsubscribe has to be
            // able to look it up.
            case "subscriptions" or "subs":
                var subs = _client!.Connection.PubSub!.Subscriptions;

                if (subs.Count == 0)
                    Console.WriteLine("No subscriptions.");

                else
                    foreach (var entry in subs)
                        Console.WriteLine($"   {entry.NodeId} at {entry.ServiceJid}" +
                                          (entry.SubId is not null ? $" (subid {entry.SubId})" : "") +
                                          // An applied-for one stands there too and says what it is:
                                          // without the state it would look like a granted one, and
                                          // the absent events would look like a mistake.
                                          (entry.State != PubSubSubscriptionState.Subscribed
                                               ? $" - {entry.State.ToString().ToLower()}"
                                               : ""));
                break;

            case "pub" or "publish":
                if (parts.Length < 4)
                {
                    Console.WriteLine("Syntax: /pubsub pub <node> <itemId> <payload>");
                    return;
                }
                var itemId  = parts[2];
                var payload = string.Join(' ', parts.Skip(3));
                Console.WriteLine(await _client!.PubSubPublishAsync(nodeId, itemId, $"<data>{XmlEscaping.Escape(payload)}</data>")
                                      ? $"📤 Published: {nodeId}/{itemId}"
                                      : $"⚠️ Not published: {nodeId}/{itemId} - see the log");
                break;

            case "get" or "items":
                int? max = parts.Length > 2 && int.TryParse(parts[2], out var m) ? m : null;
                var entries = await _client!.PubSubGetItemsAsync(nodeId, max);

                if (entries is null)
                    Console.WriteLine($"⚠️ Not fetched: {nodeId} - see the log");

                else
                {
                    Console.WriteLine($"📥 {entries.Count} item(s) from {nodeId}:");
                    foreach (var entry in entries)
                        Console.WriteLine($"   {entry.Id}: {entry.Payload}");
                }
                break;

            // Create and configure in one go: two steps would have a gap in
            // which the node stands open.
            // The model names come from the same place as with 'access' and as
            // in the form. Two of them stood here fixed in the text -
            // everything else silently became 'open', and whoever wrote
            // 'whitelist' got an open node and a success message.
            case "create":

                PubSubNodeConfiguration? access = null;

                if (parts.Length > 2)
                {

                    if (!PubSubNodeConfiguration.TryReadAccessModel(parts[2].ToLower(), out var newModel))
                    {
                        Console.WriteLine("Syntax: /pubsub create <node> [open|presence|whitelist|roster|authorize]");
                        return;
                    }

                    access = new PubSubNodeConfiguration(newModel);

                }

                Console.WriteLine(await _client!.PubSubCreateNodeAsync(nodeId, access)
                                      ? $"➕ Node created: {nodeId}" +
                                        (access is not null
                                             ? $" ({PubSubNodeConfiguration.NameOf(access.AccessModel)})"
                                             : "")
                                      : $"⚠️ Node not created: {nodeId} - see the log");
                break;

            // Who is what at my node - and what am I elsewhere?
            case "roles" or "affiliations":
                var atTheNode = await _client!.PubSubGetNodeAffiliationsAsync(nodeId);

                if (atTheNode is null)
                    Console.WriteLine($"⚠️ Roles not read: {nodeId} - see the log");

                else
                    foreach (var (who, role) in atTheNode)
                        Console.WriteLine($"   {who}: {PubSubAffiliations.NameOf(role)}");
                break;

            case "role":
                if (parts.Length < 4)
                {
                    Console.WriteLine("Syntax: /pubsub role <node> <jid> <owner|publisher|member|outcast|none>");
                    return;
                }

                if (!PubSubAffiliations.TryRead(parts[3].ToLower(), out var wanted))
                {
                    Console.WriteLine($"Unknown role: {parts[3]}");
                    return;
                }

                Console.WriteLine(await _client!.PubSubSetAffiliationAsync(nodeId, parts[2], wanted)
                                      ? $"👤 {parts[2]} is now {parts[3].ToLower()} at {nodeId}"
                                      : $"⚠️ Role not set: {nodeId} - see the log");
                break;

            // The other direction from 'subs': there it says where this client
            // hangs - here who hangs on a node of one's own.
            case "subscribers" or "who":
                var subscribers = await _client!.PubSubGetNodeSubscribersAsync(nodeId);

                if (subscribers is null)
                    Console.WriteLine($"⚠️ Subscribers not read: {nodeId} - see the log");

                else if (subscribers.Count == 0)
                    Console.WriteLine($"Nobody subscribes to {nodeId}.");

                else
                    foreach (var (who, subId, state) in subscribers)
                        Console.WriteLine($"   {who}" +
                                          (subId is not null ? $" (subid {subId})" : "") +
                                          $": {state.ToString().ToLower()}");
                break;

            case "kick" or "remove":
                if (parts.Length < 3)
                {
                    Console.WriteLine("Syntax: /pubsub kick <node> <jid> [subid]");
                    return;
                }

                var which = parts.Length > 3 ? parts[3] : null;

                Console.WriteLine(await _client!.PubSubRemoveSubscriberAsync(nodeId, parts[2], which)
                                      ? $"🚪 {parts[2]} does not subscribe to {nodeId} any more" +
                                        (which is not null ? $" (subid {which})" : " (all subscriptions)")
                                      : $"⚠️ Subscriber not removed: {nodeId} - see the log");
                break;

            case "cfg" or "nodecfg":
                var node = await _client!.PubSubGetNodeConfigAsync(nodeId);
                Console.WriteLine(node is not null
                                      ? $"⚙️ {nodeId}: access {PubSubNodeConfiguration.NameOf(node.AccessModel)}, " +
                                        $"{node.MaxItems} items, storage {(node.PersistItems ? "on" : "off")}"
                                      : $"⚠️ Node not read: {nodeId} - see the log");
                break;

            // The names come from the same place that reads the form as well.
            // Before, two of them stood here fixed in the text - 'whitelist'
            // had existed since D82, and in the help text since D82, and this
            // command refused it.
            case "access":
                if (parts.Length < 3 ||
                    !PubSubNodeConfiguration.TryReadAccessModel(parts[2].ToLower(), out var model))
                {
                    Console.WriteLine("Syntax: /pubsub access <node> <open|presence|whitelist|roster>");
                    return;
                }
                var existing = await _client!.PubSubGetNodeConfigAsync(nodeId);

                if (existing is null)
                {
                    Console.WriteLine($"⚠️ Node not read: {nodeId} - see the log");
                    return;
                }

                // Build on the state that was read and not on the default:
                // otherwise changing the access would reset the storage and the
                // number of items along the way.
                Console.WriteLine(await _client!.PubSubConfigureNodeAsync(nodeId,
                                                                          existing with { AccessModel = model })
                                      ? $"⚙️ {nodeId}: access {PubSubNodeConfiguration.NameOf(model)}"
                                      : $"⚠️ Node not configured: {nodeId} - see the log");
                break;

            // Without groups the whole roster comes in with the access model
            // 'roster' - the command without further words sets exactly that.
            case "groups" or "rostergroups":
                var before = await _client!.PubSubGetNodeConfigAsync(nodeId);

                if (before is null)
                {
                    Console.WriteLine($"⚠️ Node not read: {nodeId} - see the log");
                    return;
                }

                var allowed = parts.Skip(2).ToArray();

                Console.WriteLine(await _client!.PubSubConfigureNodeAsync(nodeId,
                                                                          before with { RosterGroups = allowed })
                                      ? $"⚙️ {nodeId}: groups " +
                                        (allowed.Length == 0 ? "(all)" : String.Join(", ", allowed))
                                      : $"⚠️ Node not configured: {nodeId} - see the log");
                break;

            case "delete":
                Console.WriteLine(await _client!.PubSubDeleteNodeAsync(nodeId)
                                      ? $"➖ Node deleted: {nodeId}"
                                      : $"⚠️ Node not deleted: {nodeId} - see the log");
                break;

            // XEP-0060, section 8.6.2: answer the pending application.
            case "request" or "authorize":
                if (parts.Length < 4 || parts[3].ToLower() is not ("yes" or "no"))
                {
                    Console.WriteLine("Syntax: /pubsub request <node> <jid> <yes|no>");

                    lock (_pendingRequests)
                        foreach (var pending in _pendingRequests)
                            Console.WriteLine($"   pending: {pending.SubscriberJid} for {pending.NodeId}");

                    return;
                }

                PubSubSubscribeAuthorization? sought;

                lock (_pendingRequests)
                    sought = _pendingRequests.FirstOrDefault(
                                  a => a.NodeId == nodeId &&
                                       a.SubscriberJid.Equals(parts[2], StringComparison.OrdinalIgnoreCase));

                if (sought is null)
                {
                    Console.WriteLine($"No pending request from {parts[2]} for {nodeId}.");
                    return;
                }

                var yes = parts[3].ToLower() == "yes";

                await _client!.PubSubAnswerSubscriptionRequestAsync(sought, yes, _client.BareJid);

                lock (_pendingRequests)
                    _pendingRequests.Remove(sought);

                Console.WriteLine(yes
                                      ? $"✅ {sought.SubscriberJid} subscribes to {nodeId}"
                                      : $"🚪 {sought.SubscriberJid} stays outside");
                break;

            // A single item instead of all of them: 'retract' takes one back,
            // 'purge' all of them, 'delete' the whole node.
            case "retract" or "undo":
                if (parts.Length < 3)
                {
                    Console.WriteLine("Syntax: /pubsub retract <node> <itemId>");
                    return;
                }
                Console.WriteLine(await _client!.PubSubRetractAsync(nodeId, parts[2])
                                      ? $"🗑️ Retracted: {nodeId}/{parts[2]}"
                                      : $"⚠️ Not retracted: {nodeId}/{parts[2]} - see the log");
                break;

            // The little brother of 'delete': the node stays, its content goes
            // - and the subscribers are kept for it.
            case "purge" or "empty":
                Console.WriteLine(await _client!.PubSubPurgeNodeAsync(nodeId)
                                      ? $"🧹 Node emptied: {nodeId}"
                                      : $"⚠️ Node not emptied: {nodeId} - see the log");
                break;

            default:
                Console.WriteLine($"Unknown PubSub command: {subCmd}");
                break;
        }

    }

    #endregion

    #region Roster display

    private static void PrintRoster(string filter)
    {

        var items = _client!.GetContacts(filter);

        if (items.Count == 0)
        {
            Console.WriteLine("No contacts found.");
            return;
        }

        Console.WriteLine($"\n╔═══ Contacts ({items.Count}) ═══");

        var grouped = items
            .SelectMany(i => i.Groups.DefaultIfEmpty("(No group)"), (item, group) => (item, group))
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
            Console.WriteLine("No contacts online.");
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
            Console.WriteLine("Syntax: /add <jid> [name] [group1,group2,...]");
            return;
        }

        var jid    = parts[0];
        var name   = parts.Length > 1 ? parts[1] : null;
        var groups = parts.Length > 2
                         ? parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries)
                         : null;

        await _client!.AddContactAsync(jid, name, groups);
        Console.WriteLine($"Contact request sent to: {jid}");

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
            Console.WriteLine($"Contact not found: {jid}");
            return;
        }

        Console.WriteLine("\n╔═══ Contact info ═══");
        Console.WriteLine($"║ JID:          {item.Jid}");
        Console.WriteLine($"║ Name:         {item.Name ?? "(not set)"}");
        Console.WriteLine($"║ Subscription: {item.Subscription}");
        Console.WriteLine($"║ Groups:       {(item.Groups.Count > 0 ? string.Join(", ", item.Groups) : "(none)")}");
        Console.WriteLine($"║ Status:       {item.Presence}");
        if (!string.IsNullOrEmpty(item.PresenceStatus))
            Console.WriteLine($"║ Status text:  {item.PresenceStatus}");
        if (item.LastSeen != default)
            Console.WriteLine($"║ Last seen:    {item.LastSeen:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine("╚" + new string('═', 25));

    }

    private static void PrintGroups()
    {

        var groups = _client!.GetGroups().ToList();

        if (groups.Count == 0)
        {
            Console.WriteLine("No groups defined.");
            return;
        }

        Console.WriteLine("\nGroups:");
        foreach (var group in groups)
        {
            var count = _client.GetContactsByGroup(group).Count();
            Console.WriteLine($"  [{group}] - {count} contacts");
        }

    }

    private static void PrintPendingSubscriptions()
    {

        var pending = _client!.PendingSubscriptions;

        if (pending.Count == 0)
        {
            Console.WriteLine("No pending contact requests.");
            return;
        }

        Console.WriteLine("\n📩 Pending contact requests:");
        for (int i = 0; i < pending.Count; i++)
            Console.WriteLine($"  {i + 1}. {pending[i]}");

        Console.WriteLine("\nUse /accept <jid> or /deny <jid>");

    }

    private static void PrintOwnStatus()
    {

        var client = _client!;

        Console.WriteLine($"Logged in as: {client.FullJid}");
        Console.WriteLine($"Bare JID: {client.BareJid}");
        Console.WriteLine($"Status: {client.State}");
        if (client.CurrentChatPartner != null)
            Console.WriteLine($"Chatting with: {client.CurrentChatPartner}");
        Console.WriteLine($"Carbons: {(client.CarbonsEnabled ? "enabled" : "disabled")}");
        Console.WriteLine($"Stream Mgmt: {(client.StreamManagement?.IsEnabled == true ? "enabled" : "disabled")}");
        Console.WriteLine($"Keepalive: {(client.KeepaliveEnabled ? $"every {client.KeepaliveInterval.TotalSeconds}s" : "disabled")}");
        Console.WriteLine($"Transport: WebSocket (RFC 7395) - {client.WebSocketUri}");

    }

    #endregion

    #region Event handlers (presentation)

    private static void HandleMessage(XMPPMessage message)
    {

        using var scope = Output();

        Console.ForegroundColor = ConsoleColor.Cyan;

        // A message handed in late gets its date along (XEP-0203): it can be
        // from yesterday, and a bare time of day would look like today.
        Console.Write(message.IsDelayed
                          ? $"[{message.Timestamp:dd.MM. HH:mm:ss}] "
                          : $"[{message.Timestamp:HH:mm:ss}] ");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"{GetShortJid(message.From)}");

        // XEP-0308: a console cannot take back what has been written - the
        // correction therefore appears as a line of its own and says that it is
        // one. That is more honest than keeping quiet about it: the receiver
        // sees both versions and knows which one holds.
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
            Console.Write("  (handed in late)");
            Console.ResetColor();
        }

        Console.WriteLine();


    }

    private static void HandleChatState(string from, ChatState state)
    {

        var shortFrom = GetShortJid(from);

        if (state == ChatState.Composing)
        {
            using var scope = Output();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"✏️ {shortFrom} is typing...");
            Console.ResetColor();
        }
        else if (state == ChatState.Paused)
        {
            using var scope = Output();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"⏸️ {shortFrom} has stopped typing");
            Console.ResetColor();
        }

    }

    private static void HandleReceipt(string from, string messageId)
    {

        using var scope = Output();
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"✓ Delivered to {GetShortJid(from)}");
        Console.ResetColor();

    }

    private static void HandleCarbon(CarbonMessage carbon)
    {

        var timestamp = DateTime.Now.ToString("HH:mm:ss");

        using var scope = Output();
        Console.ForegroundColor = ConsoleColor.Magenta;

        if (carbon.IsSent)
        {
            // Sent by me on another device
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.Write($"📤 Me → {GetShortJid(carbon.OriginalTo)}: ");
        }
        else
        {
            // Received on another device
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write($"📥 {GetShortJid(carbon.OriginalFrom)} (Carbon): ");
        }

        Console.ResetColor();
        Console.WriteLine(carbon.Body ?? "(no content)");

    }

    private static void HandleChatMarker(ChatMarker marker)
    {

        using var scope = Output();
        var shortFrom = GetShortJid(marker.From);
        var symbol    = ChatMarkers.GetSymbol(marker.Type);

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"{symbol} {shortFrom}: {marker.Type} (Msg: {marker.MessageId[..Math.Min(12, marker.MessageId.Length)]}...)");
        Console.ResetColor();

    }

    private static void HandlePubSubEvent(PubSubEvent evt)
    {

        using var scope = Output();
        Console.ForegroundColor = ConsoleColor.Yellow;

        switch (evt.Type)
        {
            case PubSubEventType.Items:
                Console.WriteLine($"📢 PubSub [{evt.NodeId}]: {evt.Items.Count} item(s)");
                foreach (var item in evt.Items)
                    Console.WriteLine($"   - {item.Id}: {item.Payload[..Math.Min(50, item.Payload.Length)]}...");
                break;

            case PubSubEventType.Retract:
                Console.WriteLine($"🗑️ PubSub [{evt.NodeId}]: item(s) removed: {string.Join(", ", evt.RetractedIds)}");
                break;

            case PubSubEventType.Purge:
                Console.WriteLine($"🧹 PubSub [{evt.NodeId}]: node emptied");
                break;

            case PubSubEventType.Delete:
                Console.WriteLine($"❌ PubSub [{evt.NodeId}]: node deleted");
                break;

            // Ended unasked - and therefore the only event here that is about
            // something this client did not set off.
            case PubSubEventType.SubscriptionEnded:
                Console.WriteLine($"🚪 PubSub [{evt.NodeId}]: subscription ended" +
                                  (evt.SubId is not null ? $" (subid {evt.SubId})" : ""));
                break;

            // The late answer to a question of one's own.
            case PubSubEventType.SubscriptionApproved:
                Console.WriteLine($"✅ PubSub [{evt.NodeId}]: subscription granted" +
                                  (evt.SubId is not null ? $" (subid {evt.SubId})" : ""));
                break;
        }

        Console.ResetColor();

    }

    /// <summary>
    /// XEP-0060, section 8.6.1: somebody asks for a subscription.
    /// </summary>
    /// <remarks>
    /// Shown and not answered: whoever grants it is a human being. The pending
    /// application afterwards stands in <see cref="_pendingRequests"/> and
    /// waits for <c>/pubsub request</c>.
    /// </remarks>
    private static void HandlePubSubRequest(PubSubSubscribeAuthorization application)
    {

        lock (_pendingRequests)
            _pendingRequests.Add(application);

        using var scope = Output();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"🔔 {application.SubscriberJid} would like to subscribe to {application.NodeId}" +
                          (application.SubId is not null ? $" (subid {application.SubId})" : ""));
        Console.WriteLine($"   /pubsub request {application.NodeId} {application.SubscriberJid} <yes|no>");
        Console.ResetColor();

    }

    /// <summary>The applications nobody has answered yet.</summary>
    private static readonly List<PubSubSubscribeAuthorization> _pendingRequests = [];

    private static void HandlePresence(string from, string type)
    {

        if (_showRawXml) return; // in raw mode this is shown already

        // Ignore our own presence
        if (JidUtilities.Bare(from).Equals(_client!.BareJid, StringComparison.OrdinalIgnoreCase))
            return;

        using var scope = Output();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {GetShortJid(from)} → {type}");
        Console.ResetColor();

    }

    private static void HandleError(string error)
    {

        using var scope = Output();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[!] {error}");
        Console.ResetColor();

    }

    private static void HandleRawXml(string xml)
    {

        if (!_showRawXml) return;

        using var scope = Output();
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine($"[XML] {xml.Trim()}");
        Console.ResetColor();

    }

    #endregion

    #region Output helpers

    private static void WriteSystemMessage(string message)
    {
        using var scope = Output();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[*] {message}");
        Console.ResetColor();
    }

    private static void WriteWarning(string message)
    {
        using var scope = Output();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[!] {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Opens an output scope: the line begun gives way, on leaving the prompt
    /// stands there again - and in between the console belongs to the caller
    /// alone.
    /// </summary>
    /// <remarks>
    /// Until D58 two separate calls stood here that every event handler had to
    /// put around its output itself: erase the line, write, draw the prompt
    /// back. Eleven times the same two lines, and whoever forgot one of them
    /// noticed it only in operation. Above all, though, the lock in between
    /// was missing - and the logger wrote past it anyway.
    ///
    /// Before connecting there is no shared output yet; then there is no input
    /// line to save either, and the scope does nothing.
    /// </remarks>
    private static IDisposable Output()
        => _output?.Begin() ?? Nothing.Instance;

    /// <summary>An output scope that does nothing.</summary>
    private sealed class Nothing : IDisposable
    {
        internal static readonly Nothing Instance = new();
        public void Dispose() { }
    }

    private static string BuildPrompt()
        => _client?.CurrentChatPartner != null
               ? $"[{GetShortJid(_client.CurrentChatPartner)}] > "
               : "> ";

    /// <summary>
    /// Writes something that comes unasked and restores the input line. Before
    /// <see cref="_output"/> - that is, before connecting - there is no prompt
    /// yet that would need saving.
    /// </summary>
    private static void Report(Action<TextWriter> output)
    {

        if (_output is not null)
            _output.Write(output);

        else
            output(Console.Out);

    }

    private static void WritePrompt() => _output?.WritePrompt();

    private static string GetShortJid(string jid)
    {
        var slashIndex = jid.IndexOf('/');
        return slashIndex > 0 ? jid[..slashIndex] : jid;
    }

    #endregion

    #region Help texts

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
Messages:
  /to <jid>          set the chat partner (then just type)
  /to                reset the chat partner
  /msg <jid> <text>  send a single message
  /fix <text>        correct the last message (XEP-0308)
  /status [show] [text]  change the status (available/away/chat/dnd/xa)

Contacts (roster):
  /roster [filter]   show all contacts
  /online            only the online contacts
  /add <jid> [name] [g1,g2]  add a contact
  /remove <jid>      remove a contact
  /info <jid>        show the contact details
  /groups            groups with the number of contacts
  /pending           pending contact requests
  /accept [jid]      accept a contact request
  /deny [jid]        deny a contact request

Chat state (XEP-0085):
  /typing    send 'is typing...'
  /paused    'has stopped typing'
  /gone      leave the chat

Chat markers (XEP-0333):
  /mark received [id]   mark as received
  /mark displayed [id]  mark as read
  /mark ack [id]        acknowledge a message

Service discovery (XEP-0030):
  /disco server      query the server features
  /disco info <jid>  query the features of a JID
  /disco items <jid> list the services
  /features          show the server and our own features

PubSub (XEP-0060):
  /pubsub sub <node>              subscribe to a node
  /pubsub unsub <node>            end a subscription
  /pubsub pub <node> <id> <data>  publish an item
  /pubsub get <node> [max]        fetch items
  /pubsub create|delete <node>    create/delete a node
  /pubsub …                       the other twenty, /pubsub for the help

Connection:
  /ping [jid]     send a ping (XEP-0199)
  /sm [on|off]    stream management (XEP-0198, experimental)
  /csi [active|inactive]  client state indication (XEP-0352)
  /omemo …        end-to-end encryption (XEP-0384), /omemo for the help
  /keepalive [s]  show or set the keepalive interval
  /who            show the status
  /carbons        message carbons status
  /raw            toggle the XML debug display
  /reconnect      connect again
  /disconnect     disconnect
  /quit           quit

Features:
  ✓ SCRAM-SHA-1/256 + SASL PLAIN authentication
  ✓ WebSocket transport (RFC 7395)
  ✓ Auto-reconnect with exponential backoff
  ✓ Keepalive (prevents the server timeout)
  ✓ Service Discovery (XEP-0030)
  ✓ Entity Capabilities (XEP-0115)
  ✓ Ping (XEP-0199)
  ✓ Chat Markers (XEP-0333)
  ✓ Receipts (XEP-0184) with spoofing protection
  ✓ Carbons (XEP-0280) with spoofing protection
");
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"
Usage: XmppClient [options]

Options:
  -j, --jid <jid>         JID (e.g. user@jabber.org)
  -p, --password <pw>     password
  -w, --websocket <uri>   WebSocket URI (e.g. wss://jabber.org:5443/ws)
  -v, --verbose           verbose logging (trace level)
  -h, --help              show this help

Examples:
  XmppClient -j user@jabber.org -p secret
  XmppClient -j user@example.com -p pw -w wss://xmpp.example.com/ws
");
    }

    #endregion

}
