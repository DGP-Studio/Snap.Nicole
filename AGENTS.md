The user might execute `git add`/`git restore` actively to observable/review your working progress, so DO NOT only rely on git status or related command to check files edits.

# Skills

## Explore external C# symbols

Extensively use/load the `ilspycmd-find-implementation` skill when writing C# codes and you are not sure about any external symbol's behavior

## Line Ending Normalization

`normalize-line-endings` skill requires be use/load and you may follow the instructions in that skill before completing the givin task.

# C# Type Members Layout Order

**IMPORTANT**:

<type-member-layout>
When writing new code or editing existing code, arrange type members in the following order:

- Fields
- Constructors
- Destructor
- Nested Delegates
- Events
- Nested Enums
- Nested Interfaces
- Properties
- Indexeres
- Conversion operators
- Other Operators
- Method
- Record Struct/Struct
- Record (Class)/Class

Then, within each member type, arrange members by accessibility modifier:

- public
- internal
- protected internal
- protected
- private protected
- private

Then, within the same accessibility and member type, arrange members in the following order:

- const
- static readonly
- readonly
- normal
</type-member-layout>

Keep related members as close together as possible without violating the rules above.
**Note**: this is only guidance for member placement order and does not necessarily mean every member type must be present in a type

# Project Structure

```
src
+-- Snap.Nicole
+-- Snap.Nicole.Native
+-- Snap.Nicole.SourceGeneration
```

- Snap.Nicole: The main project. It primarily uses C#/WinUI 3 to build an agentic, multifunctional toolbox application.
- Snap.Nicole.Native: A supporting project that uses C++/WinRT to help the main project interact with the Windows system and to polyfill features that WinUI 3 does not provide.
- Snap.Nicole.SourceGeneration: A supporting `netstandard2.0` Roslyn incremental source generator project consumed by the main project as an analyzer to reduce repetitive code in settings models, WinUI dependency properties, native callback wrappers, and resource accessors.

## Snap.Nicole

### Architecture and patterns

- A SDK style project
- `global using` declarations are configured in `Program.cs`
- Extensively adopt Microsoft.Extensions.Hosting and Microsoft.Extensions.DependencyInjection to manage the lifetime of applications, services, and objects. Services are registered at `Program.cs`.
- Extensively adopt the Model-View-ViewModel (MVVM) pattern to handle data presentation and user interactions.
- Extensively adopt Sentry to utilize it's error tracking and performance monitoring features.

### Type and file organization

- All top-level members like class/struct should be `internal` or `private`, unless XAML requires them to be `public` (for example, attached DependencyProperties and the `Application` class).
- Prefer one top-level type per file for models, result records, and enums. Split related types into files named after each type.
- For all `record` types, do not use positional record declarations or positional construction patterns. Prefer explicit properties and object initializers so members remain self-describing.

### Syntax and style

**IMPORTANT**: Balance **high performance** with **clean code** when writing.
**IMPORTANT**: Avoid useless micro-abstraction.
**IMPORTANT**: This project is in early development/close beta; forward/backward compatibility is not a concern.

- Always try your best to avoid memory leak. Especially when:
	- Subscribing events, which is extremely dangerous when not properly unsubscribed.
	- Passing closure as callback argument, if the callback is not executed immediately, the callee might stores it and prevent GC to opt in.

- DO
	- Always normalize strings to uppercase before comparison when case-insensitive matching is required and `StringComparison.OrdinalIgnoreCase` is unavailable.
	- Always organize method arguments in single line, unless they already in multiple lines when editing files.
	- Always add braces to `using` statements, except for single-line `using` declarations.
	- Wrap related arguments into context class/struct/record if necessary (Consider this when having more the 4 arguments).
	- Prefer expression Property getter and setter, for example `Property { get => value; }`. Keep accessors in the same line whenever possible.
	- Prefer FrozenSet/FrozenDictionary when creating immutable set/map for fast lookup operations.
	- For non-constant `string` or `string?` values that need an empty string, use `string.Empty`. Empty string literal (`""`) is only allowed for constants (especially inside `[Attribute]` where `string.Empty` is not applicable) or the `is` `is not` pattern.
	- When comparing an object with `null`, use `==` `!=` for WinRT Projection objects and the `is` `is not` pattern for other types.
	- When resolving multiple services from DI,  use single `IServiceProvider serviceProvider` parameter, unless some parameters must be directly injected.

