# XMPP Console Client (.NET 10)

An XMPP client for the command line with WebSocket transport (RFC 7395) and
SCRAM authentication.

> **Maturity:** experimental. The client connects, authenticates and chats
> against Prosody 13 over `wss://` — shown, not claimed: until recently the
> same stood here about ejabberd, and in fact the client could not have
> logged in to *any* RFC 7395 conformant server, because its stanzas went out
> without a namespace. Connection management and error handling are
> incomplete (see [Known limitations](#known-limitations)). Not for
> production use.

The protocol itself — client, server, XEPs, federation, OMEMO — has lived
since D97 in **[Ratatoskr](../libs/Ratatoskr/README.md)**, a library of its own
under `libs/`. This document goes on describing both together, because both
came about together and the decisions behind them stand in the
[work plan](../WORKPLAN.md); the README of Ratatoskr is the extract for
whoever uses the library without this console. **Here** stand in addition the
things that do not exist there: the console itself and the setup of the
interoperability checks against Prosody, ejabberd and python-omemo.

## Authentication

| Method | Status |
|---------|--------|
| SCRAM-SHA-256 | ✅ Preferred |
| SCRAM-SHA-1 | ✅ Fallback |
| SASL PLAIN | ⚠️ Last fallback |
| SCRAM-*-PLUS (channel binding) | ❌ Not implemented |

What is chosen is the strongest mechanism on offer — by the ranking, not by
the order of the announcement. Against the downgrade stand two lower bounds,
both on `XMPPConnection`:

| Property | Effect |
|---|---|
| `PinnedSaslMechanism` | What the last login succeeded with. Works by itself, but only from the second connection on. |
| `MinimumSaslMechanism` | What the caller demands. Works from the first frame on, but has to be set. |

Both are checked *before* the `<auth/>` goes out — with PLAIN the password
would stand in exactly that frame. If the server offers less than one of the
lower bounds demands, no connection comes about and no reconnect is tried.

The pinning is a trust-on-first-use: if the man in the middle is already there
at the very first connection, it pins his downgrade instead of fending it off.
Whoever knows what their server can do therefore also sets
`MinimumSaslMechanism`. What it fends off without any help is the attack that
pays: the client comes back by itself after every break, and a break can be
forced.

## XEP support

Legend: ✅ working · ⚠️ implemented with known gaps · 🚧 present, but off by default · ⛔ deliberately not implemented

| XEP | Name | Status | Note |
|-----|------|--------|-----------|
| XEP-0013 | Flexible Offline Message Retrieval | ⛔ | Listed by the XSF as *deprecated* (version 1.3, 2021-05-04): "Implementation of the protocol described herein is not recommended." The offline store stays with the automatic handing in under RFC 6121 §8.5.2.2.1 and XEP-0160 — see [WORKPLAN.md](../WORKPLAN.md), D37 |
| XEP-0030 | Service Discovery | ✅ | disco#info and disco#items, asked and answered. The `node` of the request is mirrored under §3.2; answered are only nodes that name this entity — the caps node with and without the current `#ver` (XEP-0115 §6.2). Every other one, an outdated `ver` included, gets `<item-not-found/>` back together with the request. disco#items answers from `DiscoManager.LocalItems` (empty by default: a client has no sub-units); a `node` is a branch in the tree there and is refused. The test server keeps no nodes and refuses every one |
| XEP-0060 | Publish-Subscribe | ⚠️ | Incoming events are parsed, checked against spoofing and carry their `SubID` from the SHIM header. Outgoing, every request is correlated with its answer: a subscription holds only after the confirmation of the service, `pending` does not count as a confirmation, several subscriptions to the same node stand side by side, and without a `subid` nothing is unsubscribed or configured when there are several. The options per subscription (§6.3) and those of a node (§8.2) are read and set — recorded is only what the service has confirmed, and `<create/>` sends its settings along, so that the node does not stand open in between. Roles are read and granted (§5.7/§8.9); a list with one unreadable entry counts as unreadable entirely. The owner sees the subscribers of their node (§8.8.1) and can remove them (§8.8.2) — remove only: a client that signs others up unasked has no name here. A sign-off from the service (§8.8.4) strikes the subscription from the own books; a grant by event is accepted only when there is an **application of one's own** pending for it (§8.6) — otherwise a service could sign the client up unasked. A `pending` is recorded but does not count as a subscription: "what have I applied for" and "am I subscribed" are two questions. As an owner the client shows incoming applications and answers them (§8.6.1/§8.6.2). Nodes are deleted and emptied (§8.4/§8.5) — **a deleted one takes the subscription to it along, an emptied one does not**, and what is struck is struck per service and not per name: `urn:xmpp:omemo:2:bundles` is called that at every account. Single items are retracted (§7.2); incoming, the retraction is reported with the ids of the items concerned and leaves the subscription standing. See [WORKPLAN.md](../WORKPLAN.md), D70–D90 |
| XEP-0085 | Chat State Notifications | ✅ | Sending + receiving |
| XEP-0115 | Entity Capabilities | ✅ | ver string complete under §5.1, together with `xml:lang` and XEP-0128 forms, checked against both vectors from §5.2 and §5.3; answers are verified under §5.4, otherwise there is no cache entry |
| XEP-0128 | Service Discovery Extensions | ✅ | Foreign forms are read, our own delivered over `DiscoManager.LocalForms`; both go into the ver string. Empty by default — see below |
| XEP-0156 | Discovering Alternative XMPP Connection Methods | ✅ | Only the HTTP way, and only as far as it is safe: `host-meta` is loaded over HTTPS alone, and only `wss://` endpoints are taken over. BOSH (`xbosh`) is read and passed over — this client does not speak it. The DNS way over `_xmppconnect` is not missing, it has been removed from the XEP |
| XEP-0160 | Best Practices for Handling Offline Messages | ✅ | On the server side: `normal` and `chat` are stored, `groupchat` refused, `headline` and `error` discarded; a `chat` whose content is nothing but a chat state (XEP-0085) likewise, and without an error to the sender. Handed in at the next non-negative available presence, announced as `msgoffline`. Holds for messages from other servers as well |
| XEP-0184 | Message Delivery Receipts | ✅ | With spoofing protection |
| XEP-0203 | Delayed Delivery | ✅ | The server stamps messages handed in late, the client reads the stamp: `XMPPMessage.Timestamp` is the time at which the message was **written**, `ReceivedAt` that of the receiving, `IsDelayed` the difference. Read only at the outer stanza — a carbon brings the stamp of its inner message along — and only with a zone: a time of day without a zone is none (D59) |
| XEP-0198 | Stream Management | ✅ | Checked against Prosody 13 and ejabberd 24.12, on by default, with resumption; after the resending an acknowledgement is requested, so that the queue empties even without a keepalive; the refusal is evaluated as well — an `h` in the `<failed/>` confirms what the server has processed so far |
| XEP-0199 | XMPP Ping | ✅ | Sending, answering, RTT measurement |
| XEP-0280 | Message Carbons | ✅ | With spoofing protection |
| XEP-0308 | Last Message Correction | ✅ | Receiving: `XMPPMessage.ReplacesId` names the message replaced, `IsCorrection` the fact. Sending: `CorrectLastMessageAsync` corrects the last message **to the same recipient** (section 5) and becomes the last one itself, so that a correction can be corrected. In the console `/fix <text>`; announced in disco#info (D60) |
| XEP-0333 | Chat Markers | ✅ | Sending + receiving, namespace-checked against being confused with XEP-0184 |
| XEP-0384 | OMEMO Encryption | ✅ | Complete, `urn:xmpp:omemo:2` — see the section "End-to-end encryption" further below. Checked against the reference implementation python-omemo, in both directions (D69) |
| XEP-0420 | Stanza Content Encryption | ✅ | The envelope OMEMO encrypts: `<content/>` with the sender inside it and a padding of random length |
| XEP-0352 | Client State Indication | ✅ | Both sides. The server announces `<csi/>` after the login (§4.1) and does not answer `<active/>`/`<inactive/>` (§4.2). Held back is only what will still be true later: presence waits and **the last one per full JID replaces the earlier ones** (§3), a message with text, an `iq`, an error and every nonza go out at once, a chat state (XEP-0085) is dropped — it would not be late on being handed in later, it would be wrong. What was held back goes out **before** the stanza that empties the buffer (RFC 6120 §10.1), and at the end of the connection into the buffer of unacknowledged stanzas. Upper bound `MaxHeldWhileInactive` (default 100); on overflow the buffer goes out instead of anything being thrown away. After a resumption "active" holds again (§5.2) — this is why the client declares itself anew after every setup. In the console `/csi active|inactive` (D61) |

## RFC conformance

### RFC 6120 — XMPP Core

| Area | Status |
|---------|--------|
| TLS (§5) | ⚠️ `wss://` over the WebSocket transport; `XMPPConnection.ServerCertificateValidator` allows a certificate check of one's own, `null` leaves it to the operating system. No STARTTLS (§5.4) — WebSocket brings TLS along underneath it, but a plaintext `ws://` is not refused |
| SASL negotiation and execution (§6) | ✅ Client and server; the client takes the strongest mechanism on offer and never a weaker one than last time, the server refuses one it did not offer |
| SASL abort (§6.4.4) | ✅ `<abort/>` is answered with `<failure><aborted/></failure>`, the half-begun SCRAM exchange discarded and the stream **not** ended — an abort is a foreseen step, not a violation. On the client connection and on the S2S stream; the initiator of an S2S stream does not answer it, it would be the sender |
| Directory harvesting (§13.11) | ⚠️ An unknown user name gets the same SCRAM exchange as a known one — made-up credentials from the name and a server key, refusal only at the proof. Otherwise the information would stand in the course of events instead of in the error word. The server key lives in the process, and across a restart the made-up salts change; with PLAIN the running time still differs. The other countermeasures of the section — rate limiting, error information only to those logged in — are missing |
| Resource binding (§7) | ✅ `XMPPConnection.Resource` (default `console-<pid>`, `null` leaves the choice to the server); a `<conflict/>` is followed by a second attempt without a wish, every other refusal breaks off |
| Legacy session (RFC 3921) | ✅ Is skipped when the feature itself carries `<optional/>` |
| Stanza errors (§8.3) | ✅ Type, condition, text and `by` are parsed; pending requests fail instead of seeming to succeed |
| Answer to unhandled IQs (§8.2.3 rule 3) | ✅ Unknown `iq get`/`set` are answered with `<service-unavailable/>` |
| Impossible addresses (§8.3.3.8, §8.1.1.1) | ✅ If the value of the `to` is no JID under RFC 7622, the server answers with `<jid-malformed/>` (error type `modify`) and does not deliver — for `message`, `presence` and `iq` at the same place, before every branch. **Both origins:** from a far side the `from` is checked as well, and before the question of which domain it may speak for — to apply `DomainOf` to something that is no JID compares fragments. An impossible `from` ends the stream under §8.1.1.1 with `<invalid-from/>`, an impossible `to` costs only the stanza (D51, D53). The sender of the refusal is the server itself and not the intended recipient: the address is none, so nobody has looked in there. A stanza **without** a `to` is not affected by this (§8.1.1.1), and an error stanza is not followed by an error (§8.3.1) — it is discarded all the same. What checks is the same RFC 7622 check the client uses for its own addresses |
| Check of the IQ type (§8.2.3 rule 2) | ✅ If the `type` attribute is missing or carries a value other than `get`, `set`, `result` or `error`, a `<bad-request/>` follows with the error type `modify` (§8.3.3.1). Checked in both roles the section names: by the client as a recipient and by the server as an "intermediate router" — there **before** every delivery, so also for what goes to the server address itself, to a local recipient or across the border. Likewise for what comes in from a far side. Without an `id` the refusal goes out all the same and then carries none |
| Stream errors (§4.9) | ✅ Parsed; after a condition that cannot be repeated, the reconnect is left out |
| Branch for incoming frames (§8.1) | ✅ What decides is the **element name**, not a prefix: `<iqbogus/>` is no `iq`, `<presence-probe/>` no `presence`, `<opencast/>` no stream opening. A namespace prefix does not change the type (`<client:iq/>` is an `iq`, `<stream:features/>` and `<features/>` are the same element) |
| Unknown element at stream level (§4.9.3.24) | ✅ On both streams — client as well as S2S — an `<unsupported-stanza-type/>` follows, and the stream ends (§4.9.1.1). Holds for an unknown element in a **known** namespace as well: `<enabled/>` is a proper XEP-0198 element, but it comes from the server and not from the client. For the S2S stream this was measured beforehand instead of assumed: over the full run against Prosody and ejabberd, outgoing as well as incoming, not a single unknown frame arrived there. A frame **without** an element is no unknown element and is passed over — whitespace is allowed as a keepalive (§4.6.1) |

### RFC 6121 — Instant messaging and presence

| Area | Status |
|---------|--------|
| Fetch, add, remove roster, groups | ✅ The groups (§2.1.2.4) were lost halfway until D91: the client sent them, the server read the `<item/>` only as far as its attributes and sent the same entry back in the push without them — and because a push **replaces** the groups of an entry, they vanished at the client with that as well. Now the server keeps them, gives them out in the fetch and in the push, they count for the version of the roster, and they survive a restart |
| Result replaces the cache (§2.1.4) | ✅ A contact removed while the client was logged off is gone afterwards — before, it stayed standing |
| Apply roster pushes | ✅ Adding and not replacing: a push carries only the changed entries |
| Sender validation of roster pushes (§2.1.6) | ✅ Only without a `from` or with our own bare JID; otherwise discarded and reported as spoofing |
| Roster versioning (§2.6) | ✅ Client and server; `<ver/>` is announced, unchanged rosters come as an empty result, pushes carry the new version. The version is a hash over the content — switchable over `XMPPServer.OfferRosterVersioning` |
| Ask for/accept/deny a presence subscription | ✅ |
| Incoming `subscribed`/`unsubscribed`/`unsubscribe` | ✅ Change the subscription state and do not count as presence |
| Message types (§5.2.2) | ✅ `chat`, `groupchat`, `headline`, `normal`, `error`; a missing or unknown value counts as `normal`. To `groupchat` and `headline` nothing is answered by itself — everybody present would see a receipt in a room |
| Delivery rules by type (§8.5) | ✅ To the bare JID: `groupchat` is refused with `<service-unavailable/>`, `error` silently discarded, `headline` to **all** resources with a non-negative priority, `normal`/`chat` to one. To a matching resource: everything, `groupchat` and `error` included (§8.5.3.1). To a resource that does not exist: `chat` as to the account (§8.5.3.2.1), everything else silently discarded. Holds for messages from local clients **and** from other servers — the section speaks of an "inbound stanza" and does not tell the origins apart. A refusal finds the way back across the border |
| Offline store (§8.5.2.2.1) | ✅ Without a reachable resource, `normal` and `chat` are stored and handed in at the next non-negative available presence — with an XEP-0203 stamp, across a restart and announced as `msgoffline` in disco#info. For messages from other servers as well, and that is the ordinary case. Switchable over `XMPPServer.StoreOfflineMessages`; then the sender gets `<service-unavailable/>`, which the same section allows equally. Upper bound `MaxStoredOfflineMessages` (default 100): once it is reached, the new message is refused and no stored one displaced |
| IQ delivery rules (§8.5.1, §8.5.2.1.3, §8.5.2.2.3, §8.5.3.2.3) | ✅ A request to a bare JID is not delivered but answered by the server with `<service-unavailable/>` — exactly once, and for an unknown account likewise, so that the answer betrays no accounts. To a matching resource it is delivered; without a matching resource the server answers. A `result` or `error` is never answered (RFC 6120 §8.2.3 rule 4) and not distributed to a bare JID. Holds for both origins |
| Request to the server address (§8.2.3 rule 3) | ✅ Ping (XEP-0199) and disco#info (XEP-0030) the server answers for itself — to a local client as to a far side, for the information does not hang on who is asking; only the way back differs. What it does not know gets `<service-unavailable/>` instead of silence. **Not** reachable that way are binding, legacy session, carbons and the roster: those change the state of a session or belong to an account — a foreign server asking for the roster gets the same refusal as for every unknown request |
| Message to an unknown account (§8.5.1) | ✅ The section leaves the choice between `<service-unavailable/>` and silence, but it has to be the same one as for an existing account that is not watching right now — otherwise it answers the question "does this account exist?". What is asked is therefore not whether an account exists but whether the store would accept the message: for an unknown one it is empty, and an empty one accepts as long as anything fits in at all. If the store is off or full, both get `<service-unavailable/>`; if it is on, the server keeps quiet for both. For an unknown account nothing is stored (D52) |
| IQ check against presence leaks (§8.5.3.1) | ✅ A request to a resource is delivered only when the recipient shares their presence with the one asking — over the roster (`from` or `both` in **their** half) or over directed presence (§4.6). Otherwise the same answer as for a resource that does not exist; nothing can be read out of the refusal, then. For `result` and `error` it does not hold — the server has to deliver those under the same section |
| Directed presence (§4.6) | ✅ Recorded per resource, emptied on the sign-off, taken back on a directed `unavailable`, and likewise when the recipient sends us a sign-off of their own (§4.6.1, MUST and SHOULD). When the resource becomes unavailable — by a sign-off of its own or by a connection break — the sign-off goes to all recipients of directed presence who do not get it over the roster anyway (§4.6.3 rule 2). A change of status in the middle of the session does not end the promise |
| Presence delivery rules (§8.5.2.1.2, §8.5.3.1) | ✅ Available and unavailable presence goes to the bare JID to all resources, to a full JID to the matching one, otherwise silently nowhere (§8.5.1, §8.5.3.2.2) — for both origins |
| Presence probe (§4.3) | ✅ The server answers it itself and delivers it to no client, whether it comes from a local client or from a far side. A probe to a foreign domain it sends out (§4.3.1). Answered only when the one asking stands in the roster of the one asked with `from` or `both`; otherwise silence, which does not betray an unknown account either (§8.5.1 leaves the choice) |
| Presence priority (§4.7.2.3) | ✅ Read and heeded; a negative priority gets nothing that went to the bare JID, but stays addressable in a directed way. The client sets it over `XMPPConnection.PresencePriority` |

### RFC 7395 — XMPP over WebSocket

| Area | Status |
|---------|--------|
| Subprotocol `xmpp`, `<open/>`/`<close/>` framing | ✅ |
| Close handshake | ✅ `<close/>` is sent, then up to 3 s waited for the far side, after that the socket is broken off |
| Endpoint discovery (XEP-0156 / `host-meta`) | ✅ Without a given endpoint, `https://<domain>/.well-known/host-meta.json` and after that `.../host-meta` are read; only `wss://` addresses are taken. Without a find it stays at `wss://<domain>:5443/ws` |

The default port is ejabberd-specific and takes effect only when the domain
delivers no `host-meta`. Whoever does not want it gives the URL, e.g. Prosody:
`wss://<host>:5281/xmpp-websocket` — a given endpoint is never overruled.

### RFC 5802 / RFC 7677 — SCRAM

| Area | Status |
|---------|--------|
| Four-step handshake | ✅ |
| Nonce check against MITM | ✅ |
| Server signature verification (constant running time) | ✅ Mandatory — a `<success/>` without a server-final-message breaks the setup off |
| SASLprep (RFC 4013) | ✅ Complete: mapping, NFKC, prohibition tables, unassigned code points and the bidi rules; checked against the example table from §3 |
| Channel binding (RFC 9266 `tls-exporter`) | ❌ |

### RFC 7622 — JID handling

`JidUtilities` splits, checks and compares JIDs under RFC 7622; checked against
both example tables from §3.5 (fifteen valid and eight invalid addresses).

| Rule | State |
|---|---|
| Splitting in the order from §3.2 (first `/`, then `@`) | ✅ |
| Localpart: UsernameCaseMapped, plus the exclusions from §3.3.1 | ✅ Mapping rules complete, IdentifierClass from the derived properties under RFC 8264 §8 |
| Resourcepart: OpaqueString, **not** lowercased | ✅ likewise, with the FreeformClass |
| Domainpart: lowercased, NFC | ✅ IDNA2008 label by label (RFC 5891/5892), Punycode computed ourselves (RFC 3492), bidi rule under RFC 5893 over a table generated from `DerivedBidiClass.txt` |
| Maximum length 1023 octets per part | ✅ |
| Comparison: local and domain part case-insensitive, resource part not | ✅ |

The class membership comes from `Precis.DerivedProperty` and thereby from the
ladder in RFC 8264 §8: exception list (RFC 5892 §2.6), Unassigned, ASCII7,
JoinControl, old Hangul Jamo, ignorable characters, Controls, HasCompat,
LetterDigits, OtherLetterDigits, Spaces, Symbols, Punctuation — in that order,
for many code points stand in several of these categories.
`Default_Ignorable_Code_Point`, `Noncharacter_Code_Point` and
`Hangul_Syllable_Type` .NET does not deliver; they stand as range tables in the
source, named with the Unicode version they come from (15.1.0).

The domain part goes through `Idna` — the same building blocks, but the ladder
from RFC 5892 §1 instead of the one from RFC 8264 §8, and therefore different
answers: an underscore belongs in a local part and in no label, a symbol in a
resource part and in no label. An A-label (`xn--…`) is decoded, checked against
the label rules and computed back; if the back-computation yields a different
spelling, it is refused. Address literals (`127.0.0.1`, `[::1]`) are exempt
under RFC 7622 §3.2.

If a single label carries right-to-left characters, the whole name is a
*bidi domain name* (RFC 5893 §2), and then **all** labels have to meet the six
conditions — the ones of pure ASCII as well. `9abc.example` is therefore a
valid domain name and `9abc.אבג` is not. The bidi classes stand in
`libs/Ratatoskr/Ratatoskr/Common/BidiClasses.cs`, generated by
`libs/Ratatoskr/tools/unicode/generate-bidiclass.py`
from `DerivedBidiClass.txt`.

The context-dependent rules from RFC 5892 appendix A are implemented in full —
for local parts as for domain labels. They do not hang on the code point but on
its surroundings: `col·la` is a Catalan word and a valid local part,
`co·lla` is not. The properties needed for that
(`Canonical_Combining_Class`, `Joining_Type`, `Script`) stand in
`libs/Ratatoskr/Ratatoskr/Common/ContextTables.cs`, generated by
`libs/Ratatoskr/tools/unicode/generate-contexttables.py`.

**One deliberate deviation:** example 18 of table 2
(`juliet@example.com/ foo`, a leading space in the resource part) is accepted.
The table lists it as a non-JID, but the rule for it is missing — the
OpaqueString profile expressly allows spaces. For a router, accepting is
besides the more cautious choice: to refuse an address other servers hold to be
valid loses messages.

## Installation

```bash
# .NET 10 SDK required
dotnet build
dotnet run
```

## Usage

```bash
# Interactive (asks for JID, password and WebSocket URI)
dotnet run

# With parameters
dotnet run -- -j user@example.com -p secret

# With an explicit WebSocket URL (needed with non-ejabberd servers)
dotnet run -- -j user@example.com -p pw -w wss://xmpp.example.com:5281/xmpp-websocket

# With the full protocol log
dotnet run -- -j user@example.com -p secret -v
```

| Option | Meaning |
|--------|-----------|
| `-j`, `--jid <jid>` | JID in the form `user@domain` |
| `-p`, `--password <pw>` | Password |
| `-w`, `--ws`, `--websocket <uri>` | WebSocket URI |
| `-v`, `--verbose` | Verbose logging (trace level, shows every stanza) |
| `-h`, `--help` | Show the help |

## Commands

### Messages
```
/to <jid>                 set the chat partner (aliases: /chat)
/to                       reset the chat partner
/msg <jid> <text>         send a single message (alias: /m)
/fix <text>               correct the last message (XEP-0308, alias: /corr)
/status [show] [text]     set the status: available|away|chat|dnd|xa (alias: /s)
```

### Contacts (roster)
```
/roster [filter]          show the contacts (aliases: /list, /contacts)
/online                   only the online contacts
/add <jid> [name] [g1,g2] add a contact and ask for a subscription
/remove <jid>             remove a contact (alias: /del)
/info <jid>               contact details
/groups                   groups with the number of contacts
/pending                  pending contact requests
/accept [jid]             accept a contact request (without an argument: the first)
/deny [jid]               deny a contact request (without an argument: the first)
```

### Chat states (XEP-0085)
```
/typing                   send 'is typing'
/paused                   send 'has stopped typing'
/gone                     leave the chat and reset the recipient
```

### Chat markers (XEP-0333)
```
/mark received [msg-id]   mark as received (alias: r)
/mark displayed [msg-id]  mark as read (aliases: d, read)
/mark ack [msg-id]        acknowledge (aliases: acknowledged, a)
```
Without a `msg-id` the message received last is used.

### Service discovery (XEP-0030)
```
/disco                    show the subcommands
/disco server             features of our own server
/disco info <jid>         features of a JID
/disco items <jid>        services/items of a JID
/features                 server features and our own features
```

### PubSub (XEP-0060)
```
/pubsub                        show the subcommands
/pubsub sub <node> [jid]       subscribe to a node (alias: subscribe)
/pubsub unsub <node> [jid] [subid]  end a subscription (alias: unsubscribe)
/pubsub subscriptions          own subscriptions with subid (alias: subs)
/pubsub sync [jid]             fetch the subscriptions from the service and take them over
/pubsub opts <node> [subid]    options of the subscription (alias: options)
/pubsub deliver <node> <on|off> [subid]  delivery on/off
/pubsub pub <node> <id> <data> publish an item (alias: publish)
/pubsub get <node> [max]       fetch items (alias: items)
/pubsub create <node> [access]   create a node (models as with 'access')
/pubsub cfg <node>             node settings (alias: nodecfg)
/pubsub access <node> <open|presence|whitelist|roster|authorize>  change the access
/pubsub roles <node>           who is what at this node (alias: affiliations)
/pubsub role <node> <jid> <owner|publisher|member|outcast|none>  set a role
/pubsub subscribers <node>     who hangs on this node (alias: who)
/pubsub kick <node> <jid> [subid]  remove a subscriber (alias: remove)
/pubsub groups <node> [group...]  roster groups for 'roster' (alias: rostergroups)
/pubsub request <node> <jid> <yes|no>  answer a subscription request (alias: authorize)
/pubsub retract <node> <id>    take back a single item (alias: undo)
/pubsub purge <node>           empty a node, keep the subscribers (alias: empty)
/pubsub delete <node>          delete a node
```

Without a `<jid>` the request goes to `pubsub.<domain>`; a PEP node belongs to
an account, and then its bare JID stands there. **Every one of these commands
reports what the service answered** — "Subscribed" means that it confirmed, and
not that it was asked.

The `[subid]` on unsubscribing is needed as soon as there are several
subscriptions to the same node: without it there would be no saying which one is
meant, and the client picks none. `/pubsub subscriptions` shows them.

`subscriptions` and `subscribers` ask in opposite directions: the one where this
client hangs, the other who hangs on a node of one's own. With `kick` the
`[subid]` is optional by contrast — without it **all** subscriptions of that JID
go, for the owner means the human being and not the books.

An applied-for subscription stands among the others at `subscriptions` and says
what it is (`pending`). Without the state it would look like a granted one, and
the absent events would look like a mistake. An incoming application is reported
as soon as it arrives; `request` answers it.

### Connection
```
/ping [jid]               send a ping and measure the RTT (XEP-0199)
/keepalive [on|off|sec]   show/change the keepalive status
/sm [on|off]              show/change the stream management status
/csi [active|inactive]    client state indication (XEP-0352)
/who                      show our own connection status
/carbons                  show the carbon status
/reconnect                connect again
/disconnect               disconnect
/raw                      toggle the XML debug output
/help                     help (aliases: /h, /?)
/quit                     quit (aliases: /q, /exit)
```

## Keepalive (anti-timeout)

Default interval: **25 seconds**. Changes take effect only after a reconnect,
because the loop is started when the connection is set up.

```
/keepalive
Keepalive status:
  Enabled: True
  Interval: 25s
  Method: Stream Management <r/>

/keepalive 60      # set the interval to 60s
/keepalive off     # disable
```

**Methods:** if stream management is active, an `<r/>` is sent (lightweight),
otherwise an XEP-0199 ping.

## Setting up a connection: succeeded or thrown

`ConnectAsync` **throws** when the setup fails — the original error, not a shell
around it: `AuthenticationException` on a refused login,
`XMPPProtocolException` on a failed negotiation. Whoever survives the call has a
connection.

**One exception to that is the transport itself.** If the connection does not
come about at all, the error from there reads "Unable to connect to the remote
server" and does not name the address — which since XEP-0156 can come from the
`host-meta` of a foreign domain as well and then stands in no source file. This
one case is therefore wrapped in an `XMPPProtocolException` that names the
endpoint; the original error is kept as the `InnerException`. A setup that was
cancelled stays an `OperationCanceledException`.

Only the express call throws. The reconnection attempt in the background has no
caller and goes on reporting over `OnError` and `OnStateChanged`.

## Deadlines when setting up a connection

Every reading step of the negotiation — stream header, features, every SASL
round — has **10 seconds**, and so does the resource binding. If a deadline runs
out, the setup fails with a message that names the step ("No answer came to the
stream header within 10 seconds").

The reason is the one case an error does not cover: a far side that accepts the
connection and then **keeps quiet**. An error arrives, a closed socket arrives —
silence does not arrive, and without a deadline `ConnectAsync` would never
return.

## Spoofing protection

With three kinds of message the client checks the sender before it processes
them:

1. **Carbons (XEP-0280)** — have to come from our own bare JID (that is, from
   our own server). Otherwise any contact could smuggle in arbitrary messages
   as supposedly sent by ourselves.
2. **Receipts (XEP-0184)** — have to come from the bare JID of the original
   recipient.
3. **PubSub events (XEP-0060)** — have to come from the configured PubSub
   service **or from the one this node was subscribed at**. The second
   permission hangs on the node and not on the sender: whoever subscribed to
   the one node at Bob has not allowed Bob to send events about every other one
   he can think of. Without it no PEP event got through at all — under XEP-0163
   it comes from the account itself and therefore counted as a forgery every
   time.
4. **Roster pushes (RFC 6121 §2.1.6)** — have to come without a `from` or from
   our own bare JID. Otherwise any sender could smuggle contacts into the local
   roster or delete them from it.

5. **Caps answers (XEP-0115 §5.4)** — a disco#info answer goes into the cache
   under `node#ver` only when its SHA-1 hash yields exactly that `ver` value.
   Otherwise anybody whose presence arrives here could announce the `node#ver`
   pair of a widespread client, answer with a list of their choosing and thereby
   foist it on every further contact that announces the same pair.

## Architecture

Three layers, cleanly separated:

| Layer | Type | Task |
|---------|-----|---------|
| UI | `Program` | Command line, command dispatch, presentation. Holds no protocol logic. |
| Application | `XMPPClient` | Session state (chat partner, pending contact requests, last message id) and composite operations. |
| Protocol | `XMPPConnection` | WebSocket I/O, SASL, resource binding, stanza routing. |

`XMPPClient` and `XMPPConnection` write nothing to the console — everything runs
over events and the injected `ILoggerFactory`.

### Setting up a connection

The setup falls into two parts, and the border lies at the resource binding:

1. **Negotiation** (`<open/>`, stream features, SASL, binding). Here
   `ConnectInternalAsync` reads from the socket itself. That is unproblematic,
   because the server has no resource yet to deliver anything to — nothing else
   can arrive. The evaluation goes over `StreamNegotiation`, a collection of
   pure functions on the parsed `XElement`.
2. **Session setup** (legacy session, XEP-0198, carbons, roster, presence).
   From the binding on, the receiving loop is running, and all the steps go over
   `SendIqAsync` — the same `TaskCompletionSource` correlation over the stanza
   id that `DiscoManager` and `PingManager` use. Whatever else arrives in that
   time (messages handed in late, presence, roster pushes) is delivered quite
   normally.

Working on text patterns are now deliberately only `StreamManagementManager`
(reads `h` and `id` out of nonzas), `StanzaError`/`StreamError` (have to cope
with ill-formed frames precisely) and `SCRAMAuthenticator` (SASL is no XML).

### Using it as a library

```csharp
using Microsoft.Extensions.Logging;
using org.GraphDefined.Vanaheimr.Ratatoskr;

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());

await using var client = new XMPPClient(
                             "user@example.com",
                             "secret",
                             "wss://xmpp.example.com:5443/ws",
                             loggerFactory);

client.OnMessage += msg =>
    Console.WriteLine($"{msg.FromBareJid}: {msg.Body}");

client.OnSubscriptionRequest += async (from, status) =>
    await client.AcceptSubscriptionAsync(from);

await client.ConnectAsync();

client.SetChatPartner("contact@example.com");
await client.SendMessageAsync("Hello!");
```

The `ILoggerFactory` is optional; without it there is a fallback to
`NullLogger` and nothing is logged at all. Log levels: `Information` for
connection steps, `Debug` for protocol details, `Trace` for single stanzas,
`Warning` for spoofing attempts fended off and protocol oddities.

## Project structure

**The protocol has not lived here since D97** but in
[Ratatoskr](../libs/Ratatoskr/README.md) — a repository of its own under
`libs/`, so that it can be used without this console. What stays here is what
is user interface, and the setup for the checks against foreign servers.

The namespace is flat throughout: `org.GraphDefined.Vanaheimr.Ratatoskr` (like
`Hermod.DNS` and `Hermod.HTTP`); the folders only group. One file per type:

```
Jabber/                                  the console
├── Program.cs                           command line, branch, presentation
└── ConsoleUI/
    ├── ConsoleOutput.cs                 One lock, one line, one output
    └── ConsoleOutputLoggerProvider.cs   The log through the same lock

tools/                                   the foreign far sides
├── prosody/setup.sh                     Prosody 13 rootless in WSL
└── ejabberd/setup.sh                    ejabberd 24.12 rootless in WSL

libs/Ratatoskr/Ratatoskr/                the protocol
├── Client/       XMPPClient, XMPPMessage, MessageType
├── Common/       JIDs (RFC 7622), PRECIS, IDNA, Punycode, bidi classes,
│                 stanza names and namespaces, XML escaping
├── Auth/         SCRAM (RFC 5802/7677), SASLprep, mechanism policy
├── Connection/   XMPPConnection: WebSocket I/O, negotiation, stanza routing
├── Errors/       stanza and stream errors
├── Rosters/      roster, subscription states, stanza building
├── Server/       XMPPServer, XMPPSession, S2S, accounts, PEP
└── XEPs/         one folder per XEP, named after its number

libs/Ratatoskr/tools/                    generated tables
├── unicode/      bidi classes and the context tables from RFC 5892 appendix A
└── stringprep/   the StringPrep tables from RFC 3454
```

The XEP managers get their sending function injected as a `Func<string, Task>`
and do not know the transport — they are thereby testable independently of
`XMPPConnection`. The complete tree stands in the
[README of Ratatoskr](../libs/Ratatoskr/README.md#project-structure).

## Tests

```bash
# Die Konsolenausgabe - acht Tests
dotnet test ../Jabber.Tests/Jabber.Tests.csproj

# Das Protokoll - der grosse Teil
dotnet test ../libs/Ratatoskr/RatatoskrTests/RatatoskrTests.csproj
```

NUnit in denselben Versionen wie `HermodTests` (NUnit 4.6.1, NUnit3TestAdapter
6.2.0, Test.Sdk 18.8.1). Die Fixtures sind nach Themen gegliedert; der Namespace
bleibt dabei flach `org.GraphDefined.Vanaheimr.Ratatoskr.Tests`, die Ordner
gliedern nur:

```
libs/Ratatoskr/RatatoskrTests/
├── Infrastructure/     Basisklasse aller Fixtures, Wache gegen interne Fehler
├── Common/             JIDs, Stanza-Namen, Namensraeume, IQ-Typen, XML-Splitter
├── Auth/               SASL/SCRAM, Mechanismus-Politik, Konten und Zertifikate
├── Streams/            Aushandlung, Binding, TLS, Fristen, Wiederverbindung
├── StreamManagement/   XEP-0198: Zaehlen, Bestaetigen, Wiederaufnehmen
├── Federation/         S2S: Dialback, SRV, TCP/WebSocket, fremde Server
├── Routing/            Zustellregeln, mehrere Resourcen, Offline-Ablage
├── Rosters/            Roster, Subscriptions, Versionierung, Push-Sicherheit
├── Stanzas/            Aufbau, Parsen und Fehler einzelner Stanzas
└── XEPs/               XEP-0115 Caps und die Nutzlasten der uebrigen XEPs
```

`Jabber.Tests` prueft nur noch die Konsolenausgabe — dass sie die Eingabezeile
heil laesst, fuer Ereignisse **und** fuer das Protokoll. Die Kommandos selbst
haben keine Tests.

**Die Zahl der uebersprungenen Tests ist die Gesundheitspruefung des Laufs.**
Steht sie auf **7**, standen Prosody, ejabberd und das OMEMO-Orakel bereit; jede
hoehere Zahl heisst, dass etwas gar nicht erst geprueft wurde. Die sieben, die
auch im guten Fall stehen bleiben, sind keine Versaeumnisse, sondern eine Grenze
der Umgebung: sechs brauchen den **eingehenden** Weg, den die Hyper-V-Firewall
von WSL zum Windows-Host verwirft (siehe unten), und einer gilt nur im
STARTTLS-Betrieb.

### XMPPServer

`libs/Ratatoskr/Ratatoskr/Server/` enthält einen echten XMPP-Server: über
WebSocket (RFC 7395) zu Clients, über TCP (RFC 6120) zu anderen Servern. Er lag
bis D97 im Hauptprojekt, damit er beim Umzug in eine Bibliothek mitwandert —
dieser Umzug ist erfolgt, und sein Namespace ist jetzt `…Ratatoskr.Server`. Er
reicht so weit, dass sich mehrere echte `XMPPClient`-Instanzen gleichzeitig anmelden und
miteinander sprechen:

- TLS: `wss://` mit einem selbst signierten Zertifikat, das der Konstruktor
  erzeugt (RFC 6120 §5). `new XMPPServer(useTLS: false)` schaltet auf `ws://`
  zurück, was für die Fehlersuche mit einem Mitschnitt gedacht ist
- SASL: SCRAM-SHA-256, SCRAM-SHA-1 und PLAIN, in dieser Reihenfolge angeboten.
  Welche Mechanismen es sein sollen, steuert `OfferedSaslMechanisms`; ein nicht
  angebotener wird auch dann abgelehnt, wenn ein Client ihn versucht
- Zugangsdaten nach RFC 5802 §3 — Salt, Iterationszahl, `StoredKey` und
  `ServerKey` je Mechanismus. Kein Klartextpasswort, auch nicht für PLAIN:
  das prüft, indem es aus dem angebotenen Passwort neu ableitet
- Ein **unbekannter Benutzername** bekommt denselben Austausch wie ein
  bekannter: erfundene Zugangsdaten aus dem Namen und einem Serverschlüssel —
  je Name andere, für denselben Namen immer dieselben —, und die Abweisung
  kommt erst am Beweis. Sonst stünde die Antwort auf „gibt es dieses Konto?"
  im Ablauf, ganz gleich welches Fehlerwort dabei steht (RFC 6120 §13.11,
  „Directory Harvesting")
- Konten und Roster über `IXMPPAccountStore`: `InMemoryAccountStore` (Vorgabe)
  oder `FileAccountStore` für einen Bestand, der den Neustart übersteht
- Routing nach Domain: was nicht hierher gehört, geht über `IServerLinks`
  hinaus; eine unerreichbare Domain wird mit `<remote-server-not-found/>`
  beantwortet. `DirectServerLinks.Connect(a, b)` verbindet zwei Instanzen im
  selben Prozess, ohne jedes Netz — für Tests, nicht für den Betrieb.
  `WebSocketServerLinks.Connect(a, b)` tut dasselbe über einen echten
  WebSocket-S2S-Stream (`S2SStream`, eigener Handshake nach RFC 7395 §3.4,
  Subprotokoll `xmpp-server`): eine Absenderfälschung beendet dort nicht nur
  die Zustellung, sondern den Stream und die Verbindung (RFC 6120 §8.1.1.1,
  §4.9)
- Zwei S2S-Transporte unter derselben Protokollschicht (`S2SStream`):
  `WebSocketServerLinks` (RFC-7395-Rahmen, Subprotokoll `xmpp-server`, nur
  zwischen Instanzen dieses Servers) und `TcpServerLinks`
  (`jabber:server`-Streams über TCP nach RFC 6120 — der Weg zu ejabberd und
  Prosody). Was sich unterscheidet, ist nur die Rahmung (`IS2SFraming`) und
  dass TCP den Strom erst über `XmlStreamSplitter` in Elemente zerlegen muss
- XEP-0288 Bidirectional Server-to-Server Streams: beide Richtungen über eine
  Verbindung. Ohne die Erweiterung antwortet jede Seite über eine *eigene*
  ausgehende Verbindung (RFC 6120 §4.1) — hinter NAT, hinter einer Firewall
  oder ohne DNS-Eintrag geht die Antwort dann verloren, und zwar
  stillschweigend. Zwei Schalter, weil es zwei Dinge sind:
  `OfferBidirectionalStreams` kündigt sie auf eingehenden Verbindungen an,
  `RequestBidirectionalStreams` erbittet sie auf ausgehenden. Über die
  Rückrichtung geht nichts vor dem Ausweis der Gegenstelle und nichts für eine
  fremde Domain. Auf beiden S2S-Transporten, gegen Prosody 13 und ejabberd
  24.12 in beiden Richtungen geprüft.

  Angekündigt werden **beide** Namensräume (`urn:xmpp:features:bidi` und
  `urn:xmpp:bidi`), gelesen ebenfalls beide. Die XEP kennt für die Ankündigung
  nur den ersten; ejabberd 24.12 legt in die Features das Freischalt-Element
  und greift nur den zweiten auf. Beobachtet, nicht vermutet — mit nur der
  XEP-Form nimmt es unsere Rückrichtung nicht. Eindeutig bleibt es trotzdem:
  das Freischalt-Element heisst in beiden Lesarten `urn:xmpp:bidi`
- Aufbewahrte Subscription-Anfragen (RFC 6121 §3.1.3): wer nicht verbunden ist,
  bekommt seine Anfragen beim nächsten Anmelden — und bei jeder weiteren
  Resource wieder, bis er zustimmt oder ablehnt. Aufbewahrt wird die
  vollständige Stanza samt `<status/>`, je Absender genau eine, und mit einer
  Obergrenze je Konto. Ein Roster-Eintrag entsteht dabei nicht: die Security
  Warning des Abschnitts untersagt ihn vor der Zustimmung
- Subscription-Pre-Approval (RFC 6121 §3.4): ein Kontakt lässt sich zulassen,
  bevor er fragt; seine spätere Anfrage beantwortet der Server selbst und stellt
  sie dem Nutzer gar nicht erst zu. Angekündigt als
  `urn:xmpp:features:pre-approval`, clientseitig `PreApproveContactAsync`
- Subscription-Handshake über die Domain-Grenze (RFC 6121 §3): jede Seite
  pflegt ihre eigene Roster-Hälfte, und ein Antragsteller, der den Kontakt
  ohnehin schon sehen darf, wird vom Server des Kontakts direkt beschieden
  (§3.1.4)
- SRV-Auflösung (RFC 6120 §3.2.1): Gegenstellen werden über
  `_xmpp-server._tcp.<domain>` gefunden statt von Hand eingetragen, mit der
  Reihenfolge aus RFC 2782. Ein Eintrag von Hand geht vor; das Zertifikat wird
  gegen die gesuchte Domain geprüft, nie gegen den Rechnernamen aus dem
  SRV-Eintrag
- SASL-EXTERNAL auf der TCP-Strecke (XEP-0178): die Domain der Gegenstelle wird
  über ihr TLS-Zertifikat belegt statt über eine Dialback-Rückfrage.
  `CertificateIdentity` liest die dNSName-Einträge — bei vorhandener SAN zählt
  der Common Name nicht mehr (RFC 6125 §6.4.4), Platzhalter gelten nicht
- STARTTLS auf der TCP-Strecke (RFC 6120 §5.4), Vorgabe von `TcpTlsMode`. Wird
  als `<required/>` angekündigt und ist es auch: wer die Verschlüsselung
  ausschlägt oder gar nicht erst anbietet, bekommt keinen Stream — und keinen
  unverschlüsselten
- Dialback (XEP-0220) auf beiden S2S-Wegen: die Domain der Gegenstelle
  wird belegt, nicht geglaubt. Der annehmende Server fragt dazu **nicht** den,
  der sich ausweisen will, sondern die für diese Domain hinterlegte Adresse —
  über eine eigene, kurzlebige Verbindung. Vor bestandenem Dialback trägt der
  Stream keine Stanza
- Resource Binding mit eindeutiger Resource je Verbindung
- Routing von `message`, `presence` und `iq` zwischen den Sitzungen
- Presence nur an Berechtigte (RFC 6121 §4): Kontakte mit `from` oder `both`
  plus die eigenen weiteren Resourcen. Dazu Presence-Probes, das Nachliefern
  des Kontaktzustands beim Anmelden und die Abmeldung beim Verbindungsende —
  auch wenn sie abreisst und der Client selbst nichts mehr sagen kann (§4.5.2)
- Subscription-Handshake (RFC 6121 §3): `subscribe`/`subscribed`/`unsubscribe`/
  `unsubscribed` ändern die Roster **beider** Seiten und lösen Roster-Pushes
  aus; `ask='subscribe'` hält eine offene Anfrage fest
- XEP-0280 Carbons (`sent` und `received`) zwischen Resourcen eines Kontos
- serverseitiger Roster mit Roster-Push
- XEP-0163 Personal Eventing als Teilmenge: Ein Konto kann in PEP-Knoten
  veröffentlichen, jeder kann sie abrufen, und Kontakte mit `from` oder `both`
  werden benachrichtigt. **Der Server antwortet dabei für das Konto und nicht
  der Client** — sonst wäre ein OMEMO-Bundle nur abrufbar, solange sein
  Besitzer online ist. Was fehlt: Knotenkonfiguration, Zugriffsmodelle,
  gefilterte Benachrichtigungen über XEP-0115
- XEP-0060 §6.1/§6.2 Abonnements auf PEP-Knoten: `<subscribe/>` und
  `<unsubscribe/>` mit `subid`, samt der Ablehnungen des XEP —
  `<item-not-found/>`, `<invalid-jid/>`, `<not-subscribed/>`,
  `<invalid-subid/>`, `<subid-required/>`. **Ein Abonnent bekommt die
  Benachrichtigungen auch ohne Presence-Berechtigung.** Den `jid` darf nur
  setzen, wem er gehört — sonst könnte jeder jeden anmelden oder, schlimmer,
  abmelden
- Mehrere Abonnements desselben JIDs auf denselben Knoten: Jedes `subscribe`
  legt eines an, zugestellt wird **je Abonnement** mit der SHIM-Kopfzeile
  `SubID` (§12.20), und Abbestellen ohne `subid` wird bei mehreren abgewiesen.
  Ein ausdrückliches Abonnement verdrängt die Presence-Zustellung, damit die
  Zahl der Zustellungen nicht davon abhängt, wer nebenbei im Roster steht
- XEP-0060 §8.1/§8.2 Knoten anlegen und konfigurieren: `<create/>` mit
  optionalem Formular, `<configure/>` im `#owner`-Namensraum, und **nur der
  Eigentümer**. Ein angelegter Knoten existiert, bevor etwas darin steht.
  Wirksame Felder: `pubsub#max_items` (eine kleinere Grenze gilt sofort),
  `pubsub#persist_items` (ein Knoten ohne Ablage meldet nur),
  `pubsub#access_model` und `pubsub#roster_groups_allowed`. Angeboten werden
  **alle fünf** Modelle — `open`, `presence`, `whitelist`, `roster` und
  `authorize`; was kein Modellname ist, wird abgewiesen statt zu `open`
  verkürzt
- Das Zugriffsmodell wird durchgesetzt: `presence` sperrt beim Abrufen und beim
  Abonnieren aus, wer die Presence des Eigentümers nicht sehen darf
  (`<not-authorized/>` mit `<presence-subscription-required/>`); der Eigentümer
  kommt immer an seinen Knoten. **Es verrät dabei, dass es den Knoten gibt** —
  so sieht es XEP-0060 §6.5.3 vor, und für einen Knoten, dessen blosse Existenz
  ein Geheimnis wäre, ist `presence` das falsche Mittel
- XEP-0060 §7.1.5 `<publish-options/>`: Die Bedingungen einer Veröffentlichung
  werden geprüft — der Knoten entsteht passend oder die Veröffentlichung wird
  mit `<conflict/>` und `<precondition-not-met/>` abgewiesen. Damit hat die
  Bedingung Wirkung, die OMEMO seit jeher mitschickt (XEP-0384 §5.2: ein Bundle
  muss offen abrufbar sein)
- XEP-0060 §4.1/§8.9 Rollen je Knoten: `publisher` darf in einen fremden Knoten
  schreiben (die Meldung kommt trotzdem vom Eigentümer), `outcast` kommt an
  keinen Knoten und **verliert bestehende Abonnements**, `member` kommt an
  einen Knoten mit dem Zugriffsmodell `whitelist`. Der Eigentümer ist das Konto
  und nicht umtragbar; `publish-only` wird abgewiesen statt angeboten.
  Verwaltet werden die Rollen vom Eigentümer (§8.9), die eigenen listet §5.7
- Drittes Zugriffsmodell `whitelist`: Herein kommt, wen der Eigentümer
  ausdrücklich daraufgesetzt hat — `member` oder `publisher`. **Der Unterschied
  zu `presence`:** Eine Presence-Berechtigung entsteht nebenbei, eine Liste
  nicht. Der Ausschluss steht über beiden Modellen
- XEP-0060 §5.6 `<subscriptions/>`: alle Abonnements des Fragenden über alle
  Knoten, mit Kennung und Zustand, auf Wunsch auf einen Knoten eingeschränkt.
  **Nur die eigenen** — wer fremde aufzählen dürfte, erführe, wer sich wofür
  interessiert. Keine Abonnements sind eine leere Liste und kein Fehler
- XEP-0060 §8.8 Die Abonnenten eines Knotens — **die Gegenrichtung zu §5.6 und
  mit Absicht:** Dort werden fremde Abonnements verschwiegen, weil sie eine
  Auskunft über Menschen wären; hier lautet die Frage nicht „wo hängt dieser
  Mensch überall", sondern „wer hängt an meinem Knoten", und die beantwortet
  der Server dem Eigentümer. Jeder Eintrag nennt seine Kennung, derselbe JID
  also mehrfach — ohne sie liesse sich keines seiner Abonnements von dem
  anderen unterscheiden. Entfernt wird mit `subscription='none'`: mit `subid`
  genau eines, ohne `subid` alle dieses JIDs, denn der Eigentümer meint den
  Menschen und nicht die Buchführung. Was niemand findet, wird nicht entfernt,
  sondern abgewiesen. **Anmelden kann der Eigentümer nicht** — das ist genau
  das, was §6.1.3.1 auf der anderen Seite verhindert, und der eigene Knoten
  ändert nichts für den, dessen Postfach sich füllt. Ein `subscribed` für ein
  bestehendes Abonnement ist trotzdem gültig: Eine Liste, die sich nicht
  unverändert zurückschicken lässt, wäre kein Zustand
- XEP-0060 §8.8.4 **Wer beendet wurde, ohne zu fragen, erfährt es** — eine
  Meldung mit Knoten, JID und Kennung, und zwar **je erloschenem Abonnement
  eine**: Käme auf ein `none` ohne `subid` nur eine, wüsste der Empfänger von
  einer Kennung, dass sie erloschen ist, und von der anderen nichts. Ebenso
  beim Ausschluss (§8.9.4) — dort ohne die Rolle zu nennen: was er an dem
  Knoten ist, geht ihn nichts an, dass er ihn nicht mehr bekommt, schon.
  Gemeldet wird, was geschehen ist, nicht was angewiesen wurde; eine
  abgewiesene Anweisung meldet nichts ab. Ein `headline` und damit **nichts
  für die Ablage** (XEP-0160): Wer offline war, erfährt es nicht — und findet
  es beim nächsten Verbinden über §5.6, wo der Stand von jetzt steht und nicht
  der von damals
- XEP-0060 §4.5/§8.6 `authorize`: **Das einzige Modell, bei dem Abonnieren und
  Hereinkommen zwei Dinge sind.** Jeder darf fragen — das Fragen ist der
  Vorgang —, und die Antwort ist ein `pending`: die angenommene Frage und nicht
  die Zusage. Bis zur Genehmigung kommt nichts an, weder über ein Abonnement
  noch über die Presence, und abrufen lässt sich auch nichts. Der Eigentümer
  bekommt den Antrag als Formular vorgelegt (§8.6.1, `pubsub#allow` steht auf
  „nein" — ein Formular, das schon auf ja steht, macht aus dem Wegklicken eine
  Zusage) und beantwortet ihn entweder damit (§8.6.2) oder über die
  Abonnentenliste (§8.8.2). **Zwei Türen, ein Raum:** Die Liste ist die Sicht
  eines Verwalters, das Formular die eines Menschen. Ein „nein" auf eine Frage
  von vorhin beendet kein inzwischen zugesagtes Abonnement
- XEP-0060 §7.2 `<retract/>`: Ein einzelner Eintrag wird zurückgenommen — von
  dem, der auch veröffentlichen dürfte. **Wer schreiben darf, darf auch
  zurücknehmen**; einen Publizierenden von fremden Einträgen fernzuhalten
  hiesse, sich zu merken, wer welchen geschrieben hat, und ohne diese Ablage
  wäre jede feinere Regel bloss behauptet. Ein Eintrag, den es nicht gibt, wird
  mit `<item-not-found/>` abgewiesen, ein Knoten ohne Ablage mit
  `<unsupported feature='persistent-items'/>` wie beim Leeren. **Die Meldung
  geht denselben Weg wie eine Veröffentlichung** — je Abonnement, mit `subid`,
  und ein stillgelegtes bleibt still: Eine Rücknahme ist eine Zustellung und
  keine Nachricht über den Knoten. Der letzte zurückgenommene Eintrag lässt den
  Knoten stehen
- XEP-0060 §8.4/§8.5 Knoten löschen und leeren, beides nur für den Eigentümer.
  **Gelöscht wird der Knoten, geleert nur sein Inhalt** — nach dem Leeren
  veröffentlicht er weiter an dieselben Empfänger, nach dem Löschen an
  niemanden. Ein gelöschter Knoten nimmt Einträge, Einstellungen, Abonnements
  **und Rollen** mit: Blieben die Rollen stehen, erbte der nächste Knoten
  desselben Namens eine Ausschlussliste, die niemand mehr sieht. Ein Knoten
  ohne Ablage lässt sich nicht leeren (§8.5.3.2, `<unsupported
  feature='persistent-items'/>`) — ein `result` wäre die Auskunft, es sei etwas
  geleert worden. Beides wird gemeldet (§8.4.2/§8.5.2), und zwar **je
  Abonnenten einmal und ohne `subid`**: Es endet nicht ein Abonnement, sondern
  der Knoten; eine Kennung zu nennen hiesse, die anderen bestünden weiter. Eine
  zweite Meldung nach §8.8.4 kommt deshalb nicht hinterher
- XEP-0060 §6.3 Konfiguration je Abonnement als Datenformular (XEP-0004) mit
  **genau einem Feld**: `pubsub#deliver` legt dieses eine Abonnement still,
  ohne es zu beenden — und ein stillgelegtes fällt auch nicht auf die
  Presence-Zustellung zurück. Ein Feld, das im Angebot nicht stand, wird
  abgewiesen statt übergangen; ein `set` ohne Formular ebenso. Was der Server
  nicht kann, bietet er nicht an: Ein `pubsub#digest`, das nichts bewirkt, wäre
  eine Zusage ohne Deckung, und ausbleibende Zusammenfassungen sehen aus wie
  Ruhe
- XEP-0352 Client State Indication: Erklärt sich ein Client für inaktiv, hält
  der Server zurück, was warten kann — Presence (nur die letzte je Full-JID),
  Empfangsbestätigungen, Marker. Ein Chat State wird fallengelassen statt
  aufgehoben, denn ein „schreibt gerade" von vorhin ist beim Nachliefern keine
  verspätete Auskunft mehr, sondern eine falsche. Nachrichten mit Text, `iq`,
  Fehler und Nonzas gehen unverändert sofort hinaus
- XEP-0198 Stream Management mit **eigener, unabhängig implementierter**
  Zählung — der Server benutzt bewusst nicht dieselbe Hilfsfunktion wie der
  Client, sonst prüften die Tests beide Seiten mit derselben Logik
- Stanza- und Stream-Fehler auf Zuruf: `StanzaErrorIq(…)` und
  `session.SendStreamErrorAsync(condition)` — das Letztere beendet den Stream
  auch, wie RFC 6120 §4.9.1.1 es verlangt: Fehler schicken, `<close/>` nach
  RFC 7395 §3.6, Verbindung niederlegen
- Offline-Ablage nach RFC 6121 §8.5.2.2.1 und XEP-0160, mit XEP-0203-Stempel;
  `StoreOfflineMessages` schaltet auf den gleichrangig erlaubten Gegenweg um
  (`<service-unavailable/>` an den Absender). Ein `chat` mit ausschliesslich
  Tippstatus-Inhalt wird verworfen — die einzige Nachricht, die dieser Server
  stillschweigend fallen lässt, und zwar weil ein Tippstatus nichts verspricht
- `OnInternalError` meldet, wenn das Verarbeiten eines Frames mit einer Ausnahme
  endet — samt Frame. Danach endet der Stream mit `<internal-server-error/>`
  (RFC 6120 §4.9.3.8 und §4.9.1.1), gefolgt von `<close/>` nach RFC 7395 §3.6:
  Was der Frame ändern sollte, ist halb geändert, und ein Stream, über dessen
  Zustand die beiden Seiten verschiedene Vorstellungen haben, ist keiner mehr.
  Die Testsammlung hängt an das Ereignis eine Wache, die jede Meldung als
  Programmierfehler behandelt; `FailFrameHandling` erreicht den Weg absichtlich.
  Sie hängt **nicht** mehr daran, dass ein Fixture sie anmeldet: Jeder Server
  meldet seine Entstehung über `OnInstanceCreated` (internal), und die Wache
  findet ihn von dort aus — auch in einem Fixture, das es morgen gibt (D54)
- Schalter für Fehlerfälle: `CompleteCloseHandshake`, `RouteStanzas`,
  `BroadcastPresence`, `DeliverCarbons`, `AnswerPings`,
  `OfferStreamManagement`, `AnswerAckRequests`, `SwallowClientStanzas`
  (verwirft eingehende Stanzas, bevor sie gezählt werden — der einzige Weg zu
  einer Stanza, die die Leitung verlässt und trotzdem nicht ankommt),
  `SweepResumableStreams` (hält den Abräumer an — der einzige Weg zu einem
  Stream, dessen Frist abgelaufen ist, während er noch dasteht),
  `FailPings`, `FailDiscoInfo`,
  `FailBind`, `SessionRequired`, `ConflictOnUsedResource`,
  `CorruptScramSignature`, `OmitScramSignature` — die letzten beiden für die
  Gegenprobe zur zweiten Hälfte von SCRAM: ein Server, der das Passwort nicht
  kennt, kann die Serversignatur nicht erzeugen, und der Client muss die
  Anmeldung dann verweigern
- `DeliverAfterBind`: Frames, die der Server unmittelbar nach der Bind-Antwort
  schickt — also mitten in die Aufbauphase des Clients hinein. `{jid}` darin
  wird durch den gebundenen Full-JID ersetzt.

```csharp
var alice = await ConnectClientAsync("alice");
var bob   = await ConnectClientAsync("bob");

bob.OnMessage += m => Console.WriteLine($"{m.FromBareJid}: {m.Body}");
await alice.SendMessageAsync(bob.BareJid, "Hallo Bob!");
```

Verbindungsabrisse simuliert `Server.KillAllSessions()`, einzelne Resourcen
`Server.SessionOf(fullJid)!.Kill()`.

Weil das Zertifikat selbst signiert ist, vertraut ihm kein Rechner. Der Client
braucht deshalb eine eigene Prüfung; `Server.IsOwnCertificate` heftet den
Fingerabdruck genau dieses Servers an:

```csharp
var connection = new XMPPConnection(jid, passwort, Server.Uri)
{
    ServerCertificateValidator = Server.IsOwnCertificate
};
```

Eine Prüfung, die pauschal `true` liefert, wäre kürzer — sie nähme TLS aber die
Authentifizierung und liesse die Tests auch gegen eine fremde Gegenstelle
bestehen.

#### Was dem Server zum Produktivbetrieb fehlt

Der Name sagt es nicht mehr — bis vor kurzem hiess die Klasse `FakeXMPPServer`.
Sie ist als Gegenstelle für Tests und Entwicklung gedacht, nicht als
Server-Implementierung:

- **TLS ohne STARTTLS und ohne Zwang.** Der Server spricht `wss://` mit einem
  selbst signierten, zur Laufzeit erzeugten Zertifikat (RFC 6120 §5). Was fehlt:
  STARTTLS (§5.4), ein Weg ein eigenes Zertifikat zu hinterlegen, und die
  Möglichkeit `ws://` zu verbieten — `new XMPPServer(useTLS: false)` liefert
  weiterhin Klartext.
- **SCRAM ohne Channel Binding.** Angeboten werden SCRAM-SHA-256, SCRAM-SHA-1
  und PLAIN; die `-PLUS`-Varianten fehlen. ~~Ein unbekanntes Konto wird
  abgelehnt, bevor der Austausch beginnt.~~ Behoben: Der Austausch läuft auch
  für einen unbekannten Namen zu Ende und scheitert am Beweis (RFC 6120 §13.11,
  siehe D50). Der Serverschlüssel, aus dem die erfundenen Salts entstehen, lebt
  aber im Prozess — über einen Neustart hinweg wechseln sie, echte nicht. Bei
  **PLAIN** ist der Ablauf ohnehin gleich; dort unterscheidet sich nur die
  Laufzeit, weil ein vorhandenes Konto PBKDF2 rechnet und ein unbekanntes nicht.
- **Der Downgrade-Schutz ist ein Trust-On-First-Use.** `PinnedSaslMechanism`
  deckt jede Verbindung ab der zweiten; die allererste deckt nur, wer
  `MinimumSaslMechanism` selbst setzt. Und die Anheftung lebt im Objekt: Ein
  neuer Prozess fängt wieder ohne sie an.
- **Kein Anlegen von Konten über XMPP** (XEP-0077) und keine
  Passwortänderung — Konten entstehen nur über `AddAccount`.
- **Der Kontenspeicher ist unverschlüsselt.** `FileAccountStore` legt eine
  JSON-Datei ohne gesetzte Zugriffsrechte an. Passwörter stehen nicht darin,
  aber die abgelegten Schlüssel erlauben, eine Anmeldung zu prüfen.
- **Aufbewahrte Anfragen haben eine Obergrenze** (RFC 6121 §3.1.3,
  `MaxStoredSubscriptionRequests`, Vorgabe 100). Ist sie erreicht, wird die
  neue Anfrage verworfen — der Antragsteller erfährt davon nichts, und der
  Kontakt sieht sie nie. Das ist die vom Abschnitt selbst empfohlene Antwort
  auf die Erschöpfungsgefahr, aber es bleibt ein stiller Verlust.
- **Die Offline-Ablage liegt im Kontenspeicher und unverschlüsselt.**
  `FileAccountStore` schreibt die vollständigen Stanzas in dieselbe JSON-Datei
  wie die Zugangsdaten — Nachrichtentexte im Klartext, ohne gesetzte
  Zugriffsrechte. Ein echter Server trennt die beiden und hätte für die Ablage
  ausserdem eine Verfallszeit; hier bleibt eine Nachricht liegen, bis jemand
  sie abholt. Was ebenfalls fehlt: die Ablage einsehen und einzeln abholen,
  statt sie beim Anmelden über sich hereinbrechen zu lassen — XEP-0013 könnte
  das und ist bewusst nicht umgesetzt (siehe oben).
- **Eine Probe an ein unbekanntes Konto bleibt unbeantwortet.** RFC 6121 §8.5.1
  stellt `<unsubscribed/>` und Schweigen frei; dieser Server schweigt, damit ein
  unbekanntes Konto genauso aussieht wie ein vorhandenes ohne Berechtigung.
- **Eine Gegenstelle erreicht nur die Auskunft über den Server, nicht den
  Zustand einer Sitzung.** Ping und disco#info an die Serveradresse werden auch
  über die Servergrenze beantwortet (seit D36); Binding, Legacy Session,
  Carbons und der Roster gehören dagegen einer Sitzung oder einem Konto und
  bleiben für S2S unerreichbar — ein fremder Server, der danach fragt, bekommt
  `<service-unavailable/>`.
- **Zwei fremde Gegenstellen, nicht mehr.** Gegen Prosody 13 und ejabberd 24.12
  sind beide S2S-Richtungen und beide Ausweisverfahren geprüft (STARTTLS,
  SASL-EXTERNAL, Dialback nach XEP-0220 in beiden Rollen, XEP-0288). Beide
  Aufbauten stehen in `tools/`, die Tests dazu in
  `libs/Ratatoskr/RatatoskrTests/Federation/`; ohne die Server überspringen
  sie sich. Was der
  zweite Server zutage förderte, stand im ersten Lauf nicht: ejabberd kündigt
  Bidi im Namensraum des Freischalt-Elements an, und wir übersahen das Angebot
  darum. Ein dritter Server fände vermutlich ein Drittes.
- **Föderation.** Es gibt drei Wege über die Domain-Grenze:
  `DirectServerLinks` (in-process, für Tests, ohne jede Authentifizierung),
  `WebSocketServerLinks` und `TcpServerLinks` (beide mit TLS und Dialback nach
  XEP-0220). Was fehlt: DNSSEC — die SRV-Auflösung ist unbeglaubigt, und wo sie
  die Gegenstellenliste bei der Dialback-Prüfung ersetzt, wandert die
  Vertrauenswurzel vom Betreiber ins DNS. Ausserdem: SASL-EXTERNAL gibt es nur
  über TCP, nicht über WebSocket, und `id-on-xmppAddr` im Zertifikat wird nicht
  gelesen. Der TCP-Weg ist in beiden Richtungen gegen zwei fremde Server
  geprüft; der WebSocket-Weg bleibt auf Instanzen dieses Servers beschränkt.
- **Stream-Resume steht** (XEP-0198 Abschnitt 5). Der Server sagt die
  Wiederaufnahme zu (`<enabled id='…' resume='true'/>`, Kennung aus dem
  Zufallsgenerator), hebt einen abgerissenen Stream samt Zählern und
  ungesendeten Stanzas auf, stellt ihm weiter zu und schiebt seine
  `unavailable`-Presence auf, bis die Frist (`ResumptionTimeout`, Vorgabe 60 s)
  abläuft. Ein `<resume/>` wird nur von einem Stream angenommen, der auf
  dasselbe Konto angemeldet ist — die Kennung allein weist niemanden aus. Eine
  ordentliche Abmeldung (`<close/>`) wird nicht aufgehoben.
  Aufgehoben wird unabhängig von der Presence: Die Zusage gehört dem Stream,
  ein unsichtbarer Client behält sie also.
  **Die Abweisung nennt einen Stand nur, wo es einen zu nennen gibt:** `h`
  steht im `<failed/>` genau dann, wenn der abgelaufene Stream noch daliegt und
  dem anfragenden Konto gehört. Eine unbekannte Kennung bekommt kein `h` —
  geraten wird nicht —, und eine fremde erst recht nicht: Die Zahl verriete,
  dass es diesen Stream gibt und wie viel über ihn gelaufen ist (siehe D49).
- ~~**Fehlerbehandlung nur auf Zuruf.** Ausser den Schaltern oben erzeugt der
  Server keine Stanza-Fehler.~~ Überholt: Er erzeugt sie von sich aus, wo die
  RFCs es verlangen — `<bad-request/>` für einen unbekannten IQ-Typ,
  `<service-unavailable/>` für einen unzustellbaren Empfänger und für ein
  `groupchat` an ein Konto, `<remote-server-not-found/>` für eine unerreichbare
  Domain, `<item-not-found/>` für einen unbekannten disco-Knoten und
  `<jid-malformed/>` für ein `to`, das kein JID ist (D51). Die Schalter sind
  dafür da, die *übrigen* Fehlerwege zu erreichen. Unbekannte IQs bekommen
  weiterhin pauschal `<service-unavailable/>` statt einer Unterscheidung nach
  Ursache.

### Kryptografische Testvektoren

Die Implementierungen werden gegen die veröffentlichten Vektoren gerechnet,
nicht gegen sich selbst:

| Quelle | Was geprüft wird | Ergebnis |
|--------|------------------|----------|
| RFC 5802 §5 | SCRAM-SHA-1: client-first, ClientProof, ServerSignature | ✅ exakt reproduziert |
| RFC 7677 §3 | SCRAM-SHA-256: client-first, ClientProof, ServerSignature | ✅ exakt reproduziert |
| XEP-0115 §5.2 | Verification String `QgayPKawpkPSDYmwT/WM94uAlu0=` | ✅ exakt reproduziert |
| XEP-0115 §5.3 | Verification String `q07IKJEyjvHSyhy//CH0CxmKi8w=` (zwei Sprachen, ein Datenformular) | ✅ exakt reproduziert |
| RFC 4013 §3 | SASLprep-Beispieltabelle, alle sieben Zeilen | ✅ exakt reproduziert |
| RFC 7622 §3.5 | JID-Beispieltabellen: 15 gültige, 8 ungültige Adressen | ✅ reproduziert (Ausnahme: Beispiel 18, siehe oben) |
| RFC 3492 §7.1 | Punycode: elf Beispiele in acht Schriften | ✅ exakt reproduziert, in beide Richtungen |
| RFC 3454 Anhang A–D | Die StringPrep-Tabellen selbst | ✅ von `libs/Ratatoskr/tools/stringprep/generate.py` aus dem RFC erzeugt, nicht abgeschrieben |
| Unicode `DerivedBidiClass.txt` | Die Bidi-Klassen für RFC 5893 | ✅ von `libs/Ratatoskr/tools/unicode/generate-bidiclass.py` aus der Unicode-Datei erzeugt (15.1.0, 764 Bereiche) |
| XEP-0220 §2.1.1 | Dialback-Schlüssel `b4835385…d23df3` | ✅ exakt reproduziert |

Damit sind Hi/PBKDF2, ClientKey, StoredKey, AuthMessage, ClientSignature,
die XOR-Verknüpfung und die Server-Signaturprüfung gemeinsam abgedeckt.

Der Dialback-Vektor hat sich dabei besonders gelohnt: `SHA256(Secret)` geht als
**Hex-Zeichenkette** in den HMAC, nicht als Rohbytes, und die Domains stehen in
der Reihenfolge Ziel vor Absender. Beide naheliegenden Gegenlesarten liefern
einen in sich stimmigen, aber falschen Schlüssel — zwei Server, die sich
verschieden entscheiden, kämen nie zusammen, ohne dass einer von beiden einen
Fehler machte, den er selbst sehen könnte.

Die Vektorarbeit hat zwei Defekte aufgedeckt, die inzwischen behoben sind. Die
beiden Tests bleiben als Regressionstests stehen — dass sie greifen, ist per
Gegenprobe belegt: mit zurückgedrehtem Fix schlagen genau diese zwei fehl:

- `IterationCountFollowingNonceWithPadding_IsParsedCorrectly` — `ExtractValue`
  suchte mit dem unverankerten Muster `{key}=([^,]+)`. Endet die kombinierte
  Nonce auf `i==`, traf die Suche nach dem Iterationszähler dieses Vorkommen
  und lieferte `"="`; `Int32.Parse` warf dann eine `FormatException` statt
  einer `AuthenticationException`. Das Muster ist jetzt auf `(?:^|,){key}=`
  verankert.
- `Features_AreSortedByOctetOrder` — XEP-0115 §5.1 verlangt Oktett-Reihenfolge,
  `Order()` sortierte kulturabhängig (`'a'` vor `'B'` statt `'B'` vor `'a'`).
  Für die aktuelle Feature-Liste fallen beide Reihenfolgen zufällig zusammen,
  der offizielle Vektor allein deckte den Fehler also nicht auf. Jetzt
  `Order(StringComparer.Ordinal)`.

Dieselbe Fehlerklasse steckte in der Identitäten-Sortierung und ist mit
`Identities_AreSortedByOctetOrderIncludingName` ebenfalls behoben und abgedeckt:
sortiert wird jetzt oktettweise über genau die Zeichenkette
`category/type/xml:lang/name`, die auch in den Hash eingeht — vorher lief die
Sortierung nur über `category/type`, sodass bei gleichem Präfix die
Einfügereihenfolge stehenblieb. Der `xml:lang`-Platz bleibt leer, weil
`DiscoIdentity` kein `xml:lang` trägt.

Zum Festnageln des Client-Nonce trägt `SCRAMAuthenticator` eine
`internal`-Eigenschaft `FixedClientNonce`; ohne sie liessen sich AuthMessage
und Proof nicht reproduzieren. Sichtbar gemacht wird sie über
`InternalsVisibleTo` in `Ratatoskr.csproj`.

## Known limitations

Was davon in welcher Reihenfolge angegangen wird, steht im
[Arbeitsplan](../WORKPLAN.md).

### Architektur
- **Eigene erweiterte Angaben sind abschaltbar und standardmäßig aus.**
  `DiscoManager.LocalForms` fängt leer an. Was dort steht, erfährt jeder
  Kontakt ungefragt — Software, Version und Betriebssystem sind genau die
  Angaben, aus denen sich ein Gerät wiedererkennen lässt. Wer sie
  veröffentlichen will:

  ```csharp
  client.Connection.Disco!.LocalForms.Add(
      DiscoForm.SoftwareInfo(Software: "Jabber", SoftwareVersion: "0.1"));
  ```

  Der Inhalt geht in den angekündigten `ver`-Wert ein. Er lässt sich deshalb
  nur zusammen mit einer neuen Presence ändern — in der Zeit dazwischen kündigt
  der Client einen Hash an, den seine Antwort nicht mehr ergibt, und eine
  Gegenstelle, die nach XEP-0115 §5.4 nachrechnet, verwirft die Auskunft (zu
  Recht) als nicht belegt.
- ~~**Log-Ausgabe und Konsolen-UI überlagern sich.**~~ Behoben: Alles, was auf
  die Konsole geht, läuft über `ConsoleUI/ConsoleOutput` — Ereignisse,
  Systemmeldungen und das Protokoll über `ConsoleOutputLoggerProvider`. Die
  angefangene Eingabezeile weicht, die Ausgabe erscheint, die
  Eingabeaufforderung steht wieder da, und **eine Sperre** hält zwei
  gleichzeitige Ausgaben auseinander (D58).
- **XEP-0198 ist per Default an, samt Wiederaufnahme.** Die Zählung ist gegen
  Prosody 13 geprüft: nach einem vollständigen Sitzungsaufbau melden beide
  Seiten denselben Stand, und zwar auf den Zähler genau — nicht nur „die
  Warteschlange lief leer", was auch ein zu grosses `h` bewirkte. Nach einem
  Abriss knüpft der Client vor dem Resource Binding an den alten Stream an: die
  Full-JID bleibt, was während der Störung ankam, wird nachgeliefert, und die
  Kontakte sehen kein Verschwinden. Gelingt es nicht — Frist abgelaufen,
  Kennung unbekannt —, bindet er neu; nennt die Abweisung dabei einen Stand
  (`<failed h='…'/>`), gilt bis dorthin dasselbe wie bei einem `<a h='…'/>`:
  verarbeitet ist verarbeitet, und verloren ist nur, was darüber hinaus offen
  war. Geprüft gegen Prosody 13
  (`mod_smacks`) und ejabberd 24.12 (`mod_stream_mgmt`) - beide verhalten sich
  hier gleich.
- ~~Der Content-Namensraum wandert nur in einer Richtung mit.~~ Behoben: jede
  Stanza an einen Client trägt jetzt `jabber:client`, jede über die
  Domain-Grenze `jabber:server` (RFC 6120 §4.8.1, RFC 7395 §3.3.3). Vorher
  schickte der Server seinen Clients **gar keinen** Namensraum und reichte
  Fremdes unverändert als `jabber:server` durch.

## Ende-zu-Ende-Verschlüsselung (OMEMO, XEP-0384)

In der Konsole:

```
/omemo an                        einschalten
/omemo an <jid> <text>           verschlüsselt senden
/omemo fingerabdruecke           eigenen und bekannte anzeigen (Alias: fp)
/omemo vertrauen <jid> <geraet>  Gerät bestätigen
/omemo ablehnen <jid> <geraet>   Gerät ablehnen
```

Aufgebaut in sieben Etappen (D62–D68): Kryptobausteine gegen veröffentlichte
Prüfvektoren, X3DH, Double Ratchet, Drahtformat samt SCE-Hülle, PEP-Verteilung,
Sitzungsspeicher und die Verdrahtung.

**Was dabei entschieden wurde, und warum:**

- **Ein Gerät, für das es kein Bundle gibt, wird übersprungen — und genannt.**
  Nicht zu senden machte einen Menschen durch ein einziges kaputtes Gerät
  unerreichbar; unverschlüsselt zu senden wäre die schlimmste Antwort, weil
  der Absender dann glaubt, verschlüsselt zu haben. `SendEncryptedMessageAsync`
  gibt deshalb die übersprungenen Geräte samt Grund zurück, und die Konsole
  zeigt sie an
- **Ohne eingeschaltetes OMEMO wird geworfen**, nicht unverschlüsselt gesendet
- **Blind Trust Before Verification** als Vorgabe (`TrustNewDevicesBlindly`).
  Ein Verfahren, das vor der ersten Nachricht einen Fingerabdruckvergleich
  verlangt, wird nicht benutzt — und unbenutzte Verschlüsselung schützt
  niemanden. Wer einmal verglichen hat, merkt jeden späteren Wechsel
- **Ein geänderter IdentityKey stoppt die Nachricht.** Neu aufgesetztes Gerät
  oder Angreifer sind von aussen nicht zu unterscheiden; das ist keine
  Entscheidung, die ein Programm treffen kann
- Der Fingerabdruck wird in Achtergruppen angezeigt, damit ein Mensch beim
  Vergleichen die Stelle nicht verliert

### Die Grenzen, ausdrücklich

- **Gegen die Referenzimplementierung geprüft, nicht gegen einen echten
  Client.** Seit D69 läuft python-omemo (Syndace) als Gegenstelle mit — dieselbe
  Fassung `urn:xmpp:omemo:2` — und zwar in beide Richtungen: Sie nimmt unser
  Bundle an (und prüft dabei unsere Signatur), wir lesen ihre Nachrichten, sie
  liest unsere. Damit sind Bundle-Format, X3DH, Ratchet-Anfang und Drahtformat
  **gegen fremden Code** belegt. Nicht belegt bleiben die SCE-Hülle, das
  `<encrypted/>`-Element, die PEP-Knoten und der Verlauf eines Gesprächs über
  mehrere Nachrichten — und ein echter Client über eine echte Verbindung
  ohnehin nicht: Conversations, Dino und Gajim sprechen überwiegend noch OMEMO
  0.3.0. Siehe [das Orakel](../libs/Ratatoskr/RatatoskrTests/XEPs/Oracle/README.md)
- **Der Sitzungsspeicher ist nicht verschlüsselt.** Er enthält den geheimen
  IdentityKey, alle PreKeys und jeden Kettenschlüssel; wer die Datei liest,
  liest die Gespräche mit. Sie gehört an einen Ort, an den nur dieser Benutzer
  kommt
- **Die Punktarithmetik für XEdDSA ist nicht gegen Zeitmessung gehärtet.** Für
  einen Client auf dem Gerät seines Benutzers ist das die richtige Reihenfolge
  der Sorgen; für einen Server wäre es die falsche
- **Kein MUC** (XEP-0045) und damit keine Gruppenverschlüsselung
- Der Signed PreKey wird nicht selbsttätig gewechselt — `RotateSignedPreKey`
  gibt es, ein Zeitplan dafür nicht

### Funktionsumfang
- Kein Multi-User Chat (XEP-0045)
- Kein Message Archive Management (XEP-0313)
- **OMEMO (XEP-0384) ist fertig** — sieben Etappen, D62 bis D68. Siehe den
  eigenen Abschnitt weiter unten Und es gibt hier keinen fremden OMEMO-Client, gegen den sich das
  prüfen liesse — geprüft ist die Übereinstimmung mit dem Text, nicht mit der
  Wirklichkeit
- Kein HTTP File Upload (XEP-0363)
- ~~Keine Client State Indication (XEP-0352)~~ Umgesetzt in D61, auf beiden
  Seiten — siehe die Tabelle oben
- Kein Flexible Offline Message Retrieval (XEP-0013) — die Ablage kommt beim
  Anmelden vollständig heraus und lässt sich nicht einsehen oder einzeln
  abholen. Bewusst so: Die XSF führt XEP-0013 als *Deprecated* (siehe D37)
- ~~Der Client liest den XEP-0203-Stempel nicht; eine nachgereichte Nachricht
  erscheint mit ihrer Empfangszeit, obwohl der Server den Verzug mitteilt~~
  Behoben in D59: Sie erscheint mit Datum und dem Vermerk „(nachgereicht)"
- **Kein TCP-Transport** — der Client spricht ausschliesslich XMPP über
  WebSocket (RFC 7395). Die Fabrikmethode `CreateTcp`, die eine `tcp://`-URI
  erzeugte und dabei funktionslos war, ist entfernt: Eine öffentliche Methode,
  die nicht funktionieren kann, ist schlechter als keine. Ein echter
  TCP-Transport steht unter „Optional" (siehe [WORKPLAN.md](../WORKPLAN.md),
  D48): Prosody, ejabberd und der Testserver bieten WebSocket an, also fehlt er
  niemandem — die Bausteine (`XmlStreamSplitter`, STARTTLS) gibt es auf der
  S2S-Seite bereits.

### Ungenutzte API-Fläche

**Derzeit keine.** Die Liste stand hier, seit es sie gab, und ist in D57
abgearbeitet — jeder Eintrag entweder benutzt oder gestrichen:

| Member | Entscheidung |
|--------|--------------|
| `RosterStanzaBuilder.GetRoster` | **benutzt** — `XMPPConnection` setzte dieselbe Anfrage daneben von Hand zusammen |
| `RosterStanzaBuilder.Unsubscribe` | **benutzt** — über das neue `CancelSubscriptionAsync`, den vierten Übergang aus RFC 6121 §3 |
| `DiscoInfo.HasFeature` | **benutzt** — von einem Test, der die Frage vorher an der Merkmalsliste vorbei stellte |
| `MessageReceipt` | gestrichen — der Typ dokumentierte selbst, dass er nirgends erzeugt wird |
| `ReceiptTracker.GetTimedOutMessages` | gestrichen — es gibt keine Frist, die ablaufen könnte |
| `PubSubManager.OnSubscriptionResult` | gestrichen — nie ausgelöst, und die einzige Warnung des Baus |
| `PubSubBuilder.Retract` / `DiscoverNodes` | gestrichen — zwei Bausteine ohne Aufrufer, wiederherstellbar an einem Nachmittag |
| `DiscoInfo.Supports*` (fünf Stück) | gestrichen — Abkürzungen für `HasFeature` mit eingebautem Namensraum |
| `CarbonManager.DisableIq` | gestrichen — der Client schaltet Carbons im Aufbau ein und bietet keinen Schalter |
| `StreamManagementManager.ResumeAsync`, `GetUnackedStanzas`, `OnStanzasLost` | **war veraltet** — alle drei werden längst benutzt |

Die letzte Zeile ist der Grund, warum eine solche Liste keine Dauereinrichtung
sein sollte: **Sie veraltet in die falsche Richtung** und behauptet ungeprüft,
was inzwischen geprüft ist. Dasselbe galt schon für
`EntityCapsManager.GetCachedInfo`, das hier stand, während
`CapsExchangeTests` längst darüber prüfte.

## Lizenz

Apache License, Version 2.0 — siehe [LICENSE](../LICENSE).

Copyright (c) 2010-2026 GraphDefined GmbH &lt;achim.friedland@graphdefined.com&gt;
