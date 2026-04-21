#!/bin/bash
# Renders XRay config.json from XRAY_TRANSPORT_MODE: plain | tls | reality
# Env: CONFIG_PATH, ACCESS_LOG, ERROR_LOG, PORT, DNS1, DNS2,
#      XRAY_MGMT_HOST, XRAY_MGMT_PORT, INBOUND_TAG
# Optional: XRAY_EXTERNAL_CONFIG_PATH (copy this file and skip generation)

set -euo pipefail

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
}

main "$@"
