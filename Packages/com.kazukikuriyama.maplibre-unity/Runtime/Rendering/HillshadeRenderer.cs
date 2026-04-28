using System.Collections.Generic;
using MapLibre.Unity.Expressions;
using MapLibre.Unity.Style;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    public class HillshadeRenderer : ILayerRenderer
    {
        private readonly Dictionary<CanonicalTileID, GameObject> _activeTileObjects = new();
        private readonly Transform _parent;
        private readonly Material _hillshadeMaterial;
        private readonly MaterialPropertyBlock _propertyBlock = new();
        private readonly Mesh _quadMesh;
        private float _yOffset;

        private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");
        private static readonly int TexelSizePropertyId = Shader.PropertyToID("_TexelSize");
        private static readonly int PixelMetersPropertyId = Shader.PropertyToID("_PixelMeters");
        private static readonly int IlluminationDirPropertyId = Shader.PropertyToID("_IlluminationDir");
        private static readonly int ExaggerationPropertyId = Shader.PropertyToID("_Exaggeration");
        private static readonly int ShadowColorPropertyId = Shader.PropertyToID("_ShadowColor");
        private static readonly int HighlightColorPropertyId = Shader.PropertyToID("_HighlightColor");
        private static readonly int AccentColorPropertyId = Shader.PropertyToID("_AccentColor");
        private static readonly int OpacityPropertyId = Shader.PropertyToID("_Opacity");

        // WGS84 equatorial circumference in meters (Web Mercator).
        private const double EarthEquatorMeters = 40075016.686;

        private HillshadePaintProperties _paintProps;
        private bool _useTerrarium = true;

        public HillshadeRenderer(Transform parent, Shader hillshadeShader, int layerOrder = 0)
        {
            _parent = parent;
            _quadMesh = CreateQuadMesh();

            // Hillshade is alpha-blended; render in the Transparent queue so it composites
            // correctly on top of any preceding raster/vector layers (also in Transparent).
            int renderQueue = 3000 + layerOrder;

            if (hillshadeShader != null)
            {
                _hillshadeMaterial = new Material(hillshadeShader);
                _hillshadeMaterial.renderQueue = renderQueue;
            }

            _yOffset = 0.0005f + layerOrder * 0.001f;
        }

        public void SetLayerOrder(int layerOrder)
        {
            if (_hillshadeMaterial != null) _hillshadeMaterial.renderQueue = 3000 + layerOrder;
            _yOffset = 0.0005f + layerOrder * 0.001f;
        }

        public void SetPaintProperties(HillshadePaintProperties props)
        {
            _paintProps = props;
        }

        public void SetEncoding(string encoding)
        {
            _useTerrarium = encoding != "mapbox";
            if (_hillshadeMaterial != null)
            {
                if (_useTerrarium)
                {
                    _hillshadeMaterial.EnableKeyword("_ENCODING_TERRARIUM");
                    _hillshadeMaterial.DisableKeyword("_ENCODING_MAPBOX");
                }
                else
                {
                    _hillshadeMaterial.EnableKeyword("_ENCODING_MAPBOX");
                    _hillshadeMaterial.DisableKeyword("_ENCODING_TERRARIUM");
                }
            }
        }

        public void ShowTile(CanonicalTileID tileId, Texture2D demTexture,
            MercatorCoordinate mapCenter, float zoom)
        {
            if (_activeTileObjects.ContainsKey(tileId))
                return;

            if (_hillshadeMaterial == null) return;

            var go = CreateTileObject($"hillshade_{tileId}");
            _activeTileObjects[tileId] = go;

            PositionTile(go, tileId, mapCenter, zoom);
            ApplyProperties(go, tileId, demTexture, zoom);
        }

        public void HideTile(CanonicalTileID tileId)
        {
            if (_activeTileObjects.TryGetValue(tileId, out var go))
            {
                Object.Destroy(go);
                _activeTileObjects.Remove(tileId);
            }
        }

        public void UpdateAllPositions(MercatorCoordinate mapCenter, float zoom)
        {
            foreach (var kvp in _activeTileObjects)
            {
                PositionTile(kvp.Value, kvp.Key, mapCenter, zoom);
                UpdateDynamicProperties(kvp.Value, zoom);
            }
        }

        public bool HasTile(CanonicalTileID tileId) => _activeTileObjects.ContainsKey(tileId);

        private void ApplyProperties(GameObject go, CanonicalTileID tileId, Texture2D demTexture, float zoom)
        {
            var ctx = new EvaluationContext(zoom);
            var renderer = go.GetComponent<MeshRenderer>();

            float texelSize = 1f / demTexture.width;
            float pixelMeters = ComputePixelMeters(tileId, demTexture.width);

            _propertyBlock.SetTexture(MainTexPropertyId, demTexture);
            _propertyBlock.SetFloat(TexelSizePropertyId, texelSize);
            _propertyBlock.SetFloat(PixelMetersPropertyId, pixelMeters);

            if (_paintProps != null)
            {
                float dirDeg = _paintProps.ResolveIlluminationDirection(ctx);
                float dirRad = dirDeg * Mathf.Deg2Rad;
                _propertyBlock.SetVector(IlluminationDirPropertyId,
                    new Vector4(Mathf.Sin(dirRad), Mathf.Cos(dirRad), 0f, 0f));
                _propertyBlock.SetFloat(ExaggerationPropertyId, _paintProps.ResolveExaggeration(ctx));
                _propertyBlock.SetColor(ShadowColorPropertyId, _paintProps.ResolveShadowColor(ctx));
                _propertyBlock.SetColor(HighlightColorPropertyId, _paintProps.ResolveHighlightColor(ctx));
                _propertyBlock.SetColor(AccentColorPropertyId, _paintProps.ResolveAccentColor(ctx));
                _propertyBlock.SetFloat(OpacityPropertyId, _paintProps.ResolveOpacity(ctx));
            }
            else
            {
                float dirRad = 335f * Mathf.Deg2Rad;
                _propertyBlock.SetVector(IlluminationDirPropertyId,
                    new Vector4(Mathf.Sin(dirRad), Mathf.Cos(dirRad), 0f, 0f));
                _propertyBlock.SetFloat(ExaggerationPropertyId, 0.5f);
                _propertyBlock.SetColor(ShadowColorPropertyId, Color.black);
                _propertyBlock.SetColor(HighlightColorPropertyId, Color.white);
                _propertyBlock.SetColor(AccentColorPropertyId, Color.black);
                _propertyBlock.SetFloat(OpacityPropertyId, 1f);
            }

            renderer.SetPropertyBlock(_propertyBlock);
        }

        private void UpdateDynamicProperties(GameObject go, float zoom)
        {
            if (_paintProps == null) return;

            var ctx = new EvaluationContext(zoom);
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            renderer.GetPropertyBlock(_propertyBlock);

            float dirDeg = _paintProps.ResolveIlluminationDirection(ctx);
            float dirRad = dirDeg * Mathf.Deg2Rad;
            _propertyBlock.SetVector(IlluminationDirPropertyId,
                new Vector4(Mathf.Sin(dirRad), Mathf.Cos(dirRad), 0f, 0f));
            _propertyBlock.SetFloat(ExaggerationPropertyId, _paintProps.ResolveExaggeration(ctx));
            _propertyBlock.SetColor(ShadowColorPropertyId, _paintProps.ResolveShadowColor(ctx));
            _propertyBlock.SetColor(HighlightColorPropertyId, _paintProps.ResolveHighlightColor(ctx));
            _propertyBlock.SetColor(AccentColorPropertyId, _paintProps.ResolveAccentColor(ctx));
            _propertyBlock.SetFloat(OpacityPropertyId, _paintProps.ResolveOpacity(ctx));

            renderer.SetPropertyBlock(_propertyBlock);
        }

        private GameObject CreateTileObject(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_parent, false);

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = _quadMesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _hillshadeMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            return go;
        }

        private void PositionTile(GameObject go, CanonicalTileID tileId,
            MercatorCoordinate mapCenter, float zoom)
        {
            float worldScale = CoordinateConversion.GetWorldScale(zoom);
            int n = 1 << tileId.Z;

            double tileSize = 1.0 / n;
            double tileCenterX = (tileId.X + 0.5) * tileSize;
            double tileCenterY = (tileId.Y + 0.5) * tileSize;

            var tileCenterMerc = new MercatorCoordinate(tileCenterX, tileCenterY);
            Vector3 worldPos = CoordinateConversion.MercatorToUnityWorld(tileCenterMerc, mapCenter, worldScale);
            worldPos.y = _yOffset;

            go.transform.localPosition = worldPos;

            float tileWorldSize = (float)(tileSize * worldScale);
            go.transform.localScale = new Vector3(tileWorldSize, 1f, tileWorldSize);
        }

        public void Clear()
        {
            var keys = new List<CanonicalTileID>(_activeTileObjects.Keys);
            foreach (var key in keys)
                HideTile(key);
        }

        public void Dispose()
        {
            Clear();
            if (_hillshadeMaterial != null) Object.Destroy(_hillshadeMaterial);
            if (_quadMesh != null) Object.Destroy(_quadMesh);
        }

        /// <summary>
        /// Compute ground meters per DEM-pixel for a tile, accounting for Web Mercator
        /// latitude distortion. Used to normalize the elevation gradient so that
        /// `hillshade-exaggeration` behaves as a physical slope multiplier independent
        /// of the source DEM tile zoom or latitude.
        /// </summary>
        private static float ComputePixelMeters(CanonicalTileID tileId, int demPixelWidth)
        {
            int n = 1 << tileId.Z;
            // Tile center latitude (degrees → radians)
            double mercY = (tileId.Y + 0.5) / n;
            double latRad = System.Math.Atan(System.Math.Sinh(System.Math.PI * (1.0 - 2.0 * mercY)));
            double tileMeters = EarthEquatorMeters * System.Math.Cos(latRad) / n;
            return (float)(tileMeters / demPixelWidth);
        }

        private static Mesh CreateQuadMesh()
        {
            var mesh = new Mesh { name = "HillshadeQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, 0f),
                new Vector2(1f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
            mesh.normals = new[]
            {
                Vector3.up, Vector3.up, Vector3.up, Vector3.up
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
