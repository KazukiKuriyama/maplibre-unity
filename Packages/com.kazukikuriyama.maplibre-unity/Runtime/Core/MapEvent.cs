using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity
{
    /// <summary>
    /// Event data passed to map event handlers.
    /// Matches the event object pattern from MapLibre GL JS.
    /// </summary>
    public class MapEvent
    {
        /// <summary>Event type.</summary>
        public MapEventType Type { get; }

        /// <summary>The MapLibreMap instance that fired the event.</summary>
        public MapLibreMap Target { get; }

        /// <summary>Geographic coordinates (for pointer/touch events).</summary>
        public LngLat? LngLat { get; set; }

        /// <summary>Screen-space point (for pointer/touch events).</summary>
        public Vector2? Point { get; set; }

        /// <summary>
        /// Source or layer ID (for legacy SourceAdd/Remove, LayerAdd/Remove events).
        /// Prefer <see cref="SourceId"/> / <see cref="LayerId"/> for new code.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Source identifier for SourceData / DataLoading events.
        /// </summary>
        public string SourceId { get; set; }

        /// <summary>
        /// Layer identifier for LayerAdd / LayerRemove and for layer-filtered pointer events.
        /// </summary>
        public string LayerId { get; set; }

        /// <summary>
        /// Touch finger index (for touch events). -1 for mouse events.
        /// </summary>
        public int TouchIndex { get; set; } = -1;

        /// <summary>
        /// Scroll delta (x = horizontal, y = vertical) for Wheel events.
        /// </summary>
        public Vector2 WheelDelta { get; set; }

        /// <summary>
        /// Error information for Error events.
        /// </summary>
        public System.Exception Error { get; set; }

        /// <summary>
        /// Error message for Error events when no exception is attached.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Viewport size (width, height in pixels) for Resize events.
        /// </summary>
        public Vector2 Size { get; set; }

        /// <summary>
        /// Whether the data load has finished.
        /// For SourceData: true when a source has fully loaded; false for intermediate updates.
        /// For StyleData: always true when the style finished applying.
        /// </summary>
        public bool IsSourceLoaded { get; set; }

        /// <summary>
        /// Rendered features at this event's point. For pointer/touch events, this is
        /// populated on first access via map.QueryRenderedFeatures(Point).
        /// Matches MapLibre GL JS `e.features` semantics. Returns null when Point is not set.
        /// </summary>
        public List<QueryFeature> Features
        {
            get
            {
                if (_featuresInitialized) return _features;
                _featuresInitialized = true;
                if (Target != null && Point.HasValue)
                {
                    QueryOptions opts = null;
                    if (_layerFilter != null)
                    {
                        var arr = new string[_layerFilter.Count];
                        for (int i = 0; i < _layerFilter.Count; i++) arr[i] = _layerFilter[i];
                        opts = new QueryOptions { Layers = arr };
                    }
                    _features = Target.QueryRenderedFeatures(Point.Value, opts);
                }
                return _features;
            }
            set
            {
                _features = value;
                _featuresInitialized = true;
            }
        }

        private List<QueryFeature> _features;
        private bool _featuresInitialized;
        private IReadOnlyList<string> _layerFilter;

        public MapEvent(MapEventType type, MapLibreMap target)
        {
            Type = type;
            Target = target;
        }

        /// <summary>
        /// Configure this event to filter Features to the given layer IDs when accessed.
        /// Used by the layer-filtered On() overload.
        /// </summary>
        internal void SetLayerFilter(IReadOnlyList<string> layerIds)
        {
            _layerFilter = layerIds;
            // Reset so the next Features access queries with the filter applied.
            _featuresInitialized = false;
            _features = null;
        }
    }
}
