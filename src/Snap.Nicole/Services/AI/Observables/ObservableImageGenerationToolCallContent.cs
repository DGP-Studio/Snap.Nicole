using Microsoft.Extensions.AI;

namespace Snap.Nicole.Services.AI.Observables;

internal sealed class ObservableImageGenerationToolCallContent : ObservableToolCallContent
{
    public static ObservableImageGenerationToolCallContent Create(ImageGenerationToolCallContent toolCallContent)
    {
        return new()
        {
            CallId = toolCallContent.CallId,
        };
    }
}
