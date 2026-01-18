# XMPP Console Client (.NET 10)

Ein minimalistischer Jabber/XMPP-Client für die Kommandozeile mit TLS, SASL PLAIN Authentifizierung, vollständigem XML-Streaming und Roster-Management.

## Features

- **TLS 1.2/1.3** via STARTTLS
- **SASL PLAIN** Authentifizierung  
- **Vollständiges XML-Streaming** mit `XmlReader` (ConformanceLevel.Fragment)
- **Roster-Management** mit Gruppen und Subscription-Handling
- **Presence-Tracking** für Online-Status der Kontakte
- **Async I/O** für gleichzeitiges Senden/Empfangen
- **Farbige Konsolen-Ausgabe**

## Architektur

```
┌─────────────────────────────────────────────────────────┐
│                      Program.cs                         │
│              (Konsolen-UI, Commands)                    │
└─────────────────────┬───────────────────────────────────┘
                      │
┌─────────────────────▼───────────────────────────────────┐
│                 XmppConnection.cs                       │
│         (Verbindung, TLS, SASL, Routing)               │
└─────────────────────┬───────────────────────────────────┘
                      │
        ┌─────────────┴─────────────┐
        │                           │
┌───────▼───────┐          ┌────────▼────────┐
│ XmppStream-   │          │    Roster.cs    │
│ Parser.cs     │          │  (Kontakte,     │
│ (XML-Parsing) │          │   Gruppen,      │
└───────────────┘          │   Presence)     │
                           └─────────────────┘
```

## Build & Run

```bash
dotnet build
dotnet run

# Mit Argumenten:
dotnet run -- -j user@jabber.org -p geheimespasswort
dotnet run -- -j user@jabber.org -p passwort -s talk.jabber.org
```

## Befehle

### Nachrichten

| Befehl | Beschreibung |
|--------|--------------|
| `/to <jid>` | Chat-Partner setzen, dann direkt tippen |
| `/to` | Chat-Partner zurücksetzen |
| `/msg <jid> <text>` | Einzelne Nachricht senden |
| `/status [show] [text]` | Status ändern (away/chat/dnd/xa) |

### Kontakte (Roster)

| Befehl | Beschreibung |
|--------|--------------|
| `/roster [filter]` | Alle Kontakte anzeigen (optional filtern) |
| `/online` | Nur Online-Kontakte anzeigen |
| `/add <jid> [name] [gruppen]` | Kontakt hinzufügen |
| `/remove <jid>` | Kontakt entfernen |
| `/info <jid>` | Kontakt-Details anzeigen |
| `/groups` | Alle Gruppen anzeigen |

### Kontaktanfragen (Subscription)

| Befehl | Beschreibung |
|--------|--------------|
| `/pending` | Ausstehende Anfragen anzeigen |
| `/accept [jid]` | Kontaktanfrage akzeptieren |
| `/deny [jid]` | Kontaktanfrage ablehnen |

### Sonstiges

| Befehl | Beschreibung |
|--------|--------------|
| `/who` | Eigene JID anzeigen |
| `/raw` | XML-Debug-Anzeige umschalten |
| `/quit` | Beenden |

## Beispiel-Session

```
> /roster
╔═══ Kontakte (3) ═══
║ [Arbeit]
║   ● ↔ alice@company.de
║   ◐ ↔ bob@company.de
║ [(Keine Gruppe)]
║   ○ → support@jabber.org
╚══════════════════════════════

> /add charlie@jabber.de Charlie Freunde
Kontaktanfrage gesendet an: charlie@jabber.de

[*] 📩 Kontaktanfrage von charlie@jabber.de: Hi, ich bin's!
[*]    Nutze /accept charlie@jabber.de oder /deny charlie@jabber.de

> /accept charlie@jabber.de
Kontaktanfrage akzeptiert: charlie@jabber.de

> /to alice@company.de
Chat mit: alice@company.de

[alice@company.de] > Hey, hast du das Meeting heute gesehen?
  → Gesendet an alice@company.de

[14:32:15] alice@company.de: Ja, war interessant!
```

## XML-Streaming Details

Der Parser (`XmppStreamParser.cs`) verwendet `XmlReader` mit `ConformanceLevel.Fragment`, was für XMPP essentiell ist, da:

1. XMPP-Streams sind **keine vollständigen XML-Dokumente** - sie haben einen öffnenden `<stream:stream>` Tag der erst am Ende der Session geschlossen wird
2. Die einzelnen Stanzas (`<message>`, `<presence>`, `<iq>`) sind **Fragmente** innerhalb dieses Streams
3. Daten kommen **inkrementell** über TCP an und müssen gepuffert werden

```csharp
var settings = new XmlReaderSettings
{
    ConformanceLevel = ConformanceLevel.Fragment,  // Keine Root-Element-Anforderung
    Async = true,
    IgnoreWhitespace = true
};
```

## Roster & Subscription-Flow

```
Du                          Server                    Kontakt
 │                             │                          │
 │─── /add alice ─────────────▶│                          │
 │    (roster set + subscribe) │                          │
 │                             │──── subscribe ──────────▶│
 │                             │                          │
 │                             │◀─── subscribed ──────────│
 │◀─── roster push ────────────│     (Kontakt akzeptiert) │
 │    (subscription: to)       │                          │
 │                             │                          │
 │                             │◀─── subscribe ───────────│
 │◀─── subscribe request ──────│     (Kontakt will auch)  │
 │                             │                          │
 │─── /accept ────────────────▶│                          │
 │    (subscribed)             │──── subscribed ─────────▶│
 │                             │                          │
 │◀─── roster push ────────────│                          │
 │    (subscription: both)     │     ✓ Gegenseitig!       │
```

## Hinweise für Produktion

1. **Zertifikat-Validierung**: In `XmppConnection.cs` Zeile ~260 akzeptiert der Client derzeit alle Zertifikate. Für Produktion:
   ```csharp
   return sslPolicyErrors == SslPolicyErrors.None;
   ```

2. **SCRAM-SHA-1/256**: SASL PLAIN sendet das Passwort Base64-kodiert (nicht verschlüsselt!). Mit TLS ist das okay, aber SCRAM wäre sicherer.

3. **Reconnection**: Der Client verbindet sich nicht automatisch neu bei Verbindungsabbruch.

4. **Message Carbons & MAM**: Für Multi-Device-Support fehlen XEP-0280 (Carbons) und XEP-0313 (MAM).
