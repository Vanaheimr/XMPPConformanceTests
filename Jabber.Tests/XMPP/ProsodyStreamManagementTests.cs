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
        private const String User2       = "bob";
        private const String Password    = "geheim";

        private readonly List<XMPPClient>  _clients = [];
        private X509Certificate2           _ca = null!;

        #endregion

        #region Aufbau / Abbau

        private static String CertDirectory
            => Environment.GetEnvironmentVariable("JABBER_PROSODY_CERTS") ?? "";

        /// <summary>
        /// Meldet einen Client bei Prosody an, oder überspringt den Test.
        /// </summary>
        /// <param name="localPart">Welches der beiden Testkonten.</param>
        /// <param name="reconnect">
        /// Wie oft der Client nach einem Abriss wiederkommen darf. Null für
        /// alles, was den Reconnect nicht braucht - dann steht am Testende
        /// nichts mehr im Hintergrund.
        /// </param>
        private async Task<XMPPClient> VerbindeAsync(String  localPart  = User,
                                                     Int32   reconnect  = 0)
        {

            var verzeichnis = CertDirectory;

            if (verzeichnis.Length == 0 || !File.Exists(Path.Combine(verzeichnis, "ca.crt")))
                Assert.Ignore("Kein Prosody-Aufbau: JABBER_PROSODY_CERTS zeigt auf keine Test-CA.");

            if (!PortAntwortet())
                Assert.Ignore($"Auf 127.0.0.1:{HttpsPort} antwortet kein Prosody-WebSocket.");

            _ca = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(verzeichnis, "ca.crt"));

            var connection = new XMPPConnection($"{localPart}@{PeerDomain}", Password, Endpoint) {
                                 KeepaliveEnabled            = false,
                                 MaxReconnectAttempts        = reconnect,
                                 InitialReconnectDelay       = TimeSpan.FromMilliseconds(300),
                                 StreamManagementEnabled     = true,
                                 ServerCertificateValidator  = TrautDerTestCA
                             };

            var client = new XMPPClient(connection);
            _clients.Add(client);

            await client.ConnectAsync();

            Assert.That(client.StreamManagement, Is.Not.Null,
                        "Ohne Stream-Management-Manager hat dieser Test nichts zu prüfen.");

            return client;

        }

        [TearDown]
        public async Task Abbau()
        {

            foreach (var client in _clients)
            {
                try { await client.DisposeAsync(); } catch { /* im Teardown egal */ }
            }

            _clients.Clear();

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

        #region ProsodyPromisesToKeepTheStream()

        /// <summary>
        /// Prosody sagt die Wiederaufnahme zu.
        /// </summary>
        /// <remarks>
        /// Bis hierher war die Zusage nur gegen den eigenen Server geprüft -
        /// also gegen unsere eigene Auffassung davon, wie ein
        /// <c>&lt;enable resume='true'/&gt;</c> auszusehen hat und was in der
        /// Antwort stehen muss. Kommt hier eine Kennung an, hat Prosody unser
        /// Ansinnen verstanden.
        /// </remarks>
        [Test]
        public async Task ProsodyPromisesToKeepTheStream()
        {

            var alice = await VerbindeAsync();

            Assert.Multiple(() =>
            {

                Assert.That(alice.StreamManagement!.CanResume, Is.True,
                            "Prosody hat die Wiederaufnahme nicht zugesagt.");

                Assert.That(alice.StreamManagement.ResumeId, Is.Not.Null.And.Not.Empty);

            });

        }

        #endregion

        #region TheStreamSurvivesABrokenConnection()

        /// <summary>
        /// Nach einem Abriss knüpft der Client bei Prosody an denselben Stream
        /// an, statt eine neue Resource zu binden.
        /// </summary>
        /// <remarks>
        /// Der Test, um dessentwillen dieser Lauf existiert. Gegen den eigenen
        /// Server prüft die Wiederaufnahme beide Seiten mit derselben Auffassung
        /// davon, wann ein <c>&lt;resume/&gt;</c> geschickt werden darf, was
        /// hineingehört und was zurückkommt. Prosody hat diese Auffassung nicht
        /// von uns.
        ///
        /// Die Verbindung wird von <b>unserer</b> Seite abgerissen - gegen eine
        /// fremde Gegenstelle gibt es keinen anderen Weg, und ein ordentliches
        /// Abmelden wäre gerade das Gegenteil dessen, was hier zu prüfen ist.
        ///
        /// Die unveränderte Kennung ist der Beleg: eine neue Aushandlung
        /// brächte eine neue.
        /// </remarks>
        [Test]
        public async Task TheStreamSurvivesABrokenConnection()
        {

            var alice = await VerbindeAsync(reconnect: 5);

            var vorher   = alice.FullJid;
            var kennung  = alice.StreamManagement!.ResumeId;

            // Ohne zugesagte Wiederaufnahme ist die Kennung auf beiden Seiten
            // null - und damit "unveraendert". Der Vergleich unten sagte dann
            // nichts. Die Mutation, die resume='true' weglaesst, kam genau
            // hier durch.
            Assert.That(alice.StreamManagement.CanResume, Is.True,
                        "Prosody hat die Wiederaufnahme gar nicht zugesagt.");

            var wiederVerbunden = 0;
            alice.OnStateChanged += (_, neu) =>
            {
                if (neu == ConnectionState.Connected)
                    Interlocked.Increment(ref wiederVerbunden);
            };

            alice.KillConnection();

            await WarteAuf(() => wiederVerbunden > 0, "den wiederaufgenommenen Stream");

            Assert.Multiple(() =>
            {

                Assert.That(alice.FullJid, Is.EqualTo(vorher),
                            "Prosody hat eine neue Resource vergeben - dann wurde neu gebunden.");

                Assert.That(alice.StreamManagement.ResumeId, Is.EqualTo(kennung),
                            "Neue Kennung, also neu ausgehandelt statt wieder aufgenommen.");

            });

        }

        #endregion

        #region ProsodyHoldsBackWhatArrivedDuringTheOutage()

        /// <summary>
        /// Was während des Abrisses ankam, liefert Prosody nach.
        /// </summary>
        /// <remarks>
        /// Der eigentliche Gewinn, und die Stelle, an der eine fremde
        /// Gegenstelle mehr sagt als die eigene: unser Server puffert, weil wir
        /// ihm das beigebracht haben. Dass Prosody es auch tut und dass wir das
        /// Nachgelieferte richtig entgegennehmen, steht auf einem anderen Blatt.
        ///
        /// Bob und Alice brauchen dafür keine Subscription - eine Nachricht
        /// geht auch ohne, nur Presence nicht.
        ///
        /// <b>Dass die Nachricht ankommt, genügt als Beleg nicht.</b> Prosody
        /// stellt sie auch dann zu, wenn die Wiederaufnahme gar nicht versucht
        /// wird und der Client eine neue Resource bindet - sie geht dann eben
        /// dorthin, und der Test bestünde, ohne von der Wiederaufnahme etwas
        /// zu wissen. Genau das ist ihm bei der Mutation „nie wiederaufnehmen"
        /// passiert. Geprüft wird deshalb beides: dass sie ankommt <i>und</i>
        /// dass es derselbe Stream war.
        /// </remarks>
        [Test]
        public async Task ProsodyHoldsBackWhatArrivedDuringTheOutage()
        {

            var alice = await VerbindeAsync(reconnect: 5);
            var bob   = await VerbindeAsync(User2);

            var vorher  = alice.FullJid;
            var kennung = alice.StreamManagement!.ResumeId;

            Assert.That(alice.StreamManagement.CanResume, Is.True,
                        "Prosody hat die Wiederaufnahme gar nicht zugesagt.");

            var angekommen = new List<String>();
            alice.OnMessage += m => { lock (angekommen) angekommen.Add(m.Body); };

            var wiederVerbunden = 0;
            alice.OnStateChanged += (_, neu) =>
            {
                if (neu == ConnectionState.Connected)
                    Interlocked.Increment(ref wiederVerbunden);
            };

            // Prosody weiss noch nichts vom Abriss: was Bob jetzt schickt, geht
            // in den aufgehobenen Stream.
            alice.KillConnection();

            await bob.SendMessageAsync($"{User}@{PeerDomain}", "Im Dunkeln geschickt");

            await WarteAuf(() => wiederVerbunden > 0, "den wiederaufgenommenen Stream");

            await WarteAuf(() => { lock (angekommen) return angekommen.Contains("Im Dunkeln geschickt"); },
                           "die nachgelieferte Nachricht");

            Assert.Multiple(() =>
            {

                Assert.That(alice.FullJid, Is.EqualTo(vorher),
                            "Die Nachricht kam an, aber an einer neu gebundenen Resource - " +
                            "dann prüft dieser Test nicht die Wiederaufnahme.");

                Assert.That(alice.StreamManagement.ResumeId, Is.EqualTo(kennung));

            });

        }

        #endregion

    }

}
