# Agent Workspace Implementation Plan

本文档配套 `TODO-SPEC.md`，目标是给 Harness agent 引入明确的 workspace 概念，同时支持两种使用场景：

1. app 管理的默认工作目录，用于普通对话、临时文件和内部工具产物
2. 用户显式选择的外部项目目录，例如某个 Java 项目，用于编程任务

核心原则：不要用 `Directory.SetCurrentDirectory(...)` 或依赖 Harness 默认 `{cwd}`。工作目录必须是 conversation 级状态，并显式传给 Harness 的 FileAccess/FileMemory/Shell/Skills 相关入口。

## 目标语义

### 默认 workspace

默认 workspace 由应用创建和管理，按 conversation 分区。

建议路径：

```text
{WellKnownLocations.Cache}\AgentWorkspaces\{conversationId:N}\working
{WellKnownLocations.Cache}\AgentWorkspaces\{conversationId:N}\memory
```

用途：

- `working`：普通对话中的临时文件、FileAccess 根目录
- `memory`：FileMemory 根目录，始终由应用管理

### 外部项目 workspace

外部项目 workspace 由用户显式选择目录，例如：

```text
D:\Projects\FooJavaApp
```

用途：

- `FileAccessStore` 根目录指向该项目目录
- Shell 工作目录指向该项目目录
- FileMemory 仍使用 app-managed `memory` 根目录，不写入外部项目

这样 agent 可以读写项目文件，但长期记忆、内部状态和工具草稿不会污染用户项目目录。

## 数据模型

新增 conversation 级 workspace 配置，放在 `AgentConversation`，跟 `SerializedSessionState` 同级。

建议类型：

```csharp
internal enum AgentWorkspaceKind
{
    AppManaged,
    ExternalFolder,
}

internal sealed class AgentConversationWorkspace
{
    public AgentWorkspaceKind Kind { get; set; }

    public string? ExternalFolderPath { get; set; }
}
```

持久化规则：

- 新 conversation 默认 `AppManaged`
- `ExternalFolderPath` 只在 `Kind == ExternalFolder` 时有效
- 加载旧 conversation 时缺省为 `AppManaged`
- 不把派生出来的完整 `working` / `memory` 路径写入 JSON，避免机器路径漂移

如果需要最近使用目录，后续可在 `AppAgentOptions` 增加 recent workspace 列表；当前选择仍归属于 conversation，不做全局当前目录。

## Workspace 服务

新增一个小服务负责解析和创建目录，例如 `AgentWorkspaceProvider`。

职责：

- 根据 `AgentConversation.Id` 和 `AgentConversationWorkspace` 生成运行时 snapshot
- 创建 app-managed `working` / `memory` 目录
- 校验 external folder 是否存在、是否为目录、是否可访问
- 对外只返回已校验的绝对路径

建议运行时对象：

```csharp
internal sealed record AgentWorkspaceSnapshot
{
    public required AgentWorkspaceKind Kind { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string MemoryDirectory { get; init; }

    public string? ExternalFolderPath { get; init; }
}
```

路径策略：

- `WorkingDirectory`：FileAccess 和 Shell 使用
- `MemoryDirectory`：FileMemory 使用
- external folder 模式下，`WorkingDirectory == ExternalFolderPath`
- app-managed 模式下，`WorkingDirectory` 是 `Cache\AgentWorkspaces\{id}\working`

## Runtime 装配

当前 agent 创建路径大致是：

```text
AgentConversationTurnController
-> AgentConversationRuntimeController.EnsureConversationAgentAsync(...)
-> IAgentService.CreateAgentAsync(...)
-> ExtendedAgentOptions.CreateHarnessAgent(...)
```

需要把 workspace snapshot 作为 runtime 输入传下去：

```csharp
ValueTask<HarnessAgent> CreateAgentAsync(ExtendedAgentOptions options, AgentWorkspaceSnapshot workspace, CancellationToken cancellationToken = default);
```

`AgentConversationRuntimeController.EnsureConversationAgentAsync(...)` 应同时比较：

- `ExtendedAgentOptions.AgentEquals(...)`
- workspace 是否影响 agent 实例

至少以下变化要重建 agent：

- app-managed 切换为 external folder
- external folder path 改变
- 启用 FileAccess/FileMemory/Shell 后，相关开关改变

## Harness options 映射

### 第一阶段：只建立 workspace，不打开危险工具

第一阶段只打通数据模型、UI、路径解析和 runtime 参数，不启用 Shell。

可保持：

```csharp
DisableFileMemory = true,
DisableFileAccess = true,
DisableAgentSkillsProvider = true,
ShellExecutor = null,
```

这样先验证 workspace 生命周期和持久化，不引入文件写入和命令执行风险。

### 第二阶段：启用 FileMemory

FileMemory 应始终指向 app-managed `MemoryDirectory`。

```csharp
FileMemoryStore = new FileSystemAgentFileStore(workspace.MemoryDirectory);
DisableFileMemory = false;
```

不允许 FileMemory 直接写外部项目目录。

### 第三阶段：启用 FileAccess

FileAccess 指向 `WorkingDirectory`。

```csharp
FileAccessStore = new FileSystemAgentFileStore(workspace.WorkingDirectory);
DisableFileAccess = false;
```

注意：

- `FileSystemAgentFileStore` 会把相对路径限制在 root 内
- 仍需要 UI 告知用户 external folder 模式下 agent 可读写项目文件
- 写入和删除最好先通过 ToolApproval UI 明确展示目标路径

### 第四阶段：启用 ShellExecutor

Shell 最后接，因为 cwd 不是安全边界。

