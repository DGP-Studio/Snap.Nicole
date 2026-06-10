using CommunityToolkit.Mvvm.ComponentModel;
using Snap.Nicole.Core;

namespace Snap.Nicole.Services.AI.Models;

[GeneratedCopyFrom<AppAgentOptions>]
internal sealed partial class AppAgentOptions : ObservableObject, ICopyFrom<AppAgentOptions>
{
    public string UserName { get; set => SetProperty(ref field, AgentOptionsNormalizer.NormalizeUserName(value)); } = AgentOptionsNormalizer.DefaultUserName;

    [ObservableProperty]
    public partial int? MaximumIterationsPerRequest { get; set; } = AgentOptionsNormalizer.DefaultMaximumIterationsPerRequest;
}
