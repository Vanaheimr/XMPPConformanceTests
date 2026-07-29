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

using System.Text;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// SASLprep (RFC 4013), das StringPrep-Profil für Benutzernamen und Passwörter.
/// </summary>
/// <remarks>
/// Zwei Leute tippen dasselbe Passwort und meinen dasselbe - nur haben sie
/// verschiedene Tastaturen, verschiedene Eingabemethoden, verschiedene
/// Betriebssysteme. Das <c>ä</c> kommt einmal als ein Zeichen an und einmal als
/// <c>a</c> mit angehängtem Trema; ein Leerzeichen ist mal U+0020 und mal ein
/// geschütztes. SASLprep führt beides auf dieselbe Form zurück, damit aus
/// derselben Eingabe derselbe Schlüssel wird.
///
/// Bis hierhin bestand diese Vorbereitung aus einer einzigen Zeile - NFKC. Das
/// deckt den zweiten Fall ab und keinen der übrigen: Die Abbildungen fehlten
/// (ein weiches Trennzeichen im Passwort blieb stehen, statt zu verschwinden),
/// die Verbote fehlten (ein Steuerzeichen ging durch), und die
/// Bidi-Prüfung fehlte ganz.
///
/// Praktisch heisst das: Ein Passwort ausserhalb von ASCII wurde hier anders
/// vorbereitet als bei Prosody oder ejabberd, und die Anmeldung schlug fehl,
/// ohne dass jemand hätte sagen können warum. Genau dafür gibt es das Profil.
///
/// Die vier Schritte nach RFC 3454, Abschnitt 7:
///
/// <list type="number">
///   <item>abbilden (RFC 4013, Abschnitt 2.1),</item>
///   <item>nach NFKC normalisieren (2.2),</item>
///   <item>verbotene Zeichen zurückweisen (2.3) - dazu die nicht zugewiesenen (2.5),</item>
///   <item>die Bidi-Regeln prüfen (2.4).</item>
/// </list>
///
/// Zurückgewiesen wird durch eine Ausnahme und nicht durch stilles Ersetzen.
/// Ein Passwort, das sich nicht eindeutig vorbereiten lässt, ist keines: Wer es
/// zurechtbiegt, lässt am Ende zwei verschiedene Eingaben auf denselben
/// Schlüssel führen.
/// </remarks>
public static class SaslPrep
{

    #region Prepare(Input, AllowUnassigned = false)

    /// <summary>
    /// Bereitet eine Zeichenkette nach SASLprep vor.
    /// </summary>
    /// <param name="Input">Benutzername oder Passwort.</param>
    /// <param name="AllowUnassigned">
    /// Nicht zugewiesene Codepoints durchlassen. Vorgabe ist <c>false</c>, also
    /// die Behandlung als „stored string" nach RFC 4013, Abschnitt 2.5: Was in
    /// Unicode 3.2 noch keine Bedeutung hatte, gehört nicht in ein Passwort,
    /// weil zwei Gegenstellen es verschieden normalisieren könnten.
    /// </param>
    /// <exception cref="AuthenticationException">
    /// Bei einem verbotenen Zeichen oder einem Verstoss gegen die Bidi-Regeln.
    /// </exception>
    public static String Prepare(String   Input,
                                 Boolean  AllowUnassigned   = false)
    {

        var mapped      = Map(Input);
        var normalised  = mapped.Normalize(NormalizationForm.FormKC);

        Prohibit(normalised, AllowUnassigned);
        CheckBidi(normalised);

        return normalised;

    }

    #endregion

    #region (private) Map(Input)

    /// <summary>
    /// RFC 4013, Abschnitt 2.1: Leerzeichen ausserhalb von ASCII werden zu
    /// U+0020, und was in Tabelle B.1 steht, fällt weg.
    /// </summary>
    /// <remarks>
    /// Das Wegfallen ist der Teil, der überrascht: Ein weiches Trennzeichen
    /// oder ein Variantenwähler im Passwort ist unsichtbar, geht also beim
    /// Tippen leicht verloren oder kommt versehentlich hinein. Beide Fassungen
    /// sollen dasselbe Passwort sein.
    /// </remarks>
    private static String Map(String Input)
    {

        var sb = new StringBuilder(Input.Length);

        foreach (var codePoint in CodePoints(Input))
        {

            if (StringPrepTables.Contains(StringPrepTables.MappedToNothing, codePoint))
                continue;

            if (StringPrepTables.Contains(StringPrepTables.NonAsciiSpace, codePoint))
            {
                sb.Append(' ');
                continue;
            }

            sb.Append(Char.ConvertFromUtf32((Int32) codePoint));

        }

        return sb.ToString();

    }

    #endregion

    #region (private) Prohibit(Value, AllowUnassigned)

    /// <summary>
    /// RFC 4013, Abschnitt 2.3 und 2.5: die verbotenen Zeichen.
    /// </summary>
    private static void Prohibit(String Value, Boolean AllowUnassigned)
    {

        foreach (var codePoint in CodePoints(Value))
        {

            var grund = Forbidden(codePoint);

            if (grund is not null)
                throw new AuthenticationException(
                          $"SASLprep: U+{codePoint:X4} ist nicht zulässig ({grund}, RFC 3454).");

            if (!AllowUnassigned &&
                StringPrepTables.Contains(StringPrepTables.Unassigned, codePoint))
                throw new AuthenticationException(
                          $"SASLprep: U+{codePoint:X4} war in Unicode 3.2 nicht zugewiesen " +
                          "(Tabelle A.1, RFC 3454).");

        }

    }

