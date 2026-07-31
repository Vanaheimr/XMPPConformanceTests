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
    /// Punycode nach RFC 3492, gegen die Beispiele aus Abschnitt 7.1.
    /// </summary>
    /// <remarks>
    /// Der RFC liefert seine Prüfsteine selbst mit: elf Zeichenketten in acht
    /// Schriften, jede mit ihrer kodierten Form. Gegen sie zu rechnen ist der
    /// Unterschied zwischen „mein Kodierer und mein Dekodierer sind sich einig"
    /// und „meine Kodierung ist die, die alle anderen auch lesen".
    ///
    /// Beide Richtungen stehen hier, und beide werden gebraucht: Dekodiert wird,
    /// um zu sehen, was ein A-Label bedeutet; kodiert wird, um zu prüfen, dass
    /// es die <b>einzige</b> Schreibweise dieser Bedeutung ist (RFC 5891,
    /// Abschnitt 4.2.2 - eine andere wäre eine zweite Adresse für dasselbe).
    /// </remarks>
    [TestFixture]
    public class PunycodeTests
    {

        #region Data

        /// <summary>
        /// RFC 3492, Abschnitt 7.1 - die Beispiele mit ihrer kodierten Form.
        /// </summary>
        private static readonly (String Klartext, String Kodiert, String Schrift)[] Beispiele =
        [
            ("ليهمابتكلموشعربي؟",
             "egbpdaj6bu4bxfgehfvwxn",
             "Arabisch (A)"),

            ("他们为什么不说中文",
             "ihqwcrb4cv8a8dqg056pqjye",
             "Chinesisch, vereinfacht (B)"),

            ("他們爲什麽不說中文",
             "ihqwctvzc91f659drss3x8bo0yb",
             "Chinesisch, traditionell (C)"),

            ("Pročprostěnemluvíčesky",
             "Proprostnemluvesky-uyb24dma41a",
             "Tschechisch (D)"),

            ("למההםפשוטלאמדבריםעברית",
             "4dbcagdahymbxekheh6e0a7fei0b",
             "Hebräisch (E)"),

            ("यहलोगहिन्दीक्योंनहींबोलसकतेहैं",
             "i1baa7eci9glrd9b2ae1bj0hfcgg6iyaf8o0a1dig0cd",
             "Hindi, Devanagari (F)"),

            ("なぜみんな日本語を話してくれないのか",
             "n8jok5ay5dzabd5bym9f0cm5685rrjetr6pdxa",
             "Japanisch (G)"),

            ("PorquénopuedensimplementehablarenEspañol",
             "PorqunopuedensimplementehablarenEspaol-fmd56a",
             "Spanisch (I)"),

            ("TạisaohọkhôngthểchỉnóitiếngViệt",
             "TisaohkhngthchnitingVit-kjcr8268qyxafd2f1b9g",
             "Vietnamesisch (J)"),

            ("3年B組金八先生",
             "3B-ww4c5e180e575a65lsy2b",
             "Japanisch (L) - mit ASCII dazwischen"),

            ("ひとつ屋根の下2",
             "2-u9tlzr9756bt3uc0v",
             "Japanisch (O) - ASCII am Ende")
        ];

        #endregion


        #region Rfc3492_Examples_Decode()

        /// <summary>
        /// Jede kodierte Form ergibt ihren Klartext.
        /// </summary>
        [Test]
        public void Rfc3492_Examples_Decode()
        {

            Assert.Multiple(() =>
            {
                foreach (var (klartext, kodiert, schrift) in Beispiele)
                    Assert.That(Punycode.Decode(kodiert), Is.EqualTo(klartext), schrift);
            });

        }

        #endregion

        #region Rfc3492_Examples_Encode()

        /// <summary>
        /// Und jeder Klartext ergibt genau diese kodierte Form.
        /// </summary>
        [Test]
        public void Rfc3492_Examples_Encode()
        {

            Assert.Multiple(() =>
            {
                foreach (var (klartext, kodiert, schrift) in Beispiele)
                    Assert.That(Punycode.Encode(klartext), Is.EqualTo(kodiert), schrift);
            });

        }

        #endregion

        #region BrokenInput_IsRefusedNotGuessed()

        /// <summary>
        /// Was kein Punycode ist, ergibt <c>null</c> - und keine Ausnahme.
        /// </summary>
        /// <remarks>
        /// Der Inhalt kommt aus einer Adresse, die irgendjemand geschickt hat.
        /// Eine Ausnahme mitten in der Stanza-Behandlung wäre die falsche
        /// Antwort auf „das ist kein gültiges Label".
        /// </remarks>
        [Test]
        public void BrokenInput_IsRefusedNotGuessed()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Punycode.Decode("$"),           Is.Null, "keine Ziffer des 36er-Alphabets");
                Assert.That(Punycode.Decode("abc-ä"),       Is.Null, "Nicht-ASCII im kodierten Teil");
                Assert.That(Punycode.Decode("9999999999"),  Is.Null, "Überlauf");
                Assert.That(Punycode.Decode(""),            Is.Null, "leer");

                // Gegenprobe zur Zeile darüber: 'a-' ist kein Bruch, sondern
                // die richtige Kodierung von 'a'. Ohne sie stünde hier die
                // Vermutung, jeder Trenner am Ende sei ein Fehler.
                Assert.That(Punycode.Decode("a-"),          Is.EqualTo("a"));
            });

        }

        #endregion

        #region PureAscii_StaysItself()

        /// <summary>
        /// Reines ASCII bleibt ASCII - mit dem Trenner am Ende.
        /// </summary>
        [Test]
        public void PureAscii_StaysItself()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Punycode.Encode("abc"),  Is.EqualTo("abc-"));
                Assert.That(Punycode.Decode("abc-"), Is.EqualTo("abc"));

                // RFC 3492, Abschnitt 5: Die Ziffern sind schreibweisenlos -
                // 'T' zählt wie 't'. Kodiert wird trotzdem nur klein, und
                // genau daran erkennt ein A-Label seine kanonische Form.
                Assert.That(Punycode.Decode("TDA"), Is.EqualTo("ü"));
                Assert.That(Punycode.Encode("ü"),   Is.EqualTo("tda"));
            });

        }

        #endregion

    }

}
