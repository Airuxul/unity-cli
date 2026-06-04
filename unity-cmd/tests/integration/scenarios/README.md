# Integration scenarios

| Scenario | Default profile | Unity prerequisite |
|----------|-----------------|-------------------|
| `editor-lifecycle` | `editor` | Editor open; `editor-play` profile for Play steps |
| `player-runtime` | `package-play` | Development Build with Dev assembly running |
| `compile-error-recovery` | `editor` | Editor open; `UNITY_CMD_WORKSPACE` set to project root |

```bat
cd unity-cmd
set UNITY_CMD_WORKSPACE=C:\Path\To\UnityProject

REM Editor full lifecycle (default scenario)
set UNITY_CMD_PROFILE=editor
npm run test:integration

REM Compile error recovery flow
set UNITY_CMD_PROFILE=editor
set UNITY_CMD_SCENARIO=compile-error-recovery
npm run test:integration

REM Dev player only
set UNITY_CMD_PROFILE=package-play
set UNITY_CMD_SCENARIO=player-runtime
npm run test:integration
```

## editor-lifecycle (~56 steps after `repeat` expansion)

1. **Edit mode** — ping, state, catalog, compile, echo, console, profiler, exec
2. **Play** — play, wait for editor-play endpoint
3. **Play mode** — runtime echo on `editor_play`; catalog isolation; editor host: ping, list, state, profiler, screenshot
4. **Exit** — stop, verify edit mode restored
5. **Play/Stop stress** — `26_play_stop_stress` repeats 5×: play → stop → ping (`listener_running`) → echo
6. **Final gate** — state + ping confirm Editor HTTP still healthy (guards `EditorServerSupervisor` regressions)

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
| **`confirmEditorHealth` try/catch** | `connector-readiness.js` | Transient `fetch failed` during Play/reload must not crash `wait` / `waitProfile` steps | Same success/failure decisions; only avoids uncaught exceptions |
| **`likelyRestarting` retry loop** | `connection.js` | Instance heartbeat can lag (`listener_running: false` + cache `stopped`) while `/health` is already OK — fixes flaky step `14_list_catalog_editor_during_play` | Only runs when heartbeat already indicates restart; normal path unchanged |
| **`13b_ping_editor_during_play`** | `editor-lifecycle.json` | Ensures Editor :6547 is probed before catalog `list` during Play | Test-only ordering |
| **`26_play_stop_stress` repeat** | `editor-lifecycle.json` | Regression cover for `EditorServerSupervisor` Play transition handling | Test-only |
| **`compile-error-recovery` flow** | `compile-error-recovery.json` | Aligns with deferred `compile` (success when compilation cycle finishes, even with script errors) | Test-only |

**Not affected:** `editor_play` / `player` hosts, catalog cache TTL, command dispatch, unit-test mocks (unless they call the same functions with the same inputs).

**Related connector fix (C#, separate package):** `EditorServerSupervisor.OnPlayModeSettled` — avoids false `[startup-failure]` LogError spam after Stop; validated by stress steps above.
