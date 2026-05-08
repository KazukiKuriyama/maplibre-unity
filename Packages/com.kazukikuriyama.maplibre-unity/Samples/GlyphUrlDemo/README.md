# Glyph URL Demo

Demonstrates the SDF text path. When the style declares a `glyphs` URL, MapLibre Unity fetches glyph PBFs on demand, packs them into an internal SDF atlas, and renders labels via a custom Unity shader (`MapLibre/SdfText`) — no `TMP_FontAsset` assignment needed.

## Behaviour

Five city labels (Tokyo / New York / Paris / Sydney / Sao Paulo) are pinned to their longitude / latitude. Pan and zoom — labels stay anchored. The first time a label is shown, the relevant 256-codepoint range is fetched from `demotiles.maplibre.org` and packed into the runtime atlas.

## What this proves

- `MapLibreMap.SetGlyphs(url)` switches the symbol renderer to the SDF mesh path automatically.
- Labels respect `text-color`, `text-halo-color`, `text-halo-width` exactly as written in the style.
- No TextMeshPro font asset is referenced — the demo works on a fresh project install with TMP Essential Resources missing.
- `text-font` chooses the primary font stack; the demo uses `Noto Sans Regular`, which the demotiles glyph endpoint actually serves (their `Open Sans Regular` stack returns 404).

## Files

- `GlyphUrlDemo.cs` — script that wires `SetGlyphs`, adds a GeoJSON source, and registers the symbol layer.
- `GlyphUrlDemoScene.unity` — minimal scene with `MapLibreMap` + `MapControlsOverlay` + this demo script.

## Required setup

Open the scene, press Play. The first frame fetches the relevant glyph ranges over HTTP — the labels appear once the data arrives (typically < 200ms on a fast connection).

## Network endpoints used

| Resource | URL |
|---|---|
| Glyphs | `https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf` |

`demotiles.maplibre.org` is the official demo tile server maintained by the MapLibre community. Production apps should host their own glyph endpoint or use a commercial tile provider.

## Falling back to TextMeshPro

To revert to the TMP path (e.g. for offline builds without glyph endpoints), call `_map.SetGlyphs(null)`. SymbolRenderer immediately switches new labels back to the TextMeshPro pipeline.
