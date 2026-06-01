# unity-cli-command

[English](README.md)

通过本机 HTTP 从命令行控制 Unity Editor，面向脚本、CI 与 Agent。

| 子项目 | 职责 |
|--------|------|
| [unity-cmd](unity-cmd/) | Node.js CLI |
| [unity-connector](unity-connector/) | Unity UPM 桥接（HTTP + 命令） |

## 工作原理

```text
unity-cmd  →  --profile  →  GET /health  →  POST /command、/jobs
              unity-connector（Editor :6547 / Play :6794 / Player :6795）
```

- 命令目录由 Unity 提供（`POST /list`），CLI 解析别名并按工程缓存。
- 长任务（`compile`、`play` 等）返回 **HTTP 202**，CLI 轮询 `GET /commands/{id}`。
- 失败输出 JSON：`ok`、`error_code`、可选 `hint`。

内置：进出场、控制台、exec、profiler、截图、菜单、重序列化等。扩展见 [unity-connector/README.zh-CN.md](unity-connector/README.zh-CN.md)。

## 快速开始

```bash
cd unity-cmd && npm install && npm link   # 可选

unity-cmd profile create editor --host 127.0.0.1 --port 6547 --host-kind editor
unity-cmd --profile editor ping
unity-cmd --profile editor list
unity-cmd --profile editor play
unity-cmd --profile editor screenshot --view game --output_path Screenshots/game.png
unity-cmd --profile editor stop
```

Connector 安装与打开工程：[unity-connector/README.zh-CN.md](unity-connector/README.zh-CN.md)。  
CLI 参数与 npm 脚本：[unity-cmd/README.zh-CN.md](unity-cmd/README.zh-CN.md)。

修改 connector 源码后：`unity-cmd --profile editor compile`（别名 `recompile`）或 `unity-cmd --profile editor refresh --compile true --timeout 30000`。

端口固定：**Editor `6547`**、**Editor Play `6794`**、**Player `6795`**，三端可同时运行互不冲突。

## AI / Agent 使用

