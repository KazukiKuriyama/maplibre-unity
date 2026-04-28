# MapLibre Unity Samples

In the package source these demos live under
`Packages/com.kazukikuriyama.maplibre-unity/Samples~/<DemoName>/`. The trailing
`~` hides the folder from Unity's asset import — to use the samples
in the Editor, import them via **Window → Package Manager → MapLibre
Unity → Samples → All Samples → Import**. They are then copied to
`Assets/Samples/MapLibre Unity/<version>/All Samples/<DemoName>/`
where Unity treats them as regular project assets. The launcher scene
`Home/HomeScene.unity` (inside the imported folder) lists every demo
and loads any of them at runtime — start there if you are exploring.

The PlayMode test suite (`Window → General → Test Runner`) loads every
sample in sequence and asserts no exceptions during boot, so this list
doubles as a smoke-test inventory.

## Getting Started

| Sample | What it shows |
|---|---|
| `BasicRasterMap` | Minimal end-to-end raster setup. **Start here.** |
| `Home` | Sample launcher used by the PlayMode test suite. |

## Vector Tiles & Symbols

| Sample | What it shows |
|---|---|
| `VectorDemo` | Plain vector tile rendering with fill / line layers. |
| `VectorWithLabels` | Vector + symbol layers (text + icon). |
| `GlyphUrlDemo` | SDF text via a `glyphs` URL — no `TMP_FontAsset` required. |
| `LineLabelsDemo` | Curved labels along roads using SDF line placement. |
| `SymbolTranslateDemo` | `text-translate` / `icon-translate` offsets. |
| `LineGradientDemo` | `line-gradient` with `["line-progress"]`. |
| `DashLineTest` | Dashed lines via `line-dasharray`. |
| `BackgroundPatternDemo` | `background-pattern` with sprite atlas. |
| `PerFeaturePatternDemo` | `fill-pattern` driven by feature properties. |
| `SpriteIconTest` | Sprite-atlas icons on symbol layers. |
| `FontTest` | Font fallback + dynamic CJK glyph generation. |

## Style Expressions & Feature State

| Sample | What it shows |
|---|---|
| `ColorInterpolationDemo` | `interpolate-hcl` vs `interpolate-rgb` side by side. |
| `FeatureStateHoverDemo` | Hover / select highlights via `SetFeatureState`. |
| `PropertyDemo` | `setPaintProperty` / `setLayoutProperty` runtime edits. |

## Camera & Controls

| Sample | What it shows |
|---|---|
| `CameraAnimationDemo` | `EaseTo` / `FlyTo` / `ZoomTo` / `RotateTo` / `PanBy` / `JumpTo` side by side. |
| `FitBoundsDemo` | `FitBounds` camera helper with padding. |
| `FreeCameraDemo` | External camera rig (orbit / Cinemachine-style). |

## Sources & Layers

| Sample | What it shows |
|---|---|
| `CustomLayerDemo` | `ICustomLayer` injecting 3D objects pinned to LngLat. |
| `CustomSourceDemo` | Procedural noise tiles via `ICustomRasterSource`. |
| `DynamicSourceLayerDemo` | `addSource` / `addLayer` / `moveLayer` end-to-end. |
| `StyleSwitchDemo` | Hot-swap `style.json` at runtime. |
| `GeoJsonDemo` | Static GeoJSON source rendered as fill / line / circle layers. |
| `RuntimeGeoJsonDemo` | Add / remove GeoJSON sources at runtime. |
| `ImageSourceDemo` | Raster image overlay anchored by corner coordinates. |
| `PMTilesDemo` | Stream a single-file PMTiles archive over HTTP Range — works on WebGL. |

## 3D, Terrain, Effects

| Sample | What it shows |
|---|---|
| `FillExtrusionDemo` | Extruded buildings with per-feature heights. |
| `TerrainDemo` | DEM-driven terrain + hillshade lighting. |
| `HillshadeDemo` | Standalone hillshade layer. |
| `HeatmapDemo` | Density heatmap with weight expressions. |
| `SkyAndLightDemo` | `SetSky` / `SetLight` driven by a time-of-day slider. |
| `CircleDemo` | Circle layer with stroke + opacity. |
| `CirclePitchDemo` | Pitch-aligned circles for billboarded markers. |

## Events & Interaction

| Sample | What it shows |
|---|---|
| `EventDemo` | Map-level event subscription (`Move`, `Click`, `StyleData`, ...). |
| `PointerEventDemo` | Layer-scoped pointer events with `e.Features`. |
| `QueryFeaturesDemo` | `QueryRenderedFeatures` under the cursor. |
| `MarkerPopupDemo` | DOM-style markers / popups anchored to map coords. |

## Running the Samples

1. Open `Home/HomeScene.unity` and press Play. Click any tile to load
   the matching demo at runtime.
2. Or open the demo's `.unity` file directly and press Play.
3. Some demos need internet access (raster / vector tile endpoints,
   PMTiles archive). The default endpoints are public demo servers
   that may rate-limit — replace them with your own provider for
   serious testing.

## Adding a New Sample

1. Create `Packages/com.kazukikuriyama.maplibre-unity/Samples~/<YourDemo>/`.
2. Add a `.unity` scene plus any scripts under that folder. Scripts
   compile against the `MapLibre.Unity.Samples` assembly (already
   wired up via the asmdef at the Samples root).
3. Register the demo in `Home/HomeScene.unity` so the launcher picks
   it up.
4. Add an entry to this README and to the Samples table in the root
   `README.md`.
5. Run the PlayMode tests to confirm the scene boots without
   exceptions.
