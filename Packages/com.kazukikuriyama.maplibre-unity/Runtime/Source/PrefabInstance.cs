using System;
using UnityEngine;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// A single prefab spawned by a <see cref="PrefabSource"/>. Holds the desired
    /// geographic position plus per-instance offsets and exposes the instantiated
    /// <see cref="GameObject"/> so callers can attach components, listen for
    /// pointer events, or animate the prefab freely.
    /// </summary>
    /// <remarks>
    /// The transform is recomputed every frame from <see cref="LngLat"/>, the
    /// optional height offset, and the map's current state. Mutating the
    /// instantiated GameObject's local position directly is therefore not
    /// persistent -- adjust <see cref="LngLat"/> or <see cref="HeightOffset"/>
    /// instead, or assign a child object to absorb your transform tweaks.
    /// </remarks>
    public class PrefabInstance
    {
        /// <summary>Stable identifier within the owning source.</summary>
        public string Id { get; }

        /// <summary>The instantiated GameObject. May be null after Destroy().</summary>
        public GameObject GameObject { get; private set; }

        /// <summary>Cached transform of the instantiated GameObject.</summary>
        public Transform Transform => GameObject != null ? GameObject.transform : null;

        /// <summary>Geographic position of the instance.</summary>
        public LngLat LngLat { get; set; }

        /// <summary>
        /// Y offset in Unity world units, applied after the geographic projection.
        /// Use this to lift the prefab off the map plane (e.g. for a hovering
        /// drone) or to bury it slightly to avoid z-fighting with raster tiles.
        /// </summary>
        public float HeightOffset { get; set; }

        /// <summary>
        /// Rotation of the prefab in map space. The map's +Z axis points
        /// geographic north, so a Y rotation orients the asset to face a
        /// specific compass direction. Identity preserves authoring orientation.
        /// </summary>
        public Quaternion LocalRotation { get; set; } = Quaternion.identity;

        /// <summary>
        /// Local scale of the instance. Defaults to <see cref="Vector3.one"/>.
        /// </summary>
        public Vector3 LocalScale { get; set; } = Vector3.one;

        /// <summary>
        /// When true, the instance is rotated each frame so its forward axis
        /// faces the camera around its Y axis (billboard).
        /// </summary>
        public bool BillboardYAxis { get; set; }

        /// <summary>
        /// When false, the instance is excluded from
        /// <see cref="MapLibreMap.QueryPrefabAt"/> hit-testing even when it
        /// has a collider. Use to keep a prefab visible but click-through.
        /// Default true.
        /// </summary>
        public bool RaycastEnabled { get; set; } = true;

        /// <summary>
        /// When true, pressing the instance and dragging the pointer moves
        /// it across the map: each frame the instance's <see cref="LngLat"/>
        /// is updated to the unprojected pointer position and
        /// <see cref="OnDrag"/> fires. Camera pan is suppressed for the
        /// duration of the drag. Requires a <see cref="Collider"/> in the
        /// prefab hierarchy. Default false.
        /// </summary>
        public bool Draggable { get; set; }

        // === Pointer events ===
        // Map fires these from MapLibreMap's per-frame pointer dispatch, so
        // they always run on the main thread and can safely touch Unity APIs.

        /// <summary>Fires the first frame the cursor enters the instance's geometry.</summary>
        public event Action<PrefabInstance, Vector2> OnPointerEnter;

        /// <summary>Fires the first frame the cursor leaves the instance's geometry.</summary>
        public event Action<PrefabInstance, Vector2> OnPointerExit;

        /// <summary>
        /// Fires on left-button release if the press and release both landed
        /// on this instance and the pointer didn't travel past the click
        /// threshold (matches the existing <see cref="MapEventType.Click"/>
        /// semantics). The map's own <see cref="MapEventType.Click"/> is
        /// suppressed for the same frame so a single click cannot fire both.
        /// </summary>
        public event Action<PrefabInstance, Vector2> OnClick;

        /// <summary>Fires once per drag, the moment the pointer crosses the drag-start threshold.</summary>
        public event Action<PrefabInstance, Vector2> OnDragStart;

        /// <summary>Fires every frame the pointer moves while a drag is in progress, with the new <see cref="LngLat"/>.</summary>
        public event Action<PrefabInstance, LngLat> OnDrag;

        /// <summary>Fires on pointer release when a drag was in progress, with the final <see cref="LngLat"/>.</summary>
        public event Action<PrefabInstance, LngLat> OnDragEnd;

        internal void FirePointerEnter(Vector2 pos) => OnPointerEnter?.Invoke(this, pos);
        internal void FirePointerExit(Vector2 pos) => OnPointerExit?.Invoke(this, pos);
        internal void FireClick(Vector2 pos) => OnClick?.Invoke(this, pos);
        internal void FireDragStart(Vector2 pos) => OnDragStart?.Invoke(this, pos);
        internal void FireDrag(LngLat lngLat) => OnDrag?.Invoke(this, lngLat);
        internal void FireDragEnd(LngLat lngLat) => OnDragEnd?.Invoke(this, lngLat);

        internal PrefabInstance(string id, GameObject gameObject, LngLat lngLat,
            PrefabPlacementOptions options)
        {
            Id = id;
            GameObject = gameObject;
            LngLat = lngLat;
            HeightOffset = options.HeightOffset;
            if (options.LocalRotation.HasValue) LocalRotation = options.LocalRotation.Value;
            if (options.LocalScale.HasValue) LocalScale = options.LocalScale.Value;
            BillboardYAxis = options.BillboardYAxis;
        }

        internal void UpdateTransform(MercatorCoordinate centerMerc, float worldScale,
            Camera camera)
        {
            if (GameObject == null) return;

            var merc = CoordinateConversion.LngLatToMercator(LngLat);
            var worldPos = CoordinateConversion.MercatorToUnityWorld(merc, centerMerc, worldScale);
            worldPos.y += HeightOffset;

            var t = GameObject.transform;
            t.localPosition = worldPos;
            t.localScale = LocalScale;

            if (BillboardYAxis && camera != null)
            {
                Vector3 toCam = camera.transform.position - t.position;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 1e-6f)
                {
                    var faceCam = Quaternion.LookRotation(toCam.normalized, Vector3.up);
                    t.localRotation = faceCam * LocalRotation;
                }
                else
                {
                    t.localRotation = LocalRotation;
                }
            }
            else
            {
                t.localRotation = LocalRotation;
            }
        }

        internal void Destroy()
        {
            if (GameObject != null)
            {
                UnityEngine.Object.Destroy(GameObject);
                GameObject = null;
            }
        }
    }
}
