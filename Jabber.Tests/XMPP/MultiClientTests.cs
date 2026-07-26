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

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Tests mit mehreren echten Clients am selben Testserver: Zustellung
    /// zwischen Konten, mehrere Resourcen eines Kontos und die darauf
    /// aufbauenden XEPs.
    /// </summary>
    [TestFixture]
    public class MultiClientTests : AXMPPTests
    {

        #region TwoClients_ExchangeMessage()

        /// <summary>
        /// Eine Nachricht von Alice muss bei Bob ankommen - mit korrektem
        /// Absender und Inhalt.
        /// </summary>
        [Test]
        public async Task TwoClients_ExchangeMessage()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var inbox = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += m => inbox.Enqueue(m);

            await alice.SendMessageAsync(bob.BareJid, "Hallo Bob!");

            await WaitFor(() => !inbox.IsEmpty, "Zustellung der Nachricht bei Bob");

            inbox.TryDequeue(out var received);

            Assert.Multiple(() =>
            {
                Assert.That(received!.Body,        Is.EqualTo("Hallo Bob!"));
                Assert.That(received.FromBareJid,  Is.EqualTo(alice.BareJid));
                Assert.That(received.MessageId,    Is.Not.Null);
            });

        }

        #endregion

        #region MessageDelivery_TriggersReceiptAndChatMarker()

        /// <summary>
        /// Der Empfänger quittiert automatisch: XEP-0184 Zustellbestätigung
        /// und XEP-0333 received-Marker müssen beim Absender ankommen.
        /// </summary>
        [Test]
        public async Task MessageDelivery_TriggersReceiptAndChatMarker()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var receipts = new ConcurrentQueue<String>();
            var markers  = new ConcurrentQueue<ChatMarker>();

            alice.OnReceiptReceived += (from, id) => receipts.Enqueue(id);
            alice.OnChatMarker      += m           => markers.Enqueue(m);

            var messageId = await alice.SendMessageAsync(bob.BareJid, "Bitte bestätigen");

            await WaitFor(() => !receipts.IsEmpty, "XEP-0184 Zustellbestätigung bei Alice");
            await WaitFor(() => !markers.IsEmpty,  "XEP-0333 received-Marker bei Alice");

            receipts.TryDequeue(out var receiptId);
            markers.TryDequeue(out var marker);

            Assert.Multiple(() =>
            {
                Assert.That(receiptId,          Is.EqualTo(messageId));
                Assert.That(marker!.Type,       Is.EqualTo(ChatMarkerType.Received));
                Assert.That(marker.MessageId,   Is.EqualTo(messageId));
            });

        }

        #endregion

        #region TypingIndicator_ReachesOtherClient()

        /// <summary>
        /// XEP-0085: Der Tippstatus muss beim Gegenüber ankommen.
        /// </summary>
        [Test]
        public async Task TypingIndicator_ReachesOtherClient()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var states = new ConcurrentQueue<ChatState>();
            bob.OnChatState += (from, state) => states.Enqueue(state);

            alice.SetChatPartner(bob.BareJid);
            await alice.SendChatStateAsync(ChatState.Composing);

            await WaitFor(() => states.Contains(ChatState.Composing),
                          "Tippstatus bei Bob");

            Assert.Pass();

        }

        #endregion

        #region TwoResourcesOfSameAccount_GetDistinctFullJids()

        /// <summary>
        /// Zwei Clients desselben Kontos müssen unterschiedliche Resourcen
        /// bekommen.
        /// </summary>
        /// <remarks>
        /// XMPPConnection fordert fest console-{ProcessId} als Resource an.
        /// Laufen zwei Clients im selben Prozess, verlangen beide dieselbe
        /// Resource; erst der Server vergibt eine abweichende. Gegen einen
        /// Server, der stattdessen mit conflict antwortet, würde der zweite
        /// Client scheitern - der Client behandelt Bind-Fehler nicht.
        /// </remarks>
        [Test]
        public async Task TwoResourcesOfSameAccount_GetDistinctFullJids()
        {

            var first   = await ConnectClientAsync("alice");
            var second  = await ConnectClientAsync("alice");

            Assert.Multiple(() =>
            {
                Assert.That(first.BareJid, Is.EqualTo(second.BareJid));
                Assert.That(first.FullJid, Is.Not.EqualTo(second.FullJid),
                            "Beide Resourcen haben denselben Full-JID erhalten.");
                Assert.That(Server.SessionsOf(first.BareJid), Has.Count.EqualTo(2));
            });

        }

        #endregion

        #region SecondResource_ReceivesSentCarbon()

        /// <summary>
        /// XEP-0280: Sendet eine Resource eine Nachricht, bekommt die andere
        /// Resource desselben Kontos eine sent-Kopie.
        /// </summary>
        [Test]
        public async Task SecondResource_ReceivesSentCarbon()
        {

            var phone    = await ConnectClientAsync("alice");
            var desktop  = await ConnectClientAsync("alice");
            var bob      = await ConnectClientAsync("bob");

            await WaitFor(() => Server.SessionsOf(phone.BareJid).All(s => s.CarbonsEnabled),
                          "Aktivierung der Carbons für beide Resourcen");

            var carbons = new ConcurrentQueue<CarbonMessage>();
            desktop.OnCarbonMessage += c => carbons.Enqueue(c);

            await phone.SendMessageAsync(bob.BareJid, "Vom Telefon geschrieben");

            await WaitFor(() => !carbons.IsEmpty, "sent-Carbon auf dem Desktop");

            carbons.TryDequeue(out var carbon);

            Assert.Multiple(() =>
            {
                Assert.That(carbon!.IsSent,   Is.True, "Der Carbon wurde nicht als 'gesendet' erkannt.");
                Assert.That(carbon.Body,      Is.EqualTo("Vom Telefon geschrieben"));
                Assert.That(carbon.OriginalTo, Does.StartWith(bob.BareJid));
            });

        }

        #endregion

        #region PingBetweenClients_MeasuresRoundTrip()

        /// <summary>
        /// XEP-0199: Ein Client kann einen anderen anpingen; die Gegenstelle
        /// antwortet automatisch.
        /// </summary>
        [Test]
        public async Task PingBetweenClients_MeasuresRoundTrip()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var rtt = await alice.PingAsync(bob.FullJid);

            Assert.Multiple(() =>
            {
                Assert.That(rtt,        Is.Not.Null, "Bob hat den Ping nicht beantwortet.");
                Assert.That(rtt!.Value, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
            });

        }

        #endregion

        #region PresenceOfOtherClient_IsObserved()

        /// <summary>
        /// Die Presence eines anderen Clients muss beim Gegenüber ankommen -
        /// sofern er sie sehen darf. Die beidseitige Subscription ist seit der
        /// Filterung nach RFC 6121, Abschnitt 4 Voraussetzung; wer sie
        /// tatsächlich bekommt und wer nicht, prüfen die
        /// <c>PresenceSubscriptionTests</c>.
        /// </summary>
        [Test]
        public async Task PresenceOfOtherClient_IsObserved()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice");

            var presences = new ConcurrentQueue<String>();
            alice.OnPresenceChanged += (from, type) => presences.Enqueue($"{from}|{type}");

            var bob = await ConnectClientAsync("bob");
            await bob.SetPresenceAsync("away", "Bin gleich zurück");

            await WaitFor(() => presences.Any(p => p.StartsWith(bob.BareJid, StringComparison.OrdinalIgnoreCase)),
                          "Presence von Bob bei Alice");

            Assert.Pass();

        }

        #endregion

    }

}
