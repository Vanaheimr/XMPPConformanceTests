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
    /// RFC 8264, Abschnitt 8: die abgeleitete Eigenschaft eines Codepoints -
    /// Zweig für Zweig.
    /// </summary>
    /// <remarks>
    /// Die Leiter aus Abschnitt 8 ist nicht nur eine Aufzählung von
    /// Kategorien, sondern eine <b>Reihenfolge</b>, und mehrere Codepoints
    /// gehören in mehr als eine davon. Wer sie als Menge liest statt als
    /// Leiter, bekommt andere Antworten:
    ///
    /// <list type="bullet">
    ///   <item>U+0640 (ARABIC TATWEEL) ist ein Modifier Letter und damit in
    ///         LetterDigits — die Ausnahmeliste steht aber davor und verbietet
    ///         ihn.</item>
    ///   <item>U+2163 (ROMAN NUMERAL FOUR) ist Nl und damit in
    ///         OtherLetterDigits — HasCompat steht davor.</item>
    ///   <item>U+00DF (ß) wäre ohne die Ausnahmeliste PVALID über
    ///         LetterDigits; die Ausnahme sagt dasselbe, aber aus einem
    ///         anderen Grund.</item>
    /// </list>
    ///
    /// Deshalb steht zu jedem Fall dabei, <i>welcher Zweig</i> ihn beantwortet.
    /// Ein Test, der nur das Ergebnis prüft, hielte eine Leiter mit vertauschten
    /// Sprossen für richtig, solange sich die Fälle nicht überschneiden.
    /// </remarks>
    [TestFixture]
    public class PrecisPropertyTests
    {

        #region TheLadderOfSection8()

        /// <summary>
        /// Ein Fall je Zweig, in der Reihenfolge des Abschnitts.
        /// </summary>
        [Test]
        public void TheLadderOfSection8()
        {

            var faelle = new (UInt32 CodePoint, PrecisProperty Erwartet, String Zweig)[]
            {
                (0x00DF, PrecisProperty.PValid,      "Exceptions: LATIN SMALL LETTER SHARP S"),
                (0x03C2, PrecisProperty.PValid,      "Exceptions: GREEK SMALL LETTER FINAL SIGMA"),
                (0x3007, PrecisProperty.PValid,      "Exceptions: IDEOGRAPHIC NUMBER ZERO"),
                (0x00B7, PrecisProperty.ContextO,    "Exceptions: MIDDLE DOT"),
                (0x0660, PrecisProperty.ContextO,    "Exceptions: ARABIC-INDIC DIGIT ZERO"),
                (0x06F9, PrecisProperty.ContextO,    "Exceptions: EXTENDED ARABIC-INDIC DIGIT NINE"),
                (0x0640, PrecisProperty.Disallowed,  "Exceptions: ARABIC TATWEEL - trotz Kategorie Lm"),
                (0x07FA, PrecisProperty.Disallowed,  "Exceptions: NKO LAJANYALAN - trotz Kategorie Lm"),
                (0x3031, PrecisProperty.Disallowed,  "Exceptions: VERTICAL KANA REPEAT MARK"),
                (0x0378, PrecisProperty.Unassigned,  "Unassigned: nicht vergeben"),
                (0x0061, PrecisProperty.PValid,      "ASCII7: 'a'"),
                (0x007E, PrecisProperty.PValid,      "ASCII7: '~' - die obere Grenze"),
                (0x200C, PrecisProperty.ContextJ,    "JoinControl: ZERO WIDTH NON-JOINER"),
                (0x200D, PrecisProperty.ContextJ,    "JoinControl: ZERO WIDTH JOINER"),
                (0x1100, PrecisProperty.Disallowed,  "OldHangulJamo: HANGUL CHOSEONG KIYEOK (L)"),
                (0x11A8, PrecisProperty.Disallowed,  "OldHangulJamo: HANGUL JONGSEONG KIYEOK (T)"),
                (0x00AD, PrecisProperty.Disallowed,  "PrecisIgnorableProperties: SOFT HYPHEN"),
                (0xFDD0, PrecisProperty.Disallowed,  "PrecisIgnorableProperties: Nichtzeichen"),
                (0xFFFE, PrecisProperty.Disallowed,  "PrecisIgnorableProperties: Nichtzeichen am Blockende"),
                (0x3164, PrecisProperty.Disallowed,  "PrecisIgnorableProperties: HANGUL FILLER - trotz Kategorie Lo"),
                (0x0009, PrecisProperty.Disallowed,  "Controls: Tabulator"),
                (0x007F, PrecisProperty.Disallowed,  "Controls: DEL - ASCII7 endet bei 7E"),
                (0x2163, PrecisProperty.FreePValid,  "HasCompat: ROMAN NUMERAL FOUR - zerfällt in 'IV'"),
                (0xFB01, PrecisProperty.FreePValid,  "HasCompat: Ligatur fi"),
                (0x00E9, PrecisProperty.PValid,      "LetterDigits: é"),
                (0x05D0, PrecisProperty.PValid,      "LetterDigits: ALEF"),
                (0x0488, PrecisProperty.FreePValid,  "OtherLetterDigits: Me"),
                (0x16EE, PrecisProperty.FreePValid,  "OtherLetterDigits: RUNIC ARLAUG SYMBOL (Nl)"),
                (0x0020, PrecisProperty.FreePValid,  "Spaces: das Leerzeichen ist kein ASCII7"),
                (0x00A0, PrecisProperty.FreePValid,  "Spaces: NO-BREAK SPACE"),
                (0x265A, PrecisProperty.FreePValid,  "Symbols: BLACK CHESS KING"),
                (0x2E00, PrecisProperty.FreePValid,  "Punctuation: RIGHT ANGLE SUBSTITUTION MARKER"),
                (0xE000, PrecisProperty.Disallowed,  "Rest: Private Use"),
                (0x0600, PrecisProperty.Disallowed,  "Rest: ARABIC NUMBER SIGN (Cf, nicht ignorierbar)")
            };

            Assert.Multiple(() =>
            {
                foreach (var (codePoint, erwartet, zweig) in faelle)
                    Assert.That(Precis.DerivedProperty(codePoint), Is.EqualTo(erwartet),
                                $"U+{codePoint:X4} - {zweig}");
            });

        }

        #endregion

        #region TheTwoClasses()

        /// <summary>
        /// IdentifierClass (RFC 8264, Abschnitt 4.2) nimmt nur PVALID,
        /// FreeformClass (Abschnitt 4.3) auch FREE_PVAL.
        /// </summary>
        /// <remarks>
        /// Das ist der ganze Unterschied zwischen den beiden Klassen, und er
        /// ist der Grund, warum ein Resourcepart ein Leerzeichen und ein
        /// Schachsymbol tragen darf und ein Localpart nicht.
        /// </remarks>
        [Test]
        public void TheTwoClasses()
        {

            Assert.Multiple(() =>
            {

                Assert.That(Precis.IsIdentifierClass(0x0061), Is.True,  "'a' gehört in beide Klassen.");
                Assert.That(Precis.IsFreeformClass  (0x0061), Is.True);

                Assert.That(Precis.IsIdentifierClass(0x265A), Is.False, "Ein Symbol ist kein Bezeichnerzeichen.");
                Assert.That(Precis.IsFreeformClass  (0x265A), Is.True);

                Assert.That(Precis.IsIdentifierClass(0x0020), Is.False, "Ein Leerzeichen ist kein Bezeichnerzeichen.");
                Assert.That(Precis.IsFreeformClass  (0x0020), Is.True);

                Assert.That(Precis.IsIdentifierClass(0x0640), Is.False, "Der Tatweel ist in keiner Klasse.");
                Assert.That(Precis.IsFreeformClass  (0x0640), Is.False);

                Assert.That(Precis.IsIdentifierClass(0x0378), Is.False, "Nicht Vergebenes ist in keiner Klasse.");
                Assert.That(Precis.IsFreeformClass  (0x0378), Is.False);

            });

        }

        #endregion

        #region TheArabicIndicDigitsRule()

        /// <summary>
        /// RFC 5892, Anhang A.8 und A.9: Die beiden Sätze arabisch-indischer
        /// Ziffern dürfen nicht in derselben Zeichenkette stehen.
        /// </summary>
        /// <remarks>
        /// Sie sehen einander ähnlich und bedeuten dasselbe. Zwei Konten, die
        /// sich nur darin unterscheiden, wären für den Leser dasselbe Konto -
        /// deshalb entweder der eine Satz oder der andere.
        ///
        /// Diese beiden Regeln sind hier umgesetzt, weil sie ohne
        /// Unicode-Eigenschaften auskommen, die .NET nicht kennt: Sie fragen
        /// nur, was sonst noch in der Zeichenkette steht.
        /// </remarks>
        [Test]
        public void TheArabicIndicDigitsRule()
        {

            const String ArabischIndisch = "٠١٢";
            const String Erweitert       = "۰۱۲";

            Assert.Multiple(() =>
            {

                Assert.That(Precis.ContextRuleSatisfied(0x0660, ArabischIndisch), Is.True,
                            "Ein Satz für sich ist zulässig.");

                Assert.That(Precis.ContextRuleSatisfied(0x06F0, Erweitert), Is.True);

                Assert.That(Precis.ContextRuleSatisfied(0x0660, ArabischIndisch + Erweitert), Is.False,
                            "Gemischt nicht.");

                Assert.That(Precis.ContextRuleSatisfied(0x06F0, ArabischIndisch + Erweitert), Is.False);

            });

        }

        #endregion

        #region TheUnimplementedContextRules()

        /// <summary>
        /// Für die übrigen kontextabhängigen Codepoints gibt es hier keine
        /// Regel - und damit keine Zulassung.
        /// </summary>
        /// <remarks>
        /// RFC 5892, Anhang A.1 bis A.7 verlangen Unicode-Eigenschaften, die
        /// .NET nicht ausliefert: Joining_Type für die beiden Joiner, Script
        /// für Keraia, Geresh, Gershayim und den Katakana-Mittelpunkt. Sie
        /// nachzubilden hiesse, die Näherung wieder einzuführen, die dieser
        /// Punkt gerade abgeschafft hat - an einer Stelle, an der sie über
        /// Zulassen oder Ablehnen entscheidet.
        ///
        /// Also lieber ablehnen und es hier hinschreiben. Es trifft
        /// Satzzeichen und unsichtbare Zeichen, nicht Buchstaben: Wer sie in
        /// einem Localpart führt, hat eine Adresse, die anderswo ebenfalls
        /// Ärger macht.
        /// </remarks>
        [Test]
        public void TheUnimplementedContextRules()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Precis.ContextRuleSatisfied(0x200C, "a‌b"),  Is.False, "ZWNJ (A.1)");
                Assert.That(Precis.ContextRuleSatisfied(0x200D, "a‍b"),  Is.False, "ZWJ (A.2)");
                Assert.That(Precis.ContextRuleSatisfied(0x00B7, "l·l"),  Is.False, "MIDDLE DOT (A.3)");
                Assert.That(Precis.ContextRuleSatisfied(0x0375, "͵α"), Is.False, "KERAIA (A.4)");
                Assert.That(Precis.ContextRuleSatisfied(0x05F3, "א׳"), Is.False, "GERESH (A.5)");
                Assert.That(Precis.ContextRuleSatisfied(0x05F4, "א״"), Is.False, "GERSHAYIM (A.6)");
                Assert.That(Precis.ContextRuleSatisfied(0x30FB, "ア・ア"), Is.False, "KATAKANA MIDDLE DOT (A.7)");
            });

        }

        #endregion

    }

}
