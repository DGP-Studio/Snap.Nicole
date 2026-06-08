using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;

namespace Snap.Nicole.Services.AI.Observables;

internal sealed partial class ObservableTextReasoningContent : ObservableAIContent
{
    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial bool IsCompleted { get; set; }

    public static ObservableTextReasoningContent Create(TextReasoningContent textReasoningContent)
    {
        return new()
        {
            Text = textReasoningContent.Text,
        };
    }

    public void Update(ObservableTextReasoningContent textReasoningContent)
    {
        Text += textReasoningContent.Text;
    }
}
