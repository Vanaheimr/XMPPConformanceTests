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

using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// XEP-0288: beide Richtungen über eine Verbindung - die Protokollschicht,
    /// ohne Transport darunter.
    /// </summary>
    /// <remarks>
    /// Ohne die Erweiterung ist eine S2S-Verbindung einseitig (RFC 6120,
    /// Abschnitt 4.1): wer eine Stanza bekommt, beantwortet sie über eine
    /// eigene Verbindung zur Absenderdomain. Das setzt voraus, dass er die
    /// Gegenstelle erreichen kann. Kann er das nicht - hinter NAT, hinter einer
    /// Firewall, ohne DNS-Eintrag -, geht die Antwort verloren, und zwar
    /// stillschweigend.
    ///
    /// Wie in <see cref="S2SStreamTests"/> werden die Handshakes von Hand
    /// gebaut. Zwei Instanzen gegeneinander laufen zu lassen prüfte nur, dass
    /// die Klasse zu sich selbst passt.
    /// </remarks>
    [TestFixture]
    public class BidirectionalStreamTests
    {

        #region Data

        private List<String> _gesendet = null!;

        #endregion

        #region SetUp / Hilfsfunktionen

        [SetUp]
        public void Leeren()
        {
            _gesendet = [];
        }

        private Task Senden(String frame, CancellationToken _)
        {
            _gesendet.Add(frame);
            return Task.CompletedTask;
        }

        private static String OpenVon(String from, String? to = "rechts.example", String? id = null)

            => $"<open xmlns='urn:ietf:params:xml:ns:xmpp-framing' from='{from}'" +
               (to is not null ? $" to='{to}'" : "") +
               (id is not null ? $" id='{id}'" : "") +
               " version='1.0'/>";

        /// <summary>Die Features des Empfängers, wahlweise mit Bidi-Ankündigung.</summary>
        private static String FeaturesMit(Boolean bidi, Boolean external = true)

            => "<stream:features xmlns:stream='http://etherx.jabber.org/streams'>" +
               (external
                    ? $"<mechanisms xmlns='{S2SStream.SaslNamespace}'><mechanism>EXTERNAL</mechanism></mechanisms>"
                    : "") +
               (bidi ? $"<bidi xmlns='{S2SStream.BidiFeatureNamespace}'/>" : "") +
               "</stream:features>";

        private Boolean Gesendet(String enthaelt)
            => _gesendet.Any(f => f.Contains(enthaelt, StringComparison.Ordinal));

        private Int32 IndexVon(String enthaelt)
            => _gesendet.FindIndex(f => f.Contains(enthaelt, StringComparison.Ordinal));

        #endregion


        #region TheReceiverAnnouncesBidi()

        /// <summary>
        /// XEP-0288, Abschnitt 3: wer es beherrscht, kündigt es in den
        /// Features an - in beiden Formen, die in freier Wildbahn vorkommen.
        /// </summary>
        /// <remarks>
        /// Die XEP-Form (<c>urn:xmpp:features:bidi</c>) ist die richtige, und
        /// Prosody greift genau sie auf. ejabberd 24.12 nicht: es kündigt
        /// selbst <c>urn:xmpp:bidi</c> an und sucht offenbar dasselbe. Ohne
        /// die zweite Form nimmt es unsere Rückrichtung nicht - beobachtet in
        /// <c>ThePeerTakesTheReturnPathWeOffered</c>, nicht vermutet.
        ///
        /// Auf dem Draht bleibt es eindeutig: das Freischalt-Element heisst in
        /// beiden Lesarten <c>urn:xmpp:bidi</c>, es kommt also nur eine
        /// Antwort zurück.
        /// </remarks>
        [Test]
        public async Task TheReceiverAnnouncesBidi()
        {

            var stream = S2SStream.Accept("rechts.example",
                                          Senden,
                                          (_, _) => Task.FromResult(RemoteStanzaResult.Accepted),
                                          offerBidi: true);

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            Assert.Multiple(() =>
            {

                Assert.That(Gesendet($"<bidi xmlns='{S2SStream.BidiFeatureNamespace}'/>"), Is.True,
                            "Die Form der XEP fehlt.");

                Assert.That(Gesendet($"<bidi xmlns='{S2SStream.BidiNamespace}'/>"), Is.True,
                            "Die Form, die ejabberd 24.12 sucht, fehlt.");

            });

        }

        #endregion

        #region WithoutTheSwitch_NothingIsAnnounced()

        /// <summary>
        /// Abgeschaltet wird nichts angekündigt - und ohne Ankündigung darf
        /// die Gegenstelle es nicht benutzen.
        /// </summary>
        [Test]
        public async Task WithoutTheSwitch_NothingIsAnnounced()
        {

            var stream = S2SStream.Accept("rechts.example",
                                          Senden,
                                          (_, _) => Task.FromResult(RemoteStanzaResult.Accepted));

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet(S2SStream.BidiFeatureNamespace), Is.False);
                Assert.That(stream.BidiEnabled, Is.False);
            });

        }

        #endregion

        #region AnUnannouncedBidi_IsRefused()

        /// <summary>
        /// Ein <c>&lt;bidi/&gt;</c> ohne vorherige Ankündigung schaltet nichts
        /// frei.
        /// </summary>
        /// <remarks>
        /// Sonst liesse sich eine Rückrichtung erzwingen, die dieser Server nie
        /// angeboten hat - und über die er anschliessend Stanzas hinausschickte,
        /// die er sonst über eine eigene, geprüfte Verbindung geschickt hätte.
        /// </remarks>
        [Test]
        public async Task AnUnannouncedBidi_IsRefused()
        {

            var stream = S2SStream.Accept("rechts.example",
                                          Senden,
                                          (_, _) => Task.FromResult(RemoteStanzaResult.Accepted));

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            var angenommen = await stream.ProcessFrameAsync(
                                 $"<bidi xmlns='{S2SStream.BidiNamespace}'/>");

            Assert.Multiple(() =>
            {
                Assert.That(angenommen,         Is.False);
                Assert.That(stream.BidiEnabled, Is.False);
            });

        }

        #endregion

        #region TheInitiatorAsksForBidi_OnlyWhenOffered()

        /// <summary>
        /// Abschnitt 4: der aufbauende Server schickt das
        /// <c>&lt;bidi/&gt;</c> - aber nur, wenn die Gegenstelle es angeboten
        /// hat.
        /// </summary>
        [Test]
        public async Task TheInitiatorAsksForBidi_OnlyWhenOffered()
        {

            var mitAngebot = S2SStream.Initiate("links.example", "rechts.example", Senden,
                                                canOfferExternal: true, useBidi: true);

            await mitAngebot.ProcessFrameAsync(OpenVon("rechts.example", "links.example", "abc"));
            await mitAngebot.ProcessFrameAsync(FeaturesMit(bidi: true));

            Assert.That(Gesendet($"<bidi xmlns='{S2SStream.BidiNamespace}'/>"), Is.True,
                        "Angeboten und nicht erbeten.");
            Assert.That(mitAngebot.BidiEnabled, Is.True);

            _gesendet.Clear();

            var ohneAngebot = S2SStream.Initiate("links.example", "rechts.example", Senden,
                                                 canOfferExternal: true, useBidi: true);

            await ohneAngebot.ProcessFrameAsync(OpenVon("rechts.example", "links.example", "abc"));
            await ohneAngebot.ProcessFrameAsync(FeaturesMit(bidi: false));

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet(S2SStream.BidiNamespace), Is.False,
                            "Nicht angeboten und trotzdem erbeten.");
                Assert.That(ohneAngebot.BidiEnabled, Is.False);
            });

        }

        #endregion

        #region AnOfferInTheEnableNamespace_IsUnderstood()

        /// <summary>
        /// Eine Ankündigung im Namensraum des Freischalt-Elements wird
        /// ebenfalls als Angebot gelesen.
        /// </summary>
        /// <remarks>
        /// XEP-0288 vergibt zwei Namensräume: <c>urn:xmpp:features:bidi</c>
        /// für die Ankündigung in den Features, <c>urn:xmpp:bidi</c> für das
        /// Element, mit dem der aufbauende Server sie annimmt. Prosody hält
        /// sich daran.
        ///
        /// ejabberd 24.12 nicht: seine annehmende Seite legt das
        /// <i>Freischalt</i>-Element in die Features, kündigt also
        /// <c>&lt;bidi xmlns='urn:xmpp:bidi'/&gt;</c> an. Upstream ist das
        /// inzwischen behoben - in den ausgelieferten Fassungen steht es noch.
        ///
        /// Das ist kein Fehler, den wir mitmachen: wir kündigen weiter die
        /// Form der XEP an, und ejabberds aufbauende Seite sucht genau die
        /// (sein Codec bildet beide Formen auf getrennte Typen ab). Nur beim
        /// <b>Lesen</b> eines fremden Angebots sind wir nachsichtig - sonst
        /// verlöre die Rückrichtung gegen jeden dieser Server, und übrig
        /// bliebe eine Verbindung, die stillschweigend einseitig ist.
        /// </remarks>
        [Test]
        public async Task AnOfferInTheEnableNamespace_IsUnderstood()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden,
                                            canOfferExternal: true, useBidi: true);

            await stream.ProcessFrameAsync(OpenVon("rechts.example", "links.example", "abc"));

            // Wortwörtlich das, was ejabberd 24.12 schickt.
            await stream.ProcessFrameAsync(
                      "<stream:features xmlns:stream='http://etherx.jabber.org/streams'>" +
                      $"<mechanisms xmlns='{S2SStream.SaslNamespace}'><mechanism>EXTERNAL</mechanism></mechanisms>" +
                      "<dialback xmlns='urn:xmpp:features:dialback'><errors/></dialback>" +
                      $"<bidi xmlns='{S2SStream.BidiNamespace}'/>" +
                      "</stream:features>");

            Assert.Multiple(() =>
            {

                Assert.That(Gesendet($"<bidi xmlns='{S2SStream.BidiNamespace}'/>"), Is.True,
                            "Das Angebot stand da, nur im anderen Namensraum.");

                Assert.That(stream.BidiEnabled, Is.True);

            });

        }

        #endregion

        #region BidiGoesOutBeforeTheAuthentication()

        /// <summary>
        /// Abschnitt 4: <i>"This SHOULD be done before either SASL negotiation
        /// or Server Dialback."</i>
        /// </summary>
        /// <remarks>
        /// Die Reihenfolge ist kein Schönheitsfehler. Die Gegenstelle
        /// entscheidet beim Abschluss der Authentifizierung, wie sie künftig
        /// antwortet; ein <c>&lt;bidi/&gt;</c> danach käme zu spät und liesse
        /// sie eine eigene Verbindung aufbauen.
        /// </remarks>
        [Test]
        public async Task BidiGoesOutBeforeTheAuthentication()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden,
                                            canOfferExternal: true, useBidi: true);

            await stream.ProcessFrameAsync(OpenVon("rechts.example", "links.example", "abc"));
            await stream.ProcessFrameAsync(FeaturesMit(bidi: true));

            var bidi = IndexVon(S2SStream.BidiNamespace);
            var auth = IndexVon("<auth");

            Assert.Multiple(() =>
            {
                Assert.That(bidi, Is.GreaterThanOrEqualTo(0), "Kein <bidi/> geschickt.");
                Assert.That(auth, Is.GreaterThanOrEqualTo(0), "Kein <auth/> geschickt.");
                Assert.That(bidi, Is.LessThan(auth),
                            "Das <bidi/> muss vor der Authentifizierung hinausgehen.");
            });

        }

        #endregion

        #region BidiAlsoGoesOutBeforeDialback()

        /// <summary>
        /// Dasselbe für den Dialback-Weg - der Abschnitt nennt beide.
        /// </summary>
        [Test]
        public async Task BidiAlsoGoesOutBeforeDialback()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden,
                                            secret:  DialbackKey.NewSecret(),
                                            useBidi: true);

            await stream.ProcessFrameAsync(OpenVon("rechts.example", "links.example", "abc"));
            await stream.ProcessFrameAsync(FeaturesMit(bidi: true, external: false));

            var bidi     = IndexVon(S2SStream.BidiNamespace);
            var dialback = IndexVon("<db:result");

            Assert.Multiple(() =>
            {
                Assert.That(bidi,     Is.GreaterThanOrEqualTo(0));
                Assert.That(dialback, Is.GreaterThanOrEqualTo(0));
                Assert.That(bidi,     Is.LessThan(dialback));
            });

        }

        #endregion

        #region TheInitiatorTakesStanzasOnlyOnceBidiIsEnabled()

        /// <summary>
        /// Ohne Bidi trägt ein ausgehender Stream nur eine Richtung; mit Bidi
        /// nimmt er entgegen, was zurückkommt.
        /// </summary>
        [Test]
        public async Task TheInitiatorTakesStanzasOnlyOnceBidiIsEnabled()
        {

            var zugestellt = new List<String>();

            Task<RemoteStanzaResult> Zustellen(String _, String stanza)
            {
                zugestellt.Add(stanza);
                return Task.FromResult(RemoteStanzaResult.Accepted);
            }

            var ohne = S2SStream.Initiate("links.example", "rechts.example", Senden,
                                          deliverStanza: Zustellen);

            await ohne.ProcessFrameAsync(OpenVon("rechts.example", "links.example", "abc"));

            var abgewiesen = await ohne.ProcessFrameAsync(
                                 "<message from='juliet@rechts.example' to='romeo@links.example'/>");

            Assert.Multiple(() =>
            {
                Assert.That(abgewiesen, Is.False,
                            "Ohne Bidi darf ein ausgehender Stream nichts entgegennehmen.");
                Assert.That(zugestellt, Is.Empty);
            });

            var mit = S2SStream.Initiate("links.example", "rechts.example", Senden,
                                         canOfferExternal:  true,
                                         deliverStanza:     Zustellen,
                                         useBidi:           true);

            await mit.ProcessFrameAsync(OpenVon("rechts.example", "links.example", "abc"));
            await mit.ProcessFrameAsync(FeaturesMit(bidi: true));

            var angenommen = await mit.ProcessFrameAsync(
                                 "<message from='juliet@rechts.example' to='romeo@links.example'/>");

            Assert.Multiple(() =>
            {
                Assert.That(angenommen, Is.True);
                Assert.That(zugestellt, Has.Count.EqualTo(1));
            });

        }

        #endregion

        #region TheReturnPath_StaysShutBeforeAuthentication()

        /// <summary>
        /// Abschnitt 4: <i>"The receiving server MUST NOT send stanzas to the
        /// peer before it has authenticated via SASL, or the peer's identity
        /// has been verified via Server Dialback."</i>
        /// </summary>
        /// <remarks>
        /// Wer noch nicht belegt hat, wer er ist, bekommt auch nichts. Ohne
        /// diese Zeile liesse sich mit einer blossen Behauptung im
        /// Stream-Kopf fremde Post abholen - man baut eine Verbindung auf,
        /// nennt sich <c>example.com</c>, bittet um die Rückrichtung und
        /// wartet.
        /// </remarks>
        [Test]
        public async Task TheReturnPath_StaysShutBeforeAuthentication()
        {

            var stream = S2SStream.Accept("rechts.example",
                                          Senden,
                                          (_, _) => Task.FromResult(RemoteStanzaResult.Accepted),
                                          verifyKey:  (_, _, _) => Task.FromResult(true),
                                          offerBidi:  true);

            await stream.ProcessFrameAsync(OpenVon("links.example"));
            await stream.ProcessFrameAsync($"<bidi xmlns='{S2SStream.BidiNamespace}'/>");

            Assert.That(stream.BidiEnabled,  Is.True);
            Assert.That(stream.IsAuthenticated, Is.False, "Aufbau des Tests: noch nicht ausgewiesen.");

            var ging = await stream.SendStanzaOverBidiAsync(
                           "<message from='juliet@rechts.example' to='romeo@links.example'/>");

            Assert.That(ging, Is.False,
                        "Vor dem Ausweis darf über die Rückrichtung nichts hinausgehen.");

        }

        #endregion

        #region TheReturnPath_CarriesOnlyOurOwnDomain()

        /// <summary>
        /// Abschnitt 4: <i>"The receiving server MUST only send stanzas for
        /// which it has been authenticated - ... this is the value of the
        /// stream's 'to' attribute."</i>
        /// </summary>
        /// <remarks>
        /// Das <c>to</c> des eingehenden Stream-Kopfs ist die eigene Domain.
        /// Für eine fremde zu sprechen wäre hier genauso falsch wie in der
        /// Gegenrichtung, wo wir es der Gegenstelle verbieten - und der
        /// Abschnitt sagt ausdrücklich, dass die Bidi-Rückrichtung diese
        /// Prüfung nicht überspringen darf.
        /// </remarks>
        [Test]
        public async Task TheReturnPath_CarriesOnlyOurOwnDomain()
        {

            var stream = S2SStream.Accept("rechts.example",
                                          Senden,
                                          (_, _) => Task.FromResult(RemoteStanzaResult.Accepted),
                                          verifyKey:  (_, _, _) => Task.FromResult(true),
                                          offerBidi:  true);

            await stream.ProcessFrameAsync(OpenVon("links.example", id: "s1"));
            await stream.ProcessFrameAsync($"<bidi xmlns='{S2SStream.BidiNamespace}'/>");

            // Dialback abschliessen, damit nur noch die Absenderprüfung im Weg
            // stehen kann.
            await stream.ProcessFrameAsync(
                      $"<db:result xmlns:db='{DialbackKey.Namespace}' " +
                      "from='links.example' to='rechts.example'>egal</db:result>");

            Assert.That(stream.IsAuthenticated, Is.True, "Aufbau des Tests: ausgewiesen.");

            var eigene = await stream.SendStanzaOverBidiAsync(
                             "<message from='juliet@rechts.example' to='romeo@links.example'/>");

            var fremde = await stream.SendStanzaOverBidiAsync(
                             "<message from='eve@woanders.example' to='romeo@links.example'/>");

            Assert.Multiple(() =>
            {
                Assert.That(eigene, Is.True,  "Die eigene Domain muss durchgehen.");
                Assert.That(fremde, Is.False, "Eine fremde Domain nicht.");
            });

        }

        #endregion

    }

}
