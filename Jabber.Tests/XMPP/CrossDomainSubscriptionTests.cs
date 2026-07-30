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
    /// Der Subscription-Handshake aus RFC 6121, Abschnitt 3 über eine
    /// Domain-Grenze hinweg.
    /// </summary>
    /// <remarks>
    /// Bisher ging das nicht: der Handshake nahm an, dass derselbe Server
    /// beide Roster in der Hand hat. Über die Grenze führt jede Seite nur
    /// ihre eigene Hälfte, und was die andere weiss, erfährt sie
    /// ausschliesslich aus dem, was ausdrücklich geschickt wird.
    ///
    /// Verbunden über <see cref="DirectServerLinks"/>: geprüft wird der
    /// Handshake, nicht der Transport. Dass Stanzas über echte Sockets,
    /// Dialback und TLS gehen, steht in den Transporttests.
    /// </remarks>
    [TestFixture]
    public class CrossDomainSubscriptionTests
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

        /// <param name="vorDemVerbinden">
        /// Wird gerufen, bevor die Verbindung steht - für Ereignisse, die
        /// sonst zwischen Anmeldung und Anhängen verlorengingen.
        /// </param>
        private async Task<XMPPClient> ConnectAsync(XMPPServer            server,
                                                    String                localPart,
                                                    Action<XMPPClient>?   vorDemVerbinden = null)
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

            vorDemVerbinden?.Invoke(client);

            await client.ConnectAsync();

            return client;

        }

        private static async Task WarteAuf(Func<Boolean> bedingung, String was)
        {
            Assert.That(await XMPPServer.WaitUntilAsync(bedingung),
                        Is.True, $"Zeitüberschreitung beim Warten auf: {was}");
        }

        /// <summary>Der Subscription-Zustand, wie ihn ein Server führt.</summary>
        private static String? Zustand(XMPPServer server, String konto, String kontakt)
            => server.GetAccount(konto)?.SubscriptionOf(kontakt);

        #endregion


        #region TheRequestReachesTheOtherDomain()

        /// <summary>
        /// Abschnitt 3.1.3: die Anfrage geht über die Grenze und erreicht den
        /// Kontakt.
        /// </summary>
        [Test]
        public async Task TheRequestReachesTheOtherDomain()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var anfragen = new List<String>();
            bob.OnSubscriptionRequest += (from, _) => anfragen.Add(from);

            await alice.AddContactAsync(bob.BareJid, "Bob");

            await WarteAuf(() => anfragen.Count > 0, "die Anfrage bei Bob");

            Assert.Multiple(() =>
            {
                Assert.That(anfragen[0], Is.EqualTo("alice@links.example"));

                // Abschnitt 3.1.2: beim Antragsteller ist die Anfrage offen.
                Assert.That(Zustand(_links, "alice@links.example", "bob@rechts.example"),
                            Is.EqualTo("none"));
            });

        }

        #endregion

        #region TheFullHandshakeSetsBothHalves()

        /// <summary>
        /// Der ganze Vorgang: Anfrage, Zustimmung, und beide Server führen
        /// danach die passende Hälfte.
        /// </summary>
        /// <remarks>
        /// Das ist der Kern. Jede Seite kennt nur ihre eigene Hälfte, und die
        /// Übereinstimmung entsteht allein daraus, dass beide dieselbe
        /// Stanzafolge verschieden auslegen - der eine setzt 'from', der
        /// andere 'to'.
        /// </remarks>
        [Test]
        public async Task TheFullHandshakeSetsBothHalves()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var anfragen = new List<String>();
            bob.OnSubscriptionRequest += (from, _) => anfragen.Add(from);

            await alice.AddContactAsync(bob.BareJid, "Bob");
            await WarteAuf(() => anfragen.Count > 0, "die Anfrage bei Bob");

            await bob.AcceptSubscriptionAsync(alice.BareJid);

            await WarteAuf(() => Zustand(_links, "alice@links.example", "bob@rechts.example") == "to",
                           "die 'to'-Hälfte bei Alice");

            Assert.Multiple(() =>
            {
                // Alice darf Bob sehen.
                Assert.That(Zustand(_links,  "alice@links.example", "bob@rechts.example"),
                            Is.EqualTo("to"));

                // Bob erlaubt Alice, ihn zu sehen.
                Assert.That(Zustand(_rechts, "bob@rechts.example",  "alice@links.example"),
                            Is.EqualTo("from"));
            });

        }

        #endregion

        #region AfterApproval_PresenceCrossesTheBoundary()

        /// <summary>
        /// Und danach fliesst Presence - ohne dass jemand den Roster von Hand
        /// gefüllt hätte.
        /// </summary>
        /// <remarks>
        /// Die bisherigen Föderationstests haben den Roster beider Seiten
        /// selbst gesetzt, weil der Handshake über die Grenze nicht ging. Erst
        /// hier entsteht die Berechtigung aus dem Protokoll.
        /// </remarks>
        [Test]
        public async Task AfterApproval_PresenceCrossesTheBoundary()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var anfragen = new List<String>();
            bob.OnSubscriptionRequest += (from, _) => anfragen.Add(from);

            await alice.AddContactAsync(bob.BareJid, "Bob");
            await WarteAuf(() => anfragen.Count > 0, "die Anfrage bei Bob");

            await bob.AcceptSubscriptionAsync(alice.BareJid);
            await WarteAuf(() => Zustand(_rechts, "bob@rechts.example", "alice@links.example") == "from",
                           "die 'from'-Hälfte bei Bob");

            var gesehen = new List<String>();
            alice.OnPresenceChanged += (from, _) => gesehen.Add(from);

            await bob.SetPresenceAsync();

            await WarteAuf(() => gesehen.Any(g => g.StartsWith("bob@rechts.example", StringComparison.Ordinal)),
                           "Bobs Presence bei Alice");

        }

        #endregion

        #region ApprovalItself_DeliversTheContactsPresence()

        /// <summary>
        /// Abschnitt 3.1.5: mit der Zustimmung schickt der Server des Kontakts
        /// dessen aktuelle Presence - der Antragsteller soll nicht warten
        /// müssen, bis der Kontakt das nächste Mal von sich aus etwas tut.
        /// </summary>
        /// <remarks>
        /// Hier wird bewusst <b>keine</b> weitere Presence gesendet. Der
        /// vorige Test tut das und verdeckt damit, ob die Zustimmung allein
        /// schon genügt.
        ///
        /// Über die Grenze hängt daran mehr als die Höflichkeit: die
        /// gespeicherte Presence ist ungerichtet und trägt nur ein 'from'.
        /// Ohne Adresse verwirft die Gegenstelle sie, weil sie ohne 'to' nicht
        /// weiss, wem sie gilt - innerhalb eines Servers fiele das nie auf.
        /// </remarks>
        [Test]
        public async Task ApprovalItself_DeliversTheContactsPresence()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var anfragen = new List<String>();
            bob.OnSubscriptionRequest += (from, _) => anfragen.Add(from);

            var gesehen = new List<String>();
            alice.OnPresenceChanged += (from, _) => gesehen.Add(from);

            await alice.AddContactAsync(bob.BareJid, "Bob");
            await WarteAuf(() => anfragen.Count > 0, "die Anfrage bei Bob");

            await bob.AcceptSubscriptionAsync(alice.BareJid);

            await WarteAuf(() => gesehen.Any(g => g.StartsWith("bob@rechts.example", StringComparison.Ordinal)),
                           "Bobs Presence allein aufgrund der Zustimmung");

        }

        #endregion

        #region ARepeatedRequest_IsAnsweredByTheServer()

        /// <summary>
        /// Abschnitt 3.1.4: darf der Antragsteller den Kontakt ohnehin schon
        /// sehen, beantwortet dessen Server die Anfrage selbst.
        /// </summary>
        /// <remarks>
        /// Ohne das würde der Kontakt bei jeder wiederholten Anfrage erneut
        /// gefragt, obwohl er längst zugestimmt hat - und ein Antragsteller,
        /// dessen Roster verlorenging, käme nie wieder in Ordnung, ohne den
        /// Kontakt zu behelligen.
        /// </remarks>
        [Test]
        public async Task ARepeatedRequest_IsAnsweredByTheServer()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var anfragen = new List<String>();
            bob.OnSubscriptionRequest += (from, _) => anfragen.Add(from);

            await alice.AddContactAsync(bob.BareJid, "Bob");
            await WarteAuf(() => anfragen.Count > 0, "die erste Anfrage bei Bob");

            await bob.AcceptSubscriptionAsync(alice.BareJid);
            await WarteAuf(() => Zustand(_links, "alice@links.example", "bob@rechts.example") == "to",
                           "die Zustimmung bei Alice");

            // Alice fragt noch einmal - Bob soll davon nichts merken.
            await alice.AddContactAsync(bob.BareJid, "Bob");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(anfragen, Has.Count.EqualTo(1),
                            "Der Kontakt darf nicht erneut gefragt werden.");
                Assert.That(Zustand(_links, "alice@links.example", "bob@rechts.example"),
                            Is.EqualTo("to"));
            });

        }

        #endregion

        #region ARevocationCrossesTheBoundary()

        /// <summary>
        /// Abschnitt 3.2: der Entzug nimmt der Gegenseite die Berechtigung -
        /// auch über die Grenze.
        /// </summary>
        [Test]
        public async Task ARevocationCrossesTheBoundary()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var anfragen = new List<String>();
            bob.OnSubscriptionRequest += (from, _) => anfragen.Add(from);

            await alice.AddContactAsync(bob.BareJid, "Bob");
            await WarteAuf(() => anfragen.Count > 0, "die Anfrage bei Bob");

            await bob.AcceptSubscriptionAsync(alice.BareJid);
            await WarteAuf(() => Zustand(_links, "alice@links.example", "bob@rechts.example") == "to",
                           "die Zustimmung bei Alice");

            await bob.DenySubscriptionAsync(alice.BareJid);

            await WarteAuf(() => Zustand(_links, "alice@links.example", "bob@rechts.example") == "none",
                           "den Entzug bei Alice");

            Assert.Multiple(() =>
            {
                Assert.That(Zustand(_links,  "alice@links.example", "bob@rechts.example"),
                            Is.EqualTo("none"));
                Assert.That(Zustand(_rechts, "bob@rechts.example",  "alice@links.example"),
                            Is.EqualTo("none"));
            });

        }

        #endregion

        #region ASubscriptionForAnUnknownLocalAccount_ChangesNothing()

        /// <summary>
        /// Eine Anfrage an ein Konto, das es hier nicht gibt, ändert nichts
        /// (RFC 6121, Abschnitt 8.1).
        /// </summary>
        [Test]
        public async Task ASubscriptionForAnUnknownLocalAccount_ChangesNothing()
        {

            var angenommen = await _rechts.ReceiveFromRemoteAsync(
                                 "links.example",
                                 "<presence from='alice@links.example' to='niemand@rechts.example' type='subscribe'/>");

            Assert.Multiple(() =>
            {
                Assert.That(angenommen, Is.True, "Die Stanza selbst ist in Ordnung.");
                Assert.That(_rechts.GetAccount("niemand@rechts.example"), Is.Null);
            });

        }

        #endregion

        #region ARequestToAnOfflineAccount_IsKeptAcrossTheBoundary()

        /// <summary>
        /// Abschnitt 3.1.3, Regel 4 gilt unabhängig davon, woher die Anfrage
        /// kam: auch eine von jenseits der Grenze wird aufbewahrt, bis der
        /// Kontakt sie sehen kann.
        /// </summary>
        /// <remarks>
        /// Über die Grenze ist der Fall der Regelfall und nicht die Ausnahme.
        /// Innerhalb eines Servers sind beide Seiten meist gleichzeitig da;
        /// zwischen zwei Servern kennt der eine die Anmeldezeiten des anderen
        /// nicht, und eine Anfrage kommt gerade dann an, wenn es passt - nicht
        /// wenn es passt.
        /// </remarks>
        [Test]
        public async Task ARequestToAnOfflineAccount_IsKeptAcrossTheBoundary()
        {

            var alice = await ConnectAsync(_links, "alice");

            // Bob gibt es, aber er ist nicht verbunden.
            _rechts.AddAccount("bob");

            await alice.AddContactAsync("bob@rechts.example", "Bob");

            await WarteAuf(() => _rechts.GetAccount("bob@rechts.example")!
                                        .PendingSubscriptionRequests
                                        .ContainsKey("alice@links.example"),
                           "die aufbewahrte Anfrage auf der anderen Seite");

            var anfragen = new List<String>();

            await ConnectAsync(_rechts, "bob",
                               bob => bob.OnSubscriptionRequest += (from, _) => anfragen.Add(from));

            await WarteAuf(() => anfragen.Count > 0, "die nachgereichte Anfrage bei Bob");

            Assert.That(anfragen[0], Is.EqualTo("alice@links.example"));

        }

        #endregion

    }

}
