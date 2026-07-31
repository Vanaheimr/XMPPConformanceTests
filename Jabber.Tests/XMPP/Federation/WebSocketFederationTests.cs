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

using System.Net.WebSockets;
using System.Text;

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
        private readonly InternalErrorGuard _guard = new();

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void ZweiServer()
        {

            // Die Wache an beide: Ein Fehler auf dem einen Server entsteht oft
            // durch eine Stanza, die der andere geschickt hat.
            _guard.Reset();

            _links   = _guard.Watched(new XMPPServer("links.example"));
            _rechts  = _guard.Watched(new XMPPServer("rechts.example"));

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

            _guard.AssertClean();

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

        /// <summary>
        /// Ein Hochstapler: verbindet sich von Hand mit dem S2S-Eingang eines
        /// Servers und behauptet, für eine fremde Domain zu sprechen.
        /// </summary>
        /// <remarks>
        /// Bewusst zu Fuss über einen rohen <see cref="ClientWebSocket"/> und
        /// nicht über <see cref="WebSocketServerLinks"/>: dessen ausgehender
        /// Zweig würde brav einen richtigen Schlüssel erzeugen. Ein Angreifer
        /// hat das Geheimnis der Domain aber gerade nicht - genau das soll
        /// geprüft werden.
        /// </remarks>
        private sealed class Hochstapler : IAsyncDisposable
        {

            private readonly ClientWebSocket _socket = new();

            public List<String> Empfangen { get; } = [];

            public async Task VerbindeAsync(WebSocketServerLinks ziel, XMPPServer zielServer)
            {

                _socket.Options.AddSubProtocol("xmpp-server");
                _socket.Options.RemoteCertificateValidationCallback = zielServer.IsOwnCertificate;

                await _socket.ConnectAsync(new Uri(ziel.Uri), CancellationToken.None);

                _ = LiesAsync();

            }

            public async Task SendeAsync(String rahmen)
                => await _socket.SendAsync(Encoding.UTF8.GetBytes(rahmen),
                                           WebSocketMessageType.Text, true, CancellationToken.None);

            private async Task LiesAsync()
            {

                var puffer = new Byte[8192];

                try
                {
                    while (_socket.State == WebSocketState.Open)
                    {

                        var ergebnis = await _socket.ReceiveAsync(puffer, CancellationToken.None);

                        if (ergebnis.MessageType == WebSocketMessageType.Close)
                            break;

                        lock (Empfangen)
                            Empfangen.Add(Encoding.UTF8.GetString(puffer, 0, ergebnis.Count));

                    }
                }
                catch (Exception)
                {
                    // Verbindung zu - im Test der erwartete Ausgang.
                }

            }

            public Boolean Sah(String text)
            {
                lock (Empfangen)
                    return Empfangen.Any(f => f.Contains(text, StringComparison.Ordinal));
            }

            public ValueTask DisposeAsync()
            {
                try { _socket.Dispose(); } catch { /* egal */ }
                return ValueTask.CompletedTask;
            }

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
        /// <remarks>
        /// Geprüft wird, dass die Zahl der Verbindungen nicht <b>wächst</b>,
        /// nicht dass sie einen bestimmten Wert hat. Wie viele der erste
        /// Austausch braucht, hängt daran, was sonst noch über die Grenze geht:
        /// mit Dialback kommt je Richtung eine Verifikationsverbindung dazu,
        /// und Bobs automatische Empfangsbestätigung (XEP-0184/0333) baut die
        /// Gegenrichtung gleich mit auf. Eine feste Zahl hier festzuschreiben
        /// hiesse, den Test bei jeder solchen Änderung nachzuziehen, ohne dass
        /// er dadurch mehr über die Wiederverwendung aussagte.
        /// </remarks>
        [Test]
        public async Task SeveralMessagesReuseTheSameConnection()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            await alice.SendMessageAsync(bob.BareJid, "eins");
            await WarteAuf(() => empfangen.Count == 1, "die erste Nachricht");

            // Der Gegenrichtung Zeit lassen, sich ebenfalls aufzubauen -
            // sonst zählte der Vergleich unten eine Verbindung mit, die
            // ohnehin gerade erst entstand.
            await Task.Delay(TimeSpan.FromSeconds(1));

            var nachDemAufbau = _rechtsLinks.InboundConnectionCount;

            await alice.SendMessageAsync(bob.BareJid, "zwei");
            await alice.SendMessageAsync(bob.BareJid, "drei");
            await WarteAuf(() => empfangen.Count == 3, "alle drei Nachrichten");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(nachDemAufbau, Is.GreaterThan(0),
                            "Der erste Austausch muss überhaupt eine Verbindung gebraucht haben.");
                Assert.That(_rechtsLinks.InboundConnectionCount, Is.EqualTo(nachDemAufbau),
                            "Weitere Nachrichten dürfen keine neue S2S-Verbindung aufbauen.");
            });

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

        #region AnImpostorWithoutTheSecret_FailsDialback()

        /// <summary>
        /// Der Punkt von Dialback: wer die Domain nur behauptet, kommt nicht
        /// durch (XEP-0220).
        /// </summary>
        /// <remarks>
        /// Der Hochstapler baut regulär auf und legt einen selbst erfundenen
        /// Schlüssel für <c>links.example</c> vor. Der annehmende Server fragt
        /// daraufhin nicht ihn, sondern die Adresse, die <b>er selbst</b> für
        /// <c>links.example</c> hinterlegt hat - und der echte
        /// <c>links.example</c> kennt den Schlüssel nicht. Genau darauf beruht
        /// das Verfahren: die Prüfung fragt nie den, der geprüft wird.
        /// </remarks>
        [Test]
        public async Task AnImpostorWithoutTheSecret_FailsDialback()
        {

            var bob = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            await using var boese = new Hochstapler();
            await boese.VerbindeAsync(_rechtsLinks, _rechts);

            await boese.SendeAsync(
                "<open xmlns='urn:ietf:params:xml:ns:xmpp-framing' " +
                "from='links.example' to='rechts.example' version='1.0'/>");

            await WarteAuf(() => boese.Sah("<open"), "den Stream-Kopf der Gegenstelle");

            // Ein frei erfundener Schlüssel - das Geheimnis von links.example
            // hat der Angreifer nicht.
            await boese.SendeAsync(
                "<db:result xmlns:db='jabber:server:dialback' " +
                "from='links.example' to='rechts.example'>" +
                "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff" +
                "</db:result>");

            await WarteAuf(() => boese.Sah("db:result") && boese.Sah("type="),
                           "die Dialback-Antwort");

            // Und der Versuch, trotzdem zuzustellen.
            await boese.SendeAsync(
                $"<message from='alice@links.example' to='{bob.BareJid}' type='chat'>" +
                "<body>Durchgerutscht?</body></message>");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(boese.Sah("type='invalid'"), Is.True,
                            "Der erfundene Schlüssel muss als ungültig zurückkommen.");
                Assert.That(boese.Sah("type='valid'"),   Is.False);
                Assert.That(empfangen, Is.Empty,
                            "Ohne bestandenes Dialback darf keine Stanza zugestellt werden.");
            });

        }

        #endregion

        #region AnImpostorForAnUnknownDomain_CannotBeVerifiedAtAll()

        /// <summary>
        /// Für eine Domain, zu der es keine hinterlegte Adresse gibt, kann
        /// niemand gefragt werden - also wird auch nichts angenommen.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe zum vorigen Test: dort scheiterte der Schlüssel,
        /// hier scheitert schon die Möglichkeit zu prüfen. Beides muss zur
        /// Ablehnung führen, sonst wäre die unbekannte Domain der bequemere
        /// Weg hinein.
        /// </remarks>
        [Test]
        public async Task AnImpostorForAnUnknownDomain_CannotBeVerifiedAtAll()
        {

            var bob = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            await using var boese = new Hochstapler();
            await boese.VerbindeAsync(_rechtsLinks, _rechts);

            await boese.SendeAsync(
                "<open xmlns='urn:ietf:params:xml:ns:xmpp-framing' " +
                "from='niemand.example' to='rechts.example' version='1.0'/>");

            await WarteAuf(() => boese.Sah("<open"), "den Stream-Kopf der Gegenstelle");

            await boese.SendeAsync(
                "<db:result xmlns:db='jabber:server:dialback' " +
                "from='niemand.example' to='rechts.example'>" +
                "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff" +
                "</db:result>");

            await WarteAuf(() => boese.Sah("db:result") && boese.Sah("type="),
                           "die Dialback-Antwort");

            await boese.SendeAsync(
                $"<message from='wer@niemand.example' to='{bob.BareJid}' type='chat'>" +
                "<body>Und so?</body></message>");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(boese.Sah("type='invalid'"), Is.True);
                Assert.That(empfangen, Is.Empty);
            });

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

            await Task.Delay(TimeSpan.FromSeconds(1));

            var vorDerEchten = _rechtsLinks.InboundConnectionCount;

            // Der Stream von vorhin ist zu. Eine echte Nachricht muss trotzdem
            // ankommen - über eine neu aufgebaute Verbindung.
            await alice.SendMessageAsync(bob.BareJid, "Trotzdem da.");
            await WarteAuf(() => empfangen.Count > 0, "die echte Nachricht nach dem Stream-Fehler");

            Assert.Multiple(() =>
            {
                Assert.That(empfangen[0].FromBareJid, Is.EqualTo("alice@links.example"));
                Assert.That(_rechtsLinks.InboundConnectionCount, Is.GreaterThan(vorDerEchten),
                            "Nach dem Stream-Fehler muss die nächste Zustellung eine neue Verbindung aufbauen.");
            });

        }

        #endregion

    }

}
