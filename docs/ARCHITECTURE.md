# unity-cli — Architecture

## Purpose

This monorepo connects a **Node CLI** (`unity-cmd`) to a **Unity UPM package** (`com.air.unity-connector`) over loopback HTTP. Unity owns the command catalog; the CLI resolves names and timeouts. Commands complete in a single `POST /command` (CONN-10).

## Editor HTTP reliability

For domain reload, port reuse, job ledger, and CLI `wait`/`diagnostics` contract, see **[EDITOR_SERVER_RELIABILITY.zh-CN.md](EDITOR_SERVER_RELIABILITY.zh-CN.md)** (design target: unified Editor Server Supervisor).

## Repository layout

```text
unity-cli/
├── README.md
├── README.zh-CN.md
├── docs/
│   ├── AGENTS.md
│   ├── ARCHITECTURE.md
│   ├── MAINTENANCE.md
│   └── README.md
├── unity-cmd/
│   ├── README.md
│   ├── README.zh-CN.md
│   └── docs/IMPLEMENTATION.md
└── com.air.unity-connector/
    ├── README.md
    ├── README.zh-CN.md
    └── docs/IMPLEMENTATION.md
```

## Design goals (implemented)

| Goal | Implementation |
|------|----------------|
| Declarative commands | `CliCommand` / `CliCommand<T>` + `InvokeDescriptor` + `Run(...)` |
| Typed CLI parameters | `[CliParam]` on param-class properties; bound via Newtonsoft.Json (`CliParamBinder`) |
| Discovery | `CliCommandDiscovery` instantiates commands; `FindForHost` resolves same-name handlers by scope |
| Shared HTTP | `ConnectorHttpEndpoint`, `ConnectorRequestDispatcher`, `JsonSerialization`, `InvokePipeline` |
| Unified command pipeline | `IInvokeHost` → `EditorInvokeHost` / `PlayModeInvokeHost`; `InvokePipeline` + `InvokeExecutor` |
| Job state | `JobStateCore` tick loop; `InvokeJobNotifier`; Editor policies in `Editor/Completion/` |
| Completion metadata | `InvokeCompletionCatalog` maps command → completion kind |
| Unity-owned catalog | `InvokeCatalog.BuildResponse()` → `POST /list` |
| CLI catalog cache | `unity-cmd/src/catalog.js` → `~/.unity-cmd/cache/`; invalidated by `connector_build` + `catalog_version` on `/health` |
| Help shows parameters | `POST /list` → `commands[].params[]`; `unity-cmd help` always refetches live catalog |
| No hardcoded CLI command table | Aliases from Unity `POST /list` only |
| Editor tools | console, menu, screenshot, exec, profiler, manage, reserialize |
| Agent-friendly CLI | `error_code`, `hint`, dynamic `help`, catalog invalidation on `connector_build` |

## End-to-end request flow

```text
unity-cmd argv
  → cli.js (parseArgs, runCommand)
       → remote-command.js
            ping | list | help     → local (list still calls Unity for catalog)
            other command          → loadCatalog (POST /list, file cache)
                                 → resolveRemoteCommand (alias, timeout, retry)
                                 → POST /command
  → Unity ConnectorRequestDispatcher
       → IInvokeHost.HandleCommand
            → InvokePipeline + PendingHttpResponses (CONN-10)
                 200 + data     sync (Editor + play; HTTP held until job done)
  → print JSON, exit 0/1
```

## Instance selection

1. **Profile file:** `~/.unity-cmd/profiles/{name}.json` — `host`, `port`, `connector_host`.
2. CLI: `--profile <name>` or `UNITY_CMD_PROFILE`.
3. Create: `unity-cmd profile create editor --host 127.0.0.1 --port 6547 --host-kind editor`.

No env-based host auto-discovery; each HTTP endpoint is a named profile.

## Command scopes (per host)

| Host (`connector_host`) | `editor` scope | `runtime` scope | `any` scope |
|-------------------------|----------------|-----------------|-------------|
| `editor` (:6547) | Yes | No | Yes |
| `editor_play` (:6794) | No | Yes | Yes |
| `player` (:6795) | No | Yes | Yes |

`CommandHostScope.Runtime` on `Descriptor` is the **command scope**, not a profile name. Runtime commands go to `editor_play` or `player` profiles.

## Runtime / Play-mode HTTP

### Three endpoints (one machine)

| `GET /health` → `host` | Port | When listening | Assembly / bootstrap |
|------------------------|------|----------------|----------------------|
| `editor` | 6547 | Editor open (always in Edit Mode) | `EditorConnectorServer` / `EditorConnectorBootstrap` |
| `editor_play` | 6794 | Editor **Play Mode** only | `EditorPlayHttpBootstrap` → `EditorPlayHttpHost` |
| `player` | 6795 | **Development Build** player running | `DevPlayerBootstrap` → `PlayerHttpHost` |

All three may be up at once (e.g. Editor + Play + local Dev build). Ports are fixed defaults; override with `UNITY_CMD_PORT`, `UNITY_CMD_EDITOR_PLAY_PORT`, `UNITY_CMD_PLAYER_PORT` on the **Unity** process. CLI uses saved profiles (`unity-cmd profile create`).

```text
                    ┌─────────────────────────────────────┐
  unity-cmd         │  Editor :6547 (EditorInvokeHost)   │  profile editor
  --profile ───────?│  editor_play :6794 (PlayMode…)      │  profile editor-play
                    │  player :6795 (PlayMode…)           │  profile package-play
                    └─────────────────────────────────────┘
                              │
                    ConnectorHttpEndpoint
                    → ConnectorMainThreadScheduler (single-flight)
                    → PlayModeInvokeHost → InvokePipeline
```

