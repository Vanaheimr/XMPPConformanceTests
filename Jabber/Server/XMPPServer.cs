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

using System.Globalization;
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
        private readonly IXMPPAccountStore _accountStore;
        private readonly CancellationTokenSource _cts = new();
        private readonly Dictionary<String, XMPPAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<XMPPSession> _sessions = [];

        /// <summary>
        /// XEP-0198, Abschnitt 5: abgerissene Streams, die auf ihren
        /// Rückkehrer warten - nach ihrer Kennung.
        /// </summary>
        private readonly Dictionary<String, ParkedStream> _resumable = new(StringComparer.Ordinal);

        private Timer? _resumptionSweeper;
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
        /// Unterstützt der Server Subscription-Pre-Approval (RFC 6121,
        /// Abschnitt 3.4)?
        /// </summary>
        /// <remarks>
        /// Optional für Server <b>und</b> Clients. Der Abschnitt verlangt, dass
        /// ein Server, der es beherrscht, es auch ankündigt - und dass ein
        /// Client es ohne Ankündigung gar nicht erst versucht. Der Schalter
        /// steuert beides gemeinsam: ohne ihn fehlt die Ankündigung, und ein
        /// <c>&lt;presence type='subscribed'/&gt;</c> ohne offene Anfrage
        /// bleibt folgenlos statt vorzumerken.
        /// </remarks>
        public Boolean OfferSubscriptionPreApproval { get; set; } = true;

        /// <summary>
        /// Unterstützt der Server Roster-Versionierung (RFC 6121,
        /// Abschnitt 2.6)?
        /// </summary>
        /// <remarks>
        /// Wie beim Pre-Approval steuert der Schalter beide Seiten der
        /// Abmachung: Ohne ihn fehlt die Ankündigung, ein <c>ver</c> an der
        /// Anfrage wird nicht beachtet, und weder Ergebnis noch Push tragen
        /// eines. Das ist wichtiger, als es klingt - ein Server, der ein
        /// <c>ver</c> stillschweigend übergeht und dennoch ein leeres Ergebnis
        /// schickt, brächte den Client dazu, einen leeren Roster für den
        /// aktuellen Stand zu halten.
        /// </remarks>
        public Boolean OfferRosterVersioning { get; set; } = true;

        /// <summary>
        /// Wie viele unbeantwortete Subscription-Anfragen je Konto aufbewahrt
        /// werden (RFC 6121, Abschnitt 3.1.3).
        /// </summary>
        /// <remarks>
        /// Der Abschnitt verlangt das Aufbewahren und warnt im selben Atemzug
        /// davor: aufgehoben wird, was Fremde schicken, und eine Anfrage darf
        /// beliebigen erweiterten Inhalt tragen. Die Security Warning rät
        /// ausdrücklich zu einer Obergrenze ("limits on the number or size of
        /// inbound presence subscription requests that the server will store
        /// in aggregate or for any given contact").
        ///
        /// Ist die Grenze erreicht, wird die neue Anfrage verworfen statt eine
        /// bereits aufbewahrte zu verdrängen. Andersherum könnte ein Angreifer
        /// die echte Anfrage eines Bekannten gezielt hinausdrängen - der
        /// Kontakt bekäme dann Müll zu sehen und das Erwartete nicht.
        /// </remarks>
        public Int32 MaxStoredSubscriptionRequests { get; set; } = 100;

        /// <summary>
        /// Bewahrt der Server Nachrichten für ein Konto ohne erreichbare
        /// Resource auf (XEP-0160)?
        /// </summary>
        /// <remarks>
        /// RFC 6121, Abschnitt 8.5.2.2.1 stellt zwei Wege nebeneinander: die
        /// Nachricht ablegen oder dem Absender
        /// <c>&lt;service-unavailable/&gt;</c> antworten. Beide sind richtig,
        /// und dieser Schalter wählt zwischen ihnen - abgeschaltet ist der
        /// Server also nicht weniger regelkonform, sondern nur weniger
        /// bequem.
        ///
        /// Was er nicht darf, ist die dritte Möglichkeit: stillschweigend
        /// verwerfen. Genau das tat dieser Server bis hierher, und es ist der
        /// unangenehmste der drei Wege - der Absender hält seine Nachricht für
        /// zugestellt.
        ///
        /// Der Schalter steuert auch die Ankündigung in disco#info
        /// (<c>msgoffline</c>): ein Client soll nicht erst am ausbleibenden
        /// Fehler merken, was der Server mit Nachrichten an Abwesende tut.
        /// </remarks>
        public Boolean StoreOfflineMessages { get; set; } = true;

        /// <summary>
        /// Wie viele Nachrichten je Konto aufbewahrt werden.
        /// </summary>
        /// <remarks>
        /// Aufbewahrt wird, was Fremde schicken - dieselbe Lage wie bei
        /// <see cref="MaxStoredSubscriptionRequests"/>, und ohne Grenze wäre
        /// die Ablage selbst die Schwachstelle. Ist die Grenze erreicht, wird
        /// die neue Nachricht abgewiesen und keine aufbewahrte verdrängt: eine
        /// abgewiesene Nachricht ist dem Absender gemeldet, eine verdrängte
        /// verschwindet unbemerkt.
        /// </remarks>
        public Int32 MaxStoredOfflineMessages { get; set; } = 100;

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

        /// <summary>
        /// Der Weg zu anderen Servern, oder null - dann ist keine fremde
        /// Domain erreichbar und jede Stanza dorthin wird mit
        /// <c>&lt;remote-server-not-found/&gt;</c> beantwortet.
        /// </summary>
        public IServerLinks? ServerLinks { get; set; }

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
        /// Verwirft eingehende Stanzas des Clients, ohne sie zu zählen oder
        /// weiterzureichen.
        /// </summary>
        /// <remarks>
        /// Stellt den einen Fall her, für den der Puffer der unbestätigten
        /// Stanzas auf der Client-Seite überhaupt existiert: die Stanza
        /// verlässt die Leitung erfolgreich und kommt trotzdem nicht an. Im
        /// selben Prozess gibt es ihn sonst nicht - ein abgerissener Socket
        /// lässt das Senden sofort scheitern, und eine nicht gesendete Stanza
        /// wird gar nicht erst mitgezählt.
        ///
        /// Nonzas bleiben unangetastet: ohne sie wären in diesem Zustand weder
        /// <c>&lt;r/&gt;</c> noch <c>&lt;resume/&gt;</c> möglich.
        /// </remarks>
        public Boolean SwallowClientStanzas { get; set; }

        /// <summary>
        /// XEP-0198, Abschnitt 5: Sagt der Server die Wiederaufnahme eines
        /// abgerissenen Streams zu?
        /// </summary>
        public Boolean OfferStreamResumption { get; set; } = true;

        /// <summary>
        /// Wie lange ein abgerissener Stream auf seinen Rückkehrer wartet.
        /// </summary>
        /// <remarks>
        /// Danach gilt die Sitzung als beendet, und die Abmeldung, die der
        /// Abriss aufgeschoben hat, wird nachgeholt. Ohne diese Frist bliebe
        /// jede abgerissene Resource für ihre Kontakte auf ewig online.
        /// </remarks>
        public TimeSpan ResumptionTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Wie viele abgerissene Streams gerade auf ihren Rückkehrer warten.
        /// </summary>
        public Int32 ResumableStreamCount
        {
            get { lock (_lock) return _resumable.Count; }
        }

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

        /// <summary>
        /// Wird ausgelöst, wenn eine Stanza von einem anderen Server abgewiesen
        /// wurde - mit der Domain der Gegenstelle und dem Grund.
        /// </summary>
        public event Action<String, String>? OnRemoteStanzaRejected;

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
        /// <param name="accountStore">
        /// Wo die Konten liegen; null nimmt einen
        /// <see cref="InMemoryAccountStore"/>, der beim Beenden verschwindet.
        /// Vorhandene Konten werden sofort eingelesen.
        /// </param>
        /// <param name="certificate">
        /// Ein von aussen gesetztes Serverzertifikat; null erzeugt ein selbst
        /// signiertes für <paramref name="domain"/>.
        /// </param>
        public XMPPServer(String              domain         = "localhost",
                          Int32               port           = 0,
                          Boolean             useTLS         = true,
                          IXMPPAccountStore?  accountStore   = null,
                          X509Certificate2?   certificate    = null)
        {

            Domain       = domain;
            Port         = port > 0 ? port : FreeTcpPort();

            // Ein selbst signiertes Zertifikat kann keine fremde Gegenstelle
            // prüfen - sie müsste genau dieses eine Zertifikat kennen, und es
            // entsteht bei jedem Start neu. Für einen Lauf gegen ejabberd oder
            // Prosody muss das Zertifikat von aussen kommen, aus einer Kette,
            // der beide Seiten trauen. Das gilt genauso für jeden Betrieb, der
            // kein Test ist.
            Certificate  = useTLS
                               ? certificate ?? CreateSelfSignedCertificate(domain)
                               : null;

            _accountStore = accountStore ?? new InMemoryAccountStore();

            foreach (var account in _accountStore.Load())
            {
                account.OnChanged        = _accountStore.Save;
                _accounts[account.BareJid] = account;
            }

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

            var account = new XMPPAccount($"{localPart}@{Domain}", password) {
                              OnChanged = _accountStore.Save
                          };

            lock (_lock)
                _accounts[account.BareJid] = account;

            _accountStore.Save(account);

            return account;

        }

        /// <summary>Liefert ein Konto oder null.</summary>
        public XMPPAccount? GetAccount(String bareJid)
        {
            lock (_lock)
                return _accounts.TryGetValue(bareJid, out var a) ? a : null;
        }

        /// <summary>Alle Konten dieses Servers.</summary>
        public IReadOnlyList<XMPPAccount> Accounts
        {
            get { lock (_lock) return _accounts.Values.ToList(); }
        }

        /// <summary>
        /// Entfernt ein Konto, auch aus dem Kontenspeicher. Bestehende
        /// Sitzungen bleiben davon unberührt.
        /// </summary>
        public void RemoveAccount(String bareJid)
        {

            lock (_lock)
            {
                if (_accounts.Remove(bareJid, out var account))
                    account.OnChanged = null;
            }

            _accountStore.Delete(bareJid);

        }

        #endregion

        #region Sitzungen

        /// <summary>
        /// Alle zustellbaren Sitzungen eines Kontos, älteste zuerst.
        /// </summary>
        /// <remarks>
        /// Zustellbar heisst nicht offen: ein aufgehobener Stream (XEP-0198,
        /// Abschnitt 5) hat keine Verbindung mehr, wartet aber auf seinen
        /// Rückkehrer und nimmt entgegen, was in der Zwischenzeit für ihn
        /// eintrifft. Bliebe er hier draussen, käme während einer Störung
        /// nichts mehr an, und die Wiederaufnahme rettete nur die letzten
        /// Stanzas vor dem Abriss.
        /// </remarks>
        public IReadOnlyList<XMPPSession> SessionsOf(String bareJid)
        {
            lock (_lock)
                return _sessions
                       .Where(s => (s.IsOpen || s.ResumptionId is not null) &&
                                   String.Equals(s.BareJid, BareOf(bareJid), StringComparison.OrdinalIgnoreCase))
                       .ToList();
        }

        /// <summary>
        /// Die zustellbare Sitzung zu einem Full-JID oder null - offen oder
        /// aufgehoben, wie bei <see cref="SessionsOf"/>.
        /// </summary>
        /// <remarks>
        /// Die offene zuerst: nach einer Wiederaufnahme tragen die alte und
        /// die neue Sitzung dieselbe Full-JID, und die alte bleibt als totes
        /// Objekt in der Liste stehen.
        /// </remarks>
        public XMPPSession? SessionOf(String fullJid)
        {
            // RFC 7622, Abschnitt 3.4: Der Resourcepart ist von der Schreibweise
            // abhängig, Local- und Domainpart sind es nicht. Ein
            // OrdinalIgnoreCase über die ganze Full-JID warf beides in einen
            // Topf - und lieferte damit zu 'alice@example.com/handy' auch die
            // Sitzung von 'alice@example.com/Handy' aus. Die Resource-Vergabe
            // unterschied die beiden von Anfang an (siehe Belegt); nur das
            // Nachschlagen nicht.
            lock (_lock)
                return _sessions.Where(s => JidUtilities.AreEqual(s.FullJid, fullJid))
                                .OrderByDescending(s => s.IsOpen)
                                .FirstOrDefault(s => s.IsOpen || s.ResumptionId is not null);
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
        {

            _webSocketServer.Start().GetAwaiter().GetResult();

            // XEP-0198, Abschnitt 5: die Frist der aufgehobenen Streams läuft
            // in Echtzeit ab, nicht beim nächsten Zugriff - sonst hinge eine
            // aufgeschobene Abmeldung daran, dass zufällig jemand anderes
            // etwas tut. Eine Sekunde reicht: die Frist liegt in der
            // Grössenordnung von Minuten.
            _resumptionSweeper = new Timer(
                                     _ => SweepResumableStreamsAsync().GetAwaiter().GetResult(),
                                     null,
                                     TimeSpan.FromSeconds(1),
                                     TimeSpan.FromSeconds(1));

        }

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

            // Schalter für den Fehlerfall: die Stanza hat die Leitung verlassen
            // und kommt trotzdem nicht an. Vor dem Aufzeichnen und vor dem
            // Zählen, damit für den Server aussieht, als sei nie etwas
            // gekommen - genau das Bild, das eine Verbindung hinterlässt, die
            // zwischen Absenden und Verarbeiten zerfällt.
            //
            // Nur Stanzas: Nonzas müssen weiter durchkommen, sonst liesse sich
            // in diesem Zustand weder ein <r/> noch ein <resume/> schicken, und
            // der Fall wäre wieder nicht zu erreichen.
            if (SwallowClientStanzas && XMPPSession.IsStanza(frame))
                return;

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

            // XEP-0198, Abschnitt 5: einem Stream, dem die Wiederaufnahme
            // zugesagt ist, wird die Abmeldung erst einmal erspart. Sonst
            // führte der Server seinen Kontakten ein Verschwinden vor, das
            // gleich darauf zurückgenommen werden müsste - und zwischen den
            // beiden Presences läge alles, was in der Zwischenzeit an eine
            // vermeintlich abgemeldete Resource gerichtet war.
            //
            // Vor dem Wächter unten, nicht dahinter: TryMarkUnavailable
            // schaltet den Zustand um, und danach wäre die Sitzung für die
            // nachgeholte Abmeldung nach Fristablauf schon verbraucht.
            if (session.ResumptionId is not null && Park(session))
                return;

            // Hat der Client sich selbst abgemeldet, ist die Sache erledigt.
            // Die Umschaltung muss atomar sein: sonst kommen ein abbrechender
            // Socket und die eigene Abmeldung des Clients beide am Wächter
            // vorbei, und die Kontakte bekommen sie zweimal.
            if (session.FullJid is null || !session.TryMarkUnavailable())
                return;

            // Beim Herunterfahren des Servers geht es an niemanden mehr.
            if (!RouteStanzas || !BroadcastPresence || _cts.IsCancellationRequested)
                return;

            var stanza = $"<presence type='unavailable' from='{session.FullJid}'/>";

            foreach (var target in PresenceTargetsOf(session))
                await target.SendAsync(stanza);

            foreach (var remote in RemotePresenceTargetsOf(session))
                await RouteToAsync(remote, StampTo(stanza, remote));

        }

        /// <summary>
        /// Hebt einen abgerissenen Stream für seinen Rückkehrer auf.
        /// </summary>
        /// <returns>
        /// false, wenn nichts aufzuheben war - dann nimmt der Aufrufer den
        /// gewohnten Weg und meldet ab.
        /// </returns>
        private Boolean Park(XMPPSession session)
        {

            // Gebunden muss die Sitzung sein - ohne Resource gibt es nichts,
            // wohin ein Rückkehrer zurückkehren könnte.
            //
            // Verfügbar muss sie *nicht* sein. Hier stand einmal zusätzlich
            // ein !session.IsAvailable, und das verwechselte zwei Dinge: Die
            // Wiederaufnahme ist eine Eigenschaft des Streams und wurde mit
            // <enabled resume='true'/> zugesagt; die Presence sagt den
            // Kontakten etwas über den Menschen davor. Ein Client, der sich
            // unsichtbar gemacht hat oder seine erste Presence noch nicht
            // geschickt hat, verlor damit stillschweigend die Zusage: Sein
            // <resume/> bekam ein <failed/>, und alles Unbestätigte war fort.
            //
            // Für die Abmeldung, in deren Ablauf diese Funktion sitzt, ist die
            // Unterscheidung ohnehin schon getroffen - TryMarkUnavailable
            // weiter unten lehnt eine nie verfügbare Sitzung von sich aus ab.
            if (session.FullJid is null)
                return false;

            lock (_lock)
            {

                // Zwei Abrisse derselben Sitzung dürfen nicht zwei Einträge
                // ergeben: der zweite bekäme eine neue Frist und hielte die
                // Abmeldung beliebig lange auf.
                if (_resumable.ContainsKey(session.ResumptionId!))
                    return true;

                _resumable[session.ResumptionId!] = new ParkedStream(
                                                        session,
                                                        DateTimeOffset.UtcNow + ResumptionTimeout);

            }

            return true;

        }

        /// <summary>
        /// Räumt abgelaufene Streams ab und holt ihre Abmeldung nach.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Durchgang wäre die Aufschiebung aus
        /// <see cref="AnnounceUnavailableAsync"/> keine Aufschiebung, sondern
        /// ein Verschlucken: die Kontakte führten jede abgerissene Resource
        /// für immer als online, und niemandem fiele etwas auf.
        /// </remarks>
        internal async Task SweepResumableStreamsAsync()
        {

            List<ParkedStream> abgelaufen;

            lock (_lock)
            {

                abgelaufen = [.. _resumable.Values.Where(p => p.Deadline <= DateTimeOffset.UtcNow)];

                foreach (var p in abgelaufen)
                    _resumable.Remove(p.Session.ResumptionId!);

            }

            foreach (var p in abgelaufen)
            {

                // Zuerst die Zusage zurücknehmen, dann abmelden: sonst sähe
                // AnnounceUnavailableAsync wieder einen wiederaufnehmbaren
                // Stream vor sich und parkte ihn erneut. Die Abmeldung käme
                // dann nie.
                p.Session.EndResumption();

                await AnnounceUnavailableAsync(p.Session);

            }

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

            // RFC 7395, Abschnitt 3.6: der Client verabschiedet sich.
            //
            // Damit ist der Stream zu Ende, und nicht abgerissen - eine
            // Wiederaufnahme kommt nicht mehr in Frage (XEP-0198, Abschnitt
            // 5.3). Ohne diese Unterscheidung hielte der Server jede
            // ordentliche Abmeldung eine Minute lang für eine Störung: die
            // Kontakte sähen den Abgemeldeten so lange als anwesend, und ein
            // erneutes Anmelden knüpfte an einen Stream an, den der Nutzer
            // selbst beendet hat.
            if (frame.StartsWith("<close", StringComparison.Ordinal))
            {
                session.EndResumption();
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

                // XEP-0198, Abschnitt 5: nur zusagen, wonach gefragt wurde.
                // Ein ungefragtes resume='true' verpflichtete den Server, jede
                // abgerissene Sitzung aufzuheben, und kein Client käme je
                // zurück, um sie abzuholen.
                var resume = OfferStreamResumption &&
                             Regex.IsMatch(frame, @"resume=['""](true|1)['""]");

                // Zähler zurücksetzen und bestätigen in einem Zug - das
                // <enabled/> selbst ist eine Nonza und zählt nicht mit, aber
                // eine Stanza dazwischen zählte nur bei einer der beiden
                // Seiten. Siehe EnableStreamManagementAsync.
                await session.EnableStreamManagementAsync(
                          resume,
                          s => resume
                                   ? $"<enabled xmlns='urn:xmpp:sm:3' id='{s.ResumptionId}' " +
                                     $"resume='true' max='{(Int32) ResumptionTimeout.TotalSeconds}'/>"
                                   : $"<enabled xmlns='urn:xmpp:sm:3' id='sm-{s.ConnectionNumber}'/>");

                return;

            }

            // XEP-0198, Abschnitt 5: der Client will an einen früheren Stream
            // anknüpfen. Das kommt vor dem Resource Binding - eine gebundene
            // Resource gibt es hier noch nicht, sie wird gerade übernommen.
            if (frame.StartsWith("<resume", StringComparison.Ordinal))
            {
                await HandleResumeAsync(session, frame);
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
                    session.AcknowledgeToClient(value);

                return;

            }

        }

        /// <summary>
        /// XEP-0198, Abschnitt 5: <c>&lt;resume/&gt;</c> - jemand knüpft an
        /// einen aufgehobenen Stream an.
        /// </summary>
        /// <remarks>
        /// Die Kennung allein reicht nicht. Sie wandert über die Leitung, und
        /// wer sie in die Finger bekommt, hätte sonst eine fremde Sitzung
        /// samt Full-JID, Roster und laufenden Gesprächen - ohne je das
        /// Passwort gesehen zu haben. Deshalb muss der Stream, auf dem das
        /// <c>&lt;resume/&gt;</c> ankommt, bereits auf <b>dasselbe Konto</b>
        /// angemeldet sein; die Kennung wählt dann nur noch aus, welcher der
        /// Streams dieses Kontos gemeint ist.
        ///
        /// Scheitert es, ist das kein Fehlerfall, sondern der Normalfall nach
        /// einer längeren Störung: der Client bekommt <c>&lt;failed/&gt;</c>
        /// und bindet eine neue Resource.
        /// </remarks>
        private async Task HandleResumeAsync(XMPPSession session, String frame)
        {

            var previd = Regex.Match(frame, @"previd=['""]([^'""]+)['""]");

            ParkedStream? geparkt = null;

            if (previd.Success)
                lock (_lock)
                    if (_resumable.TryGetValue(previd.Groups[1].Value, out var gefunden) &&
                        gefunden.Deadline > DateTimeOffset.UtcNow &&
                        session.Account is not null &&
                        String.Equals(gefunden.Session.BareJid, session.BareJid,
                                      StringComparison.OrdinalIgnoreCase))
                    {
                        geparkt = gefunden;
                        _resumable.Remove(previd.Groups[1].Value);
                    }

            if (geparkt is null)
            {
                await session.SendAsync(
                    "<failed xmlns='urn:xmpp:sm:3' h='0'>" +
                    "<item-not-found xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></failed>");
                return;
            }

            // Der neue Stream übernimmt den alten. Erst danach das <resumed/>:
            // es meldet den Empfangszähler, und der gehört zum übernommenen
            // Zustand.
            var offen = session.AdoptResumed(geparkt.Session);

            await session.SendAsync(
                $"<resumed xmlns='urn:xmpp:sm:3' h='{session.StanzasReceivedFromClient}' " +
                $"previd='{XmlEscaping.Escape(previd.Groups[1].Value)}'/>");

            // Was der alte Stream nicht mehr loswurde, geht jetzt nach. Der
            // Zähler läuft dabei weiter - diese Stanzas hat der Client noch
            // nicht gesehen, sie zählen wie jede andere auch.
            var h = Regex.Match(frame, @"h=['""](\d+)['""]");
            var bestaetigt = h.Success && UInt32.TryParse(h.Groups[1].Value, out var wert)
                                 ? wert
                                 : 0u;

            foreach (var (seq, stanza) in offen)
                if (unchecked(bestaetigt - seq) >= 0x8000_0000u)
                    await session.SendAsync(stanza);

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

                    // RFC 6121, Abschnitt 3.4: rein informativ, nie
                    // auszuhandeln - aber ohne die Ankündigung darf ein Client
                    // Pre-Approval nicht benutzen.
                    (OfferSubscriptionPreApproval
                         ? "<sub xmlns='urn:xmpp:features:pre-approval'/>"
                         : "") +

                    // RFC 6121, Abschnitt 2.6.1: Ohne diese Ankündigung darf
                    // ein Client kein 'ver' an seine Roster-Anfrage hängen -
                    // er wüsste sonst nicht, ob ein leeres Ergebnis
                    // „unverändert" heisst oder „leerer Roster".
                    (OfferRosterVersioning
                         ? "<ver xmlns='urn:xmpp:features:rosterver'/>"
                         : "") +
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

                var gestempelt = StampFrom(frame, session.FullJid);

                // Fremde Domain: hinaus damit.
                if (!IsLocal(to))
                {

                    if (!await RouteToAsync(to, gestempelt) &&
                        type != "error")
                    {
                        await SendRemoteServerNotFoundAsync(session, "iq", id, to);
                    }

                    return;

                }

                await DeliverIqLocallyAsync(session, to, gestempelt);

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
                    // XEP-0160, Abschnitt 4: Nur wenn es die Ablage wirklich
                    // gibt. Eine Ankündigung, die immer steht, verspricht einem
                    // Client, dass seine Nachricht an einen Abwesenden liegen
                    // bleibt - und lässt ihn den Fehler übersehen, mit dem der
                    // Server ihm gerade das Gegenteil sagt.
                    (StoreOfflineMessages ? "<feature var='msgoffline'/>" : "") +
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

        /// <summary>
        /// Die Zustellung einer IQ-Stanza an ein hiesiges Konto (RFC 6121,
        /// Abschnitte 8.5.1, 8.5.2.1.3, 8.5.2.2.3 und 8.5.3.2.3).
        /// </summary>
        /// <param name="origin">
        /// Die Sitzung des Absenders - oder <c>null</c>, wenn die Anfrage über
        /// die Servergrenze hereinkam.
        /// </param>
        /// <remarks>
        /// Der Unterschied zur Nachricht ist grundlegend und nicht gradweise:
        /// Eine Anfrage an einen <b>Bare-JID</b> wird nicht zugestellt, sondern
        /// vom Server selbst beantwortet - Abschnitt 8.5.2.1.3 sagt das doppelt
        /// („MUST reply on behalf of the user" und „MUST NOT deliver the IQ
        /// stanza to any of the user's available resources").
        ///
        /// Der Grund liegt in der Natur von IQ. Es ist ein Frage-Antwort-Paar,
        /// über die <c>id</c> zusammengehalten (RFC 6120, Abschnitt 8.2.3), und
        /// jede empfangene Anfrage <b>muss</b> beantwortet werden. Verteilt man
        /// sie an alle Resourcen, antworten alle: Der Fragende bekommt drei
        /// Antworten auf eine <c>id</c> und kann nicht entscheiden, welche
        /// gilt - genau das tat dieser Server. Bei einer Nachricht wäre
        /// mehrfache Zustellung höchstens lästig; hier bricht sie die Semantik.
        ///
        /// Zwei Fälle, ein Ergebnis: Abschnitt 8.5.2.1.3 (Resourcen da) und
        /// 8.5.2.2.3 (keine da) verlangen wörtlich dasselbe. Deshalb fragt
        /// dieser Weg gar nicht erst, ob jemand angemeldet ist - die Antwort
        /// wäre in beiden Fällen dieselbe, und eine Verzweigung, die nichts
        /// unterscheidet, behauptet einen Unterschied.
        /// </remarks>
        private async Task DeliverIqLocallyAsync(XMPPSession?  origin,
                                                 String        to,
                                                 String        stanza)
        {

            // Wie bei der Nachricht: ohne Absender gibt es keine Adresse für
            // eine Antwort, und eine Antwort ist hier Pflicht. Der Rücksprung
            // wird nie erreicht - beide Aufrufer stempeln oder prüfen das
            // 'from' -, macht aber alles darunter nullfrei.
            if (Attr(stanza, "from") is not { } sender)
                return;

            var type  = Attr(stanza, "type");
            var id    = Attr(stanza, "id");

            // Eine Antwort wird nie beantwortet (RFC 6120, Abschnitt 8.2.3,
            // Regel 4). Sie gehört genau der Resource, die gefragt hat, und
            // sonst niemandem; findet sie die nicht, ist sie eine Antwort auf
            // eine Frage, die niemand mehr stellt, und am besten vergessen.
            //
            // Abschnitt 8.5.3.2.3 verlangt für „eine IQ-Stanza" ohne passende
            // Resource einen Fehler und unterscheidet die Art nicht. Hier gilt
            // trotzdem Regel 4: Wer eine Antwort mit einem Fehler beantwortet,
            // schickt sie an jemanden, der nichts gefragt hat, unter der 'id'
            // einer Frage, die er selbst beantwortet hat.
            if (type is "result" or "error")
            {

                if (SessionOf(to) is { } wartender)
                    await wartender.SendAsync(stanza);

                return;

            }

            // Ab hier: eine Anfrage (get, set - oder ein unbekannter Wert, den
            // dieser Weg wie eine Anfrage behandelt, weil eine Antwort mehr
            // taugt als Schweigen).
            //
            // Eine Verzweigung, wo der RFC zwei Abschnitte hat: Abschnitt
            // 8.5.3.1 lässt eine passende Resource zustellen, 8.5.3.2.3 (keine
            // passende Resource) und 8.5.2.1.3/8.5.2.2.3 (Bare-JID) verlangen
            // alle drei dasselbe - <service-unavailable/> vom Server. Wo das
            // Verhalten dasselbe ist, kann kein Test die Fälle unterscheiden,
            // und eine Verzweigung, die es doch tut, behauptet einen
            // Unterschied, den es nicht gibt.
            //
            // Der Bare-JID fällt dabei von selbst in den Fehlerzweig, weil
            // SessionOf ausschliesslich Full-JIDs vergleicht (RFC 7622,
            // Abschnitt 3.4) - und das ist genau, was 8.5.2.1.3 mit „MUST NOT
            // deliver the IQ stanza to any of the user's available resources"
            // verlangt. Gehalten wird diese Zusage nicht von einer Prüfung hier,
            // sondern von einem Test: Er meldet zwei Resourcen an und besteht
            // nur, wenn keine die Anfrage sieht.
            //
            // Was noch fehlt, ist die zweite Hälfte von 8.5.3.1: Wer die
            // Presence des Empfängers nicht sehen darf, soll die Anfrage nicht
            // zugestellt bekommen, weil schon die Antwort verrät, dass die
            // Resource existiert. Das braucht die Aufzeichnung gerichteter
            // Presence und steht unter „Später".
            //
            // Der Fehler geht auch an ein Konto, das es hier nicht gibt:
            // Abschnitt 8.5.1 lässt bei einer Nachricht das stille Übergehen zu,
            // bei einer Anfrage nicht. Preisgegeben wird damit nichts - die
            // Antwort ist dieselbe wie für ein vorhandenes Konto ohne
            // erreichbare Resource.
            //
            // Und es ist immer <service-unavailable/>, was die vollständige
            // Umsetzung ist und keine halbe: Abschnitt 8.5.2.1.3 verlangt eine
            // eigene Antwort, „if the semantics of the qualifying namespace
            // define a reply that the server can provide on behalf of the user" -
            // und andernfalls ausdrücklich diesen Fehler. Dieser Server kennt
            // keinen solchen Namensraum; käme einer hinzu, ist dies die Stelle.
            if (SessionOf(to) is { } match)
                await match.SendAsync(stanza);

            else
                await SendServiceUnavailableAsync("iq", id, to, sender);

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

                var fassung = account.RosterVersion;

                // RFC 6121, Abschnitt 2.6.2: Kennt der Client bereits diese
                // Fassung, kommt ein leeres Ergebnis ganz ohne <query/>. Sein
                // Zwischenspeicher stimmt, es gibt nichts zu schicken.
                //
                // Das Weglassen des <query/> ist dabei die ganze Aussage: Ein
                // <query/> ohne Kinder hiesse „dein Roster ist leer" und
                // löschte beim Client alles.
                if (OfferRosterVersioning &&
                    QueryAttr(frame, "ver") is String bekannt &&
                    String.Equals(bekannt, fassung, StringComparison.Ordinal))
                {
                    await session.SendAsync($"<iq type='result' id='{id}'/>");
                    return;
                }

                var items = new StringBuilder();

                foreach (var e in account.Roster)
                {
                    items.Append($"<item jid='{e.Jid}'");
                    if (e.Name is not null)
                        items.Append($" name='{e.Name}'");
                    if (e.Ask is not null)
                        items.Append($" ask='{e.Ask}'");
                    if (e.Approved)
                        items.Append(" approved='true'");
                    items.Append($" subscription='{e.Subscription}'/>");
                }

                var verAttribut = OfferRosterVersioning ? $" ver='{fassung}'" : "";

                await session.SendAsync(
                    $"<iq type='result' id='{id}'>" +
                    $"<query xmlns='jabber:iq:roster'{verAttribut}>{items}</query></iq>");

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

            // Fremde Domain: raus damit, und wenn das nicht geht, dem Absender
            // Bescheid sagen. Die <sent>-Carbons unten gelten trotzdem - sie
            // betreffen das Konto des Absenders und nicht das Ziel.
            if (!IsLocal(to))
            {

                if (!await RouteToAsync(to, stamped) &&
                    Attr(frame, "type") != "error")
                {
                    await SendRemoteServerNotFoundAsync(session, "message", Attr(frame, "id"), to);
                }

                await SendSentCarbonsAsync(session, stamped);

                return;

            }

            await DeliverMessageLocallyAsync(session, to, stamped);

        }

        /// <summary>
        /// Die Zustellung einer Nachricht an eine hiesige Adresse (RFC 6121,
        /// Abschnitt 8.5).
        /// </summary>
        /// <param name="origin">
        /// Die Sitzung des Absenders - oder <c>null</c>, wenn die Nachricht über
        /// die Servergrenze hereinkam.
        /// </param>
        /// <param name="to">Die Adresse, wie sie in der Stanza steht.</param>
        /// <param name="stanza">Die Stanza mit gesetztem <c>from</c>.</param>
        /// <remarks>
        /// Eine Stelle für beide Herkünfte, und das ist der Kern dieses
        /// Schritts: Abschnitt 8.5 spricht durchweg von einer „inbound stanza"
        /// und unterscheidet nicht, ob sie von einem Client oder von einem
        /// anderen Server kam. Der Empfänger merkt den Unterschied ohnehin
        /// nicht - für ihn ist es eine Nachricht an sein Konto.
        ///
        /// Bis hierher nahm nur der Weg vom Client diese Regeln. Was über die
        /// Grenze kam, ging unbesehen ins Routing: ohne Offline-Ablage, ohne
        /// Rücksicht auf negative Prioritäten, ohne Unterscheidung nach Art.
        /// Das traf gerade den häufigsten Fall - der Bekannte auf einem anderen
        /// Server ist der Regelfall und nicht die Ausnahme.
        ///
        /// Der einzige Unterschied, der bleibt, sind die
        /// <c>&lt;sent&gt;</c>-Carbons: Sie gehören den anderen Geräten des
        /// Absenders, und die eines fremden Kontos sind nicht unsere Sache.
        /// Der Rückweg einer Fehlerantwort ist dagegen <b>kein</b> Unterschied -
        /// er geht in beiden Fällen über <see cref="RouteToAsync"/>, und die
        /// weiss selbst, ob eine Adresse hier liegt oder woanders. Eine eigene
        /// Verzweigung dafür wäre eine zweite Antwort auf eine Frage, die schon
        /// beantwortet ist.
        /// </remarks>
        private async Task DeliverMessageLocallyAsync(XMPPSession?  origin,
                                                     String        to,
                                                     String        stanza)
        {

            // Ohne Absender ist keine der beiden Hälften zu entscheiden: weder
            // wohin die Nachricht geht noch wohin eine Ablehnung zurückgeht.
            //
            // Kein Test hält diese Zeile fest, und es braucht auch keinen: Sie
            // lässt sich nicht entfernen, ohne dass der Compiler das Übersetzen
            // verweigert, weil alles darunter mit einer Zeichenkette und nicht
            // mit einem Vielleicht rechnet. Erreicht wird der Rücksprung
            // ohnehin nie - der eine Aufrufer stempelt das 'from' selbst, der
            // andere hat es geprüft, bevor er hierher kommt.
            if (Attr(stanza, "from") is not { } sender)
                return;

            // RFC 6121, Abschnitt 8.5: Wohin eine Nachricht geht, hängt an
            // ihrer Art *und* an der Form der Adresse. Bis hierher ging alles
            // denselben Weg.
            var messageType = MessageTypeExtensions.Parse(Attr(stanza, "type"));

            if (to.Contains('/'))
            {

                // Abschnitt 8.5.3.1: Passt die Resource, wird zugestellt - und
                // zwar unabhängig von der Art. So liefert ein Raum seine
                // groupchat-Nachrichten aus, und so erreicht eine Fehlerantwort
                // genau die Resource, die den Fehler verursacht hat.
                //
                // Auch die Priorität steht hier nicht im Weg: Wer sie negativ
                // setzt, will nichts mehr abbekommen, was bloss an sein Konto
                // ging - gerichtet ansprechbar bleibt er.
                if (SessionOf(to) is { } match)
                {

                    await match.SendAsync(stanza);

                    if (DeliverCarbons && origin is not null)
                        await SendSentCarbonsAsync(origin, stanza);

                    return;

                }

                // Abschnitt 8.5.3.2.1: Keine passende Resource. Für normal,
                // groupchat und headline darf die Stanza still verworfen
                // werden - der Absender hat diese Resource gemeint, und die
                // gibt es nicht.
                if (messageType != MessageType.Chat)
                    return;

                // Ein chat dagegen wird behandelt, als wäre er an das Konto
                // gegangen. Die Ausnahme sieht schrullig aus und trifft den
                // Alltag: Ein Client antwortet auf die Full-JID, die er zuletzt
                // gesehen hat, und wenn der Gesprächspartner in der Zwischenzeit
                // das Gerät gewechselt hat, ist sie weg. Der Absender meinte
                // nicht diese Resource, sondern seinen Gegenüber.
                //
                // Das 'to' bleibt dabei stehen, wie es ankam - nicht
                // umgeschrieben auf die Resource, die es nun bekommt.

            }

            await DeliverToAccountAsync(origin, to, stanza, Attr(stanza, "id"), sender, messageType);

        }

        /// <summary>
        /// Die Zustellung an ein Konto (RFC 6121, Abschnitt 8.5.2) - dorthin
        /// führen der Bare-JID und, für <c>chat</c>, auch die nicht passende
        /// Resource.
        /// </summary>
        /// <param name="sender">
        /// Das geprüfte <c>from</c> der Stanza - wohin eine Ablehnung
        /// zurückgeht.
        /// </param>
        private async Task DeliverToAccountAsync(XMPPSession?  origin,
                                                 String        to,
                                                 String        stamped,
                                                 String?       id,
                                                 String        sender,
                                                 MessageType   messageType)
        {

            // Eine Fehler-Stanza wird stillschweigend übergangen. Auf sie zu
            // antworten hiesse, einen Fehler mit einem Fehler zu beantworten.
            if (messageType == MessageType.Error)
                return;

            // Ein groupchat gehört in einen Raum. An ein Konto gerichtet ist
            // er nie zustellbar, weder an eine noch an alle Resourcen, und der
            // Absender bekommt es gesagt.
            if (messageType == MessageType.GroupChat)
            {
                await SendServiceUnavailableAsync("message", id, to, sender);
                return;
            }

            // Eine Resource mit negativer Priorität bekommt nichts, was bloss
            // an das Konto gerichtet war - für jede Art von Nachricht.
            var recipients = SessionsOf(to).Where(r => r.PresencePriority >= 0).ToArray();

            // Ein headline geht an *alle* nicht-negativen Resourcen: Er ist
            // eine Meldung an den Menschen und nicht an ein Gerät, und welches
            // davon er gerade ansieht, weiss niemand. Ist keine da, wird er
            // stillschweigend verworfen - er ist vergänglich und lohnt kein
            // Aufheben.
            if (messageType == MessageType.Headline)
            {

                foreach (var target in recipients)
                    await target.SendAsync(stamped);

                return;

            }

            // Bleiben normal und chat. Ist niemand erreichbar, verlangt
            // Abschnitt 8.5.2.2.1 die Ablage oder einen Fehler - stillschweigend
            // verwerfen darf der Server sie nicht.
            //
            // "Niemand erreichbar" heisst hier auch: nur negative Prioritäten.
            // Abschnitt 8.5.2.1.1 sagt das am Ende ausdrücklich - dann soll der
            // Server verfahren, als gäbe es überhaupt keine Resource. Die
            // Alternative wäre, die Nachricht doch dem Gerät zu geben, das
            // gerade gesagt hat, es wolle sie nicht.
            if (recipients.Length == 0)
            {

                await StoreOfflineOrRefuseAsync(to, stamped, id, sender);

                // Ehrlich vermerkt: Eine Mutation, die hier die Frage nach der
                // Herkunft fallen lässt, überlebt - obwohl sie für eine
                // Nachricht von aussen eine NullReferenceException wirft. Der
                // Grund liegt nicht an dieser Zeile, sondern am `catch` beim
                // Verarbeiten eines Frames (siehe oben): Es ist für abgerissene
                // Verbindungen gedacht und verschluckt jeden Programmierfehler
                // mit. Weil die Ablage vorher geschrieben ist und danach nichts
                // mehr folgt, bleibt der Wurf ohne sichtbare Folge. Steht unter
                // „Später".
                if (origin is not null)
                    await SendSentCarbonsAsync(origin, stamped);

                return;

            }

            // Wie ein echter Server: an die zuletzt gebundene Resource zustellen.
            var primary = recipients[^1];

            await primary.SendAsync(stamped);

            if (!DeliverCarbons)
                return;

            // XEP-0280 <received>: die übrigen Resourcen des Empfängers
            foreach (var other in recipients.Where(r => r != primary && r.CarbonsEnabled))
                await other.SendAsync(CarbonEnvelope("received", other.BareJid!, other.FullJid!, stamped));

            if (origin is not null)
                await SendSentCarbonsAsync(origin, stamped);

        }

        /// <summary>
        /// Legt eine Nachricht für ein Konto ohne erreichbare Resource ab -
        /// oder sagt dem Absender, dass daraus nichts wird (RFC 6121,
        /// Abschnitt 8.5.2.2.1, XEP-0160).
        /// </summary>
        /// <remarks>
        /// Der Abschnitt stellt zwei Wege nebeneinander und verbietet den
        /// dritten. Ablegen und Ablehnen sind beide richtig; stillschweigend
        /// verwerfen ist es nicht, denn dann hält der Absender seine Nachricht
        /// für zugestellt und niemand kann den Verlust bemerken.
        ///
        /// Ein Konto, das es hier nicht gibt, bleibt davon ausgenommen:
        /// Abschnitt 8.5.1 lässt für diesen Fall auch das stille Übergehen zu,
        /// und dabei bleibt es. Wer aus jeder Nachricht an einen unbekannten
        /// Namen einen Fehler machte, gäbe damit Auskunft darüber, welche Konten
        /// es auf diesem Server gibt.
        /// </remarks>
        private async Task StoreOfflineOrRefuseAsync(String   to,
                                                     String   stamped,
                                                     String?  id,
                                                     String   sender)
        {

            if (GetAccount(BareOf(to)) is not { } account)
                return;

            if (StoreOfflineMessages &&
                account.StoreOfflineMessage(stamped,
                                            DateTimeOffset.UtcNow,
                                            MaxStoredOfflineMessages))
            {
                return;
            }

            await SendServiceUnavailableAsync("message", id, to, sender);

        }

        /// <summary>
        /// Reicht einer neu verfügbaren Resource die abgelegten Nachrichten
        /// nach (XEP-0160).
        /// </summary>
        /// <remarks>
        /// Nur an eine verfügbare Resource mit nicht-negativer Priorität.
        /// XEP-0160 sagt es so ("when the recipient next sends non-negative
        /// available presence"), und es ist dieselbe Rücksicht, die Abschnitt
        /// 8.5 im laufenden Betrieb verlangt: Ein Gerät, das sich aus dem
        /// Verkehr an das Konto heraushält, ist der falsche Ort für eine
        /// Ablage, die gerade deshalb entstanden ist, weil niemand hingesehen
        /// hat.
        ///
        /// Beide Bedingungen sind nötig, nicht nur die zweite: Eine Abmeldung
        /// setzt die Priorität auf 0 zurück (<see cref="XMPPSession.RecordPresence"/>),
        /// und ohne die Frage nach der Verfügbarkeit ginge die Ablage genau an
        /// die Resource, die sich gerade abgemeldet hat.
        ///
        /// Anders als die aufbewahrten Subscription-Anfragen wird die Ablage
        /// dabei geleert - siehe
        /// <see cref="XMPPAccount.TakeOfflineMessages"/>.
        /// </remarks>
        private async Task SendOfflineMessagesToAsync(XMPPSession session)
        {

            if (session.Account is not { } account ||
                !session.IsAvailable ||
                session.PresencePriority < 0)
            {
                return;
            }

            foreach (var nachricht in account.TakeOfflineMessages())
                await session.SendAsync(WithDelay(nachricht, Domain));

        }

        /// <summary>
        /// Hängt einer nachgereichten Nachricht ihren Eingangszeitpunkt an
        /// (XEP-0203).
        /// </summary>
        /// <remarks>
        /// Ohne den Stempel behauptet eine Nachricht von gestern, sie sei von
        /// jetzt: Der Empfänger sieht den Unterschied nicht und antwortet auf
        /// etwas, das sich längst erledigt hat. Der Stempel ist der einzige
        /// Weg, den Verzug überhaupt mitzuteilen - die Stanza selbst trägt
        /// keine Zeit.
        ///
        /// Angehängt und nicht eingesetzt: Das <c>&lt;delay/&gt;</c> ist ein
        /// weiteres Kindelement der Nachricht, und die Reihenfolge der
        /// Kindelemente ist frei.
        ///
        /// Der zweite Zweig ist kein Vorsorgezweig: Eine Nachricht ohne
        /// Kindelemente (<c>&lt;message .../&gt;</c>) darf ein Client schicken,
        /// sie ist ein <c>chat</c> wie jeder andere und wird deshalb abgelegt.
        /// Ohne das Auflösen des leeren Elements ginge der Stempel entweder
        /// verloren oder hinter das Ende der Stanza.
        /// </remarks>
        internal static String WithDelay(OfflineMessage message, String from)
        {

            var stamp = message.StoredAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'",
                                                              CultureInfo.InvariantCulture);

            var delay = $"<delay xmlns='urn:xmpp:delay' from='{from}' stamp='{stamp}'>Offline Storage</delay>";

            var stanza = message.Stanza;
            var ende   = stanza.LastIndexOf("</message>", StringComparison.Ordinal);

            if (ende >= 0)
                return stanza[..ende] + delay + stanza[ende..];

            // Ein leeres Element: <message .../> wird zu <message ...>…</message>.
            var schluss = stanza.LastIndexOf("/>", StringComparison.Ordinal);

            return schluss >= 0
                       ? stanza[..schluss] + ">" + delay + "</message>"
                       : stanza;

        }

        /// <summary>
        /// XEP-0280 <c>&lt;sent&gt;</c>: die übrigen Resourcen des Absenders
        /// erfahren, was er geschrieben hat.
        /// </summary>
        private async Task SendSentCarbonsAsync(XMPPSession sender, String stamped)
        {

            if (!DeliverCarbons || sender.BareJid is null)
                return;

            foreach (var other in SessionsOf(sender.BareJid).Where(r => r != sender && r.CarbonsEnabled))
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
                await HandleSubscriptionAsync(session, type, BareOf(to), frame);
                return;
            }

            // Sonstige gerichtete Presence geht genau dorthin.
            if (to is not null)
            {
                await RouteToAsync(to, stamped);
                return;
            }

            // Vor dem Aufzeichnen gefragt: danach ist die Sitzung verfügbar,
            // und der Unterschied zwischen "war schon" und "ist gerade
            // geworden" wäre nicht mehr zu sehen.
            var wurdeVerfuegbar  = type is null && !session.IsAvailable;
            var initial          = session.RecordPresence(stamped, available: type is null);

            // RFC 6121, Abschnitt 3.1.3, Regel 4: "deliver the request when
            // the contact next has an available resource". Vor dem
            // Broadcast-Schalter, weil das Nachreichen aufbewahrter Anfragen
            // nichts mit dem Verteilen von Presence zu tun hat - wer die
            // Verteilung abschaltet, will keine Anfragen verlieren.
            if (wurdeVerfuegbar)
                await SendStoredSubscriptionRequestsToAsync(session);

            // XEP-0160: "When the recipient next sends non-negative available
            // presence to the server, the server delivers the message to the
            // resource that has sent that presence."
            //
            // Bei *jeder* solchen Presence und nicht nur beim Verfügbarwerden -
            // anders als bei der aufbewahrten Anfrage darüber. Der Unterschied
            // liegt daran, dass die Ablage beim Zustellen geleert wird: Ein
            // zweiter Durchgang findet nichts mehr und kann deshalb nichts
            // doppelt vorlegen. Und er hat einen eigenen Fall, den das
            // Verfügbarwerden nicht abdeckt: Eine Resource, die mit negativer
            // Priorität angemeldet ist und sie auf 0 hebt, war schon verfügbar -
            // sie wird aber gerade eben erst zu einem Empfänger.
            await SendOfflineMessagesToAsync(session);

            if (!BroadcastPresence)
                return;

            foreach (var target in PresenceTargetsOf(session))
                await target.SendAsync(stamped);

            // Kontakte auf fremden Domains bekommen dieselbe Presence - eine
            // nicht erreichbare Gegenstelle bleibt hier folgenlos, Presence
            // wird nicht mit Fehlern beantwortet.
            foreach (var remote in RemotePresenceTargetsOf(session))
                await RouteToAsync(remote, StampTo(stamped, remote));

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
        /// <param name="frame">Die Stanza, wie der Client sie geschickt hat.</param>
        private async Task HandleSubscriptionAsync(XMPPSession  sender,
                                                   String       type,
                                                   String       peerBareJid,
                                                   String       frame)
        {

            var senderAccount  = sender.Account;
            var peerAccount    = GetAccount(peerBareJid);

            if (senderAccount is null)
                return;

            // Nach RFC 6121, Abschnitt 3.1.1 trägt der Handshake immer den
            // Bare-JID - die Anfrage gilt dem Konto, nicht einer Resource.
            // Deshalb werden beide Adressen ersetzt und nicht bloss ergänzt.
            //
            // Gestempelt und nicht neu gebaut: eine Anfrage darf erweiterten
            // Inhalt tragen, und das <status/> darin ist die Begründung, mit
            // der ein Mensch über die Zustimmung entscheidet. Ein neu gebautes
            // <presence .../> wirft sie weg - und Abschnitt 3.1.3 verlangt,
            // die *vollständige* Stanza aufzubewahren.
            var stanza = StampTo(StampFrom(frame, senderAccount.BareJid), peerBareJid);

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
                //
                // Abschnitt 3.4.2 unterscheidet hier vier Fälle, und der
                // Unterschied hängt allein daran, ob eine Anfrage offen ist.
                case "subscribed":
                {

                    var bisher = senderAccount.SubscriptionOf(peerBareJid) ?? "none";

                    // Fall 1: der Kontakt darf uns ohnehin schon sehen -
                    // stillschweigend übergehen.
                    if (bisher is "from" or "both")
                        return;

                    // Fall 3 und 4: keine offene Anfrage. Dann ist das eine
                    // Vormerkung, und die Stanza geht ausdrücklich *nicht*
                    // hinaus - der Kontakt hat nichts gefragt und soll keine
                    // Antwort bekommen.
                    //
                    // Fragen und Erledigen in einem Schritt: die aufbewahrte
                    // Anfrage *ist* die offene Anfrage, und wer sie erst
                    // abfragt und dann löscht, kann beides auseinanderlaufen
                    // lassen.
                    if (!senderAccount.ForgetSubscriptionRequest(peerBareJid))
                    {

                        if (!OfferSubscriptionPreApproval)
                            return;

                        UpdateRosterEntry(senderAccount, peerBareJid, approved: true);
                        await PushRosterEntryAsync(senderAccount, peerBareJid);

                        return;

                    }

                    // Fall 2: es lag eine Anfrage vor - die gewöhnliche
                    // Zustimmung.
                    UpdateRosterEntry(senderAccount, peerBareJid,
                                      GrantFrom(bisher));
                    await PushRosterEntryAsync(senderAccount, peerBareJid);

                    if (peerAccount is not null)
                    {
                        UpdateRosterEntry(peerAccount, senderAccount.BareJid,
                                          GrantTo(peerAccount.SubscriptionOf(senderAccount.BareJid)),
                                          ask: AskChange.Clear);
                        await PushRosterEntryAsync(peerAccount, senderAccount.BareJid);
                    }

                    break;

                }

                // Abschnitt 3.2.2 und 3.2.3: der Entzug, spiegelbildlich.
                // Abschnitt 3.4.2, Anmerkung: ein 'unsubscribed' nimmt auch
                // eine Vormerkung zurück.
                case "unsubscribed":
                    senderAccount.ForgetSubscriptionRequest(peerBareJid);
                    UpdateRosterEntry(senderAccount, peerBareJid,
                                      RevokeFrom(senderAccount.SubscriptionOf(peerBareJid)),
                                      approved: false);
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
            //
            // Eine Anfrage an ein hiesiges Konto nimmt dabei denselben Weg wie
            // eine von aussen: dort entscheidet sich, ob sie zugestellt oder
            // selbst beantwortet wird. Über die Grenze trifft diese
            // Entscheidung der Server der Gegenseite.
            if (type == "subscribe" && IsLocal(peerBareJid))
                await DeliverSubscribeAsync(senderAccount.BareJid, peerBareJid, stanza);
            else
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
                                              AskChange    ask           = AskChange.Keep,
                                              Boolean?     approved      = null)
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
                                                   },
                                                   approved   ?? vorher?.Approved  ?? false));

        }

        /// <summary>Der Roster-Eintrag zu einem Kontakt, oder null.</summary>
        private static RosterEntry? RosterEntryOf(XMPPAccount account, String contactBareJid)
            => account.Roster.FirstOrDefault(
                   e => String.Equals(e.Jid, contactBareJid, StringComparison.OrdinalIgnoreCase));

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
                       (entry.Approved         ? " approved='true'"      : "") +
                       $" subscription='{entry.Subscription}'/>";

            // RFC 6121, Abschnitt 2.6.3: Auch der Push trägt die neue Fassung.
            // Ohne sie müsste der Client nach jeder Änderung den ganzen Roster
            // neu holen, um wieder zu wissen, wo er steht - und genau das soll
            // die Versionierung ersparen.
            var verAttribut = OfferRosterVersioning ? $" ver='{account.RosterVersion}'" : "";

            foreach (var s in SessionsOf(account.BareJid))
                await s.SendAsync($"<iq type='set' id='push-{Guid.NewGuid():N}' to='{s.FullJid}'>" +
                                  $"<query xmlns='jabber:iq:roster'{verAttribut}>{item}</query></iq>");

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
        /// <summary>
        /// Die Kontakte auf fremden Domains, die die Presence dieser Sitzung
        /// sehen dürfen - als Bare-JIDs, weil ihre Resourcen hier niemand
        /// kennt.
        /// </summary>
        /// <remarks>
        /// RFC 6121, Abschnitt 4.2.2 macht keinen Unterschied zwischen nah und
        /// fern: wer <c>from</c> oder <c>both</c> hat, bekommt die Presence.
        /// Getrennt von <see cref="PresenceTargetsOf"/>, weil das eine
        /// Sitzungen liefert und das andere Adressen - eine gemeinsame Liste
        /// müsste beides vertragen und wäre an jeder Verwendungsstelle wieder
        /// aufzutrennen.
        /// </remarks>
        private IEnumerable<String> RemotePresenceTargetsOf(XMPPSession session)
        {

            var account = session.Account;

            if (account is null)
                yield break;

            foreach (var entry in account.Roster)
            {

                if (!IsLocal(entry.Jid) &&
                    entry.Subscription is "from" or "both")
                {
                    yield return entry.Jid;
                }

            }

        }

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

        /// <summary>
        /// Die einzige Weiche zwischen "hier" und "woanders" (RFC 6120,
        /// Abschnitt 10.4).
        /// </summary>
        /// <returns>
        /// false nur dann, wenn die Stanza an eine fremde Domain ging und dort
        /// nicht hinkam. Ein unbekanntes Konto auf der eigenen Domain gilt als
        /// behandelt - was der Server damit tun sollte, ist eine andere Frage
        /// (RFC 6121, Abschnitt 8.1) und hängt nicht am Routing.
        /// </returns>
        private async Task<Boolean> RouteToAsync(String to, String stanza)
        {

            if (!IsLocal(to))
            {

                // Die Adresse muss mit hinaus. Innerhalb eines Servers weiss
                // er selbst, an wen er verteilt; über die Grenze ist das
                // 'to' alles, was die Gegenstelle hat - eine Stanza ohne
                // wird dort verworfen. Zentral hier und nicht bei den
                // Aufrufern, weil sonst jeder neue Aufrufer daran denken
                // müsste.
                //
                // Ehrlich vermerkt: kein Test hält diese Zeile fest. Der
                // einzige heutige Aufrufer, der ohne 'to' ankommt, ist die
                // nachgereichte Presence aus Abschnitt 3.1.5, und dort
                // verdeckt das Verhalten des Clients den Unterschied. Sie
                // bleibt als Vorkehrung für den nächsten Aufrufer stehen.
                // Und der Namensraum muss mitwechseln. Was von einem Client
                // hereinkam, steht in jabber:client; hinaus geht es auf einem
                // Stream, der jabber:server spricht (RFC 6120, Abschnitt
                // 4.8.1). Prosody beantwortet ein jabber:client-IQ auf dem
                // S2S-Stream mit einem Fehler - zwischen zwei Instanzen dieses
                // Servers fiele es nie auf, weil beide nur den lokalen Namen
                // ansehen.
                return ServerLinks is not null &&
                       await ServerLinks.DeliverAsync(DomainOf(to),
                                                      StanzaNamespace.Apply(StampTo(stanza, to),
                                                                            StanzaNamespace.Server),
                                                      _cts.Token);

            }

            var targets = to.Contains('/')
                              ? (SessionOf(to) is { } one ? [one] : Array.Empty<XMPPSession>())
                              : SessionsOf(to).ToArray();

            foreach (var t in targets)
                await t.SendAsync(stanza);

            return true;

        }

        /// <summary>
        /// Nimmt eine Stanza von einem anderen Server entgegen - der
        /// Gegenpart zu <see cref="IServerLinks"/>.
        /// </summary>
        /// <param name="peerDomain">
        /// Die Domain, für die die Gegenstelle sprechen darf. Ein echter
        /// Transport setzt das nach Dialback (XEP-0220) oder SASL-EXTERNAL;
        /// hier ist es das Versprechen des Links.
        /// </param>
        /// <param name="stanza">Die eingehende Stanza.</param>
        /// <returns>false, wenn sie abgewiesen wurde.</returns>
        /// <remarks>
        /// Die Absenderprüfung ist der Kern und nicht Beiwerk: eine
        /// Gegenstelle darf ausschliesslich für ihre eigene Domain sprechen.
        /// Ohne diese Prüfung könnte jeder Server, mit dem man je spricht,
        /// Nachrichten im Namen jedes beliebigen anderen einschleusen - der
        /// gesamte Aufwand von Dialback wäre dann umsonst.
        ///
        /// RFC 6120, Abschnitt 8.1.1.1 lässt einen Server bei einem falschen
        /// <c>from</c> den Stream mit <c>&lt;invalid-from/&gt;</c> beenden.
        /// Ob es dazu kommt, entscheidet nicht diese Methode, sondern der
        /// Stream, über den die Stanza kam - hier gibt es nur das Urteil.
        /// Deshalb liefert <see cref="AcceptFromRemoteAsync"/> einen
        /// <see cref="RemoteStanzaResult"/>; diese Überladung reicht ihn als
        /// Ja/Nein weiter, für Aufrufer, denen der Grund gleich ist.
        /// </remarks>
        public async Task<Boolean> ReceiveFromRemoteAsync(String peerDomain, String stanza)

            => await AcceptFromRemoteAsync(peerDomain, stanza) == RemoteStanzaResult.Accepted;

        /// <summary>
        /// Wie <see cref="ReceiveFromRemoteAsync"/>, aber mit dem Grund einer
        /// Ablehnung.
        /// </summary>
        public async Task<RemoteStanzaResult> AcceptFromRemoteAsync(String peerDomain, String stanza)
        {

            var from  = Attr(stanza, "from");
            var to    = Attr(stanza, "to");

            if (from is null || to is null)
            {
                OnRemoteStanzaRejected?.Invoke(peerDomain, "from oder to fehlt");
                return RemoteStanzaResult.MissingAddress;
            }

            if (!String.Equals(DomainOf(from), peerDomain, StringComparison.OrdinalIgnoreCase))
            {
                OnRemoteStanzaRejected?.Invoke(
                    peerDomain,
                    $"'{from}' gehört nicht zu '{peerDomain}'");
                return RemoteStanzaResult.ForeignSender;
            }

            if (!IsLocal(to))
            {
                // Weiterleiten für Dritte wäre ein offenes Relais.
                OnRemoteStanzaRejected?.Invoke(peerDomain, $"'{to}' liegt nicht auf '{Domain}'");
                return RemoteStanzaResult.ForeignRecipient;
            }

            if (!RouteStanzas)
                return RemoteStanzaResult.RoutingDisabled;

            // RFC 6121, Abschnitt 3: eine Subscription-Presence ist keine
            // Nachricht, die nur weitergereicht wird - sie ändert den Roster
            // der hiesigen Seite. Ohne diesen Schritt käme die Anfrage zwar
            // beim Client an, aber der Server vergässe sie, und die Antwort
            // fände keinen Eintrag vor, den sie ändern könnte.
            var art = SubscriptionTypeOf(stanza);

            if (art is not null)
            {
                await ApplyRemoteSubscriptionAsync(BareOf(from), BareOf(to), art, stanza);
                return RemoteStanzaResult.Accepted;
            }

            // RFC 6121, Abschnitt 8.5 gilt für jede eingehende Stanza und fragt
            // nicht, woher sie kam. Eine Nachricht nimmt deshalb denselben Weg
            // wie die eines hiesigen Clients - mit Offline-Ablage, Prioritäten
            // und Typunterscheidung. Bis hierher ging sie unbesehen ins Routing,
            // und das traf gerade den häufigsten Fall: Der Bekannte auf einem
            // anderen Server ist der Regelfall.
            if (stanza.StartsWith("<message", StringComparison.Ordinal))
            {
                await DeliverMessageLocallyAsync(null, to, stanza);
                return RemoteStanzaResult.Accepted;
            }

            // Und dasselbe für die Anfrage an ein Konto: Sie darf nicht an alle
            // Resourcen verteilt werden, sondern gehört beantwortet.
            //
            // Nur mit Lokalteil. Abschnitt 8.5.2 handelt von einer Adresse „of
            // the form <localpart@domainpart>"; eine Anfrage an die Domain
            // selbst richtet sich an den Server und nicht an einen Nutzer, und
            // dafür gilt der Abschnitt nicht.
            if (stanza.StartsWith("<iq", StringComparison.Ordinal) &&
                to.Contains('@'))
            {
                await DeliverIqLocallyAsync(null, to, stanza);
                return RemoteStanzaResult.Accepted;
            }

            // Presence nimmt weiterhin den geraden Weg, und eine Anfrage an die
            // Domain ebenso - was der Server für sich selbst beantworten müsste,
            // beantwortet er noch nicht (siehe „Später").
            await RouteToAsync(to, stanza);

            return RemoteStanzaResult.Accepted;

        }

        /// <summary>
        /// Stellt eine Anfrage an ein hiesiges Konto zu - oder beantwortet sie
        /// selbst.
        /// </summary>
        /// <remarks>
        /// Eine Stelle für beide Herkünfte, lokal wie über die Grenze. Die
        /// Entscheidung hängt nicht daran, woher die Anfrage kam, sondern
        /// allein am Roster des Empfängers; sie zweimal zu treffen hiesse,
        /// zwei Gelegenheiten zu schaffen, sie verschieden zu treffen.
        ///
        /// Zwei Gründe, selbst zu antworten:
        /// <list type="bullet">
        ///   <item>
        ///     Der Antragsteller darf uns ohnehin schon sehen (Abschnitt
        ///     3.1.4) - die Frage ist beantwortet, bevor sie gestellt wurde.
        ///   </item>
        ///   <item>
        ///     Er ist vorgemerkt (Abschnitt 3.4.2) - dann <b>darf</b> die
        ///     Anfrage dem Nutzer gar nicht erst zugestellt werden.
        ///   </item>
        /// </list>
        /// </remarks>
        private async Task DeliverSubscribeAsync(String fromBareJid,
                                                 String toBareJid,
                                                 String stanza)
        {

            var account = GetAccount(toBareJid);

            // RFC 6121, Abschnitt 8.1: für ein Konto, das es hier nicht gibt,
            // ist nichts zu tun.
            if (account is null)
                return;

            var eintrag = RosterEntryOf(account, fromBareJid);

            if (eintrag?.Approved == true ||
                account.SubscriptionOf(fromBareJid) is "from" or "both")
            {
                await AutoApproveAsync(account, fromBareJid);
                return;
            }

            // Abschnitt 3.1.3, Regel 4: die vollständige Stanza wird
            // aufbewahrt, bis der Kontakt zustimmt oder ablehnt, und bei jeder
            // neu verfügbaren Resource erneut zugestellt.
            //
            // Aufbewahrt wird immer, nicht nur wenn gerade niemand verbunden
            // ist. Die Regel verlangt die Zustellung an *jede* Resource, die
            // der Kontakt danach noch anlegt; eine Anfrage nur dann
            // aufzuheben, wenn zufällig gerade niemand da war, verfehlte
            // genau den Fall, für den es die Regel gibt - der Kontakt ist
            // angemeldet, sieht aber gerade nicht hin und meldet sich ab.
            //
            // Nebenbei hält dasselbe Aufbewahren fest, dass eine Anfrage
            // offen ist. Daran hängt nach Abschnitt 3.4.2, ob ein späteres
            // 'subscribed' eine Zustimmung ist oder eine Vormerkung.
            //
            // Anhang A, Tabelle 6: liegt bereits eine Anfrage dieses
            // Absenders vor, soll sie nicht ein zweites Mal zugestellt werden.
            if (!account.RememberSubscriptionRequest(fromBareJid, stanza,
                                                     MaxStoredSubscriptionRequests))
            {
                return;
            }

            // Kein Roster-Eintrag: die Security Warning desselben Abschnitts
            // untersagt ihn ausdrücklich, solange nicht zugestimmt wurde.
            await RouteToAsync(toBareJid, stanza);

        }

        /// <summary>
        /// Stellt einer neu verfügbaren Resource die aufbewahrten
        /// Subscription-Anfragen zu (RFC 6121, Abschnitt 3.1.3, Regel 4).
        /// </summary>
        /// <remarks>
        /// Die Anfragen bleiben dabei stehen. Die Regel verlangt die
        /// Zustellung, "until the contact either approves or denies the
        /// request" - eine beim ersten Anmelden übersehene Anfrage wäre sonst
        /// für immer verloren, und der Antragsteller wartete auf eine Antwort,
        /// die niemand mehr geben kann.
        /// </remarks>
        private async Task SendStoredSubscriptionRequestsToAsync(XMPPSession session)
        {

            if (session.Account is not { } account)
                return;

            foreach (var anfrage in account.PendingSubscriptionRequests)
                await session.SendAsync(anfrage.Value);

        }

        /// <summary>
        /// Beantwortet eine Anfrage im Namen des Nutzers.
        /// </summary>
        /// <remarks>
        /// Die Antwort geht denselben Weg wie eine von Hand gegebene: der
        /// Antragsteller soll nicht unterscheiden können, ob ein Mensch oder
        /// der Server zugestimmt hat. Liegt er auf dieser Domain, wird auch
        /// seine Roster-Hälfte gepflegt - über die Grenze erledigt das sein
        /// eigener Server, sobald das <c>subscribed</c> dort ankommt.
        /// </remarks>
        private async Task AutoApproveAsync(XMPPAccount account, String requesterBareJid)
        {

            // Vorkehrung, kein lebender Pfad: der einzige Aufrufer entscheidet
            // sich für die selbsttätige Zustimmung, *bevor* er aufbewahrt, und
            // beide Wege, auf denen eine Subscription 'from' wird, räumen die
            // Anfrage bereits ab. Es gibt also heute keinen Zustand, in dem
            // hier noch etwas läge - kein Test hält die Zeile fest, und eine
            // Mutation überlebt sie. Sie steht, weil das eine Aussage über die
            // Reihenfolge in DeliverSubscribeAsync ist und nicht über diese
            // Methode: wer dort umstellt, liesse die Anfrage sonst liegen.
            account.ForgetSubscriptionRequest(requesterBareJid);

            UpdateRosterEntry(account, requesterBareJid,
                              GrantFrom(account.SubscriptionOf(requesterBareJid)),
                              approved: false);

            await PushRosterEntryAsync(account, requesterBareJid);

            if (GetAccount(requesterBareJid) is { } requester)
            {
                UpdateRosterEntry(requester, account.BareJid,
                                  GrantTo(requester.SubscriptionOf(account.BareJid)),
                                  ask: AskChange.Clear);
                await PushRosterEntryAsync(requester, account.BareJid);
            }

            await RouteToAsync(requesterBareJid,
                               $"<presence from='{account.BareJid}' to='{requesterBareJid}' type='subscribed'/>");

        }

        /// <summary>
        /// Der Typ einer Subscription-Presence, oder null wenn es keine ist.
        /// </summary>
        private static String? SubscriptionTypeOf(String stanza)
        {

            if (!stanza.StartsWith("<presence", StringComparison.Ordinal))
                return null;

            return Attr(stanza, "type") is "subscribe" or "subscribed" or
                                           "unsubscribe" or "unsubscribed"
                       ? Attr(stanza, "type")
                       : null;

        }

        /// <summary>
        /// Wendet eine von aussen eingegangene Subscription-Presence auf den
        /// Roster des hiesigen Kontos an (RFC 6121, Abschnitt 3).
        /// </summary>
        /// <param name="remoteBareJid">Der Absender auf der fremden Domain.</param>
        /// <param name="localBareJid">Das hiesige Konto.</param>
        /// <param name="type">subscribe, subscribed, unsubscribe oder unsubscribed.</param>
        /// <param name="stanza">Die eingegangene Stanza, zur Zustellung an die Resourcen.</param>
        /// <remarks>
        /// Hier wird genau <b>eine</b> Hälfte gepflegt: die des hiesigen
        /// Kontos. Die andere gehört der fremden Domain, und sie zu raten wäre
        /// falsch - jede Seite führt ihren eigenen Roster, und über die Grenze
        /// erfährt man voneinander nur das, was ausdrücklich geschickt wird.
        /// Genau darin liegt der Unterschied zum Handshake zwischen zwei
        /// lokalen Konten, wo derselbe Server beide Hälften in der Hand hat.
        /// </remarks>
        private async Task ApplyRemoteSubscriptionAsync(String  remoteBareJid,
                                                        String  localBareJid,
                                                        String  type,
                                                        String  stanza)
        {

            var account = GetAccount(localBareJid);

            // RFC 6121, Abschnitt 8.1: für ein Konto, das es hier nicht gibt,
            // ist nichts zu tun.
            if (account is null)
                return;

            switch (type)
            {

                // Zustellen oder selbst beantworten - dieselbe Entscheidung
                // wie bei einer Anfrage von nebenan.
                case "subscribe":
                    await DeliverSubscribeAsync(remoteBareJid, localBareJid, stanza);
                    return;

                // Abschnitt 3.1.6: die Zustimmung der Gegenseite setzt unsere
                // 'to'-Hälfte und erledigt die offene Anfrage.
                case "subscribed":
                    UpdateRosterEntry(account, remoteBareJid,
                                      GrantTo(account.SubscriptionOf(remoteBareJid)),
                                      ask: AskChange.Clear);
                    await PushRosterEntryAsync(account, remoteBareJid);
                    break;

                // Abschnitt 3.2.3: der Entzug nimmt uns die 'to'-Hälfte.
                case "unsubscribed":
                    UpdateRosterEntry(account, remoteBareJid,
                                      RevokeTo(account.SubscriptionOf(remoteBareJid)),
                                      ask: AskChange.Clear);
                    await PushRosterEntryAsync(account, remoteBareJid);
                    break;

                // Abschnitt 3.3.3: die Gegenseite kündigt, was sie bei uns
                // sehen durfte - also unsere 'from'-Hälfte. Und weil sie uns
                // nicht mehr sehen darf, geht die Abmeldung hinterher.
                case "unsubscribe":
                    UpdateRosterEntry(account, remoteBareJid,
                                      RevokeFrom(account.SubscriptionOf(remoteBareJid)));
                    await PushRosterEntryAsync(account, remoteBareJid);
                    await SendOwnUnavailableToAsync(account, remoteBareJid);
                    break;

            }

            // Die Stanza selbst gehört dem Client: über 'subscribe' will er
            // entscheiden, über die übrigen Bescheid wissen.
            await RouteToAsync(localBareJid, stanza);

        }

        /// <summary>
        /// RFC 6121, Abschnitt 8.5: Die Stanza war an dieser Adresse nicht
        /// zustellbar.
        /// </summary>
        /// <param name="intendedRecipient">
        /// Die Adresse, an die es nicht ging - sie wird zum Absender der
        /// Antwort. Für den Client ist die Frage „was ist aus meiner Nachricht
        /// an bob geworden", und genau darauf antwortet sie; dieser Server als
        /// Absender wäre eine Antwort auf eine andere Frage.
        /// </param>
        /// <param name="replyTo">Das geprüfte <c>from</c> der Stanza.</param>
        /// <remarks>
        /// Ein Weg zurück, nicht zwei. Ob der Absender hier sitzt oder auf einem
        /// anderen Server, entscheidet <see cref="RouteToAsync"/> - das ist ihre
        /// einzige Aufgabe, und sie erledigt dabei auch den Namensraumwechsel.
        /// Eine eigene Verzweigung für den hiesigen Fall wäre eine zweite
        /// Antwort auf eine schon beantwortete Frage, und die beiden könnten
        /// auseinanderlaufen.
        ///
        /// Kommt die Antwort nicht an, bleibt es dabei. Ein Fehler, der einen
        /// Fehler nach sich zöge, wäre der Anfang einer Schleife (RFC 6120,
        /// Abschnitt 8.3.1) - deshalb wird das Ergebnis der Zustellung hier
        /// bewusst nicht angesehen.
        /// </remarks>
        private async Task SendServiceUnavailableAsync(String   kind,
                                                       String?  id,
                                                       String   intendedRecipient,
                                                       String   replyTo)
        {

            await RouteToAsync(
                replyTo,
                $"<{kind} type='error'" +
                (id is not null ? $" id='{id}'" : "") +
                $" from='{intendedRecipient}' to='{replyTo}'>" +
                "<error type='cancel'>" +
                "<service-unavailable xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                "</error>" +
                $"</{kind}>");

        }

        /// <summary>
        /// Meldet dem Absender, dass die Domain des Empfängers nicht erreichbar
        /// ist.
        /// </summary>
        /// <remarks>
        /// RFC 6120, Abschnitt 10.4.3 verlangt einen Stanza-Fehler, legt die
        /// Bedingung aber nicht fest; <c>&lt;remote-server-not-found/&gt;</c>
        /// steht in Abschnitt 8.3.3.
        ///
        /// Der Fehler trägt den ursprünglichen Empfänger als Absender, nicht
        /// diesen Server: für den Client ist die Frage "was ist aus meiner
        /// Nachricht an bob@anderswo.example geworden" - und genau darauf
        /// antwortet er.
        ///
        /// Auf eine Fehler-Stanza folgt nie ein Fehler (Abschnitt 8.3.1).
        /// Sonst könnten zwei Server sich gegenseitig Meldungen zuschieben,
        /// bis einer aufgibt. Diese Prüfung steht bei den Aufrufern, weil nur
        /// dort der Typ der eingehenden Stanza bekannt ist.
        /// </remarks>
        private async Task SendRemoteServerNotFoundAsync(XMPPSession  session,
                                                         String       kind,
                                                         String?      id,
                                                         String       intendedRecipient)
        {

            await session.SendAsync(
                $"<{kind} type='error'" +
                (id is not null ? $" id='{id}'" : "") +
                $" from='{intendedRecipient}' to='{session.FullJid}'>" +
                "<error type='cancel'>" +
                "<remote-server-not-found xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                "</error>" +
                $"</{kind}>");

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

        /// <summary>Setzt oder ersetzt das to-Attribut im äussersten Element.</summary>
        /// <remarks>
        /// Ungerichtete Presence trägt kein <c>to</c> - innerhalb eines
        /// Servers braucht sie auch keines, weil er selbst weiss, an wen er
        /// sie verteilt. Über eine Domain-Grenze geht das nicht: dort ist die
        /// Adresse alles, was die Gegenstelle hat, und ohne sie weist sie die
        /// Stanza ab.
        /// </remarks>
        internal static String StampTo(String stanza, String jid)
        {

            var m = Regex.Match(stanza, @"^<(\w+)([^>]*?)(/?)>");

            if (!m.Success)
                return stanza;

            var attrs = Regex.Replace(m.Groups[2].Value, @"\s+to=['""][^'""]*['""]", "");

            return $"<{m.Groups[1].Value}{attrs} to='{jid}'{m.Groups[3].Value}>" +
                   stanza[m.Length..];

        }

        private static String? Attr(String xml, String name)
        {
            var m = Regex.Match(xml, @"^<\w+[^>]*?\s" + name + @"=['""]([^'""]*)['""]");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// Ein Attribut des <c>&lt;query/&gt;</c>-Kindelements.
        /// </summary>
        /// <remarks>
        /// <see cref="Attr"/> ist auf das Wurzelelement verankert und liefert
        /// für ein Attribut am Kindelement stillschweigend null. Das
        /// <c>ver</c> der Roster-Anfrage sitzt aber am <c>&lt;query/&gt;</c>,
        /// nicht am <c>&lt;iq/&gt;</c> - eine Prüfung mit <c>Attr</c> sähe
        /// richtig aus und läse nie etwas.
        /// </remarks>
        private static String? QueryAttr(String xml, String name)
        {

            var m = Regex.Match(xml, @"<query\b([^>]*)>");

            if (!m.Success)
                return null;

            var a = Regex.Match(m.Groups[1].Value, @"\b" + name + @"\s*=\s*['""]([^'""]*)['""]");

            return a.Success ? a.Groups[1].Value : null;

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
        /// Der Domainteil eines JIDs - aus <c>alice@example.com/mobil</c> wird
        /// <c>example.com</c>.
        /// </summary>
        /// <remarks>
        /// Ein JID ohne <c>@</c> ist eine blosse Domain, wie sie in <c>to</c>
        /// steht, wenn eine Stanza an den Server selbst geht.
        /// </remarks>
        internal static String DomainOf(String jid)
        {

            var bare  = BareOf(jid);
            var at    = bare.IndexOf('@');

            return at >= 0 ? bare[(at + 1)..] : bare;

        }

        /// <summary>Gehört dieser JID zu der Domain, die dieser Server bedient?</summary>
        internal Boolean IsLocal(String jid)
            => String.Equals(DomainOf(jid), Domain, StringComparison.OrdinalIgnoreCase);

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
            // Client Authentication kommt für SASL-EXTERNAL dazu: dort legt
            // der aufbauende Server sein Zertifikat als Client vor, und ein
            // Zertifikat ohne diese Verwendung würde dabei abgelehnt.
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1"),
                                                   new Oid("1.3.6.1.5.5.7.3.2")], true));

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

            if (_resumptionSweeper is not null)
            {
                await _resumptionSweeper.DisposeAsync();
                _resumptionSweeper = null;
            }

            KillAllSessions();

            lock (_lock)
                _resumable.Clear();

            try { await _webSocketServer.Shutdown(Wait: true); }
            catch { }

            _cts.Dispose();

        }

    }

}
