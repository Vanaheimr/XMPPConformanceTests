# The oracle — OMEMO against the reference implementation

This test suite **fundamentally cannot find** one class of fault: when both
sides are the same code, they agree even when both compute the same wrong
thing. In stages D62 to D65 that was the finding five times over — an info
string, an ordering, an embedding. Every time, two clients of this house would
have understood each other perfectly and not a single foreign one.

The only remedy is a peer that nobody here wrote:
[python-omemo](https://github.com/Syndace/python-omemo) by Syndace, the
reference implementation for `urn:xmpp:omemo:2` — the same version we speak.

## Setting it up

```bash
wsl -d Debian -- python3 XMPPConformanceTests/XEPs/Oracle/fetch_oracle.py /tmp/omemo-oracle/lib
```

That downloads the wheels and unpacks them — **no pip, no venv, no sudo,
nothing changed on the system**. Wheels are zip files; unpacked onto the
`PYTHONPATH` they are importable. For a test setup that is even better than an
installation: reproducible, and nothing is left behind.

Two stumbling blocks are already solved inside it:

- **`cffi` belongs in there**, even though it does not look like it — without
  it XEdDSA does not find its native library and falls back to a variant that
  expects a browser.
- **pydantic pins `pydantic-core` to an exact version.** Whoever simply takes
  the newest of every package gets two that do not fit together. That is the
  work pip otherwise does; for this one case ten lines are enough.

If the directory is not there, **the tests skip themselves** — as do the ones
against Prosody and ejabberd. A run without WSL should not be red, it should
just say less.

If the *script* is not there, the run is **red**. The oracle lives in this
project, so a missing one is a broken checkout and not a property of the
environment.

## What is checked

In both directions, and that is the point:

| | |
|---|---|
| **It accepts our bundle** | checking our signature over the signed prekey with its own notion of what that signature covers |
| **We read what it writes** | bundle encoding, order of the four Diffie-Hellmans, X3DH info string, the `0xFF` prefix, the contribution from both identity keys, ratchet start, info strings of the root chain and of the message key, the constants `0x01`/`0x02`, protobuf field numbers, embedding of the ciphertext, truncation of the HMAC, derivation of the payload |
| **It reads what we write** | the same from the other side — and the split of our `<key kex='true'/>` into key exchange and wrapped message |

**Every single one of these points was a surviving mutant or a reading find in
D62 to D65.** This setup would have caught them all.

## What is not checked

- **The SCE envelope (XEP-0420).** python-omemo leaves it to the application
  that uses it — an envelope built inside the oracle here would not be a
  foreign check but the same assumption twice.
- **The `<encrypted/>` element and the PEP nodes.** Both sit above the layer
  the library offers.
- **A conversation over several messages.** What is checked is the beginning of
  a session, not its course.
- **A real client over a real connection.** Conversations, Dino and Gajim still
  largely speak OMEMO 0.3.0 (`eu.siacs.conversations.axolotl`); checking
  against those would mean building a second version first.
