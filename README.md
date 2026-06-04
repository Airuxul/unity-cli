# unity-cli

[简体中文](README.zh-CN.md)

Command-line control of Unity Editor over loopback HTTP — for scripts, CI, and agents.

| Package | Role |
|---------|------|
| [unity-cmd](unity-cmd/) | Node.js CLI |
| [com.air.unity-connector](com.air.unity-connector/) | Unity UPM bridge (HTTP + commands) |

## How it works

```text
unity-cmd  →  GET /health, POST /list, POST /command, GET /commands/{id}
              com.air.unity-connector (Editor :6547, Editor Play :6794, Player :6795)
```

- Unity publishes the command catalog (`POST /list`); the CLI resolves aliases and caches it per endpoint (`~/.unity-cmd/cache/catalog-<host>:<port>.json`).
- Long work (`compile`, `play`, `stop`, …) completes in **one POST** (CONN-10). HTTP 202 is rejected.
- Failures are JSON: `ok`, `error_code`, optional `hint`.

Built-ins include play/stop, console, exec, profiler, screenshot, menu, reserialize. Extend commands in the connector — see [com.air.unity-connector/README.md](com.air.unity-connector/README.md).

Editor readiness: `~/.unity-cmd/instances/*.json` (SSOT) + `/health` confirm (`session_id` / `generation`).

## Quick start

```bash
cd unity-cmd && npm install && npm link   # optional

unity-cmd profile create editor --host 127.0.0.1 --port 6547 --host-kind editor
unity-cmd --profile editor ping
unity-cmd --profile editor list
unity-cmd --profile editor play
unity-cmd --profile editor screenshot --view game --output_path Screenshots/game.png
unity-cmd --profile editor stop
```

Install the connector and open your Unity project: [com.air.unity-connector/README.md](com.air.unity-connector/README.md).
CLI flags and npm scripts: [unity-cmd/README.md](unity-cmd/README.md).

After editing connector C#: `unity-cmd --profile editor compile` (alias `recompile`). Default timeout is **20s**; do not raise unless needed.

Fixed ports: **Editor `6547`**, **Editor Play `6794`**, **Player `6795`** — all three can run together on one machine.

## AI / Agent usage

Unity automation uses the **AirUnityPackage meta repository** Cursor skill (this submodule does not ship a copy under `docs/`).

| Resource | Path when workspace root is **CustomPackages** |
|----------|--------------------------------------------------|
| Skill | `.cursor/skills/unity-cmd/SKILL.md` |

Open the meta repo root in Cursor so the skill loads. Quick reference: [docs/AGENTS.md](docs/AGENTS.md).

### Default logic (user asks to do X in Editor or runtime)

```text
1. Pick domain → profile (editor / editor-play / package-play)
2. unity-cmd --profile <name> list → match user intent on commands[]
3. Catalog present but no match → list --refresh-catalog → match again
4. Still no match → explain, suggest 2–5 command names → abort; do not invent commands
5. ping OK → run matched command
```

Catalog lives in `~/.unity-cmd/cache/catalog-<host>_<port>.json` (CLI-managed). **Do not** copy it into skill markdown. Refresh: `list --refresh-catalog`.

### Example 1: check compile after script changes

**User:** *"I changed C# — check Unity compile errors."*

**Agent:**

1. `unity-cmd --profile editor list`; confirm `compile` and `console` in `commands`.
2. No match → abort per skill; do not invent commands.
3. Run:

```bash
unity-cmd --profile editor ping
unity-cmd --profile editor compile
unity-cmd --profile editor console --type error,warning --lines 30
```

4. Report from JSON `ok`, `error_code`, `hint`; do not enter Play if `compile` failed.

### Example 2: Play Mode screenshot

**User:** *"Enter Play and capture the Game view."*

**Agent:** **editor** domain (`play` / `screenshot` / `stop` use profile `editor`):

```bash
unity-cmd --profile editor ping
unity-cmd --profile editor play
unity-cmd --profile editor screenshot --view game --output_path Screenshots/agent-play.png
unity-cmd --profile editor stop
```

For runtime `echo` while playing: `unity-cmd --profile editor-play list`, then use profile `editor-play`.

### Example 3: refresh command catalog

**User:** *"Refresh the Unity Editor command cache."*

```bash
unity-cmd --profile editor list --refresh-catalog
```

Updates `~/.unity-cmd/cache/catalog-*.json` via CLI; do **not** write skill markdown copies.

### Example 4: one-shot Cursor prompt

```text
Follow @.cursor/skills/unity-cmd/SKILL.md: run unity-cmd --profile editor list to confirm compile/console exist,
then compile in the Unity Editor and show the last 20 error/warning console lines.
```

### Rules for agents

- Flags come from `list` / catalog JSON — never invent CLI flags; do not maintain skill markdown command caches.
- `play` / `stop` → profile **`editor`** only; runtime `echo` while playing → **`editor-play`**.

## Commands per instance

Use **`--profile <name>`** (or `UNITY_CMD_PROFILE`). Create profiles once:

