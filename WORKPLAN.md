# Arbeitsplan

Was am XMPP-Client offen ist, in welcher Reihenfolge es sinnvoll ist und warum.
Die ausführliche Beschreibung der einzelnen Lücken steht in
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
| SCRAM `ExtractValue` verankert, Caps-Sortierung oktettweise | offen im Working Tree |
| XEP-0198 zählt korrekt (beide Richtungen, Nonzas, Überlauf) | offen im Working Tree |
| `XMPPServer` ins Hauptprojekt, „Fake" aus den Typnamen | offen im Working Tree |
| `#region Usings` in allen Dateien | offen im Working Tree |

Jede dieser Korrekturen ist durch Mutationstests abgesichert: Fix zurückgedreht,
geprüft dass genau die zuständigen Tests fehlschlagen, Fix wieder eingesetzt.
Aktueller Stand der Suite: **68 Tests, 0 Fehler, 0 übersprungen**.

---

## Als Nächstes

### 1. RFC 6120 §8.2.3 — unbeantwortete IQs (MUST-Verstoß)

Unbekannte `iq` vom Typ `get`/`set` werden in `ProcessIq` still verworfen.
Der RFC verlangt eine Antwort, mindestens `<service-unavailable/>`. Ein Server
oder Gegenüber, der auf Antwort wartet, läuft in einen Timeout.

**Umfang:** klein, eine Fallback-Verzweigung in `ProcessIq`.
**Test:** `XMPPServer` schickt ein unbekanntes `iq get` und erwartet einen
Fehler zurück — der Server kann das schon, es fehlt nur die Prüfung.
**Warum zuerst:** der einzige bekannte glatte MUST-Verstoß, und billig.

### 2. Stanza- und Stream-Fehler auswerten

`<error/>`-Nutzlasten (§8.3) und Stream-Fehler (§4.9) werden nirgends geparst.
Fehlgeschlagene Operationen sehen für den Aufrufer aus wie Erfolg — besonders
bei PubSub, wo IQ-Ergebnisse ohnehin nicht korreliert werden.

**Umfang:** mittel. Braucht einen Fehlertyp und Auswertung an den Stellen mit
`TaskCompletionSource`-Korrelation.

### 3. XML nicht mehr per Regex parsen

Das ist die gemeinsame Ursache der meisten Interop-Lücken: Attribut-Reihenfolge
(XEP-0333), Quote-Stil, Namespace-Präfixe und verschachtelte Elemente in
`<forwarded/>`. Solange das steht, sind Einzelkorrekturen an den Parsern
Symptombehandlung.

**Umfang:** groß, aber gut portionierbar — `XElement` pro Stanza-Typ, beginnend
bei `ProcessMessage`.
**Risiko:** hoch ohne Tests, niedrig mit der vorhandenen Suite. Vorher lohnt es,
für jede betroffene Stanza-Art einen Test mit ungewöhnlicher, aber gültiger
Schreibweise anzulegen — die schlagen dann vorher fehl und danach nicht mehr.

### 4. Aufbauphase entwirren

`ConnectInternalAsync` liest selbst vom Socket, verwirft bis zu zehn nicht
passende Stanzas (auch echte Nachrichten und Presences) und startet erst danach
die Empfangsschleife. Die `TaskCompletionSource`-Korrelation, die `DiscoManager`
und `PingManager` schon richtig machen, gibt es hier nicht.

**Umfang:** mittel.
**Nebeneffekt:** löst zugleich den Grund, warum die XEP-0198-Zählung zwei
Empfangspfade abdecken muss.

### 5. XEP-0198 gegen einen echten Server, dann Default umstellen

Die Zählung stimmt gegen `XMPPServer`. Es fehlt ein Lauf gegen ejabberd oder
Prosody; danach kann `StreamManagementEnabled` auf `true`.

Anschließend Stream-Resume: `ResumeAsync` und `GetUnackedStanzas` existieren,
werden aber nirgends aufgerufen — nach einem Reconnect baut der Client neu auf
und die unbestätigten Stanzas gehen verloren. Der `XMPPServer` beherrscht
`<resume/>` ebenfalls noch nicht, das wäre gleich mitzumachen.

---

## Später

### Protokoll
- Eingehende `subscribed`/`unsubscribed`/`unsubscribe` in den Roster einpflegen
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

### Testserver (`Jabber/Server/`)
Die Liste steht in [Jabber/README.md](Jabber/README.md#was-dem-server-zum-produktivbetrieb-fehlt).
Priorität hat davon nur, was Tests ermöglicht: `<resume/>` (siehe Punkt 5) und
SCRAM, damit der SCRAM-Pfad des Clients auch integrativ und nicht nur gegen die
RFC-Vektoren geprüft ist.

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
