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
    /// RFC 6121, Abschnitt 8.5: Wohin eine Nachricht geht, hängt an ihrer Art
    /// <b>und</b> an der Form der Adresse.
    /// </summary>
    /// <remarks>
    /// Der Server stellte bis hierher alles gleich zu. Die Unterscheidung ist
    /// keine Formsache — zwei der Regeln sind MUSS-Regeln, und beide verhindern
    /// etwas, das der Absender nicht will:
    ///
    /// <list type="bullet">
    ///   <item>
    ///     Ein <c>groupchat</c> an ein Konto ist nie zustellbar. Er gehört in
    ///     einen Raum; an einen Bare-JID gerichtet, wüsste keine Resource, was
    ///     sie damit anfangen soll.
    ///   </item>
    ///   <item>
    ///     Eine Resource mit negativer Priorität bekommt nichts, was bloss an
    ///     das Konto ging. Genau dafür setzt ein Client sie — das Gerät bleibt
    ///     gerichtet ansprechbar und hält sich aus dem Übrigen heraus.
    ///   </item>
    /// </list>
    /// </remarks>
    [TestFixture]
    public class MessageDeliveryRulesTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Meldet einen zweiten Client desselben Kontos an und setzt seine
        /// Priorität.
        /// </summary>
        private async Task<XMPPClient> ResourceAsync(String localPart, String resource, Int32 priority)
        {

            if (Server.GetAccount($"{localPart}@{Server.Domain}") is null)
                Server.AddAccount(localPart);

            var client = CreateClient(localPart);
            client.Connection.Resource = resource;

            await client.ConnectAsync();

            await client.SendRawAsync($"<presence><priority>{priority}</priority></presence>");

            await WaitFor(() => Server.SessionOf(client.FullJid!)?.PresencePriority == priority,
                          $"die Priorität {priority} für {resource}");

            return client;

        }

        #endregion


        #region AGroupchatToAnAccount_IsRefused()

        /// <summary>
        /// Ein <c>groupchat</c> an einen Bare-JID wird nicht zugestellt,
        /// sondern abgelehnt (Abschnitt 8.5.2.1.1).
        /// </summary>
        [Test]
        public async Task AGroupchatToAnAccount_IsRefused()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var eingang = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += m => eingang.Enqueue(m);

            var fehler = new ConcurrentQueue<StanzaError>();
            alice.Connection.OnStanzaError += (from, e) => fehler.Enqueue(e);

            await alice.SendRawAsync(
                      $"<message to='{bob.BareJid}' type='groupchat' id='an-das-konto'>" +
                      "<body>Gehört in einen Raum</body></message>");

            await WaitFor(() => !fehler.IsEmpty, "die Ablehnung beim Absender");

            fehler.TryDequeue(out var abgelehnt);

            Assert.Multiple(() =>
            {

                Assert.That(abgelehnt!.Condition, Is.EqualTo("service-unavailable"));

                Assert.That(eingang, Is.Empty,
                            "Ein groupchat an ein Konto darf keine Resource erreichen.");

            });

        }

        #endregion

        #region AGroupchatToAResource_IsDelivered()

        /// <summary>
        /// Die Gegenprobe: An eine passende Resource gerichtet wird derselbe
        /// <c>groupchat</c> zugestellt (Abschnitt 8.5.3.1).
        /// </summary>
        /// <remarks>
        /// Genau so liefert ein Raum aus - er schickt an
        /// <c>nutzer@server/resource</c>, nicht an das Konto. Ohne diese
        /// Gegenprobe bestünde die Sammlung auch dann, wenn <c>groupchat</c>
        /// überhaupt nicht mehr ankäme, und die Raumfunktion wäre unbenutzbar.
        /// </remarks>
        [Test]
        public async Task AGroupchatToAResource_IsDelivered()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var eingang = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += m => eingang.Enqueue(m);

            await alice.SendRawAsync(
                      $"<message to='{bob.FullJid}' type='groupchat' id='an-die-resource'>" +
                      "<body>Aus dem Raum</body></message>");

            await WaitFor(() => !eingang.IsEmpty, "die Zustellung an die Resource");

            eingang.TryDequeue(out var empfangen);

            Assert.That(empfangen!.Type, Is.EqualTo(MessageType.GroupChat));

        }

        #endregion

        #region AHeadlineReachesEveryResource()

        /// <summary>
        /// Ein <c>headline</c> an den Bare-JID geht an <b>alle</b> Resourcen
        /// mit nicht-negativer Priorität (Abschnitt 8.5.2.1.1).
        /// </summary>
        /// <remarks>
        /// Er ist eine Meldung an den Menschen und nicht an ein Gerät — welches
        /// davon er gerade ansieht, weiss niemand. Eine gewöhnliche Nachricht
        /// geht dagegen an eine Resource; die Gegenprobe steht im selben Test,
        /// sonst bestünde er auch dann, wenn schlicht alles an alle ginge.
        /// </remarks>
        [Test]
        public async Task AHeadlineReachesEveryResource()
        {

            var alice   = await ConnectClientAsync("alice");
            var handy   = await ResourceAsync("bob", "Handy",  1);
            var rechner = await ResourceAsync("bob", "Rechner", 1);

            var amHandy   = new ConcurrentQueue<XMPPMessage>();
            var amRechner = new ConcurrentQueue<XMPPMessage>();

            handy.OnMessage   += m => amHandy.Enqueue(m);
            rechner.OnMessage += m => amRechner.Enqueue(m);

            await alice.SendRawAsync(
                      $"<message to='{handy.BareJid}' type='headline' id='meldung'>" +
                      "<body>Kurs gefallen</body></message>");

            await WaitFor(() => !amHandy.IsEmpty && !amRechner.IsEmpty,
                          "die Meldung auf beiden Geräten");

            // Und nun eine gewöhnliche Nachricht - die geht an eine Resource.
            await alice.SendRawAsync(
                      $"<message to='{handy.BareJid}' type='chat' id='an-einen'>" +
                      "<body>Nur an dich</body></message>");

            await WaitFor(() => amHandy.Any(m => m.MessageId == "an-einen") ||
                                amRechner.Any(m => m.MessageId == "an-einen"),
                          "die gewöhnliche Nachricht");

            Assert.That(amHandy.Count(m => m.MessageId == "an-einen") +
                        amRechner.Count(m => m.MessageId == "an-einen"),
                        Is.EqualTo(1),
                        "Eine gewöhnliche Nachricht geht an eine Resource, nicht an alle.");

        }

        #endregion

        #region ANegativePriority_ReceivesNothingFromTheAccount()

        /// <summary>
        /// Eine Resource mit negativer Priorität bekommt nichts, was an den
        /// Bare-JID ging — bleibt aber gerichtet ansprechbar
        /// (Abschnitte 8.5.2.1.1 und 8.5.3.1).
        /// </summary>
        /// <remarks>
        /// Beide Hälften gehören zusammen. Ohne die zweite wäre die negative
        /// Priorität ein Abmelden, und das ist sie gerade nicht: Das Gerät
        /// bleibt erreichbar, es hält sich nur aus dem Verkehr heraus, der an
        /// das Konto gerichtet ist.
        /// </remarks>
        [Test]
        public async Task ANegativePriority_ReceivesNothingFromTheAccount()
        {

            var alice     = await ConnectClientAsync("alice");
            var zweitgeraet = await ResourceAsync("bob", "Zweitgerät", -1);

            var eingang = new ConcurrentQueue<XMPPMessage>();
            zweitgeraet.OnMessage += m => eingang.Enqueue(m);

            await alice.SendRawAsync(
                      $"<message to='{zweitgeraet.BareJid}' type='chat' id='an-das-konto'>" +
                      "<body>An das Konto</body></message>");

            // Gerichtet an dieselbe Resource - das muss ankommen.
            await alice.SendRawAsync(
                      $"<message to='{zweitgeraet.FullJid}' type='chat' id='an-die-resource'>" +
                      "<body>An die Resource</body></message>");

            await WaitFor(() => eingang.Any(m => m.MessageId == "an-die-resource"),
                          "die gerichtete Nachricht");

            Assert.That(eingang.Any(m => m.MessageId == "an-das-konto"), Is.False,
                        "Was an das Konto ging, darf eine negative Priorität nicht erreichen.");

        }

        #endregion

        #region AnErrorToAnAccount_IsSilentlyIgnored()

        /// <summary>
        /// Eine Fehler-Nachricht an den Bare-JID wird stillschweigend
        /// übergangen (Abschnitt 8.5.2.1.1).
        /// </summary>
        /// <remarks>
        /// Auf einen Fehler mit einem Fehler zu antworten, wäre der Anfang
        /// einer Schleife. An eine passende Resource gerichtet muss er dagegen
        /// zugestellt werden — er ist ja die Antwort auf etwas, das genau diese
        /// Resource geschickt hat.
        /// </remarks>
        [Test]
        public async Task AnErrorToAnAccount_IsSilentlyIgnored()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var beiBob = new ConcurrentQueue<StanzaError>();
            bob.Connection.OnStanzaError += (from, e) => beiBob.Enqueue(e);

            var beiAlice = new ConcurrentQueue<StanzaError>();
            alice.Connection.OnStanzaError += (from, e) => beiAlice.Enqueue(e);

            const String fehlerRumpf = "<error type='cancel'>" +
                                       "<service-unavailable xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                                       "</error>";

            await alice.SendRawAsync(
                      $"<message to='{bob.BareJid}' type='error' id='an-das-konto'>{fehlerRumpf}</message>");

            await alice.SendRawAsync(
                      $"<message to='{bob.FullJid}' type='error' id='an-die-resource'>{fehlerRumpf}</message>");

            await WaitFor(() => !beiBob.IsEmpty, "den gerichteten Fehler");

            Assert.Multiple(() =>
            {

                Assert.That(beiBob, Has.Count.EqualTo(1),
                            "Nur der gerichtete Fehler darf ankommen.");

                Assert.That(beiAlice, Is.Empty,
                            "Und auf einen Fehler folgt kein Fehler.");

            });

        }

        #endregion

        #region ThePriorityIsReadFromThePresence()

        /// <summary>
        /// Die Priorität wird aus der Presence gelesen; fehlt sie oder ist sie
        /// unbrauchbar, gilt 0.
        /// </summary>
        /// <remarks>
        /// Eine unlesbare Zahl darf keine Zustellung verhindern - sie ist ein
        /// Wunsch des Clients und kein Vertrag. Der Bereich ist nach RFC 6121,
        /// Abschnitt 4.7.2.3 auf -128 bis +127 begrenzt.
        /// </remarks>
        [Test]
        public void ThePriorityIsReadFromThePresence()
        {

            Assert.Multiple(() =>
            {

                Assert.That(XMPPSession.ReadPriority("<presence/>"), Is.EqualTo(0));

                Assert.That(XMPPSession.ReadPriority("<presence><priority>5</priority></presence>"),
                            Is.EqualTo(5));

                Assert.That(XMPPSession.ReadPriority("<presence><priority>-1</priority></presence>"),
                            Is.EqualTo(-1));

                Assert.That(XMPPSession.ReadPriority("<presence><priority>viel</priority></presence>"),
                            Is.EqualTo(0),
                            "Unbrauchbares gilt als 0 und nicht als Fehler.");

                Assert.That(XMPPSession.ReadPriority("<presence><priority>9999</priority></presence>"),
                            Is.EqualTo(127),
                            "Der Bereich endet bei +127.");

            });

        }

        #endregion

    }

}
