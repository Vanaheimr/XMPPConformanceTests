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
Aktueller Stand der Suite: **611 Tests, 0 Fehler** in gut drei Minuten, und
seit dem Default-Umstieg läuft sie mit ausgehandeltem XEP-0198. Übersprungen
wird, was ohne fremde Gegenstelle nichts zu prüfen hat — sechs Föderationstests,
die nur innerhalb von WSL laufen können — sowie einer, der eine Eigenschaft
prüft, die es nur im STARTTLS-Betrieb gibt.
Fünf benannte Ausnahmen, wo eine Mutation grün bleibt: die zwei Zeilen
im WebSocket-Verbindungsabbau (siehe S4b-2), der Vergleich in
`DialbackKey.Verify` über `FixedTimeEquals` (ein Timing-Seitenkanal ist
funktional nicht beobachtbar), die Slot-Identität im Verbindungs-Cache
(siehe S4b-3), der Zeitpunkt der SASL-Anheftung (siehe D1) und die Abkürzung
über die leere Offline-Ablage (siehe D14). Es waren sechs: Die Herkunftsfrage
vor den `<sent>`-Carbons im Offline-Zweig (D15) überlebte nur, weil ihr Wurf im
`catch` beim Verarbeiten eines Frames verschwand — seit D18 wird er gemeldet,
und sechs Tests erschlagen die Mutation.

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
`S2SStream` und in den Tests.

---

## Später

### Protokoll
- Eine IQ-Anfrage von einer Gegenstelle an die eigene Serveradresse
  (disco#info, Ping) bleibt unbeantwortet, obwohl RFC 6120 §8.2.3 Regel 3 eine
  Antwort verlangt. Die Antworten stehen in `HandleIqAsync` und wollen eine
  Client-Sitzung (siehe D16)
- RFC 6120 §8.2.3 Regel 2: Eine IQ-Stanza mit fehlendem oder unbekanntem `type`
  soll mit `<bad-request/>` beantwortet werden, ausdrücklich auch von einem
  „intermediate router". Der Zustellweg behandelt sie wie eine Anfrage; eine
  vollständige Antwort gehört an die Frame-Ebene für alle IQs (siehe D16)
- XEP-0160: eine Nachricht mit ausschliesslich XEP-0085-Inhalt soll nicht
  abgelegt werden; dieser Client schickt keine, die Regel wäre ungetestet
  (siehe D14)
- RFC 8264: die Zugehörigkeit eines Codepoints zur IdentifierClass bzw.
  FreeformClass ist angenähert (Kategorie + Kompatibilitätszerlegung) statt aus
  den abgeleiteten Eigenschaften bestimmt; IDNA2008 für Domain-Labels fehlt ganz
  (siehe D5)

### XEPs
- XEP-0030: die eigene disco#info-Antwort setzt kein `node`-Attribut
- XEP-0060: IQ-Ergebnisse korrelieren, Fehler nicht mehr verschlucken

### Transport
- Endpunkt-Discovery über XEP-0156/`host-meta` statt fest `wss://<domain>:5443/ws`
- `XMPPConnection.CreateTcp` erzeugt eine `tcp://`-URI, die `ClientWebSocket`
  ablehnt — entweder echt implementieren oder entfernen

### Testsammlung
- `TheStreamSurvivesABrokenConnection` gegen einen Fremdserver scheiterte in
  einem von vier Vollläufen mit einer Zeitüberschreitung, allein aber 4 von 4
  Mal grün. Verdacht: 15 Sekunden für Wiederverbindung samt Wiederaufnahme sind
  unter Last des vollen Laufs mit exponentiellem Backoff knapp. Undiagnostiziert
  — vor einer Änderung der Wartezeit gehört geklärt, ob wirklich der Backoff
  bremst oder die Gegenstelle den Stream früher aufgibt (siehe D16)

### Fehlerbehandlung
- `SendStreamErrorAsync` schickt den Fehler, ohne den Stream zu schliessen —
  Abschnitt 4.9.1.1 verlangt beides. Die Aufrufer in `S2SStream` und die Tests
  nutzen es bewusst so; wer den Stream beenden will, nimmt `FailStreamAsync`.
  Sauber wäre, die Aufrufer durchzusehen und die Trennung dann zu entfernen
  (siehe D22)
- Die Verdrahtung der Wache ist eine mechanische Eigenschaft und von keinem Test
  gehalten: Nähme jemand in einem einzelnen Fixture das `AssertClean()` heraus,
  fiele es nicht auf. Gesichert ist sie durch die Quelltextprüfung „kein
  `new XMPPServer(` ohne `Watched(…)`" (siehe D19)

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
