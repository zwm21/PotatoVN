# AGENTS.md

## Purpose
- This repository contains a WinUI 3 desktop client, its NUnit test project, and a VitePress docs site.
- Use this file as the fast path for build/test/style guidance before making changes.
- When repo-specific instruction files conflict with generic habits, prefer the repo-specific instruction files.

## Read These First
- Copilot rule found: `.github/copilot-instructions.md`.
- Its instruction is mandatory: before doing anything else, read `.kilocode/rules/project-main.md`.
- If you work on the client, also read `.kilocode/rules/project-info-galgamemanager.md`.
- If client work changes UI/XAML/custom controls, also read `.kilocode/rules/project-info-controls.md`.
- Additional agent docs exist under `.github/.clinerules/`; they are not Cursor rules, but they contain useful repo knowledge.
- `.github/.clinerules/main.md` asks agents to update the `project-info` knowledge files with general reusable knowledge after finishing a task.
- No `.cursor/rules/` directory or `.cursorrules` file was found at repo root.

## Repository Map
- `GalgameManager/`: main WinUI 3 desktop client.
- `GalgameManager.Core/`: shared/core .NET library.
- `GalgameManager.Test/`: client-side NUnit tests.
- `PotatoVN.Doc/`: docs site built with VitePress.
- `AuthServer/`: separate Flask-based auth component (Deperated).
- `GalgameManager.Tool/`: separate C++ utility project.
- `Directory.Build.props`: test platform defaults and Windows App SDK test overrides.
- `.editorconfig`: main formatting and naming rules.

## Environment Notes
- The client is Windows-specific and uses WinUI 3 plus Windows App SDK.
- Client packaging is MSBuild-based and produces a self-contained portable build only; the repository no longer builds MSIX/Store packages.
- Docs site uses Node/npm inside `PotatoVN.Doc/`.

## Restore
- From repo root: `dotnet restore GalgameManager.sln`
- If touching docs: in `PotatoVN.Doc/`, run `npm install`

## Build Commands
- Build everything in the .NET solution: `dotnet build GalgameManager.sln`
- Build only the client project: `dotnet build GalgameManager/GalgameManager.csproj`
- Build docs: in `PotatoVN.Doc/`, run `npm run build`
- Start docs dev server: in `PotatoVN.Doc/`, run `npm run dev`
- Preview built docs: in `PotatoVN.Doc/`, run `npm run preview`

## Client Packaging
- Distribution is a self-contained portable build only (no MSIX/Store packages are produced by this repository).
- Local one-shot build and publish to a versioned folder:
```powershell
pwsh -File GalgameManager\BuildScripts\Publish-Portable.ps1            # x64
pwsh -File GalgameManager\BuildScripts\Publish-Portable.ps1 -Platform arm64
```
- The script publishes to `publish/portable/` first and then renames it to `publish/PotatoVN-<dynamic version>-<platform>-portable/`, so the same base version yields a distinct folder per commit. The version comes from the `BuildVersionInfo.txt` that the build generates, not from a hardcoded name.
- CI equivalent is `.github/workflows/build.yml`, which publishes, verifies that `Microsoft.WindowsAppRuntime.dll` is bundled, and compresses to 7z.
- `Properties/PublishProfiles/win10-<platform>.pubxml` and `Package.appxmanifest` are load-bearing even though their names look MSIX-specific: the profile supplies `SelfContained`/`RuntimeIdentifier`/`PublishReadyToRun`, and the manifest supplies the base version for the assembly version, the dynamic version, and update comparisons. Do not delete them.
- Expect client packaging to require a Windows machine with the proper WinUI/Windows App SDK toolchain. Visual Studio / VS Build Tools are not required: with only the .NET 8 SDK installed, `dotnet build`/`dotnet publish` build the client (Windows App SDK and Windows SDK build tools come from NuGet).

## Lint And Analyzer Guidance
- No dedicated repo-level `lint` script was found.
- Treat `dotnet build` as the main analyzer/lint pass for C# projects.
- NUnit analyzers are enabled in the test project, so test builds also surface style/test issues.
- Docs site currently has build/dev scripts but no checked-in lint script.
- Use `.editorconfig` plus existing file style as the formatting authority.

## Test Commands
- Run all client tests: `dotnet test GalgameManager.Test/GalgameManager.Test.csproj`
- The client test project automatically uses `GalgameManager.Test/test.runsettings`.
- That runsettings file forces x64 and excludes tests in `Category("Phraser")` by default.
- Repo rule from `.kilocode/rules/project-info-galgamemanager.md`: after editing client code, run `GalgameManager.Test`.
- If you did not change parser code, you can skip parser tests because they call external services and are slow.

