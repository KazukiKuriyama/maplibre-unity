using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.CameraControl;
using MapLibre.Unity.Style;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates SetFeatureState. Hover a polygon to highlight it; click to
    /// toggle a sticky "selected" state. Both states are read from a single
    /// fill-color "case" expression that resolves against feature-state at draw
    /// time, so changing state requires no tile re-fetch.
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class FeatureStateHoverDemo : MonoBehaviour
    {
        private const string SourceId = "demo-zones";
        private const string FillLayerId = "demo-zones-fill";
        private const string OutlineLayerId = "demo-zones-outline";

        private MapLibreMap _map;
        private long _hoveredId;
        private readonly HashSet<long> _selectedIds = new();

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[FeatureStateHoverDemo] MapLibreMap not found on this GameObject");
                yield break;
            }

            while (!_map.IsInitialized) yield return null;
            yield return null;

            // Polygons are ~1km wide; at z9 (the project-wide default for
            // sample initial views) they collapse to a few pixels and the
            // hover hit-test is effectively impossible. Jump to z12 like
            // FillExtrusionDemo / SkyAndLightDemo where the demo content
            // genuinely needs a closer view.
            _map.JumpTo(new JumpToOptions
            {
                Center = new LngLat(139.7670, 35.6814), Zoom = 12f,
            });

            AddDemoSource();
            AddDemoLayers();

            _map.On(MapEventType.MouseMove, OnMouseMove);
            _map.On(MapEventType.MouseLeave, OnMouseLeave);
            _map.On(MapEventType.Click, OnClick);
        }

        private void AddDemoSource()
        {
            // Three Tokyo polygons with stable feature ids (1-3). Stable ids are
            // required for SetFeatureState -- they're how the map remembers which
            // feature is hovered/selected.
            _map.AddSource(SourceId, new SourceDefinition
            {
                Type = SourceType.GeoJson,
                Data = JToken.Parse(@"{
                    ""type"": ""FeatureCollection"",
                    ""features"": [
                        { ""type"": ""Feature"", ""id"": 1,
                          ""geometry"": { ""type"": ""Polygon"",
                            ""coordinates"": [[[139.7594,35.6762],[139.7594,35.6832],[139.7708,35.6832],[139.7708,35.6762],[139.7594,35.6762]]] },
                          ""properties"": { ""name"": ""Imperial Palace"" } },
                        { ""type"": ""Feature"", ""id"": 2,
                          ""geometry"": { ""type"": ""Polygon"",
                            ""coordinates"": [[[139.6950,35.6715],[139.6950,35.6780],[139.7045,35.6780],[139.7045,35.6715],[139.6950,35.6715]]] },
                          ""properties"": { ""name"": ""Yoyogi Park"" } },
                        { ""type"": ""Feature"", ""id"": 3,
                          ""geometry"": { ""type"": ""Polygon"",
                            ""coordinates"": [[[139.7700,35.7100],[139.7700,35.7180],[139.7820,35.7180],[139.7820,35.7100],[139.7700,35.7100]]] },
                          ""properties"": { ""name"": ""Ueno Park"" } }
                    ]
                }")
            });
        }

        private void AddDemoLayers()
        {
            // fill-color uses ["case", ...] to read feature-state at draw time.
            // Order matters: selected wins over hover so a clicked feature stays
            // red even when the cursor leaves it.
            _map.AddLayer(new LayerDefinition
            {
                Id = FillLayerId,
                Type = LayerType.Fill,
                Source = SourceId,
                SourceLayer = SourceId,
                Paint = new Dictionary<string, object>
                {
                    {
                        "fill-color",
                        new object[]
                        {
                            "case",
                            new object[] { "boolean", new object[] { "feature-state", "selected" }, false }, "#e53935",
                            new object[] { "boolean", new object[] { "feature-state", "hover" }, false }, "#ffb300",
                            "#42a5f5"
                        }
                    },
                    { "fill-opacity", 0.55 }
                }
            }, triggerRefresh: false);

            _map.AddLayer(new LayerDefinition
            {
                Id = OutlineLayerId,
                Type = LayerType.Line,
                Source = SourceId,
                SourceLayer = SourceId,
                Paint = new Dictionary<string, object>
                {
                    { "line-color", "#1565c0" },
                    { "line-width", 2 }
                }
            });
        }

        private void OnMouseMove(MapEvent e)
        {
            if (!e.Point.HasValue) return;

            var hits = _map.QueryRenderedFeatures(e.Point.Value, new QueryOptions
            {
                Layers = new[] { FillLayerId }
            });

            long newId = hits.Count > 0 ? (long)hits[0].Id : 0;
            if (newId == _hoveredId) return;

            // Clear the previous hover, set the new one. Doing both writes
            // via SetFeatureState ensures the partial-rebuild path runs once
            // per change; the renderer skips rebuilds entirely when no
            // fill/line layer reads feature-state.
            if (_hoveredId != 0)
            {
                _map.SetFeatureState(
                    new MapLibreMap.FeatureSelector(SourceId, SourceId, _hoveredId),
                    "hover", false);
            }
            if (newId != 0)
            {
                _map.SetFeatureState(
                    new MapLibreMap.FeatureSelector(SourceId, SourceId, newId),
                    "hover", true);
            }

            _hoveredId = newId;
        }

        private void OnMouseLeave(MapEvent e)
        {
            // Cursor left the map area -- clear any active hover so the
            // polygon doesn't stay yellow when the user looks away.
            if (_hoveredId == 0) return;
            _map.SetFeatureState(
                new MapLibreMap.FeatureSelector(SourceId, SourceId, _hoveredId),
                "hover", false);
            _hoveredId = 0;
        }

        private void OnClick(MapEvent e)
        {
            if (!e.Point.HasValue) return;

            var hits = _map.QueryRenderedFeatures(e.Point.Value, new QueryOptions
            {
                Layers = new[] { FillLayerId }
            });
            if (hits.Count == 0) return;

            long id = (long)hits[0].Id;
            if (id == 0) return;
            bool nowSelected = !_selectedIds.Contains(id);
            if (nowSelected) _selectedIds.Add(id);
            else _selectedIds.Remove(id);

            _map.SetFeatureState(
                new MapLibreMap.FeatureSelector(SourceId, SourceId, id),
                "selected", nowSelected);
        }

        private void OnDestroy()
        {
            if (_map == null) return;
            _map.Off(MapEventType.MouseMove, OnMouseMove);
            _map.Off(MapEventType.MouseLeave, OnMouseLeave);
            _map.Off(MapEventType.Click, OnClick);
        }
    }
}
