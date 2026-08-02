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
using System.Xml.Linq;

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

        /// <summary>
        /// Der Schlüssel, aus dem die erfundenen Zugangsdaten unbekannter
        /// Konten entstehen (RFC 6120, Abschnitt 13.11).
        /// </summary>
        /// <remarks>
        /// Je Server einer, aus dem Zufallsgenerator. Er darf nicht zu erraten
        /// sein: Wer ihn kennt, kann jedes erfundene Salt nachrechnen und
        /// wieder unterscheiden, welches Konto es gibt.
        /// </remarks>
        private readonly Byte[] _decoySecret =
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

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

        /// <summary>
        /// Alle Sitzungen dieses Servers, auch beendete - in der Reihenfolge
        /// ihres Aufbaus.
        /// </summary>
        /// <remarks>
        /// <see cref="Sessions"/> zeigt nur die offenen, und genau die gibt es
        /// nicht mehr, wenn ein Aufbau an der Anmeldung gescheitert ist. Wer
        /// prüfen will, was der Server einem abgewiesenen Client geantwortet
        /// hat, findet die Sitzung nur hier.
        /// </remarks>
        public IReadOnlyList<XMPPSession> AllSessions
        {
            get { lock (_lock) return [.. _sessions]; }
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
        /// Beantwortet der Server PEP-Anfragen, oder schweigt er dazu?
        /// </summary>
        /// <remarks>
        /// Wie <see cref="AnswerPings"/> für XEP-0199, und aus demselben Grund:
        /// <b>Ein Client, der auf eine Antwort wartet, ist nur gegen einen
        /// Server zu prüfen, der auch einmal keine gibt.</b> Fehlschlag und
        /// Schweigen sind zwei verschiedene Fälle, und Schweigen ist der, den
        /// man am ehesten falsch behandelt - es meldet sich nicht.
        /// </remarks>
        public Boolean AnswerPepRequests { get; set; } = true;

        /// <summary>
        /// XEP-0198: Handelt der Server Stream Management aus? Auf false
        /// antwortet er auf <c>&lt;enable/&gt;</c> mit <c>&lt;failed/&gt;</c>.
        /// </summary>
        public Boolean OfferStreamManagement { get; set; } = true;

        /// <summary>XEP-0198: Beantwortet der Server ein <c>&lt;r/&gt;</c> des Clients?</summary>
        public Boolean AnswerAckRequests { get; set; } = true;

        /// <summary>
        /// XEP-0352: Kündigt der Server Client State Indication an?
        /// </summary>
        /// <remarks>
        /// Auf false verschwindet nicht nur die Ankündigung, sondern auch die
        /// Behandlung: Ein <c>&lt;inactive/&gt;</c> gilt dann wie jedes andere
        /// unangekündigte Element. Ein Server, der die Erweiterung
        /// verschweigt und trotzdem danach handelt, wäre der schlimmere Fall -
        /// der Client hielte seine Kontakte für still, während der Server sie
        /// zurückhält.
        /// </remarks>
        public Boolean OfferClientStateIndication { get; set; } = true;

        /// <summary>
        /// XEP-0352: Wie viele Stanzas eine Sitzung höchstens zurückhält,
        /// bevor der Puffer von sich aus hinausgeht.
        /// </summary>
        public Int32 MaxHeldWhileInactive { get; set; } = 100;

        /// <summary>
        /// XEP-0163: Beantwortet der Server PEP-Anfragen für seine Konten?
        /// </summary>
        /// <remarks>
        /// Auf false verhält er sich wie ein Server ohne Personal Eventing:
        /// Eine Anfrage an einen fremden Bare-JID geht dann den gewöhnlichen
        /// Weg und landet bei dessen Client - der sie nicht kennt und mit
        /// <c>&lt;service-unavailable/&gt;</c> antwortet. Genau daran muss ein
        /// OMEMO-Client erkennen, dass hier nichts zu holen ist.
        /// </remarks>
        public Boolean OfferPersonalEventing { get; set; } = true;

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
        /// Testschalter: Der Server schweigt auf die Stream-Eröffnung.
        /// </summary>
        /// <remarks>
        /// Stellt den einen Fall her, den ein Fehlschlag nicht herstellt: eine
        /// Gegenstelle, die die Verbindung annimmt und dann <b>nichts</b> sagt.
        /// Ein Fehler kommt an, ein geschlossener Socket kommt an — Schweigen
        /// kommt nicht an, und genau darauf hat die Aushandlung des Clients
        /// unbegrenzt gewartet.
        ///
        /// Kein erfundener Fall: Ein Server hinter einer Zustandstabelle, die
        /// den Rückweg vergessen hat, verhält sich genau so, und es ist der
        /// unangenehmste Ausgang von allen — der Aufrufer erfährt nie, dass
        /// etwas nicht stimmt.
        /// </remarks>
        public Boolean AnswerStreamOpen { get; set; } = true;

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
        /// Testschalter: Räumt der Durchgang abgelaufene Streams ab?
        /// </summary>
        /// <remarks>
        /// Stellt den einen Zustand her, der sich sonst nur im Wettlauf
        /// treffen lässt: eine Frist, die abgelaufen ist, während der Stream
        /// noch daliegt. Genau dort - und nur dort - kennt der Server den
        /// Stand eines Streams, den er trotzdem nicht mehr herausgibt, und
        /// kann ihn in seiner Abweisung nennen (XEP-0198, Abschnitt 5).
        ///
        /// Im Betrieb ist das Fenster höchstens eine Sekunde breit; ein Test,
        /// der es treffen will, prüfte in Wahrheit den Abräumer.
        /// </remarks>
        public Boolean SweepResumableStreams { get; set; } = true;

        /// <summary>
        /// Wie viele abgerissene Streams gerade auf ihren Rückkehrer warten.
        /// </summary>
        public Int32 ResumableStreamCount
        {
            get { lock (_lock) return _resumable.Count; }
        }

        /// <summary>
        /// Lässt das Verarbeiten eines Frames mit einer Ausnahme scheitern -
        /// der einzige Weg, den Meldeweg von
        /// <see cref="OnInternalError"/> zu erreichen.
        /// </summary>
        /// <remarks>
        /// Ein Schalter, dessen ganze Aufgabe ein Fehlschlag ist, sieht seltsam
        /// aus und ist hier notwendig: Ein Wächter, den nichts auslöst, ist
        /// selbst unbewacht. Genau daran lag der Fehler, den dieser Schritt
        /// behebt - der alte <c>catch</c> ohne Filter wurde von keinem Test
        /// erreicht, und deshalb fiel jahrelang nicht auf, was er verschluckte.
        ///
        /// Dieselbe Begründung wie bei
        /// <see cref="SwallowClientStanzas"/>: Ein Zustand, der im Betrieb
        /// vorkommen kann, aber von aussen nicht herzustellen ist, wird sonst
        /// nie geprüft.
        /// </remarks>
        public Boolean FailFrameHandling { get; set; } = false;

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

        /// <summary>
        /// Wird ausgelöst, wenn das Verarbeiten eines Frames mit einer Ausnahme
        /// endet - mit der Sitzung, dem Frame und der Ausnahme.
        /// </summary>
        /// <remarks>
        /// Der einzige Zweck ist Sichtbarkeit. Vorher stand an dieser Stelle ein
        /// <c>catch</c> ohne Filter mit dem Vermerk „Verbindung abgerissen - im
        /// Test der Normalfall". Der Vermerk stimmte nicht mehr: Eine Messung
        /// über die gesamte Sammlung fing <b>keine einzige</b> Ausnahme. Was der
        /// Fang tatsächlich noch leistete, war das lautlose Verschlucken von
        /// Programmierfehlern - in D15 überlebte eine Mutation nur deshalb, weil
        /// ihre <c>NullReferenceException</c> hier verschwand.
        ///
        /// Gefiltert wird deshalb nicht. Eine Liste von Ausnahmen, die ein
        /// Abriss „wirklich" erzeugt, wäre geraten - die Messung sagt, dass
        /// keine davon vorkommt -, und ein Zweig, den kein Test erreicht, ist
        /// genau die Sorte Vorkehrung, die den Fehler von damals gedeckt hat.
        /// Gemeldet wird alles; die Testsammlung behandelt jede Meldung als
        /// Mangel, bis das Gegenteil gezeigt ist.
        ///
        /// Die Ausnahme wird nach der Meldung nicht weitergeworfen: Hermod fängt
        /// oberhalb ohnehin jede und schreibt sie in ein Log, das kein Test
        /// ansieht. Am Verhalten des Servers ändert sich damit nichts - nur
        /// daran, ob jemand davon erfährt.
        /// </remarks>
        public event Action<XMPPSession, String, Exception>? OnInternalError;

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

            OnInstanceCreated?.Invoke(this);

        }

        #endregion

        #region (internal, static) OnInstanceCreated

        /// <summary>
        /// Meldet jede erzeugte Instanz - nur für die Testsammlung.
        /// </summary>
        /// <remarks>
        /// Die Wache gegen verschluckte Programmierfehler
        /// (<c>OnInternalError</c>) hing bisher daran, dass jedes Fixture sie
        /// von Hand anhängt. Das ist eine mechanische Eigenschaft, die kein
        /// Test hält: Wer einen Server ohne die Wache erzeugt, bekommt keinen
        /// Fehlschlag, sondern <b>Stille</b> - und genau das war der Zustand,
        /// den die Wache abschaffen sollte.
        ///
        /// Über dieses Ereignis findet die Sammlung jeden Server, ohne dass
        /// jemand daran denken muss. Es ist <c>internal</c> und damit keine
        /// Zusage nach aussen; sichtbar wird es allein über
        /// <c>InternalsVisibleTo</c>.
        ///
        /// Ausgelöst am Ende des Konstruktors, nicht am Anfang: Ein Abonnent
        /// bekommt eine fertig aufgebaute Instanz und keine halbe.
        /// </remarks>
        internal static event Action<XMPPServer>? OnInstanceCreated;

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
                                              Interlocked.Increment(ref _connectionCounter))
                {
                    MaxHeldWhileInactive = MaxHeldWhileInactive
                };

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

            if (StanzaElement.Is(frame, "open"))
                session.OpenCount++;

            try
            {
                await HandleFrameAsync(session, frame, session.OpenCount);
            }
            catch (Exception e)
            {

                // Gemeldet statt verschluckt - siehe OnInternalError. Vor dem
                // Abschluss, damit ein Abnehmer die Ausnahme auch dann sieht,
                // wenn das Schliessen selbst schiefgeht.
                OnInternalError?.Invoke(session, frame, e);

                // RFC 6120, Abschnitt 4.9.3.8: „The server has experienced a
                // misconfiguration or other internal error that prevents it from
                // servicing the stream." Genau das ist hier eingetreten - und
                // Abschnitt 4.9.1.1 lässt danach keine Wahl: Stream-Fehler sind
                // unwiederbringlich, der Stream wird geschlossen.
                //
                // Bis D21 lief der Stream weiter. Das war bequem und falsch: Was
                // der Frame ändern sollte, ist halb geändert, und niemand weiss,
                // wie weit. Der Client rechnet mit einem Zustand, den der Server
                // nicht mehr hat - und ausgerechnet der Fehler, der am
                // wahrscheinlichsten Zustand hinterlässt, blieb der einzige ohne
                // Folgen.
                //
                // Der Client kommt wieder: <internal-server-error/> gilt als
                // wiederholbar (RFC 6120, Abschnitt 4.9.3.8 nennt keinen Grund,
                // es für endgültig zu halten), und ein neuer Stream beginnt mit
                // einem Zustand, über den beide Seiten sich einig sind. Genau das
                // ist der Sinn eines unwiederbringlichen Fehlers.
                try
                {
                    await session.SendStreamErrorAsync("internal-server-error");
                }
                catch (Exception beimSchliessen)
                {
                    OnInternalError?.Invoke(session, frame, beimSchliessen);
                }

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

            // XEP-0352: Was zurückgehalten wurde, geht jetzt seinen gewohnten
            // Weg - bei einem aufgehobenen Stream in den Puffer der
            // unbestätigten Stanzas, sonst ins Leere.
            //
            // Vor allem anderen, und das ist der Grund: Ab hier kommt nichts
            // mehr an dieser Sitzung vorbei. Bliebe der Puffer stehen, hätte
            // die Sparmassnahme aus jedem Abriss einen Verlust gemacht - der
            // Rückkehrer bekäme alles nachgeliefert ausser dem, was der Server
            // für ihn beiseitegelegt hatte.
            await session.FlushHeldAsync();

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

            // Vor dem Wächter unten abgeholt, nicht dahinter: Die Anwesenheit
            // dieser Resource ist zu Ende, und damit auch jede Zusage, die sie
            // über gerichtete Presence gegeben hat (Abschnitt 4.6.1). Stünde das
            // erst nach dem Wächter, bliebe die Liste stehen, sobald einmal
            // nicht verteilt wird - und ein Fremder dürfte eine abgemeldete
            // Resource weiter befragen (Abschnitt 8.5.3.1).
            var gerichtete = session.TakeDirectedPresenceTargets();

            // Beim Herunterfahren des Servers geht es an niemanden mehr.
            if (!RouteStanzas || !BroadcastPresence || _cts.IsCancellationRequested)
                return;

            var stanza = $"<presence type='unavailable' from='{session.FullJid}'/>";

            // Auch hier, nicht nur in RouteToAsync: Die Verteilung an hiesige
            // Kontakte geht unmittelbar an die Sitzung, ohne die Weiche zu
            // nehmen.
            //
            // Das ist kein Nachziehen der Vollständigkeit halber, sondern nötig -
            // die beiden Roster-Hälften sind hier leicht zu verwechseln. Wer die
            // Abmeldung über diesen Weg bekommt, steht in *Alices* Roster mit
            // 'from' (Bob darf Alice sehen); über sein Fragerecht entscheidet
            // aber *Bobs* Roster. Ist der leer, hängt es allein an der Liste
            // gerichteter Presence - und ohne diese Zeile überlebte es Alices
            // Abmeldung.
            foreach (var target in PresenceTargetsOf(session))
            {
                ForgetDirectedPresenceFrom(target, stanza);
                await target.SendAsync(stanza);
            }

            foreach (var remote in RemotePresenceTargetsOf(session))
                await RouteToAsync(remote, StampTo(stanza, remote));

            await SendUnavailableToDirectedTargetsAsync(session, gerichtete, stanza);

        }

        /// <summary>
        /// Reicht die Abmeldung an die Empfänger gerichteter Presence nach
        /// (RFC 6121, Abschnitt 4.6.3, Regel 2).
        /// </summary>
        /// <param name="targets">
        /// Die Empfänger, wie sie
        /// <see cref="XMPPSession.TakeDirectedPresenceTargets"/> herausgegeben
        /// hat.
        /// </param>
        /// <param name="unavailable">
        /// Die Abmeldung - entweder die des Clients selbst oder die, die der
        /// Server in seinem Namen erzeugt hat.
        /// </param>
        /// <remarks>
        /// Die Regel schliesst eine Lücke, die sonst niemandem auffällt: Wer
        /// einem Fremden seine Anwesenheit gezeigt hat, steht deswegen nicht in
        /// dessen Roster - und bekäme ohne diesen Weg nie ein Ende. Der Fremde
        /// führte die Resource für immer als anwesend, und weil ein Gespräch mit
        /// einem Nichtkontakt genau so beginnt (Abschnitt 5.1), ist das der
        /// Regelfall und nicht die Ausnahme.
        ///
        /// Übersprungen wird, wer im Roster mit <c>from</c> oder <c>both</c>
        /// steht: Der hat die Abmeldung schon über die gewöhnliche Verteilung
        /// bekommen. Ohne diese Einschränkung käme sie zweimal - und ein Client,
        /// der Presence zählt statt sie zu ersetzen, käme durcheinander. Der RFC
        /// grenzt Regel 2 aus demselben Grund auf Entitäten ein, die
        /// <b>nicht</b> mit <c>from</c> oder <c>both</c> im Roster stehen.
        ///
        /// Wer schon eine gerichtete Abmeldung bekommen hat, steht gar nicht
        /// mehr in der Liste - das erledigt
        /// <see cref="XMPPSession.RecordDirectedPresence"/>, und genau darauf
        /// zielt der Klammerzusatz der Regel („if the user has not yet sent
        /// directed unavailable presence to that entity").
        /// </remarks>
        private async Task SendUnavailableToDirectedTargetsAsync(XMPPSession                 session,
                                                                IReadOnlyCollection<String>  targets,
                                                                String                       unavailable)
        {

            foreach (var ziel in targets)
            {

                if (session.Account?.IsPresenceSubscriber(ziel) == true)
                    continue;

                await RouteToAsync(ziel, StampTo(unavailable, ziel));

            }

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

            if (!SweepResumableStreams)
                return;

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

            if (FailFrameHandling)
                throw new InvalidOperationException(
                          "FailFrameHandling: absichtlicher Fehlschlag beim Verarbeiten eines Frames.");

            // Entschieden wird am Elementnamen und nicht an einem Präfix. Ein
            // StartsWith("<iq") trifft auch <iqbogus/>, StartsWith("<presence")
            // auch <presence-probe/> - und das war kein gedachter Fall: Ein
            // <presence-probe/> lief in die Presence-Behandlung und galt dort
            // als Anwesenheit. Ein Mensch wurde seinen Kontakten als online
            // gemeldet, weil sein Element mit denselben acht Zeichen beginnt.
            var elementName = StanzaElement.NameOf(frame);

            // RFC 6120, Abschnitt 8.3.3.8: Steht im 'to' kein JID, ist die
            // Stanza nicht zustellbar - und zwar unabhängig davon, was sie
            // sonst noch ist. Deshalb vor der Weiche und für alle drei Arten
            // an einer Stelle: Jeder Zweig dahinter fragt seine eigenen Dinge,
            // und diese Frage gehört keinem von ihnen.
            if (elementName is "iq" or "message" or "presence" &&
                await RefuseMalformedToAsync(session, frame, elementName))
                return;

            switch (elementName)
            {

                case "open":
                    await HandleStreamOpenAsync(session, openCount);
                    return;

                case "auth":
                    await HandleAuthAsync(session, frame);
                    return;

                case "response":
                    await HandleSaslResponseAsync(session, frame);
                    return;

                case "abort":
                    await HandleSaslAbortAsync(session);
                    return;

                case "iq":
                    await HandleIqAsync(session, frame);
                    return;

                case "message":
                    await HandleMessageAsync(session, frame);
                    return;

                case "presence":
                    await HandlePresenceAsync(session, frame);
                    return;

            }

            // Der Namensraum allein entscheidet nicht: Was er nicht kennt,
            // fällt weiter nach unten und bekommt dieselbe Antwort wie jedes
            // andere unbekannte Element. Bis D29 endete der Zweig hier - er
            // war die letzte Stelle, an der ein Rahmen stillschweigend hinten
            // herausfiel.
            if (frame.Contains("urn:xmpp:sm:3", StringComparison.Ordinal) &&
                await HandleStreamManagementAsync(session, frame))
            {
                return;
            }

            // XEP-0352: <active/> und <inactive/>.
            if (frame.Contains(ClientStateIndication.Namespace, StringComparison.Ordinal) &&
                await HandleClientStateAsync(session, frame))
            {
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
            if (StanzaElement.Is(frame, "close"))
            {
                session.EndResumption();
                return;
            }

            // Ein Rahmen ohne Element ist kein unbekanntes Element, sondern gar
            // keines. Der Abschnitt darunter spricht von einem „first-level
            // child"; ein leerer Rahmen ist kein Kind, das nicht unterstützt
            // wird, sondern kein Kind. In D26 fiel er noch mit unter den
            // Fehler - eine Zeile zu weit.
            if (StanzaElement.NameOf(frame) is null)
                return;

            // RFC 6120, Abschnitt 4.9.3.24: „The initiating entity has sent a
            // first-level child of the stream that is not supported by the
            // server, either because the receiving entity does not understand
            // the namespace or because the receiving entity does not understand
            // the element name."
            //
            // Bis hierher fiel so ein Rahmen stillschweigend hinten heraus. Das
            // war die bequeme Antwort und die schlechtere: Wer etwas schickt,
            // das dieser Server nicht kennt, wartet sonst auf eine Antwort, die
            // nie kommt, und erfährt nie, warum. Ein Stream-Fehler beendet den
            // Stream (Abschnitt 4.9.1.1) - und das ist hier die Aussage: Über
            // diesen Stream sind wir uns nicht mehr einig.
            //
            // Er trifft auch das, was ein anderer Server beantworten würde und
            // dieser nicht - etwa ein <abort/> aus der SASL-Aushandlung
            // (Abschnitt 6.4.4). Auch dort ist die Bedingung wörtlich erfüllt:
            // „not supported by the server". Sie steht unter „Später".
            await session.SendStreamErrorAsync("unsupported-stanza-type");

        }

        /// <summary>
        /// XEP-0198: <c>&lt;enable/&gt;</c>, <c>&lt;r/&gt;</c> und <c>&lt;a/&gt;</c>.
        /// </summary>
        /// <returns>
        /// false, wenn das Element in diesem Namensraum nicht vorgesehen ist -
        /// dann behandelt es der Aufrufer wie jedes andere unbekannte.
        /// </returns>
        private async Task<Boolean> HandleStreamManagementAsync(XMPPSession session, String frame)
        {

            if (StanzaElement.Is(frame, "enable"))
            {

                if (!OfferStreamManagement)
                {
                    await session.SendAsync(
                        "<failed xmlns='urn:xmpp:sm:3'>" +
                        "<feature-not-implemented xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></failed>");
                    return true;
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

                return true;

            }

            // XEP-0198, Abschnitt 5: der Client will an einen früheren Stream
            // anknüpfen. Das kommt vor dem Resource Binding - eine gebundene
            // Resource gibt es hier noch nicht, sie wird gerade übernommen.
            if (StanzaElement.Is(frame, "resume"))
            {
                await HandleResumeAsync(session, frame);
                return true;
            }

            // Der Client fragt unseren Empfangszähler ab.
            //
            // Am vollständigen Namen und nicht am Anfangsbuchstaben: Ein
            // StartsWith("<r") traf jedes Element, das mit r beginnt, und ein
            // StartsWith("<a") jedes mit a. Die Reihenfolge der Zweige hielt
            // das bisher zusammen - <resume/> vor <r/>, <auth/> weit oben in
            // der Weiche davor. Eine Ordnung, die trägt, solange niemand sie
            // umstellt, ist keine Prüfung, sondern eine Verabredung.
            if (StanzaElement.Is(frame, "r"))
            {

                if (AnswerAckRequests)
                    await session.SendAsync(
                        $"<a xmlns='urn:xmpp:sm:3' h='{session.StanzasReceivedFromClient}'/>");

                return true;

            }

            // Der Client meldet seinen Empfangszähler.
            if (StanzaElement.Is(frame, "a"))
            {

                var h = Regex.Match(frame, @"h=['""](\d+)['""]");

                if (h.Success && UInt32.TryParse(h.Groups[1].Value, out var value))
                    session.AcknowledgeToClient(value);

                return true;

            }

            // Alles andere in diesem Namensraum kennt dieser Server nicht -
            // auch <enabled/>, <resumed/> und <failed/>, die es zwar gibt, die
            // aber der Server an den Client schickt und nicht umgekehrt.
            // Bekannt heisst nicht "bekannt in dieser Richtung".
            return false;

        }

        /// <summary>
        /// XEP-0352: <c>&lt;active/&gt;</c> und <c>&lt;inactive/&gt;</c> - der
        /// Client sagt, ob ein Mensch hinsieht.
        /// </summary>
        /// <returns>
        /// false, wenn das Element in diesem Namensraum nicht vorgesehen ist
        /// oder der Server die Erweiterung gar nicht angeboten hat - dann
        /// behandelt es der Aufrufer wie jedes andere unbekannte.
        /// </returns>
        /// <remarks>
        /// Ohne Anmeldung nicht: Die Ankündigung steht in den Features nach
        /// dem SASL-Austausch (Abschnitt 4.1), und was noch nicht angekündigt
        /// war, gilt auch noch nicht. Sonst hätte ein Unangemeldeter einen
        /// Zustand an einer Sitzung, die noch niemandem gehört.
        ///
        /// Geantwortet wird nicht - Abschnitt 4.2: „There is no reply from the
        /// server to either of these elements." Ein <c>&lt;active/&gt;</c>,
        /// das eine Bestätigung nach sich zöge, weckte das Gerät genau in dem
        /// Augenblick, in dem es sich schlafen legt.
        /// </remarks>
        private async Task<Boolean> HandleClientStateAsync(XMPPSession session, String frame)
        {

            if (!OfferClientStateIndication || session.Account is null)
                return false;

            if (StanzaElement.Is(frame, "active"))
            {
                await session.SetClientStateAsync(true);
                return true;
            }

            if (StanzaElement.Is(frame, "inactive"))
            {
                await session.SetClientStateAsync(false);
                return true;
            }

            return false;

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

            // Wie weit der alte Stream gekommen war - bekannt nur, solange er
            // noch daliegt, und nennbar nur seinem eigenen Konto.
            UInt32? verarbeitet = null;

            if (previd.Success)
                lock (_lock)
                    if (_resumable.TryGetValue(previd.Groups[1].Value, out var gefunden) &&
                        session.Account is not null &&
                        String.Equals(gefunden.Session.BareJid, session.BareJid,
                                      StringComparison.OrdinalIgnoreCase))
                    {

                        verarbeitet = gefunden.Session.StanzasReceivedFromClient;

                        if (gefunden.Deadline > DateTimeOffset.UtcNow)
                        {
                            geparkt = gefunden;
                            _resumable.Remove(previd.Groups[1].Value);
                        }

                    }

            if (geparkt is null)
            {

                // XEP-0198, Abschnitt 5: das h ist freiwillig („MAY also
                // include") und meint eine Messung - wie viel der Server vom
                // alten Stream verarbeitet hatte. Hier stand einmal fest
                // h='0', und das war keine Auskunft, sondern eine Behauptung:
                // „von allem, was du geschickt hast, ist nichts angekommen".
                // Wer sie glaubt und daraufhin nachsendet, stellt alles ein
                // zweites Mal zu.
                //
                // Weggelassen wird es in beiden Fällen, in denen der Server
                // nichts zu sagen hat: Er kennt die Kennung nicht - der
                // Normalfall nach einem Neustart oder nach dem Abräumer -,
                // oder sie gehört einem anderen Konto. Im zweiten Fall verriete
                // die Zahl, dass es diesen Stream gibt und wie viel über ihn
                // gelaufen ist; aus einem geratenen Versuch würde eine Sonde.
                await session.SendAsync(
                    $"<failed xmlns='urn:xmpp:sm:3'{(verarbeitet is null ? "" : $" h='{verarbeitet}'")}>" +
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

            if (!AnswerStreamOpen)
                return;

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

                    // XEP-0352, Abschnitt 4.1: „If the server supports CSI, it
                    // advertises it in the stream features after the client
                    // has authenticated." Deshalb nur hier und nicht in den
                    // ersten Features - vor der Anmeldung gibt es niemanden,
                    // dessen Zustand zu schonen wäre.
                    (OfferClientStateIndication
                         ? ClientStateIndication.FeatureXml
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
                                               user => GetAccount($"{user}@{Domain}"),
                                               user => XMPPCredentials.Decoy(user, _decoySecret));

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
        /// RFC 6120, Abschnitt 6.4.4: Der Client bricht die SASL-Aushandlung
        /// ab.
        /// </summary>
        /// <remarks>
        /// Der Abbruch ist ein <b>vorgesehener</b> Schritt und kein
        /// Protokollverstoss - deshalb ein SASL-Fehlschlag und kein
        /// Stream-Fehler, und deshalb bleibt der Stream stehen. Seit D26 endete
        /// er hier mit <c>&lt;unsupported-stanza-type/&gt;</c>: wörtlich nicht
        /// falsch, denn der Server unterstützte das Element nicht, aber die
        /// schlechtere von zwei Antworten. Sie zwang den Client zu einer neuen
        /// Verbindung für etwas, das der RFC innerhalb der bestehenden vorsieht.
        ///
        /// Der halbe Austausch wird verworfen, und das ist der eigentliche
        /// Inhalt eines Abbruchs. Bliebe er liegen, liesse er sich mit einer
        /// später nachgeschobenen <c>&lt;response/&gt;</c> noch zu Ende führen -
        /// der Abbruch wäre dann eine Höflichkeitsfloskel und keine Aussage.
        ///
        /// Beantwortet wird er in jedem Zustand, auch nach abgeschlossener
        /// Anmeldung. Abschnitt 6.4.4 knüpft die Antwort an keine Bedingung,
        /// und ein Abbruch ohne laufenden Austausch bricht eben nichts ab.
        /// </remarks>
        private static async Task HandleSaslAbortAsync(XMPPSession session)
        {

            session.Scram = null;

            await session.SendAsync(
                "<failure xmlns='urn:ietf:params:xml:ns:xmpp-sasl'><aborted/></failure>");

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

            // RFC 6120, Abschnitt 8.2.3, Regel 2: Ohne einen der vier
            // vorgesehenen Werte ist diese Stanza weder Frage noch Antwort.
            //
            // Vor der Weiche und nicht dahinter: Was an den Server selbst geht,
            // kommt am Zustellweg nie vorbei und fiele sonst hinten heraus.
            if (!IqTypes.IsKnown(type))
            {
                await session.SendAsync(BadRequestIq(id));
                return;
            }

            // XEP-0163: PEP beantwortet der Server für das Konto - und zwar
            // VOR der Weiterleitung.
            //
            // Das ist der Kern der Sache und leicht falsch zu machen: Eine
            // Anfrage an bob@domain sieht aus wie eine Anfrage an Bob und
            // ginge unten an seine Sitzung. Dann wäre ein Bundle nur abrufbar,
            // solange Bob online ist - und genau dafür gibt es PEP nicht. Der
            // Server antwortet stellvertretend für einen Menschen, der gerade
            // nicht da ist.
            if (OfferPersonalEventing &&
                frame.Contains(OmemoPep.PubSubNamespace, StringComparison.Ordinal) &&
                await HandlePepAsync(session, frame, id, type, to))
            {
                return;
            }

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

            // Ab hier beantwortet der Server für sich selbst - und dafür
            // braucht es diese Sitzung nicht mehr, nur noch einen Rückweg.
            if (AnswerAboutSelf(frame, id, type) is { } antwort)
                await session.SendAsync(antwort);

        }

        /// <summary>
        /// Der Namensraum der PubSub-eigenen Fehlerzustände (XEP-0060,
        /// Abschnitt 6.1.3).
        /// </summary>
        private const String PubSubErrorNamespace = "http://jabber.org/protocol/pubsub#errors";

        /// <summary>
        /// Der Namensraum der Eigentümer-Anfragen (XEP-0060, Abschnitt 8).
        /// </summary>
        /// <remarks>
        /// Ein eigener Namensraum und kein eigenes Element: Wer einen Knoten
        /// einstellt, tut etwas anderes als wer ihn benutzt, und das XEP
        /// trennt beides schon an der Adresse.
        /// </remarks>
        private const String PubSubOwnerNamespace = "http://jabber.org/protocol/pubsub#owner";

        private const String DataFormNamespace = DataForm.Namespace;

        /// <summary>
        /// XEP-0163 Personal Eventing: Veröffentlichen, Abrufen, Abonnieren und
        /// Abbestellen der PEP-Knoten eines Kontos.
        /// </summary>
        /// <returns>
        /// false, wenn dieses IQ nichts mit PEP zu tun hat - dann nimmt es der
        /// Aufrufer wie jedes andere.
        /// </returns>
        /// <remarks>
        /// <b>Eine Teilmenge, und das gehört gesagt.</b> Es gibt keine
        /// Knotenkonfiguration, keine Zugriffsmodelle und keine gefilterten
        /// Benachrichtigungen über XEP-0115. Der Knoten ist offen, wer fragt,
        /// bekommt - für einen Testserver ist das die richtige Menge, für einen
        /// echten wäre es zu wenig: Dort entscheidet das Zugriffsmodell, wer
        /// ein Bundle sehen darf, und über die Merkmalsankündigung, wer eine
        /// Benachrichtigung überhaupt will.
        ///
        /// Benachrichtigt werden die, die auch Presence bekommen - Kontakte mit
        /// <c>from</c> oder <c>both</c> und die eigenen weiteren Resourcen -,
        /// und dazu die ausdrücklichen Abonnenten. Ein Abonnement je Knoten und
        /// JID; mehrere gleichzeitige, für die es die <c>subid</c> eigentlich
        /// gibt, kennt dieser Server nicht.
        /// </remarks>
        private async Task<Boolean> HandlePepAsync(XMPPSession  session,
                                                   String       frame,
                                                   String?      id,
                                                   String?      type,
                                                   String?      to)
        {

            XElement iq;

            try
            {
                iq = XElement.Parse(frame);
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }

            var pubsub = iq.Child(OmemoPep.PubSubNamespace, "pubsub");
            var owner  = iq.Child(PubSubOwnerNamespace,     "pubsub");

            if (session.Account is null || (pubsub is null && owner is null))
                return false;

            // Der Testschalter: angenommen und nicht beantwortet. Bewusst
            // hier und nicht vor der Erkennung - was kein PEP ist, soll auch
            // dann seinen gewohnten Weg gehen.
            if (!AnswerPepRequests)
                return true;

            #region Eigentümer-Anweisungen (XEP-0060, Abschnitt 8)

            if (owner is not null)
            {

                if (type is not ("get" or "set"))
                    return false;

                // Drei Anweisungen und ein gemeinsamer Vorspann: Wem gehört der
                // Knoten, und gibt es ihn überhaupt.
                //
                // Er stand einmal bei jeder einzeln - dieselbe Entscheidung an
                // mehreren Stellen, und jede hätte die anderen still überholen
                // können. Wer eine davon lockert, lockert sie hier für alle
                // sichtbar oder gar nicht.
                var anweisung = owner.Child(PubSubOwnerNamespace, "affiliations")  ??
                                owner.Child(PubSubOwnerNamespace, "subscriptions") ??
                                owner.Child(PubSubOwnerNamespace, "delete")        ??
                                owner.Child(PubSubOwnerNamespace, "purge")         ??
                                owner.Child(PubSubOwnerNamespace, "configure");

                if (anweisung is null)
                    return false;

                // Ein PEP-Knoten gehört einem Menschen, und über ihn bestimmt
                // nur der. Fremde Knoten sind hier nicht bloss unzugänglich -
                // wer sie einstellen könnte, könnte etwa die Ablage abschalten
                // und damit fremde Bundles unerreichbar machen, ohne dass
                // irgendetwas nach einem Fehler aussieht.
                if (to is not null &&
                    !String.Equals(BareOf(to), session.BareJid, StringComparison.OrdinalIgnoreCase))
                {
                    await session.SendAsync(StanzaErrorIq(id, "forbidden", "auth"));
                    return true;
                }

                var node = anweisung.Attr("node");

                if (String.IsNullOrEmpty(node))
                {
                    await session.SendAsync(BadRequestIq(id));
                    return true;
                }

                var bestand = session.Account.PepNodeConfiguration(node);

                if (bestand is null)
                {
                    await session.SendAsync(StanzaErrorIq(id, "item-not-found"));
                    return true;
                }

                #region Rollen verwalten (XEP-0060, Abschnitt 8.9)

                if (anweisung.Name.LocalName == "affiliations")
                {

                    if (type == "get")
                    {

                        await session.SendAsync(
                            $"<iq type='result' id='{id}'>" +
                            $"<pubsub xmlns='{PubSubOwnerNamespace}'>" +
                            $"<affiliations node='{XmlEscaping.Escape(node)}'>" +
                            String.Concat(session.Account.PepAffiliations(node).Select(r =>
                                $"<affiliation jid='{XmlEscaping.Escape(r.Jid)}'" +
                                $" affiliation='{PubSubAffiliations.NameOf(r.Affiliation)}'/>")) +
                            "</affiliations></pubsub></iq>");

                        return true;

                    }

                    foreach (var eintrag in anweisung.Children(PubSubOwnerNamespace, "affiliation"))
                    {

                        // Erst alles prüfen, dann alles ausführen: Eine
                        // Anfrage, die zur Hälfte gilt, wäre schlimmer als
                        // eine, die ganz abgewiesen wird - der Absender
                        // wüsste nicht, welche Hälfte.
                        if (eintrag.Attr("jid") is not String wer ||
                            !PubSubAffiliations.TryRead(eintrag.Attr("affiliation"), out var rolle))
                        {
                            await session.SendAsync(BadRequestIq(id));
                            return true;
                        }

                        // XEP-0060, Abschnitt 8.9.2: Der Eigentümer ist das
                        // Konto. Wer ihn umtragen könnte, könnte einem anderen
                        // sein eigenes Konto wegnehmen.
                        if (String.Equals(BareOf(wer), session.BareJid, StringComparison.OrdinalIgnoreCase) ||
                            rolle == PubSubAffiliation.Owner)
                        {
                            await session.SendAsync(StanzaErrorIq(id, "not-allowed", "cancel"));
                            return true;
                        }

                    }

                    var erloschen = new List<PepSubscription>();

                    foreach (var eintrag in anweisung.Children(PubSubOwnerNamespace, "affiliation"))
                    {

                        PubSubAffiliations.TryRead(eintrag.Attr("affiliation"), out var rolle);

                        session.Account.SetPepAffiliation(node,
                                                          BareOf(eintrag.Attr("jid")!),
                                                          rolle,
                                                          out var mitgegangen);

                        erloschen.AddRange(mitgegangen);

                    }

                    await session.SendAsync($"<iq type='result' id='{id}'/>");

                    // Ein Ausschluss beendet Abonnements (Abschnitt 8.9.4), und
                    // auch davon erfährt der Betroffene. Der Ausschluss selbst
                    // bleibt ihm verborgen: Was er an diesem Knoten ist, geht
                    // ihn nichts an - dass er ihn nicht mehr bekommt, schon.
                    foreach (var eines in erloschen)
                        await NotifySubscriptionEndedAsync(session.Account, node, eines);

                    return true;

                }

                #endregion

                #region Abonnenten verwalten (XEP-0060, Abschnitt 8.8)

                if (anweisung.Name.LocalName == "subscriptions")
                {

                    var abonnenten = session.Account.PepSubscriptions(node);

                    // XEP-0060, Abschnitt 8.8.1: Wer an diesem Knoten hängt.
                    //
                    // <b>Das Gegenteil von Abschnitt 5.6, und mit Absicht.</b>
                    // Dort werden fremde Abonnements verschwiegen, weil sie
                    // eine Auskunft über Menschen wären - wer sich wofür
                    // interessiert, über alle Knoten hinweg. Hier ist die Frage
                    // eine andere: nicht „wo ist dieser Mensch überall", sondern
                    // „wer hängt an meinem Knoten". Diese Liste ist eine
                    // Auskunft über den Knoten, und der Eigentümer ist der, von
                    // dem die Empfänger ihre Daten bekommen. Ihm die Empfänger
                    // vorzuenthalten hiesse, ihn für eine Verteilung
                    // verantwortlich zu machen, die er nicht sehen darf.
                    if (type == "get")
                    {

                        // Der Zustand steht fest da: Dieser Server kennt keine
                        // Genehmigung, also ist jedes eingetragene Abonnement
                        // ein abonniertes. Käme `authorize` dazu, wäre dies
                        // eine der Stellen, die einen echten Zustand brauchen.
                        await session.SendAsync(
                            $"<iq type='result' id='{id}'>" +
                            $"<pubsub xmlns='{PubSubOwnerNamespace}'>" +
                            $"<subscriptions node='{XmlEscaping.Escape(node)}'>" +
                            String.Concat(abonnenten.Select(a =>
                                $"<subscription jid='{XmlEscaping.Escape(a.Jid)}'" +
                                $" subid='{a.SubId}'" +
                                " subscription='subscribed'/>")) +
                            "</subscriptions></pubsub></iq>");

                        return true;

                    }

                    // Erst alles prüfen, dann alles ausführen - wie bei den
                    // Rollen und aus demselben Grund.
                    foreach (var eintrag in anweisung.Children(PubSubOwnerNamespace, "subscription"))
                    {

                        if (eintrag.Attr("jid") is not String wer ||
                            !PubSubSubscription.TryReadState(eintrag.Attr("subscription"), out var zustand))
                        {
                            await session.SendAsync(BadRequestIq(id));
                            return true;
                        }

                        var gemeint = eintrag.Attr("subid");

                        var vorhanden = abonnenten.Any(
                                            a => String.Equals(a.Jid, BareOf(wer), StringComparison.OrdinalIgnoreCase) &&
                                                 (gemeint is null || String.Equals(a.SubId, gemeint, StringComparison.Ordinal)));

                        // XEP-0060, Abschnitt 8.8.2 lässt den Eigentümer auch
                        // anmelden. <b>Dieser Server nur abmelden.</b> Jemanden
                        // einzutragen, der nicht gefragt hat, ist genau das,
                        // was Abschnitt 6.1.3.1 auf der anderen Seite
                        // verhindert; dass es der eigene Knoten ist, ändert
                        // nichts für den, dessen Postfach sich füllt. Ohne
                        // Genehmigungsverfahren gäbe es dazu auch nichts, was
                        // vorher eine Frage gewesen wäre.
                        if (zustand != PubSubSubscriptionState.None)
                        {

                            // Den bestehenden Zustand noch einmal zu nennen ist
                            // keine Anweisung, sondern eine Bestätigung. Eine
                            // Liste, die sich nicht unverändert zurückschicken
                            // lässt, wäre kein Zustand, sondern ein Formular.
                            if (zustand == PubSubSubscriptionState.Subscribed && vorhanden)
                                continue;

                            await session.SendAsync(StanzaErrorIq(id, "not-allowed", "cancel"));
                            return true;

                        }

                        // Was niemand findet, wird auch nicht beendet.
                        // Stillschweigend zuzustimmen hiesse, den Erfolg einer
                        // Anweisung zu melden, die ins Leere ging - ein
                        // Tippfehler im JID, und der Eigentümer hielte jemanden
                        // für entfernt, der weiter alles bekommt.
                        if (!vorhanden)
                        {
                            await session.SendAsync(StanzaErrorIq(id, "item-not-found"));
                            return true;
                        }

                    }

                    var beendet = new List<PepSubscription>();

                    foreach (var eintrag in anweisung.Children(PubSubOwnerNamespace, "subscription"))
                    {
                        PubSubSubscription.TryReadState(eintrag.Attr("subscription"), out var zustand);
                        if (zustand == PubSubSubscriptionState.None)
                            beendet.AddRange(
                                session.Account.RemovePepSubscriptions(node,
                                                                       BareOf(eintrag.Attr("jid")!),
                                                                       eintrag.Attr("subid")));
                    }

                    await session.SendAsync($"<iq type='result' id='{id}'/>");

                    // Erst die Antwort, dann die Meldungen - und nur über das,
                    // was wirklich erloschen ist. Eine Meldung über ein
                    // Abonnement, das der Server gar nicht gefunden hat, wäre
                    // dieselbe Behauptung ins Blaue wie ein `result` darauf.
                    foreach (var eines in beendet)
                        await NotifySubscriptionEndedAsync(session.Account, node, eines);

                    return true;

                }

                #endregion

                #region Knoten löschen und leeren (XEP-0060, Abschnitte 8.4 und 8.5)

                if (anweisung.Name.LocalName is "delete" or "purge")
                {

                    // Beides verändert etwas. Ein `get` darauf ist keine Frage,
                    // die sich beantworten liesse - und dürfte auf keinen Fall
                    // beim Einstellen weiter unten landen, wo es die
                    // Knotenkonfiguration zurückbekäme.
                    if (type != "set")
                    {
                        await session.SendAsync(BadRequestIq(id));
                        return true;
                    }

                    if (anweisung.Name.LocalName == "delete")
                    {

                        var erloschen = session.Account.DeletePepNode(node)!;

                        await session.SendAsync($"<iq type='result' id='{id}'/>");

                        // XEP-0060, Abschnitt 8.4.2. <b>Eine Meldung je
                        // Abonnenten und nicht je Abonnement</b>, und ohne
                        // Kennung: Es endet nicht ein Abonnement, sondern der
                        // Knoten. Eine Kennung zu nennen hiesse, die anderen
                        // bestünden weiter.
                        //
                        // Eine zweite Meldung nach Abschnitt 8.8.4 gibt es
                        // dazu nicht - dass ein Abonnement auf einen Knoten,
                        // den es nicht mehr gibt, erloschen ist, sagt diese
                        // Meldung bereits.
                        await NotifyPepNodeAsync(session.Account, session,
                                                 $"<delete node='{XmlEscaping.Escape(node)}'/>",
                                                 erloschen.Select(a => a.Jid));

                        return true;

                    }

                    // XEP-0060, Abschnitt 8.5.3.2: Was nichts aufbewahrt, kann
                    // nichts hergeben. Ein `result` darauf wäre die Auskunft,
                    // es sei etwas geleert worden, und die Meldung an die
                    // Abonnenten die Aufforderung, etwas wegzuwerfen, das
                    // dieser Knoten nie ausgeliefert hat.
                    if (!bestand.PersistItems)
                    {
                        await session.SendAsync(
                            StanzaErrorIq(id, "feature-not-implemented", "cancel",
                                          applicationError: $"<unsupported xmlns='{PubSubErrorNamespace}'" +
                                                            " feature='persistent-items'/>"));
                        return true;
                    }

                    var abonnenten = session.Account.PepSubscriptions(node).Select(a => a.Jid);

                    session.Account.PurgePepNode(node);

                    await session.SendAsync($"<iq type='result' id='{id}'/>");

                    // XEP-0060, Abschnitt 8.5.2. Der Knoten bleibt, die
                    // Abonnements bleiben - die Meldung sagt nur, dass nichts
                    // mehr abzuholen ist.
                    await NotifyPepNodeAsync(session.Account, session,
                                             $"<purge node='{XmlEscaping.Escape(node)}'/>",
                                             abonnenten);

                    return true;

                }

                #endregion

                #region Knoten einstellen (XEP-0060, Abschnitt 8.2)

                if (type == "get")
                {

                    await session.SendAsync(
                        $"<iq type='result' id='{id}'>" +
                        $"<pubsub xmlns='{PubSubOwnerNamespace}'>" +
                        $"<configure node='{XmlEscaping.Escape(node)}'>" +
                        bestand.ToForm().ToString(SaveOptions.DisableFormatting) +
                        "</configure></pubsub></iq>");

                    return true;

                }

                var formular = anweisung.Child(DataFormNamespace, "x");

                if (formular is null ||
                    !PubSubNodeConfiguration.TryRead(formular, bestand, out var eingestellt))
                {
                    await session.SendAsync(BadRequestIq(id));
                    return true;
                }

                session.Account.ConfigurePepNode(node, eingestellt!);

                await session.SendAsync($"<iq type='result' id='{id}'/>");

                return true;

                #endregion

            }

            #endregion

            if (pubsub is null)
                return false;

            #region Anlegen

            if (type == "set" && pubsub.Child(OmemoPep.PubSubNamespace, "create") is { } create)
            {

                if (to is not null &&
                    !String.Equals(BareOf(to), session.BareJid, StringComparison.OrdinalIgnoreCase))
                {
                    await session.SendAsync(StanzaErrorIq(id, "forbidden", "auth"));
                    return true;
                }

                var node = create.Attr("node");

                // XEP-0060, Abschnitt 8.1.2 kennt Knoten ohne Namen, die der
                // Dienst benennt. Hier nicht: Ein PEP-Knoten wird über seinen
                // Namen gefunden, und einen erfundenen kennt niemand ausser
                // dem, der ihn gerade bekommen hat.
                if (String.IsNullOrEmpty(node))
                {
                    await session.SendAsync(BadRequestIq(id));
                    return true;
                }

                var wunsch = PubSubNodeConfiguration.Default;

                if (pubsub.Child(OmemoPep.PubSubNamespace, "configure")?.Child(DataFormNamespace, "x") is { } mitgegeben &&
                    !PubSubNodeConfiguration.TryRead(mitgegeben, wunsch, out wunsch))
                {
                    await session.SendAsync(BadRequestIq(id));
                    return true;
                }

                // XEP-0060, Abschnitt 8.1.3: Was es gibt, wird nicht noch
                // einmal angelegt. Stillschweigend gelten zu lassen hiesse,
                // eine bestehende Einstellung durch eine neue zu ersetzen,
                // ohne dass jemand danach gefragt hat.
                if (!session.Account.CreatePepNode(node, wunsch))
                {
                    await session.SendAsync(StanzaErrorIq(id, "conflict"));
                    return true;
                }

                await session.SendAsync(
                    $"<iq type='result' id='{id}'>" +
                    $"<pubsub xmlns='{OmemoPep.PubSubNamespace}'>" +
                    $"<create node='{XmlEscaping.Escape(node)}'/>" +
                    "</pubsub></iq>");

                return true;

            }

            #endregion

            #region Veröffentlichen

            if (type == "set" && pubsub.Child(OmemoPep.PubSubNamespace, "publish") is { } publish)
            {

                var node = publish.Attr("node");
                var item = publish.Elements().FirstOrDefault(e => e.Name.LocalName == "item");

                if (String.IsNullOrEmpty(node) || item is null)
                {
                    await session.SendAsync(BadRequestIq(id));
                    return true;
                }

                // Der Knoten gehört dem Konto und nicht dem, der schreibt.
                var besitzer = to is null ||
                               String.Equals(BareOf(to), session.BareJid, StringComparison.OrdinalIgnoreCase)
                                   ? session.Account
                                   : GetAccount(BareOf(to));

                // Schreiben darf, wer den Knoten besitzt oder wem der Besitzer
                // es erlaubt hat - eine Regel für beide Fälle.
                //
                // Ohne sie könnte jeder fremde Bundles austauschen; das wäre
                // der Angriff, gegen den die Signatur über den Signed PreKey
                // steht. Mit ihr ist es eine Rolle, die der Eigentümer vergeben
                // hat und jederzeit wieder nimmt.
                //
                // Dass ein Publizierender in einem fremden Konto keinen Knoten
                // anlegen kann, folgt daraus von selbst: Eine Rolle gehört
                // einem Knoten, und an einem, den es nicht gibt, hat niemand
                // eine.
                if (besitzer?.PepAffiliationOf(node, session.BareJid!)
                        is not (PubSubAffiliation.Owner or PubSubAffiliation.Publisher))
                {
                    await session.SendAsync(StanzaErrorIq(id, "forbidden", "auth"));
                    return true;
                }

                // XEP-0060, Abschnitt 7.1.5: Bedingungen an den Knoten.
                //
                // OMEMO schickt sie seit D66 mit - und bis K8 hat sie niemand
                // gelesen. Das war die stillste Art, eine Zusage zu geben: Der
                // Client verlangte einen offenen Knoten, bekam ein 'result'
                // und durfte annehmen, sein Bundle sei abrufbar.
                if (pubsub.Child(OmemoPep.PubSubNamespace, "publish-options")?.Child(DataFormNamespace, "x") is { } bedingungen)
                {

                    if (!PubSubPublishOptions.TryRead(bedingungen, out var verlangt))
                    {
                        await session.SendAsync(BadRequestIq(id));
                        return true;
                    }

                    var bestand = besitzer.PepNodeConfiguration(node);

                    if (bestand is null)
                        besitzer.CreatePepNode(node, verlangt!.ApplyTo(PubSubNodeConfiguration.Default));

                    else if (!verlangt!.AreMetBy(bestand))
                    {
                        await session.SendAsync(
                            StanzaErrorIq(id, "conflict", "cancel",
                                          applicationError: $"<precondition-not-met xmlns='{PubSubErrorNamespace}'/>"));
                        return true;
                    }

                }

                var itemId  = item.Attr("id") ?? Guid.NewGuid().ToString("N")[..8];
                var inhalt  = item.Elements().FirstOrDefault()?.ToString(SaveOptions.DisableFormatting) ?? "";

                besitzer.PublishPepItem(node, itemId, inhalt);

                await session.SendAsync(
                    $"<iq type='result' id='{id}'>" +
                    $"<pubsub xmlns='{OmemoPep.PubSubNamespace}'>" +
                    $"<publish node='{XmlEscaping.Escape(node)}'>" +
                    $"<item id='{XmlEscaping.Escape(itemId)}'/>" +
                    "</publish></pubsub></iq>");

                await NotifyPepAsync(besitzer, session, node,
                                     $"<item id='{XmlEscaping.Escape(itemId)}'>{inhalt}</item>");

                return true;

            }

            #endregion

            #region Zurücknehmen (XEP-0060, Abschnitt 7.2)

            if (type == "set" && pubsub.Child(OmemoPep.PubSubNamespace, "retract") is { } retract)
            {

                var node    = retract.Attr("node");
                var eintrag = retract.Child(OmemoPep.PubSubNamespace, "item")?.Attr("id");

                // Ohne Kennung ist nicht zu sagen, was zurückgenommen werden
                // soll. XEP-0060 kennt kein „nimm irgendetwas zurück" - dafür
                // gibt es das Leeren, und das ist eine andere Anweisung mit
                // einer anderen Meldung.
                if (String.IsNullOrEmpty(node) || String.IsNullOrEmpty(eintrag))
                {
                    await session.SendAsync(BadRequestIq(id));
                    return true;
                }

                var besitzer = to is null ||
                               String.Equals(BareOf(to), session.BareJid, StringComparison.OrdinalIgnoreCase)
                                   ? session.Account
                                   : GetAccount(BareOf(to));

                // Dieselbe Regel wie beim Veröffentlichen, und das ist die
                // Entscheidung: <b>Wer schreiben darf, darf auch zurücknehmen.</b>
                //
                // Ein Publizierender käme so an fremde Einträge im selben
                // Knoten. Sie auseinanderzuhalten hiesse, sich zu merken, wer
                // welchen geschrieben hat - eine Ablage, die es hier nicht
                // gibt, und ohne die jede feinere Regel bloss behauptet wäre.
                if (besitzer?.PepAffiliationOf(node, session.BareJid!)
                        is not (PubSubAffiliation.Owner or PubSubAffiliation.Publisher))
                {
                    await session.SendAsync(StanzaErrorIq(id, "forbidden", "auth"));
                    return true;
                }

                // XEP-0060, Abschnitt 7.2.3.3, wie beim Leeren: Was nichts
                // aufbewahrt, kann nichts zurücknehmen.
                if (besitzer.PepNodeConfiguration(node) is { PersistItems: false })
                {
                    await session.SendAsync(
                        StanzaErrorIq(id, "feature-not-implemented", "cancel",
                                      applicationError: $"<unsupported xmlns='{PubSubErrorNamespace}'" +
                                                        " feature='persistent-items'/>"));
                    return true;
                }

                // XEP-0060, Abschnitt 7.2.3.2. Ein `result` auf einen Eintrag,
                // den es nicht gibt, wäre die Auskunft, er sei jetzt fort - und
                // die Meldung an die Abonnenten die Aufforderung, etwas
                // wegzuwerfen, das sie nie bekommen haben.
                if (!besitzer.RetractPepItem(node, eintrag))
                {
                    await session.SendAsync(StanzaErrorIq(id, "item-not-found"));
                    return true;
                }

                await session.SendAsync($"<iq type='result' id='{id}'/>");

                // XEP-0060, Abschnitt 7.2.2.1: dieselbe Zustellung wie eine
                // Veröffentlichung, nur mit anderem Inhalt. Wer den Eintrag
                // bekommen hat, hält ihn sonst weiter für gültig.
                await NotifyPepAsync(besitzer, session, node,
                                     $"<retract id='{XmlEscaping.Escape(eintrag)}'/>");

                return true;

            }

            #endregion

            #region Abrufen

            if (type == "get" && pubsub.Child(OmemoPep.PubSubNamespace, "items") is { } items)
            {

                var node = items.Attr("node");

                if (String.IsNullOrEmpty(node))
                {
                    await session.SendAsync(BadRequestIq(id));
                    return true;
                }

                var konto = to is null
                                ? session.Account
                                : GetAccount(BareOf(to));

                // Ein Konto, das es nicht gibt, wird nicht von einem
                // unterschieden, das nichts veröffentlicht hat: Sonst liesse
                // sich über PEP herausfinden, welche Konten es auf diesem
                // Server gibt - dieselbe Überlegung wie bei der Anmeldung
                // (RFC 6120, Abschnitt 13.11, siehe D50).
                if (konto is not null &&
                    konto.PepNodeExists(node) &&
                    PepAccessErrorIq(id, konto, node, session.BareJid!) is { } abgewiesen)
                {
                    await session.SendAsync(abgewiesen);
                    return true;
                }

                var gesuchte  = items.Elements().FirstOrDefault(e => e.Name.LocalName == "item")?.Attr("id");
                var eintraege = konto?.GetPepItems(node, gesuchte) ?? [];

                if (eintraege.Count == 0)
                {
                    await session.SendAsync(StanzaErrorIq(id, "item-not-found", "cancel"));
                    return true;
                }

                await session.SendAsync(
                    $"<iq type='result' id='{id}'" +
                    (to is not null ? $" from='{XmlEscaping.Escape(BareOf(to)!)}'" : "") + ">" +
                    $"<pubsub xmlns='{OmemoPep.PubSubNamespace}'>" +
                    $"<items node='{XmlEscaping.Escape(node)}'>" +
                    String.Concat(eintraege.Select(e =>
                        $"<item id='{XmlEscaping.Escape(e.ItemId)}'>{e.Payload}</item>")) +
                    "</items></pubsub></iq>");

                return true;

            }

            #endregion

            #region Eigene Rollen aufzählen

            if (type == "get" && pubsub.Child(OmemoPep.PubSubNamespace, "affiliations") is not null)
            {

                var konto = to is null
                                ? session.Account
                                : GetAccount(BareOf(to));

                // Wie bei den Abonnements: die Rollen *des Fragenden*. Wer
                // fremde aufzählen dürfte, erführe, wer wo etwas darf.
                var meine = konto?.PepAffiliationsOf(session.BareJid!) ?? [];

                await session.SendAsync(
                    $"<iq type='result' id='{id}'" +
                    (to is not null ? $" from='{XmlEscaping.Escape(BareOf(to)!)}'" : "") + ">" +
                    $"<pubsub xmlns='{OmemoPep.PubSubNamespace}'><affiliations>" +
                    String.Concat(meine.Select(r =>
                        $"<affiliation node='{XmlEscaping.Escape(r.Node)}'" +
                        $" affiliation='{PubSubAffiliations.NameOf(r.Affiliation)}'/>")) +
                    "</affiliations></pubsub></iq>");

                return true;

            }

            #endregion

            #region Abonnements aufzählen

            if (type == "get" && pubsub.Child(OmemoPep.PubSubNamespace, "subscriptions") is { } aufzaehlung)
            {

                var konto = to is null
                                ? session.Account
                                : GetAccount(BareOf(to));

                var knoten = aufzaehlung.Attr("node");

                // XEP-0060, Abschnitt 5.6: die Abonnements *des Fragenden*.
                //
                // Nie die eines anderen, und das ist keine Auslegungsfrage:
                // Wer fremde aufzählen dürfte, erführe, wer sich wofür
                // interessiert - eine Auskunft über Menschen, nicht über
                // Knoten.
                var seine = konto?.PepSubscriptionsOf(session.BareJid!) ?? [];

                if (!String.IsNullOrEmpty(knoten))
                    seine = [.. seine.Where(e => String.Equals(e.Node, knoten, StringComparison.Ordinal))];

                // Keine Abonnements ist eine leere Liste und kein Fehler: Die
                // Frage war beantwortbar, die Antwort lautet „keine".
                await session.SendAsync(
                    $"<iq type='result' id='{id}'" +
                    (to is not null ? $" from='{XmlEscaping.Escape(BareOf(to)!)}'" : "") + ">" +
                    $"<pubsub xmlns='{OmemoPep.PubSubNamespace}'>" +
                    "<subscriptions" + (String.IsNullOrEmpty(knoten) ? "" : $" node='{XmlEscaping.Escape(knoten)}'") + ">" +
                    String.Concat(seine.Select(e =>
                        $"<subscription node='{XmlEscaping.Escape(e.Node)}'" +
                        $" jid='{XmlEscaping.Escape(e.Subscription.Jid)}'" +
                        $" subid='{e.Subscription.SubId}'" +
                        " subscription='subscribed'/>")) +
                    "</subscriptions></pubsub></iq>");

                return true;

            }

            #endregion

            #region Abonnieren

            if (type == "set" && pubsub.Child(OmemoPep.PubSubNamespace, "subscribe") is { } subscribe)
            {

                var node = subscribe.Attr("node");

                if (String.IsNullOrEmpty(node))
                {
                    await session.SendAsync(BadRequestIq(id));
                    return true;
                }

                // XEP-0060, Abschnitt 6.1.3.1: Der angegebene JID muss der des
                // Absenders sein.
                //
                // Ohne diese Prüfung könnte jeder jeden anmelden, und der
                // Angemeldete bekäme von da an Veröffentlichungen, die er nie
                // verlangt hat - von einem Knoten, dessen Namen er nicht kennt.
                // Abbestellen könnte er sie nur, wenn er darauf käme, wonach er
                // suchen muss.
                if (subscribe.Attr("jid") is not String wer ||
                    !String.Equals(BareOf(wer), session.BareJid, StringComparison.OrdinalIgnoreCase))
                {
                    await session.SendAsync(
                        StanzaErrorIq(id, "bad-request", "modify",
                                      applicationError: $"<invalid-jid xmlns='{PubSubErrorNamespace}'/>"));
                    return true;
                }

                var konto = to is null
                                ? session.Account
                                : GetAccount(BareOf(to));

                // XEP-0060, Abschnitt 6.1.3.12. Ein Konto, das es nicht gibt,
                // ist auch hier nicht von einem zu unterscheiden, das nichts
                // veröffentlicht hat - dieselbe Überlegung wie beim Abrufen.
                // Es gibt ihn, sobald er angelegt ist - nicht erst, sobald
                // etwas darin steht. Sonst wäre ein angelegter Knoten nicht zu
                // abonnieren und das Anlegen folgenlos.
                if (konto is null || !konto.PepNodeExists(node))
                {
                    await session.SendAsync(StanzaErrorIq(id, "item-not-found"));
                    return true;
                }

                // XEP-0060, Abschnitte 6.1.3.4 und 6.1.3.8
                if (PepAccessErrorIq(id, konto, node, session.BareJid!) is { } abgewiesen)
                {
                    await session.SendAsync(abgewiesen);
                    return true;
                }

                var subId = konto.AddPepSubscription(node, session.BareJid!);

                await session.SendAsync(
                    $"<iq type='result' id='{id}'" +
                    (to is not null ? $" from='{XmlEscaping.Escape(BareOf(to)!)}'" : "") + ">" +
                    $"<pubsub xmlns='{OmemoPep.PubSubNamespace}'>" +
                    $"<subscription node='{XmlEscaping.Escape(node)}'" +
                    $" jid='{XmlEscaping.Escape(session.BareJid!)}'" +
                    $" subid='{subId}' subscription='subscribed'/>" +
                    "</pubsub></iq>");

                return true;

            }

            #endregion

            #region Abbestellen

            if (type == "set" && pubsub.Child(OmemoPep.PubSubNamespace, "unsubscribe") is { } unsubscribe)
            {

                var node = unsubscribe.Attr("node");

                if (String.IsNullOrEmpty(node))
                {
                    await session.SendAsync(BadRequestIq(id));
                    return true;
                }

                if (unsubscribe.Attr("jid") is not String wer ||
                    !String.Equals(BareOf(wer), session.BareJid, StringComparison.OrdinalIgnoreCase))
                {
                    await session.SendAsync(
                        StanzaErrorIq(id, "bad-request", "modify",
                                      applicationError: $"<invalid-jid xmlns='{PubSubErrorNamespace}'/>"));
                    return true;
                }

                var konto = to is null
                                ? session.Account
                                : GetAccount(BareOf(to));

                var ergebnis = konto?.RemovePepSubscription(node,
                                                            session.BareJid!,
                                                            unsubscribe.Attr("subid"))
                                   ?? PepSubscriptionResult.NotSubscribed;

                await session.SendAsync(ergebnis switch {

                    // XEP-0060, Abschnitt 6.2.3.1: mehrere, und keines benannt.
                    // Hier ein bad-request, beim Einstellen ein not-acceptable
                    // - siehe PepSubscriptionResult.
                    PepSubscriptionResult.SubIdRequired
                        => StanzaErrorIq(id, "bad-request", "modify",
                                         applicationError: $"<subid-required xmlns='{PubSubErrorNamespace}'/>"),

                    PepSubscriptionResult.Ok
                        => $"<iq type='result' id='{id}'" +
                           (to is not null ? $" from='{XmlEscaping.Escape(BareOf(to)!)}'" : "") + "/>",

                    _   => SubscriptionErrorIq(id, ergebnis)

                });

                return true;

            }

            #endregion

            #region Einstellen

            if (pubsub.Child(OmemoPep.PubSubNamespace, "options") is { } optionen &&
                type is "get" or "set")
            {

                var node = optionen.Attr("node");

                if (String.IsNullOrEmpty(node))
                {
                    await session.SendAsync(BadRequestIq(id));
                    return true;
                }

                // Die dritte Stelle mit derselben Prüfung, und die stillste:
                // Wer fremde Abonnements einstellen dürfte, könnte sie lautlos
                // abschalten. Das Abonnement bliebe stehen - es käme nur nichts
                // mehr an, und der Betroffene fände in seiner eigenen Liste
                // nichts Auffälliges.
                if (optionen.Attr("jid") is not String werEinstellt ||
                    !String.Equals(BareOf(werEinstellt), session.BareJid, StringComparison.OrdinalIgnoreCase))
                {
                    await session.SendAsync(
                        StanzaErrorIq(id, "bad-request", "modify",
                                      applicationError: $"<invalid-jid xmlns='{PubSubErrorNamespace}'/>"));
                    return true;
                }

                var konto = to is null
                                ? session.Account
                                : GetAccount(BareOf(to));

                var subId = optionen.Attr("subid");

                // Kein `konto?.Find(...)`: Ein bedingter Aufruf lässt den
                // out-Parameter im Zweifel unbeschrieben, und der Compiler
                // sagt das zu Recht.
                PepSubscription? abonnement = null;

                var befund = konto is null
                                 ? PepSubscriptionResult.NotSubscribed
                                 : konto.FindPepSubscription(node, session.BareJid!, subId, out abonnement);

                if (befund != PepSubscriptionResult.Ok)
                {

                    // XEP-0060, Abschnitt 6.3.3: hier not-acceptable, beim
                    // Abbestellen bad-request. Die Anfrage ist in Ordnung, sie
                    // lässt sich nur in dieser Lage nicht beantworten.
                    await session.SendAsync(
                        befund == PepSubscriptionResult.SubIdRequired
                            ? StanzaErrorIq(id, "not-acceptable", "modify",
                                            applicationError: $"<subid-required xmlns='{PubSubErrorNamespace}'/>")
                            : SubscriptionErrorIq(id, befund));

                    return true;

                }

                // Das Angebot: was sich einstellen lässt und was gerade gilt.
                if (type == "get")
                {

                    await session.SendAsync(
                        $"<iq type='result' id='{id}'" +
                        (to is not null ? $" from='{XmlEscaping.Escape(BareOf(to)!)}'" : "") + ">" +
                        $"<pubsub xmlns='{OmemoPep.PubSubNamespace}'>" +
                        $"<options node='{XmlEscaping.Escape(node)}'" +
                        $" jid='{XmlEscaping.Escape(session.BareJid!)}'" +
                        $" subid='{abonnement!.SubId}'>" +
                        abonnement.Options.ToForm().ToString(SaveOptions.DisableFormatting) +
                        "</options></pubsub></iq>");

                    return true;

                }

                var formular = optionen.Child(DataFormNamespace, "x");

                if (formular is null ||
                    !PubSubSubscriptionOptions.TryRead(formular, out var eingestellt))
                {
                    await session.SendAsync(
                        StanzaErrorIq(id, "bad-request", "modify",
                                      applicationError: $"<invalid-options xmlns='{PubSubErrorNamespace}'/>"));
                    return true;
                }

                konto!.SetPepSubscriptionOptions(node, session.BareJid!, abonnement!.SubId, eingestellt!);

                await session.SendAsync(
                    $"<iq type='result' id='{id}'" +
                    (to is not null ? $" from='{XmlEscaping.Escape(BareOf(to)!)}'" : "") + "/>");

                return true;

            }

            #endregion

            return false;

        }

        /// <summary>
        /// Darf dieser JID an die Einträge des Knotens (XEP-0060,
        /// Abschnitt 4.5)?
        /// </summary>
        /// <remarks>
        /// <b>Der Eigentümer immer.</b> Er ist bei sich selbst kein
        /// Presence-Abonnent, und ein Modell, das ihn aus seinem eigenen
        /// Knoten aussperrt, hätte den Namen nicht verdient.
        ///
        /// Das Modell verrät nebenbei, dass es den Knoten gibt: Wer keinen
        /// Zugriff hat, bekommt <c>&lt;not-authorized/&gt;</c> und nicht
        /// <c>&lt;item-not-found/&gt;</c>. Das ist so vorgesehen (Abschnitt
        /// 6.5.3) und trotzdem eine Auskunft - für einen Knoten, dessen blosse
        /// Existenz ein Geheimnis wäre, ist <c>presence</c> das falsche
        /// Mittel.
        /// </remarks>
        /// <remarks>
        /// <b>Nur die Hälfte der Frage</b>, und zwar die nach dem
        /// Zugriffsmodell: Wer <i>hereindarf</i>. Wer draussen bleibt, sagt die
        /// Rolle, und das steht in <see cref="PepAccessErrorIq"/> - beides hier
        /// zu prüfen hiesse, dieselbe Entscheidung an zwei Stellen zu treffen,
        /// und eine davon würde beim nächsten Mal vergessen.
        /// </remarks>
        private static Boolean MayAccessPepNode(XMPPAccount account, String node, String requesterBareJid)
        {

            // Der Eigentümer kommt an seinen Knoten, gleich welches Modell
            // gilt. Er ist bei sich selbst weder Presence-Abonnent noch auf
            // einer Liste.
            if (String.Equals(account.BareJid, requesterBareJid, StringComparison.OrdinalIgnoreCase))
                return true;

            return account.PepNodeConfiguration(node)?.AccessModel switch {

                       PubSubAccessModel.Presence
                           => account.IsPresenceSubscriber(requesterBareJid),

                       // Auf der Liste steht, wen der Eigentümer ausdrücklich
                       // daraufgesetzt hat - eine Presence-Berechtigung
                       // entsteht nebenbei, eine Rolle nicht.
                       PubSubAccessModel.Whitelist
                           => account.PepAffiliationOf(node, requesterBareJid)
                                  is PubSubAffiliation.Publisher or PubSubAffiliation.Member,

                       _   => true

                   };

        }

        /// <summary>
        /// XEP-0060, Abschnitte 6.1.3.4 und 6.5.3: Der Knoten steht nur denen
        /// offen, die die Presence seines Eigentümers sehen dürfen.
        /// </summary>
        private String NotAuthorizedForPepNodeIq(String? id)

            => StanzaErrorIq(id, "not-authorized", "auth",
                             applicationError: $"<presence-subscription-required xmlns='{PubSubErrorNamespace}'/>");

        /// <summary>
        /// Die Absage auf einen Zugriff, oder null, wenn er erlaubt ist.
        /// </summary>
        /// <remarks>
        /// <b>Zwei Absagen und nicht eine</b>, weil sie Verschiedenes sagen:
        /// <c>&lt;not-authorized/&gt;</c> heisst „dieser Knoten steht dir nicht
        /// offen" und nennt mit
        /// <c>&lt;presence-subscription-required/&gt;</c> gleich den Weg
        /// hinein. <c>&lt;forbidden/&gt;</c> für einen Ausgeschlossenen
        /// (Abschnitt 6.1.3.8) sagt „du nicht" - und es gibt keinen Weg, den
        /// er selbst gehen könnte. Beides gleich zu beantworten hiesse, einen
        /// Ausgeschlossenen auf eine Presence-Anfrage zu schicken, die nichts
        /// ändern wird.
        /// </remarks>
        private String? PepAccessErrorIq(String? id, XMPPAccount account, String node, String requesterBareJid)

            => account.PepAffiliationOf(node, requesterBareJid) == PubSubAffiliation.Outcast
                   ? StanzaErrorIq(id, "forbidden", "auth")
                   : MayAccessPepNode(account, node, requesterBareJid)
                         ? null
                         : NotAuthorizedForPepNodeIq(id);

        /// <summary>
        /// Die Ablehnungen, die für Abbestellen und Einstellen dieselben sind
        /// (XEP-0060, Abschnitte 6.2.3 und 6.3.3).
        /// </summary>
        /// <remarks>
        /// <c>SubIdRequired</c> steht bewusst nicht darin: Dafür verlangt das
        /// XEP an den beiden Stellen verschiedene Fehler, und ein gemeinsamer
        /// Helfer, der sich für einen entschiede, würde eine der beiden
        /// Stellen still falsch beantworten.
        /// </remarks>
        private String SubscriptionErrorIq(String? id, PepSubscriptionResult result)

            => result switch {

                   PepSubscriptionResult.WrongSubId
                       => StanzaErrorIq(id, "not-acceptable", "modify",
                                        applicationError: $"<invalid-subid xmlns='{PubSubErrorNamespace}'/>"),

                   _   => StanzaErrorIq(id, "unexpected-request", "cancel",
                                        applicationError: $"<not-subscribed xmlns='{PubSubErrorNamespace}'/>")

               };

        /// <summary>
        /// Schickt eine PEP-Benachrichtigung an alle, die den Zustand dieses
        /// Kontos sehen dürfen.
        /// </summary>
        /// <remarks>
        /// <b>Die eigenen weiteren Resourcen gehören ausdrücklich dazu.</b>
        /// Bei OMEMO hängt daran mehr als Bequemlichkeit: Abschnitt 5.2
        /// verlangt von einem Client, sich <i>wieder einzutragen</i>, wenn er
        /// aus der eigenen Geräteliste verschwunden ist. Erfährt er von der
        /// Änderung nichts, kann er das nicht - und ist von da an für alle
        /// unerreichbar, ohne dass irgendetwas nach einem Fehler aussieht.
        ///
        /// <b>Dazu die ausdrücklichen Abonnenten (XEP-0060, Abschnitt 6.1).</b>
        /// Ohne sie hiesse „abonnieren" nichts anderes als „im Roster stehen",
        /// und die Zusage aus <see cref="HandlePepAsync"/> wäre eine ohne
        /// Deckung.
        ///
        /// <b>Ausdrücklich schlägt beiläufig.</b> Wer den Knoten abonniert hat,
        /// bekommt die Meldung <i>je Abonnement</i> und nicht zusätzlich über
        /// die Presence - sonst hinge die Zahl der Zustellungen daran, ob
        /// jemand nebenbei auch noch im Roster steht. Wer kein Abonnement hat,
        /// bekommt sie wie bisher einmal über die Presence.
        ///
        /// Die Kennung steht nur dort, wo es eine gibt: in der SHIM-Kopfzeile
        /// der abonnierten Zustellung (Abschnitt 12.20). Eine erfundene wäre
        /// schlimmer als keine - der Empfänger könnte danach abbestellen
        /// wollen, was nie bestellt wurde.
        /// </remarks>
        /// <param name="owner">
        /// Das Konto, dem der Knoten gehört - nicht unbedingt das des
        /// Absenders: Ein <c>publisher</c> schreibt in einen fremden Knoten,
        /// und die Meldung kommt trotzdem von dessen Eigentümer. Alles andere
        /// wäre eine Falschaussage über die Herkunft, und der Spoofing-Schutz
        /// des Empfängers hätte recht, sie zu verwerfen.
        /// </param>
        /// <param name="sender">
        /// Die Sitzung, die veröffentlicht hat - sie bekommt ihre eigene
        /// Meldung nicht.
        /// </param>
        /// <param name="content">
        /// Was in <c>&lt;items/&gt;</c> steht: ein <c>&lt;item/&gt;</c> mit
        /// seiner Nutzlast oder ein <c>&lt;retract/&gt;</c> mit der Kennung des
        /// zurückgenommenen Eintrags.
        ///
        /// <b>Beides ist eine Zustellung und geht deshalb hier durch</b> - je
        /// Abonnement, mit Kennung, und stillgelegte übergangen. Eine
        /// Rücknahme, die einem stillgelegten Abonnement doch zugestellt würde,
        /// unterliefe die Einstellung, und eine ohne Kennung wäre bei mehreren
        /// Abonnements keiner Zustellung zuzuordnen. Das unterscheidet sie vom
        /// Löschen und Leeren: Die betreffen den Knoten und gehen deshalb je
        /// Abonnenten einmal hinaus (siehe <see cref="NotifyPepNodeAsync"/>).
        /// </param>
        private async Task NotifyPepAsync(XMPPAccount   owner,
                                          XMPPSession   sender,
                                          String        node,
                                          String        content)
        {

            if (!RouteStanzas || sender.FullJid is null)
                return;

            String Ereignis(String? subId)
                => $"<message from='{owner.BareJid}' type='headline'>" +
                   "<event xmlns='http://jabber.org/protocol/pubsub#event'>" +
                   $"<items node='{XmlEscaping.Escape(node)}'>" +
                   content +
                   "</items></event>" +
                   (subId is not null
                        ? "<headers xmlns='http://jabber.org/protocol/shim'>" +
                          $"<header name='SubID'>{XmlEscaping.Escape(subId)}</header>" +
                          "</headers>"
                        : "") +
                   "</message>";

            var abonnements = owner.PepSubscriptions(node);

            // Auch die stillgelegten stehen hier drin, und das ist der Punkt:
            // Wer gesagt hat, dass er nichts bekommen will, soll es auch nicht
            // über die Presence bekommen. Sonst unterliefe ein zweiter Weg eine
            // ausdrückliche Einstellung.
            var ausdruecklich = new HashSet<String>(abonnements.Select(a => a.Jid),
                                                    StringComparer.OrdinalIgnoreCase);

            foreach (var ziel in PresenceTargetsOf(owner, sender))
                if (!ausdruecklich.Contains(ziel.BareJid ?? ""))
                    await ziel.SendAsync(StampTo(Ereignis(null), ziel.FullJid!));

            foreach (var abonnement in abonnements.Where(a => a.Options.Deliver))
                foreach (var ziel in SessionsOf(abonnement.Jid))
                    if (ziel != sender && ziel.FullJid is not null)
                        await ziel.SendAsync(StampTo(Ereignis(abonnement.SubId), ziel.FullJid));

        }

        /// <summary>
        /// Meldet allen, die von einem Knoten etwas bekommen hätten, was mit
        /// ihm geschehen ist (XEP-0060, Abschnitte 8.4.2 und 8.5.2).
        /// </summary>
        /// <param name="content">
        /// Der Inhalt der Meldung - <c>&lt;delete/&gt;</c> oder
        /// <c>&lt;purge/&gt;</c> samt Knotennamen.
        /// </param>
        /// <param name="subscribers">
        /// Die ausdrücklichen Abonnenten. Beim Löschen sind sie zu diesem
        /// Zeitpunkt schon fort und müssen deshalb mitgegeben werden - eine
        /// Meldung an die, die man hinterher noch findet, erreichte niemanden.
        /// </param>
        /// <remarks>
        /// <b>Jeden einmal, ohne Kennung.</b> Anders als eine Veröffentlichung
        /// gehört diese Meldung zu keiner Zustellung: Sie handelt vom Knoten.
        /// Wer zwei Abonnements hält, bekommt sie trotzdem nur einmal - eine
        /// Kennung zu nennen hiesse, die anderen bestünden weiter.
        ///
        /// Die Empfänger sind dieselben wie bei einer Veröffentlichung:
        /// Presence-Empfänger und ausdrückliche Abonnenten. Wer die Einträge
        /// bekommen hätte, soll erfahren, dass es sie nicht mehr gibt.
        /// </remarks>
        private async Task NotifyPepNodeAsync(XMPPAccount          owner,
                                              XMPPSession          sender,
                                              String               content,
                                              IEnumerable<String>  subscribers)
        {

            if (!RouteStanzas || sender.FullJid is null)
                return;

            var ereignis = $"<message from='{owner.BareJid}' type='headline'>" +
                           $"<event xmlns='{PubSubManager.EventNamespace}'>{content}</event>" +
                           "</message>";

            var ausdruecklich = new HashSet<String>(subscribers, StringComparer.OrdinalIgnoreCase);

            foreach (var ziel in PresenceTargetsOf(owner, sender))
                if (!ausdruecklich.Contains(ziel.BareJid ?? ""))
                    await ziel.SendAsync(StampTo(ereignis, ziel.FullJid!));

            foreach (var wer in ausdruecklich)
                foreach (var ziel in SessionsOf(wer))
                    if (ziel != sender && ziel.FullJid is not null)
                        await ziel.SendAsync(StampTo(ereignis, ziel.FullJid));

        }

        /// <summary>
        /// XEP-0060, Abschnitt 8.8.4: Sagt einem Abonnenten, dass sein
        /// Abonnement beendet wurde.
        /// </summary>
        /// <remarks>
        /// <b>Wer beendet wurde, ohne zu fragen, muss es erfahren.</b> Sonst
        /// wartet er auf Meldungen, die nicht mehr kommen - und das ist der
        /// Zustand, den <see cref="PubSubSubscriptionState"/> seit D71 als den
        /// schlimmeren beschreibt: Wer sich zu Unrecht für nicht abonniert
        /// hält, fragt noch einmal nach; wer sich zu Unrecht für abonniert
        /// hält, wartet auf etwas, das nie kommt.
        ///
        /// <b>Die Kennung gehört dazu.</b> Bei mehreren Abonnements auf
        /// denselben Knoten ist sie das einzige, woran der Empfänger erkennt,
        /// welches erloschen ist - und ohne sie müsste er alle für erloschen
        /// halten oder keines.
        ///
        /// <b>Ein <c>headline</c>, also nichts für die Ablage</b> (XEP-0160).
        /// Wer gerade offline ist, erfährt es nicht - so wie er auch die
        /// Veröffentlichungen nicht bekommt, die er versäumt. Die Auskunft
        /// bleibt trotzdem erreichbar: Abschnitt 5.6 sagt ihm beim nächsten
        /// Verbinden, was er noch hat. Eine aufbewahrte Meldung wäre die
        /// schlechtere Auskunft, denn sie beschreibt einen Stand von damals.
        /// </remarks>
        private async Task NotifySubscriptionEndedAsync(XMPPAccount      owner,
                                                        String           node,
                                                        PepSubscription  subscription)
        {

            if (!RouteStanzas)
                return;

            var ereignis = $"<message from='{owner.BareJid}' type='headline'>" +
                           $"<event xmlns='{PubSubManager.EventNamespace}'>" +
                           $"<subscription node='{XmlEscaping.Escape(node)}'" +
                           $" jid='{XmlEscaping.Escape(subscription.Jid)}'" +
                           $" subid='{XmlEscaping.Escape(subscription.SubId)}'" +
                           " subscription='none'/>" +
                           "</event></message>";

            foreach (var ziel in SessionsOf(subscription.Jid))
                if (ziel.FullJid is not null)
                    await ziel.SendAsync(StampTo(ereignis, ziel.FullJid));

        }

        /// <summary>
        /// Was der Server an <b>seiner eigenen Adresse</b> selbst beantwortet:
        /// XEP-0199 Ping, XEP-0030 disco#info, und sonst
        /// <c>&lt;service-unavailable/&gt;</c>.
        /// </summary>
        /// <returns>
        /// Die Antwort - oder <c>null</c>, wenn keine zu geben ist: Auf ein
        /// <c>result</c> oder <c>error</c> wird nie geantwortet (RFC 6120,
        /// Abschnitt 8.2.3, Regel 4), und die Testschalter können das Schweigen
        /// erzwingen.
        /// </returns>
        /// <remarks>
        /// Gebaut statt geschickt, und darum geht es hier: Diese Antworten
        /// standen bis D36 mitten in <see cref="HandleIqAsync"/> und schrieben
        /// unmittelbar in eine Client-Sitzung. Damit waren sie für eine
        /// Gegenstelle unerreichbar — eine Anfrage über die Servergrenze an die
        /// eigene Adresse blieb unbeantwortet, obwohl Regel 3 eine Antwort
        /// verlangt.
        ///
        /// <b>Die Antwort hängt nicht daran, wer fragt.</b> Was dieser Server
        /// kann, ist für einen hiesigen Client und für einen fremden Server
        /// dasselbe; nur der Rückweg unterscheidet sich, und den kennt der
        /// Aufrufer. Deshalb baut diese Stelle die Stanza und verschickt sie
        /// nicht — sonst gäbe es die Auskunft zweimal, und zwei Auskünfte über
        /// dieselbe Sache können auseinanderlaufen.
        ///
        /// Was <b>nicht</b> hierhergehört, ist ebenso wichtig: Binding, Legacy
        /// Session, Carbons und der Roster ändern den Zustand <i>einer
        /// Sitzung</i> oder gehören einem Konto. Sie bleiben in
        /// <see cref="HandleIqAsync"/> und damit für eine Gegenstelle
        /// unerreichbar — ein fremder Server, der nach unserem Roster fragt,
        /// bekommt hier <c>&lt;service-unavailable/&gt;</c> wie für jede andere
        /// unbekannte Anfrage.
        /// </remarks>
        private String? AnswerAboutSelf(String frame, String? id, String? type)
        {

            // Regel 4: Eine Antwort wird nie beantwortet.
            if (type is not ("get" or "set"))
                return null;

            // XEP-0199 Ping an den Server
            if (frame.Contains("urn:xmpp:ping", StringComparison.Ordinal) && type == "get")
                return FailPings
                           ? StanzaErrorIq(id, "service-unavailable")
                           : AnswerPings
                                 ? $"<iq type='result' id='{id}' from='{Domain}'/>"
                                 : null;

            // XEP-0030 disco#info über den Server
            if (frame.Contains("http://jabber.org/protocol/disco#info", StringComparison.Ordinal) && type == "get")
            {

                if (FailDiscoInfo)
                    return StanzaErrorIq(id, "item-not-found", "modify",
                                         "Diese Auskunft wird hier nicht erteilt.");

                // Dieser Server kündigt keine Capabilities an und führt keine
                // Nodes. Eine Frage nach einem Node fragt also nach etwas, das
                // es hier nicht gibt - und bekam bis dahin die volle
                // Merkmalsliste, als gäbe es jeden erdachten Node.
                //
                // Der Fehler nimmt die Frage mit zurück (RFC 6120,
                // Abschnitt 8.3.1); das ist zugleich die Spiegelung, die
                // XEP-0030, Abschnitt 3.2 für das 'node' verlangt.
                var node = Regex.Match(frame, @"<query[^>]*?\snode=['""]([^'""]*)['""]");

                if (node.Success)
                    return StanzaErrorIq(id, "item-not-found", "cancel",
                                         "Diesen Node gibt es hier nicht.",
                                         "<query xmlns='http://jabber.org/protocol/disco#info' " +
                                        $"node='{node.Groups[1].Value}'/>");

                return $"<iq type='result' id='{id}' from='{Domain}'>" +
                        "<query xmlns='http://jabber.org/protocol/disco#info'>" +
                        "<identity category='server' type='im' name='XMPPServer'/>" +
                        "<feature var='urn:xmpp:carbons:2'/>" +
                        "<feature var='urn:xmpp:ping'/>" +
                        "<feature var='urn:xmpp:sm:3'/>" +
                        // XEP-0160, Abschnitt 4: Nur wenn es die Ablage
                        // wirklich gibt. Eine Ankündigung, die immer steht,
                        // verspricht einem Client, dass seine Nachricht an
                        // einen Abwesenden liegen bleibt - und lässt ihn
                        // den Fehler übersehen, mit dem der Server ihm
                        // gerade das Gegenteil sagt.
                        (StoreOfflineMessages ? "<feature var='msgoffline'/>" : "") +
                        "</query></iq>";

            }

            // Unbekannte Anfragen bekommen einen Fehler (Abschnitt 8.4), und
            // zwar auch dann, wenn niemand zuhört: Regel 3 kennt keine dritte
            // Möglichkeit neben result und error.
            return StanzaErrorIq(id, "service-unavailable");

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

            // Ab hier: eine Anfrage, also get oder set. Ein anderer Wert kommt
            // hier nicht mehr an - beide Eingänge weisen ihn nach RFC 6120,
            // Abschnitt 8.2.3, Regel 2 ab, bevor sie zustellen.
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
            if (SessionOf(to) is { } match && SharesPresenceWith(match, sender))
                await match.SendAsync(stanza);

            else
                await SendServiceUnavailableAsync("iq", id, to, sender);

        }

        /// <summary>
        /// Darf der Fragende die Presence dieser Resource sehen (RFC 6121,
        /// Abschnitt 8.5.3.1)?
        /// </summary>
        /// <remarks>
        /// Die Prüfung, die Abschnitt 8.5.3.1 vor die Zustellung einer Anfrage
        /// stellt: „if the intended recipient does not share presence with the
        /// requesting entity either by means of a presence subscription of type
        /// 'both' or 'from' or by means of directed presence, then the server
        /// SHOULD NOT deliver the IQ stanza".
        ///
        /// Der Grund steht in Abschnitt 11 und ist feiner, als er zuerst
        /// aussieht: <b>Schon die Antwort ist eine Auskunft.</b> Wer eine
        /// Full-JID anfragt und ein Ergebnis bekommt, weiss, dass genau diese
        /// Resource in diesem Augenblick angemeldet ist - und wer
        /// <c>&lt;service-unavailable/&gt;</c> bekommt, weiss es nicht. Ohne
        /// diese Prüfung liesse sich die Anwesenheit eines Menschen abfragen,
        /// ohne ihn je um Erlaubnis gefragt zu haben, und Resourcenamen liessen
        /// sich durchprobieren.
        ///
        /// Zwei Wege hinein, und die Richtung ist bei beiden leicht zu
        /// verwechseln:
        /// <list type="bullet">
        ///   <item>
        ///     Der Roster des <b>Empfängers</b> trägt den Fragenden mit
        ///     <c>from</c> oder <c>both</c> - „der darf mich sehen". Ein
        ///     <c>to</c> hiesse das Gegenteil und gäbe die Auskunft an genau die
        ///     falsche Hälfte des Rosters.
        ///   </item>
        ///   <item>
        ///     Oder die Resource hat dem Fragenden gerichtete Presence
        ///     geschickt (Abschnitt 4.6) - dann hat sie ihre Anwesenheit von
        ///     selbst gezeigt, und die Antwort verrät nichts, was der Fragende
        ///     nicht schon weiss.
        ///   </item>
        /// </list>
        ///
        /// Die gerichtete Presence hängt an der <b>Sitzung</b> und nicht am
        /// Konto: Sie ist die Zusage einer Resource und endet mit ihr. Ein
        /// Roster-Eintrag gilt für alle Resourcen, eine gerichtete Presence nur
        /// für die, die sie geschickt hat.
        /// </remarks>
        private static Boolean SharesPresenceWith(XMPPSession recipient, String requester)

            => recipient.Account?.IsPresenceSubscriber(BareOf(requester)) == true ||
               recipient.HasDirectedPresenceTo(BareOf(requester));

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

                // XEP-0160, Abschnitt 3: ein chat, der *nur* einen Tippstatus
                // trägt, wird nicht abgelegt. Er ist eine Aussage über jetzt,
                // und beim Anmelden nachgereicht wäre er schlicht falsch.
                //
                // Der Absender bekommt dafür auch keinen Fehler, obwohl das
                // stillschweigende Verwerfen sonst ausgeschlossen ist: Wer eine
                // Nachricht schickt, will wissen, ob sie ankam; wer einen
                // Tippstatus schickt, hat nichts verloren, wenn er verfällt.
                if (messageType != MessageType.Chat || !IsChatStateOnly(stamped))
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
        /// Trägt diese Nachricht <b>nur</b> einen Tippstatus (XEP-0085)?
        /// </summary>
        /// <remarks>
        /// <b>Hier wird als einziger Stelle im Server ein Baum gelesen</b>, und
        /// zwar mit Absicht: Die Frage lautet „sind <i>alle</i> Kinder
        /// Tippstatus-Elemente", und die lässt sich an einer Zeichenkette nicht
        /// stellen. Ein <c>Contains</c> beantwortet „kommt vor", nicht „kommt
        /// nur vor" - und der Unterschied ist genau die Regel.
        ///
        /// Ein <c>thread</c> zählt nicht als Inhalt: XEP-0085, Abschnitt 5.3
        /// führt ihn ausdrücklich neben dem Tippstatus vor. Er ist eine
        /// Kennung, kein Text.
        ///
        /// Lässt sich die Stanza nicht lesen, ist die Antwort <c>false</c> -
        /// dann wird abgelegt wie bisher. Was sich nicht als Tippstatus
        /// nachweisen lässt, wird wie eine Nachricht behandelt; der umgekehrte
        /// Irrtum verlöre eine.
        /// </remarks>
        internal static Boolean IsChatStateOnly(String stanza)
        {

            try
            {

                var kinder = XElement.Parse(stanza).Elements().ToArray();

                // Das Any beantwortet zugleich den leeren Fall: Eine Nachricht
                // ohne Kinder trägt keinen Tippstatus.
                return kinder.Any(k => k.Name.NamespaceName == ChatStatesNamespace) &&
                       kinder.All(k => k.Name.NamespaceName == ChatStatesNamespace ||
                                       k.Name.LocalName     == "thread");

            }

            catch (System.Xml.XmlException)
            {
                return false;
            }

        }

        /// <summary>Der Namensraum der Tippstatus-Elemente (XEP-0085).</summary>
        private const String ChatStatesNamespace = "http://jabber.org/protocol/chatstates";

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

            var account = GetAccount(BareOf(to));

            // Ein Konto, das es nicht gibt, wird behandelt wie eines, das da
            // ist und gerade nicht zusieht - mit leerer Ablage.
            //
            // RFC 6121, Abschnitt 8.5.1 lässt für einen unbekannten Empfänger
            // die Wahl zwischen <service-unavailable/> und Schweigen. Frei ist
            // sie trotzdem nicht: Sie muss dieselbe sein wie für ein
            // vorhandenes, abwesendes Konto, sonst beantwortet sie die Frage
            // "gibt es dieses Konto?" - und zwar auf dem bequemsten Weg, den es
            // gibt (RFC 6120, Abschnitt 13.11; siehe D50 für dieselbe Frage bei
            // der Anmeldung).
            //
            // Hier stand ein blosses `return`, und damit fiel die Behandlung
            // auseinander, sobald die Ablage aus oder voll war: Das vorhandene
            // Konto bekam einen Fehler, das unbekannte Schweigen.
            //
            // Gefragt wird deshalb nicht "gibt es ein Konto", sondern "würde
            // die Ablage es annehmen". Für ein unbekanntes ist die Ablage leer,
            // und eine leere nimmt an, solange überhaupt etwas hineinpasst -
            // bei MaxStoredOfflineMessages = 0 also nichts.
            var abgelegt = StoreOfflineMessages &&
                           (account?.StoreOfflineMessage(stamped,
                                                         DateTimeOffset.UtcNow,
                                                         MaxStoredOfflineMessages)
                                ?? MaxStoredOfflineMessages > 0);

            if (abgelegt)
                return;

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
            //
            // Nur für ein hiesiges Konto beantwortet der Server sie selbst
            // (Abschnitt 4.3.2). Geht sie über die Grenze, ist er nicht der
            // Befragte, sondern der Übermittler: Abschnitt 4.3.1 lässt den
            // Server des Nutzers die Probe an den Server des Kontakts schicken,
            // und dort wird sie beantwortet.
            //
            // Diese Unterscheidung fehlte. Der Zweig griff für *jedes* Ziel,
            // fand für eine fremde Adresse kein Konto und kehrte zurück - eine
            // Probe an einen Kontakt auf einem anderen Server verliess diesen
            // Server also nie. Aufgefallen ist es erst, als ein Test die
            // Gegenrichtung prüfen sollte und die Probe nie ankam.
            if (type == "probe" && to is not null && session.BareJid is not null)
            {

                if (IsLocal(to))
                    await AnswerPresenceProbeAsync(session.BareJid, session.FullJid, to);

                else
                    await RouteToAsync(to, stamped);

                return;

            }

            // Der Subscription-Handshake (RFC 6121, Abschnitt 3).
            if (to is not null &&
                type is "subscribe" or "subscribed" or "unsubscribe" or "unsubscribed")
            {
                await HandleSubscriptionAsync(session, type, BareOf(to), frame);
                return;
            }

            // Sonstige gerichtete Presence geht genau dorthin - und wird
            // vermerkt.
            if (to is not null)
            {

                // RFC 6121, Abschnitt 4.6: Wer einem Fremden seine Anwesenheit
                // zeigt, lässt ihn damit auch fragen (Abschnitt 8.5.3.1). Ohne
                // diesen Vermerk wäre gerichtete Presence eine Einbahnstrasse:
                // Der Empfänger sähe, dass die Resource da ist, dürfte sie aber
                // nichts fragen - und genau darauf baut ein Gespräch mit einem
                // Nichtkontakt auf.
                session.RecordDirectedPresence(BareOf(to), type is null);

                await RouteToAsync(to, stamped);
                return;

            }

            // Vor dem Aufzeichnen gefragt: danach ist die Sitzung verfügbar,
            // und der Unterschied zwischen "war schon" und "ist gerade
            // geworden" wäre nicht mehr zu sehen.
            var wurdeVerfuegbar  = type is null && !session.IsAvailable;

            // RFC 6121, Abschnitt 4.6.3, Regel 2: Meldet sich die Resource ab,
            // bekommen auch die Empfänger gerichteter Presence die Abmeldung -
            // und die Liste ist damit erledigt (Abschnitt 4.6.1). Beides holt
            // ein Aufruf, siehe TakeDirectedPresenceTargets.
            var gerichtete       = type is null
                                       ? []
                                       : session.TakeDirectedPresenceTargets();

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
            {
                ForgetDirectedPresenceFrom(target, stamped);
                await target.SendAsync(stamped);
            }

            // Kontakte auf fremden Domains bekommen dieselbe Presence - eine
            // nicht erreichbare Gegenstelle bleibt hier folgenlos, Presence
            // wird nicht mit Fehlern beantwortet.
            foreach (var remote in RemotePresenceTargetsOf(session))
                await RouteToAsync(remote, StampTo(stamped, remote));

            await SendUnavailableToDirectedTargetsAsync(session, gerichtete, stamped);

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
            => session.Account is null
                   ? []
                   : PresenceTargetsOf(session.Account, session);

        /// <summary>
        /// Wer bekommt, was von diesem Konto ausgeht?
        /// </summary>
        /// <param name="except">
        /// Eine Sitzung, die es nicht bekommt - die des Absenders. Sie muss
        /// nicht zu diesem Konto gehören: Ein <c>publisher</c> schreibt in
        /// einen fremden Knoten und braucht seine eigene Meldung nicht.
        /// </param>
        private IEnumerable<XMPPSession> PresenceTargetsOf(XMPPAccount account, XMPPSession? except)
        {

            foreach (var other in Sessions.Where(s => s != except && s.FullJid is not null))
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
        /// <param name="proberBareJid">Wer fragt - ohne Resource.</param>
        /// <param name="replyTo">
        /// Wohin die Antwort geht: die Full-JID einer hiesigen Sitzung, sonst
        /// der Bare-JID des Fragenden auf der fremden Domain.
        /// </param>
        /// <param name="to">Nach wessen Zustand gefragt wird.</param>
        /// <remarks>
        /// Fehlt die Berechtigung, bleibt die Probe unbeantwortet. Abschnitt
        /// 8.5.1 stellt dem Server für ein unbekanntes Konto
        /// <c>&lt;unsubscribed/&gt;</c> und Schweigen frei - Schweigen verrät
        /// nicht einmal, ob es das Konto überhaupt gibt, und deshalb bleibt es
        /// dabei.
        ///
        /// Gefragt wird der Roster des <b>Befragten</b> nach <c>from</c> oder
        /// <c>both</c>: „der darf mich sehen". Dieselbe Hälfte wie bei der
        /// IQ-Prüfung aus Abschnitt 8.5.3.1, und dieselbe Verwechslungsgefahr.
        ///
        /// Ein Weg für beide Herkünfte, über <see cref="RouteToAsync"/>. Eine
        /// eigene Verzweigung für den hiesigen Fragenden wäre eine zweite
        /// Antwort auf die Frage „hier oder woanders", die die Weiche schon
        /// beantwortet.
        /// </remarks>
        private async Task AnswerPresenceProbeAsync(String proberBareJid,
                                                    String replyTo,
                                                    String to)
        {

            var account = GetAccount(BareOf(to));

            if (account is null ||
                !account.IsPresenceSubscriber(proberBareJid))
            {
                return;
            }

            foreach (var s in SessionsOf(account.BareJid).Where(s => s.LastPresence is not null))
                await RouteToAsync(replyTo, StampTo(s.LastPresence!, replyTo));

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
            {
                ForgetDirectedPresenceFrom(t, stanza);
                await t.SendAsync(stanza);
            }

            return true;

        }

        /// <summary>
        /// Eine eingehende Abmeldung nimmt ihren Absender aus der Liste
        /// gerichteter Presence des Empfängers (RFC 6121, Abschnitt 4.6.1).
        /// </summary>
        /// <remarks>
        /// Der SOLL-Teil des Abschnitts: „The server MUST remove from the
        /// directed presence list ... any entity to which the user sends
        /// directed unavailable presence and SHOULD remove any entity that sends
        /// unavailable presence to the user."
        ///
        /// Die beiden Hälften sehen ähnlich aus und meinen Gegenteiliges. Das
        /// MUSS betrifft den <b>eigenen</b> Widerruf und steht in
        /// <see cref="XMPPSession.RecordDirectedPresence"/>; dieses SOLL betrifft
        /// die Gegenrichtung: Der andere geht, und damit ist die vorübergehende
        /// Beziehung ebenfalls zu Ende. Seit D17 hängt daran, wer diese Resource
        /// etwas fragen darf (Abschnitt 8.5.3.1) - ohne diesen Weg behielte ein
        /// Rückkehrer sein Fragerecht, obwohl ihm niemand mehr etwas gezeigt
        /// hat.
        ///
        /// Angesehen wird der <b>Empfang</b> und nicht das Senden, denn genau so
        /// ist die Regel formuliert: „any entity that sends unavailable presence
        /// <i>to the user</i>". Deshalb steht der Aufruf hier, in der einen
        /// Weiche, durch die jede Stanza an eine hiesige Adresse läuft - und
        /// nicht bei den Absendern, von denen es mehrere gibt.
        /// </remarks>
        private static void ForgetDirectedPresenceFrom(XMPPSession recipient, String stanza)
        {

            if (!StanzaElement.Is(stanza, "presence") ||
                Attr(stanza, "type") != "unavailable")
            {
                return;
            }

            if (Attr(stanza, "from") is { } from)
                recipient.RecordDirectedPresence(BareOf(from), available: false);

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

            // RFC 6120, Abschnitt 8.3.3.8, hier für den Weg über die Grenze.
            // Die Prüfung des Absenders steht vor der Zuständigkeitsfrage: Ein
            // DomainOf auf eine Zeichenkette, die kein JID ist, vergleicht
            // Bruchstücke und nennt das Ergebnis dann "fremde Domain".
            if (!JidUtilities.TryParse(from, out _))
            {
                OnRemoteStanzaRejected?.Invoke(peerDomain, $"'{from}' ist kein JID");
                return RemoteStanzaResult.MalformedSender;
            }

            if (!String.Equals(DomainOf(from), peerDomain, StringComparison.OrdinalIgnoreCase))
            {
                OnRemoteStanzaRejected?.Invoke(
                    peerDomain,
                    $"'{from}' gehört nicht zu '{peerDomain}'");
                return RemoteStanzaResult.ForeignSender;
            }

            // Und der Empfänger vor der Frage, ob er hierher gehört: IsLocal
            // sieht nur die Domain an, und 'b ob@dieser.server' gehört hierher,
            // ohne eine Adresse zu sein. Bis hierher lief so eine Stanza in die
            // Zustellung und sah dort aus wie eine an einen Abwesenden.
            if (!JidUtilities.TryParse(to, out _))
            {

                OnRemoteStanzaRejected?.Invoke(peerDomain, $"'{to}' ist kein JID");

                // Abschnitt 8.3.1: auf einen Fehler folgt kein Fehler. Über die
                // Grenze wiegt das schwerer als im eigenen Haus - zwei Server,
                // die einander antworten, hören von selbst nicht auf.
                if (Attr(stanza, "type") != "error")
                    await RouteToAsync(from,
                                       JidMalformedError(StanzaElement.NameOf(stanza) ?? "message",
                                                         Attr(stanza, "id"),
                                                         from));

                return RemoteStanzaResult.MalformedRecipient;

            }

            if (!IsLocal(to))
            {
                // Weiterleiten für Dritte wäre ein offenes Relais.
                OnRemoteStanzaRejected?.Invoke(peerDomain, $"'{to}' liegt nicht auf '{Domain}'");
                return RemoteStanzaResult.ForeignRecipient;
            }

            if (!RouteStanzas)
                return RemoteStanzaResult.RoutingDisabled;

            // RFC 6120, Abschnitt 8.2.3, Regel 2, hier in der Rolle des
            // Empfängers. Ein Client dieses Servers kommt nie so weit - sein
            // eigener Server weist ihn schon als Router ab -, eine fremde
            // Implementierung, die die Regel nicht kennt, sehr wohl.
            //
            // Vor allen Zustellzweigen: Der Weg für Anfragen unterscheidet nur
            // Antwort und Anfrage und hielte alles Unbekannte für eine Anfrage.
            if (StanzaElement.Is(stanza, "iq") &&
                !IqTypes.IsKnown(Attr(stanza, "type")))
            {

                await RouteToAsync(from, BadRequestIq(Attr(stanza, "id")));

                return RemoteStanzaResult.Accepted;

            }

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
            if (StanzaElement.Is(stanza, "message"))
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
            if (StanzaElement.Is(stanza, "iq") &&
                to.Contains('@'))
            {
                await DeliverIqLocallyAsync(null, to, stanza);
                return RemoteStanzaResult.Accepted;
            }

            // Und eine Anfrage an die Serveradresse selbst beantwortet der
            // Server für sich - dieselben Auskünfte, die ein hiesiger Client
            // bekommt.
            //
            // Bis D36 ging sie ins Routing, fand dort für die Domain keine
            // Sitzung und verschwand. Die Gegenstelle wartete auf eine Antwort,
            // die Regel 3 verlangt und die nie kam - erfahren hat sie davon
            // nichts. Der Rückweg ist der einzige Unterschied zum hiesigen
            // Client; die Auskunft selbst hängt nicht daran, wer fragt.
            if (StanzaElement.Is(stanza, "iq"))
            {

                if (AnswerAboutSelf(stanza, Attr(stanza, "id"), Attr(stanza, "type")) is { } antwort)
                    await RouteToAsync(from, antwort);

                return RemoteStanzaResult.Accepted;

            }

            // Eine Presence-Probe beantwortet der Server selbst und stellt sie
            // nicht zu (RFC 6121, Abschnitte 8.5.2.1.2, 8.5.2.2.2, 8.5.3.1 und
            // 8.5.3.2.2 - alle vier verweisen dafür auf Abschnitt 4.3).
            //
            // Bis hierher ging sie ins Routing und landete beim Client. Das war
            // in beide Richtungen falsch: Der Client bekam eine Stanza zu sehen,
            // die für ihn nicht bestimmt ist und auf die er nichts antworten
            // kann, und die Gegenstelle bekam nie eine Antwort - sie fragt nach
            // dem Zustand eines Kontakts und erhält Schweigen, obwohl der Server
            // die Auskunft hat. Genau dieselbe Asymmetrie wie bei Nachricht und
            // IQ: Für einen hiesigen Client wurde die Probe seit jeher
            // beantwortet.
            if (art is null &&
                StanzaElement.Is(stanza, "presence") &&
                Attr(stanza, "type") == "probe")
            {

                await AnswerPresenceProbeAsync(BareOf(from), BareOf(from), to);

                return RemoteStanzaResult.Accepted;

            }

            // Verfügbare und unverfügbare Presence nimmt den geraden Weg, und
            // der ist hier auch der richtige: An einen Bare-JID geht sie an alle
            // Resourcen (Abschnitt 8.5.2.1.2), an eine Full-JID an die passende
            // (8.5.3.1), und ohne Konto oder ohne passende Resource still ins
            // Leere (8.5.1 und 8.5.3.2.2). Genau das tut RouteToAsync.
            //
            // Eine Anfrage an die Domain selbst nimmt ihn ebenso - was der Server
            // für sich beantworten müsste, beantwortet er noch nicht (siehe
            // „Später").
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

            if (!StanzaElement.Is(stanza, "presence"))
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
        /// Die Ablehnung nach RFC 6120, Abschnitt 8.2.3, Regel 2.
        /// </summary>
        /// <remarks>
        /// Die <c>id</c> geht mit, wenn es eine gibt, und fehlt sonst - ein
        /// leeres Attribut gehört zu keiner Frage und ist schlechter als
        /// keines.
        ///
        /// Geschickt wird die Ablehnung trotzdem, auch ohne <c>id</c>. Regel 2
        /// stellt sie unter keinen Vorbehalt, und der Grund trägt: Wo eine
        /// unbeantwortete Anfrage den Absender nur warten lässt, sagt diese
        /// Antwort etwas über die Stanza selbst - dass ihre Form nicht stimmt.
        /// Das kann er auch dann brauchen, wenn er sie keiner offenen Frage
        /// zuordnen kann.
        ///
        /// Absender ist dieser Server. <c>&lt;service-unavailable/&gt;</c>
        /// antwortet im Namen des gemeinten Empfängers, weil der Server dort
        /// für ihn geantwortet hat; hier hat er die Stanza gar nicht erst
        /// angenommen, und ein Empfänger als Absender behauptete, jemand habe
        /// hineingesehen.
        /// </remarks>
        /// <summary>
        /// RFC 6120, Abschnitt 8.3.3.8: Weist eine Stanza ab, deren
        /// <c>to</c> kein JID ist.
        /// </summary>
        /// <remarks>
        /// Die Prüfung ist die aus RFC 7622 - dieselbe, die der Client für
        /// seine eigenen Adressen anwendet. Sie stand bis hierher vollständig
        /// da und wurde vom Server kein einziges Mal gefragt: Was ankam, ging
        /// in die Zustellung, und ein unmöglicher Empfänger sah dort aus wie
        /// ein abwesender. Der Absender bekam Schweigen oder eine Ablage, die
        /// nie jemand abholt.
        ///
        /// <b>Absender der Ablehnung ist dieser Server</b>, nicht der gemeinte
        /// Empfänger - anders als bei <c>&lt;service-unavailable/&gt;</c>, das
        /// im Namen eines Empfängers antwortet, weil der Server dort für ihn
        /// geantwortet hat. Hier gibt es keinen: Die Adresse ist keine, also
        /// hat niemand hineingesehen.
        ///
        /// <b>Kein <c>to</c> ist kein falsches.</b> Eine Stanza ohne Adresse
        /// ist an den Server gerichtet (Abschnitt 8.1.1.1), und ungerichtete
        /// Presence trägt nie eine.
        ///
        /// Auf eine Fehler-Stanza folgt kein Fehler (Abschnitt 8.3.1) -
        /// verworfen wird sie trotzdem, zustellbar ist sie ja nicht.
        /// </remarks>
        /// <returns>true, wenn die Stanza hier endet.</returns>
        private async Task<Boolean> RefuseMalformedToAsync(XMPPSession  session,
                                                           String       frame,
                                                           String       kind)
        {

            var to = Attr(frame, "to");

            if (to is null || JidUtilities.TryParse(to, out _))
                return false;

            if (Attr(frame, "type") != "error")
                await session.SendAsync(
                    JidMalformedError(kind, Attr(frame, "id"), session.FullJid));

            return true;

        }

        /// <summary>
        /// Der Fehlerrahmen zu einem <c>to</c>, das kein JID ist (RFC 6120,
        /// Abschnitt 8.3.3.8).
        /// </summary>
        /// <remarks>
        /// Eine Fassung für beide Herkünfte. Die zweite hätte sich nur in
        /// Kleinigkeiten unterschieden - und genau die wären der Unterschied
        /// gewesen, den niemand bemerkt: Ein Client, der über die Grenze eine
        /// andere Fehlerart bekommt als im eigenen Haus, hat zwei Fälle zu
        /// behandeln, wo es einen gibt.
        ///
        /// <paramref name="replyTo"/> darf fehlen: Vor dem Binding hat der
        /// Absender noch keine Adresse, und ein leeres <c>to</c> wäre
        /// schlechter als keines.
        /// </remarks>
        private String JidMalformedError(String kind, String? id, String? replyTo)

            => $"<{kind} type='error'" +
               (id is not null ? $" id='{id}'" : "") +
               $" from='{Domain}'" +
               (replyTo is not null ? $" to='{replyTo}'" : "") +
               ">" +
               "<error type='modify'>" +
               "<jid-malformed xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
               "</error>" +
               $"</{kind}>";

        private String BadRequestIq(String? id)

            => "<iq type='error'" +
               (id is not null ? $" id='{id}'" : "") +
               $" from='{Domain}'>" +
               "<error type='modify'>" +
               "<bad-request xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
               "</error></iq>";

        /// <summary>
        /// Baut ein <c>iq type='error'</c> nach RFC 6120, Abschnitt 8.3.
        /// </summary>
        /// <param name="payload">
        /// Die ursprüngliche Anfrage, die der Fehler mit zurücknimmt (RFC 6120,
        /// Abschnitt 8.3.1). Ohne sie weiss ein Frager, der mehrere gleichartige
        /// Anfragen offen hat, nur <i>dass</i> eine gescheitert ist.
        /// </param>
        /// <param name="applicationError">
        /// Der anwendungseigene Fehlerzustand als fertiges XML, oder null (RFC
        /// 6120, Abschnitt 8.3.2). Die Bedingungen der RFC sind grob: Zwei
        /// Ablehnungen aus ganz verschiedenen Gründen tragen dieselbe, und erst
        /// dieses zweite Element sagt, welcher es war.
        /// </param>
        internal String StanzaErrorIq(String?  id,
                                      String   condition,
                                      String   errorType         = "cancel",
                                      String?  text              = null,
                                      String?  payload           = null,
                                      String?  applicationError  = null)

            => $"<iq type='error' id='{id}' from='{Domain}'>" +
               (payload ?? "") +
               $"<error type='{errorType}'>" +
               $"<{condition} xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
               (text is not null
                    ? $"<text xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'>{text}</text>"
                    : "") +
               (applicationError ?? "") +
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
