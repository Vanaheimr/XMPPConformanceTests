#!/usr/bin/env python3
"""Erzeugt Jabber/Common/ContextTables.cs fuer die Regeln aus RFC 5892, Anhang A.

Die sieben Regeln A.1 bis A.7 fragen nach drei Eigenschaften, die .NET nicht
ausliefert:

    Canonical_Combining_Class == 9 (Virama)   fuer A.1 und A.2
    Joining_Type                              fuer A.1
    Script                                    fuer A.4 bis A.7

Sie sind nicht ableitbar - ob ein Buchstabe zur griechischen oder zur
hebraeischen Schrift gehoert, steht in keiner Kategorie. Also derselbe Weg wie
bei den Bidi-Klassen: aus der Unicode-Datenbank holen, nicht raten.

    python3 tools/unicode/generate-contexttables.py

Ohne Argumente werden die Dateien von unicode.org geholt.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import ucd

VERSION = "15.1.0"
BASE    = f"https://www.unicode.org/Public/{VERSION}/ucd"

CCC_URL      = f"{BASE}/extracted/DerivedCombiningClass.txt"
JOINING_URL  = f"{BASE}/extracted/DerivedJoiningType.txt"
SCRIPTS_URL  = f"{BASE}/Scripts.txt"

HEADER = f"""
namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Die Unicode-Eigenschaften, auf denen die kontextabhaengigen Regeln aus
/// RFC 5892, Anhang A stehen (Unicode {VERSION}).
/// </summary>
/// <remarks>
/// <b>Erzeugt von tools/unicode/generate-contexttables.py - nicht von Hand
/// aendern.</b>
///
/// Aufgeschrieben ist nur, was die sieben Regeln brauchen: die Virama-Zeichen
/// (kombinierende Klasse 9), die vier Joining_Type-Werte, die in A.1 vorkommen,
/// und fuenf Schriften. Eine vollstaendige Script-Tabelle waere um ein
/// Vielfaches groesser und wuerde nichts beantworten, was hier gefragt wird.
/// </remarks>
internal static class ContextTables
{{

    #region Contains(Table, CodePoint)

    /// <summary>Liegt der Codepoint in einem der Bereiche dieser Tabelle?</summary>
    internal static Boolean Contains(UInt32[] Table, UInt32 CodePoint)
    {{

        var low   = 0;
        var high  = Table.Length / 2 - 1;

        while (low <= high)
        {{

            var mid = (low + high) / 2;

            if      (CodePoint < Table[mid * 2])      high  = mid - 1;
            else if (CodePoint > Table[mid * 2 + 1])  low   = mid + 1;
            else                                      return true;

        }}

        return false;

    }}

    #endregion

"""


def main():

    body = []

    ccc = ucd.load(CCC_URL)
    body.extend(ucd.emit("Virama", "Kombinierende Klasse 9 - die Virama-Zeichen",
                         ucd.ranges(ccc, "9")))

    joining = ucd.load(JOINING_URL)

    for value, comment in (("L", "Joining_Type L (nach links verbindend)"),
                           ("D", "Joining_Type D (nach beiden Seiten verbindend)"),
                           ("R", "Joining_Type R (nach rechts verbindend)"),
                           ("T", "Joining_Type T (durchsichtig - Marken und Formatzeichen)")):
        body.extend(ucd.emit(f"Joining{value}", comment, ucd.ranges(joining, value)))

    scripts = ucd.load(SCRIPTS_URL)

    for value, comment in (("Greek",    "Schrift Griechisch (A.4)"),
                           ("Hebrew",   "Schrift Hebraeisch (A.5 und A.6)"),
                           ("Hiragana", "Schrift Hiragana (A.7)"),
                           ("Katakana", "Schrift Katakana (A.7)"),
                           ("Han",      "Schrift Han (A.7)")):
        body.extend(ucd.emit(f"Script{value}", comment, ucd.ranges(scripts, value)))

    target = Path(__file__).resolve().parents[2] / "Jabber" / "Common" / "ContextTables.cs"

    ucd.write(target, ucd.LICENSE_HEADER + HEADER + "\n".join(body) + "}\n")


if __name__ == "__main__":
    main()
