using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace XmppClient;

public sealed class XmppConnection : IAsyncDisposable
{
    private readonly string _server;
    private readonly int    _port;
    private readonly string _jid;
    private readonly string _password;
    private readonly string _username;
    private readonly string _domain;
    
    private TcpClient?      _tcpClient;
    private Stream?         _stream;
    private StreamReader?   _reader;
    private StreamWriter?   _writer;
    private CancellationTokenSource? _cts;
    
    public Roster Roster { get; } = new();
    
    public event Action<string, string, string>? OnMessage;
    public event Action<string, string>?         OnPresence;
    public event Action<string>?                 OnRawXml;
    public event Action<string>?                 OnError;
    
    public bool IsConnected => _tcpClient?.Connected == true;
    public string FullJid { get; private set; } = string.Empty;

    public XmppConnection(string jid, string password, string? server = null, int port = 5222)
    {
        _jid      = jid;
        _password = password;
        
        var parts = jid.Split('@');
        if (parts.Length != 2)
            throw new ArgumentException("JID muss im Format 'user@domain' sein", nameof(jid));
        
        _username = parts[0];
        _domain   = parts[1];
        _server   = server ?? _domain;
        _port     = port;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        Console.WriteLine($"[*] Verbinde zu {_server}:{_port}...");
        
        _tcpClient = new TcpClient { NoDelay = true };
        await _tcpClient.ConnectAsync(_server, _port, ct);
        _stream = _tcpClient.GetStream();
        
        SetupStreams();
        
        // 1. Initial stream
        await SendStreamHeaderAsync();
        var features = await ReadFeaturesAsync(ct);
        
        // 2. STARTTLS
        if (features.StartTls)
        {
            Console.WriteLine("[*] STARTTLS wird initiiert...");
            await PerformStartTlsAsync(ct);
            
            SetupStreams();
            await SendStreamHeaderAsync();
            features = await ReadFeaturesAsync(ct);
        }
        
        // 3. SASL Auth
        if (features.SaslMechanisms.Contains("PLAIN"))
        {
            Console.WriteLine("[*] SASL PLAIN Authentifizierung...");
            await PerformSaslPlainAsync(ct);
        }
        else
        {
            throw new AuthenticationException($"Server unterstützt SASL PLAIN nicht. Verfügbar: {string.Join(", ", features.SaslMechanisms)}");
        }
        
        // 4. Neuer Stream nach Auth
        SetupStreams();
        await SendStreamHeaderAsync();
        features = await ReadFeaturesAsync(ct);
        
        // 5. Resource Binding
        if (features.Bind)
        {
            Console.WriteLine("[*] Resource Binding...");
            FullJid = await PerformBindAsync(ct);
            Console.WriteLine($"[+] Verbunden als: {FullJid}");
        }
        
        // 6. Session
        if (features.Session)
        {
            await PerformSessionAsync(ct);
        }
        
        // 7. Roster laden
        Console.WriteLine("[*] Lade Roster...");
        await RequestRosterAsync(ct);
        
        // 8. Presence
        await SendPresenceAsync();
        Console.WriteLine("[+] Online!");
    }

    private void SetupStreams()
    {
        _reader = new StreamReader(_stream!, Encoding.UTF8, leaveOpen: true, bufferSize: 1024);
        _writer = new StreamWriter(_stream!, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) 
        { 
            AutoFlush = true 
        };
    }

    private async Task SendStreamHeaderAsync()
    {
        var header = $"<?xml version='1.0'?>" +
                     $"<stream:stream to='{_domain}' " +
                     $"xmlns='jabber:client' " +
                     $"xmlns:stream='http://etherx.jabber.org/streams' " +
                     $"version='1.0'>";
        
        await _writer!.WriteAsync(header);
    }

