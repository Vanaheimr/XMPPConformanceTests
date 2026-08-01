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
| SASL PLAIN | ⚠️ Letzter Fallback |
| SCRAM-*-PLUS (Channel Binding) | ❌ Nicht implementiert |

Gewählt wird der stärkste angebotene Mechanismus — nach der Rangfolge, nicht
nach der Reihenfolge der Ankündigung. Gegen den Downgrade halten zwei
Untergrenzen, beide auf `XMPPConnection`:

| Eigenschaft | Wirkung |
|---|---|
| `PinnedSaslMechanism` | Womit die letzte Anmeldung gelang. Wirkt von selbst, aber erst ab der zweiten Verbindung. |
| `MinimumSaslMechanism` | Was der Aufrufer verlangt. Wirkt vom ersten Rahmen an, muss aber gesetzt werden. |

Beide werden geprüft, *bevor* das `<auth/>` hinausgeht — bei PLAIN stünde das
Passwort in genau diesem Rahmen. Bietet der Server weniger an als eine der
Untergrenzen verlangt, kommt keine Verbindung zustande und es wird kein
Reconnect versucht.

Die Anheftung ist ein Trust-On-First-Use: Steht der Zwischenmann schon beim
allerersten Verbindungsaufbau dazwischen, heftet sie sein Downgrade an, statt
es abzuwehren. Wer weiß, was sein Server kann, setzt deshalb zusätzlich
`MinimumSaslMechanism`. Was sie ohne Zutun abwehrt, ist der Angriff, der sich
lohnt: Der Client kommt nach jedem Abriss von allein wieder, und ein Abriss
lässt sich erzwingen.

## XEP-Unterstützung

Legende: ✅ funktionsfähig · ⚠️ implementiert mit bekannten Lücken · 🚧 vorhanden, aber standardmäßig aus · ⛔ bewusst nicht umgesetzt

| XEP | Name | Status | Anmerkung |
|-----|------|--------|-----------|
| XEP-0013 | Flexible Offline Message Retrieval | ⛔ | Von der XSF als *Deprecated* geführt (Fassung 1.3, 2021-05-04): „Implementation of the protocol described herein is not recommended." Die Offline-Ablage bleibt beim automatischen Nachreichen nach RFC 6121 §8.5.2.2.1 und XEP-0160 — siehe [WORKPLAN.md](../WORKPLAN.md), D37 |
| XEP-0030 | Service Discovery | ✅ | disco#info und disco#items, abgefragt und beantwortet. Das `node` der Anfrage wird nach §3.2 gespiegelt; beantwortet werden nur Nodes, die diese Entity bezeichnen — der Caps-Node mit und ohne aktuelles `#ver` (XEP-0115 §6.2). Jeder andere, auch ein veraltetes `ver`, bekommt `<item-not-found/>` mit der Anfrage zurück. disco#items antwortet aus `DiscoManager.LocalItems` (leer als Vorgabe: ein Client hat keine Untereinheiten); ein `node` ist dort ein Ast im Baum und wird abgewiesen. Der Testserver führt keine Nodes und weist jeden ab |
| XEP-0060 | Publish-Subscribe | ⚠️ | Events werden geparst und als `iq set` bestätigt; ausgehend werden die IQ-Ergebnisse nicht korreliert — ein Abonnement gilt sofort als bestehend, auch wenn der Dienst es ablehnt, und `OnSubscriptionResult` wird nie ausgelöst. Steht unter „Optional", siehe [WORKPLAN.md](../WORKPLAN.md), D38 |
| XEP-0085 | Chat State Notifications | ✅ | Senden + Empfangen |
| XEP-0115 | Entity Capabilities | ✅ | ver-String nach §5.1 vollständig, samt `xml:lang` und XEP-0128-Formularen, gegen beide Vektoren aus §5.2 und §5.3 geprüft; Antworten werden nach §5.4 verifiziert, sonst kein Cache-Eintrag |
| XEP-0128 | Service Discovery Extensions | ✅ | Fremde Formulare werden gelesen, eigene über `DiscoManager.LocalForms` ausgeliefert; beide gehen in den ver-String ein. Standardmäßig leer — siehe unten |
| XEP-0156 | Discovering Alternative XMPP Connection Methods | ✅ | Nur der HTTP-Weg, und nur so weit er sicher ist: `host-meta` wird ausschliesslich über HTTPS geladen, übernommen werden ausschliesslich `wss://`-Endpunkte. BOSH (`xbosh`) wird gelesen und übergangen — dieser Client spricht es nicht. Der DNS-Weg über `_xmppconnect` fehlt nicht, er ist aus dem XEP entfernt worden |
| XEP-0160 | Best Practices for Handling Offline Messages | ✅ | Serverseitig: `normal` und `chat` werden abgelegt, `groupchat` abgelehnt, `headline` und `error` verworfen; ein `chat` mit ausschliesslich Tippstatus-Inhalt (XEP-0085) ebenfalls, und zwar ohne Fehler an den Absender. Nachgereicht bei der nächsten nicht-negativen verfügbaren Presence, als `msgoffline` angekündigt. Gilt auch für Nachrichten von anderen Servern |
| XEP-0184 | Message Delivery Receipts | ✅ | Mit Spoofing-Schutz |
| XEP-0203 | Delayed Delivery | ✅ | Der Server stempelt nachgereichte Nachrichten, der Client liest den Stempel: `XMPPMessage.Timestamp` ist die Zeit, zu der die Nachricht **geschrieben** wurde, `ReceivedAt` die des Empfangs, `IsDelayed` der Unterschied. Gelesen wird nur an der äusseren Stanza — ein Carbon bringt den Stempel seiner inneren Nachricht mit —, und nur mit Zonenangabe: eine Uhrzeit ohne Zone ist keine (D59) |
| XEP-0198 | Stream Management | ✅ | Gegen Prosody 13 und ejabberd 24.12 geprüft, an per Default, mit Wiederaufnahme; nach dem Nachsenden wird eine Bestätigung angefordert, damit die Warteschlange auch ohne Keepalive leer wird; auch die Abweisung wird ausgewertet — ein `h` im `<failed/>` bestätigt, was der Server noch verarbeitet hat |
| XEP-0199 | XMPP Ping | ✅ | Senden, Beantworten, RTT-Messung |
| XEP-0280 | Message Carbons | ✅ | Mit Spoofing-Schutz |
| XEP-0308 | Last Message Correction | ✅ | Empfangen: `XMPPMessage.ReplacesId` nennt die abgelöste Nachricht, `IsCorrection` die Tatsache. Senden: `CorrectLastMessageAsync` berichtigt die letzte Nachricht **an denselben Empfänger** (Abschnitt 5) und wird selbst zur letzten, sodass sich eine Berichtigung berichtigen lässt. In der Konsole `/fix <text>`; angekündigt in disco#info (D60) |
| XEP-0333 | Chat Markers | ✅ | Senden + Empfangen, Namespace-geprüft gegen Verwechslung mit XEP-0184 |
| XEP-0352 | Client State Indication | ✅ | Beide Seiten. Der Server kündigt `<csi/>` nach der Anmeldung an (§4.1) und antwortet auf `<active/>`/`<inactive/>` nicht (§4.2). Zurückgehalten wird nur, was später noch wahr ist: Presence wartet und **die letzte je Full-JID löst die früheren ab** (§3), eine Nachricht mit Text, ein `iq`, ein Fehler und jede Nonza gehen sofort hinaus, ein Chat State (XEP-0085) wird fallengelassen — er wäre beim Nachliefern nicht verspätet, sondern falsch. Zurückgehaltenes geht **vor** der Stanza hinaus, die den Puffer leert (RFC 6120 §10.1), und beim Verbindungsende in den Puffer der unbestätigten Stanzas. Obergrenze `MaxHeldWhileInactive` (Vorgabe 100); beim Überlauf geht der Puffer hinaus, statt etwas wegzuwerfen. Nach einer Wiederaufnahme gilt wieder „aktiv" (§5.2) — der Client erklärt sich deshalb nach jedem Aufbau erneut. In der Konsole `/csi aktiv|inaktiv` (D61) |

