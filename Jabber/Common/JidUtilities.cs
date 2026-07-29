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
/// JIDs nach RFC 7622: zerlegen, vorbereiten, vergleichen.
/// </summary>
/// <remarks>
/// Der Kern des Ganzen ist eine Ungleichbehandlung, die leicht untergeht:
/// <b>Local- und Domainpart sind unabhängig von der Schreibweise, der
/// Resourcepart nicht.</b> Vorher lief der Vergleich überall über
/// <c>OrdinalIgnoreCase</c> auf der ganzen Zeichenkette, und damit galten
/// <c>alice@example.com/Handy</c> und <c>alice@example.com/handy</c> als
/// dieselbe Adresse - zwei verschiedene Geräte desselben Kontos. Die
/// Resource-Vergabe im Server hat sie immer schon unterschieden (dort wird
/// ordinal verglichen); nur das Nachschlagen nicht, und so konnte eine
/// Nachricht auf dem falschen Gerät landen.
///
/// <b>Zerlegt wird in der Reihenfolge aus Abschnitt 3.2</b>, und die ist nicht
/// beliebig: erst am ersten <c>/</c> trennen, dann im vorderen Stück am ersten
/// <c>@</c>. Andersherum machte RFC 7622 aus dem Beispiel 15
/// <c>a.example.com/b@example.net</c> einen JID mit Localpart
/// <c>a.example.com/b</c> - der Resourcepart darf ein <c>@</c> enthalten, der
/// Localpart nicht.
///
/// <b>Was hier noch fehlt.</b> Local- und Resourcepart sind nach RFC 7622
/// Instanzen der PRECIS-Profile UsernameCaseMapped und OpaqueString
/// (RFC 8264/8265). Die Abbildungsregeln dieser Profile - Breitenabbildung,
/// Kleinschreibung, NFC, Leerzeichenabbildung - sind hier vollständig
/// umgesetzt. Die Zugehörigkeit eines Codepoints zur IdentifierClass bzw.
/// FreeformClass ist dagegen angenähert: aus der Unicode-Kategorie und der
/// Frage, ob der Codepoint eine Kompatibilitätszerlegung hat. Das trifft die
/// Fälle, die RFC 7622 selbst als Beispiele führt, und lässt die Ausnahmeliste
/// aus RFC 8264, Tabelle F sowie die Sonderregeln für Joiner und
/// Hangul-Jamo aussen vor. Für IDNA2008 im Domainpart gilt dasselbe: geprüft
/// wird die Form, nicht die Label-Gültigkeit.
/// </remarks>
public static class JidUtilities
{

    #region Data

    /// <summary>
    /// Die Höchstlänge jedes Teils in Oktetten (RFC 7622, Abschnitte 3.2
    /// bis 3.4) - gemessen an der UTF-8-Kodierung, nicht an der Zahl der
    /// Zeichen.
    /// </summary>
    public const Int32 MaxPartOctets = 1023;

    /// <summary>
    /// Zeichen, die RFC 7622, Abschnitt 3.3.1 im Localpart zusätzlich
    /// ausschliesst, obwohl die IdentifierClass sie zuliesse.
    /// </summary>
    /// <remarks>
    /// Sie alle haben in der Adressierung selbst eine Bedeutung oder in XML
    /// eine Sonderrolle. XEP-0106 beschreibt, wie sie sich bei Bedarf
    /// umschreiben lassen.
    /// </remarks>
    public const String LocalpartExcluded = "\"&'/:<>@";

    #endregion

    #region Bare(jid)

    /// <summary>
    /// Der Bare-JID (<c>localpart@domainpart</c>) in vorbereiteter Form.
    /// </summary>
    /// <remarks>
    /// Wirft bewusst nicht: Diese Funktion läuft an Dutzenden Stellen über
    /// das, was von der Leitung kommt, und ein unbrauchbarer JID soll dort zu
    /// „passt auf nichts" führen und nicht zu einer Ausnahme mitten in der
    /// Stanza-Behandlung. Wer wissen will, ob etwas ein JID <i>ist</i>, fragt
    /// <see cref="TryParse"/>.
    /// </remarks>
    public static String Bare(String jid)
    {

        if (TryParse(jid, out var parts))
            return parts.Bare;

        // Nicht zerlegbar: wie bisher der Teil vor dem ersten '/',
        // kleingeschrieben.
        var slash = jid.IndexOf('/');

        return (slash > 0 ? jid[..slash] : jid).ToLowerInvariant();

    }

