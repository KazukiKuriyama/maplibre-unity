# Color Interpolation Demo

Side-by-side comparison of `interpolate`, `interpolate-hcl`, and `interpolate-lab` for color values. Three Tokyo polygons render with the same red→blue zoom ramp but in different color spaces.

## Behaviour

| Polygon | Operator | Midpoint Look |
|---|---|---|
| Left | `interpolate` (RGB) | Drifts through a desaturated grey-purple at midzoom |
| Centre | `interpolate-hcl` | Stays vivid, sweeping the hue circle through magenta |
| Right | `interpolate-lab` | Stays bright with a different perceptual path |

Zoom from 8 → 12 to watch the midpoints diverge.

## What this proves

- The renderer wires the operator name through to the color-interpolation space; HCL and Lab are not silent fall-throughs to RGB lerp.
- HCL midpoints are perceptually more saturated than RGB midpoints — useful for zoom-driven heatmap-style ramps where grey midpoints would "deaden" the gradient.

## Files

- `ColorInterpolationDemo.cs` — script that adds three rectangles and three fill layers, one per color space.
- `ColorInterpolationDemoScene.unity` — scene with `MapLibreMap` + `MapControlsOverlay` + this demo script.

## Required setup

Open the scene, press Play. Use scroll-wheel to zoom and watch the midzoom colours.
