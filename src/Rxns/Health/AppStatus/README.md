# AppStatus log surface (generic)

The rxns `AppStatus` server already accepts log + error events pushed by every rxns
"app" via `ReportStatus.Log` → `ReportsStatusEventsHub` / `ReportsStatusApiController`.
This document describes the additional **read** surface added for the rxns-support
portal (and any other client that wants to render a logs dashboard for a registered
app).

## Wire model

```
┌─────────────┐  ReportStatus.Log    ┌────────────────────────┐  IAppStatusLogReader   ┌─────────────────┐
│ Any rxns    │ ──────────────────▶  │ LocalAppStatusManager  │ ─────────────────────▶ │ Portal page     │
│ "app"       │  (SignalR/HTTP push) │  + InMemoryAppStatusStore                       │ (vanilla JS +   │
│ (system X)  │                      │  + LocalAppErrorManager │                       │  /api/appstatus │
└─────────────┘                      └────────────────────────┘                        │  + SignalR hub) │
                                                                                       └─────────────────┘
```

The reader is **generic** — it doesn't know what "app" is on the other end. Filter by
`SystemName` (the publisher's `IRxnAppInfo.Name`, or whatever string the publisher set
via `IReportStatusExtensions.FromMessage(msg, systemName)`) to scope to a single app.

Entries with null `SystemName` are returned only when the caller passes null too (so
older producers that never set the field keep working — they're the "unscoped" view).

## REST endpoints

Served under `/api/appstatus` by both `Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters.AppStatusLogController`
(MapControllers) and `Rxns.WebApi.MsWebApiAdapters.RxnsApiAdapters.AppStatusLogController`
(OWIN attribute routing).

| Endpoint | Query | Returns |
|---|---|---|
| `GET /api/appstatus/systems` | — | `string[]` — distinct SystemNames that have published log or error entries since process start |
| `GET /api/appstatus/log` | `systemName? level? since? skip? take?` | `AppStatusLogPage` — newest-first window over the in-memory log buffer |
| `GET /api/appstatus/errors` | `systemName? since? skip? take?` | `AppStatusLogPage` — union of (a) log entries with Level=Error/Fatal, (b) `GetOutstandingErrors` from `IAppErrorManager` |
| `GET /api/appstatus/stats` | `systemName?` | `AppStatusLogStats` — error/warning/info counts over 1h + 24h windows |

## SignalR hub (NET5-only)

`/appStatusLogHub` exposes a typed `IAppStatusLogClient` with one method:

```csharp
Task LogEntry(AppStatusLogEntry entry);
```

After connecting, the client calls **`Subscribe(systemName)`** to join the SignalR
group for that app. The hub's broadcaster subscribes once (per-process) to
`LocalAppStatusManager.Information` + `.Errors`, maps each `LogMessage<>` to a
`AppStatusLogEntry`, and pushes to the matching group only — so the portal isn't
flooded by reporters it doesn't care about.

`Unsubscribe(systemName)` leaves the group. Entries with no SystemName land in the
`_unscoped_` group; future "all systems" pages can opt in.

## How an app gets surfaced

Anything that publishes via `ReportStatus.Log` already lands in the central in-memory
log buffer. To make entries show up *for a particular system* in the portal, the
publisher needs to set `SystemName` on the emitted `SystemLogMeta`. Two ways:

1. **Direct** — the publisher constructs `SystemLogMeta` itself (using the new
   `FromMessage(msg, systemName)` overload in `IReportStatusExtensions`).
2. **Adapter** — an external app (e.g. a legacy app that doesn't know about rxns
   internally) ships a small adapter that subscribes to its own log stream and
   forwards via `ReportStatus.Log.OnInformation(reporterName, message)` /
   `OnError(reporterName, ex)`. The reporter name + `IRxnAppInfo.Name` give the
   server enough to attach a `SystemName` on receipt. A thin
   `YourApp.SupportPortalAdapter` project is the canonical shape.

## What the surface does *not* do

- No persistence beyond what `InMemoryAppStatusStore.GetLog()` already keeps
  (`CircularBuffer<object>` capped at 3500 entries).
- No filtering by tenant — that's `IAppErrorManager.GetOutstandingErrors`'s job.
- No write API. Portals read; publishers push through the existing event-publish wire.
- No auth on the controllers beyond what the host already enforces; mount under a
  `[Authorize]` filter on the host side if you need it.
