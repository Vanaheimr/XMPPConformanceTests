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
    /// Was mit einer Stanza geschieht, die an eine fremde Domain geht.
    ///
    /// Bisher: nichts. Der Server suchte eine Sitzung zum Empfänger, fand
    /// keine, und liess die Stanza fallen. Für einen Absender sieht das aus
    /// wie eine zugestellte Nachricht - er erfährt nie, dass sie nirgends
    /// angekommen ist.
    ///
    /// RFC 6120, Abschnitt 10.4.3 verlangt in diesem Fall einen Stanza-Fehler.
    /// Die Bedingung <c>&lt;remote-server-not-found/&gt;</c> steht in
    /// Abschnitt 8.3.3.
    /// </summary>
    [TestFixture]
    public class DomainRoutingTests : AXMPPTests
    {

        #region MessageToForeignDomain_IsAnsweredWithAnError()

        /// <summary>
        /// Der Kern: eine Nachricht an eine unerreichbare Domain kommt als
        /// Fehler zurück, statt spurlos zu verschwinden.
        /// </summary>
        [Test]
        public async Task MessageToForeignDomain_IsAnsweredWithAnError()
        {

            var client  = await ConnectClientAsync();
            var fehler  = new List<(String? From, StanzaError Error)>();

            client.OnStanzaError += (from, error) => fehler.Add((from, error));

            await client.SendMessageAsync("bob@anderswo.example", "Hallo?");

            await WaitFor(() => fehler.Count > 0, "Fehlermeldung zur fremden Domain");

            Assert.Multiple(() =>
            {
                Assert.That(fehler[0].Error.Condition, Is.EqualTo("remote-server-not-found"));
                Assert.That(fehler[0].From,            Is.EqualTo("bob@anderswo.example"),
                            "Der Fehler muss vom ursprünglichen Empfänger zu kommen scheinen.");
            });

        }

        #endregion

        #region IqToForeignDomain_IsAnsweredWithAnError()

        /// <summary>
        /// Dasselbe für ein <c>iq</c> - dort wiegt es schwerer, weil der
        /// Absender nach RFC 6120, Abschnitt 8.2.3 auf eine Antwort wartet.
        /// </summary>
        [Test]
        public async Task IqToForeignDomain_IsAnsweredWithAnError()
        {

            var client   = await ConnectClientAsync();
            var session  = Server.SessionOf(client.FullJid)!;

            await client.SendRawAsync(
                "<iq type='get' id='fremd-1' to='bob@anderswo.example'>" +
                "<query xmlns='http://jabber.org/protocol/disco#info'/></iq>");

            await WaitFor(() => session.Sent.Any(f => f.Contains("id='fremd-1'", StringComparison.Ordinal)),
                          "Antwort auf das iq an die fremde Domain");

            var antwort = session.Sent.First(f => f.Contains("id='fremd-1'", StringComparison.Ordinal));

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.Contain("type='error'"));
                Assert.That(antwort, Does.Contain("remote-server-not-found"));
            });

        }

        #endregion

        #region ErrorStanza_IsNotAnsweredAgain()

        /// <summary>
        /// Eine Fehler-Stanza an eine fremde Domain darf keinen weiteren
        /// Fehler auslösen.
        /// </summary>
        /// <remarks>
        /// RFC 6120, Abschnitt 8.3.1: auf einen Fehler folgt nie ein Fehler.
        /// Täte er es, könnten zwei Server sich gegenseitig Fehlermeldungen
        /// zuschieben, bis einer aufgibt.
        /// </remarks>
        [Test]
        public async Task ErrorStanza_IsNotAnsweredAgain()
        {

            var client   = await ConnectClientAsync();
            var session  = Server.SessionOf(client.FullJid)!;

            var vorher = session.Sent.Count;

            await client.SendRawAsync(
                "<message type='error' to='bob@anderswo.example' id='schon-fehler'>" +
                "<error type='cancel'><service-unavailable xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></error>" +
                "</message>");

            await WaitAgainst(() => session.Sent.Skip(vorher).Any(f => f.Contains("schon-fehler", StringComparison.Ordinal)),
                              "eine Antwort auf eine Fehler-Stanza");

        }

        #endregion

        #region LocalDelivery_IsUnaffected()

        /// <summary>
        /// Die Gegenprobe: an die eigene Domain wird weiterhin zugestellt und
        /// eben kein Fehler erzeugt.
        /// </summary>
        [Test]
        public async Task LocalDelivery_IsUnaffected()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var empfangen  = new List<String>();
            var fehler     = new List<StanzaError>();

            bob.OnMessage       += m => empfangen.Add(m.Body);
            alice.OnStanzaError += (_, e) => fehler.Add(e);

            await alice.SendMessageAsync(bob.BareJid, "Hallo Bob!");

            await WaitFor(() => empfangen.Count > 0, "die lokal zugestellte Nachricht");

            Assert.That(fehler, Is.Empty, "Eine lokale Zustellung darf keinen Fehler erzeugen.");

        }

        #endregion

        #region UnknownLocalAccount_IsStillDroppedSilently()

        /// <summary>
        /// Ein unbekanntes Konto auf der <b>eigenen</b> Domain bleibt
        /// unbeantwortet - das ist eine andere Frage als eine unerreichbare
        /// Domain und wird hier nicht mitverändert.
        /// </summary>
        /// <remarks>
        /// RFC 6121, Abschnitt 8.1 verlangte hier <c>&lt;service-unavailable/&gt;</c>.
        /// Der Test hält den heutigen Stand fest, damit die Domain-Weiche ihn
        /// nicht unbemerkt mitverschiebt; die Lücke selbst steht im
        /// Arbeitsplan.
        /// </remarks>
        [Test]
        public async Task UnknownLocalAccount_IsStillDroppedSilently()
        {

            var client  = await ConnectClientAsync();
            var fehler  = new List<StanzaError>();

            client.OnStanzaError += (_, e) => fehler.Add(e);

            await client.SendMessageAsync($"niemand@{Server.Domain}", "Hallo?");

            await WaitAgainst(() => fehler.Count > 0, "ein Fehler für ein lokales Konto");

        }

        #endregion

    }

}
