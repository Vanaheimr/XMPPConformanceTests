# Das Orakel — OMEMO gegen die Referenzimplementierung

Diese Testsammlung kann eine Klasse von Fehlern **grundsätzlich nicht finden**:
Sind beide Seiten derselbe Code, kommen sie auch dann überein, wenn beide
gleich falsch rechnen. In den Etappen D62 bis D65 war das fünfmal der Befund —
ein Info-String, eine Reihenfolge, eine Einbettung. Jedes Mal hätten sich zwei
Clients dieses Hauses bestens verstanden und kein einziger fremder.

Dagegen hilft nur eine Gegenstelle, die niemand hier geschrieben hat:
[python-omemo](https://github.com/Syndace/python-omemo) von Syndace, die
Referenzimplementierung für `urn:xmpp:omemo:2` — dieselbe Fassung, die wir
sprechen.

## Einrichten

```bash
wsl -d Debian -- python3 Jabber.Tests/XMPP/XEPs/Orakel/hole_orakel.py /tmp/omemo-oracle/lib
```

Das lädt die Wheels und entpackt sie — **kein pip, kein venv, kein sudo,
nichts am System verändert**. Wheels sind Zip-Dateien; entpackt auf dem
`PYTHONPATH` sind sie importierbar. Für einen Testaufbau ist das sogar besser
als eine Installation: reproduzierbar, und es bleibt nichts zurück.

Zwei Stolpersteine sind darin schon gelöst:

- **`cffi` gehört dazu**, auch wenn es nicht danach aussieht — ohne es findet
  XEdDSA seine native Bibliothek nicht und fällt auf eine Variante zurück, die
  einen Browser erwartet.
- **pydantic pinnt `pydantic-core` auf eine exakte Fassung.** Wer von jedem
  Paket schlicht das neueste nimmt, bekommt zwei, die nicht zueinander passen.
  Das ist die Arbeit, die pip sonst macht; für den einen Fall reichen zehn
  Zeilen.

Liegt das Verzeichnis nicht da, **überspringen sich die Tests selbst** — wie
die gegen Prosody und ejabberd. Ein Lauf ohne WSL soll nicht rot sein, nur
weniger aussagen.

## Was geprüft wird

In beide Richtungen, und das ist der Punkt:

| | |
|---|---|
| **Sie nimmt unser Bundle an** | prüft dabei unsere Signatur über den Signed PreKey mit ihrer eigenen Vorstellung davon, worüber sie geht |
| **Wir lesen, was sie schreibt** | Bundle-Kodierung, Reihenfolge der vier Diffie-Hellman, Info-String von X3DH, der `0xFF`-Vorspann, die Beigabe aus beiden IdentityKeys, Ratchet-Anfang, Info-Strings der Wurzelkette und des Nachrichtenschlüssels, die Konstanten `0x01`/`0x02`, Protobuf-Feldnummern, Einbettung des Geheimtexts, Kürzung des HMAC, Ableitung der Nutzlast |
| **Sie liest, was wir schreiben** | dasselbe von der anderen Seite — und die Trennung unseres `<key kex='true'/>` in Austausch und eingepackte Nachricht |

**Jeder einzelne dieser Punkte war in D62 bis D65 eine überlebende Mutation
oder ein Fund beim Lesen.** Dieser Aufbau hätte sie alle gefunden.

## Was nicht geprüft wird

- **Die SCE-Hülle (XEP-0420).** python-omemo überlässt sie der Anwendung, die
  es benutzt — eine Hülle, die hier im Orakel selbst gebaut würde, wäre keine
  fremde Prüfung, sondern dieselbe Annahme zweimal.
- **Das `<encrypted/>`-Element und die PEP-Knoten.** Beides liegt oberhalb der
  Schicht, die die Bibliothek anbietet.
- **Ein Gespräch über mehrere Nachrichten.** Geprüft ist der Anfang einer
  Sitzung, nicht ihr Verlauf.
- **Ein echter Client über eine echte Verbindung.** Conversations, Dino und
  Gajim sprechen überwiegend noch OMEMO 0.3.0
  (`eu.siacs.conversations.axolotl`); gegen die zu prüfen hiesse, erst eine
  zweite Fassung zu bauen.
