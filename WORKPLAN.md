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

### S4b. Der eigentliche S2S-Transport

**Entschieden: beides.** Nicht das eine *oder* das andere — TCP für die
Föderation mit vorhandenen Servern, WebSocket für Strecken zwischen zwei
Instanzen dieses Servers.

| | TCP 5269 | WebSocket |
|---|---|---|
| Rahmen | ein offenes `<stream:stream>`, Stanzas als Kindelemente | ein Frame = eine Stanza |
| TLS | STARTTLS **im** Stream (RFC 6120 §5.4) | TLS unter dem Handshake, davor |
| Auffinden | DNS SRV `_xmpp-server._tcp` (RFC 6120 §3.2) | kein Standard, Konfiguration von Hand |
| Gegenstellen | ejabberd, Prosody, alles | nur eine andere Instanz dieses Servers |

**S4b-1 ✅ Die Protokollschicht, ohne Transport darunter.** `S2SStream` (neu,
`Jabber/Server/S2SStream.cs`) kennt weder Socket noch WebSocket-Rahmen: sie
bekommt eingehende Rahmen als Zeichenketten gereicht und schickt ausgehende
über eine Funktion hinaus. Beide Rollen (`Initiate`/`Accept`) beherrschen den
`<open/>`-Handshake nach RFC 7395 §3.4, die vom Empfänger vergebene Stream-ID
(der Anker für Dialback), Stanza-Ein-/Ausgang, `<close/>` und Stream-Fehler.
Der Stream ist gerichtet (RFC 6120 §4.1) — ein ausgehender Stream nimmt keine
Stanzas an, das wäre XEP-0288 und ausgehandelt, nicht angenommen.

`ReceiveFromRemoteAsync` hat einen Zwilling bekommen, `AcceptFromRemoteAsync`,
der als `RemoteStanzaResult` sagt, *warum* abgelehnt wurde. Das war nötig, weil
die Ablehnungen jetzt unterschiedlich schwer sind: ein `from`, für das die
Gegenstelle nicht sprechen darf, beendet den Stream mit `<invalid-from/>`
(RFC 6120 §8.1.1.1); ein Empfänger auf einer dritten Domain kostet nur die eine
Stanza. `DirectServerLinks` kannte diesen Unterschied nicht — es konnte nur
verwerfen, nie den Stream beenden.

**S4b-2 ✅ WebSocket-Transport.** `WebSocketServerLinks` (neu,
`Jabber/Server/WebSocketServerLinks.cs`) ist `IServerLinks` über einen echten
Socket: eingehend ein eigener `AWebSocketServer`-Zweig auf einem zweiten Port
mit Subprotokoll `xmpp-server`, ausgehend `ClientWebSocket` mit
Verbindungs-Cache je Domain. `WebSocketServerLinks.Connect(a, b)` verkabelt wie
`DirectServerLinks.Connect`, nur mit echten Adressen und gepinntem Zertifikat
statt bloss einer Objektreferenz. `WebSocketFederationTests` fährt dasselbe
Zielbild wie `FederationTests`, diesmal über echte Sockets samt TLS.

Ein Stream-Fehler beendet jetzt auch die WebSocket-Verbindung, nicht nur den
XMPP-Stream (RFC 6120 §4.9 verlangt genau das) — sonst bliebe eine Verbindung
offen, auf der protokollseitig nichts mehr passiert. Ehrlich vermerkt: dieser
Teil und der symmetrische Ausstieg der Empfangsschleife greifen im aktuellen
Testaufbau ineinander (die eine Seite schliesst aktiv, die andere reagiert
schon auf den regulären WebSocket-Close), sodass ein Mutationstest, der nur
eine der beiden Stellen zurückdreht, nicht zuverlässig auf genau diese Zeile
zeigt. Beide bleiben, weil RFC 6120 §4.9 den Verbindungsabbau unabhängig vom
jeweils anderen Mechanismus verlangt — nur die Testschärfe dafür fehlt noch.

**Was jetzt WebSocket-S2S kann und was nicht:** verbinden, TLS, Stanza hin und
zurück, Absenderprüfung mit Konsequenz (Stream *und* Verbindung enden), und
seit S4b-3 eine belegte Gegenstellendomain. Was weiterhin fehlt: die Auflösung
über SRV statt über eine Konfigurationsliste, das Verhalten, wenn zwei Server
einander gleichzeitig anwählen (doppelte Verbindungen), und welcher Transport
gewählt wird, wenn eine Domain über beide erreichbar wäre. SASL-EXTERNAL gibt
es seit S4b-7, aber nur auf der TCP-Strecke — über WebSocket bleibt Dialback
der einzige Weg.

**S4b-3 ✅ Dialback (XEP-0220).** Die Domain der Gegenstelle wird jetzt belegt
statt geglaubt. `DialbackKey` rechnet den Schlüssel nach XEP-0220 §2.1.1
(Verfahren aus XEP-0185), geprüft gegen den veröffentlichten Vektor —
`SHA256(Secret)` geht dabei als **Hex-Zeichenkette** in den HMAC, nicht als
Rohbytes, und die Reihenfolge ist Ziel- vor Absenderdomain. Beide Lesarten
liefern sonst einen stimmigen, aber falschen Schlüssel.

`S2SStream` beherrscht alle drei Rollen: der aufbauende Server weist sich
unaufgefordert mit `<db:result/>` aus, der annehmende lässt den Schlüssel
prüfen und antwortet `valid`/`invalid`, der autoritative rechnet einen fremden
`<db:verify/>` nach. Vor bestandenem Dialback trägt der Stream keine Stanza —
das ist die Zeile, die aus dem Austausch überhaupt eine Sicherung macht
(XEP-0220 §1).

**Wo der Wert steckt:** `WebSocketServerLinks.VerifyDialbackKeyAsync` fragt
nicht den, der sich gerade ausweisen will, sondern die Adresse, die *dieser*
Server für die Absenderdomain hinterlegt hat — über eine eigene, kurzlebige
Verbindung. Wer sich fälschlich für `left.example` ausgibt, wird deshalb nie
selbst gefragt; gefragt wird der echte `left.example`, und der kennt den
Schlüssel nicht. An die Stelle der DNS-Auflösung des XEP tritt dabei die
Gegenstellenliste des Betreibers. Für den Zweck ist das eher strenger als DNS
(das unauthentifiziert ist), aber es füllt sich nicht selbst: eine Domain ohne
hinterlegte Adresse lässt sich nicht prüfen und wird deshalb abgelehnt.

Zwei Fehler kamen dabei ans Licht, beide älter als dieser Schritt:

- **Hermods `WebSocketServerConnection` vergleicht sich über `LocalSocket`** —
  und der ist bei einem Listener für jede angenommene Verbindung derselbe. Ein
  gewöhnliches `Dictionary` hält damit *alle* eingehenden Verbindungen für ein
  und dieselbe: die zweite bekam den Stream der ersten samt deren Sendefunktion
  auf einen längst geschlossenen Socket. `XMPPServer` geht dem seit jeher mit
  `ReferenceEquals` aus dem Weg; der S2S-Eingang tut es jetzt auch.
- **Der Verbindungs-Cache räumte nur auf, wenn der Aufbau bereits erfolgreich
  abgeschlossen war.** Starb der Stream noch im Aufbau — mit Dialback der
  Normalfall, weil der Aufbau mehrere Umläufe dauert —, blieb der Eintrag für
  immer stehen. Jetzt wird über die Identität des Platzes aufgeräumt.

**S4b-4 ✅ TCP als zweite Rahmung.** `TcpServerLinks` spricht
`jabber:server`-Streams über TCP (RFC 6120) — dieselbe Protokollschicht,
darunter `TcpStreamFraming` statt `WebSocketFraming` und `XmlStreamSplitter`
statt fertiger Frames. `TcpFederationTests` prüft dasselbe wie die
WebSocket-Fassung und läuft grün.

**Die Antwort auf die Frage aus S4b-1: nein, `S2SStream` blieb nicht
unverändert.** An sechs Stellen steckte RFC 7395 fest im Code — die beiden
`<open/>`-Sendungen, das `<close/>`, die zwei Erkennungen dazu, und, am
unauffälligsten, ein `XElement.Parse` auf dem Stream-Kopf. Über TCP ist der
Kopf ein *offenes* Tag; jede TCP-Verbindung wäre mit `<bad-format/>` gescheitert.
Die Abstraktion hatte die Form ihrer ersten Implementierung angenommen, genau
wie hier als Risiko notiert. Was gehalten hat, ist alles Übrige: Handshake,
Stream-ID, Dialback, Absenderprüfung, Fehlerbehandlung, Lebenszyklus — und das
ist jetzt belegt statt behauptet, weil `S2SStreamTests` dieselbe Klasse mit
beiden Rahmungen fährt, ohne Socket.

Nebenbei bestätigt: die Entscheidung aus S4b-3, Dialback-Elemente über einen
regulären Ausdruck statt über einen XML-Parser zu lesen, zahlt sich hier aus.
Ein `<db:result/>` über TCP ist für sich genommen nicht wohlgeformt — sein
Präfix hängt am Wurzelelement.

Ein Fund, den nur eine Messung liefert: der erste Zustellvorgang dauerte
4167 ms statt 82 ms. Nicht TLS, sondern `localhost` — der Name löst zuerst nach
IPv6 auf, der Listener bindet IPv4-Loopback, und jede der beiden Verbindungen
(Stanza-Stream und Dialback-Nachfrage) zahlte rund zwei Sekunden Fallback. Alles
funktionierte, nur langsam; kein Test wäre je rot geworden.

**Was an S4b-4 offen bleibt:**

- ~~Kein STARTTLS~~ ✅ **erledigt.** `TcpTlsMode` wählt zwischen Klartext, TLS
  ab dem ersten Byte und STARTTLS nach RFC 6120 §5.4; Vorgabe ist STARTTLS.
  Die Aushandlung steht im Transport und nicht in `S2SStream` — der Stream vor
  TLS ist ein Wegwerfstream, dessen Zustand nach der Verschlüsselung verworfen
  wird (§5.4.3.3), und so bekommt die Protokollschicht gar keine Gelegenheit,
  etwas daraus zu übernehmen. `TcpFederationTests` läuft seither zweimal, einmal
  je Betriebsart.
- ~~**Kein Lauf gegen ejabberd oder Prosody.**~~ ✅ nachgeholt in S8 — und die
  Lücke war grösser als vermutet, siehe dort.
- **Keine SRV-Auflösung**, Gegenstellen werden von Hand eingetragen.

**S4b-7 ✅ SASL-EXTERNAL (XEP-0178).** Die Domain der Gegenstelle wird über ihr
TLS-Zertifikat belegt statt über eine Rückfrage. `TcpServerLinks.UseSaslExternal`
fordert dafür ein Klientzertifikat an; `CertificateIdentity` sagt, für welche
Domains es gilt. Der Unterschied ist von aussen messbar und wird auch so
geprüft: mit SASL-EXTERNAL bleibt `DialbackVerificationCount` auf null, ohne es
steigt er. Die Zahl der Verbindungen taugt dafür nicht — über die Grenze läuft
noch anderes, unter anderem die automatische Empfangsbestätigung des Clients,
und genau daran ist die erste Fassung dieses Tests gescheitert.

Absichtlich streng: gibt es eine SAN-Erweiterung, zählt der Common Name nicht
mehr (RFC 6125 §6.4.4) — sonst genügte ein Zertifikat mit passendem CN und
harmlosen SANs. Platzhalter gelten nicht. Nach einem `<failure/>` gibt es
keinen Rückfall auf Dialback: wer sich per Zertifikat ausweisen wollte und
abgelehnt wurde, hat ein Problem, das ein schwächeres Verfahren verdeckt statt
löst.

Was dabei aufflog: der Stream-Neustart nach erfolgreichem SASL (RFC 6120 §6.4.6)
braucht **zwei** Dinge, die beide zuerst fehlten. Der XML-Zerleger muss
zurückgesetzt werden, sonst hält er den zweiten `<stream:stream>` für ein
Kindelement des ersten und wartet ewig auf dessen schliessendes Tag — die
Verbindung stünde still, ohne dass etwas kaputt aussähe. Und „ausgewiesen"
allein reicht als Startsignal nicht: einen Augenblick lang ist der Stream
ausgewiesen und trotzdem nicht offen, und wer da sendet, verliert die Stanza
lautlos. Dafür gibt es jetzt `WaitUntilReadyAsync`.

**Was an S4b-7 offen bleibt:**

- **`id-on-xmppAddr` wird nicht gelesen** (OID 1.3.6.1.5.5.7.8.5), obwohl
  XEP-0178 es als die vorgesehene Form nennt. Es steckt als `otherName` in der
  SAN, und die Bibliothek zählt nur dNSName und IP-Adressen auf. Eine
  Gegenstelle, die sich *nur* darüber ausweist, wird abgelehnt, obwohl sie im
  Recht ist.
- **Nur auf der TCP-Strecke.** Über WebSocket bleibt Dialback der einzige Weg.
- **Die Kette wird nicht gegen eine CA geprüft.** `CertificateIdentity` sagt,
  *wofür* ein Zertifikat ausgestellt ist, nicht, ob ihm zu trauen ist — das
  entscheidet die hinterlegte Prüfung im TLS-Handshake, im Testaufbau ein
  angehefteter Fingerabdruck.

**S4b-8 ✅ SRV-Auflösung (RFC 6120 §3.2.1).** Gegenstellen müssen nicht mehr von
Hand eingetragen werden. `DnsS2SAddressResolver` fragt
`_xmpp-server._tcp.<domain>` und fällt ohne Eintrag auf die Domain selbst
zurück; `SrvSelection` bringt die Ziele in die Reihenfolge aus RFC 2782. Ein
Eintrag von Hand geht weiterhin vor - eine Entscheidung des Betreibers wiegt
schwerer als eine Auskunft aus dem Netz.

Der auswahlkritische Teil ist die Gewichtung: sie ist **keine** Sortierung nach
Gewicht, sondern eine gewichtete Ziehung ohne Zurücklegen. Wer stattdessen
absteigend sortiert, schickt allen Verkehr an den stärksten Rechner, und die
Lastverteilung findet nie statt - auffallen würde das erst im Betrieb, und auch
dort nur jemandem, der die Auslastung anschaut. Die Zufallsquelle ist deshalb
einsetzbar, damit der Ablauf prüfbar bleibt.

**Geprüft wird gegen einen echten DNS-Server**, nicht gegen eine nachgebaute
Antwort: Hermod bringt einen mit, `InMemoryDNSZone` nimmt die Einträge, und die
Abfrage läuft über echte DNS-Pakete. Das hat sich sofort ausgezahlt -
`DnsFederationTests` verkabelt zwei Server ganz ohne Liste und deckte dabei
auf, dass die **Dialback-Rückfrage** den Resolver gar nicht benutzte. Sie
schaute nur in die Gegenstellenliste; mit einem nachgebauten Resolver wäre das
nie aufgefallen, weil kein Test ohne Liste ausgekommen wäre.

**Was das für die Vertrauenswurzel bedeutet, und es ist eine Verschlechterung:**
bisher stand bei der Dialback-Prüfung ausschliesslich die Liste des Betreibers,
und genau daraus bezog sie ihre Schärfe. Wird die autoritative Adresse über DNS
gesucht, ist Dialback nur noch so verlässlich wie die Auflösung - so ist
XEP-0220 gemeint, aber es ist weniger als vorher. Wer das nicht will, lässt
`AddressResolver` null und trägt seine Gegenstellen ein.

Unverändert gilt: **das Zertifikat wird gegen die gesuchte Domain geprüft**,
nicht gegen den Rechnernamen aus dem SRV-Eintrag (RFC 6120 §13.7.2.1). Sonst
brächte ein Angreifer, der DNS fälschen kann, den Massstab gleich mit. Dafür
gibt es einen eigenen Test, und die Mutation dazu wird gefangen.

**Was an S4b-8 offen bleibt:**

- **Kein `_xmpps-server._tcp`** (XEP-0368, direktes TLS ohne STARTTLS). Die
  Auswahl zwischen beiden Diensten wäre eine eigene Entscheidung.
- **Kein DNSSEC.** Ohne das bleibt die Auflösung unbeglaubigt; sie sagt, wohin
  verbunden wird, nie mit wem.

**Zwei Dinge, die dabei nicht untergehen dürfen:**

- **Der schwächere Weg bestimmt das Niveau.** Beide Transporte müssen die
  Domain der Gegenstelle gleich gut belegen. `S2SStream` lässt sich weiterhin
  ohne Dialback bauen (`RequiresDialback == false`) — das ist für
  `DirectServerLinks` und für einen späteren SASL-EXTERNAL-Weg gedacht, nicht
  als Abkürzung. Der WebSocket-Transport schaltet es nirgends ab.
- **Dialback klebt am Stream.** XEP-0220 ist über XML-Streams definiert und
  hängt an der Stream-ID. Über WebSocket gibt es kein `<stream:stream>`,
  sondern `<open/>` mit `id` (RFC 7395 §3.4) — `S2SStream.StreamId` trägt sie.
  Das funktioniert, ist aber eine eigene Festlegung, die ausser uns niemand
  kennt; ob TCP dieselbe Schicht unverändert trägt, entscheidet sich in S4b-4.

---

### S5. Domainübergreifende Subscriptions ✅

Der Handshake aus RFC 6121 §3 nahm an, dass derselbe Server beide Roster in der
Hand hat: der ausgehende Weg pflegte beide Hälften, der eingehende gar keine.
Eine Subscription-Presence von aussen wurde nur an den Client durchgereicht -
sie kam an, aber der Server vergass sie, und die Antwort fand keinen Eintrag
vor, den sie hätte ändern können.

Jetzt pflegt jede Seite genau **eine** Hälfte, nämlich ihre eigene, und die
Übereinstimmung entsteht allein daraus, dass beide dieselbe Stanzafolge
verschieden auslegen: der eine setzt `from`, der andere `to`. Die andere Hälfte
zu raten wäre falsch — über die Grenze erfährt man voneinander nur das, was
ausdrücklich geschickt wird.

Umgesetzt sind die vier Übergänge (§3.1.6, §3.2.3, §3.3.3) und die
Selbstbeantwortung aus §3.1.4: darf der Antragsteller den Kontakt ohnehin schon
sehen, antwortet dessen Server selbst, statt den Nutzer erneut zu fragen. Ohne
das käme ein Antragsteller, dessen Roster verlorenging, nie wieder in Ordnung,
ohne den Kontakt zu behelligen.

Ausserdem adressiert `RouteToAsync` ausgehende Stanzas jetzt zentral. Innerhalb
eines Servers weiss er selbst, an wen er verteilt; über die Grenze ist das `to`
alles, was die Gegenstelle hat.

**Was offen bleibt:**

- ~~Eine Anfrage an ein gerade nicht verbundenes Konto wird nicht aufbewahrt
  (§3.1.3)~~ ✅ erledigt in S7 — und zwar für beide Fälle in derselben Stelle.
- Die zentrale Adressierung in `RouteToAsync` ist durch keinen Test
  festgehalten; siehe den Vermerk im Code.

### S6. Subscription-Pre-Approval ✅

RFC 6121 §3.4: einen Kontakt zulassen, bevor er fragt. Der Abschnitt
unterscheidet vier Fälle, und alle hängen an derselben Frage — liegt eine
Anfrage vor oder nicht. Dasselbe `<presence type='subscribed'/>` ist einmal eine
Zustimmung und einmal eine Vormerkung; die Stanza sieht in beiden Fällen gleich
aus, der Unterschied steckt allein im Roster des Absenders.

Dafür musste der Server erst lernen, offene Anfragen zu vermerken. `RosterEntry`
bekam `PendingIn` neben `Ask` — die beiden Richtungen derselben Frage: die eine
hält fest, dass *wir* gefragt haben, die andere, dass *gefragt wurde*. Ohne
beide liesse sich §3.4 gar nicht umsetzen. Dazu kommt `Approved`, das als
`approved='true'` in Roster-Ergebnis und -Push erscheint. (`PendingIn` ist in S7
wieder verschwunden; die Tatsache liegt seither vollständig woanders.)

Die leicht zu übersehende Hälfte: bei einer Vormerkung darf das `subscribed`
**nicht** hinausgehen (Fälle 3 und 4). Ginge es doch, bekäme der Kontakt eine
Zustimmung zu einer Frage, die er nie gestellt hat, und sein Server baute daraus
eine Subscription, von der sein Nutzer nichts weiss. Umgekehrt darf eine
vorgemerkte Anfrage dem Nutzer gar nicht erst zugestellt werden — der Server
antwortet für ihn.

Der eingehende `subscribe` läuft jetzt für lokale und fremde Herkunft durch
dieselbe Stelle. Die Entscheidung hängt nicht daran, woher die Anfrage kam,
sondern allein am Roster des Empfängers; sie zweimal zu treffen hiesse, zwei
Gelegenheiten zu schaffen, sie verschieden zu treffen.

**Der Client hat eine eigene Hälfte bekommen.** `AcceptSubscriptionAsync` bricht
ohne offene Anfrage ab und stellt eine Gegenanfrage — beides ist für eine
Vormerkung falsch. `PreApproveContactAsync` tut weder das eine noch das andere
und verweigert von sich aus, wenn der Server das Feature nicht angekündigt hat
(§3.4.1 verlangt genau das).

**Was offen blieb:** eine Anfrage an ein gerade nicht verbundenes Konto wurde
weiterhin nicht aufbewahrt (§3.1.3) — erledigt in S7.

### S7. Aufbewahrte Subscription-Anfragen ✅

RFC 6121 §3.1.3, Regel 4: wer gerade nicht verbunden ist, soll seine Anfragen
trotzdem bekommen. Bis hierher gingen sie ersatzlos verloren, und zwar unbemerkt
auf beiden Seiten — der Antragsteller sah `ask='subscribe'` in seinem Roster und
wartete auf eine Antwort, der Kontakt hatte nie erfahren, dass er gefragt wurde.

**Aufbewahrt wird immer, nicht nur wenn gerade niemand da ist.** Die Regel
verlangt die Zustellung an *jede* Resource, die der Kontakt danach noch anlegt,
bis er zustimmt oder ablehnt. Eine Anfrage nur dann aufzuheben, wenn zufällig
niemand verbunden war, verfehlte genau den häufigen Fall: angemeldet, aber
gerade nicht hingesehen, dann abgemeldet. Damit fällt auch die Fallunterscheidung
weg — es gibt keinen Offline-Zweig, sondern einen Weg.

**`RosterEntry.PendingIn` ist wieder weg.** Der Zustand aus S6 war ein Ja/Nein
für dieselbe Tatsache, die jetzt vollständig in
`XMPPAccount.PendingSubscriptionRequests` liegt: die aufbewahrte Anfrage *ist*
die offene Anfrage. Zwei Orte für eine Tatsache laufen über kurz oder lang
auseinander; §3.4.2 fragt seither dieselbe Stelle, die §3.1.3 füllt. Das
Fragen und das Erledigen sind dabei ein Schritt (`ForgetSubscriptionRequest`
liefert, ob etwas vorlag), damit sie nicht getrennt werden können.

