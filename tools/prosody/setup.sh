#!/usr/bin/env bash
#
# Sets up a Prosody far side for the federation run - without root.
#
# Why without root: the package can be fetched with "apt-get download" and
# unpacked into a prefix with "dpkg-deb -x"; Prosody ships ready-built binary
# modules, so nothing is compiled. This setup therefore needs no sudo password
# and leaves nothing behind outside $PREFIX.
#
# Called from within WSL (Debian):
#     bash tools/prosody/setup.sh
#
# Afterwards Prosody runs on 127.0.0.1:5269 with the domain prosody.test and a
# certificate from the same test CA that signs our jabber.test certificate as
# well. The test suite finds the CA over JABBER_PROSODY_CERTS.

set -euo pipefail

PREFIX="${PROSODY_TEST_PREFIX:-$HOME/prosody-test}"
ROOT="$PREFIX/root"
ARCH_DIR="x86_64-linux-gnu"

PEER_DOMAIN="prosody.test"


# Two names for our side, and the difference is the core of P4:
#
#   jabber.test  - for the outgoing run. We dial Prosody, the address stands
#                  at our end by hand, no DNS is needed.
#
#   localhost    - for the incoming run. So that *Prosody* can dial us, it has
#                  to be able to resolve our domain. An entry in /etc/hosts
#                  would need root; "localhost" stands there anyway and points
#                  at 127.0.0.1. The test server then serves this domain and
#                  listens on the standard port 5269, the one Prosody falls
#                  back to without an SRV entry.
LOCAL_DOMAIN="jabber.test"
INBOUND_DOMAIN="localhost"

# This is why Prosody moves aside to another S2S port: in the incoming case
# 5269 belongs to us.
PEER_S2S_PORT=15269

# The WebSocket endpoint for the client run (XEP-0198). 5281 is Prosody's
# default for HTTPS.
HTTPS_PORT=5281

# Two accounts on Prosody: one for the client itself, one as a sender. Without
# the second there is no checking whether a message handed in during the
# outage comes after the resumption.
TEST_USER="alice"
TEST_USER2="bob"
TEST_PASSWORD="geheim"

mkdir -p "$PREFIX"/{debs,etc,var/lib,certs,run} "$ROOT"

# --------------------------------------------------------------- packages ---

echo "== Fetching and unpacking the packages"
cd "$PREFIX/debs"

# libicu76 does not stand among the dependencies of prosody, but is needed by
# util.encodings.so - without it the start breaks at the first require.
apt-get download \
    prosody lua5.4 lua-bitop lua-expat lua-filesystem lua-sec lua-socket \
    ssl-cert libicu76 >/dev/null

for f in *.deb; do dpkg-deb -x "$f" "$ROOT"; done

# The Debian launcher carries its paths built in and knows no environment
# variables for them. Bend four lines, and the shebang onto the unpacked Lua.
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

# The assignments are guarded against "set -u": this env.sh is read by this
# script itself as well, and LD_LIBRARY_PATH is often unset.
cat > "$PREFIX/env.sh" <<ENV
export LD_LIBRARY_PATH="$ROOT/usr/lib/$ARCH_DIR:\${LD_LIBRARY_PATH:-}"
export LUA_PATH="$ROOT/usr/share/lua/5.4/?.lua;$ROOT/usr/share/lua/5.4/?/init.lua;;"
export LUA_CPATH="$ROOT/usr/lib/$ARCH_DIR/lua/5.4/?.so;;"
export PATH="$ROOT/usr/bin:\${PATH:-}"
ENV

# ----------------------------------------------------------- certificates ---

echo "== Test CA and certificates"
cd "$PREFIX/certs"

if [ ! -f ca.crt ]; then

    openssl req -x509 -newkey rsa:2048 -keyout ca.key -out ca.crt -days 30 -nodes \
        -subj "/CN=XMPPConformanceTests Federation Test CA" \
        -addext "basicConstraints=critical,CA:TRUE" \
        -addext "keyUsage=critical,keyCertSign,cRLSign" 2>/dev/null

    for d in "$PEER_DOMAIN" "$LOCAL_DOMAIN" "$INBOUND_DOMAIN"; do

        openssl req -newkey rsa:2048 -keyout "$d.key" -out "$d.csr" -nodes \
                -subj "/CN=$d" 2>/dev/null

        # clientAuth has to go in as well: with SASL EXTERNAL the connecting
        # server presents its certificate as a client certificate.
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

    # Our side loads PKCS#12.
    for d in "$LOCAL_DOMAIN" "$INBOUND_DOMAIN"; do
        openssl pkcs12 -export -out "$d.pfx" -inkey "$d.key" -in "$d.crt" -passout pass:
    done

