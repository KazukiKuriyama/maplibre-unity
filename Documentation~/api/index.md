# API Reference

This namespace tree is regenerated from the C# XML documentation on every
DocFX build. If a type doesn't appear here, run

```sh
docfx Documentation~/docfx.json
```

at the repo root after opening the Unity project once (so Unity has had a
chance to lay out the source tree). The generated YAML lands in
`Documentation~/api/` and is gitignored.

## Top-level namespaces

- `MapLibre.Unity` — public entry points (`MapLibreMap`, `MapLibreLayer`,
  query / event APIs).
- `MapLibre.Unity.Style` — the parsed style document and its property
  classes.
- `MapLibre.Unity.Source` — tile sources (raster, vector, GeoJSON,
  PMTiles, MBTiles, raster-DEM, custom).
- `MapLibre.Unity.Rendering` — per-layer renderers and the SDF text /
  glyph atlas pipeline.
- `MapLibre.Unity.Expressions` — the MapLibre style-spec expression
  evaluator.
- `MapLibre.Unity.Camera` — the `MapAnimator` MonoBehaviour and the
  `easeTo` / `flyTo` / `fitBounds` family.
- `MapLibre.Unity.Controls` — Navigation, Scale, Geolocate, Fullscreen,
  Attribution overlay controls.
