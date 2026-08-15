#!/usr/bin/env python3
"""
An OMEMO Media Sharing (XEP-0454) encryptor that is not ours.

Why this exists at all: AesGcmUrl decides something the specifications leave
contested - how long the IV in the fragment is. It takes 12 bytes and refuses
16, with a comment for a reason. A comment is not evidence. This produces the
material a foreign implementation actually emits, in both readings, so the
decision can be checked against something rather than against itself.

The implementation is `cryptography` (pyca), which the OMEMO oracle already
pulls in - see fetch_oracle.py, where it stands among the packages because
xeddsa needs it. Nothing extra is fetched for this.

What it is NOT: a second opinion on AES-GCM itself. Both sides end up at
OpenSSL sooner or later. What it independently produces is the *layout* - which
bytes go into the fragment, in what order, and how ciphertext and tag are
joined - and that is where implementations disagree.

Called like the OMEMO oracle:

    PYTHONPATH=/tmp/omemo-oracle/lib python3 aesgcm_oracle.py encrypt job.json
"""

import json
import os
import sys

from cryptography.hazmat.primitives.ciphers.aead import AESGCM


def mode_encrypt(job):
    """Encrypt a payload and hand back the URL a sender would put in a body."""

    plaintext     = job["plaintext"].encode("utf-8")
    nonce_length  = job.get("nonce_length", 12)
    key_length    = job.get("key_length", 32)
    host          = job.get("host", "files.example.org")
    path          = job.get("path", "/upload/e2ee.bin")

    key    = os.urandom(key_length)
    nonce  = os.urandom(nonce_length)

    # AESGCM.encrypt returns ciphertext || tag, which is the layout XEP-0454
    # prescribes for the stored file. Nothing is rearranged here on purpose:
    # if the two sides disagree about it, the test has to see that.
    payload = AESGCM(key).encrypt(nonce, plaintext, None)

    # The fragment is hex(IV || key). Both cases are handed back, because the
    # XEP says hex and says nothing about which case, and a reader that only
    # manages one of them is wrong in a way nobody notices until it meets the
    # other.
    fragment = (nonce + key).hex()

    return {
        "url":        f"aesgcm://{host}{path}#{fragment}",
        "url_upper":  f"aesgcm://{host}{path}#{fragment.upper()}",
        "payload":    payload.hex(),
        "nonce":      nonce.hex(),
        "key":        key.hex(),
        "tag_length": 16,
    }


def mode_probe(_job):
    """Is the far side there at all? Answered before anything is measured."""

    key   = bytes(32)
    nonce = bytes(12)

    return {"ok": AESGCM(key).encrypt(nonce, b"x", None).hex()}


def main():

    mode = sys.argv[1]

    job = {}
    if len(sys.argv) > 2:
        with open(sys.argv[2], encoding="utf-8") as file:
            job = json.load(file)

    if mode == "encrypt":
        result = mode_encrypt(job)
    elif mode == "probe":
        result = mode_probe(job)
    else:
        raise SystemExit(f"Unknown mode: {mode}")

    print(json.dumps(result))


if __name__ == "__main__":
    main()
