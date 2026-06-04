# unity-cmd

[简体中文](README.zh-CN.md)

Node.js CLI that sends commands to [unity-connector](../com.air.unity-connector/) over HTTP.

**Version:** 0.1.0

## Install

```bash
cd unity-cmd
npm install
npm link   # optional
```

## Profiles

All remote commands need a profile:

```bash
unity-cmd profile create editor --host 127.0.0.1 --port 6547 --host-kind editor
unity-cmd profile create editor-play --port 6794 --host-kind editor_play
unity-cmd profile create package-play --port 6795 --host-kind player
unity-cmd profile list
```

Or set `UNITY_CMD_PROFILE=editor` and omit `--profile`.

## Usage

```bash
unity-cmd help
unity-cmd --profile editor help
unity-cmd --profile editor ping
unity-cmd --profile editor list
unity-cmd --profile editor list --refresh-catalog

unity-cmd --profile editor play
unity-cmd --profile editor stop
unity-cmd --profile editor echo --message hello

unity-cmd --profile editor compile
unity-cmd --profile editor refresh --compile true

unity-cmd --profile editor console --lines 20
unity-cmd --profile editor screenshot --view game --output_path Screenshots/game.png
unity-cmd --profile editor-play echo
```

Commands and aliases come from Unity (`POST /list`), cached under `~/.unity-cmd/cache/`.

**Local-only (no profile):** `help`, `profile …`

**Remote (profile required):** `ping`, `list`, and all connector commands.

## `list` vs `help`

| Command | Output |
|---------|--------|
| `list` | JSON catalog |
| `help` | Human-readable text; when online, refetches catalog and prints each command's `params` |

## Commands per instance

| Profile | Port | When |
|---------|------|------|
| `editor` | 6547 | Editor open (compile, play/stop, edit-mode tools) |
| `editor-play` | 6794 | Editor in Play Mode |
| `package-play` | 6795 | Development Build running |

Default ports are defined in [`src/constants.js`](src/constants.js) (`doc:check` keeps README in sync).

`play` / `stop` always use profile **`editor`**.

Full examples: [../README.md#commands-per-instance](../README.md#commands-per-instance).

## Environment

| Variable | Description |
|----------|-------------|
| `UNITY_CMD_PROFILE` | Default profile name |
| `UNITY_CMD_WORKSPACE` | Integration: Unity project root (`Assets/` steps, `assertFile`, `wait` projectPath match) |
| `UNITY_CMD_TIMEOUT_MS` | Default timeout (20000) |
| `UNITY_CMD_TOKEN` | Optional HTTP auth token |

## npm scripts

| Script | Description |
|--------|-------------|
| `npm run verify` | Unit tests + doc version check |
| `npm run test:unit` | Unit tests only |
| `npm run test:integration` | Scenario tests (needs Unity + profiles) |
| `npm run test:all` | `verify` then `test:integration` |

## Integration tests

1. Install `com.air.unity-connector` and open the Unity project.
2. Create profiles (`editor`, `editor-play`, `package-play`).
3. `set UNITY_CMD_PROFILE=editor` and `npm run test:integration`.

| Scenario | `UNITY_CMD_PROFILE` |
|----------|---------------------|
| `editor-lifecycle` (default) | `editor` |
| `player-runtime` | `package-play` |

## See also

- [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md)
- [../docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md)
- [../com.air.unity-connector/README.md](../com.air.unity-connector/README.md)
