using System.Collections;
using MapLibre.Unity.Source;
using UnityEngine;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates that <see cref="PrefabSource"/> handles ParticleSystem
    /// prefabs natively -- no extra source type needed. Spawns a smoke column
    /// at Tokyo Tower, a sparkle ring over Imperial Palace, and a brief
    /// burst on every map click.
    /// </summary>
    /// <remarks>
    /// Both the Inspector slot pattern (assign your own <c>.prefab</c>) and a
    /// runtime-built fallback are shown. The fallback uses
    /// <c>Shader.Find("Universal Render Pipeline/Particles/Unlit")</c> so the
    /// sample works in URP without an asset import; if the project uses the
    /// built-in render pipeline, swap in <c>"Particles/Standard Unlit"</c> or
    /// any particle-friendly shader you have.
    /// <para>
    /// Particle systems use <see cref="ParticleSystemSimulationSpace.Local"/>
    /// so the particles travel with the source GameObject as
    /// <see cref="PrefabSource"/> updates its world position every frame.
    /// World-space simulation also works -- particles will stay where they
    /// were emitted as the user pans the map.
    /// </para>
    /// </remarks>
    public class ParticleSourceDemo : MonoBehaviour
    {
        private const string SourceId = "particle-effects";

        [Header("Optional custom prefabs")]
        [Tooltip("Assign a ParticleSystem-bearing prefab to override the runtime fallback.")]
        [SerializeField] private GameObject _smokePrefab;
        [SerializeField] private GameObject _sparklePrefab;
        [SerializeField] private GameObject _burstPrefab;

        private MapLibreMap _map;
        private PrefabSource _source;
        private Transform _templateContainer;
        private GameObject _resolvedSmoke;
        private GameObject _resolvedSparkle;
        private GameObject _resolvedBurst;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[ParticleSourceDemo] MapLibreMap component not found");
                yield break;
            }

            while (!_map.IsInitialized) yield return null;

            ResolvePrefabs();
            _source = _map.AddPrefabSource(SourceId);

            // 1. Tokyo Tower -- slow grey smoke column.
            var smoke = _source.Add(_resolvedSmoke, new LngLat(139.7454, 35.6586),
                new PrefabPlacementOptions
                {
                    LocalScale = Vector3.one * 5f,
                    HeightOffset = 0f,
                }, instanceId: "tower-smoke");
            smoke.GameObject.SetActive(true);

            // 2. Imperial Palace -- golden sparkles puffing upward in a ring.
            var sparkle = _source.Add(_resolvedSparkle, new LngLat(139.7528, 35.6852),
                new PrefabPlacementOptions
                {
                    LocalScale = Vector3.one * 4f,
                    HeightOffset = 1f,
                }, instanceId: "palace-sparkle");
            sparkle.GameObject.SetActive(true);

            // 3. Click anywhere to drop a one-shot burst that auto-cleans up.
            _map.On(MapEventType.Click, OnMapClick);

            Debug.Log("[ParticleSourceDemo] Ready -- click the map to drop a one-shot burst");
        }

        private void ResolvePrefabs()
        {
            var container = new GameObject("ParticleTemplates");
            container.transform.SetParent(transform, worldPositionStays: false);
            container.SetActive(false);
            _templateContainer = container.transform;

            _resolvedSmoke = _smokePrefab != null
                ? _smokePrefab : BuildSmokeTemplate();
            _resolvedSparkle = _sparklePrefab != null
                ? _sparklePrefab : BuildSparkleTemplate();
            _resolvedBurst = _burstPrefab != null
                ? _burstPrefab : BuildBurstTemplate();
        }

        // ===== Runtime ParticleSystem builders =====
        // Each helper returns a parented inactive GameObject the demo clones
        // via PrefabSource.Add. Settings are tuned for the project's zoom-13
        // coordinate scale (1 unit ≈ 19 m).

        private GameObject BuildSmokeTemplate()
        {
            var go = new GameObject("SmokeTemplate");
            go.transform.SetParent(_templateContainer, worldPositionStays: false);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = 4f;
            main.startSpeed = 1.5f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.6f, 0.6f, 0.6f, 0.8f),
                new Color(0.85f, 0.85f, 0.85f, 0.6f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 200;

            var emission = ps.emission;
            emission.rateOverTime = 18f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.3f;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.6f));

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            colorOverLife.color = new ParticleSystem.MinMaxGradient(MakeFadeOutGradient());

            DisableOffscreenPause(ps);
            ApplyParticleMaterial(ps);
            return go;
        }

        private GameObject BuildSparkleTemplate()
        {
            var go = new GameObject("SparkleTemplate");
            go.transform.SetParent(_templateContainer, worldPositionStays: false);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.duration = 3f;
            main.loop = true;
            main.startLifetime = 1.5f;
            main.startSpeed = 3f;
            main.startSize = 0.25f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.85f, 0.2f),
                new Color(1f, 0.7f, 0.1f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 300;

            var emission = ps.emission;
            emission.rateOverTime = 60f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1.5f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            // All three axes must share the same MinMaxCurve mode or Unity
            // logs "Particle Velocity curves must all be in the same mode"
            // every frame. Set x and z explicitly with two-constant mode to
            // match y so the validation passes.
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(2f, 4f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            colorOverLife.color = new ParticleSystem.MinMaxGradient(MakeFadeOutGradient());

            DisableOffscreenPause(ps);
            ApplyParticleMaterial(ps);
            return go;
        }

        private GameObject BuildBurstTemplate()
        {
            var go = new GameObject("BurstTemplate");
            go.transform.SetParent(_templateContainer, worldPositionStays: false);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.startLifetime = 1.2f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.4f, 0.1f),
                new Color(1f, 0.9f, 0.3f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 100;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 60),
            });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            colorOverLife.color = new ParticleSystem.MinMaxGradient(MakeFadeOutGradient());

            DisableOffscreenPause(ps);
            ApplyParticleMaterial(ps);
            return go;
        }

        private static Gradient MakeFadeOutGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(0f, 1f),
                });
            return g;
        }

        // PrefabSource rewrites the prefab transform every frame from the map
        // state. With the default cullingMode=Automatic + simulationSpace=Local,
        // Unity treats the system as "Pause when offscreen" and decides
        // visibility from the renderer's auto-bounds. At zoom levels where the
        // camera height (baseCameraHeight / 2^zoom) sits near the column's top
        // edge, the bounds intermittently fall outside the frustum, the system
        // pauses, and the column visibly thins / cuts mid-way. AlwaysSimulate
        // keeps emission and motion running regardless of bounds estimation.
        private static void DisableOffscreenPause(ParticleSystem ps)
        {
            var main = ps.main;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        }

        // Best-effort: pick a URP particle shader if the project is on URP,
        // fall back to the built-in particle shader otherwise. Without this,
        // ParticleSystemRenderer renders with the default-magenta error
        // material on URP because the standard particle shader is not in
        // GraphicsSettings → AlwaysIncluded by default.
        private static void ApplyParticleMaterial(ParticleSystem ps)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Sprites/Default");
            if (shader == null) return;

            var mat = new Material(shader)
            {
                color = Color.white,
                renderQueue = 3000,
            };
            // URP particle shader uses _BaseMap; built-in uses _MainTex. Try both.
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", Texture2D.whiteTexture);
            renderer.material = mat;
        }

        private void OnMapClick(MapEvent e)
        {
            if (!e.LngLat.HasValue || _source == null) return;

            // Burst template has stopAction=Destroy + loop=false, so the
            // GameObject self-destructs after one cycle. We only need to
            // remove the source-side bookkeeping when it leaves the map.
            string instanceId = $"burst-{Time.frameCount}";
            var burst = _source.Add(_resolvedBurst, e.LngLat.Value,
                new PrefabPlacementOptions
                {
                    LocalScale = Vector3.one * 3f,
                    HeightOffset = 1f,
                }, instanceId: instanceId);
            burst.GameObject.SetActive(true);

            // Schedule cleanup just past the burst lifetime so the dictionary
            // doesn't grow unbounded as the user clicks repeatedly.
            StartCoroutine(RemoveBurstAfterDelay(instanceId, 2.5f));
        }

        private IEnumerator RemoveBurstAfterDelay(string instanceId, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (_source != null) _source.Remove(instanceId);
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