    #endregion

    #region Resource(jid)

    /// <summary>
    /// Der Resourcepart, oder null - unverändert in seiner Schreibweise.
    /// </summary>
    public static String? Resource(String jid)
    {

        var slash = jid.IndexOf('/');

        return slash >= 0 && slash + 1 < jid.Length
                   ? jid[(slash + 1)..]
                   : null;

    }

    #endregion

    #region AreEqual(a, b)

    /// <summary>
    /// Bezeichnen die beiden JIDs dieselbe Adresse (RFC 7622, Abschnitt 3.4)?
    /// </summary>
    /// <remarks>
    /// Local- und Domainpart werden dabei ohne Rücksicht auf die Schreibweise
    /// verglichen, der Resourcepart mit.
    /// </remarks>
    public static Boolean AreEqual(String? a, String? b)
    {

        if (a is null || b is null)
            return a is null && b is null;

        if (!TryParse(a, out var links) || !TryParse(b, out var rechts))
            // Mindestens einer ist kein JID - dann hilft nur der wörtliche
            // Vergleich, und der ist hier die sichere Antwort.
            return String.Equals(a, b, StringComparison.Ordinal);

        return links == rechts;

    }

    #endregion

    #region TryParse(jid, out Parts) / Parse(jid)

    /// <summary>
    /// Zerlegt und prüft einen JID nach RFC 7622.
    /// </summary>
    /// <returns>false, wenn es keiner ist.</returns>
    public static Boolean TryParse(String? jid, out JidParts Parts)
    {

        Parts = null!;

        try
        {
            Parts = Parse(jid ?? "");
            return true;
        }
        catch (JidFormatException)
        {
            return false;
        }

    }

    /// <summary>
    /// Zerlegt und prüft einen JID nach RFC 7622 und gibt ihn in
    /// vorbereiteter Form zurück.
    /// </summary>
    /// <exception cref="JidFormatException">Wenn es keiner ist.</exception>
    public static JidParts Parse(String jid)
    {

        if (String.IsNullOrEmpty(jid))
            throw new JidFormatException(jid, "Ein JID ist nicht die leere Zeichenkette.");

        // RFC 7622, Abschnitt 3.2: erst am ersten '/', dann am ersten '@'.
        // Die Reihenfolge entscheidet - siehe Beispiel 15.
        var slash         = jid.IndexOf('/');
        var vorDemSlash   = slash >= 0 ? jid[..slash]        : jid;
        var resourcepart  = slash >= 0 ? jid[(slash + 1)..]  : null;

        var at            = vorDemSlash.IndexOf('@');
        var localpart     = at >= 0 ? vorDemSlash[..at]        : null;
        var domainpart    = at >= 0 ? vorDemSlash[(at + 1)..]  : vorDemSlash;

        return new JidParts(localpart  is null ? null : PrepareLocalpart (jid, localpart),
                            PrepareDomainpart(jid, domainpart),
                            resourcepart is null ? null : PrepareResourcepart(jid, resourcepart));

    }

    #endregion

    #region (private) PrepareDomainpart(jid, value)

    /// <summary>
    /// RFC 7622, Abschnitt 3.2: Der Domainpart ist das einzige Pflichtstück.
    /// </summary>
    private static String PrepareDomainpart(String jid, String value)
    {

        if (value.Length == 0)
            throw new JidFormatException(jid, "Ein JID braucht einen Domainpart.");

        // Kleinschreibung und NFC - für den Vergleich zweier Domains ist die
        // Schreibweise ohne Belang.
        var vorbereitet = value.ToLowerInvariant().Normalize(NormalizationForm.FormC);

        CheckLength(jid, vorbereitet, "Domainpart");

        foreach (var codePoint in CodePoints(jid, vorbereitet))
            if (IsControlOrIgnorable(codePoint) || codePoint == ' ')
                throw new JidFormatException(
                          jid,
                          $"U+{codePoint:X4} gehört nicht in einen Domainpart.");

        return vorbereitet;

    }

    #endregion

    #region (private) PrepareLocalpart(jid, value)

