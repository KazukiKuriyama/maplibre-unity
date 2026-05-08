# Circle Layer Demo

Demonstrates the circle layer. Renders Point features from vector tiles as SDF circles.

## Setup

1. Create an empty GameObject in the scene and add a `MapLibreMap` component
2. Assign `CircleDemoStyle.json` to `Style File`
3. Press Play

## Supported properties

- `circle-radius` — radius in CSS pixels. Supports Expressions (e.g. zoom interpolate)
- `circle-color` — fill color
- `circle-opacity` — fill opacity
- `circle-blur` — blur amount (0 = sharp, 1 = fully faded)
- `circle-stroke-width` — stroke width in CSS pixels
- `circle-stroke-color` — stroke color
- `circle-stroke-opacity` — stroke opacity
