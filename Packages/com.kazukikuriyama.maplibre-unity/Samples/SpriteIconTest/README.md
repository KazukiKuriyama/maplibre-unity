# Sprite Icon Test

Sample for verifying sprite icon support.

## What this validates

| Item | What is checked |
|------|----------|
| Sprite atlas loading | Fetches PNG + JSON from a `sprite` URL |
| icon-image (Expression) | `["match", ["get", "CONTINENT"], ...]` switches icons per continent |
| icon-image (literal) | `"airport_11"` applies the same icon to every feature |
| icon-size (zoom stops) | Icon size interpolated per zoom level |
| icon-opacity | Semi-transparent icon rendering |
| Icon + text together | Country labels and icons drawn at the same time |
| Icon-only | Icon shown without any accompanying text |
| Collision | Overlapping icons are culled |

## Setup

1. Create an empty GameObject in a Unity scene
2. Add a `MapLibreMap` component
3. Set **Style File** to `SpriteIconTestStyle.json` (as a TextAsset)
4. Press Play

## Style layout

- **sprite**: `https://demotiles.maplibre.org/styles/osm-bright-gl-style/sprite`
  - 100+ icons (airport, restaurant, park, hospital, etc.)
- **Source**: MapLibre Demo Tiles (centroids layer)
- **country-icons**: per-continent icon + country name text
  - Asia -> star, Europe -> castle, Africa -> mountain, etc.
- **icons-only-layer**: airport-only icon for European/Asian countries (opacity: 0.6)

## Expected output

- zoom 2: a small icon plus country name at each country's location
- zoom 3+: icons grow gradually; semi-transparent airport icons appear over Europe/Asia
- In dense regions, overlapping icons are automatically thinned out
