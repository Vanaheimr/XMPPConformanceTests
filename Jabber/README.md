# XMPP Console Client (.NET 10)

Ein vollständiger XMPP-Client für die Kommandozeile mit WebSocket-Transport und umfassender XEP-Unterstützung.

## Features

### Authentifizierung
| Methode | Status |
|---------|--------|
| **SCRAM-SHA-256** | ✅ Bevorzugt |
| **SCRAM-SHA-1** | ✅ Unterstützt |
| SASL PLAIN | ✅ Fallback |

### XEP-Unterstützung

| XEP | Name | Beschreibung |
|-----|------|--------------|
| RFC 7395 | WebSocket Transport | Firewall-freundlich, Port 443 |
| **XEP-0198** | Stream Management | Zuverlässige Zustellung, Resume |
| **XEP-0199** | XMPP Ping | Keepalive, RTT-Messung |
| **XEP-0030** | Service Discovery | Feature-Erkennung |
| **XEP-0115** | Entity Capabilities | Capability Hashing |
| **XEP-0333** | Chat Markers | Erweiterte Lesebestätigungen |
| XEP-0085 | Chat State Notifications | "tippt gerade..." |
| XEP-0184 | Message Receipts | Zustellbestätigung |
| XEP-0280 | Message Carbons | Multi-Device Sync |
| XEP-0060 | Publish-Subscribe | Event-basiert |

## Installation

```bash
# .NET 10 SDK erforderlich
dotnet build
dotnet run
```

## Verwendung

```bash
# Interaktiv
dotnet run

# Mit Parametern
dotnet run -- -j user@jabber.org -p geheim

# Mit WebSocket-URL
dotnet run -- -j user@server.com -p pw -w wss://xmpp.server.com/ws
```

## Kommandos

### Nachrichten
```
/to <jid>              Chat-Partner setzen
/msg <jid> <text>      Nachricht senden
/typing                Tippt-Status senden
/mark displayed        Nachricht als gelesen markieren
```

### Service Discovery (XEP-0030)
```
/disco server          Server-Features abfragen
/disco info <jid>      Features eines JIDs
/disco items <jid>     Services auflisten
/features              Eigene Features anzeigen
```

### Ping, Keepalive & Stream Management
```
/ping [jid]            Ping senden (misst RTT)
/keepalive [sek]       Keepalive Status/Interval setzen
/sm                    Stream Management Status
```

### Chat Markers (XEP-0333)
```
/mark received         Nachricht empfangen
/mark displayed        Nachricht gelesen
/mark ack              Nachricht bestätigt
```

### Verbindung
```
/who        Status anzeigen
/reconnect  Neu verbinden
/disconnect Trennen
```

## Keepalive (Anti-Timeout)

Der Client sendet automatisch alle 30 Sekunden einen Keepalive um Server-Timeouts zu verhindern:

```
/keepalive
Keepalive Status:
  Aktiviert: True
  Interval: 30s
  Methode: Stream Management <r/>

/keepalive 60      # Interval auf 60s setzen
/keepalive off     # Deaktivieren
```

**Methoden:**
- Mit Stream Management: Sendet `<r/>` (Request Ack) - sehr leichtgewichtig
- Ohne Stream Management: Sendet XEP-0199 Ping

## Architektur

```
┌─────────────────────────────────────────────────────┐
│                    Program.cs                        │
│         Console UI, Event-Handler, Commands          │
└───────────────────────┬─────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────┐
│               XmppConnection.cs                      │
│                                                      │
│  ┌─────────────┐  ┌───────────────┐  ┌───────────┐  │
│  │ WebSocket   │  │ SCRAM-SHA-1/  │  │  Stream   │  │
│  │ RFC 7395    │  │ 256 Auth      │  │  Mgmt     │  │
│  └─────────────┘  └───────────────┘  └───────────┘  │
└───────────────────────┬─────────────────────────────┘
                        │
    ┌───────────────────┼───────────────────┐
    │                   │                   │
    ▼                   ▼                   ▼
┌─────────┐      ┌────────────┐      ┌────────────┐
│ScramAuth│      │XepExtensions│     │XepAdvanced │
│         │      │            │      │            │
│SCRAM-   │      │- ChatState │      │- Ping      │
│SHA-1/256│      │- Receipts  │      │- Disco     │
│         │      │- Carbons   │      │- Caps      │
│         │      │- PubSub    │      │- SM        │
│         │      │            │      │- Markers   │
└─────────┘      └────────────┘      └────────────┘
```

## SCRAM-SHA-1/256 Authentifizierung

Sicherer als PLAIN - Challenge-Response ohne Klartext-Passwort:

```
Client → Server: n,,n=user,r=clientNonce
Server → Client: r=nonce,s=salt,i=4096
Client → Server: c=biws,r=nonce,p=clientProof
Server → Client: v=serverSignature
```

## Stream Management (XEP-0198)

Zuverlässige Nachrichtenzustellung:
- **Acknowledgements**: Server bestätigt empfangene Stanzas
- **Resume**: Nach Disconnect wird Stream wiederhergestellt
- **Keine verlorenen Nachrichten**: Unbestätigte werden erneut gesendet

```
/sm
Stream Management Status:
  Eingehend: 42
  Ausgehend: 38
  Unbestätigt: 2
  Resume möglich: true
```

## Service Discovery (XEP-0030)

```
/disco server
[*] Disco#info für jabber.org...
Identities:
  server/im (ejabberd)
Features (47):
  urn:xmpp:carbons:2
  urn:xmpp:sm:3
  urn:xmpp:mam:2
  ...
```

## Chat Markers (XEP-0333)

Erweiterte Lesebestätigungen:

| Marker | Symbol | Bedeutung |
|--------|--------|-----------|
| `received` | ✓ | Nachricht empfangen |
| `displayed` | 👁 | Nachricht gelesen |
| `acknowledged` | ✓✓ | Nachricht bestätigt |

## Entity Capabilities (XEP-0115)

Capability Hashing für effiziente Feature-Discovery:
- Hash der unterstützten Features wird in Presence mitgesendet
- Einmaliges Abfragen pro Hash, dann gecacht
- Vermeidet wiederholte Disco-Queries

## Spoofing-Schutz

Drei-Ebenen-Verteidigung:

1. **Receipts**: Nur vom erwarteten Empfänger
2. **Carbons**: Nur vom eigenen Bare-JID
3. **PubSub**: Nur vom konfigurierten Service

## Dateien

| Datei | Beschreibung |
|-------|--------------|
| `XmppConnection.cs` | WebSocket, Auth, Stanza-Routing |
| `ScramAuth.cs` | SCRAM-SHA-1/256 Implementierung |
| `XepExtensions.cs` | ChatState, Receipts, Carbons, PubSub |
| `XepAdvanced.cs` | Ping, Disco, Caps, StreamMgmt, Markers |
| `Roster.cs` | Kontaktverwaltung |
| `Program.cs` | Console UI |

## Bekannte Einschränkungen

- Kein SCRAM mit Channel Binding (SCRAM-SHA-*-PLUS)
- Keine End-to-End-Verschlüsselung (OMEMO)
- Kein Multi-User Chat (MUC/XEP-0045)
- Kein Message Archive Management (MAM/XEP-0313)

## Lizenz

MIT