    private async Task<StreamFeatures> ReadFeaturesAsync(CancellationToken ct)
    {
        var xml = await ReadStreamFeaturesAsync(TimeSpan.FromSeconds(10), ct);
        
        var features = new StreamFeatures
        {
            StartTls = xml.Contains("<starttls"),
            StartTlsRequired = xml.Contains("<required"),
            Bind = xml.Contains("<bind"),
            Session = xml.Contains("<session"),
            RosterVersioning = xml.Contains("<ver")
        };
        
        // SASL Mechanisms extrahieren
        var mechMatch = Regex.Match(xml, @"<mechanisms[^>]*>(.*?)</mechanisms>", RegexOptions.Singleline);
        if (mechMatch.Success)
        {
            var mechXml = mechMatch.Groups[1].Value;
            var mechs = Regex.Matches(mechXml, @"<mechanism>([^<]+)</mechanism>");
            foreach (Match m in mechs)
            {
                features.SaslMechanisms.Add(m.Groups[1].Value);
            }
        }
        
        return features;
    }

    private async Task PerformStartTlsAsync(CancellationToken ct)
    {
        await _writer!.WriteAsync("<starttls xmlns='urn:ietf:params:xml:ns:xmpp-tls'/>");
        
        var response = await ReadStanzaAsync("proceed", TimeSpan.FromSeconds(10), ct);
        
        if (!response.Contains("<proceed"))
            throw new AuthenticationException($"STARTTLS fehlgeschlagen: {response}");
        
        var sslStream = new SslStream(
            _stream!,
            leaveInnerStreamOpen: false,
            userCertificateValidationCallback: ValidateServerCertificate
        );
        
        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = _server,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, ct);
        
