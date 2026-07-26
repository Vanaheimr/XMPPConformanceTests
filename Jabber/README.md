# XMPP Console Client (.NET 10)

Ein XMPP-Client für die Kommandozeile mit WebSocket-Transport (RFC 7395) und
SCRAM-Authentifizierung.

> **Reifegrad:** Experimentell. Der Client verbindet, authentifiziert und
> chattet gegen ejabberd, aber Verbindungsmanagement und Fehlerbehandlung sind
> unvollständig, und Stream Management kann noch nicht resumen (siehe [Bekannte
> Einschränkungen](#bekannte-einschränkungen)). Nicht für den Produktivbetrieb.

## Authentifizierung

| Methode | Status |
|---------|--------|
| SCRAM-SHA-256 | ✅ Bevorzugt |
| SCRAM-SHA-1 | ✅ Fallback |
| SASL PLAIN | ⚠️ Letzter Fallback, ohne Downgrade-Schutz |
| SCRAM-*-PLUS (Channel Binding) | ❌ Nicht implementiert |

Der Mechanismus wird allein aus der Server-Ankündigung gewählt und nicht
gepinnt: Ein aktiver MITM, der die SCRAM-Angebote entfernt, bekommt PLAIN.

## XEP-Unterstützung

Legende: ✅ funktionsfähig · ⚠️ implementiert mit bekannten Lücken · 🚧 vorhanden, aber standardmäßig aus

| XEP | Name | Status | Anmerkung |
|-----|------|--------|-----------|
| XEP-0030 | Service Discovery | ⚠️ | Abfrage + Antwort; Antwort setzt kein `node`-Attribut |
| XEP-0060 | Publish-Subscribe | ⚠️ | Events werden geparst und als `iq set` bestätigt; IQ-Ergebnisse werden nicht korreliert |
| XEP-0085 | Chat State Notifications | ✅ | Senden + Empfangen |
| XEP-0115 | Entity Capabilities | ⚠️ | ver-String weicht von XEP-0115 §5.1 ab; Antwort-Hash wird nicht verifiziert |
| XEP-0184 | Message Delivery Receipts | ✅ | Mit Spoofing-Schutz |
| XEP-0198 | Stream Management | ⚠️ | Zählung korrekt und getestet; aus per Default, kein Resume |
| XEP-0199 | XMPP Ping | ✅ | Senden, Beantworten, RTT-Messung |
| XEP-0280 | Message Carbons | ✅ | Mit Spoofing-Schutz |
| XEP-0333 | Chat Markers | ✅ | Senden + Empfangen, Namespace-geprüft gegen Verwechslung mit XEP-0184 |

## RFC-Konformität

### RFC 6120 — XMPP Core

| Bereich | Status |
|---------|--------|
| SASL-Aushandlung und -Durchführung (§6) | ✅ |
| Resource Binding (§7) | ⚠️ Feste Resource `console-<pid>`; eine Ablehnung bricht den Aufbau ab, statt einen JID anzunehmen |
| Legacy Session (RFC 3921) | ✅ Wird übersprungen, wenn das Feature selbst `<optional/>` trägt |
| Stanza-Fehler (§8.3) | ✅ Typ, Bedingung, Text und `by` werden geparst; offene Anfragen scheitern statt scheinbar zu gelingen |
| Antwort auf unbehandelte IQs (§8.2.3) | ✅ Unbekannte `iq get`/`set` werden mit `<service-unavailable/>` beantwortet |
| Stream-Fehler (§4.9) | ✅ Geparst; nach einer nicht wiederholbaren Bedingung unterbleibt der Reconnect |

### RFC 6121 — Instant Messaging und Presence

| Bereich | Status |
|---------|--------|
| Roster abrufen, hinzufügen, entfernen, Gruppen | ✅ |
| Roster-Pushes anwenden | ✅ |
| Absender-Validierung von Roster-Pushes (§2.1.6) | ✅ Nur ohne `from` oder mit dem eigenen Bare-JID; sonst verworfen und als Spoofing gemeldet |
| Roster-Versionierung (§2.6) | ❌ API vorhanden (`Roster.Version`, `RosterStanzaBuilder.GetRoster`), aber ungenutzt |
| Presence-Subscription anfragen/annehmen/ablehnen | ✅ |
| Eingehende `subscribed`/`unsubscribed`/`unsubscribe` | ❌ Werden nicht in den Roster eingepflegt |
| Message-Typen (`chat`/`error`/`groupchat`) | ❌ Nicht unterschieden |

### RFC 7395 — XMPP über WebSocket

| Bereich | Status |
|---------|--------|
| Subprotokoll `xmpp`, `<open/>`/`<close/>`-Framing | ✅ |
| Close-Handshake | ✅ `<close/>` wird gesendet, dann bis zu 3 s auf die Gegenseite gewartet, danach Socket-Abbruch |
| Endpunkt-Discovery (XEP-0156 / `host-meta`) | ❌ Fest verdrahtet auf `wss://<domain>:5443/ws` (ejabberd-Default) |

Der Default-Port ist ejabberd-spezifisch. Für andere Server muss die URL
explizit angegeben werden, z. B. Prosody: `wss://<host>:5281/xmpp-websocket`.

### RFC 5802 / RFC 7677 — SCRAM

| Bereich | Status |
|---------|--------|
| Vier-Schritt-Handshake | ✅ |
| Nonce-Prüfung gegen MITM | ✅ |
| Server-Signatur-Verifikation (konstante Laufzeit) | ✅ Zwingend — ein `<success/>` ohne server-final-message bricht den Aufbau ab |
| SASLprep (RFC 4013) | ⚠️ Auf NFKC-Normalisierung reduziert — nur für ASCII zuverlässig |
| Channel Binding (RFC 9266 `tls-exporter`) | ❌ |

### RFC 7622 — JID-Behandlung

Kein PRECIS/Stringprep. Bare-JIDs werden per `ToLowerInvariant()` verglichen,
Resourceparts werden dabei ebenfalls kleingeschrieben, obwohl sie
case-sensitiv sind.

## Installation

```bash
# .NET 10 SDK erforderlich
dotnet build
dotnet run
```

## Verwendung

```bash
# Interaktiv (fragt JID, Passwort und WebSocket-URI ab)
dotnet run

# Mit Parametern
dotnet run -- -j user@example.com -p geheim

# Mit expliziter WebSocket-URL (bei nicht-ejabberd-Servern nötig)
dotnet run -- -j user@example.com -p pw -w wss://xmpp.example.com:5281/xmpp-websocket

# Mit vollem Protokoll-Log
dotnet run -- -j user@example.com -p geheim -v
```

| Option | Bedeutung |
|--------|-----------|
| `-j`, `--jid <jid>` | JID im Format `user@domain` |
| `-p`, `--password <pw>` | Passwort |
| `-w`, `--ws`, `--websocket <uri>` | WebSocket-URI |
| `-v`, `--verbose` | Ausführliches Logging (Trace-Level, zeigt alle Stanzas) |
| `-h`, `--help` | Hilfe anzeigen |

## Kommandos

### Nachrichten
```
/to <jid>                 Chat-Partner setzen (Aliase: /chat)
/to                       Chat-Partner zurücksetzen
/msg <jid> <text>         Einzelne Nachricht senden (Alias: /m)
/status [show] [text]     Status setzen: available|away|chat|dnd|xa (Alias: /s)
```

### Kontakte (Roster)
```
/roster [filter]          Kontakte anzeigen (Aliase: /list, /contacts)
/online                   Nur Online-Kontakte
/add <jid> [name] [g1,g2] Kontakt hinzufügen und Subscription anfragen
/remove <jid>             Kontakt entfernen (Alias: /del)
/info <jid>               Kontakt-Details
/groups                   Gruppen mit Kontaktanzahl
/pending                  Offene Kontaktanfragen
/accept [jid]             Kontaktanfrage annehmen (ohne Argument: die erste)
/deny [jid]               Kontaktanfrage ablehnen (ohne Argument: die erste)
```

### Chat States (XEP-0085)
```
/typing                   'tippt gerade' senden
/paused                   'hat aufgehört zu tippen' senden
/gone                     Chat verlassen und Empfänger zurücksetzen
```

### Chat Markers (XEP-0333)
```
/mark received [msg-id]   Als empfangen markieren (Alias: r)
/mark displayed [msg-id]  Als gelesen markieren (Aliase: d, read)
/mark ack [msg-id]        Bestätigen (Aliase: acknowledged, a)
```
Ohne `msg-id` wird die zuletzt empfangene Nachricht verwendet.

### Service Discovery (XEP-0030)
```
/disco                    Unterbefehle anzeigen
/disco server             Features des eigenen Servers
/disco info <jid>         Features eines JIDs
/disco items <jid>        Services/Items eines JIDs
/features                 Server-Features und eigene Features
```

### PubSub (XEP-0060)
```
/pubsub                        Unterbefehle anzeigen
/pubsub sub <node>             Node abonnieren (Alias: subscribe)
/pubsub unsub <node>           Abo beenden (Alias: unsubscribe)
/pubsub pub <node> <id> <data> Item veröffentlichen (Alias: publish)
/pubsub get <node> [max]       Items abrufen (Alias: items)
/pubsub create <node>          Node erstellen
/pubsub delete <node>          Node löschen
```

### Verbindung
```
/ping [jid]               Ping senden und RTT messen (XEP-0199)
/keepalive [on|off|sek]   Keepalive-Status anzeigen/ändern
/sm [on|off]              Stream-Management-Status anzeigen/ändern
/who                      Eigenen Verbindungsstatus anzeigen
/carbons                  Carbon-Status anzeigen
/reconnect                Neu verbinden
/disconnect               Verbindung trennen
/raw                      XML-Debug-Ausgabe umschalten
/help                     Hilfe (Aliase: /h, /?)
/quit                     Beenden (Aliase: /q, /exit)
```

## Keepalive (Anti-Timeout)

Standard-Intervall: **25 Sekunden**. Änderungen wirken erst nach einem
Reconnect, da die Schleife beim Verbindungsaufbau gestartet wird.

```
/keepalive
Keepalive Status:
  Aktiviert: True
  Interval: 25s
  Methode: XEP-0199 Ping

/keepalive 60      # Intervall auf 60s setzen
/keepalive off     # Deaktivieren
```

**Methoden:** Ist Stream Management aktiv, wird ein `<r/>` gesendet
(leichtgewichtig), sonst ein XEP-0199 Ping.

## Spoofing-Schutz

Der Client prüft bei drei Nachrichtenarten den Absender, bevor er sie
verarbeitet:

1. **Carbons (XEP-0280)** — müssen vom eigenen Bare-JID stammen (also vom
   eigenen Server). Andernfalls könnte jeder Kontakt beliebige Nachrichten
   als angeblich selbst gesendet einschleusen.
2. **Receipts (XEP-0184)** — müssen vom Bare-JID des ursprünglichen
   Empfängers stammen.
3. **PubSub-Events (XEP-0060)** — müssen vom konfigurierten PubSub-Service
   stammen.
4. **Roster-Pushes (RFC 6121 §2.1.6)** — müssen ohne `from` kommen oder vom
   eigenen Bare-JID. Sonst könnte jeder Absender Kontakte in den lokalen
   Roster einschleusen oder daraus löschen.

**Nicht abgedeckt:** der XEP-0115-Caps-Cache, der die Antwort nicht gegen den
angekündigten Hash prüft.

## Architektur

Drei Schichten, klar getrennt:

| Schicht | Typ | Aufgabe |
|---------|-----|---------|
| UI | `Program` | Kommandozeile, Kommando-Dispatch, Darstellung. Enthält keine Protokolllogik. |
| Anwendung | `XMPPClient` | Sitzungszustand (Chatpartner, offene Kontaktanfragen, letzte Nachrichten-ID) und zusammengesetzte Operationen. |
| Protokoll | `XMPPConnection` | WebSocket-I/O, SASL, Resource Binding, Stanza-Routing. |

`XMPPClient` und `XMPPConnection` geben nichts auf der Konsole aus — alles läuft
über Events und die injizierte `ILoggerFactory`.

### Verbindungsaufbau

Der Aufbau zerfällt in zwei Abschnitte, und die Grenze liegt beim Resource
Binding:

1. **Aushandlung** (`<open/>`, Stream-Features, SASL, Binding). Hier liest
   `ConnectInternalAsync` selbst vom Socket. Das ist unproblematisch, weil der
   Server noch keine Resource hat, an die er etwas zustellen könnte — es kann
   nichts anderes eintreffen. Ausgewertet wird über `StreamNegotiation`, eine
   Sammlung reiner Funktionen auf dem geparsten `XElement`.
2. **Sitzungsaufbau** (Legacy-Session, XEP-0198, Carbons, Roster, Presence).
   Ab dem Binding läuft die Empfangsschleife, und alle Schritte laufen über
   `SendIqAsync` — dieselbe `TaskCompletionSource`-Korrelation über die
   Stanza-ID, die `DiscoManager` und `PingManager` benutzen. Was in dieser Zeit
   sonst eintrifft (nachgelieferte Nachrichten, Presence, Roster-Pushes), wird
   ganz normal zugestellt.

Auf Textmustern arbeiten bewusst nur noch `StreamManagementManager` (liest `h`
und `id` aus Nonzas), `StanzaError`/`StreamError` (müssen gerade auch mit
unwohlgeformten Rahmen umgehen) und `SCRAMAuthenticator` (SASL ist kein XML).

### Als Bibliothek verwenden

```csharp
using Microsoft.Extensions.Logging;
using org.GraphDefined.Vanaheimr.Hermod.XMPP;

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());

await using var client = new XMPPClient(
                             "user@example.com",
                             "geheim",
                             "wss://xmpp.example.com:5443/ws",
                             loggerFactory);

client.OnMessage += msg =>
    Console.WriteLine($"{msg.FromBareJid}: {msg.Body}");

client.OnSubscriptionRequest += async (from, status) =>
    await client.AcceptSubscriptionAsync(from);

await client.ConnectAsync();

client.SetChatPartner("kontakt@example.com");
await client.SendMessageAsync("Hallo!");
```

Die `ILoggerFactory` ist optional; ohne sie wird auf `NullLogger` zurückgefallen
und gar nicht geloggt. Log-Level: `Information` für Verbindungsschritte,
`Debug` für Protokolldetails, `Trace` für einzelne Stanzas, `Warning` für
abgewehrte Spoofing-Versuche und Protokollauffälligkeiten.

## Projektstruktur

Namespace ist durchgehend flach `org.GraphDefined.Vanaheimr.Hermod.XMPP`
(wie `Hermod.DNS` und `Hermod.HTTP`); die Ordner gliedern nur.
Eine Datei pro Typ:

```
Jabber/
├── Program.cs                            Konsolen-Frontend
├── Client/
│   ├── XMPPClient.cs                     Anwendungsnaher Client
│   └── XMPPMessage.cs                    Empfangene Chat-Nachricht
├── Common/
│   ├── JidUtilities.cs                   Bare-JID-Ermittlung
│   └── XmlEscaping.cs                    XML-Escaping
├── Auth/
│   ├── AuthenticationException.cs
│   ├── SCRAMAuthenticator.cs             RFC 5802 / RFC 7677
│   └── SCRAMMechanism.cs
├── Connection/
│   ├── ConnectionState.cs
│   └── XMPPConnection.cs                 WebSocket-I/O, Auth-Ablauf, Stanza-Routing
├── Rosters/
│   ├── PresenceState.cs
│   ├── Roster.cs                         Kontaktverwaltung
│   ├── RosterItem.cs
│   ├── RosterStanzaBuilder.cs
│   └── SubscriptionState.cs
└── XEPs/
    ├── XEP0030ServiceDiscovery/          DiscoManager, DiscoInfo, DiscoItems,
    │                                     DiscoIdentity, DiscoItem
    ├── XEP0060PubSub/                    PubSubManager, PubSubBuilder,
    │                                     PubSubEvent, PubSubEventType, PubSubItem
    ├── XEP0085ChatStates/                ChatState, ChatStateExtensions
    ├── XEP0115EntityCapabilities/        EntityCapsManager
    ├── XEP0184MessageReceipts/           ReceiptTracker, ReceiptBuilder,
    │                                     MessageReceipt, PendingReceipt
    ├── XEP0198StreamManagement/          StreamManagementManager
    ├── XEP0199Ping/                      PingManager
    ├── XEP0280MessageCarbons/            CarbonManager, CarbonMessage, CarbonResult
    └── XEP0333ChatMarkers/               ChatMarkers, ChatMarker, ChatMarkerType

Server/                                   XMPPServer, XMPPSession,
   (Namespace …Hermod.XMPP.Server)        XMPPAccount, RosterEntry
```

Die XEP-Manager bekommen ihre Sende-Funktion als `Func<string, Task>` injiziert
und kennen den Transport nicht — sie sind damit unabhängig von
`XMPPConnection` testbar.

## Tests

```bash
dotnet test ../Jabber.Tests/Jabber.Tests.csproj
```

Die Suite liegt in `Jabber.Tests/XMPP/` und nutzt NUnit in denselben Versionen
wie `HermodTests` (NUnit 4.6.1, NUnit3TestAdapter 6.2.0, Test.Sdk 18.8.1).
Namespaces und Ordnerschnitt entsprechen `HermodTests`, damit sich der Inhalt
von `XMPP/` später unverändert dorthin verschieben lässt.

### XMPPServer

`Jabber/Server/` enthält einen echten XMPP-over-WebSocket-Server (RFC 7395).
Er liegt bewusst im Hauptprojekt und nicht im Testprojekt, damit er beim Umzug
nach Hermod mitwandert; sein Namespace ist `…Hermod.XMPP.Server`. Er reicht so
weit, dass sich mehrere echte `XMPPClient`-Instanzen gleichzeitig anmelden und
miteinander sprechen:

- SASL PLAIN gegen hinterlegte Konten, inklusive Fehlanmeldung
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
- XEP-0198 Stream Management mit **eigener, unabhängig implementierter**
  Zählung — der Server benutzt bewusst nicht dieselbe Hilfsfunktion wie der
  Client, sonst prüften die Tests beide Seiten mit derselben Logik
- Stanza- und Stream-Fehler auf Zuruf: `StanzaErrorIq(…)` und
  `session.SendStreamErrorAsync(condition)`
- Schalter für Fehlerfälle: `CompleteCloseHandshake`, `RouteStanzas`,
  `BroadcastPresence`, `DeliverCarbons`, `AnswerPings`,
  `OfferStreamManagement`, `AnswerAckRequests`, `FailPings`, `FailDiscoInfo`,
  `FailBind`, `SessionRequired`
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

#### Was dem Server zum Produktivbetrieb fehlt

Der Name sagt es nicht mehr — bis vor kurzem hiess die Klasse `FakeXMPPServer`.
Sie ist als Gegenstelle für Tests und Entwicklung gedacht, nicht als
Server-Implementierung:

- **Kein TLS.** Der Listener spricht `http://` beziehungsweise `ws://`. Für
  RFC 6120 §5 wäre `wss://` mit Zertifikat nötig.
- **Nur SASL PLAIN**, und Passwörter liegen im Klartext im Speicher. SCRAM
  beherrscht der Server nicht — der Client fällt gegen ihn also immer auf den
  schwächsten Mechanismus zurück.
- **Keine dauerhafte Kontenverwaltung.** Konten und Roster leben im Speicher
  einer `XMPPServer`-Instanz und sind beim Beenden weg.
- **Keine Subscription-Pre-Approval** (RFC 6121 §3.4) und keine Zustellung
  offener Anfragen an später anmeldende Kontakte (§3.1.3): eine Anfrage an ein
  gerade nicht verbundenes Konto wird nicht aufbewahrt.
- **Keine Server-zu-Server-Föderation** (RFC 6120 §4) — alle Sitzungen müssen
  auf derselben Domain liegen.
- **Kein Stream-Resume.** `<enable/>` wird beantwortet, `<resume/>` nicht; die
  Gegenprobe zur Resume-Lücke des Clients fehlt damit auf beiden Seiten.
- **Fehlerbehandlung nur auf Zuruf.** Ausser den Schaltern oben erzeugt der
  Server keine Stanza-Fehler; unbekannte IQs bekommen pauschal
  `<service-unavailable/>`.

### Kryptografische Testvektoren

Die Implementierungen werden gegen die veröffentlichten Vektoren gerechnet,
nicht gegen sich selbst:

| Quelle | Was geprüft wird | Ergebnis |
|--------|------------------|----------|
| RFC 5802 §5 | SCRAM-SHA-1: client-first, ClientProof, ServerSignature | ✅ exakt reproduziert |
| RFC 7677 §3 | SCRAM-SHA-256: client-first, ClientProof, ServerSignature | ✅ exakt reproduziert |
| XEP-0115 §5.2 | Verification String `QgayPKawpkPSDYmwT/WM94uAlu0=` | ✅ exakt reproduziert |

Damit sind Hi/PBKDF2, ClientKey, StoredKey, AuthMessage, ClientSignature,
die XOR-Verknüpfung und die Server-Signaturprüfung gemeinsam abgedeckt.

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
`InternalsVisibleTo` in `Jabber.csproj` — für beide möglichen Testassembly-Namen.

## Bekannte Einschränkungen

Was davon in welcher Reihenfolge angegangen wird, steht im
[Arbeitsplan](../WORKPLAN.md).

### Architektur
- **Caps-Hash deckt keine XEP-0128-Datenformulare ab.** XEP-0115 §5.1 nimmt
  `FORM_TYPE`-Felder mit in den Verification String auf;
  `CalculateVerificationString` verarbeitet nur Identitäten und Features. Solange
  die eigene disco#info-Antwort keine Formulare enthält, stimmt der Hash.
- **Log-Ausgabe und Konsolen-UI überlagern sich.** Der Standard-Konsolenlogger
  schreibt in dieselbe Konsole wie die Eingabezeile und stört den Prompt. Ein
  eigener `ILoggerProvider`, der über dieselbe synchronisierte Ausgabe läuft,
  wäre die saubere Lösung.
- **XEP-0198 ist per Default aus und kann nicht resumen.** Die Zählung stimmt
  jetzt und ist gegen den `XMPPServer` abgesichert, aber es gab noch keinen
  Lauf gegen einen echten Server — deshalb bleibt `StreamManagementEnabled`
  vorerst `false`. `ResumeAsync` und `GetUnackedStanzas` existieren, werden aber
  nirgends aufgerufen: nach einem Reconnect baut der Client den Stream neu auf,
  statt ihn fortzusetzen, und die unbestätigten Stanzas gehen verloren.

### Funktionsumfang
- Kein Multi-User Chat (XEP-0045)
- Kein Message Archive Management (XEP-0313)
- Keine Ende-zu-Ende-Verschlüsselung (OMEMO, XEP-0384)
- Kein HTTP File Upload (XEP-0363)
- Keine Client State Indication (XEP-0352)
- Kein Last Message Correction (XEP-0308)
- Kein TCP-Transport — `XmppConnection.CreateTcp` erzeugt eine `tcp://`-URI,
  die `ClientWebSocket` ablehnt, und ist damit funktionslos.

### Ungenutzte API-Fläche
Folgende öffentliche Member werden nirgends aufgerufen und sind ungetestet:
`MessageReceipt`, `ReceiptTracker.GetTimedOutMessages`,
`PubSubManager.OnSubscriptionResult`, `PubSubBuilder.Retract`,
`PubSubBuilder.DiscoverNodes`, `StreamManagementManager.ResumeAsync`/
`GetUnackedStanzas`/`OnStanzasLost`, `EntityCapsManager.GetCachedInfo`,
`RosterStanzaBuilder.GetRoster`/`Unsubscribe`, `DiscoInfo.Supports*`,
`CarbonManager.DisableIq`.

## Lizenz

Apache License, Version 2.0 — siehe [LICENSE](../LICENSE).

Copyright (c) 2010-2026 GraphDefined GmbH &lt;achim.friedland@graphdefined.com&gt;
