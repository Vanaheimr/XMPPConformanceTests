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
same consideration as at the kept subscription requests from S7.

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

### D28. An abort is no offence ✅ — section 6.4.4

The point from D26: an `<abort/>` out of the SASL negotiation got a stream error
since D26. Word for word that was not wrong — the server did not support the
element, and section 4.9.3.24 fits every element it does not know. It was the
poorer of two answers.

**The difference is no subtlety.** The abort is an *intended* step of the
negotiation, no offence against the protocol: section 6.4.4 provides for it
expressly and demands `<failure><aborted/></failure>`. Whoever answers it with the
end of the stream forces the client to a new connection for something the RFC
provides for within the existing one.

The half SCRAM exchange is discarded in doing so, and that is the actual content
of an abort. Would it stay lying, it could be carried to the end with a
`<response/>` pushed in later — the abort would then be a polite formula and no
statement. A test of its own holds that fast.

**The S2S stream had the same gap, and it is my own from D27.** Before the
strictness an `<abort/>` stayed lying there, after it, it ended the stream. The
same answer has been drawn along — with one difference: there is nothing to
discard, because SASL EXTERNAL is a single move and knows no half exchange. And
whoever has dialled themselves answers no abort; they would be the one who sends
it.

**The lesson belongs to D26 and D27 and closes them:** whoever makes a switch
strict inherits every answer it does not yet know. Before, the unknown fell out
silently at the back, and every missing answer was a gap without consequences;
afterwards every missing answer is an ended stream. The strictness was right — but
it turns omissions into damage, and the list of what is still missing belongs
worked through from then on and not merely kept.

Checked over a raw `ClientWebSocket` after the model from
`WebSocketFederationTests`: the abort belongs **in the middle** of the
negotiation, and there the real client holds its own conversation. Only by hand
can a half-begun SCRAM exchange be produced at all.

Five mutations, all struck down — two of them only after a correction to the
tests.

The one was a gap: for the counter-direction — an initiator that gets an abort —
there was no test. Instead of noting it as a known survivor, the test is added.

**The other is the more instructive, and it is again the trap from D20 and D24.**
The mutation lets the half SCRAM exchange stand after the abort — and my test for
it passed all the same. It pushed a **nonsensical** `<response/>` in after the
abort and checked for `not-authorized`. Only a nonsensical answer yields
`not-authorized` whether the exchange was discarded or not: both worlds give the
same answer, and the test checked nothing.

Only an answer that **would get through** separates the cases. It is now built
with the real `SCRAMAuthenticator` of the client — with it the exchange left lying
led to `<success/>`, with a discarded one to a refusal. The test has since also
checked that **no** `<success/>` comes, and that is the half it is actually about.

The pattern thereby repeats itself for the third time, and it is always the same:
a test produces a situation in which the right and the wrong version answer the
same. It then looks like a proof and is none. The counter-check for it is cheap
and belongs to the habit — **which answer would the server give without this
line?** Is it the same, then the test does not check the line.

### D29. A known namespace does not make the element known ✅

The last place in the house at which a frame still fell out silently at the back:
the branch for XEP-0198 checked the **namespace** and dropped everything in it
that it did not know.

Section 4.9.3.24 names both expressly — "because the receiving entity does not
understand the namespace **or** because the receiving entity does not understand
the element name for the applicable namespace". The second half-sentence is
exactly this case, and it was the only one that still stood open.

**The more interesting of the two checked cases is not the invented element but
`<enabled/>`.** That is a *real* element from XEP-0198 — only the server sends it
to the client and not the other way round. Known does not mean "known in this
direction", and a branch that looks only at the namespace cannot make this
difference at all.

Implemented it is as a return value instead of as a second check: the branch now
says whether it was responsible, and what it does not know falls further down and
gets the same answer as every other unknown element. A second list of the known
names beside the first would have been the obvious solution and the poorer one —
two enumerations that can run apart, for a question the branch answers already
anyway.

Six mutations, all struck down — one each for each of the four branches, the
switch itself and the fallback at the end. One of them only after an added test,
and that is the actual find of this point.

**The `<a/>` branch was reached by no test.** The mutation declared it not
responsible — with which the acknowledgement of the client would have ended the
stream since this change —, and not a single test fell over it. Over a real
connection a client has never sent an `<a/>` to the server: checked was only the
counter for itself, in `StanzaCountingTests`, never its way through the server.

**The gap is older than the line that made it visible.** The branch gave nothing
back before; whether it ran was not to be seen from outside. Only the return value
made it observable — and a mutation on it could come out in the first place. A
branch whose effect nobody observes looks like one nobody needs.

That is the same pattern as in D26, only the other way round: there a new answer
made an old negligence visible, here a new return value makes an old gap in the
tests visible. **Observability is no side effect of a change, but sometimes its
greater part.**

**And the point from D25 has struck for the fourth time today.** Every mutation
that breaks the negotiation — `set` out of the list of types (D25), `iq` out of
the switch (D26), `<abort/>` without an answer, `<enable/>` as unhandled (here) —
lets the run **hang** instead of fail: `XMPPConnection.ConnectAsync` waits without
a deadline of its own for an answer that never comes. Four times the same finding
from four different directions is no coincidence any more, but a property.

The handling of it is practised by now and has itself cost two lessons:
`--blame-hang` makes a failure out of the hanger, and **the filter stays unchanged
in doing so** — a narrowing made a surviving mutant out of a struck-down one in
D25. Shot down is the script and not the test process; in D26 the old pass would
otherwise have run on beside the new one. At the breaking off here the file again
carried a mutation, and found it — for the second time — the comparison of hashes
against the backup and not my attention.

With that the series D26 to D29 is closed: first the switch guessed (D26), then it
became strict (D26, D27), then came the answers it owed through the strictness
(D28, D29). **The arc is the actual lesson.** A negligence that does nothing costs
nothing — until a tightening beside it turns it into damage. Whoever tightens
thereby takes on everything as well that was missing without consequence before.

### D30. Silence does not arrive ✅ — and my note was wrong

The point that has struck five times today: every mutation that breaks the
negotiation let the run **hang** instead of fail. Five times the same finding from
five directions is no observation any more, but a property.

**And the first act was to refute my own note.** It read since D25:
"`ConnectAsync` waits without a deadline of its own for the answer to the resource
binding". The binding does very well have a deadline — `SendIqAsync` has always
set it, ten seconds. Without a deadline were the **reading steps** before it:
stream header, features and every SASL round go over `ReceiveStanzaAsync`, and
that waited on the token of the caller alone.

The same lesson as in D19 and D23, this time at a diagnosis instead of at a list:
a note written out of one's head is no stocktaking. Had I believed it, I would have
put a deadline at a place that has one already, and kept the error.

**What a failure does not produce is silence.** An error arrives, a closed socket
arrives — both bring the negotiation to a conclusion. Silence does not arrive.
This is why the case could be reproduced with none of the existing test switches,
and this is why there is now `XMPPServer.AnswerStreamOpen`: a far side that accepts
the connection and then says nothing more. No invented case — a server behind a
state table that has forgotten the way back behaves exactly like that, and it is
the most unpleasant outcome of all, because the caller never learns that something
is not right.

The deadline holds for the **step** and not for the single reading: a frame that
arrives in pieces may not take longer together than one in one piece. And it names
what was waited for — "for the stream header", "for the SCRAM challenge". An
expired deadline without this information only shifts the search: the caller then
knows that something did not come, but not what. On exactly that I have lost time
several times today.

Four mutations, all struck down — the deadline itself, both halves of the message
and the new test switch. One broke off at first because **PowerShell 5.1 reads a
script without a BOM in the ANSI code page** and the "ü" in the search pattern
arrived mangled. The mutation scripts now carry a BOM. At least this failure was
loud; the silent ones from D25 were dearer.

**A second error of mine sat in my own test.** It expected an exception out of
`ConnectAsync` at first — that does not come, because `ConnectInternalAsync`
catches every error of connecting and reports over `OnError` and the state. That is
the build of the house and was never the defect: the defect was that the call **did
not come back at all**. Checked is now the returning and the report. Whether a
silently returning `ConnectAsync` is a good interface is another question, concerns
every caller and stands under "Later".

### D31. A call that says nothing ✅

The point from D30, and it was noted expressly as a **decision of design** and not
as an error: `ConnectAsync` returned silently at a failed setup. The error went to
`OnError` and to the state — whoever had subscribed to nothing saw no difference
between succeeded and failed and went on working on a connection that does not
exist.

The same evil as in D30, one level higher: **there no answer came at all, here
comes one that says nothing.**

A return value would not have fixed it. One can be ignored, and an ignored return
value is silence again — exactly the property it is about. So the call throws.

**Thrown is the original error**, not a shell around it: a wrong password stays an
`AuthenticationException`, a timeout an `XMPPProtocolException`, and the caller
distinguishes them without reading in a message. The stack stays the one of the
error and not the one of this place.

**And only the express call throws.** The attempt at reconnecting in the background
runs through the same `ConnectInternalAsync`, but has no caller it could owe
anything to; it still reports over events. This is why the decision stands in
`ConnectAsync` and not where the error arises — the difference is not the type of
the error, but whether somebody is waiting for an answer.

**The price was measurable, and it is the actual yield.** Eleven tests fell, and it
was exactly the eleven that check an expected failure: wrong password, unknown
account, falsified server signature, refused certificate, refused binding,
protection against downgrade. All eleven stood on a mere `await` and the assurances
after it — which was possible only because the call kept silent.

They now run over a common helper, `FailingConnectAsync`, that makes the
expectation express: **here it has to fail.** With that the eleven check one
assurance more than before — that the failure arrives at the caller at all. The
radius of a change of design is seldom only effort; here it was the list of the
places that lived off the silent returning.

Five mutations, four struck down. The fifth is a **named exception**: the resetting
of `_lastConnectError` at the beginning is unobservable today. The field is read
only when the state is not `Connected` — and there leads no way there that would
not have run through one of the two `catch` before that set it freshly. The line
stays standing all the same: it prevents a future path that fails without a `catch`
from throwing an error from the day before yesterday. A precaution, no effect —
like the shortcut over the empty offline store from D14.

### D32. The failure without a name had one ✅

The open point from D29: one full run reported **one** failure, the next identical
run was green, and the name sat in the output that was thrown away.

Find it again could not be done — repeat it could. Three full runs under the
conditions of back then (ejabberd gone, 16 skipped), this time recorded completely.
The first run had it:

```
Fehler AnAckFromTheClient_IsProcessedAndClearsTheQueue
  Expected: less than 2
  But was:  3
```

**It was my own test from D29** — the one that closed the gap in the `<a/>` branch.
It had stood in the tree for a day, and the unexplained failure came in the same
pass; the suspicion was therefore obvious and was nevertheless only a suspicion
until the recording named it.

**The error is an error of measurement and no race in the usual sense.** The test
checked: "after the acknowledgement fewer stanzas are open than before". An
acknowledgement however says nothing about a *number*. It says: **everything up to
this sequence number is settled.** What comes in afterwards — Bob's presence, a
few milliseconds later — lets the queue grow again, and the number rises although
the acknowledgement did exactly the right thing.

Checked is now the sequence number: no open stanza with `Seq <= h`. With that
whatever wants may arrive after the acknowledgement.

**And the counter-check was the more important part.** A de-flaked test easily
becomes one that checks nothing any more — the most convenient way to get rid of a
flaky candidate is to take its assurance away from it. This is why the mutation
from D29 (`<a/>` counts as unhandled) ran once more against the new version: it
still falls. De-flaked, not defused.

**The confirming runs then showed a second, different flaky candidate** — and this
time the recording lay ready at once: `AFailureWhileHandlingAFrame_IsReported`
reported

```
Expected: String containing "ausloeser"
But was:  "<presence xmlns='jabber:client'><c xmlns='...caps' .../></presence>"
```

The same error of measurement in a different shape. The test throws the switch for
failure and sends a frame; taken it then had the **first** report at all — and that
was occasionally the automatic login presence of the client, which was still on its
way when the switch was thrown. What is reported first is decided by the passing of
time; what the test wants to know is another question. Sought is now the report
**about our own frame**.

Both times the same counter-check: a de-flaked test easily becomes one that checks
nothing any more, and the most convenient way to get rid of a flaky one is to take
its assurance away from it. This is why against every new version the mutation ran
that it is to hold — `<a/>` counts as unhandled (D29) and the frame is not reported
along (D18). Both still fall.

Two things about the way of working, both my own fault: I changed the test file
**while** the second hunting run was going — its result was thereby worthless, and
I broke the hunt off instead of using it. That is the same negligence as in D26,
only without damage, because this time it came out at once. And the find itself
hangs solely on full runs going completely into a file since D29: **a failure
without a name is one that cannot be found again** — the rule that arose out of the
case has solved the case.

**And a number that gives pause:** in seven full runs on this evening two different
tests fell once each. Both were errors of measurement in tests I wrote myself, both
arose through something concurrent — a presence — getting between measuring and
checking. The suspicion is obvious that they are not the last; the hunt therefore
stays a repeatable tool and no one-off action.

### D33. A supposition that did not hold ✅

The last open flaky candidate, from D16: `TheStreamSurvivesABrokenConnection`
against a foreign server failed in one of four full runs with a timeout, on its own
however green four out of four times. The note named a suspicion — "15 seconds for
reconnecting together with resumption are tight under the load of the full run with
exponential backoff" — and the express obligation to clear up **before** a change
of the waiting time whether the backoff really slows things down.

**Cleared up is now that the suspicion does not hold.** Twenty targeted passes,
forty executions against both far sides, every single one between **519 and 669
milliseconds** — a distribution without any outlier, at a deadline of 15 seconds.
That is about twenty-five-fold air and no tight budget. The deadline therefore stays
unchanged; to raise it would have feigned a finding that does not exist.

Repeat the failure could not be done — not in the seven full runs from D32 either.
Possible is that D30 removed it along the way: before D30 a reading step of the
negotiation could hang **without a bound**, and an attempt at reconnecting that got
stuck there would have yielded exactly this picture — deadline expired, no progress.
That is an explanation that fits the symptom, and no proof; it stands here as what
it is.

**What remains is the precaution, and that is the actual yield.** At the failing the
message until now said only "timeout at the waiting for: the resumed stream" —
nothing about how far the client had got. On exactly that the case in D16 failed.
The counter now records the history: every change of state and every reported error.
Forced and reproduced it looks like this:

```
The stream was not resumed within 15 seconds.
History: Connected->Disconnected
```

— and one sees at once that the client did not even try. At a real incident the
whole chain together with errors would stand there.

**A failure that explains itself costs writing work once; one that does not costs
an investigation every time.** In D29 that cost me one lost diagnosis, in D16 one
that stayed open for sixteen points.

Cleared up by the way: at the adding of the `using` I had written a CRLF into an LF
file — exactly the mixing I had checked for in D26 and had this time produced
myself. It came out because the search pattern for the counter-check did not fit;
the file is thoroughly LF again.

### D34. A factory that cannot build anything ✅

`XMPPConnection.CreateTcp` created a `tcp://` URI that `ClientWebSocket` refuses.
The note had stood for a long time and left two ways open: implement it really or
remove it.

**The stocktaking prepared the decision, did not replace it.** The method has **zero
callers** — not in the tests, not in `Program.cs`, nowhere. It is public surface that
demonstrably does not work, and its own comment said that always: "NOT functional".

The extent of the alternative was likewise to be measured: the client touches the
WebSocket directly at **nine** places — connecting, sending, the two receiving paths,
breaking off. A real TCP transport therefore demands an abstraction of the
transport, to that STARTTLS on the client side and the TCP framing. The building
blocks exist (`XmlStreamSplitter`, STARTTLS), but on the S2S side and shaped for
`jabber:server`. That is an undertaking of its own and no repair.

Removed. **A public method that cannot work is worse than none** — it looks like an
offer, costs the caller an attempt and delivers an object that fails at the first
use. As long as nobody calls it, removing it is the cheapest honest step.

The TCP transport stays standing under "Later", now with the measured extent and
the goal of the check: Prosody listens on 127.0.0.1:5222, so a real transport would
be provable against a foreign far side.

**Without a mutation test, and that is no omission here.** No line of behaviour is
added that one could turn round; the check of a removal is the question whether
somebody used it, and that the compiler and the full run answer. Both say no.

### D35. Numbers never say what is missing ✅

At the check run to D34 a third flaky candidate came out —
`NonzasDoNotAdvanceTheCount` against Prosody, one failure in one full run:

```
Wir haben Nonzas mitgezählt.  Expected: 6  But was: 8
```

Two outgoing stanzas more than the test has sent. **Which two, the number does not
say** — and with that I stood before the same dead end as in D16 and D29.

An obvious explanation is checked and **refuted**: the test sends to itself, so the
messages come back, and the suspicion lay on an automatic answer of the client. That
however demands a `<request/>` (XEP-0184) or a `<markable/>` (XEP-0333) in the
frame, and the test messages carry only a `<body>`. They set nothing off. A
suspicion that can be refuted in five minutes is the cheapest way of getting rid of
it.

Reproduce it could not be done: twenty executions against both far sides, all green,
with a very narrow spread. Exactly the situation from D33 — and therefore the same
answer. The test now records **what actually goes out** and attaches it to the
message. At the next incident the two supernumerary stanzas stand there in plain
text, instead of only a number remaining again.

With that it is three times the same pattern in one session: D16, D29 and now here.
**An assurance about a number says that something is not right, and never what.**
Where the object is cheap to record along — the history, the frame, the recording —
it belongs in the message, and before the first failure comes and not after it.

### D36. The information does not hang on who asks ✅

The point from D16: an IQ request from a far side to our **own server address** —
ping, disco#info — stayed unanswered although RFC 6120, section 8.2.3, rule 3
demands an answer. It went into the routing, found no session there for the domain
and disappeared.

**The reason for the gap was the build, not the knowledge.** The answers existed
long since — they stood in the middle of `HandleIqAsync` and wrote directly into a
client session. With that they were bound to a client, and a far side has none.

So separated what is different: `AnswerAboutSelf` **builds** the answer and does not
send it. The local client gets it over its session, the far side over `RouteToAsync`
— **the way back is the only difference.** What this server can do is the same for
both, and to write it down twice would mean keeping two pieces of information about
the same thing that can run apart.

**What has not wandered along is the actual work at this point.** Binding, legacy
session, carbons and the roster likewise stand in `HandleIqAsync` — but they change
the state of *one session* or belong to an account. They stay where they are, and
thereby unreachable for a far side: a foreign server that asks after our roster gets
`<service-unavailable/>` like for every other unknown request. The dividing line
does not run between "answerable" and "not answerable", but between **information
about the server** and **state of a session**.

The fallback wanders along: what the server does not know gets an error from the far
side as well instead of silence. Rule 3 knows no third possibility, and silence lets
the one asking wait into their timeout without ever learning whether the question
arrived at all.

And rule 4 still holds: on a `result` or `error` to the server address nothing
follows. A test of its own holds that fast — without it the next step would be a
server that answers every stanza to its address, and two of them would push reports
back and forth to each other.

**A test from D16 predicted this change and had to give way to it.**
`AnIqToTheServersOwnAddress_IsNotClaimedByTheUserPath` held fast that the request
stays unanswered and called that expressly "an open place and no intention". Its
actual statement remains: the delivery way for users may not touch the server
address — it answered everything with `<service-unavailable/>`, so a ping as well. A
`result` it cannot produce at all, and exactly by that the confusion can be
recognised. The test now checks the `result` instead of the silence.

Six mutations, all struck down — one of them only at the second attempt, and the
reason is **for the second time** the same as in D25.

The mutation takes the information about itself away from the local client. Over my
filter — the four fixtures that have to do with this point — **it survived**: that a
client pings the server and gets information stands in other fixtures, and those
were not among them. Over the whole suite it falls with six errors.

The error is not to choose the filter narrow — that saves real time —, but to
believe a **surviving** mutant without checking the filter. A struck-down mutant is
struck down with a narrow filter too; a surviving one says something only when the
tests that could strike it down have run at all. That belongs to the fifth meaning
from D25 and is its practical form: **at every survivor suspect the filter first,
not the test.**

---

### D37. A proposal that advises against itself ⛔ — XEP-0013 falls away

XEP-0013 ("Flexible Offline Message Retrieval") stood next as a point. It is **not
implemented**, and the reason stands in the document itself: the XSF carries it as
**Deprecated** — version 1.3, state 2021-05-04, with the sentence "Implementation of
the protocol described herein is not recommended."

It would have brought the other half of the store from D14. Today the server decides
when the kept messages come: at the next non-negative available presence, all at
once, and with the giving out the store is empty (`TakeOfflineMessages`). XEP-0013
would have given this decision to the client — look in before fetching, read or
throw away single messages on purpose, leave the rest lying.

