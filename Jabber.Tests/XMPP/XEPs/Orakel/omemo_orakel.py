#!/usr/bin/env python3
"""
Das Orakel: die Referenzimplementierung von OMEMO 2 als Gegenstelle.

Warum es das gibt
-----------------
Diese Testsammlung kann eine Klasse von Fehlern grundsätzlich nicht finden:
Wenn beide Seiten derselbe Code sind, kommen sie auch dann überein, wenn beide
gleich falsch rechnen. In den Etappen D62 bis D65 war das fünfmal der Befund -
ein Info-String, eine Reihenfolge, eine Einbettung. Jedes Mal hätten sich zwei
Clients dieses Hauses bestens verstanden und kein einziger fremder Client.

Dagegen hilft nur eine Gegenstelle, die niemand hier geschrieben hat. python-
omemo (Syndace) ist die Referenzimplementierung für `urn:xmpp:omemo:2` und
genau das.

Aufruf
------
    omemo_orakel.py bundle                 gibt sein eigenes Bundle aus
    omemo_orakel.py encrypt <auftrag.json> verschluesselt gegen unser Bundle
    omemo_orakel.py decrypt <auftrag.json> entschluesselt, was wir geschickt haben

Ein- und Ausgabe ist JSON auf stdout, Byte-Felder base64.

Was es nicht prueft
-------------------
Die SCE-Huelle (XEP-0420) bleibt aussen vor: python-omemo ueberlaesst sie der
Anwendung, die es benutzt. Geprueft wird also alles von der Nutzlast abwaerts -
Bundle-Format, X3DH, Ratchet, das protobuf-Drahtformat - und das ist genau die
Schicht, in der die fuenf Funde lagen.
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


def b64(daten: bytes) -> str:
    return base64.b64encode(daten).decode("ascii")


def unb64(text: str) -> bytes:
    return base64.b64decode(text)


class SpeicherImArbeitsspeicher(Storage):
    """
    Der kleinste Speicher, der die Bibliothek zufriedenstellt - wahlweise mit
    einer Datei dahinter.

    Ohne Datei ueberlebt er den Prozess nicht, und fuer die eine Richtung ist
    das richtig: Jeder Aufruf ist ein eigener Versuch. Fuer die Gegenrichtung
    braucht es die Datei, denn dort liegen zwei Aufrufe dazwischen - erst gibt
    das Orakel sein Bundle heraus, dann soll es lesen, was wir dagegen
    geschrieben haben. Ohne Gedaechtnis waeren das zwei verschiedene Geraete.
    """

    def __init__(self, pfad: Optional[str] = None) -> None:
        super().__init__()
        self.__pfad = pfad
        self.__inhalt: Dict[str, Any] = {}

        if pfad:
            try:
                with open(pfad, encoding="utf-8") as datei:
                    self.__inhalt = json.load(datei)
            except FileNotFoundError:
                pass

    def __schreiben(self) -> None:
        if self.__pfad:
            with open(self.__pfad, "w", encoding="utf-8") as datei:
                json.dump(self.__inhalt, datei)

    async def _load(self, key: str) -> Maybe[Any]:
        return Just(self.__inhalt[key]) if key in self.__inhalt else Nothing()

    async def _store(self, key: str, value: Any) -> None:
        self.__inhalt[key] = value
        self.__schreiben()

    async def _delete(self, key: str) -> None:
        self.__inhalt.pop(key, None)
        self.__schreiben()


async def unser_bundle(auftrag: Dict[str, Any]) -> BundleImpl:
    """
    Baut aus unserem Bundle das, was die Bibliothek erwartet.

    Hier faellt die erste Pruefung an, noch bevor irgendetwas gerechnet wird:
    Die Bibliothek prueft die Signatur ueber den Signed PreKey selbst. Stimmt
    unsere Kodierung nicht - oder unterschreiben wir etwas anderes, als sie
    erwartet -, scheitert schon dieser Schritt.
    """

    return BundleImpl(
        bare_jid=auftrag["jid"],
        device_id=auftrag["device_id"],
        bundle=x3dh.Bundle(
            identity_key=unb64(auftrag["identity_key"]),
            signed_pre_key=unb64(auftrag["signed_pre_key"]),
            signed_pre_key_sig=unb64(auftrag["signed_pre_key_sig"]),
            pre_keys=frozenset(unb64(p["key"]) for p in auftrag["pre_keys"]),
        ),
        signed_pre_key_id=auftrag["signed_pre_key_id"],
        pre_key_ids={unb64(p["key"]): p["id"] for p in auftrag["pre_keys"]},
    )


async def modus_bundle(auftrag: Dict[str, Any]) -> Dict[str, Any]:
    """Das eigene Bundle - fuer die Gegenrichtung."""

    speicher = SpeicherImArbeitsspeicher(auftrag.get("state"))
    backend = Twomemo(speicher)

    # Ohne PreKeys kaeme keine Sitzung zustande, die einen benutzt - und genau
    # der Weg soll geprueft werden.
    await backend.generate_pre_keys(10)

    bundle = await backend.get_bundle("orakel@example.org", 1)

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


async def modus_encrypt(auftrag: Dict[str, Any]) -> Dict[str, Any]:
    """
    Verschluesselt eine Nachricht gegen unser Bundle.

    Heraus kommt genau das, was in ein <key kex='true'/> gehoerte, samt der
    Nutzlast. Wenn wir das lesen koennen, stimmen Bundle-Format, X3DH,
    Ratchet-Anfang und Drahtformat ueberein.
    """

    speicher = SpeicherImArbeitsspeicher()
    backend = Twomemo(speicher)

    inhalt, schluesselmaterial = await backend.encrypt_plaintext(
        auftrag["plaintext"].encode("utf-8")
    )

    sitzung, verschluesselt = await backend.build_session_active(
        auftrag["jid"],
        auftrag["device_id"],
        await unser_bundle(auftrag),
        schluesselmaterial,
    )

    # Genau die Bytes, die in ein <key kex='true'/> gehoeren: die
    # OMEMOAuthenticatedMessage, eingepackt in eine OMEMOKeyExchange.
    beglaubigt = verschluesselt.serialize()

    return {
        "payload": b64(inhalt.ciphertext),
        "key": b64(sitzung.key_exchange.serialize(beglaubigt)),
        "authenticated_message": b64(beglaubigt),
        "sender_device_id": 1,
        "sender_jid": "orakel@example.org",
    }


async def modus_decrypt(auftrag: Dict[str, Any]) -> Dict[str, Any]:
    """Entschluesselt, was wir gegen sein Bundle geschickt haben."""

    speicher = SpeicherImArbeitsspeicher(auftrag["state"])
    backend = Twomemo(speicher)

    # Aus unserem <key kex='true'/> kommen beide Teile heraus: der
    # Schluesselaustausch und die darin eingepackte OMEMOAuthenticatedMessage.
    # Kann die Bibliothek das trennen, stimmen unsere Feldnummern.
    austausch, beglaubigt = KeyExchangeImpl.parse(unb64(auftrag["key"]))

    schluessel = EncryptedKeyMaterialImpl.parse(
        beglaubigt, auftrag["sender_jid"], auftrag["sender_device_id"]
    )

    session, plain = await backend.build_session_passive(
        auftrag["sender_jid"], auftrag["sender_device_id"], austausch, schluessel
    )

    klartext = await backend.decrypt_plaintext(
        ContentImpl(unb64(auftrag["payload"])), plain
    )

    return {"plaintext": klartext.decode("utf-8")}


async def main() -> None:
    modus = sys.argv[1]

    auftrag: Dict[str, Any] = {}
    if len(sys.argv) > 2:
        with open(sys.argv[2], encoding="utf-8") as datei:
            auftrag = json.load(datei)

    if modus == "bundle":
        ergebnis = await modus_bundle(auftrag)
    elif modus == "encrypt":
        ergebnis = await modus_encrypt(auftrag)
    elif modus == "decrypt":
        ergebnis = await modus_decrypt(auftrag)
    else:
        raise SystemExit(f"Unbekannter Modus: {modus}")

    print(json.dumps(ergebnis))


if __name__ == "__main__":
    asyncio.run(main())
