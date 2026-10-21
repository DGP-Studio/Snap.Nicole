using Snap.Nicole.Services.AI.Observables;
using System.Text.Json;
using System.Collections.Generic;

namespace Snap.Nicole.Services.AI.Models;

internal sealed class AgentConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public Guid? ModelProviderProfileId { get; set; }

    public Guid? ModelProfileId { get; set; }

    public JsonElement? SerializedSessionState { get; set; }

    public List<ObservableToolApprovalRequestContent> ToolApprovalRequests { get; set; } = [];

    public ObservableToolApprovalRequestContent? ToolApprovalRequest { get; set; }

    public ObservableChatMessageCollection Messages { get; set; } = [];
}