    /// <summary>
    /// RFC 7622, Abschnitt 3.3: UsernameCaseMapped aus RFC 8265, plus die
    /// zusätzlich ausgeschlossenen Zeichen aus Abschnitt 3.3.1.
    /// </summary>
    private static String PrepareLocalpart(String jid, String value)
    {

        if (value.Length == 0)
            throw new JidFormatException(jid, "Ein Localpart darf nicht leer sein.");

        // RFC 8265, Abschnitt 3.3: Breitenabbildung, dann Kleinschreibung,
        // dann NFC. Die Breitenabbildung steckt in NFKC; angewandt wird sie
        // hier zeichenweise, damit sie nur Breiten trifft und nicht auch
        // Zeichen wie U+2163 zerlegt - die sollen gerade auffallen.
        var vorbereitet = MapWidth(value).ToLowerInvariant().Normalize(NormalizationForm.FormC);

        CheckLength(jid, vorbereitet, "Localpart");

        foreach (var codePoint in CodePoints(jid, vorbereitet))
        {

            if (codePoint < 0x80 && LocalpartExcluded.Contains((Char) codePoint))
                throw new JidFormatException(
                          jid,
                          $"'{(Char) codePoint}' ist in einem Localpart ausgeschlossen " +
                          "(RFC 7622, Abschnitt 3.3.1).");

            if (!IsIdentifierCharacter(codePoint))
                throw new JidFormatException(
                          jid,
                          $"U+{codePoint:X4} gehört nicht zur PRECIS-IdentifierClass " +
                          "und damit nicht in einen Localpart.");

        }

        return vorbereitet;

    }

    #endregion

    #region (private) PrepareResourcepart(jid, value)

    /// <summary>
    /// RFC 7622, Abschnitt 3.4: OpaqueString aus RFC 8265, Abschnitt 4.2.
    /// </summary>
    /// <remarks>
    /// Keine Breitenabbildung, <b>keine</b> Kleinschreibung, Leerzeichen
    /// ausserhalb von ASCII werden zu U+0020, dann NFC.
    /// </remarks>
    private static String PrepareResourcepart(String jid, String value)
    {

        if (value.Length == 0)
            throw new JidFormatException(jid, "Ein Resourcepart darf nicht leer sein.");

        var sb = new StringBuilder(value.Length);

        foreach (var codePoint in CodePoints(jid, value))
        {

            if (IsControlOrIgnorable(codePoint))
                throw new JidFormatException(
                          jid,
                          $"U+{codePoint:X4} gehört nicht in einen Resourcepart.");

            var zeichen = Char.ConvertFromUtf32((Int32) codePoint);

            sb.Append(codePoint != ' ' &&
                      CharUnicodeInfo.GetUnicodeCategory(zeichen, 0) == UnicodeCategory.SpaceSeparator
                          ? " "
                          : zeichen);

        }

        var vorbereitet = sb.ToString().Normalize(NormalizationForm.FormC);

        CheckLength(jid, vorbereitet, "Resourcepart");

        return vorbereitet;

    }

    #endregion

    #region (private) Zeichenklassen

    /// <summary>
    /// Die Breitenabbildung aus RFC 8265: Zeichen voller und halber Breite
    /// werden auf ihre Zerlegung abgebildet.
    /// </summary>
    /// <remarks>
    /// Zeichenweise und nur für die Kategorie, um die es geht. Ein NFKC über
    /// die ganze Zeichenkette bildete auch U+2163 (ROMAN NUMERAL FOUR) auf
    /// „IV" ab - und genau dieses Zeichen soll nach RFC 7622, Beispiel 20,
    /// den Localpart ungültig machen, statt lautlos zu etwas anderem zu
    /// werden.
    /// </remarks>
    private static String MapWidth(String value)
    {

        var sb = new StringBuilder(value.Length);

        foreach (var rune in value.EnumerateRunes())
        {

            var zeichen = rune.ToString();
            var zerlegt = zeichen.Normalize(NormalizationForm.FormKC);

            // Voll- und Halbbreitenformen liegen in diesen beiden Blöcken.
            sb.Append(rune.Value is (>= 0xFF00 and <= 0xFFEF) or 0x3000
                          ? zerlegt
                          : zeichen);

        }

        return sb.ToString();

    }

