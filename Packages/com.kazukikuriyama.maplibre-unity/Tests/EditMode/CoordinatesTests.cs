using System;
using NUnit.Framework;
using UnityEngine;

namespace MapLibre.Unity.Tests.EditMode
{
    /// <summary>
    /// Pure-function tests for the geographic-math layer: <see cref="LngLat"/>,
    /// <see cref="MercatorCoordinate"/>, <see cref="CoordinateConversion"/>, and
    /// <see cref="LngLatBounds"/>. These have no Unity / runtime dependencies
    /// so they live in EditMode.
    /// </summary>
    public class CoordinatesTests
    {
        // Generous tolerance because Mercator math goes through double-precision
        // exp / log / atan and we round-trip through them. 1e-9 is well below
        // any tile-resolution precision we care about.
        private const double LngLatEps = 1e-9;
        private const double MercEps = 1e-12;

        // === LngLat.Wrap ===

        [Test]
        public void Wrap_LongitudeAbove180_WrapsIntoRange()
        {
            var w = new LngLat(190.0, 0.0).Wrap();
            Assert.AreEqual(-170.0, w.Longitude, LngLatEps);
        }

        [Test]
        public void Wrap_LongitudeBelowMinus180_WrapsIntoRange()
        {
            var w = new LngLat(-190.0, 0.0).Wrap();
            Assert.AreEqual(170.0, w.Longitude, LngLatEps);
        }

        [Test]
        public void Wrap_LongitudeOutsideMultipleTurns_StillWraps()
        {
            // 720 = two full turns east → 0
            var w = new LngLat(720.0, 0.0).Wrap();
            Assert.AreEqual(0.0, w.Longitude, LngLatEps);
        }

        [Test]
        public void Wrap_LatitudeAbovePole_ClampsToMaxLatitude()
        {
            var w = new LngLat(0.0, 90.0).Wrap();
            Assert.AreEqual(MapConstants.MaxLatitude, w.Latitude, LngLatEps);
        }

        [Test]
        public void Wrap_LatitudeBelowPole_ClampsToMinusMaxLatitude()
        {
            var w = new LngLat(0.0, -90.0).Wrap();
            Assert.AreEqual(-MapConstants.MaxLatitude, w.Latitude, LngLatEps);
        }

        [Test]
        public void Wrap_AlreadyInRange_IsIdentity()
        {
            var input = new LngLat(135.0, 35.0);
            var w = input.Wrap();
            Assert.AreEqual(input.Longitude, w.Longitude, LngLatEps);
            Assert.AreEqual(input.Latitude, w.Latitude, LngLatEps);
        }

        // === LngLat equality / hashing ===

