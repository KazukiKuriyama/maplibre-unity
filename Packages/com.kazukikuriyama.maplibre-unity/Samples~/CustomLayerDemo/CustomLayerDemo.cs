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

            _layer = new TokyoLandmarksLayer();
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

            private GameObject _root;
            private (LngLat lngLat, Transform tf, Color colour)[] _items;

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

                for (int i = 0; i < _items.Length; i++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Landmark[{i}]";
                    go.transform.SetParent(_root.transform, worldPositionStays: false);
                    // Tile-local units are roughly [-0.5, 0.5] across the visible
                    // viewport — keep the cube small so it sits on the surface
                    // without dwarfing the map.
                    go.transform.localScale = new Vector3(0.0015f, 0.04f, 0.0015f);

                    var renderer = go.GetComponent<MeshRenderer>();
                    if (renderer != null && renderer.sharedMaterial != null)
                    {
                        // Use a per-instance material so we can colour each cube.
                        // Avoid mutating sharedMaterial which would leak across cubes.
                        var mat = new Material(renderer.sharedMaterial);
                        mat.color = _items[i].colour;
                        renderer.material = mat;
                    }

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
                    // Lift the cube so its base sits on the ground rather than
                    // half-buried; localScale.y / 2 = 0.02.
                    var p = pos.Value;
                    p.y += 0.02f;
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
