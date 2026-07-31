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

    #region ContextRuleSatisfied(CodePoints, Index)

    /// <summary>
    /// Ist die kontextabhängige Regel für diesen Codepoint an dieser Stelle
    /// erfüllt (RFC 5892, Anhang A)?
    /// </summary>
    /// <remarks>
    /// <b>Kontextabhängig heisst: Der Codepoint allein sagt es nicht.</b>
    /// Deshalb geht hier die ganze Zeichenkette und die Stelle darin hinein und
    /// nicht nur das Zeichen - drei der neun Regeln fragen nach dem Zeichen
    /// davor oder danach, eine nach allen zusammen.
    ///
    /// Die Eigenschaften, die dafür nötig sind - <c>Canonical_Combining_Class</c>,
    /// <c>Joining_Type</c> und <c>Script</c> - liefert .NET nicht; sie stehen
    /// in <see cref="ContextTables"/>, erzeugt aus der Unicode-Datenbank.
    ///
    /// Was nicht kontextabhängig ist, bekommt hier <c>false</c>: Diese Funktion
    /// beantwortet nur die Frage „darf dieser Sonderfall stehen", nicht die
    /// allgemeine nach der Zulässigkeit.
    /// </remarks>
    public static Boolean ContextRuleSatisfied(IReadOnlyList<UInt32> CodePoints, Int32 Index)
    {

        var codePoint = CodePoints[Index];

        return codePoint switch {

            // A.1: ZERO WIDTH NON-JOINER
            0x200C  => AfterVirama(CodePoints, Index) ||
                       BetweenJoiners(CodePoints, Index),

            // A.2: ZERO WIDTH JOINER
            0x200D  => AfterVirama(CodePoints, Index),

            // A.3: MIDDLE DOT - nur zwischen zwei 'l' (katalanisch l·l)
            0x00B7  => Before(CodePoints, Index) == 0x006C &&
                       After (CodePoints, Index) == 0x006C,

            // A.4: GREEK LOWER NUMERAL SIGN - vor einem griechischen Zeichen
            0x0375  => After(CodePoints, Index) is UInt32 danach &&
                       ContextTables.Contains(ContextTables.ScriptGreek, danach),

            // A.5 und A.6: GERESH und GERSHAYIM - nach einem hebräischen Zeichen
            0x05F3 or
            0x05F4  => Before(CodePoints, Index) is UInt32 davor &&
                       ContextTables.Contains(ContextTables.ScriptHebrew, davor),

            // A.7: KATAKANA MIDDLE DOT - nur in japanischem Text
            0x30FB  => CodePoints.Any(cp => ContextTables.Contains(ContextTables.ScriptHiragana, cp) ||
                                            ContextTables.Contains(ContextTables.ScriptKatakana, cp) ||
                                            ContextTables.Contains(ContextTables.ScriptHan,      cp)),

            // A.8: eine arabisch-indische Ziffer verträgt sich nicht mit der
            // erweiterten Reihe - und A.9 sagt dasselbe andersherum.
            >= UnicodeSets.ArabicIndicZero and
            <= UnicodeSets.ArabicIndicNine
                    => !CodePoints.Any(cp => cp is >= UnicodeSets.ExtendedArabicIndicZero
                                               and <= UnicodeSets.ExtendedArabicIndicNine),

            >= UnicodeSets.ExtendedArabicIndicZero and
            <= UnicodeSets.ExtendedArabicIndicNine
                    => !CodePoints.Any(cp => cp is >= UnicodeSets.ArabicIndicZero
                                               and <= UnicodeSets.ArabicIndicNine),

            _       => false

        };

    }

    #endregion

    #region (private) Nachbarn und Verbindungsarten

    private static UInt32? Before(IReadOnlyList<UInt32> CodePoints, Int32 Index)

        => Index > 0 ? CodePoints[Index - 1] : null;

    private static UInt32? After(IReadOnlyList<UInt32> CodePoints, Int32 Index)

        => Index + 1 < CodePoints.Count ? CodePoints[Index + 1] : null;

    /// <summary>
    /// Steht unmittelbar davor ein Virama (RFC 5892, Anhang A.1 und A.2)?
    /// </summary>
    /// <remarks>
    /// Ein Virama tilgt den eingebauten Vokal des Zeichens davor; ein Joiner
    /// dahinter entscheidet, ob die beiden Zeichen zu einer Ligatur
    /// zusammenwachsen. In dieser Stellung trägt er Bedeutung und ist deshalb
    /// zugelassen - überall sonst wäre er ein unsichtbares Zeichen in einer
    /// Adresse.
    /// </remarks>
    private static Boolean AfterVirama(IReadOnlyList<UInt32> CodePoints, Int32 Index)

        => Before(CodePoints, Index) is UInt32 davor &&
           ContextTables.Contains(ContextTables.Virama, davor);

    /// <summary>
    /// Der zweite Weg aus A.1: <c>(L|D) T* ZWNJ T* (R|D)</c>.
    /// </summary>
    /// <remarks>
    /// Der Ausdruck aus dem RFC in Worten: Links vom Joiner steht - über
    /// beliebig viele durchsichtige Zeichen hinweg - ein Buchstabe, der nach
    /// rechts verbindet, und rechts einer, der nach links verbindet. Genau dort
    /// verhindert der Joiner eine Verbindung, die es sonst gäbe. Steht er
    /// woanders, verhindert er nichts und ist bloss unsichtbar.
    /// </remarks>
    private static Boolean BetweenJoiners(IReadOnlyList<UInt32> CodePoints, Int32 Index)
    {

        var links = Index - 1;

        while (links >= 0 && ContextTables.Contains(ContextTables.JoiningT, CodePoints[links]))
            links--;

        if (links < 0 ||
            !(ContextTables.Contains(ContextTables.JoiningL, CodePoints[links]) ||
              ContextTables.Contains(ContextTables.JoiningD, CodePoints[links])))
            return false;

        var rechts = Index + 1;

        while (rechts < CodePoints.Count && ContextTables.Contains(ContextTables.JoiningT, CodePoints[rechts]))
            rechts++;

        return rechts < CodePoints.Count &&
               (ContextTables.Contains(ContextTables.JoiningR, CodePoints[rechts]) ||
                ContextTables.Contains(ContextTables.JoiningD, CodePoints[rechts]));

    }

    #endregion

}
