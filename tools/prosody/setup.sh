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
# default for HTTPS - and the same port carries the file uploads of XEP-0363,
# because mod_http_file_share hangs itself into the same HTTP server.
HTTPS_PORT=5281

# Three accounts on Prosody. Two of them because one client needs somebody to
# talk to: without the second there is no checking whether a message handed in
# during the outage comes after the resumption.
#
# The third is a stranger, and the point is that she stays one. alice and bob
# subscribe to each other for the avatar lane, and a roster on a real server
# outlives the test, the suite and the machine - there is no way back from that.
# So every question about what somebody NOT on the roster may see needs an
# account nobody has ever asked for anything. carol is that account: nothing
# subscribes her to anybody, and whatever reaches her, reaches her as a
# stranger.
TEST_USER="alice"
TEST_USER2="bob"
TEST_USER3="carol"
TEST_PASSWORD="geheim"

mkdir -p "$PREFIX"/{debs,etc,var/lib,certs,run} "$ROOT"

# --------------------------------------------------------------- packages ---

echo "== Fetching and unpacking the packages"
cd "$PREFIX/debs"

# libicu76 does not stand among the dependencies of prosody, but is needed by
# util.encodings.so - without it the start breaks at the first require.
#
# JABBER_PROSODY_UPSTREAM (D138) takes prosody itself out of Prosody's own
# Debian repository instead of out of Debian's. That is what the informational
# "upstream" lane of nightly.yml uses to ask whether a newer Prosody still
# behaves the way the measurements recorded here say it does - Debian 13 ships
# 13.0.1, upstream is some way past it, and a divergence written down against
# one version says nothing about the other.
#
# Exactly one package changes. The upstream build is made for Debian, unpacks
# into the same paths and wants the same dependencies, so everything below this
# block stays as it is.
if [ -n "${JABBER_PROSODY_UPSTREAM:-}" ]; then

    apt-get download \
        lua5.4 lua-bitop lua-expat lua-filesystem lua-sec lua-socket \
        ssl-cert libicu76 >/dev/null

    # Read out of the repository rather than written down here: a lane whose
    # question is "does a newer one still fit" has to follow upstream, and a
    # version named in this file would stop following it the day it was typed.
    REPO="https://packages.prosody.im/debian"
    FILE="$(curl -fsSL "$REPO/dists/trixie/main/binary-amd64/Packages" \
            | awk '/^Package: prosody$/,/^$/' \
            | awk '/^Filename: /{print $2; exit}')"

    if [ -z "$FILE" ]; then
        echo "   No prosody in $REPO - this lane would measure nothing."
        exit 1
    fi

    wget -q -O "prosody-upstream.deb" "$REPO/$FILE"
    PROSODY_DEB="prosody-upstream.deb"

else

    apt-get download \
        prosody lua5.4 lua-bitop lua-expat lua-filesystem lua-sec lua-socket \
        ssl-cert libicu76 >/dev/null
    PROSODY_DEB="$(printf '%s\n' prosody_*.deb | head -1)"

fi

# Which Prosody this actually is, said out loud and into the log. A measured
# divergence is worth exactly as much as the note of which peer it was measured
# against, and that note has to be taken here, where the answer is still known
# (D138).
# Named in each branch above and not looked for here: `ls a b` exits 2 when one
# of the two operands does not exist, 2>/dev/null hides the message and not the
# status, and under `set -euo pipefail` that ends the script. It did, in all
# three lanes at once, and only because the version line is the one piece of
# D138 that runs in the old branch too - the new one had been tried and the
# path it shares with the old one had not.
echo "   Prosody $(dpkg-deb -f "$PROSODY_DEB" Version)"

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

# Existence is not the question, validity is - and this condition used to ask
# only the first. The certificates below are minted with "-days 30", so on day
# 31 ca.crt is still a file, this block is skipped, and the far side comes up
# with an expired chain.
#
# Nothing says so at the time. Prosody and ejabberd both load an expired
# certificate without complaint and report their certificates loaded; the
# failure arrives much later, in a suite that has nothing to do with the clock,
# as "The remote certificate was rejected by the provided
# RemoteCertificateValidationCallback" on every TLS test at once. That is the
# same mistake D105 describes for the inbound probe: a check that asks what is
# convenient to ask rather than what it needs to know, and that agrees with the
# truth right up to the day it does not.
#
# "-checkend 86400" answers "will this still be valid in a day": the margin
# keeps a suite that starts now from expiring halfway through. The leaf
# certificates need no check of their own - they are minted in this same block,
# with the same -days, and signed by this CA, so they stand and fall with it.
if [ ! -f ca.crt ] || ! openssl x509 -in ca.crt -noout -checkend 86400 >/dev/null 2>&1; then

    openssl req -x509 -newkey rsa:2048 -keyout ca.key -out ca.crt -days 30 -nodes \
        -subj "/CN=XMPPConformanceTests Federation Test CA" \
        -addext "basicConstraints=critical,CA:TRUE" \
        -addext "keyUsage=critical,keyCertSign,cRLSign" 2>/dev/null

    for d in "$PEER_DOMAIN" "$LOCAL_DOMAIN" "$INBOUND_DOMAIN"; do

        openssl req -newkey rsa:2048 -keyout "$d.key" -out "$d.csr" -nodes \
                -subj "/CN=$d" 2>/dev/null

        # The room service is a domain of its own - a component lives beside
        # the host, not inside it - so it needs a name of its own on the
        # certificate. Without the second SAN our server refuses the TLS
        # handshake to conference.prosody.test, and the refusal reads like a
        # broken peer rather than like a missing line here.
        if [ "$d" = "$PEER_DOMAIN" ]; then
            SAN="DNS:$d,DNS:conference.$d"
        else
            SAN="DNS:$d"
        fi

        # clientAuth has to go in as well: with SASL EXTERNAL the connecting
        # server presents its certificate as a client certificate.
        cat > "$d.ext" <<EXT
