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

    }

}