**During Editor Play:** use profile `editor-play` for `echo` and in-play profiler. Use profile `editor` for `play` / `stop` / `compile`, screenshots, and edit-mode profiler.

**Dev player:** profile `package-play`. Release builds exclude `UnityCliConnector.Dev` — no player HTTP.

### Long-running commands and catalog

- **CONN-10:** `compile` / `play` / `stop` hold the POST connection; `PendingHttpResponses` completes on the main thread.
- **GET /commands/{id}:** read-only job ledger (domain reload); not used to complete POST.
- Editor: `EditorJobStateManager` + completion policies. Play: `RuntimeJobStateManager` + `RuntimeJobStore`.
- **Sync commands** on play hosts run on the play main thread via `PlayConnectorServer` (`BridgeDriver`).
- **Catalog** (`POST /list`): Same discovery on all hosts; player lists only commands from assemblies shipped in that build (Runtime builtins, not full Editor builtins).

### Extending Runtime behavior

```csharp
using Air.UnityConnector.Cli;
using Air.UnityConnector.Invoke;
using Air.UnityConnector.Params;

public sealed class MyPlayToolParams
{
    [CliParam(Description = "optional label")]
    public string Label { get; set; }
}

public sealed class MyPlayToolCommand : CliCommand<MyPlayToolParams>
{
    public override InvokeDescriptor Descriptor { get; } = new InvokeDescriptor<MyPlayToolParams>(
        "my.play.tool", CommandHostScope.Runtime, "...");

    public override void Run(MyPlayToolParams p) =>
        CompleteSuccess(new { label = p.Label });
}
```

Test with `echo`, then `unity-cmd --profile editor compile`. Call from CLI with `editor-play` or `package-play` while Play / player is active.

### CLI commands (per instance)

Copy-paste examples (Windows `cmd`):

```bat
unity-cmd --profile editor ping
unity-cmd --profile editor compile
unity-cmd --profile editor play

unity-cmd --profile editor-play echo
unity-cmd --profile editor screenshot --view game --output_path Screenshots/play.png

unity-cmd --profile package-play ping
unity-cmd --profile package-play echo
```

See [../README.md#commands-per-instance](../README.md#commands-per-instance) for the full command list per host.

## Editor readiness (domain reload)

Before `POST /command` on the `editor` profile, `unity-cmd` waits until:

1. `~/.unity-cmd/instances/{hash}.json` (SSOT) — `connector_state`, `commands_ready`, `listener_running`, `supervisor_phase`, `compile_errors`, stable `generation`
2. `GET /health` confirms `commands_ready`, `session_id`, `generation`

`EditorConnectorBootstrap` stops the listener on `beforeAssemblyReload`, flushes deferred jobs to `EditorJobLedger`, restarts on `afterAssemblyReload`, and keeps instance heartbeat in `reloading` until catalog warmup completes. `GET /commands/{id}` can resolve pending jobs from the ledger after reload; handlers are not redispatched.

Integration tests use `waitForProfileReady()` (not health-only polling).

## Catalog contract (`POST /list`)

Response fields used by CLI:

- `catalog_version` — 12-char hash of command metadata (also on `GET /health` for cache validation).
- `commands[]` — `name`, `scope`, `description`, `completion`, `aliases`, `default_timeout_ms`, `allow_connection_retry`, **`params`** (help lines, e.g. `--lines <int> max entries`).
- `alias_to_command` — map CLI alias → canonical `name`.

`unity-cmd list` prints this JSON. `unity-cmd help` formats commands and indented `params` for humans (live fetch, not stale cache).

## Completion kinds

| `completion` | Completes when |
|--------------|----------------|
| `compilation` | `!EditorApplication.isCompiling` (Editor host) |
| `enter_play` | `EditorApplication.isPlaying` (Editor host) |
| `exit_play` | `!EditorApplication.isPlaying` (Editor host) |
| `started` | Command completes explicitly via `CliCommand.CompleteSuccess/CompleteFail` |

Editor command status survives in `SessionState` across domain reload where applicable; runtime command status persists per host in `PlayerPrefs`. Handlers are **not** redispatched after reload.

## Assemblies (connector)

| Assembly | Role |
|----------|------|
| **Runtime** (`UnityCliConnector.Runtime`) | Protocol (`Host`/`Http`/`Invoke`/`Job`), connector HTTP stack, `CliCommandDiscovery`, play hosts, player bootstrap (`DEVELOPMENT_BUILD`) |
| **Editor** (`UnityCliConnector.Editor`) | Editor HTTP, deferred completion policies, Editor builtins, `EditorPlayHttpBootstrap` |

## Extending

1. Add param class with `[CliParam]` properties (optional key → camelCase property name).
2. Add command class: inherit `CliCommand` or `CliCommand<T>`, and expose `Descriptor`.
3. For deferred commands: `DeferredInvokeDescriptor` with `Completion` from `InvokeCompletionCatalog` constants.
4. Bump `ConnectorBuild.Id`, then `unity-cmd --profile editor compile` or `refresh --compile true`.
5. Change `unity-cmd` only for new **local meta** commands (`help`, `profile` are local; `ping`/`list` need a profile).

## Known limitations

- **Editor host (:6547) during Play:** `editor`-scoped tools (`profiler`, `screenshot`, `state`, …) remain on profile **`editor`**. Use **`editor-play`** for `echo` and other runtime-scoped commands.
- **Player host:** No Editor APIs; Editor-scoped builtins are not listed on `player` unless you add Runtime/Any handlers in shipped assemblies.
- **Integration tests** do not launch Unity; they attach to an already-open Editor (and optionally a Dev player for `player-runtime`).
