using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace LumaRoom
{
    /// <summary>Dark, Lightroom-like visual language.</summary>
    internal static class Theme
    {
        // ---- palette -------------------------------------------------------
        public static readonly Color Bg = Color.FromArgb(28, 28, 28);
        public static readonly Color Panel = Color.FromArgb(38, 38, 38);
        public static readonly Color PanelDeep = Color.FromArgb(32, 32, 32);
        public static readonly Color Header = Color.FromArgb(45, 45, 45);
        public static readonly Color Canvas = Color.FromArgb(18, 18, 18);
        public static readonly Color Line = Color.FromArgb(56, 56, 56);
        public static readonly Color LineSoft = Color.FromArgb(48, 48, 48);
        public static readonly Color Text = Color.FromArgb(205, 205, 205);
        public static readonly Color TextDim = Color.FromArgb(138, 138, 138);
        public static readonly Color TextFaint = Color.FromArgb(96, 96, 96);
        public static readonly Color Accent = Color.FromArgb(74, 158, 255);
        public static readonly Color AccentDim = Color.FromArgb(52, 108, 176);
        public static readonly Color Track = Color.FromArgb(66, 66, 66);
        public static readonly Color Value = Color.FromArgb(120, 190, 255);
        public static readonly Color MaskRed = Color.FromArgb(230, 60, 60);

        // ---- dpi -----------------------------------------------------------
        public static float Scale = 1f;

        public static int Px(int v) { return (int)Math.Round(v * Scale); }
        public static float Pxf(float v) { return v * Scale; }

        public static void InitScale(Control c)
        {
            using (Graphics g = c.CreateGraphics())
                Scale = Math.Max(1f, g.DpiX / 96f);
        }

        // ---- fonts (cached: WinForms leaks GDI handles on repeated new Font) --
        private static readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        private static string _family = "Microsoft YaHei UI";

        public static Font F(float pt, bool bold)
        {
            string key = pt.ToString("0.##") + (bold ? "b" : "");
            Font f;
            if (_fonts.TryGetValue(key, out f)) return f;
            try { f = new Font(_family, pt * Scale, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }
            catch { f = new Font("Segoe UI", pt * Scale, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }
            _fonts[key] = f;
            return f;
        }

        public static Font F(float pt) { return F(pt, false); }

        public static void Shutdown()
        {
            foreach (Font f in _fonts.Values) { try { f.Dispose(); } catch { } }
            _fonts.Clear();
        }

        // ---- helpers -------------------------------------------------------
        public static Color Mix(Color a, Color b, float t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) { p.AddRectangle(r); return p; }
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color c, float w)
        {
            using (GraphicsPath p = Round(r, radius))
            using (Pen pen = new Pen(c, w))
                g.DrawPath(pen, p);
        }

        public static void DrawText(Graphics g, string s, Font f, Color c, float x, float y)
        {
            using (SolidBrush b = new SolidBrush(c))
                g.DrawString(s, f, b, x, y, StringFormat.GenericTypographic);
        }

        public static void TextCenter(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            using (SolidBrush b = new SolidBrush(c))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(s, f, b, r, sf);
            }
        }

        public static void TextRight(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            using (SolidBrush b = new SolidBrush(c))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Far;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(s, f, b, r, sf);
            }
        }

        public static void TextLeft(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            using (SolidBrush b = new SolidBrush(c))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(s, f, b, r, sf);
            }
        }

        public static void Hair(Graphics g, int x1, int y, int x2, Color c)
        {
            using (Pen p = new Pen(c, 1))
                g.DrawLine(p, x1, y, x2, y);
        }

        /// <summary>Soft vertical gradient fill, LRC style headers.</summary>
        public static void GradV(Graphics g, Rectangle r, Color top, Color bottom)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (LinearGradientBrush b = new LinearGradientBrush(
                new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)), top, bottom, 90f))
                g.FillRectangle(b, r);
        }

        /// <summary>A small "aperture" glyph used as the app mark.</summary>
        public static void Logo(Graphics g, Rectangle r)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            float rad = Math.Min(r.Width, r.Height) / 2f - 1;
            using (Pen p = new Pen(Accent, Math.Max(1.4f, 1.4f * Scale)))
                g.DrawEllipse(p, cx - rad, cy - rad, rad * 2, rad * 2);
            using (Pen p = new Pen(Color.FromArgb(120, Accent), Math.Max(1f, 1f * Scale)))
            {
                for (int i = 0; i < 6; i++)
                {
                    double a = i * Math.PI / 3.0;
                    float r0 = rad * 0.32f, r1 = rad * 0.92f;
                    g.DrawLine(p,
                        cx + (float)Math.Cos(a) * r0, cy + (float)Math.Sin(a) * r0,
                        cx + (float)Math.Cos(a) * r1, cy + (float)Math.Sin(a) * r1);
                }
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(190, 230, 255)))
                g.FillEllipse(b, cx - rad * 0.2f, cy - rad * 0.2f, rad * 0.4f, rad * 0.4f);
            g.SmoothingMode = old;
        }
    }
}