        [Test]
        public void LngLat_Equality_IsBitExact_AndHashAgrees()
        {
            // Equals/== must be bit-exact (so hash agrees) -- same coordinates
            // hash the same, slightly different ones do not compare equal.
            var a = new LngLat(139.7670, 35.6814);
            var b = new LngLat(139.7670, 35.6814);
            Assert.IsTrue(a == b);
            Assert.IsTrue(a.Equals((object)b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());

            var c = new LngLat(139.7670 + 1e-12, 35.6814);
            Assert.IsFalse(a == c, "bit-different coordinates must not be ==");
        }

        [Test]
        public void LngLat_ApproxEquals_TreatsCloseValuesAsEqual()
        {
            // ApproxEquals is the right tool for coordinates that came out of
            // float math (e.g. Mercator round-trips).
            var a = new LngLat(139.7670, 35.6814);
            var b = new LngLat(139.7670 + 1e-12, 35.6814);
            Assert.IsTrue(a.ApproxEquals(b));
            Assert.IsFalse(a.ApproxEquals(new LngLat(139.7670 + 1e-3, 35.6814)));
        }

        [Test]
        public void Mercator_Equality_IsBitExact_AndHashAgrees()
        {
            var a = new MercatorCoordinate(0.5, 0.5);
            var b = new MercatorCoordinate(0.5, 0.5);
            Assert.IsTrue(a.Equals(b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());

            // One-ULP difference (~1.1e-16 at 0.5). Plain "0.5 + 1e-18" would
            // round back to 0.5 since the addend is below the local ULP --
            // useless as a "different" value. BitConverter gives a portable
            // ULP-step on Unity's .NET Standard 2.0 (no Math.BitIncrement).
            double oneUlpAbove = BitConverter.Int64BitsToDouble(
                BitConverter.DoubleToInt64Bits(0.5) + 1);
            var c = new MercatorCoordinate(oneUlpAbove, 0.5);
            Assert.IsFalse(a.Equals(c));
            Assert.IsTrue(a.ApproxEquals(c));
        }

        // === Mercator <-> LngLat ===

        [Test]
        public void LngLatToMercator_AtOrigin_IsCenterOfMap()
        {
            var m = CoordinateConversion.LngLatToMercator(new LngLat(0.0, 0.0));
            Assert.AreEqual(0.5, m.X, MercEps);
            Assert.AreEqual(0.5, m.Y, MercEps);
        }

        [Test]
        public void LngLatToMercator_AtMaxLatitude_IsTopOfMap()
        {
            // Web Mercator clips at ±MaxLatitude, mapping the north edge to Y=0.
            var m = CoordinateConversion.LngLatToMercator(
                new LngLat(0.0, MapConstants.MaxLatitude));
            Assert.AreEqual(0.5, m.X, MercEps);
            Assert.AreEqual(0.0, m.Y, 1e-9);
        }

        [Test]
        public void LngLatToMercator_AtMinusMaxLatitude_IsBottomOfMap()
        {
            var m = CoordinateConversion.LngLatToMercator(
                new LngLat(0.0, -MapConstants.MaxLatitude));
            Assert.AreEqual(0.5, m.X, MercEps);
            Assert.AreEqual(1.0, m.Y, 1e-9);
        }

        [Test]
        public void LngLatToMercator_AtDateline_HitsMapEdge()
        {
            var east = CoordinateConversion.LngLatToMercator(new LngLat(180.0, 0.0));
            var west = CoordinateConversion.LngLatToMercator(new LngLat(-180.0, 0.0));
            Assert.AreEqual(1.0, east.X, MercEps);
            Assert.AreEqual(0.0, west.X, MercEps);
        }

        [TestCase(139.7670, 35.6814)]    // Tokyo
        [TestCase(-74.0060, 40.7128)]    // New York
        [TestCase(151.2093, -33.8688)]   // Sydney
        [TestCase(-122.4194, 37.7749)]   // San Francisco
        [TestCase(2.3522, 48.8566)]      // Paris
        [TestCase(0.0, 0.0)]             // Equator on prime meridian
        [TestCase(179.999, 84.999)]      // Near top-right
        [TestCase(-179.999, -84.999)]    // Near bottom-left
        public void LngLatToMercator_RoundTrip_PreservesCoordinate(double lng, double lat)
        {
            var input = new LngLat(lng, lat);
            var merc = CoordinateConversion.LngLatToMercator(input);
            var back = CoordinateConversion.MercatorToLngLat(merc);
            Assert.AreEqual(input.Longitude, back.Longitude, LngLatEps);
            Assert.AreEqual(input.Latitude, back.Latitude, LngLatEps);
        }

        // === Tile-coordinate helpers ===

        [Test]
        public void LngLatToTileXY_AtOrigin_AtZ0_IsTileCenter()
        {
            var (tx, ty) = CoordinateConversion.LngLatToTileXY(new LngLat(0.0, 0.0), 0);
            Assert.AreEqual(0.5, tx, 1e-12);
            Assert.AreEqual(0.5, ty, 1e-12);
        }

        [Test]
        public void TileXYToLngLat_Z0Tile00_IsTopLeftOfWorld()
        {
            // The single z=0 tile spans the whole map: NW corner is the
            // longitude-180 / +MaxLatitude corner.
            var nw = CoordinateConversion.TileXYToLngLat(0, 0, 0);
            Assert.AreEqual(-180.0, nw.Longitude, 1e-12);
            Assert.AreEqual(MapConstants.MaxLatitude, nw.Latitude, 1e-9);
        }

        [Test]
        public void TileXYToLngLat_Z0Tile11_IsBottomRightOfWorld()
        {
            var se = CoordinateConversion.TileXYToLngLat(1, 1, 0);
            Assert.AreEqual(180.0, se.Longitude, 1e-12);
            Assert.AreEqual(-MapConstants.MaxLatitude, se.Latitude, 1e-9);
        }

        [Test]
        public void LngLatToTileXY_AndBack_IsConsistent()
        {
            // Tokyo at zoom 12 → fractional tile, NW of containing tile must
            // round-trip through Mercator to the same Mercator point.
            var input = new LngLat(139.7670, 35.6814);
            var (tx, ty) = CoordinateConversion.LngLatToTileXY(input, 12);
            int ix = (int)Math.Floor(tx);
            int iy = (int)Math.Floor(ty);

            // The tile that *contains* the point must be the one whose NW/SE
            // corners bracket the input.
            var (nw, se) = CoordinateConversion.TileBounds(ix, iy, 12);
            Assert.LessOrEqual(nw.Longitude, input.Longitude);
            Assert.GreaterOrEqual(se.Longitude, input.Longitude);
            // NW is the *northern* edge -- latitude is larger there.
            Assert.GreaterOrEqual(nw.Latitude, input.Latitude);
            Assert.LessOrEqual(se.Latitude, input.Latitude);
        }

        [Test]
        public void TileBounds_NorthwestIsNorthOfSoutheast()
        {
            var (nw, se) = CoordinateConversion.TileBounds(3, 5, 4);
            Assert.Less(nw.Longitude, se.Longitude);
            Assert.Greater(nw.Latitude, se.Latitude);
        }

        // === Mercator <-> Unity world ===

        [Test]
        public void MercatorToUnityWorld_AtCenter_IsOrigin()
        {
            var center = new MercatorCoordinate(0.5, 0.5);
            var pos = CoordinateConversion.MercatorToUnityWorld(center, center, 1024f);
            Assert.AreEqual(Vector3.zero, pos);
        }

        [Test]
        public void MercatorToUnityWorld_NorthOfCenter_HasPositiveZ()
        {
            // North = smaller Y in Mercator (top of map). Spec from the doc
            // comment: north = +Z in Unity world.
            var center = new MercatorCoordinate(0.5, 0.5);
            var north = new MercatorCoordinate(0.5, 0.4);
            var pos = CoordinateConversion.MercatorToUnityWorld(north, center, 1024f);
            Assert.AreEqual(0f, pos.x, 1e-4f);
            Assert.AreEqual(0f, pos.y, 1e-4f);
            Assert.Greater(pos.z, 0f);
        }

        [Test]
        public void MercatorToUnityWorld_EastOfCenter_HasPositiveX()
        {
            var center = new MercatorCoordinate(0.5, 0.5);
            var east = new MercatorCoordinate(0.6, 0.5);
            var pos = CoordinateConversion.MercatorToUnityWorld(east, center, 1024f);
            Assert.Greater(pos.x, 0f);
            Assert.AreEqual(0f, pos.z, 1e-4f);
        }

        [Test]
        public void MercatorToUnityWorld_RoundTripThroughUnityWorld()
        {
            var center = new MercatorCoordinate(0.5, 0.5);
            var input = new MercatorCoordinate(0.5123, 0.4876);
            var pos = CoordinateConversion.MercatorToUnityWorld(input, center, 4096f);
            var back = CoordinateConversion.UnityWorldToMercator(pos, center, 4096f);
            // Round-trip goes through float so we allow a sub-pixel epsilon
            // relative to a 4096-unit world.
            Assert.AreEqual(input.X, back.X, 1e-5);
            Assert.AreEqual(input.Y, back.Y, 1e-5);
        }

        [Test]
        public void GetWorldScale_DoublesPerZoomLevel()
        {
            float a = CoordinateConversion.GetWorldScale(5f);
            float b = CoordinateConversion.GetWorldScale(6f);
            Assert.AreEqual(a * 2f, b, 1e-3f);
        }

        // === LngLatBounds ===

        [Test]
        public void Bounds_FromCorners_IsOrderIndependent()
        {
            var p1 = new LngLat(10.0, 20.0);
            var p2 = new LngLat(30.0, 5.0);
            var a = LngLatBounds.FromCorners(p1, p2);
            var b = LngLatBounds.FromCorners(p2, p1);
            Assert.AreEqual(a, b);
            Assert.AreEqual(10.0, a.SouthWest.Longitude, LngLatEps);
            Assert.AreEqual(5.0, a.SouthWest.Latitude, LngLatEps);
            Assert.AreEqual(30.0, a.NorthEast.Longitude, LngLatEps);
            Assert.AreEqual(20.0, a.NorthEast.Latitude, LngLatEps);
        }

        [Test]
        public void Bounds_FromPoints_NoArgs_Throws()
        {
            Assert.Throws<ArgumentException>(() => LngLatBounds.FromPoints());
        }

        [Test]
        public void Bounds_FromPoints_WrapsAllInputs()
        {
            var b = LngLatBounds.FromPoints(
                new LngLat(10, 10),
                new LngLat(-5, 50),
                new LngLat(20, -10));
            Assert.AreEqual(-5, b.SouthWest.Longitude, LngLatEps);
            Assert.AreEqual(-10, b.SouthWest.Latitude, LngLatEps);
            Assert.AreEqual(20, b.NorthEast.Longitude, LngLatEps);
            Assert.AreEqual(50, b.NorthEast.Latitude, LngLatEps);
        }

        [Test]
        public void Bounds_Extend_GrowsToCoverPoint()
        {
            var b = new LngLatBounds(new LngLat(0, 0), new LngLat(10, 10))
                .Extend(new LngLat(-5, 15));
            Assert.AreEqual(-5, b.SouthWest.Longitude, LngLatEps);
            Assert.AreEqual(0, b.SouthWest.Latitude, LngLatEps);
            Assert.AreEqual(10, b.NorthEast.Longitude, LngLatEps);
            Assert.AreEqual(15, b.NorthEast.Latitude, LngLatEps);
        }

        [Test]
        public void Bounds_Extend_PointInside_LeavesUnchanged()
        {
            var b = new LngLatBounds(new LngLat(0, 0), new LngLat(10, 10));
            var b2 = b.Extend(new LngLat(5, 5));
            Assert.AreEqual(b, b2);
        }

        [Test]
        public void Bounds_Contains_IncludesBoundary()
        {
            var b = new LngLatBounds(new LngLat(0, 0), new LngLat(10, 10));
            Assert.IsTrue(b.Contains(new LngLat(0, 0)));
            Assert.IsTrue(b.Contains(new LngLat(10, 10)));
            Assert.IsTrue(b.Contains(new LngLat(5, 5)));
            Assert.IsFalse(b.Contains(new LngLat(-0.1, 0)));
            Assert.IsFalse(b.Contains(new LngLat(0, 10.1)));
        }

        [Test]
        public void Bounds_Center_IsMercatorMidpoint()
        {
            // Symmetric bounds around the equator: Mercator midpoint Y = 0.5,
            // which maps back to lat 0.
            var b = new LngLatBounds(
                new LngLat(-10, -20),
                new LngLat(10, 20));
            var c = b.Center;
            Assert.AreEqual(0.0, c.Longitude, 1e-9);
            Assert.AreEqual(0.0, c.Latitude, 1e-9);
        }

        [Test]
        public void Bounds_ToArray_MatchesGlJsFormat()
        {
            // GL JS LngLatBounds.toArray(): [[west, south], [east, north]]
            var b = new LngLatBounds(new LngLat(1, 2), new LngLat(3, 4));
            var arr = b.ToArray();
            Assert.AreEqual(1.0, arr[0][0], LngLatEps);
            Assert.AreEqual(2.0, arr[0][1], LngLatEps);
            Assert.AreEqual(3.0, arr[1][0], LngLatEps);
            Assert.AreEqual(4.0, arr[1][1], LngLatEps);
        }

        [Test]
        public void Bounds_IsEmpty_WhenCornersCoincide()
        {
            var p = new LngLat(5, 5);
            Assert.IsTrue(new LngLatBounds(p, p).IsEmpty);
            Assert.IsFalse(new LngLatBounds(p, new LngLat(6, 5)).IsEmpty);
        }
    }
}
