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
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.WebSocket;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    // Siehe XMPPServer.cs für die Begründung: ein Namespace-Member schlägt
    // einen Alias der Compilation Unit, der Alias muss deshalb hier erneut
    // stehen.
    using IPAddress = System.Net.IPAddress;

    /// <summary>
    /// Verbindet <see cref="XMPPServer"/>-Instanzen über echtes WebSocket-S2S
    /// miteinander - Gegenstück zu <see cref="DirectServerLinks"/>, nur mit
    /// einem Netz dazwischen.
    /// </summary>
    /// <remarks>
    /// Der Namensraum der Rahmung ist derselbe wie für Clients (RFC 7395);
    /// unterschieden wird über das WebSocket-Subprotokoll. RFC 7395 ist auf
    /// browserbasierte Clients zugeschnitten und sagt zu S2S nichts - "xmpp-server"
    /// ist deshalb keine Norm, sondern diese Implementierung. Das ist nach dem
    /// Arbeitsplan bewusst so: WebSocket-S2S soll nur Instanzen dieses Servers
    /// miteinander verbinden, nicht mit ejabberd oder Prosody sprechen. Wer das
    /// braucht, nimmt die TCP-Rahmung.
    ///
    /// Was diese Klasse liefert: Verbindungsaufbau, TLS, das Aufteilen der
    /// WebSocket-Rahmen in <see cref="S2SStream"/>-Frames, Verbindungs-Cache
    /// je Domain. Was sie <b>nicht</b> liefert - noch nicht -: Dialback. Die
    /// Domain der Gegenstelle wird über <see cref="AddPeer"/> von Hand
    /// hinterlegt, wie bei <see cref="DirectServerLinks"/>; die Absenderprüfung
    /// in <see cref="XMPPServer.AcceptFromRemoteAsync"/> ist trotzdem scharf.
    /// </remarks>
    public sealed class WebSocketServerLinks : IServerLinks, IAsyncDisposable
    {

        #region Data

        /// <summary>RFC 7395, Abschnitt 3.1 - dieselbe Rahmung wie für Clients.</summary>
        private const String FramingNamespace = S2SStream.FramingNamespace;

        /// <summary>Das WebSocket-Subprotokoll, über das S2S sich vom Client-Zugang unterscheidet.</summary>
        internal const String S2SSubprotocol = "xmpp-server";

        private readonly XMPPServer                          _localServer;
        private readonly S2SWebSocketListener                _listener;
        private readonly Dictionary<String, PeerConfig>      _peers      = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<String, OutboundSlot>    _outbound   = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock                                _lock       = new();

        private Int32 _bidiDeliveries;

        private sealed record PeerConfig(String Uri, RemoteCertificateValidationCallback? Validator);

        private sealed record OutboundLink(ClientWebSocket Socket, S2SStream Stream);

        /// <summary>
        /// Ein Platz im Verbindungs-Cache. Nicht der <c>Task</c> selbst, weil
        /// aufgeräumt werden muss, <b>während</b> der Aufbau noch läuft.
        /// </summary>
        /// <remarks>
        /// Zuvor stand hier der Task, und entfernt wurde nur, wenn er bereits
        /// erfolgreich abgeschlossen war. Stirbt der Stream aber noch im
        /// Aufbau - was mit Dialback der Normalfall wurde, weil der Aufbau nun
        /// mehrere Umläufe dauert -, blieb der Eintrag für immer stehen und
        /// jede weitere Zustellung an diese Domain bekam die tote Verbindung
        /// zurück. Über die Identität des Platzes lässt sich sicher
        /// aufräumen, ohne versehentlich einen inzwischen neu angelegten
        /// Eintrag zu treffen.
        /// </remarks>
        private sealed class OutboundSlot
        {
            public Task<OutboundLink?>? Connecting;
        }

        #endregion

        #region Properties

        /// <summary>Der Port, auf dem eingehende S2S-Verbindungen erwartet werden.</summary>
        public Int32 Port { get; }

        /// <summary>
        /// Das Dialback-Geheimnis dieses Servers (XEP-0220). Es entsteht beim
        /// Anlegen und verlässt den Prozess nie.
        /// </summary>
        public String DialbackSecret { get; } = DialbackKey.NewSecret();

        /// <summary>Das Zertifikat, mit dem der eingehende Zweig TLS spricht, oder null.</summary>
        public X509Certificate2? Certificate { get; }

        /// <summary>Die eigene S2S-Adresse, für <see cref="AddPeer"/> auf der Gegenseite bestimmt.</summary>
        public String Uri => $"{(Certificate is not null ? "wss" : "ws")}://localhost:{Port}/s2s/";

        /// <summary>
        /// Anzahl der jemals eingegangenen S2S-Verbindungen - unabhängig von
        /// den Client-Verbindungen des Servers, die
        /// <see cref="XMPPServer.ConnectionCount"/> zählt.
        /// </summary>
        public Int32 InboundConnectionCount => _listener.ConnectionCounter;

        /// <summary>
        /// XEP-0288: die Rückrichtung auf <b>eingehenden</b> Verbindungen
        /// anbieten.
        /// </summary>
        /// <remarks>
        /// Dieselbe Erweiterung wie bei <see cref="TcpServerLinks"/> und aus
        /// demselben Grund - die Protokollschicht darunter ist ohnehin
        /// dieselbe. Hier fällt sie im Betrieb weniger ins Gewicht, weil an
        /// beiden Enden dieses WebSocket-Transports Instanzen dieses Servers
        /// hängen, die einander eingetragen haben.
        ///
        /// Getrennt von <see cref="RequestBidirectionalStreams"/>, weil es
        /// zwei verschiedene Dinge sind: hier sagen wir einer anwählenden
        /// Gegenstelle, dass sie uns über ihre eigene Verbindung antworten
        /// darf; dort erbitten wir dasselbe von einer Gegenstelle, die wir
        /// anwählen. Zusammengeschaltet waren sie nicht bloss unscharf - es
        /// war damit unmöglich, unsere Ankündigung überhaupt zu beobachten:
        /// solange unsere ausgehende Verbindung die Rückrichtung nutzt, wählt
        /// die Gegenstelle uns gar nicht erst an.
        /// </remarks>
        public Boolean OfferBidirectionalStreams { get; init; }

        /// <summary>
        /// XEP-0288: die Rückrichtung auf <b>ausgehenden</b> Verbindungen
        /// erbitten.
        /// </summary>
        /// <remarks>
        /// Sinnvoll, wenn die Gegenstelle uns nicht erreichen kann. Siehe
        /// <see cref="OfferBidirectionalStreams"/> für die Gegenrichtung.
        /// </remarks>
        public Boolean RequestBidirectionalStreams { get; init; }

        /// <summary>
        /// Wie viele Stanzas über die Rückrichtung eines eingehenden Streams
        /// gingen, statt über eine eigene Verbindung.
        /// </summary>
        public Int32 BidirectionalDeliveryCount => Volatile.Read(ref _bidiDeliveries);

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Legt den eingehenden Zweig an und startet ihn sofort - ohne
        /// erreichbaren Eingang wäre die Föderation ohnehin nur halb.
        /// </summary>
        /// <param name="localServer">Der Server, dessen S2S-Gegenstelle dies ist.</param>
        /// <param name="port">Fester Port oder 0 für einen freien.</param>
        public WebSocketServerLinks(XMPPServer localServer, Int32 port = 0)
        {

            _localServer  = localServer;
            Port          = port > 0 ? port : FreeTcpPort();
            Certificate   = localServer.Certificate;

            _listener = new S2SWebSocketListener(this, IPPort.Parse(Port), Certificate);
            _listener.Start().GetAwaiter().GetResult();

            localServer.ServerLinks = this;

        }

        #endregion


        #region AddPeer(domain, uri, validator)

        /// <summary>
        /// Macht eine fremde Domain über ihre S2S-Adresse erreichbar.
        /// </summary>
        /// <param name="domain">Die Domain der Gegenstelle.</param>
        /// <param name="uri">Ihre S2S-WebSocket-Adresse.</param>
        /// <param name="validator">
        /// Zertifikatsprüfung für die ausgehende Verbindung; null überlässt sie
        /// dem Betriebssystem.
        /// </param>
        public void AddPeer(String                                domain,
                            String                                uri,
                            RemoteCertificateValidationCallback?  validator = null)
        {
            lock (_lock)
                _peers[domain] = new PeerConfig(uri, validator);
        }

        #endregion

        #region (static) Connect(a, b)

        /// <summary>
        /// Verbindet zwei Server in beide Richtungen - jeder erhält die
        /// S2S-Adresse und das gepinnte Zertifikat des anderen.
        /// </summary>
        /// <remarks>
        /// Legt für einen Server, der noch keine <see cref="WebSocketServerLinks"/>
        /// hat, stillschweigend eine an - dieselbe Bequemlichkeit, die
        /// <see cref="DirectServerLinks.Connect"/> schon bietet.
        /// </remarks>
        public static void Connect(XMPPServer a, XMPPServer b)
        {

            if (String.Equals(a.Domain, b.Domain, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                          $"Beide Server bedienen '{a.Domain}' - eine Föderation mit sich selbst ergibt nichts.",
                          nameof(b));

            var linksA = LinksOf(a);
            var linksB = LinksOf(b);

            linksA.AddPeer(b.Domain, linksB.Uri, b.IsOwnCertificate);
            linksB.AddPeer(a.Domain, linksA.Uri, a.IsOwnCertificate);

        }

        private static WebSocketServerLinks LinksOf(XMPPServer server)

            => server.ServerLinks as WebSocketServerLinks
               ?? new WebSocketServerLinks(server);

        #endregion

        #region DeliverAsync(remoteDomain, stanza, cancellationToken)

        /// <remarks>
        /// Anders als bei <see cref="DirectServerLinks"/> ist true hier keine
        /// Zusicherung, dass die Gegenstelle die Stanza angenommen hat -
        /// S2S kennt kein Ack je Stanza. Es heisst nur: der Stream stand und
        /// der Rahmen wurde geschrieben. Eine Absenderprüfung, die danach
        /// fehlschlägt, beendet den Stream (siehe <see cref="S2SStream"/>),
        /// meldet sich aber nicht mehr an diesen Aufruf zurück - er ist zu dem
        /// Zeitpunkt längst abgeschlossen.
        /// </remarks>
        public async Task<Boolean> DeliverAsync(String             remoteDomain,
                                                String             stanza,
                                                CancellationToken  cancellationToken = default)
        {

            // XEP-0288: trägt eine eingehende Verbindung dieser Domain die
            // Rückrichtung, geht die Stanza dort hinaus - Vorrang vor dem
            // Anwählen, weil die Gegenstelle sich die Rückrichtung genau
            // deshalb erbeten hat.
            // Kein Schalter davor - siehe TcpServerLinks: BidiEnabled setzt
            // beides bereits voraus.
            if (await S2SStream.TryDeliverOverBidiAsync(_listener.InboundStreams(), remoteDomain,
                                                        stanza, cancellationToken))
            {
                Interlocked.Increment(ref _bidiDeliveries);
                return true;
            }

            var link = await GetOrCreateOutboundAsync(remoteDomain, cancellationToken);

            return link is not null &&
                   await link.Stream.SendStanzaAsync(stanza, cancellationToken);

        }

        #endregion


        #region (internal) VerifyDialbackKeyAsync(senderDomain, streamId, key)

        /// <summary>
        /// XEP-0220, Schritt 2 und 3: fragt den autoritativen Server der
        /// Absenderdomain, ob er diesen Schlüssel ausgestellt hat.
        /// </summary>
        /// <remarks>
        /// <b>Hier steckt der ganze Wert von Dialback.</b> Die Adresse, an die
        /// gefragt wird, stammt aus der Gegenstellenliste dieses Servers -
        /// also aus der Konfiguration des Betreibers - und <b>nicht</b> von
        /// dem, der sich gerade ausweisen will. Wer sich fälschlich für eine
        /// Domain ausgibt, wird deshalb nie selbst gefragt: die Frage geht an
        /// den echten Server dieser Domain, der den Schlüssel nicht
        /// wiedererkennt und ihn ablehnt.
        ///
        /// Das ist zugleich der Unterschied zum Dialback des XEP: dort ersetzt
        /// eine DNS-Auflösung (SRV auf die Absenderdomain) diese Liste. DNS
        /// fehlt hier noch - die Liste ist der Ersatz, und für den Zweck ein
        /// strengerer, weil sie signiert ist durch die Hand, die sie gepflegt
        /// hat, statt durch ein unauthentifiziertes Protokoll. Was sie nicht
        /// leistet: sich selbst zu füllen. Eine unbekannte Domain kann nicht
        /// geprüft und deshalb nicht angenommen werden.
        ///
        /// Die Verbindung dafür ist eigen und kurzlebig. Sie darf nicht die
        /// zwischengespeicherte Stanza-Verbindung sein - die will sich
        /// ihrerseits gerade erst ausweisen, und beide aufeinander warten zu
        /// lassen wäre eine Verklemmung.
        /// </remarks>
        internal async Task<Boolean> VerifyDialbackKeyAsync(String senderDomain,
                                                            String streamId,
                                                            String key)
        {

            PeerConfig? peer;

            lock (_lock)
                _peers.TryGetValue(senderDomain, out peer);

            // Keine hinterlegte Adresse - dann gibt es niemanden, den man
            // fragen könnte, und Glauben ist keine Prüfung.
            if (peer is null)
                return false;

            ClientWebSocket? socket = null;

            try
            {

                socket = new ClientWebSocket();
                socket.Options.AddSubProtocol(S2SSubprotocol);

                if (peer.Validator is not null)
                    socket.Options.RemoteCertificateValidationCallback = peer.Validator;

                using var cts = new CancellationTokenSource(VerificationTimeout);

                await socket.ConnectAsync(new Uri(peer.Uri), cts.Token);

                var stream = S2SStream.InitiateVerification(
                                 _localServer.Domain,
                                 senderDomain,
                                 (frame, ct) => SendFrameAsync(socket, frame, ct));

                var pumping = PumpVerificationFramesAsync(socket, stream);

                await stream.OpenAsync(cts.Token);

                if (!await stream.WaitUntilOpenAsync(VerificationTimeout, cts.Token))
                    return false;

                return await stream.RequestVerificationAsync(
                           targetDomain:  _localServer.Domain,
                           streamId:      streamId,
                           key:           key,
                           Timeout:       VerificationTimeout,
                           cancellationToken: cts.Token);

            }
            catch (Exception)
            {
                // XEP-0220, Abschnitt 2.4 kennt dafür <remote-server-timeout/>;
                // für den Aufrufer ist das Ergebnis dasselbe: nicht belegt.
                return false;
            }
            finally
            {
                try { socket?.Dispose(); }
                catch { /* egal */ }
            }

        }

        /// <summary>Wie lange die Nachfrage beim autoritativen Server dauern darf.</summary>
        private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Liest die Antwort des autoritativen Servers, bis der
        /// Verifikationsstream endet.
        /// </summary>
        private static async Task PumpVerificationFramesAsync(ClientWebSocket socket, S2SStream stream)
        {

            var buffer = new Byte[8192];

            try
            {

                while (socket.State == WebSocketState.Open && !stream.IsClosed)
                {

                    var sb = new StringBuilder();
                    WebSocketReceiveResult result;

                    do
                    {

                        result = await socket.ReceiveAsync(buffer, CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                            break;

                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    if (sb.Length > 0)
                        await stream.ProcessFrameAsync(sb.ToString());

                }

            }
            catch (Exception)
            {
                // Verbindung weg - Abort unten weckt einen etwaigen Wartenden.
            }

            stream.Abort("Verifikationsverbindung beendet");

        }

        #endregion

        #region (private) GetOrCreateOutboundAsync(remoteDomain, cancellationToken)

        /// <summary>
        /// Liefert die bestehende ausgehende Verbindung zu einer Domain oder
        /// baut eine neue auf.
        /// </summary>
        /// <remarks>
        /// Der Aufbau steht als <c>Task</c> im Cache, nicht erst sein Ergebnis -
        /// sonst könnten zwei gleichzeitige Zustellungen an dieselbe Domain
        /// zwei Verbindungen aufbauen.
        /// </remarks>
        private Task<OutboundLink?> GetOrCreateOutboundAsync(String              remoteDomain,
                                                             CancellationToken   cancellationToken)
        {

            lock (_lock)
            {

                if (_outbound.TryGetValue(remoteDomain, out var existing))
                    return existing.Connecting!;

                if (!_peers.TryGetValue(remoteDomain, out var peer))
                    return Task.FromResult<OutboundLink?>(null);

                var slot = new OutboundSlot();
                _outbound[remoteDomain] = slot;

                slot.Connecting = ConnectOutboundAsync(remoteDomain, peer, slot, cancellationToken);

                return slot.Connecting;

            }

        }

        #endregion

        #region (private) ConnectOutboundAsync(remoteDomain, peer, cancellationToken)

        private async Task<OutboundLink?> ConnectOutboundAsync(String              remoteDomain,
                                                               PeerConfig          peer,
                                                               OutboundSlot        slot,
                                                               CancellationToken   cancellationToken)
        {

            try
            {

                var socket = new ClientWebSocket();
                socket.Options.AddSubProtocol(S2SSubprotocol);

                if (peer.Validator is not null)
                    socket.Options.RemoteCertificateValidationCallback = peer.Validator;

                await socket.ConnectAsync(new Uri(peer.Uri), cancellationToken);

                var stream = S2SStream.Initiate(
                                 _localServer.Domain,
                                 remoteDomain,
                                 (frame, ct) => SendFrameAsync(socket, frame, ct),
                                 secret:         DialbackSecret,

                                 // XEP-0288: was über die Rückrichtung
                                 // hereinkommt, nimmt denselben Weg wie auf
                                 // einer eingehenden Verbindung, samt
                                 // Absenderprüfung.
                                 deliverStanza:  (peerDomain, stanza)
                                                     => _localServer.AcceptFromRemoteAsync(peerDomain, stanza),
                                 useBidi:        RequestBidirectionalStreams);

                stream.OnClosed += _ => DropOutbound(remoteDomain, slot);

                _ = PumpIncomingFramesAsync(socket, stream, remoteDomain, slot);

                await stream.OpenAsync(cancellationToken);

                if (!await stream.WaitUntilOpenAsync(OutboundHandshakeTimeout, cancellationToken))
                {
                    DropOutbound(remoteDomain, slot);
                    return null;
                }

                // XEP-0220: erst wenn die Gegenstelle unsere Domain bestätigt
                // hat, ist der Stream brauchbar. Vorher zugestellte Stanzas
                // würde sie ohnehin verwerfen.
                if (!await stream.WaitUntilAuthenticatedAsync(OutboundHandshakeTimeout, cancellationToken))
                {
                    stream.Abort("Dialback nicht abgeschlossen");
                    DropOutbound(remoteDomain, slot);
                    return null;
                }

                return new OutboundLink(socket, stream);

            }
            catch (Exception)
            {

                DropOutbound(remoteDomain, slot);

                return null;

            }

        }

        /// <summary>Wie lange auf das <c>&lt;open/&gt;</c> der Gegenstelle gewartet wird.</summary>
        private static readonly TimeSpan OutboundHandshakeTimeout = TimeSpan.FromSeconds(10);

        #endregion

        #region (private) PumpIncomingFramesAsync / SendFrameAsync / RemoveOutbound

        /// <summary>
        /// Liest WebSocket-Rahmen vom ausgehenden Socket und reicht sie an den
        /// Stream weiter, bis die Verbindung endet.
        /// </summary>
        private async Task PumpIncomingFramesAsync(ClientWebSocket  socket,
                                                   S2SStream        stream,
                                                   String           remoteDomain,
                                                   OutboundSlot     slot)
        {

            var buffer = new Byte[8192];

            try
            {

                while (socket.State == WebSocketState.Open)
                {

                    var sb = new StringBuilder();
                    WebSocketReceiveResult result;

                    do
                    {

                        result = await socket.ReceiveAsync(buffer, CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                            break;

                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    var frame = sb.ToString();

                    if (frame.Length > 0)
                        await stream.ProcessFrameAsync(frame);

                    // Ein Stream-Fehler der Gegenstelle schliesst den Stream,
                    // ohne die WebSocket-Verbindung zu beenden - RFC 6120,
                    // Abschnitt 4.9 verlangt aber genau das. Ohne diesen
                    // Ausstieg liefe die Schleife weiter auf ein ReceiveAsync,
                    // das nie wieder etwas bekommt.
                    if (stream.IsClosed)
                        break;

                }

            }
            catch (Exception)
            {
                // Socket weg - der Stream erfährt es unten über Abort.
            }

            stream.Abort("Ausgehende WebSocket-Verbindung beendet");
            DropOutbound(remoteDomain, slot);

            try { socket.Dispose(); }
            catch { /* egal */ }

        }

        private async Task SendFrameAsync(ClientWebSocket socket, String frame, CancellationToken ct)
        {

            if (socket.State != WebSocketState.Open)
                return;

            var bytes = Encoding.UTF8.GetBytes(frame);

            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);

        }

        /// <summary>
        /// Räumt einen Platz aus dem Verbindungs-Cache, wenn er noch derselbe
        /// ist - unabhängig davon, ob der Aufbau schon fertig war.
        /// </summary>
        private void DropOutbound(String remoteDomain, OutboundSlot slot)
        {

            lock (_lock)
            {

                if (_outbound.TryGetValue(remoteDomain, out var current) &&
                    ReferenceEquals(current, slot))
                {
                    _outbound.Remove(remoteDomain);
                }

            }

        }

        #endregion


        #region (private class) S2SWebSocketListener

        /// <summary>
        /// Der eingehende Zweig - nimmt WebSocket-Verbindungen mit dem
        /// S2S-Subprotokoll an und hält je Verbindung einen empfangenden
        /// <see cref="S2SStream"/>.
        /// </summary>
        private sealed class S2SWebSocketListener : AWebSocketServer
        {

            #region Data

            private readonly WebSocketServerLinks  _links;
            private readonly Lock                  _lock = new();

            /// <summary>
            /// Die Streams je Verbindung - <b>ausdrücklich</b> nach
            /// Referenzgleichheit.
            /// </summary>
            /// <remarks>
            /// Hermods <c>WebSocketServerConnection</c> vergleicht sich über
            /// <c>LocalSocket</c>, und der ist bei einem Listener für jede
            /// angenommene Verbindung derselbe: aus Sicht eines gewöhnlichen
            /// Dictionary sind damit <b>alle</b> eingehenden Verbindungen ein
            /// und dieselbe. Ohne diesen Vergleicher bekam die zweite
            /// eingehende Verbindung den Stream der ersten zurück - samt deren
            /// Sendefunktion, die auf einen längst geschlossenen Socket
            /// schrieb. Die Antwort ging dann ins Leere und die Gegenstelle
            /// wartete bis ins Zeitlimit.
            ///
            /// <see cref="XMPPServer"/> geht demselben Problem seit jeher mit
            /// einem <c>ReferenceEquals</c> über eine Liste aus dem Weg.
            /// </remarks>
            private readonly Dictionary<WebSocketServerConnection, S2SStream> _streams = new(ByReference.Instance);

            private sealed class ByReference : IEqualityComparer<WebSocketServerConnection>
            {

                public static readonly ByReference Instance = new();

                public Boolean Equals(WebSocketServerConnection? a, WebSocketServerConnection? b)
                    => ReferenceEquals(a, b);

                public Int32 GetHashCode(WebSocketServerConnection connection)
                    => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(connection);

            }

            private Int32 _connectionCounter;

            #endregion

            /// <summary>Anzahl der jemals angenommenen S2S-Verbindungen.</summary>
            public Int32 ConnectionCounter => Volatile.Read(ref _connectionCounter);

            #region Constructor(s)

            public S2SWebSocketListener(WebSocketServerLinks  links,
                                        IPPort                port,
                                        X509Certificate2?     certificate)

                : base(TCPPort:                     port,
                       ServerCertificateSelector:    certificate is not null
                                                          ? (_, _) => certificate
                                                          : null,
                       RequireAuthentication:        false,
                       SecWebSocketProtocols:        [S2SSubprotocol],
                       AutoStart:                    false)

            {

                _links = links;

                // Ohne das bliebe je beendeter Verbindung ein Stream in der
                // Tabelle stehen - unauffällig, aber unbegrenzt.
                OnTCPConnectionClosed += (timestamp, server, connection, eventTrackingId, reason, ct) =>
                {

                    S2SStream? stream;

                    lock (_lock)
                    {
                        _streams.Remove(connection, out stream);
                    }

                    stream?.Abort("Eingehende WebSocket-Verbindung beendet");

                    return Task.CompletedTask;

                };

            }

            #endregion

            /// <summary>
            /// Eine Momentaufnahme der offenen eingehenden Streams - für
            /// XEP-0288 die einzige Stelle, an der sich einer für die
            /// Rückrichtung finden lässt.
            /// </summary>
            /// <remarks>
            /// Eine Kopie, damit die Sperre nicht über das Senden gehalten
            /// wird: eine langsame Gegenstelle hielte sonst jede weitere
            /// Zustellung auf, auch die an ganz andere Domains.
            /// </remarks>
            internal IReadOnlyList<S2SStream> InboundStreams()
            {
                lock (_lock)
                    return [.. _streams.Values];
            }

            private S2SStream StreamOf(WebSocketServerConnection connection)
            {

                lock (_lock)
                {

                    if (_streams.TryGetValue(connection, out var existing))
                        return existing;

                    Interlocked.Increment(ref _connectionCounter);

                    var stream = S2SStream.Accept(
                                     _links._localServer.Domain,
                                     (frame, ct) => SendTextMessage(connection, frame),
                                     (peerDomain, stanza) => _links._localServer.AcceptFromRemoteAsync(peerDomain, stanza),
                                     secret:     _links.DialbackSecret,
                                     verifyKey:  _links.VerifyDialbackKeyAsync,
                                     offerBidi:  _links.OfferBidirectionalStreams);

                    stream.OnClosed += reason =>
                    {

                        lock (_lock)
                            _streams.Remove(connection);

                        // RFC 6120, Abschnitt 4.9: ein beendeter Stream nimmt
                        // die Verbindung mit. Ohne das bliebe die WebSocket-
                        // Verbindung offen, obwohl auf ihr protokollseitig
                        // nichts mehr passiert - ein Leck, kein Fehlerfall,
                        // der irgendwann auffiele.
                        _ = Task.Run(async () =>
                        {
                            try { await connection.Close(); }
                            catch { /* egal */ }
                        });

                    };

                    _streams[connection] = stream;

                    return stream;

                }

            }

            public override async Task ProcessTextMessage(DateTimeOffset             Timestamp,
                                                          AWebSocketServer           Server,
                                                          WebSocketServerConnection  Connection,
                                                          EventTracking_Id           EventTrackingId,
                                                          WebSocketFrame             TextFrame,
                                                          String                     TextMessage,
                                                          CancellationToken          CancellationToken)
            {

                var stream = StreamOf(Connection);

                try
                {
                    await stream.ProcessFrameAsync(TextMessage, CancellationToken);
                }
                catch (Exception)
                {
                    // Verbindung abgerissen - wie beim Client-Zugang der Normalfall.
                }

            }

        }

        #endregion

        #region (private) FreeTcpPort()

        private static Int32 FreeTcpPort()
        {

            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((System.Net.IPEndPoint) l.LocalEndpoint).Port;
            l.Stop();

            return port;

        }

        #endregion


        #region DisposeAsync()

        public async ValueTask DisposeAsync()
        {

            List<Task<OutboundLink?>> outbound;

            lock (_lock)
                outbound = [.. _outbound.Values
                                        .Select(slot => slot.Connecting)
                                        .Where(task => task is not null)
                                        .Cast<Task<OutboundLink?>>()];

            foreach (var task in outbound)
            {

                try
                {

                    var link = await task;

                    if (link is not null)
                    {
                        link.Stream.Abort("Server wird beendet");
                        try { link.Socket.Dispose(); }
                        catch { /* egal */ }
                    }

                }
                catch (Exception)
                {
                    // Verbindungsaufbau war ohnehin schon gescheitert.
                }

            }

            try { await _listener.Shutdown(Wait: true); }
            catch { /* egal */ }

        }

        #endregion

    }

}
