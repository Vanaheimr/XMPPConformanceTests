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
    /// Was die abgeleiteten Eigenschaften am JID ändern - die Fälle, an denen
    /// sich die Näherung von RFC 8264 unterscheidet.
    /// </summary>
    /// <remarks>
    /// Die alte Prüfung fragte nach Unicode-Kategorie und
    /// Kompatibilitätszerlegung. Sie traf die Beispiele aus RFC 7622, und die
    /// bestehen weiterhin - <see cref="JidFormatTests"/> hält beide Tabellen.
    /// Hier stehen die Fälle, die sie <b>nicht</b> traf: Jeder einzelne wäre
    /// vorher durchgegangen oder abgewiesen worden, und zwar falsch.
    /// </remarks>
    [TestFixture]
    public class JidClassMembershipTests
    {

        #region Data

        private const String Tatweel        = "ـ";  // ARABIC TATWEEL
        private const String NkoLajanyalan  = "ߺ";  // NKO LAJANYALAN
        private const String MiddleDot      = "·";  // MIDDLE DOT
        private const String HangulChoseong = "ᄀ";  // HANGUL CHOSEONG KIYEOK
        private const String SoftHyphen     = "­";  // SOFT HYPHEN
        private const String ArabicIndic    = "٠١";
        private const String ExtArabicIndic = "۰۱";
        private const String ChessKing      = "♚";  // BLACK CHESS KING

        private static Boolean IstJid(String jid)
            => JidUtilities.TryParse(jid, out _);

        #endregion


        #region ExceptionsBeatTheCategory()

        /// <summary>
        /// Die Ausnahmeliste steht vor der Kategorie: Zwei Modifier Letters,
        /// die kein Localpart tragen darf.
        /// </summary>
        /// <remarks>
        /// U+0640 und U+07FA sind nach ihrer Kategorie (Lm) Buchstaben und
        /// kamen deshalb durch. Sie sind es aber nicht: Der Tatweel ist ein
        /// Streckungsstrich, der überall und beliebig oft eingefügt werden
        /// kann, ohne etwas zu bedeuten. Aus einem Konto werden damit beliebig
        /// viele, die alle gleich aussehen.
        /// </remarks>
        [Test]
        public void ExceptionsBeatTheCategory()
        {

            Assert.Multiple(() =>
            {
                Assert.That(IstJid($"ju{Tatweel}liet@example.com"),       Is.False, "ARABIC TATWEEL");
                Assert.That(IstJid($"ju{NkoLajanyalan}liet@example.com"), Is.False, "NKO LAJANYALAN");
            });

        }

        #endregion

        #region TheDigitsOfOneKind_AreAllowed()

        /// <summary>
        /// Arabisch-indische Ziffern sind kontextabhängig zulässig - für sich
        /// ja, gemischt nein (RFC 5892, Anhang A.8 und A.9).
        /// </summary>
        [Test]
        public void TheDigitsOfOneKind_AreAllowed()
        {

            Assert.Multiple(() =>
            {

                Assert.That(IstJid($"{ArabicIndic}@example.com"),    Is.True,
                            "Eine Ziffernreihe für sich ist ein gültiger Localpart.");

                Assert.That(IstJid($"{ExtArabicIndic}@example.com"), Is.True);

                Assert.That(IstJid($"{ArabicIndic}{ExtArabicIndic}@example.com"), Is.False,
                            "Beide Reihen nebeneinander sehen gleich aus und bedeuten dasselbe.");

            });

        }

        #endregion

        #region TheContextualOnesDependOnTheirNeighbours()

        /// <summary>
        /// Ein kontextabhängiger Codepoint hängt an seiner Umgebung - der
        /// Mittelpunkt gehört zwischen zwei <c>l</c> (RFC 5892, Anhang A.3).
        /// </summary>
        /// <remarks>
        /// <c>col·la</c> ist ein katalanisches Wort und ein gültiger Localpart;
        /// <c>co·lla</c> ist dieselbe Zeichenmenge in anderer Reihenfolge und
        /// keiner. Dass beides <b>nicht</b> dasselbe Ergebnis hat, ist der
        /// ganze Inhalt von „kontextabhängig".
        /// </remarks>
        [Test]
        public void TheContextualOnesDependOnTheirNeighbours()
        {

            Assert.Multiple(() =>
            {
                Assert.That(IstJid($"col{MiddleDot}la@example.com"), Is.True);
                Assert.That(IstJid($"co{MiddleDot}lla@example.com"), Is.False);
            });

        }

        #endregion

        #region TheResourcepartIsFreeformNotAnything()

        /// <summary>
        /// Der Resourcepart nimmt die FreeformClass - Symbole und Leerzeichen
        /// ja, alte Hangul-Jamo und unsichtbare Zeichen nein.
        /// </summary>
        /// <remarks>
        /// U+1100 ist ein Buchstabe (Lo) und kam deshalb durch. RFC 8264,
        /// Abschnitt 9.9 schliesst die alten Jamo aus: Sie setzen sich zu
        /// Silben zusammen, die es fertig als eigene Codepoints gibt - zwei
        /// Schreibweisen für dasselbe Wort, und keine Normalisierung räumt das
        /// auf.
        /// </remarks>
        [Test]
        public void TheResourcepartIsFreeformNotAnything()
        {

            Assert.Multiple(() =>
            {

                Assert.That(IstJid($"juliet@example.com/{ChessKing}"),      Is.True,
                            "Ein Symbol gehört zur FreeformClass.");

                Assert.That(IstJid("juliet@example.com/mein Gerät"),        Is.True,
                            "Ein Leerzeichen ebenfalls.");

                Assert.That(IstJid($"juliet@example.com/{HangulChoseong}"), Is.False,
                            "Ein altes Hangul-Jamo nicht.");

                Assert.That(IstJid($"juliet@example.com/a{SoftHyphen}b"),   Is.False,
                            "Ein unsichtbares Zeichen nicht.");

                Assert.That(IstJid($"juliet@example.com/a{Tatweel}b"),      Is.False,
                            "Und die Ausnahmeliste gilt in beiden Klassen.");

            });

        }

        #endregion

        #region TheSymbolStaysOutOfTheLocalpart()

        /// <summary>
        /// Die Gegenprobe: Was der Resourcepart trägt, trägt der Localpart
        /// nicht.
        /// </summary>
        /// <remarks>
        /// Ohne sie wäre „beide Teile nehmen die FreeformClass" eine
        /// bestandene Lösung - und der Unterschied zwischen den beiden Klassen
        /// verschwände, ohne dass ein Test es merkt.
        /// </remarks>
        [Test]
        public void TheSymbolStaysOutOfTheLocalpart()
        {

            Assert.Multiple(() =>
            {
                Assert.That(IstJid($"{ChessKing}@example.com"),   Is.False, "Symbol");
                Assert.That(IstJid("mein Gerät@example.com"),     Is.False, "Leerzeichen");
            });

        }

        #endregion

    }

}
