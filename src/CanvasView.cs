using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LumaRoom
{
    internal enum ToolMode { None = 0, Linear = 1, Radial = 2, Brush = 3 }

    /// <summary>The main image surface: pan/zoom plus on-canvas mask authoring.</summary>
    internal class CanvasView : Control
    {
        public Bitmap Image;
        public MaskLayer ActiveMask;
        public ToolMode Tool = ToolMode.None;

        public float BrushRadius = 0.055f;
        public float BrushHardness = 0.65f;
        public float BrushFlow = 0.85f;
        public bool BrushErase;
        public bool SpaceDown;

        public event EventHandler MaskEdited;
        public event EventHandler EditCommitted;
        public event EventHandler ViewChanged;

        private float _zoom = 1f;
        private bool _fit = true;
        private PointF _pan = PointF.Empty;

        private bool _dragging;
        private bool _panning;
        private Point _lastMouse;
        private PointF _dragStartNorm;
        private bool _hoverMode;

        public CanvasView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = Theme.Canvas;
            TabStop = true;
        }

        public bool IsFit { get { return _fit; } }
        public float Zoom { get { return _zoom; } }

        public void FitToWindow()
        {
            _fit = true;
            _pan = PointF.Empty;
            Invalidate();
            if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
        }

        public void ZoomTo(float z)
        {
            _fit = false;
            _zoom = Math.Max(0.05f, Math.Min(8f, z));
            Invalidate();
            if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
        }

        public void ZoomStep(float factor, Point around)
        {
            float before = EffectiveScale();
            _fit = false;
            _zoom = Math.Max(0.05f, Math.Min(8f, before * factor));
            Invalidate();
            if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
        }

        private float EffectiveScale()
        {
            if (Image == null || Width <= 1 || Height <= 1) return 1f;
            if (_fit) return Math.Min((float)Width / Image.Width, (float)Height / Image.Height);
            return _zoom;
        }

        public RectangleF ImageRect()
        {
            if (Image == null) return RectangleF.Empty;
            float s = EffectiveScale();
            float dw = Image.Width * s, dh = Image.Height * s;
            return new RectangleF((Width - dw) / 2f + _pan.X, (Height - dh) / 2f + _pan.Y, dw, dh);
        }

        public PointF ScreenToNorm(Point p)
        {
            RectangleF r = ImageRect();
            if (r.Width <= 0 || r.Height <= 0) return PointF.Empty;
            return new PointF((p.X - r.X) / r.Width, (p.Y - r.Y) / r.Height);
        }

        public PointF NormToScreen(PointF n)
        {
            RectangleF r = ImageRect();
            return new PointF(r.X + n.X * r.Width, r.Y + n.Y * r.Height);
        }

        // ------------------------------------------------------------- painting
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Canvas);

            if (Image == null)
            {
                Theme.TextCenter(g, "拖入或打开一张照片开始", Theme.F(10f), Theme.TextFaint, ClientRectangle);
                return;
            }

            RectangleF r = ImageRect();
            g.InterpolationMode = EffectiveScale() >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(Image, Rectangle.Round(r));

            using (Pen p = new Pen(Color.FromArgb(60, 60, 60), 1))
                g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);

            DrawMaskHandles(g);
            DrawBrushCursor(g);
        }

        private void DrawMaskHandles(Graphics g)
        {
            if (ActiveMask == null || Tool == ToolMode.None || Tool == ToolMode.Brush) return;
            MaskLayer m = ActiveMask;

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (m.Kind == MaskKind.Linear)
            {
                PointF a = NormToScreen(new PointF(m.X1, m.Y1));
                PointF b = NormToScreen(new PointF(m.X2, m.Y2));
                using (Pen p = new Pen(Color.FromArgb(220, 255, 255, 255), 1.4f))
                {
                    p.DashStyle = DashStyle.Dash;
                    g.DrawLine(p, a, b);
                }
                DrawKnob(g, a, Theme.Accent);
                DrawKnob(g, b, Color.White);
            }
            else if (m.Kind == MaskKind.Radial)
            {
                PointF c = NormToScreen(new PointF(m.X1, m.Y1));
                RectangleF ir = ImageRect();
                float rx = Math.Abs(m.X2 - m.X1) * ir.Width;
                float ry = rx * Math.Max(0.15f, m.Extent);
                using (Pen p = new Pen(Color.FromArgb(220, 255, 255, 255), 1.4f))
                {
                    p.DashStyle = DashStyle.Dash;
                    g.DrawEllipse(p, c.X - rx, c.Y - ry, rx * 2, ry * 2);
                }
                DrawKnob(g, c, Theme.Accent);
                DrawKnob(g, new PointF(c.X + rx, c.Y), Color.White);
            }
            g.SmoothingMode = old;
        }

        private void DrawKnob(Graphics g, PointF p, Color c)
        {
            float r = Theme.Pxf(4.5f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                g.FillEllipse(b, p.X - r, p.Y - r, r * 2, r * 2);
            using (Pen pen = new Pen(c, 2f))
                g.DrawEllipse(pen, p.X - r, p.Y - r, r * 2, r * 2);
        }

        private void DrawBrushCursor(Graphics g)
        {
            if (Tool != ToolMode.Brush || !_hoverMode || Image == null) return;
            Point mp = PointToClient(Cursor.Position);
            RectangleF ir = ImageRect();
            float rr = BrushRadius * ir.Width;
            if (rr < 2) rr = 2;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(Color.FromArgb(200, 255, 255, 255), 1.2f))
                g.DrawEllipse(p, mp.X - rr, mp.Y - rr, rr * 2, rr * 2);
            using (Pen p = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                g.DrawEllipse(p, mp.X - Theme.Pxf(2), mp.Y - Theme.Pxf(2), Theme.Pxf(4), Theme.Pxf(4));
            g.SmoothingMode = old;
        }

        // -------------------------------------------------------------- mouse
        protected override void OnMouseEnter(EventArgs e) { _hoverMode = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hoverMode = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            _lastMouse = e.Location;

            bool panRequest = e.Button == MouseButtons.Middle ||
                              (e.Button == MouseButtons.Left && (SpaceDown || Tool == ToolMode.None));

            if (panRequest)
            {
                _panning = true;
                Cursor = Cursors.SizeAll;
                return;
            }

            if (e.Button != MouseButtons.Left || Image == null) return;

            PointF n = ScreenToNorm(e.Location);
            if (n.X < -0.2f || n.X > 1.2f || n.Y < -0.2f || n.Y > 1.2f) return;

            if (Tool == ToolMode.Linear)
            {
                _dragging = true;
                _dragStartNorm = n;
                ActiveMask.X1 = n.X; ActiveMask.Y1 = n.Y;
                ActiveMask.X2 = n.X; ActiveMask.Y2 = Math.Min(1f, n.Y + 0.25f);
                ActiveMask.Invalidate();
                FireMaskEdited();
            }
            else if (Tool == ToolMode.Radial)
            {
                _dragging = true;
                _dragStartNorm = n;
                ActiveMask.X1 = n.X; ActiveMask.Y1 = n.Y;
                ActiveMask.X2 = Math.Min(1f, n.X + 0.12f); ActiveMask.Y2 = n.Y;
                ActiveMask.Invalidate();
                FireMaskEdited();
            }
            else if (Tool == ToolMode.Brush)
            {
                _dragging = true;
                BrushStroke s = new BrushStroke();
                s.Radius = BrushRadius;
                s.Hardness = BrushHardness;
                s.Flow = BrushFlow;
                s.Erase = BrushErase;
                s.Points.Add(new PointF(Clamp01(n.X), Clamp01(n.Y)));
                ActiveMask.Strokes.Add(s);
                ActiveMask.Invalidate();
                Masks.AppendLatest(ActiveMask, ActiveMask.WeightW, ActiveMask.WeightH);
                FireMaskEdited();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (Tool == ToolMode.Brush && _hoverMode && !_dragging) Invalidate();

            if (_panning)
            {
                _pan.X += e.X - _lastMouse.X;
                _pan.Y += e.Y - _lastMouse.Y;
                _lastMouse = e.Location;
                _fit = false;
                if (_zoom <= 0.05f) _zoom = 0.05f;
                _zoom = EffectiveScale();
                Invalidate();
                if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
                return;
            }

            if (!_dragging || ActiveMask == null) return;
            PointF n = ScreenToNorm(e.Location);
            n = new PointF(Clamp01(n.X), Clamp01(n.Y));

            if (Tool == ToolMode.Linear)
            {
                ActiveMask.X2 = n.X; ActiveMask.Y2 = n.Y;
                ActiveMask.Invalidate();
                FireMaskEdited();
            }
            else if (Tool == ToolMode.Radial)
            {
                float dx = n.X - ActiveMask.X1;
                float dy = n.Y - ActiveMask.Y1;
                float r = (float)Math.Sqrt(dx * dx + dy * dy);
                if (r < 0.01f) r = 0.01f;
                ActiveMask.X2 = ActiveMask.X1 + r;
                ActiveMask.Y2 = ActiveMask.Y1;
                ActiveMask.Extent = Math.Max(0.15f, Math.Abs(dy) / Math.Max(0.001f, r));
                ActiveMask.Invalidate();
                FireMaskEdited();
            }
            else if (Tool == ToolMode.Brush)
            {
                BrushStroke s = ActiveMask.Strokes[ActiveMask.Strokes.Count - 1];
                PointF last = s.Points[s.Points.Count - 1];
                float ir = ImageRect().Width;
                float d = (float)Math.Sqrt(Math.Pow((n.X - last.X) * ir, 2) + Math.Pow((n.Y - last.Y) * ir, 2));
                if (d < 1.5f) return;
                s.Points.Add(n);
                Masks.AppendLatest(ActiveMask, ActiveMask.WeightW, ActiveMask.WeightH);
                FireMaskEdited();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_panning)
            {
                _panning = false;
                Cursor = Cursors.Default;
                return;
            }
            if (_dragging)
            {
                _dragging = false;
                if (EditCommitted != null) EditCommitted(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && Tool == ToolMode.None) FitToWindow();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Image == null) return;
            float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
            ZoomStep(factor, e.Location);
        }

        private void FireMaskEdited()
        {
            Invalidate();
            if (MaskEdited != null) MaskEdited(this, EventArgs.Empty);
        }

        private static float Clamp01(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
    }
}
