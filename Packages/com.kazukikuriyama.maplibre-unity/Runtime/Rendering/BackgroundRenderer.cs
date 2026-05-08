using MapLibre.Unity.Style;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    public class BackgroundRenderer
    {
        private GameObject _backgroundPlane;
        private Material _backgroundMaterial;
        private Material _patternMaterial;
        private Shader _patternShader;
        private SpriteAtlas _spriteAtlas;
        // Currently-applied pattern name (empty = solid color path is in use).
        private string _activePattern;

        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int PatternTexPropertyId = Shader.PropertyToID("_PatternTex");
        private static readonly int PatternUVRectPropertyId = Shader.PropertyToID("_PatternUVRect");
        private static readonly int PatternPixelSizePropertyId = Shader.PropertyToID("_PatternPixelSize");
        private static readonly int CSSToWorldPropertyId = Shader.PropertyToID("_CSSToWorld");
        private static readonly int LayerOpacityPropertyId = Shader.PropertyToID("_LayerOpacity");

        public void Initialize(Transform parent, Shader backgroundShader, Color backgroundColor, float opacity,
            Shader patternShader = null, SpriteAtlas spriteAtlas = null)
        {
            _backgroundMaterial = new Material(backgroundShader);
            Color c = backgroundColor;
            c.a = opacity;
            _backgroundMaterial.SetColor(ColorPropertyId, c);
            _patternShader = patternShader;
            _spriteAtlas = spriteAtlas;

            _backgroundPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _backgroundPlane.name = "MapBackground";
            _backgroundPlane.transform.SetParent(parent, false);
            // Rotate quad to lie on XZ plane, slightly below tiles
            _backgroundPlane.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _backgroundPlane.transform.localPosition = new Vector3(0f, -0.01f, 0f);

            var renderer = _backgroundPlane.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _backgroundMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Remove collider
            var collider = _backgroundPlane.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            // Initial size -- will be updated dynamically based on camera far clip plane
            UpdateSize(1f, 1f);
        }

        public void UpdateColor(Color color, float opacity)
        {
            if (_backgroundMaterial == null) return;
            Color c = color;
            c.a = opacity;
            _backgroundMaterial.SetColor(ColorPropertyId, c);

            // When the layer falls back to a solid color (no pattern), restore the
            // solid material -- UpdatePattern may have swapped it earlier.
            if (string.IsNullOrEmpty(_activePattern) && _backgroundPlane != null)
            {
                var renderer = _backgroundPlane.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.sharedMaterial != _backgroundMaterial)
                    renderer.sharedMaterial = _backgroundMaterial;
            }
        }

        /// <summary>
        /// Apply background-pattern. Pass <paramref name="patternName"/>=null to revert to
        /// the solid background color. Returns false when the requested sprite is
        /// missing from the atlas (caller logs / falls back).
        /// </summary>
        public bool UpdatePattern(string patternName, float opacity)
        {
            if (_backgroundPlane == null) return false;
            if (string.IsNullOrEmpty(patternName))
            {
                _activePattern = null;
                var renderer = _backgroundPlane.GetComponent<MeshRenderer>();
                if (renderer != null && _backgroundMaterial != null)
                    renderer.sharedMaterial = _backgroundMaterial;
                return true;
            }

            if (_patternShader == null || _spriteAtlas == null) return false;
            if (!_spriteAtlas.TryGetEntry(patternName, out var entry, out var uv, out var tex))
                return false;

            if (_patternMaterial == null)
                _patternMaterial = new Material(_patternShader);
            _patternMaterial.SetTexture(PatternTexPropertyId, tex);
            _patternMaterial.SetVector(PatternUVRectPropertyId,
                new Vector4(uv.x, uv.y, uv.width, uv.height));
            _patternMaterial.SetVector(PatternPixelSizePropertyId, new Vector4(
                entry.Width / entry.PixelRatio, entry.Height / entry.PixelRatio, 0f, 0f));
            _patternMaterial.SetFloat(LayerOpacityPropertyId, opacity);

            _activePattern = patternName;
            var rendererComp = _backgroundPlane.GetComponent<MeshRenderer>();
            if (rendererComp != null) rendererComp.sharedMaterial = _patternMaterial;
            return true;
        }

        /// <summary>
        /// Refresh the per-frame CSS → world conversion for the pattern shader.
        /// Pattern world size = patternPixelSize × cssToWorld; without this update
        /// the pattern would drift in size as the camera zooms.
        /// </summary>
        public void UpdatePatternScale(float cssToWorld, float opacity)
        {
            if (_patternMaterial == null || string.IsNullOrEmpty(_activePattern)) return;
            _patternMaterial.SetFloat(CSSToWorldPropertyId, cssToWorld);
            _patternMaterial.SetFloat(LayerOpacityPropertyId, opacity);
        }

        public void UpdateSize(float worldWidth, float worldHeight)
        {
            if (_backgroundPlane == null) return;
            _backgroundPlane.transform.localScale = new Vector3(worldWidth, worldHeight, 1f);
        }

        public void Dispose()
        {
            if (_backgroundPlane != null)
                Object.Destroy(_backgroundPlane);
            if (_backgroundMaterial != null)
                Object.Destroy(_backgroundMaterial);
            if (_patternMaterial != null)
                Object.Destroy(_patternMaterial);
        }
    }
}
