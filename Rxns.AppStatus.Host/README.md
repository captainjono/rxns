# Rxns.AppStatus.Host

Embeddable portal host. Any rxns app can boot the AppStatus support portal on its own port in the same process with a single call.

## Contract

```csharp
public static class AppStatusPortal
{
    public static Task StartAsync(AppStatusHostCfg cfg, params IAppModule[] augment);
    public static void Stop();
    public static string ResolveHtml5Root();
}
```

The host serves, on a single Kestrel binding:

- **`Rxns.AppSatus/Web/dist/`** — the Angular SPA produced by `build.mjs` (resolved via `AppStatusHostCfg.Html5Root`, or auto-probed via `ResolveHtml5Root()`).
- **REST** — `/api/appstatus/{systems,log,errors,stats}` (from `Rxns.WebApiNET5`).
- **SignalR** — `/appStatusLogHub` push channel for live log entries.
- **REST** — `/api/ai/{info,chat,...}` (this assembly) — pluggable AI chat backend (the "AI bubble") with one-round tool dispatch across log/error/AppInsights/workspace tools.
- **REST** — `/api/appinsights/{info,query}` (this assembly) — KQL browser over configured AppInsights components via `az` CLI.
- **REST** — `/api/monitor/...` (this assembly) — monitor service that watches bus/log sources and raises suggestions.
- **REST** — `/api/portals/...` (this assembly) — SSDP-based portal discovery: peer AppStatus hosts on the LAN plus operator-added custom peers.

All wired into the rxns container via `AppStatusServerModule` + `AiModule` + `AppInsightsModule` + `MonitorModule` (see `AppStatusHostApp`).

> **Status: BETA in 4.0.0.** See [PLAN.md](PLAN.md) for what works today, what is in-progress, and known gaps.

## Wiring example

In any rxns app's `Main` — run the existing app on its own port AND boot the portal alongside:

```csharp
public static async Task Main(string[] args)
{
    // (existing) your app's web on :5050
    _ = AspNetCoreWebApiAdapter.StartWebServices<MyApp>(appCfg, args);

    // AppStatus portal on :5060 in the same process
    await AppStatusPortal.StartAsync(new AppStatusHostCfg
    {
        BindingUrl = "http://*:5060",
        SystemName = "myapp"
    });
}
```

Both servers run in the same process — they share the in-memory AppStatus log/error buffer because `LocalAppStatusManager` is a static singleton inside `Rxns`. The portal sees everything the host app logs.

## `IAppModule` augmentation

`augment` is a `params` slot for layering app-specific registrations into the portal's container — e.g. extra Claude tools, custom `IAppInsightsBrowser`, or domain-aware health views. The host's own modules load first, then augments, so augments override default registrations.

```csharp
public class MyAppAugmentModule : IAppModule
{
    public IRxnLifecycle Load(IRxnLifecycle lifecycle) => lifecycle
        .CreatesOncePerAppAs<MyAppKqlTool, IClaudeToolHandler>();
}

await AppStatusPortal.StartAsync(cfg, new MyAppAugmentModule());
```

## Configuration

Primary source: a single `appstatus.config` JSON file next to the host binary (or under
the dir pointed to by `RXNS_APPSTATUS_CONFIG_DIR`). Follows a per-environment
config file pattern — a base file plus an optional
`appstatus.<env>.config` overlay when `RXNS_ENV` is set.

```jsonc
// appstatus.config
{
  "Targets": [
    // AppInsights instances — operator picks which to enable via the portal UI checkboxes;
    // multi-select flattens the same KQL across all enabled targets in one merged table.
    { "Name": "myapp-dev",     "SubscriptionId": "00000000-…", "ResourceGroup": "myapp-dev",     "AppName": "myapp-dev",     "DefaultEnabled": true  },
    { "Name": "myapp-prod",    "SubscriptionId": "00000000-…", "ResourceGroup": "myapp-prod",    "AppName": "myapp-prod",    "DefaultEnabled": false },
    { "Name": "myapp-staging", "SubscriptionId": "00000000-…", "ResourceGroup": "myapp-staging", "AppName": "myapp-staging", "DefaultEnabled": false }
  ],
  "Claude": {
    "ApiKey":   "sk-ant-…",          // omit to read from CLAUDE_API_KEY env var
    "ModelId":  "claude-haiku-4-5",
    "ReadOnly": true,
    "CliPath":  null                  // future: Rxn.Create(claude-cli, …) engine
  }
}
```

