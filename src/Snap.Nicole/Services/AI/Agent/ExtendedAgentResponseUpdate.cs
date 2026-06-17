using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Observables;
using System.Collections.Generic;
using System.Text.Json;

namespace Snap.Nicole.Services.AI.Agent;

internal sealed class ExtendedAgentResponseUpdate
{
    public List<ObservableAIContent> ObservableContents { get; } = [];

    public ObservableToolApprovalRequestContent? ToolApprovalRequest { get; set; }

    public bool IsEmpty { get => ObservableContents.Count <= 0 && ToolApprovalRequest is null; }

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
                extendedUpdate.ToolApprovalRequest = request;
            }
            else
            {
                extendedUpdate.ObservableContents.Add(observableContent);
            }
        }

        return extendedUpdate;
    }
}
