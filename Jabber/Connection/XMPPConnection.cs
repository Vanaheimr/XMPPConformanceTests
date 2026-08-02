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

using System.Net.Security;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XMPP over WebSocket (RFC 7395) mit Auto-Reconnect.
///
/// Diese Klasse ist die Transport- und Protokollebene: WebSocket-I/O, SASL,
/// Resource Binding und Stanza-Routing. Die anwendungsnahe Sitzungslogik
/// (aktueller Chatpartner, offene Kontaktanfragen, zusammengesetzte
/// Operationen) liegt in <see cref="XMPPClient"/>.
///
/// Features:
/// - SCRAM-SHA-1/256 und SASL PLAIN Authentifizierung
/// - XEP-0030 Service Discovery
/// - XEP-0060 Publish-Subscribe
/// - XEP-0085 Chat State Notifications
/// - XEP-0115 Entity Capabilities
/// - XEP-0184 Message Delivery Receipts
/// - XEP-0198 Stream Management (standardmäßig deaktiviert)
/// - XEP-0199 Ping
/// - XEP-0280 Message Carbons
/// - XEP-0333 Chat Markers
/// </summary>
public sealed class XMPPConnection : IAsyncDisposable
{

    #region Data

    private string? _wsUri;
    private readonly string _defaultWsUri;
    private bool _endpointDiscovered;
    private readonly string _jid;
    private readonly string _password;
    private readonly string _username;
    private readonly string _domain;

    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger _logger;

    /// <summary>
    /// Serialisiert ausgehende Stanzas. Gesendet wird aus mehreren Richtungen
    /// gleichzeitig: Keepalive-Schleife, Auto-Receipts und Chat-Marker aus der
    /// Empfangsschleife sowie Benutzeraktionen.
    /// </summary>
    /// <remarks>
    /// Der WebSocket-Vertrag erlaubt nur einen ausstehenden Sendevorgang; ob
    /// ein Verstoß auffällt, hängt von der Implementierung ab. Auf .NET 10
    /// serialisiert ClientWebSocket intern, dort blieben 200 parallele Sends
    /// à 40 kB fehlerfrei und unbeschädigt. Andere Implementierungen (ältere
    /// Runtimes, Browser-WebSockets unter WASM) werfen dagegen
    /// InvalidOperationException. Das Lock macht die Zusicherung explizit,
    /// statt sich auf ein undokumentiertes Implementierungsdetail zu
    /// verlassen - Kosten: rund 150 ms für die genannten 200 Sends.
    /// </remarks>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>
    /// Wie lange beim Verbindungsabbau auf das Ende der Hintergrund-Schleifen
    /// gewartet wird, bevor sie aufgegeben werden.
    /// </summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Wie lange auf den WebSocket-Close-Handshake der Gegenseite gewartet
    /// wird. Ohne Grenze blockiert CloseAsync unbegrenzt, wenn der Server das
    /// Close-Frame nicht beantwortet.
    /// </summary>
    private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Namespace der Stream-Ebene (RFC 6120, Abschnitt 4.8.2).</summary>
    private const string StreamNamespace = StreamNegotiation.StreamNamespace;

    /// <summary>Namespace von XEP-0198 Stream Management.</summary>
    private const string StreamManagementNamespace = StreamManagementManager.Namespace;

    /// <summary>
    /// Wie lange die Aufbauphase auf die Antwort auf eines ihrer IQs wartet.
    /// </summary>
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// IQs, auf deren Antwort gerade jemand wartet, nach ihrer id.
    ///
    /// Ersetzt das frühere Vorgehen der Aufbauphase, bis zu zehn Rahmen selbst
    /// vom Socket zu lesen und alles zu verwerfen, was nicht nach der
    /// erwarteten Antwort aussah. Verworfen wurden dabei auch Nachrichten,
    /// Presence und Roster-Pushes; und weil "sieht aus wie" ein
    /// <c>Contains("id='roster1'")</c> auf dem Rohtext war, konnte eine
    /// Nachricht mit dieser Zeichenfolge im Text die Antwort auch ersetzen.
    /// </summary>
    private readonly Dictionary<string, TaskCompletionSource<XElement>> _pendingIqs = new();

    /// <summary>
    /// Der letzte Fehler aus <see cref="ConnectInternalAsync"/> - damit
    /// <see cref="ConnectAsync"/> ihn dem Aufrufer weiterreichen kann, statt
    /// ihn nur zu melden.
    /// </summary>
    private Exception? _lastConnectError;
    private readonly object _iqLock = new();

    /// <summary>
    /// Die Untergrenze für die SASL-Aushandlung. Gehört an die Verbindung und
    /// nicht an den einzelnen Verbindungsaufbau: Ihr Wert entsteht gerade
    /// dadurch, dass sie den Reconnect überlebt.
    /// </summary>
    private readonly SaslMechanismPolicy _saslPolicy = new();

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _keepaliveTask;

    private int _messageIdCounter;
    private int _pepCounter;
    private int _reconnectAttempts;
    private bool _intentionalDisconnect;

    /// <summary>
    /// Gesetzt, wenn der Server den Stream mit einer nicht wiederholbaren
    /// Bedingung beendet hat (RFC 6120, Abschnitt 4.9). Unterdrückt den
    /// automatischen Reconnect, der sonst denselben Fehler erneut auslösen
    /// würde. Wird bei jedem bewussten Verbindungsaufbau zurückgesetzt.
    /// </summary>
    private bool _fatalStreamError;

    #endregion

    #region Properties

    // Reconnect-Einstellungen
    public int MaxReconnectAttempts { get; set; } = 5;
    public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    // Keepalive - verhindert Inactivity Timeout vom Server
    public TimeSpan KeepaliveInterval { get; set; } = TimeSpan.FromSeconds(25);
    public bool KeepaliveEnabled { get; set; } = true;

    /// <summary>
    /// Die Priorität, die jede Presence dieses Clients trägt (RFC 6121,
    /// Abschnitt 4.7.2.3); <c>null</c> lässt das Element weg.
    /// </summary>
    /// <remarks>
    /// Sie ist die einzige Möglichkeit eines Clients zu sagen, wie sehr er
    /// gemeint ist, wenn eine Nachricht an das Konto und nicht an ihn geht.
    /// Negativ heisst: gar nicht - das Gerät bleibt gerichtet ansprechbar und
    /// hält sich aus dem Übrigen heraus. Der Server richtet sich danach
    /// (RFC 6121, Abschnitt 8.5.2.1.1), und auch die Offline-Ablage wird erst
    /// einer Resource mit nicht-negativer Priorität nachgereicht (XEP-0160).
    ///
    /// Der Bereich ist auf -128 bis +127 begrenzt; ein Wert daneben wird vom
    /// Server abgeklemmt statt abgelehnt.
    /// </remarks>
    public int? PresencePriority { get; set; }

    // XEP-0198: Stream Management. Die frühere Abschaltung wegen
    // "ejabberd-Kompatibilitätsproblemen" ging auf die fehlerhafte Zählung
    // zurück. Die ist behoben, gegen XMPPServer getestet und inzwischen gegen
    // Prosody 13 belegt: nach einem vollständigen Sitzungsaufbau melden beide
    // Seiten denselben Stand, auf den Zähler genau.
    //
    // Damit ist der Grund für den ausgeschalteten Default weggefallen. Wer
    // ihn nicht will, schaltet ihn ab - zur Laufzeit mit /sm off. Angefordert
    // wird er ohnehin nur, wenn der Server ihn ankündigt; ein Server ohne
    // XEP-0198 merkt von dieser Zeile nichts.
    public bool StreamManagementEnabled { get; set; } = true;

    /// <summary>
    /// Der schwächste SASL-Mechanismus, der noch benutzt werden darf - null
    /// verlangt nichts und überlässt die Wahl allein der Ankündigung des
    /// Servers.
    /// </summary>
    /// <remarks>
    /// Zulässig sind PLAIN, SCRAM-SHA-1 und SCRAM-SHA-256; ein anderer Name
    /// wird abgewiesen, statt lautlos gar nichts zu verlangen. Wer weiss, dass
    /// sein Server SCRAM kann, setzt das hier: Dann greift die Untergrenze
    /// schon beim allerersten Verbindungsaufbau, den
    /// <see cref="PinnedSaslMechanism"/> naturgemäss noch nicht schützen kann.
    /// </remarks>
    public string? MinimumSaslMechanism
    {
        get => _saslPolicy.Minimum;
        set => _saslPolicy.Minimum = value;
    }

    /// <summary>
    /// Der Mechanismus, über den die letzte Anmeldung gelang - und damit die
    /// Untergrenze für die nächste. Null vor der ersten.
    /// </summary>
    /// <remarks>
    /// Bietet der Server danach weniger an, kommt keine Verbindung mehr
    /// zustande. Das ist beabsichtigt: Ein Server, der SCRAM konnte und
    /// plötzlich nur noch PLAIN anbietet, ist entweder umkonfiguriert worden
    /// oder gar nicht mehr derselbe.
    /// </remarks>
    public string? PinnedSaslMechanism => _saslPolicy.Pinned;

    /// <summary>
    /// Die beim Resource Binding gewünschte Resource; null überlässt die Wahl
    /// dem Server (RFC 6120, Abschnitt 7.6).
    /// </summary>
    /// <remarks>
    /// Der Vorgabewert stammt aus der Konsolenanwendung und ist für eine
    /// Bibliothek eigentlich zu eng - zwei Nutzer im selben Prozess wünschen
    /// sich damit dieselbe Resource. Er bleibt aus Rücksicht auf bestehende
    /// Aufrufer, lässt sich aber jetzt setzen.
    /// </remarks>
    public string? Resource { get; set; } = $"console-{Environment.ProcessId}";

    /// <summary>
    /// Prüfung des Serverzertifikats bei <c>wss://</c>. Null überlässt sie dem
    /// Betriebssystem - der Server braucht dann ein Zertifikat, dem der Rechner
    /// ohnehin vertraut.
    /// </summary>
    /// <remarks>
    /// Gedacht für Zertifikate, die keine bekannte CA unterschrieben hat: ein
    /// Testserver, eine firmeneigene CA, ein angehefteter Fingerabdruck. Wer
    /// hier eine Prüfung einsetzt, die immer true liefert, hat TLS auf
    /// Verschlüsselung ohne Authentifizierung reduziert - gegen einen
    /// Mitschnitt hilft das, gegen einen Zwischenmann nicht.
    /// </remarks>
    public RemoteCertificateValidationCallback? ServerCertificateValidator { get; set; }

    // State
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string FullJid { get; private set; } = string.Empty;
    public string BareJid => JidUtilities.Bare(FullJid);
    public string Domain => _domain;

    /// <summary>
    /// Der Endpunkt, zu dem verbunden wird: der angegebene, der über XEP-0156
    /// gefundene oder der Vorgabewert - in dieser Rangfolge.
    /// </summary>
    public string WebSocketUri => _wsUri ?? _defaultWsUri;

    /// <summary>
    /// XEP-0156: Womit der Endpunkt gesucht wird, wenn der Aufrufer keinen
    /// genannt hat. Ohne Angabe wird das <c>host-meta</c> der Domain über
    /// HTTPS geladen.
    /// </summary>
    public AltConnectionsResolver? EndpointDiscovery { get; set; }
    public List<string> ServerFeatures { get; } = [];

    /// <summary>
    /// XEP-0352: Hat der Server Client State Indication angekündigt?
    /// </summary>
    public bool SupportsClientStateIndication { get; private set; }

    /// <summary>
    /// XEP-0352: Sieht gerade ein Mensch hin? Vorgabe true - ein Stream
    /// beginnt immer aktiv (Abschnitt 4.2).
    /// </summary>
    /// <remarks>
    /// Der Wert überdauert einen Verbindungsabriss, der Zustand auf dem Server
    /// nicht: Nach Abschnitt 5.2 fängt auch ein wiederaufgenommener Stream
    /// wieder aktiv an. Deshalb erklärt sich der Client nach jedem Aufbau
    /// erneut für inaktiv, solange er es ist - das Telefon liegt ja immer noch
    /// in derselben Tasche.
    /// </remarks>
    public bool ClientIsActive { get; private set; } = true;

    // Core Managers
    public Roster Roster { get; } = new();
    public ReceiptTracker Receipts { get; }
    public CarbonManager? Carbons { get; private set; }
    public PubSubManager? PubSub { get; private set; }

    // Advanced Managers (XEP-0030, 0115, 0198, 0199)
    public PingManager? Ping { get; private set; }
    public DiscoManager? Disco { get; private set; }
    public EntityCapsManager? EntityCaps { get; private set; }
    public StreamManagementManager? StreamManagement { get; private set; }

    #endregion

    #region Events

    // Events - Core
    /// <summary>
    /// Eine empfangene Nachricht - fertig zusammengesetzt.
    /// </summary>
    /// <remarks>
    /// Hier stand eine Liste einzelner Werte, die mit jeder Erweiterung länger
    /// wurde: erst fünf, mit dem Verzugsstempel acht, mit der Korrektur neun.
    /// Eine Reihe gleichartiger Zeichenketten, deren Bedeutung nur an ihrer
    /// Stellung hängt, ist eine Verwechslung, die auf ihre Gelegenheit wartet.
    ///
    /// Zusammengesetzt wird sie hier und nicht beim Aufrufer: <b>Nur hier
    /// liegt die Stanza noch vor.</b> Genau daran ist der Verzugsstempel
    /// vorbeigegangen - der Aufrufer setzte die Uhrzeit selbst und konnte gar
    /// nicht wissen, dass in der Stanza eine andere stand (siehe D59).
    /// </remarks>
    public event Action<XMPPMessage>? OnMessage;
    public event Action<string, string>? OnPresence;
    public event Action<string, ChatState>? OnChatState;
    public event Action<string, string>? OnReceiptReceived;
    public event Action<CarbonMessage>? OnCarbonMessage;
    public event Action<PubSubEvent>? OnPubSubEvent;
    public event Action<string>? OnRawXml;
    public event Action<string>? OnError;
    public event Action<string>? OnSpoofingAttempt;
    public event Action<ConnectionState, ConnectionState>? OnStateChanged;

    // Events - Advanced
    public event Action<ChatMarker>? OnChatMarker;
    public event Action<string, DiscoInfo>? OnCapsDiscovered;

    /// <summary>
    /// RFC 6120, Abschnitt 8.3: Eine Stanza wurde von der Gegenstelle
    /// abgelehnt. Der erste Parameter ist der Absender des Fehlers; er ist
    /// null, wenn der Fehler vom eigenen Server kam.
    /// </summary>
    public event Action<string?, StanzaError>? OnStanzaError;

    /// <summary>
    /// RFC 6120, Abschnitt 4.9: Der Server hat den Stream mit einem Fehler
    /// beendet. Ob ein Reconnect folgt, sagt <see cref="StreamError.IsRecoverable"/>.
    /// </summary>
    public event Action<StreamError>? OnStreamError;

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Erstellt eine neue WebSocket-basierte XMPP-Verbindung
    /// </summary>
    /// <param name="jid">Bare-JID im Format user@domain</param>
    /// <param name="password">Passwort für die SASL-Authentifizierung</param>
    /// <param name="wsUri">
    /// WebSocket-Endpunkt. Ohne Angabe wird vor dem ersten Verbinden das
    /// <c>host-meta</c> der Domain gefragt (XEP-0156); findet sich dort keiner,
    /// bleibt es bei wss://{domain}:5443/ws (ejabberd-Vorgabe).
    /// </param>
    /// <param name="LoggerFactory">Optionale Logger-Factory; ohne Angabe wird nicht geloggt</param>
    public XMPPConnection(string             jid,
                          string             password,
                          string?            wsUri           = null,
                          ILoggerFactory?    LoggerFactory   = null)
    {

        _jid       = jid;
        _password  = password;

        var parts  = jid.Split('@');
        if (parts.Length != 2)
            throw new ArgumentException("JID muss im Format 'user@domain' sein", nameof(jid));

        _username  = parts[0];
        _domain    = parts[1];

        // Getrennt gehalten: Ohne Angabe wird vor dem ersten Verbinden das
        // host-meta der Domain gefragt (XEP-0156). Wer einen Endpunkt nennt,
        // wird nicht gefragt - das XEP ist ausdrücklich der Rückfallweg, nicht
        // die erste Adresse.
        _wsUri         = wsUri;
        _defaultWsUri  = $"wss://{_domain}:5443/ws";

        _loggerFactory  = LoggerFactory;
        _logger         = CreateLogger<XMPPConnection>();

        Receipts        = new ReceiptTracker(CreateLogger<ReceiptTracker>());
        Receipts.OnReceiptReceived += (msgId, from) => OnReceiptReceived?.Invoke(from, msgId);

    }

    #endregion


    private ILogger CreateLogger<T>()
    {

        if (_loggerFactory is null)
            return NullLogger<T>.Instance;

        return _loggerFactory.CreateLogger<T>();

    }


    /// <summary>
    /// Baut die Verbindung auf und meldet sich an.
    /// </summary>
    /// <exception cref="AuthenticationException">
    /// Die Anmeldung wurde abgelehnt.
    /// </exception>
    /// <exception cref="XMPPProtocolException">
    /// Die Aushandlung ist gescheitert - etwa durch eine Zeitüberschreitung.
    /// </exception>
    /// <remarks>
    /// Ein gescheiterter Aufbau <b>wirft</b> und kehrt nicht stillschweigend
    /// zurück. Bis D31 tat er genau das: Der Fehler ging an <c>OnError</c> und
    /// an den Zustand, und wer nichts abonniert hatte, sah zwischen gelungen
    /// und gescheitert keinen Unterschied - und arbeitete auf einer Verbindung
    /// weiter, die es nicht gibt.
    ///
    /// Geworfen wird der ursprüngliche Fehler und keine Hülle darum: Ein
    /// falsches Passwort ist etwas anderes als eine Zeitüberschreitung, und der
    /// Aufrufer soll das unterscheiden können, ohne in einer Meldung zu lesen.
    ///
    /// Nur dieser Weg wirft. Der Wiederverbindungsversuch im Hintergrund läuft
    /// durch dieselbe <see cref="ConnectInternalAsync"/>, hat aber keinen
    /// Aufrufer, dem er etwas schulden könnte - er meldet weiterhin über
    /// Ereignisse. Deshalb steht die Entscheidung hier und nicht dort.
    /// </remarks>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _intentionalDisconnect = false;
        _reconnectAttempts = 0;

