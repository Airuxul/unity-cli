# unity-connector — Implementation

Version: 0.1.3

## Assemblies

| Assembly | Platform | Contents |
|----------|----------|----------|
| `UnityCliConnector.Core` | all | Protocol, discovery, `HttpServer`, `CommandCatalog` |
| `UnityCliConnector.Editor` | Editor | `EditorRequestDispatcher`, jobs, heartbeat, builtins |
| `UnityCliConnector.Runtime` | Player | Runtime HTTP (debug builds), `echo.runtime` |

## HTTP API

| Method | Path | Response |
|--------|------|----------|
| GET | `/health` | `{ ok, host }` |
| POST | `/command` | `200` sync result or `202` + `job_id` |
| GET | `/jobs/{id}` | job status + result |
| POST | `/list` | full catalog (`catalog_version`, `commands`, `alias_to_command`) |

### POST /command body

```json
{
  "command": "compile",
  "parameters": { "compile": true },
  "request_id": "optional"
}
```

## Job lifecycle

1. `[CliCommand]` metadata (`IsJob`, `Completion`) plus `refresh?compile=true` classifies job commands.
2. `JobManager.Create` stores job in `SessionState` key `UnityCliConnector.Jobs`.
3. Side effect runs on `EditorApplication.delayCall` (compile, play, stop).
4. `EditorApplication.update` → `CompletionPolicy.Tick()` observes `isCompiling` / `isPlaying`.
5. **No handler redispatch** after domain reload.

Orphan: no progress for 20s → `orphaned`.

## Completion policies

| Kind | Completes when |
|------|----------------|
| `compilation` | `!EditorApplication.isCompiling` after running |
| `enter_play` | `EditorApplication.isPlaying` |
| `exit_play` | `!EditorApplication.isPlaying` |

## Heartbeat

Every 0.5s writes `~/.unity-cmd/instances/{hash}.json`:

```json
{
  "project_path": "C:/Game",
  "host": "127.0.0.1",
  "port": 6400,
  "protocol_version": 1,
  "editor_state": {
    "is_playing": false,
    "is_compiling": false,
    "ready_for_tools": true,
    "blocking_reasons": [],
    "active_job": null
  }
}
```

Port: `UNITY_CMD_PORT` or `6400 + hash(dataPath) % 800`.

## Command routing

- Editor host, not playing: Editor + Any commands.
- Editor host, playing: Runtime + Any commands; Editor-scoped commands rejected.
- Runtime host (debug player): Runtime + Any only.

## HTTP layering

- `Core/Http/HttpServer` — listener loop and JSON responses
- `Core/Http/CommandPipeline` — shared sync (200) vs job (202) handling
- `Core/ICommandHost` — host-specific dispatch entry (`EditorCommandHost`, `RuntimeCommandHost`)
- `Editor/EditorRequestDispatcher` — `/health`, `/list`, `/command`, `/jobs/{id}`
- `Runtime/RuntimeRequestDispatcher` — `/health`, `/command` only

`CommandListBuilder` delegates to `CommandCatalog` (single catalog source).

## Editor tool commands

| Command | Aliases | Notes |
|---------|---------|-------|
| `editor.console` | `console`, `logs` | Read/clear Unity console via `LogEntries` reflection |
| `editor.menu` | `menu` | `EditorApplication.ExecuteMenuItem` |
| `editor.screenshot` | `screenshot` | Scene/Game view PNG capture |
| `editor.exec` | `exec` | Compile & run C# via Unity `csc` |
| `editor.profiler` | `profiler` | Profiler hierarchy / enable / status / clear |
| `editor.manage` | `manage` | Play, stop, pause, refresh, tags, layers, tools |
| `editor.reserialize` | `reserialize` | `AssetDatabase.ForceReserializeAssets` |

### `editor.console` parameters

- `type` — comma-separated: `error`, `warning`, `log` (default all three)
- `lines` / `count` — max entries
- `stacktrace` — `none`, `user` (default), `full`
- `clear` — clear console when `true`

### `editor.exec` parameters

- `code` — C# source (use `return` for output)
- `usings` — extra namespaces (comma-separated)
- `csc` / `dotnet` — override compiler paths

### `editor.profiler` parameters

- `action` — `hierarchy` (default), `enable`, `disable`, `status`, `clear`
- `frame`, `from`, `to`, `frames`, `thread`, `parent`, `root`, `min`, `sort`, `max`, `depth`

### `editor.manage` parameters

- `action` — `play`, `stop`, `pause`, `refresh`, `set_active_tool`, `add_tag`, `remove_tag`, `add_layer`, `remove_layer`
- `wait_for_completion` — block until play mode transition finishes
- `tool_name`, `tag_name`, `layer_name` — per action

### `editor.reserialize` parameters

- `path` — single asset path, or `paths` comma-separated; omit for entire project

## Main-thread execution

Sync commands use `EditorCommandExecutor.ExecuteSync` with `delayCall` + wait when invoked from HTTP thread.

## Tests (EditMode)

Optional UTF via Unity Test Framework (`Tests/Editor/`):

```bash
# From unity-cmd, with UNITY_PROJECT_PATH set to a Unity project that references this package:
# Run EditMode tests in Unity Test Runner, or your CI Unity batch.
```

Covers `CommandJobCatalog`, `CommandDiscovery` (ping, compile job, console), and related pure logic.
