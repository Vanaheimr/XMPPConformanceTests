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
    /// Die Unicode-Fassung, aus der die verwendeten Bereiche stammen.
    /// </summary>
    public const String UnicodeVersion = UnicodeSets.UnicodeVersion;

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
        if (UnicodeSets.TryException(CodePoint, out var ausnahme))
            return ausnahme switch {
                       UnicodeSets.ExceptionValue.PValid    => PrecisProperty.PValid,
                       UnicodeSets.ExceptionValue.ContextO  => PrecisProperty.ContextO,
                       _                                    => PrecisProperty.Disallowed
                   };

        // Ebenfalls Ausnahmen, nur als Bereich: die beiden Ziffernreihen wären
        // über ihre Kategorie Nd sonst PVALID.
        if (UnicodeSets.IsContextODigit(CodePoint))
            return PrecisProperty.ContextO;

        // BackwardCompatible (Abschnitt 2.7) ist bis heute leer. Der Zweig
        // steht trotzdem hier, denn er ist kein Versehen des RFCs: Er nimmt
        // auf, was eine neue Unicode-Fassung sonst umdrehen würde.

        if (UnicodeSets.IsUnassigned(CodePoint))
            return PrecisProperty.Unassigned;

        // ASCII7: druckbares ASCII ohne Leerzeichen
        if (CodePoint is >= 0x21 and <= 0x7E)
            return PrecisProperty.PValid;

        // JoinControl (Abschnitt 2.8)
        if (UnicodeSets.IsJoinControl(CodePoint))
            return PrecisProperty.ContextJ;

        if (UnicodeSets.IsOldHangulJamo(CodePoint))
            return PrecisProperty.Disallowed;

        if (UnicodeSets.IsDefaultIgnorable(CodePoint) || UnicodeSets.IsNoncharacter(CodePoint))
            return PrecisProperty.Disallowed;

        var kategorie = UnicodeSets.Category(CodePoint);

        if (kategorie == UnicodeCategory.Control)
            return PrecisProperty.Disallowed;

        // HasCompat: hat eine Kompatibilitätszerlegung. Steht vor
        // LetterDigits, und das ist der Unterschied ums Ganze - sonst wäre die
        // Ligatur fi ein Buchstabe wie jeder andere.
        if (UnicodeSets.HasCompat(CodePoint))
            return PrecisProperty.FreePValid;

        if (UnicodeSets.IsLetterDigits(CodePoint))
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
        if (CodePoint is >= UnicodeSets.ArabicIndicZero and <= UnicodeSets.ArabicIndicNine)
            return !Text.Any(c => c is >= (Char) UnicodeSets.ExtendedArabicIndicZero
                                    and <= (Char) UnicodeSets.ExtendedArabicIndicNine);

        if (CodePoint is >= UnicodeSets.ExtendedArabicIndicZero and <= UnicodeSets.ExtendedArabicIndicNine)
            return !Text.Any(c => c is >= (Char) UnicodeSets.ArabicIndicZero
                                    and <= (Char) UnicodeSets.ArabicIndicNine);

        return false;

    }

    #endregion

}
