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
    /// RFC 6121, Abschnitt 3: der Subscription-Handshake.
    ///
    /// Die Presence-Filterung wertet die Subscription-Zustände aus - bis
    /// hierher konnte der Server sie aber nicht <b>herstellen</b>:
    /// <c>subscribe</c> und <c>subscribed</c> wurden nur weitergereicht, ohne
    /// die Roster zu ändern. Damit blieb der Weg, den der Client mit
    /// <c>AcceptSubscriptionAsync</c> anbietet, folgenlos.
    /// </summary>
    [TestFixture]
    public class SubscriptionHandshakeTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private String Alice => $"alice@{Server.Domain}";
        private String Bob   => $"bob@{Server.Domain}";

        private String? SubscriptionOf(String owner, String contact)
            => Server.GetAccount(owner)?.SubscriptionOf(contact);

        private String? AskOf(String owner, String contact)
            => Server.GetAccount(owner)?.Roster
                     .FirstOrDefault(e => String.Equals(e.Jid, contact, StringComparison.OrdinalIgnoreCase))
                     ?.Ask;

        /// <summary>
        /// Verbindet einen Client und sammelt ab sofort alle Presence-Meldungen
        /// als <c>jid|typ</c>.
        /// </summary>
        private async Task<(XMPPClient Client, ConcurrentQueue<String> Presences)> WatcherAsync(String localPart)
        {

            var client     = await ConnectClientAsync(localPart);
            var presences  = new ConcurrentQueue<String>();

            client.OnPresenceChanged += (from, type) => presences.Enqueue($"{from}|{type}");

            return (client, presences);

        }

        /// <summary>
        /// Alice fragt Bob an, Bob nimmt an - der vollständige Handshake über
        /// die öffentliche Client-API.
        /// </summary>
        private async Task<(XMPPClient Alice, XMPPClient Bob)> HandshakeAsync()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var requests = new ConcurrentQueue<String>();
            bob.OnSubscriptionRequest += (from, _) => requests.Enqueue(from);

            await alice.AddContactAsync(Bob, "Bob");

            await WaitFor(() => requests.Any(r => r.Equals(Alice, StringComparison.OrdinalIgnoreCase)),
                          "Kontaktanfrage bei Bob");

            await bob.AcceptSubscriptionAsync(Alice);

            await WaitFor(() => SubscriptionOf(Bob, Alice) is "from" or "both",
                          "Subscription-Zustand nach der Annahme");

            return (alice, bob);

        }

        #endregion


        #region Subscribe_MarksThePendingRequestInTheRoster()

        /// <summary>
        /// RFC 6121, Abschnitt 3.1.2: Die Anfrage legt den Eintrag an - mit
        /// <c>subscription='none'</c>, denn erlaubt ist noch nichts - und
        /// vermerkt sie über <c>ask='subscribe'</c> als offen.
        /// </summary>
        [Test]
        public async Task Subscribe_MarksThePendingRequestInTheRoster()
        {

            var alice = await ConnectClientAsync("alice");
            await ConnectClientAsync("bob");

            await alice.AddContactAsync(Bob, "Bob");

            await WaitFor(() => AskOf(Alice, Bob) == "subscribe", "offene Anfrage im Roster von Alice");

            Assert.That(SubscriptionOf(Alice, Bob), Is.EqualTo("none"),
                        "Eine offene Anfrage erlaubt noch nichts.");

        }

        #endregion

        #region Subscribe_ReachesTheContact()

        /// <summary>
        /// Die Anfrage muss beim Kontakt ankommen - das ging schon vorher, weil
        /// gerichtete Presence weitergeleitet wurde. Bleibt als Gegenprobe.
        /// </summary>
        [Test]
        public async Task Subscribe_ReachesTheContact()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var requests = new ConcurrentQueue<String>();
            bob.OnSubscriptionRequest += (from, _) => requests.Enqueue(from);

            await alice.AddContactAsync(Bob, "Bob");

            await WaitFor(() => requests.Any(r => r.Equals(Alice, StringComparison.OrdinalIgnoreCase)),
                          "Kontaktanfrage bei Bob");

        }

        #endregion

        #region Approval_SetsBothSidesOfTheRoster()

        /// <summary>
        /// RFC 6121, Abschnitt 3.1.5 und 3.1.6: Die Annahme trägt in <b>beide</b>
        /// Roster ein, jeweils in die passende Richtung. Bobs Eintrag für Alice
        /// bekommt <c>from</c> ("Alice sieht mich"), Alices Eintrag für Bob
        /// <c>to</c> ("ich sehe Bob"), und die offene Anfrage ist erledigt.
        /// </summary>
        [Test]
        public async Task Approval_SetsBothSidesOfTheRoster()
        {

            await HandshakeAsync();

            await WaitFor(() => SubscriptionOf(Alice, Bob) is "to" or "both",
                          "Subscription-Zustand bei Alice");

            Assert.Multiple(() =>
            {
                Assert.That(SubscriptionOf(Bob, Alice), Is.AnyOf("from", "both"));
                Assert.That(AskOf(Alice, Bob), Is.Null, "Die Anfrage ist beantwortet.");
            });

        }

        #endregion

        #region Approval_MakesThePresenceFlow()

        /// <summary>
        /// Der eigentliche Zweck: nach der Annahme sieht Alice die Presence von
        /// Bob. Ohne die Zustandsänderung filterte sie der Server weg, weil in
        /// keinem Roster etwas stand.
        /// </summary>
        [Test]
        public async Task Approval_MakesThePresenceFlow()
        {

            var (alice, bob) = await HandshakeAsync();

            var atAlices = new ConcurrentQueue<String>();
            alice.OnPresenceChanged += (from, type) => atAlices.Enqueue($"{from}|{type}");

            await bob.SetPresenceAsync("away", "Später");

            // Auf 'available' bestehen: ein <presence type='subscribed'/> läuft
            // durch dasselbe Ereignis und wäre sonst schon die halbe Antwort.
            await WaitFor(() => atAlices.Any(p => p.StartsWith(Bob, StringComparison.OrdinalIgnoreCase) &&
                                                  p.EndsWith("|available", StringComparison.Ordinal)),
                          "Presence von Bob bei Alice");

        }

        #endregion

        #region Approval_DeliversTheCurrentPresenceAtOnce()

        /// <summary>
        /// RFC 6121, Abschnitt 3.1.5: "The contact's server MUST then also send
        /// current presence to the user from each of the contact's available
        /// resources." Der Antragsteller soll nicht warten müssen, bis der
        /// Kontakt das nächste Mal von sich aus etwas schickt.
        /// </summary>
        [Test]
        public async Task Approval_DeliversTheCurrentPresenceAtOnce()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var requests = new ConcurrentQueue<String>();
            bob.OnSubscriptionRequest += (from, _) => requests.Enqueue(from);

            var atAlices = new ConcurrentQueue<String>();
            alice.OnPresenceChanged += (from, type) => atAlices.Enqueue($"{from}|{type}");

            await alice.AddContactAsync(Bob, "Bob");
            await WaitFor(() => requests.Any(r => r.Equals(Alice, StringComparison.OrdinalIgnoreCase)),
                          "Kontaktanfrage bei Bob");

            await bob.AcceptSubscriptionAsync(Alice);

            // Bob schickt bewusst nichts nach - die Presence muss der Server
            // von sich aus nachreichen. Auf 'available' bestehen: das
            // <presence type='subscribed'/> selbst läuft durch dasselbe
            // Ereignis und wäre sonst schon die halbe Antwort.
            await WaitFor(() => atAlices.Any(p => p.StartsWith(Bob, StringComparison.OrdinalIgnoreCase) &&
                                                  p.EndsWith("|available", StringComparison.Ordinal)),
                          "nachgereichte Presence von Bob");

        }

        #endregion

        #region Denial_GrantsNothing()

        /// <summary>
        /// Eine Ablehnung schliesst die Anfrage ab, ohne etwas zu erlauben.
        /// </summary>
        [Test]
        public async Task Denial_GrantsNothing()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var requests = new ConcurrentQueue<String>();
            bob.OnSubscriptionRequest += (from, _) => requests.Enqueue(from);

            await alice.AddContactAsync(Bob, "Bob");
            await WaitFor(() => requests.Any(r => r.Equals(Alice, StringComparison.OrdinalIgnoreCase)),
                          "Kontaktanfrage bei Bob");

            await bob.DenySubscriptionAsync(Alice);

            await WaitFor(() => AskOf(Alice, Bob) is null, "erledigte Anfrage bei Alice");

            Assert.Multiple(() =>
            {
                Assert.That(SubscriptionOf(Alice, Bob), Is.EqualTo("none"));
                Assert.That(Server.GetAccount(Bob)!.IsPresenceSubscriber(Alice), Is.False,
                            "Eine Ablehnung darf keine Sichtbarkeit herstellen.");
            });

        }

        #endregion

        #region Cancellation_SendsUnavailable()

        /// <summary>
        /// RFC 6121, Abschnitt 3.2.2: "the contact's server MUST send a presence
        /// stanza of type 'unavailable' from all of the contact's online
        /// resources". Sonst behielte Alice den letzten bekannten Zustand von
        /// Bob für immer - obwohl sie ihn gerade nicht mehr sehen darf.
        /// </summary>
        [Test]
        public async Task Cancellation_SendsUnavailable()
        {

            var (alice, bob) = await HandshakeAsync();

            var atAlices = new ConcurrentQueue<String>();
            alice.OnPresenceChanged += (from, type) => atAlices.Enqueue($"{from}|{type}");

            await bob.SendRawAsync($"<presence to='{Alice}' type='unsubscribed'/>");

            await WaitFor(() => atAlices.Any(p => p.StartsWith(Bob, StringComparison.OrdinalIgnoreCase) &&
                                                  p.EndsWith("|unavailable", StringComparison.Ordinal)),
                          "unavailable nach dem Entzug");

        }

        #endregion

        #region Unsubscribe_EndsTheOwnSubscription()

        /// <summary>
        /// RFC 6121, Abschnitt 3.3: Alice kündigt selbst. Danach sieht sie Bob
        /// nicht mehr - und Bobs Eintrag für sie verliert das <c>from</c>.
        /// </summary>
        [Test]
        public async Task Unsubscribe_EndsTheOwnSubscription()
        {

            var (alice, _) = await HandshakeAsync();

            await alice.SendRawAsync($"<presence to='{Bob}' type='unsubscribe'/>");

            // Auf Bobs Seite warten, nicht auf Alices: der Server ändert erst
            // den Roster des Absenders und dann den der Gegenseite. Wer auf den
            // ersten wartet, prüft den zweiten womöglich, bevor es ihn gibt.
            await WaitFor(() => !Server.GetAccount(Bob)!.IsPresenceSubscriber(Alice),
                          "entzogene Sichtbarkeit in Bobs Roster");

            Assert.That(SubscriptionOf(Alice, Bob), Is.AnyOf("none", "from"),
                        "Alice hat ihre eigene Subscription gekündigt.");

        }

        #endregion

        #region RosterSet_DoesNotResetTheSubscription()

        /// <summary>
        /// RFC 6121, Abschnitt 2.3: Ein Roster-Set ändert Name und Gruppen, aber
        /// <b>nicht</b> den Subscription-Zustand. Der Server übernahm bisher das
        /// fehlende Attribut als <c>none</c> - ein blosses Umbenennen eines
        /// Kontakts hätte damit die gerade erst erteilte Berechtigung wieder
        /// gelöscht.
        /// </summary>
        [Test]
        public async Task RosterSet_DoesNotResetTheSubscription()
        {

            var (alice, _) = await HandshakeAsync();

            await WaitFor(() => SubscriptionOf(Alice, Bob) is "to" or "both", "Subscription vor dem Umbenennen");

            var vorher = SubscriptionOf(Alice, Bob);

            await alice.SendRawAsync(
                "<iq type='set' id='rename-1'><query xmlns='jabber:iq:roster'>" +
                $"<item jid='{Bob}' name='Bobby'/></query></iq>");

            await WaitFor(() => Server.GetAccount(Alice)!.Roster
                                      .Any(e => e.Jid.Equals(Bob, StringComparison.OrdinalIgnoreCase) &&
                                                e.Name == "Bobby"),
                          "umbenannter Kontakt");

            Assert.That(SubscriptionOf(Alice, Bob), Is.EqualTo(vorher),
                        "Ein Roster-Set darf den Subscription-Zustand nicht anfassen.");

        }

        #endregion

        #region GrantAndRevoke_ChangeOnlyTheirOwnHalf()

        /// <summary>
        /// Die vier Übergänge einzeln. Wer sie als eine Skala von none bis both
        /// begreift, verliert genau die Gegenrichtung: aus <c>both</c> würde
        /// beim Entzug <c>none</c> statt der verbleibenden Hälfte.
        /// </summary>
        [TestCase("none", "from", "none", "to")]
        [TestCase("to",   "both", "to",   "to")]
        [TestCase("from", "from", "none", "both")]
        [TestCase("both", "both", "to",   "both")]
        public void GrantAndRevoke_ChangeOnlyTheirOwnHalf(String start,
                                                          String nachGrantFrom,
                                                          String nachRevokeFrom,
                                                          String nachGrantTo)
        {

            Assert.Multiple(() =>
            {
                Assert.That(XMPPServer.GrantFrom(start),  Is.EqualTo(nachGrantFrom),  "GrantFrom");
                Assert.That(XMPPServer.RevokeFrom(start), Is.EqualTo(nachRevokeFrom), "RevokeFrom");
                Assert.That(XMPPServer.GrantTo(start),    Is.EqualTo(nachGrantTo),    "GrantTo");
            });

        }

        #endregion

        #region RevokeTo_KeepsTheOtherDirection()

        /// <summary>Die Gegenrichtung zu <c>RevokeFrom</c>.</summary>
        [TestCase("none", "none")]
        [TestCase("to",   "none")]
        [TestCase("from", "from")]
        [TestCase("both", "from")]
        public void RevokeTo_KeepsTheOtherDirection(String start, String erwartet)
        {
            Assert.That(XMPPServer.RevokeTo(start), Is.EqualTo(erwartet));
        }

        #endregion

    }

}
