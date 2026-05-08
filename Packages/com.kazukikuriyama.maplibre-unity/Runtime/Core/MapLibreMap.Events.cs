using System;
using UnityEngine;

namespace MapLibre.Unity
{
    /// <summary>
    /// Event subscription / firing surface. Mirrors map.on / map.off / map.once
    /// from MapLibre GL JS, plus the internal Fire* helpers used by the rest of
    /// the partial class.
    /// </summary>
    public partial class MapLibreMap
    {
        /// <summary>
        /// Subscribe to a map event. Matches map.on() in MapLibre GL JS.
        /// </summary>
        public void On(MapEventType type, Action<MapEvent> handler)
        {
            _eventSystem.On(type, handler);

            // If subscribing to Load after map is already initialized,
            // invoke immediately. Matches MapLibre GL JS behavior.
            if (type == MapEventType.Load && _isInitialized)
                handler(new MapEvent(MapEventType.Load, this));
        }

        /// <summary>
        /// Unsubscribe from a map event. Matches map.off() in MapLibre GL JS.
        /// </summary>
        public void Off(MapEventType type, Action<MapEvent> handler)
            => _eventSystem.Off(type, handler);

        /// <summary>
        /// Subscribe to a map event, firing only once. Matches map.once() in MapLibre GL JS.
        /// </summary>
        public void Once(MapEventType type, Action<MapEvent> handler)
        {
            _eventSystem.Once(type, handler);

            if (type == MapEventType.Load && _isInitialized)
                handler(new MapEvent(MapEventType.Load, this));
        }

        /// <summary>
        /// Layer-filtered subscription. Matches map.on(type, layerId, handler) in MapLibre GL JS:
        /// the handler only fires when the pointer event hits a rendered feature in <paramref name="layerId"/>.
        /// <c>e.Features</c> is populated with the hit features from that layer.
        /// </summary>
        public void On(MapEventType type, string layerId, Action<MapEvent> handler)
            => _eventSystem.On(type, new[] { layerId }, handler);

        /// <summary>
        /// Layer-filtered subscription with multiple layers.
        /// </summary>
        public void On(MapEventType type, string[] layerIds, Action<MapEvent> handler)
            => _eventSystem.On(type, layerIds, handler);

        /// <summary>
        /// Layer-filtered once subscription.
        /// </summary>
        public void Once(MapEventType type, string layerId, Action<MapEvent> handler)
            => _eventSystem.On(type, new[] { layerId }, handler, once: true);

        private void FireEvent(MapEventType type)
            => _eventSystem.Fire(new MapEvent(type, this));

        private void FireEvent(MapEvent evt)
            => _eventSystem.Fire(evt);

        private void FireDataLoading(string sourceId)
            => _eventSystem.Fire(new MapEvent(MapEventType.DataLoading, this)
            {
                SourceId = sourceId,
                Id = sourceId,
            });

        private void FireSourceData(string sourceId, bool isLoaded)
            => _eventSystem.Fire(new MapEvent(MapEventType.SourceData, this)
            {
                SourceId = sourceId,
                Id = sourceId,
                IsSourceLoaded = isLoaded,
            });

        private void FireError(string message, string sourceId = null, Exception exception = null,
            bool isTransient = false)
        {
            // Transient failures (network blips, parse timeouts on slow tiles)
            // happen during normal use -- log as warning to keep the Unity
            // console actionable. The MapEvent still fires as Error so user
            // code that subscribes to MapEventType.Error still hears about it.
            if (isTransient)
                Debug.LogWarning($"[MapLibreMap] {message}");
            else
                Debug.LogError($"[MapLibreMap] {message}");
            _eventSystem.Fire(new MapEvent(MapEventType.Error, this)
            {
                ErrorMessage = message,
                Error = exception,
                SourceId = sourceId,
                Id = sourceId,
            });
        }

        /// <summary>
        /// Create a pointer MapEvent with LngLat resolved from screen position.
        /// </summary>
        private MapEvent MakePointerEvent(MapEventType type, Vector2 screenPos)
        {
            return new MapEvent(type, this)
            {
                Point = screenPos,
                LngLat = Unproject(screenPos),
            };
        }
    }
}
