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

Key env (see compose): `XRayManagement__Host`, `XRayManagement__Port`, `Backend__BaseUrl`, `XRAY_TRANSPORT_MODE` (`plain` / `tls` / `reality`).

## License

MIT

## Author

**Ivan Kolganov**

| Contact | Link |
|---------|------|
| <img src="https://api.iconify.design/simple-icons/linkedin.svg?color=%230A66C2" width="16" height="16" alt="" /> **LinkedIn** | [linkedin.com/in/imkolganov](https://www.linkedin.com/in/imkolganov/?locale=en) |
| <img src="https://cdn.simpleicons.org/telegram/26A5E4" width="16" height="16" alt="" /> **Telegram** | [@KolganovIvan](https://t.me/KolganovIvan) |
| <img src="https://cdn.simpleicons.org/buymeacoffee/FFDD00" width="16" height="16" alt="" /> **Buy Me a Coffee** | [buymeacoffee.com/imkolganov](https://buymeacoffee.com/imkolganov) |
