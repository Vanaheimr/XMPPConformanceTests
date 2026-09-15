#!/usr/bin/env python3
"""
A XEP-0461 implementation that is not ours.

Why this exists. A reply is client-to-client: Prosody and ejabberd pass the
<reply/> and the <fallback/> through without looking at either, so the
federation lane cannot judge a single thing about them. Everything Ratatoskr
checks about replies, it checks against itself - and two sides that are the same
code agree even where both are wrong. That was the finding of D62 to D65, five
times over.

The contested part is not the reference. It is the number beside it. XEP-0428
points into the body with offsets counted in Unicode code points (XEP-0426), and
.NET counts in UTF-16 units. The two are the same number for every text anybody
writes a test with, and different the moment somebody quotes a message with an
emoji in it. An implementation that confuses them is wrong in a way that no
amount of testing against itself will show, because both halves confuse it the
same way.

Python is the useful opposite here: a str is code points by nature, so len() and
slicing are the XEP-0426 answer without anybody having to decide to make them
so. slixmpp's xep_0461 is built on exactly that - `len(quoted)` into the end
attribute, `body[start:end]` back out - which makes it a genuine second opinion
and not a second copy of ours.

    PYTHONPATH=/tmp/omemo-oracle/lib python3 reply_oracle.py build job.json
"""

import json
import sys
import xml.etree.ElementTree as ET

from slixmpp.stanza import Message
from slixmpp.plugins.xep_0428 import stanza as fallback_stanza
from slixmpp.plugins.xep_0461 import stanza as reply_stanza

CLIENT = "jabber:client"

# Both, and in this order: the reply plugin builds on the fallback one, and
# add_quoted_fallback reaches for a Fallback that has to be registered as a
# child of Message before it can be appended to one.
fallback_stanza.register_plugins()
reply_stanza.register_plugins()


def mode_build(job):
    """
    The stanza slixmpp writes for an answer - with its own quoting convention,
    which is deliberately not made to match ours.

    What has to agree between the two implementations is not the text of the
    quotation but where it is said to end. Forcing the conventions together
    would hide exactly the disagreement worth finding.
    """

    message = Message()
    message["to"]    = job.get("to",   "alice@example.org/home")
    message["from"]  = job.get("from", "bob@example.org/phone")
    message["id"]    = job.get("id",   "answer-1")
    message["type"]  = "chat"
    message["body"]  = job["answer"]

    message["reply"]["id"] = job["reply_to"]

    if job.get("reply_author"):
        message["reply"]["to"] = job["reply_author"]

    if job.get("quoted"):
        message["reply"].add_quoted_fallback(job["quoted"], job.get("author"))

    fallback = next((f for f in message["fallbacks"]
                     if f["for"] == reply_stanza.NS), None)

    return {
        "stanza":    str(message),
        "body":      message["body"],
        "start":     fallback["body"]["start"] if fallback is not None else None,
        "end":       fallback["body"]["end"]   if fallback is not None else None,
        "quoted":    message["reply"].get_fallback_body(),
        "stripped":  message["reply"].strip_fallback_content(),
    }


def mode_read(job):
    """
    What slixmpp makes of a stanza somebody else wrote.

    strip_fallback_content is the interesting line: body[:start] + body[end:],
    over a Python str. If the offsets were counted in UTF-16 units anywhere on
    the writing side, this cuts in the wrong place - and says so by handing back
    a different answer than the one that was sent.
    """

    message = Message(xml=ET.fromstring(job["stanza"]))

    # slixmpp creates an empty plugin on access, so "is there a reply" has to be
    # asked before asking what it says.
    reply = message.get_plugin("reply", check=True)

    if reply is None:
        return {"reply": False}

    fallback = next((f for f in message["fallbacks"]
                     if f["for"] == reply_stanza.NS), None)

    return {
        "reply":      True,
        "reply_id":   reply["id"],
        "reply_to":   str(reply["to"]) if reply["to"] else None,
        "body":       message["body"],
        "start":      fallback["body"]["start"] if fallback is not None else None,
        "end":        fallback["body"]["end"]   if fallback is not None else None,
        "quoted":     reply.get_fallback_body(),
        "stripped":   reply.strip_fallback_content(),
    }


def mode_count(job):
    """
    How long a text is, counted the way XEP-0426 says.

    Nothing but len(), and that is the point: in Python the answer comes out of
    the language rather than out of a decision somebody had to remember to make.
    """

    return {"characters": len(job["text"])}


def mode_probe(job):
    import slixmpp
    return {"slixmpp": slixmpp.__version__}


def main():

    mode = sys.argv[1]

    job = {}
    if len(sys.argv) > 2:
        with open(sys.argv[2], encoding="utf-8") as file:
            job = json.load(file)

    if mode == "build":
        result = mode_build(job)
    elif mode == "read":
        result = mode_read(job)
    elif mode == "count":
        result = mode_count(job)
    elif mode == "probe":
        result = mode_probe(job)
    else:
        raise SystemExit(f"Unknown mode: {mode}")

    print(json.dumps(result))


if __name__ == "__main__":
    main()
