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

using System.Text;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die S2S-Protokollschicht, geprüft ohne Transport darunter.
    /// </summary>
    /// <remarks>
    /// Kein Socket, kein Server, keine Zeitfenster: die Rahmen gehen in eine
    /// Liste und kommen aus einer Zeichenkette. Genau darum geht es - was
    /// TCP und WebSocket gemeinsam haben, soll auch gemeinsam geprüft sein und
    /// nicht zweimal über den Umweg eines Transports.
    ///
    /// Die Handshakes werden von Hand gebaut statt über eine zweite
    /// <see cref="S2SStream"/>-Instanz. Liessen sich beide Rollen gegeneinander
    /// laufen, prüfte der Test nur, dass die Klasse zu sich selbst passt -
    /// ein Fehler in ihrer Vorstellung von RFC 7395 bliebe unsichtbar.
    /// </remarks>
    [TestFixture]
    public class S2SStreamTests
    {

        #region Data

        private List<String> _gesendet = null!;

        #endregion

        #region SetUp

        [SetUp]
        public void Leeren()
        {
            _gesendet = [];
        }

        #endregion

        #region Hilfsfunktionen

        private Task Senden(String frame, CancellationToken _)
        {
            _gesendet.Add(frame);
            return Task.CompletedTask;
        }

        /// <summary>Ein eingehender Stream, der alles annimmt.</summary>
        private S2SStream Eingehend(List<String>? zugestellt = null)

            => S2SStream.Accept(
                   "rechts.example",
                   Senden,
                   (peer, stanza) =>
                   {
                       zugestellt?.Add(stanza);
                       return Task.FromResult(RemoteStanzaResult.Accepted);
                   });

        /// <summary>Ein eingehender Stream, der mit einem festen Urteil antwortet.</summary>
        private S2SStream EingehendMit(RemoteStanzaResult urteil)

            => S2SStream.Accept(
                   "rechts.example",
                   Senden,
                   (_, _) => Task.FromResult(urteil));

        private static String OpenVon(String from, String? to = "rechts.example", String? id = null)

            => $"<open xmlns='urn:ietf:params:xml:ns:xmpp-framing' from='{from}'" +
               (to is not null ? $" to='{to}'" : "") +
               (id is not null ? $" id='{id}'" : "") +
               " version='1.0'/>";

        private Boolean Gesendet(String enthaelt)
            => _gesendet.Any(f => f.Contains(enthaelt, StringComparison.Ordinal));

        #endregion


        #region TheInitiatorSendsItsDomainInTheStreamHeader()

        /// <summary>
        /// Der Stream-Kopf des Initiators nennt beide Domains (RFC 7395,
        /// Abschnitt 3.4).
        /// </summary>
        [Test]
        public async Task TheInitiatorSendsItsDomainInTheStreamHeader()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden);

            await stream.OpenAsync();

            Assert.Multiple(() =>
            {
                Assert.That(_gesendet, Has.Count.EqualTo(1));
                Assert.That(_gesendet[0], Does.Contain("urn:ietf:params:xml:ns:xmpp-framing"));
                Assert.That(_gesendet[0], Does.Contain("from='links.example'"));
                Assert.That(_gesendet[0], Does.Contain("to='rechts.example'"));
            });

        }

        #endregion

        #region TheResponderAnswersWithAStreamIdAndFeatures()

        /// <summary>
        /// Der Empfänger vergibt die Stream-ID (RFC 7395, Abschnitt 3.4) und
        /// schickt seine Features (RFC 6120, Abschnitt 4.3.2).
        /// </summary>
        /// <remarks>
        /// An der Stream-ID hängt später Dialback - sie ist nicht Beiwerk,
        /// sondern der Anker der einzigen Prüfung, die die Domain der
        /// Gegenstelle belegen kann.
        /// </remarks>
        [Test]
        public async Task TheResponderAnswersWithAStreamIdAndFeatures()
        {

            var stream = Eingehend();

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            Assert.Multiple(() =>
            {
                Assert.That(stream.IsOpen,       Is.True);
                Assert.That(stream.RemoteDomain, Is.EqualTo("links.example"));
                Assert.That(stream.StreamId,     Is.Not.Null.And.Not.Empty);
                Assert.That(Gesendet($"id='{stream.StreamId}'"), Is.True,
                            "Die vergebene Kennung muss auch hinausgehen.");
                Assert.That(Gesendet("stream:features"), Is.True);
            });

        }

        #endregion

        #region AStreamHeaderForAnotherHost_IsRefused()

        /// <summary>
        /// Ein <c>to</c>, das dieser Server nicht bedient, ist
        /// <c>&lt;host-unknown/&gt;</c> (RFC 6120, Abschnitt 4.9.3.6).
        /// </summary>
        [Test]
        public async Task AStreamHeaderForAnotherHost_IsRefused()
        {

            var stream = Eingehend();

            await stream.ProcessFrameAsync(OpenVon("links.example", to: "ganzwoanders.example"));

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet("host-unknown"), Is.True);
                Assert.That(stream.IsOpen,            Is.False);
                Assert.That(stream.IsClosed,          Is.True);
            });

        }

        #endregion

        #region AStreamHeaderWithoutFrom_IsRefused()

        /// <summary>
        /// Ohne <c>from</c> hätte die Absenderprüfung nichts, woran sie sich
        /// halten könnte - dann gibt es gar keinen Stream.
        /// </summary>
        [Test]
        public async Task AStreamHeaderWithoutFrom_IsRefused()
        {

            var stream = Eingehend();

            await stream.ProcessFrameAsync(
                      "<open xmlns='urn:ietf:params:xml:ns:xmpp-framing' to='rechts.example' version='1.0'/>");

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet("improper-addressing"), Is.True);
                Assert.That(stream.IsOpen,                   Is.False);
                Assert.That(stream.RemoteDomain,             Is.Null);
            });

        }

        #endregion

        #region TheInitiatorRefusesAnAnswerFromAnotherDomain()

        /// <summary>
        /// Wer sich als jemand anders meldet, als angewählt wurde, bekommt
        /// keinen Stream.
        /// </summary>
        /// <remarks>
        /// Ohne diese Prüfung wäre die Adresse der Gegenstelle das einzige,
        /// worauf der Initiator sich verlässt - und die kommt aus einer
        /// Konfigurationsdatei oder später aus dem DNS.
        /// </remarks>
        [Test]
        public async Task TheInitiatorRefusesAnAnswerFromAnotherDomain()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden);

            await stream.OpenAsync();
            await stream.ProcessFrameAsync(OpenVon("boese.example", to: "links.example", id: "abc"));

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet("invalid-from"), Is.True);
                Assert.That(stream.IsOpen,            Is.False);
                Assert.That(stream.IsClosed,          Is.True);
            });

        }

        #endregion

        #region TheInitiatorTakesTheStreamIdFromTheAnswer()

        /// <summary>
        /// Die Kennung vergibt der Empfänger; der Initiator übernimmt sie.
        /// </summary>
        [Test]
        public async Task TheInitiatorTakesTheStreamIdFromTheAnswer()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden);

            await stream.OpenAsync();
            await stream.ProcessFrameAsync(OpenVon("rechts.example", to: "links.example", id: "s-4711"));

            Assert.Multiple(() =>
            {
                Assert.That(stream.IsOpen,   Is.True);
                Assert.That(stream.StreamId, Is.EqualTo("s-4711"));
            });

        }

        #endregion

        #region AStanzaBeforeTheStreamHeader_EndsTheStream()

        /// <summary>
        /// Vor dem <c>&lt;open/&gt;</c> gibt es keine Stanzas.
        /// </summary>
        [Test]
        public async Task AStanzaBeforeTheStreamHeader_EndsTheStream()
        {

            var zugestellt = new List<String>();
            var stream     = Eingehend(zugestellt);

            await stream.ProcessFrameAsync(
                      "<message from='alice@links.example' to='bob@rechts.example'><body>Hallo</body></message>");

            Assert.Multiple(() =>
            {
                Assert.That(zugestellt,    Is.Empty, "Ohne Stream darf nichts zugestellt werden.");
                Assert.That(stream.IsClosed, Is.True);
            });

        }

        #endregion

        #region AnAcceptedStanza_ReachesTheRouting()

        /// <summary>
        /// Der Normalfall: nach dem Handshake geht die Stanza samt der Domain
        /// der Gegenstelle ans Routing.
        /// </summary>
        [Test]
        public async Task AnAcceptedStanza_ReachesTheRouting()
        {

            var zugestellt = new List<String>();
            var stream     = Eingehend(zugestellt);

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            var verstanden = await stream.ProcessFrameAsync(
                                 "<message from='alice@links.example' to='bob@rechts.example'><body>Hallo</body></message>");

            Assert.Multiple(() =>
            {
                Assert.That(verstanden, Is.True);
                Assert.That(zugestellt, Has.Count.EqualTo(1));
                Assert.That(zugestellt[0], Does.Contain("Hallo"));
            });

        }

        #endregion

        #region AForeignSender_EndsTheStream()

        /// <summary>
        /// RFC 6120, Abschnitt 8.1.1.1: ein <c>from</c>, für das die
        /// Gegenstelle nicht sprechen darf, beendet den Stream mit
        /// <c>&lt;invalid-from/&gt;</c>.
        /// </summary>
        /// <remarks>
        /// Das ist der eine Punkt, an dem der echte Transport mehr kann als
        /// <see cref="DirectServerLinks"/>: dort wurde die Stanza verworfen und
        /// die Gegenstelle konnte es beliebig oft wieder versuchen. Hier ist
        /// danach die Verbindung zu.
        /// </remarks>
        [Test]
        public async Task AForeignSender_EndsTheStream()
        {

            var stream = EingehendMit(RemoteStanzaResult.ForeignSender);

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            var abgelehnt = new List<String>();
            stream.OnStanzaRefused += grund => abgelehnt.Add(grund);

            await stream.ProcessFrameAsync(
                      "<message from='chef@bank.example' to='bob@rechts.example'><body>Überweisen Sie.</body></message>");

            Assert.Multiple(() =>
            {
                Assert.That(abgelehnt,               Is.Not.Empty);
                Assert.That(Gesendet("invalid-from"), Is.True);
                Assert.That(stream.IsClosed,          Is.True);
            });

        }

        #endregion

        #region AMalformedSender_EndsTheStreamAsWell()

        /// <summary>
        /// Ein <c>from</c>, das überhaupt kein JID ist, beendet den Stream
        /// ebenso.
        /// </summary>
        /// <remarks>
        /// RFC 6120, Abschnitt 8.1.1.1 unterscheidet die beiden Fälle nicht:
        /// Ein <c>from</c>, das die Gegenstelle nicht führen darf, und eines,
        /// das keine Adresse ist, sind beide „invalid". Und der Grund, aus dem
        /// der erste den Stream beendet, trägt hier genauso — wer einmal etwas
        /// schickt, das keine Adresse hat, tut es beim nächsten Versuch wieder.
        ///
        /// Ohne diesen Test käme das neue Urteil aus D53 im Stream nirgends an:
        /// Er reicht alles, was nicht <c>Accepted</c> ist, als verworfene
        /// Stanza weiter, und die Verbindung bliebe offen.
        /// </remarks>
        [Test]
        public async Task AMalformedSender_EndsTheStreamAsWell()
        {

            var stream = EingehendMit(RemoteStanzaResult.MalformedSender);

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            var abgelehnt = new List<String>();
            stream.OnStanzaRefused += grund => abgelehnt.Add(grund);

            await stream.ProcessFrameAsync(
                      "<message from='al ice@links.example' to='bob@rechts.example'><body>Hallo</body></message>");

            Assert.Multiple(() =>
            {
                Assert.That(abgelehnt,                Is.Not.Empty);
                Assert.That(Gesendet("invalid-from"),  Is.True);
                Assert.That(stream.IsClosed,           Is.True);
            });

        }

        #endregion

        #region AMalformedRecipient_DropsOnlyThatStanza()

        /// <summary>
        /// Ein unmöglicher <b>Empfänger</b> kostet dagegen nur die eine Stanza.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe zum vorigen Test, und sie zieht die Grenze, um die es
        /// geht: Beim Absender steht die Frage im Raum, wer da spricht — beim
        /// Empfänger ein Tippfehler in einer Adresse. Risse der die Föderation
        /// ab, wäre die Prüfung schlimmer als ihr Nutzen.
        /// </remarks>
        [Test]
        public async Task AMalformedRecipient_DropsOnlyThatStanza()
        {

            var stream = EingehendMit(RemoteStanzaResult.MalformedRecipient);

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            var abgelehnt = new List<String>();
            stream.OnStanzaRefused += grund => abgelehnt.Add(grund);

            await stream.ProcessFrameAsync(
                      "<message from='alice@links.example' to='b ob@rechts.example'><body>Hallo</body></message>");

            Assert.Multiple(() =>
            {
                Assert.That(abgelehnt,                Is.Not.Empty);
                Assert.That(Gesendet("invalid-from"),  Is.False, "Nur die Stanza ist falsch, nicht der Stream.");
                Assert.That(stream.IsClosed,           Is.False);
                Assert.That(stream.IsOpen,             Is.True);
            });

        }

        #endregion

        #region AForeignRecipient_DropsOnlyThatStanza()

        /// <summary>
        /// Die Gegenprobe: eine Stanza an eine dritte Domain wird verworfen,
        /// aber der Stream bleibt.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test bestünde der vorige auch dann, wenn jede Ablehnung
        /// den Stream beendete - und ein einziger Tippfehler in einem
        /// <c>to</c> risse die Föderation ab.
        /// </remarks>
        [Test]
        public async Task AForeignRecipient_DropsOnlyThatStanza()
        {

            var stream = EingehendMit(RemoteStanzaResult.ForeignRecipient);

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            var abgelehnt = new List<String>();
            stream.OnStanzaRefused += grund => abgelehnt.Add(grund);

            await stream.ProcessFrameAsync(
                      "<message from='alice@links.example' to='wer@ganzwoanders.example'><body>Weiter</body></message>");

            Assert.Multiple(() =>
            {
                Assert.That(abgelehnt,                Is.Not.Empty);
                Assert.That(Gesendet("invalid-from"),  Is.False, "Nur die Stanza ist falsch, nicht der Stream.");
                Assert.That(stream.IsClosed,           Is.False);
                Assert.That(stream.IsOpen,             Is.True);
            });

        }

        #endregion

        #region AnOutgoingStream_TakesNoStanzas()

        /// <summary>
        /// RFC 6120, Abschnitt 4.1: ein Stream trägt in eine Richtung. Was auf
        /// dem ausgehenden ankommt, wird gemeldet und verworfen.
        /// </summary>
        /// <remarks>
        /// Beides über eine Verbindung zu führen wäre XEP-0288 und müsste
        /// ausgehandelt werden. Ohne diese Grenze wäre unklar, für welche
        /// Domain die Gegenstelle auf welchem Stream sprechen darf - und
        /// genau daran hängt die Absenderprüfung.
        /// </remarks>
        [Test]
        public async Task AnOutgoingStream_TakesNoStanzas()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden);

            await stream.OpenAsync();
            await stream.ProcessFrameAsync(OpenVon("rechts.example", to: "links.example", id: "s-1"));

            var abgelehnt = new List<String>();
            stream.OnStanzaRefused += grund => abgelehnt.Add(grund);

            var verstanden = await stream.ProcessFrameAsync(
                                 "<message from='bob@rechts.example' to='alice@links.example'><body>Antwort</body></message>");

            Assert.Multiple(() =>
            {
                Assert.That(verstanden, Is.False);
                Assert.That(abgelehnt,  Is.Not.Empty);
            });

        }

        #endregion

        #region SendingBeforeTheHandshake_IsRefused()

        /// <summary>
        /// Bevor der Handshake steht, geht keine Stanza hinaus - sie ginge
        /// sonst an eine Gegenstelle, die sich noch nicht gemeldet hat.
        /// </summary>
        [Test]
        public async Task SendingBeforeTheHandshake_IsRefused()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden);

            await stream.OpenAsync();

            var gesendet = await stream.SendStanzaAsync(
                               "<message from='alice@links.example' to='bob@rechts.example'/>");

            Assert.Multiple(() =>
            {
                Assert.That(gesendet,  Is.False);
                Assert.That(_gesendet, Has.Count.EqualTo(1), "Nur der Stream-Kopf.");
            });

        }

        #endregion

        #region AClosedStream_TakesNothingMore()

        /// <summary>
        /// Nach dem <c>&lt;close/&gt;</c> der Gegenstelle ist Schluss.
        /// </summary>
        [Test]
        public async Task AClosedStream_TakesNothingMore()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden);

            await stream.OpenAsync();
            await stream.ProcessFrameAsync(OpenVon("rechts.example", to: "links.example", id: "s-1"));

            var grund   = "noch nicht beendet";
            stream.OnClosed += r => grund = r ?? "(ordentlich)";

            await stream.ProcessFrameAsync("<close xmlns='urn:ietf:params:xml:ns:xmpp-framing'/>");

            var gesendet = await stream.SendStanzaAsync(
                               "<message from='alice@links.example' to='bob@rechts.example'/>");

            Assert.Multiple(() =>
            {
                Assert.That(stream.IsClosed, Is.True);
                Assert.That(grund,           Is.EqualTo("(ordentlich)"));
                Assert.That(gesendet,        Is.False);
            });

        }

        #endregion

        #region TheSameLayerAlsoSpeaksTcpFraming()

        /// <summary>
        /// Dieselbe Protokollschicht, andere Rahmung: über TCP heisst der
        /// Stream-Kopf <c>&lt;stream:stream&gt;</c> und ist ein offenes Tag
        /// (RFC 6120, Abschnitt 4.7).
        /// </summary>
        /// <remarks>
        /// Der Nachweis zu S4b-1. Was sich unterscheidet, ist ausschliesslich
        /// die Rahmung; Handshake-Ablauf, Stream-ID, Absenderprüfung und
        /// Zustellung laufen unverändert. Genau deshalb steht dieser Test hier
        /// bei der Protokollschicht und nicht beim Transport - er kommt ohne
        /// Socket aus.
        /// </remarks>
        [Test]
        public async Task TheSameLayerAlsoSpeaksTcpFraming()
        {

            var zugestellt = new List<String>();

            var stream = S2SStream.Accept(
                             "rechts.example",
                             Senden,
                             (peer, stanza) =>
                             {
                                 zugestellt.Add(stanza);
                                 return Task.FromResult(RemoteStanzaResult.Accepted);
                             },
                             framing: TcpStreamFraming.Instance);

            await stream.ProcessFrameAsync(
                      "<stream:stream xmlns='jabber:server' " +
                      "xmlns:stream='http://etherx.jabber.org/streams' " +
                      "from='links.example' to='rechts.example' version='1.0'>");

            await stream.ProcessFrameAsync(
                      "<message from='alice@links.example' to='bob@rechts.example'><body>Hallo</body></message>");

            Assert.Multiple(() =>
            {
                Assert.That(stream.IsOpen,        Is.True);
                Assert.That(stream.RemoteDomain,  Is.EqualTo("links.example"));
                Assert.That(stream.StreamId,      Is.Not.Null.And.Not.Empty);
                Assert.That(zugestellt,           Has.Count.EqualTo(1));

                // Die Antwort trägt die TCP-Rahmung, nicht die von RFC 7395.
                Assert.That(Gesendet("<stream:stream"), Is.True);
                Assert.That(Gesendet("jabber:server"),  Is.True);
                Assert.That(Gesendet("<open "),         Is.False);
            });

        }

        #endregion

        #region TcpFramingClosesWithTheRootElement()

        /// <summary>
        /// Über TCP endet der Stream mit <c>&lt;/stream:stream&gt;</c>, nicht
        /// mit <c>&lt;close/&gt;</c>.
        /// </summary>
        [Test]
        public async Task TcpFramingClosesWithTheRootElement()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden,
                                            framing: TcpStreamFraming.Instance);

            await stream.OpenAsync();
            await stream.ProcessFrameAsync(
                      "<stream:stream xmlns='jabber:server' " +
                      "xmlns:stream='http://etherx.jabber.org/streams' " +
                      "from='rechts.example' to='links.example' id='s-9' version='1.0'>");

            Assert.That(stream.StreamId, Is.EqualTo("s-9"));

            await stream.CloseAsync();

            Assert.Multiple(() =>
            {
                Assert.That(_gesendet[0], Does.StartWith("<stream:stream"));
                Assert.That(_gesendet[0], Does.Not.Contain("/>"),
                            "Der Stream-Kopf ist ein offenes Tag.");
                Assert.That(_gesendet[^1], Is.EqualTo("</stream:stream>"));
                Assert.That(stream.IsClosed, Is.True);
            });

        }

        #endregion

        #region TcpFramingCarriesDialbackThroughUnchanged()

        /// <summary>
        /// Auch Dialback läuft über die TCP-Rahmung unverändert - der
        /// Schlüssel hängt an der Stream-ID, und die gibt es in beiden
        /// Rahmungen.
        /// </summary>
        /// <remarks>
        /// Das war die offene Frage aus dem Arbeitsplan: Dialback ist über
        /// XML-Streams definiert, und die WebSocket-Abbildung war eine eigene
        /// Festlegung. Hier zeigt sich, dass die Schicht darüber davon nichts
        /// mitbekommt.
        /// </remarks>
        [Test]
        public async Task TcpFramingCarriesDialbackThroughUnchanged()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden,
                                            secret:  "s3cr3tf0rd14lb4ck",
                                            framing: TcpStreamFraming.Instance);

            await stream.OpenAsync();
            await stream.ProcessFrameAsync(
                      "<stream:stream xmlns='jabber:server' " +
                      "xmlns:stream='http://etherx.jabber.org/streams' " +
                      "xmlns:db='jabber:server:dialback' " +
                      "from='rechts.example' to='links.example' id='D60000229F' version='1.0'>");

            // Derselbe Vektor wie in DialbackKeyTests, nur mit vertauschten
            // Beispiel-Domains: Ziel ist hier rechts.example.
            var erwartet = DialbackKey.Generate("s3cr3tf0rd14lb4ck",
                                                "rechts.example", "links.example", "D60000229F");

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet("<db:result"), Is.True);
                Assert.That(Gesendet(erwartet),     Is.True);
                Assert.That(stream.IsAuthenticated, Is.False, "Noch fehlt die Bestätigung.");
            });

            await stream.ProcessFrameAsync(
                      "<db:result from='rechts.example' to='links.example' type='valid'/>");

            Assert.That(stream.IsAuthenticated, Is.True);

        }

        #endregion

        #region ExternalIsOfferedOnlyWhenACertificateCanBeChecked()

        /// <summary>
        /// SASL-EXTERNAL wird nur angeboten, wenn es auch etwas zu pruefen
        /// gibt.
        /// </summary>
        /// <remarks>
        /// Ein Angebot ohne pruefbares Zertifikat waere eine Einladung in eine
        /// Sackgasse: die Gegenstelle schickte ihr <c>&lt;auth/&gt;</c> und
        /// bekaeme unweigerlich ein <c>&lt;failure/&gt;</c>.
        /// </remarks>
        [Test]
        public async Task ExternalIsOfferedOnlyWhenACertificateCanBeChecked()
        {

            var ohne = Eingehend();
            await ohne.ProcessFrameAsync(OpenVon("links.example"));

            Assert.That(Gesendet("EXTERNAL"), Is.False,
                        "Ohne Zertifikat darf EXTERNAL nicht angeboten werden.");

            _gesendet.Clear();

            var mit = S2SStream.Accept("rechts.example", Senden,
                                       (_, _) => Task.FromResult(RemoteStanzaResult.Accepted),
                                       externalIdentity: _ => true);

            await mit.ProcessFrameAsync(OpenVon("links.example"));

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet("EXTERNAL"), Is.True);
                Assert.That(Gesendet("urn:ietf:params:xml:ns:xmpp-sasl"), Is.True);
            });

        }

        #endregion

        #region AMatchingCertificate_AuthenticatesTheStream()

        /// <summary>
        /// Der Normalfall: das Zertifikat deckt die Domain, der Stream ist
        /// ausgewiesen - ohne Dialback und ohne zweite Verbindung.
        /// </summary>
        [Test]
        public async Task AMatchingCertificate_AuthenticatesTheStream()
        {

            var geprueft = new List<String>();

            var stream = S2SStream.Accept("rechts.example", Senden,
                                          (_, _) => Task.FromResult(RemoteStanzaResult.Accepted),
                                          externalIdentity: d => { geprueft.Add(d); return true; });

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            var authzid = Convert.ToBase64String(Encoding.UTF8.GetBytes("links.example"));

            await stream.ProcessFrameAsync(
                      $"<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='EXTERNAL'>{authzid}</auth>");

            Assert.Multiple(() =>
            {
                Assert.That(geprueft,                 Is.EquivalentTo(new[] { "links.example" }));
                Assert.That(Gesendet("<success"),     Is.True);
                Assert.That(stream.IsAuthenticated,   Is.True);
                Assert.That(stream.AuthenticatedBy,   Is.EqualTo("SASL-EXTERNAL"));

                // RFC 6120, Abschnitt 6.4.6: der Stream faengt von vorn an.
                Assert.That(stream.IsOpen, Is.False, "Nach <success/> wird der Stream neu geoeffnet.");
            });

        }

        #endregion

        #region ACertificateThatDoesNotCoverTheDomain_IsRefused()

        /// <summary>
        /// Deckt das Zertifikat die Domain nicht, gibt es
        /// <c>&lt;not-authorized/&gt;</c> - und keinen ausgewiesenen Stream.
        /// </summary>
        /// <remarks>
        /// Die eine Zeile, an der SASL-EXTERNAL haengt. Fiele sie weg, waere
        /// das Verfahren nur eine hoefliche Bitte um Selbstauskunft.
        /// </remarks>
        [Test]
        public async Task ACertificateThatDoesNotCoverTheDomain_IsRefused()
        {

            var stream = S2SStream.Accept("rechts.example", Senden,
                                          (_, _) => Task.FromResult(RemoteStanzaResult.Accepted),
                                          externalIdentity: _ => false);

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            var authzid = Convert.ToBase64String(Encoding.UTF8.GetBytes("links.example"));

            await stream.ProcessFrameAsync(
                      $"<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='EXTERNAL'>{authzid}</auth>");

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet("not-authorized"), Is.True);
                Assert.That(Gesendet("<success"),       Is.False);
                Assert.That(stream.IsAuthenticated,     Is.False);
            });

        }

        #endregion

        #region ClaimingADifferentDomainThanTheStreamHeader_IsRefused()

        /// <summary>
        /// Die authzid muss zu dem passen, was der Stream-Kopf genannt hat.
        /// </summary>
        /// <remarks>
        /// Ohne diese Pruefung liesse sich ein Stream nachtraeglich auf eine
        /// zweite Identitaet umschreiben: Kopf fuer die eine Domain, Ausweis
        /// fuer die andere. Der Test gibt bewusst ein Zertifikat mit, das
        /// <b>alles</b> deckt - abgelehnt wird hier nicht wegen des
        /// Zertifikats, sondern wegen des Widerspruchs.
        /// </remarks>
        [Test]
        public async Task ClaimingADifferentDomainThanTheStreamHeader_IsRefused()
        {

            var stream = S2SStream.Accept("rechts.example", Senden,
                                          (_, _) => Task.FromResult(RemoteStanzaResult.Accepted),
                                          externalIdentity: _ => true);

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            var authzid = Convert.ToBase64String(Encoding.UTF8.GetBytes("bank.example"));

            await stream.ProcessFrameAsync(
                      $"<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='EXTERNAL'>{authzid}</auth>");

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet("not-authorized"), Is.True);
                Assert.That(stream.IsAuthenticated,     Is.False);
            });

        }

        #endregion

        #region AnEmptyAuthzid_MeansTheStreamHeaderDomain()

        /// <summary>
        /// Eine leere authzid (<c>=</c>) heisst "nimm die Identitaet aus dem
        /// Zertifikat" (RFC 6120, Abschnitt 6.4.2).
        /// </summary>
        [Test]
        public async Task AnEmptyAuthzid_MeansTheStreamHeaderDomain()
        {

            var geprueft = new List<String>();

            var stream = S2SStream.Accept("rechts.example", Senden,
                                          (_, _) => Task.FromResult(RemoteStanzaResult.Accepted),
                                          externalIdentity: d => { geprueft.Add(d); return true; });

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            await stream.ProcessFrameAsync(
                      "<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='EXTERNAL'>=</auth>");

            Assert.Multiple(() =>
            {
                Assert.That(geprueft,               Is.EquivalentTo(new[] { "links.example" }));
                Assert.That(stream.IsAuthenticated, Is.True);
            });

        }

        #endregion

        #region AnUnofferedMechanism_IsRefused()

        /// <summary>
        /// Ein anderer Mechanismus als EXTERNAL wird abgelehnt.
        /// </summary>
        [Test]
        public async Task AnUnofferedMechanism_IsRefused()
        {

            var stream = S2SStream.Accept("rechts.example", Senden,
                                          (_, _) => Task.FromResult(RemoteStanzaResult.Accepted),
                                          externalIdentity: _ => true);

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            await stream.ProcessFrameAsync(
                      "<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='PLAIN'>eA==</auth>");

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet("invalid-mechanism"), Is.True);
                Assert.That(stream.IsAuthenticated,        Is.False);
            });

        }

        #endregion

        #region NoStanzasBeforeExternalHasSucceeded()

        /// <summary>
        /// Auch mit SASL-EXTERNAL gilt: vor dem Ausweis keine Stanza.
        /// </summary>
        [Test]
        public async Task NoStanzasBeforeExternalHasSucceeded()
        {

            var zugestellt = new List<String>();

            var stream = S2SStream.Accept("rechts.example", Senden,
                                          (_, stanza) =>
                                          {
                                              zugestellt.Add(stanza);
                                              return Task.FromResult(RemoteStanzaResult.Accepted);
                                          },
                                          verifyKey: (_, _, _) => Task.FromResult(false),
                                          externalIdentity: _ => true);

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            await stream.ProcessFrameAsync(
                      "<message from='alice@links.example' to='bob@rechts.example'><body>Hallo</body></message>");

            Assert.That(zugestellt, Is.Empty);

        }

        #endregion

        #region TheInitiatorRestartsTheStreamAfterSuccess()

        /// <summary>
        /// Auf der aufbauenden Seite: nach <c>&lt;success/&gt;</c> geht ein
        /// neuer Stream-Kopf hinaus (RFC 6120, Abschnitt 6.4.6).
        /// </summary>
        [Test]
        public async Task TheInitiatorRestartsTheStreamAfterSuccess()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden,
                                            canOfferExternal: true);

            await stream.OpenAsync();
            await stream.ProcessFrameAsync(OpenVon("rechts.example", to: "links.example", id: "s-1"));

            await stream.ProcessFrameAsync(
                      "<stream:features xmlns:stream='http://etherx.jabber.org/streams'>" +
                      "<mechanisms xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>" +
                      "<mechanism>EXTERNAL</mechanism></mechanisms></stream:features>");

            Assert.That(Gesendet("mechanism='EXTERNAL'"), Is.True,
                        "Auf das Angebot muss ein <auth/> folgen.");

            var vorher = _gesendet.Count;

            await stream.ProcessFrameAsync("<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'/>");

            Assert.Multiple(() =>
            {
                Assert.That(stream.IsAuthenticated, Is.True);
                Assert.That(_gesendet, Has.Count.GreaterThan(vorher));
                Assert.That(_gesendet[^1], Does.StartWith("<open"),
                            "Nach dem Erfolg faengt der Stream von vorn an.");
            });

        }

        #endregion

        #region ARefusedExternal_DoesNotFallBackToDialback()

        /// <summary>
        /// Nach <c>&lt;failure/&gt;</c> gibt es keinen zweiten Anlauf mit dem
        /// schwaecheren Verfahren.
        /// </summary>
        /// <remarks>
        /// Eine Festlegung, keine Auslassung: wer sich per Zertifikat
        /// ausweisen wollte und abgelehnt wurde, hat ein Problem, das Dialback
        /// nicht loest, sondern verdeckt.
        /// </remarks>
        [Test]
        public async Task ARefusedExternal_DoesNotFallBackToDialback()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden,
                                            secret: "s3cr3tf0rd14lb4ck",
                                            canOfferExternal: true);

            await stream.OpenAsync();
            await stream.ProcessFrameAsync(OpenVon("rechts.example", to: "links.example", id: "s-1"));
            await stream.ProcessFrameAsync(
                      "<stream:features xmlns:stream='http://etherx.jabber.org/streams'>" +
                      "<mechanisms xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>" +
                      "<mechanism>EXTERNAL</mechanism></mechanisms></stream:features>");

            await stream.ProcessFrameAsync(
                      "<failure xmlns='urn:ietf:params:xml:ns:xmpp-sasl'><not-authorized/></failure>");

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet("<db:result"),   Is.False, "Kein Rueckfall auf Dialback.");
                Assert.That(stream.IsAuthenticated,   Is.False);
                Assert.That(stream.IsClosed,          Is.True);
            });

        }

        #endregion

        #region WithoutExternalOnOffer_TheInitiatorUsesDialback()

        /// <summary>
        /// Bietet die Gegenstelle kein EXTERNAL an, geht es mit Dialback
        /// weiter.
        /// </summary>
        [Test]
        public async Task WithoutExternalOnOffer_TheInitiatorUsesDialback()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden,
                                            secret: "s3cr3tf0rd14lb4ck",
                                            canOfferExternal: true);

            await stream.OpenAsync();
            await stream.ProcessFrameAsync(OpenVon("rechts.example", to: "links.example", id: "s-1"));
            await stream.ProcessFrameAsync(
                      "<stream:features xmlns:stream='http://etherx.jabber.org/streams'/>");

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet("<db:result"),           Is.True);
                Assert.That(Gesendet("mechanism='EXTERNAL'"), Is.False);
            });

        }

        #endregion

        #region WaitingForAStreamThatNeverOpens_GivesUp()

        /// <summary>
        /// Endet der Stream, bevor der Handshake steht, wartet niemand ins
        /// Zeitlimit.
        /// </summary>
        /// <remarks>
        /// Sonst hinge jede Zustellung an eine Domain, deren Server das
        /// <c>&lt;open/&gt;</c> mit einem Fehler beantwortet, bis zum
        /// Verbindungs-Timeout - und der Absender bekäme seinen
        /// <c>&lt;remote-server-not-found/&gt;</c> erst danach.
        /// </remarks>
        [Test]
        public async Task WaitingForAStreamThatNeverOpens_GivesUp()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden);

            await stream.OpenAsync();

            var warten = stream.WaitUntilOpenAsync(TimeSpan.FromSeconds(30));

            await stream.ProcessFrameAsync(
                      "<stream:error xmlns:stream='http://etherx.jabber.org/streams'>" +
                      "<host-unknown xmlns='urn:ietf:params:xml:ns:xmpp-streams'/></stream:error>");

            Assert.That(await warten.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);

        }

        #endregion

        #region AnUnknownElement_EndsTheStream()

        /// <summary>
        /// RFC 6120, Abschnitt 4.9.3.24: Ein Element, das dieser Server auf
        /// oberster Ebene nicht kennt, beendet den Stream mit
        /// <c>&lt;unsupported-stanza-type/&gt;</c>.
        /// </summary>
        /// <remarks>
        /// Die Client-Verbindung hält diese Regel seit D26; hier blieb ein
        /// unbekanntes Element liegen. Das war <b>nicht</b> Nachlässigkeit,
        /// sondern eine offen vermerkte Lücke: Ungemessen war, was Prosody und
        /// ejabberd auf einem S2S-Stream tatsächlich schicken — und einen
        /// Stream abzubrechen, weil man ein Element nicht kennt, wäre gegenüber
        /// einer fremden Implementierung eine Wette gewesen.
        ///
        /// Gemessen ist es jetzt: über den vollen Lauf gegen beide
        /// Gegenstellen, in beide Richtungen, fiel kein einziger Rahmen durch
        /// diese Weiche.
        ///
        /// Die drei Fälle sind dieselben wie beim Client — ein erfundenes
        /// Element und zwei, die nur mit dem Namen einer Stanza
        /// <b>beginnen</b>.
        /// </remarks>
        [Test]
        [TestCase("<quatsch xmlns='urn:example:nein'/>")]
        [TestCase("<iqbogus/>")]
        [TestCase("<messages/>")]
        public async Task AnUnknownElement_EndsTheStream(String rahmen)
        {

            var stream = Eingehend();

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            _gesendet.Clear();

            var behandelt = await stream.ProcessFrameAsync(rahmen);

            Assert.Multiple(() =>
            {

                Assert.That(Gesendet("unsupported-stanza-type"), Is.True,
                            "Der Grund muss über die Leitung gehen.");

                Assert.That(stream.IsClosed, Is.True,
                            "Ein Stream-Fehler ist unwiederbringlich (Abschnitt 4.9.1.1).");

                Assert.That(behandelt, Is.True,
                            "Behandelt ist er - mit einem Fehler und nicht mit Schweigen.");

            });

        }

        #endregion

        #region AnAbort_IsAnsweredWithAborted()

        /// <summary>
        /// RFC 6120, Abschnitt 6.4.4 gilt auch hier: Bricht die Gegenstelle
        /// die SASL-Aushandlung ab, folgt
        /// <c>&lt;failure&gt;&lt;aborted/&gt;&lt;/failure&gt;</c> und kein
        /// Stream-Fehler.
        /// </summary>
        /// <remarks>
        /// Diese Lücke ist in D27 entstanden und nicht vorgefunden: Vorher
        /// blieb ein <c>&lt;abort/&gt;</c> hier schlicht liegen, seit der
        /// Strenge beendete es den Stream. Wer eine Weiche streng macht, erbt
        /// jede Antwort, die sie noch nicht kennt — und muss sie nachreichen,
        /// nicht abwarten, bis jemand darüber stolpert.
        /// </remarks>
        [Test]
        public async Task AnAbort_IsAnsweredWithAborted()
        {

            var stream = Eingehend();

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            _gesendet.Clear();

            var behandelt = await stream.ProcessFrameAsync(
                                $"<abort xmlns='{S2SStream.SaslNamespace}'/>");

            Assert.Multiple(() =>
            {

                Assert.That(Gesendet("<aborted/>"), Is.True);

                Assert.That(Gesendet("unsupported-stanza-type"), Is.False,
                            "Ein Abbruch ist ein vorgesehener Schritt, kein Verstoss.");

                Assert.That(stream.IsClosed, Is.False,
                            "Der Abbruch beendet die Aushandlung, nicht den Stream.");

                Assert.That(behandelt, Is.True);

            });

        }

        #endregion

        #region AnAbortAtTheInitiator_IsNotAnswered()

        /// <summary>
        /// Umgekehrt nicht: Wer selbst angewählt hat, beantwortet keinen
        /// Abbruch — er wäre der, der ihn schickt.
        /// </summary>
        /// <remarks>
        /// Dieselbe Asymmetrie wie bei <c>&lt;auth/&gt;</c>: Die Rollen in der
        /// SASL-Aushandlung sind verteilt, und beide Seiten dieselbe Antwort
        /// geben zu lassen hiesse, sie als austauschbar zu behandeln.
        /// </remarks>
        [Test]
        public async Task AnAbortAtTheInitiator_IsNotAnswered()
        {

            var stream = S2SStream.Initiate("links.example", "rechts.example", Senden);

            await stream.OpenAsync();

            _gesendet.Clear();

            var behandelt = await stream.ProcessFrameAsync(
                                $"<abort xmlns='{S2SStream.SaslNamespace}'/>");

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet("<aborted/>"), Is.False);
                Assert.That(behandelt,              Is.False, "Zuständig ist er dafür nicht.");
            });

        }

        #endregion

        #region AFrameWithoutAnElement_IsIgnored()

        /// <summary>
        /// Ein Rahmen ohne Element ist kein unbekanntes Element, sondern gar
        /// keines — und beendet nichts.
        /// </summary>
        /// <remarks>
        /// Abschnitt 4.9.3.24 spricht von „a first-level child of the stream
        /// that is not supported". Ein leerer Rahmen ist kein Kind, das nicht
        /// unterstützt wird; er ist kein Kind.
        ///
        /// Über TCP kommt so etwas gar nicht erst an — <c>SkipProlog</c> im
        /// Zerleger schluckt Leerraum, XML-Deklarationen und Kommentare
        /// zwischen zwei Elementen, und Leerraum als Keepalive ist auf einem
        /// Stream ausdrücklich erlaubt (Abschnitt 4.6.1). Über WebSocket wird
        /// jeder Frame durchgereicht, und dort trägt die Unterscheidung den
        /// ganzen Fall.
        /// </remarks>
        [Test]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\r\n")]
        public async Task AFrameWithoutAnElement_IsIgnored(String rahmen)
        {

            var stream = Eingehend();

            await stream.ProcessFrameAsync(OpenVon("links.example"));

            _gesendet.Clear();

            await stream.ProcessFrameAsync(rahmen);

            Assert.Multiple(() =>
            {
                Assert.That(Gesendet("unsupported-stanza-type"), Is.False);
                Assert.That(stream.IsClosed,                     Is.False);
            });

        }

        #endregion

    }

}
