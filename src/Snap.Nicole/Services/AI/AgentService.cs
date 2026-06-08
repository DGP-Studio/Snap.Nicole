using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Sentry;
using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Core.Text.Json;
using Snap.Nicole.Core.Threading;
using Snap.Nicole.Services.AI.Models;
using Snap.Nicole.Services.AI.Observables;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI;

internal sealed class AgentService(IServiceProvider serviceProvider, AgentChatClientFactory chatClientFactory) : IAgentService
{
    private readonly IServiceProvider serviceProvider = serviceProvider;
    private readonly AgentChatClientFactory chatClientFactory = chatClientFactory;
    private readonly JsonSerializerOptions functionContentJsonOptions = serviceProvider.GetRequiredKeyedService<JsonSerializerOptions>(JsonSerializerOptionsKey.AIFunctionContent);

    public ValueTask<HarnessAgent> CreateAgentAsync(ExtendedAgentOptions options, CancellationToken cancellationToken = default)
    {
        IChatClient chatClient = chatClientFactory.Create(options, serviceProvider);
        return ValueTask.FromResult(options.CreateHarnessAgent(chatClient, CreateBuiltInTools(), serviceProvider));
    }

    public ValueTask<AgentSession> CreateSessionAsync(HarnessAgent agent, CancellationToken cancellationToken = default)
    {
        return agent.CreateSessionAsync(cancellationToken);
    }

    public ValueTask<AgentSession> DeserializeSessionAsync(HarnessAgent agent, JsonElement serializedState, CancellationToken cancellationToken = default)
    {
        return agent.DeserializeSessionAsync(serializedState, cancellationToken: cancellationToken);
    }

    public ValueTask<JsonElement> SerializeSessionAsync(HarnessAgent agent, AgentSession session, CancellationToken cancellationToken = default)
    {
        return agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
    }

    public async ValueTask<SpanStatus> RunStreamingAsync(HarnessAgent agent, ChatMessage message, ObservableChatMessageCollection collection, ExtendedAgentOptions options, AgentSession session, TaskScheduler taskScheduler, CancellationToken cancellationToken = default)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.AIChatStream, "Run streaming chat completion");
        span.SetTag(SentryTags.AIProvider, options.ProviderType.ToString());
        span.SetTag(SentryTags.AIModel, options.ModelId);

        try
        {
            ObservableChatMessage inputMessage = ObservableChatMessage.Create(message, functionContentJsonOptions);
            await taskScheduler.Run(ObservableChatMessageCollection.Add, collection, inputMessage, cancellationToken);

            ObservableChatMessage? responseMessage = null;
            bool responseAdded = false;

            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync([message], session, options.AsAgentRunOptions(), cancellationToken))
            {
                List<ObservableAIContent> observableContents = [];
                foreach (AIContent content in update.Contents)
                {
                    if (ObservableAIContent.Create(content, functionContentJsonOptions) is { } observableContent)
                    {
                        observableContents.Add(observableContent);
                    }
                }

                // Fast path for updates that do not contain any content. This avoids unnecessary dispatching to the UI thread.
                if (observableContents.Count is 0)
                {
                    continue;
                }

                State state = new(collection, options, observableContents, responseMessage, responseAdded);

                await taskScheduler.Run(static (state) =>
                {
                    // TODOO: this should be possible to lift out of the UI thread dispatch,
                    // but currently if this is created on a background thread, the UI gets stuck and doesn't update at all.
                    state.ResponseMessage ??= ObservableChatMessage.Create(ChatRole.Assistant, DateTimeOffset.Now, state.Options.ModelId);

                    if (!state.ResponseAdded)
                    {
                        state.Collection.Add(state.ResponseMessage);
                        state.ResponseAdded = true;
                    }

                    foreach (ObservableAIContent observableContent in state.ObservableContents)
                    {
                        state.ResponseMessage.Contents.AddOrUpdate(observableContent);
                    }
                }, state, cancellationToken);

                responseMessage = state.ResponseMessage;
                responseAdded = state.ResponseAdded;
            }

            span.SetData(SentryData.AIResponseAdded, responseAdded);
            return SpanStatus.Ok;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            span.Finish(SpanStatus.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.AIChatStream);

            ObservableChatMessage errorMessage = ObservableChatMessage.Create(ChatRole.Assistant, DateTimeOffset.Now, content: ObservableTextContent.Create($"Error: {ex.Message}"));
            await taskScheduler.Run(ObservableChatMessageCollection.Add, collection, errorMessage, cancellationToken);
            return SpanStatus.InternalError;
        }
    }

    private static IList<AITool> CreateBuiltInTools()
    {
        return [AIFunctionFactory.Create(BuiltInFunctions.GetCurrentTime)];
    }

    private sealed class State(ObservableChatMessageCollection collection, ExtendedAgentOptions options, List<ObservableAIContent> observableContents, ObservableChatMessage? responseMessage, bool responseAdded)
    {
        public ObservableChatMessageCollection Collection { get; } = collection;

        public ExtendedAgentOptions Options { get; } = options;

        public List<ObservableAIContent> ObservableContents { get; } = observableContents;

        public ObservableChatMessage? ResponseMessage { get; set; } = responseMessage;

        public bool ResponseAdded { get; set; } = responseAdded;
    }
}
