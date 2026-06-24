using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using OpenAI.Chat;
using Snap.Nicole.Services.AI.Agent;
using Snap.Nicole.Services.AI.Agent.Workspace;
using Snap.Nicole.Services.AI.Compatibility.OpenAIChatCompletion;
using Snap.Nicole.Services.AI.Models;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Models;

internal sealed class ExtendedAgentOptions
{
    private const int DefaultMaxInputTokens = 204800;
    private const int DefaultMaxOutputTokens = 102400;

    public ModelProviderType ProviderType { get; init; } = ModelProviderType.OpenAIChatCompletion;

    public string? Endpoint { get; init; }

    public string? ApiKey { get; init; }

    public string ModelId { get; init; } = string.Empty;

    public float? Temperature { get; init; }

    public float? TopP { get; init; }

    public ReasoningEffort? ReasoningEffort { get; init; }

    public bool? ThinkingEnabled { get; init; }

    public bool OmitReasoningEffortWhenThinkingDisabled { get; init; }

    public int? MaxInputTokens { get; init; }

    public int? MaxContextWindowTokens { get; init; }

    public int? MaxOutputTokens { get; init; }

    public string? SystemPrompt { get; init; }

    public int? MaximumIterationsPerRequest { get; init; }

    public bool AgentEquals(ExtendedAgentOptions? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        // For any properties that are used in AsAgentRunOptions method, ignore them in comparison.
        return ProviderType == other.ProviderType
            && string.Equals(Endpoint, other.Endpoint, StringComparison.Ordinal)
            && string.Equals(ApiKey, other.ApiKey, StringComparison.Ordinal)
            && ThinkingEnabled == other.ThinkingEnabled
            && OmitReasoningEffortWhenThinkingDisabled == other.OmitReasoningEffortWhenThinkingDisabled
            && MaxContextWindowTokens == other.MaxContextWindowTokens
            && MaxInputTokens == other.MaxInputTokens
            && MaxOutputTokens == other.MaxOutputTokens
            && MaximumIterationsPerRequest == other.MaximumIterationsPerRequest
            && string.Equals(SystemPrompt, other.SystemPrompt, StringComparison.Ordinal);
    }

    public static ExtendedAgentOptions Create(ModelProviderProfile providerProfile, ModelProfile modelProfile, AppAgentOptions appAgentOptions)
    {
        ModelProfileAgentOptions agentOptions = modelProfile.AgentOptions;
        return new()
        {
            ProviderType = providerProfile.ProviderType.Value,
            Endpoint = providerProfile.Endpoint,
            ApiKey = providerProfile.ApiKey,
            ModelId = modelProfile.ModelId.Trim(),
            Temperature = agentOptions.Temperature,
            TopP = agentOptions.TopP,
            ReasoningEffort = agentOptions.ReasoningEffort?.Value,
            ThinkingEnabled = agentOptions.ThinkingEnabled,
            OmitReasoningEffortWhenThinkingDisabled = agentOptions.OmitReasoningEffortWhenThinkingDisabled,
            MaxInputTokens = AgentOptionsNormalizer.NormalizeTokenLimit(agentOptions.MaxInputTokens),
            MaxContextWindowTokens = AgentOptionsNormalizer.NormalizeTokenLimit(agentOptions.MaxContextWindowTokens),
            MaxOutputTokens = AgentOptionsNormalizer.NormalizeTokenLimit(agentOptions.MaxOutputTokens),
            SystemPrompt = AgentOptionsNormalizer.NormalizeSystemPrompt(appAgentOptions.SystemPrompt),
            MaximumIterationsPerRequest = AgentOptionsNormalizer.NormalizeMaximumIterationsPerRequest(appAgentOptions.MaximumIterationsPerRequest),
        };
    }

    public ChatClientAgentRunOptions AsAgentRunOptions()
    {
        // Most models will ignore temperature and top_p when reasoning is enabled, but we set them here anyway.
        ChatOptions chatOptions = new()
        {
            ModelId = ModelId,
            Temperature = Temperature,
            TopP = TopP,
            ToolMode = ChatToolMode.Auto,
        };

        if (ReasoningEffort.HasValue && (ThinkingEnabled is not false || !OmitReasoningEffortWhenThinkingDisabled))
        {
            chatOptions.Reasoning = new()
            {
                Effort = ReasoningEffort,
            };
        }

        return new(chatOptions);
    }

