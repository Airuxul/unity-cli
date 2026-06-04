# unity-cmd

[English](README.md)

通过 HTTP 向 [unity-connector](../com.air.unity-connector/) 发送命令的 Node.js 命令行工具。

**版本：** 0.1.0

## 安装

```bash
cd unity-cmd
npm install
npm link   # 可选
```

## Profile

远程命令必须指定 profile：

```bash
unity-cmd profile create editor --host 127.0.0.1 --port 6547 --host-kind editor
unity-cmd profile create editor-play --port 6794 --host-kind editor_play
unity-cmd profile create package-play --port 6795 --host-kind player
unity-cmd profile list
```

或设置 `UNITY_CMD_PROFILE=editor` 省略每次的 `--profile`。

## 用法

```bash
unity-cmd help
unity-cmd --profile editor help
unity-cmd --profile editor ping
unity-cmd --profile editor list

unity-cmd --profile editor play
unity-cmd --profile editor-play echo
unity-cmd --profile editor compile
unity-cmd --profile editor help
```

命令、别名与参数说明来自 Unity `POST /list`，缓存在 `~/.unity-cmd/cache/`。在线执行 `help` 时会强制拉取最新 catalog，并在每个命令下显示 `params` 行。

**仅本地（无需 profile）：** `help`、`profile …`

**远程（需要 profile）：** `ping`、`list` 及所有 connector 命令。

## 各实例

| Profile | 端口 | 场景 |
|---------|------|------|
| `editor` | 6547 | Editor 打开（编译、进出场 Play 等） |
| `editor-play` | 6794 | Editor 播放中 |
| `package-play` | 6795 | Development Build 运行中 |

默认端口定义见 [`src/constants.js`](src/constants.js)（`doc:check` 会校验 README 与之一致）。

`play` / `stop` 始终使用 profile **`editor`**。

完整示例：[../README.zh-CN.md#各实例命令](../README.zh-CN.md#各实例命令)。

## 环境变量

| 变量 | 说明 |
|------|------|
| `UNITY_CMD_PROFILE` | 默认 profile 名 |
| `UNITY_CMD_WORKSPACE` | 集成测试：工程根（`Assets/` 步骤、`assertFile`、`wait` 与实例 `projectPath` 对齐） |
| `UNITY_CMD_TIMEOUT_MS` | 默认超时（20000） |

## 测试

```bash
npm run verify
npm run test:integration
```

## 参见

- [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md)
- [../docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md)
