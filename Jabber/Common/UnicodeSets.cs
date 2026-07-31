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

using System.Globalization;
using System.Text;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Die Mengen aus RFC 5892, Abschnitt 2 - der gemeinsame Unterbau von
/// <see cref="Precis"/> (RFC 8264) und <see cref="Idna"/> (RFC 5892).
/// </summary>
/// <remarks>
/// <b>Beide Vorschriften bauen ihre Leiter aus denselben Bausteinen und kommen
/// zu verschiedenen Ergebnissen.</b> Die Bausteine gehören deshalb an eine
/// Stelle, die Leitern nicht: Ein Unterstrich ist in einem Localpart zulässig
/// (ASCII7) und in einem Domain-Label nicht (LDH); ein Symbol ist in einem
/// Resourcepart zulässig (FreeformClass) und in einem Label nicht. Wer die
/// beiden Leitern zusammenlegte, müsste diese Unterschiede in Sonderfälle
/// übersetzen - und Sonderfälle sind das, was man später nicht mehr nachlesen
/// kann.
///
/// <b>Was .NET nicht ausliefert, steht hier als Bereichstabelle.</b> Sie ist
/// mit der Unicode-Fassung benannt, aus der sie stammt: eine Kopie, die
/// veralten kann, aber nicht danebenliegen.
/// </remarks>
internal static class UnicodeSets
{

    #region Data

    /// <summary>
    /// Die Unicode-Fassung, aus der die eingetragenen Bereiche stammen.
    /// </summary>
    internal const String UnicodeVersion = "15.1.0";

    /// <summary>
    /// Die drei Werte, die RFC 5892, Abschnitt 2.6 einer Ausnahme geben kann.
    /// </summary>
    internal enum ExceptionValue
    {
        PValid,
        ContextO,
        Disallowed
    }

    /// <summary>
    /// RFC 5892, Abschnitt 2.6: Codepoints, die anders behandelt werden, als
    /// ihre Kategorie es nahelegt.
    /// </summary>
    private static readonly Dictionary<UInt32, ExceptionValue> _exceptions = new()
    {

        // PVALID - wären sonst DISALLOWED
        [0x00DF] = ExceptionValue.PValid,      // LATIN SMALL LETTER SHARP S
        [0x03C2] = ExceptionValue.PValid,      // GREEK SMALL LETTER FINAL SIGMA
        [0x06FD] = ExceptionValue.PValid,      // ARABIC SIGN SINDHI AMPERSAND
        [0x06FE] = ExceptionValue.PValid,      // ARABIC SIGN SINDHI POSTPOSITION MEN
        [0x0F0B] = ExceptionValue.PValid,      // TIBETAN MARK INTERSYLLABIC TSHEG
        [0x3007] = ExceptionValue.PValid,      // IDEOGRAPHIC NUMBER ZERO

        // CONTEXTO - wären sonst DISALLOWED
        [0x00B7] = ExceptionValue.ContextO,    // MIDDLE DOT
        [0x0375] = ExceptionValue.ContextO,    // GREEK LOWER NUMERAL SIGN
        [0x05F3] = ExceptionValue.ContextO,    // HEBREW PUNCTUATION GERESH
        [0x05F4] = ExceptionValue.ContextO,    // HEBREW PUNCTUATION GERSHAYIM
        [0x30FB] = ExceptionValue.ContextO,    // KATAKANA MIDDLE DOT

        // DISALLOWED - wären sonst PVALID
        [0x0640] = ExceptionValue.Disallowed,  // ARABIC TATWEEL
        [0x07FA] = ExceptionValue.Disallowed,  // NKO LAJANYALAN
        [0x302E] = ExceptionValue.Disallowed,  // HANGUL SINGLE DOT TONE MARK
        [0x302F] = ExceptionValue.Disallowed,  // HANGUL DOUBLE DOT TONE MARK
        [0x3031] = ExceptionValue.Disallowed,  // VERTICAL KANA REPEAT MARK
        [0x3032] = ExceptionValue.Disallowed,  // VERTICAL KANA REPEAT WITH VOICED SOUND MARK
        [0x3033] = ExceptionValue.Disallowed,  // VERTICAL KANA REPEAT MARK UPPER HALF
        [0x3034] = ExceptionValue.Disallowed,  // VERTICAL KANA REPEAT WITH VOICED SOUND MARK UPPER HALF
        [0x3035] = ExceptionValue.Disallowed,  // VERTICAL KANA REPEAT MARK LOWER HALF
        [0x303B] = ExceptionValue.Disallowed   // VERTICAL IDEOGRAPHIC ITERATION MARK

    };

    /// <summary>Die erste und die letzte arabisch-indische Ziffer.</summary>
    internal const UInt32 ArabicIndicZero          = 0x0660;
    internal const UInt32 ArabicIndicNine          = 0x0669;

