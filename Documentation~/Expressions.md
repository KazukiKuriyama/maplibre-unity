# Expressions, Filters, and Feature State

MapLibre Unity ships a full implementation of the
[MapLibre Style Spec expression language][spec]. Expressions describe
how a paint or layout property evaluates given a feature, the current
zoom level, and runtime feature state. They are the same syntax used
by MapLibre GL JS, so you can copy expressions straight from upstream
styles.

[spec]: https://maplibre.org/maplibre-style-spec/expressions/

## Quick Tour

A constant property:

```json
{ "fill-color": "#42a5f5" }
```

A zoom-dependent interpolation:

```json
{
  "fill-color": [
    "interpolate", ["linear"], ["zoom"],
    8,  "#a0c4e8",
    14, "#42a5f5"
  ]
}
```

A property-driven match:

```json
{
  "line-color": [
    "match", ["get", "class"],
    "motorway", "#e53935",
    "trunk",    "#fb8c00",
    "primary",  "#fdd835",
    "#9e9e9e"
  ]
}
```

All three are evaluated by the same expression engine. The renderer
re-runs evaluation as zoom changes or feature state updates, and it
rebuilds only the layers whose expressions actually depend on the
changed input.

## Supported Operators

The full list lives in
`Packages/com.kazukikuriyama.maplibre-unity/Runtime/Expression/ExpressionParser.cs`. A
condensed grouping:

| Group | Operators |
|---|---|
| Logical | `all`, `any`, `none`, `!`, `==`, `!=`, `<`, `<=`, `>`, `>=`, `in`, `!in`, `has`, `!has` |
| Decision | `case`, `match`, `coalesce` |
| Interpolation | `interpolate`, `interpolate-hcl`, `interpolate-lab`, `step` |
| Lookup | `get`, `feature-state`, `geometry-type`, `id`, `properties` |
| Math | `+`, `-`, `*`, `/`, `%`, `min`, `max`, `abs`, `floor`, `ceil`, `round`, `sqrt`, `pow`, `ln`, `log10`, `log2`, `sin`, `cos`, `tan`, `asin`, `acos`, `atan`, `pi`, `e` |
| String | `concat`, `downcase`, `upcase`, `to-string`, `to-number` |
| Array | `length`, `at`, `slice` |
| Conversion | `to-boolean`, `to-color`, `rgb`, `rgba`, `hsl`, `hsla` |
| Special | `zoom`, `heatmap-density`, `line-progress`, `literal`, `image` |

Both color-space-aware interpolations (`interpolate-hcl`,
`interpolate-lab`) are first-class. They produce smoother gradients
than naive RGB interpolation — see the `ColorInterpolationDemo`
sample for a side-by-side comparison.

## Filter Expressions

Layers accept `"filter"` to control which features render. Both the
legacy and expression forms are supported:

```json
{
  "filter": ["==", "class", "motorway"]
}
```

```json
{
  "filter": [
    "all",
    ["==", ["geometry-type"], "LineString"],
    [">=", ["get", "min_zoom"], 8]
  ]
}
```

The legacy form (`["==", "prop", value]`) is rewritten internally to
the expression form, so they behave identically.

## Feature State

Feature state lets you drive paint and filter expressions from runtime
data — typical use cases include hover highlights, selection, and
animated value changes.

```csharp
// Mark a feature as hovered
var sel = new MapLibreMap.FeatureSelector(
    source: "buildings",
    sourceLayer: "building",
    id: 12345);
map.SetFeatureState(sel, "hover", true);

// Read it back
var state = map.GetFeatureState(sel);

// Clear a single key, or pass null to clear the whole feature
map.RemoveFeatureState(sel, "hover");
map.RemoveFeatureState(sel);
```

Reference state from a paint expression:

```json
{
  "fill-color": [
    "case",
    ["boolean", ["feature-state", "selected"], false], "#e53935",
    ["boolean", ["feature-state", "hover"],    false], "#ffb300",
    "#42a5f5"
  ]
}
```

### Requirements

- The feature must have a stable, non-zero `id`. Vector tile features
  carry an id natively; for GeoJSON features the `id` field must live
  at the feature level (not inside `properties`).
- For sources with a `source-layer` (vector tiles), pass the layer
  name to the `FeatureSelector` constructor. For sources without one
  (GeoJSON, image, raster), use the two-argument constructor.
- Feature-state writes are thread-safe; the underlying store is a
  `ConcurrentDictionary` so background mesh builds can read state
  while the main thread mutates it.

### What rebuilds

When you call `SetFeatureState`, the renderer rebuilds only:

- Layers whose paint or filter expressions reference
  `["feature-state", ...]`.
- Tiles that contain the affected feature id.

Cached tile bytes are reused — no network refetch is triggered.

See the **FeatureStateHoverDemo** sample (import via Package Manager) for a
working interactive sample.

## Custom Image Lookups

`["image", "marker-15"]` resolves to a sprite or runtime-registered
image. Register textures at runtime via:

```csharp
map.AddImage("my-icon", texture2D);
```

After registration, expressions can route to your image by name:

```json
{
  "icon-image": [
    "case",
    ["==", ["get", "type"], "vip"], "my-icon",
    "marker-15"
  ]
}
```

## Performance Notes

- **Expression evaluation runs on a background thread** as part of
  vector tile mesh building. Keep custom expressions side-effect free.
- **Avoid dynamically generating expressions in `Update()`** — pre-build
  the JSON or `Expression` object once and reuse it. The parser is
  cheap, but evaluation per-feature is what scales with tile content.
- **Filter expressions short-circuit.** Put the cheapest predicates
  first inside `all` / `any` to skip work on the majority of features.

## Debugging

The parser throws `ArgumentException` with the operator name when an
expression is malformed. The error surfaces as a `MapEventType.Error`
event — listen for it to surface bad styles to the user:

```csharp
map.On(MapEventType.Error, e => Debug.LogError(e.ErrorMessage));
```

## See Also

- [Style Spec coverage](StyleSpec.md) — which sources, layers, and
  paint properties are supported.
- [MapLibre upstream expression reference][spec] — canonical syntax.
- `Packages/com.kazukikuriyama.maplibre-unity/Tests/EditMode/Expression/` — comprehensive
  unit tests; useful as worked examples of how each operator behaves.