subjectAltName=$SAN
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

    -- XEP-0313 for the accounts themselves. The room archive is switched on
    -- separately, on the component further down - they are two archives and a
    -- client asks them the same way but at different addresses.
    --
    -- archive_policy has to be said: Prosody's default keeps only what was
    -- exchanged with somebody in the roster, and the two test accounts have
    -- each other nowhere. The default is the careful one for a real server and
    -- would make this archive silently empty here.
    "mam";

    -- XEP-0163 (and XEP-0084 on top of it). Personal eventing is a module in
    -- Prosody and is not loaded by default, which the avatar lane found the
    -- blunt way: every publish came back <service-unavailable/>.
    --
    -- Worth naming because the mistake that led there was an assumption, not a
    -- typo. "Prosody does pubsub, so it does PEP" is the kind of thing that
    -- sounds like knowledge and is a guess - the same guess this suite has now
    -- made four times, for MUC, for the archive, for the upload service and
    -- here.
    "pep";

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

archive_policy = true

VirtualHost "$PEER_DOMAIN"
    ssl = {
        certificate = "$PREFIX/certs/$PEER_DOMAIN.crt";
        key         = "$PREFIX/certs/$PEER_DOMAIN.key";
        cafile      = "$PREFIX/certs/ca.crt";
    }

-- XEP-0045. A room service is a component: its own domain beside the host,
-- reached over its own s2s connection, with its own entry in the peer list on
-- our side. That is why it needs the certificate of its own above - from the
-- outside conference.prosody.test is simply another server.
--
-- restrict_room_creation = false so that a room comes into being by somebody
-- entering it. The alternative is to create rooms in the set-up, and then the
-- test no longer covers the one thing about entering a room that is easy to get
-- wrong: status 201 for a room that did not exist a moment ago.
Component "conference.$PEER_DOMAIN" "muc"
    restrict_room_creation = false

    -- XEP-0359, and it is not optional decoration here. A reply into a room
    -- may not point at the id of the stanza (XEP-0461, section 4) - everybody
    -- present sees a different one - so it points at the name the room itself
    -- gave the message, in a <stanza-id/>. Prosody attaches one only when the
    -- room archives, so without this module nothing said in a room can be
    -- answered at all. Measured, not assumed: the first run of these tests
    -- found no stanza-id anywhere.
    modules_enabled = { "muc_mam" }
    muc_log_by_default = true
    ssl = {
        certificate = "$PREFIX/certs/$PEER_DOMAIN.crt";
        key         = "$PREFIX/certs/$PEER_DOMAIN.key";
        cafile      = "$PREFIX/certs/ca.crt";
    }

-- XEP-0363. A second component, and it is the one place in this setup where
-- the interesting half does not speak XMPP at all: the slot is asked for over
-- the stream, the file goes over HTTPS, and only the two together make an
-- upload. mod_http_file_share is core in Prosody 13.
Component "upload.$PEER_DOMAIN" "http_file_share"

    -- http_host decides two things at once, and they have to agree: the host
    -- the HTTP routes are registered under, and the host that turns up in the
    -- URL the slot hands out. Left at its default both would say
    -- "upload.$PEER_DOMAIN" - a name nothing here can resolve, because this
    -- setup needs no root and therefore writes no /etc/hosts. The tests dial
    -- 127.0.0.1, so that is what the service must call itself.
    --
    -- Not http_file_share_base_url, which looks like the same thing and is
    -- the opposite: it means "somebody else serves these files" and makes
    -- Prosody register no routes at all (mod_http_file_share.lua, line 605).
    http_host  = "127.0.0.1"
    http_paths = { file_share = "/upload" }

    -- Who may ask for a slot. Prosody 13 decides this through roles, and a
    -- component has none for our accounts - without this line every request
    -- comes back <forbidden/>, which reads exactly like a client fault.
    http_file_share_access = { "$PEER_DOMAIN" }

    -- Small on purpose. The limit is announced in the disco form, and a test
    -- that wants to see a refusal has to be able to exceed it without moving
    -- ten megabytes through the loopback.
    http_file_share_size_limit = 1048576

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
for u in "$TEST_USER" "$TEST_USER2" "$TEST_USER3"; do
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

    # The same question for the upload service, and asked the same way: what
    # the log says it serves, not what the config asked for. A component whose
    # http_host does not take hold comes up silently and hands out URLs
    # pointing at a name that resolves nowhere.
    grep -q "Serving 'file_share' at https://127.0.0.1:$HTTPS_PORT/upload" "$PREFIX/prosody.log" \
        && echo "   Upload endpoint on $HTTPS_PORT/upload." \
        || echo "   WARNING - no upload endpoint on $HTTPS_PORT/upload; the XEP-0363 run falls away."
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
$TEST_USER@$PEER_DOMAIN, $TEST_USER2@$PEER_DOMAIN and $TEST_USER3@$PEER_DOMAIN,
password $TEST_PASSWORD. The third one is a stranger to the other two and is
meant to stay one - see the note beside the names in this script.

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
