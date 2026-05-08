using System.Collections;
using MapLibre.Unity.Style;
using UnityEngine;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates the ICustomLayer hook. A user-supplied layer renders three
    /// rotating Unity cubes pinned to Tokyo landmarks. The map owns the slot in
    /// render order (set via <c>beforeId</c>) and respects min/max zoom; the
    /// custom layer just runs Render() each frame to keep the cubes anchored
    /// to lng/lat as the camera moves.
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class CustomLayerDemo : MonoBehaviour
    {
        [Tooltip("Insert the cubes before this layer (renders below it). Leave blank to draw on top.")]
        [SerializeField] private string _insertBeforeLayerId = "";

        [Tooltip("Material applied to each cube (per-instance copy is made so colours stay independent). " +
                 "Required in Player builds: CreatePrimitive's Built-in \"Standard\" shader is stripped " +
                 "from URP/HDRP builds and renders magenta, and RenderPipelineAsset.defaultMaterial is " +
                 "null at runtime. The shipped CubeMaterial.mat references URP/Lit so its shader is " +
                 "included in the build.")]
        [SerializeField] private Material _cubeMaterial;

        private MapLibreMap _map;
        private TokyoLandmarksLayer _layer;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[CustomLayerDemo] MapLibreMap not found on this GameObject");
                yield break;
            }

            while (!_map.IsInitialized) yield return null;
            yield return null;

            _layer = new TokyoLandmarksLayer(_cubeMaterial);
            _map.AddLayer(_layer,
                string.IsNullOrEmpty(_insertBeforeLayerId) ? null : _insertBeforeLayerId);
        }

        private void OnDestroy()
        {
            // RemoveLayer also calls ICustomLayer.OnRemove; only call explicitly
            // if the demo wants to stop rendering before the GameObject dies.
            if (_map != null && _layer != null)
                _map.RemoveLayer(_layer.Id);
        }

        /// <summary>
        /// Custom layer that pins three Unity cubes to LngLat coordinates and
        /// spins them. Demonstrates the OnAdd/Render/OnRemove lifecycle and
        /// the LngLatToWorld → Transform pattern.
        /// </summary>
        private class TokyoLandmarksLayer : ICustomLayer
        {
            public string Id => "tokyo-landmarks";

            private readonly Material _baseMaterial;
            private GameObject _root;
            private (LngLat lngLat, Transform tf, Color colour)[] _items;

            public TokyoLandmarksLayer(Material baseMaterial)
            {
                _baseMaterial = baseMaterial;
            }

            public void OnAdd(MapLibreMap map)
            {
                _root = new GameObject("CustomLayer/Tokyo-Landmarks");
                _root.transform.SetParent(map.transform, worldPositionStays: false);

                _items = new (LngLat, Transform, Color)[]
                {
                    (new LngLat(139.7454, 35.6586), null, new Color(0.95f, 0.27f, 0.27f)), // Tokyo Tower
                    (new LngLat(139.8107, 35.7100), null, new Color(0.27f, 0.65f, 0.95f)), // Skytree
                    (new LngLat(139.7670, 35.6812), null, new Color(0.95f, 0.85f, 0.27f)), // Tokyo Station
                };

                if (_baseMaterial == null)
                {
                    Debug.LogWarning("[CustomLayerDemo] _cubeMaterial is not assigned. " +
                                     "Cubes will render with the Built-in Standard shader, which is " +
                                     "stripped from URP/HDRP Player builds and shows up magenta. " +
                                     "Assign CubeMaterial.mat (or any URP/HDRP-compatible material) " +
                                     "in the Inspector.");
                }

                for (int i = 0; i < _items.Length; i++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Landmark[{i}]";
                    go.transform.SetParent(_root.transform, worldPositionStays: false);

                    var renderer = go.GetComponent<MeshRenderer>();
                    if (renderer != null && _baseMaterial != null)
                    {
                        // Per-instance copy so each cube can hold its own colour
                        // without mutating the shared asset.
                        var mat = new Material(_baseMaterial);
                        mat.color = _items[i].colour;
                        renderer.material = mat;
                    }

                    // Fixed world-size footprint. The camera height shrinks
                    // with zoom (MapCamera.BaseCameraHeight / 2^zoom), so a
                    // 5×20×5 cube renders as ~4% of the viewport at z9 (visible
                    // but small) and ~35% at z12 (large landmark). This matches
                    // the expectation that zooming in makes things bigger.
                    go.transform.localScale = new Vector3(5f, 20f, 5f);

                    _items[i] = (_items[i].lngLat, go.transform, _items[i].colour);
                }
            }

            public void Render(MapLibreMap map, UnityEngine.Camera camera)
            {
                if (_items == null) return;
                float spinDeg = Time.time * 60f;
                for (int i = 0; i < _items.Length; i++)
                {
                    var pos = map.LngLatToWorld(_items[i].lngLat);
                    if (!pos.HasValue) continue;
                    var p = pos.Value;
                    // Lift the cube so its base sits on the ground rather than
                    // half-buried; localScale.y / 2 = 10.
                    p.y += 10f;
                    _items[i].tf.localPosition = p;
                    _items[i].tf.localRotation = Quaternion.Euler(0f, spinDeg, 0f);
                }
            }

            public void OnRemove(MapLibreMap map)
            {
                if (_root != null)
                {
                    Object.Destroy(_root);
                    _root = null;
                }
                _items = null;
            }
        }
    }
}
