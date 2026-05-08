# Per-Feature Pattern Demo

Demonstrates per-feature `fill-pattern`. A single fill layer paints three Tokyo polygons with three different sprites by reading the feature's `class` property — what was previously possible only by splitting into multiple layers.

## Behaviour

Three polygons render with three distinct procedural patterns:

| `class` value | Sprite | Visual |
|---|---|---|
| `park` | `trees` | Green dots |
| `water` | `waves` | Blue horizontal stripes |
| `industrial` | `grid` | Brown crosshatch |

The patterns are generated procedurally at runtime via `MapLibreMap.AddImage`, so the demo runs without any external sprite endpoint.

## What this proves

- A single `fill-pattern` expression with `["match", ...]` resolves to a different sprite per feature.
- Internally the renderer buckets features by their resolved pattern name and emits one mesh per group, sharing the layer's render order slot.

## Files

- `PerFeaturePatternDemo.cs` — script that registers three procedural sprites, then adds a single fill layer with a `match` expression on `class`.
- `PerFeaturePatternDemoScene.unity` — scene with `MapLibreMap` + `MapControlsOverlay` + this demo script.

## Required setup

Open the scene, press Play. Camera the view towards Tokyo to see the three zones in the same fill layer wearing different patterns.
