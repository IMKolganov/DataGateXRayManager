#!/bin/bash
set -e

API_PORT="${API_PORT:-5010}"
PORT="${PORT:-443}"
DATA_DIR="${DATA_DIR:-/data}"
DNS1="${DNS1:-8.8.8.8}"
DNS2="${DNS2:-8.8.4.4}"
XRAY_MGMT_HOST="${XRayManagement__Host:-127.0.0.1}"
XRAY_MGMT_PORT="${XRayManagement__Port:-10085}"
INBOUND_TAG="${XRay__InboundTag:-vless-in}"
XRAY_DNS_IDENTITY_ENABLED="${XRAY_DNS_IDENTITY_ENABLED:-false}"
XRAY_DNS_IDENTITY_SUBNET="${XRAY_DNS_IDENTITY_SUBNET:-10.80.0.0/24}"
XRAY_DNS_IDENTITY_IFACE="${XRAY_DNS_IDENTITY_IFACE:-eth0}"
XRAY_PID_FILE="${XRAY_PID_FILE:-$DATA_DIR/xray/xray.pid}"

if [ -n "$API_PORT" ]; then
  export ASPNETCORE_HTTP_PORTS="$API_PORT"
  echo "[entrypoint] ASPNETCORE_HTTP_PORTS=$ASPNETCORE_HTTP_PORTS"
fi

mkdir -p "$DATA_DIR/xray"
ACCESS_LOG="$DATA_DIR/xray/access.log"
ERROR_LOG="$DATA_DIR/xray/error.log"
touch "$ACCESS_LOG" "$ERROR_LOG"
# Pi-hole DNS collector cursor / runtime config (same layout idea as OpenVPN DATA_DIR)
touch "$DATA_DIR/pihole-query-cursor.txt" 2>/dev/null || true


CONFIG_PATH="${CONFIG_PATH:-$DATA_DIR/xray/config.json}"

export CONFIG_PATH ACCESS_LOG ERROR_LOG PORT DNS1 DNS2
export XRAY_MGMT_HOST XRAY_MGMT_PORT INBOUND_TAG
export XRAY_DNS_IDENTITY_ENABLED XRAY_DNS_IDENTITY_SUBNET XRAY_DNS_IDENTITY_IFACE XRAY_PID_FILE
export XRAY_TRANSPORT_MODE="${XRAY_TRANSPORT_MODE:-plain}"
export XRAY_EXTERNAL_CONFIG_PATH="${XRAY_EXTERNAL_CONFIG_PATH:-}"
export XRAY_ACCEPT_PROXY_PROTOCOL="${XRAY_ACCEPT_PROXY_PROTOCOL:-false}"
export XRAY_TCP_KEEPALIVE_IDLE="${XRAY_TCP_KEEPALIVE_IDLE:-60}"
export XRAY_TCP_KEEPALIVE_INTERVAL="${XRAY_TCP_KEEPALIVE_INTERVAL:-15}"
export XRAY_TLS_CERT_FILE="${XRAY_TLS_CERT_FILE:-}"
export XRAY_TLS_KEY_FILE="${XRAY_TLS_KEY_FILE:-}"
export XRAY_REALITY_PRIVATE_KEY="${XRAY_REALITY_PRIVATE_KEY:-}"
export XRAY_REALITY_DEST="${XRAY_REALITY_DEST:-}"
export XRAY_REALITY_SERVER_NAMES="${XRAY_REALITY_SERVER_NAMES:-}"
export XRAY_REALITY_SHORT_IDS="${XRAY_REALITY_SHORT_IDS:-}"
export XRAY_XHTTP_ENABLED="${XRAY_XHTTP_ENABLED:-false}"
export XRAY_XHTTP_PORT="${XRAY_XHTTP_PORT:-2053}"
export XRAY_XHTTP_PATH="${XRAY_XHTTP_PATH:-/api/v1/update}"
export XRAY_XHTTP_MODE="${XRAY_XHTTP_MODE:-auto}"
export XRAY_XHTTP_INBOUND_TAG="${XRAY_XHTTP_INBOUND_TAG:-vless-xhttp-in}"

echo "[entrypoint] Rendering XRay config (mode=$XRAY_TRANSPORT_MODE, acceptProxyProtocol=$XRAY_ACCEPT_PROXY_PROTOCOL, dnsIdentity=$XRAY_DNS_IDENTITY_ENABLED, keepalive=${XRAY_TCP_KEEPALIVE_IDLE}s/${XRAY_TCP_KEEPALIVE_INTERVAL}s, xhttp=$XRAY_XHTTP_ENABLED)..."
/scripts/xray/render-config.sh

echo "[entrypoint] Validating XRay config..."
xray run -test -config "$CONFIG_PATH"

# Clients must be pushed (adu) to every inbound that exists, otherwise a user created while the
# xHTTP inbound is up can connect on :$PORT but not on :$XRAY_XHTTP_PORT. Derived from the rendered
# config rather than from XRAY_XHTTP_ENABLED, so a skipped/rolled-back inbound is never announced.
if command -v jq >/dev/null 2>&1 \
  && jq -e --arg t "$XRAY_XHTTP_INBOUND_TAG" 'any(.inbounds[]?; .tag == $t)' "$CONFIG_PATH" >/dev/null 2>&1; then
  export XRay__ExtraInboundTags="$XRAY_XHTTP_INBOUND_TAG"
  echo "[entrypoint] Extra inbound tags for client push: $XRay__ExtraInboundTags"
fi

echo "[entrypoint] Starting XRay..."
xray run -config "$CONFIG_PATH" &
XRAY_PID=$!
echo "$XRAY_PID" >"$XRAY_PID_FILE"
trap 'kill $XRAY_PID 2>/dev/null || true; rm -f "$XRAY_PID_FILE"' EXIT

# Avoid race: DataGateXRayManager calls `xray api` against XRayManagement__Host:Port (default 127.0.0.1:10085).
echo "[entrypoint] Waiting for Xray API ${XRAY_MGMT_HOST}:${XRAY_MGMT_PORT}..."
wait_timeout=30
wait_elapsed=0
while true; do
  if bash -c "echo > /dev/tcp/${XRAY_MGMT_HOST}/${XRAY_MGMT_PORT}" 2>/dev/null; then
    echo "[entrypoint] Xray API port is open."
    break
  fi
  if [ "$wait_elapsed" -ge "$wait_timeout" ]; then
    echo "[entrypoint] WARNING: Xray API did not open within ${wait_timeout}s; manager may log dial errors until ready."
    break
  fi
  sleep 1
  wait_elapsed=$((wait_elapsed + 1))
done

if ! kill -0 "$XRAY_PID" 2>/dev/null; then
  echo "[entrypoint] ERROR: Xray process (pid $XRAY_PID) exited before manager start. Check $ERROR_LOG"
  wait "$XRAY_PID" 2>/dev/null || true
  exit 1
fi

echo "[entrypoint] Waiting for .NET artifacts..."
timeout=30
elapsed=0
while [ ! -f /app/DataGateXRayManager.dll ]; do
  if [ "$elapsed" -ge "$timeout" ]; then
    echo "ERROR: DataGateXRayManager.dll not found"
    ls -la /app
    exit 1
  fi
  sleep 1
  elapsed=$((elapsed + 1))
done

cd /app
echo "[entrypoint] Starting DataGateXRayManager..."
exec dotnet DataGateXRayManager.dll
