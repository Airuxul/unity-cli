# 待办 — `unity-cli`（连接器 + CLI）

**最后更新：** 2026-06-03 · **范围：** Unity 自动化栈现有功能的后续优化（中文）

> **仓库结构：** `com.air.unity-connector`（UPM）+ `unity-cmd`（Node CLI）。  
> **不负责：** `GameRuntime` 组装、产品 UI、在 game-core 重复 HTTP。  
> Agent 英文条目：[`docs/TODO.md`](docs/TODO.md)

---

## `com.air.unity-connector`（CONN-）

### 现有能力概要

- 本机 HTTP：`editor` :6547、`editor_play` :6794、`player` :6795
- `POST /list`、`POST /command`、`GET /commands/{id}`、`GET /health`
- `InvokeCatalog`、主线程调度、域重载后任务账本
- `CliCommand` 发现（非 game-core `ICommand`）

### 待办列表

| ID | 优先级 | 标题 | 说明 |
|----|--------|------|------|
| CONN-01 | P0 | 修复 `profiler` 参数转型 | `--frames`、`--from`–`--to` 已知缺陷。 |
| CONN-02 | P1 | README 版本同步 | README 1.1.0 与 `package.json` 2.3.0 不一致。 |
| CONN-03 | P1 | `UNITY_CMD_TOKEN` 加固 | 头校验与统一失败 JSON。 |
| CONN-04 | P1 | 重载后任务审计 | 孤儿任务 / `EditorJobLedger` 无再派发。 |
| CONN-05 | P2 | `ConnectorBuild.Id` 配对 | 与 CLI `MIN_CONNECTOR_BUILD` 同步 bump。 |
| CONN-06 | P2 | Release 剥离 Player HTTP | 确认 Release 无 :6795。 |

### 请勿在本包实现

| 主题 | 归属 |
|------|------|
| Node 配置、目录缓存、argv | `unity-cmd` |
| `GameRuntime`、实体、UI | `com.air.unity-game-core` / `com.air.unity-ui` |
| GoF 撤销命令 | `com.air.game-core` |

---

## `unity-cmd`（CMD-）

### 现有能力概要

- 配置目录 `~/.unity-cmd/profiles/`
- 自 `POST /list` 缓存目录；`/health` 版本失效
- 编辑器就绪：`~/.unity-cmd/instances/*.json`（SSOT）+ `/health`；`editor-http.json` 仅调试
- 集成场景：编辑器生命周期、编译恢复、Player 运行时

### 待办列表

| ID | 优先级 | 标题 | 说明 |
|----|--------|------|------|
| CMD-01 | P0 | 子模块完整性 | 稀疏检出须含 `src/`、`bin/`、单元测试。 |
| CMD-02 | P1 | 集成 CI 清理 | `compile-error-recovery` 运行前清理临时 `.cs`。 |
| CMD-03 | P1 | CI 集成矩阵 | Editor + 开发版 Player 可用时跑全场景。 |
| CMD-04 | P1 | 构建 ID 锁步 | 协议变更时 `MIN_CONNECTOR_BUILD` ↔ `ConnectorBuild.Id`。 |
| CMD-05 | P2 | 局域网配置文档 | `UNITY_CMD_BIND=lan` 与远程 `editor_play`。 |
| CMD-06 | P2 | 无 Unity 单元测试 | 目录过期、作用域不匹配、轮询超时等。 |

### 请勿在本包实现

| 主题 | 归属 |
|------|------|
| `HttpListener`、处理器、完成策略 | `com.air.unity-connector` |
| Unity 主线程 API 执行 | 连接器调度器 |