Env vars take precedence over the file (handy for k8s / CI without re-deploying the
cfg). Useful when one deployment hot-swaps an `ApiKey`:

| Variable | Purpose | Default |
| --- | --- | --- |
| `RXNS_APPSTATUS_CONFIG_DIR` | Dir containing `appstatus.config`. | Probe `AppContext.BaseDirectory`, then `./config`, then `..` |
| `RXNS_ENV` | Name for the `appstatus.<env>.config` overlay. | _(unset)_ |
| `CLAUDE_API_KEY` | Anthropic API key. When set, the SDK engine is selected. | _(unset → no Claude)_ |
| `CLAUDE_MODEL` | Override Claude model id. | `claude-haiku-4-5` |
| `CLAUDE_READONLY` | `"false"` to expose write-tagged tools. | `true` |
| `CLAUDE_CLI_PATH` | Path to `claude` CLI (designed wire only, not live yet). | _(unset)_ |

Embedding apps that prefer `appsettings.json`/Key Vault can register their own `ClaudeChatOptions` / `AppInsightsBrowserOptions` in an augment module — last registration wins.

## How a consumer app wires it

```csharp
// Program.cs — both ports in one process
var appTask = AspNetCoreWebApiAdapter
    .StartWebServices<MyApp>(appCfg, args);                  // :5050 — main app

var portalTask = AppStatusPortal.StartAsync(new AppStatusHostCfg
{
    BindingUrl = "http://*:5060",                            // :5060 — support portal
    SystemName = "myapp"
});

await Task.WhenAny(appTask, portalTask);
```

Set `SUPPORT_PORTAL_DISABLE=true` to skip the portal listener entirely
(e.g. in environments that already run a centralised portal).

## Files

- `AppStatusPortal.cs` — public facade (`StartAsync` / `Stop`).
- `AppStatusHostCfg.cs` — `IWebApiCfg` for the portal binding + `Html5Root` probe.
- `AppStatusHostApp.cs` — minimal `ConfigureAndStartAspnetCore` subclass — loads `AppStatusClientModule` + `AppStatusServerModule` + `AiModule` + `AppInsightsModule` + `MonitorModule` + caller augments.
- `Ai/` — `AiModule`, `AiChatController`, engine factory + registry (`AiChatEngineFactory`, `DynamicAiEngineRegistry`), engines (`ClaudeApiAiEngine`, `ClaudeProcessAiEngine`, `OllamaAiEngine`, `FoundryAiEngine`, `OpenAiCompatAiEngine`, `OpenAiCompatEmbeddingsEngine`), config store/scanner, LAN discovery (`Discovery/`), tools (`Tools/` — read-only log/error/AppInsights + workspace search/read/list), and workspace knowledge index (`Workspace/`).
- `AppInsights/` — `AppInsightsModule`, `AppInsightsController`, `AzCliAppInsightsBrowser` (implements `Rxns.AppInsights.IAppInsightsBrowser`).
- `Monitor/` — `MonitorModule`, `MonitorController`, `MonitorService`, `MonitorRoot` aggregate, and pluggable `IMonitorSource` impls (`Sources/BusLogSource`).
- `Discovery/` — `PortalsController`, `SsdpPeerCache` for cross-host portal discovery.

Companion source lives in `src/Rxns` (the framework package):

- `src/Rxns/Ai/` — engine/tool contracts (`IAiChatEngine`, `IAiEmbeddingsEngine`, `IAiToolHandler`).
- `src/Rxns/AppInsights/IAppInsightsBrowser.cs` — the browser contract this host implements.
- `src/Rxns/Hosting/Shell/` — remote-shell backend (`RemoteShellCmd`, `PersistentShell`, `LocalShellHandler`) driving the portal's `remoteShell` view.