## RFC-Konformität

### RFC 6120 — XMPP Core

| Bereich | Status |
|---------|--------|
| TLS (§5) | ⚠️ `wss://` über den WebSocket-Transport; `XMPPConnection.ServerCertificateValidator` erlaubt eine eigene Zertifikatsprüfung, `null` überlässt sie dem Betriebssystem. Kein STARTTLS (§5.4) — WebSocket bringt TLS unter sich mit, ein Klartext-`ws://` wird aber nicht verweigert |
| SASL-Aushandlung und -Durchführung (§6) | ✅ Client und Server; der Client nimmt den stärksten angebotenen Mechanismus und nie einen schwächeren als beim letzten Mal, der Server lehnt einen nicht angebotenen ab |
| SASL-Abbruch (§6.4.4) | ✅ `<abort/>` wird mit `<failure><aborted/></failure>` beantwortet, der halb begonnene SCRAM-Austausch verworfen und der Stream **nicht** beendet — ein Abbruch ist ein vorgesehener Schritt, kein Verstoss. Auf der Client-Verbindung und auf dem S2S-Stream; der Initiator eines S2S-Streams beantwortet ihn nicht, er wäre der Absender |
| Directory Harvesting (§13.11) | ⚠️ Ein unbekannter Benutzername bekommt denselben SCRAM-Austausch wie ein bekannter — erfundene Zugangsdaten aus dem Namen und einem Serverschlüssel, Abweisung erst am Beweis. Sonst stünde die Auskunft im Ablauf statt im Fehlerwort. Der Serverschlüssel lebt im Prozess, über einen Neustart hinweg wechseln die erfundenen Salts; bei PLAIN unterscheidet sich weiterhin die Laufzeit. Die übrigen Gegenmassnahmen des Abschnitts — Ratenbegrenzung, Fehlerauskunft nur an Angemeldete — fehlen |
| Resource Binding (§7) | ✅ `XMPPConnection.Resource` (Vorgabe `console-<pid>`, `null` überlässt die Wahl dem Server); auf `<conflict/>` folgt ein zweiter Versuch ohne Wunsch, jede andere Ablehnung bricht ab |
| Legacy Session (RFC 3921) | ✅ Wird übersprungen, wenn das Feature selbst `<optional/>` trägt |
| Stanza-Fehler (§8.3) | ✅ Typ, Bedingung, Text und `by` werden geparst; offene Anfragen scheitern statt scheinbar zu gelingen |
| Antwort auf unbehandelte IQs (§8.2.3 Regel 3) | ✅ Unbekannte `iq get`/`set` werden mit `<service-unavailable/>` beantwortet |
| Unmögliche Adressen (§8.3.3.8, §8.1.1.1) | ✅ Ist der Wert des `to` kein JID nach RFC 7622, antwortet der Server mit `<jid-malformed/>` (Fehlerart `modify`) und stellt nicht zu — für `message`, `presence` und `iq` an derselben Stelle, vor jeder Weiche. **Beide Herkünfte:** Von einer Gegenstelle wird auch das `from` geprüft, und zwar vor der Frage, für welche Domain sie sprechen darf — `DomainOf` auf etwas anzuwenden, das kein JID ist, vergleicht Bruchstücke. Ein unmögliches `from` beendet nach §8.1.1.1 den Stream mit `<invalid-from/>`, ein unmögliches `to` kostet nur die Stanza (D51, D53). Absender der Ablehnung ist der Server selbst und nicht der gemeinte Empfänger: Die Adresse ist keine, also hat dort niemand hineingesehen. Eine Stanza **ohne** `to` ist davon nicht betroffen (§8.1.1.1), und auf eine Fehler-Stanza folgt kein Fehler (§8.3.1) — verworfen wird sie trotzdem. Geprüft wird mit derselben RFC-7622-Prüfung, die der Client für seine eigenen Adressen benutzt |
| Prüfung des IQ-Typs (§8.2.3 Regel 2) | ✅ Fehlt das `type`-Attribut oder trägt es einen anderen Wert als `get`, `set`, `result` oder `error`, folgt `<bad-request/>` mit der Fehlerart `modify` (§8.3.3.1). Geprüft wird in beiden Rollen, die der Abschnitt nennt: vom Client als Empfänger und vom Server als „intermediate router" — dort **vor** jeder Zustellung, also auch für das, was an die Serveradresse selbst geht, an einen hiesigen Empfänger oder über die Grenze. Ebenso für das, was von einer Gegenstelle hereinkommt. Ohne `id` geht die Ablehnung trotzdem hinaus und trägt dann keine |
| Stream-Fehler (§4.9) | ✅ Geparst; nach einer nicht wiederholbaren Bedingung unterbleibt der Reconnect |
| Weiche für eingehende Rahmen (§8.1) | ✅ Entschieden wird am **Elementnamen**, nicht an einem Präfix: `<iqbogus/>` ist kein `iq`, `<presence-probe/>` keine `presence`, `<opencast/>` keine Stream-Eröffnung. Ein Namensraum-Präfix ändert den Typ nicht (`<client:iq/>` ist ein `iq`, `<stream:features/>` und `<features/>` sind dasselbe Element) |
| Unbekanntes Element auf Stream-Ebene (§4.9.3.24) | ✅ Auf beiden Streams — Client wie S2S — folgt `<unsupported-stanza-type/>`, und der Stream endet (§4.9.1.1). Gilt auch für ein unbekanntes Element in einem **bekannten** Namensraum: `<enabled/>` ist ein richtiges XEP-0198-Element, kommt aber vom Server und nicht vom Client. Für den S2S-Stream wurde vorher gemessen statt vermutet: Über den vollen Lauf gegen Prosody und ejabberd, ausgehend wie eingehend, kam dort kein einziger unbekannter Rahmen an. Ein Rahmen **ohne** Element ist kein unbekanntes Element und wird übergangen — Leerraum ist als Keepalive erlaubt (§4.6.1) |

