using System.Collections.Frozen;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

internal static class GitIgnorePatternParser
{
    public static void AppendRegex(StringBuilder builder, string pattern)
    {
        GitIgnorePatternLexer lexer = new(pattern);
        while (lexer.TryRead(out GitIgnorePatternToken token))
        {
            AppendTokenRegex(builder, token);
        }
    }

    private static void AppendTokenRegex(StringBuilder builder, GitIgnorePatternToken token)
    {
        switch (token.Kind)
        {
            case GitIgnorePatternTokenKind.Literal:
                AppendRegexLiteral(builder, token.Literal);
                break;

            case GitIgnorePatternTokenKind.SingleStar:
                builder.Append("[^/]*");
                break;

            case GitIgnorePatternTokenKind.SingleCharacter:
                builder.Append("[^/]");
                break;

            case GitIgnorePatternTokenKind.CharacterClass:
                AppendCharacterClassRegex(builder, token.Text);
                break;

            case GitIgnorePatternTokenKind.ZeroOrMoreDirectories:
                builder.Append("(?:[^/]+/)*");
                break;

            case GitIgnorePatternTokenKind.AnyPathCharacters:
                builder.Append(".*");
                break;
        }
    }

    private static void AppendRegexLiteral(StringBuilder builder, char value)
    {
        if (value is '/')
        {
            builder.Append('/');
            return;
        }

        builder.Append(Regex.Escape(value.ToString()));
    }

    private static void AppendCharacterClassRegex(StringBuilder builder, string characterClass)
    {
        builder.Append('[');
        int startIndex = 0;
        if (characterClass[startIndex] is '!')
        {
            builder.Append('^');
            startIndex++;
        }

        for (int i = startIndex; i < characterClass.Length; i++)
        {
            char value = characterClass[i];
            if (value is '\\' && i + 1 < characterClass.Length)
            {
                i++;
                value = characterClass[i];
            }

            if (value is '\\' or '^')
            {
                builder.Append('\\');
            }

            builder.Append(value);
        }

        builder.Append(']');
    }
}
