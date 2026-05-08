# Changelog

All notable changes to MapLibre Unity are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
intends to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
once 1.0 is cut. The history here is curated from commit messages — see
`git log` for the full record.

## [Unreleased]

## [0.2.0] - 2026-05-08

### Changed
- **Initial view priority now matches MapLibre GL JS** (Inspector >
  style JSON > 0). Previously the order was reversed (style JSON >
  Inspector), which conflicted with the upstream `new Map({zoom: 10,
  style: ...})` convention where constructor options override style
  defaults. The Inspector fields (`Initial Longitude` / `Latitude` /
  `Zoom` / `Bearing` / `Pitch`) now serialize as `NaN` by default,
  meaning "use the style JSON value." When set to a non-NaN value
  they override the style. The `MapLibreMap` Inspector grew explicit
  **Override** / **Use style** buttons and shows the effective
  `style.<field>` value as a hint when no override is active. As part
  of the same fix, `bearing` and `pitch` from the style JSON are now
  applied (previously the runtime ignored `Style.Bearing` /
  `Style.Pitch` entirely). All bundled sample scenes were updated so
  the five `_initial*` fields are `.nan` — their initial view comes
  from the corresponding `*Style.json`.
- **WebGL marked as unsupported in README.** The previous claim that the
  package "works on every Unity build target including WebGL" was wrong:
  vector tile parsing / mesh building rely on `Task.Run` + `lock`, and
  tile / glyph caches use synchronous `System.IO.File`, neither of which
  works under the default WebGL single-threaded runtime. A "Supported
  platforms" subsection was added under Requirements; PMTiles / MBTiles
  / sample-table mentions of WebGL were removed accordingly. WebGL
  support may be revisited later behind `#if UNITY_WEBGL` guards.
- **Package source moved to `Packages/com.kazukikuriyama.maplibre-unity/`** (was
  `Assets/MapLibreUnity/`). The repository now follows the canonical
  Unity embedded-package layout — Unity loads the package
  automatically when this repo is opened in Unity Hub. UPM install URL
  is now
  `https://github.com/KazukiKuriyama/maplibre-unity.git?path=Packages/com.kazukikuriyama.maplibre-unity#<tag>`.
- **Sample scenes ship bundled with the package.** Located at
  `Packages/com.kazukikuriyama.maplibre-unity/Samples/` (no trailing `~`),
  they appear directly in the Project window — no Package Manager
  "Import" step is needed and the path no longer carries a `<version>`
  segment. The `samples` array was removed from `package.json`. End
  users open `Packages/MapLibre Unity/Samples/Home/HomeScene.unity`
  to launch the demo browser. Samples under `Packages/` are read-only;
  copy a demo into `Assets/` to modify it.
- **`MapLibre > Font Setup` default save path** changed from
  `Assets/MapLibreUnity/Fonts` to `Assets/MapLibre/Fonts` so the
  generated `TMP_FontAsset` lands in the user's project rather than
  inside the (read-only) UPM package.

### Internal
- `Tests/EditMode/EnsureSamplesImported.cs` no longer triggers a UPM
  sample import (the samples ship in-package now). It still registers
  every sample scene in `EditorBuildSettings` on Editor startup, and
  additionally prunes any stale `Assets/Samples/MapLibre Unity/<version>/...`
  entries left over from the previous opt-in flow. The script is
  excluded from the UPM tarball (`Tests/` is stripped by the release
  workflow), so it never runs in end-user projects.

## [0.1.0] - 2026-04-28

First public release.

### Breaking
- **Camera animation `Duration` is now in milliseconds** (was seconds). Affects
  `EaseToOptions.Duration`, `FlyToOptions.Duration`, `FitBoundsOptions.Duration`,
  and the optional `duration` parameter on `MapLibreMap.ZoomTo` / `PanBy` /
  `PanTo` / `ZoomIn` / `ZoomOut` / `RotateTo` / `ResetNorth` / `SnapToNorth`.
  Defaults moved from `0.3f` / `0.5f` to `300f` / `500f`. Matches MapLibre GL JS
  `easeTo({duration: 300})` exactly. Callers passing literal seconds (e.g.
  `Duration = 1.5f`) need to multiply by 1000 (`Duration = 1500f`).