    public async ValueTask<AgentCreationResult> CreateHarnessAgentAsync(IChatClient chatClient, IList<AITool>? tools, IServiceProvider serviceProvider, AgentWorkspaceSnapshot workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int maxOutputTokens = Math.Clamp(MaxOutputTokens ?? DefaultMaxOutputTokens, 1, int.MaxValue - 1);
        int maxContextWindowTokens = Math.Clamp(MaxContextWindowTokens ?? ((MaxInputTokens ?? DefaultMaxInputTokens) + maxOutputTokens), maxOutputTokens + 1, int.MaxValue);

        IAsyncDisposable? resources = null;
        try
        {
            HarnessAgentOptions harnessOptions = CreateHarnessAgentOptions(tools, serviceProvider, workspace, maxContextWindowTokens, maxOutputTokens, out resources);
            HarnessAgent agent = chatClient.AsHarnessAgent(harnessOptions, loggerFactory: serviceProvider.GetRequiredService<ILoggerFactory>(), services: serviceProvider);
            return new()
            {
                Agent = agent,
                Resources = resources,
            };
        }
        catch
        {
            await (resources?.DisposeAsync() ?? ValueTask.CompletedTask);
            throw;
        }
    }

    private HarnessAgentOptions CreateHarnessAgentOptions(IList<AITool>? tools, IServiceProvider serviceProvider, AgentWorkspaceSnapshot workspace, int maxContextWindowTokens, int maxOutputTokens, out IAsyncDisposable? resources)
    {
        List<AIContextProvider> contextProviders = [new AgentWorkspaceContextProvider(workspace.WorkingDirectories)];
        LocalShellExecutor shellExecutor = CreateShellExecutor(workspace);
        resources = shellExecutor;

        return new()
        {
            ChatOptions = CreateHarnessChatOptions(tools, maxOutputTokens),
            ChatHistoryProvider = CreateChatHistoryProvider(serviceProvider),
            AIContextProviders = contextProviders,
            HarnessInstructions = string.Empty,
            MaxContextWindowTokens = maxContextWindowTokens,
            MaxOutputTokens = maxOutputTokens,
            MaximumIterationsPerRequest = MaximumIterationsPerRequest,
            DisableToolAutoApproval = false,
            DisableFileMemory = false,
            FileMemoryStore = new FileSystemAgentFileStore(workspace.MemoryDirectory),
            DisableFileAccess = true,
            FileAccessStore = null,
            DisableWebSearch = true,
            DisableTodoProvider = true,
            DisableAgentModeProvider = true,
            DisableAgentSkillsProvider = false,
            AgentSkillsSource = EmptyAgentSkillsSource.Instance,
            DisableOpenTelemetry = true,
            ShellExecutor = shellExecutor,
        };
    }

    private static LocalShellExecutor CreateShellExecutor(AgentWorkspaceSnapshot workspace)
    {
        return new(new LocalShellExecutorOptions
        {
            WorkingDirectory = workspace.WorkingDirectory,
            ConfineWorkingDirectory = true,
            Timeout = LocalShellExecutor.DefaultTimeout,
            Policy = CreateShellPolicy(),
        });
    }

    private static ShellPolicy CreateShellPolicy()
    {
        return new(
        [
            @"(^|[;&|]\s*)rm\b(?=.*\s-rf\b|.*\s-fr\b|.*\s-r\b.*\s-f\b|.*\s-f\b.*\s-r\b)\s+[/\\]?(?=\s|$)",
            @"(^|[;&|]\s*)Remove-Item\b(?=.*\b(-Recurse|-r)\b)(?=.*\b(-Force|-fo)\b)",
            @"(^|[;&|]\s*)format(\.com)?\b",
        ]);
    }

    private ChatHistoryProvider CreateChatHistoryProvider(IServiceProvider serviceProvider)
    {
        return ProviderType switch
        {
            ModelProviderType.OpenAIChatCompletion => new RoundTripInMemoryChatHistoryProvider(serviceProvider.GetRequiredService<ObjectPool<StringBuilder>>()),
            ModelProviderType.OpenAIResponses or ModelProviderType.Anthropic => new InMemoryChatHistoryProvider(),
            _ => throw new NotSupportedException($"Unsupported model provider type: {ProviderType}"),
        };
    }

    private ChatOptions CreateHarnessChatOptions(IList<AITool>? tools, int maxOutputTokens)
    {
        ChatOptions chatOptions = new()
        {
            ModelId = ModelId,
            Instructions = SystemPrompt,
            Temperature = Temperature,
            TopP = TopP,
            MaxOutputTokens = maxOutputTokens,
            ToolMode = ChatToolMode.Auto,
            Tools = tools,
        };

        if (ProviderType is ModelProviderType.OpenAIChatCompletion)
        {
            string? thinkingEnabled = ThinkingEnabled.HasValue ? ThinkingEnabled.Value ? "enabled" : "disabled" : null;

            chatOptions.RawRepresentationFactory = client =>
            {
                ChatCompletionOptions options = new();
                if (!string.IsNullOrEmpty(thinkingEnabled))
                {
                    // "thinking": { "type": "enabled" } | "thinking": { "type": "disabled" }
                    options.Patch.Set("$.thinking.type"u8, thinkingEnabled);
                }

                return options;
            };
        }

        return chatOptions;
    }
}
