namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal static class Prompt
{
    public const string ShellToolName = "run_shell";
    public const string AgentToolName = "Agent";

    public const string ShellToolDescription = """
        Execute a single shell command in a fresh local shell process and return its stdout, stderr, and exit code.

        Usage:
        - Each call starts a fresh shell process. Working directory changes, environment variables, aliases, and functions do not persist across calls.
        - The command starts in the current workspace directory.
        - On Windows, use PowerShell syntax unless the shell resolver falls back to cmd.exe.
        - Combine related steps into one command if command state matters.
        """;

    public const string WriteToolName = "Write";
    public const string WriteToolDescription = $"""
        Writes a file to the local filesystem.

        Usage:
        - This tool will overwrite the existing file if there is one at the provided path.
        - If this is an existing file, you MUST use the {ReadToolName} tool first to read the file's contents. This tool will fail if you did not read the file first.
        - Prefer the Edit tool for modifying existing files
        - it only sends the diff. Only use this tool to create new files or for complete rewrites.
        - NEVER create documentation files (*.md) or README files unless explicitly requested by the User.
        - Only use emojis if the user explicitly requests it. Avoid writing emojis to files unless asked.
        """;

    public const string ReadToolName = "Read";
    public const string ReadToolDescription = $"""
        Reads a file from the local filesystem. You can access any file directly by using this tool.
        If the User provides a path to a file assume that path is valid. It is okay to read a file that does not exist; an error will be returned.
        
        Usage:
        - The file_path parameter must be an absolute path, not a relative path
        - By default, it reads up to 2000 lines starting from the beginning of the file. Files larger than 256KB will return an error; use offset and limit for larger files
        - You can optionally specify a line offset and limit (especially handy for long files), but it's recommended to read the whole file by not providing these parameters
        - When you already know which part of the file you need, only read that part. This can be important for larger files.
        - Results are returned using cat -n format, with line numbers starting at 1
        - This tool allows you to read images (eg PNG, JPG, etc). When reading an image file the contents are presented visually.
        - This tool can only read files, not directories. To read a directory, use an ls command via the {ShellToolName} tool.
        - You will regularly be asked to read screenshots. If the user provides a path to a screenshot, ALWAYS use this tool to view the file at the path.
        - If you read a file that exists but has empty contents you will receive a system reminder warning in place of file contents.
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
    public const string GlobToolDescription = $"""
        - Fast file pattern matching tool that works with any codebase size
        - Supports glob patterns like "**/*.js" or "src/**/*.ts"
        - Returns matching file paths sorted by modification time
        - Use this tool when you need to find files by name patterns
        - When you are doing an open ended search that may require multiple rounds of globbing and grepping, use the {AgentToolName} tool instead
        """;

    public const string GrepToolName = "Grep";
    public const string GrepToolDescription = $$"""
        A powerful search tool built on ripgrep

        Usage:
        - ALWAYS use {{GrepToolName}} for search tasks. NEVER invoke `grep` or `rg` as a {{ShellToolName}} command. The {{GrepToolName}} tool has been optimized for correct permissions and access.
        - Supports full regex syntax (e.g., "log.*Error", "function\s+\w+")
        - Filter files with glob parameter (e.g., "*.js", "**/*.tsx")
        - Output modes: "content" shows matching lines, "files_with_matches" shows only file paths (default), "count" shows match counts
        - Use {{AgentToolName}} tool for open-ended searches requiring multiple rounds
        - Pattern syntax: Uses ripgrep (not grep) - literal braces need escaping (use `interface\{\}` to find `interface{}` in Go code)
        - Multiline matching: By default patterns match within single lines only. For cross-line patterns like `struct \{[\s\S]*?field`, use `multiline: true`
        """;
}
