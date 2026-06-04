# unity-connector — Implementation

Version: 2.3.0

## Package

- **UPM name:** `com.air.unity-connector`
- **Version:** see `package.json` (synced with this doc `Version:` line for `doc:check`)

## Code organization

| Module | Location | Responsibility |
|--------|----------|----------------|
| Protocol | `Runtime/Host`, `Runtime/Http`, `Runtime/Invoke`, `Runtime/Job` | `HttpListenerHost`, `HostNetwork`, `InvokeCatalog`, `JobStateCore` (moved from game-core) |
| Connector HTTP | `Runtime/Connector/Http` | `ConnectorHttpEndpoint`, `ConnectorMainThreadScheduler`, routes |
| CLI commands | `Runtime/Cli`, `Editor/Commands` | `CliCommandDiscovery`, builtins, `InvokePipeline` |
| Play hosts | `Runtime/Http`, `Runtime/State`, `Runtime/Bootstrap` | `editor_play` / `player` HTTP on main thread |

Editor assembly adds deferred command dispatch, completion policies, builtins, and services (see folder layout below).

### Editor assembly folders (`UnityCliConnector.Editor`)

| Folder | Contents |
|--------|----------|
| `Http/` | `EditorConnectorServer`, `EditorConnectorBootstrap`, `EditorHttpLocalCache`, `EditorInstanceFile`, `EditorHttpSession`, `EditorPlayHttpBootstrap` |
| `Dispatch/` | `EditorInvokeHost`, `EditorJobStore`, `EditorJobStateManager` |
| `Commands/` | Builtin Editor commands (`play`, `compile`, `screenshot`, …) |
| `Completion/` | Deferred completion policies (`enter_play`, `exit_play`, `compilation`) |
| `Services/` | `EditorManageService`, `AssetRefreshService`, `UnityConsoleReader`, … |
| `State/` | `EditorStateProvider`, `EditorPlayState` |
| `Params/` | CLI parameter DTOs for Editor commands |
| `Infrastructure/` | `MainThread` |
| `Build/` | `ConnectorPlayerBuildGuard` |

### Runtime assembly folders (`UnityCliConnector.Runtime`)

| Folder | Contents |
|--------|----------|
| `Http/` | `PlayConnectorServer`, `PlayModeHttpHosts`, `PlayModeInvokeHost` |
| `Connector/Http/` | `ConnectorServerFactory`, `ConnectorServerCore`, `ConnectorMainThreadScheduler`, `ConnectorRequestDispatcher` |
| `Commands/` | Runtime builtins (`echo`, log helpers, tests) |
| `State/` | `RuntimeJobStateManager`, `RuntimeJobStore` |
| `Params/` | Runtime command parameter DTOs |
| `Bootstrap/` | `DevPlayerBootstrap` |
| `UI/` | `RuntimeCliConsole` (optional dev UI) |

## Assemblies

| Assembly | Platform | Contents |
|----------|----------|----------|
| `UnityCliConnector.Runtime` | Editor + Development Build (`UNITY_EDITOR \|\| DEVELOPMENT_BUILD`) | `Host` / `Http` / `Invoke` / `Job`, connector HTTP stack, `CliCommandDiscovery`, play hosts, `DevPlayerBootstrap` |
| `UnityCliConnector.Editor` | Editor | `EditorConnectorServer` / `EditorConnectorBootstrap`, `EditorPlayHttpBootstrap`, `EditorJobStateManager`, Editor builtins |

## HTTP API (Editor)

| Method | Path | Response |
|--------|------|----------|
| GET | `/health` | `{ ok, host, connector_build, catalog_version, bind_mode }` |
| POST | `/list` | `{ ok, catalog_version, commands[], alias_to_command }` — each command includes `params[]` help lines |
| POST | `/command` | `200` / `4xx` when connection closes (CONN-10 hold) |
| GET | `/commands/{id}` | `{ status, result?, error? }` |

| Host (`/health`) | When | Default port | Env override |
|------------------|------|--------------|--------------|
| `editor` | Editor Edit Mode | **6547** | `UNITY_CMD_PORT` |
| `editor_play` | Editor Play Mode | **6794** | `UNITY_CMD_EDITOR_PLAY_PORT` |
| `player` | Development Build player | **6795** | `UNITY_CMD_PLAYER_PORT` |

All three can run on one PC at the same time without port clashes. CLI: named profiles (`unity-cmd profile create`).

## Runtime / Play-mode stack