The price would not have been the listing. `OfflineMessage` today carries `Stanza`
and `StoredAt`, **no identifier** — XEP-0013 addresses every kept message over a
`node` attribute that has to stay the same across a restart. That would have covered
the record, the store in `XMPPAccount` and the persistence in `FileAccountStore`. The
expensive part lies elsewhere however: a client that manages the store itself may not
get it sent at the same time. The automatic handing in later would therefore have had
to become switchable, depending on whether the client has reported before its first
presence. That is a second state in the way of logging in, at exactly the place D14
hangs on.

To do this rebuilding for a document that advises against its implementation would be
the wrong way round: the effort would arise, and what would remain is a protocol no
new client will speak any more.

**A successor XEP-0013 does not name.** It refers only to "the protocol that
supersedes this one (if any)". In practice XEP-0313 (Message Archive Management)
takes over the targeted reading up — but only the one half, and with a different
term: an archive is no store. It also contains what was delivered, and it does not
empty itself through the reading. The second half — "do not send me everything at the
logging in" — does not stand there. Whoever wants it needs it in addition. Should
that ever be due, it is a point of its own and not this one.

What remains is the way from D14: RFC 6121, section 8.5.2.2.1, and XEP-0160. Both are
current, both are implemented, and both suffice for a client that simply wants to
have the messages.

A find remains as well: **that `OfflineMessage` has no identifier is no gap but a
consequence.** As long as nobody can address a single kept message, there is nothing
to name. The identifier is missing exactly as long as it is not needed — it would be
the first line a protocol would have to change that addresses single messages.

---

### D38. A list that does not wait 🕓 — XEP-0060 becomes optional

"Later" meant two things until now: points that only lack the opportunity, and
points nobody misses. Both in one list reads like a list of debts, and the longer
it becomes, the less it says. With D37 "deliberately not implemented" came to it;
in between **"optional"** was missing: not decided against, but not due either.

XEP-0060 belongs there. The gap is real — and it is bigger than the old entry
said: `PubSubSubscribeAsync` sends the request off and enters the subscription at
once, without waiting for the answer. A refused subscription afterwards stands as
an existing one in `_subscribedNodes`, and the caller never learns it.
`OnSubscriptionResult` exists already, set off it is nowhere.

**Not due all the same, and for a reason that fits the rest of the way of
working.** This client uses PubSub nowhere itself; the members concerned already
stand in the README as unused API surface. A correlation no caller fetches could
only be checked against an invented course of events — and a test that invents its
own use case checks the invention. That is the same reason the XEP-0160 rule from
D14 stands under "Later" instead of as done.

An optional list is the place where things are forgotten in peace. This is why the
way back stands with it: **as soon as PubSub has a use case** — a subscription
against a real PubSub component at which promise and refusal can be told apart —,
the point wanders back to "Later". Not the time fetches it back, but the need.

---

### D39. We demanded what we did not give ourselves ✅ — section 3.2

XEP-0030, section 3.2: "If the request included a 'node' attribute, the response
MUST mirror the specified 'node' attribute to ensure coherence between the request
and the response." XEP-0115, section 6.2 says the same for the caps case and names
the value: `node#ver`.

**The gap was an asymmetry, no ignorance.** `EntityCapsManager` has always asked
with `node#ver` and lays the answer down under exactly this key.
`DiscoManager.RespondInfoAsync` could even set the `node` — the only caller passed
none and did not even read the attribute of the request. We therefore demanded of
every far side what we ourselves never delivered.

Nothing looked broken in doing so, and that is the treacherous part: a strict far
side does not lay an answer without a `node` down under `node#ver`, asks anew at
every presence and gets the same information every time. The use of XEP-0115 falls
away without an error appearing anywhere.

**The second half was the bigger one.** A node that does not exist here got the
same full list of features as a request without a node. This side thereby claimed
**to carry every node one might think of** — `commands`, `offline`, whatever
somebody asks, it existed. Now only what designates this entity is answered: the
caps node, with and without the current `#ver`. Everything else gets
`<item-not-found/>`.

**An outdated `ver` belongs expressly to "everything else", and that is the
uncomfortable decision.** Widespread servers send the current list there as well.
That is more convenient and wrong: the one asking recomputes the announced hash
against the answer under XEP-0115, section 5.4. To an old `ver` the new list yields
a different hash — they then have the choice of holding us to be a forger or of
giving the recomputing up. Our own `EntityCapsManager` would refuse the answer. An
error is the more honest information: **this state does not exist here any more.**

**The test server has no nodes at all**, it announces no capabilities. Every
question about a node gets an error there. In doing so a sentence came out that
claimed a distinction that did not exist: the switch `FailDiscoInfo` answered with
"This node does not exist here." — to a query that names no node, in a server that
never looked at the attribute. The sentence now stands where it holds; the switch
says what it does.

**An error too is an answer and has to say what to.** Both errors take the request
together with the `node` back with them (RFC 6120, section 8.3.1); `StanzaErrorIq`
has got a parameter for that. Without it somebody asking who queries several nodes
of the same entity learns only that *some one* is missing — and the mirroring from
section 3.2 holds for the error answer just the same.

Eight new tests, eleven mutations, ten struck down. **The survivor is a state that
does not exist:** `EntityCaps?.IsOwnNode(node) != true` against `== false` differs
only in the case "no EntityCaps", and that does not occur — `Disco` and
`EntityCaps` arise in two consecutive lines, the condition checks
`Disco is not null`. The stricter version stays standing: without a caps manager
there is no node identifier of our own, and what one does not know one cannot
confirm.

One mutation has got a test instead of being noted as a survivor. The server reads
its frames as strings — deliberately, so that it does not look at the client
through the same glasses the client looks at itself with. With that "`node=`
stands somewhere in the frame" and "the request carries a `node`" are two different
things, and the difference would have stayed unshown.
`ANodeOutsideTheQuery_DoesNotCount` puts a foreign element with a `node` beside the
request; without the anchor in the pattern this ordinary query would get an error.

**And a tool turned the work back.** `mutate.ps1` reset after every run out of a
backup folder it never filled itself — in it lay what some earlier session had put
down there. Two files thereby jumped back by a whole session; in `XMPPConnection.cs`
`CreateTcp` was there again, deleted in D34. The check of hashes obediently reported
"as before" in doing so, for it compared against exactly this old backup.

That is the same error as in D34, only one level deeper: **a measurement that does
not measure what it claims.** Only this time it was not merely blind but
destructive — the check that should have reported the damage was part of it. The
backup is now pulled at the moment of the mutation out of the file that is about to
be mutated. A backup that is older than the work is none.

**Incidental find, noted under "Later":** `LocalFeatures` announces `disco#items`,
answered it is nowhere — an incoming items query falls through as far as the
`<service-unavailable/>`. Announced and then refused is the one combination there
may not be.

---

### D40. Announced and then refused ✅ — section 4

The incidental find from D39, and it is no missing feature but a false promise:
`LocalFeatures` has always carried `http://jabber.org/protocol/disco#items`, an
items query was never answered. It fell through as far as the
`<service-unavailable/>`. A far side that believes the list of features therefore
got an error to a question we had invited it to.

**The answer is an empty list, and that is no stopgap.** "I have none" and "do not
ask me" are different pieces of information, and only the first is right: a client
has no sub-units. Whoever sends `<service-unavailable/>` instead says the second —
and whoever does not allow the question in the first place should not have
announced the feature.

`DiscoManager.LocalItems` is empty as a default and is really read; a test fills
it, otherwise "always an empty list" would be a passing solution and the list a
decoration.

**A `node` is something different here than in D39.** At disco#info it designates
the entity itself (the caps node from XEP-0115); at disco#items it is a branch in
the tree of the sub-units. This client has not a single one, so
`<item-not-found/>` — the same decision as in D39, for the same reason. The empty
list would be the wrong answer here: it would mean **"this branch exists, it is
empty"** instead of "this branch does not exist".

This is why `RespondItemsAsync` has **no** `node` parameter although its
counterpart `RespondInfoAsync` has one. It would never get a value: where a node
stands in the question, no answer is given at all. A parameter that never gets a
value looks like an ability and is none — and would promptly have been the first
survivor, because no test ever reaches it.

`RefuseUnknownNode` has got the namespace as a parameter for that: the error takes
back the request that was put. **An error that names the wrong question is worse
than one without a question** — the one asking then assigns it to the wrong query.
A mutation of its own checks exactly that.

Four tests, seven mutations, all struck down.

And one line in the README no longer held: `EntityCapsManager.GetCachedInfo` stood
under "unused and untested" while two fixtures check over it what lands in the caps
cache. Such lists age in the unpleasant direction — they claim as unchecked what is
checked by now.

---

### D41. Where to, the domain says ✅ — XEP-0156

The endpoint was wired fast: `wss://{domain}:5443/ws`, the ejabberd default. For
Prosody, for every other server and for every operator with a path of their own the
caller had to know it and pass it along. XEP-0156 is the way on which the domain
itself says where its WebSocket stands: `host-meta` under `/.well-known/`, once as
JSON (JRD), once as XML (XRD).

**Two sentences of the XEP determine the whole cut.**

The first is an order of precedence: "HTTPS queries for host-meta information MUST
be used only as a fallback after the methods specified in RFC 6120 have been
exhausted." This is why asking happens **only when the caller has named no
endpoint** — and a test of its own holds fast that the discovery then does not
start up in the first place. Without it "always look first" would be a passing
solution: expensive for everybody who knows their server, and an open door for a
foreign `host-meta` that sends them somewhere else.

The second is a rule of security, and it has two halves: "host-meta files MUST be
fetched only over HTTPS, and MUST only use connection URLs starting with 'https://'
or 'wss://'." Both belong together. Whoever fetches the information in plain text
lets every man in the middle determine where the client logs in; whoever takes a
`ws://` from a securely fetched piece of information sends user and password openly
through the network afterwards all the same. **Half a safeguard is none here.**
Both halves have a mutation of their own.

Of the permitted pair only `wss://` is left over for this client: `https://` is
BOSH (XEP-0124), which it does not speak. A BOSH link is read and passed over — not
because it would be wrong, but because an address that came back as a WebSocket
endpoint would let the building of the connection fail at something that was never
meant for it.

**The type of link decides, not the scheme.** A `host-meta` is not made for XMPP;
there stand `lrdd`, `webfinger` and whatever else the operator publishes. Whoever
checks only for `wss://` takes the first entry that happens to be encrypted — a
test of its own lays out exactly such a one.

**What is not implemented is not missing:** the DNS way over `_xmppconnect` TXT
entries stands in no current version any more — "this was insecure and has been
removed". To rebuild it would mean implementing a withdrawn recommendation.

The search runs **at most once**, across reconnections as well. The attempt at
reconnecting is a loop; one query per pass would mean waiting anew every time for
an HTTPS answer that does not exist, at a server that is just now gone. That too
stands in a test — as a count of the queries, not as a supposition.

Twelve tests, nine mutations, all struck down. **Unchecked stays the built-in
fetcher itself:** it fetches over the network, and the suite puts a function without
a network in its place — otherwise none of these tests would be repeatable. What is
checked are the addresses that are built (both `https://`, both `/.well-known/`),
and what happens with the result. The `https` bar in the fetcher itself is thereby a
second line behind a checked first and no unchecked behaviour.

**An incidental find, noted under "Later":** does the building of the connection
fail, then the exception reads "Unable to connect to the remote server" — without
the address. Until now that was bearable, for the caller had passed it along
themselves. Since this change it can come out of the `host-meta` of a foreign
domain, and then our own source no longer answers the question "where to, actually?".
The belonging test therefore checks the endpoint and not the error text — and says
in its comment why.

---

### D42. A ladder is no set ✅ — RFC 8264, section 8

Since D5 an approximation had stood here: a code point belonged to the
IdentifierClass when its Unicode category was right and it had no compatibility
decomposition. That hit the examples from RFC 7622 — and exactly for that reason it
did not come out.

**The prescription is no checklist, but an order.** RFC 8264, section 8 is a ladder
of fifteen rungs, and many code points stand on several of them. Which one takes
hold first decides the answer:

- **U+0640 (ARABIC TATWEEL)** is a modifier letter and thereby in LetterDigits —
  the list of exceptions stands before it and forbids it. It is an elongation
  stroke: insertable as often as one likes without meaning anything. Out of one
  account thereby become as many as one likes that look alike. The approximation
  let it through.
- **U+3164 (HANGUL FILLER)** is a letter (Lo) — `Default_Ignorable` stands before
  it. An invisible letter in an address.
- **U+2163 (ROMAN NUMERAL FOUR)** is Nl and thereby in OtherLetterDigits —
  HasCompat stands before it.
- **The old Hangul jamo** are letters and got through; they compose into syllables
  that exist ready-made as code points of their own. Two spellings for the same
  word, and no normalisation clears that up.

The test for it therefore checks not only the result, but names for every case
**the branch that answers it.** A test that checks only the answer would hold a
ladder with swapped rungs to be right as long as the cases do not overlap — and
here they overlap almost all.

**What .NET does not know now stands there as a table.**
`Default_Ignorable_Code_Point`, `Noncharacter_Code_Point` and
`Hangul_Syllable_Type` the runtime does not deliver. They are entered as ranges,
named with the Unicode version they come from. That is no approximation any more,
but a copy: it can grow old, but it cannot be beside the point — and where it grows
old, it says so.

**Two rules are implemented, seven not, and that is a decision.** Context-dependent
code points (CONTEXTJ/CONTEXTO) hang not on the code point but on the whole string.
A.8 and A.9 — the two rows of Arabic-Indic digits may not be mixed — get by without
Unicode properties and are implemented; they concern digits that really appear in
addresses. The others need `Joining_Type` or `Script`, and **to guess those from
block boundaries would mean reintroducing the approximation at exactly the place at
which it decides about allowing or refusing.** So refused — it concerns five
punctuation marks and two invisible characters, no letters.

The separation of the two classes gets a counter-check of its own: what a resource
part may carry (symbols, spaces) a local part may not. Without it "both take the
FreeformClass" would be a passing solution, and the difference would disappear
unnoticed.

Nine tests, thirteen mutations, all struck down. Both tables of examples from
RFC 7622 still stand and run through unchanged — the approximation hit them, the
prescription hits them too.

**The second half of the point stays open and now stands there more precisely:**
IDNA2008 for domain labels. The code point level is thereby settled, what is missing
is the label level — Punycode, bidi rule, label lengths.

---

### D43. A domain name is no string ✅ — IDNA2008

The second half of D42. The domain part was only roughly checked up to here: no
control characters, no space. Everything else got through — an underscore, a
symbol, a label with 200 characters, an `xn--` behind which nothing stands.

**The same building blocks, a different ladder.** RFC 5892, section 1 looks like
the one from RFC 8264 and answers the same question differently. Where PRECIS says
**ASCII7**, IDNA says **LDH**: hyphen, digits, small letters — and nothing else out
of ASCII. Where PRECIS catches symbols and punctuation at the end (FREE_PVAL), IDNA
ends with DISALLOWED. To that two branches that exist only here: **Unstable** (what
changes under normalisation and lower-casing) and **IgnorableBlocks**.

This is why the two ladders stand separately, on a common substructure
(`UnicodeSets`). A procedure with switches would be shorter and would raise the
question at every line while reading, "does that hold for labels or for local parts
now?".

**Punycode is computed ourselves** (RFC 3492), although .NET brings something
similar along with `IdnMapping`. The reason is not pride: `IdnMapping` brings its
own reading along (UTS 46 over ICU) and **maps where IDNA2008 refuses** — capital
letters for instance. Whoever wants to check whether a label is valid may not give
the check away to something that bends it into shape beforehand. Checked it is
against the eleven examples from section 7.1, in both directions.

**An A-label is not believed but recomputed.** Decode, apply the label rules to the
U-label, compute back — and if something else comes out than what stood there, it is
refused. Two cases make that vivid: `xn--TDA` means the same as `xn--tda` (Punycode
digits carry no case) and is nevertheless no valid spelling; `xn--abc-` packs pure
ASCII, and then the same label would stand there twice — once as itself, once in
wrapping. **Both are two addresses for the same thing, and exactly that IDNA is to
prevent.**

**Address literals go past that, and by prescription at that:** RFC 7622, section
3.2 allows an IPv4 address and a bracketed IPv6 literal beside the domain name.
`[::1]` is no domain name; colons are no label characters, and without this
exception the address would be invalid.

Nineteen mutations, all struck down — **two of them only after the tests became
sharper**, and both times for the same reason as in D5 and D36: the test case
already hit an earlier rule.

| Surviving mutation | Why it survived at first | The case that strikes it down |
|---|---|---|
| The ignorable characters do not count | U+3164 falls over **Unstable** already, U+00AD over the catching branch | U+FE00 and U+180B: variation selectors, category Mn — without this branch they would be **letters** |
| The IDNA check in the JID is no longer asked | All label tests ask `Idna` directly | A JID with `exa_mple.com`, `-example.com`, `a..example.com` |

The second is the more unpleasant: **the check was checked, its wiring not.** A
mutation that throws the result away and goes on got through the whole suite. The
same sort of gap as the guard from D19 — what puts the question has to be checked by
somebody itself.

**What stays open is the bidi rule** (RFC 5893): it demands `Bidi_Class` for every
code point of a label, and .NET does not deliver the property. Guessed from block
boundaries it would be the same approximation D42 abolished — here even more
consequential, for the rule decides about whole labels instead of about single
characters.

---

### D44. A table instead of a supposition ✅ — RFC 5893

The open point from D43. The reason there was right and the conclusion wrong:
`Bidi_Class` **cannot** be derived — but it can be **fetched**. Unicode publishes it
as `DerivedBidiClass.txt`, and for StringPrep the same way has existed in this
project for a long time: `tools/stringprep/generate.py` creates `StringPrepTables.cs`
out of the RFC text.

So `tools/unicode/generate-bidiclass.py`, after the same pattern. It loads the file,
reads the ranges and writes `Jabber/Common/BidiClasses.cs` — ten tables, 764 ranges.
**The eleventh class, L, is not written down:** it is the biggest and at the same
time the default of the Unicode file itself. What stands in no other table is L.

The difference to the approximation D42 and D43 were about is exactly this: **a
generated table can grow old, a guessed one can be wrong.** The Unicode version
stands in the head of the file, the generator beside it; whoever doubts lets it run
and compares.

**The rule is infectious, and that is its actual content.** As soon as a single
label carries right-to-left characters, the whole name is a "bidi domain name" — and
then *all* labels have to fulfil the six conditions, the ones out of pure ASCII as
well. `9abc.example` is a valid domain name, `9abc.אבג` is none. Whoever reads over
that builds one of two sorts of error: they never apply the rule, or they always
apply it and refuse names by the row that have existed for thirty years. Both sorts
have a test here.

**An A-label is unpacked for the rule.** `9abc.xn--4dbcagdahymbxekheh6e0a7fei0b`
looks in its ASCII wrapping like two left-to-right labels; in it sits Hebrew.
Whoever lets the bidi rule run over the wrapping never finds anything.

Ten mutations, all struck down — **one only after a sharpening, and for the fourth
time for the same reason** (D3, D5, D36, D43): the test case already hit an earlier
condition. `אבגa` does not check what it seems to check: it fails at condition 3 (a
right-to-left label ends in R, AL, EN or AN) and not at condition 2 (in a
right-to-left label L is inadmissible). Only `אaב` — the foreign character in the
**middle** — hits condition 2 alone. The same for condition 5 against 6.

And an error in the way of working that this time turned out lightly: I changed
**test files while the mutation run was going.** The later mutations thereby ran
against different tests than the earlier ones. Because the change only added cases,
the verdicts stayed valid - "struck down" stays struck down. Right it is
nevertheless not: the same rule holds as for the source, and for the same reason as
in D43.

---

### D45. The code point alone does not say it ✅ — RFC 5892, appendix A

The last open point from D42. Seven of the nine context-dependent rules were
missing, because they demand `Canonical_Combining_Class`, `Joining_Type` and
`Script` — and the answer is the same as in D44: **fetch instead of guess.**
`tools/unicode/generate-contexttables.py` writes `ContextTables.cs` out of three
Unicode files; the reading work both generators share now stands in
`tools/unicode/ucd.py`. Written down is only what the seven rules need: the virama
characters, four Joining_Type values, five scripts.

**"Context-dependent" means: the code point alone does not say it** — and this
sentence was not available at all in the old build. It was called
`ContextRuleSatisfied(CodePoint, Text)` and could therefore answer only rules that
look at the whole text (A.8/A.9). Three of the new rules ask after the character
**before**, two after the one **after**; at two identical characters in the same
string it would already no longer be clear which one is meant. The place therefore
belongs into the question: `ContextRuleSatisfied(CodePoints, Index)`. The caller
works on an array instead of on a sequence for that.

