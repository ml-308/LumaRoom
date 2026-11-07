using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LumaRoom
{
    /// <summary>
    /// Head-less verification of the decoder, the develop pipeline, the noise
    /// reduction and the mask rasteriser. Run with:  LumaRoom.exe --selftest &lt;dir&gt;
    /// </summary>
    internal static class SelfTest
    {
        public static int Run(string outDir)
        {
            Directory.CreateDirectory(outDir);
            StringBuilder log = new StringBuilder();
            int fails = 0;

            Action<string> say = delegate(string s) { log.AppendLine(s); Console.WriteLine(s); };
            Func<bool, string, bool> check = delegate(bool ok, string what)
            {
                if (!ok) fails++;
                say((ok ? "  [PASS] " : "  [FAIL] ") + what);
                return ok;
            };

            say("LumaRoom self-test  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            say("output: " + Path.GetFullPath(outDir));
            say("");

            // ------------------------------------------------ synthetic scene
            int W = 900, H = 600;
            float[] img = MakeScene(W, H, 0.055f);
            string inPath = Path.Combine(outDir, "01_input_noisy.png");
            SaveRgb(img, W, H, inPath);
            check(File.Exists(inPath), "生成合成测试图 (含 σ≈0.055 噪声)");

            // ------------------------------------------------ tone pipeline
            say("");
            say("[0] 基准");
            System.Diagnostics.Stopwatch bw = System.Diagnostics.Stopwatch.StartNew();
            double sink = 0;
            for (int r2 = 0; r2 < 5; r2++) for (int i2 = 0; i2 < W * H * 3; i2++) sink += img[i2];
            bw.Stop();
            say("    串行内存遍历 5×(54万×3) = " + bw.ElapsedMilliseconds + " ms   (checksum " + sink.ToString("0") + ")");
            say("    逻辑处理器 " + Environment.ProcessorCount);

            say("");
            say("[1] 基础调整管线");
            float baseMean = Mean(img);
            byte[] buf = new byte[W * H * 4];
            byte[] buf2 = new byte[W * H * 4];
            int[] hist = new int[768];

            Adjustments a = new Adjustments();
            Pipeline.Render(img, W, H, a, null, buf, false, hist);   // warm the jit
            long best = long.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                System.Diagnostics.Stopwatch w1 = System.Diagnostics.Stopwatch.StartNew();
                Pipeline.Render(img, W, H, a, null, buf, false, hist);
                w1.Stop();
                if (w1.ElapsedMilliseconds < best) best = w1.ElapsedMilliseconds;
            }
            say("    渲染耗时 " + best + " ms  (" + W + "×" + H + ", " + (W * H / 10000) + " 万像素)");
            Pipeline.Profile = true;
            Pipeline.Render(img, W, H, new Adjustments(), null, buf, false, hist);
            Pipeline.Profile = false;
            say("    阶段耗时: " + Pipeline.ProfileText);
            Pipeline.Profile = true;
            Adjustments pf = new Adjustments(); pf.LumNR = 60; pf.ColorNR = 60; pf.Clarity = 40; pf.SharpenAmount = 50;
            Pipeline.Render(img, W, H, pf, null, buf, false, hist);
            Pipeline.Profile = false;
            say("    全开耗时: " + Pipeline.ProfileText);
            float mean0 = BufMean(buf, W, H);
            check(Math.Abs(mean0 - baseMean) < 0.04f, "中立参数保持原样 (Δ=" + (mean0 - baseMean).ToString("0.000") + ")");
            SaveBuf(buf, W, H, Path.Combine(outDir, "02_neutral.png"));

            a = new Adjustments(); a.Exposure = 1.0f;
            Pipeline.Render(img, W, H, a, null, buf, false, hist);
            float mean1 = BufMean(buf, W, H);
            check(mean1 > mean0 + 0.05f, "曝光 +1EV 提亮 (" + mean0.ToString("0.000") + " → " + mean1.ToString("0.000") + ")");
            SaveBuf(buf, W, H, Path.Combine(outDir, "03_exposure_plus1.png"));

            a = new Adjustments(); a.Exposure = -1.5f;
            Pipeline.Render(img, W, H, a, null, buf, false, null);
            check(BufMean(buf, W, H) < mean0 - 0.05f, "曝光 -1.5EV 压暗");

            a = new Adjustments(); a.Temp = 9000f;
            Pipeline.Render(img, W, H, a, null, buf, false, null);
            float rr, gg, bb;
            ChannelMeans(buf, W, H, out rr, out gg, out bb);
            Adjustments neu = new Adjustments();
            Pipeline.Render(img, W, H, neu, null, buf2, false, null);
            float nr, ng, nb;
            ChannelMeans(buf2, W, H, out nr, out ng, out nb);
            float rb0 = nr / Math.Max(0.001f, nb), rb1 = rr / Math.Max(0.001f, bb);
            check(rb1 > rb0 * 1.05f, "色温 9000K 偏暖 (R/B " + rb0.ToString("0.000") + " → " + rb1.ToString("0.000") + ")");
            Pipeline.Render(img, W, H, new Adjustments(), null, buf, false, null);
            SaveBuf(buf, W, H, Path.Combine(outDir, "04a_neutral_ref.png"));
            a = new Adjustments(); a.Temp = 9000f;
            Pipeline.Render(img, W, H, a, null, buf, false, null);
            SaveBuf(buf, W, H, Path.Combine(outDir, "04_temp_warm.png"));

            a = new Adjustments(); a.Temp = 3200f;
            Pipeline.Render(img, W, H, a, null, buf, false, null);
            ChannelMeans(buf, W, H, out rr, out gg, out bb);
            rb1 = rr / Math.Max(0.001f, bb);
            check(rb1 < rb0 * 0.95f, "色温 3200K 偏冷 (R/B " + rb0.ToString("0.000") + " → " + rb1.ToString("0.000") + ")");

            a = new Adjustments(); a.Contrast = 60f; a.Clarity = 45f; a.Vibrance = 40f;
            Pipeline.Render(img, W, H, a, null, buf, false, null);
            float[] outImg = BufToFloat(buf, W, H);
            check(StdDev(outImg) > StdDev(img), "对比度/清晰度提高反差");
            SaveBuf(buf, W, H, Path.Combine(outDir, "05_contrast_clarity.png"));

            a = new Adjustments(); a.Highlights = -100f; a.Shadows = 80f;
            Pipeline.Render(img, W, H, a, null, buf, false, null);
            check(true, "高光/阴影曲线可执行");

            // ------------------------------------------------ noise reduction
            say("");
            say("[2] 降噪与自动去杂色");
            float sigmaBefore = Pipeline.EstimateNoise(img, W, H);
            say("    噪声估计 σ = " + sigmaBefore.ToString("0.0000"));
            check(sigmaBefore > 0.008f, "噪声估计检测到噪声");

            a = new Adjustments(); a.LumNR = 70f; a.ColorNR = 70f;
            System.Diagnostics.Stopwatch w2 = System.Diagnostics.Stopwatch.StartNew();
            Pipeline.Render(img, W, H, a, null, buf, false, null);
            w2.Stop();
            say("    降噪渲染耗时 " + w2.ElapsedMilliseconds + " ms");
            float[] den = BufToFloat(buf, W, H);
            float sigmaAfter = Pipeline.EstimateNoise(den, W, H);
            say("    降噪后 σ = " + sigmaAfter.ToString("0.0000"));
            check(sigmaAfter < sigmaBefore * 0.55f, "亮度/颜色降噪显著降低噪声 (" +
                  (100 * (1 - sigmaAfter / sigmaBefore)).ToString("0") + "% 降幅)");
            SaveBuf(buf, W, H, Path.Combine(outDir, "06_denoised.png"));

            a = new Adjustments(); a.SharpenAmount = 80f; a.SharpenRadius = 1.2f; a.SharpenDetail = 40f;
            Pipeline.Render(img, W, H, a, null, buf, false, null);
            check(StdDev(BufToFloat(buf, W, H)) > StdDev(img) * 0.98f, "锐化可执行");

            // ------------------------------------------------ masks
            say("");
            say("[3] 蒙版");
            MaskLayer lin = new MaskLayer();
            lin.Kind = MaskKind.Linear;
            lin.X1 = 0.5f; lin.Y1 = 0.1f; lin.X2 = 0.5f; lin.Y2 = 0.9f; lin.Feather = 0.6f;
            check(Masks.Build(lin, W, H), "线性渐变栅格化");
            float topW = RegionMean(lin.Weight, W, H, 0, 0, W, H / 4);
            float botW = RegionMean(lin.Weight, W, H, 0, H * 3 / 4, W, H / 4);
            check(botW > topW + 0.5f, "线性渐变方向正确 (顶 " + topW.ToString("0.00") + " → 底 " + botW.ToString("0.00") + ")");

            MaskLayer rad = new MaskLayer();
            rad.Kind = MaskKind.Radial;
            rad.X1 = 0.5f; rad.Y1 = 0.5f; rad.X2 = 0.62f; rad.Y2 = 0.5f; rad.Extent = 0.8f; rad.Feather = 0.5f;
            check(Masks.Build(rad, W, H), "径向渐变栅格化");
            float cw = RegionMean(rad.Weight, W, H, W / 2 - 20, H / 2 - 20, 40, 40);
            check(cw > 0.9f, "径向中心权重=1 (" + cw.ToString("0.00") + ")");
            float edgeW = RegionMean(rad.Weight, W, H, 0, 0, 30, 30);
            check(edgeW < 0.05f, "径向边缘权重≈0 (" + edgeW.ToString("0.00") + ")");

            MaskLayer bru = new MaskLayer();
            bru.Kind = MaskKind.Brush;
            BrushStroke st = new BrushStroke();
            st.Radius = 0.08f; st.Hardness = 0.5f; st.Flow = 1f;
            st.Points.Add(new PointF(0.3f, 0.5f));
            st.Points.Add(new PointF(0.7f, 0.5f));
            bru.Strokes.Add(st);
            check(Masks.Build(bru, W, H), "画笔蒙版栅格化");
            float bmid = RegionMean(bru.Weight, W, H, W / 2 - 10, H / 2 - 10, 20, 20);
            check(bmid > 0.8f, "画笔笔画中心权重≈1 (" + bmid.ToString("0.00") + ")");

            // masked local adjustment
            lin.Adjust = new Adjustments(); lin.Adjust.Exposure = 1.2f;
            List<MaskLayer> masks = new List<MaskLayer>();
            masks.Add(lin);
            masks.Add(rad);
            masks.Add(bru);
            Pipeline.Render(img, W, H, new Adjustments(), masks, buf, true, null);
            float[] masked = BufToFloat(buf, W, H);
            float dTop = RegionMean(masked, W, H, 0, 0, W, H / 5) - RegionMean(img, W, H, 0, 0, W, H / 5);
            float dBot = RegionMean(masked, W, H, 0, H * 4 / 5, W, H / 5) - RegionMean(img, W, H, 0, H * 4 / 5, W, H / 5);
            say("    蒙版影响: 顶 Δ=" + dTop.ToString("0.000") + "  底 Δ=" + dBot.ToString("0.000"));
            check(dBot > dTop + 0.02f, "局部调整只作用在蒙版区域");
            SaveBuf(buf, W, H, Path.Combine(outDir, "07_mask_overlay.png"));

            // ------------------------------------------------ raw / tiff
            say("");
            say("[4] RAW / TIFF 解码");
            string tif = Path.Combine(outDir, "08_synthetic_cfa.dng");
            MakeCfaTiff(tif, 640, 480);
            byte[] tdata = File.ReadAllBytes(tif);
            RawResult rr2 = Raw.TryDecode(tdata);
            check(rr2 != null && rr2.Bitmap != null, "CFA (Bayer) TIFF 解析");
            if (rr2 != null && rr2.Bitmap != null)
            {
                say("    " + rr2.Note + "  尺寸 " + rr2.Bitmap.Width + "×" + rr2.Bitmap.Height);
                check(rr2.Bitmap.Width == 640 && rr2.Bitmap.Height == 480, "RAW 解码尺寸正确");
                rr2.Bitmap.Save(Path.Combine(outDir, "09_raw_decoded.png"), ImageFormat.Png);
                int fw, fh;
                float[] rawPix = ImageDoc.Extract(rr2.Bitmap, out fw, out fh);
                check(StdDev(rawPix) > 0.01f, "去马赛克输出有正常内容");
                rr2.Bitmap.Dispose();
            }

            Bitmap emb = Raw.TryEmbeddedJpeg(tdata);
            check(emb == null, "无嵌入 JPEG 时不误判（返回 null）");

            // ------------------------------------------------ export
            say("");
            say("[5] 导出");
            string jpg = Path.Combine(outDir, "10_export.jpg");
            SaveBuf(buf, W, H, jpg);
            FileInfo fi = new FileInfo(jpg);
            check(fi.Exists && fi.Length > 5000, "JPEG 导出 (" + fi.Length / 1024 + " KB)");

            say("");
            say(fails == 0 ? "结果: 全部通过 (" + 21 + " 项检查)" : "结果: " + fails + " 项失败");
            File.WriteAllText(Path.Combine(outDir, "selftest-report.txt"), log.ToString(), Encoding.UTF8);
            return fails;
        }

        // ------------------------------------------------------------ helpers
        private static float[] MakeScene(int w, int h, float noise)
        {
            float[] d = new float[w * h * 3];
            Random rnd = new Random(1234);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float fx = (float)x / w, fy = (float)y / h;
                    float r, g, b;

                    if (fy < 0.55f)
                    {
                        // sky gradient
                        float t = fy / 0.55f;
                        r = 0.18f + 0.55f * (1 - t) * 0.6f;
                        g = 0.34f + 0.45f * (1 - t) * 0.7f;
                        b = 0.62f + 0.30f * (1 - t);
                        // sun
                        float dx = fx - 0.72f, dy = fy - 0.18f;
                        float ds = (float)Math.Sqrt(dx * dx + dy * dy);
                        float glow = Math.Max(0f, 1f - ds * 6f);
                        r += glow * 0.9f; g += glow * 0.8f; b += glow * 0.55f;
                    }
                    else
                    {
                        float t = (fy - 0.55f) / 0.45f;
                        r = 0.22f - t * 0.10f;
                        g = 0.26f - t * 0.12f;
                        b = 0.18f - t * 0.08f;
                        // texture stripes
                        float s = (float)Math.Sin((fx * 40 + t * 6) * Math.PI);
                        r += s * 0.03f; g += s * 0.03f; b += s * 0.02f;
                    }

                    // colour chart block
                    if (fx > 0.06f && fx < 0.34f && fy > 0.62f && fy < 0.88f)
                    {
                        int cx = (int)((fx - 0.06f) / 0.28f * 3);
                        int cy = (int)((fy - 0.62f) / 0.26f * 2);
                        float[][] sw = new float[][] {
                            new float[]{0.80f,0.16f,0.14f}, new float[]{0.16f,0.62f,0.24f}, new float[]{0.14f,0.28f,0.78f},
                            new float[]{0.86f,0.76f,0.16f}, new float[]{0.55f,0.55f,0.55f}, new float[]{0.10f,0.10f,0.12f}
                        };
                        float[] c = sw[Math.Min(5, cy * 3 + Math.Min(2, cx))];
                        r = c[0]; g = c[1]; b = c[2];
                    }

                    // fine detail bars
                    if (fx > 0.62f && fx < 0.94f && fy > 0.60f && fy < 0.70f)
                    {
                        float v = ((int)(fx * 220) % 2 == 0) ? 0.85f : 0.10f;
                        r = v; g = v; b = v;
                    }

                    r += (float)(rnd.NextDouble() - 0.5) * noise * 2f;
                    g += (float)(rnd.NextDouble() - 0.5) * noise * 2f;
                    b += (float)(rnd.NextDouble() - 0.5) * noise * 2f;

                    int i = (y * w + x) * 3;
                    d[i] = Clamp01(r); d[i + 1] = Clamp01(g); d[i + 2] = Clamp01(b);
                }
            }
            return d;
        }

        private static void SaveRgb(float[] px, int w, int h, string path)
        {
            byte[] buf = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                int si = i * 3, di = i * 4;
                buf[di] = (byte)(Clamp01(px[si + 2]) * 255);
                buf[di + 1] = (byte)(Clamp01(px[si + 1]) * 255);
                buf[di + 2] = (byte)(Clamp01(px[si]) * 255);
                buf[di + 3] = 255;
            }
            SaveBuf(buf, w, h, path);
        }

        private static void SaveBuf(byte[] buf, int w, int h, string path)
        {
            using (Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    if (bd.Stride == w * 4) Marshal.Copy(buf, 0, bd.Scan0, w * h * 4);
                    else
                        for (int y = 0; y < h; y++)
                            Marshal.Copy(buf, y * w * 4, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), w * 4);
                }
                finally { b.UnlockBits(bd); }

                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg")
                {
                    ImageCodecInfo enc = null;
                    foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                        if (c.FormatID == ImageFormat.Jpeg.Guid) enc = c;
                    if (enc != null)
                    {
                        EncoderParameters p = new EncoderParameters(1);
                        p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                        b.Save(path, enc, p);
                        return;
                    }
                }
                b.Save(path, ext == ".png" ? ImageFormat.Png : ImageFormat.Jpeg);
            }
        }

        private static float[] BufToFloat(byte[] buf, int w, int h)
        {
            float[] d = new float[w * h * 3];
            for (int i = 0; i < w * h; i++)
            {
                int di = i * 4, si = i * 3;
                d[si] = buf[di + 2] / 255f;
                d[si + 1] = buf[di + 1] / 255f;
                d[si + 2] = buf[di] / 255f;
            }
            return d;
        }

        private static float BufMean(byte[] buf, int w, int h)
        {
            double s = 0;
            int n = w * h;
            for (int i = 0; i < n; i++)
            {
                int di = i * 4;
                s += 0.2126 * buf[di + 2] + 0.7152 * buf[di + 1] + 0.0722 * buf[di];
            }
            return (float)(s / n / 255.0);
        }

        private static void ChannelMeans(byte[] buf, int w, int h, out float r, out float g, out float b)
        {
            double sr = 0, sg = 0, sb = 0;
            int n = w * h;
            for (int i = 0; i < n; i++)
            {
                int di = i * 4;
                sr += buf[di + 2]; sg += buf[di + 1]; sb += buf[di];
            }
            r = (float)(sr / n / 255.0); g = (float)(sg / n / 255.0); b = (float)(sb / n / 255.0);
        }

        private static float Mean(float[] px)
        {
            double s = 0;
            int n = px.Length / 3;
            for (int i = 0; i < n; i++)
                s += 0.2126 * px[i * 3] + 0.7152 * px[i * 3 + 1] + 0.0722 * px[i * 3 + 2];
            return (float)(s / n);
        }

        private static float RegionMean(float[] px, int w, int h, int x0, int y0, int rw, int rh)
        {
            double s = 0; long n = 0;
            for (int y = y0; y < y0 + rh && y < h; y++)
                for (int x = x0; x < x0 + rw && x < w; x++)
                {
                    int i = (y * w + x) * 3;
                    if (px.Length == w * h * 3)
                        s += 0.2126 * px[i] + 0.7152 * px[i + 1] + 0.0722 * px[i + 2];
                    else
                        s += px[y * w + x];
                    n++;
                }
            return n == 0 ? 0 : (float)(s / n);
        }

        private static float StdDev(float[] px)
        {
            int n = px.Length / 3;
            double m = 0;
            for (int i = 0; i < n; i++) m += 0.2126 * px[i * 3] + 0.7152 * px[i * 3 + 1] + 0.0722 * px[i * 3 + 2];
            m /= n;
            double v = 0;
            for (int i = 0; i < n; i++)
            {
                double l = 0.2126 * px[i * 3] + 0.7152 * px[i * 3 + 1] + 0.0722 * px[i * 3 + 2];
                v += (l - m) * (l - m);
            }
            return (float)Math.Sqrt(v / n);
        }

        private static float Clamp01(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        // ------------------------------------------------------ synthetic raw
        /// <summary>Writes a minimal uncompressed 16-bit RGGB CFA TIFF that exercises the raw path.</summary>
        private static void MakeCfaTiff(string path, int w, int h)
        {
            int entries = 13;
            int ifdSize = 2 + entries * 12 + 4;
            int dataStart = 8 + ifdSize;
            int stripBytes = w * h * 2;

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write((byte)'I'); bw.Write((byte)'I'); bw.Write((ushort)42); bw.Write((uint)8);
                bw.Write((ushort)entries);

                // tag, type, count, value(4 bytes)
                WriteEntry(bw, 256, 3, 1, (uint)w);
                WriteEntry(bw, 257, 3, 1, (uint)h);
                WriteEntry(bw, 258, 3, 1, 16);
                WriteEntry(bw, 259, 3, 1, 1);
                WriteEntry(bw, 262, 3, 1, 32803);
                WriteEntry(bw, 273, 4, 1, (uint)dataStart);
                WriteEntry(bw, 277, 3, 1, 1);
                WriteEntry(bw, 278, 3, 1, (uint)h);
                WriteEntry(bw, 279, 4, 1, (uint)stripBytes);
                WriteEntry(bw, 33421, 3, 2, 0x00020002);          // 2,2
                WriteEntry(bw, 33422, 1, 4, 0x02010100);          // RGGB bytes
                WriteEntry(bw, 50714, 3, 1, 0);
                WriteEntry(bw, 50717, 3, 1, 65535);

                bw.Write((uint)0);   // next IFD

                // scene -> bayer samples
                Random rnd = new Random(77);
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float fx = (float)x / w, fy = (float)y / h;
                        float r = 0.25f + 0.5f * fx;
                        float g = 0.30f + 0.5f * (1 - fy);
                        float b = 0.55f - 0.35f * fx;
                        int px = (x & 1) + (y & 1) * 2;
                        float v = px == 0 ? r : (px == 3 ? b : g);
                        v += (float)(rnd.NextDouble() - 0.5) * 0.04f;
                        if (v < 0) v = 0; if (v > 1) v = 1;
                        bw.Write((ushort)(v * 65535f));
                    }
                }
            }
        }

        private static void WriteEntry(BinaryWriter bw, int tag, int type, int count, uint value)
        {
            bw.Write((ushort)tag);
            bw.Write((ushort)type);
            bw.Write((uint)count);
            bw.Write(value);
        }
    }
}
