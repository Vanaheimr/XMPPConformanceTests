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

using NUnit.Framework;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Föderation gegen ejabberd - die zweite fremde Gegenstelle.
    /// </summary>
    /// <remarks>
    /// Warum eine zweite, wo doch schon eine steht: Prosody allein belegt, dass
    /// wir mit Prosody können. Wo unsere Auffassung des Protokolls von der
    /// Norm abweicht, Prosody aber dieselbe Abweichung mitmacht - sei es aus
    /// Nachsicht, sei es aus derselben Lesart -, fällt das nicht auf. Erst eine
    /// zweite, unabhängig entstandene Implementierung trennt "richtig" von
    /// "deckungsgleich mit einer Auffassung".
    ///
    /// ejabberd ist dafür der interessante Gegner: in Erlang geschrieben,
    /// anderer Werdegang, anderer Autorenkreis, eigene Vorlieben im Handshake.
    /// Was gegen beide trägt, trägt vermutlich.
    ///
    /// Der Aufbau steht in <c>tools/ejabberd/setup.sh</c>, die Mechanik in
    /// <see cref="AForeignPeerFederationTests"/>. Die Tests überspringen sich,
    /// wenn auf 25269 kein ejabberd antwortet.
    /// </remarks>
    [TestFixture]
    [Category("ejabberd")]
    public class EjabberdFederationTests : AForeignPeerFederationTests
    {

        #region Data

        protected override String  PeerName      => "ejabberd";
        protected override String  PeerDomain    => "ejabberd.test";
        protected override Int32   PeerPort      => 25269;
        protected override String  CertVariable  => "JABBER_EJABBERD_CERTS";

        /// <summary>
        /// 5270, nicht 5269 - damit beide Gegenstellen nebeneinander laufen
        /// können und kein Lauf versehentlich an der falschen hängt.
        /// </summary>
        /// <remarks>
        /// Frei wählbar ist der Port nur, weil ejabberd einen ausdrücklichen
        /// Schalter dafür hat: ohne SRV-Eintrag nimmt es
        /// <c>outgoing_s2s_port</c>. Prosody kennt keinen und bleibt fest bei
        /// 5269 - dort horcht deshalb der Prosody-Aufbau.
        /// </remarks>
        protected override Int32   InboundPort   => 5270;

        #endregion


        #region TheStreamToEjabberdCarriesAStanza()

        /// <summary>
        /// Der ausgehende Weg gegen die zweite Gegenstelle: STARTTLS,
        /// SASL-EXTERNAL, eine Stanza hinaus.
        /// </summary>
        /// <remarks>
        /// Derselbe Weg wie gegen Prosody, aber gegen einen Prüfer, der ihn
        /// unabhängig davon gelesen hat: unser Stream-Kopf, unser
        /// STARTTLS-Aufbau, unser Klientzertifikat, unser
        /// <c>&lt;auth mechanism='EXTERNAL'/&gt;</c> und der Neustart des
        /// Streams danach. Kommt die Stanza durch, hat keine der beiden
        /// Auffassungen etwas zu beanstanden gehabt.
        ///
        /// Warum ein <c>iq</c> vom Typ <c>result</c> und keine Nachricht: eine
        /// Nachricht an die blosse Domain hat dort keinen Empfänger und wird
        /// zurückgewiesen. Für die Rückweisung legt ejabberd eine ausgehende
        /// Verbindung zu <c>jabber.test</c> an - und die überlebt diesen Test,
        /// weil unser Server auf einem Ephemeralport lag, den es nach dem
        /// Abbau nicht mehr gibt. Der nächste Test bekommt sie dann aus dem
        /// Zwischenspeicher vorgesetzt und verliert seine Antwort darin. Ein
        /// <c>result</c> darf laut RFC 6120, Abschnitt 8.3.1, nie beantwortet
        /// werden und hinterlässt deshalb nichts.
        /// </remarks>
        [Test]
        public async Task TheStreamToEjabberdCarriesAStanza()
        {

            Aufbau();

            var angekommen = await Links!.DeliverAsync(
                                 PeerDomain,
                                 $"<iq from='alice@{LocalDomain}' to='{PeerDomain}' " +
                                 "type='result' id='hallo-ejabberd'/>",
                                 CancellationToken.None);

            Assert.That(angekommen, Is.True,
                        "Der Stream zu ejabberd kam nicht zustande.");

        }

        #endregion

        #region APingOverABidirectionalStream()

        /// <summary>
        /// XEP-0288 gegen die zweite Gegenstelle.
        /// </summary>
        /// <remarks>
        /// Der Test mit dem grössten Ertrag, weil die Erweiterung die jüngste
        /// und am wenigsten eingefahrene ist: sie schreibt vor, <i>wo</i> im
        /// Handshake <c>&lt;bidi/&gt;</c> zu stehen hat - nach STARTTLS, vor
        /// SASL und vor Dialback -, und eine Gegenstelle, die es an anderer
        /// Stelle noch durchgehen liesse, deckte einen Fehler bei uns zu.
        ///
        /// ejabberd kündigt <c>urn:xmpp:features:bidi</c> an, sobald
        /// <c>mod_s2s_bidi</c> geladen ist; <c>tools/ejabberd/setup.sh</c>
        /// schaltet es ein.
        /// </remarks>
        [Test]
        public async Task APingOverABidirectionalStream()
        {

            Aufbau(bidi: true);

            var alice = await AliceAsync();

            var dauer = await alice.PingAsync(PeerDomain);

            Assert.That(dauer, Is.Not.Null,
                        "ejabberd hat den Ping nicht über die Rückrichtung beantwortet.");

        }

        #endregion

        #region EjabberdDialsUsAndTheAnswerArrives()

        /// <summary>
        /// Der eingehende Weg: ejabberd baut die Verbindung auf, wir nehmen an.
        /// </summary>
        /// <remarks>
        /// Geprüft wird unsere annehmende Seite - Stream-Kopf als
        /// Antwortender, Feature-Ankündigung, Annahme eines fremden
        /// <c>&lt;auth mechanism='EXTERNAL'/&gt;</c>, Identitätsprüfung aus dem
        /// vorgelegten Zertifikat -, diesmal vor einem zweiten Gegenüber.
        ///
        /// Ohne XEP-0288, und das ist Absicht: genau dann beantwortet ejabberd
        /// den Ping über eine eigene Verbindung zu uns, und die muss unser
        /// Listener annehmen. Mit Bidi käme die Antwort über den bestehenden
        /// Stream, und der eingehende Weg bliebe ungeprüft.
        ///
        /// <b>Dieser Test läuft nur innerhalb von WSL.</b> Von Windows aus
        /// erreicht ejabberd uns nicht - die Hyper-V-Firewall verwirft jede
        /// Verbindung von WSL zum Host, und das zu ändern hiesse, eine
        /// Firewall-Regel zu setzen. Im selben Netz ist alles Rückschleife.
        /// </remarks>
        [Test]
        public async Task EjabberdDialsUsAndTheAnswerArrives()
        {

            if (!OperatingSystem.IsLinux())
                Assert.Ignore("Nur innerhalb von WSL: von Windows aus erreicht ejabberd diesen Server nicht.");

            Aufbau(erreichbar: true);

            var alice = await AliceAsync();

            var dauer = await alice.PingAsync(PeerDomain);

            Assert.Multiple(() =>
            {

                Assert.That(dauer, Is.Not.Null,
                            "ejabberd hat den Ping nicht beantwortet.");

                Assert.That(Links!.InboundConnectionCount, Is.GreaterThan(0),
                            "Die Antwort kam, aber nicht über eine eingehende Verbindung - " +
                            "dann prüft dieser Test nicht, was er prüfen soll.");

                Assert.That(Links.BidirectionalDeliveryCount, Is.Zero,
                            "Aufbau des Tests: hier soll gerade keine Rückrichtung im Spiel sein.");

                Assert.That(Links.DialbackVerificationCount, Is.Zero,
                            "Hier soll SASL-EXTERNAL tragen, nicht Dialback.");

            });

        }

        #endregion

        #region DialbackCarriesBothDirections()

        /// <summary>
        /// XEP-0220 gegen die zweite Gegenstelle - in beiden Rollen.
        /// </summary>
        /// <remarks>
        /// Ein Ping-Rundlauf übt beide Rollen auf einmal, weil jede Richtung
        /// ihre eigene Verbindung aufbaut und jede aufbauende Seite sich
        /// ausweisen muss: unsere <b>autoritative</b> Rolle beantwortet
        /// ejabberds Rückfrage nach unserem Schlüssel, unsere <b>prüfende</b>
        /// Rolle fragt bei <c>ejabberd.test</c> nach seinem.
        ///
        /// Dass ejabberd Dialback überhaupt zulässt, hängt an einer Zeile der
        /// Konfiguration: <c>s2s_use_starttls: required</c> statt
        /// <c>required_trusted</c>. Das zweite verlangt zusätzlich eine gültige
        /// Zertifikatskette und schlösse Dialback aus. Welches Verfahren zum
        /// Zug kommt, entscheidet damit unsere Seite - legen wir kein
        /// Klientzertifikat vor, bleibt nur Dialback.
        /// </remarks>
        [Test]
        public async Task DialbackCarriesBothDirections()
        {

            if (!OperatingSystem.IsLinux())
                Assert.Ignore("Nur innerhalb von WSL: ejabberds Rückfrage erreicht diesen Server sonst nicht.");

            Aufbau(erreichbar: true, dialback: true);

            var alice = await AliceAsync();

            var dauer = await alice.PingAsync(PeerDomain);

            Assert.Multiple(() =>
            {

                Assert.That(dauer, Is.Not.Null,
                            "ejabberd hat den Ping nicht beantwortet - eine der beiden " +
                            "Rückfragen ist gescheitert.");

                Assert.That(Links!.DialbackVerificationCount, Is.GreaterThan(0),
                            "Wir haben ejabberds Schlüssel nie nachgefragt.");

                Assert.That(Links.InboundConnectionCount, Is.GreaterThan(0),
                            "Ohne eingehende Verbindung gab es auch nichts zu prüfen.");

            });

        }

        #endregion

    }

}
