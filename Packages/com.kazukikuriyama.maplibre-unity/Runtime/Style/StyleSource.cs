using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace MapLibre.Unity.Style
{
    public enum SourceType
    {
        Vector,
        Raster,
        RasterDem,
        GeoJson,
        Image,
        Video
    }

    [System.Serializable]
    public class SourceDefinition
    {
        public SourceType Type;
        public string Url;
        public List<string> Tiles;
        public double[] Bounds;
        public int MinZoom = 0;
        public int MaxZoom = 22;
        public int TileSize = 256;
        public string Scheme = "xyz";
        public string Attribution;
        public bool Volatile;

        /// <summary>
        /// DEM encoding format for raster-dem sources: "mapbox" or "terrarium".
        /// Default is "mapbox" per MapLibre Style Spec.
        /// </summary>
        public string Encoding = "mapbox";

        /// <summary>
        /// Inline GeoJSON data (FeatureCollection, Feature, or Geometry object).
        /// Only used when Type == GeoJson.
        /// </summary>
        public JToken Data;

        /// <summary>
        /// Image source corner coordinates in [lng, lat] pairs.
        /// Order per MapLibre Style Spec: [top-left, top-right, bottom-right, bottom-left].
        /// Only used when Type == Image.
        /// </summary>
        public double[][] Coordinates;

        /// <summary>
        /// Whether to cluster point features. Matches GeoJSON source "cluster" option.
        /// Only Point/MultiPoint features are clustered; lines and polygons pass through.
        /// </summary>
        public bool Cluster;

        /// <summary>
        /// Cluster radius in CSS pixels (relative to a 512px tile). Default 50.
        /// </summary>
        public int ClusterRadius = 50;

        /// <summary>
        /// Maximum zoom at which clustering is performed. Above this zoom individual
        /// points are returned. Default = (source maxzoom - 1) per MapLibre spec.
        /// -1 in the field means "use the spec default".
        /// </summary>
        public int ClusterMaxZoom = -1;

        /// <summary>
        /// Minimum number of points required to form a cluster. Default 2.
        /// </summary>
        public int ClusterMinPoints = 2;

        /// <summary>
        /// When true, line metrics (cumulative length per vertex) are exposed via
        /// the <c>["line-progress"]</c> expression so a layer can drive its
        /// <c>line-gradient</c> paint property. Spec default is false; setting
        /// true on a vector / GeoJSON source enables gradient rendering for any
        /// line layer that references the source.
        /// </summary>
        public bool LineMetrics;

        /// <summary>
        /// Returns true if the tile intersects the source's declared bounds.
        /// Bounds format: [west, south, east, north] in degrees (TileJSON spec).
        /// When no bounds are set, all tiles are considered in-range.
        /// Matches MapLibre GL JS <c>TileBounds.contains()</c>.
        /// </summary>
        public bool ContainsTile(CanonicalTileID tileId)
        {
            if (Bounds == null || Bounds.Length < 4)
                return true;

            double west = Bounds[0];
            double south = Bounds[1];
            double east = Bounds[2];
            double north = Bounds[3];

            double worldSize = 1 << tileId.Z;
            var nwMerc = CoordinateConversion.LngLatToMercator(new LngLat(west, north));
            var seMerc = CoordinateConversion.LngLatToMercator(new LngLat(east, south));

            int minX = (int)Math.Floor(nwMerc.X * worldSize);
            int maxX = (int)Math.Ceiling(seMerc.X * worldSize);
            int minY = (int)Math.Floor(nwMerc.Y * worldSize);
            int maxY = (int)Math.Ceiling(seMerc.Y * worldSize);

            return tileId.X >= minX && tileId.X < maxX &&
                   tileId.Y >= minY && tileId.Y < maxY;
        }
    }
}
