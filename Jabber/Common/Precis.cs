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
/// Die abgeleitete Eigenschaft eines Codepoints nach RFC 8264, Abschnitt 8.
/// </summary>
public enum PrecisProperty
{

    /// <summary>In beiden Klassen zulässig.</summary>
    PValid,

    /// <summary>
    /// Nur in der FreeformClass zulässig (im RFC <c>ID_DIS or FREE_PVAL</c>).
    /// </summary>
    FreePValid,

    /// <summary>
    /// Zulässig, wenn die Regel aus RFC 5892, Anhang A.1/A.2 erfüllt ist
    /// (die beiden Joiner).
    /// </summary>
    ContextJ,

    /// <summary>
    /// Zulässig, wenn die Regel aus RFC 5892, Anhang A.3 bis A.9 erfüllt ist.
    /// </summary>
    ContextO,

    /// <summary>In keiner Klasse zulässig.</summary>
    Disallowed,

    /// <summary>
    /// In der zugrunde liegenden Unicode-Fassung nicht vergeben - und deshalb
    /// nirgends zulässig.
    /// </summary>
    Unassigned

}

/// <summary>
/// PRECIS nach RFC 8264: die IdentifierClass und die FreeformClass, bestimmt
/// aus den abgeleiteten Eigenschaften.
/// </summary>
/// <remarks>
/// <b>Die Leiter aus Abschnitt 8 ist eine Reihenfolge, keine Menge.</b> Viele
/// Codepoints gehören in mehrere Kategorien, und welche zuerst greift,
/// entscheidet über die Antwort: U+0640 (ARABIC TATWEEL) ist ein Modifier
/// Letter und damit in LetterDigits — aber die Ausnahmeliste steht davor und
/// verbietet ihn. U+2163 (ROMAN NUMERAL FOUR) ist Nl und damit in
/// OtherLetterDigits — HasCompat steht davor. Wer die Kategorien als Menge
/// prüft, bekommt in genau diesen Fällen die falsche Antwort.
///
/// Vorher stand hier eine Näherung: Kategorie plus die Frage, ob der Codepoint
/// eine Kompatibilitätszerlegung hat. Sie traf die Beispiele aus RFC 7622 und
/// liess die Ausnahmeliste, die Joiner und die alten Hangul-Jamo aussen vor.
///
/// <b>Was .NET nicht kennt, steht hier als Tabelle.</b>
/// <c>Default_Ignorable_Code_Point</c>, <c>Noncharacter_Code_Point</c> und
/// <c>Hangul_Syllable_Type</c> liefert die Laufzeit nicht; sie sind als
/// Bereiche eingetragen und mit ihrer Unicode-Fassung benannt. Das ist keine
/// Näherung, sondern eine Kopie — sie kann veralten, aber sie kann nicht
/// danebenliegen.
/// </remarks>
public static class Precis
{

    #region Data

    /// <summary>
    /// Die Unicode-Fassung, aus der die eingetragenen Bereiche stammen.
    /// </summary>
    public const String UnicodeVersion = "15.1.0";

    /// <summary>
    /// RFC 5892, Abschnitt 2.6: Codepoints, die anders behandelt werden, als
    /// ihre Kategorie es nahelegt.
    /// </summary>
    private static readonly Dictionary<UInt32, PrecisProperty> _exceptions = new()
    {

        // PVALID - wären sonst DISALLOWED
        [0x00DF] = PrecisProperty.PValid,      // LATIN SMALL LETTER SHARP S
        [0x03C2] = PrecisProperty.PValid,      // GREEK SMALL LETTER FINAL SIGMA
        [0x06FD] = PrecisProperty.PValid,      // ARABIC SIGN SINDHI AMPERSAND
        [0x06FE] = PrecisProperty.PValid,      // ARABIC SIGN SINDHI POSTPOSITION MEN
        [0x0F0B] = PrecisProperty.PValid,      // TIBETAN MARK INTERSYLLABIC TSHEG
        [0x3007] = PrecisProperty.PValid,      // IDEOGRAPHIC NUMBER ZERO

        // CONTEXTO - wären sonst DISALLOWED
        [0x00B7] = PrecisProperty.ContextO,    // MIDDLE DOT
        [0x0375] = PrecisProperty.ContextO,    // GREEK LOWER NUMERAL SIGN
        [0x05F3] = PrecisProperty.ContextO,    // HEBREW PUNCTUATION GERESH
        [0x05F4] = PrecisProperty.ContextO,    // HEBREW PUNCTUATION GERSHAYIM
        [0x30FB] = PrecisProperty.ContextO,    // KATAKANA MIDDLE DOT

        // DISALLOWED - wären sonst PVALID
        [0x0640] = PrecisProperty.Disallowed,  // ARABIC TATWEEL
        [0x07FA] = PrecisProperty.Disallowed,  // NKO LAJANYALAN
        [0x302E] = PrecisProperty.Disallowed,  // HANGUL SINGLE DOT TONE MARK
        [0x302F] = PrecisProperty.Disallowed,  // HANGUL DOUBLE DOT TONE MARK
        [0x3031] = PrecisProperty.Disallowed,  // VERTICAL KANA REPEAT MARK
        [0x3032] = PrecisProperty.Disallowed,  // VERTICAL KANA REPEAT WITH VOICED SOUND MARK
        [0x3033] = PrecisProperty.Disallowed,  // VERTICAL KANA REPEAT MARK UPPER HALF
        [0x3034] = PrecisProperty.Disallowed,  // VERTICAL KANA REPEAT WITH VOICED SOUND MARK UPPER HALF
        [0x3035] = PrecisProperty.Disallowed,  // VERTICAL KANA REPEAT MARK LOWER HALF
        [0x303B] = PrecisProperty.Disallowed   // VERTICAL IDEOGRAPHIC ITERATION MARK

    };