fi

# Prosody's certmanager discards PEM with CRLF as "non-certificate (based on
# contents)" - without any error message that points at it. If the files come
# from a Windows tool, that is the trap.
for f in *.crt *.key; do sed -i 's/\r$//' "$f"; done
chmod 600 ./*.key

# --------------------------------------------------------- configuration ---

echo "== Configuration"
cat > "$PREFIX/etc/prosody.cfg.lua" <<CFG
-- Prosody as a far side for the federation run. Generated by
-- tools/prosody/setup.sh - changes here are lost at the next run.

daemonize          = false
pidfile            = "$PREFIX/run/prosody.pid"
allow_registration = false

-- Prosody refuses to start as root and says so in a way that does not reach
-- this script: it writes "Danger, Will Robinson!" to stdout and leaves
-- prosody.log empty, so the check further down reports "no certificates
-- loaded" - which points at the certificates, and those were fine.
--
-- On the developer machine this never comes up; in the CI container everything
-- runs as root. The switch is the one Prosody provides for exactly this case
-- (util/startup.lua checks pposix.getuid() == 0 against it), and off root it
-- does nothing at all, so the same file serves both without a variant.
--
-- It has to stand HERE, above the first VirtualHost, and that is not a matter
-- of taste: the check reads config.get("*", "run_as_root") - the global
-- section - and in Prosody's config language every setting after a VirtualHost
-- belongs to that host. Appended at the end of the file it parses, loads, and
-- silently does nothing; measured, not assumed. It is the same trap the
-- s2s_secure_auth comment below describes.
run_as_root        = true

modules_enabled = {
    "roster"; "saslauth"; "tls"; "dialback"; "disco";
    "posix"; "ping"; "time"; "uptime"; "version";

    -- XEP-0198 on the client side, and the transport that leads there. Our
    -- client speaks XMPP over WebSocket (RFC 7395), not over the raw 5222
    -- stream - without mod_websocket there would be no way in for it.
    "smacks"; "websocket";

    -- XEP-0288: allows both directions to be carried over one connection.
    -- Without this module Prosody answers an incoming stanza exclusively over
    -- an *own* outgoing connection to the sending domain - that is how RFC
    -- 6120 section 4.1 sees the stream, and how every full-grown server
    -- behaves.
    "s2s_bidi";
}

-- Both sides carry certificates from the same test CA. SASL EXTERNAL
-- (XEP-0178) thereby shows the domain, and dialback is not needed - which is
-- just as well here, because the dialback query needs a connection from WSL
-- to the Windows host, and the Hyper-V firewall blocks that.
s2s_secure_auth        = true

-- An exception for our domain, so that dialback (XEP-0220) can be checked as
-- well. Without it Prosody demands a valid certificate chain and refuses a
-- connection that wants to identify itself over dialback alone with
-- <not-authorized/>.
--
-- As an exception list and not as "s2s_secure_auth = false" on a VirtualHost
-- of its own: mod_s2s is a global module and reads the switch *once* on being
-- loaded (mod_s2s.lua, line 40). Set per VirtualHost it silently does nothing
-- - the setup looked right for a while and was not.
--
-- Which method actually comes into play is thereby decided by our side: if we
-- present a client certificate, Prosody offers EXTERNAL and we take it; if we
-- present none, only dialback is left.
s2s_insecure_domains   = { "$INBOUND_DOMAIN" }

s2s_require_encryption = true
c2s_require_encryption = true
s2s_connect_timeout    = 10

-- 5269 stays free: in the incoming run our test server listens there, and
-- without an SRV entry Prosody falls back on exactly that port.
s2s_ports  = { $PEER_S2S_PORT }
interfaces = { "127.0.0.1" }

-- The WebSocket endpoint for the client: wss://127.0.0.1:$HTTPS_PORT/xmpp-websocket.
-- The plaintext port stays empty, so that nothing unencrypted stands beside it.
https_ports      = { $HTTPS_PORT }
https_interfaces = { "127.0.0.1" }
http_ports       = { }

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

# ------------------------------------------------------------------ start ---

echo "== Start"
# shellcheck disable=SC1091
. "$PREFIX/env.sh"

# The pattern contains the interpreter, not only the path. "pkill -f" checks
# the whole command line, and a shell that contains this call carries the bare
# path within itself - it would thereby kill itself.
pkill -f "lua5.4 $ROOT/usr/bin/prosody" 2>/dev/null || true
sleep 1
: > "$PREFIX/prosody.log"

# The account for the client run. With the server stopped, because prosodyctl
# touches the same files as the running process.
#
# "register" and not "adduser": adduser asks for the password in a dialogue
# and fails without a terminal - silently, if its output is thrown away.
# register takes it as an argument. A second call sets the password anew,
# which is just right here.
for u in "$TEST_USER" "$TEST_USER2"; do
    "$ROOT/usr/bin/prosodyctl" register "$u" "$PEER_DOMAIN" "$TEST_PASSWORD" 2>&1 \
        | grep -i "User account\|error" || true
done

cd "$PREFIX"
nohup "$ROOT/usr/bin/prosody" > "$PREFIX/prosody.out" 2>&1 &
disown
sleep 4

if grep -q "Certificates loaded" "$PREFIX/prosody.log"; then
    echo "   Prosody is running, certificates loaded."
    grep -q "Serving 'websocket' at https://127.0.0.1:$HTTPS_PORT" "$PREFIX/prosody.log" \
        && echo "   WebSocket endpoint on $HTTPS_PORT." \
        || echo "   WARNING - no WebSocket endpoint on $HTTPS_PORT; the XEP-0198 run falls away."
else
    # Both files, and prosody.out first, because the case that actually
    # happened had prosody.log empty: Prosody had refused to start at all and
    # said so on stdout. "No certificates loaded" was then the only thing this
    # script reported, and it named the one thing that was not wrong - the
    # certificates had been generated and verified twenty lines earlier. A
    # start that never happened has to be told apart from a start that failed
    # to read its keys, and only prosody.out can do that.
    echo "   ERROR - Prosody did not report loaded certificates."
    echo "   --- prosody.out (stdout; a refusal to start shows up here) ---"
    tail -20 "$PREFIX/prosody.out" 2>/dev/null || echo "   (no prosody.out)"
    echo "   --- prosody.log (empty if Prosody never got that far) ---"
    tail -20 "$PREFIX/prosody.log" 2>/dev/null || echo "   (no prosody.log)"
    exit 1
fi

cat <<DONE

Done. Prosody serves $PEER_DOMAIN on 127.0.0.1:$PEER_S2S_PORT (S2S) and
wss://127.0.0.1:$HTTPS_PORT/xmpp-websocket (client), accounts
$TEST_USER@$PEER_DOMAIN and $TEST_USER2@$PEER_DOMAIN, password $TEST_PASSWORD.

Outgoing run, from Windows:

    \$env:JABBER_PROSODY_CERTS = '\\\\wsl.localhost\\Debian$PREFIX/certs'
    dotnet test XMPPConformanceTests\\XMPPConformanceTests.csproj --filter FullyQualifiedName~ProsodyFederationTests

Incoming run (P4) - that one has to run *in* WSL, because Prosody does not
reach us otherwise: the Hyper-V firewall discards every connection from WSL to
the Windows host, and there is no arguing with that without setting a firewall
rule. Inside WSL everything is loopback:

    JABBER_PROSODY_CERTS=$PREFIX/certs \\
    dotnet test /mnt/c/.../XMPPConformanceTests/XMPPConformanceTests.csproj \\
        --artifacts-path /tmp/conformance-artifacts \\
        --filter FullyQualifiedName~ProsodyFederationTests

Log:   $PREFIX/prosody.log
Stop:  pkill -f "lua5.4 $ROOT/usr/bin/prosody"
DONE
