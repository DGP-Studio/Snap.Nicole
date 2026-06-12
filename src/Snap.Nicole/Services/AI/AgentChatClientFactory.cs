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

internal sealed class AgentChatClientFactory(IServiceProvider serviceProvider)
{
    private readonly IServiceProvider serviceProvider = serviceProvider;
    private readonly ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

    public IChatClient Create(ExtendedAgentOptions options)
    {
        return options.ProviderType switch
        {
            ModelProviderType.OpenAIChatCompletion => CreateOpenAIChatCompletionChatClient(options),
            ModelProviderType.OpenAIResponses => CreateOpenAIResponsesChatClient(options),
            ModelProviderType.Anthropic => CreateAnthropicChatClient(options),
            _ => throw new NotSupportedException($"Unsupported model provider type: {options.ProviderType}"),
        };
    }

    private IChatClient CreateOpenAIChatCompletionChatClient(ExtendedAgentOptions options)
    {
        return CreateOpenAIClient(options)
            .GetChatClient(options.ModelId)
            .AsIChatClient()
            .AsBuilder()
            .Use(innerClient => new UsageContentRectifyDelegatingChatClient(innerClient))
            .UseLogging(loggerFactory)
            .Build(serviceProvider);
    }

    private IChatClient CreateOpenAIResponsesChatClient(ExtendedAgentOptions options)
    {
        return CreateOpenAIClient(options)
            .GetResponsesClient()
            .AsIChatClient(options.ModelId)
            .AsBuilder()
            .UseLogging(loggerFactory)
            .Build(serviceProvider);
    }

    private IChatClient CreateAnthropicChatClient(ExtendedAgentOptions options)
    {
        return CreateAnthropicClient(options)
            .AsIChatClient(options.ModelId, options.MaxOutputTokens)
            .AsBuilder()
            .UseLogging(loggerFactory)
            .Build(serviceProvider);
    }

    private static OpenAIClient CreateOpenAIClient(ExtendedAgentOptions options)
    {
        return new(new ApiKeyCredential(options.ApiKey!), new()
        {
            Endpoint = options.Endpoint.ToUri(),
        });
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
