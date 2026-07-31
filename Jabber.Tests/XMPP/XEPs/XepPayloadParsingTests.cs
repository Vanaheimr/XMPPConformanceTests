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
    /// Die XEP-Nutzlasten innerhalb einer Stanza in gültiger, aber
    /// ungewöhnlicher Schreibweise.
    ///
    /// Der Stanza-Rahmen wird bereits mit einem XML-Parser gelesen; diese Tests
    /// nehmen sich die Auswertung der Kindelemente vor - Chat States,
    /// Chat Marker, Quittungen, Carbons und Entity Capabilities.
    /// </summary>
    [TestFixture]
    public class XepPayloadParsingTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private async Task<(XMPPClient Client, XMPPSession Session)> ConnectedPairAsync()
        {

            var client = await ConnectClientAsync();

            await WaitFor(() => Server.SessionOf(client.FullJid) is not null,
                          "Serversitzung zum Client");

            return (client, Server.SessionOf(client.FullJid)!);

        }

        private String Bob => $"bob@{Server.Domain}";

        #endregion


        #region ChatState_InAForeignNamespace_IsIgnored()

        /// <summary>
        /// Ein <c>&lt;composing/&gt;</c> zählt nur, wenn es zu XEP-0085 gehört.
        /// Eine Erkennung über <c>Contains("&lt;composing")</c> prüft den
        /// Namespace gar nicht und meldet jedes gleichnamige Element - etwa aus
        /// einer beliebigen Erweiterung - als Tippanzeige.
        /// </summary>
        [Test]
        public async Task ChatState_InAForeignNamespace_IsIgnored()
        {

            var (client, session) = await ConnectedPairAsync();

            ChatState? reported = null;
            client.OnChatState += (_, state) => reported = state;

            await session.SendAsync(
                $"<message from='{Bob}/x' to='{client.FullJid}' type='chat' id='cs1'>" +
                "<composing xmlns='urn:example:etwas-anderes'/>" +
                "<body>Text</body></message>");

            await WaitFor(() => client.LastReceivedMessageId == "cs1", "zugestellte Nachricht");

            Assert.That(reported, Is.Null,
                        "Ein fremdes <composing/> ist keine Chat-State-Meldung.");

        }

        #endregion

        #region ChatState_InsideForwarded_DoesNotLeakOut()

        /// <summary>
        /// Eine weitergeleitete Nachricht bringt ihren eigenen Chat State mit.
        /// Der gehört zur eingebetteten Nachricht, nicht zur äusseren.
        /// </summary>
        [Test]
        public async Task ChatState_InsideForwarded_DoesNotLeakOut()
        {

            var (client, session) = await ConnectedPairAsync();

            ChatState? reported = null;
            client.OnChatState += (_, state) => reported = state;

            await session.SendAsync(
                $"<message from='{Bob}/x' to='{client.FullJid}' type='chat' id='cs2'>" +
                "<forwarded xmlns='urn:xmpp:forward:0'>" +
                "<message xmlns='jabber:client'>" +
                "<composing xmlns='http://jabber.org/protocol/chatstates'/>" +
                "</message></forwarded>" +
                "<body>Text</body></message>");

            await WaitFor(() => client.LastReceivedMessageId == "cs2", "zugestellte Nachricht");

            Assert.That(reported, Is.Null,
                        "Der Chat State der eingebetteten Nachricht darf nicht nach aussen wirken.");

        }

        #endregion

        #region ChatState_IsStillRecognised()

        /// <summary>
        /// Kontrollgruppe: der Normalfall muss weiter erkannt werden.
        /// </summary>
        [Test]
        public async Task ChatState_IsStillRecognised()
        {

            var (client, session) = await ConnectedPairAsync();

            ChatState? reported = null;
            client.OnChatState += (_, state) => reported = state;

            await session.SendAsync(
                $"<message from='{Bob}/x' to='{client.FullJid}' type='chat'>" +
                "<composing xmlns='http://jabber.org/protocol/chatstates'/></message>");

            await WaitFor(() => reported is not null, "gemeldeter Chat State");

            Assert.That(reported, Is.EqualTo(ChatState.Composing));

        }

        #endregion

        #region ChatMarker_WithAttributesInAnyOrder_IsRecognised()

        /// <summary>
        /// Das frühere Muster verlangte <c>xmlns</c> vor <c>id</c>. XML kennt
        /// keine Attributreihenfolge - ein Server, der sie andersherum
        /// schreibt, wurde still ignoriert.
        /// </summary>
        [Test]
        public async Task ChatMarker_WithAttributesInAnyOrder_IsRecognised()
        {

            var (client, session) = await ConnectedPairAsync();

            ChatMarker? reported = null;
            client.OnChatMarker += marker => reported = marker;

            await session.SendAsync(
                $"<message from='{Bob}/x' to='{client.FullJid}' type='chat'>" +
                "<displayed id='abc-123' xmlns='urn:xmpp:chat-markers:0'/></message>");

            await WaitFor(() => reported is not null, "gemeldeter Chat Marker");

            Assert.Multiple(() =>
            {
                Assert.That(reported!.Type,       Is.EqualTo(ChatMarkerType.Displayed));
                Assert.That(reported!.MessageId,  Is.EqualTo("abc-123"));
            });

        }

        #endregion

        #region ChatMarker_InAForeignNamespace_IsIgnored()

        /// <summary>
        /// <c>&lt;received/&gt;</c> gibt es in XEP-0333 <b>und</b> in XEP-0184.
        /// Ohne Namespace-Prüfung sind sie nicht auseinanderzuhalten.
        /// </summary>
        [Test]
        public async Task ChatMarker_InAForeignNamespace_IsIgnored()
        {

            var (client, session) = await ConnectedPairAsync();

            ChatMarker? reported = null;
            client.OnChatMarker += marker => reported = marker;

            await session.SendAsync(
                $"<message from='{Bob}/x' to='{client.FullJid}' type='chat' id='cm2'>" +
                "<received id='abc' xmlns='urn:example:etwas-anderes'/>" +
                "<body>Text</body></message>");

            await WaitFor(() => client.LastReceivedMessageId == "cm2", "zugestellte Nachricht");

            Assert.That(reported, Is.Null);

        }

        #endregion

        #region ReceiptRequest_WithDoubleQuotedNamespace_IsAnswered()

        /// <summary>
        /// Die Prüfung suchte wörtlich nach <c>xmlns='urn:xmpp:receipts'</c> -
        /// also nur mit einfachen Anführungszeichen. XML lässt beide Formen zu;
        /// gegen einen Server, der doppelte benutzt, blieb jede Quittung aus.
        /// </summary>
        [Test]
        public async Task ReceiptRequest_WithDoubleQuotedNamespace_IsAnswered()
        {

            var (client, session) = await ConnectedPairAsync();

            await session.SendAsync(
                $"<message from='{Bob}/x' to='{client.FullJid}' type='chat' id='r1'>" +
                "<body>Bitte quittieren</body>" +
                "<request xmlns=\"urn:xmpp:receipts\"/></message>");

            await WaitFor(() => session.Received.Any(f => f.Contains("urn:xmpp:receipts", StringComparison.Ordinal) &&
                                                          f.Contains("id='r1'", StringComparison.Ordinal)),
                          "Quittung vom Client");

            Assert.Pass();

        }

        #endregion

        #region ReceiptRequest_InsideForwarded_IsNotAnswered()

        /// <summary>
        /// Die Bitte um eine Quittung in einer weitergeleiteten Nachricht gilt
        /// nicht für die äussere - sonst quittiert der Client eine Nachricht,
        /// die er nie erhalten hat.
        /// </summary>
        [Test]
        public async Task ReceiptRequest_InsideForwarded_IsNotAnswered()
        {

            var (client, session) = await ConnectedPairAsync();

            await session.SendAsync(
                $"<message from='{Bob}/x' to='{client.FullJid}' type='chat' id='r2'>" +
                "<forwarded xmlns='urn:xmpp:forward:0'>" +
                "<message xmlns='jabber:client' id='innen'>" +
                "<request xmlns='urn:xmpp:receipts'/></message></forwarded>" +
                "<body>Text</body></message>");

            await WaitFor(() => client.LastReceivedMessageId == "r2", "zugestellte Nachricht");

            var answered = await XMPPServer.WaitUntilAsync(
                               () => session.Received.Any(f => f.Contains("urn:xmpp:receipts", StringComparison.Ordinal)),
                               TimeSpan.FromSeconds(1));

            Assert.That(answered, Is.False,
                        "Für eine eingebettete Bitte darf keine Quittung gesendet werden.");

        }

        #endregion

        #region Carbon_WithEntitiesInTheBody_IsUnescaped()

        /// <summary>
        /// Der Inhalt einer gespiegelten Nachricht gehört genauso aufgelöst wie
        /// der einer direkten.
        /// </summary>
        [Test]
        public async Task Carbon_WithEntitiesInTheBody_IsUnescaped()
        {

            var (client, session) = await ConnectedPairAsync();

            CarbonMessage? reported = null;
            client.OnCarbonMessage += carbon => reported = carbon;

            await session.SendAsync(
                $"<message xmlns='jabber:client' from='{client.BareJid}' to='{client.FullJid}'>" +
                "<sent xmlns='urn:xmpp:carbons:2'>" +
                "<forwarded xmlns='urn:xmpp:forward:0'>" +
                $"<message xmlns='jabber:client' from='{client.BareJid}/andere' to='{Bob}' type='chat'>" +
                "<body>3 &gt; 2 &amp;&amp; 1 &lt; 2</body>" +
                "</message></forwarded></sent></message>");

            await WaitFor(() => reported is not null, "gemeldeter Carbon");

            Assert.That(reported!.Body, Is.EqualTo("3 > 2 && 1 < 2"));

        }

        #endregion

    }

}
