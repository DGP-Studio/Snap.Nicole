using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Snap.Nicole.Services.AI.Observables;

internal sealed partial class ObservableCodeInterpreterToolCallContent : ObservableToolCallContent
{
    [ObservableProperty]
    public partial ObservableAIContentCollection? Inputs { get; set; }

    public static ObservableCodeInterpreterToolCallContent Create(CodeInterpreterToolCallContent toolCallContent, JsonSerializerOptions jsonOptions)
    {
        return new()
        {
            CallId = toolCallContent.CallId,
            Inputs = ObservableAIContentCollection.Create(toolCallContent.Inputs, jsonOptions),
        };
    }
}
