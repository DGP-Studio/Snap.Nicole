using Snap.Nicole.Core;
using System.Diagnostics.CodeAnalysis;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

internal sealed class AgentWorkspaceGrepToolOptions
{
    public const int DefaultHeadLimit = 250;

    public required string Pattern { get; init; }

    public string? Glob { get; init; }

    public required AgentWorkspaceGrepToolOutputMode OutputMode { get; init; }

    public required int BeforeContext { get; init; }

    public required int AfterContext { get; init; }

    public required bool ShowLineNumbers { get; init; }

    public required bool IgnoreCase { get; init; }

    public required int HeadLimit { get; init; }

    public required int Offset { get; init; }

    public required bool Multiline { get; init; }

    public required bool OnlyMatching { get; init; }

    public static MessageResult<AgentWorkspaceGrepToolOptions> Create(string pattern, string? glob, AgentWorkspaceGrepToolOutputMode outputMode, int? beforeContext, int? afterContext, int? context, bool showLineNumbers, bool ignoreCase, int? headLimit, int offset, bool multiline, bool onlyMatching)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return "Search pattern cannot be empty.";
        }

        if (!TryNormalizeNonNegativeOption(offset, nameof(offset), 0, out int normalizedOffset, out string? message))
        {
            return message;
        }

        if (!TryNormalizeNonNegativeOption(headLimit, nameof(headLimit), DefaultHeadLimit, out int normalizedHeadLimit, out message))
        {
            return message;
        }

        if (!TryNormalizeContextOption(context, beforeContext, nameof(context), nameof(beforeContext), out int normalizedBeforeContext, out message))
        {
            return message;
        }

        if (!TryNormalizeContextOption(context, afterContext, nameof(context), nameof(afterContext), out int normalizedAfterContext, out message))
        {
            return message;
        }

        AgentWorkspaceGrepToolOptions options = new()
        {
            Pattern = pattern,
            OutputMode = outputMode,
            Offset = normalizedOffset,
            HeadLimit = normalizedHeadLimit,
            IgnoreCase = ignoreCase,
            Multiline = multiline,
            ShowLineNumbers = showLineNumbers,
            OnlyMatching = onlyMatching,
            BeforeContext = normalizedBeforeContext,
            AfterContext = normalizedAfterContext,
            Glob = glob,
        };

        return options;
    }

    private static bool TryNormalizeContextOption(int? contextValue, int? specificValue, string contextParameterName, string specificParameterName, out int normalizedValue, [NotNullWhen(false)] out string? message)
    {
        if (specificValue.HasValue)
        {
            return TryNormalizeNonNegativeOption(specificValue, specificParameterName, 0, out normalizedValue, out message);
        }

        return TryNormalizeNonNegativeOption(contextValue, contextParameterName, 0, out normalizedValue, out message);
    }

    private static bool TryNormalizeNonNegativeOption(int? value, string parameterName, int defaultValue, out int normalizedValue, [NotNullWhen(false)] out string? message)
    {
        if (!value.HasValue)
        {
            normalizedValue = defaultValue;
            message = null;
            return true;
        }

        if (value.Value < 0)
        {
            normalizedValue = defaultValue;
            message = $"{parameterName} cannot be negative.";
            return false;
        }

        normalizedValue = value.Value;
        message = null;
        return true;
    }
}
