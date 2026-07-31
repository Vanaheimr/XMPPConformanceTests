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
    /// RFC 6121, Abschnitt 3 aus Sicht des Clients: <c>subscribed</c>,
    /// <c>unsubscribed</c> und <c>unsubscribe</c> sind Zustandsänderungen,
    /// keine Anwesenheitsmeldungen.
    ///
    /// Sie liefen bisher durch <c>UpdatePresence</c>. Weil dort alles ohne
    /// <c>type='unavailable'</c> als anwesend gilt, machte ausgerechnet die
    /// Nachricht "du darfst mich nicht mehr sehen" den Kontakt online.
    /// </summary>
    [TestFixture]
    public class SubscriptionStateTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private String Bob => $"bob@{Server.Domain}";

        private async Task<(XMPPClient Client, XMPPSession Session)> ConnectedPairAsync()
        {

            var client = await ConnectClientAsync();

            await WaitFor(() => Server.SessionOf(client.FullJid) is not null, "Serversitzung zum Client");

            return (client, Server.SessionOf(client.FullJid)!);

        }

        /// <summary>
        /// Legt Bob per Roster-Push mit einem bestimmten Subscription-Zustand
        /// in den Client-Roster - ohne den Handshake zu durchlaufen, damit der
        /// Test genau einen Schritt prüft.
        /// </summary>
        private async Task SeedContactAsync(XMPPClient          client,
                                            XMPPSession         session,
                                            String              subscription,
                                            SubscriptionState   erwartet)
        {

            await session.SendAsync(
                $"<iq type='set' id='seed-{subscription}'><query xmlns='jabber:iq:roster'>" +
                $"<item jid='{Bob}' name='Bob' subscription='{subscription}'/></query></iq>");

            await WaitFor(() => client.GetContact(Bob)?.Subscription == erwartet,
                          $"Kontakt mit subscription='{subscription}'");

        }

        #endregion


        #region Subscribed_DoesNotMarkTheContactOnline()

        /// <summary>
        /// Der Kern: eine Zusage sagt nichts darüber, ob der Kontakt gerade da
        /// ist. Ob er online ist, erfährt der Client aus seiner Presence - die
        /// bei einer frisch erteilten Subscription auch prompt kommt, aber eben
        /// als eigene Stanza.
        /// </summary>
        [Test]
        public async Task Subscribed_DoesNotMarkTheContactOnline()
        {

            var (client, session) = await ConnectedPairAsync();
            await SeedContactAsync(client, session, "none", SubscriptionState.None);

            await session.SendAsync($"<presence from='{Bob}' to='{client.FullJid}' type='subscribed'/>");

            await WaitFor(() => client.GetContact(Bob)?.Subscription == SubscriptionState.To,
                          "übernommene Zusage");

            Assert.That(client.GetContact(Bob)!.Presence, Is.EqualTo(PresenceState.Offline),
                        "Eine Zusage ist keine Anwesenheitsmeldung.");

        }

        #endregion

        #region Unsubscribed_DoesNotMarkTheContactOnline()

        /// <summary>
        /// Noch deutlicher beim Entzug: "du darfst mich nicht mehr sehen"
        /// setzte den Kontakt auf online.
        /// </summary>
        [Test]
        public async Task Unsubscribed_DoesNotMarkTheContactOnline()
        {

            var (client, session) = await ConnectedPairAsync();
            await SeedContactAsync(client, session, "to", SubscriptionState.To);

            await session.SendAsync($"<presence from='{Bob}' to='{client.FullJid}' type='unsubscribed'/>");

            await WaitFor(() => client.GetContact(Bob)?.Subscription == SubscriptionState.None,
                          "entzogene Subscription");

            Assert.That(client.GetContact(Bob)!.Presence, Is.EqualTo(PresenceState.Offline),
                        "Ein Entzug ist keine Anwesenheitsmeldung.");

        }

        #endregion

        #region Unsubscribe_DoesNotMarkTheContactOnline()

        /// <summary>
        /// Und bei der Kündigung der Gegenrichtung ebenso.
        /// </summary>
        [Test]
        public async Task Unsubscribe_DoesNotMarkTheContactOnline()
        {

            var (client, session) = await ConnectedPairAsync();
            await SeedContactAsync(client, session, "from", SubscriptionState.From);

            await session.SendAsync($"<presence from='{Bob}' to='{client.FullJid}' type='unsubscribe'/>");

            await WaitFor(() => client.GetContact(Bob)?.Subscription == SubscriptionState.None,
                          "gekündigte Gegenrichtung");

            Assert.That(client.GetContact(Bob)!.Presence, Is.EqualTo(PresenceState.Offline),
                        "Eine Kündigung ist keine Anwesenheitsmeldung.");

        }

        #endregion

        #region Unsubscribed_KeepsTheOtherDirection()

        /// <summary>
        /// Bei <c>Both</c> darf der Entzug nur die eigene Hälfte nehmen: Bob
        /// sieht uns weiterhin, wir ihn nicht mehr.
        /// </summary>
        [Test]
        public async Task Unsubscribed_KeepsTheOtherDirection()
        {

            var (client, session) = await ConnectedPairAsync();
            await SeedContactAsync(client, session, "both", SubscriptionState.Both);

            await session.SendAsync($"<presence from='{Bob}' to='{client.FullJid}' type='unsubscribed'/>");

            await WaitFor(() => client.GetContact(Bob)?.Subscription == SubscriptionState.From,
                          "verbleibende Gegenrichtung");

        }

        #endregion

        #region Unsubscribe_KeepsTheOtherDirection()

        /// <summary>Dasselbe spiegelbildlich.</summary>
        [Test]
        public async Task Unsubscribe_KeepsTheOtherDirection()
        {

            var (client, session) = await ConnectedPairAsync();
            await SeedContactAsync(client, session, "both", SubscriptionState.Both);

            await session.SendAsync($"<presence from='{Bob}' to='{client.FullJid}' type='unsubscribe'/>");

            await WaitFor(() => client.GetContact(Bob)?.Subscription == SubscriptionState.To,
                          "verbleibende eigene Richtung");

        }

        #endregion

        #region Unsubscribed_ClearsAStalePresence()

        /// <summary>
        /// Ohne <c>To</c> kommen keine Presence-Meldungen mehr. Was der Client
        /// zuletzt gesehen hat, wäre ab jetzt ein eingefrorener Zustand, der
        /// beliebig alt werden kann - also gilt der Kontakt als offline.
        ///
        /// Der Testserver schickt zum Entzug zwar auch ein <c>unavailable</c>
        /// (RFC 6121, Abschnitt 3.2.2); hier kommt der Entzug bewusst ohne,
        /// damit der Client für sich allein geprüft wird.
        /// </summary>
        [Test]
        public async Task Unsubscribed_ClearsAStalePresence()
        {

            var (client, session) = await ConnectedPairAsync();
            await SeedContactAsync(client, session, "both", SubscriptionState.Both);

            await session.SendAsync($"<presence from='{Bob}/x' to='{client.FullJid}'><show>dnd</show></presence>");
            await WaitFor(() => client.GetContact(Bob)?.Presence == PresenceState.Dnd, "sichtbarer Zustand");

            await session.SendAsync($"<presence from='{Bob}' to='{client.FullJid}' type='unsubscribed'/>");

            await WaitFor(() => client.GetContact(Bob)?.Presence == PresenceState.Offline,
                          "verworfener Zustand nach dem Entzug");

        }

        #endregion

        #region Subscribe_StillRaisesTheRequest()

        /// <summary>
        /// Gegenprobe: <c>subscribe</c> ist weiterhin eine Kontaktanfrage und
        /// keine Zustandsänderung.
        /// </summary>
        [Test]
        public async Task Subscribe_StillRaisesTheRequest()
        {

            var (client, session) = await ConnectedPairAsync();

            String? angefragt = null;
            client.OnSubscriptionRequest += (from, _) => angefragt = from;

            await session.SendAsync($"<presence from='{Bob}' to='{client.FullJid}' type='subscribe'/>");

            await WaitFor(() => angefragt is not null, "gemeldete Kontaktanfrage");

            Assert.That(angefragt, Is.EqualTo(Bob));

        }

        #endregion

        #region GrantAndRevoke_ChangeOnlyTheirOwnHalf()

        /// <summary>
        /// Die vier Übergänge einzeln, ohne Server. <c>To</c> und <c>From</c>
        /// sind getrennte Hälften: aus <c>Both</c> wird beim Entzug die jeweils
        /// andere, nicht <c>None</c>.
        /// </summary>
        [TestCase(SubscriptionState.None, SubscriptionState.To,   SubscriptionState.None, SubscriptionState.From, SubscriptionState.None)]
        [TestCase(SubscriptionState.To,   SubscriptionState.To,   SubscriptionState.None, SubscriptionState.Both, SubscriptionState.To)]
        [TestCase(SubscriptionState.From, SubscriptionState.Both, SubscriptionState.From, SubscriptionState.From, SubscriptionState.None)]
        [TestCase(SubscriptionState.Both, SubscriptionState.Both, SubscriptionState.From, SubscriptionState.Both, SubscriptionState.To)]
        public void GrantAndRevoke_ChangeOnlyTheirOwnHalf(SubscriptionState  start,
                                                          SubscriptionState  nachGrantTo,
                                                          SubscriptionState  nachRevokeTo,
                                                          SubscriptionState  nachGrantFrom,
                                                          SubscriptionState  nachRevokeFrom)
        {

            Assert.Multiple(() =>
            {
                Assert.That(start.GrantTo(),     Is.EqualTo(nachGrantTo),     "GrantTo");
                Assert.That(start.RevokeTo(),    Is.EqualTo(nachRevokeTo),    "RevokeTo");
                Assert.That(start.GrantFrom(),   Is.EqualTo(nachGrantFrom),   "GrantFrom");
                Assert.That(start.RevokeFrom(),  Is.EqualTo(nachRevokeFrom),  "RevokeFrom");
            });

        }

        #endregion

    }

}
