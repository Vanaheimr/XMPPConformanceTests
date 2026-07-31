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
using System.Net;
using System.Net.Sockets;
using System.Text;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Die abgeleitete Eigenschaft eines Codepoints nach RFC 5892, Abschnitt 1.
/// </summary>
public enum IdnaProperty
{

    /// <summary>In einem Label zulässig.</summary>
    PValid,

    /// <summary>
    /// Zulässig, wenn die Regel aus RFC 5892, Anhang A.1/A.2 erfüllt ist
    /// (die beiden Joiner).
    /// </summary>
    ContextJ,

    /// <summary>
    /// Zulässig, wenn die Regel aus RFC 5892, Anhang A.3 bis A.9 erfüllt ist.
    /// </summary>
    ContextO,

    /// <summary>In einem Label unzulässig.</summary>
    Disallowed,

    /// <summary>
    /// In der zugrunde liegenden Unicode-Fassung nicht vergeben - und damit in
    /// einem Label unzulässig (RFC 5891, Abschnitt 4.2.2 lässt Unassigned nur
    /// beim Nachschlagen zu, nicht beim Registrieren).
    /// </summary>
    Unassigned

}

/// <summary>
/// IDNA2008 auf Codepoint-Ebene: RFC 5892, Abschnitt 1.
/// </summary>
/// <remarks>
/// <b>Dieselben Bausteine wie PRECIS, eine andere Leiter.</b> Beide Leitern
/// stehen auf <see cref="UnicodeSets"/>, und die Unterschiede sind keine
/// Feinheiten:
///
/// <list type="bullet">
///   <item>Statt ASCII7 steht hier <b>LDH</b> - Bindestrich, Ziffern,
///         Kleinbuchstaben. Ein Unterstrich, ein Pluszeichen, ein
///         Grossbuchstabe: alles keine Label-Zeichen.</item>
///   <item><b>Unstable</b> gibt es nur hier, und dieser Zweig wirft alles
///         hinaus, was sich unter Normalisierung und Kleinschreibung
///         verändert.</item>
///   <item><b>IgnorableProperties</b> schliesst hier auch <c>White_Space</c>
///         ein.</item>
///   <item>Am Ende steht <b>DISALLOWED</b> und kein Auffangzweig für Symbole
///         und Satzzeichen: Was nicht ausdrücklich zugelassen ist, gehört nicht
///         in einen Domainnamen.</item>
/// </list>
///
/// Das ist der Grund, aus dem die beiden Leitern getrennt bleiben. Ein
/// gemeinsames Verfahren mit Schaltern wäre kürzer und würde beim Lesen die
/// Frage „gilt das jetzt für Labels oder für Localparts?" bei jeder Zeile neu
/// stellen.
/// </remarks>
public static class Idna
{

    #region DerivedProperty(CodePoint)

    /// <summary>
    /// Die abgeleitete Eigenschaft nach RFC 5892, Abschnitt 1.
    /// </summary>
    public static IdnaProperty DerivedProperty(UInt32 CodePoint)
    {

        // Exceptions (Abschnitt 2.6)
        if (UnicodeSets.TryException(CodePoint, out var ausnahme))
            return ausnahme switch {
                       UnicodeSets.ExceptionValue.PValid    => IdnaProperty.PValid,
                       UnicodeSets.ExceptionValue.ContextO  => IdnaProperty.ContextO,
                       _                                    => IdnaProperty.Disallowed
                   };

        if (UnicodeSets.IsContextODigit(CodePoint))
            return IdnaProperty.ContextO;

        // BackwardCompatible (Abschnitt 2.7) ist leer.

        if (UnicodeSets.IsUnassigned(CodePoint))
            return IdnaProperty.Unassigned;

        // LDH (Abschnitt 2.5) - und nicht ASCII7 wie bei PRECIS.
        if (UnicodeSets.IsLdh(CodePoint))
            return IdnaProperty.PValid;

        if (UnicodeSets.IsJoinControl(CodePoint))
            return IdnaProperty.ContextJ;

        // Unstable (Abschnitt 2.2): Was sich unter Normalisierung und
        // Kleinschreibung verändert, hat in einem Domainnamen nichts zu suchen
        // - sonst gäbe es zwei Schreibweisen für dieselbe Adresse.
        if (UnicodeSets.IsUnstable(CodePoint))
            return IdnaProperty.Disallowed;

        if (UnicodeSets.IsIgnorableProperties(CodePoint))
            return IdnaProperty.Disallowed;

        if (UnicodeSets.IsIgnorableBlocks(CodePoint))
            return IdnaProperty.Disallowed;

        if (UnicodeSets.IsOldHangulJamo(CodePoint))
            return IdnaProperty.Disallowed;

        if (UnicodeSets.IsLetterDigits(CodePoint))
            return IdnaProperty.PValid;

        // Kein Auffangzweig: Was bis hierher gekommen ist, gehört nicht in
        // einen Domainnamen.
        return IdnaProperty.Disallowed;

    }

