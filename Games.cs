// Games page: a small arcade inside the island - Blocks (falling-block puzzle), Sudoku, Mines and
// Merge (sliding number puzzle).
// A menu of cards; each game takes over the page, sizes the island to fit, and gets the keyboard
// (via GamesKey) while it's open. Best scores/times live in Settings.GameBest.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DynIsland
{
    partial class IslandWindow
    {
        Grid gStage, gOverlay; UniformGrid gMenu; TextBlock gTitle, gStat1, gStat2, gOverTitle, gOverSub; Border gBack, gRestart, gOverBtn, gStat1Chip, gStat2Chip;
        TextBlock gOverBtnText; Action gOverAct; string game;   // null = the menu
        readonly Dictionary<string, TextBlock> gBestLabels = new Dictionary<string, TextBlock>();
        static readonly Random gRnd = new Random();

        double GamesH()
        {
            switch (game) { case "blocks": return 302; case "sudoku": return 284; case "mines": return 272; case "merge": return 330; }
            return 200;
        }

        int GameBest(string key) { int v; return S.GameBest != null && S.GameBest.TryGetValue(key, out v) ? v : 0; }
        void SetGameBest(string key, int v) { if (S.GameBest == null) S.GameBest = new Dictionary<string, int>(); S.GameBest[key] = v; Store.Save(); }

        // ================================================================ shell
        Grid BuildGamesPage()
        {
            var g = new Grid { Margin = new Thickness(16, 10, 16, 4) };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            g.RowDefinitions.Add(new RowDefinition());

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            gBack = K.IconBtn("", 26, 11, Theme.Text, CloseGame); gBack.Visibility = Visibility.Collapsed; gBack.Margin = new Thickness(-4, 0, 4, 0); gBack.ToolTip = "Back to games (Esc)";
            gTitle = K.TB("Games", 12.5, Theme.Text, FontWeights.SemiBold);
            left.Children.Add(gBack); left.Children.Add(gTitle);
            head.Children.Add(left);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Func<TextBlock[], Border> chip = t =>
            {
                t[0] = K.TB("", 11, Theme.Text2, FontWeights.SemiBold); K.Tabular(t[0]);
                return new Border { Height = 22, CornerRadius = new CornerRadius(11), Background = Theme.Raised, Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(0, 0, 6, 0), Child = t[0], Visibility = Visibility.Collapsed };
            };
            var tt = new TextBlock[1];
            gStat1Chip = chip(tt); gStat1 = tt[0];
            gStat2Chip = chip(tt); gStat2 = tt[0];
            gRestart = K.IconBtn("", 26, 11, Theme.Text2, () => { Pin(); RestartGame(); }); gRestart.Visibility = Visibility.Collapsed; gRestart.ToolTip = "New game";
            right.Children.Add(gStat1Chip); right.Children.Add(gStat2Chip); right.Children.Add(gRestart);
            Grid.SetColumn(right, 1);
            head.Children.Add(right);
            g.Children.Add(head);

            gMenu = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(-4, 6, -4, 0), VerticalAlignment = VerticalAlignment.Top };
            gMenu.Children.Add(GameCard("blocks", "Blocks", "Stack & clear lines", BlocksArt()));
            gMenu.Children.Add(GameCard("sudoku", "Sudoku", "Fill every 3×3", SudokuArt()));
            gMenu.Children.Add(GameCard("mines", "Mines", "Flag every mine", MinesArt()));
            gMenu.Children.Add(GameCard("merge", "Merge", "Slide & double up", MergeArt()));
            Grid.SetRow(gMenu, 1);
            g.Children.Add(gMenu);

            gStage = new Grid { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            Grid.SetRow(gStage, 1);
            g.Children.Add(gStage);

            // end-of-game / pause card over the stage
            gOverlay = new Grid { Visibility = Visibility.Collapsed, Background = K.Solid(Theme.A(Theme.Current.Island, 0.9)), Margin = new Thickness(0, 6, 0, 0) };
            var oc = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            gOverTitle = K.TB("", 18, Theme.Text, FontWeights.SemiBold); gOverTitle.HorizontalAlignment = HorizontalAlignment.Center;
            gOverSub = K.TB("", 11.5, Theme.Text2, FontWeights.Medium); gOverSub.HorizontalAlignment = HorizontalAlignment.Center; gOverSub.Margin = new Thickness(0, 4, 0, 0);
            gOverBtnText = K.TB("", 12.5, Theme.OnAccent, FontWeights.SemiBold); gOverBtnText.HorizontalAlignment = HorizontalAlignment.Center;
            gOverBtn = new Border { Height = 34, CornerRadius = new CornerRadius(17), Background = Theme.AccentFill, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Child = gOverBtnText, Cursor = Cursors.Hand };
            K.Pressable(gOverBtn, () => { Pin(); var a = gOverAct; HideGameOverlay(); if (a != null) a(); }, false);
            oc.Children.Add(gOverTitle); oc.Children.Add(gOverSub); oc.Children.Add(gOverBtn);
            gOverlay.Children.Add(oc);
            Grid.SetRow(gOverlay, 1);
            g.Children.Add(gOverlay);
            return g;
        }

        FrameworkElement GameCard(string id, string name, string sub, FrameworkElement art)
        {
            var bg = new SolidColorBrush(Theme.A(Theme.Current.Text, 0.05));
            var card = new Border { Height = 148, CornerRadius = new CornerRadius(16), Background = bg, Margin = new Thickness(4, 0, 4, 0), Cursor = Cursors.Hand, RenderTransformOrigin = new Point(0.5, 0.5) };
            var lift = new ScaleTransform(1, 1); card.RenderTransform = lift;
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            art.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(art);
            var n = K.TB(name, 13.5, Theme.Text, FontWeights.SemiBold); n.HorizontalAlignment = HorizontalAlignment.Center; n.Margin = new Thickness(0, 12, 0, 0);
            var s = K.TB(sub, 10, Theme.Text3, FontWeights.Medium); s.HorizontalAlignment = HorizontalAlignment.Center; s.Margin = new Thickness(0, 2, 0, 0);
            var best = K.TB("", 10, Theme.Accent, FontWeights.SemiBold); best.HorizontalAlignment = HorizontalAlignment.Center; best.Margin = new Thickness(0, 4, 0, 0);
            gBestLabels[id] = best;
            sp.Children.Add(n); sp.Children.Add(s); sp.Children.Add(best);
            card.Child = sp;
            card.MouseEnter += delegate { bg.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(Theme.A(Theme.Current.Text, 0.10), 140)); K.Pop(lift, 1.03, 260, K.OutBack); };
            card.MouseLeave += delegate { bg.BeginAnimation(SolidColorBrush.ColorProperty, CAnim(Theme.A(Theme.Current.Text, 0.05), 220)); K.Pop(lift, 1, 240, K.OutCubic); };
            card.MouseLeftButtonDown += (o, e) => { e.Handled = true; K.Pop(lift, 0.96, 90, K.OutQuint); };
            card.MouseLeftButtonUp += (o, e) => { e.Handled = true; K.Pop(lift, 1, 300, K.OutBack); OpenGame(id); };
            return card;
        }

        void UpdateGameBests()
        {
            Func<int, string> mmss = v => (v / 60) + ":" + (v % 60).ToString("00");
            int b = GameBest("blocks");
            gBestLabels["blocks"].Text = b > 0 ? "Best " + b.ToString("N0", K.EN) : "";
            int sd = new[] { "sudoku-easy", "sudoku-medium", "sudoku-hard" }.Select(GameBest).Where(v => v > 0).DefaultIfEmpty(0).Min();
            gBestLabels["sudoku"].Text = sd > 0 ? "Best " + mmss(sd) : "";
            int mi = GameBest("mines");
            gBestLabels["mines"].Text = mi > 0 ? "Best " + mmss(mi) : "";
            int mg = GameBest("merge");
            gBestLabels["merge"].Text = mg > 0 ? "Best " + mg.ToString("N0", K.EN) : "";
        }

        void OpenGame(string id)
        {
            Pin();
            StopGameTimers();   // a game left open behind another must not keep running
            game = id;
            gStat1.Text = ""; gStat2.Text = "";
            gMenu.Visibility = Visibility.Collapsed;
            gStage.Children.Clear();
            gStage.Visibility = Visibility.Visible;
            HideGameOverlay();
            gBack.Visibility = Visibility.Visible; gRestart.Visibility = Visibility.Visible;
            gStat1Chip.Visibility = Visibility.Visible; gStat2Chip.Visibility = Visibility.Visible;
            if (id == "blocks") { gTitle.Text = "Blocks"; BuildBlocks(); BlocksStart(); }
            else if (id == "sudoku") { gTitle.Text = "Sudoku"; BuildSudoku(); SudokuNew(); }
            else if (id == "merge") { gTitle.Text = "Merge"; BuildMerge(); MergeNew(); }
            else { gTitle.Text = "Mines"; BuildMines(); MinesNew(); }
            RefreshExpandedSize();
            K.FadeIn(gStage, 8, 320);
        }

        void CloseGame()
        {
            StopGameTimers();
            game = null;
            gStage.Children.Clear(); gStage.Visibility = Visibility.Collapsed;
            HideGameOverlay();
            gMenu.Visibility = Visibility.Visible;
            gBack.Visibility = Visibility.Collapsed; gRestart.Visibility = Visibility.Collapsed;
            gStat1Chip.Visibility = Visibility.Collapsed; gStat2Chip.Visibility = Visibility.Collapsed;
            gTitle.Text = "Games";
            UpdateGameBests();
            RefreshExpandedSize();
            K.FadeIn(gMenu, 8, 320);
        }

        void RestartGame()
        {
            HideGameOverlay();
            if (game == "blocks") BlocksStart();
            else if (game == "sudoku") SudokuNew();
            else if (game == "mines") MinesNew();
            else if (game == "merge") MergeNew();
        }

        void StopGameTimers()
        {
            if (bTimer != null) bTimer.Stop();
            if (sdClock != null) sdClock.Stop();
            if (mClock != null) mClock.Stop();
        }

        void ShowGameOverlay(string title, string sub, string btn, Action act)
        {
            gOverTitle.Text = title; gOverSub.Text = sub; gOverBtnText.Text = btn; gOverAct = act;
            gOverlay.Visibility = Visibility.Visible;
            gOverlay.Opacity = 0; gOverlay.BeginAnimation(OpacityProperty, K.Anim(1, 220, K.OutCubic));
            K.PopIn((UIElement)gOverlay.Children[0], 0.9, 360);
        }
        void HideGameOverlay() { if (gOverlay != null) { gOverlay.BeginAnimation(OpacityProperty, null); gOverlay.Visibility = Visibility.Collapsed; } }

        // Page lifecycle: leaving the page (or closing the island) pauses Blocks and the clocks.
        void GamesHidden()
        {
            if (game == "blocks" && !bOver && !bPaused) BlocksPause(true);
            if (game == "sudoku" && sdClock != null) sdClock.Stop();
            if (game == "mines" && mClock != null) mClock.Stop();
        }
        void GamesShown()
        {
            if (gMenu != null && game == null) UpdateGameBests();
            if (game == "sudoku" && !sdDone && sdClock != null) sdClock.Start();
            if (game == "mines" && mStarted && !mOver && mClock != null) mClock.Start();
        }

        bool GamesEscape()
        {
            if (game == null) return false;
            if (game == "blocks" && !bOver && !bPaused) { BlocksPause(true); return true; }
            CloseGame();
            return true;
        }

        bool GamesKey(KeyEventArgs e)
        {
            if (game == "blocks") return BlocksKey(e.Key);
            if (game == "sudoku") return SudokuKey(e.Key);
            if (game == "merge") return MergeKey(e.Key);
            return false;
        }

        // ---- menu art (tiny drawings, no images)
        FrameworkElement BlocksArt()
        {
            var c = new Canvas { Width = 52, Height = 52 };
            int[,] cells = { { 0, 0, 1 }, { 1, 0, 1 }, { 2, 0, 1 }, { 1, 1, 1 }, { 3, 2, 4 }, { 3, 3, 4 }, { 2, 3, 4 }, { 1, 3, 4 }, { 0, 3, 6 }, { 0, 2, 6 } };
            for (int i = 0; i < cells.GetLength(0); i++)
            {
                var r = new Rectangle { Width = 12, Height = 12, RadiusX = 2.5, RadiusY = 2.5, Fill = K.Solid(BColors[cells[i, 2]]) };
                Canvas.SetLeft(r, cells[i, 0] * 13); Canvas.SetTop(r, cells[i, 1] * 13);
                c.Children.Add(r);
            }
            return c;
        }
        FrameworkElement SudokuArt()
        {
            var g = new UniformGrid { Rows = 3, Columns = 3, Width = 48, Height = 48 };
            string[] d = { "5", "", "3", "", "7", "", "9", "", "1" };
            foreach (var s in d)
            {
                var b = new Border { Margin = new Thickness(1), CornerRadius = new CornerRadius(3), Background = K.Solid(Theme.A(Theme.Current.Text, 0.08)) };
                b.Child = new TextBlock { Text = s, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = s == "7" ? Theme.Accent : Theme.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                g.Children.Add(b);
            }
            return g;
        }
        FrameworkElement MinesArt()
        {
            var g = new UniformGrid { Rows = 3, Columns = 3, Width = 48, Height = 48 };
            for (int i = 0; i < 9; i++)
            {
                bool open = i == 0 || i == 1 || i == 3 || i == 4;
                var b = new Border { Margin = new Thickness(1), CornerRadius = new CornerRadius(3), Background = open ? K.Solid(Theme.A(Theme.Current.Text, 0.04)) : (Brush)Theme.Raised };
                if (i == 4) b.Child = new TextBlock { Text = "2", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Theme.Green, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                if (i == 1) b.Child = new TextBlock { Text = "1", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Theme.Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                if (i == 8) b.Child = K.Glyph("", 9, Theme.Orange);
                g.Children.Add(b);
            }
            return g;
        }

        Border SmallBtn(string glyph, string tip, Action act)
        {
            var b = K.IconBtn(glyph, 28, 11, Theme.Text, () => { Pin(); act(); });
            b.Background = Theme.Raised; b.ToolTip = tip; b.Margin = new Thickness(0, 0, 4, 0);
            return b;
        }

        // ================================================================ Blocks
        const int BW = 10, BH = 20; const double BCell = 12;
        static readonly Color[] BColors = { Colors.Transparent, Color.FromRgb(0x7a, 0xa2, 0xf7), Color.FromRgb(0xe0, 0xaf, 0x68), Color.FromRgb(0xbb, 0x9a, 0xf7), Color.FromRgb(0x9e, 0xce, 0x6a), Color.FromRgb(0xf7, 0x76, 0x8e), Color.FromRgb(0x7d, 0xcf, 0xff), Color.FromRgb(0xff, 0x9e, 0x64) };
        // pieces as (row, col) inside an N×N box; rotation = (r, c) -> (c, N-1-r)
        static readonly int[][] BShapes = {
            null,
            new[] { 4, 1,0, 1,1, 1,2, 1,3 },   // I
            new[] { 2, 0,0, 0,1, 1,0, 1,1 },   // O
            new[] { 3, 0,1, 1,0, 1,1, 1,2 },   // T
            new[] { 3, 0,1, 0,2, 1,0, 1,1 },   // S
            new[] { 3, 0,0, 0,1, 1,1, 1,2 },   // Z
            new[] { 3, 0,0, 1,0, 1,1, 1,2 },   // J
            new[] { 3, 0,2, 1,0, 1,1, 1,2 } }; // L

        int[,] bBoard; Border[,] bCells; Border[,] bNextCells; Border bBoardHost;
        int bType, bRot, bX, bY, bNext, bScore, bLines, bLevel; bool bOver, bPaused;
        bool bSwapped;      // the falling block was already traded with NEXT (once per block)
        Grid bNextGrid;
        readonly List<int> bBag = new List<int>();
        DispatcherTimer bTimer;
        TextBlock bScoreText, bLinesText;

        static List<Point> BCellsOf(int type, int rot)
        {
            var s = BShapes[type]; int n = s[0];
            var pts = new List<Point>();
            for (int i = 1; i < s.Length; i += 2)
            {
                int r = s[i], c = s[i + 1];
                for (int k = 0; k < rot; k++) { int nr = c, nc = n - 1 - r; r = nr; c = nc; }
                pts.Add(new Point(c, r));
            }
            return pts;
        }

        void BuildBlocks()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            var board = new Grid { Width = BW * BCell, Height = BH * BCell };
            bCells = new Border[BH, BW];
            for (int r = 0; r < BH; r++) board.RowDefinitions.Add(new RowDefinition());
            for (int c = 0; c < BW; c++) board.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < BH; r++)
                for (int c = 0; c < BW; c++)
                {
                    var b = new Border { Margin = new Thickness(0.6), CornerRadius = new CornerRadius(2.5) };
                    Grid.SetRow(b, r); Grid.SetColumn(b, c);
                    board.Children.Add(b); bCells[r, c] = b;
                }
            bBoardHost = new Border { Child = board, Padding = new Thickness(3), CornerRadius = new CornerRadius(9), Background = K.Solid(Theme.A(Theme.Current.Text, 0.03)), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, RenderTransformOrigin = new Point(0.5, 0.5) };
            root.Children.Add(bBoardHost);

            var side = new StackPanel { Margin = new Thickness(16, 2, 0, 0) };
            side.Children.Add(K.TB("NEXT", 9.5, Theme.Text3, FontWeights.Bold));
            var next = new Grid { Width = 4 * 11, Height = 4 * 11, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            bNextCells = new Border[4, 4];
            for (int i = 0; i < 4; i++) { next.RowDefinitions.Add(new RowDefinition()); next.ColumnDefinitions.Add(new ColumnDefinition()); }
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                {
                    var b = new Border { Margin = new Thickness(0.6), CornerRadius = new CornerRadius(2) };
                    Grid.SetRow(b, r); Grid.SetColumn(b, c); next.Children.Add(b); bNextCells[r, c] = b;
                }
            side.Children.Add(next);
            bNextGrid = next;
            next.Background = Brushes.Transparent; next.Cursor = Cursors.Hand; next.ToolTip = "Swap with the falling block (C)";
            next.MouseLeftButtonDown += (s, e) => e.Handled = true;
            next.MouseLeftButtonUp += (s, e) => { e.Handled = true; BlocksKey(Key.C); };
            var sl = K.TB("SCORE", 9.5, Theme.Text3, FontWeights.Bold); sl.Margin = new Thickness(0, 12, 0, 0);
            side.Children.Add(sl);
            bScoreText = K.TB("0", 22, Theme.Text, FontWeights.Light); K.Tabular(bScoreText);
            side.Children.Add(bScoreText);
            bLinesText = K.TB("", 10.5, Theme.Text2, FontWeights.Medium);
            side.Children.Add(bLinesText);
            var ctl = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            ctl.Children.Add(SmallBtn("", "Left (←)", () => BlocksKey(Key.Left)));
            ctl.Children.Add(SmallBtn("", "Rotate (↑)", () => BlocksKey(Key.Up)));
            ctl.Children.Add(SmallBtn("", "Right (→)", () => BlocksKey(Key.Right)));
            ctl.Children.Add(SmallBtn("", "Drop (Space)", () => BlocksKey(Key.Space)));
            ctl.Children.Add(SmallBtn("\uE8AB", "Swap with next (C)", () => BlocksKey(Key.C)));
            ctl.Children.Add(SmallBtn("", "Pause (P)", () => BlocksKey(Key.P)));
            side.Children.Add(ctl);
            var hint = K.TB("← → move · ↑ rotate\n↓ soft drop · Space hard drop\nC swap with next · P pause", 9.5, Theme.Text3, FontWeights.Normal);
            hint.TextTrimming = TextTrimming.None; hint.Margin = new Thickness(0, 10, 0, 0); hint.LineHeight = 14;
            side.Children.Add(hint);
            Grid.SetColumn(side, 1);
            root.Children.Add(side);
            gStage.Children.Add(root);
            bTimer = Timer(800, delegate { BlocksTick(); });
        }

        int BTake()
        {
            if (bBag.Count == 0) { bBag.AddRange(Enumerable.Range(1, 7).OrderBy(x => gRnd.Next())); }
            int t = bBag[0]; bBag.RemoveAt(0); return t;
        }

        void BlocksStart()
        {
            bBoard = new int[BH, BW];
            bBag.Clear(); bScore = 0; bLines = 0; bLevel = 1; bOver = false; bPaused = false;
            bNext = BTake();
            BSpawn();
            bTimer.Interval = TimeSpan.FromMilliseconds(BSpeed());
            bTimer.Start();
            BRender();
        }

        double BSpeed() { return Math.Max(90, 800 - (bLevel - 1) * 70); }

        bool BFits(int type, int rot, int x, int y)
        {
            foreach (var p in BCellsOf(type, rot))
            {
                int c = x + (int)p.X, r = y + (int)p.Y;
                if (c < 0 || c >= BW || r >= BH) return false;
                if (r >= 0 && bBoard[r, c] != 0) return false;
            }
            return true;
        }

        void BSpawn()
        {
            bType = bNext; bNext = BTake(); bSwapped = false;
            bRot = 0; bX = BSpawnX(bType); bY = BSpawnY(bType);
            if (!BFits(bType, bRot, bX, bY)) BGameOver();
        }

        static int BSpawnX(int type) { return type == 2 ? 4 : 3; }
        static int BSpawnY(int type) { return type == 1 ? -1 : 0; }

        // Trade the falling block for NEXT; the incoming one starts again from the top. Once per block.
        void BSwap()
        {
            if (bSwapped) return;
            int t = bNext;
            if (!BFits(t, 0, BSpawnX(t), BSpawnY(t))) return;
            bNext = bType; bType = t;
            bRot = 0; bX = BSpawnX(t); bY = BSpawnY(t);
            bSwapped = true;
        }

        void BlocksTick()
        {
            if (bOver || bPaused) return;
            if (BFits(bType, bRot, bX, bY + 1)) bY++;
            else BLock();
            BRender();
        }

        void BLock()
        {
            foreach (var p in BCellsOf(bType, bRot))
            {
                int c = bX + (int)p.X, r = bY + (int)p.Y;
                if (r < 0) { BGameOver(); return; }
                bBoard[r, c] = bType;
            }
            int cleared = 0;
            for (int r = BH - 1; r >= 0; r--)
            {
                bool full = true;
                for (int c = 0; c < BW; c++) if (bBoard[r, c] == 0) { full = false; break; }
                if (!full) continue;
                cleared++;
                for (int rr = r; rr > 0; rr--) for (int c = 0; c < BW; c++) bBoard[rr, c] = bBoard[rr - 1, c];
                for (int c = 0; c < BW; c++) bBoard[0, c] = 0;
                r++;   // re-check the row that dropped into place
            }
            if (cleared > 0)
            {
                bScore += new[] { 0, 100, 300, 500, 800 }[cleared] * bLevel;
                bLines += cleared;
                int lvl = bLines / 10 + 1;
                if (lvl != bLevel) { bLevel = lvl; bTimer.Interval = TimeSpan.FromMilliseconds(BSpeed()); }
                // a little bump of the board for every clear (bigger for more lines)
                var st = new ScaleTransform(1, 1); bBoardHost.RenderTransform = st;
                st.ScaleX = st.ScaleY = 1 + 0.012 * cleared; K.Pop(st, 1, 380, K.OutBack);
            }
            BSpawn();
        }

        void BGameOver()
        {
            bOver = true; bTimer.Stop();
            int best = GameBest("blocks");
            bool rec = bScore > best;
            if (rec) SetGameBest("blocks", bScore);
            BRender();
            ShowGameOverlay(rec && bScore > 0 ? "New best!" : "Game over", "Score " + bScore.ToString("N0", K.EN) + " · " + bLines + " lines" + (rec || best == 0 ? "" : " · Best " + best.ToString("N0", K.EN)), "Play again", BlocksStart);
        }

        void BlocksPause(bool on)
        {
            if (bOver) return;
            bPaused = on;
            if (on) { bTimer.Stop(); ShowGameOverlay("Paused", "Score " + bScore.ToString("N0", K.EN) + " · level " + bLevel, "Resume", () => BlocksPause(false)); }
            else { HideGameOverlay(); bTimer.Start(); }
        }

        bool BlocksKey(Key k)
        {
            if (bBoard == null) return false;
            if (bOver) { if (k == Key.Enter || k == Key.Space) { HideGameOverlay(); BlocksStart(); return true; } return k == Key.Left || k == Key.Right || k == Key.Up || k == Key.Down; }
            if (k == Key.P) { BlocksPause(!bPaused); return true; }
            if (bPaused) { if (k == Key.Enter || k == Key.Space) { BlocksPause(false); return true; } return k == Key.Left || k == Key.Right || k == Key.Up || k == Key.Down; }
            switch (k)
            {
                case Key.Left: if (BFits(bType, bRot, bX - 1, bY)) bX--; break;
                case Key.Right: if (BFits(bType, bRot, bX + 1, bY)) bX++; break;
                case Key.Down: if (BFits(bType, bRot, bX, bY + 1)) { bY++; bScore += 1; } else BLock(); break;
                case Key.Up: case Key.X:
                    {
                        int nr = (bRot + 1) % 4;
                        foreach (int dx in new[] { 0, -1, 1, -2, 2 })
                            if (BFits(bType, nr, bX + dx, bY)) { bRot = nr; bX += dx; break; }
                        break;
                    }
                case Key.Space:
                    while (BFits(bType, bRot, bX, bY + 1)) { bY++; bScore += 2; }
                    BLock(); break;
                case Key.C: case Key.LeftShift: case Key.RightShift: BSwap(); break;
                default: return false;
            }
            BRender();
            return true;
        }

        void BRender()
        {
            if (bCells == null) return;
            var empty = Theme.A(Theme.Current.Text, 0.045);
            var shown = new int[BH, BW];
            for (int r = 0; r < BH; r++) for (int c = 0; c < BW; c++) shown[r, c] = bBoard[r, c];
            var ghost = new bool[BH, BW];
            if (!bOver)
            {
                int gy = bY; while (BFits(bType, bRot, bX, gy + 1)) gy++;
                foreach (var p in BCellsOf(bType, bRot))
                {
                    int c = bX + (int)p.X, r = gy + (int)p.Y;
                    if (r >= 0) ghost[r, c] = true;
                }
                foreach (var p in BCellsOf(bType, bRot))
                {
                    int c = bX + (int)p.X, r = bY + (int)p.Y;
                    if (r >= 0) shown[r, c] = bType;
                }
            }
            for (int r = 0; r < BH; r++)
                for (int c = 0; c < BW; c++)
                {
                    int v = shown[r, c];
                    Color col = v != 0 ? BColors[v] : ghost[r, c] ? Theme.A(BColors[bType], 0.28) : empty;
                    var cur = bCells[r, c].Background as SolidColorBrush;
                    if (cur == null || cur.Color != col) bCells[r, c].Background = K.Solid(col);
                }
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) bNextCells[r, c].Background = Brushes.Transparent;
            foreach (var p in BCellsOf(bNext, 0)) bNextCells[(int)p.Y, (int)p.X].Background = K.Solid(BColors[bNext]);
            if (bNextGrid != null) bNextGrid.Opacity = bSwapped && !bOver ? 0.4 : 1;   // dimmed: swap used for this block
            bScoreText.Text = bScore.ToString("N0", K.EN);
            bLinesText.Text = bLines + " lines · level " + bLevel;
            gStat1.Text = "Level " + bLevel;
            int best = Math.Max(GameBest("blocks"), bScore);
            gStat2.Text = "Best " + best.ToString("N0", K.EN);
        }

        // ================================================================ Sudoku
        int[] sdSol, sdVal; bool[] sdGiven; Border[] sdCells; TextBlock[] sdTexts; int sdSel = -1; string sdDiff = "easy";
        DateTime sdStart; TimeSpan sdElapsed; DispatcherTimer sdClock; bool sdDone, sdBusy;
        readonly Dictionary<string, Border> sdDiffBtns = new Dictionary<string, Border>();
        readonly Border[] sdPad = new Border[10];

        void BuildSudoku()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            const double cs = 25;
            var board = new Grid { VerticalAlignment = VerticalAlignment.Top };
            for (int i = 0; i < 9; i++) { board.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cs + (i == 2 || i == 5 ? 3 : 0)) }); board.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cs + (i == 2 || i == 5 ? 3 : 0)) }); }
            sdCells = new Border[81]; sdTexts = new TextBlock[81];
            for (int i = 0; i < 81; i++)
            {
                int r = i / 9, c = i % 9, idx = i;
                var t = new TextBlock { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                K.Tabular(t);
                var b = new Border { Width = cs - 1, Height = cs - 1, CornerRadius = new CornerRadius(4), Child = t, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                b.MouseLeftButtonDown += (o, e) => { e.Handled = true; Pin(); sdSel = idx; SdRender(); };
                Grid.SetRow(b, r); Grid.SetColumn(b, c);
                board.Children.Add(b); sdCells[i] = b; sdTexts[i] = t;
            }
            root.Children.Add(board);

            var side = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            var diff = new Border { Height = 26, CornerRadius = new CornerRadius(13), Padding = new Thickness(2), Background = K.Solid(Theme.A(Theme.Current.Text, 0.06)), HorizontalAlignment = HorizontalAlignment.Left };
            var dr = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var d in new[] { "easy", "medium", "hard" })
            {
                string dd = d;
                var b = new Border { Height = 22, CornerRadius = new CornerRadius(11), Padding = new Thickness(8, 0, 8, 0), Cursor = Cursors.Hand, Child = K.TB(char.ToUpper(d[0]) + d.Substring(1, d == "medium" ? 2 : d.Length - 1), 10.5, Theme.Text2, FontWeights.SemiBold) };
                K.Pressable(b, () => { Pin(); if (sdDiff == dd && !sdDone) return; sdDiff = dd; SudokuNew(); }, false);
                sdDiffBtns[d] = b; dr.Children.Add(b);
            }
            diff.Child = dr;
            side.Children.Add(diff);
            var pad = new UniformGrid { Rows = 4, Columns = 3, Width = 3 * 36, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
            for (int n = 1; n <= 9; n++)
            {
                int nn = n;
                var b = new Border { Height = 32, Margin = new Thickness(2), CornerRadius = new CornerRadius(8), Background = Theme.Raised, Cursor = Cursors.Hand, Child = K.TB(n.ToString(), 14, Theme.Text, FontWeights.SemiBold) };
                ((TextBlock)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
                K.Pressable(b, () => { Pin(); SdPut(nn); }, true);
                sdPad[n] = b; pad.Children.Add(b);
            }
            var erase = new Border { Height = 32, Margin = new Thickness(2), CornerRadius = new CornerRadius(8), Background = Theme.Raised, Cursor = Cursors.Hand, Child = K.Glyph("", 13, Theme.Text2), ToolTip = "Erase (Backspace)" };
            K.Pressable(erase, () => { Pin(); SdPut(0); }, true);
            pad.Children.Add(new Border()); pad.Children.Add(erase);
            side.Children.Add(pad);
            var hint = K.TB("Click a cell, then a number\n1–9 · arrows · Backspace", 9.5, Theme.Text3, FontWeights.Normal);
            hint.TextTrimming = TextTrimming.None; hint.LineHeight = 14; hint.Margin = new Thickness(0, 10, 0, 0);
            side.Children.Add(hint);
            Grid.SetColumn(side, 1);
            root.Children.Add(side);
            gStage.Children.Add(root);
            sdClock = Timer(1000, delegate { SdClockTick(); });
        }

        void SdClockTick()
        {
            var t = sdElapsed + (DateTime.Now - sdStart);
            gStat1.Text = (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
        }

        void SudokuNew()
        {
            if (sdBusy) return;
            sdBusy = true; sdDone = false; sdSel = -1;
            sdClock.Stop();
            gStat1.Text = "…"; gStat2.Text = char.ToUpper(sdDiff[0]) + sdDiff.Substring(1);
            foreach (var t in sdTexts) t.Text = "";
            foreach (var kv in sdDiffBtns)
            {
                bool on = kv.Key == sdDiff;
                kv.Value.Background = on ? (Brush)Theme.Hover : Brushes.Transparent;
                ((TextBlock)kv.Value.Child).Foreground = on ? Theme.Text : Theme.Text3;
            }
            string diff = sdDiff;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var rnd = new Random(Guid.NewGuid().GetHashCode());
                var full = new int[81]; SdFill(full, 0, rnd);
                var puz = (int[])full.Clone();
                int target = diff == "easy" ? 38 : diff == "medium" ? 46 : 53, removed = 0;
                foreach (int pos in Enumerable.Range(0, 81).OrderBy(x => rnd.Next()))
                {
                    if (removed >= target) break;
                    int v = puz[pos]; puz[pos] = 0;
                    if (SdCount((int[])puz.Clone(), 2) != 1) puz[pos] = v; else removed++;
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    sdBusy = false;
                    if (game != "sudoku") return;
                    sdSol = full; sdVal = puz; sdGiven = puz.Select(v => v != 0).ToArray();
                    sdStart = DateTime.Now; sdElapsed = TimeSpan.Zero; sdClock.Start(); SdClockTick();
                    SdRender();
                    K.FadeIn(sdCells[0].Parent as UIElement, 6, 300);
                }));
            });
        }

        static bool SdFill(int[] g, int i, Random r)
        {
            if (i == 81) return true;
            foreach (int d in Enumerable.Range(1, 9).OrderBy(x => r.Next()))
                if ((SdMask(g, i) & (1 << d)) != 0) { g[i] = d; if (SdFill(g, i + 1, r)) return true; g[i] = 0; }
            return false;
        }

        // candidates for cell i as a bitmask (bits 1..9)
        static int SdMask(int[] g, int i)
        {
            int r = i / 9, c = i % 9, used = 0;
            for (int k = 0; k < 9; k++) { used |= 1 << g[r * 9 + k]; used |= 1 << g[k * 9 + c]; }
            int br = r / 3 * 3, bc = c / 3 * 3;
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) used |= 1 << g[(br + y) * 9 + bc + x];
            return ~used & 0x3FE;
        }

        // number of solutions, stopping at `limit` (most-constrained cell first)
        static int SdCount(int[] g, int limit)
        {
            int best = -1, bestN = 10, bestMask = 0;
            for (int i = 0; i < 81; i++)
            {
                if (g[i] != 0) continue;
                int m = SdMask(g, i), n = 0;
                for (int d = 1; d <= 9; d++) if ((m & (1 << d)) != 0) n++;
                if (n < bestN) { best = i; bestN = n; bestMask = m; if (n == 0) return 0; }
            }
            if (best < 0) return 1;
            int cnt = 0;
            for (int d = 1; d <= 9 && cnt < limit; d++)
                if ((bestMask & (1 << d)) != 0) { g[best] = d; cnt += SdCount(g, limit - cnt); g[best] = 0; }
            return cnt;
        }

        bool SdConflict(int i)
        {
            int v = sdVal[i]; if (v == 0) return false;
            int r = i / 9, c = i % 9, br = r / 3 * 3, bc = c / 3 * 3;
            for (int k = 0; k < 9; k++)
            {
                if (k != c && sdVal[r * 9 + k] == v) return true;
                if (k != r && sdVal[k * 9 + c] == v) return true;
            }
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) { int j = (br + y) * 9 + bc + x; if (j != i && sdVal[j] == v) return true; }
            return false;
        }

        void SdPut(int n)
        {
            if (sdVal == null || sdDone || sdSel < 0 || sdGiven[sdSel]) return;
            sdVal[sdSel] = n;
            SdRender();
            if (n != 0) K.PopIn(sdTexts[sdSel], 0.6, 300);
            if (sdVal.SequenceEqual(sdSol))
            {
                sdDone = true; sdClock.Stop();
                var t = sdElapsed + (DateTime.Now - sdStart);
                int secs = (int)t.TotalSeconds, best = GameBest("sudoku-" + sdDiff);
                bool rec = best == 0 || secs < best;
                if (rec) SetGameBest("sudoku-" + sdDiff, secs);
                Func<int, string> mmss = v => (v / 60) + ":" + (v % 60).ToString("00");
                ShowGameOverlay(rec ? "Solved · new best!" : "Solved!", char.ToUpper(sdDiff[0]) + sdDiff.Substring(1) + " in " + mmss(secs) + (rec ? "" : " · Best " + mmss(best)), "New puzzle", SudokuNew);
            }
        }

        bool SudokuKey(Key k)
        {
            if (sdVal == null || sdDone) return false;
            int n = -1;
            if (k >= Key.D1 && k <= Key.D9) n = k - Key.D0;
            else if (k >= Key.NumPad1 && k <= Key.NumPad9) n = k - Key.NumPad0;
            else if (k == Key.Back || k == Key.Delete || k == Key.D0 || k == Key.NumPad0) n = 0;
            if (n >= 0) { SdPut(n); return true; }
            int sel = sdSel < 0 ? 40 : sdSel;
            switch (k)
            {
                case Key.Left: sel = sel % 9 == 0 ? sel + 8 : sel - 1; break;
                case Key.Right: sel = sel % 9 == 8 ? sel - 8 : sel + 1; break;
                case Key.Up: sel = sel < 9 ? sel + 72 : sel - 9; break;
                case Key.Down: sel = sel >= 72 ? sel - 72 : sel + 9; break;
                default: return false;
            }
            sdSel = sel; SdRender();
            return true;
        }

        void SdRender()
        {
            if (sdVal == null) return;
            int sr = sdSel / 9, sc = sdSel % 9, sv = sdSel >= 0 ? sdVal[sdSel] : 0;
            var baseBg = Theme.A(Theme.Current.Text, 0.05);
            for (int i = 0; i < 81; i++)
            {
                int r = i / 9, c = i % 9;
                bool sel = i == sdSel;
                bool peer = sdSel >= 0 && (r == sr || c == sc || (r / 3 == sr / 3 && c / 3 == sc / 3));
                bool same = sv != 0 && sdVal[i] == sv;
                bool bad = SdConflict(i);
                Color bg = sel ? Theme.A(Theme.Current.Accent, 0.45) : same ? Theme.A(Theme.Current.Accent, 0.22) : peer ? Theme.A(Theme.Current.Text, 0.10) : baseBg;
                if (bad && !sdGiven[i]) bg = Theme.A(Theme.Current.Red, 0.22);
                sdCells[i].Background = K.Solid(bg);
                var t = sdTexts[i];
                t.Text = sdVal[i] == 0 ? "" : sdVal[i].ToString();
                t.FontWeight = sdGiven[i] ? FontWeights.SemiBold : FontWeights.Medium;
                t.Foreground = bad && !sdGiven[i] ? Theme.Red : sdGiven[i] ? Theme.Text : Theme.Accent;
            }
            // numbers that are all placed fade on the pad
            for (int n = 1; n <= 9; n++) sdPad[n].Opacity = sdVal.Count(v => v == n) >= 9 ? 0.35 : 1;
        }

        // ================================================================ Mines
        const int MW = 16, MH = 9, MCount = 22; const double MCell = 22;
        bool[] mMine, mOpen, mFlag; int[] mAdj; Border[] mCells; bool mStarted, mOver; int mFlags;
        DateTime mStart; DispatcherTimer mClock;
        static Color MNumColor(int n)
        {
            switch (n)
            {
                case 1: return Theme.Current.Accent;
                case 2: return Theme.Current.Green;
                case 3: return Theme.Current.Orange;
                case 4: return Color.FromRgb(0xbb, 0x9a, 0xf7);
                case 5: return Theme.Current.Red;
                case 6: return Color.FromRgb(0x7d, 0xcf, 0xff);
            }
            return Theme.Current.Text;
        }

        void BuildMines()
        {
            var board = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            for (int r = 0; r < MH; r++) board.RowDefinitions.Add(new RowDefinition { Height = new GridLength(MCell) });
            for (int c = 0; c < MW; c++) board.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MCell) });
            mCells = new Border[MW * MH];
            for (int i = 0; i < mCells.Length; i++)
            {
                int idx = i;
                var b = new Border { Margin = new Thickness(1), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand };
                b.MouseEnter += delegate { if (!mOpen[idx] && !mOver) b.Background = Theme.Hover; };
                b.MouseLeave += delegate { if (!mOpen[idx] && !mOver) b.Background = Theme.Raised; };
                b.MouseLeftButtonDown += (o, e) => e.Handled = true;
                b.MouseLeftButtonUp += (o, e) => { e.Handled = true; Pin(); MClick(idx); };
                b.MouseRightButtonUp += (o, e) => { e.Handled = true; Pin(); MFlag(idx); };
                Grid.SetRow(b, i / MW); Grid.SetColumn(b, i % MW);
                board.Children.Add(b); mCells[i] = b;
            }
            var sp = new StackPanel();
            sp.Children.Add(board);
            var hint = K.TB("Click to dig · right-click to flag · click a number to clear around it", 9.5, Theme.Text3, FontWeights.Normal);
            hint.HorizontalAlignment = HorizontalAlignment.Center; hint.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(hint);
            gStage.Children.Add(sp);
            mClock = Timer(1000, delegate { MClockTick(); });
        }

        void MinesNew()
        {
            int n = MW * MH;
            mMine = new bool[n]; mOpen = new bool[n]; mFlag = new bool[n]; mAdj = new int[n];
            mStarted = false; mOver = false; mFlags = 0;
            mClock.Stop();
            gStat1.Text = "0:00";
            for (int i = 0; i < n; i++) { mCells[i].Child = null; mCells[i].Background = Theme.Raised; }
            MStat();
        }

        IEnumerable<int> MNeighbors(int i)
        {
            int r = i / MW, c = i % MW;
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int rr = r + dr, cc = c + dc;
                    if (rr >= 0 && rr < MH && cc >= 0 && cc < MW) yield return rr * MW + cc;
                }
        }

        void MPlace(int safe)
        {
            var forbid = new HashSet<int>(MNeighbors(safe)) { safe };
            var spots = Enumerable.Range(0, MW * MH).Where(i => !forbid.Contains(i)).OrderBy(x => gRnd.Next()).Take(MCount);
            foreach (var i in spots) mMine[i] = true;
            for (int i = 0; i < mMine.Length; i++) mAdj[i] = MNeighbors(i).Count(j => mMine[j]);
            mStarted = true; mStart = DateTime.Now; mClock.Start();
        }

        void MClockTick() { var t = DateTime.Now - mStart; gStat1.Text = (int)t.TotalMinutes + ":" + t.Seconds.ToString("00"); }
        void MStat() { gStat2.Text = "⚑ " + (MCount - mFlags) + " left"; }

        void MClick(int i)
        {
            if (mOver || mFlag[i]) return;
            if (!mStarted) MPlace(i);
            if (mOpen[i])
            {
                // chord: a number with exactly that many flags around clears its other neighbours
                if (mAdj[i] > 0 && MNeighbors(i).Count(j => mFlag[j]) == mAdj[i])
                    foreach (var j in MNeighbors(i).ToList()) if (!mOpen[j] && !mFlag[j]) { if (MReveal(j)) return; }
                MCheckWin();
                return;
            }
            if (MReveal(i)) return;
            MCheckWin();
        }

        // returns true when a mine went off
        bool MReveal(int start)
        {
            if (mMine[start]) { MLose(start); return true; }
            var q = new Queue<int>(); q.Enqueue(start);
            int wave = 0;
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                if (mOpen[i] || mFlag[i]) continue;
                mOpen[i] = true;
                MDrawOpen(i, wave++);
                if (mAdj[i] == 0) foreach (var j in MNeighbors(i)) if (!mOpen[j]) q.Enqueue(j);
            }
            return false;
        }

        void MDrawOpen(int i, int wave)
        {
            var b = mCells[i];
            b.Background = K.Solid(Theme.A(Theme.Current.Text, 0.04));
            b.Cursor = mAdj[i] > 0 ? Cursors.Hand : Cursors.Arrow;
            if (mAdj[i] > 0) b.Child = new TextBlock { Text = mAdj[i].ToString(), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = K.Solid(MNumColor(mAdj[i])), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            else b.Child = null;
            // flood fills ripple outwards
            if (K.AnimScale > 0 && wave < 120)
            {
                b.Opacity = 0;
                var a = K.Anim(1, 180, K.OutCubic); a.BeginTime = TimeSpan.FromMilliseconds(K.Dur(Math.Min(wave, 60) * 6));
                b.BeginAnimation(OpacityProperty, a);
            }
        }

        void MFlag(int i)
        {
            if (mOver || mOpen[i]) return;
            mFlag[i] = !mFlag[i];
            mFlags += mFlag[i] ? 1 : -1;
            mCells[i].Child = mFlag[i] ? K.Glyph("", 11, Theme.Orange) : null;
            if (mFlag[i]) K.PopIn(mCells[i].Child, 0.4, 320);
            MStat();
        }

        void MCheckWin()
        {
            for (int i = 0; i < mMine.Length; i++) if (!mMine[i] && !mOpen[i]) return;
            mOver = true; mClock.Stop();
            int secs = (int)(DateTime.Now - mStart).TotalSeconds, best = GameBest("mines");
            bool rec = best == 0 || secs < best;
            if (rec) SetGameBest("mines", secs);
            for (int i = 0; i < mMine.Length; i++) if (mMine[i] && !mFlag[i]) mCells[i].Child = K.Glyph("", 11, Theme.Green);
            Func<int, string> mmss = v => (v / 60) + ":" + (v % 60).ToString("00");
            ShowGameOverlay(rec ? "Cleared · new best!" : "Cleared!", "All " + MCount + " mines found in " + mmss(secs) + (rec ? "" : " · Best " + mmss(best)), "Play again", MinesNew);
        }

        void MLose(int hit)
        {
            mOver = true; mClock.Stop();
            int k = 0;
            for (int i = 0; i < mMine.Length; i++)
            {
                if (mMine[i] && !mFlag[i])
                {
                    var b = mCells[i];
                    b.Background = i == hit ? K.Solid(Theme.A(Theme.Current.Red, 0.55)) : K.Solid(Theme.A(Theme.Current.Red, 0.14));
                    var dot = new Ellipse { Width = 8, Height = 8, Fill = i == hit ? Brushes.White : Theme.Red };
                    b.Child = dot;
                    var st = new ScaleTransform(0, 0); dot.RenderTransform = st; dot.RenderTransformOrigin = new Point(0.5, 0.5);
                    var a = K.Anim(1, 260, K.OutBack); a.BeginTime = TimeSpan.FromMilliseconds(K.Dur(k++ * 25));
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                }
                else if (mFlag[i] && !mMine[i]) mCells[i].Child = K.Glyph("", 10, Theme.Red);   // wrong flag
            }
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(K.Dur(700)) };
            t.Tick += delegate { t.Stop(); if (game == "mines" && mOver) ShowGameOverlay("Boom!", "You hit a mine · " + mOpen.Count(x => x) + " cells cleared", "Try again", MinesNew); };
            t.Start();
        }

        // ================================================================ Merge (sliding number puzzle)
        // 4×4 board; slide everything one way, two equal tiles that meet merge into their sum.
        // Tiles are real elements that glide between cells, so fast key presses chain smoothly.
        const int NN = 4; const double NCell = 50, NGap = 6;
        class NTile { public int V, R, C; public Border B; public TextBlock Tx; public TranslateTransform T; public ScaleTransform Sc; }
        NTile[,] nGrid; Canvas nCanvas; Border nBoard; Grid nFloat; Border nUndoBtn;
        int nScore, nTop, nUndoScore; int[,] nUndo; bool nOver, nCanUndo; Point nDown; bool nDownOn;

        static double NPos(int i) { return NGap + i * (NCell + NGap); }

        FrameworkElement MergeArt()
        {
            var g = new UniformGrid { Rows = 2, Columns = 2, Width = 48, Height = 48 };
            int[] v = { 2, 8, 0, 32 };
            foreach (var x in v)
            {
                var b = new Border { Margin = new Thickness(1.5), CornerRadius = new CornerRadius(5), Background = x == 0 ? K.Solid(Theme.A(Theme.Current.Text, 0.06)) : K.Solid(NTileColor(x)) };
                if (x > 0) b.Child = new TextBlock { Text = x.ToString(), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = K.Solid(NTextColor(x)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                g.Children.Add(b);
            }
            return g;
        }

        // Colours climb around the hue wheel from the theme accent and get richer as tiles grow.
        static Color NTileColor(int v)
        {
            int k = 0; while ((1 << (k + 1)) <= v) k++;   // 2 → 1, 4 → 2, …
            var p = Theme.Current;
            if (k <= 1) return Theme.A(p.Text, 0.10);
            if (k == 2) return Theme.A(p.Text, 0.17);
            double h, s, l; IslandLook.ToHsl(p.Accent, out h, out s, out l);
            double t = Math.Min(1, (k - 3) / 8.0);
            return Theme.Hsl(h + (k - 3) * 0.085, Math.Min(0.9, 0.55 + 0.35 * t), 0.56 - 0.1 * t);
        }
        static Color NTextColor(int v) { return v <= 4 ? Theme.Current.Text : Colors.White; }
        static double NFont(int v) { int d = v.ToString().Length; return d <= 2 ? 20 : d == 3 ? 17 : d == 4 ? 14 : 11.5; }

        void BuildMerge()
        {
            double size = NN * NCell + (NN + 1) * NGap;
            var root = new Grid { Width = size, Height = size };
            for (int r = 0; r < NN; r++)
                for (int c = 0; c < NN; c++)
                {
                    var slot = new Border { Width = NCell, Height = NCell, CornerRadius = new CornerRadius(10), Background = K.Solid(Theme.A(Theme.Current.Text, 0.05)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(NPos(c), NPos(r), 0, 0) };
                    root.Children.Add(slot);
                }
            nCanvas = new Canvas();
            root.Children.Add(nCanvas);
            nFloat = new Grid { IsHitTestVisible = false };
            root.Children.Add(nFloat);
            // Hand cursor marks it interactive, so a drag here slides tiles instead of switching pages
            nBoard = new Border { CornerRadius = new CornerRadius(14), Background = K.Solid(Theme.A(Theme.Current.Text, 0.03)), Child = root, HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.Hand, RenderTransformOrigin = new Point(0.5, 0.5) };
            nBoard.MouseLeftButtonDown += (o, e) => { e.Handled = true; Pin(); nDown = e.GetPosition(nBoard); nDownOn = true; nBoard.CaptureMouse(); };
            nBoard.MouseMove += (o, e) =>
            {
                if (!nDownOn) return;
                var p = e.GetPosition(nBoard); double dx = p.X - nDown.X, dy = p.Y - nDown.Y;
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < 26) return;
                nDownOn = false; nBoard.ReleaseMouseCapture();
                if (Math.Abs(dx) > Math.Abs(dy)) NMove(dx > 0 ? 1 : 3); else NMove(dy > 0 ? 2 : 0);
            };
            nBoard.MouseLeftButtonUp += (o, e) => { e.Handled = true; nDownOn = false; nBoard.ReleaseMouseCapture(); };

            var foot = new Grid { Margin = new Thickness(0, 7, 0, 0), Width = size };
            var hint = K.TB("Arrow keys, WASD or drag to slide", 9.5, Theme.Text3, FontWeights.Normal);
            hint.VerticalAlignment = VerticalAlignment.Center;
            nUndoBtn = K.GlyphPill("", "Undo", Theme.Raised, Theme.Text, 24, () => { Pin(); NUndo(); });
            nUndoBtn.HorizontalAlignment = HorizontalAlignment.Right; nUndoBtn.ToolTip = "Undo the last move (Z)";
            foot.Children.Add(hint); foot.Children.Add(nUndoBtn);

            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(nBoard); sp.Children.Add(foot);
            gStage.Children.Add(sp);
        }

        void MergeNew()
        {
            nCanvas.Children.Clear(); nFloat.Children.Clear();
            nGrid = new NTile[NN, NN];
            nScore = 0; nTop = 0; nOver = false; nCanUndo = false;
            NSpawn(0); NSpawn(60);
            NStats();
        }

        void NStats()
        {
            gStat1.Text = "Score " + nScore.ToString("N0", K.EN);
            int best = Math.Max(GameBest("merge"), nScore);
            gStat2.Text = "Best " + best.ToString("N0", K.EN);
            nUndoBtn.Opacity = nCanUndo && !nOver ? 1 : 0.4;
        }

        NTile NMake(int v, int r, int c)
        {
            var t = new NTile { V = v, R = r, C = c, T = new TranslateTransform(NPos(c), NPos(r)), Sc = new ScaleTransform(1, 1) };
            t.Tx = new TextBlock { FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            K.Tabular(t.Tx);
            t.B = new Border { Width = NCell, Height = NCell, CornerRadius = new CornerRadius(10), Child = t.Tx, RenderTransformOrigin = new Point(0.5, 0.5) };
            var tg = new TransformGroup(); tg.Children.Add(t.Sc); tg.Children.Add(t.T);
            t.B.RenderTransform = tg;
            NPaint(t);
            nCanvas.Children.Add(t.B);
            nGrid[r, c] = t;
            if (v > nTop) nTop = v;
            return t;
        }

        void NPaint(NTile t)
        {
            t.B.Background = K.Solid(NTileColor(t.V));
            t.Tx.Text = t.V.ToString(); t.Tx.FontSize = NFont(t.V); t.Tx.Foreground = K.Solid(NTextColor(t.V));
            // big tiles get a soft glow in their own colour
            t.B.Effect = t.V >= 128 ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = Math.Min(0.7, 0.25 + 0.06 * Math.Log(t.V, 2) - 0.3), Color = NTileColor(t.V), RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance } : null;
        }

        bool NSpawn(int delay)
        {
            var free = new List<int>();
            for (int r = 0; r < NN; r++) for (int c = 0; c < NN; c++) if (nGrid[r, c] == null) free.Add(r * NN + c);
            if (free.Count == 0) return false;
            int i = free[gRnd.Next(free.Count)];
            var t = NMake(gRnd.Next(10) == 0 ? 4 : 2, i / NN, i % NN);
            if (K.AnimScale > 0)
            {
                t.Sc.ScaleX = t.Sc.ScaleY = 0;
                var a = K.Anim(1, 260, K.OutBack); a.BeginTime = TimeSpan.FromMilliseconds(K.Dur(delay));
                t.Sc.BeginAnimation(ScaleTransform.ScaleXProperty, a); t.Sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            return true;
        }

        // dir: 0 up, 1 right, 2 down, 3 left
        void NMove(int dir)
        {
            if (nGrid == null || nOver) return;
            var snap = new int[NN, NN];
            for (int r = 0; r < NN; r++) for (int c = 0; c < NN; c++) snap[r, c] = nGrid[r, c] != null ? nGrid[r, c].V : 0;
            int snapScore = nScore;

            bool moved = false; int gained = 0;
            var next = new NTile[NN, NN];
            int slideMs = 110;
            for (int line = 0; line < NN; line++)
            {
                // walk each row/column from the side the tiles move towards
                var cells = new List<int[]>();
                for (int k = 0; k < NN; k++)
                {
                    int rr, cc;
                    switch (dir)
                    {
                        case 0: rr = k; cc = line; break;
                        case 2: rr = NN - 1 - k; cc = line; break;
                        case 3: rr = line; cc = k; break;
                        default: rr = line; cc = NN - 1 - k; break;
                    }
                    cells.Add(new[] { rr, cc });
                }
                int dest = 0; NTile last = null; bool lastMerged = false;
                foreach (var cell in cells)
                {
                    var t = nGrid[cell[0], cell[1]];
                    if (t == null) continue;
                    if (last != null && !lastMerged && last.V == t.V)
                    {
                        // t slides into last, then disappears; last doubles
                        var tgt = cells[dest - 1];
                        NSlide(t, tgt[0], tgt[1], slideMs, true);
                        last.V *= 2; gained += last.V; lastMerged = true; moved = true;
                        NMergePop(last, slideMs);
                        if (last.V > nTop) nTop = last.V;
                        continue;
                    }
                    var d = cells[dest++];
                    if (d[0] != t.R || d[1] != t.C) moved = true;
                    NSlide(t, d[0], d[1], slideMs, false);
                    next[d[0], d[1]] = t;
                    last = t; lastMerged = false;
                }
            }
            if (!moved) { NNudge(dir); return; }
            nGrid = next;
            nUndo = snap; nUndoScore = snapScore; nCanUndo = true;
            nScore += gained;
            if (gained > 0) NFloatScore(gained);
            NSpawn(slideMs + 20);
            NStats();
            if (nScore > GameBest("merge")) SetGameBest("merge", nScore);
            if (!NCanMove()) NGameOver();
        }

        void NSlide(NTile t, int r, int c, int ms, bool vanish)
        {
            t.R = r; t.C = c;
            if (K.AnimScale <= 0)
            {
                t.T.X = NPos(c); t.T.Y = NPos(r);
                if (vanish) nCanvas.Children.Remove(t.B);
                return;
            }
            var ax = K.Anim(NPos(c), ms, K.OutCubic); var ay = K.Anim(NPos(r), ms, K.OutCubic);
            if (vanish)
            {
                Panel.SetZIndex(t.B, -1);
                ay.Completed += delegate { nCanvas.Children.Remove(t.B); };
            }
            t.T.BeginAnimation(TranslateTransform.XProperty, ax); t.T.BeginAnimation(TranslateTransform.YProperty, ay);
        }

        void NMergePop(NTile t, int delay)
        {
            Panel.SetZIndex(t.B, 1);
            var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, K.Dur(delay))) };
            tm.Tick += delegate
            {
                tm.Stop();
                NPaint(t);
                if (K.AnimScale <= 0) return;
                var pop = new DoubleAnimationUsingKeyFrames();
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.16, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(K.Dur(90))), K.OutCubic));
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(K.Dur(260))), K.OutCubic));
                t.Sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop); t.Sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            };
            tm.Start();
        }

        // "+N" rises from the top of the board and fades
        void NFloatScore(int n)
        {
            if (K.AnimScale <= 0) return;
            var tb = K.TB("+" + n.ToString("N0", K.EN), 13, Theme.Accent, FontWeights.Bold);
            tb.HorizontalAlignment = HorizontalAlignment.Right; tb.VerticalAlignment = VerticalAlignment.Top; tb.Margin = new Thickness(0, 4, 10, 0);
            var tt = new TranslateTransform(); tb.RenderTransform = tt;
            nFloat.Children.Add(tb);
            tt.BeginAnimation(TranslateTransform.YProperty, K.Anim(-22, 700, K.OutCubic));
            var f = K.Anim(0, 700, new CubicEase { EasingMode = EasingMode.EaseIn });
            f.Completed += delegate { nFloat.Children.Remove(tb); };
            tb.BeginAnimation(OpacityProperty, f);
        }

        // nothing moved: the board leans a few px that way and springs back
        void NNudge(int dir)
        {
            if (K.AnimScale <= 0) return;
            var tt = nBoard.RenderTransform as TranslateTransform;
            if (tt == null) { tt = new TranslateTransform(); nBoard.RenderTransform = tt; }
            double dx = dir == 1 ? 4 : dir == 3 ? -4 : 0, dy = dir == 2 ? 4 : dir == 0 ? -4 : 0;
            var ax = new DoubleAnimationUsingKeyFrames(); ax.KeyFrames.Add(new EasingDoubleKeyFrame(dx, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70)))); ax.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), K.OutBack));
            var ay = new DoubleAnimationUsingKeyFrames(); ay.KeyFrames.Add(new EasingDoubleKeyFrame(dy, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70)))); ay.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), K.OutBack));
            tt.BeginAnimation(TranslateTransform.XProperty, ax); tt.BeginAnimation(TranslateTransform.YProperty, ay);
        }

        bool NCanMove()
        {
            for (int r = 0; r < NN; r++)
                for (int c = 0; c < NN; c++)
                {
                    var t = nGrid[r, c];
                    if (t == null) return true;
                    if (c + 1 < NN && nGrid[r, c + 1] != null && nGrid[r, c + 1].V == t.V) return true;
                    if (r + 1 < NN && nGrid[r + 1, c] != null && nGrid[r + 1, c].V == t.V) return true;
                }
            return false;
        }

        void NUndo()
        {
            if (!nCanUndo || nUndo == null) return;
            HideGameOverlay();
            nCanvas.Children.Clear(); nFloat.Children.Clear();
            nGrid = new NTile[NN, NN]; nTop = 0;
            for (int r = 0; r < NN; r++)
                for (int c = 0; c < NN; c++)
                    if (nUndo[r, c] > 0)
                    {
                        var t = NMake(nUndo[r, c], r, c);
                        if (K.AnimScale > 0) { t.B.Opacity = 0.4; t.B.BeginAnimation(OpacityProperty, K.Anim(1, 200, K.OutCubic)); }
                    }
            nScore = nUndoScore; nOver = false; nCanUndo = false;
            NStats();
        }

        void NGameOver()
        {
            nOver = true;
            NStats();
            int best = GameBest("merge");
            bool rec = nScore >= best && nScore > 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(K.Dur(520)) };
            t.Tick += delegate
            {
                t.Stop();
                if (game != "merge" || !nOver) return;
                ShowGameOverlay(rec ? "New best!" : "No moves left", "Score " + nScore.ToString("N0", K.EN) + " · top tile " + nTop + (rec ? "" : " · Best " + best.ToString("N0", K.EN)), "Play again", MergeNew);
            };
            t.Start();
        }

        bool MergeKey(Key k)
        {
            if (nGrid == null) return false;
            if (nOver && (k == Key.Enter || k == Key.Space)) { HideGameOverlay(); MergeNew(); return true; }
            switch (k)
            {
                case Key.Up: case Key.W: NMove(0); return true;
                case Key.Right: case Key.D: NMove(1); return true;
                case Key.Down: case Key.S: NMove(2); return true;
                case Key.Left: case Key.A: NMove(3); return true;
                case Key.Z: case Key.Back: NUndo(); return true;
            }
            return false;
        }
    }
}
