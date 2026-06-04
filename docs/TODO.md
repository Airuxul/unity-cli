# TODO — `unity-cli` (connector + CLI)

**Last Updated:** 2026-06-03 · **Owner:** package maintainers · **Scope:** automation stack follow-ups (English)

> **User doc (Chinese):** [../TODO.zh-CN.md](../TODO.zh-CN.md)

> **Submodule layout:** `com.air.unity-connector` (UPM) + `unity-cmd` (Node CLI).  
> **Out of scope:** `GameRuntime` composition, product UI, duplicate HTTP in game-core.  
> **Meta rollup:** [AirUnityPackage `docs/TODO_ROADMAP.md`](https://github.com/Airuxul/AirUnityPackage/blob/main/docs/TODO_ROADMAP.md)

---

## `com.air.unity-connector` (CONN-)

### Capability baseline

- Loopback HTTP: `editor` :6547, `editor_play` :6794, `player` :6795
- `POST /list`, `POST /command`, `GET /commands/{id}`, `GET /health`
- `InvokeCatalog`, main-thread scheduler, job ledger across domain reload
- `CliCommand` discovery (not game-core `ICommand`)

### TODO

| ID | Pri | Title | Description |
|----|-----|-------|-------------|
| CONN-01 | P0 | Fix `profiler` arg casts | `--frames` / `--from`–`--to` documented known bug. |
| CONN-02 | P1 | README version sync | README 1.1.0 vs `package.json` 2.3.0. |
| CONN-03 | P1 | `UNITY_CMD_TOKEN` hardening | Header validation + consistent failure JSON. |
| CONN-04 | P1 | Post-reload job audit | Orphaned jobs / `EditorJobLedger` without redispatch. See `docs/EDITOR_SERVER_RELIABILITY.zh-CN.md` §4.3, §6 P1. |
| CONN-07 | P0 | ~~Editor Server Supervisor~~ | **Done:** `EditorServerSupervisor` + thin `EditorConnectorBootstrap`. §8 integration tests still pending. |
| CONN-08 | P2 | ~~editor-http schema v2~~ | **Done (CLI):** `wait`/`resolveTarget` use instances SSOT; `supervisor_phase`/`http_status` on heartbeat; `editor-http.json` debug-only. |
| CONN-05 | P2 | `ConnectorBuild.Id` pairing | Bump with CLI `MIN_CONNECTOR_BUILD`. |
| CONN-06 | P2 | Release player HTTP strip | Confirm `DEVELOPMENT_BUILD` removes :6795 in Release. |

### Do not assign here

| Topic | Owner |
|-------|--------|
| Node profiles, catalog cache, argv | `unity-cmd` |
| `GameRuntime`, entities, UI | `com.air.unity-game-core` / `com.air.unity-ui` |
| GoF undo commands | `com.air.game-core` |

---

## `unity-cmd` (CMD-)

### Capability baseline

- Profiles under `~/.unity-cmd/profiles/`
- Catalog cache from `POST /list`; invalidate on `/health` versions
- Editor readiness: `instances/*.json` SSOT + `/health` confirm (see `EDITOR_SERVER_RELIABILITY.zh-CN.md`)
- Integration scenarios: editor lifecycle, compile recovery, player runtime

### TODO

| ID | Pri | Title | Description |
|----|-----|-------|-------------|
| CMD-01 | P0 | Submodule completeness | Ensure `src/`, `bin/`, unit tests present in sparse checkouts. |
| CMD-02 | P1 | Integration CI cleanup | `compile-error-recovery` temp `.cs` pre-run cleanup. |
| CMD-03 | P1 | CI integration matrix | `test:integration:all` when Editor + dev player available. |
| CMD-04 | P1 | Build ID lockstep | `MIN_CONNECTOR_BUILD` ↔ `ConnectorBuild.Id` on protocol changes. |
| CMD-05 | P2 | LAN profile docs | `UNITY_CMD_BIND=lan` + remote `editor_play` automation. |
| CMD-06 | P2 | Unit tests (no Unity) | Catalog expiry, scope mismatch, `fetchCommandStatus`. |

### Do not assign here

| Topic | Owner |
|-------|--------|
| `HttpListener`, invoke handlers, completion policies | `com.air.unity-connector` |
| Unity main-thread API execution | Connector scheduler |