    /// <summary>
    /// Gehört der Codepoint zur PRECIS-IdentifierClass (RFC 8264,
    /// Abschnitt 4.2)?
    /// </summary>
    /// <remarks>
    /// Angenähert, und zwar so: druckbares ASCII ist erlaubt; darüber hinaus
    /// nur Buchstaben, Ziffern und kombinierende Zeichen, die keine
    /// Kompatibilitätszerlegung haben.
    ///
    /// Die zweite Bedingung ist die, die RFC 7622 in Beispiel 20 vorführt:
    /// U+2163 ist die römische Vier und zerfällt kompatibel in „IV". Zwei
    /// Konten, die sich nur darin unterscheiden, wären für das Auge dasselbe.
    /// Und Beispiel 21 fällt über die erste: U+265A ist ein Symbol und damit
    /// weder Buchstabe noch Ziffer.
    /// </remarks>
    private static Boolean IsIdentifierCharacter(UInt32 CodePoint)
    {

        // ASCII: alles Druckbare ausser Leerzeichen.
        if (CodePoint < 0x80)
            return CodePoint is >= 0x21 and <= 0x7E;

        var zeichen = Char.ConvertFromUtf32((Int32) CodePoint);

        // LetterDigits nach RFC 8264, Abschnitt 9.1. Lt fehlt hier bewusst:
        // Titelschreibung ist nach der Kleinschreibung ohnehin keine mehr.
        if (CharUnicodeInfo.GetUnicodeCategory(zeichen, 0) is not
                (UnicodeCategory.LowercaseLetter      or
                 UnicodeCategory.UppercaseLetter      or
                 UnicodeCategory.OtherLetter          or
                 UnicodeCategory.ModifierLetter       or
                 UnicodeCategory.DecimalDigitNumber   or
                 UnicodeCategory.NonSpacingMark       or
                 UnicodeCategory.SpacingCombiningMark))
            return false;

        // HasCompat (RFC 8264, Abschnitt 9.6): ausgeschlossen.
        return zeichen.Normalize(NormalizationForm.FormKC) == zeichen;

    }

    /// <summary>
    /// Steuerzeichen, Formatzeichen, Surrogate, Private Use und nicht
    /// Zugewiesenes - in keinem Teil eines JIDs zulässig.
    /// </summary>
    private static Boolean IsControlOrIgnorable(UInt32 CodePoint)
    {

        var zeichen = Char.ConvertFromUtf32((Int32) CodePoint);

        return CharUnicodeInfo.GetUnicodeCategory(zeichen, 0) is
                   UnicodeCategory.Control    or
                   UnicodeCategory.Format     or
                   UnicodeCategory.Surrogate  or
                   UnicodeCategory.PrivateUse or
                   UnicodeCategory.OtherNotAssigned;

    }

    #endregion

    #region (private) Hilfsfunktionen

    /// <summary>
    /// Die Höchstlänge gilt in Oktetten nach der Vorbereitung, nicht in
    /// Zeichen davor (RFC 7622, Abschnitt 3.3).
    /// </summary>
    private static void CheckLength(String jid, String value, String teil)
    {

        var oktette = Encoding.UTF8.GetByteCount(value);

        if (oktette > MaxPartOctets)
            throw new JidFormatException(
                      jid,
                      $"Der {teil} ist {oktette} Oktette lang, erlaubt sind {MaxPartOctets}.");

    }

    /// <summary>
    /// Die Codepoints - mit einer verständlichen Meldung statt einer
    /// Ausnahme aus der Tiefe, wenn ein halbes Zeichen darin steht.
    /// </summary>
    private static IEnumerable<UInt32> CodePoints(String jid, String value)
    {

        for (var i = 0; i < value.Length; i++)
        {

            var c = value[i];

            if (Char.IsHighSurrogate(c) && i + 1 < value.Length && Char.IsLowSurrogate(value[i + 1]))
            {
                yield return (UInt32) Char.ConvertToUtf32(c, value[i + 1]);
                i++;
                continue;
            }

            if (Char.IsSurrogate(c))
                throw new JidFormatException(
                          jid,
                          $"U+{(UInt32) c:X4} steht als halbes Zeichen da.");

            yield return c;

        }

    }

    #endregion

}
