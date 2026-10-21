using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.ObjectPool;
using Snap.Nicole.Services.AI;
using Snap.Nicole.Services.AI.Compatibility.OpenAIChatCompletion;
using Snap.Nicole.Services.AI.Models;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Snap.Nicole.Tests;

[TestClass]
public sealed class ChatReliabilityTests
{
    [TestMethod]
    public void AssistantTextPartsKeepFinalBuffer()
    {
        var pool = new DefaultObjectPool<StringBuilder>(new StringBuilderPooledObjectPolicy());
        RoundTripInMemoryChatHistoryProvider provider = new(pool);
        OpenAI.Chat.AssistantChatMessage raw = new([
            OpenAI.Chat.ChatMessageContentPart.CreateTextPart("first"),
            OpenAI.Chat.ChatMessageContentPart.CreateTextPart("last")]);
        typeof(RoundTripInMemoryChatHistoryProvider).GetMethod("WorkaroundContentRoundTrip", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(provider, [raw]);
        Assert.AreEqual("firstlast", raw.Content.Single().Text);
    }

    [TestMethod]
    public void MultipleApprovalsAndPartialDecisionsSurviveSerialization()
    {
        JsonSerializerOptions options = new(AIJsonUtilities.DefaultOptions);
        AgentResponseUpdate update = new()
        {
            Contents = [new ToolApprovalRequestContent("one", new FunctionCallContent("call-one", "First", null)),
                new ToolApprovalRequestContent("two", new FunctionCallContent("call-two", "Second", null))]
        };
        ExtendedAgentResponseUpdate extended = ExtendedAgentResponseUpdate.Create(update, options);
        Assert.HasCount(2, extended.ToolApprovalRequests);
        extended.ToolApprovalRequests[0].Approved = false;
        extended.ToolApprovalRequests[0].IsHandled = true;
        AgentConversation conversation = new() { ToolApprovalRequests = extended.ToolApprovalRequests };
        AgentConversation restored = JsonSerializer.Deserialize<AgentConversation>(JsonSerializer.Serialize(conversation, options), options)!;
        Assert.HasCount(2, restored.ToolApprovalRequests);
        Assert.IsFalse(restored.ToolApprovalRequests[0].Approved);
        Assert.IsTrue(restored.ToolApprovalRequests[1].CanRespond);
        Assert.AreEqual("two", restored.ToolApprovalRequests[1].RawRepresentation!.CreateResponse(true).RequestId);
    }

    [TestMethod]
    public async Task RestoredSessionRebuildsReasoningWireRepresentation()
    {
        var pool = new DefaultObjectPool<StringBuilder>(new StringBuilderPooledObjectPolicy());
        ReasoningTestClient client = new();
        ChatClientAgent agent = new(client, new ChatClientAgentOptions { ChatHistoryProvider = new RoundTripInMemoryChatHistoryProvider(pool), ChatOptions = new() { ModelId = "test" } });
        AgentSession session = await agent.CreateSessionAsync();
        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync([new ChatMessage(ChatRole.User, "first")], session)) { }
        JsonSerializerOptions options = new(AIJsonUtilities.DefaultOptions);
        AgentSession restored = await agent.DeserializeSessionAsync(await agent.SerializeSessionAsync(session, options), options);
        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync([new ChatMessage(ChatRole.User, "second")], restored)) { }
        ChatMessage previous = client.LastMessages.Last(message => message.Role == ChatRole.Assistant);
        Assert.IsNotNull(previous.RawRepresentation);
        BinaryData raw = System.ClientModel.Primitives.ModelReaderWriter.Write((OpenAI.Chat.ChatMessage)previous.RawRepresentation!, System.ClientModel.Primitives.ModelReaderWriterOptions.Json);
        using JsonDocument json = JsonDocument.Parse(raw);
        Assert.AreEqual("reasoning", json.RootElement.GetProperty("reasoning_content").GetString());
    }

    [TestMethod]
    [DataRow("https://example.com/v1", true)]
    [DataRow("http://localhost:8080/v1", true)]
    [DataRow("http://127.0.0.1:8080/v1", true)]
    [DataRow("http://[::1]:8080/v1", true)]
    [DataRow("http://example.com/v1", false)]
    [DataRow("file:///C:/test", false)]
    [DataRow("https://user:password@example.com/v1", false)]
    public void EndpointPolicyProtectsRemoteCredentials(string endpoint, bool allowed)
    {
        if (allowed) { Assert.IsNotNull(AgentEndpoint.Validate(endpoint)); }
        else { Assert.Throws<ArgumentException>(() => AgentEndpoint.Validate(endpoint)); }
    }
}
