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
    /// Das Zielbild aus dem Arbeitsplan: zwei Server mit verschiedenen
    /// Domains, an jedem ein echter Client, und eine Nachricht geht von einem
    /// zum anderen.
    ///
    /// Verbunden sind die beiden über <see cref="DirectServerLinks"/>, also
    /// ohne Netz dazwischen. Was hier <b>nicht</b> geprüft wird, ist deshalb
    /// genauso wichtig wie das, was geprüft wird: es gibt keinen Stream, kein
    /// TLS zwischen den Servern, keinen Dialback. Geprüft wird Routing,
    /// Adressierung und Zustellung über die Domain-Grenze - und die
    /// Absenderprüfung, auf die ein echter Transport später baut.
    /// </summary>
    [TestFixture]
    public class FederationTests
    {

        #region Data

        private XMPPServer _links = null!;
        private XMPPServer _rechts = null!;
        private readonly List<XMPPClient> _clients = [];
        private readonly InternalErrorGuard _guard = new();

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void ZweiServer()
        {

            _links   = new XMPPServer("links.example");
            _rechts  = new XMPPServer("rechts.example");

            // Die Wache an beide: Ein Fehler auf dem einen Server entsteht oft
            // durch eine Stanza, die der andere geschickt hat.
            _guard.Reset();
            _guard.Watch(_links);
            _guard.Watch(_rechts);

            _links.Start();
            _rechts.Start();

            DirectServerLinks.Connect(_links, _rechts);

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

            await _links.DisposeAsync();
            await _rechts.DisposeAsync();

            _guard.AssertClean();

        }

        #endregion

        #region Hilfsfunktionen

        /// <summary>Verbindet einen Client mit einem der beiden Server.</summary>
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

        /// <summary>
        /// Trägt einen Kontakt über die Domain-Grenze hinweg in beide Roster
        /// ein, wie es nach einem vollständigen Subscription-Handshake wäre.
        /// </summary>
        private void MacheKontakte(XMPPClient a, XMPPClient b)
        {

            _links.GetAccount(a.BareJid)!.SetRosterEntry(new RosterEntry(b.BareJid, null, "both"));
            _rechts.GetAccount(b.BareJid)!.SetRosterEntry(new RosterEntry(a.BareJid, null, "both"));

        }

        private static async Task WarteAuf(Func<Boolean> bedingung, String was)
        {
            Assert.That(await XMPPServer.WaitUntilAsync(bedingung),
                        Is.True, $"Zeitüberschreitung beim Warten auf: {was}");
        }

        #endregion


        #region MessageCrossesTheDomainBoundary()

        /// <summary>
        /// Der Kern des ganzen Punktes.
        /// </summary>
        [Test]
        public async Task MessageCrossesTheDomainBoundary()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            await alice.SendMessageAsync(bob.BareJid, "Hallo über die Grenze!");

            await WarteAuf(() => empfangen.Count > 0, "die Nachricht auf dem anderen Server");

            Assert.Multiple(() =>
            {
                Assert.That(empfangen[0].Body,         Is.EqualTo("Hallo über die Grenze!"));
                Assert.That(empfangen[0].FromBareJid,  Is.EqualTo("alice@links.example"));
            });

        }

        #endregion

        #region TheAnswerFindsItsWayBack()

        /// <summary>
        /// Und zurück - die Richtung ist nicht dieselbe Frage, weil sie über
        /// die zweite Hälfte der Verkabelung läuft.
        /// </summary>
        [Test]
        public async Task TheAnswerFindsItsWayBack()
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

            Assert.Multiple(() =>
            {
                Assert.That(beiAlice[0].Body,         Is.EqualTo("Antwort"));
                Assert.That(beiAlice[0].FromBareJid,  Is.EqualTo("bob@rechts.example"));
            });

        }

        #endregion

        #region PresenceCrossesTheBoundary()

        /// <summary>
        /// Presence geht denselben Weg - an Kontakte mit <c>from</c> oder
        /// <c>both</c>, gleich auf welcher Domain sie liegen (RFC 6121,
        /// Abschnitt 4.2.2).
        /// </summary>
        [Test]
        public async Task PresenceCrossesTheBoundary()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            MacheKontakte(alice, bob);

            var gesehen = new List<(String From, String Type)>();
            bob.OnPresenceChanged += (from, type) => gesehen.Add((from, type));

            await alice.SetPresenceAsync();

            await WarteAuf(() => gesehen.Any(g => g.From.StartsWith("alice@links.example", StringComparison.Ordinal)),
                           "Alices Presence bei Bob");

        }

        #endregion

        #region PresenceStaysAwayFromNonSubscribers()

        /// <summary>
        /// Die Gegenprobe: ohne Berechtigung geht auch über die Grenze nichts.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test bestünde der vorige auch dann, wenn der Server
        /// Presence einfach an jede fremde Domain schickte, die er kennt.
        /// </remarks>
        [Test]
        public async Task PresenceStaysAwayFromNonSubscribers()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            // Kein MacheKontakte: die beiden kennen einander nicht.

            var gesehen = new List<String>();
            bob.OnPresenceChanged += (from, _) => gesehen.Add(from);

            await alice.SetPresenceAsync();

            var kam = await XMPPServer.WaitUntilAsync(
                          () => gesehen.Any(f => f.StartsWith("alice@links.example", StringComparison.Ordinal)),
                          TimeSpan.FromSeconds(2));

            Assert.That(kam, Is.False,
                        "Presence darf nur an Kontakte mit from oder both gehen.");

        }

        #endregion

        #region SpoofedSender_IsRejected()

        /// <summary>
        /// Eine Gegenstelle darf ausschliesslich für ihre eigene Domain
        /// sprechen.
        /// </summary>
        /// <remarks>
        /// Das ist die Prüfung, um derentwillen es Dialback überhaupt gibt.
        /// Ohne sie könnte jeder Server, mit dem man je spricht, Nachrichten
        /// im Namen jedes beliebigen anderen einschleusen - und ein Client
        /// hätte keine Möglichkeit, das zu bemerken.
        /// </remarks>
        [Test]
        public async Task SpoofedSender_IsRejected()
        {

            var bob = await ConnectAsync(_rechts, "bob");

            var empfangen  = new List<XMPPMessage>();
            var abgewiesen = new List<(String Peer, String Grund)>();

            bob.OnMessage                  += m => empfangen.Add(m);
            _rechts.OnRemoteStanzaRejected += (peer, grund) => abgewiesen.Add((peer, grund));

            // links.example behauptet, für eine dritte Domain zu sprechen.
            var angenommen = await _rechts.ReceiveFromRemoteAsync(
                                 "links.example",
                                 $"<message from='chef@bank.example' to='{bob.BareJid}' type='chat'>" +
                                 "<body>Bitte überweisen Sie 10000 Euro.</body></message>");

            Assert.Multiple(() =>
            {
                Assert.That(angenommen, Is.False, "Die Stanza hätte abgewiesen werden müssen.");
                Assert.That(empfangen,  Is.Empty, "Sie darf den Client nicht erreichen.");
                Assert.That(abgewiesen, Is.Not.Empty, "Die Abweisung muss gemeldet werden.");
            });

        }

        #endregion

        #region RelayingForThirdParties_IsRejected()

        /// <summary>
        /// Und der Server leitet nicht für Dritte weiter - eine Stanza an eine
        /// Domain, die nicht seine ist, nimmt er gar nicht erst an.
        /// </summary>
        /// <remarks>
        /// Sonst wäre er ein offenes Relais: wer einmal verbunden ist, könnte
        /// über ihn jede andere Domain anschreiben und die Herkunft
        /// verschleiern.
        /// </remarks>
        [Test]
        public async Task RelayingForThirdParties_IsRejected()
        {

            var abgewiesen = new List<String>();
            _rechts.OnRemoteStanzaRejected += (_, grund) => abgewiesen.Add(grund);

            var angenommen = await _rechts.ReceiveFromRemoteAsync(
                                 "links.example",
                                 "<message from='alice@links.example' to='wer@ganzwoanders.example' type='chat'>" +
                                 "<body>Weiterreichen bitte.</body></message>");

            Assert.Multiple(() =>
            {
                Assert.That(angenommen, Is.False);
                Assert.That(abgewiesen, Is.Not.Empty);
            });

        }

        #endregion

        #region UnknownDomain_StillYieldsAnError()

        /// <summary>
        /// Eine Domain, zu der es keine Verbindung gibt, führt weiterhin zum
        /// Fehler - auch wenn es andere Verbindungen gibt.
        /// </summary>
        [Test]
        public async Task UnknownDomain_StillYieldsAnError()
        {

            var alice   = await ConnectAsync(_links, "alice");
            var fehler  = new List<StanzaError>();

            alice.OnStanzaError += (_, e) => fehler.Add(e);

            await alice.SendMessageAsync("wer@ganzwoanders.example", "Hallo?");

            await WarteAuf(() => fehler.Count > 0, "den Fehler zur unbekannten Domain");

            Assert.That(fehler[0].Condition, Is.EqualTo("remote-server-not-found"));

        }

        #endregion

        #region ConnectingAServerToItself_IsRefused()

        /// <summary>
        /// Zwei Server auf derselben Domain zu verbinden ergibt nichts und ist
        /// fast sicher ein Versehen.
        /// </summary>
        [Test]
        public async Task ConnectingAServerToItself_IsRefused()
        {

            await using var doppelt = new XMPPServer("links.example");

            Assert.Throws<ArgumentException>(() => DirectServerLinks.Connect(_links, doppelt));

        }

        #endregion

        #region AStanzaFromAbroad_ReachesTheClientAsJabberClient()

        /// <summary>
        /// Was von einem fremden Server hereinkommt, geht dem lokalen Client
        /// als <c>jabber:client</c> zu - nicht als <c>jabber:server</c>.
        /// </summary>
        /// <remarks>
        /// RFC 6120, Abschnitt 4.8.1 gibt jedem Stream seinen
        /// Content-Namensraum: <c>jabber:server</c> zwischen Servern,
        /// <c>jabber:client</c> auf der Client-Verbindung. Beim Übergang muss
        /// er mitwechseln. Die ausgehende Richtung war schon behoben, die
        /// eingehende blieb liegen - unser eigener Client stört sich nicht
        /// daran, weil er Stanzas am lokalen Namen erkennt und den Namensraum
        /// gar nicht ansieht.
        ///
        /// Genau diese Nachsicht hat den Fehler auf der anderen Seite
        /// jahrelang verdeckt: der Client schickte seine Stanzas ohne jeden
        /// Namensraum hinaus, und niemand hat es bemerkt, bis Prosody das
        /// Bind-IQ abwies.
        ///
        /// Geprüft wird auf dem Draht, nicht am Ereignis: was der Client daraus
        /// macht, ist eine andere Frage als das, was er bekommt.
        /// </remarks>
        [Test]
        public async Task AStanzaFromAbroad_ReachesTheClientAsJabberClient()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            MacheKontakte(alice, bob);

            await alice.SendMessageAsync(bob.BareJid, "Über die Grenze");

            var bobsSitzung = _rechts.SessionOf(bob.FullJid!)!;

            await WarteAuf(() => bobsSitzung.Sent.Any(f => f.Contains("Über die Grenze",
                                                                     StringComparison.Ordinal)),
                           "die zugestellte Nachricht");

            var zugestellt = bobsSitzung.Sent.First(f => f.Contains("Über die Grenze",
                                                                   StringComparison.Ordinal));

            Assert.Multiple(() =>
            {

                Assert.That(zugestellt, Does.Contain("xmlns='jabber:client'"),
                            "Die Stanza kam ohne den Namensraum der Client-Verbindung an.");

                Assert.That(zugestellt, Does.Not.Contain("jabber:server"),
                            "Der Namensraum der Serververbindung ist mit durchgereicht worden.");

            });

        }

        #endregion

        #region EverythingTheServerSendsCarriesTheClientNamespace()

        /// <summary>
        /// Auch was der Server selbst erzeugt, trägt den Namensraum.
        /// </summary>
        /// <remarks>
        /// Über TCP stünde er einmal am <c>&lt;stream:stream&gt;</c> und gälte
        /// für alles darin. Über WebSocket gibt es dieses Element nicht: jeder
        /// Rahmen muss für sich lesbar sein, „complete with all relevant
        /// namespace and language declarations" (RFC 7395, Abschnitt 3.3.3).
        ///
        /// Der Server hat bis hierher gar keinen geschickt - Presence,
        /// Roster-Pushes, Fehler-IQs, alles ohne. Aufgefallen ist es nie, weil
        /// unser Client sie am lokalen Namen erkennt. Ein fremder Client dürfte
        /// strenger sein, und wir wüssten erst dann davon.
        ///
        /// Nonzas bleiben aussen vor: sie bringen ihren eigenen Namensraum mit,
        /// und ein <c>&lt;enabled/&gt;</c> nach <c>jabber:client</c> umzuhängen
        /// machte es unlesbar.
        /// </remarks>
        [Test]
        public async Task EverythingTheServerSendsCarriesTheClientNamespace()
        {

            var alice = await ConnectAsync(_links, "alice");

            var sitzung = _links.SessionOf(alice.FullJid!)!;

            await WarteAuf(() => sitzung.Sent.Any(f => f.StartsWith("<iq", StringComparison.Ordinal)),
                           "irgendeine Stanza vom Server");

            var stanzas = sitzung.Sent.Where(f => f.StartsWith("<message",  StringComparison.Ordinal) ||
                                                  f.StartsWith("<presence", StringComparison.Ordinal) ||
                                                  f.StartsWith("<iq",       StringComparison.Ordinal))
                                      .ToList();

            var nonzas  = sitzung.Sent.Where(f => f.StartsWith("<open",     StringComparison.Ordinal) ||
                                                  f.StartsWith("<features", StringComparison.Ordinal) ||
                                                  f.StartsWith("<success",  StringComparison.Ordinal) ||
                                                  f.StartsWith("<enabled",  StringComparison.Ordinal))
                                      .ToList();

            Assert.Multiple(() =>
            {

                Assert.That(stanzas, Is.Not.Empty);

                foreach (var s in stanzas)
                    Assert.That(s, Does.Contain("xmlns='jabber:client'"),
                                $"Ohne Namensraum hinausgegangen: {s}");

                foreach (var n in nonzas)
                    Assert.That(n, Does.Not.Contain("jabber:client"),
                                $"Einer Nonza den Stanza-Namensraum aufgesetzt: {n}");

            });

        }

        #endregion

    }

}
