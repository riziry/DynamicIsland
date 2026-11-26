using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynIsland
{
    // Content choreography on top of the island's morph:
    //  - compact views: the leading/trailing parts glide out from the centre as the pill opens and
    //    tuck back towards it as it closes, so the content rides the shape's edges
    //  - open pages / grids: rows rise into place one after another
    // All of it runs as render-side animations on a TranslateTransform this class owns (other code's
    // transforms are left alone) plus an opacity fade that hands the element back its own value at
    // the end: nothing is left pinned at 0 by a held animation.
    static class Motion
    {
        static readonly IEasingFunction OutQuint = new QuinticEase { EasingMode = EasingMode.EaseOut };
        static readonly IEasingFunction OutCubic = new CubicEase { EasingMode = EasingMode.EaseOut };
        static readonly IEasingFunction InCubic = new CubicEase { EasingMode = EasingMode.EaseIn };
        static readonly ConditionalWeakTable<TranslateTransform, object> owned = new ConditionalWeakTable<TranslateTransform, object>();

        // Off with the Settings toggle, with animations off, and in the Light animation mode.
        public static bool Enabled { get { return Store.S.ContentMotion && K.AnimScale > 0 && Store.S.AnimMode != 1; } }

        // ---- compact views: centre-out ------------------------------------------------------

        public static void EarsIn(FrameworkElement root, double width)
        {
            if (!Enabled || root == null) return;
            foreach (var part in Parts(root, width))
            {
                double d = Side(part, root, width);
                if (Math.Abs(d) < 0.2) { Enter(part, 0, 5, 0, 420); continue; }   // centred content: a small rise
                Enter(part, -d * 18, 0, 0, 540);
            }
        }

        public static void EarsOut(FrameworkElement root, double width)
        {
            if (!Enabled || root == null) return;
            foreach (var part in Parts(root, width))
            {
                double d = Side(part, root, width);
                if (Math.Abs(d) < 0.2) continue;
                var t = Mine(part); if (t == null) continue;
                // Stop: snaps home once the view has faded out, so nothing is left offset
                var a = new DoubleAnimation { To = -d * 10, Duration = Dur(170), EasingFunction = InCubic, FillBehavior = FillBehavior.Stop };
                t.BeginAnimation(TranslateTransform.XProperty, a, HandoffBehavior.SnapshotAndReplace);
            }
        }

        // ---- rows ------------------------------------------------------------------------

        // Children of a panel, one after another in their own order (grids of tiles).
        public static void Stagger(Panel p, double fromY, int step)
        {
            if (!Enabled || p == null) return;
            int i = 0;
            foreach (UIElement c in p.Children)
            {
                if (c.Visibility != Visibility.Visible) continue;
                Enter(c, 0, fromY, 60 + Math.Min(i, 16) * step, 440);
                i++;
            }
        }

        // Children of a panel in beats from top to bottom: elements sharing a top edge (side by side,
        // or layered in one Grid cell) move together.
        public static void Cascade(Panel p, double fromY, int step, int baseDelay)
        {
            if (!Enabled || p == null) return;
            var kids = p.Children.OfType<UIElement>().Where(c => c.Visibility == Visibility.Visible).ToList();
            var order = new List<KeyValuePair<UIElement, double>>();
            for (int i = 0; i < kids.Count; i++)
            {
                double y = i * 10000;
                try { if (kids[i].RenderSize.Height > 0) y = kids[i].TranslatePoint(new Point(0, 0), p).Y; } catch { }
                order.Add(new KeyValuePair<UIElement, double>(kids[i], y));
            }
            var tops = new List<double>();
            foreach (var kv in order.OrderBy(o => o.Value))
            {
                double y = kv.Value;
                int beat = tops.FindIndex(t => Math.Abs(t - y) < 6);
                if (beat < 0) { tops.Add(y); beat = tops.Count - 1; }
                Enter(kv.Key, 0, fromY, baseDelay + Math.Min(beat, 7) * step, 460);
            }
        }

        // ---- building blocks ------------------------------------------------------------------

        // Slide in from (dx, dy) and fade up to the element's own opacity, after delay ms.
        static void Enter(UIElement el, double dx, double dy, int delay, int ms)
        {
            var t = Mine(el);
            if (t != null)
            {
                if (Math.Abs(dx) > 0.01) t.BeginAnimation(TranslateTransform.XProperty, Held(dx, 0, delay, ms, OutQuint), HandoffBehavior.SnapshotAndReplace);
                if (Math.Abs(dy) > 0.01) t.BeginAnimation(TranslateTransform.YProperty, Held(dy, 0, delay, ms, OutQuint), HandoffBehavior.SnapshotAndReplace);
            }
            // opacity lives on the element itself: only when nobody else is animating it
            if (OpacityBusy(el)) return;
            double to = el.Opacity;
            if (to < 0.05) return;
            el.BeginAnimation(UIElement.OpacityProperty, Held(0, to, delay, Math.Min(ms, 320), OutCubic), HandoffBehavior.SnapshotAndReplace);
        }

        // from -> to after a delay, holding `from` during the delay (a plain BeginTime would show the
        // resting value first and then jump), then handing the property back (FillBehavior.Stop; the
        // end value is the resting value, so there's no jump at the end either).
        static AnimationTimeline Held(double from, double to, int delay, int ms, IEasingFunction ease)
        {
            int d = K.Dur(delay), m = Math.Max(1, K.Dur(ms));
            var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, Duration = new Duration(TimeSpan.FromMilliseconds(d + m)) };
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (d > 0) a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(d))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(d + m)), ease));
            return a;
        }

        static Duration Dur(int ms) { return new Duration(TimeSpan.FromMilliseconds(Math.Max(1, K.Dur(ms)))); }

        static bool OpacityBusy(UIElement el)
        {
            if (!el.HasAnimatedProperties) return false;
            var b = el.GetAnimationBaseValue(UIElement.OpacityProperty);
            return b is double && Math.Abs(el.Opacity - (double)b) > 0.001;
        }

        // Our translate on el: reuses ours, adopts an empty slot, and leaves elements whose transform
        // belongs to someone else (press scale, their own slide) alone.
        static TranslateTransform Mine(UIElement el)
        {
            var cur = el.RenderTransform;
            var t = cur as TranslateTransform;
            object o;
            if (t != null && owned.TryGetValue(t, out o)) return t;
            var mt = cur as MatrixTransform;
            if (cur != null && cur != Transform.Identity && !(mt != null && mt.Matrix.IsIdentity)) return null;
            t = new TranslateTransform();
            owned.Add(t, null);
            el.RenderTransform = t;
            return t;
        }

        // -1 = left edge, 0 = centre, 1 = right edge
        static double Side(FrameworkElement part, FrameworkElement root, double width)
        {
            if (width <= 1 || part.ActualWidth <= 0) return 0;
            try
            {
                var p = part.TranslatePoint(new Point(part.ActualWidth / 2, 0), root);
                return Math.Max(-1, Math.Min(1, (p.X - width / 2) / (width / 2)));
            }
            catch { return 0; }
        }

        // The pieces of a compact view: the first level with several visible children, with any
        // full-width plain panel among them opened up once more (a notification's padded grid holds
        // icon | text | close; moving it as a whole would just be a rise).
        static IEnumerable<FrameworkElement> Parts(FrameworkElement root, double width)
        {
            var res = new List<FrameworkElement>();
            foreach (var p in Level(root))
            {
                var ty = p.GetType();
                var panel = p as Panel;
                if (panel != null && (ty == typeof(Grid) || ty == typeof(StackPanel)) && p.ActualWidth > width * 0.7)
                {
                    var kids = panel.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible && c.ActualWidth > 0).ToList();
                    if (kids.Count >= 2) { res.AddRange(kids); continue; }
                }
                res.Add(p);
            }
            return res;
        }

        // Dig through plain single-child wrappers to the first level with several visible children.
        // Custom controls (rolling clock, EQ, rings, sliders) are pieces themselves; they animate
        // their own insides.
        static IEnumerable<FrameworkElement> Level(FrameworkElement root)
        {
            UIElement cur = root;
            for (int depth = 0; depth < 6 && cur != null; depth++)
            {
                var ty = cur.GetType();
                bool plain = ty == typeof(Grid) || ty == typeof(StackPanel) || ty == typeof(DockPanel) || ty == typeof(Border);
                if (!plain) break;
                var panel = cur as Panel;
                if (panel != null)
                {
                    var vis = panel.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible && c.ActualWidth > 0).ToList();
                    if (vis.Count >= 2) return vis;
                    if (vis.Count == 0) break;
                    cur = vis[0]; continue;
                }
                var b = (Border)cur;
                if (b.Background != null) break;   // a filled box (icon tile, art) is one piece
                cur = b.Child;
            }
            var fe = cur as FrameworkElement;
            return fe != null && fe != root ? new[] { fe } : new FrameworkElement[0];
        }
    }

    // Soft edges on a scroll area: content fades out towards an edge only while there's more to
    // scroll that way, and the fade grows with the distance scrolled (up to `px`), so it never pops
    // in and an unscrolled list keeps its first row fully lit. No mask at all when nothing overflows.
    static class ScrollFade
    {
        public static void Attach(ScrollViewer sv, double px, bool horizontal)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = horizontal ? new Point(1, 0) : new Point(0, 1) };
            var opaque = Color.FromArgb(255, 0, 0, 0);
            for (int i = 0; i < 4; i++) brush.GradientStops.Add(new GradientStop(opaque, i < 2 ? 0 : 1));
            Action update = () =>
            {
                double len = horizontal ? sv.ViewportWidth : sv.ViewportHeight;
                double off = horizontal ? sv.HorizontalOffset : sv.VerticalOffset;
                double max = horizontal ? sv.ScrollableWidth : sv.ScrollableHeight;
                if (len <= 1 || max <= 0.5) { if (sv.OpacityMask != null) sv.OpacityMask = null; return; }
                double a = Math.Min(px, Math.Max(0, off)) / len, b = Math.Min(px, Math.Max(0, max - off)) / len;
                var st = brush.GradientStops;
                // the edge stop goes fully transparent as soon as the fade has any length
                st[0].Color = Color.FromArgb((byte)(a > 0.001 ? 0 : 255), 0, 0, 0);
                st[1].Offset = Math.Min(0.49, a);
                st[2].Offset = Math.Max(0.51, 1 - b);
                st[3].Color = Color.FromArgb((byte)(b > 0.001 ? 0 : 255), 0, 0, 0);
                if (sv.OpacityMask != brush) sv.OpacityMask = brush;
            };
            sv.ScrollChanged += delegate { update(); };
            sv.SizeChanged += delegate { update(); };
        }
    }
}
