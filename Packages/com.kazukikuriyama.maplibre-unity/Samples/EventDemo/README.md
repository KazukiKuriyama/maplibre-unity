# Event Demo

Demonstrates the event system that mirrors MapLibre GL JS's `map.on()` / `map.off()` / `map.once()` API.

## Setup

1. Duplicate an existing sample scene (e.g. BasicRasterMapScene)
2. Add a `MapEventDemo` component to the GameObject that has `MapLibreMap`
3. Press Play and watch the Console

## Events covered

| Event | When it fires |
|---------|---------------|
| `Load` | When the map finishes initializing |
| `Idle` | After movement settles |
| `MoveStart` / `Move` / `MoveEnd` | Any state change (pan/zoom/rotate) |
| `ZoomStart` / `Zoom` / `ZoomEnd` | Zoom changes |
| `RotateStart` / `Rotate` / `RotateEnd` | Bearing changes |
| `PitchStart` / `Pitch` / `PitchEnd` | Pitch changes |
| `Click` | Left click (with geographic coordinates) |
| `SourceAdd` / `SourceRemove` | When a source is added or removed |
| `LayerAdd` / `LayerRemove` | When a layer is added or removed |

## Controls

- **Left drag**: pan -> `movestart` / `move` / `moveend`
- **Scroll**: zoom -> `zoomstart` / `zoom` / `zoomend`
- **Right drag**: rotate -> `rotatestart` / `rotate` / `rotateend`
- **Middle drag**: pitch -> `pitchstart` / `pitch` / `pitchend`
- **Left click**: -> `click` (longitude/latitude printed to the Console)
