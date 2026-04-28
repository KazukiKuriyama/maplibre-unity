# Background Pattern Demo

Renders the map with `background-pattern` instead of a solid
`background-color`. The demo:

1. Generates a 32×32 procedural checkerboard sprite at runtime via
   `AddImage("grid-pattern", ...)`.
2. Calls `SetPaintProperty("background", "background-pattern", "grid-pattern")`,
   which swaps the BackgroundRenderer over to `MapLibre/BackgroundPattern`
   and tiles the sprite seamlessly across the visible plane.
3. Adds a hexagonal fill layer on top so you can confirm the pattern sits
   behind regular layers in render order.

The base style has no vector source — the entire ground is the pattern.
Pan / zoom; the pattern stays at a constant CSS-pixel size regardless
of the camera distance because the shader uses `_CSSToWorld` derived
from the per-frame frustum.
