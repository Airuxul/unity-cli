# unity-cmd — Implementation

Version: 0.1.0

## Role

Thin HTTP client and argv front-end. No Unity business logic; all commands come from the connector catalog except local meta commands.

## Entry point

`bin/unity-cmd.js` → `src/cli.js` (`parseArgs`, `runCommand`) → `remote-command.js` / `profile.js` / `help.js`.

## Modules

| Path | Responsibility |
|------|----------------|
| `src/constants.js` | Ports, timeouts, protocol strings, profile help formatters |
| `src/runtime.js` | `resolveTimeoutMs`, `createCommandBudget`, `checkMinConnectorBuild` |
| `src/cli.js` | `parseArgs` (`--key=value`, `--timeout`, `--help`); command routing |
| `src/remote-command.js` | `ping`, `list`, remote command execution |
| `src/client/connection.js` | Profiles, `resolveTarget`, host-kind helpers |
| `src/client/instances-io.js` | Read `~/.unity-cmd/instances/*.json` (CLI readiness SSOT) |
| `src/client/connector-readiness.js` | `waitForConnectorReady`, `waitForProfileReady` |
| `src/client/instance-diagnostics.js` | `NO_INSTANCE` `diagnostics.reason` |
| `src/client/http.js` | `fetch` + `AbortSignal` timeout |
| `src/client/command.js` | `ping`, `fetchCatalog`, `sendCommand`, `fetchCommandStatus` (single GET) |
| `src/profile.js` | `profile list|show|create|set|delete` |
| `src/catalog.js` | Cache, alias resolve, scope checks, cache invalidation |
| `src/params.js` | Coerce flags for Unity JSON bodies |
| `src/errors.js` | `error_code`, `hint` on failures |
| `src/help.js` | Local / offline / live help |

## Local meta commands

| Command | Behavior |
|---------|----------|
| `help` | No profile → local usage; with profile → live catalog help (**always refetches** when online) |
| `profile` | Manage `~/.unity-cmd/profiles/*.json` |
| `ping` | `GET /health` (requires `--profile`) |
| `list` | `loadCatalog` JSON (requires `--profile`) |

Remote commands require `--profile <name>` or `UNITY_CMD_PROFILE`.

### `list` output schema

```json
{
  "ok": true,
  "profile": "editor",
  "catalog_version": "4a6528daab10",
  "connector_build": 40,
  "commands": [
    {
      "name": "console",
      "scope": "editor",
      "params": ["--type <string> [error|warning|log] ...", "--lines <int> max entries", "..."]
    }
  ],
  "alias_to_command": { "recompile": "compile" }
}
```

Flag: `--refresh-catalog` forces network fetch and rewrites cache file.

## Profiles

- Path: `~/.unity-cmd/profiles/{name}.json`
- Fields: `name`, `host`, `port`, `connector_host` (`editor` | `editor_play` | `player`), `updated_at`
- `profile create` pings `/health` by default (`--no-verify` to skip)
- `resolveTarget({ profile })` loads file, verifies `/health` `host` matches `connector_host`

Default ports: editor **6547**, editor_play **6794**, player **6795** (canonical source: `src/constants.js`; `doc:check` verifies README).

Integration mapping (`PROFILE_BY_HOST_KIND`): `editor_play` → profile `editor-play`, `player` → `package-play`.

**Minimum connector build:** `MIN_CONNECTOR_BUILD` in `constants.js` (must match `ConnectorBuild.Id`). Older builds are rejected with `CONNECTOR_OUTDATED`.

## Catalog cache

- Path: `~/.unity-cmd/cache/catalog-<host>:<port>.json` (host `:` → `_` in filename)
- Fields on disk: `updated_at`, `expires_at` (`updated_at` + 1 day), `host_kind`, `catalog_version`, `connector_build`, `commands`, `alias_to_command`
- Valid when: file exists, **not** `isCatalogExpired`, `ping` succeeds, `connector_build` / `catalog_version` match `/health`, `host_kind` matches profile
- `list` JSON includes `cache_path`, `updated_at`, `expires_at`, `catalog_expired`, `catalog_ttl_ms` for agents
- Aliases and `params` from Unity `POST /list` only
- `readCachedCatalog(target)` — shared disk read for offline help fallback

## Scope check (CLI)

`checkScopeCompatibility` mirrors Unity `CommandAvailability`:

- Play hosts (`editor_play`, `player`): `runtime` + `any` only
- Editor host: `editor` + `any` only

## Remote command dispatch

1. `requireProfile` → `createCommandBudget(20s)` → `resolveTarget` / `loadCatalog` share remaining budget
2. All host kinds: `waitForConnectorReady` before `sendCommand` (Play Mode is orthogonal to connector readiness)
3. `sendCommand` → `POST /command` (CONN-10: blocks until 200/4xx). HTTP 202 → error.

## Integration tests

| Scenario | Profile | File |
|----------|---------|------|
| `editor-lifecycle` | `editor` | `scenarios/editor-lifecycle.json` |
| `player-runtime` | `package-play` | `scenarios/player-runtime.json` |

Step types: `sleepMs`, `waitProfile`, `assertFile`, `expectFailure`, `expectCatalog`, `expectConnectorBuild` (see `MIN_CONNECTOR_BUILD` in `src/constants.js`).

`npm run test:integration` — needs profiles + running connector; skips if unreachable.
