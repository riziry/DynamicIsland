// Settings model, persistence, and the Settings window.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynIsland
{
    public class AppSettings
    {
        // user
        public string UserName = "";
        public bool Welcome = true;
        public int WelcomeMode = 0;            // 0 every start, 1 once per login
        public string GreetingText = "";       // "{name}" is replaced
        public string LastWelcomeBoot = "";
        // motion
        public int AnimMode = 2;               // 0 off, 1 light, 2 normal, 3 relaxed, 4 custom
        public double AnimCustom = 1.0;
        public bool Springy = true;
        public bool ContentMotion = true;      // centre-out compact content, cascading rows when open
        public bool ExtraMotion = false;       // playing glow
        public bool Shadows = true;
        public int Renderer = 0;               // 0 GPU (DWM), 1 compatible (layered)
        public int Font = 3;                // 0 Inter (bundled), 1 Segoe UI, 2 custom, 3 JetBrainsMono Nerd Font (bundled)
        public string FontCustom = "";
        // size & position
        public int Size = 1;                   // compact, default, large
        public double Scale = 1.0;             // whole island
        public int ExpandedWidth = 420;
        public int OffsetX = 0, OffsetY = 0;
        // look
        public int Style = 0;                  // pill, notch
        public int Shape = 0;                  // capsule, rounded, square
        public int Tint = 0;                   // island, surface, accent, gradient
        public int Outline = 1;                // none, subtle, accent, gradient
        public double OutlineWidth = 1;        // px
        // visibility
        public int Visibility = 1;             // 0 always, 1 smart, 2 auto-hide
        public bool ShowWhenIdle = true;
        public bool FullscreenNotch = true;
        public bool FullscreenQuiet = true;    // over fullscreen, song changes only ping the mini tab
        // idle
        public bool IdleClock = true;
        public bool Clock24h = true;
        public bool ClockSeconds = false;
        public bool IdleDate = true;
        // interaction
        public bool HoverExpand = true;
        public bool PauseOnHover = true;
        public bool ScrollVolume = true;
        public bool InvertSwipe = false;
        public bool LoopPages = true;
        public int Waveform = 1;               // full, reduced, off
        public bool AmbientMedia = true;
        public int OsdMs = 1600, SystemMs = 3500, TrackMs = 3000;
        // events
        public bool EvVolume = true, EvBrightness = true, EvCharging = true, EvLowBattery = true, EvNetwork = true,
            EvBluetooth = true, EvDisplay = true, EvMic = true, EvTrack = true, EvClipboard = true;
        public int LowBattery = 20, CriticalBattery = 10;
        // notifications from other apps
        public bool EvApps = true;
        public bool DoNotDisturb = false;      // hold app notifications (summary when turned off)
        public bool NotifyShowText = true;     // off = "New notification" only (privacy)
        public int AppNotifyMs = 5000;
        public List<string> NotifyMuted = new List<string>();   // app names (or parts of them) never shown
        // features
        public string TimerPresets = "1, 5, 10, 15, 25, 45";
        public int TimerLastMinutes = 5;
        public bool TimerSound = true;
        public int TodoMax = 20;
        public int ShelfMax = 30;
        public bool ShelfKeepAfterDrag = false;
        public bool ShelfCopyOnDrag = false;        // legacy, replaced by ShelfDragMode
        public string ShelfDragMode = "copy";
        public Dictionary<string, int> GameBest = new Dictionary<string, int>();   // games: best score (blocks) / best time in s      // dragging a file out of the shelf: "copy" | "move"
        public List<string> ShelfItems = new List<string>();
        // theme
        public string Palette = "oled";
        public string CustomAccent = "#7aa2f7", CustomAccent2 = "#bb9af7";
        public double TintHue = 0.64, TintStrength = 0.25, Depth = 0.4;
        // ai
        public string AiProvider = "claude";
        public string AiMethod = "auto";       // auto, cli, key
        public Dictionary<string, string> AiModels = new Dictionary<string, string>();
        public Dictionary<string, string> AiKeys = new Dictionary<string, string>();   // DPAPI-encrypted
        public string Effort = "low";
        public string SystemPrompt = Store.DefaultPrompt;
        // launcher / wallpapers
        public string SearchEngine = "google";
        public Dictionary<string, int> LaunchCounts = new Dictionary<string, int>();
        public string WallpaperDir = "";
        public int WallpaperFit = 0;           // fill, fit, center, span
        public string OnlineProvider = "wallhaven";
        public int OnlineSort = 0;
        public string WallhavenKeyEnc = "";
        public int WallhavenPurity = 0;        // 0 SFW, 1 SFW + sketchy, 2 all (incl. NSFW), 3 NSFW only — NSFW needs the API key
        public bool WallhavenBlurNsfw = true;
        public string WallhavenPurityBits = "";  // sfw/sketchy/nsfw as "100"…"111"; empty = derive from WallhavenPurity   // NSFW thumbnails stay blurred until hovered
        public string WallhavenAtleast = "1920x1080";
        public string WallhavenCategories = "111";
        // hotkeys
        public Dictionary<string, string> Hotkeys = new Dictionary<string, string>();
        public bool LinuxWinKeys = false;      // Win+1..9 desktops, Win+Enter/F/B/Q, Win+Space or Win+/ launcher (keyboard hook)
        public string WinTerminal = "wt.exe";
        public string WinFiles = "explorer.exe";
        public string WinBrowser = "";         // empty = the default browser
        public bool WinMoveFollow = true;      // Win+Shift+N also switches to desktop N (Hyprland movetoworkspace)
        public string LastPage = "home";
        public bool SetupDone = false;
        public bool EasterEgg = false;          // the secret has been found (rainbow rim on the Settings logo)         // the first-run setup wizard has been completed or skipped
        // pages
        public List<string> PageOrder = new List<string>();     // empty = default order
        public List<string> HiddenPages = new List<string>();   // pages left out of the carousel
        public List<string> HomeTiles = new List<string>();     // Home shortcuts (empty = default set), up to 5
        public int OpenTo = 0;                 // what a plain open shows: 0 smart (timer/music first), 1 home, 2 last page
        public int HoverDelay = 260;           // ms the pointer rests on the island before it opens
        // updates
        public bool AutoCheckUpdates = true;   // look for a new GitHub release at start
        public string UpdateNotified = "";     // last version announced on the island (once per version)
        public string UpdatedFrom = "";        // set just before an update restarts the app; checked once at the next start
    }

    public class TodoItem
    {
        public string Text = "";
        public bool Done;
        public long Created;
    }

    static class Store
    {
        public const string DefaultPrompt = "You are an assistant inside a Windows Dynamic Island. Answer concisely. Use Markdown only when it helps (code blocks, short lists). Reply in the user's language.";
        const string OldPrompt = "Kamu adalah asisten di dalam Dynamic Island Windows.";

        public static AppSettings S = new AppSettings();
        public static List<TodoItem> Todos = new List<TodoItem>();
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");
        static readonly string FilePath = Path.Combine(Dir, "settings.json");
        static readonly string TodoPath = Path.Combine(Dir, "todos.json");
        static DispatcherTimer saveTimer, todoTimer;
        public static bool FirstRun;

        public static readonly Dictionary<string, string> DefaultHotkeys = new Dictionary<string, string>
        {
            { "toggle", "Ctrl+Alt+I" }, { "launcher", "Alt+Space" }, { "ai", "Ctrl+Alt+C" }, { "todo", "Ctrl+Alt+T" },
            { "timer", "Ctrl+Alt+Y" }, { "system", "Ctrl+Alt+M" }, { "shelf", "Ctrl+Alt+H" }, { "wallpaper", "Ctrl+Alt+W" },
            { "pauseTimer", "Ctrl+Alt+P" }, { "dismiss", "Ctrl+Alt+D" }, { "settings", "Ctrl+Alt+S" },
        };
        public static readonly Dictionary<string, string> HotkeyLabels = new Dictionary<string, string>
        {
            { "toggle", "Open / close the island" }, { "launcher", "Launcher (search apps)" }, { "ai", "AI chat" }, { "todo", "To-do" },
            { "timer", "Timer" }, { "system", "System page" }, { "shelf", "Shelf" }, { "wallpaper", "Wallpaper picker" },
            { "pauseTimer", "Pause / resume timer" }, { "dismiss", "Dismiss event" }, { "settings", "Settings" },
        };

        public static JavaScriptSerializer Js() { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }; }

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath)) S = Js().Deserialize<AppSettings>(File.ReadAllText(FilePath, Encoding.UTF8));
                else FirstRun = true;
            }
            catch { }
            if (S == null) S = new AppSettings();
            if (S.ShelfItems == null) S.ShelfItems = new List<string>();
            if (S.AiModels == null) S.AiModels = new Dictionary<string, string>();
            if (S.AiKeys == null) S.AiKeys = new Dictionary<string, string>();
            if (S.LaunchCounts == null) S.LaunchCounts = new Dictionary<string, int>();
            if (S.Hotkeys == null) S.Hotkeys = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(S.SystemPrompt) || S.SystemPrompt.StartsWith(OldPrompt)) S.SystemPrompt = DefaultPrompt;
            if (string.IsNullOrEmpty(S.WallhavenCategories) || S.WallhavenCategories.Length != 3) S.WallhavenCategories = "111";
            foreach (var kv in DefaultHotkeys) if (!S.Hotkeys.ContainsKey(kv.Key)) S.Hotkeys[kv.Key] = kv.Value;
            if (string.IsNullOrEmpty(S.WallpaperDir))
            {
                string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string w = Path.Combine(pics, "Wallpapers");
                S.WallpaperDir = Directory.Exists(w) ? w : pics;
            }
            S.ShelfItems.RemoveAll(p => !(File.Exists(p) || Directory.Exists(p)));
            try { if (File.Exists(TodoPath)) Todos = Js().Deserialize<List<TodoItem>>(File.ReadAllText(TodoPath, Encoding.UTF8)) ?? new List<TodoItem>(); } catch { }

            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveNow(); };
            todoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            todoTimer.Tick += delegate { todoTimer.Stop(); WriteFile(TodoPath, Js().Serialize(Todos)); };
        }

        public static void Save() { saveTimer.Stop(); saveTimer.Start(); }
        public static void SaveTodos() { todoTimer.Stop(); todoTimer.Start(); }
        public static void SaveNow() { WriteFile(FilePath, Js().Serialize(S)); }

        public static void WriteFile(string path, string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text, Encoding.UTF8);
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            }
            catch { }
        }

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain.Trim()), null, DataProtectionScope.CurrentUser));
        }
        public static string Unprotect(string enc)
        {
            if (string.IsNullOrEmpty(enc)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static string ExePath { get { return System.Reflection.Assembly.GetExecutingAssembly().Location; } }
        public static bool IsStartup()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("DynamicIsland") != null; }
            catch { return false; }
        }
        public static void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue("DynamicIsland", "\"" + ExePath + "\" --silent");
                    else k.DeleteValue("DynamicIsland", false);
                }
            }
            catch { }
        }

        public static Palette ResolvePalette()
        {
            if (S.Palette == "custom") return Theme.MakeCustom(S.CustomAccent, S.CustomAccent2, S.TintHue, S.TintStrength, S.Depth);
            if (S.Palette == "wallpaper") return Theme.MakeWallpaper(WallpaperService.CurrentSeed());
            var p = Theme.Palettes.FirstOrDefault(x => x.Key == S.Palette);
            return p ?? Theme.Palettes[0];
        }

        public static double AnimScale()
        {
            switch (S.AnimMode)
            {
                case 0: return 0;
                case 1: return 0.65;
                case 3: return 1.35;
                case 4: return Math.Max(0.1, S.AnimCustom);
            }
            return 1;
        }
    }

    // ======================================================================= window
    // Sidebar with a search box and grouped sections (Personal · Island · Pages · System). Searching
    // gathers the matching rows from every section onto one page, live controls included.
    class SettingsWindow : Window
    {
        readonly IslandWindow isl;
        AppSettings S { get { return Store.S; } }
        Border shell, navInd; Grid pageHost, navHost; ScrollViewer scroller; TextBox search; string query = "";
        readonly List<TextBlock> navLabels = new List<TextBlock>(), navGlyphs = new List<TextBlock>();
        readonly List<Border> navBtns = new List<Border>();
        readonly List<Func<FrameworkElement>> builders = new List<Func<FrameworkElement>>();
        readonly List<string> navTitles = new List<string>();
        TranslateTransform navIndT; int curPage = -1, prevPage; bool closing;
        string recordingKey;
        DispatcherTimer searchTimer;
        static readonly string[] PageKeys = { "general", "look", "size", "behavior", "motion", "events", "pages", "tools", "ai", "launcher", "shortcuts", "about" };
        // old section names still accepted by ShowPage (IPC / other callers)
        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string> { { "island", "size" }, { "appearance", "look" }, { "notifications", "events" } };

        public SettingsWindow(IslandWindow isl, int page)
        {
            this.isl = isl;
            Title = "Dynamic Island Settings";
            // A normal (non-layered) window: GPU-rendered, with the native Windows 11 shadow and rounded corners.
            // Frosted: the DWM frame is extended over the whole client area and given the acrylic system backdrop;
            // WPF draws on a transparent surface and the pages tint it with translucent Theme.Glass* brushes.
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Background = Brushes.Transparent;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(0), UseAeroCaptionButtons = false, CornerRadius = new CornerRadius(0) });
            // fit the work area (small / high-DPI screens), centred on it
            var wa = SystemParameters.WorkArea;
            Width = Math.Min(900, wa.Width - 40); Height = Math.Min(660, wa.Height - 40);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = wa.Left + (wa.Width - Width) / 2; Top = wa.Top + (wa.Height - Height) / 2;
            K.CrispText(this, true);
            FontFamily = K.Font;
            Build(page);
            SourceInitialized += delegate
            {
                var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int round = 2, dark = 1;
                Native.DwmSetWindowAttribute(h, 33, ref round, 4);   // rounded corners
                Native.DwmSetWindowAttribute(h, 20, ref dark, 4);    // dark frame
                var src = System.Windows.Interop.HwndSource.FromHwnd(h);
                if (src != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                int acrylic = 3;                                       // DWMSBT_TRANSIENTWINDOW (acrylic blur)
                if (Native.DwmSetWindowAttribute(h, 38, ref acrylic, 4) != 0) shell.Background = Theme.Surface;   // pre-22H2: stay solid
            };
            Loaded += delegate
            {
                var tt = new TranslateTransform(0, 10); shell.RenderTransform = tt;
                shell.Opacity = 0;
                shell.BeginAnimation(OpacityProperty, K.AnimRaw(1, 240, K.OutQuint));
                tt.BeginAnimation(TranslateTransform.YProperty, K.AnimRaw(0, 420, K.OutQuint));
                MoveNavIndicator(false);
            };
            PreviewKeyDown += OnKey;
        }

        public void ShowPage(string name)
        {
            string a; if (name != null && Aliases.TryGetValue(name, out a)) name = a;
            int i = Array.IndexOf(PageKeys, name);
            if (i >= 0) { ClearSearch(); ShowPage(i); }
        }

        public void Search(string q) { search.Text = q ?? ""; }

        bool IsPage(string key) { return curPage >= 0 && curPage < PageKeys.Length && PageKeys[curPage] == key; }

        void OnKey(object s, KeyEventArgs e)
        {
            if (recordingKey != null)
            {
                e.Handled = true;
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.Escape) { StopRecording(null); return; }
                if (key == Key.Back || key == Key.Delete) { StopRecording(""); return; }
                if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return;
                var parts = new List<string>();
                var m = Keyboard.Modifiers;
                if ((m & ModifierKeys.Control) != 0) parts.Add("Ctrl");
                if ((m & ModifierKeys.Alt) != 0) parts.Add("Alt");
                if ((m & ModifierKeys.Shift) != 0) parts.Add("Shift");
                if ((m & ModifierKeys.Windows) != 0) parts.Add("Win");
                if (parts.Count == 0) return;
                parts.Add(HotkeyService.KeyName(key));
                StopRecording(string.Join("+", parts));
                return;
            }
            // Ctrl+F jumps to search; Esc clears a search first, then closes
            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; search.Focus(); search.SelectAll(); return; }
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (query.Length > 0) { ClearSearch(); if (curPage < 0) ShowPage(prevPage); return; }
                AnimatedClose();
            }
        }

        void StopRecording(string value)
        {
            string k = recordingKey; recordingKey = null;
            if (value != null && k != null) { S.Hotkeys[k] = value; Changed(); Svc.Hotkeys.Apply(); }
            Rebuild();
        }

        public void AnimatedClose()
        {
            if (closing) return;
            closing = true;
            Store.SaveNow();
            Close();
        }

        void Build(int page)
        {
            shell = new Border { Background = Theme.GlassBase };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            shell.Child = g;
            Content = shell;
            var glow = new Border { Background = Theme.Glow, IsHitTestVisible = false };   // warm accent light in the corner
            Grid.SetColumnSpan(glow, 2);
            g.Children.Add(glow);

            var side = new Border { Background = Theme.GlassSide, BorderBrush = Theme.GlassEdge, BorderThickness = new Thickness(0, 0, 1, 0) };
            var sideGrid = new Grid { Margin = new Thickness(14, 20, 14, 14) };
            sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sideGrid.RowDefinitions.Add(new RowDefinition());
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 16) };
            brand.Children.Add(BrandLogo());
            brand.Children.Add(K.TB("Dynamic Island", 15, Theme.Text, FontWeights.SemiBold));
            sideGrid.Children.Add(brand);

            // search
            search = new TextBox();
            var sf = K.Field(search, "Search settings  (Ctrl+F)", 0, Theme.GlassField);
            var sfBox = (Border)sf.Children[0]; sfBox.BorderBrush = Theme.GlassEdge; sfBox.BorderThickness = new Thickness(1);
            var sg = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            var sIcon = K.Glyph("", 11, Theme.Text3); sIcon.HorizontalAlignment = HorizontalAlignment.Right; sIcon.Margin = new Thickness(0, 0, 12, 0); sIcon.IsHitTestVisible = false;
            sg.Children.Add(sf); sg.Children.Add(sIcon);
            Grid.SetRow(sg, 1);
            sideGrid.Children.Add(sg);
            searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            searchTimer.Tick += delegate { searchTimer.Stop(); RunSearch(); };
            search.TextChanged += delegate { searchTimer.Stop(); searchTimer.Start(); };

            var navScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
            navHost = new Grid();
            navInd = new Border { Height = 34, CornerRadius = new CornerRadius(10), Background = Theme.AccentSoft, BorderBrush = Theme.AccentEdge, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, Opacity = 0 };
            navIndT = new TranslateTransform(); navInd.RenderTransform = navIndT;
            navHost.Children.Add(navInd);
            var navSp = new StackPanel();
            navHost.Children.Add(navSp);
            navScroll.Content = navHost;
            Grid.SetRow(navScroll, 2);
            sideGrid.Children.Add(navScroll);

            NavGroup(navSp, "Personal", true);
            AddNav(navSp, "", "General", BuildGeneral);
            NavGroup(navSp, "Island", false);
            AddNav(navSp, "", "Appearance", BuildLook);
            AddNav(navSp, "", "Size & position", BuildSize);
            AddNav(navSp, "", "Behavior", BuildBehavior);
            AddNav(navSp, "", "Motion", BuildMotion);
            AddNav(navSp, "", "Notifications", BuildEvents);
            NavGroup(navSp, "Pages", false);
            AddNav(navSp, "", "Pages", BuildPages);
            AddNav(navSp, "", "Timer, To-do & Shelf", BuildTools);
            AddNav(navSp, "", "AI chat", BuildAi);
            AddNav(navSp, "", "Launcher & Wallpapers", BuildLauncher);
            NavGroup(navSp, "System", false);
            AddNav(navSp, "", "Shortcuts", BuildShortcuts);
            AddNav(navSp, "", "About", BuildAbout);
            side.Child = sideGrid;
            g.Children.Add(side);

            var right = new Grid();
            Grid.SetColumn(right, 1);
            scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Margin = new Thickness(0, 10, 0, 10) };
            pageHost = new Grid();
            scroller.Content = pageHost;
            right.Children.Add(scroller);
            var close = K.IconBtn("", 32, 11, Theme.Text2, AnimatedClose);
            close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top; close.Margin = new Thickness(0, 12, 12, 0); close.ToolTip = "Close (Esc)";
            right.Children.Add(close);
            g.Children.Add(right);

            shell.MouseLeftButtonDown += (s, e) =>
            {
                var p = e.GetPosition(shell);
                if (p.Y < 64 && p.X > 236) { try { DragMove(); } catch { } }
                else if (p.X < 236 && p.Y < 56) { try { DragMove(); } catch { } }
            };
            ShowPage(page);
        }

        // The sidebar logo. Nothing to see here. (Tap it a few times.)
        Border BrandLogo()
        {
            var logo = new Border { Width = 30, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Colors.Black), BorderThickness = new Thickness(S.EasterEgg ? 1.5 : 1), Margin = new Thickness(0, 0, 10, 0), RenderTransformOrigin = new Point(0.5, 0.5), VerticalAlignment = VerticalAlignment.Center };
            logo.BorderBrush = S.EasterEgg ? RainbowBrush() : (Brush)Theme.Text3;
            var st = new ScaleTransform(1, 1); logo.RenderTransform = st;
            int taps = 0; DateTime last = DateTime.MinValue;
            var cool = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            cool.Tick += delegate { cool.Stop(); taps = 0; ((SolidColorBrush)logo.Background).BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Colors.Black, TimeSpan.FromMilliseconds(500))); };
            logo.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;   // and no window drag from here
                if ((DateTime.Now - last).TotalMilliseconds > 1500) taps = 0;
                last = DateTime.Now; taps++;
                // squish and spring back
                var sx = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(460) };
                sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.35, KeyTime.FromPercent(0.22), K.OutCubic));
                sx.KeyFrames.Add(new EasingDoubleKeyFrame(0.92, KeyTime.FromPercent(0.55), K.OutCubic));
                sx.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), K.OutBack));
                var sy = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(460) };
                sy.KeyFrames.Add(new EasingDoubleKeyFrame(0.7, KeyTime.FromPercent(0.22), K.OutCubic));
                sy.KeyFrames.Add(new EasingDoubleKeyFrame(1.12, KeyTime.FromPercent(0.55), K.OutCubic));
                sy.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), K.OutBack));
                st.BeginAnimation(ScaleTransform.ScaleXProperty, sx); st.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
                // it warms up a little with every tap
                var target = IslandWindow.Rainbow[Math.Min(taps, 7) - 1];
                var c = Color.FromRgb((byte)(target.R * taps / 7.0), (byte)(target.G * taps / 7.0), (byte)(target.B * taps / 7.0));
                ((SolidColorBrush)logo.Background).BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(180)));
                cool.Stop(); cool.Start();
                if (taps >= 7)
                {
                    taps = 0;
                    if (!S.EasterEgg) { S.EasterEgg = true; Store.Save(); }
                    logo.BorderBrush = RainbowBrush(); logo.BorderThickness = new Thickness(1.5);
                    isl.Party();
                }
            };
            return logo;
        }

        static Brush RainbowBrush()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            var r = IslandWindow.Rainbow;
            for (int i = 0; i < r.Length; i++) b.GradientStops.Add(new GradientStop(r[i], i / (double)(r.Length - 1)));
            b.Freeze();
            return b;
        }

        static void NavGroup(StackPanel host, string name, bool first)
        {
            var t = K.TB(name.ToUpperInvariant(), 10, Theme.Text3, FontWeights.Bold);
            t.Margin = new Thickness(12, first ? 0 : 14, 0, 4);
            host.Children.Add(t);
        }

        void AddNav(StackPanel host, string glyph, string label, Func<FrameworkElement> builder)
        {
            int idx = navLabels.Count;
            builders.Add(builder); navTitles.Add(label);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
            var gl = K.Glyph(glyph, 13, Theme.Text2); gl.Margin = new Thickness(0, 0, 11, 0); gl.Width = 16;
            var tx = K.TB(label, 13, Theme.Text2, FontWeights.Medium);
            sp.Children.Add(gl); sp.Children.Add(tx);
            var b = new Border { Height = 34, Background = Brushes.Transparent, Child = sp, Cursor = Cursors.Hand, CornerRadius = new CornerRadius(10) };
            b.MouseEnter += delegate { if (curPage != idx) tx.Foreground = Theme.Text; };
            b.MouseLeave += delegate { if (curPage != idx) tx.Foreground = Theme.Text2; };
            b.MouseLeftButtonDown += (s, e) => e.Handled = true;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; ClearSearch(); ShowPage(idx); };
            navLabels.Add(tx); navGlyphs.Add(gl); navBtns.Add(b);
            host.Children.Add(b);
        }

        void MoveNavIndicator(bool anim)
        {
            if (curPage < 0 || curPage >= navBtns.Count) { navInd.BeginAnimation(OpacityProperty, K.AnimRaw(0, 160, K.OutCubic)); return; }
            double y;
            try { y = navBtns[curPage].TranslatePoint(new Point(0, 0), navHost).Y; } catch { return; }
            if (navInd.Opacity < 0.5 || !anim) { navIndT.BeginAnimation(TranslateTransform.YProperty, null); navIndT.Y = y; }
            else navIndT.BeginAnimation(TranslateTransform.YProperty, K.AnimRaw(y, 380, K.OutBack));
            navInd.BeginAnimation(OpacityProperty, K.AnimRaw(1, 160, K.OutCubic));
        }

        void SetNavColors()
        {
            for (int i = 0; i < navLabels.Count; i++)
            {
                navLabels[i].Foreground = i == curPage ? (Brush)Theme.Accent : Theme.Text2;
                navGlyphs[i].Foreground = i == curPage ? (Brush)Theme.Accent : Theme.Text2;
            }
        }

        void ShowPage(int idx)
        {
            if (idx == curPage || idx < 0 || idx >= builders.Count) return;
            int dir = idx > curPage ? 1 : -1;
            curPage = idx; recordingKey = null;
            SetNavColors();
            if (IsLoaded) MoveNavIndicator(true);
            Show(builders[idx](), dir);
        }

        void Rebuild()
        {
            double off = scroller.VerticalOffset;
            if (curPage < 0) { if (query.Length > 0) ShowSearchResults(0); }
            else Show(builders[curPage](), 0);
            scroller.ScrollToVerticalOffset(off);
        }

        void Show(FrameworkElement page, int dir)
        {
            pageHost.Children.Clear();
            pageHost.Children.Add(page);
            if (dir == 0) return;
            scroller.ScrollToTop();
            var tt = new TranslateTransform(0, 16 * dir); page.RenderTransform = tt; page.Opacity = 0;
            page.BeginAnimation(OpacityProperty, K.AnimRaw(1, 300, K.OutQuint));
            tt.BeginAnimation(TranslateTransform.YProperty, K.AnimRaw(0, 440, K.OutQuint));
            var sp = page as Panel;
            if (sp != null && Store.AnimScale() > 0)
            {
                int i = 0;
                foreach (UIElement c in sp.Children)
                {
                    if (i++ < 2) continue;
                    c.Opacity = 0;
                    var a = K.AnimRaw(1, 320, K.OutCubic); a.BeginTime = TimeSpan.FromMilliseconds(40 + Math.Min(12, i) * 28);
                    c.BeginAnimation(OpacityProperty, a);
                }
            }
        }

        // ------------------------------------------------------------ search
        void ClearSearch()
        {
            if (search == null || query.Length == 0) return;
            query = "";
            searchTimer.Stop();
            search.Text = "";
        }

        void RunSearch()
        {
            string q = search.Text.Trim();
            if (q == query) return;
            query = q;
            if (q.Length == 0) { if (curPage < 0) ShowPage(prevPage); return; }
            int was = curPage;
            if (was >= 0) prevPage = was;
            curPage = -1; SetNavColors(); MoveNavIndicator(true);
            ShowSearchResults(was < 0 ? 0 : 1);
        }

        void ShowSearchResults(int dir)
        {
            string q = query.ToLowerInvariant();
            var terms = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var p = Page("Search", "");
            var sub = (TextBlock)p.Children[1];
            int total = 0;
            for (int i = 0; i < builders.Count; i++)
            {
                var built = builders[i]() as Panel;
                if (built == null) continue;
                bool titleHit = terms.All(t => navTitles[i].ToLowerInvariant().Contains(t));
                var hits = new List<FrameworkElement>();
                foreach (var row in FindRows(built))
                {
                    string hay = ((string)row.Tag).ToLowerInvariant() + " " + navTitles[i].ToLowerInvariant();
                    if (terms.All(t => hay.Contains(t))) hits.Add(row);
                }
                if (hits.Count == 0 && !titleHit) continue;
                int idx = i;
                // section header doubles as a link to the full page
                var link = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 22, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                link.Children.Add(K.TB(navTitles[i].ToUpperInvariant(), 11, Theme.Accent, FontWeights.SemiBold));
                var chev = K.Glyph("", 9, Theme.Accent); chev.Margin = new Thickness(6, 1, 0, 0);
                link.Children.Add(chev);
                link.MouseLeftButtonUp += (s, e) => { e.Handled = true; ClearSearch(); curPage = -1; ShowPage(idx); };
                link.ToolTip = "Open " + navTitles[i];
                p.Children.Add(link);
                if (hits.Count > 0)
                {
                    foreach (var h in hits) { var par = h.Parent as Panel; if (par != null) par.Children.Remove(h); }
                    p.Children.Add(Card(hits.ToArray()));
                    total += hits.Count;
                }
            }
            sub.Text = total == 0 && p.Children.Count <= 2 ? "Nothing matches \"" + query + "\". Try another word, e.g. \"hover\", \"clock\" or \"battery\"." : total + (total == 1 ? " setting" : " settings") + " match \"" + query + "\"";
            Show(p, dir);
        }

        static IEnumerable<FrameworkElement> FindRows(DependencyObject root)
        {
            foreach (var c in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                var fe = c as FrameworkElement;
                if (fe != null && fe.Tag is string && ((string)fe.Tag).StartsWith("§")) { yield return fe; continue; }
                foreach (var r in FindRows(c)) yield return r;
            }
        }

        // ------------------------------------------------------------ helpers
        void Changed() { Store.Save(); isl.ApplySettings(); }

        static StackPanel Page(string title, string sub)
        {
            var sp = new StackPanel { Margin = new Thickness(32, 18, 36, 30) };
            sp.Children.Add(K.TB(title, 26, Theme.Text, FontWeights.SemiBold));
            var d = K.TB(sub, 13, Theme.Text2, FontWeights.Normal); d.Margin = new Thickness(0, 4, 0, 4); d.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(d);
            sp.Children.Add(new Border { Height = 1, Background = Theme.GlassEdge, Margin = new Thickness(0, 14, 0, 4) });
            return sp;
        }

        static TextBlock Header(string text)
        {
            var t = K.TB(text.ToUpperInvariant(), 11, Theme.Text3, FontWeights.SemiBold);
            t.Margin = new Thickness(4, 22, 0, 0);
            return t;
        }

        static Border Card(params FrameworkElement[] rows)
        {
            var sp = new StackPanel();
            bool first = true;
            foreach (var r in rows)
            {
                if (r == null) continue;
                if (!first) sp.Children.Add(new Border { Height = 1, Background = Theme.Hairline, Margin = new Thickness(16, 0, 0, 0) });
                sp.Children.Add(r); first = false;
            }
            return new Border { CornerRadius = new CornerRadius(14), Background = Theme.GlassCard, BorderBrush = Theme.GlassEdge, BorderThickness = new Thickness(1), Margin = new Thickness(0, 8, 0, 0), Child = sp };
        }

        // A labelled settings row. Tag carries its words for search ("§" marks it as a row).
        static Grid Row(string title, string desc, FrameworkElement ctl)
        {
            var g = new Grid { Margin = new Thickness(16, 11, 16, 11), MinHeight = 32, Tag = "§" + title + " " + (desc ?? "") };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            left.Children.Add(K.TB(title, 13.5, Theme.Text, FontWeights.Medium));
            if (!string.IsNullOrEmpty(desc))
            {
                var d = K.TB(desc, 11.5, Theme.Text2, FontWeights.Normal); d.TextWrapping = TextWrapping.Wrap; d.Margin = new Thickness(0, 2, 0, 0);
                left.Children.Add(d);
            }
            g.Children.Add(left);
            if (ctl != null) { ctl.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(ctl, 1); g.Children.Add(ctl); }
            return g;
        }

        Grid Tog(string title, string desc, bool value, Action<bool> set)
        {
            var t = new Toggle(value);
            t.Changed += v => { set(v); Changed(); };
            return Row(title, desc, t);
        }
        // a toggle whose change shows/hides dependent rows (rebuilds the page)
        Grid TogR(string title, string desc, bool value, Action<bool> set)
        {
            var t = new Toggle(value);
            t.Changed += v => { set(v); Changed(); var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) }; tm.Tick += delegate { tm.Stop(); Rebuild(); }; tm.Start(); };
            return Row(title, desc, t);
        }

        Grid Txt(string title, string desc, string value, double width, string placeholder, Action<string> set)
        {
            var tb = new TextBox { Text = value ?? "" };
            var f = K.Field(tb, placeholder, width, Theme.Surface);
            tb.TextChanged += delegate { set(tb.Text); Changed(); };
            return Row(title, desc, f);
        }

        Grid Seg(string title, string desc, string[] items, int sel, double segW, Action<int> set)
        {
            var s = new Segmented(items, sel, segW);
            s.Changed += i => { set(i); Changed(); };
            return Row(title, desc, s);
        }

        // Slider with a live value label and an optional ↺ reset to its default.
        Grid Sld(string title, string desc, double min, double max, double step, double value, Func<double, string> fmt, Action<double> set, double? reset)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var lbl = K.TB(fmt(value), 12, Theme.Text2, FontWeights.SemiBold); lbl.Width = 62; lbl.TextAlignment = TextAlignment.Right; lbl.Margin = new Thickness(0, 0, 10, 0);
            var sl = new SliderBar(min, max, step, value, 190, 5);
            sl.Changed += v => { lbl.Text = fmt(v); set(v); isl.ApplySettings(); };
            sl.Committed += v => Store.Save();
            sp.Children.Add(lbl); sp.Children.Add(sl);
            if (reset.HasValue)
            {
                double d = reset.Value;
                var r = K.IconBtn("", 28, 11, Theme.Text2, () => { sl.SetValue(d, true); Store.Save(); });
                r.ToolTip = "Reset to " + fmt(d); r.Margin = new Thickness(6, 0, 0, 0);
                sp.Children.Add(r);
            }
            return Row(title, desc, sp);
        }

        static Func<double, string> Px { get { return v => v.ToString("0") + " px"; } }
        static Func<double, string> Sec { get { return v => (v / 1000).ToString("0.0", K.EN) + " s"; } }
        static Func<double, string> Ms { get { return v => v.ToString("0") + " ms"; } }
        static Func<double, string> Pct { get { return v => Math.Round(v * 100) + "%"; } }

        // ------------------------------------------------------------ Personal
        FrameworkElement BuildGeneral()
        {
            var p = Page("General", "Who you are and how the island greets you.");
            p.Children.Add(Header("Profile"));
            p.Children.Add(Card(
                Txt("Your name", "Used in the greeting, on Home and in the AI chat.", S.UserName, 230, "e.g. Alex", v => S.UserName = v)));
            p.Children.Add(Header("Greeting"));
            p.Children.Add(Card(
                TogR("Say hello on start", "The island greets you when it starts.", S.Welcome, v => S.Welcome = v),
                S.Welcome ? Seg("When", null, new[] { "Every start", "Once per login" }, S.WelcomeMode, 122, i => S.WelcomeMode = i) : null,
                S.Welcome ? Txt("Greeting text", "Empty = a time-of-day greeting. {name} becomes your name.", S.GreetingText, 230, "Hey {name}, let's go!", v => S.GreetingText = v) : null,
                Row("Preview", null, K.PillBtn("Show greeting", Theme.Surface, Theme.Text, 30, () => isl.PreviewWelcome()))));
            var start = new Toggle(Store.IsStartup());
            start.Changed += v => Store.SetStartup(v);
            p.Children.Add(Header("Startup"));
            p.Children.Add(Card(Row("Start with Windows", "The island appears right after you sign in.", start)));
            return p;
        }

        // ------------------------------------------------------------ Island
        FrameworkElement BuildLook()
        {
            var p = Page("Appearance", "Theme, font and the island's own look. Everything switches live.");
            p.Children.Add(Header("Theme"));
            var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var all = new List<Palette>(Theme.Palettes);
            all.Insert(0, Theme.MakeWallpaper(WallpaperService.CurrentSeed()));
            all.Add(Theme.MakeCustom(S.CustomAccent, S.CustomAccent2, S.TintHue, S.TintStrength, S.Depth));
            foreach (var pal in all)
            {
                var pl = pal;
                bool sel = S.Palette == pl.Key;
                var card = new Border { Width = 140, Height = 84, CornerRadius = new CornerRadius(12), Background = K.Solid(pl.Surface), Margin = new Thickness(0, 0, 10, 10), Cursor = Cursors.Hand, BorderThickness = new Thickness(2), BorderBrush = sel ? (Brush)K.Solid(pl.Accent) : Brushes.Transparent, RenderTransformOrigin = new Point(0.5, 0.5), Tag = "§Theme " + pl.Label + " palette colour color" };
                var cg = new Grid { Margin = new Thickness(10) };
                var pill = new Border { Width = 56, Height = 16, CornerRadius = new CornerRadius(8), Background = K.Solid(pl.Island), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
                foreach (var c in new[] { pl.Accent, pl.Green, pl.Orange, pl.Red })
                    dots.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), Background = K.Solid(c), Margin = new Thickness(3, 3, 0, 0) });
                var name = K.TB(pl.Label, 12.5, K.Solid(pl.Text), FontWeights.SemiBold); name.VerticalAlignment = VerticalAlignment.Bottom;
                var bar = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = K.Solid(pl.Raised), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 20, 0) };
                cg.Children.Add(pill); cg.Children.Add(dots); cg.Children.Add(bar); cg.Children.Add(name);
                if (sel)
                {
                    var tick = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = K.Solid(pl.Accent), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Child = K.Glyph("", 9, Brushes.White) };
                    cg.Children.Add(tick);
                }
                card.Child = cg;
                K.Pressable(card, () => { S.Palette = pl.Key; Changed(); Rebuild(); }, false);
                wrap.Children.Add(card);
            }
            p.Children.Add(wrap);

            if (S.Palette == "custom")
            {
                p.Children.Add(Header("Custom theme"));
                p.Children.Add(Card(
                    ColorRow("Accent", S.CustomAccent, v => { S.CustomAccent = v; S.Palette = "custom"; }),
                    ColorRow("Accent 2 (gradient)", S.CustomAccent2, v => { S.CustomAccent2 = v; S.Palette = "custom"; }),
                    Sld("Background hue", null, 0, 1, 0.01, S.TintHue, v => ((int)(v * 360)) + "°", v => { S.TintHue = v; S.Palette = "custom"; }, 0.64),
                    Sld("Tint strength", null, 0, 1, 0.01, S.TintStrength, Pct, v => { S.TintStrength = v; S.Palette = "custom"; }, 0.25),
                    Sld("Depth", "How much lighter raised surfaces are.", 0, 1, 0.01, S.Depth, Pct, v => { S.Depth = v; S.Palette = "custom"; }, 0.4),
                    Row("Reset custom theme", null, K.GlyphPill("", "Reset", Theme.Surface, Theme.Text, 30, () =>
                    {
                        S.CustomAccent = "#7aa2f7"; S.CustomAccent2 = "#bb9af7"; S.TintHue = 0.64; S.TintStrength = 0.25; S.Depth = 0.4;
                        Changed(); Rebuild();
                    }))));
            }
            else
            {
                var hint = K.TB("Pick the last card (Custom) to mix your own accent and background.", 11.5, Theme.Text3, FontWeights.Normal);
                hint.Margin = new Thickness(4, 2, 0, 0);
                p.Children.Add(hint);
            }

            p.Children.Add(Header("Font"));
            p.Children.Add(Card(
                Seg("Typeface", "Inter and JetBrainsMono Nerd Font are bundled. Custom uses any installed font.", new[] { "Inter", "Segoe UI", "Custom", "JetBrains" }, S.Font, 78, i => { S.Font = i; Rebuild(); }),
                S.Font == 2 ? Txt("Font name", "Exactly as it appears in Windows Settings → Fonts.", S.FontCustom, 230, "e.g. Poppins", v => S.FontCustom = v) : null));

            p.Children.Add(Header("Island"));
            p.Children.Add(Card(
                Seg("Style", "Notch hangs from the top edge with curved shoulders.", new[] { "Pill", "Notch" }, S.Style, 82, i => S.Style = i),
                Seg("Shape", "Corner rounding of the open island.", new[] { "Capsule", "Rounded", "Square" }, S.Shape, 78, i => S.Shape = i)));
            p.Children.Add(Header("Fill & outline"));
            p.Children.Add(BuildLookCard());

            p.Children.Add(Header("Effects"));
            p.Children.Add(Card(
                Tog("Shadow", "Soft shadow under the open island.", S.Shadows, v => S.Shadows = v),
                Tog("Glow while playing", "The island glows in the album art colour while music plays.", S.ExtraMotion, v => S.ExtraMotion = v),
                Tog("Ambient album art", "Blurred artwork behind the Media page.", S.AmbientMedia, v => S.AmbientMedia = v)));
            return p;
        }

        // ---- Fill & outline: a live preview of the island plus visual pickers. Every tile shows
        // the real result (that fill with your outline, or your fill with that outline) and all of
        // it cross-fades in step with the island itself.
        readonly List<Action<bool>> lookPaint = new List<Action<bool>>();

        Border BuildLookCard()
        {
            lookPaint.Clear();
            var pal = Theme.Current;

            // preview: a bit of "desktop" with the island on it
            var stage = new Border { Height = 96, CornerRadius = new CornerRadius(12), Margin = new Thickness(16, 14, 16, 4), ClipToBounds = true, Tag = "§Island look preview fill outline gradient border edge colour color" };
            stage.Background = LookBackdrop();
            var stageGrid = new Grid();
            var bigFill = IslandLook.Brush(Colors.Black, Colors.Black); var bigStroke = IslandLook.Brush(Colors.Transparent, Colors.Transparent);
            bool notch = S.Style == 1;
            var big = new Border
            {
                Width = 184, Height = 38, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, notch ? 0 : 14, 0, 0), Background = bigFill, BorderBrush = bigStroke,
                CornerRadius = notch ? new CornerRadius(0, 0, 16, 16) : new CornerRadius(19), RenderTransformOrigin = new Point(0.5, 0)
            };
            var bigScale = new ScaleTransform(1, 1); big.RenderTransform = bigScale;
            var content = new Grid { Margin = new Thickness(10, 0, 14, 0) };
            var art = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left, Background = new LinearGradientBrush(pal.Accent, IslandLook.Second(pal), 45) };
            var clock = K.TB(DateTime.Now.ToString(S.Clock24h ? "HH:mm" : "h:mm", K.EN), 13, K.Solid(pal.Text), FontWeights.SemiBold);
            clock.HorizontalAlignment = HorizontalAlignment.Right; clock.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(art); content.Children.Add(clock);
            big.Child = content;
            stageGrid.Children.Add(big);
            var caption = K.TB("", 11.5, Theme.Text2, FontWeights.Medium);
            caption.HorizontalAlignment = HorizontalAlignment.Center; caption.VerticalAlignment = VerticalAlignment.Bottom; caption.Margin = new Thickness(0, 0, 0, 10);
            stageGrid.Children.Add(caption);
            stage.Child = stageGrid;
            lookPaint.Add(anim =>
            {
                PaintLook(bigFill, bigStroke, S.Tint, S.Outline, anim);
                double bt = S.Outline == 0 ? 0 : Math.Max(0.5, S.OutlineWidth);
                big.BorderThickness = new Thickness(bt, notch ? 0 : bt, bt, bt);
                caption.Text = IslandLook.Fills[S.Tint] + " fill  ·  " + (S.Outline == 0 ? "no outline" : IslandLook.Outlines[S.Outline].ToLowerInvariant() + " outline");
                if (anim && Store.AnimScale() > 0)
                {
                    var pop = new DoubleAnimationUsingKeyFrames();
                    pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.05, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), K.OutCubic));
                    pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)), new ElasticEase { Oscillations = 1, Springiness = 6, EasingMode = EasingMode.EaseOut }));
                    bigScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop); bigScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                }
            });

            var width = Sld("Outline width", null, 0.5, 3, 0.25, S.OutlineWidth, v => v.ToString("0.##", K.EN) + " px", v => { S.OutlineWidth = v; RepaintLook(false); }, 1);
            width.Visibility = S.Outline == 0 ? Visibility.Collapsed : Visibility.Visible;

            var fillRow = LookPicker("Fill", "The island's colour. Gradient blends your accent colours.", IslandLook.Fills, () => S.Tint, i => S.Tint = i, (i, f, s, a) => PaintLook(f, s, i, S.Outline, a), null);
            var outRow = LookPicker("Outline", "A thin edge that lifts the island off dark wallpapers.", IslandLook.Outlines, () => S.Outline, i => S.Outline = i, (i, f, s, a) => PaintLook(f, s, S.Tint, i, a), i => FadeRow(width, i != 0));

            var card = Card(stage, fillRow, outRow, width);
            RepaintLook(false);
            return card;
        }

        void RepaintLook(bool anim) { foreach (var a in lookPaint) a(anim); }

        // a tinted "wallpaper" so every fill (Surface included) stands out from what's behind it
        static Brush LookBackdrop()
        {
            var pal = Theme.Current;
            var b = new LinearGradientBrush(IslandLook.Mix(pal.Hover, pal.Accent, 0.22), IslandLook.Mix(pal.Raised, IslandLook.Second(pal), 0.34), 35);
            b.Freeze();
            return b;
        }

        static void PaintLook(LinearGradientBrush fill, LinearGradientBrush stroke, int tint, int outline, bool anim)
        {
            Color f1, f2, s1, s2;
            IslandLook.Fill(Theme.Current, tint, out f1, out f2);
            IslandLook.Stroke(Theme.Current, outline, out s1, out s2);
            SetStop(fill.GradientStops[0], f1, anim, 0); SetStop(fill.GradientStops[1], f2, anim, 90);
            SetStop(stroke.GradientStops[0], s1, anim, 0); SetStop(stroke.GradientStops[1], s2, anim, 90);
        }

        static void SetStop(GradientStop st, Color c, bool anim, int lag)
        {
            if (!anim || Store.AnimScale() <= 0) { st.BeginAnimation(GradientStop.ColorProperty, null); st.Color = c; return; }
            var a = new ColorAnimation(c, TimeSpan.FromMilliseconds(K.Dur(480))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, BeginTime = TimeSpan.FromMilliseconds(K.Dur(lag)) };
            st.BeginAnimation(GradientStop.ColorProperty, a);
        }

        static void FadeRow(FrameworkElement row, bool show)
        {
            if (show == (row.Visibility == Visibility.Visible)) return;
            if (show)
            {
                row.Visibility = Visibility.Visible; row.Opacity = 0;
                row.BeginAnimation(OpacityProperty, K.AnimRaw(1, 260, K.OutCubic));
            }
            else
            {
                var a = K.AnimRaw(0, 160, K.OutCubic);
                a.Completed += delegate { if (row.Opacity < 0.05) row.Visibility = Visibility.Collapsed; };
                row.BeginAnimation(OpacityProperty, a);
            }
        }

        // Title + description on top, a row of preview tiles under it.
        FrameworkElement LookPicker(string title, string desc, string[] items, Func<int> get, Action<int> set, Action<int, LinearGradientBrush, LinearGradientBrush, bool> paint, Action<int> after)
        {
            var root = new StackPanel { Margin = new Thickness(16, 12, 16, 14), Tag = "§" + title + " " + desc + " " + string.Join(" ", items) + " island colour color" };
            root.Children.Add(K.TB(title, 13.5, Theme.Text, FontWeights.Medium));
            var d = K.TB(desc, 11.5, Theme.Text2, FontWeights.Normal); d.TextWrapping = TextWrapping.Wrap; d.Margin = new Thickness(0, 2, 0, 10);
            root.Children.Add(d);
            var tiles = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = items.Length };
            var pal = Theme.Current;
            var sels = new List<Action<bool>>();
            var backdrop = LookBackdrop();
            for (int i = 0; i < items.Length; i++)
            {
                int idx = i;
                var ring = new SolidColorBrush(Colors.Transparent);
                var tile = new Border { Height = 70, CornerRadius = new CornerRadius(11), Background = backdrop, BorderBrush = ring, BorderThickness = new Thickness(2), Margin = new Thickness(idx == 0 ? 0 : 4, 0, idx == items.Length - 1 ? 0 : 4, 0), Cursor = Cursors.Hand, RenderTransformOrigin = new Point(0.5, 0.5) };
                var tg = new Grid();
                var f = IslandLook.Brush(Colors.Black, Colors.Black); var s = IslandLook.Brush(Colors.Transparent, Colors.Transparent);
                var pill = new Border { Width = 58, Height = 20, CornerRadius = new CornerRadius(10), Background = f, BorderBrush = s, BorderThickness = new Thickness(1.2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 0, 0) };
                var lblBrush = new SolidColorBrush(Theme.A(pal.Text, 0.6));
                var lbl = K.TB(items[idx], 11.5, lblBrush, FontWeights.SemiBold); lbl.HorizontalAlignment = HorizontalAlignment.Center; lbl.VerticalAlignment = VerticalAlignment.Bottom; lbl.Margin = new Thickness(0, 0, 0, 9);
                tg.Children.Add(pill); tg.Children.Add(lbl);
                tile.Child = tg;
                lookPaint.Add(anim => paint(idx, f, s, anim));
                sels.Add(anim =>
                {
                    bool on = get() == idx;
                    Color rc = on ? pal.Accent : Colors.Transparent, lc = on ? pal.Accent : Theme.A(pal.Text, 0.6);
                    if (anim && Store.AnimScale() > 0)
                    {
                        ring.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(rc, TimeSpan.FromMilliseconds(K.Dur(220))));
                        lblBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(lc, TimeSpan.FromMilliseconds(K.Dur(220))));
                    }
                    else { ring.Color = rc; lblBrush.Color = lc; }
                });
                tile.MouseEnter += delegate { if (get() != idx) ring.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Theme.A(pal.Text, 0.28), TimeSpan.FromMilliseconds(140))); };
                tile.MouseLeave += delegate { if (get() != idx) ring.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Colors.Transparent, TimeSpan.FromMilliseconds(200))); };
                K.Pressable(tile, () =>
                {
                    if (get() == idx) return;
                    set(idx); Changed();
                    foreach (var sel in sels) sel(true);
                    RepaintLook(true);
                    if (Store.AnimScale() > 0) K.PopIn(pill, 0.7, 520);
                    if (after != null) after(idx);
                }, false);
                tiles.Children.Add(tile);
            }
            foreach (var sel in sels) sel(false);
            root.Children.Add(tiles);
            return root;
        }

        Grid ColorRow(string title, string value, Action<string> set)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var swatches = new[] { "#0a84ff", "#7aa2f7", "#bb9af7", "#cba6f7", "#ff6b9d", "#fb923c", "#facc15", "#86efac", "#5eead4", "#64d2ff" };
            var tb = new TextBox { Text = value, Width = 74 };
            var f = K.Field(tb, "#rrggbb", 0, Theme.Surface);
            var prev = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = K.Solid(Theme.Hex(value)), Margin = new Thickness(0, 0, 8, 0) };
            foreach (var sw in swatches)
            {
                string h = sw;
                var d = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = K.Solid(Theme.Hex(h)), Margin = new Thickness(0, 0, 5, 0), Cursor = Cursors.Hand };
                d.MouseLeftButtonDown += (s, e) => e.Handled = true;
                d.MouseLeftButtonUp += (s, e) => { e.Handled = true; tb.Text = h; };
                sp.Children.Add(d);
            }
            sp.Children.Add(new Border { Width = 8 });
            sp.Children.Add(prev); sp.Children.Add(f);
            tb.TextChanged += delegate
            {
                string t = tb.Text.Trim();
                if (System.Text.RegularExpressions.Regex.IsMatch(t, "^#[0-9a-fA-F]{6}$"))
                {
                    prev.Background = K.Solid(Theme.Hex(t)); set(t); Changed();
                }
            };
            return Row(title, null, sp);
        }

        FrameworkElement BuildSize()
        {
            var p = Page("Size & position", "How big the island is and where it sits.");
            p.Children.Add(Header("Size"));
            p.Children.Add(Card(
                Seg("Preset", "Pill height and widths of the compact views.", new[] { "Compact", "Default", "Large" }, S.Size, 86, i => S.Size = i),
                Sld("Scale", "Scales the whole island, open or closed.", 0.6, 1.6, 0.05, S.Scale, Pct, v => S.Scale = v, 1.0),
                Sld("Open width", "Width of the expanded pages.", 380, 560, 10, S.ExpandedWidth, Px, v => S.ExpandedWidth = (int)v, 420)));
            p.Children.Add(Header("Position"));
            p.Children.Add(Card(
                Sld("Horizontal offset", "Nudge left or right from the centre.", -600, 600, 2, S.OffsetX, Px, v => S.OffsetX = (int)v, 0),
                Sld("Vertical offset", "Distance from the top edge.", 0, 80, 1, S.OffsetY, Px, v => S.OffsetY = (int)v, 0),
                Row("Reset position", "Back to the top centre.", K.GlyphPill("", "Reset", Theme.Surface, Theme.Text, 30, () => { S.OffsetX = 0; S.OffsetY = 0; Changed(); Rebuild(); }))));
            return p;
        }

        FrameworkElement BuildBehavior()
        {
            var p = Page("Behavior", "When the island shows, what it shows while idle, and how it reacts to you.");
            p.Children.Add(Header("Visibility"));
            p.Children.Add(Card(
                Seg("Mode", "Smart tucks the island into a tiny tab when the active window sits under it. Auto-hide always tucks it away. Hover the tab or any event brings it back.", new[] { "Always", "Smart", "Auto-hide" }, S.Visibility, 82, i => S.Visibility = i),
                Tog("Mini notch in fullscreen", "Over videos and games the island tucks into a small tab that shows music and timers. Off fades it away completely.", S.FullscreenNotch, v => S.FullscreenNotch = v),
                Tog("Quiet in fullscreen", "Song changes don't open the island over a fullscreen app; the tab just pings.", S.FullscreenQuiet, v => S.FullscreenQuiet = v),
                Tog("Show when idle", "Off hides the island when nothing is happening.", S.ShowWhenIdle, v => S.ShowWhenIdle = v)));
            p.Children.Add(Header("Idle island"));
            p.Children.Add(Card(
                TogR("Clock", "Time on the closed island.", S.IdleClock, v => S.IdleClock = v),
                S.IdleClock ? Tog("24-hour time", null, S.Clock24h, v => S.Clock24h = v) : null,
                S.IdleClock ? Tog("Seconds", null, S.ClockSeconds, v => S.ClockSeconds = v) : null,
                Tog("Date", null, S.IdleDate, v => S.IdleDate = v)));
            p.Children.Add(Header("Pointer & touchpad"));
            p.Children.Add(Card(
                TogR("Open on hover", "Rest the pointer on the island to open it.", S.HoverExpand, v => S.HoverExpand = v),
                S.HoverExpand ? Sld("Hover delay", "How long the pointer rests before it opens.", 80, 900, 20, S.HoverDelay, Ms, v => S.HoverDelay = (int)v, 260) : null,
                Tog("Pause on hover", "Events stay while the pointer is over the island.", S.PauseOnHover, v => S.PauseOnHover = v),
                Tog("Scroll for volume", "Scroll over the closed island to change the volume.", S.ScrollVolume, v => S.ScrollVolume = v),
                Tog("Reverse swipe", "Two-finger swipe on the touchpad changes pages.", S.InvertSwipe, v => S.InvertSwipe = v)));
            return p;
        }

        FrameworkElement BuildMotion()
        {
            var p = Page("Motion", "Speed and character of every animation.");
            p.Children.Add(Header("Animations"));
            p.Children.Add(Card(
                Seg("Speed", "Scales every duration on the island.", new[] { "Off", "Light", "Normal", "Relaxed", "Custom" }, S.AnimMode, 68, i => { S.AnimMode = i; Rebuild(); }),
                S.AnimMode == 4 ? Sld("Custom speed", "1× = Normal. Higher is slower.", 0.3, 2.0, 0.05, S.AnimCustom, v => v.ToString("0.00", K.EN) + "×", v => S.AnimCustom = v, 1.0) : null,
                Tog("Springy", "A soft overshoot when the island changes shape and pages settle.", S.Springy, v => S.Springy = v),
                Tog("Content motion", "Content glides out from the centre and rows cascade in as the island opens.", S.ContentMotion, v => S.ContentMotion = v),
                Row("Try it", "Plays an event so you can feel the settings.", K.PillBtn("Play a sample event", Theme.AccentFill, Theme.OnAccent, 30, () => isl.SampleEvent()))));
            p.Children.Add(Header("Rendering"));
            p.Children.Add(Card(
                Seg("Renderer", "GPU is smoothest. Use Compatible if the island looks wrong (black box) on your PC. Needs a restart.", new[] { "GPU", "Compatible" }, S.Renderer, 96, i => { S.Renderer = i; Store.SaveNow(); Rebuild(); }),
                Row("Restart to apply", null, K.PillBtn("Restart", Theme.Surface, Theme.Text, 30, () => isl.Restart()))));
            return p;
        }

        Toggle DndToggle() { var t = new Toggle(S.DoNotDisturb); t.Changed += v => isl.SetDoNotDisturb(v); return t; }

        FrameworkElement BuildEvents()
        {
            var p = Page("Notifications", "What may pop up on the island, and for how long.");
            p.Children.Add(Header("Show"));
            p.Children.Add(Card(
                Tog("Volume", "Icon · bar · % (drag the bar to change it).", S.EvVolume, v => S.EvVolume = v),
                Tog("Brightness", "Laptops only.", S.EvBrightness, v => S.EvBrightness = v),
                Tog("Charging", "\"Charging 58%\" when you plug in or unplug.", S.EvCharging, v => S.EvCharging = v),
                Tog("Low battery", null, S.EvLowBattery, v => S.EvLowBattery = v),
                Tog("Network", "Wi-Fi / Ethernet connected or dropped.", S.EvNetwork, v => S.EvNetwork = v),
                Tog("Bluetooth", "A device connected or disconnected.", S.EvBluetooth, v => S.EvBluetooth = v),
                Tog("Displays", "A monitor plugged in or removed.", S.EvDisplay, v => S.EvDisplay = v),
                Tog("Microphone", "Mic muted or unmuted.", S.EvMic, v => S.EvMic = v),
                Tog("Track change", "A short peek: artwork, title, artist.", S.EvTrack, v => S.EvTrack = v),
                Tog("Clipboard", "\"Copied\" when you copy something.", S.EvClipboard, v => S.EvClipboard = v)));
            p.Children.Add(Header("Other apps"));
            string stat = isl.AppNotifyStatus;
            var mutedTb = new TextBox { Text = string.Join(", ", S.NotifyMuted ?? new List<string>()) };
            var mutedF = K.Field(mutedTb, "e.g. OneDrive, NVIDIA", 230, Theme.Surface);
            mutedTb.TextChanged += delegate { S.NotifyMuted = mutedTb.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); Changed(); };
            // one-click mute chips for apps that have notified recently
            var chips = new WrapPanel { Margin = new Thickness(16, 0, 16, 12) };
            isl.RecentNotifyApps(names => Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var n in names)
                {
                    string name = n;
                    bool muted = S.NotifyMuted.Any(m => name.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
                    var c = K.GlyphPill(muted ? "\uE74F" : "\uE767", name, muted ? Theme.RedSoft : Theme.Surface, muted ? Theme.Red : Theme.Text2, 26, () =>
                    {
                        if (muted) S.NotifyMuted.RemoveAll(m => name.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0); else S.NotifyMuted.Add(name);
                        Changed(); Rebuild();
                    });
                    c.Margin = new Thickness(0, 0, 6, 6); c.ToolTip = muted ? "Muted · click to show again" : "Click to mute";
                    chips.Children.Add(c);
                }
                if (names.Count == 0) chips.Children.Add(K.TB("Apps that send notifications will show up here.", 11, Theme.Text3, FontWeights.Normal));
            })));
            var chipsWrap = new StackPanel { Tag = "§Mute apps notifications" };
            var chipsLbl = K.TB("Recent apps · click to mute or unmute", 11.5, Theme.Text2, FontWeights.Normal); chipsLbl.Margin = new Thickness(16, 10, 16, 6);
            chipsWrap.Children.Add(chipsLbl); chipsWrap.Children.Add(chips);
            p.Children.Add(Card(
                TogR("Notifications from other apps", stat == "active" ? "WhatsApp, Discord, browsers, mail… whatever shows a Windows notification." : "Windows notification access: " + stat + ".", S.EvApps, v => S.EvApps = v),
                stat == "denied" || stat == "not allowed yet" ? Row("Allow access", "Windows Settings → Privacy → Notifications → let apps access your notifications. Then restart the island.", K.PillBtn("Open Windows settings", Theme.Surface, Theme.Text, 30, () => { try { System.Diagnostics.Process.Start("ms-settings:privacy-notifications"); } catch { } })) : null,
                S.EvApps ? Row("Do not disturb", "Holds app notifications; you get a summary when you turn it off. Also in the tray menu.", DndToggle()) : null,
                S.EvApps ? Tog("Show message text", "Off shows only the app and \"New notification\" (handy when sharing your screen).", S.NotifyShowText, v => S.NotifyShowText = v) : null,
                S.EvApps ? Sld("How long they stay", null, 2000, 12000, 500, S.AppNotifyMs, Sec, v => S.AppNotifyMs = (int)v, 5000) : null,
                S.EvApps ? Row("Muted apps", "Comma separated, part of the name is enough.", mutedF) : null,
                S.EvApps ? (FrameworkElement)chipsWrap : null));
            p.Children.Add(Header("How long they stay"));
            p.Children.Add(Card(
                Sld("Volume / brightness", null, 800, 5000, 100, S.OsdMs, Sec, v => S.OsdMs = (int)v, 1600),
                Sld("System events", "Battery, network, devices, clipboard.", 1500, 8000, 100, S.SystemMs, Sec, v => S.SystemMs = (int)v, 3500),
                Sld("Track change", null, 1500, 6000, 100, S.TrackMs, Sec, v => S.TrackMs = (int)v, 3000)));
            p.Children.Add(Header("Battery"));
            p.Children.Add(Card(
                Sld("Low level", null, 5, 50, 1, S.LowBattery, v => v.ToString("0") + "%", v => S.LowBattery = (int)v, 20),
                Sld("Critical level", "Critical alerts stay until dismissed.", 3, 30, 1, S.CriticalBattery, v => v.ToString("0") + "%", v => S.CriticalBattery = (int)v, 10)));
            return p;
        }

        // ------------------------------------------------------------ Pages
        static readonly Dictionary<string, string> PageGlyphs = new Dictionary<string, string>
            { { "home", "" }, { "media", "" }, { "ai", "" }, { "timer", "" }, { "todo", "" }, { "shelf", "" }, { "system", "" }, { "games", "" } };
        static readonly Dictionary<string, string> PageNotes = new Dictionary<string, string>
            { { "home", "Always on" }, { "media", "Only while something is playing" } };

        FrameworkElement BuildPages()
        {
            var p = Page("Pages", "The pages you swipe through when the island is open: pick which ones, their order, and where it opens.");
            p.Children.Add(Header("Opening"));
            p.Children.Add(Card(
                Seg("Open to", "Smart shows a running timer or the playing song first, otherwise Home.", new[] { "Smart", "Home", "Last page" }, S.OpenTo, 92, i => S.OpenTo = i),
                Tog("Loop pages", "After the last page comes the first.", S.LoopPages, v => S.LoopPages = v)));

            p.Children.Add(Header("Order & visibility"));
            var order = IslandWindow.OrderedPages(true);
            var rows = new List<FrameworkElement>();
            for (int i = 0; i < order.Count; i++)
            {
                string k = order[i]; int idx = i;
                bool hidden = S.HiddenPages.Contains(k);
                var g = new Grid { Margin = new Thickness(14, 8, 12, 8), Tag = "§" + IslandWindow.PageLabel(k) + " page order show hide carousel" };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                string glyph; PageGlyphs.TryGetValue(k, out glyph);
                var ic = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = Theme.Surface, Child = K.Glyph(glyph ?? "", 13, hidden ? Theme.Text3 : Theme.Text), Margin = new Thickness(0, 0, 12, 0) };
                g.Children.Add(ic);
                var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                txt.Children.Add(K.TB(IslandWindow.PageLabel(k), 13.5, hidden ? Theme.Text3 : Theme.Text, FontWeights.Medium));
                string note; if (PageNotes.TryGetValue(k, out note)) { var nt = K.TB(note, 11, Theme.Text3, FontWeights.Normal); txt.Children.Add(nt); }
                Grid.SetColumn(txt, 1); g.Children.Add(txt);
                var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var up = K.IconBtn("", 28, 10, i == 0 ? Theme.Text3 : Theme.Text2, () => MovePage(order, idx, -1)); up.ToolTip = "Move up"; up.IsEnabled = i > 0; up.Opacity = i > 0 ? 1 : 0.35;
                var dn = K.IconBtn("", 28, 10, i == order.Count - 1 ? Theme.Text3 : Theme.Text2, () => MovePage(order, idx, 1)); dn.ToolTip = "Move down"; dn.IsEnabled = i < order.Count - 1; dn.Opacity = i < order.Count - 1 ? 1 : 0.35;
                ctl.Children.Add(up); ctl.Children.Add(dn);
                if (k != "home")
                {
                    var t = new Toggle(!hidden) { Margin = new Thickness(10, 0, 0, 0) };
                    t.ToolTip = hidden ? "Show this page" : "Hide this page";
                    t.Changed += v =>
                    {
                        if (v) S.HiddenPages.Remove(k); else if (!S.HiddenPages.Contains(k)) S.HiddenPages.Add(k);
                        Changed();
                        var tm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) }; tm.Tick += delegate { tm.Stop(); Rebuild(); }; tm.Start();
                    };
                    ctl.Children.Add(t);
                }
                else ctl.Children.Add(new Border { Width = 56 });
                Grid.SetColumn(ctl, 2); g.Children.Add(ctl);
                rows.Add(g);
            }
            p.Children.Add(Card(rows.ToArray()));
            var reset = K.GlyphPill("", "Default order, show all", Theme.Raised, Theme.Text, 30, () => { S.PageOrder.Clear(); S.HiddenPages.Clear(); Changed(); Rebuild(); });
            reset.HorizontalAlignment = HorizontalAlignment.Left; reset.Margin = new Thickness(0, 10, 0, 0);
            p.Children.Add(reset);

            p.Children.Add(Header("Home shortcuts"));
            var cur = IslandWindow.HomeShortcutIds();
            var chips = new WrapPanel { Margin = new Thickness(16, 12, 16, 8) };
            foreach (var c in IslandWindow.HomeShortcutCatalog)
            {
                string id = c[0];
                int pos = cur.IndexOf(id);
                bool on = pos >= 0;
                var sp2 = new StackPanel { Orientation = Orientation.Horizontal };
                var gl = K.Glyph(c[1], 11, on ? Theme.OnAccent : Theme.Text2);
                if (id == "caffeine") gl.FontFamily = new FontFamily("Segoe UI Symbol");
                sp2.Children.Add(gl);
                var lt = K.TB(c[2], 12, on ? Theme.OnAccent : Theme.Text2, FontWeights.SemiBold); lt.Margin = new Thickness(6, 0, 0, 0);
                sp2.Children.Add(lt);
                if (on) { var n = K.TB((pos + 1).ToString(), 10, Theme.OnAccent, FontWeights.Bold); n.Margin = new Thickness(7, 0, 0, 0); n.Opacity = 0.7; sp2.Children.Add(n); }
                var chip = new Border { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(11, 0, 12, 0), Margin = new Thickness(0, 0, 6, 6), Background = on ? (Brush)Theme.AccentFill : Theme.Surface, Child = sp2, Cursor = Cursors.Hand };
                K.Pressable(chip, () =>
                {
                    var list = IslandWindow.HomeShortcutIds();
                    if (list.Contains(id)) { if (list.Count == 1) return; list.Remove(id); }
                    else { if (list.Count >= IslandWindow.MaxHomeShortcuts) return; list.Add(id); }
                    S.HomeTiles = list; Changed(); Rebuild();
                }, false);
                chips.Children.Add(chip);
            }
            var chipsBox = new StackPanel { Tag = "§Home shortcuts tiles quick actions" };
            var hint = K.TB("Up to " + IslandWindow.MaxHomeShortcuts + ", shown in the order you pick them. Pages are always in the dock below Home.", 11.5, Theme.Text2, FontWeights.Normal);
            hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new Thickness(16, 12, 16, 0);
            chipsBox.Children.Add(hint); chipsBox.Children.Add(chips);
            p.Children.Add(Card(chipsBox, Row("Reset shortcuts", null, K.GlyphPill("\uE72C", "Default", Theme.Surface, Theme.Text, 30, () => { S.HomeTiles = new List<string>(); Changed(); Rebuild(); }))));

            p.Children.Add(Header("Media page"));
            p.Children.Add(Card(
                Seg("Waveform", "Reduced ≈ 8 fps, lightest on CPU.", new[] { "Full", "Reduced", "Off" }, S.Waveform, 76, i => S.Waveform = i)));
            return p;
        }

        void MovePage(List<string> order, int idx, int dir)
        {
            int j = idx + dir;
            if (j < 0 || j >= order.Count) return;
            var o = new List<string>(order);
            var tmp = o[idx]; o[idx] = o[j]; o[j] = tmp;
            S.PageOrder = o;
            Changed(); Rebuild();
        }

        FrameworkElement BuildTools()
        {
            var p = Page("Timer, To-do & Shelf", "Small tools that live on the island's pages.");
            p.Children.Add(Header("Timer"));
            p.Children.Add(Card(
                Txt("Presets (minutes)", "Comma separated, up to six.", S.TimerPresets, 180, "1, 5, 10, 25", v => S.TimerPresets = v),
                Tog("Sound when done", null, S.TimerSound, v => S.TimerSound = v)));
            p.Children.Add(Header("To-do"));
            p.Children.Add(Card(
                Sld("Max to-dos", null, 5, 50, 1, S.TodoMax, v => v.ToString("0"), v => S.TodoMax = (int)v, 20)));
            p.Children.Add(Header("Shelf"));
            p.Children.Add(Card(
                Seg("Dragging out", "Copy leaves the original where it is; Move takes it out of its folder.", new[] { "Copy", "Move" }, S.ShelfDragMode == "move" ? 1 : 0, 78, i => S.ShelfDragMode = i == 1 ? "move" : "copy"),
                Tog("Keep files after dragging out", "Off: a file leaves the shelf once it's dropped somewhere.", S.ShelfKeepAfterDrag, v => S.ShelfKeepAfterDrag = v),
                Sld("Max shelf items", null, 5, 60, 1, S.ShelfMax, v => v.ToString("0"), v => S.ShelfMax = (int)v, 30)));
            return p;
        }

        FrameworkElement BuildAi()
        {
            var p = Page("AI chat", "Claude, Gemini and GPT: sign in with your browser (official CLIs) or use an API key.");
            p.Children.Add(Header("Defaults"));
            int pi = Math.Max(0, Array.IndexOf(AiService.Providers, S.AiProvider));
            int mi = Math.Max(0, Array.IndexOf(new[] { "auto", "cli", "key" }, S.AiMethod));
            int ei = Math.Max(0, Array.IndexOf(new[] { "low", "medium", "high" }, S.Effort));
            p.Children.Add(Card(
                Seg("Provider", null, new[] { "Claude", "Gemini", "GPT" }, pi, 78, i => S.AiProvider = AiService.Providers[i]),
                Seg("Method", "Auto uses the browser sign-in when available, otherwise the key.", new[] { "Auto", "Browser", "Key" }, mi, 78, i => S.AiMethod = new[] { "auto", "cli", "key" }[i]),
                Seg("Claude effort (API key)", "Low is the fastest reply.", new[] { "Low", "Medium", "High" }, ei, 72, i => S.Effort = new[] { "low", "medium", "high" }[i])));

            foreach (var prov in AiService.Providers)
            {
                string pv = prov;
                var st = Svc.Ai.GetStatus(pv);
                p.Children.Add(Header(AiService.Label(pv)));
                var status = K.TB((st.Cli ? AiService.CliName(pv) + " CLI installed" : AiService.CliName(pv) + " CLI not installed") + " · " +
                    (st.SignedIn ? "signed in" : "not signed in") + " · " + (st.Key ? "API key saved" : "no API key"), 12, Theme.Text2, FontWeights.Normal);
                status.TextWrapping = TextWrapping.Wrap;
                var btns = new StackPanel { Orientation = Orientation.Horizontal };
                btns.Children.Add(K.GlyphPill("", st.SignedIn ? "Sign in again" : (st.Cli ? "Sign in with browser" : "Install & sign in"), Theme.AccentFill, Theme.OnAccent, 30, () => Svc.Ai.SignIn(pv)));
                if (st.Cli)
                {
                    var upd = K.PillBtn("Update CLI", Theme.Surface, Theme.Text, 30, () => Svc.Ai.UpdateCli(pv)); upd.Margin = new Thickness(8, 0, 0, 0);
                    btns.Children.Add(upd);
                }
                var statusRow = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
                statusRow.Children.Add(status);
                btns.Margin = new Thickness(0, 10, 0, 0);
                statusRow.Children.Add(btns);
                p.Children.Add(Card(statusRow, Row(AiService.Label(pv) + " API key", AiService.KeyHint(pv), KeyBox(S.AiKeys.ContainsKey(pv) ? S.AiKeys[pv] : "", v =>
                {
                    if (v.Trim().Length == 0) S.AiKeys.Remove(pv); else S.AiKeys[pv] = Store.Protect(v);
                    Store.Save();
                }))));
            }

            var sys = new TextBox { Text = S.SystemPrompt, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, VerticalContentAlignment = VerticalAlignment.Top };
            var sysBox = K.Field(sys, "Instructions for the AI", 0, Theme.Surface);
            sys.TextChanged += delegate { S.SystemPrompt = sys.Text; Store.Save(); };
            var wrapSys = new StackPanel { Margin = new Thickness(16, 12, 16, 14), Tag = "§System instructions prompt personality rules" };
            wrapSys.Children.Add(K.TB("System instructions", 13.5, Theme.Text, FontWeights.Medium));
            var d = K.TB("Personality and rules for every provider.", 11.5, Theme.Text2, FontWeights.Normal); d.Margin = new Thickness(0, 2, 0, 8);
            wrapSys.Children.Add(d); wrapSys.Children.Add(sysBox);
            var resetPrompt = K.GlyphPill("", "Reset", Theme.Surface, Theme.Text, 28, () => { sys.Text = Store.DefaultPrompt; });
            resetPrompt.HorizontalAlignment = HorizontalAlignment.Left; resetPrompt.Margin = new Thickness(0, 8, 0, 0);
            wrapSys.Children.Add(resetPrompt);
            p.Children.Add(Header("Instructions"));
            p.Children.Add(Card(wrapSys));
            var refresh = K.PillBtn("Check status again", Theme.Raised, Theme.Text, 32, () => Svc.Ai.RefreshStatus(() => Dispatcher.BeginInvoke(new Action(() => { if (IsPage("ai")) Rebuild(); }))));
            refresh.HorizontalAlignment = HorizontalAlignment.Left; refresh.Margin = new Thickness(0, 14, 0, 0);
            p.Children.Add(refresh);
            return p;
        }

        static Border KeyBox(string enc, Action<string> set)
        {
            var pb = new PasswordBox { Password = Store.Unprotect(enc), Width = 230 };
            pb.Background = Brushes.Transparent; pb.BorderThickness = new Thickness(0); pb.Foreground = Theme.Text; pb.CaretBrush = Theme.Text; pb.FocusVisualStyle = null; pb.VerticalContentAlignment = VerticalAlignment.Center;
            pb.PasswordChanged += delegate { set(pb.Password); };
            return new Border { CornerRadius = new CornerRadius(10), Background = Theme.Surface, Padding = new Thickness(8, 7, 8, 7), Child = pb };
        }

        FrameworkElement BuildLauncher()
        {
            var p = Page("Launcher & Wallpapers", "Find apps, calculate, open sites, and a wallpaper gallery inside the island.");
            int si = Math.Max(0, Array.IndexOf(new[] { "google", "duckduckgo", "bing" }, S.SearchEngine));
            p.Children.Add(Header("Launcher"));
            p.Children.Add(Card(
                Seg("Web search (?term)", null, new[] { "Google", "DuckDuckGo", "Bing" }, si, 92, i => S.SearchEngine = new[] { "google", "duckduckgo", "bing" }[i]),
                Row("App index", Svc.Apps.Count + " apps indexed.", K.PillBtn("Re-index", Theme.Surface, Theme.Text, 30, () => Svc.Apps.Reindex(() => Dispatcher.BeginInvoke(new Action(() => { if (IsPage("launcher")) Rebuild(); }))))),
                Row("Reset ranking", "Apps you open often show up first.", K.GlyphPill("", "Reset", Theme.Surface, Theme.Text, 30, () => { S.LaunchCounts.Clear(); Store.Save(); }))));
            var tb = new TextBox { Text = S.WallpaperDir };
            var f = K.Field(tb, "Wallpaper folder", 270, Theme.Surface);
            tb.TextChanged += delegate { S.WallpaperDir = tb.Text; Store.Save(); };
            var browse = K.PillBtn("Browse…", Theme.Surface, Theme.Text, 30, () =>
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = S.WallpaperDir, Description = "Wallpaper folder" })
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) tb.Text = dlg.SelectedPath;
            });
            browse.Margin = new Thickness(8, 0, 0, 0);
            var fr = new StackPanel { Orientation = Orientation.Horizontal }; fr.Children.Add(f); fr.Children.Add(browse);
            p.Children.Add(Header("Wallpapers"));
            p.Children.Add(Card(
                Row("Folder", "Local wallpapers, and where online downloads are saved.", fr),
                Seg("Fit", null, new[] { "Fill", "Fit", "Center", "Span" }, S.WallpaperFit, 64, i => S.WallpaperFit = i)));

            p.Children.Add(Header("Online (Wallhaven)"));
            var cats = new StackPanel { Orientation = Orientation.Horizontal };
            string[] catNames = { "General", "Anime", "People" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                bool on = S.WallhavenCategories[i] == '1';
                var lbl = K.TB(catNames[i], 12, on ? Theme.OnAccent : Theme.Text2, FontWeights.SemiBold);
                var chip = new Border { Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 6, 0), Child = lbl, Cursor = Cursors.Hand, Background = on ? (Brush)Theme.AccentFill : Theme.Surface };
                K.Pressable(chip, () =>
                {
                    var c = S.WallhavenCategories.ToCharArray(); c[idx] = c[idx] == '1' ? '0' : '1';
                    if (new string(c) == "000") return;
                    S.WallhavenCategories = new string(c); Store.Save(); Rebuild();
                }, false);
                cats.Children.Add(chip);
            }
            p.Children.Add(Card(
                Row("Categories", null, cats),
                Seg("Content", "NSFW needs your API key below, and NSFW switched on in your Wallhaven account. Mix freely from the chips in the wallpaper picker.", new[] { "SFW", "+ Sketchy", "All", "NSFW only" }, Math.Max(0, Array.IndexOf(new[] { "100", "110", "111", "001" }, OnlineWallpapers.PurityBits())), 84, i => { S.WallhavenPurity = i; S.WallhavenPurityBits = OnlineWallpapers.Purity(i); Rebuild(); }),
                OnlineWallpapers.PurityBits()[2] == '1' && (Store.Unprotect(S.WallhavenKeyEnc) ?? "").Trim().Length == 0 ? Row("API key missing", "Wallhaven only returns NSFW to a signed-in account: paste your key below.", K.PillBtn("Get my key", Theme.Surface, Theme.Text, 30, () => AppIndex.Open("https://wallhaven.cc/settings/account"))) : null,
                OnlineWallpapers.PurityBits()[2] == '1' ? Tog("Blur NSFW thumbnails", "They sharpen while hovered. Handy when sharing your screen.", S.WallhavenBlurNsfw, v => S.WallhavenBlurNsfw = v) : null,
                Txt("Minimum resolution", "Empty = any size.", S.WallhavenAtleast, 140, "1920x1080", v => S.WallhavenAtleast = v.Trim()),
                Row("Wallhaven API key (optional)", "From wallhaven.cc → Settings → Account.", KeyBox(S.WallhavenKeyEnc, v => { S.WallhavenKeyEnc = Store.Protect(v); Store.Save(); }))));
            var note = K.TB("Konachan results are limited to rating:safe. Unsplash photos come via picsum.photos, Bing shows the images of the day in 4K.", 11.5, Theme.Text2, FontWeights.Normal);
            note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(4, 10, 0, 0);
            p.Children.Add(note);
            return p;
        }

        // ------------------------------------------------------------ System
        FrameworkElement BuildShortcuts()
        {
            var p = Page("Shortcuts", "Global hotkeys. Click a key, then press the new combination (Esc cancels, Backspace clears).");
            var rows = new List<FrameworkElement>();
            foreach (var kv in Store.HotkeyLabels)
            {
                string id = kv.Key;
                string cur = S.Hotkeys.ContainsKey(id) ? S.Hotkeys[id] : "";
                bool failed = Svc.Hotkeys.Failed.Contains(id);
                bool rec = recordingKey == id;
                var t = K.TB(rec ? "Press a combination…" : (cur.Length == 0 ? "—" : cur), 12.5, rec ? (Brush)Theme.Accent : failed ? Theme.Red : Theme.Text, FontWeights.SemiBold);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                var b = new Border { MinWidth = 160, Height = 30, CornerRadius = new CornerRadius(8), Background = Theme.Surface, Child = t, Cursor = Cursors.Hand, Padding = new Thickness(12, 0, 12, 0), BorderThickness = new Thickness(1), BorderBrush = rec ? (Brush)Theme.Accent : Brushes.Transparent };
                b.MouseLeftButtonDown += (s, e) => e.Handled = true;
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; recordingKey = id; Rebuild(); };
                var reset = K.IconBtn("", 30, 12, Theme.Text2, () => { S.Hotkeys[id] = Store.DefaultHotkeys[id]; Changed(); Svc.Hotkeys.Apply(); Rebuild(); });
                reset.ToolTip = "Reset to " + Store.DefaultHotkeys[id];
                var sp = new StackPanel { Orientation = Orientation.Horizontal }; sp.Children.Add(b); sp.Children.Add(reset);
                rows.Add(Row(kv.Value, failed ? "Taken by another app, pick another combination." : null, sp));
            }
            p.Children.Add(Card(rows.ToArray()));
            var all = K.GlyphPill("", "Reset all shortcuts", Theme.Raised, Theme.Text, 32, () =>
            {
                foreach (var kv in Store.DefaultHotkeys) S.Hotkeys[kv.Key] = kv.Value;
                Changed(); Svc.Hotkeys.Apply(); Rebuild();
            });
            all.HorizontalAlignment = HorizontalAlignment.Left; all.Margin = new Thickness(0, 12, 0, 0);
            p.Children.Add(all);
            var lin = Card(
                TogR("Linux-style Win keys", "Win+1…9 workspaces (virtual desktops, made on demand) · Win+Shift+1…9 send the window there · Win+Enter terminal · Win+F files · Win+B browser · Win+Q close window · Win+Space or Win+/ launcher. Overrides the Windows defaults for those keys only while on; nothing in Windows is changed, so turning it off gives them straight back.",
                    S.LinuxWinKeys, v => { S.LinuxWinKeys = v; Svc.WinKeys.Apply(); }),
                S.LinuxWinKeys ? TogR("Follow the moved window", "Win+Shift+N also switches to workspace N.", S.WinMoveFollow, v => S.WinMoveFollow = v) : null,
                S.LinuxWinKeys ? Txt("Terminal", "Win+Enter", S.WinTerminal, 220, "wt.exe", v => S.WinTerminal = v) : null,
                S.LinuxWinKeys ? Txt("Files", "Win+F", S.WinFiles, 220, "explorer.exe", v => S.WinFiles = v) : null,
                S.LinuxWinKeys ? Txt("Browser", "Win+B. Empty = your default browser.", S.WinBrowser, 220, "default browser", v => S.WinBrowser = v) : null);
            lin.Margin = new Thickness(0, 16, 0, 0);
            p.Children.Add(lin);
            var keys = K.TB("Inside the island: ← → change page · 1–9 jump to a page · Space play/pause or pause the timer · Esc closes. Middle-click plays/pauses, scroll changes volume.", 12, Theme.Text2, FontWeights.Normal);
            keys.TextWrapping = TextWrapping.Wrap; keys.Margin = new Thickness(4, 14, 0, 0);
            p.Children.Add(keys);
            return p;
        }

        FrameworkElement BuildAbout()
        {
            var p = Page("About", "Dynamic Island for Windows. Native WPF, no dependencies.");
            p.Children.Add(BuildUpdates());
            Func<bool, string> ok = b => b ? "active" : "not available";
            string repoUrl = "https://github.com/" + AppInfo.Repo;
            p.Children.Add(Card(Row("GitHub", "github.com/" + AppInfo.Repo + " · source code, issues and releases.",
                K.GlyphPill("", "Open on GitHub", Theme.Surface, Theme.Text, 30, () => AppIndex.Open(repoUrl)))));
            p.Children.Add(Card(
                Row("Volume (CoreAudio)", ok(Svc.Audio.Available), null),
                Row("Brightness (WMI)", ok(Svc.Brightness.Available), null),
                Row("Media (SMTC)", ok(isl.MediaAvailable), null),
                Row("Bluetooth", ok(Svc.Bt.Available), null),
                Row("Apps indexed", Svc.Apps.Count.ToString(), null),
                Row("Data folder", Store.Dir, K.PillBtn("Open", Theme.Surface, Theme.Text, 30, () => { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + Store.Dir + "\""); } catch { } })),
                Row("Setup wizard", "Walk through the first-run setup again (install, look, pages…).", K.PillBtn("Run setup", Theme.Surface, Theme.Text, 30, () => { AnimatedClose(); isl.OpenSetup(); })),
                Row("Restart", "Close and start the app again.", K.PillBtn("Restart", Theme.Surface, Theme.Text, 30, () => isl.Restart()))));
            return p;
        }

        // Version + updater card. The status line and button are refreshed in place while it works.
        FrameworkElement BuildUpdates()
        {
            var row = Row("Version " + AppInfo.Version, "Updates come from github.com/" + AppInfo.Repo + ".", null);
            var desc = ((StackPanel)row.Children[0]).Children.OfType<TextBlock>().Last();
            var host = new Border { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(host, 1); row.Children.Add(host);
            var notes = K.PillBtn("What's new", Theme.Surface, Theme.Text, 30, () => AppIndex.Open(Updater.PageUrl));
            var notesRow = Row("Release notes", null, notes);

            Action refresh = () =>
            {
                Action check = () => Updater.CheckAsync(null);
                string text; FrameworkElement btn = null;
                switch (Updater.State)
                {
                    case Updater.St.Checking: text = "Checking GitHub…"; break;
                    case Updater.St.UpToDate: text = "You're on the latest version."; btn = K.PillBtn("Check again", Theme.Surface, Theme.Text, 30, check); break;
                    case Updater.St.Available:
                        text = "Version " + Updater.LatestVersion + " is available." + (Updater.AssetUrl.Length == 0 ? " The release has no download attached yet." : "");
                        if (Updater.AssetUrl.Length > 0)
                            btn = K.PillBtn("Update to " + Updater.LatestVersion, Theme.Accent, Brushes.White, 30, () =>
                                Updater.InstallAsync(() => Dispatcher.BeginInvoke(new Action(() => isl.Restart()))));
                        break;
                    case Updater.St.Downloading: text = "Downloading v" + Updater.LatestVersion + "… " + (int)(Updater.Progress * 100) + "% · to " + Updater.WorkDir + " (removed after installing)"; break;
                    case Updater.St.Installing: text = "Installing, the island will restart…"; break;
                    case Updater.St.Error: text = Updater.Error; btn = K.PillBtn("Try again", Theme.Surface, Theme.Text, 30, check); break;
                    default: text = "Updates come from github.com/" + AppInfo.Repo + "."; btn = K.PillBtn("Check for updates", Theme.Surface, Theme.Text, 30, check); break;
                }
                desc.Text = text;
                host.Child = btn;
                notesRow.Visibility = Updater.State == Updater.St.Available && Updater.PageUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            Updater.OnChange = () => Dispatcher.BeginInvoke(new Action(() => { if (IsPage("about")) refresh(); }));
            refresh();
            // opening About checks on its own if the last check is stale
            if (!Updater.Busy && (DateTime.Now - Updater.LastCheck).TotalMinutes > 10) Updater.CheckAsync(null);

            return Card(row, notesRow,
                Tog("Check for updates at start", "Shows a notice on the island when a new version is out.", S.AutoCheckUpdates, v => S.AutoCheckUpdates = v));
        }
    }
}