    #endregion

    #region IsValidDomain(Domain, out Reason)

    /// <summary>Das Präfix eines A-Labels (RFC 5890, Abschnitt 2.3.2.1).</summary>
    public const String AcePrefix = "xn--";

    /// <summary>Die Höchstlänge eines Labels in Oktetten (RFC 1035).</summary>
    public const Int32 MaxLabelOctets = 63;

    /// <summary>
    /// Ist dieser Domainpart nach IDNA2008 gültig?
    /// </summary>
    /// <param name="Domain">Der bereits kleingeschriebene Domainpart.</param>
    /// <param name="Reason">Woran es scheitert - für die Fehlermeldung.</param>
    /// <remarks>
    /// <b>Adressliterale gehen daran vorbei</b>, und zwar nach Vorschrift:
    /// RFC 7622, Abschnitt 3.2 lässt neben dem Domainnamen ausdrücklich eine
    /// IPv4-Adresse und ein eingeklammertes IPv6-Literal zu. Sie sind keine
    /// Domainnamen, und IDNA hat über sie nichts zu sagen.
    ///
    /// <b>Zuletzt die Bidi-Regel</b> (RFC 5893, Abschnitt 2): Sie gilt erst,
    /// wenn ein Label rechtsläufige Zeichen trägt - dann aber für alle Labels
    /// dieses Namens. Deshalb steht sie hier und nicht in der Label-Prüfung:
    /// Ein Label allein kann die Frage nicht beantworten.
    /// </remarks>
    public static Boolean IsValidDomain(String Domain, out String? Reason)
    {

        Reason = null;

        if (Domain.Length == 0)
        {
            Reason = "Ein JID braucht einen Domainpart.";
            return false;
        }

        // RFC 7622, Abschnitt 3.2: Adressliterale sind erlaubt und keine
        // Domainnamen.
        if (IsAddressLiteral(Domain))
            return true;

        var uLabels = new List<String>();

        foreach (var label in Domain.Split('.'))
        {

            if (!IsValidLabel(label, out Reason))
                return false;

            uLabels.Add(label.StartsWith(AcePrefix, StringComparison.Ordinal)
                            ? Punycode.Decode(label[AcePrefix.Length..])!
                            : label);

        }

        // RFC 5893, Abschnitt 2: Die Bidi-Regel gilt für einen „Bidi domain
        // name" - und einer ist er, sobald ein einziges Label rechtsläufige
        // Zeichen trägt. Dann gilt sie für alle Labels, auch für die aus
        // reinem ASCII.
        if (!uLabels.Any(IsRtlLabel))
            return true;

        foreach (var uLabel in uLabels)
            if (!SatisfiesBidiRule(uLabel, out var verstoss))
            {
                Reason = $"Das Label '{uLabel}' verstösst gegen die Bidi-Regel " +
                         $"(RFC 5893, Abschnitt 2): {verstoss}";
                return false;
            }

        return true;

    }

    #endregion

    #region (private) IsValidLabel(Label, out Reason)

