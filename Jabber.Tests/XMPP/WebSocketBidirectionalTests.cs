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
    /// XEP-0288 über den WebSocket-Transport.
    /// </summary>
    /// <remarks>
    /// Die Protokollschicht ist dieselbe wie über TCP und dort schon geprüft
    /// (<see cref="BidirectionalStreamTests"/>). Hier geht es allein darum,
    /// dass dieser Transport die Aushandlung auch anstösst und die Rückrichtung
    /// wirklich benutzt - beides sind Zeilen in
    /// <see cref="WebSocketServerLinks"/> und nicht in <see cref="S2SStream"/>.
    ///
    /// <b>Ein Unterschied zum TCP-Aufbau, und er ist eingestanden:</b> dort
    /// kennt die Gegenstelle uns nicht, und ohne Rückrichtung geht die Antwort
    /// verloren. Das ginge hier nicht - der WebSocket-Weg weist sich
    /// ausschliesslich über Dialback aus (SASL-EXTERNAL gibt es hier nicht),
    /// und dessen Rückfrage braucht genau die Richtung, die es dann nicht
    /// gäbe. Beide Seiten sind hier also eingetragen, und die Antwort käme auch
    /// ohne Bidi an. Deshalb prüfen diese Tests
    /// <see cref="WebSocketServerLinks.BidirectionalDeliveryCount"/> und nicht
    /// die Ankunft: nur die Zahl sagt, welchen Weg sie genommen hat.
    /// </remarks>
    [TestFixture]
    public class WebSocketBidirectionalTests
    {

        #region Data

        private XMPPServer            _links   = null!;
        private XMPPServer            _rechts  = null!;
        private WebSocketServerLinks  _linksS  = null!;
        private WebSocketServerLinks  _rechtsS = null!;

        private readonly List<XMPPClient> _clients = [];

        #endregion

        #region TearDown

        [TearDown]
        public async Task Abraeumen()
        {

            foreach (var client in _clients)
            {
                try { await client.DisposeAsync(); } catch { /* im Teardown egal */ }
            }

            _clients.Clear();

            if (_linksS  is not null) await _linksS.DisposeAsync();
            if (_rechtsS is not null) await _rechtsS.DisposeAsync();

            if (_links   is not null) await _links.DisposeAsync();
            if (_rechts  is not null) await _rechts.DisposeAsync();

        }

        #endregion

        #region Hilfsfunktionen

        private void Verkabeln(Boolean bidi)
        {

            _links   = new XMPPServer("links.example");
            _rechts  = new XMPPServer("rechts.example");

            _links.Start();
            _rechts.Start();

            _linksS   = new WebSocketServerLinks(_links)  { OfferBidirectionalStreams = bidi, RequestBidirectionalStreams = bidi };
            _rechtsS  = new WebSocketServerLinks(_rechts) { OfferBidirectionalStreams = bidi, RequestBidirectionalStreams = bidi };

            _linksS.AddPeer(_rechts.Domain, _rechtsS.Uri, _rechts.IsOwnCertificate);
            _rechtsS.AddPeer(_links.Domain, _linksS.Uri,  _links.IsOwnCertificate);

        }

        private async Task<XMPPClient> ConnectAsync(XMPPServer server, String localPart)
        {

            if (server.GetAccount($"{localPart}@{server.Domain}") is null)
                server.AddAccount(localPart);

            var connection = new XMPPConnection($"{localPart}@{server.Domain}", "pw", server.Uri) {
                                 KeepaliveEnabled            = false,
                                 MaxReconnectAttempts        = 0,
                                 ServerCertificateValidator  = server.IsOwnCertificate
                             };

            var client = new XMPPClient(connection);
            _clients.Add(client);

            await client.ConnectAsync();

            return client;

        }

        private static async Task WarteAuf(Func<Boolean> bedingung, String was)
        {
            Assert.That(await XMPPServer.WaitUntilAsync(bedingung),
                        Is.True, $"Zeitüberschreitung beim Warten auf: {was}");
        }

        /// <summary>
        /// Schickt hin und zurück und liefert, was bei Alice ankam.
        /// </summary>
        private async Task<List<String>> HinUndZurueckAsync()
        {

            var alice  = await ConnectAsync(_links,  "alice");
            var juliet = await ConnectAsync(_rechts, "juliet");

            _links.GetAccount(alice.BareJid)!.SetRosterEntry(new RosterEntry(juliet.BareJid, null, "both"));
            _rechts.GetAccount(juliet.BareJid)!.SetRosterEntry(new RosterEntry(alice.BareJid, null, "both"));

            var beiJuliet = new List<String>();
            var beiAlice  = new List<String>();

            juliet.OnMessage += m => beiJuliet.Add(m.Body ?? "");
            alice.OnMessage  += m => beiAlice.Add(m.Body ?? "");

            await alice.SendMessageAsync(juliet.BareJid, "Hin");
            await WarteAuf(() => beiJuliet.Count > 0, "die Nachricht bei Juliet");

            await juliet.SendMessageAsync(alice.BareJid, "Zurück");
            await WarteAuf(() => beiAlice.Any(b => b == "Zurück"), "die Antwort bei Alice");

            return beiAlice;

        }

        #endregion


        #region TheAnswerTakesTheReturnPath()

        /// <summary>
        /// Mit XEP-0288 nimmt die Antwort die bestehende Verbindung.
        /// </summary>
        /// <remarks>
        /// Geprüft wird die Seite, die angewählt <i>wurde</i>. Dass die andere
        /// Seite ihre Rückrichtung nicht benutzt, wäre die naheliegende
        /// Gegenprobe - sie stimmt aber nicht: sobald auch nur eine Stanza in
        /// die Gegenrichtung läuft (schon eine Empfangsbestätigung nach
        /// XEP-0184 genügt), wählt die Gegenstelle ihrerseits an, und dann hat
        /// auch die erste Seite eine eingehende Verbindung, die sie fortan
        /// bevorzugt. Zwei sich gegenseitig kennende Server fallen unter Bidi
        /// also auf die Verbindungen zusammen, die sie ohnehin schon haben -
        /// genau der Zweck der Erweiterung, aber nichts, worauf sich eine
        /// zeitunabhängige Zusicherung stützen liesse.
        /// </remarks>
        [Test]
        public async Task TheAnswerTakesTheReturnPath()
        {

            Verkabeln(bidi: true);

            await HinUndZurueckAsync();

            Assert.That(_rechtsS.BidirectionalDeliveryCount, Is.GreaterThan(0),
                        "Die Antwort kam an, aber über eine eigene Verbindung.");

        }

        #endregion

        #region WithoutBidi_TheAnswerTakesItsOwnConnection()

        /// <summary>
        /// Ohne die Erweiterung wählt die Gegenstelle an - die Antwort kommt
        /// an, aber auf dem anderen Weg.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe zum Zähler. Ohne sie bewiese eine Zahl grösser null
        /// nichts, denn sie könnte auch ohne die Erweiterung zustande kommen -
        /// und die Ankunft allein unterscheidet die beiden Wege überhaupt
        /// nicht.
        /// </remarks>
        [Test]
        public async Task WithoutBidi_TheAnswerTakesItsOwnConnection()
        {

            Verkabeln(bidi: false);

            var beiAlice = await HinUndZurueckAsync();

            Assert.Multiple(() =>
            {
                Assert.That(beiAlice, Does.Contain("Zurück"),
                            "Ohne Bidi muss der gewöhnliche Weg weiter tragen.");
                Assert.That(_rechtsS.BidirectionalDeliveryCount, Is.Zero);
            });

        }

        #endregion

        #region AStanzaForAnUnknownDomain_DoesNotTakeSomeoneElsesReturnPath()

        /// <summary>
        /// Eine Stanza an eine dritte Domain darf nicht über die
        /// Rückrichtung einer anderen hinausgehen.
        /// </summary>
        /// <remarks>
        /// Der Abgleich der Domain steht seit S9b in
        /// <c>S2SStream.TryDeliverOverBidiAsync</c> und gilt damit für beide
        /// Transporte - aber "gilt gemeinsam" ist eine Behauptung über den
        /// Aufbau des Codes, und die prüft kein Test des anderen Transports.
        ///
        /// Hier ohne dritten Server: eine Domain, die niemand kennt, hat keine
        /// Verbindung und keine Rückrichtung. Ginge die Stanza trotzdem
        /// hinaus, hätte sie die Verbindung von <c>rechts</c> genommen.
        /// </remarks>
        [Test]
        public async Task AStanzaForAnUnknownDomain_DoesNotTakeSomeoneElsesReturnPath()
        {

            Verkabeln(bidi: true);

            await HinUndZurueckAsync();

            Assert.That(_rechtsS.BidirectionalDeliveryCount, Is.GreaterThan(0),
                        "Aufbau des Tests: es gibt eine benutzte Rückrichtung.");

            var vorher = _rechtsS.BidirectionalDeliveryCount;

            var ging = await _rechtsS.DeliverAsync(
                           "woanders.example",
                           "<message from='juliet@rechts.example' to='romeo@woanders.example'/>");

            Assert.Multiple(() =>
            {
                Assert.That(ging, Is.False,
                            "Für eine unbekannte Domain gibt es keinen Weg.");
                Assert.That(_rechtsS.BidirectionalDeliveryCount, Is.EqualTo(vorher),
                            "Und schon gar nicht die Rückrichtung einer anderen.");
            });

        }

        #endregion

    }

}
