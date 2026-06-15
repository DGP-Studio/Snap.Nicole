using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Snap.Nicole.Services.AI.Observables;
using System.Collections.Generic;
using System.Text.Json;

namespace Snap.Nicole.Services.AI;

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
            extendedUpdate.Add(ObservableAIContent.Create(content, jsonOptions));
        }

        return extendedUpdate;
    }

    public void Add(ObservableAIContent? observableContent)
    {
        if (observableContent is null)
        {
            return;
        }

        if (observableContent is ObservableToolApprovalRequestContent request)
        {
            ToolApprovalRequest = request;
        }
        else
        {
            ObservableContents.Add(observableContent);
        }
    }
}