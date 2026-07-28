#!/usr/bin/env bash
#
# Baut eine Prosody-Gegenstelle fuer den Foederationslauf auf - ohne root.
#
# Warum ohne root: das Paket laesst sich mit "apt-get download" holen und mit
# "dpkg-deb -x" in ein Praefix auspacken; Prosody bringt fertige Binaermodule
# mit, es wird also nichts uebersetzt. Damit braucht dieser Aufbau kein
# sudo-Passwort und hinterlaesst nichts ausserhalb von $PREFIX.
#
# Aufruf aus WSL (Debian) heraus:
#     bash tools/prosody/setup.sh
#
# Danach laeuft Prosody auf 127.0.0.1:5269 mit der Domain prosody.test und
# einem Zertifikat aus derselben Test-CA, die auch unser jabber.test-Zertifikat
# signiert. Die Testsammlung findet die CA ueber JABBER_PROSODY_CERTS.

set -euo pipefail

PREFIX="${PROSODY_TEST_PREFIX:-$HOME/prosody-test}"
ROOT="$PREFIX/root"
ARCH_DIR="x86_64-linux-gnu"

PEER_DOMAIN="prosody.test"

# Zwei Namen fuer unsere Seite, und der Unterschied ist der Kern von P4:
#
#   jabber.test  - fuer den ausgehenden Lauf. Wir waehlen Prosody an, die
#                  Adresse steht bei uns von Hand, es braucht kein DNS.
#
#   localhost    - fuer den eingehenden Lauf. Damit *Prosody* uns anwaehlen
#                  kann, muss es unsere Domain aufloesen koennen. Ein Eintrag
#                  in /etc/hosts braeuchte root; "localhost" steht dort
#                  ohnehin und zeigt auf 127.0.0.1. Der Testserver bedient
#                  dann diese Domain und horcht auf dem Standardport 5269,
#                  auf den Prosody ohne SRV-Eintrag zurueckfaellt.
LOCAL_DOMAIN="jabber.test"
INBOUND_DOMAIN="localhost"

# Deshalb weicht Prosody auf einen anderen S2S-Port aus: 5269 gehoert im
# eingehenden Fall uns.
PEER_S2S_PORT=15269

mkdir -p "$PREFIX"/{debs,etc,var/lib,certs,run} "$ROOT"

# ---------------------------------------------------------------- Pakete ----

echo "== Pakete holen und auspacken"
cd "$PREFIX/debs"

# libicu76 steht nicht in den Abhaengigkeiten von prosody, wird aber von
# util.encodings.so gebraucht - ohne sie bricht der Start beim ersten require.
apt-get download \
    prosody lua5.4 lua-bitop lua-expat lua-filesystem lua-sec lua-socket \
    ssl-cert libicu76 >/dev/null

for f in *.deb; do dpkg-deb -x "$f" "$ROOT"; done

# Der Debian-Launcher traegt seine Pfade fest eingebaut und kennt keine
# Umgebungsvariablen dafuer. Vier Zeilen umbiegen, dazu die Shebang auf das
# ausgepackte Lua.
for f in prosody prosodyctl; do
    sed -i \
        -e "s|^CFG_SOURCEDIR=.*|CFG_SOURCEDIR='$ROOT/usr/lib/prosody';|" \
        -e "s|^CFG_CONFIGDIR=.*|CFG_CONFIGDIR='$PREFIX/etc';|" \
        -e "s|^CFG_PLUGINDIR=.*|CFG_PLUGINDIR='$ROOT/usr/lib/prosody/modules/';|" \
        -e "s|^CFG_DATADIR=.*|CFG_DATADIR='$PREFIX/var/lib';|" \
        -e "1s|.*|#!$ROOT/usr/bin/lua5.4|" \
        "$ROOT/usr/bin/$f"
    chmod +x "$ROOT/usr/bin/$f"
done

# Die Zuweisungen sind gegen "set -u" abgesichert: dieses env.sh wird auch
# von diesem Skript selbst eingelesen, und LD_LIBRARY_PATH ist oft ungesetzt.
cat > "$PREFIX/env.sh" <<ENV
export LD_LIBRARY_PATH="$ROOT/usr/lib/$ARCH_DIR:\${LD_LIBRARY_PATH:-}"
export LUA_PATH="$ROOT/usr/share/lua/5.4/?.lua;$ROOT/usr/share/lua/5.4/?/init.lua;;"
export LUA_CPATH="$ROOT/usr/lib/$ARCH_DIR/lua/5.4/?.so;;"
export PATH="$ROOT/usr/bin:\${PATH:-}"
ENV

# ------------------------------------------------------------ Zertifikate ---

echo "== Test-CA und Zertifikate"
cd "$PREFIX/certs"

if [ ! -f ca.crt ]; then

    openssl req -x509 -newkey rsa:2048 -keyout ca.key -out ca.crt -days 30 -nodes \
        -subj "/CN=Jabber Federation Test CA" \
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

