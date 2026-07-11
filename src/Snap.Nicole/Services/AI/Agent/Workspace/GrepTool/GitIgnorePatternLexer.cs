namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

internal sealed class GitIgnorePatternLexer(string pattern)
{
    private readonly string pattern = pattern;
    private int index;

    public bool TryRead(out GitIgnorePatternToken token)
    {
        if (index >= pattern.Length)
        {
            token = default;
            return false;
        }

        char value = pattern[index];
        if (value is '\\' && index + 1 < pattern.Length)
        {
            index += 2;
            token = new(GitIgnorePatternTokenKind.Literal, pattern[index - 1]);
            return true;
        }

        if (value is '*')
        {
            token = ReadStarToken();
            return true;
        }

        if (value is '?')
        {
            index++;
            token = new(GitIgnorePatternTokenKind.SingleCharacter);
            return true;
        }

        if (value is '[' && TryReadCharacterClass(out string characterClass))
        {
            token = new(GitIgnorePatternTokenKind.CharacterClass, characterClass);
            return true;
        }

        index++;
        token = new(GitIgnorePatternTokenKind.Literal, value);
        return true;
    }

    private GitIgnorePatternToken ReadStarToken()
    {
        if (index + 1 >= pattern.Length || pattern[index + 1] is not '*')
        {
            index++;
            return new(GitIgnorePatternTokenKind.SingleStar);
        }

        // ^**/ or /**/
        if ((index is 0 || pattern[index - 1] is '/') && (index + 2 < pattern.Length && pattern[index + 2] is '/'))
        {
            index += 3;
            return new(GitIgnorePatternTokenKind.ZeroOrMoreDirectories);
        }

        // /**$
        if ((index > 0 && pattern[index - 1] is '/') && index + 2 == pattern.Length)
        {
            index += 2;
            return new(GitIgnorePatternTokenKind.AnyPathCharacters);
        }

        index++;
        return new(GitIgnorePatternTokenKind.SingleStar);
    }

    private bool TryReadCharacterClass(out string characterClass)
    {
        int endIndex = index + 1;
        if (endIndex < pattern.Length && pattern[endIndex] is '!' or '^')
        {
            endIndex++;
        }

        // The ']' character can be included in the character class if it is the first character after the opening '[' or after a negation character.
        if (endIndex < pattern.Length && pattern[endIndex] is ']')
        {
            endIndex++;
        }

        while (endIndex < pattern.Length && pattern[endIndex] is not ']')
        {
            endIndex++;
        }

        if (endIndex >= pattern.Length)
        {
            characterClass = string.Empty;
            return false;
        }

        characterClass = pattern[(index + 1)..endIndex];
        index = endIndex + 1;
        return true;
    }
}
