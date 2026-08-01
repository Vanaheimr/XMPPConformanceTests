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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// So viel Protocol Buffers, wie OMEMO braucht - Varints und
/// längenbegrenzte Felder, mehr nicht.
/// </summary>
/// <remarks>
/// <b>Warum von Hand und nicht mit einer Bibliothek.</b> Gebraucht werden drei
/// Nachrichtenarten mit zusammen elf Feldern, alle vom Typ <c>uint32</c> oder
/// <c>bytes</c>. Dafür einen Codegenerator samt Werkzeugkette in den Bau zu
/// hängen, kostet mehr als es trägt - und die Kodierung selbst ist in
/// vierzig Zeilen erschöpfend beschrieben.
///
/// <b>Der eigentliche Grund ist aber ein anderer:</b> Diese Bytes gehen in die
/// Beigabe der Verschlüsselung ein (XEP-0384, Abschnitt 4.3:
/// <c>ad ‖ OMEMOMessage.proto(header)</c>). Damit muss die Kodierung
/// <b>bitgenau reproduzierbar</b> sein - beide Seiten müssen aus demselben
/// Header dieselben Bytes bilden, sonst scheitert jede Prüfung. Eine
/// Bibliothek, die Felder umsortiert, Vorgabewerte weglässt oder Varints
/// anders auffüllt, wäre hier kein Komfort, sondern eine Fehlerquelle, die
/// niemand sieht.
///
/// Deshalb schreibt dieser Kodierer immer alle Felder, immer in der
/// Reihenfolge ihrer Nummern und nie mit gepolsterten Varints.
/// </remarks>
public static class Protobuf
{

    #region Schreiben

    /// <summary>
    /// Ein Varint (base 128, kleinstwertige Gruppe zuerst, oberstes Bit als
    /// Fortsetzungszeichen).
    /// </summary>
    public static void WriteVarint(List<Byte> ziel, UInt64 wert)
    {

        while (wert >= 0x80)
        {
            ziel.Add((Byte) (wert | 0x80));
            wert >>= 7;
        }

        ziel.Add((Byte) wert);

    }

    /// <summary>
    /// Ein Feld vom Typ <c>uint32</c> (Wire-Type 0).
    /// </summary>
    public static void WriteUInt32(List<Byte> ziel, Int32 feldnummer, UInt32 wert)
    {
        WriteVarint(ziel, (UInt64) feldnummer << 3 | 0);
        WriteVarint(ziel, wert);
    }

    /// <summary>
    /// Ein Feld vom Typ <c>bytes</c> (Wire-Type 2): Länge, dann Inhalt.
    /// </summary>
    public static void WriteBytes(List<Byte> ziel, Int32 feldnummer, Byte[] wert)
    {

        WriteVarint(ziel, (UInt64) feldnummer << 3 | 2);
        WriteVarint(ziel, (UInt64) wert.Length);

        ziel.AddRange(wert);

    }

    #endregion

    #region Lesen

    /// <summary>
    /// Liest die Felder einer Nachricht in der Reihenfolge, in der sie
    /// dastehen.
    /// </summary>
    /// <returns>
    /// Feldnummer, Wire-Type und der Rohwert: bei Wire-Type 0 die Zahl, bei
    /// Wire-Type 2 die Bytes.
    /// </returns>
    /// <remarks>
    /// Unbekannte Feldnummern werden übersprungen und nicht abgewiesen - so
    /// will es Protocol Buffers, und so bleibt eine spätere Fassung der
    /// Spezifikation lesbar. Ein <b>unbekannter Wire-Type</b> dagegen ist ein
    /// Abbruch: Ab dort ist nicht mehr zu erkennen, wo das nächste Feld
    /// anfängt, und was danach gelesen würde, wäre geraten.
    /// </remarks>
    public static IEnumerable<(Int32 Field, Int32 WireType, UInt64 Number, Byte[] Data)> Read(Byte[] daten)
    {

        var i = 0;

        while (i < daten.Length)
        {

            var schluessel  = ReadVarint(daten, ref i);
            var feld        = (Int32) (schluessel >> 3);
            var typ         = (Int32) (schluessel & 7);

            switch (typ)
            {

                case 0:
                    yield return (feld, typ, ReadVarint(daten, ref i), []);
                    break;

                case 2:
                    var laenge = (Int32) ReadVarint(daten, ref i);

                    if (laenge < 0 || i + laenge > daten.Length)
                        throw new FormatException("Ein längenbegrenztes Feld reicht über das Ende hinaus.");

                    yield return (feld, typ, 0, daten[i..(i + laenge)]);
                    i += laenge;
                    break;

                default:
                    throw new FormatException(
                              $"Wire-Type {typ} kommt in OMEMO nicht vor; ab hier ist nicht mehr zu " +
                              "erkennen, wo das nächste Feld anfängt.");

            }

        }

    }

    /// <summary>Liest ein Varint und schiebt den Lesezeiger weiter.</summary>
    public static UInt64 ReadVarint(Byte[] daten, ref Int32 i)
    {

        UInt64  wert       = 0;
        var     verschub   = 0;

        while (true)
        {

            if (i >= daten.Length)
                throw new FormatException("Das Varint endet vor seinem letzten Byte.");

            // Zehn Gruppen à sieben Bit sind siebzig - mehr als in einen
            // UInt64 passen. Ohne diese Grenze liesse sich mit einer Kette von
            // Fortsetzungsbytes beliebig weit über den Wert hinauslesen.
            if (verschub > 63)
                throw new FormatException("Das Varint ist länger, als ein 64-Bit-Wert sein kann.");

            var b = daten[i++];

            wert |= (UInt64) (b & 0x7F) << verschub;

            if ((b & 0x80) == 0)
                return wert;

            verschub += 7;

        }

    }

    #endregion

}
