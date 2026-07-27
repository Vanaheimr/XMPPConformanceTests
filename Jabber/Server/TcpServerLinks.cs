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
    /// <b>Was hier bewusst anders ist als in RFC 6120, Abschnitt 5.4:</b> es
    /// gibt kein STARTTLS. TLS wird entweder von Anfang an gesprochen oder gar
    /// nicht, je nachdem was für die Gegenstelle hinterlegt ist. STARTTLS
    /// verhandelt Verschlüsselung <i>innerhalb</i> des Streams: Klartext-Stream
    /// öffnen, <c>&lt;starttls/&gt;</c> anbieten, <c>&lt;proceed/&gt;</c>
    /// abwarten, TLS aufsetzen, Stream neu öffnen. Das ist machbar, aber es ist
    /// ein eigener Zustandsautomat über der Rahmung, und ohne ihn ist die
    /// Strecke nicht weniger sicher - nur weniger kompatibel. Für die
    /// Föderation mit einem Server, der Klartext nicht akzeptiert, fehlt es
    /// trotzdem; das steht im Arbeitsplan.
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

        private sealed record PeerConfig(String                                Host,
                                         Int32                                 Port,
                                         Boolean                               UseTLS,
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

        /// <summary>Das Dialback-Geheimnis dieses Servers (XEP-0220).</summary>
        public String DialbackSecret { get; } = DialbackKey.NewSecret();

        /// <summary>Anzahl der jemals angenommenen eingehenden Verbindungen.</summary>
        public Int32 InboundConnectionCount => Volatile.Read(ref _inboundCounter);

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Legt den eingehenden Zweig an und nimmt sofort Verbindungen an.
        /// </summary>
        /// <param name="localServer">Der Server, dessen S2S-Gegenstelle dies ist.</param>
        /// <param name="port">Fester Port, oder 0 für einen freien. Vorgesehen ist 5269.</param>
        /// <param name="useTLS">
        /// TLS von der ersten Sekunde an, mit dem Zertifikat des Servers. Ohne
        /// STARTTLS ist das die einzige Art, die Strecke zu verschlüsseln.
        /// </param>
        public TcpServerLinks(XMPPServer  localServer,
                              Int32       port     = 0,
                              Boolean     useTLS   = true)
        {

            _localServer  = localServer;
            Certificate   = useTLS ? localServer.Certificate : null;

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
                            Boolean                               useTLS      = true,
                            RemoteCertificateValidationCallback?  validator   = null)
        {
            lock (_lock)
                _peers[domain] = new PeerConfig(host, port, useTLS, validator);
        }

        #endregion

        #region (static) Connect(a, b)

        /// <summary>
        /// Verbindet zwei Server über TCP in beide Richtungen.
        /// </summary>
        public static void Connect(XMPPServer a, XMPPServer b)
        {

            if (String.Equals(a.Domain, b.Domain, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                          $"Beide Server bedienen '{a.Domain}' - eine Föderation mit sich selbst ergibt nichts.",
                          nameof(b));

            var linksA = LinksOf(a);
            var linksB = LinksOf(b);

            // Ausdrücklich die Adresse und nicht "localhost": der Listener
            // bindet IPv4-Loopback, und ein Name, der zuerst nach IPv6
            // auflöst, kostet je Verbindung den Fallback ab.
            var loopback = IPAddress.Loopback.ToString();

            linksA.AddPeer(b.Domain, loopback, linksB.Port, linksB.Certificate is not null, b.IsOwnCertificate);
            linksB.AddPeer(a.Domain, loopback, linksA.Port, linksA.Certificate is not null, a.IsOwnCertificate);

        }

        private static TcpServerLinks LinksOf(XMPPServer server)

            => server.ServerLinks as TcpServerLinks
               ?? new TcpServerLinks(server);

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

            PeerConfig? peer;

            lock (_lock)
                _peers.TryGetValue(senderDomain, out peer);

            if (peer is null)
                return false;

            TcpClient? client = null;

            try
            {

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                cts.CancelAfter(VerificationTimeout);

                client = new TcpClient();
                await client.ConnectAsync(peer.Host, peer.Port, cts.Token);

                var netz = await WrapAsync(client, peer, senderDomain, cts.Token);

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

            Stream? netz = null;

            try
            {

                netz = client.GetStream();

                if (Certificate is not null)
                {

                    var tls = new SslStream(netz, leaveInnerStreamOpen: false);

                    await tls.AuthenticateAsServerAsync(
                              new SslServerAuthenticationOptions {
                                  ServerCertificate         = Certificate,
                                  ClientCertificateRequired = false
                              },
                              _cts.Token);

                    netz = tls;

                }

                var stream = S2SStream.Accept(
                                 _localServer.Domain,
                                 (frame, ct) => SendAsync(netz, frame, ct),
                                 (peerDomain, stanza) => _localServer.AcceptFromRemoteAsync(peerDomain, stanza),
                                 secret:     DialbackSecret,
                                 verifyKey:  VerifyDialbackKeyAsync,
                                 framing:    TcpStreamFraming.Instance);

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

                if (!_peers.TryGetValue(remoteDomain, out var peer))
                    return Task.FromResult<S2SStream?>(null);

                var slot = new OutboundSlot();
                _outbound[remoteDomain] = slot;

                slot.Connecting = ConnectOutboundAsync(remoteDomain, peer, slot, cancellationToken);

                return slot.Connecting;

            }

        }

        private async Task<S2SStream?> ConnectOutboundAsync(String             remoteDomain,
                                                            PeerConfig         peer,
                                                            OutboundSlot       slot,
                                                            CancellationToken  cancellationToken)
        {

            try
            {

                var client = new TcpClient();
                await client.ConnectAsync(peer.Host, peer.Port, cancellationToken);

                var netz = await WrapAsync(client, peer, remoteDomain, cancellationToken);

                var stream = S2SStream.Initiate(
                                 _localServer.Domain,
                                 remoteDomain,
                                 (frame, ct) => SendAsync(netz, frame, ct),
                                 secret:   DialbackSecret,
                                 framing:  TcpStreamFraming.Instance);

                stream.OnClosed += _ => DropOutbound(remoteDomain, slot);

                _ = PumpAsync(netz, stream, () =>
                    {
                        DropOutbound(remoteDomain, slot);
                        try { client.Dispose(); } catch { /* egal */ }
                    });

                await stream.OpenAsync(cancellationToken);

                if (!await stream.WaitUntilOpenAsync(HandshakeTimeout, cancellationToken) ||
                    !await stream.WaitUntilAuthenticatedAsync(HandshakeTimeout, cancellationToken))
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

        #region (private) WrapAsync / SendAsync / PumpAsync

        /// <summary>
        /// Legt TLS über die Verbindung, falls für diese Gegenstelle
        /// vorgesehen.
        /// </summary>
        private static async Task<Stream> WrapAsync(TcpClient          client,
                                                    PeerConfig         peer,
                                                    String             remoteDomain,
                                                    CancellationToken  cancellationToken)
        {

            var netz = (Stream) client.GetStream();

            if (!peer.UseTLS)
                return netz;

            var tls = new SslStream(
                          netz,
                          leaveInnerStreamOpen: false,
                          userCertificateValidationCallback: peer.Validator);

            await tls.AuthenticateAsClientAsync(
                      new SslClientAuthenticationOptions {
                          TargetHost = remoteDomain
                      },
                      cancellationToken);

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
                try { (await task)?.Abort("Server wird beendet"); }
                catch { /* Aufbau war ohnehin gescheitert */ }
            }

            _cts.Dispose();

        }

        #endregion

    }

}
