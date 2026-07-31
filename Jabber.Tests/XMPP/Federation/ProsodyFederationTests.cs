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
    /// Föderation gegen Prosody - eine fremde, ausgewachsene Gegenstelle.
    /// </summary>
    /// <remarks>
    /// Diese Tests überspringen sich, wenn auf 15269 kein Prosody antwortet.
    /// Der Aufbau steht in <c>tools/prosody/</c>: Prosody wird ohne root in
    /// WSL ausgepackt, bekommt ein von einer Test-CA signiertes Zertifikat und
    /// horcht auf 127.0.0.1:15269. Dieselbe CA signiert unser Zertifikat -
    /// damit trägt SASL-EXTERNAL (XEP-0178), und Dialback wird nicht gebraucht.
    ///
    /// Die Mechanik des Aufbaus steht in
    /// <see cref="AForeignPeerFederationTests"/>; hier steht nur, was Prosody
    /// eigen ist.
    /// </remarks>
    [TestFixture]
    [Category("Prosody")]
    public class ProsodyFederationTests : AForeignPeerFederationTests
    {

        #region Data

        protected override String  PeerName      => "Prosody";
        protected override String  PeerDomain    => "prosody.test";
        protected override Int32   PeerPort      => 15269;
        protected override String  CertVariable  => "JABBER_PROSODY_CERTS";

        /// <summary>
        /// 5269 - der Port, auf den Prosody ohne SRV-Eintrag zurückfällt.
        /// Prosody kennt keinen Schalter dafür, also weicht im eingehenden Lauf
        /// Prosody selbst auf 15269 aus und überlässt uns diesen hier.
        /// </summary>
        protected override Int32   InboundPort   => 5269;

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

            var angekommen = await Links!.DeliverAsync(
                                 PeerDomain,
                                 $"<message from='alice@{LocalDomain}' to='{PeerDomain}' type='chat'>" +
                                 "<body>Hallo Prosody</body></message>",
                                 CancellationToken.None);

            Assert.That(angekommen, Is.True,
                        "Der Stream zu Prosody kam nicht zustande.");

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

        #region ProsodyDialsUsAndTheAnswerArrives()

        /// <summary>
        /// Der eingehende Weg: Prosody baut die Verbindung auf, wir nehmen an.
        /// </summary>
        /// <remarks>
        /// Bis hierher stand unsere annehmende Seite nie vor einer fremden
        /// Gegenstelle. Was hier zum ersten Mal geprüft wird, ist unser
        /// Stream-Kopf als Antwortender, unsere Feature-Ankündigung, unsere
        /// Annahme eines fremden <c>&lt;auth mechanism='EXTERNAL'/&gt;</c> und
        /// die Identitätsprüfung aus dem vorgelegten Zertifikat. Der Rückweg
        /// aus S9 lief zwar in eingehender Richtung, aber über einen Stream,
        /// den <i>wir</i> aufgebaut hatten.
        ///
        /// Ohne XEP-0288, und das ist Absicht: genau dann beantwortet Prosody
        /// den Ping über eine eigene Verbindung zu uns, und die muss unser
        /// Listener annehmen. Mit Bidi käme die Antwort über den bestehenden
        /// Stream, und der eingehende Weg bliebe wieder ungeprüft.
        ///
        /// <b>Dieser Test läuft nur innerhalb von WSL.</b> Von Windows aus
        /// erreicht Prosody uns nicht - die Hyper-V-Firewall verwirft jede
        /// Verbindung von WSL zum Host, und das zu ändern hiesse, eine
        /// Firewall-Regel zu setzen. Im selben Netz ist alles Rückschleife.
        /// </remarks>
        [Test]
        public async Task ProsodyDialsUsAndTheAnswerArrives()
        {

            if (!OperatingSystem.IsLinux())
                Assert.Ignore("Nur innerhalb von WSL: von Windows aus erreicht Prosody diesen Server nicht.");

            Aufbau(erreichbar: true);

            var alice = await AliceAsync();

            var dauer = await alice.PingAsync(PeerDomain);

            Assert.Multiple(() =>
            {

                Assert.That(dauer, Is.Not.Null,
                            "Prosody hat den Ping nicht beantwortet.");

                Assert.That(Links!.InboundConnectionCount, Is.GreaterThan(0),
                            "Die Antwort kam, aber nicht über eine eingehende Verbindung - " +
                            "dann prüft dieser Test nicht, was er prüfen soll.");

                Assert.That(Links.BidirectionalDeliveryCount, Is.Zero,
                            "Aufbau des Tests: hier soll gerade keine Rückrichtung im Spiel sein.");

                // Und der Nachweis, dass Prosody sich über sein Zertifikat
                // ausgewiesen hat und nicht über Dialback: sonst hätten *wir*
                // zurückfragen müssen.
                Assert.That(Links.DialbackVerificationCount, Is.Zero,
                            "Hier soll SASL-EXTERNAL tragen, nicht Dialback.");

            });

        }

        #endregion

        #region DialbackCarriesBothDirections()

        /// <summary>
        /// XEP-0220 gegen eine fremde Gegenstelle - in beiden Rollen.
        /// </summary>
        /// <remarks>
        /// Dialback war zuletzt das einzige Verfahren, das nur gegen die eigene
        /// Gegenstelle geprüft war. Ein Ping-Rundlauf übt beide Rollen auf
        /// einmal, weil jede Richtung ihre eigene Verbindung aufbaut und jede
        /// aufbauende Seite sich ausweisen muss:
        ///
        /// <list type="number">
        ///   <item>
        ///     Wir wählen an und schicken <c>&lt;db:result/&gt;</c>. Prosody
        ///     fragt daraufhin beim autoritativen Server unserer Domain nach -
        ///     das sind wieder wir, auf 5269. Hier antwortet unsere
        ///     <b>autoritative</b> Rolle einer fremden Gegenstelle.
        ///   </item>
        ///   <item>
        ///     Prosody wählt an, um die Antwort zuzustellen, und schickt
        ///     seinerseits <c>&lt;db:result/&gt;</c>. Wir fragen bei
        ///     <c>prosody.test</c> nach. Hier arbeitet unsere <b>prüfende</b>
        ///     Rolle gegen eine fremde Gegenstelle.
        ///   </item>
        /// </list>
        ///
        /// Dass der Ping ankommt, belegt beide: scheiterte Prosodys Rückfrage
        /// an uns, nähme es unsere Stanza nicht an; scheiterte unsere Rückfrage
        /// an Prosody, nähmen wir seine Antwort nicht an.
        /// <c>DialbackVerificationCount</c> hält die zweite Rolle zusätzlich
        /// fest - ohne sie bestünde der Test auch dann, wenn wir jemanden
        /// ungeprüft durchgelassen hätten.
        /// </remarks>
        [Test]
        public async Task DialbackCarriesBothDirections()
        {

            if (!OperatingSystem.IsLinux())
                Assert.Ignore("Nur innerhalb von WSL: Prosodys Rückfrage erreicht diesen Server sonst nicht.");

            Aufbau(erreichbar: true, dialback: true);

            var alice = await AliceAsync();

            var dauer = await alice.PingAsync(PeerDomain);

            Assert.Multiple(() =>
            {

                Assert.That(dauer, Is.Not.Null,
                            "Prosody hat den Ping nicht beantwortet - eine der beiden " +
                            "Rückfragen ist gescheitert.");

                Assert.That(Links!.DialbackVerificationCount, Is.GreaterThan(0),
                            "Wir haben Prosodys Schlüssel nie nachgefragt.");

                Assert.That(Links.InboundConnectionCount, Is.GreaterThan(0),
                            "Ohne eingehende Verbindung gab es auch nichts zu prüfen.");

            });

        }

        #endregion

    }

}
