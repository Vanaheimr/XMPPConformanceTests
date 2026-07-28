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
    /// XEP-0198 Stream Management gegen Prosody - unser Client als Client.
    /// </summary>
    /// <remarks>
    /// Die Zählung stimmt bisher gegen <see cref="XMPPServer"/>, also gegen
    /// unsere eigene Auffassung davon, was eine Stanza ist. Genau das ist bei
    /// XEP-0198 die heikle Stelle: Abschnitt 2 zählt ausschliesslich
    /// <c>message</c>, <c>presence</c> und <c>iq</c>. Alles andere -
    /// <c>&lt;enable/&gt;</c>, <c>&lt;r/&gt;</c>, <c>&lt;a/&gt;</c>,
    /// SASL-Elemente, der Stream-Kopf - zählt nicht. Wer eines davon mitzählt,
    /// merkt es nie an sich selbst, sondern erst an einem fremden Server: die
    /// Zähler laufen auseinander, und irgendwann bestätigt ein <c>h</c> die
    /// falschen Stanzas oder der Gegenüber bricht den Stream ab.
    ///
    /// Deshalb reicht es hier nicht, dass die Warteschlange leerläuft. Ein zu
    /// grosses <c>h</c> täte das auch. Geprüft wird Gleichheit zwischen dem,
    /// was wir gezählt haben, und dem, was Prosody bestätigt.
    ///
    /// Der Aufbau steht in <c>tools/prosody/setup.sh</c>: Prosody bekommt
    /// <c>mod_smacks</c> und <c>mod_websocket</c>, einen HTTPS-Endpunkt auf
    /// 5281 und ein Konto. Unser Client spricht XMPP über WebSocket (RFC 7395),
    /// nicht über den rohen 5222er-Strom - der Weg dorthin ist also
    /// <c>wss://</c>, und ohne <c>mod_websocket</c> gäbe es gar keinen.
    /// </remarks>
    [TestFixture]
    [Category("Prosody")]
    public class ProsodyStreamManagementTests
    {

        #region Data

        private const String PeerDomain  = "prosody.test";
        private const String Endpoint    = "wss://127.0.0.1:5281/xmpp-websocket";
        private const Int32  HttpsPort   = 5281;
        private const String User        = "alice";
        private const String Password    = "geheim";

        private XMPPClient?       _client;
        private X509Certificate2  _ca = null!;

        #endregion

        #region Aufbau / Abbau

        private static String CertDirectory
            => Environment.GetEnvironmentVariable("JABBER_PROSODY_CERTS") ?? "";

        /// <summary>
        /// Meldet einen Client bei Prosody an, oder überspringt den Test.
        /// </summary>
        private async Task<XMPPClient> VerbindeAsync()
        {

            var verzeichnis = CertDirectory;

            if (verzeichnis.Length == 0 || !File.Exists(Path.Combine(verzeichnis, "ca.crt")))
                Assert.Ignore("Kein Prosody-Aufbau: JABBER_PROSODY_CERTS zeigt auf keine Test-CA.");

            if (!PortAntwortet())
                Assert.Ignore($"Auf 127.0.0.1:{HttpsPort} antwortet kein Prosody-WebSocket.");

            _ca = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(verzeichnis, "ca.crt"));

            var connection = new XMPPConnection($"{User}@{PeerDomain}", Password, Endpoint) {
                                 KeepaliveEnabled            = false,
                                 MaxReconnectAttempts        = 0,
                                 StreamManagementEnabled     = true,
                                 ServerCertificateValidator  = TrautDerTestCA
                             };

            _client = new XMPPClient(connection);
            await _client.ConnectAsync();

            Assert.That(_client.StreamManagement, Is.Not.Null,
                        "Ohne Stream-Management-Manager hat dieser Test nichts zu prüfen.");

            return _client;

        }

        [TearDown]
        public async Task Abbau()
        {

            if (_client is not null)
            {
                try { await _client.DisposeAsync(); } catch { /* im Teardown egal */ }
                _client = null;
            }

        }

        #endregion

        #region Hilfsfunktionen

        private static Boolean PortAntwortet()
        {
            try
            {
                using var s = new TcpClient();
                return s.ConnectAsync("127.0.0.1", HttpsPort).Wait(TimeSpan.FromSeconds(2));
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
        /// Der Name wird bewusst nicht geprüft: angewählt wird 127.0.0.1, das
        /// Zertifikat lautet auf <c>prosody.test</c> oder <c>localhost</c>. Ein
        /// Name liesse sich nur über einen Eintrag in <c>/etc/hosts</c>
        /// auflösen, und der bräuchte root. Die Kette wird dafür vollständig
        /// geprüft - "alles annehmen" bestünde auch gegen eine beliebige fremde
        /// Gegenstelle und sagte nichts.
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

        private static async Task WarteAuf(Func<Boolean> bedingung, String was)
        {

            var ok = await XMPPServer.WaitUntilAsync(bedingung, TimeSpan.FromSeconds(10));

            Assert.That(ok, Is.True, $"Zeitüberschreitung beim Warten auf: {was}");

        }

        #endregion


        #region ProsodyAcceptsOurEnable()

        /// <summary>
        /// Prosody nimmt unser <c>&lt;enable/&gt;</c> an.
        /// </summary>
        /// <remarks>
        /// Der schwächste der vier Tests und trotzdem nicht überflüssig: er
        /// belegt, dass unser <c>&lt;enable/&gt;</c> im richtigen Namensraum
        /// (<c>urn:xmpp:sm:3</c>) und an der richtigen Stelle des Aufbaus steht
        /// - nach dem Bind, vor allem Weiteren. Steht es falsch, antwortet
        /// Prosody mit <c>&lt;failed/&gt;</c> statt <c>&lt;enabled/&gt;</c>.
        /// </remarks>
        [Test]
        public async Task ProsodyAcceptsOurEnable()
        {

            var client = await VerbindeAsync();

            Assert.That(client.StreamManagement!.IsEnabled, Is.True,
                        "Prosody hat Stream Management nicht freigeschaltet.");

        }

        #endregion

        #region ProsodyCountsTheSetupExactlyAsWeDo()

        /// <summary>
        /// Nach dem Aufbau melden beide Seiten denselben Stand.
        /// </summary>
        /// <remarks>
        /// Der Test, um dessentwillen dieser Aufbau existiert. Zwischen
        /// <c>&lt;enabled/&gt;</c> und diesem Punkt schickt der Client Carbons,
        /// eine Roster-Abfrage und die erste Presence - und dazwischen Nonzas.
        /// Zählen wir eine davon mit, die Prosody nicht zählt, weichen die
        /// Stände hier um genau diese eine ab.
        ///
        /// Geprüft wird Gleichheit, nicht nur eine leere Warteschlange: ein zu
        /// grosses <c>h</c> räumte sie ebenfalls, und ein Client, der zu wenig
        /// zählt, käme damit durch.
        /// </remarks>
        [Test]
        public async Task ProsodyCountsTheSetupExactlyAsWeDo()
        {

            var client  = await VerbindeAsync();
            var sm      = client.StreamManagement!;

            var unser   = sm.OutboundCount;

            await sm.RequestAckAsync();
            await WarteAuf(() => sm.LastAcknowledged == unser,
                           $"ein <a/> über {unser} Stanzas (Prosody meldete zuletzt {sm.LastAcknowledged})");

            Assert.Multiple(() =>
            {

                Assert.That(sm.LastAcknowledged, Is.EqualTo(unser),
                            "Prosody zählt den Aufbau anders als wir.");

                Assert.That(sm.UnackedCount, Is.Zero,
                            "Nach einem vollständigen Ack darf nichts offen bleiben.");

            });

        }

        #endregion

        #region NonzasDoNotAdvanceTheCount()

        /// <summary>
        /// XEP-0198 Abschnitt 2: Nonzas zählen nicht - auf beiden Seiten.
        /// </summary>
        /// <remarks>
        /// Drei Nachrichten, dazwischen je ein <c>&lt;r/&gt;</c>, und Prosody
        /// beantwortet jedes davon mit einem <c>&lt;a/&gt;</c>. Am Ende darf
        /// der Stand um genau drei gestiegen sein. Zählte eine der beiden
        /// Seiten die sechs Nonzas mit, stünde hier ein anderer Wert - und
        /// zwar einer, den keine Gegenprobe gegen den eigenen Server je
        /// gezeigt hätte, weil dort beide Seiten denselben Fehler machten.
        /// </remarks>
        [Test]
        public async Task NonzasDoNotAdvanceTheCount()
        {

            var client  = await VerbindeAsync();
            var sm      = client.StreamManagement!;

            var vorher  = sm.OutboundCount;

            for (var i = 0; i < 3; i++)
            {

                await client.SendRawAsync(
                          $"<message to='{User}@{PeerDomain}' type='chat' id='zaehl-{i}'>" +
                          $"<body>{i}</body></message>");

                await sm.RequestAckAsync();

            }

            await WarteAuf(() => sm.LastAcknowledged == vorher + 3,
                           $"ein <a/> über {vorher + 3} Stanzas (zuletzt {sm.LastAcknowledged})");

            Assert.Multiple(() =>
            {

                Assert.That(sm.OutboundCount, Is.EqualTo(vorher + 3),
                            "Wir haben Nonzas mitgezählt.");

                Assert.That(sm.LastAcknowledged, Is.EqualTo(sm.OutboundCount),
                            "Prosody hat andere Nonzas mitgezählt als wir.");

            });

        }

        #endregion

        #region OurInboundCountIsNotTooHigh()

        /// <summary>
        /// Die Gegenrichtung: unser <c>&lt;a h='...'/&gt;</c> übersteigt nicht,
        /// was Prosody geschickt hat.
        /// </summary>
        /// <remarks>
        /// Für die eingehende Richtung gibt es keinen Wert, den die Gegenstelle
        /// uns nennt - wir können unseren Zähler also nicht direkt vergleichen.
        /// Prosody prüft ihn aber: ein <c>h</c>, das grösser ist als die Zahl
        /// der tatsächlich geschickten Stanzas, ist ein Protokollfehler und
        /// beendet den Stream.
        ///
        /// Der Nachweis läuft deshalb über das Weiterleben: wir melden unseren
        /// Stand und fragen danach nach. Kommt die Antwort, hat Prosody den
        /// Wert hingenommen. Nach unten ist er damit nicht abgesichert - ein zu
        /// kleines <c>h</c> wäre zulässig und fiele hier nicht auf.
        /// </remarks>
        [Test]
        public async Task OurInboundCountIsNotTooHigh()
        {

            var client  = await VerbindeAsync();
            var sm      = client.StreamManagement!;

            await sm.SendAckAsync();

            var dauer = await client.PingAsync();

            Assert.That(dauer, Is.Not.Null,
                        $"Prosody hat nach unserem <a h='{sm.InboundCount}'/> nicht mehr geantwortet - " +
                        "vermutlich haben wir mehr gezählt, als es geschickt hat.");

        }

        #endregion

    }

}