    /// <summary>
    /// Ein Label nach RFC 5891, Abschnitt 4.2 - samt A-Label-Rückprobe.
    /// </summary>
    private static Boolean IsValidLabel(String Label, out String? Reason)
    {

        Reason = null;

        if (Label.Length == 0)
        {
            Reason = "Ein Domain-Label darf nicht leer sein.";
            return false;
        }

        // A-Label: Der ASCII-Text ist nur die Verpackung. Geprüft wird, was
        // darin steht - und dass die Verpackung die einzig mögliche ist.
        if (Label.StartsWith(AcePrefix, StringComparison.Ordinal))
        {

            if (Label.Length > MaxLabelOctets)
            {
                Reason = $"Das Label '{Label}' ist länger als {MaxLabelOctets} Oktette.";
                return false;
            }

            var uLabel = Punycode.Decode(Label[AcePrefix.Length..]);

            if (uLabel is null)
            {
                Reason = $"'{Label}' beginnt wie ein A-Label, ist aber kein Punycode.";
                return false;
            }

            // RFC 5890, Abschnitt 2.3.2.1: Ein U-Label trägt mindestens ein
            // Zeichen ausserhalb von ASCII. Sonst gäbe es dasselbe Label
            // zweimal - einmal als es selbst und einmal verpackt.
            if (uLabel.All(Char.IsAscii))
            {
                Reason = $"'{Label}' verpackt reines ASCII ('{uLabel}') als A-Label.";
                return false;
            }

            // RFC 5891, Abschnitt 5.4: Zu einer Bedeutung gehört genau eine
            // Schreibweise. Kodiert die Rückrechnung etwas anderes, ist dieses
            // A-Label eine zweite Adresse für dieselbe Sache.
            if (Punycode.Encode(uLabel) is not String zurueck ||
                !String.Equals(AcePrefix + zurueck, Label, StringComparison.Ordinal))
            {
                Reason = $"'{Label}' ist nicht die kanonische Schreibweise von '{uLabel}'.";
                return false;
            }

            return IsValidULabel(uLabel, Label, out Reason);

        }

        if (Encoding.UTF8.GetByteCount(Label) > MaxLabelOctets)
        {
            Reason = $"Das Label '{Label}' ist länger als {MaxLabelOctets} Oktette.";
            return false;
        }

        return IsValidULabel(Label, Label, out Reason);

    }

    #endregion

    #region (private) IsValidULabel(ULabel, Angezeigt, out Reason)

    /// <summary>
    /// Die Regeln aus RFC 5891, Abschnitt 4.2.3 und 4.2.4 über dem
    /// Unicode-Label.
    /// </summary>
    private static Boolean IsValidULabel(String ULabel, String Angezeigt, out String? Reason)
    {

        Reason = null;

        // Abschnitt 4.2.3.1: kein Bindestrich am Anfang oder Ende ...
        if (ULabel[0] == '-' || ULabel[^1] == '-')
        {
            Reason = $"Das Label '{Angezeigt}' beginnt oder endet mit einem Bindestrich.";
            return false;
        }

        // ... und keine zwei an der dritten und vierten Stelle. Dort steht das
        // Präfix eines A-Labels, und ein U-Label darf nicht so aussehen wie
        // eines.
        if (ULabel.Length >= 4 && ULabel[2] == '-' && ULabel[3] == '-')
        {
            Reason = $"Das Label '{Angezeigt}' trägt '--' an dritter und vierter Stelle.";
            return false;
        }

        // Abschnitt 4.2.3.2: kein kombinierendes Zeichen am Anfang - es hätte
        // nichts, womit es sich verbinden könnte.
        if (Char.GetUnicodeCategory(ULabel, 0) is UnicodeCategory.NonSpacingMark       or
                                                  UnicodeCategory.SpacingCombiningMark or
                                                  UnicodeCategory.EnclosingMark)
        {
            Reason = $"Das Label '{Angezeigt}' beginnt mit einem kombinierenden Zeichen.";
            return false;
        }

        // Als Feld und nicht als Folge: Die kontextabhängigen Regeln fragen
        // nach dem Zeichen davor und danach (RFC 5892, Anhang A).
        var punkte = CodePoints(ULabel).ToArray();

        for (var i = 0; i < punkte.Length; i++)
        {

            var codePoint   = punkte[i];
            var eigenschaft = DerivedProperty(codePoint);

            if (eigenschaft == IdnaProperty.PValid)
                continue;

            if (eigenschaft is IdnaProperty.ContextJ or IdnaProperty.ContextO &&
                Precis.ContextRuleSatisfied(punkte, i))
                continue;

            Reason = $"U+{codePoint:X4} gehört nicht in ein Domain-Label " +
                     $"('{Angezeigt}', RFC 5892: {eigenschaft}).";

            return false;

        }

        return true;

    }

    #endregion

    #region SatisfiesBidiRule(ULabel, out Reason)

    /// <summary>
    /// Trägt dieses Label mindestens ein rechtsläufiges Zeichen (RFC 5893,
    /// Abschnitt 1.4)?
    /// </summary>
    private static Boolean IsRtlLabel(String ULabel)

        => CodePoints(ULabel).Any(cp => BidiClasses.ClassOf(cp) is BidiClass.R  or
                                                                   BidiClass.AL or
                                                                   BidiClass.AN);

