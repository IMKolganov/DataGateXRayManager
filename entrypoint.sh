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

if [ -n "$API_PORT" ]; then
  export ASPNETCORE_HTTP_PORTS="$API_PORT"
  echo "[entrypoint] ASPNETCORE_HTTP_PORTS=$ASPNETCORE_HTTP_PORTS"
fi

mkdir -p "$DATA_DIR/xray"
ACCESS_LOG="$DATA_DIR/xray/access.log"
ERROR_LOG="$DATA_DIR/xray/error.log"
touch "$ACCESS_LOG" "$ERROR_LOG"

CONFIG_PATH="${CONFIG_PATH:-$DATA_DIR/xray/config.json}"

export CONFIG_PATH ACCESS_LOG ERROR_LOG PORT DNS1 DNS2
export XRAY_MGMT_HOST XRAY_MGMT_PORT INBOUND_TAG
export XRAY_TRANSPORT_MODE="${XRAY_TRANSPORT_MODE:-plain}"
export XRAY_EXTERNAL_CONFIG_PATH="${XRAY_EXTERNAL_CONFIG_PATH:-}"
export XRAY_TLS_CERT_FILE="${XRAY_TLS_CERT_FILE:-}"
export XRAY_TLS_KEY_FILE="${XRAY_TLS_KEY_FILE:-}"
export XRAY_REALITY_PRIVATE_KEY="${XRAY_REALITY_PRIVATE_KEY:-}"
export XRAY_REALITY_DEST="${XRAY_REALITY_DEST:-}"
export XRAY_REALITY_SERVER_NAMES="${XRAY_REALITY_SERVER_NAMES:-}"
export XRAY_REALITY_SHORT_IDS="${XRAY_REALITY_SHORT_IDS:-}"

echo "[entrypoint] Rendering XRay config (mode=$XRAY_TRANSPORT_MODE)..."
/scripts/xray/render-config.sh

echo "[entrypoint] Validating XRay config..."
xray run -test -config "$CONFIG_PATH"

echo "[entrypoint] Starting XRay..."
xray run -config "$CONFIG_PATH" &
XRAY_PID=$!
trap 'kill $XRAY_PID 2>/dev/null || true' EXIT

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
