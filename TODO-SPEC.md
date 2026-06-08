**已随 Harness 接入，部分已有产品化控制面**
1. `FunctionInvokingChatClient`
   Harness 默认启用自动 function/tool invocation。当前实际传入的工具只有 `BuiltInFunctions.GetCurrentTime`。
   已接入：`MaximumIterationsPerRequest` 作为全局应用配置保存在 `AppSettings.AgentOptions`，设置页“智能体全局配置”可调整，默认值为 8。
   后续：工具调用日志、失败重试策略和每轮调用上限。

2. `MessageInjectingChatClient`
   Harness 管线里有“运行中注入消息”的能力，但我们现在没有任何 UI/API 使用它。
   建议：可用于用户中途补充指令、系统级中断提示、外部事件注入；需要先定义注入消息是否持久化、是否显示、是否参与标题/统计。

3. `CompactionProvider` / context-window compaction
   Harness 内建基于 context window 的压缩。我们现在用 `MaxInputTokens + MaxOutputTokens` 推导 context window。
   建议：下一步把 `MaxInputTokens` 的 UI 文案改清楚，区分“输入预算”和“模型上下文窗口”；加调试信息显示压缩发生次数和压缩后 token 估计。

**Harness 支持但当前显式关闭**
1. `ToolApprovalAgent`
   能做工具审批和“不要再问”规则。
   建议优先接入。它是 Shell/File/Web/Skills 的安全前置条件。需要审批消息 UI、允许/拒绝命令、永久规则管理、按工具类型默认策略。

2. `HostedWebSearchTool`
   能自动给 agent 增加 Web 搜索工具。
   建议第二阶段接。仓库已有 `ObservableWebSearchToolCallContent` / `ObservableWebSearchToolResultContent`，但还需要搜索结果渲染、引用展示、网络错误、成本提示和 provider capability gating。

3. `TodoProvider`
   能让 agent 管理任务列表。
   建议和 AgentMode 一起接，风险低、收益高。UI 可做 conversation 侧边任务面板，持久化到 session，不必先做全局任务系统。

4. `AgentModeProvider`
   能提供 plan/execute 等模式状态。
   建议用于约束工具权限：plan 模式只读，execute 模式才允许写文件或跑命令。UI 需要显示当前模式并支持用户重置/切换。

5. `FileMemoryProvider`
   文件型 session memory。
   建议先做受控存储根目录、conversation 隔离、配额、清理和隐私开关。不要使用默认 `{cwd}/agent-file-memory`。

6. `FileAccessProvider`
   共享文件访问。
   建议比 FileMemory 晚接。必须有 allowlist root、路径边界校验、读写审计、覆盖确认。写/删权限应独立于读权限。

7. `AgentSkillsProvider`
   skill 发现和加载。
   建议先只允许内置/受信任 skill source；脚本 skill 需要签名/来源显示、超时、沙箱、日志和用户启停。不要默认扫描当前工作目录。

**Harness 支持但当前没有配置入口**
1. `BackgroundAgents`
   可把一组后台 agent 暴露给主 agent 委派任务。
   建议在 ToolApproval、任务状态、取消机制稳定后再做。每个 background agent 需要唯一名称、独立 session、进度 UI、结果归档。

2. `BackgroundAgentsProviderOptions`
   配置 background agents 的说明和列表格式。
   建议跟 `BackgroundAgents` 同步实现，不单独暴露。

3. `ShellExecutor`
   可提供 shell 环境探测，并注册 `run_shell` 工具。
   建议最后做，风险最高。必须有命令预览、逐次确认、工作目录边界、超时、输出截断、危险命令拦截、权限策略和完整日志。

4. `ShellEnvironmentProviderOptions`
   配置 shell 探测行为。
   建议作为 ShellExecutor 的高级设置，只在 shell 功能启用后出现。

5. `AIContextProviders`
   可挂自定义上下文提供器。
   建议用于接入应用内上下文：当前设置、Git 状态、Sentry issue、workspace 文件摘要、MCP 资源。每个 provider 都要有 token 预算和刷新时机。

6. `FileMemoryStore` / `FileAccessStore`
   可替换默认文件存储。
   建议实现 app-owned store：根目录来自设置服务，不依赖 process cwd；按 conversation/profile 分区。

7. `AgentModeProviderOptions`
   自定义 agent mode。
   建议初期用默认 plan/execute；等 UI 稳定后支持自定义 mode。

8. `AgentSkillsSource`
   自定义 skill source。
   建议先用显式注册 source，不做自动发现；后续可以加插件目录和用户目录。

9. `Id` / `Name` / `Description`
   Harness agent metadata。
   建议用 conversation/profile 派生，尤其 `Name` 对 background agents 和 handoff 很重要。

**非 Harness 但现有内容模型已经预留的能力**
1. MCP tool call/result
   已有 `ObservableMcpServerToolCallContent` / `ObservableMcpServerToolResultContent`。
   建议未来接 MCP server 管理 UI、工具列表、权限和结果渲染。

2. Image generation / code interpreter
   已有对应 observable content 类型。
   建议作为 provider-specific 能力接，不要混进基础 chat flow；需要专门的 artifact UI。

3. Input request / input response
   已有 `ObservableInputRequestContent` / `ObservableInputResponseContent`。
   建议可复用给 tool approval、人机确认、补充参数请求。先统一交互协议，再分别接具体功能。

建议实际路线：ToolApproval UI -> Todo/Mode -> WebSearch -> FileMemory -> FileAccess -> Skills -> BackgroundAgents -> ShellExecutor
