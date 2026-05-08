# Getting Started

A walkthrough that takes you from a fresh Unity project to a panning,
zooming MapLibre map. Estimated time: 10 minutes.

## 1. Project requirements

| Requirement | Version |
|---|---|
| Unity | 6 (6000.3.x) — earlier versions are not tested |
| Render pipeline | URP 17.x (Built-in / HDRP unsupported) |
| Input system | New Input System (`UnityEngine.InputSystem`) |
| `com.unity.nuget.newtonsoft-json` | 3.2.1 — installs automatically as a dependency |
| TextMeshPro | bundled with Unity 6 — only needed if your style does **not** declare a `glyphs` URL |

If your project already targets URP and has the New Input System enabled,
you can skip the rest of this section.

## 2. Add the package

### Via Unity Package Manager (recommended)

In the Unity Editor, open
**Window → Package Manager → + → Install package from git URL...** and
paste:

```
https://github.com/KazukiKuriyama/maplibre-unity.git?path=Packages/com.kazukikuriyama.maplibre-unity#v0.1.0
```

Replace `v0.1.0` with the desired tag, or omit `#v0.1.0` to track
`main`. Once installed, the package appears under **Packages → MapLibre
Unity** with this layout:

```
Packages/com.kazukikuriyama.maplibre-unity/
  Runtime/        Library code (asmdef: MapLibre.Unity)
  Editor/         Inspectors, font setup wizard
  Samples/        Example scenes — visible immediately under Packages/
  Shaders/        URP shaders for raster / vector / SDF text
```

Sample scenes ship with the package — no import step is required. Open
`Packages/MapLibre Unity/Samples/Home/HomeScene.unity` from the Project
window. Add the sample scenes to **File → Build Profiles** the first
time you press Play so HomeScene's `SceneManager.LoadScene` calls
resolve. Samples under `Packages/` are read-only; copy a demo into
`Assets/` if you want to modify it.

## 3. The minimum viable map

Create an empty scene with **Camera + Directional Light + EventSystem** as
usual, then add an empty GameObject named `Map`:

1. **Add Component → MapLibreMap**
2. In the Inspector, fill **Style Json** with:

```json
{
  "version": 8,
  "name": "OSM Raster",
  "center": [139.7670, 35.6814],
  "zoom": 11,
  "sources": {
    "osm": {
      "type": "raster",
      "tiles": ["https://tile.openstreetmap.org/{z}/{x}/{y}.png"],
      "tileSize": 256,
      "maxzoom": 19,
      "attribution": "© OpenStreetMap contributors"
    }
  },
  "layers": [
    { "id": "background", "type": "background", "paint": { "background-color": "#e0e0e0" } },
    { "id": "tiles", "type": "raster", "source": "osm" }
  ]
}
```

3. **Press Play.** OpenStreetMap raster tiles around Tokyo Station appear.
   Drag with the left mouse button to pan, scroll to zoom, right-drag to
   rotate, middle-drag to pitch.

That's it for the minimum case. Everything below is optional and shows
how to layer in more advanced features.

## 4. Adding a vector layer

Vector tiles render at any zoom and respect MapLibre Style Spec
expressions. Replace the `style.json` above with:

```json
{
  "version": 8,
  "sources": {
    "demo": {
      "type": "vector",
      "url": "https://demotiles.maplibre.org/tiles/tiles.json"
    }
  },
  "glyphs": "https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf",
  "center": [0, 0], "zoom": 1,
  "layers": [
    { "id": "bg", "type": "background", "paint": { "background-color": "#a0c4e8" } },
    { "id": "land", "type": "fill", "source": "demo", "source-layer": "land",
      "paint": { "fill-color": "#f7f3df" } },
    { "id": "places", "type": "symbol", "source": "demo", "source-layer": "place",
      "layout": { "text-field": "{name}", "text-font": ["Open Sans Regular"], "text-size": 14 },
      "paint": { "text-color": "#222", "text-halo-color": "#fff", "text-halo-width": 1.5 } }
  ]
}
```

Because the style declares a `glyphs` URL, MapLibre Unity fetches glyph
PBFs on demand and renders the labels via SDF — no `TMP_FontAsset` to
configure. If you remove the `glyphs` line, SymbolRenderer falls back to
TextMeshPro and you'll need to assign **Symbol Font** in the Inspector.

## 5. Driving styles from C#

Most apps build the style in code rather than typing JSON. The same
result as section 4:

```csharp
using MapLibre.Unity;
using MapLibre.Unity.Style;
using Newtonsoft.Json.Linq;

public class MapBootstrap : MonoBehaviour
{
    [SerializeField] MapLibreMap _map;

    void Start()
    {
        _map.SetGlyphs("https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf");

        _map.AddSource("demo", new SourceDefinition {
            Type = SourceType.Vector,
            Url = "https://demotiles.maplibre.org/tiles/tiles.json",
        });

        _map.AddLayer(new LayerDefinition {
            Id = "land",
            Type = LayerType.Fill,
            Source = "demo",
            SourceLayer = "land",
            Paint = new() { ["fill-color"] = "#f7f3df" },
        });
    }
}
```

`Map` should be the GameObject hosting `MapLibreMap`. Press Play —
identical to the JSON version.

## 6. Reacting to input

Map events use a familiar `On(type, handler)` pattern:

```csharp
_map.On(MapEventType.Click, e => {
    if (!e.LngLat.HasValue) return;
    Debug.Log($"Clicked at {e.LngLat.Value}");
});

_map.On(MapEventType.MoveEnd, e => {
    Debug.Log($"Centre is now {_map.GetCenter()} at zoom {_map.GetZoom()}");
});
```

Layer-scoped handlers populate `e.Features` automatically:

```csharp
_map.On(MapEventType.Click, "places", e => {
    foreach (var f in e.Features)
        Debug.Log($"Hit {f.Layer?.Id}: {f.Properties["name"]}");
});
```

## 7. Where to look next

- The bundled **Home** sample (`Packages/MapLibre Unity/Samples/Home/`) — `HomeScene` lists every sample and loads them at runtime.
- **README → Advanced Features** — short snippets for Custom Layer,
  Custom Source, feature-state, and Glyph URL.
- The bundled **FeatureStateHoverDemo** sample — the canonical
  example for state-driven highlighting.
- The bundled **CustomLayerDemo** sample — pin Unity GameObjects to
  longitude/latitude and let the renderer manage z-order.
- **CHANGELOG.md** — what's been added recently and what's still on the
  Phase 11 todo list.

## 8. Common stumbling blocks

The single highest-leverage step when something goes wrong is wiring
an error listener at startup so HTTP failures, parse errors, and
expression problems surface in the Console:

```csharp
_map.On(MapEventType.Error, e => Debug.LogError(e.ErrorMessage));
```

The most frequent issues:

| Symptom | First thing to check |
|---|---|
| Black / nothing rendering | The active camera must use a URP renderer asset. |
| Tiles never load | `MapEventType.Error` for the actual HTTP error; check tile URL template uses `{z}/{x}/{y}`. |
| Labels appear as squares | Either import TMP Essential Resources (TMP fallback path) or add a `glyphs` URL to the style (SDF path). |
| `EaseTo` / `FlyTo` snaps instantly | Durations are milliseconds — `600f`, not `0.6f`. |
| `feature-state` does nothing | Feature needs a stable non-zero `id`, and an expression must actually reference `["feature-state", ...]`. |

The full list with deeper explanations and platform-specific notes
(iOS ATS, Android `Internet Access`, font fallback at build time,
sprite/pattern lookups, …) lives in [Troubleshooting](Troubleshooting.md).
