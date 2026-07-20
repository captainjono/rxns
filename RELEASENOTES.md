# Release notes

## 4.0.0

### New (BETA)

- **`Rxns.AppStatus.Host`** — embeddable AppStatus support portal any rxns app can
  boot on its own port in-process (`AppStatusPortal.StartAsync`). Bundles the
  log/error portal, the **AI bubble** (pluggable chat engines + read-only tools),
  an AppInsights KQL browser, a monitor service, and SSDP portal discovery.
  Shipped as **beta** — see `Rxns.AppStatus.Host/PLAN.md` for what works today vs
  what is in-progress and known gaps. Also introduces `Rxns.AppStatus.Host.Launcher`
  (standalone runner) and `Rxns.AppStatus.Host.Tests`.
- **`Rxns.Ai`** (in `src/Rxns/Ai`) — engine/tool contracts (`IAiChatEngine`,
  `IAiEmbeddingsEngine`, `IAiToolHandler`) consumed by the AppStatus AI bubble.
- **`src/Rxns/AppInsights/IAppInsightsBrowser`** — AppInsights browser contract
  (az-CLI implementation lives in the host).
- **`src/Rxns/Hosting/Shell`** — remote-shell backend (`RemoteShellCmd`,
  `PersistentShell`, `LocalShellHandler`) driving the portal's remote-shell view (experimental).
- **`Rxns.WebApi`** — `AppStatusLogController` provides OWIN/.NET-Framework parity
  for the AppStatus REST surface (REST polling; SignalR remains NET5+ only).

### Breaking changes

- `WebApiCfg.Port` changed from `string` to `int`.
- AppStatus module split: `AppStatusCoreModule` → `AppStatusServerCoreModule`
  (server-side registrations moved out of the shared core module).
- `net10` multi-target across the coordinated package line.

### Coordinated package versions

`Rxns`, `Rxns.WebApiNET5`, `Rxns.Redis`, `Rxns.Autofac`, `Rxns.Windows`,
`Rxns.Azure`, `Rxns.WebApi`, and the new `Rxns.AppStatus.Host` all move to
**4.0.0** (`Rxns.WebApi` was previously 2.0.0; realigned to the shared line).
