# Work plan

What is open on the client and the server, in what order that makes sense and
why. The detailed description of the individual gaps stands in
[Jabber/README.md](Jabber/README.md) — here stands only what is to be **done**.

State: 2026-07-27

---

## Done

| What | Evidence |
|-----|-------|
| Split into one class per file, uniform namespace, licence header | `e42c684` |
| `XMPPClient` as a real client class, `Program.cs` nothing but console UI | `e42c684` |
| `ILogger` instead of `Console.WriteLine` in the library | `e42c684` |
| Send lock, CTS leak, roster push check, close handshake timeout | `e42c684` |
| `Jabber.Tests` with `XMPPServer` as the far side, multi-client scenarios | `e42c684` |
| SCRAM and caps test vectors from RFC 5802/7677 and XEP-0115 | `e42c684` |
| SCRAM `ExtractValue` anchored, caps sorting octet by octet | `78fdb1c` |
| XEP-0198 counts correctly (both directions, nonzas, overflow) | `78fdb1c` |
| `XMPPServer` into the main project, "fake" out of the type names | `78fdb1c` |
| `#region Usings` in every file | `78fdb1c` |
| RFC 6120 §8.2.3: unanswered IQs get `<service-unavailable/>` | `87f3dd6` |
| RFC 6120 §8.3/§4.9: stanza and stream errors are evaluated | `0249de1` |
| Stanza frames and roster over `XElement` instead of regex | `15a11aa` |
| `message` and `presence` payloads over `XElement` (XEP-0085/0115/0184/0280/0333) | `107aa87` |
| `iq` payloads over `XElement` (XEP-0030/0060/0199); raw text parameters gone | `39cb6fb` |
| Setup phase untangled: IQ correlation instead of discarding, negotiation over `XElement` | `cc9dccb` |
| S3: presence only to subscribers, presence probe, state on login | `4fe23cd` |
| S3c: sign-off at the end of the connection, on a break as well | `fdb8c3b` |
| S3b: subscription handshake, a roster set leaves the subscription alone | `590d38c` |
| The client evaluates `subscribed`/`unsubscribed`/`unsubscribe` instead of reading them as presence | `a5bc49d` |
| Resource settable, a `<conflict/>` leads to a second bind without a wish | `2f6f830` |
| One test spent six minutes in twenty reconnects | `4a2b3b6` |
| S1: transport on Hermod's WebSocket server, the server speaks `wss://` | `a92583e`, `b97db5e`, `2ebc805` |
| S2: credentials derived instead of in the clear, SCRAM on the server, account store | `d54dacb`, `c35ae85`, `d29dc3c` |
| A sign-off was remembered as the last presence and handed in later — cause of the sporadic failure | `bccf648` |
| S4: domain branch, error path, federation of two servers (without a real transport) | `d9c4333`, `323795f` |
| S4b-1: S2S protocol layer without a transport (`S2SStream`) | `f0a4bbd` |
| S4b-2: WebSocket S2S over real sockets together with TLS | `8e0aec3` |
| S4b-3: dialback (XEP-0220) against the vector of the XEP, the domain shown instead of claimed | `c92560d`, `a60631c` |
| S4b-4: framing exchangeable, XML splitter, TCP with `jabber:server` streams | `a24d1f2`, `e0d88f4` |
| S4b-6: STARTTLS (RFC 6120 §5.4) together with downgrade protection | `f4a9c80` |
| S4b-7: SASL EXTERNAL (XEP-0178) over the TLS certificate | `031f8ca` |
| S4b-8: SRV resolution (RFC 6120 §3.2, RFC 2782) | `0d1391f` |
| S5: cross-domain subscriptions (RFC 6121 §3) | `a94b416` |
| S6: subscription pre-approval (RFC 6121 §3.4) | *(this commit)* |

Every one of these corrections is secured by mutation testing: the fix turned
back, checked that exactly the responsible tests fail, the fix put in again.
Current state of the suite: **709 tests, 0 failures** in a good three minutes,
and since the switch of the default it runs with XEP-0198 negotiated. Skipped
is what has nothing to check without a foreign far side — six federation tests
that can run only inside WSL — as well as one that checks a property which
exists only in STARTTLS operation.
Six named exceptions, where a mutation stays green: the two lines in the
WebSocket connection teardown (see S4b-2), the comparison in
`DialbackKey.Verify` over `FixedTimeEquals` (a timing side channel is not
observable functionally), the slot identity in the connection cache
(see S4b-3), the moment of the SASL pinning (see D1), the shortcut over the
empty offline store (see D14) and the resetting of
`_lastConnectError` (see D31). There were six: the question of origin
before the `<sent>` carbons in the offline branch (D15) survived only because
its throw vanished in the `catch` while a frame was being processed — since D18
it is reported, and six tests strike the mutation down.

---

## The server is to become a real server

`XMPPServer` came about as a far side for tests. It is to lose the image of a
mere test server — for that three things were missing, the first of them is
done now, and a fourth would be the proof that it works. The complete list of
gaps stands in
[Jabber/README.md](Jabber/README.md#what-the-server-lacks-for-production-use).

### S1. TLS ✅

Done. The server speaks `wss://` with a self-signed certificate, as RFC 6120 §5
demands it; the whole suite runs over it. Implemented in four steps: `a92583e`
(reference to Hermod), `b97db5e` (transport), `2ebc805` (TLS), plus `4a2b3b6`
as a find along the way.

The transport is delivered by Hermod's `AWebSocketServer` — `HttpListener` and
the hand-written receiving loop are gone. `XMPPServer` does not inherit it but
holds a private derivation that overrides `ProcessTextMessage`; that way its
public surface stays small and all the tests went on compiling unchanged.

What lay differently than expected during the rebuild:

- Not one name collision but two: besides `WebSocket` also `IPAddress`. Both
  aliases have to stand **inside** the namespace declaration — at the level of
  the compilation unit the namespace member wins.
- Receiving does not run over `OnTextMessageReceived` but over the overridden
  method `ProcessTextMessage`; the event belongs to the example class
  `WebSocketMirrorServer`, not to the base class.
- The constructor parameter is called `TCPPort`, not `HTTPPort`.
- Close, ping and subprotocol negotiation were **no** problem — the suite was
  green on the first full run. The only real deviation: Hermod always answers a
  close frame and offers no switch against it. `CompleteCloseHandshake = false`
  therefore delays the answer instead of suppressing it.

**What stayed open about it:**

- No STARTTLS (RFC 6120 §5.4), and TLS is not enforced — whoever builds the
  server with `useTLS: false` still gets `ws://`.
- The certificate is self-signed and created at runtime. For operation there
  would have to be a way to deposit one of one's own.
- The original side effect is still outstanding: the server still offers PLAIN
  alone, so the SCRAM path of the client is as before tested only against the
  RFC vectors.

### S2. Permanent account management ✅

Done in three steps: `d54dacb` (credentials), `c35ae85` (SCRAM on the server),
`d29dc3c` (account store).

Passwords no longer lie in the clear but as what RFC 5802 §3 provides for:
salt, iteration count and per mechanism `StoredKey` and `ServerKey`.
`IXMPPAccountStore` carries accounts and rosters; `InMemoryAccountStore`
remains the default, `FileAccountStore` writes a JSON file.

**The side effect from S1 is thereby redeemed:** the server offers
SCRAM-SHA-256, SCRAM-SHA-1 and PLAIN, and because the client takes the
strongest by itself, the whole suite runs over SCRAM-SHA-256. The SCRAM path of
the client is thereby checked integratively for the first time — its check of
the server signature in particular, for which there was no test before that
would have noticed its failure.

**What stayed open about it:**

- **No channel binding** (`SCRAM-SHA-*-PLUS`). The GS2 header is checked for
  agreement, and RFC 5802 §6 demands no more than that of a server without
  channel binding either.
- ~~**An unknown account is refused before the exchange begins.** With that the
  server betrays whether an account exists; RFC 5802 §7 recommends carrying on
  with a made-up salt.~~ ✅ done in D50 — and the source given was wrong: §7 of
  RFC 5802 is the formal syntax, and the RFC recommends nothing about this. The
  recommendation stands in RFC 6120 §13.11.
- **The account file is unencrypted** and its access rights are not set. The
  keys stored are no passwords, but they do allow a login to be checked.
- **No creating of accounts over XMPP** (XEP-0077 In-Band Registration) and no
  password change.
- The iteration count stands at 4096, the lower bound from RFC 7677 §4. Too
  little for operation; settable per account.

### S3. Presence only to subscribers ✅

Done. Undirected presence now goes only to contacts with `from` or `both` and
to our own further resources; to that come presence probes and the handing in
of the contact state on login.

### S3b. Subscription handshake (RFC 6121 §3) ✅

Done. The four steps change the rosters of both sides and set off roster
pushes; `ask='subscribe'` holds a pending request fast. After the acceptance
the current presence goes to the applicant at once (§3.1.5), after a withdrawal
an `unavailable` (§3.2.2). A roster set no longer touches the subscription
state (§2.3).

What stayed open about it:
- ~~Pre-approval (§3.4) is missing~~ ✅ done in S6.
- ~~A request to an account that is not connected at the time is not kept
  (§3.1.3)~~ ✅ done in S7.

### S3c. `unavailable` at the end of the connection ✅

Done. When a session ends — orderly, broken off or at an exception — the server
signs the resource off with the same recipients that got its sign-on. If the
client has signed off itself, the repetition is left out.

### S4. Two servers, two clients, one message ✅ (routing) / ⚠️ (transport)

The target picture stands: two `XMPPServer` instances with different domains, a
real `XMPPClient` at each, and a message goes from one to the other — together
with the answer back and together with presence. Done in two steps: `d9c4333`
(domain branch and error path), `323795f` (federation).

**Decided:** routing and addressing first, the transport later. `IServerLinks`
is the place where it is put in; `DirectServerLinks` connects two servers in
the same process.

What came along with it: a stanza to a foreign domain used to vanish without a
trace. Now a `<remote-server-not-found/>` comes back (RFC 6120 §10.4.3, the
condition from §8.3.3).

**What stays open — and the reason why this is no ✅ on its own:**

- **There is no real transport.** `DirectServerLinks` has no stream, no TLS, no
  dialback and no authentication: the domain a far side may speak for is simply
  claimed. For operation that is nothing.
- **No dialback (XEP-0220) and no SASL EXTERNAL.** The sender check on the way
  in is there and sharp — it is exactly what a real transport builds on
  afterwards — but there is nothing that shows the claim of the far side to be
  true.
- ~~Cross-domain subscriptions are missing~~ ✅ **done (S5).** The handshake now
  runs across the border as well.
- ~~No resolution over DNS~~ — done in S4b-8.

### S4b. The real S2S transport

**Decided: both.** Not the one *or* the other — TCP for the federation with
existing servers, WebSocket for routes between two instances of this server.

| | TCP 5269 | WebSocket |
|---|---|---|
| Framing | one open `<stream:stream>`, stanzas as child elements | one frame = one stanza |
| TLS | STARTTLS **in** the stream (RFC 6120 §5.4) | TLS under the handshake, before it |
| Finding | DNS SRV `_xmpp-server._tcp` (RFC 6120 §3.2) | no standard, configuration by hand |
| Far sides | ejabberd, Prosody, everything | only another instance of this server |

**S4b-1 ✅ The protocol layer, without a transport under it.** `S2SStream` (new,
`Jabber/Server/S2SStream.cs`) knows neither socket nor WebSocket frame: it gets
incoming frames handed to it as strings and sends outgoing ones out over a
function. Both roles (`Initiate`/`Accept`) master the `<open/>` handshake under
RFC 7395 §3.4, the stream ID given out by the recipient (the anchor for
dialback), stanza in and out, `<close/>` and stream errors.
The stream is directed (RFC 6120 §4.1) — an outgoing stream takes no stanzas,
that would be XEP-0288 and negotiated, not assumed.

`ReceiveFromRemoteAsync` has got a twin, `AcceptFromRemoteAsync`, that says as a
`RemoteStanzaResult` *why* something was refused. That was necessary because the
refusals now weigh differently: a `from` the far side may not speak for ends the
stream with `<invalid-from/>` (RFC 6120 §8.1.1.1); a recipient on a third domain
costs only the one stanza. `DirectServerLinks` did not know this difference — it
could only discard, never end the stream.

**S4b-2 ✅ WebSocket transport.** `WebSocketServerLinks` (new,
`Jabber/Server/WebSocketServerLinks.cs`) is `IServerLinks` over a real socket:
incoming a branch of its own on `AWebSocketServer` on a second port with the
subprotocol `xmpp-server`, outgoing `ClientWebSocket` with a connection cache per
domain. `WebSocketServerLinks.Connect(a, b)` wires up like
`DirectServerLinks.Connect`, only with real addresses and a pinned certificate
instead of a mere object reference. `WebSocketFederationTests` drives the same
target picture as `FederationTests`, this time over real sockets together with
TLS.

A stream error now ends the WebSocket connection as well, not only the XMPP
stream (RFC 6120 §4.9 demands exactly that) — otherwise a connection would stay
open on which nothing happens any more on the protocol side. Honestly noted:
this part and the symmetric exit of the receiving loop mesh with each other in
the current test setup (the one side closes actively, the other reacts to the
regular WebSocket close as it is), so that a mutation test which turns back only
one of the two places does not point reliably at exactly this line. Both stay,
because RFC 6120 §4.9 demands the teardown of the connection independently of
whichever other mechanism — only the test sharpness for it is still missing.

**What WebSocket S2S can do now and what it cannot:** connect, TLS, a stanza
there and back, a sender check with a consequence (stream *and* connection end),
and since S4b-3 a shown far-side domain. What is still missing: the resolution
over SRV instead of over a configuration list, the behaviour when two servers
dial each other at the same time (double connections), and which transport is
chosen when a domain would be reachable over both. SASL EXTERNAL has been there
since S4b-7, but only on the TCP route — over WebSocket dialback stays the only
way.

**S4b-3 ✅ Dialback (XEP-0220).** The domain of the far side is now shown instead
of believed. `DialbackKey` computes the key under XEP-0220 §2.1.1 (procedure from
XEP-0185), checked against the published vector — `SHA256(Secret)` goes into the
HMAC as a **hex string**, not as raw bytes, and the order is the target before
the sender domain. Both other readings yield a coherent but wrong key.

`S2SStream` masters all three roles: the server building up identifies itself
unasked with `<db:result/>`, the accepting one has the key checked and answers
`valid`/`invalid`, the authoritative one recomputes a foreign `<db:verify/>`.
Before a passed dialback the stream carries no stanza — that is the line which
makes the exchange a safeguard in the first place (XEP-0220 §1).

**Where the value sits:** `WebSocketServerLinks.VerifyDialbackKeyAsync` does not
ask the one that is just now identifying itself, but the address *this* server
has deposited for the sender domain — over a short-lived connection of its own.
Whoever falsely gives themselves out as `left.example` is therefore never asked
themselves; asked is the real `left.example`, and it does not know the key. In
place of the DNS resolution of the XEP steps the far-side list of the operator.
For the purpose that is rather stricter than DNS (which is unauthenticated), but
it does not fill itself: a domain without a deposited address cannot be checked
and is therefore refused.

Two errors came to light while doing this, both older than this step:

- **Hermod's `WebSocketServerConnection` compares itself over `LocalSocket`** —
  and that is the same one for a listener for every accepted connection. An
  ordinary `Dictionary` thereby holds *all* incoming connections to be one and
  the same: the second got the stream of the first together with its sending
  function on a socket long since closed. `XMPPServer` has always got out of the
  way of that with `ReferenceEquals`; the S2S entrance does it now as well.
- **The connection cache cleared up only when the setup had already been
  finished successfully.** Did the stream die while still being set up — with
  dialback the normal case, because the setup takes several round trips —, the
  entry stayed for ever. Now it is cleared up over the identity of the slot.

**S4b-4 ✅ TCP as a second framing.** `TcpServerLinks` speaks `jabber:server`
streams over TCP (RFC 6120) — the same protocol layer, under it
`TcpStreamFraming` instead of `WebSocketFraming` and `XmlStreamSplitter` instead
of finished frames. `TcpFederationTests` checks the same as the WebSocket version
and runs green.

**The answer to the question from S4b-1: no, `S2SStream` did not stay
unchanged.** In six places RFC 7395 sat fast in the code — the two `<open/>`
sendings, the `<close/>`, the two recognitions for those, and, most
inconspicuously, an `XElement.Parse` on the stream header. Over TCP the header is
an *open* tag; every TCP connection would have failed with `<bad-format/>`.
The abstraction had taken on the shape of its first implementation, exactly as
noted here as a risk. What has held is all the rest: handshake, stream ID,
dialback, sender check, error handling, life cycle — and that is now shown
instead of claimed, because `S2SStreamTests` drives the same class with both
framings, without a socket.

Confirmed along the way: the decision from S4b-3 to read dialback elements over a
regular expression instead of over an XML parser pays off here. A `<db:result/>`
over TCP is not well-formed taken by itself — its prefix hangs on the root
element.

A find that only a measurement delivers: the first delivery took 4167 ms instead
of 82 ms. Not TLS, but `localhost` — the name resolves to IPv6 first, the
listener binds the IPv4 loopback, and each of the two connections (stanza stream
and dialback query) paid about two seconds of fallback. Everything worked, only
slowly; no test would ever have gone red.

**What stays open about S4b-4:**

- ~~No STARTTLS~~ ✅ **done.** `TcpTlsMode` chooses between plain text, TLS from
  the first byte on and STARTTLS under RFC 6120 §5.4; the default is STARTTLS.
  The negotiation stands in the transport and not in `S2SStream` — the stream
  before TLS is a throwaway stream whose state is discarded after the encryption
  (§5.4.3.3), and so the protocol layer gets no opportunity at all to carry
  anything over out of it. `TcpFederationTests` has run twice since then, once
  per mode of operation.
- ~~**No run against ejabberd or Prosody.**~~ ✅ made up for in S8 — and the gap
  was bigger than supposed, see there.
- **No SRV resolution**, far sides are entered by hand.

**S4b-7 ✅ SASL EXTERNAL (XEP-0178).** The domain of the far side is shown over
its TLS certificate instead of over a query back.
`TcpServerLinks.UseSaslExternal` asks for a client certificate for that;
`CertificateIdentity` says which domains it holds for. The difference is
measurable from outside and is checked that way too: with SASL EXTERNAL
`DialbackVerificationCount` stays at zero, without it it rises. The number of
connections is no use for that — other things run over the border as well, among
them the automatic receipt of the client, and it is on exactly that that the
first version of this test failed.

Deliberately strict: is there a SAN extension, then the common name no longer
counts (RFC 6125 §6.4.4) — otherwise a certificate with a fitting CN and harmless
SANs would suffice. Wildcards do not hold. After a `<failure/>` there is no
falling back to dialback: whoever wanted to identify themselves by certificate
and was refused has a problem that a weaker procedure covers up instead of
solving.

What came out while doing this: the stream restart after a successful SASL
(RFC 6120 §6.4.6) needs **two** things, and both were missing at first. The XML
splitter has to be reset, otherwise it holds the second `<stream:stream>` to be a
child element of the first and waits for ever for its closing tag — the
connection would stand still without anything looking broken. And "identified"
alone does not suffice as a starting signal: for a moment the stream is
identified and nevertheless not open, and whoever sends there loses the stanza
silently. For that there is now `WaitUntilReadyAsync`.

**What stays open about S4b-7:**

- **`id-on-xmppAddr` is not read** (OID 1.3.6.1.5.5.7.8.5), although XEP-0178
  names it as the intended form. It sits as an `otherName` in the SAN, and the
  library enumerates dNSName and IP addresses alone. A far side that identifies
  itself *only* over that is refused, although it is in the right.
- **Only on the TCP route.** Over WebSocket dialback stays the only way.
- **The chain is not checked against a CA.** `CertificateIdentity` says *what
  for* a certificate is issued, not whether it is to be trusted — that is decided
  by the deposited check in the TLS handshake, in the test setup a pinned
  fingerprint.

**S4b-8 ✅ SRV resolution (RFC 6120 §3.2.1).** Far sides no longer have to be
entered by hand. `DnsS2SAddressResolver` asks `_xmpp-server._tcp.<domain>` and
falls back without an entry to the domain itself; `SrvSelection` brings the
targets into the order from RFC 2782. An entry by hand still goes first - a
decision of the operator weighs heavier than a piece of information out of the
network.

The part critical to the selection is the weighting: it is **no** sorting by
weight, but a weighted draw without replacement. Whoever sorts descending instead
sends all traffic to the strongest machine, and the load spreading never takes
place - that would be noticed only in operation, and even there only by someone
who looks at the utilisation. The source of randomness is therefore settable, so
that the course of events stays checkable.

**Checked against a real DNS server**, not against a rebuilt answer: Hermod
brings one along, `InMemoryDNSZone` takes the entries, and the query runs over
real DNS packets. That paid off at once - `DnsFederationTests` wires up two
servers quite without a list and uncovered in doing so that the **dialback query
back** did not use the resolver at all. It looked only into the far-side list;
with a rebuilt resolver that would never have been noticed, because no test would
have got by without a list.

**What that means for the root of trust, and it is a worsening:** until now there
stood in the dialback check the list of the operator alone, and it drew its
sharpness from exactly that. Is the authoritative address looked up over DNS,
then dialback is only as reliable as the resolution - that is how XEP-0220 is
meant, but it is less than before. Whoever does not want that leaves
`AddressResolver` null and enters their far sides.

Unchanged it holds: **the certificate is checked against the domain sought**, not
against the machine name from the SRV entry (RFC 6120 §13.7.2.1). Otherwise an
attacker who can forge DNS would bring the yardstick along with them. For that
there is a test of its own, and the mutation for it is caught.

**What stays open about S4b-8:**

- **No `_xmpps-server._tcp`** (XEP-0368, direct TLS without STARTTLS). The
  choice between the two services would be a decision of its own.
- **No DNSSEC.** Without that the resolution stays unattested; it says where a
  connection goes, never with whom.

**Two things that must not go under while doing this:**

- **The weaker way sets the level.** Both transports have to show the domain of
  the far side equally well. `S2SStream` can still be built without dialback
  (`RequiresDialback == false`) — that is meant for `DirectServerLinks` and for a
  later SASL EXTERNAL way, not as a shortcut. The WebSocket transport switches it
  off nowhere.
- **Dialback sticks to the stream.** XEP-0220 is defined over XML streams and
  hangs on the stream ID. Over WebSocket there is no `<stream:stream>`, but
  `<open/>` with `id` (RFC 7395 §3.4) — `S2SStream.StreamId` carries it.
  That works, but is a determination of our own that nobody besides us knows;
  whether TCP carries the same layer unchanged is decided in S4b-4.

---

### S5. Cross-domain subscriptions ✅

The handshake from RFC 6121 §3 assumed that the same server has both rosters in
hand: the outgoing way tended both halves, the incoming one none at all.
A subscription presence from outside was only handed through to the client - it
arrived, but the server forgot it, and the answer found no entry before it that
it could have changed.

Now each side tends exactly **one** half, namely its own, and the agreement
arises solely out of both laying out the same sequence of stanzas differently:
the one sets `from`, the other `to`. To guess the other half would be wrong —
over the border one learns of one another only what is expressly sent.

Implemented are the four transitions (§3.1.6, §3.2.3, §3.3.3) and the
self-answering from §3.1.4: may the applicant see the contact anyway already,
then that one's server answers itself instead of asking the user again. Without
that an applicant whose roster got lost would never come right again without
bothering the contact.

Besides that `RouteToAsync` now addresses outgoing stanzas centrally. Inside a
server it knows itself whom it distributes to; over the border the `to` is all
the far side has.

**What stays open:**

- ~~A request to an account that is not connected at the time is not kept
  (§3.1.3)~~ ✅ done in S7 — and for both cases in the same place.
- The central addressing in `RouteToAsync` is held fast by no test; see the note
  in the code.

### S6. Subscription pre-approval ✅

RFC 6121 §3.4: to let a contact in before they ask. The section distinguishes
four cases, and all of them hang on the same question — is there a request or
not. The same `<presence type='subscribed'/>` is once an agreement and once a
pre-approval; the stanza looks the same in both cases, the difference sits solely
in the roster of the sender.

For that the server first had to learn to note down open requests. `RosterEntry`
got `PendingIn` beside `Ask` — the two directions of the same question: the one
holds fast that *we* have asked, the other that *there was an asking*. Without
both, §3.4 could not be implemented at all. To that comes `Approved`, which
appears as `approved='true'` in the roster result and push. (`PendingIn` has
vanished again in S7; the fact has lain wholly elsewhere since then.)

The half that is easy to overlook: at a pre-approval the `subscribed` may
**not** go out (cases 3 and 4). Did it go out all the same, the contact would get
an agreement to a question they never put, and their server would build a
subscription out of it that their user knows nothing about. The other way round a
pre-approved request may not be delivered to the user in the first place — the
server answers for them.

The incoming `subscribe` now runs for local and foreign origin through the same
place. The decision does not hang on where the request came from, but solely on
the roster of the recipient; to take it twice would mean creating two
opportunities to take it differently.

**The client has got a half of its own.** `AcceptSubscriptionAsync` breaks off
without an open request and puts a counter-request — both are wrong for a
pre-approval. `PreApproveContactAsync` does neither the one nor the other and
refuses of its own accord when the server has not announced the feature (§3.4.1
demands exactly that).

**What stayed open:** a request to an account not connected at the time was
still not kept (§3.1.3) — done in S7.

### S7. Kept subscription requests ✅

RFC 6121 §3.1.3, rule 4: whoever is not connected at the time is to get their
requests all the same. Up to here they were lost without replacement, and
unnoticed on both sides at that — the applicant saw `ask='subscribe'` in their
roster and waited for an answer, the contact had never learned that they were
asked.

**Kept is always, not only when nobody is there at the time.** The rule demands
the delivery to *every* resource the contact still creates afterwards, until they
agree or refuse. To keep a request only when by chance nobody was connected
missed exactly the frequent case: signed on, but just not looking, then signed
off. With that the distinction of cases falls away too — there is no offline
branch, but one way.

**`RosterEntry.PendingIn` is gone again.** The state from S6 was a yes/no for the
same fact that now lies wholly in `XMPPAccount.PendingSubscriptionRequests`: the
kept request *is* the open request. Two places for one fact run apart sooner or
later; §3.4.2 has asked the same place since then that §3.1.3 fills. The asking
and the settling are one step in doing so (`ForgetSubscriptionRequest` delivers
whether something was there), so that they cannot be separated.

**And the roster stays clean.** The security warning of the same section forbids
a roster entry for an applicant that has not yet been agreed to — until now one
arose, with `subscription='none'`. Whoever can write arbitrary strangers into
foreign rosters can fill them up.

**The stanza is stamped instead of built anew.** `HandleSubscriptionAsync` used
to put together a fresh `<presence …/>` and thereby threw the `<status/>` away —
the reason with which a human decides about the agreement. Rule 4 demands
however the *complete* stanza, "including any extended content contained
therein"; without this change the appeal to it would have been wrong.

**Two limits, both out of the section itself.** Per sender exactly one request
stays standing, and the first at that — otherwise whoever asks last would
determine what the contact gets to see, and could exchange it as often as they
liked (appendix A, table 6 says about it: do not deliver again). To that
`MaxStoredSubscriptionRequests` per account, default 100: the security warning
expressly advises an upper bound, because what strangers send is kept. Is it
reached, then the new request is discarded instead of a kept one being displaced
— the other way round the real request of an acquaintance could be pushed out on
purpose.

**Incidental find:** `FileAccountStore` did not write `Approved` along at all
since S6 — a pre-approval survived no restart. Now both persist, pre-approval and
kept request; "kept" would mean "until the next restart" otherwise.

14 mutations, 11 dead at once. The three survivors were instructive:

- *Handing in later at every presence instead of at the *becoming* available*: no
  test saw the difference. In operation every change to "away" would have put the
  same unanswered request up again. Held fast by
  `AStatusChange_DoesNotRepeatTheRequest`.
- *A repeated request displaces the kept one*: invisible, because the only test
  for it had signed the contact off — then refusing and replacing look the same.
  With a connected contact every accepted request goes out at once, and the
  difference becomes visible.
  `AFurtherRequest_IsNotDeliveredAgain` now checks both, number and content.
- *`AutoApproveAsync` does not forget the request*: survived, and rightly so —
  the only caller decides for the automatic agreement before it keeps anything,
  and both ways to `from` clear the request away anyway. The line is a statement
  about the order in `DeliverSubscribeAsync`, not about this method; it stands
  with exactly this note in the code.

**What stays open:** the upper bound throws away silently — neither the applicant
nor the contact learns of it. That is the answer the section recommends to the
danger of exhaustion, but it stays a loss without a receipt.

### S8. Run against Prosody ⚠️ — a find that concerns the federation

Since S4b there stood here: *every single procedure is checked against our own
far side, none against a foreign one.* The run is made up for, against Prosody
13.0.1.

**The setup stands in `tools/prosody/setup.sh` and needs no root.** The package
can be fetched with `apt-get download` and unpacked into a prefix with
`dpkg-deb -x`; Prosody brings finished binary modules along, nothing is compiled.
Four paths built fast into the Debian launcher are bent round, to that `LUA_PATH`,
`LUA_CPATH` and `LD_LIBRARY_PATH`. Two traps along the way, both without a usable
error message: `libicu76` does not stand in the dependencies but is needed by
`util.encodings.so`; and Prosody's `certmanager` discards PEM files with CRLF
wordlessly as *"non-certificate (based on contents)"*.

**What carried straight away.** STARTTLS under RFC 6120 §5.4, TLS 1.3, our
CA-signed certificate as a client certificate, `EXTERNAL` offered by Prosody and
accepted — in the log: *"Accepting SASL EXTERNAL identity from jabber.test"*,
*"Incoming s2s connection jabber.test->prosody.test complete"*. Our stanza
arrived and was processed. The whole way out is right.

**For that the server first had to be able to accept a certificate from
outside.** It always built itself a self-signed one. No foreign far side can
check that: it would have to know exactly this one, and it arises anew at every
start. `XMPPServer` now takes one in — not for the test, but because every
operation outside this test suite needs it.

**And then the find.** Prosody answers the ping — the answer stands in the log —
and does **not** send it back over the stream the question came over. It builds
a connection of *its own* to `jabber.test` for that, fails at it and discards the
answer:

```
Received[s2sin]: <iq from='alice@jabber.test/...' to='prosody.test' type='get'>
mod_s2s  debug  opening a new outgoing connection for this stanza
s2sout   debug  s2s connection attempt failed: unable to resolve service
s2sout   debug  Not eligible for bouncing, discarding <iq ... type='result' ...>
```

Exactly so is RFC 6120 §4.1 meant: an XML stream is **one-sided**, and an S2S
connection carries only one direction.

> **Correction (S9).** Here there stood at first that our federation answers over
> the same stream and thereby does it wrong. That is demonstrably not so.
> `TcpServerLinks.DeliverAsync` and `WebSocketServerLinks.DeliverAsync` go
> without exception over `GetOrCreateOutboundAsync`, and
> `S2SStream.ProcessStanzaAsync` expressly refuses a stanza on an outgoing stream
> — with exactly this section as the reason in the comment. Our side therefore
> behaves like Prosody.
>
> What the run really showed: Prosody could not get at `jabber.test`. In WSL
> there is no DNS for `.test`, and the Hyper-V firewall discards the way from WSL
> to the Windows host anyway. Both sides behaved correctly, the environment did
> not allow the way back.
>
> The run has thereby shown the **outgoing** way against a foreign far side and
> left the **incoming** one open — not because of an error, but because the far
> side had no way back. The error sat in my reading of the log, not in the code.

**What really follows from it — two ways, both work steps of their own:**

- **XEP-0288 (Bidirectional Server-to-Server Streams).** Both directions over one
  connection, negotiated over `urn:xmpp:features:bidi`. Prosody announces it as
  soon as `mod_s2s_bidi` runs — the setup switches it on, and the announcement is
  checked. No fixing of an error, but the extension that makes the way back
  needless: exactly what is missing here. Done in S9.
- **Check incoming connections against a foreign far side**, that is, let Prosody
  dial us. The way without the extension. The code for it stands, checked it is
  only against our own far side.

**What additionally blocks the second way here:** WSL2 runs in NAT mode, and the
Hyper-V firewall discards connections from WSL to the Windows host. Windows → WSL
goes, the counter-direction does not. That hits dialback as well, whose query
back needs exactly this direction — this is why the setup runs over SASL EXTERNAL
with a shared test CA and not over XEP-0220. To change that would go over
`networkingMode=mirrored` in `.wslconfig` or a firewall rule; both are a decision
about the machine, not about this project.

**Test suite.** `ProsodyFederationTests` skips itself without a setup, so that the
ordinary run stays untouched. `TheStreamToProsodyCarriesAStanza` passes against
the real far side. `APingReachesProsodyAndComesBack` is shut down instead of
deleted — it holds fast that without XEP-0288 no answer comes as long as the far
side cannot reach us.

### S9. XEP-0288 — both directions over one connection ✅

The extension that makes the way back needless. The initiator sends after TLS a
`<bidi xmlns='urn:xmpp:bidi'/>` as soon as the far side announces
`urn:xmpp:features:bidi`; after that the same connection carries both
directions.

**Both roles, not only the one.** Offered on incoming connections, asked for on
outgoing ones — `UseBidirectionalStreams` switches both at once.
Only half the extension would help only half the federation.

**The two safeguards from section 4, and both are no formalities:**

- *"MUST NOT send stanzas to the peer before it has authenticated"* — whoever has
  not shown who they are gets nothing. Without this line foreign post could be
  fetched with a mere claim in the stream header: build a connection, call
  yourself `example.com`, ask for the counter-direction, wait.
- *"MUST only send stanzas for which it has been authenticated … the value of
  the stream's 'to' attribute"* — over the counter-direction only our own domain
  goes out. The same check we impose on the far side holds here for us.

To that, to refuse an unannounced `<bidi/>`: otherwise a counter-direction could
be forced that this server never offered.

**The order uncovered something.** The `<bidi/>` has to go out before SASL *and*
before dialback. Our initiator however sent the unasked `<db:result/>` from
XEP-0220 already at the stream header — that is, before the features were there
at all out of which the bidi offer can be read. It now waits for the features in
both cases. `BidiAlsoGoesOutBeforeDialback` found that at the first run.

**Checked against Prosody.** `APingOverABidirectionalStream` passes against the
real far side, and its log shows exactly the expected:

```
Received[s2sin_unauthed]: <bidi xmlns='urn:xmpp:bidi'>
debug   Requested bidirectional stream
Received[s2sin]:  <iq ... type='get' id='ping-1'>
Sending[s2sin]:   <iq from='prosody.test' ... type='result'>
```

`Sending[s2sin]` instead of `opening a new outgoing connection` — the answer
takes the existing connection. With that the **incoming** way of our protocol
layer is shown against a foreign far side for the first time as well, if over the
counter-direction and not over a real incoming connection.

**The setup of the tests is deliberately one-sided.** `left` knows `right`,
`right` does not know `left`. The usual `TcpServerLinks.Connect` is no use for
that — it enters both sides, and then the answer would arrive over a connection
of its own without bidi ever having been involved. This is why the test checks
`BidirectionalDeliveryCount` and not only the arrival, and this is why there is
`WithoutBidi_TheAnswerIsLost` as a counter-check.

Incidentally: in this setup dialback is no use as proof, for its query back would
go in of all directions the one that does not exist. SASL EXTERNAL gets by
without a way back — the same consideration as at the Prosody setup.

11 mutations, 9 dead at once. The two survivors:

- *Selection without a domain comparison*: invisible, because only one far side
  hung on every test. In operation it would be a leak between two foreign
  servers — the stanza would go to the wrong far side, which does discard it, but
  has read it beforehand, and the actual recipient would get nothing without an
  error running up anywhere. Held fast by `TheReturnPath_GoesToTheRightDomain`
  with three servers.
- *Switch in the transport*: survived rightly. `BidiEnabled` is set only when the
  stream was created with `offerBidi`, and that comes from the same switch — the
  line is a shortcut, no safeguard. It stands with this note in the code.

**What stayed open:** WebSocket S2S did not negotiate bidi — made up for in S9b.

### S9b. XEP-0288 over WebSocket as well ✅

The same extension on the second transport. In operation it weighs less there,
because at both ends hang instances of this server that have entered each other.
To have it all the same is the answer to the point that two transports under the
same protocol layer should not behave differently: what holds for the one should
not first have to be looked up at the other.

**The rule of selection now lies at one place.**
`S2SStream.TryDeliverOverBidiAsync` — the comparison of the domain is exactly the
rule at which a mutation got past at the mutation run in S9, and two versions of
it would have been two opportunities for the same error. Since then tests of
**both** transports kill the same mutation.

**The setup cannot repeat the sharp probe from S9, and that is admitted.** Over
TCP the far side does not know us, and without a counter-direction the answer is
lost — the force of proof hangs on that there. Over WebSocket that does not go:
this way identifies itself exclusively over dialback (SASL EXTERNAL does not
exist here), and its query back needs exactly the direction that would then not
exist. Both sides are therefore entered here, the answer would arrive without
bidi as well, and this is why these tests check `BidirectionalDeliveryCount`
instead of the arrival. The fixture comment says that, so that the difference
does not look like carelessness.

**One test of mine was plainly wrong.** I had assumed the dialling server uses no
counter-direction — it has no incoming connection after all. The counter stood at
3 however. As soon as even one stanza runs back, even a receipt under XEP-0184,
the far side dials for its part, and then the first side has an incoming
connection too, which it prefers from then on. Two servers that know each other
therefore fall together under bidi onto the connections they have anyway. That is
the purpose of the extension — but nothing on which an assurance independent of
time could be based. The test is replaced, the observation stands as a comment at
the remaining one.

5 mutations, all dead.

### P4. Prosody dials us ✅

The incoming way against a foreign far side — the last direction that was
checked only against our own. What stood before a real server here for the first
time: our stream header as the answering one, our feature announcement, our
acceptance of a foreign `<auth mechanism='EXTERNAL'/>` and the identity check out
of the certificate laid before us. The way back from S9 did run in the incoming
direction, but over a stream *we* had built.

Prosody's log says it exactly:

```
prosody.test:saslauth  Initiating SASL EXTERNAL with localhost
prosody.test:saslauth  SASL EXTERNAL with localhost succeeded
s2sout   Outgoing s2s connection prosody.test->localhost complete
s2sout   Sending[s2sout]: <iq to='alice@localhost/...' type='result' from='prosody.test'>
```

**No intervention in the firewall.** The blocker was the whole time that the
Hyper-V firewall (`DefaultInboundAction = Block` on the WSL vSwitch) discards
every connection from WSL to the Windows host. To set a rule for that would be a
change to the security settings of the machine. It goes without as well: in WSL
there lies a .NET 10 SDK, so the test runs **there**, in the same network as
Prosody — all loopback, no firewall in between.

    JABBER_PROSODY_CERTS=~/prosody-test/certs \
    dotnet test /mnt/c/.../Jabber.Tests/Jabber.Tests.csproj \
        --artifacts-path /tmp/jabber-artifacts \
        --filter FullyQualifiedName~ProsodyFederationTests

The `--artifacts-path` holds the Linux build artefacts out of the Windows tree;
without it the two runs write over each other's `obj` directories.

**Two names for our side, and the difference is the core.** So that Prosody can
dial us, it has to resolve our domain. An entry in `/etc/hosts` would need root;
`localhost` stands there anyway. The test server therefore serves this domain in
the incoming case and listens on 5269 — the port Prosody falls back to without an
SRV entry. Prosody moves aside to 15269 for that and binds 127.0.0.1 alone. For
the outgoing case it stays at `jabber.test`, where the address stands by hand and
no DNS is necessary.

**Expressly without XEP-0288.** With bidi the answer would come over the existing
stream, and the incoming way would be unchecked again. The test holds that fast
with two side conditions: `InboundConnectionCount > 0` and
`BidirectionalDeliveryCount == 0`. Without them it would pass even if the answer
had taken a quite different way.

**No mutations for this step** — there is no new production code. P4 changes only
the setup and the test suite; its yield is that existing code has stood before a
foreign far side for the first time. `APingReachesProsodyAndComesBack` has fallen
away: the shut-down test said nothing any more that the two running ones do not
say.

### P5. Dialback against Prosody ✅ — and an error the run brought out

XEP-0220 was lastly the only procedure that was checked against our own far side
alone. A ping round trip exercises both roles at once, because each direction
builds a connection of its own and each building side has to identify itself: we
dial and send `<db:result/>`, Prosody asks at the authoritative server of our
domain — that is us again. Then Prosody dials to deliver the answer, sends
`<db:result/>` for its part, and we ask at `prosody.test`. Both roles, one test.

Which procedure comes into play is decided in doing so by **our** side: do we lay
a client certificate before them, then Prosody offers `EXTERNAL`; do we lay none
before them, then only dialback is left. `UseSaslExternal` is the whole
difference between the two tests, and `DialbackVerificationCount` separates them
cleanly — in the EXTERNAL case it has to be zero, in the dialback case greater
than zero. Without these two assurances every test would pass in the respective
other regime as well.

**A Prosody switch that silently does nothing.** At first there stood here a
second VirtualHost with `s2s_secure_auth = false`. It looked right and had no
effect: `mod_s2s` is a **global** module and reads the switch *once* on loading
(`mod_s2s.lua`, line 40). Set per VirtualHost it goes nowhere. Prosody went on
refusing us with `<not-authorized/>` — "Your server's certificate could not be
validated". The intended way is the exception list `s2s_insecure_domains`, and
that is in now; the second VirtualHost is gone again, because a configuration
line without an effect is worse than none.

**And then the actual find: `TcpServerLinks.DisposeAsync` left accepted
connections open.** It cancelled the token, ended the listener and cleared away
the *outgoing* streams — the incoming ones not. Cancelling the token does not
suffice for that: the reading on a socket does not break off reliably with it,
the loop stays standing until the far side hangs up.

It became visible in that Prosody went on answering the next request for thirty
seconds over the long since dead socket — the test server was gone, the
connection from Prosody's point of view not. Between two instances of this server
that never comes out, because there both sides disappear at the same time. In
operation it means: whoever ends the server leaves every far side in the belief
that it can go on delivering, and everything delivered is lost.

Held fast by `DisposingTheLinks_ClosesEstablishedInboundConnections` — without
TLS, because it is about the socket and not about the handshake over it. The
mutation that takes the `Dispose` of the connection out again dies at exactly
this test.

Incidentally the same omission came out in the fixture: the teardown did not
clear `_links` away, and the port 5269 held fast was missing for the next test. A
failed bind looks like a protocol error in doing so — that cost two test runs.

### P6. Run against ejabberd ✅ — the second witness, and what it alone saw

Prosody alone shows that we can manage with Prosody. Where our understanding of
the protocol deviates from the norm, but Prosody joins in the same deviation,
that does not come out. Therefore a second, independently arisen server:
ejabberd 24.12, in Erlang, a different background, a different circle of authors.

Setup in `tools/ejabberd/setup.sh`, after the same pattern as Prosody: unpacked
without root, a test CA of its own, `ejabberd.test` on 127.0.0.1:25269. Two
places wanted a different tool than at Prosody:

- **Erlang is wired fast to `/usr/lib/erlang` in Debian** — and that in all three
  branches of the case distinction in the `erl` start script, even in the one
  that according to the source is supposed to heed `ERL_ROOTDIR`. To set the
  variable looks as though it would have to suffice, and does nothing. The same
  trap as Prosody's `CFG_*`, only better camouflaged.
- **`ejabberdctl` breaks off with "can only be run by root or the user ejabberd"**
  before it does anything at all. Empty `INSTALLUSER`, then it goes.

The four tests mirror the Prosody suite; the common mechanics are pulled into
`AForeignPeerFederationTests`, so that each suite carries only domain, ports,
environment variable and its own prose. ejabberd listens on 25269 and dials us on
5270 (`outgoing_s2s_port`) — both far sides can thereby run beside each other.

**The find: we overlooked ejabberd's bidi offer.** XEP-0288 gives out two
namespaces and means two things by them — `urn:xmpp:features:bidi` for the
announcement, `urn:xmpp:bidi` for the element with which the building server
accepts it. Prosody keeps to that. ejabberd 24.12 puts the *enabling* element
into the features, that is, it announces `<bidi xmlns='urn:xmpp:bidi'/>`.

We saw no offer in that, sent no `<bidi/>`, and the answer to the ping went over
a connection that did not exist: thirty seconds of timeout, no error, no message.
Exactly the sort of failure XEP-0288 is meant against.

Before a change came out of it, three findings instead of suppositions:

1. The XEP (1.0.1, 2016) names for the announcement unambiguously
   `urn:xmpp:features:bidi`.
2. ejabberd's own codec maps **both** forms onto separate types — asked directly:
   `urn:xmpp:features:bidi` → `{s2s_bidi_feature}`, `urn:xmpp:bidi` →
   `{s2s_bidi}`.
3. Its *building* side looks for `{s2s_bidi_feature}`, is therefore conformant
   and understands our announcement. Upstream the accepting side has been fixed
   in the meantime (`s2s_in_features(Acc, _) -> [#s2s_bidi_feature{}|Acc].`).

Out of that follows a one-sided change: `S2SStream.AnnouncesBidi` reads both
forms, announced is still only the one of the XEP. Lenient in reading, strict in
writing — and no line more than that, because for a second announcement no
evidence lay before us.

**And a test that would have been right twice and once not.** The bidi ping
passed, then failed and passed again. The log said what it was down to: ejabberd
had the bidi stream, but chose a *cached* `s2s_out` to `jabber.test` for the
answer, created in an earlier run and pointing at a long since dead ephemeral
port.

Where that came from: our `<message>` to the bare domain `ejabberd.test` has no
recipient there and is refused — and for the refusal ejabberd creates an outgoing
connection to us that survives the test. An `<iq type='result'/>` may under
RFC 6120, section 8.3.1, never be answered and therefore leaves nothing behind.
After that three times in a row 8/8 without a restart in between.

Two things stick to that. First: the Prosody suite sends the same message and has
not come out so far — it was not changed all the same, because for that no
evidence lies before us and a change without evidence is only noise. Second: that
ejabberd prefers an old `s2s_out` to an existing bidi stream is a second
peculiarity; it bites only because our test server lies on a different port at
every run.

**What stayed open** was whether ejabberd really accepts our announcement when
*it* dials us — here only concluded from its source. Observed in the meantime,
and the conclusion was **wrong**: see R6.

---

## Client

### XEP-0198 against a real server ✅ — and the client could not log in at all

The counting was right against `XMPPServer`, that is, against our own
understanding of what a stanza is. The run against Prosody was to check that. It
did not get that far at first.

**The client could log in at no RFC 7395 conformant server.** Prosody refused the
bind IQ with `<unsupported-stanza-type/>` and closed the stream. In the log stood
why: it arrived as `<iq … xmlns=''>`.

RFC 7395, section 3.3.3 demands that every frame is readable for itself as a
complete XML document, "complete with all relevant namespace and language
declarations". Over TCP the content namespace stands once at the
`<stream:stream>` and holds for everything in it; over WebSocket there is no such
enclosing element, and a stanza without a declaration of its own stands in *no*
namespace. Our server never found fault with it, because it recognises stanzas by
the local name and does not look at the namespace at all — both sides made the
same error, so it did not come out.

Fixed in `StanzaNamespace.Apply`, called in `XMPPConnection.SendAsync` — the same
one place through which the counting also happens, and for the same reason: it is
the only one through which every outgoing frame runs.

**And the fix brought a second error out.** All at once
`APingOverABidirectionalStream` failed: our server handed the `jabber:client`
stanza on unchanged onto the S2S stream, and there it is no valid stanza —
Prosody answered with an error IQ. As long as the stanza carried no namespace at
all, it inherited the right one silently on the S2S stream; the error was there
the whole time and invisible. It would have hit every real client whose stanza
goes over the domain border.

Fixed in `RouteToAsync`, beside `StampTo` — the one switch between "here" and
"elsewhere".

Seven mutations, all struck down by exactly the responsible tests: the client
does not stamp (four Prosody tests), the server does not exchange
(`APingOverABidirectionalStream`), the check of the name gone (twelve tests right
across the counting), a naive "an xmlns stands somewhere" (the bind IQ case), a
prefix declaration counts as the default namespace, a `>` in the attribute value
ends the start tag, `LastAcknowledged` reports our own counter.

**The actual result:** the counting is right.
`ProsodyCountsTheSetupExactlyAsWeDo` compares after the complete setup — carbons,
roster, first presence, and nonzas in between — our `OutboundCount` with
Prosody's `h`, and both values are equal. Checked is equality and not only an
emptied queue: too big an `h` would clear it as well, and a client that counts
too little would get through with that. That is what `LastAcknowledged` is there
for in the first place.

**The counter-direction at the domain border** stayed open here at first and is
done in the meantime — see below.

### Switch of the default ✅ — and a test that stopped checking without saying so

`StreamManagementEnabled` stands at `true`. The reason for the switched-off
default — a counting that was faulty once — has been gone since the Prosody run.

**The switch alone would have effected nothing at all.** `AXMPPTests.CreateClient`
set it hard to `false` and thereby overwrote the default; the whole suite would
have gone on running without XEP-0198, and the changeover would have gone through
unchecked. The parameter is therefore now `Boolean?`: `null` means "leave the
default standing". Only with that does the suite run with what a caller without an
opinion of their own gets.

Two tests hung on that, and the second is the more instructive:

- `Disconnect_StopsKeepalive` went **red**. The keepalive loop chooses its means
  by the situation: with XEP-0198 it sends an `<r/>`, otherwise an XEP-0199 ping.
  The test counted pings, and those came no more.
- `Reconnect_DoesNotAccumulateKeepaliveLoops` stayed **green**. It checks an upper
  bound, and "zero pings are at most seven pings" holds true. The test stopped
  measuring and said nothing about it.

Both now run over both procedures (`[TestCase(true/false)]`) and count what the
loop actually sends. And the upper bound has a lower bound facing it — without it
the test would pass even when no keepalive fires at all, and that is exactly what
it was for a while.

The default itself now has a test (`StreamManagement_IsNegotiatedByDefault`) that
checks both: the value and that it comes through as far as the wire. A test on
the property alone would pass even if the setup ignored it afterwards.

Three mutations, all struck down by exactly the responsible tests: the default
back to `false`, the keepalive sends nothing any more under XEP-0198 (kills both
keepalive tests in the SM case — the second only because of the new lower bound),
and `CreateClient` nails the switch fast again.

---

## Stream resumption (XEP-0198 section 5)

Two cuts, because the `<resume/>` itself sits in the setup phase of the client —
after the login, **before** the resource binding. Without a client that sends it,
no test way leads there: the test base drives real `XMPPClient` instances, and
those always bind. A second, hand-written SASL client only for this one test
would be effort without insight, for R2 follows immediately.

### R1. The server keeps broken-off streams ✅

The part that is checkable without a returner.

**The identifier was guessable, and nobody noticed.** The earlier version sent
`id='sm-{connection number}'` — a small number anybody reading along can count
along with. Without a resumption it was without consequence: there was nothing
that could be taken over with it. With it it would have become a way in, for the
identifier is the only secret that identifies a returner. Now it comes out of the
random generator, 128 bits.

**The actual intervention sits where until now the sign-off happened
unconditionally.** Does the connection break, then the server has always created
a sign-off in the name of the client (RFC 6121, section 4.5.2) — otherwise the
contacts carry the resource as online for ever. Whoever may come back may not do
that: the contacts would see a disappearing that would have to be taken back
straight after, and between the two presences would lie everything that went to a
supposedly signed-off resource in the meantime.

So the stream is parked instead of signed off — and that demands the
counter-check at once: **a postponed sign-off that never comes is worse than one
that is too early.** Nobody would notice it. Therefore a pass at the beat of a
second which clears away expired streams and makes up for the sign-off, and a
test that waits for exactly that.

A trap in doing so that became visible only while writing: the clearer calls the
same `AnnounceUnavailableAsync` that parks at the front. Without a preceding
`EndResumption()` it sees a resumable stream again and parks it anew — with a new
deadline, for ever. The mutation that removes this line kills the expiry test.

To that the buffer of the not yet acknowledged stanzas, out of which a resending
would have to happen after a resumption. It is filled only at a promised
resumption — otherwise it would be a store nobody ever reads from — and empties
itself at the `<a h='…'/>` of the client, in the same modulo arithmetic as on the
client side.

Five mutations, each struck down by exactly the responsible test: never park,
expiry without `EndResumption`, promise unasked, identifier out of the connection
number, buffer does not empty itself.

### R2. The client comes back ✅

The attempt sits exactly between login and binding. Does it succeed, then there
is no new resource — and no second presence, no second roster fetch, no renewed
negotiation: a resumed session is no new one.

**The manager has to survive the reconnect.** `InitialiseManagers()` created it
anew at every setup; on it however hang the identifier and the unacknowledged
stanzas. It is now the only one that stays standing — its session state it resets
itself as soon as an `<enabled/>` comes.

**The identifier is no proof of identity, but a selection.** It travels over the
wire; whoever intercepts it would otherwise have a foreign session together with
the full JID and the roster, without ever having seen a password. The stream on
which the `<resume/>` arrives has to be logged in to **the same account** already
— identified itself the client has beforehand, over SASL.
`AStolenId_DoesNotHandOverTheStream` holds that fast.

**Three things only the run showed:**

1. *A clean `<close/>` may not be parked.* Five existing tests failed, and they
   were right: the server held every proper sign-off to be a disturbance, kept it
   for a minute and kept quiet about it to the contacts that long. XEP-0198
   section 5.3 holds for broken-off streams, not for ones taken leave of.

2. *A parked stream has to stay deliverable to.* `SessionsOf` filtered on open
   connections — what arrived during the disturbance was discarded instead of
   going into the buffer. Without this change the resumption saved only what was
   on its way in the last tenth of a second before the break, and the actual case
   — connection gone, messages come all the same — would fail.

3. *`<enable/>` and `<enabled/>` belong under the same lock.* Does a stanza go
   out in between, then the server counts it and the client does not — that one
   resets its counter only at the `<enabled/>`. The states then stay exactly one
   apart, and the buffer never runs empty again.

And the counter-check to the resending: the `h` in the `<resumed/>` clears the
queue of the client away as far as the state of the server. Without that every
recipient would get everything the server had long since twice after every break.

Six mutations, all struck down by exactly the responsible tests: the check of the
account gone, the manager anew at every setup, a parked stream takes nothing in,
a clean taking of leave is parked, the client never resumes, and after the
resumption the full setup runs all the same.

**Two test errors of my own on the way**, both of the same sort — an assurance
that demands more than the test means:

- The waiting condition `IsConnected && ResumableStreamCount == 0` was already
  fulfilled while the client still stood in the middle of the setup. The mutation
  "manager anew at every setup" got past that, because the assurances read the
  old manager before it was replaced. Now it is waited for the *finished* setup.
- `AcknowledgedStanzas_LeaveTheBuffer` demanded an **empty** buffer while Bob's
  XEP-0184 receipts went on putting entries in. Wrong in about every third full
  run, run on its own never. Meant was: what was acknowledged does not lie in
  there any more.

**Not covered** stayed here at first a stanza the client sends off successfully
and that never reaches the server — done in the meantime, see R7.

### R3. Resumption against Prosody ✅

Up to here the resumption was checked only against our own server — both sides
with the same understanding of when a `<resume/>` may be sent, what belongs in it
and what comes back. Prosody does not have this understanding from us.

Necessary for that were two things: a **break from our side** (`KillConnection()`,
the counterpart to `XMPPSession.Kill()` — against a foreign far side the session
cannot be cut from over there, and a proper signing off is exactly the opposite of
what is to be checked) and a **second account** on Prosody, otherwise there is no
sender for a message during the disturbance.

It ran straight away, and because that was suspiciously smooth, into the Prosody
log first instead of believing it. There stands the whole course of events:
`Session going into hibernation (not being destroyed)`, our
`<resume previd='…' h='2'/>`, `mod_smacks resuming existing session`,
`<resumed previd='…' h='3'/>` and `resending all unacked stanzas that are still
queued after resume`.

**Two mutations, two tests too weak — and both times the same cause:** the
assurance was fulfilled without a resumption as well.

- *"Never resume"* let `ProsodyHoldsBackWhatArrivedDuringTheOutage` pass. Prosody
  delivers the message even when the client binds a new resource — it just goes
  there then. That it arrives shows nothing about the resumption. Now it is
  additionally checked that it was the same stream.
- *"Leave out resume='true'"* let both new tests pass. Without a promise the
  identifier is `null` on both sides, and `null == null` means "unchanged". Both
  now check first that anything was promised at all.

The comparison "before equals after" is only then a piece of evidence when
something stood there *before*. That is the third test in this session that was
green and measured nothing.

### R4. The same probe against ejabberd ✅ — and this time no find

ejabberd gets an `ejabberd_http_ws` handler on 5443, `mod_stream_mgmt` and two
accounts. The seven checks are pulled into `AForeignPeerStreamManagementTests` —
they check the same for every far side, and what differs the derivations lay down
in twenty lines. A third server thereby costs almost nothing.

**Fourteen out of fourteen, no deviation.** That is a different result than at
XEP-0288, where ejabberd sent the enabling element instead of the announcement in
the stream features and we therefore overlooked its offer. At XEP-0198 both
servers agree in everything we check: counting of the setup, nonzas, our counter
of what comes in, promise, resumption, resending.

That is no wasted run. Before it was open whether our resumption hangs on
Prosody's reading; now it is not that. A second witness that confirms says less
than one that contradicts — but it says something.

Two differences in the setup, both banal and both would have become a trap on
wiring them fast:

- The WebSocket path is called `/websocket` at ejabberd, `/xmpp-websocket` at
  Prosody. RFC 7395 prescribes none.
- `ejabberdctl register` goes over an RPC call into the running node and needs it
  started; `prosodyctl register` touches the files directly and wants it
  *stopped*. Exactly the wrong way round.

### R5. The namespace in the counter-direction ✅ — and it was missing everywhere

Noted was a narrow thing: what comes in from a foreign server stands in
`jabber:server` and is handed on unchanged to the local client. The second test
for it has shown that it lies broader — **the server has never sent its clients a
namespace at all.** Bind answer, carbons confirmation, roster, presence:
everything without.

That is the same error as the one Prosody refused at the bind IQ of the client,
only mirror-inverted. Over WebSocket there is no enclosing `<stream:stream>` from
which a stanza could inherit its namespace (RFC 7395, section 3.3.3); over the
domain border it changes from `jabber:server` to `jabber:client` (RFC 6120,
section 4.8.1).

Neither ever came out, and for the same reason: our client recognises stanzas by
the local name and does not look at the namespace at all. This leniency has
covered up the error on the client side for years and here once again. A foreign
client would presumably be stricter — and we would learn of it from them first.

Fixed in `XMPPSession.SendAsync`: the one place through which every frame to a
client runs, and chosen for the same reason as at the counting. Nonzas stay
outside; `<enabled/>` goes past this place anyway, but is no stanza either.

Two mutations, both struck down by exactly the two new tests: set no namespace at
all, and `jabber:server` instead of `jabber:client`.

The run against Prosody and ejabberd stayed green unchanged afterwards — the
change concerns only what our clients get from our server.

### R6. Offering and asking separated ✅ — and the conclusion from P6 falls

`UseBidirectionalStreams` steered both at once: the announcement on incoming
connections and the request on outgoing ones. That was not merely unsharp — it
made the one direction **unobservable**. As long as our outgoing connection uses
the counter-direction, the far side answers over that and does not dial us in the
first place; there was therefore no state in which our announcement could show
itself.

Now two switches, `OfferBidirectionalStreams` and `RequestBidirectionalStreams`.
With that there is the state "offer, do not ask", and with that the test
`ThePeerTakesTheReturnPathWeOffered`: two pings, because at the first the incoming
connection does not exist yet, and `BidirectionalDeliveryCount` as evidence.

**Prosody accepts, ejabberd 24.12 does not.** Exactly the deviation the second
server is there for — and it refutes what stood here in P6. There I had concluded
from ejabberd's *master* that its building side looks for the XEP form
`urn:xmpp:features:bidi`, and out of that, that our announcement suffices. The
shipped 24.12 behaves differently: it announces `urn:xmpp:bidi` itself and
apparently looks for the same.

The same error as back then in the small: concluded from the source of one version
to the behaviour of the running one. The difference is that this time it came out,
because a test asked after it.

Fixed by **two** announcements. On the wire it stays unambiguous: the enabling
element is called `urn:xmpp:bidi` in both readings, so only one answer comes back,
and whoever knows only the XEP form passes over the second element as an unknown
feature. After the change both servers accept the counter-direction.

Incidentally a third thing hung on the same switch: whether we *use* an existing
counter-direction at all. That belongs to the offering and not to the asking — and
is now quite without a switch, because `BidiEnabled` presupposes both already.

Two mutations, both struck down by `ThePeerTakesTheReturnPathWeOffered`: announce
only the XEP form (ejabberd falls out), and hang the offering on the switch for
the outgoing side again (both fall out).

### R7. The lost stanza ✅ — a case that did not exist in the process

`ResendUnackedAsync` had been implemented since R2 and unchecked, and the reason
was no omission but a problem of the setup: there was no way to create a stanza
that leaves the wire successfully and nevertheless does not arrive. A broken-off
socket lets the sending fail at once and loudly, and a stanza not sent is not
counted along in the first place — the queue therefore always stayed empty, and
the whole branch never ran.

`XMPPServer.SwallowClientStanzas` produces the case: the server takes the frame in
and throws it away **before** it records, counts or hands it on. To the client it
looks like a successful sending, to the server as if nothing had ever come. Nonzas
stay untouched — without them neither `<r/>` nor `<resume/>` would be possible in
this state, and the case would be unreachable again.

The switch falls in with the existing switches for error cases
(`CompleteCloseHandshake`, `RouteStanzas`, `AnswerAckRequests`,
`BroadcastPresence`) and is the same thought: some ways are walkable only when
the server misbehaves on purpose.

Two mutations, both struck down by
`StanzasLostInFlight_GoOutAgainAfterResumption`: resend nothing at all, and count
along again while resending. The second is the one that stayed expressly
unstruck in R2 — there it stood noted that no test way exists for it. Now there
is one.

With that the whole XEP-0198 strand has no unchecked line any more.

### D1. The SASL downgrade ✅ — never weaker than the last time

The client took the strongest mechanism offered. That is right as long as the
announcement comes from the one that has to make it — only it is not
authenticated. It does come over TLS, but TLS shows only that the far side has a
certificate of a trusted CA, and the classic man in the middle has one. Whoever
follows the announcement alone thereby follows the one who forged it as well:
out of the features the SCRAM offers disappear, left over is PLAIN, and the
client willingly sends the password itself instead of a proof that it knows it.
The same movement as at the STARTTLS downgrade from S4b-6, one layer higher.

`SaslMechanismPolicy` holds two lower bounds that run through the same check:
`Minimum`, what the caller demands, and `Pinned`, what the last login succeeded
with. The first takes effect from the first frame on and has to be set, the
second takes effect of itself and only from the second connection on.

Two places decide about the value of the whole thing, and both are questions of
order:

- **Checked is before the `<auth/>`, not after the answer.** At PLAIN the
  password stands in exactly this frame. Whoever notices the downgrade only at
  the answer has already given it to the man in the middle, and to break off the
  login afterwards does not take it back off him.
- **Pinned is after the login, not before.** A failure says nothing about what
  this server can do.

That the pinning is a trust on first use stays: does the man in the middle stand
in between at the very first setup already, then it pins his downgrade. Only that
is not the attack that is worth it. The client comes back of itself after every
break, and a break can be forced — it therefore suffices to disturb the
connection and to intercept the *second* login. Exactly that one is covered now,
without anybody configuring anything.

The test server plays the man in the middle by changing `OfferedSaslMechanisms`
between the two connections.

Seven mutations, all struck down:

| Mutation | Struck down by |
|---|---|
| do not check `Minimum` | `TheMinimumHoldsOnTheVeryFirstConnect`, `Minimum_HoldsWithoutAnyPreviousLogin` |
| do not check `Pinned` | `AWeakerServerOnTheSecondConnect_IsRefused`, `TheRefusalHappensBeforeThePasswordGoesOut`, `Pinned_RefusesTheWeakerAndAllowsTheStronger` |
| pin nothing at all | six tests |
| `Strongest` takes the first known instead of the strongest | `Strongest_ReadsTheRankingAndNotTheOrder`, `AStrongerServerOnTheSecondConnect_IsAccepted` |
| check the pinning on equality instead of on strength | `AStrongerServerOnTheSecondConnect_IsAccepted`, `Pinned_RefusesTheWeakerAndAllowsTheStronger` |
| push the check behind the SASL exchange | `TheRefusalHappensBeforeThePasswordGoesOut` and two others |
| the setter accepts an unknown mechanism name | `AnUnknownMinimum_IsRefusedAtTheSetter`, `Minimum_RefusesAnUnknownName` |

The fourth is the one no integration test could have found: the test server
announces from the strongest to the weakest, and there "take the first" looks
exactly like "take the strongest". The difference becomes visible only when a
server retrofits and hangs the new mechanism on at the back — which
`AStrongerServerOnTheSecondConnect_IsAccepted` reproduces, but only after the
unit test had asked after it.

The last is the quietest: an unknown name has the strength 0, and a lower bound
of 0 demands nothing at all. A typing error in `MinimumSaslMechanism` would have
effected silently the opposite of what the caller wrote down — this is why the
setter refuses it instead of taking it.

Not struck down, and demonstrably unreachable at that: to pull the pinning
*before* the login. It would need a failed login followed by a further one — but
every authentication error suppresses the reconnect, and the password cannot be
changed any more after the creation of the connection. The pinned value would be
the same one anyway that `EnsureAcceptable` has just let through.

### D2. The poisonable caps cache ✅ — a hash that was created but never checked

`ver` is no identifier an entity chooses for itself, but the hash over what it
answers to disco#info. This client has always created it correctly — shown
against the test vector from XEP-0115 §5.2 — and did not recompute it a single
time at foreign answers.

With that the cache was poisonable by everybody whose presence arrives here. The
movement is short: the attacker announces in their presence the `node#ver` pair
of a widespread client and answers the following query with a list of their
choice. Under this pair lies their list from then on — and it is handed out to
every further contact that announces the same pair, without that one ever being
asked. The attacker thereby determines what this client believes about third
parties: which encryption they can do, whether they understand receipts, what can
be sent to them.

The computation lay there already, only not reachable:
`CalculateVerificationString` read fixedly out of `LocalIdentities`/
`LocalFeatures`. It is now applicable over arbitrary information as
`VerificationString(identities, features)` — no more than that was needed to make
a checked value out of a created one.

Three reasons lead to an entry not being laid down, and they are not the same:

| Reason | What it means |
|---|---|
| No `hash` attribute | Old form before XEP-0115 1.4; `ver` is a version number there and no hash at all |
| Unknown algorithm | Recomputed can be only `sha-1` |
| Data form in the answer | XEP-0128 goes into the `ver` value, this computation does not know it yet |
| Hash does not fit | The forgery |

Only the last is an attack. The other three are inability — our own or that of
the far side —, and the difference belongs into the protocol: over
`OnCapsRejected` the reason goes out in plain text. Reported is the answer in all
four cases all the same over `OnCapsDiscovered`; it is what this entity says
about itself, and exactly that would come out of an ordinary disco#info query as
well. Refused is only the bundling.

Nine mutations, all struck down. Two of them are the ones it is about:

- **Warn and lay down all the same** — the classic half fix. Five tests fall out,
  because `GetCachedInfo` finds the entry.
- **The calling place drops the `hash` attribute.** Struck down by
  `CapsOfARealContact_AreVerifiedAndCached` alone. Without this test this mutation
  would have survived, and with it the cache would have stayed empty for good
  without anything having gone red — the check would have gone on working, only
  always with the result "not checkable". The test was written expressly against
  this gap and shows incidentally that our own `ver` fits our own disco#info
  answer.

One mutation survived at first and uncovered something in doing so: the check for
a missing `hash` attribute is redundant for the decision — `null` is not `sha-1`
anyway, so the next branch catches it along. It carries the more precise reason
alone. With that stood the choice of striking it or checking the reason; the test
now checks it. A branch whose only purpose is a statement has to be secured over
this statement — otherwise it is decoration.

### D3. The verification string, complete ✅ — and four rules that checked nothing

D2 made a checked hash out of a created one. Only with that did it become visible
what was missing from the computation: it went over identities and features, and
XEP-0115 §5.1 lets two more things flow in — the `xml:lang` of an identity and
the XEP-0128 data forms. Neither ever came out before, because a value nobody
recomputes cannot be wrong either. After D2 the consequence was concrete: every
far side that carries its name in a language or publishes its software
information was refused — not as a forger, but not believed either.

Both are in now, and the proof for it is not self-made: XEP-0115 §5.3 prints a
second vector for exactly that ("Complex Generation Example", two identities that
differ only in `xml:lang` and name, plus a softwareinfo form with a multi-valued
field). It is reproduced exactly, and as at the simple vector a second test checks
that the printed `ver` value really is the SHA-1 hash of the printed S string.

To that come the three rules of invalidity from §5.4: the same identity twice,
the same feature twice, two forms with the same `FORM_TYPE` or a `FORM_TYPE` with
several values. That is no formal strictness. The verification string arises
through an answer being carried over into *exactly one* string; where duplications
stand, there is more than one — and with that a second answer can be built to a
given hash. The multi-valued `FORM_TYPE` is the clearest case: the field itself is
not appended along, so the second value disappears without trace from the
computation.

Fourteen mutations. Ten fell at once. **The four rules from §5.4 all four
survived** — and the reason is the sort of self-deception this procedure is there
for: my test announced a `ver` value the ambiguous answer did not fit anyway. So
the comparison of the hash struck it down already, and the rules it was about
never ran. The test now announces the value the ambiguous answer *really* yields —
with which only these rules can still stop it. After that all four fell.

A test that reproduces an attack has to let the attack succeed as far as the
place that is to catch it. Otherwise it checks the sentry in front of it.

And one that would not have arisen at all without a mutation pass:
`RespondInfoAsync` gives out the `xml:lang` of an identity — checked that was by
nothing, because our own identity carries none. The way there leads over two
files: announcement and answer. Do they not agree, then this client is a liar for
everybody who checks under §5.4.
`AnIdentityWithXmlLang_SurvivesTheRoundTrip` lets both run against each other.

### D4. SASLprep, complete ✅ — tables one does not copy out

The preparation of user name and password consisted of one line: NFKC. That is
one of four steps. Missing were the mappings (a soft hyphen in the password
stayed standing instead of disappearing), the prohibition tables (a control
character went through) and the bidi check entirely.

The consequence was not that somebody got in who should not, but the opposite: a
password outside of ASCII was prepared differently here than at Prosody or
ejabberd, and the login failed without anybody being able to say why. The same
typed password, two different keys.

To that came a second version of the same abridgement: client
(`SCRAMAuthenticator`) and server (`XMPPCredentials`) each normalised for
themselves. Two copies of the same procedure are two opportunities to run apart;
now it is one.

**The tables are not copied out but generated.** RFC 3454 carries about nine
hundred code point ranges, of those 396 alone for the ones not assigned in
Unicode 3.2 and 360 for the right-to-left characters. A typing error in it would
be practically impossible to find — it comes out only when a particular character
appears in a password, and then as a login that fails without a reason.
`tools/stringprep/generate.py` reads the RFC and writes
`Jabber/Auth/StringPrepTables.cs`; whoever doubts the tables lets it run and
compares.

That the tables are fixed to Unicode 3.2 is in doing so no backlog but the point
of the thing — and `UnassignedCodePoints_AreRefused` shows it at U+0221, which
.NET has long known as a Latin small letter and RFC 3454 does not.

Eleven mutations, ten struck down at once. The eleventh is the instructive one:
**the client may send PLAIN unprepared, and everything stayed green.** The reason
is that the server prepares what arrives at it — the login succeeds one way or
the other, and my test looked only at it. Covered with that was the server half,
not the one of the client. Against a server that relies on the preparation of the
client we would have run aground without a test having noticed it. Now the test
checks what stands on the wire instead of what comes out at the end.

Twice in a row the same error in my own tests: in D3 I let the attack fail too
early, here I let the proof run over the wrong half. Both are tests that look at
the result instead of at the way — and both would have passed without a mutation
pass.

By the way, but not incidental: `Verify` catches a failed preparation and reports
a failed attempt. What stands in a PLAIN `<auth/>` is determined by the far side;
a control character in it may not knock the server over.

### D5. JIDs under RFC 7622 ✅ — and the message on the wrong device

The comparison of two JIDs ran everywhere over `OrdinalIgnoreCase` on the whole
string. Under RFC 7622, section 3.4 however only the local and the domain part
are independent of the spelling, the resource part is not.

The error was not theoretical, and it had an ugly shape. The giving out of
resources in the server has always compared ordinally — `Mobile` and `mobile`
were two different devices for it, and the second login therefore got through
instead of being refused as a conflict. Only the *looking up* of a session did
not do it. The server therefore accepted two devices and afterwards delivered the
traffic of the same one to both: the message landed on the wrong one, and at the
sender everything looked like success.

`JidUtilities` is now an implementation of RFC 7622 instead of one line
`ToLowerInvariant`: split up in the order from section 3.2, prepare each part
after its PRECIS profile, maximum lengths in octets, compare part by part.
Checked against both tables of examples from section 3.5.

The class membership of a code point is approximated — out of the Unicode
category and the compatibility decomposition instead of out of the derived
properties under RFC 8264. That is named in the README, together with what stays
outside because of it.

One deviation is deliberate and has a test of its own, so that it has a place at
which it comes out: example 18 (a leading space in the resource part) is
accepted. The table carries it as a non-JID, but in the part with the rules
nothing of the kind stands — the OpaqueString profile allows spaces. For a router
accepting is the more careful choice: to refuse an address other servers hold to
be valid loses messages, and ours at that.

Twelve mutations, nine struck down at once. **Three survived, and all three for
the same reason: the test case already hit an earlier rule.**

| Mutation | Why it survived at first |
|---|---|
| Split at the last instead of at the first `/` | My example had only one slash |
| Allow compatibility characters in the local part | The Roman four falls over its category already |
| Allow an empty resource part | Example 19 has *both* parts empty; the local part is checked first |

That is the same sort of self-deception as in D3 and D4, now for the third time
and in three forms at once. The common denominator can be named clearly by now:
**an example out of a specification is no test yet.** The tables are built for
demonstrating, not for separating — a line may happily offend against three rules
at once. A test that is to secure a particular rule needs a case that violates
exactly *this* one.

Fixed with `juliet@example.com/foo/bar` (the case section 3.4 names itself), the
ligature ﬁ (a small letter that decomposes compatibly into "fi") and the two
empty parts each for itself.

### D6. Our own extended information ✅ — and a window the test itself tore open

Foreign XEP-0128 forms this client has read since D3; own ones it delivered none.
`DiscoManager.LocalForms` closes the gap, and `DiscoForm.SoftwareInfo` builds the
usual case from XEP-0232.

Two things about it are decisions and no matters of course:

- **The list starts empty.** What stands there every contact learns unasked, and
  software, version and operating system are exactly the pieces of information
  from which a device can be recognised again. A default that publishes something
  would be a default against the user. `WithoutOwnForms_NothingIsAnnounced` holds
  that fast.
- **What is not given becomes no empty field.** "I say nothing about my operating
  system" and "my operating system is called the empty string" are two different
  statements; only the first is meant, and the second would yield a different
  hash.

Seven mutations. Six fell at once — among them both halves that belong together:
the form not into the answer (the hash does not fit then) and the form not into
the hash (the answer does not fit then). Both times we would have been a forger
for every checking far side, at completely honest information.

The seventh survived at first and is again the same sort: "not given becomes an
empty field", applied to `software` — a field my test always filled. Checked was
the rule only at the one field I had left out. Now one test checks all four
singly.

**And a find that did not come out of the mutation pass, but out of a single red
run:** `OwnDataForm_SurvivesTheRoundTrip` failed once in about five runs. The
cause is no error in the code, but a property of the protocol the test itself
brought about. Alice changes her information after connecting and sends a new
presence; between the two lies a window in which the old `ver` value is announced
and the new answer would already be given. Whoever asks in it gets a deviation
reported rightly — exactly what D2 built in.

The test now waits until the new presence stands at the server before Bob comes
along. The same grip into the window had sat in
`AnIdentityWithXmlLang_SurvivesTheRoundTrip` since D3 without ever coming out; it
is fixed along.

That is the addendum to the rule from D5: a test setup can produce a situation
that does not exist at all in the intended course of events — and then it is not
the code that is to be changed, but the setup.

### D7. Roster versioning ✅ — and two tests that deceived themselves

RFC 6121, section 2.6: the client names the version it has cached, and gets an
empty result when it still holds. The roster is the biggest thing that goes over
the wire at the login, and it changes rarely.

The version is **computed, not counted** — a hash over the content. A counter
would have to be stored with the account and would survive a restart only if
somebody thinks of it; the hash needs no store and stays right even when somebody
changes the roster past the file. It moreover has a property a counter does not
have: does the roster go from A to B and back to A, then the version is the old
one again — and that is right, for the intermediate state of the client is right
again after all.

Everything hangs on a subtlety that easily comes out wrong: "unchanged" is a
result **entirely without** a `<query/>`. A `<query/>` without children means on
the other hand "your roster is empty". Whoever confuses the two deletes the
contact list of the user — the mutation that does exactly that stands as M2 in the
list.

**A find even before the mutation pass.** The first test run was red, because the
server read the `ver` with `Attr` — and `Attr` is anchored on the root element.
The attribute sits however at the `<query/>`, not at the `<iq/>`. The check looked
completely right and always read `null`; without the test the versioning would
have stayed without effect on the server side without anything having come out.
Now there is `QueryAttr`, and the comment there names the trap by its name.

Thirteen mutations, all struck down — announcement, empty result, version in the
result, version in the push, taking over on both ways, the doing without an
announcement, and one field each that falls out of the computation.

**Two tests deceived themselves in doing so, and the second deception was my
repair of the first.**

`ARosterPush_CarriesTheNewVersion` failed occasionally under full load. Cause:
`AddContactAsync` is two things — a roster set and a `subscribe` —, so *two*
pushes come. The test stopped at the first and then compared against a server
state that had already run on.

My first repair — wait for agreement instead of for a change — was worse than the
error: at the start both sides stand at the empty roster, so they are in agreement
already. The waiting condition was fulfilled before anything had happened, and
the test *always* failed afterwards. Right is both together: changed **and** in
agreement.

That is the third form of the same thing in four commits, and it has now earned a
name: **a waiting condition the initial state already fulfils does not wait.**

Noticed by the way and not fixed: a full roster is mixed into the cached one
instead of replacing it. A contact removed while the client was signed off
thereby stays standing. That is an error of its own with a counter-check of its
own and stands under "Later".

### D8. The contact one does not get rid of ✅

Noticed at D7, fixed now: the result of a roster query was *mixed into* the cached
roster. It is however the state and no addition (RFC 6121, section 2.1.4) — what
does not stand in it does not exist any more.

The way to the damage is everyday: a contact is deleted at another device while
this one here is signed off. At the next login the server does not send them any
more — and nobody takes them out. They come back and cannot be removed from this
device any more: an attempt to delete creates a push with `subscription='remove'`,
the entry disappears, and at the login after next it is there again. In running
operation nothing comes out, because there the push always comes.

`Roster.ReplaceAll` is called what it behaves like and is called exclusively for
the result — never for a push. That is the point at which the thing could tip
over: on the wire both look the same, a `<query/>` with `<item/>`. Whoever treated
the push the same way deleted the whole remaining roster at *every* change. That
is what `ARosterPush_DoesNotReplaceTheWholeRoster` stands there for, and the
belonging mutation M5 is the only one that knocks over exactly this one test.

Five mutations, all struck down.

Two attempts the push test needed however, and both times it was down to my
triggering the change at the wrong place: an intervention past the account
(`SetRosterEntry`) creates no push, an `AddContactAsync` creates two at once.
Needed is exactly one roster set from the client — then comes exactly one push
with exactly one element. That too belongs to the rule from D5: the setup has to
produce the situation that is to be checked, and not one that looks like it.

**An existing test had to be touched for that**, and that is worth mentioning
because it looks like a convenient adjustment. `RosterPushAfterBind_IsApplied` let
the server push a contact in the setup phase that its own roster did not contain.
After that comes the result, and the result is the state — the contact therefore
disappeared again, and the test went red.

Not the test was too strict, but its server was impossible: XMPP delivers in order
on a stream, a push *before* the result is thereby older than the result. A server
that announces an entry it does not itself carry contradicts itself. The contact
now stands in the roster of the account as well; checked is still the same thing,
namely that a stanza from the setup phase does not get lost.

That came out only in the full pass — the filtered runs during the work did not
contain this test. Three full runs in a row, each with the same failure: no
flickering, but a regression, and without the full pass it would have gone along.

### D9. The resending asks afterwards ✅ — and the stopgap from D7 falls away

In D7 `TheResumedCountPreventsADoubleDelivery` went red once under full load. I
lengthened the waiting time back then and noted openly that the cause is not
found. It is now, and the waiting time was the wrong answer.

`ResendUnackedAsync` sends everything open out once again after a resumption —
and asked after nothing afterwards. The `<resumed h='…'/>` has emptied the queue
only as far as the state of the server; what was open beyond that waited for an
`<a/>` that never came of itself. The server acknowledges when it is asked, and
asked has the keepalive alone.

With that there were two versions of the same error:

- **Keepalive on** (the default): the queue stays standing until the next `<r/>`,
  that is, up to 25 seconds. Annoying, but bounded.
- **Keepalive off**: it stays standing for ever. And at every further resumption
  everything in it goes out once again.

Why that came out only under load: whether anything stays open at all after the
`<resumed/>` depends on whether the server had already processed everything at
the break. On a quiet machine it had — and the queue was empty without any doing.

**The test that could have shown it covered it up.** In R7 there stood a
`RequestAckAsync` by hand in `StanzasLostInFlight_GoOutAgainAfterResumption`. I
had written it there because the queue did not become empty otherwise — and
exactly that was the finding I read as a need of the test instead of as an error.
The call is gone; without the correction the test is red, with it green.

Two mutations, both struck down by this test: do not ask afterwards at all, and
ask before the resending instead of after it. The second is the finer one — an
`<r/>` before the resent stanzas fetches an acknowledgement about the state
*before*, and the queue stays standing just the same.

The lengthened deadline from D7 stands at the default again.

The lesson is more uncomfortable than the ones from D3 to D5. There tests measured
something not; here a test **steered round** a real error, and I wrote the
steering round myself and even gave reasons for it in the commit. When a test
needs a helping hand so that it runs through, the first question is not how best
to formulate it, but why it needs it.

**A second race independent of that** came to light at the check and is fixed
along: `TheClientResumesInsteadOfBindingAnew` went red in one of four full runs,
with the message "the stream was negotiated anew". That held true and said nothing
about the checked code. The cause is the order between two clocks: the client
comes back after its reconnect period, the server lays the broken-off session
aside at a beat of its own. Is it not that far yet, then the `<resume/>` finds
nothing before it and the client binds anew — acted rightly, only not what the
test wanted to check.

`KillAndAwaitParked` now waits for `ResumableStreamCount > 0` at the three places
at which a successful resumption is the precondition. Does it fail all the same,
then the reason stands in the message.

Whether this race existed before already is open: four runs on the state of D8
stayed green, four with the correction yielded one red — at a single event that
cannot be told apart. A connection with the additional `<r/>` cannot be seen (as a
nonza it is not counted along and goes out only after the start of the receiving
loop), but it is not ruled out with that.

### D10. Waiting for an event instead of for silence ✅

`IqWithoutId_IsNotAnswered` was the third flaky test of this series and the only
one whose flakiness sat in the arrangement from the start: it waited a second for
the number of frames received by the client *not to rise at all*. With that it
counted along everything that had nothing to do with the checked IQ — every setup
frame that was still on its way —, and under load one of them was among it at some
point.

A negative proof needs no waiting time when there is an event it can be fastened
to. On a stream things are processed in order: after the IQ without an `id` a
second one now goes out that *has* to be answered. Is its answer there, then the
client has already had the first in hand and decided — and then the finding that
no `type='error'` is among it suffices. No `WaitUntil` over a second, no dependence
on load, and the test is faster by the way.

One mutation, struck down: drop the check for the missing `id` and answer with
`id=''`.

Two of the three flaky candidates from D7 to D9 were real errors in the code, this
one is one in the arrangement of the test. The rule behind it: **whoever wants to
check that something stays out needs an event after which it must have stayed
out.** A deadline is only a substitute for that, and a poor one.

At the securing a fourth came along that is expressly <b>not</b> fixed here:
`AStolenId_DoesNotHandOverTheStream` ran into the timeout in one of seven runs at
the waiting for `ResumableStreamCount == 1` — the server had not laid the
broken-off session aside within ten seconds. Alice has `maxReconnectAttempts: 0`
there, so she does not come back and cannot clear the entry away herself; an
explanation I do not have. After D9 a longer deadline would be exactly the wrong
answer, and to write down a supposition would be worse than the open question.
Stands under "Later".
*(Addendum: found in D11 — it was no test error.)*

### D11. The resumption belongs to the stream, not to the presence ✅

The open point from D10 is cleared up, and it was no test artefact. `Park`
demanded that the session be <i>available</i>:

```csharp
if (session.FullJid is null || !session.IsAvailable)
    return false;
```

With that two things hung on each other that have nothing to do with each other.
The resumption is promised with `<enabled resume='true'/>` and belongs to the
stream; the presence says something to the contacts about the human in front of
it. Whoever made themselves invisible without ending the connection lost the
promise silently: at the break their stream was not laid aside, their `<resume/>`
got a `<failed/>`, and everything unacknowledged was gone — exactly the loss the
buffer from R2 and R7 was built for in the first place.

The same hit the client whose first presence was still on its way. And exactly on
that hung the flaky test: it broke the connection off as soon as the resumption
was promised, and that is in the setup of the client *before* its first presence.
On a quiet machine it came in time, under load not always — an error that
disguised itself as a problem of timing.

The condition is gone. For the sign-off, in whose course `Park` sits, that changes
nothing: `TryMarkUnavailable` refuses a never available session of its own accord,
the distinction had long since been taken there. The check in `Park` was therefore
not only wrong but also double.

One mutation, struck down by `AnInvisibleClient_KeepsItsResumableStream`: demand
the availability again.

Three out of four flaky candidates were thereby real errors in the code, one was
an error in the arrangement of the test. That is the yield when one follows up a
single red run instead of repeating it until it is green.

### D12. The type of a message ✅ — and the receipt before an audience

RFC 6121, section 5.2.2 knows five types of messages. This client knew one:
everything arrived alike, and the recipient could not distinguish the headline of
a news source from the line of an acquaintance — and the one out of a room not
from one directed at them alone. Only `error` was separated already, because an
error stanza would otherwise have run through as a chat line.

Where that concerns not the display but the behaviour, it becomes unpleasant:
**the client acknowledged every message, the one out of a room as well.** The
sender there is the room and not a human; the receipt would go to the room, and
that hands it on to everybody. Out of a silent acknowledgement would become a
speaking up before an audience — at twenty present four hundred receipts for
twenty lines. At the headline the RFC says it itself: "no reply is expected".

`MessageType` now carries the type as far as into the application, and
`ExpectsAReply` decides at one place whether an answer is given of itself — in
both directions: whoever writes into a room no longer requests an acknowledgement
either (XEP-0184, section 5.3 expressly advises the sender against it).

The default is a MUST and no matter of taste: is the attribute missing **or is its
value unknown**, then the message counts as `normal`. The reason lies in the
future — a later extension is to arrive at old recipients as an ordinary message
and not to disappear.

Seven mutations, all struck down.

The test needed a second attempt, and the error was an old acquaintance in a new
garb: I observed the receipt over `OnReceiptReceived` at the sender — and the
event fires only for messages the client itself has sent off over
`SendMessageAsync`. At a raw stanza the receipt counts as an attempt at forgery
and is discarded. Observed is now what the recipient sends out, and with that the
way instead of the effect.

Not touched: the type-dependent delivery rules of the *server* from section 8. The
test server still delivers alike for all types. Stands under "Later".

### D13. The delivery rules of the server ✅ — and the address decides too

The client half from D12 has made the server half visible: RFC 6121, section 8.5
makes the delivery depend on the type of the message, and this server delivered
everything alike.

Four rules, two of them MUST:

| To the bare JID | Behaviour |
|---|---|
| `groupchat` | never deliver, `<service-unavailable/>` to the sender |
| `error` | discard silently — an error on an error would be the beginning of a loop |
| `headline` | to **all** resources with a non-negative priority |
| `normal`/`chat` | to one resource |

To that the priority, which did not exist at all before: a resource with a
negative priority gets nothing that went merely to the account. Exactly for that a
client sets it — the second device stays addressable directedly and keeps out of
the rest.

**The address decides too, and that cost me an attempt.** My first version refused
`groupchat` and `error` unseen. Section 8.5.3.1 says however for a *matching
resource*: "For a message stanza, the server MUST deliver the stanza to the
resource" — without a distinction by type. And that is no special case but the
normal one: a room delivers to `user@server/resource`, not to the account. My rule
would have made the room function unusable and swallowed every error answer.

It came out in that two tests from D12 went red — those that sent a room message
to a bare JID. They too were wrong: this addressing does not exist at a
rule-conformant server. They now go to the full JID, the way a room would do it.

Eight mutations, all struck down — among them the finest: to apply the priority to
directed messages as well. It looks like thoroughness and takes from the negative
priority exactly what makes it what it is.

Not fixed and noted: without a reachable resource section 8.5.2.2.1 demands for
`normal` and `chat` a store or an error. This server has no store for the absent
and discards silently — for the three other types that is right, for these two it
is not.

### D14. The offline store ✅ — and the third way that does not exist

The open point from D13. Section 8.5.2.2.1 puts two ways beside each other and
forbids the third:

| Without a reachable resource | Prescription |
|---|---|
| `normal`, `chat` | store **or** `<service-unavailable/>` to the sender |
| `groupchat` | MUST `<service-unavailable/>` |
| `headline`, `error` | MUST discard silently |

The third way — to discard silently what would have to be stored or refused — was
exactly the one this server went. And it is the most unpleasant of all: the sender
holds their message to be delivered, the recipient has never learned that it
existed, and **nobody can notice the loss**. An error that hides itself is worse
than one that makes a noise.

Both permitted ways are there now, because they bound each other: without a store
the refusal would be the rule, without a refusal a store that has run full would
have no way out. `StoreOfflineMessages` chooses between them — switched off the
server is not less rule-conformant, only less convenient.

**Two places lead into the store, not one.** The second is section 8.5.3.2.1: a
`chat` to a resource that does not exist is treated as though it had gone to the
account. The exception looks quirky and hits everyday life — a client answers to
the full JID it last saw, and if the partner in the conversation has changed
device in the meantime, that one is gone. For the other types it stays at the
silent discarding: whoever writes to a full JID means this resource; at a
conversation that is a shortcut for "the one I am talking to", at everything else
a piece of information the sender wanted that way.

To implement only half the exception would have been worse than the previous
state: the message would land in the store while the recipient sits beside it with
another resource and waits. This is why it stands in a test.

**The limit refuses and does not displace.** Both directions lose a message, but
only one of them says it to somebody. And a limit that displaces would itself be
the attack: whoever writes the store full would thereby delete foreign post. The
same consideration as at the kept subscription requests from S6.

**Handed in later at every non-negative available presence, not at the
*becoming* available** — unlike the kept request directly above. The difference
lies in the store being emptied at the delivering: a second pass finds nothing any
more and can put nothing up twice. And it has a case of its own: a resource that
is logged in with a negative priority and raises it to 0 was available already —
it becomes a recipient however only just now.

Both conditions are necessary, not only the priority. A sign-off resets the
priority of the session to 0, for a signed-off resource has no state to report.
Whoever asks only after the priority empties the store into a stream that is just
taking its leave.

To that three things without which the store is only half of use: the XEP-0203
stamp (without it a message from yesterday claims to be from now), the surviving
of a restart (an accepted sender may rely on it) and the announcement as
`msgoffline` in disco#info (otherwise a client would have to conclude from the
staying out of an error that something was stored — and an error can be late).

New at the client: `PresencePriority`. Without it a client cannot say how much it
is meant when a message goes to the account — and the negative branch of the store
would not have been checkable through the client at all.

27 mutations, 26 struck down. The survivor is the shortcut `if
(_offlineMessages.Count == 0) return [];` in `TakeOfflineMessages`. It is no
statement about behaviour, but a precaution against a writing without a change:
without it every available presence would report a change of the account, and the
file store would write at every login. No test holds it fast, and that is right so
— a test on it would check the file access and not the protocol.

**The instructive failure lay this time in the tool.** One test failed three runs
in a row, on its own as well, after a rebuild as well — and was right after all.
My mutation script sets the file back at the end with `Copy-Item` out of a backup,
and `Copy-Item` takes over the timestamp of the backup. That is older than the
mutated binary; MSBuild held the build to be current and did not compile anew. The
"reproducible" failure ran against the binary of the last mutation.

The lesson is not new, but it had a new disguise: **when a test fails that one has
just written, the suspect is not always the code — it can also be that with which
one measures.** The script now sets the timestamp anew, and every mutation pass
brackets the mutation between two green runs without it.

Not fixed and noted: a message that comes in over the server border still does not
take the way from section 8.5 — it goes straight into the routing. With that
neither the store nor the priorities nor the type rules from D13 take hold for it.
That is no hole the store tore open, but one it makes visible: for the most
frequent case of a message to an absent person — the acquaintance on another
server — the store is not yet responsible.

Likewise noted: XEP-0160 advises not to store a message with exclusively XEP-0085
content (typing state). This client sends none, so there is no way to check that —
the rule would stay untested.

### D15. Incoming S2S stanzas ✅ — and a switch that was there already

The open point from D14, and the bigger of the two: section 8.5 speaks throughout
of an "inbound stanza" and asks nowhere whether it came from a client or from
another server. This server asked it all the same. What came over the border went
unseen into the routing — without a store, without priorities, without a
distinction by type.

With that the gap lay in exactly the most frequent case. The acquaintance on
another server is the rule and not the exception; two accounts on the same
instance are not. Whoever builds an offline store builds it above all for them —
and in D14 it did nothing for them.

Now both origins take one route, `DeliverMessageLocallyAsync`. The whole
difference sits in one parameter: `XMPPSession? origin` is `null` when the message
came from outside, and that alone decides about the `<sent>` carbons — those
belong to the other devices of the sender, and the ones of a foreign account are
not our business.

**Two of my branchings were superfluous, and the clearing up was the instructive
part.** I had first built two ways back for an error answer — into the stream of
the sender if they sit here, otherwise out over the border — and two ways to
determine the sender (`origin.FullJid` or the `from` of the stanza). Both pairs
were the same thing:

- `RouteToAsync` **is** the switch between "here" and "elsewhere"; its own comment
  says that since S4a. A branching beside it was a second answer to an already
  answered question — and two answers run apart with time. It settles the change
  of namespace along the way as well.
- The `from` is checked and not claimed in both cases: at a client the server
  stamps it itself, at a foreign stanza `AcceptFromRemoteAsync` has checked the
  sender domain against the far side. `origin.FullJid` beside it delivered the
  same string.

It came out at the mutations: both branchings could be removed without a test
noticing it — not because the tests were full of gaps, but because the lines did
nothing. **A surviving mutant is not always a missing check; sometimes it is
superfluous code that disguises itself as thoroughness.**

Ten mutations, nine struck down — two of them only at the second attempt, and both
times the test was to blame:

**The presence guard stood at the wrong place.** It was to show that only messages
take the new way. With Bob connected it passed with the mutation that sends
*everything* through the message route as well — for that delivers to a reachable
resource too. The wrong way becomes visible only where the two differ, and that is
the store: a `<presence/>` has no `type`, would thereby count as `normal` and
would lie ready at the next login as a presence from the day before yesterday. The
test now checks with Bob **absent**.

That is a new version of an old rule. "Observe the way, not the effect" meant until
now: look at what goes out. Here it means: **a guard against the wrong way has to
stand where the ways part.** At a place at which both do the same thing it guards
nothing.

**The refusal arrived but was addressed to the wrong one.** To replace the `to` of
the error answer by the address of the recipient survived: delivered it is by the
routing address, and that stayed right. Over the border it does not come out
anyway, because `RouteToAsync` sets a `StampTo` on going out and overwrites the
wrong `to` — at home it does. A client that checks under RFC 6120, section 8.1.1
whether a stanza is addressed to it would discard it silently. The local test of
the refusal from D13 checks that along now.

The one survivor is none about this code: does one leave out the question of the
origin in the offline branch, then the mutant throws a `NullReferenceException`
for a message from outside — and **no test sees it**. The `catch` at the processing
of a frame is meant for broken-off connections and swallows every programming
error along. Because the store is written before and nothing follows afterwards,
the throw stays without consequence. The line is right; the `catch` is the
problem, and it stands under "Later".

Not fixed and noted: presence and IQ from another server still take the straight
way. At presence the difference is small, at IQ it is not — a request to a bare JID
the server is to answer itself under section 8.5.2.1.3; distributed it is at
present to **all** resources, and each one answers. Several answers to one `id`.

### D16. The request to an account ✅ — and two sections, one behaviour

The open point from D15, and the only one of the series that broke a procedure
instead of merely violating a rule.

Section 8.5.2.1.3 says it twice: "the server itself MUST reply on behalf of the
user" **and** "MUST NOT deliver the IQ stanza to any of the user's available
resources". The doubling has a reason. IQ is a question-answer pair, held together
over the `id`, and every request received *must* be answered (RFC 6120, section
8.2.3, rule 3). Whoever distributes it to all resources gets an answer from all of
them — the one asking holds three answers to one `id` in hand and cannot decide
which one holds.

Exactly that this server did: every IQ request to a foreign address went into the
routing, and that distributed to a bare JID to every session it found. At a message
multiple delivery would be a nuisance; here it breaks the procedure.

**The answer is always `<service-unavailable/>`, and that is complete and not
half.** The section demands an answer of its own, "if the semantics of the
qualifying namespace define a reply that the server can provide on behalf of the
user" — and otherwise expressly this error. This server knows no namespace it
could answer in the name of a user; the place for a later one is marked.

**An answer is never answered.** Here two prescriptions stood against each other:
section 8.5.3.2.3 demands for "an IQ stanza" without a matching resource an error
and does not distinguish the type; RFC 6120, section 8.2.3, rule 4 forbids
answering a `result` or `error`. Rule 4 wins: an error on a `result` would go to
somebody who has asked nothing, under the `id` of a question they answered
themselves.

**The difference to the unknown account is instructive.** At a message the server
may under section 8.5.1 keep silent and thereby does not betray which accounts
there are; at a request it has to answer. Nothing is given away all the same,
because the answer is the same as for an existing account without a reachable
resource. Two tests therefore stand beside each other — were the answers
different, the server would have blabbed out a directory of its accounts.

Nine mutations, all struck down — after two rounds, and both taught something:

**Two sections, one behaviour.** I had treated the bare JID case and "full JID
without a matching resource" separately, because the RFC treats them in two
sections. A mutation that lifted the separation survived — and had to survive: the
sections 8.5.2.1.3, 8.5.2.2.3 and 8.5.3.2.3 all demand the same. **Where the
prescribed behaviour is the same, no test can distinguish the cases, and a
branching that does it all the same claims a difference that does not exist.** The
structure of an RFC is no plan of construction for branchings.

What has stayed is one line less: `SessionOf` compares exclusively full JIDs, a
bare JID therefore falls into the error branch of itself. The "MUST NOT deliver"
thereby hangs on a property of another method — held it is not by a check, but by
a test that logs in two resources and passes only when neither sees the request.
The mutation that restores exactly the old error (to all instead of to one) it
strikes down.

**The server address is no user.** A mutation that took the delivery way for users
for requests to the domain itself as well survived at first. It would have answered
`<service-unavailable/>` where the server today keeps silent — and that would be
worse: silence is a gap, an error is a statement, and this statement would be
wrong. A far side that believes it does not ask again. A test now holds fast that
the delivery way for users does not touch the server address.

**A red run that does not belong here, and it stays noted.** In one of four full
runs `TheStreamSurvivesABrokenConnection` failed against a foreign server with
"timeout at the waiting for: the resumed stream". The test drives a client against
Prosody or ejabberd; `XMPPServer` appears in it only as a static waiting aid, so
the changed delivery way not at all. On its own it runs green 4 out of 4 times,
after that two further full runs likewise.

The probable mechanism: the test breaks the connection off and gives the client 15
seconds for reconnecting together with resumption. Under the load of a full run —
many fixtures at the same time, the far side beyond the WSL loopback — that does
not always suffice at exponential backoff. Proved that is not: a single event
cannot separate the explanation from another one. This is why it stands under
"Later" and not as done.

Not fixed and noted: the second half of section 8.5.3.1. Whoever may not see the
presence of the recipient is not to get a request to that one's resource delivered
— the answer alone already betrays that the resource exists. That needs the
recording of directed presence, which does not exist here. Likewise open: a request
from a far side to our own server address (disco#info, ping) stays unanswered; the
answers for that stand in `HandleIqAsync` and want a session that does not exist at
S2S.

### D17. The answer alone is already information ✅

The open point from D16: the first half of section 8.5.3.1. An IQ request to a
resource is delivered only when the one asking may see the presence of the
recipient — otherwise `<service-unavailable/>`.

The reason stands in section 11 and is finer than it looks at first: **the answer
alone is already information.** Whoever asks a full JID and gets a result knows
that exactly this resource is logged in at this moment; whoever gets
`<service-unavailable/>` does not know it. Without the check the presence of a
human could be queried without ever having asked them for permission — and resource
names could be tried through until one answers.

This is why a test also checks that the refusal for an **existing** resource is the
same as for an invented one. Were the two different, the check would be without
effect: the one asking would read out of the kind of refusal what it is supposed to
keep from them.

**Two ways in, and both were necessary.** The roster of the recipient with `from`
or `both` — or directed presence (section 4.6). To take only the roster would be
too strict for the most frequent case of all: a conversation with somebody who does
not stand in the roster begins with one showing them one's presence (section 5.1).
Whoever has done that loses nothing more through an answer.

The list for that is new and follows section 4.6.1 word for word: per resource,
emptied when the user signs off, and an entry disappears as soon as directed
`unavailable` presence is sent to it. Both are MUST rules, and both have the same
reason as the check itself: a permission one cannot take back is none.

**The direction in the roster is easy to confuse, and `both` covers the confusion
up completely.** Asked is the half of the **recipient**: "that one may see me"
(`from` or `both`). A `to` means the opposite and would give the information to
exactly the wrong side — to everybody the recipient watches, instead of to
everybody who may watch them. At `both` both halves are right, and an
implementation that reads the wrong one does not come out. The test therefore sets
`to` first (refusal) and then `from` (delivery).

**Three existing tests documented a leak without noticing it.**
`PingBetweenClients_MeasuresRoundTrip` pinged a stranger, and two tests from D16
asked a foreign resource. All three passed only because the server did not know
the rule — a ping between two strangers is exactly the case it refuses. They now
make contacts first, which is moreover the more realistic setup.

Ten mutations, all struck down. Three of them the suite would not have held
before:

- Read the wrong roster half — struck down only by the new one-sided test.
- Note the directed presence with the full JID instead of with the bare JID —
  struck down only because a test writes to the full JID. Both forms now appear,
  because a client sends both.
- Apply the check to `result` and `error` as well. That looks like thoroughness
  and offends against the second half of the same section: "For an IQ stanza of
  type 'result' or 'error', the server MUST deliver the stanza to the resource."
  An answer belongs to the one who asked, and that one has had their permission
  with the question already.

Not fixed and noted: the SHOULD part of section 4.6.1 — an entity that sends us
`unavailable` is to disappear from the list. And section 4.6.3, rule 2: does the
resource become unavailable, then the sign-off is to go to every entity it has sent
directed presence to. The list for that exists now; the sending is missing.

### D18. A `catch` without a filter ✅ — and a measurement that turned the task round

The point from D15: around the processing of a frame there stood a `catch` without
a filter, with the note "connection broken off - in the test the normal case". I
wanted to narrow it to the exceptions a break really creates — and measured first.

**The measurement turned the task round.** I replaced the catching by an appending
to a file and let the whole suite run: **not a single exception.** The note no
longer held; the break has long since been caught elsewhere (`SendAsync` asks
`IsClosed`, Hermod delivers a `SentStatus` instead of throwing). What the catching
still achieved was exclusively the swallowing of programming errors.

With that the planned solution falls away. A list of exceptions a break "really"
creates would be guessed — and a branch no test reaches is exactly the sort of
precaution that covered the error of back then. There is nothing to filter.

**To remove it without replacement would have been wrong as well**, and that I
likewise looked up instead of supposing: Hermod catches above every exception out
of `ProcessTextMessage` and writes it away with `Logger.LogError`. Without our
catching the error would therefore wander from "discarded silently" to "in a log no
test looks at". Better, but not the solution.

The solution is **visibility**: `OnInternalError` reports session, frame and
exception; nothing further is thrown, nothing changes about the behaviour of the
server. And in the test suite a guard hangs on **every** test that treats every
report as a defect. Where such an error occurs one does not know beforehand — a
test of its own for it would guard only the way it goes itself.

**The proof is the mutation of back then.** The D15 survivor — leave out the
question of the origin before the `<sent>` carbons, which throws a
`NullReferenceException` for a message from outside — is now struck down by **six**
tests. For the first time in this series a step makes a survivor named earlier
mortal after the fact. The list of the named exceptions goes from six to five.

Five mutations on our own lines, all struck down — one only at the second attempt,
and it is the most interesting: **a guard nothing sets off is itself unguarded.**
The mutation "the guard always lets through" survived every test. It had to: where
no error is reported, a guard without effect behaves exactly like an effective one,
and a test that *has* to fail cannot be written as a passing test. Only the
separation of `Watch` (wiring) and `Record` (recording) made the guard directly
questionable — the same trap as at the old `catch`, only one level higher.

New and given reasons for: `FailFrameHandling`, a switch whose whole task is a
failure. Without it the way of reporting would be reachable by no test — the same
reason as at `SwallowClientStanzas`, and exactly the defect at which the old
catching stayed unnoticed for so long.

Not fixed and noted: the guard hangs on `AXMPPTests` and on the three fixtures that
deliver stanzas between two of our own servers (`FederationTests`,
`CrossDomainSubscriptionTests`, `RemoteDeliveryRulesTests`). Further fixtures run
servers of their own without being guarded — there it still holds that a
programming error lands only in Hermod's log. *(Fixed in D19.)*

### D19. The remaining fixtures ✅ — the guard where the server arises

The open point from D18. It was not nine fixtures, as noted there, but **eleven**:
`AccountStoreTests` and `AForeignPeerFederationTests` I had overlooked in the list.
It came out at the counting up of the places of creation, not at the reading of my
own note — a list one writes out of one's head is no stocktaking.

Now every server in the suite is guarded: `AXMPPTests` plus fourteen fixtures that
run their own.

**Wired over `Watched(…)`, not over a line of its own.** The three from D18 had
`_guard.Watch(_links)` standing separately under it; that is a second place one
forgets at the next server. `Watched(new XMPPServer(…))` gives the server back and
puts it under the guard — with that it stands where the server arises, and the
three from D18 are brought to the same form. Several fixtures create their servers
not in the SetUp anyway, but in the middle of the test; for those there is no other
usable place.

**Two fixtures needed a new `[SetUp]`**, and the reason is a property of NUnit that
is easy to overlook: a fixture instance is reused for all its tests. Without
`Reset()` the next test would take the report of the previous one along and fail at
a foreign error.

Three mutations, all struck down — all three on the same point: that `Watched` is
no pass-through. Hang no guard on, give a different server back, or short-circuit
the passing on in the test base. Checked that is on the real way: a second server
gets a client, fails on purpose, and the report has to arrive at the guard of the
same test. Without this test all eleven fixtures would be unguarded without a
single other test noticing it — where no error occurs, a missing guard looks like
an effective one.

**The third measurement, and the most complete:** a full run with both foreign
servers, every server of the suite guarded — **not a single report.** The old
`catch` was dead ballast across the whole suite, and that now stands not on one
measurement but on three.

Honestly noted: the wiring itself no test holds. Would somebody take the
`AssertClean()` out in a single fixture, it would not come out — a test for that
would have to set off an error in every fixture. Secured it is by a check of the
source: in the test project there stands no `new XMPPServer(` without `Watched(…)`,
with exactly two wanted exceptions — the server of the base, which is guarded in
the following line, and the auxiliary variable of the test that checks the return
of `Watched`.

### D20. A promise that ends ✅ — section 4.6.3, rule 2

The open point from D17: does a resource become unavailable, then the sign-off
goes to the recipients of its directed presence as well.

The rule closes a gap that nobody notices otherwise. Whoever shows a stranger
their presence does **not** stand in that one's roster because of it — and without
this way would never get an end. The stranger would carry the resource as present
for ever. And that is the rule, not the exception: a conversation with somebody
who does not stand in the roster begins under section 5.1 with exactly that. Since
D17 who may ask this resource anything at all hangs on the same list as well
(section 8.5.3.1) — a promise that never ends would thereby be doubly unpleasant.

**Two ways lead into unavailability, and the second is the more frequent one.**
The client's own sign-off, and the break of the connection, at which the server
creates it in their name (section 4.5.2). A client mostly disappears without
taking its leave; did the sign-off go only to the roster, then it would be exactly
then that the stranger stayed behind.

**The restriction to the roster is no formality.** Whoever stands in the roster
with `from` or `both` gets the sign-off over the ordinary distribution already.
The RFC narrows rule 2 to entities that do *not* stand in the roster that way for
the same reason — came it twice, a client that counts presence instead of
replacing it would get confused.

**The addition in brackets coincides with the list.** "if the user has not yet
sent directed unavailable presence to that entity": a directed sign-off takes the
recipient out of the list (section 4.6.1), and what does not stand in it is not
notified. Two prescriptions, one implementation — and a test that holds both at
once.

**Giving out and emptying in one call**, `TakeDirectedPresenceTargets`. That is
the core of the design: section 4.6.1 demands the emptying at the signing off,
rule 2 demands sending the sign-off to exactly these recipients beforehand. Were
they two calls, the second could be forgotten — this way nobody gets at the
recipients without emptying the list, and nobody empties it without holding it in
hand.

With that a negligence from D17 is fixed as well: the emptying stood there in
`RecordPresence`, that is, **before** the place that needs the list — and the way
over the break of the connection did not empty it at all. A stranger was allowed to
go on asking a broken-off resource.

Six mutations, all struck down — one only at the second attempt, and it is the
instructive one: **to fetch the list at *every* presence instead of only at the
sign-off.** No test survived that not, because none sent an ordinary presence after
the directed one — the order that is the rule in operation. A client reports a new
presence at every change to "away"; whoever empties the list in doing so takes both
from the other side in the middle of the conversation, the sign-off at the end and
the right to ask.

The lesson to that: **my tests let the client do exactly one thing per section, and
the mutation lived in the gap between the sections.** A test that checks only the
order it has built itself does not check the one that occurs.

A fixture of its own, `DirectedPresenceTests`. At first the tests stood in
`IqDeliveryRulesTests`, because the list had arisen there — they check however the
delivery of presence and not the one of IQ, and a test belongs where what it is
about is.

### D21. Whoever goes loses their place ✅ — and a reason that was wrong

The last open point at section 4.6: the SHOULD part of 4.6.1. Whoever sends the
user a sign-off disappears from that one's list of directed presence. With that the
section is complete.

**The two halves of the sentence look similar and mean the opposite.** The MUST
concerns our *own* revocation — "any entity **to which** the user sends directed
unavailable presence" —, the SHOULD the counter-direction: "any entity that
**sends** unavailable presence **to** the user". The other one goes, and with that
the temporary relationship is at an end as well. It becomes visible over section
8.5.3.1: without this way a returner would keep their right to ask although nobody
has shown them anything any more.

Looked at is the **receiving** and not the sending, for exactly so the rule is
formulated. The call therefore stands in `RouteToAsync` — the one switch through
which every stanza to a local address runs — and additionally in the two broadcast
loops that send directly to the session.

**And here lay the instructive error, this time not in the code but in my
reasoning.** Two mutations survived: the two broadcast loops. I had written into
the code that the forgetting is without a visible consequence for them — "whoever
stands in the roster keeps their right to ask over the roster". That was wrong,
because I had confused the two roster halves:

- That Alice's sign-off reaches Bob over the ordinary distribution is decided by
  **Alice's** roster: Bob stands there with `from`.
- Whether Alice may ask Bob anything is decided by **Bob's** roster.

At a one-sided roster — Alice's half filled, Bob's empty — the sign-off therefore
arrives while the right to ask hangs on the list alone. The way is very well
observable. Two new tests, both mutations struck down.

The lesson is more unpleasant than the usual ones: **a plausible-sounding reason
for "not observable" deserves the same check as the code.** Had I left it standing,
two named exceptions would have landed in the list — with an argument that did not
hold even at the writing down. The mutation pass refuted not the code but the
comment.

Seven mutations, all struck down.

### D22. The stream ends ✅ — a decision that has been taken differently

D18 made the failure at the processing of a frame visible and noted the going on
expressly as a **decision**, not as a gap. The decision has now been taken
differently: the stream ends with `<internal-server-error/>`.

The reason is the state. What the frame was to change is half changed, and nobody
knows how far — the client reckons with a state the server no longer has. Of all
errors the one that most probably leaves state behind stayed the only one without
consequences. Section 4.9.1.1 leaves no choice afterwards either: "Stream-level
errors are unrecoverable."

And the client loses nothing in doing so: `internal-server-error` counts as
repeatable, it builds the stream anew and begins with a state both sides are in
agreement about. That is more than a stream running on with a half-processed stanza
gives it.

**Three steps, and the middle one is the one one forgets over WebSocket.** Stream
error, then `<close/>` (RFC 7395, section 3.6 — it stands for the
`</stream:stream>`), then the connection. Without the `<close/>` the client sees a
socket that falls shut without a farewell, and that is a network failure and no
ended stream. Exactly this line survived a mutation at first: the stream error was
out already, and `OnStreamError` fired without it as well. Now the test checks the
frame on the wire.

**A test has replaced its opposite, and that is no contradiction.**
`TheConnectionSurvivesAReportedFailure` held fast in D18 that the stream runs on —
right for the decision of back then. In its place there now stands
`TheStreamEndsWithInternalServerError`. What remains of it is the second half of
its statement: the failed frame may not be delivered on a detour after all; that
now stands as a test of its own.

**A second test needed a correction, and the reason is instructive.**
`ASecondServer_IsWatchedThroughWatched` waited for the report about a *particular*
frame. Since the stream ends after the first failure, it hangs on chance which
frame that is — our own message or an `<a/>` of the stream management. The test
thereby checked the order of the frames instead of the wiring and now waits for
*any* report; from another server it cannot come. **A test that looks more closely
than necessary checks something other than meant at some point.**

Six mutations, all struck down — one only at the second attempt (the `<close/>`).

Not fixed and noted: `SendStreamErrorAsync` still sends only the error, without
closing. Section 4.9.1.1 demands both, and the distinction to `FailStreamAsync` is
a convenience for the callers in `S2SStream` and in the tests. *(The half-sentence
about `S2SStream` was wrong — see D23.)*

### D23. A choice that does not exist ✅

The point from D22: `SendStreamErrorAsync` sent the stream error without closing
the stream. Section 4.9.1.1 demands both, and in one go at that.

**First the stocktaking, and it refuted my own note.** In D22 I had written that
the separation is "a convenience for the callers in `S2SStream` and in the tests".
`S2SStream` has a method of **its own** with the same name and never calls the one
of the session — and its own has always closed the stream (`MarkClosed`). The
session variant was the only exception in the house, and of all things it carried
the same name as the right version beside it. That was the actual trap.

Left over were two callers, both tests — and both made up for the closing directly
afterwards with `session.Kill()` by hand. **There was therefore not a single caller
that needed the separation.** A choice nobody takes should not be offered by the
interface.

This is why no third method, but one fewer: `SendStreamErrorAsync` now does both,
and `FailStreamAsync` from D22 is gone again. With that the methods of the same
name in `XMPPSession` and `S2SStream` are not only called the same, they do the
same as well.

The two tests are lighter by their `Kill()` — and truer: they now reproduce a
rule-conformant server and not one that sends an error and afterwards pulls the
socket away separately.

Three mutations, all struck down. The revealing one is the first: that the closing
really happens is held by no test of its own, but by
`RecoverableStreamError_IsReportedButAllowsReconnect` — a reconnect presupposes
that the connection is gone. The test stood there for a long time and has got its
second purpose only now.

**The lesson stands in D19 already and repeats itself here word for word:** a list
one writes out of one's head is no stocktaking. Back then it was nine fixtures
instead of eleven, this time a caller that did not exist. Both times a `grep` would
have sufficed, and both times the wrong statement stood in the repository first.

### D24. The probe belongs to the server ✅ — and two tests that checked nothing

The last point at section 8.5: presence from another server. For available and
unavailable presence `RouteToAsync` already does the right thing — to a bare JID
all resources (8.5.2.1.2), to a full JID the matching one (8.5.3.1), otherwise
silently into the void (8.5.1 and 8.5.3.2.2). The error sat elsewhere: **at the
probe.**

All four sections refer for `type='probe'` to section 4.3: the server answers it
itself. Coming from the far side it went into the routing and landed at the client
— that one got to see a stanza that is not meant for it, and the asking far side
never got an answer. For a local client the probe has always been answered; the
same asymmetry as at message (D15) and IQ (D16), and the last of its kind.

**And the counter-direction was just as broken, which I did not know beforehand.**
The local probe branch took hold for *every* target, found no account for a foreign
address and returned — a probe to a contact on another server therefore never left
this server. Section 4.3.1 lets the server of the user send the probe out; now it
does that.

That came out only because a test failed that I held to be right.

**Two tests passed without checking what their name says — and both for the same
reason.** My new test waited for Alice to see Bob's state after she has sent a
probe. It passed even before the implementation existed. The reason is a race: Bob's
*first* presence is processed while the test sets the roster entry. Does it already
find it there, then it goes to Alice over the ordinary distribution — and the test
sees Bob's state without a probe ever having been answered. It now waits first for
Bob's first presence to be processed and sets the roster afterwards.

The same race sat in the **existing** local probe test from the S times:
`atBobs.Clear()` clears away what the login brings along — arrives it late, it
counts as an answer to the probe. It too would pass at a server that does not
answer probes at all. It now waits first for the delivery of the login and empties
afterwards.

Six mutations, all struck down — two of them only after these two corrections of
tests.

**And a self-correction that belongs here:** on the way there I suspected the
mutation script, because the same mutation was reported once as struck down and
once as surviving, and imputed a timestamp error to it. That was wrong — the
fluctuation came out of the race in the test. A tool that answers differently twice
is an obvious suspect; obvious is not the same as guilty, and the measurement
cleared it up, not the supposition.

Not fixed and noted: a probe to an unknown account stays unanswered. Section 8.5.1
leaves `<unsubscribed/>` and silence free; silence does not betray whether the
account exists, and at that it stays.

### D25. Neither question nor answer ✅ — section 8.2.3, rule 2

The point from D16: an IQ stanza without a `type` or with a value other than `get`,
`set`, `result`, `error` gets `<bad-request/>`. The actual content of the rule sits
in its subordinate clause — it obliges "the recipient **or an intermediate
router**". At every other stanza a server may hand through and let the recipient
judge; here not. The reason lies in the nature of IQ: a question-answer pair hangs
on `type` and `id`, and what carries none of the four values is neither question
nor answer. Does everybody hand it on, then it wanders through the network, and the
sender never learns what became of it.

**The stock was wrong differently in three roles, and only one of them was
silence.** Directed at the server address the stanza fell out at the back of
`HandleIqAsync`. To a foreign domain it went out — the role of the router,
unchecked. And to a local recipient it was **delivered**: `DeliverIqLocallyAsync`
asked only whether the type is `result` or `error` and treated everything else as a
request. The recipient thereby got something put before them that they would have
to answer under rule 3 and to which no answer fits. That was the worst of the three
cases and at the same time the one that looked the most orderly.

The check therefore stands right at the front at both entrances — in `HandleIqAsync`
before the delivery switch, in `AcceptFromRemoteAsync` before all delivery branches.
A test holds exactly this place fast:
`AnIqToTheServerItselfWithoutAType_IsRefused` would not pass if the check sat in the
delivery way, for what goes to the server itself never comes past there.

**And the client has the same rule in the other role.** It is "the recipient", and
it did nothing at all: the assignment to an open question takes only `result` and
`error`, the fallback at the end asks for `get` or `set` — a fifth value fell
through silently. Against this server something like that would never arrive at it;
against a foreign implementation without rule 2 very well.

The four values therefore stand **once** in the house, in `Jabber/Common/IqTypes.cs`.
Two enumerations could run apart, and the effect would be silent: a value the one
side knows and the other does not would get through or not depending on the way.

**Two decisions that are no formalities.**

The refusal goes out **without an `id`** as well and then carries none. That is
deliberately different from `RespondUnhandledIq`, which keeps silent without an
`id`, and the difference lies in the content: a `<service-unavailable/>` answers a
question, and an answer without an `id` can be assigned to none — it is of use to
nobody. `<bad-request/>` says something about the stanza itself, namely that its
form is not right, and the sender can use that without an assignment as well; all
the more as the missing `id` belongs to it itself under rule 1. An empty `id=''`
would be the worst outcome — it belongs to no question and would look as though it
belonged to one.

The sender is **this server**, not the intended recipient. `<service-unavailable/>`
answers in the name of the recipient, because the server has answered for them
there; here it did not accept the stanza in the first place. A recipient as sender
would claim that somebody had looked in.

**A comment became wrong through the change, and that is the D21 lesson in its more
harmless form.** In `DeliverIqLocallyAsync` there stood "or an unknown value that
this way treats like a request, because an answer is of more use than silence". That
was right as long as there was no check before it, and with it it describes a case
that does not arrive there any more. Unlike in D21 the reason was not already wrong
at the writing — it has become so. A comment ages with the code under it, and at
changing it belongs read along.

Seventeen mutations — one each for each of the four values, the check at each of
the three entrances, the answer itself, the two attributes and the type of error on
both sides.

**Three addenda out of the tool, all on the same theme — a measurement that does not
measure what it gives out that it measures.**

The reset check of the mutation script checked against `HEAD`. For a **new, not yet
tracked** file `git diff` never shows anything, and the check therefore reported
"CHECK" even after a clean reset — of all things at the one file this commit newly
creates. It now compares the hash against the backup, and that is the question it is
about: does what stood there before the mutation stand there again?

And the second mutation ran 35 minutes and had to be broken off. Falls `set` out of
the list, then the resource binding stays unanswered — and `ConnectAsync` waits for
it **without a deadline of its own**. A hanging run is no result: it does not say
whether the mutation survived, but only that nobody answers any more. The affected
runs now get `--blame-hang`, which makes a failure out of it. That a client waits
for the binding without a deadline stands under "Later" — here it came out only
because a mutation produced the case a test never produces.

And the third, the worst: after I had shot down the hanging run, a `testhost`
stayed behind and **locked the test DLL**. The next `dotnet test` thereby failed not
in the run but in the *build* already (MSB3027) — and the script filtered the output
for "Error:" and "Passed!", found nothing and wrote nothing. Six mutations thereby
looked as though they were settled and were not measured at all. It came out only
because a line without a verdict stood beside lines with a verdict.

Two changes out of that, and the first is the more important: finds the script no
summary, then it gives out the raw output instead of keeping silent. **A run without
a verdict may not look like a passed one.** And before every run it clears away
`testhost` processes left over.

The six have been repeated. All seventeen mutations are struck down.

**And the fourth addendum is the most instructive, because it was no error of the
tool but mine.** One mutation removes `result` from the list. I narrowed its run to
**a single test** — out of caution against a hanger like at `set`. Result: passed,
21 seconds. The mutant had survived.

Only the caution did not hold. `set` hangs because the **server** refuses the
binding; `result` concerns at the client only the receiving path, and the building
of the connection runs past it — which the 21 seconds themselves have proved. The
narrowing was therefore not only unnecessary, it had removed exactly the test that
strikes the mutation down: `TheFourKnownTypes_ReachTheResource` with the value
`result`. With the full filter it falls with five errors.

With that a *surviving* mutant has got a fifth meaning that did not yet appear in
D14 to D24: **the run did not execute the test that strikes it down.** The four
known meanings — a missing check, superfluous code, a test with an order that does
not occur, a wrong reason — all presuppose that what was to be measured was
measured. A narrowed filter violates exactly this precondition, and it does it
silently: the run reports "passed", not "not checked".

The price for the honesty was high here: the full run needed 18 minutes, because
with `result` refused almost every test runs into its waiting time. It was worth it.
A shortcut that changes the answer is no shortcut.

### D26. The switch guessed ✅ — a name is no prefix

The point from D25, and it was bigger than its occasion. The switch for incoming
frames compared prefixes: `StartsWith("<iq")` hits `<iqbogus/>` as well,
`StartsWith("<presence")` `<presence-probe/>` as well, `StartsWith("<open")`
`<opencast/>` as well.

**It came out at the most harmless of the three cases.** In D25 the server began to
answer an IQ stanza with an unusable type — and thereby answered an `<iqbogus/>` as
well, that is, an element that is no IQ stanza at all. The negligence was there
just the same before, only it did nothing visible. A check that begins to answer
makes the switch before it observable for the first time.

The actual damage lay elsewhere: **a `<presence-probe/>` ran into the presence
handling and counted there as presence.** That reads a missing `type` as "is
there" — a human was reported to their contacts as online because their element
happens to begin with the same eight characters. A statement about a human, derived
from a comparison of strings. And an `<opencast/>` counted as the opening of a
stream.

**The knowledge was in the house and lay at the wrong place.**
`StreamManagementManager.IsCountableStanza` has always read the element name
completely, together with the handling of the namespace prefix — it answers only a
different question (does the frame count for XEP-0198?) and was therefore never at
the disposal of the switch. The reader now stands as `StanzaElement` in
`Jabber/Common/`, and `IsCountableStanza` calls it.

One place stays deliberately independent: `XMPPSession.IsStanza`. There the note has
stood for a long time that the server side is **deliberately** implemented
independently of the client — used both the same helper function, then the tests
that hold the two counters against each other would check both sides with the same
logic, and a shared error of thought would stay undiscovered. The note holds, the
comparison of prefixes did not: `<iqbogus/>` counted along on the server side and
not at the client. Of all things the two counters that have to run alike would have
run apart. It now reads the name over a regular expression — a different way than
over there, the same answer — and a new test holds both at the same answer without
forcing them onto the same way.

**And what the switch does not know now ends the stream** — RFC 6120, section
4.9.3.24: "a first-level child of the stream that is not supported by the server".
Until now such a frame fell out silently at the back, and that was the convenient
answer and the poorer one: whoever sends something this server does not know
otherwise waits for an answer that never comes.

**Exactly this strictness killed a test, and the case is the most interesting of
the point.** `SendLockTests` sends 200 frames with 40 kB each, to measure the send
lock of the client and the integrity of the frames. As a payload an invented `<p/>`
served — **because** it is unknown and the server does nothing with it. The thought
was right: the frame is to be without consequence so that the test measures what it
wants to measure. The way there no longer carried, for "unknown" has not been
without consequence since this point: the first of the 200 frames broke the
connection for the remaining 199.

Without consequence is now achieved differently — an `iq` of type `result` without
a recipient, that is, an answer to the server about nothing. Rule 4 from section
8.2.3 forbids answering it; it is accepted, recorded and dropped. The same
consequencelessness, with an element that exists in the protocol.

At the rebuilding something else came out that the old frame had covered up: the
client sets the namespace `jabber:client` on every **stanza** (RFC 7395, section
3.3.3). The `<p/>` never got it, because it is no stanza — the new frame therefore
arrives differently than it was sent off. The test therefore no longer lays down the
order of the attributes, but it does lay down the frame as a whole, and that is
exactly its question.

**Not changed was the S2S stream.** It gets the same reading of the element name,
but not the new finality. The difference is no convenience but a question of
knowledge: on the client stream both sides speak the same, there a foreign
implementation stands opposite, and what Prosody or ejabberd otherwise send is **not
surveyed**. To break a stream off because one does not know an element would be a
bet against them. That stands under "Later" — to be measured, not to be supposed.

Incidentally a branching falls away there: `<stream:features/>` and `<features/>`
were two branches and are one element; which prefix is bound to the streams
namespace is up to the server (section 4.8.1).

**And once again the theme of D25, this time at its coarsest.** One mutation hung —
without `iq` there is no resource binding, and the client waits without a deadline
of its own (the same point as in D25, for the second time). I shot the `testhost`
processes down. That ends the running `dotnet test`, **not the script above it**:
the old pass ran on with the next mutation while the new one was already mutating.
Two scripts wrote the same files, and the numbers went visibly apart — 14 tests
here, 20 there, for the same question.

The comparison of hashes against the backup afterwards found a file that still
carried a mutation. **Exactly for that it is there**, and it was that which saved
the tree from a commit with an applied mutation — not my attention.

The lesson is not "pay better attention", but: whoever shoots a process down has to
know who its parent is. To end a child process looks like a breaking off and is only
an interruption — the order above it runs on, and from then on nobody measures any
more what they believe they are measuring.

Sixteen mutations, all struck down — ten at the reader, six at the switch and at the
two counters. The ten at the reader run without a net and need less than a minute
together; the switch costs its time, because it demands a server. Suite: 685 tests,
0 errors, 7 skipped, against Prosody and ejabberd.

### D27. Measure first, then become strict ✅

The point from D26, and it was noted expressly as a **measurement** and not as a
change: the S2S stream left an unknown element lying while the client connection has
refused it with `<unsupported-stanza-type/>` since D26. The reason for the
hesitation stood with it — on the client stream both sides speak the same, on the
S2S stream a foreign implementation stands opposite, and what Prosody and ejabberd
otherwise send there was not surveyed. To break a stream off because one does not
know an element would have been a bet.

**So first the feeler.** At the place at which a frame falls through all branches
came a recording with a deadline — every unknown frame with direction and domain
into a file. Then the full run against both far sides.

Result: **not a single frame.** 685 tests, dialback, SASL EXTERNAL, bidi, stream
management, TCP and WebSocket — nothing fell through.

**Two things made this measurement usable in the first place, and both would have
been easy to pass over.**

The first attempt ran only against Prosody: ejabberd had fallen away in between,
and 15 instead of 7 skipped tests betrayed it. Without the known baseline "found
nothing" would have looked like a result and would have been half the measurement.
Likewise the direction: the incoming tests run only **inside** WSL, and exactly
there the foreign server dials and speaks first. They have been made up for singly.

And then the question that would have made the rest worthless: **does the feeler
respond at all?** Across the whole suite it did not set off a single time — that is
exactly the picture a broken feeler gives as well. Shown it was only by the new
test: it feeds three unknown elements in, and the feeler recorded all three. A proof
about an absence is worth only as much as the proof that the presence would have
been visible.

With that the strictness is shown instead of supposed, and the S2S stream now holds
the same rule as the client connection. The full run against both far sides is at
the same time the standing counter-check: does one of them send something unknown
after all, then the stream dies and the federation tests fall.

**One line from D26 reached too far in doing so.** There *every* frame ended the
stream that the switch could not assign — an empty one as well. Section 4.9.3.24
speaks however of "a first-level child of the stream that is not supported", and an
empty frame is no child that is not supported; it is no child. Over TCP that does
not come out, because `SkipProlog` in the splitter swallows whitespace, XML
declarations and comments anyway — and whitespace as a keepalive is expressly
allowed on a stream (section 4.6.1). Over WebSocket every frame is handed through,
and there an empty frame would have cost the connection. Both ways now
distinguish.

Five mutations, all struck down. Three of them broke off at first, and the breaking
off was a find: `S2SStream.cs` has **LF** line endings, the repository is mixed, and
a multi-line search pattern therefore fitted only by chance. The mutation script now
tries both variants — and keeps the encoding it found at the writing back, instead
of silently giving an LF file a BOM. At least this failure was loud; the three
silent ones from D25 were dearer.

### D28. Ein Abbruch ist kein Verstoss ✅ — Abschnitt 6.4.4

Der Punkt aus D26: Ein `<abort/>` aus der SASL-Aushandlung bekam seit D26 einen
Stream-Fehler. Wörtlich war das nicht falsch — der Server unterstützte das
Element nicht, und Abschnitt 4.9.3.24 passt auf jedes Element, das er nicht
kennt. Es war die schlechtere von zwei Antworten.

**Der Unterschied ist keine Feinheit.** Der Abbruch ist ein *vorgesehener*
Schritt der Aushandlung, kein Protokollverstoss: Abschnitt 6.4.4 sieht ihn
ausdrücklich vor und verlangt `<failure><aborted/></failure>`. Wer ihn mit dem
Ende des Streams beantwortet, zwingt den Client zu einer neuen Verbindung für
etwas, das der RFC innerhalb der bestehenden vorsieht.

Der halbe SCRAM-Austausch wird dabei verworfen, und das ist der eigentliche
Inhalt eines Abbruchs. Bliebe er liegen, liesse er sich mit einer später
nachgeschobenen `<response/>` noch zu Ende führen — der Abbruch wäre dann eine
Höflichkeitsfloskel und keine Aussage. Ein eigener Test hält das fest.

**Der S2S-Stream hatte dieselbe Lücke, und die ist meine eigene aus D27.** Vor
der Strenge blieb ein `<abort/>` dort liegen, danach beendete es den Stream.
Dieselbe Antwort ist nachgezogen — mit einem Unterschied: Zu verwerfen ist
nichts, weil SASL-EXTERNAL ein einziger Zug ist und keinen halben Austausch
kennt. Und wer selbst angewählt hat, beantwortet keinen Abbruch; er wäre der,
der ihn schickt.

**Die Lehre gehört zu D26 und D27 und schliesst sie ab:** Wer eine Weiche streng
macht, erbt jede Antwort, die sie noch nicht kennt. Vorher fiel Unbekanntes
stillschweigend hinten heraus, und jede fehlende Antwort war eine Lücke ohne
Folgen; danach ist jede fehlende Antwort ein beendeter Stream. Die Strenge war
richtig — aber sie verwandelt Unterlassungen in Schäden, und die Liste dessen,
was noch fehlt, gehört ab da abgearbeitet und nicht nur geführt.

Geprüft wird über einen rohen `ClientWebSocket` nach dem Vorbild aus
`WebSocketFederationTests`: Der Abbruch gehört **mitten** in die Aushandlung, und
dort führt der richtige Client sein eigenes Gespräch. Nur von Hand lässt sich ein
halb begonnener SCRAM-Austausch überhaupt herstellen.

Fünf Mutationen, alle erschlagen — zwei davon erst nach einer Korrektur an den
Tests.

Die eine war eine Lücke: Für die Gegenrichtung — ein Initiator, der einen
Abbruch bekommt — gab es keinen Test. Statt sie als bekannten Überlebenden zu
vermerken, ist der Test nachgetragen.

**Die andere ist die lehrreichere, und sie ist wieder die Falle aus D20 und
D24.** Die Mutation lässt den halben SCRAM-Austausch nach dem Abbruch stehen —
und mein Test dafür bestand trotzdem. Er schob nach dem Abbruch eine
**unsinnige** `<response/>` nach und prüfte auf `not-authorized`. Nur ergibt
eine unsinnige Antwort `not-authorized`, ob der Austausch nun verworfen wurde
oder nicht: Beide Welten geben dieselbe Antwort, und der Test prüfte nichts.

Erst eine Antwort, die **durchginge**, trennt die Fälle. Sie wird jetzt mit dem
echten `SCRAMAuthenticator` des Clients gebaut — mit ihr führte der liegen
gebliebene Austausch zu `<success/>`, mit verworfenem zu einer Absage. Der Test
prüft seitdem auch, dass **kein** `<success/>` kommt, und das ist die Hälfte, um
die es eigentlich geht.

Das Muster wiederholt sich damit zum dritten Mal, und es ist immer dasselbe:
Ein Test stellt eine Lage her, in der die richtige und die falsche Fassung
dasselbe antworten. Er sieht dann aus wie ein Nachweis und ist keiner. Die
Gegenprobe dafür ist billig und gehört zur Gewohnheit — **welche Antwort gäbe
der Server ohne diese Zeile?** Ist es dieselbe, prüft der Test die Zeile nicht.

### D29. Ein bekannter Namensraum macht das Element nicht bekannt ✅

Die letzte Stelle im Haus, an der ein Rahmen noch stillschweigend hinten
herausfiel: Der Zweig für XEP-0198 prüfte den **Namensraum** und liess alles
darin fallen, was er nicht kannte.

Abschnitt 4.9.3.24 nennt ausdrücklich beides — „because the receiving entity
does not understand the namespace **or** because the receiving entity does not
understand the element name for the applicable namespace". Der zweite Halbsatz
ist genau dieser Fall, und er war der einzige, der noch offen stand.

**Der interessantere der beiden geprüften Fälle ist nicht das erfundene
Element, sondern `<enabled/>`.** Das ist ein *richtiges* Element aus XEP-0198 —
nur schickt es der Server an den Client und nicht umgekehrt. Bekannt heisst
nicht „bekannt in dieser Richtung", und ein Zweig, der nur den Namensraum
ansieht, kann diesen Unterschied gar nicht machen.

Umgesetzt ist es als Rückgabewert statt als zweiter Prüfung: Der Zweig sagt
jetzt, ob er zuständig war, und was er nicht kennt, fällt weiter nach unten und
bekommt dieselbe Antwort wie jedes andere unbekannte Element. Eine zweite Liste
der bekannten Namen neben der ersten wäre die naheliegende Lösung gewesen und
die schlechtere — zwei Aufzählungen, die auseinanderlaufen können, für eine
Frage, die der Zweig ohnehin schon beantwortet.

Sechs Mutationen, alle erschlagen — je eine für jeden der vier Zweige, die
Weiche selbst und den Rückfall am Ende. Eine davon erst nach einem
nachgetragenen Test, und die ist der eigentliche Fund dieses Punktes.

**Der `<a/>`-Zweig war von keinem Test erreicht.** Die Mutation erklärte ihn für
unzuständig — womit die Bestätigung des Clients seit dieser Änderung den Stream
beendet hätte —, und kein einziger Test fiel darüber. Über eine echte Verbindung
hat nie ein Client ein `<a/>` an den Server geschickt: Geprüft war nur der
Zähler für sich, in `StanzaCountingTests`, nie sein Weg durch den Server.

**Die Lücke ist älter als die Zeile, die sie sichtbar gemacht hat.** Der Zweig
gab vorher nichts zurück; ob er lief, war von aussen nicht zu sehen. Erst der
Rückgabewert hat ihn beobachtbar gemacht — und eine Mutation daran konnte
überhaupt erst auffallen. Ein Zweig, dessen Wirkung niemand beobachtet, sieht
aus wie einer, den niemand braucht.

Das ist dasselbe Muster wie in D26, nur andersherum: Dort machte eine neue
Antwort eine alte Nachlässigkeit sichtbar, hier macht ein neuer Rückgabewert
eine alte Testlücke sichtbar. **Beobachtbarkeit ist keine Nebenwirkung einer
Änderung, sondern manchmal ihr grösserer Teil.**

**Und der Punkt aus D25 hat heute zum vierten Mal zugeschlagen.** Jede Mutation,
die die Aushandlung zerbricht — `set` aus der Typliste (D25), `iq` aus der
Weiche (D26), `<abort/>` ohne Antwort, `<enable/>` als unbehandelt (hier) —,
lässt den Lauf **hängen** statt scheitern: `XMPPConnection.ConnectAsync` wartet
ohne eigene Frist auf eine Antwort, die nie kommt. Viermal derselbe Befund aus
vier verschiedenen Richtungen ist kein Zufall mehr, sondern eine Eigenschaft.

Der Umgang damit ist inzwischen eingespielt und hat selbst zwei Lehren gekostet:
`--blame-hang` macht aus dem Hänger einen Fehlschlag, und **der Filter bleibt
dabei unverändert** — eine Verengung hat in D25 aus einem erschlagenen Mutanten
einen überlebenden gemacht. Abgeschossen wird das Skript und nicht der
Testprozess; in D26 lief sonst der alte Durchgang neben dem neuen weiter.
Beim Abbruch hier trug die Datei wieder eine Mutation, und gefunden hat sie —
zum zweiten Mal — der Hash-Vergleich gegen die Sicherung und nicht meine
Aufmerksamkeit.

Damit ist die Reihe D26 bis D29 abgeschlossen: Erst riet die Weiche (D26), dann
wurde sie streng (D26, D27), dann kamen die Antworten nach, die sie durch die
Strenge schuldig wurde (D28, D29). **Der Bogen ist die eigentliche Lehre.** Eine
Nachlässigkeit, die nichts tut, kostet nichts — bis eine Verschärfung daneben
sie in einen Schaden verwandelt. Wer verschärft, übernimmt damit auch alles,
was vorher folgenlos fehlte.

### D30. Schweigen kommt nicht an ✅ — und mein Vermerk war falsch

Der Punkt, der heute fünfmal zugeschlagen hat: Jede Mutation, die die
Aushandlung zerbricht, liess den Lauf **hängen** statt scheitern. Fünfmal
derselbe Befund aus fünf Richtungen ist keine Beobachtung mehr, sondern eine
Eigenschaft.

**Und die erste Handlung war, den eigenen Vermerk zu widerlegen.** Er lautete
seit D25: „`ConnectAsync` wartet ohne eigene Frist auf die Antwort zum Resource
Binding". Das Binding hat sehr wohl eine Frist — `SendIqAsync` setzt sie seit
jeher, zehn Sekunden. Ohne Frist waren die **Lese-Schritte** davor: Stream-Kopf,
Features und jede SASL-Runde gehen über `ReceiveStanzaAsync`, und das wartete
allein auf dem Token des Aufrufers.

Dieselbe Lehre wie in D19 und D23, diesmal an einer Diagnose statt an einer
Liste: Ein aus dem Kopf geschriebener Vermerk ist keine Bestandsaufnahme. Hätte
ich ihn geglaubt, hätte ich eine Frist an eine Stelle gesetzt, die schon eine
hat, und den Fehler behalten.

**Was ein Fehlschlag nicht herstellt, ist Schweigen.** Ein Fehler kommt an, ein
geschlossener Socket kommt an — beides bringt die Aushandlung zum Abschluss.
Schweigen kommt nicht an. Deshalb liess sich der Fall mit keinem der
vorhandenen Testschalter nachstellen, und deshalb gibt es jetzt
`XMPPServer.AnswerStreamOpen`: eine Gegenstelle, die die Verbindung annimmt und
dann nichts mehr sagt. Kein erfundener Fall — ein Server hinter einer
Zustandstabelle, die den Rückweg vergessen hat, verhält sich genau so, und es
ist der unangenehmste Ausgang von allen, weil der Aufrufer nie erfährt, dass
etwas nicht stimmt.

Die Frist gilt dem **Schritt** und nicht dem einzelnen Lesevorgang: Ein Rahmen,
der in Stücken ankommt, darf zusammen nicht länger brauchen als einer am Stück.
Und sie nennt, worauf gewartet wurde — „auf den Stream-Kopf", „auf die
SCRAM-Challenge". Eine abgelaufene Frist ohne diese Angabe verschiebt die Suche
nur: Der Aufrufer weiss dann, dass etwas nicht kam, aber nicht, was. Genau daran
habe ich heute mehrfach Zeit verloren.

Vier Mutationen, alle erschlagen — die Frist selbst, beide Hälften der Meldung
und der neue Testschalter. Eine brach zuerst ab, weil **PowerShell 5.1 ein
Skript ohne BOM in der ANSI-Codepage liest** und das „ü" im Suchmuster
verstümmelt ankam. Die Mutationsskripte tragen jetzt ein BOM. Immerhin war
dieser Fehlschlag laut; die stillen aus D25 waren teurer.

**Ein zweiter Irrtum steckte im eigenen Test.** Er erwartete zuerst eine
Ausnahme aus `ConnectAsync` — die kommt nicht, weil `ConnectInternalAsync` jeden
Verbindungsfehler abfängt und über `OnError` und den Zustand meldet. Das ist die
Bauart des Hauses und war nie der Mangel: Der Mangel war, dass der Aufruf **gar
nicht zurückkam**. Geprüft wird jetzt die Rückkehr und die Meldung. Ob ein
stillschweigend zurückkehrendes `ConnectAsync` eine gute Schnittstelle ist, ist
eine andere Frage, betrifft jeden Aufrufer und steht unter „Später".

### D31. Ein Aufruf, der nichts sagt ✅

Der Punkt aus D30, und er war ausdrücklich als **Entwurfsentscheidung** vermerkt
und nicht als Fehler: `ConnectAsync` kehrte bei einem gescheiterten Aufbau
stillschweigend zurück. Der Fehler ging an `OnError` und an den Zustand — wer
nichts abonniert hatte, sah zwischen gelungen und gescheitert keinen
Unterschied und arbeitete auf einer Verbindung weiter, die es nicht gibt.

Dasselbe Übel wie in D30, eine Ebene höher: **Dort kam gar keine Antwort, hier
kommt eine, die nichts sagt.**

Ein Rückgabewert hätte es nicht behoben. Einen kann man ignorieren, und ein
ignorierter Rückgabewert ist wieder Schweigen — genau die Eigenschaft, um die es
geht. Also wirft der Aufruf.

**Geworfen wird der ursprüngliche Fehler**, nicht eine Hülle darum: Ein falsches
Passwort bleibt eine `AuthenticationException`, eine Zeitüberschreitung eine
`XMPPProtocolException`, und der Aufrufer unterscheidet sie, ohne in einer
Meldung zu lesen. Der Stapel bleibt der des Fehlers und nicht der dieser Stelle.

**Und nur der ausdrückliche Aufruf wirft.** Der Wiederverbindungsversuch im
Hintergrund läuft durch dieselbe `ConnectInternalAsync`, hat aber keinen
Aufrufer, dem er etwas schulden könnte; er meldet weiterhin über Ereignisse.
Deshalb steht die Entscheidung in `ConnectAsync` und nicht dort, wo der Fehler
entsteht — der Unterschied ist nicht die Art des Fehlers, sondern ob jemand auf
eine Antwort wartet.

**Der Preis war messbar, und er ist der eigentliche Ertrag.** Elf Tests fielen,
und es waren genau die elf, die einen erwarteten Fehlschlag prüfen: falsches
Passwort, unbekanntes Konto, verfälschte Serversignatur, abgelehntes Zertifikat,
abgelehntes Binding, Downgrade-Schutz. Alle elf standen auf einem blossen
`await` und den Zusicherungen danach — was nur ging, weil der Aufruf schwieg.

Sie laufen jetzt über einen gemeinsamen Helfer, `FailingConnectAsync`, der die
Erwartung ausdrücklich macht: **hier muss es scheitern.** Damit prüfen die elf
eine Zusicherung mehr als vorher — dass der Fehlschlag überhaupt beim Aufrufer
ankommt. Der Radius einer Entwurfsänderung ist selten nur Aufwand; hier war er
die Liste der Stellen, die von der stillen Rückkehr gelebt haben.

Fünf Mutationen, vier erschlagen. Die fünfte ist eine **benannte Ausnahme**: Das
Zurücksetzen von `_lastConnectError` zu Beginn ist heute unbeobachtbar. Gelesen
wird das Feld nur, wenn der Zustand nicht `Connected` ist — und dorthin führt
kein Weg, der nicht vorher durch einen der beiden `catch` gelaufen wäre, die es
frisch setzen. Die Zeile bleibt trotzdem stehen: Sie verhindert, dass ein
künftiger Pfad, der ohne `catch` scheitert, einen Fehler von vorgestern wirft.
Vorkehrung, nicht Wirkung — wie die Abkürzung über die leere Offline-Ablage aus
D14.

### D32. Der Fehlschlag ohne Namen hatte einen ✅

Der offene Punkt aus D29: Ein Vollauf meldete **einen** Fehlschlag, der nächste
gleiche Lauf war grün, und der Name steckte in der weggeworfenen Ausgabe.

Wiederfinden liess er sich nicht — wiederholen schon. Drei Vollläufe unter den
Bedingungen von damals (ejabberd weg, 16 übersprungen), diesmal vollständig
mitgeschnitten. Der erste Lauf hatte ihn:

```
Fehler AnAckFromTheClient_IsProcessedAndClearsTheQueue
  Expected: less than 2
  But was:  3
```

**Es war mein eigener Test aus D29** — der, der die Lücke im `<a/>`-Zweig
geschlossen hat. Er stand seit einem Tag im Baum, und der unerklärte Fehlschlag
kam im selben Durchgang; der Verdacht lag also nahe und war trotzdem nur ein
Verdacht, bis der Mitschnitt ihn benannt hat.

**Der Fehler ist ein Massfehler und kein Wettlauf im üblichen Sinn.** Der Test
prüfte: „nach der Bestätigung sind weniger Stanzas offen als vorher". Eine
Bestätigung sagt aber nichts über eine *Anzahl*. Sie sagt: **alles bis zu dieser
Folgenummer ist erledigt.** Was danach hereinkommt — Bobs Presence, ein paar
Millisekunden später —, lässt die Warteschlange wieder wachsen, und die Anzahl
steigt, obwohl die Bestätigung genau das Richtige getan hat.

Geprüft wird jetzt die Folgenummer: keine offene Stanza mit `Seq <= h`. Damit
darf nach der Bestätigung ankommen, was will.

**Und die Gegenprobe war der wichtigere Teil.** Ein entflockter Test wird leicht
zu einem, der nichts mehr prüft — die bequemste Art, einen Wackelkandidaten
loszuwerden, ist, ihm die Zusicherung zu nehmen. Deshalb lief die Mutation aus
D29 (`<a/>` gilt als unbehandelt) noch einmal gegen die neue Fassung: Sie fällt
weiterhin. Entflockt, nicht entschärft.

**Die Bestätigungsläufe haben dann einen zweiten, anderen Wackelkandidaten
gezeigt** — und diesmal lag der Mitschnitt sofort vor:
`AFailureWhileHandlingAFrame_IsReported` meldete

```
Expected: String containing "ausloeser"
But was:  "<presence xmlns='jabber:client'><c xmlns='...caps' .../></presence>"
```

Derselbe Massfehler in anderer Gestalt. Der Test legt den Fehlschalter um und
schickt einen Rahmen; genommen hat er dann die **erste** Meldung überhaupt — und
das war gelegentlich die automatische Anmelde-Presence des Clients, die noch
unterwegs war, als der Schalter umging. Was zuerst gemeldet wird, entscheidet
der Zeitverlauf; was der Test wissen will, ist eine andere Frage. Gesucht wird
jetzt die Meldung **zum eigenen Rahmen**.

Beide Male dieselbe Gegenprobe: Ein entflockter Test wird leicht zu einem, der
nichts mehr prüft, und die bequemste Art, einen Wackler loszuwerden, ist, ihm
die Zusicherung zu nehmen. Deshalb lief gegen jede neue Fassung die Mutation,
die sie halten soll — `<a/>` gilt als unbehandelt (D29) und der Frame wird nicht
mitgemeldet (D18). Beide fallen weiterhin.

Zwei Dinge zur Arbeitsweise, beide selbstverschuldet: Ich habe die Testdatei
geändert, **während** der zweite Jagdlauf lief — dessen Ergebnis war damit
wertlos, und ich habe die Jagd abgebrochen statt es zu verwenden. Das ist
dieselbe Nachlässigkeit wie in D26, nur ohne Schaden, weil sie diesmal sofort
auffiel. Und der Fund selbst hängt allein daran, dass Vollläufe seit D29
vollständig in eine Datei gehen: **Ein Fehlschlag ohne Namen ist einer, den man
nicht wiederfindet** — die Regel, die aus dem Fall entstanden ist, hat den Fall
gelöst.

**Und eine Zahl, die nachdenklich macht:** In sieben Vollläufen an diesem Abend
fielen zwei verschiedene Tests je einmal. Beide waren Massfehler in Tests, die
ich selbst geschrieben habe, beide entstanden dadurch, dass etwas Nebenläufiges
— eine Presence — zwischen Messung und Prüfung geriet. Der Verdacht liegt nahe,
dass es nicht die letzten sind; die Jagd bleibt deshalb ein wiederholbares
Werkzeug und keine einmalige Aktion.

### D33. Eine Vermutung, die nicht trug ✅

Der letzte offene Wackelkandidat, aus D16: `TheStreamSurvivesABrokenConnection`
gegen einen Fremdserver scheiterte in einem von vier Vollläufen mit einer
Zeitüberschreitung, allein aber vier von vier Mal grün. Der Vermerk nannte einen
Verdacht — „15 Sekunden für Wiederverbindung samt Wiederaufnahme sind unter Last
des vollen Laufs mit exponentiellem Backoff knapp" — und die ausdrückliche
Auflage, **vor** einer Änderung der Wartezeit zu klären, ob wirklich der Backoff
bremst.

**Geklärt ist jetzt, dass der Verdacht nicht trägt.** Zwanzig gezielte
Durchgänge, vierzig Ausführungen gegen beide Gegenstellen, jede einzelne
zwischen **519 und 669 Millisekunden** — eine Verteilung ohne jeden Ausreisser,
bei einer Frist von 15 Sekunden. Das ist rund fünfundzwanzigfache Luft und kein
knappes Budget. Die Frist bleibt deshalb unverändert; sie zu erhöhen hätte einen
Befund vorgetäuscht, den es nicht gibt.

Wiederholen liess sich der Fehlschlag nicht — auch nicht in den sieben
Vollläufen aus D32. Möglich ist, dass D30 ihn nebenbei beseitigt hat: Vor D30
konnte ein Lese-Schritt der Aushandlung **unbegrenzt** hängen, und ein
Wiederverbindungsversuch, der dort steckenblieb, hätte genau dieses Bild
ergeben — Frist abgelaufen, kein Fortschritt. Das ist eine Erklärung, die zum
Symptom passt, und kein Nachweis; sie steht hier als das, was sie ist.

**Was bleibt, ist die Vorsorge, und die ist der eigentliche Ertrag.** Beim
Scheitern sagte die Meldung bisher nur „Zeitüberschreitung beim Warten auf: den
wiederaufgenommenen Stream" — nichts darüber, wie weit der Client gekommen ist.
Genau daran ist der Fall in D16 gescheitert. Der Zähler schreibt jetzt den
Verlauf mit: jeden Zustandswechsel und jeden gemeldeten Fehler. Erzwungen
nachgestellt sieht das so aus:

```
Der Stream wurde binnen 15 Sekunden nicht wieder aufgenommen.
Verlauf: Connected->Disconnected
```

— und man sieht sofort, dass der Client es nicht einmal versucht hat. Bei einem
echten Vorfall stünde dort die ganze Kette samt Fehlern.

**Ein Fehlschlag, der sich selbst erklärt, kostet einmal Schreibarbeit; einer,
der es nicht tut, kostet jedes Mal eine Untersuchung.** In D29 hat mich das eine
verlorene Diagnose gekostet, in D16 eine, die sechzehn Punkte lang offen blieb.

Nebenbei aufgeräumt: Beim Ergänzen des `using` hatte ich ein CRLF in eine
LF-Datei geschrieben — genau die Vermischung, auf die ich in D26 noch geprüft
und die ich diesmal selbst erzeugt hatte. Aufgefallen ist sie, weil das
Suchmuster für die Gegenprobe nicht passte; die Datei ist wieder durchgehend LF.

### D34. Eine Fabrik, die nichts bauen kann ✅

`XMPPConnection.CreateTcp` erzeugte eine `tcp://`-URI, die `ClientWebSocket`
ablehnt. Der Vermerk stand seit langem und liess zwei Wege offen: echt
implementieren oder entfernen.

**Die Bestandsaufnahme hat die Entscheidung vorbereitet, nicht ersetzt.** Die
Methode hat **null Aufrufer** — nicht in den Tests, nicht in `Program.cs`,
nirgends. Sie ist öffentliche Oberfläche, die dokumentiert nicht funktioniert,
und ihr eigener Kommentar sagte das seit jeher: „NICHT funktionsfähig".

Der Umfang der Alternative war ebenso zu messen: Der Client fasst den WebSocket
an **neun** Stellen unmittelbar an — Verbinden, Senden, die beiden
Empfangspfade, Abbruch. Ein echter TCP-Transport verlangt also eine
Transportabstraktion, dazu clientseitiges STARTTLS und die TCP-Rahmung. Die
Bausteine gibt es (`XmlStreamSplitter`, STARTTLS), aber auf der S2S-Seite und
für `jabber:server` geformt. Das ist ein eigenes Vorhaben und keine Reparatur.

Entfernt. **Eine öffentliche Methode, die nicht funktionieren kann, ist
schlechter als keine** — sie sieht aus wie ein Angebot, kostet den Aufrufer
einen Versuch und liefert einen Gegenstand, der beim ersten Gebrauch scheitert.
Solange niemand sie ruft, ist das Entfernen der billigste ehrliche Schritt.

Der TCP-Transport bleibt unter „Später" stehen, jetzt mit dem gemessenen Umfang
und dem Prüfziel: Prosody lauscht auf 127.0.0.1:5222, ein echter Transport wäre
also gegen eine fremde Gegenstelle nachweisbar.

**Ohne Mutationstest, und das ist hier kein Versäumnis.** Es kommt keine
Verhaltenszeile hinzu, die man umdrehen könnte; die Prüfung einer Entfernung ist
die Frage, ob jemand sie gebraucht hat, und die beantworten Übersetzer und
Vollauf. Beide sagen nein.

### D35. Zahlen sagen nie, was fehlt ✅

Beim Prüflauf zu D34 fiel ein dritter Wackelkandidat auf —
`NonzasDoNotAdvanceTheCount` gegen Prosody, ein Fehlschlag in einem Vollauf:

```
Wir haben Nonzas mitgezählt.  Expected: 6  But was: 8
```

Zwei ausgehende Stanzas mehr, als der Test geschickt hat. **Welche zwei, sagt
die Zahl nicht** — und damit stand ich vor derselben Sackgasse wie in D16 und
D29.

Eine naheliegende Erklärung ist geprüft und **widerlegt**: Der Test schickt an
sich selbst, die Nachrichten kommen also zurück, und der Verdacht lag auf einer
automatischen Antwort des Clients. Die verlangt aber ein `<request/>`
(XEP-0184) oder ein `<markable/>` (XEP-0333) im Rahmen, und die Testnachrichten
tragen nur einen `<body>`. Sie lösen nichts aus. Ein Verdacht, der sich in fünf
Minuten widerlegen lässt, ist die billigste Art, ihn loszuwerden.

Reproduzieren liess er sich nicht: zwanzig Ausführungen gegen beide
Gegenstellen, alle grün, mit sehr enger Streuung. Genau die Lage aus D33 — und
deshalb dieselbe Antwort. Der Test schneidet jetzt mit, **was tatsächlich
hinausgeht**, und legt es der Meldung bei. Beim nächsten Vorfall stehen die zwei
überzähligen Stanzas im Klartext da, statt dass wieder nur eine Zahl bleibt.

Damit ist das dreimal dasselbe Muster in einer Sitzung: D16, D29 und jetzt hier.
**Eine Zusicherung über eine Zahl sagt, dass etwas nicht stimmt, und nie was.**
Wo der Gegenstand billig mitzuschreiben ist — der Verlauf, der Rahmen, der
Mitschnitt —, gehört er in die Meldung, und zwar bevor der erste Fehlschlag
kommt und nicht danach.

### D36. Die Auskunft hängt nicht daran, wer fragt ✅

Der Punkt aus D16: Eine IQ-Anfrage von einer Gegenstelle an die **eigene
Serveradresse** — Ping, disco#info — blieb unbeantwortet, obwohl RFC 6120,
Abschnitt 8.2.3, Regel 3 eine Antwort verlangt. Sie ging ins Routing, fand dort
für die Domain keine Sitzung und verschwand.

**Der Grund für die Lücke war die Bauform, nicht das Wissen.** Die Antworten gab
es längst — sie standen mitten in `HandleIqAsync` und schrieben unmittelbar in
eine Client-Sitzung. Damit waren sie an einen Client gebunden, und eine
Gegenstelle hat keinen.

Also getrennt, was verschieden ist: `AnswerAboutSelf` **baut** die Antwort und
verschickt sie nicht. Der hiesige Client bekommt sie über seine Sitzung, die
Gegenstelle über `RouteToAsync` — **der Rückweg ist der einzige Unterschied.**
Was dieser Server kann, ist für beide dasselbe, und es zweimal aufzuschreiben
hiesse, zwei Auskünfte über dieselbe Sache zu führen, die auseinanderlaufen
können.

**Was nicht mitgewandert ist, ist die eigentliche Arbeit an diesem Punkt.**
Binding, Legacy Session, Carbons und der Roster stehen ebenfalls in
`HandleIqAsync` — aber sie ändern den Zustand *einer Sitzung* oder gehören einem
Konto. Sie bleiben, wo sie sind, und damit für eine Gegenstelle unerreichbar:
Ein fremder Server, der nach unserem Roster fragt, bekommt
`<service-unavailable/>` wie für jede andere unbekannte Anfrage. Die Trennlinie
verläuft nicht zwischen „beantwortbar" und „nicht beantwortbar", sondern
zwischen **Auskunft über den Server** und **Zustand einer Sitzung**.

Der Rückfall wandert mit: Was der Server nicht kennt, bekommt auch von der
Gegenstelle einen Fehler statt Schweigen. Regel 3 kennt keine dritte
Möglichkeit, und Schweigen lässt den Frager bis in seine Zeitüberschreitung
warten, ohne je zu erfahren, ob die Frage überhaupt ankam.

Und Regel 4 gilt weiter: Auf ein `result` oder `error` an die Serveradresse
folgt nichts. Ein eigener Test hält das fest — ohne ihn wäre der nächste Schritt
ein Server, der jede Stanza an seine Adresse beantwortet, und zwei davon
schöben sich gegenseitig Meldungen zu.

**Ein Test aus D16 hat diese Änderung vorhergesagt und musste ihr weichen.**
`AnIqToTheServersOwnAddress_IsNotClaimedByTheUserPath` hielt fest, dass die
Anfrage unbeantwortet bleibt, und nannte das ausdrücklich „eine offene Stelle
und keine Absicht". Seine eigentliche Aussage bleibt erhalten: Der
Nutzer-Zustellweg darf die Serveradresse nicht anfassen — er antwortete auf
alles mit `<service-unavailable/>`, auf einen Ping also auch. Ein `result` kann
er gar nicht erzeugen, und genau daran ist die Verwechslung zu erkennen. Der
Test prüft jetzt das `result` statt des Schweigens.

Sechs Mutationen, alle erschlagen — eine davon erst im zweiten Anlauf, und der
Grund ist **zum zweiten Mal** derselbe wie in D25.

Die Mutation nimmt dem hiesigen Client die Selbstauskunft weg. Über meinen
Filter — die vier Fixtures, die mit diesem Punkt zu tun haben — **überlebte
sie**: Dass ein Client den Server anpingt und eine Auskunft bekommt, steht in
anderen Fixtures, und die waren nicht dabei. Über die ganze Sammlung fällt sie
mit sechs Fehlern.

Der Fehler ist nicht, den Filter eng zu wählen — das spart echte Zeit —, sondern
einem **überlebenden** Mutanten zu glauben, ohne den Filter zu prüfen. Ein
erschlagener Mutant ist auch mit engem Filter erschlagen; ein überlebender sagt
erst dann etwas, wenn die Tests, die ihn erschlagen könnten, überhaupt gelaufen
sind. Das gehört zur fünften Bedeutung aus D25 und ist ihre praktische Form:
**Bei jedem Überlebenden zuerst den Filter verdächtigen, nicht den Test.**

---

### D37. Ein Vorschlag, der von sich selbst abrät ⛔ — XEP-0013 entfällt

XEP-0013 („Flexible Offline Message Retrieval") stand als nächster Punkt an. Es
wird **nicht umgesetzt**, und der Grund steht im Dokument selbst: Die XSF führt
es als **Deprecated** — Fassung 1.3, Stand 2021-05-04, mit dem Satz
„Implementation of the protocol described herein is not recommended."

Gebracht hätte es die andere Hälfte der Ablage aus D14. Heute entscheidet der
Server, wann die aufbewahrten Nachrichten kommen: bei der nächsten
nicht-negativen verfügbaren Presence, alle auf einmal, und mit dem Herausgeben
ist die Ablage leer (`TakeOfflineMessages`). XEP-0013 hätte diese Entscheidung
dem Client gegeben — hineinsehen, bevor man abholt, einzelne Nachrichten gezielt
lesen oder wegwerfen, den Rest liegen lassen.

Der Preis wäre nicht die Auflistung gewesen. `OfflineMessage` trägt heute
`Stanza` und `StoredAt`, **keinen Bezeichner** — XEP-0013 spricht jede
aufbewahrte Nachricht über ein `node`-Attribut an, das über einen Neustart
hinweg dasselbe bleiben muss. Das hätte den Datensatz, die Ablage in
`XMPPAccount` und die Persistenz in `FileAccountStore` erfasst. Der teure Teil
liegt aber woanders: Ein Client, der die Ablage selbst verwaltet, darf sie nicht
gleichzeitig zugeschickt bekommen. Die automatische Nachlieferung hätte also
abschaltbar werden müssen, abhängig davon, ob der Client sich vor seiner ersten
Presence gemeldet hat. Das ist ein zweiter Zustand im Anmeldeweg, genau an der
Stelle, an der D14 hängt.

Diesen Umbau für ein Dokument zu machen, das von seiner Umsetzung abrät, wäre
falsch herum: Der Aufwand fiele an, und geblieben wäre ein Protokoll, das kein
neuer Client mehr sprechen wird.

**Einen Nachfolger benennt XEP-0013 nicht.** Es verweist nur auf „the protocol
that supersedes this one (if any)". In der Praxis übernimmt XEP-0313 (Message
Archive Management) das gezielte Nachlesen — aber nur die eine Hälfte, und mit
einem anderen Begriff: Ein Archiv ist keine Ablage. Es enthält auch, was
zugestellt wurde, und es leert sich nicht durchs Lesen. Die zweite Hälfte —
„schick mir beim Anmelden nicht alles zu" — steht dort nicht. Wer sie will,
braucht sie zusätzlich. Sollte das je anstehen, ist es ein eigener Punkt und
nicht dieser.

Was bleibt, ist der Weg aus D14: RFC 6121, Abschnitt 8.5.2.2.1, und XEP-0160.
Beide sind aktuell, beide sind umgesetzt, und beide reichen für einen Client,
der die Nachrichten schlicht haben will.

Ein Fund bleibt auch: **Dass `OfflineMessage` keinen Bezeichner hat, ist keine
Lücke, sondern eine Folge.** Solange niemand eine einzelne aufbewahrte Nachricht
ansprechen kann, gibt es nichts zu benennen. Der Bezeichner fehlt genau so
lange, wie er nicht gebraucht wird — er wäre die erste Zeile, die ein Protokoll
ändern müsste, das einzelne Nachrichten adressiert.

---

### D38. Eine Liste, die nicht wartet 🕓 — XEP-0060 wird optional

„Später" hiess bisher zweierlei: Punkte, denen nur die Gelegenheit fehlt, und
Punkte, die niemandem fehlen. Beides in einer Liste liest sich wie eine
Schuldenliste, und je länger sie wird, desto weniger sagt sie. Mit D37 kam
„Bewusst nicht umgesetzt" dazu; dazwischen fehlte **„Optional"**: nicht
entschieden dagegen, aber auch nicht anstehend.

XEP-0060 gehört dorthin. Die Lücke ist echt — und sie ist grösser, als der alte
Eintrag sagte: `PubSubSubscribeAsync` verschickt die Anfrage und trägt das
Abonnement sofort ein, ohne die Antwort abzuwarten. Ein abgelehntes Abonnement
steht danach als bestehendes in `_subscribedNodes`, und der Aufrufer erfährt es
nie. `OnSubscriptionResult` gibt es bereits, ausgelöst wird es nirgends.

**Trotzdem nicht anstehend, und zwar aus einem Grund, der zum Rest der
Arbeitsweise passt.** Dieser Client benutzt PubSub an keiner Stelle selbst;
die betroffenen Member stehen bereits als ungenutzte API-Fläche im README. Eine
Korrelation, die kein Aufrufer abholt, liesse sich nur gegen einen ausgedachten
Ablauf prüfen — und ein Test, der seinen eigenen Anwendungsfall erfindet, prüft
die Erfindung. Das ist derselbe Grund, aus dem die XEP-0160-Regel aus D14 unter
„Später" steht statt als erledigt.

Eine optionale Liste ist der Ort, an dem Dinge in Ruhe vergessen werden. Deshalb
steht der Rückweg dabei: **Sobald PubSub einen Anwendungsfall hat** — ein
Abonnement gegen eine echte PubSub-Komponente, an dem sich Zusage und Ablehnung
unterscheiden lassen —, wandert der Punkt zurück nach „Später". Nicht die Zeit
holt ihn zurück, sondern der Bedarf.

---

### D39. Wir haben verlangt, was wir selbst nicht gaben ✅ — Abschnitt 3.2

XEP-0030, Abschnitt 3.2: „If the request included a 'node' attribute, the
response MUST mirror the specified 'node' attribute to ensure coherence between
the request and the response." XEP-0115, Abschnitt 6.2 sagt dasselbe für den
Caps-Fall und nennt den Wert: `node#ver`.

**Die Lücke war eine Asymmetrie, keine Unkenntnis.** `EntityCapsManager` fragt
seit jeher mit `node#ver` und legt die Antwort unter genau diesem Schlüssel ab.
`DiscoManager.RespondInfoAsync` konnte das `node` sogar setzen — der einzige
Aufrufer übergab keines und las das Attribut der Anfrage nicht einmal. Wir haben
also von jeder Gegenstelle verlangt, was wir selbst nie geliefert haben.

Kaputt sah dabei nichts aus, und das ist das Tückische: Eine strenge
Gegenstelle legt eine Antwort ohne `node` nicht unter `node#ver` ab, fragt bei
jeder Presence erneut und bekommt jedes Mal dieselbe Auskunft. Der Nutzen von
XEP-0115 fällt weg, ohne dass irgendwo ein Fehler erscheint.

**Die zweite Hälfte war die grössere.** Ein Node, den es hier nicht gibt, bekam
dieselbe volle Merkmalsliste wie eine Anfrage ohne Node. Diese Seite behauptete
damit, **jeden erdachten Node zu führen** — `commands`, `offline`, was auch
immer jemand fragt, es gab ihn. Jetzt wird nur beantwortet, was diese Entity
bezeichnet: der Caps-Node, mit und ohne aktuelles `#ver`. Alles andere bekommt
`<item-not-found/>`.

**Ein veraltetes `ver` gehört ausdrücklich zu „alles andere", und das ist die
unbequeme Entscheidung.** Verbreitete Server schicken auch dort die aktuelle
Liste. Das ist bequemer und falsch: Der Frager rechnet nach XEP-0115,
Abschnitt 5.4 den angekündigten Hash gegen die Antwort. Zu einem alten `ver`
ergibt die neue Liste einen anderen Hash — er hat dann die Wahl, uns für einen
Fälscher zu halten oder das Nachrechnen aufzugeben. Unser eigener
`EntityCapsManager` würde die Antwort ablehnen. Ein Fehler ist die ehrlichere
Auskunft: **Diesen Stand gibt es hier nicht mehr.**

**Der Testserver hat gar keine Nodes**, er kündigt keine Capabilities an. Jede
Frage nach einem Node bekommt dort einen Fehler. Dabei fiel ein Satz auf, der
eine Unterscheidung behauptete, die es nicht gab: Der Schalter `FailDiscoInfo`
antwortete mit „Diesen Node gibt es hier nicht." — auf eine Abfrage, die keinen
Node nennt, in einem Server, der das Attribut nie ansah. Der Satz steht jetzt
dort, wo er zutrifft; der Schalter sagt, was er tut.

**Auch ein Fehler ist eine Antwort und muss sagen, worauf.** Beide Fehler nehmen
die Anfrage samt `node` mit zurück (RFC 6120, Abschnitt 8.3.1); `StanzaErrorIq`
hat dafür einen Parameter bekommen. Ohne das erfährt ein Frager, der mehrere
Nodes derselben Entity abfragt, nur, dass *irgendeiner* fehlt — und die
Spiegelung aus Abschnitt 3.2 gilt für die Fehlerantwort genauso.

Acht neue Tests, elf Mutationen, zehn erschlagen. **Der Überlebende ist ein
Zustand, den es nicht gibt:** `EntityCaps?.IsOwnNode(node) != true` gegen
`== false` unterscheidet sich allein im Fall „kein EntityCaps", und der tritt
nicht ein — `Disco` und `EntityCaps` entstehen in zwei aufeinanderfolgenden
Zeilen, die Bedingung prüft `Disco is not null`. Die strengere Fassung bleibt
stehen: Ohne Caps-Manager gibt es keine eigene Node-Kennung, und was man nicht
kennt, kann man nicht bestätigen.

Eine Mutation hat einen Test bekommen, statt als Überlebender vermerkt zu
werden. Der Server liest seine Frames als Zeichenketten — bewusst, damit er den
Client nicht mit derselben Brille ansieht, mit der der Client sich selbst
ansieht. Damit sind „steht `node=` irgendwo im Frame" und „die Anfrage trägt ein
`node`" zwei verschiedene Dinge, und der Unterschied wäre unbelegt geblieben.
`ANodeOutsideTheQuery_DoesNotCount` legt der Anfrage ein fremdes Element mit
`node` bei; ohne den Anker im Muster bekäme diese gewöhnliche Abfrage einen
Fehler.

**Und ein Werkzeug hat die Arbeit zurückgedreht.** `mutate.ps1` setzte nach
jedem Lauf aus einem Sicherungsordner zurück, den es nie selbst gefüllt hat —
darin lag, was irgendeine frühere Sitzung dort abgelegt hatte. Zwei Dateien
sprangen so um eine ganze Sitzung zurück; in `XMPPConnection.cs` war `CreateTcp`
wieder da, in D34 gelöscht. Die Hash-Prüfung meldete dabei brav „wie zuvor",
denn sie verglich gegen genau diese alte Sicherung.

Das ist derselbe Fehler wie in D34, nur eine Ebene tiefer: **eine Messung, die
nicht misst, was sie behauptet.** Nur war sie diesmal nicht bloss blind, sondern
zerstörend — die Prüfung, die den Schaden hätte melden sollen, war Teil davon.
Die Sicherung wird jetzt im Augenblick der Mutation aus der Datei gezogen, die
gleich mutiert wird. Eine Sicherung, die älter ist als die Arbeit, ist keine.

**Nebenbefund, notiert unter „Später":** `LocalFeatures` kündigt
`disco#items` an, beantwortet wird es nirgends — eine eingehende items-Abfrage
fällt bis zum `<service-unavailable/>` durch. Angekündigt und dann verweigert
ist die einzige Kombination, die es nicht geben darf.

---

### D40. Angekündigt und dann verweigert ✅ — Abschnitt 4

Der Nebenbefund aus D39, und er ist kein fehlendes Merkmal, sondern ein
falsches Versprechen: `LocalFeatures` führt
`http://jabber.org/protocol/disco#items` seit jeher, beantwortet wurde eine
items-Abfrage nie. Sie fiel durch bis zum `<service-unavailable/>`. Eine
Gegenstelle, die der Merkmalsliste glaubt, bekam also einen Fehler auf eine
Frage, zu der wir sie eingeladen hatten.

**Die Antwort ist eine leere Liste, und das ist keine Notlösung.** „Ich habe
keine" und „frag mich nicht" sind verschiedene Auskünfte, und nur die erste
stimmt: Ein Client hat keine Untereinheiten. Wer stattdessen
`<service-unavailable/>` schickt, sagt das Zweite — und wer die Frage gar nicht
erst zulässt, hätte das Merkmal nicht ankündigen dürfen.

`DiscoManager.LocalItems` ist leer als Vorgabe und wird tatsächlich gelesen; ein
Test füllt sie, sonst wäre „immer eine leere Liste" eine bestandene Lösung und
die Liste eine Zierde.

**Ein `node` ist hier etwas anderes als in D39.** Bei disco#info bezeichnet er
die Entity selbst (der Caps-Node aus XEP-0115); bei disco#items ist er ein Ast
im Baum der Untereinheiten. Dieser Client hat keinen einzigen, also
`<item-not-found/>` — dieselbe Entscheidung wie in D39, aus demselben Grund. Die
leere Liste wäre hier die falsche Antwort: Sie hiesse **„diesen Zweig gibt es,
er ist leer"** statt „diesen Zweig gibt es nicht".

Deshalb hat `RespondItemsAsync` **keinen** `node`-Parameter, obwohl sein
Gegenstück `RespondInfoAsync` einen hat. Er bekäme nie einen Wert: Wo ein Node
in der Frage steht, wird gar nicht geantwortet. Ein Parameter, der nie einen
Wert bekommt, sieht aus wie eine Fähigkeit und ist keine — und wäre prompt der
erste Überlebende gewesen, weil ihn kein Test je erreicht.

`RefuseUnknownNode` hat dafür den Namensraum als Parameter bekommen: Der Fehler
nimmt die Anfrage zurück, die gestellt wurde. **Ein Fehler, der die falsche
Frage nennt, ist schlechter als einer ohne Frage** — der Frager ordnet ihn dann
der falschen Abfrage zu. Eine eigene Mutation prüft genau das.

Vier Tests, sieben Mutationen, alle erschlagen.

Und eine Zeile im README stimmte nicht mehr: `EntityCapsManager.GetCachedInfo`
stand unter „ungenutzt und ungetestet", während zwei Fixtures darüber prüfen,
was im Caps-Cache landet. Solche Listen veralten in die unangenehme Richtung —
sie behaupten ungeprüft, was inzwischen geprüft ist.

---

### D41. Wohin, sagt die Domain ✅ — XEP-0156

Der Endpunkt war fest verdrahtet: `wss://{domain}:5443/ws`, die ejabberd-Vorgabe.
Für Prosody, für jeden anderen Server und für jeden Betreiber mit eigenem Pfad
musste der Aufrufer ihn kennen und mitgeben. XEP-0156 ist der Weg, auf dem die
Domain selbst sagt, wo ihr WebSocket steht: `host-meta` unter
`/.well-known/`, einmal als JSON (JRD), einmal als XML (XRD).

**Zwei Sätze des XEPs bestimmen den ganzen Zuschnitt.**

Der erste ist eine Rangfolge: „HTTPS queries for host-meta information MUST be
used only as a fallback after the methods specified in RFC 6120 have been
exhausted." Gefragt wird deshalb **nur, wenn der Aufrufer keinen Endpunkt
genannt hat** — und ein eigener Test hält fest, dass die Discovery dann gar
nicht erst anläuft. Ohne ihn wäre „immer erst nachschauen" eine bestandene
Lösung: teuer für jeden, der seinen Server kennt, und eine offene Tür für ein
fremdes `host-meta`, das ihn woandershin schickt.

Der zweite ist eine Sicherheitsregel, und sie hat zwei Hälften: „host-meta files
MUST be fetched only over HTTPS, and MUST only use connection URLs starting with
'https://' or 'wss://'." Beide gehören zusammen. Wer die Auskunft im Klartext
holt, lässt jeden Zwischenmann bestimmen, wohin sich der Client anmeldet; wer
einer sicher geholten Auskunft ein `ws://` abnimmt, schickt Benutzer und
Passwort hinterher trotzdem offen durchs Netz. **Eine halbe Absicherung ist hier
keine.** Beide Hälften haben ihre eigene Mutation.

Vom erlaubten Paar bleibt für diesen Client nur `wss://` übrig: `https://` ist
BOSH (XEP-0124), das er nicht spricht. Ein BOSH-Link wird gelesen und übergangen
— nicht, weil er falsch wäre, sondern weil eine Adresse, die als
WebSocket-Endpunkt zurückkäme, den Verbindungsaufbau an etwas scheitern liesse,
das nie dafür gedacht war.

**Der Link-Typ entscheidet, nicht das Schema.** Ein `host-meta` ist nicht für
XMPP gemacht; dort stehen `lrdd`, `webfinger` und was der Betreiber sonst
veröffentlicht. Wer nur auf `wss://` prüft, nimmt den erstbesten Eintrag, der
zufällig verschlüsselt ist — ein eigener Test legt genau so einen aus.

**Was nicht umgesetzt ist, fehlt nicht:** Der DNS-Weg über
`_xmppconnect`-TXT-Einträge steht in keiner aktuellen Fassung mehr — „this was
insecure and has been removed". Ihn nachzubauen hiesse, eine zurückgezogene
Empfehlung umzusetzen.

Die Suche läuft **höchstens einmal**, auch über Wiederverbindungen hinweg. Der
Wiederverbindungsversuch ist eine Schleife; eine Abfrage je Durchgang hiesse,
bei einem Server, der gerade weg ist, jedes Mal erneut auf eine HTTPS-Antwort zu
warten, die es nicht gibt. Auch das steht in einem Test — als Zählung der
Abfragen, nicht als Vermutung.

Zwölf Tests, neun Mutationen, alle erschlagen. **Ungeprüft bleibt der eingebaute
Abrufer selbst:** Er holt über das Netz, und die Sammlung setzt an seine Stelle
eine Funktion ohne Netz — anders wäre keiner dieser Tests wiederholbar. Was
geprüft ist, sind die Adressen, die gebaut werden (beide `https://`, beide
`/.well-known/`), und was mit dem Ergebnis geschieht. Die `https`-Sperre im
Abrufer selbst ist damit eine zweite Linie hinter einer geprüften ersten und
kein ungeprüftes Verhalten.

**Ein Nebenbefund, notiert unter „Später":** Scheitert der Verbindungsaufbau,
lautet die Ausnahme „Unable to connect to the remote server" — ohne die Adresse.
Bisher war das verschmerzbar, denn der Aufrufer hatte sie selbst mitgegeben.
Seit dieser Änderung kann sie aus dem `host-meta` einer fremden Domain stammen,
und dann beantwortet der eigene Quelltext die Frage „wohin eigentlich?" nicht
mehr. Der zugehörige Test prüft deshalb den Endpunkt und nicht den Fehlertext —
und sagt in seinem Kommentar, warum.

---

### D42. Eine Leiter ist keine Menge ✅ — RFC 8264, Abschnitt 8

Seit D5 stand hier eine Näherung: Ein Codepoint gehörte zur IdentifierClass,
wenn seine Unicode-Kategorie stimmte und er keine Kompatibilitätszerlegung
hatte. Das traf die Beispiele aus RFC 7622 — und genau deshalb fiel es nicht
auf.

**Die Vorschrift ist keine Prüfliste, sondern eine Reihenfolge.** RFC 8264,
Abschnitt 8 ist eine Leiter von fünfzehn Sprossen, und viele Codepoints stehen
auf mehreren davon. Welche zuerst greift, entscheidet über die Antwort:

- **U+0640 (ARABIC TATWEEL)** ist ein Modifier Letter und damit in
  LetterDigits — die Ausnahmeliste steht davor und verbietet ihn. Er ist ein
  Streckungsstrich: beliebig oft einfügbar, ohne etwas zu bedeuten. Aus einem
  Konto werden damit beliebig viele, die gleich aussehen. Die Näherung liess ihn
  durch.
- **U+3164 (HANGUL FILLER)** ist ein Buchstabe (Lo) — `Default_Ignorable`
  steht davor. Ein unsichtbarer Buchstabe in einer Adresse.
- **U+2163 (ROMAN NUMERAL FOUR)** ist Nl und damit in OtherLetterDigits —
  HasCompat steht davor.
- **Die alten Hangul-Jamo** sind Buchstaben und kamen durch; sie setzen sich zu
  Silben zusammen, die es fertig als eigene Codepoints gibt. Zwei Schreibweisen
  für dasselbe Wort, und keine Normalisierung räumt das auf.

Der Test dazu prüft deshalb nicht nur das Ergebnis, sondern nennt zu jedem Fall
**den Zweig, der ihn beantwortet.** Ein Test, der nur die Antwort prüft, hielte
eine Leiter mit vertauschten Sprossen für richtig, solange sich die Fälle nicht
überschneiden — und hier überschneiden sie sich fast alle.

**Was .NET nicht kennt, steht jetzt als Tabelle da.**
`Default_Ignorable_Code_Point`, `Noncharacter_Code_Point` und
`Hangul_Syllable_Type` liefert die Laufzeit nicht. Sie sind als Bereiche
eingetragen, mit der Unicode-Fassung benannt, aus der sie stammen. Das ist keine
Näherung mehr, sondern eine Kopie: Sie kann veralten, aber sie kann nicht
danebenliegen — und wo sie veraltet, steht es dran.

**Zwei Regeln sind umgesetzt, sieben nicht, und das ist eine Entscheidung.**
Kontextabhängige Codepoints (CONTEXTJ/CONTEXTO) hängen nicht am Codepoint,
sondern an der ganzen Zeichenkette. A.8 und A.9 — die beiden Reihen
arabisch-indischer Ziffern dürfen nicht gemischt werden — kommen ohne
Unicode-Eigenschaften aus und sind umgesetzt; sie betreffen Ziffern, die in
Adressen wirklich vorkommen. Die übrigen brauchen `Joining_Type` oder `Script`,
und **die aus Blockgrenzen zu erraten hiesse, die Näherung an genau der Stelle
wieder einzuführen, an der sie über Zulassen oder Ablehnen entscheidet.** Also
abgewiesen — es trifft fünf Satzzeichen und zwei unsichtbare Zeichen, keine
Buchstaben.

Die Trennung der beiden Klassen bekommt eine eigene Gegenprobe: Was ein
Resourcepart tragen darf (Symbole, Leerzeichen), darf ein Localpart nicht. Ohne
sie wäre „beide nehmen die FreeformClass" eine bestandene Lösung, und der
Unterschied verschwände unbemerkt.

Neun Tests, dreizehn Mutationen, alle erschlagen. Beide Beispieltabellen aus
RFC 7622 stehen weiter und laufen unverändert durch — die Näherung traf sie, die
Vorschrift trifft sie auch.

**Die zweite Hälfte des Punktes bleibt offen und steht jetzt genauer da:**
IDNA2008 für Domain-Labels. Die Codepoint-Ebene ist damit erledigt, es fehlt die
Label-Ebene — Punycode, Bidi-Regel, Label-Längen.

---

### D43. Ein Domainname ist keine Zeichenkette ✅ — IDNA2008

Die zweite Hälfte von D42. Der Domainpart wurde bis hierher nur grob geprüft:
keine Steuerzeichen, kein Leerzeichen. Alles andere ging durch — ein
Unterstrich, ein Symbol, ein Label mit 200 Zeichen, ein `xn--`, hinter dem
nichts steht.

**Dieselben Bausteine, eine andere Leiter.** RFC 5892, Abschnitt 1 sieht aus wie
die aus RFC 8264 und beantwortet dieselbe Frage anders. Wo PRECIS **ASCII7**
sagt, sagt IDNA **LDH**: Bindestrich, Ziffern, Kleinbuchstaben — und sonst
nichts aus ASCII. Wo PRECIS am Ende Symbole und Satzzeichen auffängt (FREE_PVAL),
endet IDNA mit DISALLOWED. Dazu zwei Zweige, die es nur hier gibt: **Unstable**
(was sich unter Normalisierung und Kleinschreibung verändert) und
**IgnorableBlocks**.

Deshalb stehen die beiden Leitern getrennt, auf einem gemeinsamen Unterbau
(`UnicodeSets`). Ein Verfahren mit Schaltern wäre kürzer und stellte beim Lesen
bei jeder Zeile die Frage „gilt das jetzt für Labels oder für Localparts?".

**Punycode ist selbst gerechnet** (RFC 3492), obwohl .NET mit `IdnMapping`
etwas Ähnliches mitbringt. Der Grund ist nicht Stolz: `IdnMapping` bringt seine
eigene Auslegung mit (UTS 46 über ICU) und **bildet ab, wo IDNA2008 ablehnt** —
Grossbuchstaben etwa. Wer prüfen will, ob ein Label gültig ist, darf die Prüfung
nicht an etwas abgeben, das vorher zurechtbiegt. Geprüft wird gegen die elf
Beispiele aus Abschnitt 7.1, in beide Richtungen.

**Ein A-Label wird nicht geglaubt, sondern nachgerechnet.** Dekodieren, die
Label-Regeln auf das U-Label anwenden, zurückrechnen — und wenn dabei etwas
anderes herauskommt als das, was dastand, ist es abgewiesen. Zwei Fälle machen
das anschaulich: `xn--TDA` bedeutet dasselbe wie `xn--tda` (Punycode-Ziffern
sind schreibweisenlos) und ist trotzdem keine gültige Schreibweise; `xn--abc-`
verpackt reines ASCII, und dann stünde dasselbe Label zweimal da — einmal als es
selbst, einmal in Verpackung. **Beides sind zwei Adressen für dieselbe Sache,
und genau das soll IDNA verhindern.**

**Adressliterale gehen daran vorbei, und zwar nach Vorschrift:** RFC 7622,
Abschnitt 3.2 lässt neben dem Domainnamen eine IPv4-Adresse und ein
eingeklammertes IPv6-Literal zu. `[::1]` ist kein Domainname; Doppelpunkte sind
keine Label-Zeichen, und ohne diese Ausnahme wäre die Adresse ungültig.

Neunzehn Mutationen, alle erschlagen — **zwei davon erst, nachdem die Tests
schärfer wurden**, und beide Male aus demselben Grund wie in D5 und D36: Der
Testfall traf schon eine frühere Regel.

| Überlebende Mutation | Warum sie zuerst überlebte | Der Fall, der sie erschlägt |
|---|---|---|
| Die ignorierbaren Zeichen zählen nicht | U+3164 fällt schon über **Unstable**, U+00AD über den Auffangzweig | U+FE00 und U+180B: Variantenselektoren, Kategorie Mn — sie wären ohne diesen Zweig **Buchstaben** |
| Die IDNA-Prüfung im JID wird nicht mehr gefragt | Alle Label-Tests fragen `Idna` unmittelbar | Ein JID mit `exa_mple.com`, `-example.com`, `a..example.com` |

Die zweite ist die unangenehmere: **Die Prüfung war geprüft, ihre Verdrahtung
nicht.** Eine Mutation, die das Ergebnis wegwirft und weitermacht, kam durch die
ganze Sammlung. Dieselbe Sorte Lücke wie die Wache aus D19 — was die Frage
stellt, muss selbst jemand prüfen.

**Was offen bleibt, ist die Bidi-Regel** (RFC 5893): Sie verlangt `Bidi_Class`
für jeden Codepoint eines Labels, und .NET liefert die Eigenschaft nicht. Aus
Blockgrenzen geraten wäre sie dieselbe Näherung, die D42 abgeschafft hat — hier
sogar folgenreicher, denn die Regel entscheidet über ganze Labels statt über
einzelne Zeichen.

---

### D44. Eine Tabelle statt einer Vermutung ✅ — RFC 5893

Der offene Punkt aus D43. Die Begründung dort war richtig und die Folgerung
falsch: `Bidi_Class` **lässt** sich nicht ableiten — aber sie lässt sich
**holen**. Unicode veröffentlicht sie als `DerivedBidiClass.txt`, und für
StringPrep gibt es in diesem Projekt seit Langem denselben Weg:
`tools/stringprep/generate.py` erzeugt `StringPrepTables.cs` aus dem RFC-Text.

Also `tools/unicode/generate-bidiclass.py`, nach demselben Muster. Er lädt die
Datei, liest die Bereiche und schreibt `Jabber/Common/BidiClasses.cs` — zehn
Tabellen, 764 Bereiche. **Die elfte Klasse, L, ist nicht aufgeschrieben:** Sie
ist die grösste und zugleich die Vorgabe der Unicode-Datei selbst. Was in keiner
anderen Tabelle steht, ist L.

Der Unterschied zur Näherung, um die es in D42 und D43 ging, ist genau dieser:
**Eine erzeugte Tabelle kann veralten, eine geratene kann falsch sein.** Die
Unicode-Fassung steht im Kopf der Datei, der Generator daneben; wer zweifelt,
lässt ihn laufen und vergleicht.

**Die Regel ist ansteckend, und das ist ihr eigentlicher Inhalt.** Sobald ein
einziges Label rechtsläufige Zeichen trägt, ist der ganze Name ein „Bidi domain
name" — und dann müssen *alle* Labels die sechs Bedingungen erfüllen, auch die
aus reinem ASCII. `9abc.example` ist ein gültiger Domainname, `9abc.אבג` ist
keiner. Wer das überliest, baut eine von zwei Sorten Fehler: Er wendet die Regel
nie an, oder er wendet sie immer an und weist reihenweise Namen ab, die es seit
dreissig Jahren gibt. Beide Sorten haben hier einen Test.

**Ein A-Label wird für die Regel ausgepackt.** `9abc.xn--4dbcagdahymbxekheh6e0a7fei0b`
sieht in seiner ASCII-Verpackung aus wie zwei linksläufige Labels; darin steckt
Hebräisch. Wer die Bidi-Regel über die Verpackung laufen lässt, findet nie
etwas.

Zehn Mutationen, alle erschlagen — **eine erst nach einer Verschärfung, und zum
vierten Mal aus demselben Grund** (D3, D5, D36, D43): Der Testfall traf schon
eine frühere Bedingung. `אבגa` prüft nicht, was es zu prüfen scheint: Es
scheitert an Bedingung 3 (ein rechtsläufiges Label endet auf R, AL, EN oder AN)
und nicht an Bedingung 2 (in einem rechtsläufigen Label ist L unzulässig).
Erst `אaב` — das fremde Zeichen in der **Mitte** — trifft Bedingung 2 allein.
Dasselbe für Bedingung 5 gegen 6.

Und ein Fehler in der Arbeitsweise, der diesmal glimpflich ausging: Ich habe
**Testdateien geändert, während der Mutationslauf lief.** Die späteren
Mutationen liefen damit gegen andere Tests als die früheren. Weil die Änderung
nur Fälle hinzufügte, blieben die Urteile gültig - „erschlagen" bleibt
erschlagen. Richtig ist es trotzdem nicht: Es gilt dieselbe Regel wie für den
Quelltext, und aus demselben Grund wie in D43.

---

### D45. Der Codepoint allein sagt es nicht ✅ — RFC 5892, Anhang A

Der letzte offene Punkt aus D42. Sieben der neun kontextabhängigen Regeln
fehlten, weil sie `Canonical_Combining_Class`, `Joining_Type` und `Script`
verlangen — und die Antwort ist dieselbe wie in D44: **holen statt raten.**
`tools/unicode/generate-contexttables.py` schreibt `ContextTables.cs` aus drei
Unicode-Dateien; die Lesearbeit, die sich beide Generatoren teilen, steht jetzt
in `tools/unicode/ucd.py`. Aufgeschrieben ist nur, was die sieben Regeln
brauchen: die Virama-Zeichen, vier Joining_Type-Werte, fünf Schriften.

**„Kontextabhängig" heisst: Der Codepoint allein sagt es nicht** — und dieser
Satz stand in der alten Bauform gar nicht zur Verfügung. Sie hiess
`ContextRuleSatisfied(CodePoint, Text)` und konnte deshalb nur Regeln
beantworten, die den ganzen Text betrachten (A.8/A.9). Drei der neuen Regeln
fragen nach dem Zeichen **davor**, zwei nach dem **danach**; bei zwei gleichen
Zeichen in derselben Zeichenkette wäre schon nicht mehr klar, welches gemeint
ist. Die Stelle gehört also in die Frage: `ContextRuleSatisfied(CodePoints,
Index)`. Der Aufrufer arbeitet dafür auf einem Feld statt auf einer Folge.

Der Unterschied wird an einem Wort sichtbar, das es wirklich gibt: **`col·la`
ist katalanisch und ein gültiger Localpart, `co·lla` ist keiner.** Dieselben
Zeichen, andere Reihenfolge, andere Antwort — mehr ist über
„kontextabhängig" nicht zu sagen.

A.7 fällt aus der Reihe: Der Katakana-Mittelpunkt fragt nicht nach Nachbarn,
sondern danach, ob **irgendwo** in der Zeichenkette japanische Schrift steht. Er
trennt in japanischem Text die Teile eines Fremdworts; ohne japanische Zeichen
trennt er nichts.

Vierzehn Mutationen, alle erschlagen — **drei erst nach einer Verschärfung, und
zum fünften Mal aus demselben Grund.** Diesmal in seiner reinsten Form: Regel
A.1 hat zwei Seiten (links ein verbindender Buchstabe, rechts einer), und mein
Testfall `a‌b` verletzte **beide**. Er konnte deshalb nicht zeigen, dass jede
für sich geprüft wird. Erst `a‌ي` (links falsch, rechts richtig) und
`ب‌b` (umgekehrt) trennen die beiden Hälften — und ein drittes Paar mit
einem durchsichtigen Zeichen dazwischen zeigt, dass die Regel darüber
hinwegsieht.

Nebenbei ein Vermerk, der nicht mehr stimmte: Die Beschreibung von
`Idna.IsValidDomain` sagte weiterhin, die Bidi-Regel fehle — seit D44 tut sie
das nicht mehr. **Ein Kommentar, der eine Lücke benennt, ist so lange nützlich,
wie die Lücke besteht, und danach eine Falschaussage an prominenter Stelle.**

Damit ist RFC 7622 vollständig umgesetzt: Codepoint-Ebene (D42), Label-Ebene
und Punycode (D43), Bidi-Regel (D44), kontextabhängige Regeln (D45).

---

### D46. Ein Tippstatus verspricht nichts ✅ — XEP-0160, Abschnitt 3

Der letzte Punkt unter „Später → Protokoll", und der Grund für die Verschiebung
war von Anfang an der falsche. Er lautete: „dieser Client schickt keine solche
Nachricht, die Regel wäre ungetestet". Das stimmt für den Client — **nur gehört
die Regel dem Server.** Ein Test braucht keinen Client, der einen Tippstatus an
einen Abwesenden schickt; er braucht eine Zeichenkette auf der Leitung, und die
schreibt `SendRawAsync` seit jeher.

XEP-0160, Abschnitt 3 nennt die Ausnahme beim `chat`: „with the exception of
messages that contain only Chat State Notifications (XEP-0085) content (such
messages SHOULD NOT be stored offline)". Ein Tippstatus ist eine Aussage über
*jetzt*. Beim Anmelden nachgereicht sagt er, jemand tippe gerade — und das
stimmt dann garantiert nicht mehr. Zehn davon verdrängen ausserdem die
Nachrichten, für die die Ablage da ist.

**Und der Absender bekommt keinen Fehler**, obwohl D14 das stillschweigende
Verwerfen ausdrücklich ausgeschlossen hat. Das ist kein Rückfall, sondern die
Grenze jener Regel: Sie schützt eine Erwartung. Wer eine Nachricht schickt, will
wissen, ob sie ankam; wer einen Tippstatus schickt, hat nichts verloren, wenn er
verfällt. Ein `<service-unavailable/>` dafür wäre Lärm — und einer, der bei
jedem Tastendruck neu käme.

**Hier liest der Server als einziger Stelle einen Baum**, und der Grund steht in
der Regel selbst: Die Frage lautet „sind *alle* Kinder Tippstatus-Elemente".
Ein `Contains` beantwortet „kommt vor", nicht „kommt nur vor" — und genau dieser
Unterschied ist die Vorschrift. Die Zeichenkettenbrille aus D26 bleibt dort, wo
sie hingehört: bei der Weiche, die entscheidet, *was* eine Stanza ist.

Drei Entscheidungen dabei, jede mit einem Test:

- Ein `<thread/>` zählt nicht als Inhalt — XEP-0085, Abschnitt 5.3 führt genau
  diese Form vor.
- Eine Nachricht ohne Text ist deshalb noch lange kein Tippstatus: Eine
  Empfangsbestätigung (XEP-0184) und ein Lesevermerk (XEP-0333) haben keinen
  Text und sollen ankommen. Die naheliegende Abkürzung „ohne `<body/>` nicht
  ablegen" wäre falsch.
- `normal` mit demselben Inhalt wird abgelegt. Das ist der Buchstabe des
  Abschnitts: Dort steht „SHOULD be stored offline" ohne Einschränkung. Die
  Regel weiter zu ziehen als geschrieben hiesse, eine eigene Vorschrift zu
  erfinden und sie fremd zu nennen.

Sieben Mutationen, alle erschlagen — eine erst nach einer Verschärfung, und der
Fall ist hübsch: Die Mutation prüfte statt des Namensraums den Namen
(`composing`). **Alle meine Fälle benutzten ausgerechnet `<composing/>`** — die
Mutation war damit unsichtbar, obwohl XEP-0085 fünf Zustände kennt. Ein
`<active/>` genügt, um sie zu erschlagen.

Zum zweiten Mal in zwei Punkten stand ausserdem eine Aussage im README, die
ihre Wahrheit überlebt hatte: „Eine Anfrage von einer Gegenstelle an die
Serveradresse bleibt unbeantwortet" — beantwortet seit D36. **Ein Vermerk über
eine Lücke braucht dasselbe Nachziehen wie der Quelltext**; sonst wird aus der
ehrlichsten Zeile die falscheste.

---

### D47. Wohin eigentlich? ✅ — der Endpunkt im Fehlertext

Scheiterte der Verbindungsaufbau, lautete die Ausnahme „Unable to connect to the
remote server" — ohne die Adresse. Solange der Aufrufer sie selbst mitgab, war
das verschmerzbar: Er konnte in seinem eigenen Quelltext nachsehen. **Seit
XEP-0156 (D41) kann sie aus dem `host-meta` einer fremden Domain stammen**, und
dann steht sie nirgends, wo er nachsehen könnte.

Also wird genau dieser eine Aufruf eingefasst: Was `ClientWebSocket.ConnectAsync`
wirft, kommt als `XMPPProtocolException` heraus, die den Endpunkt nennt und den
ursprünglichen Fehler als `InnerException` mitführt.

**Das ist kein Rückzieher gegenüber D31.** Dort ging es um den *Stapel* des
ursprünglichen Fehlers — „für den Aufrufer ist die Stelle interessant, an der es
schiefging". Genau das trifft hier nicht zu: Der Stapel endet in
`ClientWebSocket.ConnectAsync` und sagt nichts, was man nicht schon weiss. Was
fehlt, ist die Adresse. Alles danach — Aushandlung, SASL, Binding — bleibt
unverändert und wirft weiter seine eigenen Ausnahmen; ein
`AuthenticationException` ist nach wie vor eines, und der Wiederverbindungs­weg
entscheidet weiter an ihm.

Zwei Grenzen dazu, beide mit einem Test:

- **Ein Abbruch bleibt ein Abbruch.** Wer sein Token zieht, bekommt seine
  `OperationCanceledException` und nicht die Meldung über den Endpunkt - sonst
  liesse sich der eigene Abbruch nicht mehr von einem Fehlschlag unterscheiden.
- **Genannt wird der benutzte Endpunkt, nicht der Vorgabewert.** Der Test lässt
  die Discovery `wss://127.0.0.1:1/ws` finden; genau diese Adresse muss in der
  Meldung stehen. Ohne ihn wäre „nenne den eingebauten Vorgabewert" eine
  bestandene Lösung — und die verschwiege gerade den Fall, für den die ganze
  Änderung da ist.

Vier Mutationen, alle erschlagen, ohne Nachschärfen.

---

### D48. Der Transport, den niemand vermisst 🕓 — TCP wird optional

Der TCP-Transport für den Client wandert von „Später" nach „Optional". Der
Umfang ist seit D34 gemessen und hat sich nicht geändert; **was sich geändert
hat, ist die Einsicht, dass niemand darauf wartet.** Dieser Client spricht XMPP
über WebSocket, und alle drei Server, gegen die er läuft — Prosody, ejabberd,
der eigene Testserver — bieten das an.

Damit gilt für ihn, was in D38 die Liste begründet hat: nicht falsch, nicht
dringend, und ohne Anwendungsfall auch nicht prüfbar. Ein Transport, den kein
Aufrufer benutzt, liesse sich nur gegen einen ausgedachten Ablauf messen — und
das ist genau die Sorte Test, die ihre eigene Erfindung prüft.

**Der Rückweg steht dabei, wie bei jedem Punkt dieser Liste:** ein Server, den
dieser Client erreichen soll und der keinen WebSocket-Endpunkt anbietet. Dann
gibt es den Anwendungsfall und mit ihm die Gegenprobe — Prosody hört in dieser
Umgebung auf 127.0.0.1:5222.

Damit ist „Später → Transport" leer. Was dort bleibt, sind zwei Punkte der
Testsammlung, drei am Server und die Struktur.

---

### D49. Die Zahl, die niemand gemessen hat ✅ — das `h` im `<failed/>`

Der Punkt hiess „XEP-0198 `<resume/>` beantworten" und stand seit dem 26. Juli
unter „Später → Server". **R1 hat ihn am 28. Juli erledigt**, R2 und R3 haben
die Wiederaufnahme danach gegen den eigenen Server und gegen Prosody geprüft —
die Liste hat es nur nie erfahren. Ein erledigter Punkt, der stehenbleibt, ist
nicht bloss Papier: Er verdeckt, was von ihm wirklich noch offen war.

Offen war die **Abweisung**. Der Server antwortete auf jedes gescheiterte
`<resume/>` mit

```xml
<failed xmlns='urn:xmpp:sm:3' h='0'><item-not-found .../></failed>
```

und das `h` darin war keine Auskunft, sondern eine Behauptung: *„Von allem, was
du geschickt hast, ist nichts angekommen."* Nach XEP-0198, Abschnitt 5, ist das
Attribut freiwillig („MAY also include") und meint eine Messung — wie weit der
Server auf dem alten Stream gekommen war. Gemessen hat hier nichts.

**Folgenlos war es nur, weil auch niemand zuhörte.** `ProcessFailed()` nahm den
Rahmen gar nicht erst entgegen und erklärte jede unbestätigte Stanza für
verloren. Beide Fehler zusammen ergaben ein stimmiges Bild — die falsche Zahl
wurde von niemandem gelesen, und der Client kam ohne sie aus, weil er sowieso
alles für verloren hielt. Genau so überleben Fehler paarweise.

Was jetzt gilt, sind drei Fälle statt einem:

- **Unbekannte Kennung** — kein `h`. Der Normalfall nach einem Neustart oder
  nachdem der Abräumer da war: Der Server weiss nichts und sagt nichts.
- **Fremdes Konto** — kein `h`. Die Zahl verriete, dass es diesen Stream gibt
  und wie viel über ihn gelaufen ist; aus einem geratenen Versuch würde eine
  Sonde. Auskunft bekommt nur, wer ohnehin Zugriff hätte — dieselbe Grenze wie
  bei der Übernahme selbst (R2).
- **Abgelaufen, aber noch da** — das echte `h`. Der Fall, den der Abschnitt
  ausdrücklich nennt („an earlier session that has timed out").

Auf der Client-Seite liest `ProcessFailed(xml)` den Stand jetzt über
`ProcessAck` — dieselbe Modulo-Arithmetik wie bei jedem `<a h='…'/>`, denn zwei
Auffassungen derselben Rechnung sind eine zu viel. Verloren ist danach nur, was
**darüber hinaus** offen war. Das ist kein Schönheitsfehler: Abschnitt 4
empfiehlt, Verlorenes erneut zu schicken — auf der alten Grundlage stellte das
alles ein zweites Mal zu.

**Ein Testschalter, und diesmal einer, der gebraucht wird.**
`SweepResumableStreams` hält den Abräumer an. Ohne ihn ist der dritte Fall nur
im Wettlauf zu treffen: Der Durchgang geht im Sekundentakt, und was er abgeräumt
hat, weiss der Server nicht mehr — das Fenster ist im Betrieb höchstens eine
Sekunde breit.

**Die Mutation, die zuerst überlebt hat, war genau dieser Schalter.** Mit den
üblichen 200 ms Wartezeit kam der Rückkehrer dem Abräumer schlicht zuvor, und
beide neuen Tests bestanden auch dann, wenn der Schalter wirkungslos war — sie
gewannen ein Rennen, das sie gar nicht hätten laufen sollen. Drei Sekunden
Wartezeit später ist der Fall herbeigeführt statt erhofft, und die Mutation
fällt.

Sieben Mutationen, alle erschlagen: `h='0'` statt Weglassen, `h` nie genannt,
`h` auch an ein fremdes Konto, Frist nicht geprüft, Client liest den Stand
nicht, Rahmen erreicht den Client-Manager nicht, Abräumer nicht anzuhalten. Die
ersten sechs sind nach der Teständerung noch einmal gelaufen — ein Urteil über
eine Fassung, die es nicht mehr gibt, ist keines (siehe D44).

Am Server bleiben damit zwei Punkte: SCRAM anbieten und Stanza-Fehler auch dort
erzeugen, wo es keinen Schalter dafür gibt.

---

### D50. Ein Konto, das es nicht gibt ✅ — und eine Quelle, die nichts sagt

Wieder ein Punkt, der älter war als seine Erledigung: „SCRAM anbieten, damit der
SCRAM-Pfad des Clients integrativ geprüft wird". **S2 hat das getan** — der
Server bietet SCRAM-SHA-256, SCRAM-SHA-1 und PLAIN an, der Client nimmt von
sich aus den stärksten, und damit läuft die gesamte Suite über SCRAM-SHA-256.
Es steht sogar wörtlich in S2 („zum ersten Mal integrativ geprüft"). Die Liste
hat es wieder nicht erfahren.

Offen war etwas, das S2 selbst notiert hatte:

> Ein unbekanntes Konto wird abgelehnt, bevor der Austausch beginnt. Damit
> verrät der Server, ob es ein Konto gibt; **RFC 5802 §7** empfiehlt, mit einem
> erfundenen Salt weiterzumachen.

**Die Quellenangabe stimmt nicht.** RFC 5802 §7 ist die formale Syntax, und der
RFC empfiehlt an keiner Stelle ein erfundenes Salt — er führt in eben dieser
Syntax sogar ein `unknown-user` als Fehlerwert und überlässt es dem Server, ob
er den echten Grund durch `other-error` ersetzt. Die Empfehlung, die gemeint
war, steht woanders und ist deutlicher: **RFC 6120 §13.11, „Directory
Harvesting"** — „not reveal whether or not an account exists at a server when an
entity attempts to authenticate". Ein Satz, der zweimal falsch zitiert dastand
(im WORKPLAN und in `UnknownUser_DoesNotStart`), belegt nichts; er sieht nur so
aus.

**Der Fehlerwert war nie das Problem.** Beide Fälle bekamen schon vorher
`<not-authorized/>`, und §6.5.10 deckt beide ausdrücklich ab: „this might
include, but is not limited to, the case in which the user does not exist".
Verraten hat der **Ablauf**:

| | erste Nachricht | zweite Nachricht |
|---|---|---|
| Konto vorhanden, Passwort falsch | `<challenge/>` | `<failure/>` |
| Konto nicht vorhanden | `<failure/>` | — |

Eine Runde Unterschied, und eine Namensliste ist in einem Durchgang sortiert.

Jetzt läuft der Austausch auch für einen unbekannten Namen zu Ende, mit
**erfundenen Zugangsdaten aus dem Benutzernamen und einem Serverschlüssel**.
Drei Eigenschaften, und jede davon hat ihren eigenen Test, weil jede für sich
allein die Massnahme aushebelt:

- **gleichbleibend** — ein Salt, das bei jedem Versuch anders ausfällt, ist
  selbst die Auskunft; das eines echten Kontos steht fest. Zweimal fragen
  genügte.
- **je Name verschieden** — ein festes, eingebautes Salt wäre die schlechteste
  Lösung von allen: Zwei Namen mit demselben Salt gibt es unter echten Konten
  nicht.
- **nicht vorherzusagen** — der Serverschlüssel ist zufällig, sonst rechnet der
  Fragende die erfundenen Salts selbst nach und sortiert wie zuvor.

Dazu Iterationszahl und Salt-Länge wie bei einem echten Konto; beides steht
offen in der server-first-message.

**Was das nicht leistet, steht dabei:** Über einen Neustart hinweg wechseln die
erfundenen Salts, die echten nicht — der Serverschlüssel lebt im Prozess. Ein
dauerhafter gehörte in den Kontenspeicher. Und **PLAIN** bleibt unberührt: Dort
ist der Ablauf ohnehin in beiden Fällen derselbe, es unterscheidet sich nur die
Laufzeit (ein echtes Konto rechnet PBKDF2, ein unbekanntes nicht). Das zu
schliessen wäre leicht, ein Test dafür aber würde die Maschine messen und nicht
den Code — deshalb hier benannt und nicht heimlich mitgemacht.

Sieben Mutationen, sechs erschlagen: sofort scheitern, Salt zufällig, Salt für
alle gleich, Iterationszahl abweichend, Salt kürzer, Sicherung gegen eine
Anmeldung ohne Konto entfernt. **Die siebte überlebt und soll es:** Die
erfundenen *Schlüssel* hängen ebenfalls am Namen, und das kann kein Test
bemerken — sie erreichen die Leitung nie. Über den StoredKey läuft nur der
Vergleich, und die server-final-message, in der der ServerKey steckt, gibt es
nur bei einer geglückten Anmeldung, die es hier nie gibt. Die Ableitung bleibt
trotzdem: Sie kostet nichts und ist die Konstruktion, die man verteidigen kann.

Der eine Test, den es dazu doch gibt, musste sich den Fall borgen:
`AValidProof_IsNotEnoughWithoutAnAccount` schiebt dem Austausch die **echten**
Zugangsdaten als erfundene unter. Der Beweis stimmt dann — und wird trotzdem
abgewiesen, weil kein Konto dahintersteht.

Am Server bleibt damit ein Punkt: Stanza-Fehler auch dort erzeugen, wo es
keinen Schalter dafür gibt.

---

### D51. Eine Adresse, die keine ist ✅ — `<jid-malformed/>`

Der letzte Punkt der Serverliste, und wieder war er zur Hälfte längst erledigt:
Der Server erzeugt seit D26 bis D50 eine ganze Reihe von Stanza-Fehlern von
sich aus — `<bad-request/>` für einen unbekannten IQ-Typ, `<service-unavailable/>`
für einen unzustellbaren Empfänger und für ein `groupchat` an ein Konto,
`<remote-server-not-found/>` für eine unerreichbare Domain, `<item-not-found/>`
für einen unbekannten disco-Knoten. Die Schalter sind längst nicht mehr die
einzige Quelle.

**Eine Bedingung fehlte vollständig, und zwar die, für die alles bereitlag.**
`<jid-malformed/>` (RFC 6120, Abschnitt 8.3.3.8) kam im ganzen Server nicht vor
— das Wort stand an genau einer Stelle im Quelltext, im Kommentar von
`JidFormatException`. Und die Prüfung dahinter gibt es seit **D42 bis D45
vollständig**: RFC 7622 mit PRECIS, IDNA2008, der Bidi-Regel und den
kontextabhängigen Regeln aus Anhang A, gegen die Tabellen der UCD gerechnet.

Der Server hat sie nie gefragt. `JidUtilities` kam in `XMPPServer.cs` genau
einmal vor, in `AreEqual` beim Vergleich zweier Full-JIDs. Was hereinkam, ging
in die Zustellung, und ein unmöglicher Empfänger sah dort aus wie ein
abwesender: Der Absender bekam Schweigen oder eine Ablage, aus der ihn nie
jemand abholt.

**Das ist zum dritten Mal dasselbe Muster.** In D43 war die IDNA-Prüfung fertig
und im JID nicht verdrahtet, in D45 die kontextabhängigen Regeln. Eine geprüfte
Regel ohne Aufrufer ist keine halbe Regel, sondern keine — und sie fällt
niemandem auf, weil ihre eigenen Tests grün sind.

Die Prüfung sitzt **vor der Weiche**, an einer Stelle für alle drei Arten: Jeder
Zweig dahinter stellt seine eigenen Fragen, und diese gehört keinem von ihnen.
Drei Grenzen dazu, jede mit einem Test:

- **Kein `to` ist kein falsches `to`.** Eine Stanza ohne Adresse ist an den
  Server gerichtet (§8.1.1.1), und ungerichtete Presence trägt nie eine. Die
  Mutation, die beides gleich behandelt, legt die halbe Sammlung lahm — ohne
  Presence gilt keine Sitzung als verfügbar.
- **Auf einen Fehler folgt kein Fehler** (§8.3.1). Verworfen wird die Stanza
  trotzdem: zustellbar ist sie ja nicht.
- **Absender der Ablehnung ist der Server**, nicht der gemeinte Empfänger.
  `<service-unavailable/>` antwortet im Namen eines Empfängers, weil der Server
  dort für ihn geantwortet hat; hier gibt es keinen — die Adresse ist keine,
  also hat niemand hineingesehen.

Fünf unmögliche Adressen im Test, und jede aus einem anderen Grund: `alice@`
fällt schon einem Vergleich auf zwei leere Zeichenketten auf, `alice@-localhost`
erst der Labelregel aus RFC 5891, `al ice@localhost` nur der
PRECIS-IdentifierClass. Eine einzige liesse offen, wie weit die Prüfung reicht.

**Die Lücke, die mir selbst auffiel:** Kein Test hielt fest, dass die
abgewiesene Stanza auch wirklich endet. Eine Prüfung, die antwortet und danach
trotzdem zustellt, wäre von der richtigen nicht zu unterscheiden gewesen —
`ARefusedStanza_IsNotDeliveredAnyway` schickt deshalb an `bob@…/`: kein JID,
aber der Teil davor gehört einem angemeldeten Konto, und über den Weg für
Bare-JIDs käme es bei Bob an.

Sieben Mutationen, keine übersteht den Lauf:

| | Mutation | erschlagen von |
|---|---|---|
| X1 | die Prüfung entfällt | 8 Tests |
| X2 | eine fehlende Adresse gilt als falsche | siehe unten |
| X3 | Fehlerart `cancel` statt `modify` | die fünf unmöglichen Adressen |
| X4 | Absender ist der gemeinte Empfänger | dieselben fünf |
| X5 | auch eine Fehler-Stanza wird beantwortet | `AnErrorStanza_IsNotAnsweredWithAnError` |
| X6 | abgewiesen, aber trotzdem weitergereicht | `ARefusedStanza_IsNotDeliveredAnyway` |
| X7 | die `id` der Anfrage geht verloren | `AnIqToANonJid_KeepsItsId` |

**X2 wird nicht von einer Zusicherung erschlagen, sondern vom Hänger-Schutz** —
und das ist selbst der Befund. Gilt eine fehlende Adresse als falsche, wird
jede ungerichtete Presence abgewiesen; keine Sitzung wird je verfügbar, und der
Verbindungsaufbau des Clients wartet darauf **ohne eigene Frist**. Der erste
Lauf stand deshalb 74 Minuten, bis ich ihn abgebrochen habe; mit
`--blame-hang-timeout 3m` bricht der Testlauf nach drei Minuten mit einem
Hangdump ab. Durchgehen könnte die Mutation nie — bestanden ist etwas anderes
als abgestürzt —, aber gemessen hat sie kein Test.

**Zwei Lehren aus dem Abbruch, beide teuer bezahlt:**

1. *Der Hänger-Schutz gehört an jede Mutation, nicht nur an die, von der man
   ihn erwartet.* Das Skript hat den Schalter seit M2 und ich hatte ihn nicht
   gesetzt.
2. *Ein abgebrochener Mutationslauf lässt den Quelltext mutiert zurück.*
   `mutate.ps1` setzt erst zurück, wenn `dotnet test` zurückkommt — wird es
   abgeschossen, steht die Mutation noch da. Die Sicherung vom Mutationszeitpunkt
   hat sie eingefangen; ohne die Prüfung „ist meine Zeile wieder da" wäre sie
   in den Commit gewandert. Genau das war schon einmal die Ursache in D39, nur
   andersherum.

Nebenbei: Der Hangdump legt 219 MB unter `Jabber.Tests/TestResults/` ab, und
das Verzeichnis stand in keinem `.gitignore`. Ein `git add -A` hätte ihn
mitgenommen. Steht jetzt drin.

---

### D52. Schweigen ist auch eine Antwort ✅ — der stillschweigend verworfene Fall

Der erste der beiden Funde aus D51. In `StoreOfflineOrRefuseAsync` stand:

```csharp
if (GetAccount(BareOf(to)) is not { } account)
    return;
```

Eine Nachricht an ein Konto, das es nicht gibt, verschwand. RFC 6121,
Abschnitt 8.5.1 erlaubt das ausdrücklich — für einen unbekannten Empfänger
steht `<service-unavailable/>` **oder** Schweigen zur Wahl.

**Frei ist die Wahl trotzdem nicht.** Sie muss dieselbe sein wie für ein
vorhandenes Konto, das gerade nicht zusieht, sonst beantwortet sie eine ganz
andere Frage: *Gibt es dieses Konto?* Und zwar auf dem bequemsten Weg, den es
gibt — eine Nachricht schicken und hinsehen, ob etwas zurückkommt. Das ist
dieselbe Frage wie in D50, nur ohne Anmeldung.

Auseinander fiel sie, sobald die Ablage nicht annahm:

| | Ablage an | Ablage aus oder voll |
|---|---|---|
| Konto vorhanden, abwesend | Schweigen (abgelegt) | `<service-unavailable/>` |
| Konto nicht vorhanden | Schweigen (verworfen) | **Schweigen** |

In der rechten Spalte steht die Auskunft. Auf einem Server ohne Offline-Ablage
ist jede Namensliste in einem Durchgang sortiert.

**Gefragt wird deshalb nicht mehr „gibt es ein Konto", sondern „würde die
Ablage es annehmen".** Für ein unbekanntes ist die Ablage leer, und eine leere
nimmt an, solange überhaupt etwas hineinpasst:

```csharp
account?.StoreOfflineMessage(…) ?? MaxStoredOfflineMessages > 0
```

**Der zweite Summand ist der Punkt.** Ein schlichtes `?? true` wäre 99 von 100
Fällen richtig und im hundertsten falsch: Bei `MaxStoredOfflineMessages = 0`
nimmt auch eine leere Ablage nichts an, das vorhandene Konto bekommt einen
Fehler — und das unbekannte hätte wieder geschwiegen. `AFullStore_RefusesForBothAlike`
hält genau das fest, und die Mutation `?? true` stirbt daran.

Die wichtigere Gegenprobe ist aber `WithTheStore_NeitherRecipientIsTold`: „Antworte
für Unbekannte einfach immer" wäre die naheliegende Lösung und träfe **genau
daneben** — bei eingeschalteter Ablage, also der Vorgabe, bekäme dann das
vorhandene Konto Schweigen und das unbekannte einen Fehler. Die Frage wäre
wieder beantwortet, nur andersherum. Der Test war der einzige der drei, der von
Anfang an grün war; ohne ihn wäre die Verschlimmbesserung nicht aufgefallen.

Vier Mutationen, alle erschlagen: wieder stillschweigend verwerfen, `?? true`,
`?? false`, und die abgeschaltete Ablage nicht mehr fragen.

Angelegt wird für den unbekannten Empfänger nichts — der Test sieht nach.
Nachgereicht wird ihm auch nie etwas; das ist der Unterschied zwischen „tut so,
als sei abgelegt worden" und „legt ab", und er fällt niemandem auf, weil es das
Konto nicht gibt.

---

### D53. Dieselbe Prüfung, andere Tür ✅ — `<jid-malformed/>` über die Grenze

Der zweite Fund aus D51. Die Prüfung des `to` galt nur für Stanzas von
Clients; was über `AcceptFromRemoteAsync` von einer Gegenstelle kam, wurde auf
Herkunft und Zuständigkeit geprüft und dann zugestellt. **Dort trifft sie den
wahrscheinlicheren Fall:** Den eigenen Client schreibt dieselbe Bibliothek, die
fremde Implementierung nicht.

**Beim Hinsehen hatte das `from` dieselbe Lücke, und die ist die ernstere.**
`DomainOf("al ice@left.example")` liefert brav `left.example`, die
Zuständigkeitsprüfung ist zufrieden, und eine Stanza mit einer Absenderadresse,
die keine ist, läuft durch. Bruchstücke zu vergleichen und das Ergebnis „fremde
Domain" zu nennen ist keine Prüfung.

Die beiden Fälle wiegen verschieden schwer, und darin liegt die eigentliche
Entscheidung:

- **`MalformedSender`** geht denselben Weg wie `ForeignSender`: RFC 6120,
  Abschnitt 8.1.1.1 nennt beides ein ungültiges `from`, der Stream endet mit
  `<invalid-from/>`. Der Grund trägt genauso — wer einmal etwas ohne Adresse
  schickt, tut es beim nächsten Versuch wieder.
- **`MalformedRecipient`** kostet nur die eine Stanza, dazu ein
  `<jid-malformed/>` zurück an den Absender. Das ist ein Tippfehler in einer
  Adresse und keine Aussage darüber, wer da spricht. Risse er die Föderation
  ab, wäre die Prüfung schlimmer als ihr Nutzen — `AMalformedRecipient_DropsOnlyThatStanza`
  hält die Grenze fest.

**Die Reihenfolge ist selbst eine Aussage** und hat deshalb einen eigenen
Testfall. Bei `bob@-right.example` ist schon die Domain keine; `IsLocal` hielte
sie für die einer dritten Partei. Stünde die Prüfung dahinter, wäre die Stanza
richtig abgewiesen und **falsch begründet** — der Absender suchte den Fehler an
der falschen Stelle. Die Mutation, die genau das tut, stirbt an diesem Fall und
an keinem anderen.

Der Fehlerrahmen aus D51 ist dabei zu **einer** Fassung zusammengezogen
(`JidMalformedError`). Zwei Buchstabierungen hätten sich nur in Kleinigkeiten
unterschieden, und genau die wären der Unterschied gewesen, den niemand
bemerkt: Ein Client, der über die Grenze eine andere Fehlerart bekommt als im
eigenen Haus, hat zwei Fälle zu behandeln, wo es einen gibt.

Sieben Mutationen, alle erschlagen: Absender nicht geprüft, Empfänger nicht
geprüft, Empfänger erst nach der Zuständigkeitsfrage, Fehler-Stanza wird
beantwortet, Ablehnung nennt den Empfänger als Absender, unmöglicher Absender
beendet den Stream nicht mehr, und — die Gegenrichtung — jede Ablehnung beendet
den Stream.

Eine Beobachtung am Rande, die beim nächsten Mal Zeit spart: In den
Mutationsläufen standen **11 übersprungene** Tests statt der gewohnten 7. Kein
Rätsel, sondern die fehlenden Umgebungsvariablen `JABBER_*_CERTS` — `mutate.ps1`
gibt sie nicht weiter. Für diese Mutationen war es folgenlos (keine davon
betrifft die fremden Gegenstellen), aber eine Mutation im S2S-Transport wäre
dort gegen weniger Tests gemessen worden, als der Name der Sammlung verspricht.

---

### D54. Eine Wache, an die niemand denken muss ✅

Der Punkt lautete: *Die Verdrahtung der Wache ist eine mechanische Eigenschaft
und von keinem Test gehalten. Nähme jemand in einem einzelnen Fixture das
`AssertClean()` heraus, fiele es nicht auf.* Gesichert war sie durch eine
Quelltextprüfung von Hand — „kein `new XMPPServer(` ohne `Watched(…)`" (D19),
39 Erzeugungsstellen in 17 Dateien.

**Nicht abgesichert, sondern abgeschafft.** Ein Test, der prüft, dass jedes
Fixture die beiden Zeilen schreibt, wäre nur eine zweite Stelle gewesen, an der
dasselbe Vergessen möglich ist: Er hätte den Quelltext gelesen und nichts
gemessen, und für das Fixture von morgen hätte er nichts getan.

Stattdessen meldet jeder `XMPPServer` seine Entstehung — ein `internal static
event OnInstanceCreated`, ausgelöst am Ende des Konstruktors —, und ein
`ITestAction` auf Assembly-Ebene hängt sich an jeden davon. Damit ist die Wache
keine Eigenschaft mehr, die jemand herstellen muss, sondern eine, die von
selbst gilt.

Drei Zeilen Produktivcode allein für die Testsammlung sind eine Entscheidung
und keine Selbstverständlichkeit. Sie sind vertretbar, weil sie `internal`
sind — nach aussen sagt der Server nichts zu —, und weil die Alternative war,
sich weiter auf die Aufmerksamkeit von Menschen zu verlassen. Der Server trägt
ohnehin ein Dutzend Testschalter; dies ist der erste, der nicht sein Verhalten
ändert, sondern nur zusieht.

**Die Wache je Fixture bleibt.** `InternalErrorGuard` liefert `InternalErrors`
für die Tests, die die Meldungen *ansehen* wollen. Was wegfällt, ist ihre
Unverzichtbarkeit: Wer künftig `Watched(…)` oder `AssertClean()` vergisst,
verliert nichts mehr. `Expect()` reicht die Absicht an die globale Wache
weiter — sonst müsste ein Fixture zweimal sagen, dass sein Fehler gewollt ist,
und die zweite Stelle wäre wieder eine zum Vergessen.

**Der Test, ohne den das Ganze wertlos wäre:** dass die neue Wache auch
*scheitern lässt*. Die schlimmste Fassung ist die, die alles aufnimmt und nie
etwas daraus macht — sie sieht aus wie eine Sicherung, ist keine, und die
Sammlung bleibt grün. Genau dieselbe Falle hat `InternalErrorGuard.Record`
schon entschärft, und aus demselben Grund gibt es das Aufnehmen jetzt auch hier
getrennt vom Anhängen.

Dazu die Trennung zwischen zwei Tests: Bliebe eine Meldung stehen, fiele es nur
dem *nachfolgenden* Test auf — und welcher das ist, entscheidet der Testläufer.
Der Test stellt den Übergang deshalb selbst nach: melden, scheitern lassen, den
nächsten Test beginnen, nachsehen.

**Der erste volle Lauf mit scharfer Wache war sauber.** Die Quelltextregel aus
D19 war also lückenlos eingehalten — nur eben von Hand. Sechs Mutationen, alle
erschlagen: Entstehung nicht gemeldet, Wache macht aus dem Gemeldeten nichts,
räumt zwischen zwei Tests nicht auf (24 Tests fallen mit), hängt sich an keinen
Server, reicht die Absicht nicht weiter, und läuft einmal je Sammlung statt je
Test.

Die aufschlussreichste ist die dritte: **Eine fehlende Zeile schleppt jede
Meldung in alle folgenden Tests.** Genau deshalb steht der Übergang zwischen
zwei Tests als eigene Zusicherung da und nicht als Hoffnung auf die Reihenfolge
des Testläufers. Und die fünfte zeigt die Kehrseite der neuen Reichweite: Ohne
die Weitergabe von `Expect()` fallen die fünf Tests, die absichtlich einen
internen Fehler auslösen — die Wache über alle Server sieht eben auch das, was
gewollt ist.

**Ein Lauf, der nichts gemessen hat, sah dabei aus wie ein bestandener.** Der
erste Anlauf zum vollen Durchgang meldete `782 erfolgreich, 25 übersprungen` —
grün. Die Gegenstellen liefen, die Zertifikatspfade waren lesbar; die
Umgebungsvariablen hatten den Testprozess nur nicht erreicht, weil der Lauf
über die Bash-Schale statt über PowerShell gestartet war. **Die Zahl der
übersprungenen Tests ist das einzige, was die beiden unterscheidet** — 7 heisst
„beide fremden Server standen", alles darüber heisst „die Föderation wurde
nicht angefasst". Wiederholt, diesmal richtig: 800 grün, 7 übersprungen.

---

### D55. Eine Zahl, wo eine Beziehung gemeint war ✅ — der Wackler ist erklärt

`NonzasDoNotAdvanceTheCount` gegen Prosody, aufgefallen in D34 als **ein**
Fehlschlag in einem Vollauf und danach in zwanzig gezielten Ausführungen nicht
zu wiederholen. Der Mitschnitt aus D35 wurde nie fällig — geklärt ist der Fall
trotzdem, und zwar aus den beiden Zahlen, die schon im Protokoll standen:

```
Wir haben Nonzas mitgezählt.               Expected: 6  But was: 8
Prosody hat andere Nonzas mitgezählt.      Expected: 8  But was: 6
```

Der Ausgangsstand war 3, Prosody bestätigte **6** — also genau die drei
Nachrichten des Tests, und keine einzige der sechs Nonzas. **Prosody hat
richtig gezählt, und wir auch.** Bei uns standen nur zwei Stanzas mehr im
Zähler, die dieser Test nicht geschickt hat und die nach Prosodys `<a/>`
hinausgingen.

Damit ist die naheliegende Erklärung — „eine Seite zählt Nonzas mit" — genau
die, die nicht zutrifft. Ein Client schickt von sich aus: Er beantwortet, was
hereinkommt, und **wann** das geschieht, bestimmt nicht der Test. Die drei
Nachrichten gehen an das eigene Konto und kommen zurück; was der Client
daraufhin tut, fällt in das Fenster zwischen der Bestätigung und dem Ablesen
des Zählers.

**Der Fehler lag im Test, nicht im Zähler.** Er prüfte „der Stand ist um genau
drei gestiegen" — eine Zahl. Abschnitt 2 sagt aber keine Zahl, sondern eine
Beziehung: *der Zähler steigt um die Stanzas und um nichts sonst.* Genau die
steht jetzt da, gemessen gegen den Mitschnitt statt gegen die Absicht:

```csharp
Assert.That(sm.OutboundCount - vorher, Is.EqualTo(Gezaehlt(hinaus)));
```

Drei ist nur noch die Untergrenze, damit überhaupt etwas gemessen wird, und
eine vierte Zusicherung verlangt mindestens drei **Nonzas** im Ausgang — sonst
prüfte der Test seine eigene Überschrift nicht.

**Gezählt wird mit einer eigenen Fassung der Regel**, nicht mit
`StreamManagementManager.IsCountableStanza`. Die ist die Funktion, deren
Ergebnis hier geprüft wird; nähme der Test sie, verglich er eine Zahl mit sich
selbst und bestünde auch dann, wenn sie falsch antwortet — dieselbe Trennung,
aus der auch der Testserver eigenständig zählt.

Dazu ein Nachfrage-Anlauf statt eines einzigen `<r/>`: Was nach dem letzten
`<r/>` hinausgeht, bliebe sonst für immer unbestätigt, und die Gleichheit der
beiden Stände käme nie zustande. Drei Runden, jede mit eigener Nachfrage.

Vier Mutationen, alle erschlagen: ausgehend alles mitzählen, ausgehend nichts
mitzählen, der Zähler springt um zwei, und nur `<message>` zählt. Die erste ist
die eigentliche Probe — sie fällt in beiden Ableitungen, gegen Prosody wie
gegen ejabberd.

**Und das Werkzeug ist mitrepariert:** `mutate.ps1` reicht jetzt die
`JABBER_*_CERTS` weiter (siehe die Beobachtung in D53). In allen Läufen dieses
Eintrags stand `übersprungen: 0` — vorher wären es die Hälfte der Tests
gewesen, und die Mutation hätte gegen die fremden Server gar nichts gemessen.

---

### D56. Vierzig Läufe, die nichts widerlegen konnten ✅

Der zweite Wackler, und er ist das Gegenstück zu D55: Dort war die Erklärung
falsch, hier war es die **Widerlegung**.

`TheStreamSurvivesABrokenConnection` fiel in D16 einmal mit „Der Stream wurde
binnen 15 Sekunden nicht wieder aufgenommen". D33 hat daraufhin gemessen —
vierzig Ausführungen, alle zwischen 519 und 669 Millisekunden — und daraus
geschlossen, die Erklärung „unter Last knapp" trage nicht. Die Frist blieb.

**Der Schluss war falsch, und zwar aus Arithmetik.** Der Client darf in diesem
Test fünfmal wiederkommen und wartet dazwischen mit Verdopplung, beginnend bei
300 Millisekunden:

| Anlauf | 1 | 2 | 3 | 4 | 5 | Summe |
|---|---|---|---|---|---|---|
| Wartezeit davor | 300 ms | 600 ms | 1,2 s | 2,4 s | 4,8 s | **9,3 s** |

Von den 15 Sekunden blieben also **5,7 für fünf vollständige
Verbindungsaufbauten** — Aushandlung, TLS, SASL, Bind, Wiederaufnahme. Zwei
fehlgeschlagene Anläufe genügen, und die Frist ist überschritten, während der
Client sich genau so verhält, wie er eingestellt ist.

**Die vierzig schnellen Durchgänge widerlegen das nicht — sie sind alle beim
ersten Anlauf durchgekommen.** Über den Fall mit Wiederholungen sagen sie
nichts. Ein Mittelwert aus lauter geglückten Läufen begrenzt den Ausreisser
nicht; er beschreibt nur, wie es aussieht, wenn nichts schiefgeht. Die
Verteilung ist zweigipflig, und gemessen wurde ausschliesslich der vordere
Gipfel.

Die Geduld ist deshalb keine geratene Zahl mehr, sondern die Summe dessen, was
der Client tun darf: die Wartezeiten seiner eigenen Politik plus je drei
Sekunden für den Anlauf selbst. Für diese Einstellung sind das gut 24 statt 15
Sekunden. Die Meldung nennt beim Scheitern jetzt auch, woraus die Frist besteht
— sonst rechnet der nächste Leser dasselbe noch einmal nach.

**Was sich nicht herbeiführen lässt, lässt sich nicht durch einen Test halten,
der auf sein Eintreten wartet** — der Fehlschlag trat einmal auf und war
danach in vierzig Ausführungen nicht zu wiederholen. Nachrechnen lässt er sich
dafür: `ThePatienceCoversWhatTheClientMayTake` prüft die Frist gegen die von
Hand gerechneten 9,3 Sekunden plus fünf Anläufe. Die Zahlen stehen dort
ausgeschrieben und nicht als Aufruf derselben Formel — sonst prüfte der Test
sie gegen sich selbst, dieselbe Trennung wie bei der Zählung in D55.

Es ist zugleich die einzige Prüfung dieser Sammlung, die ohne Gegenstelle
auskommt: Sie rechnet, statt zu warten. Drei Mutationen, alle erschlagen:
zurück zur festen Frist, der Aufbau kostet nichts, nur der erste Anlauf zählt.

**Damit ist die Ursache benannt, aber nicht bewiesen.** Bewiesen ist, dass die
alte Frist den eingestellten Ablauf nicht deckte; ob genau das in D16 zugeschlagen
hat, bleibt die wahrscheinlichste Erklärung. Der Unterschied zu vorher: Sie
passt zu den Daten, statt ihnen zu widersprechen.

---

### D57. Elf Member, drei Entscheidungen ✅

„Ungenutzte öffentliche Member entscheiden: benutzen oder streichen." Die Liste
stand im README, seit es sie gab. **Der erste Schritt war, ihr nicht zu
glauben** — sie warnt selbst davor, dass sie „in die falsche Richtung
veraltet", und genau das war eingetreten: `ResumeAsync`, `GetUnackedStanzas`
und `OnStanzasLost` werden längst benutzt, das letzte davon seit D49. Drei von
elf Einträgen waren schlicht falsch.

**Benutzt (3):**

- **`RosterStanzaBuilder.GetRoster`.** `XMPPConnection` setzte dieselbe Anfrage
  daneben von Hand zusammen — zwei Schreibweisen einer Stanza. Die Feinheit
  stand dabei nur in einer: Ein *leeres* `ver=''` ist kein Platzhalter, sondern
  die Ansage „ich kann Versionierung, habe aber noch nichts" (RFC 6121 §2.6.1).
  Sie steht jetzt im Baustein, dort, wo sie hingehört.
- **`RosterStanzaBuilder.Unsubscribe`** über ein neues
  `CancelSubscriptionAsync`. Von den vier Übergängen aus RFC 6121 §3 bot der
  Client drei an; der vierte fehlte, obwohl der Baustein dastand und der Server
  ihn seit S3b beherrscht. Aufgefallen ist er nicht, **weil der Test die Lücke
  überbrückt hat**: `Unsubscribe_EndsTheOwnSubscription` schrieb die Presence
  selbst. Ein Test, der am Client vorbei prüft, hält das Verhalten und
  verbirgt, dass es keinen Weg dorthin gibt.
- **`DiscoInfo.HasFeature`** — von einem Test, der die Frage vorher an der
  Merkmalsliste vorbei stellte.

**Gestrichen (8):** `MessageReceipt` (der Typ dokumentierte selbst, dass ihn
niemand erzeugt), `ReceiptTracker.GetTimedOutMessages` (es gibt keine Frist,
die ablaufen könnte), `PubSubManager.OnSubscriptionResult`,
`PubSubBuilder.Retract` und `DiscoverNodes`, `CarbonManager.DisableIq` und die
fünf `DiscoInfo.Supports*`.

Die fünf Abkürzungen sind der lehrreichste Fall: Jede war eine Zeile über
`HasFeature` und trug ihren Namensraum eingebaut mit sich. Sie konnten nichts,
was `HasFeature` nicht kann — aber sie führten eine zweite Abschrift jedes
Namensraums, und die veraltet für sich allein.

**Der Bau ist jetzt warnungsfrei.** `OnSubscriptionResult` war die einzige
Warnung (CS0067, „wird nie verwendet") und stand über Dutzende von Läufen in
jeder Ausgabe. Eine Warnung, die immer da ist, wird zur Tapete — und die
nächste, die dazukommt, fällt dann nicht mehr auf.

Drei Mutationen auf das neu Benutzte, alle erschlagen: die Kündigung schickt
`unsubscribed` statt `unsubscribe`, die Roster-Anfrage lässt die Fassung immer
weg, `HasFeature` bejaht alles.

**Was das Streichen nicht ist: eine Aussage über XEP-0060.** Der Punkt unter
„Optional" bleibt, wie er war — es fehlte dort nie die Meldung, sondern die
Korrelation von IQ-Ergebnis und Anfrage. Wer sie baut, deklariert das Ereignis
in derselben Stunde wieder. Ein nie ausgelöstes Ereignis ist keine halbe
Umsetzung, sondern eine Zusage ohne Deckung.

**Und die Liste kommt nicht wieder.** Eine stehende Aufzählung ungenutzter
Member ist eine Buchhaltung, die niemand führt: Sie stimmt am Tag ihrer
Entstehung und danach nie wieder. Was ungenutzt ist, entscheidet der Compiler
(bei Ereignissen) oder eine Suche (bei allem anderen) — beides in Sekunden und
immer aktuell.

---

### D58. Eine Tür für alles, was auf die Konsole geht ✅

Der Punkt lautete: „Der Standard-Konsolenlogger schreibt in dieselbe Konsole
wie die Eingabezeile und zerlegt den Prompt. Ein eigener `ILoggerProvider` über
die **synchronisierte Ausgabe** wäre die saubere Lösung."

**Die synchronisierte Ausgabe gab es nicht.** Was es gab, war eine Verabredung:
Jede Ereignisbehandlung klammerte ihre Ausgabe von Hand in `ClearCurrentLine()`
… `WritePrompt()` — elfmal dieselben zwei Zeilen. Wer eine davon vergisst,
merkt es erst im Betrieb, und **eine Sperre lag über keiner von ihnen**. Die
Ereignisse kommen aus dem Empfangsfaden, das Protokoll aus jedem beliebigen;
zwei gleichzeitige Ausgaben verschränken sich mitten im Wort, samt der Farbe,
die die eine gesetzt und die andere zurückgestellt hat.

Der Logger war also nur der auffälligste von drei Fällen desselben Problems.

`ConsoleOutput` ist jetzt die eine Tür. Sie kann zweierlei:

- `Write(w => …)` für eine Ausgabe in einem Zug,
- `Begin()` für die, die sich nicht in einen Rückruf fassen lassen, ohne
  unleserlich zu werden — die PubSub-Ausgabe wechselt in einer `switch`-Weiche
  die Farbe. Der Bereich hält die Sperre bis zum Verlassen und zieht dann die
  Eingabeaufforderung nach.

Damit schrumpfen die elf Klammern auf je eine Zeile (`using var sperre =
Ausgabe();`), und der Logger geht durch dieselbe Tür — das ist der ganze
Unterschied zwischen `AddSimpleConsole` und `ConsoleOutputLoggerProvider`.

**Zwei Kleinigkeiten, die dabei mit abfielen:**

- Der volle Kategoriename ist der Typname samt Namensraum, hier rund fünfzig
  Zeichen — auf einer Konsole mit Eingabezeile die halbe Breite für eine
  Auskunft, die in jeder Zeile dieselbe ist. Es steht jetzt nur der letzte Teil
  da.
- `ILogger` reicht die Ausnahme **getrennt** vom Text durch, und der
  Formatierer lässt sie weg. Wer sie nicht selbst anhängt, protokolliert
  „Verbindung verloren" und verschweigt, woran.

**Der Teil des Projekts, der bis hierher gar keine Tests hatte**, hat jetzt
acht. Geprüft wird gegen einen `StringWriter` mit vorgegebener Breite: Auf
einem Testläufer gibt es kein Fenster, und der Test soll die Zeile löschen und
nicht die Umgebung ausmessen.

Fünf Mutationen, alle erschlagen: Zeile nicht räumen, Eingabeaufforderung nicht
nachziehen, Sperre nur halb entfernt (das wirft beim Verlassen und reisst alle
acht mit), Logger schreibt an der Ausgabe vorbei — und **die Sperre
vollständig entfernt**. Die letzte ist die interessante: Sie tötet **genau
einen** Test, `ParallelWriters_DoNotInterleave`. Damit ist belegt, dass er die
gegenseitige Ausschliessung wirklich misst und nicht nur mitläuft.

Ein Test, der beim ersten Lauf rot war, hatte übrigens unrecht und nicht der
Code: `WriteLine` endet unter Windows auf `\r\n`, und „die Ausgabe enthält
keinen Wagenrücklauf" ist deshalb nie wahr. Gemeint war die Löschfolge am
Anfang — geprüft wird jetzt der Anfang.

---

### D59. Eine Uhrzeit, die dasteht und nicht stimmt ✅ — XEP-0203 gelesen

Der Server schreibt den Verzugsstempel seit jeher — `AStoredMessage_CarriesADelayStamp`
hält seit D-lang fest, dass jede nachgereichte Nachricht ein `<delay/>` trägt,
mit UTC-Zeit und dem Server als Urheber. **Der Client hat ihn nie gelesen.**
`urn:xmpp:delay` kam in seinem gesamten Quelltext nicht vor, und
`XMPPMessage.Timestamp` war laut eigener Dokumentation „Zeitpunkt des Empfangs
(lokale Uhr)".

Die Folge war eine Lüge mit Uhrzeit: Eine Nachricht von gestern Abend erschien
nach dem Anmelden mit der Uhrzeit von jetzt. **Das ist schlimmer als eine
fehlende Angabe** — es lädt dazu ein, auf eine Frage zu antworten, die sich
längst erledigt hat.

Von allen sieben Punkten der Umfangsliste war das der einzige, bei dem etwas
Falsches angezeigt wurde statt etwas zu fehlen.

`Timestamp` ist jetzt die Zeit, zu der die Nachricht **geschrieben** wurde,
`ReceivedAt` die des Empfangs, `IsDelayed` der Unterschied zwischen beiden.
Gelesen wird die Stanza dort, wo sie noch vorliegt — in der Verbindung; das
`DateTime.Now` im Client, das die Auskunft überschrieb, ist fort.

**Zwei Feinheiten, beide mit eigenem Test:**

- **Nur direkte Kinder.** Ein Carbon (XEP-0280) und eine Weiterleitung
  (XEP-0297) bringen in ihrem `<forwarded/>` den Stempel der *inneren*
  Nachricht mit. Wer die ganze Stanza durchsucht, datiert die äussere auf die
  Zeit der inneren — und liegt genau dann falsch, wenn es darauf ankommt.
- **Nur mit Zonenangabe.** Das kam durch eine überlebende Mutation dazu, und
  sie war die lehrreichste des Tages: `RoundtripKind` gegen `AssumeUniversal`
  liess sich nicht erschlagen. Der Grund war keine schwache Prüfung, sondern
  eine Lücke dahinter — ein Stempel **ohne** Zone verstösst gegen Abschnitt 3,
  liess sich aber lesen und wurde als hiesige Zeit gedeutet. **Die
  schlechteste aller Auslegungen:** Die Nachricht verschiebt sich um genau den
  Zonenunterschied und sieht dabei vollkommen plausibel aus. Jetzt gilt sie wie
  kein Stempel.

Nach dieser Verschärfung ist dieselbe Mutation **gleichwertig statt
überlebend**: Mit erzwungener Zone können sich die beiden Auslegungen nicht
mehr unterscheiden, denn `AssumeUniversal` greift nur, wo keine Zone steht. Ein
Überlebender, dessen Gleichwertigkeit sich beweisen lässt, ist etwas anderes
als einer, der ungeprüft danebensteht.

Fünf Mutationen: vier erschlagen (Stempel gar nicht gelesen, ganze Stanza
durchsucht, unlesbarer Stempel wirft statt zu verneinen, Zonenangabe nicht mehr
verlangt), eine gleichwertig.

Die Konsole zeigt eine nachgereichte Nachricht jetzt mit Datum und dem Vermerk
„(nachgereicht)" — ohne das Datum sähe eine Uhrzeit von gestern aus wie heute.

---

### D60. „Ich meinte: morgen." ✅ — XEP-0308

Die Korrektur ist eine gewöhnliche Nachricht mit eigener `id` und
**vollständigem** Text; das `<replace/>` nennt nur, welche sie ablöst. Das ist
Absicht: Ein Empfänger, der die Erweiterung nicht kennt, zeigt sie als zweite
Nachricht an — unschön, aber vollständig. Wer stattdessen nur den geänderten
Teil schickte, hinterliesse bei ihm eine leere Zeile.

**Die Grenze aus Abschnitt 5 ist die eigentliche Entscheidung.** Berichtigen
lässt sich nur die zuletzt an **denselben Empfänger** geschickte Nachricht.
Deshalb merkt sich der Client die letzte Kennung *je Empfänger* und nicht
insgesamt: Ein einzelner Merkposten wäre nach jedem Themenwechsel falsch — und
zwar so, dass die Berichtigung beim vorigen Gesprächspartner landet. Die
Mutation, die das Merken vom Empfänger löst, fällt an genau diesem Fall.

Und die Korrektur wird selbst zur letzten Nachricht, sodass sich eine
Berichtigung wiederum berichtigen lässt. Kein Sonderfall, sondern der übliche:
Wer sich vertippt, vertippt sich auch in der Berichtigung. Zeigte die zweite
Korrektur weiter auf das Original, hinge die erste beim Empfänger in der Luft.

**Beim Empfangen wird gemeldet, nicht entschieden.** `ReplacesId` und
`IsCorrection` stehen an der Nachricht; was daraus wird, ist Sache der
Oberfläche. Eine Konsole kann Geschriebenes nicht zurücknehmen — sie setzt ein
`✎` an den Absender und zeigt beide Fassungen. Das ist ehrlicher, als die
Korrektur zu verschweigen: Der Leser sieht, dass es eine gab, und welche gilt.

Nebenbei ist die Parameterliste des Nachrichten-Ereignisses verschwunden. Sie
war mit jeder Erweiterung länger geworden — fünf Werte, mit dem Verzugsstempel
acht, mit der Korrektur neun —, und **eine Reihe gleichartiger Zeichenketten,
deren Bedeutung nur an ihrer Stellung hängt, ist eine Verwechslung, die auf
ihre Gelegenheit wartet.** Die Verbindung setzt die `XMPPMessage` jetzt selbst
zusammen; sie ist ohnehin die einzige Stelle, an der die Stanza noch vorliegt.
Genau daran war der Verzugsstempel in D59 vorbeigegangen.

Sechs Mutationen, alle erschlagen: Vermerk nicht gelesen, ganze Stanza
durchsucht, leere `id` als Ziel, `<replace/>` geht nicht mit hinaus, Korrektur
wird nicht zur neuen letzten, Merken hängt nicht am Empfänger.

Angekündigt wird die Erweiterung in disco#info (Abschnitt 4) — ohne die
Ankündigung muss ein Gegenüber annehmen, dass seine Korrektur als zweite
Nachricht erscheint, und schickt dann lieber keine.

---

### D61. Wenn niemand hinsieht ✅ — XEP-0352

Das Protokoll ist an einem Nachmittag gelesen: zwei Nonzas, `<active/>` und
`<inactive/>`, angekündigt in den Features nach der Anmeldung (Abschnitt 4.1),
und **keine Antwort darauf** (Abschnitt 4.2) — eine Bestätigung weckte das
Gerät genau in dem Augenblick, in dem es sich schlafen legt.

Die Arbeit steckt woanders. **Was zurückgehalten werden darf, entscheidet der
Server**; das XEP nennt in Abschnitt 3 nur Beispiele. Meine Leitlinie:
*zurückgehalten wird nur, was später noch wahr ist.*

- **Presence wartet**, und die letzte je Full-JID löst die früheren ab
  („push the latest presence from each contact"). Je Full-JID und nicht je
  Mensch: Zwei Geräte sind zwei Anwesenheiten, und die eine darf die andere
  nicht verdrängen — sonst verschwände Bobs Telefon aus der Liste, weil sein
  Rechner sich abgemeldet hat.
- **Ein Chat State wird fallengelassen**, nicht aufgehoben. Das ist der einzige
  Punkt, an dem etwas verloren geht, und er ist der wichtigste: Ein „schreibt
  gerade" von vorhin ist beim Nachliefern keine verspätete Auskunft mehr,
  sondern eine falsche.
- **Text, `iq`, Fehler und jede Nonza gehen sofort hinaus.** XEP-0352 ist eine
  Sparmassnahme für den Akku und keine Ruhefunktion für den Menschen davor. Ein
  `iq` ist ausserdem eine Frage mit Frist — wer es zurückhält, beantwortet es
  nach Ablauf, und die Antwort käme zu einer Frage, die niemand mehr stellt.
- Eine Kontaktanfrage ist eine Presence und trotzdem keine
  Anwesenheitsmeldung: Sie wartet auf die Entscheidung eines Menschen
  (RFC 6121, Abschnitt 3.1.3) und geht sofort hinaus.

**Zwei Feinheiten, die sich erst beim Bauen zeigen:**

- **Zurückgehaltenes geht vor der Stanza hinaus, die den Puffer leert.** Ohne
  diese Regel überholte Bobs Nachricht seine eigene Presence, und RFC 6120,
  Abschnitt 10.1 verlangt zwischen zwei Entitäten ausdrücklich die
  Reihenfolge. Alice sähe sonst erst „Bob schreibt: bin unterwegs" und danach,
  dass Bob online gegangen ist.
- **Eine Nonza leert den Puffer nicht.** Ein `<r/>` des Servers (XEP-0198)
  fragt nach dem Empfangszähler und trägt keine Reihenfolge; leerte es den
  Puffer, wäre jede Zählnachfrage ein Weckruf durch die Hintertür. Die Zählung
  bleibt dabei stimmig, weil Zurückgehaltenes nicht gesendet und damit auch
  nicht gezählt ist.

**Der Puffer hat eine Obergrenze** (`MaxHeldWhileInactive`, Vorgabe 100). Ein
Client, der sich für inaktiv erklärt und dann nicht mehr wiederkommt, nötigte
dem Server sonst mit einem einzigen `<inactive/>` unbegrenzt Speicher ab. Beim
Überlauf geht der ganze Puffer hinaus, statt etwas wegzuwerfen: Der Client
bekommt dann Verkehr, den er gerade nicht wollte — die freundlichere der beiden
Möglichkeiten.

**Und am Ende der Verbindung bleibt nichts liegen.** Was zurückgehalten wurde,
hat den Client nie erreicht und wäre auch nicht im Puffer der unbestätigten
Stanzas gelandet — eine Wiederaufnahme fände es nicht, und niemand erführe
davon, denn eine nie gesendete Stanza fehlt auch keiner Zählung. Der Abschied
leert den Puffer deshalb zuerst; bei einem aufgehobenen Stream geht er damit
seinen gewohnten Weg.

**Abschnitt 5.2 nimmt einem die Frage nach der Wiederaufnahme ab:** „stream
resumption does not affect the current CSI state, which always defaults to
'active' for new and resumed streams." Der Server übernimmt den Zustand also
bewusst *nicht* — und der Client erklärt sich nach jedem Aufbau erneut für
inaktiv, denn das Gerät liegt in derselben Tasche wie vorher. Ohne diese
Wiederholung wäre jede Störung ein stilles Ende der Sparmassnahme, und niemand
bemerkte es: Es funktioniert ja alles weiter.

Ohne Ankündigung schickt der Client nichts, und ohne eigene Ankündigung
gehorcht der Server nicht. Der zweite Fall ist der gefährlichere: Ein Server,
der schweigt und trotzdem zurückhält, liesse den Client seine Kontakte für
still halten. Vor der Anmeldung gilt es ebenfalls nicht — sonst hätte ein
Unangemeldeter einen Zustand an einer Sitzung, die noch niemandem gehört.

Zu Abschnitt 6 (Security Considerations, „servers MUST NOT reveal the clients
active/inactive state to other entities on the network") war nichts zu tun und
das ist der Punkt: Der Zustand ändert nichts an der Presence und verlässt die
Sitzung nirgends — es gibt kein automatisches „abwesend", das ihn den Kontakten
vorführte.

**21 Mutationen, alle erschlagen** — Kontaktanfrage wartet, Text zählt nicht,
leeres `<body/>` gilt als Text, alle Kinder statt nur der Erweiterungen,
Nachricht ohne Erweiterung verfällt, Ablösung je Mensch statt je Gerät, keine
Ablösung, `iq` zurückgehalten, gar nichts zurückgehalten, Puffer nicht
mitgenommen, Puffer auch von Nonzas geleert, `<active/>` liefert nichts nach,
keine Obergrenze, Chat State aufgehoben statt fallengelassen, Puffer bleibt am
Verbindungsende liegen, Feature nicht angekündigt, Server gehorcht ohne
Ankündigung, Unangemeldeter darf setzen, Client schickt ohne Ankündigung,
Client wiederholt sich nach dem Wiederaufbau nicht, Client merkt sich seinen
Zustand nicht.

In der Konsole: `/csi` zeigt den Zustand, `/csi inaktiv` und `/csi aktiv`
melden ihn.

---

### D62. Fremde Zahlen ✅ — OMEMO, Etappe 1 von 7: die Kryptobausteine

OMEMO ist keine Erweiterung, die man an einem Abend einbaut. XEP-0384 (Fassung
0.9.1, `urn:xmpp:omemo:2`) verlangt X3DH, den Double Ratchet, ein
protobuf-Drahtformat, PEP-Verteilung von Device-Liste und Bundles, einen
Sitzungsspeicher, der einen Neustart übersteht, und eine Vertrauensentscheidung
für den Menschen davor. Das sind sieben Etappen; hier ist die erste, und sie
ist die einzige, die ohne XMPP auskommt.

**Der Unterbau war schon da.** BouncyCastle 2.6.2 hängt über Hermod ohnehin im
Baum — X25519 und Ed25519 gibt es also, ohne eine neue Abhängigkeit zu wählen.
.NET 10 hat X25519 nicht: In `System.Security.Cryptography.dll` kommt die
Zeichenfolge kein einziges Mal vor. Das Paket steht jetzt ausdrücklich in der
`.csproj`, obwohl es transitiv schon da war — wer eine transitive Abhängigkeit
direkt benutzt, verliert sie in dem Augenblick, in dem der Vorbesitzer sie
ablegt.

**Eine Lücke musste ich selbst füllen, und der Weg dorthin gehört
aufgeschrieben.** BouncyCastle gibt sein `ScalarMultBase` für Ed25519 nicht
heraus; öffentlich sind nur `Sign` und `Verify`, und beide leiten den Skalar
aus einem Seed ab. XEdDSA braucht aber einen *gegebenen* Skalar. Der naheliegende
Ausweg — den Nonce über `GeneratePublicKey` aus einem zufälligen Seed erzeugen —
ist eine Falle: Der Skalar wäre dann **geklammert**, also ein Vielfaches von 8
in einem festen Fenster, rund vier Bit vorhersagbar. Genau darauf zielt der
Angriff auf verzerrte Nonces; wenige hundert Signaturen genügen, und der
Identitätsschlüssel fällt. **Ein verzerrter Nonce ist kein Schönheitsfehler,
sondern der übliche Weg, wie solche Schlüssel gestohlen werden.** Also die
Punktarithmetik selbst, mit den vollständigen Formeln aus RFC 8032, Abschnitt
5.1.4 — und mit dem ausdrücklichen Vermerk im Quelltext, dass sie **nicht**
gegen Zeitmessung gehärtet ist. Für einen Client auf dem Gerät seines Benutzers
ist das die richtige Reihenfolge der Sorgen; für einen Server wäre es die
falsche, und es steht dort, damit niemand es später für erledigt hält.

**Geprüft wird gegen fremde Zahlen.** Eine Verschlüsselung prüft sich selbst zu
leicht: Wer entschlüsseln kann, was er selbst verschlüsselt hat, hat gezeigt,
dass er zweimal denselben Fehler macht. Beweiskraft haben nur veröffentlichte
Vektoren — RFC 7748 (Abschnitte 5.2 und 6.1), RFC 8032 (Abschnitt 7.1, drei
Vektoren, über den Umweg der Ed25519-eigenen Skalarbildung), RFC 5869, RFC 4231,
NIST SP 800-38A. Dazu ein Punkt, den beide Kurven benennen: Der
X25519-Basispunkt `u = 9` muss nach der Umrechnung der Ed25519-Basispunkt sein.

**Der erste Lauf hat zwei Fehler gefunden, und sie sind verschieden
gefährlich:**

- `Aes.Create().DecryptCbc(…)` entschlüsselte mit einem **zufälligen**
  Schlüssel — ich hatte ihn nur beim Verschlüsseln ans Objekt gehängt. Das
  scheitert immer und fällt sofort auf.
- In XEdDSA wird mit `-k` weitergerechnet, wenn `kB` das Vorzeichenbit trägt.
  Meine Negation lief über die Gruppenordnung hinaus und ergab eine negative
  Zahl — und das trifft **jeden zweiten Schlüssel**. Ein Test mit einem
  erzeugten Schlüssel wäre in jedem zweiten Lauf grün gewesen. Dagegen steht
  jetzt einer, der 32 Schlüssel durchgeht *und hinterher nachzählt, dass beide
  Vorzeichen vorkamen* — sonst prüft er den halben Weg und sagt es nicht.

**26 Mutationen, 23 erschlagen, drei beweisbar gleichwertig:**

- Die Längenprüfung der Signatur — ohne sie wirft der fremde Prüfer, und die
  Ausnahme wird ohnehin zu „ungültig".
- Der Schleifenanfang bei Bit 254 statt 253 — der Skalar wird vorher modulo der
  Gruppenordnung reduziert, die oberen Bits sind danach immer null.
- Das Salz aus 32 Nullbyte gegen 16 — HMAC füllt jeden Schlüssel unterhalb der
  Blocklänge mit Nullen auf, beide ergeben denselben Wert. Die 32 stehen
  trotzdem da, weil die Spezifikation sie so nennt.

**Eine überlebende Mutation war ein echtes Loch und hat einen Test erzwungen:**
Der Info-String der Ableitung liess sich auf `""` setzen, ohne dass etwas
scheiterte — alle Tests prüften die Struktur der 80 Byte, keiner ihren Wert.
Der Fehler wäre in diesem Haus nie aufgefallen: **Zwei Clients mit demselben
falschen String verstehen sich bestens.** Erst eine fremde Gegenstelle bekäme
Buchstabensalat, und die gibt es hier nicht. Jetzt rechnet ein zweites HKDF —
das von BouncyCastle statt das der BCL — dieselben 80 Byte nach, mit den
Parametern aus Abschnitt 4.4 buchstäblich hingeschrieben.

Das ist zugleich die Grenze dieser Etappe und der ganzen Reihe, und sie gehört
vorweg gesagt: **Gegen einen echten OMEMO-Client ist hier nichts geprüft.**
Prosody und ejabberd tragen OMEMO nur, sie sprechen es nicht; Conversations,
Dino und Gajim gibt es im Testaufbau nicht. Was bleibt, sind veröffentlichte
Vektoren und buchstäblich hingeschriebene Vorschriften — beides prüft die
Übereinstimmung mit dem Text, nicht mit der Wirklichkeit.

---

### D63. Vier Handschläge ✅ — OMEMO, Etappe 2 von 7: X3DH

Eine Sitzung beginnt, ohne dass beide gleichzeitig da sind: Bob ist offline,
Alice schreibt ihm trotzdem verschlüsselt. Das geht nur, weil sein Server seine
Schlüssel vorrätig hält — **und damit ist der Server auch der naheliegende
Angreifer.** Genau dagegen steht die Signatur über den Signed PreKey, und
deshalb bricht ein Bundle mit falscher Signatur hier ab, statt eine Warnung zu
melden: Eine Sitzung darauf wäre schlimmer als keine, denn sie sähe aus wie
eine verschlüsselte.

**Die vier Diffie-Hellman beantworten vier verschiedene Fragen** — wer schreibt
(DH1), wer liest (DH2), ist es frisch (DH3), und ist diese erste Nachricht von
jeder anderen verschieden (DH4). Der vierte entfällt, wenn der PreKey-Vorrat
leer ist; das ist ausdrücklich vorgesehen und kostet genau diese eine
Eigenschaft. Eine Verweigerung wäre die schlechtere Antwort — sie machte aus
einem leeren Vorrat einen Ausfall der Erreichbarkeit.

**Der Fehler, den ich beim Schreiben gemacht habe, ist der, vor dem diese
Erweiterung am lautesten warnt.** XEP-0384 überträgt den IdentityKey *immer* in
Ed25519-Form (Abschnitt 5.3.2), der Diffie-Hellman rechnet aber in
Montgomery-Form. Ich habe die eine Fassung an die Methode für die andere
gegeben — und bekam keine Fehlermeldung: Beides sind 32 gültige Byte, die
Umrechnung läuft durch, und heraus kommt ein Schlüssel, zu dem keine Signatur
passt. Jetzt heissen die beiden Wege `Verify` und `VerifyEdwards`. **Ein
`Boolean istEdwards` wäre an der Aufrufstelle unsichtbar gewesen, und die
Aufrufstelle ist der Ort, an dem man sich irrt.**

**Zum dritten Mal dasselbe Muster bei den Mutationen, und es ist das Muster
dieses ganzen Vorhabens:** Der `0xFF`-Vorspann, der Info-String und die
Reihenfolge der beiden IdentityKeys in der Beigabe liessen sich alle drei
ändern, ohne dass ein Test etwas sagte. Der Grund ist immer derselbe — **beide
Seiten rechnen mit derselben Funktion und kommen weiterhin überein.** Ein Test,
der prüft „beide bekommen dasselbe heraus", kann so etwas grundsätzlich nicht
finden. Der Schaden träte erst gegenüber einem fremden Client auf, und den gibt
es hier nicht.

Dagegen hilft nur eines: **die Vorschrift ein zweites Mal wörtlich
hinschreiben.** Die Ableitung wird jetzt mit einem zweiten HKDF nachgerechnet,
und die Beigabe wird nicht auf „beide gleich" geprüft, sondern darauf, welche
Hälfte wem gehört. Wer den Wert im Quelltext ändert, muss ihn zweimal ändern —
und sieht dabei, dass er die Spezifikation verlässt.

19 Mutationen, alle erschlagen: Signatur ungeprüft, DH1 und DH2 mit
vertauschten Schlüsseln, Vorspann weg, Info-String weg, Beigabe verdreht
(zweimal), gewechselter Signed PreKey übergangen, verbrauchter PreKey
angenommen, PreKey beim Entnehmen nicht gelöscht, Kennungen wiederverwendet,
gewechselter Schlüssel nicht neu unterschrieben, IdentityKey in falscher Form
veröffentlicht, Signatur gegen die falsche Form geprüft.

**Eine ungeprüfte Annahme steht ausdrücklich im Quelltext:** Der Signed PreKey
wird in Montgomery-Form unterschrieben. Abschnitt 5.3.2 sagt nur „the signed
PreKey signature" und lässt offen, welche Kodierung gemeint ist. Stimmt die
Lesart nicht, scheitert die Prüfung gegen fremde Clients an dieser einen Zeile —
und es gibt hier keine Gegenstelle, an der sich das entscheiden liesse.

---

### D64. Zwei Ratschen, sieben Überlebende ✅ — OMEMO, Etappe 3 von 7

Das Herzstück. Die symmetrische Ratsche läuft mit jeder Nachricht und gibt
**Forward Secrecy** — wer den heutigen Zustand stiehlt, kann gestern nicht mehr
lesen. Die Diffie-Hellman-Ratsche läuft bei jedem Richtungswechsel und gibt
**Break-in Recovery** — wer den Zustand gestohlen hat, verliert ihn wieder,
sobald die beiden einmal in beide Richtungen geschrieben haben.

**Fehler sind hier still, und deshalb sehen die Tests anders aus.** Eine
Ratsche, die nicht weiterläuft, verschlüsselt weiterhin einwandfrei — sie tut
es nur immer wieder mit demselben Schlüssel. Ein Test, der „hin und zurück
ergibt den Klartext" prüft, bestünde auch dann. Geprüft wird deshalb
zusätzlich, dass Geheimtexte sich *unterscheiden*, dass Schlüssel
*verschwinden* und dass eine Nachricht an falscher Stelle *abgewiesen* wird.

**Und trotzdem überlebten sieben von zwanzig Mutationen den ersten Lauf.**
Das ist der wichtigste Befund dieser Reihe, denn drei davon waren nicht bloss
Interop-Fragen, sondern Aufhebungen der Sicherheit:

- **`mk` und `ck` aus derselben Konstante.** Dann ist der
  Nachrichtenschlüssel zugleich der nächste Kettenschlüssel: Wer eine einzige
  Nachricht mitliest, rechnet die ganze weitere Kette aus. **Aus Forward
  Secrecy wird ihr genaues Gegenteil.**
- **Wurzel und Kette aus derselben Hälfte** der 64 abgeleiteten Byte. Dann ist
  der Wurzelschlüssel bekannt, sobald ein Kettenschlüssel es ist.
- **Salz und Eingabematerial der Wurzelkette vertauscht.**

Der Grund ist immer derselbe und inzwischen der rote Faden dieses Vorhabens:
**beide Seiten rechnen mit derselben Funktion und kommen weiterhin überein.**
Bei D62 und D63 kostete das nur die Verständigung mit fremden Clients — hier
kostet es die Eigenschaft, um derentwillen es das ganze Verfahren gibt.

Das Gegenmittel ist dasselbe wie zweimal zuvor: **die Vorschrift ein zweites
Mal wörtlich hinschreiben.** Dafür sind `DeriveRootChain`, `AdvanceChain` und
`Material` jetzt einzeln greifbar und werden gegen ein zweites HKDF gehalten.

**Zwei eigene Testfehler kamen dabei ans Licht, und beide sind lehrreicher als
der Code:**

- `TheChainConstants_AreDistinct` **prüfte gar nichts.** Er rechnete
  `HMAC(ck,0x01)` und `HMAC(ck,0x02)` im Test selbst nach und stellte fest,
  dass sie sich unterscheiden — über den Quelltext sagte er kein Wort. Er
  hätte auch bestanden, wenn die Implementierung beide Male `0x01` genommen
  hätte. **Ein Test, der die Vorschrift nachrechnet statt den Code zu fragen,
  ist eine Verdopplung der Vorschrift und keine Prüfung.**
- `ATamperedMessage_IsRefused` **vergiftete sich selbst.** Drei Fälle
  nacheinander auf demselben Ratchet-Paar — aber eine *abgewiesene* Nachricht
  verändert den Zustand trotzdem: Es wurde vorgespult, ein Schlüssel ist
  verbraucht. Der dritte Fall, die fremde Beigabe, hätte die HMAC-Mutation
  erschlagen, warf aber aus einem ganz anderen Grund. Jeder Fall bekommt jetzt
  ein frisches Paar.

**Die Obergrenze der übersprungenen Schlüssel hat ihren eigenen Beweis
geliefert.** Ohne sie stürzte der Testhost ab — nicht ein Test schlug fehl,
der ganze Prozess starb an einer einzigen Nachricht mit `n = 4000000000` und
hinterliess ein **32 GB grosses Absturzabbild**. Genau das ist der Angriff:
Ein Fremder braucht weder Schlüssel noch Zugang, nur diese eine Zahl. Der
Lauf meldete dabei zunächst „Bestanden, 4 von 13" — und das ist die Falle aus
D54 in Reinform: **Ein Lauf, der vier von dreizehn Tests meldet, ist kein
bestandener Lauf.** Nachgesehen, wo er starb, statt die Zusammenfassung zu
glauben.

Nebenbei hat sich die Kodierung des Nachrichtenkopfes nach vorn gezogen,
obwohl sie zu Etappe 4 gehört: Die Beigabe der Verschlüsselung ist
`ad ‖ OMEMOMessage.proto(header)` (Abschnitt 4.3). Mit einer provisorischen
Kodierung wäre der Ratchet gegen etwas geprüft worden, das später ersetzt wird.
Protocol Buffers von Hand, und der Grund ist nicht Sparsamkeit: Diese Bytes
müssen **bitgenau reproduzierbar** sein, und eine Bibliothek, die Felder
umsortiert oder Vorgabewerte weglässt, wäre hier keine Hilfe, sondern eine
Fehlerquelle, die niemand sieht.

20 Mutationen, alle erschlagen — sechs davon erst, nachdem die Tests
nachgebessert waren, und eine dadurch, dass sie den Prozess umbringt.

---

### D65. Drei Byte, die niemand gesehen hätte ✅ — OMEMO, Etappe 4 von 7

Das Drahtformat: die drei Protobuf-Nachrichten, das
`<encrypted/>`-Element und die SCE-Hülle aus XEP-0420.

**Der wichtigste Fund kam diesmal beim Lesen, nicht durch eine Mutation.**
Abschnitt 4.3 sagt, der HMAC laufe über `ad ‖ OMEMOMessage.proto` — „after
ciphertext is added to the proto". In D64 hing der Geheimtext **roh hinter dem
Kopf**; verlangt ist er als Feld 4 *innerhalb* der kodierten Nachricht. Der
Unterschied sind drei Byte, Feldkennung `22` und Längenangabe, und **beide
Seiten dieses Hauses hätten ihn nie bemerkt.** Gegen einen fremden Client hätte
keine einzige Prüfsumme gestimmt.

Damit ist es zum vierten Mal dieselbe Familie: D62 der Info-String, D63 die
Beigabe, D64 die Wurzelkette, jetzt die Einbettung. **Alle vier haben
gemeinsam, dass die eigenen Tests sie nicht finden konnten** — nicht weil sie
schlecht waren, sondern weil ein Test zwischen „richtig" und „auf beiden
Seiten gleich falsch" grundsätzlich nicht unterscheidet, solange beide Seiten
derselbe Code sind.

**Drei Entscheidungen im Format:**

- **Der HMAC steht ausserhalb der Nachricht.** Stünde er darin, prüfte er sich
  selbst mit; deshalb die Hülle `OMEMOAuthenticatedMessage` — innen die
  Nachricht, aussen ihre Beglaubigung.
- **`kex='false'` wird nicht geschrieben.** Abschnitt 4.5 gibt dem Attribut
  diesen Vorgabewert, und ein ausgeschriebener Vorgabewert reist bei jeder
  Nachricht an jedes Gerät mit, ohne je etwas zu bedeuten.
- **Der Schlüssel wird über JID *und* Gerätekennung gesucht.** Die Kennung ist
  eine Zufallszahl je Gerät; zwei Konten können dieselbe tragen. Wer nur nach
  ihr suchte, nähme unter Umständen den Eintrag eines fremden Kontos und
  scheiterte an einer Entschlüsselung, deren Grund er nicht sieht.

Bei der SCE-Hülle ist die Begründung wichtiger als der Code. **Verschlüsselt
wird nicht der Text, sondern eine ganze Stanza-Hülle.** Wer nur den `<body/>`
verschlüsselt, lässt Chat States, Empfangsbestätigungen und Korrekturvermerke
im Klartext stehen — der Inhalt wäre geschützt, das Gespräch nicht. Der
Absender steht **innerhalb** der Hülle, weil er aussen von jedem änderbar ist;
ohne diesen Abgleich liesse sich ein Geheimtext abfangen und unter fremdem
Namen weiterreichen. Und das `<rpad/>` ist keine Zierde: Ohne es verrät die
Länge des Geheimtextes die Länge der Nachricht, und bei „ja" und „nein" ist das
der ganze Inhalt.

19 Mutationen, alle erschlagen — **die beiden Überlebenden des ersten Laufs
waren wieder Fehler in meinen Tests**, und beide von der stillen Sorte:

- Die Prüfung der MAC-Länge liess sich entfernen, ohne dass etwas geschah: Mein
  Testfall packte zufällige Bytes als innere Nachricht ein, und die scheiterten
  schon beim Protobuf-Lesen. **Der Test bestand also aus dem falschen Grund.**
  Jetzt steht dort eine sonst einwandfreie Nachricht — und eine Gegenprobe, die
  fehlschlägt, wenn der Fall gar nicht mehr durchkommt.
- Die Suche nach `kex='false'` im ausgegebenen XML konnte **nie** zutreffen:
  `XElement.ToString` schreibt Attribute mit doppelten Anführungszeichen. Der
  Test bestand immer, auch als die Mutation den Vorgabewert ausschrieb. Gefragt
  wird jetzt das Attribut selbst.

Beide sind dieselbe Lehre wie in D64: **Ein Test, der eine Zeichenfolge im
Ausgabetext sucht oder einen Fehlerfall über einen anderen Fehler herstellt,
prüft nicht, was sein Name behauptet.** Gefunden hat es die Mutation, nicht das
Lesen.

---

### D66. Der Server antwortet für einen Abwesenden ✅ — OMEMO, Etappe 5 von 7

Die erste Etappe seit vier, die wieder XMPP prüft statt Kryptographie — und
damit die erste, bei der ein Durchlauf mehr aussagt als eine nachgerechnete
Vorschrift.

**Dafür hat der Testserver PEP bekommen** (XEP-0163, als Teilmenge:
veröffentlichen, abrufen, benachrichtigen). Ohne das wäre O5 gar nicht prüfbar
gewesen: Prosody und ejabberd erreichen wir nur über S2S, nie als eigenen
Heimatserver, und unser Client spricht ausschliesslich WebSocket. Was fehlt,
steht im Quelltext — keine Knotenkonfiguration, keine Zugriffsmodelle, keine
gefilterten Benachrichtigungen über XEP-0115.

**Die wichtigste Entscheidung: PEP wird vor der Weiterleitung behandelt.** Eine
Anfrage an `bob@domain` sieht aus wie eine Anfrage an Bob und ginge sonst an
seine Sitzung — dann wäre ein Bundle nur abrufbar, solange Bob online ist, und
genau dafür gibt es PEP nicht. **Der Server antwortet stellvertretend für einen
Menschen, der gerade nicht da ist**, und das ist die ganze Zusage dieser
Etappe.

**Ein alter Fehler kam dabei ans Licht, und er lag nicht im neuen Code.**
PubSub-Benachrichtigungen wurden ausschliesslich in `ProcessIq` behandelt. In
der Praxis kommen sie als `<message type='headline'/>` — die Hälfte gab es
nicht, obwohl der Kommentar daneben seit jeher „kann als message oder iq
kommen" behauptete. Aufgefallen ist es erst, als mit OMEMO zum ersten Mal
jemand auf eine Benachrichtigung *angewiesen* war; dieselbe halb verdrahtete
Ecke wie in D38.

21 Mutationen, alle erschlagen — **sechs überlebten den ersten Lauf, und fünf
davon waren echte Lücken**, keine Gleichwertigkeiten:

- **Ein leeres `<spk/>` kam durch.** Leeres Base64 ist gültiges Base64 und
  ergibt ein Feld von null Byte; daraus wäre weiter unten eine Ausnahme aus der
  Kurvenarithmetik geworden, mit einer Meldung, die niemandem sagt, dass ein
  Bundle unbrauchbar war. Jetzt werden die Längen geprüft, dort wo sie zählen.
- **Die Eintragskennung `current` liess sich umbenennen** — zum fünften Mal die
  Familie „beide Seiten benutzen dieselbe Konstante und finden sich weiterhin".
- **Die Eintragskennung beim Abrufen liess sich übergehen.** Mit einem
  veröffentlichten Gerät dasselbe Ergebnis; mit zweien bekommt der Absender das
  **falsche Bundle** und verschlüsselt für ein Telefon, das gar nicht mitliest.
- **Eine Ablehnung des Servers galt als Erfolg** — genau der Rückgabewert,
  dessentwegen diese Methoden überhaupt einen haben.
- Ein leerer Knoten wurde als leeres Ergebnis statt als `<item-not-found/>`
  beantwortet.

**Und ein Testfehler, der eine eigene Lehre trägt:** Mein Test für „eine fremde
Geräteliste löst keinen Wiedereintrag aus" prüfte, ob Alices Liste unverändert
blieb. Das war wertlos — **der Server weist fremde Knoten ohnehin ab**, also
blieb sie auch dann sauber, wenn Bobs Client es versuchte. Gefragt werden muss,
ob der Prüfling etwas geschickt hat, nicht ob sein Nachbar es abgewehrt hat.
**Ein Test, der die Wirkung an der falschen Stelle misst, prüft die falsche
Sicherung.**

Nebenbei hat eine Nullable-Warnung des Kompilers einen vertauschten Parameter
gefangen, bevor irgendein Test lief: ein JID an der Stelle der Fehlerbedingung.

---

### D67. Ein Lauf gegen eine rote Grundlinie ✅ — OMEMO, Etappe 6 von 7

Der Sitzungsspeicher. **Ohne ihn ist jede Wiederverbindung ein
Vertrauensbruch:** Ein neuer IdentityKey bedeutet einen neuen Fingerabdruck,
und jeder Vergleich, den irgendein Mensch je angestellt hat, ist damit wertlos.
Ein Client, der bei jedem Start neue Schlüssel erzeugt, sieht für seine
Kontakte aus wie ein Angreifer — jedes Mal.

Die Prüfung ist bei jedem Test dieselbe: **neu starten und weitermachen.** Ein
Speicher, der ablegt und wieder herausgibt, ist noch keiner — er muss so viel
ablegen, dass die Gegenstelle vom Neustart nichts merkt. Geprüft wird deshalb
nicht, ob der Zustand gleich aussieht, sondern ob das Gespräch weitergeht.

**Der abgelöste Signed PreKey wird jetzt aufgehoben** — genau einer. Das stand
seit D63 aus, ausdrücklich aufgeschoben, weil es ohne Speicher eine Zusage
gewesen wäre, die niemand hält. Jeder weitere aufgehobene Schlüssel nähme ein
Stück von dem zurück, wofür es den Wechsel gibt.

**Die Signatur wird mitgenommen und nicht neu gerechnet.** XEdDSA mischt Zufall
in jede — die neue sähe anders aus als die veröffentlichte, und das Bundle im
PEP-Knoten wäre mit dem Gerät uneins.

**Ein geänderter IdentityKey wird gemeldet und nie stillschweigend
übernommen.** Dafür gibt es zwei Erklärungen — neu aufgesetztes Gerät oder
jemand dazwischen — und von aussen sind sie nicht zu unterscheiden. Der alte
Vermerk bleibt samt Vertrauensentscheidung stehen; wer ihn überschriebe, machte
aus einer bestätigten Identität eine unbestätigte, und die Warnung wäre nach
dem ersten Ansehen fort.

**Eine unlesbare Datei wirft, statt frisch zu starten.** Der bequeme Weg wäre
hier der gefährliche: Aus einem behebbaren Lesefehler würde ein stiller Wechsel
des eigenen Fingerabdrucks, und die alte Datei wäre beim ersten Ablegen
überschrieben.

## Der eigentliche Fund: ein Mutationslauf, der nichts gemessen hat

**Der erste O6-Mutationslauf meldete zwanzig von zwanzig erschlagen — und war
wertlos.** Die Änderung am Signed PreKey hatte einen bestehenden X3DH-Test
gebrochen, der genau das Gegenteil festhielt („jeder ausser dem aktuellen wird
abgewiesen"). Dieser Test lief im Mutationsfilter mit. **Damit meldete jeder
Lauf „Fehler", ob die Mutation nun etwas erschlug oder nicht.**

Aufgefallen ist es nur, weil eine einzelne Mutation mir zu bequem tot war — die
Nebendatei beim Schreiben. Einzeln laufen lassen: sie überlebt. Und drei Läufe
der *unveränderten* Grundlinie zeigten dann dreimal denselben Fehlschlag.

Das ist die Falle aus D54 in neuer Gestalt. Damals mass ein grüner Lauf nichts,
weil Tests sich selbst übersprangen; hier mass ein roter Lauf nichts, weil er
schon vorher rot war. **Ein Mutationslauf ohne grüne Grundlinie kann nicht
zwischen „meine Mutation wurde gefunden" und „hier war schon vorher etwas
kaputt" unterscheiden.** Die Grundlinie gehört vor jeden Batch geprüft, nicht
angenommen.

Gegen die grüne Grundlinie neu gemessen: **20 Mutationen, 19 erschlagen.** Zwei
der drei Überlebenden waren echte Lücken und haben je einen Test erzwungen —
der abgelöste Signed PreKey überlebte den Neustart nicht, und eine zweimal
abgelegte Sitzung wurde danebengelegt statt ersetzt. Der zweite Fall wäre der
schlimmste Schaden gewesen, den dieser Speicher anrichten kann: Nach einem
Neustart stünde die Ratsche auf einem alten Stand, und alles seitdem wäre für
beide Seiten unlesbar, ohne erkennbaren Grund.

**Der eine echte Überlebende, benannt statt weggeredet:** Das Schreiben über
eine Nebendatei lässt sich durch ein direktes Schreiben ersetzen, ohne dass ein
Test etwas sagt. Der Unterschied zeigt sich nur bei einem Absturz **mitten im
Schreiben**, und den stellt diese Sammlung nicht her. Er ist damit nicht
gleichwertig, sondern ungeprüft — und das ist ein Unterschied, der hier
aufgeschrieben gehört.

**Und eine Sache steht ausdrücklich da, statt durch ein beruhigendes Verfahren
ersetzt zu werden:** Die Datei ist nicht verschlüsselt. Sie enthält den
geheimen IdentityKey, alle PreKeys und jeden Kettenschlüssel; wer sie liest,
liest die Gespräche mit. Eine Verschlüsselung mit einem Schlüssel, der
danebenläge, wäre keine — und einen, den ein Mensch eingibt, gibt es in dieser
Anwendung nicht. Ein Test hält es fest, damit wer es ändert, die Bemerkung
mitändern muss.

---

### D68. Die erste verschlüsselte Nachricht ✅ — OMEMO, Etappe 7 von 7

Alles zusammengeführt: Alice schaltet ein, schreibt, Bob liest. Dazwischen
liegen Schlüsselerzeugung, PEP-Veröffentlichung, Bundle-Abruf, X3DH, Ratchet,
Protobuf, SCE und der Speicher — und der Test fasst keines davon einzeln an.

**Der Test ist erst durch das etwas wert, was er ausschliesst:** Der Klartext
darf in keiner Stanza vorkommen, die der Server gesehen hat. Dazu eine
Gegenprobe, dass überhaupt eine OMEMO-Stanza über die Leitung ging — ohne sie
bestünde er auch dann, wenn gar nichts gesendet würde.

**Drei Entscheidungen beim Verdrahten:**

- **Ein Gerät ohne abrufbares Bundle wird übersprungen und genannt.** Nicht zu
  senden machte einen Menschen durch ein einziges kaputtes Gerät unerreichbar.
  Unverschlüsselt zu senden wäre die schlimmste der drei Antworten: Der
  Absender glaubt dann, verschlüsselt zu haben — und wer ein Bundle
  unerreichbar macht, bekommt den Klartext.
- **Ohne eingeschaltetes OMEMO wird geworfen.** Eine Ausnahme ist laut, eine
  unverschlüsselt gesendete Nachricht ist es nicht.
- **Blind Trust Before Verification als Vorgabe**, mit Begründung: Ein
  Verfahren, das vor der ersten Nachricht einen Fingerabdruckvergleich
  verlangt, wird nicht benutzt — und unbenutzte Verschlüsselung schützt
  niemanden.

## Der schwächste Mutationslauf der Reihe

**Acht von vierzehn überlebten den ersten Durchgang** — mit Abstand das
schlechteste Ergebnis dieser sieben Etappen. Der Grund ist lehrreich: Die
Ende-zu-Ende-Tests sind **breit, aber stumpf**. Sie prüfen, dass es
funktioniert, nicht warum. Ein Gespräch zwischen zwei Clients läuft auch dann
durch, wenn die halbe Sorgfalt fehlt.

Sechs der acht waren echte Lücken, und jede hat einen Test erzwungen:

- **Zwei Nachrichten hintereinander ohne Antwort dazwischen.** Im
  Wechselgespräch fällt ein fehlendes Ablegen der Sitzung nicht auf — das
  Entschlüsseln der Antwort legt sie ohnehin ab. Erst zwei Nachrichten in Folge
  zeigen, ob das *Senden* seinen Fortschritt behält.
- **Das eigene zweite Gerät liest mit, das sendende nicht.** Beides hängt an
  derselben Zeile, und die Mutationen sind in beide Richtungen durchgekommen.
- **Der Absender steht in der Hülle** — und wird abgeglichen. Solange die
  Auskunft nur mitgeführt und nicht bis zum Aufrufer gereicht wurde, war die
  Prüfung nicht zu belegen.
- Verbrauchter PreKey sofort im Speicher, geänderter IdentityKey stoppt die
  Nachricht, und das Einschalten ergänzt die Geräteliste statt sie zu
  überschreiben.

**Zwei Funde, die kein Mutationslauf hervorgebracht hat, sondern die neuen
Tests selbst:**

- **Über Carbons eintreffende OMEMO-Nachrichten wurden nicht entschlüsselt.**
  Genau so sieht ein zweites eigenes Gerät, was das erste geschrieben hat — der
  Schlüsseleintrag war da, die Nachricht kam an, und niemand sah sie an, weil
  sie im `<forwarded/>` steckt. Dieselbe Familie wie „nur direkte Kinder" aus
  D59, D60 und D65, nur andersherum: Dort durfte man **nicht** hineinschauen,
  hier **muss** man es.
- Mein eigener Test griff die falsche Stanza: Die erste mit `urn:xmpp:omemo:2`
  ist eine PEP-Veröffentlichung und keine Nachricht.

**Ein Test musste einen Umweg nehmen, und der Grund gehört aufgeschrieben.**
„Das Einschalten ergänzt die Geräteliste" lässt sich mit zwei echten Clients
nicht prüfen: Verdrängt das zweite Gerät das erste, bemerkt das erste die
PEP-Benachrichtigung und trägt sich sofort wieder ein (D66) — der Endzustand
stimmt wieder, und der Test sieht nichts. Jetzt steht dort ein Eintrag für ein
Gerät, **das es gar nicht gibt**: Es kann sich nicht wehren, und damit bleibt
sichtbar, was das Einschalten tut.

**Der letzte Überlebende verlangte, den Angriff wirklich zu bauen.** Alice
schreibt an Bob und Mallory zugleich; Mallory reicht dieselbe
`<encrypted/>`-Stanza unverändert an Bob weiter, unter ihrem eigenen Namen.
Bobs Eintrag ist unangetastet, der Ratchet-Schritt geht auf, die Prüfsumme
stimmt — **alles kryptographisch einwandfrei**. Nur steht innen „von Alice" und
aussen „von Mallory". Genau dafür gibt es die Beigabe aus XEP-0420, und erst
dieser Test belegt sie.

14 Mutationen, alle erschlagen — sechs davon erst nach dem Nachschärfen.

**Was diese Reihe nicht kann, steht jetzt im README:** Gegen keinen fremden
OMEMO-Client geprüft; der Sitzungsspeicher unverschlüsselt; die Punktarithmetik
nicht gegen Zeitmessung gehärtet; kein MUC und damit keine
Gruppenverschlüsselung; kein Zeitplan für den Wechsel des Signed PreKey.

---

### D69. Eine Gegenstelle, die niemand hier geschrieben hat ✅ — OMEMO gegen die Referenz

Sieben Etappen lang stand dieselbe Grenze im README: **gegen keinen fremden
Client geprüft.** Und siebenmal war der Befund derselbe — der Info-String
(D62), die Beigabe (D63), die Wurzelkette (D64), die Einbettung des
Geheimtexts (D65), die Eintragskennung (D66). **Jedes Mal hätten sich zwei
Clients dieses Hauses bestens verstanden und kein einziger fremder.**

Der Grund ist keine Nachlässigkeit, sondern eine Eigenschaft der Anordnung:
**Sind beide Seiten derselbe Code, kommen sie auch dann überein, wenn beide
gleich falsch rechnen.** Ein Test kann das grundsätzlich nicht unterscheiden.

Jetzt gibt es die Gegenstelle: **python-omemo (Syndace)**, die
Referenzimplementierung für `urn:xmpp:omemo:2` — dieselbe Fassung, die wir
sprechen. Und zwar in beide Richtungen:

- **Sie nimmt unser Bundle an.** Dabei prüft sie unsere Signatur über den
  Signed PreKey mit ihrer eigenen Vorstellung davon, worüber sie geht. **Damit
  ist die ungeprüfte Annahme aus D63 entschieden** — in Montgomery-Form
  unterschrieben, und die Lesart stimmt.
- **Wir lesen, was sie schreibt.** In einem Zug geprüft: Bundle-Kodierung,
  Reihenfolge der vier Diffie-Hellman, Info-String von X3DH, `0xFF`-Vorspann,
  Beigabe aus beiden IdentityKeys, Ratchet-Anfang, Info-Strings von
  Wurzelkette und Nachrichtenschlüssel, die Konstanten `0x01`/`0x02`,
  Protobuf-Feldnummern, Einbettung des Geheimtexts, Kürzung des HMAC,
  Ableitung der Nutzlast.
- **Sie liest, was wir schreiben.** Die Richtung, die darüber entscheidet, ob
  uns jemand lesen kann — und die man am ehesten vergisst, weil ihr Ausbleiben
  wie Schweigen aussieht: Wer nie eine Antwort bekommt, weiss nicht, ob niemand
  schreiben wollte oder niemand lesen konnte.

**Jeder einzelne Punkt dieser Liste war zuvor eine überlebende Mutation oder
ein Fund beim Lesen.** Drei Tests hätten alle fünf gefunden.

## Ohne etwas am System zu verändern

`sudo` verlangt ein Passwort, und das gebe ich nicht ein. Also anders: Wheels
sind Zip-Dateien. Elf Pakete geholt und entpackt, `PYTHONPATH` davor — **kein
pip, kein venv, kein sudo.** Für einen Testaufbau ist das sogar besser als eine
Installation: reproduzierbar, und es bleibt nichts zurück. Das Skript liegt bei
(`Orakel/hole_orakel.py`).

Zwei Stolpersteine unterwegs, beide festgehalten: **`cffi` gehört dazu**, auch
wenn es nicht danach aussieht — ohne es findet XEdDSA seine native Bibliothek
nicht und fällt auf eine Variante zurück, die einen Browser erwartet. Und
**pydantic pinnt `pydantic-core` exakt**; wer von jedem Paket das neueste
nimmt, bekommt zwei, die nicht zueinander passen. Das ist die Arbeit, die pip
sonst macht.

Die Tests **überspringen sich selbst**, wenn das Orakel nicht da ist — wie die
gegen Prosody und ejabberd. Ein Lauf ohne WSL soll nicht rot sein, nur weniger
aussagen.

## Was auch jetzt nicht geprüft ist

Und das gehört genauso deutlich hin wie das Ergebnis: Die **SCE-Hülle** bleibt
aussen vor — python-omemo überlässt sie der Anwendung, und eine Hülle, die ich
im Orakel selbst baute, wäre keine fremde Prüfung, sondern dieselbe Annahme
zweimal. Ebenso wenig geprüft: das `<encrypted/>`-Element, die PEP-Knoten, ein
Gespräch über mehrere Nachrichten — und ein echter Client über eine echte
Verbindung erst recht nicht.

---

### D70. Eine Zusage, die etwas bewirkt ✅ — der Server lernt Abonnements

Der Anlass ist die Frage nach der ausgehenden Korrelation (Punkt unter
„Optional", seit D38). Bevor ein Client lernen kann, Antworten auszuwerten,
muss es Antworten geben, die etwas sagen: **Dieser Server sagte auf jedes
`subscribe` `<service-unavailable/>`** — er kannte die Anfrage nicht. Wer nur
Absagen kennt, kann nicht zeigen, dass er eine Zusage richtig liest.

Also erst der Server. XEP-0060, Abschnitte 6.1 und 6.2: `<subscribe/>` und
`<unsubscribe/>` mit `subid`, den drei Ablehnungen des XEP —
`<item-not-found/>` für einen Knoten, den es nicht gibt, `<invalid-jid/>`, wenn
jemand einen anderen anmelden will, `<not-subscribed/>` und `<invalid-subid/>`
beim Abbestellen.

**Und mit Wirkung, nicht bloss mit Antwort.** Ein Abonnement, das nirgends
wirkt, wäre genau die Zusage ohne Deckung, für die in D57 ein nie ausgelöstes
Ereignis gestrichen wurde. Bisher bekam eine PEP-Benachrichtigung, wer ohnehin
Presence bekam — damit war „abonnieren" nur ein anderes Wort für „im Roster
stehen", und für einen fremden Knoten gab es überhaupt keinen Weg. Jetzt gehen
die Meldungen an **eine** Liste aus beiden Quellen; wer über beide in Frage
kommt, bekommt sie trotzdem einmal.

Die schärfste der neuen Prüfungen ist die auf den `jid`, und zwar in beide
Richtungen: Ein fremdes Abonnement **anzulegen** ist lästig — jemand bekäme
Meldungen, die er nie bestellt hat, von einem Knoten, dessen Namen er nicht
kennt. Ein fremdes zu **beenden** ist ein Entzug: Der Betroffene bekäme nichts
mehr und merkte es nicht, denn Ausbleiben sieht aus wie Ruhe.

Genau diese zweite Prüfung war zuerst ungeprüft: **eine von vierzehn Mutationen
überlebte**, die weggenommene JID-Prüfung beim Abbestellen. Der nachgezogene
Test prüft beides — die Absage *und* dass Carols Abonnement danach noch trägt.
Nur die Absage zu prüfen liesse eine Umsetzung durch, die erst abmeldet und
sich dann beschwert.

Zwölf Tests, vierzehn Mutationen, alle erschlagen. Voller Lauf: 962 bestanden,
7 übersprungen.

**Was der Server weiterhin nicht kann** und was damit auch der Client nie zu
sehen bekommt: mehrere gleichzeitige Abonnements desselben JIDs auf denselben
Knoten — dafür gibt es die `subid` überhaupt. Sie wird trotzdem vergeben und
geprüft, denn sie benennt ein Abonnement eindeutig; nur der Fall, der sie
unentbehrlich macht, tritt hier nicht ein.

---

### D71. Erst die Antwort, dann die Buchführung ✅ — die ausgehende Korrelation

Der Punkt stand seit D38 unter „Optional", und der Fehler war die ganze Zeit
derselbe: `PubSubSubscribeAsync` verschickte die Anfrage und trug das
Abonnement **in derselben Zeile** ein. Ein abgelehntes stand danach als
bestehendes da, und der Aufrufer erfuhr es nie.

**Es ist dieselbe Sorte Fehler wie die fünf aus der OMEMO-Reihe, nur ohne
Kryptographie: eine Behauptung über etwas, das niemand nachgesehen hat.** Sie
fällt lange nicht auf, weil sie im guten Fall stimmt.

Jetzt geht jede der sechs Anfragen über `SendIqAsync`, jede mit eigener
Kennung — bis hierher trugen alle `subscribe` dieselbe feste `pubsub-sub`, was
folgenlos war, solange niemand zuordnete, und beim ersten Zuordnen die zweite
Anfrage mit der Antwort auf die erste versorgt hätte. Eingetragen wird nach dem
`result`, gelöscht ebenfalls: **Wer den Eintrag vor der Antwort löscht, macht
denselben Fehler andersherum** und verwirft die Meldungen eines Abonnements,
das noch besteht.

Vom Ergebnis bleibt, was nur der Dienst weiss: die `subid`. Sie geht beim
Abbestellen mit — vorgeschrieben ist sie erst bei mehreren Abonnements auf
denselben Knoten, aber sie benennt auch das eine eindeutig.

`PubSubGetItemsAsync` hatte dieselbe Krankheit in ihrer klarsten Form: Sie
verschickte die Anfrage und war fertig. Die Antwort kam an, gehörte niemandem
und fiel aus dem Empfang heraus — **die Einträge, um die es ging, hat nie
jemand gesehen.** Jetzt gibt sie sie zurück.

## Ein Abonnement, das nichts einbrachte

Dabei kam der Fund dieser Etappe heraus: **Der Spoofing-Schutz verwarf jede
PEP-Meldung.** Er verglich den Absender mit dem PubSub-Dienst der Domain — eine
PEP-Meldung kommt aber nach XEP-0163 vom Konto selbst. Aufgefallen ist es nie,
weil bis zu diesem Punkt niemand ein Abonnement hatte, dessen Meldungen jemand
erwartete; OMEMO geht seinen eigenen Weg.

Ein bestätigtes Abonnement erlaubt jetzt zusätzlich den, bei dem es besteht —
**und zwar für seinen Knoten, nicht überhaupt.** Wer bei Bob den Wetterknoten
abonniert hat, hat nicht erlaubt, dass Bob Meldungen über jeden erdachten
anderen schickt. Genau dafür ist die Adresse in der Buchführung die, an die
*gefragt* wurde, und nicht das `from` der Antwort: Sonst könnte eine
Gegenstelle sich selbst zur Quelle erklären.

## Drei Mutationen, die einen Zufall aufdeckten

Von fünfzehn Mutationen überlebten drei, und alle drei zeigten auf dieselbe
Lücke: **Antworten, die ein wohlerzogener Server nicht gibt.** Ein `result`
ohne Zusage, eine Zusage ohne Knoten, ein Zustand, den dieser Client nicht
kennt. Gegen den eigenen Server kommt so etwas nie — die Ablehnung hing also
nicht an einer Entscheidung, sondern daran, dass in einer Fehlerantwort
zufällig keine Zusage steht.

Prüfbar wurden sie über einen Testschalter: `AnswerPepRequests` lässt den
Server schweigen, damit der Test selbst den Dienst spielen kann — wie
`AnswerPings` für XEP-0199. Er trägt zugleich den Fall, den man am ehesten
falsch behandelt, weil er sich nicht meldet: **Schweigen ist keine Zusage.**

Siebzehn Tests, fünfzehn Mutationen, alle erschlagen. Voller Lauf: 977
bestanden, 7 übersprungen.

---

### D72. Wofür es die subid gibt ✅ — mehrere Abonnements auf denselben Knoten

Am Ende von D71 stand die Grenze im README: mehrere gleichzeitige Abonnements
desselben JIDs auf denselben Knoten — **der Fall, für den es die `subid`
überhaupt gibt** — sind nicht umgesetzt. Bis dahin gab ein zweites `subscribe`
dieselbe Kennung zurück, und damit war die Kennung Zierde: Wo es nie zwei gibt,
benennt sie nichts, was der Knoten nicht auch benennt.

**Der Fall ist nicht ausgedacht.** Er entsteht von selbst, wenn ein Client neu
startet und wieder abonniert, ohne seine alte Kennung zu kennen. Danach hat der
Dienst zwei, und von da an ist jedes Abbestellen ohne Kennung zweideutig — der
Client aus D71 kann genau dort landen.

Jetzt ist jedes `subscribe` ein eigenes Abonnement mit eigener Kennung. Daraus
folgt dreierlei, und jedes davon ist eine Entscheidung, die auch anders hätte
ausfallen können:

- **Abbestellen ohne Kennung wird bei mehreren abgewiesen** —
  `<bad-request/>` mit `<subid-required/>` (Abschnitt 6.2.3.1). Sich eines
  auszusuchen wäre die bequeme Antwort und die falsche: Der Dienst beendete
  vielleicht das andere und bestätigte dem Absender, es sei seines gewesen.
- **Zugestellt wird je Abonnement**, nicht je Abonnent, und jede Zustellung
  nennt ihr Abonnement in der SHIM-Kopfzeile `SubID` (Abschnitt 12.20).
- **Ausdrücklich schlägt beiläufig.** Wer den Knoten abonniert hat, bekommt die
  Meldung nicht zusätzlich über die Presence — sonst hinge die Zahl der
  Zustellungen daran, ob jemand nebenbei auch noch im Roster steht. Und die
  Presence-Zustellung trägt keine Kennung, denn es gibt keine: eine erfundene
  wäre schlimmer als keine, der Empfänger könnte danach abbestellen wollen, was
  nie bestellt wurde.

Ein Test der vorigen Etappe behauptete das Gegenteil (`SubscribingTwice_KeepsOneSubscription`)
und ist ersetzt. Das war nicht falsch gewesen — ein Dienst darf so verfahren —,
aber es war die Fassung ohne die Sache.

**Was weiterhin fehlt**, und es ist der Grund, aus dem sich zwei Abonnements
sonst überhaupt unterscheiden: die Konfiguration je Abonnement (Abschnitt 6.3).
Ohne sie bringt ein zweites nichts ein als eine zweite Zustellung. Der Server
muss trotzdem richtig antworten, wenn es eines gibt — das ist der ganze Punkt
dieser Etappe.

Fünfzehn Tests, zehn Mutationen, alle erschlagen. Voller Lauf: 980 bestanden,
7 übersprungen.

---

### D73. Zwei Abonnements, die niemand verwechselt ✅ — die Kennung auf der Clientseite

Die Gegenseite zu D72, und sie hatte einen eigenen Fehler: **Der Client hielt je
Knoten genau ein Abonnement fest**, und ein zweites überschrieb das erste. Damit
war dessen Kennung weg — und weg heisst hier mehr als „vergessen": Es liess sich
**nie wieder abbestellen**, denn der Dienst verlangt bei mehreren eine Kennung,
und die kannte niemand mehr.

Jetzt steht je Knoten eine Liste. Daraus folgt das Verhalten, auf das es
ankommt: **Bei mehreren und ohne Kennung fragt der Client gar nicht erst.** Der
Dienst wiese es mit `<subid-required/>` ab, das weiss der Client selbst — und
was er nicht tut, ist wichtiger als was er tut: sich eines aussuchen. Das
beendete vielleicht das falsche, und der Aufrufer hielte es für das gemeinte.

Eine Kennung, die hier nicht steht, geht trotzdem hinaus, wenn der Aufrufer sie
nennt: Ein anderes Gerät desselben Kontos kann ein Abonnement halten, von dem
dieser Client nichts weiss. Die Buchführung ist die eigene Sicht und nicht die
Wahrheit über den Dienst.

Eingehend liest der Client jetzt die SHIM-Kopfzeile `SubID` und hängt sie an das
Ereignis. Sie steht **neben** dem `event` und nicht darin, und das ist keine
Formsache: Sie sagt etwas über die Zustellung, nicht über das Ereignis.
Dieselbe Veröffentlichung kommt bei zwei Abonnements zweimal an — dann ist diese
Kopfzeile das einzige, worin sich die beiden Meldungen unterscheiden.

Ein Test hält fest, was leicht verlorengeht: **Nach dem letzten Abbestellen ist
der Absender wieder ein Fremder.** Die Erlaubnis des Spoofing-Schutzes hängt an
der Buchführung; bliebe dort ein leerer Rest stehen, bliebe auch die Erlaubnis,
und der Schutz wäre für diesen Knoten dauerhaft offen. Genau das war eine der
acht Mutationen.

Die Konsole kann jetzt `/pubsub abos` — bei mehreren Abonnements auf denselben
Knoten ist die Kennung das einzige, was sie unterscheidet, und wer abbestellen
will, muss sie nachsehen können.

Zweiundzwanzig Tests, acht Mutationen, alle erschlagen. Voller Lauf: 985
bestanden, 7 übersprungen.

---

### D74. Ein Feld, und das ist die Aussage ✅ — Konfiguration je Abonnement

Der letzte offene Punkt aus D72: **die Konfiguration je Abonnement** (XEP-0060,
Abschnitt 6.3) — der Grund, aus dem sich zwei Abonnements desselben JIDs auf
denselben Knoten überhaupt unterscheiden können. Bis hierher waren zwei
Abonnements zwei gleiche Dinge, und das zweite brachte nichts ein als eine
zweite Zustellung. Jetzt ist die `subid` nicht nur eine Kennung, sondern **die
Adresse einer Einstellung**.

**Das Formular hat genau ein Feld: `pubsub#deliver`.** XEP-0060 kennt ein
Dutzend weitere — Zusammenfassungen, Ablauffristen, Tiefe, Presence-Filter. Was
dieser Server nicht kann, bietet er auch nicht an: Ein Formular mit
`pubsub#digest` darin, das dann nichts bewirkt, wäre eine Zusage ohne Deckung,
und zwar eine, die der Abonnent nicht nachprüfen kann — **eine ausbleibende
Zusammenfassung sieht aus wie Ruhe.**

Aus demselben Grund wird ein Feld, das im Angebot nicht stand, **abgewiesen
statt übergangen**. Das ist strenger, als XEP-0004 verlangt: Wer Unbekanntes
stillschweigend schluckt, lässt den Absender in dem Glauben, seine Einstellung
gelte. Eine Absage kann man lesen, eine ausbleibende Wirkung nicht.

Drei Entscheidungen, die auch anders hätten ausfallen können:

- **Ein stillgelegtes Abonnement fällt nicht auf die Presence-Zustellung
  zurück.** Wer gesagt hat, dass er nichts bekommen will, bekommt nichts — auch
  wenn er nebenbei im Roster steht. Alles andere hiesse, eine ausdrückliche
  Einstellung über einen zweiten Weg zu unterlaufen.
- **Ein `set` ohne Formular wird abgewiesen**, statt die Vorgaben einzusetzen.
  Aus einer unvollständigen Anfrage würde sonst eine Änderung, die niemand
  verlangt hat — und sie träfe ausgerechnet den, der gerade etwas anderes
  eingestellt hatte.
- **Fehlt bei mehreren die Kennung, ist der Fehler ein anderer als beim
  Abbestellen**: `<not-acceptable/>` statt `<bad-request/>` (Abschnitte 6.3.3
  gegen 6.2.3.1). Das ist keine Willkür des XEP — dort ist die Anfrage
  unvollständig, hier ist sie in Ordnung und nur in dieser Lage nicht zu
  beantworten. Eine Umsetzung, die beide Stellen gleich behandelt, hat eine
  davon nicht gelesen. Deshalb liefert die gemeinsame Suche den **Befund** und
  nicht die Antwort.

Die JID-Prüfung steht jetzt an drei Stellen, und die dritte ist die stillste:
**Wer fremde Abonnements einstellen dürfte, könnte sie lautlos abschalten.** Das
Abonnement bliebe stehen — es käme nur nichts mehr an, und der Betroffene fände
in seiner eigenen Liste nichts Auffälliges.

Sechsundzwanzig Tests, elf Mutationen, alle erschlagen. Voller Lauf: 996
bestanden, 7 übersprungen — und damit hat die Sammlung die tausend überschritten.

---

### D75. Streng beim Befolgen, nachsichtig beim Lesen ✅ — die Einstellung auf der Clientseite

Die Gegenseite zu D74, und sie brachte eine Unterscheidung mit, die vorher
nirgends stand: **Dasselbe Formular wird in zwei Richtungen verschieden
gelesen.**

- Ein **abgeschicktes** Formular ist eine *Anweisung*. Ein Feld darin, das
  niemand angeboten hat, wird abgewiesen — ein übergangenes wäre eine
  verworfene Anweisung, von der der Absender nichts erfährt.
- Ein **angebotenes** Formular ist eine *Auskunft*. Ein Feld darin, das dieser
  Client nicht setzen kann, wird übergangen — wer daran scheiterte, könnte mit
  keinem echten Dienst sprechen, denn der bietet ein Dutzend an.

Das ist kein Widerspruch, sondern die Richtung. Es hat auch eine Grenze, und
die zeigte eine überlebende Mutation: **Ein Angebot, das die Zustellung gar
nicht nennt, sagt über sie nichts** — die Vorgabe einzusetzen hiesse, sie zu
erfinden. Dasselbe eine Ebene höher, ebenfalls von einer Mutation gefunden: Ein
`result` ohne Formular ist keine Auskunft über die Einstellungen. Aus dem
Ausbleiben eines Fehlers auf einen Zustand zu schliessen ist die bequemste Art,
sich etwas einzubilden — und hier besonders heikel, weil die Vorgabe „wird
zugestellt" sagt: Der Client hielte ein stillgelegtes Abonnement für ein lautes.

Vermerkt wird erst, was der Dienst bestätigt hat — derselbe Fehler wie in D71,
nur eine Ebene tiefer. Und die Vormerkung trifft **das benannte Abonnement**,
nicht den Knoten: Eine dritte überlebende Mutation zeigte, dass der Fehler
stumm wäre, denn der Dienst stellte das richtige ein und nur die eigene
Buchführung zeigte danach einen Zustand, den es nicht gibt.

`null` heisst in dieser Buchführung **„nicht gefragt" und nicht „Vorgabe"**.
Gefragt wird auch dann, wenn schon etwas dasteht: Ein anderes Gerät desselben
Kontos kann dasselbe Abonnement inzwischen umgestellt haben, und dann wäre die
eigene Angabe eine Erinnerung und keine Auskunft.

Die Auswahl des gemeinten Abonnements teilen Abbestellen und Einstellen sich
jetzt — dieselbe Regel, eine Stelle: **Bei mehreren und ohne Kennung wird gar
nicht erst gefragt.**

Neunundzwanzig Tests, vierzehn Mutationen, alle erschlagen. Voller Lauf: 1003
bestanden, 7 übersprungen.

---

### D76. Ein Knoten, bevor etwas darin steht ✅ — Anlegen und Konfigurieren

Bisher hiess „es gibt den Knoten" dasselbe wie „es steht etwas darin". Das
klang harmlos und war es nicht: **Das Anlegen war folgenlos** — der Client
konnte `<create/>` schicken und bekam `<service-unavailable/>` —, und ein
Knoten ohne Ablage wäre überhaupt nie abonnierbar gewesen.

Jetzt gibt es beides getrennt: die Einstellungen eines Knotens und seinen
Inhalt. Ein angelegter Knoten existiert, bevor etwas darin steht.

**Drei Felder, und jedes tut etwas** (XEP-0060 kennt zwei Dutzend):

- `pubsub#max_items` — was der Knoten behält. Eine kleinere Grenze gilt
  **sofort** und nicht erst beim nächsten Veröffentlichen: Wer sie setzt, will
  nicht so viele aufbewahrt wissen, und auf einem Knoten, in dem nie wieder
  etwas erscheint, bliebe sonst alles liegen.
- `pubsub#persist_items` — behalten oder nur melden. Ein Knoten ohne Ablage
  meldet weiterhin; wer nicht zuhörte, hat es verpasst.
- `pubsub#access_model` — wer an die Einträge kommt. **Gespeichert, aber noch
  nicht durchgesetzt**; das ist K8, und bis dahin steht es so im README.

Angeboten wird nur, was wirkt. Bei einem Zugriffsmodell wäre eine Zusage ohne
Deckung am teuersten: **Wer `whitelist` einstellt und `open` bekommt, glaubt
seine Einträge geschützt und hat sie veröffentlicht.** Deshalb kennt dieser
Server `open` und `presence` — und weist alles andere ab, statt es
freundlich zu `open` zu verkürzen. Eine Mutation, die genau das tat, wurde
erschlagen.

Ein Teilformular ändert nur, was darin steht (Abschnitt 8.2.4). Die fehlenden
Felder mit der Vorgabe zu füllen wäre die naheliegende Abkürzung und eine
lautlose Änderung dessen, wonach niemand gefragt hat — auch dafür gab es eine
Mutation.

Und `max_items=0` ist kein Formfehler, sondern eine Falle: Ein Knoten, der
nichts behalten darf, sähe aus wie einer, in den niemand schreibt.

Nebenbei entstand ein kleiner gemeinsamer Baustein für XEP-0004
(`DataForm`): Zwei Formulare bauen dieselben Felder und lesen denselben
Wahrheitswert — zweimal dasselbe zu schreiben heisst, es einmal zu ändern und
einmal zu vergessen. Ein Formularmodell ist es ausdrücklich nicht.

Neununddreissig Tests, vierzehn Mutationen, alle erschlagen. Voller Lauf: 1016
bestanden, 7 übersprungen.

---

### D77. Eine Bedingung, die seit D66 niemand gelesen hat ✅ — Zugriffsmodell und publish-options

Zwei Dinge, die zusammengehören: Das Zugriffsmodell aus D76 war **gespeichert
und wirkungslos** — genau die Sorte Zusage, gegen die diese ganze Reihe
argumentiert. Und die Bedingungen, die OMEMO seit D66 mit jeder Veröffentlichung
mitschickt, hat **nie jemand angesehen**.

Das zweite ist der stillere Fehler. Der Client verlangte einen offenen Knoten
für sein Bundle, bekam ein `result` und durfte annehmen, es sei abrufbar. Ein
`result` auf eine Anfrage mit Bedingungen heisst „Bedingungen erfüllt" — es gab
sie nur nie. XEP-0384, Abschnitt 5.2 verlangt das offene Modell aus einem
konkreten Grund: **Wer verschlüsselt schreiben will, muss das Bundle lesen
können, und das ist im Zweifel jemand, der in keinem Roster steht.**

Jetzt wirkt beides. `presence` sperrt aus, wer die Presence des Eigentümers
nicht sehen darf — beim Abrufen wie beim Abonnieren, mit
`<not-authorized/>` und `<presence-subscription-required/>`. Der Eigentümer
kommt immer an seinen Knoten; er ist bei sich selbst kein Presence-Abonnent,
und ein Modell, das ihn aussperrt, hätte den Namen nicht verdient.

**Bedingung und Einstellung sind nicht dasselbe**, und der Unterschied liegt in
einem `null`: Es heisst „danach wird nicht gefragt" und nicht „Vorgabe". Wer
beides verwechselt, weist eine Veröffentlichung ab, weil der Knoten in einem
Punkt von der Vorgabe abweicht, über den der Absender nie etwas gesagt hat. Das
war die einzige überlebende Mutation, und der nachgezogene Test prüft genau
diesen Satz.

Eine unerfüllte Bedingung hält die Veröffentlichung **ganz** auf: Ein Dienst,
der die Bedingung abwiese und den Eintrag trotzdem ablegte, hätte das Gegenteil
dessen getan, wofür es Bedingungen gibt.

Ehrlich dazugesagt: Das Modell verrät, dass es den Knoten gibt — wer keinen
Zugriff hat, bekommt `<not-authorized/>` und nicht `<item-not-found/>`. So sieht
es das XEP vor, und es bleibt eine Auskunft: Für einen Knoten, dessen blosse
Existenz ein Geheimnis wäre, ist `presence` das falsche Mittel.

Achtundvierzig Tests, elf Mutationen, alle erschlagen. Voller Lauf: 1025
bestanden, 7 übersprungen.

---

### D78. Anlegen und einstellen in einem Zug ✅ — die Knoten auf der Clientseite

Die Clientseite von D76/D77, und sie hat eine eigene Pointe: **`<create/>` und
`<configure/>` gehen zusammen hinaus.** Zwei Schritte hätten eine Lücke — der
Knoten stünde zwischen dem Anlegen und dem Einstellen offen, und wer in dieser
Zeit fragt, bekommt. XEP-0060, Abschnitt 8.1.3 sieht das nicht ohne Grund vor.

Ansonsten dieselben Regeln wie in D75, und das ist der Punkt: Sie sind nicht
für die Abonnement-Einstellungen erfunden worden, sondern für Formulare
überhaupt. Ein `result` ohne Formular ist keine Auskunft — hier wäre die
Vorgabe besonders irreführend, denn sie sagt `open`, und der Client zeigte
einen geschützten Knoten als offen an. Ein `type='error'` bleibt eine Absage,
auch wenn ein vollständiges Formular darin steht; das war die einzige
überlebende Mutation, und der Test dazu ist wörtlich derselbe Gedanke wie in
D71.

Die Konsole setzt beim Umstellen des Zugriffs auf dem **gelesenen Stand** auf
und nicht auf der Vorgabe. Sonst setzte ein `/pubsub access` nebenbei die
Ablage und die Zahl der Einträge zurück — eine Änderung, nach der niemand
gefragt hat, und die stillste Art, die eigene Konfiguration zu verlieren.

Vierunddreissig Tests, acht Mutationen, alle erschlagen. Voller Lauf: 1030
bestanden, 7 übersprungen.

Damit ist die PubSub-Reihe (D70–D78) abgeschlossen. **Was von XEP-0060
weiterhin fehlt**, und es steht so im README: Sammelabfragen (`<subscriptions/>`,
`<affiliations/>`), das Löschen und Leeren von Knoten, `<retract/>`, die
Genehmigungsvorgänge hinter `authorize`, und die Zugriffsmodelle `roster` und
`whitelist`.

---

### D79. Die Frage, die sich niemand selbst beantworten kann ✅ — `<subscriptions/>`

XEP-0060, Abschnitt 5.6: eine Anfrage, und alle eigenen Abonnements stehen da —
über alle Knoten hinweg, mit Knoten, Kennung und Zustand; auf Wunsch auf einen
Knoten eingeschränkt.

**Der Anlass ist ein Loch, das die letzten Etappen selbst aufgemacht haben.**
Der `PubSubManager` wird in `InitialiseManagers` erzeugt, und das läuft bei
jedem Verbindungsaufbau — nur der Stream-Management-Manager überlebt einen
Reconnect, ausdrücklich und kommentiert. Danach ist die Buchführung leer, die
Abonnements aber nicht: Sie stehen am Konto und überdauern. Der Client kennt
also keine einzige `subid` mehr, und seit D72 weist der Dienst ein `unsubscribe`
ohne Kennung ab, sobald es mehrere gibt. Wer dann neu abonniert, hat zwei und
kann keines davon beenden.

Das ist genau die Klemme, mit der ich D72 begründet habe („ein Client startet
neu und abonniert wieder") — **ohne zu bemerken, dass unser eigener Client bei
jedem Abriss hineinläuft.**

Die schärfste Regel steht in einem Satz: **Aufgezählt werden die Abonnements
des Fragenden, nie die eines anderen.** Das ist keine Auslegungsfrage — wer
fremde aufzählen dürfte, erführe, wer sich wofür interessiert. Eine Auskunft
über Menschen, nicht über Knoten.

Und keine Abonnements sind eine leere Liste und kein Fehler: Die Frage war
beantwortbar, die Antwort lautet „keine". Ein Fehler hiesse etwas anderes —
dass sich die Frage nicht stellen liess —, und ein Client müsste anschliessend
raten, woran es lag.

Dreiundfünfzig Tests, sieben Mutationen, alle erschlagen. Voller Lauf: 1035
bestanden, 7 übersprungen.

---

### D80. Zurück zu den Kennungen ✅ — die Sammelabfrage auf der Clientseite

Die Gegenseite zu D79, und mit ihr ist die Klemme aus D72 auflösbar: Der Client
holt seine Abonnements beim Dienst und weiss danach wieder, was er hält. **Ein
Test spannt den ganzen Bogen** — zwei Abonnements anlegen, die Verbindung
abreissen lassen, prüfen dass die Buchführung wirklich leer ist (sonst prüfte er
nichts), abholen, und mit der wiedergefundenen Kennung abbestellen.

Drei Unterscheidungen, jede von einer überlebenden Mutation erzwungen:

- **Eine leere Aufzählung ist etwas anderes als eine fehlende.** Leer heisst „du
  hast keine" und leert die Buchführung zu Recht; fehlend heisst „darüber steht
  hier nichts". Beides gleichzusetzen kostet die ganze Buchführung — die
  Kennungen wären weg, obwohl die Abonnements bestehen.
- **Eine Aufzählung gilt für ihren Dienst**, nicht für alle. Aus dem Schweigen
  des einen auf das Ende der Abonnements beim anderen zu schliessen wäre ein
  Verlust ohne Anlass. Ebenso bei der Einschränkung auf einen Knoten: Wonach
  nicht gefragt wurde, bleibt stehen.
- **Was aufgezählt wird, ist nicht immer ein Abonnement.** Abschnitt 5.6 nennt
  jeden Zustand, auch `pending`. Der eigene Server sagt immer `subscribed`; ein
  fremder mit Genehmigungsvorgang tut es nicht — und dann stünde ein
  beantragtes Abonnement als bestehendes da. Derselbe Fehler wie in D71, nur
  über die Sammelabfrage hereingetragen.

**Von selbst geschieht nichts.** Ein Client, der bei jedem Verbindungsaufbau
ungefragt einen PubSub-Dienst anspräche, schickte eine Anfrage für ein Merkmal,
das die meisten nie benutzen — und gegen eine Adresse, die es womöglich gar
nicht gibt. Die Konsole hat dafür zwei Befehle statt eines: `abos` zeigt, was
dieser Client zu wissen glaubt, `sync` fragt den Dienst. Das sind zwei
verschiedene Fragen, und diese Reihe hat sich neun Etappen lang daran
abgearbeitet, sie auseinanderzuhalten.

Einundvierzig Tests, neun Mutationen, alle erschlagen. Voller Lauf: 1042
bestanden, 7 übersprungen.

**Was von den Sammelabfragen bleibt**: `<affiliations/>` (Abschnitt 5.7) und die
Eigentümer-Sicht auf die Abonnenten eines Knotens (Abschnitt 8.8). Das erste
wäre heute fast leer — dieser Server kennt keine Affiliations, ein PEP-Knoten
gehört seinem Konto und alle anderen haben nichts. Es lohnt sich erst, wenn
`publisher`, `member` und `outcast` beim Veröffentlichen und Abonnieren
tatsächlich etwas entscheiden; vorher stellte man eine Rolle ein, die niemand
prüft.

---

### D81. Rollen, die etwas entscheiden ✅ — Affiliations

In D80 stand, `<affiliations/>` lohne sich erst, wenn `publisher`, `member` und
`outcast` beim Veröffentlichen und Abonnieren tatsächlich etwas entscheiden.
Also nicht die Aufzählung zuerst, sondern das, was sie aufzählt:

- **`publisher`** darf in einen fremden Knoten schreiben. Die Meldung kommt
  trotzdem **vom Eigentümer** — käme sie vom Schreibenden, wäre sie eine
  Falschaussage über die Herkunft, und der Spoofing-Schutz des Empfängers hätte
  recht, sie zu verwerfen.
- **`outcast`** kommt an keinen Knoten, gleich wie offen der steht, **und
  verliert bestehende Abonnements** (Abschnitt 8.9.4). Ihn nur an neuen zu
  hindern hiesse, den Ausschluss von dem Zufall abhängig zu machen, ob er
  vorher schon da war.
- **`member`** entscheidet noch nichts — das ist K13, und bis dahin steht es so
  im README. Angeboten wird die Rolle trotzdem, weil sie sich sonst nicht
  vergeben liesse, bevor das Zugriffsmodell sie braucht.

**Der Eigentümer ist kein Eintrag, sondern das Konto.** Er steht in der Liste,
ohne dass ihn jemand eingetragen hätte, und lässt sich nicht umtragen: Wer das
könnte, könnte einem anderen sein eigenes Konto wegnehmen.

Zwei Absagen statt einer, weil sie Verschiedenes sagen: `<not-authorized/>`
heisst „dieser Knoten steht dir nicht offen" und nennt mit
`<presence-subscription-required/>` den Weg hinein; `<forbidden/>` für einen
Ausgeschlossenen sagt „du nicht", und einen Weg gibt es nicht. Ihn auf eine
Presence-Anfrage zu schicken, die nichts ändern wird, wäre eine falsche
Auskunft.

## Drei Mutationen gegen Code, der nichts entschied

Sie überlebten nicht, weil Tests fehlten, sondern weil es an drei Stellen
**zwei Wege zu derselben Entscheidung** gab:

- Die Eigentümer-Erkennung in `PepAffiliationOf` wurde nirgends benutzt — das
  Veröffentlichen verglich stattdessen JIDs. Jetzt fragt es nach der Rolle, und
  die Regel steht einmal statt zweimal: **schreiben darf, wer besitzt oder wem
  der Besitzer es erlaubt hat.**
- Der Ausschluss wurde in `MayAccessPepNode` <i>und</i> in der Fehlerauswahl
  geprüft. Die zweite Prüfung entscheidet, also ist die erste weg.
- Und die eigens geschriebene Prüfung „ein Publizierender legt keine Knoten an"
  war unerreichbar: **An einem Knoten, den es nicht gibt, hat niemand eine
  Rolle**, die Absage kommt schon von der Rollenprüfung. Der Test dazu prüft
  jetzt die Regel dahinter — eine Rolle gehört einem Knoten und nicht einem
  Konto.

Vierundsechzig Tests, fünfzehn Mutationen, alle erschlagen. Voller Lauf: 1053
bestanden, 7 übersprungen.

---

### D82. Eine Liste entsteht nicht nebenbei ✅ — `whitelist`

Das dritte Zugriffsmodell, und der einzige Grund, aus dem es diese Etappe gibt:
**`member` entschied bis hierher nichts.** Die Rolle war vergebbar und
folgenlos — in D81 ausdrücklich so notiert, damit sie sich vergeben lässt,
bevor das Modell sie braucht. Jetzt braucht es sie.

Der Unterschied zu `presence` ist der Punkt: **Presence-Berechtigung entsteht
nebenbei.** Jemand nimmt einen Kontakt auf, und schon sieht er mehr. Eine Liste
entsteht nicht nebenbei — auf ihr steht nur, wen der Eigentümer ausdrücklich
daraufgesetzt hat. Der Test hält das fest, indem Carol Kontakt ist und trotzdem
draussen bleibt.

Zwei Entscheidungen, die auch anders hätten ausfallen können:

- **Ein `publisher` steht auch auf der Liste.** Alles andere wäre eine Rolle,
  die man nur mit einer zweiten zusammen gebrauchen kann, und der Eigentümer
  müsste bei jedem Publizierenden daran denken, ihn zusätzlich zum Mitglied zu
  machen.
- **Der Ausschluss steht über dem Modell.** Ein Ausgeschlossener, den jemand
  versehentlich auf die Liste setzt, bleibt draussen — sonst hinge der
  Ausschluss davon ab, in welcher Reihenfolge zwei Anweisungen kamen.

Nebenbei aufgeräumt: Das Zugriffsmodell wurde an **vier Stellen** gelesen und
geschrieben — Knotenformular hin, Knotenformular zurück, Bedingungen einer
Veröffentlichung, Serverprüfung. Vier Stellen, die dieselbe Liste führen,
führen sie irgendwann verschieden, und die eine, die ein Modell nicht kennt,
lässt es still als `open` durchgehen. Jetzt gibt es eine.

Ein Test aus D76 musste umgeschrieben werden: Er benutzte `whitelist` als
Beispiel für ein nicht angebotenes Modell. Er prüft jetzt `authorize` — der
Genehmigungsvorgang dahinter fehlt weiterhin, und darum wird es abgewiesen.

Achtundsechzig Tests, sieben Mutationen, alle erschlagen. Voller Lauf: 1057
bestanden, 7 übersprungen.

---

### D83. Zum dritten Mal dieselbe Stelle ✅ — Rollen auf der Clientseite

Vergeben, nachsehen, wirken lassen — die Clientseite von D81/D82. Drei Fragen,
die auseinandergehalten gehören: **was habe ich vergeben** (Abschnitt 8.9.1),
**was bin ich anderswo** (5.7), und darf ich, was die Rolle verspricht.

Beide Listen sehen gleich aus und werden von einer Stelle gelesen; sie
unterscheiden sich im Namensraum und darin, ob der Eintrag einen Knoten oder
einen JID nennt. Zwei Mutationen haben genau diese Verwechslung geprüft.

**Ein Eintrag mit einer unbekannten Rolle lässt die ganze Liste scheitern**,
statt still zu fehlen. Eine Liste, aus der einzelne Zeilen verschwinden, ist
schlimmer als keine: Wer sie ansieht, hält jemanden für rechtlos, der es nicht
ist — und nimmt ihm womöglich auch noch die Rolle, die er zu haben glaubte.

Und die überlebende Mutation war zum dritten Mal dieselbe: **Ein `type='error'`
bleibt eine Absage, auch wenn eine vollständige Liste darin steht.** Ohne die
Prüfung auf den Typ hinge die Ablehnung daran, dass in einer Fehlerantwort
zufällig keine Liste steht. Hier wäre die Verwechslung besonders unangenehm —
der Client zeigte eine Rollenliste an, die er nicht einsehen darf, und der
Eigentümer erführe daraus, dass sein Knoten offener steht, als er steht.

Beim Testschreiben eine eigene Falle vermieden: `Assert.Multiple` nimmt eine
`Action`. Ein `async`-Lambda darin liefe als `async void` weiter, und die
Zusicherungen fielen womöglich nach dem Block — also nirgends. Erst awaiten,
dann prüfen.

Fünfundvierzig Tests, sieben Mutationen, alle erschlagen. Voller Lauf: 1061
bestanden, 7 übersprungen.

Damit sind die Rollen fertig (D81–D83) und von XEP-0060 bleibt: die
Eigentümer-Sicht auf die **Abonnenten** eines Knotens (Abschnitt 8.8), das
Löschen und Leeren von Knoten, `<retract/>`, sowie die Zugriffsmodelle
`authorize` und `roster` — für die es einen Genehmigungsvorgang und
Rostergruppen als Zugriffsregel bräuchte.

---

### D84. Wer an meinem Knoten hängt ✅ — die Abonnenten-Sicht des Eigentümers

In D79 stand über die Sammelabfrage: „Wer fremde aufzählen dürfte, erführe, wer
sich wofür interessiert — eine Auskunft über Menschen, nicht über Knoten."
Jetzt tut der Server genau das, und es ist kein Rückzieher, sondern eine andere
Frage. **Abschnitt 5.6 fragt „wo hängt dieser Mensch überall", Abschnitt 8.8
fragt „wer hängt an meinem Knoten".** Das erste ist ein Interessenprofil und
geht über alle Knoten eines Dienstes; das zweite ist eine Auskunft über einen
Knoten — und wer sie nicht bekommt, ist derjenige, von dem alle Empfänger ihre
Daten haben. Ihm die Empfängerliste vorzuenthalten hiesse, ihn für eine
Verteilung verantwortlich zu machen, die er nicht sehen darf.

**Die Kennung ist hier keine Zierde.** Seit D72 kann derselbe JID mehrfach
abonniert sein; ohne `subid` stünde er zweimal gleich da, und der Eigentümer
könnte keines seiner Abonnements von dem anderen unterscheiden — also auch
keines einzeln beenden.

Drei Entscheidungen:

- **Der Eigentümer darf wegnehmen, nicht hergeben.** Abschnitt 8.8.2 lässt ihn
  auch anmelden; dieser Server nicht. Jemanden einzutragen, der nicht gefragt
  hat, ist genau das, was Abschnitt 6.1.3.1 auf der anderen Seite verhindert,
  und dass es der eigene Knoten ist, ändert nichts für den, dessen Postfach
  sich füllt. Ohne Genehmigungsvorgang gäbe es dazu auch nichts, was vorher
  eine Frage gewesen wäre.
- **Ohne Kennung gehen alle** — kein Widerspruch zu Abschnitt 6.2.3.1. Dort
  muss der *Abonnent* sagen, welches er meint, weil die anderen seine bleiben
  sollen. Hier meint der *Eigentümer* den Menschen und nicht die Buchführung:
  Eines stehen zu lassen hiesse, die Anweisung zur Hälfte auszuführen, und der
  Entfernte bekäme weiter alles.
- **Was niemand findet, wird nicht beendet, sondern abgewiesen.** Ein `none`
  für einen, der gar nicht abonniert hat, stillschweigend gelten zu lassen wäre
  wieder die Meldung über etwas, das niemand nachgesehen hat — ein Tippfehler
  im JID, und der Eigentümer hielte jemanden für entfernt, der weiter alles
  bekommt.

Ein `subscribed` für ein *bestehendes* Abonnement gilt trotzdem: Es ist keine
Anweisung, sondern eine Bestätigung. **Eine Liste, die sich nicht unverändert
zurückschicken lässt, wäre kein Zustand, sondern ein Formular.**

Und die Lehre aus D83 diesmal vorher gezogen statt hinterher: Der
Eigentümer-Block prüfte Besitz und Knoten an **jeder** Anweisung einzeln — mit
den Abonnenten wäre es die dritte Kopie derselben Entscheidung geworden. Jetzt
steht der Vorspann einmal davor, und wer ihn lockert, lockert ihn für alle
sichtbar oder gar nicht.

**Was hier noch fehlt:** Der Entfernte erfährt nichts davon. Er wartet auf
Meldungen, die nicht mehr kommen — und das ist genau der Zustand, den
`PubSubSubscriptionState` seit D71 als den schlimmeren beschreibt. Abschnitt
8.8.4 sieht dafür eine Nachricht vor; sie ist D85.

Einundachtzig Tests, vierzehn Mutationen, alle erschlagen. Voller Lauf: 1074
bestanden, 7 übersprungen.

---

### D85. Eine Meldung über das, was geschehen ist ✅ — die Abmeldung

Das Loch aus D84 zugemacht: Wer entfernt wurde, wartete auf Meldungen, die nicht
mehr kommen. **Das ist der schlimmere der beiden Irrtümer** — so steht es seit
D71 in `PubSubSubscriptionState`: Wer sich zu Unrecht für nicht abonniert hält,
fragt noch einmal nach; wer sich zu Unrecht für abonniert hält, wartet auf
etwas, das nie kommt.

**Je erloschenem Abonnement eine Meldung, nicht je Anweisung.** Ein `none` ohne
Kennung beendet alle Abonnements eines JIDs; käme darauf nur eine Meldung,
wüsste der Empfänger von einer Kennung, dass sie erloschen ist, und von der
anderen nichts. Deshalb meldet der Server nicht, was ihm aufgeschrieben wurde,
sondern was er tatsächlich entfernt hat — eine abgewiesene Anweisung meldet
nichts ab.

**Auch der Ausschluss meldet sich**, denn er beendet Abonnements (Abschnitt
8.9.4). Er nennt dabei seine eigene Ursache nicht: Was der Ausgeschlossene an
diesem Knoten *ist*, geht ihn nichts an — dass er ihn nicht mehr bekommt,
schon. Zwei verschiedene Auskünfte, und nur die zweite schuldet der Server ihm.

Dafür musste `SetPepAffiliation` sagen können, was der Ausschluss gekostet hat.
Die Auskunft gehört dorthin, wo entfernt wird: Sie sich vorher selbst
zusammenzusuchen hiesse, dieselbe Frage zweimal zu beantworten — und die zweite
Antwort wäre die ungenauere, weil zwischen Nachsehen und Setzen etwas
dazwischenkommen kann. Beide Wege zum Beenden führen jetzt durch dieselbe
Methode; zwei Stellen, die Abonnements beenden, beenden sie irgendwann
verschieden.

**Ein `headline` und damit nichts für die Ablage** (XEP-0160). Wer offline ist,
erfährt es nicht — so wie er auch die Veröffentlichungen nicht bekommt, die er
versäumt. Die Auskunft bleibt trotzdem erreichbar, und das ist der Grund, aus
dem D79/D80 vorher dran waren: Abschnitt 5.6 sagt ihm beim nächsten Verbinden,
was er noch hat. **Eine aufbewahrte Meldung wäre die schlechtere Auskunft**,
denn sie beschreibt einen Stand von damals.

Neunundachtzig Tests, acht Mutationen, alle erschlagen. Voller Lauf: 1082
bestanden, 7 übersprungen.

---

### D86. Zwei Aufzählungen, die sich zum Verwechseln ähneln ✅ — die Clientseite

Die Clientseite von D84/D85. `<subscriptions/>` heisst beides: „wo hänge ich
überall" (Abschnitt 5.6) und „wer hängt an meinem Knoten" (8.8.1). Gleicher
Elementname, gleicher Aufbau, und der Eintrag nennt einmal einen Knoten und
einmal einen JID — **zu unterscheiden sind sie allein am Namensraum.** Drei
Mutationen haben genau diese Verwechslung geprüft; es ist dieselbe Falle wie
bei den Rollen in D83, nur mit einem Elementnamen, den man leichter für
denselben hält.

**Der Zustand wird hier streng gelesen, und in der eigenen Zusage nicht.** Das
sieht nach einer Unstimmigkeit aus und ist der Punkt: Dort ist ein unbekannter
Name als „nicht abonniert" die vorsichtige Annahme — wer sich zu Unrecht für
nicht abonniert hält, fragt noch einmal. Hier wäre dieselbe Nachsicht das
Gegenteil von vorsichtig: Der Eigentümer hielte einen Abonnenten für abwesend,
den der Dienst führt, und entfernte womöglich einen anderen an seiner Stelle.
Ein unlesbarer Eintrag lässt darum die ganze Liste scheitern.

**Der Client kann entfernen und nicht anmelden**, obwohl Abschnitt 8.8.2 beides
zulässt — dieselbe Entscheidung wie im Server, und aus demselben Grund. Ein
Client, der einen anderen ungefragt anmelden kann, braucht dafür keinen Namen
in `PubSubBuilder`: Wer das will, schreibt es hin und sagt, was er tut.

Dazu die Gegenprobe im Eingang: Eine `<subscription/>`-Meldung mit
`subscription='subscribed'` wird **nicht** eingetragen. Eine Zusage kommt auf
eine Anfrage; wer sie ungefragt annähme, liesse sich von einem Dienst anmelden.
Damit weisen beide Seiten dasselbe ab.

Der Knoten einer Abmeldung musste in `NodeOf` aufgenommen werden, und nicht nur
damit sie ankommt: **An diesem Knoten hängt die Absenderprüfung.** Eine Meldung,
deren Knoten dort leer bleibt, gilt als Meldung über den Knoten `""` — den
niemand abonniert hat. Die Mutation, die den Eintrag wieder herausnimmt, wird
deshalb nicht vom Auswerten erschlagen, sondern vom Spoofing-Schutz.

Zweiundfünfzig Tests, zehn Mutationen, alle erschlagen. Voller Lauf: 1091
bestanden, 7 übersprungen.

Damit ist Abschnitt 8.8 fertig (D84–D86) und von XEP-0060 bleibt: das Löschen
und Leeren von Knoten, `<retract/>` sowie die Zugriffsmodelle `authorize` und
`roster` — für die es einen Genehmigungsvorgang und Rostergruppen als
Zugriffsregel bräuchte.

---

### D87. Der Knoten und sein Inhalt ✅ — Löschen und Leeren

Zwei Anweisungen, die man leicht für Abstufungen derselben hält, und die
verschiedene Dinge betreffen: **Gelöscht wird der Knoten, geleert nur sein
Inhalt.** Wer geleert hat, veröffentlicht weiter an dieselben Empfänger; wer
gelöscht hat, an niemanden.

Der Testserver konnte bis hierher keines von beiden — `/pubsub delete` gab es
in der Konsole seit jeher, und der Server antwortete darauf, wie er auf alles
Unbekannte antwortet. Der fehlende Teil war also nicht der Client, sondern die
Gegenstelle.

**Ein gelöschter Knoten nimmt vier Dinge mit**, und das vierte ist der Grund,
es hinzuschreiben: Einträge, Einstellungen, Abonnements **und Rollen**. Blieben
die Rollen stehen, erbte der nächste Knoten desselben Namens eine
Ausschlussliste, die niemand mehr sieht — und der Eigentümer wunderte sich,
warum ein Bekannter an seinen neuen Knoten nicht herankommt.

## Die überlebende Mutation war gar keine

Beim Leeren stand zuerst `eintraege.Clear()` statt `_pepNodes.Remove(node)`,
und zwar mit einer Begründung, die gut klang: Ein Knoten, der bloss durchs
Veröffentlichen entstanden ist, stünde allein in der Ablage — wird sie entfernt,
hätte das Leeren ihn gelöscht. Die Mutation, die genau das tut, hat **überlebt**,
zweimal, auch nachdem der Test die Lücke schloss, durch die er beim ersten Mal
gefallen war.

Der Grund: **Den Fall gibt es nicht.** `PublishPepItem` legt die Einstellung an,
bevor es den ersten Eintrag schreibt, genau wie `CreatePepNode` — es gibt keinen
Knoten, der nur in der Ablage steht. Die Abwehr richtete sich gegen einen
Zustand, den nichts herstellen kann, und war deshalb nicht zu widerlegen.

Dahinter lag der eigentliche Fund: **Die Frage „gibt es diesen Knoten" hatte
zwei Antworten** — Einstellung vorhanden *oder* Einträge vorhanden. Die zweite
war unerreichbar und wäre beim Leeren zur Falle geworden. Jetzt hängt ein Knoten
an seiner Einstellung, an einer Stelle und nur dort; dieselbe Vereinfachung
räumte eine zweite Aufzählung in `PepAffiliationsOf` mit weg. Das ist der Fund
aus D81 in neuer Gestalt: nicht ein fehlender Test, sondern **zwei Wege zu
derselben Entscheidung.**

Der Test, den die erste Mutation aufgedeckt hat, bleibt trotzdem stehen — er
sah erst nach der nächsten Veröffentlichung nach, und die legt den Knoten wieder
an. **Ein gelöschter hätte danach ausgesehen wie ein geleerter.**

**Je Abonnenten eine Meldung, nicht je Abonnement** — und ohne Kennung. Das ist
die Gegenentscheidung zu D85, aus demselben Grund: Dort endeten einzelne
Abonnements, und die Kennung sagte, welches. Hier endet der Knoten; eine
Kennung zu nennen hiesse, die anderen bestünden weiter. Aus demselben Grund
kommt keine zweite Meldung nach Abschnitt 8.8.4 hinterher.

Zwei Absagen, die auch anders hätten ausfallen können:

- **Ein Knoten ohne Ablage lässt sich nicht leeren** (Abschnitt 8.5.3.2). Für
  das Gegenteil liesse sich argumentieren — die Meldung ist ja an den
  Abonnenten gerichtet, und der hat womöglich etwas aufbewahrt. Das XEP
  entscheidet anders, und mit dem besseren Grund: Ein `result` wäre die
  Auskunft, es sei etwas geleert worden, und die Meldung die Aufforderung,
  etwas wegzuwerfen, das dieser Knoten nie ausgeliefert hat.
- **Ein `get` auf `<delete/>` ist ein `<bad-request/>`** und kein Löschen.
  Ohne diese Prüfung fiele es bis zum Einstellen durch und bekäme die
  Knotenkonfiguration zurück — eine Antwort auf eine Frage, die niemand
  gestellt hat.

Nicht umgesetzt: das `<redirect/>` aus Abschnitt 8.4.2, mit dem ein gelöschter
Knoten auf seinen Nachfolger zeigt. Es wäre ein Verweis, dem der Client folgen
müsste, und ohne den zweiten Knoten ein Versprechen ohne Deckung.

Hundert Tests, zwölf Mutationen, alle erschlagen. Voller Lauf: 1102 bestanden,
7 übersprungen.

---

### D88. Was der Löschende als einziger nicht erfährt ✅ — die Clientseite

Die Clientseite von D87, und sie besteht fast ganz aus dem, was **nach** der
Antwort zu tun ist.

**Ein gelöschter Knoten nimmt das Abonnement darauf mit, ein geleerter nicht.**
Das ist derselbe Unterschied wie im Server, nur von der anderen Seite gesehen:
Nach einem `<purge/>` kommt die nächste Veröffentlichung an dieselbe Adresse,
und wer hier mit aufräumte, hätte danach keinen Eintrag mehr über ein
Abonnement, das weiterbesteht — und müsste dessen Meldungen für Fälschungen
halten.

**Der Löschende bekommt keine Meldung.** Der Dienst schickt das `<delete/>` an
alle ausser den, der gelöscht hat — richtig so, aber es heisst, dass genau der
seinen Eintrag selbst streichen muss. Wer sich auf die Meldung verliesse,
behielte als einziger eine Buchführung über einen Knoten, den er selbst
beseitigt hat. Eine abgewiesene Löschung räumt dagegen nichts auf; auch das ist
eine eigene Mutation wert.

**Gestrichen wird je Dienst und nicht je Namen.** `urn:xmpp:omemo:2:bundles`
heisst bei jedem Konto so — wer beim Löschen bloss den Knotennamen aus der
Buchführung nimmt, beendet zugleich das Abonnement auf den gleichnamigen Knoten
von jemand anderem und merkt es erst, wenn dessen Meldungen ausbleiben. Der
Test dazu hält zwei Abonnements auf denselben Namen bei zwei Konten.

Nebenbei: `PubSubBuilder.DeleteNode` schrieb seinen Namensraum als Zeichenkette
aus, während alle anderen Eigentümer-Anfragen die Konstante benutzen. Zwei
Schreibweisen derselben Sache halten sich, bis eine von beiden falsch wird.

Siebenundfünfzig Tests, sieben Mutationen, alle erschlagen. Voller Lauf: 1107
bestanden, 7 übersprungen.

Damit bleibt von XEP-0060 noch `<retract/>` sowie die Zugriffsmodelle
`authorize` und `roster`.

---

### D89. Eine Zustellung und keine Nachricht über den Knoten ✅ — `<retract/>`

Der Gegensatz zu D87 in einem Satz: **Löschen und Leeren betreffen den Knoten,
eine Rücknahme betrifft einen Eintrag.** Daran hängt alles Weitere. Sie geht
deshalb nicht je Abonnenten einmal hinaus, sondern **je Abonnement, mit
Kennung, und an ein stillgelegtes gar nicht** — genau wie eine
Veröffentlichung, denn sie ist eine Zustellung.

Das liess sich beweisen, statt es zu behaupten: Die Zustellung von
Veröffentlichung und Rücknahme läuft jetzt durch dieselbe Stelle, die nur noch
den Inhalt von `<items/>` gereicht bekommt. Für das stillgelegte Abonnement war
danach nichts mehr zu bedenken — der Test dazu prüft, dass es auch so bleibt.

**Wer schreiben darf, darf auch zurücknehmen.** Dieselbe Rollenprüfung wie beim
Veröffentlichen, und damit kommt ein `publisher` auch an fremde Einträge im
selben Knoten. Die feinere Regel — jeder nur seine eigenen — wäre die bessere,
setzte aber voraus, sich zu merken, wer welchen Eintrag geschrieben hat. Diese
Ablage gibt es hier nicht, und ohne sie wäre die Regel bloss behauptet.

Zwei Absagen, beide aus demselben Grund wie in D87: Ein Eintrag, den es nicht
gibt, bekommt `<item-not-found/>`; ein Knoten ohne Ablage `<unsupported
feature='persistent-items'/>`. Ein `result` wäre jeweils die Auskunft, etwas sei
zurückgenommen worden — und die Meldung an die Abonnenten die Aufforderung,
etwas wegzuwerfen, das sie nie bekommen haben.

Ein Test hatte zuerst unrecht, und die Antwort des Servers war die bessere: Für
einen **fremden** Knoten erwartete er `<forbidden/>` mit der Begründung aus D81
— an einem Knoten, den es nicht gibt, hat niemand eine Rolle. Für den
Eigentümer gilt das nicht: **Er wird erkannt und nicht nachgeschlagen**, weil
ein PEP-Knoten dem Konto gehört. Ihm fehlt also nicht die Erlaubnis, sondern der
Eintrag, und genau das sagt `<item-not-found/>`.

Der letzte zurückgenommene Eintrag lässt den Knoten stehen. Ein Knoten, der mit
seinem Inhalt verschwände, wäre für seine Abonnenten ohne Ankündigung fort — und
die nächste Veröffentlichung legte einen neuen an, den niemand abonniert hat.

**Was die Zusammenlegung nebenbei aufgedeckt hat:** Die Mutation, die eine
Veröffentlichung ohne ihre `<item/>`-Hülle hinausschickt, hat überlebt. Diese
Sammlung prüfte den Inhalt einer Zustellung, die Herkunft und die Kennung des
Abonnements — **nie aber die Kennung des zugestellten Eintrags.** Das ist keine
Förmlichkeit: Ein Client, der Einträge nach ihrer Kennung führt, übergeht ein
Item ohne sie ganz. Der Inhalt käme an und wäre trotzdem verloren.

Hundertsieben Tests, neun Mutationen, alle erschlagen. Voller Lauf: 1114
bestanden, 7 übersprungen.

---

### D90. Der Teil, der schon da war ✅ — `<retract/>` auf der Clientseite

Die kürzeste Etappe dieser Reihe, und das aus einem Grund, der zu ihr gehört:
**Der Client konnte eingehende Rücknahmen von Anfang an lesen.** `PubSubEvent`
kennt `Retract` samt der Liste betroffener Kennungen, seit es
`PubSubManager.ProcessEvent` gibt — es kam nur nie eine an, weil kein Server in
Reichweite eine schickte. Erst D89 hat die Gegenstelle nachgeliefert, und
seither ist der Zweig zum ersten Mal gelaufen. Dieselbe Geschichte wie beim
Löschen in D88, nur ohne den Aufräumteil.

Denn aufzuräumen gibt es hier nichts, und das ist die einzige Entscheidung
dieser Etappe: **Eine Rücknahme betrifft einen Eintrag und nicht den Knoten.**
Das Abonnement bleibt stehen — anders als beim Löschen, wo es mitgeht. Es hier
ebenfalls zu streichen wäre ein Verlust ohne Anlass: Der Knoten besteht weiter,
und die nächste Veröffentlichung käme an eine Adresse, die dieser Client nicht
mehr kennt. Der Test dafür veröffentlicht nach der Rücknahme noch einmal und
prüft, dass es unter derselben Kennung ankommt.

Was ankommt, ist allein die Kennung des Eintrags — eine Rücknahme hat keine
Nutzlast. Wer sie nicht liest, weiss, dass sich etwas geändert hat, aber nicht
was, und muss den ganzen Knoten neu abrufen.

Sechzig Tests, sechs Mutationen, alle erschlagen. Voller Lauf: 1117 bestanden,
7 übersprungen.

Damit ist XEP-0060 bis auf die Zugriffsmodelle `authorize` und `roster` fertig —
für die es einen Genehmigungsvorgang und Rostergruppen als Zugriffsregel
bräuchte.

---

### D91. Die Gruppe, die es nie bis zum Server schaffte ✅ — Roster-Gruppen

Auf dem Weg zum Zugriffsmodell `roster` stellte sich heraus, dass die
Voraussetzung fehlt: **Der Testserver kannte keine Roster-Gruppen.** Und nicht
nur das — er tat so, als kennte er sie:

- `RosterStanzaBuilder.SetItem` schickt `<group/>` mit, seit es ihn gibt.
- `RosterItem.Groups` führt sie beim Client, `/roster` zeigt danach sortiert an.
- Der Kommentar in der Roster-Behandlung des Servers sagt seit jeher, ein Set
  ändere „Name **und Gruppen**".
- Gelesen wurde das `<item/>` nur bis zu seinen Attributen.

Die Gruppe kam an, wurde still verworfen, und der Push brachte denselben
Eintrag ohne sie zurück. **Weil ein Push die Gruppen eines Eintrags ersetzt,
verschwand sie damit auch beim Client** — was der Mensch eingestellt hatte, war
einen Wimpernschlag später weg, und nichts sah nach einem Fehler aus.

**Zwei Stellen, an denen dasselbe noch einmal passiert wäre**, sind beim
Nachziehen aufgefallen:

- Der **Handschlag** (`UpdateRosterEntry`) baute den Eintrag Feld für Feld neu.
  Die frisch gesetzte Gruppe fiel dabei heraus, weil `AddContactAsync` gleich
  nach dem Set eine Presence-Anfrage schickt — der Test war rot, obwohl das
  Lesen längst stimmte. Jetzt wird der bestehende Eintrag mit `with` geändert;
  das kennt auch die Felder, die noch kommen.
- Die **Ablage** (`FileAccountStore`) schrieb den Roster ebenso Feld für Feld.
  Ohne die Ergänzung hätten die Gruppen jeden Serverneustart nicht überlebt.

**Die Fassung des Rosters zählt sie mit** (RFC 6121, Abschnitt 2.6). Das ist der
Teil, an dem sonst nichts auffiele: Bliebe die Fassung nach einem Umgruppieren
dieselbe, bekäme ein Client, der sie zwischengespeichert hat, beim nächsten
Anmelden ein leeres Ergebnis — und behielte die alte Einteilung für immer. Der
Fehler zeigte sich erst Tage später und an einem anderen Gerät.

Dazu ein `XmlEscaping.Unescape` für die Stellen, die eine Stanza mit einem
Muster lesen statt sie zu zerlegen. **Das kaufmännische Und zuletzt:** Wer es
zuerst ersetzt, macht aus `&amp;lt;` ein `<` — aus einem Text, der von einem
Zeichen handelt, wird das Zeichen. Der Test dazu trägt eine Gruppe namens
`A&lt;B`, die genau das wörtlich meint.

Sechs Tests, sechs Mutationen, alle erschlagen. Voller Lauf: 1123 bestanden,
7 übersprungen.

---

### D92. Die Liste, die der Eigentümer ohnehin führt ✅ — Zugriffsmodell `roster`

Das vierte von fünf Modellen, und nach D91 fast eine Formsache: Wer im Roster
des Eigentümers steht, kommt herein; sind Gruppen genannt, nur wer in einer
davon steht.

**Ein Eintrag genügt, ein Presence-Zustand wird nicht verlangt** — das ist der
Unterschied zu `presence`, und er ist keine Ungenauigkeit, sondern eine andere
Frage: Dort geht es darum, wer *mich sehen darf*, hier darum, wen *ich führe*.
Beides kann auseinandergehen, und dann sind es zwei Antworten und nicht eine
ungefähre.

**Ohne genannte Gruppen kommt der ganze Roster herein.** Die leere Liste als
„niemand" zu lesen wäre die andere Möglichkeit und die schlechtere: Sie machte
`roster` in seiner Grundeinstellung wirkungsgleich mit einer leeren
`whitelist` — zwei Namen für dieselbe Sache, und einer davon führte in die
Irre.

Die Gruppenliste steht auch dann im Formular, wenn ein anderes Modell gilt. Sie
ist eine Einstellung des **Knotens** und nicht des Modells: Wer von `open` auf
`roster` umstellt, soll die Liste vorher setzen können, statt den Knoten
zwischen zwei Anweisungen für den ganzen Roster offen stehen zu lassen.

`pubsub#roster_groups_allowed` ist das erste Feld dieses Hauses, das **mehrere
Werte** trägt. Der Formularhelfer sagte bis hierher ausdrücklich, Mehrfachwerte
würden nicht gebraucht — jetzt gibt es sie, und ein `list-multi`, von dem nur
der erste Wert gelesen würde, wäre genau die stille Verkürzung, gegen die
dieses Haus sonst schreibt.

Nebenbei ein Fund derselben Art wie in D91: **Der Konsolenbefehl `/pubsub
access` kannte `whitelist` nicht** — er nahm seit jeher nur `open` und
`presence`, während der Hilfetext daneben und das README seit D82 alle drei
versprachen. Er liest die Namen jetzt aus derselben Stelle wie das Formular.

Fünf Tests, sieben Mutationen, alle erschlagen. Voller Lauf: 1128 bestanden,
7 übersprungen.

---

### D93. Das Modell, bei dem Fragen und Dürfen zweierlei sind ✅ — `authorize`

Das fünfte und letzte Zugriffsmodell. **Bei allen anderen entscheidet dieselbe
Regel zweierlei:** Wer nicht hereindarf, darf auch nicht abonnieren. Hier nicht
— jeder darf fragen, denn das Fragen ist der Vorgang. Wer beides
zusammenwürfe, machte den Genehmigungsvorgang unerreichbar: Um zu dürfen,
müsste man schon dürfen.

Damit bekommt `PubSubSubscriptionState.Pending` zum ersten Mal einen Sinn. Der
Zustand steht seit D71 im Code, mit der Begründung, ein `pending` sehe wie eine
Zusage aus und sei keine — **auf dem Papier**, denn kein Knoten konnte einen
erzeugen. Jetzt kann einer, und an drei Stellen im Server stand
`subscription='subscribed'` als feste Zeichenkette. Jede davon war ab sofort
eine Behauptung.

Die Zusage geht durch die Tür, die D84 gebaut hat: die Abonnentenliste. Dort
stand ausdrücklich, der Zustand sei fest eingetragen und dies wäre „eine der
Stellen, die einen echten Zustand brauchen", sobald es `authorize` gibt — und
ebenso, ein `subscribed` sei „keine Anweisung, sondern eine Bestätigung". Beides
gilt jetzt anders herum, und der Grund war schon damals notiert: *Ohne
Genehmigungsvorgang gäbe es nichts, was vorher eine Frage gewesen wäre.* Jetzt
gibt es etwas. **Ein `subscribed` auf ein beantragtes Abonnement ist die Zusage,
auf ein zugesagtes bleibt es die Bestätigung von vorher** — und die meldet sich
nicht, weil sich nichts geändert hat.

## Was `authorize` nebenbei aufgedeckt hat

**Die beiläufige Zustellung fragte das Zugriffsmodell nicht.** Presence-Kontakte
bekamen jede Veröffentlichung — auch von einem Knoten, dessen Modell ihnen den
Abruf versperrte. Das Modell hielt die Tür zu und liess die Meldung durch, in
der der Eintrag vollständig steht. Für `whitelist` und `roster` war das seit
D82 und D92 falsch und fiel niemandem auf, weil beide Modelle nur am Abruf und
am Abonnieren geprüft wurden. Bei `authorize` wäre die Genehmigung damit eine
blosse Förmlichkeit gewesen: Wer wartet, hätte längst alles bekommen.

Jetzt fragt auch dieser Weg dieselbe Stelle — eine Zeile, und sie räumt drei
Modelle zugleich auf.

Und ein Test hat sein Beispiel **zum zweiten Mal verloren**, beide Male aus dem
besten Grund: „Ein Zugriffsmodell, das niemand anbietet, wird abgewiesen" hiess
bis K13 `whitelist` und bis D93 `authorize`. Beide sind jetzt angeboten, weil
sie sich durchsetzen lassen. Übrig bleibt der Fall, den es immer geben wird:
ein Name, den niemand vergeben hat.

Hundertsiebzehn Tests, zehn Mutationen, alle erschlagen. Voller Lauf: 1133
bestanden, 7 übersprungen.

**Was noch fehlt:** die Genehmigungsanfrage nach Abschnitt 8.6.1 — die
Nachricht mit dem Formular, über die ein fremder Client den Antrag anzeigt und
beantwortet. Solange es sie nicht gibt, erfährt der Eigentümer vom Antrag nur,
wenn er nachsieht. Das ist die nächste Etappe, und sie hängt nicht in der Luft:
Ohne sie wäre schon heute nichts falsch, nur unbequem.

---

### D94. Zwei Türen, ein Raum ✅ — die Genehmigungsanfrage

Der Antrag wird dem Eigentümer jetzt vorgelegt, statt auf ihn zu warten
(Abschnitt 8.6.1) — und die Antwort darauf kommt an (8.6.2).

**Zwei Türen zu derselben Entscheidung, und deshalb keine zweite
Entscheidung.** Genehmigen liess sich ein Antrag seit D93 über die
Abonnentenliste; jetzt geht es auch über das Formular, und beide Wege rufen
dieselbe Stelle im Konto auf. Zwei Türen sind trotzdem nötig: **Die Liste ist
die Sicht eines Verwalters, das Formular die eines Menschen, dem sein Client
eine Frage anzeigt.** Wer nur die Liste hätte, verlangte von jedem Client, dass
er Abonnenten verwalten kann.

Daraus folgt auch die Kopplung, die diese Etappe überhaupt zu einer macht: **Ein
Formular, das niemand beantworten kann, wäre schlimmer als keines.** Es genügt
nicht, die Frage zu stellen — wer sie stellt, muss die Antwort annehmen, sonst
genehmigt ein Mensch etwas und es geschieht nichts. Deshalb stehen Lesen und
Schreiben des Formulars in einer Datei nebeneinander.

Drei Entscheidungen im Kleinen:

- **`pubsub#allow` steht auf „nein".** Ein Formular, das schon auf ja steht,
  macht aus dem Wegklicken eine Zusage.
- **Ein „nein" auf eine Frage von vorhin beendet kein zugesagtes Abonnement.**
  Sonst entschiede die Reihenfolge zweier Nachrichten darüber, was gilt — ein
  spät eintreffendes Formular nähme jemandem etwas weg, das er längst hat.
- **Was hier nicht verstanden wird, wird nicht verschluckt.** Ein Formular über
  einen fremden Knoten oder eines, das sich nicht lesen lässt, geht seinen
  gewöhnlichen Weg als Nachricht weiter. Eine Nachricht spurlos verschwinden zu
  lassen ist die teuerste Art, höflich zu sein.

  Der Test dazu hat das zuerst nicht geprüft, und die Mutation, die die
  Knotenprüfung entfernt, hat überlebt: Er schickte das fremde Formular an das
  Konto des Absenders, wo es ohnehin nichts bewirken konnte. **Beide Fassungen
  taten dasselbe — nämlich nichts.** Jetzt geht es an einen Dritten, und der
  Unterschied ist zu sehen: Ohne die Prüfung kommt es bei ihm nie an.

Die Anfrage selbst ist ein `headline` und wird nicht aufbewahrt. **Sie ist eine
Bequemlichkeit und kein Träger des Zustands:** Der Antrag steht im Abonnement,
die Nachricht sagt nur, dass es ihn gibt. Wer offline war, verpasst die
Nachricht und nicht den Antrag — und eine aufbewahrte wäre die schlechtere
Auskunft, weil sie einen Stand von damals beschriebe, der längst beschieden sein
kann.

Hundertzweiundzwanzig Tests, sieben Mutationen, alle erschlagen. Voller Lauf:
1138 bestanden, 7 übersprungen.

Damit ist XEP-0060 in dem Umfang fertig, den dieses Projekt braucht: alle fünf
Zugriffsmodelle, Rollen, Abonnements samt Kennungen und Einstellungen,
Knotenverwaltung, Rücknahme und Genehmigung.

---

### D95. Zwei Fragen, ein Merkmal ✅ — `authorize` auf der Clientseite

Die Clientseite von D93/D94, und ihr Kern ist eine Zeile, die seit D71 richtig
aussah: **Ein `pending` wurde verworfen.** Der Aufrufer bekam `null` — dieselbe
Antwort wie auf eine Absage.

Das war die richtige Antwort auf „bin ich abonniert" und die falsche auf **„was
habe ich beantragt"**. Zwei Fragen hingen an einem Merkmal. Und die zweite ist
nicht nebensächlich: **Die Kennung des Antrags kommt vom Dienst.** Ohne sie kann
der Client die Zusage, die später als Meldung eintrifft, keiner eigenen Frage
zuordnen — dazwischen liegt ein Mensch, der sie beantwortet, und deshalb kommt
sie nicht als Antwort auf das IQ.

Eingetragen wird das `pending` jetzt also, aber als das, was es ist:
`IsSubscribed` zählt Zugesagtes und nicht Eingetragenes. Die Verwechslung, vor
der D71 warnte, bleibt ausgeschlossen — nur an einer anderen Stelle.

**Die Regel aus D86 gilt weiter, und sie wird genauer.** Dort hiess es: Eine
Zusage kommt auf eine Anfrage, wer sie ungefragt annimmt, lässt sich von einem
Dienst anmelden. Richtig — nur gibt es jetzt einen Fall, in dem sie verlangt
war, und den erkennt dieser Client an seinem **offenen Antrag**: Zusagen ohne
einen solchen werden weiterhin abgewiesen.

Auf der anderen Seite legt der Client dem Eigentümer den Antrag vor und
beantwortet ihn — **angezeigt und nicht beantwortet**: Wer zusagt, ist ein
Mensch. Ein Client, der von sich aus antwortete, entschiede über fremden Zugang
nach einer Regel, die niemand gesehen hat.

Eine Mutation hat überlebt und wieder auf den Test gezeigt: „zugesagt wird auch,
was schon zugesagt ist" ging durch, weil die unverlangte Zusage im Test eine
**fremde Kennung** trug — abgewiesen wurde sie daran und nicht an der Regel. Der
Test schickt jetzt beides: die erfundene Kennung und die richtige. **Zugesagt
ist zugesagt** — eine zweite Zusage ist keine Änderung und meldet sich nicht.

Dreiundsechzig Tests, sieben Mutationen, alle erschlagen. Voller Lauf: 1141
bestanden, 7 übersprungen.

---

### D96. Drei Listen derselben Befehle ✅ — die Konsole im README

Nachgezogen, und zwar in beide Richtungen abgeglichen: **kein Befehl im Code,
den das README nicht nennt; keiner im README, den es nicht gibt.** Die
PubSub-Unterbefehle, die obersten Befehle und `/omemo` sind je einmal
durchgezählt.

Es gibt sie nämlich **dreimal**: in `PrintHelp`, in der Hilfe von `/pubsub` und
im README. Drei Listen derselben Sache halten sich, bis eine von ihnen falsch
wird — und genau das war passiert:

- **`/fix` fehlte im README ganz.** Der Befehl gibt es seit D60, die
  Merkmalstabelle nennt ihn („In der Konsole `/fix <text>`"), die Konsolenhilfe
  auch — nur die Befehlsliste nicht, also gerade die Stelle, an der jemand
  nachsieht, der wissen will, was er tippen kann.
- **`/pubsub access` versprach drei Modelle, `create` kannte zwei.** Das erste
  war seit D92 behoben; beim zweiten stand dieselbe Verkürzung noch im Text.
  <b>Wer `whitelist` schrieb, bekam einen offenen Knoten und eine
  Erfolgsmeldung</b> — die stillste Art, eine Einstellung zu verlieren. Jetzt
  liest auch `create` die Namen aus der Stelle, die auch das Formular liest.
- Zwei Aliase (`rostergroups`, `authorize`, `fp`) waren nirgends vermerkt.

Und die Kurzhilfe sagt jetzt, dass sie eine ist: Die fünf PubSub-Zeilen in
`/help` sahen aus wie die ganze Menge; es sind fünf von zwanzig.

**Warum das überhaupt auseinanderlaufen konnte:** Die Konsole hat keine Tests.
Sie ist die einzige Ecke dieses Projekts, in der eine Behauptung ohne Deckung
niemandem auffällt — kein Mutant kann hier etwas erschlagen, weil nichts
hinsieht. Der Abgleich lief deshalb als Wegwerf-Skript über beide Dateien;
es als Test einzubauen hiesse, den Pfad zweier Textdateien in die Testsammlung
zu schreiben, und der Umzug nach `HermodTests` steht noch aus.

Voller Lauf: 1141 bestanden, 7 übersprungen.

### D97. Das Protokoll zieht aus ✅ — Ratatoskr

Der Umzug selbst kam von aussen: Client, Server, XEPs und die Testsammlung
liegen jetzt in **Ratatoskr**, einem eigenen Repository unter `libs/`, mit dem
Namensraum `org.GraphDefined.Vanaheimr.Ratatoskr`. Hier bleiben die Konsole,
ihre Tests und die beiden fremden Gegenstellen in `tools/`.

Dieser Eintrag handelt von dem, was so ein Umzug hinter sich herzieht. **Vier
Dinge waren danach kaputt, und drei davon hätten sich nicht von selbst
gemeldet.**

**Der Übersetzer meldete zwei Zeilen, gemeint waren vier.** `IPPort`,
`IPv4Address` und `IPSocket` kommen von Hermod, und niemand hatte je ein
`using` dafür geschrieben — der Namensraum lag *unterhalb* von Hermod, die
Typen kamen über die Verschachtelung herein. Zwei Dateien in der Bibliothek,
zwei in den Föderationstests. Das ist die freundliche Sorte Fehler: Sie steht
im Bauprotokoll.

**Mit derselben Verschachtelung ist eine Begründung verfallen.** Am Alias
`using IPAddress = System.Net.IPAddress;` stand, er müsse im Rumpf der
Namespace-Deklaration stehen, weil ein Namespace-Member gegen einen Alias der
Compilation Unit gewinnt. Das stimmte, solange der Namensraum unter Hermod lag.
Jetzt kommt Hermods `IPAddress` nur noch über eine `using`-Direktive, und gegen
die gewinnt der Alias — er steht deshalb wieder oben bei den anderen. Der
Kommentar sagt jetzt beides: warum es den Alias braucht, und warum er nicht
mehr in den Rumpf muss.

**Drei Tests haben sich seither stillschweigend übersprungen.** Das
OMEMO-Orakel wurde gesucht, indem von der Ausgabe aus nach oben gelaufen wurde,
bis `WORKPLAN.md` dalag — und von dort aus unter
`Jabber.Tests/XMPP/XEPs/Orakel/`. Beide Marken gehören dem Programm und nicht
der Bibliothek, und beide waren nach dem Umzug falsch. Die Meldung dazu lautete
**„Das Orakel ist nicht erreichbar (python-omemo in WSL …)"** — sie klingt nach
fehlender Referenzimplementierung und nicht nach einem falschen Pfad. Das ist
genau der Unterschied zwischen **7 und 10 Übersprungenen**, also zwischen „die
Gegenstelle stand bereit" und „die Gegenstelle wurde nie gefragt".

Gesucht wird jetzt nach dem Skript selbst, und **fehlt es, ist der Lauf rot und
nicht übersprungen**: Das Orakel liegt in demselben Projekt wie die Tests, ein
fehlendes ist also ein kaputter Checkout. Übersprungen wird nur noch, was
wirklich an der Umgebung liegt.

**Drei Erzeugerskripte schrieben ins Leere.** `tools/unicode/` und
`tools/stringprep/` holen die Unicode-Datei beziehungsweise RFC 3454 und
schreiben daraus `Common/BidiClasses.cs`, `Common/ContextTables.cs` und
`Auth/StringPrepTables.cs`. Ihr Ziel stand als `parents[2] / "Jabber" / …` im
Quelltext. Beim nächsten Unicode-Wechsel hätten sie ein frisches
`Jabber/Common/BidiClasses.cs` neben die Konsole gelegt, „fertig" gemeldet, und
die Tabelle, die tatsächlich übersetzt wird, wäre die alte geblieben. Sie sind
mit ihrem Erzeugnis nach `libs/Ratatoskr/tools/` gezogen.

**Und zwei Abhängigkeiten standen am falschen Ort — beide funktionierten
trotzdem.** BouncyCastle stand in `Jabber.csproj`, wo seit dem Umzug kein OMEMO
mehr liegt; in `Ratatoskr.csproj` stand weder es noch
`Microsoft.Extensions.Logging`. Übersetzt hat es dennoch, weil Hermod beide
mitbringt. Genau davor warnte der Kommentar, der über dem Paket stand: *wer
eine transitive Abhängigkeit direkt benutzt, verliert sie in dem Augenblick, in
dem der Vorbesitzer sie ablegt.* Der Kommentar ist mitgewandert, das Paket
auch; dieselbe Begründung steht jetzt am ausdrücklichen `ProjectReference` der
Föderationstests auf Hermod.

**Ein Provisorium hat sich erledigt.** In `Jabber.csproj` standen zwei
`InternalsVisibleTo`-Namen — der zweite „für den Fall, dass die Tests später
nach `HermodTests` wandern". Sie sind gewandert, nur woandershin. Jetzt steht
einer, in `Ratatoskr.csproj`, und er nennt die Assembly, die es gibt.

**Das README ist geteilt, nicht verschoben.** Das grosse bleibt hier: Es
beschreibt beides zusammen, weil beides zusammen entstanden ist und die
Entscheidungen dahinter in diesem Arbeitsplan stehen. Ratatoskr bekommt daraus
den Auszug für den, der die Bibliothek ohne diese Konsole benutzt — XEPs,
RFC-Konformität, Server, Testvektoren, OMEMO. **Was den Prüfungen gegen fremde
Gegenstellen gilt, bleibt hier**, denn hier liegen die Aufbauten. Nachgezogen
sind ausserdem die Pfade in beiden `setup.sh`, die noch auf `Jabber.Tests`
zeigten.

**Keine Mutationen für diesen Schritt.** Es gibt keinen neuen Produktivcode —
bis auf die eine Zeile, die entscheidet, wo das Orakel gesucht wird, und die
ist dadurch belegt, dass drei Tests wieder laufen statt sich zu überspringen.

Voller Lauf: 1133 bestanden, 7 übersprungen; dazu 8 für die Konsole.

---

## Später

### Testsammlung
- ~~**`AFailureWhileHandlingAFrame_IsReported` wackelt seit D68 unter Last.**~~
  Behoben in D69, und der Grund war kein Zeitproblem, sondern ein Wettlauf:
  Nach dem Verbindungsaufbau ist noch etwas unterwegs — die erste Presence,
  die Antwort auf den Roster-Abruf. Fiel der Testschalter, während davon noch
  etwas beim Server ankam, scheiterte *jener* Rahmen zuerst, der Server
  beendete den Stream, und die Nachricht mit der gesuchten Kennung ging nie
  hinaus. Der Test wartete dann zehn Sekunden auf eine Meldung, die es nicht
  mehr geben konnte.
  **Der Wettlauf war immer da; sichtbar wurde er erst, als die OMEMO-Tests die
  Maschine genug beschäftigten** — zwei von vier vollen Läufen fielen darüber.
  Jetzt wartet der Test, bis vom Client nichts mehr nachkommt, statt bis
  `ConnectAsync` zurückkehrt. **Ein Test, der die Hälfte der Zeit fällt, misst
  nichts mehr** — und die erste Vermutung („zu knapp bemessen") war falsch: Es
  half kein Warten, weil die Meldung nicht spät kam, sondern gar nicht.
- ~~**`NonzasDoNotAdvanceTheCount` gegen Prosody scheitert gelegentlich** — in D34
  aufgefallen, ein Fehlschlag in einem Vollauf. Der Mitschnitt liegt vor:

  ```
  Wir haben Nonzas mitgezählt.  Expected: 6  But was: 8
  Prosody hat andere Nonzas mitgezählt als wir.  Expected: 8  But was: 6
  ```

  Der Client hatte also **zwei** ausgehende Stanzas mehr gezählt als die drei,
  die der Test schickt; Prosody bestätigte die erwarteten sechs. Beide
  Zusicherungen fallen zusammen, weil beide dieselbe Zahl vergleichen.

  Eine naheliegende Erklärung ist bereits **widerlegt**: Der Test schickt an
  sich selbst, die Nachrichten kommen also zurück — aber die automatischen
  Antworten des Clients (XEP-0184, XEP-0333) verlangen ein `<request/>` bzw.
  `<markable/>` im Rahmen, und die Testnachrichten tragen nur einen `<body>`.
  Sie lösen nichts aus.

  Offen ist damit, **welche zwei Stanzas** mitgezählt wurden. Seit D35
  schneidet der Test den Ausgang mit und legt ihn der Meldung bei — beim
  nächsten Vorfall steht dort, was hinausging, statt einer Zahl. Zwanzig
  gezielte Ausführungen konnten ihn nicht wiederholen (siehe D34, D35)~~
  ✅ erledigt in D55 — und die Frage nach den zwei Stanzas war die falsche:
  Prosody hatte richtig gezählt und wir auch. Der Test verglich eine Zahl, wo
  Abschnitt 2 eine Beziehung meint
- ~~`TheStreamSurvivesABrokenConnection` (D16) ist seit D33 **nicht mehr
  reproduzierbar** und der damalige Verdacht widerlegt: vierzig Ausführungen
  zwischen 519 und 669 ms bei 15 Sekunden Frist. Ob D30 ihn beseitigt hat, ist
  eine passende Erklärung und kein Nachweis. Tritt er wieder auf, nennt die
  Meldung jetzt den Verlauf — dann ist er in einem Anlauf zu klären (siehe D33)~~
  ✅ erledigt in D56 — der Verdacht war **nicht** widerlegt, die Messung konnte
  ihn gar nicht widerlegen: Alle vierzig Durchgänge kamen beim ersten Anlauf
  durch, und die Frist von 15 Sekunden lag nur 5,7 Sekunden über den 9,3, die
  der Client allein mit Warten verbringen darf

### Server (`libs/Ratatoskr/Ratatoskr/Server/`)
Die grossen Brocken stehen oben unter [S1 bis S4](#der-server-soll-ein-richtiger-server-werden).
Was dort nicht auftauchte und trotzdem anstand, ist in D49 bis D53
abgearbeitet: `<resume/>` beantworten (war seit R1 erledigt, offen blieb das
`h` im `<failed/>` — D49), SCRAM anbieten (war seit S2 erledigt, offen blieb
das unbekannte Konto — D50) und Stanza-Fehler ohne Schalter (D51 bis D53).
**Hier steht derzeit nichts offen.**

### Struktur
- ~~`Jabber.Tests/XMPP/` nach `HermodTests/XMPP/` verschieben. Bewusst
  aufgeschoben; Namespaces, Ordnerschnitt und der doppelte
  `InternalsVisibleTo`-Eintrag in `Jabber.csproj` sind bereits darauf ausgelegt,
  dass das eine Kopie wird.~~ ✅ erledigt in D97 — nur anders als geplant: Nicht
  die Testsammlung ist zu Hermod gewandert, sondern das ganze Protokoll in eine
  eigene Bibliothek (**Ratatoskr**), und die Tests mit ihm. Die Vorarbeit hat
  trotzdem getragen: Ordnerschnitt und Namensraum liessen sich unverändert
  übernehmen. Der doppelte `InternalsVisibleTo` ist damit einer geworden.
- ~~Konsolen-UI und Logger trennen: der Standard-Konsolenlogger schreibt in
  dieselbe Konsole wie die Eingabezeile und zerlegt den Prompt. Ein eigener
  `ILoggerProvider` über die synchronisierte Ausgabe wäre die saubere Lösung.~~
  ✅ erledigt in D58 — die synchronisierte Ausgabe gab es dabei noch gar nicht:
  Die Ereignisbehandlung klammerte jede Ausgabe von Hand, ohne Sperre
- ~~Ungenutzte öffentliche Member entscheiden: benutzen oder streichen. Liste in
  [Jabber/README.md](Jabber/README.md).~~ ✅ erledigt in D57

---

## Optional

Was hier steht, ist nicht falsch und nicht dringend: Es fehlt niemandem, solange
niemand es benutzt. Ein Punkt wandert von hier nach „Später", sobald es einen
Anwendungsfall gibt, an dem sich die Umsetzung prüfen lässt.

- ~~**XEP-0060 — Publish-Subscribe.**~~ Erledigt in D70 und D71. Die Begründung,
  warum der Punkt hier stand, war am Ende der Weg zur Umsetzung: Es gab keinen
  Ablauf, an dem sich die Korrelation prüfen liess, weil der Testserver auf
  jedes `subscribe` `<service-unavailable/>` sagte. Also erst der Server (D70),
  dann der Client (D71) — und der eigentliche Fund lag dazwischen: Ein
  bestätigtes Abonnement brachte gar nichts ein, weil der Spoofing-Schutz jede
  PEP-Meldung verwarf.

  **Was das über die Liste sagt:** „Kein Anwendungsfall" hiess hier nicht, dass
  niemand es braucht, sondern dass keine Gegenstelle es beantworten konnte.
  Das ist ein Grund zu warten, aber ein anderer als der, der hier stand

- **TCP-Transport für den Client.** Dieser Client spricht XMPP über WebSocket
  (RFC 7395), und die Server, gegen die er läuft, bieten ihn an — Prosody,
  ejabberd und der eigene Testserver. Solange das so bleibt, fehlt der
  TCP-Transport niemandem.

  Der Umfang ist seit D34 gemessen: Der Client fasst den WebSocket an neun
  Stellen unmittelbar an (Verbinden, Senden, die beiden Empfangspfade,
  Abbruch), es bräuchte also eine Transportabstraktion, dazu clientseitiges
  STARTTLS und die TCP-Rahmung. `XmlStreamSplitter` und die
  STARTTLS-Aushandlung gibt es auf der S2S-Seite bereits, sind dort aber für
  `jabber:server` geformt. `CreateTcp` — die Fabrikmethode, die eine
  `tcp://`-URI erzeugte und dabei funktionslos war — ist in D34 entfernt
  worden; eine öffentliche Methode, die nicht funktionieren kann, ist
  schlechter als keine.

  **Der Rückweg:** ein Server, den dieser Client erreichen soll und der keinen
  WebSocket-Endpunkt anbietet. Dann ist der Anwendungsfall da, und mit ihm die
  Gegenprobe — Prosody hört auf 127.0.0.1:5222 und wäre der Prüfstein
  (siehe D34, D48)

---

## Bewusst nicht umgesetzt

Was hier steht, ist entschieden und wartet nicht auf Gelegenheit.

- **XEP-0013 — Flexible Offline Message Retrieval.** Von der XSF als
  *Deprecated* geführt (Fassung 1.3, 2021-05-04): „Implementation of the
  protocol described herein is not recommended." Die Offline-Ablage bleibt beim
  automatischen Nachreichen nach RFC 6121, Abschnitt 8.5.2.2.1, und XEP-0160.
  Einen Nachfolger benennt das Dokument nicht; das gezielte Nachlesen läge bei
  XEP-0313 (MAM), das aber ein Archiv beschreibt und keine Ablage (siehe D37)

---

## Arbeitsweise

Was sich in diesem Projekt bewährt hat und beibehalten werden sollte:

- **Fixes durch Mutation absichern.** Grün allein beweist nichts — den Fix
  zurückdrehen und prüfen, dass genau die zuständigen Tests rot werden. So sind
  alle bisherigen Korrekturen belegt.
- **Gegen veröffentlichte Vektoren rechnen, nicht gegen sich selbst.** SCRAM und
  der Caps-Hash sind gegen RFC 5802/7677 und XEP-0115 geprüft; zwei Defekte kamen
  überhaupt erst dadurch ans Licht.
- **Testserver unabhängig implementieren.** `XMPPServer` zählt XEP-0198 bewusst
  mit eigener Logik. Benutzten beide Seiten dieselbe Hilfsfunktion, bliebe ein
  gemeinsamer Denkfehler unsichtbar.
