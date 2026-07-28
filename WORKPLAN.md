# Arbeitsplan

Was an Client und Server offen ist, in welcher Reihenfolge es sinnvoll ist und
warum. Die ausführliche Beschreibung der einzelnen Lücken steht in
[Jabber/README.md](Jabber/README.md) — hier steht nur, was zu **tun** ist.

Stand: 2026-07-27

---

## Erledigt

| Was | Beleg |
|-----|-------|
| Aufteilung in eine Klasse pro Datei, einheitlicher Namespace, Lizenzheader | `e42c684` |
| `XMPPClient` als echte Client-Klasse, `Program.cs` nur noch Konsolen-UI | `e42c684` |
| `ILogger` statt `Console.WriteLine` in der Bibliothek | `e42c684` |
| Sende-Lock, CTS-Leak, Roster-Push-Prüfung, Close-Handshake-Timeout | `e42c684` |
| `Jabber.Tests` mit `XMPPServer` als Gegenstelle, Mehr-Client-Szenarien | `e42c684` |
| SCRAM- und Caps-Testvektoren aus RFC 5802/7677 und XEP-0115 | `e42c684` |
| SCRAM `ExtractValue` verankert, Caps-Sortierung oktettweise | `78fdb1c` |
| XEP-0198 zählt korrekt (beide Richtungen, Nonzas, Überlauf) | `78fdb1c` |
| `XMPPServer` ins Hauptprojekt, „Fake" aus den Typnamen | `78fdb1c` |
| `#region Usings` in allen Dateien | `78fdb1c` |
| RFC 6120 §8.2.3: unbeantwortete IQs bekommen `<service-unavailable/>` | `87f3dd6` |
| RFC 6120 §8.3/§4.9: Stanza- und Stream-Fehler werden ausgewertet | `0249de1` |
| Stanza-Rahmen und Roster über `XElement` statt Regex | `15a11aa` |
| `message`- und `presence`-Nutzlasten über `XElement` (XEP-0085/0115/0184/0280/0333) | `107aa87` |
| `iq`-Nutzlasten über `XElement` (XEP-0030/0060/0199); Rohtext-Parameter entfallen | `39cb6fb` |
| Aufbauphase entwirrt: IQ-Korrelation statt Verwerfen, Aushandlung über `XElement` | `cc9dccb` |
| S3: Presence nur an Subscriber, Presence-Probe, Zustand beim Anmelden | `4fe23cd` |
| S3c: Abmeldung beim Verbindungsende, auch bei Abriss | `fdb8c3b` |
| S3b: Subscription-Handshake, Roster-Set lässt die Subscription in Ruhe | `590d38c` |
| Client wertet `subscribed`/`unsubscribed`/`unsubscribe` aus, statt sie als Anwesenheit zu lesen | `a5bc49d` |
| Resource einstellbar, `<conflict/>` führt zu einem zweiten Bind ohne Wunsch | `2f6f830` |
| Ein Test verbrachte sechs Minuten in zwanzig Reconnects | `4a2b3b6` |
| S1: Transport auf Hermods WebSocket-Server, Server spricht `wss://` | `a92583e`, `b97db5e`, `2ebc805` |
| S2: Zugangsdaten abgeleitet statt im Klartext, SCRAM auf dem Server, Kontenspeicher | `d54dacb`, `c35ae85`, `d29dc3c` |
| Abmeldung wurde als letzte Presence gemerkt und nachgeliefert — Ursache des sporadischen Fehlschlags | `bccf648` |
| S4: Domain-Weiche, Fehlerpfad, Föderation zweier Server (ohne echten Transport) | `d9c4333`, `323795f` |
| S4b-1: S2S-Protokollschicht ohne Transport (`S2SStream`) | `f0a4bbd` |
| S4b-2: WebSocket-S2S über echte Sockets samt TLS | `8e0aec3` |
| S4b-3: Dialback (XEP-0220) gegen den Vektor des XEP, Domain belegt statt behauptet | `c92560d`, `a60631c` |
| S4b-4: Rahmung austauschbar, XML-Zerleger, TCP mit `jabber:server`-Streams | `a24d1f2`, `e0d88f4` |
| S4b-6: STARTTLS (RFC 6120 §5.4) samt Downgrade-Schutz | `f4a9c80` |
| S4b-7: SASL-EXTERNAL (XEP-0178) über das TLS-Zertifikat | `031f8ca` |
| S4b-8: SRV-Auflösung (RFC 6120 §3.2, RFC 2782) | `0d1391f` |
| S5: Domainübergreifende Subscriptions (RFC 6121 §3) | `a94b416` |
| S6: Subscription-Pre-Approval (RFC 6121 §3.4) | *(dieser Commit)* |