### RFC 6121 — Instant Messaging und Presence

| Bereich | Status |
|---------|--------|
| Roster abrufen, hinzufügen, entfernen, Gruppen | ✅ |
| Ergebnis ersetzt den Zwischenspeicher (§2.1.4) | ✅ Ein Kontakt, der bei abgemeldetem Client entfernt wurde, ist danach weg — vorher blieb er stehen |
| Roster-Pushes anwenden | ✅ Ergänzend und nicht ersetzend: Ein Push trägt nur die geänderten Einträge |
| Absender-Validierung von Roster-Pushes (§2.1.6) | ✅ Nur ohne `from` oder mit dem eigenen Bare-JID; sonst verworfen und als Spoofing gemeldet |
| Roster-Versionierung (§2.6) | ✅ Client und Server; `<ver/>` wird angekündigt, unveränderte Roster kommen als leeres Ergebnis, Pushes tragen die neue Fassung. Die Fassung ist ein Streuwert über den Inhalt — abschaltbar über `XMPPServer.OfferRosterVersioning` |
| Presence-Subscription anfragen/annehmen/ablehnen | ✅ |
| Eingehende `subscribed`/`unsubscribed`/`unsubscribe` | ✅ Ändern den Subscription-Zustand und gelten nicht als Anwesenheit |
| Message-Typen (§5.2.2) | ✅ `chat`, `groupchat`, `headline`, `normal`, `error`; fehlender oder unbekannter Wert gilt als `normal`. Auf `groupchat` und `headline` wird nicht von selbst geantwortet — eine Quittung in einen Raum sähen alle Anwesenden |
| Zustellregeln nach Typ (§8.5) | ✅ An den Bare-JID: `groupchat` wird mit `<service-unavailable/>` abgelehnt, `error` still verworfen, `headline` an **alle** Resourcen mit nicht-negativer Priorität, `normal`/`chat` an eine. An eine passende Resource: alles, auch `groupchat` und `error` (§8.5.3.1). An eine Resource, die es nicht gibt: `chat` wie an das Konto (§8.5.3.2.1), alles andere still verworfen. Gilt für Nachrichten von hiesigen Clients **und** von anderen Servern — der Abschnitt spricht von einer „inbound stanza" und unterscheidet die Herkunft nicht. Eine Ablehnung findet den Rückweg über die Grenze |
| Offline-Ablage (§8.5.2.2.1) | ✅ Ohne erreichbare Resource werden `normal` und `chat` abgelegt und bei der nächsten nicht-negativen verfügbaren Presence nachgereicht — mit XEP-0203-Stempel, über einen Neustart hinweg und als `msgoffline` in disco#info angekündigt. Auch für Nachrichten von anderen Servern, und das ist der Regelfall. Abschaltbar über `XMPPServer.StoreOfflineMessages`; dann bekommt der Absender `<service-unavailable/>`, was derselbe Abschnitt gleichrangig zulässt. Obergrenze `MaxStoredOfflineMessages` (Vorgabe 100): Ist sie erreicht, wird die neue Nachricht abgewiesen und keine abgelegte verdrängt |
| IQ-Zustellregeln (§8.5.1, §8.5.2.1.3, §8.5.2.2.3, §8.5.3.2.3) | ✅ Eine Anfrage an einen Bare-JID wird nicht zugestellt, sondern vom Server mit `<service-unavailable/>` beantwortet — genau einmal, und für ein unbekanntes Konto ebenso, damit die Antwort keine Konten verrät. An eine passende Resource wird zugestellt; ohne passende Resource antwortet der Server. Ein `result` oder `error` wird nie beantwortet (RFC 6120 §8.2.3 Regel 4) und an einen Bare-JID nicht verteilt. Gilt für beide Herkünfte |
| Anfrage an die Serveradresse (§8.2.3 Regel 3) | ✅ Ping (XEP-0199) und disco#info (XEP-0030) beantwortet der Server für sich selbst — einem hiesigen Client wie einer Gegenstelle, denn die Auskunft hängt nicht daran, wer fragt; nur der Rückweg unterscheidet sich. Was er nicht kennt, bekommt `<service-unavailable/>` statt Schweigen. **Nicht** darüber erreichbar sind Binding, Legacy Session, Carbons und der Roster: Die ändern den Zustand einer Sitzung oder gehören einem Konto — ein fremder Server, der nach dem Roster fragt, bekommt dieselbe Absage wie für jede unbekannte Anfrage |
| Nachricht an ein unbekanntes Konto (§8.5.1) | ✅ Der Abschnitt lässt die Wahl zwischen `<service-unavailable/>` und Schweigen, aber sie muss dieselbe sein wie für ein vorhandenes Konto, das gerade nicht zusieht — sonst beantwortet sie die Frage „gibt es dieses Konto?". Gefragt wird deshalb nicht, ob es ein Konto gibt, sondern ob die Ablage die Nachricht annähme: für ein unbekanntes ist sie leer, und eine leere nimmt an, solange überhaupt etwas hineinpasst. Ist die Ablage aus oder voll, bekommen beide `<service-unavailable/>`; ist sie an, schweigt der Server für beide. Abgelegt wird für ein unbekanntes Konto nichts (D52) |
| IQ-Prüfung gegen Presence-Lecks (§8.5.3.1) | ✅ Eine Anfrage an eine Resource wird nur zugestellt, wenn der Empfänger seine Presence mit dem Fragenden teilt — über den Roster (`from` oder `both` in **seiner** Hälfte) oder über gerichtete Presence (§4.6). Sonst dieselbe Antwort wie für eine Resource, die es nicht gibt; aus der Ablehnung lässt sich also nichts herauslesen. Für `result` und `error` gilt sie nicht — die muss der Server nach demselben Abschnitt zustellen |
| Gerichtete Presence (§4.6) | ✅ Je Resource vermerkt, geleert bei der Abmeldung, zurückgenommen bei gerichtetem `unavailable`, und ebenso, wenn der Empfänger uns seinerseits eine Abmeldung schickt (§4.6.1, MUSS und SOLL). Wird die Resource unverfügbar — durch eigene Abmeldung oder Verbindungsabriss —, geht die Abmeldung an alle Empfänger gerichteter Presence, die sie nicht schon über den Roster bekommen (§4.6.3 Regel 2). Eine Statusänderung mitten in der Sitzung beendet die Zusage nicht |
| Presence-Zustellregeln (§8.5.2.1.2, §8.5.3.1) | ✅ Verfügbare und unverfügbare Presence geht an den Bare-JID an alle Resourcen, an eine Full-JID an die passende, sonst still ins Leere (§8.5.1, §8.5.3.2.2) — für beide Herkünfte |
| Presence-Probe (§4.3) | ✅ Beantwortet der Server selbst und stellt sie keinem Client zu, gleich ob sie von einem hiesigen Client oder von einer Gegenstelle kommt. Eine Probe an eine fremde Domain schickt er hinaus (§4.3.1). Geantwortet wird nur, wenn der Fragende im Roster des Befragten mit `from` oder `both` steht; sonst Schweigen, das auch ein unbekanntes Konto nicht verrät (§8.5.1 lässt die Wahl) |
| Presence-Priorität (§4.7.2.3) | ✅ Gelesen und beachtet; eine negative Priorität bekommt nichts, was an den Bare-JID ging, bleibt aber gerichtet ansprechbar. Der Client setzt sie über `XMPPConnection.PresencePriority` |

