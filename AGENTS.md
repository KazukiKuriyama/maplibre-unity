# Project Instructions for AI Coding Agents

This file is the canonical source for AI-coding-agent context on this
repository. Codex CLI, GitHub Copilot Workspace, Cursor, Claude Code, and
similar tools all look for an `AGENTS.md` at the repo root, so any rule
written here applies uniformly. (`CLAUDE.md` is preserved as a pointer
to keep older Claude-only setups working.)

## Project Overview

A Unity port of MapLibre GL JS. Pure C# implementation (no native plugins),
targeting full MapLibre Style Spec compliance.

## Tech Stack

- Unity 6 (6000.3.10f1) + URP 17.3.0
- C# (.NET Standard 2.1)
- Newtonsoft.Json (com.unity.nuget.newtonsoft-json 3.2.1)
- Unity Input System (1.18.0)
- TextMeshPro (com.unity.textmeshpro) — used to render text and icons in
  Symbol layers. TMP Essential Resources must be imported (not required
  when the style supplies a `glyphs` URL — the SDF text path is used as
  a fallback in that case).

## Code Structure

All source code lives under `Packages/com.kazukikuriyama.maplibre-unity/` —
loaded as an embedded UPM package when this repo is opened in Unity.

```
Packages/com.kazukikuriyama.maplibre-unity/
  package.json    UPM manifest (name, version, dependencies, samples)
  Runtime/
    Core/         MapLibreMap (main MonoBehaviour, split across
                  .Async / .Camera / .Events / .FeatureState / .Input /
                  .Layers / .Sources / .Style partials), MapState,
                  MapConstants, MapEvent / MapEventSystem / MapEventType,
                  FeatureQuery, GeometryHitTest, QueryFeature
    Coordinates/  LngLat, LngLatBounds, MercatorCoordinate,
                  CoordinateConversion
    Style/        StyleSpec, StyleSource, StyleLayer (declares
                  ICustomLayer), StyleParser, TileJSON, SpriteAtlas,
                  TerrainDefinition
    Expression/   Expression, ExpressionParser, EvaluationContext
                  (declares IFeatureStateStore), ColorSpaceConversion
    Tiles/        TileID, TileGrid, TileManager, TileState
    Source/       ISource, RasterTileSource, VectorTileSource,
                  GeoJsonSource, GeoJsonToVectorTile, ImageSource,
                  ICustomRasterSource, ICustomVectorSource,
                  TileCache, VectorTileCache, TileDiskCache,
                  HttpRetryPolicy, TransformRequest, SpriteLoader,
                  SuperclusterLite, GlyphSource, GlyphAtlas,
                  GlyphPbfParser, AwaitableExtensions,
                  PMTiles/, MBTiles/
    Rendering/    ILayerRenderer, MapRenderer, RasterTileRenderer,
                  VectorTileRenderer, BackgroundRenderer,
                  CircleRenderer, FillExtrusionRenderer,
                  HeatmapRenderer (+ HeatmapColorRamp),
                  HillshadeRenderer, ImageRenderer, SkyRenderer,
                  SymbolRenderer (.LinePlacement / .TextAndIcons
                  partials), SdfTextMeshBuilder, SdfLineTextMeshBuilder
    Pool/         ObjectPool, TileObjectPool
    Camera/       MapCamera, MapInputHandler, MapAnimator,
                  FreeCameraOptions
    Terrain/      TerrainManager (DEM-based 3D terrain)
    VectorTile/   VectorTileData, VectorTileParser,
                  VectorTileMeshBuilder, GeometryDecoder,
                  EarClipTriangulator, PbfReader
    UI/           MapControlsOverlay, Marker, MarkerManager, Popup
                  (+ MapPanelSettings.asset)
  Shaders/        MapLibreRasterTile / Background / BackgroundPattern /
                  Fill / FillPattern / Line / LinePattern /
                  LineGradient / Circle / FillExtrusion /
                  FillExtrusionPattern / Heatmap / Hillshade / Sky /
                  Icon / SdfText / TerrainTile
  Editor/         MapLibreMapEditor, FontSetupWindow, TMPEssentialsCheck
  Samples/        HomeScene + per-feature demo scenes (bundled with
                  the package; visible directly under Packages/ in
                  the Project window — read-only, no import needed)
  Tests/
    EditMode/     Coordinates / EarClipTriangulator / Expression /
                  StyleParser / ColorSpace / PMTiles / MBTiles unit tests
    PlayMode/     Sample scene smoke tests (run after importing samples)
```

## Namespaces

- `MapLibre.Unity` — Core, Coordinates, Tiles
- `MapLibre.Unity.Style` — Style parsing, ICustomLayer
- `MapLibre.Unity.Expressions` — Style expressions, IFeatureStateStore
- `MapLibre.Unity.Source` — Tile sources / caching, ICustom\*Source,
  GlyphSource / GlyphAtlas, PMTiles, MBTiles
