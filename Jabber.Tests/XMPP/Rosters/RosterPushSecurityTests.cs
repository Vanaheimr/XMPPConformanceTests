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

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// RFC 6121, Abschnitt 2.1.6: Ein Roster-Push darf nur angewendet werden,
    /// wenn er kein from-Attribut trägt oder das from dem eigenen Bare-JID
    /// entspricht. Ohne diese Prüfung kann jeder Absender den lokalen Roster
    /// manipulieren.
    /// </summary>
    [TestFixture]
    public class RosterPushSecurityTests : AXMPPTests
    {

        #region SpoofedRosterPush_IsIgnored()

        /// <summary>
        /// Ein Push von einem fremden Absender darf keinen Kontakt anlegen.
        /// </summary>
        [Test]
        public async Task SpoofedRosterPush_IsIgnored()
        {

            var client = await ConnectClientAsync();
            var alerts = new List<String>();

            client.OnSpoofingAttempt += m => { lock (alerts) alerts.Add(m); };

            await Server.PushAsync(client.FullJid,
                "<iq type='set' id='spoof-1' from='evil@example.com'>" +
                "<query xmlns='jabber:iq:roster'>" +
                "<item jid='hacker@evil.com' name='Trojaner' subscription='both'/>" +
                "</query></iq>");

            await Task.Delay(300);

            Assert.Multiple(() =>
            {
                Assert.That(client.Roster.GetItem("hacker@evil.com"), Is.Null,
                            "Der gefälschte Kontakt wurde in den Roster übernommen.");

                Assert.That(alerts, Has.Count.EqualTo(1),
                            "Es wurde kein Spoofing-Versuch gemeldet.");
            });

        }

        #endregion

        #region SpoofedRosterPush_IsNotAcknowledged()

        /// <summary>
        /// Ein verworfener Push darf nicht mit iq type='result' quittiert werden.
        /// </summary>
        [Test]
        public async Task SpoofedRosterPush_IsNotAcknowledged()
        {

            var client   = await ConnectClientAsync();
            var session  = Server.SessionOf(client.FullJid)!;

            await Server.PushAsync(client.FullJid,
                "<iq type='set' id='spoof-2' from='evil@example.com'>" +
                "<query xmlns='jabber:iq:roster'>" +
                "<item jid='hacker@evil.com' subscription='both'/>" +
                "</query></iq>");

            await Task.Delay(300);

            Assert.That(session.CountReceived("id='spoof-2'"), Is.Zero,
                        "Der Client hat den gefälschten Push quittiert.");

        }

        #endregion

        #region SpoofedRemove_DoesNotDeleteContact()

        /// <summary>
        /// Ein gefälschtes subscription='remove' darf einen echten Kontakt
        /// nicht aus dem Roster löschen.
        /// </summary>
        [Test]
        public async Task SpoofedRemove_DoesNotDeleteContact()
        {

            var client = await ConnectClientAsync();

            // Ein echter Kontakt, per legitimem Push angelegt
            await Server.PushAsync(client.FullJid,
                "<iq type='set' id='legit-1'>" +
                "<query xmlns='jabber:iq:roster'>" +
                "<item jid='freund@localhost' name='Freund' subscription='both'/>" +
                "</query></iq>");

            await WaitFor(() => client.Roster.GetItem("freund@localhost") is not null,
                          "Anlegen des echten Kontakts");

            // Angriff: fremder Absender will ihn löschen
            await Server.PushAsync(client.FullJid,
                "<iq type='set' id='spoof-3' from='evil@example.com'>" +
                "<query xmlns='jabber:iq:roster'>" +
                "<item jid='freund@localhost' subscription='remove'/>" +
                "</query></iq>");

            await Task.Delay(300);

            Assert.That(client.Roster.GetItem("freund@localhost"), Is.Not.Null,
                        "Der echte Kontakt wurde durch einen gefälschten Push gelöscht.");

        }

        #endregion

        #region RosterPushWithoutFrom_IsApplied()

        /// <summary>
        /// Ein Push ohne from stammt implizit vom eigenen Konto und ist gültig.
        /// </summary>
        [Test]
        public async Task RosterPushWithoutFrom_IsApplied()
        {

            var client = await ConnectClientAsync();

            await Server.PushAsync(client.FullJid,
                "<iq type='set' id='legit-2'>" +
                "<query xmlns='jabber:iq:roster'>" +
                "<item jid='kollege@localhost' name='Kollege' subscription='to'/>" +
                "</query></iq>");

            await WaitFor(() => client.Roster.GetItem("kollege@localhost") is not null,
                          "Übernahme des Pushes ohne from");

            Assert.That(client.Roster.GetItem("kollege@localhost")!.Name, Is.EqualTo("Kollege"));

        }

        #endregion

        #region RosterPushFromOwnBareJid_IsApplied()

        /// <summary>
        /// Ein Push mit dem eigenen Bare-JID als from ist ebenfalls gültig.
        /// </summary>
        [Test]
        public async Task RosterPushFromOwnBareJid_IsApplied()
        {

            var client = await ConnectClientAsync();

            await Server.PushAsync(client.FullJid,
                $"<iq type='set' id='legit-3' from='{client.BareJid}'>" +
                "<query xmlns='jabber:iq:roster'>" +
                "<item jid='chefin@localhost' name='Chefin' subscription='both'/>" +
                "</query></iq>");

            await WaitFor(() => client.Roster.GetItem("chefin@localhost") is not null,
                          "Übernahme des Pushes mit eigenem Bare-JID");

            Assert.Pass();

        }

        #endregion

    }

}
