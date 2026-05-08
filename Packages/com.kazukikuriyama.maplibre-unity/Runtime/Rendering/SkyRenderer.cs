using MapLibre.Unity.Style;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Renders a screen-space vertical gradient for the style's "sky" property.
    /// Drawn before all geometry (Queue=Background, ZTest=Always) so it sits
    /// behind tiles / extrusions; when the camera is pitched and ground tiles
    /// no longer cover the upper portion of the screen, the gradient shows
    /// through as sky + horizon.
    ///
    /// The mesh is a single quad with vertices already in NDC, and the shader
    /// skips MVP transformation -- so the quad always fills the screen
    /// regardless of camera pose. No per-frame transform tracking needed.
    /// </summary>
    public class SkyRenderer
    {
        private static readonly int SkyColorId = Shader.PropertyToID("_SkyColor");
        private static readonly int HorizonColorId = Shader.PropertyToID("_HorizonColor");
        private static readonly int SkyHorizonBlendId = Shader.PropertyToID("_SkyHorizonBlend");

        private readonly Material _material;
        private readonly GameObject _go;
        private readonly Mesh _ndcQuad;

        public SkyRenderer(Transform parent, Shader shader)
        {
            _material = new Material(shader);
            // Just before Geometry so opaque map content overdraws the sky where
            // tiles are present. Background queue default is 1000.
            _material.renderQueue = 1000;

            _ndcQuad = CreateNdcQuad();

            _go = new GameObject("Sky");
            _go.transform.SetParent(parent, false);

            var mf = _go.AddComponent<MeshFilter>();
            mf.sharedMesh = _ndcQuad;

            var mr = _go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            // The shader already skips MVP so view/projection culling must be
            // relaxed -- otherwise the quad's NDC-space bounds get culled.
            mr.allowOcclusionWhenDynamic = false;
        }

        public void SetSky(SkyDefinition sky)
        {
            if (sky == null)
            {
                SetEnabled(false);
                return;
            }
            SetEnabled(true);
            _material.SetColor(SkyColorId, sky.SkyColor);
            _material.SetColor(HorizonColorId, sky.HorizonColor);
            _material.SetFloat(SkyHorizonBlendId, Mathf.Clamp01(sky.SkyHorizonBlend));
        }

        public void SetEnabled(bool enabled)
        {
            if (_go != null) _go.SetActive(enabled);
        }

        public void Dispose()
        {
            if (_go != null) Object.Destroy(_go);
            if (_material != null) Object.Destroy(_material);
            if (_ndcQuad != null) Object.Destroy(_ndcQuad);
        }

        /// <summary>
        /// 4-vertex quad with positions already in NDC clip space. Bounds are
        /// forced to infinite-ish so Unity never frustum-culls it.
        /// </summary>
        private static Mesh CreateNdcQuad()
        {
            var mesh = new Mesh { name = "SkyNdcQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3( 1f, -1f, 0f),
                new Vector3(-1f,  1f, 0f),
                new Vector3( 1f,  1f, 0f),
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            // Huge bounds so the mesh is never culled regardless of camera pose.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            return mesh;
        }
    }
}
