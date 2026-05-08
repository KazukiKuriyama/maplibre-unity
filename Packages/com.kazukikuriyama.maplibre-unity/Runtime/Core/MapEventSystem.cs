using System;
using System.Collections.Generic;

namespace MapLibre.Unity
{
    /// <summary>
    /// Manages map event subscriptions with on/off/once semantics
    /// matching MapLibre GL JS.
    /// </summary>
    public class MapEventSystem
    {
        private readonly Dictionary<MapEventType, List<Action<MapEvent>>> _handlers = new();
        private readonly Dictionary<MapEventType, List<Action<MapEvent>>> _onceHandlers = new();

        /// <summary>
        /// Layer-filtered handler: fires only when the event's queried features include
        /// at least one feature from <c>LayerIds</c>. Features are eagerly populated for
        /// the invocation so the handler can read <c>evt.Features</c> without re-querying.
        /// </summary>
        private class FilteredHandler
        {
            public string[] LayerIds;
            public Action<MapEvent> Handler;
            public bool Once;
        }

        private readonly Dictionary<MapEventType, List<FilteredHandler>> _filteredHandlers = new();

        public void On(MapEventType type, Action<MapEvent> handler)
        {
            if (!_handlers.TryGetValue(type, out var list))
                _handlers[type] = list = new List<Action<MapEvent>>();
            list.Add(handler);
        }

        public void Off(MapEventType type, Action<MapEvent> handler)
        {
            if (_handlers.TryGetValue(type, out var list))
                list.Remove(handler);
            if (_filteredHandlers.TryGetValue(type, out var fList))
                fList.RemoveAll(fh => fh.Handler == handler);
        }

        public void Once(MapEventType type, Action<MapEvent> handler)
        {
            if (!_onceHandlers.TryGetValue(type, out var list))
                _onceHandlers[type] = list = new List<Action<MapEvent>>();
            list.Add(handler);
        }

        /// <summary>
        /// Layer-filtered subscription. Matches map.on(type, layerId, handler) in MapLibre GL JS:
        /// the handler only fires when the pointer event hits a rendered feature in the given
        /// layer(s), and <c>e.Features</c> is populated with those features.
        /// </summary>
        public void On(MapEventType type, string[] layerIds, Action<MapEvent> handler, bool once = false)
        {
            if (!_filteredHandlers.TryGetValue(type, out var list))
                _filteredHandlers[type] = list = new List<FilteredHandler>();
            list.Add(new FilteredHandler { LayerIds = layerIds, Handler = handler, Once = once });
        }

        public void Fire(MapEvent evt)
        {
            // Unfiltered handlers
            if (_handlers.TryGetValue(evt.Type, out var list))
            {
                for (int i = 0, count = list.Count; i < count; i++)
                    list[i]?.Invoke(evt);
            }

            // Layer-filtered handlers -- only for events where Features lookup makes sense.
            if (_filteredHandlers.TryGetValue(evt.Type, out var fList) && fList.Count > 0 &&
                evt.Point.HasValue)
            {
                // Iterate a snapshot so `once` removal mid-iteration is safe.
                for (int i = fList.Count - 1; i >= 0; i--)
                {
                    var fh = fList[i];
                    evt.SetLayerFilter(fh.LayerIds);
                    var features = evt.Features;
                    if (features != null && features.Count > 0)
                    {
                        fh.Handler?.Invoke(evt);
                        if (fh.Once) fList.RemoveAt(i);
                    }
                }
                // Reset filter so downstream consumers (once handlers below) get a clean query.
                evt.SetLayerFilter(null);
            }

            if (_onceHandlers.TryGetValue(evt.Type, out var onceList) && onceList.Count > 0)
            {
                var snapshot = new List<Action<MapEvent>>(onceList);
                onceList.Clear();
                foreach (var handler in snapshot)
                    handler?.Invoke(evt);
            }
        }

        public void Clear()
        {
            _handlers.Clear();
            _onceHandlers.Clear();
            _filteredHandlers.Clear();
        }
    }
}
