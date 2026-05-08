using MapLibre.Unity.Expressions;
using NUnit.Framework;
using UnityEngine;

namespace MapLibre.Unity.Tests.EditMode
{
    /// <summary>
    /// Validates the math in <see cref="ColorSpaceConversion"/>. The point of
    /// these tests is not pixel-perfect equality (CIE colour math + 8-bit
    /// rounding makes that brittle) but rather that:
    /// 1. Conversions round-trip with low loss.
    /// 2. The endpoints of an interpolation reproduce the input colours.
    /// 3. HCL midpoints differ from RGB midpoints in the expected direction --
    ///    "red→blue" stays vivid in HCL (passing through purple) but dips
    ///    through grey in RGB.
    /// </summary>
    public class ColorSpaceConversionTests
    {
        // 1/255 isn't quite enough headroom for sRGB→Lab→sRGB round-trips;
        // we allow ~3 levels of 8-bit drift per channel.
        private const float ChannelTolerance = 3f / 255f;

        private static void AssertColorClose(Color expected, Color actual, float tolerance, string what)
        {
            Assert.AreEqual(expected.r, actual.r, tolerance, $"{what}: r");
            Assert.AreEqual(expected.g, actual.g, tolerance, $"{what}: g");
            Assert.AreEqual(expected.b, actual.b, tolerance, $"{what}: b");
            Assert.AreEqual(expected.a, actual.a, tolerance, $"{what}: a");
        }

        // === Round-trips ===

        [Test]
        public void RgbToLabAndBack_PreservesColor()
        {
            var input = new Color(0.7f, 0.3f, 0.1f, 1f);
            ColorSpaceConversion.RgbToLab(input, out double L, out double a, out double b);
            var roundTripped = ColorSpaceConversion.LabToRgb(L, a, b, input.a);
            AssertColorClose(input, roundTripped, ChannelTolerance, "RGB↔Lab round-trip");
        }

        [Test]
        public void RgbToHclAndBack_PreservesColor()
        {
            var input = new Color(0.2f, 0.6f, 0.9f, 0.5f);
            ColorSpaceConversion.RgbToHcl(input, out double H, out double C, out double L);
            var roundTripped = ColorSpaceConversion.HclToRgb(H, C, L, input.a);
            AssertColorClose(input, roundTripped, ChannelTolerance, "RGB↔HCL round-trip");
        }

        // === Endpoint behaviour ===

        [Test]
        public void LerpLab_AtT0_ReturnsFirstColour()
        {
            var a = Color.red;
            var b = Color.blue;
            var actual = ColorSpaceConversion.LerpLab(a, b, 0f);
            AssertColorClose(a, actual, ChannelTolerance, "LerpLab(t=0)");
        }

        [Test]
        public void LerpLab_AtT1_ReturnsSecondColour()
        {
            var a = Color.red;
            var b = Color.blue;
            var actual = ColorSpaceConversion.LerpLab(a, b, 1f);
            AssertColorClose(b, actual, ChannelTolerance, "LerpLab(t=1)");
        }

        [Test]
        public void LerpHcl_AtT0_ReturnsFirstColour()
        {
            var a = Color.green;
            var b = Color.yellow;
            var actual = ColorSpaceConversion.LerpHcl(a, b, 0f);
            AssertColorClose(a, actual, ChannelTolerance, "LerpHcl(t=0)");
        }

        [Test]
        public void LerpHcl_AtT1_ReturnsSecondColour()
        {
            var a = Color.green;
            var b = Color.yellow;
            var actual = ColorSpaceConversion.LerpHcl(a, b, 1f);
            AssertColorClose(b, actual, ChannelTolerance, "LerpHcl(t=1)");
        }

        // === Alpha channel is interpolated linearly regardless of space ===

        [Test]
        public void LerpLab_InterpolatesAlphaLinearly()
        {
            var a = new Color(1f, 0f, 0f, 0.2f);
            var b = new Color(0f, 0f, 1f, 0.8f);
            var mid = ColorSpaceConversion.LerpLab(a, b, 0.5f);
            Assert.AreEqual(0.5f, mid.a, 0.001f);
        }

        [Test]
        public void LerpHcl_InterpolatesAlphaLinearly()
        {
            var a = new Color(1f, 0f, 0f, 0.2f);
            var b = new Color(0f, 0f, 1f, 0.8f);
            var mid = ColorSpaceConversion.LerpHcl(a, b, 0.5f);
            Assert.AreEqual(0.5f, mid.a, 0.001f);
        }

        // === Perceptual differences from RGB ===

        [Test]
        public void LerpHcl_RedToBlueMidpoint_StaysSaturated()
        {
            // RGB midpoint of red→blue is dark purple-grey: (0.5, 0, 0.5).
            // HCL midpoint travels along the hue circle through magenta and
            // remains visibly saturated. We assert that the HCL midpoint has
            // higher chroma (max(R,B) - min(R,G,B)) than the RGB midpoint.
            var rgbMid = Color.Lerp(Color.red, Color.blue, 0.5f);
            var hclMid = ColorSpaceConversion.LerpHcl(Color.red, Color.blue, 0.5f);

            float rgbChroma = Mathf.Max(rgbMid.r, rgbMid.b) - Mathf.Min(rgbMid.r, Mathf.Min(rgbMid.g, rgbMid.b));
            float hclChroma = Mathf.Max(hclMid.r, hclMid.b) - Mathf.Min(hclMid.r, Mathf.Min(hclMid.g, hclMid.b));

            Assert.Greater(hclChroma, rgbChroma,
                "HCL interpolation should keep red→blue more saturated than RGB lerp");
        }

        [Test]
        public void LerpHcl_HueWrapsShortestArc()
        {
            // 350° → 10° is a 20° arc going forward (through 0°), not a 340°
            // arc going backward. Express it through near-red colours:
            // hue 350 ≈ pinkish-red, hue 10 ≈ orange-red. The midpoint should
            // be very close to pure red (hue 0) -- well within the chromatic
            // span of red, never approaching cyan/teal which would mean the
            // long way around the wheel.
            var pinkishRed = new Color(0.9f, 0.1f, 0.3f);
            var orangeRed = new Color(0.95f, 0.3f, 0.05f);
            var mid = ColorSpaceConversion.LerpHcl(pinkishRed, orangeRed, 0.5f);

            // Red dominates: r > g and r > b is the necessary signature.
            Assert.Greater(mid.r, mid.g, "midpoint should remain red-dominant");
            Assert.Greater(mid.r, mid.b, "midpoint should remain red-dominant");
        }
    }
}
