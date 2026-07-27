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
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Föderation gegen Prosody - eine fremde, ausgewachsene Gegenstelle.
    /// </summary>
    /// <remarks>
    /// Alles andere in dieser Testsammlung prüft unseren Server gegen unseren
    /// Server. Das beweist, dass beide Seiten dieselbe Auffassung vom Protokoll
    /// haben - nicht, dass diese Auffassung stimmt. Wo unsere beiden Seiten
    /// denselben Fehler machen, fällt er nicht auf.
    ///
    /// Diese Tests überspringen sich, wenn auf 5269 kein Prosody antwortet.
    /// Der Aufbau steht in <c>tools/prosody/</c>: Prosody wird ohne root in
    /// WSL ausgepackt, bekommt ein von einer Test-CA signiertes Zertifikat und
    /// horcht auf 127.0.0.1:5269. Dieselbe CA signiert unser Zertifikat -
    /// damit trägt SASL-EXTERNAL (XEP-0178), und Dialback wird nicht gebraucht.
    /// </remarks>
    [TestFixture]
    [Category("Prosody")]
    public class ProsodyFederationTests
    {

        #region Data

        private const String PeerDomain   = "prosody.test";
        private const String LocalDomain  = "jabber.test";
        private const Int32  PeerPort     = 5269;

        private XMPPServer?       _server;
        private XMPPClient?       _client;
        private TcpServerLinks?   _links;
        private X509Certificate2  _ca      = null!;
        private X509Certificate2  _ourCert = null!;

        #endregion

        #region Aufbau / Abbau

        /// <summary>
        /// Wo die Test-CA und unser Zertifikat liegen. Ohne sie oder ohne
        /// laufenden Prosody hat dieser Test nichts zu prüfen.
        /// </summary>
        private static String CertDirectory
            => Environment.GetEnvironmentVariable("JABBER_PROSODY_CERTS") ?? "";

        /// <summary>
        /// Baut Server und S2S-Zweig auf, oder überspringt den Test, wenn kein
        /// Prosody bereitsteht.
        /// </summary>
        /// <param name="bidi">XEP-0288 aushandeln.</param>
        private void Aufbau(Boolean bidi = false)
        {

            var verzeichnis = CertDirectory;

            if (verzeichnis.Length == 0 || !File.Exists(Path.Combine(verzeichnis, "ca.crt")))
                Assert.Ignore("Kein Prosody-Aufbau: JABBER_PROSODY_CERTS zeigt auf keine Test-CA.");

            if (!PortAntwortet())
                Assert.Ignore($"Auf 127.0.0.1:{PeerPort} antwortet kein Prosody.");

            _ca       = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(verzeichnis, "ca.crt"));
            _ourCert  = X509CertificateLoader.LoadPkcs12FromFile(
                            Path.Combine(verzeichnis, $"{LocalDomain}.pfx"), null);

            _server   = new XMPPServer(LocalDomain, certificate: _ourCert);
            _server.Start();
            _server.AddAccount("alice");

            _links = new TcpServerLinks(_server, mode: TcpTlsMode.StartTls) {
                         UseSaslExternal          = true,
                         UseBidirectionalStreams  = bidi
                     };

            _links.AddPeer(PeerDomain, "127.0.0.1", PeerPort, TcpTlsMode.StartTls, TrautDerTestCA);

        }

        [TearDown]
        public async Task Abbau()
        {

            if (_client is not null)
            {
                try { await _client.DisposeAsync(); } catch { /* im Teardown egal */ }
                _client = null;
            }

            if (_server is not null)
            {
                await _server.DisposeAsync();
                _server = null;
            }

        }

        #endregion

        #region Hilfsfunktionen

        private static Boolean PortAntwortet()
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
        /// Nimmt genau die Zertifikate an, die von der Test-CA signiert sind.
        /// </summary>
        /// <remarks>
        /// Nicht "alles annehmen": eine Prüfung, die jedes Zertifikat
        /// durchlässt, bestünde auch gegen eine beliebige fremde Gegenstelle
        /// und sagte über den Handshake nichts aus. Der Betriebssystemspeicher
        /// hilft hier nicht - die Test-CA steht dort nicht und soll es auch
        /// nicht.
        /// </remarks>
        private Boolean TrautDerTestCA(Object            sender,
                                       X509Certificate?  certificate,
                                       X509Chain?        chain,
                                       SslPolicyErrors   errors)
        {

            if (certificate is null)
                return false;

            var zertifikat = certificate as X509Certificate2
                                 ?? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());

            using var pruefung = new X509Chain();

            pruefung.ChainPolicy.TrustMode       = X509ChainTrustMode.CustomRootTrust;
            pruefung.ChainPolicy.RevocationMode  = X509RevocationMode.NoCheck;
            pruefung.ChainPolicy.CustomTrustStore.Add(_ca);

            return pruefung.Build(zertifikat);

        }

        private async Task<XMPPClient> AliceAsync()
        {

            var connection = new XMPPConnection($"alice@{LocalDomain}", "pw", _server!.Uri) {
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


        #region TheStreamToProsodyCarriesAStanza()

        /// <summary>
        /// Der ausgehende Weg gegen eine fremde Gegenstelle: STARTTLS,
        /// SASL-EXTERNAL, eine Stanza hinaus.
        /// </summary>
        /// <remarks>
        /// Ein <c>true</c> von <c>DeliverAsync</c> heisst hier mehr als bei
        /// einem Lauf gegen die eigene Gegenstelle: Prosody hat den
        /// STARTTLS-Aufbau angenommen, unser Zertifikat gegen seine CA geprüft,
        /// <c>EXTERNAL</c> angeboten, unsere Identität daraus abgeleitet und
        /// den Stream freigegeben. Jeder dieser Schritte war bisher nur gegen
        /// unsere eigene Auffassung davon geprüft.
        /// </remarks>
        [Test]
        public async Task TheStreamToProsodyCarriesAStanza()
        {

            Aufbau();

            var angekommen = await _links!.DeliverAsync(
                                 PeerDomain,
                                 $"<message from='alice@{LocalDomain}' to='{PeerDomain}' type='chat'>" +
                                 "<body>Hallo Prosody</body></message>",
                                 CancellationToken.None);

            Assert.That(angekommen, Is.True,
                        "Der Stream zu Prosody kam nicht zustande.");

        }

        #endregion

        #region APingReachesProsodyAndComesBack()

        /// <summary>
        /// Der ganze Weg: eine Stanza hinaus und die Antwort zurück.
        /// </summary>
        /// <remarks>
        /// Prosody nimmt den Ping an und erzeugt die Antwort - sie steht so im
        /// Log. Es schickt sie aber nicht über den Stream zurück, über den die
        /// Frage kam, sondern baut dafür eine <i>eigene</i> Verbindung zu
        /// <c>jabber.test</c> auf. Genau so ist RFC 6120, Abschnitt 4.1
        /// gemeint: ein XML-Stream ist einseitig. Unsere Seite tut dasselbe -
        /// <c>DeliverAsync</c> geht ausnahmslos über eine ausgehende
        /// Verbindung, und eine Stanza auf einem ausgehenden Stream wird
        /// abgewiesen.
        ///
        /// Hier scheitert der Rückweg an der Umgebung und nicht am Protokoll:
        /// in WSL gibt es kein DNS für <c>.test</c>, und die Hyper-V-Firewall
        /// verwirft ohnehin jede Verbindung von WSL zum Windows-Host.
        ///
        /// Genau dafür gibt es XEP-0288, und mit ausgehandelter Bidi läuft
        /// dieser Weg über <see cref="APingOverABidirectionalStream"/>. Dieser
        /// Test bleibt als Gegenprobe stehen: <b>ohne</b> Bidi kommt keine
        /// Antwort, solange die Gegenstelle uns nicht erreichen kann.
        /// </remarks>
        [Test]
        [Ignore("Ohne XEP-0288 braucht die Antwort eine eigene Verbindung von " +
                "Prosody zu uns - die verwirft die Hyper-V-Firewall. Mit Bidi " +
                "läuft derselbe Weg in APingOverABidirectionalStream.")]
        public async Task APingReachesProsodyAndComesBack()
        {

            Aufbau();

            var alice = await AliceAsync();

            var dauer = await alice.PingAsync(PeerDomain);

            Assert.That(dauer, Is.Not.Null,
                        "Prosody hat den Ping nicht beantwortet.");

        }

        #endregion

        #region APingOverABidirectionalStream()

        /// <summary>
        /// Dasselbe mit XEP-0288: die Antwort nimmt die Verbindung, über die
        /// die Frage kam.
        /// </summary>
        /// <remarks>
        /// Der Test, um dessentwillen der Prosody-Aufbau existiert. Die
        /// Rückrichtung ist sonst nur gegen die eigene Gegenstelle geprüft -
        /// und eine Aushandlung, bei der beide Seiten dieselbe Vorstellung von
        /// der Erweiterung haben, beweist über die Erweiterung nichts.
        ///
        /// Prosody kündigt <c>urn:xmpp:features:bidi</c> an, sobald
        /// <c>mod_s2s_bidi</c> läuft; <c>tools/prosody/setup.sh</c> schaltet es
        /// ein. Kommt die Antwort an, stand unser <c>&lt;bidi/&gt;</c> in der
        /// richtigen Form, im richtigen Namensraum und an der richtigen Stelle
        /// des Handshakes.
        /// </remarks>
        [Test]
        public async Task APingOverABidirectionalStream()
        {

            Aufbau(bidi: true);

            var alice = await AliceAsync();

            var dauer = await alice.PingAsync(PeerDomain);

            Assert.That(dauer, Is.Not.Null,
                        "Prosody hat den Ping nicht über die Rückrichtung beantwortet.");

        }

        #endregion

    }

}
