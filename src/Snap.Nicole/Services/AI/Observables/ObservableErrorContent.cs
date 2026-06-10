using CommunityToolkit.Mvvm.ComponentModel;

namespace Snap.Nicole.Services.AI.Observables;

internal sealed partial class ObservableErrorContent : ObservableAIContent
{
    [ObservableProperty]
    public partial string Message { get; set; }

    [ObservableProperty]
    public partial string? ErrorCode { get; set; }

    [ObservableProperty]
    public partial string? Details { get; set; }

    public static ObservableErrorContent Create(string message, string? errorCode = default, string? details = default)
    {
        return new()
        {
            Message = message,
            ErrorCode = errorCode,
            Details = details,
        };
    }
}