    /// <summary>Dasselbe für die erweiterte Reihe.</summary>
    internal const UInt32 ExtendedArabicIndicZero  = 0x06F0;
    internal const UInt32 ExtendedArabicIndicNine  = 0x06F9;

    /// <summary>
    /// <c>Default_Ignorable_Code_Point</c> (Unicode 15.1, DerivedCoreProperties).
    /// </summary>
    private static readonly (UInt32 Von, UInt32 Bis)[] _defaultIgnorable =
    [
        (0x00AD,  0x00AD),  (0x034F,  0x034F),  (0x061C,  0x061C),
        (0x115F,  0x1160),  (0x17B4,  0x17B5),  (0x180B,  0x180F),
        (0x200B,  0x200F),  (0x202A,  0x202E),  (0x2060,  0x206F),
        (0x3164,  0x3164),  (0xFE00,  0xFE0F),  (0xFEFF,  0xFEFF),
        (0xFFA0,  0xFFA0),  (0xFFF0,  0xFFF8),  (0x1BCA0, 0x1BCA3),
        (0x1D173, 0x1D17A), (0xE0000, 0xE0FFF)
    ];

    /// <summary>
    /// <c>Hangul_Syllable_Type</c> in {L, V, T} - die alten Jamo, aus denen
    /// sich Silben zusammensetzen liessen (Unicode 15.1).
    /// </summary>
    private static readonly (UInt32 Von, UInt32 Bis)[] _oldHangulJamo =
    [
        (0x1100, 0x115F),  // L
        (0x1160, 0x11A7),  // V
        (0x11A8, 0x11FF),  // T
        (0xA960, 0xA97C),  // L (Jamo Extended-A)
        (0xD7B0, 0xD7C6),  // V (Jamo Extended-B)
        (0xD7CB, 0xD7FB)   // T (Jamo Extended-B)
    ];

    /// <summary>
    /// RFC 5892, Abschnitt 2.4: drei Blöcke, deren Zeichen in einem
    /// Domainnamen nichts zu suchen haben.
    /// </summary>
    private static readonly (UInt32 Von, UInt32 Bis)[] _ignorableBlocks =
    [
        (0x20D0,  0x20FF),   // Combining Diacritical Marks for Symbols
        (0x1D100, 0x1D1FF),  // Musical Symbols
        (0x1D200, 0x1D24F)   // Ancient Greek Musical Notation
    ];

    #endregion


    #region Mengen aus RFC 5892, Abschnitt 2

    /// <summary>Abschnitt 2.6: die Ausnahmeliste.</summary>
    internal static Boolean TryException(UInt32 CodePoint, out ExceptionValue Value)

        => _exceptions.TryGetValue(CodePoint, out Value);

    /// <summary>
    /// Abschnitt 2.6, als Bereich: die beiden Ziffernreihen sind CONTEXTO,
    /// obwohl ihre Kategorie (Nd) sie zu PVALID machen würde.
    /// </summary>
    internal static Boolean IsContextODigit(UInt32 CodePoint)

        => CodePoint is >= ArabicIndicZero         and <= ArabicIndicNine or
                        >= ExtendedArabicIndicZero and <= ExtendedArabicIndicNine;

    /// <summary>Abschnitt 2.1: <c>{Ll, Lu, Lo, Nd, Lm, Mn, Mc}</c>.</summary>
    internal static Boolean IsLetterDigits(UInt32 CodePoint)

        => Category(CodePoint) is UnicodeCategory.LowercaseLetter      or
                                  UnicodeCategory.UppercaseLetter      or
                                  UnicodeCategory.OtherLetter          or
                                  UnicodeCategory.DecimalDigitNumber   or
                                  UnicodeCategory.ModifierLetter       or
                                  UnicodeCategory.NonSpacingMark       or
                                  UnicodeCategory.SpacingCombiningMark;

