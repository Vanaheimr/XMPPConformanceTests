#!/usr/bin/env python3
"""Erzeugt Jabber/Common/BidiClasses.cs aus der Unicode-Datei DerivedBidiClass.txt.

Die Bidi-Regel aus RFC 5893 fragt fuer jeden Codepoint eines Labels nach seiner
Eigenschaft Bidi_Class. .NET liefert sie nicht, und sie laesst sich auch nicht
aus etwas anderem ableiten: Ob ein Buchstabe R, AL oder L ist, haengt an seiner
Schrift, nicht an seiner Kategorie.

Von Hand abgeschrieben waere die Tabelle nicht zu pruefen - es sind ueber
tausend Bereiche. Deshalb dieser Weg: Der Generator laedt die Unicode-Datei,
liest die Bereiche und schreibt die C#-Datei. Wer die Tabelle anzweifelt, laesst
ihn laufen und vergleicht.

    python3 tools/unicode/generate-bidiclass.py

Ohne Argumente wird die Datei von unicode.org geholt; mit einem Pfad als erstem
Argument aus einer oertlichen Kopie gelesen.

Nicht aufgeschrieben wird die Klasse L. Sie ist die groesste und zugleich die
Vorgabe: Was in keiner der anderen Tabellen steht, ist L. Fuer die Bidi-Regel
genuegt das, denn alles, was hier ankommt, ist ein zugewiesener Codepoint - die
nicht zugewiesenen hat die IDNA-Leiter vorher aussortiert.
"""

import re
import sys
import urllib.request
from pathlib import Path

VERSION = "15.1.0"
URL     = f"https://www.unicode.org/Public/{VERSION}/ucd/extracted/DerivedBidiClass.txt"

# Bidi_Class -> (C#-Feldname, Kommentar). L fehlt mit Absicht (siehe oben).
CLASSES = {
    "R":    ("R",    "Rechtslaeufig (hebraeisch und verwandte Schriften)"),
    "AL":   ("AL",   "Rechtslaeufig arabisch"),
    "AN":   ("AN",   "Arabische Ziffern"),
    "EN":   ("EN",   "Europaeische Ziffern"),
    "ES":   ("ES",   "Europaeische Trennzeichen (Plus, Minus)"),
    "CS":   ("CS",   "Gemeinsame Trennzeichen (Komma, Punkt, Doppelpunkt)"),
    "ET":   ("ET",   "Europaeische Zusatzzeichen (Waehrung, Prozent)"),
    "ON":   ("ON",   "Sonstige neutrale Zeichen"),
    "BN":   ("BN",   "Ohne Wirkung auf die Laufrichtung"),
    "NSM":  ("NSM",  "Nicht abstandshaltende Zeichen (kombinierende Marken)"),
}

