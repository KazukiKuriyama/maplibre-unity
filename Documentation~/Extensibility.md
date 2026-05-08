# Extensibility

MapLibre Unity exposes the same extension points as MapLibre GL JS for
plugging your own rendering and tile pipelines into the map. Every
extension point is a plain C# interface — no MonoBehaviour subclasses,
no editor scripting, no native plugins.

| Extension point | Use case |
|---|---|
| `ICustomLayer` | Inject user-rendered geometry into the map's layer order. |
| `ICustomRasterSource` | Serve raster tiles from any backend (procedural, local files, custom HTTP). |
| `ICustomVectorSource` | Same, for vector PBF tiles. |
| `MapLibreMap.AddImage` | Register a `Texture2D` as a sprite at runtime, addressable by name from style expressions. |

## Custom Layer (`ICustomLayer`)

Mirrors MapLibre GL JS's `type: "custom"`. The library owns the slot
in render order (so `beforeId`, `MoveLayer`, and `minzoom` / `maxzoom`
all apply); you provide the rendering.

```csharp
public class MyLayer : ICustomLayer
{
    public string Id => "my-3d-objects";
    private GameObject _root;

    public void OnAdd(MapLibreMap map)
    {
        _root = new GameObject("MyLayer-Root");
        _root.transform.SetParent(map.transform, false);
        // Instantiate MeshRenderers / particle systems / etc.
    }

    public void Render(MapLibreMap map, UnityEngine.Camera camera)
    {
        var pos = map.LngLatToWorld(new LngLat(139.7, 35.7));
        if (pos.HasValue) _root.transform.localPosition = pos.Value;
    }

    public void OnRemove(MapLibreMap map)
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }
}

map.AddLayer(new MyLayer(), beforeId: "labels");
```

`Render` is called once per frame after the renderer has painted
layers up to the slot you occupy. `LngLatToWorld` returns the world
position for a given longitude/latitude — ideal for pinning Unity
GameObjects to map coordinates while the user pans and zooms.

See the **CustomLayerDemo** sample (rotating cubes pinned to Tokyo
landmarks) for an end-to-end example.

## Custom Source (`ICustomRasterSource` / `ICustomVectorSource`)

Serve tiles from any backend — procedural generation, local files,
custom HTTP, IPC, render-to-texture. Mirrors MapLibre GL JS's
`type: "custom"` source.

### Raster

```csharp
public class NoiseRasterSource : ICustomRasterSource
{
    public int TileSize => 256;
    public int MinZoom => 0;
    public int MaxZoom => 14;
    public double[] Bounds => null;
    public string Attribution => "Procedural Demo";

    public void LoadTile(CanonicalTileID tileId,
        Action<Texture2D> onSuccess, Action<string> onError)
    {
        var tex = GenerateProceduralTile(tileId);
        onSuccess(tex);
    }

    public void UnloadTile(CanonicalTileID tileId) { }
}

map.AddSource("noise", new NoiseRasterSource());
```

`onSuccess` / `onError` may be invoked asynchronously — the library
handles dispatch back to the main thread. The `Texture2D` you pass to
`onSuccess` is owned by `MapLibreMap` after the call: do not destroy
or reuse it; release happens in `UnloadTile`.

See the **CustomSourceDemo** sample for a working procedural noise
example.

### Vector

`ICustomVectorSource` follows the same shape, but `LoadTile` returns
PBF bytes:

```csharp
public class MyVectorSource : ICustomVectorSource
{
    public int MinZoom => 0;
    public int MaxZoom => 14;
    public double[] Bounds => null;
    public string Attribution => "My Provider";
    public string[] VectorLayerIds => new[] { "buildings", "roads" };

    public void LoadTile(CanonicalTileID tileId,
        Action<byte[]> onSuccess, Action<string> onError)
    {
        var pbf = FetchOrGenerate(tileId);
        onSuccess(pbf);
    }

    public void UnloadTile(CanonicalTileID tileId) { }
}

map.AddSource("custom-vector", new MyVectorSource());
```

The bytes flow through the standard gzip + parse + expression
pipeline, so paint expressions and filters work without any
additional wiring.

## Runtime Image Registration (`AddImage`)

When a style expression resolves to `["image", "name"]` (icons,
sprite-driven `fill-pattern`, `line-pattern`), the library looks the
name up in:

1. The style's `sprite` atlas, if declared.
2. Runtime images registered via `AddImage`.

This lets you ship dynamic icons without a sprite atlas:

```csharp
map.AddImage("vip-marker", myTexture2D);
```

Reference the image from any layer that accepts an image expression:

```json
{
  "icon-image": [
    "case",
    ["==", ["get", "type"], "vip"], "vip-marker",
    "marker-15"
  ]
}
```

`HasImage(name)` checks for existence; `RemoveImage(name)` releases
it. `UpdateImage(name, texture)` replaces the texture without
re-evaluating expressions, so animated icons can swap frames cheaply.

## See Also

- [Style Spec coverage](StyleSpec.md) — sources and layers supported
  natively (use `ICustom*` only when none of those fit).
- [Offline tile archives](OfflineArchives.md) — PMTiles / MBTiles use
  the same `ICustomRasterSource` / `ICustomVectorSource` hooks.
- [Expressions](Expressions.md) — `feature-state` and image lookups
  in paint expressions.
