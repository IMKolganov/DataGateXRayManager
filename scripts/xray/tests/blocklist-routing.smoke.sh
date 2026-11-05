#!/usr/bin/env bash
# Local smoke for apply_blocklist in render-config.sh (no production server).
# Renders inside the xray image so `xray run -test` validates the patched config, then checks that the
# API rule stays first, a re-run does not stack rules and a missing list leaves a working config.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
SCRIPT="${ROOT}/scripts/xray/render-config.sh"
IMAGE="${BLOCKLIST_SMOKE_IMAGE:-imkolganov/datagate-monitor-xray:latest}"

lists="$(mktemp -d)"
trap 'rm -rf "$lists"' EXIT
printf '# MAX\noneme.ru\nmax.ru\nmaxvd578.okcdn.ru\nnot a domain\n' >"$lists/domains"
printf '# MAX\n155.212.204.0/23 # api\n213.59.252.0/22\n' >"$lists/cidrs"

echo "[smoke] image=$IMAGE"
docker run --rm \
  -v "$SCRIPT:/t/render-config.sh:ro" -v "$lists:/bl:ro" \
  -e CONFIG_PATH=/tmp/config.json -e ACCESS_LOG=/tmp/access.log -e ERROR_LOG=/tmp/error.log \
  -e XRAY_MGMT_HOST=127.0.0.1 -e XRAY_MGMT_PORT=10085 -e INBOUND_TAG=vless-in \
  -e XRAY_TRANSPORT_MODE=plain -e PORT=443 -e DNS1=1.1.1.1 -e DNS2=1.1.1.1 \
  -e XRAY_BLOCKLIST_DOMAINS_FILE=/bl/domains -e XRAY_BLOCKLIST_CIDRS_FILE=/bl/cidrs \
  --entrypoint bash "$IMAGE" -c '
set -euo pipefail
fail() { echo "[smoke] FAIL: $*" >&2; exit 1; }

bash /t/render-config.sh >/dev/null
xray run -test -config /tmp/config.json >/dev/null || fail "patched config does not validate"
jq -e ".routing.rules[0].inboundTag == [\"api\"]" /tmp/config.json >/dev/null || fail "API rule is not first"
jq -e ".routing.rules[1] == {type: \"field\", domain: [\"domain:oneme.ru\", \"domain:max.ru\", \"domain:maxvd578.okcdn.ru\"], outboundTag: \"blocked\"}" \
  /tmp/config.json >/dev/null || fail "domain rule wrong: $(jq -c .routing.rules[1] /tmp/config.json)"
jq -e ".routing.rules[2].ip == [\"155.212.204.0/23\", \"213.59.252.0/22\"]" /tmp/config.json >/dev/null || fail "CIDR rule wrong"
jq -e "[.outbounds[] | select(.tag == \"blocked\" and .protocol == \"blackhole\")] | length == 1" /tmp/config.json >/dev/null \
  || fail "blackhole outbound missing"

cp /tmp/config.json /tmp/external.json
XRAY_EXTERNAL_CONFIG_PATH=/tmp/external.json bash /t/render-config.sh >/dev/null
jq -e "([.routing.rules[] | select(.outboundTag == \"blocked\")] | length) == 2 and ([.outbounds[] | select(.tag == \"blocked\")] | length) == 1" \
  /tmp/config.json >/dev/null || fail "re-run stacked block rules"

rm /tmp/config.json
XRAY_BLOCKLIST_DOMAINS_FILE=/missing XRAY_BLOCKLIST_CIDRS_FILE= bash /t/render-config.sh >/dev/null 2>&1
xray run -test -config /tmp/config.json >/dev/null || fail "missing list broke the config"
jq -e "[.outbounds[] | select(.tag == \"blocked\")] | length == 0" /tmp/config.json >/dev/null || fail "missing list still patched"
echo "[smoke] OK: blocklist routing"
'
