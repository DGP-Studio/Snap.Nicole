using Snap.Nicole.Core.IO;
using Snap.Nicole.Core.Text;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

internal sealed class GitIgnoreRuleSet
{
    private const string GitIgnoreFileName = ".gitignore";

    public static GitIgnoreRuleSet Empty { get; } = new()
    {
        Rules = [],
    };

    public required IReadOnlyList<GitIgnoreRule> Rules { get; init; }

    public static GitIgnoreRuleSet CreateForDirectory(string rootDirectory, string directoryPath)
    {
        string normalizedRootDirectory = Path.TrimEndingDirectorySeparator(rootDirectory);
        string normalizedDirectoryPath = Path.TrimEndingDirectorySeparator(directoryPath);

        if (!Path.IsEqualOrSubdirectory(normalizedRootDirectory, normalizedDirectoryPath))
        {
            return Empty;
        }

        List<string> directories = [];
        string currentDirectory = normalizedDirectoryPath;
        while (true)
        {
            directories.Add(currentDirectory);
            if (Path.IsEqual(normalizedRootDirectory, currentDirectory))
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

        GitIgnoreRuleSet ruleSet = Empty;
        for (int i = directories.Count - 1; i >= 0; i--)
        {
            ruleSet = ruleSet.CreateChild(normalizedRootDirectory, directories[i]);
        }

        return ruleSet;
    }

    public static GitIgnoreRuleSet CreateForParentDirectory(string rootDirectory, string directoryPath)
    {
        string normalizedDirectoryPath = Path.TrimEndingDirectorySeparator(directoryPath);
        string? parentDirectory = Path.GetDirectoryName(normalizedDirectoryPath);
        return string.IsNullOrEmpty(parentDirectory) ? Empty : CreateForDirectory(rootDirectory, parentDirectory);
    }

    public GitIgnoreRuleSet CreateChild(string rootDirectory, string directoryPath)
    {
        IReadOnlyList<GitIgnoreRule> localRules = CreateRules(rootDirectory, directoryPath);
        if (localRules.Count is 0)
        {
            return this;
        }

        List<GitIgnoreRule> rules = new(Rules.Count + localRules.Count);
        for (int i = 0; i < Rules.Count; i++)
        {
            rules.Add(Rules[i]);
        }

        for (int i = 0; i < localRules.Count; i++)
        {
            rules.Add(localRules[i]);
        }

        return new()
        {
            Rules = rules,
        };
    }

    public GitIgnoreRuleSet WithoutRulesMatching(string rootRelativePath, bool isDirectory)
    {
        List<GitIgnoreRule>? remainingRules = null;
        for (int i = 0; i < Rules.Count; i++)
        {
            GitIgnoreRule rule = Rules[i];
            if (rule.Matches(rootRelativePath, isDirectory))
            {
                if (remainingRules is null)
                {
                    remainingRules = new(i);
                    for (int j = 0; j < i; j++)
                    {
                        remainingRules.Add(Rules[j]);
                    }
                }

                continue;
            }

            remainingRules?.Add(rule);
        }

        return remainingRules is null ? this : new()
        {
            Rules = remainingRules,
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

    private static IReadOnlyList<GitIgnoreRule> CreateRules(string rootDirectory, string directoryPath)
    {
        string gitIgnorePath = Path.Combine(directoryPath, GitIgnoreFileName);
        if (!File.Exists(gitIgnorePath))
        {
            return [];
        }

        string baseRelativeDirectory = AgentWorkspacePath.GetRelativePath(rootDirectory, directoryPath);
        if (string.Equals(baseRelativeDirectory, ".", StringComparison.Ordinal))
        {
            baseRelativeDirectory = string.Empty;
        }

        string[] lines = File.ReadAllLines(gitIgnorePath, Encoding.UTF8WithoutBOM);
        List<GitIgnoreRule> rules = [];
        for (int i = 0; i < lines.Length; i++)
        {
            string line = i is 0 ? lines[i].TrimStart('\uFEFF') : lines[i];
            if (GitIgnoreRule.Create(baseRelativeDirectory, line) is { } rule)
            {
                rules.Add(rule);
            }
        }

        return rules;
    }
}