**Und der Roster bleibt sauber.** Die Security Warning desselben Abschnitts
untersagt einen Roster-Eintrag für einen Antragsteller, dem noch nicht
zugestimmt wurde — bisher entstand einer, mit `subscription='none'`. Wer
beliebige Fremde in fremde Roster schreiben kann, kann sie vollschreiben.

**Die Stanza wird gestempelt statt neu gebaut.** `HandleSubscriptionAsync` setzte
bisher ein frisches `<presence …/>` zusammen und warf damit das `<status/>` weg —
die Begründung, mit der ein Mensch über die Zustimmung entscheidet. Regel 4
verlangt aber die *vollständige* Stanza, "including any extended content
contained therein"; ohne diese Änderung wäre die Berufung darauf falsch gewesen.

**Zwei Grenzen, beide aus dem Abschnitt selbst.** Je Absender bleibt genau eine
Anfrage stehen, und zwar die erste — sonst bestimmte, wer zuletzt fragt, was der
Kontakt zu sehen bekommt, und könnte es beliebig oft austauschen (Anhang A,
Tabelle 6 sagt dazu: nicht noch einmal zustellen). Dazu
`MaxStoredSubscriptionRequests` je Konto, Vorgabe 100: die Security Warning rät
ausdrücklich zu einer Obergrenze, weil aufgehoben wird, was Fremde schicken. Ist
sie erreicht, wird die neue Anfrage verworfen statt eine aufbewahrte zu
verdrängen — andersherum liesse sich die echte Anfrage eines Bekannten gezielt
hinausdrängen.

**Nebenbefund:** `FileAccountStore` schrieb `Approved` seit S6 gar nicht mit —
eine Vormerkung überlebte keinen Neustart. Jetzt persistieren beide, Vormerkung
und aufbewahrte Anfrage; "aufbewahrt" hiesse sonst "bis zum nächsten Neustart".

14 Mutationen, 11 sofort tot. Die drei Überlebenden waren aufschlussreich:

- *Nachreichen bei jeder Presence statt beim Verfügbar**werden***: kein Test sah
  den Unterschied. Im Betrieb hätte jeder Wechsel auf "abwesend" dieselbe
  unbeantwortete Anfrage erneut vorgelegt. Festgehalten durch
  `AStatusChange_DoesNotRepeatTheRequest`.
- *Eine wiederholte Anfrage verdrängt die aufbewahrte*: unsichtbar, weil der
  einzige Test dazu den Kontakt abgemeldet hatte — dann sieht Abweisen und
  Ersetzen gleich aus. Bei verbundenem Kontakt geht jede angenommene Anfrage
  sofort hinaus, und der Unterschied wird sichtbar.
  `AFurtherRequest_IsNotDeliveredAgain` prüft jetzt beides, Zahl und Inhalt.
- *`AutoApproveAsync` vergisst die Anfrage nicht*: überlebt, und das zu Recht —
  der einzige Aufrufer entscheidet sich für die selbsttätige Zustimmung, bevor
  er aufbewahrt, und beide Wege zu `from` räumen die Anfrage ohnehin ab. Die
  Zeile ist eine Aussage über die Reihenfolge in `DeliverSubscribeAsync`, nicht
  über diese Methode; sie steht mit genau diesem Vermerk im Code.

**Was offen bleibt:** die Obergrenze wirft still weg — weder der Antragsteller
noch der Kontakt erfährt davon. Das ist die vom Abschnitt empfohlene Antwort auf
die Erschöpfungsgefahr, aber es bleibt ein Verlust ohne Quittung.

### S8. Lauf gegen Prosody ⚠️ — ein Fund, der die Föderation betrifft

Seit S4b stand hier: *jedes einzelne Verfahren ist gegen die eigene Gegenstelle
geprüft, keines gegen eine fremde.* Der Lauf ist nachgeholt, gegen Prosody 13.0.1.

**Der Aufbau steht in `tools/prosody/setup.sh` und braucht kein root.** Das Paket
lässt sich mit `apt-get download` holen und mit `dpkg-deb -x` in ein Präfix
auspacken; Prosody bringt fertige Binärmodule mit, es wird nichts übersetzt. Vier
fest eingebaute Pfade im Debian-Launcher werden umgebogen, dazu `LUA_PATH`,
`LUA_CPATH` und `LD_LIBRARY_PATH`. Zwei Fallen unterwegs, beide ohne brauchbare
Fehlermeldung: `libicu76` steht nicht in den Abhängigkeiten, wird aber von
`util.encodings.so` gebraucht; und Prosodys `certmanager` verwirft PEM-Dateien
mit CRLF wortlos als *"non-certificate (based on contents)"*.

**Was auf Anhieb trug.** STARTTLS nach RFC 6120 §5.4, TLS 1.3, unser
CA-signiertes Zertifikat als Klientzertifikat, `EXTERNAL` von Prosody angeboten
und angenommen — im Log: *"Accepting SASL EXTERNAL identity from jabber.test"*,
*"Incoming s2s connection jabber.test->prosody.test complete"*. Unsere Stanza kam
an und wurde verarbeitet. Der ganze Weg hinaus stimmt.

**Dafür musste der Server erst ein Zertifikat von aussen annehmen können.** Er
baute sich immer ein selbst signiertes. Das kann keine fremde Gegenstelle prüfen:
sie müsste genau dieses eine kennen, und es entsteht bei jedem Start neu.
`XMPPServer` nimmt jetzt eines entgegen — nicht für den Test, sondern weil jeder
Betrieb ausserhalb dieser Testsammlung es braucht.

**Und dann der Fund.** Prosody beantwortet den Ping — die Antwort steht im Log —
und schickt sie **nicht** über den Stream zurück, über den die Frage kam. Es baut
dafür eine *eigene* Verbindung zu `jabber.test` auf, scheitert daran und verwirft
die Antwort:

```
Received[s2sin]: <iq from='alice@jabber.test/...' to='prosody.test' type='get'>
mod_s2s  debug  opening a new outgoing connection for this stanza
s2sout   debug  s2s connection attempt failed: unable to resolve service
s2sout   debug  Not eligible for bouncing, discarding <iq ... type='result' ...>
```

Genau so ist RFC 6120 §4.1 gemeint: ein XML-Stream ist **einseitig**, und eine
S2S-Verbindung trägt nur eine Richtung.

> **Korrektur (S9).** Hier stand zuerst, unsere Föderation antworte über denselben
> Stream und mache es damit falsch. Das ist nachweislich nicht so.
> `TcpServerLinks.DeliverAsync` und `WebSocketServerLinks.DeliverAsync` gehen
> ausnahmslos über `GetOrCreateOutboundAsync`, und `S2SStream.ProcessStanzaAsync`
> weist eine Stanza auf einem ausgehenden Stream ausdrücklich ab — mit genau
> diesem Abschnitt als Begründung im Kommentar. Unsere Seite verhält sich also
> wie Prosody.
>
> Was der Lauf wirklich zeigte: Prosody kam an `jabber.test` nicht heran. In WSL
> gibt es kein DNS für `.test`, und die Hyper-V-Firewall verwirft den Weg von WSL
> zum Windows-Host ohnehin. Beide Seiten haben sich richtig verhalten, die
> Umgebung liess die Rückrichtung nicht zu.
>
> Der Lauf hat damit den **ausgehenden** Weg gegen eine fremde Gegenstelle
> belegt und den **eingehenden** offen gelassen — nicht wegen eines Fehlers,
> sondern weil die Gegenstelle keinen Weg zurück hatte. Der Fehler sass in
> meiner Lesart des Logs, nicht im Code.

**Was daraus wirklich folgt — zwei Wege, beide eigene Arbeitsschritte:**

- **XEP-0288 (Bidirectional Server-to-Server Streams).** Beide Richtungen über
  eine Verbindung, ausgehandelt über `urn:xmpp:features:bidi`. Prosody kündigt es
  an, sobald `mod_s2s_bidi` läuft — der Aufbau schaltet es ein, und die
  Ankündigung ist geprüft. Kein Fehlerbehebung, sondern die Erweiterung, die den
  Rückweg überflüssig macht: genau das, was hier fehlt. Erledigt in S9.
- **Eingehende Verbindungen gegen eine fremde Gegenstelle prüfen**, also Prosody
  uns anwählen lassen. Der Weg ohne Erweiterung. Der Code dafür steht, geprüft
  ist er nur gegen die eigene Gegenstelle.

**Was den zweiten Weg hier zusätzlich blockiert:** WSL2 läuft im NAT-Modus, und
die Hyper-V-Firewall verwirft Verbindungen von WSL zum Windows-Host. Windows →
WSL geht, die Gegenrichtung nicht. Das trifft auch Dialback, dessen Rückfrage
genau diese Richtung braucht — deshalb läuft der Aufbau über SASL-EXTERNAL mit
einer gemeinsamen Test-CA und nicht über XEP-0220. Zu ändern wäre das über
`networkingMode=mirrored` in `.wslconfig` oder eine Firewall-Regel; beides ist
eine Entscheidung über die Maschine, nicht über dieses Projekt.

**Testsammlung.** `ProsodyFederationTests` überspringt sich ohne Aufbau, sodass
der gewöhnliche Lauf unberührt bleibt. `TheStreamToProsodyCarriesAStanza` besteht
gegen die echte Gegenstelle. `APingReachesProsodyAndComesBack` ist stillgelegt
statt gelöscht — er hält fest, dass ohne XEP-0288 keine Antwort kommt, solange
die Gegenstelle uns nicht erreichen kann.

### S9. XEP-0288 — beide Richtungen über eine Verbindung ✅

Die Erweiterung, die den Rückweg überflüssig macht. Der Initiator schickt nach
TLS ein `<bidi xmlns='urn:xmpp:bidi'/>`, sobald die Gegenstelle
`urn:xmpp:features:bidi` ankündigt; danach trägt dieselbe Verbindung beide
Richtungen.

**Beide Rollen, nicht nur die eine.** Angeboten auf eingehenden Verbindungen,
erbeten auf ausgehenden — `UseBidirectionalStreams` schaltet beides zugleich.
Nur die halbe Erweiterung hälfe nur der halben Föderation.

**Die zwei Sicherungen aus Abschnitt 4, und beide sind keine Formalitäten:**

- *"MUST NOT send stanzas to the peer before it has authenticated"* — wer nicht
  belegt hat, wer er ist, bekommt nichts. Ohne diese Zeile liesse sich mit einer
  blossen Behauptung im Stream-Kopf fremde Post abholen: Verbindung aufbauen,
  sich `example.com` nennen, um die Rückrichtung bitten, warten.
- *"MUST only send stanzas for which it has been authenticated … the value of
  the stream's 'to' attribute"* — über die Rückrichtung geht nur die eigene
  Domain hinaus. Dieselbe Prüfung, die wir der Gegenstelle auferlegen, gilt hier
  für uns.

Dazu ein unangekündigtes `<bidi/>` abzuweisen: sonst liesse sich eine
Rückrichtung erzwingen, die dieser Server nie angeboten hat.

**Die Reihenfolge hat etwas aufgedeckt.** Das `<bidi/>` muss vor SASL *und* vor
Dialback hinaus. Unser Initiator schickte den unaufgeforderten `<db:result/>`
aus XEP-0220 aber schon beim Stream-Kopf — also bevor die Features überhaupt da
waren, aus denen sich das Bidi-Angebot ablesen lässt. Er wartet jetzt in beiden
Fällen auf die Features. `BidiAlsoGoesOutBeforeDialback` hat das beim ersten
Lauf gefunden.

**Geprüft gegen Prosody.** `APingOverABidirectionalStream` besteht gegen die
echte Gegenstelle, und ihr Log zeigt genau das Erwartete:

```
Received[s2sin_unauthed]: <bidi xmlns='urn:xmpp:bidi'>
debug   Requested bidirectional stream
Received[s2sin]:  <iq ... type='get' id='ping-1'>
Sending[s2sin]:   <iq from='prosody.test' ... type='result'>
```

`Sending[s2sin]` statt `opening a new outgoing connection` — die Antwort nimmt
die bestehende Verbindung. Damit ist auch der **eingehende** Weg unserer
Protokollschicht erstmals gegen eine fremde Gegenstelle belegt, wenn auch über
die Rückrichtung und nicht über eine echte eingehende Verbindung.

**Der Aufbau der Tests ist absichtlich einseitig.** `links` kennt `rechts`,
`rechts` kennt `links` nicht. Der übliche `TcpServerLinks.Connect` taugt dafür
nicht — er trägt beide Seiten ein, und dann käme die Antwort über eine eigene
Verbindung an, ohne dass Bidi je beteiligt gewesen wäre. Deshalb prüft der Test
`BidirectionalDeliveryCount` und nicht nur die Ankunft, und deshalb gibt es
`WithoutBidi_TheAnswerIsLost` als Gegenprobe.

Nebenbei: in diesem Aufbau taugt Dialback nicht als Nachweis, denn seine
Rückfrage ginge ausgerechnet in die Richtung, die es nicht gibt. SASL-EXTERNAL
kommt ohne Rückweg aus — dieselbe Überlegung wie beim Prosody-Aufbau.

11 Mutationen, 9 sofort tot. Die beiden Überlebenden:

- *Auswahl ohne Domainabgleich*: unsichtbar, weil an jedem Test nur eine
  Gegenstelle hing. Im Betrieb wäre es ein Leck zwischen zwei fremden Servern —
  die Stanza ginge an die falsche Gegenstelle, die sie zwar verwirft, aber
  vorher gelesen hat, und der eigentliche Empfänger bekäme nichts, ohne dass
  irgendwo ein Fehler aufliefe. Festgehalten durch
  `TheReturnPath_GoesToTheRightDomain` mit drei Servern.
- *Schalter im Transport*: überlebt zu Recht. `BidiEnabled` wird nur gesetzt,
  wenn der Stream mit `offerBidi` angelegt wurde, und das kommt aus demselben
  Schalter — die Zeile ist eine Abkürzung, keine Sicherung. Sie steht mit
  diesem Vermerk im Code.

**Was offen blieb:** WebSocket-S2S handelte Bidi nicht aus — nachgeholt in S9b.

### S9b. XEP-0288 auch über WebSocket ✅

Dieselbe Erweiterung auf dem zweiten Transport. Im Betrieb fällt sie dort
weniger ins Gewicht, weil an beiden Enden Instanzen dieses Servers hängen, die
einander eingetragen haben. Sie trotzdem zu haben ist die Antwort darauf, dass
zwei Transporte unter derselben Protokollschicht sich nicht verschieden
verhalten sollten: was für den einen gilt, soll man beim anderen nicht erst
nachschlagen müssen.

**Die Auswahlregel liegt jetzt an einer Stelle.**
`S2SStream.TryDeliverOverBidiAsync` — der Abgleich der Domain ist genau die
Regel, an der beim Mutationslauf in S9 eine Mutation vorbeikam, und zwei
Fassungen davon wären zwei Gelegenheiten für denselben Fehler gewesen. Seither
töten Tests **beider** Transporte dieselbe Mutation.

**Der Aufbau kann die scharfe Probe aus S9 nicht wiederholen, und das ist
eingestanden.** Über TCP kennt die Gegenstelle uns nicht, und ohne Rückrichtung
geht die Antwort verloren — daran hängt dort die Beweiskraft. Über WebSocket
geht das nicht: dieser Weg weist sich ausschliesslich über Dialback aus
(SASL-EXTERNAL gibt es hier nicht), und dessen Rückfrage braucht genau die
Richtung, die es dann nicht gäbe. Beide Seiten sind hier also eingetragen, die
Antwort käme auch ohne Bidi an, und deshalb prüfen diese Tests
`BidirectionalDeliveryCount` statt der Ankunft. Der Fixture-Kommentar sagt das,
damit der Unterschied nicht wie Nachlässigkeit aussieht.

**Ein Test von mir war schlicht falsch.** Ich hatte angenommen, der anwählende
Server benutze keine Rückrichtung — er hat ja keine eingehende Verbindung. Der
Zähler stand aber auf 3. Sobald auch nur eine Stanza zurückläuft, schon eine
Empfangsbestätigung nach XEP-0184, wählt die Gegenstelle ihrerseits an, und
dann hat auch die erste Seite eine eingehende Verbindung, die sie fortan
bevorzugt. Zwei sich gegenseitig kennende Server fallen unter Bidi also auf die
Verbindungen zusammen, die sie ohnehin haben. Das ist der Zweck der Erweiterung
— aber nichts, worauf sich eine zeitunabhängige Zusicherung stützen liesse. Der
Test ist ersetzt, die Beobachtung steht als Kommentar am verbliebenen.

5 Mutationen, alle tot.

### P4. Prosody wählt uns an ✅

Der eingehende Weg gegen eine fremde Gegenstelle — die letzte Richtung, die
nur gegen die eigene geprüft war. Was hier zum ersten Mal vor einem echten
Server stand: unser Stream-Kopf als Antwortender, unsere Feature-Ankündigung,
unsere Annahme eines fremden `<auth mechanism='EXTERNAL'/>` und die
Identitätsprüfung aus dem vorgelegten Zertifikat. Der Rückweg aus S9 lief zwar
in eingehender Richtung, aber über einen Stream, den *wir* aufgebaut hatten.

Prosodys Log sagt es genau:

```
prosody.test:saslauth  Initiating SASL EXTERNAL with localhost
prosody.test:saslauth  SASL EXTERNAL with localhost succeeded
s2sout   Outgoing s2s connection prosody.test->localhost complete
s2sout   Sending[s2sout]: <iq to='alice@localhost/...' type='result' from='prosody.test'>
```

**Kein Eingriff in die Firewall.** Der Blocker war die ganze Zeit, dass die
Hyper-V-Firewall (`DefaultInboundAction = Block` auf dem WSL-vSwitch) jede
Verbindung von WSL zum Windows-Host verwirft. Eine Regel dafür zu setzen wäre
eine Änderung an den Sicherheitseinstellungen der Maschine. Es geht auch ohne:
in WSL liegt ein .NET-10-SDK, also läuft der Test **dort**, im selben Netz wie
Prosody — alles Rückschleife, keine Firewall dazwischen.

    JABBER_PROSODY_CERTS=~/prosody-test/certs \
    dotnet test /mnt/c/.../Jabber.Tests/Jabber.Tests.csproj \
        --artifacts-path /tmp/jabber-artifacts \
        --filter FullyQualifiedName~ProsodyFederationTests

Das `--artifacts-path` hält die Linux-Bauartefakte aus dem Windows-Baum heraus;
ohne das schreiben sich beide Läufe gegenseitig die `obj`-Verzeichnisse um.

**Zwei Namen für unsere Seite, und der Unterschied ist der Kern.** Damit Prosody
uns anwählen kann, muss es unsere Domain auflösen. Ein Eintrag in `/etc/hosts`
bräuchte root; `localhost` steht dort ohnehin. Der Testserver bedient im
eingehenden Fall also diese Domain und horcht auf 5269 — dem Port, auf den
Prosody ohne SRV-Eintrag zurückfällt. Prosody weicht dafür auf 15269 aus und
bindet nur 127.0.0.1. Für den ausgehenden Fall bleibt es bei `jabber.test`, wo
die Adresse von Hand steht und kein DNS nötig ist.

**Ausdrücklich ohne XEP-0288.** Mit Bidi käme die Antwort über den bestehenden
Stream, und der eingehende Weg bliebe wieder ungeprüft. Der Test hält das mit
zwei Nebenbedingungen fest: `InboundConnectionCount > 0` und
`BidirectionalDeliveryCount == 0`. Ohne sie bestünde er auch dann, wenn die
Antwort einen ganz anderen Weg genommen hätte.

**Keine Mutationen für diesen Schritt** — es gibt keinen neuen Produktivcode.
P4 ändert nur den Aufbau und die Testsammlung; sein Ertrag ist, dass
vorhandener Code erstmals vor einer fremden Gegenstelle bestanden hat.
`APingReachesProsodyAndComesBack` ist entfallen: der stillgelegte Test sagte
nichts mehr, was die beiden laufenden nicht sagen.

### P5. Dialback gegen Prosody ✅ — und ein Fehler, den der Lauf herausholte

XEP-0220 war zuletzt das einzige Verfahren, das nur gegen die eigene
Gegenstelle geprüft war. Ein Ping-Rundlauf übt beide Rollen auf einmal, weil
jede Richtung ihre eigene Verbindung aufbaut und jede aufbauende Seite sich
ausweisen muss: wir wählen an und schicken `<db:result/>`, Prosody fragt beim
autoritativen Server unserer Domain nach — das sind wieder wir. Dann wählt
Prosody an, um die Antwort zuzustellen, schickt seinerseits `<db:result/>`, und
wir fragen bei `prosody.test` nach. Beide Rollen, ein Test.

Welches Verfahren zum Zug kommt, entscheidet dabei **unsere** Seite: legen wir
ein Klientzertifikat vor, bietet Prosody `EXTERNAL` an; legen wir keines vor,
bleibt nur Dialback. `UseSaslExternal` ist der ganze Unterschied zwischen den
beiden Tests, und `DialbackVerificationCount` trennt sie sauber — im
EXTERNAL-Fall muss er null sein, im Dialback-Fall grösser null. Ohne diese
beiden Zusicherungen bestünde jeder Test auch im jeweils anderen Regime.

**Ein Prosody-Schalter, der stillschweigend nichts tut.** Zuerst stand hier ein
zweiter VirtualHost mit `s2s_secure_auth = false`. Er sah richtig aus und wirkte
nicht: `mod_s2s` ist ein **globales** Modul und liest den Schalter *einmal* beim
Laden (`mod_s2s.lua`, Zeile 40). Pro VirtualHost gesetzt geht er ins Leere.
Prosody wies uns weiter mit `<not-authorized/>` ab — „Your server's certificate
could not be validated". Der vorgesehene Weg ist die Ausnahmeliste
`s2s_insecure_domains`, und die ist jetzt drin; der zweite VirtualHost ist
wieder weg, weil eine Konfigurationszeile ohne Wirkung schlimmer ist als keine.

**Und dann der eigentliche Fund: `TcpServerLinks.DisposeAsync` liess angenommene
Verbindungen offen.** Es brach den Token ab, beendete den Listener und räumte
die *ausgehenden* Streams ab — die eingehenden nicht. Das Abbrechen des Tokens
genügt dafür nicht: der Lesevorgang auf einem Socket bricht damit nicht
zuverlässig ab, die Schleife bleibt stehen, bis die Gegenstelle auflegt.

Sichtbar wurde es daran, dass Prosody die nächste Anfrage noch dreissig Sekunden
lang über den längst toten Socket beantwortete — der Testserver war weg, die
Verbindung aus Prosodys Sicht nicht. Zwischen zwei Instanzen dieses Servers
fällt das nie auf, weil dort beide Seiten gleichzeitig verschwinden. Im Betrieb
heisst es: wer den Server beendet, lässt jede Gegenstelle im Glauben, sie könne
weiter zustellen, und alles Zugestellte ist verloren.

Festgehalten durch `DisposingTheLinks_ClosesEstablishedInboundConnections` —
ohne TLS, weil es um den Socket geht und nicht um den Handshake darüber. Die
Mutation, die das `Dispose` der Verbindung wieder herausnimmt, stirbt an genau
diesem Test.

