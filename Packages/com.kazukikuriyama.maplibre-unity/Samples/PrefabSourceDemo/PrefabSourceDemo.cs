using System.Collections;
using MapLibre.Unity.Source;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates <see cref="PrefabSource"/> -- anchoring Unity prefabs to
    /// geographic coordinates. Spawns five prefabs at notable Tokyo landmarks
    /// (one of them billboarded to the camera) and drops a new prefab on every
    /// map click. Attach to the same GameObject as MapLibreMap.
    /// </summary>
    /// <remarks>
    /// Two ways to supply a prefab are demonstrated side-by-side:
    /// <list type="number">
    /// <item>
    /// <b>Inspector slot (recommended for production)</b> -- assign a
    /// <c>.prefab</c> asset to one of the <c>_*Prefab</c> fields below. When
    /// set, the slot's prefab is used as-is and its existing materials are
    /// preserved. The sample scene ships with <c>TowerPrefab</c> and
    /// <c>StationPrefab</c> wired into the Tokyo Tower and Tokyo Station
    /// slots so this path is exercised out of the box; the remaining
    /// landmarks fall through to the runtime primitive path below.
    /// </item>
    /// <item>
    /// <b>Runtime primitive (fallback)</b> -- leave a slot empty and the demo
    /// builds a <see cref="GameObject.CreatePrimitive"/>-based template at
    /// runtime, then re-colours it so the landmarks remain visually
    /// distinguishable. Keeps the sample self-contained when no prefab asset
    /// is supplied; using your own prefabs is the production-ready pattern.
    /// </item>
    /// </list>
    /// Scales are tuned for the project's coordinate system: at zoom 13, one
    /// Unity unit ≈ 19 metres on the ground, so landmarks are scaled to
    /// roughly 100–500 m so neighbouring sites do not overlap each other.
    ///
    /// <para>
    /// Pointer events are wired on every landmark -- hover scales the prefab
    /// up by 1.2×, click logs its name. Tokyo Station is also marked
    /// <see cref="PrefabInstance.Draggable"/> so you can pick it up and drop
    /// it elsewhere on the map. Clicks that land on a landmark suppress the
    /// map's own click event, so the click-to-drop handler below only fires
    /// when you click empty terrain.
    /// </para>
    /// </remarks>
    public class PrefabSourceDemo : MonoBehaviour
    {
        private const string SourceId = "landmarks";

        [Header("Optional custom prefabs")]
        [Tooltip("Assign your own prefab to override the runtime-generated primitive. " +
                 "Custom prefabs keep their own materials; primitives are auto-coloured.")]
        [SerializeField] private GameObject _towerPrefab;        // Tokyo Tower fallback: cylinder
        [SerializeField] private GameObject _stationPrefab;      // Tokyo Station fallback: cube
        [SerializeField] private GameObject _templePrefab;       // Senso-ji fallback: capsule
        [SerializeField] private GameObject _intersectionPrefab; // Shibuya fallback: sphere
        [SerializeField] private GameObject _palacePrefab;       // Imperial Palace fallback: quad
        [SerializeField] private GameObject _clickDropPrefab;    // Click-drop fallback: cube

        private MapLibreMap _map;
        private PrefabSource _source;

        // Container for runtime-built primitive templates. Kept inactive so
        // the templates themselves never render -- only their clones do.
        private Transform _templateContainer;
        private GameObject _resolvedTower;
        private GameObject _resolvedStation;
        private GameObject _resolvedTemple;
        private GameObject _resolvedIntersection;
        private GameObject _resolvedPalace;
        private GameObject _resolvedClickDrop;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[PrefabSourceDemo] MapLibreMap component not found");
                yield break;
            }

            while (!_map.IsInitialized) yield return null;

            ResolvePrefabs();
            _source = _map.AddPrefabSource(SourceId);

            // 1. Tokyo Tower -- tall thin cylinder.
            var tokyoTower = _source.Add(_resolvedTower, new LngLat(139.7454, 35.6586),
                new PrefabPlacementOptions
                {
                    LocalScale = new Vector3(3f, 16f, 3f),
                    HeightOffset = 16f,
                }, instanceId: "tokyo-tower");
            ApplyFallbackColor(tokyoTower, _towerPrefab, new Color(0.9f, 0.25f, 0.2f));
            WirePointerEvents(tokyoTower, "Tokyo Tower");

            // 2. Tokyo Station -- wide low cube. Draggable so the user can
            // pick it up and drop it anywhere on the map.
            var station = _source.Add(_resolvedStation, new LngLat(139.7671, 35.6812),
                new PrefabPlacementOptions
                {
                    LocalScale = new Vector3(10f, 3f, 6f),
                    HeightOffset = 1.5f,
                }, instanceId: "tokyo-station");
            station.Draggable = true;
            ApplyFallbackColor(station, _stationPrefab, new Color(0.2f, 0.45f, 0.9f));
            WirePointerEvents(station, "Tokyo Station (drag me!)");
            station.OnDragEnd += (inst, ll) =>
                Debug.Log($"[PrefabSourceDemo] {inst.Id} dropped at {ll.Longitude:F4}, {ll.Latitude:F4}");

            // 3. Senso-ji -- capsule landmark.
            var sensoji = _source.Add(_resolvedTemple, new LngLat(139.7966, 35.7148),
                new PrefabPlacementOptions
                {
                    LocalScale = new Vector3(5f, 5f, 5f),
                    HeightOffset = 5f,
                }, instanceId: "sensoji");
            ApplyFallbackColor(sensoji, _templePrefab, new Color(0.25f, 0.7f, 0.3f));
            WirePointerEvents(sensoji, "Senso-ji");

            // 4. Shibuya Crossing -- sphere hovering over the intersection.
            var shibuya = _source.Add(_resolvedIntersection, new LngLat(139.7005, 35.6594),
                new PrefabPlacementOptions
                {
                    LocalScale = Vector3.one * 7f,
                    HeightOffset = 8f,
                }, instanceId: "shibuya");
            ApplyFallbackColor(shibuya, _intersectionPrefab, new Color(1f, 0.55f, 0.1f));
            WirePointerEvents(shibuya, "Shibuya Crossing");

            // 5. Imperial Palace -- billboarded quad that always faces the camera.
            var palace = _source.Add(_resolvedPalace, new LngLat(139.7528, 35.6852),
                new PrefabPlacementOptions
                {
                    LocalScale = new Vector3(10f, 10f, 1f),
                    HeightOffset = 10f,
                    BillboardYAxis = true,
                }, instanceId: "imperial-palace");
            ApplyFallbackColor(palace, _palacePrefab, new Color(0.85f, 0.75f, 0.1f));
            WirePointerEvents(palace, "Imperial Palace");

            _map.On(MapEventType.Click, OnMapClick);

            Debug.Log("[PrefabSourceDemo] Ready -- hover / click landmarks, drag Tokyo Station, click empty map to drop a cube");
        }

        /// <summary>
        /// Hover scales the prefab up by 1.2×, click logs the human-readable
        /// name. Cached <see cref="PrefabInstance.LocalScale"/> is captured at
        /// wiring time so the hover restore returns to the authored value
        /// rather than a hard-coded one.
        /// </summary>
        private static void WirePointerEvents(PrefabInstance instance, string displayName)
        {
            if (instance == null) return;
            Vector3 baseScale = instance.LocalScale;
            instance.OnPointerEnter += (inst, _) => inst.LocalScale = baseScale * 1.2f;
            instance.OnPointerExit += (inst, _) => inst.LocalScale = baseScale;
            instance.OnClick += (inst, pos) =>
                Debug.Log($"[PrefabSourceDemo] Clicked {displayName} ({inst.Id}) at screen {pos}");
        }

        /// <summary>
        /// For each landmark slot, choose the user-supplied prefab when set,
        /// or build a primitive template at runtime as fallback. Templates
        /// live under a deactivated container so they never render directly.
        /// </summary>
        private void ResolvePrefabs()
        {
            var container = new GameObject("PrefabTemplates");
            container.transform.SetParent(transform, worldPositionStays: false);
            container.SetActive(false);
            _templateContainer = container.transform;

            _resolvedTower = _towerPrefab != null
                ? _towerPrefab : MakePrimitiveTemplate(PrimitiveType.Cylinder, "TowerTemplate");
            _resolvedStation = _stationPrefab != null
                ? _stationPrefab : MakePrimitiveTemplate(PrimitiveType.Cube, "StationTemplate");
            _resolvedTemple = _templePrefab != null
                ? _templePrefab : MakePrimitiveTemplate(PrimitiveType.Capsule, "TempleTemplate");
            _resolvedIntersection = _intersectionPrefab != null
                ? _intersectionPrefab : MakePrimitiveTemplate(PrimitiveType.Sphere, "IntersectionTemplate");
            _resolvedPalace = _palacePrefab != null
                ? _palacePrefab : MakePrimitiveTemplate(PrimitiveType.Quad, "PalaceTemplate");
            _resolvedClickDrop = _clickDropPrefab != null
                ? _clickDropPrefab : MakePrimitiveTemplate(PrimitiveType.Cube, "ClickDropTemplate");
        }

        private GameObject MakePrimitiveTemplate(PrimitiveType type, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            // CreatePrimitive assigns the Built-in RP "Standard" shader, which
            // is stripped from URP/HDRP builds and renders as the magenta error
            // shader. Swap to a pipeline-compatible material so the sample
            // looks identical in Editor and in standalone builds.
            EnsurePipelineCompatibleMaterial(go);
            // Keep the auto-generated collider so the prefab can receive
            // pointer events. The map's own click event is still suppressed
            // for clicks that hit a prefab -- see PrefabConsumedClickThisFrame
            // in MapLibreMap.PrefabSources -- so empty-map clicks still work.
            go.transform.SetParent(_templateContainer, worldPositionStays: false);
            return go;
        }

        private void EnsurePipelineCompatibleMaterial(GameObject go)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null) return; // Built-in RP -- Standard shader is fine

            var fallback = ResolveFallbackMaterial(pipeline);
            if (fallback == null) return;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = fallback;
        }

        // URP 17.3 / HDRP return null from defaultMaterial in Player builds
        // (`UniversalRenderPipelineAsset.DefaultResources.cs` returns null
        // outside UNITY_EDITOR), so the editor-only path renders Standard /
        // magenta in builds. Borrow a sharedMaterial from any user-assigned
        // prefab -- those materials are reachable from the scene graph and are
        // therefore included in the build alongside their shader.
        private Material ResolveFallbackMaterial(RenderPipelineAsset pipeline)
        {
            var pipelineMaterial = pipeline.defaultMaterial;
            if (pipelineMaterial != null) return pipelineMaterial;

            foreach (var prefab in new[]
            {
                _towerPrefab, _stationPrefab, _templePrefab,
                _intersectionPrefab, _palacePrefab, _clickDropPrefab,
            })
            {
                if (prefab == null) continue;
                var prefabRenderer = prefab.GetComponentInChildren<Renderer>(true);
                if (prefabRenderer != null && prefabRenderer.sharedMaterial != null)
                    return prefabRenderer.sharedMaterial;
            }
            return null;
        }

        /// <summary>
        /// Re-colour a primitive instance so each landmark is visually
        /// distinct. Skipped when the user supplied a custom prefab -- those
        /// keep their authored materials untouched.
        /// </summary>
        private static void ApplyFallbackColor(PrefabInstance instance,
            GameObject userPrefab, Color color)
        {
            if (instance == null || userPrefab != null) return;
            var renderer = instance.GameObject != null
                ? instance.GameObject.GetComponentInChildren<Renderer>() : null;
            if (renderer == null) return;
            renderer.material.color = color;
        }

        private void OnMapClick(MapEvent e)
        {
            if (!e.LngLat.HasValue || _source == null) return;

            var clone = _source.Add(_resolvedClickDrop, e.LngLat.Value,
                new PrefabPlacementOptions
                {
                    LocalScale = Vector3.one * 3f,
                    HeightOffset = 1.5f,
                });
            ApplyFallbackColor(clone, _clickDropPrefab,
                Random.ColorHSV(0f, 1f, 0.55f, 0.85f, 0.7f, 1f));
        }

        private void OnDestroy()
        {
            if (_map != null)
            {
                _map.Off(MapEventType.Click, OnMapClick);
                if (_map.GetPrefabSource(SourceId) != null)
                    _map.RemovePrefabSource(SourceId);
            }
            if (_templateContainer != null) Destroy(_templateContainer.gameObject);
        }
    }
}
