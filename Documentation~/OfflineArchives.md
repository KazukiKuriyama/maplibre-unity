# Offline Tile Archives (PMTiles / MBTiles)

Both single-file tile archive formats are supported. They plug in via
the same [`ICustomRasterSource` / `ICustomVectorSource`][extensibility]
hooks documented in the extensibility guide, so existing styles work
unchanged once the source is registered under a chosen id.

[extensibility]: Extensibility.md

## When to Pick Which

| Format | Best for | Backend dependency |
|---|---|---|
| **PMTiles** | Range-served HTTPS hosting, simple deployments. | None — pure C#. |
| **MBTiles** | Existing MBTiles tooling output (e.g. `tilemaker`, `tippecanoe`). | A SQLite binding you supply. |

If you have a free choice, PMTiles is simpler to deploy: a static
HTTPS host with `Range` support is sufficient (S3, Cloudflare R2,
GitHub Pages, …). MBTiles is the right pick when you already have a
toolchain that emits `.mbtiles` files.

## PMTiles

PMTiles is pure C#. Open over HTTPS Range or from a local file:

```csharp
var archive = new PMTilesArchive("https://example.com/world.pmtiles", this);

StartCoroutine(archive.Open(
    onReady: () =>
    {
        if (archive.Header.TileType == PMTilesType.Mvt)
            map.AddSource("vector", new PMTilesVectorSource(archive, this));
        else
            map.AddSource("raster", new PMTilesRasterSource(archive, this));
    },
    onError: err => Debug.LogError($"PMTiles open failed: {err}")));
```

`this` is a `MonoBehaviour` used to drive `UnityWebRequest`
coroutines. The archive caches its directory so subsequent tile
requests avoid re-downloading the header.

For local files, pass an absolute `file://` path or
`Application.streamingAssetsPath`-relative path; the archive uses
`UnityWebRequest` for both. On Standalone builds this Just Works.
The same code path is also intended to handle Android StreamingAssets
(which live inside the APK and cannot be read with `System.IO.File`),
but mobile platforms are currently untested by the maintainer — see
the README's
[Supported platforms](https://github.com/KazukiKuriyama/maplibre-unity#supported-platforms)
section.

See the **PMTilesDemo** sample for an end-to-end example streaming a
public PMTiles archive over HTTP Range.

## MBTiles

MBTiles requires a SQLite binding because the file is a SQLite
database. The library ships the wrapper (`MBTilesReader` /
`MBTilesRasterSource` / `MBTilesVectorSource`) but expects you to
plug in a backend implementing `IMBTilesBackend`:

```csharp
public class MyMBTilesBackend : IMBTilesBackend
{
    private SQLiteConnection _conn;  // sqlite-net, Mono.Data.Sqlite, etc.

    public void Open(string filePath)
        => _conn = new SQLiteConnection(filePath);

    public byte[] GetTile(int z, int x, int y)
        => _conn.ExecuteScalar<byte[]>(
            "SELECT tile_data FROM tiles WHERE zoom_level=? AND tile_column=? AND tile_row=?",
            z, x, y);

    public Dictionary<string, string> GetMetadata()
    {
        // SELECT name, value FROM metadata
        // ...
    }

    public void Close() => _conn?.Close();
}

var reader = new MBTilesReader(new MyMBTilesBackend());
reader.Open("/path/to/world.mbtiles");
map.AddSource("vector", new MBTilesVectorSource(reader));
```

`MBTilesReader` already converts MBTiles' TMS y-coordinate to XYZ, so
the backend only needs raw column reads — no flip arithmetic on your
side.

### Recommended SQLite Bindings

| Binding | When to use |
|---|---|
| `sqlite-net-pcl` | Cross-platform; lightweight; works in IL2CPP builds. |
| `Mono.Data.Sqlite` | Editor / desktop Standalone, when you already have it referenced. |

The binding is a runtime concern only — `IMBTilesBackend` is the
abstraction layer, so you can swap implementations per platform if
needed.

## Mixing With Style URLs

Both formats are sources, not styles. You still need a `style.json`
declaring the layers that consume the source. A typical layout:

```csharp
// 1. Load the style with a placeholder source (e.g. an empty vector source).
await map.SetStyleAsync(localStyleJson);

// 2. Replace the placeholder with the offline archive once it opens.
map.RemoveSource("placeholder-id");
map.AddSource("vector", new PMTilesVectorSource(archive, this));
```

The renderer reuses cached tile bytes when the source id stays the
same — adding the archive after style load is fine, just register it
under the id the layers reference.

## See Also

- [Extensibility](Extensibility.md) — the `ICustom*Source` hooks
  PMTiles / MBTiles build on.
- [Style Spec coverage](StyleSpec.md) — vector vs raster source
  semantics.
- **PMTilesDemo** sample — public PMTiles archive over HTTP Range.
