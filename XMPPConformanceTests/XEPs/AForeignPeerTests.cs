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

using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Ratatoskr;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// Everything a test against a foreign server needs before it can ask
    /// anything: the certificate, the port, the accounts, the log-in.
    /// </summary>
    /// <remarks>
    /// Split off from the room tests when the second subject arrived. The rooms
    /// and the file uploads have nothing to do with each other and share every
    /// line of this - and a base class called "room" that a test about uploads
    /// inherits from is a name that has stopped being true.
    ///
    /// What is peculiar to a counterpart - its domain, its endpoint - the
    /// derived classes lay down. A second service costs twenty lines.
    /// </remarks>
    public abstract class AForeignPeerTests
    {

        #region What makes up the counterpart

        /// <summary>Name of the counterpart - for messages only.</summary>
        protected abstract String  PeerName      { get; }

        /// <summary>The domain the counterpart serves.</summary>
        protected abstract String  PeerDomain    { get; }

        /// <summary>The WebSocket endpoint (RFC 7395).</summary>
        protected abstract URL     Endpoint      { get; }

        /// <summary>The port behind it - for the reachability check.</summary>
        protected abstract Int32   EndpointPort  { get; }

        /// <summary>
        /// The environment variable pointing at the certificate directory.
        /// </summary>
        protected abstract String  CertVariable  { get; }

        #endregion

        #region Data

        protected const String User      = "alice";
        protected const String User2     = "bob";

        /// <summary>
        /// The stranger, and the point is that she stays one.
        /// </summary>
        /// <remarks>
        /// <b>Nothing here may ever subscribe her to anybody.</b> The rosters of
        /// these accounts live on a real server and outlive the test, the suite
        /// and the machine - which is why the avatar lane makes its two
        /// contacts idempotently and why undoing that afterwards would be the
        /// kind of order-dependent cleanup D128 was spent removing.
        ///
        /// So every question about what somebody <i>not</i> on the roster may
        /// see needs a third account that nobody has ever asked for anything,
        /// and this is it. Added in D131, and the set-up scripts carry the same
        /// note beside the names.
        /// </remarks>
        protected const String User3     = "carol";

        // Stays German on purpose: the password of the real accounts that
        // tools/prosody/setup.sh and tools/ejabberd/setup.sh create.
        protected const String Password  = "geheim";

        private readonly List<XMPPClient>  _clients = [];
        private readonly List<HttpClient>  _https   = [];
        private X509Certificate2           _ca = null!;

        #endregion

        #region Setting up / tearing down

        private String CertDirectory
            => Environment.GetEnvironmentVariable(CertVariable) ?? "";

        private Boolean PortAnswers()
        {
            try
            {
                using var probe = new TcpClient();
                return probe.ConnectAsync("127.0.0.1", EndpointPort).Wait(TimeSpan.FromSeconds(2)) &&
                       probe.Connected;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Judges a certificate by the test CA and not by its name.
        /// </summary>
        /// <remarks>
        /// The name cannot be judged here: everything is dialled as 127.0.0.1,
        /// because these set-ups need no root and therefore write no
        /// <c>/etc/hosts</c>. What is checked is the one thing that can be - the
        /// chain up to the CA the set-up minted.
        /// </remarks>
        protected Boolean TrustsTheTestCA(Object?                                          sender,
                                          System.Security.Cryptography.X509Certificates.X509Certificate?  certificate,
                                          X509Chain?                                       chain,
                                          System.Net.Security.SslPolicyErrors              errors)
        {

            if (certificate is null)
                return false;

            var policy = new X509Chain();
            policy.ChainPolicy.TrustMode         = X509ChainTrustMode.CustomRootTrust;
            policy.ChainPolicy.RevocationMode    = X509RevocationMode.NoCheck;
            policy.ChainPolicy.CustomTrustStore.Add(_ca);

            return policy.Build(X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()));

        }

        /// <summary>
        /// Logs a client in and waits until the far side has taken notice of
        /// them, or skips the test.
        /// </summary>
        /// <remarks>
        /// <b>Being connected is not being visible</b>, and the difference cost
        /// six entries of "not from this change" (D128).
        /// <see cref="XMPPClient.ConnectAsync"/> returns once the initial
        /// presence has been <i>written</i>; whether the server has <i>handled</i>
        /// it is a later moment, and a stanza that arrives at this account in
        /// between is at the mercy of the service. Prosody keeps it and hands it
        /// over afterwards; ejabberd, configured here without
        /// <c>mod_offline</c>, bounces it and it is gone.
        ///
        /// Both are allowed, which is the point: a round that does not wait is
        /// not measuring the question it asks, it is measuring whose default
        /// happens to be forgiving.
        ///
        /// A round trip settles it. RFC 6120, section 10.1 has a server handle
        /// one stream’s stanzas in order, so an answer to something sent after
        /// the presence is proof that the presence is done with.
        /// </remarks>
        protected async Task<XMPPClient> ConnectAsync(String localPart = User)
        {

            var directory = CertDirectory;

            if (directory.Length == 0 || !File.Exists(Path.Combine(directory, "ca.crt")))
                Assert.Ignore($"No {PeerName} setup: {CertVariable} points at no test CA.");

            if (!PortAnswers())
                Assert.Ignore($"On 127.0.0.1:{EndpointPort} no {PeerName} WebSocket answers.");

            _ca = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(directory, "ca.crt"));

            var connection = new XMPPConnection(
                                 JID.Parse($"{localPart}@{PeerDomain}"),
                                 Password,
                                 Endpoint
                             ) {
                                 KeepaliveEnabled            = false,
                                 MaxReconnectAttempts        = 0,
                                 StreamManagementEnabled     = true,
                                 ServerCertificateValidator  = TrustsTheTestCA
                             };

            var client = new XMPPClient(connection);
            _clients.Add(client);

            await client.ConnectAsync();

            // The round trip of the remark above. Asserted rather than awaited
            // and forgotten: a service that does not answer leaves the race in
            // place, and a round standing in a race shall say so loudly instead
            // of failing somewhere else every third run.
            Assert.That(await client.PingAsync(), Is.Not.Null,
                        $"{PeerName} did not answer a ping, so there is no knowing whether it " +
                        $"has taken notice of {localPart} yet.");

            return client;

        }

        /// <summary>
        /// An HTTP client that judges the far side the way the stream does.
        /// </summary>
        /// <remarks>
        /// For the questions the library's own upload path cannot ask, because
        /// it would refuse to: a PUT to a slot nobody issued, a PUT of the wrong
        /// length. Those have to be sent by hand, and they still have to get
        /// past the same certificate.
        ///
        /// Redirects off, for the same reason the library has them off - a
        /// capability URL is not to be repeated at an address that did not issue
        /// it, and a test that quietly followed one would be measuring somewhere
        /// else.
        /// </remarks>
        protected HttpClient NewHttpClient()
        {

            var handler = new SocketsHttpHandler {
                              AllowAutoRedirect = false
                          };

            handler.SslOptions.RemoteCertificateValidationCallback =
                (sender, certificate, chain, errors) => TrustsTheTestCA(sender, certificate, chain, errors);

            var http = new HttpClient(handler) {
                           Timeout = TimeSpan.FromSeconds(30)
                       };

            _https.Add(http);

            return http;

        }

        [TearDown]
        public async Task CleanUp()
        {

            foreach (var client in _clients)
            {
                try { await client.DisposeAsync(); }
                catch { /* does not matter in the teardown */ }
            }

            _clients.Clear();

            foreach (var http in _https)
                http.Dispose();

            _https.Clear();

        }

        #endregion

        #region Helper functions

        protected static async Task WaitFor(Func<Boolean> condition, String what)
        {

            var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

            while (DateTime.UtcNow < until)
            {
                if (condition())
                    return;
                await Task.Delay(50);
            }

            Assert.Fail($"Timeout while waiting for: {what}");

        }

        #endregion

    }

}
