namespace Snap.Nicole.Services.AI.Agent.Workspace.GrepTool;

internal enum GitIgnorePatternTokenKind
{
    Literal,
    SingleStar,             // *
    SingleCharacter,        // ?
    CharacterClass,         // [abc]
    ZeroOrMoreDirectories,  // ^**/ or /**/
    AnyPathCharacters,      // /**
}
