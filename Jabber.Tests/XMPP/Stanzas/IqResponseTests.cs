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
    /// RFC 6120, Abschnitt 8.2.3: Auf ein <c>iq</c> vom Typ <c>get</c> oder
    /// <c>set</c> MUSS eine Antwort folgen - <c>result</c> oder <c>error</c>.
    ///
    /// Bleibt sie aus, wartet die Gegenstelle bis in ihren Timeout. Ein Server
    /// wertet das je nach Implementierung als tote Sitzung.
    /// </summary>
    [TestFixture]
    public class IqResponseTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Schickt eine Stanza an den Client und wartet auf eine Antwort mit
        /// derselben id.
        /// </summary>
        private async Task<String> AskAsync(XMPPSession session, String id, String stanza)
        {

            await session.SendAsync(stanza);

            await WaitFor(() => session.Received.Any(f => f.Contains($"id='{id}'", StringComparison.Ordinal)),
                          $"Antwort auf IQ '{id}'");

            return session.Received.First(f => f.Contains($"id='{id}'", StringComparison.Ordinal));

        }

        /// <summary>
        /// Prüft, dass innerhalb der Wartezeit keine Antwort mit dieser id kommt.
        /// </summary>
        private async Task AssertNoAnswerAsync(XMPPSession session, String id)
        {

            var answered = await XMPPServer.WaitUntilAsync(
                               () => session.Received.Any(f => f.Contains($"id='{id}'", StringComparison.Ordinal)),
                               TimeSpan.FromSeconds(1));

            Assert.That(answered, Is.False,
                        $"Auf '{id}' hätte keine Antwort kommen dürfen.");

        }

        private async Task<XMPPSession> ConnectedSessionAsync()
        {

            await ConnectClientAsync();

            await WaitFor(() => Server.Sessions.Any(s => s.FullJid is not null),
                          "gebundene Sitzung");

            return Server.Sessions.First(s => s.FullJid is not null);

        }

        #endregion


        #region UnknownIqGet_IsAnsweredWithServiceUnavailable()

        /// <summary>
        /// Der Kern: ein <c>iq get</c>, für das es keinen Handler gibt, wurde
        /// früher still verworfen. Jetzt muss ein Fehler zurückkommen.
        /// </summary>
        [Test]
        public async Task UnknownIqGet_IsAnsweredWithServiceUnavailable()
        {

            var session = await ConnectedSessionAsync();

            var reply = await AskAsync(session, "probe-get",
                            $"<iq type='get' id='probe-get' from='{Server.Domain}' to='{session.FullJid}'>" +
                            "<query xmlns='urn:example:does-not-exist'/></iq>");

            Assert.Multiple(() =>
            {
                Assert.That(reply, Does.Contain("type='error'"));
                Assert.That(reply, Does.Contain("<service-unavailable"));
                Assert.That(reply, Does.Contain("urn:ietf:params:xml:ns:xmpp-stanzas"));
                Assert.That(reply, Does.Contain("type='cancel'"));
                Assert.That(reply, Does.Contain($"to='{Server.Domain}'"));
            });

        }

        #endregion

        #region UnknownIqSet_IsAnsweredWithServiceUnavailable()

        /// <summary>
        /// Dasselbe für <c>set</c>.
        /// </summary>
        [Test]
        public async Task UnknownIqSet_IsAnsweredWithServiceUnavailable()
        {

            var session = await ConnectedSessionAsync();

            var reply = await AskAsync(session, "probe-set",
                            $"<iq type='set' id='probe-set' from='{Server.Domain}' to='{session.FullJid}'>" +
                            "<command xmlns='urn:example:does-not-exist'/></iq>");

            Assert.Multiple(() =>
            {
                Assert.That(reply, Does.Contain("type='error'"));
                Assert.That(reply, Does.Contain("<service-unavailable"));
            });

        }

        #endregion

        #region UnknownIqWithoutFrom_IsAnsweredWithoutToAttribute()

        /// <summary>
        /// Ohne 'from' kam die Anfrage vom eigenen Server (RFC 6120,
        /// Abschnitt 8.1.1.1). Die Antwort muss trotzdem kommen, und zwar ohne
        /// 'to' - sie wird dann implizit an den Server zugestellt.
        /// </summary>
        [Test]
        public async Task UnknownIqWithoutFrom_IsAnsweredWithoutToAttribute()
        {

            var session = await ConnectedSessionAsync();

            var reply = await AskAsync(session, "probe-nofrom",
                            "<iq type='get' id='probe-nofrom'>" +
                            "<query xmlns='urn:example:does-not-exist'/></iq>");

            Assert.Multiple(() =>
            {
                Assert.That(reply, Does.Contain("type='error'"));
                Assert.That(reply, Does.Not.Contain(" to='"),
                            "Ohne 'from' darf die Antwort kein 'to' tragen.");
            });

        }

        #endregion

        #region IqResult_IsNotAnswered()

        /// <summary>
        /// Auf <c>result</c> und <c>error</c> darf nie geantwortet werden -
        /// sonst schaukeln sich zwei Gegenstellen endlos hoch.
        /// </summary>
        [Test]
        [TestCase("result", TestName = "IqResult_IsNotAnswered")]
        [TestCase("error",  TestName = "IqError_IsNotAnswered")]
        public async Task IqResponse_IsNotAnswered(String type)
        {

            var session = await ConnectedSessionAsync();

            await session.SendAsync(
                $"<iq type='{type}' id='probe-{type}' from='{Server.Domain}' to='{session.FullJid}'/>");

            await AssertNoAnswerAsync(session, $"probe-{type}");

        }

        #endregion

        #region IqWithoutId_IsNotAnswered()

        /// <summary>
        /// Ohne 'id' liesse sich die Antwort nicht zuordnen; das Attribut ist
        /// nach Abschnitt 8.2.3 zwingend. Statt eine unzuordenbare Antwort zu
        /// erzeugen, bleibt der Client still.
        /// </summary>
        [Test]
        public async Task IqWithoutId_IsNotAnswered()
        {

            var session = await ConnectedSessionAsync();

            // Das IQ, um das es geht: ohne 'id' und mit einem Namensraum, für
            // den es keinen Handler gibt. Beantwortet werden dürfte es nur mit
            // einem <service-unavailable/> - und genau das verbietet das
            // fehlende Attribut.
            await session.SendAsync(
                $"<iq type='get' from='{Server.Domain}' to='{session.FullJid}'>" +
                "<query xmlns='urn:example:does-not-exist'/></iq>");

            // Und direkt danach eines, das beantwortet werden *muss*.
            //
            // Das ist der Kern der Sache: Auf einem Stream wird der Reihe nach
            // verarbeitet. Ist die Antwort auf das zweite da, hat der Client
            // das erste bereits in der Hand gehabt und sich entschieden. Damit
            // braucht dieser Test keine Wartezeit mehr, innerhalb derer nichts
            // passieren darf.
            var antwort = await AskAsync(session, "probe-danach",
                              "<iq type='get' id='probe-danach' " +
                              $"from='{Server.Domain}' to='{session.FullJid}'>" +
                              "<query xmlns='http://jabber.org/protocol/disco#info'/></iq>");

            Assert.Multiple(() =>
            {

                Assert.That(antwort, Does.Contain("type='result'"),
                            "Vorbedingung: das zweite IQ muss beantwortet worden sein.");

                // Ein <service-unavailable/> hätte nur das erste auslösen
                // können - das zweite ist beantwortbar und beantwortet.
                Assert.That(session.Received.Any(f => f.Contains("type='error'", StringComparison.Ordinal)),
                            Is.False,
                            "Ein IQ ohne 'id' ist nicht beantwortbar und darf keine Antwort auslösen.");

            });

        }

        #endregion

        #region KnownIqGet_IsAnsweredByItsHandlerNotWithAnError()

        /// <summary>
        /// Der Fallback darf nicht vor die echten Handler geraten: ein Ping
        /// muss weiterhin ein <c>result</c> bekommen und keinen Fehler.
        /// </summary>
        [Test]
        public async Task KnownIqGet_IsAnsweredByItsHandlerNotWithAnError()
        {

            var session = await ConnectedSessionAsync();

            var reply = await AskAsync(session, "probe-ping",
                            $"<iq type='get' id='probe-ping' from='{Server.Domain}' to='{session.FullJid}'>" +
                            "<ping xmlns='urn:xmpp:ping'/></iq>");

            Assert.Multiple(() =>
            {
                Assert.That(reply, Does.Contain("type='result'"));
                Assert.That(reply, Does.Not.Contain("service-unavailable"));
            });

        }

        #endregion

        #region DiscoInfoRequest_IsAnsweredWithFeatures()

        /// <summary>
        /// Auch disco#info muss vom eigenen Handler beantwortet werden.
        /// </summary>
        [Test]
        public async Task DiscoInfoRequest_IsAnsweredWithFeatures()
        {

            var session = await ConnectedSessionAsync();

            var reply = await AskAsync(session, "probe-disco",
                            $"<iq type='get' id='probe-disco' from='{Server.Domain}' to='{session.FullJid}'>" +
                            "<query xmlns='http://jabber.org/protocol/disco#info'/></iq>");

            Assert.Multiple(() =>
            {
                Assert.That(reply, Does.Contain("type='result'"));
                Assert.That(reply, Does.Contain("<identity"));
                Assert.That(reply, Does.Not.Contain("service-unavailable"));
            });

        }

        #endregion

        #region SpoofedRosterPush_IsNotAnswered()

        /// <summary>
        /// Die eine erlaubte Ausnahme von Abschnitt 8.2.3: RFC 6121
        /// Abschnitt 2.1.6 gestattet ausdrücklich, auf einen Roster-Push von
        /// einem nicht autorisierten Absender gar nicht zu antworten - eine
        /// Antwort würde bestätigen, dass das Konto online ist.
        ///
        /// Der Fallback darf hier also nicht greifen.
        /// </summary>
        [Test]
        public async Task SpoofedRosterPush_IsNotAnswered()
        {

            var session = await ConnectedSessionAsync();

            await session.SendAsync(
                $"<iq type='set' id='probe-spoof' from='mallory@evil.example' to='{session.FullJid}'>" +
                "<query xmlns='jabber:iq:roster'>" +
                "<item jid='mallory@evil.example' subscription='both'/></query></iq>");

            await AssertNoAnswerAsync(session, "probe-spoof");

        }

        #endregion

        #region LegitimateRosterPush_IsAnsweredWithResult()

        /// <summary>
        /// Ein Roster-Push vom eigenen Konto wird dagegen normal bestätigt.
        /// </summary>
        [Test]
        public async Task LegitimateRosterPush_IsAnsweredWithResult()
        {

            var session = await ConnectedSessionAsync();

            var reply = await AskAsync(session, "probe-roster",
                            $"<iq type='set' id='probe-roster' to='{session.FullJid}'>" +
                            "<query xmlns='jabber:iq:roster'>" +
                            $"<item jid='carol@{Server.Domain}' subscription='both'/></query></iq>");

            Assert.That(reply, Does.Contain("type='result'"));

        }

        #endregion

        #region AnIqWithAnUnknownType_IsRefusedWithBadRequest()

        /// <summary>
        /// RFC 6120, Abschnitt 8.2.3, Regel 2: Ein <c>type</c>, der keiner der
        /// vier vorgesehenen Werte ist, bekommt
        /// <c>&lt;bad-request/&gt;</c> — von „the recipient", und das ist hier
        /// der Client.
        /// </summary>
        /// <remarks>
        /// Nicht dieselbe Prüfung wie im Server, sondern die zweite Rolle
        /// derselben Regel. Der Server weist ab, was er weiterreichen soll; der
        /// Client weist ab, was bei ihm ankommt. Beide werden gebraucht: Gegen
        /// diesen Server käme eine solche Stanza nie beim Client an, gegen eine
        /// fremde Implementierung ohne Regel 2 sehr wohl.
        ///
        /// Vorher fiel sie hier stillschweigend durch: Der Fallback am Ende von
        /// <c>ProcessIq</c> fragt nach <c>get</c> oder <c>set</c>, und ein
        /// fünfter Wert ist keins von beidem.
        /// </remarks>
        [Test]
        [TestCase("vielleicht", TestName = "AnIqWithAnUnknownType_IsRefusedWithBadRequest")]
        [TestCase(null,         TestName = "AnIqWithoutAType_IsRefusedWithBadRequest")]
        public async Task AnIqWithABrokenType_IsRefusedWithBadRequest(String? type)
        {

            var session = await ConnectedSessionAsync();

            var reply = await AskAsync(session, "probe-typ",
                            "<iq" +
                            (type is not null ? $" type='{type}'" : "") +
                            $" id='probe-typ' from='{Server.Domain}' to='{session.FullJid}'>" +
                            "<query xmlns='urn:example:does-not-exist'/></iq>");

            Assert.Multiple(() =>
            {

                Assert.That(reply, Does.Contain("type='error'"));
                Assert.That(reply, Does.Contain("<bad-request "));
                Assert.That(reply, Does.Contain("urn:ietf:params:xml:ns:xmpp-stanzas"));

                // Abschnitt 8.3.3.1: modify und nicht cancel - der Absender
                // kann es richtig gestellt noch einmal versuchen.
                Assert.That(reply, Does.Contain("type='modify'"));

                Assert.That(reply, Does.Contain($"to='{Server.Domain}'"));

            });

        }

        #endregion

        #region TheRefusalComesEvenWithoutAnId()

        /// <summary>
        /// Ohne <c>id</c> wird die Ablehnung trotzdem geschickt — anders als
        /// bei einer unbehandelten, aber wohlgeformten Anfrage.
        /// </summary>
        /// <remarks>
        /// Der Unterschied ist der Inhalt der Antwort. Ein
        /// <c>&lt;service-unavailable/&gt;</c> beantwortet eine Frage, und eine
        /// Antwort ohne <c>id</c> lässt sich keiner Frage zuordnen — sie nützt
        /// niemandem, deshalb schweigt der Client dort (siehe
        /// <see cref="IqWithoutId_IsNotAnswered"/>).
        ///
        /// <c>&lt;bad-request/&gt;</c> sagt etwas über die Stanza selbst: dass
        /// ihre Form nicht stimmt. Das kann der Absender auch dann brauchen,
        /// wenn er es keiner offenen Frage zuordnen kann — zumal die fehlende
        /// <c>id</c> nach Regel 1 selbst zu dem gehört, was nicht stimmt.
        ///
        /// Ein leeres <c>id=''</c> wäre der schlechteste Ausgang: Es gehört zu
        /// keiner Frage und sieht aus, als gehörte es zu einer.
        /// </remarks>
        [Test]
        public async Task TheRefusalComesEvenWithoutAnId()
        {

            var session = await ConnectedSessionAsync();

            await session.SendAsync(
                      $"<iq type='vielleicht' from='{Server.Domain}' to='{session.FullJid}'>" +
                      "<query xmlns='urn:example:does-not-exist'/></iq>");

            await WaitFor(() => session.Received.Any(f => f.Contains("bad-request", StringComparison.Ordinal)),
                          "die Ablehnung ohne id");

            var reply = session.Received.First(f => f.Contains("bad-request", StringComparison.Ordinal));

            Assert.That(reply, Does.Not.Contain("id="),
                        "Was keine id hatte, bekommt auch keine leere zurück.");

        }

        #endregion

        #region TheRefusalWithoutASenderCarriesNoTo()

        /// <summary>
        /// Ohne <c>from</c> kam die Stanza vom eigenen Server (RFC 6120,
        /// Abschnitt 8.1.1.1) — die Ablehnung geht dann ohne <c>to</c> zurück.
        /// </summary>
        /// <remarks>
        /// Und das ist der Regelfall, nicht die Ausnahme: Was ein Server seinem
        /// eigenen Client schickt, trägt oft kein <c>from</c>. Ein <c>to=''</c>
        /// wäre hier eine Adresse, die es nicht gibt.
        /// </remarks>
        [Test]
        public async Task TheRefusalWithoutASenderCarriesNoTo()
        {

            var session = await ConnectedSessionAsync();

            var reply = await AskAsync(session, "probe-ohne-absender",
                            "<iq type='vielleicht' id='probe-ohne-absender'>" +
                            "<query xmlns='urn:example:does-not-exist'/></iq>");

            Assert.Multiple(() =>
            {
                Assert.That(reply, Does.Contain("<bad-request "));
                Assert.That(reply, Does.Not.Contain(" to='"));
            });

        }

        #endregion

    }

}
