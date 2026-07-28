# XMPP Console Client (.NET 10)

Ein XMPP-Client für die Kommandozeile mit WebSocket-Transport (RFC 7395) und
SCRAM-Authentifizierung.

> **Reifegrad:** Experimentell. Der Client verbindet, authentifiziert und
> chattet gegen Prosody 13 über `wss://` — geprüft, nicht behauptet: bis vor
> kurzem stand hier dasselbe über ejabberd, und tatsächlich hätte sich der
> Client an *keinem* RFC-7395-konformen Server anmelden können, weil seine
> Stanzas ohne Namensraum hinausgingen. Verbindungsmanagement und
> Fehlerbehandlung sind unvollständig (siehe
> [Bekannte Einschränkungen](#bekannte-einschränkungen)). Nicht für den
> Produktivbetrieb.

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
| XEP-0198 | Stream Management | ✅ | Gegen Prosody 13 und ejabberd 24.12 geprüft, an per Default, mit Wiederaufnahme |
| XEP-0199 | XMPP Ping | ✅ | Senden, Beantworten, RTT-Messung |
| XEP-0280 | Message Carbons | ✅ | Mit Spoofing-Schutz |
| XEP-0333 | Chat Markers | ✅ | Senden + Empfangen, Namespace-geprüft gegen Verwechslung mit XEP-0184 |

## RFC-Konformität

### RFC 6120 — XMPP Core

| Bereich | Status |
|---------|--------|
| TLS (§5) | ⚠️ `wss://` über den WebSocket-Transport; `XMPPConnection.ServerCertificateValidator` erlaubt eine eigene Zertifikatsprüfung, `null` überlässt sie dem Betriebssystem. Kein STARTTLS (§5.4) — WebSocket bringt TLS unter sich mit, ein Klartext-`ws://` wird aber nicht verweigert |
| SASL-Aushandlung und -Durchführung (§6) | ✅ Client und Server; der Client nimmt den stärksten angebotenen Mechanismus, der Server lehnt einen nicht angebotenen ab |
| Resource Binding (§7) | ✅ `XMPPConnection.Resource` (Vorgabe `console-<pid>`, `null` überlässt die Wahl dem Server); auf `<conflict/>` folgt ein zweiter Versuch ohne Wunsch, jede andere Ablehnung bricht ab |
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
| Eingehende `subscribed`/`unsubscribed`/`unsubscribe` | ✅ Ändern den Subscription-Zustand und gelten nicht als Anwesenheit |
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
  Methode: Stream Management <r/>

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

- TLS: `wss://` mit einem selbst signierten Zertifikat, das der Konstruktor
  erzeugt (RFC 6120 §5). `new XMPPServer(useTLS: false)` schaltet auf `ws://`
  zurück, was für die Fehlersuche mit einem Mitschnitt gedacht ist
- SASL: SCRAM-SHA-256, SCRAM-SHA-1 und PLAIN, in dieser Reihenfolge angeboten.
  Welche Mechanismen es sein sollen, steuert `OfferedSaslMechanisms`; ein nicht
  angebotener wird auch dann abgelehnt, wenn ein Client ihn versucht
- Zugangsdaten nach RFC 5802 §3 — Salt, Iterationszahl, `StoredKey` und
  `ServerKey` je Mechanismus. Kein Klartextpasswort, auch nicht für PLAIN:
  das prüft, indem es aus dem angebotenen Passwort neu ableitet
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
- XEP-0288 Bidirectional Server-to-Server Streams (`UseBidirectionalStreams`):
  beide Richtungen über eine Verbindung. Ohne die Erweiterung antwortet jede
  Seite über eine *eigene* ausgehende Verbindung (RFC 6120 §4.1) — hinter NAT,
  hinter einer Firewall oder ohne DNS-Eintrag geht die Antwort dann verloren,
  und zwar stillschweigend. Angeboten auf eingehenden Verbindungen, erbeten auf
  ausgehenden; über die Rückrichtung geht nichts vor dem Ausweis der
  Gegenstelle und nichts für eine fremde Domain. Auf beiden S2S-Transporten,
  gegen Prosody 13 und ejabberd 24.12 geprüft. Angekündigt wird die Form der
  XEP (`urn:xmpp:features:bidi`); gelesen wird zusätzlich `urn:xmpp:bidi`,
  weil ejabberd 24.12 in den Features das Freischalt-Element ablegt
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
- XEP-0198 Stream Management mit **eigener, unabhängig implementierter**
  Zählung — der Server benutzt bewusst nicht dieselbe Hilfsfunktion wie der
  Client, sonst prüften die Tests beide Seiten mit derselben Logik
- Stanza- und Stream-Fehler auf Zuruf: `StanzaErrorIq(…)` und
  `session.SendStreamErrorAsync(condition)`
- Schalter für Fehlerfälle: `CompleteCloseHandshake`, `RouteStanzas`,
  `BroadcastPresence`, `DeliverCarbons`, `AnswerPings`,
  `OfferStreamManagement`, `AnswerAckRequests`, `FailPings`, `FailDiscoInfo`,
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
  und PLAIN; die `-PLUS`-Varianten fehlen. Ein unbekanntes Konto wird
  abgelehnt, bevor der Austausch beginnt — damit verrät der Server, ob es ein
  Konto gibt, statt nach RFC 5802 §7 mit einem erfundenen Salt weiterzumachen.
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
- **Zwei fremde Gegenstellen, nicht mehr.** Gegen Prosody 13 und ejabberd 24.12
  sind beide S2S-Richtungen und beide Ausweisverfahren geprüft (STARTTLS,
  SASL-EXTERNAL, Dialback nach XEP-0220 in beiden Rollen, XEP-0288). Beide
  Aufbauten stehen in `tools/`; die Tests überspringen sich ohne sie. Was der
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
  Nicht abgedeckt: eine Stanza, die der Client erfolgreich abschickt und die
  den Server nie erreicht — im Testaufbau gibt es diesen Fall nicht.
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
- **XEP-0198 ist per Default an, samt Wiederaufnahme.** Die Zählung ist gegen
  Prosody 13 geprüft: nach einem vollständigen Sitzungsaufbau melden beide
  Seiten denselben Stand, und zwar auf den Zähler genau — nicht nur „die
  Warteschlange lief leer", was auch ein zu grosses `h` bewirkte. Nach einem
  Abriss knüpft der Client vor dem Resource Binding an den alten Stream an: die
  Full-JID bleibt, was während der Störung ankam, wird nachgeliefert, und die
  Kontakte sehen kein Verschwinden. Gelingt es nicht — Frist abgelaufen,
  Kennung unbekannt —, bindet er neu. Geprüft gegen Prosody 13
  (`mod_smacks`) und ejabberd 24.12 (`mod_stream_mgmt`) - beide verhalten sich
  hier gleich.
- ~~Der Content-Namensraum wandert nur in einer Richtung mit.~~ Behoben: jede
  Stanza an einen Client trägt jetzt `jabber:client`, jede über die
  Domain-Grenze `jabber:server` (RFC 6120 §4.8.1, RFC 7395 §3.3.3). Vorher
  schickte der Server seinen Clients **gar keinen** Namensraum und reichte
  Fremdes unverändert als `jabber:server` durch.

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
