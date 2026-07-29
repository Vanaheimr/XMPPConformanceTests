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
    /// Die Art einer Nachricht (RFC 6121, Abschnitt 5.2.2).
    /// </summary>
    /// <remarks>
    /// Bis hierher kam alles gleich an: Der Empfänger konnte den Zuruf einer
    /// Nachrichtenquelle nicht von der Zeile eines Bekannten unterscheiden, und
    /// die Zeile aus einem Raum nicht von einer an ihn allein gerichteten. Wo
    /// das nicht bloss die Anzeige betrifft, sondern das Verhalten, wird es
    /// heikel — der Client quittierte jede Nachricht, auch die aus einem Raum.
    /// </remarks>
    [TestFixture]
    public class MessageTypeTests : AXMPPTests
    {

        #region TheDefaultIsNormal()

        /// <summary>
        /// Fehlt das Attribut oder ist sein Wert unbekannt, gilt die Nachricht
        /// als <c>normal</c>.
        /// </summary>
        /// <remarks>
        /// RFC 6121, Abschnitt 5.2.2 ist hier ungewöhnlich deutlich und sagt
        /// MUSS. Der Grund liegt in der Zukunft: Eine spätere Erweiterung soll
        /// bei alten Empfängern als gewöhnliche Nachricht ankommen und nicht
        /// verschwinden.
        /// </remarks>
        [Test]
        public void TheDefaultIsNormal()
        {

            Assert.Multiple(() =>
            {

                Assert.That(MessageTypeExtensions.Parse(null),        Is.EqualTo(MessageType.Normal));
                Assert.That(MessageTypeExtensions.Parse(""),          Is.EqualTo(MessageType.Normal));
                Assert.That(MessageTypeExtensions.Parse("normal"),    Is.EqualTo(MessageType.Normal));

                // Unbekannt - und trotzdem keine Ablehnung.
                Assert.That(MessageTypeExtensions.Parse("shout"),     Is.EqualTo(MessageType.Normal));

                // Gross geschrieben ist es nicht derselbe Wert; XML-Attribute
                // dieser Art sind in RFC 6121 kleingeschrieben festgelegt.
                Assert.That(MessageTypeExtensions.Parse("Chat"),      Is.EqualTo(MessageType.Normal));

                Assert.That(MessageTypeExtensions.Parse("chat"),      Is.EqualTo(MessageType.Chat));
                Assert.That(MessageTypeExtensions.Parse("groupchat"), Is.EqualTo(MessageType.GroupChat));
                Assert.That(MessageTypeExtensions.Parse("headline"),  Is.EqualTo(MessageType.Headline));
                Assert.That(MessageTypeExtensions.Parse("error"),     Is.EqualTo(MessageType.Error));

            });

        }

        #endregion

        #region TheDefaultIsNotWrittenOut()

        /// <summary>
        /// <c>normal</c> ist der Vorgabewert und wird nicht geschrieben.
        /// </summary>
        [Test]
        public void TheDefaultIsNotWrittenOut()
        {

            Assert.Multiple(() =>
            {

                Assert.That(MessageType.Normal.AsAttribute(),    Is.Null);

                Assert.That(MessageType.Chat.AsAttribute(),      Is.EqualTo("chat"));
                Assert.That(MessageType.GroupChat.AsAttribute(), Is.EqualTo("groupchat"));
                Assert.That(MessageType.Headline.AsAttribute(),  Is.EqualTo("headline"));
                Assert.That(MessageType.Error.AsAttribute(),     Is.EqualTo("error"));

                // Und wieder zurück: was geschrieben wird, wird auch gelesen.
                foreach (var typ in Enum.GetValues<MessageType>())
                    Assert.That(MessageTypeExtensions.Parse(typ.AsAttribute()), Is.EqualTo(typ),
                                $"Hin und zurück verloren: {typ}");

            });

        }

        #endregion

        #region TheTypeReachesTheApplication()

        /// <summary>
        /// Die Art kommt beim Empfänger an — sonst hätte sie niemand.
        /// </summary>
        [Test]
        public async Task TheTypeReachesTheApplication()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var eingang = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += m => eingang.Enqueue(m);

            await alice.SendMessageAsync(bob.BareJid, "Aus dem Raum", MessageType.GroupChat);

            await WaitFor(() => !eingang.IsEmpty, "die Zustellung");

            eingang.TryDequeue(out var empfangen);

            Assert.Multiple(() =>
            {
                Assert.That(empfangen!.Type, Is.EqualTo(MessageType.GroupChat));
                Assert.That(empfangen.Body,  Is.EqualTo("Aus dem Raum"));
            });

        }

        #endregion

        #region AGroupchatMessage_IsNotAcknowledged()

        /// <summary>
        /// Der Kern: Auf eine Nachricht aus einem Raum wird nicht von selbst
        /// geantwortet.
        /// </summary>
        /// <remarks>
        /// Der Absender ist dort der Raum und nicht ein Mensch. Eine
        /// Empfangsbestätigung ginge an den Raum, und der reicht sie an alle
        /// darin weiter — aus einer stillen Quittung würde eine Wortmeldung vor
        /// Publikum, und zwar von jedem Anwesenden für jede Nachricht. Bei
        /// zwanzig Leuten im Raum sind das vierhundert Quittungen für zwanzig
        /// Zeilen.
        ///
        /// Geprüft wird über die Gegenprobe im selben Test: Dieselbe Nachricht
        /// als <c>chat</c> wird quittiert. Ohne sie bestünde der Test auch
        /// dann, wenn gar nichts mehr quittiert würde.
        /// </remarks>
        [Test]
        public async Task AGroupchatMessage_IsNotAcknowledged()
        {

            var alice      = await ConnectClientAsync("alice");
            var bob        = await ConnectClientAsync("bob");
            var bobSitzung = Server.SessionOf(bob.FullJid!)!;

            var eingang = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += m => eingang.Enqueue(m);

            // Von Hand, weil SendMessageAsync für einen Raum von sich aus keine
            // Bestätigung mehr anfordert - hier soll gerade der Empfänger
            // entscheiden.
            await alice.SendRawAsync(
                      $"<message to='{bob.BareJid}' type='groupchat' id='raum-1'>" +
                      "<body>Aus dem Raum</body>" +
                      "<request xmlns='urn:xmpp:receipts'/>" +
                      "<markable xmlns='urn:xmpp:chat-markers:0'/>" +
                      "</message>");

            await WaitFor(() => eingang.Any(m => m.MessageId == "raum-1"),
                          "die Zustellung der Raum-Nachricht");

            // Und nun dieselbe Nachricht als Gespräch unter vier Augen.
            await alice.SendRawAsync(
                      $"<message to='{bob.BareJid}' type='chat' id='direkt-1'>" +
                      "<body>Nur an dich</body>" +
                      "<request xmlns='urn:xmpp:receipts'/>" +
                      "</message>");

            // Beobachtet wird, was Bob hinausschickt - nicht, was bei Alice
            // ankommt: Alices Quittungsverfolgung kennt nur Nachrichten, die
            // sie selbst über SendMessageAsync abgeschickt hat, und meldete
            // eine Quittung auf eine rohe Stanza als Fälschungsversuch.
            await WaitFor(() => bobSitzung.Received.Any(f => f.Contains("id='direkt-1'",
                                                                        StringComparison.Ordinal)),
                          "die Quittung für die direkte Nachricht");

            Assert.That(bobSitzung.Received.Any(f => f.Contains("id='raum-1'",
                                                                StringComparison.Ordinal)),
                        Is.False,
                        "Auf eine Nachricht aus einem Raum darf weder Quittung noch Marker folgen.");

        }

        #endregion

        #region AHeadline_IsNotAcknowledged()

        /// <summary>
        /// Und ein Zuruf ebenso wenig — RFC 6121, Abschnitt 5.2.2: „no reply
        /// is expected".
        /// </summary>
        [Test]
        public async Task AHeadline_IsNotAcknowledged()
        {

            var alice      = await ConnectClientAsync("alice");
            var bob        = await ConnectClientAsync("bob");
            var bobSitzung = Server.SessionOf(bob.FullJid!)!;

            var eingang = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += m => eingang.Enqueue(m);

            await alice.SendRawAsync(
                      $"<message to='{bob.BareJid}' type='headline' id='zuruf-1'>" +
                      "<body>Kurs gefallen</body>" +
                      "<request xmlns='urn:xmpp:receipts'/>" +
                      "</message>");

            await WaitFor(() => eingang.Any(m => m.MessageId == "zuruf-1"),
                          "die Zustellung des Zurufs");

            await alice.SendRawAsync(
                      $"<message to='{bob.BareJid}' type='chat' id='direkt-2'>" +
                      "<body>Nur an dich</body>" +
                      "<request xmlns='urn:xmpp:receipts'/>" +
                      "</message>");

            await WaitFor(() => bobSitzung.Received.Any(f => f.Contains("id='direkt-2'",
                                                                        StringComparison.Ordinal)),
                          "die Quittung für die direkte Nachricht");

            Assert.Multiple(() =>
            {

                Assert.That(bobSitzung.Received.Any(f => f.Contains("id='zuruf-1'",
                                                                    StringComparison.Ordinal)),
                            Is.False,
                            "Ein Zuruf erwartet keine Antwort.");

                Assert.That(eingang.First(m => m.MessageId == "zuruf-1").Type,
                            Is.EqualTo(MessageType.Headline));

            });

        }

        #endregion

        #region ARoomMessage_RequestsNoReceipt()

        /// <summary>
        /// Und die andere Richtung: Wer in einen Raum schreibt, fordert keine
        /// Bestätigung an.
        /// </summary>
        /// <remarks>
        /// XEP-0184, Abschnitt 5.3 rät dem Absender ausdrücklich davon ab. Der
        /// Grund ist derselbe wie beim Empfänger, nur eine Ebene früher: Was
        /// nicht angefordert wird, muss auch niemand übergehen.
        /// </remarks>
        [Test]
        public async Task ARoomMessage_RequestsNoReceipt()
        {

            var alice   = await ConnectClientAsync("alice");
            var sitzung = Server.SessionOf(alice.FullJid!)!;

            await alice.SendMessageAsync($"bob@{Server.Domain}", "In den Raum",
                                         MessageType.GroupChat);

            await WaitFor(() => sitzung.Received.Any(f => f.Contains("In den Raum",
                                                                     StringComparison.Ordinal)),
                          "die abgeschickte Nachricht");

            var hinaus = sitzung.Received.First(f => f.Contains("In den Raum", StringComparison.Ordinal));

            Assert.Multiple(() =>
            {

                Assert.That(hinaus, Does.Contain("type='groupchat'"));

                Assert.That(hinaus, Does.Not.Contain("urn:xmpp:receipts"),
                            "In einen Raum wird keine Bestätigung angefordert.");

                Assert.That(hinaus, Does.Not.Contain("urn:xmpp:chat-markers"),
                            "Und kein Marker.");

            });

        }

        #endregion

    }

}
