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
/// Punycode nach RFC 3492: die Kodierung, mit der ein Domain-Label aus Unicode
/// in ASCII passt.
/// </summary>
/// <remarks>
/// <b>Selbst gerechnet und nicht von der Laufzeit geholt</b>, obwohl .NET mit
/// <c>IdnMapping</c> etwas Ähnliches mitbringt. Der Grund ist nicht Stolz:
/// <c>IdnMapping</c> bringt auf .NET seine eigene Auslegung mit (UTS 46 über
/// ICU), die abbildet, wo IDNA2008 ablehnt - etwa Grossbuchstaben. Wer die
/// Gültigkeit eines Labels prüfen will, darf die Prüfung nicht an etwas
/// abgeben, das vorher zurechtbiegt.
///
/// Die Rechnung selbst ist der Bootstring-Algorithmus aus Abschnitt 6, mit den
/// Parametern aus Abschnitt 5. Geprüft wird sie gegen die elf Beispiele aus
/// Abschnitt 7.1 - in beide Richtungen.
/// </remarks>
public static class Punycode
{

    #region Data

    private const Int32  Base         = 36;
    private const Int32  TMin         = 1;
    private const Int32  TMax         = 26;
    private const Int32  Skew         = 38;
    private const Int32  Damp         = 700;
    private const Int32  InitialBias  = 72;
    private const Int32  InitialN     = 0x80;
    private const Char   Delimiter    = '-';

    /// <summary>Die grösste Codepoint-Zahl, die Unicode kennt.</summary>
    private const Int32  MaxCodePoint = 0x10FFFF;

    #endregion

    #region Decode(Punycode)

    /// <summary>
    /// Dekodiert ein Punycode-Label - oder gibt <c>null</c> zurück, wenn es
    /// keines ist.
    /// </summary>
    public static String? Decode(String Punycode)
    {

        if (Punycode.Length == 0)
            return null;

        var ausgabe    = new List<Int32>();
        var n          = InitialN;
        var i          = 0;
        var bias       = InitialBias;

        // Der letzte Trenner trennt den ASCII-Teil vom Rest (Abschnitt 6.2).
        var trenner    = Punycode.LastIndexOf(Delimiter);

        if (trenner > 0)
        {

            foreach (var zeichen in Punycode[..trenner])
            {

                if (zeichen >= 0x80)
                    return null;

                ausgabe.Add(zeichen);

            }

        }

        for (var index = trenner < 0 ? 0 : trenner + 1; index < Punycode.Length; )
        {

            var altesI  = i;
            var gewicht = 1;

            for (var k = Base; ; k += Base)
            {

                if (index >= Punycode.Length)
                    return null;

                var ziffer = Digit(Punycode[index++]);

                if (ziffer < 0)
                    return null;

                // Überlauf: Ein Label, das sich nur mit mehr als 31 Bit
                // schreiben liesse, ist keines.
                if (ziffer > (Int32.MaxValue - i) / gewicht)
                    return null;

                i += ziffer * gewicht;

                var t = k <= bias            ? TMin
                            : k >= bias + TMax ? TMax
                            : k - bias;

                if (ziffer < t)
                    break;

                if (gewicht > Int32.MaxValue / (Base - t))
                    return null;

                gewicht *= Base - t;

            }

            bias = Adapt(i - altesI, ausgabe.Count + 1, altesI == 0);

            if (i / (ausgabe.Count + 1) > Int32.MaxValue - n)
                return null;

            n += i / (ausgabe.Count + 1);
            i %= ausgabe.Count + 1;

            if (n > MaxCodePoint || (n >= 0xD800 && n <= 0xDFFF))
                return null;

            ausgabe.Insert(i++, n);

        }

        var sb = new StringBuilder(ausgabe.Count);

        foreach (var codePoint in ausgabe)
            sb.Append(Char.ConvertFromUtf32(codePoint));

        return sb.ToString();

    }

    #endregion

    #region Encode(Text)

