using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Observables;
using System.Collections.Generic;
using System.Text.Json;

namespace Snap.Nicole.Services.AI;

internal sealed class ExtendedAgentResponseUpdate
{
    public List<ObservableAIContent> ObservableContents { get; } = [];

    public List<ObservableToolApprovalRequestContent> ToolApprovalRequests { get; } = [];

    public bool IsEmpty { get => ObservableContents.Count <= 0 && ToolApprovalRequests.Count == 0; }

    public static ExtendedAgentResponseUpdate Create(AgentResponseUpdate update, JsonSerializerOptions jsonOptions)
    {
        ExtendedAgentResponseUpdate extendedUpdate = new();
        foreach (AIContent content in update.Contents)
        {
            if (ObservableAIContent.Create(content, jsonOptions) is not { } observableContent)
            {
                continue;
            }

            if (observableContent is ObservableToolApprovalRequestContent request)
            {
                extendedUpdate.ToolApprovalRequests.Add(request);
            }
            else
            {
                extendedUpdate.ObservableContents.Add(observableContent);
            }
        }

        return extendedUpdate;
    }
}
