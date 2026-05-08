# Troubleshooting

This page collects the most common problems users hit when integrating
MapLibre Unity, organized by symptom. If your issue is not listed,
file a [GitHub issue][issues] with a minimal repro `style.json` and
the relevant Console output (especially any `MapEventType.Error`
events — see [Surfacing errors](#surfacing-errors) below).

[issues]: https://github.com/KazukiKuriyama/maplibre-unity/issues

## Rendering

### The map is black / nothing renders

- The active camera must use a URP renderer asset. The default
  built-in pipeline does not draw URP shaders. Open
  **Edit → Project Settings → Graphics → Scriptable Render Pipeline
  Settings** and confirm a `UniversalRenderPipelineAsset` is assigned.
- Check the camera's clear flags. If it clears to *Skybox* but no
  skybox material is set, the viewport stays black even when tiles
  are present.
- Confirm the `MapLibreMap` GameObject is not at a position the
  camera cannot see. The map is laid out in world space — the default
  pivot is the GameObject origin.

### Labels render as squares / tofu

Two distinct fallbacks exist for symbol layer text:

1. **Glyph URL path** — when the style declares
   `"glyphs": "https://.../{fontstack}/{range}.pbf"`, MapLibre Unity
   downloads SDF glyphs at runtime and renders them via a custom URP
   shader. No `TMP_FontAsset` is needed.
2. **TextMeshPro fallback** — when the style has no `glyphs` URL,
   `SymbolRenderer` uses TextMeshPro. The TMP Essential Resources
   must be imported (**Window → TextMeshPro → Import TMP Essential
   Resources**) and a `TMP_FontAsset` either assigned via
   **MapLibreMap → Text → Symbol Font** or auto-generated through
   **MapLibre → Font Setup**.

If labels still render as squares after the right path is wired up,
the most likely causes are:

- The font does not include the requested glyphs (typical for CJK on
  Latin-only fonts) — switch to Dynamic mode in the Font Asset
  Creator, or supply a Noto-family fallback.
- The `text-font` array references a fontstack the glyph server does
  not know about. The MapLibre demotiles server only ships
  `Open Sans Regular` / `Noto Sans Regular`.

### Custom Layer GameObjects vanish at zoom-out

`ICustomLayer` respects the spec's `minzoom` / `maxzoom`. If you
constructed the `LayerDefinition` without specifying them, leave them
`null` (which means "always visible"). Setting `MaxZoom = 14` will
hide the layer at zoom 13 and below.

## Tiles & networking

### Tiles never load

1. Listen for `MapEventType.Error` to see the actual HTTP status — the
   library does not Debug.LogError network failures by default to
   avoid log spam:

   ```csharp
   _map.On(MapEventType.Error, e => Debug.LogError(e.ErrorMessage));
   ```

2. Confirm the tile URL template uses `{z}/{x}/{y}` (not `{x}/{y}/{z}`
   or `{zoom}/{x}/{y}`) — MapLibre Style Spec is strict about the
   variable names.
3. On iOS, ATS may block plain `http://` endpoints. Either switch the
   tile server to HTTPS or add the host to
   `NSAppTransportSecurity → NSExceptionDomains` in `Info.plist`.
4. Some public tile servers rate-limit aggressively. The OSM main
   tile server (`tile.openstreetmap.org`) forbids "heavy use" — for
   anything more than light development, switch to a commercial
   provider or self-host.

### Tiles flicker or disappear during pan/zoom

The default tile cache is bounded — when the LRU evicts a tile that
is still on screen, the renderer rebuilds it on the next frame. This
manifests as a one-frame flicker. If you see it consistently:

- Increase the cache size via `MapLibreMap`'s tile cache options.
- Reduce concurrent tile counts by raising `minZoom` or limiting the
  visible bounds.

### CORS errors in WebGL

WebGL is currently unsupported (see
[Supported platforms in the README][platforms]). Vector tile parsing
relies on `Task.Run`, and the tile/glyph caches use synchronous
`System.IO.File` APIs — neither works under the default WebGL
single-threaded runtime. Builds will compile but crash at runtime.

[platforms]: https://github.com/KazukiKuriyama/maplibre-unity#supported-platforms

## Style and expressions

### `feature-state` does nothing

- The feature must have a stable, non-zero `id`. Vector tile features
  carry an id natively; for GeoJSON features the `id` field must live
  at the feature level (not inside `properties`).
- For sources with a `source-layer` (vector tiles), pass the layer
  name to the `FeatureSelector` constructor. For sources without one
  (GeoJSON, image, raster), use the two-argument constructor.
- The paint or filter expression must actually reference
  `["feature-state", "<key>"]`. Setting a feature-state value that no
  expression reads triggers no rebuild — by design.

### `fill-pattern` / `line-pattern` shows a solid color

The style must declare a `sprite` URL, and the pattern name must
match a sprite entry. Check the Console for "sprite not found"
warnings during style load.

### Expression throws `ArgumentException`

`ExpressionParser` throws with the operator name when the JSON shape
is wrong. Wrap the load in a try/catch or listen on
`MapEventType.Error` to surface bad styles. The
[Expressions guide](Expressions.md) has the full operator list.

## Camera

### `EaseTo` / `FlyTo` finishes immediately

Durations are **milliseconds**, not seconds — the upstream MapLibre
GL JS convention. `Duration = 0.6f` is interpreted as "less than one
millisecond" and the animation snaps to the target. Use `600f`
instead. See [Camera animation](CameraAnimation.md#durations-are-in-milliseconds).

### Animation cancels unexpectedly

Animations cancel on user input by design — `MapInputHandler` raises
an interaction event that `MapAnimator` listens for. If you are
running an intro cinematic, either disable input until it finishes or
swallow the cancellation:

```csharp
try { await _map.FlyToAsync(opts, ct); }
catch (OperationCanceledException) { /* user took over */ }
```

## Builds & packaging

### Standalone build cannot find the font

The OS-fallback path (Hiragino on macOS, Yu Gothic on Windows) only
works on machines where those fonts are installed. For shipping
builds:

1. Generate a `TMP_FontAsset` via **MapLibre → Font Setup**, or
2. Bundle an OFL-licensed font (e.g. Noto Sans JP) and assign it
   manually, or
3. Use a `glyphs` URL in the style so SDF glyphs are fetched at
   runtime.

See [THIRD_PARTY_NOTICES.md][notices] for recommended fonts.

[notices]: https://github.com/KazukiKuriyama/maplibre-unity/blob/main/Packages/com.kazukikuriyama.maplibre-unity/THIRD_PARTY_NOTICES.md

### iOS / Android: tiles work in Editor but not on device

> Mobile targets are **not regularly tested** by the maintainer (only
> Standalone is verified). The notes below are best-effort guidance
> based on the underlying Unity APIs the runtime depends on. PRs and
> bug reports from people running on device are very welcome.

- Confirm the `Internet Access` Player Setting is `Required` on
  Android.
- For HTTP endpoints on iOS, see ATS notes above.
- StreamingAssets paths differ across platforms. Prefer
  `Application.streamingAssetsPath` over hard-coded paths, and on
  Android remember that StreamingAssets contents live inside the APK
  and must be read via `UnityWebRequest` rather than `System.IO.File`.

## Surfacing errors

The single most useful debugging step is wiring an error listener
once at startup:

```csharp
_map.On(MapEventType.Error, e => Debug.LogError(e.ErrorMessage));
_map.On(MapEventType.StyleData, _ => Debug.Log("style ready"));
_map.On(MapEventType.SourceData, e => Debug.Log($"source: {e.SourceId}"));
```

`Error` covers tile fetch failures, style parse errors, and
expression evaluation failures. `StyleData` and `SourceData` confirm
the style and individual sources finished loading.

## See Also

- [Getting Started](GettingStarted.md) — fresh-project walkthrough.
- [Style Spec coverage](StyleSpec.md) — what is and is not supported.
- [Expressions](Expressions.md) — expression / filter syntax.
- [Camera animation](CameraAnimation.md) — easeTo / flyTo / awaitable.