    /// <summary>
    /// Die CONTEXTO-Codepoints, die anders als ihre Kategorie behandelt werden
    /// und dabei Ziffern sind (RFC 5892, Anhang A.8 und A.9).
    /// </summary>
    private const UInt32 ArabicIndicZero          = 0x0660;
    private const UInt32 ArabicIndicNine          = 0x0669;
    private const UInt32 ExtendedArabicIndicZero  = 0x06F0;
    private const UInt32 ExtendedArabicIndicNine  = 0x06F9;

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

    #endregion

    #region DerivedProperty(CodePoint)

    /// <summary>
    /// Die abgeleitete Eigenschaft nach RFC 8264, Abschnitt 8.
    /// </summary>
    /// <remarks>
    /// Die Zweige stehen in der Reihenfolge des Abschnitts, und daran darf
    /// nicht gerührt werden - siehe die Beispiele in der Klassenbeschreibung.
    /// </remarks>
    public static PrecisProperty DerivedProperty(UInt32 CodePoint)
    {

        // Exceptions (RFC 5892, Abschnitt 2.6)
        if (_exceptions.TryGetValue(CodePoint, out var ausnahme))
            return ausnahme;

        // Ebenfalls Ausnahmen, nur als Bereich: die beiden Ziffernreihen wären
        // über ihre Kategorie Nd sonst PVALID.
        if (CodePoint is >= ArabicIndicZero         and <= ArabicIndicNine or
                         >= ExtendedArabicIndicZero and <= ExtendedArabicIndicNine)
            return PrecisProperty.ContextO;

        // BackwardCompatible (Abschnitt 2.7) ist bis heute leer. Der Zweig
        // steht trotzdem hier, denn er ist kein Versehen des RFCs: Er nimmt
        // auf, was eine neue Unicode-Fassung sonst umdrehen würde.

        if (IsUnassigned(CodePoint))
            return PrecisProperty.Unassigned;

        // ASCII7: druckbares ASCII ohne Leerzeichen
        if (CodePoint is >= 0x21 and <= 0x7E)
            return PrecisProperty.PValid;

        // JoinControl (Abschnitt 2.8)
        if (CodePoint is 0x200C or 0x200D)
            return PrecisProperty.ContextJ;

        if (InRanges(CodePoint, _oldHangulJamo))
            return PrecisProperty.Disallowed;

        if (IsDefaultIgnorable(CodePoint) || IsNoncharacter(CodePoint))
            return PrecisProperty.Disallowed;

        var kategorie = Category(CodePoint);

        if (kategorie == UnicodeCategory.Control)
            return PrecisProperty.Disallowed;

        // HasCompat: hat eine Kompatibilitätszerlegung. Steht vor
        // LetterDigits, und das ist der Unterschied ums Ganze - sonst wäre die
        // Ligatur fi ein Buchstabe wie jeder andere.
        if (HasCompat(CodePoint))
            return PrecisProperty.FreePValid;

        if (kategorie is UnicodeCategory.LowercaseLetter      or
                         UnicodeCategory.UppercaseLetter      or
                         UnicodeCategory.OtherLetter          or
                         UnicodeCategory.DecimalDigitNumber   or
                         UnicodeCategory.ModifierLetter       or
                         UnicodeCategory.NonSpacingMark       or
                         UnicodeCategory.SpacingCombiningMark)
            return PrecisProperty.PValid;

        if (kategorie is UnicodeCategory.TitlecaseLetter      or
                         UnicodeCategory.LetterNumber         or
                         UnicodeCategory.OtherNumber          or
                         UnicodeCategory.EnclosingMark        or
                         UnicodeCategory.SpaceSeparator       or
                         UnicodeCategory.MathSymbol           or
                         UnicodeCategory.CurrencySymbol       or
                         UnicodeCategory.ModifierSymbol       or
                         UnicodeCategory.OtherSymbol          or
                         UnicodeCategory.ConnectorPunctuation or
                         UnicodeCategory.DashPunctuation      or
                         UnicodeCategory.OpenPunctuation      or
                         UnicodeCategory.ClosePunctuation     or
                         UnicodeCategory.InitialQuotePunctuation or
                         UnicodeCategory.FinalQuotePunctuation   or
                         UnicodeCategory.OtherPunctuation)
            return PrecisProperty.FreePValid;

        return PrecisProperty.Disallowed;

    }

    #endregion