Jede dieser Korrekturen ist durch Mutationstests abgesichert: Fix zurückgedreht,
geprüft dass genau die zuständigen Tests fehlschlagen, Fix wieder eingesetzt.
Aktueller Stand der Suite: **464 Tests, 0 Fehler** in knapp drei Minuten, und
seit dem Default-Umstieg läuft sie mit ausgehandeltem XEP-0198. Übersprungen
wird, was ohne fremde Gegenstelle nichts zu prüfen hat — acht Föderationstests
gegen Prosody und ejabberd, vier XEP-0198-Tests gegen Prosody — sowie einer,
der eine Eigenschaft prüft, die es nur im STARTTLS-Betrieb gibt.
Drei benannte Ausnahmen, wo eine Mutation grün bleibt: die zwei Zeilen
im WebSocket-Verbindungsabbau (siehe S4b-2), der Vergleich in
`DialbackKey.Verify` über `FixedTimeEquals` (ein Timing-Seitenkanal ist
funktional nicht beobachtbar) und die Slot-Identität im Verbindungs-Cache
(siehe S4b-3).

---

## Der Server soll ein richtiger Server werden

`XMPPServer` ist als Gegenstelle für Tests entstanden. Er soll das Image des
reinen Testservers verlieren — dafür fehlten drei Dinge, das erste davon ist
jetzt erledigt, und ein viertes wäre der Beweis, dass es funktioniert. Die
vollständige Lückenliste steht in
[Jabber/README.md](Jabber/README.md#was-dem-server-zum-produktivbetrieb-fehlt).

### S1. TLS ✅

Erledigt. Der Server spricht `wss://` mit einem selbst signierten Zertifikat,
wie RFC 6120 §5 es verlangt; die gesamte Suite läuft darüber. Umgesetzt in vier
Schritten: `a92583e` (Referenz auf Hermod), `b97db5e` (Transport),
`2ebc805` (TLS), plus `4a2b3b6` als Zwischenfund.

Den Transport liefert Hermods `AWebSocketServer` — `HttpListener` und die
selbstgeschriebene Empfangsschleife sind weg. `XMPPServer` erbt ihn nicht,
sondern hält eine private Ableitung, die `ProcessTextMessage` überschreibt; so
bleibt seine öffentliche Oberfläche klein und alle Tests kompilierten
unverändert weiter.

Was beim Umbau anders lag als erwartet:

- Nicht eine Namenskollision, sondern zwei: neben `WebSocket` auch `IPAddress`.
  Beide Aliase müssen **innerhalb** der Namespace-Deklaration stehen — auf
  Ebene der Compilation Unit gewinnt das Namespace-Member.
- Empfangen läuft nicht über `OnTextMessageReceived`, sondern über die
  überschriebene Methode `ProcessTextMessage`; das Ereignis gehört der
  Beispielklasse `WebSocketMirrorServer`, nicht der Basisklasse.
- Der Konstruktorparameter heißt `TCPPort`, nicht `HTTPPort`.
- Close, Ping und Subprotokoll-Aushandlung waren **kein** Problem — die
  Suite war beim ersten vollen Lauf grün. Die einzige echte Abweichung: Hermod
  beantwortet ein Close-Frame immer und bietet keinen Schalter dagegen.
  `CompleteCloseHandshake = false` verzögert die Antwort daher, statt sie zu
  unterdrücken.

**Was daran offen blieb:**

- Kein STARTTLS (RFC 6120 §5.4), und TLS ist nicht erzwungen — wer den Server
  mit `useTLS: false` baut, bekommt weiter `ws://`.
- Das Zertifikat ist selbst signiert und wird zur Laufzeit erzeugt. Für den
  Betrieb bräuchte es einen Weg, ein eigenes zu hinterlegen.
- Der ursprüngliche Nebeneffekt steht noch aus: der Server bietet weiterhin
  nur PLAIN an, also ist der SCRAM-Pfad des Clients nach wie vor nur gegen die
  RFC-Vektoren getestet.

### S2. Dauerhafte Kontenverwaltung ✅

Erledigt in drei Schritten: `d54dacb` (Zugangsdaten), `c35ae85` (SCRAM auf dem
Server), `d29dc3c` (Kontenspeicher).

Passwörter liegen nicht mehr im Klartext, sondern als das, was RFC 5802 §3
dafür vorsieht: Salt, Iterationszahl und je Mechanismus `StoredKey` und
`ServerKey`. `IXMPPAccountStore` trägt Konten und Roster; `InMemoryAccountStore`
bleibt die Vorgabe, `FileAccountStore` schreibt eine JSON-Datei.

**Der Nebeneffekt aus S1 ist damit eingelöst:** der Server bietet
SCRAM-SHA-256, SCRAM-SHA-1 und PLAIN an, und weil der Client von sich aus den
stärksten nimmt, läuft die gesamte Suite über SCRAM-SHA-256. Der SCRAM-Pfad des
Clients ist damit zum ersten Mal integrativ geprüft — insbesondere seine
Prüfung der Serversignatur, für die es zuvor keinen Test gab, der ihr Versagen
bemerkt hätte.

**Was daran offen blieb:**

- **Kein Channel Binding** (`SCRAM-SHA-*-PLUS`). Der GS2-Header wird auf
  Übereinstimmung geprüft, mehr verlangt RFC 5802 §6 von einem Server ohne
  Channel Binding auch nicht.
- **Ein unbekanntes Konto wird abgelehnt, bevor der Austausch beginnt.** Damit
  verrät der Server, ob es ein Konto gibt; RFC 5802 §7 empfiehlt, mit einem
  erfundenen Salt weiterzumachen.
- **Die Kontendatei ist unverschlüsselt** und ihre Zugriffsrechte werden nicht
  gesetzt. Die abgelegten Schlüssel sind keine Passwörter, erlauben aber, eine
  Anmeldung zu prüfen.
- **Kein Anlegen von Konten über XMPP** (XEP-0077 In-Band Registration) und
  keine Passwortänderung.
- Die Iterationszahl steht auf 4096, der Untergrenze aus RFC 7677 §4. Für den
  Betrieb zu wenig; je Konto einstellbar.

### S3. Presence nur an Subscriber ✅

Erledigt. Ungerichtete Presence geht nur noch an Kontakte mit `from` oder
`both` und an die eigenen weiteren Resourcen; dazu kommen Presence-Probes und
das Nachliefern des Kontaktzustands beim Anmelden.

### S3b. Subscription-Handshake (RFC 6121 §3) ✅

Erledigt. Die vier Schritte ändern die Roster beider Seiten und lösen
Roster-Pushes aus; `ask='subscribe'` hält eine offene Anfrage fest. Nach der
Annahme geht die aktuelle Presence sofort an den Antragsteller (§3.1.5), nach
einem Entzug ein `unavailable` (§3.2.2). Ein Roster-Set fasst den
Subscription-Zustand nicht mehr an (§2.3).

Was daran offen blieb:
- ~~Pre-Approval (§3.4) fehlt~~ ✅ erledigt in S6.
- ~~Eine Anfrage an ein gerade nicht verbundenes Konto wird nicht aufbewahrt
  (§3.1.3)~~ ✅ erledigt in S7.

### S3c. `unavailable` beim Verbindungsende ✅

Erledigt. Endet eine Sitzung — ordentlich, abgerissen oder an einer Ausnahme —,
meldet der Server die Resource bei denselben Empfängern ab, die auch ihre
Anmeldung bekommen haben. Hat der Client sich selbst abgemeldet, unterbleibt
die Wiederholung.

### S4. Zwei Server, zwei Clients, eine Nachricht ✅ (Routing) / ⚠️ (Transport)

Das Zielbild steht: zwei `XMPPServer`-Instanzen mit verschiedenen Domains, an
jeder ein echter `XMPPClient`, und eine Nachricht geht von einem zum anderen —
samt Antwort zurück und samt Presence. Erledigt in zwei Schritten: `d9c4333`
(Domain-Weiche und Fehlerpfad), `323795f` (Föderation).

**Entschieden:** erst Routing und Adressierung, der Transport später. `IServerLinks`
ist die Stelle, an der er eingesetzt wird; `DirectServerLinks` verbindet zwei
Server im selben Prozess.

Was dabei mitkam: eine Stanza an eine fremde Domain verschwand bisher
spurlos. Jetzt kommt `<remote-server-not-found/>` zurück (RFC 6120 §10.4.3,
Bedingung aus §8.3.3).

**Was offen bleibt — und der Grund, warum das hier kein ✅ allein ist:**

- **Es gibt keinen echten Transport.** `DirectServerLinks` hat keinen Stream,
  kein TLS, keinen Dialback und keine Authentifizierung: die Domain, für die
  eine Gegenstelle sprechen darf, wird schlicht behauptet. Für den Betrieb ist
  das nichts.
- **Kein Dialback (XEP-0220) und kein SASL-EXTERNAL.** Die Absenderprüfung im
  Eingang ist da und scharf — sie ist genau das, worauf ein echter Transport
  danach baut —, aber es gibt nichts, was die Behauptung der Gegenstelle
  belegt.
- ~~Domainübergreifende Subscriptions fehlen~~ ✅ **erledigt (S5).** Der
  Handshake läuft jetzt auch über die Grenze.
- ~~Keine Auflösung über DNS~~ — erledigt in S4b-8.

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
Verbindung. Wer sich fälschlich für `links.example` ausgibt, wird deshalb nie
selbst gefragt; gefragt wird der echte `links.example`, und der kennt den
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

**Offen geblieben:** ob ejabberd unsere Ankündigung tatsächlich annimmt, wenn
*es* uns anwählt, ist nicht beobachtet — nur aus seinem Quelltext geschlossen.
Zu sehen wäre es erst, wenn `TcpServerLinks` das Anbieten vom Benutzen trennte
(heute schaltet `UseBidirectionalStreams` beides zugleich). Solange unsere
ausgehende Verbindung Bidi nutzt, wählt ejabberd uns gar nicht erst an.

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

**Offen geblieben — die Gegenrichtung an der Domain-Grenze.** Was von einem
fremden Server hereinkommt, steht in `jabber:server` und wird unverändert an
den lokalen Client zugestellt. Unser Client stört sich nicht daran, weil er
Stanzas am lokalen Namen erkennt — genau die Nachsicht, die den ersten Fehler
verdeckt hat. Ein fremder Client dürfte strenger sein. Nicht mitgemacht, weil
kein Lauf es zeigt und ein Eingriff ohne Beleg nur Rauschen wäre; die Stelle
ist `RouteToAsync`, lokaler Zweig.

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

**Nicht abgedeckt:** eine Stanza, die der Client erfolgreich abschickt und die
den Server nie erreicht. Im selben Prozess gibt es diesen Fall nicht — ein
abgerissener Socket lässt das Senden sofort scheitern, und eine nicht gesendete
Stanza wird gar nicht erst mitgezählt. Der Code dafür (`ResendUnackedAsync`,
ohne erneutes Mitzählen) ist da und ungeprüft.

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

---

## Später

### Protokoll
- Message-Typen `chat`/`error`/`groupchat` unterscheiden
- Roster-Versionierung nutzen (`Roster.Version` und `RosterStanzaBuilder.GetRoster` liegen ungenutzt herum)
- SASL-Downgrade-Schutz: gewählten Mechanismus pinnen statt blind der Server-Ankündigung folgen
- RFC 7622: kein PRECIS, und der Resourcepart wird beim Vergleich fälschlich kleingeschrieben
- SASLprep ist auf NFKC reduziert — für Nicht-ASCII-Passwörter falsch

### XEPs
- XEP-0115: Caps-Cache verifiziert den Hash der Antwort nicht (der Cache ist damit vergiftbar)
- XEP-0115: XEP-0128-Datenformulare fehlen im Verification String
- XEP-0030: die eigene disco#info-Antwort setzt kein `node`-Attribut
- XEP-0060: IQ-Ergebnisse korrelieren, Fehler nicht mehr verschlucken

### Transport
- Endpunkt-Discovery über XEP-0156/`host-meta` statt fest `wss://<domain>:5443/ws`
- `XMPPConnection.CreateTcp` erzeugt eine `tcp://`-URI, die `ClientWebSocket`
  ablehnt — entweder echt implementieren oder entfernen

### Server (`Jabber/Server/`)
Die grossen Brocken stehen oben unter [S1 bis S4](#der-server-soll-ein-richtiger-server-werden).
Was dort nicht auftaucht und trotzdem ansteht:
- XEP-0198 `<resume/>` beantworten — die Gegenprobe zu Punkt 2
- SCRAM anbieten, damit der SCRAM-Pfad des Clients integrativ geprüft wird und
  nicht nur gegen die RFC-Vektoren (setzt S1 voraus)
- Stanza-Fehler auch dort erzeugen, wo heute kein Schalter dafür existiert

### Struktur
- `Jabber.Tests/XMPP/` nach `HermodTests/XMPP/` verschieben. Bewusst aufgeschoben;
  Namespaces, Ordnerschnitt und der doppelte `InternalsVisibleTo`-Eintrag in
  `Jabber.csproj` sind bereits darauf ausgelegt, dass das eine Kopie wird.
- Konsolen-UI und Logger trennen: der Standard-Konsolenlogger schreibt in
  dieselbe Konsole wie die Eingabezeile und zerlegt den Prompt. Ein eigener
  `ILoggerProvider` über die synchronisierte Ausgabe wäre die saubere Lösung.
- Ungenutzte öffentliche Member entscheiden: benutzen oder streichen. Liste in
  [Jabber/README.md](Jabber/README.md).

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