## Running A Single Test
- Preferred pattern: `dotnet test <project> --filter "FullyQualifiedName~Namespace.Class.Method"`
- Single client test example:
```powershell
dotnet test GalgameManager.Test/GalgameManager.Test.csproj --filter "FullyQualifiedName~GalgameManager.Test.ProjectTest.Version_ShouldMatch_PackageAppxmanifest"
```
- To run a whole test class, filter on the class name instead of the method name.
- Be aware that the default client runsettings still excludes `Phraser` tests.
- `Phraser` tests hit external APIs; only run them when changing parser/scraper logic.
- Some upgrade/E2E tests launch the built client executable and are more environment-sensitive.

## Core C# Style
- Use file-scoped namespaces.
- Keep `using` directives outside the namespace.
- Prefer `System.*` usings before other usings when organizing imports.
- Use 4 spaces for indentation; do not use tabs.
- Preserve final newlines and trim trailing whitespace.
- Preserve the existing line endings of the file you edit; C# files are typically CRLF here.
- Use Allman braces for types, methods, properties, and control blocks.
- Short guard clauses without braces exist in this repo; follow local style when editing nearby code.

## Types And Language Features
- `ImplicitUsings` is enabled in main projects; do not add redundant usings.
- Nullable reference types are enabled in the client and test projects; respect nullability annotations.
- `GalgameManager.Core` does not explicitly enable nullable in its project file, so be extra careful crossing that boundary.
- Use `?` for truly optional references.
- Use `!` sparingly and only when the invariant is already established.
- Match local style on `var` versus explicit types; this repo uses both.
- Explicit types are common for service, model, DTO, and interface variables.
- `var` is common when the right-hand side is obvious.
- Target-typed `new()`, collection expressions like `[]`, and modern C# syntax are already in use.

## Naming Conventions
- Use PascalCase for classes, structs, enums, methods, properties, and public members.
- Interfaces should start with `I`.
- Private fields should use `_camelCase`.
- Injected dependencies are usually stored in private readonly `_camelCase` fields in the client.
- Test method names are usually descriptive sentence-style names with underscores.

## Dependency Injection And Architecture
- The overall architecture is MVVM on the client.
- Prefer constructor injection over service location.
- Client ViewModels should keep UI state and orchestration logic, not heavy persistence/networking logic.
- Business logic should live in services/models rather than in XAML code-behind.
- Code-behind is acceptable for view-only behavior, event wiring, or platform-specific UI glue.

## Client MVVM Patterns
- Use CommunityToolkit.Mvvm patterns already present in the codebase.
- Prefer `ObservableObject`/`ObservableRecipient` for ViewModels and observable helpers.
- Prefer `[ObservableProperty]` over hand-written backing boilerplate when consistent with the file.
- Prefer `[RelayCommand]` and related toolkit attributes for commands.
- Use `partial` ViewModels where source generators are expected.
- Follow existing patterns for `INavigationAware` and service-backed page initialization.

## XAML And UI Conventions
- Prefer `x:Bind` over classic `{Binding}` when working with ViewModel-backed pages.
- Use typed templates with `x:DataType` where the surrounding file already does so.
- Reuse existing custom controls and patterns instead of inventing new ones.
- Common custom controls called out by repo docs: `Setting`, `SettingToggleSwitch`, `ComboBoxWithI18N`, and `Panel`.
- Keep UI additions consistent with the existing WinUI 3 style and MVVM structure.

## Localization Rules
- XAML localization uses `x:Uid`.
- C# localization typically uses `"Key".GetLocalized()` or `ResourceExtensions.GetLocalized(...)`.
- For `ContentDialog`, localize the root with `x:Uid` and use `.Title`, `.PrimaryButtonText`, and `.SecondaryButtonText` keys.
- For child controls, use the expected suffix like `.Text`, `.Label`, `.Content`, or `.ToolTipService.ToolTip`.
- Do not directly edit `.resw` files.
- Use `GalgameManager/Strings/resw_tool.py` to search, add, or update localization entries.

## Error Handling
- On the client, catch exceptions at UI/page boundaries and route them through `IInfoService` when appropriate.
- `InfoService` is the central notification/logging path and writes logs through Serilog.
- Avoid silently swallowing exceptions unless the code is intentionally best-effort and the surrounding style already does so.

## Test Conventions
- NUnit is the test framework used by `GalgameManager.Test`.
- Client tests use categories such as `Phraser` and `E2E`.
- Default client test runs exclude `Phraser` tests via runsettings.
- Some E2E tests are non-parallel and require a built unpackaged x64 client executable.

## Practical Editing Advice
- Follow the existing file's local style before applying generic preferences.
- Do not rewrite large areas just to normalize style.
- When touching localization-backed UI, update strings through `resw_tool.py` and keep `x:Uid` naming consistent.
- When adding settings, follow the existing pattern: setting key, default value, ViewModel property, UI control, localization.

## Quick Pre-PR Checklist
- Build the relevant project(s).
- Run the most relevant tests; at minimum run the test project for the area you changed.
- For client changes, run `GalgameManager.Test` unless the change is clearly isolated and non-code.
- For parser changes, explicitly consider `Phraser` tests.
- For docs changes, run `npm run build` in `PotatoVN.Doc/`.