### RFC 7395 — XMPP über WebSocket

| Bereich | Status |
|---------|--------|
| Subprotokoll `xmpp`, `<open/>`/`<close/>`-Framing | ✅ |
| Close-Handshake | ✅ `<close/>` wird gesendet, dann bis zu 3 s auf die Gegenseite gewartet, danach Socket-Abbruch |
| Endpunkt-Discovery (XEP-0156 / `host-meta`) | ✅ Ohne angegebenen Endpunkt wird `https://<domain>/.well-known/host-meta.json` und danach `.../host-meta` gelesen; nur `wss://`-Adressen werden genommen. Ohne Fund bleibt es bei `wss://<domain>:5443/ws` |

Der Vorgabe-Port ist ejabberd-spezifisch und greift nur, wenn die Domain kein
`host-meta` ausliefert. Wer ihn nicht will, gibt die URL an, z. B. Prosody:
`wss://<host>:5281/xmpp-websocket` — ein angegebener Endpunkt wird nie
überstimmt.

### RFC 5802 / RFC 7677 — SCRAM

| Bereich | Status |
|---------|--------|
| Vier-Schritt-Handshake | ✅ |
| Nonce-Prüfung gegen MITM | ✅ |
| Server-Signatur-Verifikation (konstante Laufzeit) | ✅ Zwingend — ein `<success/>` ohne server-final-message bricht den Aufbau ab |
| SASLprep (RFC 4013) | ✅ Vollständig: Abbildung, NFKC, Verbotstabellen, nicht zugewiesene Codepoints und die Bidi-Regeln; gegen die Beispieltabelle aus §3 geprüft |
| Channel Binding (RFC 9266 `tls-exporter`) | ❌ |

### RFC 7622 — JID-Behandlung

`JidUtilities` zerlegt, prüft und vergleicht JIDs nach RFC 7622; geprüft gegen
beide Beispieltabellen aus §3.5 (fünfzehn gültige und acht ungültige Adressen).

| Regel | Stand |
|---|---|
| Zerlegung in der Reihenfolge aus §3.2 (erst `/`, dann `@`) | ✅ |
| Localpart: UsernameCaseMapped, plus die Ausschlüsse aus §3.3.1 | ✅ Abbildungsregeln vollständig, IdentifierClass aus den abgeleiteten Eigenschaften nach RFC 8264 §8 |
| Resourcepart: OpaqueString, **nicht** kleingeschrieben | ✅ ebenso, mit der FreeformClass |
| Domainpart: kleingeschrieben, NFC | ✅ IDNA2008 Label für Label (RFC 5891/5892), Punycode selbst gerechnet (RFC 3492), Bidi-Regel nach RFC 5893 über einer aus `DerivedBidiClass.txt` erzeugten Tabelle |
| Höchstlänge 1023 Oktette je Teil | ✅ |
| Vergleich: Local-/Domainpart schreibweisenunabhängig, Resourcepart nicht | ✅ |

