using Anthropic;
using Anthropic.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Responses;
using Snap.Nicole.Core;
using Snap.Nicole.Services.AI.Compatibility.OpenAIChatCompletion;
using Snap.Nicole.Services.AI.Models;
using System.ClientModel;

namespace Snap.Nicole.Services.AI;

internal sealed class AgentChatClientFactory(ILoggerFactory loggerFactory)
{
    private readonly ILoggerFactory loggerFactory = loggerFactory;

    public IChatClient Create(ExtendedAgentOptions options, IServiceProvider serviceProvider)
    {
        return options.ProviderType switch
        {
            ModelProviderType.OpenAIChatCompletion => CreateOpenAIChatCompletionChatClient(options, serviceProvider),
            ModelProviderType.OpenAIResponses => CreateOpenAIResponsesChatClient(options),
            ModelProviderType.Anthropic => CreateAnthropicChatClient(options),
            _ => throw new NotSupportedException($"Unsupported model provider type: {options.ProviderType}"),
        };
    }

    private IChatClient CreateOpenAIChatCompletionChatClient(ExtendedAgentOptions options, IServiceProvider serviceProvider)
    {
        return CreateOpenAIClient(options, enableLogging: true)
            .GetChatClient(options.ModelId)
            .AsIChatClient()
            .AsBuilder()
            .Use(innerClient => new UsageContentRectifyDelegatingChatClient(innerClient))
            .Build(serviceProvider);
    }

    private IChatClient CreateOpenAIResponsesChatClient(ExtendedAgentOptions options)
    {
        ResponsesClient client = CreateOpenAIClient(options, enableLogging: false).GetResponsesClient();
        return client.AsIChatClient(options.ModelId);
    }

    private static IChatClient CreateAnthropicChatClient(ExtendedAgentOptions options)
    {
        return CreateAnthropicClient(options).AsIChatClient(options.ModelId, options.MaxOutputTokens);
    }

    private OpenAIClient CreateOpenAIClient(ExtendedAgentOptions options, bool enableLogging)
    {
        OpenAIClientOptions clientOptions = new()
        {
            Endpoint = options.Endpoint.ToUri(),
        };

        if (enableLogging)
        {
            clientOptions.ClientLoggingOptions = new()
            {
                LoggerFactory = loggerFactory,
                EnableMessageContentLogging = true,
            };
        }

        return new(new ApiKeyCredential(options.ApiKey!), clientOptions);
    }

    private static AnthropicClient CreateAnthropicClient(ExtendedAgentOptions options)
    {
        return new(new ClientOptions
        {
            ApiKey = options.ApiKey,
            BaseUrl = string.IsNullOrWhiteSpace(options.Endpoint) ? EnvironmentUrl.Production : options.Endpoint,
        });
    }
}
