// Expanded Island: page carousel (Home, Media, AI, Timer, To-do, Shelf, System),
// Launcher and Wallpaper views.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Media.Control;

namespace DynIsland
{
    partial class IslandWindow
    {
        View vExp, vLaunch, vWall;
        static readonly string[] AllPages = { "home", "media", "ai", "timer", "todo", "shelf", "system", "games" };
        static readonly Dictionary<string, string> PageLabels = new Dictionary<string, string>
            { { "home", "Home" }, { "media", "Media" }, { "ai", "AI" }, { "timer", "Timer" }, { "todo", "To-do" }, { "shelf", "Shelf" }, { "system", "System" }, { "games", "Games" } };
        readonly List<string> pageKeys = new List<string>();
        readonly Dictionary<string, Grid> pageRoots = new Dictionary<string, Grid>();
        Grid pagesHost; StackPanel dots; Border dotsHost; bool dotsHover;
        bool pageDrag, dragArmed; Point dragStart; double dragV0, lastDragX, velX; DateTime lastDragT;
        double hwAcc; DateTime hwLast;
        bool aiBig;
        const double DotsH = 28;

        // ================================================================ build
        void BuildExpanded()
        {
            vExp = NewView("expanded", 420, 220, true);
            vExp.R = -1;
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(UIElement.OpacityProperty, typeof(Grid)).AddValueChanged(vExp.Root, delegate { UpdateAmbient(); });
            pagesHost = new Grid { ClipToBounds = false };
            var canvas = new Canvas();
            pagesHost.Children.Add(canvas);
            foreach (var k in AllPages)
            {
                Grid g;
                switch (k)
                {
                    case "home": g = BuildHome(); break;
                    case "media": g = BuildMediaPage(); break;
                    case "ai": g = BuildAiPage(); break;
                    case "timer": g = BuildTimerPage(); break;
                    case "todo": g = BuildTodoPage(); break;
                    case "shelf": g = BuildShelfPage(); break;
                    case "games": g = BuildGamesPage(); break;
                    default: g = BuildSystemPage(); break;
                }
                var outer = new Grid { Background = Brushes.Transparent, Visibility = Visibility.Collapsed, ClipToBounds = true };
                outer.Children.Add(g);
                pageRoots[k] = outer; canvas.Children.Add(outer);
            }
            vExp.Root.Children.Add(pagesHost);

            BuildDotsHost();
            vExp.Root.Children.Add(dotsHost); vExp.Root.Children.Add(dotTipLayer);

            // drag across pages
            vExp.Root.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (IsInteractive(e.OriginalSource as DependencyObject)) return;
                dragArmed = true; dragStart = e.GetPosition(vExp.Root); dragV0 = spg.Value; lastDragX = dragStart.X; lastDragT = DateTime.Now; velX = 0;
            };
            vExp.Root.PreviewMouseMove += (s, e) =>
            {
                if (!dragArmed || e.LeftButton != MouseButtonState.Pressed) { dragArmed = false; return; }
                var p = e.GetPosition(vExp.Root);
                if (!pageDrag && Math.Abs(p.X - dragStart.X) > 8 && Math.Abs(p.X - dragStart.X) > Math.Abs(p.Y - dragStart.Y)) { pageDrag = true; vExp.Root.CaptureMouse(); }
                if (!pageDrag) return;
                double dt = Math.Max(0.001, (DateTime.Now - lastDragT).TotalSeconds);
                velX = (p.X - lastDragX) / dt; lastDragX = p.X; lastDragT = DateTime.Now;
                spg.Value = dragV0 - (p.X - dragStart.X) / vExp.W; spg.Vel = 0;
                Kick();
            };
            vExp.Root.PreviewMouseLeftButtonUp += (s, e) =>
            {
                dragArmed = false;
                if (!pageDrag) return;
                pageDrag = false; vExp.Root.ReleaseMouseCapture(); e.Handled = true;
                double target = Math.Round(spg.Value - velX / vExp.W * 0.18);
                if (Math.Abs(target - dragV0) > 1) target = dragV0 + Math.Sign(target - dragV0);
                GoIndex((int)target, true);
            };

            vLaunch = NewView("launcher", 560, 440, true);
            BuildLauncher();
            vWall = NewView("wallpaper", WallW, WallH, true);
            BuildWallpaper();
            ApplyPagesSettings();
        }

        static bool IsInteractive(DependencyObject d)
        {
            while (d != null)
            {
                if (d is TextBox || d is SliderBar || d is Toggle || d is PasswordBox) return true;
                var b = d as Border;
                if (b != null && b.Cursor == Cursors.Hand) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        void ApplyPagesSettings()
        {
            BuildHomeTiles();
            RebuildPageKeys();
            RebuildPresets();
            RefreshTodos(null);
            RefreshShelf(null);
            AiRefreshHeader();
        }

        // The carousel order from Settings (unknown/missing pages fall back to the default order);
        // hidden pages are left out unless includeHidden. Home can't be hidden.
        public static List<string> OrderedPages(bool includeHidden)
        {
            var s = Store.S;
            var order = new List<string>();
            if (s.PageOrder != null) foreach (var k in s.PageOrder) if (AllPages.Contains(k) && !order.Contains(k)) order.Add(k);
            foreach (var k in AllPages) if (!order.Contains(k)) order.Add(k);
            if (!includeHidden && s.HiddenPages != null) order.RemoveAll(k => k != "home" && s.HiddenPages.Contains(k));
            return order;
        }
        public static string PageLabel(string k) { string l; return PageLabels.TryGetValue(k, out l) ? l : k; }

        void RebuildPageKeys()
        {
            string curKey = pageKeys.Count > 0 ? CurrentPageKey() : null;
            pageKeys.Clear();
            // a hidden page opened on purpose (Home tile, hotkey) joins the carousel for as long as it's shown
            foreach (var k in OrderedPages(true))
            {
                bool hiddenPage = k != "home" && S.HiddenPages != null && S.HiddenPages.Contains(k);
                if (hiddenPage && k != transientPage) continue;
                if (k != "media" || ses != null) pageKeys.Add(k);
            }
            if (curKey != null && expanded && expandMode == "pages")
            {
                int i = pageKeys.IndexOf(curKey);
                if (i < 0) i = 0;
                spg.Snap(i);
            }
            RenderDots();
            if (expanded) RefreshExpandedSize();
        }

        int Mod(int a, int n) { return n == 0 ? 0 : ((a % n) + n) % n; }
        double ModD(double a, int n) { return n == 0 ? 0 : ((a % n) + n) % n; }

        string CurrentPageKey()
        {
            if (pageKeys.Count == 0) return "home";
            return pageKeys[Mod((int)Math.Round(spg.Target), pageKeys.Count)];
        }

        double PageW()
        {
            string k = CurrentPageKey();
            if (k == "ai" && aiBig) return 620;
            if (k == "games") return Math.Max(S.ExpandedWidth, 420);   // the boards need the room
            return S.ExpandedWidth;
        }
        double PageH(string k)
        {
            switch (k)
            {
                case "home": return 204;
                case "media": return 176;
                case "ai": return aiBig ? 520 : 370;
                case "timer": return 220;
                case "todo": return 258;
                case "shelf": return 204;
                case "system": return 236;
                case "games": return GamesH();
            }
            return 200;
        }

        void RefreshExpandedSize()
        {
            if (expandMode == "launcher") { SetViewSize(vLaunch, LaunchW, LauncherH()); return; }
            if (expandMode == "wallpaper") { SetViewSize(vWall, WallW, WallH); return; }
            double w = PageW(), h = PageH(CurrentPageKey()) + DotsH;
            foreach (var g in pageRoots.Values) g.Width = w;
            foreach (var kv in pageRoots) kv.Value.Height = PageH(kv.Key);
            SetViewSize(vExp, w, h);
            pagesHost.Width = w; pagesHost.Height = h;
        }

        bool shelfOnNextOpen;   // set by a drop onto the shelf, consumed by the next plain open

        void OpenPages(string page)
        {
            RebuildPageKeys();
            string key = page;
            if (key == null && !shelfOnNextOpen && S.OpenTo == 1) key = "home";
            else if (key == null && !shelfOnNextOpen && S.OpenTo == 2 && !string.IsNullOrEmpty(S.LastPage) && pageKeys.Contains(S.LastPage)) key = S.LastPage;
            if (key == null && shelfOnNextOpen)
            {
                // first open after files were dropped on the shelf: show them (one time only)
                shelfOnNextOpen = false;
                if (S.ShelfItems.Count > 0) key = "shelf";
            }
            if (key == null)
            {
                if (tRunning) key = "timer";
                else if (playing && ses != null) key = "media";
                else key = "home";
            }
            if (page != null) EnsurePage(key);   // a hidden page asked for by name (hotkey, Home) still opens
            int i = pageKeys.IndexOf(key);
            if (i < 0) i = 0;
            spg.Snap(i);
            RefreshExpandedSize();
            foreach (var kv in pageRoots) kv.Value.Visibility = kv.Key == pageKeys[i] ? Visibility.Visible : Visibility.Collapsed;
            RenderDots();
            OnPageShown(pageKeys[i], false);
        }

        void ClosePages()
        {
            if (pageKeys.Count > 0 && expandMode == "pages") S.LastPage = CurrentPageKey();
            if (transientPage != null) { transientPage = null; RebuildPageKeys(); }
            StopStats();
            AiCloseOverlay();
            AmbientDrift(false);
            GamesHidden();
        }

        void OnExpandedHidden() { }

        string transientPage;   // hidden page shown temporarily because it was opened directly

        // makes a hidden page reachable for now; returns false if the page can't be shown at all
        bool EnsurePage(string key)
        {
            if (pageKeys.Contains(key)) return true;
            if (!AllPages.Contains(key) || (key == "media" && ses == null)) return false;
            transientPage = key;
            RebuildPageKeys();
            return pageKeys.Contains(key);
        }

        void GoPage(string key) { if (!EnsurePage(key)) return; int i = pageKeys.IndexOf(key); if (i >= 0) GoIndex(i, true); }

        void GoIndex(int target, bool anim)
        {
            int n = pageKeys.Count;
            if (n == 0) return;
            if (!S.LoopPages) target = Math.Max(0, Math.Min(n - 1, target));
            else
            {
                // nearest representative of target around the current value
                double curV = spg.Value;
                int t = Mod(target, n);
                double best = t; double bd = double.MaxValue;
                for (int k = -2; k <= 2; k++) { double c = t + k * n; if (Math.Abs(c - curV) < bd) { bd = Math.Abs(c - curV); best = c; } }
                if (Math.Abs(target - curV) <= 1.01) best = target;
                target = (int)best;
            }
            string oldKey = CurrentPageKey();
            // a jump of more than one page is shown as ONE slide (old page out, new page in) instead
            // of racing past every page in between
            if (anim && Math.Abs(target - spg.Value) > 1.01) { jumpFrom = oldKey; jumpStart = spg.Value; jumpTarget = target; }
            else jumpFrom = null;
            spg.Target = target;
            if (!anim) { spg.Snap(target); jumpFrom = null; }
            string key = CurrentPageKey();
            RefreshExpandedSize();
            RenderDots();
            if (key != oldKey) { OnPageHidden(oldKey); OnPageShown(key, anim && K.AnimScale > 0); }
            Kick();
        }

        void ApplyPages()
        {
            if (curView != vExp && !(vExp.Root.Visibility == Visibility.Visible)) return;
            int n = pageKeys.Count;
            double w = vExp.W;
            if (pageDrag) jumpFrom = null;
            string jumpTo = null; double jf = 0, jdir = 1;
            if (jumpFrom != null && n > 0)
            {
                jumpTo = pageKeys[Mod(jumpTarget, n)];
                jdir = Math.Sign(jumpTarget - jumpStart);
                jf = (spg.Value - jumpStart) / (jumpTarget - jumpStart);   // 0 -> 1 (a spring overshoot may pass 1 a touch)
            }
            for (int i = 0; i < n; i++)
            {
                var g = pageRoots[pageKeys[i]];
                double pos = i - spg.Value;
                if (S.LoopPages && n > 1) { pos = ModD(pos + n / 2.0, n) - n / 2.0; }
                if (jumpTo != null)
                {
                    if (pageKeys[i] == jumpFrom) pos = -jdir * jf;
                    else if (pageKeys[i] == jumpTo) pos = jdir * (1 - jf);
                    else pos = 9;
                }
                bool vis = Math.Abs(pos) < 0.999;
                if (vis != (g.Visibility == Visibility.Visible)) g.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                if (vis)
                {
                    Canvas.SetLeft(g, Math.Round(pos * w));
                    // depth while sliding: the page on its way out/in dims and sinks a little
                    double ap = Math.Min(1, Math.Abs(pos));
                    var st = g.RenderTransform as ScaleTransform;
                    if (st == null) { st = new ScaleTransform(1, 1); g.RenderTransform = st; g.RenderTransformOrigin = new Point(0.5, 0.5); }
                    double sc = 1 - 0.05 * ap;
                    if (Math.Abs(st.ScaleX - sc) > 0.0005) { st.ScaleX = sc; st.ScaleY = sc; }
                    double op = 1 - 0.55 * ap;
                    if (Math.Abs(g.Opacity - op) > 0.002) g.Opacity = op;
                }
            }
            foreach (var kv in pageRoots) if (!pageKeys.Contains(kv.Key)) kv.Value.Visibility = Visibility.Collapsed;
        }

        string jumpFrom; double jumpStart; int jumpTarget;

        void PagesSettled()
        {
            // the temporary page leaves the carousel once you've moved on from it
            if (transientPage != null && CurrentPageKey() != transientPage) { transientPage = null; RebuildPageKeys(); }
            if (jumpFrom != null) { jumpFrom = null; ApplyPages(); }
            foreach (var g in pageRoots.Values) if (g.CacheMode != null) g.CacheMode = null;
            if (pagePending != null && expanded && pagePending == CurrentPageKey()) { var k = pagePending; pagePending = null; RunPageWork(k); }
            pagePending = null;
            int n = pageKeys.Count;
            if (n > 0 && S.LoopPages && (spg.Value < 0 || spg.Value > n - 1 + 1e-6))
            {
                double v = ModD(spg.Value, n);
                spg.Snap(Math.Round(v));
                ApplyPages();
            }
        }

        // Page indicator: one persistent item per page plus a sliding "active" indicator behind them.
        // Idle: dots, the current page as a small labelled pill. Hovered: a dock of page icons, the
        // current page as icon + label; the icon under the mouse lifts and shows its name in a bubble.
        // Item widths and the indicator's X/width animate with the same easing/duration, so the
        // indicator stays glued to the active item through every morph.
        class PageDot { public string Key; public Border B; public TextBlock T, G; public TranslateTransform GT; public SolidColorBrush Bg; public double LabelW, TextW; }
        static readonly Dictionary<string, string> PageIcons = new Dictionary<string, string>
            { { "home", "" }, { "media", "" }, { "ai", "" }, { "timer", "" }, { "todo", "" }, { "shelf", "" }, { "system", "" }, { "games", "" } };
        readonly List<PageDot> dotItems = new List<PageDot>();
        Grid dotsGrid; Border dotsTrack, dotInd, dotTip; SolidColorBrush dotsTrackBg, dotIndBg; TranslateTransform dotIndT, dotTipT; TextBlock dotTipText; Canvas dotTipLayer;
        string dotKeys; FontFamily dotFont; string dotHoverKey;
        const double DotGap = 3, DockItemW = 28, GlyphW = 12;

        void BuildDotsHost()
        {
            // the hover zone is the whole bottom strip of the island (no page content lives there)
            dotsHost = new Border { Height = DotsH, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, Padding = new Thickness(8, 0, 8, 4) };
            dotsTrackBg = new SolidColorBrush(Colors.Transparent);
            dotsTrack = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(2), Background = dotsTrackBg, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            dotsGrid = new Grid { Height = 20 };
            dotIndBg = new SolidColorBrush(Theme.Text.Color);
            dotIndT = new TranslateTransform();
            dotInd = new Border { Width = 16, Height = 6, CornerRadius = new CornerRadius(10), Background = dotIndBg, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, RenderTransform = dotIndT, IsHitTestVisible = false };
            dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            dotsGrid.Children.Add(dotInd); dotsGrid.Children.Add(dots);
            dotsTrack.Child = dotsGrid;
            dotsHost.Child = dotsTrack;
            // a short grace period on leave so brushing past the edge doesn't flicker it open/closed
            var leave = Timer(160, null);
            leave.Tick += delegate { leave.Stop(); if (dotsHost.IsMouseOver) return; dotsHover = false; dotHoverKey = null; DotTip(null); RenderDots(); };
            dotsHost.MouseEnter += delegate { leave.Stop(); if (dotsHover) return; dotsHover = true; RenderDots(); };
            dotsHost.MouseLeave += delegate { leave.Stop(); leave.Start(); };
            // wheel over the strip flips pages
            dotsHost.MouseWheel += (s, e) => { e.Handled = true; Pin(); GoIndex((int)Math.Round(spg.Target) + (e.Delta > 0 ? -1 : 1), true); };

            // name bubble for the hovered icon (a layer over the whole view, never hit-testable)
            dotTipLayer = new Canvas { IsHitTestVisible = false };
            dotTipText = K.TB("", 10.5, Theme.Text, FontWeights.SemiBold);
            dotTipT = new TranslateTransform();
            dotTip = new Border { CornerRadius = new CornerRadius(8), Background = Theme.Surface, BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), Padding = new Thickness(8, 2, 8, 3), Child = dotTipText, Opacity = 0, RenderTransform = dotTipT };
            dotTipLayer.Children.Add(dotTip);
        }

        void DotTip(PageDot d)
        {
            if (dotTip == null) return;
            if (d == null || d.Key == CurrentPageKey() || !dotsHover)
            {
                dotTip.BeginAnimation(OpacityProperty, K.Anim(0, 120, K.OutCubic));
                return;
            }
            dotTipText.Text = PageLabels[d.Key];
            dotTip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Point c;
            try { c = d.B.TranslatePoint(new Point(DockItemW / 2, 0), dotTipLayer); } catch { return; }
            Canvas.SetLeft(dotTip, Math.Round(c.X - dotTip.DesiredSize.Width / 2));
            Canvas.SetTop(dotTip, Math.Round(c.Y - dotTip.DesiredSize.Height - 7));
            dotTip.BeginAnimation(OpacityProperty, K.Anim(1, 140, K.OutCubic));
            dotTipT.BeginAnimation(TranslateTransform.YProperty, null); dotTipT.Y = 4;
            dotTipT.BeginAnimation(TranslateTransform.YProperty, K.Anim(0, 220, K.OutBack));
        }

        void BuildDots()
        {
            dotKeys = string.Join(",", pageKeys); dotFont = K.Font;
            dots.Children.Clear(); dotItems.Clear();
            var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            foreach (var k in pageKeys)
            {
                var d = new PageDot { Key = k };
                d.T = K.TB(PageLabels[k], 10.5, Theme.Text2, FontWeights.SemiBold);
                d.T.FontFamily = K.Font; d.T.TextTrimming = TextTrimming.None; d.T.Opacity = 0;
                d.T.Measure(inf);
                d.TextW = Math.Ceiling(d.T.DesiredSize.Width);
                d.LabelW = d.TextW + 18;
                string icon; PageIcons.TryGetValue(k, out icon);
                d.G = K.Glyph(icon ?? "", 11, Theme.Text2); d.G.Visibility = Visibility.Collapsed; d.G.Width = GlyphW;
                d.GT = new TranslateTransform(); d.G.RenderTransform = d.GT;
                var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                content.Children.Add(d.G); content.Children.Add(d.T);
                d.Bg = new SolidColorBrush(Theme.Text3.Color);
                d.B = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(10), Background = d.Bg, Margin = new Thickness(DotGap, 0, DotGap, 0), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true, Cursor = Cursors.Hand, Child = content };
                var dd = d;
                d.B.MouseEnter += delegate { dotHoverKey = dd.Key; ColorDotLabels(); DotTip(dd); };
                d.B.MouseLeave += delegate { if (dotHoverKey == dd.Key) { dotHoverKey = null; DotTip(null); } ColorDotLabels(); };
                d.B.MouseLeftButtonDown += (s, e) => e.Handled = true;
                d.B.MouseLeftButtonUp += (s, e) => { e.Handled = true; Pin(); DotTip(null); GoPage(dd.Key); };
                dotItems.Add(d); dots.Children.Add(d.B);
            }
            LayoutDots(false);
        }

        void RenderDots()
        {
            if (dots == null) return;
            if (dotKeys != string.Join(",", pageKeys) || !ReferenceEquals(dotFont, K.Font)) BuildDots();
            LayoutDots(true);
        }