Die Klassenzugehörigkeit kommt aus `Precis.DerivedProperty` und damit aus der
Leiter in RFC 8264 §8: Ausnahmeliste (RFC 5892 §2.6), Unassigned, ASCII7,
JoinControl, alte Hangul-Jamo, ignorierbare Zeichen, Controls, HasCompat,
LetterDigits, OtherLetterDigits, Spaces, Symbols, Punctuation — in dieser
Reihenfolge, denn viele Codepoints stehen in mehreren dieser Kategorien.
`Default_Ignorable_Code_Point`, `Noncharacter_Code_Point` und
`Hangul_Syllable_Type` liefert .NET nicht; sie stehen als Bereichstabellen im
Quelltext, mit der Unicode-Fassung benannt, aus der sie stammen (15.1.0).

Der Domainpart geht durch `Idna` — dieselben Bausteine, aber die Leiter aus
RFC 5892 §1 statt der aus RFC 8264 §8, und darum andere Antworten: Ein
Unterstrich gehört in einen Localpart und in kein Label, ein Symbol in einen
Resourcepart und in kein Label. Ein A-Label (`xn--…`) wird dekodiert, auf die
Label-Regeln geprüft und zurückgerechnet; ergibt die Rückrechnung eine andere
Schreibweise, wird es abgewiesen. Adressliterale (`127.0.0.1`, `[::1]`) sind
nach RFC 7622 §3.2 ausgenommen.

Trägt ein einziges Label rechtsläufige Zeichen, ist der ganze Name ein
*Bidi domain name* (RFC 5893 §2), und dann müssen **alle** Labels die sechs
Bedingungen erfüllen — auch die aus reinem ASCII. `9abc.example` ist deshalb ein
gültiger Domainname und `9abc.אבג` keiner. Die Bidi-Klassen stehen in
`Jabber/Common/BidiClasses.cs`, erzeugt von `tools/unicode/generate-bidiclass.py`
aus `DerivedBidiClass.txt`.

Die kontextabhängigen Regeln aus RFC 5892 Anhang A sind vollständig umgesetzt —
für Localparts wie für Domain-Labels. Sie hängen nicht am Codepoint, sondern an
seiner Umgebung: `col·la` ist ein katalanisches Wort und ein gültiger Localpart,
`co·lla` ist keiner. Die dafür nötigen Eigenschaften
(`Canonical_Combining_Class`, `Joining_Type`, `Script`) stehen in
`Jabber/Common/ContextTables.cs`, erzeugt von
`tools/unicode/generate-contexttables.py`.

**Eine bewusste Abweichung:** Beispiel 18 der Tabelle 2
(`juliet@example.com/ foo`, führendes Leerzeichen im Resourcepart) wird
angenommen. Die Tabelle führt es als Nicht-JID, aber die Regel dazu fehlt — das
OpaqueString-Profil lässt Leerzeichen ausdrücklich zu. Für einen Router ist
Annehmen ausserdem die vorsichtigere Wahl: Eine Adresse zurückzuweisen, die
andere Server für gültig halten, verliert Nachrichten.

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
/csi [aktiv|inaktiv]      Client State Indication (XEP-0352)
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

## Verbindungsaufbau: gelungen oder geworfen

`ConnectAsync` **wirft**, wenn der Aufbau scheitert — den ursprünglichen Fehler,
nicht eine Hülle darum: `AuthenticationException` bei abgelehnter Anmeldung,
`XMPPProtocolException` bei einer gescheiterten Aushandlung. Wer den Aufruf
überlebt, hat eine Verbindung.

**Eine Ausnahme davon ist der Transport selbst.** Kommt die Verbindung gar nicht
erst zustande, lautet der Fehler von dort „Unable to connect to the remote
server" und nennt die Adresse nicht — die seit XEP-0156 auch aus dem `host-meta`
einer fremden Domain stammen kann und dann in keinem Quelltext steht. Dieser
eine Fall wird deshalb in eine `XMPPProtocolException` gefasst, die den Endpunkt
nennt; der ursprüngliche Fehler bleibt als `InnerException` erhalten. Ein
abgebrochener Aufbau bleibt eine `OperationCanceledException`.

Nur der ausdrückliche Aufruf wirft. Der Wiederverbindungsversuch im Hintergrund
hat keinen Aufrufer und meldet weiterhin über `OnError` und `OnStateChanged`.

## Fristen beim Verbindungsaufbau

