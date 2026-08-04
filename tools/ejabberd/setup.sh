#!/usr/bin/env bash
#
# Sets up an ejabberd far side for the federation run - without root.
#
# Why a second one at all: Prosody alone shows that we can do Prosody. Only a
# second, independently grown implementation tells "right" apart from "happens
# to coincide with one reading". ejabberd is written in Erlang, has a
# different history and different preferences in the handshake - which is
# exactly why it is the interesting second opponent.
#
# Called from within WSL (Debian):
#     bash tools/ejabberd/setup.sh
#
# Afterwards ejabberd serves the domain ejabberd.test on 127.0.0.1:25269 with
# a certificate from a test CA of its own. The test suite finds it over
# JABBER_EJABBERD_CERTS.

set -euo pipefail

PREFIX="${EJABBERD_TEST_PREFIX:-$HOME/ejabberd-test}"
ROOT="$PREFIX/root"
ARCH_DIR="x86_64-linux-gnu"

PEER_DOMAIN="ejabberd.test"

# The same split in two as in the Prosody setup: jabber.test for the outgoing
# run (the address stands at our end by hand, no DNS is needed), localhost for
# the incoming one (so that *ejabberd* can resolve us, without an /etc/hosts
# entry and thereby root being needed).
LOCAL_DOMAIN="jabber.test"
INBOUND_DOMAIN="localhost"

# Both ports lie beside those of the Prosody setup, so that the two far sides
# can run next to each other and no run hangs on the wrong server by accident:
#
#   25269 - here ejabberd listens.       (Prosody: 15269)
#    5270 - here ejabberd dials us       (Prosody falls back to 5269)
#
# The second value is freely choosable only because ejabberd has an express
# switch for it: without an SRV entry it takes outgoing_s2s_port. Prosody
# knows none and stays at 5269.
PEER_S2S_PORT=25269
INBOUND_PORT=5270

# The WebSocket endpoint for the client run (XEP-0198). 5443 is ejabberd's
# default; Prosody lies on 5281, so both can stand next to each other.
WSS_PORT=5443

# Two accounts: one for the client itself, one as a sender. Without the second
# there is no checking whether a message handed in during the outage comes
# after the resumption.
TEST_USER="alice"
TEST_USER2="bob"
TEST_PASSWORD="geheim"

mkdir -p "$PREFIX"/{debs,etc,logs,spool,certs} "$ROOT"

# --------------------------------------------------------------- packages ---

echo "== Fetching and unpacking the packages"
cd "$PREFIX/debs"

# ejabberd drags in half the Erlang runtime - forty-one packages. Instead of
# listing them and maintaining that list at every change of Debian, the set
# can be worked out by apt itself: --print-uris resolves without installing.
apt-get install --print-uris -y --no-install-recommends ejabberd 2>/dev/null \
    | grep "^'http" | cut -d"'" -f2 > uris.txt

echo "   $(wc -l < uris.txt) packages"
wget -q -N -i uris.txt

