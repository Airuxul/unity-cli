# unity-cmd

Node.js CLI that sends commands to [unity-connector](../unity-connector/) over HTTP.

**Version:** 0.1.0

## Install

```bash
cd unity-cmd
npm install
npm link   # optional
```

## Usage

```bash
unity-cmd ping
unity-cmd list
unity-cmd editor.play
unity-cmd echo.editor --message hello
unity-cmd recompile          # recommended after editing unity-connector (120s job timeout)
unity-cmd compile            # same as recompile
unity-cmd console --type error,warning --lines 20
unity-cmd logs               # alias → editor.console
unity-cmd menu --menu_path "File/Save Project"
unity-cmd screenshot --view game --output_path Screenshots/game.png
unity-cmd refresh --compile true   # AssetDatabase refresh + compile job
unity-cmd exec --code "return 1+1;"
unity-cmd profiler --action hierarchy --max 10
unity-cmd manage --action pause
unity-cmd reserialize              # entire project (slow)
```

Commands and aliases come from Unity (`POST /list`), cached under `~/.unity-cmd/cache/`. Local-only: `ping`, `list`, `help`.

## Environment

| Variable | Description |
|----------|-------------|
| `UNITY_CMD_PROJECT` | Select instance by project path or folder name |
| `UNITY_CMD_HOST` | Override host |
| `UNITY_CMD_PORT` | Override port |
| `UNITY_CMD_TIMEOUT_MS` | Default timeout (20000) |

## npm scripts

| Script | Description |
|--------|-------------|
| `npm run verify` | Unit tests + documentation version check |
| `npm run test:unit` | Node unit tests only |
| `npm run test:integration` | Full lifecycle against an open Editor (skips if none) |
| `npm run test:all` | `verify` then `test:integration` |
| `npm run doc:check` | Sync doc `Version:` headers with package.json |

## Integration tests

1. Install `unity-connector` in your Unity project and open it in the Editor.
2. Set `UNITY_CMD_PROJECT` to your project path when multiple Editors are open.
3. Run `npm run test:integration`.

Scenario `tests/integration/scenarios/full-lifecycle.json` covers catalog, console, play/stop, runtime echo, connector state, and compile.

See [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md) and [../docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md).