    /// <summary>
    /// Die sechs Bedingungen der Bidi-Regel (RFC 5893, Abschnitt 2).
    /// </summary>
    /// <remarks>
    /// <b>Die Richtung eines Labels bestimmt sein erstes Zeichen</b>, und daran
    /// hängt alles Weitere: Ein Label, das mit einem lateinischen Buchstaben
    /// beginnt und ein hebräisches Zeichen enthält, ist kein rechtsläufiges
    /// Label mit einem Gast darin, sondern ein linksläufiges mit einem Verstoss
    /// (Bedingungen 1 und 5).
    ///
    /// Bedingung 3 und 6 - woran ein Label enden darf - sind über
    /// <see cref="IsValidDomain"/> nicht erreichbar: Die Zeichen, mit denen ein
    /// Label falsch enden könnte, fallen schon auf der Codepoint-Ebene heraus.
    /// Sie stehen hier trotzdem, denn diese Funktion ist die Regel aus dem RFC
    /// und nicht die Teilmenge, die ein bestimmter Aufrufer übriglässt.
    /// </remarks>
    internal static Boolean SatisfiesBidiRule(String ULabel, out String? Reason)
    {

        Reason = null;

        var klassen = CodePoints(ULabel).Select(BidiClasses.ClassOf).ToList();

        if (klassen.Count == 0)
        {
            Reason = "Das Label ist leer.";
            return false;
        }

        // Bedingung 1
        if (klassen[0] is not (BidiClass.L or BidiClass.R or BidiClass.AL))
        {
            Reason = $"Das erste Zeichen ist {klassen[0]} und weder L noch R noch AL.";
            return false;
        }

        var rechtslaeufig = klassen[0] is BidiClass.R or BidiClass.AL;

        // Das letzte Zeichen, das kein NSM ist - Bedingung 3 und 6 lassen
        // danach beliebig viele NSM zu.
        var letztes = klassen.FindLastIndex(k => k != BidiClass.NSM);

        if (rechtslaeufig)
        {

            // Bedingung 2
            foreach (var klasse in klassen)
                if (klasse is not (BidiClass.R  or BidiClass.AL or BidiClass.AN or
                                   BidiClass.EN or BidiClass.ES or BidiClass.CS or
                                   BidiClass.ET or BidiClass.ON or BidiClass.BN or
                                   BidiClass.NSM))
                {
                    Reason = $"In einem rechtsläufigen Label ist {klasse} nicht zulässig.";
                    return false;
                }

            // Bedingung 3
            if (letztes < 0 || klassen[letztes] is not (BidiClass.R  or BidiClass.AL or
                                                        BidiClass.EN or BidiClass.AN))
            {
                Reason = "Ein rechtsläufiges Label endet auf R, AL, EN oder AN.";
                return false;
            }

            // Bedingung 4
            if (klassen.Contains(BidiClass.EN) && klassen.Contains(BidiClass.AN))
            {
                Reason = "Europäische und arabische Ziffern stehen nicht im selben Label.";
                return false;
            }

        }

        else
        {

            // Bedingung 5
            foreach (var klasse in klassen)
                if (klasse is not (BidiClass.L  or BidiClass.EN or BidiClass.ES or
                                   BidiClass.CS or BidiClass.ET or BidiClass.ON or
                                   BidiClass.BN or BidiClass.NSM))
                {
                    Reason = $"In einem linksläufigen Label ist {klasse} nicht zulässig.";
                    return false;
                }

            // Bedingung 6
            if (letztes < 0 || klassen[letztes] is not (BidiClass.L or BidiClass.EN))
            {
                Reason = "Ein linksläufiges Label endet auf L oder EN.";
                return false;
            }

        }

        return true;

    }

    #endregion

    #region (private) IsAddressLiteral(Domain) / CodePoints(Text)

    /// <summary>
    /// Ein IPv4-Literal oder ein eingeklammertes IPv6-Literal (RFC 7622,
    /// Abschnitt 3.2).
    /// </summary>
    private static Boolean IsAddressLiteral(String Domain)

        // Voll ausgeschrieben: Hermod bringt einen eigenen Typ dieses Namens
        // mit, und der beantwortet eine andere Frage.
        => Domain.Length > 2 && Domain[0] == '[' && Domain[^1] == ']'
               ? System.Net.IPAddress.TryParse(Domain[1..^1], out _)
               : System.Net.IPAddress.TryParse(Domain, out var adresse) &&
                 adresse.AddressFamily == AddressFamily.InterNetwork;

    private static IEnumerable<UInt32> CodePoints(String Text)
    {

        foreach (var rune in Text.EnumerateRunes())
            yield return (UInt32) rune.Value;

    }

    #endregion

}
