# Third-Party Notices

This file lists third-party software and assets used by or recommended for MapLibre Unity.

---

## Upstream Reference

### MapLibre GL JS

MapLibre Unity is described as a C# port of
[MapLibre GL JS](https://github.com/maplibre/maplibre-gl-js). While the
runtime is an independent Unity / C# implementation, the public API
surface, Style Spec semantics, and several algorithms (raster / vector
tile pipeline, expression evaluation, camera animations) were modelled
after MapLibre GL JS. The upstream is distributed under the
3-Clause BSD License:

```
Copyright (c) 2020, MapLibre contributors
Copyright (c) 2014-2020, Mapbox

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name "MapLibre" nor the names of its contributors may be used
   to endorse or promote products derived from this software without specific
   prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

---

## Runtime Dependencies

### Newtonsoft.Json (Json.NET)

- **Package**: `com.unity.nuget.newtonsoft-json` (3.2.1)
- **License**: MIT License
- **Copyright**: Copyright (c) 2007 James Newton-King
- **URL**: https://www.newtonsoft.com/json

### Unity Input System

- **Package**: `com.unity.inputsystem` (1.18.0)
- **License**: Unity Companion License
- **URL**: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.18/

### TextMeshPro

- **Package**: `com.unity.textmeshpro` (Unity 6 built-in)
- **License**: Unity Companion License
- **URL**: https://docs.unity3d.com/Packages/com.unity.textmeshpro@latest

### Universal Render Pipeline (URP)

- **Package**: `com.unity.render-pipelines.universal` (17.3.0)
- **License**: Unity Companion License
- **Notes**: MapLibre Unity ships URP-tagged shaders; the URP package must
  be installed in the consuming project for tile rendering.
- **URL**: https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.3/

### Unity UI (uGUI)

- **Package**: `com.unity.ugui` (2.0.0)
- **License**: Unity Companion License
- **Notes**: Used by sample overlays and the controls UI.
- **URL**: https://docs.unity3d.com/Packages/com.unity.ugui@latest

---

## Ported / Derived Source Code

### mapbox/earcut

`Packages/com.kazukikuriyama.maplibre-unity/Runtime/VectorTile/EarClipTriangulator.cs`
is a C# port of the [mapbox/earcut](https://github.com/mapbox/earcut)
JavaScript polygon triangulation library. It is redistributed under the
upstream ISC License:

```
ISC License

Copyright (c) 2016, Mapbox

Permission to use, copy, modify, and/or distribute this software for any purpose
with or without fee is hereby granted, provided that the above copyright notice
and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS
OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER
TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF
THIS SOFTWARE.
```

### mapbox/supercluster (API surface only)

`Packages/com.kazukikuriyama.maplibre-unity/Runtime/Source/SuperclusterLite.cs`
exposes a small clustering API (`GetCluster`, `GetClusterChildren`,
`GetClusterLeaves`, `GetClusterExpansionZoom`) modelled after
[mapbox/supercluster](https://github.com/mapbox/supercluster) so that
GeoJSONSource cluster queries match the MapLibre GL JS surface. The
implementation is independent — an O(N²) per-zoom merge rather than
the upstream KD-tree — and no upstream source is incorporated. The
API attribution is provided here for nominative reference only.

### mapbox/geojson-vt (clean-room reference)

`Packages/com.kazukikuriyama.maplibre-unity/Runtime/Source/GeoJsonToVectorTile.cs`
converts GeoJSON features into MVT-compatible tile data following the
shape of [mapbox/geojson-vt](https://github.com/mapbox/geojson-vt). The
projection / clipping logic is written from scratch against the public
GeoJSON (RFC 7946) and MVT specifications; no upstream source is
incorporated.

---

## Public Specifications Implemented

The following file formats and encodings are implemented from public
specifications. Each is a clean-room implementation; no upstream source
code is incorporated, and these notices are provided solely for
attribution / reproducibility.

### Mapbox Vector Tile (MVT) Specification

`Packages/com.kazukikuriyama.maplibre-unity/Runtime/VectorTile/`
(`PbfReader.cs`, `VectorTileParser.cs`, `GeometryDecoder.cs`,
`VectorTileData.cs`) decodes vector tiles per the
[Mapbox Vector Tile Specification](https://github.com/mapbox/vector-tile-spec).
The specification document is © Mapbox and is published under
[CC-BY 3.0 US](https://creativecommons.org/licenses/by/3.0/us/). MVT is
also adopted as an OGC standard.

### Mapbox Glyph PBF Format

`Packages/com.kazukikuriyama.maplibre-unity/Runtime/Source/GlyphPbfParser.cs`
implements the wire format defined by
[mapbox/glyph-pbf-composite](https://github.com/mapbox/glyph-pbf-composite)
(`proto/glyphs.proto`, BSD-3-Clause). Only the wire-compatible reader is
written from the schema; the upstream `.proto` file itself is not
redistributed.

### Mapbox Terrain-RGB DEM Encoding

`Shaders/MapLibreHillshade.shader` and `Runtime/Terrain/TerrainManager.cs`
decode elevation tiles using the publicly documented
[Terrain-RGB encoding](https://docs.mapbox.com/data/tilesets/reference/mapbox-terrain-rgb-v1/)
formula `height = -10000 + ((R*65536 + G*256 + B) * 0.1)`. Mathematical
formulas are not subject to copyright; this notice is informational.

### PMTiles v3 Specification

`Packages/com.kazukikuriyama.maplibre-unity/Runtime/Source/PMTiles/`
implements the
[PMTiles v3 specification](https://github.com/protomaps/PMTiles/blob/main/spec/v3/spec.md)
maintained by Protomaps LLC (BSD-3-Clause). Implementation is
clean-room from the spec.

### MBTiles Specification

`Packages/com.kazukikuriyama.maplibre-unity/Runtime/Source/MBTiles/`
implements the
[MBTiles specification](https://github.com/mapbox/mbtiles-spec) (BSD-3,
Mapbox). The library does **not** bundle a SQLite binding; users supply
their own `IMBTilesBackend` (e.g. Mono.Data.Sqlite, sqlite-net) so the
package stays binding-agnostic.

### Trademark Notice

"Mapbox" is a registered trademark of Mapbox, Inc.; "MapLibre" is a
trademark of the MapLibre community. References to these names in
this file and in source comments are nominative — used only to
identify the public specifications and upstream projects this library
is interoperable with — and do not imply any affiliation with,
endorsement by, or sponsorship from either organization.

---

## Sample Demo Tile Sources

### Tile Providers

The bundled sample scenes reference public tile servers for illustration
purposes. The library itself fetches tiles via HTTP only — no tile data is
redistributed. When forking a sample for shipping, confirm your use case
fits each provider's terms; for heavy / commercial traffic, switch to a
provider you have a commercial agreement with.

| Provider | URL template | License / Terms | Used by |
|---|---|---|---|
| OpenStreetMap Standard | `tile.openstreetmap.org/{z}/{x}/{y}.png` | [Tile Usage Policy](https://operations.osmfoundation.org/policies/tiles/) — heavy / commercial use prohibited; ODbL on data | BasicRasterMap, MarkerPopupDemo, EventDemo, PointerEventDemo, TerrainDemo, GeoJsonDemo, CustomLayerDemo, FeatureStateHoverDemo, ColorInterpolationDemo, CustomSourceDemo, PerFeaturePatternDemo, ImageSourceDemo, DynamicSourceLayerDemo, PMTilesDemo, BackgroundPatternDemo, PrefabSourceDemo, ParticleSourceDemo, StyleSwitchDemo |
| CARTO Voyager | `basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}.png` | CC BY 3.0 (basemap design) + ODbL (data); see [carto.com/attributions](https://carto.com/attributions) | StyleSwitchDemo |
| OpenTopoMap | `a.tile.opentopomap.org/{z}/{x}/{y}.png` | CC-BY-SA 3.0 (style) + ODbL (data) + SRTM; non-commercial / educational use, see [opentopomap.org/about](https://opentopomap.org/about) | StyleSwitchDemo |
| OpenFreeMap (planet) | `tiles.openfreemap.org/planet/{z}/{x}/{y}.pbf` | OpenMapTiles schema + OSM (ODbL); free public service, see [openfreemap.org](https://openfreemap.org) | VectorDemo, CircleDemo, FillExtrusionDemo, HeatmapDemo, LineGradientDemo, DashLineTest, SkyAndLightDemo, SymbolTranslateDemo, etc. |
| MapLibre demotiles | `demotiles.maplibre.org/style.json` (vector) | OpenMapTiles schema + OSM (ODbL); demo service intended for testing | (previously StyleSwitchDemo, removed) |
| MapTiler Demo Tiles | `api.maptiler.com/...` | Requires API key for production use; some samples use the public demo key | (none currently) |

Each source's attribution string is encoded in the corresponding sample's
style JSON and is shown automatically by the `MapControlsOverlay`
attribution control. If you swap a source, update its attribution string
to match the new provider.

---

## Fonts (User-Provided)

MapLibre Unity does **not** bundle any font files. Users must provide their own
`TMP_FontAsset` via the Inspector (`MapLibreMap > Text > Symbol Font`).

When selecting a font for your project, ensure the license is compatible with
your use case. Below are recommended open-source fonts suitable for map labels.

### Recommended Fonts for CJK (Japanese, Chinese, Korean)

| Font | License | URL |
|------|---------|-----|
| Noto Sans JP | SIL Open Font License 1.1 | https://fonts.google.com/noto/specimen/Noto+Sans+JP |
| Noto Sans CJK | SIL Open Font License 1.1 | https://github.com/notofonts/noto-cjk |
| M PLUS 1p | SIL Open Font License 1.1 | https://fonts.google.com/specimen/M+PLUS+1p |
| Source Han Sans | SIL Open Font License 1.1 | https://github.com/adobe-fonts/source-han-sans |
| BIZ UDGothic | SIL Open Font License 1.1 | https://fonts.google.com/specimen/BIZ+UDGothic |

### Recommended Fonts for Latin / General

| Font | License | URL |
|------|---------|-----|
| Noto Sans | SIL Open Font License 1.1 | https://fonts.google.com/noto/specimen/Noto+Sans |
| Open Sans | SIL Open Font License 1.1 | https://fonts.google.com/specimen/Open+Sans |
| Roboto | Apache License 2.0 | https://fonts.google.com/specimen/Roboto |

### SIL Open Font License 1.1 (Summary)

Fonts under SIL OFL 1.1 can be:
- Used freely in any project (commercial or non-commercial)
- Bundled and redistributed with your application
- Modified and redistributed under the same license

The only restriction is that the fonts cannot be sold by themselves.
Full license text: https://openfontlicense.org/

### Font Setup

**Automatic (recommended):** Use the **MapLibre > Font Setup** menu in the Unity Editor.
This generates a TMP_FontAsset from OS system fonts and auto-assigns it to MapLibreMap.

**Manual:** If you prefer to use a specific font file:

1. Import the `.ttf` or `.otf` file into your Unity project
2. Open **Window > TextMeshPro > Font Asset Creator**
3. Select the source font and configure atlas settings
4. For CJK fonts, use **Dynamic** rendering mode to generate glyphs on demand
   (this avoids creating a massive static atlas for thousands of characters)
5. Assign the generated `TMP_FontAsset` to `MapLibreMap > Text > Symbol Font`

**Runtime fallback (development only):**
If no font is assigned, MapLibre Unity reads fonts already installed on the OS
(Hiragino on macOS, Yu Gothic on Windows, etc.) and uses them at runtime.
This is the same as a web browser or word processor displaying text with OS fonts —
no font files are copied or redistributed. This feature exists so that developers
can test without configuring a font first.

> **When distributing your application:**
> The runtime fallback depends on fonts being installed on the end user's machine.
> For reliable text display in a distributed app:
>
> 1. Use **MapLibre > Font Setup** to generate a font asset, or
> 2. Import an OFL-licensed font (e.g. Noto Sans JP) and create a TMP_FontAsset manually
>
> Then assign it to `MapLibreMap > Text > Symbol Font`.
> If you include a font file in your build, make sure its license allows redistribution.
> All fonts listed above under SIL OFL 1.1 permit this.