Nebenbei fiel im Fixture dasselbe Versäumnis auf: der Teardown räumte `_links`
nicht ab, und der festgehaltene Port 5269 fehlte dem nächsten Test. Ein
gescheiterter Bind sieht dabei aus wie ein Protokollfehler — das kostete zwei
Testläufe.

### P6. Lauf gegen ejabberd ✅ — der zweite Zeuge, und was er allein sah

Prosody allein belegt, dass wir mit Prosody können. Wo unsere Auffassung des
Protokolls von der Norm abweicht, Prosody dieselbe Abweichung aber mitmacht,
fällt das nicht auf. Deshalb ein zweiter, unabhängig entstandener Server:
ejabberd 24.12, in Erlang, anderer Werdegang, anderer Autorenkreis.

Aufbau in `tools/ejabberd/setup.sh`, nach demselben Muster wie Prosody: ohne
root ausgepackt, eigene Test-CA, `ejabberd.test` auf 127.0.0.1:25269. Zwei
Stellen wollten dabei anderes Werkzeug als bei Prosody:

- **Erlang ist in Debian fest auf `/usr/lib/erlang` verdrahtet** — und zwar in
  allen drei Zweigen der Fallunterscheidung im `erl`-Startskript, auch in dem,
  der laut Quelltext `ERL_ROOTDIR` beachten soll. Die Variable zu setzen sieht
  aus, als müsste es reichen, und tut nichts. Dieselbe Falle wie Prosodys
  `CFG_*`, nur besser getarnt.
- **`ejabberdctl` bricht mit „can only be run by root or the user ejabberd" ab,**
  bevor es irgendetwas tut. `INSTALLUSER` leeren, dann geht es.

Die vier Tests spiegeln die Prosody-Sammlung; die gemeinsame Mechanik ist nach
`AForeignPeerFederationTests` gezogen, sodass jede Sammlung nur noch Domain,
Ports, Umgebungsvariable und ihre eigene Prosa trägt. ejabberd horcht auf 25269
und wählt uns auf 5270 an (`outgoing_s2s_port`) — beide Gegenstellen können
damit nebeneinander laufen.

**Der Fund: wir übersahen ejabberds Bidi-Angebot.** XEP-0288 vergibt zwei
Namensräume und meint zwei Dinge damit — `urn:xmpp:features:bidi` für die
Ankündigung, `urn:xmpp:bidi` für das Element, mit dem der aufbauende Server sie
annimmt. Prosody hält sich daran. ejabberd 24.12 legt in die Features das
*Freischalt*-Element, kündigt also `<bidi xmlns='urn:xmpp:bidi'/>` an.

Wir sahen darin kein Angebot, schickten kein `<bidi/>`, und die Antwort auf den
Ping ging über eine Verbindung, die es nicht gab: dreissig Sekunden
Zeitüberschreitung, kein Fehler, keine Meldung. Genau die Sorte Ausfall, gegen
die XEP-0288 gedacht ist.

Bevor daraus eine Änderung wurde, drei Feststellungen statt Vermutungen:

1. Die XEP (1.0.1, 2016) nennt für die Ankündigung eindeutig
   `urn:xmpp:features:bidi`.
2. ejabberds eigener Codec bildet **beide** Formen auf getrennte Typen ab —
   direkt nachgefragt: `urn:xmpp:features:bidi` → `{s2s_bidi_feature}`,
   `urn:xmpp:bidi` → `{s2s_bidi}`.
3. Seine *aufbauende* Seite sucht `{s2s_bidi_feature}`, ist also konform und
   versteht unsere Ankündigung. Upstream ist die annehmende Seite inzwischen
   behoben (`s2s_in_features(Acc, _) -> [#s2s_bidi_feature{}|Acc].`).

Daraus folgt eine einseitige Änderung: `S2SStream.KuendigtBidiAn` liest beide
Formen, angekündigt wird weiter nur die der XEP. Nachsichtig beim Lesen, streng
beim Schreiben — und keine Zeile mehr als das, weil für eine zweite Ankündigung
kein Beleg vorlag.

**Und ein Test, der zweimal recht gehabt hätte und einmal nicht.** Der
Bidi-Ping bestand, fiel dann durch und bestand wieder. Das Log sagte, woran es
lag: ejabberd hatte den Bidi-Stream, wählte für die Antwort aber einen
*zwischengespeicherten* `s2s_out` nach `jabber.test`, angelegt in einem
früheren Lauf und zeigend auf einen längst toten Ephemeralport.

Woher der stammte: unsere `<message>` an die blosse Domain `ejabberd.test` hat
dort keinen Empfänger und wird zurückgewiesen — und für die Rückweisung legt
ejabberd eine ausgehende Verbindung zu uns an, die den Test überlebt. Ein
`<iq type='result'/>` darf laut RFC 6120, Abschnitt 8.3.1, nie beantwortet
werden und hinterlässt deshalb nichts. Danach dreimal hintereinander 8/8 ohne
Neustart dazwischen.

Zweierlei bleibt daran hängen. Erstens: die Prosody-Sammlung schickt dieselbe
Nachricht und ist bisher nicht aufgefallen — geändert wurde sie trotzdem nicht,
weil dafür kein Beleg vorliegt und Änderung ohne Beleg nur Rauschen ist.
Zweitens: dass ejabberd einen alten `s2s_out` einem bestehenden Bidi-Stream
vorzieht, ist eine zweite Eigenheit; sie beisst nur, weil unser Testserver bei
jedem Lauf auf einem anderen Port liegt.

**Offen geblieben** war, ob ejabberd unsere Ankündigung tatsächlich annimmt,
wenn *es* uns anwählt — hier nur aus seinem Quelltext geschlossen. Inzwischen
beobachtet, und der Schluss war **falsch**: siehe R6.

---

## Client

### XEP-0198 gegen einen echten Server ✅ — und der Client konnte sich gar nicht anmelden

Die Zählung stimmte gegen `XMPPServer`, also gegen unsere eigene Auffassung
davon, was eine Stanza ist. Der Lauf gegen Prosody sollte das prüfen. Er kam
zunächst nicht so weit.

**Der Client konnte sich an keinem RFC-7395-konformen Server anmelden.**
Prosody wies das Bind-IQ mit `<unsupported-stanza-type/>` ab und schloss den
Stream. Im Log stand, warum: es kam als `<iq … xmlns=''>` an.

RFC 7395, Abschnitt 3.3.3 verlangt, dass jeder Rahmen für sich als
vollständiges XML-Dokument lesbar ist, „complete with all relevant namespace
and language declarations". Über TCP steht der Content-Namensraum einmal am
`<stream:stream>` und gilt für alles darin; über WebSocket gibt es dieses
umschliessende Element nicht, und eine Stanza ohne eigene Deklaration steht in
*keinem* Namensraum. Unser Server hat das nie bemängelt, weil er Stanzas am
lokalen Namen erkennt und den Namensraum gar nicht ansieht — beide Seiten
machten denselben Fehler, also fiel er nicht auf.

Behoben in `StanzaNamespace.Apply`, aufgerufen in `XMPPConnection.SendAsync` —
derselben einen Stelle, durch die auch gezählt wird, und aus demselben Grund:
sie ist die einzige, durch die jeder ausgehende Rahmen läuft.

**Und der Fix holte einen zweiten Fehler heraus.** Mit einem Mal fiel
`APingOverABidirectionalStream` durch: unser Server reichte die
`jabber:client`-Stanza unverändert auf den S2S-Stream weiter, und dort ist sie
keine gültige Stanza — Prosody antwortete mit einem Fehler-IQ. Solange die
Stanza gar keinen Namensraum trug, erbte sie auf dem S2S-Stream stillschweigend
den richtigen; der Fehler war die ganze Zeit da und unsichtbar. Er hätte jeden
echten Client getroffen, dessen Stanza über die Domain-Grenze geht.

Behoben in `RouteToAsync`, neben `StampTo` — der einen Weiche zwischen „hier"
und „woanders".

Sieben Mutationen, alle von genau den zuständigen Tests erschlagen: Client
stempelt nicht (vier Prosody-Tests), Server tauscht nicht
(`APingOverABidirectionalStream`), Namensprüfung weg (zwölf Tests quer durch
die Zählung), naives „steht irgendwo ein xmlns" (der Bind-IQ-Fall),
Präfix-Deklaration zählt als Standard-Namensraum, `>` im Attributwert beendet
das Start-Tag, `LastAcknowledged` meldet den eigenen Zähler.

**Das eigentliche Ergebnis:** die Zählung stimmt. `ProsodyCountsTheSetupExactlyAsWeDo`
vergleicht nach dem vollständigen Aufbau — Carbons, Roster, erste Presence, und
dazwischen Nonzas — unseren `OutboundCount` mit Prosodys `h`, und beide Werte
sind gleich. Geprüft wird Gleichheit und nicht nur eine leergelaufene
Warteschlange: ein zu grosses `h` räumte sie ebenfalls, und ein Client, der zu
wenig zählt, käme damit durch. Dafür gibt es `LastAcknowledged` überhaupt.

**Die Gegenrichtung an der Domain-Grenze** blieb hier zunächst offen und ist
inzwischen erledigt — siehe unten.

### Default-Umstieg ✅ — und ein Test, der aufgehört hat zu prüfen, ohne es zu sagen

`StreamManagementEnabled` steht auf `true`. Der Grund für den ausgeschalteten
Vorgabewert — eine einmal fehlerhafte Zählung — ist seit dem Prosody-Lauf
weg.

**Der Schalter allein hätte gar nichts bewirkt.** `AXMPPTests.CreateClient`
setzte ihn hart auf `false` und überschrieb damit den Vorgabewert; die ganze
Sammlung wäre weiter ohne XEP-0198 gelaufen, und die Umstellung wäre
ungeprüft durchgegangen. Der Parameter ist deshalb jetzt `Boolean?`: `null`
heisst „den Vorgabewert stehen lassen". Erst damit läuft die Sammlung mit dem,
was ein Aufrufer ohne eigene Meinung bekommt.

Zwei Tests hingen daran, und der zweite ist der lehrreichere:

- `Disconnect_StopsKeepalive` wurde **rot**. Die Keepalive-Schleife wählt ihr
  Mittel nach Lage: mit XEP-0198 schickt sie ein `<r/>`, sonst einen
  XEP-0199-Ping. Der Test zählte Pings, und die kamen nicht mehr.
- `Reconnect_DoesNotAccumulateKeepaliveLoops` blieb **grün**. Es prüft eine
  Obergrenze, und „null Pings sind höchstens sieben Pings" trifft zu. Der Test
  hat aufgehört zu messen und nichts davon gesagt.

Beide laufen jetzt über beide Verfahren (`[TestCase(true/false)]`) und zählen,
was die Schleife tatsächlich schickt. Und der Obergrenze steht eine Untergrenze
gegenüber — ohne sie bestünde der Test auch dann, wenn gar kein Keepalive mehr
feuert, und genau das war er eine Zeitlang.

Der Vorgabewert selbst hat jetzt einen Test
(`StreamManagement_IsNegotiatedByDefault`), der beides prüft: den Wert und dass
er bis auf die Leitung durchschlägt. Ein Test nur auf die Eigenschaft bestünde
auch dann, wenn der Aufbau sie danach ignorierte.

Drei Mutationen, alle von genau den zuständigen Tests erschlagen: Vorgabewert
zurück auf `false`, Keepalive schickt unter XEP-0198 nichts mehr (tötet beide
Keepalive-Tests im SM-Fall — den zweiten nur wegen der neuen Untergrenze), und
`CreateClient` nagelt den Schalter wieder fest.

---

## Stream-Resume (XEP-0198 Abschnitt 5)

Zwei Schnitte, weil das `<resume/>` selbst in der Aufbauphase des Clients sitzt
— nach der Anmeldung, **vor** dem Resource Binding. Ohne einen Client, der es
schickt, führt kein Testweg dorthin: die Testbasis fährt echte
`XMPPClient`-Instanzen, und die binden immer. Ein zweiter, handgeschriebener
SASL-Client nur für diesen einen Test wäre Aufwand ohne Erkenntnis, denn R2
folgt unmittelbar.

### R1. Der Server hebt abgerissene Streams auf ✅

Der Teil, der ohne Rückkehrer prüfbar ist.

**Die Kennung war ratbar, und niemandem fiel es auf.** Die frühere Fassung
schickte `id='sm-{Verbindungsnummer}'` — eine kleine Zahl, die jeder Mitlesende
mitzählen kann. Ohne Wiederaufnahme war sie folgenlos: es gab nichts, was
sich damit übernehmen liesse. Mit ihr wäre sie ein Einfallstor geworden, denn
die Kennung ist das einzige Geheimnis, das einen Rückkehrer ausweist. Jetzt
kommt sie aus dem Zufallsgenerator, 128 Bit.

**Der eigentliche Eingriff sitzt dort, wo bisher bedingungslos abgemeldet
wurde.** Reisst die Verbindung, erzeugt der Server seit jeher eine Abmeldung im
Namen des Clients (RFC 6121, Abschnitt 4.5.2) — sonst führen die Kontakte die
Resource für immer als online. Wer wiederkommen darf, darf das nicht: die
Kontakte sähen ein Verschwinden, das gleich darauf zurückzunehmen wäre, und
zwischen den beiden Presences läge alles, was inzwischen an eine vermeintlich
abgemeldete Resource ging.

Also wird der Stream geparkt statt abgemeldet — und das verlangt sofort die
Gegenprobe: **eine aufgeschobene Abmeldung, die nie kommt, ist schlimmer als
eine zu frühe.** Sie fiele niemandem auf. Deshalb ein Durchgang im
Sekundentakt, der abgelaufene Streams abräumt und die Abmeldung nachholt, und
ein Test, der genau darauf wartet.

Dabei eine Falle, die nur beim Schreiben sichtbar wurde: der Abräumer ruft
dieselbe `AnnounceUnavailableAsync` auf, die vorne parkt. Ohne vorheriges
`EndResumption()` sieht sie wieder einen wiederaufnehmbaren Stream und parkt
ihn erneut — mit neuer Frist, für immer. Die Mutation, die diese Zeile
entfernt, tötet den Verfallstest.

Dazu der Puffer der noch nicht bestätigten Stanzas, aus dem nach einer
Wiederaufnahme nachzusenden wäre. Er wird nur bei zugesagter Wiederaufnahme
gefüllt — sonst wäre es ein Speicher, aus dem nie jemand liest — und leert sich
am `<a h='…'/>` des Clients, in derselben Modulo-Arithmetik wie auf der
Client-Seite.

Fünf Mutationen, jede von genau dem zuständigen Test erschlagen: nie parken,
Verfall ohne `EndResumption`, ungefragt zusagen, Kennung aus der
Verbindungsnummer, Puffer leert sich nicht.

### R2. Der Client kommt zurück ✅

Der Versuch sitzt genau zwischen Anmeldung und Binding. Gelingt er, gibt es
keine neue Resource — und keine zweite Presence, keinen zweiten Roster-Abruf,
keine erneute Aushandlung: eine wiederaufgenommene Sitzung ist keine neue.

**Der Manager muss den Reconnect überleben.** `InitialiseManagers()` erzeugte
ihn bei jedem Aufbau neu; an ihm hängen aber Kennung und unbestätigte Stanzas.
Er ist jetzt der einzige, der stehenbleibt — seinen Sitzungszustand setzt er
selbst zurück, sobald ein `<enabled/>` kommt.

**Die Kennung ist kein Ausweis, sondern eine Auswahl.** Sie wandert über die
Leitung; wer sie abfängt, hätte sonst eine fremde Sitzung samt Full-JID und
Roster, ohne je ein Passwort gesehen zu haben. Der Stream, auf dem das
`<resume/>` ankommt, muss deshalb bereits auf **dasselbe Konto** angemeldet
sein — ausgewiesen hat sich der Client vorher, über SASL.
`AStolenId_DoesNotHandOverTheStream` hält das fest.

**Drei Dinge, die erst der Lauf zeigte:**

1. *Ein sauberes `<close/>` darf nicht geparkt werden.* Fünf bestehende Tests
   fielen durch, und sie hatten recht: der Server hielt jede ordentliche
   Abmeldung für eine Störung, hob sie eine Minute lang auf und verschwieg sie
   den Kontakten so lange. XEP-0198 Abschnitt 5.3 gilt abgerissenen Streams,
   nicht verabschiedeten.

2. *Ein geparkter Stream muss weiter zustellbar sein.* `SessionsOf` filterte
   auf offene Verbindungen — was während der Störung ankam, wurde verworfen,
   statt in den Puffer zu gehen. Ohne diese Änderung rettete die Wiederaufnahme
   nur, was in der letzten Zehntelsekunde vor dem Abriss unterwegs war, und der
   eigentliche Fall — Verbindung weg, Nachrichten kommen trotzdem — fiele
   durch.

3. *`<enable/>` und `<enabled/>` gehören unter dieselbe Sperre.* Geht dazwischen
   eine Stanza hinaus, zählt der Server sie und der Client nicht — der setzt
   seinen Zähler erst beim `<enabled/>` zurück. Die Stände bleiben dann um
   genau eine auseinander, und der Puffer läuft nie mehr leer.

Und die Gegenprobe zum Nachsenden: das `h` im `<resumed/>` räumt die
Warteschlange des Clients bis zum Stand des Servers ab. Ohne das bekäme jeder
Empfänger nach jedem Abriss alles doppelt, was der Server längst hatte.

Sechs Mutationen, alle von genau den zuständigen Tests erschlagen: Kontoprüfung
weg, Manager bei jedem Aufbau neu, geparkter Stream nimmt nichts entgegen,
sauberes Verabschieden wird geparkt, Client nimmt nie wieder auf, und nach der
Wiederaufnahme läuft trotzdem der volle Aufbau.

**Zwei eigene Testfehler auf dem Weg**, beide von derselben Sorte — eine
Zusicherung, die mehr verlangt als der Test meint:

- Die Wartebedingung `IsConnected && ResumableStreamCount == 0` war schon
  erfüllt, während der Client noch mitten im Aufbau stand. Die Mutation
  „Manager bei jedem Aufbau neu" kam daran vorbei, weil die Zusicherungen den
  alten Manager lasen, bevor er ersetzt wurde. Jetzt wird auf den
  *abgeschlossenen* Aufbau gewartet.
- `AcknowledgedStanzas_LeaveTheBuffer` verlangte einen **leeren** Puffer,
  während Bobs XEP-0184-Empfangsbestätigungen weiter Einträge nachlegten. In
  etwa jedem dritten vollen Lauf falsch, allein ausgeführt nie. Gemeint war:
  was bestätigt wurde, liegt nicht mehr drin.

**Nicht abgedeckt** blieb hier zunächst eine Stanza, die der Client
erfolgreich abschickt und die den Server nie erreicht — inzwischen erledigt,
siehe R7.

### R3. Wiederaufnahme gegen Prosody ✅

Bis hierher war die Wiederaufnahme nur gegen den eigenen Server geprüft — beide
Seiten mit derselben Auffassung davon, wann ein `<resume/>` geschickt werden
darf, was hineingehört und was zurückkommt. Prosody hat diese Auffassung nicht
von uns.

Nötig war dafür zweierlei: ein **Abriss von unserer Seite** (`KillConnection()`,
das Gegenstück zu `XMPPSession.Kill()` — gegen eine fremde Gegenstelle lässt
sich die Sitzung nicht von drüben kappen, und ein ordentliches Abmelden ist
gerade das Gegenteil dessen, was zu prüfen ist) und ein **zweites Konto** auf
Prosody, sonst gibt es keinen Absender für eine Nachricht während der Störung.

Es lief auf Anhieb, und weil das verdächtig glatt war, erst ins Prosody-Log
statt es zu glauben. Dort steht der ganze Ablauf: `Session going into
hibernation (not being destroyed)`, unser `<resume previd='…' h='2'/>`,
`mod_smacks resuming existing session`, `<resumed previd='…' h='3'/>` und
`resending all unacked stanzas that are still queued after resume`.

**Zwei Mutationen, zwei zu schwache Tests — und beide Male dieselbe Ursache:**
die Zusicherung war auch ohne Wiederaufnahme erfüllt.

- *„Nie wiederaufnehmen"* liess `ProsodyHoldsBackWhatArrivedDuringTheOutage`
  bestehen. Prosody stellt die Nachricht auch dann zu, wenn der Client eine
  neue Resource bindet — sie geht dann eben dorthin. Dass sie ankommt, belegt
  die Wiederaufnahme nicht. Jetzt wird zusätzlich geprüft, dass es derselbe
  Stream war.
- *„resume='true' weglassen"* liess beide neuen Tests bestehen. Ohne Zusage ist
  die Kennung auf beiden Seiten `null`, und `null == null` heisst „unverändert".
  Beide prüfen jetzt zuerst, dass überhaupt zugesagt wurde.

Der Vergleich „vorher gleich nachher" ist nur dann ein Beleg, wenn *vorher*
etwas dastand. Das ist in dieser Sitzung der dritte Test, der grün war und
nichts gemessen hat.

### R4. Dieselbe Probe gegen ejabberd ✅ — und diesmal kein Fund

ejabberd bekommt einen `ejabberd_http_ws`-Handler auf 5443,
`mod_stream_mgmt` und zwei Konten. Die sieben Prüfungen sind nach
`AForeignPeerStreamManagementTests` gezogen — sie prüfen für jede Gegenstelle
dasselbe, und was sich unterscheidet, legen die Ableitungen in zwanzig Zeilen
fest. Ein dritter Server kostet damit fast nichts.

**Vierzehn von vierzehn, keine Abweichung.** Das ist ein anderes Ergebnis als
bei XEP-0288, wo ejabberd in den Stream-Features das Freischalt-Element statt
der Ankündigung schickte und wir sein Angebot deshalb übersahen. Bei
XEP-0198 stimmen beide Server in allem überein, was wir prüfen: Zählung des
Aufbaus, Nonzas, unser Empfangszähler, Zusage, Wiederaufnahme, Nachlieferung.

Das ist kein verschwendeter Lauf. Vorher war offen, ob unsere Wiederaufnahme
an Prosodys Auslegung hängt; jetzt ist es das nicht. Ein zweiter Zeuge, der
bestätigt, sagt weniger als einer, der widerspricht — aber er sagt etwas.

Zwei Unterschiede im Aufbau, beide banal und beide wären beim Festverdrahten
zur Falle geworden:

- Der WebSocket-Pfad heisst bei ejabberd `/websocket`, bei Prosody
  `/xmpp-websocket`. RFC 7395 schreibt keinen vor.
- `ejabberdctl register` geht über einen RPC-Aufruf in den laufenden Knoten
  und braucht ihn gestartet; `prosodyctl register` fasst die Dateien direkt an
  und will ihn *angehalten*. Genau verkehrt herum.

### R5. Der Namensraum in der Gegenrichtung ✅ — und er fehlte überall

Notiert war eine schmale Sache: was von einem fremden Server hereinkommt, steht
in `jabber:server` und wird unverändert an den lokalen Client weitergereicht.
Der zweite Test dazu hat gezeigt, dass es breiter liegt — **der Server hat
seinen Clients überhaupt nie einen Namensraum geschickt.** Bind-Antwort,
Carbons-Bestätigung, Roster, Presence: alles ohne.