本仓库在 **`docs/unity-cmd-skill/`** 提供可提交的 [Cursor Agent Skill](https://cursor.com/docs/skills) 源文件。`.cursor/` 通常不纳入 Git，需在本机安装一次。

### 安装 Skill（必做）

将 **`docs/unity-cmd-skill`** 整个文件夹复制为：

```text
<仓库根>/.cursor/skills/unity-cmd/
```

**目标文件夹名必须为 `unity-cmd`**（与 `SKILL.md` 中 `name: unity-cmd` 一致）。

```powershell
# Windows PowerShell（在仓库根目录执行）
New-Item -ItemType Directory -Force -Path .cursor\skills | Out-Null
Copy-Item -Recurse -Force docs\unity-cmd-skill .cursor\skills\unity-cmd
```

```bash
# macOS / Linux
mkdir -p .cursor/skills
cp -R docs/unity-cmd-skill .cursor/skills/unity-cmd
```

说明详见 [docs/unity-cmd-skill/README.md](docs/unity-cmd-skill/README.md)。

### 仓库内 Skill 目录结构

```text
docs/unity-cmd-skill/           # Git 中的源（复制到 .cursor/skills/unity-cmd/ 后由 Cursor 加载）
  SKILL.md
  references/guide.zh-CN.md
```

### 接入方式

| 方式 | 说明 |
|------|------|
| 自动加载 | 安装后，讨论 Unity 自动化时 Cursor 加载 `.cursor/skills/unity-cmd/` |
| `/unity-cmd` | 聊天输入斜杠命令 |
| `@` 引用 | `@.cursor/skills/unity-cmd/SKILL.md`（需先安装）；或直接 `@docs/unity-cmd-skill/SKILL.md` 阅读规范 |
| 完整指南 | [docs/unity-cmd-skill/references/guide.zh-CN.md](docs/unity-cmd-skill/references/guide.zh-CN.md) |
| 英文速查 | [docs/AGENTS.md](docs/AGENTS.md) |

### 默认逻辑（用户说「在 Unity 编辑器 / 运行时做 xxx」）

```text
1. 判定域 → 选 profile（editor / editor-play / package-play）
2. unity-cmd --profile <名> list → 在 catalog 的 commands 中匹配用户意图
3. 有 catalog 但未命中 → list --refresh-catalog → 再匹配
4. 仍无匹配 → 说明无此能力、列 2～5 条相关命令名 → 中断；不臆造命令
5. ping 成功 → 执行已匹配命令
```

命令目录由 CLI 写入 `~/.unity-cmd/cache/catalog-<host>_<port>.json`，**不要**在 Skill 内维护 markdown 副本。刷新目录：`unity-cmd --profile <名> list --refresh-catalog`。

### 范例 1：改脚本后检查编译

**用户：**「我刚改了 C#，帮你看 Unity 有没有编译错误。」

**Agent：**

1. `unity-cmd --profile editor list`，在 `commands` 中确认有 `compile`、`console`。
2. 无匹配则按 Skill 中断；勿臆造命令。
3. 执行：

```bash
unity-cmd --profile editor ping
unity-cmd --profile editor compile --timeout 60000
unity-cmd --profile editor console --type error,warning --lines 30
```

4. 根据 JSON 的 `ok`、`error_code`、`hint` 汇报；`compile` 失败则不要继续 Play。

### 范例 2：进 Play 并截图

**用户：**「进入播放模式并截一张 Game 视图。」

**Agent：** 域为 **editor**（`play` / `screenshot` / `stop` 均用 profile `editor`）：

```bash
unity-cmd --profile editor ping
unity-cmd --profile editor play --timeout 90000
unity-cmd --profile editor screenshot --view game --output_path Screenshots/agent-play.png
unity-cmd --profile editor stop
```

若用户还要「播放中跑 runtime echo」：`unity-cmd --profile editor-play list`，再用 `editor-play` profile。

### 范例 3：刷新命令目录（CLI catalog）

**用户：**「更新一下 Unity 编辑器命令缓存。」

**Agent：**

```bash
unity-cmd --profile editor list --refresh-catalog
```

由 CLI 更新 `~/.unity-cmd/cache/catalog-*.json`；**不要**在 Skill 目录写 markdown 副本。

### 范例 4：在 Cursor 里一次性启用 Skill

在 Agent 对话开头发送：

```text
请按 @.cursor/skills/unity-cmd/SKILL.md 操作 Unity（若未安装 Skill，请先复制 docs/unity-cmd-skill）：先 unity-cmd --profile editor list 确认有 compile/console，
再帮我在编辑器里执行 compile 并读取最近 20 条 error/warning 控制台日志。
```

### Agent 注意

- 命令参数以 `list` / `~/.unity-cmd/cache/catalog-*.json` 为准，**禁止**臆造 flag；**禁止**在 Skill 内维护 markdown 命令副本。
- `play` / `stop` 仅 profile **`editor`**；播放中 runtime `echo` 用 **`editor-play`**。

## 各实例命令

使用 **`--profile <名称>`** 或 **`UNITY_CMD_PROFILE`**。先创建 profile：

```bat
unity-cmd profile create editor --host 127.0.0.1 --port 6547 --host-kind editor
unity-cmd profile create editor-play --port 6794 --host-kind editor_play
unity-cmd profile create package-play --port 6795 --host-kind player
unity-cmd profile list
```

### 1. Editor — 编辑模式（profile `editor`，端口 **6547**）

**时机：** Unity Editor 已打开。  
**前提：** 工程已装 `unity-connector`；Console 出现 `http://127.0.0.1:6547/`。

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

### 2. Editor Play — 播放模式（profile `editor-play`，端口 **6794**）

**时机：** Editor **正在播放**（仅 Play 时监听 6794）。  
先用 **`--profile editor`** 执行 `play`。

```bat
unity-cmd --profile editor-play ping
unity-cmd --profile editor-play echo
unity-cmd --profile editor screenshot --view game --output_path Screenshots/play.png
unity-cmd --profile editor-play profiler --action status
```

```bat
unity-cmd --profile editor play
REM ... editor-play 命令 ...
unity-cmd --profile editor stop
```

### 3. Development Build 包体（profile `package-play`，端口 **6795**）

**时机：** **Development Build** 可执行程序在运行（Release 无 HTTP）。

```bat
unity-cmd --profile package-play ping
unity-cmd --profile package-play list
unity-cmd --profile package-play echo
```

局域网：创建 profile 时把 `--host` 设为局域网 IP，例如：

```bat
unity-cmd profile create phone-play --host 192.168.1.50 --port 6794 --host-kind editor_play
```

### 集成测试（在 `unity-cmd/` 目录）

```bat
set UNITY_CMD_PROFILE=editor
set UNITY_CMD_WORKSPACE=C:\Project\GameDemo
npm run test:integration

set UNITY_CMD_PROFILE=package-play
set UNITY_CMD_SCENARIO=player-runtime
npm run test:integration
```

| Profile（示例） | `connector_host` | 端口 | 用途 |
|-----------------|------------------|------|------|
| `editor` | `editor` | 6547 | 编辑模式、延迟完成命令、进出场 Play |
| `editor-play` | `editor_play` | 6794 | Editor 播放中 |
| `package-play` | `player` | 6795 | Development Build |

详见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#runtime--play-mode-http)、[unity-cmd/README.zh-CN.md](unity-cmd/README.zh-CN.md#各实例命令)、[unity-connector/docs/IMPLEMENTATION.md](unity-connector/docs/IMPLEMENTATION.md#runtime--play-mode-stack)。

## 与 [youngwoocho02/unity-cli](https://github.com/youngwoocho02/unity-cli) 的差异

理念相近：都走本机 HTTP（不依赖 MCP）来驱动 Unity。**本仓库不是其 Fork**，而是从 CLI 交互、协议形态到扩展机制均独立设计与实现。

| | [youngwoocho02/unity-cli](https://github.com/youngwoocho02/unity-cli) | 本仓库 |
|---|------------------------------------------------------------------------|--------|
| CLI | Go 单二进制、`install.sh` | Node `unity-cmd`、`npm install` |
| 实例发现 | `~/.unity-cli/instances/` 心跳文件 | profile + `/health`（无心跳文件） |
| 命令写法 | `unity-cli editor play` | `unity-cmd play` |
| 发现 | 请求时反射；`list` 含参数 schema | `POST /list` 目录 + CLI 缓存 |
| 长任务 | 同步 HTTP + `--wait` | **202 延迟命令状态** + 轮询 |
| Runtime / 播放模式 | **仅 Editor 编辑模式** — 播放中无 HTTP，无包体端点 | **`editor_play` :6794** + **`player` :6795**；`CommandScope.Runtime`、播放中 Profiler/截图 |
| 状态 | `unity-cli status` | `ping`、`state` |
| 扩展 | `[UnityCliTool]` + `HandleCommand(JObject)` | `Descriptor` + 单一 `Run(...)` 命令类 + `[CliParam]` |
| 输出 | `success` / `message` | `ok`、`data`、`error_code`、`hint` |
| 其他 | 内置 `test`、`update` | 无（测试走 Unity/CI） |
| 编译延迟超时 | 120s | 30s（`compile` 默认） |

| 上游 | 本仓库 |
|------|--------|
| `unity-cli status` | `unity-cmd ping` |
| `unity-cli editor play` | `unity-cmd play` |
| `unity-cli exec "code"` | `unity-cmd exec --code "code"` |
| `unity-cli profiler hierarchy` | `unity-cmd profiler --action hierarchy` |
| `unity-cli editor refresh --compile` | `unity-cmd refresh --compile true` |

## 环境变量

| 变量 | 用途 |
|------|------|
| `UNITY_CMD_PROFILE` | 默认 profile 名称（也可用 `--profile`） |
| `UNITY_CMD_WORKSPACE` | 仅集成测试：Unity 工程根目录（截图文件断言） |
| `UNITY_CMD_TIMEOUT_MS` | 超时（默认 `20000`） |
| `UNITY_CMD_TOKEN` | 可选鉴权（与 Unity 侧一致） |

Unity 侧端口环境变量（创建 profile 时对应默认端口）：`UNITY_CMD_PORT`、`UNITY_CMD_EDITOR_PLAY_PORT`、`UNITY_CMD_PLAYER_PORT`。
| `UNITY_CMD_EDITOR_PLAY_PORT` | Editor Play HTTP 端口（默认 `6794`） |
| `UNITY_CMD_BIND` | `loopback`（默认）、`lan`（局域网）、`any`（需 URL ACL） |
| `UNITY_CMD_LAN` | 设为 `1` 等同 `UNITY_CMD_BIND=lan` |
| `UNITY_CMD_ADVERTISE_HOST` | LAN 模式下 `/health` endpoints 对外 IP（如 `192.168.1.10`） |
| `UNITY_CMD_TOKEN` | 可选鉴权；CLI 与 Unity 需一致 |
| `UNITY_CMD_TIMEOUT_MS` | 超时（默认 `20000`） |

## 测试

```bash
cd unity-cmd
npm run verify              # 单元测试，无需 Unity
npm run test:integration    # 需已打开 Editor；无实例则跳过
```

## 文档

| 文档 | |
|------|---|
| [docs/unity-cmd-skill/](docs/unity-cmd-skill/SKILL.md) | **Cursor Skill 源**（复制到 `.cursor/skills/unity-cmd/`） |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | 架构与请求流 |
| [docs/AGENTS.md](docs/AGENTS.md) | 自动化速查 |
| [unity-cmd/docs/IMPLEMENTATION.md](unity-cmd/docs/IMPLEMENTATION.md) | CLI 实现 |
| [unity-connector/docs/IMPLEMENTATION.md](unity-connector/docs/IMPLEMENTATION.md) | HTTP API 与参数 |
