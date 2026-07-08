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

    public static ValidationResult Ask(string message)
    {
        return new()
        {
            Result = false,
            Behavior = "ask",
            Message = message,
        };
    }
}
