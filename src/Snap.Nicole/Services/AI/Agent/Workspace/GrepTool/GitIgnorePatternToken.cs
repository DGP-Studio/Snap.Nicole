namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

internal readonly struct GitIgnorePatternToken
{
    public GitIgnorePatternToken(GitIgnorePatternTokenKind kind)
    {
        Kind = kind;
        Literal = default;
        Text = string.Empty;
    }

    public GitIgnorePatternToken(GitIgnorePatternTokenKind kind, char literal)
    {
        Kind = kind;
        Literal = literal;
        Text = string.Empty;
    }

    public GitIgnorePatternToken(GitIgnorePatternTokenKind kind, string text)
    {
        Kind = kind;
        Literal = default;
        Text = text;
    }

    public GitIgnorePatternTokenKind Kind { get; }

    public char Literal { get; }

    public string Text { get; }
}
