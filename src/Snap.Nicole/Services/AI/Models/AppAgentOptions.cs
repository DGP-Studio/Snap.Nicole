using CommunityToolkit.Mvvm.ComponentModel;
using Snap.Nicole.Core;

namespace Snap.Nicole.Services.AI.Models;

[GeneratedCopyFrom<AppAgentOptions>]
internal sealed partial class AppAgentOptions : ObservableObject, ICopyFrom<AppAgentOptions>
{
    [ObservableProperty]
    public partial int? MaximumIterationsPerRequest { get; set; } = AgentOptionsNormalizer.DefaultMaximumIterationsPerRequest;
}
