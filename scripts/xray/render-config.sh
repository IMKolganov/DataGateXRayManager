#!/bin/bash
# Renders XRay config.json from XRAY_TRANSPORT_MODE: plain | tls | reality
# Env: CONFIG_PATH, ACCESS_LOG, ERROR_LOG, PORT, DNS1, DNS2,
#      XRAY_MGMT_HOST, XRAY_MGMT_PORT, INBOUND_TAG
# Optional: XRAY_EXTERNAL_CONFIG_PATH (copy this file and skip generation)
# Optional: XRAY_ACCEPT_PROXY_PROTOCOL=true — set sockopt.acceptProxyProtocol on VLESS/etc.
#           Use with nginx stream `proxy_protocol on;` so Xray sees the real client IP.
# Optional: XRAY_TCP_KEEPALIVE_IDLE / XRAY_TCP_KEEPALIVE_INTERVAL (seconds, default 60/15; 0 0 disables)
# Optional: XRAY_XHTTP_ENABLED=true — extra VLESS inbound over xHTTP on XRAY_XHTTP_PORT
#           (own TLS, reached directly, no nginx and no PROXY protocol). See apply_xhttp_inbound.

set -euo pipefail

is_truthy() {
  case "${1:-}" in
    1|true|TRUE|yes|YES|on|ON) return 0 ;;
    *) return 1 ;;
  esac
}

# entrypoint.sh runs `xray run -test` under `set -e`, so an invalid config means the container never
# starts. Optional patches must verify their own output and roll back instead of shipping it.
validate_config() {
  local out
  if command -v xray >/dev/null 2>&1; then
    if out=$(xray run -test -config "$CONFIG_PATH" 2>&1); then
      return 0
    fi
    echo "$out" >&2
    return 1
  fi
  # No xray binary (dev machine, verify-installer.sh) — structural check only.
  jq -e 'type == "object" and (.inbounds | type == "array")' "$CONFIG_PATH" >/dev/null 2>&1
}