Jeder Lese-Schritt der Aushandlung — Stream-Kopf, Features, jede SASL-Runde —
hat **10 Sekunden**, ebenso das Resource Binding. Läuft eine Frist ab, scheitert
der Aufbau mit einer Meldung, die den Schritt nennt („Auf den Stream-Kopf kam
innerhalb von 10 Sekunden keine Antwort").

Der Grund ist der eine Fall, den ein Fehler nicht abdeckt: Eine Gegenstelle, die
die Verbindung annimmt und dann **schweigt**. Ein Fehler kommt an, ein
geschlossener Socket kommt an — Schweigen kommt nicht an, und ohne Frist kehrte
`ConnectAsync` nie zurück.

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

5. **Caps-Antworten (XEP-0115 §5.4)** — eine disco#info-Antwort kommt nur dann
   unter `node#ver` in den Cache, wenn ihr SHA-1-Hash genau diesen `ver`-Wert
   ergibt. Sonst könnte jeder, dessen Presence hier ankommt, das `node#ver`-Paar
   eines verbreiteten Clients ankündigen, eine Liste seiner Wahl antworten und
   sie damit jedem weiteren Kontakt unterschieben, der dasselbe Paar ankündigt.

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

Die Fixtures sind nach Themen gegliedert; der Namespace bleibt dabei flach
`org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP`, die Ordner gliedern nur:

```
Jabber.Tests/XMPP/
├── Infrastructure/     Basisklasse aller Fixtures, Wache gegen interne Fehler
├── Common/             JIDs, Stanza-Namen, Namensräume, IQ-Typen, XML-Splitter
├── Auth/               SASL/SCRAM, Mechanismus-Politik, Konten und Zertifikate
├── Streams/            Aushandlung, Binding, TLS, Fristen, Wiederverbindung
├── StreamManagement/   XEP-0198: Zählen, Bestätigen, Wiederaufnehmen
├── Federation/         S2S: Dialback, SRV, TCP/WebSocket, fremde Server
├── Routing/            Zustellregeln, mehrere Resourcen, Offline-Ablage
├── Rosters/            Roster, Subscriptions, Versionierung, Push-Sicherheit
├── Stanzas/            Aufbau, Parsen und Fehler einzelner Stanzas
└── XEPs/               XEP-0115 Caps und die Nutzlasten der übrigen XEPs
```

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
- Ein **unbekannter Benutzername** bekommt denselben Austausch wie ein
  bekannter: erfundene Zugangsdaten aus dem Namen und einem Serverschlüssel —
  je Name andere, für denselben Namen immer dieselben —, und die Abweisung
  kommt erst am Beweis. Sonst stünde die Antwort auf „gibt es dieses Konto?"
  im Ablauf, ganz gleich welches Fehlerwort dabei steht (RFC 6120 §13.11,
  „Directory Harvesting")
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
- XEP-0288 Bidirectional Server-to-Server Streams: beide Richtungen über eine
  Verbindung. Ohne die Erweiterung antwortet jede Seite über eine *eigene*
  ausgehende Verbindung (RFC 6120 §4.1) — hinter NAT, hinter einer Firewall
  oder ohne DNS-Eintrag geht die Antwort dann verloren, und zwar
  stillschweigend. Zwei Schalter, weil es zwei Dinge sind:
  `OfferBidirectionalStreams` kündigt sie auf eingehenden Verbindungen an,
  `RequestBidirectionalStreams` erbittet sie auf ausgehenden. Über die
  Rückrichtung geht nichts vor dem Ausweis der Gegenstelle und nichts für eine
  fremde Domain. Auf beiden S2S-Transporten, gegen Prosody 13 und ejabberd
  24.12 in beiden Richtungen geprüft.

  Angekündigt werden **beide** Namensräume (`urn:xmpp:features:bidi` und
  `urn:xmpp:bidi`), gelesen ebenfalls beide. Die XEP kennt für die Ankündigung
  nur den ersten; ejabberd 24.12 legt in die Features das Freischalt-Element
  und greift nur den zweiten auf. Beobachtet, nicht vermutet — mit nur der
  XEP-Form nimmt es unsere Rückrichtung nicht. Eindeutig bleibt es trotzdem:
  das Freischalt-Element heisst in beiden Lesarten `urn:xmpp:bidi`
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
- XEP-0352 Client State Indication: Erklärt sich ein Client für inaktiv, hält
  der Server zurück, was warten kann — Presence (nur die letzte je Full-JID),
  Empfangsbestätigungen, Marker. Ein Chat State wird fallengelassen statt
  aufgehoben, denn ein „schreibt gerade" von vorhin ist beim Nachliefern keine
  verspätete Auskunft mehr, sondern eine falsche. Nachrichten mit Text, `iq`,
  Fehler und Nonzas gehen unverändert sofort hinaus
- XEP-0198 Stream Management mit **eigener, unabhängig implementierter**
  Zählung — der Server benutzt bewusst nicht dieselbe Hilfsfunktion wie der
  Client, sonst prüften die Tests beide Seiten mit derselben Logik
- Stanza- und Stream-Fehler auf Zuruf: `StanzaErrorIq(…)` und
  `session.SendStreamErrorAsync(condition)` — das Letztere beendet den Stream
  auch, wie RFC 6120 §4.9.1.1 es verlangt: Fehler schicken, `<close/>` nach
  RFC 7395 §3.6, Verbindung niederlegen
- Offline-Ablage nach RFC 6121 §8.5.2.2.1 und XEP-0160, mit XEP-0203-Stempel;
  `StoreOfflineMessages` schaltet auf den gleichrangig erlaubten Gegenweg um
  (`<service-unavailable/>` an den Absender). Ein `chat` mit ausschliesslich
  Tippstatus-Inhalt wird verworfen — die einzige Nachricht, die dieser Server
  stillschweigend fallen lässt, und zwar weil ein Tippstatus nichts verspricht
- `OnInternalError` meldet, wenn das Verarbeiten eines Frames mit einer Ausnahme
  endet — samt Frame. Danach endet der Stream mit `<internal-server-error/>`
  (RFC 6120 §4.9.3.8 und §4.9.1.1), gefolgt von `<close/>` nach RFC 7395 §3.6:
  Was der Frame ändern sollte, ist halb geändert, und ein Stream, über dessen
  Zustand die beiden Seiten verschiedene Vorstellungen haben, ist keiner mehr.
  Die Testsammlung hängt an das Ereignis eine Wache, die jede Meldung als
  Programmierfehler behandelt; `FailFrameHandling` erreicht den Weg absichtlich.
  Sie hängt **nicht** mehr daran, dass ein Fixture sie anmeldet: Jeder Server
  meldet seine Entstehung über `OnInstanceCreated` (internal), und die Wache
  findet ihn von dort aus — auch in einem Fixture, das es morgen gibt (D54)
- Schalter für Fehlerfälle: `CompleteCloseHandshake`, `RouteStanzas`,
  `BroadcastPresence`, `DeliverCarbons`, `AnswerPings`,
  `OfferStreamManagement`, `AnswerAckRequests`, `SwallowClientStanzas`
  (verwirft eingehende Stanzas, bevor sie gezählt werden — der einzige Weg zu
  einer Stanza, die die Leitung verlässt und trotzdem nicht ankommt),
  `SweepResumableStreams` (hält den Abräumer an — der einzige Weg zu einem
  Stream, dessen Frist abgelaufen ist, während er noch dasteht),
  `FailPings`, `FailDiscoInfo`,
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
  und PLAIN; die `-PLUS`-Varianten fehlen. ~~Ein unbekanntes Konto wird
  abgelehnt, bevor der Austausch beginnt.~~ Behoben: Der Austausch läuft auch
  für einen unbekannten Namen zu Ende und scheitert am Beweis (RFC 6120 §13.11,
  siehe D50). Der Serverschlüssel, aus dem die erfundenen Salts entstehen, lebt
  aber im Prozess — über einen Neustart hinweg wechseln sie, echte nicht. Bei
  **PLAIN** ist der Ablauf ohnehin gleich; dort unterscheidet sich nur die
  Laufzeit, weil ein vorhandenes Konto PBKDF2 rechnet und ein unbekanntes nicht.
- **Der Downgrade-Schutz ist ein Trust-On-First-Use.** `PinnedSaslMechanism`
  deckt jede Verbindung ab der zweiten; die allererste deckt nur, wer
  `MinimumSaslMechanism` selbst setzt. Und die Anheftung lebt im Objekt: Ein
  neuer Prozess fängt wieder ohne sie an.
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
- **Die Offline-Ablage liegt im Kontenspeicher und unverschlüsselt.**
  `FileAccountStore` schreibt die vollständigen Stanzas in dieselbe JSON-Datei
  wie die Zugangsdaten — Nachrichtentexte im Klartext, ohne gesetzte
  Zugriffsrechte. Ein echter Server trennt die beiden und hätte für die Ablage
  ausserdem eine Verfallszeit; hier bleibt eine Nachricht liegen, bis jemand
  sie abholt. Was ebenfalls fehlt: die Ablage einsehen und einzeln abholen,
  statt sie beim Anmelden über sich hereinbrechen zu lassen — XEP-0013 könnte
  das und ist bewusst nicht umgesetzt (siehe oben).
- **Eine Probe an ein unbekanntes Konto bleibt unbeantwortet.** RFC 6121 §8.5.1
  stellt `<unsubscribed/>` und Schweigen frei; dieser Server schweigt, damit ein
  unbekanntes Konto genauso aussieht wie ein vorhandenes ohne Berechtigung.
- **Eine Gegenstelle erreicht nur die Auskunft über den Server, nicht den
  Zustand einer Sitzung.** Ping und disco#info an die Serveradresse werden auch
  über die Servergrenze beantwortet (seit D36); Binding, Legacy Session,
  Carbons und der Roster gehören dagegen einer Sitzung oder einem Konto und
  bleiben für S2S unerreichbar — ein fremder Server, der danach fragt, bekommt
  `<service-unavailable/>`.
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
  Aufgehoben wird unabhängig von der Presence: Die Zusage gehört dem Stream,
  ein unsichtbarer Client behält sie also.
  **Die Abweisung nennt einen Stand nur, wo es einen zu nennen gibt:** `h`
  steht im `<failed/>` genau dann, wenn der abgelaufene Stream noch daliegt und
  dem anfragenden Konto gehört. Eine unbekannte Kennung bekommt kein `h` —
  geraten wird nicht —, und eine fremde erst recht nicht: Die Zahl verriete,
  dass es diesen Stream gibt und wie viel über ihn gelaufen ist (siehe D49).
- ~~**Fehlerbehandlung nur auf Zuruf.** Ausser den Schaltern oben erzeugt der
  Server keine Stanza-Fehler.~~ Überholt: Er erzeugt sie von sich aus, wo die
  RFCs es verlangen — `<bad-request/>` für einen unbekannten IQ-Typ,
  `<service-unavailable/>` für einen unzustellbaren Empfänger und für ein
  `groupchat` an ein Konto, `<remote-server-not-found/>` für eine unerreichbare
  Domain, `<item-not-found/>` für einen unbekannten disco-Knoten und
  `<jid-malformed/>` für ein `to`, das kein JID ist (D51). Die Schalter sind
  dafür da, die *übrigen* Fehlerwege zu erreichen. Unbekannte IQs bekommen
  weiterhin pauschal `<service-unavailable/>` statt einer Unterscheidung nach
  Ursache.

### Kryptografische Testvektoren

Die Implementierungen werden gegen die veröffentlichten Vektoren gerechnet,
nicht gegen sich selbst:

| Quelle | Was geprüft wird | Ergebnis |
|--------|------------------|----------|
| RFC 5802 §5 | SCRAM-SHA-1: client-first, ClientProof, ServerSignature | ✅ exakt reproduziert |
| RFC 7677 §3 | SCRAM-SHA-256: client-first, ClientProof, ServerSignature | ✅ exakt reproduziert |
| XEP-0115 §5.2 | Verification String `QgayPKawpkPSDYmwT/WM94uAlu0=` | ✅ exakt reproduziert |
| XEP-0115 §5.3 | Verification String `q07IKJEyjvHSyhy//CH0CxmKi8w=` (zwei Sprachen, ein Datenformular) | ✅ exakt reproduziert |
| RFC 4013 §3 | SASLprep-Beispieltabelle, alle sieben Zeilen | ✅ exakt reproduziert |
| RFC 7622 §3.5 | JID-Beispieltabellen: 15 gültige, 8 ungültige Adressen | ✅ reproduziert (Ausnahme: Beispiel 18, siehe oben) |
| RFC 3492 §7.1 | Punycode: elf Beispiele in acht Schriften | ✅ exakt reproduziert, in beide Richtungen |
| RFC 3454 Anhang A–D | Die StringPrep-Tabellen selbst | ✅ von `tools/stringprep/generate.py` aus dem RFC erzeugt, nicht abgeschrieben |
| Unicode `DerivedBidiClass.txt` | Die Bidi-Klassen für RFC 5893 | ✅ von `tools/unicode/generate-bidiclass.py` aus der Unicode-Datei erzeugt (15.1.0, 764 Bereiche) |
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
- **Eigene erweiterte Angaben sind abschaltbar und standardmäßig aus.**
  `DiscoManager.LocalForms` fängt leer an. Was dort steht, erfährt jeder
  Kontakt ungefragt — Software, Version und Betriebssystem sind genau die
  Angaben, aus denen sich ein Gerät wiedererkennen lässt. Wer sie
  veröffentlichen will:

  ```csharp
  client.Connection.Disco!.LocalForms.Add(
      DiscoForm.SoftwareInfo(Software: "Jabber", SoftwareVersion: "0.1"));
  ```

  Der Inhalt geht in den angekündigten `ver`-Wert ein. Er lässt sich deshalb
  nur zusammen mit einer neuen Presence ändern — in der Zeit dazwischen kündigt
  der Client einen Hash an, den seine Antwort nicht mehr ergibt, und eine
  Gegenstelle, die nach XEP-0115 §5.4 nachrechnet, verwirft die Auskunft (zu
  Recht) als nicht belegt.
- ~~**Log-Ausgabe und Konsolen-UI überlagern sich.**~~ Behoben: Alles, was auf
  die Konsole geht, läuft über `ConsoleUI/ConsoleOutput` — Ereignisse,
  Systemmeldungen und das Protokoll über `ConsoleOutputLoggerProvider`. Die
  angefangene Eingabezeile weicht, die Ausgabe erscheint, die
  Eingabeaufforderung steht wieder da, und **eine Sperre** hält zwei
  gleichzeitige Ausgaben auseinander (D58).
- **XEP-0198 ist per Default an, samt Wiederaufnahme.** Die Zählung ist gegen
  Prosody 13 geprüft: nach einem vollständigen Sitzungsaufbau melden beide
  Seiten denselben Stand, und zwar auf den Zähler genau — nicht nur „die
  Warteschlange lief leer", was auch ein zu grosses `h` bewirkte. Nach einem
  Abriss knüpft der Client vor dem Resource Binding an den alten Stream an: die
  Full-JID bleibt, was während der Störung ankam, wird nachgeliefert, und die
  Kontakte sehen kein Verschwinden. Gelingt es nicht — Frist abgelaufen,
  Kennung unbekannt —, bindet er neu; nennt die Abweisung dabei einen Stand
  (`<failed h='…'/>`), gilt bis dorthin dasselbe wie bei einem `<a h='…'/>`:
  verarbeitet ist verarbeitet, und verloren ist nur, was darüber hinaus offen
  war. Geprüft gegen Prosody 13
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
- ~~Keine Client State Indication (XEP-0352)~~ Umgesetzt in D61, auf beiden
  Seiten — siehe die Tabelle oben
- Kein Flexible Offline Message Retrieval (XEP-0013) — die Ablage kommt beim
  Anmelden vollständig heraus und lässt sich nicht einsehen oder einzeln
  abholen. Bewusst so: Die XSF führt XEP-0013 als *Deprecated* (siehe D37)
- ~~Der Client liest den XEP-0203-Stempel nicht; eine nachgereichte Nachricht
  erscheint mit ihrer Empfangszeit, obwohl der Server den Verzug mitteilt~~
  Behoben in D59: Sie erscheint mit Datum und dem Vermerk „(nachgereicht)"
- **Kein TCP-Transport** — der Client spricht ausschliesslich XMPP über
  WebSocket (RFC 7395). Die Fabrikmethode `CreateTcp`, die eine `tcp://`-URI
  erzeugte und dabei funktionslos war, ist entfernt: Eine öffentliche Methode,
  die nicht funktionieren kann, ist schlechter als keine. Ein echter
  TCP-Transport steht unter „Optional" (siehe [WORKPLAN.md](../WORKPLAN.md),
  D48): Prosody, ejabberd und der Testserver bieten WebSocket an, also fehlt er
  niemandem — die Bausteine (`XmlStreamSplitter`, STARTTLS) gibt es auf der
  S2S-Seite bereits.

### Ungenutzte API-Fläche

**Derzeit keine.** Die Liste stand hier, seit es sie gab, und ist in D57
abgearbeitet — jeder Eintrag entweder benutzt oder gestrichen:

| Member | Entscheidung |
|--------|--------------|
| `RosterStanzaBuilder.GetRoster` | **benutzt** — `XMPPConnection` setzte dieselbe Anfrage daneben von Hand zusammen |
| `RosterStanzaBuilder.Unsubscribe` | **benutzt** — über das neue `CancelSubscriptionAsync`, den vierten Übergang aus RFC 6121 §3 |
| `DiscoInfo.HasFeature` | **benutzt** — von einem Test, der die Frage vorher an der Merkmalsliste vorbei stellte |
| `MessageReceipt` | gestrichen — der Typ dokumentierte selbst, dass er nirgends erzeugt wird |
| `ReceiptTracker.GetTimedOutMessages` | gestrichen — es gibt keine Frist, die ablaufen könnte |
| `PubSubManager.OnSubscriptionResult` | gestrichen — nie ausgelöst, und die einzige Warnung des Baus |
| `PubSubBuilder.Retract` / `DiscoverNodes` | gestrichen — zwei Bausteine ohne Aufrufer, wiederherstellbar an einem Nachmittag |
| `DiscoInfo.Supports*` (fünf Stück) | gestrichen — Abkürzungen für `HasFeature` mit eingebautem Namensraum |
| `CarbonManager.DisableIq` | gestrichen — der Client schaltet Carbons im Aufbau ein und bietet keinen Schalter |
| `StreamManagementManager.ResumeAsync`, `GetUnackedStanzas`, `OnStanzasLost` | **war veraltet** — alle drei werden längst benutzt |

Die letzte Zeile ist der Grund, warum eine solche Liste keine Dauereinrichtung
sein sollte: **Sie veraltet in die falsche Richtung** und behauptet ungeprüft,
was inzwischen geprüft ist. Dasselbe galt schon für
`EntityCapsManager.GetCachedInfo`, das hier stand, während
`CapsExchangeTests` längst darüber prüfte.

## Lizenz

Apache License, Version 2.0 — siehe [LICENSE](../LICENSE).

Copyright (c) 2010-2026 GraphDefined GmbH &lt;achim.friedland@graphdefined.com&gt;