- `MapLibre.Unity.Rendering` — Renderers, SdfTextMeshBuilder
- `MapLibre.Unity.Pool` — Object pooling
- `MapLibre.Unity.CameraControl` — Camera (NOT `Camera` to avoid the
  `UnityEngine.Camera` name clash)
- `MapLibre.Unity.VectorTile` — Vector tile parsing and mesh building

## Assembly Definitions

- `MapLibre.Unity` (Runtime) — references: `Unity.InputSystem`,
  `Newtonsoft.Json`, `Unity.TextMeshPro`
- `MapLibre.Unity.Editor` (Editor) — references: `MapLibre.Unity`
- `MapLibre.Unity.Samples` (Samples) — depends on `MapLibre.Unity`
- `MapLibre.Unity.Tests.EditMode` / `.PlayMode` — TestAssemblies +
  NUnit + `Newtonsoft.Json.dll` (precompiled DLL ref)

## Key Design Decisions

- Map center is always at Unity origin (0,0,0) to avoid floating-point
  precision issues at high zoom.
- `MaterialPropertyBlock` for per-tile / per-label parameters — never
  duplicate `Material` instances per tile.
- Event-driven tile lifecycle: `TileManager` fires `OnTileNeeded` /
  `OnTileExpired`; renderers listen and create / destroy GameObjects.
- Object pooling for tile GameObjects to reduce GC pressure.
- LRU tile cache (default 256 entries) + on-disk HTTP cache that honours
  `Cache-Control` / `ETag`.
- Max 6 concurrent HTTP requests per source.
- `Task.Run` for vector tile PBF parsing and mesh building (background
  thread). Glyph atlas lookups are `ConcurrentDictionary`-backed so
  background mesh builds can read state safely while the main thread
  mutates it.

## Git Workflow

- **Never `git commit` or `git push` without explicit instruction.** Even
  after finishing an edit, leave the working tree dirty until the user
  asks to commit (e.g., "コミットして" / "commit and push" / similar).
  Showing the diff or asking "shall I commit?" is fine; running the
  command is not.
- This applies to every change — tiny typo fixes included. The user
  reviews each change before it lands.
- When the user confirms, prefer batching related changes into a single
  commit rather than splitting unless they ask otherwise.
- Never `--force` push without an explicit, scoped request.

## Conventions

- All new runtime code goes under `Packages/com.kazukikuriyama.maplibre-unity/Runtime/`
  in the appropriate subdirectory.
- Use existing namespace conventions.
- Follow MapLibre Style Spec naming where applicable (e.g., property
  names match the spec).
- No native plugins — pure C# only.
- URP-compatible shaders only (use `"RenderPipeline"="UniversalPipeline"`
  tag).
- **Documentation language**: All repository-facing documentation must
  be written in English. Applies to `README.md`, `LICENSE`,
  `THIRD_PARTY_NOTICES.md`, sample `README.md`s, XML `<summary>` doc
  comments, and any in-code user-facing strings that ship with the
  library. Conversational chat replies remain in Japanese as directed;
  documentation files do not.
- **Input System**: This project uses the new Unity Input System
  package. `UnityEngine.Input` (legacy API) is forbidden. Use
  `Keyboard.current`, `Mouse.current`, etc. from
  `UnityEngine.InputSystem`.
- **UI**: Prefer UI Toolkit (UITK). Use `UIDocument`, `VisualElement`,
  USS / UXML. Fall back to uGUI (`UnityEngine.UI`) only when UITK can't
  satisfy the requirement.
- **`UIDocument` and `PanelSettings`**: When dynamically creating a
  `UIDocument`, always assign `PanelSettings`. Without it, the UI is
  not rendered. Pattern: `[SerializeField] private PanelSettings _panelSettings;`
  for inspector configurability, with a fallback that calls
  `Resources.FindObjectsOfTypeAll<PanelSettings>()` and reuses the
  asset MapLibreMap created.
- **UI Toolkit fonts**: Dynamically-created `Label` / `Button` / etc.
  must set `style.unityFontDefinition = StyleKeyword.None` and
  `style.unityFont = new StyleFont(font)`. The default panel SDF font
  is unset, so omitting these lines yields invisible text. Use
  `Font.CreateDynamicFontFromOSFont("Arial", 16)` for a runtime fallback.

### C# Naming (follows Microsoft conventions / enforced by `.editorconfig`)

This project follows the **Microsoft .NET / C# coding conventions**, not
Unity Technologies' internal C# style guide. Roslyn analyzer rules in
`.editorconfig` flag deviations as warnings.

