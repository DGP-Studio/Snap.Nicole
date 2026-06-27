namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal static class Prompt
{
    public const string WriteToolName = "Write";
    public const string ReadToolName = "Read";
    public const string ReadToolDescription = $"""
        Reads a file from the local filesystem.

        - `filePath` must be an absolute path.
        - Reads up to 2000 lines by default.
        - You can optionally specify a line offset and limit (especially handy for long files), but it's recommended to read the whole file by not providing these parameters
        - Results are returned using cat -n format, with line numbers starting at 1
        - Reads images (PNG, JPG, ...) and presents them visually.
        - Reading a directory, a missing file, or an empty file returns an error or system reminder rather than content.
        - Do NOT re-read a file you just edited to verify - Edit/Write would have errored if the change failed, and the harness tracks file state for you.
        """;
    public const string EditToolName = "Edit";
    public const string EditToolDescription = $"""
        Performs exact string replacements in files.

        Usage:
        - You must use your `{ReadToolName}` tool at least once in the conversation before editing. This tool will error if you attempt an edit without reading the file. 
        - When editing text from Read tool output, ensure you preserve the exact indentation (tabs/spaces) as it appears AFTER the line number prefix. The line number prefix format is: (line number + tab). Everything after that is the actual file content to match. Never include any part of the line number prefix in the oldString or newString.
        - ALWAYS prefer editing existing files in the codebase. NEVER write new files unless explicitly required.
        - Only use emojis if the user explicitly requests it. Avoid adding emojis to files unless asked.
        - The edit will FAIL if `oldString` is not unique in the file. Either provide a larger string with more surrounding context to make it unique or use `replaceAll` to change every instance of `oldString`.
        - Use the smallest oldString that's clearly unique — usually 2-4 adjacent lines is sufficient. Avoid including 10+ lines of context when less uniquely identifies the target.
        - Use `replaceAll` for replacing and renaming strings across the file. This parameter is useful if you want to rename a variable for instance.
        """;
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
