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
using System.Security.Cryptography.X509Certificates;
using System.Text;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    // Siehe XMPPServer.cs: Hermod bringt einen eigenen Typ IPAddress mit, der
    // den gleichnamigen aus System.Net verdeckt. Der Alias muss innerhalb der
    // Namespace-Deklaration stehen, sonst gewinnt das Namespace-Member.
    using IPAddress = System.Net.IPAddress;

    /// <summary>
    /// Server-zu-Server über die klassische Rahmung: TCP, Port 5269,
    /// <c>jabber:server</c>-Streams (RFC 6120).
    /// </summary>
    /// <remarks>
    /// Dieselbe Protokollschicht wie <see cref="WebSocketServerLinks"/> - es
    /// wechselt nur, was darunter liegt: <see cref="TcpStreamFraming"/> statt
    /// <see cref="WebSocketFraming"/> und <see cref="XmlStreamSplitter"/> statt
    /// fertiger WebSocket-Rahmen. Dialback, Absenderprüfung,
    /// Verbindungsverwaltung und Fehlerbehandlung stehen unverändert in
    /// <see cref="S2SStream"/>.
    ///
    /// <b>Das ist der Weg zu fremden Servern.</b> ejabberd und Prosody sprechen
    /// genau das; die WebSocket-Strecke verbindet nur Instanzen dieses Servers
    /// miteinander.
    ///
    /// <b>Wie TLS zustande kommt, entscheidet <see cref="TcpTlsMode"/>.</b>
    /// Vorgabe ist STARTTLS (RFC 6120, Abschnitt 5.4): der Stream beginnt im
    /// Klartext, handelt Verschlüsselung aus und fängt danach von vorn an.
    /// <see cref="TcpTlsMode.Direct"/> spart die Aushandlung und ist zwischen
    /// zwei Instanzen dieses Servers das Einfachere.
    ///
    /// Die Aushandlung selbst steht hier im Transport und nicht in
    /// <see cref="S2SStream"/>. Das ist kein Zufall: der Stream vor TLS ist ein
    /// Wegwerfstream, dessen Zustand nach der Verschlüsselung verworfen wird
    /// (Abschnitt 5.4.3.3). Die Protokollschicht bekommt den Strom erst, wenn
    /// er verschlüsselt ist, und muss von der Aushandlung nichts wissen - und
    /// bekommt so auch keine Gelegenheit, versehentlich etwas aus der
    /// Klartextphase zu übernehmen.
    /// </remarks>
    public sealed class TcpServerLinks : IServerLinks, IAsyncDisposable
    {

        #region Data

        private readonly XMPPServer                       _localServer;
        private readonly TcpListener                      _listener;
        private readonly CancellationTokenSource          _cts        = new();
        private readonly Dictionary<String, PeerConfig>   _peers      = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<String, OutboundSlot> _outbound   = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock                             _lock       = new();

        private Int32 _inboundCounter;
        private Int32 _dialbackVerifications;

        private sealed record PeerConfig(String                                Host,
                                         Int32                                 Port,
                                         TcpTlsMode                            Mode,
                                         RemoteCertificateValidationCallback?  Validator);

        private sealed class OutboundSlot
        {
            public Task<S2SStream?>? Connecting;
        }

        #endregion

        #region Properties

        /// <summary>Der Port, auf dem eingehende S2S-Verbindungen erwartet werden.</summary>
        public Int32 Port { get; }

        /// <summary>Das Zertifikat für eingehende Verbindungen, oder null für Klartext.</summary>
        public X509Certificate2? Certificate { get; }

        /// <summary>Wie eingehende Verbindungen zu TLS kommen.</summary>
        public TcpTlsMode Mode { get; }

        /// <summary>
        /// Soll die Domain der Gegenstelle ueber ihr TLS-Zertifikat belegt
        /// werden (SASL-EXTERNAL, XEP-0178) statt ueber Dialback?
        /// </summary>
        /// <remarks>
        /// Setzt gegenseitiges TLS voraus - ohne Klientzertifikat gibt es
        /// nichts zu pruefen. Ist es eingeschaltet und legt die Gegenstelle
        /// keines vor, bleibt Dialback der Weg; das Angebot unterbleibt dann
        /// einfach.
        /// </remarks>
        public Boolean UseSaslExternal { get; init; }

        /// <summary>Das Dialback-Geheimnis dieses Servers (XEP-0220).</summary>
        public String DialbackSecret { get; } = DialbackKey.NewSecret();

        /// <summary>
        /// Woher die Adresse einer Domain kommt, die nicht von Hand
        /// hinterlegt ist. Null lässt es bei der Gegenstellenliste.
        /// </summary>
        /// <remarks>
        /// Die Liste geht vor. Das ist Absicht und keine Bequemlichkeit: ein
        /// Eintrag von Hand ist eine Entscheidung des Betreibers, eine
        /// DNS-Antwort nur eine Auskunft aus dem Netz - und ohne DNSSEC eine
        /// unbeglaubigte. Wer beides hat, soll die Entscheidung behalten.
        ///
        /// <b>Für die Dialback-Rückfrage verschiebt das die Vertrauenswurzel.</b>
        /// Bisher stand dort ausschliesslich die Liste des Betreibers, und
        /// genau daraus bezog die Prüfung ihre Schärfe. Wird die autoritative
        /// Adresse über DNS gesucht, ist Dialback nur noch so verlässlich wie
        /// die Auflösung - so ist XEP-0220 gemeint, aber es ist weniger, als
        /// die Liste bot. Wer das nicht will, lässt diese Eigenschaft null und
        /// trägt seine Gegenstellen ein.
        /// </remarks>
        public IS2SAddressResolver? AddressResolver { get; init; }

        /// <summary>Anzahl der jemals angenommenen eingehenden Verbindungen.</summary>
        public Int32 InboundConnectionCount => Volatile.Read(ref _inboundCounter);

        /// <summary>
        /// Wie oft dieser Server einen Dialback-Schlüssel beim autoritativen
        /// Server nachgefragt hat.
        /// </summary>
        /// <remarks>
        /// Der einzige von aussen sichtbare Unterschied zwischen Dialback und
        /// SASL-EXTERNAL: das eine ruft zurück, das andere liest das
        /// Zertifikat. Die Zahl der Verbindungen taugt dafür nicht - über die
        /// Grenze läuft noch anderes, etwa die automatische
        /// Empfangsbestätigung des Clients.
        /// </remarks>
        public Int32 DialbackVerificationCount => Volatile.Read(ref _dialbackVerifications);

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Legt den eingehenden Zweig an und nimmt sofort Verbindungen an.
        /// </summary>
        /// <param name="localServer">Der Server, dessen S2S-Gegenstelle dies ist.</param>
        /// <param name="port">Fester Port, oder 0 für einen freien. Vorgesehen ist 5269.</param>
        /// <param name="mode">
        /// Wie TLS zustande kommt. Vorgabe ist STARTTLS, weil das der Weg aus
        /// RFC 6120, Abschnitt 5.4 ist und weil fremde Server ihn erwarten.
        /// </param>
        public TcpServerLinks(XMPPServer  localServer,
                              Int32       port   = 0,
                              TcpTlsMode  mode   = TcpTlsMode.StartTls)
        {

            _localServer  = localServer;
            Mode          = mode;
            Certificate   = mode == TcpTlsMode.None ? null : localServer.Certificate;

            _listener     = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();

            Port          = ((IPEndPoint) _listener.LocalEndpoint).Port;

            _ = AcceptLoopAsync();

            localServer.ServerLinks = this;

        }

        #endregion


        #region AddPeer(domain, host, port, useTLS, validator)

        /// <summary>
        /// Macht eine fremde Domain über Rechnername und Port erreichbar.
        /// </summary>
        /// <remarks>
        /// Von Hand, weil die Auflösung über SRV-Records
        /// (<c>_xmpp-server._tcp</c>, RFC 6120 Abschnitt 3.2.1) noch fehlt.
        /// Diese Liste ist zugleich das, was bei der Dialback-Prüfung an die
        /// Stelle des DNS tritt - siehe <see cref="VerifyDialbackKeyAsync"/>.
        ///
        /// <b>Der Rechnername sollte zu einer Adressfamilie auflösen, auf der
        /// die Gegenstelle auch horcht.</b> Dieser Listener bindet
        /// IPv4-Loopback; ein Name wie <c>localhost</c>, der zuerst nach IPv6
        /// auflöst, kostet dann je Verbindung rund zwei Sekunden, bis der
        /// Fallback greift - die Verbindung kommt zustande, nur eben spät.
        /// Genau das hat den ersten Zustellvorgang von 82 auf 4167 Millisekunden
        /// verlängert, und zwar unauffällig, weil am Ende alles funktionierte.
        /// </remarks>
        public void AddPeer(String                                domain,
                            String                                host,
                            Int32                                 port,
                            TcpTlsMode                            mode        = TcpTlsMode.StartTls,
                            RemoteCertificateValidationCallback?  validator   = null)
        {
            lock (_lock)
                _peers[domain] = new PeerConfig(host, port, mode, validator);
        }

        #endregion

        #region (static) Connect(a, b)

        /// <summary>
        /// Verbindet zwei Server über TCP in beide Richtungen.
        /// </summary>
        public static void Connect(XMPPServer  a,
                                   XMPPServer  b,
                                   TcpTlsMode  mode              = TcpTlsMode.StartTls,
                                   Boolean     useSaslExternal   = false)
        {

            if (String.Equals(a.Domain, b.Domain, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                          $"Beide Server bedienen '{a.Domain}' - eine Föderation mit sich selbst ergibt nichts.",
                          nameof(b));

            var linksA = LinksOf(a, mode, useSaslExternal);
            var linksB = LinksOf(b, mode, useSaslExternal);

            // Ausdrücklich die Adresse und nicht "localhost": der Listener
            // bindet IPv4-Loopback, und ein Name, der zuerst nach IPv6
            // auflöst, kostet je Verbindung den Fallback ab.
            var loopback = IPAddress.Loopback.ToString();

            linksA.AddPeer(b.Domain, loopback, linksB.Port, linksB.Mode, b.IsOwnCertificate);
            linksB.AddPeer(a.Domain, loopback, linksA.Port, linksA.Mode, a.IsOwnCertificate);

        }

        private static TcpServerLinks LinksOf(XMPPServer server, TcpTlsMode mode, Boolean useSaslExternal)

            => server.ServerLinks as TcpServerLinks
               ?? new TcpServerLinks(server, mode: mode) { UseSaslExternal = useSaslExternal };

        #endregion

        #region DeliverAsync(remoteDomain, stanza, cancellationToken)

        public async Task<Boolean> DeliverAsync(String             remoteDomain,
                                                String             stanza,
                                                CancellationToken  cancellationToken = default)
        {

            var stream = await GetOrCreateOutboundAsync(remoteDomain, cancellationToken);

            return stream is not null &&
                   await stream.SendStanzaAsync(stanza, cancellationToken);

        }

        #endregion


        #region (internal) VerifyDialbackKeyAsync(senderDomain, streamId, key)

        /// <summary>
        /// XEP-0220, Schritt 2 und 3 - wie bei
        /// <see cref="WebSocketServerLinks"/>, nur über TCP.
        /// </summary>
        /// <remarks>
        /// Auch hier gilt: gefragt wird die hinterlegte Adresse der
        /// Absenderdomain, nicht der, der sich gerade ausweisen will.
        /// </remarks>
        internal async Task<Boolean> VerifyDialbackKeyAsync(String senderDomain,
                                                            String streamId,
                                                            String key)
        {

            Interlocked.Increment(ref _dialbackVerifications);

            foreach (var peer in await KandidatenFuerAsync(senderDomain))
            {

                if (await VerifyAtAsync(peer, senderDomain, streamId, key))
                    return true;

            }

            return false;

        }

        /// <summary>
        /// Die Adressen, unter denen eine Domain für die Dialback-Rückfrage
        /// erreichbar sein könnte.
        /// </summary>
        /// <remarks>
        /// Der Eintrag von Hand geht vor; erst danach die Auflösung. Ohne
        /// beides gibt es niemanden zu fragen - und Glauben ist keine
        /// Prüfung.
        ///
        /// Dass die Rückfrage überhaupt aufgelöst werden muss, ist der
        /// Normalfall aus XEP-0220: der prüfende Server sucht den autoritativen
        /// selbst. Es verschiebt aber die Vertrauenswurzel vom Betreiber ins
        /// DNS - siehe <see cref="AddressResolver"/>.
        /// </remarks>
        private async Task<IReadOnlyList<PeerConfig>> KandidatenFuerAsync(String domain)
        {

            PeerConfig? eingetragen;

            lock (_lock)
                _peers.TryGetValue(domain, out eingetragen);

            if (eingetragen is not null)
                return [eingetragen];

            if (AddressResolver is null)
                return [];

            try
            {

                var ziele = await AddressResolver.ResolveAsync(domain);

                return [.. ziele.Select(z => new PeerConfig(z.Host,
                                                            z.Port,
                                                            Mode,
                                                            DefaultPeerValidator))];

            }
            catch (Exception)
            {
                return [];
            }

        }

        /// <summary>
        /// Fragt eine einzelne Adresse nach dem Dialback-Schlüssel.
        /// </summary>
        private async Task<Boolean> VerifyAtAsync(PeerConfig  peer,
                                                  String      senderDomain,
                                                  String      streamId,
                                                  String      key)
        {

            TcpClient? client = null;

            try
            {

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                cts.CancelAfter(VerificationTimeout);

                client = new TcpClient();
                await client.ConnectAsync(peer.Host, peer.Port, cts.Token);

                var netz = await WrapAsync(client, peer, senderDomain, cts.Token);

                if (netz is null)
                    return false;

                var stream = S2SStream.InitiateVerification(
                                 _localServer.Domain,
                                 senderDomain,
                                 (frame, ct) => SendAsync(netz, frame, ct),
                                 framing: TcpStreamFraming.Instance);

                _ = PumpAsync(netz, stream, null);

                await stream.OpenAsync(cts.Token);

                if (!await stream.WaitUntilOpenAsync(VerificationTimeout, cts.Token))
                    return false;

                return await stream.RequestVerificationAsync(
                           targetDomain:       _localServer.Domain,
                           streamId:           streamId,
                           key:                key,
                           Timeout:            VerificationTimeout,
                           cancellationToken:  cts.Token);

            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                try { client?.Dispose(); }
                catch { /* egal */ }
            }

        }

        private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan HandshakeTimeout    = TimeSpan.FromSeconds(10);

        #endregion

        #region (private) AcceptLoopAsync()

        private async Task AcceptLoopAsync()
        {

            while (!_cts.IsCancellationRequested)
            {

                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception)
                {
                    // Listener beendet - Schluss.
                    return;
                }

                Interlocked.Increment(ref _inboundCounter);

                _ = HandleInboundAsync(client);

            }

        }

        #endregion

        #region (private) HandleInboundAsync(client)

        private async Task HandleInboundAsync(TcpClient client)
        {

            Stream?             netz             = null;
            X509Certificate?    peerCertificate  = null;

            try
            {

                netz = client.GetStream();

                if (Mode == TcpTlsMode.Direct)
                {

                    var tls = new SslStream(netz, leaveInnerStreamOpen: false);

                    await tls.AuthenticateAsServerAsync(
                              ServerOptions(),
                              _cts.Token);

                    netz = tls;
                    peerCertificate = tls.RemoteCertificate;

                }

                else if (Mode == TcpTlsMode.StartTls)
                {

                    // Mit Zeitlimit: eine Gegenstelle, die den Handshake
                    // anfängt und dann schweigt, hielte diese Verbindung sonst
                    // für immer - und beim Herunterfahren den ganzen Server.
                    using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    handshakeCts.CancelAfter(HandshakeTimeout);

                    var tls = await StartTlsAsServerAsync(netz, handshakeCts.Token);

                    // Ohne TLS gibt es keinen Stream. Der Aufrufer erfährt es
                    // daran, dass die Verbindung endet.
                    if (tls is null)
                        return;

                    netz             = tls;
                    peerCertificate  = tls.RemoteCertificate;

                }

                var stream = S2SStream.Accept(
                                 _localServer.Domain,
                                 (frame, ct) => SendAsync(netz, frame, ct),
                                 (peerDomain, stanza) => _localServer.AcceptFromRemoteAsync(peerDomain, stanza),
                                 secret:            DialbackSecret,
                                 verifyKey:         VerifyDialbackKeyAsync,
                                 framing:           TcpStreamFraming.Instance,
                                 externalIdentity:  IdentityCheckFor(peerCertificate));

                await PumpAsync(netz, stream, null);

            }
            catch (Exception)
            {
                // Verbindung abgerissen - im Betrieb der Normalfall.
            }
            finally
            {
                try { netz?.Dispose(); }  catch { /* egal */ }
                try { client.Dispose(); } catch { /* egal */ }
            }

        }

        #endregion

        #region (private) GetOrCreateOutboundAsync / ConnectOutboundAsync

        private Task<S2SStream?> GetOrCreateOutboundAsync(String             remoteDomain,
                                                          CancellationToken  cancellationToken)
        {

            lock (_lock)
            {

                if (_outbound.TryGetValue(remoteDomain, out var vorhanden))
                    return vorhanden.Connecting!;

                // Kein Eintrag von Hand und kein Resolver - dann gibt es die
                // Domain für diesen Server nicht.
                if (!_peers.TryGetValue(remoteDomain, out var peer) &&
                    AddressResolver is null)
                {
                    return Task.FromResult<S2SStream?>(null);
                }

                var slot = new OutboundSlot();
                _outbound[remoteDomain] = slot;

                slot.Connecting = peer is not null
                                      ? ConnectOutboundAsync(remoteDomain, peer, slot, cancellationToken)
                                      : ResolveAndConnectAsync(remoteDomain, slot, cancellationToken);

                return slot.Connecting;

            }

        }

        /// <summary>
        /// Sucht die Adressen einer Domain und versucht sie der Reihe nach.
        /// </summary>
        /// <remarks>
        /// Der Reihe nach und nicht nur das erste Ziel: SRV-Einträge nennen
        /// Ausweichrechner, und sie aufzulisten ohne sie zu benutzen wäre eine
        /// halbe Umsetzung. Die Reihenfolge stammt aus
        /// <see cref="SrvSelection"/> und wird hier nicht mehr angetastet.
        ///
        /// Die Betriebsart und die Zertifikatsprüfung kommen von den
        /// Vorgabewerten dieses Servers - insbesondere wird gegen die
        /// <b>gesuchte Domain</b> geprüft und nicht gegen den Rechnernamen aus
        /// dem SRV-Eintrag. Andersherum genügte ein gefälschter Eintrag, um
        /// die Prüfung zu bestehen.
        /// </remarks>
        private async Task<S2SStream?> ResolveAndConnectAsync(String             remoteDomain,
                                                              OutboundSlot       slot,
                                                              CancellationToken  cancellationToken)
        {

            IReadOnlyList<SrvTarget> ziele;

            try
            {
                ziele = await AddressResolver!.ResolveAsync(remoteDomain, cancellationToken);
            }
            catch (Exception)
            {
                ziele = [];
            }

            foreach (var ziel in ziele)
            {

                var peer = new PeerConfig(ziel.Host,
                                          ziel.Port,
                                          Mode,
                                          DefaultPeerValidator);

                var stream = await ConnectOutboundAsync(remoteDomain, peer, slot, cancellationToken);

                if (stream is not null)
                {

                    // Der Platz im Cache wurde von ConnectOutboundAsync bei
                    // jedem Fehlversuch geräumt - für den Erfolg muss er
                    // wieder stehen, sonst baut die nächste Zustellung erneut
                    // auf.
                    lock (_lock)
                        _outbound[remoteDomain] = slot;

                    return stream;

                }

            }

            DropOutbound(remoteDomain, slot);

            return null;

        }

        /// <summary>
        /// Die Zertifikatsprüfung für aufgelöste Gegenstellen.
        /// </summary>
        /// <remarks>
        /// Null überlässt sie dem Betriebssystem - für den Betrieb die
        /// richtige Vorgabe, weil ein fremder Server ein Zertifikat einer
        /// bekannten CA vorlegen soll. Im Testaufbau wird sie gesetzt, weil
        /// selbst signierte Zertifikate sonst nirgends durchkämen.
        /// </remarks>
        public RemoteCertificateValidationCallback? DefaultPeerValidator { get; init; }

        private async Task<S2SStream?> ConnectOutboundAsync(String             remoteDomain,
                                                            PeerConfig         peer,
                                                            OutboundSlot       slot,
                                                            CancellationToken  cancellationToken)
        {

            try
            {

                var client = new TcpClient();
                await client.ConnectAsync(peer.Host, peer.Port, cancellationToken);

                using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                handshakeCts.CancelAfter(HandshakeTimeout);

                var netz = await WrapAsync(client, peer, remoteDomain, handshakeCts.Token);

                if (netz is null)
                {
                    DropOutbound(remoteDomain, slot);
                    return null;
                }

                var stream = S2SStream.Initiate(
                                 _localServer.Domain,
                                 remoteDomain,
                                 (frame, ct) => SendAsync(netz, frame, ct),
                                 secret:            DialbackSecret,
                                 framing:           TcpStreamFraming.Instance,
                                 canOfferExternal:  UseSaslExternal && Certificate is not null);

                stream.OnClosed += _ => DropOutbound(remoteDomain, slot);

                _ = PumpAsync(netz, stream, () =>
                    {
                        DropOutbound(remoteDomain, slot);
                        try { client.Dispose(); } catch { /* egal */ }
                    });

                await stream.OpenAsync(cancellationToken);

                if (!await stream.WaitUntilReadyAsync(HandshakeTimeout, cancellationToken))
                {
                    stream.Abort("Aufbau nicht abgeschlossen");
                    DropOutbound(remoteDomain, slot);
                    return null;
                }

                return stream;

            }
            catch (Exception)
            {
                DropOutbound(remoteDomain, slot);
                return null;
            }

        }

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

        #region (private class) FrameReader

        /// <summary>
        /// Liest einzelne Rahmen aus einem Strom - für die Aushandlung, bevor
        /// ein <see cref="S2SStream"/> übernimmt.
        /// </summary>
        private sealed class FrameReader
        {

            private readonly Stream             _stream;
            private readonly XmlStreamSplitter  _splitter  = new();
            private readonly Queue<String>      _pending   = new();
            private readonly Byte[]             _buffer    = new Byte[8192];

            public FrameReader(Stream stream)
            {
                _stream = stream;
            }

            /// <summary>
            /// Steht noch etwas im Puffer, das die Gegenstelle vorausgeschickt
            /// hat?
            /// </summary>
            public Boolean HasPending => _pending.Count > 0;

            /// <summary>Der nächste Rahmen, oder null wenn der Strom endet.</summary>
            public async Task<String?> NextAsync(CancellationToken cancellationToken)
            {

                while (_pending.Count == 0)
                {

                    var gelesen = await _stream.ReadAsync(_buffer, cancellationToken);

                    if (gelesen <= 0)
                        return null;

                    foreach (var rahmen in _splitter.Push(Encoding.UTF8.GetString(_buffer, 0, gelesen)))
                        _pending.Enqueue(rahmen);

                }

                return _pending.Dequeue();

            }

        }

        #endregion

        #region (private) STARTTLS (RFC 6120, Abschnitt 5.4)

        /// <summary>Der Namensraum der TLS-Aushandlung.</summary>
        private const String TlsNamespace = "urn:ietf:params:xml:ns:xmpp-tls";

        /// <summary>
        /// STARTTLS auf der annehmenden Seite.
        /// </summary>
        /// <returns>Der verschlüsselte Strom, oder null wenn nichts daraus wurde.</returns>
        private async Task<SslStream?> StartTlsAsServerAsync(Stream             netz,
                                                             CancellationToken  cancellationToken)
        {

            var leser = new FrameReader(netz);

            var kopf = await leser.NextAsync(cancellationToken);

            if (kopf is null || !TcpStreamFraming.Instance.IsStreamOpen(kopf))
                return null;

            await SendAsync(netz,
                            TcpStreamFraming.Instance.StreamOpen(_localServer.Domain,
                                                                 S2SStream.Attr(kopf, "from"),
                                                                 Guid.NewGuid().ToString("N")),
                            cancellationToken);

            // <required/>, weil RFC 6120, Abschnitt 13.7 für S2S
            // Verschlüsselung verlangt. Wer sie ausschlägt, bekommt keinen
            // Stream - nicht einen unverschlüsselten.
            await SendAsync(netz,
                            $"<stream:features xmlns:stream='{S2SStream.StreamNamespace}'>" +
                            $"<starttls xmlns='{TlsNamespace}'><required/></starttls>" +
                            "</stream:features>",
                            cancellationToken);

            var anfrage = await leser.NextAsync(cancellationToken);

            if (anfrage is null ||
                !anfrage.StartsWith("<starttls", StringComparison.Ordinal) ||
                !anfrage.Contains(TlsNamespace, StringComparison.Ordinal))
            {

                await SendAsync(netz, $"<failure xmlns='{TlsNamespace}'/>", cancellationToken);

                return null;

            }

            // RFC 6120, Abschnitt 5.4.3.3: nach dem <starttls/> darf im
            // Klartext nichts mehr folgen. Steht doch etwas im Puffer, hat die
            // Gegenstelle vorausgeschickt - entweder ist sie kaputt, oder
            // jemand versucht, Klartext in den gleich verschlüsselten Stream
            // zu schmuggeln. Beides ist ein Grund aufzuhören und keiner
            // weiterzumachen.
            if (leser.HasPending)
                return null;

            await SendAsync(netz, $"<proceed xmlns='{TlsNamespace}'/>", cancellationToken);

            var tls = new SslStream(netz, leaveInnerStreamOpen: false);

            await tls.AuthenticateAsServerAsync(ServerOptions(), cancellationToken);

            return tls;

        }

        /// <summary>
        /// STARTTLS auf der aufbauenden Seite.
        /// </summary>
        /// <remarks>
        /// Der hier geführte Stream ist ein Wegwerfstream: nach der
        /// Verschlüsselung fängt alles von vorn an, mit neuem Stream-Kopf und
        /// neuer Stream-ID (RFC 6120, Abschnitt 5.4.3.3). Deshalb steht das
        /// hier im Transport und nicht in <see cref="S2SStream"/> - jene
        /// Schicht bekommt den Strom erst, wenn er verschlüsselt ist, und
        /// muss von der Aushandlung nichts wissen.
        /// </remarks>
        private async Task<SslStream?> StartTlsAsClientAsync(Stream             netz,
                                                             PeerConfig         peer,
                                                             String             remoteDomain,
                                                             CancellationToken  cancellationToken)
        {

            var leser = new FrameReader(netz);

            await SendAsync(netz,
                            TcpStreamFraming.Instance.StreamOpen(_localServer.Domain, remoteDomain, null),
                            cancellationToken);

            var bietetTls = false;

            while (await leser.NextAsync(cancellationToken) is { } rahmen)
            {

                if (TcpStreamFraming.Instance.IsStreamOpen(rahmen))
                    continue;

                bietetTls = rahmen.Contains(TlsNamespace, StringComparison.Ordinal);
                break;

            }

            // Kein STARTTLS im Angebot - dann gibt es keine Verbindung. Im
            // Klartext weiterzumachen wäre genau der Rückfall, gegen den die
            // Aushandlung existiert.
            if (!bietetTls)
                return null;

            await SendAsync(netz, $"<starttls xmlns='{TlsNamespace}'/>", cancellationToken);

            var antwort = await leser.NextAsync(cancellationToken);

            if (antwort is null || !antwort.StartsWith("<proceed", StringComparison.Ordinal))
                return null;

            if (leser.HasPending)
                return null;

            var tls = new SslStream(netz,
                                    leaveInnerStreamOpen: false,
                                    userCertificateValidationCallback: peer.Validator);

            await tls.AuthenticateAsClientAsync(ClientOptions(remoteDomain), cancellationToken);

            return tls;

        }

        #endregion

        #region (private) SASL-EXTERNAL

        /// <summary>
        /// Die TLS-Einstellungen des annehmenden Servers.
        /// </summary>
        /// <remarks>
        /// Fuer SASL-EXTERNAL muss das Klientzertifikat <b>angefordert</b>
        /// werden - ohne diese Zeile gibt es keines, und die Pruefung haette
        /// nichts zu lesen. Angefordert heisst nicht verlangt: bleibt es aus,
        /// kommt die Verbindung trotzdem zustande und die Gegenstelle weist
        /// sich per Dialback aus.
        /// </remarks>
        private SslServerAuthenticationOptions ServerOptions()

            => new () {
                   ServerCertificate                   = Certificate,
                   ClientCertificateRequired           = UseSaslExternal,
                   RemoteCertificateValidationCallback = UseSaslExternal
                                                             ? (_, _, _, _) => true
                                                             : null
               };

        /// <summary>
        /// Die TLS-Einstellungen des aufbauenden Servers.
        /// </summary>
        /// <remarks>
        /// Das eigene Zertifikat geht nur mit, wenn SASL-EXTERNAL vorgesehen
        /// ist. Die Gegenstelle prueft es; ob es <i>ihr</i> genuegt, entscheidet
        /// sie.
        /// </remarks>
        private SslClientAuthenticationOptions ClientOptions(String remoteDomain)

            => new () {
                   TargetHost              = remoteDomain,
                   ClientCertificates      = UseSaslExternal && Certificate is not null
                                                 ? [Certificate]
                                                 : null
               };

        /// <summary>
        /// Macht aus dem vorgelegten Zertifikat die Pruefung, die
        /// <see cref="S2SStream"/> braucht - oder null, wenn es keines gibt.
        /// </summary>
        /// <remarks>
        /// Null ist hier die richtige Antwort und keine Notloesung: ohne
        /// Zertifikat darf SASL-EXTERNAL gar nicht erst angeboten werden.
        ///
        /// <b>Was diese Pruefung nicht leistet:</b> sie sagt, fuer welche
        /// Domains das Zertifikat ausgestellt ist - nicht, ob ihm zu trauen
        /// ist. Die Kette gegen eine bekannte CA zu pruefen ist Sache des
        /// TLS-Handshakes und damit der hinterlegten Pruefung; im Testaufbau
        /// ist das ein angehefteter Fingerabdruck. Wer hier eine Pruefung
        /// einsetzt, die alles durchlaesst, hat SASL-EXTERNAL auf eine
        /// Selbstauskunft reduziert.
        /// </remarks>
        private Func<String, Boolean>? IdentityCheckFor(X509Certificate? peerCertificate)
        {

            if (!UseSaslExternal || peerCertificate is null)
                return null;

            var zertifikat = peerCertificate as X509Certificate2
                                 ?? X509CertificateLoader.LoadCertificate(peerCertificate.GetRawCertData());

            return domain => CertificateIdentity.Authorises(zertifikat, domain);

        }

        #endregion

        #region (private) WrapAsync / SendAsync / PumpAsync

        /// <summary>
        /// Bringt die Verbindung in den Zustand, in dem die Protokollschicht
        /// sie übernehmen darf - je nach Modus im Klartext, sofort
        /// verschlüsselt oder nach STARTTLS.
        /// </summary>
        /// <returns>null, wenn TLS vorgesehen war und nicht zustande kam.</returns>
        private async Task<Stream?> WrapAsync(TcpClient          client,
                                              PeerConfig         peer,
                                              String             remoteDomain,
                                              CancellationToken  cancellationToken)
        {

            var netz = (Stream) client.GetStream();

            if (peer.Mode == TcpTlsMode.None)
                return netz;

            if (peer.Mode == TcpTlsMode.StartTls)
                return await StartTlsAsClientAsync(netz, peer, remoteDomain, cancellationToken);

            var tls = new SslStream(
                          netz,
                          leaveInnerStreamOpen: false,
                          userCertificateValidationCallback: peer.Validator);

            await tls.AuthenticateAsClientAsync(ClientOptions(remoteDomain), cancellationToken);

            return tls;

        }

        private static async Task SendAsync(Stream stream, String frame, CancellationToken cancellationToken)
        {

            var bytes = Encoding.UTF8.GetBytes(frame);

            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);

        }

        /// <summary>
        /// Liest den Strom und reicht jeden vollständigen Rahmen an die
        /// Protokollschicht.
        /// </summary>
        /// <remarks>
        /// Hier steckt der ganze Unterschied zu WebSocket: was gelesen wird,
        /// hat mit Elementgrenzen nichts zu tun.
        /// <see cref="XmlStreamSplitter"/> macht daraus Rahmen.
        /// </remarks>
        private static async Task PumpAsync(Stream stream, S2SStream s2s, Action? beimEnde)
        {

            var puffer    = new Byte[8192];
            var zerleger  = new XmlStreamSplitter();

            // Nach einem SASL-Neustart beginnt der Strom als neues Dokument.
            s2s.OnRestart += zerleger.Reset;

            try
            {

                while (!s2s.IsClosed)
                {

                    var gelesen = await stream.ReadAsync(puffer);

                    if (gelesen <= 0)
                        break;

                    foreach (var rahmen in zerleger.Push(Encoding.UTF8.GetString(puffer, 0, gelesen)))
                    {

                        await s2s.ProcessFrameAsync(rahmen);

                        if (s2s.IsClosed)
                            break;

                    }

                }

            }
            catch (Exception)
            {
                // Verbindung weg.
            }

            s2s.Abort("TCP-Verbindung beendet");

            beimEnde?.Invoke();

        }

        #endregion


        #region DisposeAsync()

        public async ValueTask DisposeAsync()
        {

            await _cts.CancelAsync();

            try { _listener.Stop(); }
            catch { /* egal */ }

            List<Task<S2SStream?>> ausgehend;

            lock (_lock)
                ausgehend = [.. _outbound.Values
                                         .Select(slot => slot.Connecting)
                                         .Where(task => task is not null)
                                         .Cast<Task<S2SStream?>>()];

            foreach (var task in ausgehend)
            {
                // Mit Zeitlimit: ein hängender Verbindungsaufbau darf das
                // Herunterfahren nicht blockieren. Ohne das wurde aus einem
                // fehlgeschlagenen Test ein stehender Testlauf.
                try { (await task.WaitAsync(HandshakeTimeout))?.Abort("Server wird beendet"); }
                catch { /* Aufbau gescheitert oder zu langsam */ }
            }

            _cts.Dispose();

        }

        #endregion

    }

}
