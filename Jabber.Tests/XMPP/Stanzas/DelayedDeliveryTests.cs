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

using System.Xml.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// XEP-0203: Der Stempel, der sagt, dass eine Nachricht nicht jetzt
    /// entstanden ist.
    /// </summary>
    /// <remarks>
    /// Geprüft wird hier die Lesung an der Stanza; dass sie im Client ankommt
    /// und die angezeigte Zeit bestimmt, steht in
    /// <c>OfflineMessageTests.AStoredMessage_KeepsTheTimeItWasWritten</c>.
    /// </remarks>
    [TestFixture]
    public class DelayedDeliveryTests
    {

        #region Hilfsfunktionen

        private static XElement Nachricht(String inhalt)
            => XElement.Parse($"<message xmlns='jabber:client' from='bob@example' " +
                              $"to='alice@example'>{inhalt}<body>Hallo</body></message>");

        #endregion


        #region AStamp_IsRead()

        /// <summary>
        /// Der gewöhnliche Fall: Zeitpunkt und Urheber.
        /// </summary>
        [Test]
        public void AStamp_IsRead()
        {

            var gelesen = DelayedDelivery.TryRead(
                              Nachricht("<delay xmlns='urn:xmpp:delay' from='example' " +
                                        "stamp='2026-07-31T20:14:05Z'>Offline Storage</delay>"),
                              out var stempel,
                              out var von);

            Assert.Multiple(() =>
            {

                Assert.That(gelesen, Is.True);

                Assert.That(stempel.UtcDateTime,
                            Is.EqualTo(new DateTime(2026, 7, 31, 20, 14, 5, DateTimeKind.Utc)));

                Assert.That(von, Is.EqualTo("example"));

            });

        }

        #endregion

        #region TheStampKeepsItsZone()

        /// <summary>
        /// Der Zeitzonenteil wird gelesen und nicht überschrieben.
        /// </summary>
        /// <remarks>
        /// XEP-0203, Abschnitt 3 verlangt UTC, aber die Lesung darf sich nicht
        /// darauf verlassen: Ein Stempel mit Zonenangabe ist eindeutig, und wer
        /// ihn in die Zeitzone <i>dieses</i> Rechners dreht, verschiebt eine
        /// Nachricht aus einem anderen Land um Stunden. Was gemeint ist, steht
        /// in der Zeichenkette und nicht in der Umgebung.
        /// </remarks>
        [Test]
        public void TheStampKeepsItsZone()
        {

            var gelesen = DelayedDelivery.TryRead(
                              Nachricht("<delay xmlns='urn:xmpp:delay' stamp='2026-07-31T22:14:05+02:00'/>"),
                              out var stempel,
                              out _);

            Assert.Multiple(() =>
            {
                Assert.That(gelesen, Is.True);
                Assert.That(stempel.Offset, Is.EqualTo(TimeSpan.FromHours(2)));
                Assert.That(stempel.UtcDateTime.Hour, Is.EqualTo(20));
            });

        }

        #endregion

        #region WithoutAStamp_NothingIsRead()

        /// <summary>Eine gewöhnliche Nachricht trägt keinen.</summary>
        [Test]
        public void WithoutAStamp_NothingIsRead()
        {
            Assert.That(DelayedDelivery.TryRead(Nachricht(""), out _, out _),
                        Is.False);
        }

        #endregion

        #region AnUnreadableStamp_CountsAsNone()

        /// <summary>
        /// Was sich nicht lesen lässt, gilt wie kein Stempel.
        /// </summary>
        /// <remarks>
        /// Er kommt von der Gegenstelle, und was von dort kommt, darf hier
        /// nichts umwerfen. Die Nachricht ist dann eben so alt, wie sie
        /// angekommen ist - das ist die schlechtere Auskunft, aber keine
        /// falsche Uhrzeit und kein Absturz.
        ///
        /// Der letzte Fall kam durch eine überlebende Mutation dazu: Ein
        /// Stempel <b>ohne Zonenangabe</b> verstösst gegen Abschnitt 3, liess
        /// sich aber lesen - und wurde als hiesige Zeit gedeutet. Das ist die
        /// schlechteste aller Auslegungen: Die Nachricht verschiebt sich um
        /// genau den Zonenunterschied, sieht dabei aber vollkommen plausibel
        /// aus.
        /// </remarks>
        [TestCase("<delay xmlns='urn:xmpp:delay' stamp='gestern abend'/>",  TestName = "Kein Zeitpunkt")]
        [TestCase("<delay xmlns='urn:xmpp:delay' stamp=''/>",               TestName = "Leerer Stempel")]
        [TestCase("<delay xmlns='urn:xmpp:delay'/>",                        TestName = "Ohne Attribut")]
        [TestCase("<delay xmlns='urn:xmpp:delay' stamp='2026-07-31T20:14:05'/>",
                  TestName = "Ohne Zonenangabe")]
        public void AnUnreadableStamp_CountsAsNone(String delay)
        {
            Assert.That(DelayedDelivery.TryRead(Nachricht(delay), out _, out _),
                        Is.False);
        }

        #endregion

        #region AStampFromAnotherNamespace_IsIgnored()

        /// <summary>
        /// Das alte <c>jabber:x:delay</c> aus XEP-0091 wird nicht gelesen.
        /// </summary>
        /// <remarks>
        /// XEP-0091 ist von der XSF als <i>Obsolete</i> zurückgezogen, und sein
        /// Zeitformat ist ein anderes (<c>CCYYMMDDThh:mm:ss</c>). Es hier
        /// mitzulesen hiesse, ein zweites Format zu pflegen, das niemand mehr
        /// schickt - und zwar an genau der Stelle, an der ein Fehler wieder
        /// eine falsche Uhrzeit ergäbe.
        /// </remarks>
        [Test]
        public void AStampFromAnotherNamespace_IsIgnored()
        {
            Assert.That(DelayedDelivery.TryRead(
                            Nachricht("<x xmlns='jabber:x:delay' stamp='20260731T20:14:05'/>"),
                            out _, out _),
                        Is.False);
        }

        #endregion

        #region AStampInsideAForwardedMessage_IsNotTheOuterOne()

        /// <summary>
        /// Der Stempel einer eingepackten Nachricht datiert nicht die
        /// äussere.
        /// </summary>
        /// <remarks>
        /// Der Fall, für den die Lesung nur direkte Kinder ansieht: Ein Carbon
        /// (XEP-0280) und eine Weiterleitung (XEP-0297) bringen in ihrem
        /// <c>&lt;forwarded/&gt;</c> die <i>innere</i> Nachricht samt deren
        /// Stempel mit. Wer die ganze Stanza durchsucht, datiert die äussere
        /// auf die Zeit der inneren - und liegt genau dann falsch, wenn es
        /// darauf ankommt.
        /// </remarks>
        [Test]
        public void AStampInsideAForwardedMessage_IsNotTheOuterOne()
        {

            var carbon = Nachricht(
                             "<received xmlns='urn:xmpp:carbons:2'>" +
                             "<forwarded xmlns='urn:xmpp:forward:0'>" +
                             "<delay xmlns='urn:xmpp:delay' stamp='2020-01-01T00:00:00Z'/>" +
                             "<message xmlns='jabber:client'><body>innen</body></message>" +
                             "</forwarded></received>");

            Assert.That(DelayedDelivery.TryRead(carbon, out _, out _), Is.False,
                        "Der Stempel der inneren Nachricht wurde der äusseren zugeschrieben.");

        }

        #endregion

    }

}
