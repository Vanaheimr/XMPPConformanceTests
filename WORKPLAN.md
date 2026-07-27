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
| S4b-3: Dialback (XEP-0220) gegen den Vektor des XEP, Domain belegt statt behauptet | `c92560d` |

Jede dieser Korrekturen ist durch Mutationstests abgesichert: Fix zurückgedreht,
geprüft dass genau die zuständigen Tests fehlschlagen, Fix wieder eingesetzt.
Aktueller Stand der Suite: **298 Tests, 0 Fehler, 0 übersprungen** in gut einer
Minute. Drei benannte Ausnahmen, wo eine Mutation grün bleibt: die zwei Zeilen
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
- **Pre-Approval** (§3.4) fehlt.
- Eine Anfrage an ein gerade nicht verbundenes Konto wird nicht aufbewahrt
  (§3.1.3) — sie geht verloren statt beim nächsten Anmelden zuzustellen.

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
- **Domainübergreifende Subscriptions fehlen.** Der Handshake aus RFC 6121 §3
  nimmt an, dass beide Seiten lokal sind. Ein bestehender Eintrag wird über die
  Grenze beachtet, ein neuer lässt sich nicht aushandeln.
- **Keine Auflösung über DNS** (SRV-Records nach RFC 6120 §3.2) — Gegenstellen
  werden von Hand eingetragen.

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
seit S4b-3 eine belegte Gegenstellendomain. Was weiterhin fehlt: SASL-EXTERNAL
als Alternative zu Dialback, die Auflösung über SRV statt über eine
Konfigurationsliste, das Verhalten, wenn zwei Server einander gleichzeitig
anwählen (doppelte Verbindungen), und welcher Transport gewählt wird, wenn eine
Domain über beide erreichbar wäre.

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

**S4b-4 (offen): TCP 5269 als zweite Rahmung.** `S2SStream` sollte dafür
unverändert bleiben — wenn nicht, war die Trennung in S4b-1 nicht sauber genug.
Voraussetzung für Föderation mit ejabberd oder Prosody.

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

## Als Nächstes (Client)

### XEP-0198 gegen einen echten Server, dann Default umstellen

Die Zählung stimmt gegen `XMPPServer`. Es fehlt ein Lauf gegen ejabberd oder
Prosody; danach kann `StreamManagementEnabled` auf `true`.

Anschließend Stream-Resume: `ResumeAsync` und `GetUnackedStanzas` existieren,
werden aber nirgends aufgerufen — nach einem Reconnect baut der Client neu auf
und die unbestätigten Stanzas gehen verloren. Der `XMPPServer` beherrscht
`<resume/>` ebenfalls noch nicht, das wäre gleich mitzumachen.

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