Play-mode HTTP reuses the same `ConnectorRequestDispatcher` and `HttpListenerHost` as the Editor host (`Runtime/Connector/Http` + `Runtime/Http`).

### Components

| Type | Role |
|------|------|
| `ConnectorHttpEndpoint` | Shared listener: `TryStart` / `Stop` / `TryProbeHealth`; used by Editor and play-mode servers |
| `ConnectorServerFactory` / `ConnectorServerCore` | Wires `IInvokeHost` + `ConnectorMainThreadScheduler` + `ConnectorHttpEndpoint` |
| `ConnectorMainThreadScheduler` | Single-flight `POST /command` (`503` busy); optional `503` reloading during domain reload |
| `MainThreadHttpWork` | Shared drain for `POST /command`, `POST /list`, `GET /commands/{id}` on main thread |
| `IConnectorServer` | Unified lifecycle (`Start` / `Stop` / `IsListening`) for Editor and Play |
| `PlayConnectorServer` | Play-mode server (`editor_play`, `player`); main-thread drain via `BridgeDriver` |
| `PlayModeHttpHosts.EditorPlayHttpHost` | `host=editor_play`, port `ResolveEditorPlayPort()` |
| `PlayModeHttpHosts.PlayerHttpHost` | `host=player`, port `ResolvePlayerPort()` |
| `PlayModeInvokeHost` | `IInvokeHost`; `/health` host string; routes via unified `InvokePipeline` |
| `EditorPlayHttpBootstrap` | `[InitializeOnLoad]` — start/stop `EditorPlayHttpHost` on Play Mode transitions |
| `DevPlayerBootstrap` | `[RuntimeInitializeOnLoadMethod]` — `PlayerHttpHost.Start()` in Development Build players |
| `ConnectorHttpLifecycle` | Shared start/stop for Editor and play-mode listeners |
| `EchoRuntimeCommand` | Builtin `echo`, `Scope = Runtime` — smoke test for play/player hosts |

### Lifecycle

**Editor HTTP (`editor`, :6547):**

1. `EditorConnectorServer` (`IConnectorServer`) owns the listener; `EditorConnectorBootstrap` handles start/stop, health probe, watchdog, catalog warmup.
2. `EditorHttpLocalCache` persists `~/.unity-cmd/editor-http.json` (`pid`, `session_id`, `listener_id`, `status`) so each domain reload can release orphans and only one server binds the port.
3. Stays up during Play; `beforeAssemblyReload` stops cleanly and marks cache `stopped`; `afterAssemblyReload` / Play / watchdog call `RequestEnsureRunning`.
4. `GET /health` returns `session_id`, `generation`, `listener_running`, `connector_state`, `play_mode`, `commands_ready` (alias `ready`), and `blocking_reasons` (connector-only: `reloading` / `compiling`; Play Mode is not a blocker).
5. `POST /list`, `POST /command`, `GET /commands/{id}` run on the Editor main thread via `ConnectorMainThreadScheduler` (single-flight; concurrent commands get `503` busy).
6. **`canAcceptCommand`** (Editor) checks `ListenerActive` only — not full `/health.ready`. During domain reload the scheduler returns `503` + `error=reloading`; deferred commands rely on CLI `allow_connection_retry` to retry until the shared timeout budget expires.

**Editor Play (`editor_play`):**

1. `EditorApplication` enters Play → `EditorPlayHttpBootstrap` calls `EditorPlayHttpHost.Start()`.
2. `PlayConnectorServer` starts `ConnectorHttpEndpoint` and `BridgeDriver` for main-thread drain.
3. Exiting Play → `EditorPlayHttpHost.Stop()`; listener thread shuts down without logging benign abort errors when the listener stops normally.

**Dev player (`player`):**

1. After first scene load, `DevPlayerBootstrap` starts `PlayerHttpHost` (same endpoint stack, different `host` on `/health`).
2. Release builds: `UnityCliConnector.Runtime.asmdef` excludes player runtime bootstrap by define constraint (`DEVELOPMENT_BUILD`) — no port 6795 listener.

### Scope routing on play-mode hosts

```text
HostKind.IsPlayModeHost(hostKind)  →  editor_play | player
```

| Handler `CommandHostScope` | `editor` :6547 | `editor_play` :6794 | `player` :6795 |
|------------------------|----------------|---------------------|----------------|
| `Editor` | Yes | No | No |
| `Runtime` | No | Yes | Yes |
| `Any` | Yes | Yes | Yes (if shipped in build) |

Profiler, screenshot, and `state` are **Editor** scope — use profile **`editor`** (:6547), including during Play Mode.

