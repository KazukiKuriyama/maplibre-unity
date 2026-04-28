namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Common contract for tile-driven layer renderers (Raster / Terrain /
    /// Vector / Symbol / Circle / FillExtrusion / Heatmap / Hillshade).
    /// Provides the per-tile teardown hook used by tile expiry, so callers can
    /// dispatch to the right renderer through a single dictionary lookup
    /// instead of switching on layer type. ShowTile / Update signatures stay
    /// type-specific because raster renderers consume <c>Texture2D</c> while
    /// vector renderers consume <c>VectorTileData</c> + <c>LayerDefinition</c>.
    /// </summary>
    public interface ILayerRenderer
    {
        /// <summary>
        /// Tear down active GameObjects / meshes for the given canonical tile.
        /// Called when <see cref="TileManager"/> retires a tile (camera moved
        /// out of view) or when the layer is invalidated.
        /// </summary>
        void HideTile(CanonicalTileID tileId);
    }
}
