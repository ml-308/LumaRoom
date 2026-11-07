using System;
using System.Threading.Tasks;

namespace LumaRoom
{
    /// <summary>Colour space helpers and the tone/colour math shared by preview and export.</summary>
    internal static class Photo
    {
        public const int LUT = 1024;

        private static readonly float[] _s2l = new float[LUT];
        private static readonly float[] _l2s = new float[LUT + 1];

        static Photo()
        {
            for (int i = 0; i < LUT; i++)
            {
                float s = i / (float)(LUT - 1);
                _s2l[i] = s <= 0.04045f ? s / 12.92f : (float)Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            for (int i = 0; i <= LUT; i++)
            {
                float l = i / (float)LUT;
                _l2s[i] = l <= 0.0031308f ? l * 12.92f : 1.055f * (float)Math.Pow(l, 1.0 / 2.4) - 0.055f;
            }
        }

        public static float Srgb2Lin(float s)
        {
            if (s <= 0) return 0;
            if (s >= 1) return 1;
            int i = (int)(s * (LUT - 1) + 0.5f);
            return _s2l[i];
        }

        public static float Lin2Srgb(float l)
        {
            if (l <= 0) return 0;
            if (l >= 1) return 1;
            int i = (int)(l * LUT + 0.5f);
            return _l2s[i];
        }

        public static float Luma(float r, float g, float b)
        {
            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        public static float Clamp01(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        public static float SmoothStep(float e0, float e1, float x)
        {
            if (e1 <= e0) return x >= e1 ? 1f : 0f;
            float t = (x - e0) / (e1 - e0);
            if (t < 0) t = 0; else if (t > 1) t = 1;
            return t * t * (3f - 2f * t);
        }

        // ------------------------------------------------------------ temp/tint
        private static void KelvinRgb(float k, out float r, out float g, out float b)
        {
            if (k < 1500) k = 1500;
            if (k > 20000) k = 20000;
            float t = k / 100f;
            if (t <= 66f) r = 255f;
            else r = 329.698727446f * (float)Math.Pow(t - 60f, -0.1332047592f);
            if (t <= 66f) g = 99.4708025861f * (float)Math.Log(t) - 161.1195681661f;
            else g = 288.1221695283f * (float)Math.Pow(t - 60f, -0.0755148492f);
            if (t >= 66f) b = 255f;
            else if (t <= 19f) b = 0f;
            else b = 138.5177312231f * (float)Math.Log(t - 10f) - 305.0447927307f;
            r = Clamp01(r / 255f); g = Clamp01(g / 255f); b = Clamp01(b / 255f);
        }

        /// <summary>Per channel linear gains for a temperature/tint pair. 0.1..10 safe range.</summary>
        public static void WhiteBalance(float tempK, float tint, out float gr, out float gg, out float gb)
        {
            float br, bg, bb, cr, cg, cb;
            KelvinRgb(5500f, out br, out bg, out bb);
            KelvinRgb(tempK, out cr, out cg, out cb);
            float r = Safe(br / Math.Max(0.02f, cr));
            float g = Safe(bg / Math.Max(0.02f, cg));
            float b = Safe(bb / Math.Max(0.02f, cb));
            float m = g;
            gr = r / m; gg = 1f; gb = b / m;
            float t = tint / 150f;
            gg *= (1f - t * 0.22f);
            gr *= (1f + t * 0.04f);
            gb *= (1f + t * 0.04f);
        }

        private static float Safe(float v)
        {
            if (v < 0.2f) return 0.2f;
            if (v > 5f) return 5f;
            return v;
        }

        // ------------------------------------------------------------------ tone
        /// <summary>The tone response curve, sampled into a LUT by the pipeline.</summary>
        public static float ToneCurve(float y, Adjustments a)
        {
            if (y < 0) y = 0;

            // contrast: smooth S around mid grey
            float c = a.Contrast / 100f;
            if (Math.Abs(c) > 0.0005f)
            {
                float k = Math.Abs(c) * 1.35f;
                float xx = y < 0.5f ? y : 1f - y;
                float bump = (xx == 0f) ? 0f : 4f * xx * (1f - 2f * xx);
                y += (c > 0 ? 1f : -1f) * k * 0.18f * bump;
            }

            // highlights / shadows, weighted by luminance zones
            if (Math.Abs(a.Highlights) > 0.0005f)
            {
                float h = a.Highlights / 100f;
                float w = SmoothStep(0.30f, 1.0f, y);
                y += h * 0.34f * w * (h > 0 ? (1f - y) : y + 0.05f);
            }
            if (Math.Abs(a.Shadows) > 0.0005f)
            {
                float s = a.Shadows / 100f;
                float w = 1f - SmoothStep(0.0f, 0.55f, y);
                y += s * 0.34f * w * (s > 0 ? (1f - y) : y + 0.05f);
            }

            // white / black point
            float wPush = a.Whites / 100f * 0.16f;
            float bPush = a.Blacks / 100f * 0.16f;
            float lo = -bPush;
            float hi = 1f - wPush;
            if (hi - lo > 0.05f) y = (y - lo) / (hi - lo);

            // dehaze == pull the black point in
            float dh = a.Dehaze / 100f;
            if (Math.Abs(dh) > 0.0005f)
            {
                float t = dh * 0.22f;
                y = (y - t) / (1f - t);
            }

            return y;
        }
    }
}
