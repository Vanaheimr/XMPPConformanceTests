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

        #region Hilfsfunktionen

        /// <summary>
        /// Ist die Regel für die erste Fundstelle dieses Codepoints erfüllt?
        /// </summary>
        private static Boolean Regel(String Text, UInt32 CodePoint)
        {

            var punkte = Text.EnumerateRunes().Select(r => (UInt32) r.Value).ToArray();
            var stelle = Array.IndexOf(punkte, CodePoint);

            Assert.That(stelle, Is.GreaterThanOrEqualTo(0),
                        $"U+{CodePoint:X4} kommt in '{Text}' gar nicht vor.");

            return Precis.ContextRuleSatisfied(punkte, stelle);

        }

        #endregion


        #region TheJoinersNeedAReasonToBeThere()

        /// <summary>
        /// RFC 5892, Anhang A.1 und A.2: Die beiden Joiner sind zulässig, wo
        /// sie etwas bewirken - und nur dort.
        /// </summary>
        /// <remarks>
        /// Beide sind unsichtbar. In einer Adresse ist ein unsichtbares Zeichen
        /// zuerst einmal ein Weg, zwei verschiedene Adressen gleich aussehen zu
        /// lassen. Die Regeln benennen die Stellen, an denen sie trotzdem
        /// gebraucht werden:
        ///
        /// <list type="bullet">
        ///   <item>Nach einem Virama (A.1 und A.2): Das Virama tilgt den
        ///         eingebauten Vokal, der Joiner entscheidet über die
        ///         Ligatur.</item>
        ///   <item>Zwischen zwei verbindenden Buchstaben (nur A.1): Dort
        ///         verhindert der Non-Joiner eine Verbindung, die es sonst
        ///         gäbe.</item>
        /// </list>
        /// </remarks>
        [Test]
        public void TheJoinersNeedAReasonToBeThere()
        {

            const String Zwnj    = "‌";
            const String Zwj     = "‍";
            const String Virama  = "्";  // DEVANAGARI SIGN VIRAMA
            const String Ka      = "क";  // DEVANAGARI LETTER KA

            // Arabisch: BEH und YEH verbinden nach beiden Seiten (Joining_Type D).
            const String Beh     = "ب";
            const String Yeh     = "ي";
            const String Shadda  = "ّ";  // ARABIC SHADDA, Joining_Type T

            Assert.Multiple(() =>
            {

                Assert.That(Regel(Ka + Virama + Zwnj + Ka, 0x200C), Is.True,
                            "A.1, erster Weg: nach einem Virama.");

                Assert.That(Regel(Ka + Virama + Zwj + Ka, 0x200D), Is.True,
                            "A.2: nach einem Virama.");

                Assert.That(Regel(Beh + Zwnj + Yeh, 0x200C), Is.True,
                            "A.1, zweiter Weg: zwischen zwei verbindenden Buchstaben.");

                Assert.That(Regel("a" + Zwnj + "b", 0x200C), Is.False,
                            "Zwischen zwei lateinischen Buchstaben verbindet sich nichts.");

                Assert.That(Regel("a" + Zwj + "b", 0x200D), Is.False,
                            "Für den Joiner gibt es den zweiten Weg gar nicht.");

                Assert.That(Regel(Beh + Zwj + Yeh, 0x200D), Is.False,
                            "Auch nicht zwischen verbindenden Buchstaben.");

                // Die drei Fälle, an denen sich zeigt, dass beide Seiten und
                // die durchsichtigen Zeichen dazwischen wirklich geprüft
                // werden. Ohne sie genügte es, eine der beiden Seiten
                // anzusehen: Die Fälle darüber scheitern jeweils schon an der
                // anderen.
                Assert.That(Regel("a" + Zwnj + Yeh, 0x200C), Is.False,
                            "Links steht kein verbindender Buchstabe.");

                Assert.That(Regel(Beh + Zwnj + "b", 0x200C), Is.False,
                            "Rechts steht keiner.");

                Assert.That(Regel(Beh + Shadda + Zwnj + Yeh, 0x200C), Is.True,
                            "Ein durchsichtiges Zeichen dazwischen zählt nicht mit.");

            });

        }

        #endregion

        #region TheMiddleDotBelongsBetweenTwoLs()

        /// <summary>
        /// RFC 5892, Anhang A.3: Der Mittelpunkt steht zwischen zwei <c>l</c> -
        /// dem katalanischen <c>l·l</c> - und sonst nirgends.
        /// </summary>
        [Test]
        public void TheMiddleDotBelongsBetweenTwoLs()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Regel("col·la",  0x00B7), Is.True);
                Assert.That(Regel("co·lla",  0x00B7), Is.False, "davor kein 'l'");
                Assert.That(Regel("coll·a",  0x00B7), Is.False, "danach kein 'l'");
                Assert.That(Regel("·la",     0x00B7), Is.False, "am Anfang");
            });

        }

        #endregion

        #region TheGreekAndHebrewMarks()

        /// <summary>
        /// RFC 5892, Anhang A.4 bis A.6: Die Keraia steht vor griechischer
        /// Schrift, Geresh und Gershayim stehen nach hebräischer.
        /// </summary>
        /// <remarks>
        /// Die drei Zeichen gehören zu ihrer Schrift wie ein Buchstabe.
        /// Ausserhalb sind sie Satzzeichen in einer Adresse - und Satzzeichen
        /// sind das Werkzeug, mit dem sich eine Adresse einer anderen ähnlich
        /// machen lässt.
        /// </remarks>
        [Test]
        public void TheGreekAndHebrewMarks()
        {

            const String Keraia     = "͵";
            const String Geresh     = "׳";
            const String Gershayim  = "״";

            Assert.Multiple(() =>
            {

                Assert.That(Regel(Keraia + "α", 0x0375), Is.True,  "A.4: vor Griechisch");
                Assert.That(Regel(Keraia + "a", 0x0375), Is.False, "A.4: vor Latein");
                Assert.That(Regel("α" + Keraia, 0x0375), Is.False, "A.4: am Ende");

                Assert.That(Regel("א" + Geresh,    0x05F3), Is.True,  "A.5: nach Hebräisch");
                Assert.That(Regel("a" + Geresh,    0x05F3), Is.False, "A.5: nach Latein");

                Assert.That(Regel("א" + Gershayim, 0x05F4), Is.True,  "A.6: nach Hebräisch");
                Assert.That(Regel(Gershayim + "א", 0x05F4), Is.False, "A.6: am Anfang");

            });

        }

        #endregion

        #region TheKatakanaMiddleDotNeedsJapanese()

        /// <summary>
        /// RFC 5892, Anhang A.7: Der Katakana-Mittelpunkt ist zulässig, wenn
        /// irgendwo in der Zeichenkette japanische Schrift steht.
        /// </summary>
        /// <remarks>
        /// Diese Regel sieht als einzige der sieben nicht auf die Nachbarn,
        /// sondern auf das Ganze. Der Mittelpunkt trennt in japanischem Text
        /// die Teile eines Fremdworts; ohne japanische Zeichen trennt er nichts.
        /// </remarks>
        [Test]
        public void TheKatakanaMiddleDotNeedsJapanese()
        {

            const String Punkt = "・";

            Assert.Multiple(() =>
            {
                Assert.That(Regel("ア" + Punkt + "ア", 0x30FB), Is.True,  "Katakana");
                Assert.That(Regel("あ" + Punkt + "あ", 0x30FB), Is.True,  "Hiragana");
                Assert.That(Regel("中" + Punkt + "中", 0x30FB), Is.True,  "Han");
                Assert.That(Regel("a"  + Punkt + "b",  0x30FB), Is.False, "kein japanisches Zeichen");
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
        /// </remarks>
        [Test]
        public void TheArabicIndicDigitsRule()
        {

            const String ArabischIndisch = "٠١٢";
            const String Erweitert       = "۰۱۲";

            Assert.Multiple(() =>
            {

                Assert.That(Regel(ArabischIndisch, 0x0660), Is.True,
                            "Ein Satz für sich ist zulässig.");

                Assert.That(Regel(Erweitert, 0x06F0), Is.True);

                Assert.That(Regel(ArabischIndisch + Erweitert, 0x0660), Is.False,
                            "Gemischt nicht.");

                Assert.That(Regel(ArabischIndisch + Erweitert, 0x06F0), Is.False);

            });

        }

        #endregion

        #region WhatIsNotContextual()

        /// <summary>
        /// Was gar nicht kontextabhängig ist, bekommt hier keine Zulassung.
        /// </summary>
        /// <remarks>
        /// Diese Funktion beantwortet nur die Frage „darf dieser Sonderfall hier
        /// stehen". Ein gewöhnlicher Buchstabe ist keiner - für ihn entscheidet
        /// die Leiter, und ein <c>true</c> an dieser Stelle wäre eine zweite,
        /// stillere Zulassung neben ihr.
        /// </remarks>
        [Test]
        public void WhatIsNotContextual()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Regel("abc", 0x0061), Is.False, "'a'");
                Assert.That(Regel("♚",   0x265A), Is.False, "ein Symbol");
            });

        }

        #endregion

    }

}