Das ist derselbe Fehler wie der, den Prosody am Bind-IQ des Clients abgewiesen
hat, nur spiegelverkehrt. Über WebSocket gibt es kein umschliessendes
`<stream:stream>`, von dem eine Stanza ihren Namensraum erben könnte
(RFC 7395, Abschnitt 3.3.3); über die Domain-Grenze wechselt er von
`jabber:server` auf `jabber:client` (RFC 6120, Abschnitt 4.8.1).

Aufgefallen ist beides nie, und aus demselben Grund: unser Client erkennt
Stanzas am lokalen Namen und sieht den Namensraum gar nicht an. Diese
Nachsicht hat den Fehler auf der Client-Seite jahrelang verdeckt und hier
gleich noch einmal. Ein fremder Client wäre vermutlich strenger — und wir
erführen es erst von ihm.

Behoben in `XMPPSession.SendAsync`: die eine Stelle, durch die jeder Rahmen an
einen Client läuft, und aus demselben Grund gewählt wie beim Zählen. Nonzas
bleiben aussen vor; `<enabled/>` geht ohnehin an dieser Stelle vorbei, ist aber
auch keine Stanza.

Zwei Mutationen, beide von genau den zwei neuen Tests erschlagen: gar keinen
Namensraum setzen, und `jabber:server` statt `jabber:client`.

Der Lauf gegen Prosody und ejabberd blieb danach unverändert grün — die
Änderung betrifft nur, was unsere Clients von unserem Server bekommen.

### R6. Anbieten und Erbitten getrennt ✅ — und der Schluss aus P6 fällt

`UseBidirectionalStreams` steuerte beides zugleich: die Ankündigung auf
eingehenden Verbindungen und die Bitte auf ausgehenden. Das war nicht bloss
unscharf — es machte die eine Richtung **unbeobachtbar**. Solange unsere
ausgehende Verbindung die Rückrichtung nutzt, antwortet die Gegenstelle
darüber und wählt uns gar nicht erst an; es gab also keinen Zustand, in dem
sich unsere Ankündigung zeigen konnte.

Jetzt zwei Schalter, `OfferBidirectionalStreams` und
`RequestBidirectionalStreams`. Damit gibt es den Zustand „anbieten, nicht
erbitten", und damit den Test `ThePeerTakesTheReturnPathWeOffered`: zwei Pings,
weil es beim ersten die eingehende Verbindung noch nicht gibt, und
`BidirectionalDeliveryCount` als Beleg.

**Prosody nimmt an, ejabberd 24.12 nicht.** Genau die Abweichung, für die der
zweite Server da ist — und sie widerlegt, was in P6 hier stand. Dort hatte ich
aus ejabberds *master* geschlossen, seine aufbauende Seite suche die XEP-Form
`urn:xmpp:features:bidi`, und daraus, dass unsere Ankündigung genügt. Die
ausgelieferte 24.12 verhält sich anders: sie kündigt selbst `urn:xmpp:bidi` an
und sucht offenbar dasselbe.

Derselbe Fehler wie damals im Kleinen: aus dem Quelltext einer anderen Fassung
auf das Verhalten der laufenden geschlossen. Der Unterschied ist, dass es
diesmal auffiel, weil ein Test danach fragte.

Behoben durch **zwei** Ankündigungen. Auf dem Draht bleibt es eindeutig: das
Freischalt-Element heisst in beiden Lesarten `urn:xmpp:bidi`, es kommt also nur
eine Antwort zurück, und wer nur die XEP-Form kennt, übergeht das zweite
Element als unbekanntes Feature. Nach der Änderung nehmen beide Server die
Rückrichtung an.

Nebenbei hing eine dritte Sache am selben Schalter: ob wir eine bestehende
Rückrichtung überhaupt *benutzen*. Das gehört zum Anbieten und nicht zum
Erbitten — und ist jetzt ganz ohne Schalter, weil `BidiEnabled` beides schon
voraussetzt.

Zwei Mutationen, beide von `ThePeerTakesTheReturnPathWeOffered` erschlagen: nur
die XEP-Form ankündigen (ejabberd fällt aus), und das Anbieten wieder an den
Schalter für die ausgehende Seite hängen (beide fallen aus).

### R7. Die verlorene Stanza ✅ — ein Fall, den es im Prozess nicht gab

`ResendUnackedAsync` war seit R2 implementiert und ungeprüft, und der Grund war
kein Versäumnis, sondern ein Aufbauproblem: es gab keinen Weg, eine Stanza zu
erzeugen, die die Leitung erfolgreich verlässt und trotzdem nicht ankommt. Ein
abgerissener Socket lässt das Senden sofort und lautstark scheitern, und eine
nicht gesendete Stanza wird gar nicht erst mitgezählt — die Warteschlange
blieb also immer leer, und der ganze Zweig lief nie.

`XMPPServer.SwallowClientStanzas` stellt den Fall her: der Server nimmt den
Rahmen entgegen und wirft ihn weg, **bevor** er ihn aufzeichnet, zählt oder
weiterreicht. Für den Client sieht es aus wie ein geglücktes Senden, für den
Server, als sei nie etwas gekommen. Nonzas bleiben unangetastet — ohne sie
wären in diesem Zustand weder `<r/>` noch `<resume/>` möglich, und der Fall
wäre wieder nicht zu erreichen.

Der Schalter reiht sich in die bestehenden Fehlerfall-Schalter ein
(`CompleteCloseHandshake`, `RouteStanzas`, `AnswerAckRequests`,
`BroadcastPresence`) und ist derselbe Gedanke: manche Wege sind nur begehbar,
wenn der Server sich absichtlich schlecht benimmt.

Zwei Mutationen, beide von `StanzasLostInFlight_GoOutAgainAfterResumption`
erschlagen: gar nichts nachsenden, und beim Nachsenden erneut mitzählen. Die
zweite ist die, die in R2 ausdrücklich unerschlagen blieb — dort stand
vermerkt, dass für sie kein Testweg existiert. Jetzt gibt es einen.

Damit hat der ganze XEP-0198-Strang keine ungeprüfte Zeile mehr.

### D1. Der SASL-Downgrade ✅ — nie schwächer als beim letzten Mal

Der Client nahm den stärksten angebotenen Mechanismus. Das ist richtig, solange
die Ankündigung von dem kommt, der sie zu machen hat — nur ist sie nicht
authentifiziert. Sie kommt zwar über TLS, aber TLS belegt nur, dass die
Gegenstelle ein Zertifikat einer vertrauten CA hat, und der klassische
Zwischenmann hat eines. Wer allein der Ankündigung folgt, folgt damit auch dem,
der sie gefälscht hat: Aus den Features verschwinden die SCRAM-Angebote, übrig
bleibt PLAIN, und der Client schickt bereitwillig das Passwort selbst statt
eines Beweises, dass er es kennt. Dieselbe Bewegung wie beim STARTTLS-Downgrade
aus S4b-6, eine Schicht höher.

`SaslMechanismPolicy` hält zwei Untergrenzen, die dieselbe Prüfung durchlaufen:
`Minimum`, was der Aufrufer verlangt, und `Pinned`, womit die letzte Anmeldung
gelang. Die erste wirkt vom ersten Rahmen an und muss gesetzt werden, die
zweite wirkt von selbst und erst ab der zweiten Verbindung.

Zwei Stellen entscheiden über den Wert des Ganzen, und beide sind
Reihenfolgefragen:

- **Geprüft wird vor dem `<auth/>`, nicht nach der Antwort.** Bei PLAIN steht
  das Passwort in genau diesem Rahmen. Wer das Downgrade erst an der Antwort
  bemerkt, hat es dem Zwischenmann schon gegeben, und die Anmeldung danach
  abzubrechen nimmt es ihm nicht wieder ab.
- **Angeheftet wird nach der Anmeldung, nicht davor.** Ein Fehlschlag sagt
  nichts darüber, was dieser Server kann.

Dass die Anheftung ein Trust-On-First-Use ist, bleibt: Steht der Zwischenmann
schon beim allerersten Aufbau dazwischen, heftet sie sein Downgrade an. Nur ist
das nicht der Angriff, der sich lohnt. Der Client kommt nach jedem Abriss von
allein wieder, und ein Abriss lässt sich erzwingen — es genügt also, die
Verbindung zu stören und die *zweite* Anmeldung abzufangen. Genau die ist jetzt
gedeckt, ohne dass irgendwer irgendetwas konfiguriert.

Der Testserver spielt den Zwischenmann, indem er `OfferedSaslMechanisms`
zwischen den beiden Verbindungen ändert.

Sieben Mutationen, alle erschlagen:

| Mutation | Erschlagen von |
|---|---|
| `Minimum` nicht prüfen | `TheMinimumHoldsOnTheVeryFirstConnect`, `Minimum_HoldsWithoutAnyPreviousLogin` |
| `Pinned` nicht prüfen | `AWeakerServerOnTheSecondConnect_IsRefused`, `TheRefusalHappensBeforeThePasswordGoesOut`, `Pinned_RefusesTheWeakerAndAllowsTheStronger` |
| gar nichts anheften | sechs Tests |
| `Strongest` nimmt den ersten bekannten statt den stärksten | `Strongest_ReadsTheRankingAndNotTheOrder`, `AStrongerServerOnTheSecondConnect_IsAccepted` |
| Anheftung auf Gleichheit statt auf Stärke prüfen | `AStrongerServerOnTheSecondConnect_IsAccepted`, `Pinned_RefusesTheWeakerAndAllowsTheStronger` |
| Prüfung hinter den SASL-Austausch schieben | `TheRefusalHappensBeforeThePasswordGoesOut` und zwei weitere |
| Setzer nimmt einen unbekannten Mechanismusnamen an | `AnUnknownMinimum_IsRefusedAtTheSetter`, `Minimum_RefusesAnUnknownName` |

Die vierte ist die, die kein Integrationstest hätte finden können: Der
Testserver kündigt vom stärksten zum schwächsten an, und dort sieht „nimm den
ersten" genauso aus wie „nimm den stärksten". Sichtbar wird der Unterschied
erst, wenn ein Server nachrüstet und den neuen Mechanismus hinten anhängt —
was `AStrongerServerOnTheSecondConnect_IsAccepted` nachstellt, aber erst,
nachdem der Unit-Test danach gefragt hatte.

Die letzte ist die stillste: Ein unbekannter Name hat die Stärke 0, und eine
Untergrenze von 0 verlangt gar nichts. Ein Tippfehler in
`MinimumSaslMechanism` hätte lautlos das Gegenteil dessen bewirkt, was der
Aufrufer hinschrieb — deshalb weist der Setzer ihn ab, statt ihn zu nehmen.

Nicht erschlagen, und zwar nachweislich unerreichbar: das Anheften *vor* die
Anmeldung zu ziehen. Es bräuchte eine gescheiterte Anmeldung, der eine weitere
folgt — aber jeder Authentifizierungsfehler unterdrückt den Reconnect, und das
Passwort lässt sich nach dem Erzeugen der Verbindung nicht mehr ändern. Der
angeheftete Wert wäre ohnehin derselbe, den `EnsureAcceptable` gerade
durchgelassen hat.

### D2. Der vergiftbare Caps-Cache ✅ — ein Hash, der erzeugt, aber nie geprüft wurde

`ver` ist keine Kennung, die eine Entity sich aussucht, sondern der Hash über
das, was sie auf disco#info antwortet. Dieser Client erzeugte ihn seit jeher
korrekt — gegen den Testvektor aus XEP-0115 §5.2 belegt — und rechnete ihn bei
fremden Antworten kein einziges Mal nach.

Damit war der Cache von jedem vergiftbar, dessen Presence hier ankommt. Die
Bewegung ist kurz: Der Angreifer kündigt in seiner Presence das
`node#ver`-Paar eines verbreiteten Clients an und antwortet auf die folgende
Abfrage mit einer Liste seiner Wahl. Unter diesem Paar liegt fortan seine
Liste — und ausgeliefert wird sie an jeden weiteren Kontakt, der dasselbe Paar
ankündigt, ohne dass der je gefragt würde. Der Angreifer bestimmt damit, was
dieser Client über Dritte glaubt: welche Verschlüsselung sie können, ob sie
Empfangsbestätigungen verstehen, was sich ihnen schicken lässt.

Die Rechnung lag schon da, nur nicht erreichbar: `CalculateVerificationString`
las fest aus `LocalIdentities`/`LocalFeatures`. Sie ist jetzt als
`VerificationString(identities, features)` über beliebige Angaben anwendbar —
mehr brauchte es nicht, um aus einem erzeugten Wert einen geprüften zu machen.

Drei Gründe führen dazu, dass ein Eintrag nicht abgelegt wird, und sie sind
nicht dasselbe:

| Grund | Was er bedeutet |
|---|---|
| Kein `hash`-Attribut | Altform vor XEP-0115 1.4; `ver` ist dort eine Versionsnummer und gar kein Hash |
| Unbekannter Algorithmus | Nachrechnen lässt sich nur `sha-1` |
| Datenformular in der Antwort | XEP-0128 geht in den `ver`-Wert ein, diese Rechnung kennt es noch nicht |
| Hash passt nicht | Die Fälschung |

Nur der letzte ist ein Angriff. Die anderen drei sind Unvermögen — eigenes oder
das der Gegenstelle —, und der Unterschied gehört ins Protokoll: Über
`OnCapsRejected` geht der Grund im Klartext hinaus. Gemeldet wird die Antwort
in allen vier Fällen trotzdem über `OnCapsDiscovered`; sie ist das, was diese
Entity über sich sagt, und genau das ergäbe auch eine gewöhnliche
disco#info-Abfrage. Verweigert wird nur das Bündeln.

Neun Mutationen, alle erschlagen. Zwei davon sind die, um die es geht:

- **Warnen und trotzdem ablegen** — der klassische halbe Fix. Fünf Tests fallen
  aus, weil `GetCachedInfo` den Eintrag findet.
- **Die Aufrufstelle lässt das `hash`-Attribut fallen.** Erschlagen allein von
  `CapsOfARealContact_AreVerifiedAndCached`. Ohne diesen Test hätte diese
  Mutation überlebt, und mit ihr wäre der Cache dauerhaft leer geblieben, ohne
  dass irgendetwas rot geworden wäre — die Prüfung hätte weiter funktioniert,
  nur eben immer mit dem Ergebnis „nicht prüfbar". Der Test war eigens gegen
  diese Lücke geschrieben und belegt nebenbei, dass unser eigenes `ver` zu
  unserer eigenen disco#info-Antwort passt.

Eine Mutation überlebte zunächst und deckte dabei etwas auf: die Prüfung auf
ein fehlendes `hash`-Attribut ist für die Entscheidung redundant — `null` ist
ohnehin nicht `sha-1`, der nächste Zweig fängt sie also mit. Sie trägt allein
die genauere Begründung. Damit stand die Wahl, sie zu streichen oder die
Begründung zu prüfen; der Test prüft sie jetzt. Ein Zweig, dessen einziger
Zweck eine Aussage ist, muss über diese Aussage abgesichert sein — sonst ist er
Zierde.

### D3. Der Verification String, vollständig ✅ — und vier Regeln, die nichts prüften

D2 machte aus einem erzeugten Hash einen geprüften. Damit wurde erst sichtbar,
was an der Rechnung fehlte: Sie ging über Identitäten und Features, und
XEP-0115 §5.1 lässt noch zwei Dinge einfliessen — das `xml:lang` einer
Identität und die XEP-0128-Datenformulare. Beides fiel vorher nie auf, weil ein
Wert, den niemand nachrechnet, auch nicht falsch sein kann. Nach D2 war die
Folge konkret: Jede Gegenstelle, die ihren Namen in einer Sprache führt oder
ihre Software-Angaben veröffentlicht, wurde abgelehnt — nicht als Fälscher,
aber eben auch nicht geglaubt.

Beides ist jetzt drin, und der Beweis dafür ist nicht selbstgemacht: XEP-0115
§5.3 druckt genau dafür einen zweiten Vektor ab („Complex Generation Example",
zwei Identitäten, die sich nur in `xml:lang` und Name unterscheiden, plus ein
softwareinfo-Formular mit einem mehrwertigen Feld). Er wird exakt reproduziert,
und wie beim einfachen Vektor prüft ein zweiter Test, dass der abgedruckte
`ver`-Wert wirklich der SHA-1-Hash des abgedruckten S-Strings ist.

Dazu kommen die drei Ungültigkeitsregeln aus §5.4: dieselbe Identität zweimal,
dasselbe Feature zweimal, zwei Formulare mit demselben `FORM_TYPE` oder ein
`FORM_TYPE` mit mehreren Werten. Das ist keine Formstrenge. Der Verification
String entsteht dadurch, dass eine Antwort in *genau eine* Zeichenkette
überführt wird; wo Doppelungen stehen, gibt es mehr als eine — und damit lässt
sich zu einem gegebenen Hash eine zweite Antwort bauen. Der mehrwertige
`FORM_TYPE` ist der deutlichste Fall: Das Feld selbst wird nicht mit angehängt,
der zweite Wert verschwindet also spurlos aus der Rechnung.

Vierzehn Mutationen. Zehn fielen sofort. **Die vier Regeln aus §5.4 überlebten
alle vier** — und der Grund ist die Sorte Selbsttäuschung, für die dieses
Verfahren da ist: Mein Test kündigte einen `ver`-Wert an, zu dem die
mehrdeutige Antwort ohnehin nicht passte. Also erschlug sie schon der
Hash-Vergleich, und die Regeln, um die es ging, liefen nie. Der Test kündigt
jetzt den Wert an, den die mehrdeutige Antwort *wirklich* ergibt — womit nur
noch diese Regeln sie aufhalten können. Danach fielen alle vier.

Ein Test, der einen Angriff nachstellt, muss den Angriff auch gelingen lassen
bis zu der Stelle, die ihn abfangen soll. Sonst prüft er den Wachposten davor.

Und einer, der ohne Mutationsdurchgang gar nicht entstanden wäre:
`RespondInfoAsync` gibt das `xml:lang` einer Identität aus — geprüft hat das
nichts, weil die eigene Identität keines trägt. Der Weg dorthin führt über zwei
Dateien: Ankündigung und Antwort. Stimmen sie nicht überein, ist dieser Client
für jeden, der nach §5.4 prüft, ein Lügner.
`AnIdentityWithXmlLang_SurvivesTheRoundTrip` lässt beide gegeneinander laufen.

### D4. SASLprep, vollständig ✅ — Tabellen, die man nicht abschreibt

Die Vorbereitung von Benutzername und Passwort bestand aus einer Zeile: NFKC.
Das ist einer von vier Schritten. Es fehlten die Abbildungen (ein weiches
Trennzeichen im Passwort blieb stehen, statt zu verschwinden), die
Verbotstabellen (ein Steuerzeichen ging durch) und die Bidi-Prüfung ganz.

Die Folge war nicht, dass jemand hereinkam, der nicht sollte, sondern das
Gegenteil: Ein Passwort ausserhalb von ASCII wurde hier anders vorbereitet als
bei Prosody oder ejabberd, und die Anmeldung scheiterte, ohne dass jemand hätte
sagen können warum. Dasselbe getippte Passwort, zwei verschiedene Schlüssel.

Dazu kam eine zweite Fassung derselben Kurzfassung: Client (`SCRAMAuthenticator`)
und Server (`XMPPCredentials`) normalisierten jeder für sich. Zwei Kopien
desselben Verfahrens sind zwei Gelegenheiten auseinanderzulaufen; jetzt ist es
eine.

**Die Tabellen sind nicht abgeschrieben, sondern erzeugt.** RFC 3454 führt rund
neunhundert Codepoint-Bereiche, davon allein 396 für die in Unicode 3.2 nicht
zugewiesenen und 360 für die linksläufigen Zeichen. Ein Tippfehler darin wäre
praktisch nicht zu finden — er fällt erst auf, wenn ein bestimmtes Zeichen in
einem Passwort vorkommt, und dann als Anmeldung, die grundlos scheitert.
`tools/stringprep/generate.py` liest den RFC und schreibt
`Jabber/Auth/StringPrepTables.cs`; wer die Tabellen anzweifelt, lässt ihn
laufen und vergleicht.

Dass die Tabellen auf Unicode 3.2 festgeschrieben sind, ist dabei kein
Rückstand, sondern der Sinn der Sache — und
`UnassignedCodePoints_AreRefused` belegt es an U+0221, den .NET längst als
lateinischen Kleinbuchstaben kennt und RFC 3454 nicht.

Elf Mutationen, zehn sofort erschlagen. Die elfte ist die lehrreiche: **der
Client darf PLAIN unvorbereitet schicken, und alles blieb grün.** Der Grund ist,
dass der Server vorbereitet, was bei ihm ankommt — die Anmeldung gelingt also
so oder so, und mein Test sah nur auf sie. Gedeckt war damit die Server-Hälfte,
nicht die des Clients. Gegen einen Server, der sich auf die Vorbereitung des
Clients verlässt, wären wir aufgelaufen, ohne dass ein Test es gemerkt hätte.
Jetzt prüft der Test, was auf der Leitung steht, statt was am Ende dabei
herauskommt.

Zweimal in Folge derselbe Fehler in meinen eigenen Tests: in D3 liess ich den
Angriff zu früh scheitern, hier den Nachweis über die falsche Hälfte laufen.
Beides sind Tests, die auf das Ergebnis sehen statt auf den Weg — und beide
hätten ohne Mutationsdurchgang bestanden.

Am Rande, aber nicht nebensächlich: `Verify` fängt eine misslungene
Vorbereitung ab und meldet einen Fehlversuch. Was in einem
PLAIN-`<auth/>` steht, bestimmt die Gegenstelle; ein Steuerzeichen darin darf
nicht den Server umwerfen.

### D5. JIDs nach RFC 7622 ✅ — und die Nachricht auf dem falschen Gerät

Der Vergleich zweier JIDs lief überall über `OrdinalIgnoreCase` auf der ganzen
Zeichenkette. Nach RFC 7622, Abschnitt 3.4 sind aber nur Local- und Domainpart
von der Schreibweise unabhängig, der Resourcepart nicht.

Der Fehler war nicht theoretisch, und er hatte eine hässliche Form. Die
Resource-Vergabe im Server hat immer schon ordinal verglichen — `Handy` und
`handy` waren für sie zwei verschiedene Geräte, und die zweite Anmeldung kam
deshalb durch statt als Konflikt abgewiesen zu werden. Nur das *Nachschlagen*
einer Sitzung tat es nicht. Der Server nahm also zwei Geräte an und stellte
danach beiden den Verkehr desselben zu: Die Nachricht landete auf dem falschen,
und beim Absender sah alles nach Erfolg aus.

`JidUtilities` ist jetzt eine Umsetzung von RFC 7622 statt einer Zeile
`ToLowerInvariant`: zerlegen in der Reihenfolge aus Abschnitt 3.2, jeden Teil
nach seinem PRECIS-Profil vorbereiten, Höchstlängen in Oktetten, vergleichen
Teil für Teil. Geprüft gegen beide Beispieltabellen aus Abschnitt 3.5.