for f in ./*.deb; do dpkg-deb -x "$f" "$ROOT"; done

# Debian wires ROOTDIR into the Erlang start scripts - and in all three
# branches of the case distinction, including the one that according to the
# source is meant to heed ERL_ROOTDIR. Setting that variable therefore looks
# as if it ought to be enough, and does nothing.
ERTS_DIR="$(basename "$(ls -d "$ROOT"/usr/lib/erlang/erts-* | head -1)")"

for f in erl erlc escript dialyzer typer start_erl; do
    [ -f "$ROOT/usr/lib/erlang/bin/$f" ] || continue
    sed -i "s|ROOTDIR=/usr/lib/erlang|ROOTDIR=$ROOT/usr/lib/erlang|g" \
        "$ROOT/usr/lib/erlang/bin/$f"
done

# ejabberdctl is the Debian launcher and carries three paths and a user name
# built in. The user name is the most important intervention: the script
# breaks off with "can only be run by root or the user ejabberd" before it
# does anything at all.
sed -i \
    -e "s|^ERL=\"/usr/bin/erl\"|ERL=\"$ROOT/usr/lib/erlang/bin/erl\"|" \
    -e "s|^EPMD=\"/usr/bin/epmd\"|EPMD=\"$ROOT/usr/lib/erlang/$ERTS_DIR/bin/epmd\"|" \
    -e "s|^INSTALLUSER=ejabberd|INSTALLUSER=|" \
    -e "s|^ERL_LIBS='/usr/lib/$ARCH_DIR'|ERL_LIBS='$ROOT/usr/lib/$ARCH_DIR'|" \
    "$ROOT/usr/sbin/ejabberdctl"
chmod +x "$ROOT/usr/sbin/ejabberdctl"

# The remaining paths ejabberdctl takes from the environment: it sets its
# defaults with ": ${VAR:=...}", which leaves values already set standing.
cat > "$PREFIX/env.sh" <<ENV
export LD_LIBRARY_PATH="$ROOT/usr/lib/$ARCH_DIR:\${LD_LIBRARY_PATH:-}"
export PATH="$ROOT/usr/sbin:$ROOT/usr/lib/erlang/bin:\${PATH:-}"
export CONFIG_DIR="$PREFIX/etc"
export LOGS_DIR="$PREFIX/logs"
export SPOOL_DIR="$PREFIX/spool"
export ERL_EPMD_ADDRESS="127.0.0.1"
ENV

cp "$ROOT/etc/ejabberd/inetrc" "$PREFIX/etc/inetrc"

# ----------------------------------------------------------- certificates ---

echo "== Test CA and certificates"
cd "$PREFIX/certs"

# A CA of its own, not the one from the Prosody setup: that way each far side
# stays buildable by itself, and a run against the one cannot silently live
# off a certificate of the other.
if [ ! -f ca.crt ]; then

    openssl req -x509 -newkey rsa:2048 -keyout ca.key -out ca.crt -days 30 -nodes \
        -subj "/CN=XMPPConformanceTests ejabberd Test CA" \
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

# ejabberd expects key and certificate in one file.
for f in *.crt *.key; do sed -i 's/\r$//' "$f"; done
cat "$PEER_DOMAIN.key" "$PEER_DOMAIN.crt" > "$PEER_DOMAIN.pem"
chmod 600 ./*.key "$PEER_DOMAIN.pem"

# --------------------------------------------------------- configuration ---

echo "== Configuration"
cat > "$PREFIX/etc/ejabberd.yml" <<CFG
### ejabberd as a far side for the federation run. Generated by
### tools/ejabberd/setup.sh - changes here are lost at the next run.

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

  ## The way in for our client: it speaks XMPP over WebSocket (RFC 7395), not
  ## over the raw 5222 stream. Without this handler there would be no way in
  ## for it.
  -
    port: $WSS_PORT
    ip: "127.0.0.1"
    module: ejabberd_http
    tls: true
    request_handlers:
      /websocket: ejabberd_http_ws

## "required", not "required_trusted": both demand STARTTLS, but only the
## second demands a valid chain on top of it and would thereby rule dialback
## out. This way our side decides which method comes into play - if we present
## a client certificate, ejabberd offers EXTERNAL; if we present none,
## dialback is left.
s2s_use_starttls: required
s2s_access: all
s2s_dns_timeout: 5

## Without an SRV entry ejabberd dials this port. Prosody knows no such switch
## and falls back firmly on 5269 - which is why our incoming listener can
## stand on a port of its own here, and both far sides can run next to each
## other.
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

  ## XEP-0220: without this module ejabberd refuses every connection that
  ## cannot identify itself over a certificate.
  mod_s2s_dialback: {}

  ## XEP-0288: allows both directions over one connection. Without the module
  ## ejabberd answers an incoming stanza exclusively over an *own* outgoing
  ## connection - that is how RFC 6120 section 4.1 sees the stream, and how
  ## every full-grown server behaves.
  mod_s2s_bidi: {}

  ## XEP-0198 together with resumption. resume_timeout short enough for a test
  ## to wait out the expiry, long enough for a reconnect.
  mod_stream_mgmt:
    resume_timeout: 60
    max_resume_timeout: 300
CFG

# ------------------------------------------------------------------ start ---

echo "== Start"
# shellcheck disable=SC1091
. "$PREFIX/env.sh"

"$ROOT/usr/sbin/ejabberdctl" stop >/dev/null 2>&1 || true

# Wait until the node is really gone: "stop" returns at once, the shutdown is
# still running. A "start" that comes too early then fails with "node is
# already running" - and with "set -e" that ends this script.
for _ in $(seq 20); do
    "$ROOT/usr/sbin/ejabberdctl" status >/dev/null 2>&1 || break
    sleep 1
done

rm -f "$PREFIX/logs/ejabberd.log"

"$ROOT/usr/sbin/ejabberdctl" start

# Not "ejabberdctl started": that waiting runs over a second Erlang node,
# which on the first attempt finds no name in the epmd yet and then does not
# wait but breaks off hard - "Runtime terminating during boot". Waiting from
# the outside gets by without that second node.
started=0
for _ in $(seq 30); do
    if "$ROOT/usr/sbin/ejabberdctl" status 2>/dev/null | grep -q "is running"; then
        started=1
        break
    fi
    sleep 1
done

if [ "$started" = 1 ]; then

    echo "   ejabberd is running."

    # The accounts only now: ejabberdctl register goes over an RPC call into
    # the running node, unlike Prosody's prosodyctl, which touches the files
    # directly. With the server stopped there would only be a "nodedown" here.
    for u in "$TEST_USER" "$TEST_USER2"; do
        "$ROOT/usr/sbin/ejabberdctl" register "$u" "$PEER_DOMAIN" "$TEST_PASSWORD" 2>&1 \
            | grep -iv "^$" | head -1 || true
    done

    grep -q "Start accepting TLS connections at 127.0.0.1:$WSS_PORT" "$PREFIX/logs/ejabberd.log" \
        && echo "   WebSocket endpoint on $WSS_PORT." \
        || echo "   WARNING - no WebSocket endpoint on $WSS_PORT; the XEP-0198 run falls away."

else
    echo "   ERROR - ejabberd has not come up:"
    tail -30 "$PREFIX/logs/ejabberd.log" 2>/dev/null
    exit 1
fi

cat <<DONE

Done. ejabberd serves $PEER_DOMAIN on 127.0.0.1:$PEER_S2S_PORT (S2S) and
wss://127.0.0.1:$WSS_PORT/websocket (client), accounts
$TEST_USER@$PEER_DOMAIN and $TEST_USER2@$PEER_DOMAIN, password $TEST_PASSWORD.

Outgoing run, from Windows:

    \$env:JABBER_EJABBERD_CERTS = '\\\\wsl.localhost\\Debian$PREFIX/certs'
    dotnet test XMPPConformanceTests\\XMPPConformanceTests.csproj --filter FullyQualifiedName~EjabberdFederationTests

Incoming run - that one has to run *in* WSL, because ejabberd does not reach
us otherwise: the Hyper-V firewall discards every connection from WSL to the
Windows host. Inside WSL everything is loopback:

    JABBER_EJABBERD_CERTS=$PREFIX/certs \\
    dotnet test /mnt/c/.../XMPPConformanceTests/XMPPConformanceTests.csproj \\
        --artifacts-path /tmp/conformance-artifacts \\
        --filter FullyQualifiedName~EjabberdFederationTests

Log:   $PREFIX/logs/ejabberd.log
Stop:  CONFIG_DIR=$PREFIX/etc LOGS_DIR=$PREFIX/logs SPOOL_DIR=$PREFIX/spool \\
       $ROOT/usr/sbin/ejabberdctl stop
DONE
