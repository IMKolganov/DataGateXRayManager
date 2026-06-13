# DataGate Monitor — Xray sidecar

Xray proxy sidecar and **DataGateXRayManager** API for the [DataGateMonitor](https://github.com/IMKolganov/DataGateMonitor) stack.

Submodule path: `xray/` in the monorepo. Standalone repo: [DataGateXRayManager](https://github.com/IMKolganov/DataGateXRayManager).

## Links

- [DataGate](https://datagateapp.com/) · [Download](https://datagateapp.com/download)
- [Dashboard](https://dash.datagateapp.com/) · [Telegram @datagateapp](https://t.me/datagateapp)

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

**Ivan Kolganov** — [LinkedIn](https://www.linkedin.com/in/imkolganov/?locale=en) · [Telegram](https://t.me/KolganovIvan) · [Buy Me a Coffee](https://buymeacoffee.com/imkolganov)
