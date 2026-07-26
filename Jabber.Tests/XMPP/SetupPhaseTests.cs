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
    /// Die Aufbauphase zwischen Resource Binding und <c>Connected</c>.
    ///
    /// Der Client aktiviert dort Carbons und holt den Roster ab. Was in dieser
    /// Zeit sonst noch eintrifft - nachgelieferte Nachrichten, Presence,
    /// Roster-Pushes - gehört genauso zugestellt wie später auch. Ein echter
    /// Server schickt es, sobald die Resource gebunden ist, und wartet nicht
    /// ab, bis der Client mit seinem Aufbau fertig ist.
    /// </summary>
    [TestFixture]
    public class SetupPhaseTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Erstellt einen Client samt Konto, hängt die Ereignisse an und
        /// verbindet erst danach - Ereignisse aus der Aufbauphase gingen sonst
        /// verloren, bevor der Test sie sehen kann.
        /// </summary>
        private XMPPClient PreparedClient(String localPart = "alice")
        {

            if (Server.GetAccount($"{localPart}@{Server.Domain}") is null)
                Server.AddAccount(localPart);

            return CreateClient(localPart);

        }

        #endregion


        #region MessageAfterBind_IsDelivered()

        /// <summary>
        /// Der Kern: eine Nachricht, die zwischen Binding und
        /// <c>Connected</c> eintrifft, wurde von der Aufbauphase vom Socket
        /// gelesen und stillschweigend verworfen. Sie muss ankommen.
        /// </summary>
        [Test]
        public async Task MessageAfterBind_IsDelivered()
        {

            Server.DeliverAfterBind.Add(
                "<message from='bob@localhost/desktop' to='{jid}' type='chat' id='offline-1'>" +
                "<body>Noch offen von gestern</body></message>");

            var client    = PreparedClient();
            var received  = new List<XMPPMessage>();

            client.OnMessage += m => received.Add(m);

            await client.ConnectAsync();

            await WaitFor(() => received.Count == 1,
                          "nachgelieferte Nachricht aus der Aufbauphase");

            Assert.That(received[0].Body, Is.EqualTo("Noch offen von gestern"));

        }

        #endregion

        #region PresenceAfterBind_IsDelivered()

        /// <summary>
        /// Dasselbe für Presence. Geprüft wird nur die Zustellung, nicht der
        /// Roster: der Kontakt steht zu diesem Zeitpunkt noch gar nicht darin,
        /// weil die Presence dem Roster-Abruf vorausläuft.
        /// </summary>
        [Test]
        public async Task PresenceAfterBind_IsDelivered()
        {

            Server.DeliverAfterBind.Add(
                "<presence from='bob@localhost/desktop' to='{jid}'><show>dnd</show></presence>");

            var client     = PreparedClient();
            var presences  = new List<String>();

            client.OnPresenceChanged += (from, type) => presences.Add(from);

            await client.ConnectAsync();

            await WaitFor(() => presences.Contains("bob@localhost/desktop"),
                          "Presence aus der Aufbauphase");

        }

        #endregion

        #region RosterPushAfterBind_IsApplied()

        /// <summary>
        /// Ein Roster-Push direkt nach dem Binding. Er trägt kein 'from' und
        /// ist damit nach RFC 6121, Abschnitt 2.1.6 autorisiert.
        /// </summary>
        [Test]
        public async Task RosterPushAfterBind_IsApplied()
        {

            Server.DeliverAfterBind.Add(
                "<iq type='set' id='push-early' to='{jid}'>" +
                "<query xmlns='jabber:iq:roster'>" +
                "<item jid='carol@localhost' name='Carol' subscription='both'/>" +
                "</query></iq>");

            var client = PreparedClient();

            await client.ConnectAsync();

            await WaitFor(() => client.Roster.GetItem("carol@localhost") is not null,
                          "Roster-Push aus der Aufbauphase");

            Assert.That(client.Roster.GetItem("carol@localhost")?.Name, Is.EqualTo("Carol"));

        }

        #endregion

        #region MessageMentioningTheRosterId_IsNotMistakenForTheAnswer()

        /// <summary>
        /// Die Zuordnung lief über <c>Contains("id='roster1'")</c> - also über
        /// den Text des ganzen Rahmens. Eine Nachricht, die diese Zeichenfolge
        /// im Text führt, wurde damit für die Roster-Antwort gehalten: der
        /// Client hörte auf zu warten, fand kein <c>&lt;query/&gt;</c> und
        /// blieb ohne Kontakte zurück.
        /// </summary>
        [Test]
        public async Task MessageMentioningTheRosterId_IsNotMistakenForTheAnswer()
        {

            var account = Server.AddAccount("alice");
            account.SetRosterEntry(new RosterEntry("dave@localhost", "Dave", "both"));

            // Die erste Nachricht bringt die Carbons-Schleife dazu, ihre
            // Antwort für gefunden zu halten; erst dadurch bekommt die
            // Roster-Schleife die zweite überhaupt zu sehen.
            Server.DeliverAfterBind.Add(
                "<message from='bob@localhost/desktop' to='{jid}' type='chat'>" +
                "<body>Steht da id='carbons-enable'?</body></message>");

            Server.DeliverAfterBind.Add(
                "<message from='bob@localhost/desktop' to='{jid}' type='chat'>" +
                "<body>Schau mal, da steht id='roster1' drin</body></message>");

            var client = PreparedClient();

            await client.ConnectAsync();

            await WaitFor(() => client.Roster.GetItem("dave@localhost") is not null,
                          "Roster trotz vorgetäuschter Antwort");

        }

        #endregion

        #region MessageMentioningTheCarbonsId_IsNotMistakenForTheAnswer()

        /// <summary>
        /// Dieselbe Textzuordnung bei XEP-0280: eine Nachricht, die
        /// <c>id='carbons-enable'</c> im Text führt, galt als Antwort. Weil
        /// sie kein <c>type='result'</c> trägt, hielt der Client Carbons
        /// anschliessend für nicht verfügbar - obwohl der Server sie gleich
        /// darauf bestätigte.
        /// </summary>
        [Test]
        public async Task MessageMentioningTheCarbonsId_IsNotMistakenForTheAnswer()
        {

            Server.DeliverAfterBind.Add(
                "<message from='bob@localhost/desktop' to='{jid}' type='chat'>" +
                "<body>Steht da wirklich id='carbons-enable'?</body></message>");

            var client = PreparedClient();

            await client.ConnectAsync();

            Assert.That(client.CarbonsEnabled, Is.True,
                        "Carbons hätten nach der Bestätigung des Servers aktiv sein müssen.");

        }

        #endregion

        #region RejectedBind_IsNotReportedAsSuccess()

        /// <summary>
        /// Der gebundene JID wurde mit <c>&lt;jid&gt;([^&lt;]+)&lt;/jid&gt;</c>
        /// gesucht; blieb die Suche erfolglos, nahm der Client stillschweigend
        /// den selbst gewünschten JID an. Ein abgelehntes Binding sah damit
        /// aus wie ein erfolgreiches, und der Client meldete sich mit einem
        /// JID online, den er nie zugeteilt bekommen hat.
        /// </summary>
        [Test]
        public async Task RejectedBind_IsNotReportedAsSuccess()
        {

            Server.FailBind = true;

            var client  = PreparedClient();
            var errors  = new List<String>();

            // Ein abgelehntes Binding schickt den Client sonst durch zwanzig
            // Reconnects mit exponentiellem Backoff - der Testlauf hing dadurch
            // gut sechs Minuten an dieser einen Frage, und der Runner brach ihn
            // ab, wenn der Test allein lief. Über einen Reconnect zum selben
            // Ergebnis zu kommen wäre auch keine Antwort, nur eine langsame
            // Wiederholung derselben Frage.
            client.Connection.MaxReconnectAttempts = 0;

            client.OnError += e => errors.Add(e);

            await client.ConnectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(client.IsConnected, Is.False,
                            "Nach abgelehntem Binding darf der Client nicht als verbunden gelten.");
                Assert.That(errors, Is.Not.Empty,
                            "Ein abgelehntes Binding muss gemeldet werden.");
            });

        }

        #endregion

        #region RequiredSession_IsRequested()

        /// <summary>
        /// Die Legacy-Session (RFC 3921) wurde übersprungen, sobald das Wort
        /// "optional" irgendwo in den Stream-Features vorkam. XEP-0198 setzt
        /// aber genau dieses Element in sein eigenes Feature
        /// (<c>&lt;sm&gt;&lt;optional/&gt;&lt;/sm&gt;</c>) - ein Server, der
        /// beides ankündigt, bekam die zwingende Session nie angefordert.
        /// </summary>
        [Test]
        public async Task RequiredSession_IsRequested()
        {

            Server.SessionRequired = true;

            var client = PreparedClient();

            await client.ConnectAsync();

            await WaitFor(() => Server.AllReceived.Any(f => f.Contains("urn:ietf:params:xml:ns:xmpp-session",
                                                                       StringComparison.Ordinal)),
                          "angeforderte Legacy-Session");

        }

        #endregion

        #region OptionalSession_IsNotRequested()

        /// <summary>
        /// Die Gegenprobe: ist die Session als <c>&lt;optional/&gt;</c>
        /// angekündigt, wird sie nicht angefordert.
        /// </summary>
        [Test]
        public async Task OptionalSession_IsNotRequested()
        {

            var client = PreparedClient();

            await client.ConnectAsync();

            Assert.That(Server.AllReceived.Any(f => f.Contains("urn:ietf:params:xml:ns:xmpp-session",
                                                               StringComparison.Ordinal)),
                        Is.False,
                        "Eine als optional angekündigte Session gehört nicht angefordert.");

        }

        #endregion

    }

}