HEADER = """/*
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
/// Die Bidi-Klasse eines Codepoints (Unicode %VERSION%, DerivedBidiClass.txt).
/// </summary>
public enum BidiClass
{
    L,
    R,
    AL,
    AN,
    EN,
    ES,
    CS,
    ET,
    ON,
    BN,
    NSM
}

/// <summary>
/// Die Tabelle der Bidi-Klassen, auf der die Bidi-Regel aus RFC 5893 steht.
/// </summary>
/// <remarks>
/// <b>Erzeugt von tools/unicode/generate-bidiclass.py - nicht von Hand
/// aendern.</b>
///
/// Jede Klasse steht als flache Folge von Bereichsgrenzen da: an gerader Stelle
/// der erste, an ungerader der letzte Codepoint des Bereichs, aufsteigend
/// sortiert. Gesucht wird binaer.
///
/// <b>L ist nicht aufgeschrieben</b>, sondern die Vorgabe: Was in keiner der
/// anderen Tabellen steht, ist L. Das ist keine Abkuerzung, sondern die
/// Bauweise der Unicode-Datei selbst - und es spart die groesste aller
/// Tabellen.
/// </remarks>
internal static class BidiClasses
{

    #region ClassOf(CodePoint)

    /// <summary>Die Bidi-Klasse dieses Codepoints.</summary>
    public static BidiClass ClassOf(UInt32 CodePoint)
    {

        if (Contains(R,   CodePoint))  return BidiClass.R;
        if (Contains(AL,  CodePoint))  return BidiClass.AL;
        if (Contains(AN,  CodePoint))  return BidiClass.AN;
        if (Contains(EN,  CodePoint))  return BidiClass.EN;
        if (Contains(ES,  CodePoint))  return BidiClass.ES;
        if (Contains(CS,  CodePoint))  return BidiClass.CS;
        if (Contains(ET,  CodePoint))  return BidiClass.ET;
        if (Contains(ON,  CodePoint))  return BidiClass.ON;
        if (Contains(BN,  CodePoint))  return BidiClass.BN;
        if (Contains(NSM, CodePoint))  return BidiClass.NSM;

        return BidiClass.L;

    }

    #endregion

    #region (private) Contains(Table, CodePoint)

    /// <summary>Liegt der Codepoint in einem der Bereiche dieser Tabelle?</summary>
    private static Boolean Contains(UInt32[] Table, UInt32 CodePoint)
    {

        var low   = 0;
        var high  = Table.Length / 2 - 1;

        while (low <= high)
        {

            var mid = (low + high) / 2;

            if      (CodePoint < Table[mid * 2])      high  = mid - 1;
            else if (CodePoint > Table[mid * 2 + 1])  low   = mid + 1;
            else                                      return true;

        }

        return false;

    }

    #endregion

"""


def load(source):
    if source:
        return Path(source).read_text(encoding="utf-8").splitlines()
    with urllib.request.urlopen(URL) as response:
        return response.read().decode("utf-8").splitlines()


def ranges(lines, name):
    """Die Bereiche einer Bidi-Klasse, aufsteigend sortiert und zusammengefasst.

    Zeilen mit '@missing' beschreiben die Vorgabewerte fuer nicht zugewiesene
    Codepoints. Sie bleiben aussen vor: Was nicht zugewiesen ist, kommt in einem
    Label ohnehin nicht vor - die IDNA-Leiter weist es vorher ab.
    """

    found = []

    for line in lines:

        line = line.split("#", 1)[0].strip()

        if not line or line.startswith("@"):
            continue

        match = re.match(r"^([0-9A-F]{4,6})(?:\.\.([0-9A-F]{4,6}))?\s*;\s*(\w+)\s*$", line)

        if not match:
            raise SystemExit(f"unverstandene Zeile {line!r}")

        if match.group(3) != name:
            continue

        first = int(match.group(1), 16)
        last  = int(match.group(2) or match.group(1), 16)

        found.append((first, last))

    if not found:
        raise SystemExit(f"Klasse {name}: kein einziger Bereich gefunden")

    found.sort()

    merged = []

    for first, last in found:
        if merged and first <= merged[-1][1] + 1:
            merged[-1] = (merged[-1][0], max(merged[-1][1], last))
        else:
            merged.append((first, last))

    return merged


def emit(field, comment, values):

    out = [f"    /// <summary>{comment} ({len(values)} Bereiche).</summary>",
           f"    internal static readonly UInt32[] {field} = ["]

    line = "       "

    for first, last in values:
        piece = f" 0x{first:04X}, 0x{last:04X},"
        if len(line) + len(piece) > 96:
            out.append(line)
            line = "       "
        line += piece

    out.append(line)
    out.append("    ];")
    out.append("")

    return out


def main():

    lines = load(sys.argv[1] if len(sys.argv) > 1 else None)

    body = []

    for name, (field, comment) in CLASSES.items():
        body.extend(emit(field, comment, ranges(lines, name)))

    target = Path(__file__).resolve().parents[2] / "Jabber" / "Common" / "BidiClasses.cs"

    text = HEADER.replace("%VERSION%", VERSION) + "\n".join(body) + "}\n"

    target.write_bytes(b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode("utf-8"))

    print(f"{target} geschrieben")


if __name__ == "__main__":
    main()