    /// <summary>
    /// Kodiert ein Label - oder gibt <c>null</c> zurück, wenn das nicht geht.
    /// </summary>
    /// <remarks>
    /// Der Trenner steht auch dann da, wenn nichts Nicht-ASCII folgt
    /// (Abschnitt 6.3): <c>abc</c> wird zu <c>abc-</c>. Für ein A-Label ist das
    /// ohne Belang - dort steht ohnehin das Präfix <c>xn--</c> davor, und die
    /// Rückprobe vergleicht mit derselben Rechnung.
    /// </remarks>
    public static String? Encode(String Text)
    {

        var codePoints = new List<Int32>();

        for (var i = 0; i < Text.Length; i++)
        {

            if (Char.IsHighSurrogate(Text[i]) && i + 1 < Text.Length && Char.IsLowSurrogate(Text[i + 1]))
            {
                codePoints.Add(Char.ConvertToUtf32(Text[i], Text[i + 1]));
                i++;
            }

            else if (Char.IsSurrogate(Text[i]))
                return null;

            else
                codePoints.Add(Text[i]);

        }

        var sb        = new StringBuilder();
        var n         = InitialN;
        var delta     = 0;
        var bias      = InitialBias;

        foreach (var codePoint in codePoints)
            if (codePoint < 0x80)
                sb.Append((Char) codePoint);

        var behandelt = sb.Length;
        var einfache  = behandelt;

        if (einfache > 0)
            sb.Append(Delimiter);

        while (behandelt < codePoints.Count)
        {

            // Der nächste noch nicht behandelte Codepoint.
            var m = Int32.MaxValue;

            foreach (var codePoint in codePoints)
                if (codePoint >= n && codePoint < m)
                    m = codePoint;

            if (m - n > (Int32.MaxValue - delta) / (behandelt + 1))
                return null;

            delta += (m - n) * (behandelt + 1);
            n      = m;

            foreach (var codePoint in codePoints)
            {

                if (codePoint < n)
                {

                    if (++delta == 0)
                        return null;

                }

                else if (codePoint == n)
                {

                    var q = delta;

                    for (var k = Base; ; k += Base)
                    {

                        var t = k <= bias            ? TMin
                                    : k >= bias + TMax ? TMax
                                    : k - bias;

                        if (q < t)
                            break;

                        sb.Append(Character(t + (q - t) % (Base - t)));
                        q = (q - t) / (Base - t);

                    }

                    sb.Append(Character(q));

                    bias   = Adapt(delta, behandelt + 1, behandelt == einfache);
                    delta  = 0;
                    behandelt++;

                }

            }

            delta++;
            n++;

        }

        return sb.ToString();

    }

    #endregion

    #region (private) Bootstring-Rechnung

    /// <summary>RFC 3492, Abschnitt 6.1: die Anpassung der Vorspannung.</summary>
    private static Int32 Adapt(Int32 Delta, Int32 Anzahl, Boolean ErsteAnpassung)
    {

        Delta = ErsteAnpassung ? Delta / Damp : Delta / 2;
        Delta += Delta / Anzahl;

        var k = 0;

        while (Delta > ((Base - TMin) * TMax) / 2)
        {
            Delta /= Base - TMin;
            k     += Base;
        }

        return k + (Base - TMin + 1) * Delta / (Delta + Skew);

    }

    /// <summary>Der Wert einer Ziffer des 36er-Alphabets, oder -1.</summary>
    private static Int32 Digit(Char Character)

        => Character switch {
               >= 'a' and <= 'z'  => Character - 'a',
               >= 'A' and <= 'Z'  => Character - 'A',
               >= '0' and <= '9'  => Character - '0' + 26,
               _                  => -1
           };

    /// <summary>Die Ziffer zu einem Wert - Kleinbuchstaben, dann Ziffern.</summary>
    private static Char Character(Int32 Digit)

        => (Char) (Digit < 26 ? Digit + 'a' : Digit - 26 + '0');

    #endregion

}
