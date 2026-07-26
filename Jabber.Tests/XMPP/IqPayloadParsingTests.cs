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

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die Nutzlasten innerhalb von <c>iq</c>-Stanzas in gültiger, aber
    /// ungewöhnlicher Schreibweise: Service Discovery, PubSub und Ping.
    ///
    /// Der letzte Teil des Umbaus von regulären Ausdrücken auf einen
    /// XML-Parser.
    /// </summary>
    [TestFixture]
    public class IqPayloadParsingTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private async Task<(XMPPClient Client, XMPPSession Session)> ConnectedPairAsync()
        {

            var client = await ConnectClientAsync();

            await WaitFor(() => Server.SessionOf(client.FullJid) is not null,
                          "Serversitzung zum Client");

            return (client, Server.SessionOf(client.FullJid)!);

        }

        #endregion


        #region Ping_WithDoubleQuotedType_IsAnsweredWithAResult()

        /// <summary>
        /// Die Ping-Erkennung suchte wörtlich nach <c>type='get'</c>, also nur
        /// mit einfachen Anführungszeichen. Gegen einen Server, der doppelte
        /// benutzt, wurde der Ping nicht erkannt - und landete seit der
        /// Umsetzung von RFC 6120 §8.2.3 im Rückfall, bekam also ein
        /// <c>&lt;service-unavailable/&gt;</c> statt einer Antwort.
        /// </summary>
        [Test]
        public async Task Ping_WithDoubleQuotedType_IsAnsweredWithAResult()
        {

            var (_, session) = await ConnectedPairAsync();

            await session.SendAsync(
                $"<iq type=\"get\" id=\"p1\" from=\"{Server.Domain}\">" +
                "<ping xmlns=\"urn:xmpp:ping\"/></iq>");

            await WaitFor(() => session.Received.Any(f => f.Contains("id='p1'", StringComparison.Ordinal)),
                          "Antwort auf den Ping");

            var reply = session.Received.First(f => f.Contains("id='p1'", StringComparison.Ordinal));

            Assert.Multiple(() =>
            {
                Assert.That(reply, Does.Contain("type='result'"));
                Assert.That(reply, Does.Not.Contain("service-unavailable"),
                            "Ein erkannter Ping darf nicht im Rückfall landen.");
            });

        }

        #endregion

        #region DiscoInfo_WithSlashInTheIdentityName_IsParsed()

        /// <summary>
        /// Das Muster für Identitäten schloss den Schrägstrich aus
        /// (<c>&lt;identity([^/&gt;]+)/?&gt;</c>), damit es das schliessende
        /// <c>/&gt;</c> nicht mitfrisst. Ein Name mit Schrägstrich - der eigene
        /// Client heisst „XMPP Console Client" mit Kategorie
        /// <c>client/console</c>, so etwas ist also alles andere als exotisch -
        /// liess die Identität komplett verschwinden.
        /// </summary>
        [Test]
        public async Task DiscoInfo_WithSlashInTheIdentityName_IsParsed()
        {

            var (client, session) = await ConnectedPairAsync();

            Server.OnStanzaReceived += (s, frame) =>
            {
                if (frame.Contains("disco#info", StringComparison.Ordinal) &&
                    frame.Contains("type='get'", StringComparison.Ordinal))
                {

                    var id = System.Text.RegularExpressions.Regex.Match(frame, @"id='([^']+)'").Groups[1].Value;

                    _ = s.SendAsync(
                        $"<iq type='result' id='{id}' from='{Server.Domain}'>" +
                        "<query xmlns='http://jabber.org/protocol/disco#info'>" +
                        "<identity category='client' type='pc' name='Foo/Bar &amp; Co.'/>" +
                        "<feature var='urn:xmpp:ping'/>" +
                        "</query></iq>");

                }
            };

            var info = await client.Connection.Disco!.QueryInfoAsync(Server.Domain,
                                                                     timeout: TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(info, Is.Not.Null);
                Assert.That(info!.Identities, Has.Count.EqualTo(1));
                Assert.That(info!.Identities[0].Name, Is.EqualTo("Foo/Bar & Co."),
                            "Schrägstrich und Entity im Namen müssen erhalten bleiben.");
                Assert.That(info!.Features, Does.Contain("urn:xmpp:ping"));
            });

        }

        #endregion

        #region DiscoInfo_WithVarNotBeingTheFirstAttribute_IsParsed()

        /// <summary>
        /// Das Feature-Muster verlangte <c>var</c> unmittelbar nach
        /// <c>&lt;feature</c>. Steht ein anderes Attribut davor, verschwand das
        /// Feature aus der Liste - und der Client hielt die Gegenstelle für
        /// weniger fähig, als sie ist.
        /// </summary>
        [Test]
        public async Task DiscoInfo_WithVarNotBeingTheFirstAttribute_IsParsed()
        {

            var (client, session) = await ConnectedPairAsync();

            Server.OnStanzaReceived += (s, frame) =>
            {
                if (frame.Contains("disco#info", StringComparison.Ordinal) &&
                    frame.Contains("type='get'", StringComparison.Ordinal))
                {

                    var id = System.Text.RegularExpressions.Regex.Match(frame, @"id='([^']+)'").Groups[1].Value;

                    _ = s.SendAsync(
                        $"<iq type='result' id='{id}' from='{Server.Domain}'>" +
                        "<query xmlns='http://jabber.org/protocol/disco#info'>" +
                        "<identity category='server' type='im'/>" +
                        "<feature xml:lang='de' var='urn:xmpp:carbons:2'/>" +
                        "</query></iq>");

                }
            };

            var info = await client.Connection.Disco!.QueryInfoAsync(Server.Domain,
                                                                     timeout: TimeSpan.FromSeconds(5));

            Assert.That(info?.Features, Does.Contain("urn:xmpp:carbons:2"));

        }

        #endregion

        #region PubSubEvent_WithDoubleQuotedNamespace_IsRecognised()

        /// <summary>
        /// Das Event-Muster suchte wörtlich nach
        /// <c>&lt;event xmlns='…pubsub#event'</c> - einfache Anführungszeichen,
        /// und <c>xmlns</c> als erstes Attribut. Beides schreibt XML nicht vor.
        /// </summary>
        [Test]
        public async Task PubSubEvent_WithDoubleQuotedNamespace_IsRecognised()
        {

            var (client, session) = await ConnectedPairAsync();

            PubSubEvent? reported = null;
            client.OnPubSubEvent += e => reported = e;

            await session.SendAsync(
                $"<iq type='set' id='ps1' from='pubsub.{Server.Domain}' to='{client.FullJid}'>" +
                "<event xmlns=\"http://jabber.org/protocol/pubsub#event\">" +
                "<items node=\"urn:example:nachrichten\">" +
                "<item id=\"1\"><payload xmlns='urn:example:x'>Inhalt</payload></item>" +
                "</items></event></iq>");

            await WaitFor(() => reported is not null, "gemeldetes PubSub-Event");

            Assert.Multiple(() =>
            {
                Assert.That(reported!.NodeId, Is.EqualTo("urn:example:nachrichten"));
                Assert.That(reported!.Type,   Is.EqualTo(PubSubEventType.Items));
                Assert.That(reported!.Items,  Has.Count.EqualTo(1));
            });

        }

        #endregion

        #region PubSubEvent_WithItemWithoutPayload_IsRecognised()

        /// <summary>
        /// Ein <c>&lt;item/&gt;</c> ohne Nutzlast ist zulässig - XEP-0060
        /// erlaubt reine Benachrichtigungen ohne Inhalt. Das frühere Muster
        /// verlangte ein Paar aus öffnendem und schliessendem Tag und übersah
        /// selbstschliessende Items ganz.
        /// </summary>
        [Test]
        public async Task PubSubEvent_WithItemWithoutPayload_IsRecognised()
        {

            var (client, session) = await ConnectedPairAsync();

            PubSubEvent? reported = null;
            client.OnPubSubEvent += e => reported = e;

            await session.SendAsync(
                $"<iq type='set' id='ps2' from='pubsub.{Server.Domain}' to='{client.FullJid}'>" +
                "<event xmlns='http://jabber.org/protocol/pubsub#event'>" +
                "<items node='urn:example:signale'>" +
                "<item id='ohne-inhalt'/>" +
                "</items></event></iq>");

            await WaitFor(() => reported is not null, "gemeldetes PubSub-Event");

            Assert.Multiple(() =>
            {
                Assert.That(reported!.Items, Has.Count.EqualTo(1));
                Assert.That(reported!.Items[0].Id, Is.EqualTo("ohne-inhalt"));
            });

        }

        #endregion

        #region RosterNamespaceInsideAForwardedMessage_IsNotTakenAsARosterPush()

        /// <summary>
        /// Die Fallunterscheidung in ProcessIq lief über
        /// <c>stanza.Contains("jabber:iq:roster")</c> - der Namensraum musste
        /// also nur irgendwo im Text vorkommen. Eine eingebettete Nachricht,
        /// die ihn erwähnt, wurde damit als Roster-Push behandelt.
        /// </summary>
        [Test]
        public async Task RosterNamespaceInsideAForwardedMessage_IsNotTakenAsARosterPush()
        {

            var (client, session) = await ConnectedPairAsync();

            await session.SendAsync(
                $"<iq type='set' id='fake-push' to='{client.FullJid}'>" +
                "<forwarded xmlns='urn:xmpp:forward:0'>" +
                "<message xmlns='jabber:client'>" +
                "<query xmlns='jabber:iq:roster'>" +
                $"<item jid='eindringling@{Server.Domain}' subscription='both'/>" +
                "</query></message></forwarded></iq>");

            await WaitFor(() => session.Received.Any(f => f.Contains("id='fake-push'", StringComparison.Ordinal)),
                          "Antwort auf das IQ");

            Assert.That(client.GetContact($"eindringling@{Server.Domain}"), Is.Null,
                        "Ein eingebettetes Roster-Element ist kein Roster-Push.");

        }

        #endregion

    }

}
