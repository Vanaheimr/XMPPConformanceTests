#!/usr/bin/env python3
"""
Fetches the OMEMO reference implementation as wheels and unpacks it into a
directory - without pip, without a venv, without installing anything.

Wheels are zip files; unpacked into a directory on the PYTHONPATH they are
importable. That is exactly right here: we need the library as an object of
comparison, not as part of the system - and what installs nothing, nobody has
to clean up afterwards.
"""

import io
import json
import os
import sys
import urllib.request
import zipfile

TARGET = sys.argv[1] if len(sys.argv) > 1 else "/tmp/omemo-oracle/lib"

def fits(name):
    """Pure Python wheels, and native ones for exactly this interpreter."""

    if "py3-none-any" in name:
        return True

    return ("manylinux" in name and "x86_64" in name and
            ("cp313" in name or "abi3" in name))

# cffi belongs in here even though it does not look like it: without it xeddsa
# does not find its native library and falls back to a variant that expects a
# browser.
PACKAGES = ["typing-extensions", "pycparser", "cffi", "cryptography",
            "annotated-types", "typing-inspection", "pydantic-core", "pydantic",
            "XEdDSA", "DoubleRatchet", "X3DH",
            "OMEMO", "twomemo", "oldmemo", "protobuf"]


def pin_of(package, dependency):
    """
    Which version of a dependency a package demands exactly.

    Needed because pydantic pins its pydantic-core to an exact version - and
    whoever simply takes the newest of every package gets two that do not fit
    together. That is the work pip otherwise does; here the one case suffices.
    """

    with urllib.request.urlopen(f"https://pypi.org/pypi/{package}/json", timeout=30) as f:
        data = json.load(f)

    for entry in data["info"]["requires_dist"] or []:
        if entry.lower().startswith(dependency.lower()) and "==" in entry:
            return entry.split("==")[1].split(";")[0].strip()

    return None


def wheel_url(package, version=None):
    with urllib.request.urlopen(f"https://pypi.org/pypi/{package}/json", timeout=30) as f:
        data = json.load(f)

    version = version or data["info"]["version"]

    for file in data["releases"][version]:
        name = file["filename"]
        if name.endswith(".whl") and fits(name):
            return version, file["url"], name

    return version, None, None


os.makedirs(TARGET, exist_ok=True)

PINS = {"pydantic-core": pin_of("pydantic", "pydantic-core")}

for package in PACKAGES:
    version, url, name = wheel_url(package, PINS.get(package))

    if url is None:
        print(f"  {package} {version}: no matching wheel")
        continue

    with urllib.request.urlopen(url, timeout=120) as f:
        content = f.read()

    with zipfile.ZipFile(io.BytesIO(content)) as z:
        z.extractall(TARGET)

    print(f"  {package} {version}: {name}")
