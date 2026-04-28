using System;
using System.Collections.Generic;
using System.Linq;

namespace MapLibre.Unity
{
    public class TileManager
    {
        public event Action<CanonicalTileID> OnTileNeeded;
        public event Action<CanonicalTileID> OnTileExpired;

        private HashSet<CanonicalTileID> _requiredTiles = new();
        private readonly HashSet<CanonicalTileID> _activeTiles = new();
        private readonly HashSet<CanonicalTileID> _fallbackTiles = new();
        private readonly Dictionary<CanonicalTileID, TileState> _tileStates = new();

        public void UpdateVisibleTiles(HashSet<CanonicalTileID> requiredTiles)
        {
            _requiredTiles = requiredTiles;

            // Request new tiles that aren't active yet
            foreach (var tile in requiredTiles)
            {
                if (!_activeTiles.Contains(tile))
                {
                    _activeTiles.Add(tile);
                    _tileStates[tile] = TileState.None;
                    OnTileNeeded?.Invoke(tile);
                }
            }

            // Determine which fallback (parent) tiles are still needed.
            // A fallback tile is kept while any of its descendant required tiles are not yet Loaded.
            var neededFallbacks = new HashSet<CanonicalTileID>();
            foreach (var tile in requiredTiles)
            {
                if (GetTileState(tile) != TileState.Loaded)
                {
                    // Walk up the zoom hierarchy to find loaded ancestors
                    var parent = tile;
                    while (parent.Z > 0)
                    {
                        parent = parent.Parent();
                        if (GetTileState(parent) == TileState.Loaded)
                        {
                            neededFallbacks.Add(parent);
                            break;
                        }
                    }
                }
            }

            // Remove tiles that are no longer required AND no longer needed as fallbacks
            var toRemove = new List<CanonicalTileID>();
            foreach (var tile in _activeTiles)
            {
                if (!requiredTiles.Contains(tile) && !neededFallbacks.Contains(tile))
                    toRemove.Add(tile);
            }

            foreach (var tile in toRemove)
            {
                _activeTiles.Remove(tile);
                _fallbackTiles.Remove(tile);
                _tileStates.Remove(tile);
                OnTileExpired?.Invoke(tile);
            }

            _fallbackTiles.Clear();
            foreach (var fb in neededFallbacks)
                _fallbackTiles.Add(fb);
        }

        public void SetTileState(CanonicalTileID tileId, TileState state)
        {
            if (_tileStates.ContainsKey(tileId))
                _tileStates[tileId] = state;
        }

        public TileState GetTileState(CanonicalTileID tileId)
        {
            return _tileStates.TryGetValue(tileId, out var state) ? state : TileState.None;
        }

        public bool IsActive(CanonicalTileID tileId) => _activeTiles.Contains(tileId);

        /// <summary>
        /// Currently active tile IDs (loaded or loading).
        /// Used by queryRenderedFeatures / querySourceFeatures.
        /// </summary>
        public IReadOnlyCollection<CanonicalTileID> ActiveTiles => _activeTiles;

        public void Clear()
        {
            foreach (var tile in _activeTiles)
                OnTileExpired?.Invoke(tile);

            _activeTiles.Clear();
            _fallbackTiles.Clear();
            _tileStates.Clear();
            _requiredTiles.Clear();
        }
    }
}
