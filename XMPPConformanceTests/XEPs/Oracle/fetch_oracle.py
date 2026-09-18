#!/usr/bin/env python3
"""
Fetches the reference implementations as wheels and unpacks them into a
directory - without pip, without a venv, without installing anything.

Wheels are zip files; unpacked into a directory on the PYTHONPATH they are
importable. That is exactly right here: we need the library as an object of
comparison, not as part of the system - and what installs nothing, nobody has
to clean up afterwards.

The versions are pinned, which they were not until D138. An oracle is the
thing this project measures itself against, so it has to be the same oracle
from one run to the next: taking the newest of everything means a release
somebody else made overnight can turn the nightly red, and then the first
question of the morning is whose change it was. That question should never
have to be asked.

`--latest` takes the newest of everything instead. That is how the pins get
checked and how the next upgrade gets found - deliberately, by somebody
watching, and not at twenty past three in the morning.
"""

import io
import json
import os
import sys
import urllib.request
import zipfile

LATEST = "--latest" in sys.argv
ARGS   = [argument for argument in sys.argv[1:] if not argument.startswith("--")]
TARGET = ARGS[0] if ARGS else "/tmp/omemo-oracle/lib"

def fits(name):
    """Pure Python wheels, and native ones for exactly this interpreter."""

    if "py3-none-any" in name:
        return True

    return ("manylinux" in name and "x86_64" in name and
            ("cp313" in name or "abi3" in name))

# cffi belongs in here even though it does not look like it: without it xeddsa
# does not find its native library and falls back to a variant that expects a
# browser.
#
# slixmpp and the three below it are the XEP-0461 side and have nothing to do
# with OMEMO. They live in the same directory because there is no reason for a
# second one: both oracles are read-only libraries on a PYTHONPATH, and one
# setup command is one thing to forget rather than two. aiodns and pycares are
# not used by anything here - slixmpp imports them at module level for its own
# resolver, and a library that will not import is not an oracle.
#
# These are the versions the nightly of 2026-09-18 measured 137 of 137 against,
# read out of that run rather than out of anybody's memory. Raising one is a
# change like any other: run with --latest, see what moves, and write the new
# number down here in the same commit as whatever had to move with it.
PINS = {
    "typing-extensions":  "4.16.0",
    "pycparser":          "3.0",
    "cffi":               "2.1.1",
    "cryptography":       "50.0.1",
    "annotated-types":    "0.8.0",
    "typing-inspection":  "0.4.4",
    "pydantic-core":      "2.46.5",
    "pydantic":           "2.13.5",
    "XEdDSA":             "1.2.0",
    "DoubleRatchet":      "1.3.0",
    "X3DH":               "1.3.0",
    "OMEMO":              "2.1.0",
    "twomemo":            "2.1.0",
    "oldmemo":            "2.1.0",
    "protobuf":           "7.36.2",
    "slixmpp":            "1.17.0",
    "pyasn1":             "0.6.4",
    "pyasn1-modules":     "0.4.2",
    "pycares":            "5.0.1",
    "aiodns":             "4.0.4",
}

# The order is the order they are fetched in, and the dict keeps it.
PACKAGES = list(PINS)


def pin_of(package, dependency):
    """
    Which version of a dependency a package demands exactly.

    Needed only under --latest: pydantic pins its pydantic-core to an exact
    version, and whoever simply takes the newest of every package gets two that
    do not fit together. That is the work pip otherwise does; here the one case
    suffices.
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

    # .get rather than [], so a pin nobody ever released reads as "no matching
    # wheel" below instead of a traceback that names no package.
    for file in data["releases"].get(version, []):
        name = file["filename"]
        if name.endswith(".whl") and fits(name):
            return version, file["url"], name

    return version, None, None


os.makedirs(TARGET, exist_ok=True)

if LATEST:
    print("  --latest: the pins are ignored. What this fetches is NOT what the")
    print("  recorded measurements were taken against.")
    wanted = {"pydantic-core": pin_of("pydantic", "pydantic-core")}
else:
    wanted = PINS

missing = []

for package in PACKAGES:
    version, url, name = wheel_url(package, wanted.get(package))

    if url is None:
        print(f"  {package} {version}: no matching wheel")
        missing.append(f"{package} {version}")
        continue

    with urllib.request.urlopen(url, timeout=120) as f:
        content = f.read()

    with zipfile.ZipFile(io.BytesIO(content)) as z:
        z.extractall(TARGET)

    print(f"  {package} {version}: {name}")

# Until D138 this only printed and carried on. A half-fetched oracle imports
# and then fails somewhere far from here, and it fails in a way that reads as a
# fault in the thing being measured rather than in the measuring - which is the
# one confusion an oracle exists to prevent.
if missing:
    print()
    print(f"  The oracle is incomplete: {', '.join(missing)}.")
    print("  Nothing measured against it would mean anything, so this stops here.")
    sys.exit(1)
