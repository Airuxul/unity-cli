# Integration scenarios

| Scenario | Default profile | Unity prerequisite |
|----------|-----------------|-------------------|
| `editor-lifecycle` | `editor` | Editor open; `editor-play` profile; set `UNITY_CMD_WORKSPACE` when cwd ≠ project root |
| `gamedemo-scene-switch-play` | `editor` | GameDemo: StatUp→Boot 切场景后 Play；验证 :6547 与 Editor Play :6794（:6795 在 Editor 内应不可用） |
| `player-runtime` | `package-play` | Development Build with Dev assembly running |
| `compile-error-recovery` | `editor` | Editor open; `UNITY_CMD_WORKSPACE` set to project root |
| `compile-recompile-cycle` | `editor` | `UNITY_CMD_WORKSPACE` with `Assets/`; runs in `test:integration:all` |
| `editor-reliability-stress` | `editor` | build ≥ 40; `UNITY_CMD_INTEGRATION_STRESS=1` with `test:integration:all` |

```bat
cd unity-cmd
set UNITY_CMD_WORKSPACE=C:\Path\To\UnityProject

REM GameDemo scene switch + Play (requires UNITY_CMD_WORKSPACE)
set UNITY_CMD_WORKSPACE=C:\Project\GameDemo
set UNITY_CMD_SCENARIO=gamedemo-scene-switch-play
npm run test:integration:gamedemo

REM Editor full lifecycle (default scenario)
set UNITY_CMD_PROFILE=editor
npm run test:integration

REM Compile error recovery flow
set UNITY_CMD_PROFILE=editor
set UNITY_CMD_SCENARIO=compile-error-recovery
npm run test:integration

REM Script add/remove + compile x3 (domain reload stress)
set UNITY_CMD_WORKSPACE=C:\Project\GameDemo
set UNITY_CMD_PROFILE=editor
set UNITY_CMD_SCENARIO=compile-recompile-cycle
npm run test:integration

REM Dev player only
set UNITY_CMD_PROFILE=package-play
set UNITY_CMD_SCENARIO=player-runtime
npm run test:integration
```

## editor-lifecycle (~56 steps after `repeat` expansion)

1. **Edit mode** — ping, state, catalog, compile, **wait after compile**, echo, console, profiler, exec
2. **Play** — play, wait for editor-play endpoint
3. **Play mode** — runtime echo on `editor_play`; catalog isolation; editor host: ping, list, state, profiler, screenshot
4. **Exit** — stop, verify edit mode restored
5. **Play/Stop stress** — `26_play_stop_stress` repeats 5×: play → stop → ping → echo (uses default CLI/step timeouts only)
6. **Final gate** — state + ping confirm Editor HTTP still healthy (guards `EditorServerSupervisor` regressions)

Do not add per-step `timeoutMs` unless a step truly needs more than `INTEGRATION_ATTACH_TIMEOUT_MS` / deferred command defaults.

### Scenario `repeat` blocks

A step may define `"repeat": N` and nested `"steps": [...]`. The runner flattens these into
`{parent}_{cycle}_{sub}` names (see `flattenScenarioSteps` in `lib/steps.mjs`).

## compile-error-recovery (14 steps)

Requires `UNITY_CMD_WORKSPACE` pointing to a writable Unity project root.

1. **Baseline** — ping + compile to confirm a clean build
2. **Inject error** — write `Assets/_IntegrationTest_CompileErrorRecovery.cs` with a syntax error
3. **Compile** — deferred `compile` completes (`ok: true`); Unity reports script errors
4. **Console** — verify error entries exist
5. **Fix** — delete the bad `.cs` (+ `.meta`) file
6. **Recover** — `compile` again; state, ping, catalog, echo confirm edit-mode recovery

> The test file is always cleaned up by `deleteFile`. For CI, add a pre-run cleanup if a prior run aborted mid-scenario.

## player-runtime (7 steps)

Runtime catalog + `echo`; negative checks for Editor-only commands on player host.

---

## CLI changes tied to these tests (scope & necessity)

These edits live in `unity-cmd/src/client/` only. They do **not** change connector C# behaviour.

| Change | File | Why necessary | Impact on other logic |
|--------|------|---------------|------------------------|
| **`resolveWaitProjectPath`** | `connector-readiness.js` | `wait` / `waitProfile` match `instances` heartbeat `projectPath` via `UNITY_CMD_WORKSPACE` (not CLI `cwd`) | Required when running integration from `unity-cmd/` |
| **`confirmEditorHealth` try/catch** | `connector-readiness.js` | Transient `fetch failed` during Play/reload must not crash `wait` / `waitProfile` steps | Same success/failure decisions; only avoids uncaught exceptions |
| **`likelyRestarting` retry loop** | `connection.js` | Heartbeat can lag while `/health` is OK — fixes flaky `list` during Play | Only when heartbeat indicates restart |
| **`03c_wait_after_compile`** | `editor-lifecycle.json` | Domain reload after `compile` before `echo` | Test-only |
| **`26_play_stop_stress` repeat** | `editor-lifecycle.json` | `EditorServerSupervisor` Play transition regression | Test-only |

**Not affected:** `editor_play` / `player` hosts, catalog cache TTL, unit-test mocks (unless they call the same functions).

**Related connector fixes (C#, `com.air.unity-connector`):**

- **CONN-10** — sync `POST /command` (no HTTP 202); `PendingHttpResponses`; `MIN_CONNECTOR_BUILD` 40.
- **`EditorServerSupervisor`** — single HTTP lifecycle writer; drain → port wait → start; `TryRecoverStuckDomainReload` (watchdog + post-reload).
- **`OnPlayModeSettled`** — reuse listener when cache lags instead of unnecessary drain.

Recompile the Unity project after pulling connector changes.

When integration fails after Play/Stop, inspect `~/.unity-cmd/editor-server-trace.log` (filter Unity Console: `[unity-connector][supervisor]`).
