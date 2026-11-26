// Shared foundation: Win32 interop, spring physics, theme/palettes, styled controls.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DynIsland
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, int f);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, int f);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint param, string vparam, uint winIni);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint param, StringBuilder vparam, uint winIni);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, LowLevelProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMSTATUS m);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO fi, uint size, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SHGetFileInfo(IntPtr pidl, uint attr, ref SHFILEINFO fi, uint size, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHParseDisplayName(string name, IntPtr bc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
        [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr p);

        public delegate IntPtr LowLevelProc(int code, IntPtr w, IntPtr l);
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc fn, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int L, R, T, B; }
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
        [DllImport("gdi32.dll")] public static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMSTATUS
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        public static void Key(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); }

        const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
        public static void SetNoActivate(IntPtr h, bool on)
        {
            if (h == IntPtr.Zero) return;
            int ex = GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
            ex = on ? (ex | WS_EX_NOACTIVATE) : (ex & ~WS_EX_NOACTIVATE);
            SetWindowLong(h, GWL_EXSTYLE, ex);
        }

        public static Point CursorPx() { POINT p; GetCursorPos(out p); return new Point(p.X, p.Y); }

        static ImageSource FromIcon(IntPtr hIcon)
        {
            if (hIcon == IntPtr.Zero) return null;
            try
            {
                // copy into a managed bitmap: the interop one can still reference the icon we destroy below
                var src = new WriteableBitmap(Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()));
                src.Freeze();
                return src;
            }
            catch { return null; }
            finally { DestroyIcon(hIcon); }
        }

        public static ImageSource FileIcon(string path)
        {
            var fi = new SHFILEINFO();
            IntPtr r = SHGetFileInfo(path, 0, ref fi, (uint)Marshal.SizeOf(fi), 0x100);
            return r == IntPtr.Zero ? null : FromIcon(fi.hIcon);
        }

        // ---- Explorer-style thumbnail (real previews for images/video/PDF, large icons otherwise).
        // The pixels are copied into a managed bitmap before the HBITMAP is freed.
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItemImageFactory { [PreserveSig] int GetImage(NSIZE size, int flags, out IntPtr phbm); }
        [StructLayout(LayoutKind.Sequential)] public struct NSIZE { public int cx, cy; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
        [StructLayout(LayoutKind.Sequential)] struct BITMAPS { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
        [StructLayout(LayoutKind.Sequential)] struct BMIH { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)] struct DIBSECTION { public BITMAPS dsBm; public BMIH dsBmih; public uint f0, f1, f2; public IntPtr dshSection; public uint dsOffset; }
        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, ref DIBSECTION ds);
        // BITMAPINFO: the header plus room for a colour table GetDIBits may touch
        [StructLayout(LayoutKind.Sequential)] struct BMI32 { public BMIH h; public uint c0, c1, c2, c3; }
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BMI32 bmi, uint usage);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

        public static ImageSource ShellThumb(string path, int size)
        {
            IntPtr hbm = IntPtr.Zero;
            try
            {
                IShellItemImageFactory f;
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
                if (f.GetImage(new NSIZE { cx = size, cy = size }, 0, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                var ds = new DIBSECTION();
                if (GetObject(hbm, Marshal.SizeOf(typeof(DIBSECTION)), ref ds) == 0 || ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32) return null;
                int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight, stride = w * 4;
                // GetObject reports biHeight positive for top-down DIBs too, so its sign can't say whether to
                // flip (Firefox's icon came out upside down). GetDIBits into a top-down buffer always lands right.
                var px = new byte[stride * h];
                var bi = new BMI32 { h = new BMIH { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 } };
                IntPtr dc = GetDC(IntPtr.Zero);
                int lines;
                try { lines = GetDIBits(dc, hbm, 0, (uint)h, px, ref bi, 0); }
                finally { ReleaseDC(IntPtr.Zero, dc); }
                if (lines != h) return null;
                // thumbnails of opaque images can come back with alpha = 0 everywhere
                bool anyAlpha = false;
                for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
                if (!anyAlpha) for (int i = 3; i < px.Length; i += 4) px[i] = 255;
                var bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, stride);
                bs.Freeze();
                return bs;
            }
            catch (Exception ex) { Log.Write("thumb " + path + ": " + ex.Message); return null; }
            finally { if (hbm != IntPtr.Zero) DeleteObject(hbm); }
        }

        // Icon for any shell parsing name (e.g. "shell:AppsFolder\<AUMID>").
        public static ImageSource ShellIcon(string parsingName)
        {
            IntPtr pidl; uint o;
            if (SHParseDisplayName(parsingName, IntPtr.Zero, out pidl, 0, out o) != 0 || pidl == IntPtr.Zero) return null;
            try
            {
                var fi = new SHFILEINFO();
                IntPtr r = SHGetFileInfo(pidl, 0, ref fi, (uint)Marshal.SizeOf(fi), 0x100 | 0x8 /*PIDL*/);
                return r == IntPtr.Zero ? null : FromIcon(fi.hIcon);
            }
            finally { CoTaskMemFree(pidl); }
        }
    }

    // Damped spring (semi-implicit Euler, sub-stepped): interruptible, keeps momentum.
    class Spring
    {
        public double Value, Target, Vel, K, D, Eps;
        public Spring(double v, double eps) { Value = Target = v; Eps = eps; K = 300; D = 30; }
        public void Tune(double k, double zeta) { K = k; D = 2 * zeta * Math.Sqrt(k); }
        public void Step(double dt) { double a = K * (Target - Value) - D * Vel; Vel += a * dt; Value += Vel * dt; }
        public bool Settle()
        {
            if (Math.Abs(Target - Value) < Eps && Math.Abs(Vel) < Eps * 4) { Value = Target; Vel = 0; return true; }
            return false;
        }
        public void Snap(double v) { Value = Target = v; Vel = 0; }
    }

    // WinRT IAsyncOperation -> Task bridge.
    static class RT
    {
        public static Task<T> Run<T>(global::Windows.Foundation.IAsyncOperation<T> op)
        {
            var tcs = new TaskCompletionSource<T>();
            op.Completed = (o, s) =>
            {
                try
                {
                    if (s == global::Windows.Foundation.AsyncStatus.Completed) tcs.TrySetResult(o.GetResults());
                    else tcs.TrySetException(new Exception("WinRT op " + s));
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
            };
            return tcs.Task;
        }
    }

    // ===================================================================== theme
    class Palette
    {
        public string Key, Label;
        public Color Island, Surface, Raised, Hover, Text, Accent, Accent2, Green, Orange, Red, Yellow;
        public Palette(string key, string label, string island, string surface, string raised, string hover, string text, string accent, string green, string orange, string red, string yellow)
        {
            Key = key; Label = label;
            Island = Theme.Hex(island); Surface = Theme.Hex(surface); Raised = Theme.Hex(raised); Hover = Theme.Hex(hover);
            Text = Theme.Hex(text); Accent = Theme.Hex(accent); Accent2 = Accent;
            Green = Theme.Hex(green); Orange = Theme.Hex(orange); Red = Theme.Hex(red); Yellow = Theme.Hex(yellow);
        }
    }

    static class Theme
    {
        public static readonly List<Palette> Palettes = new List<Palette>
        {
            new Palette("oled", "OLED", "#000000", "#0b0b0d", "#17171b", "#222228", "#f5f5f7", "#0a84ff", "#30d158", "#ff9f0a", "#ff453a", "#ffd60a"),
            new Palette("graphite", "Graphite", "#0e0e10", "#1c1c1e", "#2c2c2e", "#3a3a3c", "#f2f2f7", "#64d2ff", "#32d74b", "#ff9f0a", "#ff453a", "#ffd60a"),
            new Palette("tokyonight", "Tokyo Night", "#13131c", "#1a1b26", "#24283b", "#2f3549", "#c0caf5", "#7aa2f7", "#9ece6a", "#ff9e64", "#f7768e", "#e0af68"),
            new Palette("catppuccin", "Catppuccin", "#11111b", "#1e1e2e", "#313244", "#45475a", "#cdd6f4", "#cba6f7", "#a6e3a1", "#fab387", "#f38ba8", "#f9e2af"),
            new Palette("nord", "Nord", "#20242d", "#2e3440", "#3b4252", "#434c5e", "#eceff4", "#88c0d0", "#a3be8c", "#d08770", "#bf616a", "#ebcb8b"),
            new Palette("gruvbox", "Gruvbox", "#1d2021", "#282828", "#3c3836", "#504945", "#ebdbb2", "#83a598", "#b8bb26", "#fe8019", "#fb4934", "#fabd2f"),
            new Palette("rosepine", "Rosé Pine", "#141220", "#1f1d2e", "#26233a", "#393552", "#e0def4", "#c4a7e7", "#9ccfd8", "#f6c177", "#eb6f92", "#f6c177"),
            new Palette("aurora", "Aurora", "#070a12", "#0e1320", "#18203a", "#222c4a", "#e6ecff", "#5eead4", "#4ade80", "#fb923c", "#f87171", "#facc15"),
            new Palette("sunset", "Sunset", "#120a0d", "#1c1116", "#2b1a22", "#3a2430", "#fde8e4", "#fb923c", "#86efac", "#fdba74", "#fb7185", "#fde047"),
            new Palette("forest", "Forest", "#080d0a", "#101812", "#1a261d", "#24342a", "#e3f1e6", "#86efac", "#4ade80", "#fdba74", "#fca5a5", "#fde68a"),
        };

        public static Palette Current = Palettes[0];
        public static string CurrentKey = "oled";
        public static bool UseGradient;

        static SolidColorBrush NB() { return new SolidColorBrush(Colors.Black); }
        public static readonly SolidColorBrush Island = NB(), Surface = NB(), Raised = NB(), Hover = NB(), Text = NB(), Text2 = NB(), Text3 = NB(),
            Hairline = NB(), Highlight = NB(), Accent = NB(), Accent2 = NB(), Green = NB(), Orange = NB(), Red = NB(), Yellow = NB(), OnAccent = NB(),
            AccentSoft = NB(), RedSoft = NB(), OrangeSoft = NB(), GreenSoft = NB(), Shadow = NB(),
            // frosted Settings window: translucent tints over the acrylic backdrop
            GlassBase = NB(), GlassSide = NB(), GlassCard = NB(), GlassEdge = NB(), GlassField = NB(), AccentEdge = NB();
        public static readonly RadialGradientBrush Glow = new RadialGradientBrush { Center = new Point(0.9, 0), GradientOrigin = new Point(0.9, 0), RadiusX = 0.75, RadiusY = 0.9 };
        static readonly GradientStop w0 = new GradientStop(Colors.Transparent, 0), w1 = new GradientStop(Colors.Transparent, 1);
        public static readonly LinearGradientBrush AccentFill = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        static readonly GradientStop g0 = new GradientStop(Colors.White, 0), g1 = new GradientStop(Colors.White, 1);
        public static event Action Changed;

        static Theme() { AccentFill.GradientStops.Add(g0); AccentFill.GradientStops.Add(g1); Glow.GradientStops.Add(w0); Glow.GradientStops.Add(w1); }

        public static Color Hex(string s)
        {
            try { return (Color)ColorConverter.ConvertFromString(s); } catch { return Colors.Gray; }
        }
        public static string ToHex(Color c) { return string.Format("#{0:x2}{1:x2}{2:x2}", c.R, c.G, c.B); }
        public static Color A(Color c, double a) { return Color.FromArgb((byte)Math.Round(255 * a), c.R, c.G, c.B); }

        public static Color Hsl(double h, double s, double l)
        {
            h = ((h % 1) + 1) % 1; s = Math.Max(0, Math.Min(1, s)); l = Math.Max(0, Math.Min(1, l));
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            Func<double, double> f = t =>
            {
                if (t < 0) t += 1; if (t > 1) t -= 1;
                if (t < 1 / 6.0) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
                return p;
            };
            return Color.FromRgb((byte)Math.Round(f(h + 1 / 3.0) * 255), (byte)Math.Round(f(h) * 255), (byte)Math.Round(f(h - 1 / 3.0) * 255));
        }

        public static void ToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
            l = (mx + mn) / 2; h = 0; s = 0;
            if (mx == mn) return;
            double d = mx - mn;
            s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
            if (mx == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (mx == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }

        public static Palette MakeCustom(string accent, string accent2, double hue, double strength, double depth)
        {
            double b = 0.02 + 0.06 * depth;
            var p = new Palette("custom", "Custom", "#000", "#000", "#000", "#000", "#fff", accent, "#86efac", "#fdba74", "#fca5a5", "#fde68a");
            p.Island = Hsl(hue, strength, b); p.Surface = Hsl(hue, strength * 0.9, b + 0.05);
            p.Raised = Hsl(hue, strength * 0.85, b + 0.11); p.Hover = Hsl(hue, strength * 0.8, b + 0.16);
            p.Text = Hsl(hue, Math.Min(0.3, strength), 0.93);
            p.Accent = Hex(accent); p.Accent2 = Hex(accent2);
            return p;
        }

        public static Palette MakeWallpaper(Color seed)
        {
            double h, s, l; ToHsl(seed, out h, out s, out l);
            double sat = Math.Max(0.35, Math.Min(0.85, s));
            var p = new Palette("wallpaper", "Wallpaper", "#000", "#000", "#000", "#000", "#fff", "#fff", "#86efac", "#fdba74", "#fca5a5", "#fde68a");
            p.Island = Hsl(h, 0.28, 0.04); p.Surface = Hsl(h, 0.22, 0.085); p.Raised = Hsl(h, 0.2, 0.14); p.Hover = Hsl(h, 0.18, 0.19);
            p.Text = Hsl(h, 0.25, 0.93); p.Accent = Hsl(h, sat, 0.7); p.Accent2 = p.Accent;
            return p;
        }

        static void Set(SolidColorBrush b, Color c, bool anim)
        {
            if (!anim) { b.BeginAnimation(SolidColorBrush.ColorProperty, null); b.Color = c; return; }
            b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        public static void Apply(Palette p, bool anim)
        {
            Current = p; CurrentKey = p.Key;
            UseGradient = p.Key == "custom" && p.Accent != p.Accent2;
            Set(Island, p.Island, anim); Set(Surface, p.Surface, anim); Set(Raised, p.Raised, anim); Set(Hover, p.Hover, anim);
            Set(Text, p.Text, anim); Set(Text2, A(p.Text, 0.62), anim); Set(Text3, A(p.Text, 0.38), anim);
            Set(Hairline, A(p.Text, 0.09), anim); Set(Highlight, A(p.Text, 0.06), anim);
            Set(Accent, p.Accent, anim); Set(Accent2, p.Accent2, anim);
            Set(Green, p.Green, anim); Set(Orange, p.Orange, anim); Set(Red, p.Red, anim); Set(Yellow, p.Yellow, anim);
            Set(AccentSoft, A(p.Accent, 0.18), anim); Set(RedSoft, A(p.Red, 0.18), anim); Set(OrangeSoft, A(p.Orange, 0.18), anim); Set(GreenSoft, A(p.Green, 0.18), anim);
            Set(Shadow, A(Colors.Black, 0.5), false);
            Set(GlassBase, A(p.Surface, 0.42), anim); Set(GlassSide, A(p.Island, 0.3), anim); Set(GlassCard, A(p.Raised, 0.42), anim);
            Set(GlassEdge, A(p.Text, 0.08), anim); Set(GlassField, A(p.Island, 0.45), anim); Set(AccentEdge, A(p.Accent, 0.45), anim);
            w0.Color = A(p.Accent, 0.22); w1.Color = A(p.Accent, 0);
            double lum = 0.2126 * p.Accent.R / 255 + 0.7152 * p.Accent.G / 255 + 0.0722 * p.Accent.B / 255;
            Set(OnAccent, lum > 0.6 ? p.Island : Colors.White, anim);
            g0.BeginAnimation(GradientStop.ColorProperty, null); g1.BeginAnimation(GradientStop.ColorProperty, null);
            g0.Color = p.Accent; g1.Color = UseGradient ? p.Accent2 : p.Accent;
            if (Changed != null) Changed();
        }

        public static Brush Named(string name)
        {
            switch (name)
            {
                case "green": return Green;
                case "orange": return Orange;
                case "red": return Red;
                case "yellow": return Yellow;
                case "accent": return Accent;
            }
            if (name != null && name.StartsWith("#")) return K.Solid(Hex(name));
            return Text;
        }
    }

    // ===================================================================== helpers
    static class K
    {
        // UI text inherits the window's FontFamily (K.Font), so changing the font in Settings is live.
        public static FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI");
        // Typeface choices: 0 Inter (bundled), 1 Segoe UI, 2 custom installed font, 3 JetBrainsMono Nerd Font (bundled).
        public static FontFamily ResolveFont(int choice, string custom)
        {
            FontFamily f = null;
            if (choice == 0) f = Bundled("Inter-Regular.ttf", "Inter");
            else if (choice == 3) f = Bundled("JetBrainsMonoNerdFont-Regular.ttf", "JetBrainsMono Nerd Font");
            if (f != null) return f;
            if (choice == 2 && !string.IsNullOrWhiteSpace(custom)) return new FontFamily(custom.Trim() + ", Segoe UI Variable Text, Segoe UI");
            return new FontFamily("Segoe UI Variable Text, Segoe UI");
        }
        static FontFamily Bundled(string file, string family)
        {
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "fonts");
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, file)))
                    return new FontFamily(new Uri(dir.Replace('\\', '/') + "/", UriKind.Absolute), "./#" + family + ", Segoe UI Variable Text, Segoe UI");
            }
            catch { }
            return null;
        }
        // Text setup. ClearType windows (Settings, Wizard) keep pixel-snapped hinting. The island is a
        // scaled grayscale surface: WPF's hinted grayscale glyphs get no vertical anti-aliasing, so
        // curves and icons stair-step ("pixelated") at Scale > 1 and mid-morph. Animated hinting draws
        // unhinted, fully smoothed glyphs at the real on-screen size, which reads like macOS text.
        public static void CrispText(FrameworkElement w, bool clearType)
        {
            w.UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Ideal);
            TextOptions.SetTextHintingMode(w, clearType ? TextHintingMode.Auto : TextHintingMode.Animated);
            TextOptions.SetTextRenderingMode(w, clearType ? TextRenderingMode.ClearType : TextRenderingMode.Grayscale);
        }
        public static readonly FontFamily ICO = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly FontFamily Mono = Bundled("JetBrainsMonoNerdFont-Regular.ttf", "JetBrainsMono Nerd Font") ?? new FontFamily("Cascadia Mono, Cascadia Code, Consolas");
        public static readonly CultureInfo ID = new CultureInfo("id-ID");
        public static readonly CultureInfo EN = CultureInfo.GetCultureInfo("en-US");
        public static readonly IEasingFunction OutQuint = new QuinticEase { EasingMode = EasingMode.EaseOut };
        public static readonly IEasingFunction OutCubic = new CubicEase { EasingMode = EasingMode.EaseOut };
        public static readonly IEasingFunction OutBack = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        public static readonly IEasingFunction Elastic = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 5 };

        public static readonly DependencyProperty HoverTo = DependencyProperty.RegisterAttached("HoverTo", typeof(Color), typeof(K), new PropertyMetadata(Colors.Transparent));
        public static readonly DependencyProperty HoverFrom = DependencyProperty.RegisterAttached("HoverFrom", typeof(Color), typeof(K), new PropertyMetadata(Colors.Transparent));

        // Global duration multiplier (Settings → animation mode). 0 = instant.
        public static double AnimScale = 1;
        public static int Dur(int ms) { return (int)Math.Round(ms * AnimScale); }

        public static Brush Solid(Color c) { var x = new SolidColorBrush(c); x.Freeze(); return x; }
        public static Brush B(byte a, byte r, byte g, byte b) { return Solid(Color.FromArgb(a, r, g, b)); }

        public static TextBlock TB(string text, double size, Brush fg, FontWeight w)
        {
            var t = new TextBlock();
            t.Text = text; t.FontSize = size; t.Foreground = fg; t.FontWeight = w;
            t.TextTrimming = TextTrimming.CharacterEllipsis; t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }
        public static TextBlock Glyph(string g, double size, Brush fg)
        {
            var t = new TextBlock();
            t.Text = g; t.FontSize = size; t.Foreground = fg; t.FontFamily = ICO;
            t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }
        public static void Tabular(TextBlock t) { System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); }

        public static DoubleAnimation Anim(double to, int ms, IEasingFunction ease)
        {
            var a = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(Math.Max(1, Dur(ms)))));
            a.EasingFunction = ease;
            return a;
        }
        public static DoubleAnimation AnimRaw(double to, int ms, IEasingFunction ease)
        {
            var a = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(Math.Max(1, ms))));
            a.EasingFunction = ease;
            return a;
        }
        public static void Pop(ScaleTransform st, double to, int ms, IEasingFunction ease)
        {
            var a = Anim(to, ms, ease);
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        public static void PopIn(UIElement el, double from, int ms)
        {
            var st = new ScaleTransform(from, from); el.RenderTransform = st; el.RenderTransformOrigin = new Point(0.5, 0.5);
            Pop(st, 1, ms, OutBack);
        }
        public static void FadeIn(UIElement el, double fromY, int ms)
        {
            var tt = new TranslateTransform(0, fromY);
            el.RenderTransform = tt; el.Opacity = 0;
            el.BeginAnimation(UIElement.OpacityProperty, Anim(1, ms, OutQuint));
            tt.BeginAnimation(TranslateTransform.YProperty, Anim(0, ms, OutQuint));
        }

        public static Border IconBtn(string glyph, double size, double fs, Brush fg, Action click, out TextBlock tb)
        {
            var t = Glyph(glyph, fs, fg);
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Brushes.Transparent, Child = t, RenderTransformOrigin = new Point(0.5, 0.5), Cursor = Cursors.Hand };
            Pressable(b, click, true);
            tb = t;
            return b;
        }
        public static Border IconBtn(string glyph, double size, double fs, Brush fg, Action click)
        {
            TextBlock d; return IconBtn(glyph, size, fs, fg, click, out d);
        }

        public static Border PillBtn(string text, Brush bg, Brush fg, double h, Action click)
        {
            var t = TB(text, 12.5, fg, FontWeights.SemiBold); t.HorizontalAlignment = HorizontalAlignment.Center;
            var b = new Border { Height = h, CornerRadius = new CornerRadius(h / 2), Background = bg, Child = t, Padding = new Thickness(14, 0, 14, 0), RenderTransformOrigin = new Point(0.5, 0.5), Cursor = Cursors.Hand };
            Pressable(b, click, false);
            return b;
        }

        // Glyph + label pill.
        public static Border GlyphPill(string glyph, string text, Brush bg, Brush fg, double h, Action click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var g = Glyph(glyph, 12, fg); g.Margin = new Thickness(0, 0, 6, 0);
            sp.Children.Add(g); sp.Children.Add(TB(text, 12, fg, FontWeights.SemiBold));
            var b = new Border { Height = h, CornerRadius = new CornerRadius(h / 2), Background = bg, Child = sp, Padding = new Thickness(12, 0, 12, 0), RenderTransformOrigin = new Point(0.5, 0.5), Cursor = Cursors.Hand };
            Pressable(b, click, false);
            return b;
        }

        public static void Pressable(Border b, Action click, bool hoverBg)
        {
            var st = new ScaleTransform(1, 1); b.RenderTransform = st;
            Brush orig = null; SolidColorBrush fx = null;
            b.MouseEnter += delegate
            {
                if (!hoverBg) { b.BeginAnimation(UIElement.OpacityProperty, AnimRaw(0.84, 140, OutCubic)); return; }
                if (fx == null || b.Background != fx)
                {
                    orig = b.Background;
                    var sc = orig as SolidColorBrush;
                    bool clear = sc == null || sc.Color.A == 0;
                    Color from = clear ? Theme.A(Theme.Current.Text, 0) : sc.Color;
                    fx = new SolidColorBrush(from);
                    b.Background = fx;
                    // translucent buttons (e.g. over album-art ambience) stay translucent and just get a
                    // touch brighter; jumping to the opaque hover colour looked like a flash
                    Color to = clear ? Theme.A(Theme.Current.Text, 0.08)
                             : sc.Color.A < 250 ? Theme.A(Theme.Current.Text, Math.Min(1, sc.Color.A / 255.0 + 0.08))
                             : Theme.Current.Hover;
                    fx.SetValue(HoverTo, to);
                    fx.SetValue(HoverFrom, from);
                }
                fx.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation((Color)fx.GetValue(HoverTo), TimeSpan.FromMilliseconds(160)) { EasingFunction = OutCubic });
            };
            b.MouseLeave += delegate
            {
                if (hoverBg && fx != null)
                {
                    var anim = new ColorAnimation((Color)fx.GetValue(HoverFrom), TimeSpan.FromMilliseconds(220)) { EasingFunction = OutCubic };
                    var keep = fx; var o = orig;
                    anim.Completed += delegate { if (b.Background == keep && !b.IsMouseOver) { b.Background = o; fx = null; } };
                    fx.BeginAnimation(SolidColorBrush.ColorProperty, anim);
                }
                else if (!hoverBg) b.BeginAnimation(UIElement.OpacityProperty, AnimRaw(1, 200, OutCubic));
                Pop(st, 1, 200, OutQuint);
            };
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; Pop(st, 0.86, 90, OutQuint); };
            b.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                Pop(st, 1, 520, Elastic);
                if (click != null) click();
            };
        }

        public static string Fmt(double s)
        {
            if (s < 0) s = 0;
            var t = TimeSpan.FromSeconds(Math.Floor(s));
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        public static string Bytes(double b)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return (b >= 100 || i == 0 ? b.ToString("0") : b.ToString("0.0", EN)) + " " + u[i];
        }

        // Text input with placeholder, styled for dark surfaces.
        public static Grid Field(TextBox tb, string placeholder, double width, Brush bg)
        {
            tb.Background = Brushes.Transparent; tb.BorderThickness = new Thickness(0); tb.Foreground = Theme.Text;
            tb.CaretBrush = Theme.Text; tb.SelectionBrush = Theme.Accent; tb.FontSize = 13;
            tb.VerticalContentAlignment = VerticalAlignment.Center; tb.FocusVisualStyle = null; tb.Padding = new Thickness(2, 0, 2, 0);
            var box = new Border { CornerRadius = new CornerRadius(10), Background = bg ?? Theme.Raised, Padding = new Thickness(8, 7, 8, 7), Child = tb };
            var g = new Grid();
            if (width > 0) g.Width = width;
            g.Children.Add(box);
            var ph = TB(placeholder ?? "", 13, Theme.Text3, FontWeights.Normal); ph.Margin = new Thickness(12, 0, 0, 0); ph.IsHitTestVisible = false;
            ph.Visibility = string.IsNullOrEmpty(tb.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (tb.AcceptsReturn) { ph.VerticalAlignment = VerticalAlignment.Top; ph.Margin = new Thickness(12, 8, 0, 0); }
            g.Children.Add(ph);
            tb.TextChanged += delegate { ph.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            return g;
        }

        public static Border Card(UIElement child, double radius, Brush bg)
        {
            return new Border { CornerRadius = new CornerRadius(radius), Background = bg ?? Theme.Raised, Child = child };
        }
    }

    // ===================================================================== controls
    // Animated equalizer / waveform bars. mode: 0 full, 1 reduced (~8 fps), 2 off.
    class Eq : StackPanel
    {
        readonly ScaleTransform[] st; readonly Rectangle[] bars; bool running; int mode;
        static readonly double[] Durs = { 0.42, 0.58, 0.36, 0.50, 0.66, 0.46 };
        public Eq(int n, double w, double h, double gap)
        {
            Orientation = Orientation.Horizontal; VerticalAlignment = VerticalAlignment.Center;
            st = new ScaleTransform[n]; bars = new Rectangle[n];
            for (int i = 0; i < n; i++)
            {
                var r = new Rectangle { Width = w, Height = h, RadiusX = w / 2, RadiusY = w / 2, Fill = Theme.Text, Margin = new Thickness(i == 0 ? 0 : gap, 0, 0, 0) };
                var s = new ScaleTransform(1, 0.3);
                r.RenderTransform = s; r.RenderTransformOrigin = new Point(0.5, 0.5);
                st[i] = s; bars[i] = r; Children.Add(r);
            }
        }
        public void SetColor(Brush b) { foreach (var r in bars) r.Fill = b; }

        // "Reduced": every equaliser on screen is driven by ONE 8 fps timer, so the island repaints at
        // most 8 times a second (separate animation clocks per bar added up to ~40 repaints/s).
        static readonly List<Eq> reduced = new List<Eq>();
        static System.Windows.Threading.DispatcherTimer reducedTimer;
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        static void ReducedTick(object s, EventArgs e)
        {
            double t = clock.Elapsed.TotalSeconds;
            foreach (var q in reduced)
                for (int i = 0; i < q.st.Length; i++)
                    q.st[i].ScaleY = 0.22 + 0.78 * (0.5 + 0.5 * Math.Sin(t * Math.PI / Durs[i % Durs.Length] + i * 1.3));
        }

        public void Run(bool on, int waveMode)
        {
            if (on == running && waveMode == mode) return;
            running = on; mode = waveMode;
            reduced.Remove(this);
            if (on && mode == 1)
            {
                foreach (var s in st) s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                reduced.Add(this);
                if (reducedTimer == null) { reducedTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) }; reducedTimer.Tick += ReducedTick; }
                reducedTimer.Start();
                return;
            }
            if (reduced.Count == 0 && reducedTimer != null) reducedTimer.Stop();
            for (int i = 0; i < st.Length; i++)
            {
                if (on && mode == 0)
                {
                    var a = new DoubleAnimation(0.22, 1.0, TimeSpan.FromSeconds(Durs[i % Durs.Length] * (mode == 1 ? 1.4 : 1)));
                    a.AutoReverse = true; a.RepeatBehavior = RepeatBehavior.Forever;
                    a.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
                    a.BeginTime = TimeSpan.FromSeconds(i * 0.07);
                    Timeline.SetDesiredFrameRate(a, mode == 1 ? 8 : 40);
                    st[i].BeginAnimation(ScaleTransform.ScaleYProperty, a);
                }
                else
                {
                    var a = K.AnimRaw(on ? 0.35 + 0.5 * ((i * 37) % 10) / 10.0 : 0.3, 260, K.OutCubic);
                    st[i].BeginAnimation(ScaleTransform.ScaleYProperty, a);
                }
            }
        }
    }

    // Circular progress ring.
    class Ring : Grid
    {
        readonly Path arc; readonly double size, thick; double progress = -1;
        public Ring(double size, double thick, Brush color)
        {
            this.size = size; this.thick = thick;
            Width = size; Height = size;
            Children.Add(new Ellipse { Stroke = Theme.Hairline, StrokeThickness = thick, Margin = new Thickness(0) });
            arc = new Path { Stroke = color, StrokeThickness = thick, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            Children.Add(arc);
            Progress = 0;
        }
        public Brush Color { set { arc.Stroke = value; } }

        // Eased change of Progress (gauges glide between samples instead of jumping).
        static readonly DependencyProperty AnimProgressProperty = DependencyProperty.Register("AnimProgress", typeof(double), typeof(Ring),
            new PropertyMetadata(0.0, (d, e) => ((Ring)d).Progress = (double)e.NewValue));
        public void AnimateTo(double target, int ms)
        {
            target = Math.Max(0, Math.Min(0.9999, target));
            var a = new System.Windows.Media.Animation.DoubleAnimation(Math.Max(0, progress), target, TimeSpan.FromMilliseconds(Math.Max(1, K.Dur(ms)))) { EasingFunction = K.OutCubic };
            BeginAnimation(AnimProgressProperty, a);
        }
        public double Progress
        {
            get { return progress; }
            set
            {
                value = Math.Max(0, Math.Min(0.9999, value));
                if (Math.Abs(value - progress) < 0.0005) return;
                progress = value;
                double r = (size - thick) / 2, c = size / 2;
                double a = value * 2 * Math.PI;
                var start = new Point(c, c - r);
                var end = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(start, false, false);
                    ctx.ArcTo(end, new Size(r, r), 0, value > 0.5, SweepDirection.Clockwise, true, false);
                }
                g.Freeze();
                arc.Data = value <= 0.0005 ? null : g;
            }
        }
    }

    // Rounded battery: soft outline with a pill-shaped level fill and a round nub; green while
    // charging (with a bolt), red at 20% or less. The level glides when it changes.
    class BattView : StackPanel
    {
        readonly TextBlock pct, bolt; readonly Border body, level; readonly double inner;
        public BattView(double fs)
        {
            Orientation = Orientation.Horizontal; VerticalAlignment = VerticalAlignment.Center;
            pct = K.TB("", fs, Theme.Text, FontWeights.SemiBold); pct.Margin = new Thickness(0, 0, 6, 0); K.Tabular(pct);
            bolt = K.Glyph("\uE945", fs - 2, Theme.Green); bolt.Margin = new Thickness(0, 0, 4, 0);
            double h = Math.Round(fs * 0.95), w = Math.Round(h * 2.05);
            inner = w - 2 * 1.4 - 2 * 1.6;
            body = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(h * 0.36), BorderThickness = new Thickness(1.4), BorderBrush = K.Solid(Theme.A(Theme.Current.Text, 0.45)), Padding = new Thickness(1.6), VerticalAlignment = VerticalAlignment.Center };
            level = new Border { CornerRadius = new CornerRadius(h * 0.22), Background = Theme.Text, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            body.Child = level;
            var nub = new Border { Width = 2.2, Height = h * 0.4, CornerRadius = new CornerRadius(0, 1.4, 1.4, 0), Background = K.Solid(Theme.A(Theme.Current.Text, 0.45)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 0, 0) };
            Children.Add(pct); Children.Add(bolt); Children.Add(body); Children.Add(nub);
        }
        public void Update(bool none, int p, bool ac)
        {
            Visibility = none ? Visibility.Collapsed : Visibility.Visible;
            if (none) return;
            p = Math.Max(0, Math.Min(100, p));
            pct.Text = p + "%";
            bolt.Visibility = ac ? Visibility.Visible : Visibility.Collapsed;
            Brush c = ac ? Theme.Green : (p <= 20 ? (Brush)Theme.Red : Theme.Text);
            level.Background = c;
            pct.Foreground = p <= 20 && !ac ? Theme.Red : Theme.Text;
            double target = Math.Max(level.CornerRadius.TopLeft * 2, inner * p / 100.0);
            level.BeginAnimation(WidthProperty, K.Anim(target, 600, K.OutCubic));
        }
    }

    // iOS-style toggle.
    class Toggle : Border
    {
        readonly Border knob; readonly TranslateTransform tt; readonly Border fill; bool on;
        public event Action<bool> Changed;
        public bool IsOn { get { return on; } }
        public Toggle(bool v)
        {
            Width = 46; Height = 28; CornerRadius = new CornerRadius(14); Cursor = Cursors.Hand; Background = Theme.Hover;
            var g = new Grid();
            fill = new Border { CornerRadius = new CornerRadius(14), Background = Theme.AccentFill };
            knob = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0) };
            tt = new TranslateTransform(); knob.RenderTransform = tt;
            g.Children.Add(fill); g.Children.Add(knob);
            Child = g;
            Set(v, false);
            MouseLeftButtonDown += (s, e) => e.Handled = true;
            MouseLeftButtonUp += (s, e) => { e.Handled = true; Set(!on, true); if (Changed != null) Changed(on); };
        }
        public void Set(bool v, bool anim)
        {
            on = v;
            double x = v ? 18 : 0;
            if (!anim) { fill.Opacity = v ? 1 : 0; tt.X = x; return; }
            fill.BeginAnimation(OpacityProperty, K.Anim(v ? 1 : 0, 200, K.OutCubic));
            tt.BeginAnimation(TranslateTransform.XProperty, K.Anim(x, 420, new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 6 }));
        }
    }

    // Segmented control with a sliding indicator.
    class Segmented : Border
    {
        readonly TranslateTransform tt; readonly List<TextBlock> labels = new List<TextBlock>(); readonly double segW; int sel;
        public event Action<int> Changed;
        public Segmented(string[] items, int selected, double segWidth)
        {
            segW = segWidth; sel = Math.Max(0, Math.Min(items.Length - 1, selected));
            CornerRadius = new CornerRadius(10); Background = Theme.Surface; Padding = new Thickness(2); Height = 32;
            var g = new Grid { Width = segW * items.Length };
            var ind = new Border { Width = segW, CornerRadius = new CornerRadius(8), Background = Theme.Hover, HorizontalAlignment = HorizontalAlignment.Left };
            tt = new TranslateTransform(sel * segW, 0); ind.RenderTransform = tt;
            g.Children.Add(ind);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < items.Length; i++)
            {
                int idx = i;
                var t = K.TB(items[i], 12, i == sel ? Theme.Text : Theme.Text2, FontWeights.SemiBold);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                var cell = new Border { Width = segW, Background = Brushes.Transparent, Child = t, Cursor = Cursors.Hand };
                cell.MouseLeftButtonDown += (s, e) => e.Handled = true;
                cell.MouseLeftButtonUp += (s, e) => { e.Handled = true; Select(idx, true); };
                labels.Add(t); sp.Children.Add(cell);
            }
            g.Children.Add(sp);
            Child = g;
        }
        public void Select(int i, bool fire)
        {
            if (i == sel) return;
            sel = i;
            tt.BeginAnimation(TranslateTransform.XProperty, K.Anim(i * segW, 380, K.OutBack));
            for (int j = 0; j < labels.Count; j++) labels[j].Foreground = j == i ? Theme.Text : Theme.Text2;
            if (fire && Changed != null) Changed(i);
        }
    }

    // Centers each child inside its own size (top-aligned when Tag == "top") during layout, so a child's
    // position always matches the island's size in the same frame. Children may be larger than the panel.
    class CenterPanel : Panel
    {
        protected override Size MeasureOverride(Size available)
        {
            foreach (UIElement c in InternalChildren) c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return new Size(0, 0);
        }
        protected override Size ArrangeOverride(Size final)
        {
            foreach (UIElement c in InternalChildren)
            {
                var ds = c.DesiredSize;
                var fe = c as FrameworkElement;
                bool top = fe != null && (fe.Tag as string) == "top";
                double x = Math.Round((final.Width - ds.Width) / 2), y = top ? 0 : Math.Round((final.Height - ds.Height) / 2);
                c.Arrange(new Rect(x, y, ds.Width, ds.Height));
            }
            return final;
        }
    }

    // Text whose changed digits roll vertically (clock / timer), like a split-flap.
    class RollingText : StackPanel
    {
        string text = ""; readonly double size; readonly Brush fg; readonly FontWeight weight;
        public RollingText(double size, Brush fg, FontWeight weight)
        {
            this.size = size; this.fg = fg; this.weight = weight;
            Orientation = Orientation.Horizontal; VerticalAlignment = VerticalAlignment.Center;
        }
        public string Text { get { return text; } set { Set(value ?? ""); } }

        TextBlock Glyph(char c)
        {
            var t = K.TB(c.ToString(), size, fg, weight); t.TextTrimming = TextTrimming.None; K.Tabular(t);
            t.RenderTransform = new TranslateTransform();
            return t;
        }

        void Set(string t)
        {
            if (t == text) return;
            bool roll = K.AnimScale > 0 && t.Length == text.Length && Children.Count == t.Length;
            if (roll)
                for (int i = 0; i < t.Length; i++)
                    if (t[i] != text[i] && !(char.IsDigit(t[i]) && char.IsDigit(text[i]))) { roll = false; break; }
            string old = text; text = t;
            if (!roll)
            {
                Children.Clear();
                foreach (char c in t) { var g = new Grid { ClipToBounds = true }; g.Children.Add(Glyph(c)); Children.Add(g); }
                return;
            }
            double h = size * 1.1;
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] == old[i]) continue;
                var cell = (Grid)Children[i];
                var prev = (TextBlock)cell.Children[cell.Children.Count - 1];
                var nw = Glyph(t[i]);
                cell.Children.Add(nw);
                int delay = (t.Length - 1 - i) * 40;
                var up = K.Anim(-h, 360, K.OutCubic); up.BeginTime = TimeSpan.FromMilliseconds(K.Dur(delay));
                var fade = K.Anim(0, 260, K.OutCubic); fade.BeginTime = up.BeginTime;
                var p = prev;
                up.Completed += delegate { cell.Children.Remove(p); };
                ((TranslateTransform)prev.RenderTransform).BeginAnimation(TranslateTransform.YProperty, up);
                prev.BeginAnimation(UIElement.OpacityProperty, fade);
                var tt = (TranslateTransform)nw.RenderTransform; tt.Y = h; nw.Opacity = 0;
                var inA = K.Anim(0, 420, K.OutBack); inA.BeginTime = up.BeginTime;
                var inF = K.Anim(1, 260, K.OutCubic); inF.BeginTime = up.BeginTime;
                tt.BeginAnimation(TranslateTransform.YProperty, inA);
                nw.BeginAnimation(UIElement.OpacityProperty, inF);
            }
        }
    }

    // Horizontal slider: track + accent fill + knob, drag or click.
    class SliderBar : Grid
    {
        readonly Border fill, knob; readonly double min, max, step; double val;
        public event Action<double> Changed;      // live while dragging
        public event Action<double> Committed;    // on release
        public bool ShowKnob = true; bool hoverKnob;
        public double Value { get { return val; } }
        public SliderBar(double min, double max, double step, double value, double width, double height)
        {
            this.min = min; this.max = max; this.step = step;
            if (width > 0) Width = width;
            Height = Math.Max(height, 20); Background = Brushes.Transparent; Cursor = Cursors.Hand;
            var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = Theme.Hover, VerticalAlignment = VerticalAlignment.Center };
            fill = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = Theme.AccentFill, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            knob = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            Children.Add(track); Children.Add(fill); Children.Add(knob);
            SizeChanged += delegate { Layout(); };
            MouseLeftButtonDown += (s, e) => { e.Handled = true; CaptureMouse(); FromPos(e.GetPosition(this).X, false); };
            MouseMove += (s, e) => { if (IsMouseCaptured) FromPos(e.GetPosition(this).X, false); };
            MouseLeftButtonUp += (s, e) => { e.Handled = true; if (IsMouseCaptured) { ReleaseMouseCapture(); FromPos(e.GetPosition(this).X, true); } };
            MouseWheel += (s, e) => { e.Handled = true; SetValue(val + (e.Delta > 0 ? 1 : -1) * Math.Max(step, (max - min) / 20), true); if (Committed != null) Committed(val); };
            SetValue(value, false);
        }
        public void SetFill(Brush b) { fill.Background = b; }

        // Track + fill thicken (and the knob appears) while hovered or dragged.
        public void GrowOnHover(double grown)
        {
            var track = (Border)Children[0];
            double h0 = track.Height;
            Action<bool> grow = on =>
            {
                double h = on ? grown : h0;
                var a = K.Anim(h, on ? 180 : 260, K.OutCubic);
                track.BeginAnimation(HeightProperty, a); fill.BeginAnimation(HeightProperty, a);
                var cr = new CornerRadius(h / 2); track.CornerRadius = cr; fill.CornerRadius = cr;
                hoverKnob = on;
                knob.Width = knob.Height = 12; knob.CornerRadius = new CornerRadius(6);
                Layout();
                if (on) K.PopIn(knob, 0.4, 260);
            };
            MouseEnter += delegate { grow(true); };
            MouseLeave += delegate { if (!IsMouseCaptured) grow(false); };
            LostMouseCapture += delegate { if (!IsMouseOver) grow(false); };
        }

        void FromPos(double x, bool commit)
        {
            double w = Math.Max(1, ActualWidth);
            SetValue(min + (max - min) * Math.Max(0, Math.Min(1, x / w)), true);
            if (commit && Committed != null) Committed(val);
        }
        public void SetValue(double v, bool fire)
        {
            if (step > 0) v = Math.Round(v / step) * step;
            v = Math.Max(min, Math.Min(max, v));
            bool changed = Math.Abs(v - val) > 1e-9;
            val = v; Layout();
            if (fire && changed && Changed != null) Changed(val);
        }
        void Layout()
        {
            double w = ActualWidth > 0 ? ActualWidth : Width;
            if (double.IsNaN(w) || w <= 0) return;
            double f = (val - min) / Math.Max(1e-9, max - min);
            fill.Width = Math.Max(fill.Height, f * w);
            knob.Visibility = ShowKnob || hoverKnob ? Visibility.Visible : Visibility.Collapsed;
            double kw = knob.Width;
            knob.Margin = new Thickness(Math.Max(0, Math.Min(w - kw, f * w - kw / 2)), 0, 0, 0);
        }
    }
}
