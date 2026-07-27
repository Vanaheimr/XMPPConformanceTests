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
    /// Dasselbe Zielbild wie in <see cref="FederationTests"/> - zwei Server,
    /// zwei Clients, eine Nachricht über die Domain-Grenze -, diesmal aber
    /// über ein echtes Netz: <see cref="WebSocketServerLinks"/> statt
    /// <see cref="DirectServerLinks"/>.
    /// </summary>
    /// <remarks>
    /// Der Unterschied zu <see cref="FederationTests"/> ist genau die Zeile
    /// im Setup, die die beiden Server verbindet. Alles andere - Routing,
    /// Adressierung, Absenderprüfung - ist bereits dort geprüft und muss hier
    /// nicht noch einmal geprüft werden; hier geht es um den Transport
    /// selbst: kommt eine Stanza wirklich über einen Socket, durch TLS,
    /// zweimal auseinandergefaltet (WebSocket-Rahmen, dann S2S-Rahmen) und
    /// wieder zusammen an.
    /// </remarks>
    [TestFixture]
    public class WebSocketFederationTests
    {

        #region Data

        private XMPPServer _links = null!;
        private XMPPServer _rechts = null!;
        private WebSocketServerLinks _linksLinks = null!;
        private WebSocketServerLinks _rechtsLinks = null!;
        private readonly List<XMPPClient> _clients = [];

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void ZweiServer()
        {

            _links   = new XMPPServer("links.example");
            _rechts  = new XMPPServer("rechts.example");

            _links.Start();
            _rechts.Start();

            WebSocketServerLinks.Connect(_links, _rechts);

            _linksLinks   = (WebSocketServerLinks) _links.ServerLinks!;
            _rechtsLinks  = (WebSocketServerLinks) _rechts.ServerLinks!;

        }

        [TearDown]
        public async Task Abraeumen()
        {

            foreach (var client in _clients)
            {
                try { await client.DisposeAsync(); }
                catch { /* im Teardown egal */ }
            }

            _clients.Clear();

            await _linksLinks.DisposeAsync();
            await _rechtsLinks.DisposeAsync();

            await _links.DisposeAsync();
            await _rechts.DisposeAsync();

        }

        #endregion

        #region Hilfsfunktionen

        private async Task<XMPPClient> ConnectAsync(XMPPServer server, String localPart)
        {

            if (server.GetAccount($"{localPart}@{server.Domain}") is null)
                server.AddAccount(localPart);

            var connection = new XMPPConnection($"{localPart}@{server.Domain}",
                                                "pw",
                                                server.Uri)
            {
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

        #endregion


        #region MessageCrossesTheDomainBoundaryOverWebSocket()

        /// <summary>
        /// Der Kern: eine Nachricht geht durch zwei echte Server, verbunden
        /// über einen echten WebSocket-S2S-Link.
        /// </summary>
        [Test]
        public async Task MessageCrossesTheDomainBoundaryOverWebSocket()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            await alice.SendMessageAsync(bob.BareJid, "Hallo über den echten Draht!");

            await WarteAuf(() => empfangen.Count > 0, "die Nachricht auf dem anderen Server");

            Assert.Multiple(() =>
            {
                Assert.That(empfangen[0].Body,         Is.EqualTo("Hallo über den echten Draht!"));
                Assert.That(empfangen[0].FromBareJid,  Is.EqualTo("alice@links.example"));
            });

        }

        #endregion

        #region TheAnswerFindsItsWayBackOverWebSocket()

        /// <summary>
        /// Zurück läuft die Antwort über den zweiten, unabhängig aufgebauten
        /// Link in Gegenrichtung.
        /// </summary>
        [Test]
        public async Task TheAnswerFindsItsWayBackOverWebSocket()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var beiBob    = new List<XMPPMessage>();
            var beiAlice  = new List<XMPPMessage>();

            bob.OnMessage    += m => beiBob.Add(m);
            alice.OnMessage  += m => beiAlice.Add(m);

            await alice.SendMessageAsync(bob.BareJid, "Frage");
            await WarteAuf(() => beiBob.Count > 0, "die Frage bei Bob");

            await bob.SendMessageAsync(beiBob[0].FromBareJid, "Antwort");
            await WarteAuf(() => beiAlice.Count > 0, "die Antwort bei Alice");

            Assert.That(beiAlice[0].Body, Is.EqualTo("Antwort"));

        }

        #endregion

        #region SeveralMessagesReuseTheSameConnection()

        /// <summary>
        /// Zweite und dritte Nachricht bauen keine neue Verbindung mehr auf -
        /// der Verbindungs-Cache greift.
        /// </summary>
        [Test]
        public async Task SeveralMessagesReuseTheSameConnection()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            await alice.SendMessageAsync(bob.BareJid, "eins");
            await WarteAuf(() => empfangen.Count == 1, "die erste Nachricht");

            await alice.SendMessageAsync(bob.BareJid, "zwei");
            await alice.SendMessageAsync(bob.BareJid, "drei");
            await WarteAuf(() => empfangen.Count == 3, "alle drei Nachrichten");

            Assert.That(_rechtsLinks.InboundConnectionCount, Is.EqualTo(1),
                        "Auf der Empfängerseite darf nur eine S2S-Verbindung angekommen sein.");

        }

        #endregion

        #region ADomainWithoutAPeer_StillYieldsAnError()

        /// <summary>
        /// Eine unbekannte Domain führt weiterhin zum Fehler, jetzt über den
        /// echten Transport statt über <see cref="DirectServerLinks"/>.
        /// </summary>
        [Test]
        public async Task ADomainWithoutAPeer_StillYieldsAnError()
        {

            var alice   = await ConnectAsync(_links, "alice");
            var fehler  = new List<StanzaError>();

            alice.OnStanzaError += (_, e) => fehler.Add(e);

            await alice.SendMessageAsync("wer@ganzwoanders.example", "Hallo?");

            await WarteAuf(() => fehler.Count > 0, "den Fehler zur unbekannten Domain");

            Assert.That(fehler[0].Condition, Is.EqualTo("remote-server-not-found"));

        }

        #endregion

        #region SpoofedSender_IsRejectedAndEndsTheStream()

        /// <summary>
        /// Über den echten Transport hat die Absenderprüfung jetzt eine
        /// Konsequenz, die <see cref="DirectServerLinks"/> nicht bieten
        /// konnte: der Stream endet, und die Verbindung wird abgebaut statt
        /// als Leiche weiterzuhängen.
        /// </summary>
        /// <remarks>
        /// <see cref="WebSocketServerLinks.DeliverAsync"/> meldet nur, ob der
        /// Rahmen auf einen offenen Stream geschrieben wurde - für eine
        /// echte S2S-Verbindung gibt es kein synchrones "angekommen und
        /// akzeptiert" je Stanza, das wäre XEP-0198 und keine Eigenschaft von
        /// S2S. Beobachtbar ist die Ablehnung deshalb nicht am Rückgabewert,
        /// sondern daran, dass der Client die Nachricht nie sieht und dass
        /// die nächste Zustellung an dieselbe Domain eine neue Verbindung
        /// aufbaut, weil die alte tot ist.
        /// </remarks>
        [Test]
        public async Task SpoofedSender_IsRejectedAndEndsTheStream()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var empfangen  = new List<XMPPMessage>();
            var abgewiesen = new List<(String Peer, String Grund)>();

            bob.OnMessage                  += m => empfangen.Add(m);
            _rechts.OnRemoteStanzaRejected += (peer, grund) => abgewiesen.Add((peer, grund));

            // links.example baut regulär auf (die Verbindung weist sich also
            // korrekt als "links.example" aus), behauptet in der Stanza selbst
            // aber, für eine dritte Domain zu sprechen.
            await _linksLinks.DeliverAsync(
                "rechts.example",
                $"<message from='chef@bank.example' to='{bob.BareJid}' type='chat'>" +
                "<body>Bitte überweisen Sie 10000 Euro.</body></message>");

            await WarteAuf(() => abgewiesen.Count > 0, "die Abweisung durch die Absenderprüfung");

            Assert.That(empfangen, Is.Empty, "Die gefälschte Nachricht darf den Client nicht erreichen.");

            // Der Stream von vorhin ist zu. Eine echte Nachricht muss trotzdem
            // ankommen - über eine neu aufgebaute Verbindung.
            await alice.SendMessageAsync(bob.BareJid, "Trotzdem da.");
            await WarteAuf(() => empfangen.Count > 0, "die echte Nachricht nach dem Stream-Fehler");

            Assert.Multiple(() =>
            {
                Assert.That(empfangen[0].FromBareJid, Is.EqualTo("alice@links.example"));
                Assert.That(_rechtsLinks.InboundConnectionCount, Is.EqualTo(2),
                            "Nach dem Stream-Fehler muss die nächste Zustellung eine neue Verbindung aufbauen.");
            });

        }

        #endregion

    }

}
