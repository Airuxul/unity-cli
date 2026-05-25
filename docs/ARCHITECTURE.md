# unity-cli — Architecture

This document records the design goals from the initial refactor and their completion status.

## Goals (all implemented)

| Goal | Status | Implementation |
|------|--------|----------------|
| Class-level `[CliCommand]` + static `Run` | Done | `CommandDiscovery` reflects types in all loaded assemblies |
| Shared HTTP layer | Done | `Core/Http/HttpServer`, `IRequestDispatcher`, `CommandHttpHelper`, `CommandPipeline` |
| Unified sync/job pipeline | Done | `ICommandHost` → `EditorCommandHost` / `RuntimeCommandHost` |
| Job metadata on attributes | Done | `IsJob`, `Completion`, `DefaultTimeoutMs`, `AllowConnectionRetry`, `Aliases` on `CliCommandAttribute` |
| Single job classifier | Done | `CommandJobCatalog` uses handler metadata; only special case: `refresh` + `compile` |
| Unity-owned command catalog | Done | `CommandCatalog.BuildResponse()` → `POST /list` |
| CLI catalog cache | Done | `unity-cmd/src/catalog.js` → `~/.unity-cmd/cache/` |
| No hardcoded CLI command table | Done | `commands.js` removed; bootstrap aliases for offline/legacy only |
| Editor diagnostics tools | Done | `editor.console`, `editor.menu`, `editor.screenshot` |
| Extended editor tools (reference port) | Done | `editor.exec`, `editor.profiler`, `editor.manage`, `editor.reserialize` |

## Request flow

```text
unity-cmd argv
  → dispatch (ping/list/help local)
  → loadCatalog (POST /list, cached)
  → resolveRemoteCommand (aliases, timeouts)
  → POST /command
       → IRequestDispatcher
            → ICommandHost.HandleCommand
                 → CommandPipeline (202 job | 200 sync)
  → pollJob if 202
```

## Assemblies

- **Core** — protocol, discovery, catalog, HTTP, `ICommandHost`
- **Editor** — HTTP dispatcher, jobs, heartbeat, builtins, services (`UnityConsoleReader`, `AssetRefreshService`)
- **Runtime** — debug-player HTTP, runtime commands only

## Extending

Add a static class with `[CliCommand("my.tool", ...)]` and `Run(CliParams)`. Recompile the connector (`unity-cmd recompile`). No `unity-cmd` changes unless adding a new local meta command (`ping`, `list`, `help`).
