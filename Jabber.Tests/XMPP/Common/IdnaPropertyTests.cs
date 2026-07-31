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
    /// RFC 5892, Abschnitt 1: die abgeleitete Eigenschaft eines Codepoints für
    /// IDNA2008 - Zweig für Zweig.
    /// </summary>
    /// <remarks>
    /// <b>Dieselben Bausteine, eine andere Leiter, andere Antworten.</b> Der
    /// Vergleich mit <see cref="PrecisPropertyTests"/> ist der Inhalt dieser
    /// Sammlung:
    ///
    /// <list type="bullet">
    ///   <item><c>_</c> ist in einem Localpart zulässig (ASCII7) und in einem
    ///         Domain-Label nicht (LDH kennt nur Bindestrich, Ziffern und
    ///         Kleinbuchstaben).</item>
    ///   <item><c>A</c> ebenso: In einem Domainnamen gibt es keine
    ///         Grossbuchstaben, sie sind nach Abschnitt 2.2 unstabil.</item>
    ///   <item>Ein Symbol ist in einem Resourcepart zulässig (FreeformClass)
    ///         und in einem Label nicht - die IDNA-Leiter endet ohne
    ///         Auffangzweig für Symbole und Satzzeichen.</item>
    ///   <item>U+2163 ist für PRECIS FREE_PVAL (HasCompat), für IDNA
    ///         DISALLOWED (Unstable).</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    public class IdnaPropertyTests
    {

        #region TheLadderOfRfc5892()

        /// <summary>
        /// Ein Fall je Zweig, in der Reihenfolge des Abschnitts.
        /// </summary>
        [Test]
        public void TheLadderOfRfc5892()
        {

            var faelle = new (UInt32 CodePoint, IdnaProperty Erwartet, String Zweig)[]
            {
                (0x00DF, IdnaProperty.PValid,      "Exceptions: ß - der berühmteste Fall von IDNA2008"),
                (0x03C2, IdnaProperty.PValid,      "Exceptions: Schluss-Sigma"),
                (0x00B7, IdnaProperty.ContextO,    "Exceptions: MIDDLE DOT"),
                (0x0660, IdnaProperty.ContextO,    "Exceptions: ARABIC-INDIC DIGIT ZERO"),
                (0x0640, IdnaProperty.Disallowed,  "Exceptions: ARABIC TATWEEL"),
                (0x0378, IdnaProperty.Unassigned,  "Unassigned"),
                (0x0061, IdnaProperty.PValid,      "LDH: 'a'"),
                (0x0039, IdnaProperty.PValid,      "LDH: '9'"),
                (0x002D, IdnaProperty.PValid,      "LDH: der Bindestrich"),
                (0x0041, IdnaProperty.Disallowed,  "Unstable: 'A' - Domainnamen sind kleingeschrieben"),
                (0x005F, IdnaProperty.Disallowed,  "Rest: '_' ist kein LDH"),
                (0x002B, IdnaProperty.Disallowed,  "Rest: '+' ist kein LDH"),
                (0x200C, IdnaProperty.ContextJ,    "JoinControl: ZWNJ"),
                (0x2163, IdnaProperty.Disallowed,  "Unstable: ROMAN NUMERAL FOUR"),
                (0x0130, IdnaProperty.Disallowed,  "Unstable: LATIN CAPITAL LETTER I WITH DOT ABOVE"),
                (0x00AD, IdnaProperty.Disallowed,  "IgnorableProperties: SOFT HYPHEN"),
                (0x3164, IdnaProperty.Disallowed,  "IgnorableProperties: HANGUL FILLER - trotz Kategorie Lo"),
                (0xFE00, IdnaProperty.Disallowed,  "IgnorableProperties: VARIATION SELECTOR-1 - trotz Kategorie Mn"),
                (0x180B, IdnaProperty.Disallowed,  "IgnorableProperties: MONGOLIAN FREE VARIATION SELECTOR - trotz Kategorie Mn"),
                (0x0020, IdnaProperty.Disallowed,  "IgnorableProperties: White_Space"),
                (0xFDD0, IdnaProperty.Disallowed,  "IgnorableProperties: Nichtzeichen"),
                (0x20D0, IdnaProperty.Disallowed,  "IgnorableBlocks: Combining Marks for Symbols - trotz Kategorie Mn"),
                (0x1D165, IdnaProperty.Disallowed, "IgnorableBlocks: Musical Symbols - trotz Kategorie Mc"),
                (0x1100, IdnaProperty.Disallowed,  "OldHangulJamo"),
                (0x00E9, IdnaProperty.PValid,      "LetterDigits: é"),
                (0x05D0, IdnaProperty.PValid,      "LetterDigits: ALEF"),
                (0x4E2D, IdnaProperty.PValid,      "LetterDigits: 中"),
                (0x265A, IdnaProperty.Disallowed,  "Rest: ein Symbol, und die Leiter hat keinen Auffangzweig"),
                (0x002E, IdnaProperty.Disallowed,  "Rest: der Punkt trennt Labels, er steht nicht in einem")
            };

            Assert.Multiple(() =>
            {
                foreach (var (codePoint, erwartet, zweig) in faelle)
                    Assert.That(Idna.DerivedProperty(codePoint), Is.EqualTo(erwartet),
                                $"U+{codePoint:X4} - {zweig}");
            });

        }

        #endregion

        #region WhereTheTwoLaddersDisagree()

        /// <summary>
        /// Dieselben Codepoints, zwei Vorschriften, zwei Antworten.
        /// </summary>
        /// <remarks>
        /// Diese Tabelle ist der Grund, warum die beiden Leitern getrennt
        /// bleiben. Legte man sie zusammen, müssten alle vier Zeilen zu
        /// Sonderfällen werden - und Sonderfälle sind das, was man später nicht
        /// mehr nachlesen kann.
        /// </remarks>
        [Test]
        public void WhereTheTwoLaddersDisagree()
        {

            Assert.Multiple(() =>
            {

                Assert.That(Precis.IsIdentifierClass(0x005F),  Is.True,
                            "Der Unterstrich gehört in einen Localpart ...");
                Assert.That(Idna.DerivedProperty(0x005F),      Is.EqualTo(IdnaProperty.Disallowed),
                            "... und nicht in ein Domain-Label.");

                Assert.That(Precis.IsFreeformClass(0x265A),    Is.True,
                            "Ein Symbol gehört in einen Resourcepart ...");
                Assert.That(Idna.DerivedProperty(0x265A),      Is.EqualTo(IdnaProperty.Disallowed),
                            "... und nicht in ein Domain-Label.");

                Assert.That(Precis.IsFreeformClass(0x2163),    Is.True,
                            "Die römische Vier ist für PRECIS eine Freiform-Zeichen ...");
                Assert.That(Idna.DerivedProperty(0x2163),      Is.EqualTo(IdnaProperty.Disallowed),
                            "... und für IDNA unstabil.");

                Assert.That(Precis.IsIdentifierClass(0x0041),  Is.True,
                            "Ein 'A' darf in einem Localpart stehen (er wird kleingeschrieben) ...");
                Assert.That(Idna.DerivedProperty(0x0041),      Is.EqualTo(IdnaProperty.Disallowed),
                            "... und ist als Codepoint eines Labels unzulässig.");

            });

        }

        #endregion

    }

}