    /// <summary>Die Tabelle, die diesen Codepoint verbietet - oder null.</summary>
    private static String? Forbidden(UInt32 CodePoint)
    {

        if (StringPrepTables.Contains(StringPrepTables.NonAsciiSpace,               CodePoint)) return "Tabelle C.1.2";
        if (StringPrepTables.Contains(StringPrepTables.AsciiControl,                CodePoint)) return "Tabelle C.2.1";
        if (StringPrepTables.Contains(StringPrepTables.NonAsciiControl,             CodePoint)) return "Tabelle C.2.2";
        if (StringPrepTables.Contains(StringPrepTables.PrivateUse,                  CodePoint)) return "Tabelle C.3";
        if (StringPrepTables.Contains(StringPrepTables.NonCharacter,                CodePoint)) return "Tabelle C.4";
        if (StringPrepTables.Contains(StringPrepTables.Surrogate,                   CodePoint)) return "Tabelle C.5";
        if (StringPrepTables.Contains(StringPrepTables.InappropriateForPlainText,   CodePoint)) return "Tabelle C.6";
        if (StringPrepTables.Contains(StringPrepTables.InappropriateForCanonical,   CodePoint)) return "Tabelle C.7";
        if (StringPrepTables.Contains(StringPrepTables.DisplayOrDeprecated,         CodePoint)) return "Tabelle C.8";
        if (StringPrepTables.Contains(StringPrepTables.Tagging,                     CodePoint)) return "Tabelle C.9";

        return null;

    }

    #endregion

    #region (private) CheckBidi(Value)

    /// <summary>
    /// RFC 3454, Abschnitt 6: die Regeln für gemischte Schreibrichtungen.
    /// </summary>
    /// <remarks>
    /// Eine Zeichenkette aus rechtsläufiger und linksläufiger Schrift wird je
    /// nach Umgebung verschieden angezeigt. Wer sie liest, sieht also nicht
    /// unbedingt, was darin steht - und ein Angreifer kann einen Namen
    /// zusammensetzen, der wie ein anderer aussieht. Die beiden Regeln
    /// schliessen die Fälle aus, in denen die Anzeige mehrdeutig würde.
    /// </remarks>
    private static void CheckBidi(String Value)
    {

        var codePoints = CodePoints(Value).ToList();

        if (codePoints.Count == 0)
            return;

        var hatRandAL = codePoints.Any(cp => StringPrepTables.Contains(StringPrepTables.RandALCat, cp));

        if (!hatRandAL)
            return;

        // Regel 2: Rechtsläufiges und linksläufiges Zeichen zusammen - verboten.
        if (codePoints.Any(cp => StringPrepTables.Contains(StringPrepTables.LCat, cp)))
            throw new AuthenticationException(
                      "SASLprep: rechtsläufige und linksläufige Zeichen zusammen " +
                      "(RFC 3454, Abschnitt 6, Regel 2).");

        // Regel 3: Steht rechtsläufiges darin, müssen erstes und letztes Zeichen
        // rechtsläufig sein.
        if (!StringPrepTables.Contains(StringPrepTables.RandALCat, codePoints[0]) ||
            !StringPrepTables.Contains(StringPrepTables.RandALCat, codePoints[^1]))
            throw new AuthenticationException(
                      "SASLprep: eine rechtsläufige Zeichenkette muss rechtsläufig " +
                      "beginnen und enden (RFC 3454, Abschnitt 6, Regel 3).");

    }

    #endregion

    #region (private) CodePoints(Value)

    /// <summary>
    /// Die Codepoints der Zeichenkette.
    /// </summary>
    /// <remarks>
    /// Von Hand und nicht über <c>EnumerateRunes</c>: Das ersetzt ein
    /// alleinstehendes Surrogat stillschweigend durch U+FFFD, und stillschweigend
    /// ist hier das Falsche - ein halbes Zeichen ist nach Tabelle C.5 verboten
    /// und soll als solches gemeldet werden.
    /// </remarks>
    private static IEnumerable<UInt32> CodePoints(String Value)
    {

        for (var i = 0; i < Value.Length; i++)
        {

            var c = Value[i];

            if (Char.IsHighSurrogate(c))
            {

                if (i + 1 < Value.Length && Char.IsLowSurrogate(Value[i + 1]))
                {
                    yield return (UInt32) Char.ConvertToUtf32(c, Value[i + 1]);
                    i++;
                    continue;
                }

                throw new AuthenticationException(
                          $"SASLprep: U+{(UInt32) c:X4} steht als halbes Zeichen da " +
                          "(Tabelle C.5, RFC 3454).");

            }

            if (Char.IsLowSurrogate(c))
                throw new AuthenticationException(
                          $"SASLprep: U+{(UInt32) c:X4} steht als halbes Zeichen da " +
                          "(Tabelle C.5, RFC 3454).");

            yield return c;

        }

    }

    #endregion

}