- **Source / loader public APIs migrated from `IEnumerator` (coroutine) to
  `Awaitable` (async).** The library no longer exposes any IEnumerator-typed
  methods on its public surface. Specifically:
  - `ImageSource.LoadFromUrl()` → `LoadFromUrlAsync()` (`Awaitable`)
  - `GeoJsonSource.LoadData()` → `LoadDataAsync()` (`Awaitable`)
  - `SpriteLoader.Load(url, onSuccess, onError)` →
    `SpriteLoader.LoadAsync(url)` returning `Awaitable<SpriteAtlas>`; throws
    `SpriteLoadException` on fetch / parse failure.
  - `TileJSONFetcher.Fetch(url, onComplete, onError)` →
    `TileJSONFetcher.FetchAsync(url)` returning `Awaitable<TileJSONData>`;
    throws on HTTP / parse failure.
  - `PMTilesArchive.Open(onReady, onError)` → `OpenAsync()` (`Awaitable`);
    throws `PMTilesException` on failure.
  - `PMTilesArchive.LoadTileAsync(z, x, y, onSuccess, onMissing, onError)` →
    `LoadTileAsync(z, x, y)` returning `Awaitable<byte[]>` (`null` on
    sparse-archive miss; throws on I/O / decode failure).
  - `PMTilesRasterSource` / `PMTilesVectorSource` constructors now accept an
    optional `MonoBehaviour coroutineHost` (defaults to `null`); the archive
    no longer needs a coroutine host since it runs entirely on `Awaitable`.
  Sample code in `PMTilesDemo.cs` was updated accordingly.

### Added
- **Awaitable camera / lifecycle / asset API** under `MapLibreMap.*Async`:
  `LoadAsync`, `OnceAsync`, `EaseToAsync`, `FlyToAsync`, `ZoomToAsync`,
  `PanToAsync`, `RotateToAsync`, `FitBoundsAsync`, `SetStyleAsync`,
  `SetStyleJsonAsync`, `LoadImageAsync`. Each returns Unity 6's
  `Awaitable` / `Awaitable<T>` so callers can `await` directly. All methods
  accept an optional `CancellationToken` that throws
  `OperationCanceledException` when signalled. Existing fire-and-forget
  methods (`EaseTo`, `SetStyle`, `LoadImage`, …) are kept unchanged.
- **Custom layers** via the new `ICustomLayer` interface. User-supplied code
  can render into the map's render order alongside built-in layers (mirrors
  MapLibre GL JS `type: "custom"`). Includes `OnAdd` / `Render` / `OnRemove`
  lifecycle and an optional `Prerender` hook for off-screen passes.
- **Custom sources** via `ICustomRasterSource` and `ICustomVectorSource`.
  Serve tiles from any backend — procedural generation, local files, custom
  HTTP, IPC, render-to-texture, etc. Vector PBF bytes flow through the
  existing parser pipeline so all expression / filter / paint features work
  unchanged.
- **`feature-state` runtime API**: `SetFeatureState` / `GetFeatureState` /
  `RemoveFeatureState` / `ClearFeatureState`, with the corresponding
  `["feature-state", key]` expression usable in both paint and filter. State
  is read on the renderer's background mesh-build threads via a
  `ConcurrentDictionary`-backed store, and only the layers actually
  referencing feature-state are rebuilt — using cached tile data, no network
  refetch.
- **`interpolate-hcl` / `interpolate-lab`** color interpolation. Color
  gradients now run in CIELAB / CIELCH (D65) so transitions don't dip
  through grey. `interpolate-hcl` uses shortest-arc hue interpolation.
- **Per-feature pattern** for `fill-pattern` / `line-pattern` /
  `fill-extrusion-pattern`. Features within one layer are bucketed by their
  resolved pattern name and rendered as separate meshes, so
  `["match", ["get", "class"], "park", "trees", "water", "waves", "default"]`
  now produces the expected visual.
- **Glyph URL / SDF text rendering**. When a style declares `glyphs`, the
  library fetches glyph PBFs on demand, packs them into a runtime SDF
  atlas, and renders labels via the new `MapLibre/SdfText` URP shader —
  no `TMP_FontAsset` configuration needed. SymbolRenderer keeps the
  TextMeshPro path as a fall-back when no glyph URL is configured.
- **SDF line placement** (`symbol-placement: "line"`) for the glyph-URL
  path. Curved labels along roads / rivers now bake one quad per glyph
  through the SDF atlas via the new `SdfLineTextMeshBuilder`, dropping
  the TMP `ForceMeshUpdate` cost and keeping styling consistent with
  point-placement SDF labels. Falls back to the existing TextMeshPro
  warp pipeline when no glyph URL is configured.