The difference becomes visible at a word that really exists: **`col·la` is Catalan
and a valid local part, `co·lla` is none.** The same characters, a different order,
a different answer — more than that is not to be said about "context-dependent".

A.7 falls out of the row: the Katakana middle dot does not ask after neighbours, but
after whether **anywhere** in the string Japanese script stands. It separates the
parts of a foreign word in Japanese text; without Japanese characters it separates
nothing.

Fourteen mutations, all struck down — **three only after a sharpening, and for the
fifth time for the same reason.** This time in its purest form: rule A.1 has two
sides (on the left a joining letter, on the right one), and my test case `a‌b`
violated **both**. It could therefore not show that each is checked for itself. Only
`a‌ي` (left wrong, right right) and `ب‌b` (the other way round) separate the two
halves — and a third pair with a transparent character in between shows that the
rule looks over it.

Incidentally a note that no longer held: the description of `Idna.IsValidDomain`
still said the bidi rule was missing — since D44 it does not do that any more. **A
comment that names a gap is useful as long as the gap exists, and after that a false
statement in a prominent place.**

With that RFC 7622 is completely implemented: code point level (D42), label level
and Punycode (D43), bidi rule (D44), context-dependent rules (D45).

---

### D46. A typing state promises nothing ✅ — XEP-0160, section 3

The last point under "Later → protocol", and the reason for the postponement was
the wrong one from the beginning. It read: "this client sends no such message, the
rule would be untested". That holds for the client — **only the rule belongs to the
server.** A test needs no client that sends a typing state to an absent person; it
needs a string on the wire, and that `SendRawAsync` has always written.

XEP-0160, section 3 names the exception at the `chat`: "with the exception of
messages that contain only Chat State Notifications (XEP-0085) content (such
messages SHOULD NOT be stored offline)". A typing state is a statement about *now*.
Handed in later at the logging in it says somebody is typing at this moment — and
that is then guaranteed no longer true. Ten of them moreover displace the messages
the store is there for.

**And the sender gets no error**, although D14 expressly ruled the silent discarding
out. That is no relapse but the limit of that rule: it protects an expectation.
Whoever sends a message wants to know whether it arrived; whoever sends a typing
state has lost nothing when it expires. A `<service-unavailable/>` for it would be
noise — and one that would come anew at every keystroke.

**Here the server reads a tree as the only place**, and the reason stands in the
rule itself: the question reads "are *all* children typing-state elements". A
`Contains` answers "occurs", not "occurs only" — and exactly this difference is the
prescription. The string glasses from D26 stay where they belong: at the switch that
decides *what* a stanza is.

Three decisions in doing so, each with a test:

- A `<thread/>` does not count as content — XEP-0085, section 5.3 demonstrates
  exactly this form.
- A message without text is not by a long way a typing state because of it: a
  receipt (XEP-0184) and a read marker (XEP-0333) have no text and are to arrive.
  The obvious shortcut "without a `<body/>` do not store" would be wrong.
- `normal` with the same content is stored. That is the letter of the section:
  there stands "SHOULD be stored offline" without a restriction. To draw the rule
  wider than written would mean inventing a prescription of one's own and calling
  it somebody else's.

Seven mutations, all struck down — one only after a sharpening, and the case is
pretty: the mutation checked the name instead of the namespace (`composing`). **All
my cases used `<composing/>` of all things** — the mutation was thereby invisible
although XEP-0085 knows five states. An `<active/>` suffices to strike it down.

For the second time in two points there moreover stood a statement in the README
that had outlived its truth: "A request from a far side to the server address stays
unanswered" — answered since D36. **A note about a gap needs the same drawing along
as the source**; otherwise the most honest line becomes the falsest.

---

### D47. Where to, actually? ✅ — the endpoint in the error text

Did the building of the connection fail, then the exception read "Unable to connect
to the remote server" — without the address. As long as the caller passed it along
themselves, that was bearable: they could look in their own source. **Since XEP-0156
(D41) it can come out of the `host-meta` of a foreign domain**, and then it stands
nowhere they could look.

So exactly this one call is wrapped: what `ClientWebSocket.ConnectAsync` throws
comes out as an `XMPPProtocolException` that names the endpoint and carries the
original error along as the `InnerException`.

**That is no climb-down against D31.** There it was about the *stack* of the
original error — "for the caller the place is interesting at which it went wrong".
Exactly that does not hold here: the stack ends in `ClientWebSocket.ConnectAsync`
and says nothing one does not know already. What is missing is the address.
Everything after it — negotiation, SASL, binding — stays unchanged and still throws
its own exceptions; an `AuthenticationException` is one as before, and the way of
reconnecting still decides on it.

Two limits to that, both with a test:

- **An abort stays an abort.** Whoever pulls their token gets their
  `OperationCanceledException` and not the message about the endpoint - otherwise
  their own abort could no longer be told apart from a failure.
- **Named is the endpoint used, not the default.** The test lets the discovery find
  `wss://127.0.0.1:1/ws`; exactly this address has to stand in the message. Without
  it "name the built-in default" would be a passing solution — and that would keep
  quiet about precisely the case the whole change is there for.

Four mutations, all struck down, without sharpening.

---

### D48. The transport nobody misses 🕓 — TCP becomes optional

The TCP transport for the client wanders from "Later" to "optional". The extent
has been measured since D34 and has not changed; **what has changed is the insight
that nobody is waiting for it.** This client speaks XMPP over WebSocket, and all
three servers it runs against — Prosody, ejabberd, our own test server — offer
that.

With that what gave the list its reason in D38 holds for it: not wrong, not
urgent, and without a use case not checkable either. A transport no caller uses
could only be measured against an invented course of events — and that is exactly
the sort of test that checks its own invention.

**The way back stands with it, as at every point of this list:** a server this
client is to reach that offers no WebSocket endpoint. Then the use case exists and
with it the counter-check — Prosody listens on 127.0.0.1:5222 in this environment.

With that "Later → transport" is empty. What stays there are two points of the
test suite, three at the server and the structure.

---

### D49. The number nobody measured ✅ — the `h` in the `<failed/>`

The point was called "answer the XEP-0198 `<resume/>`" and had stood under
"Later → server" since 26 July. **R1 settled it on 28 July**, R2 and R3 checked the
resumption afterwards against our own server and against Prosody — the list only
never learned it. A settled point that stays standing is not merely paper: it
covers up what of it really was still open.

Open was the **refusal**. The server answered every failed `<resume/>` with

```xml
<failed xmlns='urn:xmpp:sm:3' h='0'><item-not-found .../></failed>
```

and the `h` in it was no information but a claim: *"Of everything you have sent,
nothing has arrived."* Under XEP-0198, section 5, the attribute is voluntary ("MAY
also include") and means a measurement — how far the server had got on the old
stream. Measured nothing had here.

**Without consequence it was only because nobody was listening either.**
`ProcessFailed()` did not take the frame in at all and declared every unacknowledged
stanza lost. Both errors together yielded a coherent picture — the wrong number was
read by nobody, and the client got by without it because it held everything to be
lost anyway. Exactly that way errors survive in pairs.

What holds now are three cases instead of one:

- **Unknown identifier** — no `h`. The normal case after a restart or after the
  clearer has been there: the server knows nothing and says nothing.
- **Foreign account** — no `h`. The number would betray that this stream exists and
  how much has run over it; out of a guessed attempt would become a probe.
  Information gets only whoever would have access anyway — the same limit as at the
  taking over itself (R2).
- **Expired but still there** — the real `h`. The case the section names expressly
  ("an earlier session that has timed out").

On the client side `ProcessFailed(xml)` now reads the state over `ProcessAck` — the
same modulo arithmetic as at every `<a h='…'/>`, for two understandings of the same
computation are one too many. Lost afterwards is only what was open **beyond** it.
That is no blemish: section 4 recommends sending the lost again — on the old basis
that delivered everything a second time.

**A test switch, and this time one that is needed.** `SweepResumableStreams` stops
the clearer. Without it the third case can only be hit in a race: the pass goes at
the beat of a second, and what it has cleared away the server no longer knows — the
window is at most a second wide in operation.

**The mutation that survived at first was exactly this switch.** With the usual
200 ms of waiting time the returner simply got ahead of the clearer, and both new
tests passed even when the switch was without effect — they won a race they should
not have run at all. Three seconds of waiting time later the case is brought about
instead of hoped for, and the mutation falls.

Seven mutations, all struck down: `h='0'` instead of leaving out, `h` never named,
`h` to a foreign account as well, deadline not checked, client does not read the
state, frame does not reach the client manager, clearer not stoppable. The first
six have run once more after the change to the tests — a verdict about a version
that no longer exists is none (see D44).

At the server two points thereby remain: offer SCRAM, and create stanza errors
where there is no switch for it as well.

---

### D50. An account that does not exist ✅ — and a source that says nothing

Again a point that was older than its settling: "offer SCRAM so that the SCRAM path
of the client is checked integratively". **S2 did that** — the server offers
SCRAM-SHA-256, SCRAM-SHA-1 and PLAIN, the client takes the strongest by itself, and
with that the whole suite runs over SCRAM-SHA-256. It even stands word for word in
S2 ("checked integratively for the first time"). The list again did not learn it.

Open was something S2 had noted itself:

> An unknown account is refused before the exchange begins. With that the server
> betrays whether an account exists; **RFC 5802 §7** recommends carrying on with a
> made-up salt.

**The source given is not right.** RFC 5802 §7 is the formal syntax, and the RFC
recommends a made-up salt in no place — in this very syntax it even carries an
`unknown-user` as an error value and leaves it to the server whether it replaces
the real reason by `other-error`. The recommendation that was meant stands elsewhere
and is clearer: **RFC 6120 §13.11, "Directory Harvesting"** — "not reveal whether or
not an account exists at a server when an entity attempts to authenticate". A
sentence that stood there wrongly cited twice (in the work plan and in
`UnknownUser_DoesNotStart`) shows nothing; it only looks like it.

**The error value was never the problem.** Both cases got `<not-authorized/>`
before already, and §6.5.10 covers both expressly: "this might include, but is not
limited to, the case in which the user does not exist". Betrayed the **course of
events** did:

| | first message | second message |
|---|---|---|
| Account present, password wrong | `<challenge/>` | `<failure/>` |
| Account not present | `<failure/>` | — |

One round of difference, and a list of names is sorted in one pass.

Now the exchange runs to the end for an unknown name as well, with **made-up
credentials out of the user name and a server key**. Three properties, and each of
them has a test of its own, because each on its own defeats the measure:

- **constant** — a salt that turns out differently at every attempt is itself the
  information; the one of a real account stands fast. Asking twice would suffice.
- **different per name** — a fixed, built-in salt would be the worst solution of
  all: two names with the same salt do not exist among real accounts.
- **not to be predicted** — the server key is random, otherwise the one asking
  recomputes the made-up salts themselves and sorts as before.

To that iteration count and length of salt as at a real account; both stand openly
in the server-first-message.

**What that does not achieve stands with it:** across a restart the made-up salts
change, the real ones do not — the server key lives in the process. A lasting one
would belong in the account store. And **PLAIN** stays untouched: there the course
of events is the same in both cases anyway, only the running time differs (a real
account computes PBKDF2, an unknown one does not). To close that would be easy, but
a test for it would measure the machine and not the code — this is why it is named
here and not silently gone along with.

Seven mutations, six struck down: fail at once, salt random, salt the same for all,
iteration count deviating, salt shorter, safeguard against a login without an
account removed. **The seventh survives and is meant to:** the made-up *keys* hang
on the name as well, and no test can notice that — they never reach the wire. Over
the StoredKey only the comparison runs, and the server-final-message, in which the
ServerKey sits, exists only at a successful login, which here never exists. The
derivation stays all the same: it costs nothing and is the construction one can
defend.

The one test that does exist for it had to borrow the case:
`AValidProof_IsNotEnoughWithoutAnAccount` slips the **real** credentials to the
exchange as made-up ones. The proof is then right — and is refused all the same,
because no account stands behind it.

At the server one point thereby remains: create stanza errors where there is no
switch for it as well.

---

### D51. An address that is none ✅ — `<jid-malformed/>`

The last point of the server list, and again it was long since settled by half:
the server has created a whole row of stanza errors of its own accord since D26 to
D50 — `<bad-request/>` for an unknown IQ type, `<service-unavailable/>` for an
undeliverable recipient and for a `groupchat` to an account,
`<remote-server-not-found/>` for an unreachable domain, `<item-not-found/>` for an
unknown disco node. The switches have long since not been the only source.

**One condition was missing completely, and it was the one everything lay ready
for.** `<jid-malformed/>` (RFC 6120, section 8.3.3.8) did not appear in the whole
server — the word stood at exactly one place in the source, in the comment of
`JidFormatException`. And the check behind it has existed **completely since D42 to
D45**: RFC 7622 with PRECIS, IDNA2008, the bidi rule and the context-dependent rules
from appendix A, computed against the tables of the UCD.

The server never asked it. `JidUtilities` appeared in `XMPPServer.cs` exactly once,
in `AreEqual` at the comparison of two full JIDs. What came in went into the
delivery, and an impossible recipient looked there like an absent one: the sender
got silence or a store nobody ever fetches them out of.

**That is the same pattern for the third time.** In D43 the IDNA check was finished
and not wired in the JID, in D45 the context-dependent rules. A checked rule without
a caller is no half rule, but none — and nobody notices it, because its own tests
are green.

The check sits **before the switch**, at one place for all three types: every branch
behind it puts its own questions, and this one belongs to none of them. Three limits
to that, each with a test:

- **No `to` is no wrong `to`.** A stanza without an address is directed at the
  server (§8.1.1.1), and undirected presence never carries one. The mutation that
  treats both alike lames half the suite — without presence no session counts as
  available.
- **On an error no error follows** (§8.3.1). Discarded the stanza is all the same:
  deliverable it is not after all.
- **The sender of the refusal is the server**, not the intended recipient.
  `<service-unavailable/>` answers in the name of a recipient, because the server
  has answered for them there; here there is none — the address is none, so nobody
  has looked in.

Five impossible addresses in the test, and each for a different reason: `alice@`
comes out at a comparison against two empty strings already, `alice@-localhost` only
at the label rule from RFC 5891, `al ice@localhost` only at the PRECIS
IdentifierClass. A single one would leave open how far the check reaches.

**The gap that came out to me myself:** no test held fast that the refused stanza
really ends as well. A check that answers and afterwards delivers all the same would
not have been distinguishable from the right one —
`ARefusedStanza_IsNotDeliveredAnyway` therefore sends to `bob@…/`: no JID, but the
part before it belongs to a logged-in account, and over the way for bare JIDs it
would arrive at Bob.

Seven mutations, none survives the run:

| | Mutation | struck down by |
|---|---|---|
| X1 | the check falls away | 8 tests |
| X2 | a missing address counts as a wrong one | see below |
| X3 | error type `cancel` instead of `modify` | the five impossible addresses |
| X4 | the sender is the intended recipient | the same five |
| X5 | an error stanza is answered as well | `AnErrorStanza_IsNotAnsweredWithAnError` |
| X6 | refused, but handed on all the same | `ARefusedStanza_IsNotDeliveredAnyway` |
| X7 | the `id` of the request gets lost | `AnIqToANonJid_KeepsItsId` |

**X2 is struck down not by an assurance but by the protection against hangers** —
and that is itself the finding. Does a missing address count as a wrong one, then
every undirected presence is refused; no session ever becomes available, and the
building of the connection of the client waits for it **without a deadline of its
own**. The first run therefore stood for 74 minutes until I broke it off; with
`--blame-hang-timeout 3m` the test run breaks off after three minutes with a hang
dump. Get through the mutation never could — passed is something other than crashed
—, but measured it no test did.

**Two lessons out of the breaking off, both dearly paid:**

1. *The protection against hangers belongs on every mutation, not only on the one
   one expects it from.* The script has had the switch since M2 and I had not set
   it.
2. *A broken-off mutation run leaves the source mutated behind.* `mutate.ps1` only
   resets when `dotnet test` comes back — is it shot down, then the mutation still
   stands there. The backup from the moment of the mutation caught it; without the
   check "is my line there again" it would have wandered into the commit. Exactly
   that was already once the cause in D39, only the other way round.

Incidentally: the hang dump lays 219 MB down under `Jabber.Tests/TestResults/`, and
the directory stood in no `.gitignore`. A `git add -A` would have taken it along.
Stands in it now.

---

### D52. Silence is an answer too ✅ — the silently discarded case

The first of the two finds from D51. In `StoreOfflineOrRefuseAsync` there stood:

```csharp
if (GetAccount(BareOf(to)) is not { } account)
    return;
```

A message to an account that does not exist disappeared. RFC 6121, section 8.5.1
allows that expressly — for an unknown recipient `<service-unavailable/>` **or**
silence stand to the choice.

**Free the choice is nevertheless not.** It has to be the same as for an existing
account that is just not watching, otherwise it answers a quite different question:
*does this account exist?* And on the most convenient way there is — send a message
and look whether something comes back. That is the same question as in D50, only
without a login.

Apart it fell as soon as the store did not accept:

| | store on | store off or full |
|---|---|---|
| Account present, absent | silence (stored) | `<service-unavailable/>` |
| Account not present | silence (discarded) | **silence** |

In the right-hand column stands the information. On a server without an offline
store every list of names is sorted in one pass.

**Asked is therefore no longer "is there an account", but "would the store accept
it".** For an unknown one the store is empty, and an empty one accepts as long as
anything fits in it at all:

```csharp
account?.StoreOfflineMessage(…) ?? MaxStoredOfflineMessages > 0
```

**The second summand is the point.** A plain `?? true` would be right in 99 out of
100 cases and wrong in the hundredth: at `MaxStoredOfflineMessages = 0` an empty
store accepts nothing either, the existing account gets an error — and the unknown
one would have kept silent again. `AFullStore_RefusesForBothAlike` holds exactly
that fast, and the mutation `?? true` dies at it.

The more important counter-check however is `WithTheStore_NeitherRecipientIsTold`:
"just always answer for unknown ones" would be the obvious solution and would hit
**exactly beside it** — at a switched-on store, that is, at the default, the
existing account would then get silence and the unknown one an error. The question
would be answered again, only the other way round. The test was the only one of the
three that was green from the start; without it the improvement that made it worse
would not have come out.

Four mutations, all struck down: discard silently again, `?? true`, `?? false`, and
no longer ask the switched-off store.

Created for the unknown recipient is nothing — the test looks. Handed in later
nothing ever is to them either; that is the difference between "acts as though
something had been stored" and "stores", and nobody notices it, because the account
does not exist.

---

### D53. The same check, a different door ✅ — `<jid-malformed/>` over the border

The second find from D51. The check of the `to` held only for stanzas from clients;
what came over `AcceptFromRemoteAsync` from a far side was checked on origin and
responsibility and then delivered. **There it hits the more probable case:** our own
client is written by the same library, the foreign implementation is not.

**On looking, the `from` had the same gap, and that is the more serious one.**
`DomainOf("al ice@left.example")` obediently delivers `left.example`, the check of
responsibility is satisfied, and a stanza with a sender address that is none runs
through. To compare fragments and to call the result "foreign domain" is no check.

The two cases weigh differently, and in that lies the actual decision:

- **`MalformedSender`** goes the same way as `ForeignSender`: RFC 6120, section
  8.1.1.1 calls both an invalid `from`, the stream ends with `<invalid-from/>`. The
  reason carries just the same — whoever once sends something without an address
  does it again at the next attempt.
- **`MalformedRecipient`** costs only the one stanza, to that a `<jid-malformed/>`
  back to the sender. That is a typing error in an address and no statement about
  who is speaking there. Did it break the federation off, the check would be worse
  than its use — `AMalformedRecipient_DropsOnlyThatStanza` holds the limit fast.

**The order is itself a statement** and therefore has a test case of its own. At
`bob@-right.example` the domain is already none; `IsLocal` would hold it to be that
of a third party. Stood the check behind it, the stanza would be refused rightly and
**given the wrong reason** — the sender would look for the error in the wrong place.
The mutation that does exactly that dies at this case and at no other.

The frame of the error from D51 is drawn together into **one** version
(`JidMalformedError`) in doing so. Two spellings would have differed only in small
things, and exactly those would have been the difference nobody notices: a client
that gets a different type of error over the border than in its own house has two
cases to handle where there is one.

Seven mutations, all struck down: sender not checked, recipient not checked,
recipient only after the question of responsibility, error stanza is answered,
refusal names the recipient as the sender, an impossible sender no longer ends the
stream, and — the counter-direction — every refusal ends the stream.