Die Klassenzugehörigkeit eines Codepoints ist angenähert — aus Unicode-Kategorie
und Kompatibilitätszerlegung statt aus den abgeleiteten Eigenschaften nach
RFC 8264. Das ist im README benannt, samt dem, was dadurch aussen vor bleibt.

Eine Abweichung ist bewusst und hat einen eigenen Test, damit sie eine Stelle
hat, an der sie auffällt: Beispiel 18 (führendes Leerzeichen im Resourcepart)
wird angenommen. Die Tabelle führt es als Nicht-JID, aber im Regelteil steht
nichts dergleichen — das OpaqueString-Profil lässt Leerzeichen zu. Für einen
Router ist Annehmen die vorsichtigere Wahl: Eine Adresse zurückzuweisen, die
andere Server für gültig halten, verliert Nachrichten, und zwar unsere.

Zwölf Mutationen, neun sofort erschlagen. **Drei überlebten, und alle drei aus
demselben Grund: Der Testfall traf schon eine frühere Regel.**

| Mutation | Warum sie zuerst überlebte |
|---|---|
| Am letzten statt am ersten `/` trennen | Mein Beispiel hatte nur einen Schrägstrich |
| Kompatibilitätszeichen im Localpart zulassen | Die römische Vier fällt schon über ihre Kategorie |
| Leeren Resourcepart zulassen | Beispiel 19 hat *beide* Teile leer; der Localpart wird zuerst geprüft |

Das ist dieselbe Sorte Selbsttäuschung wie in D3 und D4, jetzt zum dritten Mal
und in drei Ausprägungen gleichzeitig. Der gemeinsame Nenner ist inzwischen
klar zu benennen: **Ein Beispiel aus einer Spezifikation ist noch kein Test.**
Die Tabellen sind zum Vorführen gebaut, nicht zum Trennen — eine Zeile darf
gern gegen drei Regeln zugleich verstossen. Ein Test, der eine bestimmte Regel
absichern soll, braucht einen Fall, der genau *diese* eine verletzt.

Behoben mit `juliet@example.com/foo/bar` (der Fall, den Abschnitt 3.4 selbst
nennt), der Ligatur ﬁ (ein Kleinbuchstabe, der kompatibel in „fi" zerfällt) und
den beiden leeren Teilen je für sich.

### D6. Die eigenen erweiterten Angaben ✅ — und ein Fenster, das der Test selbst aufriss

Fremde XEP-0128-Formulare las dieser Client seit D3; eigene lieferte er keine.
`DiscoManager.LocalForms` schliesst die Lücke, und `DiscoForm.SoftwareInfo`
baut den üblichen Fall aus XEP-0232.

Zwei Dinge daran sind Entscheidungen und keine Selbstverständlichkeiten:

- **Die Liste fängt leer an.** Was dort steht, erfährt jeder Kontakt ungefragt,
  und Software, Fassung und Betriebssystem sind genau die Angaben, aus denen
  sich ein Gerät wiedererkennen lässt. Eine Vorgabe, die etwas veröffentlicht,
  wäre eine Vorgabe gegen den Nutzer. `WithoutOwnForms_NothingIsAnnounced` hält
  das fest.
- **Nicht Angegebenes wird kein leeres Feld.** „Ich sage nichts über mein
  Betriebssystem" und „mein Betriebssystem heisst Leerstring" sind zwei
  verschiedene Aussagen; nur die erste ist gemeint, und die zweite ergäbe einen
  anderen Hash.

Sieben Mutationen. Sechs fielen sofort — darunter beide Hälften, die
zusammengehören: Formular nicht in die Antwort (der Hash stimmt dann nicht) und
Formular nicht in den Hash (die Antwort stimmt dann nicht). Beide Male wären
wir für jede prüfende Gegenstelle ein Fälscher gewesen, bei völlig ehrlicher
Auskunft.

Die siebte überlebte zunächst und ist wieder dieselbe Sorte: „nicht angegeben
wird zu leerem Feld", angewandt auf `software` — ein Feld, das mein Test immer
füllte. Geprüft war die Regel nur an dem einen Feld, das ich weggelassen hatte.
Jetzt prüft ein Test alle vier einzeln.

**Und ein Fund, der nicht aus dem Mutationsdurchgang kam, sondern aus einem
einzelnen roten Lauf:** `OwnDataForm_SurvivesTheRoundTrip` schlug einmal unter
etwa fünf Läufen fehl. Die Ursache ist kein Fehler im Code, sondern eine
Eigenschaft des Protokolls, die der Test selbst herbeigeführt hat. Alice ändert
ihre Auskunft nach dem Verbinden und schickt eine neue Presence; zwischen
beidem liegt ein Fenster, in dem der alte `ver`-Wert angekündigt ist und schon
die neue Antwort gegeben würde. Wer darin fragt, bekommt zu Recht eine
Abweichung gemeldet — genau das, was D2 eingebaut hat.

Der Test wartet jetzt, bis die neue Presence beim Server steht, bevor Bob
dazukommt. Derselbe Fenstergriff steckte seit D3 in
`AnIdentityWithXmlLang_SurvivesTheRoundTrip`, ohne je aufzufallen; er ist
mitbehoben.

Das ist der Nachtrag zu der Regel aus D5: Ein Testaufbau kann eine Lage
herstellen, die es im gemeinten Ablauf gar nicht gibt — und dann ist nicht der
Code zu ändern, sondern der Aufbau.

### D7. Roster-Versionierung ✅ — und zwei Tests, die sich selbst betrogen

RFC 6121, Abschnitt 2.6: Der Client nennt die Fassung, die er
zwischengespeichert hat, und bekommt ein leeres Ergebnis, wenn sie noch
stimmt. Der Roster ist das Grösste, was beim Anmelden über die Leitung geht,
und er ändert sich selten.

Die Fassung ist **gerechnet, nicht gezählt** — ein Streuwert über den Inhalt.
Ein Zähler müsste mit dem Konto gespeichert werden und überstünde einen
Neustart nur, wenn jemand daran denkt; der Streuwert braucht keinen Speicher
und bleibt auch dann richtig, wenn jemand den Roster an der Datei vorbei
ändert. Er hat zudem eine Eigenschaft, die ein Zähler nicht hat: Geht der
Roster von A nach B und zurück nach A, ist die Fassung wieder die alte — und
das ist richtig, denn der Zwischenstand des Clients stimmt ja wieder.

Alles hängt an einer Feinheit, die leicht falsch herauskommt: „unverändert" ist
ein Ergebnis **ganz ohne** `<query/>`. Ein `<query/>` ohne Kinder heisst
dagegen „dein Roster ist leer". Wer beides verwechselt, löscht dem Nutzer die
Kontaktliste — die Mutation, die genau das tut, steht als M2 in der Liste.

**Ein Fund noch vor dem Mutationsdurchgang.** Der erste Testlauf war rot, weil
der Server das `ver` mit `Attr` las — und `Attr` ist auf das Wurzelelement
verankert. Das Attribut sitzt aber am `<query/>`, nicht am `<iq/>`. Die Prüfung
sah vollkommen richtig aus und las immer `null`; ohne den Test wäre die
Versionierung serverseitig wirkungslos geblieben, ohne dass irgendetwas
aufgefallen wäre. Jetzt gibt es `QueryAttr`, und der Kommentar dort nennt die
Falle beim Namen.

Dreizehn Mutationen, alle erschlagen — Ankündigung, leeres Ergebnis, Fassung im
Ergebnis, Fassung im Push, Übernahme auf beiden Wegen, der Verzicht ohne
Ankündigung, und je ein Feld, das aus der Rechnung fällt.

**Zwei Tests haben sich dabei selbst betrogen, und der zweite Betrug war meine
Reparatur des ersten.**

`ARosterPush_CarriesTheNewVersion` schlug unter Volllast gelegentlich fehl.
Ursache: `AddContactAsync` ist zweierlei — ein Roster-Set und ein
`subscribe` —, also kommen *zwei* Pushes. Der Test hielt beim ersten an und
verglich dann gegen einen Serverstand, der schon weitergelaufen war.

Meine erste Reparatur — auf Übereinstimmung warten statt auf Änderung — war
schlimmer als der Fehler: Am Anfang stehen beide Seiten beim leeren Roster,
sind also bereits einig. Die Wartebedingung war erfüllt, bevor irgendetwas
geschehen war, und der Test schlug danach *immer* fehl. Richtig ist beides
zusammen: geändert **und** einig.

Das ist die dritte Ausprägung derselben Sache in vier Commits, und sie hat
jetzt einen Namen verdient: **Eine Wartebedingung, die der Anfangszustand schon
erfüllt, wartet nicht.**

Nebenbei aufgefallen und nicht behoben: Ein voller Roster wird in den
zwischengespeicherten hineingemischt statt ihn zu ersetzen. Ein Kontakt, der
bei abgemeldetem Client entfernt wurde, bleibt damit stehen. Das ist ein
eigener Fehler mit eigener Gegenprobe und steht unter „Später".

### D8. Der Kontakt, den man nicht loswird ✅

Aufgefallen bei D7, jetzt behoben: Das Ergebnis einer Roster-Anfrage wurde in
den zwischengespeicherten Roster *hineingemischt*. Es ist aber der Stand und
keine Ergänzung (RFC 6121, Abschnitt 2.1.4) — was nicht darin steht, gibt es
nicht mehr.

Der Weg zum Schaden ist alltäglich: Ein Kontakt wird an einem anderen Gerät
gelöscht, während dieses hier abgemeldet ist. Beim nächsten Anmelden schickt
der Server ihn nicht mehr — und niemand nimmt ihn heraus. Er kommt zurück und
lässt sich von diesem Gerät aus nicht mehr entfernen: Ein Löschversuch erzeugt
einen Push mit `subscription='remove'`, der Eintrag verschwindet, und beim
übernächsten Anmelden ist er wieder da. Im laufenden Betrieb fällt nichts auf,
weil dort immer der Push kommt.

`Roster.ReplaceAll` heisst so, wie es sich verhält, und wird ausschliesslich
für das Ergebnis gerufen — nie für einen Push. Das ist der Punkt, an dem die
Sache kippen könnte: Auf dem Draht sehen beide gleich aus, ein `<query/>` mit
`<item/>`. Wer den Push genauso behandelte, löschte bei *jeder* Änderung den
gesamten übrigen Roster. Dafür steht `ARosterPush_DoesNotReplaceTheWholeRoster`
da, und die zugehörige Mutation M5 ist die einzige, die genau diesen einen Test
umwirft.

Fünf Mutationen, alle erschlagen.

Zwei Anläufe brauchte der Push-Test allerdings, und beide Male lag es daran,
dass ich die Änderung an der falschen Stelle auslöste: Ein Eingriff am Konto
vorbei (`SetRosterEntry`) erzeugt keinen Push, ein `AddContactAsync` erzeugt
gleich zwei. Gebraucht wird genau ein Roster-Set vom Client — dann kommt genau
ein Push mit genau einem Element. Auch das gehört zu der Regel aus D5: Der
Aufbau muss die Lage herstellen, die geprüft werden soll, und nicht eine, die
ihr ähnlich sieht.

**Ein bestehender Test musste dafür angefasst werden**, und das ist erwähnenswert,
weil es nach einer bequemen Anpassung aussieht. `RosterPushAfterBind_IsApplied`
liess den Server in der Aufbauphase einen Kontakt pushen, den sein eigener
Roster nicht enthielt. Danach kommt das Ergebnis, und das Ergebnis ist der
Stand — der Kontakt verschwand also wieder, und der Test wurde rot.

Nicht der Test war zu streng, sondern sein Server war unmöglich: XMPP liefert
auf einem Stream der Reihe nach, ein Push *vor* dem Ergebnis ist damit älter
als das Ergebnis. Ein Server, der einen Eintrag ankündigt, den er selbst nicht
führt, widerspricht sich. Der Kontakt steht jetzt auch im Roster des Kontos;
geprüft wird weiterhin dasselbe, nämlich dass eine Stanza aus der Aufbauphase
nicht verlorengeht.

Aufgefallen ist das erst im vollen Durchlauf — die gefilterten Läufe während
der Arbeit enthielten diesen Test nicht. Drei volle Läufe hintereinander,
jeder mit demselben Fehlschlag: kein Flackern, sondern ein Regressionsfehler,
und ohne den vollen Durchlauf wäre er mitgegangen.

### D9. Das Nachsenden fragt nach ✅ — und die Notlösung aus D7 fällt weg

In D7 wurde `TheResumedCountPreventsADoubleDelivery` einmal unter Volllast rot.
Ich habe damals die Wartezeit verlängert und offen vermerkt, dass die Ursache
nicht gefunden ist. Sie ist es jetzt, und die Wartezeit war die falsche
Antwort.

`ResendUnackedAsync` schickt nach einer Wiederaufnahme alles Offene noch einmal
hinaus — und fragte danach nach nichts. Das `<resumed h='…'/>` hat die
Warteschlange nur bis zum Stand des Servers geleert; was darüber hinaus offen
war, wartete auf ein `<a/>`, das von selbst nie kam. Der Server bestätigt, wenn
er gefragt wird, und gefragt hat allein der Keepalive.

Damit gab es zwei Fassungen desselben Fehlers:

- **Keepalive an** (die Vorgabe): Die Warteschlange bleibt bis zum nächsten
  `<r/>` stehen, also bis zu 25 Sekunden. Ärgerlich, aber begrenzt.
- **Keepalive aus**: Sie bleibt für immer stehen. Und bei jeder weiteren
  Wiederaufnahme geht alles darin noch einmal hinaus.

Warum das nur unter Last auffiel: Ob nach dem `<resumed/>` überhaupt etwas
offen bleibt, hängt davon ab, ob der Server beim Abriss schon alles verarbeitet
hatte. Bei ruhiger Maschine hatte er das — und die Warteschlange war ohne
Zutun leer.

**Der Test, der es hätte zeigen können, hat es verdeckt.** In R7 stand in
`StanzasLostInFlight_GoOutAgainAfterResumption` ein `RequestAckAsync` von Hand.
Ich hatte es dort hingeschrieben, weil die Warteschlange sonst nicht leer wurde
— und genau das war der Befund, den ich als Testbedarf gelesen habe statt als
Fehler. Der Aufruf ist weg; ohne die Korrektur ist der Test rot, mit ihr grün.

Zwei Mutationen, beide von diesem Test erschlagen: gar nicht nachfragen, und
vor dem Nachsenden fragen statt danach. Die zweite ist die feinere — ein `<r/>`
vor den nachgesendeten Stanzas holt eine Bestätigung über den Stand *davor*,
und die Warteschlange bleibt genauso stehen.

Die verlängerte Frist aus D7 steht wieder auf dem Vorgabewert.

Die Lehre ist unbequemer als die aus D3 bis D5. Dort haben Tests etwas nicht
gemessen; hier hat ein Test einen echten Fehler **umschifft**, und ich habe die
Umschiffung selbst geschrieben und im Commit sogar begründet. Wenn ein Test
eine Handreichung braucht, damit er durchläuft, ist die erste Frage nicht, wie
man sie am besten formuliert, sondern warum er sie braucht.

**Ein zweiter, davon unabhängiger Wettlauf** kam bei der Prüfung ans Licht und
ist mitbehoben: `TheClientResumesInsteadOfBindingAnew` wurde in einem von vier
vollen Läufen rot, mit der Meldung „der Stream wurde neu ausgehandelt". Das
traf zu und sagte über den geprüften Code nichts. Ursache ist die Reihenfolge
zwischen zwei Uhren: Der Client kommt nach seiner Reconnect-Frist wieder, der
Server legt die abgerissene Sitzung in seinem eigenen Takt ab. Ist er noch
nicht so weit, findet das `<resume/>` nichts vor und der Client bindet neu —
richtig gehandelt, nur nicht das, was der Test prüfen wollte.

`KillAndAwaitParked` wartet jetzt an den drei Stellen, an denen eine gelungene
Wiederaufnahme die Voraussetzung ist, auf `ResumableStreamCount > 0`. Schlägt
es doch fehl, steht der Grund in der Meldung.

Ob dieser Wettlauf schon vorher bestand, ist offen: Vier Läufe auf dem Stand
von D8 blieben grün, vier mit der Korrektur ergaben einen roten — bei einem
einzigen Ereignis lässt sich das nicht auseinanderhalten. Ein Zusammenhang mit
dem zusätzlichen `<r/>` ist nicht zu erkennen (es wird als Nonza nicht
mitgezählt und geht erst nach dem Start der Empfangsschleife hinaus),
ausgeschlossen ist er damit aber nicht.

### D10. Warten auf ein Ereignis statt auf Stille ✅

`IqWithoutId_IsNotAnswered` war der dritte wackelige Test dieser Reihe und der
einzige, dessen Wackeln von Anfang an in der Anlage steckte: Er wartete eine
Sekunde darauf, dass die Zahl der vom Client empfangenen Rahmen *überhaupt*
nicht steigt. Damit zählte er alles mit, was mit dem geprüften IQ nichts zu tun
hatte — jeden Aufbaurahmen, der noch unterwegs war —, und unter Last war
irgendwann einer darunter.

Ein negativer Nachweis braucht keine Wartezeit, wenn es ein Ereignis gibt, an
dem man ihn festmachen kann. Auf einem Stream wird der Reihe nach verarbeitet:
Nach dem IQ ohne `id` geht jetzt ein zweites hinaus, das beantwortet werden
*muss*. Ist dessen Antwort da, hat der Client das erste bereits in der Hand
gehabt und sich entschieden — und dann genügt die Feststellung, dass kein
`type='error'` dabei ist. Kein `WaitUntil` über eine Sekunde, keine
Lastabhängigkeit, und der Test ist nebenbei schneller.

Eine Mutation, erschlagen: die Prüfung auf das fehlende `id` fallen lassen und
mit `id=''` antworten.

Zwei der drei Wackelkandidaten aus D7 bis D9 waren echte Fehler im Code, dieser
ist einer in der Testanlage. Die Regel dahinter: **Wer prüfen will, dass etwas
ausbleibt, braucht ein Ereignis, nach dem es ausgeblieben sein muss.** Eine
Frist ist dafür nur ein Ersatz, und ein schlechter.

Bei der Absicherung kam ein vierter dazu, der hier ausdrücklich <b>nicht</b>
behoben ist: `AStolenId_DoesNotHandOverTheStream` lief in einem von sieben
Läufen in die Zeitüberschreitung beim Warten auf `ResumableStreamCount == 1` —
der Server hatte die abgerissene Sitzung binnen zehn Sekunden nicht abgelegt.
Alice hat dort `maxReconnectAttempts: 0`, kommt also nicht zurück und kann den
Eintrag nicht selbst wieder abräumen; eine Erklärung habe ich nicht. Nach D9
wäre eine längere Frist genau die falsche Antwort, und eine Vermutung
aufzuschreiben wäre schlechter als die offene Frage. Steht unter „Später".
*(Nachtrag: in D11 gefunden — es war kein Testfehler.)*

### D11. Die Wiederaufnahme gehört dem Stream, nicht der Presence ✅

Der offene Punkt aus D10 ist geklärt, und er war kein Testartefakt. `Park`
verlangte, dass die Sitzung <i>verfügbar</i> ist:

```csharp
if (session.FullJid is null || !session.IsAvailable)
    return false;
```

Damit hingen zwei Dinge aneinander, die nichts miteinander zu tun haben. Die
Wiederaufnahme wird mit `<enabled resume='true'/>` zugesagt und gehört dem
Stream; die Presence sagt den Kontakten etwas über den Menschen davor. Wer sich
unsichtbar machte, ohne die Verbindung zu beenden, verlor die Zusage
stillschweigend: Beim Abriss wurde sein Stream nicht abgelegt, sein
`<resume/>` bekam ein `<failed/>`, und alles Unbestätigte war fort — genau der
Verlust, für den der Puffer aus R2 und R7 überhaupt gebaut wurde.

Dasselbe traf den Client, dessen erste Presence noch unterwegs war. Und genau
daran hing der wackelige Test: Er riss die Verbindung ab, sobald die
Wiederaufnahme zugesagt war, und das ist im Aufbau des Clients *vor* seiner
ersten Presence. Auf einer ruhigen Maschine kam sie rechtzeitig, unter Last
nicht immer — ein Fehler, der sich als Zeitproblem verkleidet hat.

Die Bedingung ist weg. Für die Abmeldung, in deren Ablauf `Park` sitzt, ändert
das nichts: `TryMarkUnavailable` lehnt eine nie verfügbare Sitzung von sich aus
ab, die Unterscheidung war dort längst getroffen. Die Prüfung in `Park` war
also nicht nur falsch, sondern auch doppelt.

Eine Mutation, erschlagen von `AnInvisibleClient_KeepsItsResumableStream`: die
Verfügbarkeit wieder verlangen.

Drei von vier Wackelkandidaten waren damit echte Fehler im Code, einer war ein
Fehler in der Testanlage. Das ist die Ausbeute, wenn man einem einzelnen roten
Lauf nachgeht, statt ihn zu wiederholen, bis er grün ist.

### D12. Die Art einer Nachricht ✅ — und die Quittung vor Publikum

RFC 6121, Abschnitt 5.2.2 kennt fünf Arten von Nachrichten. Dieser Client kannte
eine: Alles kam gleich an, und der Empfänger konnte den Zuruf einer
Nachrichtenquelle nicht von der Zeile eines Bekannten unterscheiden — und die
aus einem Raum nicht von einer an ihn allein gerichteten. Nur `error` war schon
getrennt, weil eine Fehler-Stanza sonst als Chatzeile durchgelaufen wäre.

Wo das nicht die Anzeige betrifft, sondern das Verhalten, wird es unangenehm:
**Der Client quittierte jede Nachricht, auch die aus einem Raum.** Der Absender
ist dort der Raum und nicht ein Mensch; die Quittung ginge an den Raum, und der
reicht sie an alle weiter. Aus einer stillen Bestätigung würde eine Wortmeldung
vor Publikum — bei zwanzig Anwesenden vierhundert Quittungen für zwanzig
Zeilen. Beim Zuruf sagt es der RFC selbst: „no reply is expected".

`MessageType` trägt die Art jetzt bis in die Anwendung, und `ExpectsAReply`
entscheidet an einer Stelle, ob von selbst geantwortet wird — in beide
Richtungen: Wer in einen Raum schreibt, fordert auch keine Bestätigung mehr an
(XEP-0184, Abschnitt 5.3 rät dem Absender ausdrücklich davon ab).

Die Vorgabe ist ein MUSS und kein Geschmack: Fehlt das Attribut **oder ist sein
Wert unbekannt**, gilt die Nachricht als `normal`. Der Grund liegt in der
Zukunft — eine spätere Erweiterung soll bei alten Empfängern als gewöhnliche
Nachricht ankommen und nicht verschwinden.

Sieben Mutationen, alle erschlagen.

Der Test brauchte einen zweiten Anlauf, und der Fehler war ein alter Bekannter
in neuem Gewand: Ich beobachtete die Quittung über `OnReceiptReceived` beim
Absender — und das Ereignis feuert nur für Nachrichten, die der Client selbst
über `SendMessageAsync` abgeschickt hat. Bei einer rohen Stanza gilt die
Quittung als Fälschungsversuch und wird verworfen. Beobachtet wird jetzt, was
der Empfänger hinausschickt, und damit der Weg statt der Wirkung.

Nicht angefasst: die typabhängigen Zustellregeln des *Servers* aus Abschnitt 8.
Der Testserver stellt weiterhin für alle Typen gleich zu. Steht unter „Später".

### D13. Die Zustellregeln des Servers ✅ — und die Adresse entscheidet mit

Die Client-Hälfte aus D12 hat die Server-Hälfte sichtbar gemacht: RFC 6121,
Abschnitt 8.5 macht die Zustellung von der Art der Nachricht abhängig, und
dieser Server stellte alles gleich zu.

Vier Regeln, zwei davon MUSS:

| An den Bare-JID | Verhalten |
|---|---|
| `groupchat` | nie zustellen, `<service-unavailable/>` an den Absender |
| `error` | still verwerfen — ein Fehler auf einen Fehler wäre der Anfang einer Schleife |
| `headline` | an **alle** Resourcen mit nicht-negativer Priorität |
| `normal`/`chat` | an eine Resource |

Dazu die Priorität, die es vorher gar nicht gab: Eine Resource mit negativer
Priorität bekommt nichts, was bloss an das Konto ging. Genau dafür setzt ein
Client sie — das Zweitgerät bleibt gerichtet ansprechbar und hält sich aus dem
Übrigen heraus.

**Die Adresse entscheidet mit, und das hat mich einen Anlauf gekostet.** Meine
erste Fassung lehnte `groupchat` und `error` unbesehen ab. Abschnitt 8.5.3.1
sagt aber für eine *passende Resource*: „For a message stanza, the server MUST
deliver the stanza to the resource" — ohne Unterscheidung nach Art. Und das ist
kein Sonderfall, sondern der Normalfall: Ein Raum liefert an
`nutzer@server/resource` aus, nicht an das Konto. Meine Regel hätte die
Raumfunktion unbenutzbar gemacht und jede Fehlerantwort verschluckt.

Aufgefallen ist es daran, dass zwei Tests aus D12 rot wurden — jene, die eine
Raum-Nachricht an einen Bare-JID schickten. Auch sie waren falsch: Diese
Adressierung gibt es bei einem regelkonformen Server nicht. Sie gehen jetzt an
die Full-JID, so wie ein Raum es täte.

Acht Mutationen, alle erschlagen — darunter die feinste: die Priorität auch für
gerichtete Nachrichten anzuwenden. Sie sieht nach Gründlichkeit aus und nimmt
der negativen Priorität genau das, was sie ausmacht.

Nicht behoben und vermerkt: Ohne erreichbare Resource verlangt Abschnitt
8.5.2.2.1 für `normal` und `chat` Ablage oder Fehler. Dieser Server hat keine
Ablage für Abwesende und verwirft still — für die drei übrigen Arten ist das
richtig, für diese beiden nicht.

### D14. Die Offline-Ablage ✅ — und der dritte Weg, den es nicht gibt

Der offene Punkt aus D13. Abschnitt 8.5.2.2.1 stellt zwei Wege nebeneinander und
verbietet den dritten:

| Ohne erreichbare Resource | Vorschrift |
|---|---|
| `normal`, `chat` | ablegen **oder** `<service-unavailable/>` an den Absender |
| `groupchat` | MUSS `<service-unavailable/>` |
| `headline`, `error` | MUSS still verwerfen |

Der dritte Weg — stillschweigend verwerfen, was abgelegt oder abgelehnt werden
müsste — war genau der, den dieser Server ging. Und er ist der unangenehmste von
allen: Der Absender hält seine Nachricht für zugestellt, der Empfänger hat nie
erfahren, dass es sie gab, und **niemand kann den Verlust bemerken**. Ein Fehler,
der sich selbst verbirgt, ist schlimmer als einer, der lärmt.

Beide erlaubten Wege sind jetzt da, weil sie sich gegenseitig begrenzen: Ohne
Ablage wäre die Ablehnung der Regelfall, ohne Ablehnung hätte eine voll gelaufene
Ablage keinen Ausweg. `StoreOfflineMessages` wählt zwischen ihnen — abgeschaltet
ist der Server nicht weniger regelkonform, nur unbequemer.

**Zwei Stellen führen in die Ablage, nicht eine.** Die zweite ist Abschnitt
8.5.3.2.1: Ein `chat` an eine Resource, die es nicht gibt, wird behandelt, als
wäre er an das Konto gegangen. Die Ausnahme sieht schrullig aus und trifft den
Alltag — ein Client antwortet auf die Full-JID, die er zuletzt gesehen hat, und
wenn der Gesprächspartner in der Zwischenzeit das Gerät gewechselt hat, ist sie
weg. Für die übrigen Arten bleibt es beim stillen Verwerfen: Wer eine Full-JID
anschreibt, meint diese Resource; bei einem Gespräch ist das eine Abkürzung für
„mein Gegenüber", bei allem anderen eine Angabe, die der Absender so gewollt hat.

Nur die halbe Ausnahme umzusetzen wäre schlimmer als der bisherige Zustand
gewesen: Die Nachricht landete in der Ablage, während der Empfänger mit einer
anderen Resource daneben sitzt und wartet. Deshalb steht sie in einem Test.

**Die Grenze weist ab und verdrängt nicht.** Beide Richtungen verlieren eine
Nachricht, aber nur eine davon sagt es jemandem. Und eine Grenze, die verdrängt,
wäre selbst der Angriff: Wer die Ablage vollschreibt, löschte damit fremde Post.
Dieselbe Überlegung wie bei den aufbewahrten Subscription-Anfragen aus S6.

**Nachgereicht wird bei jeder nicht-negativen verfügbaren Presence, nicht beim
Verfügbar*werden*** — anders als die aufbewahrte Anfrage direkt darüber. Der
Unterschied liegt daran, dass die Ablage beim Zustellen geleert wird: Ein zweiter
Durchgang findet nichts mehr und kann nichts doppelt vorlegen. Und er hat einen
eigenen Fall: Eine Resource, die mit negativer Priorität angemeldet ist und sie
auf 0 hebt, war schon verfügbar — sie wird aber gerade eben erst zu einem
Empfänger.

Beide Bedingungen sind nötig, nicht nur die Priorität. Eine Abmeldung setzt die
Priorität der Sitzung auf 0 zurück, denn eine abgemeldete Resource hat keinen
Zustand zu berichten. Wer nur nach der Priorität fragt, leert die Ablage in einen
Stream, der sich gerade verabschiedet.

Dazu drei Dinge, ohne die die Ablage nur halb taugt: der XEP-0203-Stempel (ohne
ihn behauptet eine Nachricht von gestern, sie sei von jetzt), das Überdauern
eines Neustarts (ein angenommener Absender darf sich darauf verlassen) und die
Ankündigung als `msgoffline` in disco#info (sonst müsste ein Client aus dem
Ausbleiben eines Fehlers schliessen, dass abgelegt wurde — und ein Fehler kann
sich verspäten).