| Kind | Convention | Example |
|------|------|----|
| namespace, class, struct, enum, method, property, event | PascalCase | `MapLibreMap`, `GetPaintProperty()` |
| public / protected fields | PascalCase | `MaxZoom` |
| private / internal instance fields | `_camelCase` | `_tileManager` |
| private static fields (non-readonly) | `_camelCase` | `_singletonCache` |
| const | PascalCase | `MaxLogLines` |
| static readonly | PascalCase | `DefaultColors` |
| Local variables, parameters | camelCase | `tileId`, `zoomLevel` |
| Interfaces | `I` + PascalCase | `ISource` |
| Type parameters | `T` + PascalCase | `TValue` |

**Do NOT use Unity-Technologies-style prefixes** (`m_`, `k_`, `s_`).
Unity's official C# Style Guide uses these for member / constant /
static fields, but this project does not — they are ruled out for
consistency with the wider .NET ecosystem and to keep the API surface
familiar to non-Unity contributors. Roslyn warnings will fire if you
introduce them. Concretely:

```csharp
// ❌ Unity-Technologies style — DO NOT use
private const int     k_MaxRetries = 3;
private int           m_CurrentRetry;
private static Foo    s_Instance;

// ✅ This project (Microsoft conventions)
private const int  MaxRetries = 3;
private int        _currentRetry;
private static Foo _instance;
```

## OSS Quality Guidelines

This is a public OSS library. All changes must meet the following
standards without needing to be asked:

### API Design

- **Never silently override user settings** (Camera properties,
  Inspector values, etc.). If the library needs to control a shared
  Unity resource, provide a `[SerializeField]` toggle (default ON) so
  users can opt out.
- **No magic numbers** — configurable values should be `[SerializeField]`
  fields or constants with clear names.
- **Don't break existing overloads** — add new overloads instead of
  changing signatures. Keep backward compatibility.
- **Default arguments instead of behaviour change** — prefer adding
  optional parameters (with sensible defaults) rather than altering the
  semantics of existing call paths.

### MapLibre GL JS Parity

- Before implementing a feature, consider how MapLibre GL JS handles
  the same problem. Match the design approach (e.g., `coveringTiles`
  uses frustum polygon projection, not simple AABB).
- The library should behave identically to the JS version from the
  user's perspective (same tile coverage, same pitch / bearing
  behaviour, same expression semantics).

### Unity Integration

- Assume the camera and other Unity objects may be shared with
  non-map content.
- Avoid side effects on objects the user didn't explicitly assign to
  the map.
- Prefer composition over modification — add components, don't mutate
  existing ones.

### Platform-specific code

- **WebGL Player builds run single-threaded** (no SharedArrayBuffer in
  the default config). When introducing new code that needs threading
  or filesystem access, wrap with `#if UNITY_WEBGL && !UNITY_EDITOR` and
  provide a fallback. Patterns already in use:
  - Background work: route `Task.Run` through `BackgroundTask.Run`
    (Runtime/BackgroundTask.cs). The wrapper inlines work on WebGL
    Player and forwards to `Task.Run` elsewhere.
  - Heavy parsers: expose an incremental `ParseLazy` (returns
    `IEnumerable`) alongside the eager `Parse` so the WebGL coroutine
    path can yield to the main loop with a per-frame budget. See
    `VectorTileParser` + `VectorTileSource.ParseTileBytesIncremental`.
  - Disk caching: skip on WebGL (`TileDiskCache.Enabled = false`,
    `GlyphSource._diskCacheDir = null`). The browser HTTP cache is the
    fallback.
  - OS font enumeration: WebGL has no `Font.GetPathsToOSFonts`; route
    through `SystemFontFallback.Resolve`, which returns
    `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` on WebGL.
- Test-runner tests (Tests/EditMode) execute on the Editor platform, so
  `#if UNITY_WEBGL && !UNITY_EDITOR` branches are NOT exercised by CI.
  Verify those manually by switching the Build Profile to WebGL and
  running a sample scene in the browser.

### Tests

- New runtime behaviour with deterministic input/output should land
  with an EditMode test under `Packages/com.kazukikuriyama.maplibre-unity/Tests/EditMode/`
  (Expression / StyleParser / ColorSpace / PMTiles / MBTiles patterns
  serve as references).
- Behaviour that requires Unity's frame loop (scene loading, async
  fetches, render pipeline) belongs to PlayMode under
  `Packages/com.kazukikuriyama.maplibre-unity/Tests/PlayMode/`.
- The EditMode suite is gated by GitHub Actions CI
  (`.github/workflows/test.yml`). Do not break the green build.

## Implementation Status

See [`CHANGELOG.md`](CHANGELOG.md) for the running list of features —
both released and unreleased — instead of duplicating the status here.
The phase numbering used historically (Phase 1 raster → Phase 11
remaining) is summarised in `README.md` under "Roadmap".
