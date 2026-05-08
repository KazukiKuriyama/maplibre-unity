using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// Unity-specific source that anchors arbitrary <see cref="GameObject"/> prefabs
    /// at geographic coordinates. Each prefab is instantiated as a child of the
    /// map and its Unity world position is updated every frame so that pan / zoom /
    /// rotate / pitch keep the model glued to the map.
    /// </summary>
    /// <remarks>
    /// PrefabSource is a Unity-only extension and is intentionally not part of the
    /// MapLibre Style Spec. It does not implement <see cref="ISource"/> because
    /// there are no tile / layer concepts involved -- the source owns
    /// <see cref="PrefabInstance"/> records directly. Use it for placing 3D
    /// landmarks, vehicles, agents, or any prefab whose pivot should follow a
    /// real-world coordinate.
    ///
    /// <para>
    /// <b>ParticleSystem / VFX Graph</b>: any prefab with a
    /// <c>ParticleSystem</c> or <c>VisualEffect</c> component works without
    /// extra setup -- the GameObject is parented under the source's container
    /// and follows pan / zoom like any other prefab. Use
    /// <c>ParticleSystemSimulationSpace.Local</c> so particles stay anchored
    /// when the user pans. See the <c>ParticleSourceDemo</c> sample for the
    /// recommended pattern.
    /// </para>
    /// </remarks>
    public class PrefabSource
    {
        /// <summary>Unique identifier of this source as registered with the map.</summary>
        public string Id { get; }

        /// <summary>
        /// Hierarchy container for every <see cref="PrefabInstance"/> spawned from this
        /// source. Parented to the map root so map-relative coordinates work. Disabling
        /// it hides every instance at once.
        /// </summary>
        public Transform Container { get; }

        private readonly Dictionary<string, PrefabInstance> _instances = new();
        private int _autoIdCounter;

        internal PrefabSource(string id, Transform mapTransform)
        {
            Id = id;
            var go = new GameObject($"PrefabSource_{id}");
            go.transform.SetParent(mapTransform, worldPositionStays: false);
            Container = go.transform;
        }

        /// <summary>
        /// Spawn <paramref name="prefab"/> at <paramref name="lngLat"/> and return a
        /// handle that lets you reposition / restyle / remove the instance later.
        /// </summary>
        /// <param name="prefab">Prefab asset or runtime template. Required.</param>
        /// <param name="lngLat">Geographic position of the instance.</param>
        /// <param name="options">Optional placement parameters (height, rotation, scale, ...).</param>
        /// <param name="instanceId">
        /// Optional stable identifier. When omitted an auto-generated id is used.
        /// </param>
        public PrefabInstance Add(GameObject prefab, LngLat lngLat,
            PrefabPlacementOptions options = default, string instanceId = null)
        {
            if (prefab == null)
            {
                Debug.LogError($"[PrefabSource] '{Id}' Add: prefab is null");
                return null;
            }

            string id = instanceId ?? $"_auto_{_autoIdCounter++}";
            if (_instances.ContainsKey(id))
            {
                Debug.LogWarning($"[PrefabSource] '{Id}' Add: instance '{id}' already exists");
                return _instances[id];
            }

            var go = Object.Instantiate(prefab, Container);
            go.name = $"{prefab.name} ({id})";

            var instance = new PrefabInstance(id, go, lngLat, options);
            // Marker lets a Physics.Raycast hit walk back to the owning
            // PrefabInstance via GetComponentInParent -- needed for click /
            // hover / drag dispatch from MapLibreMap.
            var marker = go.AddComponent<PrefabInstanceMarker>();
            marker.Instance = instance;
            _instances[id] = instance;
            return instance;
        }

        /// <summary>Remove an instance by id and destroy its GameObject.</summary>
        public bool Remove(string instanceId)
        {
            if (!_instances.TryGetValue(instanceId, out var instance))
                return false;
            _instances.Remove(instanceId);
            instance.Destroy();
            return true;
        }

        /// <summary>Look up an instance by id. Returns null if not found.</summary>
        public PrefabInstance Get(string instanceId)
            => _instances.TryGetValue(instanceId, out var instance) ? instance : null;

        /// <summary>Enumerate every instance owned by this source.</summary>
        public IEnumerable<PrefabInstance> Instances => _instances.Values;

        /// <summary>Number of live instances.</summary>
        public int Count => _instances.Count;

        /// <summary>Remove and destroy every instance.</summary>
        public void Clear()
        {
            foreach (var instance in _instances.Values)
                instance.Destroy();
            _instances.Clear();
        }

        /// <summary>
        /// Refresh the Unity world position of every instance against the current
        /// map state. Called by <see cref="MapLibreMap"/> every frame.
        /// </summary>
        internal void UpdatePositions(MercatorCoordinate centerMerc, float worldScale,
            Camera camera)
        {
            foreach (var instance in _instances.Values)
                instance.UpdateTransform(centerMerc, worldScale, camera);
        }

        /// <summary>Tear down the source and its hierarchy container.</summary>
        internal void Dispose()
        {
            Clear();
            if (Container != null)
                Object.Destroy(Container.gameObject);
        }
    }

    /// <summary>
    /// Optional parameters used when adding a prefab to a <see cref="PrefabSource"/>.
    /// Default values mean "lay flat on the ground, no extra rotation / scale".
    /// </summary>
    public struct PrefabPlacementOptions
    {
        /// <summary>
        /// Height above the map ground plane, in Unity world units. Defaults to 0
        /// so the prefab sits on Y=0. This is *not* metres of altitude -- it is
        /// applied directly in Unity-space and therefore scales with the map.
        /// </summary>
        public float HeightOffset;

        /// <summary>
        /// Rotation of the prefab in map space. Identity means the prefab keeps
        /// its authoring orientation; the map's +Z axis points geographic north,
        /// so a Y rotation here lets you orient an asset to face a specific
        /// compass direction. Defaults to identity.
        /// </summary>
        public Quaternion? LocalRotation;

        /// <summary>
        /// Scale applied to the instance. Defaults to <see cref="Vector3.one"/>.
        /// Useful for sizing landmarks consistently regardless of prefab scale.
        /// </summary>
        public Vector3? LocalScale;

        /// <summary>
        /// When true, the instance is rotated each frame so that its forward
        /// axis faces the camera around its Y axis (billboard). Useful for
        /// sprite-like prefabs and labels that should always face the viewer.
        /// </summary>
        public bool BillboardYAxis;
    }
}
