using System.Diagnostics.CodeAnalysis;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class ValidationResult
{
    public static ValidationResult Ok { get; } = new()
    {
        Result = true,
    };

    [MemberNotNullWhen(false, nameof(Message))]
    public required bool Result { get; init; }

    public string? Behavior { get; init; }

    public string? Message { get; init; }

    public int ErrorCode { get; init; }

    public static ValidationResult Ask(string message, int errorCode)
    {
        return new()
        {
            Result = false,
            Behavior = "ask",
            Message = message,
            ErrorCode = errorCode,
        };
    }
}
