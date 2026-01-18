# XMPP Console Client (.NET 10)

Ein vollständiger XMPP-Client für die Kommandozeile mit **WebSocket-Transport** (RFC 7395), Auto-Reconnect und umfangreicher XEP-Unterstützung.

## Warum WebSocket?

| Aspekt | TCP+STARTTLS | WebSocket |
|--------|--------------|-----------|
| **Framing** | Streaming XML (komplex) | 1 Message = 1 Stanza ✅ |
| **Firewall** | Port 5222 (oft blockiert) | Port 443 ✅ |
| **Proxies** | Problematisch | Transparent ✅ |
| **Parsing** | Fragment-Zusammensetzung | Direkt verwendbar ✅ |
| **Cloud/LB** | Schwierig | Native Unterstützung ✅ |

## Features

### Core
- ✅ WebSocket Transport (RFC 7395 / XEP-0156)
- ✅ SASL PLAIN Authentifizierung
- ✅ Auto-Reconnect mit Exponential Backoff
- ✅ Roster-Management mit Gruppen
- ✅ Presence (Online-Status)
- ✅ 1:1 Chat

### XEP-Erweiterungen mit Spoofing-Schutz
| XEP | Name | Beschreibung |
|-----|------|--------------|
| XEP-0085 | Chat State Notifications | "tippt gerade..." |
| XEP-0184 | Message Delivery Receipts | Zustellbestätigungen |
| XEP-0280 | Message Carbons | Multi-Device Sync |
| XEP-0060 | Publish-Subscribe | Event-basierte Kommunikation |

## Installation

```bash
# .NET 10 SDK erforderlich
dotnet build
dotnet run
```

## Verwendung

### Starten
```bash
# Interaktiv (fragt nach Credentials)
dotnet run

# Mit Parametern
dotnet run -- -j user@jabber.org -p geheim

# Mit expliziter WebSocket-URL
dotnet run -- -j user@example.com -p pw -w wss://xmpp.example.com/ws
```

### Standard WebSocket-Endpunkte

Der Client versucht automatisch `wss://{domain}:5443/ws`. Bekannte Server:

| Server | WebSocket URL |
|--------|---------------|
| jabber.org | wss://jabber.org:5443/ws |
| conversations.im | wss://conversations.im:5443/ws |
| ejabberd | wss://{host}:5443/ws |
| Prosody | wss://{host}:5281/xmpp-websocket |

### Kommandos

#### Nachrichten
```
/to <jid>              Chat-Partner setzen
/msg <jid> <text>      Nachricht senden
/status [show] [text]  Status ändern
```

#### Kontakte
```
/roster [filter]   Alle Kontakte
/online            Nur Online
/add <jid>         Kontakt hinzufügen
/remove <jid>      Kontakt entfernen
```

#### Chat-Status (XEP-0085)
```
/typing    'Tippt gerade...'
/paused    'Hat aufgehört'
/gone      Chat verlassen
```

#### PubSub (XEP-0060)
```
/pubsub sub <node>           Abonnieren
/pubsub pub <node> <id> <x>  Veröffentlichen
/pubsub get <node>           Items abrufen
```

#### Verbindung
```
/who        Status anzeigen
/reconnect  Manuell verbinden
/disconnect Trennen
/quit       Beenden
```

## Auto-Reconnect

Bei Verbindungsverlust:

```
[!] Verbindung verloren
[*] Reconnect-Versuch 1/5 in 1.0s...
[*] Reconnect-Versuch 2/5 in 2.0s...
[*] Reconnect-Versuch 3/5 in 4.0s...
[+] Reconnect erfolgreich!
```

**Einstellungen** (in XmppConnection):
- `MaxReconnectAttempts = 5`
- `InitialReconnectDelay = 1s`
- `MaxReconnectDelay = 30s`

## Spoofing-Schutz

### Receipt-Spoofing (XEP-0184)
```
Nachricht an bob@server.com (ID: msg-123)
  ↓
Receipt von alice@evil.com → ⚠️ ABGELEHNT
Receipt von bob@server.com → ✓ OK
```

