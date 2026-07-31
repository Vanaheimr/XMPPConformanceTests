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
    /// XEP-0198 Stream Management: die Zähler beider Seiten müssen exakt
    /// übereinstimmen.
    ///
    /// Der Testserver zählt unabhängig mit (siehe
    /// <see cref="XMPPSession.IsStanza"/>) und beantwortet
    /// <c>&lt;r/&gt;</c>. Damit lässt sich prüfen, ob der Client dem Server
    /// genau das meldet, was der Server tatsächlich geschickt hat - der
    /// Punkt, an dem ein echter Server bei Abweichung die Verbindung als
    /// Protokollverletzung abbricht.
    /// </summary>
    [TestFixture]
    public class StreamManagementTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Verbindet einen Client mit ausgehandeltem Stream Management und
        /// liefert Client und zugehörige Serversitzung.
        /// </summary>
        private async Task<(XMPPClient Client, XMPPSession Session)> ConnectWithSmAsync()
        {

            var client = await ConnectClientAsync(streamManagement: true);

            await WaitFor(() => Server.Sessions.Count(s => s.StreamManagementEnabled) == 1,
                          "ausgehandeltes Stream Management");

            var session = Server.Sessions.Single(s => s.StreamManagementEnabled);

            Assert.That(client.StreamManagement?.IsEnabled, Is.True,
                        "Der Client hält Stream Management nicht für aktiv.");

            return (client, session);

        }

        #endregion


        #region ClientAck_ReportsEveryStanzaTheServerSent()

        /// <summary>
        /// Der Kern des Fehlers: der Client muss auf <c>&lt;r/&gt;</c> mit
        /// genau der Anzahl Stanzas antworten, die der Server seit
        /// <c>&lt;enabled/&gt;</c> geschickt hat.
        ///
        /// Früher zählte nur ProcessStanza mit. Die Ergebnisse von
        /// Carbons-Enable und Roster-Abruf werden aber in der Aufbauphase
        /// direkt über ReceiveStanzaAsync gelesen und kamen dort nie an, so
        /// dass der Client dauerhaft zu wenig bestätigte.
        /// </summary>
        [Test]
        public async Task ClientAck_ReportsEveryStanzaTheServerSent()
        {

            var (_, session) = await ConnectWithSmAsync();

            // Der Server muss in der Aufbauphase nach <enabled/> überhaupt
            // etwas geschickt haben, sonst prüft der Test nichts.
            await WaitFor(() => session.StanzasSentToClient > 0,
                          "Stanzas vom Server nach <enabled/>");

            var sent = session.StanzasSentToClient;

            await session.RequestAckAsync();

            await WaitFor(() => session.LastAckFromClient is not null,
                          "<a h='...'/> vom Client");

            Assert.That(session.LastAckFromClient, Is.EqualTo(sent),
                        $"Der Client bestätigt {session.LastAckFromClient} von {sent} Stanzas.");

        }

        #endregion

        #region ClientAck_CountsStanzasArrivingAfterConnect()

        /// <summary>
        /// Auch nach dem Verbindungsaufbau, also über die Empfangsschleife,
        /// muss weitergezählt werden - und zwar zusätzlich zu den in der
        /// Aufbauphase empfangenen Stanzas.
        /// </summary>
        [Test]
        public async Task ClientAck_CountsStanzasArrivingAfterConnect()
        {

            var (_, session) = await ConnectWithSmAsync();

            await WaitFor(() => session.StanzasSentToClient > 0,
                          "Stanzas vom Server nach <enabled/>");

            var before = session.StanzasSentToClient;

            await session.SendAsync(
                $"<message from='bob@{Server.Domain}/x' to='{session.FullJid}' type='chat'>" +
                "<body>Hallo</body></message>");

            await WaitFor(() => session.StanzasSentToClient == before + 1,
                          "gezählte Nachricht auf Serverseite");

            await session.RequestAckAsync();

            await WaitFor(() => session.LastAckFromClient == before + 1,
                          $"<a h='{before + 1}'/> vom Client");

            Assert.That(session.LastAckFromClient, Is.EqualTo(before + 1));

        }

        #endregion

        #region OutboundCount_CoversEveryStanzaNotJustMessages()

        /// <summary>
        /// Der ausgehende Zähler muss jede Stanza erfassen, nicht nur die aus
        /// SendMessageAsync. Schon der Verbindungsaufbau schickt nach
        /// <c>&lt;enabled/&gt;</c> mehrere IQs und eine Presence; früher wurde
        /// davon nichts gezählt, weil TrackOutgoing nur an einer einzigen von
        /// rund 25 Sendestellen stand.
        /// </summary>
        [Test]
        public async Task OutboundCount_CoversEveryStanzaNotJustMessages()
        {

            var (client, session) = await ConnectWithSmAsync();

            await WaitFor(() => session.StanzasReceivedFromClient > 1,
                          "mehrere Stanzas vom Client");

            await WaitFor(() => client.StreamManagement!.OutboundCount == session.StanzasReceivedFromClient,
                          "übereinstimmende Ausgangszähler");

            Assert.That(client.StreamManagement!.OutboundCount,
                        Is.EqualTo(session.StanzasReceivedFromClient),
                        "Client und Server zählen unterschiedlich viele ausgehende Stanzas.");

        }

        #endregion

        #region OutboundCount_IgnoresNonzas()

        /// <summary>
        /// <c>&lt;r/&gt;</c> und <c>&lt;a/&gt;</c> sind Nonzas und zählen nach
        /// XEP-0198 Abschnitt 2 nicht mit. Würden sie mitgezählt, liefe der
        /// Zähler bei jedem Keepalive weiter auseinander.
        /// </summary>
        [Test]
        public async Task OutboundCount_IgnoresNonzas()
        {

            var (client, session) = await ConnectWithSmAsync();

            await WaitFor(() => session.StanzasReceivedFromClient > 0,
                          "Stanzas vom Client");

            var before = client.StreamManagement!.OutboundCount;

            // <r/> vom Client an den Server ...
            await client.RequestAckAsync();

            // ... und <a/> vom Client als Antwort auf ein <r/> des Servers.
            await session.RequestAckAsync();

            await WaitFor(() => session.LastAckFromClient is not null,
                          "<a h='...'/> vom Client");

            Assert.That(client.StreamManagement!.OutboundCount, Is.EqualTo(before),
                        "Nonzas dürfen den Ausgangszähler nicht erhöhen.");

        }

        #endregion

        #region SentMessage_IsCountedAndAcknowledged()

        /// <summary>
        /// Eine gesendete Nachricht muss gezählt, in die Unacked-Queue gelegt
        /// und durch das <c>&lt;a/&gt;</c> des Servers wieder daraus entfernt
        /// werden.
        /// </summary>
        [Test]
        public async Task SentMessage_IsCountedAndAcknowledged()
        {

            var (client, session) = await ConnectWithSmAsync();

            await WaitFor(() => session.StanzasReceivedFromClient > 0,
                          "Stanzas vom Client");

            var before = client.StreamManagement!.OutboundCount;

            await client.SendMessageAsync($"bob@{Server.Domain}", "Hallo");

            await WaitFor(() => client.StreamManagement!.OutboundCount == before + 1,
                          "gezählte Nachricht");

            // Der Server bestätigt alles, was er bisher empfangen hat.
            await client.RequestAckAsync();

            await WaitFor(() => client.StreamManagement!.UnackedCount == 0,
                          "geleerte Unacked-Queue");

            Assert.That(client.StreamManagement!.UnackedCount, Is.Zero);

        }

        #endregion

        #region CountersStayEqual_UnderConcurrentSends()

        /// <summary>
        /// Gleichzeitige Sendeaufrufe dürfen die Zählung nicht durcheinander
        /// bringen. Deshalb wird unter dem Sende-Lock gezählt und nicht davor.
        /// </summary>
        [Test]
        public async Task CountersStayEqual_UnderConcurrentSends()
        {

            var (client, session) = await ConnectWithSmAsync();

            await WaitFor(() => session.StanzasReceivedFromClient > 0,
                          "Stanzas vom Client");

            var before = client.StreamManagement!.OutboundCount;

            await Task.WhenAll(Enumerable.Range(0, 50)
                                         .Select(i => client.SendMessageAsync($"bob@{Server.Domain}", $"Nachricht {i}")));

            await WaitFor(() => client.StreamManagement!.OutboundCount == before + 50,
                          "50 gezählte Nachrichten");

            await WaitFor(() => session.StanzasReceivedFromClient == client.StreamManagement!.OutboundCount,
                          "übereinstimmende Zähler nach parallelem Senden");

            Assert.That(client.StreamManagement!.OutboundCount,
                        Is.EqualTo(session.StanzasReceivedFromClient));

        }

        #endregion

        #region DisabledStreamManagement_DoesNotCount()

        /// <summary>
        /// Ohne ausgehandeltes Stream Management darf nichts gezählt werden -
        /// sonst stünde beim späteren <c>&lt;enable/&gt;</c> ein Wert im
        /// Zähler, der dem Server nie gemeldet wurde.
        /// </summary>
        [Test]
        public async Task DisabledStreamManagement_DoesNotCount()
        {

            var client = await ConnectClientAsync(streamManagement: false);

            await client.SendMessageAsync($"bob@{Server.Domain}", "Hallo");

            Assert.Multiple(() =>
            {
                Assert.That(client.StreamManagement?.IsEnabled,      Is.False);
                Assert.That(client.StreamManagement?.OutboundCount,  Is.Zero);
                Assert.That(client.StreamManagement?.InboundCount,   Is.Zero);
            });

        }

        #endregion

        #region StreamManagement_IsNegotiatedByDefault()

        /// <summary>
        /// Ein Client, der nichts einstellt, handelt Stream Management aus.
        /// </summary>
        /// <remarks>
        /// Der Vorgabewert stand jahrelang auf <c>false</c>, weil die Zählung
        /// einmal falsch war. Sie ist es nicht mehr und ist gegen Prosody 13
        /// belegt (<c>ProsodyStreamManagementTests</c>) - deshalb steht er
        /// jetzt auf <c>true</c>.
        ///
        /// Geprüft wird beides: der Wert selbst und dass er bis auf die
        /// Leitung durchschlägt. Ein Test nur auf die Eigenschaft bestünde
        /// auch dann, wenn der Aufbau sie danach ignorierte; ein Test nur auf
        /// die Aushandlung liesse offen, ob sie am Vorgabewert hängt oder an
        /// etwas anderem.
        ///
        /// Dass die übrige Sammlung diesen Weg überhaupt geht, hängt daran,
        /// dass <c>CreateClient</c> den Schalter <i>nicht</i> setzt, solange
        /// niemand ihn verlangt - siehe <see cref="AXMPPTests"/>.
        /// </remarks>
        [Test]
        public async Task StreamManagement_IsNegotiatedByDefault()
        {

            Assert.That(new XMPPConnection("alice@example.com", "pw").StreamManagementEnabled,
                        Is.True,
                        "Der Vorgabewert von XMPPConnection.StreamManagementEnabled.");

            var client = await ConnectClientAsync();

            await WaitFor(() => Server.Sessions.Count(s => s.StreamManagementEnabled) == 1,
                          "ausgehandeltes Stream Management ohne Zutun des Aufrufers");

            Assert.That(client.StreamManagement?.IsEnabled, Is.True,
                        "Der Client hält Stream Management nicht für aktiv.");

        }

        #endregion

    }

}
