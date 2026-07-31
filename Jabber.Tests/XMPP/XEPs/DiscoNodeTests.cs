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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// XEP-0030, Abschnitt 3.2: „If the request included a 'node' attribute,
    /// the response MUST mirror the specified 'node' attribute to ensure
    /// coherence between the request and the response."
    /// </summary>
    /// <remarks>
    /// Die Spiegelung ist kein Schmuck. XEP-0115, Abschnitt 6.2 lässt eine
    /// Gegenstelle mit <c>node#ver</c> fragen und legt die Antwort unter genau
    /// diesem Schlüssel ab; das zurückgegebene <c>node</c> ist die Zusage, dass
    /// die Antwort zu der Frage gehört. Fehlt sie, füllt eine strenge
    /// Gegenstelle ihren Cache nicht und fragt bei jeder Presence erneut - der
    /// Nutzen von XEP-0115 fällt weg, ohne dass irgendetwas kaputt aussieht.
    ///
    /// Wir haben das lange von anderen verlangt und selbst nicht geliefert:
    /// <c>EntityCapsManager</c> fragt seit jeher mit <c>node#ver</c>, unsere
    /// eigene Antwort trug nie ein <c>node</c>.
    ///
    /// Die zweite Hälfte ist die unangenehmere: Ein Node, den es hier nicht
    /// gibt, wurde bis dahin genauso beantwortet wie gar keiner - mit der
    /// vollen Merkmalsliste. Damit behauptete diese Seite, jeden erdachten Node
    /// zu führen. Ein veralteter <c>ver</c> gehört ausdrücklich dazu: Er fragt
    /// nach dem Stand von damals, und den gibt es nicht mehr. Die aktuelle
    /// Liste zu schicken wäre die falsche Antwort auf die richtige Frage - der
    /// Frager rechnet den angekündigten Hash nach, bekommt einen anderen und
    /// müsste uns für einen Fälscher halten.
    /// </remarks>
    [TestFixture]
    public class DiscoNodeTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Fragt den <b>Client</b> über seine Serversitzung nach disco#info und
        /// gibt seine Antwort roh zurück.
        /// </summary>
        private async Task<String> FrageClientAsync(XMPPSession  session,
                                                    String       id,
                                                    String?      node)
        {

            var nodeAttribut = node is not null ? $" node='{node}'" : "";

            await session.SendAsync(
                      $"<iq type='get' id='{id}' from='{Server.Domain}' to='{session.FullJid}'>" +
                      $"<query xmlns='http://jabber.org/protocol/disco#info'{nodeAttribut}/></iq>");

            await WaitFor(() => session.Received.Any(f => f.Contains($"id='{id}'", StringComparison.Ordinal)),
                          $"die disco#info-Antwort auf '{id}'");

            return session.Received.First(f => f.Contains($"id='{id}'", StringComparison.Ordinal));

        }

        /// <summary>Die gebundene Sitzung des einzigen angemeldeten Clients.</summary>
        private async Task<XMPPSession> SitzungAsync(XMPPClient client)
        {

            await WaitFor(() => Server.Sessions.Any(s => s.FullJid == client.Connection.FullJid),
                          "die gebundene Sitzung des Clients");

            return Server.Sessions.First(s => s.FullJid == client.Connection.FullJid);

        }

        /// <summary>
        /// Fragt den <b>Server</b> an seiner eigenen Adresse nach disco#info und
        /// gibt seine Antwort roh zurück.
        /// </summary>
        private async Task<String> FrageServerAsync(XMPPClient  client,
                                                    String      id,
                                                    String?     node)
        {

            var nodeAttribut = node is not null ? $" node='{node}'" : "";
            var antworten    = new ConcurrentQueue<String>();

            client.Connection.OnRawXml += xml =>
            {
                if (xml.Contains("<<<", StringComparison.Ordinal) &&
                    xml.Contains(id,    StringComparison.Ordinal))
                {
                    antworten.Enqueue(xml);
                }
            };

            await client.SendRawAsync(
                      $"<iq type='get' id='{id}' to='{Server.Domain}'>" +
                      $"<query xmlns='http://jabber.org/protocol/disco#info'{nodeAttribut}/></iq>");

            await WaitFor(() => !antworten.IsEmpty, $"die disco#info-Antwort des Servers auf '{id}'");

            antworten.TryDequeue(out var antwort);

            return antwort!;

        }

        #endregion


        #region WithoutANode_TheAnswerCarriesNone()

        /// <summary>
        /// Die Gegenprobe zuerst: Ohne <c>node</c> in der Frage darf auch die
        /// Antwort keines tragen.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test wäre „immer irgendein <c>node</c> anhängen" eine
        /// bestandene Lösung. Sie wäre falsch: Ein <c>node</c> in der Antwort
        /// auf eine Frage ohne Node behauptet, die Auskunft gelte nur für einen
        /// Teilbereich.
        /// </remarks>
        [Test]
        public async Task WithoutANode_TheAnswerCarriesNone()
        {

            var client  = await ConnectClientAsync("alice");
            var session = await SitzungAsync(client);

            var antwort = await FrageClientAsync(session, "ohne-node", null);

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.Contain("type='result'"));
                Assert.That(antwort, Does.Not.Contain("node="),
                            $"Die Antwort trägt ein node, obwohl keines gefragt war: {antwort}");
            });

        }

        #endregion

        #region OurOwnCapsNode_IsMirrored()

        /// <summary>
        /// Der Kern: Die Frage nach <c>node#ver</c> - so, wie XEP-0115,
        /// Abschnitt 6.2 sie stellt - bekommt dasselbe <c>node</c> zurück.
        /// </summary>
        [Test]
        public async Task OurOwnCapsNode_IsMirrored()
        {

            var client  = await ConnectClientAsync("alice");
            var session = await SitzungAsync(client);

            var caps    = client.Connection.EntityCaps!;
            var node    = $"{caps.Node}#{caps.CalculateVerificationString()}";

            var antwort = await FrageClientAsync(session, "caps-node", node);

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.Contain("type='result'"));
                Assert.That(antwort, Does.Contain($"node='{node}'"),
                            $"Das node der Frage fehlt in der Antwort: {antwort}");
                Assert.That(antwort, Does.Contain("<identity"),
                            "Die Antwort auf den eigenen Caps-Node ist die volle Auskunft.");
                Assert.That(antwort, Does.Contain("urn:xmpp:ping"));
            });

        }

        #endregion

        #region TheBareCapsNode_IsAnsweredToo()

        /// <summary>
        /// Der Caps-Node ohne <c>#ver</c> bezeichnet uns ebenfalls und wird
        /// beantwortet.
        /// </summary>
        /// <remarks>
        /// XEP-0115 sagt „SHOULD" zu der Form <c>node#ver</c>, nicht „MUST".
        /// Wer nur den Node nennt, fragt nach dieser Entity, ohne einen Stand
        /// festzunageln - das ist beantwortbar, und zwar mit dem heutigen.
        /// </remarks>
        [Test]
        public async Task TheBareCapsNode_IsAnsweredToo()
        {

            var client  = await ConnectClientAsync("alice");
            var session = await SitzungAsync(client);

            var node    = client.Connection.EntityCaps!.Node;

            var antwort = await FrageClientAsync(session, "blanker-node", node);

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.Contain("type='result'"));
                Assert.That(antwort, Does.Contain($"node='{node}'"));
            });

        }

        #endregion

        #region AStaleVerificationString_IsRefused()

        /// <summary>
        /// Ein <c>ver</c>, der nicht mehr unserer ist, fragt nach einem Stand,
        /// den es nicht mehr gibt - und bekommt <c>&lt;item-not-found/&gt;</c>.
        /// </summary>
        /// <remarks>
        /// Das ist die Entscheidung dieses Punktes, und sie ist unbequem:
        /// Verbreitete Server schicken hier die aktuelle Liste. Das ist
        /// bequemer und falsch. Der Frager prüft nach XEP-0115, Abschnitt 5.4
        /// den angekündigten Hash gegen die Antwort; bekommt er zu einem alten
        /// <c>ver</c> die neue Liste, ergibt sie einen anderen Hash. Er hat dann
        /// die Wahl, uns für einen Fälscher zu halten oder die Prüfung
        /// aufzugeben - unser eigener <c>EntityCapsManager</c> würde die Antwort
        /// ablehnen. Ein Fehler ist die ehrlichere Auskunft: Diesen Stand gibt
        /// es hier nicht mehr.
        /// </remarks>
        [Test]
        public async Task AStaleVerificationString_IsRefused()
        {

            var client  = await ConnectClientAsync("alice");
            var session = await SitzungAsync(client);

            var node    = $"{client.Connection.EntityCaps!.Node}#VonGesternXXXXXXXXXXXXXXX=";

            var antwort = await FrageClientAsync(session, "alter-ver", node);

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.Contain("type='error'"));
                Assert.That(antwort, Does.Contain("<item-not-found"));
                Assert.That(antwort, Does.Contain("urn:ietf:params:xml:ns:xmpp-stanzas"));
                Assert.That(antwort, Does.Contain($"node='{node}'"),
                            "Auch der Fehler nennt die Frage, auf die er antwortet " +
                            $"(RFC 6120, Abschnitt 8.3.1): {antwort}");
                Assert.That(antwort, Does.Not.Contain("<identity"),
                            $"Ein Fehler trägt keine Auskunft: {antwort}");
            });

        }

        #endregion

        #region AForeignNode_IsRefused()

        /// <summary>
        /// Ein Node, der mit uns nichts zu tun hat, bekommt ebenfalls
        /// <c>&lt;item-not-found/&gt;</c> statt der vollen Liste.
        /// </summary>
        [Test]
        public async Task AForeignNode_IsRefused()
        {

            var client  = await ConnectClientAsync("alice");
            var session = await SitzungAsync(client);

            var antwort = await FrageClientAsync(session, "fremder-node",
                                                 "http://jabber.org/protocol/commands");

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.Contain("type='error'"));
                Assert.That(antwort, Does.Contain("<item-not-found"));
                Assert.That(antwort, Does.Not.Contain("<feature"),
                            $"Ein unbekannter Node bekommt keine Merkmalsliste: {antwort}");
            });

        }

        #endregion

        #region TheServerAnswersWithoutANode()

        /// <summary>
        /// Dieselbe Gegenprobe für den Server: Die gewöhnliche Frage bleibt
        /// unverändert beantwortet.
        /// </summary>
        [Test]
        public async Task TheServerAnswersWithoutANode()
        {

            var client  = await ConnectClientAsync("alice");

            var antwort = await FrageServerAsync(client, "server-ohne-node", null);

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.Contain("type='result'"));
                Assert.That(antwort, Does.Contain("category='server'"));
                Assert.That(antwort, Does.Not.Contain("node="),
                            $"Die Antwort trägt ein node, obwohl keines gefragt war: {antwort}");
            });

        }

        #endregion

        #region ANodeOutsideTheQuery_DoesNotCount()

        /// <summary>
        /// Ein <c>node</c>-Attribut, das nicht zur Anfrage gehört, macht die
        /// Anfrage nicht zu einer nach einem Node.
        /// </summary>
        /// <remarks>
        /// Der Server liest seine Frames als Zeichenketten, nicht als Baum -
        /// bewusst, denn er soll den Client nicht mit derselben Brille ansehen,
        /// mit der der Client sich selbst ansieht. Der Preis ist, dass „steht
        /// <c>node=</c> irgendwo im Frame" und „trägt die Anfrage ein
        /// <c>node</c>" zwei verschiedene Dinge sind. Ohne diesen Test wäre der
        /// Unterschied unbelegt, und die gewöhnliche Abfrage eines Clients, der
        /// seiner Anfrage irgendetwas beilegt, bekäme einen Fehler.
        /// </remarks>
        [Test]
        public async Task ANodeOutsideTheQuery_DoesNotCount()
        {

            var client  = await ConnectClientAsync("alice");

            var antworten = new ConcurrentQueue<String>();

            client.Connection.OnRawXml += xml =>
            {
                if (xml.Contains("<<<",           StringComparison.Ordinal) &&
                    xml.Contains("node-daneben",  StringComparison.Ordinal))
                {
                    antworten.Enqueue(xml);
                }
            };

            await client.SendRawAsync(
                      $"<iq type='get' id='node-daneben' to='{Server.Domain}'>" +
                      "<query xmlns='http://jabber.org/protocol/disco#info'/>" +
                      "<beilage xmlns='urn:example:egal' node='nicht-gefragt'/></iq>");

            await WaitFor(() => !antworten.IsEmpty, "die disco#info-Antwort des Servers");

            antworten.TryDequeue(out var antwort);

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.Contain("type='result'"),
                            $"Das node der Beilage gehört nicht zur Anfrage: {antwort}");
                Assert.That(antwort, Does.Contain("category='server'"));
            });

        }

        #endregion

        #region TheServerHasNoNodes()

        /// <summary>
        /// Der Server kündigt keine Capabilities an und führt keine Nodes -
        /// eine Frage nach einem Node bekommt <c>&lt;item-not-found/&gt;</c>.
        /// </summary>
        /// <remarks>
        /// Der Schalter <c>FailDiscoInfo</c> antwortete schon immer mit dem Text
        /// „Diesen Node gibt es hier nicht." - einer Aussage, die der Server gar
        /// nicht treffen konnte, weil er das Attribut nie ansah. Jetzt trifft
        /// sie zu.
        /// </remarks>
        [Test]
        public async Task TheServerHasNoNodes()
        {

            var client  = await ConnectClientAsync("alice");

            var antwort = await FrageServerAsync(client, "server-mit-node",
                                                 "http://jabber.org/protocol/offline");

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.Contain("type='error'"));
                Assert.That(antwort, Does.Contain("<item-not-found"));
                Assert.That(antwort, Does.Contain("node='http://jabber.org/protocol/offline'"),
                            "Auch der Fehler nennt die Frage, auf die er antwortet " +
                            $"(RFC 6120, Abschnitt 8.3.1): {antwort}");
                Assert.That(antwort, Does.Not.Contain("category='server'"),
                            $"Ein unbekannter Node bekommt keine Auskunft: {antwort}");
            });

        }

        #endregion

    }

}