An observation on the side that saves time next time: in the mutation runs there
stood **11 skipped** tests instead of the usual 7. No riddle, but the missing
environment variables `JABBER_*_CERTS` — `mutate.ps1` does not pass them on. For
these mutations it was without consequence (none of them concerns the foreign far
sides), but a mutation in the S2S transport would have been measured there against
fewer tests than the name of the suite promises.

---

### D54. A guard nobody has to think of ✅

The point read: *the wiring of the guard is a mechanical property and held by no
test. Would somebody take the `AssertClean()` out in a single fixture, it would not
come out.* Secured it was by a check of the source by hand — "no `new XMPPServer(`
without `Watched(…)`" (D19), 39 places of creation in 17 files.

**Not secured, but abolished.** A test that checks that every fixture writes the two
lines would have been only a second place at which the same forgetting is possible:
it would have read the source and measured nothing, and for the fixture of tomorrow
it would have done nothing.

Instead every `XMPPServer` reports its arising — an `internal static event
OnInstanceCreated`, set off at the end of the constructor —, and an `ITestAction` at
the assembly level hangs itself on every one of them. With that the guard is no
longer a property somebody has to produce, but one that holds of itself.

Three lines of production code solely for the test suite are a decision and no
matter of course. They are defensible because they are `internal` — outwards the
server says nothing about it —, and because the alternative was to go on relying on
the attention of humans. The server carries a dozen test switches anyway; this is
the first that does not change its behaviour but only watches.

**The guard per fixture stays.** `InternalErrorGuard` delivers `InternalErrors` for
the tests that want to *look at* the reports. What falls away is its
indispensability: whoever forgets `Watched(…)` or `AssertClean()` in future loses
nothing any more. `Expect()` passes the intention on to the global guard —
otherwise a fixture would have to say twice that its error is wanted, and the second
place would again be one to forget.

**The test without which the whole thing would be worthless:** that the new guard
also *lets things fail*. The worst version is the one that takes everything in and
never makes anything of it — it looks like a safeguard, is none, and the suite stays
green. Exactly the same trap `InternalErrorGuard.Record` already defused, and for
the same reason the taking in now exists separately from the hanging on here as
well.

To that the separation between two tests: would a report stay standing, it would
come out only to the *following* test — and which one that is the test runner
decides. The test therefore reproduces the transition itself: report, let it fail,
begin the next test, look.

**The first full run with a sharp guard was clean.** The rule about the source from
D19 had therefore been kept without a gap — only by hand. Six mutations, all struck
down: arising not reported, guard makes nothing of the reported, does not clear up
between two tests (24 tests fall with it), hangs itself on no server, does not pass
the intention on, and runs once per suite instead of per test.

The most revealing is the third: **a missing line drags every report into all
following tests.** Exactly for that the transition between two tests stands there as
an assurance of its own and not as a hope about the order of the test runner. And
the fifth shows the reverse side of the new reach: without the passing on of
`Expect()` the five tests fall that set off an internal error on purpose — the guard
over all servers sees what is wanted as well.

**A run that measured nothing looked like a passed one in doing so.** The first
attempt at the full pass reported `782 succeeded, 25 skipped` — green. The far sides
were running, the certificate paths were readable; the environment variables had
only not reached the test process, because the run had been started over the Bash
shell instead of over PowerShell. **The number of skipped tests is the only thing
that distinguishes the two** — 7 means "both foreign servers stood", everything
above it means "the federation was not touched". Repeated, this time rightly: 800
green, 7 skipped.

---

### D55. A number where a relation was meant ✅ — the flaky test is explained

`NonzasDoNotAdvanceTheCount` against Prosody, come out in D34 as **one** failure in
one full run and afterwards not to be repeated in twenty targeted executions. The
recording from D35 never fell due — cleared up the case is all the same, and out of
the two numbers that already stood in the log at that:

```
Wir haben Nonzas mitgezählt.               Expected: 6  But was: 8
Prosody hat andere Nonzas mitgezählt.      Expected: 8  But was: 6
```

The starting state was 3, Prosody acknowledged **6** — that is, exactly the three
messages of the test, and not a single one of the six nonzas. **Prosody counted
rightly, and we did too.** At our end there stood only two stanzas more in the
counter that this test did not send and that went out after Prosody's `<a/>`.

With that the obvious explanation — "one side counts nonzas along" — is exactly the
one that does not hold. A client sends of its own accord: it answers what comes in,
and **when** that happens the test does not determine. The three messages go to our
own account and come back; what the client does thereupon falls into the window
between the acknowledgement and the reading of the counter.

**The error lay in the test, not in the counter.** It checked "the state has risen
by exactly three" — a number. Section 2 however says no number, but a relation: *the
counter rises by the stanzas and by nothing else.* Exactly that now stands there,
measured against the recording instead of against the intention:

```csharp
Assert.That(sm.OutboundCount - before, Is.EqualTo(Counted(outgoing)));
```

Three is only the lower bound any more, so that anything is measured at all, and a
fourth assurance demands at least three **nonzas** in the outgoing — otherwise the
test would not check its own heading.

**Counted is with a version of the rule of its own**, not with
`StreamManagementManager.IsCountableStanza`. That is the function whose result is
checked here; took the test it, then it would compare a number with itself and would
pass even when it answers wrongly — the same separation out of which the test server
counts independently as well.

To that a round of asking instead of a single `<r/>`: what goes out after the last
`<r/>` would otherwise stay unacknowledged for ever, and the equality of the two
states would never come about. Three rounds, each with an asking of its own.

Four mutations, all struck down: count everything outgoing, count nothing outgoing,
the counter jumps by two, and only `<message>` counts. The first is the actual
probe — it falls in both derivations, against Prosody as against ejabberd.

**And the tool is repaired along:** `mutate.ps1` now passes the `JABBER_*_CERTS` on
(see the observation in D53). In all runs of this entry there stood `skipped: 0` —
before it would have been half the tests, and the mutation would have measured
nothing at all against the foreign servers.

---

### D56. Forty runs that could refute nothing ✅

The second flaky test, and it is the counterpart to D55: there the explanation was
wrong, here it was the **refutation**.

`TheStreamSurvivesABrokenConnection` fell once in D16 with "the stream was not
resumed within 15 seconds". D33 thereupon measured — forty executions, all between
519 and 669 milliseconds — and concluded from that that the explanation "tight
under load" does not hold. The deadline stayed.

**The conclusion was wrong, and out of arithmetic at that.** The client may come
back five times in this test and waits in between with doubling, beginning at 300
milliseconds:

| Attempt | 1 | 2 | 3 | 4 | 5 | Sum |
|---|---|---|---|---|---|---|
| Waiting time before | 300 ms | 600 ms | 1.2 s | 2.4 s | 4.8 s | **9.3 s** |

Of the 15 seconds there therefore remained **5.7 for five complete buildings of a
connection** — negotiation, TLS, SASL, bind, resumption. Two failed attempts
suffice, and the deadline is exceeded while the client behaves exactly as it is
set.

**The forty fast passes do not refute that — they all got through at the first
attempt.** About the case with repetitions they say nothing. A mean out of nothing
but successful runs does not bound the outlier; it describes only what it looks
like when nothing goes wrong. The distribution is two-peaked, and measured was
exclusively the front peak.

The patience is therefore no longer a guessed number, but the sum of what the
client may do: the waiting times of its own policy plus three seconds each for the
attempt itself. For this setting that is a good 24 instead of 15 seconds. The
message now also names at the failing what the deadline consists of — otherwise
the next reader computes the same thing over again.

**What cannot be brought about cannot be held by a test that waits for its
occurring** — the failure occurred once and was afterwards not to be repeated in
forty executions. Recomputed it can be however:
`ThePatienceCoversWhatTheClientMayTake` checks the deadline against the 9.3 seconds
computed by hand plus five attempts. The numbers stand there written out and not as
a call of the same formula — otherwise the test would check them against
themselves, the same separation as at the counting in D55.

It is at the same time the only check of this suite that gets by without a far
side: it computes instead of waiting. Three mutations, all struck down: back to the
fixed deadline, the setting up costs nothing, only the first attempt counts.

**With that the cause is named but not proved.** Proved is that the old deadline did
not cover the set course of events; whether exactly that struck in D16 stays the
most probable explanation. The difference to before: it fits the data instead of
contradicting them.

---

### D57. Eleven members, three decisions ✅

"Decide about unused public members: use or strike." The list had stood in the
README since it existed. **The first step was not to believe it** — it warns itself
that it "grows old in the wrong direction", and exactly that had occurred:
`ResumeAsync`, `GetUnackedStanzas` and `OnStanzasLost` are long since used, the
last of them since D49. Three of eleven entries were plainly wrong.

**Used (3):**

- **`RosterStanzaBuilder.GetRoster`.** `XMPPConnection` put the same request
  together by hand beside it — two spellings of one stanza. The subtlety stood in
  only one of them: an *empty* `ver=''` is no placeholder but the announcement "I
  can do versioning but have nothing yet" (RFC 6121 §2.6.1). It now stands in the
  building block, where it belongs.
- **`RosterStanzaBuilder.Unsubscribe`** over a new `CancelSubscriptionAsync`. Of the
  four transitions from RFC 6121 §3 the client offered three; the fourth was
  missing although the building block stood there and the server has mastered it
  since S3b. It did not come out **because the test bridged the gap**:
  `Unsubscribe_EndsTheOwnSubscription` wrote the presence itself. A test that
  checks past the client holds the behaviour and hides that there is no way there.
- **`DiscoInfo.HasFeature`** — by a test that put the question past the list of
  features before.

**Struck (8):** `MessageReceipt` (the type documented itself that nobody creates
it), `ReceiptTracker.GetTimedOutMessages` (there is no deadline that could run out),
`PubSubManager.OnSubscriptionResult`, `PubSubBuilder.Retract` and `DiscoverNodes`,
`CarbonManager.DisableIq` and the five `DiscoInfo.Supports*`.

The five shortcuts are the most instructive case: each was one line over
`HasFeature` and carried its namespace built in with it. They could do nothing
`HasFeature` cannot — but they kept a second copy of every namespace, and that
grows old on its own.

**The build is now free of warnings.** `OnSubscriptionResult` was the only warning
(CS0067, "is never used") and stood in every output over dozens of runs. A warning
that is always there becomes wallpaper — and the next one that comes along then does
not come out any more.

Three mutations on the newly used, all struck down: the cancellation sends
`unsubscribed` instead of `unsubscribe`, the roster request always leaves the
version out, `HasFeature` says yes to everything.

**What the striking is not: a statement about XEP-0060.** The point under "optional"
stays as it was — what was missing there was never the report, but the correlation
of IQ result and request. Whoever builds it declares the event again in the same
hour. An event never set off is no half implementation, but a promise without
backing.

**And the list does not come back.** A standing enumeration of unused members is a
bookkeeping nobody keeps: it is right on the day of its arising and never again
afterwards. What is unused the compiler decides (at events) or a search (at
everything else) — both in seconds and always current.

---

### D58. One door for everything that goes to the console ✅

The point read: "the standard console logger writes into the same console as the
input line and takes the prompt apart. An `ILoggerProvider` of its own over the
**synchronised output** would be the clean solution."

**The synchronised output did not exist.** What existed was an agreement: every
handling of an event bracketed its output by hand in `ClearCurrentLine()` …
`WritePrompt()` — eleven times the same two lines. Whoever forgets one of them
notices it only in operation, and **a lock lay over none of them**. The events come
out of the receiving thread, the log out of any one at all; two simultaneous outputs
interleave in the middle of a word, together with the colour the one has set and the
other has put back.

The logger was therefore only the most conspicuous of three cases of the same
problem.

`ConsoleOutput` is now the one door. It can do two things:

- `Write(w => …)` for an output in one go,
- `Begin()` for those that cannot be put into a callback without becoming
  unreadable — the PubSub output changes the colour in a `switch`. The scope holds
  the lock until leaving and then draws the prompt along.

With that the eleven brackets shrink to one line each (`using var scope =
Output();`), and the logger goes through the same door — that is the whole
difference between `AddSimpleConsole` and `ConsoleOutputLoggerProvider`.

**Two small things that fell out along the way:**

- The full category name is the type name together with the namespace, here about
  fifty characters — on a console with an input line half the width for a piece of
  information that is the same in every line. Only the last part stands there now.
- `ILogger` passes the exception through **separately** from the text, and the
  formatter leaves it out. Whoever does not append it themselves logs "connection
  lost" and keeps quiet about what at.

**The part of the project that had no tests at all up to here** now has eight.
Checked it is against a `StringWriter` with a given width: on a test runner there is
no window, and the test is to delete the line and not to measure the environment.

Five mutations, all struck down: do not clear the line, do not draw the prompt
along, lock only half removed (that throws at the leaving and takes all eight with
it), logger writes past the output — and **the lock removed completely**. The last
is the interesting one: it kills **exactly one** test,
`ParallelWriters_DoNotInterleave`. With that it is shown that it really measures the
mutual exclusion and does not only run along.

A test that was red at the first run was wrong by the way and not the code:
`WriteLine` ends under Windows in `\r\n`, and "the output contains no carriage
return" is therefore never true. Meant was the sequence of deletion at the beginning
— checked is now the beginning.

---

### D59. A time that stands there and is not right ✅ — XEP-0203 read

The server has always written the delay stamp —
`AStoredMessage_CarriesADelayStamp` has held fast since D14 that every message
handed in later carries a `<delay/>`, with UTC time and the server as the originator.
**The client has never read it.** `urn:xmpp:delay` did not appear in its whole
source, and `XMPPMessage.Timestamp` was according to its own documentation "moment of
the receiving (local clock)".

The consequence was a lie with a time on it: a message from yesterday evening
appeared after the logging in with the time of now. **That is worse than a missing
piece of information** — it invites answering a question that has long since settled
itself.

Of all seven points of the list of extent that was the only one at which something
wrong was shown instead of something missing.

`Timestamp` is now the time at which the message was **written**, `ReceivedAt` the
one of the receiving, `IsDelayed` the difference between the two. Read the stanza is
where it is still there — in the connection; the `DateTime.Now` in the client that
overwrote the information is gone.

**Two subtleties, both with a test of their own:**

- **Only direct children.** A carbon (XEP-0280) and a forwarding (XEP-0297) bring
  the stamp of the *inner* message along in their `<forwarded/>`. Whoever searches
  the whole stanza dates the outer one to the time of the inner one — and is wrong
  exactly when it matters.
- **Only with a zone.** That came along through a surviving mutation, and it was the
  most instructive of the day: `RoundtripKind` against `AssumeUniversal` could not
  be struck down. The reason was no weak check, but a gap behind it — a stamp
  **without** a zone offends against section 3, could however be read and was
  interpreted as local time. **The worst of all readings:** the message shifts by
  exactly the difference of the zones and looks completely plausible in doing so.
  Now it counts like no stamp.

After this sharpening the same mutation is **equivalent instead of surviving**: with
a forced zone the two readings can no longer differ, for `AssumeUniversal` takes
hold only where no zone stands. A survivor whose equivalence can be proved is
something other than one that stands there unchecked.

Five mutations: four struck down (stamp not read at all, whole stanza searched,
unreadable stamp throws instead of saying no, zone no longer demanded), one
equivalent.

The console now shows a message handed in later with a date and the note "(handed in
later)" — without the date a time from yesterday would look like today.

---

### D60. "I meant: tomorrow." ✅ — XEP-0308

The correction is an ordinary message with an `id` of its own and the **complete**
text; the `<replace/>` names only which one it replaces. That is intentional: a
recipient that does not know the extension shows it as a second message — ugly, but
complete. Whoever sent only the changed part instead would leave an empty line
behind at their end.

**The limit from section 5 is the actual decision.** Corrected can be only the
message sent last to **the same recipient**. This is why the client remembers the
last id *per recipient* and not in total: a single note would be wrong after every
change of subject — and wrong in such a way that the correction lands at the
previous partner in conversation. The mutation that separates the remembering from
the recipient falls at exactly this case.

And the correction itself becomes the last message, so that a correction can in turn
be corrected. No special case, but the usual one: whoever mistypes mistypes in the
correction as well. Did the second correction still point at the original, the first
would hang in the air at the recipient's end.

**At the receiving it is reported, not decided.** `ReplacesId` and `IsCorrection`
stand at the message; what becomes of it is the business of the surface. A console
cannot take back what is written — it puts a `✎` at the sender and shows both
versions. That is more honest than keeping quiet about the correction: the reader
sees that there was one, and which one holds.

Incidentally the parameter list of the message event has disappeared. It had become
longer with every extension — five values, with the delay stamp eight, with the
correction nine —, and **a row of similar strings whose meaning hangs only on their
position is a confusion waiting for its opportunity.** The connection now puts the
`XMPPMessage` together itself; it is the only place anyway at which the stanza is
still there. On exactly that the delay stamp in D59 had gone past.

Six mutations, all struck down: note not read, whole stanza searched, empty `id` as
the target, `<replace/>` does not go out along, correction does not become the new
last one, the remembering does not hang on the recipient.

Announced the extension is in disco#info (section 4) — without the announcement the
other side has to assume that its correction appears as a second message, and then
prefers to send none.

---

### D61. When nobody is looking ✅ — XEP-0352

The protocol is read in one afternoon: two nonzas, `<active/>` and `<inactive/>`,
announced in the features after the login (section 4.1), and **no answer to them**
(section 4.2) — a confirmation would wake the device at exactly the moment at which
it lies down to sleep.

The work sits elsewhere. **What may be held back the server decides**; the XEP names
only examples in section 3. My guideline: *held back is only what will still be true
later.*

