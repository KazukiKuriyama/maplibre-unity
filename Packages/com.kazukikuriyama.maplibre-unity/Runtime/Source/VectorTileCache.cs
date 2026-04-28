using System.Collections.Generic;
using MapLibre.Unity.VectorTile;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// LRU cache for parsed vector tile data.
    /// </summary>
    public class VectorTileCache
    {
        private readonly int _capacity;
        private readonly Dictionary<CanonicalTileID, VectorTileData> _cache;
        private readonly LinkedList<CanonicalTileID> _lruList;
        private readonly Dictionary<CanonicalTileID, LinkedListNode<CanonicalTileID>> _nodeMap;

        public int Count => _cache.Count;

        public VectorTileCache(int capacity = 256)
        {
            _capacity = capacity;
            _cache = new Dictionary<CanonicalTileID, VectorTileData>(capacity);
            _lruList = new LinkedList<CanonicalTileID>();
            _nodeMap = new Dictionary<CanonicalTileID, LinkedListNode<CanonicalTileID>>(capacity);
        }

        public bool TryGet(CanonicalTileID tileId, out VectorTileData data)
        {
            if (_cache.TryGetValue(tileId, out data))
            {
                // Move to front (most recently used)
                var node = _nodeMap[tileId];
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                return true;
            }
            data = null;
            return false;
        }

        public void Put(CanonicalTileID tileId, VectorTileData data)
        {
            if (_cache.ContainsKey(tileId))
            {
                _cache[tileId] = data;
                var node = _nodeMap[tileId];
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                return;
            }

            // Evict LRU if at capacity
            while (_cache.Count >= _capacity && _lruList.Count > 0)
            {
                var lruId = _lruList.Last.Value;
                _lruList.RemoveLast();
                _cache.Remove(lruId);
                _nodeMap.Remove(lruId);
            }

            _cache[tileId] = data;
            var newNode = _lruList.AddFirst(tileId);
            _nodeMap[tileId] = newNode;
        }

        public void Remove(CanonicalTileID tileId)
        {
            if (_nodeMap.TryGetValue(tileId, out var node))
            {
                _lruList.Remove(node);
                _nodeMap.Remove(tileId);
            }
            _cache.Remove(tileId);
        }

        public void Clear()
        {
            _cache.Clear();
            _lruList.Clear();
            _nodeMap.Clear();
        }
    }
}