Neu am Client: `PresencePriority`. Ohne sie kann ein Client nicht sagen, wie sehr
er gemeint ist, wenn eine Nachricht an das Konto geht — und der negative Zweig
der Ablage wäre durch den Client überhaupt nicht prüfbar gewesen.

27 Mutationen, 26 erschlagen. Der Überlebende ist die Abkürzung `if
(_offlineMessages.Count == 0) return [];` in `TakeOfflineMessages`. Sie ist keine
Aussage über Verhalten, sondern eine Vorkehrung gegen ein Schreiben ohne
Änderung: Ohne sie meldete jede verfügbare Presence eine Kontoänderung, und der
Dateispeicher schriebe bei jeder Anmeldung. Kein Test hält sie fest, und das ist
richtig so — ein Test darauf prüfte den Dateizugriff und nicht das Protokoll.

**Der lehrreiche Fehlschlag lag diesmal im Werkzeug.** Ein Test scheiterte drei
Läufe hintereinander, auch allein, auch nach Neubau — und war doch richtig. Mein
Mutationsskript setzt die Datei am Ende mit `Copy-Item` aus einer Sicherung
zurück, und `Copy-Item` übernimmt den Zeitstempel der Sicherung. Der ist älter
als das mutierte Binary; MSBuild hielt den Build für aktuell und übersetzte nicht
neu. Der „reproduzierbare" Fehlschlag lief gegen das Binary der letzten Mutation.

Die Lehre ist nicht neu, aber sie hatte eine neue Verkleidung: **Wenn ein Test
scheitert, den man gerade geschrieben hat, ist der Verdächtige nicht immer der
Code — er kann auch das sein, womit man messt.** Das Skript setzt den Zeitstempel
jetzt neu, und jeder Mutationsdurchgang klammert die Mutation zwischen zwei grüne
Läufe ohne sie.

Nicht behoben und vermerkt: Eine Nachricht, die über die Servergrenze
hereinkommt, nimmt weiterhin nicht den Weg aus Abschnitt 8.5 — sie geht direkt
ins Routing. Damit greifen für sie weder die Ablage noch die Prioritäten noch die
Typregeln aus D13. Das ist kein Loch, das die Ablage aufgerissen hat, sondern
eines, das sie sichtbar macht: Für den häufigsten Fall einer Nachricht an einen
Abwesenden — den Bekannten auf einem anderen Server — ist die Ablage noch nicht
zuständig.

Ebenfalls vermerkt: XEP-0160 rät, eine Nachricht mit ausschliesslich
XEP-0085-Inhalt (Tippstatus) nicht abzulegen. Dieser Client schickt keine, also
gibt es dafür keinen Weg zu prüfen — die Regel bliebe ungetestet.

### D15. Eingehende S2S-Stanzas ✅ — und eine Weiche, die schon da war

Der offene Punkt aus D14, und der grössere von beiden: Abschnitt 8.5 spricht
durchweg von einer „inbound stanza" und fragt nirgends, ob sie von einem Client
oder von einem anderen Server kam. Dieser Server fragte es doch. Was über die
Grenze kam, ging unbesehen ins Routing — ohne Ablage, ohne Prioritäten, ohne
Typunterscheidung.

Damit lag die Lücke genau im häufigsten Fall. Der Bekannte auf einem anderen
Server ist der Regelfall und nicht die Ausnahme; zwei Konten auf derselben
Instanz sind es nicht. Wer eine Offline-Ablage baut, baut sie vor allem für ihn —
und in D14 tat sie für ihn nichts.

Jetzt nehmen beide Herkünfte eine Strecke, `DeliverMessageLocallyAsync`. Der
ganze Unterschied steckt in einem Parameter: `XMPPSession? origin` ist `null`,
wenn die Nachricht von aussen kam, und das entscheidet allein über die
`<sent>`-Carbons — die gehören den anderen Geräten des Absenders, und die eines
fremden Kontos sind nicht unsere Sache.

**Zwei meiner Verzweigungen waren überflüssig, und das Aufräumen war der
lehrreiche Teil.** Ich hatte zuerst zwei Rückwege für eine Fehlerantwort gebaut —
in den Stream des Absenders, wenn er hier sitzt, sonst über die Grenze hinaus —
und zwei Wege, den Absender zu bestimmen (`origin.FullJid` oder das `from` der
Stanza). Beide Paare waren dasselbe:

- `RouteToAsync` **ist** die Weiche zwischen „hier" und „woanders"; ihr eigener
  Kommentar sagt das seit S4a. Eine Verzweigung daneben war eine zweite Antwort
  auf eine schon beantwortete Frage — und zwei Antworten laufen mit der Zeit
  auseinander. Sie erledigt nebenbei auch den Namensraumwechsel.
- Das `from` ist in beiden Fällen geprüft und nicht behauptet: Bei einem Client
  stempelt es der Server selbst, bei einer fremden Stanza hat
  `AcceptFromRemoteAsync` die Absenderdomain gegen die Gegenstelle geprüft.
  `origin.FullJid` daneben lieferte dieselbe Zeichenkette.

Aufgefallen ist es an den Mutationen: Beide Verzweigungen liessen sich entfernen,
ohne dass ein Test es merkte — nicht weil die Tests lückenhaft waren, sondern
weil die Zeilen nichts taten. **Ein überlebender Mutant ist nicht immer eine
fehlende Prüfung; manchmal ist er überflüssiger Code, der sich als Gründlichkeit
tarnt.**

Zehn Mutationen, neun erschlagen — zwei davon erst im zweiten Anlauf, und beide
Male war der Test schuld:

**Der Presence-Wächter stand am falschen Ort.** Er sollte belegen, dass nur
Nachrichten den neuen Weg nehmen. Bei verbundenem Bob bestand er auch mit der
Mutation, die *alles* durch die Nachrichtenstrecke schickt — denn einer
erreichbaren Resource stellt auch die zu. Sichtbar wird der falsche Weg erst
dort, wo die beiden sich unterscheiden, und das ist die Ablage: Ein
`<presence/>` hat kein `type`, gälte damit als `normal` und läge beim nächsten
Anmelden als Anwesenheit von vorgestern bereit. Der Test prüft jetzt mit
**abwesendem** Bob.

Das ist eine neue Fassung einer alten Regel. „Beobachte den Weg, nicht die
Wirkung" hiess bisher: Sieh nach, was hinausgeht. Hier heisst es: **Ein Wächter
gegen den falschen Weg muss dort stehen, wo die Wege sich trennen.** An einer
Stelle, an der beide dasselbe tun, bewacht er nichts.

**Die Ablehnung kam an, war aber an den Falschen adressiert.** Das `to` der
Fehlerantwort durch die Empfängeradresse zu ersetzen, überlebte: Zugestellt wird
sie nach der Routing-Adresse, und die blieb richtig. Über die Grenze fällt es
ohnehin nicht auf, weil `RouteToAsync` beim Hinausgehen ein `StampTo` setzt und
das falsche `to` überschreibt — daheim schon. Ein Client, der nach RFC 6120,
Abschnitt 8.1.1 prüft, ob eine Stanza an ihn adressiert ist, verwürfe sie
stillschweigend. Der lokale Ablehnungstest aus D13 prüft das jetzt mit.

Der eine Überlebende ist keiner über diesen Code: Lässt man im Offline-Zweig die
Frage nach der Herkunft weg, wirft der Mutant für eine Nachricht von aussen eine
`NullReferenceException` — und **kein Test sieht sie**. Das `catch` beim
Verarbeiten eines Frames ist für abgerissene Verbindungen gedacht und verschluckt
jeden Programmierfehler mit. Weil die Ablage vorher geschrieben ist und danach
nichts mehr folgt, bleibt der Wurf ohne Folge. Die Zeile ist richtig; das `catch`
ist das Problem, und es steht unter „Später".

Nicht behoben und vermerkt: Presence und IQ von einem anderen Server nehmen
weiterhin den geraden Weg. Bei Presence ist der Unterschied klein, bei IQ nicht —
eine Anfrage an einen Bare-JID soll der Server nach Abschnitt 8.5.2.1.3 selbst
beantworten; verteilt wird sie derzeit an **alle** Resourcen, und jede antwortet.
Mehrere Antworten auf eine `id`.

### D16. Die Anfrage an ein Konto ✅ — und zwei Abschnitte, ein Verhalten

Der offene Punkt aus D15, und der einzige der Reihe, der ein Verfahren zerbrach
statt nur eine Regel zu verletzen.

Abschnitt 8.5.2.1.3 sagt es doppelt: „the server itself MUST reply on behalf of
the user" **und** „MUST NOT deliver the IQ stanza to any of the user's available
resources". Die Verdopplung hat einen Grund. IQ ist ein Frage-Antwort-Paar, über
die `id` zusammengehalten, und jede empfangene Anfrage *muss* beantwortet werden
(RFC 6120, Abschnitt 8.2.3, Regel 3). Wer sie an alle Resourcen verteilt, bekommt
von allen eine Antwort — der Fragende hält drei Antworten auf eine `id` in der
Hand und kann nicht entscheiden, welche gilt.

Genau das tat dieser Server: Jede IQ-Anfrage an eine fremde Adresse ging ins
Routing, und das verteilte an einen Bare-JID an jede Sitzung, die es fand. Bei
einer Nachricht wäre mehrfache Zustellung lästig; hier bricht sie das Verfahren.

**Die Antwort ist immer `<service-unavailable/>`, und das ist vollständig und
nicht halb.** Der Abschnitt verlangt eine eigene Antwort, „if the semantics of
the qualifying namespace define a reply that the server can provide on behalf of
the user" — und andernfalls ausdrücklich diesen Fehler. Dieser Server kennt
keinen Namensraum, den er im Namen eines Nutzers beantworten könnte; die Stelle
für einen späteren ist markiert.

**Eine Antwort wird nie beantwortet.** Hier standen zwei Vorschriften
gegeneinander: Abschnitt 8.5.3.2.3 verlangt für „eine IQ-Stanza" ohne passende
Resource einen Fehler und unterscheidet die Art nicht; RFC 6120, Abschnitt 8.2.3,
Regel 4 verbietet, ein `result` oder `error` zu beantworten. Regel 4 gewinnt: Ein
Fehler auf ein `result` ginge an jemanden, der nichts gefragt hat, unter der `id`
einer Frage, die er selbst beantwortet hat.

**Der Unterschied zum unbekannten Konto ist lehrreich.** Bei einer Nachricht darf
der Server nach Abschnitt 8.5.1 schweigen und verrät damit nicht, welche Konten
es gibt; bei einer Anfrage muss er antworten. Preisgegeben wird trotzdem nichts,
weil die Antwort dieselbe ist wie für ein vorhandenes Konto ohne erreichbare
Resource. Zwei Tests stehen deshalb nebeneinander — wären die Antworten
verschieden, hätte der Server ein Verzeichnis seiner Konten ausgeplaudert.

Neun Mutationen, alle erschlagen — nach zwei Runden, und beide haben etwas
gelehrt:

**Zwei Abschnitte, ein Verhalten.** Ich hatte den Bare-JID-Fall und „Full-JID
ohne passende Resource" getrennt behandelt, weil der RFC sie in zwei Abschnitten
behandelt. Eine Mutation, die die Trennung aufhob, überlebte — und musste
überleben: Die Abschnitte 8.5.2.1.3, 8.5.2.2.3 und 8.5.3.2.3 verlangen alle
dasselbe. **Wo das vorgeschriebene Verhalten dasselbe ist, kann kein Test die
Fälle unterscheiden, und eine Verzweigung, die es doch tut, behauptet einen
Unterschied, den es nicht gibt.** Die Gliederung eines RFC ist kein Bauplan für
Verzweigungen.

Geblieben ist eine Zeile weniger: `SessionOf` vergleicht ausschliesslich
Full-JIDs, ein Bare-JID fällt deshalb von selbst in den Fehlerzweig. Das „MUST
NOT deliver" hängt damit an einer Eigenschaft einer anderen Methode — gehalten
wird es nicht von einer Prüfung, sondern von einem Test, der zwei Resourcen
anmeldet und nur besteht, wenn keine die Anfrage sieht. Die Mutation, die genau
den alten Fehler wiederherstellt (an alle statt an eine), erschlägt er.

**Die Serveradresse ist kein Nutzer.** Eine Mutation, die den Zustellweg für
Nutzer auch für Anfragen an die Domain selbst nahm, überlebte zunächst. Sie hätte
`<service-unavailable/>` geantwortet, wo der Server heute schweigt — und das wäre
schlechter: Schweigen ist eine Lücke, ein Fehler ist eine Aussage, und diese
Aussage wäre falsch. Eine Gegenstelle, die sie glaubt, fragt nicht wieder. Ein
Test hält jetzt fest, dass der Nutzer-Zustellweg die Serveradresse nicht anfasst.

**Ein roter Lauf, der nicht dazugehört, und er bleibt vermerkt.** In einem von
vier Vollläufen scheiterte `TheStreamSurvivesABrokenConnection` gegen einen
Fremdserver mit „Zeitüberschreitung beim Warten auf: den wiederaufgenommenen
Stream". Der Test fährt einen Client gegen Prosody bzw. ejabberd; `XMPPServer`
kommt darin nur als statische Warte-Hilfe vor, der geänderte Zustellweg also gar
nicht. Allein läuft er 4 von 4 Mal grün, danach zwei weitere Vollläufe ebenso.

Der wahrscheinliche Mechanismus: Der Test reisst die Verbindung ab und gibt dem
Client 15 Sekunden für Wiederverbindung samt Wiederaufnahme. Unter der Last eines
vollen Laufs — viele Fixtures gleichzeitig, die Gegenstelle jenseits der
WSL-Rückschleife — reicht das bei exponentiellem Backoff nicht immer. Bewiesen
ist das nicht: Ein einzelnes Ereignis kann die Erklärung nicht von einer anderen
trennen. Deshalb steht es unter „Später" und nicht als erledigt.