### Play-mode HTTP

- **CONN-10:** `PendingHttpResponses` holds POST until job completes (200/4xx). `GET /commands/{id}` is ledger/status read-only.
- **Main thread:** Handlers run on the play loop via `PlayConnectorServer` `BridgeDriver.Update`; avoid blocking.
- **Single-flight:** Concurrent `POST /command` on the same host returns `503` + `error=busy`.
- **Screenshot in Play:** `screenshot` (`Scope = editor`) — use profile **`editor`** (:6547) even while playing; `view=game` captures Game view; `view=scene` needs Scene view.
- **Profiler in Play:** Use `editor` profile on :6547 for edit-mode profiler; in-play hierarchy often uses `editor-play` if command is `runtime`/`any` scope only.

### CLI examples (Runtime)

```bash
# Editor Play — dedicated endpoint (recommended during Play)
unity-cmd --profile editor-play ping
unity-cmd --profile editor-play echo
unity-cmd --profile editor screenshot --view game --output_path Screenshots/play.png

# Development Build player
unity-cmd --profile package-play ping
unity-cmd --profile package-play echo

# Enter/exit Play still use Editor host
unity-cmd --profile editor play
unity-cmd --profile editor stop
```


### POST /command body

```json
{
  "command": "compile",
  "parameters": { },
  "request_id": "optional-uuid"
}
```

Sync / long-running (CONN-10): `200` or `4xx` + `{ ok, data, command_id, request_id }` when the HTTP connection closes.

## Command discovery

- Command class implements `ICliInvokeDescriptorProvider` (`CliCommand` / `CliCommand<T>` with `InvokeDescriptor` or `DeferredInvokeDescriptor<TParams>`).
- Execution entry is `Run()` / `Run(TParams)`; runtime callbacks via `BindRuntime` → `InvokeContext`.
- `CliCommandDiscovery` scans loaded assemblies; skips abstract types; builds `IInvokeHandler` per concrete command.
- Param type: `Descriptor.ParamType` or `CliCommand<TParams>`.
- `InvokeCompletionCatalog.GetCompletionKind`: deferred commands use `DeferredInvokeDescriptor.Completion`. `refresh` is immediate in catalog; `compile=true` upgrades the job to `compilation` at runtime.

## Command parameters (`CliParam`)

| Component | Role |
|-----------|------|
| `CliParamAttribute` | CLI flag metadata: optional `key` (default: camelCase property name), `Required`, `Description`, `AllowedValues`, `AlternateKeys` |
| `CliParamBinder` | Normalizes incoming `Dictionary<string, object>` → JSON → `DeserializeObject<T>` (Newtonsoft) |
| `CliParamContractResolver` | Maps `[CliParam]` keys to JSON property names |
| `ICommandHandler.ParamDescriptions` | Help lines for `POST /list` / CLI `help` |

Builtin param classes: `Editor/Params/EditorCommandParams.cs`, `Runtime/Params/EchoParams.cs`.

**Validation:** Missing `Required` properties throw bind errors before `Run` is invoked.

**Legacy input names:** `AlternateKeys` on a property (e.g. `wait` accepts `wait_for_completion`, `lines` accepts `count`).

**No `CliParams` bag:** Commands receive a typed instance only; services take `*Params` or primitives directly.

## Deferred lifecycle

**Editor host (`EditorJobStateManager`):**

1. `InvokePipeline` creates command status via `EditorJobStateManager`.
2. `compile` / `refresh --compile` call `ScriptCompilationService` (Unity pipeline events; see below); play/stop run immediately.
3. `EditorApplication.update` → completion policy tick (fallback when compilation service did not finish).
4. Terminal: `succeeded` | `failed` | `orphaned` (no progress 20s).
5. **No handler redispatch** after domain reload.

**Play / player hosts (`RuntimeJobStateManager`):**

1. `PlayModeInvokeHost` → `InvokePipeline` → `RuntimeJobStateManager.Create(host, …)`.
2. `InvokeExecutor.Execute` on the play main thread (via `PlayModeInvokeHost`).
3. `PlayConnectorServer` `BridgeDriver` drains the scheduler each frame; `RuntimeJobStateManager` ticks per host; command status persists in `PlayerPrefs`.
4. `GET /commands/{id}` via `RuntimeJobStore` + `JobResponseBuilder`.

### Completion policies

