using Snap.Nicole.Services.AI.Observables;
using System.Collections.Generic;
using System.Text.Json;

namespace Snap.Nicole.Services.AI.Models;

internal sealed class AgentConversation
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Title { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    public Guid? ModelProviderProfileId { get; init; }

    public Guid? ModelProfileId { get; init; }

    public JsonElement? SerializedSessionState { get; init; }

    public List<string> ExternalDirectories { get; init; } = [];

    public ObservableToolApprovalRequestContent? ToolApprovalRequest { get; init; }

    public ObservableChatMessageCollection Messages { get; init; } = [];
}
