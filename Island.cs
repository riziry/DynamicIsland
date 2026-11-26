// Dynamic Island for Windows — native WPF. Engine, controller, compact views.
// Build: build.bat (uses the csc.exe that ships with Windows).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Media.Control;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace DynIsland
{
    class IslandEvent
    {
        public string Kind;          // osd-volume, osd-brightness, charging, system, track, welcome, drop
        public int Priority;
        public int Duration;         // ms; 0 = stays until dismissed
        public string Key;           // same key → update in place
        public string Glyph, Title, Sub;
        public Brush Color;
        public ImageSource Image;
        public double Value;
        public bool Muted, Queueable;
        public Action Click;
        public string App; public int Count = 1;   // app notifications: source app name, grouped count
    }

    class View
    {
        public string Name; public Grid Root; public double W, H, R = -1; public bool Top;
        public readonly ScaleTransform Fit = new ScaleTransform(1, 1), Pop = new ScaleTransform(1, 1);
    }

    // Fill & outline colours, shared by the island and the Settings previews. Each is a two-stop
    // horizontal gradient (solid options use the same colour twice) so every switch can cross-fade.
    static class IslandLook
    {
        public static readonly string[] Fills = { "Island", "Surface", "Accent", "Gradient" };
        public static readonly string[] Outlines = { "None", "Subtle", "Accent", "Gradient" };

        public static void Fill(Palette p, int tint, out Color a, out Color b)
        {
            switch (tint)
            {
                case 1: a = b = p.Surface; break;
                case 2: a = b = Mix(p.Island, p.Accent, 0.22); break;
                case 3: a = Mix(p.Island, p.Accent, 0.30); b = Mix(p.Island, Second(p), 0.30); break;
                default: a = b = p.Island; break;
            }
        }

        public static void Stroke(Palette p, int outline, out Color a, out Color b)
        {
            switch (outline)
            {
                case 1: a = b = Theme.A(p.Text, 0.13); break;
                case 2: a = b = Theme.A(p.Accent, 0.75); break;
                case 3: a = Theme.A(p.Accent, 0.9); b = Theme.A(Second(p), 0.9); break;
                default: a = Theme.A(p.Accent, 0); b = Theme.A(Second(p), 0); break;   // transparent, but fades from/to the right hue
            }
        }

        public static LinearGradientBrush Brush(Color a, Color b)
        {
            return new LinearGradientBrush(new GradientStopCollection { new GradientStop(a, 0), new GradientStop(b, 1) }, new Point(0, 0.5), new Point(1, 0.5));
        }

        // The gradient's second colour: the palette's Accent 2, or the accent swung ~45° round the hue wheel.
        public static Color Second(Palette p)
        {
            if (p.Accent2 != p.Accent) return p.Accent2;
            double h, s, l; ToHsl(p.Accent, out h, out s, out l);
            return Theme.Hsl(h + 0.125, s, l);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        public static void ToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn;
            l = (mx + mn) / 2; h = 0; s = 0;
            if (d < 1e-6) return;
            s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
            if (mx == r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6;
            else if (mx == g) h = ((b - r) / d + 2) / 6;
            else h = ((r - g) / d + 4) / 6;
        }
    }

    static class Pri { public const int Urgent = 100, Interactive = 90, Osd = 80, Charging = 75, System = 70, Notification = 60, Track = 40, Timer = 30, Media = 20; }

    partial class IslandWindow : Window
    {
        AppSettings S { get { return Store.S; } }

        // ---- engine
        Grid root, shapeGrid; System.Windows.Shapes.Path shapePath; CenterPanel host;
        ScaleTransform shapeScale;
        readonly LinearGradientBrush fillBrush = IslandLook.Brush(Colors.Black, Colors.Black), strokeBrush = IslandLook.Brush(Colors.Transparent, Colors.Transparent);
        System.Windows.Shapes.Path shinePath, outlinePath; LinearGradientBrush shineBrush; int lookTint = -1, lookOutline = -1;
        DropShadowEffect glow; System.Windows.Shapes.Path fxPath;
        readonly Spring sw = new Spring(30, 0.05), sh = new Spring(8, 0.05), sr = new Spring(4, 0.05), ss = new Spring(1, 0.0004), sy = new Spring(6, 0.05), spg = new Spring(0, 0.0005);
        bool animating; TimeSpan lastT;
        readonly List<View> views = new List<View>();
        View curView, pendingIn; DateTime pendingAt;
        double winW, winH, dpi = 1;
        IntPtr hwnd;

        // ---- sizes (from preset)
        double PH = 34, IdleW = 128, ActW = 248, PeekW = 340, PeekH = 44;

        // ---- state
        bool hover, pressed, hidden, mini, miniReveal, expanded, pinned, expandedByHover, dragOut;
        string expandMode = "pages"; // pages, launcher, wallpaper
        IslandEvent cur; readonly List<IslandEvent> queue = new List<IslandEvent>();
        DateTime evEnds; TimeSpan evRemain;
        DispatcherTimer evTimer, clockTimer, fsTimer, clipDebounce, hoverPoll, hoverOpen, leaveTimer, slowTimer, miniPoll, tTick, dragWatch;
        DateTime lastDragOver, ignoreClipUntil, pausedAt = DateTime.MinValue;
        IntPtr prevForeground;
        readonly MouseHook mouseHook = new MouseHook();
        WF.NotifyIcon tray; WF.ContextMenuStrip menu; SettingsWindow settingsWin;
        readonly bool silent;
        bool lastAc, battNone, lowNotified, critNotified; int battPct;
        SoundPlayer alarm;

        // ---- compact views
        View vIdle, vMusic, vTimer, vMusicTimer, vOsd, vCharging, vSystem, vTrack, vWelcome, vDrop, vMini, vEmpty;
        RollingText idleClock; TextBlock idleBadgeText; StackPanel idleBadge;
        Border musicArt, mtArt; TextBlock musicGlyph, mtGlyph; Eq musicEq;
        Ring timerRing, mtRing; TextBlock timerText, mtText;
        TextBlock osdGlyph, osdPct; SliderBar osdBar;
        Border chargeFill; TextBlock chargePct, chargeTitle;
        Border sysIcon; TextBlock sysGlyph, sysTitle, sysSub;
        Border trackArt; TextBlock trackTitle, trackArtist, trackGlyph;
        Border welcomeIcon; TextBlock welcomeGlyph, welcomeTitle, welcomeSub;
        Rectangle dropRect;
        Border miniHandle, miniFill; ScaleTransform miniHandleScale; bool miniPeek, miniBreathing, fsNow, calmMorph; DateTime miniPingUntil;
        int forceFs = -1; DateTime peekHoldUntil;
        const double MiniW = 84, MiniH = 8, MiniPeekW = 118, MiniPeekH = 11;

        // ---- timer
        bool tRunning, tPaused; DateTime tEnd; TimeSpan tTotal, tRemain; int setupMinutes; long lastTimerSec = -1;

        // ---- media
        GlobalSystemMediaTransportControlsSessionManager mgr; GlobalSystemMediaTransportControlsSession ses;
        global::Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> propsH;
        global::Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> playH;
        string lastTitle; int propsVer; ImageSource art; Color artColor = Colors.White; bool playing, hasMedia, mediaInit;
        string mTitleStr = "", mArtistStr = "", mSource = "";
        public bool MediaAvailable { get { return mgr != null; } }

        public IslandWindow(bool silent)
        {
            this.silent = silent;
            // GPU renderer: a normal window made transparent by DWM (no per-frame GPU→CPU copy) with a
            // click region that follows the island. Compatible: the classic layered window.
            gpu = S.Renderer == 0;
            WindowStyle = WindowStyle.None; AllowsTransparency = !gpu; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; ShowActivated = false;
            Width = 520; Height = 160; Title = "Dynamic Island";
            K.CrispText(this, false);   // layered window: ClearType isn't available, grayscale AA
            FontFamily = K.Font; fontKey = S.Font + "|" + S.FontCustom;
            setupMinutes = Math.Max(1, S.TimerLastMinutes);
            Theme.Apply(Store.ResolvePalette(), false);
            ApplySizes();

            BuildShell();
            BuildCompactViews();
            BuildExpanded();
            BuildTray();

            evTimer = Timer(1000, delegate { evTimer.Stop(); if (cur != null && cur.Duration > 0) Dismiss(); });
            clockTimer = Timer(1000, delegate { OnClock(); clockTimer.Interval = TimeSpan.FromMilliseconds(1000 - DateTime.Now.Millisecond + 5); });
            fsTimer = Timer(3000, delegate { CheckFullscreen(); CheckScaleChange(); });
            clipDebounce = Timer(120, delegate { clipDebounce.Stop(); OnClipboard(); });
            hoverPoll = Timer(60, delegate { if (!CursorInIsland(6)) EndHover(); });
            AumidReady = () => Dispatcher.BeginInvoke(new Action(() => { if (ses != null) { mSource = SourceName(ses); PagesSessions(); } }));
            hoverOpen = Timer(Math.Max(60, S.HoverDelay), delegate { hoverOpen.Stop(); if (hover && !expanded && CanHoverOpen()) { expandedByHover = true; Expand(null, false); } });
            leaveTimer = Timer(450, delegate { leaveTimer.Stop(); if (expanded && expandedByHover && !hover && !dragOut) Collapse(); });
            slowTimer = Timer(30000, delegate { CheckBattery(); });
            miniPoll = Timer(70, delegate { MiniPoll(); });
            tTick = Timer(200, delegate { TimerTick(); });
            dragWatch = Timer(120, delegate
            {
                if ((DateTime.Now - lastDragOver).TotalMilliseconds > 250 && !CursorInIsland(6)) { dragWatch.Stop(); ShelfDropGlow(false); if (cur != null && cur.Kind == "drop") Dismiss(); }
            });

            Theme.Changed += ApplyFill;
            Loaded += OnLoaded;
            Closed += delegate { Cleanup(); };
            Deactivated += delegate { if (expanded && pinned && !aiPicking && !CursorInIsland(6) && !dragOut && settingsWin == null) Collapse(); };
            PreviewKeyDown += OnKey;
            mouseHook.Down += p => { if (expanded && !aiPicking && !dragOut && !PointInIsland(p, 4)) Collapse(); };
        }

        static DispatcherTimer Timer(int ms, EventHandler tick)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
            t.Tick += tick;
            return t;
        }

        // ================================================================ shell
        void BuildShell()
        {
            root = new Grid();
            shapeGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand, AllowDrop = true };
            shapeScale = new ScaleTransform(1, 1);
            shapeGrid.RenderTransform = shapeScale; shapeGrid.RenderTransformOrigin = new Point(0.5, 0);
            shapePath = new System.Windows.Shapes.Path { Fill = fillBrush };
            outlinePath = new System.Windows.Shapes.Path { Stroke = strokeBrush, StrokeThickness = 1, IsHitTestVisible = false, StrokeLineJoin = PenLineJoin.Round };
            host = new CenterPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            // media ambience lives at the shape level (clipped to the full island outline, notch
            // shoulders and page-dots strip included) instead of inside the page
            ambLayer = new Grid { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Opacity = 0, Visibility = Visibility.Collapsed, ClipToBounds = false };
            // shadow / glow are drawn by a copy of the outline inside a Canvas: effects on shapePath itself got
            // cut to the island's bounding box (the path is layout-clipped), which showed as hard square edges
            fxPath = new System.Windows.Shapes.Path { Fill = fillBrush, IsHitTestVisible = false };
            var fxHost = new Canvas { IsHitTestVisible = false };
            fxHost.Children.Add(fxPath);
            // a band of light that runs once along the outline when the fill or outline changes
            shineBrush = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 0) }, new Point(0, 0.3), new Point(1, 0.7));
            shinePath = new System.Windows.Shapes.Path { Stroke = shineBrush, StrokeThickness = 1.6, IsHitTestVisible = false, Visibility = Visibility.Collapsed, StrokeLineJoin = PenLineJoin.Round };
            // the outline sits above the content so pages with their own backgrounds can't cover it
            glow = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 0, Opacity = 0, Color = Colors.White, RenderingBias = RenderingBias.Performance };
            shapeGrid.Children.Add(fxHost); shapeGrid.Children.Add(shapePath); shapeGrid.Children.Add(ambLayer); shapeGrid.Children.Add(host); shapeGrid.Children.Add(outlinePath); shapeGrid.Children.Add(shinePath);
            root.Children.Add(shapeGrid);
            Content = root;

            shapeGrid.MouseEnter += delegate { BeginHover(); };
            shapeGrid.MouseMove += delegate { BeginHover(); };
            shapeGrid.MouseLeftButtonDown += (s, e) => { if (!expanded) { pressed = true; UpdateScaleTarget(); } else Pin(); };
            shapeGrid.MouseLeftButtonUp += (s, e) =>
            {
                bool wasPressed = pressed; pressed = false; UpdateScaleTarget();
                if (expanded || !wasPressed) return;
                if (cur != null && cur.Click != null) { var c = cur.Click; c(); return; }
                if (cur != null && cur.Priority >= Pri.Urgent) { Dismiss(); return; }
                if (mini && !miniReveal) { miniReveal = true; ShowBase(); return; }
                Expand(null, true);
            };
            shapeGrid.MouseRightButtonUp += (s, e) => { e.Handled = true; menu.Show(WF.Cursor.Position); };
            shapeGrid.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Middle) { e.Handled = true; Cmd(1); } };
            shapeGrid.MouseWheel += (s, e) =>
            {
                if (expanded) return;
                if (S.ScrollVolume && Svc.Audio.Available) { e.Handled = true; Svc.Audio.Step(e.Delta > 0 ? 1 : -1); }
            };
            shapeGrid.DragEnter += OnDragOver;
            shapeGrid.DragOver += OnDragOver;
            shapeGrid.DragLeave += delegate { lastDragOver = DateTime.MinValue; ShelfDropGlow(false); };
            shapeGrid.Drop += OnDrop;
        }

        View NewView(string name, double w, double h, bool top)
        {
            var v = new View { Name = name, W = w, H = h, Top = top, Root = new Grid { Width = w, Height = h, Opacity = 0, Visibility = Visibility.Collapsed, IsHitTestVisible = false, Tag = top ? "top" : null } };
            var tg = new TransformGroup(); tg.Children.Add(v.Fit); tg.Children.Add(v.Pop);
            v.Root.RenderTransform = tg; v.Root.RenderTransformOrigin = new Point(0.5, top ? 0 : 0.5);
            host.Children.Add(v.Root); views.Add(v);
            return v;
        }

        void SetViewSize(View v, double w, double h)
        {
            v.W = w; v.H = h; v.Root.Width = w; v.Root.Height = h;
            if (v == curView) { Retarget(); EnsureWindow(); }
        }

        void ApplySizes()
        {
            double[][] presets = { new double[] { 30, 112, 224, 320, 40 }, new double[] { 34, 128, 248, 340, 44 }, new double[] { 40, 150, 280, 370, 50 } };
            var p = presets[Math.Max(0, Math.Min(2, S.Size))];
            PH = p[0]; IdleW = p[1]; ActW = p[2]; PeekW = p[3]; PeekH = p[4];
        }

        double CompactR(double h) { return S.Shape == 0 ? h / 2 : S.Shape == 1 ? Math.Min(h / 2, 12) : Math.Min(h / 2, 8); }
        double BigR() { return S.Shape == 0 ? 32 : S.Shape == 1 ? 22 : 12; }

        void ApplyFill()
        {
            var p = Theme.Current;
            Color f1, f2, s1, s2;
            IslandLook.Fill(p, S.Tint, out f1, out f2);
            IslandLook.Stroke(p, S.Outline, out s1, out s2);
            bool changed = lookTint >= 0 && (lookTint != S.Tint || lookOutline != S.Outline);
            lookTint = S.Tint; lookOutline = S.Outline;
            // eased cross-fade; the right stop trails a little so a gradient visibly "flows" in
            FadeStop(fillBrush.GradientStops[0], f1, 0); FadeStop(fillBrush.GradientStops[1], f2, changed ? 90 : 0);
            FadeStop(strokeBrush.GradientStops[0], s1, 0); FadeStop(strokeBrush.GradientStops[1], s2, changed ? 90 : 0);
            outlinePath.StrokeThickness = Math.Max(0.5, Math.Min(3, S.OutlineWidth));
            outlinePath.Visibility = S.Outline == 0 && !changed ? Visibility.Collapsed : Visibility.Visible;
            if (S.Outline == 0 && changed)
            {
                // keep it until the fade to transparent has finished
                var t = Timer(K.Dur(700) + 20, null);
                t.Tick += delegate { t.Stop(); if (S.Outline == 0) outlinePath.Visibility = Visibility.Collapsed; };
                t.Start();
            }
            if (changed) { Shine(S.Outline >= 2 ? s1 : Theme.A(p.Text, 0.9)); Bump(); }
            Apply();
        }

        static void FadeStop(GradientStop st, Color to, int delay)
        {
            var a = new ColorAnimation(to, TimeSpan.FromMilliseconds(Math.Max(1, K.Dur(520)))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            if (delay > 0) a.BeginTime = TimeSpan.FromMilliseconds(K.Dur(delay));
            st.BeginAnimation(GradientStop.ColorProperty, a);
        }

        // One sweep of light along the outline, left to right.
        void Shine(Color c)
        {
            if (K.AnimScale <= 0 || shinePath == null) return;
            shinePath.Visibility = Visibility.Visible;
            Apply();
            var stops = shineBrush.GradientStops;
            stops[0].Color = Theme.A(c, 0); stops[1].Color = c; stops[2].Color = Theme.A(c, 0);
            int ms = K.Dur(760);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            double[] from = { -0.35, -0.2, -0.05 }, to = { 1.05, 1.2, 1.35 };
            for (int i = 0; i < 3; i++)
            {
                var a = new DoubleAnimation(from[i], to[i], TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
                if (i == 1) a.Completed += delegate { shinePath.Visibility = Visibility.Collapsed; };
                stops[i].BeginAnimation(GradientStop.OffsetProperty, a);
            }
        }

        static Color Blend(Color a, Color b, double t)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        // ================================================================ motion
        void TuneSprings()
        {
            // iOS-like: ~460 ms morph; bouncy = soft ~4% overshoot that settles without a second wobble.
            double sc = Math.Max(0.3, K.AnimScale);
            bool bouncy = S.Springy && S.AnimMode != 1 && S.AnimMode != 0;
            // tucking into the mini tab: a quicker, critically damped glide up into the edge (a bounce
            // there read as the tab wobbling over the video)
            double k = (calmMorph ? 330 : 250) / (sc * sc), z = bouncy && !calmMorph ? 0.72 : 1.0;
            sw.Tune(k, z); sh.Tune(k * 1.2, Math.Min(1, z + 0.06)); sr.Tune(k * 1.2, Math.Min(1, z + 0.12));
            ss.Tune(420 / (sc * sc), bouncy ? 0.82 : 1.0);   // hover/press scale: no undershoot "dip"
            // (these two used to sit inside the comment above, so page slides and the vertical offset
            // ignored the animation-speed setting)
            sy.Tune(k, 0.95);
            spg.Tune(260 / (sc * sc), bouncy ? 0.86 : 0.95);   // page slide: brisk, a hint of settle when springy
        }

        void Kick()
        {
            if (K.AnimScale <= 0)
            {
                foreach (var s in new[] { sw, sh, sr, ss, sy, spg }) s.Snap(s.Target);
                Apply();
                if (pendingIn != null) { FadeIn(pendingIn); pendingIn = null; }
                ShrinkWindow();
                return;
            }
            if (animating) return;
            animating = true; lastT = TimeSpan.Zero;
            perfFrames = 0; perfMaxGap = 0; perfApply = 0; perfApplyMax = 0; perfWatch.Restart();
            CompositionTarget.Rendering += OnRender;
        }
        readonly System.Diagnostics.Stopwatch perfWatch = new System.Diagnostics.Stopwatch(); int perfFrames; double perfMaxGap, perfLast, perfApply, perfApplyMax;

        void OnRender(object s, EventArgs e)
        {
            frames++;
            var re = e as RenderingEventArgs;
            TimeSpan t = re != null ? re.RenderingTime : TimeSpan.Zero;
            if (t == lastT && lastT != TimeSpan.Zero) return;
            double nowMs = perfWatch.Elapsed.TotalMilliseconds;
            if (perfFrames > 0) perfMaxGap = Math.Max(perfMaxGap, nowMs - perfLast);
            perfLast = nowMs; perfFrames++;
            double dt = lastT == TimeSpan.Zero ? 1 / 60.0 : (t - lastT).TotalSeconds;
            lastT = t;
            if (dt > 0.05) dt = 0.05;
            int n = (int)Math.Ceiling(dt / 0.004); double h = dt / n;
            bool dragging = pageDrag;
            for (int i = 0; i < n; i++) { sw.Step(h); sh.Step(h); sr.Step(h); ss.Step(h); sy.Step(h); if (!dragging) spg.Step(h); }
            bool done = sw.Settle() & sh.Settle() & sr.Settle() & ss.Settle() & sy.Settle() & (dragging || spg.Settle());
            if (pendingIn != null && DateTime.Now >= pendingAt && SizeReady(pendingIn)) { FadeIn(pendingIn); pendingIn = null; }
            double a0 = perfWatch.Elapsed.TotalMilliseconds;
            Apply();
            double ad = perfWatch.Elapsed.TotalMilliseconds - a0; perfApply += ad; if (ad > perfApplyMax) perfApplyMax = ad;
            if (done && pendingIn == null && !dragging)
            {
                CompositionTarget.Rendering -= OnRender; animating = false; Log.T("settled " + (curView != null ? curView.Name : ""));
                if (outlinePath.Visibility == Visibility.Visible) Apply();   // once more, now pixel-snapped
                if (Environment.GetEnvironmentVariable("DI_DEBUG") == "1" && perfFrames > 5)
                {
                    double ms = perfWatch.Elapsed.TotalMilliseconds;
                    Log.Write(string.Format(K.EN, "anim {0} frames in {1:0} ms = {2:0.0} fps, worst gap {3:0} ms, apply avg {7:0.00} max {8:0.0} ms, view={4}, win={5:0}x{6:0}", perfFrames, ms, perfFrames * 1000.0 / ms, perfMaxGap, curView != null ? curView.Name : "-", winW, winH, perfApply / perfFrames, perfApplyMax));
                }
                ShrinkWindow(); PagesSettled(); UpdateShadow();
                // fit the region exactly a few frames later, once the last frame is surely on screen
                if (gpu)
                {
                    var fit = Timer(90, null);
                    fit.Tick += delegate { fit.Stop(); if (!animating) UpdateRegion(true); };
                    fit.Start();
                }
            }
        }

        bool SizeReady(View v)
        {
            if (sw.Target >= sw.Value - 1 && sh.Target >= sh.Value - 1) return true;   // growing or steady: scale to fit
            return sw.Value <= v.W * 1.08 + 2 && sh.Value <= v.H * 1.15 + 2;
        }

        // Attached to the top edge: the notch style, or any time the island grows out of the mini tab.
        bool Attached { get { return S.Style == 1 || mini; } }
        double Shoulder(double h)
        {
            if (curView == vMini) return Math.Min(5, h);
            return Attached ? Math.Min(10, h * 0.45) : 0;
        }

        void Apply()
        {
            double w = Math.Max(1, sw.Value), h = Math.Max(1, sh.Value);
            double r = Math.Max(0, Math.Min(sr.Value, Math.Min(h / 2, w / 2)));
            double s = Shoulder(h);
            shapeGrid.Width = w + 2 * s; shapeGrid.Height = h;
            shapeGrid.Margin = new Thickness(0, Math.Max(0, sy.Value), 0, 0);
            shapePath.Data = ShapeGeo(w, h, r, s);
            fxPath.Data = shapePath.Data;
            // Outline and shine are inside strokes: the shape inset by half the stroke, so the whole line
            // sits within the island and nothing clips it (a centred stroke lost its outer half; a
            // clip-to-shape stroke lost slivers to layout rounding, which ate thin bottom edges).
            // The edge against the top of the screen isn't stroked.
            if (outlinePath.Visibility == Visibility.Visible) outlinePath.Data = InsetGeo(w, h, r, s, outlinePath.StrokeThickness, OutlineBottom(h, outlinePath.StrokeThickness));
            if (shinePath.Visibility == Visibility.Visible) shinePath.Data = InsetGeo(w, h, r, s, shinePath.StrokeThickness, h - shinePath.StrokeThickness / 2);
            host.Margin = new Thickness(s, 0, 0, 0); host.Width = w; host.Height = h;
            host.Clip = BodyGeo(w, h, r, Attached);
            ambLayer.Clip = shapePath.Data;
            UpdateAmbient();
            shapeScale.ScaleX = shapeScale.ScaleY = ss.Value * UiScale;
            foreach (var v in views)
            {
                if (v.Root.Visibility != Visibility.Visible) continue;
                // open views are revealed top-down by the clip (content stays still); compact ones fit both ways
                double fit = v.Top ? Math.Min(1, w / v.W) : Math.Min(1, Math.Min(w / v.W, h / v.H));
                if (fit < 0.05) fit = 0.05;
                v.Fit.ScaleX = v.Fit.ScaleY = fit;
            }
            ApplyPages();
            UpdateRegion(false);
        }

        Grid ambLayer;
        // Ambient strength = how much of the Media page is on screen x the expanded view's fade.
        void UpdateAmbient()
        {
            if (ambLayer == null || vExp == null) return;
            double f = 0;
            if (curView == vExp && expandMode == "pages")
            {
                int n = pageKeys.Count, i = pageKeys.IndexOf("media");
                if (i >= 0 && n > 0)
                {
                    double pos = i - spg.Value;
                    if (S.LoopPages && n > 1) pos = ModD(pos + n / 2.0, n) - n / 2.0;
                    f = Math.Max(0, 1 - Math.Abs(pos));
                }
                f *= vExp.Root.Opacity;
            }
            if (Math.Abs(ambLayer.Opacity - f) > 0.001) ambLayer.Opacity = f;
            if (mAmbient != null && f > 0.001) { Canvas.SetLeft(mAmbient, Math.Round((shapeGrid.Width - mAmbient.Width) / 2)); Canvas.SetTop(mAmbient, -40); }
            var vis = f > 0.001 ? Visibility.Visible : Visibility.Collapsed;
            if (ambLayer.Visibility != vis) { ambLayer.Visibility = vis; AmbientDrift(vis == Visibility.Visible && CurrentPageKey() == "media"); }
        }

        static Geometry ShapeGeo(double w, double h, double r, double s) { return ShapeGeo(w, h, r, s, false); }

        // Where the bottom outline's centre line goes: h - t/2, nudged up (never down) onto the middle of
        // a device pixel, so a thin line along the flat bottom stays crisp instead of smearing over two
        // half-lit rows (which read as the bottom edge being covered).
        double OutlineBottom(double h, double t)
        {
            double ideal = h - t / 2;
            double sc = shapeScale.ScaleY, top = Math.Max(0, sy.Value);
            if (sc <= 0 || dpi <= 0 || animating) return ideal;
            double dev = (top + ideal * sc) * dpi;
            double snapped = Math.Floor(dev - 0.5) + 0.5;
            double y = (snapped / dpi - top) / sc;
            return y > ideal - 1 ? y : ideal;
        }

        // The outline path for a stroke of thickness t lying fully inside the shape; bottom = where the
        // bottom edge's centre line sits.
        static Geometry InsetGeo(double w, double h, double r, double s, double t, double bottom)
        {
            double i = t / 2;
            bool attached = s > 0.01;
            double gh = attached ? bottom : bottom - i;
            var g = (StreamGeometry)ShapeGeo(Math.Max(1, w - t), Math.Max(1, gh), Math.Max(0, r - i), s, attached).CloneCurrentValue();
            g.Transform = new TranslateTransform(i, attached ? 0 : i);
            g.Freeze();
            return g;
        }

        // openTop: leave the edge along the top of the screen unstroked (attached / notch shapes)
        static Geometry ShapeGeo(double w, double h, double r, double s, bool openTop)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                if (s <= 0.01)
                {
                    c.BeginFigure(new Point(r, 0), true, true);
                    c.LineTo(new Point(w - r, 0), true, false);
                    c.ArcTo(new Point(w, r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(w, h - r), true, false);
                    c.ArcTo(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(r, h), true, false);
                    c.ArcTo(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(0, r), true, false);
                    c.ArcTo(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                }
                else
                {
                    double W = w + 2 * s, rb = Math.Max(0, Math.Min(r, Math.Min(h - s, w / 2)));
                    c.BeginFigure(new Point(0, 0), true, true);
                    c.LineTo(new Point(W, 0), !openTop, false);
                    c.ArcTo(new Point(W - s, s), new Size(s, s), 0, false, SweepDirection.Counterclockwise, true, false);
                    c.LineTo(new Point(W - s, h - rb), true, false);
                    c.ArcTo(new Point(W - s - rb, h), new Size(rb, rb), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(s + rb, h), true, false);
                    c.ArcTo(new Point(s, h - rb), new Size(rb, rb), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(s, s), true, false);
                    c.ArcTo(new Point(0, 0), new Size(s, s), 0, false, SweepDirection.Counterclockwise, true, false);
                }
            }
            g.Freeze();
            return g;
        }

        static Geometry BodyGeo(double w, double h, double r, bool flatTop)
        {
            if (!flatTop) { var rg = new RectangleGeometry(new Rect(0, 0, w, h), r, r); rg.Freeze(); return rg; }
            double rb = Math.Min(r, Math.Min(h, w / 2));
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, 0), true, true);
                c.LineTo(new Point(w, 0), true, false);
                c.LineTo(new Point(w, h - rb), true, false);
                c.ArcTo(new Point(w - rb, h), new Size(rb, rb), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(rb, h), true, false);
                c.ArcTo(new Point(0, h - rb), new Size(rb, rb), 0, false, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        // Three beats: outgoing fades (and shrinks with the pill), pill morphs, incoming fades in.
        void Transition(View v)
        {
            if (v == curView)
            {
                if (v.Root.Visibility != Visibility.Visible || v.Root.Opacity < 0.99) { v.Root.Visibility = Visibility.Visible; FadeIn(v); }
                Retarget(); return;
            }
            var old = curView; curView = v;
            bool calm = v == vMini;
            if (calm != calmMorph) { calmMorph = calm; TuneSprings(); }
            if (old != null) FadeOut(old, v);
            v.Root.Visibility = Visibility.Visible; v.Root.IsHitTestVisible = true;
            v.Root.BeginAnimation(OpacityProperty, null); v.Root.Opacity = 0;
            pendingIn = v; pendingAt = DateTime.Now.AddMilliseconds(K.Dur(90));
            // the shadow comes back once the morph settles (a shadow redrawn every frame is costly)
            if (shadow != null && fxPath.Effect == shadow) fxPath.Effect = null;
            // the music glow goes quickly when leaving the music views: fading slowly while the island
            // grew, its coloured halo hugged the sides/bottom and made the outline there look covered
            if (fxPath.Effect == glow && v != vMusic && v != vMusicTimer)
            {
                var ga = K.Anim(0, 90, K.OutCubic);
                ga.Completed += delegate { if (fxPath.Effect == glow && curView != vMusic && curView != vMusicTimer) fxPath.Effect = null; };
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, ga);
            }
            Retarget();
        }

        // Content cross-fade: opacity + a small scale only (no blur: a per-frame blur pass was the
        // biggest cost while morphing).
        void FadeOut(View old, View next)
        {
            Log.T("fadeout " + old.Name + " -> " + next.Name);
            old.Root.IsHitTestVisible = false;
            int ms = 130;
            var a = K.Anim(0, ms, K.OutCubic);
            a.Completed += delegate { if (curView != old) { old.Root.Visibility = Visibility.Collapsed; old.Root.Effect = null; OnViewHidden(old); } };
            old.Root.BeginAnimation(OpacityProperty, a);
            K.Pop(old.Pop, next.W * next.H < old.W * old.H ? 0.92 : 1.03, ms + 40, K.OutCubic);
            if (!old.Top && old != vMini && old != vEmpty) Motion.EarsOut(old.Root, old.W);
        }

        void FadeIn(View v)
        {
            Log.T("fadein " + v.Name);
            v.Root.Visibility = Visibility.Visible; v.Root.IsHitTestVisible = true; v.Root.Effect = null;
            var fade = K.Anim(1, 280, K.OutCubic);
            // (no BitmapCache here: WPF drew a freshly cached view offset for its first frame)
            v.Root.BeginAnimation(OpacityProperty, fade);
            v.Pop.BeginAnimation(ScaleTransform.ScaleXProperty, null); v.Pop.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            v.Pop.ScaleX = v.Pop.ScaleY = 0.96;
            K.Pop(v.Pop, 1, 420, K.OutQuint);
            // compact content glides out from the centre with the pill (open pages cascade their
            // rows from OnPageShown)
            if (!v.Top && v != vMini && v != vEmpty) Motion.EarsIn(v.Root, v.W);
            OnViewShown(v);
        }

        void Retarget()
        {
            if (curView == null) return;
            double w = curView.W, h = curView.H, r = curView.R >= 0 ? curView.R : (curView.H > PH + 30 ? BigR() : CompactR(h));
            double top = Attached ? (mini ? 0 : S.OffsetY) : 6 + S.OffsetY;
            if (curView == vMini) { top = 0; r = h / 2; }
            sw.Target = w; sh.Target = h; sr.Target = r; sy.Target = top;
            UpdateScaleTarget();
            bool hide = curView == vEmpty;
            // while hidden over fullscreen the fade-out in SetHidden owns the opacity
            if (!hidden) shapeGrid.BeginAnimation(OpacityProperty, K.Anim(hide ? 0 : 1, hide ? 200 : 260, K.OutCubic));
            shapeGrid.IsHitTestVisible = !hide;
            EnsureWindow();
            Kick();
        }

        bool CanLift() { return cur == null || cur.Kind == "system" || cur.Kind == "track" || cur.Kind == "welcome"; }

        void UpdateScaleTarget()
        {
            // hover lifts by scale only (the size stays put), press sinks slightly: one consistent motion
            double t = expanded ? 1 : pressed ? 0.975 : (hover && curView != vMini && CanLift() ? 1.03 : 1);
            if (Math.Abs(t - ss.Target) < 1e-6) return;
            ss.Target = t;
            Kick();
        }

        void Bump() { if (K.AnimScale > 0) { ss.Vel += 0.9; Kick(); } }

        // ---- window sizing: the layered window only ever covers what the pill needs.
        double UiScale { get { return Math.Max(0.6, Math.Min(1.6, S.Scale)); } }

        void EnsureWindow()
        {
            if (gpu) return;
            double sc = UiScale * 1.06;
            double needW = Math.Max(sw.Value, sw.Target) * sc + 2 * 12 + 90;
            double needH = Math.Max(sh.Value, sh.Target) * sc + Math.Max(sy.Value, sy.Target) + 64;
            if (needW > winW + 0.5 || needH > winH + 0.5) SetWin(Math.Max(needW, winW), Math.Max(needH, winH));
        }

        void ShrinkWindow()
        {
            if (gpu) return;
            double sc = UiScale * 1.06;
            double needW = sw.Target * sc + 2 * 12 + 90, needH = sh.Target * sc + sy.Target + 64;
            if (needW < winW - 2 || needH < winH - 2) SetWin(needW, needH);
        }

        // Soft shadow under the open island (fades in once it is open).
        DropShadowEffect shadow;
        void UpdateShadow()
        {
            Log.T("shadow? animating=" + animating + " effect=" + (fxPath.Effect != null));
            if (animating) return;
            bool glowing = fxPath.Effect == glow && glow.Opacity > 0.01;
            if (glowing) return;
            bool want = S.Shadows && (expanded || (cur != null && curView != null && curView.H > PH + 8));
            if (want)
            {
                if (shadow == null) shadow = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Color = Colors.Black, Opacity = 0, RenderingBias = RenderingBias.Performance };
                if (fxPath.Effect != shadow) { fxPath.Effect = shadow; shadow.Opacity = 0; }
                shadow.BeginAnimation(DropShadowEffect.OpacityProperty, K.Anim(0.45, 420, K.OutCubic));
            }
            else if (fxPath.Effect == shadow && shadow != null)
            {
                var a = K.Anim(0, 160, K.OutCubic);
                a.Completed += delegate { if (!(S.Shadows && expanded) && fxPath.Effect == shadow) fxPath.Effect = null; };
                shadow.BeginAnimation(DropShadowEffect.OpacityProperty, a);
            }
        }

        // ---- GPU renderer
        readonly bool gpu;
        int rgnL = -1, rgnT, rgnR, rgnB;
        readonly List<int[]> rgnHist = new List<int[]>();

        // One fixed window big enough for the largest view, so it never resizes mid-animation.
        void SetFixedWindow()
        {
            double sc = UiScale * 1.04;
            double w = Math.Max(Math.Max(S.ExpandedWidth, 620), 660) * sc + 120;
            double h = (520 + 24) * sc + 80 + Math.Max(0, S.OffsetY);
            SetWin(w, h);
            UpdateRegion(true);
        }

        // Only the island's bounds take part in hit-testing; the rest of the window lets clicks through.
        void UpdateRegion(bool force)
        {
            if (!gpu || hwnd == IntPtr.Zero) return;
            double sc = shapeScale.ScaleX;
            double s = Shoulder(Math.Max(1, sh.Value));
            double W = (Math.Max(1, sw.Value) + 2 * s) * sc, H = Math.Max(1, sh.Value) * sc;
            double left = (winW - W) / 2, top = Math.Max(0, sy.Value);
            bool vis = shapeGrid.Visibility == Visibility.Visible && curView != vEmpty;
            int pad = fxPath.Effect != null ? 22 : 1;
            int L = 0, T = 0, R = 0, B = 0;
            if (vis)
            {
                L = (int)Math.Floor(left * dpi) - pad; T = Math.Max(0, (int)Math.Floor(top * dpi) - pad);
                R = (int)Math.Ceiling((left + W) * dpi) + pad; B = (int)Math.Ceiling((top + H) * dpi) + pad + (pad > 1 ? 8 : 0);
            }
            // No window region any more (changing it made DWM flash an offset copy of the window).
            // The whole window is click-through; it only takes the mouse while the cursor is over the
            // island's bounds (see HitPoll).
            rgnL = L; rgnT = T; rgnR = R; rgnB = B;
            if (force) HitPoll();
        }

        bool clickThrough;
        DispatcherTimer hitTimer;
        void SetClickThrough(bool on)
        {
            if (hwnd == IntPtr.Zero) return;
            // Check the real style every time: WPF rewrites the extended style after startup and can
            // drop WS_EX_LAYERED, and without it WS_EX_TRANSPARENT no longer passes clicks through
            // (the whole fixed-size window then swallowed clicks even with the island collapsed).
            int cur = Native.GetWindowLong(hwnd, -20);
            int ex = cur | 0x80000;                  // WS_EX_LAYERED
            ex = on ? (ex | 0x20) : (ex & ~0x20);    // WS_EX_TRANSPARENT
            if (on != clickThrough) Log.T("clickthrough " + on);
            clickThrough = on;
            if (ex == cur) return;
            bool relayer = (cur & 0x80000) == 0;
            Native.SetWindowLong(hwnd, -20, ex);
            if (relayer) { Native.SetLayeredWindowAttributes(hwnd, 0, 255, 2); Log.T("layered style restored"); }
        }

        void HitPoll()
        {
            if (!gpu || hwnd == IntPtr.Zero) return;
            Native.RECT wr;
            if (!Native.GetWindowRect(hwnd, out wr)) return;
            var c = Native.CursorPx();
            double x = c.X - wr.L, y = c.Y - wr.T;
            bool inside = rgnR > rgnL && x >= rgnL - 2 && x <= rgnR + 2 && y >= rgnT - 2 && y <= rgnB + 2;
            if (dragOut || System.Windows.Forms.Control.MouseButtons != System.Windows.Forms.MouseButtons.None && !clickThrough) inside = true; // don't flip mid-drag
            SetClickThrough(!inside);
        }

        void SetWin(double w, double h)
        {
            winW = Math.Ceiling(w); winH = Math.Ceiling(h);
            if (hwnd == IntPtr.Zero) { Width = winW; Height = winH; return; }
            var mon = WF.Screen.PrimaryScreen.Bounds;
            int pw = (int)Math.Ceiling(winW * dpi), ph = (int)Math.Ceiling(winH * dpi);
            int x = mon.Left + (mon.Width - pw) / 2 + (int)Math.Round(S.OffsetX * dpi);
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, mon.Top, pw, ph, 0x0004 | 0x0010);
        }

        // ================================================================ compact views
        Grid Ears(View v, UIElement lead, UIElement trail, double lm, double rm)
        {
            var fe = lead as FrameworkElement; if (fe != null) { fe.HorizontalAlignment = HorizontalAlignment.Left; fe.Margin = new Thickness(lm, 0, 0, 0); fe.VerticalAlignment = VerticalAlignment.Center; }
            var te = trail as FrameworkElement; if (te != null) { te.HorizontalAlignment = HorizontalAlignment.Right; te.Margin = new Thickness(0, 0, rm, 0); te.VerticalAlignment = VerticalAlignment.Center; }
            if (lead != null) v.Root.Children.Add(lead);
            if (trail != null) v.Root.Children.Add(trail);
            return v.Root;
        }

        static Border ArtBox(double size, double radius, double glyphSize, out TextBlock glyph)
        {
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius), Background = Theme.Raised, VerticalAlignment = VerticalAlignment.Center };
            glyph = K.Glyph("", glyphSize, Theme.Text2);
            b.Child = glyph;
            RenderOptions.SetBitmapScalingMode(b, BitmapScalingMode.HighQuality);
            return b;
        }

        void BuildCompactViews()
        {
            vEmpty = NewView("empty", 60, 6, false);
            vMini = NewView("mini", MiniW, MiniH, false);
            // a small handle inside the tab: shows what's going on (music, timer) and that it can be hovered
            miniHandle = new Border { Width = 24, Height = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 0), Background = K.Solid(Theme.A(Theme.Current.Text, 0.3)), ClipToBounds = true };
            miniHandleScale = new ScaleTransform(1, 1); miniHandle.RenderTransform = miniHandleScale; miniHandle.RenderTransformOrigin = new Point(0.5, 0.5);
            miniFill = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.5), Background = Theme.Orange, Width = 0 };
            miniHandle.Child = miniFill;
            vMini.Root.Children.Add(miniHandle);

            vIdle = NewView("idle", IdleW, PH, false);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            idleClock = new RollingText(13, Theme.Text, FontWeights.SemiBold);
            idleBadge = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            idleBadge.Children.Add(K.Glyph("", 12, Theme.Accent));
            idleBadgeText = K.TB("", 11.5, Theme.Accent, FontWeights.Bold); idleBadgeText.Margin = new Thickness(4, 0, 0, 0);
            idleBadge.Children.Add(idleBadgeText);
            sp.Children.Add(idleClock); sp.Children.Add(idleBadge);
            vIdle.Root.Children.Add(sp);

            vMusic = NewView("music", ActW, PH, false);
            musicArt = ArtBox(PH - 12, 6, 11, out musicGlyph);
            musicEq = new Eq(5, 3, PH * 0.45, 2.5);
            Ears(vMusic, musicArt, musicEq, 7, 14);

            vTimer = NewView("timer", ActW * 0.8, PH, false);
            timerRing = new Ring(PH - 14, 2.6, Theme.Orange);
            timerText = K.TB("0:00", 14, Theme.Orange, FontWeights.SemiBold); K.Tabular(timerText);
            Ears(vTimer, timerRing, timerText, 10, 14);

            vMusicTimer = NewView("musictimer", ActW, PH, false);
            mtArt = ArtBox(PH - 12, 6, 11, out mtGlyph);
            var mtRight = new StackPanel { Orientation = Orientation.Horizontal };
            mtRing = new Ring(PH - 16, 2.4, Theme.Orange); mtRing.Margin = new Thickness(0, 0, 7, 0);
            mtText = K.TB("0:00", 13, Theme.Orange, FontWeights.SemiBold); K.Tabular(mtText);
            mtRight.Children.Add(mtRing); mtRight.Children.Add(mtText);
            Ears(vMusicTimer, mtArt, mtRight, 7, 14);

            vOsd = NewView("osd", PeekW, PeekH, false);
            var og = new Grid { Margin = new Thickness(16, 0, 16, 0) };
            og.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            og.ColumnDefinitions.Add(new ColumnDefinition());
            og.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            osdGlyph = K.Glyph("", 16, Theme.Text); osdGlyph.HorizontalAlignment = HorizontalAlignment.Left;
            osdBar = new SliderBar(0, 100, 1, 50, 0, 6) { Margin = new Thickness(6, 0, 10, 0) };
            osdBar.Changed += v => OsdDrag(v);
            osdPct = K.TB("50", 13, Theme.Text, FontWeights.SemiBold); osdPct.HorizontalAlignment = HorizontalAlignment.Right; K.Tabular(osdPct);
            Grid.SetColumn(osdBar, 1); Grid.SetColumn(osdPct, 2);
            og.Children.Add(osdGlyph); og.Children.Add(osdBar); og.Children.Add(osdPct);
            vOsd.Root.Children.Add(og);

            vCharging = NewView("charging", PeekW, PeekH, false);
            var batt = new Border { Width = 28, Height = 14, CornerRadius = new CornerRadius(4), BorderBrush = Theme.Green, BorderThickness = new Thickness(1.4), Padding = new Thickness(1.6) };
            chargeFill = new Border { CornerRadius = new CornerRadius(2), Background = Theme.Green, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            batt.Child = chargeFill;
            var lead = new StackPanel { Orientation = Orientation.Horizontal };
            lead.Children.Add(batt);
            chargeTitle = K.TB("Charging", 13, Theme.Text, FontWeights.SemiBold); chargeTitle.Margin = new Thickness(10, 0, 0, 0);
            lead.Children.Add(chargeTitle);
            chargePct = K.TB("0%", 13, Theme.Green, FontWeights.Bold); K.Tabular(chargePct);
            Ears(vCharging, lead, chargePct, 16, 16);

            vSystem = NewView("system", PeekW, PeekH + 12, false);
            var sg = new Grid();
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sg.ColumnDefinitions.Add(new ColumnDefinition());
            sysIcon = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(sysIcon, BitmapScalingMode.HighQuality);
            sysGlyph = K.Glyph("", 15, Brushes.White); sysIcon.Child = sysGlyph;
            var stx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 18, 0) };
            sysTitle = K.TB("", 13.5, Theme.Text, FontWeights.SemiBold);
            sysSub = K.TB("", 11.5, Theme.Text2, FontWeights.Normal);
            stx.Children.Add(sysTitle); stx.Children.Add(sysSub);
            Grid.SetColumn(stx, 1);
            sg.Children.Add(sysIcon); sg.Children.Add(stx);
            vSystem.Root.Children.Add(sg);

            BuildNotifyView();

            vTrack = NewView("track", PeekW + 20, PeekH + 14, false);
            var tg = new Grid();
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tg.ColumnDefinitions.Add(new ColumnDefinition());
            trackArt = ArtBox(PeekH - 4, 9, 16, out trackGlyph); trackArt.Margin = new Thickness(9, 0, 0, 0);
            var ttx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 18, 0) };
            trackTitle = K.TB("", 13.5, Theme.Text, FontWeights.SemiBold);
            trackArtist = K.TB("", 11.5, Theme.Text2, FontWeights.Normal);
            ttx.Children.Add(trackTitle); ttx.Children.Add(trackArtist);
            Grid.SetColumn(ttx, 1);
            tg.Children.Add(trackArt); tg.Children.Add(ttx);
            vTrack.Root.Children.Add(tg);

            vWelcome = NewView("welcome", 390, 76, false);
            var wg = new Grid();
            wg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wg.ColumnDefinitions.Add(new ColumnDefinition());
            welcomeIcon = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Margin = new Thickness(15, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            welcomeGlyph = K.Glyph("", 20, Brushes.White); welcomeIcon.Child = welcomeGlyph;
            var wtx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 20, 0) };
            welcomeTitle = K.TB("", 16.5, Theme.Text, FontWeights.SemiBold);
            welcomeSub = K.TB("", 12, Theme.Text2, FontWeights.Normal); welcomeSub.Margin = new Thickness(0, 2, 0, 0);
            wtx.Children.Add(welcomeTitle); wtx.Children.Add(welcomeSub);
            Grid.SetColumn(wtx, 1);
            wg.Children.Add(welcomeIcon); wg.Children.Add(wtx);
            vWelcome.Root.Children.Add(wg);

            vDrop = NewView("drop", 380, 116, false);
            dropRect = new Rectangle { Margin = new Thickness(10), RadiusX = 24, RadiusY = 24, Stroke = Theme.Accent, StrokeThickness = 1.8, StrokeDashArray = new DoubleCollection { 4, 3 } };
            var dsp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var dg = K.Glyph("", 24, Theme.Accent); dg.Margin = new Thickness(0, 0, 0, 6);
            var dt = K.TB("Drop to add to Shelf", 13.5, Theme.Text, FontWeights.SemiBold); dt.HorizontalAlignment = HorizontalAlignment.Center;
            dsp.Children.Add(dg); dsp.Children.Add(dt);
            vDrop.Root.Children.Add(dropRect); vDrop.Root.Children.Add(dsp);
        }

        void OnViewShown(View v)
        {
            UpdateEq();
            if (v == vDrop) dropRect.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(0, -14, TimeSpan.FromMilliseconds(700)) { RepeatBehavior = RepeatBehavior.Forever });
            if (v == vCharging) chargeFill.BeginAnimation(WidthProperty, K.Anim(Math.Max(1, 21.8 * battPct / 100.0), 900, K.OutCubic));
        }
        void OnViewHidden(View v)
        {
            if (v == vDrop) dropRect.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            if (v == vExp) OnExpandedHidden();
            UpdateEq();
        }

        void UpdateEq()
        {
            bool vis = curView == vMusic;
            musicEq.Run(vis && playing, S.Waveform);
            PagesEq();
            UpdateMini(); UpdateMiniBreath();
            bool g = S.ExtraMotion && playing && (curView == vMusic || curView == vMusicTimer);
            if (g)
            {
                glow.Color = artColor;
                if (fxPath.Effect != glow) { fxPath.Effect = glow; glow.Opacity = 0; }
                var pulse = new DoubleAnimation(0.35, 0.65, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                Timeline.SetDesiredFrameRate(pulse, 24);
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, pulse);
            }
            else if (fxPath.Effect == glow)
            {
                var a = K.Anim(0, 400, K.OutCubic);
                a.Completed += delegate { if (fxPath.Effect == glow && !(S.ExtraMotion && playing && (curView == vMusic || curView == vMusicTimer))) { fxPath.Effect = null; UpdateShadow(); } };
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, a);
            }
            else UpdateShadow();
        }
        void PopIcon(UIElement el)
        {
            if (K.AnimScale <= 0) return;
            var st = new ScaleTransform(0.4, 0.4); el.RenderTransform = st; el.RenderTransformOrigin = new Point(0.5, 0.5);
            var a = K.Anim(1, 620, K.Elastic); a.BeginTime = TimeSpan.FromMilliseconds(K.Dur(140));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        void UpdateIdle()
        {
            var now = DateTime.Now;
            string t = "";
            if (S.IdleClock)
            {
                t = now.ToString(S.Clock24h ? (S.ClockSeconds ? "HH:mm:ss" : "HH:mm") : (S.ClockSeconds ? "h:mm:ss tt" : "h:mm tt"), K.EN);
                if (S.IdleDate) t = now.ToString("ddd d MMM", K.EN) + "  ·  " + t;
            }
            int n = S.ShelfItems.Count;
            idleBadge.Visibility = n > 0 && S.IdleClock ? Visibility.Visible : n > 0 ? Visibility.Visible : Visibility.Collapsed;
            idleBadge.Margin = new Thickness(t.Length > 0 ? 10 : 0, 0, 0, 0);
            idleBadgeText.Text = n.ToString();
            idleClock.Text = t; idleClock.Visibility = t.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            var parent = (FrameworkElement)idleClock.Parent;
            parent.Measure(inf);
            double w = Math.Max(IdleW, Math.Ceiling(parent.DesiredSize.Width) + 40);
            if (Math.Abs(w - vIdle.W) > 0.5 || Math.Abs(PH - vIdle.H) > 0.5) SetViewSize(vIdle, w, PH);
        }

        // ================================================================ controller
        void Post(IslandEvent e)
        {
            // Over fullscreen video/games, minor news (song changes) doesn't pop the island open:
            // the mini tab just pings in the event's colour.
            if (fsNow && mini && !miniReveal && !expanded && cur == null && S.FullscreenQuiet && e.Priority <= Pri.Track && curView == vMini)
            {
                MiniPing(e.Kind == "track" ? K.Solid(artColor) : (e.Color ?? Theme.Accent));
                return;
            }
            if (expanded)
            {
                if (e.Priority >= Pri.Urgent) Collapse();
                else { if (e.Queueable) Enqueue(e); return; }
            }
            if (cur != null)
            {
                if (cur.Key != null && cur.Key == e.Key) { Show(e, true); return; }
                if (e.Priority >= cur.Priority) { if (cur.Queueable) Enqueue(cur); Show(e, false); return; }
                if (e.Queueable) Enqueue(e);
                return;
            }
            Show(e, false);
        }

        void Enqueue(IslandEvent e)
        {
            if (e.Key != null) queue.RemoveAll(q => q.Key == e.Key);
            queue.Add(e);
            while (queue.Count > 12) queue.RemoveAt(0);
        }

        void Show(IslandEvent e, bool inPlace)
        {
            bool first = cur == null;
            cur = e;
            inPlaceFill = inPlace;
            View v = FillEventView(e);
            if (!inPlace) { Transition(v); if (!first) Bump(); }
            else if (curView != v) Transition(v);
            evTimer.Stop();
            if (e.Duration > 0)
            {
                evRemain = TimeSpan.FromMilliseconds(e.Duration); evEnds = DateTime.Now + evRemain;
                if (!(hover && S.PauseOnHover)) { evTimer.Interval = evRemain; evTimer.Start(); }
            }
        }

        public void Dismiss()
        {
            evTimer.Stop();
            if (cur != null && cur.Kind == "system" && cur.Priority >= Pri.Urgent) StopAlarm();
            cur = null;
            if (queue.Count > 0 && !expanded)
            {
                var next = queue.OrderByDescending(q => q.Priority).First();
                queue.Remove(next);
                Show(next, false);
                return;
            }
            ShowBase();
        }

        void ShowBase()
        {
            if (expanded || cur != null) return;
            bool musicOn = ses != null && hasMedia && (playing || (DateTime.Now - pausedAt).TotalSeconds < 60);
            View v;
            if (mini && !miniReveal) v = vMini;
            else if (tRunning && musicOn) v = vMusicTimer;
            else if (tRunning) v = vTimer;
            else if (musicOn) v = vMusic;
            else if (!S.ShowWhenIdle) v = vEmpty;
            else { UpdateIdle(); v = vIdle; }
            if (v == vMusicTimer || v == vMusic) { SetViewSize(v, ActW, PH); }
            if (v == vTimer) SetViewSize(v, ActW * 0.78, PH);
            if (v != vMini) miniPeek = false;
            UpdateMini();
            Transition(v);
            UpdateMiniBreath();
        }

        View FillEventView(IslandEvent e)
        {
            switch (e.Kind)
            {
                case "osd-volume":
                case "osd-brightness":
                    {
                        bool vol = e.Kind == "osd-volume";
                        int pct = (int)Math.Round(e.Value * 100);
                        osdGlyph.Text = vol ? (e.Muted || pct == 0 ? "" : pct < 34 ? "" : pct < 67 ? "" : "") : "";
                        osdPct.Text = e.Muted ? "—" : pct.ToString();
                        if (!osdDragging) osdBar.SetValue(e.Muted ? 0 : pct, false);
                        osdBar.Tag = e.Kind;
                        SetViewSize(vOsd, PeekW, PeekH);
                        return vOsd;
                    }
                case "charging":
                    chargePct.Text = battPct + "%";
                    chargeFill.Width = 0;
                    SetViewSize(vCharging, PeekW, PeekH);
                    return vCharging;
                case "track":
                    trackTitle.Text = e.Title; trackArtist.Text = e.Sub ?? "";
                    SetArt(trackArt, trackGlyph, e.Image);
                    if (!inPlaceFill) PopIcon(trackArt);
                    SetViewSize(vTrack, Math.Max(PeekW + 20, Math.Min(440, Measure(e.Title, e.Sub) + PeekH + 40)), PeekH + 14);
                    return vTrack;
                case "welcome":
                    welcomeTitle.Text = e.Title; welcomeSub.Text = e.Sub ?? "";
                    welcomeGlyph.Text = e.Glyph; welcomeIcon.Background = e.Color;
                    if (!inPlaceFill) PopIcon(welcomeIcon);
                    SetViewSize(vWelcome, Math.Max(360, Math.Min(480, Measure(e.Title, e.Sub, 16.5) + 46 + 50)), 76);
                    return vWelcome;
                case "drop":
                    return vDrop;
                case "notify":
                    return FillNotify(e);
                default:
                    sysTitle.Text = e.Title ?? ""; sysSub.Text = e.Sub ?? "";
                    sysSub.Visibility = string.IsNullOrEmpty(e.Sub) ? Visibility.Collapsed : Visibility.Visible;
                    if (e.Image != null)
                    {
                        var ib = new ImageBrush(e.Image) { Stretch = Stretch.UniformToFill }; ib.Freeze();
                        sysIcon.Background = ib; sysIcon.CornerRadius = new CornerRadius(9); sysGlyph.Text = "";
                    }
                    else { sysIcon.Background = e.Color ?? Theme.Accent; sysIcon.CornerRadius = new CornerRadius(17); sysGlyph.Text = e.Glyph ?? ""; sysGlyph.Foreground = Brushes.White; }
                    if (!inPlaceFill) PopIcon(sysIcon);
                    SetViewSize(vSystem, Math.Max(PeekW - 40, Math.Min(460, Measure(e.Title, e.Sub) + 34 + 50)), PeekH + 12);
                    return vSystem;
            }
        }

        static double Measure(string a, string b, double size = 13.5)
        {
            var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            var t1 = K.TB(a ?? "", size, Theme.Text, FontWeights.SemiBold); t1.FontFamily = K.Font; t1.Measure(inf);
            var t2 = K.TB(b ?? "", 11.5, Theme.Text, FontWeights.Normal); t2.FontFamily = K.Font; t2.Measure(inf);
            return Math.Max(t1.DesiredSize.Width, t2.DesiredSize.Width);
        }

        void SystemEvent(string glyph, Brush color, string title, string sub, int pri, int dur, string key, Action click)
        {
            Post(new IslandEvent { Kind = "system", Glyph = glyph, Color = color, Title = title, Sub = sub, Priority = pri, Duration = dur, Key = key, Queueable = true, Click = click });
        }

        bool osdDragging, inPlaceFill; DateTime osdDragAt;
        void OsdDrag(double v)
        {
            osdDragging = true; osdDragAt = DateTime.Now;
            if ((string)osdBar.Tag == "osd-brightness") Svc.Brightness.Set((int)v); else Svc.Audio.SetLevel((float)(v / 100));
            osdPct.Text = ((int)v).ToString();
            if (cur != null) { evTimer.Stop(); evTimer.Interval = TimeSpan.FromMilliseconds(S.OsdMs); evTimer.Start(); }
            var t = Timer(300, null); t.Tick += delegate { t.Stop(); if ((DateTime.Now - osdDragAt).TotalMilliseconds >= 280) osdDragging = false; }; t.Start();
        }

        // ================================================================ hover & input
        void BeginHover()
        {
            leaveTimer.Stop();
            if (hover) return;
            hover = true; hoverPoll.Start();
            if (cur != null && S.PauseOnHover && evTimer.IsEnabled) { evRemain = evEnds - DateTime.Now; evTimer.Stop(); NotifyBarHold(); }
            if (!expanded && S.HoverExpand && CanHoverOpen()) hoverOpen.Start();
            Retarget();
        }

        void EndHover()
        {
            hoverPoll.Stop(); hoverOpen.Stop();
            if (!hover) return;
            hover = false; pressed = false;
            if (cur != null && cur.Duration > 0 && !evTimer.IsEnabled)
            {
                var rem = evRemain.TotalMilliseconds < 900 ? TimeSpan.FromMilliseconds(900) : evRemain;
                evEnds = DateTime.Now + rem; evTimer.Interval = rem; evTimer.Start();
                NotifyBarRun(rem.TotalMilliseconds);
            }
            if (expanded && expandedByHover) leaveTimer.Start();
            if (mini && miniReveal && cur == null && !expanded) { miniReveal = false; ShowBase(); }
            Retarget();
        }

        bool CanHoverOpen()
        {
            if (cur == null) return curView != vMini && curView != vEmpty;
            return cur.Kind == "system" || cur.Kind == "track" || cur.Kind == "welcome";
        }

        bool CursorInIsland(double pad) { return PointInIsland(Native.CursorPx(), pad); }

        bool PointInIsland(Point devPx, double pad)
        {
            try
            {
                var p = shapeGrid.PointFromScreen(devPx);
                return p.X >= -pad && p.Y >= -pad - 10 && p.X <= shapeGrid.ActualWidth + pad && p.Y <= shapeGrid.ActualHeight + pad;
            }
            catch { return false; }
        }

        void Pin()
        {
            expandedByHover = false; leaveTimer.Stop();
            if (!pinned) { pinned = true; ActivateIsland(); }
        }

        public void ActivateIsland()
        {
            Log.T("activate");
            var fg = Native.GetForegroundWindow();
            if (fg != hwnd && fg != IntPtr.Zero) prevForeground = fg;
            Native.SetNoActivate(hwnd, false);
            Activate();
        }

        void ReleaseFocus()
        {
            Log.T("releasefocus");
            Native.SetNoActivate(hwnd, true);
            if (IsActive && prevForeground != IntPtr.Zero && Native.IsWindow(prevForeground)) Native.SetForegroundWindow(prevForeground);
            prevForeground = IntPtr.Zero;
        }

        void OnKey(object s, KeyEventArgs e)
        {
            if (cur != null && cur.Key == "dpi" && (e.Key == Key.Enter || e.Key == Key.Return)) { e.Handled = true; Restart(); return; }
            if (!expanded) return;
            if (e.Key == Key.Escape) { e.Handled = true; if (!PagesEscape()) Collapse(); return; }
            PagesKey(e);
        }

        // ================================================================ expand / collapse
        public void Expand(string page, bool byClick)
        {
            if (hidden && !mini) return;
            if (cur != null && cur.Kind != "system" && cur.Kind != "track" && cur.Kind != "welcome" && cur.Kind != "charging" && cur.Kind != "osd-volume" && cur.Kind != "osd-brightness" && cur.Kind != "notify") return;
            if (cur != null) { if (cur.Queueable && cur.Duration > 0 && cur.Priority < Pri.Urgent) { } evTimer.Stop(); cur = null; }
            expandMode = "pages";
            expanded = true; miniReveal = true;
            if (byClick) { expandedByHover = false; pinned = true; ActivateIsland(); }
            else pinned = false;
            OpenPages(page);
            Transition(vExp);
            mouseHook.Start(Dispatcher);
            UpdateScaleTarget();
        }

        public void OpenMode(string mode)
        {
            if (hidden && !mini) SetHidden(false);
            if (cur != null) { evTimer.Stop(); cur = null; }
            expanded = true; pinned = true; expandedByHover = false; miniReveal = true;
            ActivateIsland();
            expandMode = mode;
            if (mode == "launcher") OpenLauncher();
            else if (mode == "wallpaper") OpenWallpapers();
            mouseHook.Start(Dispatcher);
            UpdateScaleTarget();
        }

        public void Collapse()
        {
            if (!expanded) return;
            if (Environment.GetEnvironmentVariable("DI_DEBUG") == "1")
                Log.Write("collapse ← " + string.Join(" < ", new System.Diagnostics.StackTrace().GetFrames().Skip(1).Take(3).Select(f => f.GetMethod().Name)));
            expanded = false; pinned = false; expandedByHover = false;
            leaveTimer.Stop(); hoverOpen.Stop();
            mouseHook.Stop();
            ClosePages();
            ReleaseFocus();
            if (mini) miniReveal = CursorInIsland(6);
            UpdateScaleTarget();
            if (queue.Count > 0) { var next = queue.OrderByDescending(q => q.Priority).First(); queue.Remove(next); Show(next, false); }
            else ShowBase();
        }

        public void Toggle()
        {
            if (expanded) Collapse(); else Expand(null, true);
        }

        // ================================================================ lifecycle
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd = new WindowInteropHelper(this).Handle;
            var src = HwndSource.FromHwnd(hwnd);
            dpi = src.CompositionTarget.TransformToDevice.M11;
            Native.SetNoActivate(hwnd, true);
            src.AddHook(WndProc);
            Native.AddClipboardFormatListener(hwnd);
            Svc.Hotkeys.Init(hwnd);
            Svc.Hotkeys.Pressed += OnHotkey;
            Svc.WinKeys.Init(hwnd, OnHotkey);
            if (gpu)
            {
                src.CompositionTarget.BackgroundColor = Colors.Transparent;
                var m = new Native.MARGINS { L = -1, R = -1, T = -1, B = -1 };
                Native.DwmExtendFrameIntoClientArea(hwnd, ref m);
                int noRound = 1; Native.DwmSetWindowAttribute(hwnd, 33, ref noRound, 4);
                // layered (constant alpha, still GPU-composed by DWM) so WS_EX_TRANSPARENT can pass clicks through
                Native.SetWindowLong(hwnd, -20, Native.GetWindowLong(hwnd, -20) | 0x80000);
                Native.SetLayeredWindowAttributes(hwnd, 0, 255, 2);
                SetClickThrough(true);
                hitTimer = Timer(30, delegate { HitPoll(); });
                hitTimer.Start();
                SetFixedWindow();
            }
            else SetWin(400, 120);
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x031D) { clipDebounce.Stop(); clipDebounce.Start(); }
            else if (msg == 0x0312) { if (!Svc.WinKeys.Handle(w.ToInt32())) Svc.Hotkeys.Handle(w.ToInt32()); }
            else if (msg == 0x020E) { int delta = (short)((w.ToInt64() >> 16) & 0xFFFF); OnHWheel(delta); handled = true; }
            else if (msg == 0x02E0) { dpi = (w.ToInt32() & 0xFFFF) / 96.0; SetWin(winW, winH); }
            else if (msg == 0x007C && gpu && w.ToInt32() == -20)
            {
                // WM_STYLECHANGING: WPF strips WS_EX_LAYERED from a non-AllowsTransparency window (and
                // rewrites the style on Show/Topmost). Without layered, WS_EX_TRANSPARENT doesn't pass
                // clicks through, so the whole fixed-size window blocked the screen under it. Keep
                // layered on, and transparent as HitPoll wants it, and don't let WPF's handler undo it.
                int ns = Marshal.ReadInt32(l, 4) | 0x80000;
                ns = clickThrough ? (ns | 0x20) : (ns & ~0x20);
                Marshal.WriteInt32(l, 4, ns);
                handled = true;
            }
            else if (msg == 0x004A)
            {
                var parts = Ipc.Read(l);
                if (parts != null) { Dispatcher.BeginInvoke(new Action(() => HandleIpc(parts))); handled = true; return new IntPtr(1); }
            }
            return IntPtr.Zero;
        }

        void HandleIpc(string[] a)
        {
            string c = a.Length > 0 ? a[0].ToLowerInvariant() : "";
            string arg = a.Length > 1 ? a[1] : null;
            try
            {
                switch (c)
                {
                    case "toggle": Toggle(); break;
                    case "expand": Expand(arg, true); break;
                    case "show": Expand(arg, false); break;   // open without taking keyboard focus (screenshots while the PC is in use)
                    case "collapse": Collapse(); break;
                    case "renderinfo": Log.Write("render tier=" + (RenderCapability.Tier >> 16) + " hwnd mode=" + (HwndSource.FromHwnd(hwnd).CompositionTarget.RenderMode) + " gpuRenderer=" + gpu + " layered=" + AllowsTransparency); break;
                    case "lkey": { Key lk; if (expandMode == "launcher" && arg != null && Enum.TryParse(arg, true, out lk)) LauncherKey(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, lk) { RoutedEvent = Keyboard.KeyDownEvent }); } break;
                    case "dotclick": if (arg != null) { Pin(); DotTip(null); GoPage(arg); } break;   // same path as clicking a page dot
                    case "dots":   // test the page-indicator hover state: dots on|off · dots hover <page>
                        if (arg == "hover" && a.Length > 2) { dotsHover = true; RenderDots(); string hk = a[2]; var hd = dotItems.FirstOrDefault(x => x.Key == hk); var tm = Timer(500, null); tm.Tick += delegate { tm.Stop(); dotHoverKey = hk; ColorDotLabels(); DotTip(hd); }; tm.Start(); }
                        else { dotsHover = arg == "on"; if (!dotsHover) { dotHoverKey = null; DotTip(null); } RenderDots(); }
                        break;
                    // games test hooks: game <blocks|sudoku|mines|menu> · gamekey <Key> · minesclick <index> [flag]
                    case "game": Expand("games", true); if (arg == "menu") { if (game != null) CloseGame(); } else if (arg != null) OpenGame(arg); break;
                    case "gamekey": { Key gk; if (arg != null && Enum.TryParse(arg, out gk)) GamesKey(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, gk) { RoutedEvent = Keyboard.KeyDownEvent }); } break;
                    case "minesclick": { int mi; if (game == "mines" && int.TryParse(arg, out mi)) { if (a.Length > 2 && a[2] == "flag") MFlag(mi); else MClick(mi); } } break;
                    case "todotoggle":   // test hook: tick the n-th to-do (as stored) through its real checkbox
                        {
                            int ti; FrameworkElement row;
                            if (int.TryParse(arg, out ti) && ti >= 0 && ti < Store.Todos.Count && todoRowMap.TryGetValue(Store.Todos[ti], out row))
                            {
                                var chk = ((Grid)((Border)row).Child).Children[0];
                                chk.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                            }
                        }
                        break;
                    case "mediainfo":   // log what each media session reports for its timeline
                        foreach (var s in Sessions())
                        {
                            try
                            {
                                var tl = s.GetTimelineProperties();
                                Log.Write(string.Format("media {0} start={1} end={2} pos={3} minSeek={4} maxSeek={5} updated={6:HH:mm:ss} cur={7}", SourceName(s), tl.StartTime, tl.EndTime, tl.Position, tl.MinSeekTime, tl.MaxSeekTime, tl.LastUpdatedTime, s == ses));
                            }
                            catch (Exception ex) { Log.Write("media err " + ex.Message); }
                        }
                        { double ps; Log.Write(string.Format("home kind={0} prog={1:0.000} track={2} w={3:0} fill={4:0} ses={5}", hNowKind, MediaProgress(out ps), hNowTrack.Visibility, hNowTrack.ActualWidth, hNowFill.ActualWidth, ses != null)); }
                        break;
                    case "dismiss": if (cur != null) Dismiss(); break;
                    case "fs": forceFs = arg == "on" ? 1 : arg == "off" ? 0 : -1; CheckFullscreen(); break;
                    case "minipeek": peekHoldUntil = DateTime.Now.AddSeconds(4); SetMiniPeek(arg != "off"); break;
                    case "miniping": MiniPing(Theme.Accent); break;
                    case "page": if (expanded && expandMode == "pages") GoPage(arg); else Expand(arg, true); break;
                    case "launcher": OpenMode("launcher"); if (arg != null) lInput.Text = string.Join(" ", a.Skip(1)); break;
                    case "wallpaper": OpenMode("wallpaper"); if (arg == "online") SwitchWallTab("online"); else if (arg == "local") SwitchWallTab("local"); break;
                    case "settings": OpenSettings(arg); break;
                    case "notifyinfo": Log.Write("notify " + NotifyDebug()); break;
                    case "caffeine": SetCaffeine(arg == "on" || (arg == null && !caffeine)); break;
                    case "wallsearch": OpenMode("wallpaper"); SwitchWallTab("online"); wSearch.Text = string.Join(" ", a.Skip(1)); wSearchActive = wSearch.Text.Trim().Length > 0; WallSortState(); LoadOnline(true); break;
                    case "party": Party(); break;
                    case "winkey": if (arg != null) Svc.WinKeys.Run(arg); else Log.Write("winkeys active=" + Svc.WinKeys.Active); break;
                    case "setup": { OpenSetup(); int sn; if (int.TryParse(arg, out sn) && setupWin != null) { var w2 = setupWin; var tm = Timer(600, null); tm.Tick += delegate { tm.Stop(); w2.ShowStep(sn); }; tm.Start(); } } break;
                    case "settings-search": OpenSettings(null); if (settingsWin != null) settingsWin.Search(string.Join(" ", a.Skip(1))); break;
                    case "welcome": PreviewWelcome(); break;
                    case "quit": Close(); break;
                    case "update":
                        // update [check|install]: check logs the result; install checks first, then updates if newer
                        Updater.CheckAsync(newer =>
                        {
                            Log.Write("update: current=" + AppInfo.Version + " latest=" + Updater.LatestVersion + " newer=" + newer + " asset=" + Updater.AssetName + " state=" + Updater.State + (Updater.State == Updater.St.Error ? " error=" + Updater.Error : ""));
                            if (newer && arg == "install") Updater.InstallAsync(() => Dispatcher.BeginInvoke(new Action(Restart)));
                        });
                        break;
                    case "event": SystemEvent("", Theme.Accent, arg ?? "Event", a.Length > 2 ? a[2] : null, Pri.System, S.SystemMs, null, null); break;
                    case "timer":
                        if (arg == "toggle") TimerPauseToggle(); else if (arg == "cancel") TimerCancel();
                        else { int sec; if (int.TryParse(arg, out sec)) TimerStart(TimeSpan.FromSeconds(sec)); }
                        break;
                    case "todo": if (arg != null) { Store.Todos.Insert(0, new TodoItem { Text = string.Join(" ", a.Skip(1)), Created = DateTime.Now.Ticks }); Store.SaveTodos(); RefreshTodos(null); } break;
                    case "shelf": if (arg == "clear") ClearShelf(); else if (arg != null) { AddToShelf(a.Skip(1)); RefreshShelf(null); } break;
                    case "ai": if (arg == "send" && a.Length > 2) { Expand("ai", true); aiInput.Text = string.Join(" ", a.Skip(2)); AiSend(); } else if (arg == "new") AiNewChat(); else if (arg == "attach" && a.Length > 2) { Expand("ai", true); AiAttachFiles(a.Skip(2)); } else if (arg == "paste") { Expand("ai", true); Log.Write("ai paste -> " + AiPasteImage()); } break;
                    case "theme": if (arg != null) { S.Palette = arg; Store.Save(); ApplySettings(); } break;
                    case "set":
                        {
                            var f = typeof(AppSettings).GetField(arg ?? "", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                            if (f != null && a.Length > 2)
                            {
                                f.SetValue(S, Convert.ChangeType(a[2], f.FieldType, CultureInfo.InvariantCulture));
                                Store.Save(); ApplySettings();
                            }
                            break;
                        }
                    case "probe":
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            foreach (var pv in OnlineWallpapers.Providers)
                            {
                                try
                                {
                                    bool more; var list = OnlineWallpapers.Fetch(pv, 0, "", 1, out more);
                                    var th = list.Count > 0 ? OnlineWallpapers.Thumb(list[0]) : null;
                                    Log.Write("probe " + pv + ": " + list.Count + " items, more=" + more + ", thumb=" + (th != null ? th.PixelWidth + "x" + th.PixelHeight : "none") + ", full=" + (list.Count > 0 ? list[0].Full : ""));
                                }
                                catch (Exception ex) { Log.Write("probe " + pv + " failed: " + ex.Message); }
                            }
                        });
                        break;
                    case "closesettings": if (settingsWin != null) settingsWin.AnimatedClose(); break;
                    case "simulate":
                        switch (arg)
                        {
                            case "volume": Post(new IslandEvent { Kind = "osd-volume", Key = "osd-volume", Priority = Pri.Osd, Duration = S.OsdMs, Value = Svc.Audio.Level }); break;
                            case "brightness": Post(new IslandEvent { Kind = "osd-brightness", Key = "osd-brightness", Priority = Pri.Osd, Duration = S.OsdMs, Value = Svc.Brightness.Level / 100.0 }); break;
                            case "charging": Post(new IslandEvent { Kind = "charging", Key = "power", Priority = Pri.Charging, Duration = 3200 }); break;
                            case "track": Post(new IslandEvent { Kind = "track", Key = "track", Priority = Pri.Track, Duration = S.TrackMs, Title = "Song title", Sub = "Artist", Image = art }); break;
                            case "urgent": SystemEvent("", Theme.Red, "Important event", "Stays until dismissed", Pri.Urgent, 0, "urgent", () => Dismiss()); break;
                            case "scale": SystemEvent("\uE7F4", Theme.Accent, "Display scale changed", "Press Enter or click here to restart the island", Pri.Urgent, 0, "dpi", Restart); break;
                            case "notify": Post(new IslandEvent { Kind = "notify", Key = "app:test", Priority = Pri.Notification, Duration = 8000, Title = "Rizky", Sub = "Bro jadi main nanti malam? Kabarin ya, gue udah siap dari tadi sore nih, tinggal nunggu lu doang", App = "WhatsApp", Count = 1, Queueable = true }); break;
                            case "drop": Post(new IslandEvent { Kind = "drop", Key = "drop", Priority = Pri.Urgent - 1, Duration = 2500 }); break;
                        }
                        break;
                }
            }
            catch (Exception ex) { Log.Error(ex); }
            Log.Write("ipc " + string.Join(" ", a) + " → view=" + (curView != null ? curView.Name : "-") + " expanded=" + expanded + " page=" + CurrentPageKey());
        }

        void OnLoaded(object s, RoutedEventArgs e)
        {
            K.AnimScale = Store.AnimScale();
            TuneSprings(); ApplyFill();
            var ps = WF.SystemInformation.PowerStatus;
            lastAc = ps.PowerLineStatus == WF.PowerLineStatus.Online;
            UpdateBattery();
            SystemEvents.PowerModeChanged += OnPowerMode;
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            Svc.Audio.Init(Dispatcher);
            Svc.Audio.VolumeChanged += (lvl, muted) => { if (S.EvVolume && !osdDragging) Post(new IslandEvent { Kind = "osd-volume", Key = "osd-volume", Priority = Pri.Osd, Duration = S.OsdMs, Value = lvl, Muted = muted }); };
            Svc.Audio.MicMuteChanged += m => { if (S.EvMic) SystemEvent(m ? "" : "", m ? (Brush)Theme.Red : Theme.Green, m ? "Microphone muted" : "Microphone on", null, Pri.System, S.SystemMs, "mic", null); };
            Svc.Brightness.Init(Dispatcher);
            Svc.Brightness.Changed += b => { if (S.EvBrightness && !osdDragging) Post(new IslandEvent { Kind = "osd-brightness", Key = "osd-brightness", Priority = Pri.Osd, Duration = S.OsdMs, Value = b / 100.0 }); };
            Svc.Net.Init(Dispatcher);
            Svc.Net.Event += (g, t, sub) => { if (S.EvNetwork) SystemEvent(g, t.Contains("Disconnected") ? (Brush)Theme.Red : Theme.Accent, t, sub, Pri.System, S.SystemMs, "net", null); };
            Svc.Bt.Init(Dispatcher);
            Svc.Bt.Event += (name, on) => { if (S.EvBluetooth) SystemEvent("", on ? (Brush)Theme.Accent : Theme.Hover, on ? "Connected" : "Disconnected", name, Pri.System, S.SystemMs, "bt:" + name, null); };
            Svc.Ai.Init(Dispatcher);
            Svc.Apps.Init(Dispatcher);

            clockTimer.Start(); fsTimer.Start(); slowTimer.Start();
            InstallWindowHooks();
            CheckScaleChange();   // records the scale we started with
            CheckFullscreen();
            InitMedia();
            InitAppNotifications();
            ShowBase();
            var greet = Timer(600, null);
            greet.Tick += delegate { greet.Stop(); StartupGreeting(); };
            greet.Start();
        }

        int sampleIdx;
        public void SampleEvent()
        {
            switch (sampleIdx++ % 4)
            {
                case 0: Post(new IslandEvent { Kind = "track", Key = "track", Priority = Pri.Track, Duration = S.TrackMs, Title = mTitleStr.Length > 0 ? mTitleStr : "Midnight City", Sub = mArtistStr.Length > 0 ? mArtistStr : "M83", Image = art }); break;
                case 1: SystemEvent("", Theme.Accent, "Copied", "This is how events look", Pri.System, S.SystemMs, "sample", null); break;
                case 2: Post(new IslandEvent { Kind = "osd-volume", Key = "osd-volume", Priority = Pri.Osd, Duration = S.OsdMs, Value = Svc.Audio.Available ? Svc.Audio.Level : 0.6 }); break;
                default: Post(new IslandEvent { Kind = "charging", Key = "power", Priority = Pri.Charging, Duration = 3200 }); break;
            }
        }

        // ---- first-run setup wizard
        SetupWizard setupWin;
        public void OpenSetup()
        {
            if (setupWin != null) { setupWin.Activate(); return; }
            setupWin = new SetupWizard(this);
            setupWin.Closed += delegate { setupWin = null; };
            setupWin.Show(); setupWin.Activate();
        }

        void StartupGreeting()
        {
            // just came back from an update: say whether it took
            if (!string.IsNullOrEmpty(S.UpdatedFrom))
            {
                string from = S.UpdatedFrom; S.UpdatedFrom = ""; Store.Save();
                var uc = Timer(1800, null);
                uc.Tick += delegate
                {
                    uc.Stop();
                    if (from != AppInfo.Version)
                        SystemEvent("", Theme.Accent, "Updated to v" + AppInfo.Version, "Was v" + from + " · click for Settings → About", Pri.System, 5000, "updated", () => OpenSettings("about"));
                    else
                        SystemEvent("", Theme.Accent, "Update didn't apply", "Still on v" + AppInfo.Version + " · see Settings → About", Pri.System, 6000, "updated", () => OpenSettings("about"));
                };
                uc.Start();
            }
            // a new GitHub release: announce it once per version, a while after start
            if (S.AutoCheckUpdates)
            {
                var ut = Timer(15000, null);
                ut.Tick += delegate
                {
                    ut.Stop();
                    Updater.CheckAsync(newer => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!newer || S.UpdateNotified == Updater.LatestVersion) return;
                        S.UpdateNotified = Updater.LatestVersion; Store.Save();
                        SystemEvent("", Theme.Accent, "Update available: v" + Updater.LatestVersion, "Click to open Settings → About and update", Pri.System, 8000, "update", () => OpenSettings("about"));
                    })));
                };
                ut.Start();
            }
            // brand-new install: the setup wizard comes up once the island has settled
            if (Store.FirstRun && !S.SetupDone) { var st = Timer(1400, null); st.Tick += delegate { st.Stop(); OpenSetup(); }; st.Start(); }
            if (S.Welcome)
            {
                string boot = (DateTime.Now - TimeSpan.FromMilliseconds(Native.GetTickCount64())).ToString("yyyyMMddHHmm");
                if (S.WelcomeMode == 1 && S.LastWelcomeBoot == boot) return;
                S.LastWelcomeBoot = boot; Store.Save();
                PreviewWelcome();
            }
            else if (!silent) SystemEvent("", Theme.Accent, "Dynamic Island is on", "Click to open · right-click for menu", Pri.System, 2600, null, null);
        }

        public void PreviewWelcome()
        {
            var now = DateTime.Now; int h = now.Hour;
            string part = h < 5 ? "night" : h < 12 ? "morning" : h < 18 ? "afternoon" : "evening";
            string name = (S.UserName ?? "").Trim();
            string custom = (S.GreetingText ?? "").Trim();
            string title = custom.Length > 0 ? custom.Replace("{name}", name).Trim() : "Good " + part + (name.Length > 0 ? ", " + name : "");
            int open = Store.Todos.Count(x => !x.Done);
            string sub = now.ToString("dddd, MMMM d", K.EN) + (open > 0 ? "  ·  " + open + " open to-do" + (open > 1 ? "s" : "") : "");
            if (name.Length == 0 && Store.FirstRun) sub = "Right-click the island → Settings to add your name";
            bool night = part == "night" || part == "evening";
            Post(new IslandEvent
            {
                Kind = "welcome", Priority = Pri.System, Duration = 4500, Queueable = true, Title = title, Sub = sub,
                Glyph = night ? "" : "", Color = K.Solid(night ? Theme.Hex("#5e5ce6") : part == "afternoon" ? Theme.Hex("#ff6b35") : Theme.Hex("#ff9f0a")),
                Click = () => { Dismiss(); Expand("home", true); }
            });
        }

        void Cleanup()
        {
            Store.SaveNow(); Store.SaveTodos();
            Svc.Ai.Cancel(); Svc.Hotkeys.Stop(); Svc.WinKeys.Stop(); Svc.Brightness.Stop(); Svc.Bt.Stop(); mouseHook.Stop();
            foreach (var h in winHooks) Native.UnhookWinEvent(h);
            if (moveHook != IntPtr.Zero) Native.UnhookWinEvent(moveHook);
            SystemEvents.PowerModeChanged -= OnPowerMode;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            if (hwnd != IntPtr.Zero) Native.RemoveClipboardFormatListener(hwnd);
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (settingsWin != null) settingsWin.Close();
            Application.Current.Shutdown();
        }

        public void Restart()
        {
            Store.SaveNow();
            try { System.Diagnostics.Process.Start(Store.ExePath, "--silent --restart"); } catch { }
            Close();
        }

        string fontKey;
        public void ApplySettings()
        {
            Svc.WinKeys.Apply();
            string fk = S.Font + "|" + S.FontCustom;
            if (fk != fontKey)
            {
                fontKey = fk;
                K.Font = K.ResolveFont(S.Font, S.FontCustom);
                FontFamily = K.Font;
                if (settingsWin != null) settingsWin.FontFamily = K.Font;
                UpdateIdle();
            }
            K.AnimScale = Store.AnimScale();
            TuneSprings();
            ApplySizes();
            Theme.Apply(Store.ResolvePalette(), true);
            ApplyFill();
            SetViewSize(vOsd, PeekW, PeekH);
            ApplyPagesSettings();
            hoverOpen.Interval = TimeSpan.FromMilliseconds(Math.Max(60, S.HoverDelay));
            if (hwnd != IntPtr.Zero) { if (gpu) SetFixedWindow(); else SetWin(winW, winH); CheckFullscreen(); }
            if (expanded) RefreshExpandedSize();
            else if (cur == null) ShowBase();
            UpdateIdle();
            UpdateEq();
            Retarget();
        }

        void OnHotkey(string name)
        {
            Log.Write("hotkey " + name + " expanded=" + expanded + " cur=" + (cur == null ? "-" : cur.Kind) + " hidden=" + hidden + " mini=" + mini);
            switch (name)
            {
                case "toggle": Toggle(); break;
                case "launcher": if (expanded && expandMode == "launcher") Collapse(); else OpenMode("launcher"); break;
                case "wallpaper": if (expanded && expandMode == "wallpaper") Collapse(); else OpenMode("wallpaper"); break;
                case "ai": case "todo": case "timer": case "system": case "shelf":
                    if (expanded && expandMode == "pages" && CurrentPageKey() == name) Collapse(); else Expand(name, true); break;
                case "pauseTimer": TimerPauseToggle(); break;
                case "dismiss": if (cur != null) Dismiss(); else if (expanded) Collapse(); break;
                case "settings": OpenSettings(null); break;
            }
        }

        // ================================================================ tray
        void BuildTray()
        {
            menu = new WF.ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false };
            menu.Items.Add("Open island", null, delegate { Dispatcher.BeginInvoke(new Action(() => Expand(null, true))); });
            menu.Items.Add("Launcher", null, delegate { Dispatcher.BeginInvoke(new Action(() => OpenMode("launcher"))); });
            menu.Items.Add("Wallpapers", null, delegate { Dispatcher.BeginInvoke(new Action(() => OpenMode("wallpaper"))); });
            menu.Items.Add("Settings", null, delegate { Dispatcher.BeginInvoke(new Action(() => OpenSettings(null))); });
            menu.Items.Add("Setup wizard…", null, delegate { Dispatcher.BeginInvoke(new Action(OpenSetup)); });
            var dnd = new WF.ToolStripMenuItem("Do not disturb") { Checked = S.DoNotDisturb, CheckOnClick = true };
            dnd.CheckedChanged += delegate { bool on = dnd.Checked; Dispatcher.BeginInvoke(new Action(() => SetDoNotDisturb(on))); };
            menu.Opening += delegate { dnd.Checked = S.DoNotDisturb; };
            menu.Items.Add(dnd);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Quit", null, delegate { Close(); });
            foreach (WF.ToolStripItem it in menu.Items) { it.ForeColor = SD.Color.FromArgb(235, 235, 240); it.Padding = new WF.Padding(6, 4, 6, 4); }
            tray = new WF.NotifyIcon { Icon = MakeIcon(), Text = "Dynamic Island", Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) Dispatcher.BeginInvoke(new Action(() => Expand(null, true))); };
            tray.MouseDoubleClick += delegate { Dispatcher.BeginInvoke(new Action(() => OpenSettings(null))); };
        }

        class DarkRenderer : WF.ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
            protected override void OnRenderItemText(WF.ToolStripItemTextRenderEventArgs e) { e.TextColor = SD.Color.FromArgb(235, 235, 240); base.OnRenderItemText(e); }
        }
        class DarkColors : WF.ProfessionalColorTable
        {
            static readonly SD.Color Bg = SD.Color.FromArgb(28, 28, 31), Hi = SD.Color.FromArgb(52, 52, 58), Ln = SD.Color.FromArgb(48, 48, 54);
            public override SD.Color ToolStripDropDownBackground { get { return Bg; } }
            public override SD.Color MenuItemSelected { get { return Hi; } }
            public override SD.Color MenuItemBorder { get { return Hi; } }
            public override SD.Color MenuBorder { get { return Ln; } }
            public override SD.Color SeparatorDark { get { return Ln; } }
            public override SD.Color SeparatorLight { get { return Ln; } }
            public override SD.Color ImageMarginGradientBegin { get { return Bg; } }
            public override SD.Color ImageMarginGradientMiddle { get { return Bg; } }
            public override SD.Color ImageMarginGradientEnd { get { return Bg; } }
            public override SD.Color MenuItemSelectedGradientBegin { get { return Hi; } }
            public override SD.Color MenuItemSelectedGradientEnd { get { return Hi; } }
        }

        static SD.Icon MakeIcon()
        {
            using (var bmp = new SD.Bitmap(32, 32))
            {
                using (var g = SD.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
                    using (var p = new SD.Drawing2D.GraphicsPath())
                    {
                        p.AddArc(2, 9, 14, 14, 90, 180); p.AddArc(16, 9, 14, 14, 270, 180); p.CloseFigure();
                        using (var br = new SD.SolidBrush(SD.Color.Black)) g.FillPath(br, p);
                        using (var pen = new SD.Pen(SD.Color.FromArgb(200, 255, 255, 255), 1.5f)) g.DrawPath(pen, p);
                    }
                    using (var dot = new SD.SolidBrush(SD.Color.FromArgb(255, 48, 209, 88))) g.FillEllipse(dot, 21, 13, 6, 6);
                }
                return SD.Icon.FromHandle(bmp.GetHicon());
            }
        }

        public void OpenSettings(string page)
        {
            if (expanded) Collapse();
            if (settingsWin == null)
            {
                settingsWin = new SettingsWindow(this, 0);
                settingsWin.Closed += delegate { settingsWin = null; };
                settingsWin.Show();
            }
            if (page != null) settingsWin.ShowPage(page);
            var sw0 = settingsWin;
            sw0.Topmost = true; sw0.Activate();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => { sw0.Topmost = false; }));
        }

        // ================================================================ system events
        int frames;
        void OnClock()
        {
            if (DateTime.Now.Second % 5 == 0 && Environment.GetEnvironmentVariable("DI_DEBUG") == "1")
            {
                Log.Write("perf frames/5s=" + frames + " animating=" + animating + " pending=" + (pendingIn != null ? pendingIn.Name : "-") + " view=" + (curView != null ? curView.Name : "-") + " sw=" + sw.Value.ToString("0.0") + "/" + sw.Target + " sh=" + sh.Value.ToString("0.0") + "/" + sh.Target + " ss=" + ss.Value.ToString("0.000") + "/" + ss.Target + " sy=" + sy.Value.ToString("0.0") + "/" + sy.Target + " spg=" + spg.Value.ToString("0.00") + "/" + spg.Target + " settings=" + (settingsWin != null));
                frames = 0;
            }
            if (curView == vIdle) UpdateIdle();
            if (!playing && hasMedia && pausedAt != DateTime.MinValue && (DateTime.Now - pausedAt).TotalSeconds >= 60 && (curView == vMusic || curView == vMusicTimer) && cur == null && !expanded)
            { pausedAt = DateTime.MinValue; ShowBase(); }
            PagesClock();
        }

        void UpdateBattery()
        {
            var ps = WF.SystemInformation.PowerStatus;
            battNone = (ps.BatteryChargeStatus & WF.BatteryChargeStatus.NoSystemBattery) != 0 || ps.BatteryLifePercent > 1.0f;
            battPct = (int)Math.Round(ps.BatteryLifePercent * 100);
            PagesBattery(battNone, battPct, ps.PowerLineStatus == WF.PowerLineStatus.Online);
        }

        void OnPowerMode(object s, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.StatusChange) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateBattery();
                bool ac = WF.SystemInformation.PowerStatus.PowerLineStatus == WF.PowerLineStatus.Online;
                if (ac == lastAc || battNone) { CheckBattery(); return; }
                lastAc = ac;
                if (ac) { lowNotified = critNotified = false; }
                if (!S.EvCharging) return;
                if (ac) Post(new IslandEvent { Kind = "charging", Key = "power", Priority = Pri.Charging, Duration = 3200 });
                else SystemEvent("", battPct <= S.LowBattery ? (Brush)Theme.Red : Theme.Hover, "On battery", battPct + "% left", Pri.Charging, 3000, "power", null);
            }));
        }

        void CheckBattery()
        {
            UpdateBattery();
            if (battNone || lastAc || !S.EvLowBattery) return;
            if (battPct <= S.CriticalBattery && !critNotified)
            {
                critNotified = lowNotified = true;
                SystemEvent("", Theme.Red, "Battery critical · " + battPct + "%", "Plug in your charger now", Pri.Urgent, 0, "battery", () => Dismiss());
            }
            else if (battPct <= S.LowBattery && !lowNotified)
            {
                lowNotified = true;
                SystemEvent("", Theme.Orange, "Battery low · " + battPct + "%", "Consider plugging in", Pri.System, S.SystemMs, "battery", null);
            }
        }

        // ---- display scale changed while running: Windows then stretches the island as a bitmap and
        // its coordinates no longer line up. Keep a notice up until the user restarts (click or Enter).
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int i);
        double startScaleRatio = -1; bool scaleNotice;

        // physical desktop width / the width this process sees: 1 unless Windows is virtualising us
        static double ScaleRatio()
        {
            var dc = GetDC(IntPtr.Zero);
            try { int phys = GetDeviceCaps(dc, 118), seen = GetDeviceCaps(dc, 8); return seen > 0 ? (double)phys / seen : 1; }
            finally { ReleaseDC(IntPtr.Zero, dc); }
        }

        void CheckScaleChange()
        {
            double r;
            try { r = ScaleRatio(); } catch { return; }
            if (startScaleRatio < 0) { startScaleRatio = r; return; }
            bool changed = Math.Abs(r - startScaleRatio) > 0.02;
            if (changed == scaleNotice) return;
            scaleNotice = changed;
            Log.Write("display scale ratio " + startScaleRatio.ToString("0.###", K.EN) + " → " + r.ToString("0.###", K.EN));
            if (changed)
            {
                if (expanded) Collapse();
                SystemEvent("\uE7F4", Theme.Accent, "Display scale changed", "Press Enter or click here to restart the island", Pri.Urgent, 0, "dpi", Restart);
                ActivateIsland();   // so Enter reaches us
            }
            else if (cur != null && cur.Key == "dpi") { ReleaseFocus(); Dismiss(); }   // scale set back: nothing to fix
        }

        void OnDisplayChanged(object s, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CheckScaleChange();
                try { dpi = HwndSource.FromHwnd(hwnd).CompositionTarget.TransformToDevice.M11; } catch { }
                SetWin(winW, winH);
                if (S.EvDisplay) SystemEvent("", Theme.Accent, "Displays changed", WF.Screen.AllScreens.Length + " display" + (WF.Screen.AllScreens.Length > 1 ? "s" : "") + " connected", Pri.System, S.SystemMs, "display", null);
            }));
        }

        void OnClipboard()
        {
            if (!S.EvClipboard || DateTime.Now < ignoreClipUntil) return;
            string sub;
            try
            {
                if (Clipboard.ContainsText())
                {
                    sub = Regex.Replace(Clipboard.GetText(), @"\s+", " ").Trim();
                    if (sub.Length == 0) return;
                    if (sub.Length > 48) sub = sub.Substring(0, 48) + "…";
                }
                else if (Clipboard.ContainsFileDropList()) { int n = Clipboard.GetFileDropList().Count; sub = n + " file" + (n > 1 ? "s" : ""); }
                else if (Clipboard.ContainsImage()) sub = "Image";
                else return;
            }
            catch { return; }
            Post(new IslandEvent { Kind = "system", Glyph = "", Color = Theme.Accent, Title = "Copied", Sub = sub, Priority = Pri.Notification, Duration = 1800, Key = "clip", Queueable = false });
        }

        public void CopyText(string t)
        {
            try { ignoreClipUntil = DateTime.Now.AddSeconds(1); Clipboard.SetText(t); } catch { }
        }

        // ---- visibility: Always · Smart (tucks away when the active window sits under it) · Auto-hide
        Native.WinEventProc winEventProc; readonly List<IntPtr> winHooks = new List<IntPtr>(); DispatcherTimer smartDebounce;
        DateTime nearSince = DateTime.MinValue;

        void InstallWindowHooks()
        {
            smartDebounce = Timer(120, delegate { smartDebounce.Stop(); CheckFullscreen(); });
            winEventProc = (h, ev, w, obj, child, th, time) =>
            {
                if (ev == 0x800B && (obj != 0 || w != Native.GetForegroundWindow())) return;
                if (ev == 0x0003) HookForegroundMoves(w);
                smartDebounce.Stop(); smartDebounce.Start();
            };
            foreach (var r in new[] { new uint[] { 0x0003, 0x0003 }, new uint[] { 0x000B, 0x000B }, new uint[] { 0x0016, 0x0017 } })
                winHooks.Add(Native.SetWinEventHook(r[0], r[1], IntPtr.Zero, winEventProc, 0, 0, 0x0002));
            HookForegroundMoves(Native.GetForegroundWindow());
        }

        // Window moves (maximize/restore/snap) are watched only for the active window's process,
        // so mouse-cursor location events from the rest of the system never reach us.
        IntPtr moveHook; uint moveHookPid;
        void HookForegroundMoves(IntPtr fg)
        {
            uint pid = 0;
            if (fg != IntPtr.Zero) Native.GetWindowThreadProcessId(fg, out pid);
            if (pid == moveHookPid && moveHook != IntPtr.Zero) return;
            if (moveHook != IntPtr.Zero) { Native.UnhookWinEvent(moveHook); moveHook = IntPtr.Zero; }
            moveHookPid = pid;
            if (pid == 0 || pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return;
            moveHook = Native.SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, winEventProc, pid, 0, 0x0002);
        }

        bool IsAppWindow(IntPtr fg)
        {
            if (fg == IntPtr.Zero || fg == hwnd || fg == Native.GetShellWindow() || Native.IsIconic(fg)) return false;
            if (settingsWin != null && fg == new WindowInteropHelper(settingsWin).Handle) return false;
            var sb = new StringBuilder(64); Native.GetClassName(fg, sb, 64);
            string c = sb.ToString();
            if (c == "WorkerW" || c == "Progman" || c == "Shell_TrayWnd" || c == "XamlExplorerHostIslandWindow" || c == "Windows.UI.Core.CoreWindow") return false;
            int cloaked; return Native.DwmGetWindowAttribute(fg, 14, out cloaked, 4) != 0 || cloaked == 0;
        }

        Native.RECT IslandZone()
        {
            var mon = WF.Screen.PrimaryScreen.Bounds;
            double cx = mon.Left + mon.Width / 2.0 + S.OffsetX * dpi;
            double half = (Math.Max(IdleW, ActW) / 2 + 16) * UiScale * dpi;
            double bottom = ((S.Style == 1 ? 0 : 6) + S.OffsetY + PH + 6) * UiScale * dpi;
            return new Native.RECT { L = (int)(cx - half), T = mon.Top, R = (int)(cx + half), B = mon.Top + (int)bottom };
        }

        // Is any real app window (visible, not minimized/cloaked/tool/overlay) under the island?
        bool AnyWindowUnderIsland()
        {
            var z = IslandZone();
            bool hit = false;
            IntPtr myMon = Native.MonitorFromWindow(hwnd, 2);
            IntPtr settingsH = settingsWin != null ? new WindowInteropHelper(settingsWin).Handle : IntPtr.Zero;
            Native.EnumWindows((h, l) =>
            {
                // cheapest checks first; DWM queries only for windows that touch the zone
                if (h == hwnd || h == settingsH || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                Native.RECT r;
                if (!Native.GetWindowRect(h, out r) || r.R - r.L < 40 || r.B - r.T < 40) return true;
                if (!(r.L - 8 < z.R && r.R + 8 > z.L && r.T - 8 < z.B && r.B + 8 > z.T)) return true;
                int ex = Native.GetWindowLong(h, -20);
                if ((ex & (0x80 | 0x08000000 | 0x20)) != 0 || Native.GetWindowTextLength(h) == 0) return true;   // tool, no-activate, overlays
                var sb = new StringBuilder(64); Native.GetClassName(h, sb, 64);
                string c = sb.ToString();
                if (c == "WorkerW" || c == "Progman" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c == "XamlExplorerHostIslandWindow" || c == "Windows.UI.Core.CoreWindow") return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                Native.RECT f;
                if (Native.DwmGetWindowAttribute(h, 9, out f, Marshal.SizeOf(typeof(Native.RECT))) == 0) r = f;
                if (r.L < z.R && r.R > z.L && r.T < z.B && r.B > z.T) { hit = true; return false; }
                return true;
            }, IntPtr.Zero);
            return hit;
        }

        void CheckFullscreen()
        {
            bool fs = false, overlap = false;
            IntPtr fg = Native.GetForegroundWindow();
            if (IsAppWindow(fg))
            {
                Native.RECT r;
                IntPtr mon = Native.MonitorFromWindow(fg, 2);
                if (mon == Native.MonitorFromWindow(hwnd, 2) && (Native.DwmGetWindowAttribute(fg, 9, out r, Marshal.SizeOf(typeof(Native.RECT))) == 0 || Native.GetWindowRect(fg, out r)))
                {
                    var mi = new Native.MONITORINFO(); mi.cbSize = Marshal.SizeOf(mi);
                    if (Native.GetMonitorInfo(mon, ref mi))
                        fs = r.L <= mi.rcMonitor.L && r.T <= mi.rcMonitor.T && r.R >= mi.rcMonitor.R && r.B >= mi.rcMonitor.B;
                    var z = IslandZone();
                    overlap = r.L < z.R && r.R > z.L && r.T < z.B && r.B > z.T;
                }
            }
            if (forceFs >= 0) fs = forceFs == 1;   // --ipc fs on|off|auto (testing)
            if (S.Visibility == 1 && !fs) overlap = AnyWindowUnderIsland();
            bool wantMini = (fs && S.FullscreenNotch) || S.Visibility == 2 || (S.Visibility == 1 && overlap);
            if (Environment.GetEnvironmentVariable("DI_DEBUG") == "1")
            {
                var sbc = new StringBuilder(64); Native.GetClassName(fg, sbc, 64); var z = IslandZone();
                Native.RECT rr; Native.GetWindowRect(fg, out rr);
                Log.Write("smart fg=" + sbc + " app=" + IsAppWindow(fg) + " rect=" + rr.L + "," + rr.T + "," + rr.R + "," + rr.B + " zone=" + z.L + "," + z.T + "," + z.R + "," + z.B + " overlap=" + overlap + " fs=" + fs + " vis=" + S.Visibility + " mini=" + mini + "→" + wantMini);
            }
            fsNow = fs;
            if (wantMini != mini)
            {
                mini = wantMini; miniReveal = mini && CursorInIsland(10);
                if (mini) { if (expanded && !pinned) Collapse(); miniPoll.Start(); } else { miniPoll.Stop(); miniPeek = false; }
                nearSince = DateTime.MinValue;
                if (cur == null && !expanded) ShowBase();
            }
            SetHidden(fs && !S.FullscreenNotch);
            UpdateRegion(false);
            if (!fs) Native.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        // Fullscreen with the mini notch off: fade away (and back) instead of blinking out.
        void SetHidden(bool h)
        {
            if (h == hidden && shapeGrid.Visibility == (h ? Visibility.Hidden : Visibility.Visible)) return;
            hidden = h;
            if (h)
            {
                var a = K.Anim(0, 220, K.OutCubic);
                a.Completed += delegate { if (hidden) { shapeGrid.Visibility = Visibility.Hidden; UpdateRegion(true); } };
                shapeGrid.BeginAnimation(OpacityProperty, a);
            }
            else
            {
                shapeGrid.Visibility = Visibility.Visible;
                shapeGrid.BeginAnimation(OpacityProperty, K.Anim(curView == vEmpty ? 0 : 1, 320, K.OutCubic));
                if (!expanded && cur == null) ShowBase();
            }
        }

        void MiniPoll()
        {
            if (DateTime.Now < peekHoldUntil) return;
            if (!mini || expanded || cur != null) { SetMiniPeek(false); return; }
            var c = Native.CursorPx();
            var mon = WF.Screen.PrimaryScreen.Bounds;
            double cx = mon.Left + mon.Width / 2.0 + S.OffsetX * dpi;
            double dx = Math.Abs(c.X - cx), dy = c.Y - mon.Top, s = UiScale * dpi;
            // Approaching the top edge near the tab widens it a little (it's there, hover me);
            // only the tab itself (plus a hair) reveals the island, after a short dwell, so tabs
            // and title bars under it stay usable.
            SetMiniPeek(!miniReveal && dx < 150 * s && dy < 44 * dpi);
            bool near = dx < (miniPeek ? 72 : 60) * s && dy < (miniPeek ? 14 : 10) * dpi;
            if (near) { if (nearSince == DateTime.MinValue) nearSince = DateTime.Now; }
            else nearSince = DateTime.MinValue;
            if (near && !miniReveal && (DateTime.Now - nearSince).TotalMilliseconds > 160) { miniReveal = true; miniPeek = false; ShowBase(); }
            else if (!near && miniReveal && !CursorInIsland(10)) { miniReveal = false; ShowBase(); }
        }

        void SetMiniPeek(bool on)
        {
            if (on == miniPeek) return;
            miniPeek = on;
            UpdateMini();
        }

        // Tab size and handle look: timer → progress bar, music → art-coloured handle that breathes,
        // otherwise a faint grabber. Pings (quiet events) override the colour for a moment.
        void UpdateMini()
        {
            if (miniHandle == null) return;
            double w = miniPeek ? MiniPeekW : MiniW, h = miniPeek ? MiniPeekH : MiniH;
            if (Math.Abs(vMini.W - w) > 0.1 || Math.Abs(vMini.H - h) > 0.1) SetViewSize(vMini, w, h);
            bool musicOn = ses != null && hasMedia && playing;
            double hw = tRunning ? (miniPeek ? 52 : 38) : (miniPeek ? 34 : 24);
            miniHandle.BeginAnimation(WidthProperty, K.Anim(hw, 260, K.OutCubic));
            miniHandle.Height = miniPeek ? 4 : 3;
            miniHandle.CornerRadius = new CornerRadius(miniHandle.Height / 2);
            if (DateTime.Now < miniPingUntil) return;
            var p = Theme.Current;
            if (tRunning)
            {
                miniHandle.Background = K.Solid(Theme.A(p.Text, 0.18));
                miniFill.Visibility = Visibility.Visible;
                double prog = 1 - Remaining().TotalSeconds / Math.Max(1, tTotal.TotalSeconds);
                miniFill.Width = Math.Max(0, hw * Math.Max(0, Math.Min(1, prog)));
            }
            else
            {
                miniFill.Visibility = Visibility.Collapsed;
                miniHandle.Background = K.Solid(musicOn ? artColor : Theme.A(p.Text, miniPeek ? 0.55 : 0.3));
            }
        }

        void UpdateMiniBreath()
        {
            if (miniHandle == null) return;
            bool want = curView == vMini && ses != null && hasMedia && playing && !tRunning && K.AnimScale > 0 && DateTime.Now >= miniPingUntil;
            if (want == miniBreathing) return;
            miniBreathing = want;
            if (want)
            {
                var a = new DoubleAnimation(1, 0.45, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                Timeline.SetDesiredFrameRate(a, 24);
                miniHandle.BeginAnimation(OpacityProperty, a);
            }
            else miniHandle.BeginAnimation(OpacityProperty, K.Anim(1, 200, K.OutCubic));
        }

        // A quiet "something happened" on the mini tab: the handle flashes the event colour,
        // stretches out and settles back.
        void MiniPing(Brush color)
        {
            if (miniHandle == null) return;
            miniPingUntil = DateTime.Now.AddMilliseconds(1500);
            miniBreathing = false;
            miniHandle.BeginAnimation(OpacityProperty, null); miniHandle.Opacity = 1;
            miniFill.Visibility = Visibility.Collapsed;
            miniHandle.Background = color;
            if (K.AnimScale > 0)
            {
                var sx = new DoubleAnimationUsingKeyFrames();
                sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(K.Dur(220))), K.OutCubic));
                sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(K.Dur(900)))));
                sx.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(K.Dur(1400))), K.OutCubic));
                miniHandleScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
                ss.Vel += 0.5; Kick();
            }
            var t = Timer(1520, null);
            t.Tick += delegate { t.Stop(); if (DateTime.Now >= miniPingUntil) { UpdateMini(); UpdateMiniBreath(); } };
            t.Start();
        }

        // ================================================================ timer
        public void TimerStart(TimeSpan d)
        {
            if (d.TotalMinutes >= 1) { S.TimerLastMinutes = (int)Math.Round(d.TotalMinutes); Store.Save(); }
            tTotal = d; tEnd = DateTime.Now + d; tRunning = true; tPaused = false; lastTimerSec = -1;
            tTick.Start();
            TimerTick();
            PagesTimer();
            if (!expanded && cur == null) ShowBase();
        }

        TimeSpan Remaining() { return tPaused ? tRemain : tEnd - DateTime.Now; }

        public void TimerPauseToggle()
        {
            if (!tRunning) return;
            if (tPaused) { tEnd = DateTime.Now + tRemain; tPaused = false; }
            else { tRemain = tEnd - DateTime.Now; tPaused = true; }
            timerText.Foreground = mtText.Foreground = tPaused ? Theme.Text2 : Theme.Orange;
            PagesTimer();
        }

        public void TimerAdd(int min)
        {
            if (!tRunning) return;
            var d = TimeSpan.FromMinutes(min);
            if (tPaused) tRemain += d; else tEnd += d;
            tTotal += d; lastTimerSec = -1;
            TimerTick();
        }

        public void TimerCancel()
        {
            tRunning = false; tPaused = false; tTick.Stop();
            timerText.Foreground = mtText.Foreground = Theme.Orange;
            PagesTimer();
            if (!expanded && cur == null) ShowBase();
        }

        void TimerTick()
        {
            if (!tRunning) return;
            var rem = Remaining();
            if (rem.TotalSeconds <= 0) { TimerDone(); return; }
            double prog = 1 - rem.TotalSeconds / Math.Max(1, tTotal.TotalSeconds);
            timerRing.Progress = prog; mtRing.Progress = prog;
            if (curView == vMini && miniFill != null) miniFill.Width = Math.Max(0, miniHandle.Width * prog);
            long sec = (long)Math.Ceiling(rem.TotalSeconds);
            if (sec != lastTimerSec)
            {
                lastTimerSec = sec;
                string txt = K.Fmt(sec);
                timerText.Text = txt; mtText.Text = txt;
            }
            PagesTimerTick(rem, prog);
        }

        void TimerDone()
        {
            var total = tTotal;
            TimerCancel();
            if (S.TimerSound) PlayAlarm();
            SystemEvent("", Theme.Orange, "Time's up!", K.Fmt(total.TotalSeconds) + " timer finished · click to dismiss", Pri.Urgent, 0, "timer-done", () => Dismiss());
            Shake();
        }

        void Shake()
        {
            if (K.AnimScale <= 0) return;
            var tt = new TranslateTransform();
            var tg = new TransformGroup(); tg.Children.Add(shapeScale); tg.Children.Add(tt);
            shapeGrid.RenderTransform = tg;
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(620), RepeatBehavior = new RepeatBehavior(2) };
            double[] xs = { 0, -10, 10, -8, 8, -5, 5, -2, 0 };
            for (int i = 0; i < xs.Length; i++) a.KeyFrames.Add(new EasingDoubleKeyFrame(xs[i], KeyTime.FromPercent(i / (double)(xs.Length - 1)), new SineEase()));
            tt.BeginAnimation(TranslateTransform.XProperty, a);
        }

        void PlayAlarm()
        {
            try
            {
                string f = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Media\Alarm01.wav");
                if (File.Exists(f)) { alarm = new SoundPlayer(f); alarm.Play(); } else SystemSounds.Exclamation.Play();
            }
            catch { }
        }
        void StopAlarm() { try { if (alarm != null) { alarm.Stop(); alarm = null; } } catch { } }

        // ================================================================ shelf drop
        void OnDragOver(object s, DragEventArgs e)
        {
            e.Handled = true;
            if (dragOut || !e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; return; }
            e.Effects = DragDropEffects.Copy;
            lastDragOver = DateTime.Now;
            if (expanded && expandMode == "pages" && CurrentPageKey() == "shelf") { ShelfDropGlow(true); return; }
            if (AiDropTarget(e)) return;
            if (expanded) Collapse();
            if (cur == null || cur.Kind != "drop") Post(new IslandEvent { Kind = "drop", Key = "drop", Priority = Pri.Urgent - 1, Duration = 0 });
            dragWatch.Start();
        }

        // Photos dragged onto the open AI chat become attachments instead of shelf items.
        bool AiDropTarget(DragEventArgs e)
        {
            if (!expanded || CurrentPageKey() != "ai") return false;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            return files != null && files.Any(AiService.IsImageFile);
        }

        void OnDrop(object s, DragEventArgs e)
        {
            e.Handled = true; dragWatch.Stop(); ShelfDropGlow(false);
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && AiDropTarget(e)) { AiAttachFiles(files); return; }
            if (cur != null && cur.Kind == "drop") { evTimer.Stop(); cur = null; }
            if (files == null) { ShowBase(); return; }
            var added = AddToShelf(files);
            Expand("shelf", true);
            ShelfAdded(added);
            // and the next time the island is opened it goes straight back to the shelf (once)
            if (added.Count > 0) shelfOnNextOpen = true;
        }

        List<string> AddToShelf(IEnumerable<string> files)
        {
            var added = new List<string>();
            foreach (var f in files)
            {
                if (S.ShelfItems.Contains(f, StringComparer.OrdinalIgnoreCase)) continue;
                if (S.ShelfItems.Count >= S.ShelfMax) break;
                S.ShelfItems.Add(f); added.Add(f);
            }
            Store.Save();
            return added;
        }

        // ================================================================ media
        async void InitMedia()
        {
            try
            {
                mgr = await RT.Run(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                propsH = (s, e) => Dispatcher.BeginInvoke(new Action(RefreshProps));
                playH = (s, e) => Dispatcher.BeginInvoke(new Action(RefreshPlayback));
                mgr.CurrentSessionChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() => HookSession(null)));
                mgr.SessionsChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() => { HookSession(null); PagesSessions(); }));
                HookSession(null);
            }
            catch (Exception ex) { Log.Write("smtc: " + ex.Message); }
        }

        public List<GlobalSystemMediaTransportControlsSession> Sessions()
        {
            try { return mgr == null ? new List<GlobalSystemMediaTransportControlsSession>() : mgr.GetSessions().ToList(); }
            catch { return new List<GlobalSystemMediaTransportControlsSession>(); }
        }

        // AUMID -> app display name from shell:AppsFolder (gives "Firefox" for hashed ids like
        // 308046B0AF4A39CB and proper names for PWAs, e.g. "YouTube Music"). Built once off the UI
        // thread; onReady re-labels the current source when it's done.
        static Dictionary<string, string> aumidNames; static bool aumidBuilding;
        public static Action AumidReady;
        static string AumidName(string aumid)
        {
            if (aumidNames == null)
            {
                if (!aumidBuilding)
                {
                    aumidBuilding = true;
                    var th = new Thread(() =>
                    {
                        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        try
                        {
                            var t = Type.GetTypeFromProgID("Shell.Application");
                            object sh = Activator.CreateInstance(t);
                            object f = t.InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { "shell:AppsFolder" });
                            object items = f.GetType().InvokeMember("Items", System.Reflection.BindingFlags.InvokeMethod, null, f, null);
                            foreach (object it in (System.Collections.IEnumerable)items)
                            {
                                try
                                {
                                    string p = it.GetType().InvokeMember("Path", System.Reflection.BindingFlags.GetProperty, null, it, null) as string;
                                    string n = it.GetType().InvokeMember("Name", System.Reflection.BindingFlags.GetProperty, null, it, null) as string;
                                    if (!string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(n) && !d.ContainsKey(p)) d[p] = n;
                                }
                                catch { }
                            }
                        }
                        catch (Exception ex) { Log.Write("aumid: " + ex.Message); }
                        aumidNames = d;
                        var cb = AumidReady; if (cb != null) cb();
                    }) { IsBackground = true };
                    th.SetApartmentState(ApartmentState.STA);
                    th.Start();
                }
                return null;
            }
            string name;
            return aumidNames.TryGetValue(aumid, out name) ? name : null;
        }

        public static string SourceName(GlobalSystemMediaTransportControlsSession s)
        {
            string id = "";
            try { id = s.SourceAppUserModelId ?? ""; } catch { }
            if (id.Length > 0) { string nice = AumidName(id); if (!string.IsNullOrEmpty(nice)) return nice; }
            if (id.Contains("!")) id = id.Substring(id.LastIndexOf('!') + 1);
            id = Regex.Replace(id, @"\.exe$", "", RegexOptions.IgnoreCase);
            if (id.Contains("_")) id = id.Substring(0, id.IndexOf('_'));
            if (id.Contains(".")) id = id.Substring(id.LastIndexOf('.') + 1);
            if (id.Length == 0 || Regex.IsMatch(id, "^[0-9A-Fa-f]{12,}$")) return "Media";
            if (id.Equals("chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
            if (id.Equals("msedge", StringComparison.OrdinalIgnoreCase)) return "Edge";
            return char.ToUpper(id[0]) + id.Substring(1);
        }

        bool userPickedSession;
        public void PickSession(GlobalSystemMediaTransportControlsSession s) { userPickedSession = true; HookSession(s); }

        void HookSession(GlobalSystemMediaTransportControlsSession pick)
        {
            GlobalSystemMediaTransportControlsSession n = pick;
            if (n == null)
            {
                try
                {
                    if (userPickedSession && ses != null && Sessions().Contains(ses)) n = ses;
                    else { userPickedSession = false; n = mgr.GetCurrentSession(); }
                }
                catch { }
            }
            if (n != null && n == ses) { RefreshPlayback(); return; }
            if (ses != null) { try { ses.MediaPropertiesChanged -= propsH; ses.PlaybackInfoChanged -= playH; } catch { } }
            ses = n;
            if (ses != null) { try { ses.MediaPropertiesChanged += propsH; ses.PlaybackInfoChanged += playH; } catch { } }
            mSource = ses != null ? SourceName(ses) : "";
            RefreshProps(); RefreshPlayback(); PagesSessions();
        }

        async void RefreshProps()
        {
            int v = ++propsVer; var s = ses;
            if (s == null)
            {
                hasMedia = false; art = null; lastTitle = null; mediaInit = true; mTitleStr = mArtistStr = "";
                ApplyMedia(Colors.White); return;
            }
            try
            {
                var p = await RT.Run(s.TryGetMediaPropertiesAsync());
                if (v != propsVer || p == null) return;
                string title = p.Title ?? "", artist = p.Artist ?? "";
                ImageSource img = null; Color acc = Colors.White;
                if (p.Thumbnail != null)
                {
                    try
                    {
                        var ras = await RT.Run(p.Thumbnail.OpenReadAsync());
                        if (v != propsVer) return;
                        uint size = (uint)Math.Min(ras.Size, 8u * 1024 * 1024);
                        var rd = new global::Windows.Storage.Streams.DataReader(ras.GetInputStreamAt(0));
                        uint got = await RT.Run<uint>(rd.LoadAsync(size));
                        var bytes = new byte[got]; rd.ReadBytes(bytes);
                        rd.Dispose(); ras.Dispose();
                        if (v != propsVer) return;
                        var bi = new BitmapImage();
                        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 200; bi.StreamSource = new MemoryStream(bytes); bi.EndInit(); bi.Freeze();
                        img = bi; acc = ArtAccent(bi);
                    }
                    catch { }
                }
                if (v != propsVer) return;
                bool changed = title.Length > 0 && title != lastTitle;
                hasMedia = title.Length > 0; lastTitle = title; art = img;
                mTitleStr = title; mArtistStr = artist;
                ApplyMedia(acc);
                if (changed && mediaInit && playing && S.EvTrack && !expanded)
                    Post(new IslandEvent { Kind = "track", Key = "track", Priority = Pri.Track, Duration = S.TrackMs, Title = title, Sub = artist, Image = img, Click = () => { Dismiss(); Expand("media", true); } });
                mediaInit = true;
                if (!expanded && cur == null) ShowBase();
            }
            catch { }
        }

        void ApplyMedia(Color acc)
        {
            artColor = acc;
            SetArt(musicArt, musicGlyph, art); SetArt(mtArt, mtGlyph, art);
            musicEq.SetColor(K.Solid(acc));
            PagesMedia();
            UpdateEq();
        }

        public static void SetArt(Border b, TextBlock glyph, ImageSource img)
        {
            if (img != null)
            {
                var ib = new ImageBrush(img) { Stretch = Stretch.UniformToFill }; ib.Freeze();
                b.Background = ib; glyph.Visibility = Visibility.Collapsed;
            }
            else { b.Background = Theme.Raised; glyph.Visibility = Visibility.Visible; }
        }

        void RefreshPlayback()
        {
            bool p = false;
            if (ses != null)
            {
                try
                {
                    var info = ses.GetPlaybackInfo();
                    p = info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                }
                catch { }
            }
            if (playing && !p) pausedAt = DateTime.Now;
            if (p) pausedAt = DateTime.MinValue;
            bool changed = p != playing;
            // a seeked-to position still standing in for the player's: count only the time spent playing
            if (changed && tlLocalPos >= 0) { if (playing) tlLocalPos += (DateTimeOffset.Now - tlLocalAt).TotalSeconds; tlLocalAt = DateTimeOffset.Now; }
            playing = p;
            PagesPlayback();
            if (changed && !expanded && cur == null) ShowBase();
            UpdateEq();
        }

        public async void Cmd(int k)
        {
            if (ses == null) { Native.Key(k == 0 ? (byte)0xB1 : k == 1 ? (byte)0xB3 : (byte)0xB0); return; }
            try
            {
                if (k == 1) await RT.Run(ses.TryTogglePlayPauseAsync());
                else if (k == 0) await RT.Run(ses.TrySkipPreviousAsync());
                else await RT.Run(ses.TrySkipNextAsync());
            }
            catch { }
        }

        public async void Seek(double frac)
        {
            if (ses == null) return;
            try
            {
                var tl = ses.GetTimelineProperties();
                frac = Math.Max(0, Math.Min(1, frac));
                // the reported timeline can be empty right after an earlier seek: use the duration we know
                double end, pos;
                if (!MediaTimeline(out end, out pos)) return;
                MediaSeeked(frac, end);
                UpdateMediaTimeline(false);
                await RT.Run(ses.TryChangePlaybackPositionAsync(tl.StartTime.Ticks + (long)(TimeSpan.FromSeconds(end).Ticks * frac)));
            }
            catch { }
        }

        static Color ArtAccent(BitmapSource src)
        {
            try
            {
                var sc = new TransformedBitmap(src, new ScaleTransform(12.0 / src.PixelWidth, 12.0 / src.PixelHeight));
                var fc = new FormatConvertedBitmap(sc, PixelFormats.Bgra32, null, 0);
                int w = fc.PixelWidth, h = fc.PixelHeight; var px = new byte[w * h * 4];
                fc.CopyPixels(px, w * 4, 0);
                double R = 0, G = 0, Bl = 0, W = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    double b = px[i], g = px[i + 1], r = px[i + 2];
                    double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                    double sat = mx < 1 ? 0 : (mx - mn) / mx;
                    double wt = sat * sat * (mx / 255) + 0.01;
                    R += r * wt; G += g * wt; Bl += b * wt; W += wt;
                }
                R /= W; G /= W; Bl /= W;
                double m = Math.Max(R, Math.Max(G, Bl));
                if (m < 1) return Colors.White;
                double kk = 240 / m;
                Func<double, byte> c = x => (byte)Math.Max(70, Math.Min(255, x * kk));
                return Color.FromRgb(c(R), c(G), c(Bl));
            }
            catch { return Colors.White; }
        }
    }

    static class Log
    {
        static readonly bool dbg = Environment.GetEnvironmentVariable("DI_DEBUG") == "1";
        public static void T(string s) { if (dbg) Write(DateTime.Now.ToString("HH:mm:ss.fff") + " T " + s); }
        public static void Error(Exception ex) { Write("ERROR " + ex); }
        public static void Write(string s)
        {
            try
            {
                Directory.CreateDirectory(Store.Dir);
                string f = System.IO.Path.Combine(Store.Dir, "error.log");
                if (File.Exists(f) && new FileInfo(f).Length > 512 * 1024) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + s + "\r\n");
            }
            catch { }
        }
    }

    // Command line → running instance (WM_COPYDATA). e.g. DynamicIsland.exe --ipc page ai
    static class Ipc
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref CopyData cds);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
        [StructLayout(LayoutKind.Sequential)] public struct CopyData { public IntPtr dwData; public int cbData; public IntPtr lpData; }

        public static bool Send(string[] parts)
        {
            IntPtr h = FindWindow(null, "Dynamic Island");
            if (h == IntPtr.Zero) return false;
            uint pid; GetWindowThreadProcessId(h, out pid); AllowSetForegroundWindow(pid);
            var bytes = Encoding.UTF8.GetBytes(string.Join("\u001f", parts));
            var cds = new CopyData { dwData = new IntPtr(0x4449), cbData = bytes.Length, lpData = Marshal.AllocHGlobal(bytes.Length) };
            try { Marshal.Copy(bytes, 0, cds.lpData, bytes.Length); SendMessage(h, 0x4A, IntPtr.Zero, ref cds); }
            finally { Marshal.FreeHGlobal(cds.lpData); }
            return true;
        }

        public static string[] Read(IntPtr l)
        {
            var cds = (CopyData)Marshal.PtrToStructure(l, typeof(CopyData));
            if (cds.dwData != new IntPtr(0x4449) || cds.cbData <= 0) return null;
            var bytes = new byte[cds.cbData];
            Marshal.Copy(cds.lpData, bytes, 0, cds.cbData);
            return Encoding.UTF8.GetString(bytes).Split('\u001f');
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--ipc") { Ipc.Send(args.Skip(1).ToArray()); return; }
            if (Array.IndexOf(args, "--uninstall") >= 0) { Installer.Uninstall(); return; }
            bool restart = Array.IndexOf(args, "--restart") >= 0;
            if (restart) Thread.Sleep(800);
            bool created;
            using (var m = new Mutex(true, "DynIsland_SingleInstance", out created))
            {
                if (!created) return;
                Updater.CleanupOld();
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) => { Log.Error(e.Exception); e.Handled = true; };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("FATAL " + e.ExceptionObject);
                Store.Load();
                K.AnimScale = Store.AnimScale();
                K.Font = K.ResolveFont(Store.S.Font, Store.S.FontCustom);
                bool silent = Array.IndexOf(args, "--silent") >= 0;
                var w = new IslandWindow(silent);
                w.Show();
                app.Run();
            }
        }
    }
}
