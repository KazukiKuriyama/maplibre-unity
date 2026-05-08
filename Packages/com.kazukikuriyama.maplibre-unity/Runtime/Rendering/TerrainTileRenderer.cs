using System.Collections.Generic;
using MapLibre.Unity.Terrain;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Per-raster-layer renderer used when the active style declares a "terrain"
    /// property. Generates a subdivided grid mesh per visible tile and lets the
    /// vertex shader displace each vertex by sampling the loaded DEM texture.
    /// The fragment shader draws the layer's raster tile (e.g. OSM) draped over
    /// the displaced surface, producing real 3D relief visible at any pitch.
    ///
    /// Each tile owns its own Material instance (rather than sharing one and
    /// using MaterialPropertyBlock) -- this is the only reliable way to bind a
    /// runtime-created texture for vertex-stage sampling in URP. The MPB path
    /// silently leaves the DEM unbound in vertex shaders, leaving the mesh flat.
    /// Per-tile materials cost <100 KB total for the typical 16-tile working set.
    /// </summary>
    public class TerrainTileRenderer : ILayerRenderer
    {
        private const int GridResolution = 33;          // vertices per edge (=> 32x32 quads)
        private const double EarthEquatorMeters = 40075016.686;

        private readonly Transform _parent;
        private readonly Shader _terrainShader;
        private readonly TerrainManager _terrainManager;
        private readonly Mesh _gridMesh;
        private readonly Dictionary<CanonicalTileID, TileEntry> _activeTiles = new();

        private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");
        private static readonly int DemTexPropertyId = Shader.PropertyToID("_DemTex");
        private static readonly int OpacityPropertyId = Shader.PropertyToID("_Opacity");
        private static readonly int MetersToWorldYPropertyId = Shader.PropertyToID("_MetersToWorldY");
        private static readonly int DemUVOffsetPropertyId = Shader.PropertyToID("_DemUVOffset");
        private static readonly int DemUVScalePropertyId = Shader.PropertyToID("_DemUVScale");

        private struct TileEntry
        {
            public GameObject GameObject;
            public Material Material;
            public Texture2D RasterTexture;
        }

        public TerrainTileRenderer(Transform parent, Shader terrainShader, TerrainManager terrainManager)
        {
            _parent = parent;
            _terrainShader = terrainShader;
            _terrainManager = terrainManager;
            _gridMesh = BuildGridMesh(GridResolution);
            _terrainManager.OnTerrainTileReady += OnTerrainTileReady;
        }

        public void ShowTile(CanonicalTileID tileId, Texture2D rasterTexture,
            MercatorCoordinate mapCenter, float zoom)
        {
            if (_activeTiles.TryGetValue(tileId, out var existing))
            {
                existing.RasterTexture = rasterTexture;
                _activeTiles[tileId] = existing;
                ApplyMaterialState(existing.Material, tileId, rasterTexture, zoom);
                PositionTile(existing.GameObject, tileId, mapCenter, zoom);
                return;
            }

            // Create a per-tile Material so DEM texture binding survives into the
            // vertex stage (MPB-bound textures are unreliable for vertex sampling).
            var material = new Material(_terrainShader);
            ApplyEncodingKeyword(material);

            var go = new GameObject($"terrain_{tileId}");
            go.transform.SetParent(_parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _gridMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _activeTiles[tileId] = new TileEntry
            {
                GameObject = go,
                Material = material,
                RasterTexture = rasterTexture
            };

            PositionTile(go, tileId, mapCenter, zoom);
            ApplyMaterialState(material, tileId, rasterTexture, zoom);
        }

        public void HideTile(CanonicalTileID tileId)
        {
            if (_activeTiles.TryGetValue(tileId, out var entry))
            {
                if (entry.Material != null) Object.Destroy(entry.Material);
                Object.Destroy(entry.GameObject);
                _activeTiles.Remove(tileId);
            }
        }

        public bool HasTile(CanonicalTileID tileId) => _activeTiles.ContainsKey(tileId);

        public void UpdateAllPositions(MercatorCoordinate mapCenter, float zoom)
        {
            foreach (var kvp in _activeTiles)
            {
                PositionTile(kvp.Value.GameObject, kvp.Key, mapCenter, zoom);
                // _MetersToWorldY depends on zoom; refresh it (and DEM binding,
                // in case sub-region sampling changed).
                ApplyMaterialState(kvp.Value.Material, kvp.Key, kvp.Value.RasterTexture, zoom);
            }
        }

        private void OnTerrainTileReady(CanonicalTileID demTileId)
        {
            // Re-bind DEM on any active tile that depends on this DEM tile.
            foreach (var kvp in _activeTiles)
            {
                if (IsDescendantOrSame(kvp.Key, demTileId))
                    BindDem(kvp.Value.Material, kvp.Key);
            }
        }

        private static bool IsDescendantOrSame(CanonicalTileID candidate, CanonicalTileID ancestor)
        {
            if (candidate.Z < ancestor.Z) return false;
            int dz = candidate.Z - ancestor.Z;
            return (candidate.X >> dz) == ancestor.X && (candidate.Y >> dz) == ancestor.Y;
        }

        private void ApplyMaterialState(Material material, CanonicalTileID tileId,
            Texture2D rasterTexture, float zoom)
        {
            if (material == null) return;
            if (rasterTexture != null)
                material.SetTexture(MainTexPropertyId, rasterTexture);
            material.SetFloat(OpacityPropertyId, 1f);
            material.SetFloat(MetersToWorldYPropertyId, ComputeMetersToWorldY(zoom));
            BindDem(material, tileId);
        }

        private void BindDem(Material material, CanonicalTileID renderTileId)
        {
            if (material == null) return;
            if (_terrainManager.TryResolveDem(renderTileId, out _, out var demTex,
                    out var uvOffset, out var uvScale))
            {
                material.SetTexture(DemTexPropertyId, demTex);
                material.SetVector(DemUVOffsetPropertyId, new Vector4(uvOffset.x, uvOffset.y, 0, 0));
                material.SetVector(DemUVScalePropertyId, new Vector4(uvScale.x, uvScale.y, 0, 0));
            }
            else
            {
                // No DEM yet: set scale to 0 so the vertex shader doesn't displace.
                material.SetVector(DemUVOffsetPropertyId, Vector4.zero);
                material.SetVector(DemUVScalePropertyId, Vector4.zero);
            }
        }

        private void ApplyEncodingKeyword(Material material)
        {
            if (_terrainManager.IsTerrarium)
            {
                material.EnableKeyword("_ENCODING_TERRARIUM");
                material.DisableKeyword("_ENCODING_MAPBOX");
            }
            else
            {
                material.EnableKeyword("_ENCODING_MAPBOX");
                material.DisableKeyword("_ENCODING_TERRARIUM");
            }
        }

        private float ComputeMetersToWorldY(float zoom)
        {
            float worldScale = CoordinateConversion.GetWorldScale(zoom);
            float exaggeration = _terrainManager.Exaggeration;
            return (float)(worldScale * exaggeration / EarthEquatorMeters);
        }

        private static void PositionTile(GameObject go, CanonicalTileID tileId,
            MercatorCoordinate mapCenter, float zoom)
        {
            float worldScale = CoordinateConversion.GetWorldScale(zoom);
            int n = 1 << tileId.Z;
            double tileSize = 1.0 / n;
            double tileCenterX = (tileId.X + 0.5) * tileSize;
            double tileCenterY = (tileId.Y + 0.5) * tileSize;
            var tileCenterMerc = new MercatorCoordinate(tileCenterX, tileCenterY);
            Vector3 worldPos = CoordinateConversion.MercatorToUnityWorld(tileCenterMerc, mapCenter, worldScale);

            go.transform.localPosition = worldPos;
            float tileWorldSize = (float)(tileSize * worldScale);
            // X/Z scale to tile footprint; Y scale = 1 so the vertex shader's
            // world-meters → world-Y delta maps directly to scene units.
            go.transform.localScale = new Vector3(tileWorldSize, 1f, tileWorldSize);
        }

        private static Mesh BuildGridMesh(int resolution)
        {
            int verts = resolution * resolution;
            int quads = (resolution - 1) * (resolution - 1);
            var positions = new Vector3[verts];
            var uvs = new Vector2[verts];
            var normals = new Vector3[verts];
            var triangles = new int[quads * 6];

            for (int j = 0; j < resolution; j++)
            {
                float v = j / (float)(resolution - 1);
                for (int i = 0; i < resolution; i++)
                {
                    float u = i / (float)(resolution - 1);
                    int idx = j * resolution + i;
                    positions[idx] = new Vector3(u - 0.5f, 0f, v - 0.5f);
                    uvs[idx] = new Vector2(u, v);
                    normals[idx] = Vector3.up;
                }
            }

            int t = 0;
            for (int j = 0; j < resolution - 1; j++)
            {
                for (int i = 0; i < resolution - 1; i++)
                {
                    int a = j * resolution + i;
                    int b = a + 1;
                    int c = a + resolution;
                    int d = c + 1;
                    triangles[t++] = a;
                    triangles[t++] = c;
                    triangles[t++] = b;
                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = d;
                }
            }

            var mesh = new Mesh
            {
                name = $"TerrainGrid_{resolution}x{resolution}",
                indexFormat = verts > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.vertices = positions;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            // Generous Y bounds so vertex displacement (up to several thousand
            // world units for high mountains) doesn't get frustum-culled.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 10000f, 1f));
            return mesh;
        }

        public void Clear()
        {
            var keys = new List<CanonicalTileID>(_activeTiles.Keys);
            foreach (var key in keys)
                HideTile(key);
        }

        public void Dispose()
        {
            Clear();
            if (_terrainManager != null)
                _terrainManager.OnTerrainTileReady -= OnTerrainTileReady;
            if (_gridMesh != null) Object.Destroy(_gridMesh);
        }
    }
}