### Carbon-Spoofing (XEP-0280)
Carbons werden **nur** vom eigenen Bare-JID akzeptiert:
```
Carbon von eve@attacker.com → ⚠️ ABGELEHNT
Carbon von mein@account.com → ✓ OK
```

### PubSub-Spoofing (XEP-0060)
Events nur vom konfigurierten PubSub-Service.

## Architektur

```
┌────────────────────────────────────────────────────┐
│                   Program.cs                       │
│  Console UI, Event-Handler, Commands               │
└─────────────────────┬──────────────────────────────┘
                      │
┌─────────────────────▼──────────────────────────────┐
│              XmppConnection.cs                     │
│  - WebSocket (System.Net.WebSockets)               │
│  - RFC 7395 Framing (<open>, <close>)              │
│  - SASL Authentication                             │
│  - Auto-Reconnect mit Backoff                      │
│  - Stanza Routing + Spoofing-Checks                │
└─────────────────────┬──────────────────────────────┘
                      │
        ┌─────────────┼─────────────┐
        │             │             │
        ▼             ▼             ▼
┌───────────┐  ┌────────────┐  ┌─────────────────┐
│ Roster.cs │  │ XepExten-  │  │ XepExtensions   │
│           │  │ sions.cs   │  │                 │
│ - Items   │  │            │  │ - ChatState     │
│ - Groups  │  │ - Receipt  │  │ - ReceiptTrack  │
│ - Pres.   │  │   Tracker  │  │ - CarbonMgr     │
│ - Subscr. │  │ - Carbon   │  │ - PubSubMgr     │
└───────────┘  │   Manager  │  │ - PubSubBuild   │
               └────────────┘  └─────────────────┘
```

## Dateien

| Datei | Beschreibung |
|-------|--------------|
| `XmppConnection.cs` | WebSocket-Verbindung, SASL, Reconnect |
| `XepExtensions.cs` | XEP-0085, 0184, 0280, 0060 |
| `Roster.cs` | Kontaktverwaltung |
| `Program.cs` | Console UI |

## Beispiel-Session

```
  ╔═══════════════════════════════════════════╗
  ║      XMPP Console Client (.NET 10)        ║
  ║   WebSocket (RFC 7395) + Auto-Reconnect   ║
  ╚═══════════════════════════════════════════╝

[*] Verbinde zu wss://jabber.org:5443/ws...
[+] WebSocket verbunden
[*] SASL PLAIN Authentifizierung...
[+] Authentifizierung erfolgreich
[*] Resource Binding...
[+] Verbunden als: user@jabber.org/console-12345
[*] Aktiviere Message Carbons...
[+] Message Carbons aktiviert
[*] Lade Roster...
[+] Roster geladen: 5 Kontakte
[+] Online!

> /to alice@jabber.org
Chat mit: alice@jabber.org

[alice@jabber.org] > Hallo!
  → Gesendet an alice@jabber.org
✓ Zugestellt an alice@jabber.org

✏️ alice@jabber.org tippt...
[10:15:32] alice@jabber.org: Hi! Wie geht's?

📤 Ich → bob@jabber.org: Test vom Handy
  (Carbon von anderem Gerät)

🔄 Verbindung verloren, versuche Reconnect...
[*] Reconnect-Versuch 1/5 in 1.0s...
✅ Reconnect erfolgreich!
```

## Bekannte Einschränkungen

- Nur SASL PLAIN (kein SCRAM-SHA-1/256)
- Keine End-to-End-Verschlüsselung (OMEMO)
- Kein Multi-User Chat (MUC/XEP-0045)
- WebSocket-Zertifikate werden nicht strikt geprüft

## Nächste Schritte

- [ ] SCRAM-SHA-256 Authentifizierung
- [ ] XEP-0045: Multi-User Chat
- [ ] XEP-0384: OMEMO Encryption
- [ ] XEP-0313: Message Archive Management
- [ ] DNS SRV/TXT Lookup für WebSocket-Discovery

## Lizenz

MIT