    #region IsIdentifierClass(CodePoint) / IsFreeformClass(CodePoint)

    /// <summary>
    /// Gehört der Codepoint zur IdentifierClass (RFC 8264, Abschnitt 4.2)?
    /// </summary>
    /// <remarks>
    /// Kontextabhängige Codepoints zählen hier nicht mit - ob sie zulässig
    /// sind, hängt an der ganzen Zeichenkette und beantwortet
    /// <see cref="ContextRuleSatisfied"/>.
    /// </remarks>
    public static Boolean IsIdentifierClass(UInt32 CodePoint)

        => DerivedProperty(CodePoint) == PrecisProperty.PValid;

    /// <summary>
    /// Gehört der Codepoint zur FreeformClass (RFC 8264, Abschnitt 4.3)?
    /// </summary>
    public static Boolean IsFreeformClass(UInt32 CodePoint)

        => DerivedProperty(CodePoint) is PrecisProperty.PValid or
                                         PrecisProperty.FreePValid;

    #endregion

    #region ContextRuleSatisfied(CodePoint, Text)

    /// <summary>
    /// Ist die kontextabhängige Regel für diesen Codepoint in dieser
    /// Zeichenkette erfüllt (RFC 5892, Anhang A)?
    /// </summary>
    /// <remarks>
    /// <b>Umgesetzt sind A.8 und A.9</b>, die beiden Regeln für die
    /// arabisch-indischen Ziffern: Die beiden Sätze dürfen nicht gemischt
    /// werden. Sie sehen einander ähnlich und bedeuten dasselbe; zwei Konten,
    /// die sich nur darin unterschieden, wären für den Leser eines. Diese
    /// beiden Regeln kommen ohne Unicode-Eigenschaften aus - sie fragen nur,
    /// was sonst noch dasteht.
    ///
    /// <b>Alles andere wird abgelehnt</b>, und zwar nicht aus Bequemlichkeit.
    /// A.1 und A.2 brauchen <c>Joining_Type</c> und die Virama-Eigenschaft,
    /// A.3 bis A.7 brauchen <c>Script</c> - beides liefert .NET nicht. Sie aus
    /// Blockgrenzen zu erraten hiesse, genau die Näherung wieder einzuführen,
    /// die diese Klasse gerade abgeschafft hat, und zwar an der Stelle, an der
    /// sie über Zulassen oder Ablehnen entscheidet.
    ///
    /// Es trifft fünf Satzzeichen und zwei unsichtbare Zeichen, keine
    /// Buchstaben.
    /// </remarks>
    public static Boolean ContextRuleSatisfied(UInt32 CodePoint, String Text)
    {

        // A.8: eine arabisch-indische Ziffer verträgt sich nicht mit der
        // erweiterten Reihe - und A.9 sagt dasselbe andersherum.
        if (CodePoint is >= ArabicIndicZero and <= ArabicIndicNine)
            return !Text.Any(c => c is >= (Char) ExtendedArabicIndicZero
                                    and <= (Char) ExtendedArabicIndicNine);

        if (CodePoint is >= ExtendedArabicIndicZero and <= ExtendedArabicIndicNine)
            return !Text.Any(c => c is >= (Char) ArabicIndicZero
                                    and <= (Char) ArabicIndicNine);

        return false;

    }

    #endregion

    #region (private) Kategorien

    private static UnicodeCategory Category(UInt32 CodePoint)

        => CharUnicodeInfo.GetUnicodeCategory(Char.ConvertFromUtf32((Int32) CodePoint), 0);

    /// <summary>
    /// RFC 5892, Abschnitt 2.10: nicht vergeben - und keines der Nichtzeichen,
    /// die dieselbe Kategorie tragen.
    /// </summary>
    private static Boolean IsUnassigned(UInt32 CodePoint)

        => Category(CodePoint) == UnicodeCategory.OtherNotAssigned &&
           !IsNoncharacter(CodePoint);

    /// <summary>
    /// <c>Noncharacter_Code_Point</c>: die 32 aus dem arabischen Block und die
    /// beiden am Ende jeder Ebene.
    /// </summary>
    private static Boolean IsNoncharacter(UInt32 CodePoint)

        => CodePoint is >= 0xFDD0 and <= 0xFDEF ||
           (CodePoint & 0xFFFE) == 0xFFFE;

    private static Boolean IsDefaultIgnorable(UInt32 CodePoint)

        => InRanges(CodePoint, _defaultIgnorable);

    /// <summary>
    /// RFC 8264, Abschnitt 9.17: <c>toNFKC(cp) != cp</c>.
    /// </summary>
    private static Boolean HasCompat(UInt32 CodePoint)
    {

        var zeichen = Char.ConvertFromUtf32((Int32) CodePoint);

        return zeichen.Normalize(NormalizationForm.FormKC) != zeichen;

    }

    private static Boolean InRanges(UInt32 CodePoint, (UInt32 Von, UInt32 Bis)[] Bereiche)
    {

        foreach (var (von, bis) in Bereiche)
            if (CodePoint >= von && CodePoint <= bis)
                return true;

        return false;

    }

    #endregion

}
