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
    /// XEP-0030, Abschnitt 4: disco#items - die Untereinheiten einer Entity.
    /// </summary>
    /// <remarks>
    /// Der Anlass ist kein fehlendes Merkmal, sondern ein falsches Versprechen.
    /// <c>LocalFeatures</c> führt <c>http://jabber.org/protocol/disco#items</c>
    /// seit jeher, beantwortet wurde eine items-Abfrage nie: Sie fiel bis zum
    /// <c>&lt;service-unavailable/&gt;</c> durch. Angekündigt und dann
    /// verweigert ist die einzige Kombination, die es nicht geben darf - eine
    /// Gegenstelle, die den Merkmalen glaubt, bekommt einen Fehler auf eine
    /// Frage, zu der sie eingeladen wurde.
    ///
    /// Die Antwort ist eine <b>leere</b> Liste, und das ist keine Notlösung:
    /// Ein Client hat keine Untereinheiten. „Ich habe keine" und „frag mich
    /// nicht" sind verschiedene Auskünfte, und nur die erste stimmt hier.
    /// </remarks>
    [TestFixture]
    public class DiscoItemsTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Fragt den Client über seine Serversitzung ab und gibt seine Antwort
        /// roh zurück.
        /// </summary>
        private async Task<String> FrageClientAsync(XMPPSession  session,
                                                    String       id,
                                                    String       ns,
                                                    String?      node = null)
        {

            var nodeAttribut = node is not null ? $" node='{node}'" : "";

            await session.SendAsync(
                      $"<iq type='get' id='{id}' from='{Server.Domain}' to='{session.FullJid}'>" +
                      $"<query xmlns='{ns}'{nodeAttribut}/></iq>");

            await WaitFor(() => session.Received.Any(f => f.Contains($"id='{id}'", StringComparison.Ordinal)),
                          $"die Antwort auf '{id}'");

            return session.Received.First(f => f.Contains($"id='{id}'", StringComparison.Ordinal));

        }

        /// <summary>Die gebundene Sitzung des angemeldeten Clients.</summary>
        private async Task<XMPPSession> SitzungAsync(XMPPClient client)
        {

            await WaitFor(() => Server.Sessions.Any(s => s.FullJid == client.Connection.FullJid),
                          "die gebundene Sitzung des Clients");

            return Server.Sessions.First(s => s.FullJid == client.Connection.FullJid);

        }

        #endregion


        #region TheAnnouncedFeature_IsActuallyAnswered()

        /// <summary>
        /// Der Kern: Was in der Merkmalsliste steht, muss auch beantwortet
        /// werden. Beides in einem Test, weil erst der Widerspruch der Fehler
        /// ist.
        /// </summary>
        [Test]
        public async Task TheAnnouncedFeature_IsActuallyAnswered()
        {

            var client   = await ConnectClientAsync("alice");
            var session  = await SitzungAsync(client);

            var merkmale = await FrageClientAsync(session, "merkmale",
                                                  "http://jabber.org/protocol/disco#info");

            var einheiten = await FrageClientAsync(session, "einheiten",
                                                   "http://jabber.org/protocol/disco#items");

            Assert.Multiple(() =>
            {

                Assert.That(merkmale, Does.Contain("var='http://jabber.org/protocol/disco#items'"),
                            "Ohne die Ankündigung gäbe es hier nichts zu prüfen.");

                Assert.That(einheiten, Does.Contain("type='result'"),
                            $"Angekündigt und dann verweigert: {einheiten}");

                Assert.That(einheiten, Does.Contain("xmlns='http://jabber.org/protocol/disco#items'"),
                            $"Die Antwort gehört zu der Frage, die gestellt wurde: {einheiten}");

            });

        }

        #endregion

        #region WithoutItems_TheListIsEmptyAndNotTheInfoAnswer()

        /// <summary>
        /// Ein Client ohne Untereinheiten antwortet mit einer leeren Liste -
        /// und nicht mit seiner Merkmalsliste.
        /// </summary>
        /// <remarks>
        /// Die zweite Hälfte ist die Gegenprobe gegen die naheliegendste
        /// Abkürzung: die items-Abfrage einfach an die info-Antwort zu hängen.
        /// Sie wäre grün für „es kommt etwas zurück" und falsch in allem
        /// anderen.
        /// </remarks>
        [Test]
        public async Task WithoutItems_TheListIsEmptyAndNotTheInfoAnswer()
        {

            var client  = await ConnectClientAsync("alice");
            var session = await SitzungAsync(client);

            var antwort = await FrageClientAsync(session, "leer",
                                                 "http://jabber.org/protocol/disco#items");

            Assert.Multiple(() =>
            {

                Assert.That(antwort, Does.Contain("type='result'"));

                Assert.That(antwort, Does.Not.Contain("<item "),
                            $"Dieser Client hat keine Untereinheiten: {antwort}");

                Assert.That(antwort, Does.Not.Contain("<identity"),
                            $"Das ist die Antwort auf disco#info, nicht auf disco#items: {antwort}");

                Assert.That(antwort, Does.Not.Contain("<feature"),
                            $"Das ist die Antwort auf disco#info, nicht auf disco#items: {antwort}");

                Assert.That(antwort, Does.Not.Contain("node="),
                            $"Ohne node in der Frage keines in der Antwort: {antwort}");

            });

        }

        #endregion

        #region ConfiguredItems_AreListed()

        /// <summary>
        /// Was in <c>LocalItems</c> steht, steht auch in der Antwort - mit
        /// <c>jid</c>, <c>node</c> und <c>name</c>.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test wäre „immer eine leere Liste" eine bestandene
        /// Lösung, und die Liste bliebe eine Zierde.
        /// </remarks>
        [Test]
        public async Task ConfiguredItems_AreListed()
        {

            var client  = await ConnectClientAsync("alice");
            var session = await SitzungAsync(client);

            client.Connection.Disco!.LocalItems.Add(
                new DiscoItem("dienst.example.test", "urn:example:zweig", "Ein Dienst"));

            var antwort = await FrageClientAsync(session, "mit-inhalt",
                                                 "http://jabber.org/protocol/disco#items");

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.Contain("jid='dienst.example.test'"));
                Assert.That(antwort, Does.Contain("node='urn:example:zweig'"));
                Assert.That(antwort, Does.Contain("name='Ein Dienst'"));
            });

        }

        #endregion

        #region AnItemsRequestWithNode_IsRefused()

        /// <summary>
        /// Ein Zweig, den es hier nicht gibt, bekommt
        /// <c>&lt;item-not-found/&gt;</c> - dieselbe Regel wie bei disco#info
        /// (siehe D39).
        /// </summary>
        /// <remarks>
        /// Für disco#items ist ein <c>node</c> ein Ast im Baum der
        /// Untereinheiten, nicht der Caps-Node aus XEP-0115. Dieser Client hat
        /// keinen einzigen. Eine leere Liste wäre hier die falsche Antwort: Sie
        /// hiesse „diesen Zweig gibt es, er ist leer" statt „diesen Zweig gibt
        /// es nicht".
        /// </remarks>
        [Test]
        public async Task AnItemsRequestWithNode_IsRefused()
        {

            var client  = await ConnectClientAsync("alice");
            var session = await SitzungAsync(client);

            var antwort = await FrageClientAsync(session, "zweig",
                                                 "http://jabber.org/protocol/disco#items",
                                                 "urn:example:kein-zweig");

            Assert.Multiple(() =>
            {

                Assert.That(antwort, Does.Contain("type='error'"));
                Assert.That(antwort, Does.Contain("<item-not-found"));

                Assert.That(antwort, Does.Contain("node='urn:example:kein-zweig'"),
                            "Auch der Fehler nennt die Frage, auf die er antwortet " +
                            $"(RFC 6120, Abschnitt 8.3.1): {antwort}");

                Assert.That(antwort, Does.Contain("xmlns='http://jabber.org/protocol/disco#items'"),
                            $"Der Fehler nimmt die items-Anfrage zurück, nicht irgendeine: {antwort}");

            });

        }

        #endregion

    }

}
