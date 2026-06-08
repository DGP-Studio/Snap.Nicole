The user might execute `git add`/`git restore` actively to observable/review your working progress, so DO NOT only rely on git status or related command to check files edits.

# Skills

## Explore external C# symbols

Extensively use/load the `ilspycmd-find-implementation` skill when writing C# codes and you are not sure about any external symbol's behavior

## Line Ending Normalization

`normalize-line-endings` skill requires be use/load and you may follow the instructions in that skill before completing the givin task.

# Project Structure

```
src
+-- Snap.Nicole
+-- Snap.Nicole.Native
+-- Snap.Nicole.SourceGeneration
```

- Snap.Nicole: The main project. It primarily uses C#/WinUI 3 to build an agentic, multifunctional toolbox application.
- Snap.Nicole.Native: A supporting project that uses C++/WinRT to help the main project interact with the Windows system and to polyfill features that WinUI 3 does not provide.
- Snap.Nicole.SourceGeneration: A supporting project and Roslyn source generator that reduces the overhead of writing repetitive code in the main project.

## Snap.Nicole

### Architecture and patterns

A SDK style project

- Extensively adopt Microsoft.Extensions.Hosting and Microsoft.Extensions.DependencyInjection to manage the lifetime of applications, services, and objects. Services are registered at `Program.cs`.
- Extensively adopt the Model-View-ViewModel (MVVM) pattern to handle data presentation and user interactions.
- Extensively adopt Sentry to utilize it's error tracking and performance monitoring features.

### Type and file organization

- All top-level members like class/struct should be `internal` or `private`, unless XAML requires them to be `public` (for example, attached DependencyProperties and the `Application` class).
- Prefer one top-level type per file for models, result records, and enums. Avoid broad aggregate files such as `*Models.cs`; split related types into files named after each type.
- For all `record` types, do not use positional record declarations or positional construction patterns. Prefer explicit properties and object initializers so members remain self-describing.

### Syntax and style

- Always organize method arguments in single line, no matter how long they are. Wrap related arguments into context class/struct/record if necessary.
- Do not use expression-bodied syntax for methods, constructors, operators, or conversions. Lambdas or expressions inside method/property bodies are unaffected.
- When comparing an object with `null`, use `==` `!=` for WinRT Projection objects and the `is` `is not` pattern for all other types.
- For read-only properties, do not use direct expression-bodied declarations like `Property => value;`; use an expression get accessor instead, for example `Property { get => value; }`. Keep accessors in the same line whenever possible.
- For non-constant `string` or `string?` values that need an empty string, use `string.Empty` instead of `""`. Empty string literals are allowed only for constants (especially inside `[Attribute]` where `string.Empty` is not applicable) or the `is pattern`.
- Use `Interlocked.Exchange` for atomic read-modify-write operations:
``` C#
if (Interlocked.Exchange(ref value, true))
{
    return;
}
```
instead of separate read and write operations:
``` C#
if (value)
{
    return;
}

value = true;
```

### Cryptography

- Perfer uisng `System.Security.Cryptography.CryptographicOperations` for general oneshot usage over certain types like `SHA256`,`MD5`

### String comparision

- Always normalize strings to uppercase before comparison when case-insensitive matching is required and `StringComparison.OrdinalIgnoreCase` is unavailable.

### Resources and reuse

- In `.resx` resources, single-line user-visible text should not end with a sentence-ending period. Preserve meaningful punctuation such as ellipses, URLs, file extensions, or multi-line prose.
- Do not reinvent the wheel when runtime libraries already provide equivalent functionality; use the `ilspycmd` command-line tool extensively to verify existing implementations before adding new code.

### How to build `Snap.Nicole`

Snap.Nicole depends on the Snap.Nicole.Native c++ project, so dotnet build won't work properly.
In order to build the project correctly and reduce build fails, you need to:

1. Run cmd `where /r "C:\Program Files\Microsoft Visual Studio" msbuild` to locate msbuild
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

No information provided yet.

# Encoding

Windows PowerShell reads files using the active ANSI code page when no encoding is specified.
Files that contain non-ASCII text, may display as mojibake and XML parsing can report false structural errors.

- Prefer `rg` for searching text because it reads UTF-8 correctly in this repo.
- When using PowerShell to read or parse text files, specify UTF-8 explicitly, for example `Get-Content -Raw -Encoding UTF8 src\Snap.Nicole\Resources\SR.resx`.
- When parsing XML resources in PowerShell, use `[xml]$doc = Get-Content -Raw -Encoding UTF8 path\to\file.resx` instead of relying on the default encoding.
- When writing files from PowerShell, specify the intended encoding explicitly to avoid accidental re-encoding.
