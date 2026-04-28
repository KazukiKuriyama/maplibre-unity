using System;
using UnityEngine;

namespace MapLibre.Unity
{
    public static class CoordinateConversion
    {
        /// <summary>
        /// Convert longitude/latitude to normalized Mercator coordinates [0,1].
        /// (0,0) = top-left (NW), (1,1) = bottom-right (SE).
        /// </summary>
        public static MercatorCoordinate LngLatToMercator(LngLat lngLat)
        {
            double x = (lngLat.Longitude + 180.0) / 360.0;
            double latRad = lngLat.Latitude * Math.PI / 180.0;
            double y = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0;
            return new MercatorCoordinate(x, y);
        }

        /// <summary>
        /// Convert normalized Mercator coordinates back to longitude/latitude.
        /// </summary>
        public static LngLat MercatorToLngLat(MercatorCoordinate merc)
        {
            double lon = merc.X * 360.0 - 180.0;
            double lat = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * merc.Y))) * 180.0 / Math.PI;
            return new LngLat(lon, lat);
        }

        /// <summary>
        /// Convert longitude/latitude to fractional tile X,Y at a given integer zoom level.
        /// </summary>
        public static (double tileX, double tileY) LngLatToTileXY(LngLat lngLat, int zoom)
        {
            double n = 1 << zoom;
            MercatorCoordinate merc = LngLatToMercator(lngLat);
            return (merc.X * n, merc.Y * n);
        }

        /// <summary>
        /// Convert tile X,Y at a given zoom to the NW corner longitude/latitude.
        /// </summary>
        public static LngLat TileXYToLngLat(int tileX, int tileY, int zoom)
        {
            double n = 1 << zoom;
            double lon = tileX / n * 360.0 - 180.0;
            double lat = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * tileY / n))) * 180.0 / Math.PI;
            return new LngLat(lon, lat);
        }

        /// <summary>
        /// Get bounding box (NW, SE corners) for a tile.
        /// </summary>
        public static (LngLat nw, LngLat se) TileBounds(int tileX, int tileY, int zoom)
        {
            LngLat nw = TileXYToLngLat(tileX, tileY, zoom);
            LngLat se = TileXYToLngLat(tileX + 1, tileY + 1, zoom);
            return (nw, se);
        }

        /// <summary>
        /// Convert Mercator coordinate to Unity world position relative to map center.
        /// Map plane is Y=0, north = +Z, east = +X.
        /// </summary>
        public static Vector3 MercatorToUnityWorld(
            MercatorCoordinate merc, MercatorCoordinate centerMerc, float worldScale)
        {
            float x = (float)(merc.X - centerMerc.X) * worldScale;
            float z = (float)(centerMerc.Y - merc.Y) * worldScale;
            return new Vector3(x, 0f, z);
        }

        /// <summary>
        /// Convert Unity world position back to Mercator coordinate.
        /// Inverse of MercatorToUnityWorld.
        /// </summary>
        public static MercatorCoordinate UnityWorldToMercator(
            Vector3 worldPos, MercatorCoordinate centerMerc, float worldScale)
        {
            double x = centerMerc.X + worldPos.x / worldScale;
            double y = centerMerc.Y - worldPos.z / worldScale;
            return new MercatorCoordinate(x, y);
        }

        /// <summary>
        /// Compute Unity world scale: how many Unity units per Mercator unit.
        /// At zoom z, the world is TileSize * 2^z units wide.
        /// </summary>
        public static float GetWorldScale(float zoom)
        {
            return MapConstants.TileSize * Mathf.Pow(2f, zoom);
        }
    }
}
