# Project Instructions for AI Coding Agents

This file is the canonical source for AI-coding-agent context on this
repository. Codex CLI, GitHub Copilot Workspace, Cursor, Claude Code, and
similar tools all look for an `AGENTS.md` at the repo root, so any rule
written here applies uniformly. (`CLAUDE.md` is preserved as a pointer
to keep older Claude-only setups working.)

## Project Overview

MapLibre GL JS の Unity 版。C# 純粋実装 (ネイティブプラグインなし) で
MapLibre Style Spec 完全準拠を目指す。

## Tech Stack

- Unity 6 (6000.3.10f1) + URP 17.3.0
- C# (.NET Standard 2.1)
- Newtonsoft.Json (com.unity.nuget.newtonsoft-json 3.2.1)
- Unity Input System (1.18.0)
- TextMeshPro (com.unity.textmeshpro) — Symbol レイヤーのテキスト・アイコン
  描画に使用。TMP Essential Resources のインポートが必要 (style.glyphs URL
  を使う場合は不要 — SDF 経路にフォールバック)。

## Code Structure

All source code lives under `Packages/com.kazukikuriyama.maplibre-unity/` —
loaded as an embedded UPM package when this repo is opened in Unity.

```
Packages/com.kazukikuriyama.maplibre-unity/
  Runtime/
    Core/         MapLibreMap (main MonoBehaviour), MapState, MapConstants
    Coordinates/  LngLat, MercatorCoordinate, CoordinateConversion
    Style/        StyleSpec, StyleSource, StyleLayer, StyleParser, TileJSON
    Expression/   Expression, ExpressionParser, EvaluationContext, ColorSpaceConversion
    Tiles/        TileID, TileGrid, TileManager, TileState
    Source/       ISource, RasterTileSource, VectorTileSource, TileCache,
                  ICustomRasterSource, ICustomVectorSource,
                  GlyphSource, GlyphAtlas, GlyphPbfParser,
                  PMTiles/, MBTiles/
    Rendering/    RasterTileRenderer, VectorTileRenderer, SymbolRenderer,
                  CircleRenderer, FillExtrusionRenderer, HeatmapRenderer,
                  HillshadeRenderer, BackgroundRenderer, MapRenderer,
                  SdfTextMeshBuilder
    Pool/         ObjectPool, TileObjectPool
    Camera/       MapCamera, MapInputHandler, MapAnimator
    VectorTile/   VectorTileData, VectorTileMeshBuilder, GeometryDecoder, PbfReader
    UI/           MapControlsOverlay, Marker, Popup
  Shaders/        MapLibreRasterTile / Background / Fill / Line / FillPattern /
                  LinePattern / Circle / FillExtrusion / FillExtrusionPattern /
                  Heatmap / Hillshade / Sky / Icon / SdfText / TerrainTile
  Editor/
  Samples~/       HomeScene + per-feature demo scenes (opt-in via
                  Package Manager UI; trailing ~ hides them from
                  asset import — import them once for local dev)
  Tests/
    EditMode/     Expression / StyleParser / ColorSpace / PMTiles / MBTiles 単体テスト
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

### C# Naming (Microsoft 標準準拠 / `.editorconfig` で強制)

| 種類 | 規約 | 例 |
|------|------|----|
| namespace, class, struct, enum, method, property, event | PascalCase | `MapLibreMap`, `GetPaintProperty()` |
| public / protected フィールド | PascalCase | `MaxZoom` |
| private / internal インスタンスフィールド | `_camelCase` | `_tileManager` |
| const | PascalCase | `MaxLogLines` |
| static readonly | PascalCase | `DefaultColors` |
| ローカル変数, パラメータ | camelCase | `tileId`, `zoomLevel` |
| インターフェース | `I` + PascalCase | `ISource` |
| 型パラメータ | `T` + PascalCase | `TValue` |

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