# After templates / external copy: enable PROXY protocol accept when env is set.
apply_accept_proxy_protocol() {
  if ! is_truthy "${XRAY_ACCEPT_PROXY_PROTOCOL:-}"; then
    return 0
  fi

  if ! command -v jq >/dev/null 2>&1; then
    echo "[xray-config] ERROR: XRAY_ACCEPT_PROXY_PROTOCOL is set but jq is missing." >&2
    exit 1
  fi

  echo "[xray-config] XRAY_ACCEPT_PROXY_PROTOCOL=true → sockopt.acceptProxyProtocol on proxy inbounds"
  local tmp="${CONFIG_PATH}.proxyprotocol.tmp"
  jq '
    (.inbounds[]?
      | select(.protocol == "vless" or .protocol == "vmess" or .protocol == "trojan")
      | .streamSettings) |=
        ((. // {network: "tcp"})
         | .sockopt = ((.sockopt // {}) + {acceptProxyProtocol: true}))
  ' "$CONFIG_PATH" >"$tmp"
  mv "$tmp" "$CONFIG_PATH"
}

# Xray-core disables TCP keepalive on listeners by default, and the VLESS inbound does not apply
# policy connIdle. A peer that vanishes without FIN (mobile handover, suspended laptop) therefore keeps
# its socket in ESTABLISHED forever, and since the online map is refcounted per connection with no TTL
# (app/stats/online_map.go), the user stays "online" in `statsonlineiplist` indefinitely.
apply_tcp_keepalive() {
  local idle="${XRAY_TCP_KEEPALIVE_IDLE:-60}"
  local interval="${XRAY_TCP_KEEPALIVE_INTERVAL:-15}"

  if ! [[ "$idle" =~ ^[0-9]+$ ]] || ! [[ "$interval" =~ ^[0-9]+$ ]]; then
    echo "[xray-config] ERROR: XRAY_TCP_KEEPALIVE_IDLE/XRAY_TCP_KEEPALIVE_INTERVAL must be non-negative integers (got '$idle'/'$interval')." >&2
    exit 1
  fi

  if [ "$idle" -eq 0 ] && [ "$interval" -eq 0 ]; then
    echo "[xray-config] TCP keepalive disabled — dead peers will keep their user online until the socket dies." >&2
    return 0
  fi

  if ! command -v jq >/dev/null 2>&1; then
    echo "[xray-config] WARNING: jq is missing, skipping sockopt.tcpKeepAlive* — stale online sessions will not be reaped." >&2
    return 0
  fi

  echo "[xray-config] sockopt keepalive: idle=${idle}s interval=${interval}s on proxy inbounds"
  local tmp="${CONFIG_PATH}.keepalive.tmp"
  jq --argjson idle "$idle" --argjson interval "$interval" '
    (.inbounds[]?
      | select(.protocol == "vless" or .protocol == "vmess" or .protocol == "trojan")
      | .streamSettings) |=
        ((. // {network: "tcp"})
         | .sockopt = ((.sockopt // {})
             + (if $idle > 0 then {tcpKeepAliveIdle: $idle} else {} end)
             + (if $interval > 0 then {tcpKeepAliveInterval: $interval} else {} end)))
  ' "$CONFIG_PATH" >"$tmp"
  mv "$tmp" "$CONFIG_PATH"
}

# Adds a second VLESS inbound over xHTTP next to the primary one, on its own port with its own TLS.
# Russian TSPU polices TLS *connections* on :443 and fingerprints steady tunnels, so this inbound
# exists to look like ordinary HTTP/2 request-response traffic on a non-443 port.
#
# Every failure here is non-fatal on purpose: the primary inbound must keep working even if the extra
# one cannot be built, and a config that does not validate is rolled back rather than handed to xray.
apply_xhttp_inbound() {
  is_truthy "${XRAY_XHTTP_ENABLED:-}" || return 0

  local port="${XRAY_XHTTP_PORT:-2053}"
  local path="${XRAY_XHTTP_PATH:-/api/v1/update}"
  local mode="${XRAY_XHTTP_MODE:-auto}"
  local tag="${XRAY_XHTTP_INBOUND_TAG:-vless-xhttp-in}"
  local cert="${XRAY_TLS_CERT_FILE:-}"
  local key="${XRAY_TLS_KEY_FILE:-}"

  local skip="[xray-config] WARNING: XRAY_XHTTP_ENABLED=true but the xHTTP inbound was skipped —"
  if ! command -v jq >/dev/null 2>&1; then
    echo "$skip jq is missing." >&2
    return 0
  fi
  if ! [[ "$port" =~ ^[0-9]+$ ]] || [ "$port" -lt 1 ] || [ "$port" -gt 65535 ]; then
    echo "$skip XRAY_XHTTP_PORT must be 1-65535 (got '$port')." >&2
    return 0
  fi
  if [ "$port" = "${PORT:-443}" ]; then
    echo "$skip XRAY_XHTTP_PORT ($port) collides with the primary inbound port." >&2
    return 0
  fi
  if [ -z "$cert" ] || [ -z "$key" ]; then
    echo "$skip XRAY_TLS_CERT_FILE / XRAY_TLS_KEY_FILE are required (xHTTP terminates TLS itself)." >&2
    return 0
  fi
  if [ ! -f "$cert" ] || [ ! -f "$key" ]; then
    echo "$skip certificate or key file is missing ($cert / $key)." >&2
    return 0
  fi
  if [[ "$path" != /* ]]; then
    echo "$skip XRAY_XHTTP_PATH must start with '/' (got '$path')." >&2
    return 0
  fi
  if jq -e --arg t "$tag" 'any(.inbounds[]?; .tag == $t)' "$CONFIG_PATH" >/dev/null 2>&1; then
    echo "[xray-config] xHTTP inbound '$tag' already present — leaving it as is."
    return 0
  fi

  echo "[xray-config] xHTTP inbound '$tag' on :$port (path=$path mode=$mode)"
  local backup="${CONFIG_PATH}.pre-xhttp"
  local tmp="${CONFIG_PATH}.xhttp.tmp"
  cp -f "$CONFIG_PATH" "$backup"

  if ! jq \
    --argjson port "$port" \
    --arg tag "$tag" \
    --arg path "$path" \
    --arg mode "$mode" \
    --arg cert "$cert" \
    --arg key "$key" \
    '.inbounds += [{
        listen: "0.0.0.0",
        port: $port,
        protocol: "vless",
        tag: $tag,
        settings: { clients: [], decryption: "none" },
        streamSettings: {
          network: "xhttp",
          security: "tls",
          tlsSettings: {
            alpn: ["h2", "http/1.1"],
            certificates: [ { certificateFile: $cert, keyFile: $key } ]
          },
          xhttpSettings: { path: $path, mode: $mode }
        },
        sniffing: { enabled: true, destOverride: ["http", "tls", "quic"] }
      }]' "$CONFIG_PATH" >"$tmp"; then
    echo "$skip jq patch failed; keeping the config without it." >&2
    rm -f "$tmp" "$backup"
    return 0
  fi
  mv "$tmp" "$CONFIG_PATH"

  if ! validate_config; then
    echo "$skip the patched config did not pass validation; rolled back to the working one." >&2
    mv -f "$backup" "$CONFIG_PATH"
    return 0
  fi

  rm -f "$backup"
}

write_plain() {
  cat <<EOF >"$CONFIG_PATH"
{
  "log": {
    "loglevel": "warning",
    "access": "$ACCESS_LOG",
    "error": "$ERROR_LOG"
  },
  "stats": {},
  "api": {
    "tag": "api",
    "services": ["HandlerService", "LoggerService", "StatsService"]
  },
  "policy": {
    "levels": {
      "0": {
        "statsUserUplink": true,
        "statsUserDownlink": true,
        "statsUserOnline": true
      }
    }
  },
  "inbounds": [
    {
      "listen": "$XRAY_MGMT_HOST",
      "port": $XRAY_MGMT_PORT,
      "protocol": "dokodemo-door",
      "settings": {
        "address": "$XRAY_MGMT_HOST"
      },
      "tag": "api"
    },
    {
      "listen": "0.0.0.0",
      "port": $PORT,
      "protocol": "vless",
      "tag": "$INBOUND_TAG",
      "settings": {
        "clients": [],
        "decryption": "none"
      },
      "streamSettings": {
        "network": "tcp",
        "security": "none"
      },
      "sniffing": {
        "enabled": true,
        "destOverride": ["http", "tls", "quic"]
      }
    }
  ],
  "outbounds": [
    {
      "protocol": "freedom",
      "tag": "direct"
    }
  ],
  "routing": {
    "domainStrategy": "AsIs",
    "rules": [
      {
        "type": "field",
        "inboundTag": ["api"],
        "outboundTag": "api"
      }
    ]
  },
  "dns": {
    "servers": ["$DNS1", "$DNS2"]
  }
}
EOF
}

write_tls() {
  local cert="${XRAY_TLS_CERT_FILE:?XRAY_TLS_CERT_FILE is required for tls mode}"
  local key="${XRAY_TLS_KEY_FILE:?XRAY_TLS_KEY_FILE is required for tls mode}"
  cat <<EOF >"$CONFIG_PATH"
{
  "log": {
    "loglevel": "warning",
    "access": "$ACCESS_LOG",
    "error": "$ERROR_LOG"
  },
  "stats": {},
  "api": {
    "tag": "api",
    "services": ["HandlerService", "LoggerService", "StatsService"]
  },
  "policy": {
    "levels": {
      "0": {
        "statsUserUplink": true,
        "statsUserDownlink": true,
        "statsUserOnline": true
      }
    }
  },
  "inbounds": [
    {
      "listen": "$XRAY_MGMT_HOST",
      "port": $XRAY_MGMT_PORT,
      "protocol": "dokodemo-door",
      "settings": {
        "address": "$XRAY_MGMT_HOST"
      },
      "tag": "api"
    },
    {
      "listen": "0.0.0.0",
      "port": $PORT,
      "protocol": "vless",
      "tag": "$INBOUND_TAG",
      "settings": {
        "clients": [],
        "decryption": "none"
      },
      "streamSettings": {
        "network": "tcp",
        "security": "tls",
        "tlsSettings": {
          "certificates": [
            {
              "certificateFile": "$cert",
              "keyFile": "$key"
            }
          ]
        }
      },
      "sniffing": {
        "enabled": true,
        "destOverride": ["http", "tls", "quic"]
      }
    }
  ],
  "outbounds": [
    {
      "protocol": "freedom",
      "tag": "direct"
    }
  ],
  "routing": {
    "domainStrategy": "AsIs",
    "rules": [
      {
        "type": "field",
        "inboundTag": ["api"],
        "outboundTag": "api"
      }
    ]
  },
  "dns": {
    "servers": ["$DNS1", "$DNS2"]
  }
}
EOF
}

write_reality() {
  local pk="${XRAY_REALITY_PRIVATE_KEY:?XRAY_REALITY_PRIVATE_KEY is required for reality mode}"
  local dest="${XRAY_REALITY_DEST:-www.microsoft.com:443}"
  local snames_json
  snames_json=$(echo "${XRAY_REALITY_SERVER_NAMES:-www.microsoft.com}" | jq -R -c 'split(",") | map(gsub("^ +| +$";"")) | map(select(length>0))')
  local sids_json
  sids_json=$(printf '%s' "${XRAY_REALITY_SHORT_IDS:-,}" | jq -R -c 'split(",")')

  jq -n \
    --arg access "$ACCESS_LOG" \
    --arg error "$ERROR_LOG" \
    --arg mhost "$XRAY_MGMT_HOST" \
    --argjson mport "$XRAY_MGMT_PORT" \
    --argjson port "$PORT" \
    --arg tag "$INBOUND_TAG" \
    --arg dns1 "$DNS1" \
    --arg dns2 "$DNS2" \
    --arg dest "$dest" \
    --arg pk "$pk" \
    --argjson snames "$snames_json" \
    --argjson sids "$sids_json" \
    '{
      log: { loglevel: "warning", access: $access, error: $error },
      stats: {},
      api: { tag: "api", services: ["HandlerService", "LoggerService", "StatsService"] },
      policy: {
        levels: {
          "0": {
            statsUserUplink: true,
            statsUserDownlink: true,
            statsUserOnline: true
          }
        }
      },
      inbounds: [
        {
          listen: $mhost,
          port: $mport,
          protocol: "dokodemo-door",
          settings: { address: $mhost },
          tag: "api"
        },
        {
          listen: "0.0.0.0",
          port: $port,
          protocol: "vless",
          tag: $tag,
          settings: { clients: [], decryption: "none" },
          streamSettings: {
            network: "tcp",
            security: "reality",
            realitySettings: {
              show: false,
              dest: $dest,
              xver: 0,
              serverNames: $snames,
              privateKey: $pk,
              shortIds: $sids
            }
          },
          sniffing: { enabled: true, destOverride: ["http", "tls", "quic"] }
        }
      ],
      outbounds: [ { protocol: "freedom", tag: "direct" } ],
      routing: {
        domainStrategy: "AsIs",
        rules: [
          { type: "field", inboundTag: ["api"], outboundTag: "api" }
        ]
      },
      dns: { servers: [$dns1, $dns2] }
    }' >"$CONFIG_PATH"
}

main() {
  if [ -n "${XRAY_EXTERNAL_CONFIG_PATH:-}" ] && [ -f "$XRAY_EXTERNAL_CONFIG_PATH" ]; then
    echo "[xray-config] Using external config: $XRAY_EXTERNAL_CONFIG_PATH"
    cp -f "$XRAY_EXTERNAL_CONFIG_PATH" "$CONFIG_PATH"
    # Built-in templates always set stats + policy; external configs often omit them → dashboard traffic stays 0.
    if command -v jq >/dev/null 2>&1 \
      && ! jq -e '.stats != null and .policy != null and (.policy.levels["0"].statsUserUplink == true) and (.policy.levels["0"].statsUserDownlink == true)' "$CONFIG_PATH" >/dev/null 2>&1; then
      echo "[xray-config] WARNING: external config is missing stats/policy user counters (see write_plain in this script: stats {}, policy.levels[\"0\"] statsUserUplink/Downlink/Online, api.services StatsService) — bytes in the UI may stay 0." >&2
    fi
    # xHTTP is added after the PROXY protocol patch on purpose: it is reached directly, not via nginx.
    apply_accept_proxy_protocol
    apply_xhttp_inbound
    apply_tcp_keepalive
    return 0
  fi

  local mode="${XRAY_TRANSPORT_MODE:-plain}"
  mode=$(echo "$mode" | tr '[:upper:]' '[:lower:]')

  echo "[xray-config] Transport mode: $mode -> $CONFIG_PATH"

  case "$mode" in
    plain|tcp|none)
      write_plain
      ;;
    tls)
      write_tls
      ;;
    reality)
      write_reality
      ;;
    *)
      echo "ERROR: Unknown XRAY_TRANSPORT_MODE=$mode (use plain, tls, or reality)" >&2
      exit 1
      ;;
  esac

  apply_accept_proxy_protocol
  apply_xhttp_inbound
  apply_tcp_keepalive
}

main "$@"
