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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// RFC 6120, Abschnitt 5: XMPP läuft über TLS.
    ///
    /// Der Testserver sprach <c>ws://</c>, also gingen die Passwörter der
    /// SASL-PLAIN-Anmeldung im Klartext über die Leitung. Alles andere in
    /// diesem Projekt war damit akademisch.
    ///
    /// Diese Tests prüfen zweierlei, und das zweite ist das schwierigere: dass
    /// eine Verbindung zustande kommt, und dass sie es aus dem richtigen Grund
    /// tut. Eine TLS-Prüfung, die alles durchwinkt, ist von einer, die das
    /// richtige Zertifikat erkennt, nur an den Gegenproben zu unterscheiden.
    /// </summary>
    [TestFixture]
    public class TlsTests : AXMPPTests
    {

        #region ServerUri_IsWss()

        /// <summary>
        /// Der Einstieg: der Server bietet <c>wss://</c> an, nicht
        /// <c>ws://</c>.
        /// </summary>
        [Test]
        public void ServerUri_IsWss()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Server.Uri, Does.StartWith("wss://"),
                            "Der Server muss über TLS erreichbar sein.");

                Assert.That(Server.Certificate, Is.Not.Null,
                            "Ohne Zertifikat gibt es kein TLS.");
            });

        }

        #endregion

        #region Client_ConnectsOverTls()

        /// <summary>
        /// Ein Client verbindet sich über TLS und kann sich anmelden - die
        /// gesamte Aushandlung läuft dann verschlüsselt.
        /// </summary>
        [Test]
        public async Task Client_ConnectsOverTls()
        {

            var client = await ConnectClientAsync();

            Assert.Multiple(() =>
            {
                Assert.That(client.IsConnected, Is.True);
                Assert.That(client.Connection.WebSocketUri, Does.StartWith("wss://"));
            });

        }

        #endregion

        #region RejectedCertificate_PreventsTheConnection()

        /// <summary>
        /// Die erste Gegenprobe: weist der Client das Zertifikat zurück, kommt
        /// keine Verbindung zustande.
        ///
        /// Ohne diesen Test bestünde der vorige auch dann, wenn gar kein TLS im
        /// Spiel wäre - eine Zertifikatsprüfung, die nie aufgerufen wird, kann
        /// nichts verhindern.
        /// </summary>
        [Test]
        public async Task RejectedCertificate_PreventsTheConnection()
        {

            Server.AddAccount("alice");

            var client = CreateClient();

            client.Connection.MaxReconnectAttempts       = 0;
            client.Connection.ServerCertificateValidator = (_, _, _, _) => false;

            var errors = new List<String>();
            client.OnError += e => errors.Add(e);

            await FailingConnectAsync(client);

            Assert.Multiple(() =>
            {
                Assert.That(client.IsConnected, Is.False,
                            "Ein zurückgewiesenes Zertifikat darf keine Verbindung ergeben.");

                Assert.That(errors, Is.Not.Empty,
                            "Der Abbruch muss gemeldet werden.");
            });

        }

        #endregion

        #region DefaultValidation_RejectsTheSelfSignedCertificate()

        /// <summary>
        /// Die zweite Gegenprobe: ohne eigene Prüfung greift die des
        /// Betriebssystems, und die lehnt ein selbst signiertes Zertifikat ab.
        ///
        /// Damit ist belegt, dass die Verbindungen der übrigen Tests am
        /// angehefteten Fingerabdruck hängen und nicht daran, dass irgendwo
        /// eine Prüfung fehlt.
        /// </summary>
        [Test]
        public async Task DefaultValidation_RejectsTheSelfSignedCertificate()
        {

            Server.AddAccount("alice");

            var client = CreateClient();

            client.Connection.MaxReconnectAttempts       = 0;
            client.Connection.ServerCertificateValidator = null;

            await FailingConnectAsync(client);

            Assert.That(client.IsConnected, Is.False,
                        "Ein selbst signiertes Zertifikat darf die Standardprüfung nicht bestehen.");

        }

        #endregion

        #region PinnedCertificate_AcceptsOnlyTheOwnOne()

        /// <summary>
        /// Die Prüfung, mit der alle Tests arbeiten, nimmt genau das
        /// Zertifikat dieses Servers an - ein anderes, gleich gebautes nicht.
        /// </summary>
        /// <remarks>
        /// Ein zweiter Server erzeugt sich ein eigenes Zertifikat mit
        /// demselben Namen. Würde die Prüfung nur den Namen ansehen, ginge das
        /// hier durch.
        /// </remarks>
        [Test]
        public async Task PinnedCertificate_AcceptsOnlyTheOwnOne()
        {

            await using var anderer = Watched(new XMPPServer());

            Assert.Multiple(() =>
            {

                Assert.That(Server.IsOwnCertificate(this, Server.Certificate, null, System.Net.Security.SslPolicyErrors.None),
                            Is.True,
                            "Das eigene Zertifikat muss angenommen werden.");

                Assert.That(Server.IsOwnCertificate(this, anderer.Certificate, null, System.Net.Security.SslPolicyErrors.None),
                            Is.False,
                            "Das Zertifikat eines anderen Servers darf nicht durchgehen.");

            });

        }

        #endregion

        #region PlainServer_StillSpeaksWs()

        /// <summary>
        /// Der Ausweg bleibt offen: ohne TLS spricht der Server weiterhin
        /// <c>ws://</c>. Das ist für die Fehlersuche mit einem Mitschnitt
        /// nützlich - und der Schalter soll nicht bloss behauptet sein.
        /// </summary>
        [Test]
        public async Task PlainServer_StillSpeaksWs()
        {

            await using var klartext = Watched(new XMPPServer(useTLS: false));

            klartext.Start();
            klartext.AddAccount("alice");

            Assert.That(klartext.Uri, Does.StartWith("ws://"));
            Assert.That(klartext.Certificate, Is.Null);

            var connection = new XMPPConnection($"alice@{klartext.Domain}",
                                                "pw",
                                                klartext.Uri)
            {
                KeepaliveEnabled      = false,
                MaxReconnectAttempts  = 0
            };

            await using var client = new XMPPClient(connection);

            await client.ConnectAsync();

            Assert.That(client.IsConnected, Is.True);

        }

        #endregion

        #region ASuppliedCertificate_IsUsedInsteadOfASelfSignedOne()

        /// <summary>
        /// Das Serverzertifikat darf von aussen kommen.
        /// </summary>
        /// <remarks>
        /// Ein selbst signiertes Zertifikat kann keine fremde Gegenstelle
        /// prüfen: sie müsste genau dieses eine kennen, und es entsteht bei
        /// jedem Start neu. Für einen Lauf gegen Prosody - und für jeden
        /// Betrieb, der kein Test ist - muss es aus einer Kette kommen, der
        /// beide Seiten trauen. Daran scheiterte der Anlauf gegen eine fremde
        /// Gegenstelle, bevor ein einziges Byte Protokoll gewechselt war.
        /// </remarks>
        [Test]
        public async Task ASuppliedCertificate_IsUsedInsteadOfASelfSignedOne()
        {

            using var eigenes = ErzeugeZertifikat("beispiel.test");

            await using var server = Watched(new XMPPServer("beispiel.test", certificate: eigenes));

            server.Start();
            server.AddAccount("alice");

            Assert.That(server.Certificate?.Thumbprint, Is.EqualTo(eigenes.Thumbprint),
                        "Der Server hat sich trotzdem eines selbst gebaut.");

            // Und es trägt auch wirklich den Handshake: die Prüfung des
            // Clients heftet den Fingerabdruck genau dieses Zertifikats an.
            var connection = new XMPPConnection($"alice@{server.Domain}", "pw", server.Uri) {
                                 KeepaliveEnabled            = false,
                                 MaxReconnectAttempts        = 0,
                                 ServerCertificateValidator  = (_, c, _, _) =>
                                     c is not null &&
                                     c.GetCertHashString(HashAlgorithmName.SHA256)
                                      .Equals(eigenes.GetCertHashString(HashAlgorithmName.SHA256),
                                              StringComparison.OrdinalIgnoreCase)
                             };

            await using var client = new XMPPClient(connection);

            await client.ConnectAsync();

            Assert.That(client.IsConnected, Is.True);

        }

        private static X509Certificate2 ErzeugeZertifikat(String domain)
        {

            using var key = RSA.Create(2048);

            var request = new CertificateRequest($"CN={domain}", key,
                                                 HashAlgorithmName.SHA256,
                                                 RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], true));

            var namen = new SubjectAlternativeNameBuilder();
            namen.AddDnsName(domain);
            namen.AddDnsName("localhost");
            request.CertificateExtensions.Add(namen.Build());

            var zertifikat = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
                                                      DateTimeOffset.UtcNow.AddDays(1));

            return X509CertificateLoader.LoadPkcs12(zertifikat.Export(X509ContentType.Pfx), null);

        }

        #endregion

    }

}