| Kind | Host | Completes when |
|------|------|----------------|
| `compilation` | Editor | `ScriptCompilationService`: `compilationStarted`/`compilationFinished` (matched `context`); if no cycle starts, idle frames + `!isCompiling`; policy fallback: `!EditorApplication.isCompiling`. No public API to pre-check if compile is needed (Unity 2021.3+). |
| `enter_play` | Editor | `EditorApplication.isPlaying` |
| `exit_play` | Editor | `!EditorApplication.isPlaying` |
| `started` (`signal`) | Editor / Play / player | command decides completion via `ctx.CompleteSuccess/CompleteFail` or `ctx.RunBackground` (no extra policy class needed) |

## Host routing (`InvokeAvailability`)

| HTTP host | Allowed scopes |
|-----------|------------------|
| `editor` (:6547) | `Editor`, `Any` |
| `editor_play` (:6794) | `Runtime`, `Any` |
| `player` (:6795) | `Runtime`, `Any` |

CLI mirrors this in `checkScopeCompatibility` before `POST /command`.

## Instance discovery

CLI (`unity-cmd/src/client/connection.js`): profile file only — no port scanning.

1. `~/.unity-cmd/profiles/{name}.json` from `unity-cmd profile create`
2. `--profile` or `UNITY_CMD_PROFILE`
3. `GET /health` must match `connector_host` in the profile

**LAN:** `UNITY_CMD_BIND=lan` or `UNITY_CMD_LAN=1` on Unity; remote machine: `unity-cmd profile create phone --host <ip> --port <p> --host-kind editor_play`.

**Compile / domain reload:** Editor HTTP stops and restarts. CLI `wait` uses `instances/*.json` (SSOT) + `/health` for editor; play/player poll `/health` only. Jobs flush to `EditorJobLedger` before reload; `GET /commands/{id}` is read-only recovery (no redispatch). CONN-10 POST holds until completion when the listener is up.

**HTTP (unified):** `ConnectorRequestDispatcher` — `/health`, `POST /list`, `GET /commands/{id}`, `POST /command` via `IMainThreadHttpScheduler` (Editor / play-mode bridges).

## Built-in commands

| Command | Aliases | Deferred | Scope | Notes |
|---------|---------|----------|-------|-------|
| `ping` | — | no | any | Health |
| `state` | — | no | editor | Snapshot + `connector_build`, `recent_console` |
| `echo` | — | no | editor / runtime | Editor vs play handlers; shared `EchoParams` |
| `compile` | `recompile`, `reload` | yes | editor | `compilation`, 30s default |
| `play` | — | yes | editor | `enter_play`, 60s |
| `stop` | — | yes | editor | `exit_play`, 60s |
| `refresh` | — | immediate; `compile=true` → compilation job | editor | `AssetRefreshService.Refresh(RefreshParams)` |
| `console` | — | no | editor | LogEntries reflection |
| `menu` | — | no | editor | `ExecuteMenuItem` |
| `screenshot` | — | no | editor | Scene/Game PNG; Editor host only |
| `exec` | — | no | editor | Roslyn/csc snippet |
| `profiler` | — | no | editor | Editor host; works during Play on :6547 |
| `manage` | — | no | editor | play/stop/pause/refresh/tags/layers/tools |
| `reserialize` | — | no | editor | ForceReserializeAssets |

## Command parameters (agent reference)

### `state`

No parameters. Returns `connector_state`, `play_mode`, `commands_ready`, `is_playing`, `is_paused`, `is_compiling`, `ready_for_tools`, `blocking_reasons` (connector-only), `active_command`, `connector_build`, `recent_console` (last N entries).

### `console`

| Param | Default | Values |
|-------|---------|--------|
| `type` | `error,warning` | `error`, `warning`, `log` (comma-separated) |
| `lines` / `count` | all matched | max entries |
| `stacktrace` | `user` | `none`, `user`, `all` |
| `clear` | false | `true` clears console |

### `menu`

| Param | Required |
|-------|----------|
| `menu_path` | yes | e.g. `File/Save Project` |

### `screenshot`

`Scope = any` (Editor host in edit and Play). During Play, `view=game` uses `Camera.main` render, or `ScreenCapture.CaptureScreenshotAsTexture` fallback.

| Param | Notes |
|-------|-------|
| `view` | `game`, `scene` (`scene` needs active SceneView; may fail in Play) |
| `output_path` | relative to project or absolute |
| `width` / `height` | default 1920×1080 |

### `exec`

| Param | Notes |
|-------|-------|
| `code` | C# snippet; use `return` for value |
| `usings` | extra namespaces, comma-separated |
| `csc` / `dotnet` | optional compiler paths |