```csharp
ShellExecutor = new LocalShellExecutor(new LocalShellExecutorOptions
{
    WorkingDirectory = workspace.WorkingDirectory,
    ConfineWorkingDirectory = true,
    Timeout = LocalShellExecutor.DefaultTimeout,
});
```

必须同时具备：

- 每次命令执行前审批
- 命令预览
- 工作目录显示
- 超时
- 输出截断
- 危险命令拒绝或强提示
- Sentry 日志和审计信息

Shell persistent 模式要求一个 executor 只归一个 conversation/session 所有。接入 Shell 时，`AgentConversationRuntime` 需要持有可释放资源，并在 reset、conversation 删除、app shutdown 时 dispose 旧 executor。

## UI 计划

在 Agent 页当前 conversation 区域增加 workspace 选择入口。

基础控件：

- 当前 workspace 显示
- 选择文件夹
- 打开文件夹
- 重置为默认

显示文本建议：

- app-managed：`默认工作目录`
- external folder：显示目录名和完整路径 tooltip

外部目录选择流程：

1. 用户点击选择文件夹
2. 使用 Windows folder picker 选择目录
3. 显示确认提示：agent 将能在此目录内读取和修改文件，Shell 启用后命令也会从此目录运行
4. 保存到当前 conversation
5. reset 当前 runtime，使后续 turn 使用新 workspace

不要把 workspace 选择做成全局设置页里的唯一开关；它首先是 conversation 级上下文。

## 安全和权限

必须区分三类权限：

1. FileMemory：只允许 app-managed memory root
2. FileAccess：允许在 selected working root 内读写
3. Shell：可以从 working root 启动，但命令本身可能访问 root 外路径

所以：

- FileAccess 可以依赖 `FileSystemAgentFileStore` 做路径边界
- Shell 不能只依赖 cwd，必须保留 ToolApproval
- external folder 模式下，默认不自动启用 Shell
- 高风险命令需要明确审批，不做“不要再问”的第一阶段

## 清理策略

app-managed workspace：

- 删除 conversation 时，可删除对应 `Cache\AgentWorkspaces\{id}` 目录
- 如果删除失败，只记录 telemetry，不阻塞 conversation 删除
- 可后续增加孤儿目录清理：扫描无对应 conversation 的 workspace

external folder：

- 永不自动删除用户目录
- 只删除 app-managed memory root
- reset workspace 为默认时，不修改原外部目录内容

## 实施阶段

### Phase 1: Workspace model and persistence

- 新增 `AgentWorkspaceKind`
- 新增 `AgentConversationWorkspace`
- 在 `AgentConversation` 增加 `Workspace`
- 更新 factory / copy / persistence 流程
- 确认旧 conversation JSON 可加载

验证：

- 新旧 JSON round-trip
- 切换 workspace 后 conversation 能保存和恢复

### Phase 2: Workspace provider

- 新增 `AgentWorkspaceProvider`
- 使用 `WellKnownLocations.Cache` 生成 app-managed root
- 校验 external folder
- 提供 `AgentWorkspaceSnapshot`

验证：

- app-managed 路径稳定
- external folder 不存在时有明确错误
- 不写入派生绝对路径到 conversation JSON

### Phase 3: Runtime plumbing

- `IAgentService.CreateAgentAsync(...)` 增加 workspace 参数
- `AgentConversationRuntimeController` 在创建 agent 前解析 workspace
- workspace 变化时 reset runtime
- 暂不启用 FileAccess/FileMemory/Shell

验证：

- 普通聊天行为不变
- 切换 workspace 不影响已有消息
- missing API key 等 pre-stream failure 仍留在 VM/turn boundary

### Phase 4: Workspace UI

- Agent 页显示当前 workspace
- 支持选择 external folder
- 支持重置默认
- 支持打开当前 folder
- 选择 external folder 时显示确认提示

验证：

- UI 状态随 current conversation 切换
- 删除/切换 conversation 不误用上一个 workspace
- external folder path 展示不挤压输入区

### Phase 5: FileMemory

- 增加启用开关或内部实验开关
- `FileMemoryStore` 指向 app-managed `MemoryDirectory`
- 明确 memory root 和 working root 不同

验证：

- external folder 模式下不会在项目目录创建 memory 文件
- session serialize/deserialize 不丢 FileMemory 状态

### Phase 6: FileAccess

- 增加启用开关
- `FileAccessStore` 指向 `WorkingDirectory`
- 对写入/删除类操作接 ToolApproval 展示目标路径

验证：

- app-managed 模式只能访问 app workspace
- external folder 模式只能通过 FileAccess 访问选中 root 内路径
- `..`、绝对路径、symlink/reparse point 边界行为符合 `FileSystemAgentFileStore`

### Phase 7: ShellExecutor

- 新增 per-conversation shell runtime resource
- `LocalShellExecutorOptions.WorkingDirectory = workspace.WorkingDirectory`
- 保持 `ConfineWorkingDirectory = true`
- 接入命令审批、预览、审计、输出截断和超时
- reset/runtime dispose 时释放 executor

验证：

- 每个 conversation 有独立 shell
- 切换 workspace 后旧 shell 被释放
- 命令审批气泡显示 cwd 和 command
- 取消/异常不会留下忙状态

### Phase 8: Skills and future providers

- 不默认扫描 current directory
- 使用显式 `AgentSkillsSource`
- 只允许内置或受信任 skill source
- 脚本 skill 需要来源显示、超时、日志和启停控制

## Open questions

1. external folder 是否允许网络盘、UNC 路径、OneDrive 同步目录
2. app-managed workspace 是否应放在 `Cache`，还是用户可配置的 data root
3. FileAccess 写入/删除是否需要统一的 diff preview
4. Shell 是否优先支持 Docker executor，而不是 host local shell
5. 是否需要 conversation 级 recent workspace，还是全局 recent list 足够
