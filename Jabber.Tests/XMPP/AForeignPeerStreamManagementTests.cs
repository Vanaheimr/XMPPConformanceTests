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

using System.Collections.Concurrent;
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
    /// XEP-0198 Stream Management und Wiederaufnahme gegen eine fremde
    /// Gegenstelle - unser Client als Client.
    /// </summary>
    /// <remarks>
    /// Die Zählung stimmt gegen <see cref="XMPPServer"/>, also gegen unsere
    /// eigene Auffassung davon, was eine Stanza ist. Genau das ist bei
    /// XEP-0198 die heikle Stelle: Abschnitt 2 zählt ausschliesslich
    /// <c>message</c>, <c>presence</c> und <c>iq</c>. Alles andere -
    /// <c>&lt;enable/&gt;</c>, <c>&lt;r/&gt;</c>, <c>&lt;a/&gt;</c>,
    /// SASL-Elemente, der Stream-Kopf - zählt nicht. Wer eines davon mitzählt,
    /// merkt es nie an sich selbst, sondern erst an einem fremden Server.
    ///
    /// Dasselbe gilt für die Wiederaufnahme: gegen den eigenen Server teilen
    /// beide Seiten eine Auffassung davon, wann ein <c>&lt;resume/&gt;</c>
    /// geschickt werden darf, was hineingehört und was zurückkommt. Ein fremder
    /// Server hat diese Auffassung nicht von uns.
    ///
    /// Die Tests stehen hier und nicht in den Ableitungen: sie prüfen für jede
    /// Gegenstelle dasselbe, und was sich unterscheidet - Domain, Endpunkt,
    /// Port, Konten, Umgebungsvariable - legen die Ableitungen fest. Ein
    /// dritter Server kostet damit zwanzig Zeilen.
    /// </remarks>
    public abstract class AForeignPeerStreamManagementTests
    {

        #region Was die Gegenstelle ausmacht

        /// <summary>Name der Gegenstelle - nur für Fehlermeldungen.</summary>
        protected abstract String  PeerName      { get; }

        /// <summary>Die Domain, die die Gegenstelle bedient.</summary>
        protected abstract String  PeerDomain    { get; }

        /// <summary>Der WebSocket-Endpunkt (RFC 7395).</summary>
        protected abstract String  Endpoint      { get; }

        /// <summary>Der Port dahinter - für die Erreichbarkeitsprüfung.</summary>
        protected abstract Int32   EndpointPort  { get; }

        /// <summary>
        /// Die Umgebungsvariable, die auf das Zertifikatsverzeichnis des
        /// Aufbaus zeigt.
        /// </summary>
        protected abstract String  CertVariable  { get; }

        #endregion

        #region Data

        /// <summary>Das Konto des Clients selbst.</summary>
        protected const String User      = "alice";

        /// <summary>Ein zweites Konto als Absender.</summary>
        protected const String User2     = "bob";

        protected const String Password  = "geheim";

        private readonly List<XMPPClient>  _clients = [];
        private X509Certificate2           _ca = null!;

        #endregion

        #region Aufbau / Abbau

        private String CertDirectory
            => Environment.GetEnvironmentVariable(CertVariable) ?? "";

        /// <summary>
        /// Meldet einen Client an, oder überspringt den Test.
        /// </summary>
        /// <param name="localPart">Welches der beiden Testkonten.</param>
        /// <param name="reconnect">
        /// Wie oft der Client nach einem Abriss wiederkommen darf. Null für
        /// alles, was den Reconnect nicht braucht - dann steht am Testende
        /// nichts mehr im Hintergrund.
        /// </param>
        protected async Task<XMPPClient> VerbindeAsync(String  localPart  = User,
                                                       Int32   reconnect  = 0)
        {

            var verzeichnis = CertDirectory;

            if (verzeichnis.Length == 0 || !File.Exists(Path.Combine(verzeichnis, "ca.crt")))
                Assert.Ignore($"Kein {PeerName}-Aufbau: {CertVariable} zeigt auf keine Test-CA.");

            if (!PortAntwortet())
                Assert.Ignore($"Auf 127.0.0.1:{EndpointPort} antwortet kein {PeerName}-WebSocket.");

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

        private Boolean PortAntwortet()
        {
            try
            {
                using var s = new TcpClient();
                return s.ConnectAsync("127.0.0.1", EndpointPort).Wait(TimeSpan.FromSeconds(2));
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
        /// Zertifikat lautet auf die Domain der Gegenstelle. Ein Name liesse
        /// sich nur über einen Eintrag in <c>/etc/hosts</c> auflösen, und der
        /// bräuchte root. Die Kette wird dafür vollständig geprüft - "alles
        /// annehmen" bestünde auch gegen eine beliebige fremde Gegenstelle und
        /// sagte nichts.
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

            var ok = await XMPPServer.WaitUntilAsync(bedingung, TimeSpan.FromSeconds(15));

            Assert.That(ok, Is.True, $"Zeitüberschreitung beim Warten auf: {was}");

        }

        /// <summary>
        /// Zählt die Übergänge nach <c>Connected</c> - der einzige Zeitpunkt,
        /// zu dem der Aufbau nachweislich abgeschlossen ist.
        /// </summary>
        /// <remarks>
        /// Auf etwas Früheres zu warten - etwa darauf, dass die Gegenstelle
        /// den Stream abgeholt hat - prüft den Client mitten in seiner
        /// Aufbauphase, in einem Zustand, den er gleich wieder verlässt.
        /// </remarks>
        /// <remarks>
        /// Mitgeschrieben wird seit D33 auch der <b>Weg</b> dorthin. Die
        /// Zählung allein genügte, solange alles gutging; blieb die
        /// Wiederaufnahme aus, sagte die Meldung nur „Zeitüberschreitung beim
        /// Warten auf: den wiederaufgenommenen Stream" — und damit nichts
        /// darüber, wie weit der Client gekommen ist: ob er es überhaupt
        /// versucht hat, wie oft, und woran es lag.
        ///
        /// Genau das ist in D16 passiert — ein Fehlschlag in einem von vier
        /// Vollläufen, unaufklärbar, weil die Meldung nichts hergab. Er stand
        /// seitdem als offener Punkt im Plan.
        /// </remarks>
        private static Verlauf ZaehleWiederverbindungen(XMPPClient client)
            => new(client);

        /// <summary>
        /// Zählt die abgeschlossenen Aufbauten und schreibt mit, was dazwischen
        /// geschah.
        /// </summary>
        /// <remarks>
        /// Zwanzig gezielte Durchgänge in D33 konnten den Fehlschlag aus D16
        /// nicht wiederholen: Die vierzig Ausführungen lagen alle zwischen 519
        /// und 669 Millisekunden, bei einer Frist von 15 Sekunden. Die damalige
        /// Vermutung „unter Last knapp" trägt damit nicht — die Frist bleibt
        /// deshalb unverändert.
        ///
        /// Was bleibt, ist die Vorsorge für das nächste Mal: Der Verlauf steht
        /// dann in der Meldung. <b>Ein Fehlschlag, der sich selbst erklärt,
        /// kostet einmal Schreibarbeit; einer, der es nicht tut, kostet jedes
        /// Mal eine Untersuchung.</b>
        /// </remarks>
        protected sealed class Verlauf
        {

            private readonly ConcurrentQueue<String> _schritte = new();
            private Int32 _verbunden;

            public Verlauf(XMPPClient client)
            {

                client.OnStateChanged += (alt, neu) =>
                {

                    _schritte.Enqueue($"{alt}->{neu}");

                    if (neu == ConnectionState.Connected)
                        Interlocked.Increment(ref _verbunden);

                };

                client.OnError += meldung => _schritte.Enqueue($"Fehler: {meldung}");

            }

            /// <summary>Hat der Client den Aufbau mindestens einmal abgeschlossen?</summary>
            public Boolean WiederVerbunden
                => Volatile.Read(ref _verbunden) > 0;

            public override String ToString()
                => _schritte.IsEmpty
                       ? "(nichts geschehen - der Client hat es nicht einmal versucht)"
                       : String.Join(" | ", _schritte);

        }

        /// <summary>
        /// Wartet auf die Wiederaufnahme und nennt beim Scheitern den Verlauf.
        /// </summary>
        private static async Task WarteAufWiederaufnahmeAsync(Verlauf verlauf)
        {

            var ok = await XMPPServer.WaitUntilAsync(() => verlauf.WiederVerbunden,
                                                     TimeSpan.FromSeconds(15));

            Assert.That(ok, Is.True,
                        "Der Stream wurde binnen 15 Sekunden nicht wieder aufgenommen. " +
                        $"Verlauf: {verlauf}");

        }

        #endregion


        #region TheServerAcceptsOurEnable()

        /// <summary>
        /// Die Gegenstelle nimmt unser <c>&lt;enable/&gt;</c> an.
        /// </summary>
        /// <remarks>
        /// Der schwächste dieser Tests und trotzdem nicht überflüssig: er
        /// belegt, dass unser <c>&lt;enable/&gt;</c> im richtigen Namensraum
        /// (<c>urn:xmpp:sm:3</c>) und an der richtigen Stelle des Aufbaus steht
        /// - nach dem Bind, vor allem Weiteren. Steht es falsch, kommt
        /// <c>&lt;failed/&gt;</c> statt <c>&lt;enabled/&gt;</c>.
        /// </remarks>
        [Test]
        public async Task TheServerAcceptsOurEnable()
        {

            var client = await VerbindeAsync();

            Assert.That(client.StreamManagement!.IsEnabled, Is.True,
                        $"{PeerName} hat Stream Management nicht freigeschaltet.");

        }

        #endregion

        #region TheServerCountsTheSetupExactlyAsWeDo()

        /// <summary>
        /// Nach dem Aufbau melden beide Seiten denselben Stand.
        /// </summary>
        /// <remarks>
        /// Der Test, um dessentwillen dieser Aufbau existiert. Zwischen
        /// <c>&lt;enabled/&gt;</c> und diesem Punkt schickt der Client Carbons,
        /// eine Roster-Abfrage und die erste Presence - und dazwischen Nonzas.
        /// Zählen wir eine davon mit, die die Gegenstelle nicht zählt, weichen
        /// die Stände hier um genau diese eine ab.
        ///
        /// Geprüft wird Gleichheit, nicht nur eine leere Warteschlange: ein zu
        /// grosses <c>h</c> räumte sie ebenfalls, und ein Client, der zu wenig
        /// zählt, käme damit durch.
        /// </remarks>
        [Test]
        public async Task TheServerCountsTheSetupExactlyAsWeDo()
        {

            var client  = await VerbindeAsync();
            var sm      = client.StreamManagement!;

            var unser   = sm.OutboundCount;

            await sm.RequestAckAsync();
            await WarteAuf(() => sm.LastAcknowledged == unser,
                           $"ein <a/> über {unser} Stanzas (zuletzt {sm.LastAcknowledged})");

            Assert.Multiple(() =>
            {

                Assert.That(sm.LastAcknowledged, Is.EqualTo(unser),
                            $"{PeerName} zählt den Aufbau anders als wir.");

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
        /// Drei Nachrichten, dazwischen je ein <c>&lt;r/&gt;</c>, und die
        /// Gegenstelle beantwortet jedes davon mit einem <c>&lt;a/&gt;</c>. Am
        /// Ende darf der Stand um genau drei gestiegen sein. Zählte eine der
        /// beiden Seiten die sechs Nonzas mit, stünde hier ein anderer Wert -
        /// und zwar einer, den keine Gegenprobe gegen den eigenen Server je
        /// gezeigt hätte, weil dort beide Seiten denselben Fehler machten.
        /// </remarks>
        [Test]
        public async Task NonzasDoNotAdvanceTheCount()
        {

            var client  = await VerbindeAsync();
            var sm      = client.StreamManagement!;

            // Was tatsächlich hinausgeht - für den Fall, dass die Zahl am Ende
            // nicht stimmt.
            //
            // Zahlen sagen, *dass* etwas nicht stimmt, und nie *was*. Dieser
            // Test ist in D34 einmal in einem Vollauf gefallen ("Expected: 6,
            // But was: 8"), und die zwei überzähligen Stanzas liessen sich
            // hinterher nicht mehr benennen - dieselbe Sackgasse wie in D16 und
            // D29. Der Mitschnitt kostet nichts und beendet sie.
            var hinaus = new ConcurrentQueue<String>();

            client.Connection.OnRawXml += x =>
            {
                if (x.StartsWith(">>>", StringComparison.Ordinal))
                    hinaus.Enqueue(x);
            };

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

            var mitschnitt = String.Join("\n   ", hinaus);

            Assert.Multiple(() =>
            {

                Assert.That(sm.OutboundCount, Is.EqualTo(vorher + 3),
                            $"Wir haben Nonzas mitgezählt. Hinausgegangen ist:\n   {mitschnitt}");

                Assert.That(sm.LastAcknowledged, Is.EqualTo(sm.OutboundCount),
                            $"{PeerName} hat andere Nonzas mitgezählt als wir. " +
                            $"Hinausgegangen ist:\n   {mitschnitt}");

            });

        }

        #endregion

        #region OurInboundCountIsNotTooHigh()

        /// <summary>
        /// Die Gegenrichtung: unser <c>&lt;a h='...'/&gt;</c> übersteigt nicht,
        /// was die Gegenstelle geschickt hat.
        /// </summary>
        /// <remarks>
        /// Für die eingehende Richtung gibt es keinen Wert, den die Gegenstelle
        /// uns nennt - wir können unseren Zähler also nicht direkt vergleichen.
        /// Sie prüft ihn aber: ein <c>h</c>, das grösser ist als die Zahl der
        /// tatsächlich geschickten Stanzas, ist ein Protokollfehler und beendet
        /// den Stream.
        ///
        /// Der Nachweis läuft deshalb über das Weiterleben: wir melden unseren
        /// Stand und fragen danach nach. Kommt die Antwort, wurde der Wert
        /// hingenommen. Nach unten ist er damit nicht abgesichert - ein zu
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
                        $"{PeerName} hat nach unserem <a h='{sm.InboundCount}'/> nicht mehr " +
                        "geantwortet - vermutlich haben wir mehr gezählt, als es geschickt hat.");

        }

        #endregion

        #region TheServerPromisesToKeepTheStream()

        /// <summary>
        /// Die Gegenstelle sagt die Wiederaufnahme zu.
        /// </summary>
        /// <remarks>
        /// Kommt hier eine Kennung an, hat sie unser
        /// <c>&lt;enable resume='true'/&gt;</c> verstanden.
        /// </remarks>
        [Test]
        public async Task TheServerPromisesToKeepTheStream()
        {

            var alice = await VerbindeAsync();

            Assert.Multiple(() =>
            {

                Assert.That(alice.StreamManagement!.CanResume, Is.True,
                            $"{PeerName} hat die Wiederaufnahme nicht zugesagt.");

                Assert.That(alice.StreamManagement.ResumeId, Is.Not.Null.And.Not.Empty);

            });

        }

        #endregion

        #region TheStreamSurvivesABrokenConnection()

        /// <summary>
        /// Nach einem Abriss knüpft der Client an denselben Stream an, statt
        /// eine neue Resource zu binden.
        /// </summary>
        /// <remarks>
        /// Die Verbindung wird von <b>unserer</b> Seite abgerissen - gegen eine
        /// fremde Gegenstelle gibt es keinen anderen Weg, und ein ordentliches
        /// Abmelden wäre gerade das Gegenteil dessen, was hier zu prüfen ist.
        ///
        /// Die unveränderte Kennung ist der Beleg; die Full-JID allein taugt
        /// nicht, weil die Resource prozessfest ist und ein neuer Bind dieselbe
        /// Adresse ergäbe.
        /// </remarks>
        [Test]
        public async Task TheStreamSurvivesABrokenConnection()
        {

            var alice = await VerbindeAsync(reconnect: 5);

            var vorher   = alice.FullJid;
            var kennung  = alice.StreamManagement!.ResumeId;

            // Ohne zugesagte Wiederaufnahme wäre die Kennung auf beiden Seiten
            // null - und damit "unverändert". Der Vergleich unten sagte dann
            // nichts.
            Assert.That(alice.StreamManagement.CanResume, Is.True,
                        $"{PeerName} hat die Wiederaufnahme gar nicht zugesagt.");

            var wiederVerbunden = ZaehleWiederverbindungen(alice);

            alice.KillConnection();

            await WarteAufWiederaufnahmeAsync(wiederVerbunden);

            Assert.Multiple(() =>
            {

                Assert.That(alice.FullJid, Is.EqualTo(vorher),
                            "Eine neue Resource vergeben - dann wurde neu gebunden.");

                Assert.That(alice.StreamManagement.ResumeId, Is.EqualTo(kennung),
                            "Neue Kennung, also neu ausgehandelt statt wieder aufgenommen.");

            });

        }

        #endregion

        #region TheServerHoldsBackWhatArrivedDuringTheOutage()

        /// <summary>
        /// Was während des Abrisses ankam, liefert die Gegenstelle nach.
        /// </summary>
        /// <remarks>
        /// Der eigentliche Gewinn, und die Stelle, an der eine fremde
        /// Gegenstelle mehr sagt als die eigene: unser Server puffert, weil wir
        /// ihm das beigebracht haben.
        ///
        /// <b>Dass die Nachricht ankommt, genügt als Beleg nicht.</b> Ein
        /// Server stellt sie auch dann zu, wenn die Wiederaufnahme gar nicht
        /// versucht wird und der Client eine neue Resource bindet - sie geht
        /// dann eben dorthin, und der Test bestünde, ohne von der
        /// Wiederaufnahme etwas zu wissen. Genau das ist ihm bei der Mutation
        /// „nie wiederaufnehmen" passiert. Geprüft wird deshalb beides.
        ///
        /// Alice und Bob brauchen dafür keine Subscription - eine Nachricht
        /// geht auch ohne, nur Presence nicht.
        /// </remarks>
        [Test]
        public async Task TheServerHoldsBackWhatArrivedDuringTheOutage()
        {

            var alice = await VerbindeAsync(reconnect: 5);
            var bob   = await VerbindeAsync(User2);

            var vorher  = alice.FullJid;
            var kennung = alice.StreamManagement!.ResumeId;

            Assert.That(alice.StreamManagement.CanResume, Is.True,
                        $"{PeerName} hat die Wiederaufnahme gar nicht zugesagt.");

            var angekommen = new List<String>();
            alice.OnMessage += m => { lock (angekommen) angekommen.Add(m.Body); };

            var wiederVerbunden = ZaehleWiederverbindungen(alice);

            // Die Gegenstelle weiss noch nichts vom Abriss: was Bob jetzt
            // schickt, geht in den aufgehobenen Stream.
            alice.KillConnection();

            await bob.SendMessageAsync($"{User}@{PeerDomain}", "Im Dunkeln geschickt");

            await WarteAufWiederaufnahmeAsync(wiederVerbunden);

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
