using UnityEngine;

namespace MapLibre.Unity.Expressions
{
    /// <summary>
    /// Helpers for interpolating between two <see cref="Color"/> values in
    /// perceptually uniform color spaces, mirroring the
    /// <c>interpolate-hcl</c> / <c>interpolate-lab</c> behaviour from the
    /// MapLibre Style Spec. The reference implementation in MapLibre GL JS
    /// uses d3-color, which itself uses CIE LAB / LCH (D65 white point).
    ///
    /// Inputs and outputs are sRGB Unity <see cref="Color"/> in [0, 1] with
    /// alpha channel preserved by the caller (the conversions only touch RGB).
    /// </summary>
    internal static class ColorSpaceConversion
    {
        // sRGB <-> linear RGB. Using the IEC 61966-2-1 piecewise transfer
        // function -- the same one Unity itself applies in linear color space
        // projects, but we run it explicitly because Color values from style
        // JSON are sRGB regardless of the project's color space setting.

        private static float SrgbToLinear(float c)
        {
            return c <= 0.04045f
                ? c / 12.92f
                : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        private static float LinearToSrgb(float c)
        {
            return c <= 0.0031308f
                ? 12.92f * c
                : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;
        }

        // sRGB linear primaries → CIE XYZ (D65). Matrix from
        // https://www.w3.org/TR/css-color-4/#color-conversion-code
        private static void LinearRgbToXyz(float r, float g, float b,
            out double x, out double y, out double z)
        {
            x = 0.41239079926595948 * r + 0.35758433938387796 * g + 0.18048078840183429 * b;
            y = 0.21263900587151036 * r + 0.71516867876775593 * g + 0.07219231536073371 * b;
            z = 0.01933081871559185 * r + 0.11919477979462599 * g + 0.95053215224966398 * b;
        }

        private static void XyzToLinearRgb(double x, double y, double z,
            out float r, out float g, out float b)
        {
            r = (float)(3.24096994190452134 * x - 1.53738317757009346 * y - 0.49861076029300328 * z);
            g = (float)(-0.96924363628087983 * x + 1.87596750150772067 * y + 0.04155505740717561 * z);
            b = (float)(0.05563007969699366 * x - 0.20397695888897652 * y + 1.05697151424287856 * z);
        }

        // CIE LAB constants for D65 white point.
        private const double Xn = 0.95047;
        private const double Yn = 1.0;
        private const double Zn = 1.08883;
        private const double LabDelta = 6.0 / 29.0;
        private const double LabDeltaCubed = LabDelta * LabDelta * LabDelta;
        private const double LabFConstant = 4.0 / 29.0;

        private static double LabF(double t)
        {
            return t > LabDeltaCubed
                ? System.Math.Pow(t, 1.0 / 3.0)
                : t / (3.0 * LabDelta * LabDelta) + LabFConstant;
        }

        private static double LabFInverse(double t)
        {
            return t > LabDelta
                ? t * t * t
                : 3.0 * LabDelta * LabDelta * (t - LabFConstant);
        }

        public static void RgbToLab(Color c, out double L, out double a, out double b)
        {
            float lr = SrgbToLinear(c.r);
            float lg = SrgbToLinear(c.g);
            float lb = SrgbToLinear(c.b);
            LinearRgbToXyz(lr, lg, lb, out double x, out double y, out double z);

            double fx = LabF(x / Xn);
            double fy = LabF(y / Yn);
            double fz = LabF(z / Zn);

            L = 116.0 * fy - 16.0;
            a = 500.0 * (fx - fy);
            b = 200.0 * (fy - fz);
        }

        public static Color LabToRgb(double L, double a, double b, float alpha)
        {
            double fy = (L + 16.0) / 116.0;
            double fx = a / 500.0 + fy;
            double fz = fy - b / 200.0;

            double x = Xn * LabFInverse(fx);
            double y = Yn * LabFInverse(fy);
            double z = Zn * LabFInverse(fz);

            XyzToLinearRgb(x, y, z, out float lr, out float lg, out float lb);
            return new Color(
                Mathf.Clamp01(LinearToSrgb(lr)),
                Mathf.Clamp01(LinearToSrgb(lg)),
                Mathf.Clamp01(LinearToSrgb(lb)),
                alpha);
        }

        // HCL is the cylindrical form of LAB (= LCH with H/C/L axes reordered):
        //   H = atan2(b, a) in degrees [0, 360)
        //   C = sqrt(a² + b²)
        //   L = L
        // Hue is interpolated as a circular value (shortest arc).

        public static void RgbToHcl(Color c, out double H, out double C, out double L)
        {
            RgbToLab(c, out L, out double a, out double b);
            C = System.Math.Sqrt(a * a + b * b);
            H = System.Math.Atan2(b, a) * (180.0 / System.Math.PI);
            if (H < 0.0) H += 360.0;
        }

        public static Color HclToRgb(double H, double C, double L, float alpha)
        {
            double hRad = H * (System.Math.PI / 180.0);
            double a = C * System.Math.Cos(hRad);
            double b = C * System.Math.Sin(hRad);
            return LabToRgb(L, a, b, alpha);
        }

        /// <summary>
        /// Linear interpolation in CIELAB. Each axis interpolates independently;
        /// LAB is already perceptually-uniform so straight-line mixes match
        /// human perception better than RGB mixes (which dip through grey).
        /// </summary>
        public static Color LerpLab(Color a, Color b, float t)
        {
            RgbToLab(a, out double aL, out double aA, out double aB);
            RgbToLab(b, out double bL, out double bA, out double bB);
            double L = aL + (bL - aL) * t;
            double A = aA + (bA - aA) * t;
            double B = aB + (bB - aB) * t;
            float alpha = a.a + (b.a - a.a) * t;
            return LabToRgb(L, A, B, alpha);
        }

        /// <summary>
        /// Interpolation in HCL space. Hue takes the shortest arc around the
        /// 360° wheel, so red→blue goes through purple rather than detouring
        /// through green/yellow. Chroma and Lightness interpolate linearly.
        /// </summary>
        public static Color LerpHcl(Color a, Color b, float t)
        {
            RgbToHcl(a, out double aH, out double aC, out double aL);
            RgbToHcl(b, out double bH, out double bC, out double bL);

            // Shortest-arc hue interpolation.
            double dh = bH - aH;
            if (dh > 180.0) dh -= 360.0;
            else if (dh < -180.0) dh += 360.0;
            double H = aH + dh * t;
            if (H < 0.0) H += 360.0;
            else if (H >= 360.0) H -= 360.0;

            double C = aC + (bC - aC) * t;
            double L = aL + (bL - aL) * t;
            float alpha = a.a + (b.a - a.a) * t;
            return HclToRgb(H, C, L, alpha);
        }
    }
}
