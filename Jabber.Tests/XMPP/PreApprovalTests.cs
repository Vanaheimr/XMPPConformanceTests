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

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Subscription-Pre-Approval nach RFC 6121, Abschnitt 3.4: eine Anfrage
    /// zulassen, bevor sie gestellt wurde.
    /// </summary>
    /// <remarks>
    /// Der Abschnitt unterscheidet vier Fälle, und alle vier hängen an
    /// derselben Frage - liegt eine Anfrage vor oder nicht. Dasselbe
    /// <c>&lt;presence type='subscribed'/&gt;</c> ist einmal eine Zustimmung
    /// und einmal eine Vormerkung, und die Stanza selbst sieht in beiden
    /// Fällen gleich aus. Der Unterschied steckt allein im Roster des
    /// Absenders.
    /// </remarks>
    [TestFixture]
    public class PreApprovalTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private String Alice => $"alice@{Server.Domain}";
        private String Bob   => $"bob@{Server.Domain}";

        private String? SubscriptionOf(String owner, String contact)
            => Server.GetAccount(owner)?.SubscriptionOf(contact);

        private Boolean IstVorgemerkt(String owner, String contact)
            => Server.GetAccount(owner)?
                     .Roster.FirstOrDefault(e => e.Jid.Equals(contact, StringComparison.OrdinalIgnoreCase))?
                     .Approved == true;

        #endregion


        #region TheServerAdvertisesPreApproval()

        /// <summary>
        /// Abschnitt 3.4: ein Server, der es beherrscht, muss es ankündigen -
        /// und ohne Ankündigung darf ein Client es nicht benutzen.
        /// </summary>
        [Test]
        public async Task TheServerAdvertisesPreApproval()
        {

            var alice = await ConnectClientAsync("alice");

            Assert.Multiple(() =>
            {
                Assert.That(alice.Connection.ServerFeatures,
                            Does.Contain("urn:xmpp:features:pre-approval"));
                Assert.That(alice.ServerSupportsPreApproval, Is.True);
            });

        }

        #endregion

        #region WithoutAPendingRequest_SubscribedIsRememberedNotSent()

        /// <summary>
        /// Fall 3 und 4: ohne offene Anfrage wird vorgemerkt - und die Stanza
        /// geht ausdrücklich <b>nicht</b> hinaus.
        /// </summary>
        /// <remarks>
        /// Die zweite Hälfte ist die wichtigere und leicht zu übersehen. Ginge
        /// das <c>subscribed</c> trotzdem hinaus, bekäme der Kontakt eine
        /// Zustimmung zu einer Frage, die er nie gestellt hat - sein Server
        /// würde daraus eine Subscription bauen, von der der Nutzer nichts
        /// weiss.
        /// </remarks>
        [Test]
        public async Task WithoutAPendingRequest_SubscribedIsRememberedNotSent()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var beiBob = new List<String>();
            bob.OnPresenceChanged += (from, type) => beiBob.Add($"{from}/{type}");

            await alice.PreApproveContactAsync(Bob);

            await WaitFor(() => IstVorgemerkt(Alice, Bob), "die Vormerkung bei Alice");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(IstVorgemerkt(Alice, Bob), Is.True);

                // Vorgemerkt heisst noch nicht berechtigt.
                Assert.That(SubscriptionOf(Alice, Bob), Is.EqualTo("none"));

                Assert.That(beiBob.Any(e => e.Contains("subscribed", StringComparison.Ordinal)),
                            Is.False,
                            "Ohne gestellte Anfrage darf keine Zustimmung hinausgehen.");
            });

        }

        #endregion

        #region APreApprovedRequest_IsAnsweredWithoutAskingTheUser()

        /// <summary>
        /// Abschnitt 3.4.2: ist der Kontakt vorgemerkt, darf seine Anfrage dem
        /// Nutzer gar nicht erst zugestellt werden - der Server antwortet für
        /// ihn.
        /// </summary>
        [Test]
        public async Task APreApprovedRequest_IsAnsweredWithoutAskingTheUser()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var anfragenBeiAlice = new List<String>();
            alice.OnSubscriptionRequest += (from, _) => anfragenBeiAlice.Add(from);

            await alice.PreApproveContactAsync(Bob);
            await WaitFor(() => IstVorgemerkt(Alice, Bob), "die Vormerkung");

            // Jetzt fragt Bob tatsächlich.
            await bob.AddContactAsync(Alice, "Alice");

            await WaitFor(() => SubscriptionOf(Bob, Alice) == "to",
                          "Bobs 'to'-Hälfte aus der selbsttätigen Zustimmung");

            Assert.Multiple(() =>
            {
                Assert.That(SubscriptionOf(Alice, Bob), Is.EqualTo("from"));
                Assert.That(SubscriptionOf(Bob,   Alice), Is.EqualTo("to"));

                Assert.That(anfragenBeiAlice, Is.Empty,
                            "Eine vorgemerkte Anfrage darf den Nutzer nicht erreichen.");
            });

        }

        #endregion

        #region WithAPendingRequest_SubscribedIsANormalApproval()

        /// <summary>
        /// Fall 2: liegt eine Anfrage vor, ist dasselbe <c>subscribed</c> eine
        /// gewöhnliche Zustimmung - mit Weiterleitung.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe zur Vormerkung. Ohne sie bestünde der Verdacht, dass
        /// jedes <c>subscribed</c> nur noch vorgemerkt und nie mehr zugestellt
        /// wird.
        /// </remarks>
        [Test]
        public async Task WithAPendingRequest_SubscribedIsANormalApproval()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var anfragen = new List<String>();
            bob.OnSubscriptionRequest += (from, _) => anfragen.Add(from);

            await alice.AddContactAsync(Bob, "Bob");
            await WaitFor(() => anfragen.Count > 0, "die Anfrage bei Bob");

            await bob.AcceptSubscriptionAsync(Alice);

            await WaitFor(() => SubscriptionOf(Alice, Bob) == "to",
                          "Alices 'to'-Hälfte");

            Assert.Multiple(() =>
            {
                Assert.That(SubscriptionOf(Bob,   Alice), Is.EqualTo("from"));
                Assert.That(SubscriptionOf(Alice, Bob),   Is.EqualTo("to"));

                // Eine beantwortete Anfrage ist keine Vormerkung.
                Assert.That(IstVorgemerkt(Bob, Alice), Is.False);
            });

        }

        #endregion

        #region AnEstablishedSubscription_IgnoresAFurtherSubscribed()

        /// <summary>
        /// Fall 1: darf der Kontakt uns ohnehin schon sehen, wird ein weiteres
        /// <c>subscribed</c> stillschweigend übergangen.
        /// </summary>
        [Test]
        public async Task AnEstablishedSubscription_IgnoresAFurtherSubscribed()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var anfragen = new List<String>();
            bob.OnSubscriptionRequest += (from, _) => anfragen.Add(from);

            await alice.AddContactAsync(Bob, "Bob");
            await WaitFor(() => anfragen.Count > 0, "die Anfrage bei Bob");

            await bob.AcceptSubscriptionAsync(Alice);
            await WaitFor(() => SubscriptionOf(Bob, Alice) == "from", "die Zustimmung");

            // Noch einmal - das darf nichts ändern, insbesondere keine
            // Vormerkung erzeugen.
            //
            // Über die Verbindung und nicht über den Client: dessen
            // AcceptSubscriptionAsync verlangt eine offene Anfrage und täte
            // hier schlicht nichts. Der Test hätte dann bestanden, ohne die
            // Stanza je abgeschickt zu haben - und genau so ist er zuerst
            // durchgelaufen.
            await bob.Connection.AcceptSubscriptionAsync(Alice);

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(SubscriptionOf(Bob, Alice), Is.EqualTo("from"));
                Assert.That(IstVorgemerkt(Bob, Alice),  Is.False);
            });

        }

        #endregion

        #region UnsubscribedCancelsThePreApproval()

        /// <summary>
        /// Abschnitt 3.4.2, Anmerkung: eine Vormerkung lässt sich mit
        /// <c>unsubscribed</c> zurücknehmen.
        /// </summary>
        [Test]
        public async Task UnsubscribedCancelsThePreApproval()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            await alice.PreApproveContactAsync(Bob);
            await WaitFor(() => IstVorgemerkt(Alice, Bob), "die Vormerkung");

            await alice.DenySubscriptionAsync(Bob);
            await WaitFor(() => !IstVorgemerkt(Alice, Bob), "die Rücknahme");

            var anfragen = new List<String>();
            alice.OnSubscriptionRequest += (from, _) => anfragen.Add(from);

            // Ohne Vormerkung muss Bobs Anfrage wieder bei Alice landen.
            await bob.AddContactAsync(Alice, "Alice");

            await WaitFor(() => anfragen.Count > 0,
                          "die Anfrage bei Alice nach zurückgenommener Vormerkung");

            Assert.That(SubscriptionOf(Alice, Bob), Is.EqualTo("none"));

        }

        #endregion

        #region WithPreApprovalTurnedOff_NothingIsRemembered()

        /// <summary>
        /// Ohne Unterstützung wird weder angekündigt noch vorgemerkt.
        /// </summary>
        /// <remarks>
        /// Der Abschnitt stellt Pre-Approval ausdrücklich frei. Ein Server, der
        /// es abschaltet, darf ein <c>subscribed</c> ohne Anfrage folgenlos
        /// lassen - er darf es nur nicht ankündigen und sich dann anders
        /// verhalten.
        /// </remarks>
        [Test]
        public async Task WithPreApprovalTurnedOff_NothingIsRemembered()
        {

            Server.OfferSubscriptionPreApproval = false;

            var alice = await ConnectClientAsync("alice");
            await ConnectClientAsync("bob");

            Assert.Multiple(() =>
            {
                Assert.That(alice.Connection.ServerFeatures,
                            Does.Not.Contain("urn:xmpp:features:pre-approval"));
                Assert.That(alice.ServerSupportsPreApproval, Is.False);
            });

            // Abschnitt 3.4.1: ohne Ankündigung darf der Client es nicht
            // einmal versuchen - die Methode verweigert von sich aus.
            Assert.That(await alice.PreApproveContactAsync(Bob), Is.False);

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(IstVorgemerkt(Alice, Bob),   Is.False);
                Assert.That(SubscriptionOf(Alice, Bob),  Is.Not.EqualTo("from"));
            });

        }

        #endregion

    }

}
