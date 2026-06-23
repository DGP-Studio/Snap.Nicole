namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal static class Prompt
{
    public const string WriteToolName = "Write";
    public const string ReadToolName = "Read";
    public const string EditToolName = "Edit";
    public const string GlobToolName = "Glob";
    public const string GlobToolDescription = """
        - Fast file pattern matching tool that works with any codebase size
        - Supports glob patterns like "**/*.js" or "src/**/*.ts"
        - Returns matching file paths sorted by modification time
        - Use this tool when you need to find files by name patterns
        - When you are doing an open ended search that may require multiple rounds of globbing and grepping, use the Agent tool instead
        """;
    public const string GrepToolName = "Grep";
}