using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using Snap.Nicole.Resources;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace Snap.Nicole.Services.AI.Observables;

internal sealed partial class ObservableToolApprovalRequestContent : ObservableInputRequestContent
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolName), nameof(ToolArguments))]
    public partial ObservableToolCallContent ToolCall { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRespond), nameof(StateDisplay))]
    public partial bool IsHandled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateDisplay))]
    public partial bool? Approved { get; set; }

    [ObservableProperty]
    public partial string? Reason { get; set; }

    [JsonIgnore]
    public ToolApprovalRequestContent? RawRepresentation { get; set; }

    [JsonIgnore]
    public ICommand? ApproveCommand { get; set; }

    [JsonIgnore]
    public ICommand? DenyCommand { get; set; }

    [JsonIgnore]
    public string ToolName { get => ToolCall is null ? string.Empty : ToolCall.DisplayName; }

    [JsonIgnore]
    public string? ToolArguments { get => ToolCall?.DisplayArguments; }

    [JsonIgnore]
    public bool CanRespond { get => !IsHandled && RawRepresentation is not null; }

    [JsonIgnore]
    public StringResourceValue StateDisplay
    {
        get
        {
            return IsHandled
                ? Approved is true
                    ? SRName.UIXamlControlsChatToolApprovalRequestSegmentViewLabelApproved
                    : SRName.UIXamlControlsChatToolApprovalRequestSegmentViewLabelRejected
                : SRName.UIXamlControlsChatToolApprovalRequestSegmentViewLabelPending;
        }
    }

    public static ObservableToolApprovalRequestContent Create(ToolApprovalRequestContent toolApprovalRequestContent, JsonSerializerOptions jsonOptions)
    {
        return new()
        {
            RequestId = toolApprovalRequestContent.RequestId,
            ToolCall = ObservableToolCallContent.Create(toolApprovalRequestContent.ToolCall, jsonOptions),
            RawRepresentation = toolApprovalRequestContent,
        };
    }

    public void Update(ObservableToolApprovalRequestContent toolApprovalRequestContent)
    {
        ToolCall = toolApprovalRequestContent.ToolCall;
        RawRepresentation = toolApprovalRequestContent.RawRepresentation;
    }

    public void Update(ObservableToolApprovalResponseContent toolApprovalResponseContent)
    {
        IsHandled = true;
        Approved = toolApprovalResponseContent.Approved;
        Reason = toolApprovalResponseContent.Reason;
    }
}