### `profiler`

| Param | Notes |
|-------|-------|
| `action` | `hierarchy` (default), `enable`, `disable`, `status`, `clear` |
| `frame` | single frame index (default: last) |
| `frames` | average last N frames — **known bug: may throw cast error** |
| `from`, `to` | inclusive range for average — same bug risk |
| `thread` | default `0` |
| `root`, `parent` | subtree filter |
| `min` | min ms threshold |
| `sort` | `total`, `self`, `calls` |
| `max` | max items (default 30) |
| `depth` | tree depth (default 1; `0` or large → deep) |

**Agent workflow (Edit Mode host):** `enable` on `:6547` → `play` → wait → `stop` → `hierarchy --frame <n>` on `:6547`.

**In Play (`editor_play`):** `profiler --action status` / `hierarchy` and `screenshot --view game` on `:6794` without stopping Play.

### `manage`

| Param | Notes |
|-------|-------|
| `action` | `play`, `stop`, `pause`, `refresh`, `set_active_tool`, `add_tag`, `remove_tag`, `add_layer`, `remove_layer` |
| `wait` | bool, block until play transition done (`wait_for_completion` accepted via `AlternateKeys`) |
| `tool_name`, `tag_name`, `layer_name` | per action |

### `reserialize`

| Param | Notes |
|-------|-------|
| `paths` | comma-separated (`path` accepted) |
| omit | entire project (slow) |

### `refresh`

| Param | Notes |
|-------|-------|
| `compile` | `true` → triggers compilation deferred flow |
| `force` | `true` → allow refresh while entering play mode |

## Main-thread execution

Editor commands run on the main thread via `ConnectorMainThreadScheduler` (queued from HTTP); worker threads never call Unity APIs or `CliCommandDiscovery`.

## Services (Editor)

| Service | Used by |
|---------|---------|
| `UnityConsoleReader` | `console` |
| `AssetRefreshService` | `refresh` |
| `ProfilerHierarchyService` | `profiler` |
| `CsharpExecutor` | `exec` |

## Tests (EditMode)

Assembly: `UnityCliConnector.Tests.Editor` in consuming project.

```bash
Unity -runTests -batchmode -projectPath <Project> -testPlatform editmode \
  -assemblyNames UnityCliConnector.Tests.Editor
```

Set `UNITY_PROJECT_PATH` for external test project reference.

## Extending

```csharp
using Air.UnityConnector.Cli;
using Air.UnityConnector.Invoke;
using Air.UnityConnector.Params;

public sealed class MyToolParams
{
    [CliParam(Required = true, Description = "example flag")]
    public string Name { get; set; }
}

public sealed class MyToolCommand : CliCommand<MyToolParams>
{
    public override InvokeDescriptor Descriptor { get; } = new DeferredInvokeDescriptor<MyToolParams>(
        "my.tool", CommandHostScope.Editor, "…", defaultTimeoutMs: 30000);

    public override void Run(MyToolParams p) => MarkRunning();
}
```

### Task-based deferred template (recommended)

For most async commands, use default `started` completion and report from `InvokeContext` callbacks (`CompleteSuccess` / `CompleteFail`).

```csharp
using System.Threading;
using Air.UnityConnector.Cli;
using Air.UnityConnector.Invoke;
using Air.UnityConnector.Params;

public sealed class SlowHashParams
{
    [CliParam(Required = true, Description = "input text")]
    public string Input { get; set; }
}

public sealed class SlowHashCommand : CliCommand<SlowHashParams>
{
    public override InvokeDescriptor Descriptor { get; } = new DeferredInvokeDescriptor<SlowHashParams>(
        "slow.hash",
        CommandHostScope.Runtime,
        "Compute hash in background Task",
        defaultTimeoutMs: 15000);

    public override void Run(SlowHashParams p)
    {
        MarkRunning();
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            Thread.Sleep(800);
            var hash = p.Input?.GetHashCode() ?? 0;
            CompleteSuccess(new { hash, input = p.Input });
        });
    }
}
```

If you need main-thread Unity API after background work, compute in a Task first, then marshal the apply step back to your main-thread bridge/update loop and call `CompleteSuccess(...)` there.

1. Add name to `CommandNames.cs` (recommended).
2. Increment `ConnectorBuild.Id`.
3. `unity-cmd --profile editor compile` — command and `params` appear on next `POST /list` / `help`.

See [../../docs/MAINTENANCE.md](../../docs/MAINTENANCE.md) for immediate vs deferred patterns.
