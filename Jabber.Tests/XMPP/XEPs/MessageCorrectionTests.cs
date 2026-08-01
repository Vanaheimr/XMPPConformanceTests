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
using System.Xml.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// XEP-0308: „Ich meinte: morgen." - eine Nachricht ersetzt die vorige.
    /// </summary>
    /// <remarks>
    /// Geprüft wird beides: die Lesung an der Stanza und der Weg über zwei
    /// echte Clients. Das Zweite ist hier nicht Zierde - die Korrektur nennt
    /// eine <c>id</c>, die auf der anderen Seite <b>wiedererkannt</b> werden
    /// muss, und ob die beiden Seiten dieselbe meinen, sagt nur ein Durchlauf.
    /// </remarks>
    [TestFixture]
    public class MessageCorrectionTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private static XElement Nachricht(String inhalt)
            => XElement.Parse($"<message xmlns='jabber:client' from='bob@example' " +
                              $"to='alice@example' id='neu'>{inhalt}<body>Doch nicht</body></message>");

        #endregion


        #region TheReplacedId_IsRead()

        /// <summary>Der gewöhnliche Fall.</summary>
        [Test]
        public void TheReplacedId_IsRead()
        {
            Assert.That(MessageCorrection.ReplacedId(
                            Nachricht("<replace id='vorher' xmlns='urn:xmpp:message-correct:0'/>")),
                        Is.EqualTo("vorher"));
        }

        #endregion

        #region WithoutAReplace_NothingIsRead()

        /// <summary>Eine gewöhnliche Nachricht berichtigt nichts.</summary>
        [Test]
        public void WithoutAReplace_NothingIsRead()
        {
            Assert.That(MessageCorrection.ReplacedId(Nachricht("")), Is.Null);
        }

        #endregion

        #region AnEmptyId_CountsAsNone()

        /// <summary>
        /// Eine leere <c>id</c> gilt wie keine.
        /// </summary>
        /// <remarks>
        /// Sie zeigt auf nichts, und eine Ersetzung ohne Ziel ist keine. Ohne
        /// diese Prüfung erschiene die Nachricht als Korrektur einer Nachricht
        /// ohne Namen - und die Oberfläche suchte etwas, das es nicht gibt.
        /// </remarks>
        [Test]
        public void AnEmptyId_CountsAsNone()
        {
            Assert.That(MessageCorrection.ReplacedId(
                            Nachricht("<replace id='' xmlns='urn:xmpp:message-correct:0'/>")),
                        Is.Null);
        }

        #endregion

        #region AReplaceInsideAForwardedMessage_IsNotTheOuterOne()

        /// <summary>
        /// Der Korrekturvermerk einer eingepackten Nachricht gehört nicht der
        /// äusseren.
        /// </summary>
        /// <remarks>
        /// Dieselbe Falle wie beim Verzugsstempel in D59: Ein Carbon bringt in
        /// seinem <c>&lt;forwarded/&gt;</c> eine vollständige eigene Nachricht
        /// mit. Wer die ganze Stanza durchsucht, erklärt die äussere zur
        /// Berichtigung von etwas, das sie nie geschickt hat.
        /// </remarks>
        [Test]
        public void AReplaceInsideAForwardedMessage_IsNotTheOuterOne()
        {

            var carbon = Nachricht(
                             "<received xmlns='urn:xmpp:carbons:2'>" +
                             "<forwarded xmlns='urn:xmpp:forward:0'>" +
                             "<message xmlns='jabber:client'>" +
                             "<replace id='innen' xmlns='urn:xmpp:message-correct:0'/>" +
                             "<body>innen</body></message>" +
                             "</forwarded></received>");

            Assert.That(MessageCorrection.ReplacedId(carbon), Is.Null);

        }

        #endregion

        #region ACorrection_ArrivesAsSuch()

        /// <summary>
        /// Über die Leitung: Alice berichtigt, Bob sieht die Berichtigung -
        /// und weiss, welche Nachricht sie ablöst.
        /// </summary>
        /// <remarks>
        /// Die <c>id</c> ist der ganze Punkt. Ohne sie wäre die Korrektur eine
        /// zweite Nachricht, und der Empfänger stünde vor zwei Zeilen ohne
        /// Anhaltspunkt, welche gilt.
        /// </remarks>
        [Test]
        public async Task ACorrection_ArrivesAsSuch()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync();
            var bob   = await ConnectClientAsync("bob", createAccount: false);

            var eingang = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += m => eingang.Enqueue(m);

            var erste = await alice.SendMessageAsync($"bob@{Server.Domain}", "Bis heute Abend");

            await WaitFor(() => eingang.Count == 1, "die erste Nachricht");

            var korrektur = await alice.CorrectLastMessageAsync("Bis morgen Abend",
                                                                $"bob@{Server.Domain}");

            await WaitFor(() => eingang.Count == 2, "die Berichtigung");

            eingang.TryDequeue(out var alt);
            eingang.TryDequeue(out var neu);

            Assert.Multiple(() =>
            {

                Assert.That(alt!.IsCorrection, Is.False,
                            "Die erste Nachricht berichtigt nichts.");

                Assert.That(neu!.IsCorrection, Is.True);

                Assert.That(neu.ReplacesId, Is.EqualTo(erste),
                            "Die Berichtigung zeigt nicht auf die Nachricht, die sie ablöst.");

                Assert.That(neu.MessageId, Is.Not.EqualTo(erste),
                            "XEP-0308: Die Korrektur trägt eine eigene id.");

                Assert.That(neu.Body, Is.EqualTo("Bis morgen Abend"),
                            "Der Body ist der volle neue Text und nicht die Änderung daran.");

                Assert.That(korrektur, Is.EqualTo(neu.MessageId));

            });

        }

        #endregion

        #region ACorrectionCanBeCorrected()

        /// <summary>
        /// Eine Berichtigung wird selbst zur letzten Nachricht.
        /// </summary>
        /// <remarks>
        /// Kein Sonderfall, sondern der übliche: Wer sich vertippt, vertippt
        /// sich auch in der Berichtigung. Zeigte die zweite Korrektur weiter
        /// auf das Original, hinge beim Empfänger die erste Korrektur in der
        /// Luft - sie wäre durch nichts abgelöst und stünde neben der zweiten.
        /// </remarks>
        [Test]
        public async Task ACorrectionCanBeCorrected()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync();
            var bob   = await ConnectClientAsync("bob", createAccount: false);

            var eingang = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += m => eingang.Enqueue(m);

            var bob_jid = $"bob@{Server.Domain}";

            await alice.SendMessageAsync(bob_jid, "Bis heute");

            await WaitFor(() => eingang.Count == 1, "die erste Nachricht");

            var erste = await alice.CorrectLastMessageAsync("Bis morgen", bob_jid);

            await WaitFor(() => eingang.Count == 2, "die erste Berichtigung");

            await alice.CorrectLastMessageAsync("Bis übermorgen", bob_jid);

            await WaitFor(() => eingang.Count == 3, "die zweite Berichtigung");

            var alle = eingang.ToArray();

            Assert.That(alle[2].ReplacesId, Is.EqualTo(erste),
                        "Die zweite Berichtigung löst die erste ab, nicht das Original.");

        }

        #endregion

        #region WithoutAPreviousMessage_ThereIsNothingToCorrect()

        /// <summary>
        /// An einen Empfänger, an den noch nichts hinausging, lässt sich
        /// nichts berichtigen.
        /// </summary>
        /// <remarks>
        /// Der Aufrufer bekommt null und keine erfundene Ersetzung. Eine
        /// Korrektur mit geratener <c>id</c> wäre schlimmer als keine: Beim
        /// Empfänger löst sie eine Nachricht ab, die er nie bekommen hat, oder
        /// - schlimmer - eine fremde.
        /// </remarks>
        [Test]
        public async Task WithoutAPreviousMessage_ThereIsNothingToCorrect()
        {

            var alice = await ConnectClientAsync();

            Assert.That(await alice.CorrectLastMessageAsync("zu spät", $"niemand@{Server.Domain}"),
                        Is.Null);

        }

        #endregion

        #region TheFeature_IsAnnounced()

        /// <summary>
        /// Der Client kündigt XEP-0308 in disco#info an.
        /// </summary>
        /// <remarks>
        /// Abschnitt 4 verlangt es, und der Grund ist praktisch: Ohne die
        /// Ankündigung muss ein Gegenüber annehmen, dass seine Korrektur als
        /// zweite Nachricht erscheint - und schickt dann lieber keine.
        /// </remarks>
        [Test]
        public async Task TheFeature_IsAnnounced()
        {

            var alice = await ConnectClientAsync();

            Assert.That(alice.Connection.Disco!.LocalFeatures,
                        Does.Contain("urn:xmpp:message-correct:0"));

        }

        #endregion

    }

}
