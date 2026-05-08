# Line Gradient Demo

Renders a single GeoJSON LineString with a six-stop rainbow `line-gradient`
that drives off `["line-progress"]`. The source declares `lineMetrics: true`
so the renderer emits a 0..1 progress UV per vertex and bakes the gradient
expression into a 256x1 ramp texture.

The gradient interpolation:

```json
"line-gradient": [
  "interpolate", ["linear"], ["line-progress"],
  0.0, "#ff0000",
  0.2, "#ff8c00",
  0.4, "#ffd700",
  0.6, "#00c853",
  0.8, "#1976d2",
  1.0, "#9c27b0"
]
```

Pan / zoom to verify the colours stay anchored to feature progress as
the camera moves.
