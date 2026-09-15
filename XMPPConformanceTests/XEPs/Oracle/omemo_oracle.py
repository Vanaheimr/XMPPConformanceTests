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
    omemo_oracle.py bundle               prints its own bundle
    omemo_oracle.py encrypt <job.json>   encrypts against our bundle
    omemo_oracle.py empty <job.json>     the same, with no content at all - the
                                         key transport a client sends to heal a
                                         session
    omemo_oracle.py decrypt <job.json>   decrypts the FIRST message of a session
    omemo_oracle.py continue <job.json>  decrypts a LATER one, in a session
                                         that already exists

`encrypt` and `empty` take an optional "state", and then the session they open
stays. Without it every call is a device of its own, which is right for a single
question and wrong for every one that has a second half.

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

    # The device id comes from the job, because a fan-out test needs the oracle
    # to be several devices. Each one gets its own "state", which is what
    # actually makes them distinct - the id is only what the bundle is labelled
    # with, and two devices sharing a state would share an identity key too.
    bundle = await backend.get_bundle(
        job.get("jid", "oracle@example.org"),
        job.get("device_id", 1),
    )

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


async def open_session_to_us(job: Dict[str, Any], content, key_material) -> Dict[str, Any]:
    """
    The half `encrypt` and `empty` have in common: open a session against our
    bundle and hand out what belongs inside a <key kex='true'/>.

    The storage takes "state" when there is one. Without it every call is a
    device of its own - right for a single question, and wrong for every one
    with a second half, because the follow-up would arrive at a stranger.
    """

    storage = InMemoryStorage(job.get("state"))
    backend = Twomemo(storage)

    session, encrypted = await backend.build_session_active(
        job["jid"],
        job["device_id"],
        await our_bundle(job),
        key_material,
    )

    # Only worth anything with a state behind it, and harmless without.
    await backend.store_session(session)

    # Exactly the bytes that belong inside a <key kex='true'/>: the
    # OMEMOAuthenticatedMessage, wrapped in an OMEMOKeyExchange.
    authenticated = encrypted.serialize()

    return {
        "payload": b64(content.ciphertext),
        "key": b64(session.key_exchange.serialize(authenticated)),
        "authenticated_message": b64(authenticated),
        "sender_device_id": 1,
        "sender_jid": "oracle@example.org",
        "empty": content.empty,
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

    return await open_session_to_us(job, content, key_material)


async def mode_empty(job: Dict[str, Any]) -> Dict[str, Any]:
    """
    The same, with no content at all.

    This is what a client sends when it wants a session and has nothing to say -
    after a restart, after a device appears, whenever a ratchet has to be
    brought forward. The specification calls it a key transport element, and it
    is not an edge case: it is how most sessions in a real conversation come
    about, because the first thing between two clients is usually a repair and
    not a sentence.

    The ciphertext is empty and `content.empty` is True, which is reported so
    that the other side can be held to leaving the <payload/> away rather than
    sending an empty one.
    """

    content, key_material = await Twomemo(InMemoryStorage()).encrypt_empty()

    return await open_session_to_us(job, content, key_material)


async def mode_decrypt(job: Dict[str, Any]) -> Dict[str, Any]:
    """
    Decrypts what we sent against its bundle - the FIRST message of a session.

    Two things happen here beyond decrypting, and both are what a real
    responder owes:

    The session is stored. Without that it is decrypted and forgotten, and
    `continue` below would have nothing to go on.

    The one-time prekey this session consumed is hidden. A responder that
    leaves it usable has not consumed it at all: the same message builds a
    second session just as well, and the forward secrecy X3DH promises for that
    first message is gone. `hide_pre_key` returns False when the key is already
    hidden or deleted - which is exactly what a replay looks like from in here,
    and is reported rather than raised, because whether a replay is refused is
    the question and not the answer.
    """

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

    consumed = await backend.hide_pre_key(session)
    visible = await backend.get_num_visible_pre_keys()

    await backend.store_session(session)

    return {
        "plaintext": plaintext.decode("utf-8"),
        "pre_key_was_consumed": consumed,
        "visible_pre_keys": visible,
    }


async def mode_continue(job: Dict[str, Any]) -> Dict[str, Any]:
    """
    Decrypts a message in a session that already exists.

    The difference to `decrypt` is the entire point of this mode. Every check
    against this oracle until now was the first message of a session, and the
    first message is the one place where the Double Ratchet has not yet done
    anything a plain key agreement would not also have done. From the second
    onwards the chain key has to have stepped on the same way on both sides,
    and the counters in the header have to say so.

    No key exchange comes in here: what arrives is a bare
    OMEMOAuthenticatedMessage, which is what a client sends once the session
    stands.
    """

    storage = InMemoryStorage(job["state"])
    backend = Twomemo(storage)

    session = await backend.load_session(job["sender_jid"], job["sender_device_id"])

    if session is None:
        raise SystemExit(
            "No session with that device - `decrypt` has to have run against "
            "this state first."
        )

    key = EncryptedKeyMaterialImpl.parse(
        unb64(job["key"]), job["sender_jid"], job["sender_device_id"]
    )

    plain = await backend.decrypt_key_material(session, key)

    plaintext = await backend.decrypt_plaintext(
        ContentImpl(unb64(job["payload"])), plain
    )

    await backend.store_session(session)

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
    elif mode == "empty":
        result = await mode_empty(job)
    elif mode == "decrypt":
        result = await mode_decrypt(job)
    elif mode == "continue":
        result = await mode_continue(job)
    else:
        raise SystemExit(f"Unknown mode: {mode}")

    print(json.dumps(result))


if __name__ == "__main__":
    asyncio.run(main())
