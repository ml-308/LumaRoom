using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LumaRoom
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [STAThread]
        private static int Main(string[] args)
        {
            // 「发现新版本」检查：命令行开关 + 无人值守 probe
            UpdateKit.UpdateCheck.Configure(args);
            int? probeExit = UpdateKit.UpdateCheck.TryProbe(args);
            if (probeExit.HasValue) return probeExit.Value;

            // head-less verification mode
            if (args != null && args.Length >= 2 && args[0] == "--selftest")
                return SelfTest.Run(args[1]);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                MessageBox.Show("发生错误：\r\n" + e.Exception.Message, "LumaRoom",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                MessageBox.Show("发生严重错误：\r\n" + (ex == null ? "unknown" : ex.Message), "LumaRoom",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            MainForm form = new MainForm();

            string shotPath = null;
            string exportPath = null;
            bool demo = false;
            System.Collections.Generic.List<string> rest = new System.Collections.Generic.List<string>();
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--shot" && i + 1 < args.Length) { shotPath = args[++i]; continue; }
                    if (args[i] == "--export" && i + 1 < args.Length) { exportPath = args[++i]; continue; }
                    if (args[i] == "--demo") { demo = true; continue; }
                    rest.Add(args[i]);
                }
            }
            form.DemoMode = demo;
            form.AutoExportPath = exportPath;
            form.CloseAfterExport = exportPath != null && shotPath == null;
            if (rest.Count > 0) form.PendingFiles = rest.ToArray();

            if (shotPath != null)
                form.Shown += delegate { StartShot(form, shotPath); };

            UpdateKit.UpdatePrompt.ScheduleAutoCheck(form);
            Application.Run(form);
            Theme.Shutdown();
            return 0;
        }

        private static void StartShot(MainForm form, string path)
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 3000;
            t.Tick += delegate
            {
                t.Stop();
                try
                {
                    RECT wr;
                    GetWindowRect(form.Handle, out wr);
                    int w = wr.Right - wr.Left, h = wr.Bottom - wr.Top;

                    File.WriteAllText(Path.ChangeExtension(path, ".txt"),
                        "form.Bounds=" + form.Bounds + Environment.NewLine +
                        "windowRect=" + w + "x" + h + Environment.NewLine +
                        "client=" + form.ClientSize + Environment.NewLine +
                        "Theme.Scale=" + Theme.Scale + Environment.NewLine +
                        "screen=" + Screen.PrimaryScreen.Bounds + " work=" + Screen.PrimaryScreen.WorkingArea);

                    using (Bitmap bmp = new Bitmap(w, h))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            IntPtr hdc = g.GetHdc();
                            PrintWindow(form.Handle, hdc, 2);
                            g.ReleaseHdc(hdc);
                        }
                        bmp.Save(path, ImageFormat.Png);
                    }
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.ChangeExtension(path, ".txt"), "shot failed: " + ex); } catch { }
                }
                form.Close();
            };
            t.Start();
        }
    }
}
