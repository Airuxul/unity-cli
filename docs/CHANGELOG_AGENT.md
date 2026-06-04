# Agent Changelog — `unity-cli`

## 2026-06-04

- **CONN-10:** Sync `POST /command` (build 40); removed CLI HTTP 202 poll (`command-status.js`, `editor-http-cache.js`).
- **CLI SSOT:** `wait` / `resolveTarget` use `~/.unity-cmd/instances/*.json`; `resolveWaitProjectPath()` + `UNITY_CMD_WORKSPACE` for integration cwd ≠ Unity root.
- **Editor:** `EditorServerSupervisor` single writer; `TryRecoverStuckDomainReload`; `PendingHttpResponses` for held HTTP.
- **Tests:** `editor-lifecycle` post-compile wait; `compile-recompile-cycle`, `editor-reliability-stress`, `gamedemo-scene-switch-play`; docs synced for `UNITY_CMD_WORKSPACE`.

## 2026-06-02

- Added `docs/DOC_GOVERNANCE.md` (bilingual README layout, meta AirUnityPackage link)
- Added `docs/CHANGELOG_AGENT.md`
- Updated `docs/AGENTS.md` (metadata, governance index)
- Moved `unity-cmd` skill to meta `.cursor/skills/unity-cmd/SKILL.md` (single English file; no references/)
- Aligned `README.md` / `README.zh-CN.md` (environment variable table, LAN profile example)
- Updated `docs/README.md` / `docs/README.zh-CN.md` (governance doc links)
