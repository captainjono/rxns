# `support/` — diagnostic surfaces for the AppStatus portal

This module hosts UIs that consume the AppStatus host's support APIs:

- `support.claudeChat` — diagnostic chat against `/api/claude/info` + `/api/claude/chat`.
- `support.appInsights` — KQL browser against `/api/appinsights/info` + `/api/appinsights/query`.

A third related surface, **Insights Logs** (`supportInsights`), lives under
`systemstatus/partial/supportInsights/` because it extends the SignalR
`/appStatusLogHub` already attached to the systemstatus module. Same
backend domain, different host module.

## Augmentation pattern

The portal bundles every `.html` file under the top-level module folders
into the Angular `$templateCache` via `build.mjs:findPartials()`. To add
a new diagnostic surface:

1. **Pick a module.** If the surface extends an existing one (e.g. another
   systemstatus hub), drop the partial into that module's `partial/` folder.
   If it is its own concern, add a new module here under `support/partial/`.

2. **Lay down four files** in the partial folder (e.g. `support/partial/foo/`):
   - `foo.js` — `angular.module('support').controller('FooCtrl', ...)`
   - `foo.html` — `<div ng-controller="FooCtrl">...</div>`
   - `foo.less` — module-scoped LESS, imported from `support.less`
   - (optional) `foo-spec.js` — Karma tests; the rest of the portal has
     a smattering of these next to controllers.

3. **Register the state** in `support/support.js`:
   ```js
   $stateProvider.state('foo', {
       url: '/foo',
       templateUrl: 'support/partial/foo/foo.html'
   });
   ```

4. **Add `<script>` tags** in `index.html` next to the other `support/`
   entries so the dev (non-bundled) page wires it up; the `build.mjs`
   bundle picks them up automatically.

5. **Import the LESS** from `support/support.less`.

## Backend endpoints used here

| Surface | Endpoint(s) | Notes |
|---|---|---|
| `supportInsights` | `GET /api/appstatus/systems`, `GET /api/appstatus/stats`, `GET /api/appstatus/log`, SignalR hub `/appStatusLogHub` | Hub method `Subscribe(systemName)` joins a group; emits `LogEntry`. |
| `claudeChat` | `GET /api/claude/info`, `POST /api/claude/chat` | `ResolveTools: true` makes the server run the tool round-trip; we render tool calls + tool results inline. |
| `appInsights` | `GET /api/appinsights/info`, `POST /api/appinsights/query` | Body: `{Target, PresetName|Kql, Offset, MaxRows}`. |

All three are read-only by design; the Claude chat shows the server's
`readOnly` flag as a disabled toggle to make that contract visible.

## App-specific clone path

If a downstream system wants its own diagnostic page — same shape, but
hitting a different backend — clone one of these partials and rename
the controller + state. The HTML/LESS structure carries over verbatim;
swap the `$http.get('/api/...')` URLs and you have a custom surface
in five minutes.
