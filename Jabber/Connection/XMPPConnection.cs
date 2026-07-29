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

    private readonly string _wsUri;
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
    public string WebSocketUri => _wsUri;
    public List<string> ServerFeatures { get; } = [];

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
    public event Action<string, string, string, string?, MessageType>? OnMessage;  // from, to, body, id, type
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
    /// <param name="wsUri">WebSocket-Endpunkt; ohne Angabe wss://{domain}:5443/ws (ejabberd-Default)</param>
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

        _wsUri     = wsUri ?? $"wss://{_domain}:5443/ws";

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
    /// Alternative: Verbindung über klassischen TCP mit STARTTLS
    /// </summary>
    /// <remarks>
    /// NICHT funktionsfähig: Der erzeugte tcp://-URI wird von ClientWebSocket abgelehnt.
    /// </remarks>
    public static XMPPConnection CreateTcp(string jid, string password, string? server = null, int port = 5222)
    {
        // Fallback auf TCP - wird intern anders behandelt
        var conn = new XMPPConnection(jid, password, $"tcp://{server ?? jid.Split('@')[1]}:{port}");
        return conn;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _intentionalDisconnect = false;
        _reconnectAttempts = 0;

        // Ein bewusster Verbindungsaufbau hebt die Sperre aus einem früheren
        // Stream-Fehler auf: der Aufrufer weiss, was er tut.
        _fatalStreamError = false;

        await ConnectInternalAsync(ct);
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

        try
        {
            // WebSocket verbinden
            var webSocket = new ClientWebSocket();
            webSocket.Options.AddSubProtocol("xmpp");  // RFC 7395

            if (ServerCertificateValidator is not null)
                webSocket.Options.RemoteCertificateValidationCallback = ServerCertificateValidator;

            _webSocket = webSocket;

            _logger.LogInformation("Verbinde zu {WebSocketUri} ...", _wsUri);
            await webSocket.ConnectAsync(new Uri(_wsUri), ct);
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
            SetState(ConnectionState.Disconnected);
            _logger.LogError(ex, "Authentifizierungsfehler");
            OnError?.Invoke($"Authentifizierungsfehler: {ex.Message}");
            // KEIN Reconnect bei Auth-Fehlern!
        }
        catch (Exception ex)
        {
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
    private async Task<XElement> ReceiveElementAsync(CancellationToken ct)
    {

        var xml = await ReceiveStanzaAsync(ct);

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

        var element = await ReceiveElementAsync(ct);

        if (StreamNegotiation.IsStreamOpen(element))
            element = await ReceiveElementAsync(ct);

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

        StreamManagement.ProcessFailed();

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

    private async Task<string> ReceiveStanzaAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();

        WebSocketReceiveResult result;
        do
        {
            result = await _webSocket!.ReceiveAsync(buffer, ct);

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
                    StreamManagement?.ProcessFailed();
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

            OnMessage?.Invoke(from, to, body, msgId, messageType);

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
            if (element.Child(DiscoManager.InfoNamespace, "query") is not null && Disco is not null)
            {
                _ = Disco.RespondInfoAsync(id, from);
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

        var response = await ReceiveElementAsync(ct);

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
        var challenge = await ReceiveElementAsync(ct);

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
        var final = await ReceiveElementAsync(ct);

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

        var ver = versioniert
                      ? $" ver='{XmlEscaping.Escape(Roster.Version ?? "")}'"
                      : "";

        var response = await SendIqAsync(
                           "roster1",
                           "<iq type='get' id='roster1'>" +
                           $"<query xmlns='{RosterStanzaBuilder.Namespace}'{ver}/>" +
                           "</iq>",
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
                                               MessageType  type            = MessageType.Chat)
    {
        var messageId = GenerateMessageId();

        var typeAttr = type.AsAttribute() is string t ? $" type='{t}'" : "";

        var sb = new StringBuilder();
        sb.Append($"<message to='{XmlEscaping.Escape(to)}'{typeAttr} id='{messageId}'>");
        sb.Append($"<body>{XmlEscaping.Escape(body)}</body>");

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

    // PubSub Operations
    public async Task PubSubSubscribeAsync(string nodeId, string? service = null)
    {
        await SendAsync(PubSubBuilder.Subscribe(service ?? PubSub!.PubSubService, nodeId, BareJid));
        PubSub!.AddSubscription(nodeId);
    }

    public async Task PubSubUnsubscribeAsync(string nodeId, string? service = null)
    {
        await SendAsync(PubSubBuilder.Unsubscribe(service ?? PubSub!.PubSubService, nodeId, BareJid));
        PubSub!.RemoveSubscription(nodeId);
    }

    public async Task PubSubPublishAsync(string nodeId, string itemId, string payload, string? service = null)
    {
        await SendAsync(PubSubBuilder.Publish(service ?? PubSub!.PubSubService, nodeId, itemId, payload));
    }

    public async Task PubSubCreateNodeAsync(string nodeId, string? service = null)
    {
        await SendAsync(PubSubBuilder.CreateNode(service ?? PubSub!.PubSubService, nodeId));
    }

    public async Task PubSubDeleteNodeAsync(string nodeId, string? service = null)
    {
        await SendAsync(PubSubBuilder.DeleteNode(service ?? PubSub!.PubSubService, nodeId));
    }

    public async Task PubSubGetItemsAsync(string nodeId, int? maxItems = null, string? service = null)
    {
        await SendAsync(PubSubBuilder.GetItems(service ?? PubSub!.PubSubService, nodeId, maxItems));
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
