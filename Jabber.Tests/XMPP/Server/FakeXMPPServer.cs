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

using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP.Server
{

    /// <summary>
    /// Ein minimaler XMPP-over-WebSocket-Server (RFC 7395) für Tests.
    ///
    /// Er beherrscht so viel vom Protokoll, dass sich mehrere echte
    /// <c>XMPPClient</c>-Instanzen gleichzeitig anmelden und miteinander
    /// sprechen können:
    ///
    /// <list type="bullet">
    ///   <item>SASL PLAIN gegen hinterlegte Konten</item>
    ///   <item>Resource Binding mit eindeutiger Resource je Verbindung</item>
    ///   <item>Routing von message, presence und iq zwischen den Sitzungen</item>
    ///   <item>XEP-0280 Message Carbons zwischen den Resourcen eines Kontos</item>
    ///   <item>serverseitiger Roster inklusive Roster-Push</item>
    ///   <item>XEP-0199 Ping, zum Server und zwischen Clients</item>
    /// </list>
    ///
    /// Nicht implementiert und für Tests bewusst vereinfacht: Presence wird an
    /// alle Sitzungen verteilt statt nur an Subscriber, TLS gibt es nicht, und
    /// Fehlerfälle werden nur dort erzeugt, wo ein Schalter es verlangt.
    /// </summary>
    public sealed class FakeXMPPServer : IAsyncDisposable
    {

        #region Data

        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Dictionary<String, FakeXMPPAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<FakeXMPPSession> _sessions = [];
        private readonly Lock _lock = new();

        private Task? _acceptLoop;
        private Int32 _connectionCounter;

        #endregion

        #region Properties

        /// <summary>Der bediente Port.</summary>
        public Int32 Port { get; }

        /// <summary>Die Domain, für die der Server zuständig ist.</summary>
        public String Domain { get; }

        /// <summary>WebSocket-URI für den Client.</summary>
        public String Uri => $"ws://localhost:{Port}/ws/";

        /// <summary>Anzahl aller jemals akzeptierten Verbindungen.</summary>
        public Int32 ConnectionCount => Volatile.Read(ref _connectionCounter);

        /// <summary>Alle derzeit offenen Sitzungen.</summary>
        public IReadOnlyList<FakeXMPPSession> Sessions
        {
            get { lock (_lock) return _sessions.Where(s => s.IsOpen).ToList(); }
        }

        /// <summary>Alle Frames aller Sitzungen, unabhängig vom Absender.</summary>
        public IReadOnlyList<String> AllReceived
        {
            get { lock (_lock) return _sessions.SelectMany(s => s.Received).ToList(); }
        }

        #endregion

        #region Verhaltensschalter

        /// <summary>
        /// Beantwortet der Server das Close-Frame des Clients? Auf false lässt
        /// sich ein Server simulieren, der den Handshake offen lässt.
        /// </summary>
        public Boolean CompleteCloseHandshake { get; set; } = true;

        /// <summary>Werden message/presence/iq zwischen Sitzungen zugestellt?</summary>
        public Boolean RouteStanzas { get; set; } = true;

        /// <summary>Wird Presence ohne 'to' an alle anderen Sitzungen verteilt?</summary>
        public Boolean BroadcastPresence { get; set; } = true;

        /// <summary>Werden XEP-0280 Carbons an weitere Resourcen verteilt?</summary>
        public Boolean DeliverCarbons { get; set; } = true;

        /// <summary>Beantwortet der Server XEP-0199 Pings, die an ihn gerichtet sind?</summary>
        public Boolean AnswerPings { get; set; } = true;

        #endregion

        #region Events

        /// <summary>Wird für jede vom Client empfangene Stanza ausgelöst.</summary>
        public event Action<FakeXMPPSession, String>? OnStanzaReceived;

        /// <summary>Wird ausgelöst, sobald eine Sitzung erfolgreich gebunden wurde.</summary>
        public event Action<FakeXMPPSession>? OnSessionBound;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Erstellt einen Testserver auf einem freien Port.
        /// </summary>
        /// <param name="domain">Die bediente Domain; muss zum JID der Clients passen.</param>
        /// <param name="port">Fester Port oder 0 für einen freien.</param>
        public FakeXMPPServer(String domain = "localhost", Int32 port = 0)
        {

            Domain  = domain;
            Port    = port > 0 ? port : FreeTcpPort();

            _listener.Prefixes.Add($"http://localhost:{Port}/ws/");

        }

        #endregion


        #region Konten

        /// <summary>
        /// Legt ein Konto an, an dem sich ein Client anmelden darf.
        /// </summary>
        public FakeXMPPAccount AddAccount(String localPart, String password = "pw")
        {

            var account = new FakeXMPPAccount($"{localPart}@{Domain}", password);

            lock (_lock)
                _accounts[account.BareJid] = account;

            return account;

        }

        /// <summary>Liefert ein Konto oder null.</summary>
        public FakeXMPPAccount? GetAccount(String bareJid)
        {
            lock (_lock)
                return _accounts.TryGetValue(bareJid, out var a) ? a : null;
        }

        #endregion

        #region Sitzungen

        /// <summary>Alle offenen Sitzungen eines Kontos, älteste zuerst.</summary>
        public IReadOnlyList<FakeXMPPSession> SessionsOf(String bareJid)
        {
            lock (_lock)
                return _sessions
                       .Where(s => s.IsOpen &&
                                   String.Equals(s.BareJid, BareOf(bareJid), StringComparison.OrdinalIgnoreCase))
                       .ToList();
        }

        /// <summary>Die Sitzung zu einem Full-JID oder null.</summary>
        public FakeXMPPSession? SessionOf(String fullJid)
        {
            lock (_lock)
                return _sessions.FirstOrDefault(s => s.IsOpen &&
                                                     String.Equals(s.FullJid, fullJid, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Reisst alle offenen Sitzungen ab.</summary>
        public void KillAllSessions()
        {
            foreach (var s in Sessions)
                s.Kill();
        }

        /// <summary>Reisst alle Sitzungen eines Kontos ab.</summary>
        public void KillSessionsOf(String bareJid)
        {
            foreach (var s in SessionsOf(bareJid))
                s.Kill();
        }

        #endregion

        #region Senden und Warten

        /// <summary>
        /// Schickt eine Stanza an alle Sitzungen des angegebenen JIDs; bei
        /// einem Full-JID nur an die betreffende Resource.
        /// </summary>
        public async Task PushAsync(String jid, String xml)
        {

            var targets = jid.Contains('/')
                              ? [SessionOf(jid)]
                              : SessionsOf(jid).Cast<FakeXMPPSession?>().ToArray();

            foreach (var t in targets)
                if (t is not null)
                    await t.SendAsync(xml);

        }

        /// <summary>Schickt eine Stanza an alle offenen Sitzungen.</summary>
        public async Task BroadcastAsync(String xml)
        {
            foreach (var s in Sessions)
                await s.SendAsync(xml);
        }

        /// <summary>
        /// Wartet, bis die Bedingung zutrifft, oder bis der Timeout abläuft.
        /// </summary>
        public static async Task<Boolean> WaitUntilAsync(Func<Boolean> condition,
                                                         TimeSpan?     timeout = null,
                                                         TimeSpan?     poll    = null)
        {

            var deadline  = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
            var interval  = poll ?? TimeSpan.FromMilliseconds(25);

            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;

                await Task.Delay(interval);
            }

            return condition();

        }

        /// <summary>Wartet, bis mindestens so viele Sitzungen gebunden sind.</summary>
        public Task<Boolean> WaitForBoundSessionsAsync(Int32 count, TimeSpan? timeout = null)
            => WaitUntilAsync(() => Sessions.Count(s => s.FullJid is not null) >= count, timeout);

        #endregion

        #region Start und Verbindungsannahme

        public void Start()
        {
            _listener.Start();
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {

            while (!_cts.IsCancellationRequested)
            {

                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }

                if (!ctx.Request.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                    continue;
                }

                HttpListenerWebSocketContext wsCtx;
                try { wsCtx = await ctx.AcceptWebSocketAsync("xmpp"); }
                catch { continue; }

                var session = new FakeXMPPSession(wsCtx.WebSocket,
                                                  Interlocked.Increment(ref _connectionCounter));

                lock (_lock)
                    _sessions.Add(session);

                _ = Task.Run(() => ServeAsync(session, wsCtx.WebSocket));

            }

        }

        private async Task ServeAsync(FakeXMPPSession session, WebSocket ws)
        {

            var buffer     = new Byte[32768];
            var openCount  = 0;

            try
            {
                while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {

                    var sb = new StringBuilder();
                    WebSocketReceiveResult r;

                    do
                    {

                        r = await ws.ReceiveAsync(buffer, _cts.Token);

                        if (r.MessageType == WebSocketMessageType.Close)
                        {
                            if (CompleteCloseHandshake)
                            {
                                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                                catch { }
                            }
                            return;
                        }

                        sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));

                    }
                    while (!r.EndOfMessage);

                    var frame = sb.ToString();
                    session.RecordReceived(frame);
                    OnStanzaReceived?.Invoke(session, frame);

                    if (frame.StartsWith("<open", StringComparison.Ordinal))
                        openCount++;

                    await HandleFrameAsync(session, frame, openCount);

                }
            }
            catch
            {
                // Verbindung abgerissen - im Test der Normalfall
            }

        }

        #endregion

        #region Protokollbehandlung

        private async Task HandleFrameAsync(FakeXMPPSession session, String frame, Int32 openCount)
        {

            if (frame.StartsWith("<open", StringComparison.Ordinal))
            {
                await HandleStreamOpenAsync(session, openCount);
                return;
            }

            if (frame.StartsWith("<auth", StringComparison.Ordinal))
            {
                await HandleAuthAsync(session, frame);
                return;
            }

            if (frame.StartsWith("<iq", StringComparison.Ordinal))
            {
                await HandleIqAsync(session, frame);
                return;
            }

            if (frame.StartsWith("<message", StringComparison.Ordinal))
            {
                await HandleMessageAsync(session, frame);
                return;
            }

            if (frame.StartsWith("<presence", StringComparison.Ordinal))
            {
                await HandlePresenceAsync(session, frame);
                return;
            }

        }

        private async Task HandleStreamOpenAsync(FakeXMPPSession session, Int32 openCount)
        {

            await session.SendAsync(
                $"<open xmlns='urn:ietf:params:xml:ns:xmpp-framing' from='{Domain}' id='stream-{session.ConnectionNumber}' version='1.0'/>");

            if (openCount == 1)
                await session.SendAsync(
                    "<stream:features xmlns:stream='http://etherx.jabber.org/streams'>" +
                    "<mechanisms xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>" +
                    "<mechanism>PLAIN</mechanism>" +
                    "</mechanisms></stream:features>");
            else
                await session.SendAsync(
                    "<stream:features xmlns:stream='http://etherx.jabber.org/streams'>" +
                    "<bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'/>" +
                    "<session xmlns='urn:ietf:params:xml:ns:xmpp-session'><optional/></session>" +
                    "<sm xmlns='urn:xmpp:sm:3'/>" +
                    "</stream:features>");

        }

        private async Task HandleAuthAsync(FakeXMPPSession session, String frame)
        {

            // SASL PLAIN: base64( \0 benutzer \0 passwort )
            var payload = Regex.Match(frame, @"<auth[^>]*>([^<]*)</auth>").Groups[1].Value;

            String user = "", password = "";

            try
            {
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(payload)).Split('\0');
                if (parts.Length >= 3)
                {
                    user      = parts[1];
                    password  = parts[2];
                }
            }
            catch { /* unlesbar -> schlaegt unten fehl */ }

            var account = GetAccount($"{user}@{Domain}");

            if (account is null || account.Password != password)
            {
                await session.SendAsync(
                    "<failure xmlns='urn:ietf:params:xml:ns:xmpp-sasl'><not-authorized/></failure>");
                return;
            }

            session.Account = account;
            await session.SendAsync("<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'/>");

        }

        private async Task HandleIqAsync(FakeXMPPSession session, String frame)
        {

            var id    = Attr(frame, "id");
            var type  = Attr(frame, "type");
            var to    = Attr(frame, "to");

            // An eine andere Entity gerichtet? Dann weiterleiten.
            if (RouteStanzas &&
                to is not null &&
                !String.Equals(to, Domain, StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(BareOf(to), session.BareJid, StringComparison.OrdinalIgnoreCase))
            {
                await RouteToAsync(to, StampFrom(frame, session.FullJid));
                return;
            }

            // Resource Binding
            if (frame.Contains("urn:ietf:params:xml:ns:xmpp-bind", StringComparison.Ordinal) && type == "set")
            {
                await HandleBindAsync(session, frame, id);
                return;
            }

            // Legacy Session
            if (frame.Contains("urn:ietf:params:xml:ns:xmpp-session", StringComparison.Ordinal))
            {
                await session.SendAsync($"<iq type='result' id='{id}'/>");
                return;
            }

            // XEP-0280 Carbons an/aus
            if (frame.Contains("urn:xmpp:carbons:2", StringComparison.Ordinal))
            {
                session.CarbonsEnabled = frame.Contains("<enable", StringComparison.Ordinal);
                await session.SendAsync($"<iq type='result' id='{id}'/>");
                return;
            }

            // Roster
            if (frame.Contains("jabber:iq:roster", StringComparison.Ordinal))
            {
                await HandleRosterAsync(session, frame, id, type);
                return;
            }

            // XEP-0199 Ping an den Server
            if (frame.Contains("urn:xmpp:ping", StringComparison.Ordinal) && type == "get")
            {
                if (AnswerPings)
                    await session.SendAsync($"<iq type='result' id='{id}' from='{Domain}'/>");
                return;
            }

            // XEP-0030 disco#info über den Server
            if (frame.Contains("http://jabber.org/protocol/disco#info", StringComparison.Ordinal) && type == "get")
            {
                await session.SendAsync(
                    $"<iq type='result' id='{id}' from='{Domain}'>" +
                    "<query xmlns='http://jabber.org/protocol/disco#info'>" +
                    "<identity category='server' type='im' name='FakeXMPPServer'/>" +
                    "<feature var='urn:xmpp:carbons:2'/>" +
                    "<feature var='urn:xmpp:ping'/>" +
                    "<feature var='urn:xmpp:sm:3'/>" +
                    "</query></iq>");
                return;
            }

            // Unbekannte Anfragen bekommen einen Fehler, Antworten werden verworfen.
            if (type is "get" or "set")
                await session.SendAsync(
                    $"<iq type='error' id='{id}'><error type='cancel'>" +
                    "<service-unavailable xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                    "</error></iq>");

        }

        private async Task HandleBindAsync(FakeXMPPSession session, String frame, String? id)
        {

            var requested = Regex.Match(frame, @"<resource>([^<]*)</resource>").Groups[1].Value;

            if (String.IsNullOrEmpty(requested))
                requested = "auto";

            // Der Client verwendet console-{ProcessId} als Resource. Laufen mehrere
            // Clients im selben Prozess, kollidieren sie - der Server vergibt dann
            // wie ein echter Server eine abweichende, eindeutige Resource.
            String resource;

            lock (_lock)
            {

                resource = requested;
                var n = 2;

                while (_sessions.Any(s => s.IsOpen &&
                                          String.Equals(s.BareJid, session.BareJid, StringComparison.OrdinalIgnoreCase) &&
                                          String.Equals(s.Resource, resource, StringComparison.Ordinal)))
                {
                    resource = $"{requested}-{n++}";
                }

                session.Resource = resource;

            }

            await session.SendAsync(
                $"<iq type='result' id='{id}'>" +
                "<bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'>" +
                $"<jid>{session.FullJid}</jid>" +
                "</bind></iq>");

            OnSessionBound?.Invoke(session);

        }

        private async Task HandleRosterAsync(FakeXMPPSession session, String frame, String? id, String? type)
        {

            var account = session.Account;

            if (account is null)
                return;

            if (type == "get")
            {

                var items = new StringBuilder();

                foreach (var e in account.Roster)
                {
                    items.Append($"<item jid='{e.Jid}'");
                    if (e.Name is not null)
                        items.Append($" name='{e.Name}'");
                    items.Append($" subscription='{e.Subscription}'/>");
                }

                await session.SendAsync(
                    $"<iq type='result' id='{id}'>" +
                    $"<query xmlns='jabber:iq:roster'>{items}</query></iq>");

                return;

            }

            if (type == "set")
            {

                var m = Regex.Match(frame, @"<item\s+([^>]+?)/?>");

                if (m.Success)
                {

                    var attrs         = m.Groups[1].Value;
                    var jid           = AttrIn(attrs, "jid");
                    var subscription  = AttrIn(attrs, "subscription");

                    if (jid is not null)
                    {
                        if (subscription == "remove")
                            account.RemoveRosterEntry(jid);
                        else
                            account.SetRosterEntry(new FakeRosterEntry(jid, AttrIn(attrs, "name"), subscription ?? "none"));
                    }

                }

                await session.SendAsync($"<iq type='result' id='{id}'/>");

                // Roster-Push an alle Resourcen des Kontos - ohne 'from', wie ein echter Server.
                foreach (var s in SessionsOf(account.BareJid))
                    await s.SendAsync(
                        $"<iq type='set' id='push-{Guid.NewGuid():N}' to='{s.FullJid}'>" +
                        $"<query xmlns='jabber:iq:roster'>{m.Value}</query></iq>");

            }

        }

        private async Task HandleMessageAsync(FakeXMPPSession session, String frame)
        {

            if (!RouteStanzas)
                return;

            var to = Attr(frame, "to");

            if (to is null || session.FullJid is null)
                return;

            var stamped = StampFrom(frame, session.FullJid);

            // Zustellung an den Empfänger
            var recipients = to.Contains('/')
                                 ? (SessionOf(to) is { } one ? [one] : Array.Empty<FakeXMPPSession>())
                                 : SessionsOf(to).ToArray();

            FakeXMPPSession? primary = null;

            if (recipients.Length > 0)
            {
                // Wie ein echter Server: an die zuletzt gebundene Resource zustellen.
                primary = recipients[^1];
                await primary.SendAsync(stamped);
            }

            if (!DeliverCarbons)
                return;

            // XEP-0280 <received>: die übrigen Resourcen des Empfängers
            foreach (var other in recipients.Where(r => r != primary && r.CarbonsEnabled))
                await other.SendAsync(CarbonEnvelope("received", other.BareJid!, other.FullJid!, stamped));

            // XEP-0280 <sent>: die übrigen Resourcen des Absenders
            foreach (var other in SessionsOf(session.BareJid!).Where(r => r != session && r.CarbonsEnabled))
                await other.SendAsync(CarbonEnvelope("sent", other.BareJid!, other.FullJid!, stamped));

        }

        private async Task HandlePresenceAsync(FakeXMPPSession session, String frame)
        {

            if (!RouteStanzas || session.FullJid is null)
                return;

            var to       = Attr(frame, "to");
            var stamped  = StampFrom(frame, session.FullJid);

            if (to is not null)
            {
                await RouteToAsync(to, stamped);
                return;
            }

            if (!BroadcastPresence)
                return;

            foreach (var s in Sessions.Where(s => s != session && s.FullJid is not null))
                await s.SendAsync(stamped);

        }

        private async Task RouteToAsync(String to, String stanza)
        {

            var targets = to.Contains('/')
                              ? (SessionOf(to) is { } one ? [one] : Array.Empty<FakeXMPPSession>())
                              : SessionsOf(to).ToArray();

            foreach (var t in targets)
                await t.SendAsync(stanza);

        }

        #endregion

        #region Hilfsfunktionen

        private static String CarbonEnvelope(String kind, String ownBareJid, String targetFullJid, String inner)
            => $"<message xmlns='jabber:client' from='{ownBareJid}' to='{targetFullJid}'>" +
               $"<{kind} xmlns='urn:xmpp:carbons:2'>" +
               $"<forwarded xmlns='urn:xmpp:forward:0'>{inner}</forwarded>" +
               $"</{kind}></message>";

        /// <summary>Setzt oder ersetzt das from-Attribut im äussersten Element.</summary>
        internal static String StampFrom(String stanza, String? fullJid)
        {

            if (fullJid is null)
                return stanza;

            var m = Regex.Match(stanza, @"^<(\w+)([^>]*?)(/?)>");

            if (!m.Success)
                return stanza;

            var attrs = Regex.Replace(m.Groups[2].Value, @"\s+from=['""][^'""]*['""]", "");

            return $"<{m.Groups[1].Value}{attrs} from='{fullJid}'{m.Groups[3].Value}>" +
                   stanza[m.Length..];

        }

        private static String? Attr(String xml, String name)
        {
            var m = Regex.Match(xml, @"^<\w+[^>]*?\s" + name + @"=['""]([^'""]*)['""]");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static String? AttrIn(String attrs, String name)
        {
            var m = Regex.Match(attrs, name + @"\s*=\s*['""]([^'""]*)['""]");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static String BareOf(String jid)
        {
            var slash = jid.IndexOf('/');
            return slash > 0 ? jid[..slash] : jid;
        }

        private static Int32 FreeTcpPort()
        {

            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint) l.LocalEndpoint).Port;
            l.Stop();

            return port;

        }

        #endregion

        public async ValueTask DisposeAsync()
        {

            _cts.Cancel();

            KillAllSessions();

            try { _listener.Stop(); } catch { }

            if (_acceptLoop is not null)
            {
                try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch { }
            }

            try { _listener.Close(); } catch { }

            _cts.Dispose();

        }

    }

}
