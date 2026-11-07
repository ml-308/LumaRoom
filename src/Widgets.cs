using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace LumaRoom
{
    internal static class Gfx
    {
        public static void Setup(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }
    }

    // =====================================================================
    internal class LButton : Control
    {
        public bool Toggled;
        public bool Primary;
        public bool Ghost;
        private bool _hover, _down;
        public int Radius = 3;

        public LButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ForeColor = Theme.Text;
            BackColor = Theme.Panel;
            Cursor = Cursors.Hand;
            Font = Theme.F(9f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Setup(g);
            Rectangle r = new Rectangle(0, 0, Width, Height);

            Color bg;
            bool en = Enabled;
            if (!en)
                bg = Theme.Mix(Theme.Panel, Color.White, 0.015f);
            else if (Primary)
                bg = _down ? Theme.Mix(Theme.Accent, Color.Black, 0.22f) : (_hover ? Theme.Mix(Theme.Accent, Color.White, 0.12f) : Theme.Accent);
            else if (Toggled)
                bg = _down ? Theme.AccentDim : Theme.Mix(Theme.Panel, Theme.Accent, _hover ? 0.42f : 0.32f);
            else if (Ghost && !_hover && !_down)
                bg = Color.Transparent;
            else
                bg = _down ? Theme.Mix(Theme.Panel, Color.Black, 0.25f) : (_hover ? Theme.Mix(Theme.Panel, Color.White, 0.14f) : Theme.Mix(Theme.Panel, Color.White, 0.085f));

            if (bg != Color.Transparent)
                Theme.FillRound(g, r, Radius, bg);
            else
            {
                using (Pen p = new Pen(Theme.Line, 1))
                using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
                    g.DrawPath(p, path);
            }

            if (!Primary && !Toggled && bg != Color.Transparent)
            {
                using (Pen p = new Pen(Theme.Mix(bg, Color.White, 0.12f), 1))
                using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
                    g.DrawPath(p, path);
            }

            if (Toggled && !Primary)
            {
                using (Pen p = new Pen(Theme.Accent, 1))
                using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
                    g.DrawPath(p, path);
            }

            Color fg = !en ? Theme.TextFaint
                            : (Primary ? Color.White : (Toggled ? Color.White : (_hover ? Theme.Text : Theme.TextDim)));
            Theme.TextCenter(g, Text, Font, fg, r);
        }
    }

    // =====================================================================
    /// <summary>Lightroom style slider: label on top, thin track below, value at right.</summary>
    internal class LSlider : Control
    {
        private readonly ParamDef _def;
        private Adjustments _target;
        private bool _drag;
        private bool _hover;
        private bool _leftDown;
        public int RowHeight = 29;

        public event EventHandler ValueChanged;
        public event EventHandler DragStarted;
        public event EventHandler DragEnded;

        public LSlider(ParamDef def, Adjustments target)
        {
            _def = def;
            _target = target == null ? new Adjustments() : target;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.Px(RowHeight);
            BackColor = Theme.Panel;
            Font = Theme.F(8.6f);
        }

        public ParamDef Def { get { return _def; } }
        public Adjustments Target
        {
            get { return _target; }
            set { _target = value == null ? new Adjustments() : value; Invalidate(); }
        }
        public float Value { get { return _def.Get(_target); } }
        public bool IsDefault { get { return Math.Abs(Value - _def.Def) < 0.0005f; } }

        public void SetValue(float v, bool fire)
        {
            if (v < _def.Min) v = _def.Min;
            if (v > _def.Max) v = _def.Max;
            _def.Set(_target, v);
            Invalidate();
            if (fire && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        public void ResetDefault() { SetValue(_def.Def, true); }

        private Rectangle TrackRect
        {
            get
            {
                int pad = Theme.Px(2);
                int top = Height - Theme.Px(11);
                return new Rectangle(pad, top, Math.Max(10, Width - pad * 2), Theme.Px(3));
            }
        }

        private float ValueToX(float v)
        {
            Rectangle t = TrackRect;
            float f = (v - _def.Min) / (_def.Max - _def.Min);
            return t.X + f * t.Width;
        }

        private float XToValue(int x)
        {
            Rectangle t = TrackRect;
            float f = (x - t.X) / (float)Math.Max(1, t.Width);
            if (f < 0) f = 0; else if (f > 1) f = 1;
            float v = _def.Min + f * (_def.Max - _def.Min);
            if (_def.Step >= 1f) v = (float)Math.Round(v);
            else v = (float)Math.Round(v / 0.1f) * 0.1f;
            return v;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                if (e.Clicks == 2)
                {
                    ResetDefault();
                    return;
                }
                _drag = true;
                _leftDown = true;
                Capture = true;
                if (DragStarted != null) DragStarted(this, EventArgs.Empty);
                SetValue(XToValue(e.X), true);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_leftDown && _drag) SetValue(XToValue(e.X), true);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_leftDown)
            {
                _drag = false;
                _leftDown = false;
                Capture = false;
                if (DragEnded != null) DragEnded(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Setup(g);
            g.Clear(Theme.Panel);

            Rectangle t = TrackRect;
            bool def = IsDefault;
            Color label = _hover ? Theme.Text : Theme.TextDim;

            Theme.TextLeft(g, _def.Label, Font, label, new Rectangle(0, 0, Width - Theme.Px(52), Theme.Px(18)));
            Theme.TextRight(g, _def.Format(Value), Font, def ? Theme.TextFaint : Theme.Value,
                            new Rectangle(Width - Theme.Px(52), 0, Theme.Px(52), Theme.Px(18)));

            // track
            using (SolidBrush b = new SolidBrush(Theme.Track))
                g.FillRectangle(b, t);

            float midX = ValueToX(_def.Def);
            float curX = ValueToX(Value);
            Color fill = def ? Theme.Mix(Theme.Track, Color.White, 0.12f) : Theme.Accent;
            if (!def)
            {
                float x0 = Math.Min(midX, curX), x1 = Math.Max(midX, curX);
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillRectangle(b, x0, t.Y, Math.Max(1f, x1 - x0), t.Height);
                using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Track, Color.White, 0.25f)))
                {
                    if (curX > midX) g.FillRectangle(b, midX, t.Y, curX - midX, t.Height);
                    else if (curX < midX) g.FillRectangle(b, curX, t.Y, midX - curX, t.Height);
                }
            }

            // default tick
            using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Track, Color.White, 0.35f)))
                g.FillRectangle(b, midX - 0.5f, t.Y - 1, 1, t.Height + 2);

            // knob
            float r = Theme.Pxf(_hover || _drag ? 5.5f : 4.5f);
            using (SolidBrush b = new SolidBrush(def ? Color.FromArgb(190, 190, 190) : Color.White))
                g.FillEllipse(b, curX - r, t.Y + t.Height / 2f - r, r * 2, r * 2);
            if (_hover || _drag)
            {
                using (Pen p = new Pen(Color.FromArgb(70, Theme.Accent), 2f))
                    g.DrawEllipse(p, curX - r - 3, t.Y + t.Height / 2f - r - 3, (r + 3) * 2, (r + 3) * 2);
            }
        }
    }

    // =====================================================================
    internal class SectionHeader : Control
    {
        public bool Expanded = true;
        public event EventHandler Toggled;
        private bool _hover;

        public SectionHeader(string title)
        {
            Text = title;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.Px(30);
            Font = Theme.F(9.2f, true);
            BackColor = Theme.Panel;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Expanded = !Expanded;
            Invalidate();
            if (Toggled != null) Toggled(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Setup(g);
            g.Clear(Theme.Panel);
            if (_hover) using (SolidBrush b = new SolidBrush(Theme.Mix(Theme.Panel, Color.White, 0.04f))) g.FillRectangle(b, ClientRectangle);

            int cx = Theme.Px(9), cy = Height / 2;
            int s = Theme.Px(4);
            Point[] pts = Expanded
                ? new Point[] { new Point(cx - s, cy - s / 2), new Point(cx + s, cy - s / 2), new Point(cx, cy + s) }
                : new Point[] { new Point(cx - s / 2, cy - s), new Point(cx + s, cy), new Point(cx - s / 2, cy + s) };
            using (SolidBrush b = new SolidBrush(Theme.TextDim))
                g.FillPolygon(b, pts);

            Theme.TextLeft(g, Text, Font, Theme.Text, new Rectangle(Theme.Px(22), 0, Width - Theme.Px(26), Height));
        }
    }

    // =====================================================================
    internal class HistogramView : Control
    {
        private int[] _hist;
        public HistogramView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.PanelDeep;
        }

        public void SetData(int[] hist)
        {
            _hist = hist;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.PanelDeep);
            Rectangle r = ClientRectangle;

            if (_hist == null)
            {
                Theme.TextCenter(g, "直方图", Theme.F(8f), Theme.TextFaint, r);
                return;
            }

            int max = 1;
            for (int i = 0; i < 768; i++) if (_hist[i] > max) max = _hist[i];
            if (max < 10) max = 10;

            int pad = Theme.Px(3);
            Rectangle inner = new Rectangle(r.X + pad, r.Y + pad, r.Width - pad * 2, r.Height - pad * 2);
            if (inner.Width < 8 || inner.Height < 8) return;

            Color[] cols = new Color[] {
                Color.FromArgb(105, 255, 70, 70),
                Color.FromArgb(105, 70, 255, 100),
                Color.FromArgb(115, 80, 130, 255)
            };

            for (int c = 0; c < 3; c++)
            {
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddLine(inner.X, inner.Bottom, inner.X, inner.Bottom);
                    for (int i = 0; i < 256; i++)
                    {
                        float x = inner.X + i / 255f * inner.Width;
                        float h = (float)Math.Sqrt((double)_hist[c * 256 + i] / max) * inner.Height;
                        if (h < 0) h = 0;
                        path.AddLine(x, inner.Bottom, x, inner.Bottom - h);
                    }
                    path.AddLine(inner.Right, inner.Bottom, inner.Right, inner.Bottom);
                    using (SolidBrush b = new SolidBrush(cols[c]))
                        g.FillPath(b, path);
                }
            }
            g.CompositingMode = CompositingMode.SourceOver;

            using (Pen p = new Pen(Theme.LineSoft, 1))
            {
                g.DrawLine(p, inner.X, inner.Bottom, inner.Right, inner.Bottom);
            }
        }
    }

    // =====================================================================
    /// <summary>Vertical scroll host with a hand drawn scrollbar.</summary>
    internal class ScrollHost : Control
    {
        private int _offset;
        private int _contentH;
        private bool _dragBar;
        private int _dragStartY, _dragStartOff;
        private bool _barHover;

        public int Padding2 = 10;

        public ScrollHost()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
        }

        public int ContentHeight { get { return _contentH; } set { _contentH = value; Clamp(); LayoutChildren(); Invalidate(); } }
        public int Offset { get { return _offset; } }

        private int ViewH { get { return Height; } }
        private int MaxOff { get { return Math.Max(0, _contentH - ViewH); } }

        private Rectangle BarRect
        {
            get
            {
                if (MaxOff <= 0) return Rectangle.Empty;
                int bw = Theme.Px(6);
                float frac = (float)ViewH / Math.Max(1, _contentH);
                int bh = Math.Max(Theme.Px(24), (int)(ViewH * frac));
                int by = (int)((float)_offset / MaxOff * (ViewH - bh));
                return new Rectangle(Width - bw - Theme.Px(2), by, bw, bh);
            }
        }

        private void Clamp()
        {
            if (_offset > MaxOff) _offset = MaxOff;
            if (_offset < 0) _offset = 0;
        }

        public void ScrollTo(int off)
        {
            _offset = off;
            Clamp();
            LayoutChildren();
            Invalidate();
        }

        private void LayoutChildren()
        {
            foreach (Control c in Controls)
            {
                int baseTop = (c.Tag is int) ? (int)c.Tag : c.Top;
                c.Tag = baseTop;
                c.Top = baseTop - _offset;
            }
        }

        /// <summary>Registers a child with its absolute scroll position.</summary>
        public void Place(Control c, int x, int y, int w, int h)
        {
            if (!Controls.Contains(c)) Controls.Add(c);
            c.Tag = y;
            c.SetBounds(x, y - _offset, w, h);
            c.Visible = true;
        }

        public void HideAll()
        {
            foreach (Control c in Controls) c.Visible = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (MaxOff > 0)
            {
                ScrollTo(_offset - e.Delta / 3);
                return;   // don't bubble
            }
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Rectangle br = BarRect;
            if (!br.IsEmpty && br.Contains(e.Location))
            {
                _dragBar = true;
                _dragStartY = e.Y;
                _dragStartOff = _offset;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Rectangle br = BarRect;
            bool hover = !br.IsEmpty && br.Contains(e.Location);
            if (hover != _barHover) { _barHover = hover; Invalidate(); }

            if (_dragBar)
            {
                int bh = br.Height;
                int span = Math.Max(1, ViewH - bh);
                int dy = e.Y - _dragStartY;
                ScrollTo(_dragStartOff + (int)((float)dy / span * MaxOff));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragBar = false;
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Panel);
            Rectangle br = BarRect;
            if (!br.IsEmpty)
            {
                using (SolidBrush b = new SolidBrush(_barHover || _dragBar ? Theme.Mix(Theme.Track, Color.White, 0.35f) : Theme.Mix(Theme.Panel, Color.White, 0.18f)))
                    Theme.FillRound(e.Graphics, br, br.Width / 2, b.Color);
            }
        }
    }
}
