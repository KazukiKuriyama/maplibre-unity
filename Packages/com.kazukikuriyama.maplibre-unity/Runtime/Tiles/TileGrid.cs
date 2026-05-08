using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity
{
    public static class TileGrid
    {
        /// <summary>
        /// Compute visible tiles by projecting the camera frustum footprint onto tile space.
        /// Only tiles that intersect the frustum polygon are included, matching
        /// MapLibre GL JS coveringTiles behavior.
        /// Uses a scanline approach: for each tile row, compute the X span where
        /// the polygon edges cross that row, then include only tiles within that span.
        /// </summary>
        public static HashSet<CanonicalTileID> GetVisibleTiles(
            LngLat center, float zoom, Vector3[] groundFootprint,
            int minSourceZoom = 0, int maxSourceZoom = 22, int tileBuffer = 1)
        {
            int intZoom = (int)Math.Floor(zoom);
            intZoom = Math.Clamp(intZoom, minSourceZoom, maxSourceZoom);

            int n = 1 << intZoom;
            float worldScale = CoordinateConversion.GetWorldScale(zoom);
            var centerMerc = CoordinateConversion.LngLatToMercator(center);

            // Convert ground footprint (Unity world coords) to tile coordinates
            int vertCount = groundFootprint.Length;
            double[] polyX = new double[vertCount];
            double[] polyY = new double[vertCount];
            double minTX = double.MaxValue, maxTX = double.MinValue;
            double minTY = double.MaxValue, maxTY = double.MinValue;

            for (int i = 0; i < vertCount; i++)
            {
                var merc = CoordinateConversion.UnityWorldToMercator(groundFootprint[i], centerMerc, worldScale);
                polyX[i] = merc.X * n;
                polyY[i] = merc.Y * n;
                minTX = Math.Min(minTX, polyX[i]);
                maxTX = Math.Max(maxTX, polyX[i]);
                minTY = Math.Min(minTY, polyY[i]);
                maxTY = Math.Max(maxTY, polyY[i]);
            }

            // Bounding box with buffer, clamped to valid tile range
            int minTileY = Math.Max((int)Math.Floor(minTY) - tileBuffer, 0);
            int maxTileY = Math.Min((int)Math.Floor(maxTY) + tileBuffer, n - 1);

            int bboxMinTileX = (int)Math.Floor(minTX) - tileBuffer;
            int bboxMaxTileX = (int)Math.Floor(maxTX) + tileBuffer;

            // Clamp X range to at most one full world width
            if (bboxMaxTileX - bboxMinTileX >= n)
            {
                bboxMinTileX = 0;
                bboxMaxTileX = n - 1;
            }

            var result = new HashSet<CanonicalTileID>();

            // Scanline: for each tile row, find the X range that intersects the polygon.
            // For each row [y, y+1), we find polygon edge intersections at the row
            // boundaries AND include any polygon vertices that fall within the row.
            for (int y = minTileY; y <= maxTileY; y++)
            {
                double rowMinX = double.MaxValue;
                double rowMaxX = double.MinValue;

                // Include polygon vertices that fall within this tile row [y, y+1)
                for (int i = 0; i < vertCount; i++)
                {
                    if (polyY[i] >= y && polyY[i] <= y + 1)
                    {
                        rowMinX = Math.Min(rowMinX, polyX[i]);
                        rowMaxX = Math.Max(rowMaxX, polyX[i]);
                    }
                }

                // Find edge intersections at the top (y) and bottom (y+1) of the row
                for (int pass = 0; pass <= 1; pass++)
                {
                    double scanY = y + pass;

                    for (int i = 0; i < vertCount; i++)
                    {
                        int j = (i + 1) % vertCount;
                        double y0 = polyY[i], y1 = polyY[j];
                        double x0 = polyX[i], x1 = polyX[j];

                        // Skip edges that don't span this scanline
                        double eMin = Math.Min(y0, y1);
                        double eMax = Math.Max(y0, y1);
                        if (eMax < scanY || eMin > scanY) continue;

                        if (Math.Abs(y1 - y0) < 1e-12)
                        {
                            // Horizontal edge
                            rowMinX = Math.Min(rowMinX, Math.Min(x0, x1));
                            rowMaxX = Math.Max(rowMaxX, Math.Max(x0, x1));
                        }
                        else
                        {
                            double t = (scanY - y0) / (y1 - y0);
                            t = Math.Clamp(t, 0.0, 1.0);
                            double ix = x0 + t * (x1 - x0);
                            rowMinX = Math.Min(rowMinX, ix);
                            rowMaxX = Math.Max(rowMaxX, ix);
                        }
                    }
                }

                if (rowMinX > rowMaxX) continue;

                int xStart = Math.Max((int)Math.Floor(rowMinX) - tileBuffer, bboxMinTileX);
                int xEnd = Math.Min((int)Math.Floor(rowMaxX) + tileBuffer, bboxMaxTileX);

                for (int x = xStart; x <= xEnd; x++)
                {
                    int wrappedX = ((x % n) + n) % n;
                    result.Add(new CanonicalTileID(intZoom, wrappedX, y));
                }
            }

            return result;
        }

        /// <summary>
        /// LOD-aware tile selection: returns a set of tiles at *varying* zoom levels.
        /// Near the camera, tiles are returned at the map's current zoom; far from
        /// the camera, parent (lower-zoom) tiles are returned so the on-screen
        /// pixel size of each tile stays roughly constant.
        ///
        /// This matches MapLibre GL JS' coveringTiles behavior and is the only
        /// way to keep tile counts manageable when the camera is heavily pitched
        /// (where the horizon would otherwise require thousands of tiles).
        ///
        /// Algorithm: start from a root tile and recursively subdivide. A tile is
        /// kept (not subdivided) when (a) it's at the source max zoom, or
        /// (b) its on-screen pixel size has dropped below the target threshold.
        /// </summary>
        public static HashSet<CanonicalTileID> GetVisibleTilesLOD(
            UnityEngine.Camera camera,
            LngLat center, float mapZoom,
            int minSourceZoom = 0, int maxSourceZoom = 22,
            float targetTileScreenSize = 256f,
            float maxTerrainHeightUnits = 1000f)
        {
            var result = new HashSet<CanonicalTileID>();
            if (camera == null) return result;

            // Don't request tiles deeper than the map's current zoom (matches
            // MapLibre behavior -- over-zoom is handled by stretching, not finer
            // tiles). Also clamp to the source's maxzoom.
            int effectiveMaxZ = System.Math.Min(maxSourceZoom,
                System.Math.Max((int)System.Math.Floor(mapZoom), minSourceZoom));

            float worldScale = CoordinateConversion.GetWorldScale(mapZoom);
            var centerMerc = CoordinateConversion.LngLatToMercator(center);
            var frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
            Vector3 cameraPos = camera.transform.position;
            float focalLengthPx = camera.pixelHeight /
                (2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));

            // Start from the source min zoom (or z=0 if minZ=0) and recurse down.
            int rootZ = System.Math.Max(0, minSourceZoom);
            int rootN = 1 << rootZ;
            for (int x = 0; x < rootN; x++)
            {
                for (int y = 0; y < rootN; y++)
                {
                    VisitLOD(new CanonicalTileID(rootZ, x, y),
                        frustumPlanes, cameraPos, focalLengthPx,
                        centerMerc, worldScale,
                        minSourceZoom, effectiveMaxZ,
                        targetTileScreenSize, maxTerrainHeightUnits, result);
                }
            }

            return result;
        }

        private static void VisitLOD(
            CanonicalTileID tile,
            Plane[] frustumPlanes,
            Vector3 cameraPos,
            float focalLengthPx,
            MercatorCoordinate centerMerc,
            float worldScale,
            int minZ,
            int maxZ,
            float targetSize,
            float maxHeightUnits,
            HashSet<CanonicalTileID> result)
        {
            // Tile world AABB (XZ from corners, Y range to accommodate terrain displacement).
            int n = 1 << tile.Z;
            double tileSize = 1.0 / n;
            var nwMerc = new MercatorCoordinate(tile.X * tileSize, tile.Y * tileSize);
            var seMerc = new MercatorCoordinate((tile.X + 1) * tileSize, (tile.Y + 1) * tileSize);
            Vector3 nwWorld = CoordinateConversion.MercatorToUnityWorld(nwMerc, centerMerc, worldScale);
            Vector3 seWorld = CoordinateConversion.MercatorToUnityWorld(seMerc, centerMerc, worldScale);

            float minX = Mathf.Min(nwWorld.x, seWorld.x);
            float maxX = Mathf.Max(nwWorld.x, seWorld.x);
            float minZw = Mathf.Min(nwWorld.z, seWorld.z);
            float maxZw = Mathf.Max(nwWorld.z, seWorld.z);
            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(minX, 0f, minZw),
                             new Vector3(maxX, maxHeightUnits, maxZw));

            // Frustum cull.
            if (!GeometryUtility.TestPlanesAABB(frustumPlanes, bounds)) return;

            // At source max zoom: stop subdivision regardless of screen size.
            if (tile.Z >= maxZ)
            {
                if (tile.Z >= minZ) result.Add(tile);
                return;
            }

            // Estimate screen-space pixel size of this tile (distance-based projection).
            // The tile's world width is (maxX - minX); divide by camera distance and
            // multiply by the camera's focal length in pixels.
            float tileWorldWidth = maxX - minX;
            Vector3 tileCenter = new Vector3((minX + maxX) * 0.5f, 0f, (minZw + maxZw) * 0.5f);
            float distance = Mathf.Max(Vector3.Distance(cameraPos, tileCenter), 0.01f);
            float screenSize = tileWorldWidth / distance * focalLengthPx;

            // Adequate detail at this LOD -- stop subdividing if at/above source min zoom.
            if (tile.Z >= minZ && screenSize <= targetSize)
            {
                result.Add(tile);
                return;
            }

            // Otherwise subdivide.
            foreach (var child in tile.Children())
            {
                VisitLOD(child, frustumPlanes, cameraPos, focalLengthPx,
                    centerMerc, worldScale, minZ, maxZ, targetSize, maxHeightUnits, result);
            }
        }

        /// <summary>
        /// Fallback overload for simple viewport-based calculation (no pitch/bearing).
        /// </summary>
        public static HashSet<CanonicalTileID> GetVisibleTiles(
            LngLat center, float zoom, float viewportWidthUnits, float viewportHeightUnits,
            int minSourceZoom = 0, int maxSourceZoom = 22, int tileBuffer = 1)
        {
            int intZoom = (int)Math.Floor(zoom);
            intZoom = Math.Clamp(intZoom, minSourceZoom, maxSourceZoom);

            int n = 1 << intZoom;

            var (centerTileX, centerTileY) = CoordinateConversion.LngLatToTileXY(center, intZoom);

            float fractionalScale = (float)Math.Pow(2, zoom - intZoom);
            float tileSizeInWorld = MapConstants.TileSize * fractionalScale;
            float tilesVisibleX = viewportWidthUnits / tileSizeInWorld;
            float tilesVisibleY = viewportHeightUnits / tileSizeInWorld;

            int minTileX = (int)Math.Floor(centerTileX - tilesVisibleX / 2.0) - tileBuffer;
            int maxTileX = (int)Math.Floor(centerTileX + tilesVisibleX / 2.0) + tileBuffer;
            int minTileY = (int)Math.Floor(centerTileY - tilesVisibleY / 2.0) - tileBuffer;
            int maxTileY = (int)Math.Floor(centerTileY + tilesVisibleY / 2.0) + tileBuffer;

            minTileY = Math.Max(minTileY, 0);
            maxTileY = Math.Min(maxTileY, n - 1);

            var result = new HashSet<CanonicalTileID>();
            for (int y = minTileY; y <= maxTileY; y++)
            {
                for (int x = minTileX; x <= maxTileX; x++)
                {
                    int wrappedX = ((x % n) + n) % n;
                    result.Add(new CanonicalTileID(intZoom, wrappedX, y));
                }
            }
            return result;
        }
    }
}
