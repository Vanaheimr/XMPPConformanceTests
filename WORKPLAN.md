# Arbeitsplan

Was an Client und Server offen ist, in welcher Reihenfolge es sinnvoll ist und
warum. Die ausführliche Beschreibung der einzelnen Lücken steht in
[Jabber/README.md](Jabber/README.md) — hier steht nur, was zu **tun** ist.

Stand: 2026-07-26

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
| S3b: Subscription-Handshake, Roster-Set lässt die Subscription in Ruhe | offen im Working Tree |

Jede dieser Korrekturen ist durch Mutationstests abgesichert: Fix zurückgedreht,
geprüft dass genau die zuständigen Tests fehlschlagen, Fix wieder eingesetzt.
Aktueller Stand der Suite: **196 Tests, 0 Fehler, 0 übersprungen**.

---

## Der Server soll ein richtiger Server werden

`XMPPServer` ist als Gegenstelle für Tests entstanden. Er soll das Image des
reinen Testservers verlieren — dafür fehlen drei Dinge, und ein viertes wäre
der Beweis, dass es funktioniert. Die vollständige Lückenliste steht in
[Jabber/README.md](Jabber/README.md#was-dem-server-zum-produktivbetrieb-fehlt).

### S1. TLS

Der Listener spricht `http://` beziehungsweise `ws://`. RFC 6120 §5 verlangt
`wss://` mit Zertifikat. Ohne das ist jeder andere Punkt hier akademisch, weil
Passwörter im Klartext über die Leitung gingen.

**Umfang:** mittel. `HttpListener` kann TLS nur über eine im System registrierte
Zertifikatsbindung (`netsh http add sslcert`), was schlecht zu Tests passt —
realistischer ist der Wechsel auf `TcpListener` mit `SslStream` oder auf Hermods
eigenen HTTP-Server, der ohnehin im Repo liegt.
**Nebeneffekt:** erst damit lässt sich SCRAM sinnvoll ergänzen, und damit auch
der SCRAM-Pfad des Clients integrativ testen statt nur gegen die RFC-Vektoren.

### S2. Dauerhafte Kontenverwaltung

Konten und Roster leben im Speicher einer `XMPPServer`-Instanz und sind beim
Beenden weg. Passwörter liegen im Klartext.

**Umfang:** mittel. Eine Persistenzschnittstelle (`IAccountStore` o.ä.) mit
einer In-Memory-Implementierung für Tests und einer dateibasierten für den
Betrieb. Passwörter gehören dann als SCRAM-Salted-Password abgelegt, nicht im
Klartext — was S1 voraussetzt, weil PLAIN sonst der einzige Mechanismus bleibt.

### S3. Presence nur an Subscriber ✅

Erledigt. Ungerichtete Presence geht nur noch an Kontakte mit `from` oder
`both` und an die eigenen weiteren Resourcen; dazu kommen Presence-Probes und
das Nachliefern des Kontaktzustands beim Anmelden.

Was dabei offen blieb und jetzt der nächste Schritt ist:

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

### S4. Zwei Server, zwei Clients, eine Nachricht

Das Zielbild: zwei `XMPPServer`-Instanzen mit verschiedenen Domains, an jeder
ein `XMPPClient`, und eine Nachricht geht von einem zum anderen. Das ist
Server-zu-Server-Föderation (RFC 6120 §4) und heute gar nicht vorhanden — alle
Sitzungen müssen auf derselben Domain liegen.

**Umfang:** groß. Braucht eine S2S-Verbindung zwischen den Servern, Routing
anhand der Domain im `to`, und mindestens Dialback (XEP-0220) oder
SASL-EXTERNAL zur Authentifizierung der Gegenstelle.
**Warum es sich lohnt:** es ist zugleich der schärfste Integrationstest, den das
Projekt haben kann — er übt Routing, Adressierung und Zustellung über eine
echte Grenze hinweg, statt alles in einer Instanz kurzzuschliessen.

---

## Als Nächstes (Client)

### 1. XEP-0198 gegen einen echten Server, dann Default umstellen

Die Zählung stimmt gegen `XMPPServer`. Es fehlt ein Lauf gegen ejabberd oder
Prosody; danach kann `StreamManagementEnabled` auf `true`.

Anschließend Stream-Resume: `ResumeAsync` und `GetUnackedStanzas` existieren,
werden aber nirgends aufgerufen — nach einem Reconnect baut der Client neu auf
und die unbestätigten Stanzas gehen verloren. Der `XMPPServer` beherrscht
`<resume/>` ebenfalls noch nicht, das wäre gleich mitzumachen.

### 2. Feste Resource ersetzen

Der Client bittet um `console-<pid>`. Laufen zwei Clients im selben Prozess,
kollidieren sie, und ein Server ohne eigene Vergabe antwortet mit
`<conflict/>` — was seit dem Umbau der Aufbauphase auch richtig als Ablehnung
ankommt, den Aufbau aber abbricht. Sauber wäre: bei `<conflict/>` einmal ohne
`<resource/>` neu binden und die vom Server vergebene Resource übernehmen.

**Umfang:** klein.

---

## Später

### Protokoll
- Eingehende `subscribed`/`unsubscribed`/`unsubscribe` im Client auswerten.
  Seit S3b schickt der Server dazu Roster-Pushes, der Zustand kommt also an —
  die Stanzas selbst laufen aber weiter durch `UpdatePresence` und setzen den
  Kontakt dabei fälschlich auf *online*, weil sie kein `type='unavailable'`
  tragen. Das ist jetzt erreichbar geworden und gehört zusammen behoben.
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