- DO NOT
	- Do not use `await using` when the actual type implments both `IDisposable` & `IAsyncDisposable`.
	- Do not use expression body syntax for methods, constructors, operators, or conversions. Lambdas or expressions inside method/property bodies are unaffected.
	- Avoid closures that capture more than 4 variables. Closures should generally be minimized; prefer method overload that accepts a state argument. Non-static local functions should generally be avoided.

### Implementation choices

- Perfer uisng `System.Security.Cryptography.CryptographicOperations` for general oneshot usage over certain types like `SHA256`,`MD5`

### Resources and reuse

- In `.resx` resources, single-line user-visible text should not end with a sentence-ending period. Preserve meaningful punctuation such as ellipses, URLs, file extensions, or multi-line prose.
- Do not reinvent the wheel when runtime libraries already provide equivalent functionality; use the `ilspycmd` command-line tool extensively to verify existing implementations.

### How to build `Snap.Nicole`

Snap.Nicole depends on the Snap.Nicole.Native c++ project, so dotnet build won't work properly.
In order to build the project correctly and reduce build fails, you need to:

1. Run cmd `where /r "C:\Program Files\Microsoft Visual Studio" msbuild` to locate msbuild (If you already know the path, skip this step)
2. Run cmd `"path/to/msbuild" "path/to/Snap.Nicole.csproj" -restore -t:Build -p:Configuration=Debug -p:Platform=x64 -nologo -verbosity:minimal -clp:ErrorsOnly`

If CMD is not available and PowerShell is, the script above may be converted to PowerShell instead.

## Snap.Nicole.Native

- COM ABI
	- The project exposes a hand-written Classic COM ABI consumed by `src/Snap.Nicole/Native/*`, not generated WinRT metadata.
- Project file maintenance
	- The native `.vcxproj` is not SDK-style globbing.
	- New `.cpp` files must be added to `src/Snap.Nicole.Native/Snap.Nicole.Native.vcxproj` as `ClCompile`.
	- Also update `Snap.Nicole.Native.vcxproj.filters` so Visual Studio shows the file in the expected filter.
- Native coding conventions
	- Extensively uses Windows Implementation Library (Wil) helpers macros/methods/classes to keep the control flow straight and the code clean.

## Snap.Nicole.SourceGeneration

The project is a Roslyn analyzer/source-generator assembly, not a runtime dependency of `Snap.Nicole`.

- Targeting and dependencies
	- The project targets `netstandard2.0` so it can run inside the compiler/analyzer host.
	- `AddGenerationTimeReferences` packs resolved compile-time dependencies into `analyzers/dotnet/cs`; preserve this when adding generator-time dependencies needed by the analyzer host.
- Maintenance rules
	- Prefer incremental source-generator APIs over classic generator patterns.
	- Prefer Roslyn syntax factories and shared helpers in `Primitive`/`WellKnownSyntax` over string-concatenated C#.
	- When adding a new app-facing marker attribute or interface, define it in `src/Snap.Nicole`, add the fully qualified metadata name to `WellKnownMetadataNames`, and keep the generator matched by metadata name.
	- When adding a new `.resx` file that should participate in generation, include it as `AdditionalFiles` in `src/Snap.Nicole/Snap.Nicole.csproj`.

# Encoding

Windows PowerShell reads files using the active ANSI code page when no encoding is specified.
Files that contain non-ASCII text, may display as mojibake and XML parsing can report false structural errors.

- When using PowerShell to read or parse text files, specify UTF-8 explicitly, for example `Get-Content -Raw -Encoding UTF8 src\Snap.Nicole\Resources\SR.resx`.
- When parsing XML resources in PowerShell, use `[xml]$doc = Get-Content -Raw -Encoding UTF8 path\to\file.resx` instead of relying on the default encoding.
- When writing files from PowerShell, specify the intended encoding explicitly to avoid accidental re-encoding.

# Code of Conduct

- When the user's instructions conflict with any of the above, point out the conflict, pause any changes, and suggest that the user modify this document.