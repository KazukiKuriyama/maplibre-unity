# MapLibre for Unity

A pure-C# port of [MapLibre GL JS](https://maplibre.org/) for the Unity game
engine. Renders raster and vector tiles, runs the MapLibre style spec
(expressions, filters, paint properties), and ships with the same
camera-control API surface as the upstream library — `easeTo`, `flyTo`,
`fitBounds`, `setBearing`, `queryRenderedFeatures`, controls, and more.

No native plugins — the entire pipeline runs on the managed Unity runtime.

**Verified platform:** Standalone (Windows / macOS / Linux) — the
only target the maintainer regularly builds and runs against.
**Untested:** iOS / Android (expected to work, no device verification yet).
**Not supported:** WebGL — vector tile parsing relies on `Task.Run`
and the tile/glyph caches use synchronous `System.IO.File` APIs,
neither of which works under the default WebGL single-threaded
runtime. See the README's
[Supported platforms](https://github.com/KazukiKuriyama/maplibre-unity#supported-platforms)
section for details.

## Quick links

- **[Getting Started](GettingStarted.md)** — install the package, drop a
  `MapLibreMap` component into a scene, and load a style.
- **[Style Spec coverage](StyleSpec.md)** — which sources, layers, and
  paint properties are supported, plus runtime style mutation.
- **[Expressions](Expressions.md)** — full expression / filter syntax
  and the `feature-state` API.
- **[Camera animation](CameraAnimation.md)** — `easeTo`, `flyTo`,
  `fitBounds`, and the awaitable variants.
- **[Extensibility](Extensibility.md)** — `ICustomLayer`,
  `ICustomRasterSource`, `ICustomVectorSource`, `AddImage`.
- **[Offline tile archives](OfflineArchives.md)** — PMTiles and
  MBTiles archive readers.
- **[Troubleshooting](Troubleshooting.md)** — common rendering, tile,
  expression, and build-time problems with first-step fixes.
- **[API Reference](api/)** — generated from the Runtime / Editor source
  XML doc-comments. Use the search box (top right) to jump to a type.
- **[GitHub repository](https://github.com/KazukiKuriyama/maplibre-unity)** —
  source, issues, and discussions.

## What's in the box

The library is structured around the same concepts MapLibre GL JS users
already know:

- `MapLibreMap` — the top-level component you drop in a scene. Owns the
  style, camera, sources, layers, and dispatch loop.
- `Style` — parsed from `style.json` (URL or local asset). Tiles, layers,
  expressions, sprites, glyphs, terrain, sky, and light all flow through
  here.
- **Sources** — `RasterSource`, `VectorSource`, `GeoJsonSource`,
  `RasterDemSource`, plus pluggable `ICustomRasterSource` /
  `ICustomVectorSource` interfaces for procedural tiles. PMTiles and
  MBTiles archives are first-class.
- **Renderers** — one renderer class per layer type: fill, line, symbol,
  fill-extrusion, hillshade, heatmap, raster, background, sky.
- **Expressions** — full coverage of the MapLibre style-spec expression
  language, including `interpolate-hcl` / `interpolate-lab`,
  `feature-state`, and `image`.

## Project status

Most of the MapLibre style spec is implemented. The
[Feature Comparison table](https://github.com/KazukiKuriyama/maplibre-unity#feature-comparison)
in the README is the running source of truth for what is supported
relative to MapLibre Native and MapLibre GL JS, and the
[CHANGELOG](https://github.com/KazukiKuriyama/maplibre-unity/blob/main/CHANGELOG.md)
records what has landed in each release. Remaining gaps are tracked as
GitHub issues. PRs are welcome.

## License

MIT License. Bundled glyph PBFs and font files have their own licenses —
see `THIRD_PARTY_NOTICES.md` for the exact terms.
