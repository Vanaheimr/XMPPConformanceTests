# XMPP Console Client (.NET 10)

Ein vollständiger XMPP-Client für die Kommandozeile mit TLS, SASL-Authentifizierung und umfangreicher XEP-Unterstützung.

## Features

### Core
- ✅ TLS 1.2/1.3 (STARTTLS)
- ✅ SASL PLAIN Authentifizierung
- ✅ Resource Binding
- ✅ Roster-Management mit Gruppen
- ✅ Presence (Online-Status)
- ✅ 1:1 Chat

### XEP-Erweiterungen
| XEP | Name | Status |
|-----|------|--------|
| XEP-0085 | Chat State Notifications | ✅ Vollständig |
| XEP-0184 | Message Delivery Receipts | ✅ Mit Spoofing-Schutz |
| XEP-0280 | Message Carbons | ✅ Mit Spoofing-Schutz |
| XEP-0060 | Publish-Subscribe | ✅ Mit Spoofing-Schutz |

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

# Mit eigenem Server
dotnet run -- -j user@domain.com -p geheim -s jabber.domain.com --port 5222
```

### Kommandos

#### Nachrichten
```
/to <jid>              Chat-Partner setzen (dann direkt tippen)
/msg <jid> <text>      Einzelne Nachricht senden
/status [show] [text]  Status ändern (away/chat/dnd/xa)
```

#### Kontakte (Roster)
```
/roster [filter]       Alle Kontakte anzeigen
/online                Nur Online-Kontakte
/add <jid> [name] [gruppen]  Kontakt hinzufügen
/remove <jid>          Kontakt entfernen
/info <jid>            Kontakt-Details anzeigen
/groups                Alle Gruppen anzeigen
```

#### Kontaktanfragen
```
/pending               Ausstehende Anfragen anzeigen
/accept [jid]          Anfrage akzeptieren
/deny [jid]            Anfrage ablehnen
```

#### Chat-Status (XEP-0085)
```
/typing                'Tippt gerade...' senden
/paused                'Hat aufgehört zu tippen' senden
/gone                  Chat verlassen
```

#### PubSub (XEP-0060)
```
/pubsub                Hilfe anzeigen
/pubsub sub <node>     Node abonnieren
/pubsub unsub <node>   Abo beenden
/pubsub pub <node> <id> <data>  Item veröffentlichen
/pubsub get <node> [max]  Items abrufen
/pubsub create <node>  Node erstellen
/pubsub delete <node>  Node löschen
```

#### Sonstiges
```
/who                   Eigene JID und Status anzeigen
/carbons               Message Carbons Status
/raw                   XML-Debug-Anzeige
/quit                  Beenden
```

## Spoofing-Schutz

Der Client implementiert Schutzmaßnahmen gegen Message-Spoofing:

### Receipt-Spoofing (XEP-0184)
```
Sende Nachricht an bob@server.com (ID: msg-123)
  ↓
Receipt von alice@evil.com mit ID msg-123
  → ⚠️ ABGELEHNT: Erwarteter Absender war bob@server.com

Receipt von bob@server.com mit ID msg-123
  → ✓ Akzeptiert
```

### Carbon-Spoofing (XEP-0280)
```
Meine JID: ahzf@graphdefined.com

Carbon-Nachricht von eve@attacker.com
  → ⚠️ ABGELEHNT: Carbons dürfen nur vom eigenen Account kommen

Carbon-Nachricht von ahzf@graphdefined.com
  → ✓ Akzeptiert (eigener Bare-JID)
```

### PubSub-Spoofing (XEP-0060)
```
Erwarteter PubSub-Service: pubsub.graphdefined.com

Event von fake-pubsub@evil.com
  → ⚠️ ABGELEHNT: Falscher Service

Event von pubsub.graphdefined.com
  → ✓ Akzeptiert
```

## Architektur

```
┌─────────────────────────────────────────────────────────────┐
│                      Program.cs (UI)                        │
│  - Konsolen-Interface                                       │
│  - Event-Handler für alle XEPs                              │
│  - Command-Processing                                       │
└────────────────────────┬────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────┐
│                  XmppConnection.cs                          │
│  - TCP/TLS Connection                                       │
│  - SASL Authentication                                      │
│  - XML Stream Processing                                    │
│  - Stanza Routing mit Spoofing-Checks                       │
└────────────────────────┬────────────────────────────────────┘
                         │
         ┌───────────────┼───────────────┐
         │               │               │
         ▼               ▼               ▼
┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐
│  Roster.cs  │  │ XepExtens.  │  │    XepExtensions.cs     │
│             │  │             │  │  - ChatState            │
│  - Items    │  │  - Receipt  │  │  - ReceiptTracker       │
│  - Groups   │  │    Tracker  │  │  - CarbonManager        │
│  - Presence │  │  - Carbon   │  │  - PubSubManager        │
│  - Subscr.  │  │    Manager  │  │  - PubSubBuilder        │
└─────────────┘  └─────────────┘  └─────────────────────────┘
```

## Dateien

| Datei | Zeilen | Beschreibung |
|-------|--------|--------------|
| `XmppConnection.cs` | ~750 | Hauptverbindung, TLS, SASL, Stanza-Routing |
| `XepExtensions.cs` | ~500 | XEP-0085, 0184, 0280, 0060 Implementierungen |
| `Roster.cs` | ~270 | Kontaktverwaltung, Presence-Tracking |
| `Program.cs` | ~940 | UI, Commands, Event-Handler |

## Beispiel-Session

```
╔═══════════════════════════════════════╗
║     XMPP Console Client (.NET 10)     ║
║        TLS + SASL + Roster            ║
╚═══════════════════════════════════════╝
[*] Verbinde zu jabber.graphdefined.com:5222...
[*] STARTTLS wird initiiert...
[+] TLS Tls13 etabliert
[*] SASL PLAIN Authentifizierung...
[+] Authentifizierung erfolgreich
[*] Resource Binding...
[+] Verbunden als: ahzf@graphdefined.com/console-12345
[*] Aktiviere Message Carbons...
[+] Message Carbons aktiviert
[*] Lade Roster...
[+] Roster geladen: 3 Kontakte
[+] Online!

> /to bob@jabber.org
Chat mit: bob@jabber.org

[bob@jabber.org] > Hallo Bob!
  → Gesendet an bob@jabber.org
✓ Zugestellt an bob@jabber.org

[10:30:15] bob@jabber.org: Hey! Wie geht's?
✏️ bob@jabber.org tippt...

[bob@jabber.org] > /typing
⌨️ Typing-Indicator gesendet an bob@jabber.org

[bob@jabber.org] > Gut, danke!
  → Gesendet an bob@jabber.org

📤 Ich → alice@jabber.org: Test von anderem Gerät
```

## Bekannte Einschränkungen

- Nur SASL PLAIN (kein SCRAM-SHA-1/256)
- Kein automatisches Reconnect
- Keine End-to-End-Verschlüsselung (OMEMO)
- Keine Multi-User Chats (MUC)
- Zertifikat-Validierung ist permissiv (für Demo)
