using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace LumaRoom
{
    /// <summary>All non-destructive develop settings (LRC "Basic" + "Detail").</summary>
    internal sealed class Adjustments
    {
        public float Temp = 5500f;
        public float Tint = 0f;

        public float Exposure = 0f;      // EV, -5..+5
        public float Contrast = 0f;      // -100..100
        public float Highlights = 0f;
        public float Shadows = 0f;
        public float Whites = 0f;
        public float Blacks = 0f;

        public float Texture = 0f;
        public float Clarity = 0f;
        public float Dehaze = 0f;

        public float Vibrance = 0f;
        public float Saturation = 0f;

        // Detail panel
        public float SharpenAmount = 40f;
        public float SharpenRadius = 1.0f;
        public float SharpenDetail = 25f;

        public float LumNR = 0f;
        public float LumNRDetail = 50f;
        public float LumNRContrast = 0f;

        public float ColorNR = 25f;
        public float ColorNRDetail = 50f;
        public float ColorNRSmooth = 50f;

        public Adjustments Clone()
        {
            return (Adjustments)MemberwiseClone();
        }

        public void CopyFrom(Adjustments o)
        {
            Temp = o.Temp; Tint = o.Tint;
            Exposure = o.Exposure; Contrast = o.Contrast;
            Highlights = o.Highlights; Shadows = o.Shadows;
            Whites = o.Whites; Blacks = o.Blacks;
            Texture = o.Texture; Clarity = o.Clarity; Dehaze = o.Dehaze;
            Vibrance = o.Vibrance; Saturation = o.Saturation;
            SharpenAmount = o.SharpenAmount; SharpenRadius = o.SharpenRadius; SharpenDetail = o.SharpenDetail;
            LumNR = o.LumNR; LumNRDetail = o.LumNRDetail; LumNRContrast = o.LumNRContrast;
            ColorNR = o.ColorNR; ColorNRDetail = o.ColorNRDetail; ColorNRSmooth = o.ColorNRSmooth;
        }

        /// <summary>True when nothing but identity settings are present (used to skip mask work).</summary>
        public bool IsIdentityTone()
        {
            return Math.Abs(Temp - 5500f) < 1 && Math.Abs(Tint) < 0.5f &&
                   Math.Abs(Exposure) < 0.001f && Math.Abs(Contrast) < 0.5f &&
                   Math.Abs(Highlights) < 0.5f && Math.Abs(Shadows) < 0.5f &&
                   Math.Abs(Whites) < 0.5f && Math.Abs(Blacks) < 0.5f &&
                   Math.Abs(Vibrance) < 0.5f && Math.Abs(Saturation) < 0.5f;
        }
    }

    internal enum MaskKind { Linear = 0, Radial = 1, Brush = 2 }

    internal sealed class BrushStroke
    {
        public List<PointF> Points = new List<PointF>();   // normalized 0..1
        public float Radius = 0.05f;                        // fraction of image width
        public float Hardness = 0.6f;
        public float Flow = 0.8f;
        public bool Erase = false;

        public BrushStroke Clone()
        {
            BrushStroke s = new BrushStroke();
            s.Points = new List<PointF>(Points);
            s.Radius = Radius; s.Hardness = Hardness; s.Flow = Flow; s.Erase = Erase;
            return s;
        }
    }

    /// <summary>A local adjustment mask. Geometry is stored normalized so it can be
    /// re-rasterised at full resolution on export.</summary>
    internal sealed class MaskLayer
    {
        public MaskKind Kind = MaskKind.Linear;
        public string Name = "蒙版";
        public bool Enabled = true;
        public bool Invert = false;
        public bool ShowOverlay = false;

        // linear: (X1,Y1) -> (X2,Y2) gradient axis.  radial: center (X1,Y1), radius = dist to (X2,Y2)
        public float X1 = 0.5f, Y1 = 0.3f, X2 = 0.5f, Y2 = 0.7f;
        public float Angle = 90f;      // degrees, for linear
        public float Feather = 0.5f;   // 0..1 soft transition
        public float Extent = 1f;      // radial aspect (1 = circle)

        public List<BrushStroke> Strokes = new List<BrushStroke>();

        public Adjustments Adjust = new Adjustments();

        // rasterised weight cache (preview resolution)
        [NonSerialized] public float[] Weight;
        [NonSerialized] public int WeightW, WeightH;
        [NonSerialized] public bool WeightDirty = true;

        public void Invalidate() { WeightDirty = true; }

        public MaskLayer Clone()
        {
            MaskLayer m = new MaskLayer();
            m.Kind = Kind; m.Name = Name; m.Enabled = Enabled; m.Invert = Invert;
            m.X1 = X1; m.Y1 = Y1; m.X2 = X2; m.Y2 = Y2;
            m.Angle = Angle; m.Feather = Feather; m.Extent = Extent;
            m.Adjust = Adjust.Clone();
            foreach (BrushStroke s in Strokes) m.Strokes.Add(s.Clone());
            return m;
        }
    }

    internal sealed class HistoryEntry
    {
        public string Label;
        public Adjustments Snapshot;
        public HistoryEntry(string label, Adjustments a) { Label = label; Snapshot = a.Clone(); }
    }

    /// <summary>One opened photograph plus its develop state.</summary>
    internal sealed class ImageDoc
    {
        public string Path = "";
        public string DisplayName = "";
        public bool FromRaw = false;
        public string RawNote = "";

        public Bitmap Full;            // full resolution source
        public float[] Src;            // preview working source, sRGB 0..1 interleaved RGB
        public int W, H;               // preview dims

        public Adjustments Adj = new Adjustments();
        public List<MaskLayer> Masks = new List<MaskLayer>();
        public List<HistoryEntry> History = new List<HistoryEntry>();

        public int FullW { get { return Full == null ? 0 : Full.Width; } }
        public int FullH { get { return Full == null ? 0 : Full.Height; } }

        public static readonly string[] Exts = new string[]
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff",
            ".dng", ".cr2", ".cr3", ".nef", ".arw", ".raf", ".orf", ".rw2", ".pef", ".srw", ".sr2", ".raw", ".3fr", ".iiq"
        };

        public static bool IsSupported(string path)
        {
            string e = System.IO.Path.GetExtension(path).ToLowerInvariant();
            foreach (string x in Exts) if (x == e) return true;
            return false;
        }

        /// <summary>Downscale to a work resolution and keep it as linear-ish float sRGB.</summary>
        public void BuildPreview(int maxSide)
        {
            if (Full == null) return;
            int fw = Full.Width, fh = Full.Height;
            float s = Math.Min(1f, (float)maxSide / Math.Max(fw, fh));
            int pw = Math.Max(1, (int)Math.Round(fw * s));
            int ph = Math.Max(1, (int)Math.Round(fh * s));

            using (Bitmap small = new Bitmap(pw, ph, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(Full, new Rectangle(0, 0, pw, ph));
                }
                Src = Extract(small, out W, out H);
            }
        }

        /// <summary>Bitmap -> float RGB (0..1) array, no alpha.</summary>
        public static float[] Extract(Bitmap bmp, out int w, out int h)
        {
            w = bmp.Width; h = bmp.Height;
            float[] dst = new float[w * h * 3];
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = bd.Stride;
                byte[] buf = new byte[stride * h];
                Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                int di = 0;
                for (int y = 0; y < h; y++)
                {
                    int ro = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int so = ro + x * 4;
                        dst[di++] = buf[so + 2] * (1f / 255f);
                        dst[di++] = buf[so + 1] * (1f / 255f);
                        dst[di++] = buf[so] * (1f / 255f);
                    }
                }
            }
            finally { bmp.UnlockBits(bd); }
            return dst;
        }

        public void PushHistory(string label)
        {
            History.Add(new HistoryEntry(label, Adj));
            if (History.Count > 60) History.RemoveAt(0);
        }
    }

    /// <summary>Slider metadata so the UI can be generated from a table.</summary>
    internal sealed class ParamDef
    {
        public string Key;
        public string Label;
        public float Min, Max, Def;
        public string Fmt = "0";
        public string Suffix = "";
        public Func<Adjustments, float> Get;
        public Action<Adjustments, float> Set;
        public float Step = 1f;

        public ParamDef(string key, string label, float min, float max, float def,
                        Func<Adjustments, float> get, Action<Adjustments, float> set, string fmt)
        {
            Key = key; Label = label; Min = min; Max = max; Def = def;
            Get = get; Set = set; Fmt = fmt;
        }

        public string Format(float v)
        {
            string s = v.ToString(Fmt, CultureInfo.InvariantCulture);
            if (Math.Abs(v) < 0.0001f && Min < 0) return "0";
            if (v > 0 && Min < 0 && !Fmt.StartsWith("#")) s = "+" + s;
            return s + Suffix;
        }
    }

    internal static class Params
    {
        public static readonly List<ParamDef> Basic = new List<ParamDef>();
        public static readonly List<ParamDef> Detail = new List<ParamDef>();

        static Params()
        {
            Basic.Add(new ParamDef("Temp", "色温", 2000, 12000, 5500,
                delegate(Adjustments a) { return a.Temp; }, delegate(Adjustments a, float v) { a.Temp = v; }, "0"));
            Basic.Add(new ParamDef("Tint", "色调", -150, 150, 0,
                delegate(Adjustments a) { return a.Tint; }, delegate(Adjustments a, float v) { a.Tint = v; }, "0"));
            Basic.Add(new ParamDef("Exposure", "曝光度", -5, 5, 0,
                delegate(Adjustments a) { return a.Exposure; }, delegate(Adjustments a, float v) { a.Exposure = v; }, "0.00"));
            Basic.Add(new ParamDef("Contrast", "对比度", -100, 100, 0,
                delegate(Adjustments a) { return a.Contrast; }, delegate(Adjustments a, float v) { a.Contrast = v; }, "0"));
            Basic.Add(new ParamDef("Highlights", "高光", -100, 100, 0,
                delegate(Adjustments a) { return a.Highlights; }, delegate(Adjustments a, float v) { a.Highlights = v; }, "0"));
            Basic.Add(new ParamDef("Shadows", "阴影", -100, 100, 0,
                delegate(Adjustments a) { return a.Shadows; }, delegate(Adjustments a, float v) { a.Shadows = v; }, "0"));
            Basic.Add(new ParamDef("Whites", "白色色阶", -100, 100, 0,
                delegate(Adjustments a) { return a.Whites; }, delegate(Adjustments a, float v) { a.Whites = v; }, "0"));
            Basic.Add(new ParamDef("Blacks", "黑色色阶", -100, 100, 0,
                delegate(Adjustments a) { return a.Blacks; }, delegate(Adjustments a, float v) { a.Blacks = v; }, "0"));
            Basic.Add(new ParamDef("Texture", "纹理", -100, 100, 0,
                delegate(Adjustments a) { return a.Texture; }, delegate(Adjustments a, float v) { a.Texture = v; }, "0"));
            Basic.Add(new ParamDef("Clarity", "清晰度", -100, 100, 0,
                delegate(Adjustments a) { return a.Clarity; }, delegate(Adjustments a, float v) { a.Clarity = v; }, "0"));
            Basic.Add(new ParamDef("Dehaze", "去朦胧", -100, 100, 0,
                delegate(Adjustments a) { return a.Dehaze; }, delegate(Adjustments a, float v) { a.Dehaze = v; }, "0"));
            Basic.Add(new ParamDef("Vibrance", "自然饱和度", -100, 100, 0,
                delegate(Adjustments a) { return a.Vibrance; }, delegate(Adjustments a, float v) { a.Vibrance = v; }, "0"));
            Basic.Add(new ParamDef("Saturation", "饱和度", -100, 100, 0,
                delegate(Adjustments a) { return a.Saturation; }, delegate(Adjustments a, float v) { a.Saturation = v; }, "0"));

            Detail.Add(new ParamDef("SharpenAmount", "锐化数量", 0, 150, 40,
                delegate(Adjustments a) { return a.SharpenAmount; }, delegate(Adjustments a, float v) { a.SharpenAmount = v; }, "0"));
            Detail.Add(new ParamDef("SharpenRadius", "半径", 0.5f, 3f, 1f,
                delegate(Adjustments a) { return a.SharpenRadius; }, delegate(Adjustments a, float v) { a.SharpenRadius = v; }, "0.0"));
            Detail.Add(new ParamDef("SharpenDetail", "细节", 0, 100, 25,
                delegate(Adjustments a) { return a.SharpenDetail; }, delegate(Adjustments a, float v) { a.SharpenDetail = v; }, "0"));
            Detail.Add(new ParamDef("LumNR", "亮度降噪", 0, 100, 0,
                delegate(Adjustments a) { return a.LumNR; }, delegate(Adjustments a, float v) { a.LumNR = v; }, "0"));
            Detail.Add(new ParamDef("LumNRDetail", "保留细节", 0, 100, 50,
                delegate(Adjustments a) { return a.LumNRDetail; }, delegate(Adjustments a, float v) { a.LumNRDetail = v; }, "0"));
            Detail.Add(new ParamDef("LumNRContrast", "对比度", 0, 100, 0,
                delegate(Adjustments a) { return a.LumNRContrast; }, delegate(Adjustments a, float v) { a.LumNRContrast = v; }, "0"));
            Detail.Add(new ParamDef("ColorNR", "颜色降噪", 0, 100, 25,
                delegate(Adjustments a) { return a.ColorNR; }, delegate(Adjustments a, float v) { a.ColorNR = v; }, "0"));
            Detail.Add(new ParamDef("ColorNRDetail", "细节", 0, 100, 50,
                delegate(Adjustments a) { return a.ColorNRDetail; }, delegate(Adjustments a, float v) { a.ColorNRDetail = v; }, "0"));
            Detail.Add(new ParamDef("ColorNRSmooth", "平滑度", 0, 100, 50,
                delegate(Adjustments a) { return a.ColorNRSmooth; }, delegate(Adjustments a, float v) { a.ColorNRSmooth = v; }, "0"));
        }

        public static ParamDef Find(List<ParamDef> list, string key)
        {
            foreach (ParamDef p in list) if (p.Key == key) return p;
            return null;
        }

        public static string Label(List<ParamDef> list, string key)
        {
            ParamDef p = Find(list, key);
            return p == null ? key : p.Label;
        }
    }
}
