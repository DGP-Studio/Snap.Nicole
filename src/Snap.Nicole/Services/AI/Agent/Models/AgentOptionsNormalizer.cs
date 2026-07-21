namespace Snap.Nicole.Services.AI.Agent.Models;

internal static class AgentOptionsNormalizer
{
    public const string DefaultUserName = "You";

    public const string DefaultSystemPrompt = "You are an interactive agent that helps users with software engineering tasks.";

    public const int DefaultMaximumIterationsPerRequest = 500;

    public static string NormalizeUserName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DefaultUserName : value.Trim();
    }

    public static int? NormalizeTokenLimit(int? value)
    {
        return value is > 0 ? value : null;
    }

    public static string? NormalizeSystemPrompt(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static int NormalizeMaximumIterationsPerRequest(int? value)
    {
        return value is > 0 ? value.Value : DefaultMaximumIterationsPerRequest;
    }
}
