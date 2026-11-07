using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace LumaRoom
{
    /// <summary>Per-size scratch buffers so dragging a slider does not churn the GC.</summary>
    internal sealed class Scratch
    {
        public int W, H, N;
        private float[] _rgb, _tmp, _tmp2, _a, _b, _c, _d, _y, _cb, _cr;

        // lazily allocated so exporting a huge file only pays for what it uses
        public float[] Rgb { get { if (_rgb == null) _rgb = new float[N * 3]; return _rgb; } }
        public float[] Tmp { get { if (_tmp == null) _tmp = new float[N]; return _tmp; } }
        public float[] Tmp2 { get { if (_tmp2 == null) _tmp2 = new float[N]; return _tmp2; } }
        public float[] A { get { if (_a == null) _a = new float[N]; return _a; } }
        public float[] B { get { if (_b == null) _b = new float[N]; return _b; } }
        public float[] C { get { if (_c == null) _c = new float[N]; return _c; } }
        public float[] D { get { if (_d == null) _d = new float[N]; return _d; } }
        public float[] Y { get { if (_y == null) _y = new float[N]; return _y; } }
        public float[] Cb { get { if (_cb == null) _cb = new float[N]; return _cb; } }
        public float[] Cr { get { if (_cr == null) _cr = new float[N]; return _cr; } }

        public float[] WbLut = new float[3 * Photo.LUT];
        public float[] ToneLut = new float[Photo.LUT + 1];

        private static Scratch _cur;

        public static Scratch For(int w, int h)
        {
            if (_cur != null && _cur.W == w && _cur.H == h) return _cur;
            Scratch s = new Scratch();
            s.W = w; s.H = h; s.N = w * h;
            _cur = s;
            return s;
        }

        public static void Drop()
        {
            _cur = null;
        }
    }

    internal static class Pipeline
    {
        public static int PreviewMaxSide = 1300;

        public static bool Profile = false;
        public static string ProfileText = "";
        private static System.Diagnostics.Stopwatch _pw = new System.Diagnostics.Stopwatch();
        private static System.Text.StringBuilder _pl = new System.Text.StringBuilder();

        private static void Mark(string name)
        {
            if (!Profile) return;
            _pl.Append(name).Append("=").Append(_pw.ElapsedMilliseconds).Append("ms ");
            _pw.Restart();
        }

        // ================================================================ entry
        public static void Render(float[] src, int w, int h, Adjustments adj,
                                  List<MaskLayer> masks, byte[] dstBgra, bool overlayMasks,
                                  int[] hist)
        {
            int n = w * h;
            if (Profile) { _pw.Reset(); _pw.Start(); _pl.Length = 0; }
            Scratch s = Scratch.For(w, h);
            Mark("scratch");

            BuildLuts(adj, s);
            TonePass(src, s.Rgb, w, h, s);
            Mark("tone");

            if (Math.Abs(adj.Clarity) > 0.4f || Math.Abs(adj.Texture) > 0.4f)
                DetailPass(s.Rgb, w, h, adj, s);
            Mark("detail");

            if (Math.Abs(adj.Vibrance) > 0.4f || Math.Abs(adj.Saturation) > 0.4f)
                SatPass(s.Rgb, n, adj);
            Mark("sat");

            if (adj.LumNR > 0.4f || adj.ColorNR > 0.4f)
                DenoisePass(s, adj);
            Mark("denoise");

            if (adj.SharpenAmount > 0.4f)
                SharpenPass(s, adj);
            Mark("sharpen");

            if (masks != null && masks.Count > 0)
                MaskPass(src, s, w, h, adj, masks);
            Mark("mask");

            WriteOut(s.Rgb, n, dstBgra, hist, w, h, masks, overlayMasks);
            Mark("write");

            if (Profile) ProfileText = _pl.ToString();
        }

        // ================================================================= luts
        private static void BuildLuts(Adjustments a, Scratch s)
        {
            float gr, gg, gb;
            Photo.WhiteBalance(a.Temp, a.Tint, out gr, out gg, out gb);
            float ev = (float)Math.Pow(2.0, a.Exposure);
            float[] gains = new float[3] { gr * ev, gg * ev, gb * ev };
            for (int c = 0; c < 3; c++)
            {
                int off = c * Photo.LUT;
                float g = gains[c];
                for (int i = 0; i < Photo.LUT; i++)
                {
                    float srgb = i / (float)(Photo.LUT - 1);
                    s.WbLut[off + i] = Photo.Srgb2Lin(srgb) * g;
                }
            }
            for (int i = 0; i <= Photo.LUT; i++)
                s.ToneLut[i] = Photo.ToneCurve(i / (float)Photo.LUT, a);
        }

        // ============================================================ tone pass
        private static void TonePass(float[] src, float[] dst, int w, int h, Scratch s)
        {
            float[] wb = s.WbLut;
            float[] tl = s.ToneLut;
            const int M = Photo.LUT - 1;

            Parallel.For(0, h, delegate(int y)
            {
                int ro = y * w;
                int si = ro * 3;
                int di = si;
                for (int x = 0; x < w; x++, si += 3, di += 3)
                {
                    int ir = (int)(src[si] * M + 0.5f);
                    int ig = (int)(src[si + 1] * M + 0.5f);
                    int ib = (int)(src[si + 2] * M + 0.5f);
                    if (ir < 0) ir = 0; else if (ir > M) ir = M;
                    if (ig < 0) ig = 0; else if (ig > M) ig = M;
                    if (ib < 0) ib = 0; else if (ib > M) ib = M;

                    float r = wb[ir];
                    float g = wb[Photo.LUT + ig];
                    float b = wb[2 * Photo.LUT + ib];

                    float L = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    float ls = Photo.Lin2Srgb(L);
                    int li = (int)(ls * Photo.LUT + 0.5f);
                    if (li < 0) li = 0; else if (li > Photo.LUT) li = Photo.LUT;
                    float tgt = tl[li];
                    float k = tgt / (ls > 0.0015f ? ls : 0.0015f);

                    dst[di] = Photo.Lin2Srgb(r * k);
                    dst[di + 1] = Photo.Lin2Srgb(g * k);
                    dst[di + 2] = Photo.Lin2Srgb(b * k);
                }
            });
        }

        // ========================================================== detail pass
        private static void DetailPass(float[] rgb, int w, int h, Adjustments a, Scratch s)
        {
            int n = w * h;
            float[] y = s.Y;
            Parallel.For(0, h, delegate(int yy)
            {
                int ro = yy * w;
                for (int x = 0; x < w; x++)
                {
                    int i = ro + x, si = i * 3;
                    y[i] = 0.2126f * rgb[si] + 0.7152f * rgb[si + 1] + 0.0722f * rgb[si + 2];
                }
            });

            float[] big = s.A;
            float[] small = s.B;
            int rc = Math.Max(4, (int)(Math.Min(w, h) * 0.018f));
            int rt = 2;

            float[] tmp = s.C;
            if (Math.Abs(a.Clarity) > 0.4f)
            {
                BoxBlur(y, big, tmp, w, h, rc);
                BoxBlur(big, big, tmp, w, h, 2);
            }
            if (Math.Abs(a.Texture) > 0.4f)
                BoxBlur(y, small, tmp, w, h, rt);

            float kc = a.Clarity / 100f * 1.15f;
            float kt = a.Texture / 100f * 1.25f;
            bool doC = Math.Abs(a.Clarity) > 0.4f;
            bool doT = Math.Abs(a.Texture) > 0.4f;

            Parallel.For(0, h, delegate(int yy)
            {
                int ro = yy * w;
                for (int x = 0; x < w; x++)
                {
                    int i = ro + x;
                    float d = 0f;
                    if (doC) d += (y[i] - big[i]) * kc;
                    if (doT) d += (y[i] - small[i]) * kt;
                    if (d == 0f) continue;
                    int si = i * 3;
                    rgb[si] = Clamp01(rgb[si] + d);
                    rgb[si + 1] = Clamp01(rgb[si + 1] + d);
                    rgb[si + 2] = Clamp01(rgb[si + 2] + d);
                }
            });
        }

        // ============================================================= sat pass
        private static void SatPass(float[] rgb, int n, Adjustments a)
        {
            float sat = a.Saturation / 100f;
            float vib = a.Vibrance / 100f;

            Parallel.For(0, n, delegate(int i)
            {
                int si = i * 3;
                float r = rgb[si], g = rgb[si + 1], b = rgb[si + 2];
                float mx = Math.Max(r, Math.Max(g, b));
                float mn = Math.Min(r, Math.Min(g, b));
                float cur = mx <= 0.0001f ? 0f : (mx - mn) / mx;

                float f = 1f + sat;
                if (Math.Abs(vib) > 0.0005f)
                {
                    float soft = 1f - cur;
                    f += vib * soft * 0.9f;
                }
                if (f < 0f) f = 0f;

                float L = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                rgb[si] = Clamp01(L + (r - L) * f);
                rgb[si + 1] = Clamp01(L + (g - L) * f);
                rgb[si + 2] = Clamp01(L + (b - L) * f);
            });
        }

        // ========================================================= denoise pass
        private static void DenoisePass(Scratch s, Adjustments a)
        {
            int w = s.W, h = s.H, n = s.N;
            float[] rgb = s.Rgb;
            float[] Y = s.Y, Cb = s.Cb, Cr = s.Cr;

            Parallel.For(0, h, delegate(int y)
            {
                int ro = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = ro + x, si = i * 3;
                    float r = rgb[si], g = rgb[si + 1], b = rgb[si + 2];
                    float yy = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    Y[i] = yy;
                    Cb[i] = b - yy;
                    Cr[i] = r - yy;
                }
            });

            // --- luminance: edge preserving smoothing
            if (a.LumNR > 0.4f)
            {
                float strength = a.LumNR / 100f;
                int r = 1 + (int)(strength * 5f);
                float eps = (0.006f + strength * 0.055f);
                eps = eps * eps * 12f;
                float[] q = s.A;
                GuidedFilter(Y, q, s.C, s.D, s.Tmp, s.Tmp2, w, h, r, eps);

                float keep = 1f - strength * (1f - a.LumNRDetail / 200f);
                if (keep < 0.05f) keep = 0.05f;
                if (keep > 1f) keep = 1f;

                // local contrast restore
                float lc = a.LumNRContrast / 100f;

                Parallel.For(0, h, delegate(int y)
                {
                    int ro = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        int i = ro + x;
                        float basev = q[i];
                        float det = Y[i] - basev;
                        float outv = basev + det * keep;
                        if (lc > 0.001f)
                        {
                            // re-add a portion of micro contrast
                            outv += det * lc * 0.5f;
                        }
                        Y[i] = outv;
                    }
                });
            }

            // --- chroma: big radius chroma blur, this is what kills colour speckle
            if (a.ColorNR > 0.4f)
            {
                float st = a.ColorNR / 100f;
                int r = 1 + (int)(st * 7f);
                float[] tb = s.B, t2 = s.Tmp, cb2 = s.C;
                float kc = st * (1f - a.ColorNRDetail / 260f);
                if (kc > 1f) kc = 1f;

                BoxBlur(Cb, tb, t2, w, h, r);
                BoxBlur(Cr, cb2, t2, w, h, r);
                Parallel.For(0, n, delegate(int i)
                {
                    Cb[i] = Cb[i] * (1f - kc) + tb[i] * kc;
                    Cr[i] = Cr[i] * (1f - kc) + cb2[i] * kc;
                });

                if (a.ColorNR > 38f)
                {
                    float mk = (a.ColorNR - 38f) / 62f * 0.85f;
                    Median3(Cb, cb2, w, h, mk);
                    Median3(Cr, cb2, w, h, mk);
                }
            }

            Parallel.For(0, h, delegate(int y)
            {
                int ro = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = ro + x, si = i * 3;
                    float yy = Y[i];
                    float cb = Cb[i], cr = Cr[i];
                    float r = yy + cr;
                    float g = yy - 0.2126f / 0.7152f * cr - 0.0722f / 0.7152f * cb;
                    float b = yy + cb;
                    rgb[si] = Clamp01(r);
                    rgb[si + 1] = Clamp01(g);
                    rgb[si + 2] = Clamp01(b);
                }
            });
        }

        private static void Sum(float[] dst, float[] blur, float k)
        {
            int n = dst.Length;
            Parallel.For(0, n, delegate(int i)
            {
                dst[i] = dst[i] * (1f - k) + blur[i] * k;
            });
        }

        // =========================================================== sharpen pass
        private static void SharpenPass(Scratch s, Adjustments a)
        {
            int w = s.W, h = s.H;
            int n = s.N;
            float[] rgb = s.Rgb;
            float[] Y = s.Y;

            Parallel.For(0, h, delegate(int y)
            {
                int ro = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = ro + x, si = i * 3;
                    Y[i] = 0.2126f * rgb[si] + 0.7152f * rgb[si + 1] + 0.0722f * rgb[si + 2];
                }
            });

            float[] blur = s.B;
            int r = Math.Max(1, (int)Math.Round(a.SharpenRadius));
            BoxBlur(Y, blur, s.Tmp, w, h, r);

            float amt = a.SharpenAmount / 100f * 1.4f;
            float thr = (1f - a.SharpenDetail / 100f) * 0.030f + 0.002f;

            Parallel.For(0, h, delegate(int y)
            {
                int ro = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = ro + x;
                    float e = Y[i] - blur[i];
                    float ae = Math.Abs(e);
                    if (ae < thr) continue;
                    float wgt = (ae - thr) / (ae + 0.0001f);
                    float d = e * amt * wgt;
                    int si = i * 3;
                    rgb[si] = Clamp01(rgb[si] + d);
                    rgb[si + 1] = Clamp01(rgb[si + 1] + d);
                    rgb[si + 2] = Clamp01(rgb[si + 2] + d);
                }
            });
        }

        // ============================================================== mask pass
        private static void MaskPass(float[] src, Scratch s, int w, int h, Adjustments global, List<MaskLayer> masks)
        {
            float[] local = new float[w * h * 3];

            foreach (MaskLayer m in masks)
            {
                if (!m.Enabled) continue;
                if (!Masks.Build(m, w, h)) continue;

                Adjustments la = global.Clone();
                la.Exposure += m.Adjust.Exposure;
                la.Contrast += m.Adjust.Contrast;
                la.Highlights += m.Adjust.Highlights;
                la.Shadows += m.Adjust.Shadows;
                la.Whites += m.Adjust.Whites;
                la.Blacks += m.Adjust.Blacks;
                la.Temp += (m.Adjust.Temp - 5500f) * 0.5f;
                la.Tint += m.Adjust.Tint * 0.5f;
                la.Saturation += m.Adjust.Saturation;
                la.Vibrance += m.Adjust.Vibrance;
                la.Clarity += m.Adjust.Clarity * 0.6f;
                if (la.Temp < 2000f) la.Temp = 2000f;
                if (la.Temp > 12000f) la.Temp = 12000f;

                Scratch ls = new Scratch();
                ls.W = w; ls.H = h; ls.N = w * h;
                ls.WbLut = new float[3 * Photo.LUT];
                ls.ToneLut = new float[Photo.LUT + 1];
                BuildLuts(la, ls);
                TonePass(src, local, w, h, ls);

                float[] mw = m.Weight;
                int x0 = 0, y0 = 0, x1 = w, y1 = h;
                Masks.Bounds(m, out x0, out y0, out x1, out y1);

                Parallel.For(y0, y1, delegate(int y)
                {
                    int ro = y * w;
                    for (int x = x0; x < x1; x++)
                    {
                        int i = ro + x;
                        float t = mw[i];
                        if (t <= 0.001f) continue;
                        int si = i * 3;
                        float inv = 1f - t;
                        s.Rgb[si] = s.Rgb[si] * inv + local[si] * t;
                        s.Rgb[si + 1] = s.Rgb[si + 1] * inv + local[si + 1] * t;
                        s.Rgb[si + 2] = s.Rgb[si + 2] * inv + local[si + 2] * t;
                    }
                });
            }
        }

        // =============================================================== output
        private static void WriteOut(float[] rgb, int n, byte[] dst, int[] hist, int w, int h,
                                     List<MaskLayer> masks, bool overlay)
        {
            bool doOverlay = overlay && masks != null;
            float[] ov = null;
            if (doOverlay)
            {
                ov = new float[n];
                bool any = false;
                foreach (MaskLayer m in masks)
                {
                    if (!m.Enabled || !m.ShowOverlay || m.Weight == null) continue;
                    any = true;
                    for (int i = 0; i < n; i++) if (m.Weight[i] > ov[i]) ov[i] = m.Weight[i];
                }
                if (!any) ov = null;
            }

            int cols = w;
            Parallel.For(0, h, delegate(int y)
            {
                int ro = y * cols;
                for (int x = 0; x < cols; x++)
                {
                    int i = ro + x, si = i * 3, di = i * 4;
                    float r = rgb[si], g = rgb[si + 1], b = rgb[si + 2];
                    if (ov != null)
                    {
                        float t = ov[i] * 0.55f;
                        if (t > 0.002f)
                        {
                            r = r * (1f - t) + 0.92f * t;
                            g = g * (1f - t) + 0.12f * t;
                            b = b * (1f - t) + 0.12f * t;
                        }
                    }
                    dst[di] = (byte)(Clamp01(b) * 255f + 0.5f);
                    dst[di + 1] = (byte)(Clamp01(g) * 255f + 0.5f);
                    dst[di + 2] = (byte)(Clamp01(r) * 255f + 0.5f);
                    dst[di + 3] = 255;
                }
            });

            if (hist != null)
            {
                for (int i = 0; i < hist.Length; i++) hist[i] = 0;
                int stride = 1;
                if (n > 900000) stride = 2;
                for (int i = 0; i < n; i += stride)
                {
                    int di = i * 4;
                    hist[dst[di + 2]]++;
                    hist[256 + dst[di + 1]]++;
                    hist[512 + dst[di]]++;
                }
            }
        }

        public static float Clamp01(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        // =========================================================== blur utils
        public static void BoxBlur(float[] src, float[] dst, float[] tmp, int w, int h, int r)
        {
            if (r < 1) { Array.Copy(src, dst, w * h); return; }
            BlurH(src, tmp, w, h, r);
            BlurV(tmp, dst, w, h, r);
        }

        private static void BlurH(float[] s, float[] d, int w, int h, int r)
        {
            float inv = 1f / (2 * r + 1);
            Parallel.For(0, h, delegate(int y)
            {
                int ro = y * w;
                float sum = 0f;
                for (int k = -r; k <= r; k++)
                {
                    int xx = k < 0 ? 0 : (k > w - 1 ? w - 1 : k);
                    sum += s[ro + xx];
                }
                for (int x = 0; x < w; x++)
                {
                    d[ro + x] = sum * inv;
                    int add = x + r + 1; if (add > w - 1) add = w - 1;
                    int rem = x - r; if (rem < 0) rem = 0;
                    sum += s[ro + add] - s[ro + rem];
                }
            });
        }

        private static void BlurV(float[] s, float[] d, int w, int h, int r)
        {
            float inv = 1f / (2 * r + 1);
            Parallel.For(0, w, delegate(int x)
            {
                float sum = 0f;
                for (int k = -r; k <= r; k++)
                {
                    int yy = k < 0 ? 0 : (k > h - 1 ? h - 1 : k);
                    sum += s[yy * w + x];
                }
                for (int y = 0; y < h; y++)
                {
                    d[y * w + x] = sum * inv;
                    int add = y + r + 1; if (add > h - 1) add = h - 1;
                    int rem = y - r; if (rem < 0) rem = 0;
                    sum += s[add * w + x] - s[rem * w + x];
                }
            });
        }

        /// <summary>Edge preserving filter (He et al.) used for luminance noise reduction.</summary>
        public static void GuidedFilter(float[] I, float[] q, float[] a, float[] b, float[] t1, float[] t2,
                                        int w, int h, int r, float eps)
        {
            int n = w * h;
            BoxBlur(I, a, t1, w, h, r);                    // mean_I

            Parallel.For(0, n, delegate(int i) { float v = I[i]; t2[i] = v * v; });
            BoxBlur(t2, b, t1, w, h, r);                   // mean_II

            Parallel.For(0, n, delegate(int i)
            {
                float m = a[i];
                float var = b[i] - m * m;
                if (var < 0f) var = 0f;
                float ai = var / (var + eps);
                a[i] = ai;
                b[i] = m - ai * m;                          // (1-ai)*m
            });

            BoxBlur(a, t2, t1, w, h, r);                   // mean_a
            BoxBlur(b, q, t1, w, h, r);                    // mean_b
            Parallel.For(0, n, delegate(int i)
            {
                float v = t2[i] * I[i] + q[i];
                q[i] = v;
            });
        }

        private static void Median3(float[] src, float[] dst, int w, int h, float k)
        {
            Parallel.For(0, h, delegate(int y)
            {
                int y0 = y > 0 ? y - 1 : 0;
                int y1 = y < h - 1 ? y + 1 : h - 1;
                int r0 = y0 * w, r1 = y * w, r2 = y1 * w;
                for (int x = 0; x < w; x++)
                {
                    int x0 = x > 0 ? x - 1 : 0;
                    int x1 = x < w - 1 ? x + 1 : w - 1;
                    float a = src[r0 + x0], b = src[r0 + x], c = src[r0 + x1];
                    float d = src[r1 + x0], e = src[r1 + x], f = src[r1 + x1];
                    float g = src[r2 + x0], hh = src[r2 + x], i = src[r2 + x1];
                    float t;

                    // 25 comparison median-of-9 network (no allocation)
                    if (a > b) { t = a; a = b; b = t; }
                    if (d > e) { t = d; d = e; e = t; }
                    if (g > hh) { t = g; g = hh; hh = t; }
                    if (b > c) { t = b; b = c; c = t; }
                    if (e > f) { t = e; e = f; f = t; }
                    if (hh > i) { t = hh; hh = i; i = t; }
                    if (a > b) { t = a; a = b; b = t; }
                    if (d > e) { t = d; d = e; e = t; }
                    if (g > hh) { t = g; g = hh; hh = t; }
                    if (b > c) { t = b; b = c; c = t; }
                    if (e > f) { t = e; e = f; f = t; }
                    if (hh > i) { t = hh; hh = i; i = t; }
                    if (a > d) { t = a; a = d; d = t; }
                    if (b > e) { t = b; b = e; e = t; }
                    if (c > f) { t = c; c = f; f = t; }
                    if (e > hh) { t = e; e = hh; hh = t; }
                    if (f > i) { t = f; f = i; i = t; }
                    if (b > d) { t = b; b = d; d = t; }
                    if (c > e) { t = c; c = e; e = t; }
                    if (e > g) { t = e; e = g; g = t; }
                    if (f > hh) { t = f; f = hh; hh = t; }
                    if (c > d) { t = c; c = d; d = t; }
                    if (e > f) { t = e; e = f; f = t; }
                    if (d > e) { t = d; d = e; e = t; }

                    dst[r1 + x] = src[r1 + x] * (1f - k) + e * k;
                }
            });
        }

        // ========================================================= noise estimate
        /// <summary>Robust estimate of luminance noise from flat-ish regions.</summary>
        public static float EstimateNoise(float[] src, int w, int h)
        {
            if (w < 8 || h < 8) return 0f;
            List<float> samples = new List<float>(20000);
            int stepX = Math.Max(2, w / 160);
            int stepY = Math.Max(2, h / 160);

            for (int y = 1; y < h - 1; y += stepY)
            {
                for (int x = 1; x < w - 1; x += stepX)
                {
                    int i = y * w + x;
                    int si = i * 3;
                    float c = Luma(src, si);
                    float l = Luma(src, si - 3);
                    float r = Luma(src, si + 3);
                    float u = Luma(src, si - w * 3);
                    float d = Luma(src, si + w * 3);
                    float lap = Math.Abs(4f * c - l - r - u - d);
                    // only near-flat pixels: real edges would inflate this
                    float gx = Math.Abs(r - l), gy = Math.Abs(d - u);
                    if (gx < 0.05f && gy < 0.05f) samples.Add(lap);
                }
            }
            if (samples.Count < 50) return 0f;
            samples.Sort();
            // 25th percentile of |laplacian| for pure gaussian noise is ~1.42 sigma
            float q25 = samples[(int)(samples.Count * 0.25)];
            return q25 / 1.42f;
        }

        private static float Luma(float[] src, int si)
        {
            return 0.2126f * src[si] + 0.7152f * src[si + 1] + 0.0722f * src[si + 2];
        }
    }
}