- **DocFX documentation scaffold** (`Documentation~/docfx.json`,
  `index.md`, `toc.yml`, `filterConfig.yml`). Running `docfx
  Documentation~/docfx.json --serve` after a `dotnet tool install -g
  docfx` produces an API-reference site at `localhost:8080` from the
  Runtime / Editor XML doc-comments. Build artefacts (`_site/`, `api/`)
  are gitignored apart from a seed `api/index.md`.
- **`FullscreenControl`** + improved `GeolocateControl` (state colours for
  idle / waiting / active / error, plus optional `trackUserLocation`
  continuous recenter).
- **`PrerenderCustomLayers`** is invoked once per frame for every visible
  custom layer before the main `Render` pass — same two-phase contract as
  MapLibre GL JS `prerender` / `render`.
- **PlayMode smoke test suite** that loads every sample scene from
  EditorBuildSettings and asserts no exceptions or asserts during boot.
  Adds a new `MapLibre.Unity.Tests.PlayMode` assembly.
- **Sample scenes**: `FeatureStateHoverDemo`, `CustomLayerDemo`,
  `CustomSourceDemo`, `GlyphUrlDemo`. All registered in HomeScene.
- **Sample scenes (additional coverage)**: `CameraAnimationDemo`
  (EaseTo / FlyTo / ZoomTo / RotateTo / PanBy / JumpTo side-by-side),
  `SkyAndLightDemo` (time-of-day slider driving `SetSky` / `SetLight`),
  `FreeCameraDemo` (orbit rig driving `SetFreeCameraOptions`), and
  `PMTilesDemo` (HTTP Range PMTiles archive streamed at runtime). All
  four are registered in HomeScene and EditorBuildSettings.

### Changed
- `SetFeatureState` skips the global `RefreshTiles` when no layer reads
  feature-state from the mutated source. When at least one layer is
  affected, only that layer is rebuilt — and from cached tile data.
- `feature-state` storage is now a 3-level `ConcurrentDictionary` so the
  background mesh-build threads can read state safely while the main
  thread mutates it.
- Filter expressions now resolve `feature-state` correctly for every
  built-in renderer (`Vector` / `Circle` / `Heatmap` / `FillExtrusion` /
  `Symbol`). Previously the `EvaluationContext` for filter passes was
  built without the source identity, so `["==", ["feature-state", "x"],
  true]` always returned false.

## Notable additions before this changelog began

These were committed before CHANGELOG.md existed but are worth surfacing
because they shape the public API:

- `setSky` / `getSky` — sky gradient at high pitch.
- `setLight` / `getLight` — directional light for fill-extrusion shading.
- `SetTerrain` runtime API — toggle / swap DEM source at runtime.
- `GetFreeCameraOptions` / `SetFreeCameraOptions` — drive the camera from
  Cinemachine or another external rig.
- `ScaleControl` — bottom-left scale bar (m / km / ft / mi).
- `BoxZoom` (Shift + left drag) with a visual selection rectangle.
- HTTP disk cache (`Cache-Control` / `ETag` aware).
- GeoJSON clustering (`cluster` / `clusterRadius` / `clusterMaxZoom` /
  `clusterMinPoints`) and cluster query helpers
  (`GetClusterExpansionZoom` / `GetClusterChildren` / `GetClusterLeaves`).
- Raster parent-tile fallback while child tiles are loading
  (`raster-fade-duration` driven cross-fade).
- Symbol collision improvements: `text-padding` / `symbol-sort-key` /
  `text-allow-overlap` / `text-ignore-placement` /
  `icon-rotation-alignment` / `text-rotation-alignment` /
  `*-pitch-alignment` / `text-keep-upright`.
- HTTP exponential-backoff retry for tile / sprite / glyph fetches.
- Pattern fills: `fill-pattern` / `line-pattern` / `fill-extrusion-pattern`
  (initially uniform per layer; now per-feature, see Unreleased above).
- `text-font` font-stack fallback for multi-language labels.
- `loadImage` async URL helper, `addImage` / `updateImage` / `removeImage`
  runtime image API.
- `SetSprite` / `SetGlyphs` runtime style hot-swap.
- Camera convenience: `PanBy` / `ZoomIn` / `ZoomOut` / `RotateTo` / `Stop`
  / `SetFilter` / `Set*Zoom` / `MaxBounds` / `HasLayer`, etc.
- `fill-translate` / `line-translate` / `circle-translate`.

## Versioning

The project has not yet cut a tagged release — this changelog tracks the
`main` branch. Once 1.0 is published, every entry under `[Unreleased]`
will be moved into the appropriate dated section.