        // Ein bewusster Verbindungsaufbau hebt die Sperre aus einem früheren
        // Stream-Fehler auf: der Aufrufer weiss, was er tut.
        _fatalStreamError = false;

        _lastConnectError = null;

        await ConnectInternalAsync(ct);

        if (State == ConnectionState.Connected)
            return;

        // Den ursprünglichen Fehler mit seinem eigenen Stapel weiterwerfen -
        // nicht neu verpacken. Für den Aufrufer ist die Stelle interessant, an
        // der es schiefging, und nicht diese hier.
        if (_lastConnectError is not null)
            ExceptionDispatchInfo.Capture(_lastConnectError).Throw();

        // Ohne festgehaltenen Fehler bleibt nur der Befund selbst: Das kommt
        // vor, wenn die Wiederverbindungsversuche aufgebraucht sind, ohne dass
        // der letzte Versuch überhaupt begonnen hätte.
        throw new XMPPProtocolException(
                  $"Der Verbindungsaufbau zu {WebSocketUri} ist gescheitert, Zustand: {State}.");

    }

    /// <summary>
    /// Sucht den Endpunkt über XEP-0156, falls der Aufrufer keinen genannt hat.
    /// </summary>
    /// <remarks>
    /// <b>Höchstens einmal je Verbindung, auch über Wiederverbindungen
    /// hinweg.</b> Der Wiederverbindungsversuch läuft in einer Schleife; eine
    /// Abfrage je Durchgang hiesse, bei einem Server, der gerade weg ist,
    /// zwanzigmal auf eine HTTPS-Antwort zu warten, die es nicht gibt.
    ///
    /// Bleibt die Suche ohne Ergebnis, wird sie nicht wiederholt und der
    /// Vorgabewert bleibt stehen. Das ist die Rangfolge des XEPs: Die Discovery
    /// ist der Rückfallweg, und ein Rückfallweg, der selbst scheitert, darf den
    /// Verbindungsaufbau nicht aufhalten.
    /// </remarks>
    private async Task DiscoverEndpointAsync(CancellationToken ct)
    {

        if (_wsUri is not null || _endpointDiscovered)
            return;

        _endpointDiscovered = true;

        var gefunden = await (EndpointDiscovery ?? new AltConnectionsResolver()).
                                 DiscoverWebSocketAsync(_domain, ct);

        if (gefunden is not null)
        {
            _logger.LogInformation("XEP-0156: {WebSocketUri} aus dem host-meta von {Domain}",
                                   gefunden, _domain);
            _wsUri = gefunden;
        }

        else
            _logger.LogDebug("XEP-0156: kein WebSocket-Endpunkt für {Domain}, es bleibt bei {WebSocketUri}",
                             _domain, _defaultWsUri);

    }

    /// <summary>
    /// Beendet Empfangs- und Keepalive-Schleife der aktuellen Verbindung,
    /// wartet auf deren Ende und gibt CancellationTokenSource und Socket frei.
    /// </summary>
    /// <remarks>
    /// Ohne diesen Abbau überschreibt ein Reconnect die alte
    /// CancellationTokenSource, ohne sie abzubrechen: Die Schleifen der
    /// vorigen Verbindung laufen dann weiter, greifen über die Felder auf den
    /// neuen Socket zu und summieren sich mit jedem Reconnect auf.
    /// </remarks>
    private async Task ShutdownConnectionAsync()
    {

        var cts           = _cts;
        var receiveTask   = _receiveTask;
        var keepaliveTask = _keepaliveTask;
        var webSocket     = _webSocket;

        _cts           = null;
        _receiveTask   = null;
        _keepaliveTask = null;
        _webSocket     = null;

        CancelPendingIqs();

        if (cts is null && webSocket is null)
            return;

        if (cts is not null)
        {
            try
            {
                await cts.CancelAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Abbruch der Hintergrund-Tasks fehlgeschlagen (ignoriert)");
            }
        }

        var pending = new List<Task>(2);
        if (receiveTask   is not null) pending.Add(receiveTask);
        if (keepaliveTask is not null) pending.Add(keepaliveTask);

        if (pending.Count > 0)
        {
            try
            {
                // Warten, damit die alten Schleifen den neuen Socket nicht mehr anfassen.
                await Task.WhenAll(pending).WaitAsync(ShutdownTimeout);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Hintergrund-Tasks nicht innerhalb von {Timeout}s beendet",
                                 ShutdownTimeout.TotalSeconds);
            }
        }

        cts?.Dispose();
        webSocket?.Dispose();

    }

    private async Task ConnectInternalAsync(CancellationToken ct)
    {

        // Reste einer vorigen Verbindung abräumen, bevor eine neue entsteht.
        await ShutdownConnectionAsync();

        SetState(ConnectionState.Connecting);

        await DiscoverEndpointAsync(ct);

        try
        {
            // WebSocket verbinden
            var webSocket = new ClientWebSocket();
            webSocket.Options.AddSubProtocol("xmpp");  // RFC 7395

            if (ServerCertificateValidator is not null)
                webSocket.Options.RemoteCertificateValidationCallback = ServerCertificateValidator;

            _webSocket = webSocket;

            _logger.LogInformation("Verbinde zu {WebSocketUri} ...", WebSocketUri);

            // Der Endpunkt gehört in die Ausnahme, und zwar nur hier. Was der
            // Transport wirft, lautet „Unable to connect to the remote server"
            // und sagt nicht, wohin - seit XEP-0156 (D41) muss die Adresse
            // nicht einmal mehr vom Aufrufer stammen, und dann steht sie in
            // keinem Quelltext, den er lesen könnte.
            //
            // Das ist kein Rückzieher gegenüber D31: Dort geht es um den
            // *Stapel* des ursprünglichen Fehlers, und der ist hier ohne Wert
            // (er endet in ClientWebSocket.ConnectAsync). Die Ausnahme bleibt
            // als InnerException erhalten; die Aushandlungs- und
            // Anmeldefehler danach werden nicht angefasst.
            try
            {
                await webSocket.ConnectAsync(new Uri(WebSocketUri), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new XMPPProtocolException(
                          $"Der Verbindungsaufbau zu {WebSocketUri} ist gescheitert: {ex.Message}",
                          ex);
            }

            _logger.LogInformation("WebSocket verbunden");

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // ===== Aushandlung =====
            //
            // Bis zum Resource Binding liest dieser Abschnitt selbst vom
            // Socket. Das ist hier richtig: der Server hat noch keine Resource,
            // an die er etwas routen könnte, es kann also nichts anderes
            // eintreffen als die Aushandlung selbst.

            await SendAsync(OpenStream());

            var features   = await ReceiveFeaturesAsync(ct);
            var mechanisms = StreamNegotiation.SaslMechanisms(features);

            if (mechanisms.Count > 0)
            {
                _logger.LogDebug("Verfügbare SASL-Mechanismen: {Mechanisms}", string.Join(", ", mechanisms));
            }

            // SASL Auth - Präferenz: SCRAM-SHA-256 > SCRAM-SHA-1 > PLAIN
            var chosen = SaslMechanismPolicy.Strongest(mechanisms);

            if (chosen is null)
                throw new AuthenticationException(
                          mechanisms.Count > 0
                              ? $"Keine unterstützten SASL-Mechanismen. Verfügbar: {string.Join(", ", mechanisms)}"
                              : "Server bietet keine SASL-Mechanismen an. Features: " +
                                Shorten(features.ToString(), 200));

            // Die Untergrenze wird geprüft, bevor der erste Rahmen hinausgeht,
            // nicht danach: Bei PLAIN steht das Passwort in genau diesem
            // <auth/>. Wer das Downgrade erst an der Antwort bemerkt, hat es
            // dem Zwischenmann schon gegeben.
            _saslPolicy.EnsureAcceptable(chosen);

            _logger.LogInformation("{Mechanism} Authentifizierung ...", chosen);

            switch (chosen)
            {

                case SaslMechanismPolicy.ScramSha256:
                    await PerformScramAsync(SCRAMMechanism.ScramSha256, ct);
                    break;

                case SaslMechanismPolicy.ScramSha1:
                    await PerformScramAsync(SCRAMMechanism.ScramSha1, ct);
                    break;

                case SaslMechanismPolicy.Plain:
                    // PLAIN überträgt das Passwort im Klartext (nur durch TLS geschützt)
                    // und ist der schwächste hier unterstützte Mechanismus.
                    _logger.LogWarning("SASL PLAIN Authentifizierung - Server bietet kein SCRAM an");
                    await PerformSaslPlainAsync(ct);
                    break;

                // Ein Mechanismus, der in der Rangfolge steht, aber hier kein
                // Verfahren hat, ist ein Fehler in dieser Datei - und keiner,
                // der auf PLAIN zurückfallen darf.
                default:
                    throw new AuthenticationException(
                              $"Für den gewählten Mechanismus {chosen} ist kein Verfahren hinterlegt.");

            }

            // Erst jetzt, nach der gelungenen Anmeldung.
            _saslPolicy.Remember(chosen);

            // Neuen Stream öffnen nach Auth (RFC 6120, Abschnitt 6.4.6)
            await SendAsync(OpenStream());
            features = await ReceiveFeaturesAsync(ct);

            ServerFeatures.Clear();
            ServerFeatures.AddRange(StreamNegotiation.FeatureNamespaces(features));

            SupportsClientStateIndication = StreamNegotiation.OffersClientStateIndication(features);

            // XEP-0198, Abschnitt 5: der Versuch, an den früheren Stream
            // anzuknüpfen, gehört genau hierhin - nach der Anmeldung, vor dem
            // Binding. Gelingt er, gibt es keine neue Resource: die alte
            // Full-JID gilt weiter, und alles, was seit dem Abriss an sie
            // adressiert war, kommt nach.
            var wiederaufgenommen = await TryResumeAsync(features, ct);

            if (!wiederaufgenommen && StreamNegotiation.OffersBind(features))
            {
                _logger.LogDebug("Resource Binding ...");
                FullJid = await PerformBindAsync(ct);
                _logger.LogInformation("Verbunden als {FullJid}", FullJid);
            }

            // ===== Ab hier ist die Sitzung nutzbar =====
            //
            // Die Manager entstehen vor der Empfangsschleife: sobald die
            // Resource gebunden ist, darf der Server zustellen, und die erste
            // Stanza kann eintreffen, bevor die nächste Zeile läuft.

            InitialiseManagers();

            // Die Empfangsschleife bekommt ihren Socket explizit mit, damit sie
            // nach einem Reconnect nicht am neuen Socket hängt.
            _receiveTask = ReceiveLoopAsync(webSocket, _cts.Token);

            // Ein wiederaufgenommener Stream ist keine neue Sitzung: Session,
            // Stream Management, Carbons, Roster und Presence stehen alle
            // schon. Sie noch einmal zu durchlaufen wäre nicht bloss
            // überflüssig - eine zweite Presence meldete die Resource neu an,
            // und den Kontakten sähe es aus wie das Wiederkommen, das die
            // Wiederaufnahme gerade vermeiden soll.
            if (!wiederaufgenommen)
            {

                // Session (falls nötig - in RFC 6121 entfallen)
                if (StreamNegotiation.RequiresSession(features))
                    await PerformSessionAsync(ct);

                // XEP-0198: Stream Management, standardmässig an. Die Zählung ist
                // gegen Prosody 13 belegt (ProsodyStreamManagementTests); der
                // Grund für die frühere Abschaltung - eine fehlerhafte Zählung -
                // besteht nicht mehr.
                //
                // Mit Wiederaufnahme: sie kostet nichts, solange sie nicht
                // gebraucht wird, und ohne sie wirft jeder Abriss die
                // unbestätigten Stanzas weg.
                if (StreamManagementEnabled && StreamNegotiation.OffersStreamManagement(features))
                {
                    _logger.LogInformation("Aktiviere Stream Management ...");

                    if (!await StreamManagement!.NegotiateAsync(requestResume: true, SetupTimeout, ct))
                        _logger.LogWarning("Stream Management vom Server abgelehnt");
                }

                // Carbons aktivieren
                _logger.LogDebug("Aktiviere Message Carbons ...");
                await EnableCarbonsAsync(ct);

                // Roster laden
                _logger.LogDebug("Lade Roster ...");
                await RequestRosterAsync(StreamNegotiation.OffersRosterVersioning(features), ct);

                // Online gehen
                await SendPresenceAsync();

            }

            else
                await ResendUnackedAsync();

            // XEP-0352, Abschnitt 5.2: „stream resumption does not affect the
            // current CSI state, which always defaults to 'active' for new and
            // resumed streams". Der Server hat den Zustand also vergessen, das
            // Gerät liegt aber immer noch in der Tasche - deshalb hier und
            // ausserhalb des Zweigs darüber: Es gilt für den neu gebundenen
            // wie für den wiederaufgenommenen Stream.
            if (!ClientIsActive && SupportsClientStateIndication)
                await SendAsync(ClientStateIndication.InactiveXml);

            SetState(ConnectionState.Connected);
            _reconnectAttempts = 0;
            _logger.LogInformation("Online");

            // Keepalive-Loop starten (verhindert Server-Timeout)
            if (KeepaliveEnabled)
            {
                _logger.LogDebug("Starte Keepalive (Interval: {Seconds}s) ...", KeepaliveInterval.TotalSeconds);
                _keepaliveTask = KeepaliveLoopAsync(_cts.Token);
            }
        }
        catch (AuthenticationException ex)
        {
            // Auth-Fehler sind permanent - kein Reconnect sinnvoll
            _lastConnectError = ex;
            SetState(ConnectionState.Disconnected);
            _logger.LogError(ex, "Authentifizierungsfehler");
            OnError?.Invoke($"Authentifizierungsfehler: {ex.Message}");
            // KEIN Reconnect bei Auth-Fehlern!
        }
        catch (Exception ex)
        {
            _lastConnectError = ex;
            SetState(ConnectionState.Disconnected);
            _logger.LogError(ex, "Verbindungsfehler");
            OnError?.Invoke($"Verbindungsfehler: {ex.Message}");

            if (!_intentionalDisconnect)
            {
                await TryReconnectAsync(ct);
            }
        }
    }

    /// <summary>Der Stream-Kopf nach RFC 7395.</summary>
    private string OpenStream()
        => $"<open xmlns='{StreamNegotiation.FramingNamespace}' " +
           $"to='{XmlEscaping.Escape(_domain)}' version='1.0'/>";

    /// <summary>
    /// Liest den nächsten Rahmen der Aushandlung und gibt ihn geparst zurück.
    /// </summary>
    /// <param name="erwartet">
    /// Worauf gewartet wird - erscheint in der Meldung, wenn die Frist abläuft.
    /// Eine abgelaufene Frist ohne Angabe verschiebt die Suche nur: Der
    /// Aufrufer weiss dann, dass etwas nicht kam, aber nicht, was.
    /// </param>
    private async Task<XElement> ReceiveElementAsync(CancellationToken ct,
                                                     string            erwartet = "der Aushandlung")
    {

        var xml = await ReceiveStanzaAsync(ct, erwartet);

        try
        {
            return XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            throw new XMPPProtocolException(
                      $"Rahmen der Aushandlung ist kein wohlgeformtes XML: {ex.Message}", ex);
        }

    }

    /// <summary>
    /// Wartet auf die Stream-Features. Ob der Server <c>&lt;open/&gt;</c> und
    /// <c>&lt;features/&gt;</c> in einem oder in zwei Rahmen schickt, ist ihm
    /// überlassen.
    /// </summary>
    private async Task<XElement> ReceiveFeaturesAsync(CancellationToken ct)
    {

        var element = await ReceiveElementAsync(ct, "den Stream-Kopf");

        if (StreamNegotiation.IsStreamOpen(element))
            element = await ReceiveElementAsync(ct, "die Stream-Features");

        if (!StreamNegotiation.IsFeatures(element))
            throw new XMPPProtocolException(
                      $"Erwartet wurden die Stream-Features, empfangen wurde <{element.Name.LocalName}/>.");

        return element;

    }

    /// <summary>
    /// XEP-0198, Abschnitt 5: Versucht, an den früheren Stream anzuknüpfen.
    /// </summary>
    /// <remarks>
    /// Gelesen wird hier noch direkt vom Socket, wie im ganzen Abschnitt der
    /// Aushandlung: die Empfangsschleife läuft erst, wenn die Sitzung steht.
    /// Ein Umweg über sie wäre auch inhaltlich falsch - solange nicht
    /// feststeht, ob dieser Stream der alte ist, gibt es niemanden, an den
    /// eine Stanza zuzustellen wäre.
    ///
    /// Ein <c>&lt;failed/&gt;</c> ist kein Fehler, sondern der Normalfall nach
    /// einer längeren Störung. Der Aufrufer bindet dann eine neue Resource.
    /// </remarks>
    /// <returns>true, wenn der alte Stream weitergeht.</returns>
    private async Task<bool> TryResumeAsync(XElement features, CancellationToken ct)
    {

        if (!StreamManagementEnabled ||
            StreamManagement?.CanResume != true ||
            !StreamNegotiation.OffersStreamManagement(features))
            return false;

        _logger.LogInformation("Versuche, den Stream wieder aufzunehmen ...");

        await StreamManagement.ResumeAsync();

        var antwort = await ReceiveElementAsync(ct);
        var name    = antwort.Name.LocalName;

        if (name == "resumed")
        {
            StreamManagement.ProcessResumed(antwort.ToString());
            _logger.LogInformation("Stream wieder aufgenommen als {FullJid}", FullJid);
            return true;
        }

        // Alles andere als ein <resumed/> heisst: der alte Stream ist fort.
        // ProcessFailed räumt die Kennung ab und meldet, was dabei verloren
        // ging - ohne das versuchte der nächste Reconnect es wieder mit einer
        // Kennung, die der Server längst vergessen hat.
        if (name != "failed")
            _logger.LogWarning("Unerwartete Antwort auf <resume/>: <{Name}/>", name);

        // Mit dem Rahmen: ein <failed h='…'/> nennt den Stand des alten
        // Streams, und was der Server verarbeitet hat, ist nicht verloren.
        StreamManagement.ProcessFailed(antwort.ToString());

        return false;

    }

    /// <summary>
    /// Erzeugt die XEP-Manager für diese Verbindung.
    /// </summary>
    /// <remarks>
    /// Muss vor dem Start der Empfangsschleife laufen: <c>ProcessStanza</c>
    /// greift auf alle davon zu, und nach dem Resource Binding darf der Server
    /// jederzeit zustellen.
    /// </remarks>
    private void InitialiseManagers()
    {

        // XEP-0198, Abschnitt 5: dieser eine Manager überlebt den Reconnect.
        // An ihm hängen die Kennung des aufgehobenen Streams und die noch
        // unbestätigten Stanzas - würde er hier wie die übrigen neu erzeugt,
        // wäre nach einem Abriss beides fort, und die Wiederaufnahme hätte
        // nichts, woran sie anknüpfen könnte. Seinen Sitzungszustand setzt er
        // selbst zurück, sobald ein <enabled/> kommt.
        if (StreamManagement is null)
        {
            StreamManagement = new StreamManagementManager(xml => SendAsync(xml), CreateLogger<StreamManagementManager>());
            StreamManagement.OnAckReceived += count =>
                _logger.LogTrace("Stream Management: {Count} Stanzas bestätigt", count);
        }

        Carbons = new CarbonManager(BareJid);
        Carbons.OnCarbonReceived += c => OnCarbonMessage?.Invoke(c);
        Carbons.OnParseError     += msg => OnError?.Invoke($"[Carbon] {msg}");

        PubSub = new PubSubManager($"pubsub.{_domain}", CreateLogger<PubSubManager>());
        PubSub.OnEvent += e => OnPubSubEvent?.Invoke(e);

        // XEP-0199: Ping Manager
        Ping = new PingManager(xml => SendAsync(xml));
        Ping.OnPingTimeout += target => OnError?.Invoke($"Ping Timeout: {target}");

        // XEP-0030: Service Discovery
        Disco = new DiscoManager(xml => SendAsync(xml));

        // XEP-0115: Entity Capabilities
        EntityCaps = new EntityCapsManager(Disco);
        EntityCaps.OnCapsDiscovered += (from, info) => OnCapsDiscovered?.Invoke(from, info);

    }

    private static string Shorten(string text, int max)
        => text.Length <= max ? text : text[..max];

    private async Task TryReconnectAsync(CancellationToken ct)
    {
        if (_intentionalDisconnect || _reconnectAttempts >= MaxReconnectAttempts)
        {
            _logger.LogWarning("Reconnect aufgegeben nach {Attempts} Versuchen", _reconnectAttempts);
            return;
        }

        _reconnectAttempts++;

        // Exponential Backoff
        var delay = TimeSpan.FromMilliseconds(
            Math.Min(
                InitialReconnectDelay.TotalMilliseconds * Math.Pow(2, _reconnectAttempts - 1),
                MaxReconnectDelay.TotalMilliseconds
            )
        );

        SetState(ConnectionState.Reconnecting);
        _logger.LogInformation("Reconnect-Versuch {Attempt}/{Max} in {Seconds:F1}s ...",
                               _reconnectAttempts, MaxReconnectAttempts, delay.TotalSeconds);

        try
        {
            await Task.Delay(delay, ct);
            await ConnectInternalAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Abgebrochen
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconnect fehlgeschlagen");
            OnError?.Invoke($"Reconnect fehlgeschlagen: {ex.Message}");
        }
    }

    private void SetState(ConnectionState newState)
    {
        var oldState = State;
        if (oldState != newState)
        {
            State = newState;
            _logger.LogDebug("Verbindungszustand: {OldState} -> {NewState}", oldState, newState);
            OnStateChanged?.Invoke(oldState, newState);
        }
    }

    // ===== WEBSOCKET I/O =====

    /// <summary>
    /// XEP-0198, Abschnitt 5: Schickt nach einer Wiederaufnahme nach, was der
    /// alte Stream nicht mehr bestätigt bekommen hat.
    /// </summary>
    /// <remarks>
    /// Ohne Mitzählen: diese Stanzas tragen ihre Sequenznummer bereits und
    /// stehen weiter in der Warteschlange, bis der Server sie bestätigt. Wer
    /// sie beim Nachsenden erneut zählte, verschöbe seinen Ausgangszähler
    /// gegen den Empfangszähler der Gegenstelle - und ab da bestätigte jedes
    /// <c>&lt;a h='…'/&gt;</c> die falschen Stanzas.
    /// </remarks>
    private async Task ResendUnackedAsync()
    {

        var offen = StreamManagement?.GetUnackedStanzas() ?? [];

        if (offen.Count == 0)
            return;

        _logger.LogInformation("Sende {Count} unbestätigte Stanzas nach", offen.Count);

        foreach (var stanza in offen)
            await SendAsync(stanza, track: false);

        // Und danach nach einer Bestätigung fragen.
        //
        // Ohne das bleibt die Warteschlange stehen. Das <resumed h='…'/> hat
        // sie nur bis zum Stand des Servers geleert; was darüber hinaus
        // offen war, ist gerade noch einmal hinausgegangen und wartet nun auf
        // ein <a/>, das von selbst nie kommt: Der Server bestätigt, wenn er
        // gefragt wird, und der Keepalive fragt nur, wenn er eingeschaltet
        // ist. Aus einer Störung wurde so eine Warteschlange, die bis zum
        // Ende der Sitzung nicht mehr leer wird - und bei jeder weiteren
        // Wiederaufnahme noch einmal komplett hinausging.
        await StreamManagement!.RequestAckAsync();

    }

    private async Task SendAsync(string xml, bool track = true)
    {

        // RFC 7395, Abschnitt 3.3.3: über WebSocket gibt es kein umschliessendes
        // <stream:stream>, von dem eine Stanza ihren Namensraum erben könnte -
        // sie muss ihn selbst tragen. Hier und nicht an den rund 25 Aufrufern,
        // aus demselben Grund, aus dem auch gezählt wird: das ist die einzige
        // Stelle, durch die jeder ausgehende Rahmen läuft.
        xml = StanzaNamespace.Apply(xml, StanzaNamespace.Client);

        // Socket lokal festhalten: das Feld kann während eines Reconnects
        // ausgetauscht werden, während wir noch auf das Lock warten.
        var webSocket = _webSocket;

        if (webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket nicht verbunden");

        var bytes = Encoding.UTF8.GetBytes(xml);
        var token = _cts?.Token ?? CancellationToken.None;

        await _sendLock.WaitAsync(token);

        try
        {

            // Nach dem Warten erneut prüfen - die Verbindung kann inzwischen
            // geschlossen worden sein.
            if (webSocket.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket nicht verbunden");

            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, token);

            // XEP-0198: hier ist die einzige Stelle, durch die jede ausgehende
            // Stanza läuft - deshalb wird hier gezählt und nicht an den rund
            // 25 Aufrufern. Erst nach dem erfolgreichen Senden, damit eine
            // fehlgeschlagene Stanza den Zähler nicht dauerhaft verschiebt,
            // und noch unter dem Sende-Lock, damit die Sequenznummern der
            // Reihenfolge auf der Leitung entsprechen.
            if (track)
                StreamManagement?.TrackOutgoing(xml);

        }
        finally
        {
            _sendLock.Release();
        }

        _logger.LogTrace(">>> {Xml}", xml);
        OnRawXml?.Invoke($">>> {xml}");

    }

    /// <summary>
    /// Sendet ein IQ und wartet auf die Antwort mit derselben id.
    /// </summary>
    /// <remarks>
    /// Dasselbe Verfahren, das <see cref="DiscoManager"/> und
    /// <see cref="PingManager"/> schon benutzen: die Antwort kommt über die
    /// Empfangsschleife herein und wird über ihre id zugeordnet, statt dass der
    /// Wartende selbst vom Socket liest.
    /// </remarks>
    /// <returns>Die Antwort, oder null bei Zeitüberschreitung.</returns>
    private async Task<XElement?> SendIqAsync(string             id,
                                              string             xml,
                                              CancellationToken  ct)
    {

        // RunContinuationsAsynchronously: die Antwort wird im Thread der
        // Empfangsschleife abgeliefert; ohne das liefe der wartende Aufbau dort
        // weiter und hielte das Lesen der nächsten Stanzas auf.
        var tcs = new TaskCompletionSource<XElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_iqLock)
            _pendingIqs[id] = tcs;

        try
        {

            await SendAsync(xml);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(SetupTimeout);

            return await tcs.Task.WaitAsync(cts.Token);

        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Keine Antwort auf IQ '{Id}' innerhalb von {Seconds}s",
                               id, SetupTimeout.TotalSeconds);
            return null;
        }
        finally
        {
            lock (_iqLock)
                _pendingIqs.Remove(id);
        }

    }

    /// <summary>
    /// Liefert eine IQ-Antwort an den Wartenden aus, falls es einen gibt.
    /// </summary>
    private bool TryCompleteIq(string id, XElement element)
    {

        TaskCompletionSource<XElement>? tcs;

        lock (_iqLock)
        {
            if (!_pendingIqs.TryGetValue(id, out tcs))
                return false;

            _pendingIqs.Remove(id);
        }

        return tcs.TrySetResult(element);

    }

    /// <summary>
    /// Bricht alle offenen IQ-Anfragen ab. Ohne das wartete ein Reconnect erst
    /// deren Zeitüberschreitung ab, obwohl die Antwort über den alten Socket
    /// gar nicht mehr kommen kann.
    /// </summary>
    private void CancelPendingIqs()
    {

        List<TaskCompletionSource<XElement>> pending;

        lock (_iqLock)
        {
            pending = [.. _pendingIqs.Values];
            _pendingIqs.Clear();
        }

        foreach (var tcs in pending)
            tcs.TrySetCanceled();

    }

    private async Task<string> ReceiveStanzaAsync(CancellationToken ct, string erwartet = "der Aushandlung")
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();

        // Eine Frist für den Schritt, nicht für den einzelnen Lesevorgang: Ein
        // Rahmen, der in Stücken ankommt, darf zusammen nicht länger brauchen
        // als einer am Stück.
        //
        // Ohne sie wartete die Aushandlung unbegrenzt. Ein Fehler kommt an, ein
        // geschlossener Socket kommt an - Schweigen kommt nicht an, und dann
        // kehrte ConnectAsync nie zurück. Aufgefallen ist das an fünf
        // Mutationen quer durch D25 bis D29, die alle den Lauf haengen liessen
        // statt ihn scheitern zu lassen: ein Ergebnis, das keines ist.
        //
        // Das Resource Binding war nie betroffen - SendIqAsync hat seine Frist
        // seit jeher. Betroffen war alles davor, was hier liest.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(SetupTimeout);

        WebSocketReceiveResult result;
        do
        {

            try
            {
                result = await _webSocket!.ReceiveAsync(buffer, cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new XMPPProtocolException(
                          $"Zeitüberschreitung in der Aushandlung: Auf {erwartet} kam " +
                          $"innerhalb von {SetupTimeout.TotalSeconds:0} Sekunden keine Antwort.");
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new IOException("WebSocket geschlossen");
            }

            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        while (!result.EndOfMessage);

        var xml = sb.ToString();
        _logger.LogTrace("<<< {Xml}", xml);
        OnRawXml?.Invoke($"<<< {xml}");
        NoteInboundStanza(xml);
        return xml;
    }

    /// <summary>
    /// XEP-0198: zählt eine empfangene Stanza.
    ///
    /// Sitzt bewusst auf beiden Empfangspfaden. Zu zählen gibt es auf dem
    /// direkten Pfad heute zwar nichts mehr - <see cref="ReceiveStanzaAsync"/>
    /// liest nur noch die Aushandlung, und die endet vor
    /// <c>&lt;enabled/&gt;</c> -, aber die Zusicherung "jede empfangene Stanza
    /// kommt hier vorbei" soll nicht davon abhängen, wo die Grenze zwischen
    /// den beiden Pfaden gerade verläuft.
    /// </summary>
    private void NoteInboundStanza(string xml)
    {
        StreamManagement?.TrackIncoming(xml);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket webSocket, CancellationToken ct)
    {
        var buffer = new byte[8192];

        try
        {
            while (!ct.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult result;

                do
                {
                    result = await webSocket.ReceiveAsync(buffer, ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning("Server hat Verbindung geschlossen");
                        break;
                    }

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                var stanza = sb.ToString();
                if (!string.IsNullOrEmpty(stanza))
                {
                    _logger.LogTrace("<<< {Xml}", stanza);
                    OnRawXml?.Invoke($"<<< {stanza}");
                    NoteInboundStanza(stanza);
                    ProcessStanza(stanza);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal
        }
        catch (WebSocketException ex)
        {
            _logger.LogError(ex, "WebSocket-Fehler");
            OnError?.Invoke($"WebSocket-Fehler: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Empfangsfehler");
            OnError?.Invoke($"Empfangsfehler: {ex.Message}");
        }

        // Eine Schleife einer bereits abgelösten Verbindung darf keinen
        // Reconnect mehr auslösen.
        if (!ReferenceEquals(webSocket, _webSocket))
        {
            _logger.LogDebug("Empfangsschleife einer abgelösten Verbindung beendet");
            return;
        }

        // Verbindung verloren - Reconnect versuchen.
        // Bewusst über Task.Run entkoppelt: der Reconnect räumt via
        // ShutdownConnectionAsync unter anderem diese Schleife ab und würde
        // sonst auf sich selbst warten.
        if (_fatalStreamError)
        {
            _logger.LogDebug("Kein Reconnect nach nicht wiederholbarem Stream-Fehler");
            SetState(ConnectionState.Disconnected);
            return;
        }

        if (!_intentionalDisconnect && State == ConnectionState.Connected)
        {
            SetState(ConnectionState.Disconnected);
            _ = Task.Run(() => TryReconnectAsync(CancellationToken.None));
        }
    }

    private async Task KeepaliveLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Keepalive-Loop gestartet (Interval: {Seconds}s)", KeepaliveInterval.TotalSeconds);

        try
        {
            while (!ct.IsCancellationRequested && State == ConnectionState.Connected)
            {
                await Task.Delay(KeepaliveInterval, ct);

                if (State != ConnectionState.Connected)
                {
                    _logger.LogDebug("Keepalive-Abbruch - nicht mehr verbunden");
                    break;
                }

                // Bevorzugt: Stream Management <r/> (weniger Overhead)
                if (StreamManagement?.IsEnabled == true)
                {
                    _logger.LogTrace("Keepalive: sende Stream-Management <r/>");
                    await StreamManagement.RequestAckAsync();
                }
                // Fallback: XEP-0199 Ping
                else if (Ping != null)
                {
                    _logger.LogTrace("Keepalive: sende Ping");
                    var rtt = await Ping.PingAsync(ct: ct);
                    if (rtt.HasValue)
                        _logger.LogTrace("Keepalive: Pong nach {Milliseconds:F0}ms", rtt.Value.TotalMilliseconds);
                    else
                        _logger.LogWarning("Keepalive: Ping Timeout");
                }
                else
                {
                    _logger.LogWarning("Keepalive: weder Stream Management noch Ping verfügbar");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Keepalive-Loop beendet (cancelled)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Keepalive-Fehler");
            OnError?.Invoke($"Keepalive-Fehler: {ex.Message}");
        }
    }

    // ===== STANZA PROCESSING =====

    /// <summary>
    /// Zerlegt einen empfangenen Rahmen und leitet ihn weiter.
    ///
    /// Der Rahmen wird genau einmal geparst; die Weiterverarbeitung arbeitet
    /// auf dem <see cref="XElement"/>. Die frühere Erkennung über
    /// <c>StartsWith</c> scheiterte an gültigen Schreibweisen: ein an
    /// <c>jabber:client</c> gebundenes Präfix (<c>&lt;c:message/&gt;</c>) liess
    /// die Stanza komplett durchfallen, und <c>StartsWith("&lt;a")</c> traf
    /// auch <c>&lt;auth/&gt;</c>.
    ///
    /// Der Rohtext wird zusätzlich durchgereicht, weil die XEP-Manager ihn noch
    /// erwarten - deren Umstellung steht aus.
    /// </summary>
    private void ProcessStanza(string stanza)
    {
        try
        {
            // XEP-0198: das Mitzählen passiert bewusst nicht hier, sondern in
            // NoteInboundStanza auf beiden Empfangspfaden.

            XElement element;

            try
            {
                element = XElement.Parse(stanza, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException ex)
            {
                // Nicht wohlgeformt - in der Praxis vor allem ein
                // <stream:error/>, dessen Präfix der Server nur auf dem
                // Stream-Root deklariert hat. Dafür bleibt der Textpfad.
                _logger.LogWarning("Stanza ist kein wohlgeformtes XML: {Reason}", ex.Message);

                if (StreamError.TryParse(stanza, out var rawStreamError) && rawStreamError is not null)
                    ProcessStreamError(rawStreamError);
                else
                    OnError?.Invoke($"Stanza ist kein wohlgeformtes XML: {ex.Message}");

                return;
            }

            var name = element.Name.LocalName;
            var ns   = element.Name.NamespaceName;

            switch (name)
            {

                case "message":
                    ProcessMessage(element);
                    return;

                case "presence":
                    ProcessPresence(element);
                    return;

                case "iq":
                    ProcessIq(element);
                    return;

                case "close":
                    _logger.LogWarning("Stream vom Server geschlossen");
                    OnError?.Invoke("Stream vom Server geschlossen");
                    return;

                // RFC 6120, Abschnitt 4.9: Stream-Fehler. Danach ist der Stream tot.
                case "error" when ns == StreamNamespace:
                    if (StreamError.TryParse(stanza, out var streamError) && streamError is not null)
                        ProcessStreamError(streamError);
                    return;

                // XEP-0198: Stream Management. Jetzt über den Namespace geprüft
                // statt über den Anfangsbuchstaben.
                case "enabled" when ns == StreamManagementNamespace:
                    StreamManagement?.ProcessEnabled(stanza);
                    return;

                case "a" when ns == StreamManagementNamespace:
                    StreamManagement?.ProcessAck(stanza);
                    return;

                case "r" when ns == StreamManagementNamespace:
                    _ = StreamManagement?.ProcessRequestAsync();
                    return;

                case "resumed" when ns == StreamManagementNamespace:
                    StreamManagement?.ProcessResumed(stanza);
                    return;

                case "failed" when ns == StreamManagementNamespace:
                    StreamManagement?.ProcessFailed(stanza);
                    return;

                default:
                    _logger.LogDebug("Unbehandelter Rahmen <{Name}/> aus {Namespace}", name, ns);
                    return;

            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stanza-Verarbeitung fehlgeschlagen");
            OnError?.Invoke($"Stanza-Verarbeitung fehlgeschlagen: {ex.Message}");
        }
    }

    /// <summary>
    /// RFC 6120, Abschnitt 4.9: Nach einem Stream-Fehler schliesst der Server
    /// den Stream unmittelbar. Ein Reconnect lohnt nur bei vorübergehenden
    /// Bedingungen - bei allem anderen liefe er in dieselbe Ablehnung und
    /// erzeugte eine Schleife.
    /// </summary>
    private void ProcessStreamError(StreamError streamError)
    {

        if (streamError.IsRecoverable)
            _logger.LogWarning("Stream-Fehler vom Server: {Error} - Reconnect wird versucht", streamError);

        else
        {
            _logger.LogError("Stream-Fehler vom Server: {Error} - kein Reconnect", streamError);

            // Verhindert, dass die Empfangsschleife gleich einen Reconnect
            // anstösst; der Fehler wiederholte sich nur.
            _fatalStreamError = true;
        }

        OnStreamError?.Invoke(streamError);
        OnError?.Invoke($"Stream-Fehler: {streamError}");

    }

    private void ProcessMessage(XElement element)
    {
        var from = element.Attr("from") ?? "unknown";
        var to = element.Attr("to") ?? FullJid;
        var msgId = element.Attr("id");

        // RFC 6120, Abschnitt 8.3: Eine Fehler-Stanza trägt keine Nutzlast,
        // sondern die Begründung. Früher lief sie als normale Nachricht durch.
        if (element.Attr("type") == "error")
        {

            var parsed = StanzaError.TryParse(element.ToString(), out var stanzaError) && stanzaError is not null
                             ? stanzaError
                             : new StanzaError(StanzaErrorType.Cancel, "undefined-condition");

            _logger.LogDebug("Nachricht an {From} abgelehnt: {Error}", from, parsed);

            OnStanzaError?.Invoke(from, parsed);
            return;

        }

        // XEP-0384: verschlüsselt eingetroffen.
        //
        // Vor allem anderen, denn was hier steht, ist von aussen nicht zu
        // sehen: Die Stanza hat keinen <body/>, und jede Auswertung danach
        // hielte sie für leer.
        if (element.Attr("from") is String von && TryProcessEncrypted(element, von))
            return;

        // XEP-0060/XEP-0163: eine PubSub-Benachrichtigung.
        //
        // Sie kommt in der Praxis fast immer als <message type='headline'/> -
        // und wurde bis hierher nur in ProcessIq behandelt, wo sie so gut wie
        // nie ankommt. Der Kommentar dort behauptete seit jeher „kann als
        // message oder iq kommen"; die message-Hälfte gab es nicht. Aufgefallen
        // ist es erst, als mit OMEMO zum ersten Mal jemand auf eine
        // Benachrichtigung angewiesen war - dieselbe halb verdrahtete Ecke wie
        // in D38.
        if (element.Child(PubSubManager.EventNamespace, "event") is not null &&
            element.Attr("from") is not null)
        {

            PubSub?.ProcessEvent(element, from, PubSub.PubSubService);

            _ = ProcessPepEventAsync(element, from);

            return;

        }

        // XEP-0280 und XEP-0384 zusammen: Ein Carbon bringt die Nachricht
        // eines anderen eigenen Geräts mit, und die kann verschlüsselt sein.
        //
        // Der eingepackten Nachricht gilt dabei alles, was der äusseren gälte -
        // ausser dem Absender: Steht die eigene Adresse aussen, kommt die
        // Nachricht vom eigenen Konto, und der wirkliche Absender steht innen.
        //
        // Ohne diesen Zweig sieht das eigene zweite Gerät nicht, was das erste
        // geschrieben hat: Der Schlüsseleintrag ist da, die Nachricht kommt an -
        // und niemand sieht sie an, weil sie im <forwarded/> steckt.
        if (Omemo is not null &&
            element.HasNamespace(CarbonManager.Namespace) &&
            element.Descendants()
                   .FirstOrDefault(e => e.Name.LocalName     == "forwarded" &&
                                        e.Name.NamespaceName == "urn:xmpp:forward:0")
                  ?.Elements()
                   .FirstOrDefault(e => e.Name.LocalName == "message") is XElement eingepackt &&
            (eingepackt.Attr("from") ?? eingepackt.Attr("to")) is String innererAbsender &&
            TryProcessEncrypted(eingepackt, innererAbsender))
        {
            return;
        }

        // XEP-0280: Carbon Check
        if (element.HasNamespace(CarbonManager.Namespace))
        {
            if (Carbons != null)
            {
                var result = Carbons.ProcessCarbon(element, from);

                switch (result)
                {
                    case CarbonResult.Success:
                        return; // Carbon wurde verarbeitet

                    case CarbonResult.SpoofingDetected:
                        _logger.LogWarning("Carbon-Spoofing von {From}", from);
                        OnSpoofingAttempt?.Invoke($"Carbon-Spoofing von {from}");
                        return;

                    case CarbonResult.ParseError:
                        _logger.LogError("Carbon-Parse-Fehler von {From}", from);
                        OnError?.Invoke($"Carbon-Parse-Fehler von {from}");
                        return;

                    case CarbonResult.NotACarbon:
                        // Kein Carbon, weiter verarbeiten als normale Nachricht
                        break;
                }
            }
        }

        // XEP-0333: Chat Markers
        var chatMarker = ChatMarkers.Parse(element, from);
        if (chatMarker != null)
        {
            OnChatMarker?.Invoke(chatMarker);
            return;
        }

        // XEP-0184: Receipt
        var receiptId = ReceiptBuilder.ExtractReceiptId(element);
        if (receiptId != null)
        {
            if (!Receipts.ProcessReceipt(receiptId, from))
                OnSpoofingAttempt?.Invoke($"Receipt-Spoofing: {receiptId} von {from}");
            return;
        }

        // XEP-0085: Chat State
        var chatState = ChatStateExtensions.ParseChatState(element);
        if (chatState.HasValue)
        {
            OnChatState?.Invoke(from, chatState.Value);
        }

        // Normale Nachricht. Nur direkte Kinder: eine weitergeleitete Nachricht
        // in <forwarded/> bringt ihren eigenen <body/> mit.
        var body = element.ChildValue("body");
        if (!string.IsNullOrEmpty(body))
        {

            var messageType = MessageTypeExtensions.Parse(element.Attr("type"));

            // XEP-0203: Wurde sie aufgehoben, gilt ihre eigene Zeit und nicht
            // die des Empfangs. Der Stempel steht nur an der äusseren Stanza -
            // deshalb hier und nicht im Carbon-Zweig, der seine eigene innere
            // Nachricht mitbringt.
            var empfangen  = DateTime.Now;
            var geschrieben = DelayedDelivery.TryRead(element, out var stempel, out var aufgehobenVon)
                                  ? stempel.ToLocalTime().DateTime
                                  : empfangen;

            OnMessage?.Invoke(new XMPPMessage(from,
                                              to,
                                              body,
                                              msgId,
                                              geschrieben,
                                              messageType,
                                              empfangen,
                                              aufgehobenVon,
                                              MessageCorrection.ReplacedId(element)));

            // Von selbst geantwortet wird nur, wo eine Antwort hingehört.
            // Einem Zuruf ist nicht zu quittieren, und in einen Raum schon gar
            // nicht - dort bekämen alle Anwesenden die Quittung zu sehen.
            if (!messageType.ExpectsAReply())
                return;

            // Auto-Receipt (XEP-0184)
            if (ReceiptBuilder.HasReceiptRequest(element) && msgId != null)
            {
                _ = SendReceiptAsync(from, msgId);
            }

            // Auto-Received Marker (XEP-0333)
            if (ChatMarkers.IsMarkable(element) && msgId != null)
            {
                _ = SendChatMarkerAsync(from, msgId, ChatMarkerType.Received);
            }
        }
    }

    private void ProcessPresence(XElement element)
    {
        var from = element.Attr("from") ?? "unknown";
        var type = element.Attr("type") ?? "available";

        // RFC 6120, Abschnitt 8.3: 'error' ist kein Präsenzzustand. Früher
        // wanderte er über UpdatePresence in den Roster, wo der Kontakt dann
        // als im Zustand "error" geführt wurde.
        if (type == "error")
        {

            var parsed = StanzaError.TryParse(element.ToString(), out var stanzaError) && stanzaError is not null
                             ? stanzaError
                             : new StanzaError(StanzaErrorType.Cancel, "undefined-condition");

            _logger.LogDebug("Presence von/an {From} abgelehnt: {Error}", from, parsed);

            OnStanzaError?.Invoke(from, parsed);
            return;

        }

        if (type == "subscribe")
        {
            Roster.RaiseSubscriptionRequest(from, element.ChildValue("status") ?? "");
        }

        // RFC 6121, Abschnitt 3: Zustandsänderungen, keine Anwesenheit. Sie
        // liefen früher durch UpdatePresence, und weil dort alles ohne
        // 'unavailable' als anwesend gilt, machte ausgerechnet ein
        // <presence type='unsubscribed'/> den Kontakt online.
        else if (type is "subscribed" or "unsubscribed" or "unsubscribe")
        {
            Roster.ProcessSubscriptionChange(from, type);
        }

        else
        {
            var show = element.ChildValue("show");
            var status = element.ChildValue("status");
            Roster.UpdatePresence(from, type, show, status);

            // XEP-0115: Entity Capabilities
            // WICHTIG: Eigene Presences überspringen - wir kennen unsere Caps bereits!
            // Sonst Query-Loop an uns selbst → Server-Error → Disconnect
            var fromBareJid = JidUtilities.Bare(from);
            var isOwnPresence = fromBareJid.Equals(BareJid, StringComparison.OrdinalIgnoreCase);

            if (!isOwnPresence && (type == "available" || string.IsNullOrEmpty(type)))
            {
                var caps = EntityCapsManager.ParseCaps(element);
                if (caps.HasValue && EntityCaps != null)
                {
                    // Query an Full-JID (für korrekte Resource), nicht Bare-JID
                    // Server routet die Antwort korrekt
                    //
                    // Das hash-Attribut geht mit: Ohne es lässt sich der
                    // ver-Wert nicht nachrechnen, und was sich nicht
                    // nachrechnen lässt, wird nicht abgelegt.
                    _ = EntityCaps.ProcessCapsAsync(from,
                                                    caps.Value.Node,
                                                    caps.Value.Ver,
                                                    caps.Value.Hash);
                }
            }
        }

        OnPresence?.Invoke(from, type);
    }

    private void ProcessIq(XElement element)
    {
        var type = element.Attr("type");
        var id = element.Attr("id");
        var from = element.Attr("from");

        // RFC 6120, Abschnitt 8.2.3, Regel 2: Ohne einen der vier vorgesehenen
        // Werte ist diese Stanza weder Frage noch Antwort - hier trifft die
        // Regel den Client in der Rolle "the recipient".
        //
        // Ganz vorn, weil jede Zeile darunter den Typ schon voraussetzt: Die
        // Zuordnung zu einer offenen Frage nimmt nur result und error, und der
        // Fallback am Ende fragt nach get oder set. Ein fünfter Wert fiel damit
        // stillschweigend hinten heraus.
        if (!IqTypes.IsKnown(type))
        {
            RefuseMalformedIq(id, from);
            return;
        }

        // Wartet jemand auf genau diese Antwort? Die Zuordnung über die id geht
        // allem anderen vor - auch dem Fehlerpfad, denn für den Wartenden ist
        // ein 'error' genauso eine Antwort wie ein 'result'.
        if (id is not null && type is "result" or "error" && TryCompleteIq(id, element))
            return;

        // RFC 6120, Abschnitt 8.3: Ein iq 'error' ist keine Antwort mit Inhalt,
        // sondern eine Ablehnung. Früher lief er durch dieselben Handler wie ein
        // 'result' - ein abgelehnter Ping wurde damit als gemessene Laufzeit
        // gewertet und eine abgelehnte disco-Abfrage als leeres Ergebnis.
        if (type == "error")
        {

            var parsed = StanzaError.TryParse(element.ToString(), out var stanzaError) && stanzaError is not null
                             ? stanzaError
                             : new StanzaError(StanzaErrorType.Cancel, "undefined-condition");

            _logger.LogDebug("Stanza-Fehler auf IQ '{Id}' von {From}: {Error}",
                             id ?? "(ohne id)", from ?? "(Server)", parsed);

            if (id != null)
            {
                if (id.StartsWith("ping-"))
                    Ping?.ProcessError(id, parsed);

                else if (id.StartsWith("disco-info-") || id.StartsWith("disco-items-"))
                    Disco?.ProcessError(id, parsed);
            }

            OnStanzaError?.Invoke(from, parsed);
            return;

        }

        // IQ Result für pending queries
        if (type == "result")
        {
            if (id != null)
            {
                // XEP-0199: Ping Antwort
                if (id.StartsWith("ping-"))
                {
                    Ping?.ProcessPong(id);
                    return;
                }

                // XEP-0030: Disco Info Antwort
                if (id.StartsWith("disco-info-") && from != null)
                {
                    Disco?.ProcessInfoResult(id, element, from);
                    return;
                }

                // XEP-0030: Disco Items Antwort
                if (id.StartsWith("disco-items-") && from != null)
                {
                    Disco?.ProcessItemsResult(id, element, from);
                    return;
                }
            }
        }

        // IQ Get - Anfragen
        //
        // Kein 'from' bedeutet nicht "nicht beantwortbar": nach RFC 6120,
        // Abschnitt 8.1.1.1 kommt die Anfrage dann vom eigenen Server. Früher
        // wurden solche Anfragen still verworfen; jetzt antworten die Manager
        // ohne 'to'. Ist der zuständige Manager noch nicht initialisiert, fällt
        // die Anfrage bewusst durch bis zum <service-unavailable/> unten -
        // das ist die ehrlichere Antwort als Schweigen.
        if (type == "get" && id != null)
        {
            // XEP-0199: Ping Anfrage
            if (PingManager.IsPing(element) && Ping is not null)
            {
                _ = Ping.RespondAsync(id, from);
                return;
            }

            // XEP-0030: Disco Info Anfrage
            if (element.Child(DiscoManager.InfoNamespace, "query") is XElement discoQuery && Disco is not null)
            {

                var node = discoQuery.Attr("node");

                // XEP-0030, Abschnitt 3.2: Das 'node' der Frage gehört in die
                // Antwort - und beantwortet wird nur, was diese Entity auch
                // bezeichnet. Ohne diese Unterscheidung bekäme jeder erdachte
                // Node die volle Merkmalsliste, und diese Seite behauptete
                // damit, jeden davon zu führen.
                if (node is not null && EntityCaps?.IsOwnNode(node) != true)
                    RefuseUnknownNode(id, from, node);

                else
                    _ = Disco.RespondInfoAsync(id, from, node);

                return;

            }

            // XEP-0030, Abschnitt 4: Disco Items Anfrage
            //
            // Ein 'node' ist hier ein Ast im Baum der Untereinheiten, nicht der
            // Caps-Node aus XEP-0115. Dieser Client hat keinen einzigen, und
            // eine leere Liste wäre die falsche Antwort: Sie hiesse „diesen
            // Zweig gibt es, er ist leer" statt „diesen Zweig gibt es nicht".
            if (element.Child(DiscoManager.ItemsNamespace, "query") is XElement itemsQuery && Disco is not null)
            {

                var node = itemsQuery.Attr("node");

                if (node is not null)
                    RefuseUnknownNode(id, from, node, DiscoManager.ItemsNamespace);

                else
                    _ = Disco.RespondItemsAsync(id, from);

                return;

            }
        }

        // IQ Set
        if (type == "set")
        {
            // Roster-Push
            if (element.Child(RosterStanzaBuilder.Namespace, "query") is not null)
            {
                // RFC 6121, Abschnitt 2.1.6: Ein Roster-Push darf nur
                // akzeptiert werden, wenn er kein 'from' trägt (dann kommt er
                // implizit vom eigenen Konto) oder das 'from' dem eigenen
                // Bare-JID entspricht. Ohne diese Prüfung könnte jeder
                // Absender den lokalen Roster manipulieren.
                if (!IsAuthorizedRosterPush(from))
                {
                    _logger.LogWarning("Roster-Push von nicht autorisiertem Absender {From} verworfen", from);
                    OnSpoofingAttempt?.Invoke($"Roster-Push-Spoofing von {from}");

                    // Bewusst ohne Antwort. RFC 6121, Abschnitt 2.1.6 erlaubt
                    // das ausdrücklich: der Client darf "refuse to return a
                    // stanza error at all (the latter behavior overrides a
                    // MUST-level requirement from [XMPP-CORE] for the purpose
                    // of preventing a presence leak)". Eine Antwort würde dem
                    // Absender bestätigen, dass dieses Konto online ist.
                    return;
                }

                ProcessRosterPush(element);
                _ = SendAsync($"<iq type='result' id='{id}'/>");
                return;
            }
        }

        // PubSub Event (kann als message oder iq kommen)
        if (element.Child(PubSubManager.EventNamespace, "event") is not null && from != null)
        {
            PubSub?.ProcessEvent(element, from, PubSub.PubSubService);

            // XEP-0384, Abschnitt 5.2: Die Geräteliste kommt über denselben
            // Weg, verlangt aber eine Antwort - fehlt das eigene Gerät darin,
            // muss es sich wieder eintragen.
            _ = ProcessPepEventAsync(element, from);

            // Kommt das Event als iq set statt als message, ist es eine
            // Anfrage und braucht nach Abschnitt 8.2.3 ein Ergebnis.
            if (type is "get" or "set" && id != null)
                _ = SendAsync($"<iq type='result' id='{XmlEscaping.Escape(id)}' to='{XmlEscaping.Escape(from)}'/>");

            return;
        }

        // RFC 6120, Abschnitt 8.2.3: Auf ein iq 'get' oder 'set' MUSS eine
        // Antwort folgen. Alles, was oben niemand beansprucht hat, wird hier
        // abschliessend beantwortet.
        if (type is "get" or "set")
            RespondUnhandledIq(id, from);
    }

    /// <summary>
    /// Weist eine IQ-Stanza zurück, deren <c>type</c> fehlt oder keiner der
    /// vier vorgesehenen Werte ist (RFC 6120, Abschnitt 8.2.3, Regel 2).
    /// </summary>
    /// <remarks>
    /// <c>modify</c> und nicht <c>cancel</c>: Abschnitt 8.3.3.1 sieht es für
    /// <c>&lt;bad-request/&gt;</c> so vor, und die Art ist eine Auskunft -
    /// richtig gestellt kann der Absender es noch einmal versuchen.
    ///
    /// Anders als <see cref="RespondUnhandledIq"/> geht diese Antwort auch ohne
    /// <c>id</c> hinaus. Dort wäre sie eine Antwort auf eine Frage, die sich
    /// ohne <c>id</c> keiner zuordnen lässt und deshalb niemandem nützt; hier
    /// sagt sie etwas über die Stanza selbst - dass ihre Form nicht stimmt.
    /// Zumal die fehlende <c>id</c> nach Regel 1 selbst dazugehört. Was sie
    /// nicht bekommt, ist ein leeres <c>id=''</c>: Das gehört zu keiner Frage
    /// und sähe aus, als gehörte es zu einer.
    /// </remarks>
    private void RefuseMalformedIq(string? id, string? from)
    {

        _logger.LogDebug("IQ mit unbrauchbarem 'type' von {From} mit <bad-request/> beantwortet",
                         from ?? "(Server)");

        // Ohne 'from' kam die Stanza vom eigenen Server; die Antwort geht dann
        // ohne 'to' implizit dorthin zurück (Abschnitt 8.1.1.1).
        var idAttr  = id   != null ? $" id='{XmlEscaping.Escape(id)}'"   : "";
        var toAttr  = from != null ? $" to='{XmlEscaping.Escape(from)}'" : "";

        _ = SendAsync($"<iq type='error'{idAttr}{toAttr}>" +
                       "<error type='modify'>" +
                       "<bad-request xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                       "</error></iq>");

    }

    /// <summary>
    /// Beantwortet eine disco-Anfrage nach einem Node, den es hier nicht gibt,
    /// mit <c>&lt;item-not-found/&gt;</c>.
    /// </summary>
    /// <param name="ns">
    /// Der Namensraum der Anfrage - disco#info oder disco#items. Die
    /// zurückgenommene Anfrage muss die gestellte sein; ein Fehler, der die
    /// falsche Frage nennt, ist schlechter als einer ohne.
    /// </param>
    /// <remarks>
    /// XEP-0030, Abschnitt 7 verlangt „an appropriate error" und lässt die
    /// Wahl; <c>item-not-found</c> ist die Auskunft, um die es geht: Die
    /// Adresse stimmt, der Node nicht.
    ///
    /// Der Fehler trägt die ursprüngliche Anfrage samt <c>node</c> zurück
    /// (RFC 6120, Abschnitt 8.3.1). Das ist hier mehr als Form: Ein Frager, der
    /// mehrere Nodes derselben Entity abfragt, erfährt sonst nur, dass
    /// <i>irgendeiner</i> fehlt.
    /// </remarks>
    private void RefuseUnknownNode(string   id,
                                   string?  from,
                                   string   node,
                                   string   ns    = DiscoManager.InfoNamespace)
    {

        _logger.LogDebug("disco-Abfrage nach unbekanntem Node '{Node}' von {From} mit <item-not-found/> beantwortet",
                         node, from ?? "(Server)");

        // Ohne 'from' kam die Anfrage vom eigenen Server; die Antwort geht
        // dann ohne 'to' implizit dorthin zurück (Abschnitt 8.1.1.1).
        var toAttr = from != null ? $" to='{XmlEscaping.Escape(from)}'" : "";

        _ = SendAsync($"<iq type='error' id='{XmlEscaping.Escape(id)}'{toAttr}>" +
                      $"<query xmlns='{ns}' node='{XmlEscaping.Escape(node)}'/>" +
                       "<error type='cancel'>" +
                       "<item-not-found xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                       "</error></iq>");

    }

    /// <summary>
    /// Beantwortet ein IQ, für das es keinen Handler gibt.
    ///
    /// RFC 6120, Abschnitt 8.2.3 verlangt auf jedes <c>iq</c> vom Typ
    /// <c>get</c> oder <c>set</c> eine Antwort - <c>result</c> oder
    /// <c>error</c>. Bleibt sie aus, wartet die Gegenstelle bis in ihren
    /// Timeout; bei einem Server kann das die Sitzung kosten. Für nicht
    /// unterstützte Anfragen ist die richtige Antwort nach Abschnitt 8.4
    /// <c>&lt;service-unavailable/&gt;</c>.
    /// </summary>
    private void RespondUnhandledIq(string? id, string? from)
    {

        // Ohne 'id' liesse sich die Antwort nicht zuordnen - dort ist das
        // Attribut nach Abschnitt 8.2.3 zwingend, die Anfrage ist also selbst
        // fehlerhaft.
        if (id is null)
        {
            _logger.LogWarning("IQ ohne 'id' von {From} - nicht beantwortbar", from ?? "(Server)");
            return;
        }

        // Ohne 'from' kam die Anfrage vom eigenen Server; die Antwort geht
        // dann ohne 'to' implizit dorthin zurück (Abschnitt 8.1.1.1).
        var toAttr = from != null ? $" to='{XmlEscaping.Escape(from)}'" : "";

        _logger.LogDebug("Unbekanntes IQ '{Id}' von {From} mit <service-unavailable/> beantwortet",
                         id, from ?? "(Server)");

        _ = SendAsync($"<iq type='error' id='{XmlEscaping.Escape(id)}'{toAttr}>" +
                       "<error type='cancel'>" +
                       "<service-unavailable xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                       "</error></iq>");

    }

    /// <summary>
    /// RFC 6121, Abschnitt 2.1.6: Prüft, ob ein Roster-Push vom eigenen Konto
    /// stammt und damit angewendet werden darf.
    /// </summary>
    /// <param name="from">Das 'from'-Attribut des IQ; null, wenn nicht gesetzt.</param>
    internal bool IsAuthorizedRosterPush(string? from)
    {

        // Kein 'from' bedeutet: implizit vom Bare-JID des eigenen Kontos.
        if (from is null)
            return true;

        // Vor dem Resource Binding gibt es noch keinen eigenen JID, gegen den
        // geprüft werden könnte - dann im Zweifel ablehnen.
        if (string.IsNullOrEmpty(FullJid))
            return false;

        return JidUtilities.Bare(from).Equals(BareJid, StringComparison.OrdinalIgnoreCase);

    }

    /// <summary>
    /// Wendet einen Roster-Push an.
    ///
    /// Das frühere Muster verlangte die Attribute in der Reihenfolge
    /// <c>jid</c>, <c>name</c>, <c>subscription</c>. Ein
    /// <c>&lt;item subscription='both' jid='…'/&gt;</c> - gültig und je nach
    /// Server üblich - passte darauf nicht und der Push wurde still verworfen.
    /// Gruppen las es überhaupt nicht.
    /// </summary>
    private void ProcessRosterPush(XElement element)
    {

        var query = element.Child("query");

        if (query is null)
            return;

        foreach (var itemElement in query.Elements().Where(e => e.Name.LocalName == "item"))
        {

            var jid = itemElement.Attr("jid");

            if (string.IsNullOrEmpty(jid))
                continue;

            if (itemElement.Attr("subscription") == "remove")
                Roster.RemoveItem(jid);
            else
                Roster.ProcessRosterItem(ToRosterItem(itemElement, jid));

        }

        // RFC 6121, Abschnitt 2.6.3: Der Push trägt die Fassung, auf der der
        // Roster nach dieser Änderung steht. Sie zu übernehmen ist der ganze
        // Zweck der Übung - ohne das fragt der Client beim nächsten Anmelden
        // mit einer veralteten Fassung und bekommt alles noch einmal.
        if (query.Attr("ver") is string fassung)
            Roster.Version = fassung;

    }

    /// <summary>
    /// Baut aus einem <c>&lt;item/&gt;</c> des Rosters einen
    /// <see cref="RosterItem"/> - inklusive der Gruppen, die vorher verloren
    /// gingen.
    /// </summary>
    private static RosterItem ToRosterItem(XElement itemElement, string jid)
    {

        var item = new RosterItem(jid)
        {
            Name          = itemElement.Attr("name"),
            Subscription  = ParseSubscription(itemElement.Attr("subscription") ?? "")
        };

        foreach (var group in itemElement.Elements().Where(e => e.Name.LocalName == "group"))
            item.Groups.Add(group.Value);

        return item;

    }

    // ===== AUTH & SETUP =====

    private async Task PerformSaslPlainAsync(CancellationToken ct)
    {
        // RFC 4616, Abschnitt 2: Auch PLAIN schickt Benutzername und Passwort
        // in der SASLprep-Form. Sonst hinge es am Mechanismus, ob dasselbe
        // Passwort passt - über SCRAM vorbereitet, über PLAIN nicht.
        var authData = $"\0{SaslPrep.Prepare(_username)}\0{SaslPrep.Prepare(_password)}";
        var authBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(authData));

        await SendAsync($"<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='PLAIN'>{authBase64}</auth>");

        var response = await ReceiveElementAsync(ct, "die Antwort auf SASL PLAIN");

        if (StreamNegotiation.IsSasl(response, "success"))
            _logger.LogInformation("Authentifizierung erfolgreich (PLAIN)");

        else if (StreamNegotiation.IsSasl(response, "failure"))
            throw new AuthenticationException(
                      $"SASL PLAIN abgelehnt: {StreamNegotiation.SaslFailureCondition(response) ?? "ohne Angabe"}");

        else
            throw new AuthenticationException(
                      $"Unerwartete Antwort auf SASL PLAIN: <{response.Name.LocalName}/>");

    }

    private async Task PerformScramAsync(SCRAMMechanism mechanism, CancellationToken ct)
    {
        var scram = new SCRAMAuthenticator(_username, _password, mechanism);

        // Schritt 1: client-first-message
        var clientFirst = scram.CreateClientFirstMessage();
        await SendAsync($"<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='{scram.MechanismName}'>{clientFirst}</auth>");

        // Schritt 2: server-first-message (challenge)
        var challenge = await ReceiveElementAsync(ct, "die SCRAM-Challenge");

        if (!StreamNegotiation.IsSasl(challenge, "challenge"))
        {

            if (StreamNegotiation.IsSasl(challenge, "failure"))
                throw new AuthenticationException(
                          $"SCRAM abgelehnt: {StreamNegotiation.SaslFailureCondition(challenge) ?? "ohne Angabe"}");

            throw new AuthenticationException(
                      $"Unerwartete Antwort auf die client-first-message: <{challenge.Name.LocalName}/>");

        }

        var serverFirst = StreamNegotiation.SaslPayload(challenge);

        if (serverFirst.Length == 0)
            throw new AuthenticationException("Die SASL-Challenge des Servers ist leer.");

        // Schritt 3: client-final-message
        var clientFinal = scram.ProcessServerFirstMessage(serverFirst);
        await SendAsync($"<response xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>{clientFinal}</response>");

        // Schritt 4: server-final-message (success oder failure)
        var final = await ReceiveElementAsync(ct, "die SCRAM-Serversignatur");

        if (StreamNegotiation.IsSasl(final, "success"))
        {

            var serverFinal = StreamNegotiation.SaslPayload(final);

            // RFC 5802, Abschnitt 5: Die Serversignatur zu prüfen ist die
            // zweite Hälfte von SCRAM - sie belegt, dass die Gegenstelle das
            // Passwort ebenfalls kennt. Früher war das eine Kür: kam ein
            // <success/> ohne Nutzlast, unterblieb die Prüfung stillschweigend
            // und die gegenseitige Authentifizierung war damit wertlos.
            if (serverFinal.Length == 0)
                throw new AuthenticationException(
                          "Der Server hat SCRAM ohne server-final-message bestätigt - " +
                          "seine Signatur ist damit nicht prüfbar.");

            if (!scram.VerifyServerFinalMessage(serverFinal))
                throw new AuthenticationException("Server-Signatur ungültig - möglicher MITM-Angriff!");

            _logger.LogInformation("Authentifizierung erfolgreich ({Mechanism})", scram.MechanismName);

        }

        else if (StreamNegotiation.IsSasl(final, "failure"))
            throw new AuthenticationException(
                      $"SCRAM fehlgeschlagen: {StreamNegotiation.SaslFailureCondition(final) ?? "ohne Angabe"}");

        else
            throw new AuthenticationException(
                      $"Unerwartete Antwort auf die client-final-message: <{final.Name.LocalName}/>");

    }

    private async Task<string> PerformBindAsync(CancellationToken ct)
    {

        var response = await RequestBindAsync("bind1", Resource, ct);
        var jid      = StreamNegotiation.ReadBoundJid(response);

        if (jid is not null)
            return jid;

        // RFC 6120, Abschnitt 7.7.2.2: Ist die gewünschte Resource schon
        // gebunden, darf der Server sie mit <conflict/> ablehnen - andere
        // Server vergeben stattdessen selbst eine abweichende. Auf die
        // Ablehnung gehört der zweite Versuch ohne Wunsch; nur so kommt ein
        // zweiter Client desselben Kontos überhaupt herein.
        //
        // Nur bei <conflict/>: jede andere Bedingung käme beim zweiten Versuch
        // genauso wieder.
        if (Resource is not null && IsConflict(response))
        {

            _logger.LogInformation("Resource '{Resource}' ist belegt - der Server soll eine vergeben", Resource);

            response = await RequestBindAsync("bind2", null, ct);
            jid      = StreamNegotiation.ReadBoundJid(response);

            if (jid is not null)
                return jid;

        }

        throw new XMPPProtocolException($"Resource Binding abgelehnt: {DescribeRejection(response)}");

    }

    /// <summary>
    /// Wurde die Anfrage mit <c>&lt;conflict/&gt;</c> abgelehnt?
    /// </summary>
    private static bool IsConflict(XElement response)
        => StanzaError.TryParse(response.ToString(), out var error) &&
           error?.Condition == "conflict";

    /// <summary>
    /// Schickt eine Bind-Anfrage und liest die Antwort.
    /// </summary>
    /// <param name="resource">Die gewünschte Resource, oder null für "vergib du".</param>
    private async Task<XElement> RequestBindAsync(string id, string? resource, CancellationToken ct)
    {

        var wunsch = resource is not null
                         ? $"<resource>{XmlEscaping.Escape(resource)}</resource>"
                         : "";

        await SendAsync($"<iq type='set' id='{id}'>" +
                        $"<bind xmlns='{StreamNegotiation.BindNamespace}'>{wunsch}</bind>" +
                        $"</iq>");

        return await ReceiveElementAsync(ct);

    }

    /// <summary>
    /// Beschreibt eine abgelehnte Anfrage für die Fehlermeldung.
    /// </summary>
    private static string DescribeRejection(XElement response)
        => StanzaError.TryParse(response.ToString(), out var error) && error is not null
               ? error.ToString()
               : Shorten(response.ToString(), 200);

    private async Task PerformSessionAsync(CancellationToken ct)
    {

        var response = await SendIqAsync(
                           "sess1",
                           "<iq type='set' id='sess1'>" +
                           $"<session xmlns='{StreamNegotiation.SessionNamespace}'/>" +
                           "</iq>",
                           ct);

        if (response is null)
            _logger.LogWarning("Keine Antwort auf die Session-Anfrage");

        else if (response.Attr("type") != "result")
            _logger.LogWarning("Session-Anfrage abgelehnt: {Reason}", DescribeRejection(response));

    }

    private async Task EnableCarbonsAsync(CancellationToken ct)
    {

        var response = await SendIqAsync("carbons-enable", CarbonManager.EnableIq(), ct);

        if (response is null)
        {
            _logger.LogWarning("Message Carbons: keine Antwort vom Server");
            return;
        }

        if (response.Attr("type") == "result")
        {
            Carbons!.SetEnabled(true);
            _logger.LogInformation("Message Carbons aktiviert");
        }
        else
            _logger.LogWarning("Message Carbons nicht verfügbar: {Reason}", DescribeRejection(response));

    }

    #region OMEMO (XEP-0384), PEP-Verteilung

    /// <summary>
    /// XEP-0384: Veröffentlicht die eigene Geräteliste.
    /// </summary>
    /// <returns>false, wenn der Server es abgelehnt hat.</returns>
    /// <remarks>
    /// <b>Der Rückgabewert ist der Punkt.</b> Bis hierher hat dieses Haus
    /// PubSub-Anfragen abgeschickt und nicht nachgesehen, was zurückkam (siehe
    /// D38) - für ein Abonnement war das lässlich. Hier ist es das nicht: Wer
    /// seine Geräteliste veröffentlicht und nicht erfährt, dass es misslang,
    /// ist für alle seine Kontakte unerreichbar und merkt nichts davon. Alles
    /// sieht aus wie immer, nur schreibt ihm niemand mehr verschlüsselt.
    /// </remarks>
    public async Task<bool> PublishOmemoDeviceListAsync(OmemoDeviceList   list,
                                                        CancellationToken ct = default)
        => await PublishPepAsync(OmemoDeviceList.Node, OmemoDeviceList.ItemId, list.ToXml(), ct);

    /// <summary>
    /// XEP-0384: Veröffentlicht das eigene Bundle unter der Gerätekennung.
    /// </summary>
    public async Task<bool> PublishOmemoBundleAsync(UInt32             deviceId,
                                                    OmemoBundle        bundle,
                                                    CancellationToken  ct = default)
        => await PublishPepAsync(OmemoPep.BundlesNode, deviceId.ToString(), bundle.ToXml(), ct);

    private async Task<bool> PublishPepAsync(string             node,
                                             string             itemId,
                                             XElement           payload,
                                             CancellationToken  ct)
    {

        var id       = $"pep-{Interlocked.Increment(ref _pepCounter)}";
        var response = await SendIqAsync(id, OmemoPep.PublishIq(id, node, itemId, payload), ct);

        if (response is null)
        {
            _logger.LogWarning("PEP: keine Antwort auf das Veröffentlichen in {Node}", node);
            return false;
        }

        if (response.Attr("type") != "result")
        {
            _logger.LogWarning("PEP: {Node} abgelehnt: {Reason}", node, DescribeRejection(response));
            return false;
        }

        return true;

    }

    /// <summary>
    /// XEP-0384: Holt die Geräteliste eines Kontos.
    /// </summary>
    /// <returns>
    /// null, wenn es keine gibt - dieser Mensch benutzt OMEMO nicht, oder
    /// sein Server hält nichts bereit. Beides ist dasselbe für den, der
    /// schreiben will.
    /// </returns>
    public async Task<OmemoDeviceList?> FetchOmemoDeviceListAsync(string             bareJid,
                                                                  CancellationToken  ct = default)
    {

        var inhalt = await FetchPepAsync(bareJid, OmemoDeviceList.Node, OmemoDeviceList.ItemId, ct);

        return inhalt is not null && OmemoDeviceList.TryRead(inhalt, out var liste)
                   ? liste
                   : null;

    }

    /// <summary>
    /// XEP-0384: Holt das Bundle eines bestimmten Geräts.
    /// </summary>
    /// <remarks>
    /// <b>Die Signatur wird hier geprüft und nicht erst beim Aufrufer.</b> Ein
    /// Bundle kommt vom Server der Gegenstelle - also von der Partei, gegen
    /// die OMEMO schützt. Ein ungeprüftes Bundle weiterzureichen hiesse, die
    /// Prüfung dem zu überlassen, der sie am ehesten vergisst.
    /// </remarks>
    public async Task<OmemoBundle?> FetchOmemoBundleAsync(string             bareJid,
                                                          UInt32             deviceId,
                                                          CancellationToken  ct = default)
    {

        var inhalt = await FetchPepAsync(bareJid, OmemoPep.BundlesNode, deviceId.ToString(), ct);

        if (inhalt is null || !OmemoPep.TryReadBundle(inhalt, out var bundle))
            return null;

        if (!bundle!.SignatureIsValid())
        {
            _logger.LogWarning("OMEMO: Das Bundle von {Jid}/{Device} ist nicht gültig unterschrieben",
                               bareJid, deviceId);
            return null;
        }

        return bundle;

    }

    private async Task<XElement?> FetchPepAsync(string             bareJid,
                                                string             node,
                                                string             itemId,
                                                CancellationToken  ct)
    {

        var id       = $"pep-{Interlocked.Increment(ref _pepCounter)}";
        var response = await SendIqAsync(id, OmemoPep.FetchIq(id, bareJid, node, itemId), ct);

        if (response is null || response.Attr("type") != "result")
            return null;

        return response.Child(OmemoPep.PubSubNamespace, "pubsub")
                      ?.Child(OmemoPep.PubSubNamespace, "items")
                      ?.Elements().FirstOrDefault(e => e.Name.LocalName == "item")
                      ?.Elements().FirstOrDefault();

    }

    /// <summary>
    /// Eine fremde Geräteliste ist eingetroffen - über PEP, ohne dass jemand
    /// gefragt hätte.
    /// </summary>
    public event Action<string, OmemoDeviceList>? OnOmemoDeviceListChanged;

    /// <summary>
    /// Die eigene Gerätekennung, sobald sie feststeht - daran hängt der
    /// Wiedereintrag nach Abschnitt 5.2.
    /// </summary>
    public UInt32? OmemoDeviceId { get; set; }

    /// <summary>
    /// Verarbeitet eine PEP-Benachrichtigung (XEP-0163).
    /// </summary>
    /// <remarks>
    /// <b>Der Wiedereintrag ist ein MUSS der Spezifikation</b>, und der Grund
    /// ist unangenehm: Ein anderes Gerät desselben Menschen - oder ein
    /// aufräumender Server - kann die Liste neu schreiben und dieses Gerät
    /// dabei vergessen. Von da an schreibt niemand mehr an dieses Gerät
    /// verschlüsselt, und es merkt nichts davon, weil ihm nichts fehlt: Es
    /// bekommt weiterhin alles, was unverschlüsselt kommt.
    ///
    /// Ergänzt wird, nicht ersetzt: Wer hier eine Liste mit nur dem eigenen
    /// Gerät veröffentlichte, machte aus dem Wiedereintrag eine Verdrängung
    /// aller anderen Geräte.
    /// </remarks>
    internal async Task ProcessPepEventAsync(XElement stanza, string from)
    {

        var items = stanza.Child("http://jabber.org/protocol/pubsub#event", "event")
                         ?.Child("http://jabber.org/protocol/pubsub#event", "items");

        if (items?.Attr("node") != OmemoDeviceList.Node)
            return;

        var payload = items.Elements().FirstOrDefault(e => e.Name.LocalName == "item")
                          ?.Elements().FirstOrDefault();

        if (payload is null || !OmemoDeviceList.TryRead(payload, out var liste) || liste is null)
            return;

        OnOmemoDeviceListChanged?.Invoke(JidUtilities.Bare(from), liste);

        if (OmemoDeviceId is not UInt32 eigenes ||
            !string.Equals(JidUtilities.Bare(from), BareJid, StringComparison.OrdinalIgnoreCase) ||
            liste.Contains(eigenes))
            return;

        _logger.LogWarning("OMEMO: Das eigene Gerät {Device} fehlt in der Geräteliste - trage es wieder ein",
                           eigenes);

        await PublishOmemoDeviceListAsync(liste.With(new OmemoDevice(eigenes)));

    }

    /// <summary>
    /// XEP-0384: der OMEMO-Verwalter, sobald er eingeschaltet ist.
    /// </summary>
    public OmemoManager? Omemo { get; private set; }

    /// <summary>
    /// Eine verschlüsselt eingetroffene Nachricht - schon entschlüsselt.
    /// </summary>
    public event Action<XMPPMessage, OmemoDecrypted>? OnEncryptedMessage;

    /// <summary>
    /// XEP-0384: Schaltet OMEMO ein - Schlüsselmaterial aus dem Speicher,
    /// Geräteliste und Bundle veröffentlicht.
    /// </summary>
    /// <remarks>
    /// <b>Die Geräteliste wird ergänzt und nicht ersetzt.</b> Wer sie neu
    /// schriebe, verdrängte damit jedes andere Gerät desselben Menschen - und
    /// die bekämen von da an nichts mehr, ohne dass jemand etwas bemerkt.
    /// </remarks>
    public async Task<bool> EnableOmemoAsync(IOmemoStore store, CancellationToken ct = default)
    {

        Omemo = new OmemoManager(store,
                                 BareJid,
                                 jid => FetchOmemoDeviceListAsync(jid, ct),
                                 (jid, device) => FetchOmemoBundleAsync(jid, device, ct),
                                 _logger);

        OmemoDeviceId = Omemo.Identity.DeviceId;

        var vorhanden = await FetchOmemoDeviceListAsync(BareJid, ct)
                            ?? new OmemoDeviceList([]);

        var ergaenzt = vorhanden.With(new OmemoDevice(Omemo.Identity.DeviceId));

        if (!await PublishOmemoDeviceListAsync(ergaenzt, ct))
            return false;

        return await PublishOmemoBundleAsync(Omemo.Identity.DeviceId, Omemo.Identity.Bundle(), ct);

    }

    /// <summary>
    /// XEP-0384: Schickt eine verschlüsselte Nachricht.
    /// </summary>
    /// <returns>
    /// Die übersprungenen Geräte - <b>leer heisst: alle lesen mit</b>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Wenn OMEMO nicht eingeschaltet ist. <b>Hier wird geworfen und nicht
    /// unverschlüsselt gesendet:</b> Wer verschlüsselt schreiben wollte und
    /// unverschlüsselt sendet, hat den schlimmsten aller Fehler gemacht - und
    /// zwar lautlos.
    /// </exception>
    public async Task<IReadOnlyList<OmemoSkippedDevice>> SendEncryptedMessageAsync(
        string to, string body, CancellationToken ct = default)
    {

        if (Omemo is null)
            throw new InvalidOperationException(
                      "OMEMO ist nicht eingeschaltet. Diese Nachricht wird nicht unverschlüsselt " +
                      "gesendet - das wäre der schlimmste aller Fehler, und lautlos.");

        XNamespace client = "jabber:client";

        var ergebnis = await Omemo.EncryptAsync([to], [new XElement(client + "body", body)]);

        // Ein <store/> nach XEP-0334, damit die Ablage sie aufhebt: Von aussen
        // sieht diese Nachricht wie eine ohne Inhalt aus, und ein Server, der
        // nach dem <body/> entscheidet, würde sie wegwerfen.
        var stanza = new XElement(client + "message",
                                  new XAttribute("to",   JidUtilities.Bare(to)),
                                  new XAttribute("type", "chat"),
                                  new XAttribute("id",   GenerateMessageId()),
                                  ergebnis.Element.ToXml(),
                                  new XElement(XNamespace.Get("urn:xmpp:hints") + "store"));

        await SendAsync(stanza.ToString(SaveOptions.DisableFormatting));

        return ergebnis.Skipped;

    }

    /// <summary>
    /// Nimmt eine verschlüsselte Nachricht entgegen.
    /// </summary>
    /// <returns>true, wenn sie verarbeitet wurde - dann geht sie nicht mehr den gewöhnlichen Weg.</returns>
    private bool TryProcessEncrypted(XElement element, string from)
    {

        if (Omemo is null || !OmemoEncryptedElement.TryRead(element, out var verschluesselt))
            return false;

        _ = Task.Run(async () =>
        {

            var entschluesselt = await Omemo.DecryptAsync(verschluesselt!, from);

            if (entschluesselt is null)
                return;

            var body = entschluesselt.Content
                                     .FirstOrDefault(e => e.Name.LocalName == "body")
                                    ?.Value;

            if (body is null)
                return;

            OnEncryptedMessage?.Invoke(
                new XMPPMessage(from,
                                element.Attr("to") ?? FullJid,
                                body,
                                element.Attr("id"),
                                DateTime.Now,
                                MessageType.Chat),
                entschluesselt);

        });

        return true;

    }

    #endregion

    /// <summary>
    /// XEP-0352: Sagt dem Server, ob gerade ein Mensch hinsieht.
    /// </summary>
    /// <param name="active">
    /// false, wenn das Gerät in der Tasche liegt - der Server hält dann
    /// zurück, was warten kann.
    /// </param>
    /// <returns>
    /// false, wenn der Server die Erweiterung nicht angekündigt hat. Dann
    /// bleibt es beim aktiven Zustand, und zwar auf beiden Seiten: Ein
    /// Client, der seinen Wunsch trotzdem vermerkte, hielte den Server für
    /// sparsam, während dieser weiterhin alles schickt.
    /// </returns>
    public async Task<bool> SetClientStateAsync(bool active)
    {

        if (!SupportsClientStateIndication)
        {
            _logger.LogWarning("XEP-0352: Der Server bietet keine Client State Indication an.");
            return false;
        }

        await SendAsync(active
                            ? ClientStateIndication.ActiveXml
                            : ClientStateIndication.InactiveXml);

        // Erst nach dem erfolgreichen Senden. Wirft das Senden, ist der
        // Zustand auf dem Server unverändert, und die beiden Seiten wären
        // sich sonst uneinig darüber, was gerade zurückgehalten wird.
        ClientIsActive = active;

        return true;

    }

    /// <summary>
    /// Holt den Roster (RFC 6121, Abschnitt 2.1) - versioniert, wenn der
    /// Server es anbietet.
    /// </summary>
    /// <remarks>
    /// Beim ersten Mal geht ein leeres <c>ver=''</c> hinaus. Das ist kein
    /// Platzhalter, sondern die Ansage „ich kann Versionierung, habe aber noch
    /// nichts" (RFC 6121, Abschnitt 2.6.1): Der Server schickt den vollen
    /// Roster und diesmal mit einer Fassung dazu.
    /// </remarks>
    private async Task RequestRosterAsync(Boolean versioniert, CancellationToken ct)
    {

        var response = await SendIqAsync(
                           "roster1",
                           RosterStanzaBuilder.GetRoster(versioniert ? Roster.Version ?? "" : null),
                           ct);

        if (response is null)
        {
            _logger.LogWarning("Keine Antwort auf die Roster-Anfrage");
            return;
        }

        if (response.Attr("type") != "result")
        {
            _logger.LogWarning("Roster-Anfrage abgelehnt: {Reason}", DescribeRejection(response));
            return;
        }

        var query = response.Child(RosterStanzaBuilder.Namespace, "query");

        // RFC 6121, Abschnitt 2.6.2: Ein Ergebnis ganz ohne <query/> heisst
        // „unverändert" - der Zwischenspeicher bleibt, wie er ist. Das gilt
        // aber nur, wenn wir überhaupt versioniert gefragt haben; sonst wäre es
        // schlicht ein Server, der nichts geschickt hat.
        if (query is null)
        {

            if (versioniert)
                _logger.LogDebug("Roster unverändert (Fassung {Version}), {Count} Kontakte aus dem Zwischenspeicher",
                                 Roster.Version, Roster.Items.Count);
            else
                _logger.LogWarning("Roster-Antwort ohne <query/>");

            return;

        }

        var stand = new List<RosterItem>();

        foreach (var itemElement in query.Children(RosterStanzaBuilder.Namespace, "item"))
        {

            var jid = itemElement.Attr("jid");

            if (!string.IsNullOrEmpty(jid))
                stand.Add(ToRosterItem(itemElement, jid));

        }

        // Ersetzen und nicht ergänzen: Das Ergebnis ist der vollständige
        // Roster (RFC 6121, Abschnitt 2.1.4). Wer hier nur einarbeitet,
        // behält einen Kontakt, den der Server längst nicht mehr führt.
        Roster.ReplaceAll(stand);

        // Die Fassung gehört zu genau diesem Stand und wird deshalb erst
        // übernommen, nachdem er eingearbeitet ist.
        if (query.Attr("ver") is string fassung)
            Roster.Version = fassung;

        _logger.LogInformation("Roster geladen: {Count} Kontakte (Fassung {Version})",
                               Roster.Items.Count, Roster.Version ?? "ohne");
    }

    // ===== PUBLIC API =====

    public async Task SendPresenceAsync(string? show = null, string? status = null)
    {
        var sb = new StringBuilder("<presence>");
        if (!string.IsNullOrEmpty(show))
            sb.Append($"<show>{XmlEscaping.Escape(show)}</show>");
        if (!string.IsNullOrEmpty(status))
            sb.Append($"<status>{XmlEscaping.Escape(status)}</status>");

        // RFC 6121, Abschnitt 4.7.2.3: Die Priorität steht hinter show und
        // status, wie der Abschnitt sie aufzählt.
        if (PresencePriority.HasValue)
            sb.Append($"<priority>{PresencePriority.Value}</priority>");

        // XEP-0115: Entity Capabilities
        if (EntityCaps != null)
        {
            sb.Append(EntityCaps.GetCapsElement());
        }

        sb.Append("</presence>");

        await SendAsync(sb.ToString());
    }

    /// <summary>
    /// Schickt eine Nachricht.
    /// </summary>
    /// <param name="type">
    /// Die Art der Nachricht (RFC 6121, Abschnitt 5.2.2). Vorgabe ist
    /// <see cref="MessageType.Chat"/> - dieser Client ist einer für Gespräche
    /// unter vier Augen.
    /// </param>
    /// <param name="requestReceipt">
    /// Eine Empfangsbestätigung anfordern (XEP-0184). Wird für Nachrichten
    /// übergangen, bei denen keine Antwort zu erwarten ist: In einem Raum
    /// bekämen alle Anwesenden die Quittungen zu sehen, und ein Zuruf will
    /// keine.
    /// </param>
    public async Task<string> SendMessageAsync(string       to,
                                               string       body,
                                               bool         requestReceipt  = true,
                                               bool         markable        = true,
                                               MessageType  type            = MessageType.Chat,
                                               string?      corrects        = null)
    {
        var messageId = GenerateMessageId();

        var typeAttr = type.AsAttribute() is string t ? $" type='{t}'" : "";

        var sb = new StringBuilder();
        sb.Append($"<message to='{XmlEscaping.Escape(to)}'{typeAttr} id='{messageId}'>");
        sb.Append($"<body>{XmlEscaping.Escape(body)}</body>");

        // XEP-0308: Eine eigene id und der volle neue Text - das <replace/>
        // nennt nur, welche Nachricht abgelöst wird. Ein Empfänger ohne diese
        // Erweiterung zeigt sie als zweite Nachricht an, und das ist
        // beabsichtigt: unschön, aber vollständig.
        if (corrects is not null)
            sb.Append(MessageCorrection.ReplaceXml(corrects));

        // Was keine Antwort erwartet, bekommt auch keine angefordert.
        if (!type.ExpectsAReply())
        {
            requestReceipt  = false;
            markable        = false;
        }

        // XEP-0184: Receipt Request
        if (requestReceipt)
        {
            sb.Append(ReceiptBuilder.RequestXml);
            Receipts.TrackMessage(messageId, to);
        }

        // XEP-0333: Chat Markers - markable
        if (markable)
        {
            sb.Append(ChatMarkers.Markable);
        }

        // XEP-0085: Chat State
        sb.Append(ChatState.Active.ToXml());
        sb.Append("</message>");

        var xml = sb.ToString();

        // XEP-0198: das Mitzählen passiert zentral in SendAsync.
        await SendAsync(xml);
        return messageId;
    }

    public async Task SendChatStateAsync(string to, ChatState state)
    {
        await SendAsync($"<message to='{XmlEscaping.Escape(to)}' type='chat'>{state.ToXml()}</message>");
    }

    public async Task SendReceiptAsync(string to, string messageId)
    {
        await SendAsync(ReceiptBuilder.CreateReceipt(to, messageId));
    }

    /// <summary>
    /// XEP-0333: Sendet einen Chat Marker
    /// </summary>
    public async Task SendChatMarkerAsync(string to, string refMessageId, ChatMarkerType type)
    {
        await SendAsync(ChatMarkers.CreateMarker(to, refMessageId, type));
    }

    /// <summary>
    /// XEP-0199: Sendet einen Ping
    /// </summary>
    public Task<TimeSpan?> PingAsync(string? to = null, CancellationToken ct = default)
    {
        return Ping?.PingAsync(to, ct) ?? Task.FromResult<TimeSpan?>(null);
    }

    /// <summary>
    /// XEP-0030: Fragt Service Discovery Info ab
    /// </summary>
    public Task<DiscoInfo?> DiscoverInfoAsync(string jid, CancellationToken ct = default)
    {
        return Disco?.QueryInfoAsync(jid, ct: ct) ?? Task.FromResult<DiscoInfo?>(null);
    }

    /// <summary>
    /// XEP-0030: Fragt Service Discovery Items ab
    /// </summary>
    public Task<DiscoItems?> DiscoverItemsAsync(string jid, CancellationToken ct = default)
    {
        return Disco?.QueryItemsAsync(jid, ct: ct) ?? Task.FromResult<DiscoItems?>(null);
    }

    /// <summary>
    /// XEP-0198: Fordert Ack vom Server an
    /// </summary>
    public Task RequestAckAsync()
    {
        return StreamManagement?.RequestAckAsync() ?? Task.CompletedTask;
    }

    public async Task SendRawAsync(string xml) => await SendAsync(xml);

    // Roster Operations
    public async Task AddContactAsync(string jid, string? name = null, IEnumerable<string>? groups = null)
    {
        await SendAsync(RosterStanzaBuilder.SetItem(jid, name, groups));
        await SendAsync(RosterStanzaBuilder.Subscribe(jid));
    }

    public async Task RemoveContactAsync(string jid) => await SendAsync(RosterStanzaBuilder.RemoveItem(jid));
    public async Task AcceptSubscriptionAsync(string jid) => await SendAsync(RosterStanzaBuilder.Subscribed(jid));
    public async Task DenySubscriptionAsync(string jid) => await SendAsync(RosterStanzaBuilder.Unsubscribed(jid));

    /// <summary>
    /// Kündigt das eigene Abonnement auf die Presence eines Kontakts
    /// (RFC 6121, Abschnitt 3.3).
    /// </summary>
    /// <remarks>
    /// Der vierte der vier Übergänge aus Abschnitt 3 - und bis D57 der einzige,
    /// den dieser Client nicht anbieten konnte, obwohl der Baustein dafür
    /// dastand und der Server ihn seit S3b beherrscht. Wer den Kontakt ganz
    /// loswerden will, nimmt <see cref="RemoveContactAsync"/>; hier bleibt er
    /// im Roster stehen, nur seine Presence kommt nicht mehr.
    ///
    /// Der Unterschied zu <see cref="DenySubscriptionAsync"/> ist die Richtung:
    /// Dort geht es darum, was der Kontakt von mir sieht, hier darum, was ich
    /// von ihm sehe.
    /// </remarks>
    public async Task CancelSubscriptionAsync(string jid) => await SendAsync(RosterStanzaBuilder.Unsubscribe(jid));

    #region PubSub (XEP-0060) - ausgehend

    /// <summary>
    /// Die Kennung der nächsten PubSub-Anfrage.
    /// </summary>
    /// <remarks>
    /// Eine je Anfrage, und deshalb ein Zähler. Bis D71 trugen alle
    /// <c>subscribe</c> dieselbe feste Kennung <c>pubsub-sub</c> - solange
    /// niemand die Antworten zuordnete, fiel das nicht auf; sobald jemand es
    /// tut, bekäme die zweite Anfrage die Antwort auf die erste.
    /// </remarks>
    private Int32 _pubSubCounter;

    private String NextPubSubId()
        => $"pubsub-{Interlocked.Increment(ref _pubSubCounter)}";

    /// <summary>
    /// XEP-0060, Abschnitt 6.1: Abonniert einen Knoten und <b>wartet die
    /// Antwort ab</b>.
    /// </summary>
    /// <param name="service">
    /// Der Dienst oder das Konto, bei dem abonniert wird; ohne Angabe der
    /// PubSub-Dienst der eigenen Domain.
    /// </param>
    /// <returns>
    /// Das zugesagte Abonnement, oder null - bei einer Absage, bei einer
    /// Antwort ohne Zusage und bei Schweigen. Die drei Fälle stehen im Log
    /// auseinander; für den Aufrufer sind sie dasselbe: <b>er ist nicht
    /// abonniert.</b>
    /// </returns>
    /// <remarks>
    /// <b>Ein <c>pending</c> wird nicht eingetragen.</b> Es sieht wie eine
    /// Zusage aus - der Dienst hat die Anfrage angenommen -, heisst aber, dass
    /// noch jemand darüber entscheidet (Abschnitt 6.1.4). Wer es als
    /// Abonnement bucht, wartet auf Meldungen, die nicht kommen, und hält das
    /// für einen Fehler anderswo.
    /// </remarks>
    public async Task<PubSubSubscription?> PubSubSubscribeAsync(String             nodeId,
                                                                String?            service  = null,
                                                                CancellationToken  ct       = default)
    {

        var ziel     = service ?? PubSub!.PubSubService;
        var id       = NextPubSubId();
        var antwort  = await SendIqAsync(id, PubSubBuilder.Subscribe(ziel, nodeId, BareJid, id), ct);

        if (antwort is null)
        {
            _logger.LogWarning("PubSub: keine Antwort auf das Abonnieren von {Node} bei {Service}", nodeId, ziel);
            return null;
        }

        if (antwort.Attr("type") != "result")
        {
            _logger.LogWarning("PubSub: {Node} bei {Service} abgelehnt: {Reason}",
                               nodeId, ziel, DescribeRejection(antwort));
            return null;
        }

        if (!PubSubSubscription.TryRead(antwort, ziel, out var abo))
        {
            _logger.LogWarning("PubSub: die Antwort auf das Abonnieren von {Node} enthält keine Zusage", nodeId);
            return null;
        }

        if (abo!.State != PubSubSubscriptionState.Subscribed)
        {
            _logger.LogInformation("PubSub: {Node} bei {Service} steht auf {State} - noch kein Abonnement",
                                   nodeId, ziel, abo.State);
            return null;
        }

        PubSub!.AddSubscription(abo);

        return abo;

    }

    /// <summary>
    /// XEP-0060, Abschnitt 6.2: Beendet ein Abonnement.
    /// </summary>
    /// <param name="subId">
    /// Welches Abonnement gemeint ist. Ohne Angabe geht es nur, solange es
    /// genau eines gibt.
    /// </param>
    /// <remarks>
    /// Die <c>subid</c> aus der Zusage geht mit, wenn es eine gibt. Sie ist
    /// vorgeschrieben, sobald ein JID mehrere Abonnements auf denselben Knoten
    /// hält (Abschnitt 6.2.3.1), und benennt auch das eine eindeutig.
    ///
    /// <b>Bei mehreren und ohne Kennung wird gar nicht erst gefragt.</b> Der
    /// Dienst wiese es mit <c>&lt;subid-required/&gt;</c> ab; das weiss dieser
    /// Client selbst. Was er nicht tut, ist wichtiger: sich eines aussuchen.
    /// Das beendete vielleicht das falsche, und der Aufrufer hielte es für das
    /// gemeinte.
    ///
    /// Der Eintrag fällt erst nach dem <c>result</c>. Ihn vorher zu löschen
    /// wäre derselbe Fehler wie vorher einzutragen, nur andersherum: Man
    /// verlöre die Meldungen eines Abonnements, das noch besteht.
    /// </remarks>
    public async Task<Boolean> PubSubUnsubscribeAsync(String             nodeId,
                                                      String?            service  = null,
                                                      String?            subId    = null,
                                                      CancellationToken  ct       = default)
    {

        if (!TryPickSubscription(nodeId, subId, service, out var ziel, out var verwendet))
            return false;

        var id       = NextPubSubId();
        var antwort  = await SendIqAsync(id,
                                         PubSubBuilder.Unsubscribe(ziel, nodeId, BareJid, id, verwendet),
                                         ct);

        if (antwort is null || antwort.Attr("type") != "result")
        {
            _logger.LogWarning("PubSub: {Node} bei {Service} nicht abbestellt: {Reason}",
                               nodeId, ziel,
                               antwort is null ? "keine Antwort" : DescribeRejection(antwort));
            return false;
        }

        PubSub!.RemoveSubscription(nodeId, verwendet);

        return true;

    }

    /// <summary>
    /// XEP-0060, Abschnitt 5.6: Holt die eigenen Abonnements beim Dienst und
    /// übernimmt sie in die Buchführung.
    /// </summary>
    /// <returns>
    /// Was der Dienst sagt, oder null bei Absage und Schweigen. <b>Eine leere
    /// Liste ist etwas anderes als null</b>: Sie heisst „keine", und die
    /// Buchführung wird entsprechend geleert.
    /// </returns>
    /// <remarks>
    /// <b>Der Weg aus der Klemme nach einem Verbindungsabriss.</b> Die
    /// Buchführung dieses Clients steht im Arbeitsspeicher und wird bei jedem
    /// Verbindungsaufbau neu erzeugt; die Abonnements bestehen beim Dienst
    /// weiter. Ohne diese Anfrage kennt der Client danach keine einzige
    /// Kennung mehr - und kann bei mehreren Abonnements auf denselben Knoten
    /// keines davon beenden.
    ///
    /// Sie geschieht <b>nicht von selbst</b>: Ein Client, der bei jedem
    /// Verbindungsaufbau ungefragt einen PubSub-Dienst anspräche, schickte
    /// eine Anfrage für ein Merkmal, das die meisten nie benutzen - und gegen
    /// eine Adresse, die es womöglich gar nicht gibt.
    /// </remarks>
    public async Task<IReadOnlyList<PubSubSubscription>?> PubSubGetSubscriptionsAsync(String?            service  = null,
                                                                                      String?            nodeId   = null,
                                                                                      CancellationToken  ct       = default)
    {

        var ziel     = service ?? PubSub!.PubSubService;
        var id       = NextPubSubId();
        var antwort  = await SendIqAsync(id, PubSubBuilder.GetSubscriptions(ziel, id, nodeId), ct);

        if (antwort is null || antwort.Attr("type") != "result")
        {
            _logger.LogWarning("PubSub: die Abonnements bei {Service} nicht gelesen: {Reason}",
                               ziel,
                               antwort is null ? "keine Antwort" : DescribeRejection(antwort));
            return null;
        }

        var liste = antwort.Child(PubSubSubscription.Namespace, "pubsub")
                          ?.Child(PubSubSubscription.Namespace, "subscriptions");

        if (liste is null)
        {
            _logger.LogWarning("PubSub: die Antwort von {Service} enthält keine Aufzählung", ziel);
            return null;
        }

        var gelesen = liste.Children(PubSubSubscription.Namespace, "subscription")
                           .Where (e => e.Attr("node") is not null)
                           .Select(e => new PubSubSubscription(e.Attr("node")!,
                                                               ziel,
                                                               e.Attr("subid"),
                                                               PubSubSubscription.StateOf(e.Attr("subscription"))))
                           .Where (a => a.State == PubSubSubscriptionState.Subscribed)
                           .ToList();

        // Nur eine Einschränkung auf einen Knoten sagt nichts über die
        // übrigen: Was der Dienst nicht aufzählen sollte, darf hier nicht als
        // beendet gelten.
        if (nodeId is null)
            PubSub!.ReplaceSubscriptionsOf(ziel, gelesen);

        else
            foreach (var abo in gelesen)
                PubSub!.AddSubscription(abo);

        return gelesen;

    }

    /// <summary>
    /// XEP-0060, Abschnitt 5.7: Holt die eigenen Rollen - was bin ich wo?
    /// </summary>
    /// <returns>
    /// Je Knoten die Rolle, oder null bei Absage und Schweigen.
    /// </returns>
    public async Task<IReadOnlyList<(String NodeId, PubSubAffiliation Affiliation)>?>
        PubSubGetAffiliationsAsync(String? service = null, CancellationToken ct = default)

        => await ReadAffiliationsAsync(PubSubBuilder.GetAffiliations(service ?? PubSub!.PubSubService,
                                                                     NextPubSubId()),
                                       PubSubSubscription.Namespace, "node", ct);

    /// <summary>
    /// XEP-0060, Abschnitt 8.9.1: Holt als Eigentümer die Rollen an einem
    /// Knoten.
    /// </summary>
    /// <returns>
    /// Je Eintrag der JID und seine Rolle, oder null - <b>etwa, weil der
    /// Knoten einem anderen gehört.</b> Das ist keine leere Liste: „Ich weiss
    /// es nicht" und „da ist niemand" sind zwei Antworten.
    /// </returns>
    public async Task<IReadOnlyList<(String Jid, PubSubAffiliation Affiliation)>?>
        PubSubGetNodeAffiliationsAsync(String             nodeId,
                                       String?            service  = null,
                                       CancellationToken  ct       = default)

        => await ReadAffiliationsAsync(PubSubBuilder.GetNodeAffiliations(service ?? PubSub!.PubSubService,
                                                                         nodeId, NextPubSubId()),
                                       PubSubBuilder.OwnerNamespace, "jid", ct);

    /// <summary>
    /// XEP-0060, Abschnitt 8.9.2: Setzt als Eigentümer eine Rolle.
    /// </summary>
    public async Task<Boolean> PubSubSetAffiliationAsync(String             nodeId,
                                                         String             jid,
                                                         PubSubAffiliation  affiliation,
                                                         String?            service  = null,
                                                         CancellationToken  ct       = default)

        => await PubSubRequestAsync(PubSubBuilder.SetAffiliation(service ?? PubSub!.PubSubService,
                                                                 nodeId, NextPubSubId(), jid,
                                                                 PubSubAffiliations.NameOf(affiliation)),
                                    "Rolle setzen", nodeId, ct);

    /// <summary>
    /// XEP-0060, Abschnitt 8.8.1: Holt als Eigentümer die Abonnenten eines
    /// Knotens.
    /// </summary>
    /// <returns>
    /// Je Eintrag der JID, die Kennung und der Zustand, oder null bei Absage
    /// und Schweigen - <b>etwa, weil der Knoten einem anderen gehört</b>. Das
    /// ist keine leere Liste: „Ich weiss es nicht" und „da ist niemand" sind
    /// zwei Antworten.
    /// </returns>
    /// <remarks>
    /// <b>Der Zustand wird hier streng gelesen, anders als in der eigenen
    /// Zusage.</b> Dort ist ein unbekannter Name als „nicht abonniert" die
    /// vorsichtige Annahme: Wer sich zu Unrecht für nicht abonniert hält,
    /// fragt noch einmal. Hier wäre dieselbe Nachsicht das Gegenteil von
    /// vorsichtig - der Eigentümer hielte einen Abonnenten für abwesend, den
    /// der Dienst führt, und entfernte womöglich einen anderen an seiner
    /// Stelle. Ein unlesbarer Eintrag lässt deshalb die ganze Liste
    /// scheitern, wie bei den Rollen.
    /// </remarks>
    public async Task<IReadOnlyList<(String Jid, String? SubId, PubSubSubscriptionState State)>?>
        PubSubGetNodeSubscribersAsync(String             nodeId,
                                      String?            service  = null,
                                      CancellationToken  ct       = default)
    {

        var ziel     = service ?? PubSub!.PubSubService;
        var id       = NextPubSubId();
        var antwort  = await SendIqAsync(id, PubSubBuilder.GetNodeSubscriptions(ziel, nodeId, id), ct);

        if (antwort is null || antwort.Attr("type") != "result")
        {
            _logger.LogWarning("PubSub: die Abonnenten von {Node} nicht gelesen: {Reason}",
                               nodeId,
                               antwort is null ? "keine Antwort" : DescribeRejection(antwort));
            return null;
        }

        var liste = antwort.Child(PubSubBuilder.OwnerNamespace, "pubsub")
                          ?.Child(PubSubBuilder.OwnerNamespace, "subscriptions");

        if (liste is null)
        {
            _logger.LogWarning("PubSub: die Antwort zu {Node} enthält keine Abonnentenliste", nodeId);
            return null;
        }

        var gelesen = new List<(String, String?, PubSubSubscriptionState)>();

        foreach (var eintrag in liste.Children(PubSubBuilder.OwnerNamespace, "subscription"))
        {

            if (eintrag.Attr("jid") is not String wer ||
                !PubSubSubscription.TryReadState(eintrag.Attr("subscription"), out var zustand))
            {
                _logger.LogWarning("PubSub: unlesbarer Eintrag in der Abonnentenliste: {Eintrag}", eintrag);
                return null;
            }

            // Die Kennung darf fehlen - ein Dienst muss keine vergeben, solange
            // es nur eine gibt (Abschnitt 12.19).
            gelesen.Add((wer, eintrag.Attr("subid"), zustand));

        }

        return gelesen;

    }

    /// <summary>
    /// XEP-0060, Abschnitt 8.8.2: Beendet als Eigentümer ein Abonnement an
    /// einem eigenen Knoten.
    /// </summary>
    /// <param name="subId">
    /// Ein bestimmtes Abonnement, oder null für alle dieses JIDs an diesem
    /// Knoten.
    /// </param>
    public async Task<Boolean> PubSubRemoveSubscriberAsync(String             nodeId,
                                                           String             jid,
                                                           String?            subId    = null,
                                                           String?            service  = null,
                                                           CancellationToken  ct       = default)

        => await PubSubRequestAsync(PubSubBuilder.RemoveSubscriber(service ?? PubSub!.PubSubService,
                                                                    nodeId, NextPubSubId(), jid, subId),
                                    "Abonnent entfernen", nodeId, ct);

    /// <summary>
    /// Liest eine Rollenliste - beide sehen gleich aus, nur der Namensraum und
    /// das kennzeichnende Attribut unterscheiden sich.
    /// </summary>
    /// <remarks>
    /// <b>Ein Eintrag mit einer unbekannten Rolle lässt die ganze Liste
    /// scheitern</b>, statt still zu fehlen. Eine Liste, aus der einzelne
    /// Zeilen verschwinden, ist schlimmer als keine: Wer sie ansieht, hält
    /// jemanden für rechtlos, der es nicht ist.
    /// </remarks>
    private async Task<IReadOnlyList<(String, PubSubAffiliation)>?> ReadAffiliationsAsync(String             iq,
                                                                                          String             ns,
                                                                                          String             key,
                                                                                          CancellationToken  ct)
    {

        var id       = XElement.Parse(iq).Attr("id")!;
        var antwort  = await SendIqAsync(id, iq, ct);

        if (antwort is null || antwort.Attr("type") != "result")
        {
            _logger.LogWarning("PubSub: die Rollen nicht gelesen: {Reason}",
                               antwort is null ? "keine Antwort" : DescribeRejection(antwort));
            return null;
        }

        var liste = antwort.Child(ns, "pubsub")?.Child(ns, "affiliations");

        if (liste is null)
        {
            _logger.LogWarning("PubSub: die Antwort enthält keine Rollenliste");
            return null;
        }

        var gelesen = new List<(String, PubSubAffiliation)>();

        foreach (var eintrag in liste.Children(ns, "affiliation"))
        {

            if (eintrag.Attr(key) is not String wer ||
                !PubSubAffiliations.TryRead(eintrag.Attr("affiliation"), out var rolle))
            {
                _logger.LogWarning("PubSub: unlesbarer Eintrag in der Rollenliste: {Eintrag}", eintrag);
                return null;
            }

            gelesen.Add((wer, rolle));

        }

        return gelesen;

    }

    /// <summary>
    /// XEP-0060, Abschnitt 6.3.1: Holt die Einstellungen eines Abonnements.
    /// </summary>
    /// <returns>
    /// Was der Dienst sagt, oder null bei Absage und Schweigen.
    /// </returns>
    /// <remarks>
    /// <b>Gefragt wird auch dann, wenn die Einstellungen schon in der eigenen
    /// Buchführung stehen.</b> Dort steht, was dieser Client gesetzt hat - ein
    /// anderes Gerät desselben Kontos kann dasselbe Abonnement inzwischen
    /// umgestellt haben, und dann wäre die eigene Angabe eine Erinnerung und
    /// keine Auskunft.
    /// </remarks>
    public async Task<PubSubSubscriptionOptions?> PubSubGetOptionsAsync(String             nodeId,
                                                                        String?            service  = null,
                                                                        String?            subId    = null,
                                                                        CancellationToken  ct       = default)
    {

        if (!TryPickSubscription(nodeId, subId, service, out var ziel, out var verwendet))
            return null;

        var id       = NextPubSubId();
        var antwort  = await SendIqAsync(id, PubSubBuilder.GetOptions(ziel, nodeId, BareJid, id, verwendet), ct);

        if (antwort is null || antwort.Attr("type") != "result")
        {
            _logger.LogWarning("PubSub: Einstellungen von {Node} bei {Service} nicht gelesen: {Reason}",
                               nodeId, ziel,
                               antwort is null ? "keine Antwort" : DescribeRejection(antwort));
            return null;
        }

        var formular = antwort.Child(PubSubSubscription.Namespace, "pubsub")
                             ?.Child(PubSubSubscription.Namespace, "options")
                             ?.Child(PubSubSubscriptionOptions.DataFormNamespace, "x");

        if (formular is null || !PubSubSubscriptionOptions.TryReadForm(formular, out var optionen))
        {
            _logger.LogWarning("PubSub: die Antwort auf die Einstellungen von {Node} enthält kein lesbares Formular",
                               nodeId);
            return null;
        }

        PubSub!.SetOptions(nodeId, verwendet, optionen!);

        return optionen;

    }

    /// <summary>
    /// XEP-0060, Abschnitt 6.3.5: Stellt ein Abonnement ein.
    /// </summary>
    /// <remarks>
    /// Vermerkt wird erst nach dem <c>result</c>. Ein abgelehnter Wunsch als
    /// geltender Zustand wäre derselbe Fehler wie ein Abonnement, das vor der
    /// Zusage eingetragen wird - nur eine Ebene tiefer.
    /// </remarks>
    public async Task<Boolean> PubSubSetOptionsAsync(String                     nodeId,
                                                     PubSubSubscriptionOptions  options,
                                                     String?                    service  = null,
                                                     String?                    subId    = null,
                                                     CancellationToken          ct       = default)
    {

        if (!TryPickSubscription(nodeId, subId, service, out var ziel, out var verwendet))
            return false;

        var id       = NextPubSubId();
        var antwort  = await SendIqAsync(id,
                                         PubSubBuilder.SetOptions(ziel, nodeId, BareJid, id, verwendet,
                                                                  options.ToSubmit()
                                                                         .ToString(SaveOptions.DisableFormatting)),
                                         ct);

        if (antwort is null || antwort.Attr("type") != "result")
        {
            _logger.LogWarning("PubSub: Einstellungen von {Node} bei {Service} nicht gesetzt: {Reason}",
                               nodeId, ziel,
                               antwort is null ? "keine Antwort" : DescribeRejection(antwort));
            return false;
        }

        PubSub!.SetOptions(nodeId, verwendet, options);

        return true;

    }

    /// <summary>
    /// Sucht heraus, welches Abonnement gemeint ist und wohin die Anfrage
    /// geht.
    /// </summary>
    /// <returns>
    /// false, wenn es mehrere gibt und keine Kennung sagt, welches - dann wird
    /// gar nicht erst gefragt. Dieselbe Regel wie beim Abbestellen, und aus
    /// demselben Grund: Der Client sucht sich keines aus.
    /// </returns>
    private Boolean TryPickSubscription(String       nodeId,
                                        String?      subId,
                                        String?      service,
                                        out String   target,
                                        out String?  usedSubId)
    {

        var abos = PubSub!.SubscriptionsOf(nodeId);

        target     = service ?? PubSub!.PubSubService;
        usedSubId  = subId;

        if (subId is null && abos.Count > 1)
        {
            _logger.LogWarning("PubSub: {Count} Abonnements auf {Node} - ohne subid ist nicht zu sagen, welches gemeint ist",
                               abos.Count, nodeId);
            return false;
        }

        var gemeint = subId is not null
                          ? abos.FirstOrDefault(a => String.Equals(a.SubId, subId, StringComparison.Ordinal))
                          : abos.FirstOrDefault();

        target     = service ?? gemeint?.ServiceJid ?? PubSub!.PubSubService;
        usedSubId  = subId ?? gemeint?.SubId;

        return true;

    }

    /// <summary>
    /// XEP-0060, Abschnitt 7.1: Veröffentlicht einen Eintrag.
    /// </summary>
    public async Task<Boolean> PubSubPublishAsync(String             nodeId,
                                                  String             itemId,
                                                  String             payload,
                                                  String?            service  = null,
                                                  CancellationToken  ct       = default)

        => await PubSubRequestAsync(PubSubBuilder.Publish(service ?? PubSub!.PubSubService,
                                                          nodeId, itemId, payload, NextPubSubId()),
                                    "Veröffentlichen", nodeId, ct);

    /// <summary>
    /// XEP-0060, Abschnitt 8.1: Legt einen Knoten an, wahlweise gleich mit
    /// seinen Einstellungen.
    /// </summary>
    /// <remarks>
    /// Anlegen und einstellen in einem Zug, weil zwei Schritte eine Lücke
    /// hätten: Zwischen dem Anlegen und dem Einstellen stünde der Knoten
    /// offen, und wer in dieser Zeit fragt, bekommt.
    /// </remarks>
    public async Task<Boolean> PubSubCreateNodeAsync(String                    nodeId,
                                                     PubSubNodeConfiguration?  configuration  = null,
                                                     String?                   service        = null,
                                                     CancellationToken         ct             = default)

        => await PubSubRequestAsync(PubSubBuilder.CreateNode(service ?? PubSub!.PubSubService,
                                                             nodeId, NextPubSubId(),
                                                             configuration?.ToSubmit()
                                                                           .ToString(SaveOptions.DisableFormatting)),
                                    "Anlegen", nodeId, ct);

    /// <summary>
    /// XEP-0060, Abschnitt 8.2.1: Holt die Einstellungen eines Knotens.
    /// </summary>
    /// <returns>
    /// Was der Dienst sagt, oder null bei Absage und Schweigen - und auch
    /// dann, wenn im Angebot nichts steht, was dieser Client versteht.
    /// </returns>
    public async Task<PubSubNodeConfiguration?> PubSubGetNodeConfigAsync(String             nodeId,
                                                                         String?            service  = null,
                                                                         CancellationToken  ct       = default)
    {

        var ziel     = service ?? PubSub!.PubSubService;
        var id       = NextPubSubId();
        var antwort  = await SendIqAsync(id, PubSubBuilder.GetNodeConfig(ziel, nodeId, id), ct);

        if (antwort is null || antwort.Attr("type") != "result")
        {
            _logger.LogWarning("PubSub: Einstellungen des Knotens {Node} bei {Service} nicht gelesen: {Reason}",
                               nodeId, ziel,
                               antwort is null ? "keine Antwort" : DescribeRejection(antwort));
            return null;
        }

        var formular = antwort.Child(PubSubBuilder.OwnerNamespace, "pubsub")
                             ?.Child(PubSubBuilder.OwnerNamespace, "configure")
                             ?.Child(DataForm.Namespace, "x");

        if (formular is null || !PubSubNodeConfiguration.TryReadForm(formular, out var einstellung))
        {
            _logger.LogWarning("PubSub: die Antwort über den Knoten {Node} enthält kein lesbares Formular", nodeId);
            return null;
        }

        return einstellung;

    }

    /// <summary>
    /// XEP-0060, Abschnitt 8.2.4: Stellt einen Knoten ein.
    /// </summary>
    public async Task<Boolean> PubSubConfigureNodeAsync(String                   nodeId,
                                                        PubSubNodeConfiguration  configuration,
                                                        String?                  service  = null,
                                                        CancellationToken        ct       = default)

        => await PubSubRequestAsync(PubSubBuilder.SetNodeConfig(service ?? PubSub!.PubSubService,
                                                                nodeId, NextPubSubId(),
                                                                configuration.ToSubmit()
                                                                             .ToString(SaveOptions.DisableFormatting)),
                                    "Einstellen", nodeId, ct);

    /// <summary>
    /// XEP-0060, Abschnitt 7.2: Nimmt einen einzelnen Eintrag zurück.
    /// </summary>
    /// <remarks>
    /// <b>Die Buchführung bleibt unberührt</b>, und zwar aus zwei Richtungen:
    /// Der Knoten besteht weiter, also auch jedes Abonnement darauf - und was
    /// dieser Client von dem Eintrag hat, führt er nicht. Was er führt, sind
    /// Abonnements; die Einträge liegen beim Dienst.
    /// </remarks>
    public async Task<Boolean> PubSubRetractAsync(String             nodeId,
                                                  String             itemId,
                                                  String?            service  = null,
                                                  CancellationToken  ct       = default)

        => await PubSubRequestAsync(PubSubBuilder.Retract(service ?? PubSub!.PubSubService,
                                                          nodeId, itemId, NextPubSubId()),
                                    "Zurücknehmen", nodeId, ct);

    /// <summary>
    /// XEP-0060, Abschnitt 8.4: Löscht einen Knoten.
    /// </summary>
    /// <remarks>
    /// <b>Danach ist auch das eigene Abonnement darauf fort</b>, und dieser
    /// Client muss es selbst streichen: Die Meldung nach Abschnitt 8.4.2 geht
    /// an alle ausser den, der gelöscht hat. Wer sich darauf verliesse,
    /// behielte als einziger einen Eintrag über einen Knoten, den er selbst
    /// beseitigt hat.
    /// </remarks>
    public async Task<Boolean> PubSubDeleteNodeAsync(String             nodeId,
                                                     String?            service  = null,
                                                     CancellationToken  ct       = default)
    {

        var ziel = service ?? PubSub!.PubSubService;

        if (!await PubSubRequestAsync(PubSubBuilder.DeleteNode(ziel, nodeId, NextPubSubId()),
                                      "Löschen", nodeId, ct))
        {
            return false;
        }

        PubSub!.RemoveSubscriptionsOf(nodeId, ziel);

        return true;

    }

    /// <summary>
    /// XEP-0060, Abschnitt 8.5: Leert einen Knoten.
    /// </summary>
    /// <remarks>
    /// Und lässt die Buchführung in Ruhe: Den Knoten gibt es weiter, das
    /// Abonnement darauf auch - die nächste Veröffentlichung kommt an dieselbe
    /// Adresse.
    /// </remarks>
    public async Task<Boolean> PubSubPurgeNodeAsync(String             nodeId,
                                                    String?            service  = null,
                                                    CancellationToken  ct       = default)

        => await PubSubRequestAsync(PubSubBuilder.PurgeNode(service ?? PubSub!.PubSubService,
                                                             nodeId, NextPubSubId()),
                                    "Leeren", nodeId, ct);

    /// <summary>
    /// Schickt eine PubSub-Anfrage und meldet, ob der Dienst zugestimmt hat.
    /// </summary>
    /// <remarks>
    /// Die Kennung steht schon im fertigen XML - deshalb wird sie hier wieder
    /// herausgelesen statt neu vergeben. Zwei Stellen, die sich eine Kennung
    /// ausdenken, denken sich irgendwann zwei verschiedene aus.
    /// </remarks>
    private async Task<Boolean> PubSubRequestAsync(String             iq,
                                                   String             was,
                                                   String             nodeId,
                                                   CancellationToken  ct)
    {

        var id       = XElement.Parse(iq).Attr("id")!;
        var antwort  = await SendIqAsync(id, iq, ct);

        if (antwort is null || antwort.Attr("type") != "result")
        {
            _logger.LogWarning("PubSub: {Was} in {Node} gescheitert: {Reason}",
                               was, nodeId,
                               antwort is null ? "keine Antwort" : DescribeRejection(antwort));
            return false;
        }

        return true;

    }

    #endregion

    /// <summary>
    /// XEP-0060, Abschnitt 6.5: Holt die Einträge eines Knotens.
    /// </summary>
    /// <returns>
    /// Die Einträge, oder null bei Absage und Schweigen. Eine leere Liste ist
    /// etwas anderes: Der Knoten war erreichbar und hatte nichts.
    /// </returns>
    /// <remarks>
    /// Bis D71 verschickte diese Methode die Anfrage und war fertig. Die
    /// Antwort kam an, wurde keinem Wartenden zugeordnet und fiel aus dem
    /// Empfang heraus - die Einträge, um die es ging, hat nie jemand gesehen.
    /// </remarks>
    public async Task<IReadOnlyList<PubSubItem>?> PubSubGetItemsAsync(String             nodeId,
                                                                      Int32?             maxItems  = null,
                                                                      String?            service   = null,
                                                                      CancellationToken  ct        = default)
    {

        var ziel     = service ?? PubSub!.PubSubService;
        var id       = NextPubSubId();
        var antwort  = await SendIqAsync(id, PubSubBuilder.GetItems(ziel, nodeId, maxItems, id), ct);

        if (antwort is null || antwort.Attr("type") != "result")
        {
            _logger.LogWarning("PubSub: {Node} bei {Service} nicht abgerufen: {Reason}",
                               nodeId, ziel,
                               antwort is null ? "keine Antwort" : DescribeRejection(antwort));
            return null;
        }

        var items = antwort.Child(PubSubSubscription.Namespace, "pubsub")
                          ?.Child(PubSubSubscription.Namespace, "items");

        if (items is null)
            return null;

        return [.. items.Children(PubSubSubscription.Namespace, "item")
                        .Where (item => item.Attr("id") is not null)
                        .Select(item => new PubSubItem(item.Attr("id")!,
                                                       items.Attr("node") ?? nodeId,
                                                       String.Concat(item.Nodes())))];

    }

    // ===== HELPERS =====

    private string GenerateMessageId() => $"msg-{Interlocked.Increment(ref _messageIdCounter)}-{Guid.NewGuid():N}";

    // ExtractAttribute, ExtractAttributeValue und ExtractElement sind entfallen.
    // Sie fanden Attribute und Elemente irgendwo im Text statt am gemeinten
    // Element, verlangten <body> ohne Attribute und lieferten Entities roh
    // zurück. Ersatz sind die Erweiterungsmethoden in StanzaExtensions, die auf
    // dem geparsten XElement arbeiten.

    // ExtractSaslMechanisms ist entfallen; die Aushandlung liest jetzt über
    // StreamNegotiation aus dem geparsten <features/>.

    private static SubscriptionState ParseSubscription(string? sub) => sub switch
    {
        "to" => SubscriptionState.To,
        "from" => SubscriptionState.From,
        "both" => SubscriptionState.Both,
        "remove" => SubscriptionState.Remove,
        _ => SubscriptionState.None
    };

    /// <summary>
    /// Reisst die Verbindung ohne Close-Handshake ab - simuliert einen
    /// Netzwerkausfall und löst den Reconnect aus.
    /// </summary>
    /// <remarks>
    /// Das Gegenstück zu <c>XMPPSession.Kill()</c> auf der Serverseite. Für
    /// einen Lauf gegen eine <b>fremde</b> Gegenstelle gibt es keinen anderen
    /// Weg: dort lässt sich die Sitzung nicht von der anderen Seite kappen,
    /// und ein ordentliches <see cref="DisconnectAsync"/> ist gerade nicht,
    /// was geprüft werden soll - ein verabschiedeter Stream wird nicht wieder
    /// aufgenommen.
    ///
    /// <c>Abort</c> und nicht <c>CloseAsync</c>: nur das legt den Socket
    /// nieder, ohne ein Close-Frame zu schicken.
    /// </remarks>
    public void KillConnection()
        => _webSocket?.Abort();

    public async Task DisconnectAsync()
    {

        _intentionalDisconnect = true;

        // Stream zuerst sauber schließen, dann abbrechen: SendAsync nutzt das
        // Token der Verbindung, ein vorheriges Cancel würde das <close/>
        // verhindern.
        try
        {
            var webSocket = _webSocket;

            if (webSocket?.State == WebSocketState.Open)
            {
                await SendAsync("<close xmlns='urn:ietf:params:xml:ns:xmpp-framing'/>");

                try
                {
                    using var closeCts = new CancellationTokenSource(CloseHandshakeTimeout);
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Goodbye", closeCts.Token);
                }
                catch (Exception ex)
                {
                    // Gegenstelle antwortet nicht auf das Close-Frame - Socket hart beenden,
                    // sonst blockiert der Abbau unbegrenzt.
                    _logger.LogDebug(ex, "Close-Handshake nicht abgeschlossen, breche Socket ab");
                    webSocket.Abort();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Fehler beim Schließen der Verbindung (ignoriert)");
        }

        await ShutdownConnectionAsync();

        SetState(ConnectionState.Disconnected);

    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();

        _sendLock.Dispose();
    }

}