    /// <summary>
    /// Abschnitt 2.2: <c>toNFKC(toCaseFold(toNFKC(cp))) != cp</c>.
    /// </summary>
    /// <remarks>
    /// <b>Hier steht <c>ToLowerInvariant</c> statt <c>toCaseFold</c></b>, denn
    /// .NET kennt kein Case Folding - und die beiden gehen auseinander. Der
    /// Fall, der es zeigt, ist U+0130 (I mit Punkt): Case Folding macht daraus
    /// <c>i</c> + Punkt, <c>ToLowerInvariant</c> lässt ihn <b>unverändert</b>,
    /// weil .NET die türkische I-Frage nicht in der invarianten Kultur
    /// entscheiden will. Über die Rechnung allein käme er als stabil und damit
    /// als zulässiges Label-Zeichen heraus, obwohl die IANA-Tabelle ihn
    /// verbietet.
    ///
    /// Deshalb die zweite Bedingung: <b>Ein Gross- oder Titelbuchstabe ist nie
    /// faltungsstabil.</b> Genau das ist die Aussage von Case Folding - es
    /// bildet auf die kleine Form ab. Ein Domain-Label ist kleingeschrieben,
    /// und ein Codepoint der Kategorie Lu oder Lt gehört in keines.
    /// </remarks>
    internal static Boolean IsUnstable(UInt32 CodePoint)
    {

        if (Category(CodePoint) is UnicodeCategory.UppercaseLetter or
                                   UnicodeCategory.TitlecaseLetter)
            return true;

        var zeichen = Char.ConvertFromUtf32((Int32) CodePoint);

        var gefaltet = zeichen.Normalize(NormalizationForm.FormKC).
                               ToLowerInvariant().
                               Normalize(NormalizationForm.FormKC);

        return gefaltet != zeichen;

    }

    /// <summary>
    /// Abschnitt 2.3: <c>Default_Ignorable_Code_Point</c> oder
    /// <c>White_Space</c> oder <c>Noncharacter_Code_Point</c>.
    /// </summary>
    internal static Boolean IsIgnorableProperties(UInt32 CodePoint)

        => IsDefaultIgnorable(CodePoint) ||
           IsNoncharacter(CodePoint)     ||
           Rune.IsWhiteSpace(new Rune(CodePoint));

    /// <summary>Abschnitt 2.4: die drei Blöcke.</summary>
    internal static Boolean IsIgnorableBlocks(UInt32 CodePoint)

        => InRanges(CodePoint, _ignorableBlocks);

    /// <summary>
    /// Abschnitt 2.5: <c>{002D, 0030..0039, 0061..007A}</c> - Bindestrich,
    /// Ziffern, Kleinbuchstaben.
    /// </summary>
    /// <remarks>
    /// Grossbuchstaben fehlen hier nicht versehentlich: Sie sind nach
    /// Abschnitt 2.2 unstabil, und ein Domain-Label ist kleingeschrieben.
    /// </remarks>
    internal static Boolean IsLdh(UInt32 CodePoint)

        => CodePoint is 0x2D or (>= 0x30 and <= 0x39) or (>= 0x61 and <= 0x7A);

    /// <summary>Abschnitt 2.8: die beiden Joiner.</summary>
    internal static Boolean IsJoinControl(UInt32 CodePoint)

        => CodePoint is 0x200C or 0x200D;

    /// <summary>Abschnitt 2.9: die alten Hangul-Jamo.</summary>
    internal static Boolean IsOldHangulJamo(UInt32 CodePoint)

        => InRanges(CodePoint, _oldHangulJamo);

    /// <summary>
    /// Abschnitt 2.10: nicht vergeben - und keines der Nichtzeichen, die
    /// dieselbe Kategorie tragen.
    /// </summary>
    internal static Boolean IsUnassigned(UInt32 CodePoint)

        => Category(CodePoint) == UnicodeCategory.OtherNotAssigned &&
           !IsNoncharacter(CodePoint);

    #endregion

    #region Weitere Mengen (RFC 8264, Abschnitt 9)

    /// <summary>
    /// <c>Noncharacter_Code_Point</c>: die 32 aus dem arabischen Block und die
    /// beiden am Ende jeder Ebene.
    /// </summary>
    internal static Boolean IsNoncharacter(UInt32 CodePoint)

        => CodePoint is >= 0xFDD0 and <= 0xFDEF ||
           (CodePoint & 0xFFFE) == 0xFFFE;

    internal static Boolean IsDefaultIgnorable(UInt32 CodePoint)

        => InRanges(CodePoint, _defaultIgnorable);

    /// <summary>RFC 8264, Abschnitt 9.17: <c>toNFKC(cp) != cp</c>.</summary>
    internal static Boolean HasCompat(UInt32 CodePoint)
    {

        var zeichen = Char.ConvertFromUtf32((Int32) CodePoint);

        return zeichen.Normalize(NormalizationForm.FormKC) != zeichen;

    }

    internal static UnicodeCategory Category(UInt32 CodePoint)

        => CharUnicodeInfo.GetUnicodeCategory(Char.ConvertFromUtf32((Int32) CodePoint), 0);

    #endregion

    #region (private) InRanges(CodePoint, Bereiche)

    private static Boolean InRanges(UInt32 CodePoint, (UInt32 Von, UInt32 Bis)[] Bereiche)
    {

        foreach (var (von, bis) in Bereiche)
            if (CodePoint >= von && CodePoint <= bis)
                return true;

        return false;

    }

    #endregion

}
