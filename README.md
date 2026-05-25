# unity-cli

Monorepo for controlling Unity Editor from the command line.

| Project | Description |
|---------|-------------|
| [unity-cmd](unity-cmd/) | Node.js CLI — sends HTTP commands to the connector |
| [unity-connector](unity-connector/) | Unity UPM package — HTTP server and command router inside the Editor / Player |

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for design goals and completion checklist.

## Install the connector

Add to your Unity project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.airuxul.unity-connector": "file:../../unity-cli/unity-connector"
  }
}
```

Adjust the path to where you cloned this repo. Open the project in Unity once so the connector starts and writes a heartbeat file.

## CLI quick reference

```bash
cd unity-cmd
npm install
npm link   # optional: global `unity-cmd` command

unity-cmd ping
unity-cmd list                    # command catalog from Unity (cached)
unity-cmd recompile               # script compile job (120s default)
unity-cmd console --type error,warning --lines 20
unity-cmd logs --lines 10         # alias of console
unity-cmd editor.play
unity-cmd menu --menu_path "File/Save Project"
unity-cmd screenshot --view game
unity-cmd exec --code "return 42;"
unity-cmd profiler --action status
unity-cmd manage --action stop
```

After changing **unity-connector** sources, reload scripts in the open Editor:

```bash
unity-cmd refresh --compile true --timeout 120000
# or: unity-cmd recompile
```

## Environment variables

| Variable | Description |
|----------|-------------|
| `UNITY_CMD_PROJECT` | Project path or name fragment to select an Editor instance |
| `UNITY_CMD_HOST` | Override host (default from heartbeat) |
| `UNITY_CMD_PORT` | Override port |
| `UNITY_CMD_TIMEOUT_MS` | Per-command timeout (default `20000`) |
| `UNITY_PROJECT_PATH` | External project path for Unity EditMode tests (UTF) |

## Tests

All npm scripts live under `unity-cmd/`:

```bash
cd unity-cmd
npm run verify              # unit tests + doc version check (no Unity)
npm run test:integration    # full lifecycle against an open Editor
npm run test:all            # verify + integration
```

Integration tests **do not** start Unity. Open your project in the Editor first, then run `test:integration`. If no instance is found within 20s, the run logs hints and exits `0` (skipped, not failed).

## Layout

```text
unity-cli/
├── README.md
├── docs/ARCHITECTURE.md
├── unity-cmd/
└── unity-connector/
```

There is no root `package.json` — run commands from `unity-cmd/`.