# Prosodys certmanager verwirft PEM mit CRLF als "non-certificate (based on
# contents)" - ohne jede Fehlermeldung, die darauf hindeutet. Kommen die
# Dateien von einem Windows-Werkzeug, ist das die Falle.
for f in *.crt *.key; do sed -i 's/\r$//' "$f"; done
chmod 600 ./*.key

# ---------------------------------------------------------- Konfiguration ---

echo "== Konfiguration"
cat > "$PREFIX/etc/prosody.cfg.lua" <<CFG
-- Prosody als Gegenstelle fuer den Foederationslauf. Erzeugt von
-- tools/prosody/setup.sh - Aenderungen hier gehen beim naechsten Lauf verloren.

daemonize          = false
pidfile            = "$PREFIX/run/prosody.pid"
allow_registration = false

modules_enabled = {
    "roster"; "saslauth"; "tls"; "dialback"; "disco";
    "posix"; "ping"; "time"; "uptime"; "version";

    -- XEP-0288: erlaubt es, beide Richtungen ueber eine Verbindung zu
    -- fuehren. Ohne dieses Modul beantwortet Prosody eine eingehende Stanza
    -- ausschliesslich ueber eine *eigene* ausgehende Verbindung zur
    -- Absenderdomain - so sieht RFC 6120 Abschnitt 4.1 den Stream, und so
    -- verhaelt sich jeder ausgewachsene Server.
    "s2s_bidi";
}

-- Beide Seiten tragen Zertifikate aus derselben Test-CA. Damit belegt
-- SASL-EXTERNAL (XEP-0178) die Domain, und Dialback wird nicht gebraucht -
-- was hier auch gut ist, weil die Dialback-Rueckfrage eine Verbindung von
-- WSL zum Windows-Host braucht, und die blockiert die Hyper-V-Firewall.
s2s_secure_auth        = true
s2s_require_encryption = true
c2s_require_encryption = true
s2s_connect_timeout    = 10

-- 5269 bleibt frei: im eingehenden Lauf horcht dort unser Testserver, und
-- Prosody faellt ohne SRV-Eintrag genau auf diesen Port zurueck.
s2s_ports  = { $PEER_S2S_PORT }
interfaces = { "127.0.0.1" }

certificates = "$PREFIX/certs"

ssl = {
    certificate = "$PREFIX/certs/$PEER_DOMAIN.crt";
    key         = "$PREFIX/certs/$PEER_DOMAIN.key";
    cafile      = "$PREFIX/certs/ca.crt";
}

log = {
    { levels = { min = "debug" }, to = "file", filename = "$PREFIX/prosody.log" };
}

data_path    = "$PREFIX/var/lib"
plugin_paths = { }

VirtualHost "$PEER_DOMAIN"
    ssl = {
        certificate = "$PREFIX/certs/$PEER_DOMAIN.crt";
        key         = "$PREFIX/certs/$PEER_DOMAIN.key";
        cafile      = "$PREFIX/certs/ca.crt";
    }
CFG

# ------------------------------------------------------------------ Start ---

echo "== Start"
# shellcheck disable=SC1091
. "$PREFIX/env.sh"

# Das Muster enthaelt den Interpreter, nicht nur den Pfad. "pkill -f" prueft
# die ganze Kommandozeile, und eine Shell, die diesen Aufruf enthaelt, traegt
# den blossen Pfad selbst darin - sie braechte sich damit selbst um.
pkill -f "lua5.4 $ROOT/usr/bin/prosody" 2>/dev/null || true
sleep 1
: > "$PREFIX/prosody.log"

cd "$PREFIX"
nohup "$ROOT/usr/bin/prosody" > "$PREFIX/prosody.out" 2>&1 &
disown
sleep 4

if grep -q "Certificates loaded" "$PREFIX/prosody.log"; then
    echo "   Prosody laeuft, Zertifikate geladen."
else
    echo "   FEHLER - Prosody hat keine Zertifikate geladen:"
    tail -20 "$PREFIX/prosody.log"
    exit 1
fi

cat <<DONE

Fertig. Prosody bedient $PEER_DOMAIN auf 127.0.0.1:$PEER_S2S_PORT.

Ausgehender Lauf, von Windows aus:

    \$env:JABBER_PROSODY_CERTS = '\\\\wsl.localhost\\Debian$PREFIX/certs'
    dotnet test Jabber.Tests\\Jabber.Tests.csproj --filter FullyQualifiedName~ProsodyFederationTests

Eingehender Lauf (P4) - der muss *in* WSL laufen, weil Prosody uns sonst
nicht erreicht: die Hyper-V-Firewall verwirft jede Verbindung von WSL zum
Windows-Host, und daran ist nichts zu deuteln, ohne eine Firewall-Regel zu
setzen. Innerhalb von WSL ist alles Loopback:

    JABBER_PROSODY_CERTS=$PREFIX/certs \\
    dotnet test /mnt/c/.../Jabber.Tests/Jabber.Tests.csproj \\
        --artifacts-path /tmp/jabber-artifacts \\
        --filter FullyQualifiedName~ProsodyFederationTests

Log:      $PREFIX/prosody.log
Beenden:  pkill -f "lua5.4 $ROOT/usr/bin/prosody"
DONE
