# Style Switch Demo

Demonstrates runtime layer-visibility switching across multiple raster
basemaps. Three raster sources (OSM Standard, CartoDB Voyager, OpenTopoMap)
are declared in a single style JSON; the demo's three buttons flip
`layout.visibility` on the corresponding layers via `SetLayoutProperty()`.

## Why visibility instead of `setStyle()`

`setStyle()` tears down all sources, renderers and tile pools before
reinitialising — that path is synchronous when the style is inline raster
JSON (no awaits actually pause), which can stall the main thread long
enough to trigger macOS' unresponsive-app watchdog. Visibility flipping
reuses the existing renderer state, so the cost is roughly one
`RefreshTiles()` per frame.

## Tile sources & attribution

All three providers are free to use for demo / educational / non-commercial
purposes with attribution. Their attribution strings are baked into the
inline style JSON in the scene and surface automatically in the
`MapControlsOverlay` attribution control once the source is loaded.

| Source | License | Attribution shown |
|---|---|---|
| OSM Standard | OSM Foundation Tile Usage Policy / ODbL data | "Map data © OpenStreetMap contributors (ODbL)" |
| CartoDB Voyager | CC BY 3.0 (basemap design) + ODbL data | "Map tiles © CARTO (CC BY 3.0) \| Data © OpenStreetMap contributors (ODbL)" |
| OpenTopoMap | CC-BY-SA 3.0 (basemap design) + ODbL data + SRTM | "Map style © OpenTopoMap (CC-BY-SA) \| Data © OpenStreetMap contributors, SRTM" |

If you fork this demo for shipping, please:

- Confirm your use case fits each provider's terms (links below).
- For heavy / commercial traffic, switch to a provider you have a
  commercial agreement with — the OSM Foundation tile servers in
  particular are not intended for production traffic.

References:

- [OSM Tile Usage Policy](https://operations.osmfoundation.org/policies/tiles/)
- [CARTO Attribution Requirements](https://carto.com/attributions)
- [OpenTopoMap About / Terms](https://opentopomap.org/about)

## Files

- `StyleSwitchDemo.cs` — MonoBehaviour that listens for button / key input
  and toggles `layout.visibility` on the three layers.
- `StyleSwitchDemoScene.unity` — minimal scene with `MapLibreMap` plus the
  three-source style as inline `_styleJson`.

## Required setup

Open the scene, press Play. Click the bottom-left buttons or press 1/2/3
to flip basemaps.
