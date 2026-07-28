#!/usr/bin/env bash
#
# Baut eine ejabberd-Gegenstelle fuer den Foederationslauf auf - ohne root.
#
# Warum ueberhaupt eine zweite: Prosody allein belegt, dass wir mit Prosody
# koennen. Erst eine zweite, unabhaengig entstandene Implementierung trennt
# "richtig" von "zufaellig deckungsgleich mit einer Auffassung". ejabberd ist
# in Erlang geschrieben, hat einen anderen Werdegang und andere Vorlieben im
# Handshake - genau darum ist es der interessante zweite Gegner.
#
# Aufruf aus WSL (Debian) heraus:
#     bash tools/ejabberd/setup.sh
#
# Danach bedient ejabberd die Domain ejabberd.test auf 127.0.0.1:25269 mit
# einem Zertifikat aus einer eigenen Test-CA. Die Testsammlung findet sie
# ueber JABBER_EJABBERD_CERTS.

set -euo pipefail

PREFIX="${EJABBERD_TEST_PREFIX:-$HOME/ejabberd-test}"
ROOT="$PREFIX/root"
ARCH_DIR="x86_64-linux-gnu"

PEER_DOMAIN="ejabberd.test"

# Dieselbe Zweiteilung wie beim Prosody-Aufbau: jabber.test fuer den
# ausgehenden Lauf (die Adresse steht bei uns von Hand, es braucht kein DNS),
# localhost fuer den eingehenden (damit *ejabberd* uns aufloesen kann, ohne
# dass ein /etc/hosts-Eintrag und damit root noetig waere).
LOCAL_DOMAIN="jabber.test"
INBOUND_DOMAIN="localhost"

# Beide Ports liegen neben denen des Prosody-Aufbaus, damit die zwei
# Gegenstellen nebeneinander laufen koennen und kein Lauf versehentlich am
# falschen Server haengt:
#
#   25269 - hier horcht ejabberd.        (Prosody: 15269)
#    5270 - hierhin waehlt ejabberd uns  (Prosody faellt auf 5269 zurueck)
#
# Der zweite Wert ist nur deshalb frei waehlbar, weil ejabberd einen
# ausdruecklichen Schalter dafuer hat: ohne SRV-Eintrag nimmt es
# outgoing_s2s_port. Prosody kennt keinen und bleibt bei 5269.
PEER_S2S_PORT=25269
INBOUND_PORT=5270

# Der WebSocket-Endpunkt fuer den Client-Lauf (XEP-0198). 5443 ist ejabberds
# Vorgabe; Prosody liegt auf 5281, beide koennen also nebeneinander stehen.
WSS_PORT=5443

# Zwei Konten: eines fuer den Client selbst, eines als Absender. Ohne den
# zweiten laesst sich nicht pruefen, ob eine waehrend der Stoerung zugestellte
# Nachricht nach der Wiederaufnahme nachkommt.
TEST_USER="alice"
TEST_USER2="bob"
TEST_PASSWORD="geheim"

mkdir -p "$PREFIX"/{debs,etc,logs,spool,certs} "$ROOT"

# ---------------------------------------------------------------- Pakete ----

echo "== Pakete holen und auspacken"
cd "$PREFIX/debs"

# ejabberd zieht die halbe Erlang-Laufzeit nach - einundvierzig Pakete. Statt
# sie aufzuzaehlen und bei jedem Debian-Wechsel nachzupflegen, laesst sich der
# Satz von apt selbst ausrechnen: --print-uris loest auf, ohne zu installieren.
apt-get install --print-uris -y --no-install-recommends ejabberd 2>/dev/null \
    | grep "^'http" | cut -d"'" -f2 > uris.txt

echo "   $(wc -l < uris.txt) Pakete"
wget -q -N -i uris.txt

