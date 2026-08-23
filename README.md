<h1 align="left">
  <img src="https://raw.githubusercontent.com/IMKolganov/DataGateMonitorFrontend/main/public/favicon.svg" width="32" height="32" alt="" />
  DataGate Monitor — Xray sidecar
</h1>

Xray proxy sidecar and **DataGateXRayManager** API for the [DataGateMonitor](https://github.com/IMKolganov/DataGateMonitor) stack.

Submodule path: `xray/` in the monorepo. Standalone repo: [DataGateXRayManager](https://github.com/IMKolganov/DataGateXRayManager).

## Links

| Resource | Link |
|----------|------|
| <img src="https://raw.githubusercontent.com/IMKolganov/DataGateMonitorFrontend/main/public/favicon.svg" width="16" height="16" alt="" /> **DataGate** | [datagateapp.com](https://datagateapp.com/) |
| <img src="https://cdn.simpleicons.org/googleplay/414141" width="16" height="16" alt="" /> **Download** | [datagateapp.com/download](https://datagateapp.com/download) |
| <img src="https://cdn.simpleicons.org/grafana/F46800" width="16" height="16" alt="" /> **Dashboard** | [dash.datagateapp.com](https://dash.datagateapp.com/) |
| <img src="https://cdn.simpleicons.org/telegram/26A5E4" width="16" height="16" alt="" /> **Telegram channel** | [@datagateapp](https://t.me/datagateapp) |

## Role in the stack

- Runs Xray (VLESS / REALITY / TLS modes via `XRAY_TRANSPORT_MODE`)
- Exposes management API consumed by the backend
- Persists config under Docker volume `xray_data`

## Docker (monorepo)

From the monorepo root:

```bash
docker compose -f docker-compose-local.yml --env-file .env.dev.x64 up -d --build xray
```

Image: `imkolganov/datagate-monitor-xray`.

Key env (see compose / `.env.example`): `XRayManagement__Host`, `XRayManagement__Port`, `Backend__BaseUrl`, `XRAY_TRANSPORT_MODE` (`plain` / `tls` / `reality`), `XRAY_ACCEPT_PROXY_PROTOCOL` (`true` when nginx stream uses `proxy_protocol on;`).

### Pi-hole per-user DNS (identity IP)

Same idea as OpenVPN VirtualAddress → CN:

1. Set `DNS1`/`DNS2` to Pi-hole (reachable from the container).
2. `XRAY_DNS_IDENTITY_ENABLED=true`, subnet `10.80.0.0/24` (default), container needs `NET_ADMIN`.
   On a **shared** Pi-hole with multiple Xray nodes, give each node a **non-overlapping** subnet (e.g. `10.80.1.0/24`, `10.80.2.0/24`) and matching dashboard `ClientSubnetPrefix`.
3. Dashboard Pi-hole subnet prefix: `10.80.0.` (or the node-specific prefix).
4. Client VPN DNS must be that Pi-hole address through the VLESS tunnel (VLESS does not push dhcp-option DNS).
   Source of truth for apps: `GET /api/info` → `Config.ClientDnsServers` / `Config.DnsIdentityEnabled`, and link placeholders
   `{{dns_servers_json}}`, `{{dns1}}`, `{{dns2}}`, `{{dns_identity_enabled}}` (recommended profile template):
   `{"vless":"{{vless_uri}}","dnsServers":{{dns_servers_json}},"dnsIdentityEnabled":{{dns_identity_enabled}},"friendlyName":"{{friendly_name}}","uuid":"{{uuid}}","endpoint":"{{server_ip}}:{{server_port}}"}`
5. Pi-hole must be able to **reply** to sources in `10.80.0.0/24` (same L2 as the Xray container aliases, or an explicit route). Otherwise DNS blackholes after `sendThrough`.
   After Xray recreate the host route is lost — restore, e.g. `ip route replace 10.80.0.0/24 via <xray-container-ip> dev <docker-bridge>`.
   Pi-hole FTL must **listen** on the bridge IP (`listen-address=172.20.0.1` in `dnsmasq.d`, with `misc.etc_dnsmasq_d=true`). UFW: allow `:53`/`:8080` on that bridge (iface names like `br-bf6a6b3f3bed` change if the network is recreated).
6. Only classic DNS on **port 53** (tcp/udp) gets the per-user identity IP. DoH/DoT bypass this path.
   Dashboard `/api/info` with JWT is **not** a public health check; probe from the node `127.0.0.1` or send Bearer. Nginx `:9443` may `allow` only dashboard IPs (403 from elsewhere).

Manager allocates `IdentityIp` per client, adds iface aliases + Xray `sendThrough` rules for port 53, and enriches Pi-hole queries `ClientIp` → CommonName.

## License

MIT

## Author

**Ivan Kolganov**

| Contact | Link |
|---------|------|
| <img src="https://api.iconify.design/simple-icons/linkedin.svg?color=%230A66C2" width="16" height="16" alt="" /> **LinkedIn** | [linkedin.com/in/imkolganov](https://www.linkedin.com/in/imkolganov/?locale=en) |
| <img src="https://cdn.simpleicons.org/telegram/26A5E4" width="16" height="16" alt="" /> **Telegram** | [@KolganovIvan](https://t.me/KolganovIvan) |
| <img src="https://cdn.simpleicons.org/buymeacoffee/FFDD00" width="16" height="16" alt="" /> **Buy Me a Coffee** | [buymeacoffee.com/imkolganov](https://buymeacoffee.com/imkolganov) |
