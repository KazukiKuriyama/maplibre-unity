using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity.Source
{
    public class TileCache
    {
        private readonly int _capacity;
        private readonly Dictionary<CanonicalTileID, Texture2D> _cache;
        private readonly LinkedList<CanonicalTileID> _lruList;
        private readonly Dictionary<CanonicalTileID, LinkedListNode<CanonicalTileID>> _lruNodes;

        public TileCache(int capacity = 256)
        {
            _capacity = capacity;
            _cache = new Dictionary<CanonicalTileID, Texture2D>(capacity);
            _lruList = new LinkedList<CanonicalTileID>();
            _lruNodes = new Dictionary<CanonicalTileID, LinkedListNode<CanonicalTileID>>(capacity);
        }

        public bool TryGet(CanonicalTileID tileId, out Texture2D texture)
        {
            if (_cache.TryGetValue(tileId, out texture))
            {
                // Move to front (most recently used)
                var node = _lruNodes[tileId];
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                return true;
            }
            texture = null;
            return false;
        }

        public void Put(CanonicalTileID tileId, Texture2D texture)
        {
            if (_cache.ContainsKey(tileId))
            {
                // Update existing
                _cache[tileId] = texture;
                var node = _lruNodes[tileId];
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                return;
            }

            // Evict LRU if at capacity
            while (_cache.Count >= _capacity && _lruList.Count > 0)
            {
                var lruTile = _lruList.Last.Value;
                _lruList.RemoveLast();
                _lruNodes.Remove(lruTile);
                if (_cache.TryGetValue(lruTile, out var evictedTex))
                {
                    _cache.Remove(lruTile);
                    if (evictedTex != null)
                        Object.Destroy(evictedTex);
                }
            }

            _cache[tileId] = texture;
            var newNode = _lruList.AddFirst(tileId);
            _lruNodes[tileId] = newNode;
        }

        public void Remove(CanonicalTileID tileId)
        {
            if (_cache.TryGetValue(tileId, out var texture))
            {
                _cache.Remove(tileId);
                if (_lruNodes.TryGetValue(tileId, out var node))
                {
                    _lruList.Remove(node);
                    _lruNodes.Remove(tileId);
                }
                if (texture != null)
                    Object.Destroy(texture);
            }
        }

        public bool Contains(CanonicalTileID tileId) => _cache.ContainsKey(tileId);

        public void Clear()
        {
            foreach (var kvp in _cache)
            {
                if (kvp.Value != null)
                    Object.Destroy(kvp.Value);
            }
            _cache.Clear();
            _lruList.Clear();
            _lruNodes.Clear();
        }

        public int Count => _cache.Count;
    }
}
