# Feature State Hover Demo

Interactive demo of `SetFeatureState`. Three Tokyo polygons (Imperial Palace, Yoyogi Park, Ueno Park) drive their fill-color from a single `["case", ...]` expression that resolves against runtime feature state at draw time.

## Behaviour

- **Hover** a polygon: turns yellow (`hover` state = true).
- **Click** a polygon: toggles a sticky red highlight (`selected` state). Selected wins over hover, so a clicked polygon stays red even when the cursor leaves.
- **Click** a selected polygon again: clears the selection.

## Why this matters

`SetFeatureState` does not re-fetch tiles. The library walks the active layers, finds those whose paint/layout/filter actually reference `["feature-state", ...]`, and rebuilds only those layers' tiles from the source cache. Layers that never read feature-state are skipped entirely.

## Files

- `FeatureStateHoverDemo.cs` — wires up sources, layers, and the hover/click handlers.
- `FeatureStateHoverDemoScene.unity` — minimal scene with `MapLibreMap` + `MapControlsOverlay` + this demo script.

## Required setup

Open the scene, press Play. Move the cursor over the polygons; click to toggle selection. No external network credentials needed — the GeoJSON is inline.
