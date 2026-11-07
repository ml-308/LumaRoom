using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace LumaRoom
{
    /// <summary>
    /// Image loading. Handles ordinary formats through GDI+, and camera raw files
    /// through a small built-in TIFF reader (bilinear demosaic) with a fallback to
    /// the embedded JPEG preview that every raw file carries.
    /// </summary>
    internal static class Decoder
    {
        public static ImageDoc Open(string path, int maxSide)
        {
            ImageDoc doc = new ImageDoc();
            doc.Path = path;
            doc.DisplayName = System.IO.Path.GetFileName(path);

            byte[] data = File.ReadAllBytes(path);
            Bitmap bmp = null;

            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            bool rawExt = IsRawExt(ext);

            if (rawExt)
            {
                RawResult rr = Raw.TryDecode(data);
                if (rr != null && rr.Bitmap != null)
                {
                    bmp = rr.Bitmap;
                    doc.FromRaw = true;
                    doc.RawNote = rr.Note;
                }
                if (bmp == null)
                {
                    Bitmap emb = Raw.TryEmbeddedJpeg(data);
                    if (emb != null)
                    {
                        bmp = emb;
                        doc.FromRaw = true;
                        doc.RawNote = "嵌入预览";
                    }
                }
            }

            if (bmp == null)
            {
                bmp = FromBytes(data);
                if (bmp != null) bmp = ApplyExifOrientation(bmp);
            }

            if (bmp == null) return null;

            doc.Full = bmp;
            doc.BuildPreview(maxSide);
            doc.Adj = new Adjustments();
            doc.PushHistory("打开图像");
            return doc;
        }

        public static bool IsRawExt(string ext)
        {
            switch (ext)
            {
                case ".dng": case ".cr2": case ".cr3": case ".nef": case ".arw":
                case ".raf": case ".orf": case ".rw2": case ".pef": case ".srw":
                case ".sr2": case ".raw": case ".3fr": case ".iiq": case ".mrw": case ".x3f":
                    return true;
            }
            return false;
        }

        public static Bitmap FromBytes(byte[] data)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(data))
                using (Image img = Image.FromStream(ms, true, false))
                {
                    Bitmap b = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
                    }
                    return b;
                }
            }
            catch { return null; }
        }

        private static Bitmap ApplyExifOrientation(Bitmap b)
        {
            try
            {
                const int OrientationId = 0x0112;
                bool has = false;
                foreach (int id in b.PropertyIdList) if (id == OrientationId) { has = true; break; }
                if (!has) return b;
                int o = b.GetPropertyItem(OrientationId).Value[0];
                if (o <= 1 || o > 8) return b;
                RotateFlipType rf = RotateFlipType.RotateNoneFlipNone;
                switch (o)
                {
                    case 2: rf = RotateFlipType.RotateNoneFlipX; break;
                    case 3: rf = RotateFlipType.Rotate180FlipNone; break;
                    case 4: rf = RotateFlipType.Rotate180FlipX; break;
                    case 5: rf = RotateFlipType.Rotate90FlipX; break;
                    case 6: rf = RotateFlipType.Rotate90FlipNone; break;
                    case 7: rf = RotateFlipType.Rotate270FlipX; break;
                    case 8: rf = RotateFlipType.Rotate270FlipNone; break;
                }
                b.RotateFlip(rf);
                return b;
            }
            catch { return b; }
        }

        /// <summary>Small thumbnail for the filmstrip.</summary>
        public static Bitmap Thumb(string path, int w, int h)
        {
            try
            {
                byte[] data = File.ReadAllBytes(path);
                Bitmap src = null;
                if (IsRawExt(System.IO.Path.GetExtension(path).ToLowerInvariant()))
                {
                    RawResult rr = Raw.TryDecodeMini(data, w, h);
                    if (rr != null) src = rr.Bitmap;
                    if (src == null) src = Raw.TryEmbeddedJpeg(data);
                }
                if (src == null) src = FromBytes(data);
                if (src == null) return null;

                Bitmap t = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(t))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.Clear(Theme.PanelDeep);
                    float s = Math.Min((float)w / src.Width, (float)h / src.Height);
                    int dw = Math.Max(1, (int)(src.Width * s)), dh = Math.Max(1, (int)(src.Height * s));
                    g.DrawImage(src, new Rectangle((w - dw) / 2, (h - dh) / 2, dw, dh));
                }
                src.Dispose();
                return t;
            }
            catch { return null; }
        }
    }

    internal sealed class RawResult
    {
        public Bitmap Bitmap;
        public string Note = "";
    }

    /// <summary>Minimal TIFF/raw reader: IFD walk, strip decode, CFA demosaic.</summary>
    internal static class Raw
    {
        // ---------------------------------------------------------------- TIFF
        private sealed class Tiff
        {
            public byte[] D;
            public bool Le;
            public int U16(long o) { return Le ? D[o] | (D[o + 1] << 8) : (D[o] << 8) | D[o + 1]; }
            public int I16(long o) { return (short)U16(o); }
            public long U32(long o) { return Le ? (uint)(D[o] | (D[o + 1] << 8) | (D[o + 2] << 16) | (D[o + 3] << 24))
                                               : (uint)((D[o] << 24) | (D[o + 1] << 16) | (D[o + 2] << 8) | D[o + 3]); }
            public int I32(long o) { return (int)U32(o); }
            public float F32(long o) { return BitConverter.ToSingle(Le ? new byte[] { D[o], D[o + 1], D[o + 2], D[o + 3] } : new byte[] { D[o + 3], D[o + 2], D[o + 1], D[o] }, 0); }
        }

        private sealed class Entry
        {
            public int Tag;
            public int Type;
            public int Count;
            public long ValueOffset;
        }

        private sealed class Ifd
        {
            public Dictionary<int, Entry> Tags = new Dictionary<int, Entry>();
            public bool Has(int t) { return Tags.ContainsKey(t); }
            public Entry Get(int t) { Entry e; Tags.TryGetValue(t, out e); return e; }
        }

        private static int TypeSize(int t)
        {
            switch (t)
            {
                case 1: case 2: case 6: case 7: return 1;
                case 3: case 8: return 2;
                case 4: case 9: case 11: return 4;
                case 5: case 10: case 12: return 8;
                default: return 1;
            }
        }

        private static Ifd ReadIfd(Tiff t, long offset)
        {
            Ifd ifd = new Ifd();
            if (offset <= 0 || offset + 2 > t.D.Length) return ifd;
            int n = t.U16(offset);
            long p = offset + 2;
            for (int i = 0; i < n; i++)
            {
                long eo = p + i * 12;
                if (eo + 12 > t.D.Length) break;
                Entry e = new Entry();
                e.Tag = t.U16(eo);
                e.Type = t.U16(eo + 2);
                e.Count = t.I32(eo + 4);
                int sz = TypeSize(e.Type) * e.Count;
                e.ValueOffset = sz <= 4 ? eo + 8 : t.U32(eo + 8);
                ifd.Tags[e.Tag] = e;
            }
            return ifd;
        }

        private static long[] ReadInts(Tiff t, Ifd ifd, int tag)
        {
            Entry e = ifd.Get(tag);
            if (e == null) return null;
            int count = Math.Min(e.Count, 4096);
            long[] v = new long[count];
            for (int i = 0; i < count; i++)
            {
                long o = e.ValueOffset + i * TypeSize(e.Type);
                if (o + TypeSize(e.Type) > t.D.Length) return v;
                if (e.Type == 3) v[i] = t.U16(o);
                else if (e.Type == 4) v[i] = t.U32(o);
                else if (e.Type == 1) v[i] = t.D[o];
                else if (e.Type == 8) v[i] = t.I16(o);
                else if (e.Type == 9) v[i] = t.I32(o);
            }
            return v;
        }

        private static float[] ReadRats(Tiff t, Ifd ifd, int tag)
        {
            Entry e = ifd.Get(tag);
            if (e == null) return null;
            int n = Math.Min(e.Count, 64);
            float[] v = new float[n];
            for (int i = 0; i < n; i++)
            {
                long o = e.ValueOffset + i * 8;
                if (o + 8 > t.D.Length) return v;
                if (e.Type == 5)
                {
                    long num = t.U32(o), den = t.U32(o + 4);
                    v[i] = den == 0 ? 0 : (float)num / den;
                }
                else if (e.Type == 10)
                {
                    int num = t.I32(o), den = t.I32(o + 4);
                    v[i] = den == 0 ? 0 : (float)num / den;
                }
                else if (e.Type == 11) v[i] = t.F32(o);
            }
            return v;
        }

        private static long Val(Tiff t, Ifd ifd, int tag, long def)
        {
            long[] v = ReadInts(t, ifd, tag);
            return (v == null || v.Length == 0) ? def : v[0];
        }

        // ------------------------------------------------------------- public
        public static RawResult TryDecode(byte[] data)
        {
            try { return DecodeCore(data, 0); }
            catch { return null; }
        }

        public static RawResult TryDecodeMini(byte[] data, int tw, int th)
        {
            try { return DecodeCore(data, Math.Max(tw, th) * 2); }
            catch { return null; }
        }

        private static RawResult DecodeCore(byte[] data, int maxSide)
        {
            if (data.Length < 16) return null;
            Tiff t = new Tiff();
            t.D = data;
            if (data[0] == 0x49 && data[1] == 0x49) t.Le = true;
            else if (data[0] == 0x4D && data[1] == 0x4D) t.Le = false;
            else return null;
            if (t.U16(2) != 42) return null;

            long first = t.U32(4);
            Ifd root = ReadIfd(t, first);
            if (root.Tags.Count == 0) return null;

            // gather candidate image IFDs (root + subIFDs + next IFD chain)
            List<Ifd> cands = new List<Ifd>();
            cands.Add(root);
            long[] subs = ReadInts(t, root, 330);
            if (subs != null)
                foreach (long s in subs) { Ifd si = ReadIfd(t, s); if (si.Tags.Count > 0) cands.Add(si); }
            long nxt = t.U32(first + 2 + (long)t.U16(first) * 12);
            if (nxt > 0 && nxt < data.Length) { Ifd n2 = ReadIfd(t, nxt); if (n2.Tags.Count > 0) cands.Add(n2); }

            Ifd best = null; long bestPix = 0; RawResult res = null;
            foreach (Ifd c in cands)
            {
                long w = Val(t, c, 256, 0), h = Val(t, c, 257, 0);
                if (w <= 0 || h <= 0) continue;
                long photo = Val(t, c, 262, -1);
                bool cfa = photo == 32803 || photo == 34892 || photo == 1;   // CFA / LinearRaw / BlackIsZero 16bit
                int bits = (int)Val(t, c, 258, 8);
                if (!cfa && photo != 2) continue;
                if (cfa && bits < 12) continue;
                long pix = w * h;
                if (pix > bestPix) { bestPix = pix; best = c; }
            }
            if (best == null) return null;

            int W = (int)Val(t, best, 256, 0), H = (int)Val(t, best, 257, 0);
            if (W <= 8 || H <= 8) return null;

            long compression = Val(t, best, 259, 1);
            int spp = (int)Val(t, best, 277, 1);
            int photo2 = (int)Val(t, best, 262, 1);

            if (compression != 1 && compression != 32773)
                return null;   // compressed (lossless jpeg / vendor) -> caller falls back

            long[] offs = ReadInts(t, best, 273);
            long[] cnts = ReadInts(t, best, 279);
            if (offs == null || offs.Length == 0) return null;
            long rowsPerStrip = Val(t, best, 278, H);

            int bps = (int)Val(t, best, 258, 16);
            if (bps != 8 && bps != 16) return null;
            int bytesPer = bps / 8;

            long total = (long)W * H * spp * bytesPer;
            if (total > 900L * 1024 * 1024) return null;

            byte[] raw = new byte[total];
            int pos = 0;
            for (int s = 0; s < offs.Length; s++)
            {
                long o = offs[s];
                long c = (cnts != null && s < cnts.Length) ? cnts[s] : 0;
                if (o <= 0 || o >= data.Length) break;
                int n = (int)Math.Min(c > 0 ? c : (W * rowsPerStrip * spp * bytesPer), data.Length - o);
                if (pos + n > raw.Length) n = raw.Length - pos;
                if (n <= 0) break;
                if (compression == 32773) n = PackBits(data, (int)o, n, raw, pos);
                else Buffer.BlockCopy(data, (int)o, raw, pos, n);
                pos += n;
            }
            if (pos < W * H * spp * bytesPer / 2) return null;

            float[] img = new float[(long)W * H * 3];
            float black = 0f, white = 65535f;
            long[] bl = ReadInts(t, best, 50714);
            if (bl != null && bl.Length > 0)
            {
                black = 0;
                for (int i = 0; i < bl.Length; i++) black += bl[i];
                black /= bl.Length;
            }
            long[] wl = ReadInts(t, best, 50717);
            if (wl != null && wl.Length > 0) white = wl[0];
            if (white <= black) white = bps == 16 ? 65535f : 255f;

            bool isCfa = (photo2 == 32803) || (spp == 1 && bps == 16);
            int[] cfaPat = new int[4] { 0, 1, 1, 2 };  // RGGB default

            if (isCfa && spp == 1)
            {
                long[] pat = ReadInts(t, best, 33422);
                if (pat != null && pat.Length >= 4)
                    for (int i = 0; i < 4; i++) cfaPat[i] = (int)(pat[i] & 3);
                Demosaic(raw, img, W, H, bps, black, white, cfaPat);
            }
            else
            {
                // RGB / gray linear
                int ch = spp;
                float scale = 1f / Math.Max(1e-6f, white);
                for (int i = 0; i < W * H; i++)
                {
                    int si = i * ch * bytesPer;
                    for (int c = 0; c < 3; c++)
                    {
                        float v;
                        if (ch == 1) v = ReadSample(raw, si, bps) - black;
                        else v = ReadSample(raw, si + Math.Min(c, ch - 1) * bytesPer, bps) - black;
                        v = Math.Max(0f, Math.Min(1f, v * scale));
                        img[(long)i * 3 + c] = Photo.Lin2Srgb(v);
                    }
                }
            }

            // white balance from AsShotNeutral, else gray world on the linear data
            float[] neutral = ReadRats(t, best, 50728);
            float gr = 1f, gg = 1f, gb = 1f;
            if (neutral != null && neutral.Length >= 3 && neutral[0] > 0 && neutral[1] > 0 && neutral[2] > 0)
            {
                float nr = 1f / neutral[0], ng = 1f / neutral[1], nb = 1f / neutral[2];
                float m = ng;
                gr = nr / m; gg = 1f; gb = nb / m;
                if (gr < 0.2f || gr > 5f || gb < 0.2f || gb > 5f) { gr = gg = gb = 1f; }
            }
            else
            {
                double sr = 0, sg = 0, sb = 0; long cnt = 0;
                for (long i = 0; i < (long)W * H; i += 7)
                {
                    sr += img[i * 3]; sg += img[i * 3 + 1]; sb += img[i * 3 + 2]; cnt++;
                }
                if (cnt > 0 && sr > 0 && sg > 0 && sb > 0)
                {
                    double avg = (sr + sg + sb) / (3.0 * cnt);
                    gr = (float)(avg / (sr / cnt)); gg = (float)(avg / (sg / cnt)); gb = (float)(avg / (sb / cnt));
                    if (gr < 0.5f || gr > 2f) gr = 1f;
                    if (gb < 0.5f || gb > 2f) gb = 1f;
                }
            }
            if (Math.Abs(gr - 1) > 0.001f || Math.Abs(gb - 1) > 0.001f)
            {
                float[] tmp = new float[img.Length];
                // white balance must be applied in linear space -> undo sRGB encode, gain, re-encode
                for (long i = 0; i < (long)W * H; i++)
                {
                    tmp[i * 3] = Photo.Srgb2Lin(img[i * 3]) * gr;
                    tmp[i * 3 + 1] = Photo.Srgb2Lin(img[i * 3 + 1]) * gg;
                    tmp[i * 3 + 2] = Photo.Srgb2Lin(img[i * 3 + 2]) * gb;
                }
                for (long i = 0; i < (long)W * H; i++)
                {
                    img[i * 3] = Photo.Lin2Srgb(Math.Min(1f, tmp[i * 3]));
                    img[i * 3 + 1] = Photo.Lin2Srgb(Math.Min(1f, tmp[i * 3 + 1]));
                    img[i * 3 + 2] = Photo.Lin2Srgb(Math.Min(1f, tmp[i * 3 + 2]));
                }
            }

            Bitmap bmp = ToBitmap(img, W, H);
            res = new RawResult();
            res.Bitmap = bmp;
            res.Note = "线性解码";
            return res;
        }

        private static float ReadSample(byte[] raw, int off, int bps)
        {
            if (bps == 16) return (float)(raw[off] | (raw[off + 1] << 8));
            return raw[off];
        }

        private static void Demosaic(byte[] raw, float[] img, int W, int H, int bps, float black, float white, int[] pat)
        {
            float scale = 1f / Math.Max(1f, white - black);
            float[] lin = new float[(long)W * H];
            for (long i = 0; i < (long)W * H; i++)
            {
                float v = ReadSample(raw, (int)(i * (bps / 8)), bps) - black;
                if (v < 0) v = 0;
                lin[i] = v * scale;
                if (lin[i] > 1f) lin[i] = 1f;
            }

            for (int y = 0; y < H; y++)
            {
                int ym = y > 0 ? y - 1 : 0;
                int yp = y < H - 1 ? y + 1 : H - 1;
                for (int x = 0; x < W; x++)
                {
                    int xm = x > 0 ? x - 1 : 0;
                    int xp = x < W - 1 ? x + 1 : W - 1;
                    long i = (long)y * W + x;
                    float v = lin[i];
                    int c = pat[(y & 1) * 2 + (x & 1)];
                    float r, g, b;
                    if (c == 0)
                    {
                        r = v;
                        g = 0.25f * (lin[(long)ym * W + x] + lin[(long)yp * W + x] + lin[(long)y * W + xm] + lin[(long)y * W + xp]);
                        b = 0.25f * (lin[(long)ym * W + xm] + lin[(long)ym * W + xp] + lin[(long)yp * W + xm] + lin[(long)yp * W + xp]);
                    }
                    else if (c == 2)
                    {
                        b = v;
                        g = 0.25f * (lin[(long)ym * W + x] + lin[(long)yp * W + x] + lin[(long)y * W + xm] + lin[(long)y * W + xp]);
                        r = 0.25f * (lin[(long)ym * W + xm] + lin[(long)ym * W + xp] + lin[(long)yp * W + xm] + lin[(long)yp * W + xp]);
                    }
                    else
                    {
                        g = v;
                        int rowLeft = pat[(y & 1) * 2];
                        bool rowHasR = rowLeft == 0;
                        float hor = 0.5f * (lin[(long)y * W + xm] + lin[(long)y * W + xp]);
                        float ver = 0.5f * (lin[(long)ym * W + x] + lin[(long)yp * W + x]);
                        if (rowHasR) { r = hor; b = ver; }
                        else { r = ver; b = hor; }
                    }
                    long o = i * 3;
                    img[o] = Photo.Lin2Srgb(r);
                    img[o + 1] = Photo.Lin2Srgb(g);
                    img[o + 2] = Photo.Lin2Srgb(b);
                }
            }
        }

        private static int PackBits(byte[] src, int start, int len, byte[] dst, int dstOff)
        {
            int i = start, end = start + len, o = dstOff;
            while (i < end)
            {
                int n = (sbyte)src[i++];
                if (n >= 0)
                {
                    int cnt = n + 1;
                    for (int k = 0; k < cnt && i < end && o < dst.Length; k++) dst[o++] = src[i++];
                }
                else if (n != -128)
                {
                    int cnt = 1 - n;
                    if (i >= end) break;
                    byte v = src[i++];
                    for (int k = 0; k < cnt && o < dst.Length; k++) dst[o++] = v;
                }
            }
            return o - dstOff;
        }

        private static Bitmap ToBitmap(float[] img, int W, int H)
        {
            Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = bd.Stride;
                byte[] buf = new byte[stride * H];
                for (int y = 0; y < H; y++)
                {
                    int ro = y * stride;
                    long so = (long)y * W * 3;
                    for (int x = 0; x < W; x++)
                    {
                        int o = ro + x * 4;
                        buf[o] = (byte)(img[so + 2] * 255f + 0.5f);
                        buf[o + 1] = (byte)(img[so + 1] * 255f + 0.5f);
                        buf[o + 2] = (byte)(img[so] * 255f + 0.5f);
                        buf[o + 3] = 255;
                        so += 3;
                    }
                }
                Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }

        /// <summary>Every raw file embeds at least one JPEG; grab the biggest one.</summary>
        public static Bitmap TryEmbeddedJpeg(byte[] d)
        {
            try
            {
                int bestStart = -1, bestLen = 0;
                for (int i = 0; i + 3 < d.Length; i++)
                {
                    if (d[i] != 0xFF || d[i + 1] != 0xD8 || d[i + 2] != 0xFF) continue;
                    int end = -1;
                    for (int j = i + 3; j + 1 < d.Length; j++)
                    {
                        if (d[j] == 0xFF && d[j + 1] == 0xD9) { end = j + 1; break; }
                    }
                    if (end < 0) break;
                    int len = end - i + 1;
                    if (len > bestLen) { bestLen = len; bestStart = i; }
                    i = end;
                }
                if (bestStart < 0 || bestLen < 4096) return null;
                byte[] jpg = new byte[bestLen];
                Buffer.BlockCopy(d, bestStart, jpg, 0, bestLen);
                return Decoder.FromBytes(jpg);
            }
            catch { return null; }
        }
    }
}
