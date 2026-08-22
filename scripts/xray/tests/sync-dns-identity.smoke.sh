#!/usr/bin/env bash
# Local smoke for sync-dns-identity.sh (no production server).
# Runs inside the xray image with NET_ADMIN; asserts exit 0 twice (idempotent) under a deadline.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
SCRIPT="${ROOT}/scripts/xray/sync-dns-identity.sh"
IMAGE="${SYNC_SMOKE_IMAGE:-imkolganov/datagate-monitor-xray:latest}"
DEADLINE_SEC="${SYNC_SMOKE_DEADLINE_SEC:-25}"

if [[ ! -f "$SCRIPT" ]]; then
  echo "missing $SCRIPT" >&2
  exit 1
fi

echo "[smoke] image=$IMAGE deadline=${DEADLINE_SEC}s"

docker run --rm --cap-add NET_ADMIN \
  -v "$SCRIPT:/scripts/xray/sync-dns-identity.sh:ro" \
  --entrypoint bash \
  "$IMAGE" \
  -c "
set -euo pipefail
DEADLINE=${DEADLINE_SEC}

mkdir -p /tmp/smoke/xray
cat > /tmp/smoke/xray/config.json <<'EOF'
{
  \"log\": { \"loglevel\": \"warning\" },
  \"inbounds\": [
    {
      \"listen\": \"127.0.0.1\",
      \"port\": 10085,
      \"protocol\": \"dokodemo-door\",
      \"settings\": { \"address\": \"127.0.0.1\" },
      \"tag\": \"api\"
    },
    {
      \"listen\": \"0.0.0.0\",
      \"port\": 443,
      \"protocol\": \"vless\",
      \"tag\": \"vless-in\",
      \"settings\": { \"clients\": [], \"decryption\": \"none\" },
      \"streamSettings\": { \"network\": \"tcp\", \"security\": \"none\" }
    }
  ],
  \"outbounds\": [ { \"protocol\": \"freedom\", \"tag\": \"direct\" } ],
  \"routing\": {
    \"rules\": [
      { \"type\": \"field\", \"inboundTag\": [\"api\"], \"outboundTag\": \"api\" }
    ]
  },
  \"dns\": { \"servers\": [\"172.20.0.1\"] }
}
EOF

# Start a live xray so sync must restart it (prod-like).
xray run -config /tmp/smoke/xray/config.json &
echo \$! > /tmp/smoke/xray/xray.pid
sleep 0.5
kill -0 \"\$(cat /tmp/smoke/xray/xray.pid)\"

IFACE=\$(ip -br link | awk '\$1 != \"lo\" {print \$1; exit}' | cut -d@ -f1)
echo \"[smoke] using iface=\$IFACE\"
ip link show \"\$IFACE\" >/dev/null

export CONFIG_PATH=/tmp/smoke/xray/config.json
export XRAY_PID_FILE=/tmp/smoke/xray/xray.pid
export XRAY_DNS_IDENTITY_IFACE=\"\$IFACE\"
export XRAY_DNS_IDENTITY_SUBNET=10.80.0.0/24
export CLIENTS_JSON='[{\"commonName\":\"user-a\",\"identityIp\":\"10.80.0.2\"},{\"commonName\":\"user-b\",\"identityIp\":\"10.80.0.3\"}]'

run_once() {
  local label=\"\$1\"
  echo \"[smoke] \$label starting\"
  local start=\$(date +%s)
  if ! timeout \"\${DEADLINE}s\" /scripts/xray/sync-dns-identity.sh; then
    echo \"[smoke] FAIL: \$label did not finish within \${DEADLINE}s (or non-zero exit)\" >&2
    exit 1
  fi
  local end=\$(date +%s)
  echo \"[smoke] \$label ok in \$((end-start))s\"
}

run_once first
# Second run must be idempotent (aliases already present).
run_once second

# Assertions
jq -e '
  ([.outbounds[] | select(.tag|startswith(\"dns-id-\"))] | length) == 2
  and ([.routing.rules[] | select(.port==\"53\")] | length) == 2
  and (.outbounds[] | select(.tag==\"dns-id-10-80-0-2\") | .sendThrough) == \"10.80.0.2\"
' \"\$CONFIG_PATH\" >/dev/null

ip -o -4 addr show dev \"\$IFACE\" | awk '{print \$4}' | grep -qx '10.80.0.2/32'
ip -o -4 addr show dev \"\$IFACE\" | awk '{print \$4}' | grep -qx '10.80.0.3/32'
kill -0 \"\$(cat \$XRAY_PID_FILE)\"

echo '[smoke] PASS'
"
