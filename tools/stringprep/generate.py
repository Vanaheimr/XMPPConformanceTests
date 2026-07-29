#!/usr/bin/env python3
"""Erzeugt Jabber/Auth/StringPrepTables.cs aus RFC 3454.

Die Tabellen aus RFC 3454 sind normativ und lang - zusammen rund neunhundert
Bereiche. Von Hand abgeschrieben waere ein Tippfehler darin praktisch nicht zu
finden: Er faellt erst auf, wenn ein bestimmtes Zeichen in einem Passwort
vorkommt, und dann als Anmeldung, die grundlos scheitert.

Deshalb dieser Weg. Der Generator laedt den RFC-Text, liest die Bereiche und
schreibt die C#-Datei. Wer die Tabellen anzweifelt, laesst ihn laufen und
vergleicht.

    python3 tools/stringprep/generate.py

Ohne Argumente wird der RFC von rfc-editor.org geholt; mit einem Pfad als
erstem Argument aus einer oertlichen Kopie gelesen.
"""

import re
import sys
import urllib.request
from pathlib import Path

RFC_URL = "https://www.rfc-editor.org/rfc/rfc3454.txt"

# Tabellenname -> (C#-Feldname, Kommentar)
TABLES = {
    "A.1":   ("Unassigned",        "A.1: In Unicode 3.2 nicht zugewiesen"),
    "B.1":   ("MappedToNothing",   "B.1: Faellt bei der Abbildung ersatzlos weg"),
    "C.1.2": ("NonAsciiSpace",     "C.1.2: Leerzeichen ausserhalb von ASCII"),
    "C.2.1": ("AsciiControl",      "C.2.1: ASCII-Steuerzeichen"),
    "C.2.2": ("NonAsciiControl",   "C.2.2: Steuerzeichen ausserhalb von ASCII"),
    "C.3":   ("PrivateUse",        "C.3: Private Use"),
    "C.4":   ("NonCharacter",      "C.4: Nicht-Zeichen"),
    "C.5":   ("Surrogate",         "C.5: Surrogate"),
    "C.6":   ("InappropriateForPlainText",
              "C.6: Fuer Klartext ungeeignet"),
    "C.7":   ("InappropriateForCanonical",
              "C.7: Fuer die kanonische Darstellung ungeeignet"),
    "C.8":   ("DisplayOrDeprecated",
              "C.8: Aendern die Darstellung oder sind abgekuendigt"),
    "C.9":   ("Tagging",           "C.9: Tagging-Zeichen"),
    "D.1":   ("RandALCat",         "D.1: Bidirektionale Kategorie R oder AL"),
    "D.2":   ("LCat",              "D.2: Bidirektionale Kategorie L"),
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
/// Die Tabellen aus RFC 3454 (StringPrep), auf denen SASLprep aufsetzt.
/// </summary>
/// <remarks>
/// <b>Erzeugt von tools/stringprep/generate.py - nicht von Hand aendern.</b>
///
/// Jede Tabelle steht als flache Folge von Bereichsgrenzen da: an gerader
/// Stelle der erste, an ungerader der letzte Codepoint des Bereichs, aufsteigend
/// sortiert. Gesucht wird binaer.
///
/// Die Tabellen sind auf Unicode 3.2 festgeschrieben - das ist keine
/// Nachlaessigkeit, sondern der Sinn der Sache: Wer ein Passwort mit einem
/// spaeter zugewiesenen Zeichen anders behandelt als die Gegenstelle, kommt
/// nicht mehr herein. StringPrep friert den Zeichenvorrat deshalb ein.
/// </remarks>
internal static class StringPrepTables
{

    #region Contains(Table, CodePoint)

    /// <summary>Liegt der Codepoint in einem der Bereiche dieser Tabelle?</summary>
    public static Boolean Contains(UInt32[] Table, UInt32 CodePoint)
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
    with urllib.request.urlopen(RFC_URL) as response:
        return response.read().decode("utf-8").splitlines()


def ranges(lines, name):
    """Die Bereiche einer Tabelle, aufsteigend sortiert und zusammengefasst."""

    start = lines.index(f"   ----- Start Table {name} -----")
    end   = lines.index(f"   ----- End Table {name} -----")

    found = []

    for line in lines[start + 1:end]:

        line = line.strip()

        # Seitenumbrueche des RFC-Textes
        if not line or "Hoffman" in line or line.startswith("RFC 3454"):
            continue

        match = re.match(r"^([0-9A-F]{4,6})(?:-([0-9A-F]{4,6}))?\s*(?:;|$)", line)

        if not match:
            raise SystemExit(f"Tabelle {name}: unverstandene Zeile {line!r}")

        first = int(match.group(1), 16)
        last  = int(match.group(2) or match.group(1), 16)

        found.append((first, last))

    found.sort()

    # Aneinandergrenzende Bereiche zusammenlegen - das macht die Tabelle
    # kuerzer und die binaere Suche nicht falsch.
    merged = []

    for first, last in found:
        if merged and first <= merged[-1][1] + 1:
            merged[-1] = (merged[-1][0], max(merged[-1][1], last))
        else:
            merged.append((first, last))

    return merged


def emit(name, field, comment, values):

    out = [f"    /// <summary>{comment} ({len(values)} Bereiche).</summary>",
           f"    public static readonly UInt32[] {field} = ["]

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

    for name, (field, comment) in TABLES.items():
        body.extend(emit(name, field, comment, ranges(lines, name)))

    target = Path(__file__).resolve().parents[2] / "Jabber" / "Auth" / "StringPrepTables.cs"

    text = HEADER + "\n".join(body) + "}\n"

    target.write_bytes(b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode("utf-8"))

    print(f"{target} geschrieben")


if __name__ == "__main__":
    main()
