using System.Collections.Generic;
using MapLibre.Unity.Source;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MapLibre.Unity
{
    /// <summary>
    /// Unity-only PrefabSource surface -- AddPrefabSource / GetPrefabSource /
    /// RemovePrefabSource and the per-frame world-position update hook.
    /// PrefabSource lets callers anchor arbitrary GameObjects (3D models, FX,
    /// agents, ...) to geographic coordinates. It is intentionally not part of
    /// the MapLibre Style Spec because the spec has no concept of native
    /// scene-graph instances.
    /// </summary>
    public partial class MapLibreMap
    {
        private readonly Dictionary<string, PrefabSource> _prefabSources = new();

        /// <summary>
        /// Register a new <see cref="PrefabSource"/> with the map and return it
        /// so the caller can add prefab instances. The source's container
        /// GameObject is parented under the map for clean hierarchy.
        /// </summary>
        public PrefabSource AddPrefabSource(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError("[MapLibreMap] AddPrefabSource: id is null or empty");
                return null;
            }
            if (_prefabSources.ContainsKey(id))
            {
                Debug.LogWarning($"[MapLibreMap] AddPrefabSource: source '{id}' already exists");
                return _prefabSources[id];
            }

            var source = new PrefabSource(id, transform);
            _prefabSources[id] = source;
            return source;
        }

        /// <summary>Look up a prefab source by id. Returns null if not found.</summary>
        public PrefabSource GetPrefabSource(string id)
            => _prefabSources.TryGetValue(id, out var source) ? source : null;

        /// <summary>Tear down a prefab source and destroy every instance it owns.</summary>
        public bool RemovePrefabSource(string id)
        {
            if (!_prefabSources.TryGetValue(id, out var source))
                return false;
            _prefabSources.Remove(id);
            source.Dispose();
            return true;
        }

        /// <summary>Enumerate every registered prefab source.</summary>
        public IEnumerable<PrefabSource> PrefabSources => _prefabSources.Values;

        /// <summary>
        /// Refresh world positions of every <see cref="PrefabInstance"/>. Called
        /// from <see cref="MapLibreMap"/>'s Update so pan / zoom / rotate keep
        /// instances glued to their LngLat.
        /// </summary>
        private void UpdatePrefabSources()
        {
            if (_prefabSources.Count == 0 || State == null) return;

            var centerMerc = CoordinateConversion.LngLatToMercator(State.Center);
            float worldScale = CoordinateConversion.GetWorldScale(State.Zoom);
            foreach (var source in _prefabSources.Values)
                source.UpdatePositions(centerMerc, worldScale, _mapCamera);
        }

        private void DisposePrefabSources()
        {
            foreach (var source in _prefabSources.Values)
                source.Dispose();
            _prefabSources.Clear();
        }

        // === Pointer dispatch ===========================================
        // Map-level state for prefab pointer events. Updated each frame by
        // UpdatePrefabPointer alongside the existing HandlePointerEvents
        // and consumed there to suppress map-level click when a prefab was
        // clicked in the same frame.

        private PrefabInstance _hoveredPrefab;
        private PrefabInstance _pressedPrefab;
        private Vector2 _pressedPrefabPos;
        private PrefabInstance _draggingPrefab;
        private bool _prefabConsumedClickThisFrame;

        private const float PrefabClickMaxDistance = 5f;
        private const float PrefabDragStartThreshold = 3f;

        /// <summary>
        /// Hit-test every <see cref="PrefabInstance"/> hierarchy under the
        /// cursor via <see cref="Physics.Raycast"/>. The prefab must include a
        /// <see cref="Collider"/> (any kind). Returns null if nothing under
        /// the cursor belongs to a registered <see cref="PrefabSource"/>.
        /// </summary>
        public PrefabInstance QueryPrefabAt(Vector2 screenPos)
        {
            if (_mapCamera == null || _prefabSources.Count == 0) return null;
            var ray = _mapCamera.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0));
            if (!Physics.Raycast(ray, out var hit, _mapCamera.farClipPlane)) return null;

            var marker = hit.collider != null
                ? hit.collider.GetComponentInParent<PrefabInstanceMarker>() : null;
            if (marker == null || marker.Instance == null) return null;
            return marker.Instance.RaycastEnabled ? marker.Instance : null;
        }

        /// <summary>
        /// True when the cursor is over a draggable <see cref="PrefabInstance"/>
        /// or a drag is currently in progress. Composed into the
        /// <see cref="MapLibre.Unity.CameraControl.MapInputHandler"/>'s
        /// pointer-over-UI predicate so the camera doesn't fight the user
        /// while a prefab drag is active.
        /// </summary>
        public bool IsPointerOverDraggablePrefab(Vector2 screenPos)
        {
            if (_draggingPrefab != null) return true;
            var hit = QueryPrefabAt(screenPos);
            return hit != null && hit.Draggable;
        }

        /// <summary>
        /// True when a prefab consumed the left-click this frame. Read by the
        /// map's pointer event handler to suppress its own
        /// <see cref="MapEventType.Click"/> so a single click cannot fire on
        /// both the prefab and the map.
        /// </summary>
        internal bool PrefabConsumedClickThisFrame => _prefabConsumedClickThisFrame;

        /// <summary>
        /// Per-frame prefab pointer dispatch: hover transitions, click,
        /// drag start / drag / drag end. Runs immediately before
        /// <see cref="HandlePointerEvents"/> so the click-suppression flag is
        /// visible to the map-level click logic.
        /// </summary>
        private void UpdatePrefabPointer()
        {
            _prefabConsumedClickThisFrame = false;
            if (_prefabSources.Count == 0 || _mapCamera == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 pos = mouse.position.ReadValue();
            bool overUI = IsPointerOverOverlayUI(pos);

            // Hit-test once per frame and reuse the result everywhere below.
            // While a drag is active the dragged instance always wins so the
            // pointer can leave its silhouette without losing the drag.
            var hit = _draggingPrefab ?? QueryPrefabAt(pos);

            // --- Hover transitions ---
            if (hit != _hoveredPrefab)
            {
                _hoveredPrefab?.FirePointerExit(pos);
                _hoveredPrefab = hit;
                _hoveredPrefab?.FirePointerEnter(pos);
            }

            // --- Mouse down (left) → arm a press / drag candidate ---
            if (mouse.leftButton.wasPressedThisFrame && !overUI)
            {
                _pressedPrefab = hit;
                _pressedPrefabPos = pos;
            }

            // --- Drag start once the cursor crosses the threshold ---
            if (_pressedPrefab != null && _pressedPrefab.Draggable &&
                _draggingPrefab == null &&
                Vector2.Distance(_pressedPrefabPos, pos) > PrefabDragStartThreshold)
            {
                _draggingPrefab = _pressedPrefab;
                _draggingPrefab.FireDragStart(pos);
            }

            // --- Drag move: project pointer to ground LngLat and follow ---
            if (_draggingPrefab != null && mouse.delta.ReadValue().sqrMagnitude > 0.01f)
            {
                var ll = Unproject(pos);
                if (ll.HasValue)
                {
                    _draggingPrefab.LngLat = ll.Value;
                    _draggingPrefab.FireDrag(ll.Value);
                }
            }

            // --- Mouse up (left): finalise drag or fire click ---
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (_draggingPrefab != null)
                {
                    var ll = Unproject(pos) ?? _draggingPrefab.LngLat;
                    _draggingPrefab.FireDragEnd(ll);
                    _draggingPrefab = null;
                    _prefabConsumedClickThisFrame = true; // drag swallows the click
                }
                else if (_pressedPrefab != null && _pressedPrefab == hit &&
                         Vector2.Distance(_pressedPrefabPos, pos) <= PrefabClickMaxDistance)
                {
                    _pressedPrefab.FireClick(pos);
                    _prefabConsumedClickThisFrame = true;
                }
                _pressedPrefab = null;
            }
        }
    }
}
