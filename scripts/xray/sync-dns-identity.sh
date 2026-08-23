#!/bin/bash
# Sync per-user DNS identity IPs: iface aliases + Xray freedom sendThrough outbounds for :53 + restart.
# Env: CONFIG_PATH, XRAY_PID_FILE, XRAY_DNS_IDENTITY_IFACE, XRAY_DNS_IDENTITY_SUBNET
# Args: optional path to JSON array [{"commonName":"...","identityIp":"..."}, ...]
#   or reads CLIENTS_JSON env / stdin.
set -euo pipefail

CONFIG_PATH="${CONFIG_PATH:-/data/xray/config.json}"
XRAY_PID_FILE="${XRAY_PID_FILE:-/data/xray/xray.pid}"
IFACE="${XRAY_DNS_IDENTITY_IFACE:-eth0}"
SUBNET="${XRAY_DNS_IDENTITY_SUBNET:-10.80.0.0/24}"
TAG_PREFIX="dns-id-"

if ! command -v jq >/dev/null 2>&1; then
  echo "[dns-identity] ERROR: jq is required" >&2
  exit 1
fi

if ! command -v ip >/dev/null 2>&1; then
  echo "[dns-identity] ERROR: ip (iproute2) is required" >&2
  exit 1
fi

CLIENTS_JSON="${CLIENTS_JSON:-}"
if [[ $# -ge 1 && -f "$1" ]]; then
  CLIENTS_JSON="$(cat "$1")"
elif [[ -z "$CLIENTS_JSON" && ! -t 0 ]]; then
  CLIENTS_JSON="$(cat)"
fi
CLIENTS_JSON="${CLIENTS_JSON:-[]}"

echo "[dns-identity] Syncing identity IPs on $IFACE (subnet $SUBNET) into $CONFIG_PATH"

mapfile -t DESIRED_IPS < <(echo "$CLIENTS_JSON" | jq -r '.[].identityIp // empty' | sort -u)

TMP="$(mktemp --suffix=.json)"
trap 'rm -f "$TMP"' EXIT

# Build new config first; validate before touching aliases or live process.
jq --argjson clients "$CLIENTS_JSON" --arg prefix "$TAG_PREFIX" '
  .outbounds = ((.outbounds // []) | map(select((.tag // "") | startswith($prefix) | not)))
  | .routing.rules = ((.routing.rules // []) | map(select((.outboundTag // "") | startswith($prefix) | not)))
  | . as $root
  | reduce $clients[] as $c (
      $root;
      if ($c.identityIp != null and $c.identityIp != "" and $c.commonName != null and $c.commonName != "") then
        ($prefix + ($c.identityIp | gsub("\\."; "-"))) as $tag
        | .outbounds += [{
            protocol: "freedom",
            tag: $tag,
            sendThrough: $c.identityIp,
            settings: {}
          }]
        | .routing.rules = (
            [
              {
                type: "field",
                user: [$c.commonName],
                port: "53",
                network: "tcp,udp",
                outboundTag: $tag
              }
            ] + .routing.rules
          )
      else . end
    )
' "$CONFIG_PATH" >"$TMP"

echo "[dns-identity] Validating config JSON..."
# Structural check only. Avoid `xray run -test` while a live core is running:
# GNU timeout without -k can hang forever if xray ignores SIGTERM.
if ! jq -e '
  (.inbounds | type == "array")
  and (.outbounds | type == "array")
  and (.routing.rules | type == "array")
  and ([.outbounds[]? | select(.sendThrough != null)] | length) >= 0
' "$TMP" >/dev/null; then
  echo "[dns-identity] ERROR: generated config failed jq validation" >&2
  exit 1
fi
echo "[dns-identity] Config JSON OK"

# Aliases only after config validates
PREFIX="${SUBNET%%/*}"
OCTETS="$(echo "$PREFIX" | awk -F. '{print $1"."$2"."$3"."}')"
# ip -o -4 addr: "2: eth0    inet 10.80.0.2/32 scope ..." → address is field 4
while read -r line; do
  ip_addr="$(echo "$line" | awk '{print $4}' | cut -d/ -f1)"
  [[ -z "$ip_addr" ]] && continue
  case "$ip_addr" in
    ${OCTETS}*)
      keep=0
      for d in "${DESIRED_IPS[@]:-}"; do
        [[ "$ip_addr" == "$d" ]] && keep=1 && break
      done
      if [[ "$keep" -eq 0 ]]; then
        echo "[dns-identity] Removing alias $ip_addr/32"
        ip addr del "$ip_addr/32" dev "$IFACE" 2>/dev/null || true
      fi
      ;;
  esac
done < <(ip -o -4 addr show dev "$IFACE" 2>/dev/null || true)

for ip_addr in "${DESIRED_IPS[@]:-}"; do
  [[ -z "$ip_addr" ]] && continue
  if ! ip -o -4 addr show dev "$IFACE" | awk '{print $4}' | grep -qx "${ip_addr}/32"; then
    echo "[dns-identity] Adding alias $ip_addr/32"
    if ! out="$(ip addr add "$ip_addr/32" dev "$IFACE" 2>&1)"; then
      if echo "$out" | grep -qiE 'File exists|Address already assigned'; then
        echo "[dns-identity] Alias $ip_addr/32 already present"
      else
        echo "[dns-identity] ERROR: failed to add $ip_addr/32 (need NET_ADMIN / correct iface?): $out" >&2
        exit 1
      fi
    fi
  else
    echo "[dns-identity] Alias $ip_addr/32 already present"
  fi
done

mv "$TMP" "$CONFIG_PATH"
trap - EXIT

echo "[dns-identity] Restarting Xray..."
if [[ -f "$XRAY_PID_FILE" ]]; then
  old_pid="$(cat "$XRAY_PID_FILE" 2>/dev/null || true)"
  if [[ -n "${old_pid:-}" ]] && kill -0 "$old_pid" 2>/dev/null; then
    kill "$old_pid" 2>/dev/null || true
    for _ in $(seq 1 20); do
      kill -0 "$old_pid" 2>/dev/null || break
      sleep 0.25
    done
    kill -9 "$old_pid" 2>/dev/null || true
  fi
fi

# nohup + disown: background xray must survive script exit (SIGHUP).
nohup xray run -config "$CONFIG_PATH" >/dev/null 2>&1 &
new_pid=$!
disown "$new_pid" 2>/dev/null || true
echo "$new_pid" >"$XRAY_PID_FILE"
sleep 0.5
if ! kill -0 "$new_pid" 2>/dev/null; then
  echo "[dns-identity] ERROR: Xray failed to start after sync" >&2
  exit 1
fi

echo "[dns-identity] Xray restarted pid=$new_pid (clients=$(echo "$CLIENTS_JSON" | jq 'length'))"
