#!/usr/bin/env python3
"""
The oracle: the reference implementation of OMEMO 2 as a peer.

Why this exists
---------------
This test suite fundamentally cannot find one class of fault: when both sides
are the same code, they agree even when both compute the same wrong thing. In
stages D62 to D65 that was the finding five times over - an info string, an
ordering, an embedding. Every time, two clients of this house would have
understood each other perfectly and not a single foreign client.

The only remedy is a peer that nobody here wrote. python-omemo (Syndace) is the
reference implementation for `urn:xmpp:omemo:2` and exactly that.

Usage
-----
    omemo_oracle.py bundle              prints its own bundle
    omemo_oracle.py encrypt <job.json>  encrypts against our bundle
    omemo_oracle.py decrypt <job.json>  decrypts what we sent

Input and output are JSON on stdout, byte fields base64.

What it does not check
----------------------
The SCE envelope (XEP-0420) stays out of it: python-omemo leaves it to the
application that uses it. What is checked is therefore everything from the
payload downwards - bundle format, X3DH, ratchet, the protobuf wire format -
and that is exactly the layer the five findings were in.
"""

import asyncio
import base64
import json
import sys
from typing import Any, Dict, Optional

import doubleratchet
import x3dh
import xeddsa
from twomemo.twomemo import (
    BundleImpl,
    ContentImpl,
    EncryptedKeyMaterialImpl,
    KeyExchangeImpl,
    PlainKeyMaterialImpl,
    Twomemo,
)
from omemo import Storage, Just, Nothing, Maybe


def b64(data: bytes) -> str:
    return base64.b64encode(data).decode("ascii")


def unb64(text: str) -> bytes:
    return base64.b64decode(text)


class InMemoryStorage(Storage):
    """
    The smallest store that satisfies the library - optionally with a file
    behind it.

    Without a file it does not survive the process, and for the one direction
    that is right: every call is an attempt of its own. The other direction
    needs the file, because two calls lie between - first the oracle hands out
    its bundle, then it is meant to read what we wrote against it. Without a
    memory those would be two different devices.
    """

    def __init__(self, path: Optional[str] = None) -> None:
        super().__init__()
        self.__path = path
        self.__content: Dict[str, Any] = {}

        if path:
            try:
                with open(path, encoding="utf-8") as file:
                    self.__content = json.load(file)
            except FileNotFoundError:
                pass

    def __write(self) -> None:
        if self.__path:
            with open(self.__path, "w", encoding="utf-8") as file:
                json.dump(self.__content, file)

    async def _load(self, key: str) -> Maybe[Any]:
        return Just(self.__content[key]) if key in self.__content else Nothing()

    async def _store(self, key: str, value: Any) -> None:
        self.__content[key] = value
        self.__write()

    async def _delete(self, key: str) -> None:
        self.__content.pop(key, None)
        self.__write()


async def our_bundle(job: Dict[str, Any]) -> BundleImpl:
    """
    Builds what the library expects out of our bundle.

    The first check happens here, before anything at all is computed: the
    library verifies the signature over the signed prekey itself. If our
    encoding is wrong - or if we sign something other than what it expects -
    this step already fails.
    """

    return BundleImpl(
        bare_jid=job["jid"],
        device_id=job["device_id"],
        bundle=x3dh.Bundle(
            identity_key=unb64(job["identity_key"]),
            signed_pre_key=unb64(job["signed_pre_key"]),
            signed_pre_key_sig=unb64(job["signed_pre_key_sig"]),
            pre_keys=frozenset(unb64(p["key"]) for p in job["pre_keys"]),
        ),
        signed_pre_key_id=job["signed_pre_key_id"],
        pre_key_ids={unb64(p["key"]): p["id"] for p in job["pre_keys"]},
    )


async def mode_bundle(job: Dict[str, Any]) -> Dict[str, Any]:
    """Its own bundle - for the other direction."""

    storage = InMemoryStorage(job.get("state"))
    backend = Twomemo(storage)

    # Without prekeys no session that uses one would come about - and that is
    # exactly the path to be checked.
    await backend.generate_pre_keys(10)

    bundle = await backend.get_bundle("oracle@example.org", 1)

    return {
        "jid": bundle.bare_jid,
        "device_id": bundle.device_id,
        "identity_key": b64(bundle.identity_key),
        "signed_pre_key_id": bundle.signed_pre_key_id,
        "signed_pre_key": b64(bundle.bundle.signed_pre_key),
        "signed_pre_key_sig": b64(bundle.bundle.signed_pre_key_sig),
        "pre_keys": [
            {"id": bundle.pre_key_ids[k], "key": b64(k)}
            for k in bundle.bundle.pre_keys
        ],
    }


async def mode_encrypt(job: Dict[str, Any]) -> Dict[str, Any]:
    """
    Encrypts a message against our bundle.

    What comes out is exactly what would belong inside a <key kex='true'/>,
    along with the payload. If we can read that, then bundle format, X3DH,
    ratchet start and wire format agree.
    """

    storage = InMemoryStorage()
    backend = Twomemo(storage)

    content, key_material = await backend.encrypt_plaintext(
        job["plaintext"].encode("utf-8")
    )

    session, encrypted = await backend.build_session_active(
        job["jid"],
        job["device_id"],
        await our_bundle(job),
        key_material,
    )

    # Exactly the bytes that belong inside a <key kex='true'/>: the
    # OMEMOAuthenticatedMessage, wrapped in an OMEMOKeyExchange.
    authenticated = encrypted.serialize()

    return {
        "payload": b64(content.ciphertext),
        "key": b64(session.key_exchange.serialize(authenticated)),
        "authenticated_message": b64(authenticated),
        "sender_device_id": 1,
        "sender_jid": "oracle@example.org",
    }


async def mode_decrypt(job: Dict[str, Any]) -> Dict[str, Any]:
    """Decrypts what we sent against its bundle."""

    storage = InMemoryStorage(job["state"])
    backend = Twomemo(storage)

    # Both parts come out of our <key kex='true'/>: the key exchange and the
    # OMEMOAuthenticatedMessage wrapped inside it. If the library can separate
    # them, our field numbers are right.
    exchange, authenticated = KeyExchangeImpl.parse(unb64(job["key"]))

    key = EncryptedKeyMaterialImpl.parse(
        authenticated, job["sender_jid"], job["sender_device_id"]
    )

    session, plain = await backend.build_session_passive(
        job["sender_jid"], job["sender_device_id"], exchange, key
    )

    plaintext = await backend.decrypt_plaintext(
        ContentImpl(unb64(job["payload"])), plain
    )

    return {"plaintext": plaintext.decode("utf-8")}


async def main() -> None:
    mode = sys.argv[1]

    job: Dict[str, Any] = {}
    if len(sys.argv) > 2:
        with open(sys.argv[2], encoding="utf-8") as file:
            job = json.load(file)

    if mode == "bundle":
        result = await mode_bundle(job)
    elif mode == "encrypt":
        result = await mode_encrypt(job)
    elif mode == "decrypt":
        result = await mode_decrypt(job)
    else:
        raise SystemExit(f"Unknown mode: {mode}")

    print(json.dumps(result))


if __name__ == "__main__":
    asyncio.run(main())