Nicht behoben und vermerkt: die zweite Hälfte von Abschnitt 8.5.3.1. Wer die
Presence des Empfängers nicht sehen darf, soll eine Anfrage an dessen Resource
nicht zugestellt bekommen — schon die Antwort verrät, dass die Resource
existiert. Das braucht die Aufzeichnung gerichteter Presence, die es hier nicht
gibt. Ebenfalls offen: Eine Anfrage von einer Gegenstelle an die eigene
Serveradresse (disco#info, Ping) bleibt unbeantwortet; die Antworten dafür stehen
in `HandleIqAsync` und wollen eine Sitzung, die es bei S2S nicht gibt.

### D17. Schon die Antwort ist eine Auskunft ✅

Der offene Punkt aus D16: die erste Hälfte von Abschnitt 8.5.3.1. Eine IQ-Anfrage
an eine Resource wird nur zugestellt, wenn der Fragende die Presence des
Empfängers sehen darf — sonst `<service-unavailable/>`.

Der Grund steht in Abschnitt 11 und ist feiner, als er zuerst aussieht: **Schon
die Antwort ist eine Auskunft.** Wer eine Full-JID anfragt und ein Ergebnis
bekommt, weiss, dass genau diese Resource in diesem Augenblick angemeldet ist;
wer `<service-unavailable/>` bekommt, weiss es nicht. Ohne die Prüfung liesse
sich die Anwesenheit eines Menschen abfragen, ohne ihn je um Erlaubnis gefragt zu
haben — und Resourcenamen liessen sich durchprobieren, bis einer antwortet.

Deshalb prüft ein Test auch, dass die Abweisung für eine **vorhandene** Resource
dieselbe ist wie für eine erfundene. Wären die beiden verschieden, wäre die
Prüfung wirkungslos: Der Fragende läse aus der Art der Ablehnung heraus, was sie
ihm verschweigen soll.

**Zwei Wege hinein, und beide waren nötig.** Der Roster des Empfängers mit
`from` oder `both` — oder gerichtete Presence (Abschnitt 4.6). Nur den Roster zu
nehmen wäre zu streng für den häufigsten Fall überhaupt: Ein Gespräch mit
jemandem, der nicht im Roster steht, beginnt damit, dass man ihm seine Anwesenheit
zeigt (Abschnitt 5.1). Wer das getan hat, verliert durch eine Antwort nichts mehr.

Die Liste dafür ist neu und folgt Abschnitt 4.6.1 wörtlich: je Resource, geleert
wenn der Nutzer sich abmeldet, und ein Eintrag verschwindet, sobald ihm gerichtete
`unavailable`-Presence geschickt wird. Beides sind MUSS-Regeln, und beide haben
denselben Grund wie die Prüfung selbst: Eine Erlaubnis, die man nicht
zurücknehmen kann, ist keine.

**Die Richtung im Roster ist leicht zu verwechseln, und `both` verdeckt die
Verwechslung vollständig.** Gefragt wird die Hälfte des **Empfängers**: „der darf
mich sehen" (`from` oder `both`). Ein `to` heisst das Gegenteil und gäbe die
Auskunft an genau die falsche Seite — an jeden, den der Empfänger beobachtet,
statt an jeden, der ihn beobachten darf. Bei `both` stimmen beide Hälften, und
eine Umsetzung, die die falsche liest, fällt nicht auf. Der Test setzt deshalb
erst `to` (Abweisung) und dann `from` (Zustellung).

**Drei bestehende Tests haben einen Leck dokumentiert, ohne es zu merken.**
`PingBetweenClients_MeasuresRoundTrip` pingte einen Fremden an, und zwei Tests aus
D16 fragten eine fremde Resource. Alle drei bestanden nur, weil der Server die
Regel nicht kannte — ein Ping zwischen zwei Fremden ist genau der Fall, den sie
abweist. Sie machen jetzt zuerst Kontakte, was ausserdem der realistischere
Aufbau ist.

Zehn Mutationen, alle erschlagen. Drei davon hätte die Sammlung vorher nicht
gehalten:

- Die falsche Roster-Hälfte lesen — erschlagen nur vom neuen einseitigen Test.
- Die gerichtete Presence mit der Full-JID vermerken statt mit dem Bare-JID —
  erschlagen nur, weil ein Test die Full-JID anschreibt. Beide Formen kommen
  jetzt vor, weil ein Client beide schickt.
- Die Prüfung auch auf `result` und `error` anwenden. Das sieht nach
  Gründlichkeit aus und verstösst gegen die zweite Hälfte desselben Abschnitts:
  „For an IQ stanza of type 'result' or 'error', the server MUST deliver the
  stanza to the resource." Eine Antwort gehört dem, der gefragt hat, und der hat
  seine Erlaubnis mit der Frage schon gehabt.

Nicht behoben und vermerkt: der SOLL-Teil von Abschnitt 4.6.1 — eine Entität, die
uns `unavailable` schickt, soll aus der Liste verschwinden. Und Abschnitt 4.6.3,
Regel 2: Wird die Resource unverfügbar, soll die Abmeldung an jede Entität gehen,
der sie gerichtete Presence geschickt hat. Die Liste dafür gibt es nun; das
Verschicken fehlt.

### D18. Ein `catch` ohne Filter ✅ — und eine Messung, die die Aufgabe umgedreht hat

Der Punkt aus D15: Um das Verarbeiten eines Frames stand ein `catch` ohne Filter,
mit dem Vermerk „Verbindung abgerissen - im Test der Normalfall". Ich wollte ihn
auf die Ausnahmen einschränken, die ein Abriss wirklich erzeugt — und habe erst
gemessen.

**Die Messung hat die Aufgabe umgedreht.** Ich habe den Fang durch ein Anhängen
an eine Datei ersetzt und die ganze Sammlung laufen lassen: **keine einzige
Ausnahme.** Der Vermerk stimmte nicht mehr; der Abriss wird längst anderswo
abgefangen (`SendAsync` fragt `IsClosed`, Hermod liefert einen `SentStatus`
statt zu werfen). Was der Fang noch leistete, war ausschliesslich das Verschlucken
von Programmierfehlern.

Damit fällt die geplante Lösung weg. Eine Liste von Ausnahmen, die ein Abriss
„wirklich" erzeugt, wäre geraten — und ein Zweig, den kein Test erreicht, ist
genau die Sorte Vorkehrung, die den Fehler von damals gedeckt hat. Es gibt nichts
zu filtern.

**Ersatzlos entfernen wäre auch falsch gewesen**, und das habe ich ebenfalls
nachgesehen statt vermutet: Hermod fängt oberhalb jede Ausnahme aus
`ProcessTextMessage` und schreibt sie mit `Logger.LogError` weg. Ohne unseren Fang
wanderte der Fehler also von „stillschweigend verworfen" nach „in einem Log, das
kein Test ansieht". Besser, aber nicht die Lösung.

Die Lösung ist **Sichtbarkeit**: `OnInternalError` meldet Sitzung, Frame und
Ausnahme; geworfen wird nichts weiter, am Verhalten des Servers ändert sich
nichts. Und in der Testsammlung hängt eine Wache an **jedem** Test, die jede
Meldung als Mangel behandelt. Wo ein solcher Fehler auftritt, weiss man vorher
nicht — ein eigener Test dafür bewachte nur den Weg, den er selbst geht.

**Der Nachweis ist die Mutation von damals.** Der D15-Überlebende — die
Herkunftsfrage vor den `<sent>`-Carbons weglassen, was für eine Nachricht von
aussen eine `NullReferenceException` wirft — wird jetzt von **sechs** Tests
erschlagen. Zum ersten Mal in dieser Reihe macht ein Schritt einen früher benannten
Überlebenden nachträglich sterblich. Die Liste der benannten Ausnahmen geht von
sechs auf fünf.

Fünf Mutationen auf die eigenen Zeilen, alle erschlagen — eine erst im zweiten
Anlauf, und sie ist die interessanteste: **Ein Wächter, den nichts auslöst, ist
selbst unbewacht.** Die Mutation „die Wache gibt immer frei" überlebte jeden
Test. Sie musste: Wo kein Fehler gemeldet wird, verhält sich eine wirkungslose
Wache genau wie eine wirksame, und ein Test, der scheitern *muss*, lässt sich
nicht als bestehender Test schreiben. Erst die Trennung von `Watch` (Verdrahtung)
und `Record` (Aufnahme) machte die Wache unmittelbar befragbar — dieselbe Falle
wie beim alten `catch`, nur eine Ebene höher.

Neu und begründet: `FailFrameHandling`, ein Schalter, dessen ganze Aufgabe ein
Fehlschlag ist. Ohne ihn wäre der Meldeweg von keinem Test erreichbar — dieselbe
Begründung wie bei `SwallowClientStanzas`, und genau der Mangel, an dem der alte
Fang so lange unbemerkt blieb.

Nicht behoben und vermerkt: Die Wache hängt an `AXMPPTests` und an den drei
Fixtures, die Stanzas zwischen zwei eigenen Servern zustellen
(`FederationTests`, `CrossDomainSubscriptionTests`, `RemoteDeliveryRulesTests`).
Weitere Fixtures betreiben eigene Server, ohne bewacht zu sein — dort gilt
weiterhin, dass ein Programmierfehler nur in Hermods Log landet. *(Behoben in
D19.)*

### D19. Die restlichen Fixtures ✅ — die Wache dorthin, wo der Server entsteht

Der offene Punkt aus D18. Es waren nicht neun Fixtures, wie dort vermerkt,
sondern **elf**: `AccountStoreTests` und `AForeignPeerFederationTests` hatte ich
in der Liste übersehen. Aufgefallen ist es beim Nachzählen der Erzeugungsstellen,
nicht beim Lesen der eigenen Notiz — eine Liste, die man aus dem Kopf schreibt,
ist keine Bestandsaufnahme.

Jetzt ist jeder Server in der Sammlung bewacht: `AXMPPTests` plus vierzehn
Fixtures, die eigene betreiben.

**Verdrahtet über `Watched(…)`, nicht über eine eigene Zeile.** Die drei aus D18
hatten `_guard.Watch(_links)` getrennt darunter stehen; das ist ein zweiter Ort,
den man beim nächsten Server vergisst. `Watched(new XMPPServer(…))` gibt den
Server zurück und stellt ihn unter die Wache — damit steht sie dort, wo er
entsteht, und die drei aus D18 sind auf dieselbe Form gebracht. Mehrere Fixtures
erzeugen ihre Server ohnehin nicht im SetUp, sondern mitten im Test; für die gibt
es keine andere brauchbare Stelle.

**Zwei Fixtures brauchten ein neues `[SetUp]`**, und der Grund ist eine
Eigenschaft von NUnit, die leicht zu übersehen ist: Eine Fixture-Instanz wird für
alle ihre Tests wiederverwendet. Ohne `Reset()` nähme der nächste Test die Meldung
des vorigen mit und scheiterte an einem fremden Fehler.

Drei Mutationen, alle erschlagen — alle drei auf denselben Punkt: dass `Watched`
kein Durchreicher ist. Keine Wache anhängen, einen anderen Server zurückgeben,
oder die Weiterleitung in der Testbasis kurzschliessen. Geprüft wird das am
echten Weg: Ein zweiter Server bekommt einen Client, scheitert absichtlich, und
die Meldung muss bei der Wache desselben Tests ankommen. Ohne diesen Test wären
alle elf Fixtures unbewacht, ohne dass ein einziger anderer Test es merkte — wo
kein Fehler auftritt, sieht eine fehlende Wache wie eine wirksame aus.

**Die dritte Messung, und die vollständigste:** ein Volllauf mit beiden
Fremdservern, jeder Server der Sammlung bewacht — **keine einzige Meldung.** Der
alte `catch` war über die ganze Sammlung hinweg toter Ballast, und das steht
jetzt nicht mehr auf einer Messung, sondern auf drei.

Ehrlich vermerkt: Die Verdrahtung selbst hält kein Test. Nähme jemand in einem
einzelnen Fixture das `AssertClean()` heraus, fiele es nicht auf — ein Test dafür
müsste in jedem Fixture einen Fehler auslösen. Gesichert ist sie durch eine
Quelltextprüfung: Im Testprojekt steht kein `new XMPPServer(` ohne `Watched(…)`,
mit genau zwei gewollten Ausnahmen — der Server der Basis, der in der Folgezeile
bewacht wird, und die Hilfsvariable des Tests, der die Rückgabe von `Watched`
prüft.

### D20. Eine Zusage, die endet ✅ — Abschnitt 4.6.3, Regel 2

Der offene Punkt aus D17: Wird eine Resource unverfügbar, geht die Abmeldung auch
an die Empfänger ihrer gerichteten Presence.

Die Regel schliesst eine Lücke, die sonst niemandem auffällt. Wer einem Fremden
seine Anwesenheit zeigt, steht deswegen **nicht** in dessen Roster — und bekäme
ohne diesen Weg nie ein Ende. Der Fremde führte die Resource für immer als
anwesend. Und das ist der Regelfall, nicht die Ausnahme: Ein Gespräch mit
jemandem, der nicht im Roster steht, beginnt nach Abschnitt 5.1 genau damit. Seit
D17 hängt an derselben Liste ausserdem, wer diese Resource überhaupt etwas fragen
darf (Abschnitt 8.5.3.1) — eine Zusage, die nie endet, wäre damit doppelt
unangenehm.

**Zwei Wege führen in die Unverfügbarkeit, und der zweite ist der häufigere.** Die
eigene Abmeldung des Clients, und der Verbindungsabriss, bei dem der Server sie in
seinem Namen erzeugt (Abschnitt 4.5.2). Ein Client verschwindet meist, ohne sich
zu verabschieden; ginge die Abmeldung nur an den Roster, bliebe genau dann der
Fremde zurück.

**Die Roster-Einschränkung ist keine Formsache.** Wer mit `from` oder `both` im
Roster steht, bekommt die Abmeldung schon über die gewöhnliche Verteilung. Der RFC
grenzt Regel 2 aus demselben Grund auf Entitäten ein, die *nicht* so im Roster
stehen — käme sie zweimal, käme ein Client durcheinander, der Presence zählt statt
sie zu ersetzen.

**Der Klammerzusatz fällt mit der Liste zusammen.** „if the user has not yet sent
directed unavailable presence to that entity": Eine gerichtete Abmeldung nimmt den
Empfänger aus der Liste (Abschnitt 4.6.1), und was nicht darin steht, wird nicht
benachrichtigt. Zwei Vorschriften, eine Umsetzung — und ein Test, der beide
zugleich hält.

**Herausgeben und Leeren in einem Aufruf**, `TakeDirectedPresenceTargets`. Das ist
der Kern des Entwurfs: Abschnitt 4.6.1 verlangt das Leeren beim Abmelden, Regel 2
verlangt, die Abmeldung vorher an genau diese Empfänger zu schicken. Wären es zwei
Aufrufe, liesse sich der zweite vergessen — so kommt niemand an die Empfänger,
ohne die Liste zu leeren, und niemand leert sie, ohne sie in der Hand zu halten.

Damit ist auch eine Nachlässigkeit aus D17 behoben: Das Leeren stand dort in
`RecordPresence`, also **vor** der Stelle, die die Liste braucht — und der Weg
über den Verbindungsabriss leerte sie überhaupt nicht. Ein Fremder durfte eine
abgerissene Resource weiter befragen.

Sechs Mutationen, alle erschlagen — eine erst im zweiten Anlauf, und sie ist die
lehrreiche: **die Liste bei *jeder* Presence abzuholen statt nur bei der
Abmeldung.** Kein Test überlebte das nicht, weil keiner nach der gerichteten
Presence noch eine gewöhnliche schickte — die Reihenfolge, die im Betrieb die
Regel ist. Ein Client meldet bei jedem Wechsel auf „abwesend" eine neue Presence;
wer dabei die Liste leert, nimmt dem Gegenüber mitten im Gespräch beides, die
Abmeldung am Ende und das Fragerecht.

Die Lehre dazu: **Meine Tests liessen den Client je Abschnitt genau eine Sache
tun, und die Mutation lebte in der Lücke zwischen den Abschnitten.** Ein Test, der
nur die Reihenfolge prüft, die er selbst gebaut hat, prüft nicht die, die
vorkommt.

Ein eigenes Fixture, `DirectedPresenceTests`. Zuerst standen die Tests in
`IqDeliveryRulesTests`, weil die Liste dort entstanden war — sie prüfen aber die
Zustellung von Presence und nicht die von IQ, und ein Test gehört dorthin, wovon
er handelt.

### D21. Wer geht, verliert seinen Platz ✅ — und eine Begründung, die falsch war

Der letzte offene Punkt an Abschnitt 4.6: der SOLL-Teil von 4.6.1. Wer dem Nutzer
eine Abmeldung schickt, verschwindet aus dessen Liste gerichteter Presence. Damit
ist der Abschnitt vollständig.

**Die beiden Hälften des Satzes sehen ähnlich aus und meinen Gegenteiliges.** Das
MUSS betrifft den *eigenen* Widerruf — „any entity **to which** the user sends
directed unavailable presence" —, das SOLL die Gegenrichtung: „any entity that
**sends** unavailable presence **to** the user". Der andere geht, und damit ist die
vorübergehende Beziehung ebenfalls zu Ende. Sichtbar wird es über Abschnitt
8.5.3.1: Ohne diesen Weg behielte ein Rückkehrer sein Fragerecht, obwohl ihm
niemand mehr etwas gezeigt hat.

Angesehen wird der **Empfang** und nicht das Senden, denn genau so ist die Regel
formuliert. Der Aufruf steht deshalb in `RouteToAsync` — der einen Weiche, durch
die jede Stanza an eine hiesige Adresse läuft — und zusätzlich in den zwei
Broadcast-Schleifen, die unmittelbar an die Sitzung senden.

**Und hier lag der lehrreiche Fehler, diesmal nicht im Code, sondern in meiner
Begründung.** Zwei Mutationen überlebten: die beiden Broadcast-Schleifen. Ich
hatte in den Code geschrieben, das Vergessen sei für sie ohne sichtbare Folge —
„wer im Roster steht, behält sein Fragerecht über den Roster". Das war falsch, weil
ich die beiden Roster-Hälften verwechselt hatte:

- Dass Alices Abmeldung Bob über die gewöhnliche Verteilung erreicht, entscheidet
  **Alices** Roster: Dort steht Bob mit `from`.
- Ob Alice Bob etwas fragen darf, entscheidet **Bobs** Roster.

Bei einem einseitigen Roster — Alices Hälfte gefüllt, Bobs leer — kommt die
Abmeldung also an, während das Fragerecht allein an der Liste hängt. Der Weg ist
sehr wohl beobachtbar. Zwei neue Tests, beide Mutationen erschlagen.

Die Lehre ist unangenehmer als die üblichen: **Eine plausibel klingende Begründung
für „nicht beobachtbar" verdient dieselbe Prüfung wie der Code.** Hätte ich sie
stehen gelassen, wären zwei benannte Ausnahmen in der Liste gelandet — mit einem
Argument, das schon beim Aufschreiben nicht stimmte. Der Mutationsdurchgang hat
nicht den Code widerlegt, sondern den Kommentar.

Sieben Mutationen, alle erschlagen.

### D22. Der Stream endet ✅ — eine Entscheidung, die anders gefallen ist

D18 hat den Fehlschlag beim Verarbeiten eines Frames sichtbar gemacht und das
Weitermachen ausdrücklich als **Entscheidung** vermerkt, nicht als Lücke. Die
Entscheidung ist nun anders gefallen: Der Stream endet mit
`<internal-server-error/>`.

Der Grund ist der Zustand. Was der Frame ändern sollte, ist halb geändert, und
niemand weiss, wie weit — der Client rechnet mit einem Zustand, den der Server
nicht mehr hat. Ausgerechnet der Fehler, der am wahrscheinlichsten Zustand
hinterlässt, blieb der einzige ohne Folgen. Abschnitt 4.9.1.1 lässt danach auch
keine Wahl: „Stream-level errors are unrecoverable."

Und der Client verliert dabei nichts: `internal-server-error` gilt als
wiederholbar, er baut den Stream neu auf und beginnt mit einem Zustand, über den
beide Seiten sich einig sind. Das ist mehr, als ihm ein weiterlaufender Stream mit
halb verarbeiteter Stanza gibt.

**Drei Schritte, und der mittlere ist der, den man über WebSocket vergisst.**
Stream-Fehler, dann `<close/>` (RFC 7395, Abschnitt 3.6 — es steht für das
`</stream:stream>`), dann die Verbindung. Ohne das `<close/>` sieht der Client
einen Socket, der ohne Abschied zufällt, und das ist ein Netzwerkausfall und kein
beendeter Stream. Genau diese Zeile überlebte zunächst eine Mutation: Der
Stream-Fehler war ja schon draussen, und `OnStreamError` feuerte auch ohne sie.
Jetzt prüft der Test den Rahmen auf dem Draht.

**Ein Test hat sein Gegenteil ersetzt, und das ist kein Widerspruch.**
`TheConnectionSurvivesAReportedFailure` hielt in D18 fest, dass der Stream
weiterläuft — richtig für die damalige Entscheidung. An seiner Stelle steht jetzt
`TheStreamEndsWithInternalServerError`. Was von ihm bleibt, ist die zweite Hälfte
seiner Aussage: Der gescheiterte Frame darf auch nicht auf einem Umweg doch noch
zugestellt werden; die steht nun als eigener Test.

**Ein zweiter Test brauchte eine Korrektur, und der Grund ist lehrreich.**
`ASecondServer_IsWatchedThroughWatched` wartete auf die Meldung zu einem
*bestimmten* Frame. Seit der Stream nach dem ersten Fehlschlag endet, hängt es am
Zufall, welcher Frame das ist — die eigene Nachricht oder ein `<a/>` des Stream
Managements. Der Test prüfte damit die Reihenfolge der Frames statt die
Verdrahtung und wartet jetzt auf *irgendeine* Meldung; von einem anderen Server
kann sie nicht kommen. **Ein Test, der genauer hinsieht als nötig, prüft
irgendwann etwas anderes als gemeint.**

Sechs Mutationen, alle erschlagen — eine erst im zweiten Anlauf (das `<close/>`).

Nicht behoben und vermerkt: `SendStreamErrorAsync` schickt weiterhin nur den
Fehler, ohne zu schliessen. Abschnitt 4.9.1.1 verlangt beides, und die
Unterscheidung zu `FailStreamAsync` ist eine Bequemlichkeit für die Aufrufer in
`S2SStream` und in den Tests. *(Der Halbsatz über `S2SStream` war falsch — siehe
D23.)*

### D23. Eine Wahl, die es nicht gibt ✅

Der Punkt aus D22: `SendStreamErrorAsync` schickte den Stream-Fehler, ohne den
Stream zu schliessen. Abschnitt 4.9.1.1 verlangt beides, und zwar in einem Zug.

**Erst die Bestandsaufnahme, und sie hat meinen eigenen Vermerk widerlegt.** In
D22 hatte ich geschrieben, die Trennung sei „eine Bequemlichkeit für die Aufrufer
in `S2SStream` und in den Tests". `S2SStream` hat eine **eigene** Methode
gleichen Namens und ruft die der Sitzung nie — und die eigene schliesst den Stream
seit immer (`MarkClosed`). Die Sitzungs-Variante war die einzige Ausnahme im
Haus, und ausgerechnet sie trug denselben Namen wie die richtige Fassung daneben.
Das war die eigentliche Falle.

Übrig blieben zwei Aufrufer, beide Tests — und beide holten das Schliessen
unmittelbar danach mit `session.Kill()` von Hand nach. **Es gab also keinen
einzigen Aufrufer, der die Trennung brauchte.** Eine Wahl, die niemand trifft,
sollte die Schnittstelle nicht anbieten.

Deshalb keine dritte Methode, sondern eine weniger: `SendStreamErrorAsync` tut
jetzt beides, und `FailStreamAsync` aus D22 ist wieder weg. Damit heissen die
gleichnamigen Methoden in `XMPPSession` und `S2SStream` nicht nur gleich, sie tun
auch dasselbe.

Die zwei Tests sind um ihr `Kill()` leichter — und wahrer: Sie stellen jetzt einen
regelkonformen Server nach und nicht einen, der einen Fehler schickt und danach
getrennt den Socket wegzieht.

Drei Mutationen, alle erschlagen. Die aufschlussreiche ist die erste: Dass das
Schliessen wirklich geschieht, hält kein eigener Test, sondern
`RecoverableStreamError_IsReportedButAllowsReconnect` — ein Reconnect setzt
voraus, dass die Verbindung weg ist. Der Test stand lange da und hat seinen
zweiten Zweck erst jetzt bekommen.

**Die Lehre steht schon in D19 und wiederholt sich hier wörtlich:** Eine Liste,
die man aus dem Kopf schreibt, ist keine Bestandsaufnahme. Damals waren es neun
Fixtures statt elf, diesmal ein Aufrufer, den es nicht gab. Beide Male hätte ein
`grep` gereicht, und beide Male stand die falsche Angabe erst einmal im
Repository.

### D24. Die Probe gehört dem Server ✅ — und zwei Tests, die nichts prüften

Der letzte Punkt an Abschnitt 8.5: Presence von einem anderen Server. Für
verfügbare und unverfügbare Presence tut `RouteToAsync` bereits das Richtige — an
einen Bare-JID alle Resourcen (8.5.2.1.2), an eine Full-JID die passende
(8.5.3.1), sonst still ins Leere (8.5.1 und 8.5.3.2.2). Der Fehler steckte
woanders: **bei der Probe.**

Alle vier Abschnitte verweisen für `type='probe'` auf Abschnitt 4.3: Der Server
beantwortet sie selbst. Von der Gegenstelle kommend ging sie ins Routing und
landete beim Client — der bekam eine Stanza zu sehen, die nicht für ihn bestimmt
ist, und die fragende Gegenstelle bekam nie eine Antwort. Für einen hiesigen
Client wurde die Probe seit jeher beantwortet; dieselbe Asymmetrie wie bei
Nachricht (D15) und IQ (D16), und die letzte ihrer Art.

**Und die Gegenrichtung war ebenso kaputt, was ich vorher nicht wusste.** Der
lokale Probe-Zweig griff für *jedes* Ziel, fand für eine fremde Adresse kein
Konto und kehrte zurück — eine Probe an einen Kontakt auf einem anderen Server
verliess diesen Server also nie. Abschnitt 4.3.1 lässt den Server des Nutzers die
Probe hinausschicken; jetzt tut er das.

Aufgefallen ist das nur, weil ein Test scheiterte, den ich für richtig hielt.

**Zwei Tests haben bestanden, ohne zu prüfen, was ihr Name sagt — und beide aus
demselben Grund.** Mein neuer Test wartete darauf, dass Alice Bobs Zustand sieht,
nachdem sie eine Probe geschickt hat. Er bestand auch, bevor es die Umsetzung
gab. Der Grund ist ein Wettlauf: Bobs *erste* Presence wird verarbeitet, während
der Test den Roster-Eintrag setzt. Trifft sie ihn schon an, geht sie über die
gewöhnliche Verteilung an Alice — und der Test sieht Bobs Zustand, ohne dass je
eine Probe beantwortet wurde. Er wartet jetzt erst darauf, dass Bobs erste
Presence verarbeitet ist, und setzt den Roster danach.

Derselbe Wettlauf steckte im **vorhandenen** lokalen Probe-Test aus S-Zeiten:
`atBobs.Clear()` räumt weg, was die Anmeldung mitbringt — kommt es verspätet an,
zählt es als Antwort auf die Probe. Auch er bestünde bei einem Server, der Proben
gar nicht beantwortet. Er wartet jetzt erst auf die Zustellung der Anmeldung und
leert danach.

Sechs Mutationen, alle erschlagen — zwei davon erst nach diesen beiden
Testkorrekturen.

**Und eine Selbstkorrektur, die hierher gehört:** Auf dem Weg dahin habe ich das
Mutationsskript verdächtigt, weil dieselbe Mutation einmal als erschlagen und
einmal als überlebend gemeldet wurde, und ihm einen Zeitstempel-Fehler
unterstellt. Das war falsch — die Schwankung kam aus dem Wettlauf im Test. Ein
Werkzeug, das zweimal verschieden antwortet, ist ein naheliegender Verdächtiger;
naheliegend ist nicht dasselbe wie schuldig, und die Messung hat es geklärt, nicht
die Vermutung.

Nicht behoben und vermerkt: Eine Probe an ein unbekanntes Konto bleibt
unbeantwortet. Abschnitt 8.5.1 stellt `<unsubscribed/>` und Schweigen frei;
Schweigen verrät nicht, ob es das Konto gibt, und dabei bleibt es.

### D25. Weder Frage noch Antwort ✅ — Abschnitt 8.2.3, Regel 2

Der Punkt aus D16: Eine IQ-Stanza ohne `type` oder mit einem anderen Wert als
`get`, `set`, `result`, `error` bekommt `<bad-request/>`. Der eigentliche Inhalt
der Regel steckt in ihrem Nebensatz — sie verpflichtet „the recipient **or an
intermediate router**". Bei jeder anderen Stanza darf ein Server durchreichen und
den Empfänger urteilen lassen; hier nicht. Der Grund liegt in der Natur von IQ:
Ein Frage-Antwort-Paar hängt an `type` und `id`, und was keinen der vier Werte
trägt, ist weder Frage noch Antwort. Reicht jeder es weiter, wandert es durch das
Netz, und der Absender erfährt nie, was daraus wurde.

**Der Bestand war in drei Rollen verschieden falsch, und nur eine davon war
Schweigen.** An die Serveradresse gerichtet fiel die Stanza hinten aus
`HandleIqAsync` heraus. An eine fremde Domain ging sie hinaus — die Rolle des
Routers, ungeprüft. Und an einen hiesigen Empfänger wurde sie **zugestellt**:
`DeliverIqLocallyAsync` fragte nur, ob der Typ `result` oder `error` ist, und
behandelte alles übrige als Anfrage. Der Empfänger bekam damit etwas vorgelegt,
worauf er nach Regel 3 antworten müsste und worauf keine Antwort passt. Das war
der schlimmste der drei Fälle und zugleich der, der am ordentlichsten aussah.

Die Prüfung steht deshalb an beiden Eingängen ganz vorn — in `HandleIqAsync` vor
der Zustellweiche, in `AcceptFromRemoteAsync` vor allen Zustellzweigen. Ein Test
hält genau diese Stelle fest: `AnIqToTheServerItselfWithoutAType_IsRefused`
bestünde nicht, wenn die Prüfung im Zustellweg sässe, denn was an den Server
selbst geht, kommt dort nie vorbei.

**Und der Client hat dieselbe Regel in der anderen Rolle.** Er ist „the
recipient", und er tat gar nichts: Die Zuordnung zu einer offenen Frage nimmt nur
`result` und `error`, der Fallback am Ende fragt nach `get` oder `set` — ein
fünfter Wert fiel stillschweigend hindurch. Gegen diesen Server käme so etwas nie
bei ihm an; gegen eine fremde Implementierung ohne Regel 2 sehr wohl.

Die vier Werte stehen deshalb **einmal** im Haus, in `Jabber/Common/IqTypes.cs`.
Zwei Aufzählungen könnten auseinanderlaufen, und die Wirkung wäre still: Ein
Wert, den die eine Seite kennt und die andere nicht, käme je nach Weg durch oder
nicht.

**Zwei Entscheidungen, die keine Förmlichkeiten sind.**

Die Ablehnung geht auch **ohne `id`** hinaus und trägt dann keine. Das ist
bewusst anders als bei `RespondUnhandledIq`, das ohne `id` schweigt, und der
Unterschied liegt im Inhalt: Ein `<service-unavailable/>` beantwortet eine Frage,
und eine Antwort ohne `id` lässt sich keiner zuordnen — sie nützt niemandem.
`<bad-request/>` sagt etwas über die Stanza selbst, nämlich dass ihre Form nicht
stimmt, und das kann der Absender auch ohne Zuordnung brauchen; zumal die
fehlende `id` nach Regel 1 selbst dazugehört. Ein leeres `id=''` wäre der
schlechteste Ausgang — es gehört zu keiner Frage und sähe aus, als gehörte es zu
einer.

Absender ist **dieser Server**, nicht der gemeinte Empfänger.
`<service-unavailable/>` antwortet im Namen des Empfängers, weil der Server dort
für ihn geantwortet hat; hier hat er die Stanza gar nicht erst angenommen. Ein
Empfänger als Absender behauptete, jemand habe hineingesehen.

**Ein Kommentar wurde durch die Änderung falsch, und das ist die D21-Lehre in
ihrer harmloseren Form.** In `DeliverIqLocallyAsync` stand „oder ein unbekannter
Wert, den dieser Weg wie eine Anfrage behandelt, weil eine Antwort mehr taugt als
Schweigen". Das war richtig, solange es keine Prüfung davor gab, und mit ihr
beschreibt es einen Fall, der dort nicht mehr ankommt. Anders als in D21 war die
Begründung nicht schon beim Schreiben falsch — sie ist es geworden. Ein Kommentar
altert mit dem Code darunter, und beim Ändern gehört er mitgelesen.

Siebzehn Mutationen — je eine für jeden der vier Werte, die Prüfung an jedem der
drei Eingänge, die Antwort selbst, die beiden Attribute und die Fehlerart auf
beiden Seiten.

**Drei Nachträge aus dem Werkzeug, alle zum selben Thema — eine Messung, die
nicht misst, was sie zu messen vorgibt.**

Der Rücksetz-Check des Mutationsskripts prüfte gegen `HEAD`. Für eine **neue,
noch nicht getrackte** Datei zeigt `git diff` nie etwas, und die Prüfung meldete
deshalb auch nach einer sauberen Rücksetzung „PRUEFEN" — ausgerechnet bei der
einen Datei, die dieser Commit neu anlegt. Er vergleicht jetzt den Streuwert
gegen die Sicherung, und das ist die Frage, um die es geht: Steht wieder das da,
was vor der Mutation dastand?

Und die zweite Mutation lief 35 Minuten und musste abgebrochen werden. Fällt
`set` aus der Liste, bleibt das Resource Binding unbeantwortet — und
`ConnectAsync` wartet darauf **ohne eigene Frist**. Ein hängender Lauf ist kein
Ergebnis: Er sagt nicht, ob die Mutation überlebt hat, sondern nur, dass niemand
mehr antwortet. Die betroffenen Läufe bekommen jetzt `--blame-hang`, das daraus
einen Fehlschlag macht. Dass ein Client ohne Frist auf das Binding wartet, steht
unter „Später" — hier fiel es nur auf, weil eine Mutation den Fall erzeugt hat,
den ein Test nie erzeugt.

Und der dritte, der schlimmste: Nachdem ich den hängenden Lauf abgeschossen
hatte, blieb ein `testhost` zurück und **sperrte die Test-DLL**. Der nächste
`dotnet test` scheiterte damit nicht im Lauf, sondern schon im *Bau* (MSB3027) —
und das Skript filterte die Ausgabe auf „Fehler:" und „Bestanden!", fand nichts
und schrieb nichts. Sechs Mutationen sahen dadurch aus wie erledigt und waren
gar nicht gemessen. Aufgefallen ist es nur, weil eine Zeile ohne Urteil neben
Zeilen mit Urteil stand.

Zwei Änderungen daraus, und die erste ist die wichtigere: Findet das Skript keine
Zusammenfassung, gibt es die Rohausgabe aus, statt zu schweigen. **Ein Lauf ohne
Urteil darf nicht aussehen wie ein bestandener.** Und vor jedem Lauf räumt es
übriggebliebene `testhost`-Prozesse weg.

Die sechs sind wiederholt worden. Alle siebzehn Mutationen sind erschlagen.

**Und der vierte Nachtrag ist der lehrreichste, weil er kein Werkzeugfehler war,
sondern meiner.** Eine Mutation entfernt `result` aus der Liste. Ich habe ihren
Lauf auf **einen einzelnen Test** verengt — aus Vorsicht gegen einen Hänger wie
bei `set`. Ergebnis: bestanden, 21 Sekunden. Der Mutant hatte überlebt.

Nur stimmte die Vorsicht nicht. `set` hängt, weil der **Server** das Binding
ablehnt; `result` betrifft beim Client nur den Empfangspfad, und der
Verbindungsaufbau läuft daran vorbei — was die 21 Sekunden selbst bewiesen
haben. Die Verengung war also nicht nur unnötig, sie hatte genau den Test
entfernt, der die Mutation erschlägt: `TheFourKnownTypes_ReachTheResource` mit
dem Wert `result`. Mit dem vollen Filter fällt sie mit fünf Fehlern.

Damit hat ein *überlebender* Mutant eine fünfte Bedeutung bekommen, die in D14
bis D24 noch nicht vorkam: **Der Lauf hat den Test nicht ausgeführt, der ihn
erschlägt.** Die vier bekannten Bedeutungen — fehlende Prüfung, überflüssiger
Code, ein Test mit einer Reihenfolge, die nicht vorkommt, eine falsche Begründung
— setzen alle voraus, dass gemessen wurde, was gemessen werden sollte. Ein
verengter Filter verletzt genau diese Voraussetzung, und er tut es lautlos: Der
Lauf meldet „bestanden", nicht „nicht geprüft".

Der Preis für die Ehrlichkeit war hier hoch: Der volle Lauf brauchte 18 Minuten,
weil mit abgelehntem `result` fast jeder Test in seine Wartezeit läuft. Er war
ihn wert. Eine Abkürzung, die die Antwort ändert, ist keine Abkürzung.

### D26. Die Weiche riet ✅ — ein Name ist kein Präfix

Der Punkt aus D25, und er war grösser als sein Anlass. Die Weiche für eingehende
Rahmen verglich Präfixe: `StartsWith("<iq")` trifft auch `<iqbogus/>`,
`StartsWith("<presence")` auch `<presence-probe/>`, `StartsWith("<open")` auch
`<opencast/>`.

**Aufgefallen ist es am harmlosesten der drei Fälle.** In D25 fing der Server an,
einer IQ-Stanza mit unbrauchbarem Typ zu antworten — und antwortete damit auch
einem `<iqbogus/>`, also einem Element, das gar keine IQ-Stanza ist. Die
Nachlässigkeit war vorher genauso da, nur tat sie nichts Sichtbares. Eine
Prüfung, die anfängt zu antworten, macht die Weiche davor zum ersten Mal
beobachtbar.

Der eigentliche Schaden lag woanders: **Ein `<presence-probe/>` lief in die
Presence-Behandlung und galt dort als Anwesenheit.** Die liest ein fehlendes
`type` als „ist da" — ein Mensch wurde seinen Kontakten als online gemeldet, weil
sein Element zufällig mit denselben acht Zeichen beginnt. Eine Aussage über einen
Menschen, hergeleitet aus einem Zeichenkettenvergleich. Und ein `<opencast/>`
zählte als Stream-Eröffnung.

**Das Wissen war im Haus und lag an der falschen Stelle.**
`StreamManagementManager.IsCountableStanza` liest den Elementnamen seit jeher
vollständig, samt Behandlung des Namensraum-Präfixes — sie beantwortet nur eine
andere Frage (zählt der Rahmen für XEP-0198?) und stand deshalb der Weiche nie
zur Verfügung. Der Leser steht jetzt als `StanzaElement` in `Jabber/Common/`, und
`IsCountableStanza` ruft ihn auf.

Eine Stelle bleibt bewusst eigenständig: `XMPPSession.IsStanza`. Dort steht seit
langem der Vermerk, die Serverseite sei **absichtlich** unabhängig vom Client
implementiert — benutzten beide dieselbe Hilfsfunktion, prüften die Tests, die
die zwei Zähler gegeneinander halten, beide Seiten mit derselben Logik, und ein
gemeinsamer Denkfehler bliebe unentdeckt. Der Vermerk trägt, der Präfixvergleich
trug nicht: `<iqbogus/>` zählte auf der Serverseite mit und beim Client nicht.
Ausgerechnet die zwei Zähler, die gleich laufen müssen, wären auseinandergelaufen.
Sie liest den Namen jetzt über einen regulären Ausdruck — ein anderer Weg als
drüben, dieselbe Antwort — und ein neuer Test hält beide auf derselben Antwort,
ohne sie auf denselben Weg zu zwingen.

**Und was die Weiche nicht kennt, beendet jetzt den Stream** — RFC 6120,
Abschnitt 4.9.3.24: „a first-level child of the stream that is not supported by
the server". Bisher fiel so ein Rahmen stillschweigend hinten heraus, und das war
die bequeme Antwort und die schlechtere: Wer etwas schickt, das dieser Server
nicht kennt, wartet sonst auf eine Antwort, die nie kommt.

**Genau diese Strenge hat einen Test umgebracht, und der Fall ist der
interessanteste des Punktes.** `SendLockTests` schickt 200 Rahmen mit je 40 kB,
um den Sende-Lock des Clients und die Unversehrtheit der Rahmen zu messen. Als
Nutzlast diente ein erfundenes `<p/>` — **weil** es unbekannt ist und der Server
nichts damit tut. Der Gedanke war richtig: Der Rahmen soll folgenlos sein, damit
der Test misst, was er messen will. Der Weg dorthin trug nicht mehr, denn
„unbekannt" ist seit diesem Punkt nicht mehr folgenlos: Der erste der 200 Rahmen
riss die Verbindung für die übrigen 199.

Folgenlos ist jetzt anders erreicht — ein `iq` vom Typ `result` ohne Empfänger,
also eine Antwort an den Server auf nichts. Regel 4 aus Abschnitt 8.2.3 verbietet,
darauf zu antworten; sie wird angenommen, aufgezeichnet und fallen gelassen.
Dieselbe Folgenlosigkeit, mit einem Element, das es im Protokoll gibt.

Beim Umbau kam noch etwas heraus, das der alte Rahmen verdeckt hatte: Der Client
setzt auf jede **Stanza** den Namensraum `jabber:client` (RFC 7395, Abschnitt
3.3.3). Das `<p/>` bekam ihn nie, weil es keine Stanza ist — der neue Rahmen
kommt also anders an, als er abgeschickt wurde. Der Test legt die Reihenfolge der
Attribute deshalb nicht mehr fest, wohl aber den Rahmen als Ganzes, und das ist
genau seine Frage.

**Nicht geändert wurde der S2S-Stream.** Er bekommt dieselbe Lesung des
Elementnamens, aber nicht die neue Endgültigkeit. Der Unterschied ist keine
Bequemlichkeit, sondern eine Frage der Kenntnis: Auf dem Client-Stream sprechen
beide Seiten dasselbe, dort steht eine fremde Implementierung gegenüber, und was
Prosody oder ejabberd sonst noch schicken, ist **nicht erhoben**. Einen Stream
abzubrechen, weil man ein Element nicht kennt, wäre gegenüber ihnen eine Wette.
Das steht unter „Später" — zu messen, nicht zu vermuten.

Nebenbei fällt dort eine Verzweigung weg: `<stream:features/>` und `<features/>`
waren zwei Zweige und sind ein Element; welches Präfix an den Streams-Namensraum
gebunden ist, steht dem Server frei (Abschnitt 4.8.1).

**Und noch einmal das Thema von D25, diesmal am gröbsten.** Eine Mutation hing —
ohne `iq` gibt es kein Resource Binding, und der Client wartet ohne eigene Frist
(derselbe Punkt wie in D25, zum zweiten Mal). Ich habe die `testhost`-Prozesse
abgeschossen. Das beendet den laufenden `dotnet test`, **nicht das Skript
darüber**: Der alte Durchgang lief mit der nächsten Mutation weiter, während der
neue schon mutierte. Zwei Skripte schrieben dieselben Dateien, und die Zahlen
gingen sichtbar auseinander — 14 Tests hier, 20 dort, für dieselbe Frage.

Der Hash-Vergleich gegen die Sicherung hat danach eine Datei gefunden, die noch
eine Mutation trug. **Genau dafür ist er da**, und er war es, der den Baum vor
einem Commit mit angewandter Mutation bewahrt hat — nicht meine Aufmerksamkeit.

Die Lehre ist nicht „besser aufpassen", sondern: Wer einen Prozess abschiesst,
muss wissen, wer sein Elternteil ist. Ein Kindprozess zu beenden sieht aus wie
ein Abbruch und ist nur eine Unterbrechung — der Auftrag darüber läuft weiter,
und ab da misst niemand mehr, was er zu messen glaubt.

Sechzehn Mutationen, alle erschlagen — zehn am Leser, sechs an der Weiche und an
den beiden Zählern. Die zehn am Leser laufen ohne Netz und brauchen zusammen
weniger als eine Minute; die Weiche kostet ihre Zeit, weil sie einen Server
verlangt. Suite: 685 Tests, 0 Fehler, 7 übersprungen, gegen Prosody und
ejabberd.

### D27. Erst messen, dann streng werden ✅

Der Punkt aus D26, und er war ausdrücklich als **Messung** vermerkt und nicht als
Änderung: Der S2S-Stream liess ein unbekanntes Element liegen, während die
Client-Verbindung es seit D26 mit `<unsupported-stanza-type/>` abweist. Der Grund
für das Zögern stand dabei — auf dem Client-Stream sprechen beide Seiten dasselbe,
auf dem S2S-Stream steht eine fremde Implementierung gegenüber, und was Prosody
und ejabberd dort sonst noch schicken, war nicht erhoben. Einen Stream
abzubrechen, weil man ein Element nicht kennt, wäre eine Wette gewesen.

**Also zuerst der Fühler.** An die Stelle, an der ein Rahmen durch alle Zweige
fällt, kam eine befristete Aufzeichnung — jeder unbekannte Rahmen mit Richtung
und Domain in eine Datei. Dann der volle Lauf gegen beide Gegenstellen.

Ergebnis: **kein einziger Rahmen.** 685 Tests, Dialback, SASL-EXTERNAL, Bidi,
Stream Management, TCP und WebSocket — nichts fiel durch.

**Zwei Dinge haben diese Messung erst brauchbar gemacht, und beide wären leicht
zu übergehen gewesen.**

Der erste Versuch lief nur gegen Prosody: ejabberd war zwischendurch
weggefallen, und 15 statt 7 übersprungene Tests haben es verraten. Ohne die
bekannte Grundlinie hätte „nichts gefunden" wie ein Ergebnis ausgesehen und wäre
die halbe Messung gewesen. Ebenso die Richtung: Die eingehenden Tests laufen nur
**innerhalb** von WSL, und genau dort wählt der fremde Server an und spricht
zuerst. Sie sind einzeln nachgeholt worden.

Und dann die Frage, die den Rest wertlos gemacht hätte: **Schlägt der Fühler
überhaupt an?** Über die gesamte Sammlung hat er kein einziges Mal ausgelöst —
das ist genau das Bild, das ein kaputter Fühler auch abgibt. Belegt hat es erst
der neue Test: Er speist drei unbekannte Elemente ein, und der Fühler hat alle
drei aufgezeichnet. Ein Nachweis über eine Abwesenheit ist nur so viel wert wie
der Nachweis, dass die Anwesenheit sichtbar gewesen wäre.

Damit ist die Strenge belegt statt vermutet, und der S2S-Stream hält jetzt
dieselbe Regel wie die Client-Verbindung. Der volle Lauf gegen beide
Gegenstellen ist zugleich die stehende Gegenprobe: Schickt eine von ihnen doch
etwas Unbekanntes, stirbt der Stream und die Föderationstests fallen.

**Eine Zeile aus D26 war dabei zu weit gegriffen.** Dort beendete *jeder* Rahmen
den Stream, den die Weiche nicht zuordnen konnte — auch ein leerer. Abschnitt
4.9.3.24 spricht aber von „a first-level child of the stream that is not
supported", und ein leerer Rahmen ist kein Kind, das nicht unterstützt wird; er
ist kein Kind. Über TCP fällt das nicht auf, weil `SkipProlog` im Zerleger
Leerraum, XML-Deklarationen und Kommentare ohnehin schluckt — und Leerraum als
Keepalive ist auf einem Stream ausdrücklich erlaubt (Abschnitt 4.6.1). Über
WebSocket wird jeder Frame durchgereicht, und dort hätte ein leerer Frame die
Verbindung gekostet. Beide Wege unterscheiden jetzt.

Fünf Mutationen, alle erschlagen. Drei davon brachen zuerst ab, und der Abbruch
war eine Fundstelle: `S2SStream.cs` hat **LF**-Zeilenenden, das Repository ist
gemischt, und ein mehrzeiliges Suchmuster passte deshalb nur zufällig. Das
Mutationsskript versucht jetzt beide Varianten — und behält beim Zurückschreiben
die vorgefundene Kodierung, statt einer LF-Datei stillschweigend ein BOM zu
verpassen. Immerhin war dieser Fehlschlag laut; die drei stillen aus D25 waren
teurer.

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