```bat
unity-cmd profile create editor --host 127.0.0.1 --port 6547 --host-kind editor
unity-cmd profile create editor-play --port 6794 --host-kind editor_play
unity-cmd profile create package-play --port 6795 --host-kind player
unity-cmd profile list
```

### 1. Editor — Edit Mode (profile `editor`, port **6547**)

**When:** Unity Editor is open.
**Prerequisite:** Project has `com.air.unity-connector` installed; Console shows `http://127.0.0.1:6547/`.

```bat
unity-cmd --profile editor ping
unity-cmd --profile editor list
unity-cmd --profile editor state
unity-cmd --profile editor compile
unity-cmd --profile editor console --type error,warning --lines 20
unity-cmd --profile editor profiler --action enable
unity-cmd --profile editor play
unity-cmd --profile editor screenshot --view scene --output_path Screenshots/edit.png
```

### 2. Editor Play (profile `editor-play`, port **6794**)

**When:** Editor is in **Play Mode** (port listens only while playing).
Enter Play with `--profile editor` first.

```bat
unity-cmd --profile editor-play ping
unity-cmd --profile editor-play echo
unity-cmd --profile editor screenshot --view game --output_path Screenshots/play.png
unity-cmd --profile editor-play profiler --action status
```

```bat
unity-cmd --profile editor play
REM ... editor-play commands ...
unity-cmd --profile editor stop
```

### 3. Development Build player (profile `package-play`, port **6795**)

**When:** A **Development Build** is running (not Release).

```bat
unity-cmd --profile package-play ping
unity-cmd --profile package-play list
unity-cmd --profile package-play echo
```

**LAN:** On Unity, set `UNITY_CMD_BIND=lan` (or `UNITY_CMD_LAN=1`). On another machine, point the profile at the LAN IP:

```bat
unity-cmd profile create phone-play --host 192.168.1.50 --port 6794 --host-kind editor_play
```

### Integration tests (from `unity-cmd/`)

```bat
set UNITY_CMD_PROFILE=editor
set UNITY_CMD_WORKSPACE=C:\Project\GameDemo
npm run test:integration

set UNITY_CMD_PROFILE=package-play
set UNITY_CMD_SCENARIO=player-runtime
npm run test:integration
```

| Profile (example) | `connector_host` | Port | Use |
|-------------------|------------------|------|-----|
| `editor` | `editor` | 6547 | Edit Mode, deferred commands, enter/exit Play |
| `editor-play` | `editor_play` | 6794 | During Editor Play |
| `package-play` | `player` | 6795 | Development Build |

Details: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#runtime--play-mode-http), [unity-cmd/README.md](unity-cmd/README.md#commands-per-instance), [com.air.unity-connector/docs/IMPLEMENTATION.md](com.air.unity-connector/docs/IMPLEMENTATION.md#runtime--play-mode-stack).

## Environment

| Variable | Side | Purpose |
|----------|------|---------|
| `UNITY_CMD_PROFILE` | CLI | Default profile name (`--profile` overrides) |
| `UNITY_CMD_WORKSPACE` | CLI | Integration tests: Unity project root (`Assets/` writes, `assertFile`, `wait` heartbeat `projectPath` match). Omit for daily `ping`/`compile`. |
| `UNITY_CMD_SCENARIO` | CLI | Integration tests: scenario name |
| `UNITY_CMD_TIMEOUT_MS` | CLI | Timeout (default `20000`) |
| `UNITY_CMD_TOKEN` | CLI + Unity | Optional shared auth token |
| `UNITY_CMD_PORT` | Unity | Editor HTTP port (default `6547`) |
| `UNITY_CMD_EDITOR_PLAY_PORT` | Unity | Editor Play HTTP port (default `6794`) |
| `UNITY_CMD_PLAYER_PORT` | Unity | Dev player HTTP port (default `6795`) |
| `UNITY_CMD_BIND` | Unity | `loopback` (default), `lan`, or `any` (URL ACL) |
| `UNITY_CMD_LAN` | Unity | Set to `1` = same as `UNITY_CMD_BIND=lan` |
| `UNITY_CMD_ADVERTISE_HOST` | Unity | LAN: IP advertised in `/health` endpoints |

## Tests

```bash
cd unity-cmd
npm run verify              # unit tests, no Unity
npm run test:integration    # needs Editor open; skips if no instance
```

## Documentation

| Doc | |
|-----|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Design and request flow |
| [docs/AGENTS.md](docs/AGENTS.md) | Automation quick reference |
| [docs/DOC_GOVERNANCE.md](docs/DOC_GOVERNANCE.md) | Doc workflow (links meta AirUnityPackage) |
| [unity-cmd/docs/IMPLEMENTATION.md](unity-cmd/docs/IMPLEMENTATION.md) | CLI internals |
| [com.air.unity-connector/docs/IMPLEMENTATION.md](com.air.unity-connector/docs/IMPLEMENTATION.md) | HTTP API and parameters |
