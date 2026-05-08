# Vector Tiles with Labels Sample

A sample demonstrating vector tile rendering with labels using MapLibre Demo Tiles.

## Feature demo
- **Fill layers**: water, parks, buildings (minzoom: 14, zoom-dependent opacity)
- **Line layers**: roads (zoom-dependent line-width), railways, country borders
- **Symbol layers**: city / town / village names, road names, POI labels
- **Filter**: `["==", "class", "city"]`, `["in", "class", ...]`, etc.
- **Zoom functions**: line-width and text-size interpolation via exponential stops
- **minzoom/maxzoom**: per-layer visibility control by zoom level

## Setup

1. Create an empty GameObject in a Unity scene
2. Add a `MapLibreMap` component
3. Set **Style File** to `VectorLabelsStyle.json` (as a TextAsset)
4. Press Play

## Style layout

| Layer | Type | Source layer | Zoom range |
|---|---|---|---|
| water | fill | water | all zooms |
| landuse-park | fill | landuse | all zooms |
| building | fill | building | 14+ |
| road-minor | line | transportation | 13+ |
| road-major | line | transportation | all zooms |
| road-motorway | line | transportation | all zooms |
| railway | line | transportation | all zooms |
| boundary-country | line | boundary | all zooms |
| label-place-city | symbol | place | 4-14 |
| label-place-town | symbol | place | 8-16 |
| label-place-village | symbol | place | 12+ |
| label-road | symbol | transportation_name | 14+ |
| label-poi | symbol | poi | 15+ |

## Data source

[MapLibre Demo Tiles](https://demotiles.maplibre.org/) (OpenMapTiles schema)
