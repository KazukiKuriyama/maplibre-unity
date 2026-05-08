# MapLibre Unity Samples

These demos ship bundled with the package under
`Packages/com.kazukikuriyama.maplibre-unity/Samples/<DemoName>/` and
appear directly in the Project window under **Packages → MapLibre Unity
→ Samples** as soon as the package is installed — no Package Manager
"Import" step is required. The launcher scene `Home/HomeScene.unity`
lists every demo and loads any of them at runtime — start there if you
are exploring.

Samples live under `Packages/` so they are read-only. To experiment with
a demo, copy its folder into `Assets/` first and edit the copy.

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
| `PMTilesDemo` | Stream a single-file PMTiles archive over HTTP Range. |
| `PrefabSourceDemo` | Anchor 3D primitive prefabs to LngLat coordinates (Unity-only source). |
| `ParticleSourceDemo` | Anchor `ParticleSystem` effects to LngLat (smoke / sparkle / click bursts). |

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

## Tile Source Notice

The bundled style JSONs reference public demo tile servers purely for
illustration. **They are not licensed for production / shipped apps.**
Before distributing anything built on top of these samples, swap the
URLs for an endpoint you are entitled to use.

| Endpoint used in samples | Allowed use | Required action for production |
|---|---|---|
| `tile.openstreetmap.org` (`a.`/`b.`/`c.` subdomains) | Light development / personal use only. The [OSMF Tile Usage Policy](https://operations.osmfoundation.org/policies/tiles/) forbids "heavy use" and redistribution in apps. | Self-host, or use a commercial OSM tile provider (Stadia, MapTiler, Geofabrik, etc.). |
| `demotiles.maplibre.org` | MapLibre demo server, intended for demos / docs only. | Switch to your own vector tile / glyph server. |
| `tiles.openfreemap.org` | OpenFreeMap (free for any use, attribution required). | Keep `© OpenStreetMap contributors` attribution; consider self-hosting for high traffic. |

The sample style JSONs already include `attribution` strings; preserve
them (or replace them with the equivalent for your provider) when you
copy a style into your own project.

## Adding a New Sample

1. Create `Packages/com.kazukikuriyama.maplibre-unity/Samples/<YourDemo>/`.
2. Add a `.unity` scene plus any scripts under that folder. Scripts
   compile against the `MapLibre.Unity.Samples` assembly (already
   wired up via the asmdef at the Samples root).
3. Register the demo in `Home/HomeScene.unity` so the launcher picks
   it up.
4. Add an entry to this README and to the Samples table in the root
   `README.md`.
5. Run the PlayMode tests to confirm the scene boots without
   exceptions.