for f in ./*.deb; do dpkg-deb -x "$f" "$ROOT"; done

# Debian verdrahtet ROOTDIR in den Erlang-Startskripten fest - und zwar in
# allen drei Zweigen der Fallunterscheidung, auch in dem, der laut Quelltext
# ERL_ROOTDIR beachten soll. Die Variable zu setzen sieht also aus, als
# muesste es reichen, und tut nichts.
ERTS_DIR="$(basename "$(ls -d "$ROOT"/usr/lib/erlang/erts-* | head -1)")"

for f in erl erlc escript dialyzer typer start_erl; do
    [ -f "$ROOT/usr/lib/erlang/bin/$f" ] || continue
    sed -i "s|ROOTDIR=/usr/lib/erlang|ROOTDIR=$ROOT/usr/lib/erlang|g" \
        "$ROOT/usr/lib/erlang/bin/$f"
done

# ejabberdctl ist der Debian-Launcher und traegt drei Pfade und einen
# Benutzernamen fest eingebaut. Der Benutzername ist der wichtigste Eingriff:
# das Skript bricht mit "can only be run by root or the user ejabberd" ab,
# bevor es irgendetwas tut.
sed -i \
    -e "s|^ERL=\"/usr/bin/erl\"|ERL=\"$ROOT/usr/lib/erlang/bin/erl\"|" \
    -e "s|^EPMD=\"/usr/bin/epmd\"|EPMD=\"$ROOT/usr/lib/erlang/$ERTS_DIR/bin/epmd\"|" \
    -e "s|^INSTALLUSER=ejabberd|INSTALLUSER=|" \
    -e "s|^ERL_LIBS='/usr/lib/$ARCH_DIR'|ERL_LIBS='$ROOT/usr/lib/$ARCH_DIR'|" \
    "$ROOT/usr/sbin/ejabberdctl"
chmod +x "$ROOT/usr/sbin/ejabberdctl"

# Die uebrigen Pfade nimmt ejabberdctl aus der Umgebung: es setzt seine
# Vorgaben mit ": ${VAR:=...}", was gesetzte Werte stehen laesst.
cat > "$PREFIX/env.sh" <<ENV
export LD_LIBRARY_PATH="$ROOT/usr/lib/$ARCH_DIR:\${LD_LIBRARY_PATH:-}"
export PATH="$ROOT/usr/sbin:$ROOT/usr/lib/erlang/bin:\${PATH:-}"
export CONFIG_DIR="$PREFIX/etc"
export LOGS_DIR="$PREFIX/logs"
export SPOOL_DIR="$PREFIX/spool"
export ERL_EPMD_ADDRESS="127.0.0.1"
ENV

cp "$ROOT/etc/ejabberd/inetrc" "$PREFIX/etc/inetrc"

# ------------------------------------------------------------ Zertifikate ---

echo "== Test-CA und Zertifikate"
cd "$PREFIX/certs"

# Eine eigene CA, nicht die des Prosody-Aufbaus: so bleibt jede Gegenstelle
# fuer sich aufbaubar, und ein Lauf gegen die eine kann nicht stillschweigend
# von einem Zertifikat der anderen leben.
if [ ! -f ca.crt ]; then

    openssl req -x509 -newkey rsa:2048 -keyout ca.key -out ca.crt -days 30 -nodes \
        -subj "/CN=Jabber ejabberd Test CA" \
        -addext "basicConstraints=critical,CA:TRUE" \
        -addext "keyUsage=critical,keyCertSign,cRLSign" 2>/dev/null

    for d in "$PEER_DOMAIN" "$LOCAL_DOMAIN" "$INBOUND_DOMAIN"; do

        openssl req -newkey rsa:2048 -keyout "$d.key" -out "$d.csr" -nodes \
                -subj "/CN=$d" 2>/dev/null

        # clientAuth muss mit hinein: bei SASL-EXTERNAL legt der aufbauende
        # Server sein Zertifikat als Klientzertifikat vor.
        cat > "$d.ext" <<EXT
subjectAltName=DNS:$d
extendedKeyUsage=serverAuth,clientAuth
keyUsage=critical,digitalSignature,keyEncipherment
basicConstraints=CA:FALSE
EXT
        openssl x509 -req -in "$d.csr" -CA ca.crt -CAkey ca.key -CAcreateserial \
                -out "$d.crt" -days 30 -sha256 -extfile "$d.ext" 2>/dev/null

        openssl verify -CAfile ca.crt "$d.crt"

    done

    # Unsere Seite laedt PKCS#12.
    for d in "$LOCAL_DOMAIN" "$INBOUND_DOMAIN"; do
        openssl pkcs12 -export -out "$d.pfx" -inkey "$d.key" -in "$d.crt" -passout pass:
    done

fi

# ejabberd erwartet Schluessel und Zertifikat in einer Datei.
for f in *.crt *.key; do sed -i 's/\r$//' "$f"; done
cat "$PEER_DOMAIN.key" "$PEER_DOMAIN.crt" > "$PEER_DOMAIN.pem"
chmod 600 ./*.key "$PEER_DOMAIN.pem"

# ---------------------------------------------------------- Konfiguration ---

echo "== Konfiguration"
cat > "$PREFIX/etc/ejabberd.yml" <<CFG
### ejabberd als Gegenstelle fuer den Foederationslauf. Erzeugt von
### tools/ejabberd/setup.sh - Aenderungen hier gehen beim naechsten Lauf verloren.

hosts:
  - "$PEER_DOMAIN"

loglevel: debug
log_rotate_size: infinity

certfiles:
  - "$PREFIX/certs/$PEER_DOMAIN.pem"

ca_file: "$PREFIX/certs/ca.crt"

listen:
  -
    port: $PEER_S2S_PORT
    ip: "127.0.0.1"
    module: ejabberd_s2s_in

  ## Der Weg fuer unseren Client: er spricht XMPP ueber WebSocket (RFC 7395),
  ## nicht ueber den rohen 5222er-Strom. Ohne diesen Handler gaebe es fuer ihn
  ## keinen Weg herein.
  -
    port: $WSS_PORT
    ip: "127.0.0.1"
    module: ejabberd_http
    tls: true
    request_handlers:
      /websocket: ejabberd_http_ws

## "required", nicht "required_trusted": beides verlangt STARTTLS, aber nur
## das zweite verlangt zusaetzlich eine gueltige Kette und wuerde damit
## Dialback ausschliessen. So entscheidet unsere Seite, welches Verfahren zum
## Zug kommt - legen wir ein Klientzertifikat vor, bietet ejabberd EXTERNAL an;
## legen wir keines vor, bleibt Dialback.
s2s_use_starttls: required
s2s_access: all
s2s_dns_timeout: 5

## Ohne SRV-Eintrag waehlt ejabberd diesen Port an. Prosody kennt keinen
## solchen Schalter und faellt fest auf 5269 zurueck - deshalb kann unser
## eingehender Listener hier auf einem eigenen Port stehen, und beide
## Gegenstellen koennen nebeneinander laufen.
outgoing_s2s_port: $INBOUND_PORT

acl:
  local:
    user_regexp: ""

access_rules:
  local:
    allow: local

modules:
  mod_disco: {}
  mod_ping: {}
  mod_roster: {}
  mod_version: {}

  ## XEP-0220: ohne dieses Modul weist ejabberd jede Verbindung ab, die sich
  ## nicht ueber ein Zertifikat ausweisen kann.
  mod_s2s_dialback: {}

  ## XEP-0288: erlaubt beide Richtungen ueber eine Verbindung. Ohne das Modul
  ## beantwortet ejabberd eine eingehende Stanza ausschliesslich ueber eine
  ## *eigene* ausgehende Verbindung - so sieht RFC 6120 Abschnitt 4.1 den
  ## Stream, und so verhaelt sich jeder ausgewachsene Server.
  mod_s2s_bidi: {}

  ## XEP-0198 samt Wiederaufnahme. resume_timeout kurz genug, dass ein Test
  ## den Verfall abwarten kann, lang genug fuer einen Reconnect.
  mod_stream_mgmt:
    resume_timeout: 60
    max_resume_timeout: 300
CFG

# ------------------------------------------------------------------ Start ---

echo "== Start"
# shellcheck disable=SC1091
. "$PREFIX/env.sh"

"$ROOT/usr/sbin/ejabberdctl" stop >/dev/null 2>&1 || true

# Abwarten, bis der Knoten wirklich weg ist: "stop" kehrt sofort zurueck, das
# Herunterfahren laeuft noch. Ein zu frueher "start" scheitert dann mit
# "node is already running" - und mit "set -e" endet damit dieses Skript.
for _ in $(seq 20); do
    "$ROOT/usr/sbin/ejabberdctl" status >/dev/null 2>&1 || break
    sleep 1
done

rm -f "$PREFIX/logs/ejabberd.log"

"$ROOT/usr/sbin/ejabberdctl" start

# Nicht "ejabberdctl started": das Warten laeuft ueber einen zweiten
# Erlang-Knoten, der beim ersten Versuch noch keinen Namen im epmd findet und
# dann nicht wartet, sondern hart abbricht - "Runtime terminating during boot".
# Ein Warten von aussen kommt ohne diesen zweiten Knoten aus.
gestartet=0
for _ in $(seq 30); do
    if "$ROOT/usr/sbin/ejabberdctl" status 2>/dev/null | grep -q "is running"; then
        gestartet=1
        break
    fi
    sleep 1
done

if [ "$gestartet" = 1 ]; then

    echo "   ejabberd laeuft."

    # Die Konten erst jetzt: ejabberdctl register geht ueber einen RPC-Aufruf
    # in den laufenden Knoten, anders als Prosodys prosodyctl, das die Dateien
    # direkt anfasst. Bei angehaltenem Server gaebe es hier nur ein "nodedown".
    for u in "$TEST_USER" "$TEST_USER2"; do
        "$ROOT/usr/sbin/ejabberdctl" register "$u" "$PEER_DOMAIN" "$TEST_PASSWORD" 2>&1 \
            | grep -iv "^$" | head -1 || true
    done

    grep -q "Start accepting TLS connections at 127.0.0.1:$WSS_PORT" "$PREFIX/logs/ejabberd.log" \
        && echo "   WebSocket-Endpunkt auf $WSS_PORT." \
        || echo "   WARNUNG - kein WebSocket-Endpunkt auf $WSS_PORT; der XEP-0198-Lauf faellt aus."

else
    echo "   FEHLER - ejabberd ist nicht hochgekommen:"
    tail -30 "$PREFIX/logs/ejabberd.log" 2>/dev/null
    exit 1
fi

cat <<DONE

Fertig. ejabberd bedient $PEER_DOMAIN auf 127.0.0.1:$PEER_S2S_PORT (S2S) und
wss://127.0.0.1:$WSS_PORT/websocket (Client), Konten
$TEST_USER@$PEER_DOMAIN und $TEST_USER2@$PEER_DOMAIN, Passwort $TEST_PASSWORD.

Ausgehender Lauf, von Windows aus:

    \$env:JABBER_EJABBERD_CERTS = '\\\\wsl.localhost\\Debian$PREFIX/certs'
    dotnet test Jabber.Tests\\Jabber.Tests.csproj --filter FullyQualifiedName~EjabberdFederationTests

Eingehender Lauf - der muss *in* WSL laufen, weil ejabberd uns sonst nicht
erreicht: die Hyper-V-Firewall verwirft jede Verbindung von WSL zum
Windows-Host. Innerhalb von WSL ist alles Rueckschleife:

    JABBER_EJABBERD_CERTS=$PREFIX/certs \\
    dotnet test /mnt/c/.../Jabber.Tests/Jabber.Tests.csproj \\
        --artifacts-path /tmp/jabber-artifacts \\
        --filter FullyQualifiedName~EjabberdFederationTests

Log:      $PREFIX/logs/ejabberd.log
Beenden:  CONFIG_DIR=$PREFIX/etc LOGS_DIR=$PREFIX/logs SPOOL_DIR=$PREFIX/spool \\
          $ROOT/usr/sbin/ejabberdctl stop
DONE
