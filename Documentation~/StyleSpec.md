# Style Spec Coverage

MapLibre Unity parses upstream `style.json` files following the
[MapLibre Style Spec v8](https://maplibre.org/maplibre-style-spec/).
This page summarizes which parts of the spec are implemented, where
the Unity port deviates, and how to load and update styles at runtime.

If you are new to the spec itself, the upstream reference is the
canonical source — this document only highlights the Unity-specific
behavior that is not obvious from reading it.

## Loading a Style

Styles can be supplied in three ways:

1. **`Style Url` Inspector field** — fetched once at startup. Accepts
   `https://...`, `file://...`, and `StreamingAssets/...` paths.
2. **`Style Json` Inspector field** — paste the JSON document directly.
   Useful for quick prototyping.
3. **Runtime API** — `map.SetStyle(url)` / `map.SetStyleJson(json)`.
   Both have `Async` siblings returning `Awaitable`:

   ```csharp
   await map.SetStyleAsync("https://demotiles.maplibre.org/style.json");
   ```

When `setStyle` runs, the existing layer / source state is torn down
and rebuilt. Cached tile bytes for unchanged sources are reused —
only newly-introduced sources hit the network.

## Initial View (`center` / `zoom` / `bearing` / `pitch`)

The four top-level positional properties from the Style Spec are
honored on first load. They can also be overridden from the
`MapLibreMap` Inspector:

```
Inspector field set (non-NaN) → Inspector value wins
Inspector field NaN           → style JSON value
style JSON missing the key    → 0
```

This matches the MapLibre GL JS convention where
`new Map({ zoom: 10, style: ... })` overrides the style's `zoom`.
Inspector defaults are `NaN`, so a fresh `MapLibreMap` component
defers to the style. Use the Inspector's **Override** / **Use style**
buttons next to each field to toggle between the two modes.

## Sources

| Type | Status | Notes |
|---|:---:|---|
| `raster` | ✅ | XYZ template URLs and TileJSON |
| `raster-dem` | ✅ | Mapbox / TerrarRGB encodings |
| `vector` | ✅ | PBF tiles, both `tiles` array and `url` (TileJSON) |
| `geojson` | ✅ | Inline `data` and external URL |
| `image` | ✅ | Static raster overlay with corner coordinates |
| `video` | ❌ | Not implemented |
| Custom (`ICustomRasterSource`) | ✅ | Pluggable runtime hook |
| Custom (`ICustomVectorSource`) | ✅ | Pluggable runtime hook |
| PMTiles | ✅ | Pure-C# reader; HTTP Range and local files |
| MBTiles | ✅ | Requires user-supplied `IMBTilesBackend` (SQLite) |

GeoJSON clustering options (`cluster`, `clusterRadius`, `clusterMaxZoom`)
are honored on `geojson` sources.

## Layers

| Type | Status | Notes |
|---|:---:|---|
| `background` | ✅ | Solid color, pattern |
| `fill` | ✅ | `fill-color`, `fill-pattern`, `fill-outline-color`, `fill-translate` |
| `line` | ✅ | Dash arrays, line patterns, `line-gradient` |
| `symbol` | ✅ | Text + icon, point and line placement, SDF text |
| `raster` | ✅ | Opacity, hue rotate, brightness, saturation, contrast, fade duration |
| `circle` | ✅ | Pitch alignment, stroke, translate |
| `fill-extrusion` | ✅ | Per-feature heights, ambient occlusion |
| `heatmap` | ✅ | Weight expressions, kernel + colormap |
| `hillshade` | ✅ | Illumination direction, accent color, shadow / highlight color |
| `sky` | ✅ | Atmospheric gradient, sun position |
| `custom` (`ICustomLayer`) | ✅ | User-rendered geometry slotted in render order |

`minzoom` / `maxzoom` are respected on every layer.

## Paint and Layout Properties

Most paint and layout properties from the spec are supported. The
[Feature Comparison table in the README](../README.md#feature-comparison)
is the running source of truth — please check there for the exact
list when planning a feature.

Properties accept either a constant value or a full
[expression](Expressions.md). Zoom-dependent stops (legacy
`{ stops: [[zoom, value]] }` form) are converted to expressions
internally, so both styles work.

```csharp
// Constant
"fill-color": "#42a5f5"

// Expression
"fill-color": ["interpolate", ["linear"], ["zoom"],
    8, "#a0c4e8",
    14, "#42a5f5"]
```

## Sprites

Set `"sprite": "https://example.com/sprite"` in the style document.
The library fetches `sprite.json` and `sprite.png` (and the `@2x`
variants when available) and exposes them through:

- `fill-pattern` / `line-pattern` — referenced by sprite name.
- `icon-image` on symbol layers.
- `map.AddImage(name, texture)` to register textures at runtime
  without going through a sprite atlas.

## Glyphs

When the style declares a `glyphs` URL:

```json
{
  "glyphs": "https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf"
}
```

MapLibre Unity fetches glyph PBFs on demand, packs them into a runtime
SDF atlas, and renders text labels through a custom URP shader. No
`TMP_FontAsset` configuration is required.

When the style has no `glyphs` URL, `SymbolRenderer` falls back to
TextMeshPro automatically. Existing TMP-based projects do not need
migration.

See the **GlyphUrlDemo** sample (import via Package Manager) for a working example.

## Terrain, Sky, Light

These top-level style sections are supported:

```json
{
  "terrain": { "source": "dem", "exaggeration": 1.0 },
  "sky":     { "sky-color": "#a0c4e8" },
  "light":   { "anchor": "viewport", "intensity": 0.5 }
}
```

Equivalent runtime APIs exist as `map.SetTerrain(...)`,
`map.SetSky(...)`, `map.SetLight(...)`.

## Runtime Style Mutation

The API matches MapLibre GL JS:

```csharp
map.AddSource(id, sourceDef);
map.RemoveSource(id);

map.AddLayer(layerDef, beforeId: "labels");
map.RemoveLayer(id);
map.MoveLayer(id, beforeId);

map.SetPaintProperty("roads", "line-color", "#ff0000");
map.SetLayoutProperty("roads", "visibility", "none");
```

Mutations rebuild only the affected layers, reusing cached tile bytes
where possible. No network refetch occurs unless a new source is
introduced or an existing source's `tiles` URL changes.

## Known Deviations from the Spec

- **Globe projection** is not implemented. The map renders in Web
  Mercator only.
- **Right-to-left / complex text shaping** is not implemented. Latin,
  CJK, and most LTR scripts render correctly; bidirectional resolution
  for scripts like Arabic and Hebrew is on the roadmap.
- **Video sources** are not implemented.
- **Pattern animations** (animated `*-pattern`) follow GL JS semantics
  but performance has not been profiled at scale.

If you hit a behavior that does not match the upstream spec or
MapLibre GL JS, please file an issue with a minimal repro `style.json`
and a description of the expected outcome.

## See Also

- [Expressions guide](Expressions.md) — full expression / filter syntax.
- [Camera animation guide](CameraAnimation.md) — `easeTo`, `flyTo`,
  `fitBounds`, awaitable variants.
- [README → Feature Comparison](../README.md#feature-comparison) — the
  authoritative status matrix vs. MapLibre Native and GL JS.
