using CommunityToolkit.Mvvm.ComponentModel;
using Snap.Nicole.Core;

namespace Snap.Nicole.Services.AI.Agent.Models;

// DO NOT use AgentOptionsNormalizer.Normalize* call in the setter, because the options
// from UI will be normalized eventually before using, and we don't want to normalize them twice.
[GeneratedCopyFrom<AppAgentOptions>]
internal sealed partial class AppAgentOptions : ObservableObject, ICopyFrom<AppAgentOptions>
{
    [ObservableProperty]
    public partial string UserName { get; set; } = AgentOptionsNormalizer.DefaultUserName;

    [ObservableProperty]
    public partial string? SystemPrompt { get; set; } = AgentOptionsNormalizer.DefaultSystemPrompt;

    [ObservableProperty]
    public partial int? MaximumIterationsPerRequest { get; set; } = AgentOptionsNormalizer.DefaultMaximumIterationsPerRequest;
}
