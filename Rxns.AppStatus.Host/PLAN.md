# Rxns.AppStatus.Host — Beta status & roadmap

**Ships in 4.0.0 as BETA.** The package version carries no `-beta` suffix; beta
status is recorded here and in the package release notes. The subsystem compiles
and the host boots, but several areas are still in-progress and the surface may
change before it is declared stable.

## What this subsystem is

An embeddable AppStatus support portal any rxns app can boot on its own port in
the same process (`AppStatusPortal.StartAsync`). It bundles four capabilities
behind one Kestrel binding:

- **Log/error portal** — live log + error stream over SignalR (`/appStatusLogHub`)
  and REST (`/api/appstatus/*`), backed by the in-process `LocalAppStatusManager`.
- **AI bubble** — pluggable chat backend (`/api/ai/*`) with one-round tool
  dispatch. Engines are declared in config or auto-added from env vars, and can
  be discovered on the LAN. Built-in read-only tools query logs, errors,
  AppInsights, and an indexed workspace.
- **AppInsights browser** — KQL over configured components via the `az` CLI
  (`/api/appinsights/*`).
- **Monitor + discovery** — a monitor service that watches sources and raises
  suggestions (`/api/monitor/*`), and SSDP peer discovery of other portals on the
  LAN plus operator-added custom peers (`/api/portals/*`).

## What works today

- `AppStatusPortal.StartAsync` / `Stop` facade; embeds alongside a host app in one process.
- `AppStatusHostApp` module wiring: `AppStatusClientModule` + `AppStatusServerModule` +
  `AiModule` + `AppInsightsModule` + `MonitorModule`, plus caller `IAppModule` augments (last wins).
- Log/error/stats portal REST + SignalR live push.
- AI chat with multiple engine kinds: Claude (API + CLI process), Ollama, Foundry,
  and any OpenAI-compatible endpoint; engine selection via config, env vars, or LAN scan.
- Read-only AI tools: query logs, query errors, query AppInsights, workspace
  search/read/list; workspace knowledge index (chunker + indexer + embeddings).
- AppInsights KQL browser over multi-select targets via `az` CLI.
- Monitor service with the built-in `BusLogSource` and pluggable `IMonitorSource` augments.
- SSDP portal discovery with a persisted custom-peer list.
- OWIN parity REST controller (`AppStatusLogController`) in `Rxns.WebApi` for
  legacy .NET Framework hosts (REST polling only — SignalR is NET5+ only).
- Unit tests (`Rxns.AppStatus.Host.Tests`) cover the AI engine scanner/config store,
  Foundry discovery, workspace indexer/scanner/tools, and host bootstrap.

## In-progress / pending

- **Monitor persistence** — V1 uses the in-memory tape repo from `DDDServerModule`;
  suggestions/trust list reset on host restart. Disk-backed `ITapeSource` is a
  drop-in override but not wired by default.
- **AI write tools** — the tool surface is read-only by default (`AI_READONLY`);
  write-tagged tools are stubbed/gated and not production-ready.
- **Claude CLI engine** (`ClaudeProcessAiEngine`) — designed and wired, less
  exercised than the API engine.
- **Remote shell** — `src/Rxns/Hosting/Shell/*` backend and the `remoteShell` web
  view are new and lightly tested; treat as experimental.
- **Config surface** — `appstatus.config` schema (Targets + Ai engines) may still
  shift; the `appstatus.local.config` overlay managed from the bubble Settings tab
  is new.

## Known gaps

- No auth on the portal endpoints — intended to bind to trusted/internal networks
  or sit behind an existing gateway.
- AppInsights browser shells out to `az`; the host must have the Azure CLI logged in.
- SSDP discovery depends on multicast being permitted on the LAN.
- Persistence for monitor state and discovered peers is file/in-memory only.

## How to run

Launch the standalone host:

```powershell
dotnet run --project Rxns.AppStatus.Host.Launcher
```

Or embed it in any rxns app (see [README.md](README.md) for the full wiring example):

```csharp
await AppStatusPortal.StartAsync(new AppStatusHostCfg
{
    BindingUrl = "http://*:5060",
    SystemName = "my-app"
});
```

Configuration lives in `appstatus.config` next to the host binary (see
[`appstatus.sample.config`](appstatus.sample.config)); env vars override the file.
