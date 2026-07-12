using Snap.Nicole.Core.IO;
using Snap.Nicole.Core.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

internal sealed class GitIgnoreRuleSet
{
    public static GitIgnoreRuleSet Empty { get; } = new()
    {
        Rules = [],
    };

    public required IReadOnlyList<GitIgnoreRule> Rules { get; init; }

    public static GitIgnoreRuleSet CreateForDirectory(AgentWorkspaceRootDirectory rootDirectory, string directoryPath)
    {
        string normalizedDirectoryPath = Path.TrimEndingDirectorySeparator(directoryPath);

        if (!rootDirectory.Contains(normalizedDirectoryPath))
        {
            return Empty;
        }

        Stack<string> directories = [];
        string currentDirectory = normalizedDirectoryPath;
        while (true)
        {
            directories.Push(currentDirectory);
            if (Path.IsEqual(rootDirectory.FullPath, currentDirectory))
            {
                break;
            }

            string? parentDirectory = Path.GetDirectoryName(currentDirectory);
            if (string.IsNullOrEmpty(parentDirectory))
            {
                break;
            }

            currentDirectory = Path.TrimEndingDirectorySeparator(parentDirectory);
        }

        // Create a rule set by traversing from the root directory to the specified directory, loading .gitignore rules along the way.
        GitIgnoreRuleSet ruleSet = Empty;
        while (directories.Count > 0)
        {
            ruleSet = ruleSet.CreateChild(rootDirectory, directories.Pop());
        }

        return ruleSet;
    }

    public static GitIgnoreRuleSet CreateForParentDirectory(AgentWorkspaceDirectory directory)
    {
        // Loads all .gitignore rules from the root directory to the parent directory of the specified directory.
        string? parentDirectory = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory.FullPath));
        return string.IsNullOrEmpty(parentDirectory) ? Empty : CreateForDirectory(directory.RootDirectory, parentDirectory);
    }

    public GitIgnoreRuleSet CreateChild(AgentWorkspaceRootDirectory rootDirectory, string directoryPath)
    {
        IReadOnlyList<GitIgnoreRule> localRules = CreateRules(rootDirectory, directoryPath);
        if (localRules.Count is 0)
        {
            return this;
        }

        return new()
        {
            Rules = [.. Rules, .. localRules],
        };
    }

    public GitIgnoreRuleSet WithoutRulesMatching(string rootRelativePath, bool isDirectory)
    {
        List<GitIgnoreRule>? rules = null;
        for (int i = 0; i < Rules.Count; i++)
        {
            GitIgnoreRule rule = Rules[i];
            if (rule.Matches(rootRelativePath, isDirectory))
            {
                rules ??= [with(i), .. Rules.Take(i)];
                continue;
            }

            rules?.Add(rule);
        }

        return rules is null ? this : new()
        {
            Rules = rules,
        };
    }

    public bool IsIgnored(string rootRelativePath, bool isDirectory)
    {
        bool ignored = false;
        for (int i = 0; i < Rules.Count; i++)
        {
            GitIgnoreRule rule = Rules[i];
            if (rule.Matches(rootRelativePath, isDirectory))
            {
                ignored = !rule.IsNegated;
            }
        }

        return ignored;
    }

    private static IReadOnlyList<GitIgnoreRule> CreateRules(AgentWorkspaceRootDirectory rootDirectory, string directoryPath)
    {
        string gitIgnorePath = Path.Combine(directoryPath, ".gitignore");
        if (!File.Exists(gitIgnorePath))
        {
            return [];
        }

        string rootRelativeDirectory = rootDirectory.GetRelativePath(directoryPath);
        if (string.Equals(rootRelativeDirectory, ".", StringComparison.Ordinal))
        {
            rootRelativeDirectory = string.Empty;
        }

        List<GitIgnoreRule> rules = [];
        foreach ((int index, string line) in File.ReadAllLines(gitIgnorePath, Encoding.UTF8WithoutBOM).Index())
        {
            if (GitIgnoreRule.Create(rootRelativeDirectory, index is 0 ? line.TrimStart('\uFEFF') : line) is { } rule)
            {
                rules.Add(rule);
            }
        }

        return rules;
    }
}
