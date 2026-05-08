# Contributing to MapLibre Unity

Thanks for your interest in contributing! This document describes how
to file issues, set up a local development environment, and submit
pull requests.

For AI coding agents (Codex, Copilot, Cursor, Claude Code, ...), the
authoritative project rules live in [`AGENTS.md`](AGENTS.md). This
file is the human-friendly summary; the two are kept in sync.

## Code of Conduct

This project follows the [Contributor Covenant 2.1](CODE_OF_CONDUCT.md).
By participating, you agree to uphold its terms.

## Reporting Issues

Please file issues on the
[GitHub issue tracker](https://github.com/KazukiKuriyama/maplibre-unity/issues).

When filing a bug report, include:

- **Unity version** (e.g. `6000.3.10f1`) and target platform (Editor /
  Standalone — verified; iOS / Android — untested but reports welcome;
  WebGL — unsupported).
- **URP version** and render pipeline asset name if non-default.
- **Repro steps** — ideally a minimal sample scene or a stripped-down
  `style.json`. Attach the relevant Unity Console output.
- **Expected vs. actual** behavior, including comparison with
  [MapLibre GL JS](https://maplibre.org/maplibre-gl-js/docs/) when the
  feature has an upstream counterpart.

Feature requests are welcome too — please describe the use case and,
if possible, the matching MapLibre GL JS or Native API surface.

## Development Setup

### Prerequisites

| Tool | Version |
|---|---|
| Unity | 6 (6000.3.10f1) or newer in the 6000.3.x line |
| Render pipeline | URP 17.x |
| .NET SDK | 8.0+ (only required to build DocFX site or run dotnet tooling locally) |
| Git LFS | not used — keep binary assets out of the repo |

### Clone and open

```sh
git clone https://github.com/KazukiKuriyama/maplibre-unity.git
cd maplibre-unity
# Open the folder in Unity Hub. The first import takes a few minutes.
```

The library source lives under `Packages/com.kazukikuriyama.maplibre-unity/` —
Unity loads it as an embedded UPM package automatically. Sample scenes
ship under `Packages/com.kazukikuriyama.maplibre-unity/Samples/` and
appear directly in the Project window; no Package Manager import step is
needed.

1. Open `Packages/MapLibre Unity/Samples/Home/HomeScene.unity` and press
   Play to verify the project boots.
2. The first time you open this repo, `Tests/EditMode/EnsureSamplesImported.cs`
   automatically registers every sample scene in **File → Build Profiles**
   so HomeScene's `SceneManager.LoadScene` calls and the PlayMode smoke
   test can resolve them. If you ever clear Build Settings manually,
   delete the SessionState marker (`Edit → Clear All PlayerPrefs` is too
   aggressive — restart the Editor instead) to re-trigger registration.
3. Samples under `Packages/` are read-only. To experiment, copy the
   demo folder you want to modify into `Assets/` first.

For runtime / editor code changes edit files directly in
`Packages/com.kazukikuriyama.maplibre-unity/Runtime/` / `Editor/` and
Unity recompiles in place.

### Branching

- Branch off `main`. Use a descriptive prefix:
  `feature/<thing>`, `fix/<thing>`, `docs/<thing>`, `refactor/<thing>`.
- Keep changes focused. One PR per concern.
- Rebase rather than merge `main` into your branch when integrating
  upstream changes (keeps history linear).

## Testing

The project ships with two test assemblies. Both run via Unity's Test
Runner (`Window → General → Test Runner`).

### EditMode tests

`Packages/com.kazukikuriyama.maplibre-unity/Tests/EditMode/` — pure C# unit tests covering
the Expression evaluator, Style parser, color-space conversions,
PMTiles, MBTiles, and similar logic without Unity frame loop. Runs in
a few seconds.

```
Window → General → Test Runner → EditMode → Run All
```

### PlayMode tests

`Packages/com.kazukikuriyama.maplibre-unity/Tests/PlayMode/` — loads every sample
scene in sequence and asserts that no exceptions occur during
initialization. Each scene early-exits as soon as every `MapLibreMap`
reports `IsInitialized` past a 1.5 s settle window (capped at 3 s).
Full suite finishes in roughly 90–120 seconds.

> **Note:** PlayMode tests rely on every sample scene being registered
> in Build Settings. `Tests/EditMode/EnsureSamplesImported.cs` does this
> automatically on Editor startup, so a fresh clone usually "just works".
> If Build Settings is empty (and the test reports a single named
> failure), restart the Editor to re-trigger registration.

```
Window → General → Test Runner → PlayMode → Run All in Player
```

Run both before submitting a PR.

### Adding tests

- Behavior with deterministic input/output → EditMode test.
  See `Tests/EditMode/Expression/` for examples.
- Behavior that depends on Unity's frame loop, scene loading, async
  fetches, or rendering → PlayMode test.

## Coding Standards

### Language

- C# (`.NET Standard 2.1`). No native plugins.
- All runtime code goes under
  `Packages/com.kazukikuriyama.maplibre-unity/Runtime/<Subsystem>/` using the
  existing namespace conventions (see `AGENTS.md` for the full layout).
- URP-compatible shaders only
  (`"RenderPipeline" = "UniversalPipeline"` tag).

### Naming (Microsoft conventions, enforced via `.editorconfig`)

| Kind | Convention | Example |
|------|------------|---------|
| namespace, class, struct, enum, method, property, event | PascalCase | `MapLibreMap`, `GetPaintProperty()` |
| public / protected fields | PascalCase | `MaxZoom` |
| private / internal instance fields | `_camelCase` | `_tileManager` |
| const | PascalCase | `MaxLogLines` |
| static readonly | PascalCase | `DefaultColors` |
| local variables, parameters | camelCase | `tileId`, `zoomLevel` |
| interface | `I` + PascalCase | `ISource` |
| type parameter | `T` + PascalCase | `TValue` |

### Input System

The project uses Unity's new Input System package.
`UnityEngine.Input` (legacy API) is forbidden — use
`Keyboard.current`, `Mouse.current`, etc. from
`UnityEngine.InputSystem`.

### UI

Prefer UI Toolkit (`UIDocument`, `VisualElement`, USS / UXML). Fall
back to uGUI (`UnityEngine.UI`) only when UI Toolkit cannot satisfy
the requirement. When dynamically creating a `UIDocument`, always
assign a `PanelSettings` asset; otherwise the UI does not render.

### Documentation language

All repository-facing documentation must be written in English. This
covers `README.md`, `CHANGELOG.md`, sample `README.md` files, XML
`<summary>` doc comments, and any user-facing strings shipped with
the library. Issue / PR discussions and chat replies in other
languages are fine.

## OSS Quality Guidelines

Please uphold these standards on every change:

### API design

- Do not silently override user settings (Camera properties, Inspector
  values, etc.). If the library needs to control a shared Unity
  resource, expose a `[SerializeField]` toggle (default ON) so users
  can opt out.
- No magic numbers — configurable values should be `[SerializeField]`
  fields or named constants.
- Do not break existing overloads. Add new overloads instead of
  changing signatures.
- Prefer adding optional parameters with sensible defaults rather than
  changing the semantics of existing call paths.

### MapLibre GL JS parity

Before implementing a feature, check how MapLibre GL JS handles the
same problem and match the design approach where it makes sense. From
the user's perspective, the library should behave identically to the
JS version (same tile coverage, same pitch / bearing behavior, same
expression semantics).

### Unity integration

Assume the camera and other Unity objects may be shared with non-map
content. Avoid side effects on objects the user did not explicitly
hand to the map. Prefer composition over modification — add
components rather than mutating existing ones.

## Pull Request Workflow

1. Open an issue first for non-trivial changes so we can align on
   scope before you spend time on it. Bug fixes and small docs PRs
   can skip this step.
2. Create a feature branch off `main`.
3. Make focused commits with clear messages. Imperative mood
   (`Add foo`, `Fix bar`) is preferred. Reference the issue number
   when relevant (`Fix #123: ...`).
4. Run EditMode and PlayMode tests locally.
5. Update `CHANGELOG.md` under the `## [Unreleased]` section
   describing the user-visible change.
6. Open a PR against `main`. Fill in the template:
   - **What** — short summary.
   - **Why** — motivation / linked issue.
   - **Test plan** — what you ran, what you visually verified.
   - **Screenshots / recordings** for any visible change.
7. Be prepared to iterate. Reviewers may request changes; please rebase
   and force-push the branch (this repo prefers a linear history).

### What gets rejected

- Native plugins or platform-specific binary dependencies.
- Style spec deviations without a documented reason.
- Changes that break existing public API without a deprecation path.
- PRs that disable tests to make CI green.

## Documentation Changes

End-user prose lives under `Documentation~/`. The `~` suffix tells
Unity to skip the folder during asset import, so updates do not
trigger a recompile.

To preview the DocFX site locally:

```sh
dotnet tool install -g docfx
# Open the project in Unity once so the source paths resolve.
docfx Documentation~/docfx.json --serve
# Visit http://localhost:8080
```

Add new pages to `Documentation~/toc.yml` so they appear in the
sidebar.

## License

By contributing, you agree that your contributions will be licensed
under the [MIT License](LICENSE) that covers the project.

## Questions

If something here is unclear or you are unsure how to proceed, open a
GitHub Discussion or a draft PR — we are happy to help.
