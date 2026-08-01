#!/usr/bin/env python3
"""
Holt die OMEMO-Referenzimplementierung als Wheels und entpackt sie in ein
Verzeichnis - ohne pip, ohne venv, ohne etwas zu installieren.

Wheels sind Zip-Dateien; entpackt in einem Verzeichnis auf dem PYTHONPATH
sind sie importierbar. Das ist hier genau richtig: Wir brauchen die
Bibliothek als Prüfgegenstand, nicht als Bestandteil des Systems - und was
nichts installiert, muss auch niemand wieder aufräumen.
"""

import io
import json
import os
import sys
import urllib.request
import zipfile

ZIEL = sys.argv[1] if len(sys.argv) > 1 else "/tmp/omemo-oracle/lib"

def passt(name):
    """Reine Python-Wheels und native für genau diesen Interpreter."""

    if "py3-none-any" in name:
        return True

    return ("manylinux" in name and "x86_64" in name and
            ("cp313" in name or "abi3" in name))

# cffi gehört dazu, auch wenn es nicht danach aussieht: Ohne es findet xeddsa
# seine native Bibliothek nicht und fällt auf eine Variante zurück, die einen
# Browser erwartet.
PAKETE = ["typing-extensions", "pycparser", "cffi", "cryptography",
          "annotated-types", "typing-inspection", "pydantic-core", "pydantic",
          "XEdDSA", "DoubleRatchet", "X3DH",
          "OMEMO", "twomemo", "oldmemo", "protobuf"]


def pin_von(paket, abhaengigkeit):
    """
    Welche Fassung einer Abhängigkeit ein Paket genau verlangt.

    Nötig, weil pydantic sein pydantic-core auf eine exakte Version festlegt -
    und wer von jedem Paket schlicht das neueste nimmt, bekommt zwei, die
    nicht zueinander passen. Das ist die Arbeit, die pip sonst macht; hier
    reicht der eine Fall.
    """

    with urllib.request.urlopen(f"https://pypi.org/pypi/{paket}/json", timeout=30) as f:
        daten = json.load(f)

    for eintrag in daten["info"]["requires_dist"] or []:
        if eintrag.lower().startswith(abhaengigkeit.lower()) and "==" in eintrag:
            return eintrag.split("==")[1].split(";")[0].strip()

    return None


def wheel_url(paket, version=None):
    with urllib.request.urlopen(f"https://pypi.org/pypi/{paket}/json", timeout=30) as f:
        daten = json.load(f)

    version = version or daten["info"]["version"]

    for datei in daten["releases"][version]:
        name = datei["filename"]
        if name.endswith(".whl") and passt(name):
            return version, datei["url"], name

    return version, None, None


os.makedirs(ZIEL, exist_ok=True)

PINS = {"pydantic-core": pin_von("pydantic", "pydantic-core")}

for paket in PAKETE:
    version, url, name = wheel_url(paket, PINS.get(paket))

    if url is None:
        print(f"  {paket} {version}: kein passendes Wheel")
        continue

    with urllib.request.urlopen(url, timeout=120) as f:
        inhalt = f.read()

    with zipfile.ZipFile(io.BytesIO(inhalt)) as z:
        z.extractall(ZIEL)

    print(f"  {paket} {version}: {name}")