        void LayoutDots(bool anim)
        {
            bool dock = dotsHover;
            string cur = CurrentPageKey();
            int ms = 420; var ease = dock ? K.OutQuint : K.OutBack;
            double x = 0, ix = 0, iw = 16, ih = 6;
            // the dock must fit the island; if a narrow island can't hold it, scale the row down
            double scale = 1;
            if (dock && dotItems.Count > 0)
            {
                double avail = Math.Max(120, PageW() - 16 - 8);
                double total = dotItems.Sum(d => d.Key == cur ? GlyphW + 5 + d.TextW + 18 : DockItemW) + dotItems.Count * 2 * DotGap;
                if (total > avail) scale = avail / total;
            }
            var lt = dotsTrack.LayoutTransform as ScaleTransform;
            if (lt == null) { lt = new ScaleTransform(1, 1); dotsTrack.LayoutTransform = lt; }
            if (Math.Abs(lt.ScaleX - scale) > 0.001) { lt.ScaleX = scale; lt.ScaleY = scale; }
            foreach (var d in dotItems)
            {
                bool on = d.Key == cur;
                double w = dock ? (on ? GlyphW + 5 + d.TextW + 18 : DockItemW) : (on ? d.LabelW - 4 : 6);
                double h = dock ? 20 : (on ? 16 : 6);
                bool showLabel = on;   // idle pill and dock both name only the current page
                bool showGlyph = dock;
                Color bg = DotBg(d, dock, on);
                x += DotGap;
                if (on) { ix = x; iw = w; ih = h; }
                x += w + DotGap;
                d.G.Visibility = showGlyph ? Visibility.Visible : Visibility.Collapsed;
                d.T.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
                d.T.Margin = new Thickness(showGlyph && showLabel ? 5 : 0, 0, 0, 0);
                if (anim)
                {
                    d.B.BeginAnimation(WidthProperty, K.Anim(w, ms, ease));
                    d.B.BeginAnimation(HeightProperty, K.Anim(h, ms, K.OutQuint));
                    d.Bg.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(bg, 260));
                    d.T.BeginAnimation(OpacityProperty, K.Anim(showLabel ? 1 : 0, showLabel ? 260 : 110, K.OutCubic));
                    if (showGlyph) { d.G.Opacity = 0; d.G.BeginAnimation(OpacityProperty, K.Anim(1, 260, K.OutCubic)); }
                }
                else
                {
                    d.B.BeginAnimation(WidthProperty, null); d.B.BeginAnimation(HeightProperty, null); d.Bg.BeginAnimation(SolidColorBrush.ColorProperty, null); d.T.BeginAnimation(OpacityProperty, null); d.G.BeginAnimation(OpacityProperty, null);
                    d.B.Width = w; d.B.Height = h; d.Bg.Color = bg; d.T.Opacity = showLabel ? 1 : 0; d.G.Opacity = 1;
                }
            }
            ColorDotLabels();
            Color ind = dock ? Theme.Current.Hover : Theme.A(Theme.Current.Text, 0.16);
            Color track = dock ? Theme.A(Theme.Current.Text, 0.06) : Theme.A(Theme.Current.Text, 0);
            if (anim)
            {
                dotIndT.BeginAnimation(TranslateTransform.XProperty, K.Anim(ix, ms, ease));
                dotInd.BeginAnimation(WidthProperty, K.Anim(iw, ms, ease));
                dotInd.BeginAnimation(HeightProperty, K.Anim(ih, ms, K.OutQuint));
                dotIndBg.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(ind, 260));
                dotsTrackBg.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(track, 260));
            }
            else
            {
                dotIndT.BeginAnimation(TranslateTransform.XProperty, null); dotInd.BeginAnimation(WidthProperty, null); dotInd.BeginAnimation(HeightProperty, null);
                dotIndBg.BeginAnimation(SolidColorBrush.ColorProperty, null); dotsTrackBg.BeginAnimation(SolidColorBrush.ColorProperty, null);
                dotIndT.X = ix; dotInd.Width = iw; dotInd.Height = ih; dotIndBg.Color = ind; dotsTrackBg.Color = track;
            }
        }

        // idle dots are Text3; in the dock items are clear, except a soft highlight under the hovered one
        Color DotBg(PageDot d, bool dock, bool on)
        {
            if (!dock) return on ? Theme.A(Theme.Text3.Color, 0) : Theme.Text3.Color;
            return !on && d.Key == dotHoverKey ? Theme.A(Theme.Current.Text, 0.08) : Theme.A(Theme.Current.Text, 0);
        }

        void ColorDotLabels()
        {
            string cur = CurrentPageKey();
            foreach (var d in dotItems)
            {
                bool on = d.Key == cur, hot = d.Key == dotHoverKey;
                d.T.Foreground = on ? Theme.Text : Theme.Text2;
                d.G.Foreground = on || hot ? Theme.Text : Theme.Text2;
                // the hovered icon lifts a little
                d.GT.BeginAnimation(TranslateTransform.YProperty, K.Anim(dotsHover && hot && !on ? -2 : 0, hot ? 260 : 200, hot ? K.OutBack : K.OutCubic));
                if (dotsHover) d.Bg.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(DotBg(d, true, on), 140));
            }
        }

        static ColorAnimation CAnim(Color to, int ms)
        {
            return new ColorAnimation(to, new Duration(TimeSpan.FromMilliseconds(Math.Max(1, K.Dur(ms))))) { EasingFunction = K.OutCubic };
        }

        void OnHWheel(int delta)
        {
            if (!expanded)
            {
                if (hover && Math.Abs(delta) > 0) Expand(null, false);
                return;
            }
            if (expandMode != "pages") return;
            if ((DateTime.Now - hwLast).TotalMilliseconds > 300) hwAcc = 0;
            hwLast = DateTime.Now;
            hwAcc += delta;
            if (Math.Abs(hwAcc) >= 60 && (DateTime.Now - hwCooldown).TotalMilliseconds > 420)
            {
                int dir = hwAcc > 0 ? 1 : -1;
                if (S.InvertSwipe) dir = -dir;
                hwAcc = 0; hwCooldown = DateTime.Now;
                GoIndex((int)Math.Round(spg.Target) + dir, true);
            }
        }
        DateTime hwCooldown;

        void PagesKey(KeyEventArgs e)
        {
            bool inText = Keyboard.FocusedElement is TextBox;
            if (expandMode == "launcher") { LauncherKey(e); return; }
            if (expandMode == "wallpaper") { WallpaperKey(e); return; }
            if (inText) return;
            if (CurrentPageKey() == "games" && game != null && GamesKey(e)) { e.Handled = true; return; }
            if (e.Key == Key.Left) { e.Handled = true; GoIndex((int)Math.Round(spg.Target) - 1, true); }
            else if (e.Key == Key.Right) { e.Handled = true; GoIndex((int)Math.Round(spg.Target) + 1, true); }
            else if (e.Key >= Key.D1 && e.Key <= Key.D8) { int i = e.Key - Key.D1; if (i < pageKeys.Count) { e.Handled = true; GoIndex(i, true); } }
            else if (e.Key == Key.Space)
            {
                e.Handled = true;
                string k = CurrentPageKey();
                if (k == "timer") TimerPauseToggle(); else Cmd(1);
            }
        }

        bool PagesEscape()
        {
            if (todoEditCancel != null) { todoEditCancel(); return true; }
            if (expandMode == "pages" && CurrentPageKey() == "games" && GamesEscape()) return true;
            if (expandMode == "launcher" && lInput.Text.Length > 0) { lInput.Text = ""; return true; }
            if (expandMode == "pages" && CurrentPageKey() == "ai")
            {
                if (aiOverlay.Visibility == Visibility.Visible) { AiCloseOverlay(); return true; }
                if (Svc.Ai.Busy) { Svc.Ai.Cancel(); return true; }
                if (aiInput.Text.Length > 0) { aiInput.Text = ""; return true; }
            }
            return false;
        }

        // Rows of the page slide in one after another.
        void StaggerPage(string k)
        {
            if (S.AnimMode == 1) return;
            Grid outer;
            if (!pageRoots.TryGetValue(k, out outer) || outer.Children.Count == 0) return;
            var root = outer.Children[0] as Panel;
            Panel best = root;
            if (root != null)
                foreach (var c in root.Children.OfType<Panel>())
                    if (c.Visibility == Visibility.Visible && c.Children.Count > best.Children.Count && !(c is Canvas)) best = c;
            Motion.Cascade(best, 12, 36, 60);
        }

        // Heavy page work (rebuilding lists, icons, process counts, AI header) never runs mid-slide:
        // on first open it runs right away (the view is still fading in), on a page change it waits
        // until the slide has settled, so every animation frame stays free.
        string pagePending;
        void OnPageShown(string k, bool sliding)
        {
            if (!sliding) { RunPageWork(k); StaggerPage(k); }
            else pagePending = k;
            UpdateEq();
        }

        void RunPageWork(string k)
        {
            if (k == "system") StartStats();
            if (k == "media") { UpdateMediaTimeline(false); AmbientDrift(true); }
            if (k == "timer") PagesTimer();
            if (k == "shelf") RefreshShelf(null);
            if (k == "todo") RefreshTodos(null);
            if (k == "home") PagesClock();
            if (k == "ai") AiOnShown();
            if (k == "games") GamesShown();
            UpdateEq();
        }
        void OnPageHidden(string k)
        {
            if (k == "system") StopStats();
            if (k == "ai") AiCloseOverlay();
            if (k == "media") AmbientDrift(false);
            if (k == "games") GamesHidden();
        }

        void PagesEq() { if (mEq != null) mEq.Run(playing && expanded && curView == vExp && CurrentPageKey() == "media", S.Waveform); }

        // small helpers
        static Grid PageGrid(double l, double t, double r, double b)
        {
            return new Grid { Margin = new Thickness(l, t, r, b) };
        }

        TextBox PinnedTextBox(TextBox tb)
        {
            tb.PreviewMouseDown += delegate { Pin(); Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { tb.Focus(); Keyboard.Focus(tb); })); };
            return tb;
        }

        // ================================================================ home
        // Home: greeting + clock header, one contextual "now" card (timer > media > to-do), and a
        // 4x2 grid of quick-action tiles.
        TextBlock hGreet, hTime, hAmPm, hDate, hNowTitle, hNowSub, hNowGlyph; Border hNow, hNowIcon; BattView hBatt;
        string hNowKind; object hNowArt; TextBlock hCafBadge; UniformGrid hTiles;

        // ---- Home shortcuts: id, icon, label (the order here is the order offered in Settings)
        public static readonly string[][] HomeShortcutCatalog = {
            new[] { "search", "\uE721", "Search" }, new[] { "caffeine", "\u2615", "Caffeine" }, new[] { "walls", "\uE91B", "Wallpapers" },
            new[] { "timer", "\uE916", "Timer" }, new[] { "settings", "\uE713", "Settings" }, new[] { "ai", "\uE8BD", "AI" },
            new[] { "todo", "\uE9D5", "To-do" }, new[] { "shelf", "\uE8B7", "Shelf" }, new[] { "system", "\uE9D9", "System" },
            new[] { "games", "\uE7FC", "Games" }, new[] { "media", "\uE8D6", "Media" } };
        public static readonly string[] DefaultHomeShortcuts = { "search", "caffeine", "walls", "timer", "settings" };
        public const int MaxHomeShortcuts = 5;

        public static List<string> HomeShortcutIds()
        {
            var ids = (Store.S.HomeTiles ?? new List<string>()).Where(x => HomeShortcutCatalog.Any(c => c[0] == x)).Distinct().Take(MaxHomeShortcuts).ToList();
            return ids.Count > 0 ? ids : DefaultHomeShortcuts.ToList();
        }

        void BuildHomeTiles()
        {
            if (hTiles == null) return;
            hTiles.Children.Clear();
            hTimerBadge = hTodoBadge = hShelfBadge = hCafBadge = null;
            var ids = HomeShortcutIds();
            hTiles.Columns = ids.Count;
            var P = Theme.Current;
            foreach (var id in ids)
            {
                var c = HomeShortcutCatalog.First(x => x[0] == id);
                switch (id)
                {
                    case "search": HomeTile(c[1], c[2], P.Accent, () => OpenMode("launcher")); break;
                    case "caffeine":
                        hCafBadge = HomeTile(c[1], c[2], P.Orange, () => SetCaffeine(!caffeine));
                        // the cup is a Segoe UI Symbol character (monochrome), not a Fluent icon
                        ((TextBlock)((StackPanel)((Grid)((Border)hCafBadge.Parent).Parent).Children[0]).Children[0]).FontFamily = new FontFamily("Segoe UI Symbol");
                        break;
                    case "walls": HomeTile(c[1], c[2], P.Accent, () => OpenMode("wallpaper")); break;
                    case "timer": hTimerBadge = HomeTile(c[1], c[2], P.Orange, () => GoPage("timer")); break;
                    case "settings": HomeTile(c[1], c[2], P.Accent, () => OpenSettings(null)); break;
                    case "todo": hTodoBadge = HomeTile(c[1], c[2], P.Green, () => GoPage("todo")); break;
                    case "shelf": hShelfBadge = HomeTile(c[1], c[2], P.Accent, () => GoPage("shelf")); break;
                    default: { string pg = id; HomeTile(c[1], c[2], P.Accent, () => GoPage(pg)); } break;
                }
            }
            UpdateCaffeineTile();
            if (hTime != null) UpdateHomeNow();
        }

        // a quick-action tile: monochrome icon + label, a live badge, a little icon hop on hover
        TextBlock HomeTile(string glyph, string label, Color tint, Action act)
        {
            var tile = new Border { Height = 52, CornerRadius = new CornerRadius(14), Background = Theme.Raised, Margin = new Thickness(3), Cursor = Cursors.Hand, RenderTransformOrigin = new Point(0.5, 0.5) };
            var host = new Grid();
            var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var gl = K.Glyph(glyph, 15, Theme.Text);
            var hop = new TranslateTransform(); gl.RenderTransform = hop;
            var lbl = K.TB(label, 10.5, Theme.Text2, FontWeights.Medium); lbl.HorizontalAlignment = HorizontalAlignment.Center; lbl.Margin = new Thickness(0, 5, 0, 0);
            st.Children.Add(gl); st.Children.Add(lbl);
            host.Children.Add(st);
            var badgeT = K.TB("", 9, Theme.OnAccent, FontWeights.Bold); badgeT.HorizontalAlignment = HorizontalAlignment.Center; K.Tabular(badgeT);
            var badge = new Border { MinWidth = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = K.Solid(tint), Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 5, 0), Child = badgeT, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            host.Children.Add(badge);
            tile.Child = host;
            K.Pressable(tile, act, true);
            tile.MouseEnter += delegate
            {
                if (K.AnimScale <= 0) return;
                var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(K.Dur(420)) };
                a.KeyFrames.Add(new EasingDoubleKeyFrame(-3, KeyTime.FromPercent(0.35), K.OutCubic));
                a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), K.OutBack));
                hop.BeginAnimation(TranslateTransform.YProperty, a);
            };
            hTiles.Children.Add(tile);
            return badgeT;
        }

        // ---- Caffeine: keeps the PC and the screen awake while on (until switched off or the app closes)
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
        bool caffeine;
        public void SetCaffeine(bool on)
        {
            caffeine = on;
            // ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED, or back to normal
            SetThreadExecutionState(on ? 0x80000003u : 0x80000000u);
            UpdateCaffeineTile();
            SystemEvent("\uE706", on ? Theme.Orange : Theme.Raised, on ? "Caffeine on" : "Caffeine off", on ? "Your PC and screen stay awake" : "Sleep settings are back to normal", Pri.System, 2200, "caffeine", null);
        }
        void UpdateCaffeineTile()
        {
            if (hCafBadge == null) return;
            var tile = (Border)((Grid)((Border)hCafBadge.Parent).Parent).Parent;
            var glyph = (TextBlock)((StackPanel)((Grid)tile.Child).Children[0]).Children[0];
            tile.Background = caffeine ? K.Solid(Theme.A(Theme.Current.Orange, 0.22)) : Theme.Raised;
            glyph.Foreground = caffeine ? Theme.Orange : Theme.Text;
            SetBadge(hCafBadge, caffeine ? "On" : null);
        } TextBlock hTimerBadge, hTodoBadge, hShelfBadge;

        // tile badge: hidden when empty, pops in when it first appears
        static void SetBadge(TextBlock t, string text)
        {
            if (t == null) return;
            var b = (Border)t.Parent;
            bool show = !string.IsNullOrEmpty(text);
            if (t.Text != (text ?? "")) t.Text = text ?? "";
            var vis = show ? Visibility.Visible : Visibility.Collapsed;
            if (b.Visibility == vis) return;
            b.Visibility = vis;
            if (show) K.PopIn(b, 0.4, 360);
        } Grid hNowTrack; Border hNowFill; double hNowProg = -1;

        // ---- song timeline. Browsers (Firefox especially) report an empty timeline (no duration) right
        // after a seek and don't send a proper one again until play/pause changes. So: remember the
        // last good duration for this song and where the user seeked to, and fall back to those until
        // the player reports something newer than the seek.
        double tlKnownEnd; string tlKnownTrack; double tlLocalPos = -1; DateTimeOffset tlLocalAt;

        string TrackId() { return mTitleStr + "\u0001" + mArtistStr; }

        // Duration and current position in seconds; false = unknown.
        bool MediaTimeline(out double end, out double pos)
        {
            end = 0; pos = 0;
            if (ses == null) return false;
            try
            {
                var tl = ses.GetTimelineProperties();
                end = (tl.EndTime - tl.StartTime).TotalSeconds; pos = (tl.Position - tl.StartTime).TotalSeconds;
                bool sameTrack = tlKnownTrack == TrackId();
                if (!sameTrack) tlLocalPos = -1;
                bool fresh = tlLocalPos < 0 || (tl.LastUpdatedTime.Year > 2000 && tl.LastUpdatedTime > tlLocalAt.AddMilliseconds(300));
                if (end > 1 && fresh)
                {
                    tlKnownEnd = end; tlKnownTrack = TrackId(); tlLocalPos = -1;
                    if (playing && tl.LastUpdatedTime.Year > 2000) pos += (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds;
                }
                else if (sameTrack && tlKnownEnd > 1)
                {
                    end = tlKnownEnd;
                    if (tlLocalPos >= 0) pos = tlLocalPos + (playing ? (DateTimeOffset.Now - tlLocalAt).TotalSeconds : 0);
                    else if (playing && tl.LastUpdatedTime.Year > 2000) pos += (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds;
                }
                else return false;
                pos = Math.Max(0, Math.Min(end, pos));
                return true;
            }
            catch { return false; }
        }

        // the user jumped to frac of the song: show it right away, whatever the player reports
        void MediaSeeked(double frac, double end)
        {
            if (end <= 1) return;
            tlKnownEnd = end; tlKnownTrack = TrackId();
            tlLocalPos = frac * end; tlLocalAt = DateTimeOffset.Now;
        }

        // Song position as 0..1 (-1 = unknown), extrapolated between the player's updates.
        double MediaProgress(out double perSec)
        {
            perSec = 0;
            double end, pos;
            if (!MediaTimeline(out end, out pos)) return -1;
            if (playing) perSec = 1 / end;
            return pos / end;
        }

        void HomeProgress(double p, double perSec, Brush color)
        {
            if (hNowTrack == null) return;
            hNowTrack.Visibility = p < 0 ? Visibility.Collapsed : Visibility.Visible;
            if (p < 0) { hNowProg = -1; return; }
            hNowFill.Background = color;
            double w = hNowTrack.ActualWidth;
            if (w <= 0) return;
            double target = Math.Max(3, Math.Min(1, p + perSec) * w);
            // glide linearly to where it'll be at the next tick (1 s), so it moves smoothly;
            // jumps (seek, new track) snap instead
            if (hNowProg >= 0 && Math.Abs(p - hNowProg) < 0.05 && K.AnimScale > 0)
                hNowFill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(1000)));
            else { hNowFill.BeginAnimation(WidthProperty, null); hNowFill.Width = target; }
            hNowProg = p;
        }

        Grid BuildHome()
        {
            var g = new Grid();
            var sp = new StackPanel { Margin = new Thickness(20, 12, 20, 0) };

            // header
            var top = new Grid();
            var clock = new StackPanel();
            hGreet = K.TB("", 11.5, Theme.Accent, FontWeights.SemiBold);
            var timeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-1, 0, 0, 0) };
            hTime = K.TB("", 38, Theme.Text, FontWeights.Light); K.Tabular(hTime);
            hTime.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; hTime.LineHeight = 44;
            hAmPm = K.TB("", 13, Theme.Text2, FontWeights.Medium); hAmPm.VerticalAlignment = VerticalAlignment.Bottom; hAmPm.Margin = new Thickness(6, 0, 0, 9);
            timeRow.Children.Add(hTime); timeRow.Children.Add(hAmPm);
            hDate = K.TB("", 12, Theme.Text2, FontWeights.Medium); hDate.Margin = new Thickness(1, -5, 0, 0);
            clock.Children.Add(hGreet); clock.Children.Add(timeRow); clock.Children.Add(hDate);
            hBatt = new BattView(12) { VerticalAlignment = VerticalAlignment.Center };
            // battery + settings in the top-right corner (settings moved here from the tile grid)
            var corner = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 2, 0) };
            corner.Children.Add(hBatt);
            top.Children.Add(clock); top.Children.Add(corner);
            sp.Children.Add(top);

            // contextual "now" card
            hNow = new Border { Height = 46, CornerRadius = new CornerRadius(14), Background = Theme.Raised, Margin = new Thickness(0, 10, 0, 0), Cursor = Cursors.Hand, Padding = new Thickness(7, 0, 12, 0) };
            var ng = new Grid();
            ng.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ng.ColumnDefinitions.Add(new ColumnDefinition());
            ng.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hNowIcon = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(10), Background = K.Solid(Theme.A(Theme.Current.Accent, 0.16)), VerticalAlignment = VerticalAlignment.Center };
            hNowGlyph = K.Glyph("", 13, Theme.Accent);
            hNowIcon.Child = hNowGlyph;
            var nt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
            hNowTitle = K.TB("", 12.5, Theme.Text, FontWeights.SemiBold);
            hNowSub = K.TB("", 10.5, Theme.Text2, FontWeights.Normal); hNowSub.Margin = new Thickness(0, 1, 0, 0);
            nt.Children.Add(hNowTitle); nt.Children.Add(hNowSub);
            // thin progress line (song position / timer) under the text
            hNowTrack = new Grid { Height = 3, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
            hNowTrack.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = Theme.Hover });
            hNowFill = new Border { CornerRadius = new CornerRadius(1.5), Background = Theme.AccentFill, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            hNowTrack.Children.Add(hNowFill);
            hNowTrack.SizeChanged += delegate { hNowProg = -1; UpdateHomeNow(); };
            nt.Children.Add(hNowTrack);
            Grid.SetColumn(nt, 1);
            var chev = K.Glyph("", 10, Theme.Text3); Grid.SetColumn(chev, 2);
            ng.Children.Add(hNowIcon); ng.Children.Add(nt); ng.Children.Add(chev);
            hNow.Child = ng;
            K.Pressable(hNow, () => GoPage(hNowKind == "timer" ? "timer" : hNowKind == "media" ? "media" : "todo"), true);
            sp.Children.Add(hNow);

            // one row of shortcuts (picked in Settings → Pages); pages themselves live in the dock below
            hTiles = new UniformGrid { Rows = 1, Margin = new Thickness(-3, 9, -3, 0) };
            sp.Children.Add(hTiles);
            g.Children.Add(sp);
            return g;
        }

        void PagesClock()
        {
            var now = DateTime.Now;
            if (hTime != null)
            {
                int hr = now.Hour;
                string part = hr < 5 ? "night" : hr < 12 ? "morning" : hr < 18 ? "afternoon" : "evening";
                string name = (S.UserName ?? "").Trim();
                hGreet.Text = "Good " + part + (name.Length > 0 ? ", " + name : "");
                hTime.Text = now.ToString(S.Clock24h ? "HH:mm" : "h:mm", K.EN);
                hAmPm.Text = S.Clock24h ? "" : now.ToString("tt", K.EN);
                hAmPm.Visibility = S.Clock24h ? Visibility.Collapsed : Visibility.Visible;
                hDate.Text = now.ToString("dddd, MMMM d", K.EN);
                UpdateHomeNow();
                TimerEnds();
            }
            if (expanded && expandMode == "pages" && CurrentPageKey() == "media") UpdateMediaTimeline(true);
        }

        void UpdateHomeNow()
        {
            string kind, title, sub, glyph;
            object artKey = null;
            if (tRunning)
            {
                kind = "timer"; glyph = tPaused ? "" : "";
                title = K.Fmt(Math.Ceiling(Remaining().TotalSeconds)) + (tPaused ? " · paused" : " left");
                sub = "Timer · " + K.Fmt(tTotal.TotalSeconds) + " total";
            }
            else if (hasMedia && ses != null && mTitleStr.Length > 0)
            {
                kind = "media"; glyph = playing ? "" : ""; artKey = art;
                title = mTitleStr;
                sub = (playing ? "Now playing" : "Paused") + (mArtistStr.Length > 0 ? " · " + mArtistStr : "");
            }
            else
            {
                kind = "todo"; glyph = "";
                var next = Store.Todos.FirstOrDefault(t => !t.Done);
                int open = Store.Todos.Count(t => !t.Done);
                title = next != null ? next.Text : "You're all caught up";
                sub = next != null ? (open > 1 ? (open - 1) + " more to-do" + (open > 2 ? "s" : "") : "Last one on the list") : "No to-dos · click to add one";
            }
            hNowKind = kind;
            int openTodos = Store.Todos.Count(x => !x.Done);
            SetBadge(hTodoBadge, openTodos > 0 ? openTodos.ToString() : null);
            SetBadge(hShelfBadge, S.ShelfItems.Count > 0 ? S.ShelfItems.Count.ToString() : null);
            SetBadge(hTimerBadge, tRunning ? (tPaused ? "II" : K.Fmt(Math.Ceiling(Remaining().TotalSeconds))) : null);
            if (kind == "timer") HomeProgress(1 - Remaining().TotalSeconds / Math.Max(1, tTotal.TotalSeconds), tPaused ? 0 : 1 / Math.Max(1, tTotal.TotalSeconds), Theme.Orange);
            else if (kind == "media")
            {
                double ps; double p = MediaProgress(out ps);
                bool pale = artColor.R > 225 && artColor.G > 225 && artColor.B > 225;
                HomeProgress(p, ps, pale ? (Brush)Theme.AccentFill : K.Solid(artColor));
            }
            else HomeProgress(-1, 0, null);
            if (hNowTitle.Text != title) hNowTitle.Text = title;
            if (hNowSub.Text != sub) hNowSub.Text = sub;
            hNowGlyph.Text = glyph;
            if (!ReferenceEquals(artKey, hNowArt) || hNowIcon.Tag as string != kind)
            {
                hNowArt = artKey; hNowIcon.Tag = kind;
                var img = artKey as ImageSource;
                if (img != null) { hNowIcon.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill }; hNowGlyph.Visibility = Visibility.Collapsed; }
                else
                {
                    Color c = kind == "timer" ? Theme.Current.Orange : Theme.Current.Accent;
                    hNowIcon.Background = K.Solid(Theme.A(c, 0.16));
                    hNowGlyph.Foreground = K.Solid(c);
                    hNowGlyph.Visibility = Visibility.Visible;
                }
            }
        }

        void PagesBattery(bool none, int pct, bool ac) { if (hBatt != null) hBatt.Update(none, pct, ac); }

        // ================================================================ media
        // Blurred-art ambience that fades out at the bottom, art that settles back when paused, a
        // track-change transition, a seek bar tinted with the art colour that thickens on hover (and
        // previews the time while dragging), and controls with their own motion.
        Border mArt, mSourceChip, mPlayBtn; TextBlock mGlyph, mTitle, mArtist, mElapsed, mRemain, mPlayGlyph, mSourceText; Image mAmbient; Eq mEq; SliderBar mSeek; Grid mTimeRow;
        StackPanel mText; ScaleTransform mArtScale; TranslateTransform mPrevT, mNextT;
        bool mSeeking; TranslateTransform mAmbientT; string mShownTitle; double mDur; bool mShownPlaying = true;

        // Continuous drift only while the Media page is on screen (no idle CPU otherwise).
        void AmbientDrift(bool on)
        {
            if (mAmbientT == null) return;
            if (on && S.AmbientMedia && K.AnimScale > 0)
            {
                var drift = new DoubleAnimation(-30, 30, TimeSpan.FromSeconds(14)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                Timeline.SetDesiredFrameRate(drift, 20);
                mAmbientT.BeginAnimation(TranslateTransform.XProperty, drift);
            }
            else mAmbientT.BeginAnimation(TranslateTransform.XProperty, null);
        }

        Grid BuildMediaPage()
        {
            var g = new Grid { ClipToBounds = true };
            mAmbient = new Image { Stretch = Stretch.UniformToFill, Opacity = 0.32, Effect = new BlurEffect { Radius = 46, RenderingBias = RenderingBias.Performance }, CacheMode = new BitmapCache(0.5), IsHitTestVisible = false };
            var amT = new TranslateTransform(); var amS = new ScaleTransform(1.5, 1.5);
            var amG = new TransformGroup(); amG.Children.Add(amS); amG.Children.Add(amT);
            mAmbient.RenderTransform = amG; mAmbient.RenderTransformOrigin = new Point(0.5, 0.5);
            mAmbientT = amT;
            // drawn by the island shell (see UpdateAmbient) so it fills the whole outline: notch
            // shoulders and the page-dots strip too. A fixed size keeps the blur cache stable while morphing.
            mAmbient.Width = 700; mAmbient.Height = 560; mAmbient.HorizontalAlignment = HorizontalAlignment.Center; mAmbient.VerticalAlignment = VerticalAlignment.Top;
            var amCanvas = new Canvas(); amCanvas.Children.Add(mAmbient); ambLayer.Children.Add(amCanvas);   // a Canvas never layout-clips it

            var sp = new StackPanel { Margin = new Thickness(22, 16, 22, 0) };
            var info = new Grid();
            info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            info.ColumnDefinitions.Add(new ColumnDefinition());
            info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mArt = ArtBox(64, 14, 24, out mGlyph);
            mArtScale = new ScaleTransform(1, 1); mArt.RenderTransform = mArtScale; mArt.RenderTransformOrigin = new Point(0.5, 0.5);
            mArt.Cursor = Cursors.Hand; mArt.ToolTip = "Play / pause";
            mArt.MouseLeftButtonDown += (s, e) => e.Handled = true;
            mArt.MouseLeftButtonUp += (s, e) => { e.Handled = true; Cmd(1); };
            mText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) };
            mTitle = K.TB("", 16, Theme.Text, FontWeights.SemiBold);
            mArtist = K.TB("", 12.5, Theme.Text2, FontWeights.Normal); mArtist.Margin = new Thickness(0, 1, 0, 0);
            mSourceText = K.TB("", 10.5, Theme.Text2, FontWeights.SemiBold);
            var srcRow = new StackPanel { Orientation = Orientation.Horizontal };
            srcRow.Children.Add(K.Glyph("", 9, Theme.Text2));
            mSourceText.Margin = new Thickness(5, 0, 0, 0);
            srcRow.Children.Add(mSourceText);
            mSourceChip = new Border { Height = 20, CornerRadius = new CornerRadius(10), Background = K.Solid(Theme.A(Theme.Current.Text, 0.08)), Padding = new Thickness(8, 0, 9, 0), Child = srcRow, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Cursor = Cursors.Hand };
            K.Pressable(mSourceChip, NextSession, false);
            mSourceChip.ToolTip = "Switch player";
            mText.Children.Add(mTitle); mText.Children.Add(mArtist); mText.Children.Add(mSourceChip);
            mEq = new Eq(4, 3.5, 20, 3);
            Grid.SetColumn(mText, 1); Grid.SetColumn(mEq, 2);
            info.Children.Add(mArt); info.Children.Add(mText); info.Children.Add(mEq);
            sp.Children.Add(info);

            mTimeRow = new Grid { Margin = new Thickness(0, 14, 0, 0), Height = 20 };
            mTimeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            mTimeRow.ColumnDefinitions.Add(new ColumnDefinition());
            mTimeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            mElapsed = K.TB("0:00", 10.5, Theme.Text2, FontWeights.Medium); K.Tabular(mElapsed);
            mRemain = K.TB("-0:00", 10.5, Theme.Text2, FontWeights.Medium); mRemain.HorizontalAlignment = HorizontalAlignment.Right; K.Tabular(mRemain);
            mSeek = new SliderBar(0, 1, 0, 0, 0, 5) { ShowKnob = false };
            mSeek.GrowOnHover(8);
            // while dragging, the labels preview where the song will jump to
            mSeek.Changed += v =>
            {
                mSeeking = true;
                if (mDur > 1) { mElapsed.Text = K.Fmt(v * mDur); mRemain.Text = "-" + K.Fmt(mDur - v * mDur); mElapsed.Foreground = Theme.Text; }
            };
            mSeek.Committed += v => { mSeeking = false; mElapsed.Foreground = Theme.Text2; Seek(v); };
            Grid.SetColumn(mSeek, 1); Grid.SetColumn(mRemain, 2);
            mTimeRow.Children.Add(mElapsed); mTimeRow.Children.Add(mSeek); mTimeRow.Children.Add(mRemain);
            sp.Children.Add(mTimeRow);

            var ctr = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            var prev = K.IconBtn("", 44, 18, Theme.Text, () => { Nudge(mPrevT, -5); Cmd(0); });
            mPrevT = new TranslateTransform(); ((TextBlock)prev.Child).RenderTransform = mPrevT;
            mPlayBtn = K.IconBtn("", 52, 22, Theme.Text, () => Cmd(1), out mPlayGlyph); mPlayBtn.Margin = new Thickness(20, 0, 20, 0);
            mPlayBtn.Background = K.Solid(Theme.A(Theme.Current.Text, 0.10));
            mPlayGlyph.RenderTransformOrigin = new Point(0.5, 0.5);
            var next = K.IconBtn("", 44, 18, Theme.Text, () => { Nudge(mNextT, 5); Cmd(2); });
            mNextT = new TranslateTransform(); ((TextBlock)next.Child).RenderTransform = mNextT;
            ctr.Children.Add(prev); ctr.Children.Add(mPlayBtn); ctr.Children.Add(next);
            sp.Children.Add(ctr);
            g.Children.Add(sp);
            return g;
        }

        // skip arrows dart in their direction and spring back
        static void Nudge(TranslateTransform t, double dx)
        {
            if (K.AnimScale <= 0) return;
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(K.Dur(380)) };
            a.KeyFrames.Add(new EasingDoubleKeyFrame(dx, KeyTime.FromPercent(0.3), K.OutCubic));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), K.OutBack));
            t.BeginAnimation(TranslateTransform.XProperty, a);
        }

        void NextSession()
        {
            var list = Sessions();
            if (list.Count < 2) return;
            int i = ses != null ? list.IndexOf(ses) : -1;
            PickSession(list[(i + 1) % list.Count]);
        }

        void PagesSessions()
        {
            bool had = pageKeys.Contains("media"), has = ses != null;
            if (had != has) RebuildPageKeys();
            if (mSourceText != null)
            {
                int n = Sessions().Count;
                mSourceText.Text = mSource + (n > 1 ? "  ·  " + n + " players" : "");
                mSourceChip.ToolTip = n > 1 ? "Switch player" : mSource;
            }
        }

        void PagesMedia()
        {
            if (mTitle == null) return;
            string title = mTitleStr.Length > 0 ? mTitleStr : "Untitled";
            bool changed = mShownTitle != null && title != mShownTitle;
            mShownTitle = title;
            mTitle.Text = title;
            mArtist.Text = mArtistStr;
            SetArt(mArt, mGlyph, art);
            var b = K.Solid(artColor);
            mEq.SetColor(b);
            // the seek fill takes the art colour (white art falls back to the accent)
            bool pale = artColor.R > 225 && artColor.G > 225 && artColor.B > 225;
            mSeek.SetFill(pale ? (Brush)Theme.AccentFill : b);
            var newAmb = S.AmbientMedia ? art : null;
            if (changed && expanded && K.AnimScale > 0)
            {
                // new track: text slides up into place, art pops, the glow cross-fades
                K.FadeIn(mText, 10, 380);
                mArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); mArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                mArtScale.ScaleX = mArtScale.ScaleY = 0.86;
                K.Pop(mArtScale, playing ? 1 : 0.9, 520, K.OutBack);
                mAmbient.Opacity = 0; mAmbient.Source = newAmb;
                mAmbient.BeginAnimation(OpacityProperty, K.Anim(0.32, 600, K.OutCubic));
            }
            else mAmbient.Source = newAmb;
            PagesSessions();
        }

        void PagesPlayback()
        {
            if (mPlayGlyph != null && mShownPlaying != playing)
            {
                mShownPlaying = playing;
                string to = playing ? "" : "";
                if (expanded && K.AnimScale > 0)
                {
                    // glyph shrinks out, swaps, springs back in
                    var st = new ScaleTransform(1, 1); mPlayGlyph.RenderTransform = st;
                    var outA = K.Anim(0.5, 110, K.OutCubic);
                    outA.Completed += delegate { mPlayGlyph.Text = to; K.Pop(st, 1, 380, K.OutBack); mPlayGlyph.BeginAnimation(OpacityProperty, K.Anim(1, 160, K.OutCubic)); };
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, outA); st.BeginAnimation(ScaleTransform.ScaleYProperty, K.Anim(0.5, 110, K.OutCubic));
                    mPlayGlyph.BeginAnimation(OpacityProperty, K.Anim(0.2, 110, K.OutCubic));
                }
                else mPlayGlyph.Text = to;
                // the artwork settles back a little while paused
                if (mArtScale != null) K.Pop(mArtScale, playing ? 1 : 0.9, 520, playing ? K.OutBack : K.OutQuint);
            }
            else if (mPlayGlyph != null) mPlayGlyph.Text = playing ? "" : "";
            UpdateMediaTimeline(false);
        }

        void UpdateMediaTimeline(bool tick)
        {
            if (ses == null || mSeek == null || mSeeking) return;
            try
            {
                double end, pos;
                bool known = MediaTimeline(out end, out pos);
                mTimeRow.Visibility = Visibility.Visible;
                if (!known)
                {
                    // the player hasn't reported a duration yet (e.g. right after a track change):
                    // keep the bar, dimmed, with placeholder times
                    mDur = 0; mSeek.SetValue(0, false); mSeek.Opacity = 0.45; mSeek.IsHitTestVisible = false;
                    mElapsed.Text = "--:--"; mRemain.Text = "--:--";
                    return;
                }
                mSeek.Opacity = 1; mSeek.IsHitTestVisible = true;
                mDur = end;
                pos = Math.Max(0, Math.Min(end, pos));
                mSeek.SetValue(pos / end, false);
                mElapsed.Text = K.Fmt(pos); mRemain.Text = "-" + K.Fmt(end - pos);
            }
            catch { }
        }


        // ================================================================ timer
        // Setup: big duration with -/+ (and wheel), presets (the current one highlighted), "ends at"
        // preview and a Start button. Running: ring with time left + total, status, end time and
        // three round controls (pause/resume, +1 min, cancel); paused dims the ring and pulses the time.
        StackPanel tSetup; Grid tRun; WrapPanel tPresets; TextBlock tSetText, tSetEnds, tRunText, tRunOf, tPauseGlyph, tPauseLbl, tStatus, tRunEnds; Ring tRing;
        Border tStatusDot, tPauseBtn;

        string ClockFmt(DateTime t) { return t.ToString(S.Clock24h ? "HH:mm" : "h:mm tt", K.EN); }

        Grid BuildTimerPage()
        {
            var g = new Grid();

            // ---- setup
            tSetup = new StackPanel { Margin = new Thickness(20, 12, 20, 0) };
            var top = new Grid();
            top.Children.Add(K.TB("Timer", 12, Theme.Text2, FontWeights.SemiBold));
            var ends = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            ends.Children.Add(K.Glyph("", 10, Theme.Text3));
            tSetEnds = K.TB("", 11, Theme.Text3, FontWeights.Medium); tSetEnds.Margin = new Thickness(5, 0, 0, 0);
            ends.Children.Add(tSetEnds);
            top.Children.Add(ends);
            tSetup.Children.Add(top);

            var row = new Grid { Height = 70, Background = Brushes.Transparent, Margin = new Thickness(0, 2, 0, 0) };
            var minus = K.IconBtn("", 40, 13, Theme.Text, () => AdjustSetup(-1)); minus.Background = Theme.Raised; minus.HorizontalAlignment = HorizontalAlignment.Left;
            var plus = K.IconBtn("", 40, 13, Theme.Text, () => AdjustSetup(1)); plus.Background = Theme.Raised; plus.HorizontalAlignment = HorizontalAlignment.Right;
            var mid = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            tSetText = K.TB("05:00", 44, Theme.Text, FontWeights.Light); tSetText.HorizontalAlignment = HorizontalAlignment.Center; K.Tabular(tSetText);
            tSetText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; tSetText.LineHeight = 50;
            var hint = K.TB("scroll or use − / + to adjust", 10, Theme.Text3, FontWeights.Normal); hint.HorizontalAlignment = HorizontalAlignment.Center;
            mid.Children.Add(tSetText); mid.Children.Add(hint);
            row.Children.Add(minus); row.Children.Add(mid); row.Children.Add(plus);
            row.MouseWheel += (s, e) => { e.Handled = true; AdjustSetup(e.Delta > 0 ? 1 : -1); };
            tSetup.Children.Add(row);

            tPresets = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            tSetup.Children.Add(tPresets);
            var start = K.GlyphPill("", "Start", Theme.AccentFill, Theme.OnAccent, 38, () => TimerStart(TimeSpan.FromMinutes(setupMinutes)));
            start.Width = 170; start.HorizontalAlignment = HorizontalAlignment.Center; start.Margin = new Thickness(0, 12, 0, 0);
            tSetup.Children.Add(start);

            // ---- running
            tRun = new Grid { Margin = new Thickness(24, 16, 20, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top };
            tRun.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tRun.ColumnDefinitions.Add(new ColumnDefinition());
            var ringHost = new Grid { Width = 138, Height = 138 };
            tRing = new Ring(138, 8, Theme.Orange);
            var inRing = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            tRunText = K.TB("0:00", 32, Theme.Orange, FontWeights.Light); tRunText.HorizontalAlignment = HorizontalAlignment.Center; K.Tabular(tRunText);
            tRunOf = K.TB("", 10.5, Theme.Text3, FontWeights.Medium); tRunOf.HorizontalAlignment = HorizontalAlignment.Center; tRunOf.Margin = new Thickness(0, -2, 0, 0);
            inRing.Children.Add(tRunText); inRing.Children.Add(tRunOf);
            ringHost.Children.Add(tRing); ringHost.Children.Add(inRing);
            tRun.Children.Add(ringHost);

            var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 0, 0) };
            var st = new StackPanel { Orientation = Orientation.Horizontal };
            tStatusDot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Background = Theme.Orange, VerticalAlignment = VerticalAlignment.Center };
            tStatus = K.TB("Running", 11.5, Theme.Orange, FontWeights.SemiBold); tStatus.Margin = new Thickness(6, 0, 0, 0);
            st.Children.Add(tStatusDot); st.Children.Add(tStatus);
            side.Children.Add(st);
            tRunEnds = K.TB("", 14, Theme.Text, FontWeights.Medium); tRunEnds.Margin = new Thickness(0, 3, 0, 0);
            side.Children.Add(tRunEnds);

            var ctl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            Func<string, string, Brush, Brush, double, Action, Border> round = (glyph, label, bg, fg, size, act) =>
            {
                var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
                var b = K.IconBtn(glyph, size, size > 42 ? 16 : 13, fg, act); b.Background = bg; b.HorizontalAlignment = HorizontalAlignment.Center;
                var l = K.TB(label, 10, Theme.Text2, FontWeights.Medium); l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 4, 0, 0);
                cell.Children.Add(b); cell.Children.Add(l);
                ctl.Children.Add(cell);
                b.Tag = l;
                return b;
            };
            tPauseBtn = round("", "Pause", Theme.OrangeSoft, Theme.Orange, 46, TimerPauseToggle);
            tPauseGlyph = (TextBlock)tPauseBtn.Child; tPauseLbl = (TextBlock)tPauseBtn.Tag;
            var addB = round("", "+1 min", Theme.Raised, Theme.Text, 46, () => TimerAdd(1));
            var canB = round("", "Cancel", Theme.RedSoft, Theme.Red, 46, TimerCancel);
            side.Children.Add(ctl);
            Grid.SetColumn(side, 1);
            tRun.Children.Add(side);

            g.Children.Add(tSetup); g.Children.Add(tRun);
            return g;
        }

        void RebuildPresets()
        {
            if (tPresets == null) return;
            tPresets.Children.Clear();
            var nums = new List<int>();
            foreach (var part in (S.TimerPresets ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int n; if (int.TryParse(part.Trim(), out n) && n > 0 && n <= 600 && !nums.Contains(n)) nums.Add(n);
                if (nums.Count >= 6) break;
            }
            foreach (var n in nums)
            {
                int m = n;
                // one click starts it (and it stays highlighted as the chosen duration)
                var chip = K.PillBtn(m >= 60 && m % 60 == 0 ? (m / 60) + " h" : m + " min", Theme.Raised, Theme.Text2, 28, () => { setupMinutes = m; TimerStart(TimeSpan.FromMinutes(m)); });
                ((TextBlock)chip.Child).FontSize = 12; chip.Padding = new Thickness(12, 0, 12, 0); chip.Margin = new Thickness(3);
                chip.Tag = m;
                tPresets.Children.Add(chip);
            }
            PagesTimer();
        }

        void PopSetText()
        {
            var st = new ScaleTransform(1, 1); tSetText.RenderTransform = st; tSetText.RenderTransformOrigin = new Point(0.5, 0.5);
            st.ScaleX = st.ScaleY = 1.08; K.Pop(st, 1, 300, K.OutBack);
        }

        void AdjustSetup(int d)
        {
            int step = setupMinutes >= 60 ? 5 : 1;
            setupMinutes = Math.Max(1, Math.Min(600, setupMinutes + d * step));
            PagesTimer();
            PopSetText();
        }

        void PagesTimer()
        {
            if (tSetup == null) return;
            bool wasRun = tRun.Visibility == Visibility.Visible;
            tSetup.Visibility = tRunning ? Visibility.Collapsed : Visibility.Visible;
            tRun.Visibility = tRunning ? Visibility.Visible : Visibility.Collapsed;
            if (wasRun != tRunning && expanded) K.FadeIn(tRunning ? (UIElement)tRun : tSetup, 10, 320);
            tSetText.Text = setupMinutes >= 60 ? string.Format("{0}:{1:00}:00", setupMinutes / 60, setupMinutes % 60) : string.Format("{0:00}:00", setupMinutes);
            foreach (var c in tPresets.Children.OfType<Border>())
            {
                bool on = c.Tag is int && (int)c.Tag == setupMinutes;
                c.Background = on ? (Brush)Theme.Hover : Theme.Raised;
                ((TextBlock)c.Child).Foreground = on ? Theme.Text : Theme.Text2;
            }
            // paused: grey ring, pulsing time, play icon
            tPauseGlyph.Text = tPaused ? "" : "";
            tPauseLbl.Text = tPaused ? "Resume" : "Pause";
            tRunText.Foreground = tPaused ? Theme.Text2 : Theme.Orange;
            tRing.Color = tPaused ? Theme.Text3 : Theme.Orange;
            tStatus.Text = tPaused ? "Paused" : "Running";
            tStatus.Foreground = tPaused ? Theme.Text2 : Theme.Orange;
            tStatusDot.Background = tPaused ? Theme.Text2 : Theme.Orange;
            if (tPaused && K.AnimScale > 0)
                tRunText.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            else { tRunText.BeginAnimation(OpacityProperty, null); tRunText.Opacity = 1; }
            TimerEnds();
            if (!expanded && cur == null) ShowBase();
        }

        // "Ends at" for both views (called each second by the clock and on every change).
        void TimerEnds()
        {
            if (tSetEnds == null) return;
            if (!tRunning) { tSetEnds.Text = "Ends at " + ClockFmt(DateTime.Now.AddMinutes(setupMinutes)); return; }
            tRunEnds.Text = tPaused ? "Tap resume to continue" : "Ends at " + ClockFmt(DateTime.Now + Remaining());
            tRunOf.Text = "of " + K.Fmt(tTotal.TotalSeconds);
        }

        void PagesTimerTick(TimeSpan rem, double prog)
        {
            if (tRing == null) return;
            tRing.Progress = prog;
            tRunText.Text = K.Fmt(Math.Ceiling(rem.TotalSeconds));
        }


        // ================================================================ to-do
        // Header with progress bar, an input bar like the AI one, rows with a round check, inline edit
        // (click the text), age that swaps to a delete button on hover, done items sink to the bottom,
        // and "Clear done" only when there is something to clear.
        TextBox todoInput; StackPanel todoList; TextBlock todoFooter, todoCount; ScrollViewer todoScroll;
        Border todoBar, todoClear; Grid todoTrack; SolidColorBrush todoBarBorder, todoAddBg; TextBlock todoAddGlyph;
        DispatcherTimer todoMsgTimer; Action todoEditCancel;   // set while a row is being edited (Esc cancels it)

        Grid BuildTodoPage()
        {
            var g = new Grid { Margin = new Thickness(16, 12, 16, 2) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // header + progress
            var head = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
            var hr = new Grid();
            hr.Children.Add(K.TB("To-do", 12, Theme.Text2, FontWeights.SemiBold));
            todoCount = K.TB("", 11, Theme.Text3, FontWeights.Medium); todoCount.HorizontalAlignment = HorizontalAlignment.Right;
            hr.Children.Add(todoCount);
            head.Children.Add(hr);
            todoTrack = new Grid { Height = 4, Margin = new Thickness(0, 6, 0, 0) };
            todoTrack.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Theme.Hover });
            todoBar = new Border { CornerRadius = new CornerRadius(2), Background = Theme.Green, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            todoTrack.Children.Add(todoBar);
            head.Children.Add(todoTrack);
            g.Children.Add(head);

            // input bar
            todoBarBorder = new SolidColorBrush(Theme.A(Theme.Current.Text, 0.06));
            var bar = new Border { CornerRadius = new CornerRadius(19), Background = Theme.Raised, MinHeight = 38, BorderThickness = new Thickness(1), BorderBrush = todoBarBorder, Margin = new Thickness(0, 10, 0, 0) };
            var ib = new Grid();
            ib.ColumnDefinitions.Add(new ColumnDefinition());
            ib.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            todoInput = PinnedTextBox(new TextBox());
            var field = K.Field(todoInput, "Add a to-do…", 0, Brushes.Transparent);
            todoInput.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; AddTodo(); } };
            todoAddBg = new SolidColorBrush(Theme.Current.Hover);
            var add = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = todoAddBg, Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            todoAddGlyph = K.Glyph("", 12, Theme.Text3); add.Child = todoAddGlyph;
            K.Pressable(add, AddTodo, false);
            Grid.SetColumn(add, 1);
            ib.Children.Add(field); ib.Children.Add(add);
            bar.Child = ib;
            bool lit = false;
            todoInput.TextChanged += delegate
            {
                bool on = todoInput.Text.Trim().Length > 0;
                if (on == lit) return; lit = on;
                todoAddBg.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(on ? Theme.Current.Accent : Theme.Current.Hover, 180));
                todoAddGlyph.Foreground = on ? Theme.OnAccent : Theme.Text3;
            };
            todoInput.GotKeyboardFocus += delegate { todoBarBorder.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(Theme.A(Theme.Current.Accent, 0.55), 200)); };
            todoInput.LostKeyboardFocus += delegate { todoBarBorder.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(Theme.A(Theme.Current.Text, 0.06), 260)); };
            Grid.SetRow(bar, 1);
            g.Children.Add(bar);

            todoScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Margin = new Thickness(0, 6, 0, 2) }; ScrollFade.Attach(todoScroll, 22, false);
            todoList = new StackPanel();
            todoScroll.Content = todoList;
            Grid.SetRow(todoScroll, 2);
            g.Children.Add(todoScroll);

            // footer: hint (or a short message) + "Clear done"
            var foot = new Grid { Height = 26 };
            todoFooter = K.TB("", 10.5, Theme.Text3, FontWeights.Normal); todoFooter.Margin = new Thickness(4, 0, 0, 0);
            todoClear = K.GlyphPill("", "Clear done", Theme.Raised, Theme.Text2, 24, () => { Store.Todos.RemoveAll(t => t.Done); Store.SaveTodos(); RefreshTodos(null); });
            todoClear.HorizontalAlignment = HorizontalAlignment.Right; todoClear.Padding = new Thickness(10, 0, 10, 0);
            foreach (var tb in ((StackPanel)todoClear.Child).Children.OfType<TextBlock>()) tb.FontSize = tb.FontFamily == K.ICO ? 10 : 11;
            foot.Children.Add(todoFooter); foot.Children.Add(todoClear);
            Grid.SetRow(foot, 3);
            g.Children.Add(foot);
            todoTrack.SizeChanged += delegate { TodoStats(false); };
            return g;
        }

        void TodoMessage(string msg)
        {
            todoFooter.Text = msg; todoFooter.Foreground = Theme.Orange;
            if (todoMsgTimer == null) { todoMsgTimer = Timer(2600, null); todoMsgTimer.Tick += delegate { todoMsgTimer.Stop(); todoFooter.Foreground = Theme.Text3; TodoStats(false); }; }
            todoMsgTimer.Stop(); todoMsgTimer.Start();
        }

        void AddTodo()
        {
            string t = todoInput.Text.Trim();
            if (t.Length == 0) return;
            if (Store.Todos.Count >= S.TodoMax) { TodoMessage("Limit of " + S.TodoMax + " to-dos reached · clear some first"); return; }
            var item = new TodoItem { Text = t, Created = DateTime.Now.Ticks };
            Store.Todos.Insert(0, item); Store.SaveTodos();
            todoInput.Text = "";
            RefreshTodos(item);
        }

        void TodoStats(bool anim)
        {
            if (todoCount == null) return;
            int total = Store.Todos.Count, done = Store.Todos.Count(x => x.Done);
            todoCount.Text = total == 0 ? "" : done == total ? "All " + total + " done" : done + " of " + total + " done";
            double w = todoTrack.ActualWidth > 0 ? todoTrack.ActualWidth : S.ExpandedWidth - 40;
            double target = total == 0 ? 0 : w * done / total;
            if (anim) todoBar.BeginAnimation(WidthProperty, K.Anim(target, 420, K.OutCubic));
            else { todoBar.BeginAnimation(WidthProperty, null); todoBar.Width = target; }
            todoClear.Visibility = done > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (todoMsgTimer == null || !todoMsgTimer.IsEnabled)
                todoFooter.Text = total == 0 ? "" : "Click a to-do to edit · tick the circle when done";
        }

        // Re-sorts the list (open first, done last) with a FLIP animation: every row starts where it
        // was and glides to its new place. Debounced so ticking several in a row moves them together.
        DispatcherTimer todoReorder;
        readonly Dictionary<TodoItem, FrameworkElement> todoRowMap = new Dictionary<TodoItem, FrameworkElement>();
        void TodoReorderSoon()
        {
            if (todoReorder == null) { todoReorder = Timer(420, null); todoReorder.Tick += delegate { todoReorder.Stop(); TodoReorder(); }; }
            todoReorder.Stop(); todoReorder.Start();
        }

        void TodoReorder()
        {
            if (todoList == null || todoEditCancel != null) return;   // never yank a row that is being edited
            var before = new Dictionary<TodoItem, double>();
            foreach (var kv in todoRowMap)
            {
                try { before[kv.Key] = kv.Value.TranslatePoint(new Point(0, 0), todoList).Y; } catch { }
            }
            // nothing to do when the order on screen already matches
            var want = Store.Todos.Where(x => !x.Done).Concat(Store.Todos.Where(x => x.Done)).ToList();
            var shown = todoList.Children.OfType<FrameworkElement>().Select(r => r.Tag as TodoItem).Where(x => x != null).ToList();
            if (want.SequenceEqual(shown)) return;
            RefreshTodos(null);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                foreach (var kv in todoRowMap)
                {
                    double old;
                    if (!before.TryGetValue(kv.Key, out old)) continue;
                    double now;
                    try { now = kv.Value.TranslatePoint(new Point(0, 0), todoList).Y; } catch { continue; }
                    double dy = old - now;
                    if (Math.Abs(dy) < 0.5) continue;
                    var tt = new TranslateTransform(0, dy); kv.Value.RenderTransform = tt;
                    tt.BeginAnimation(TranslateTransform.YProperty, K.Anim(0, 460, K.OutCubic));
                }
            }));
        }

        void RefreshTodos(TodoItem fresh)
        {
            if (todoList == null) return;
            todoList.Children.Clear(); todoRowMap.Clear();
            // open first (in list order), finished ones sink to the bottom
            foreach (var t in Store.Todos.Where(x => !x.Done).Concat(Store.Todos.Where(x => x.Done)))
            {
                var row = TodoRow(t);
                row.Tag = t; todoRowMap[t] = row;
                todoList.Children.Add(row);
                if (t == fresh) K.FadeIn(row, -10, 320);
            }
            if (Store.Todos.Count == 0)
            {
                var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
                var ic = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Theme.GreenSoft, HorizontalAlignment = HorizontalAlignment.Center, Child = K.Glyph("", 16, Theme.Green) };
                var t1 = K.TB("All clear", 13.5, Theme.Text, FontWeights.SemiBold); t1.HorizontalAlignment = HorizontalAlignment.Center; t1.Margin = new Thickness(0, 10, 0, 0);
                var t2 = K.TB("Type above and press Enter to add a to-do", 11, Theme.Text3, FontWeights.Normal); t2.HorizontalAlignment = HorizontalAlignment.Center; t2.Margin = new Thickness(0, 2, 0, 0);
                empty.Children.Add(ic); empty.Children.Add(t1); empty.Children.Add(t2);
                todoList.Children.Add(empty);
                if (expanded) K.FadeIn(empty, 6, 300);
            }
            TodoStats(true);
            PagesClock();
        }

        static string Age(long ticks)
        {
            if (ticks <= 0) return "";
            var d = DateTime.Now - new DateTime(ticks);
            if (d.TotalMinutes < 1) return "now";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + "m";
            if (d.TotalDays < 1) return (int)d.TotalHours + "h";
            if (d.TotalDays < 7) return (int)d.TotalDays + "d";
            return new DateTime(ticks).ToString("MMM d", K.EN);
        }

        FrameworkElement TodoRow(TodoItem t)
        {
            var bg = new SolidColorBrush(Colors.Transparent);
            var row = new Border { Height = 40, CornerRadius = new CornerRadius(10), Background = bg, Padding = new Thickness(8, 0, 4, 0), Margin = new Thickness(0, 1, 0, 1) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.Child = g;

            var check = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.6), BorderBrush = t.Done ? Theme.Green : Theme.Text3, Background = t.Done ? (Brush)Theme.Green : Brushes.Transparent, Margin = new Thickness(0, 0, 12, 0), Cursor = Cursors.Hand };
            var cg = K.Glyph("", 10, Brushes.Black); cg.Visibility = t.Done ? Visibility.Visible : Visibility.Collapsed;
            check.Child = cg;

            var textHost = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.IBeam, Background = Brushes.Transparent };
            var tx = K.TB(t.Text, 13, t.Done ? Theme.Text3 : Theme.Text, FontWeights.Normal);
            tx.FontFamily = K.Font;
            var strike = new Rectangle { Height = 1.4, Fill = Theme.Text3, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) };
            textHost.Children.Add(tx); textHost.Children.Add(strike);
            Func<double> textW = () => { tx.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return tx.DesiredSize.Width; };
            strike.Width = t.Done ? textW() : 0;

            // right column: age, swapped for a delete button on hover
            var age = K.TB(Age(t.Created), 10.5, Theme.Text3, FontWeights.Medium); age.HorizontalAlignment = HorizontalAlignment.Right; age.Margin = new Thickness(0, 0, 6, 0);
            var del = K.IconBtn("", 28, 11, Theme.Text2, () =>
            {
                var tt = new TranslateTransform(); row.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.XProperty, K.Anim(36, 220, K.OutCubic));
                row.BeginAnimation(OpacityProperty, K.Anim(0, 200, K.OutCubic));
                var h = K.Anim(0, 240, K.OutCubic); h.BeginTime = TimeSpan.FromMilliseconds(K.Dur(120));
                h.Completed += delegate { Store.Todos.Remove(t); Store.SaveTodos(); RefreshTodos(null); };
                row.BeginAnimation(HeightProperty, h);
            });
            del.HorizontalAlignment = HorizontalAlignment.Right; del.Opacity = 0; del.ToolTip = "Delete";
            Grid.SetColumn(textHost, 1); Grid.SetColumn(age, 2); Grid.SetColumn(del, 2);
            g.Children.Add(check); g.Children.Add(textHost); g.Children.Add(age); g.Children.Add(del);

            row.MouseEnter += delegate
            {
                bg.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(Theme.A(Theme.Current.Text, 0.05), 140));
                del.BeginAnimation(OpacityProperty, K.Anim(1, 150, K.OutCubic)); age.BeginAnimation(OpacityProperty, K.Anim(0, 120, K.OutCubic));
            };
            row.MouseLeave += delegate
            {
                bg.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(Colors.Transparent, 220));
                del.BeginAnimation(OpacityProperty, K.Anim(0, 150, K.OutCubic)); age.BeginAnimation(OpacityProperty, K.Anim(1, 180, K.OutCubic));
            };

            Action toggle = () =>
            {
                t.Done = !t.Done; Store.SaveTodos();
                check.BorderBrush = t.Done ? Theme.Green : Theme.Text3;
                check.Background = t.Done ? (Brush)Theme.Green : Brushes.Transparent;
                cg.Visibility = t.Done ? Visibility.Visible : Visibility.Collapsed;
                K.PopIn(check, 0.7, 380);
                tx.Foreground = t.Done ? Theme.Text3 : Theme.Text;
                strike.BeginAnimation(WidthProperty, K.Anim(t.Done ? textW() : 0, 320, K.OutCubic));
                TodoStats(true);
                PagesClock();
                TodoReorderSoon();   // once the strike-through has played, done items glide to the bottom
            };
            check.MouseLeftButtonDown += (s, e) => e.Handled = true;
            check.MouseLeftButtonUp += (s, e) => { e.Handled = true; toggle(); };

            // inline edit: click the text; Enter/click away saves, Esc cancels, empty deletes
            textHost.MouseLeftButtonDown += (s, e) => e.Handled = true;
            textHost.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (textHost.Children.OfType<TextBox>().Any()) return;
                Pin();
                var tb = new TextBox { Text = t.Text, FontSize = 13, Foreground = Theme.Text, CaretBrush = Theme.Text, SelectionBrush = Theme.Accent, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Margin = new Thickness(-2, 0, 0, 0), MinWidth = 160, VerticalAlignment = VerticalAlignment.Center };
                tx.Visibility = Visibility.Hidden; strike.Visibility = Visibility.Hidden;
                textHost.HorizontalAlignment = HorizontalAlignment.Stretch;
                textHost.Children.Add(tb);
                bool closed = false;
                Action<bool> finish = save =>
                {
                    if (closed) return; closed = true; todoEditCancel = null;
                    string v = tb.Text.Trim();
                    textHost.Children.Remove(tb); tx.Visibility = Visibility.Visible; strike.Visibility = Visibility.Visible;
                    textHost.HorizontalAlignment = HorizontalAlignment.Left;
                    if (!save || v == t.Text) return;
                    if (v.Length == 0) { Store.Todos.Remove(t); Store.SaveTodos(); RefreshTodos(null); return; }
                    t.Text = v; Store.SaveTodos();
                    tx.Text = v; if (t.Done) strike.Width = textW();
                    PagesClock();
                };
                tb.PreviewKeyDown += (s2, e2) =>
                {
                    if (e2.Key == Key.Enter) { e2.Handled = true; finish(true); }
                    else if (e2.Key == Key.Escape) { e2.Handled = true; finish(false); }
                };
                tb.LostKeyboardFocus += delegate { finish(true); };
                todoEditCancel = () => finish(false);
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { tb.Focus(); Keyboard.Focus(tb); tb.SelectAll(); }));
            };
            return row;
        }


        // ================================================================ shelf
        // Header (count + total size, keep/copy/drag-all/clear), cards with Explorer thumbnails, a type
        // badge, middle-trimmed names and sizes; hover lifts a card; a drop zone that lights up while
        // files are dragged over the open shelf.
        Border shelfDragAll; TextBlock dragAllGlyph; readonly Dictionary<string, Border> shelfModeBtns = new Dictionary<string, Border>();
        StackPanel shelfPanel; ScrollViewer shelfScroll; Grid shelfEmpty; TextBlock shelfCount, shelfSub, keepGlyph, clearText; Border clearBtn; bool clearArmed;
        Rectangle shelfDropRing, shelfEmptyRing; TextBlock shelfEmptyTitle;
        readonly Dictionary<string, ImageSource> thumbCache = new Dictionary<string, ImageSource>();
        static readonly string[] ImgExt = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".jfif" };

        Grid BuildShelfPage()
        {
            var page = new Grid { Margin = new Thickness(14, 10, 14, 4) };
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            page.RowDefinitions.Add(new RowDefinition());

            var head = new Grid { Margin = new Thickness(6, 0, 0, 0) };
            // titles take what's left, the actions keep their size (no overlap; long subtitles get "…")
            head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, ToolTip = "Click a file to open it · right-click to show it in Explorer · drag it out to move/copy" };
            shelfCount = K.TB("Shelf", 12.5, Theme.Text, FontWeights.SemiBold);
            shelfSub = K.TB("", 10.5, Theme.Text3, FontWeights.Medium);
            titles.Children.Add(shelfCount); titles.Children.Add(shelfSub);
            var acts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var keep = K.IconBtn("", 28, 12, Theme.Text2, () => { S.ShelfKeepAfterDrag = !S.ShelfKeepAfterDrag; Store.Save(); UpdateShelfHeader(); }, out keepGlyph);
            keep.ToolTip = "Keep files on the shelf after dragging them out";
            var copy = K.IconBtn("", 28, 12, Theme.Text2, () =>
            {
                if (S.ShelfItems.Count == 0) return;
                CopyText(string.Join(Environment.NewLine, S.ShelfItems));
                shelfSub.Text = "Paths copied ✓"; shelfSub.Foreground = Theme.Green;
                var t = Timer(1600, null); t.Tick += delegate { t.Stop(); shelfSub.Foreground = Theme.Text3; UpdateShelfHeader(); }; t.Start();
            });
            copy.ToolTip = "Copy all paths";
            var dragAll = K.IconBtn("\uE7C2", 28, 12, Theme.Text, null, out dragAllGlyph); dragAll.Background = Theme.Raised; dragAll.Margin = new Thickness(2, 0, 0, 0);
            dragAll.Cursor = Cursors.SizeAll;

            shelfDragAll = dragAll;
            bool armed = false; Point st = new Point();
            dragAll.PreviewMouseLeftButtonDown += (s, e) => { armed = true; st = e.GetPosition(this); };
            dragAll.MouseMove += (s, e) =>
            {
                if (!armed || e.LeftButton != MouseButtonState.Pressed || S.ShelfItems.Count == 0) return;
                var p = e.GetPosition(this);
                if (Math.Abs(p.X - st.X) + Math.Abs(p.Y - st.Y) > 6) { armed = false; DoDragOut(S.ShelfItems.ToArray(), dragAll); }
            };
            dragAll.PreviewMouseLeftButtonUp += delegate { armed = false; };
            clearText = K.TB("Clear", 11.5, Theme.Text, FontWeights.SemiBold); clearText.HorizontalAlignment = HorizontalAlignment.Center;
            clearBtn = new Border { Height = 26, CornerRadius = new CornerRadius(13), Background = Theme.Raised, Child = clearText, Padding = new Thickness(11, 0, 11, 0), Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand };
            K.Pressable(clearBtn, () =>
            {
                if (!clearArmed)
                {
                    clearArmed = true; clearText.Text = "Clear all?"; clearBtn.Background = Theme.RedSoft; clearText.Foreground = Theme.Red;
                    var t = Timer(3000, null); t.Tick += delegate { t.Stop(); clearArmed = false; UpdateShelfHeader(); }; t.Start();
                }
                else { clearArmed = false; ClearShelf(); }
            }, false);
            acts.Children.Add(BuildShelfModeSwitch()); acts.Children.Add(keep); acts.Children.Add(copy); acts.Children.Add(dragAll); acts.Children.Add(clearBtn);
            titles.Margin = new Thickness(0, 0, 8, 0); Grid.SetColumn(acts, 1);
            head.Children.Add(titles); head.Children.Add(acts);
            page.Children.Add(head);

            shelfScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 8, 0, 0) }; ScrollFade.Attach(shelfScroll, 26, true);
            shelfPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            shelfScroll.Content = shelfPanel;
            shelfScroll.PreviewMouseWheel += (s, e) => { e.Handled = true; shelfScroll.ScrollToHorizontalOffset(shelfScroll.HorizontalOffset - e.Delta * 0.6); };
            // the cards and their drag-over ring share one cell (so the page's stagger-in animates the
            // pair, and never fades the hidden ring up)
            var shelfBody = new Grid();
            shelfBody.Children.Add(shelfScroll);
            // lights up around the cards while files are dragged over the open shelf
            shelfDropRing = new Rectangle { RadiusX = 16, RadiusY = 16, Stroke = Theme.Accent, StrokeThickness = 1.6, StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = Theme.AccentSoft, Margin = new Thickness(0, 6, 0, 2), Opacity = 0, IsHitTestVisible = false };
            shelfBody.Children.Add(shelfDropRing);
            Grid.SetRow(shelfBody, 1);
            page.Children.Add(shelfBody);

            shelfEmpty = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            shelfEmptyRing = new Rectangle { RadiusX = 16, RadiusY = 16, Stroke = Theme.Text3, StrokeThickness = 1.4, StrokeDashArray = new DoubleCollection { 4, 3 } };
            shelfEmpty.Children.Add(shelfEmptyRing);
            var es = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var ec = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Theme.Raised, HorizontalAlignment = HorizontalAlignment.Center, Child = K.Glyph("", 16, Theme.Text2) };
            shelfEmptyTitle = K.TB("Drop files here", 13, Theme.Text, FontWeights.SemiBold); shelfEmptyTitle.HorizontalAlignment = HorizontalAlignment.Center; shelfEmptyTitle.Margin = new Thickness(0, 8, 0, 0);
            var esub = K.TB("Drag files onto the island · they wait here until you drag them out", 10.5, Theme.Text3, FontWeights.Normal); esub.HorizontalAlignment = HorizontalAlignment.Center; esub.Margin = new Thickness(0, 2, 0, 0);
            es.Children.Add(ec); es.Children.Add(shelfEmptyTitle); es.Children.Add(esub);
            shelfEmpty.Children.Add(es);
            Grid.SetRow(shelfEmpty, 1);
            page.Children.Add(shelfEmpty);
            return page;
        }

        // drag-over feedback while the shelf page is open
        bool shelfGlow;
        void ShelfDropGlow(bool on)
        {
            if (shelfDropRing == null || on == shelfGlow) return;
            shelfGlow = on;
            bool empty = S.ShelfItems.Count == 0;
            shelfEmptyRing.Stroke = on && empty ? Theme.Accent : Theme.Text3;
            shelfEmptyRing.Fill = on && empty ? Theme.AccentSoft : Brushes.Transparent;
            shelfEmptyTitle.Text = on && empty ? "Release to add" : "Drop files here";
            shelfDropRing.BeginAnimation(OpacityProperty, K.Anim(on && !empty ? 1 : 0, on ? 160 : 240, K.OutCubic));
        }

        // Copy | Move: what dragging a file out of the shelf does. Shown right in the header so it's
        // never a surprise; the choice is handed to Explorer as the preferred drop effect.
        bool ShelfMove { get { return S.ShelfDragMode == "move"; } }

        FrameworkElement BuildShelfModeSwitch()
        {
            var track = new Border { Height = 26, CornerRadius = new CornerRadius(13), Padding = new Thickness(2), Background = K.Solid(Theme.A(Theme.Current.Text, 0.06)), Margin = new Thickness(0, 0, 4, 0), ToolTip = "What dragging a file out does" };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var m in new[] { new[] { "copy", "\uE8C8", "Copy" }, new[] { "move", "\uE8DE", "Move" } })
            {
                string mode = m[0];
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(K.Glyph(m[1], 10, Theme.Text2));
                var l = K.TB(m[2], 11, Theme.Text2, FontWeights.SemiBold); l.Margin = new Thickness(5, 0, 0, 0);
                sp.Children.Add(l);
                var b = new Border { Height = 22, CornerRadius = new CornerRadius(11), Padding = new Thickness(8, 0, 9, 0), Background = new SolidColorBrush(Colors.Transparent), Child = sp, Cursor = Cursors.Hand };
                K.Pressable(b, () => { if (S.ShelfDragMode == mode) return; S.ShelfDragMode = mode; Store.Save(); UpdateShelfHeader(); }, false);
                shelfModeBtns[mode] = b;
                row.Children.Add(b);
            }
            track.Child = row;
            return track;
        }

        void UpdateShelfHeader()
        {
            foreach (var kv in shelfModeBtns)
            {
                bool on = (kv.Key == "move") == ShelfMove;
                ((SolidColorBrush)kv.Value.Background).BeginAnimation(SolidColorBrush.ColorProperty, CAnim(on ? (kv.Key == "move" ? Theme.A(Theme.Current.Orange, 0.22) : Theme.Current.Hover) : Colors.Transparent, 200));
                foreach (var tb in ((StackPanel)kv.Value.Child).Children.OfType<TextBlock>())
                    tb.Foreground = on ? (kv.Key == "move" ? Theme.Orange : Theme.Text) : Theme.Text3;
            }
            if (shelfDragAll != null) shelfDragAll.ToolTip = (ShelfMove ? "Move" : "Copy") + " every file at once (drag me)";
            if (shelfPanel != null)
                foreach (var tile in shelfPanel.Children.OfType<Border>())
                    tile.ToolTip = (string)tile.Tag + "\n" + (ShelfMove ? "Drag out to move" : "Drag out to copy") + " · click to open · right-click to show in Explorer";
            int n = S.ShelfItems.Count;
            shelfCount.Text = n == 0 ? "Shelf" : "Shelf · " + n + (n == 1 ? " item" : " items");
            if (shelfSub.Foreground == Theme.Text3 || shelfSub.Text.Length == 0)
            {
                double bytes = 0; int folders = 0;
                foreach (var p in S.ShelfItems) { try { if (Directory.Exists(p)) folders++; else bytes += new FileInfo(p).Length; } catch { } }
                shelfSub.Text = n == 0 ? "Empty" : K.Bytes(bytes) + (folders > 0 ? " · " + folders + (folders == 1 ? " folder" : " folders") : "");
                shelfSub.Foreground = Theme.Text3;
            }
            keepGlyph.Foreground = S.ShelfKeepAfterDrag ? (Brush)Theme.Accent : Theme.Text2;
            if (!clearArmed) { clearText.Text = "Clear"; clearBtn.Background = Theme.Raised; clearText.Foreground = Theme.Text; }
        }

        ImageSource Thumb(string path)
        {
            ImageSource src;
            if (thumbCache.TryGetValue(path, out src)) return src;
            string ext = (System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (ImgExt.Contains(ext)) src = WallpaperService.Thumb(path, 160);
            if (src == null) src = Native.ShellThumb(path, 96);
            if (src == null) src = Native.FileIcon(path);
            thumbCache[path] = src;
            return src;
        }

        // "a-very-long-file-name.txt" -> "a-very-lo…name.txt" (keeps the extension readable)
        static string MiddleTrim(string s, int max)
        {
            if (s.Length <= max) return s;
            int tail = Math.Min(8, max / 2), head = max - tail - 1;
            return s.Substring(0, head) + "…" + s.Substring(s.Length - tail);
        }

        Border MakeTile(string path)
        {
            var thumb = Thumb(path);
            string ext = (System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant();
            bool isDir = Directory.Exists(path), isImg = ImgExt.Contains(ext) && thumb != null;
            var bg = new SolidColorBrush(Theme.A(Theme.Current.Text, 0.04));
            var b = new Border { Width = 86, Height = 116, CornerRadius = new CornerRadius(14), Margin = new Thickness(3, 0, 3, 0), Background = bg, Cursor = Cursors.Hand, Tag = path, ToolTip = path + "\n" + (ShelfMove ? "Drag out to move" : "Drag out to copy") + " · click to open · right-click to show in Explorer", RenderTransformOrigin = new Point(0.5, 0.5) };
            var lift = new ScaleTransform(1, 1); b.RenderTransform = lift;
            var g = new Grid();
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };

            var vis = new Grid { Width = 58, Height = 58, HorizontalAlignment = HorizontalAlignment.Center };
            if (isImg)
            {
                var ib = new ImageBrush(thumb) { Stretch = Stretch.UniformToFill }; ib.Freeze();
                vis.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = ib });
            }
            else if (thumb != null)
            {
                var im = new Image { Source = thumb, Stretch = Stretch.Uniform, Width = 52, Height = 52 };
                RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                vis.Children.Add(im);
            }
            else vis.Children.Add(K.Glyph(isDir ? "" : "", 30, Theme.Text2));
            // type badge (not for folders)
            if (!isDir && ext.Length > 1)
            {
                var badge = new Border { CornerRadius = new CornerRadius(5), Background = Theme.Surface, BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), Padding = new Thickness(4, 0, 4, 1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -6, -3) };
                badge.Child = K.TB(ext.Substring(1).ToUpperInvariant(), 8.5, Theme.Text2, FontWeights.Bold);
                vis.Children.Add(badge);
            }
            sp.Children.Add(vis);

            string fname = System.IO.Path.GetFileName(path.TrimEnd('\\'));
            var name = K.TB(MiddleTrim(fname, 13), 10.5, Theme.Text, FontWeights.Medium);
            name.HorizontalAlignment = HorizontalAlignment.Center; name.Margin = new Thickness(6, 9, 6, 0);
            string size = "";
            try { size = isDir ? "Folder" : K.Bytes(new FileInfo(path).Length); } catch { }
            var meta = K.TB(size, 9.5, Theme.Text3, FontWeights.Normal); meta.HorizontalAlignment = HorizontalAlignment.Center; meta.Margin = new Thickness(0, 1, 0, 0);
            sp.Children.Add(name); sp.Children.Add(meta);
            g.Children.Add(sp);

            var x = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Theme.Surface, BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0), Opacity = 0, Child = K.Glyph("", 8, Theme.Text), Cursor = Cursors.Hand, ToolTip = "Remove from shelf" };
            x.MouseLeftButtonDown += (s, e) => e.Handled = true;
            x.MouseLeftButtonUp += (s, e) => { e.Handled = true; RemoveShelf(path, b); };
            g.Children.Add(x);
            b.Child = g;

            b.MouseEnter += delegate
            {
                bg.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(Theme.A(Theme.Current.Text, 0.09), 140));
                K.Pop(lift, 1.04, 260, K.OutBack);
                x.BeginAnimation(OpacityProperty, K.Anim(1, 140, K.OutCubic));
            };
            b.MouseLeave += delegate
            {
                bg.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(Theme.A(Theme.Current.Text, 0.04), 220));
                K.Pop(lift, 1, 260, K.OutCubic);
                x.BeginAnimation(OpacityProperty, K.Anim(0, 160, K.OutCubic));
            };
            bool armed = false, moved = false; Point start = new Point();
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; armed = true; moved = false; start = e.GetPosition(this); K.Pop(lift, 0.96, 90, K.OutQuint); };
            b.MouseMove += (s, e) =>
            {
                if (!armed || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(this);
                if (Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) > 6)
                {
                    armed = false; moved = true;
                    b.Opacity = 0.4;   // the card stays ghosted while it's being dragged
                    DoDragOut(new[] { path }, b);
                    b.BeginAnimation(OpacityProperty, K.Anim(1, 200, K.OutCubic));
                }
            };
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; K.Pop(lift, b.IsMouseOver ? 1.04 : 1, 300, K.OutBack); if (armed && !moved) Launcher.Open(path); armed = false; };
            b.MouseRightButtonUp += (s, e) => { e.Handled = true; try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { } };
            return b;
        }

        void RefreshShelf(ICollection<string> fresh)
        {
            if (shelfPanel == null) return;
            S.ShelfItems.RemoveAll(p => !(File.Exists(p) || Directory.Exists(p)));
            shelfPanel.Children.Clear();
            int i = 0;
            foreach (var p in S.ShelfItems)
            {
                var t = MakeTile(p);
                shelfPanel.Children.Add(t);
                if (fresh != null && fresh.Contains(p))
                {
                    var st = (ScaleTransform)t.RenderTransform; st.ScaleX = st.ScaleY = 0.4; t.Opacity = 0;
                    var delay = TimeSpan.FromMilliseconds(K.Dur(60 * i++));
                    var a = K.Anim(1, 520, K.Elastic); a.BeginTime = delay;
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                    var o = K.Anim(1, 200, K.OutQuint); o.BeginTime = delay;
                    t.BeginAnimation(OpacityProperty, o);
                }
            }
            bool empty = S.ShelfItems.Count == 0;
            shelfEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            shelfScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            shelfGlow = true; ShelfDropGlow(false);   // reset both drop visuals
            UpdateShelfHeader();
            if (fresh != null && fresh.Count > 0) shelfScroll.ScrollToRightEnd();
            if (curView == vIdle) UpdateIdle();
        }

        void ShelfAdded(List<string> added) { RefreshShelf(added); }

        void RemoveShelf(string path, Border tile)
        {
            var st = tile.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1); tile.RenderTransform = st;
            var a = K.Anim(0.3, 180, K.OutQuint);
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            tile.BeginAnimation(OpacityProperty, K.Anim(0, 180, K.OutQuint));
            // then the gap closes
            var w = K.Anim(0, 220, K.OutCubic); w.BeginTime = TimeSpan.FromMilliseconds(K.Dur(140));
            w.Completed += delegate { S.ShelfItems.Remove(path); Store.Save(); RefreshShelf(null); };
            tile.BeginAnimation(WidthProperty, w);
        }

        public void ClearShelf() { S.ShelfItems.Clear(); Store.Save(); RefreshShelf(null); }

        void DoDragOut(string[] paths, DependencyObject src)
        {
            dragOut = true;
            DragDropEffects eff = DragDropEffects.None;
            try
            {
                var data = new DataObject(DataFormats.FileDrop, paths);
                // Explorer follows "Preferred DropEffect" instead of guessing from the drive; copy stays
                // allowed in move mode so browsers/chat apps (copy-only targets) still accept the file
                data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(ShelfMove ? 2 : 1)));
                eff = DragDrop.DoDragDrop(src, data, ShelfMove ? DragDropEffects.Move | DragDropEffects.Copy : DragDropEffects.Copy);
            }
            catch { }
            dragOut = false;
            bool changed = false;
            foreach (var p in paths)
            {
                bool gone = !(File.Exists(p) || Directory.Exists(p));
                if (gone || eff == DragDropEffects.Move || (eff != DragDropEffects.None && !S.ShelfKeepAfterDrag)) { S.ShelfItems.Remove(p); changed = true; }
            }
            if (changed) { Store.Save(); RefreshShelf(null); }
            // say what actually happened
            int movedN = paths.Count(p => !(File.Exists(p) || Directory.Exists(p)));
            int n = paths.Length;
            string msg = movedN > 0 ? "Moved " + movedN + (movedN == 1 ? " file" : " files")
                : eff != DragDropEffects.None ? (eff == DragDropEffects.Move ? "Moved " : "Copied ") + n + (n == 1 ? " file" : " files") : null;
            if (msg != null && shelfSub != null)
            {
                shelfSub.Text = msg + " ✓"; shelfSub.Foreground = Theme.Green;
                var t = Timer(2200, null); t.Tick += delegate { t.Stop(); shelfSub.Foreground = Theme.Text3; UpdateShelfHeader(); }; t.Start();
            }
            if (expanded && !CursorInIsland(6) && expandedByHover) leaveTimer.Start();
        }


        // ================================================================ system
        // Three gauges (CPU / Memory / Disk) that turn orange/red under load, a full-width CPU history
        // chart with a 50% guide, and chips for uptime, processes and virtual memory.
        Ring sCpuRing, sRamRing, sDiskRing; TextBlock sCpuPct, sRamPct, sDiskPct, sCpuSub, sRamSub, sDiskSub, sUpChip, sProcChip, sComChip, sChartNow;
        Canvas sChart; Polyline sLine; Polygon sArea; Line sGuide; DispatcherTimer statsTimer;

        // accent while relaxed, orange when busy, red when nearly full
        static Color LoadColor(double f) { return f >= 0.9 ? Theme.Current.Red : f >= 0.7 ? Theme.Current.Orange : Theme.Current.Accent; }

        Grid BuildSystemPage()
        {
            var g = new Grid();
            var sp = new StackPanel { Margin = new Thickness(20, 12, 20, 0) };

            // gauges
            var gauges = new UniformGrid { Rows = 1, Columns = 3 };
            Func<string, Ring[], TextBlock[], FrameworkElement> gauge = (name, ring, txt) =>
            {
                var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var host = new Grid { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center };
                ring[0] = new Ring(64, 6, Theme.Accent);
                txt[0] = K.TB("0%", 15, Theme.Text, FontWeights.SemiBold); txt[0].HorizontalAlignment = HorizontalAlignment.Center; K.Tabular(txt[0]);
                host.Children.Add(ring[0]); host.Children.Add(txt[0]);
                var l = K.TB(name, 11, Theme.Text, FontWeights.SemiBold); l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 6, 0, 0);
                txt[1] = K.TB("", 10, Theme.Text3, FontWeights.Medium); txt[1].HorizontalAlignment = HorizontalAlignment.Center; K.Tabular(txt[1]);
                cell.Children.Add(host); cell.Children.Add(l); cell.Children.Add(txt[1]);
                return cell;
            };
            var r = new Ring[1]; var tx = new TextBlock[2];
            gauges.Children.Add(gauge("CPU", r, tx)); sCpuRing = r[0]; sCpuPct = tx[0]; sCpuSub = tx[1];
            gauges.Children.Add(gauge("Memory", r, tx)); sRamRing = r[0]; sRamPct = tx[0]; sRamSub = tx[1];
            gauges.Children.Add(gauge("Disk " + System.IO.Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\'), r, tx)); sDiskRing = r[0]; sDiskPct = tx[0]; sDiskSub = tx[1];
            sp.Children.Add(gauges);

            // CPU history
            var ch = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            ch.Children.Add(K.TB("CPU · last minute", 10, Theme.Text3, FontWeights.SemiBold));
            sChartNow = K.TB("", 10, Theme.Text3, FontWeights.SemiBold); sChartNow.HorizontalAlignment = HorizontalAlignment.Right; K.Tabular(sChartNow);
            ch.Children.Add(sChartNow);
            sp.Children.Add(ch);
            var chartBox = new Border { Height = 40, Margin = new Thickness(0, 4, 0, 0), CornerRadius = new CornerRadius(8), Background = K.Solid(Theme.A(Theme.Current.Text, 0.04)), ClipToBounds = true };
            sChart = new Canvas { ClipToBounds = true };
            var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fill.GradientStops.Add(new GradientStop(Theme.A(Theme.Current.Accent, 0.35), 0));
            fill.GradientStops.Add(new GradientStop(Theme.A(Theme.Current.Accent, 0.02), 1));
            sArea = new Polygon { Fill = fill };
            sLine = new Polyline { Stroke = Theme.Accent, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
            sGuide = new Line { Stroke = Theme.Hairline, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } };
            sChart.Children.Add(sGuide); sChart.Children.Add(sArea); sChart.Children.Add(sLine);
            chartBox.Child = sChart;
            sChart.SizeChanged += delegate { DrawChart(); };
            sp.Children.Add(chartBox);

            // chips
            var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            Func<string, TextBlock> chip = glyph =>
            {
                var c = new StackPanel { Orientation = Orientation.Horizontal };
                c.Children.Add(K.Glyph(glyph, 10, Theme.Text2));
                var t = K.TB("", 10.5, Theme.Text2, FontWeights.Medium); t.Margin = new Thickness(5, 0, 0, 0);
                c.Children.Add(t);
                chips.Children.Add(new Border { Height = 22, CornerRadius = new CornerRadius(11), Background = Theme.Raised, Padding = new Thickness(9, 0, 10, 0), Margin = new Thickness(0, 0, 6, 0), Child = c });
                return t;
            };
            sUpChip = chip(""); sProcChip = chip(""); sComChip = chip("");
            ((Border)((StackPanel)sComChip.Parent).Parent).ToolTip = "Committed memory: RAM in use plus what's parked in the page file";
            sp.Children.Add(chips);

            g.Children.Add(sp);
            statsTimer = Timer(1500, delegate { SampleStats(); });
            return g;
        }

        void StartStats() { SampleStats(); statsTimer.Start(); }
        void StopStats() { if (statsTimer != null) statsTimer.Stop(); }

        // Sampling (process count, WMI, disk) happens off the UI thread; only drawing happens here.
        bool sampling;
        void SampleStats()
        {
            if (sampling) return;
            sampling = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Svc.Stats.Sample(); } catch { }
                Dispatcher.BeginInvoke(new Action(() => { sampling = false; DrawStats(); }));
            });
        }

        void DrawStats()
        {
            var st = Svc.Stats;
            Action<Ring, TextBlock, double> gauge = (ring, pct, f) =>
            {
                f = Math.Max(0, Math.Min(1, f));
                ring.AnimateTo(f, 700);
                var c = K.Solid(LoadColor(f));
                ring.Color = c;
                pct.Text = (int)Math.Round(f * 100) + "%";
                pct.Foreground = f >= 0.7 ? c : Theme.Text;
            };
            gauge(sCpuRing, sCpuPct, st.Cpu);
            sCpuSub.Text = double.IsNaN(st.TempC) ? (st.Cpu >= 0.7 ? "Busy" : "Normal") : Math.Round(st.TempC) + "°C";
            sCpuSub.Foreground = !double.IsNaN(st.TempC) && st.TempC >= 85 ? Theme.Red : !double.IsNaN(st.TempC) && st.TempC >= 70 ? Theme.Orange : Theme.Text3;
            gauge(sRamRing, sRamPct, st.MemTotal > 0 ? (double)st.MemUsed / st.MemTotal : 0);
            sRamSub.Text = K.Bytes(st.MemUsed) + " / " + K.Bytes(st.MemTotal);
            gauge(sDiskRing, sDiskPct, st.DiskTotal > 0 ? (double)st.DiskUsed / st.DiskTotal : 0);
            sDiskSub.Text = K.Bytes(st.DiskTotal - st.DiskUsed) + " free";
            var up = st.Uptime;
            sUpChip.Text = "Up " + (up.Days > 0 ? up.Days + "d " : "") + up.Hours + "h " + up.Minutes + "m";
            sProcChip.Text = st.Procs + " processes";
            sComChip.Text = "Virtual " + K.Bytes(st.CommitUsed) + " / " + K.Bytes(st.CommitTotal);
            sChartNow.Text = "now " + (int)Math.Round(st.Cpu * 100) + "%";
            DrawChart();
        }

        void DrawChart()
        {
            if (sChart == null) return;
            var st = Svc.Stats;
            double w = sChart.ActualWidth, h = sChart.ActualHeight;
            if (w <= 0 || h <= 0) return;
            sGuide.X1 = 0; sGuide.X2 = w; sGuide.Y1 = sGuide.Y2 = Math.Round(h / 2) + 0.5;
            var pts = new PointCollection(); var area = new PointCollection();
            int n = st.History.Count;
            if (n == 0) { sLine.Points = pts; sArea.Points = area; return; }
            double step = w / 39.0;
            area.Add(new Point(w - (n - 1) * step, h));
            for (int i = 0; i < n; i++)
            {
                var p = new Point(w - (n - 1 - i) * step, h - 3 - Math.Max(0, Math.Min(1, st.History[i])) * (h - 6));
                pts.Add(p); area.Add(p);
            }
            area.Add(new Point(w, h));
            sLine.Points = pts; sArea.Points = area;
            var c = LoadColor(st.Cpu);
            sLine.Stroke = K.Solid(c);
            var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fill.GradientStops.Add(new GradientStop(Theme.A(c, 0.35), 0));
            fill.GradientStops.Add(new GradientStop(Theme.A(c, 0.02), 1));
            fill.Freeze();
            sArea.Fill = fill;
        }

        // ================================================================ AI
        Grid aiOverlay; StackPanel aiProviders, aiList, aiEmpty, aiEmptyBtns, aiOverlayList; TextBlock aiModelText, aiMethodText, aiEmptyTitle, aiEmptySub, aiSendGlyph, aiExpandGlyph, aiWarnText;
        Border aiEmptyHalo; WrapPanel aiSuggest; SolidColorBrush aiBarBorder, aiSendBg; bool aiSendLit;
        readonly Dictionary<string, Border> aiProvBtns = new Dictionary<string, Border>();
        ScrollViewer aiScroll; TextBox aiInput; Border aiWarn, aiEmptyIcon; Border aiStreamBubble; readonly StringBuilder aiStreamText = new StringBuilder();
        DispatcherTimer aiRenderTimer; bool aiRenderDirty; Border aiThinkingRow;
        WrapPanel aiAttachStrip; readonly List<string> aiPending = new List<string>();
        bool aiPicking;   // file dialog open: the island must not collapse under it

        Grid BuildAiPage()
        {
            var g = new Grid { Margin = new Thickness(14, 10, 14, 6) };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new Grid();
            aiProviders = new StackPanel { Orientation = Orientation.Horizontal };
            // providers as a segmented control: a soft track, the active one raised
            var provTrack = new Border { CornerRadius = new CornerRadius(15), Padding = new Thickness(2), Background = K.Solid(Theme.A(Theme.Current.Text, 0.06)), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Child = aiProviders };
            head.Children.Add(provTrack);
            var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
            aiModelText = K.TB("", 11.5, Theme.Text, FontWeights.SemiBold);
            var modelChip = Chip(aiModelText, true, () => AiShowModels());
            aiMethodText = K.TB("", 11.5, Theme.Text2, FontWeights.SemiBold);
            var methodChip = Chip(aiMethodText, false, AiCycleMethod);
            methodChip.Margin = new Thickness(6, 0, 0, 0);
            chips.Children.Add(modelChip); chips.Children.Add(methodChip);
            head.Children.Add(chips);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            var keyBtn = K.IconBtn("", 28, 12, Theme.Text2, AiConnect); keyBtn.ToolTip = "Sign in / API key";
            var usageBtn = K.IconBtn("", 28, 12, Theme.Text2, AiShowUsage); usageBtn.ToolTip = "Usage limits";
            var bigBtn = K.IconBtn("", 28, 12, Theme.Text2, AiToggleBig, out aiExpandGlyph); bigBtn.ToolTip = "Bigger chat";
            var newBtn = K.IconBtn("", 28, 12, Theme.Text2, AiNewChat); newBtn.ToolTip = "New chat";
            foreach (var b in new[] { keyBtn, usageBtn, bigBtn, newBtn }) right.Children.Add(b);
            head.Children.Add(right);
            g.Children.Add(head);

            aiWarn = new Border { CornerRadius = new CornerRadius(8), Background = Theme.OrangeSoft, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            aiWarnText = K.TB("", 11.5, Theme.Orange, FontWeights.SemiBold); aiWarnText.TextWrapping = TextWrapping.Wrap;
            aiWarn.Child = aiWarnText;
            Grid.SetRow(aiWarn, 1);
            g.Children.Add(aiWarn);

            aiScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Margin = new Thickness(0, 6, 0, 8) };
            // messages fade out under the header (and above the input) instead of being cut off hard
            ScrollFade.Attach(aiScroll, 28, false);
            aiList = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 10, 0, 0) };
            aiScroll.Content = aiList;
            Grid.SetRow(aiScroll, 2);
            g.Children.Add(aiScroll);

            aiEmpty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            // brand-coloured icon inside a soft halo ring
            aiEmptyIcon = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = K.Glyph("\uE8BD", 18, Brushes.White) };
            aiEmptyHalo = new Border { Width = 60, Height = 60, CornerRadius = new CornerRadius(30), Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Center, Child = aiEmptyIcon };
            aiEmptyTitle = K.TB("", 15, Theme.Text, FontWeights.SemiBold); aiEmptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
            aiEmptySub = K.TB("", 11, Theme.Text2, FontWeights.Normal); aiEmptySub.HorizontalAlignment = HorizontalAlignment.Center; aiEmptySub.Margin = new Thickness(0, 3, 0, 0);
            aiEmptyBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
            // starter prompts (fill the input, don't send)
            aiSuggest = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0), MaxWidth = 380 };
            foreach (var s in new[] { new[] { "\uE8C8", "Summarize my clipboard" }, new[] { "\uE775", "Translate to English" }, new[] { "\uE70F", "Write a short email" }, new[] { "\uE82F", "Explain like I'm 5" } })
            {
                string prompt = s[1];
                var chip = K.GlyphPill(s[0], prompt, Theme.Raised, Theme.Text2, 28, () => AiUseSuggestion(prompt));
                chip.Margin = new Thickness(3);
                aiSuggest.Children.Add(chip);
            }
            aiEmpty.Children.Add(aiEmptyHalo); aiEmpty.Children.Add(aiEmptyTitle); aiEmpty.Children.Add(aiEmptySub); aiEmpty.Children.Add(aiEmptyBtns); aiEmpty.Children.Add(aiSuggest);
            Grid.SetRow(aiEmpty, 2);
            g.Children.Add(aiEmpty);

            aiBarBorder = new SolidColorBrush(Theme.A(Theme.Current.Text, 0.06));
            var bar = new Border { CornerRadius = new CornerRadius(20), Background = Theme.Raised, MinHeight = 40, BorderThickness = new Thickness(1), BorderBrush = aiBarBorder };
            var bg = new Grid();
            bg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bg.ColumnDefinitions.Add(new ColumnDefinition());
            bg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bg.RowDefinitions.Add(new RowDefinition());
            // photos waiting to be sent: a row of thumbnails above the text, inside the bar
            aiAttachStrip = new WrapPanel { Margin = new Thickness(8, 8, 8, 0), Visibility = Visibility.Collapsed };
            Grid.SetColumnSpan(aiAttachStrip, 3);
            bg.Children.Add(aiAttachStrip);
            var attach = K.IconBtn("", 32, 13, Theme.Text2, AiPickImages);
            attach.ToolTip = "Add photo (or paste with Ctrl+V)";
            attach.Margin = new Thickness(3, 3, 0, 3); attach.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetRow(attach, 1);
            bg.Children.Add(attach);
            aiInput = PinnedTextBox(new TextBox { TextWrapping = TextWrapping.Wrap, MaxHeight = 96, AcceptsReturn = false, VerticalAlignment = VerticalAlignment.Center });
            var field = K.Field(aiInput, "Ask anything…", 0, Brushes.Transparent);
            aiInput.PreviewKeyDown += (s, e) =>
            {
                // Ctrl+V / Shift+Insert with a picture (or image files) on the clipboard attaches it
                if ((e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0) || (e.Key == Key.Insert && (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
                {
                    if (AiPasteImage()) e.Handled = true;
                    return;
                }
                if (e.Key == Key.Back && aiInput.Text.Length == 0 && aiPending.Count > 0) { e.Handled = true; AiRemovePending(aiPending[aiPending.Count - 1]); return; }
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { int i = aiInput.CaretIndex; aiInput.Text = aiInput.Text.Insert(i, "\n"); aiInput.CaretIndex = i + 1; }
                else AiSend();
            };
            aiSendBg = new SolidColorBrush(Theme.Current.Hover);
            var send = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = aiSendBg, Margin = new Thickness(0, 3, 3, 3), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Bottom };
            aiSendGlyph = K.Glyph("", 13, Theme.Text3); send.Child = aiSendGlyph;
            K.Pressable(send, AiSend, false);
            // send lights up only when there's something to send (or a reply to stop); the bar outline
            // picks up the accent while the input has focus
            aiInput.TextChanged += delegate { AiSendState(); };
            aiInput.GotKeyboardFocus += delegate { aiBarBorder.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(Theme.A(Theme.Current.Accent, 0.55), 200)); };
            aiInput.LostKeyboardFocus += delegate { aiBarBorder.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(Theme.A(Theme.Current.Text, 0.06), 260)); };
            Grid.SetColumn(field, 1); Grid.SetRow(field, 1); field.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(send, 2); Grid.SetRow(send, 1);
            bg.Children.Add(field); bg.Children.Add(send);
            bar.Child = bg;
            Grid.SetRow(bar, 3);
            g.Children.Add(bar);

            aiOverlay = new Grid { Visibility = Visibility.Collapsed, Background = Brushes.Transparent };
            Grid.SetRowSpan(aiOverlay, 4);
            aiOverlay.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == aiOverlay) { e.Handled = true; AiCloseOverlay(); } };
            var panel = new Border { CornerRadius = new CornerRadius(14), Background = Theme.Surface, BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 60, 0, 0), Width = 250, MaxHeight = 290, Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.5 } };
            var psv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden }; ScrollFade.Attach(psv, 18, false);
            aiOverlayList = new StackPanel { Margin = new Thickness(6) };
            psv.Content = aiOverlayList; panel.Child = psv;
            aiOverlay.Children.Add(panel);
            g.Children.Add(aiOverlay);

            aiRenderTimer = Timer(70, delegate { aiRenderTimer.Stop(); if (aiRenderDirty) AiRenderStream(); });
            Svc.Ai.StatusChanged += delegate { AiRefreshHeader(); };
            return g;
        }

        Border Chip(TextBlock t, bool caret, Action click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(t);
            if (caret) sp.Children.Add(new TextBlock { Text = "  ", FontFamily = K.ICO, FontSize = 8, Foreground = Theme.Text2, VerticalAlignment = VerticalAlignment.Center });
            var b = new Border { Height = 26, CornerRadius = new CornerRadius(13), Background = Theme.Raised, Padding = new Thickness(10, 0, 10, 0), Child = sp, Cursor = Cursors.Hand };
            K.Pressable(b, click, false);
            return b;
        }

        void AiRefreshHeader()
        {
            if (aiProviders == null) return;
            // persistent buttons so switching provider animates instead of rebuilding
            if (aiProvBtns.Count == 0)
            {
                foreach (var p in AiService.Providers)
                {
                    string pv = p;
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = K.Solid(AiService.Brand(pv)), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                    sp.Children.Add(K.TB(AiService.Label(pv), 11.5, Theme.Text2, FontWeights.SemiBold));
                    var b = new Border { Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Colors.Transparent), Padding = new Thickness(9, 0, 10, 0), Child = sp, Cursor = Cursors.Hand };
                    K.Pressable(b, () => { if (S.AiProvider == pv) return; S.AiProvider = pv; Store.Save(); AiRefreshHeader(); AiCheckUsage(); }, false);
                    aiProvBtns[pv] = b; aiProviders.Children.Add(b);
                }
            }
            foreach (var kv in aiProvBtns)
            {
                bool on = S.AiProvider == kv.Key;
                var sp = (StackPanel)kv.Value.Child;
                sp.Children[0].BeginAnimation(OpacityProperty, K.Anim(on ? 1 : 0.5, 200, K.OutCubic));
                ((TextBlock)sp.Children[1]).Foreground = on ? Theme.Text : Theme.Text2;
                var bg = kv.Value.Background as SolidColorBrush;
                if (bg == null || bg.IsFrozen) { bg = new SolidColorBrush(Colors.Transparent); kv.Value.Background = bg; }
                bg.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(on ? Theme.Current.Hover : Theme.A(Theme.Current.Hover, 0), 220));
            }
            string prov = S.AiProvider, method = Svc.Ai.MethodFor(prov), model = Svc.Ai.ModelFor(prov);
            var known = AiService.StaticModels[prov].FirstOrDefault(m => m[0] == model);
            aiModelText.Text = model.Length == 0 ? "Default" : known != null ? known[1] : model;
            aiMethodText.Text = S.AiMethod == "auto" ? "Auto · " + (method == "cli" ? "Browser" : "Key") : method == "cli" ? "Browser" : "Key";
            aiExpandGlyph.Text = aiBig ? "" : "";
            aiEmptyIcon.Background = K.Solid(AiService.Brand(prov));
            aiEmptyHalo.Background = K.Solid(Theme.A(AiService.Brand(prov), 0.18));
            string n = (S.UserName ?? "").Trim();
            aiEmptyTitle.Text = (n.Length > 0 ? "Hi " + n + "! " : "") + "Ask " + AiService.Label(prov) + " anything";
            var st = Svc.Ai.GetStatus(prov);
            bool ready = method == "cli" ? st.SignedIn : st.Key;
            aiEmptySub.Text = ready ? (method == "cli" ? "Signed in with browser" : "Using API key") + " · " + aiModelText.Text + " · Enter to send" : st.Cli ? "Not connected to " + AiService.Label(prov) + " yet" : "Needs the free " + AiService.CliName(prov) + " CLI · installs in one click";
            aiEmptyBtns.Children.Clear();
            if (!ready)
            {
                var a = K.GlyphPill("", st.Cli ? "Install & sign in" : "Sign in with browser", Theme.AccentFill, Theme.OnAccent, 30, () => Svc.Ai.SignIn(prov));
                var b = K.PillBtn("Use an API key", Theme.Raised, Theme.Text, 30, () => OpenSettings("ai")); b.Margin = new Thickness(8, 0, 0, 0);
                aiEmptyBtns.Children.Add(a); aiEmptyBtns.Children.Add(b);
            }
            aiSuggest.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
            AiEmptyVisibility();
        }

        void AiEmptyVisibility()
        {
            // the list itself counts: on a fresh chat the first bubble + thinking row are added before
            // the message reaches the history, and they must show (and animate) right away
            bool empty = Svc.Ai.Chat.Messages.Count == 0 && !Svc.Ai.Busy && aiList.Children.Count == 0;
            bool wasEmpty = aiEmpty.Visibility == Visibility.Visible;
            aiEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            aiScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (wasEmpty && !empty) { aiScroll.Opacity = 0; aiScroll.BeginAnimation(OpacityProperty, K.Anim(1, 220, K.OutCubic)); }
        }

        bool aiRendered;
        void AiOnShown()
        {
            if (!aiRendered) { AiRenderAll(); aiRendered = true; }
            AiRefreshHeader();
            AiCheckUsage();
            if (pinned) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { aiInput.Focus(); Keyboard.Focus(aiInput); }));
        }

        void AiRenderAll()
        {
            aiList.Children.Clear();
            foreach (var m in Svc.Ai.Chat.Messages) aiList.Children.Add(m.Role == "user" ? UserBubble(m.Content, m.Images) : AsstBubble(m.Content, m.Provider, m.Model, false));
            AiEmptyVisibility();
            ScrollAiEnd();
        }

        // You: compact accent bubble on the right, any photos stacked above it like a message thread.
        FrameworkElement UserBubble(string text, List<string> images)
        {
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(48, 10, 0, 2) };
            var imgs = images == null ? new List<string>() : images.Where(File.Exists).ToList();
            if (imgs.Count > 0)
            {
                var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, FlowDirection = FlowDirection.LeftToRight, MaxWidth = aiBig ? 470 : 300 };
                double side = imgs.Count == 1 ? (aiBig ? 220 : 170) : 92;
                foreach (var f in imgs)
                {
                    var bmp = AiThumb(f, (int)(side * 2));
                    if (bmp == null) continue;
                    double w = side, h = side;
                    // a single photo keeps its shape; several are square tiles
                    if (imgs.Count == 1) { double ar = (double)bmp.PixelWidth / Math.Max(1, bmp.PixelHeight); if (ar >= 1) h = Math.Max(60, side / ar); else w = Math.Max(60, side * ar); }
                    var tile = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(14), Margin = new Thickness(4, 0, 0, 4), Cursor = Cursors.Hand, ToolTip = "Open photo",
                        Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill }, BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1) };
                    RenderOptions.SetBitmapScalingMode(tile, BitmapScalingMode.HighQuality);
                    string path = f;
                    K.Pressable(tile, () => { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { } }, true);
                    wrap.Children.Add(tile);
                }
                col.Children.Add(wrap);
            }
            if (!string.IsNullOrWhiteSpace(text) || imgs.Count == 0)
            {
                var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Theme.OnAccent, LineHeight = 19 };
                col.Children.Add(new Border { Child = tb, Padding = new Thickness(12, 7, 12, 8), MaxWidth = aiBig ? 470 : 300, CornerRadius = new CornerRadius(16, 16, 4, 16), Background = Theme.AccentFill, HorizontalAlignment = HorizontalAlignment.Right });
            }
            return col;
        }

        static BitmapSource AiThumb(string file, int px)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.DecodePixelWidth = px; bi.UriSource = new Uri(file); bi.EndInit(); bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        // ---- photo attachments
        void AiAddPending(IEnumerable<string> saved)
        {
            foreach (var f in saved)
            {
                if (f == null || aiPending.Count >= 6) continue;
                aiPending.Add(f);
                var bmp = AiThumb(f, 112);
                var g = new Grid { Width = 52, Height = 52, Margin = new Thickness(0, 0, 6, 0), Tag = f };
                var tile = new Border { CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 4, 4, 0), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1),
                    Background = bmp != null ? (Brush)new ImageBrush(bmp) { Stretch = Stretch.UniformToFill } : Theme.Hover };
                RenderOptions.SetBitmapScalingMode(tile, BitmapScalingMode.HighQuality);
                var x = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Background = K.Solid(Color.FromArgb(235, 28, 28, 30)), BorderBrush = K.Solid(Color.FromArgb(90, 255, 255, 255)), BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand, ToolTip = "Remove", Child = K.Glyph("", 7.5, Brushes.White) };
                string path = f;
                K.Pressable(x, () => AiRemovePending(path), true);
                g.Children.Add(tile); g.Children.Add(x);
                aiAttachStrip.Children.Add(g);
                if (K.AnimScale > 0) K.PopIn(g, 0.6, 420);
            }
            aiAttachStrip.Visibility = aiPending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AiSendState();
            Pin();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { aiInput.Focus(); Keyboard.Focus(aiInput); }));
        }

        void AiRemovePending(string f)
        {
            aiPending.Remove(f);
            foreach (var g in aiAttachStrip.Children.OfType<FrameworkElement>().Where(c => (string)c.Tag == f).ToList()) aiAttachStrip.Children.Remove(g);
            try { if (!Svc.Ai.Chat.Messages.Any(m => m.Images != null && m.Images.Contains(f))) File.Delete(f); } catch { }
            aiAttachStrip.Visibility = aiPending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AiSendState();
        }

        void AiClearPending()
        {
            aiPending.Clear(); aiAttachStrip.Children.Clear(); aiAttachStrip.Visibility = Visibility.Collapsed;
        }

        // Picture on the clipboard (copied image, screenshot, or copied image files) → attachment.
        bool AiPasteImage()
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList().Cast<string>().Where(AiService.IsImageFile).ToList();
                    if (files.Count == 0) return false;
                    AiAttachFiles(files);
                    return true;
                }
                if (!Clipboard.ContainsImage()) return false;
                BitmapSource src = null; bool opaque = true;
                // browsers / Snipping Tool also put a real PNG (with proper alpha) on the clipboard
                var data = Clipboard.GetDataObject();
                var png = data != null && data.GetDataPresent("PNG") ? data.GetData("PNG") as MemoryStream : null;
                if (png != null)
                {
                    var dec = BitmapDecoder.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    src = dec.Frames[0]; opaque = false;
                }
                if (src == null) src = Clipboard.GetImage();
                string saved = AiService.SaveImage(src, opaque);
                if (saved == null) return false;
                AiAddPending(new[] { saved });
                return true;
            }
            catch (Exception ex) { Log.Write("ai paste image failed: " + ex.Message); return false; }
        }

        void AiAttachFiles(IEnumerable<string> files)
        {
            var saved = new List<string>();
            foreach (var f in files.Where(AiService.IsImageFile).Take(6))
            {
                try { saved.Add(AiService.SaveImageFile(f)); }
                catch (Exception ex) { Log.Write("ai attach " + f + " failed: " + ex.Message); }
            }
            if (saved.Count > 0) AiAddPending(saved);
        }

        void AiPickImages()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Add photos", Multiselect = true,
                Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.tif;*.tiff;*.heic|All files|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };
            Pin();
            aiPicking = true;
            bool ok;
            try { ok = dlg.ShowDialog(this) == true; }
            finally { aiPicking = false; }
            if (ok) AiAttachFiles(dlg.FileNames);
        }

        // Assistant: no bubble, full width, with a small provider header; copy appears on hover.
        Grid AiSpeaker(string provider, out TextBlock label)
        {
            string prov = string.IsNullOrEmpty(provider) ? S.AiProvider : provider;
            var head = new Grid { Height = 20, Margin = new Thickness(0, 0, 0, 3) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var av = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = K.Solid(AiService.Brand(prov)), Child = K.Glyph("\uE8BD", 8, Brushes.White) };
            label = K.TB(AiService.Label(prov), 11, Theme.Text2, FontWeights.SemiBold); label.Margin = new Thickness(7, 0, 0, 0);
            sp.Children.Add(av); sp.Children.Add(label);
            head.Children.Add(sp);
            return head;
        }

        // Friendly name of the model a reply came from ("" = the provider's default).
        static string AiModelName(string provider, string model)
        {
            string prov = string.IsNullOrEmpty(provider) ? Store.S.AiProvider : provider;
            if (string.IsNullOrEmpty(model)) return AiService.Label(prov) + " · default model";
            string[][] list;
            if (AiService.StaticModels.TryGetValue(prov, out list))
            {
                var known = list.FirstOrDefault(m => m[0] == model);
                if (known != null) return known[1];
            }
            return model;
        }

        Border AsstBubble(string text, string provider, string model, bool streaming)
        {
            TextBlock lbl;
            var head = AiSpeaker(provider, out lbl);
            var host = new Grid();
            host.Children.Add(Markdown.Render(text, aiBig ? 560 : 370));
            var outer = new StackPanel();
            outer.Children.Add(head); outer.Children.Add(host);
            // which model answered, under the reply (fades in once streaming is done)
            var foot = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Opacity = streaming ? 0 : 1 };
            foot.Children.Add(K.Glyph("", 9.5, Theme.Text3));
            var ft = K.TB(AiModelName(provider, model), 10.5, Theme.Text3, FontWeights.Medium); ft.Margin = new Thickness(5, 0, 0, 0);
            foot.Children.Add(ft);
            outer.Children.Add(foot);
            var b = new Border { Child = outer, Padding = new Thickness(2, 0, 4, 0), Margin = new Thickness(0, 12, 0, 2), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch };
            b.Tag = text;
            TextBlock copyGlyph = null, copyText = null;
            var csp = new StackPanel { Orientation = Orientation.Horizontal };
            copyGlyph = K.Glyph("\uE8C8", 10, Theme.Text2);
            copyText = K.TB("Copy", 10.5, Theme.Text2, FontWeights.SemiBold); copyText.Margin = new Thickness(5, 0, 0, 0);
            csp.Children.Add(copyGlyph); csp.Children.Add(copyText);
            var copy = new Border { Height = 20, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 0, 8, 0), Background = Theme.Raised, Child = csp, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0, ToolTip = "Copy reply" };
            K.Pressable(copy, () =>
            {
                CopyText((string)b.Tag);
                copyGlyph.Text = "\uE73E"; copyText.Text = "Copied"; copyGlyph.Foreground = Theme.Green;
                var t = Timer(1400, null); t.Tick += delegate { t.Stop(); copyGlyph.Text = "\uE8C8"; copyText.Text = "Copy"; copyGlyph.Foreground = Theme.Text2; }; t.Start();
            }, true);
            head.Children.Add(copy);
            b.MouseEnter += delegate { copy.BeginAnimation(OpacityProperty, K.Anim(1, 160, K.OutCubic)); };
            b.MouseLeave += delegate { copy.BeginAnimation(OpacityProperty, K.Anim(0, 200, K.OutCubic)); };
            return b;
        }

        // Typing indicator: the speaker header plus three dots in the provider's colour that bob in turn.
        Border ThinkingRow(string provider)
        {
            TextBlock lbl;
            var head = AiSpeaker(provider, out lbl);
            lbl.Text = lbl.Text + " is thinking…";
            var brand = K.Solid(AiService.Brand(string.IsNullOrEmpty(provider) ? S.AiProvider : provider));
            var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(23, 4, 0, 0), Height = 12 };
            for (int i = 0; i < 3; i++)
            {
                var e = new Ellipse { Width = 6, Height = 6, Fill = brand, Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.35 };
                var tt = new TranslateTransform(); e.RenderTransform = tt;
                var begin = TimeSpan.FromMilliseconds(i * 150);
                e.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(380)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, EasingFunction = K.OutCubic });
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -3, TimeSpan.FromMilliseconds(380)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, EasingFunction = K.OutCubic });
                dots.Children.Add(e);
            }
            var sp = new StackPanel(); sp.Children.Add(head); sp.Children.Add(dots);
            return new Border { Child = sp, Padding = new Thickness(2, 0, 4, 0), Margin = new Thickness(0, 12, 0, 2), HorizontalAlignment = HorizontalAlignment.Left };
        }

        void AiSendState(bool busy)
        {
            if (aiSendBg == null) return;
            bool lit = busy || aiInput.Text.Trim().Length > 0 || aiPending.Count > 0;
            if (lit == aiSendLit) return;
            aiSendLit = lit;
            aiSendBg.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(lit ? Theme.Current.Accent : Theme.Current.Hover, 180));
            aiSendGlyph.Foreground = lit ? Theme.OnAccent : Theme.Text3;
        }
        void AiSendState() { AiSendState(Svc.Ai.Busy); }

        void AiUseSuggestion(string prompt)
        {
            Pin();
            string t;
            if (prompt.StartsWith("Summarize"))
            {
                string c = "";
                try { if (Clipboard.ContainsText()) c = Clipboard.GetText().Trim(); } catch { }
                t = c.Length > 0 ? "Summarize this:\n\n" + c : "Summarize this: ";
            }
            else if (prompt.StartsWith("Write")) t = "Write a short email about ";
            else t = prompt + ": ";
            aiInput.Text = t;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { aiInput.Focus(); Keyboard.Focus(aiInput); aiInput.CaretIndex = aiInput.Text.Length; }));
        }

        void ScrollAiEnd() { Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => aiScroll.ScrollToEnd())); }

        void AiSend()
        {
            if (Svc.Ai.Busy) { Svc.Ai.Cancel(); return; }
            string text = aiInput.Text.Trim();
            var images = aiPending.ToList();
            if (text.Length == 0 && images.Count == 0) return;
            AiCloseOverlay();
            AiClearPending();
            aiInput.Text = "";
            var ub = UserBubble(text, images);
            aiList.Children.Add(ub); K.FadeIn(ub, 12, 320);
            aiThinkingRow = ThinkingRow(S.AiProvider); aiList.Children.Add(aiThinkingRow); K.FadeIn(aiThinkingRow, 8, 260);
            aiStreamBubble = null; aiStreamText.Clear();
            aiSendGlyph.Text = ""; AiSendState(true);
            AiEmptyVisibility(); ScrollAiEnd();
            string prov = S.AiProvider, model = Svc.Ai.ModelFor(prov);
            Svc.Ai.Send(text, images,
                (type, t) =>
                {
                    if (type == "thinking") return;
                    if (aiStreamBubble == null)
                    {
                        if (aiThinkingRow != null) { aiList.Children.Remove(aiThinkingRow); aiThinkingRow = null; }
                        aiStreamBubble = AsstBubble("", prov, model, true);
                        aiList.Children.Add(aiStreamBubble); K.FadeIn(aiStreamBubble, 10, 300);
                    }
                    aiStreamText.Append(t);
                    aiRenderDirty = true;
                    if (!aiRenderTimer.IsEnabled) aiRenderTimer.Start();
                },
                (err, action) =>
                {
                    if (aiThinkingRow != null) { aiList.Children.Remove(aiThinkingRow); aiThinkingRow = null; }
                    if (aiStreamBubble != null)
                    {
                        AiRenderStream();
                        var foot = ((StackPanel)aiStreamBubble.Child).Children[2];
                        foot.BeginAnimation(OpacityProperty, K.Anim(1, 320, K.OutCubic));
                    }
                    aiSendGlyph.Text = ""; AiSendState(false);
                    if (err != null) AiError(err, action);
                    else if (action == "cancelled" && aiStreamBubble == null) { }
                    else if (aiStreamBubble == null && action != "cancelled") AiError("(no reply)", "");
                    if (err == null && aiStreamBubble != null && (!expanded || CurrentPageKey() != "ai"))
                    {
                        string first = aiStreamText.ToString().Trim().Split('\n')[0];
                        SystemEvent("", K.Solid(AiService.Brand(prov)), AiService.Label(prov) + " replied", first.Length > 60 ? first.Substring(0, 60) + "…" : first, Pri.Notification, 5000, "ai-peek", () => { Dismiss(); Expand("ai", true); });
                    }
                    aiStreamBubble = null;
                    AiEmptyVisibility(); ScrollAiEnd();
                    AiCheckUsage();
                });
        }

        void AiRenderStream()
        {
            aiRenderDirty = false;
            if (aiStreamBubble == null) return;
            string text = aiStreamText.ToString();
            aiStreamBubble.Tag = text;
            var host = (Grid)((StackPanel)aiStreamBubble.Child).Children[1];
            host.Children.RemoveAt(0);
            host.Children.Insert(0, Markdown.Render(text, aiBig ? 520 : 340));
            ScrollAiEnd();
        }

        void AiError(string err, string action)
        {
            var sp = new StackPanel();
            var t = K.TB(err, 12.5, Theme.Red, FontWeights.Medium); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None;
            sp.Children.Add(t);
            string prov = S.AiProvider;
            if (action == "login" || action == "update" || action == "key")
            {
                var btn = action == "login" ? K.GlyphPill("", "Sign in with browser", Theme.AccentFill, Theme.OnAccent, 28, () => Svc.Ai.SignIn(prov))
                    : action == "update" ? K.PillBtn("Update CLI", Theme.Hover, Theme.Text, 28, () => Svc.Ai.UpdateCli(prov))
                    : K.PillBtn("Set API key", Theme.Hover, Theme.Text, 28, () => OpenSettings("ai"));
                btn.HorizontalAlignment = HorizontalAlignment.Left; btn.Margin = new Thickness(0, 8, 0, 0);
                sp.Children.Add(btn);
            }
            var b = new Border { Child = sp, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 3, 30, 3), CornerRadius = new CornerRadius(14), Background = Theme.RedSoft, HorizontalAlignment = HorizontalAlignment.Left };
            aiList.Children.Add(b); K.FadeIn(b, 8, 280);
        }

        void AiNewChat()
        {
            AiClearPending();
            Svc.Ai.NewChat();
            aiList.Children.Clear(); aiStreamBubble = null; aiThinkingRow = null;
            aiSendGlyph.Text = ""; AiSendState(false);
            AiEmptyVisibility(); K.FadeIn(aiEmpty, 10, 300);
            Pin();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => aiInput.Focus()));
        }

        void AiToggleBig()
        {
            aiBig = !aiBig;
            AiRefreshHeader();
            RefreshExpandedSize();
            AiRenderAll();
        }

        void AiCycleMethod()
        {
            var order = new[] { "auto", "cli", "key" };
            S.AiMethod = order[(Array.IndexOf(order, S.AiMethod) + 1) % 3];
            Store.Save(); AiRefreshHeader(); AiCheckUsage();
        }

        void AiConnect()
        {
            string prov = S.AiProvider;
            if (Svc.Ai.MethodFor(prov) == "key") OpenSettings("ai");
            else Svc.Ai.SignIn(prov);
        }

        void AiCloseOverlay() { if (aiOverlay != null) aiOverlay.Visibility = Visibility.Collapsed; }

        void AiOpenOverlay()
        {
            aiOverlay.Visibility = Visibility.Visible;
            var panel = (Border)aiOverlay.Children[0];
            var st = new ScaleTransform(0.94, 0.94); panel.RenderTransform = st; panel.RenderTransformOrigin = new Point(0, 0);
            K.Pop(st, 1, 260, K.OutBack);
            panel.Opacity = 0; panel.BeginAnimation(OpacityProperty, K.Anim(1, 180, K.OutCubic));
        }

        void AiShowModels()
        {
            Pin();
            string prov = S.AiProvider;
            aiOverlayList.Children.Clear();
            aiOverlayList.Children.Add(OverlayHeader(AiService.Label(prov) + " models · loading…"));
            AiOpenOverlay();
            Svc.Ai.Models(prov, list =>
            {
                aiOverlayList.Children.Clear();
                aiOverlayList.Children.Add(OverlayHeader(AiService.Label(prov) + " models"));
                string cur = Svc.Ai.ModelFor(prov);
                foreach (var m in list)
                {
                    string id = m[0];
                    aiOverlayList.Children.Add(OverlayItem(m[1], id == cur, () => { S.AiModels[prov] = id; Store.Save(); AiCloseOverlay(); AiRefreshHeader(); }));
                }
                var tb = PinnedTextBox(new TextBox());
                var f = K.Field(tb, "Other model id… (Enter)", 0, Theme.Raised); f.Margin = new Thickness(4, 6, 4, 4);
                tb.KeyDown += (s, e) => { if (e.Key == Key.Enter && tb.Text.Trim().Length > 0) { e.Handled = true; S.AiModels[prov] = tb.Text.Trim(); Store.Save(); AiCloseOverlay(); AiRefreshHeader(); } };
                aiOverlayList.Children.Add(f);
            });
        }

        void AiShowUsage()
        {
            Pin();
            string prov = S.AiProvider;
            aiOverlayList.Children.Clear();
            aiOverlayList.Children.Add(OverlayHeader("Usage · loading…"));
            AiOpenOverlay();
            Svc.Ai.Usage(prov, u =>
            {
                aiOverlayList.Children.Clear();
                aiOverlayList.Children.Add(OverlayHeader(AiService.Label(prov) + (u.Plan.Length > 0 ? " · " + u.Plan : "")));
                foreach (var w in u.Windows)
                {
                    var r = new StackPanel { Margin = new Thickness(8, 6, 8, 4) };
                    var h = new Grid();
                    h.Children.Add(K.TB(w.Label, 12, Theme.Text, FontWeights.SemiBold));
                    var pct = K.TB(w.Percent < 0 ? "—" : Math.Round(w.Percent) + "%", 12, w.Percent >= 80 ? (Brush)Theme.Orange : Theme.Text2, FontWeights.SemiBold); pct.HorizontalAlignment = HorizontalAlignment.Right;
                    h.Children.Add(pct);
                    var track = new Grid { Height = 5, Margin = new Thickness(0, 4, 0, 0) };
                    track.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), Background = Theme.Hover });
                    track.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), Background = w.Percent >= 80 ? (Brush)Theme.Orange : Theme.AccentFill, HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(4, 222 * Math.Max(0, Math.Min(100, w.Percent)) / 100) });
                    r.Children.Add(h); r.Children.Add(track);
                    string reset = w.ResetsAt > 0 ? ResetText(w.ResetsAt) : "";
                    string det = (w.Detail ?? "") + (reset.Length > 0 ? ((w.Detail ?? "").Length > 0 ? " · " : "") + reset : "");
                    if (det.Length > 0) { var d = K.TB(det, 10.5, Theme.Text2, FontWeights.Normal); d.Margin = new Thickness(0, 3, 0, 0); r.Children.Add(d); }
                    aiOverlayList.Children.Add(r);
                }
                if (u.Note.Length > 0) { var n = K.TB(u.Note, 11, Theme.Text2, FontWeights.Normal); n.TextWrapping = TextWrapping.Wrap; n.Margin = new Thickness(8, 6, 8, 4); aiOverlayList.Children.Add(n); }
                if (u.Url.Length > 0) aiOverlayList.Children.Add(OverlayItem("Open the usage page ↗", false, () => Launcher.Open(u.Url)));
                AiApplyWarn(u);
            });
        }

        static string ResetText(double epoch)
        {
            var d = TimeSpan.FromSeconds(epoch - (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds);
            if (d.TotalSeconds <= 0) return "";
            return "resets in " + (d.TotalHours >= 24 ? (int)d.TotalDays + "d " + d.Hours + "h" : d.TotalHours >= 1 ? (int)d.TotalHours + "h " + d.Minutes + "m" : d.Minutes + "m");
        }

        void AiCheckUsage()
        {
            string prov = S.AiProvider;
            if (prov == "gemini") { aiWarn.Visibility = Visibility.Collapsed; return; }
            Svc.Ai.Usage(prov, AiApplyWarn);
        }

        void AiApplyWarn(UsageInfo u)
        {
            var hi = u.Windows.Where(w => w.Percent >= 80).OrderByDescending(w => w.Percent).FirstOrDefault();
            if (hi == null) { aiWarn.Visibility = Visibility.Collapsed; return; }
            aiWarnText.Text = hi.Label + " usage at " + Math.Round(hi.Percent) + "%" + (hi.ResetsAt > 0 ? " · " + ResetText(hi.ResetsAt) : "");
            aiWarn.Visibility = Visibility.Visible;
        }

        static TextBlock OverlayHeader(string t)
        {
            var h = K.TB(t, 11, Theme.Text3, FontWeights.SemiBold); h.Margin = new Thickness(8, 4, 8, 4);
            return h;
        }

        static Border OverlayItem(string label, bool sel, Action click)
        {
            var g = new Grid();
            g.Children.Add(K.TB(label, 12.5, Theme.Text, sel ? FontWeights.SemiBold : FontWeights.Normal));
            if (sel) { var c = K.Glyph("", 11, Theme.Accent); c.HorizontalAlignment = HorizontalAlignment.Right; g.Children.Add(c); }
            var b = new Border { Height = 32, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 0, 8, 0), Background = sel ? (Brush)Theme.Highlight : Brushes.Transparent, Child = g, Cursor = Cursors.Hand };
            K.Pressable(b, click, true);
            return b;
        }

        // ================================================================ launcher
        TextBox lInput; ScrollViewer lScroll; StackPanel lResults, lBrowse; List<LaunchResult> lItems = new List<LaunchResult>(); int lSel; readonly List<Border> lRows = new List<Border>(); bool lBrowseDirty = true;
        // sliding selection, per-row "what Enter does" chips, the search box's focus ring and clear/esc
        Border lSelInd, lBox, lClear, lEsc; TranslateTransform lSelT; SolidColorBrush lBoxRing;
        readonly List<Border> lChips = new List<Border>(); readonly List<double> lRowY = new List<double>(), lRowH = new List<double>();
        HashSet<string> lPrevKeys = new HashSet<string>(); Point lLastMouse; bool lSelPlaced;
        const double LaunchW = 560, LaunchMaxH = 440, LaunchMinH = 140, LTop = 14, LBoxH = 46, LGap = 10, LBottom = 12;

        void BuildLauncher()
        {
            var g = new Grid { Margin = new Thickness(16, LTop, 16, LBottom) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());

            // search box: soft accent ring while focused, x to clear once there's text, an Esc hint before
            lBoxRing = new SolidColorBrush(Theme.A(Theme.Current.Accent, 0));
            lBox = new Border { CornerRadius = new CornerRadius(14), Background = Theme.Raised, Height = LBoxH, Padding = new Thickness(14, 0, 8, 0), BorderBrush = lBoxRing, BorderThickness = new Thickness(1.2) };
            var bg = new Grid();
            bg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bg.ColumnDefinitions.Add(new ColumnDefinition());
            bg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lens = K.Glyph("", 16, Theme.Text2); lens.VerticalAlignment = VerticalAlignment.Center;
            bg.Children.Add(lens);
            lInput = PinnedTextBox(new TextBox());
            var f = K.Field(lInput, "Search apps, 2*(3+4), a site, ?web search, >command", 0, Brushes.Transparent);
            lInput.FontSize = 16;
            ((TextBlock)f.Children[1]).FontSize = 14;
            Grid.SetColumn(f, 1);
            bg.Children.Add(f);
            var trail = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            lClear = K.IconBtn("", 28, 10, Theme.Text2, () => { Pin(); lInput.Text = ""; lInput.Focus(); });
            lClear.ToolTip = "Clear"; lClear.Visibility = Visibility.Collapsed;
            lEsc = KeyCap("Esc"); lEsc.ToolTip = "Close";
            trail.Children.Add(lEsc); trail.Children.Add(lClear);
            Grid.SetColumn(trail, 2);
            bg.Children.Add(trail);
            lBox.Child = bg;
            lInput.GotKeyboardFocus += delegate { lBoxRing.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Theme.A(Theme.Current.Accent, 0.55), TimeSpan.FromMilliseconds(Math.Max(1, K.Dur(200))))); lens.Foreground = Theme.Accent; };
            lInput.LostKeyboardFocus += delegate { lBoxRing.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Theme.A(Theme.Current.Accent, 0), TimeSpan.FromMilliseconds(Math.Max(1, K.Dur(200))))); lens.Foreground = Theme.Text2; };
            g.Children.Add(lBox);

            lScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Margin = new Thickness(0, LGap, 0, 0) }; ScrollFade.Attach(lScroll, 30, false);
            var stack = new StackPanel();
            // results sit over one highlight that glides between rows (instead of rows flipping colour)
            var resHost = new Grid();
            lSelT = new TranslateTransform();
            var bar = new Border { Width = 3, Height = 18, CornerRadius = new CornerRadius(1.5), Background = Theme.Accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            lSelInd = new Border { CornerRadius = new CornerRadius(10), Background = Theme.Raised, VerticalAlignment = VerticalAlignment.Top, Height = 44, RenderTransform = lSelT, Child = bar, Opacity = 0, IsHitTestVisible = false };
            lResults = new StackPanel();
            resHost.Children.Add(lSelInd); resHost.Children.Add(lResults);
            lBrowse = new StackPanel();
            stack.Children.Add(resHost); stack.Children.Add(lBrowse);
            lScroll.Content = stack;
            Grid.SetRow(lScroll, 1);
            g.Children.Add(lScroll);
            vLaunch.Root.Children.Add(g);
            lInput.TextChanged += delegate
            {
                bool has = lInput.Text.Length > 0;
                if (has != (lClear.Visibility == Visibility.Visible))
                {
                    lClear.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
                    lEsc.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
                    if (has) K.PopIn(lClear, 0.6, 260); else K.FadeIn(lEsc, 0, 200);
                }
                LauncherSearch();
            };
            Svc.Apps.Updated += delegate { lBrowseDirty = true; if (expanded && expandMode == "launcher" && lInput.Text.Length == 0) BuildBrowse(); };
        }

        static Border KeyCap(string text)
        {
            var t = K.TB(text, 10, Theme.Text3, FontWeights.SemiBold);
            return new Border { Height = 20, CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 0, 6, 0), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), Child = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        }

        void OpenLauncher()
        {
            lInput.Text = "";
            if (lBrowseDirty) BuildBrowse();
            LauncherSearch();
            Transition(vLaunch);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { lInput.Focus(); Keyboard.Focus(lInput); }));
        }

        // The island fits the results: a couple of hits make a short island, browsing gets the full height.
        double LauncherH()
        {
            if (lInput == null || lInput.Text.Trim().Length == 0) return LaunchMaxH;
            return Math.Max(LaunchMinH, Math.Min(LaunchMaxH, LTop + LBoxH + LGap + lRowH.Sum() + LBottom + 2));
        }
        void LauncherFit()
        {
            double h = LauncherH();
            if (Math.Abs(vLaunch.W - LaunchW) > 0.5 || Math.Abs(vLaunch.H - h) > 0.5) SetViewSize(vLaunch, LaunchW, h);
        }

        List<LaunchResult> LauncherActions()
        {
            return new List<LaunchResult>
            {
                new LaunchResult { Kind = "action", Title = "Dynamic Island settings", Sub = "Island", Glyph = "", Key = "settings preferences", Run = () => OpenSettings(null) },
                new LaunchResult { Kind = "action", Title = "Wallpaper picker", Sub = "Island", Glyph = "", Key = "wallpaper", Run = () => OpenMode("wallpaper") },
                new LaunchResult { Kind = "action", Title = "AI chat", Sub = "Island", Glyph = "", Key = "ai chat claude gemini gpt", Run = () => { expandMode = "pages"; Expand("ai", true); } },
                new LaunchResult { Kind = "action", Title = "Timer", Sub = "Island", Glyph = "", Key = "timer", Run = () => { expandMode = "pages"; Expand("timer", true); } },
                new LaunchResult { Kind = "action", Title = "To-do", Sub = "Island", Glyph = "", Key = "todo", Run = () => { expandMode = "pages"; Expand("todo", true); } },
                new LaunchResult { Kind = "action", Title = "System monitor", Sub = "Island", Glyph = "", Key = "system cpu ram", Run = () => { expandMode = "pages"; Expand("system", true); } },
            };
        }

        void LauncherSearch()
        {
            string q = lInput.Text;
            lResults.Children.Clear(); lRows.Clear(); lChips.Clear(); lRowY.Clear(); lRowH.Clear();
            lLastMouse = Native.CursorPx();   // a mouse resting over the list doesn't pick a row while you type
            if (q.Trim().Length == 0)
            {
                lItems = new List<LaunchResult>(); lPrevKeys.Clear();
                lSelInd.BeginAnimation(OpacityProperty, null); lSelInd.Opacity = 0; lSelPlaced = false; lSelInd.Visibility = Visibility.Collapsed;   // it would hold 44 px open above the app grid
                lBrowse.Visibility = Visibility.Visible;
                LauncherFit();
                return;
            }
            lBrowse.Visibility = Visibility.Collapsed;
            var m = Regex.Match(q.Trim(), @"^timer\s+(\d+)$", RegexOptions.IgnoreCase);
            lItems = Svc.Apps.Search(q, LauncherActions());
            if (m.Success) { int mins = int.Parse(m.Groups[1].Value); lItems.Insert(0, new LaunchResult { Kind = "action", Title = "Start a " + mins + " min timer", Sub = "Island", Glyph = "", Run = () => { TimerStart(TimeSpan.FromMinutes(mins)); } }); }
            lSel = 0;
            // rows that were already listed stay put; only new ones slide in (no re-animating on every key)
            var keys = new HashSet<string>(); int fresh = 0; double y = 0;
            for (int i = 0; i < lItems.Count; i++)
            {
                var r = lItems[i];
                var row = ResultRow(r, i, q.Trim());
                lRows.Add(row); lResults.Children.Add(row);
                lRowY.Add(y); lRowH.Add(row.Height); y += row.Height;
                string key = r.Kind + "|" + (r.Key ?? r.Title);
                keys.Add(key);
                if (!lPrevKeys.Contains(key) && K.AnimScale > 0 && fresh < 10)
                {
                    var tt = new TranslateTransform(0, 6); row.RenderTransform = tt; row.Opacity = 0;
                    var begin = TimeSpan.FromMilliseconds(K.Dur(fresh * 18));
                    var a = K.Anim(0, 320, K.OutQuint); a.BeginTime = begin; tt.BeginAnimation(TranslateTransform.YProperty, a);
                    var o = K.Anim(1, 220, K.OutCubic); o.BeginTime = begin; row.BeginAnimation(OpacityProperty, o);
                    fresh++;
                }
            }
            lPrevKeys = keys;
            lScroll.ScrollToTop();
            HighlightSel(false);
            LauncherFit();
        }

        static string RunVerb(LaunchResult r)
        {
            if (r.CopyText != null) return "Copy";
            switch (r.Kind) { case "web": return "Search"; case "cmd": return "Run"; case "action": return "Go"; }
            return "Open";
        }

        // Title with the typed text picked out in the accent colour.
        static TextBlock MatchTitle(string title, string q, double size, FontWeight weight)
        {
            var tb = K.TB("", size, Theme.Text, weight);
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
            int i = q.Length > 0 ? title.IndexOf(q, StringComparison.OrdinalIgnoreCase) : -1;
            if (i < 0) { tb.Text = title; return tb; }
            if (i > 0) tb.Inlines.Add(new System.Windows.Documents.Run(title.Substring(0, i)));
            tb.Inlines.Add(new System.Windows.Documents.Run(title.Substring(i, q.Length)) { Foreground = Theme.Accent, FontWeight = FontWeights.SemiBold });
            if (i + q.Length < title.Length) tb.Inlines.Add(new System.Windows.Documents.Run(title.Substring(i + q.Length)));
            return tb;
        }

        Border ResultRow(LaunchResult r, int idx, string q)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            FrameworkElement icon = r.Icon != null ? (FrameworkElement)new Image { Source = r.Icon, Width = 26, Height = 26 } : K.Glyph(r.Glyph ?? "", 16, r.Kind == "calc" ? (Brush)Theme.Accent : Theme.Text2);
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            bool calc = r.Kind == "calc", plain = calc || r.Kind == "web" || r.Kind == "cmd" || r.Kind == "url";
            tx.Children.Add(plain ? K.TB(r.Title, calc ? 17 : 13.5, Theme.Text, calc ? FontWeights.SemiBold : FontWeights.Medium) : MatchTitle(r.Title, q, 13.5, FontWeights.Medium));
            if (!string.IsNullOrEmpty(r.Sub)) tx.Children.Add(K.TB(r.Sub, 11, Theme.Text2, FontWeights.Normal));
            Grid.SetColumn(tx, 1);
            // what Enter does, shown on the selected row only
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal };
            var ret = K.TB("↵", 11, Theme.Text2, FontWeights.SemiBold); ret.Margin = new Thickness(0, 0, 5, 0);
            chipRow.Children.Add(ret); chipRow.Children.Add(K.TB(RunVerb(r), 11, Theme.Text2, FontWeights.SemiBold));
            var chip = new Border { Height = 22, CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 0, 8, 0), Background = K.Solid(Theme.A(Theme.Current.Text, 0.06)), Child = chipRow, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Opacity = 0, IsHitTestVisible = false };
            Grid.SetColumn(chip, 2);
            g.Children.Add(icon); g.Children.Add(tx); g.Children.Add(chip);
            lChips.Add(chip);
            var b = new Border { Height = calc ? 52 : 44, CornerRadius = new CornerRadius(10), Padding = new Thickness(6, 0, 8, 0), Child = g, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            // follow the mouse only when it really moves (rows sliding under a still cursor don't count)
            b.MouseMove += delegate
            {
                var p = Native.CursorPx();
                if (Math.Abs(p.X - lLastMouse.X) < 1 && Math.Abs(p.Y - lLastMouse.Y) < 1) return;
                lLastMouse = p;
                if (lSel != idx) { lSel = idx; HighlightSel(false); }
            };
            b.MouseLeftButtonDown += (s, e) => e.Handled = true;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; RunResult(r); };
            return b;
        }

        // Glide the highlight to the selected row; scroll only when the keyboard moved it.
        void HighlightSel(bool scroll)
        {
            if (lSel < 0 || lSel >= lRows.Count) { lSelInd.Opacity = 0; return; }
            lSelInd.Visibility = Visibility.Visible;
            double y = lRowY[lSel], h = lRowH[lSel];
            if (K.AnimScale > 0 && lSelPlaced)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                lSelT.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(K.Dur(170))) { EasingFunction = ease });
                lSelInd.BeginAnimation(HeightProperty, new DoubleAnimation(h, TimeSpan.FromMilliseconds(K.Dur(170))) { EasingFunction = ease });
            }
            else
            {
                lSelT.BeginAnimation(TranslateTransform.YProperty, null); lSelT.Y = y;
                lSelInd.BeginAnimation(HeightProperty, null); lSelInd.Height = h;
                lSelInd.Opacity = 0; lSelInd.BeginAnimation(OpacityProperty, K.Anim(1, 180, K.OutCubic));
            }
            lSelPlaced = true;
            for (int i = 0; i < lChips.Count; i++)
            {
                double to = i == lSel ? 1 : 0;
                if (Math.Abs(lChips[i].Opacity - to) > 0.01 || lChips[i].HasAnimatedProperties) lChips[i].BeginAnimation(OpacityProperty, K.Anim(to, i == lSel ? 180 : 100, K.OutCubic));
            }
            if (scroll)
            {
                // keep the selected row inside the viewport with a little margin
                double top = lRowY[lSel], bot = top + lRowH[lSel], vt = lScroll.VerticalOffset, vh = lScroll.ViewportHeight;
                if (top < vt) lScroll.ScrollToVerticalOffset(Math.Max(0, top - 4));
                else if (bot > vt + vh) lScroll.ScrollToVerticalOffset(bot - vh + 4);
            }
        }

        void RunResult(LaunchResult r)
        {
            if (r.CopyText != null) { CopyText(r.CopyText); Collapse(); SystemEvent("", Theme.Accent, "Result copied", r.CopyText, Pri.Notification, 1800, "calc", null); return; }
            bool island = r.Kind == "action";
            if (!island) Collapse();
            if (r.Run != null) r.Run();
        }

        void BuildBrowse()
        {
            lBrowseDirty = false;
            lBrowse.Children.Clear();
            var freq = Svc.Apps.Frequent(8).Where(x => Store.S.LaunchCounts.ContainsKey(x.Key)).ToList();
            if (freq.Count > 0)
            {
                lBrowse.Children.Add(SectionLabel("Frequently used"));
                var w = new WrapPanel();
                foreach (var r in freq) w.Children.Add(AppTile(r));
                lBrowse.Children.Add(w);
            }
            lBrowse.Children.Add(SectionLabel("All apps · " + Svc.Apps.Count));
            var all = new WrapPanel();
            foreach (var a in Svc.Apps.Apps)
            {
                var app = a;
                all.Children.Add(AppTile(new LaunchResult { Kind = "app", Title = a.Name, Icon = a.Icon, Run = () => Svc.Apps.Launch(app) }));
            }
            lBrowse.Children.Add(all);
        }

        static TextBlock SectionLabel(string t)
        {
            var l = K.TB(t.ToUpperInvariant(), 10.5, Theme.Text3, FontWeights.SemiBold); l.Margin = new Thickness(6, 4, 0, 6);
            return l;
        }

        Border AppTile(LaunchResult r)
        {
            // top-aligned: icons line up across a row whether the name takes one line or two
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
            FrameworkElement icon = r.Icon != null ? (FrameworkElement)new Image { Source = r.Icon, Width = 34, Height = 34 } : K.Glyph("", 22, Theme.Text2);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            var name = K.TB(r.Title, 10.5, Theme.Text, FontWeights.Normal);
            name.TextWrapping = TextWrapping.Wrap; name.TextAlignment = TextAlignment.Center; name.MaxHeight = 28; name.Margin = new Thickness(4, 6, 4, 0);
            sp.Children.Add(icon); sp.Children.Add(name);
            var b = new Border { Width = 86, Height = 84, CornerRadius = new CornerRadius(12), Child = sp, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = r.Title };
            K.Pressable(b, () => RunResult(r), true);
            return b;
        }

        void LauncherKey(KeyEventArgs e)
        {
            bool down = e.Key == Key.Down || (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) || (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control);
            bool up = e.Key == Key.Up || (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) || (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control);
            if (down || up)
            {
                e.Handled = true;
                if (lRows.Count == 0) return;
                lSel = (lSel + (down ? 1 : -1) + lRows.Count) % lRows.Count;
                HighlightSel(true);
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (lSel >= 0 && lSel < lItems.Count) RunResult(lItems[lSel]);
            }
        }

        // ================================================================ wallpapers (local + online)
        WrapPanel wGrid; List<string> wFiles = new List<string>(); int wSel; readonly List<Border> wTiles = new List<Border>();
        TextBlock wHeader, wStatus; ScrollViewer wScroll; string wCurrent, wTab = "local";
        Segmented wTabSeg, wProvSeg; Border wSortHost, wSearchBox, wRandomBtn; TextBox wSearch; Grid wOnlineBar;
        List<OnlineWall> wOnline = new List<OnlineWall>(); int wPage, wGen; bool wMore, wLoading;
        const double WallW = 660, WallH = 470;

        void BuildWallpaper()
        {
            var g = new Grid { Margin = new Thickness(16, 14, 16, 10) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new Grid { Height = 36 };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ht = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 12, 0) };
            ht.Children.Add(K.TB("Wallpapers", 15, Theme.Text, FontWeights.SemiBold));
            // subtitle doubles as the folder picker: "3 images · Wallpapers ⌄"
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Change wallpaper folder" };
            wHeader = K.TB("", 11, Theme.Text2, FontWeights.Normal);
            var chev = K.Glyph("\uE70D", 8, Theme.Text3); chev.Margin = new Thickness(5, 1, 0, 0);
            sub.Children.Add(wHeader); sub.Children.Add(chev);
            sub.MouseLeftButtonDown += (s, e) => e.Handled = true;
            sub.MouseLeftButtonUp += (s, e) => { e.Handled = true; Pin(); PickWallpaperFolder(); };
            sub.MouseEnter += delegate { wHeader.Foreground = Theme.Text; };
            sub.MouseLeave += delegate { wHeader.Foreground = Theme.Text2; };
            ht.Children.Add(sub);
            var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            wTabSeg = new Segmented(new[] { "Local", "Online" }, 0, 70);
            wTabSeg.Changed += i => SwitchWallTab(i == 0 ? "local" : "online");
            // fit as one chip that cycles Fill -> Fit -> Center -> Span (applies to the current wallpaper)
            string[] fits = { "Fill", "Fit", "Center", "Span" };
            TextBlock fitText;
            var fit = K.GlyphPill("\uE740", fits[Math.Max(0, Math.Min(3, S.WallpaperFit))], Theme.Raised, Theme.Text, 30, null);
            fitText = (TextBlock)((StackPanel)fit.Child).Children[1];
            fit.Margin = new Thickness(8, 0, 0, 0); fit.ToolTip = "How the picture fills the screen (click to change)";
            fit.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true; Pin();
                S.WallpaperFit = (S.WallpaperFit + 1) % 4; Store.Save();
                fitText.Text = fits[S.WallpaperFit]; K.PopIn(fitText, 0.7, 300);
                if (wCurrent != null && File.Exists(wCurrent)) ApplyWallpaper(wCurrent, false);
            };
            wRandomBtn = K.IconBtn("\uE8B1", 30, 13, Theme.Text2, RandomWallpaper); wRandomBtn.ToolTip = "Shuffle (R)"; wRandomBtn.Margin = new Thickness(6, 0, 0, 0);
            var folder = K.IconBtn("\uE8B7", 30, 13, Theme.Text2, () => { try { Process.Start("explorer.exe", "\"" + S.WallpaperDir + "\""); } catch { } }); folder.ToolTip = "Open the folder";
            acts.Children.Add(wTabSeg); acts.Children.Add(fit); acts.Children.Add(wRandomBtn); acts.Children.Add(folder);
            Grid.SetColumn(acts, 1);
            head.Children.Add(ht); head.Children.Add(acts);
            g.Children.Add(head);

            wOnlineBar = new Grid { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            wOnlineBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wOnlineBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wOnlineBar.ColumnDefinitions.Add(new ColumnDefinition());
            int pi = Math.Max(0, Array.IndexOf(OnlineWallpapers.Providers, S.OnlineProvider));
            wProvSeg = new Segmented(OnlineWallpapers.Providers.Select(OnlineWallpapers.Label).ToArray(), pi, 84);
            wProvSeg.Changed += i => { S.OnlineProvider = OnlineWallpapers.Providers[i]; S.OnlineSort = 0; Store.Save(); BuildSortSeg(); LoadOnline(true); };
            wSortHost = new Border { Margin = new Thickness(8, 0, 0, 0) };
            // sits inside the search box, on the right, while a search is active
            wBestMatch = K.TB("Best matches · all time", 11, Theme.Text3, FontWeights.SemiBold); wBestMatch.Margin = new Thickness(0, 8, 14, 0); wBestMatch.VerticalAlignment = VerticalAlignment.Center; wBestMatch.HorizontalAlignment = HorizontalAlignment.Right; wBestMatch.Visibility = Visibility.Collapsed; wBestMatch.IsHitTestVisible = false;
            Grid.SetRow(wBestMatch, 1); Grid.SetColumnSpan(wBestMatch, 3);
            wSearch = PinnedTextBox(new TextBox());
            var sf = K.Field(wSearch, "Search…  (Enter)", 0, Theme.Raised);
            wSearch.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; wSearchActive = wSearch.Text.Trim().Length > 0; WallSortState(); LoadOnline(true); } };
            // clearing the box goes straight back to browsing (sort tabs active again)
            wSearch.TextChanged += delegate
            {
                WallSortState();
                if (wSearch.Text.Trim().Length == 0 && wSearchActive) { wSearchActive = false; LoadOnline(true); }
            };
            wSearchBox = new Border { Margin = new Thickness(0, 8, 0, 0), Child = sf };
            wOnlineBar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            wOnlineBar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            wOnlineBar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            // Wallhaven filters: categories + purity as toggle chips
            wFilters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(wFilters, 2); Grid.SetColumnSpan(wFilters, 3);
            wOnlineBar.Children.Add(wFilters);
            Grid.SetColumn(wSortHost, 1); Grid.SetRow(wSearchBox, 1); Grid.SetColumnSpan(wSearchBox, 3);
            wOnlineBar.Children.Add(wProvSeg); wOnlineBar.Children.Add(wSortHost); wOnlineBar.Children.Add(wSearchBox); wOnlineBar.Children.Add(wBestMatch);
            Grid.SetRow(wOnlineBar, 1);
            g.Children.Add(wOnlineBar);

            wScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Margin = new Thickness(0, 10, 0, 0) }; ScrollFade.Attach(wScroll, 30, false);
            wGrid = new WrapPanel();
            wScroll.Content = wGrid;
            wScroll.ScrollChanged += (s, e) =>
            {
                if (wTab == "online" && wMore && !wLoading && wScroll.VerticalOffset + wScroll.ViewportHeight > wScroll.ExtentHeight - 160) LoadOnline(false);
            };
            Grid.SetRow(wScroll, 2);
            g.Children.Add(wScroll);
            wStatus = K.TB("", 11.5, Theme.Text2, FontWeights.Medium); wStatus.Margin = new Thickness(2, 6, 0, 0);
            Grid.SetRow(wStatus, 3);
            g.Children.Add(wStatus);
            vWall.Root.Children.Add(g);
            BuildSortSeg();
        }

        void BuildSortSeg()
        {
            var sorts = OnlineWallpapers.Sorts(S.OnlineProvider);
            var seg = new Segmented(sorts, Math.Min(S.OnlineSort, sorts.Length - 1), 62);
            seg.Changed += i => { S.OnlineSort = i; Store.Save(); LoadOnline(true); };
            wSortHost.Child = seg;
            WallSortState();
            wSearchBox.Visibility = OnlineWallpapers.Searchable(S.OnlineProvider) ? Visibility.Visible : Visibility.Collapsed;
            RenderWallFilters();
        }

        StackPanel wFilters; bool wSearchActive; TextBlock wBestMatch;

        // while a search is typed the sort tabs don't apply (results are best-match): dim them, say so
        void WallSortState()
        {
            if (wSortHost == null) return;
            bool searching = wSearch != null && wSearch.Text.Trim().Length > 0 && OnlineWallpapers.Searchable(S.OnlineProvider);
            if (wSortHost.Child != null) { wSortHost.Child.Opacity = searching ? 0.4 : 1; wSortHost.Child.IsHitTestVisible = !searching; }
            wSortHost.ToolTip = searching ? "Searches show the best matches from everything. Clear the search to browse Top / Hot / New / Random." : null;
            if (wBestMatch != null) wBestMatch.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        }

        // General · Anime · People  |  SFW · Sketchy · NSFW — each toggles one bit; at least one of
        // each group stays on. Changes reload the results.
        void RenderWallFilters()
        {
            if (wFilters == null) return;
            wFilters.Children.Clear();
            bool wh = S.OnlineProvider == "wallhaven";
            wFilters.Visibility = wh ? Visibility.Visible : Visibility.Collapsed;
            if (!wh) return;
            string cats = (S.WallhavenCategories ?? "111").PadRight(3, '1').Substring(0, 3);
            WallSortState();
            string pur = OnlineWallpapers.PurityBits();
            Func<string, string, bool, Brush, Action, Border> chip = (glyph, label, on, onBg, click) =>
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                if (glyph != null) { var gl = K.Glyph(glyph, 10, on ? Theme.OnAccent : Theme.Text3); gl.Margin = new Thickness(0, 0, 5, 0); sp.Children.Add(gl); }
                sp.Children.Add(K.TB(label, 11.5, on ? Theme.OnAccent : Theme.Text2, FontWeights.SemiBold));
                var b = new Border { Height = 26, CornerRadius = new CornerRadius(13), Padding = new Thickness(11, 0, 11, 0), Margin = new Thickness(0, 0, 6, 0), Background = on ? onBg : Theme.Raised, Child = sp, Cursor = Cursors.Hand };
                K.Pressable(b, () => { Pin(); click(); }, false);
                return b;
            };
            string[] cn = { "General", "Anime", "People" };
            var catChips = new List<Border>();
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                catChips.Add(chip(null, cn[i], cats[i] == '1', Theme.AccentFill, () =>
                {
                    var c = cats.ToCharArray(); c[idx] = c[idx] == '1' ? '0' : '1';
                    if (new string(c) == "000") { wStatus.Text = "Keep at least one category on."; return; }
                    S.WallhavenCategories = new string(c); Store.Save(); RenderWallFilters(); LoadOnline(true);
                }));
                wFilters.Children.Add(catChips[i]);
            }

            wFilters.Children.Add(new Border { Width = 1, Height = 18, Background = Theme.Hairline, Margin = new Thickness(4, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            string[] pn = { "SFW", "Sketchy", "NSFW" };
            Brush[] pb = { Theme.AccentFill, K.Solid(Theme.Current.Orange), K.Solid(Theme.Current.Red) };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                wFilters.Children.Add(chip(i == 2 ? "\uE7BA" : null, pn[i], pur[i] == '1', pb[i], () =>
                {
                    var c = pur.ToCharArray(); c[idx] = c[idx] == '1' ? '0' : '1';
                    if (new string(c) == "000") { wStatus.Text = "Keep at least one of SFW / Sketchy / NSFW on."; return; }
                    S.WallhavenPurityBits = new string(c); Store.Save(); RenderWallFilters();
                    if (c[2] == '1' && (Store.Unprotect(S.WallhavenKeyEnc) ?? "").Trim().Length == 0)
                    {
                        wGrid.Children.Clear(); wTiles.Clear(); wOnline.Clear();
                        wStatus.Text = "NSFW needs your Wallhaven API key: Settings → Launcher & Wallpapers.";
                        return;
                    }
                    LoadOnline(true);
                }));
            }
        }

        void OpenWallpapers()
        {
            wCurrent = WallpaperService.Current();
            Transition(vWall);
            SetViewSize(vWall, WallW, WallH);
            if (wTab == "online") { wTabSeg.Select(1, false); ShowOnline(); if (wOnline.Count == 0) LoadOnline(true); }
            else ShowLocal();
        }

        void SwitchWallTab(string tab)
        {
            if (wTab == tab) return;
            wTab = tab;
            wTabSeg.Select(tab == "local" ? 0 : 1, false);
            if (tab == "local") ShowLocal();
            else { ShowOnline(); if (wOnline.Count == 0) LoadOnline(true); }
            K.FadeIn(wScroll, 10, 300);
        }

        void ShowLocal()
        {
            wOnlineBar.Visibility = Visibility.Collapsed; wRandomBtn.Visibility = Visibility.Visible;
            wFiles = WallpaperService.List(S.WallpaperDir);
            wHeader.Text = wFiles.Count + " images · " + System.IO.Path.GetFileName(S.WallpaperDir.TrimEnd('\\'));
            wHeader.ToolTip = S.WallpaperDir;
            wStatus.Text = "Click to apply · double-click to apply & close · arrows + Enter · R shuffle · Tab online";
            wGrid.Children.Clear(); wTiles.Clear();
            wSel = Math.Max(0, wFiles.FindIndex(f => string.Equals(f, wCurrent, StringComparison.OrdinalIgnoreCase)));
            int i = 0;
            foreach (var f in wFiles) { var tile = WallTile(f, i++); wTiles.Add(tile); wGrid.Children.Add(tile); }
            // a dashed "find more" tile at the end leads to the Online tab
            wGrid.Children.Add(MoreTile(wFiles.Count == 0 ? "Find wallpapers online" : "Find more online", () => SwitchWallTab("online")));
            if (wFiles.Count == 0)
            {
                var t = K.TB("This folder has no images yet. Try the Online tab, or pick a folder in Settings → Launcher & Wallpapers.", 12.5, Theme.Text2, FontWeights.Normal);
                t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(6, 0, 6, 12); t.Width = WallW - 50;
                wGrid.Children.Insert(0, t);
            }
            HighlightWall();
            Motion.Stagger(wGrid, 12, 18);
            var files = wFiles.ToList(); var tiles = wTiles.ToList(); int gen = ++wGen;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                for (int k = 0; k < files.Count && gen == wGen; k++)
                {
                    var bi = WallpaperService.Thumb(files[k], 260);
                    int idx = k;
                    if (bi != null) Dispatcher.BeginInvoke(new Action(() => { if (gen == wGen && idx < tiles.Count) SetTileImage(tiles[idx], bi); }));
                }
            });
        }

        static void SetTileImage(Border tile, ImageSource bi)
        {
            var ib = new ImageBrush(bi) { Stretch = Stretch.UniformToFill }; ib.Freeze();
            var img = (Border)((Grid)tile.Child).Children[0];
            img.Background = ib; img.Opacity = 0; img.BeginAnimation(OpacityProperty, K.Anim(1, 320, K.OutCubic));
        }

        Border TileShell(string tip, out Grid g)
        {
            g = new Grid();
            var img = new Border { CornerRadius = new CornerRadius(10), Background = Theme.Raised };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            g.Children.Add(img);
            var b = new Border { Width = 198, Height = 114, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Child = g, Cursor = Cursors.Hand, ToolTip = tip, RenderTransformOrigin = new Point(0.5, 0.5) };
            var st = new ScaleTransform(1, 1); b.RenderTransform = st;
            b.MouseEnter += delegate { K.Pop(st, 1.035, 220, K.OutCubic); };
            b.MouseLeave += delegate { K.Pop(st, 1, 220, K.OutCubic); };
            return b;
        }

        Border MoreTile(string label, Action act)
        {
            var g = new Grid();
            g.Children.Add(new Rectangle { RadiusX = 10, RadiusY = 10, Stroke = Theme.Text3, StrokeThickness = 1.4, StrokeDashArray = new DoubleCollection { 4, 3 } });
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(K.Glyph("\uE774", 18, Theme.Text2));
            var t = K.TB(label, 11.5, Theme.Text2, FontWeights.SemiBold); t.Margin = new Thickness(0, 6, 0, 0); t.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(t);
            g.Children.Add(sp);
            var b = new Border { Width = 198, Height = 114, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Child = g, Cursor = Cursors.Hand, RenderTransformOrigin = new Point(0.5, 0.5) };
            K.Pressable(b, () => { Pin(); act(); }, true);
            return b;
        }

        // "✓ Current" on the wallpaper in use
        static Border CurrentBadge()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(K.Glyph("\uE73E", 9, Theme.OnAccent));
            var t = K.TB("Current", 10, Theme.OnAccent, FontWeights.SemiBold); t.Margin = new Thickness(4, 0, 0, 0);
            sp.Children.Add(t);
            return new Border { Height = 20, CornerRadius = new CornerRadius(10), Background = Theme.AccentFill, Padding = new Thickness(7, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0), Child = sp, Tag = "check", IsHitTestVisible = false };
        }

        // round icon button that sits on top of a picture
        static Border OnImageBtn(string glyph, string tip, Action act)
        {
            var b = K.IconBtn(glyph, 26, 11, Brushes.White, act);
            b.Background = K.B(150, 0, 0, 0); b.ToolTip = tip; b.Margin = new Thickness(4, 0, 0, 0);
            return b;
        }

        // hover overlay: a soft dark fade at the bottom with a label and action buttons
        static void TileOverlay(Border tile, Grid g, string label, params Border[] buttons)
        {
            var ov = new Grid { Opacity = 0 };
            var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(200, 0, 0, 0), 1));
            fade.Freeze();
            ov.Children.Add(new Border { Height = 50, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(0, 0, 10, 10), Background = fade, IsHitTestVisible = false });
            var row = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(9, 0, 6, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (!string.IsNullOrEmpty(label)) { var t = K.TB(label, 10.5, Brushes.White, FontWeights.SemiBold); t.Margin = new Thickness(0, 0, 6, 4); t.VerticalAlignment = VerticalAlignment.Bottom; row.Children.Add(t); }
            var bs = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var b in buttons) bs.Children.Add(b);
            Grid.SetColumn(bs, 1); row.Children.Add(bs);
            ov.Children.Add(row);
            g.Children.Add(ov);
            tile.MouseEnter += delegate { ov.BeginAnimation(OpacityProperty, K.Anim(1, 160, K.OutCubic)); };
            tile.MouseLeave += delegate { ov.BeginAnimation(OpacityProperty, K.Anim(0, 200, K.OutCubic)); };
        }

        void PickWallpaperFolder()
        {
            try
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = S.WallpaperDir, Description = "Wallpaper folder" })
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && Directory.Exists(dlg.SelectedPath))
                    {
                        S.WallpaperDir = dlg.SelectedPath; Store.Save();
                        if (wTab == "local") { ShowLocal(); K.FadeIn(wScroll, 10, 300); } else ShowOnline();
                    }
            }
            catch { }
        }

        Border WallTile(string file, int idx)
        {
            Grid g;
            var b = TileShell(System.IO.Path.GetFileName(file), out g);
            bool isCur = string.Equals(file, wCurrent, StringComparison.OrdinalIgnoreCase);
            var check = CurrentBadge(); check.Visibility = isCur ? Visibility.Visible : Visibility.Collapsed;
            g.Children.Add(check);
            TileOverlay(b, g, System.IO.Path.GetFileNameWithoutExtension(file),
                OnImageBtn("\uE8B7", "Show in folder", () => { try { Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { } }));
            b.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true; wSel = idx; HighlightWall();
                if (e.ClickCount == 2) { ApplyWallpaper(file, true); Collapse(); }
                else ApplyWallpaper(file, false);
            };
            return b;
        }

        void HighlightWall()
        {
            for (int i = 0; i < wTiles.Count; i++) wTiles[i].BorderBrush = i == wSel ? (Brush)Theme.Accent : Brushes.Transparent;
            if (wSel >= 0 && wSel < wTiles.Count) wTiles[wSel].BringIntoView();
        }

        void ApplyWallpaper(string file, bool announce)
        {
            wCurrent = file;
            if (wTab == "local")
                for (int i = 0; i < wTiles.Count && i < wFiles.Count; i++)
                    foreach (var c in ((Grid)wTiles[i].Child).Children.OfType<Border>())
                        if ((c.Tag as string) == "check")
                        {
                            bool on = string.Equals(wFiles[i], file, StringComparison.OrdinalIgnoreCase);
                            bool was = c.Visibility == Visibility.Visible;
                            c.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                            if (on && !was) K.PopIn(c, 0.5, 380);
                        }
            WallpaperService.Apply(file, S.WallpaperFit, () => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (S.Palette == "wallpaper") Theme.Apply(Store.ResolvePalette(), true);
                if (announce) Post(new IslandEvent { Kind = "system", Image = WallpaperService.Thumb(file, 80), Title = "Wallpaper changed", Sub = System.IO.Path.GetFileName(file), Priority = Pri.Notification, Duration = 2200, Key = "wallpaper", Queueable = true });
            })));
        }

        void RandomWallpaper()
        {
            if (wTab != "local" || wFiles.Count == 0) return;
            var rnd = new Random();
            int i = rnd.Next(wFiles.Count);
            if (wFiles.Count > 1) while (string.Equals(wFiles[i], wCurrent, StringComparison.OrdinalIgnoreCase)) i = rnd.Next(wFiles.Count);
            wSel = i; HighlightWall(); ApplyWallpaper(wFiles[i], false);
        }

        // ---- online
        void ShowOnline()
        {
            wOnlineBar.Visibility = Visibility.Visible; wRandomBtn.Visibility = Visibility.Collapsed;
            wHeader.Text = "From " + OnlineWallpapers.Label(S.OnlineProvider) + " · saved to " + System.IO.Path.GetFileName(S.WallpaperDir.TrimEnd('\\'));
            wGrid.Children.Clear(); wTiles.Clear();
            int gen = wGen;
            for (int i = 0; i < wOnline.Count; i++)
            {
                var t = OnlineTile(wOnline[i], i); wTiles.Add(t); wGrid.Children.Add(t);
                // thumbnails are cached on the item; reload any that never arrived
                var ww = wOnline[i]; var tt = t;
                if (ww.ThumbImg != null) SetTileImage(tt, ww.ThumbImg);
                else ThreadPool.QueueUserWorkItem(__ => { var bi = OnlineWallpapers.Thumb(ww); if (bi != null) Dispatcher.BeginInvoke(new Action(() => { ww.ThumbImg = bi; if (wTiles.Contains(tt)) SetTileImage(tt, bi); })); });
            }
            wSel = 0; HighlightWall();
            wStatus.Text = wOnline.Count > 0 ? wOnline.Count + " wallpapers · click to apply · hover for download / open page · Tab local" : "";
        }

        void LoadOnline(bool reset)
        {
            if (reset) { wOnline.Clear(); wPage = 0; wMore = true; wGrid.Children.Clear(); wTiles.Clear(); wScroll.ScrollToTop(); }
            if (!wMore) return;
            wLoading = true;
            int gen = ++wGen; int page = ++wPage;
            string prov = S.OnlineProvider, q = wSearch.Text; int sort = S.OnlineSort;
            wHeader.Text = "From " + OnlineWallpapers.Label(prov) + " · saved to " + System.IO.Path.GetFileName(S.WallpaperDir.TrimEnd('\\'));
            wStatus.Text = "Loading " + OnlineWallpapers.Label(prov) + "…";
            // skeleton tiles while the first page loads
            var skeletons = new List<Border>();
            if (reset)
                for (int i = 0; i < 9; i++)
                {
                    Grid sg; var sk = TileShell(null, out sg); sk.Cursor = Cursors.Arrow;
                    var pulse = new DoubleAnimation(0.35, 0.8, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(i * 60) };
                    sg.Children[0].BeginAnimation(OpacityProperty, pulse);
                    skeletons.Add(sk); wGrid.Children.Add(sk);
                }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<OnlineWall> list = null; bool more = false; string err = null;
                try { list = OnlineWallpapers.Fetch(prov, sort, q, page, out more); }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (gen != wGen || wTab != "online") return;
                    foreach (var sk in skeletons) wGrid.Children.Remove(sk);
                    wLoading = false;
                    if (err != null) { wStatus.Text = "Couldn't load " + OnlineWallpapers.Label(prov) + ": " + err; wMore = false; return; }
                    wMore = more;
                    int start = wOnline.Count;
                    foreach (var w in list)
                    {
                        if (wOnline.Any(x => x.Provider == w.Provider && x.Id == w.Id)) continue;
                        wOnline.Add(w);
                        var tile = OnlineTile(w, wOnline.Count - 1);
                        wTiles.Add(tile); wGrid.Children.Add(tile);
                        K.FadeIn(tile, 10, 320);
                        var ww = w; var tt = tile;
                        ThreadPool.QueueUserWorkItem(__ =>
                        {
                            var bi = OnlineWallpapers.Thumb(ww);
                            if (bi != null) Dispatcher.BeginInvoke(new Action(() => { ww.ThumbImg = bi; if (wTiles.Contains(tt)) SetTileImage(tt, bi); }));
                        });
                    }
                    if (wOnline.Count == 0) wStatus.Text = "Nothing found.";
                    else wStatus.Text = wOnline.Count + " wallpapers" + (wMore ? " · scroll for more" : "") + " · click to apply · hover for download / open page";
                    if (start == 0) HighlightWall();
                }));
            });
        }

        Border OnlineTile(OnlineWall w, int idx)
        {
            Grid g;
            var b = TileShell((w.Title ?? "") + (string.IsNullOrEmpty(w.Res) ? "" : "  ·  " + w.Res), out g);
            if (!string.IsNullOrEmpty(w.Res) && w.Res != "x")
            {
                var res = new Border { CornerRadius = new CornerRadius(6), Background = K.B(170, 0, 0, 0), Padding = new Thickness(6, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6), Child = K.TB(w.Res, 10, Brushes.White, FontWeights.SemiBold) };
                g.Children.Add(res);
            }
            var prog = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.AccentFill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 6, 4), Width = 0, Visibility = Visibility.Collapsed };
            g.Children.Add(prog);
            if (w.Nsfw)
            {
                // NSFW: a red tag, and (by default) the thumbnail stays blurred until hovered
                g.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = K.Solid(Theme.A(Theme.Current.Red, 0.9)), Padding = new Thickness(6, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6), Child = K.TB("NSFW", 9.5, Brushes.White, FontWeights.Bold) });
                if (S.WallhavenBlurNsfw)
                {
                    var img = (Border)g.Children[0];
                    var blur = new System.Windows.Media.Effects.BlurEffect { Radius = 26, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
                    img.Effect = blur;
                    b.MouseEnter += delegate { blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, K.Anim(0, 220, K.OutCubic)); };
                    b.MouseLeave += delegate { blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, K.Anim(26, 260, K.OutCubic)); };
                }
            }
            TileOverlay(b, g, null,
                OnImageBtn("\uE896", "Download only", () => { wSel = idx; HighlightWall(); DownloadOnline(w, prog, false); }),
                OnImageBtn("\uE8A7", "Open the page", () => { if (!string.IsNullOrEmpty(w.Page)) AppIndex.Open(w.Page); }),
                OnImageBtn("\uE73E", "Download & apply", () => { wSel = idx; HighlightWall(); DownloadOnline(w, prog, true); }));
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; wSel = idx; HighlightWall(); DownloadOnline(w, prog, true); };
            b.MouseRightButtonUp += (s, e) => { e.Handled = true; wSel = idx; HighlightWall(); DownloadOnline(w, prog, false); };
            b.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Middle && !string.IsNullOrEmpty(w.Page)) { e.Handled = true; AppIndex.Open(w.Page); } };
            return b;
        }

        void DownloadOnline(OnlineWall w, Border prog, bool apply)
        {
            if (prog.Tag as string == "busy") return;
            prog.Tag = "busy"; prog.Visibility = Visibility.Visible; prog.Width = 0;
            double maxW = 198 - 16;
            wStatus.Text = "Downloading " + (w.Title ?? w.Id) + "…";
            string dir = S.WallpaperDir;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string path = null, err = null;
                try { path = OnlineWallpapers.Download(w, dir, f => Dispatcher.BeginInvoke(new Action(() => prog.Width = Math.Max(4, maxW * f)))); }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    prog.Tag = null;
                    if (err != null) { prog.Visibility = Visibility.Collapsed; wStatus.Text = "Download failed: " + err; return; }
                    prog.Width = maxW;
                    var fade = K.Anim(0, 600, K.OutCubic); fade.BeginTime = TimeSpan.FromMilliseconds(300);
                    fade.Completed += delegate { prog.Visibility = Visibility.Collapsed; prog.BeginAnimation(OpacityProperty, null); prog.Opacity = 1; };
                    prog.BeginAnimation(OpacityProperty, fade);
                    if (apply) { ApplyWallpaper(path, false); wStatus.Text = "Applied · saved as " + System.IO.Path.GetFileName(path); }
                    else wStatus.Text = "Saved to " + path;
                }));
            });
        }

        void WallpaperKey(KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox && e.Key != Key.Tab) return;
            int cols = 3, n = wTiles.Count;
            if (e.Key == Key.Tab) { e.Handled = true; SwitchWallTab(wTab == "local" ? "online" : "local"); return; }
            if (n == 0) return;
            if (e.Key == Key.Right) { e.Handled = true; wSel = Math.Min(n - 1, wSel + 1); HighlightWall(); }
            else if (e.Key == Key.Left) { e.Handled = true; wSel = Math.Max(0, wSel - 1); HighlightWall(); }
            else if (e.Key == Key.Down) { e.Handled = true; wSel = Math.Min(n - 1, wSel + cols); HighlightWall(); }
            else if (e.Key == Key.Up) { e.Handled = true; wSel = Math.Max(0, wSel - cols); HighlightWall(); }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (wTab == "local" && wSel < wFiles.Count) { ApplyWallpaper(wFiles[wSel], true); Collapse(); }
                else if (wTab == "online" && wSel < wOnline.Count)
                {
                    var prog = ((Grid)wTiles[wSel].Child).Children.OfType<Border>().LastOrDefault(x => x.Height == 4);
                    if (prog != null) DownloadOnline(wOnline[wSel], prog, true);
                }
            }
            else if (e.Key == Key.R) { e.Handled = true; RandomWallpaper(); }
        }
    }

    static class Launcher
    {
        public static void Open(string target) { AppIndex.Open(target); }
    }

    // ================================================================ markdown
    // Markdown for AI replies: headings, paragraphs (single newlines kept), nested/ordered/task lists,
    // blockquotes, tables, fenced code (language label + copy), rules, and nested inline styles
    // (bold, italic, strike, code, links, bare URLs, escapes).
    static class Markdown
    {
        const double Body = 13;

        public static FrameworkElement Render(string md, double maxW)
        {
            var sp = new StackPanel { MaxWidth = maxW };
            var lines = (md ?? "").Replace("\r", "").Replace("\t", "    ").Split('\n');
            Block(lines, sp, maxW);
            return sp;
        }

        static void Add(StackPanel sp, FrameworkElement el, double gap)
        {
            var m = el.Margin;
            el.Margin = new Thickness(m.Left, m.Top + (sp.Children.Count > 0 ? gap : 0), m.Right, m.Bottom);
            sp.Children.Add(el);
        }

        static readonly Regex ListRe = new Regex(@"^(\s*)([-*+•]|\d{1,3}[.)])\s+(.*)$");
        static readonly Regex SepRe = new Regex(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$");

        static bool IsFence(string t) { return t.StartsWith("```") || t.StartsWith("~~~"); }
        static bool IsSep(string l) { return l.Contains("-") && (l.Contains("|") || l.Trim().StartsWith(":")) && SepRe.IsMatch(l); }

        static void Block(string[] L, StackPanel sp, double maxW)
        {
            var para = new List<string>();
            bool inList = false;
            Action flush = () =>
            {
                if (para.Count == 0) return;
                Add(sp, Text(string.Join("\n", para), Body, FontWeights.Normal), 8);
                para.Clear();
            };
            for (int i = 0; i < L.Length; i++)
            {
                string l = L[i], tr = l.Trim();
                bool wasList = inList; inList = false;

                if (IsFence(tr))
                {
                    flush();
                    string f = tr.Substring(0, 3), lang = tr.Substring(3).Trim();
                    var code = new List<string>();
                    i++;
                    while (i < L.Length && !L[i].Trim().StartsWith(f)) { code.Add(L[i]); i++; }
                    Add(sp, CodeBlock(string.Join("\n", code), lang), 8);
                    continue;
                }
                if (tr.Length == 0) { flush(); continue; }

                var h = Regex.Match(tr, @"^(#{1,6})\s+(.*?)\s*#*$");
                if (h.Success)
                {
                    flush();
                    int n = h.Groups[1].Length;
                    double sz = n == 1 ? 17 : n == 2 ? 15.5 : n == 3 ? 14 : 13;
                    Add(sp, Text(h.Groups[2].Value, sz, n <= 2 ? FontWeights.Bold : FontWeights.SemiBold), n <= 2 ? 14 : 10);
                    continue;
                }
                if (Regex.IsMatch(tr, @"^([-*_])(\s*\1){2,}$"))
                {
                    flush();
                    Add(sp, new Border { Height = 1, Background = Theme.Hairline, Margin = new Thickness(0, 4, 0, 4) }, 8);
                    continue;
                }
                if (tr.StartsWith(">"))
                {
                    flush();
                    var q = new List<string>();
                    while (i < L.Length && L[i].Trim().StartsWith(">")) { q.Add(Regex.Replace(L[i].Trim(), @"^>\s?", "")); i++; }
                    i--;
                    var inner = new StackPanel();
                    Block(q.ToArray(), inner, maxW - 16);
                    foreach (var c in inner.Children.OfType<TextBlock>()) c.Foreground = Theme.Text2;
                    Add(sp, new Border { BorderBrush = Theme.Accent, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(11, 2, 0, 2), Child = inner }, 8);
                    continue;
                }
                if (l.Contains("|") && i + 1 < L.Length && IsSep(L[i + 1]))
                {
                    flush();
                    var head = Cells(L[i]); var al = Aligns(L[i + 1], head.Length);
                    var rows = new List<string[]>();
                    i += 2;
                    while (i < L.Length && L[i].Contains("|") && L[i].Trim().Length > 0) { rows.Add(Cells(L[i])); i++; }
                    i--;
                    Add(sp, Table(head, al, rows), 10);
                    continue;
                }
                var li = ListRe.Match(l);
                if (li.Success)
                {
                    flush();
                    int indent = li.Groups[1].Value.Length;
                    string text = li.Groups[3].Value;
                    // continuation lines (indented, not a new item / block)
                    while (i + 1 < L.Length)
                    {
                        string nx = L[i + 1];
                        if (nx.Trim().Length == 0 || ListRe.IsMatch(nx) || IsFence(nx.Trim()) || nx.Trim().StartsWith("#") || nx.Trim().StartsWith(">") || nx.Trim().StartsWith("|")) break;
                        if (nx.Length - nx.TrimStart().Length <= indent && !wasList) break;
                        text += "\n" + nx.Trim(); i++;
                    }
                    Add(sp, ListItem(li.Groups[2].Value, text, Math.Min(4, indent / 2)), wasList ? 4 : 8);
                    inList = true;
                    continue;
                }
                para.Add(tr);
            }
            flush();
        }

        static FrameworkElement ListItem(string marker, string text, int level)
        {
            var g = new Grid { Margin = new Thickness(level * 16, 0, 0, 0) };
            bool ordered = char.IsDigit(marker[0]);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ordered ? 22 : 16) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            FrameworkElement mark;
            var task = Regex.Match(text, @"^\[([ xX])\]\s+(.*)$", RegexOptions.Singleline);
            bool done = false;
            if (task.Success)
            {
                done = task.Groups[1].Value != " ";
                text = task.Groups[2].Value;
                var box = K.Glyph(done ? "" : "", 12, done ? Theme.Accent : Theme.Text2);
                box.VerticalAlignment = VerticalAlignment.Top; box.Margin = new Thickness(0, 3, 0, 0); box.HorizontalAlignment = HorizontalAlignment.Left;
                mark = box;
                g.ColumnDefinitions[0].Width = new GridLength(20);
            }
            else
            {
                string b = ordered ? marker : level == 0 ? "•" : level == 1 ? "◦" : "▪";
                var tb = new TextBlock { Text = b, FontSize = Body, FontWeight = ordered ? FontWeights.SemiBold : FontWeights.Bold, Foreground = Theme.Text2, LineHeight = Body + 6, VerticalAlignment = VerticalAlignment.Top };
                mark = tb;
            }
            var t = Text(text, Body, FontWeights.Normal);
            if (done) { t.Foreground = Theme.Text2; t.TextDecorations = TextDecorations.Strikethrough; }
            Grid.SetColumn(t, 1);
            g.Children.Add(mark); g.Children.Add(t);
            return g;
        }

        static string[] Cells(string l)
        {
            string t = l.Trim();
            if (t.StartsWith("|")) t = t.Substring(1);
            if (t.EndsWith("|") && !t.EndsWith("\\|")) t = t.Substring(0, t.Length - 1);
            return Regex.Split(t, @"(?<!\\)\|").Select(c => c.Trim().Replace("\\|", "|")).ToArray();
        }

        static TextAlignment[] Aligns(string sep, int n)
        {
            var c = Cells(sep);
            var r = new TextAlignment[n];
            for (int i = 0; i < n; i++)
            {
                string s = i < c.Length ? c[i] : "";
                r[i] = s.StartsWith(":") && s.EndsWith(":") ? TextAlignment.Center : s.EndsWith(":") ? TextAlignment.Right : TextAlignment.Left;
            }
            return r;
        }

        static FrameworkElement Table(string[] head, TextAlignment[] al, List<string[]> rows)
        {
            int n = head.Length;
            var g = new Grid();
            for (int c = 0; c < n; c++)
            {
                double w = head[c].Length;
                foreach (var r in rows) if (c < r.Length) w = Math.Max(w, r[c].Length);
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(4, Math.Min(36, w)), GridUnitType.Star) });
            }
            int total = rows.Count + 1;
            for (int r = 0; r < total; r++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var cells = r == 0 ? head : rows[r - 1];
                for (int c = 0; c < n; c++)
                {
                    var t = Text(c < cells.Length ? cells[c] : "", 12.5, r == 0 ? FontWeights.SemiBold : FontWeights.Normal);
                    t.LineHeight = 18; t.TextAlignment = al[c];
                    if (r == 0) t.Foreground = Theme.Text;
                    var cell = new Border
                    {
                        Child = t, Padding = new Thickness(10, 6, 10, 6),
                        BorderBrush = Theme.Hairline, BorderThickness = new Thickness(c > 0 ? 1 : 0, 0, 0, r < total - 1 ? 1 : 0),
                        Background = r == 0 ? (Brush)Theme.Raised : Brushes.Transparent
                    };
                    Grid.SetRow(cell, r); Grid.SetColumn(cell, c);
                    g.Children.Add(cell);
                }
            }
            // rounded frame; the clip keeps the header fill inside the corners
            var frame = new Border { CornerRadius = new CornerRadius(10), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), Background = Theme.Surface, Child = g };
            frame.SizeChanged += delegate { g.Clip = new RectangleGeometry(new Rect(0, 0, g.ActualWidth, g.ActualHeight), 9, 9); };
            return frame;
        }

        static FrameworkElement CodeBlock(string code, string lang)
        {
            var sp = new StackPanel();
            var head = new Grid { Margin = new Thickness(12, 6, 6, 0), Height = 22 };
            var lt = K.TB(lang.Length > 0 ? lang.ToLowerInvariant() : "code", 10.5, Theme.Text3, FontWeights.Medium); lt.FontFamily = K.Mono;
            head.Children.Add(lt);
            var cg = K.Glyph("", 10, Theme.Text2);
            var ct = K.TB("Copy", 10.5, Theme.Text2, FontWeights.SemiBold); ct.Margin = new Thickness(5, 0, 0, 0);
            var cs = new StackPanel { Orientation = Orientation.Horizontal }; cs.Children.Add(cg); cs.Children.Add(ct);
            var copy = new Border { Height = 22, CornerRadius = new CornerRadius(11), Padding = new Thickness(8, 0, 8, 0), Background = Brushes.Transparent, Child = cs, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right };
            K.Pressable(copy, () =>
            {
                try { Clipboard.SetText(code); } catch { }
                cg.Text = ""; cg.Foreground = Theme.Green; ct.Text = "Copied";
                var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
                tm.Tick += delegate { tm.Stop(); cg.Text = ""; cg.Foreground = Theme.Text2; ct.Text = "Copy"; };
                tm.Start();
            }, true);
            head.Children.Add(copy);
            sp.Children.Add(head);
            var body = new TextBlock { Text = code, FontFamily = K.Mono, FontSize = 12, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(12, 2, 12, 10) };
            sp.Children.Add(body);
            return new Border { Background = Theme.Surface, CornerRadius = new CornerRadius(10), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), Child = sp };
        }

        static TextBlock Text(string s, double size, FontWeight w)
        {
            var tb = new TextBlock { FontSize = size, FontWeight = w, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, LineHeight = size + 6 };
            var parts = s.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) tb.Inlines.Add(new LineBreak());
                // a trailing backslash / double space is a hard break too; just drop it
                foreach (var inl in Inlines(parts[i].TrimEnd('\\').TrimEnd())) tb.Inlines.Add(inl);
            }
            return tb;
        }

        static readonly Regex InlineRe = new Regex(
            @"(?<tick>`+)(?<code>.+?)\k<tick>" +
            @"|\*\*(?<b1>(?=\S).+?(?<=\S))\*\*" +
            @"|(?<![\w])__(?<b2>(?=\S).+?(?<=\S))__(?![\w])" +
            @"|~~(?<st>(?=\S).+?(?<=\S))~~" +
            @"|\[(?<lt>[^\]]+)\]\((?<lu>[^)\s]+)(?:\s+""[^""]*"")?\)" +
            @"|(?<url>https?://[^\s<>()\[\]]+[^\s<>()\[\].,;:!?'""*_])" +
            @"|(?<![\w*])\*(?<i1>[^*\s](?:[^*]*?[^*\s])?)\*(?![\w*])" +
            @"|(?<![\w])_(?<i2>[^_\s](?:[^_]*?[^_\s])?)_(?![\w])" +
            @"|\\(?<esc>[\\`*_{}\[\]()#+\-.!|~>])");

        static List<Inline> Inlines(string s)
        {
            var list = new List<Inline>();
            int pos = 0;
            foreach (Match m in InlineRe.Matches(s))
            {
                if (m.Index > pos) list.Add(new Run(s.Substring(pos, m.Index - pos)));
                if (m.Groups["code"].Success)
                    list.Add(new Run(" " + m.Groups["code"].Value.Trim() + " ") { FontFamily = K.Mono, FontSize = 12, Background = Theme.Raised });
                else if (m.Groups["b1"].Success || m.Groups["b2"].Success)
                    list.Add(Wrap(new Bold(), m.Groups["b1"].Success ? m.Groups["b1"].Value : m.Groups["b2"].Value));
                else if (m.Groups["st"].Success)
                {
                    var sp = Wrap(new Span(), m.Groups["st"].Value);
                    sp.TextDecorations = TextDecorations.Strikethrough; sp.Foreground = Theme.Text2;
                    list.Add(sp);
                }
                else if (m.Groups["lt"].Success) list.Add(Link(Inlines(m.Groups["lt"].Value), m.Groups["lu"].Value));
                else if (m.Groups["url"].Success) list.Add(Link(new List<Inline> { new Run(m.Groups["url"].Value) }, m.Groups["url"].Value));
                else if (m.Groups["i1"].Success || m.Groups["i2"].Success)
                    list.Add(Wrap(new Italic(), m.Groups["i1"].Success ? m.Groups["i1"].Value : m.Groups["i2"].Value));
                else if (m.Groups["esc"].Success) list.Add(new Run(m.Groups["esc"].Value));
                pos = m.Index + m.Length;
            }
            if (pos < s.Length) list.Add(new Run(s.Substring(pos)));
            return list;
        }

        static Span Wrap(Span sp, string inner)
        {
            foreach (var x in Inlines(inner)) sp.Inlines.Add(x);
            return sp;
        }

        static Hyperlink Link(List<Inline> content, string url)
        {
            var link = new Hyperlink { Foreground = Theme.Accent, TextDecorations = null, Cursor = Cursors.Hand, ToolTip = url };
            foreach (var x in content) link.Inlines.Add(x);
            link.MouseEnter += delegate { link.TextDecorations = TextDecorations.Underline; };
            link.MouseLeave += delegate { link.TextDecorations = null; };
            link.Click += delegate { AppIndex.Open(url); };
            return link;
        }
    }
}
