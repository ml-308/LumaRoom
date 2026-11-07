using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LumaRoom
{
    internal class MaskList : Control
    {
        public List<MaskLayer> Items = new List<MaskLayer>();
        public int Selected;
        public event EventHandler SelectionChanged;
        public event EventHandler DeleteRequested;
        private int _hover = -1;

        public int RowH { get { return Theme.Px(26); } }

        public MaskList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            Font = Theme.F(8.8f);
        }

        public int DesiredHeight { get { return Math.Max(RowH, Items.Count * RowH + Theme.Px(4)); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = e.Y / RowH;
            if (idx >= Items.Count) idx = -1;
            if (idx != _hover) { _hover = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int idx = e.Y / RowH;
            if (idx < 0 || idx >= Items.Count) return;

            if (e.Button == MouseButtons.Right || (e.X > Width - Theme.Px(24)))
            {
                if (DeleteRequested != null) DeleteRequested(this, EventArgs.Empty);
                return;
            }
            Selected = idx;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Setup(g);
            g.Clear(Theme.Panel);

            for (int i = 0; i < Items.Count; i++)
            {
                Rectangle r = new Rectangle(Theme.Px(6), i * RowH, Width - Theme.Px(12), RowH - Theme.Px(2));
                bool sel = i == Selected;
                if (sel)
                    Theme.FillRound(g, r, 3, Theme.Mix(Theme.Panel, Theme.Accent, 0.30f));
                else if (i == _hover)
                    Theme.FillRound(g, r, 3, Theme.Mix(Theme.Panel, Color.White, 0.06f));

                MaskLayer m = Items[i];
                Color fg = sel ? Color.White : Theme.Text;

                // kind glyph
                Rectangle gi = new Rectangle(r.X + Theme.Px(7), r.Y + r.Height / 2 - Theme.Px(5), Theme.Px(10), Theme.Px(10));
                using (Pen p = new Pen(sel ? Color.White : Theme.TextDim, 1.2f))
                {
                    if (m.Kind == MaskKind.Linear)
                        g.DrawLine(p, gi.X, gi.Bottom, gi.Right, gi.Y);
                    else if (m.Kind == MaskKind.Radial)
                        g.DrawEllipse(p, gi);
                    else
                        g.DrawEllipse(p, gi.X + 2, gi.Y + 2, gi.Width - 4, gi.Height - 4);
                }

                Theme.TextLeft(g, m.Name, Font, m.Enabled ? fg : Theme.TextFaint,
                               new Rectangle(r.X + Theme.Px(24), r.Y, r.Width - Theme.Px(46), r.Height));
                Theme.TextRight(g, m.Enabled ? "" : "隐藏", Theme.F(7.6f), Theme.TextFaint,
                                new Rectangle(r.Right - Theme.Px(28), r.Y, Theme.Px(24), r.Height));
            }

            if (Items.Count == 0)
                Theme.TextCenter(g, "尚未创建蒙版", Theme.F(8f), Theme.TextFaint, ClientRectangle);
        }
    }

    internal class HistoryList : Control
    {
        public List<HistoryEntry> Items = new List<HistoryEntry>();
        public int Selected = -1;
        public event EventHandler SelectedChanged;
        private int _hover = -1;

        public int RowH { get { return Theme.Px(23); } }

        public HistoryList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            Font = Theme.F(8.6f);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = e.Y / RowH;
            if (idx >= Items.Count) idx = -1;
            if (idx != _hover) { _hover = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int idx = e.Y / RowH;
            if (idx < 0 || idx >= Items.Count) return;
            Selected = idx;
            Invalidate();
            if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Setup(g);
            g.Clear(Theme.Panel);

            // newest first
            int count = Items.Count;
            for (int k = 0; k < count; k++)
            {
                int i = count - 1 - k;
                Rectangle r = new Rectangle(Theme.Px(6), k * RowH, Width - Theme.Px(12), RowH);
                int idx = k;
                bool sel = i == Selected;
                if (sel) Theme.FillRound(g, r, 3, Theme.Mix(Theme.Panel, Theme.Accent, 0.28f));
                else if (idx == _hover) Theme.FillRound(g, r, 3, Theme.Mix(Theme.Panel, Color.White, 0.05f));

                using (SolidBrush b = new SolidBrush(sel ? Color.White : Theme.TextDim))
                    g.FillEllipse(b, r.X + Theme.Px(6), r.Y + r.Height / 2 - Theme.Px(2), Theme.Px(4), Theme.Px(4));

                Theme.TextLeft(g, Items[i].Label, Font, sel ? Color.White : Theme.Text,
                               new Rectangle(r.X + Theme.Px(16), r.Y, r.Width - Theme.Px(20), r.Height));
            }
        }
    }

    internal class FilmStrip : Control
    {
        public List<string> Paths = new List<string>();
        public List<Bitmap> Thumbs = new List<Bitmap>();
        public int Selected = -1;
        public event EventHandler SelectionChanged;

        private int _itemW { get { return Theme.Px(84); } }
        private int _gap { get { return Theme.Px(6); } }
        private int _hover = -1;
        private int _scroll;

        public FilmStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.PanelDeep;
            Font = Theme.F(7.6f);
        }

        public void Add(string path, Bitmap thumb)
        {
            Paths.Add(path);
            Thumbs.Add(thumb);
            Invalidate();
        }

        public void Clear()
        {
            foreach (Bitmap b in Thumbs) if (b != null) b.Dispose();
            Paths.Clear();
            Thumbs.Clear();
            Selected = -1;
            _scroll = 0;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = HitTest(e.X);
            if (idx != _hover) { _hover = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        private int HitTest(int x)
        {
            int rel = x - Theme.Px(8) + _scroll;
            int idx = rel / (_itemW + _gap);
            if (idx < 0 || idx >= Paths.Count) return -1;
            return idx;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int idx = HitTest(e.X);
            if (idx < 0) return;
            Selected = idx;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int total = Paths.Count * (_itemW + _gap) + Theme.Px(16);
            int max = Math.Max(0, total - Width);
            _scroll -= e.Delta / 2;
            if (_scroll < 0) _scroll = 0;
            if (_scroll > max) _scroll = max;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Setup(g);
            g.Clear(Theme.PanelDeep);

            if (Paths.Count == 0)
            {
                Theme.TextCenter(g, "将照片拖到此处，或点击“打开”添加照片", Theme.F(8.6f), Theme.TextFaint, ClientRectangle);
                return;
            }

            int h = Height - Theme.Px(10);
            for (int i = 0; i < Paths.Count; i++)
            {
                int x = Theme.Px(8) + i * (_itemW + _gap) - _scroll;
                if (x > Width || x + _itemW < 0) continue;
                Rectangle r = new Rectangle(x, Theme.Px(5), _itemW, h);

                bool sel = i == Selected;
                Theme.FillRound(g, r, 3, sel ? Theme.Mix(Theme.Panel, Theme.Accent, 0.30f) : Theme.Panel);

                if (i < Thumbs.Count && Thumbs[i] != null)
                {
                    Rectangle ir = new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - Theme.Px(16));
                    float s = Math.Min((float)ir.Width / Thumbs[i].Width, (float)ir.Height / Thumbs[i].Height);
                    int dw = Math.Max(1, (int)(Thumbs[i].Width * s));
                    int dh = Math.Max(1, (int)(Thumbs[i].Height * s));
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(Thumbs[i], new Rectangle(ir.X + (ir.Width - dw) / 2, ir.Y + (ir.Height - dh) / 2, dw, dh));
                }

                if (sel)
                    Theme.DrawRound(g, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), 3, Theme.Accent, 1.4f);

                string name = System.IO.Path.GetFileName(Paths[i]);
                Theme.TextCenter(g, name, Font, sel ? Color.White : Theme.TextDim,
                                 new Rectangle(r.X + 2, r.Bottom - Theme.Px(15), r.Width - 4, Theme.Px(13)));
            }
        }
    }
}
