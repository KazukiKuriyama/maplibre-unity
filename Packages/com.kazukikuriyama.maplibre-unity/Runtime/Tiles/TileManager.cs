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

        // Tiles that left the required set this frame are not expired
        // immediately -- they sit here until ExpirationGraceFrames frames have
        // elapsed without becoming required again. Prevents flicker during
        // orbit / fast pan when frustum culling oscillates a tile in and out
        // across consecutive frames; the entries are cleared the moment the
        // tile is re-required, so no extra GameObject churn for stable views.
        private readonly Dictionary<CanonicalTileID, int> _pendingExpiration = new();
        private const int ExpirationGraceFrames = 30;

        public void UpdateVisibleTiles(HashSet<CanonicalTileID> requiredTiles)
        {
            _requiredTiles = requiredTiles;

            // Request new tiles that aren't active yet. Re-required tiles get
            // their pending-expiration entry cleared so the grace timer resets.
            foreach (var tile in requiredTiles)
            {
                if (!_activeTiles.Contains(tile))
                {
                    _activeTiles.Add(tile);
                    _tileStates[tile] = TileState.None;
                    OnTileNeeded?.Invoke(tile);
                }
                _pendingExpiration.Remove(tile);
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

            // Schedule (or refresh) the grace deadline for tiles that just
            // left the required set. Fallback tiles are kept indefinitely so
            // their expiration entry is cleared too.
            int currentFrame = UnityEngine.Time.frameCount;
            foreach (var tile in _activeTiles)
            {
                bool stillRequired = requiredTiles.Contains(tile);
                bool stillFallback = neededFallbacks.Contains(tile);
                if (stillRequired || stillFallback)
                {
                    _pendingExpiration.Remove(tile);
                }
                else if (!_pendingExpiration.ContainsKey(tile))
                {
                    _pendingExpiration[tile] = currentFrame + ExpirationGraceFrames;
                }
            }

            // Expire tiles whose grace period has elapsed. We collect first
            // because the loop body mutates _activeTiles via OnTileExpired
            // listeners that may, in turn, observe _pendingExpiration.
            List<CanonicalTileID> toRemove = null;
            foreach (var kv in _pendingExpiration)
            {
                if (currentFrame >= kv.Value)
                {
                    toRemove ??= new List<CanonicalTileID>();
                    toRemove.Add(kv.Key);
                }
            }

            if (toRemove != null)
            {
                foreach (var tile in toRemove)
                {
                    _activeTiles.Remove(tile);
                    _fallbackTiles.Remove(tile);
                    _tileStates.Remove(tile);
                    _pendingExpiration.Remove(tile);
                    OnTileExpired?.Invoke(tile);
                }
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
            _pendingExpiration.Clear();
        }
    }
}
