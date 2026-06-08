using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using OpenAI.Chat;
using Snap.Nicole.Services.AI.Compatibility.OpenAIChatCompletion;
using System.Collections.Generic;
using System.Text;

namespace Snap.Nicole.Services.AI.Models;

internal sealed class ExtendedAgentOptions
{
    private const int DefaultMaxInputTokens = 204800;
    private const int DefaultMaxOutputTokens = 4096;

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

    public static ExtendedAgentOptions Create(ModelProviderProfile providerProfile, ModelProfile modelProfile)
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
            ReasoningEffort = agentOptions.ReasoningEffort,
            ThinkingEnabled = agentOptions.ThinkingEnabled,
            OmitReasoningEffortWhenThinkingDisabled = agentOptions.OmitReasoningEffortWhenThinkingDisabled,
            MaxInputTokens = NormalizeTokenLimit(agentOptions.MaxInputTokens),
            MaxContextWindowTokens = NormalizeTokenLimit(agentOptions.MaxContextWindowTokens),
            MaxOutputTokens = NormalizeTokenLimit(agentOptions.MaxOutputTokens),
            SystemPrompt = NormalizeSystemPrompt(agentOptions.SystemPrompt),
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

    public HarnessAgent CreateHarnessAgent(IChatClient chatClient, IList<AITool>? tools, IServiceProvider serviceProvider)
    {
        int maxOutputTokens = Math.Clamp(MaxOutputTokens ?? DefaultMaxOutputTokens, 1, int.MaxValue - 1);
        if (MaxContextWindowTokens is int maxContextWindowTokens)
        {
            maxContextWindowTokens = Math.Clamp(maxContextWindowTokens, maxOutputTokens + 1, int.MaxValue);
        }
        else
        {
            maxContextWindowTokens = Math.Clamp(MaxInputTokens ?? DefaultMaxInputTokens, 1, int.MaxValue - maxOutputTokens) + maxOutputTokens;
        }

        HarnessAgentOptions harnessOptions = CreateHarnessAgentOptions(tools, serviceProvider, maxOutputTokens);
        return chatClient.AsHarnessAgent(maxContextWindowTokens, maxOutputTokens, harnessOptions, loggerFactory: serviceProvider.GetRequiredService<ILoggerFactory>(), services: serviceProvider);
    }

    private HarnessAgentOptions CreateHarnessAgentOptions(IList<AITool>? tools, IServiceProvider serviceProvider, int maxOutputTokens)
    {
        return new()
        {
            ChatOptions = CreateHarnessChatOptions(tools, maxOutputTokens),
            ChatHistoryProvider = CreateChatHistoryProvider(serviceProvider),
            HarnessInstructions = string.Empty,
            DisableToolApproval = true,
            DisableFileMemory = true,
            DisableFileAccess = true,
            DisableWebSearch = true,
            DisableTodoProvider = true,
            DisableAgentModeProvider = true,
            DisableAgentSkillsProvider = true,
            DisableOpenTelemetry = true,
        };
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
        string? thinkingEnabled = ThinkingEnabled.HasValue
            ? ThinkingEnabled.Value ? "enabled" : "disabled"
            : null;

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

    private static int? NormalizeTokenLimit(int? value)
    {
        return value is > 0 ? value : null;
    }

    private static string? NormalizeSystemPrompt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
