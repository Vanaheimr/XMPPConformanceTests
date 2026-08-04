/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Ratatoskr <https://www.github.com/Vanaheimr/Ratatoskr>
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
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Ratatoskr;
using org.GraphDefined.Vanaheimr.Ratatoskr.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// Base for federation runs against a foreign, fully grown far end.
    /// </summary>
    /// <remarks>
    /// Everything else in this test collection checks our server against our
    /// server. That establishes that both sides have the same understanding of
    /// the protocol - not that this understanding is right. Where our two
    /// sides make the same error, it does not stand out.
    ///
    /// The set-up is the same for every far end: it is unpacked in WSL without
    /// root, gets a certificate signed by a test CA and listens on the
    /// loopback. The same CA signs our certificate - with that SASL-EXTERNAL
    /// carries (XEP-0178). What differs are the domain, the ports and the
    /// environment variable over which the test collection finds the CA;
    /// precisely that is what the derivations lay down.
    /// </remarks>
    public abstract class AForeignPeerFederationTests
    {

        #region What makes up the far end

        /// <summary>Name of the far end - only for error messages.</summary>
        protected abstract String  PeerName      { get; }

        /// <summary>The domain the far end serves.</summary>
        protected abstract String  PeerDomain    { get; }

        /// <summary>The port on which it accepts S2S connections.</summary>
        protected abstract Int32   PeerPort      { get; }

        /// <summary>
        /// The port on which <b>we</b> listen in the inbound run - that is, the
        /// one the far end falls back on without an SRV record.
        /// </summary>
        protected abstract Int32   InboundPort   { get; }

        /// <summary>
        /// The environment variable pointing at the certificate directory of
        /// the set-up.
        /// </summary>
        protected abstract String  CertVariable  { get; }

        #endregion

        #region Data

        /// <summary>Our domain in the outbound run.</summary>
        protected const String LocalDomain    = "jabber.test";

        /// <summary>
        /// Our domain in the inbound run - and the reason why there are two.
        /// </summary>
        /// <remarks>
        /// So that the <b>far end</b> can dial us, it has to be able to resolve
        /// our domain. An entry in <c>/etc/hosts</c> would need root;
        /// <c>localhost</c> stands there anyway and points at the loopback.
        /// That is why the test server serves this domain in this case.
        /// </remarks>
        protected const String InboundDomain  = "localhost";

        private   XMPPClient?       _client;
        private   X509Certificate2  _ca       = null!;
        private   X509Certificate2  _ourCert  = null!;

        /// <summary>Our server in the running test.</summary>
        protected XMPPServer?      Server  { get; private set; }

        /// <summary>Our S2S branch in the running test.</summary>
        protected TcpServerLinks?  Links   { get; private set; }

        #endregion

        #region Set-up / tear-down

        private readonly InternalErrorGuard _guard = new();

        /// <summary>Arm the guard before every test.</summary>
        [SetUp]
        public void ArmTheGuard()
            => _guard.Reset();

        /// <summary>
        /// Where the test CA and our certificate lie. Without them or without a
        /// running far end the test has nothing to check.
        /// </summary>
        private String CertDirectory
            => Environment.GetEnvironmentVariable(CertVariable) ?? "";

        /// <summary>
        /// Builds the server and the S2S branch up, or skips the test when the
        /// far end is not standing by.
        /// </summary>
        /// <param name="bidi">
        /// XEP-0288 in both directions: offering and requesting.
        /// </param>
        /// <param name="offerOnly">
        /// Offer XEP-0288 on inbound connections only, do not request it on
        /// outbound ones.
        /// </param>
        /// <param name="reachable">
        /// Build up under a domain and a port under which the far end can dial
        /// us of its own accord.
        /// </param>
        /// <param name="dialback">
        /// Build up without SASL-EXTERNAL, so that both sides fall back on the
        /// dialback query.
        /// </param>
        protected void BuildUp(Boolean bidi       = false,
                               Boolean offerOnly  = false,
                               Boolean reachable  = false,
                               Boolean dialback   = false)
        {

            var directory = CertDirectory;

            if (directory.Length == 0 || !File.Exists(Path.Combine(directory, "ca.crt")))
                Assert.Ignore($"No {PeerName} set-up: {CertVariable} points at no test CA.");

            if (!PortAnswers())
                Assert.Ignore($"On 127.0.0.1:{PeerPort} no {PeerName} answers.");

            var domain = reachable ? InboundDomain : LocalDomain;

            _ca       = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(directory, "ca.crt"));
            _ourCert  = X509CertificateLoader.LoadPkcs12FromFile(
                            Path.Combine(directory, $"{domain}.pfx"), null);

            Server    = _guard.Watched(new XMPPServer(domain, certificate: _ourCert));
            Server.Start();
            Server.AddAccount("alice");

            Links     = new TcpServerLinks(Server,
                                           port: reachable ? InboundPort : 0,
                                           mode: TcpTlsMode.StartTls) {

                            // Without SASL-EXTERNAL we present no client
                            // certificate. The far end then has nothing to
                            // check and does not even offer EXTERNAL - the path
                            // thereby falls back on dialback of its own accord,
                            // without either side having to force it.
                            UseSaslExternal              = !dialback,
                            OfferBidirectionalStreams    = bidi || offerOnly,
                            RequestBidirectionalStreams  = bidi
                        };

            Links.AddPeer(PeerDomain, "127.0.0.1", PeerPort, TcpTlsMode.StartTls, TrustsTheTestCA);

        }

        [TearDown]
        public async Task CleanUp()
        {

            if (_client is not null)
            {
                try { await _client.DisposeAsync(); } catch { /* does not matter in the teardown */ }
                _client = null;
            }

            // Expressly before the server: the S2S branch holds a fixed port in
            // the reachable set-up, and otherwise the next test does not get it
            // any more. That cost two test runs with the Prosody set-up,
            // because a failed bind looks like a protocol error.
            if (Links is not null)
            {
                try { await Links.DisposeAsync(); } catch { /* does not matter in the teardown */ }
                Links = null;
            }

            if (Server is not null)
            {
                await Server.DisposeAsync();
                Server = null;
            }

            _guard.AssertClean();

        }

        #endregion

        #region Helper functions

        private Boolean PortAnswers()
        {
            try
            {
                using var s = new TcpClient();
                return s.ConnectAsync("127.0.0.1", PeerPort).Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Accepts exactly those certificates that are signed by the test CA.
        /// </summary>
        /// <remarks>
        /// Not "accept everything": a check letting every certificate through
        /// would pass against an arbitrary foreign far end as well and would
        /// say nothing about the handshake. The operating system store does not
        /// help here - the test CA does not stand there and is not supposed to.
        /// </remarks>
        private Boolean TrustsTheTestCA(Object            sender,
                                        X509Certificate?  certificate,
                                        X509Chain?        chain,
                                        SslPolicyErrors   errors)
        {

            if (certificate is null)
                return false;

            var certificate2 = certificate as X509Certificate2
                                 ?? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());

            using var check = new X509Chain();

            check.ChainPolicy.TrustMode       = X509ChainTrustMode.CustomRootTrust;
            check.ChainPolicy.RevocationMode  = X509RevocationMode.NoCheck;
            check.ChainPolicy.CustomTrustStore.Add(_ca);

            return check.Build(certificate2);

        }

        /// <summary>
        /// Connects a real client to our test server.
        /// </summary>
        protected async Task<XMPPClient> AliceAsync()
        {

            var connection                                   = new XMPPConnection($"alice@{Server!.Domain}", "pw", Server.Uri) {
                                 KeepaliveEnabled            = false,
                                 MaxReconnectAttempts        = 0,
                                 ServerCertificateValidator  = (_, c, _, _) =>
                                     c is not null &&
                                     c.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256)
                                      .Equals(_ourCert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256),
                                              StringComparison.OrdinalIgnoreCase)
                             };

            _client = new XMPPClient(connection);
            await _client.ConnectAsync();

            return _client;

        }

        #endregion


        #region ThePeerTakesTheReturnPathWeOffered()

        /// <summary>
        /// The far end takes our XEP-0288 announcement when <b>it</b> dials us.
        /// </summary>
        /// <remarks>
        /// The direction that until recently was only inferred from foreign
        /// source and never observed - and it was so for a home-made reason: a
        /// single switch steered offering and requesting at once. As long as
        /// our outbound connection uses the return direction, the far end
        /// answers over it and does not dial us at all. So there was no state
        /// in which our announcement could show itself.
        ///
        /// With separate switches there is one: we offer but do not request.
        /// The sequence is then
        ///
        /// <list type="number">
        ///   <item>
        ///     Alice pings. We dial - without <c>&lt;bidi/&gt;</c> -, the far
        ///     end answers the ping over a connection of <b>its own</b> to us
        ///     (RFC 6120, section 4.1).
        ///   </item>
        ///   <item>
        ///     On this inbound connection we announce the return direction. If
        ///     the far end takes it, the stream is cleared at our end.
        ///   </item>
        ///   <item>
        ///     The second ping then goes out over precisely this stream instead
        ///     of over a new connection - and that is what
        ///     <c>BidirectionalDeliveryCount</c> counts.
        ///   </item>
        /// </list>
        ///
        /// Two pings, not one: with the first one the inbound connection does
        /// not exist yet.
        ///
        /// <b>Only inside WSL.</b> From Windows the far end does not reach us.
        /// </remarks>
        [Test]
        public async Task ThePeerTakesTheReturnPathWeOffered()
        {

            if (!OperatingSystem.IsLinux())
                Assert.Ignore($"Only inside WSL: from Windows {PeerName} does not reach this server.");

            BuildUp(offerOnly: true, reachable: true);

            var alice = await AliceAsync();

            Assert.That(await alice.PingAsync(PeerDomain), Is.Not.Null,
                        "Even the first ping did not come back.");

            Assert.That(await XMPPServer.WaitUntilAsync(() => Links!.InboundConnectionCount > 0,
                                                       TimeSpan.FromSeconds(10)),
                        Is.True,
                        $"No inbound connection from {PeerName}.");

            var duration = await alice.PingAsync(PeerDomain);

            Assert.Multiple(() =>
            {

                Assert.That(duration, Is.Not.Null,
                            "The second ping did not come back.");

                Assert.That(Links!.BidirectionalDeliveryCount, Is.GreaterThan(0),
                            $"{PeerName} did not take our announcement - " +
                            "the second stanza went out over a connection of its own.");

            });

        }

        #endregion

    }

}
