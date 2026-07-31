#!/usr/bin/env python3
"""Gemeinsames Handwerkszeug fuer die Generatoren aus dem Unicode Character Database.

Die Dateien der UCD haben alle dieselbe Form:

    0370..0373    ; Greek # L&  [4] GREEK CAPITAL LETTER HETA..

also ein Bereich, ein Wert, ein Kommentar. Was sich unterscheidet, ist nur, aus
welcher Datei gelesen und welcher Wert gesucht wird - deshalb steht das Lesen
hier und nicht zweimal daneben.
"""

import re
import urllib.request
from pathlib import Path

LICENSE_HEADER = """/*
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
"""

LINE = re.compile(r"^([0-9A-F]{4,6})(?:\.\.([0-9A-F]{4,6}))?\s*;\s*([^#]+?)\s*$")


def load(url, source=None):
    """Die Zeilen einer UCD-Datei - aus dem Netz oder aus einer oertlichen Kopie."""

    if source:
        return Path(source).read_text(encoding="utf-8").splitlines()

    with urllib.request.urlopen(url) as response:
        return response.read().decode("utf-8").splitlines()


def ranges(lines, value):
    """Die Bereiche mit diesem Wert, aufsteigend sortiert und zusammengefasst.

    Zeilen mit '@missing' beschreiben Vorgabewerte fuer nicht zugewiesene
    Codepoints und bleiben aussen vor: Was nicht zugewiesen ist, kommt in einem
    JID ohnehin nicht vor - die Leitern aus RFC 8264 und RFC 5892 weisen es
    vorher ab.
    """

    found = []

    for line in lines:

        line = line.split("#", 1)[0].strip()

        if not line or line.startswith("@"):
            continue

        match = LINE.match(line)

        if not match:
            raise SystemExit(f"unverstandene Zeile {line!r}")

        if match.group(3) != value:
            continue

        first = int(match.group(1), 16)
        last  = int(match.group(2) or match.group(1), 16)

        found.append((first, last))

    if not found:
        raise SystemExit(f"Wert {value!r}: kein einziger Bereich gefunden")

    found.sort()

    merged = []

    for first, last in found:
        if merged and first <= merged[-1][1] + 1:
            merged[-1] = (merged[-1][0], max(merged[-1][1], last))
        else:
            merged.append((first, last))

    return merged


def emit(field, comment, values, visibility="internal"):
    """Eine Tabelle als flache Folge von Bereichsgrenzen."""

    out = [f"    /// <summary>{comment} ({len(values)} Bereiche).</summary>",
           f"    {visibility} static readonly UInt32[] {field} = ["]

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


def write(target, text):
    """Die Datei schreiben - mit BOM und CRLF, wie der Rest des Projekts."""

    target.write_bytes(b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode("utf-8"))

    print(f"{target} geschrieben")