        Console.WriteLine($"[+] TLS {sslStream.SslProtocol} etabliert");
        _stream = sslStream;
    }

    private static bool ValidateServerCertificate(
        object sender, 
        X509Certificate? certificate, 
        X509Chain? chain, 
        SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;
        
        Console.WriteLine($"[!] Zertifikat-Warnung: {sslPolicyErrors}");
        return true;
    }

    private async Task PerformSaslPlainAsync(CancellationToken ct)
    {
        var authData = $"\0{_username}\0{_password}";
        var authBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(authData));
        
        await _writer!.WriteAsync(
            $"<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='PLAIN'>{authBase64}</auth>");
        
        // SASL Response kann <success/>, <success>...</success> oder <failure>...</failure> sein
        var response = await ReadSaslResponseAsync(TimeSpan.FromSeconds(10), ct);
        
        if (response.Contains("<success"))
        {
            Console.WriteLine("[+] Authentifizierung erfolgreich");
        }
        else if (response.Contains("<failure"))
        {
            var errorMatch = Regex.Match(response, @"<([a-z-]+)(?:\s|/|>)", RegexOptions.IgnoreCase);
            var error = errorMatch.Success ? errorMatch.Groups[1].Value : "unbekannt";
            throw new AuthenticationException($"SASL fehlgeschlagen: {error}");
        }
        else
        {
            throw new AuthenticationException($"Unerwartete Auth-Antwort: {response}");
        }
    }
    
    /// <summary>
    /// Liest eine SASL-Antwort (success oder failure)
    /// </summary>
    private async Task<string> ReadSaslResponseAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        
        var buffer = new StringBuilder();
        var charBuf = new char[4096];
        
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();
            
            var read = await _reader!.ReadAsync(charBuf.AsMemory(), cts.Token);
            if (read == 0)
            {
                throw new IOException($"Verbindung geschlossen. Buffer: {buffer}");
            }
            
            buffer.Append(charBuf, 0, read);
            var xml = buffer.ToString();
            
            // Prüfe auf success - kann sein:
            // <success xmlns='...'/> (self-closing mit Attributen)
            // <success/> (self-closing ohne Attribute)
            // <success>...</success> (mit Content)
            if (xml.Contains("<success"))
            {
                // Warte bis das Tag komplett ist (endet mit /> oder </success>)
                if (IsCompleteElement(xml, "success"))
                {
                    return xml;
                }
            }
            
            // Prüfe auf failure
            if (xml.Contains("<failure"))
            {
                if (IsCompleteElement(xml, "failure"))
                {
                    return xml;
                }
            }
        }
    }
    
    /// <summary>
    /// Prüft ob ein XML-Element komplett ist (self-closing oder mit End-Tag)
    /// </summary>
    private static bool IsCompleteElement(string xml, string tagName)
    {
        var startIdx = xml.IndexOf($"<{tagName}", StringComparison.Ordinal);
        if (startIdx < 0) return false;
        
        // Prüfe auf self-closing
        var selfCloseIdx = FindSelfClosingEnd(xml, startIdx);
        if (selfCloseIdx >= 0) return true;
        
        // Prüfe auf End-Tag
        var endTag = $"</{tagName}>";
        return xml.IndexOf(endTag, startIdx, StringComparison.Ordinal) >= 0;
    }

    private async Task<string> PerformBindAsync(CancellationToken ct)
    {
        var resource = $"console-{Environment.ProcessId}";
        
        await _writer!.WriteAsync(
            $"<iq type='set' id='bind1'>" +
            $"<bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'>" +
            $"<resource>{resource}</resource>" +
            $"</bind></iq>");
        
        var response = await ReadStanzaAsync("iq", TimeSpan.FromSeconds(10), ct);
        
        var jidMatch = Regex.Match(response, @"<jid>([^<]+)</jid>");
        return jidMatch.Success ? jidMatch.Groups[1].Value : $"{_jid}/{resource}";
    }

    private async Task PerformSessionAsync(CancellationToken ct)
    {
        await _writer!.WriteAsync(
            "<iq type='set' id='sess1'>" +
            "<session xmlns='urn:ietf:params:xml:ns:xmpp-session'/>" +
            "</iq>");
        
        // Session-Response kann <iq .../> oder <iq ...>...</iq> sein
        await ReadStanzaAsync("iq", TimeSpan.FromSeconds(10), ct);
    }

    private async Task RequestRosterAsync(CancellationToken ct)
    {
        await _writer!.WriteAsync(
            "<iq type='get' id='roster1'>" +
            "<query xmlns='jabber:iq:roster'/>" +
            "</iq>");
        
        var response = await ReadStanzaAsync("iq", TimeSpan.FromSeconds(10), ct);
        
        Console.WriteLine($"[DEBUG Roster] Response: {response}");
        
        // Finde alle <item .../> oder <item ...>...</item> Elemente
        var itemMatches = Regex.Matches(response, @"<item\s+([^>]+?)(?:/>|>(.*?)</item>)", RegexOptions.Singleline);
        
        Console.WriteLine($"[DEBUG Roster] Gefundene Items: {itemMatches.Count}");
        
        foreach (Match m in itemMatches)
        {
            var attributes = m.Groups[1].Value;
            var content = m.Groups[2].Success ? m.Groups[2].Value : "";
            
            Console.WriteLine($"[DEBUG Roster] Item attributes: {attributes}");
            
            // Extrahiere Attribute einzeln (unabhängig von Reihenfolge)
            var jid = ExtractAttributeValue(attributes, "jid");
            var name = ExtractAttributeValue(attributes, "name");
            var subscription = ExtractAttributeValue(attributes, "subscription");
            
            if (string.IsNullOrEmpty(jid))
            {
                Console.WriteLine("[DEBUG Roster] Item ohne JID übersprungen");
                continue;
            }
            
            var item = new RosterItem(jid)
            {
                Name = !string.IsNullOrEmpty(name) ? name : null,
                Subscription = ParseSubscription(subscription)
            };
            
            // Gruppen aus Content extrahieren
            if (!string.IsNullOrEmpty(content))
            {
                var groups = Regex.Matches(content, @"<group>([^<]+)</group>");
                foreach (Match g in groups)
                {
                    item.Groups.Add(g.Groups[1].Value);
                }
            }
            
            Console.WriteLine($"[DEBUG Roster] Verarbeite: {jid}, name={name}, sub={subscription}");
            Roster.ProcessRosterItem(item);
        }
        
        Console.WriteLine($"[+] Roster geladen: {Roster.Items.Count} Kontakte");
    }
    
    /// <summary>
    /// Extrahiert einen Attributwert aus einem Attribut-String (z.B. "jid='user@server' name='Name'")
    /// </summary>
    private static string? ExtractAttributeValue(string attributes, string attrName)
    {
        // Suche nach attrName='...' oder attrName="..."
        var match = Regex.Match(attributes, $@"{attrName}\s*=\s*['""]([^'""]*)['""]", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static SubscriptionState ParseSubscription(string? sub) => sub switch
    {
        "to" => SubscriptionState.To,
        "from" => SubscriptionState.From,
        "both" => SubscriptionState.Both,
        "remove" => SubscriptionState.Remove,
        _ => SubscriptionState.None
    };

    /// <summary>
    /// Liest solange vom Stream bis ein komplettes XML-Element des angegebenen Tags empfangen wurde.
    /// Unterstützt sowohl self-closing Tags als auch Tags mit Content.
    /// </summary>
    private async Task<string> ReadStanzaAsync(string tagName, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        
        var buffer = new StringBuilder();
        var charBuf = new char[4096];
        
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();
            
            var read = await _reader!.ReadAsync(charBuf.AsMemory(), cts.Token);
            if (read == 0)
            {
                throw new IOException($"Verbindung geschlossen. Buffer: {buffer}");
            }
            
            buffer.Append(charBuf, 0, read);
            
            var xml = buffer.ToString();
            
            // Suche nach dem Start-Tag
            var startTag = $"<{tagName}";
            var startIdx = xml.IndexOf(startTag, StringComparison.Ordinal);
            if (startIdx < 0) continue;
            
            // Prüfe ob self-closing: <tag ... />
            var selfCloseIdx = FindSelfClosingEnd(xml, startIdx);
            if (selfCloseIdx >= 0)
            {
                return xml[..(selfCloseIdx + 2)]; // inkl. "/>"
            }
            
            // Prüfe ob vollständiges Element mit End-Tag
            var endTag = $"</{tagName}>";
            var endIdx = xml.IndexOf(endTag, startIdx, StringComparison.Ordinal);
            if (endIdx >= 0)
            {
                return xml[..(endIdx + endTag.Length)];
            }
            
            // Noch nicht komplett - weiter lesen
        }
    }
    
    /// <summary>
    /// Findet das Ende eines self-closing Tags, z.B. <tag attr="val" />
    /// Gibt -1 zurück wenn kein self-closing Tag gefunden wurde.
    /// </summary>
    private static int FindSelfClosingEnd(string xml, int startIdx)
    {
        var inQuote = false;
        var quoteChar = '"';
        
        for (var i = startIdx; i < xml.Length - 1; i++)
        {
            var c = xml[i];
            
            // Quote-Tracking für Attributwerte
            if ((c == '"' || c == '\'') && !inQuote)
            {
                inQuote = true;
                quoteChar = c;
            }
            else if (c == quoteChar && inQuote)
            {
                inQuote = false;
            }
            
            // Außerhalb von Quotes: suche nach /> oder >
            if (!inQuote)
            {
                if (c == '/' && xml[i + 1] == '>')
                {
                    return i; // Position des '/'
                }
                if (c == '>')
                {
                    // Normales Tag-Ende, kein self-closing
                    return -1;
                }
            }
        }
        
        return -1; // Noch nicht komplett
    }
    
    /// <summary>
    /// Liest bis stream:features komplett empfangen wurde
    /// </summary>
    private async Task<string> ReadStreamFeaturesAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        
        var buffer = new StringBuilder();
        var charBuf = new char[4096];
        
        while (true)
        {
            cts.Token.ThrowIfCancellationRequested();
            
            var read = await _reader!.ReadAsync(charBuf.AsMemory(), cts.Token);
            if (read == 0)
            {
                throw new IOException($"Verbindung geschlossen. Buffer: {buffer}");
            }
            
            buffer.Append(charBuf, 0, read);
            var xml = buffer.ToString();
            
            // stream:features kann leer sein: <stream:features/>
            // oder mit Inhalt: <stream:features>...</stream:features>
            
            if (xml.Contains("<stream:features/>"))
            {
                return xml;
            }
            
            if (xml.Contains("</stream:features>"))
            {
                return xml;
            }
        }
    }

    // ===== PUBLIC API =====

    public async Task SendPresenceAsync(string? show = null, string? status = null)
    {
        var presence = new StringBuilder("<presence>");
        
        if (!string.IsNullOrEmpty(show))
            presence.Append($"<show>{XmlEscape(show)}</show>");
        
        if (!string.IsNullOrEmpty(status))
            presence.Append($"<status>{XmlEscape(status)}</status>");
        
        presence.Append("</presence>");
        
        await _writer!.WriteAsync(presence.ToString());
    }

    public async Task SendMessageAsync(string to, string body)
    {
        var message = $"<message to='{XmlEscape(to)}' type='chat'>" +
                      $"<body>{XmlEscape(body)}</body>" +
                      $"</message>";
        
        await _writer!.WriteAsync(message);
    }

    public async Task SendRawAsync(string xml)
    {
        await _writer!.WriteAsync(xml);
    }

    public async Task AddContactAsync(string jid, string? name = null, IEnumerable<string>? groups = null)
    {
        await SendRawAsync(RosterStanzaBuilder.SetItem(jid, name, groups));
        await SendRawAsync(RosterStanzaBuilder.Subscribe(jid));
    }

    public async Task RemoveContactAsync(string jid)
    {
        await SendRawAsync(RosterStanzaBuilder.RemoveItem(jid));
    }

    public async Task AcceptSubscriptionAsync(string jid)
    {
        await SendRawAsync(RosterStanzaBuilder.Subscribed(jid));
    }

    public async Task DenySubscriptionAsync(string jid)
    {
        await SendRawAsync(RosterStanzaBuilder.Unsubscribed(jid));
    }

    // ===== RECEIVE LOOP =====

    public async Task StartReceivingAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var buffer = new StringBuilder();
        var charBuf = new char[4096];
        
        try
        {
            while (!_cts.Token.IsCancellationRequested && IsConnected)
            {
                var read = await _reader!.ReadAsync(charBuf.AsMemory(), _cts.Token);
                if (read == 0)
                {
                    OnError?.Invoke("Verbindung geschlossen");
                    break;
                }
                
                buffer.Append(charBuf, 0, read);
                ProcessIncomingXml(buffer);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke($"Empfangsfehler: {ex.Message}");
        }
    }

    private void ProcessIncomingXml(StringBuilder buffer)
    {
        var xml = buffer.ToString();
        
        // Messages
        ProcessStanzaType(ref xml, "message", stanza =>
        {
            var from = ExtractAttribute(stanza, "from") ?? "unknown";
            var to = ExtractAttribute(stanza, "to") ?? FullJid;
            var body = ExtractElement(stanza, "body");
            
            if (!string.IsNullOrEmpty(body))
            {
                OnMessage?.Invoke(from, to, body);
            }
        });
        
        // Presence
        ProcessStanzaType(ref xml, "presence", stanza =>
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
        });
        
        // IQ
        ProcessStanzaType(ref xml, "iq", stanza =>
        {
            var type = ExtractAttribute(stanza, "type");
            var id = ExtractAttribute(stanza, "id");
            
            if (type == "set" && stanza.Contains("jabber:iq:roster"))
            {
                ProcessRosterPush(stanza);
                _ = SendRawAsync($"<iq type='result' id='{id}'/>");
            }
        });
        
        buffer.Clear();
        buffer.Append(xml);
    }

    private void ProcessStanzaType(ref string xml, string tagName, Action<string> handler)
    {
        var startTag = $"<{tagName}";
        var endTag = $"</{tagName}>";
        
        while (true)
        {
            var start = xml.IndexOf(startTag, StringComparison.Ordinal);
            if (start < 0) break;
            
            var selfClose = xml.IndexOf("/>", start, StringComparison.Ordinal);
            var fullClose = xml.IndexOf(endTag, start, StringComparison.Ordinal);
            var nextTagStart = xml.IndexOf('<', start + 1);
            
            int end, endLen;
            
            if (selfClose >= 0 && (nextTagStart < 0 || selfClose < nextTagStart))
            {
                end = selfClose;
                endLen = 2;
            }
            else if (fullClose >= 0)
            {
                end = fullClose;
                endLen = endTag.Length;
            }
            else
            {
                break;
            }
            
            var stanza = xml.Substring(start, end - start + endLen);
            xml = xml.Remove(start, end - start + endLen);
            
            OnRawXml?.Invoke(stanza);
            
            try { handler(stanza); }
            catch (Exception ex) { OnError?.Invoke($"Stanza-Fehler: {ex.Message}"); }
        }
    }

    private void ProcessRosterPush(string stanza)
    {
        var itemMatch = Regex.Match(stanza, 
            @"<item\s+jid=['""]([^'""]+)['""](?:\s+name=['""]([^'""]*)['""])?(?:\s+subscription=['""]([^'""]*)['""])?",
            RegexOptions.Singleline);
        
        if (itemMatch.Success)
        {
            var jid = itemMatch.Groups[1].Value;
            var subscription = ParseSubscription(itemMatch.Groups[3].Value);
            
            if (subscription == SubscriptionState.Remove)
            {
                Roster.RemoveItem(jid);
            }
            else
            {
                var item = new RosterItem(jid)
                {
                    Name = itemMatch.Groups[2].Success ? itemMatch.Groups[2].Value : null,
                    Subscription = subscription
                };
                Roster.ProcessRosterItem(item);
            }
        }
    }

    // ===== HELPERS =====

    private static string? ExtractAttribute(string xml, string name)
    {
        var match = Regex.Match(xml, $@"{name}=['""]([^'""]*)['""]");
        return match.Success ? XmlUnescape(match.Groups[1].Value) : null;
    }

    private static string? ExtractElement(string xml, string name)
    {
        var match = Regex.Match(xml, $@"<{name}>([^<]*)</{name}>");
        return match.Success ? XmlUnescape(match.Groups[1].Value) : null;
    }

    private static string XmlEscape(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("'", "&apos;")
            .Replace("\"", "&quot;");

    private static string XmlUnescape(string text) =>
        text.Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&apos;", "'")
            .Replace("&quot;", "\"")
            .Replace("&amp;", "&");

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        
        try
        {
            if (_writer != null)
            {
                await _writer.WriteAsync("</stream:stream>");
                await _writer.FlushAsync();
            }
        }
        catch { }
        
        _writer?.Dispose();
        _reader?.Dispose();
        
        if (_stream is SslStream ssl)
            await ssl.DisposeAsync();
        else
            _stream?.Dispose();
        
        _tcpClient?.Dispose();
        _cts?.Dispose();
    }
}

/// <summary>
/// Stream Features nach Verbindung/TLS/Auth
/// </summary>
public sealed class StreamFeatures
{
    public bool StartTls { get; set; }
    public bool StartTlsRequired { get; set; }
    public List<string> SaslMechanisms { get; set; } = [];
    public bool Bind { get; set; }
    public bool Session { get; set; }
    public bool RosterVersioning { get; set; }
}