- **Presence waits**, and the last one per full JID replaces the earlier ones ("push
  the latest presence from each contact"). Per full JID and not per human: two
  devices are two presences, and the one may not displace the other — otherwise
  Bob's telephone would disappear from the list because his computer has signed off.
- **A chat state is dropped**, not kept. That is the only point at which anything is
  lost, and it is the most important one: a "is typing" from earlier is at the
  handing in later no longer a late piece of information, but a wrong one.
- **Text, `iq`, errors and every nonza go out at once.** XEP-0352 is a saving
  measure for the battery and no do-not-disturb function for the human in front of
  it. An `iq` is moreover a question with a deadline — whoever holds it back answers
  it after it has run out, and the answer would come to a question nobody is putting
  any more.
- A subscription request is a presence and nevertheless no report of presence: it
  waits for the decision of a human (RFC 6121, section 3.1.3) and goes out at once.

**Two subtleties that show themselves only at the building:**

- **What is held back goes out before the stanza that empties the buffer.** Without
  this rule Bob's message would overtake his own presence, and RFC 6120, section
  10.1 expressly demands the order between two entities. Alice would otherwise see
  first "Bob writes: on my way" and after that that Bob has gone online.
- **A nonza does not empty the buffer.** An `<r/>` of the server (XEP-0198) asks
  after the counter of what has come in and carries no order; did it empty the
  buffer, then every count query would be a wake-up call through the back door. The
  counting stays coherent in doing so, because what is held back is not sent and
  thereby not counted either.

**The buffer has an upper bound** (`MaxHeldWhileInactive`, default 100). A client
that declares itself inactive and then does not come back again would otherwise
force unbounded memory on the server with a single `<inactive/>`. At an overflow the
whole buffer goes out instead of anything being thrown away: the client then gets
traffic it did not want at that moment — the friendlier of the two possibilities.

**And at the end of the connection nothing stays lying.** What was held back has
never reached the client and would not have landed in the buffer of unacknowledged
stanzas either — a resumption would not find it, and nobody would learn of it, for a
stanza never sent is missing from no counting either. The farewell therefore empties
the buffer first; at a kept stream it thereby goes its accustomed way.

**Section 5.2 takes the question about the resumption off one's hands:** "stream
resumption does not affect the current CSI state, which always defaults to 'active'
for new and resumed streams." The server therefore deliberately does *not* take the
state over — and the client declares itself inactive anew after every setting up,
for the device lies in the same pocket as before. Without this repetition every
disturbance would be a silent end of the saving measure, and nobody would notice it:
everything goes on working after all.

Without an announcement the client sends nothing, and without an announcement of its
own the server does not obey. The second case is the more dangerous one: a server
that keeps silent and holds back all the same would let the client hold its contacts
to be quiet. Before the login it does not hold either — otherwise somebody not
logged in would have a state at a session that belongs to nobody yet.

To section 6 (Security Considerations, "servers MUST NOT reveal the clients
active/inactive state to other entities on the network") there was nothing to do and
that is the point: the state changes nothing about the presence and leaves the
session nowhere — there is no automatic "away" that would show it to the contacts.

**21 mutations, all struck down** — subscription request waits, text does not count,
an empty `<body/>` counts as text, all children instead of only the extensions, a
message without an extension expires, replacement per human instead of per device,
no replacement, `iq` held back, nothing held back at all, buffer not taken along,
buffer emptied by nonzas as well, `<active/>` hands in nothing later, no upper bound,
chat state kept instead of dropped, buffer stays lying at the end of the connection,
feature not announced, server obeys without an announcement, somebody not logged in
may set, client sends without an announcement, client does not repeat itself after
the rebuilding, client does not remember its state.

In the console: `/csi` shows the state, `/csi inactive` and `/csi active` report it.

---

### D62. Foreign numbers ✅ — OMEMO, stage 1 of 7: the crypto building blocks

OMEMO is no extension one builds in in one evening. XEP-0384 (version 0.9.1,
`urn:xmpp:omemo:2`) demands X3DH, the double ratchet, a protobuf wire format, PEP
distribution of the device list and bundles, a session store that survives a
restart, and a decision of trust for the human in front of it. That is seven stages;
here is the first, and it is the only one that gets by without XMPP.

**The substructure was there already.** BouncyCastle 2.6.2 hangs in the tree over
Hermod anyway — X25519 and Ed25519 therefore exist without choosing a new
dependency. .NET 10 does not have X25519: in `System.Security.Cryptography.dll` the
string does not appear a single time. The package now stands expressly in the
`.csproj` although it was already there transitively — whoever uses a transitive
dependency directly loses it at the moment at which the previous owner puts it down.

**One gap I had to fill myself, and the way there belongs written down.**
BouncyCastle does not give its `ScalarMultBase` for Ed25519 out; public are only
`Sign` and `Verify`, and both derive the scalar from a seed. XEdDSA however needs a
*given* scalar. The obvious way out — to create the nonce over `GeneratePublicKey`
from a random seed — is a trap: the scalar would then be **clamped**, that is, a
multiple of 8 in a fixed window, about four bits predictable. Exactly at that the
attack on biased nonces aims; a few hundred signatures suffice, and the identity key
falls. **A biased nonce is no blemish, but the usual way such keys are stolen.** So
the point arithmetic itself, with the complete formulas from RFC 8032, section 5.1.4
— and with the express note in the source that it is **not** hardened against
timing. For a client on the device of its user that is the right order of worries;
for a server it would be the wrong one, and it stands there so that nobody later
holds it to be settled.

**Checked it is against foreign numbers.** An encryption checks itself too easily:
whoever can decrypt what they have encrypted themselves has shown that they make the
same error twice. Force of proof have only published vectors — RFC 7748 (sections
5.2 and 6.1), RFC 8032 (section 7.1, three vectors, over the detour of the
Ed25519-own forming of the scalar), RFC 5869, RFC 4231, NIST SP 800-38A. To that a
point both curves name: the X25519 base point `u = 9` has to be the Ed25519 base
point after the conversion.

**The first run found two errors, and they are differently dangerous:**

- `Aes.Create().DecryptCbc(…)` decrypted with a **random** key — I had hung it on
  the object only at the encrypting. That fails always and comes out at once.
- In XEdDSA one computes on with `-k` when `kB` carries the sign bit. My negation
  ran out beyond the order of the group and yielded a negative number — and that
  hits **every second key**. A test with one created key would have been green in
  every second run. Against that there now stands one that goes through 32 keys
  *and counts afterwards that both signs occurred* — otherwise it checks half the
  way and does not say so.

**26 mutations, 23 struck down, three provably equivalent:**

- The check of the length of the signature — without it the foreign checker throws,
  and the exception becomes "invalid" anyway.
- The beginning of the loop at bit 254 instead of 253 — the scalar is reduced modulo
  the order of the group beforehand, the upper bits are always zero afterwards.
- The salt out of 32 zero bytes against 16 — HMAC pads every key below the length of
  a block with zeros, both yield the same value. The 32 stand there all the same,
  because the specification names them that way.

**One surviving mutation was a real hole and forced a test:** the info string of the
derivation could be set to `""` without anything failing — all tests checked the
structure of the 80 bytes, none their value. The error would never have come out in
this house: **two clients with the same wrong string understand each other
perfectly.** Only a foreign far side would get gibberish, and that does not exist
here. Now a second HKDF — the one of BouncyCastle instead of the one of the BCL —
recomputes the same 80 bytes, with the parameters from section 4.4 written down
literally.

That is at the same time the limit of this stage and of the whole series, and it
belongs said in advance: **against a real OMEMO client nothing is checked here.**
Prosody and ejabberd only carry OMEMO, they do not speak it; Conversations, Dino and
Gajim do not exist in the test setup. What remains are published vectors and
prescriptions written down literally — both check the agreement with the text, not
with reality.

---

### D63. Four handshakes ✅ — OMEMO, stage 2 of 7: X3DH

A session begins without both being there at the same time: Bob is offline, Alice
writes to him encrypted all the same. That works only because his server keeps his
keys in stock — **and with that the server is also the obvious attacker.** Exactly
against that stands the signature over the signed PreKey, and this is why a bundle
with a wrong signature breaks off here instead of reporting a warning: a session on
it would be worse than none, for it would look like an encrypted one.

**The four Diffie-Hellmans answer four different questions** — who writes (DH1), who
reads (DH2), is it fresh (DH3), and is this first message different from every other
(DH4). The fourth falls away when the stock of PreKeys is empty; that is expressly
provided for and costs exactly this one property. A refusal would be the poorer
answer — it would make an outage of reachability out of an empty stock.

**The error I made at the writing is the one this extension warns about most
loudly.** XEP-0384 transmits the IdentityKey *always* in Ed25519 form (section
5.3.2), the Diffie-Hellman however computes in Montgomery form. I gave the one
version to the method for the other — and got no error message: both are 32 valid
bytes, the conversion runs through, and out comes a key no signature fits. Now the
two ways are called `Verify` and `VerifyEdwards`. **A `Boolean isEdwards` would have
been invisible at the calling place, and the calling place is where one goes
wrong.**

**For the third time the same pattern at the mutations, and it is the pattern of
this whole undertaking:** the `0xFF` prefix, the info string and the order of the two
IdentityKeys in the associated data could all three be changed without a test saying
anything. The reason is always the same — **both sides compute with the same function
and still agree.** A test that checks "both get the same out" cannot find such a
thing in principle. The damage would occur only against a foreign client, and that
does not exist here.

Against that only one thing helps: **write the prescription down a second time
literally.** The derivation is now recomputed with a second HKDF, and the associated
data is not checked on "both the same" but on which half belongs to whom. Whoever
changes the value in the source has to change it twice — and sees in doing so that
they are leaving the specification.

19 mutations, all struck down: signature unchecked, DH1 and DH2 with swapped keys,
prefix gone, info string gone, associated data twisted (twice), changed signed
PreKey passed over, used-up PreKey accepted, PreKey not deleted at the taking out,
identifiers reused, changed key not signed anew, IdentityKey published in the wrong
form, signature checked against the wrong form.

**An unchecked assumption stands expressly in the source:** the signed PreKey is
signed in Montgomery form. Section 5.3.2 says only "the signed PreKey signature" and
leaves open which encoding is meant. Is the reading not right, then the check against
foreign clients fails at this one line — and there is no far side here at which that
could be decided.

---

### D64. Two ratchets, seven survivors ✅ — OMEMO, stage 3 of 7

The heart of the thing. The symmetric ratchet runs with every message and gives
**forward secrecy** — whoever steals today's state can no longer read yesterday.
The Diffie-Hellman ratchet runs at every change of direction and gives
**break-in recovery** — whoever has stolen the state loses it again as soon as the
two have written in both directions once.

**Errors are silent here, and this is why the tests look different.** A ratchet
that does not run on still encrypts perfectly — it just does it again and again
with the same key. A test that checks "there and back yields the plain text" would
pass even then. Checked is therefore additionally that ciphertexts *differ*, that
keys *disappear* and that a message in the wrong place is *refused*.

**And seven of twenty mutations survived the first run all the same.** That is the
most important finding of this series, for three of them were not merely questions
of interoperation but abolitions of the security:

- **`mk` and `ck` out of the same constant.** Then the message key is at the same
  time the next chain key: whoever reads along a single message computes the whole
  further chain. **Out of forward secrecy becomes its exact opposite.**
- **Root and chain out of the same half** of the 64 derived bytes. Then the root
  key is known as soon as a chain key is.
- **Salt and input material of the root chain swapped.**

The reason is always the same and by now the red thread of this undertaking:
**both sides compute with the same function and still agree.** At D62 and D63 that
cost only the understanding with foreign clients — here it costs the property for
whose sake the whole procedure exists.

The remedy is the same as twice before: **write the prescription down a second time
literally.** For that `DeriveRootChain`, `AdvanceChain` and `Material` are now
individually within reach and are held against a second HKDF.

**Two test errors of my own came to light in doing so, and both are more
instructive than the code:**

- `TheChainConstants_AreDistinct` **checked nothing at all.** It recomputed
  `HMAC(ck,0x01)` and `HMAC(ck,0x02)` in the test itself and established that they
  differ — about the source it said not a word. It would have passed even if the
  implementation had taken `0x01` both times. **A test that recomputes the
  prescription instead of asking the code is a doubling of the prescription and no
  check.**
- `ATamperedMessage_IsRefused` **poisoned itself.** Three cases one after another
  on the same pair of ratchets — but a *refused* message changes the state all the
  same: it was wound forward, a key is used up. The third case, the foreign
  associated data, would have struck the HMAC mutation down but threw for a quite
  different reason. Every case now gets a fresh pair.

**The upper bound of the skipped keys has delivered its own proof.** Without it the
test host crashed — not one test failed, the whole process died at a single message
with `n = 4000000000` and left a **crash dump of 32 GB** behind. Exactly that is the
attack: a stranger needs neither keys nor access, only this one number. The run
reported "passed, 4 of 13" in doing so — and that is the trap from D54 in its purest
form: **a run that reports four of thirteen tests is no passed run.** Looked up
where it died instead of believing the summary.

Incidentally the encoding of the message header has drawn itself forward although
it belongs to stage 4: the associated data of the encryption is
`ad ‖ OMEMOMessage.proto(header)` (section 4.3). With a provisional encoding the
ratchet would have been checked against something that is replaced later. Protocol
Buffers by hand, and the reason is not thrift: these bytes have to be **reproducible
bit for bit**, and a library that reorders fields or leaves defaults out would be no
help here but a source of errors nobody sees.

20 mutations, all struck down — six of them only after the tests had been improved,
and one through its killing the process.

---

### D65. Three bytes nobody would have seen ✅ — OMEMO, stage 4 of 7

The wire format: the three protobuf messages, the `<encrypted/>` element and the
SCE envelope from XEP-0420.

**The most important find came this time at the reading, not through a mutation.**
Section 4.3 says the HMAC runs over `ad ‖ OMEMOMessage.proto` — "after ciphertext is
added to the proto". In D64 the ciphertext hung **raw behind the header**; demanded
it is as field 4 *inside* the encoded message. The difference is three bytes, field
tag `22` and the length — and **both sides of this house would never have noticed
it.** Against a foreign client not a single checksum would have been right.

With that it is the same family for the fourth time: D62 the info string, D63 the
associated data, D64 the root chain, now the embedding. **All four have in common
that our own tests could not find them** — not because they were poor, but because a
test does not distinguish in principle between "right" and "equally wrong on both
sides" as long as both sides are the same code.

**Three decisions in the format:**

- **The HMAC stands outside the message.** Stood it inside, it would check itself
  along; this is why the envelope `OMEMOAuthenticatedMessage` — inside the message,
  outside its attestation.
- **`kex='false'` is not written.** Section 4.5 gives the attribute this default,
  and a written-out default travels along at every message to every device without
  ever meaning anything.
- **The key is looked up over the JID *and* the device id.** The id is a random
  number per device; two accounts can carry the same one. Whoever looked it up by
  that alone would under some circumstances take the entry of a foreign account and
  fail at a decryption whose reason they do not see.

At the SCE envelope the reasoning is more important than the code. **Encrypted is
not the text, but a whole stanza envelope.** Whoever encrypts only the `<body/>`
leaves chat states, receipts and notes of correction standing in plain text — the
content would be protected, the conversation not. The sender stands **inside** the
envelope, because outside it is changeable by anybody; without this comparison a
ciphertext could be intercepted and handed on under a foreign name. And the
`<rpad/>` is no decoration: without it the length of the ciphertext betrays the
length of the message, and at "yes" and "no" that is the whole content.

19 mutations, all struck down — **the two survivors of the first run were again
errors in my tests**, and both of the silent sort:

- The check of the length of the MAC could be removed without anything happening:
  my test case packed random bytes in as the inner message, and those failed at the
  protobuf reading already. **The test therefore passed for the wrong reason.** Now
  there stands an otherwise perfect message there — and a counter-check that fails
  if the case does not get through at all any more.
- The search for `kex='false'` in the emitted XML could **never** hold:
  `XElement.ToString` writes attributes with double quotation marks. The test always
  passed, even when the mutation wrote the default out. Asked is now the attribute
  itself.

Both are the same lesson as in D64: **a test that looks for a string in the output
text or produces an error case over a different error does not check what its name
claims.** Found it the mutation did, not the reading.

---

### D66. The server answers for somebody absent ✅ — OMEMO, stage 5 of 7

The first stage in four that checks XMPP again instead of cryptography — and thereby
the first at which a pass says more than a recomputed prescription.

**For that the test server has got PEP** (XEP-0163, as a subset: publish, fetch,
notify). Without that O5 would not have been checkable at all: Prosody and ejabberd
we reach only over S2S, never as our own home server, and our client speaks
exclusively WebSocket. What is missing stands in the source — no configuration of
nodes, no access models, no notifications filtered over XEP-0115.

**The most important decision: PEP is handled before the forwarding.** A request to
`bob@domain` looks like a request to Bob and would otherwise go to his session — then
a bundle would be fetchable only as long as Bob is online, and exactly for that PEP
does not exist. **The server answers as a stand-in for a human who is not there at
the moment**, and that is the whole promise of this stage.

**An old error came to light in doing so, and it did not lie in the new code.**
PubSub notifications were handled exclusively in `ProcessIq`. In practice they come
as `<message type='headline'/>` — half of it did not exist although the comment
beside it has always claimed "can come as message or iq". It came out only when with
OMEMO somebody was *dependent* on a notification for the first time; the same
half-wired corner as in D38.

21 mutations, all struck down — **six survived the first run, and five of them were
real gaps**, no equivalences:

- **An empty `<spk/>` got through.** Empty Base64 is valid Base64 and yields a field
  of zero bytes; further down an exception out of the curve arithmetic would have
  become of it, with a message that says to nobody that a bundle was unusable. Now
  the lengths are checked, where they count.
- **The item id `current` could be renamed** — for the fifth time the family "both
  sides use the same constant and still find each other".
- **The item id at the fetching could be passed over.** With one published device the
  same result; with two the sender gets the **wrong bundle** and encrypts for a
  telephone that is not reading along at all.
- **A refusal of the server counted as a success** — exactly the return value for
  whose sake these methods have one at all.
- An empty node was answered as an empty result instead of as `<item-not-found/>`.

**And a test error that carries a lesson of its own:** my test for "a foreign device
list sets off no re-entry" checked whether Alice's list stayed unchanged. That was
worthless — **the server refuses foreign nodes anyway**, so it stayed clean even
when Bob's client tried it. What has to be asked is whether the thing under test
sent something, not whether its neighbour fended it off. **A test that measures the
effect at the wrong place checks the wrong safeguard.**

Incidentally a nullable warning of the compiler caught a swapped parameter before
any test ran: a JID in the place of the error condition.

---

### D67. A run against a red baseline ✅ — OMEMO, stage 6 of 7

The session store. **Without it every reconnection is a breach of trust:** a new
IdentityKey means a new fingerprint, and every comparison any human has ever made is
thereby worthless. A client that creates new keys at every start looks to its
contacts like an attacker — every time.

The check is the same at every test: **restart and go on.** A store that lays down
and gives out again is not one yet — it has to lay down so much that the far side
notices nothing of the restart. Checked is therefore not whether the state looks the
same, but whether the conversation goes on.

**The replaced signed PreKey is now kept** — exactly one. That had been outstanding
since D63, expressly postponed because without a store it would have been a promise
nobody keeps. Every further kept key would take back a piece of what the change
exists for.

**The signature is taken along and not computed anew.** XEdDSA mixes randomness into
every one — the new one would look different from the published one, and the bundle
in the PEP node would be at odds with the device.

**A changed IdentityKey is reported and never taken over silently.** There are two
explanations for it — a newly set up device or somebody in between — and from outside
they cannot be told apart. The old note stays standing together with the decision of
trust; whoever overwrote it would make an unconfirmed identity out of a confirmed
one, and the warning would be gone after the first looking.

**An unreadable file throws instead of starting fresh.** The convenient way would be
the dangerous one here: out of a recoverable error of reading would become a silent
change of our own fingerprint, and the old file would be overwritten at the first
laying down.

## The actual find: a mutation run that measured nothing

**The first O6 mutation run reported twenty of twenty struck down — and was
worthless.** The change at the signed PreKey had broken an existing X3DH test that
held exactly the opposite fast ("every one except the current is refused"). This test
ran along in the mutation filter. **With that every run reported "error", whether the
mutation struck something down or not.**

It came out only because a single mutation was too conveniently dead for me — the
side file at the writing. Let it run singly: it survives. And three runs of the
*unchanged* baseline then showed the same failure three times.

That is the trap from D54 in a new shape. Back then a green run measured nothing
because tests skipped themselves; here a red run measured nothing because it was red
before already. **A mutation run without a green baseline cannot distinguish between
"my mutation was found" and "something was broken here before already".** The
baseline belongs checked before every batch, not assumed.

Measured anew against the green baseline: **20 mutations, 19 struck down.** Two of
the three survivors were real gaps and each forced a test — the replaced signed
PreKey did not survive the restart, and a session laid down twice was laid down
beside instead of replaced. The second case would have been the worst damage this
store can do: after a restart the ratchet would stand at an old state, and everything
since then would be unreadable for both sides, without a recognisable reason.

**The one real survivor, named instead of talked away:** the writing over a side file
can be replaced by a direct writing without a test saying anything. The difference
shows itself only at a crash **in the middle of the writing**, and that this suite
does not produce. It is thereby not equivalent, but unchecked — and that is a
difference that belongs written down here.

**And one thing stands there expressly instead of being replaced by a soothing
procedure:** the file is not encrypted. It contains the secret IdentityKey, all
PreKeys and every chain key; whoever reads it reads the conversations along. An
encryption with a key that lay beside it would be none — and one a human enters does
not exist in this application. A test holds it fast so that whoever changes it has to
change the remark along.

---

### D68. The first encrypted message ✅ — OMEMO, stage 7 of 7

Everything brought together: Alice switches on, writes, Bob reads. In between lie the
creation of keys, PEP publication, fetching of the bundle, X3DH, ratchet, protobuf,
SCE and the store — and the test touches none of them individually.

**The test is worth something only through what it excludes:** the plain text may
appear in no stanza the server has seen. To that a counter-check that an OMEMO stanza
went over the wire at all — without it it would pass even if nothing were sent.

**Three decisions at the wiring:**

- **A device without a fetchable bundle is skipped and named.** Not to send would
  make a human unreachable through a single broken device. To send unencrypted would
  be the worst of the three answers: the sender then believes they have encrypted —
  and whoever makes a bundle unreachable gets the plain text.
- **Without OMEMO switched on it throws.** An exception is loud, a message sent
  unencrypted is not.
- **Blind trust before verification as the default**, with a reason: a procedure that
  demands a comparison of fingerprints before the first message is not used — and
  unused encryption protects nobody.

## The weakest mutation run of the series

**Eight of fourteen survived the first pass** — by far the poorest result of these
seven stages. The reason is instructive: the end-to-end tests are **broad but blunt**.
They check that it works, not why. A conversation between two clients runs through
even when half the care is missing.

Six of the eight were real gaps, and each forced a test:

- **Two messages one after another without an answer in between.** In an exchange a
  missing laying down of the session does not come out — the decrypting of the answer
  lays it down anyway. Only two messages in a row show whether the *sending* keeps
  its progress.
- **Our own second device reads along, the sending one does not.** Both hang on the
  same line, and the mutations got through in both directions.
- **The sender stands in the envelope** — and is compared. As long as the information
  was only carried along and not passed as far as the caller, the check could not be
  shown.
- Used-up PreKey at once in the store, a changed IdentityKey stops the message, and
  the switching on adds to the device list instead of overwriting it.

**Two finds no mutation run brought out, but the new tests themselves:**

- **OMEMO messages arriving over carbons were not decrypted.** Exactly like that a
  second device of our own sees what the first has written — the key entry was there,
  the message arrived, and nobody looked at it, because it sits in the `<forwarded/>`.
  The same family as "only direct children" from D59, D60 and D65, only the other way
  round: there one may **not** look in, here one **has to**.
- My own test grabbed the wrong stanza: the first with `urn:xmpp:omemo:2` is a PEP
  publication and no message.

**One test had to take a detour, and the reason belongs written down.** "The
switching on adds to the device list" cannot be checked with two real clients: does
the second device displace the first, then the first notices the PEP notification and
enters itself again at once (D66) — the end state is right again, and the test sees
nothing. Now there stands an entry there for a device **that does not exist at all**:
it cannot defend itself, and thereby what the switching on does stays visible.

**The last survivor demanded really building the attack.** Alice writes to Bob and
Mallory at the same time; Mallory hands the same `<encrypted/>` stanza on to Bob
unchanged, under her own name. Bob's entry is untouched, the ratchet step works out,
the checksum is right — **everything cryptographically perfect**. Only inside it says
"from Alice" and outside "from Mallory". Exactly for that the associated data from
XEP-0420 exists, and only this test shows it.

14 mutations, all struck down — six of them only after the sharpening.

**What this series cannot do now stands in the README:** checked against no foreign
OMEMO client; the session store unencrypted; the point arithmetic not hardened
against timing; no MUC and thereby no group encryption; no schedule for the change of
the signed PreKey.

---

### D69. A far side nobody here wrote ✅ — OMEMO against the reference

Seven stages long the same limit stood in the README: **checked against no foreign
client.** And seven times the finding was the same — the info string (D62), the
associated data (D63), the root chain (D64), the embedding of the ciphertext (D65),
the item id (D66). **Every time two clients of this house would have understood each
other perfectly and not a single foreign one.**

The reason is no negligence but a property of the arrangement: **are both sides the
same code, then they agree even when both compute equally wrongly.** A test cannot
distinguish that in principle.

Now the far side exists: **python-omemo (Syndace)**, the reference implementation for
`urn:xmpp:omemo:2` — the same version we speak. And in both directions at that:

- **It accepts our bundle.** In doing so it checks our signature over the signed
  PreKey with its own idea of what it goes over. **With that the unchecked assumption
  from D63 is decided** — signed in Montgomery form, and the reading is right.
- **We read what it writes.** Checked in one go: encoding of the bundle, order of the
  four Diffie-Hellmans, info string of X3DH, `0xFF` prefix, associated data out of
  both IdentityKeys, beginning of the ratchet, info strings of the root chain and the
  message key, the constants `0x01`/`0x02`, protobuf field numbers, embedding of the
  ciphertext, truncation of the HMAC, derivation of the payload.
- **It reads what we write.** The direction that decides whether anybody can read us
  — and the one most easily forgotten, because its staying out looks like silence:
  whoever never gets an answer does not know whether nobody wanted to write or nobody
  could read.

**Every single point of this list was previously a surviving mutation or a find at
the reading.** Three tests would have found all five.

## Without changing anything on the system

`sudo` demands a password, and that I do not enter. So differently: wheels are zip
files. Eleven packages fetched and unpacked, `PYTHONPATH` in front of it — **no pip,
no venv, no sudo.** For a test setup that is even better than an installation:
reproducible, and nothing stays behind. The script comes with it
(`Oracle/fetch_oracle.py`).

Two stumbling blocks along the way, both held fast: **`cffi` belongs to it**, even if
it does not look like it — without it XEdDSA does not find its native library and
falls back on a variant that expects a browser. And **pydantic pins `pydantic-core`
exactly**; whoever takes the newest of every package gets two that do not fit
together. That is the work pip otherwise does.

The tests **skip themselves** when the oracle is not there — like the ones against
Prosody and ejabberd. A run without WSL is not to be red, only to say less.

## What is not checked even now

And that belongs there just as clearly as the result: the **SCE envelope** stays
outside — python-omemo leaves it to the application, and an envelope I built in the
oracle myself would be no foreign check, but the same assumption twice. Just as
little checked: the `<encrypted/>` element, the PEP nodes, a conversation over
several messages — and a real client over a real connection all the less.

---

### D70. A confirmation that has an effect ✅ — the server learns subscriptions

The occasion is the question about the outgoing correlation (a point under
"optional", since D38). Before a client can learn to evaluate answers, there have to
be answers that say something: **this server said `<service-unavailable/>` to every
`subscribe`** — it did not know the request. Whoever knows only refusals cannot show
that they read a confirmation rightly.

So the server first. XEP-0060, sections 6.1 and 6.2: `<subscribe/>` and
`<unsubscribe/>` with a `subid`, with the three refusals of the XEP —
`<item-not-found/>` for a node that does not exist, `<invalid-jid/>` when somebody
wants to sign another up, `<not-subscribed/>` and `<invalid-subid/>` at the
cancelling.

**And with an effect, not merely with an answer.** A subscription that takes effect
nowhere would be exactly the promise without backing for which an event never set off
was struck in D57. Until now a PEP notification was got by whoever got presence
anyway — with that "to subscribe" was only another word for "to stand in the roster",
and for a foreign node there was no way at all. Now the reports go to **one** list out
of both sources; whoever comes into question over both gets them once all the same.

The sharpest of the new checks is the one on the `jid`, and in both directions at
that: **to create** a foreign subscription is a nuisance — somebody would get reports
they never ordered, from a node whose name they do not know. **To end** a foreign one
is a deprivation: the one concerned would get nothing more and would not notice it,
for a staying out looks like quiet.

Exactly this second check was unchecked at first: **one of fourteen mutations
survived**, the removed JID check at the cancelling. The added test checks both — the
refusal *and* that Carol's subscription still carries afterwards. To check only the
refusal would let an implementation through that first signs off and then complains.

Twelve tests, fourteen mutations, all struck down. Full run: 962 passed, 7 skipped.

**What the server still cannot do** and what the client thereby never gets to see:
several simultaneous subscriptions of the same JID to the same node — that is what
the `subid` exists for in the first place. It is given out and checked all the same,
for it names a subscription unambiguously; only the case that makes it indispensable
does not occur here.

---

### D71. First the answer, then the bookkeeping ✅ — the outgoing correlation

The point had stood under "optional" since D38, and the error was the same the whole
time: `PubSubSubscribeAsync` sent the request off and entered the subscription **in
the same line**. A refused one afterwards stood there as an existing one, and the
caller never learned it.

**It is the same sort of error as the five from the OMEMO series, only without
cryptography: a claim about something nobody has looked up.** It does not come out
for a long time, because in the good case it is right.

Now each of the six requests goes over `SendIqAsync`, each with an id of its own —
up to here all `subscribe` carried the same fixed `pubsub-sub`, which was without
consequence as long as nobody assigned anything, and at the first assigning would
have supplied the second request with the answer to the first. Entered it is after
the `result`, deleted likewise: **whoever deletes the entry before the answer makes
the same error the other way round** and discards the reports of a subscription that
still exists.

Of the result there remains what only the service knows: the `subid`. It goes along
at the cancelling — prescribed it is only at several subscriptions to the same node,
but it names the one unambiguously as well.

`PubSubGetItemsAsync` had the same illness in its clearest form: it sent the request
off and was finished. The answer arrived, belonged to nobody and fell out of the
receiving — **the items it was about nobody ever saw.** Now it gives them back.

## A subscription that brought nothing in

In doing so the find of this stage came out: **the protection against spoofing
discarded every PEP report.** It compared the sender with the PubSub service of the
domain — a PEP report however comes under XEP-0163 from the account itself. It never
came out because up to this point nobody had a subscription whose reports anybody
expected; OMEMO goes its own way.

A confirmed subscription now additionally allows the one at whom it exists — **and
for its node at that, not in general.** Whoever has subscribed to the weather node at
Bob has not allowed Bob to send reports about every other one one might think of.
Exactly for that the address in the bookkeeping is the one that was *asked*, and not
the `from` of the answer: otherwise a far side could declare itself the source.

## Three mutations that uncovered a coincidence

Of fifteen mutations three survived, and all three pointed at the same gap:
**answers a well-behaved server does not give.** A `result` without a confirmation, a
confirmation without a node, a state this client does not know. Against our own
server such a thing never comes — the refusal therefore hung not on a decision but on
there happening to be no confirmation in an error answer.

Checkable they became over a test switch: `AnswerPepRequests` lets the server keep
silent so that the test can play the service itself — like `AnswerPings` for
XEP-0199. It carries at the same time the case one most easily handles wrongly,
because it does not report itself: **silence is no confirmation.**

Seventeen tests, fifteen mutations, all struck down. Full run: 977 passed, 7 skipped.

---

### D72. What the subid exists for ✅ — several subscriptions to the same node

At the end of D71 the limit stood in the README: several simultaneous
subscriptions of the same JID to the same node — **the case the `subid` exists for
in the first place** — are not implemented. Until then a second `subscribe` gave
the same id back, and with that the id was decoration: where there are never two,
it names nothing the node does not name as well.

**The case is not invented.** It arises of itself when a client restarts and
subscribes again without knowing its old id. Afterwards the service has two, and
from then on every cancelling without an id is ambiguous — the client from D71 can
land exactly there.

Now every `subscribe` is a subscription of its own with an id of its own. Three
things follow from that, and each of them is a decision that could have turned out
differently:

- **Cancelling without an id is refused when there are several** —
  `<bad-request/>` with `<subid-required/>` (section 6.2.3.1). To choose one would
  be the convenient answer and the wrong one: the service would perhaps end the
  other and confirm to the sender that it had been theirs.
- **Delivered is per subscription**, not per subscriber, and every delivery names
  its subscription in the SHIM header `SubID` (section 12.20).
- **Express beats incidental.** Whoever has subscribed to the node does not get the
  report additionally over the presence — otherwise the number of deliveries would
  hang on whether somebody also happens to stand in the roster. And the presence
  delivery carries no id, for there is none: an invented one would be worse than
  none, the recipient could afterwards want to cancel what was never ordered.

A test of the previous stage claimed the opposite
(`SubscribingTwice_KeepsOneSubscription`) and is replaced. That had not been wrong
— a service may proceed that way — but it was the version without the thing.

**What is still missing**, and it is the reason two subscriptions otherwise differ
at all: the configuration per subscription (section 6.3). Without it a second
brings in nothing but a second delivery. The server has to answer rightly all the
same when there is one — that is the whole point of this stage.

Fifteen tests, ten mutations, all struck down. Full run: 980 passed, 7 skipped.

---

### D73. Two subscriptions nobody confuses ✅ — the id on the client side

The other side to D72, and it had an error of its own: **the client held exactly
one subscription per node**, and a second overwrote the first. With that its id was
gone — and gone means here more than "forgotten": it could **never be cancelled
again**, for the service demands an id when there are several, and nobody knew it
any more.

Now there stands a list per node. From that follows the behaviour that matters: **at
several and without an id the client does not ask in the first place.** The service
would refuse it with `<subid-required/>`, the client knows that itself — and what it
does not do is more important than what it does: choose one. That would perhaps end
the wrong one, and the caller would hold it to be the one meant.

An id that does not stand here goes out all the same when the caller names it:
another device of the same account can hold a subscription this client knows nothing
about. The bookkeeping is our own view and not the truth about the service.

Incoming the client now reads the SHIM header `SubID` and hangs it on the event. It
stands **beside** the `event` and not in it, and that is no formality: it says
something about the delivery, not about the event. The same publication arrives twice
at two subscriptions — then this header is the only thing in which the two reports
differ.

A test holds fast what easily gets lost: **after the last cancelling the sender is a
stranger again.** The permission of the protection against spoofing hangs on the
bookkeeping; would an empty remainder stay standing there, the permission would stay
as well, and the protection would be permanently open for this node. Exactly that was
one of the eight mutations.

The console can now do `/pubsub subs` — at several subscriptions to the same node
the id is the only thing that distinguishes them, and whoever wants to cancel has to
be able to look it up.

Twenty-two tests, eight mutations, all struck down. Full run: 985 passed, 7 skipped.

---

### D74. One field, and that is the statement ✅ — configuration per subscription

The last open point from D72: **the configuration per subscription** (XEP-0060,
section 6.3) — the reason two subscriptions of the same JID to the same node can
differ at all. Up to here two subscriptions were two identical things, and the second
brought in nothing but a second delivery. Now the `subid` is not only an id but **the
address of a setting**.

**The form has exactly one field: `pubsub#deliver`.** XEP-0060 knows a dozen others —
digests, expiry deadlines, depth, presence filters. What this server cannot do it
does not offer either: a form with `pubsub#digest` in it that then has no effect
would be a promise without backing, and one the subscriber cannot check at that — **a
digest that stays out looks like quiet.**

For the same reason a field that did not stand in the offer is **refused instead of
passed over**. That is stricter than XEP-0004 demands: whoever swallows the unknown
silently leaves the sender in the belief that their setting holds. A refusal one can
read, an effect that stays out one cannot.

Three decisions that could have turned out differently:

- **A silenced subscription does not fall back on the presence delivery.** Whoever
  has said that they want to get nothing gets nothing — even when they happen to
  stand in the roster. Anything else would mean undermining an express setting over
  a second way.
- **A `set` without a form is refused**, instead of the defaults being put in. Out of
  an incomplete request would otherwise become a change nobody demanded — and it
  would hit of all people the one who had just set something else.
- **If the id is missing at several, the error is a different one than at the
  cancelling**: `<not-acceptable/>` instead of `<bad-request/>` (sections 6.3.3
  against 6.2.3.1). That is no arbitrariness of the XEP — there the request is
  incomplete, here it is in order and only not answerable in this situation. An
  implementation that treats both places alike has not read one of them. This is why
  the common lookup delivers the **finding** and not the answer.

The JID check now stands at three places, and the third is the quietest: **whoever
were allowed to configure foreign subscriptions could switch them off silently.** The
subscription would stay standing — only nothing would arrive any more, and the one
concerned would find nothing conspicuous in their own list.

Twenty-six tests, eleven mutations, all struck down. Full run: 996 passed, 7 skipped
— and with that the suite has passed the thousand.

---

### D75. Strict at obeying, lenient at reading ✅ — the setting on the client side

The other side to D74, and it brought a distinction along that stood nowhere before:
**the same form is read differently in two directions.**

- A **sent** form is an *instruction*. A field in it that nobody offered is refused —
  one passed over would be a discarded instruction the sender learns nothing about.
- An **offered** form is a piece of *information*. A field in it this client cannot
  set is passed over — whoever failed at that could speak with no real service, for
  that one offers a dozen.

That is no contradiction, but the direction. It also has a limit, and a surviving
mutation showed it: **an offer that does not name the delivery at all says nothing
about it** — to put the default in would mean inventing it. The same one level
higher, likewise found by a mutation: a `result` without a form is no information
about the settings. To conclude from the staying out of an error to a state is the
most convenient way of imagining something — and here especially delicate, because
the default says "is delivered": the client would hold a silenced subscription to be
a loud one.

Noted is only what the service has confirmed — the same error as in D71, only one
level deeper. And the note hits **the named subscription**, not the node: a third
surviving mutation showed that the error would be mute, for the service set the right
one and only our own bookkeeping showed a state afterwards that does not exist.

`null` means in this bookkeeping **"not asked" and not "default"**. Asked it is even
when something stands there already: another device of the same account can have
changed the same subscription in the meantime, and then our own entry would be a
memory and no information.

The choice of the subscription meant is now shared by cancelling and configuring —
the same rule, one place: **at several and without an id it does not ask in the first
place.**

Twenty-nine tests, fourteen mutations, all struck down. Full run: 1003 passed, 7
skipped.

---

### D76. A node before anything stands in it ✅ — creating and configuring

Until now "the node exists" meant the same as "something stands in it". That sounded
harmless and was not: **the creating was without consequence** — the client could
send `<create/>` and got `<service-unavailable/>` —, and a node without a store would
never have been subscribable at all.

Now there are the two separately: the settings of a node and its content. A created
node exists before anything stands in it.

**Three fields, and each does something** (XEP-0060 knows two dozen):

- `pubsub#max_items` — what the node keeps. A smaller bound holds **at once** and
  not only at the next publishing: whoever sets it does not want so many kept, and on
  a node in which nothing ever appears again everything would otherwise stay lying.
- `pubsub#persist_items` — keep or only report. A node without a store still reports;
  whoever was not listening has missed it.
- `pubsub#access_model` — who gets at the items. **Stored but not yet enforced**;
  that is K8, and until then it stands so in the README.

Offered is only what has an effect. At an access model a promise without backing
would be the dearest: **whoever sets `whitelist` and gets `open` believes their items
protected and has published them.** This is why this server knows `open` and
`presence` — and refuses everything else instead of kindly shortening it to `open`. A
mutation that did exactly that was struck down.

A partial form changes only what stands in it (section 8.2.4). To fill the missing
fields with the default would be the obvious shortcut and a silent change of what
nobody asked for — for that too there was a mutation.

And `max_items=0` is no formal error but a trap: a node that may keep nothing would
look like one nobody writes into.

Incidentally a small common building block for XEP-0004 arose (`DataForm`): two forms
build the same fields and read the same truth value — to write the same thing twice
means changing it once and forgetting it once. A model of forms it expressly is not.

Thirty-nine tests, fourteen mutations, all struck down. Full run: 1016 passed, 7
skipped.

---

### D77. A condition nobody has read since D66 ✅ — access model and publish-options

Two things that belong together: the access model from D76 was **stored and without
effect** — exactly the sort of promise this whole series argues against. And the
conditions OMEMO has sent along with every publication since D66 **nobody has ever
looked at**.

The second is the quieter error. The client demanded an open node for its bundle, got
a `result` and was allowed to assume it was fetchable. A `result` to a request with
conditions means "conditions fulfilled" — there just never were any. XEP-0384,
section 5.2 demands the open model for a concrete reason: **whoever wants to write
encrypted has to be able to read the bundle, and that is in case of doubt somebody who
stands in no roster.**

Now both have an effect. `presence` shuts out whoever may not see the presence of the
owner — at the fetching as at the subscribing, with `<not-authorized/>` and
`<presence-subscription-required/>`. The owner always gets at their node; they are no
presence subscriber at themselves, and a model that shut them out would not deserve
the name.

**A condition and a setting are not the same**, and the difference lies in a `null`:
it means "that is not asked about" and not "default". Whoever confuses the two refuses
a publication because the node deviates from the default in a point the sender never
said anything about. That was the only surviving mutation, and the added test checks
exactly this sentence.

An unfulfilled condition holds the publication up **entirely**: a service that refused
the condition and laid the item down all the same would have done the opposite of what
conditions exist for.

Honestly said with it: the model betrays that the node exists — whoever has no access
gets `<not-authorized/>` and not `<item-not-found/>`. That is how the XEP provides for
it, and it stays a piece of information: for a node whose mere existence would be a
secret, `presence` is the wrong means.

Forty-eight tests, eleven mutations, all struck down. Full run: 1025 passed, 7
skipped.

---

### D78. Creating and setting in one go ✅ — the nodes on the client side

The client side of D76/D77, and it has a point of its own: **`<create/>` and
`<configure/>` go out together.** Two steps would have a gap — the node would stand
open between the creating and the setting, and whoever asks in this time gets. XEP-0060,
section 8.1.3 does not provide for that without a reason.

Otherwise the same rules as in D75, and that is the point: they were not invented for
the settings of subscriptions, but for forms in general. A `result` without a form is
no information — here the default would be especially misleading, for it says `open`,
and the client would show a protected node as an open one. A `type='error'` stays a
refusal even when a complete form stands in it; that was the only surviving mutation,
and the test for it is word for word the same thought as in D71.

The console builds on the **state it has read** at the changing of the access and not
on the default. Otherwise a `/pubsub access` would incidentally reset the store and
the number of items — a change nobody asked for, and the quietest way of losing one's
own configuration.

Thirty-four tests, eight mutations, all struck down. Full run: 1030 passed, 7 skipped.

With that the PubSub series (D70–D78) is closed. **What is still missing of
XEP-0060**, and it stands so in the README: collective queries (`<subscriptions/>`,
`<affiliations/>`), the deleting and emptying of nodes, `<retract/>`, the procedures
of approval behind `authorize`, and the access models `roster` and `whitelist`.

---

### D79. The question nobody can answer for themselves ✅ — `<subscriptions/>`

XEP-0060, section 5.6: one request, and all our own subscriptions stand there —
across all nodes, with node, id and state; on request narrowed to one node.

**The occasion is a hole the last stages made themselves.** The `PubSubManager` is
created in `InitialiseManagers`, and that runs at every building of a connection —
only the stream management manager survives a reconnect, expressly and with a comment.
Afterwards the bookkeeping is empty, the subscriptions however are not: they stand at
the account and outlast. The client therefore knows not a single `subid` any more, and
since D72 the service refuses an `unsubscribe` without an id as soon as there are
several. Whoever then subscribes anew has two and can end none of them.

That is exactly the fix I gave as the reason for D72 ("a client restarts and
subscribes again") — **without noticing that our own client runs into it at every
break.**

The sharpest rule stands in one sentence: **enumerated are the subscriptions of the
one asking, never those of another.** That is no question of interpretation — whoever
were allowed to enumerate foreign ones would learn who is interested in what. A piece
of information about humans, not about nodes.

And no subscriptions are an empty list and no error: the question was answerable, the
answer reads "none". An error would mean something else — that the question could not
be put —, and a client would afterwards have to guess what it was down to.

Fifty-three tests, seven mutations, all struck down. Full run: 1035 passed, 7 skipped.

---

### D80. Back to the ids ✅ — the collective query on the client side

The other side to D79, and with it the fix from D72 is resolvable: the client fetches
its subscriptions from the service and knows afterwards again what it holds. **One
test spans the whole arc** — create two subscriptions, let the connection break,
check that the bookkeeping really is empty (otherwise it would check nothing), fetch,
and cancel with the id found again.

Three distinctions, each forced by a surviving mutation:

- **An empty enumeration is something other than a missing one.** Empty means "you
  have none" and empties the bookkeeping rightly; missing means "about that nothing
  stands here". To equate the two costs the whole bookkeeping — the ids would be gone
  although the subscriptions exist.
- **An enumeration holds for its service**, not for all. To conclude from the silence
  of the one to the end of the subscriptions at the other would be a loss without an
  occasion. Likewise at the narrowing to one node: what was not asked about stays
  standing.
- **What is enumerated is not always a subscription.** Section 5.6 names every state,
  `pending` as well. Our own server always says `subscribed`; a foreign one with a
  procedure of approval does not — and then an applied-for subscription would stand
  there as an existing one. The same error as in D71, only carried in over the
  collective query.

**Of itself nothing happens.** A client that spoke to a PubSub service unasked at
every building of a connection would send a request for a feature most never use — and
against an address that possibly does not exist at all. The console has two commands
for that instead of one: `subs` shows what this client believes it knows, `sync` asks
the service. Those are two different questions, and this series has worked at telling
them apart for nine stages.

Forty-one tests, nine mutations, all struck down. Full run: 1042 passed, 7 skipped.

**What remains of the collective queries**: `<affiliations/>` (section 5.7) and the
owner's view of the subscribers of a node (section 8.8). The first would be almost
empty today — this server knows no affiliations, a PEP node belongs to its account and
all others have nothing. It is worth it only when `publisher`, `member` and `outcast`
really decide something at the publishing and subscribing; before that one would set a
role nobody checks.

---

### D81. Roles that decide something ✅ — affiliations

In D80 it stood that `<affiliations/>` is worth it only when `publisher`, `member` and
`outcast` really decide something at the publishing and subscribing. So not the
enumeration first, but what it enumerates:

- **`publisher`** may write into a foreign node. The report comes **from the owner**
  all the same — came it from the one writing, it would be a false statement about the
  origin, and the protection against spoofing of the recipient would be right to
  discard it.
- **`outcast`** gets at no node, however open it stands, **and loses existing
  subscriptions** (section 8.9.4). To hinder them only at new ones would mean making
  the exclusion depend on the chance of whether they were there before.
- **`member`** decides nothing yet — that is K13, and until then it stands so in the
  README. The role is offered all the same, because it could otherwise not be given
  out before the access model needs it.

**The owner is no entry but the account.** They stand in the list without anybody
having entered them, and cannot be re-entered: whoever could do that could take their
own account away from another.

Two refusals instead of one, because they say different things: `<not-authorized/>`
means "this node does not stand open to you" and names the way in with
`<presence-subscription-required/>`; `<forbidden/>` for somebody excluded says "not
you", and there is no way. To send them onto a presence request that will change
nothing would be a piece of false information.

## Three mutations against code that decided nothing

They survived not because tests were missing, but because there were **two ways to the
same decision** at three places:

- The recognition of the owner in `PepAffiliationOf` was used nowhere — the publishing
  compared JIDs instead. Now it asks after the role, and the rule stands once instead
  of twice: **write may whoever owns or whoever the owner has allowed it.**
- The exclusion was checked in `MayAccessPepNode` <i>and</i> in the choice of the
  error. The second check decides, so the first is gone.
- And the specially written check "somebody publishing creates no nodes" was
  unreachable: **at a node that does not exist nobody has a role**, the refusal comes
  from the check of the role already. The test for it now checks the rule behind it —
  a role belongs to a node and not to an account.

Sixty-four tests, fifteen mutations, all struck down. Full run: 1053 passed, 7 skipped.

---

### D82. A list does not arise incidentally ✅ — `whitelist`

The third access model, and the only reason this stage exists: **`member` decided
nothing up to here.** The role was givable and without consequence — noted expressly
that way in D81 so that it can be given out before the model needs it. Now it needs
it.

The difference to `presence` is the point: **presence authorisation arises
incidentally.** Somebody takes a contact on, and already they see more. A list does not
arise incidentally — on it stands only whoever the owner has expressly put on it. The
test holds that fast by Carol being a contact and staying outside all the same.

Two decisions that could have turned out differently:

- **A `publisher` stands on the list as well.** Anything else would be a role one can
  use only together with a second, and the owner would have to think at every one
  publishing of additionally making them a member.
- **The exclusion stands above the model.** Somebody excluded whom another puts on the
  list by mistake stays outside — otherwise the exclusion would depend on the order in
  which two instructions came.

Incidentally cleared up: the access model was read and written at **four places** —
node form there, node form back, conditions of a publication, check at the server.
Four places that keep the same list keep it differently at some point, and the one
that does not know a model lets it through silently as `open`. Now there is one.

A test from D76 had to be rewritten: it used `whitelist` as an example of a model not
offered. It now checks `authorize` — the procedure of approval behind it is still
missing, and that is why it is refused.

Sixty-eight tests, seven mutations, all struck down. Full run: 1057 passed, 7 skipped.

---

### D83. The same place for the third time ✅ — roles on the client side

Give out, look up, let take effect — the client side of D81/D82. Three questions that
belong told apart: **what have I given out** (section 8.9.1), **what am I elsewhere**
(5.7), and may I do what the role promises.

Both lists look the same and are read by one place; they differ in the namespace and
in whether the entry names a node or a JID. Two mutations checked exactly this
confusion.

**An entry with an unknown role lets the whole list fail**, instead of being missing
silently. A list from which single lines disappear is worse than none: whoever looks at
it holds somebody to be without rights who is not — and possibly takes from them the
role they believed they had as well.

And the surviving mutation was the same one for the third time: **a `type='error'`
stays a refusal even when a complete list stands in it.** Without the check on the type
the refusal would hang on there happening to be no list in an error answer. Here the
confusion would be especially unpleasant — the client would show a list of roles it
may not see, and the owner would learn from it that their node stands more open than it
does.

At the writing of the tests a trap of my own avoided: `Assert.Multiple` takes an
`Action`. An `async` lambda in it would run on as `async void`, and the assurances
would possibly fall after the block — that is, nowhere. First await, then check.

Forty-five tests, seven mutations, all struck down. Full run: 1061 passed, 7 skipped.

With that the roles are finished (D81–D83) and of XEP-0060 there remains: the owner's
view of the **subscribers** of a node (section 8.8), the deleting and emptying of
nodes, `<retract/>`, as well as the access models `authorize` and `roster` — for which
a procedure of approval and roster groups as a rule of access would be needed.

---

### D84. Who hangs on my node ✅ — the owner's view of the subscribers

In D79 it stood about the collective query: "whoever were allowed to enumerate
foreign ones would learn who is interested in what — a piece of information about
humans, not about nodes." Now the server does exactly that, and it is no
climb-down but a different question. **Section 5.6 asks "where does this human hang
everywhere", section 8.8 asks "who hangs on my node".** The first is a profile of
interests and goes over all nodes of a service; the second is a piece of information
about one node — and whoever does not get it is the one from whom all the recipients
have their data. To withhold the list of recipients from them would mean making them
responsible for a distribution they may not see.

**The id is no decoration here.** Since D72 the same JID can be subscribed several
times; without a `subid` it would stand there twice alike, and the owner could
distinguish none of their subscriptions from the other — so could end none of them
singly either.

Three decisions:

- **The owner may take away, not give out.** Section 8.8.2 lets them sign somebody
  up as well; this server does not. To enter somebody who has not asked is exactly
  what section 6.1.3.1 prevents on the other side, and that it is one's own node
  changes nothing for the one whose mailbox fills up. Without a procedure of approval
  there would be nothing about it that had been a question beforehand either.
- **Without an id all of them go** — no contradiction to section 6.2.3.1. There the
  *subscriber* has to say which one they mean, because the others are to stay theirs.
  Here the *owner* means the human and not the bookkeeping: to leave one standing
  would mean carrying the instruction out by half, and the one removed would go on
  getting everything.
- **What nobody finds is not ended but refused.** To let a `none` for somebody who
  has not subscribed at all hold silently would again be the report about something
  nobody has looked up — a typing error in the JID, and the owner would hold somebody
  to be removed who goes on getting everything.

A `subscribed` for an *existing* subscription holds all the same: it is no
instruction but a confirmation. **A list that cannot be sent back unchanged would be
no state, but a form.**

And the lesson from D83 drawn beforehand this time instead of afterwards: the block
for the owner checked ownership and node at **every** instruction singly — with the
subscribers it would have become the third copy of the same decision. Now the
preamble stands once in front of it, and whoever loosens it loosens it visibly for
all or not at all.

**What is still missing here:** the one removed learns nothing of it. They wait for
reports that come no more — and that is exactly the state
`PubSubSubscriptionState` has described as the worse one since D71. Section 8.8.4
provides a message for it; it is D85.

Eighty-one tests, fourteen mutations, all struck down. Full run: 1074 passed, 7
skipped.

---

### D85. A report about what has happened ✅ — the removal

The hole from D84 closed: whoever was removed waited for reports that come no more.
**That is the worse of the two mistakes** — so it has stood in
`PubSubSubscriptionState` since D71: whoever wrongly holds themselves to be not
subscribed asks once more; whoever wrongly holds themselves to be subscribed waits
for something that never comes.

**One report per extinguished subscription, not per instruction.** A `none` without
an id ends all subscriptions of a JID; came only one report on that, the recipient
would know of one id that it is extinguished and of the other nothing. This is why
the server does not report what was written down for it, but what it has actually
removed — a refused instruction unsubscribes nothing.

**The exclusion reports itself as well**, for it ends subscriptions (section 8.9.4).
It does not name its own cause in doing so: what the one excluded *is* at this node
is none of their business — that they no longer get it, is. Two different pieces of
information, and the server owes them only the second.

For that `SetPepAffiliation` had to be able to say what the exclusion had cost. The
information belongs where the removing happens: to gather it beforehand oneself would
mean answering the same question twice — and the second answer would be the less
exact one, because something can come in between the looking up and the setting. Both
ways to the ending now lead through the same method; two places that end
subscriptions end them differently at some point.

**A `headline` and thereby nothing for the store** (XEP-0160). Whoever is offline
does not learn it — just as they do not get the publications they miss. The
information stays reachable all the same, and that is the reason D79/D80 came before:
section 5.6 tells them at the next connecting what they still have. **A kept report
would be the poorer information**, for it describes a state of back then.

Eighty-nine tests, eight mutations, all struck down. Full run: 1082 passed, 7
skipped.

---

### D86. Two enumerations confusingly alike ✅ — the client side

The client side of D84/D85. `<subscriptions/>` means both: "where do I hang
everywhere" (section 5.6) and "who hangs on my node" (8.8.1). The same element name,
the same build, and the entry names once a node and once a JID — **to be
distinguished they are by the namespace alone.** Three mutations checked exactly this
confusion; it is the same trap as at the roles in D83, only with an element name one
more easily holds to be the same.

**The state is read strictly here, and in our own promise not.** That looks like an
inconsistency and is the point: there an unknown name as "not subscribed" is the
careful assumption — whoever wrongly holds themselves to be not subscribed asks once
more. Here the same leniency would be the opposite of careful: the owner would hold a
subscriber the service carries to be absent, and would possibly remove another one in
their place. An unreadable entry therefore lets the whole list fail.

**The client can remove and not sign up**, although section 8.8.2 allows both — the
same decision as in the server, and for the same reason. A client that can sign
another up unasked needs no name in `PubSubBuilder` for it: whoever wants that writes
it down and says what they are doing.

To that the counter-check at the entrance: a `<subscription/>` report with
`subscription='subscribed'` is **not** entered. A promise comes on a request; whoever
accepted it unasked could be signed up by a service. With that both sides refuse the
same thing.

The node of a removal had to be taken into `NodeOf`, and not only so that it arrives:
**on this node the check of the sender hangs.** A report whose node stays empty there
counts as a report about the node `""` — which nobody has subscribed to. The mutation
that takes the entry out again is therefore struck down not by the evaluating but by
the protection against spoofing.

Fifty-two tests, ten mutations, all struck down. Full run: 1091 passed, 7 skipped.

With that section 8.8 is finished (D84–D86) and of XEP-0060 there remains: the
deleting and emptying of nodes, `<retract/>` as well as the access models `authorize`
and `roster` — for which a procedure of approval and roster groups as a rule of access
would be needed.

---

### D87. The node and its content ✅ — deleting and emptying

Two instructions one easily holds to be gradations of the same, and which concern
different things: **deleted is the node, emptied only its content.** Whoever has
emptied goes on publishing to the same recipients; whoever has deleted, to nobody.

The test server could do neither of the two up to here — `/pubsub delete` had always
existed in the console, and the server answered it the way it answers everything
unknown. The missing part was therefore not the client but the far side.

**A deleted node takes four things with it**, and the fourth is the reason for
writing it down: items, settings, subscriptions **and roles**. Would the roles stay
standing, the next node of the same name would inherit a list of exclusions nobody
sees any more — and the owner would wonder why an acquaintance cannot get at their
new node.

## The surviving mutation was none at all

At the emptying there stood at first `items.Clear()` instead of
`_pepNodes.Remove(node)`, and with a reason that sounded good: a node that arose
merely through the publishing would stand alone in the store — is it removed, then
the emptying would have deleted it. The mutation that does exactly that has
**survived**, twice, even after the test closed the gap through which it had fallen
the first time.

The reason: **the case does not exist.** `PublishPepItem` creates the setting before
it writes the first item, exactly like `CreatePepNode` — there is no node that stands
only in the store. The defence was directed against a state nothing can produce, and
was therefore not to be refuted.

Behind it lay the actual find: **the question "does this node exist" had two answers**
— a setting present *or* items present. The second was unreachable and would have
become a trap at the emptying. Now a node hangs on its setting, at one place and only
there; the same simplification cleared a second enumeration in `PepAffiliationsOf`
away with it. That is the find from D81 in a new shape: not a missing test, but **two
ways to the same decision.**

The test the first mutation uncovered stays standing all the same — it looked only
after the next publication, and that creates the node again. **A deleted one would
have looked afterwards like an emptied one.**

**One report per subscriber, not per subscription** — and without an id. That is the
counter-decision to D85, for the same reason: there single subscriptions ended, and
the id said which one. Here the node ends; to name an id would mean the others still
exist. For the same reason no second report under section 8.8.4 follows afterwards.

Two refusals that could have turned out differently:

- **A node without a store cannot be emptied** (section 8.5.3.2). For the opposite
  one could argue — the report is directed at the subscriber after all, and that one
  has possibly kept something. The XEP decides otherwise, and with the better reason:
  a `result` would be the information that something had been emptied, and the report
  the request to throw away something this node never delivered.
- **A `get` on `<delete/>` is a `<bad-request/>`** and no deleting. Without this
  check it would fall through as far as the configuring and would get the
  configuration of the node back — an answer to a question nobody has put.

Not implemented: the `<redirect/>` from section 8.4.2, with which a deleted node
points at its successor. It would be a reference the client would have to follow, and
without the second node a promise without backing.

A hundred tests, twelve mutations, all struck down. Full run: 1102 passed, 7 skipped.

---

### D88. What only the one deleting does not learn ✅ — the client side

The client side of D87, and it consists almost entirely of what is to be done
**after** the answer.

**A deleted node takes the subscription to it along, an emptied one does not.** That
is the same difference as in the server, only seen from the other side: after a
`<purge/>` the next publication comes to the same address, and whoever cleared up here
as well would afterwards have no entry any more about a subscription that still
exists — and would have to hold its reports to be forgeries.

**The one deleting gets no report.** The service sends the `<delete/>` to everybody
except the one who deleted — right so, but it means that exactly that one has to strike
their entry themselves. Whoever relied on the report would as the only one keep a
bookkeeping about a node they removed themselves. A refused deletion on the other hand
clears nothing up; that too is worth a mutation of its own.

**Struck is per service and not per name.** `urn:xmpp:omemo:2:bundles` is called that
at every account — whoever at the deleting takes merely the name of the node out of
the bookkeeping ends at the same time the subscription to the node of the same name of
somebody else and notices it only when their reports stay out. The test for it holds
two subscriptions to the same name at two accounts.

Incidentally: `PubSubBuilder.DeleteNode` wrote its namespace out as a string while all
other owner requests use the constant. Two spellings of the same thing hold until one
of the two becomes wrong.

Fifty-seven tests, seven mutations, all struck down. Full run: 1107 passed, 7 skipped.

With that of XEP-0060 there still remains `<retract/>` as well as the access models
`authorize` and `roster`.

---

### D89. A delivery and no report about the node ✅ — `<retract/>`

The opposite of D87 in one sentence: **deleting and emptying concern the node, a
retraction concerns an item.** On that everything further hangs. It therefore does not
go out once per subscriber, but **per subscription, with an id, and to a silenced one
not at all** — exactly like a publication, for it is a delivery.

That could be proved instead of claimed: the delivery of a publication and of a
retraction now runs through the same place, which only gets the content of `<items/>`
handed to it. For the silenced subscription there was afterwards nothing more to
consider — the test for it checks that it stays that way as well.

**Whoever may write may also retract.** The same check of the role as at the
publishing, and with that a `publisher` gets at foreign items in the same node as well.
The finer rule — everybody only their own — would be the better one, but would
presuppose remembering who wrote which item. That store does not exist here, and
without it the rule would be merely claimed.

Two refusals, both for the same reason as in D87: an item that does not exist gets
`<item-not-found/>`; a node without a store `<unsupported
feature='persistent-items'/>`. A `result` would in each case be the information that
something had been retracted — and the report to the subscribers the request to throw
away something they never got.

One test was wrong at first, and the answer of the server was the better one: for a
**foreign** node it expected `<forbidden/>` with the reason from D81 — at a node that
does not exist nobody has a role. For the owner that does not hold: **they are
recognised and not looked up**, because a PEP node belongs to the account. They
therefore lack not the permission but the item, and exactly that `<item-not-found/>`
says.

The last retracted item leaves the node standing. A node that disappeared with its
content would be gone without an announcement for its subscribers — and the next
publication would create a new one nobody has subscribed to.

**What the drawing together uncovered along the way:** the mutation that sends a
publication out without its `<item/>` envelope has survived. This suite checked the
content of a delivery, the origin and the id of the subscription — **never however the
id of the delivered item.** That is no formality: a client that keeps items by their
id passes over an item without one entirely. The content would arrive and would be
lost all the same.

A hundred and seven tests, nine mutations, all struck down. Full run: 1114 passed, 7
skipped.

---

### D90. The part that was there already ✅ — `<retract/>` on the client side

The shortest stage of this series, and for a reason that belongs to it: **the client
could read incoming retractions from the beginning.** `PubSubEvent` knows `Retract`
together with the list of ids concerned, since `PubSubManager.ProcessEvent` exists —
only none ever arrived, because no server in reach sent one. Only D89 delivered the
far side, and since then the branch has run for the first time. The same story as at
the deleting in D88, only without the clearing-up part.

For there is nothing to clear up here, and that is the only decision of this stage: **a
retraction concerns an item and not the node.** The subscription stays standing —
unlike at the deleting, where it goes along. To strike it here as well would be a loss
without an occasion: the node still exists, and the next publication would come to an
address this client no longer knows. The test for it publishes once more after the
retraction and checks that it arrives under the same id.

What arrives is solely the id of the item — a retraction has no payload. Whoever does
not read it knows that something has changed, but not what, and has to fetch the whole
node anew.

Sixty tests, six mutations, all struck down. Full run: 1117 passed, 7 skipped.

With that XEP-0060 is finished except for the access models `authorize` and `roster` —
for which a procedure of approval and roster groups as a rule of access would be
needed.

---

### D91. The group that never made it to the server ✅ — roster groups

On the way to the access model `roster` it turned out that the precondition is
missing: **the test server knew no roster groups.** And not only that — it acted as
though it knew them:

- `RosterStanzaBuilder.SetItem` sends `<group/>` along, since it has existed.
- `RosterItem.Groups` keeps them at the client, `/roster` shows sorted by them.
- The comment in the roster handling of the server has always said that a set changes
  "name **and groups**".
- Read the `<item/>` was only as far as its attributes.

The group arrived, was discarded silently, and the push brought the same entry back
without it. **Because a push replaces the groups of an entry, it thereby disappeared
at the client as well** — what the human had set was gone the blink of an eye later,
and nothing looked like an error.

**Two places at which the same would have happened once more** came out at the drawing
along:

- The **handshake** (`UpdateRosterEntry`) built the entry anew field by field. The
  freshly set group fell out in doing so, because `AddContactAsync` sends a presence
  request straight after the set — the test was red although the reading had long been
  right. Now the existing entry is changed with `with`; that knows the fields that are
  still to come as well.
- The **store** (`FileAccountStore`) wrote the roster field by field likewise. Without
  the addition the groups would not have survived any restart of the server.

**The version of the roster counts them along** (RFC 6121, section 2.6). That is the
part at which nothing else would come out: would the version stay the same after a
regrouping, a client that has cached it would get an empty result at the next logging
in — and would keep the old division for ever. The error would show itself only days
later and at a different device.

To that an `XmlEscaping.Unescape` for the places that read a stanza with a pattern
instead of taking it apart. **The ampersand last:** whoever replaces it first makes a
`<` out of `&amp;lt;` — out of a text that is about a character becomes the character.
The test for it carries a group named `A&lt;B`, which means exactly that literally.

Six tests, six mutations, all struck down. Full run: 1123 passed, 7 skipped.

---

### D92. The list the owner keeps anyway ✅ — access model `roster`

The fourth of five models, and after D91 almost a formality: whoever stands in the
roster of the owner gets in; are groups named, only whoever stands in one of them.

**One entry suffices, a state of presence is not demanded** — that is the difference to
`presence`, and it is no inexactness but a different question: there it is about who
*may see me*, here about whom *I keep*. The two can go apart, and then they are two
answers and not one approximate one.

**Without named groups the whole roster gets in.** To read the empty list as "nobody"
would be the other possibility and the poorer one: it would make `roster` in its basic
setting equal in effect to an empty `whitelist` — two names for the same thing, and one
of them would mislead.

The list of groups stands in the form even when another model holds. It is a setting of
the **node** and not of the model: whoever changes from `open` to `roster` is to be
able to set the list beforehand, instead of leaving the node standing open for the
whole roster between two instructions.

`pubsub#roster_groups_allowed` is the first field of this house that carries **several
values**. The helper for forms said expressly up to here that multiple values were not
needed — now they exist, and a `list-multi` of which only the first value were read
would be exactly the silent shortening this house otherwise writes against.

Incidentally a find of the same kind as in D91: **the console command `/pubsub access`
did not know `whitelist`** — it has always taken only `open` and `presence`, while the
help text beside it and the README promised all three since D82. It now reads the names
out of the same place as the form.

Five tests, seven mutations, all struck down. Full run: 1128 passed, 7 skipped.

---

### D93. The model at which asking and being allowed are two things ✅ — `authorize`

The fifth and last access model. **At all the others the same rule decides two
things:** whoever may not get in may not subscribe either. Here not — everybody may
ask, for the asking is the procedure. Whoever threw both together would make the
procedure of approval unreachable: to be allowed one would already have to be allowed.

With that `PubSubSubscriptionState.Pending` gets a sense for the first time. The state
has stood in the code since D71, with the reason that a `pending` looks like a promise
and is none — **on paper**, for no node could create one. Now one can, and at three
places in the server there stood `subscription='subscribed'` as a fixed string. Each of
them was from that moment on a claim.

The promise goes through the door D84 built: the list of subscribers. There it stood
expressly that the state is entered fixedly and that this would be "one of the places
that need a real state" as soon as `authorize` exists — and likewise that a
`subscribed` is "no instruction but a confirmation". Both now hold the other way round,
and the reason was noted back then already: *without a procedure of approval there
would be nothing that had been a question beforehand.* Now there is something. **A
`subscribed` on an applied-for subscription is the promise, on a promised one it stays
the confirmation from before** — and that does not report itself, because nothing has
changed.

## What `authorize` uncovered along the way

**The incidental delivery did not ask the access model.** Presence contacts got every
publication — from a node too whose model barred them from the fetching. The model held
the door shut and let the report through, in which the item stands complete. For
`whitelist` and `roster` that was wrong since D82 and D92 and nobody noticed, because
both models were checked only at the fetching and at the subscribing. At `authorize`
the approval would thereby have been a mere formality: whoever waits would long since
have got everything.

Now this way asks the same place too — one line, and it clears three models up at once.

And a test has lost its example **for the second time**, both times for the best
reason: "an access model nobody offers is refused" was called `whitelist` until K13 and
`authorize` until D93. Both are now offered, because they can be enforced. What remains
is the case there will always be: a name nobody has given out.

A hundred and seventeen tests, ten mutations, all struck down. Full run: 1133 passed, 7
skipped.

**What is still missing:** the request for approval under section 8.6.1 — the message
with the form over which a foreign client shows the application and answers it. As long
as it does not exist, the owner learns of the application only when they look. That is
the next stage, and it does not hang in the air: without it nothing would be wrong even
today, only inconvenient.

---

### D94. Two doors, one room ✅ — the request for approval

The application is now put before the owner instead of waiting for them (section
8.6.1) — and the answer to it arrives (8.6.2).

**Two doors to the same decision, and therefore no second decision.** Approve an
application could be done over the list of subscribers since D93; now it goes over the
form as well, and both ways call the same place in the account. Two doors are necessary
all the same: **the list is the view of an administrator, the form the one of a human
whose client shows them a question.** Whoever had only the list would demand of every
client that it can administer subscribers.

From that follows the coupling that makes this stage one at all: **a form nobody can
answer would be worse than none.** It does not suffice to put the question — whoever
puts it has to accept the answer, otherwise a human approves something and nothing
happens. This is why the reading and the writing of the form stand beside each other in
one file.

Three decisions in the small:

- **`pubsub#allow` stands at "no".** A form that already stands at yes makes a promise
  out of the clicking away.
- **A "no" to a question from earlier ends no promised subscription.** Otherwise the
  order of two messages would decide what holds — a form arriving late would take from
  somebody something they have long had.
- **What is not understood here is not swallowed.** A form about a foreign node or one
  that cannot be read goes its ordinary way on as a message. To let a message disappear
  without a trace is the dearest way of being polite.

  The test for it did not check that at first, and the mutation that removes the check
  of the node has survived: it sent the foreign form to the account of the sender, where
  it could have no effect anyway. **Both versions did the same thing — namely nothing.**
  Now it goes to a third party, and the difference is to be seen: without the check it
  never arrives at them.

The request itself is a `headline` and is not kept. **It is a convenience and no
carrier of the state:** the application stands in the subscription, the message says
only that it exists. Whoever was offline misses the message and not the application —
and a kept one would be the poorer information, because it would describe a state of
back then that can long since have been decided.

A hundred and twenty-two tests, seven mutations, all struck down. Full run: 1138
passed, 7 skipped.

With that XEP-0060 is finished in the extent this project needs: all five access
models, roles, subscriptions together with ids and settings, administration of nodes,
retraction and approval.

---

### D95. Two questions, one property ✅ — `authorize` on the client side

The client side of D93/D94, and its core is one line that had looked right since
D71: **a `pending` was discarded.** The caller got `null` — the same answer as to a
refusal.

That was the right answer to "am I subscribed" and the wrong one to **"what have I
applied for"**. Two questions hung on one property. And the second is not
incidental: **the id of the application comes from the service.** Without it the
client cannot assign the promise that arrives later as a report to any question of
its own — between them lies a human who answers it, and this is why it does not come
as an answer to the IQ.

Entered the `pending` therefore now is, but as what it is: `IsSubscribed` counts what
was promised and not what was entered. The confusion D71 warned about stays ruled out
— only at a different place.

**The rule from D86 still holds, and it becomes more exact.** There it read: a
promise comes on a request, whoever accepts it unasked can be signed up by a service.
Right — only there is now a case in which it was demanded, and this client recognises
that by its **open application**: promises without such a one are still refused.

On the other side the client puts the application before the owner and answers it —
**shown and not answered**: whoever promises is a human. A client that answered of its
own accord would decide about foreign access after a rule nobody has seen.

One mutation survived and pointed at the test again: "promised is also what is
promised already" got through because the unrequested promise in the test carried a
**foreign id** — refused it was by that and not by the rule. The test now sends both:
the invented id and the right one. **Promised is promised** — a second promise is no
change and does not report itself.

Sixty-three tests, seven mutations, all struck down. Full run: 1141 passed, 7
skipped.

---

### D96. Three lists of the same commands ✅ — the console in the README

Drawn along, and compared in both directions at that: **no command in the code the
README does not name; none in the README that does not exist.** The PubSub
subcommands, the top-level commands and `/omemo` are counted through once each.

They exist namely **three times**: in `PrintHelp`, in the help of `/pubsub` and in the
README. Three lists of the same thing hold until one of them becomes wrong — and
exactly that had happened:

- **`/fix` was missing from the README entirely.** The command has existed since D60,
  the table of features names it ("in the console `/fix <text>`"), the help of the
  console too — only the list of commands did not, that is, precisely the place
  somebody looks who wants to know what they can type.
- **`/pubsub access` promised three models, `create` knew two.** The first was fixed
  since D92; at the second the same shortening still stood in the text.
  <b>Whoever wrote `whitelist` got an open node and a message of success</b> — the
  quietest way of losing a setting. Now `create` too reads the names out of the place
  the form reads them from.
- Three aliases (`rostergroups`, `authorize`, `fp`) were noted nowhere.

And the short help now says that it is one: the five PubSub lines in `/help` looked
like the whole set; they are five out of twenty.

**Why that could run apart at all:** the console has no tests. It is the only corner
of this project in which a claim without backing comes out to nobody — no mutant can
strike anything down here, because nothing is looking. The comparison therefore ran as
a throwaway script over both files; to build it in as a test would mean writing the
path of two text files into the test suite, and the move to `HermodTests` is still
outstanding.

Full run: 1141 passed, 7 skipped.

### D97. The protocol moves out ✅ — Ratatoskr

The move itself came from outside: client, server, XEPs and the test suite now lie in
**Ratatoskr**, a repository of its own under `libs/`, with the namespace
`org.GraphDefined.Vanaheimr.Ratatoskr`. Here stay the console, its tests and the two
foreign far sides in `tools/`.

This entry is about what such a move drags along behind it. **Four things were broken
afterwards, and three of them would not have reported themselves.**

**The compiler reported two lines, meant were four.** `IPPort`, `IPv4Address` and
`IPSocket` come from Hermod, and nobody had ever written a `using` for it — the
namespace lay *beneath* Hermod, the types came in over the nesting. Two files in the
library, two in the federation tests. That is the friendly sort of error: it stands in
the build log.

**With the same nesting a reason has expired.** At the alias
`using IPAddress = System.Net.IPAddress;` it stood that it has to stand in the body of
the namespace declaration, because a namespace member wins against an alias of the
compilation unit. That held as long as the namespace lay under Hermod. Now Hermod's
`IPAddress` comes only over a `using` directive, and against that the alias wins — it
therefore stands at the top with the others again. The comment now says both: why the
alias is needed, and why it no longer has to be in the body.

**Three tests have skipped themselves silently since then.** The OMEMO oracle was
looked for by walking up from the output until `WORKPLAN.md` lay there — and from
there under `Jabber.Tests/XMPP/XEPs/Orakel/`. Both marks belong to the program and not
to the library, and both were wrong after the move. The message for it read **"The
oracle is not reachable (python-omemo in WSL …)"** — it sounds like a missing
reference implementation and not like a wrong path. That is exactly the difference
between **7 and 10 skipped**, that is, between "the far side stood ready" and "the far
side was never asked".

Looked for is now the script itself, and **if it is missing the run is red and not
skipped**: the oracle lies in the same project as the tests, so a missing one is a
broken checkout. Skipped is only what really is down to the environment.

**Three generator scripts wrote into the void.** `tools/unicode/` and
`tools/stringprep/` fetch the Unicode file and RFC 3454 respectively and write
`Common/BidiClasses.cs`, `Common/ContextTables.cs` and `Auth/StringPrepTables.cs` out
of them. Their target stood as `parents[2] / "Jabber" / …` in the source. At the next
change of Unicode they would have laid a fresh `Jabber/Common/BidiClasses.cs` beside
the console, reported "done", and the table that is actually compiled would have
stayed the old one. They have moved to `libs/Ratatoskr/tools/` with what they produce.

**And two dependencies stood in the wrong place — both worked all the same.**
BouncyCastle stood in `Jabber.csproj`, where since the move no OMEMO lies any more; in
`Ratatoskr.csproj` stood neither it nor `Microsoft.Extensions.Logging`. It compiled
nevertheless, because Hermod brings both along. Exactly against that the comment
warned that stood above the package: *whoever uses a transitive dependency directly
loses it at the moment at which the previous owner puts it down.* The comment has
wandered along, the package too; the same reason now stands at the express
`ProjectReference` of the federation tests on Hermod.

**A stopgap has settled itself.** In `Jabber.csproj` there stood two
`InternalsVisibleTo` names — the second "for the case that the tests wander to
`HermodTests` later". They have wandered, only somewhere else. Now there stands one,
in `Ratatoskr.csproj`, and it names the assembly that exists.

**The README is divided, not moved.** The big one stays here: it describes both
together, because both arose together and the decisions behind them stand in this work
plan. Ratatoskr gets the extract out of it for whoever uses the library without this
console — XEPs, RFC conformance, server, test vectors, OMEMO. **What holds for the
checks against foreign far sides stays here**, for here lie the setups. Drawn along
are moreover the paths in both `setup.sh` that still pointed at `Jabber.Tests`.

**No mutations for this step.** There is no new production code — except for the one
line that decides where the oracle is looked for, and that is shown by three tests
running again instead of skipping themselves.

Full run: 1133 passed, 7 skipped; to that 8 for the console.

---

## Later

### Test suite
- ~~**`AFailureWhileHandlingAFrame_IsReported` has been flaky under load since
  D68.**~~ Fixed in D69, and the reason was no problem of timing but a race: after the
  building of the connection something is still on its way — the first presence, the
  answer to the fetching of the roster. Did the test switch fall while something of
  that was still arriving at the server, then *that* frame failed first, the server
  ended the stream, and the message with the id sought never went out. The test then
  waited ten seconds for a report that could no longer exist.
  **The race was always there; it became visible only when the OMEMO tests kept the
  machine busy enough** — two of four full runs fell over it. Now the test waits until
  nothing more comes from the client, instead of until `ConnectAsync` returns. **A
  test that falls half the time measures nothing any more** — and the first supposition
  ("too tightly measured") was wrong: no waiting helped, because the report did not
  come late but not at all.
- ~~**`NonzasDoNotAdvanceTheCount` against Prosody fails occasionally** — come out in
  D34, one failure in one full run. The recording is there:

  ```
  Wir haben Nonzas mitgezählt.  Expected: 6  But was: 8
  Prosody hat andere Nonzas mitgezählt als wir.  Expected: 8  But was: 6
  ```

  The client had therefore counted **two** outgoing stanzas more than the three the
  test sends; Prosody acknowledged the expected six. Both assurances fall together,
  because both compare the same number.

  An obvious explanation is already **refuted**: the test sends to itself, so the
  messages come back — but the automatic answers of the client (XEP-0184, XEP-0333)
  demand a `<request/>` or `<markable/>` respectively in the frame, and the test
  messages carry only a `<body>`. They set nothing off.

  Open is thereby **which two stanzas** were counted along. Since D35 the test records
  the outgoing and attaches it to the message — at the next incident what went out
  stands there instead of a number. Twenty targeted executions could not repeat it
  (see D34, D35)~~
  ✅ done in D55 — and the question about the two stanzas was the wrong one: Prosody
  had counted rightly and we did too. The test compared a number where section 2 means
  a relation
- ~~`TheStreamSurvivesABrokenConnection` (D16) has been **no longer reproducible**
  since D33 and the suspicion of back then refuted: forty executions between 519 and
  669 ms at a deadline of 15 seconds. Whether D30 removed it is a fitting explanation
  and no proof. Does it occur again, the message now names the history — then it can be
  cleared up in one attempt (see D33)~~
  ✅ done in D56 — the suspicion was **not** refuted, the measurement could not refute
  it at all: all forty passes got through at the first attempt, and the deadline of 15
  seconds lay only 5.7 seconds above the 9.3 the client may spend on waiting alone

### Server (`libs/Ratatoskr/Ratatoskr/Server/`)
The big lumps stand above under [S1 to S4](#the-server-is-to-become-a-real-server).
What did not appear there and was due all the same is worked through in D49 to D53:
answer the `<resume/>` (had been done since R1, what stayed open was the `h` in the
`<failed/>` — D49), offer SCRAM (had been done since S2, what stayed open was the
unknown account — D50) and stanza errors without a switch (D51 to D53).
**Nothing stands open here at present.**

### Structure
- ~~Move `Jabber.Tests/XMPP/` to `HermodTests/XMPP/`. Deliberately postponed;
  namespaces, the cut of the folders and the double `InternalsVisibleTo` entry in
  `Jabber.csproj` are already laid out for that becoming a copy.~~ ✅ done in D97 —
  only differently than planned: not the test suite wandered to Hermod, but the whole
  protocol into a library of its own (**Ratatoskr**), and the tests with it. The
  preparatory work carried all the same: the cut of the folders and the namespace could
  be taken over unchanged. The double `InternalsVisibleTo` has thereby become one.
- ~~Separate console UI and logger: the standard console logger writes into the same
  console as the input line and takes the prompt apart. An `ILoggerProvider` of its own
  over the synchronised output would be the clean solution.~~ ✅ done in D58 — the
  synchronised output did not exist at all in doing so: the handling of the events
  bracketed every output by hand, without a lock
- ~~Decide about unused public members: use or strike. List in
  [Jabber/README.md](Jabber/README.md).~~ ✅ done in D57

---

## Optional

What stands here is not wrong and not urgent: nobody misses it as long as nobody uses
it. A point wanders from here to "Later" as soon as there is a use case at which the
implementation can be checked.

- ~~**XEP-0060 — Publish-Subscribe.**~~ Done in D70 and D71. The reason why the point
  stood here was in the end the way to the implementation: there was no course of
  events at which the correlation could be checked, because the test server said
  `<service-unavailable/>` to every `subscribe`. So the server first (D70), then the
  client (D71) — and the actual find lay in between: a confirmed subscription brought
  nothing in at all, because the protection against spoofing discarded every PEP
  report.

  **What that says about the list:** "no use case" did not mean here that nobody needs
  it, but that no far side could answer it. That is a reason to wait, but a different
  one than the one that stood here

- **TCP transport for the client.** This client speaks XMPP over WebSocket (RFC 7395),
  and the servers it runs against offer it — Prosody, ejabberd and our own test server.
  As long as that stays so, nobody misses the TCP transport.

  The extent has been measured since D34: the client touches the WebSocket directly at
  nine places (connecting, sending, the two receiving paths, breaking off), so it would
  need an abstraction of the transport, to that STARTTLS on the client side and the TCP
  framing. `XmlStreamSplitter` and the STARTTLS negotiation exist on the S2S side
  already, but are shaped for `jabber:server` there. `CreateTcp` — the factory method
  that created a `tcp://` URI and was without function in doing so — was removed in
  D34; a public method that cannot work is worse than none.

  **The way back:** a server this client is to reach that offers no WebSocket endpoint.
  Then the use case is there, and with it the counter-check — Prosody listens on
  127.0.0.1:5222 and would be the touchstone
  (see D34, D48)

---

## Deliberately not implemented

What stands here is decided and does not wait for an opportunity.

- **XEP-0013 — Flexible Offline Message Retrieval.** Carried by the XSF as
  *Deprecated* (version 1.3, 2021-05-04): "Implementation of the protocol described
  herein is not recommended." The offline store stays with the automatic handing in
  later under RFC 6121, section 8.5.2.2.1, and XEP-0160. A successor the document does
  not name; the targeted reading up would lie at XEP-0313 (MAM), which however
  describes an archive and no store (see D37)

---

## Way of working

What has proved itself in this project and should be kept:

- **Secure fixes by mutation.** Green alone proves nothing — turn the fix back and
  check that exactly the responsible tests go red. That is how all corrections so far
  are shown.
- **Compute against published vectors, not against oneself.** SCRAM and the caps hash
  are checked against RFC 5802/7677 and XEP-0115; two defects came to light through
  that in the first place.
- **Implement the test server independently.** `XMPPServer` counts XEP-0198
  deliberately with logic of its own. Used both sides the same helper function, a
  shared error of thought would stay invisible.
