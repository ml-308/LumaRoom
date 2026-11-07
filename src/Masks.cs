using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace LumaRoom
{
    /// <summary>Rasterises mask geometry into a 0..1 weight buffer.</summary>
    internal static class Masks
    {
        public static bool Build(MaskLayer m, int w, int h)
        {
            if (w <= 0 || h <= 0) return false;
            if (m.Weight != null && !m.WeightDirty && m.WeightW == w && m.WeightH == h) return true;
            if (m.Weight == null || m.Weight.Length != w * h) m.Weight = new float[w * h];
            m.WeightW = w; m.WeightH = h;
            Array.Clear(m.Weight, 0, m.Weight.Length);

            switch (m.Kind)
            {
                case MaskKind.Linear: BuildLinear(m, w, h); break;
                case MaskKind.Radial: BuildRadial(m, w, h); break;
                case MaskKind.Brush: BuildBrush(m, w, h); break;
            }

            if (m.Invert)
                for (int i = 0; i < m.Weight.Length; i++) m.Weight[i] = 1f - m.Weight[i];

            m.WeightDirty = false;
            return true;
        }

        public static void Bounds(MaskLayer m, out int x0, out int y0, out int x1, out int y1)
        {
            int w = m.WeightW, h = m.WeightH;
            x0 = 0; y0 = 0; x1 = w; y1 = h;
            if (m.Kind == MaskKind.Radial)
            {
                float rx = Math.Abs(m.X2 - m.X1), ry;
                if (rx < 1e-4f) rx = 0.02f;
                ry = rx * Math.Max(0.2f, m.Extent);
                x0 = (int)Math.Max(0, (m.X1 - rx) * w - 2);
                x1 = (int)Math.Min(w, (m.X1 + rx) * w + 2);
                y0 = (int)Math.Max(0, (m.Y1 - ry) * h - 2);
                y1 = (int)Math.Min(h, (m.Y1 + ry) * h + 2);
            }
            else if (m.Kind == MaskKind.Brush && m.Strokes.Count > 0)
            {
                float minx = 1f, miny = 1f, maxx = 0f, maxy = 0f, rad = 0f;
                foreach (BrushStroke s in m.Strokes)
                {
                    if (s.Radius > rad) rad = s.Radius;
                    foreach (PointF p in s.Points)
                    {
                        if (p.X < minx) minx = p.X;
                        if (p.Y < miny) miny = p.Y;
                        if (p.X > maxx) maxx = p.X;
                        if (p.Y > maxy) maxy = p.Y;
                    }
                }
                float pad = rad * 1.2f;
                x0 = (int)Math.Max(0, (minx - pad) * w);
                x1 = (int)Math.Min(w, (maxx + pad) * w + 1);
                y0 = (int)Math.Max(0, (miny - pad) * h);
                y1 = (int)Math.Min(h, (maxy + pad) * h + 1);
            }
            else if (m.Kind == MaskKind.Linear)
            {
                // gradient can span most of the frame; keep full range
                x0 = 0; y0 = 0; x1 = w; y1 = h;
            }
            if (x1 <= x0) x1 = Math.Min(w, x0 + 1);
            if (y1 <= y0) y1 = Math.Min(h, y0 + 1);
        }

        // ------------------------------------------------------------- linear
        private static void BuildLinear(MaskLayer m, int w, int h)
        {
            float ax = m.X1, ay = m.Y1;
            float bx = m.X2, by = m.Y2;
            float dx = bx - ax, dy = by - ay;
            float len2 = dx * dx + dy * dy;
            if (len2 < 1e-6f) { dx = 0; dy = 1; len2 = 1f; }

            float feather = Math.Max(0.02f, Math.Min(1f, m.Feather));
            float e0 = 0.5f - feather * 0.5f;
            float e1 = 0.5f + feather * 0.5f;

            float[] wt = m.Weight;
            Parallel.For(0, h, delegate(int y)
            {
                float py = (y + 0.5f) / h - ay;
                int ro = y * w;
                for (int x = 0; x < w; x++)
                {
                    float px = (x + 0.5f) / w - ax;
                    float t = (px * dx + py * dy) / len2;
                    wt[ro + x] = Photo.SmoothStep(e0, e1, t);
                }
            });
        }

        // ------------------------------------------------------------- radial
        private static void BuildRadial(MaskLayer m, int w, int h)
        {
            float cx = m.X1 * w, cy = m.Y1 * h;
            float rx = Math.Abs((m.X2 - m.X1)) * w;
            if (rx < 4f) rx = 4f;
            float ry = rx * Math.Max(0.15f, m.Extent);
            if (ry < 4f) ry = 4f;

            float feather = Math.Max(0.02f, Math.Min(1f, m.Feather));
            float inner = 1f - feather;

            float[] wt = m.Weight;
            Parallel.For(0, h, delegate(int y)
            {
                float dy = (y + 0.5f - cy) / ry;
                int ro = y * w;
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f - cx) / rx;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    wt[ro + x] = 1f - Photo.SmoothStep(inner, 1f, d);
                }
            });
        }

        // -------------------------------------------------------------- brush
        private static void BuildBrush(MaskLayer m, int w, int h)
        {
            foreach (BrushStroke s in m.Strokes)
                CompositeStroke(m.Weight, s, w, h);
        }

        /// <summary>Paints one stroke; overlapping segments within a stroke take max,
        /// erasing multiplies down, which keeps repeated passes predictable.</summary>
        public static void CompositeStroke(float[] wt, BrushStroke s, int w, int h)
        {
            if (s == null || s.Points.Count == 0) return;

            List<PointF> pts = s.Points;
            for (int i = 0; i < pts.Count; i++)
            {
                PointF a = pts[i];
                PointF b = (i + 1 < pts.Count) ? pts[i + 1] : pts[i];
                Stamp(wt, a, b, s, w, h, i == 0);
            }
        }

        private static void Stamp(float[] wt, PointF a, PointF b, BrushStroke s, int w, int h, bool first)
        {
            float rad = Math.Max(0.004f, s.Radius);
            float rpx = rad * w;
            if (rpx < 1f) rpx = 1f;

            float ax = a.X * w, ay = a.Y * h;
            float bx = b.X * w, by = b.Y * h;
            float segLen = (float)Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            int steps = Math.Max(1, (int)(segLen / Math.Max(1.5f, rpx * 0.25f)));

            float inner = Math.Max(0.02f, s.Hardness) * 0.94f;
            float flow = Math.Max(0.05f, Math.Min(1f, s.Flow));

            for (int t = 0; t <= steps; t++)
            {
                float f = (float)t / steps;
                float cx = ax + (bx - ax) * f;
                float cy = ay + (by - ay) * f;

                int x0 = (int)Math.Max(0, cx - rpx - 1);
                int x1 = (int)Math.Min(w - 1, cx + rpx + 1);
                int y0 = (int)Math.Max(0, cy - rpx - 1);
                int y1 = (int)Math.Min(h - 1, cy + rpx + 1);

                for (int y = y0; y <= y1; y++)
                {
                    float dy = y + 0.5f - cy;
                    int ro = y * w;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - cx;
                        float d = (float)Math.Sqrt(dx * dx + dy * dy) / rpx;
                        if (d >= 1f) continue;
                        float v = 1f - Photo.SmoothStep(inner, 1f, d);
                        v *= flow;
                        int i = ro + x;
                        if (s.Erase)
                        {
                            float nv = wt[i] * (1f - v);
                            if (nv < wt[i]) wt[i] = nv;
                        }
                        else if (v > wt[i]) wt[i] = v;
                    }
                }
            }
        }

        /// <summary>Fast path used while the user is dragging a fresh brush stroke.</summary>
        public static void AppendLatest(MaskLayer m, int w, int h)
        {
            if (m.Kind != MaskKind.Brush || m.Strokes.Count == 0) return;
            if (m.Weight == null || m.WeightW != w || m.WeightH != h) return;
            BrushStroke s = m.Strokes[m.Strokes.Count - 1];
            CompositeStroke(m.Weight, s, w, h);
        }
    }
}
