# Custom Layer Demo

Demonstrates `ICustomLayer` — the hook that lets user-supplied code draw into the map's render order, mirroring MapLibre GL JS `type: "custom"` layers.

## Behaviour

Three rotating cubes are pinned to Tokyo landmarks (Tokyo Tower, Skytree, Tokyo Station). Pan and zoom the camera — the cubes stay anchored to their longitude/latitude. The cubes spin via `Render()`, which the map calls every frame while the layer is visible at the current zoom.

## What this proves

- The map owns the slot in render order. Set `_insertBeforeLayerId` in the Inspector to push the cubes below labels or other layers, exactly like `addLayer(layer, beforeId)` in GL JS.
- `OnAdd` / `OnRemove` lifecycle is honoured. The cubes are destroyed when the layer (or the entire map) is removed.
- `LngLatToWorld` translates geographic coordinates into Unity world space — the standard pattern for any custom layer that needs to anchor objects to the map.

## Files

- `CustomLayerDemo.cs` — the demo MonoBehaviour and an inner `TokyoLandmarksLayer : ICustomLayer` implementation.
- `CustomLayerDemoScene.unity` — minimal scene with `MapLibreMap` + `MapControlsOverlay` + this demo script.

## Required setup

Open the scene, press Play. Pan around Tokyo to see the cubes track their geographic positions.

## Where this is useful in a real app

- Pinning 3D models, particle systems, or VFX to LngLat positions.
- Drawing custom geometry that the built-in layers can't express (e.g. animated geometry, CPU-generated meshes).
- Inserting third-party Unity rendering (Cinemachine framing, post-processing effects scoped to a layer slot) into the map's render order.
