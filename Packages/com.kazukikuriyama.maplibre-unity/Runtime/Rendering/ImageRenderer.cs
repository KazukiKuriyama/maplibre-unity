using MapLibre.Unity.Source;
using MapLibre.Unity.Style;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Renders an image source as a subdivided quad with four geographic corners.
    /// The mesh is rebuilt when the corners change; its world-space position is
    /// refreshed every frame so it stays aligned with the moving/zooming map.
    /// </summary>
    public class ImageRenderer
    {
        private const int SubdivisionsPerEdge = 16;

        private readonly Transform _parent;
        private readonly Material _material;
        private readonly MaterialPropertyBlock _propertyBlock = new();
        private float _yOffset;

        private GameObject _gameObject;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private ImageSource _source;
        private RasterPaintProperties _paintProps = new();
        private MercatorCoordinate[] _cornerMerc;

        private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");
        private static readonly int OpacityPropertyId = Shader.PropertyToID("_Opacity");

        /// <param name="layerOrder">Layer index in style (0-based). Higher = rendered on top.</param>
        public ImageRenderer(Transform parent, Shader rasterShader, int layerOrder = 0)
        {
            _parent = parent;
            if (rasterShader != null)
            {
                _material = new Material(rasterShader);
                _material.renderQueue = 3000 + layerOrder;
            }
            _yOffset = 0.005f + layerOrder * 0.001f;
        }

        public void SetLayerOrder(int layerOrder)
        {
            if (_material != null) _material.renderQueue = 3000 + layerOrder;
            _yOffset = 0.005f + layerOrder * 0.001f;
        }

        public void SetPaintProperties(RasterPaintProperties paint)
        {
            _paintProps = paint ?? new RasterPaintProperties();
        }

        public void SetSource(ImageSource source)
        {
            if (_source != null)
                _source.OnChanged -= HandleSourceChanged;
            _source = source;
            if (_source != null)
                _source.OnChanged += HandleSourceChanged;
            HandleSourceChanged();
        }

        private void HandleSourceChanged()
        {
            if (_source == null) return;

            EnsureGameObject();

            var texture = _source.Texture;
            if (texture != null)
            {
                _propertyBlock.SetTexture(MainTexPropertyId, texture);
                _meshRenderer.SetPropertyBlock(_propertyBlock);
            }

            BuildMesh(_source.Corners);
        }

        private void EnsureGameObject()
        {
            if (_gameObject != null) return;

            _gameObject = new GameObject("ImageSource");
            _gameObject.transform.SetParent(_parent, worldPositionStays: false);
            _meshFilter = _gameObject.AddComponent<MeshFilter>();
            _meshRenderer = _gameObject.AddComponent<MeshRenderer>();
            _meshRenderer.sharedMaterial = _material;
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;

            _mesh = new Mesh { name = "ImageSourceMesh" };
            _meshFilter.sharedMesh = _mesh;
        }

        private void BuildMesh(LngLat[] corners)
        {
            if (corners == null || corners.Length < 4 || _mesh == null)
            {
                if (_meshRenderer != null) _meshRenderer.enabled = false;
                _cornerMerc = null;
                return;
            }

            _meshRenderer.enabled = true;

            // Cache Mercator corners -- the image should drape over the mercator
            // projection, so we interpolate in mercator space (not lat/lng).
            _cornerMerc = new[]
            {
                CoordinateConversion.LngLatToMercator(corners[0]),
                CoordinateConversion.LngLatToMercator(corners[1]),
                CoordinateConversion.LngLatToMercator(corners[2]),
                CoordinateConversion.LngLatToMercator(corners[3])
            };

            int n = SubdivisionsPerEdge;
            int vertCount = (n + 1) * (n + 1);
            var vertices = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var triangles = new int[n * n * 6];

            // Positions get written each frame in UpdatePosition; initialize to zero.
            for (int y = 0; y <= n; y++)
            {
                for (int x = 0; x <= n; x++)
                {
                    int idx = y * (n + 1) + x;
                    vertices[idx] = Vector3.zero;
                    // MapLibre spec: coords[0] = top-left, UV (0,1); coords[3] = bottom-left UV (0,0).
                    // Unity texture V axis goes bottom→top, so flip y.
                    uvs[idx] = new Vector2(x / (float)n, 1f - y / (float)n);
                }
            }

            int t = 0;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i0 = y * (n + 1) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + (n + 1);
                    int i3 = i2 + 1;
                    triangles[t++] = i0;
                    triangles[t++] = i2;
                    triangles[t++] = i1;
                    triangles[t++] = i1;
                    triangles[t++] = i2;
                    triangles[t++] = i3;
                }
            }

            _mesh.Clear();
            _mesh.vertices = vertices;
            _mesh.uv = uvs;
            _mesh.triangles = triangles;
        }

        public void UpdatePosition(MercatorCoordinate mapCenter, float zoom)
        {
            if (_source == null || _mesh == null || _cornerMerc == null || _gameObject == null)
                return;

            _gameObject.transform.localPosition = new Vector3(0f, _yOffset, 0f);

            float worldScale = CoordinateConversion.GetWorldScale(zoom);
            int n = SubdivisionsPerEdge;
            int vertCount = (n + 1) * (n + 1);
            var vertices = new Vector3[vertCount];

            // Bilinear interpolation in mercator space.
            // corners: [0]=TL, [1]=TR, [2]=BR, [3]=BL
            for (int y = 0; y <= n; y++)
            {
                float v = y / (float)n;
                for (int x = 0; x <= n; x++)
                {
                    float u = x / (float)n;
                    double mx =
                        (1 - u) * (1 - v) * _cornerMerc[0].X +
                        u * (1 - v) * _cornerMerc[1].X +
                        u * v * _cornerMerc[2].X +
                        (1 - u) * v * _cornerMerc[3].X;
                    double my =
                        (1 - u) * (1 - v) * _cornerMerc[0].Y +
                        u * (1 - v) * _cornerMerc[1].Y +
                        u * v * _cornerMerc[2].Y +
                        (1 - u) * v * _cornerMerc[3].Y;
                    vertices[y * (n + 1) + x] = CoordinateConversion.MercatorToUnityWorld(
                        new MercatorCoordinate(mx, my), mapCenter, worldScale);
                }
            }

            _mesh.vertices = vertices;
            _mesh.RecalculateBounds();

            // Zoom-dependent opacity from raster-opacity paint property.
            float opacity = _paintProps.ResolveOpacity(zoom);
            _propertyBlock.SetFloat(OpacityPropertyId, opacity);
            if (_source.Texture != null)
                _propertyBlock.SetTexture(MainTexPropertyId, _source.Texture);
            _meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        public void Dispose()
        {
            if (_source != null)
                _source.OnChanged -= HandleSourceChanged;
            if (_mesh != null)
                Object.Destroy(_mesh);
            if (_gameObject != null)
                Object.Destroy(_gameObject);
            if (_material != null)
                Object.Destroy(_material);
            _mesh = null;
            _gameObject = null;
        }
    }
}
