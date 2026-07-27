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
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.WebSocket;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    // Hermod bringt einen eigenen Typ IPAddress mit, der hier den
    // gleichnamigen aus System.Net verdeckt. Der Alias muss innerhalb der
    // Namespace-Deklaration stehen, weil ein Namespace-Member sonst gegen einen
    // Alias der Compilation Unit gewinnt.
    using IPAddress = System.Net.IPAddress;

    /// <summary>
    /// Ein minimaler XMPP-over-WebSocket-Server (RFC 7395).
    ///
    /// Gedacht als Gegenstelle für Tests und für die Entwicklung, nicht für
    /// den Produktivbetrieb: es fehlt eine dauerhafte Kontenverwaltung.
    ///
    /// Den Transport - WebSocket-Rahmen, Verbindungsverwaltung und TLS -
    /// liefert Hermods <c>AWebSocketServer</c>; hier steht nur das Protokoll.
    ///
    /// Er beherrscht so viel vom Protokoll, dass sich mehrere echte
    /// <c>XMPPClient</c>-Instanzen gleichzeitig anmelden und miteinander
    /// sprechen können:
    ///
    /// <list type="bullet">
    ///   <item>SASL PLAIN gegen hinterlegte Konten</item>
    ///   <item>Resource Binding mit eindeutiger Resource je Verbindung</item>
    ///   <item>Routing von message, presence und iq zwischen den Sitzungen</item>
    ///   <item>Presence nur an Subscriber, samt Probe (RFC 6121, Abschnitt 4)</item>
    ///   <item>Subscription-Handshake mit Roster-Pushes an beide Seiten (Abschnitt 3)</item>
    ///   <item>XEP-0280 Message Carbons zwischen den Resourcen eines Kontos</item>
    ///   <item>serverseitiger Roster inklusive Roster-Push</item>
    ///   <item>XEP-0199 Ping, zum Server und zwischen Clients</item>
    ///   <item>XEP-0198 Stream Management mit eigener, unabhängiger Zählung</item>
    /// </list>
    ///
    /// Fehlerfälle erzeugt er nur dort, wo ein Schalter es verlangt.
    /// </summary>
    public sealed class XMPPServer : IAsyncDisposable
    {

        #region Data

        private readonly XMPPWebSocketServer _webSocketServer;
        private readonly CancellationTokenSource _cts = new();
        private readonly Dictionary<String, XMPPAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<XMPPSession> _sessions = [];
        private readonly Lock _lock = new();

        private Int32 _connectionCounter;

        #endregion

        #region Properties

        /// <summary>Der bediente Port.</summary>
        public Int32 Port { get; }

        /// <summary>Die Domain, für die der Server zuständig ist.</summary>
        public String Domain { get; }

        /// <summary>
        /// Das selbst signierte Zertifikat dieses Servers, oder null, wenn er
        /// im Klartext spricht.
        /// </summary>
        public X509Certificate2? Certificate { get; }

        /// <summary>WebSocket-URI für den Client.</summary>
        public String Uri => $"{(Certificate is not null ? "wss" : "ws")}://localhost:{Port}/ws/";

        /// <summary>Anzahl aller jemals akzeptierten Verbindungen.</summary>
        public Int32 ConnectionCount => Volatile.Read(ref _connectionCounter);

        /// <summary>Alle derzeit offenen Sitzungen.</summary>
        public IReadOnlyList<XMPPSession> Sessions
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
        /// sich ein Server simulieren, der den Handshake offen lässt: er hält
        /// seine Antwort um <c>SilentCloseDelay</c> zurück, während die
        /// Verbindung offen bleibt.
        /// </summary>
        public Boolean CompleteCloseHandshake { get; set; } = true;

        /// <summary>
        /// Welche SASL-Mechanismen der Server anbietet, in der Reihenfolge der
        /// Ankündigung.
        /// </summary>
        /// <remarks>
        /// Der Client wählt selbst, und zwar den stärksten, den er kennt. Die
        /// Vorgabe entspricht dem, was verbreitete Server anbieten. PLAIN ist
        /// dabei, weil es hinter TLS vertretbar ist und ältere Clients nichts
        /// anderes können - für die Gegenprobe lässt sich die Liste
        /// einschränken.
        ///
        /// Ein Mechanismus, der hier fehlt, wird auch dann abgelehnt, wenn ein
        /// Client ihn trotzdem versucht.
        /// </remarks>
        public IList<String> OfferedSaslMechanisms { get; } =
            ["SCRAM-SHA-256", "SCRAM-SHA-1", "PLAIN"];

        /// <summary>
        /// Schickt der Server eine falsche Serversignatur im
        /// <c>&lt;success/&gt;</c>?
        /// </summary>
        /// <remarks>
        /// Für die Gegenprobe zur zweiten Hälfte von SCRAM: ein Server, der
        /// das Passwort nicht kennt, kann sie nicht erzeugen. Der Client muss
        /// die Anmeldung dann verweigern (RFC 5802, Abschnitt 5).
        /// </remarks>
        public Boolean CorruptScramSignature { get; set; } = false;

        /// <summary>
        /// Lässt der Server die Serversignatur im <c>&lt;success/&gt;</c>
        /// ganz weg?
        /// </summary>
        /// <remarks>
        /// Der zweite Weg, an der gegenseitigen Authentifizierung vorbei zu
        /// kommen - und der gefährlichere, weil ein Client leicht dazu neigt,
        /// eine fehlende Signatur einfach nicht zu prüfen.
        /// </remarks>
        public Boolean OmitScramSignature { get; set; } = false;

        /// <summary>Werden message/presence/iq zwischen Sitzungen zugestellt?</summary>
        public Boolean RouteStanzas { get; set; } = true;

        /// <summary>
        /// Wird Presence ohne 'to' überhaupt verteilt? Wer sie bekommt,
        /// entscheidet der Subscription-Zustand; dieser Schalter setzt die
        /// Verteilung ganz aus.
        /// </summary>
        public Boolean BroadcastPresence { get; set; } = true;

        /// <summary>Werden XEP-0280 Carbons an weitere Resourcen verteilt?</summary>
        public Boolean DeliverCarbons { get; set; } = true;

        /// <summary>Beantwortet der Server XEP-0199 Pings, die an ihn gerichtet sind?</summary>
        public Boolean AnswerPings { get; set; } = true;

        /// <summary>
        /// XEP-0198: Handelt der Server Stream Management aus? Auf false
        /// antwortet er auf <c>&lt;enable/&gt;</c> mit <c>&lt;failed/&gt;</c>.
        /// </summary>
        public Boolean OfferStreamManagement { get; set; } = true;

        /// <summary>XEP-0198: Beantwortet der Server ein <c>&lt;r/&gt;</c> des Clients?</summary>
        public Boolean AnswerAckRequests { get; set; } = true;

        /// <summary>
        /// Beantwortet der Server XEP-0199 Pings mit einem Stanza-Fehler statt
        /// mit einem Ergebnis? Für Tests der Fehlerbehandlung.
        /// </summary>
        public Boolean FailPings { get; set; } = false;

        /// <summary>
        /// Beantwortet der Server disco#info-Abfragen mit einem Stanza-Fehler?
        /// </summary>
        public Boolean FailDiscoInfo { get; set; } = false;

        /// <summary>
        /// Lehnt der Server das Resource Binding ab? Ein echter Server tut das
        /// etwa bei <c>&lt;conflict/&gt;</c> oder
        /// <c>&lt;resource-constraint/&gt;</c>.
        /// </summary>
        public Boolean FailBind { get; set; } = false;

        /// <summary>
        /// Kündigt der Server die Legacy-Session (RFC 3921) als zwingend an,
        /// also ohne <c>&lt;optional/&gt;</c>?
        /// </summary>
        public Boolean SessionRequired { get; set; } = false;

        /// <summary>
        /// Antwortet der Server auf eine bereits belegte Resource mit
        /// <c>&lt;conflict/&gt;</c>, statt selbst eine freie zu vergeben?
        /// </summary>
        /// <remarks>
        /// RFC 6120, Abschnitt 7.7.2.2 lässt dem Server beides. Der Default
        /// bleibt das Vergeben einer abweichenden Resource - so verhalten sich
        /// die verbreiteten Server, und die Mehr-Client-Tests im selben Prozess
        /// hängen daran. Für die Gegenprobe gibt es diesen Schalter.
        /// </remarks>
        public Boolean ConflictOnUsedResource { get; set; } = false;

        /// <summary>
        /// Frames, die der Server unmittelbar nach der Bind-Antwort an die
        /// Sitzung schickt - noch bevor der Client Carbons aktiviert und den
        /// Roster abgeholt hat.
        ///
        /// So verhalten sich echte Server: nachgelieferte Nachrichten,
        /// Roster-Pushes und Presence treffen ein, sobald die Resource
        /// gebunden ist, und nicht erst, wenn der Client mit seiner
        /// Aufbauphase fertig ist.
        /// </summary>
        public List<String> DeliverAfterBind { get; } = [];

        #endregion

        #region Events

        /// <summary>Wird für jede vom Client empfangene Stanza ausgelöst.</summary>
        public event Action<XMPPSession, String>? OnStanzaReceived;

        /// <summary>Wird ausgelöst, sobald eine Sitzung erfolgreich gebunden wurde.</summary>
        public event Action<XMPPSession>? OnSessionBound;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Erstellt einen Testserver auf einem freien Port.
        /// </summary>
        /// <param name="domain">Die bediente Domain; muss zum JID der Clients passen.</param>
        /// <param name="port">Fester Port oder 0 für einen freien.</param>
        /// <param name="useTLS">
        /// TLS mit einem selbst erzeugten Zertifikat, wie RFC 6120,
        /// Abschnitt 5 es verlangt. Auf false spricht der Server
        /// <c>ws://</c> - brauchbar für die Fehlersuche mit einem Mitschnitt,
        /// sonst nichts.
        /// </param>
        public XMPPServer(String   domain   = "localhost",
                          Int32    port     = 0,
                          Boolean  useTLS   = true)
        {

            Domain       = domain;
            Port         = port > 0 ? port : FreeTcpPort();
            Certificate  = useTLS ? CreateSelfSignedCertificate(domain) : null;

            _webSocketServer = new XMPPWebSocketServer(this, IPPort.Parse(Port), Certificate);

            _webSocketServer.OnNewWebSocketConnection  += OnConnectionOpenedAsync;
            _webSocketServer.OnCloseMessageReceived    += OnCloseFrameReceivedAsync;
            _webSocketServer.OnTCPConnectionClosed     += OnConnectionClosedAsync;

        }

        #endregion


        #region Konten

        /// <summary>
        /// Legt ein Konto an, an dem sich ein Client anmelden darf.
        /// </summary>
        public XMPPAccount AddAccount(String localPart, String password = "pw")
        {

            var account = new XMPPAccount($"{localPart}@{Domain}", password);

            lock (_lock)
                _accounts[account.BareJid] = account;

            return account;

        }

        /// <summary>Liefert ein Konto oder null.</summary>
        public XMPPAccount? GetAccount(String bareJid)
        {
            lock (_lock)
                return _accounts.TryGetValue(bareJid, out var a) ? a : null;
        }

        #endregion

        #region Sitzungen

        /// <summary>Alle offenen Sitzungen eines Kontos, älteste zuerst.</summary>
        public IReadOnlyList<XMPPSession> SessionsOf(String bareJid)
        {
            lock (_lock)
                return _sessions
                       .Where(s => s.IsOpen &&
                                   String.Equals(s.BareJid, BareOf(bareJid), StringComparison.OrdinalIgnoreCase))
                       .ToList();
        }

        /// <summary>Die Sitzung zu einem Full-JID oder null.</summary>
        public XMPPSession? SessionOf(String fullJid)
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
                              : SessionsOf(jid).Cast<XMPPSession?>().ToArray();

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
            => _webSocketServer.Start().GetAwaiter().GetResult();

        /// <summary>
        /// Der WebSocket-Transport. Das Protokoll steckt vollständig in
        /// <see cref="XMPPServer"/>; Hermod liefert Rahmen, TLS und die
        /// Verbindungsverwaltung.
        /// </summary>
        /// <remarks>
        /// Komposition statt Vererbung: <see cref="XMPPServer"/> soll nach
        /// aussen seine eigene, kleine Oberfläche behalten und nicht die
        /// gesamte von <c>AWebSocketServer</c> erben.
        /// </remarks>
        private sealed class XMPPWebSocketServer : AWebSocketServer
        {

            private readonly XMPPServer _xmpp;

            public XMPPWebSocketServer(XMPPServer         xmpp,
                                       IPPort             port,
                                       X509Certificate2?  certificate)

                : base(TCPPort:                port,

                       // RFC 6120, Abschnitt 5: XMPP gehört über TLS. Ohne
                       // Selektor bleibt der Listener im Klartext.
                       ServerCertificateSelector:  certificate is not null
                                                       ? (_, _) => certificate
                                                       : null,

                       // Sonst verlangte Hermod eine HTTP-Basic-Authentifizierung
                       // beim Handshake. Wer sich anmelden darf, entscheidet in
                       // XMPP das SASL danach.
                       RequireAuthentication:  false,

                       // RFC 7395, Abschnitt 3.3: das Subprotokoll heisst "xmpp".
                       SecWebSocketProtocols:  ["xmpp"],

                       AutoStart:              false)

            {
                _xmpp = xmpp;
            }

            public override Task ProcessTextMessage(DateTimeOffset             Timestamp,
                                                    AWebSocketServer           Server,
                                                    WebSocketServerConnection  Connection,
                                                    EventTracking_Id           EventTrackingId,
                                                    WebSocketFrame             TextFrame,
                                                    String                     TextMessage,
                                                    CancellationToken          CancellationToken)

                => _xmpp.HandleTextMessageAsync(Connection, TextMessage);

        }

        /// <summary>
        /// Eine neue Verbindung steht - ab hier gibt es eine Sitzung dazu.
        /// </summary>
        private Task OnConnectionOpenedAsync(DateTimeOffset             timestamp,
                                             AWebSocketServer           server,
                                             WebSocketServerConnection  connection,
                                             IEnumerable<String>        sharedSubprotocols,
                                             String?                    selectedSubprotocol,
                                             EventTracking_Id           eventTrackingId,
                                             CancellationToken          ct)
        {

            SessionOf(connection);

            return Task.CompletedTask;

        }

        /// <summary>
        /// Liefert die Sitzung zu einer Verbindung und legt sie an, falls es
        /// noch keine gibt.
        /// </summary>
        /// <remarks>
        /// Das Anlegen steht hier und nicht nur im Verbindungsereignis, weil
        /// die Reihenfolge zwischen jenem Ereignis und dem ersten Textframe
        /// nichts ist, worauf sich das Protokoll verlassen sollte.
        /// </remarks>
        private XMPPSession SessionOf(WebSocketServerConnection connection)
        {

            lock (_lock)
            {

                var existing = _sessions.FirstOrDefault(s => ReferenceEquals(s.Connection, connection));

                if (existing is not null)
                    return existing;

                var session = new XMPPSession(_webSocketServer,
                                              connection,
                                              Interlocked.Increment(ref _connectionCounter));

                _sessions.Add(session);

                return session;

            }

        }

        /// <summary>
        /// Ein Textframe des Clients - der Einstieg ins Protokoll.
        /// </summary>
        private async Task HandleTextMessageAsync(WebSocketServerConnection  connection,
                                                  String                     frame)
        {

            var session = SessionOf(connection);

            session.RecordReceived(frame);
            OnStanzaReceived?.Invoke(session, frame);

            if (frame.StartsWith("<open", StringComparison.Ordinal))
                session.OpenCount++;

            try
            {
                await HandleFrameAsync(session, frame, session.OpenCount);
            }
            catch
            {
                // Verbindung abgerissen - im Test der Normalfall
            }

        }

        /// <summary>
        /// Der Client hat den Stream geschlossen.
        /// </summary>
        /// <remarks>
        /// Hermod beantwortet ein Close-Frame von sich aus mit einem eigenen,
        /// wie RFC 6455, Abschnitt 5.5.1 es verlangt, und legt danach die
        /// TCP-Verbindung nieder. Ist <see cref="CompleteCloseHandshake"/>
        /// abgeschaltet, hält dieser Ereignisbehandler die Antwort auf -
        /// Hermod wartet ihn ab, bevor es schliesst.
        ///
        /// Verschieben und nicht unterdrücken: der Client soll Schweigen
        /// sehen, und zwar auf einer offenen Verbindung. Ein abgerissener
        /// Socket beendet sein Warten sofort und liesse den Test bestehen,
        /// ohne dass das Zeitlimit je gegriffen hätte - genau daran wäre die
        /// erste Fassung hier fast vorbeigelaufen.
        /// </remarks>
        private async Task OnCloseFrameReceivedAsync(DateTimeOffset                    timestamp,
                                                     AWebSocketServer                  server,
                                                     WebSocketServerConnection         connection,
                                                     WebSocketFrame                    frame,
                                                     EventTracking_Id                  eventTrackingId,
                                                     WebSocketFrame.ClosingStatusCode  statusCode,
                                                     String?                           reason,
                                                     CancellationToken                 ct)
        {

            if (CompleteCloseHandshake)
                return;

            try
            {
                await Task.Delay(SilentCloseDelay, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Server fährt herunter - dann ist die Verzögerung erledigt.
            }

        }

        /// <summary>
        /// Wie lange ein Server mit abgeschaltetem
        /// <see cref="CompleteCloseHandshake"/> schweigt. Muss über dem
        /// Zeitlimit liegen, das der Client seinem Close-Handshake gibt (drei
        /// Sekunden), sonst prüft der Test nicht das Zeitlimit, sondern nur
        /// eine langsame Antwort.
        /// </summary>
        private static readonly TimeSpan SilentCloseDelay = TimeSpan.FromSeconds(6);

        /// <summary>
        /// Die Verbindung ist weg - egal ob ordentlich, abgerissen oder an
        /// einer Ausnahme: die Kontakte müssen es erfahren.
        /// </summary>
        private async Task OnConnectionClosedAsync(DateTimeOffset             timestamp,
                                                   AWebSocketServer           server,
                                                   WebSocketServerConnection  connection,
                                                   EventTracking_Id           eventTrackingId,
                                                   String?                    reason,
                                                   CancellationToken          ct)
        {

            XMPPSession? session;

            lock (_lock)
                session = _sessions.FirstOrDefault(s => ReferenceEquals(s.Connection, connection));

            if (session is not null)
                await AnnounceUnavailableAsync(session);

        }

        /// <summary>
        /// Meldet eine beendete Sitzung bei ihren Kontakten ab.
        /// </summary>
        /// <remarks>
        /// RFC 6121, Abschnitt 4.5.2 (Server Processing of Outbound
        /// Unavailable Presence): Ein Client kann seine Abmeldung nicht mehr
        /// schicken, wenn ihm die Verbindung unter den Füssen wegbricht -
        /// also erzeugt der Server sie in seinem Namen. Ohne das führen die
        /// Kontakte die Resource für immer als online.
        ///
        /// Empfänger sind dieselben wie bei jeder anderen Presence: die
        /// Abmeldung ist eine Auskunft über den eigenen Zustand und darf
        /// Fremde ebenso wenig erreichen wie die Anmeldung.
        /// </remarks>
        private async Task AnnounceUnavailableAsync(XMPPSession session)
        {

            // Hat der Client sich selbst abgemeldet, ist die Sache erledigt.
            if (!session.IsAvailable || session.FullJid is null)
                return;

            session.IsAvailable   = false;
            session.LastPresence  = null;

            // Beim Herunterfahren des Servers geht es an niemanden mehr.
            if (!RouteStanzas || !BroadcastPresence || _cts.IsCancellationRequested)
                return;

            var stanza = $"<presence type='unavailable' from='{session.FullJid}'/>";

            foreach (var target in PresenceTargetsOf(session))
                await target.SendAsync(stanza);

        }

        #endregion

        #region Protokollbehandlung

        private async Task HandleFrameAsync(XMPPSession session, String frame, Int32 openCount)
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

            // Steht vor der Stream-Management-Abfrage, aber die prüft ohnehin
            // auf den Namensraum - sonst hielte sie ein <response/> für ein
            // <r/>, weil beide mit "<r" beginnen.
            if (frame.StartsWith("<response", StringComparison.Ordinal))
            {
                await HandleSaslResponseAsync(session, frame);
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

            if (frame.Contains("urn:xmpp:sm:3", StringComparison.Ordinal))
            {
                await HandleStreamManagementAsync(session, frame);
                return;
            }

        }

        /// <summary>
        /// XEP-0198: <c>&lt;enable/&gt;</c>, <c>&lt;r/&gt;</c> und <c>&lt;a/&gt;</c>.
        /// </summary>
        private async Task HandleStreamManagementAsync(XMPPSession session, String frame)
        {

            if (frame.StartsWith("<enable", StringComparison.Ordinal))
            {

                if (!OfferStreamManagement)
                {
                    await session.SendAsync(
                        "<failed xmlns='urn:xmpp:sm:3'>" +
                        "<feature-not-implemented xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></failed>");
                    return;
                }

                // Zähler zuerst zurücksetzen: das <enabled/> selbst ist eine
                // Nonza und zählt nicht mit.
                session.EnableStreamManagement();

                await session.SendAsync(
                    $"<enabled xmlns='urn:xmpp:sm:3' id='sm-{session.ConnectionNumber}'/>");

                return;

            }

            // Der Client fragt unseren Empfangszähler ab.
            if (frame.StartsWith("<r", StringComparison.Ordinal))
            {

                if (AnswerAckRequests)
                    await session.SendAsync(
                        $"<a xmlns='urn:xmpp:sm:3' h='{session.StanzasReceivedFromClient}'/>");

                return;

            }

            // Der Client meldet seinen Empfangszähler.
            if (frame.StartsWith("<a", StringComparison.Ordinal))
            {

                var h = Regex.Match(frame, @"h=['""](\d+)['""]");

                if (h.Success && UInt32.TryParse(h.Groups[1].Value, out var value))
                    session.LastAckFromClient = value;

                return;

            }

        }

        private async Task HandleStreamOpenAsync(XMPPSession session, Int32 openCount)
        {

            await session.SendAsync(
                $"<open xmlns='urn:ietf:params:xml:ns:xmpp-framing' from='{Domain}' id='stream-{session.ConnectionNumber}' version='1.0'/>");

            if (openCount == 1)
                await session.SendAsync(
                    "<stream:features xmlns:stream='http://etherx.jabber.org/streams'>" +
                    "<mechanisms xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>" +
                    String.Concat(OfferedSaslMechanisms.Select(m => $"<mechanism>{m}</mechanism>")) +
                    "</mechanisms></stream:features>");
            else
                await session.SendAsync(
                    "<stream:features xmlns:stream='http://etherx.jabber.org/streams'>" +
                    "<bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'/>" +
                    (SessionRequired
                         ? "<session xmlns='urn:ietf:params:xml:ns:xmpp-session'/>"
                         : "<session xmlns='urn:ietf:params:xml:ns:xmpp-session'><optional/></session>") +
                    // XEP-0198, Abschnitt 3 zeigt das Feature genau so: das
                    // <optional/> gehört zum <sm/> und sagt nichts über die
                    // Legacy-Session aus.
                    "<sm xmlns='urn:xmpp:sm:3'><optional/></sm>" +
                    "</stream:features>");

        }

        private async Task HandleAuthAsync(XMPPSession session, String frame)
        {

            var payload    = Regex.Match(frame, @"<auth[^>]*>([^<]*)</auth>").Groups[1].Value;
            var mechanism  = Attr(frame, "mechanism") ?? "PLAIN";

            // Ein Mechanismus, den der Server gar nicht angeboten hat, ist
            // abzulehnen - sonst liesse sich die Aushandlung umgehen.
            if (!OfferedSaslMechanisms.Contains(mechanism, StringComparer.Ordinal))
            {
                await session.SendAsync(
                    "<failure xmlns='urn:ietf:params:xml:ns:xmpp-sasl'><invalid-mechanism/></failure>");
                return;
            }

            if (ScramMechanismOf(mechanism) is SCRAMMechanism scram)
            {
                await BeginScramAsync(session, payload, scram);
                return;
            }

            await HandlePlainAsync(session, payload);

        }

        /// <summary>
        /// SASL PLAIN (RFC 4616): base64( \0 benutzer \0 passwort ).
        /// </summary>
        private async Task HandlePlainAsync(XMPPSession session, String payload)
        {

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

            if (account is null || !account.Credentials.Verify(password))
            {
                await session.SendAsync(
                    "<failure xmlns='urn:ietf:params:xml:ns:xmpp-sasl'><not-authorized/></failure>");
                return;
            }

            session.Account = account;
            await session.SendAsync("<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'/>");

        }

        /// <summary>
        /// SCRAM, erste Hälfte: client-first-message rein,
        /// server-first-message raus (RFC 5802, Abschnitt 5).
        /// </summary>
        private async Task BeginScramAsync(XMPPSession     session,
                                           String          payload,
                                           SCRAMMechanism  mechanism)
        {

            var exchange = SCRAMExchange.Begin(payload,
                                               mechanism,
                                               user => GetAccount($"{user}@{Domain}"));

            if (exchange is null)
            {
                session.Scram = null;
                await session.SendAsync(
                    "<failure xmlns='urn:ietf:params:xml:ns:xmpp-sasl'><not-authorized/></failure>");
                return;
            }

            session.Scram = exchange;

            await session.SendAsync(
                $"<challenge xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>{exchange.Challenge}</challenge>");

        }

        /// <summary>
        /// SCRAM, zweite Hälfte: client-final-message rein, bei Erfolg
        /// <c>&lt;success/&gt;</c> samt Serversignatur raus.
        /// </summary>
        private async Task HandleSaslResponseAsync(XMPPSession session, String frame)
        {

            var exchange = session.Scram;

            // Ein <response/> ohne vorangegangenes <auth/> gehört zu keinem
            // Austausch.
            if (exchange is null)
            {
                await session.SendAsync(
                    "<failure xmlns='urn:ietf:params:xml:ns:xmpp-sasl'><not-authorized/></failure>");
                return;
            }

            session.Scram = null;

            var payload      = Regex.Match(frame, @"<response[^>]*>([^<]*)</response>").Groups[1].Value;
            var serverFinal  = exchange.Complete(payload);

            if (serverFinal is null)
            {
                await session.SendAsync(
                    "<failure xmlns='urn:ietf:params:xml:ns:xmpp-sasl'><not-authorized/></failure>");
                return;
            }

            session.Account = exchange.Account;

            if (OmitScramSignature)
                serverFinal = "";

            else if (CorruptScramSignature)
                serverFinal = Convert.ToBase64String(
                                  Encoding.UTF8.GetBytes(
                                      $"v={Convert.ToBase64String(new Byte[32])}"));

            // RFC 5802, Abschnitt 3: die Serversignatur gehört mitgeschickt.
            // Ohne sie kann der Client nicht prüfen, dass die Gegenstelle das
            // Passwort ebenfalls kennt.
            await session.SendAsync(
                $"<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>{serverFinal}</success>");

        }

        /// <summary>
        /// Der SCRAM-Mechanismus hinter einem Namen, oder null bei PLAIN und
        /// allem Unbekannten.
        /// </summary>
        internal static SCRAMMechanism? ScramMechanismOf(String mechanism)
            => mechanism switch {
                   "SCRAM-SHA-1"    => SCRAMMechanism.ScramSha1,
                   "SCRAM-SHA-256"  => SCRAMMechanism.ScramSha256,
                   _                => null
               };

        private async Task HandleIqAsync(XMPPSession session, String frame)
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
                if (FailPings)
                    await session.SendAsync(StanzaErrorIq(id, "service-unavailable"));

                else if (AnswerPings)
                    await session.SendAsync($"<iq type='result' id='{id}' from='{Domain}'/>");

                return;
            }

            // XEP-0030 disco#info über den Server
            if (frame.Contains("http://jabber.org/protocol/disco#info", StringComparison.Ordinal) && type == "get")
            {

                if (FailDiscoInfo)
                {
                    await session.SendAsync(StanzaErrorIq(id, "item-not-found", "modify",
                                                          "Diesen Node gibt es hier nicht."));
                    return;
                }

                await session.SendAsync(
                    $"<iq type='result' id='{id}' from='{Domain}'>" +
                    "<query xmlns='http://jabber.org/protocol/disco#info'>" +
                    "<identity category='server' type='im' name='XMPPServer'/>" +
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

        private async Task HandleBindAsync(XMPPSession session, String frame, String? id)
        {

            if (FailBind)
            {
                await session.SendAsync(StanzaErrorIq(id, "not-allowed", "cancel",
                                                      "Diese Resource darf nicht gebunden werden."));
                return;
            }

            var requested  = Regex.Match(frame, @"<resource>([^<]*)</resource>").Groups[1].Value;
            var gewuenscht = !String.IsNullOrEmpty(requested);
            var konflikt   = false;

            // Der Client verwendet console-{ProcessId} als Resource. Laufen mehrere
            // Clients im selben Prozess, kollidieren sie - der Server vergibt dann
            // wie ein echter Server eine abweichende, eindeutige Resource.
            lock (_lock)
            {

                Boolean Belegt(String kandidat)
                    => _sessions.Any(s => s.IsOpen &&
                                          String.Equals(s.BareJid, session.BareJid, StringComparison.OrdinalIgnoreCase) &&
                                          String.Equals(s.Resource, kandidat, StringComparison.Ordinal));

                // RFC 6120, Abschnitt 7.7.2.2: Auf eine belegte Resource darf
                // der Server auch schlicht mit <conflict/> antworten.
                if (gewuenscht && ConflictOnUsedResource && Belegt(requested))
                    konflikt = true;

                else
                {

                    var basis     = gewuenscht ? requested : "auto";
                    var resource  = basis;
                    var n         = 2;

                    while (Belegt(resource))
                        resource = $"{basis}-{n++}";

                    session.Resource = resource;

                }

            }

            if (konflikt)
            {
                await session.SendAsync(StanzaErrorIq(id, "conflict", "cancel",
                                                      "Diese Resource ist bereits gebunden."));
                return;
            }

            await session.SendAsync(
                $"<iq type='result' id='{id}'>" +
                "<bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'>" +
                $"<jid>{session.FullJid}</jid>" +
                "</bind></iq>");

            OnSessionBound?.Invoke(session);

            // Alles, was ein echter Server direkt nach dem Binding nachliefert.
            foreach (var frameToDeliver in DeliverAfterBind.ToArray())
                await session.SendAsync(frameToDeliver.Replace("{jid}", session.FullJid));

        }

        private async Task HandleRosterAsync(XMPPSession session, String frame, String? id, String? type)
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

                if (!m.Success)
                {
                    await session.SendAsync($"<iq type='result' id='{id}'/>");
                    return;
                }

                var attrs         = m.Groups[1].Value;
                var jid           = AttrIn(attrs, "jid");
                var name          = AttrIn(attrs, "name");
                var subscription  = AttrIn(attrs, "subscription");

                if (jid is null)
                {
                    await session.SendAsync($"<iq type='result' id='{id}'/>");
                    return;
                }

                if (subscription == "remove")
                {
                    account.RemoveRosterEntry(jid);
                    await session.SendAsync($"<iq type='result' id='{id}'/>");

                    var entfernt = $"<item jid='{jid}' subscription='remove'/>";

                    foreach (var s in SessionsOf(account.BareJid))
                        await s.SendAsync(
                            $"<iq type='set' id='push-{Guid.NewGuid():N}' to='{s.FullJid}'>" +
                            $"<query xmlns='jabber:iq:roster'>{entfernt}</query></iq>");

                    return;
                }

                // RFC 6121, Abschnitt 2.3.2: Ein Roster-Set ändert Name und
                // Gruppen. Den Subscription-Zustand fasst es nicht an - der
                // gehört dem Handshake aus Abschnitt 3. Das fehlende Attribut
                // als 'none' zu übernehmen hätte eine gerade erst erteilte
                // Berechtigung beim blossen Umbenennen wieder gelöscht.
                var bestand = account.Roster.FirstOrDefault(
                                  e => String.Equals(e.Jid, jid, StringComparison.OrdinalIgnoreCase));

                account.SetRosterEntry(new RosterEntry(jid,
                                                       name,
                                                       bestand?.Subscription ?? "none",
                                                       bestand?.Ask));

                await session.SendAsync($"<iq type='result' id='{id}'/>");

                // Der Push wird aus dem gespeicherten Eintrag neu gebaut und
                // nicht aus dem Text des Clients zusammengesetzt. Ein <item/>
                // mit getrenntem Schluss-Tag - was RosterStanzaBuilder.SetItem
                // erzeugt - ergäbe sonst ein offenes Element im Push und damit
                // unwohlgeformtes XML.
                await PushRosterEntryAsync(account, jid);

            }

        }

        private async Task HandleMessageAsync(XMPPSession session, String frame)
        {

            if (!RouteStanzas)
                return;

            var to = Attr(frame, "to");

            if (to is null || session.FullJid is null)
                return;

            var stamped = StampFrom(frame, session.FullJid);

            // Zustellung an den Empfänger
            var recipients = to.Contains('/')
                                 ? (SessionOf(to) is { } one ? [one] : Array.Empty<XMPPSession>())
                                 : SessionsOf(to).ToArray();

            XMPPSession? primary = null;

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

        private async Task HandlePresenceAsync(XMPPSession session, String frame)
        {

            if (!RouteStanzas || session.FullJid is null)
                return;

            var type     = Attr(frame, "type");
            var to       = Attr(frame, "to");
            var stamped  = StampFrom(frame, session.FullJid);

            // Presence-Probe: die Frage nach dem Zustand eines Kontakts
            // (RFC 6121, Abschnitt 4.3).
            if (type == "probe" && to is not null)
            {
                await AnswerPresenceProbeAsync(session, to);
                return;
            }

            // Der Subscription-Handshake (RFC 6121, Abschnitt 3).
            if (to is not null &&
                type is "subscribe" or "subscribed" or "unsubscribe" or "unsubscribed")
            {
                await HandleSubscriptionAsync(session, type, BareOf(to));
                return;
            }

            // Sonstige gerichtete Presence geht genau dorthin.
            if (to is not null)
            {
                await RouteToAsync(to, stamped);
                return;
            }

            var initial = !session.HasSentInitialPresence;

            session.LastPresence            = stamped;
            session.HasSentInitialPresence  = true;
            session.IsAvailable             = type is null;

            if (!BroadcastPresence)
                return;

            foreach (var target in PresenceTargetsOf(session))
                await target.SendAsync(stamped);

            // RFC 6121, Abschnitt 4.3.1: Nach der ersten Presence fragt der
            // Server für den Client den Zustand von dessen Kontakten ab. Weil
            // hier alle Konten auf derselben Instanz liegen, liefern wir gleich
            // aus, was wir wissen - das Ergebnis einer Probe wäre dasselbe.
            if (initial && type is null)
                await SendKnownPresencesToAsync(session);

        }

        /// <summary>
        /// Der Subscription-Handshake nach RFC 6121, Abschnitt 3.
        /// </summary>
        /// <remarks>
        /// Ein echter Server sieht davon immer nur eine Hälfte: die Abschnitte
        /// trennen die ausgehende Verarbeitung beim Absender von der
        /// eingehenden beim Empfänger, weil dazwischen die S2S-Verbindung
        /// liegt. Hier liegen beide Konten in derselben Instanz, also fallen
        /// die Hälften zusammen - was die Roster beider Seiten in einem Schritt
        /// ändert.
        ///
        /// Beide Roster-Einträge müssen dabei zueinander passen: <c>from</c>
        /// beim einen heisst <c>to</c> beim anderen. Jede Richtung ändert
        /// deshalb nur ihre eigene Hälfte.
        /// </remarks>
        /// <param name="sender">Die Sitzung, die den Handshake-Schritt schickt.</param>
        /// <param name="type">subscribe, subscribed, unsubscribe oder unsubscribed.</param>
        /// <param name="peerBareJid">Der Bare-JID der Gegenseite.</param>
        private async Task HandleSubscriptionAsync(XMPPSession sender, String type, String peerBareJid)
        {

            var senderAccount  = sender.Account;
            var peerAccount    = GetAccount(peerBareJid);

            if (senderAccount is null)
                return;

            // Nach RFC 6121, Abschnitt 3.1.1 trägt der Handshake immer den
            // Bare-JID - die Anfrage gilt dem Konto, nicht einer Resource.
            var stanza = $"<presence from='{senderAccount.BareJid}' to='{peerBareJid}' type='{type}'/>";

            switch (type)
            {

                // Abschnitt 3.1.2: Der Eintrag entsteht mit subscription='none'
                // - erlaubt ist noch nichts -, und ask='subscribe' hält fest,
                // dass die Anfrage offen ist.
                case "subscribe":
                    UpdateRosterEntry(senderAccount, peerBareJid, subscription: null, ask: AskChange.Set);
                    await PushRosterEntryAsync(senderAccount, peerBareJid);
                    break;

                // Abschnitt 3.1.5 und 3.1.6: Der Zustimmende erlaubt dem
                // Gegenüber, ihn zu sehen; beim Gegenüber ist damit die Anfrage
                // erledigt und die Gegenrichtung gesetzt.
                case "subscribed":
                    UpdateRosterEntry(senderAccount, peerBareJid,
                                      GrantFrom(senderAccount.SubscriptionOf(peerBareJid)));
                    await PushRosterEntryAsync(senderAccount, peerBareJid);

                    if (peerAccount is not null)
                    {
                        UpdateRosterEntry(peerAccount, senderAccount.BareJid,
                                          GrantTo(peerAccount.SubscriptionOf(senderAccount.BareJid)),
                                          ask: AskChange.Clear);
                        await PushRosterEntryAsync(peerAccount, senderAccount.BareJid);
                    }
                    break;

                // Abschnitt 3.2.2 und 3.2.3: der Entzug, spiegelbildlich.
                case "unsubscribed":
                    UpdateRosterEntry(senderAccount, peerBareJid,
                                      RevokeFrom(senderAccount.SubscriptionOf(peerBareJid)));
                    await PushRosterEntryAsync(senderAccount, peerBareJid);

                    if (peerAccount is not null)
                    {
                        UpdateRosterEntry(peerAccount, senderAccount.BareJid,
                                          RevokeTo(peerAccount.SubscriptionOf(senderAccount.BareJid)),
                                          ask: AskChange.Clear);
                        await PushRosterEntryAsync(peerAccount, senderAccount.BareJid);
                    }
                    break;

                // Abschnitt 3.3.2 und 3.3.3: Der Absender kündigt seine eigene
                // Subscription - hier ändert sich also seine 'to'-Hälfte.
                case "unsubscribe":
                    UpdateRosterEntry(senderAccount, peerBareJid,
                                      RevokeTo(senderAccount.SubscriptionOf(peerBareJid)),
                                      ask: AskChange.Clear);
                    await PushRosterEntryAsync(senderAccount, peerBareJid);

                    if (peerAccount is not null)
                    {
                        UpdateRosterEntry(peerAccount, senderAccount.BareJid,
                                          RevokeFrom(peerAccount.SubscriptionOf(senderAccount.BareJid)));
                        await PushRosterEntryAsync(peerAccount, senderAccount.BareJid);
                    }
                    break;

            }

            // Die Stanza selbst geht an die Gegenseite: der Kontakt soll die
            // Anfrage sehen, der Antragsteller die Antwort.
            await RouteToAsync(peerBareJid, stanza);

            // Abschnitt 3.1.5: "The contact's server MUST then also send current
            // presence to the user from each of the contact's available
            // resources." Ohne das wartet der Antragsteller, bis der Kontakt
            // das nächste Mal von sich aus etwas schickt.
            if (type == "subscribed")
                await SendOwnPresenceToAsync(sender, peerBareJid);

            // Abschnitt 3.2.2: "the contact's server MUST send a presence stanza
            // of type 'unavailable' from all of the contact's online
            // resources". Sonst behielte die Gegenseite den letzten bekannten
            // Zustand, obwohl sie ihn nicht mehr sehen darf.
            if (type == "unsubscribed")
                await SendOwnUnavailableToAsync(senderAccount, peerBareJid);

            // Spiegelbildlich zum Entzug: wer selbst kündigt, soll den Kontakt
            // ebenfalls nicht mehr als anwesend führen.
            if (type == "unsubscribe" && peerAccount is not null)
                await SendOwnUnavailableToAsync(peerAccount, senderAccount.BareJid);

        }

        /// <summary>
        /// Was mit dem ask-Vermerk eines Roster-Eintrags geschehen soll.
        /// </summary>
        /// <remarks>
        /// Drei Fälle, und null taugt für höchstens zwei davon: eine Anfrage
        /// vermerken, eine beantwortete löschen, oder den Vermerk gar nicht
        /// anfassen.
        /// </remarks>
        private enum AskChange
        {
            Keep,
            Set,
            Clear
        }

        /// <summary>
        /// Setzt Subscription und/oder ask eines Roster-Eintrags und legt ihn
        /// an, falls es ihn noch nicht gibt. Eine Subscription von null lässt
        /// den bisherigen Wert stehen.
        /// </summary>
        private static void UpdateRosterEntry(XMPPAccount  account,
                                              String       contactBareJid,
                                              String?      subscription  = null,
                                              AskChange    ask           = AskChange.Keep)
        {

            var vorher = account.Roster.FirstOrDefault(
                             e => String.Equals(e.Jid, contactBareJid, StringComparison.OrdinalIgnoreCase));

            account.SetRosterEntry(new RosterEntry(contactBareJid,
                                                   vorher?.Name,
                                                   subscription ?? vorher?.Subscription ?? "none",
                                                   ask switch {
                                                       AskChange.Set    => "subscribe",
                                                       AskChange.Clear  => null,
                                                       _                => vorher?.Ask
                                                   }));

        }

        /// <summary>
        /// Schickt einen Roster-Push für genau einen Eintrag an alle Resourcen
        /// des Kontos (RFC 6121, Abschnitt 2.1.6).
        /// </summary>
        private async Task PushRosterEntryAsync(XMPPAccount account, String contactBareJid)
        {

            var entry = account.Roster.FirstOrDefault(
                            e => String.Equals(e.Jid, contactBareJid, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
                return;

            var item = $"<item jid='{entry.Jid}'" +
                       (entry.Name is not null ? $" name='{entry.Name}'" : "") +
                       (entry.Ask  is not null ? $" ask='{entry.Ask}'"   : "") +
                       $" subscription='{entry.Subscription}'/>";

            foreach (var s in SessionsOf(account.BareJid))
                await s.SendAsync($"<iq type='set' id='push-{Guid.NewGuid():N}' to='{s.FullJid}'>" +
                                  $"<query xmlns='jabber:iq:roster'>{item}</query></iq>");

        }

        /// <summary>
        /// Schickt die aktuelle Presence einer Sitzung an einen einzelnen JID.
        /// </summary>
        private async Task SendOwnPresenceToAsync(XMPPSession sender, String peerBareJid)
        {

            if (sender.LastPresence is null)
                return;

            await RouteToAsync(peerBareJid, sender.LastPresence);

        }

        /// <summary>
        /// Meldet alle Resourcen eines Kontos bei einem einzelnen JID ab.
        /// </summary>
        private async Task SendOwnUnavailableToAsync(XMPPAccount account, String peerBareJid)
        {

            foreach (var s in SessionsOf(account.BareJid).Where(s => s.IsAvailable && s.FullJid is not null))
                await RouteToAsync(peerBareJid, $"<presence type='unavailable' from='{s.FullJid}'/>");

        }

        /// <summary>
        /// Wer bekommt die ungerichtete Presence dieser Sitzung?
        /// </summary>
        /// <remarks>
        /// RFC 6121, Abschnitt 4.2.2: die Kontakte mit <c>from</c> oder
        /// <c>both</c>. Dazu nach Abschnitt 4.4.2 die weiteren Resourcen des
        /// eigenen Kontos, für die es keinen Roster-Eintrag braucht.
        /// </remarks>
        private IEnumerable<XMPPSession> PresenceTargetsOf(XMPPSession session)
        {

            var account = session.Account;

            if (account is null)
                yield break;

            foreach (var other in Sessions.Where(s => s != session && s.FullJid is not null))
            {

                if (String.Equals(other.BareJid, account.BareJid, StringComparison.OrdinalIgnoreCase) ||
                    account.IsPresenceSubscriber(other.BareJid!))
                {
                    yield return other;
                }

            }

        }

        /// <summary>
        /// Liefert einer frisch angemeldeten Sitzung den bekannten Zustand
        /// ihrer Kontakte nach.
        /// </summary>
        private async Task SendKnownPresencesToAsync(XMPPSession session)
        {

            var account = session.Account;

            if (account is null)
                return;

            foreach (var other in Sessions.Where(s => s != session &&
                                                      s.FullJid     is not null &&
                                                      s.LastPresence is not null))
            {

                // Ob ein Kontakt seinen Zustand preisgibt, entscheidet sein
                // Roster, nicht unserer - deshalb wird hier die Gegenseite
                // gefragt.
                var eigeneResource = String.Equals(other.BareJid, account.BareJid,
                                                   StringComparison.OrdinalIgnoreCase);

                if (eigeneResource ||
                    other.Account?.IsPresenceSubscriber(account.BareJid) == true)
                {
                    await session.SendAsync(other.LastPresence!);
                }

            }

        }

        /// <summary>
        /// Beantwortet eine Presence-Probe (RFC 6121, Abschnitt 4.3.2).
        /// </summary>
        /// <remarks>
        /// Fehlt die Berechtigung, bleibt die Probe unbeantwortet. Der Abschnitt
        /// stellt dem Server <c>&lt;unsubscribed/&gt;</c> und Schweigen frei -
        /// Schweigen verrät nicht einmal, ob es das Konto überhaupt gibt.
        /// </remarks>
        private async Task AnswerPresenceProbeAsync(XMPPSession prober, String to)
        {

            var account = GetAccount(BareOf(to));

            if (account is null ||
                prober.BareJid is null ||
                !account.IsPresenceSubscriber(prober.BareJid))
            {
                return;
            }

            foreach (var s in SessionsOf(account.BareJid).Where(s => s.LastPresence is not null))
                await prober.SendAsync(s.LastPresence!);

        }

        private async Task RouteToAsync(String to, String stanza)
        {

            var targets = to.Contains('/')
                              ? (SessionOf(to) is { } one ? [one] : Array.Empty<XMPPSession>())
                              : SessionsOf(to).ToArray();

            foreach (var t in targets)
                await t.SendAsync(stanza);

        }

        #endregion

        #region Subscription-Zustände

        // Die vier Übergänge aus RFC 6121, Abschnitt 3. Der Subscription-Wert
        // steht immer aus Sicht des Roster-Eigentümers: 'from' heisst "der
        // Kontakt sieht mich", 'to' heisst "ich sehe den Kontakt". Deshalb
        // ändert jede Richtung nur ihre eigene Hälfte und lässt die andere
        // stehen - genau daran scheitert eine Umsetzung, die die vier Zustände
        // als eine Skala von none bis both behandelt.

        /// <summary>Der Kontakt darf uns nun sehen: none→from, to→both.</summary>
        internal static String GrantFrom(String? subscription)
            => subscription is "to" or "both" ? "both" : "from";

        /// <summary>Der Kontakt darf uns nicht mehr sehen: from→none, both→to.</summary>
        internal static String RevokeFrom(String? subscription)
            => subscription is "to" or "both" ? "to" : "none";

        /// <summary>Wir dürfen den Kontakt nun sehen: none→to, from→both.</summary>
        internal static String GrantTo(String? subscription)
            => subscription is "from" or "both" ? "both" : "to";

        /// <summary>Wir dürfen den Kontakt nicht mehr sehen: to→none, both→from.</summary>
        internal static String RevokeTo(String? subscription)
            => subscription is "from" or "both" ? "from" : "none";

        #endregion

        #region Hilfsfunktionen

        /// <summary>
        /// Baut ein <c>iq type='error'</c> nach RFC 6120, Abschnitt 8.3.
        /// </summary>
        internal String StanzaErrorIq(String?  id,
                                      String   condition,
                                      String   errorType  = "cancel",
                                      String?  text       = null)

            => $"<iq type='error' id='{id}' from='{Domain}'>" +
               $"<error type='{errorType}'>" +
               $"<{condition} xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
               (text is not null
                    ? $"<text xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'>{text}</text>"
                    : "") +
               "</error></iq>";

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

        /// <summary>
        /// Erzeugt ein selbst signiertes Serverzertifikat für die Domain.
        /// </summary>
        /// <remarks>
        /// Bewusst über die BCL und nicht über Hermods <c>PKIFactory</c>: das
        /// spart die Abhängigkeit auf BouncyCastle und eine dreistufige
        /// CA-Kette, von der hier nichts gebraucht wird.
        ///
        /// Der Umweg über PFX am Ende ist auf Windows nötig. Ein Zertifikat
        /// aus <c>CreateSelfSigned</c> trägt seinen Schlüssel in einer Form,
        /// die <c>SslStream</c> beim Handshake nicht annimmt; erst nach Export
        /// und erneutem Laden ist er brauchbar.
        /// </remarks>
        private static X509Certificate2 CreateSelfSignedCertificate(String domain)
        {

            using var key = RSA.Create(2048);

            var request = new CertificateRequest($"CN={domain}",
                                                 key,
                                                 HashAlgorithmName.SHA256,
                                                 RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, true));

            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature |
                                          X509KeyUsageFlags.KeyEncipherment,
                                          true));

            // Ohne Server Authentication weist die Prüfung des Betriebssystems
            // das Zertifikat auch dann ab, wenn man ihm sonst vertraute.
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], true));

            var alternativeNames = new SubjectAlternativeNameBuilder();
            alternativeNames.AddDnsName(domain);
            alternativeNames.AddDnsName("localhost");
            alternativeNames.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(alternativeNames.Build());

            var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
                                                       DateTimeOffset.UtcNow.AddYears(1));

            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx),
                                                     null);

        }

        /// <summary>
        /// Eine Zertifikatsprüfung für den Client, die genau das Zertifikat
        /// dieses Servers annimmt und sonst nichts.
        /// </summary>
        /// <remarks>
        /// Steht hier, weil nur der Testserver seinen eigenen Fingerabdruck
        /// kennt. Verglichen wird der Fingerabdruck und nicht der Name: zwei
        /// Server dieser Klasse heissen beide "localhost", tragen aber
        /// verschiedene Schlüssel.
        ///
        /// Absichtlich keine Prüfung, die alles durchwinkt. Eine solche wäre
        /// kürzer, hätte aber die Verbindungen der Tests von TLS entkoppelt:
        /// sie kämen dann auch gegen einen beliebigen anderen Server zustande.
        /// </remarks>
        public Boolean IsOwnCertificate(Object            sender,
                                        X509Certificate?  certificate,
                                        X509Chain?        chain,
                                        SslPolicyErrors   errors)

            => Certificate is not null &&
               certificate is not null &&
               String.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256),
                             Certificate.GetCertHashString(HashAlgorithmName.SHA256),
                             StringComparison.OrdinalIgnoreCase);

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

            try { await _webSocketServer.Shutdown(Wait: true); }
            catch { }

            _cts.Dispose();

        }

    }

}
