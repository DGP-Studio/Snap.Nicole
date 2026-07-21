**已随 Harness 接入，部分已有产品化控制面**
1. `FunctionInvokingChatClient`
   Harness 默认启用自动 function/tool invocation。当前实际传入的内置工具是 `BuiltInFunctions.GetCurrentTime` 和 `BuiltInFunctions.ShowSummary`，并通过 `ApprovalRequiredAIFunction` 要求逐次审批。
   已完成：`MaximumIterationsPerRequest` 已有全局设置入口，用于限制单个用户消息轮次中的工具调用循环。
   后续：工具调用日志、失败重试策略、工具执行耗时/错误状态展示。

2. `ToolApprovalAgent`
   当前 `DisableToolApproval = false`，内置工具已进入 Harness 审批流程；UI 已有 pending approval 展示、允许/拒绝命令，并把审批结果合并回原始 request 气泡。
   已完成：基础审批消息 UI、允许/拒绝命令、pending request 阻止普通输入、过期 request 防护。
   后续：工具调用审计、审批取消/失败状态、面向高风险工具的默认策略。不要把第一阶段扩成永久规则管理；如果以后接 Shell/File/Web/Skills，再单独评估“不要再问”和按工具类型策略。

3. `MessageInjectingChatClient`
   Harness 管线里有“运行中注入消息”的能力，但当前没有 UI/API 使用它。
   建议：可用于用户中途补充指令、系统级中断提示、外部事件注入；需要先定义注入消息是否持久化、是否显示、是否参与标题/统计。

4. `CompactionProvider` / context-window compaction
   Harness 内建基于 context window 的压缩。当前已有 `MaxContextWindowTokens` 设置；未显式设置时才从 `MaxInputTokens + MaxOutputTokens` 推导 context window。
   已完成：设置页已区分“上下文窗口长度”“最大输入长度”“最大输出长度”。
   后续：加调试信息显示压缩发生次数、压缩前后 token 估计，以及实际触发压缩时的可观测事件。

**Harness 支持但当前显式关闭**
1. `HostedWebSearchTool`
   能自动给 agent 增加 Web 搜索工具。
   建议第二阶段接。仓库已有 `ObservableWebSearchToolCallContent` / `ObservableWebSearchToolResultContent` 和对应基础模板，但 `ObservableAIContent.Create(...)` 目前还没有把普通 `WebSearchToolCallContent` / tool result 转成 observable 的入口；接入时需要补转换链路、搜索结果渲染、引用展示、网络错误、成本提示和 provider capability gating。

2. `TodoProvider`
   能让 agent 管理任务列表。
   建议和 AgentMode 一起接，风险低、收益高。UI 可做 conversation 侧边任务面板，持久化到 session，不必先做全局任务系统。

3. `AgentModeProvider`
   能提供 plan/execute 等模式状态。
   建议用于约束工具权限：plan 模式只读，execute 模式才允许写文件或跑命令。UI 需要显示当前模式并支持用户重置/切换。

4. `FileMemoryProvider`
   文件型 session memory。
   建议先做受控存储根目录、conversation 隔离、配额、清理和隐私开关。不要使用默认 `{cwd}/agent-file-memory`。

5. `FileAccessProvider`
   共享文件访问。
   建议比 FileMemory 晚接。必须有 allowlist root、路径边界校验、读写审计、覆盖确认。写/删权限应独立于读权限。

6. `AgentSkillsProvider`
   skill 发现和加载。
   建议先只允许内置/受信任 skill source；脚本 skill 需要签名/来源显示、超时、沙箱、日志和用户启停。不要默认扫描当前工作目录。

**Harness 支持但当前没有配置入口**
1. `BackgroundAgents`
   可把一组后台 agent 暴露给主 agent 委派任务。
   建议在任务状态、取消机制、结果归档和高风险工具审批策略稳定后再做。每个 background agent 需要唯一名称、独立 session、进度 UI、结果归档。

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
   已有 `ObservableMcpServerToolCallContent` / `ObservableMcpServerToolResultContent` 和基础模板。
   建议未来接 MCP server 管理 UI、工具列表、权限和结果渲染；同时补齐普通 tool call/result 到 observable 的转换入口。

2. Image generation / code interpreter
   已有对应 observable content 类型和基础模板。
   建议作为 provider-specific 能力接，不要混进基础 chat flow；需要专门的 artifact UI，并补齐普通 tool call/result 到 observable 的转换入口。

3. Input request / input response
   已有 `ObservableInputRequestContent` / `ObservableInputResponseContent`，其中 tool approval 已经复用了这条内容模型。
   建议后续复用给人机确认、补充参数请求；先统一交互协议，再分别接具体功能。

建议实际路线：WebSearch -> Todo/Mode -> FileMemory -> FileAccess -> Skills -> BackgroundAgents -> ShellExecutor
