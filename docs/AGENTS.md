# AGENTS — `unity-cli`

**Last Updated:** 2026-06-04 · **Owner:** package maintainers · **Scope:** canonical agent entry (this repository, English)

## Canonical rule

This file is the agent entrypoint for **this** Git repository (`unity-cmd` + `com.air.unity-connector`). If another file in this repo conflicts with it, this file wins.

For **running** Unity automation, use the meta-repo skill [`.cursor/skills/unity-cmd/SKILL.md`](../../../.cursor/skills/unity-cmd/SKILL.md) (workspace root = **CustomPackages** / AirUnityPackage). This file is a short supplement only.

## Package scope

| Property | Value |
|----------|--------|
| **Monorepo** | `unity-cmd` (Node CLI) + `com.air.unity-connector` (Unity UPM) |
| **Meta index** | [AirUnityPackage `config/registry.json`](https://github.com/Airuxul/AirUnityPackage/blob/main/config/registry.json) |
| **No root `package.json`** | Run npm scripts from `unity-cmd/` |

## User documentation

| File | Language |
|------|----------|
| [README.md](../README.md) | English |
| [README.zh-CN.md](../README.zh-CN.md) | Chinese |
| [TODO.zh-CN.md](../TODO.zh-CN.md) | Chinese backlog — IDs sync with [TODO.md](TODO.md) |

## Agent documentation (this repo)

| File | Purpose |
|------|---------|
| [AGENTS.md](AGENTS.md) | This file |
| [DOC_GOVERNANCE.md](DOC_GOVERNANCE.md) | Doc workflow |
| [CHANGELOG_AGENT.md](CHANGELOG_AGENT.md) | Agent-side changelog |
| [TODO.md](TODO.md) | Optimization backlog (CONN- / CMD-; meta [TODO_ROADMAP](https://github.com/Airuxul/AirUnityPackage/blob/main/docs/TODO_ROADMAP.md)) |
| [ARCHITECTURE.md](ARCHITECTURE.md) | System design, request flow |
| [MAINTENANCE.md](MAINTENANCE.md) | Command patterns, bumps, extension |
| [../unity-cmd/docs/IMPLEMENTATION.md](../unity-cmd/docs/IMPLEMENTATION.md) | CLI internals |
| [../com.air.unity-connector/docs/IMPLEMENTATION.md](../com.air.unity-connector/docs/IMPLEMENTATION.md) | HTTP API, deferred commands |

## Cursor skills (meta repo only)

| Skill | Path (CustomPackages root) | Use |
|-------|----------------------------|-----|
| `unity-cmd` | `.cursor/skills/unity-cmd/` | Run `unity-cmd` against Unity |
| `doc-read-index` | `.cursor/skills/doc-read-index/` | Documentation inventory |
| `doc-generate-update` | `.cursor/skills/doc-generate-update/` | Documentation updates |

**Do not** add `.cursor/skills/` under this submodule. **Do not** restore `docs/unity-cmd-skill/`.

**Catalog:** `unity-cmd --profile <name> list` only; cache at `~/.unity-cmd/cache/catalog-<host>:<port>.json`.

## Required reads before doc updates

1. `docs/AGENTS.md` (this file)
2. `docs/DOC_GOVERNANCE.md`
3. `README.md` and `README.zh-CN.md`

## Dual-track update rule

| Change type | Action |
|-------------|--------|
| User-visible | Update both READMEs and relevant `docs/*.md` |
| Unity automation behavior | Update meta `.cursor/skills/unity-cmd/` (not this submodule's `docs/`) |
| Code-only | `CHANGELOG_AGENT.md` when agent-relevant |

## Quick facts

- **Command source of truth:** Unity `POST /list` → `~/.unity-cmd/cache/catalog-<host>:<port>.json`.
- **Profiles:** `editor` :6547, `editor_play` :6794, `player` :6795.
- **After connector C# changes:** `wait` → `ping` → `compile` (build ≥ `MIN_CONNECTOR_BUILD`, currently **40**).
- **Integration:** set `UNITY_CMD_WORKSPACE` to the Unity project root when running tests from `unity-cmd/` (see [EDITOR_SERVER_RELIABILITY.zh-CN.md](EDITOR_SERVER_RELIABILITY.zh-CN.md)).

See [README.md](../README.md#ai--agent-usage) for examples.
