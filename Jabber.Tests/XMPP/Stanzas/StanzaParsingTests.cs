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
    /// Stanzas in ungewöhnlicher, aber vollkommen gültiger Schreibweise.
    ///
    /// Alle Formen hier sind nach XML und RFC 6120 zulässig und kommen bei
    /// echten Servern vor. Ein Parser, der sie nicht versteht, verliert
    /// Nachrichten still - genau das ist der Grund, warum die Stanza-Auswertung
    /// von regulären Ausdrücken auf einen XML-Parser umgestellt wurde.
    /// </summary>
    [TestFixture]
    public class StanzaParsingTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private async Task<(XMPPClient Client, XMPPSession Session)> ConnectedPairAsync()
        {

            var client = await ConnectClientAsync();

            await WaitFor(() => Server.SessionOf(client.FullJid) is not null,
                          "Serversitzung zum Client");

            return (client, Server.SessionOf(client.FullJid)!);

        }

        /// <summary>
        /// Schickt eine rohe Stanza an den Client und wartet auf OnMessage.
        /// </summary>
        private async Task<XMPPMessage?> DeliverAsync(XMPPClient client, XMPPSession session, String stanza)
        {

            XMPPMessage? received = null;
            client.OnMessage += m => received = m;

            await session.SendAsync(stanza);

            await XMPPServer.WaitUntilAsync(() => received is not null, TimeSpan.FromSeconds(3));

            return received;

        }

        #endregion


        #region Message_WithNamespacePrefix_IsDelivered()

        /// <summary>
        /// Der Elementname darf ein Präfix tragen, solange es an
        /// <c>jabber:client</c> gebunden ist. Eine Erkennung über
        /// <c>StartsWith("&lt;message")</c> verwirft solche Stanzas komplett.
        /// </summary>
        [Test]
        public async Task Message_WithNamespacePrefix_IsDelivered()
        {

            var (client, session) = await ConnectedPairAsync();

            var received = await DeliverAsync(client, session,
                               $"<c:message xmlns:c='jabber:client' from='bob@{Server.Domain}/x' " +
                               $"to='{client.FullJid}' type='chat' id='m1'>" +
                               "<c:body>Mit Präfix</c:body></c:message>");

            Assert.That(received?.Body, Is.EqualTo("Mit Präfix"));

        }

        #endregion

        #region Message_WithLanguageTaggedBody_IsDelivered()

        /// <summary>
        /// <c>&lt;body/&gt;</c> darf ein <c>xml:lang</c> tragen - RFC 6121
        /// Abschnitt 5.2.3 sieht das ausdrücklich vor. Ein Muster wie
        /// <c>&lt;body&gt;(...)&lt;/body&gt;</c> findet es dann nicht mehr.
        /// </summary>
        [Test]
        public async Task Message_WithLanguageTaggedBody_IsDelivered()
        {

            var (client, session) = await ConnectedPairAsync();

            var received = await DeliverAsync(client, session,
                               $"<message from='bob@{Server.Domain}/x' to='{client.FullJid}' " +
                               "type='chat' id='m2'>" +
                               "<body xml:lang='de'>Mit Sprachangabe</body></message>");

            Assert.That(received?.Body, Is.EqualTo("Mit Sprachangabe"));

        }

        #endregion

        #region Message_WithEntities_IsUnescaped()

        /// <summary>
        /// Entities gehören vom Parser aufgelöst. Wer den Inhalt roh
        /// weiterreicht, zeigt dem Benutzer <c>&amp;lt;</c> statt <c>&lt;</c>.
        /// </summary>
        [Test]
        public async Task Message_WithEntities_IsUnescaped()
        {

            var (client, session) = await ConnectedPairAsync();

            var received = await DeliverAsync(client, session,
                               $"<message from='bob@{Server.Domain}/x' to='{client.FullJid}' " +
                               "type='chat' id='m3'>" +
                               "<body>1 &lt; 2 &amp;&amp; 3 &gt; 2</body></message>");

            Assert.That(received?.Body, Is.EqualTo("1 < 2 && 3 > 2"));

        }

        #endregion

        #region Message_WithNestedBody_UsesTheOuterOne()

        /// <summary>
        /// XEP-0297: eine weitergeleitete Nachricht steckt vollständig in
        /// <c>&lt;forwarded/&gt;</c> - mitsamt eigenem <c>&lt;body/&gt;</c>.
        /// Ein Muster ohne Verschachtelungsbegriff nimmt das erste, also das
        /// innere.
        /// </summary>
        [Test]
        public async Task Message_WithNestedBody_UsesTheOuterOne()
        {

            var (client, session) = await ConnectedPairAsync();

            var received = await DeliverAsync(client, session,
                               $"<message from='bob@{Server.Domain}/x' to='{client.FullJid}' " +
                               "type='chat' id='m4'>" +
                               "<forwarded xmlns='urn:xmpp:forward:0'>" +
                               "<message xmlns='jabber:client'><body>innen</body></message>" +
                               "</forwarded>" +
                               "<body>aussen</body></message>");

            Assert.That(received?.Body, Is.EqualTo("aussen"));

        }

        #endregion

        #region Message_WithAttributeContainingTheIdOfANestedElement_UsesTheOuterOne()

        /// <summary>
        /// Ein unverankertes Attributmuster findet auch Attribute in
        /// Kindelementen. Weil die Attribute des äusseren Elements immer zuerst
        /// im Text stehen, fällt das nur auf, wenn das äussere Element das
        /// gesuchte Attribut gar nicht hat: die id stammt dann aus der
        /// eingebetteten Nachricht.
        ///
        /// Das ist keine Spitzfindigkeit - eine Quittung oder ein Chat-Marker
        /// auf diese id ginge an eine Nachricht, die nie gesendet wurde.
        /// </summary>
        [Test]
        public async Task Message_WithoutId_DoesNotBorrowOneFromANestedElement()
        {

            var (client, session) = await ConnectedPairAsync();

            var received = await DeliverAsync(client, session,
                               $"<message to='{client.FullJid}' type='chat' " +
                               $"from='bob@{Server.Domain}/x'>" +
                               "<forwarded xmlns='urn:xmpp:forward:0'>" +
                               "<message xmlns='jabber:client' id='innen'><body>x</body></message>" +
                               "</forwarded>" +
                               "<body>Text</body></message>");

            Assert.Multiple(() =>
            {
                Assert.That(received?.Body,       Is.EqualTo("Text"));
                Assert.That(received?.MessageId,  Is.Null,
                            "Die äussere Nachricht hat keine id - sie darf sich keine ausleihen.");
            });

        }

        #endregion

        #region Presence_WithLanguageTaggedStatus_IsRead()

        /// <summary>
        /// Auch <c>&lt;status/&gt;</c> trägt in der Praxis oft ein
        /// <c>xml:lang</c>.
        /// </summary>
        [Test]
        public async Task Presence_WithLanguageTaggedStatus_IsRead()
        {

            var (client, session) = await ConnectedPairAsync();
            var bob = $"bob@{Server.Domain}";

            await client.AddContactAsync(bob, "Bob");
            await WaitFor(() => client.GetContact(bob) is not null, "Kontakt im Roster");

            await session.SendAsync(
                $"<presence from='{bob}/x' to='{client.FullJid}'>" +
                "<show>away</show>" +
                "<status xml:lang='de'>Bin essen</status></presence>");

            await WaitFor(() => client.GetContact(bob)!.Presence == PresenceState.Away,
                          "Präsenzwechsel auf away");

            Assert.That(client.GetContact(bob)!.PresenceStatus, Is.EqualTo("Bin essen"));

        }

        #endregion

        #region Presence_WithNamespacePrefix_IsRead()

        /// <summary>
        /// Dasselbe Präfix-Problem wie bei message.
        /// </summary>
        [Test]
        public async Task Presence_WithNamespacePrefix_IsRead()
        {

            var (client, session) = await ConnectedPairAsync();
            var bob = $"bob@{Server.Domain}";

            await client.AddContactAsync(bob, "Bob");
            await WaitFor(() => client.GetContact(bob) is not null, "Kontakt im Roster");

            await session.SendAsync(
                $"<c:presence xmlns:c='jabber:client' from='{bob}/x' to='{client.FullJid}'>" +
                "<c:show>dnd</c:show></c:presence>");

            await WaitFor(() => client.GetContact(bob)!.Presence == PresenceState.Dnd,
                          "Präsenzwechsel auf dnd");

            Assert.That(client.GetContact(bob)!.Presence, Is.EqualTo(PresenceState.Dnd));

        }

        #endregion

        #region RosterPush_WithAttributesInAnyOrder_IsApplied()

        /// <summary>
        /// Das frühere Muster verlangte die Attribute in der Reihenfolge
        /// <c>jid</c>, <c>name</c>, <c>subscription</c>. XML kennt keine
        /// Attributreihenfolge - ein Server, der sie anders schreibt, wurde
        /// still ignoriert und der Kontakt fehlte im Roster.
        /// </summary>
        [Test]
        public async Task RosterPush_WithAttributesInAnyOrder_IsApplied()
        {

            var (client, session) = await ConnectedPairAsync();
            var carol = $"carol@{Server.Domain}";

            await session.SendAsync(
                $"<iq type='set' id='push-1' to='{client.FullJid}'>" +
                "<query xmlns='jabber:iq:roster'>" +
                $"<item subscription='both' name='Carol' jid='{carol}'/>" +
                "</query></iq>");

            await WaitFor(() => client.GetContact(carol) is not null, "Kontakt aus dem Push");

            Assert.That(client.GetContact(carol)!.Name, Is.EqualTo("Carol"));

        }

        #endregion

        #region RosterPush_KeepsGroups()

        /// <summary>
        /// Gruppen stehen als Kindelemente im <c>&lt;item/&gt;</c>. Das
        /// Attributmuster sah sie nie, also verlor jeder Push die
        /// Gruppenzuordnung.
        /// </summary>
        [Test]
        public async Task RosterPush_KeepsGroups()
        {

            var (client, session) = await ConnectedPairAsync();
            var dave = $"dave@{Server.Domain}";

            await session.SendAsync(
                $"<iq type='set' id='push-2' to='{client.FullJid}'>" +
                "<query xmlns='jabber:iq:roster'>" +
                $"<item jid='{dave}' name='Dave' subscription='both'>" +
                "<group>Arbeit</group><group>Projekt X</group>" +
                "</item></query></iq>");

            await WaitFor(() => client.GetContact(dave) is not null, "Kontakt aus dem Push");

            Assert.That(client.GetContact(dave)!.Groups,
                        Is.EquivalentTo(new[] { "Arbeit", "Projekt X" }));

        }

        #endregion

        #region RosterPush_UnescapesEntitiesInNames()

        /// <summary>
        /// Ein Anzeigename mit <c>&amp;</c> kommt escaped über die Leitung und
        /// gehört aufgelöst.
        /// </summary>
        [Test]
        public async Task RosterPush_UnescapesEntitiesInNames()
        {

            var (client, session) = await ConnectedPairAsync();
            var eve = $"eve@{Server.Domain}";

            await session.SendAsync(
                $"<iq type='set' id='push-3' to='{client.FullJid}'>" +
                "<query xmlns='jabber:iq:roster'>" +
                $"<item jid='{eve}' name='Eve &amp; Co. &lt;Support&gt;' subscription='both'/>" +
                "</query></iq>");

            await WaitFor(() => client.GetContact(eve) is not null, "Kontakt aus dem Push");

            Assert.That(client.GetContact(eve)!.Name, Is.EqualTo("Eve & Co. <Support>"));

        }

        #endregion

        #region Message_WithUnusualButValidSpelling_IsStillDelivered()

        /// <summary>
        /// Kontrollgruppe: Anführungszeichenstil, Attributreihenfolge und
        /// zusätzlicher Leerraum im Tag sind gültig und funktionierten auch
        /// vorher schon. Diese Fälle dürfen durch die Umstellung nicht
        /// kaputtgehen.
        /// </summary>
        [Test]
        public async Task Message_WithUnusualButValidSpelling_IsStillDelivered()
        {

            var (client, session) = await ConnectedPairAsync();

            var received = await DeliverAsync(client, session,
                               "<message   type=\"chat\"   id=\"m5\"\n" +
                               $"           to=\"{client.FullJid}\"\n" +
                               $"           from=\"bob@{Server.Domain}/x\" >" +
                               "<body>Doppelte Anführungszeichen</body></message>");

            Assert.Multiple(() =>
            {
                Assert.That(received?.Body,       Is.EqualTo("Doppelte Anführungszeichen"));
                Assert.That(received?.MessageId,  Is.EqualTo("m5"));
            });

        }

        #endregion

    }

}
