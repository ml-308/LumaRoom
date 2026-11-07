using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LumaRoom
{
    internal class MainForm : Form
    {
        // ---- documents
        private List<ImageDoc> _docs = new List<ImageDoc>();
        private int _docIndex = -1;
        private ImageDoc _doc;
        private Bitmap _renderBmp;
        private Bitmap _navBmp;

        // ---- chrome
        private Panel _top, _left, _right, _stage, _filmPanel;
        private CanvasView _canvas;
        private HistogramView _hist;
        private ScrollHost _scroll;
        private FilmStrip _film;
        private HistoryList _history;
        private MaskList _maskList;
        private PictureBox _nav;
        private Label _status;

        private SectionHeader _secBasic, _secDetail, _secMask;
        private List<LSlider> _basicSliders = new List<LSlider>();
        private List<LSlider> _detailSliders = new List<LSlider>();
        private List<LSlider> _maskSliders = new List<LSlider>();
        private List<LSlider> _brushSliders = new List<LSlider>();

        private LButton _btnOpen, _btnExport, _btnAuto, _btnCompare, _btnReset;
        private LButton _btnAutoNR, _btnMaskLinear, _btnMaskRadial, _btnMaskBrush, _btnMaskErase, _btnMaskDel;

        private int _maskIndex = -1;
        private bool _showOriginal;

        // ---- render scheduling
        private bool _rendering;
        private bool _pending;
        private int _seq;
        private volatile bool _closing;

        // brush settings live outside Adjustments
        private float _brushRadius = 0.05f;
        private float _brushHardness = 0.7f;
        private float _brushFlow = 0.9f;

        /// <summary>Files handed over on the command line, loaded after the window shows.</summary>
        public string[] PendingFiles;

        /// <summary>Fills in a sample edit so a screenshot can show the develop panels.</summary>
        public bool DemoMode;

        /// <summary>Command line export target, used for head-less verification.</summary>
        public string AutoExportPath;

        /// <summary>Quit right after a command line export.</summary>
        public bool CloseAfterExport;

        public MainForm()
        {
            Theme.InitScale(this);
            Text = "LumaRoom — 轻量图像暗房";
            StartPosition = FormStartPosition.CenterScreen;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int ww = Math.Min(Theme.Px(1480), (int)(wa.Width * 0.94f));
            int wh = Math.Min(Theme.Px(900), (int)(wa.Height * 0.94f));
            Size = new Size(ww, wh);
            MinimumSize = new Size(Math.Min(Theme.Px(1120), ww), Math.Min(Theme.Px(700), wh));
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.F(9f);
            KeyPreview = true;
            AllowDrop = true;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();

            DragEnter += new DragEventHandler(OnDragEnter);
            DragDrop += new DragEventHandler(OnDragDrop);
            FormClosing += delegate(object s, FormClosingEventArgs e) { _closing = true; };
            Resize += delegate { LayoutChrome(); };
            Shown += delegate
            {
                LayoutChrome();
                if (PendingFiles != null && PendingFiles.Length > 0)
                {
                    string[] f = PendingFiles;
                    PendingFiles = null;
                    LoadFiles(f);
                }
                else if (_docs.Count == 0) RequestRender();

                if (DemoMode && _doc != null) ApplyDemo();

                if (!string.IsNullOrEmpty(AutoExportPath))
                {
                    bool ok = _doc != null && ExportTo(AutoExportPath);
                    try
                    {
                        System.IO.File.WriteAllText(AutoExportPath + ".log",
                            "ok=" + ok + "\r\n" + LastExportMessage + "\r\n");
                    }
                    catch { }
                    if (CloseAfterExport) BeginInvoke((MethodInvoker)delegate { Close(); });
                }
            };
        }

        // ================================================================= UI
        private void BuildUi()
        {
            _stage = new Panel();
            _stage.Dock = DockStyle.Fill;
            _stage.BackColor = Theme.Canvas;

            _top = new Panel();
            _top.Dock = DockStyle.Top;
            _top.Height = Theme.Px(46);
            _top.BackColor = Theme.PanelDeep;
            _top.Paint += PaintTop;
            _top.Resize += delegate { LayoutTopBar(); };

            _left = new Panel();
            _left.Dock = DockStyle.Left;
            _left.Width = Theme.Px(232);
            _left.BackColor = Theme.Panel;
            _left.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Line, 1)) e.Graphics.DrawLine(p, _left.Width - 1, 0, _left.Width - 1, _left.Height);
            };

            _right = new Panel();
            _right.Dock = DockStyle.Right;
            _right.Width = Theme.Px(344);
            _right.BackColor = Theme.Panel;
            _right.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Line, 1)) e.Graphics.DrawLine(p, 0, 0, 0, _right.Height);
            };

            _filmPanel = new Panel();
            _filmPanel.Dock = DockStyle.Bottom;
            _filmPanel.Height = Theme.Px(98);
            _filmPanel.BackColor = Theme.PanelDeep;
            _filmPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Line, 1)) e.Graphics.DrawLine(p, 0, 0, _filmPanel.Width, 0);
            };

            Controls.Add(_stage);
            Controls.Add(_left);
            Controls.Add(_right);
            Controls.Add(_filmPanel);
            Controls.Add(_top);

            // ---- canvas
            _canvas = new CanvasView();
            _canvas.Dock = DockStyle.Fill;
            _canvas.MaskEdited += delegate { RequestRender(); };
            _canvas.EditCommitted += delegate { CommitHistory(MaskHistoryLabel()); };
            _stage.Controls.Add(_canvas);

            _status = new Label();
            _status.AutoSize = false;
            _status.Height = Theme.Px(20);
            _status.Dock = DockStyle.Bottom;
            _status.BackColor = Theme.Canvas;
            _status.ForeColor = Theme.TextFaint;
            _status.Font = Theme.F(8f);
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(Theme.Px(10), 0, 0, 0);
            _status.Text = "";
            _stage.Controls.Add(_status);

            // ---- top bar buttons
            _btnOpen = MkTopBtn("打开照片", Theme.Px(84), delegate { OpenDialog(); });
            _btnOpen.Primary = true;
            _btnExport = MkTopBtn("导出", Theme.Px(64), delegate { ExportDialog(); });
            _btnCompare = MkTopBtn("对比", Theme.Px(56), delegate { ToggleCompare(); });
            _btnReset = MkTopBtn("复位", Theme.Px(56), delegate { ResetAll(); });
            _top.Controls.Add(_btnOpen);
            _top.Controls.Add(_btnExport);
            _top.Controls.Add(_btnCompare);
            _top.Controls.Add(_btnReset);

            // ---- left: navigator + history
            Label navTitle = new Label();
            navTitle.Text = "导航器";
            navTitle.Font = Theme.F(8.4f);
            navTitle.ForeColor = Theme.TextDim;
            navTitle.SetBounds(Theme.Px(12), Theme.Px(20), Theme.Px(120), Theme.Px(16));
            _left.Controls.Add(navTitle);

            Panel navWrap = new Panel();
            navWrap.SetBounds(Theme.Px(12), Theme.Px(40), Theme.Px(208), Theme.Px(146));
            navWrap.BackColor = Theme.PanelDeep;
            navWrap.Paint += delegate(object s, PaintEventArgs e)
            {
                Gfx.Setup(e.Graphics);
                Theme.DrawRound(e.Graphics, new Rectangle(0, 0, navWrap.Width - 1, navWrap.Height - 1), 3, Theme.Line, 1);
            };
            _nav = new PictureBox();
            _nav.SetBounds(Theme.Px(6), Theme.Px(6), Theme.Px(196), Theme.Px(134));
            _nav.SizeMode = PictureBoxSizeMode.Zoom;
            _nav.BackColor = Theme.PanelDeep;
            navWrap.Controls.Add(_nav);
            _left.Controls.Add(navWrap);

            _history = new HistoryList();
            _history.SetBounds(Theme.Px(12), Theme.Px(212), Theme.Px(208), Theme.Px(400));
            _history.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _history.SelectedChanged += delegate { RestoreHistory(); };
            _left.Controls.Add(_history);

            Label histTitle = new Label();
            histTitle.Text = "历史记录";
            histTitle.Font = Theme.F(8.4f);
            histTitle.ForeColor = Theme.TextDim;
            histTitle.SetBounds(Theme.Px(12), Theme.Px(194), Theme.Px(120), Theme.Px(16));
            _left.Controls.Add(histTitle);

            // ---- right: histogram + scrolling panels
            _hist = new HistogramView();
            _hist.SetBounds(Theme.Px(14), Theme.Px(12), Theme.Px(316), Theme.Px(100));
            _right.Controls.Add(_hist);

            _scroll = new ScrollHost();
            _scroll.SetBounds(Theme.Px(0), Theme.Px(126), Theme.Px(344), Theme.Px(600));
            _scroll.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _right.Controls.Add(_scroll);

            BuildBasicPanel();
            BuildDetailPanel();
            BuildMaskPanel();

            // ---- filmstrip
            _film = new FilmStrip();
            _film.Dock = DockStyle.Fill;
            _film.SelectionChanged += delegate { SelectDoc(_film.Selected); };
            _filmPanel.Controls.Add(_film);
        }

        private LButton MkTopBtn(string text, int w, EventHandler onClick)
        {
            LButton b = new LButton();
            b.Text = text;
            b.Size = new Size(w, Theme.Px(28));
            b.Click += onClick;
            b.Font = Theme.F(8.8f);
            return b;
        }

        private LSlider AddSlider(ScrollHost host, ParamDef def, Adjustments target, List<LSlider> bucket)
        {
            LSlider s = new LSlider(def, target);
            s.ValueChanged += delegate { RequestRender(); };
            s.DragEnded += delegate { CommitHistory(def.Label); };
            bucket.Add(s);
            host.Controls.Add(s);
            return s;
        }

        private void BuildBasicPanel()
        {
            _secBasic = new SectionHeader("基本");
            _secBasic.Toggled += delegate { LayoutRight(); };
            _scroll.Controls.Add(_secBasic);

            _btnAuto = new LButton();
            _btnAuto.Text = "自动色调";
            _btnAuto.Font = Theme.F(8.6f);
            _btnAuto.Click += delegate { AutoTone(); };
            _scroll.Controls.Add(_btnAuto);

            foreach (ParamDef p in Params.Basic)
                AddSlider(_scroll, p, null, _basicSliders);
        }

        private void BuildDetailPanel()
        {
            _secDetail = new SectionHeader("细节");
            _secDetail.Toggled += delegate { LayoutRight(); };
            _scroll.Controls.Add(_secDetail);

            _btnAutoNR = new LButton();
            _btnAutoNR.Text = "自动去杂色";
            _btnAutoNR.Font = Theme.F(8.6f);
            _btnAutoNR.Click += delegate { AutoDenoise(); };
            _scroll.Controls.Add(_btnAutoNR);

            foreach (ParamDef p in Params.Detail)
                AddSlider(_scroll, p, null, _detailSliders);
        }

        private void BuildMaskPanel()
        {
            _secMask = new SectionHeader("蒙版");
            _secMask.Toggled += delegate { LayoutRight(); };
            _scroll.Controls.Add(_secMask);

            _btnMaskLinear = new LButton(); _btnMaskLinear.Text = "线性";
            _btnMaskRadial = new LButton(); _btnMaskRadial.Text = "径向";
            _btnMaskBrush = new LButton(); _btnMaskBrush.Text = "画笔";
            _btnMaskErase = new LButton(); _btnMaskErase.Text = "擦除";
            _btnMaskDel = new LButton(); _btnMaskDel.Text = "删除";

            LButton[] tools = new LButton[] { _btnMaskLinear, _btnMaskRadial, _btnMaskBrush, _btnMaskErase, _btnMaskDel };
            foreach (LButton b in tools)
            {
                b.Font = Theme.F(8.4f);
                _scroll.Controls.Add(b);
            }
            _btnMaskLinear.Click += delegate { ActivateTool(ToolMode.Linear); };
            _btnMaskRadial.Click += delegate { ActivateTool(ToolMode.Radial); };
            _btnMaskBrush.Click += delegate { ActivateTool(ToolMode.Brush); };
            _btnMaskErase.Click += delegate
            {
                _canvas.BrushErase = !_canvas.BrushErase;
                _btnMaskErase.Toggled = _canvas.BrushErase;
                if (_canvas.Tool != ToolMode.Brush) ActivateTool(ToolMode.Brush);
            };
            _btnMaskDel.Click += delegate { DeleteMask(); };

            _maskList = new MaskList();
            _maskList.SelectionChanged += delegate { SelectMask(_maskList.Selected); };
            _maskList.DeleteRequested += delegate { DeleteMask(); };
            _scroll.Controls.Add(_maskList);

            // brush settings (radius / hardness / flow) - bound to a throwaway holder
            _brushSliders.Clear();
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                ParamDef def = null;
                if (i == 0) def = FreeParam("画笔大小", 1, 100, 5, delegate(float v) { _brushRadius = v / 100f; _canvas.BrushRadius = _brushRadius; });
                if (i == 1) def = FreeParam("羽化", 0, 100, 70, delegate(float v) { _brushHardness = v / 100f; _canvas.BrushHardness = _brushHardness; });
                if (i == 2) def = FreeParam("流量", 5, 100, 90, delegate(float v) { _brushFlow = v / 100f; _canvas.BrushFlow = _brushFlow; });
                LSlider s = new LSlider(def, _doc == null ? new Adjustments() : _doc.Adj);
                s.DragEnded += delegate { };
                _brushSliders.Add(s);
                _scroll.Controls.Add(s);
            }

            // local adjustment sliders for the selected mask
            string[] keys = new string[] { "Exposure", "Contrast", "Highlights", "Shadows", "Temp", "Tint", "Saturation" };
            foreach (string k in keys)
            {
                ParamDef p = Params.Find(Params.Basic, k);
                LSlider s = new LSlider(p, new Adjustments());
                s.ValueChanged += delegate { RequestRender(); };
                s.DragEnded += delegate { CommitHistory("蒙版 " + p.Label); };
                _maskSliders.Add(s);
                _scroll.Controls.Add(s);
            }
        }

        private ParamDef FreeParam(string label, float min, float max, float value, Action<float> setter)
        {
            Adjustments holder = new Adjustments();
            float init = value;
            holder.Exposure = init;
            ParamDef d = new ParamDef(label, label, min, max, value,
                delegate(Adjustments a) { return holder.Exposure; },
                delegate(Adjustments a, float v) { holder.Exposure = v; setter(v); }, "0");
            return d;
        }

        // ============================================================= layout
        private void LayoutChrome()
        {
            if (_btnOpen == null) return;
            LayoutTopBar();

            _hist.SetBounds(Theme.Px(14), Theme.Px(12), _right.Width - Theme.Px(28), Theme.Px(100));
            _scroll.SetBounds(0, Theme.Px(122), _right.Width, Math.Max(50, _right.Height - Theme.Px(122)));
            _history.SetBounds(Theme.Px(12), Theme.Px(212), _left.Width - Theme.Px(24), Math.Max(60, _left.Height - Theme.Px(226)));
            LayoutRight();
        }

        private void LayoutTopBar()
        {
            if (_btnOpen == null || _top.Width < 200) return;
            int y = (_top.Height - Theme.Px(28)) / 2;
            int right = _top.Width - Theme.Px(14);

            _btnReset.SetBounds(right - Theme.Px(56), y, Theme.Px(56), Theme.Px(28)); right -= Theme.Px(64);
            _btnCompare.SetBounds(right - Theme.Px(56), y, Theme.Px(56), Theme.Px(28)); right -= Theme.Px(64);
            _btnExport.SetBounds(right - Theme.Px(64), y, Theme.Px(64), Theme.Px(28)); right -= Theme.Px(72);
            _btnOpen.SetBounds(right - Theme.Px(84), y, Theme.Px(84), Theme.Px(28));
        }

        private void LayoutRight()
        {
            if (_scroll == null) return;
            _scroll.HideAll();
            int w = _scroll.Width - Theme.Px(18);
            int x = Theme.Px(10);
            int y = Theme.Px(4);

            y = PlaceSection(_secBasic, y, w) ;
            if (_secBasic.Expanded)
            {
                _scroll.Place(_btnAuto, x + Theme.Px(4), y + Theme.Px(2), w - Theme.Px(8), Theme.Px(28));
                y += Theme.Px(36);
                foreach (LSlider s in _basicSliders) { _scroll.Place(s, x + Theme.Px(2), y, w - Theme.Px(4), Theme.Px(29)); y += Theme.Px(29); }
                y += Theme.Px(8);
            }

            y = PlaceSection(_secDetail, y, w);
            if (_secDetail.Expanded)
            {
                _scroll.Place(_btnAutoNR, x + Theme.Px(4), y + Theme.Px(4), w - Theme.Px(8), Theme.Px(28));
                y += Theme.Px(36);
                foreach (LSlider s in _detailSliders) { _scroll.Place(s, x + Theme.Px(2), y, w - Theme.Px(4), Theme.Px(29)); y += Theme.Px(29); }
                y += Theme.Px(8);
            }

            y = PlaceSection(_secMask, y, w);
            if (_secMask.Expanded)
            {
                int bw = (w - Theme.Px(20)) / 5;
                LButton[] tools = new LButton[] { _btnMaskLinear, _btnMaskRadial, _btnMaskBrush, _btnMaskErase, _btnMaskDel };
                for (int i = 0; i < tools.Length; i++)
                    _scroll.Place(tools[i], x + Theme.Px(4) + i * (bw + Theme.Px(2)), y + Theme.Px(6), bw, Theme.Px(26));
                y += Theme.Px(40);

                _maskList.Items = _doc == null ? new List<MaskLayer>() : _doc.Masks;
                _maskList.Selected = _maskIndex;
                _scroll.Place(_maskList, x + Theme.Px(4), y, w - Theme.Px(8), _maskList.DesiredHeight);
                y += _maskList.DesiredHeight + Theme.Px(6);

                bool brush = _canvas != null && _canvas.Tool == ToolMode.Brush;
                if (brush)
                {
                    foreach (LSlider s in _brushSliders)
                    {
                        _scroll.Place(s, x + Theme.Px(2), y, w - Theme.Px(4), Theme.Px(29));
                        y += Theme.Px(29);
                    }
                }

                if (_maskIndex >= 0)
                {
                    foreach (LSlider s in _maskSliders)
                    {
                        _scroll.Place(s, x + Theme.Px(2), y, w - Theme.Px(4), Theme.Px(29));
                        y += Theme.Px(29);
                    }
                }
                y += Theme.Px(10);
            }

            _scroll.ContentHeight = y + Theme.Px(16);
        }

        private int PlaceSection(SectionHeader h, int y, int w)
        {
            if (h.Expanded)
            {
                _scroll.Place(h, Theme.Px(4), y, w, Theme.Px(30));
                return y + Theme.Px(30);
            }
            _scroll.Place(h, Theme.Px(4), y, w, Theme.Px(30));
            return y + Theme.Px(30);
        }

        private void PaintTop(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Setup(g);
            g.Clear(Theme.PanelDeep);
            using (Pen p = new Pen(Theme.Line, 1))
                g.DrawLine(p, 0, _top.Height - 1, _top.Width, _top.Height - 1);

            Theme.Logo(g, new Rectangle(Theme.Px(14), Theme.Px(13), Theme.Px(20), Theme.Px(20)));
            Theme.DrawText(g, "LumaRoom", Theme.F(11f, true), Theme.Text, Theme.Px(42), Theme.Px(14));
            using (SolidBrush b = new SolidBrush(Theme.TextFaint))
                g.DrawString("轻量暗房", Theme.F(8f), b, Theme.Px(156), Theme.Px(16));

            if (_doc != null)
            {
                string info = _doc.DisplayName;
                if (_doc.FromRaw) info += "   ·   RAW " + _doc.FullW + "×" + _doc.FullH + "  (" + _doc.RawNote + ")";
                else info += "   ·   " + _doc.FullW + "×" + _doc.FullH;
                Theme.TextLeft(g, info, Theme.F(8.6f), Theme.TextDim,
                               new Rectangle(Theme.Px(210), 0, Math.Max(10, _top.Width - Theme.Px(560)), Theme.Px(46)));
            }
            else
            {
                Theme.TextLeft(g, "尚未打开照片", Theme.F(8.6f), Theme.TextFaint,
                               new Rectangle(Theme.Px(210), 0, Theme.Px(200), Theme.Px(46)));
            }

            Theme.TextRight(g, _canvas == null ? "" : (_canvas.IsFit ? "适应窗口" : (int)(_canvas.Zoom * 100) + "%"),
                            Theme.F(8f), Theme.TextFaint, new Rectangle(_top.Width - Theme.Px(360), 0, Theme.Px(110), Theme.Px(46)));
        }

        // ============================================================== files
        private void OpenDialog()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "打开照片";
                d.Multiselect = true;
                d.Filter = "照片文件|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.dng;*.cr2;*.cr3;*.nef;*.arw;*.raf;*.orf;*.rw2;*.pef;*.srw|所有文件|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                LoadFiles(d.FileNames);
            }
        }

        private void LoadFiles(string[] paths)
        {
            List<string> ok = new List<string>();
            Cursor = Cursors.WaitCursor;
            UseWaitCursor = true;
            try
            {
                foreach (string p in paths)
                {
                    if (!File.Exists(p)) continue;
                    if (!ImageDoc.IsSupported(p)) continue;
                    try
                    {
                        ImageDoc doc = Decoder.Open(p, Pipeline.PreviewMaxSide);
                        if (doc == null) continue;
                        _docs.Add(doc);
                        _film.Add(p, Decoder.Thumb(p, Theme.Px(78), Theme.Px(56)));
                        ok.Add(System.IO.Path.GetFileName(p));
                    }
                    catch { }
                }
            }
            finally
            {
                Cursor = Cursors.Default;
                UseWaitCursor = false;
            }

            if (ok.Count > 0)
            {
                _film.Selected = _docs.Count - 1;
                _film.Invalidate();
                SelectDoc(_docs.Count - 1);
                TrimDocMemory();
                SetStatus("已载入 " + ok.Count + " 张照片");
            }
            else if (paths.Length > 0)
            {
                SetStatus("无法打开所选文件");
            }
        }

        private void SelectDoc(int i)
        {
            if (i < 0 || i >= _docs.Count) return;
            _docIndex = i;
            _doc = _docs[i];
            if (_doc.Full == null) ReloadFull(_doc);
            _film.Selected = i;
            _film.Invalidate();

            _maskIndex = _doc.Masks.Count > 0 ? 0 : -1;
            _canvas.Tool = ToolMode.None;
            _canvas.BrushErase = false;
            _btnMaskErase.Toggled = false;
            UpdateToolButtons();

            foreach (LSlider s in _basicSliders) s.Target = _doc.Adj;
            foreach (LSlider s in _detailSliders) s.Target = _doc.Adj;
            foreach (LSlider s in _maskSliders) s.Target = new Adjustments();
            _history.Items = _doc.History;
            _history.Selected = _doc.History.Count - 1;
            _history.Invalidate();

            BindMaskSliders();
            LayoutRight();
            RequestRender();
            Invalidate();
        }

        /// <summary>Keeps only the newest few full size bitmaps around; older ones are
        /// dropped and decoded again on demand so a big filmstrip does not eat all RAM.</summary>
        private void TrimDocMemory()
        {
            const int keep = 4;
            int limit = _docs.Count - keep;
            for (int i = 0; i < limit; i++)
            {
                ImageDoc d = _docs[i];
                if (d == _doc || d.Full == null) continue;
                d.Full.Dispose();
                d.Full = null;
            }
        }

        private void ReloadFull(ImageDoc d)
        {
            if (d.Full != null || string.IsNullOrEmpty(d.Path)) return;
            if (!File.Exists(d.Path)) return;
            try
            {
                ImageDoc t = Decoder.Open(d.Path, 64);
                if (t != null && t.Full != null)
                {
                    d.Full = t.Full;
                    t.Full = null;
                }
            }
            catch { }
        }

        // ============================================================ render
        private MaskLayer ActiveMask
        {
            get
            {
                if (_doc == null || _maskIndex < 0 || _maskIndex >= _doc.Masks.Count) return null;
                return _doc.Masks[_maskIndex];
            }
        }

        private bool IsMaskEditing()
        {
            return _canvas.Tool != ToolMode.None && ActiveMask != null;
        }

        private void RequestRender()
        {
            if (_doc == null) { _canvas.Image = null; _canvas.Invalidate(); return; }
            foreach (MaskLayer m in _doc.Masks)
                if (m.Enabled) Masks.Build(m, _doc.W, _doc.H);

            _pending = true;
            if (_rendering) return;
            StartRender();
        }

        private void StartRender()
        {
            if (_doc == null) return;
            _pending = false;
            _rendering = true;

            int seq = ++_seq;
            ImageDoc doc = _doc;
            Adjustments adj = _showOriginal ? new Adjustments() : doc.Adj.Clone();
            List<MaskLayer> masks = _showOriginal ? new List<MaskLayer>() : doc.Masks;

            bool overlay = IsMaskEditing();
            int w = doc.W, h = doc.H;

            System.Threading.ThreadPool.QueueUserWorkItem(delegate(object st)
            {
                byte[] buf = new byte[w * h * 4];
                int[] hist = new int[768];
                string err = null;
                try { Pipeline.Render(doc.Src, w, h, adj, masks, buf, overlay, hist); }
                catch (Exception ex) { err = ex.Message; }

                if (_closing) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (_closing) return;
                        if (seq == _seq && _doc == doc) ApplyRender(buf, w, h, hist, err);
                        _rendering = false;
                        if (_pending) StartRender();
                    });
                }
                catch { _rendering = false; }
            });
        }

        private void ApplyRender(byte[] buf, int w, int h, int[] hist, string err)
        {
            if (_renderBmp == null || _renderBmp.Width != w || _renderBmp.Height != h)
            {
                if (_renderBmp != null) _renderBmp.Dispose();
                _renderBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            }

            BitmapData bd = _renderBmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = bd.Stride;
                if (stride == w * 4)
                    Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
                else
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(buf, y * w * 4, new IntPtr(bd.Scan0.ToInt64() + (long)y * stride), w * 4);
            }
            finally { _renderBmp.UnlockBits(bd); }

            _canvas.Image = _renderBmp;
            _canvas.Invalidate();
            _hist.SetData(hist);

            UpdateNavThumb();
            if (err != null) SetStatus("渲染出错: " + err);
        }

        private void UpdateNavThumb()
        {
            if (_renderBmp == null) return;
            int tw = Math.Max(8, _nav.Width), th = Math.Max(8, _nav.Height);
            Bitmap t = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(t))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.Clear(Theme.PanelDeep);
                float s = Math.Min((float)tw / _renderBmp.Width, (float)th / _renderBmp.Height);
                int dw = Math.Max(1, (int)(_renderBmp.Width * s)), dh = Math.Max(1, (int)(_renderBmp.Height * s));
                g.DrawImage(_renderBmp, new Rectangle((tw - dw) / 2, (th - dh) / 2, dw, dh));
            }
            Image old = _nav.Image;
            _nav.Image = t;
            _navBmp = t;
            if (old != null) old.Dispose();
        }

        private void SetStatus(string s)
        {
            _status.Text = "  " + s;
        }

        // ============================================================ history
        private void CommitHistory(string label)
        {
            if (_doc == null) return;
            _doc.PushHistory(label);
            _history.Items = _doc.History;
            _history.Selected = _doc.History.Count - 1;
            _history.Invalidate();
        }

        private string MaskHistoryLabel()
        {
            MaskLayer m = ActiveMask;
            if (m == null) return "编辑";
            if (m.Kind == MaskKind.Linear) return "线性渐变";
            if (m.Kind == MaskKind.Radial) return "径向渐变";
            return _canvas.BrushErase ? "画笔擦除" : "画笔";
        }

        private void RestoreHistory()
        {
            if (_doc == null) return;
            int idx = _history.Selected;
            if (idx < 0 || idx >= _doc.History.Count) return;
            _doc.Adj.CopyFrom(_doc.History[idx].Snapshot);
            foreach (LSlider s in _basicSliders) s.Invalidate();
            foreach (LSlider s in _detailSliders) s.Invalidate();
            RequestRender();
        }

        // ============================================================== masks
        private void ActivateTool(ToolMode t)
        {
            if (_doc == null) return;
            MaskKind kind = t == ToolMode.Linear ? MaskKind.Linear : (t == ToolMode.Radial ? MaskKind.Radial : MaskKind.Brush);

            MaskLayer cur = ActiveMask;
            if (cur == null || cur.Kind != kind)
            {
                MaskLayer m = new MaskLayer();
                m.Kind = kind;
                m.Adjust = new Adjustments();
                if (kind == MaskKind.Brush) m.Name = "画笔 " + (_doc.Masks.Count + 1);
                else if (kind == MaskKind.Linear) m.Name = "线性渐变 " + (_doc.Masks.Count + 1);
                else m.Name = "径向渐变 " + (_doc.Masks.Count + 1);
                if (kind == MaskKind.Radial) { m.X1 = 0.5f; m.Y1 = 0.5f; m.X2 = 0.62f; m.Y2 = 0.5f; m.Extent = 0.7f; }
                _doc.Masks.Add(m);
                _maskIndex = _doc.Masks.Count - 1;
                CommitHistory("新建 " + m.Name);
            }

            _canvas.Tool = t;
            _canvas.ActiveMask = ActiveMask;
            foreach (MaskLayer m in _doc.Masks) m.ShowOverlay = (m == ActiveMask);

            UpdateToolButtons();
            BindMaskSliders();
            LayoutRight();
            RequestRender();
        }

        private void UpdateToolButtons()
        {
            ToolMode t = _canvas.Tool;
            _btnMaskLinear.Toggled = t == ToolMode.Linear;
            _btnMaskRadial.Toggled = t == ToolMode.Radial;
            _btnMaskBrush.Toggled = t == ToolMode.Brush;
            MaskLayer m = ActiveMask;
            _btnMaskDel.Enabled = m != null;
        }

        private void SelectMask(int idx)
        {
            if (_doc == null) return;
            _maskIndex = idx;
            MaskLayer m = ActiveMask;
            if (m != null)
            {
                ToolMode t = m.Kind == MaskKind.Linear ? ToolMode.Linear : (m.Kind == MaskKind.Radial ? ToolMode.Radial : ToolMode.Brush);
                _canvas.Tool = t;
                _canvas.ActiveMask = m;
                foreach (MaskLayer x in _doc.Masks) x.ShowOverlay = (x == m);
            }
            else
            {
                _canvas.Tool = ToolMode.None;
                _canvas.ActiveMask = null;
            }
            UpdateToolButtons();
            BindMaskSliders();
            LayoutRight();
            RequestRender();
        }

        private void BindMaskSliders()
        {
            MaskLayer m = ActiveMask;
            Adjustments target;
            if (m != null) target = m.Adjust;
            else target = new Adjustments();

            string[] keys = new string[] { "Exposure", "Contrast", "Highlights", "Shadows", "Temp", "Tint", "Saturation" };
            for (int i = 0; i < _maskSliders.Count && i < keys.Length; i++)
            {
                _maskSliders[i].Target = target;
                _maskSliders[i].Invalidate();
            }
            _maskList.Invalidate();
        }

        private void DeleteMask()
        {
            if (_doc == null || _maskIndex < 0 || _maskIndex >= _doc.Masks.Count) return;
            string name = _doc.Masks[_maskIndex].Name;
            _doc.Masks.RemoveAt(_maskIndex);
            _maskIndex = _doc.Masks.Count > 0 ? Math.Min(_maskIndex, _doc.Masks.Count - 1) : -1;
            _canvas.ActiveMask = ActiveMask;
            if (_maskIndex < 0) _canvas.Tool = ToolMode.None;
            CommitHistory("删除 " + name);
            UpdateToolButtons();
            BindMaskSliders();
            LayoutRight();
            RequestRender();
        }

        // =============================================================== auto
        private void AutoDenoise()
        {
            if (_doc == null) return;
            float sigma = Pipeline.EstimateNoise(_doc.Src, _doc.W, _doc.H);
            float lum = (float)Math.Round(Math.Min(88f, Math.Max(0f, (sigma - 0.005f) * 1400f)));
            float col = (float)Math.Round(Math.Min(82f, Math.Max(12f, 12f + sigma * 900f)));

            if (_doc.FromRaw) { lum = Math.Min(88f, lum * 1.15f); }

            _doc.Adj.LumNR = lum;
            _doc.Adj.ColorNR = col;
            _doc.Adj.ColorNRSmooth = Math.Min(80f, 40f + col * 0.4f);
            _doc.Adj.LumNRDetail = Math.Max(25f, 70f - lum * 0.4f);

            foreach (LSlider s in _detailSliders) s.Invalidate();
            CommitHistory("自动去杂色");
            RequestRender();
            SetStatus(string.Format("噪声估计 σ={0:0.0000} → 亮度降噪 {1} · 颜色降噪 {2}", sigma, lum, col));
        }

        private void AutoTone()
        {
            if (_doc == null) return;
            int[] hist = new int[768];
            int n = _doc.W * _doc.H;
            float[] src = _doc.Src;
            for (int i = 0; i < n; i++)
            {
                int si = i * 3;
                int r = (int)(src[si] * 255f), g = (int)(src[si + 1] * 255f), b = (int)(src[si + 2] * 255f);
                hist[r]++; hist[256 + g]++; hist[512 + b]++;
            }
            int total = n;
            int lo = 0, hi = 255;
            int acc = 0;
            for (int i = 0; i < 256; i++) { acc += hist[i]; if (acc > total * 0.01) { lo = i; break; } }
            acc = 0;
            for (int i = 255; i >= 0; i--) { acc += hist[i]; if (acc > total * 0.01) { hi = i; break; } }

            Adjustments a = _doc.Adj;
            a.Blacks = Math.Max(-60f, Math.Min(60f, -(lo - 8) * 0.55f));
            a.Whites = Math.Max(-60f, Math.Min(60f, (262 - hi) * 0.55f));
            float mid = (lo + hi) / 2f;
            a.Exposure = Math.Max(-2f, Math.Min(2f, (float)Math.Log(128f / Math.Max(8f, mid), 2.0) * 0.55f));
            a.Contrast = 12f;

            foreach (LSlider s in _basicSliders) s.Invalidate();
            CommitHistory("自动色调");
            RequestRender();
            SetStatus("自动色调：黑场 " + (int)lo + " / 白场 " + (int)hi);
        }

        private void ResetAll()
        {
            if (_doc == null) return;
            _doc.Adj = new Adjustments();
            _doc.Masks.Clear();
            _maskIndex = -1;
            _canvas.ActiveMask = null;
            _canvas.Tool = ToolMode.None;
            foreach (LSlider s in _basicSliders) { s.Target = _doc.Adj; s.Invalidate(); }
            foreach (LSlider s in _detailSliders) { s.Target = _doc.Adj; s.Invalidate(); }
            CommitHistory("复位");
            UpdateToolButtons();
            BindMaskSliders();
            LayoutRight();
            RequestRender();
            SetStatus("已复位所有调整");
        }

        private void ApplyDemo()
        {
            if (_doc == null) return;
            _doc.Adj.Exposure = 0.45f;
            _doc.Adj.Contrast = 22f;
            _doc.Adj.Highlights = -38f;
            _doc.Adj.Shadows = 40f;
            _doc.Adj.Clarity = 22f;
            _doc.Adj.Vibrance = 25f;
            _doc.Adj.Temp = 6200f;
            _doc.Adj.LumNR = 45f;
            _doc.Adj.ColorNR = 55f;

            MaskLayer m = new MaskLayer();
            m.Kind = MaskKind.Linear;
            m.Name = "线性渐变 1";
            m.X1 = 0.5f; m.Y1 = 0.42f; m.X2 = 0.5f; m.Y2 = 1.0f;
            m.Feather = 0.7f;
            m.Adjust = new Adjustments();
            m.Adjust.Exposure = -0.35f;
            m.Adjust.Temp = 4600f;
            _doc.Masks.Add(m);
            _maskIndex = 0;

            _secBasic.Expanded = true;
            _secDetail.Expanded = false;
            _secMask.Expanded = false;

            _canvas.Tool = ToolMode.Linear;
            _canvas.ActiveMask = m;
            m.ShowOverlay = false;

            foreach (LSlider s in _basicSliders) s.Invalidate();
            foreach (LSlider s in _detailSliders) s.Invalidate();
            UpdateToolButtons();
            BindMaskSliders();
            LayoutRight();
            CommitHistory("演示调整");
            RequestRender();
        }

        private void ToggleCompare()        {
            _showOriginal = !_showOriginal;
            _btnCompare.Toggled = _showOriginal;
            RequestRender();
        }

        // ============================================================= export
        private void ExportDialog()
        {
            if (_doc == null) { SetStatus("请先打开照片"); return; }

            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "导出";
                d.Filter = "JPEG 图像|*.jpg|PNG 图像|*.png|TIFF 图像|*.tif";
                string baseName = System.IO.Path.GetFileNameWithoutExtension(_doc.Path);
                d.FileName = baseName + "_LumaRoom.jpg";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                DoExport(d.FileName);
            }
        }

        private void DoExport(string path)
        {
            Cursor = Cursors.WaitCursor;
            UseWaitCursor = true;
            SetStatus("正在导出…");
            Application.DoEvents();
            try
            {
                ExportTo(path);
            }
            finally
            {
                Cursor = Cursors.Default;
                UseWaitCursor = false;
                SetStatus(LastExportMessage);
            }
        }

        public string LastExportMessage = "";

        /// <summary>Full resolution render + save. Returns true on success.</summary>
        public bool ExportTo(string path)
        {
            if (_doc == null) { LastExportMessage = "没有可导出的照片"; return false; }
            ImageDoc doc = _doc;
            if (doc.Full == null) ReloadFull(doc);
            if (doc.Full == null) { LastExportMessage = "无法重新读取源文件"; return false; }
            Adjustments adj = doc.Adj.Clone();
            MaskLayer[] masks = new MaskLayer[doc.Masks.Count];
            for (int i = 0; i < doc.Masks.Count; i++) masks[i] = doc.Masks[i].Clone();

            try
            {
                int fw = doc.FullW, fh = doc.FullH;
                float[] full = ImageDoc.Extract(doc.Full, out fw, out fh);

                // keep memory bounded on huge files
                long px = (long)fw * fh;
                if (px > 16000000L)
                {
                    float s = (float)Math.Sqrt(16000000.0 / px);
                    int nw = Math.Max(1, (int)(fw * s)), nh = Math.Max(1, (int)(fh * s));
                    using (Bitmap b = new Bitmap(nw, nh, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(b))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(doc.Full, new Rectangle(0, 0, nw, nh));
                        }
                        full = ImageDoc.Extract(b, out fw, out fh);
                    }
                }

                List<MaskLayer> list = new List<MaskLayer>();
                foreach (MaskLayer m in masks)
                {
                    if (!m.Enabled) continue;
                    m.Weight = null; m.WeightDirty = true;
                    Masks.Build(m, fw, fh);
                    list.Add(m);
                }

                byte[] buf = new byte[(long)fw * fh * 4];
                Pipeline.Render(full, fw, fh, adj, list, buf, false, null);

                using (Bitmap outBmp = new Bitmap(fw, fh, PixelFormat.Format32bppArgb))
                {
                    BitmapData bd = outBmp.LockBits(new Rectangle(0, 0, fw, fh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        int stride = bd.Stride;
                        if (stride == fw * 4) Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
                        else
                            for (int y = 0; y < fh; y++)
                                Marshal.Copy(buf, y * fw * 4, new IntPtr(bd.Scan0.ToInt64() + (long)y * stride), fw * 4);
                    }
                    finally { outBmp.UnlockBits(bd); }

                    string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                    ImageFormat fmt = ImageFormat.Jpeg;
                    if (ext == ".png") fmt = ImageFormat.Png;
                    else if (ext == ".tif" || ext == ".tiff") fmt = ImageFormat.Tiff;
                    outBmp.Save(path, fmt);
                }

                LastExportMessage = "已导出 " + fw + "×" + fh + " → " + System.IO.Path.GetFileName(path);
                return true;
            }
            catch (OutOfMemoryException)
            {
                LastExportMessage = "内存不足，导出失败（照片过大）";
                return false;
            }
            catch (Exception ex)
            {
                LastExportMessage = "导出失败: " + ex.Message;
                return false;
            }
        }

        // ============================================================== input
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) { _canvas.SpaceDown = true; _canvas.Cursor = Cursors.Hand; }
            if (e.KeyCode == Keys.Oem5 || e.KeyCode == Keys.OemBackslash) ToggleCompare();
            if (e.Control && e.KeyCode == Keys.O) OpenDialog();
            if (e.Control && e.KeyCode == Keys.S) ExportDialog();
            if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus) _canvas.ZoomStep(1.2f, Point.Empty);
            if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus) _canvas.ZoomStep(1f / 1.2f, Point.Empty);
            if (e.KeyCode == Keys.D0) _canvas.FitToWindow();
            if (e.KeyCode == Keys.OemOpenBrackets) AdjustBrush(-1);
            if (e.KeyCode == Keys.OemCloseBrackets) AdjustBrush(1);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space) { _canvas.SpaceDown = false; _canvas.Cursor = Cursors.Default; }
        }

        private void AdjustBrush(int dir)
        {
            _brushRadius = Math.Max(0.005f, Math.Min(0.5f, _brushRadius * (dir > 0 ? 1.15f : 1f / 1.15f)));
            _canvas.BrushRadius = _brushRadius;
            if (_brushSliders.Count > 0) _brushSliders[0].SetValue(_brushRadius * 100f, false);
            _canvas.Invalidate();
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) LoadFiles(files);
        }
    }
}
