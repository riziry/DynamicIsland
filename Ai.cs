// AI chat backend: Claude · Gemini · GPT, each through its official CLI (browser sign-in:
// claude · agy · codex) or over HTTP with an API key. One shared conversation.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DynIsland
{
    public class AiMessage
    {
        public string Role = "user";        // user | assistant
        public string Content = "";
        public string Provider = "";
        public string Model = "";
        public string Method = "";
        public List<Dictionary<string, object>> Blocks;   // Claude API content blocks (replayed unchanged)
        public List<string> Images;                       // attached photos (files in AiService.ImgDir)

        public bool HasImages { get { return Images != null && Images.Any(File.Exists); } }
        public List<string> LiveImages() { return Images == null ? new List<string>() : Images.Where(File.Exists).ToList(); }
    }

    public class AiConversation
    {
        public List<AiMessage> Messages = new List<AiMessage>();
        public Dictionary<string, string> Sessions = new Dictionary<string, string>();
    }

    class AiStatus { public bool Cli, SignedIn, Key; }

    class UsageWindow { public string Label, Detail; public double Percent = -1; public double ResetsAt; }
    class UsageInfo { public string Plan = "", Note = "", Url = ""; public List<UsageWindow> Windows = new List<UsageWindow>(); public double At; }

    class AiService
    {
        public static readonly string[] Providers = { "claude", "gemini", "gpt" };
        static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        static readonly string Work = Path.Combine(Store.Dir, "ai-work");
        static readonly string ChatPath = Path.Combine(Store.Dir, "ai-chat.json");
        static readonly string AgyMarker = Path.Combine(Home, ".gemini", "antigravity-cli", ".island-signed-in");

        public AiConversation Chat = new AiConversation();
        readonly Dictionary<string, AiStatus> status = new Dictionary<string, AiStatus>();
        readonly Dictionary<string, UsageInfo> keyLimits = new Dictionary<string, UsageInfo>();
        UsageInfo claudePlanCache; DateTime claudePlanAt = DateTime.MinValue, claudePlanTried = DateTime.MinValue;
        public event Action StatusChanged;
        public bool Busy { get; private set; }
        volatile Process proc; volatile HttpWebRequest req; volatile bool cancelled;
        Dispatcher ui;

        public static string Label(string p) { return p == "claude" ? "Claude" : p == "gemini" ? "Gemini" : "GPT"; }
        public static string CliName(string p) { return p == "claude" ? "claude" : p == "gemini" ? "agy" : "codex"; }
        public static string KeyHint(string p)
        {
            return p == "claude" ? "console.anthropic.com" : p == "gemini" ? "aistudio.google.com" : "platform.openai.com";
        }
        public static Color Brand(string p)
        {
            return p == "claude" ? Theme.Hex("#d97757") : p == "gemini" ? Theme.Hex("#7b9cff") : Theme.Hex("#10a37f");
        }
        public static readonly Dictionary<string, string[][]> StaticModels = new Dictionary<string, string[][]>
        {
            { "claude", new[] { new[] { "claude-opus-5-5", "Opus 5.5" }, new[] { "claude-sonnet-5-5", "Sonnet 5.5" }, new[] { "claude-haiku-5-5", "Haiku 5.5" }, new[] { "claude-fable-5-1", "Fable 5.1" } } },
            { "gemini", new[] { new[] { "gemini-3.8-flash", "Gemini 3.8 Flash" }, new[] { "gemini-3.1-pro-preview", "Gemini 3.1 Pro" }, new[] { "gemini-2.5-pro", "Gemini 2.5 Pro" }, new[] { "gemini-2.5-flash", "Gemini 2.5 Flash" } } },
            { "gpt", new[] { new[] { "gpt-5.6-terra", "GPT-5.6 Terra" }, new[] { "gpt-5.6-luna", "GPT-5.6 Luna" } } },
        };

        public void Init(Dispatcher d)
        {
            ui = d;
            try { if (File.Exists(ChatPath)) Chat = Store.Js().Deserialize<AiConversation>(File.ReadAllText(ChatPath, Encoding.UTF8)) ?? new AiConversation(); } catch { }
            if (Chat.Messages == null) Chat.Messages = new List<AiMessage>();
            if (Chat.Sessions == null) Chat.Sessions = new Dictionary<string, string>();
            foreach (var p in Providers) status[p] = new AiStatus { Key = KeyFor(p).Length > 0 };
            CleanImages(null);
            RefreshStatus(null);
        }

        public void SaveChat()
        {
            while (Chat.Messages.Count > 200) Chat.Messages.RemoveAt(0);
            Store.WriteFile(ChatPath, Store.Js().Serialize(Chat));
        }

        public void NewChat() { Cancel(); Chat = new AiConversation(); SaveChat(); CleanImages(null); }

        // ------------------------------------------------------------- photos
        public static readonly string ImgDir = Path.Combine(Store.Dir, "ai-images");
        const int MaxImageEdge = 1600;   // big enough to read text in screenshots, small enough for every API

        /// Normalises any picture (pasted, imported, dropped) into a PNG/JPEG in ImgDir, downscaled so the
        /// long edge is at most MaxImageEdge. Returns the file path, or null if it isn't a usable image.
        public static string SaveImage(BitmapSource src, bool opaque)
        {
            if (src == null || src.PixelWidth < 1 || src.PixelHeight < 1) return null;
            BitmapSource b = src;
            double sc = Math.Min(1.0, (double)MaxImageEdge / Math.Max(b.PixelWidth, b.PixelHeight));
            if (sc < 1) b = new TransformedBitmap(b, new ScaleTransform(sc, sc));
            // clipboard DIBs often carry a garbage (all-zero) alpha channel: drop it
            if (opaque || b.Format == PixelFormats.Bgr32 || b.Format == PixelFormats.Bgr24) b = new FormatConvertedBitmap(b, PixelFormats.Bgr32, null, 0);
            Directory.CreateDirectory(ImgDir);
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            byte[] png;
            using (var ms = new MemoryStream()) { var e = new PngBitmapEncoder(); e.Frames.Add(BitmapFrame.Create(b)); e.Save(ms); png = ms.ToArray(); }
            string path;
            // photos compress far better as JPEG; keep PNG for screenshots / transparency
            if (png.Length > 1500000)
            {
                var flat = new FormatConvertedBitmap(b, PixelFormats.Bgr32, null, 0);
                using (var ms = new MemoryStream()) { var e = new JpegBitmapEncoder { QualityLevel = 88 }; e.Frames.Add(BitmapFrame.Create(flat)); e.Save(ms); path = Path.Combine(ImgDir, id + ".jpg"); File.WriteAllBytes(path, ms.ToArray()); }
            }
            else { path = Path.Combine(ImgDir, id + ".png"); File.WriteAllBytes(path, png); }
            return path;
        }

        public static string SaveImageFile(string file)
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.UriSource = new Uri(file); bi.EndInit(); bi.Freeze();
            return SaveImage(bi, false);
        }

        public static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic" };
        public static bool IsImageFile(string f) { return ImageExts.Contains((Path.GetExtension(f) ?? "").ToLowerInvariant()); }

        static string Mime(string path) { return path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/png"; }
        static string B64(string path) { return Convert.ToBase64String(File.ReadAllBytes(path)); }

        /// Deletes photos no message (and nothing in keep) refers to.
        public void CleanImages(IEnumerable<string> keep)
        {
            try
            {
                if (!Directory.Exists(ImgDir)) return;
                var used = new HashSet<string>(Chat.Messages.Where(m => m.Images != null).SelectMany(m => m.Images), StringComparer.OrdinalIgnoreCase);
                if (keep != null) foreach (var k in keep) used.Add(k);
                foreach (var f in Directory.GetFiles(ImgDir)) if (!used.Contains(f)) { try { File.Delete(f); } catch { } }
            }
            catch { }
        }

        // Images are only re-sent for the most recent user turns, to keep requests small.
        static HashSet<AiMessage> ImageTurns(List<AiMessage> history)
        {
            return new HashSet<AiMessage>(history.Where(m => m.Role == "user" && m.HasImages).Reverse().Take(4));
        }
        static string TextOr(AiMessage m, string fallback) { return string.IsNullOrWhiteSpace(m.Content) ? fallback : m.Content; }
        const string ImageOnlyPrompt = "What's in this image?";

        // ------------------------------------------------------------- status
        public AiStatus GetStatus(string p)
        {
            AiStatus s;
            if (!status.TryGetValue(p, out s)) s = new AiStatus();
            s.Key = KeyFor(p).Length > 0;
            return s;
        }

        public void RefreshStatus(Action done)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                foreach (var p in Providers)
                {
                    var st = new AiStatus();
                    string exe = Which(p);
                    st.Cli = exe != null;
                    st.SignedIn = st.Cli && CliSignedIn(p, exe);
                    st.Key = KeyFor(p).Length > 0;
                    lock (status) status[p] = st;
                }
                ui.BeginInvoke(new Action(() => { if (StatusChanged != null) StatusChanged(); if (done != null) done(); }));
            });
        }

        public static string KeyFor(string p)
        {
            string env = Environment.GetEnvironmentVariable(p == "claude" ? "ANTHROPIC_API_KEY" : p == "gemini" ? "GEMINI_API_KEY" : "OPENAI_API_KEY");
            if (!string.IsNullOrEmpty(env)) return env;
            string enc;
            return Store.S.AiKeys.TryGetValue(p, out enc) ? Store.Unprotect(enc) : "";
        }

        /// Resolved backend for a provider: "cli" or "key".
        public string MethodFor(string p)
        {
            string m = Store.S.AiMethod;
            if (m == "cli" || m == "key") return m;
            var st = GetStatus(p);
            if (st.SignedIn) return "cli";
            if (st.Key) return "key";
            return "cli";
        }

        public string ModelFor(string p)
        {
            string m;
            if (Store.S.AiModels.TryGetValue(p, out m)) return m;
            // Signed in through a CLI: let the CLI pick the plan's default model unless one was chosen.
            if (MethodFor(p) == "cli" && (p == "gemini" || p == "gpt")) return "";
            return StaticModels[p][0][0];
        }

        // ------------------------------------------------------------- CLI discovery
        // Installs done while the app runs (Node via winget, codex via npm) change PATH in the
        // registry only; pick those up so freshly installed CLIs are found without a restart.
        static DateTime pathAt = DateTime.MinValue;
        static void RefreshPath()
        {
            if ((DateTime.Now - pathAt).TotalSeconds < 3) return;
            pathAt = DateTime.Now;
            try
            {
                string m = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
                string u = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
                var parts = new List<string>();
                foreach (var p in (m + ";" + u + ";" + (Environment.GetEnvironmentVariable("Path") ?? "")).Split(';'))
                {
                    string x = Environment.ExpandEnvironmentVariables(p.Trim());
                    if (x.Length > 0 && !parts.Contains(x, StringComparer.OrdinalIgnoreCase)) parts.Add(x);
                }
                Environment.SetEnvironmentVariable("Path", string.Join(";", parts));
            }
            catch { }
        }

        static IEnumerable<string> ExtraDirs()
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs");
            yield return Path.Combine(Home, ".local", "bin");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
            yield return Path.Combine(Home, ".bun", "bin");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Antigravity", "bin");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin");   // standalone agy CLI installer
            yield return Path.Combine(Home, ".antigravity", "bin");
        }

        public static string Which(string provider)
        {
            RefreshPath();
            string name = CliName(provider);
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(d => d.Trim().Length > 0).ToList();
            dirs.AddRange(ExtraDirs());
            foreach (var d in dirs)
                foreach (var ext in new[] { ".exe", ".cmd", ".bat" })
                {
                    try { string f = Path.Combine(d.Trim().Trim('"'), name + ext); if (File.Exists(f)) return f; } catch { }
                }
            if (provider == "claude")
            {
                // Claude Code bundled with the VS Code / Cursor / Windsurf extension.
                foreach (var root in new[] { ".vscode", ".vscode-insiders", ".cursor", ".windsurf" })
                {
                    try
                    {
                        string ext = Path.Combine(Home, root, "extensions");
                        if (!Directory.Exists(ext)) continue;
                        var best = Directory.GetDirectories(ext, "anthropic.claude-code-*")
                            .Select(x => Path.Combine(x, "resources", "native-binary", "claude.exe")).Where(File.Exists)
                            .OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
                        if (best != null) return best;
                    }
                    catch { }
                }
            }
            return null;
        }

        static string WhichPlain(string name)
        {
            RefreshPath();
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Concat(ExtraDirs()))
                foreach (var ext in new[] { ".exe", ".cmd" })
                {
                    try { string f = Path.Combine(d.Trim().Trim('"'), name + ext); if (File.Exists(f)) return f; } catch { }
                }
            return null;
        }

        static bool CliSignedIn(string p, string exe)
        {
            try
            {
                if (p == "claude")
                {
                    string o = RunQuick(exe, new[] { "auth", "status" }, 12000);
                    var d = Store.Js().DeserializeObject(o) as Dictionary<string, object>;
                    return d != null && d.ContainsKey("loggedIn") && Convert.ToBoolean(d["loggedIn"]);
                }
                if (p == "gpt")
                {
                    string home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Home, ".codex");
                    string f = Path.Combine(home, "auth.json");
                    if (!File.Exists(f)) return false;
                    var d = Store.Js().DeserializeObject(File.ReadAllText(f)) as Dictionary<string, object>;
                    return d != null && ((d.ContainsKey("tokens") && d["tokens"] != null) || (d.ContainsKey("OPENAI_API_KEY") && d["OPENAI_API_KEY"] != null));
                }
                if (p == "gemini") return File.Exists(AgyMarker);
            }
            catch { }
            return false;
        }

        // Windows command-line quoting (CommandLineToArgvW rules).
        static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"', '\n', '\r' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in a)
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', bs); sb.Append(c); }
                bs = 0;
            }
            sb.Append('\\', bs * 2); sb.Append('"');
            return sb.ToString();
        }

        // npm .cmd shims are run as "node script.js" so the prompt never goes through cmd.exe.
        static ProcessStartInfo MakePsi(string exe, IList<string> args)
        {
            string file = exe; var list = new List<string>(args);
            if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                string dir = Path.GetDirectoryName(exe);
                var m = Regex.Match(File.ReadAllText(exe), @"%~?dp0%?\\([^""%\r\n]+?\.(?:js|cjs|mjs))", RegexOptions.IgnoreCase);
                if (!m.Success) throw new Exception("Can't run " + Path.GetFileName(exe) + " safely.");
                string script = Path.Combine(dir, m.Groups[1].Value);
                string node = File.Exists(Path.Combine(dir, "node.exe")) ? Path.Combine(dir, "node.exe") : WhichPlain("node");
                if (node == null) throw new Exception("Node.js not found.");
                file = node; list.Insert(0, script);
            }
            Directory.CreateDirectory(Work);
            return new ProcessStartInfo(file, string.Join(" ", list.Select(Quote)))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false), WorkingDirectory = Work
            };
        }

        static string RunQuick(string exe, string[] args, int ms)
        {
            var psi = MakePsi(exe, args);
            using (var p = Process.Start(psi))
            {
                p.StandardInput.Close();
                var t = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(ms)) { try { p.Kill(); } catch { } return ""; }
                return t.Result;
            }
        }

        // ------------------------------------------------------------- sign-in / update (in a console window)
        public void SignIn(string p)
        {
            string exe = Which(p);
            string script;
            if (p == "claude")
            {
                script = exe != null
                    ? "Write-Host 'Sign in to Claude - your browser opens; approve it there. A Pro/Max plan works.' -ForegroundColor Cyan; & " + PsQ(exe) + " auth login"
                    : "Write-Host 'Installing Claude Code first...' -ForegroundColor Cyan; irm https://claude.ai/install.ps1 | iex; & (Join-Path $env:USERPROFILE '.local\\bin\\claude.exe') auth login";
            }
            else if (p == "gpt")
            {
                string npm = WhichPlain("npm");
                if (exe != null) script = "Write-Host 'Sign in to ChatGPT (Codex CLI) - your browser opens.' -ForegroundColor Cyan; & " + PsQ(exe) + " login";
                else if (npm != null) script = "Write-Host 'Installing the Codex CLI first...' -ForegroundColor Cyan; & " + PsQ(npm) + " install -g @openai/codex@latest; " +
                    "$codex = Join-Path $env:APPDATA 'npm\\codex.cmd'; if (-not (Test-Path $codex)) { $codex = 'codex' }; Write-Host 'Sign in to ChatGPT - your browser opens.' -ForegroundColor Cyan; & $codex login";
                else script = "Write-Host 'The Codex CLI needs Node.js. Installing Node.js LTS with winget...' -ForegroundColor Yellow; winget install -e --id OpenJS.NodeJS.LTS --accept-source-agreements --accept-package-agreements; " +
                    "$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User'); " +
                    "Write-Host 'Installing the Codex CLI...' -ForegroundColor Cyan; npm install -g @openai/codex@latest; " +
                    "$codex = Join-Path $env:APPDATA 'npm\\codex.cmd'; if (-not (Test-Path $codex)) { $codex = 'codex' }; Write-Host 'Sign in to ChatGPT - your browser opens.' -ForegroundColor Cyan; & $codex login";
            }
            else
            {
                // agy only needs the standalone CLI (not the full Antigravity app): the official
                // installer drops agy.exe in %LOCALAPPDATA%\agy\bin, then the same window signs in.
                string login = "Write-Host 'Sign in to Gemini (Antigravity CLI).' -ForegroundColor Cyan; Write-Host 'Open the link below, sign in with Google, copy the authorization code, paste it here and press Enter (within 60 s).'; " +
                               "Remove-Item -Force -ErrorAction SilentlyContinue " + PsQ(AgyMarker) + "; Push-Location $env:TEMP; & $agy -p 'Reply with just: signed in'; $ok = $LASTEXITCODE -eq 0; Pop-Location; " +
                               "if ($ok) { New-Item -Force -ItemType File " + PsQ(AgyMarker) + " | Out-Null }";
                if (exe != null) script = "$agy = " + PsQ(exe) + "; " + login;
                else script = "Write-Host 'Installing the Antigravity CLI (agy) first - just the CLI, not the full app...' -ForegroundColor Cyan; " +
                              "try { irm https://antigravity.google/cli/install.ps1 | iex } catch { Write-Host $_ -ForegroundColor Red }; " +
                              "$ErrorActionPreference = 'Continue'; Set-StrictMode -Off; " +
                              "$agy = Join-Path $env:LOCALAPPDATA 'agy\\bin\\agy.exe'; " +
                              "if (Test-Path $agy) { Write-Host ''; " + login + " } else { Write-Host 'Install failed - check your connection and click Sign in again.' -ForegroundColor Red }";
            }
            RunConsole(script);
        }

        public void UpdateCli(string p)
        {
            string exe = Which(p);
            if (exe == null) { SignIn(p); return; }
            string script = p == "claude" ? "& " + PsQ(exe) + " update"
                : p == "gpt" ? "npm install -g @openai/codex@latest"
                : "& " + PsQ(exe) + " update";
            RunConsole("Write-Host 'Updating " + CliName(p) + "...' -ForegroundColor Cyan; " + script);
        }

        static string PsQ(string s) { return "'" + s.Replace("'", "''") + "'"; }

        void RunConsole(string script)
        {
            RefreshPath();
            string full = "$host.UI.RawUI.WindowTitle = 'Dynamic Island · AI'; " +
                "$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User') + ';' + $env:Path; " + script +
                "; Write-Host ''; if ($?) { Write-Host 'Done. You can close this window.' -ForegroundColor Green } else { Write-Host 'That did not work - see the messages above.' -ForegroundColor Red }; Read-Host 'Press Enter to close'";
            try
            {
                var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command " + Quote(full)) { UseShellExecute = true });
                ThreadPool.QueueUserWorkItem(_ => { try { p.WaitForExit(); } catch { } RefreshStatus(null); });
            }
            catch (Exception ex) { Log.Write("console: " + ex.Message); }
        }

        // ------------------------------------------------------------- models
        public void Models(string p, Action<List<string[]>> done)
        {
            string method = MethodFor(p);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<string[]> list = null;
                try
                {
                    string key = KeyFor(p);
                    if (method == "key" && key.Length > 0) list = FetchModels(p, key);
                    else if (method == "cli" && p == "gpt")
                    {
                        list = new List<string[]> { new[] { "", "Default" } };
                        list.AddRange(CodexModels() ?? new List<string[]>());
                        if (list.Count == 1) list.AddRange(StaticModels[p]);
                    }
                    else if (method == "cli" && p == "gemini")
                    {
                        list = new List<string[]> { new[] { "", "Default" } };
                        list.AddRange(AgyModels());
                    }
                }
                catch { }
                if (list == null || list.Count == 0) list = StaticModels[p].ToList();
                ui.BeginInvoke(new Action(() => done(list)));
            });
        }

        static List<string[]> CodexModels()
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Home, ".codex");
            string f = Path.Combine(home, "models_cache.json");
            if (!File.Exists(f)) return null;
            var d = Store.Js().DeserializeObject(File.ReadAllText(f)) as Dictionary<string, object>;
            var res = new List<string[]>();
            var arr = d != null && d.ContainsKey("models") ? d["models"] as object[] : null;
            if (arr == null) return null;
            foreach (Dictionary<string, object> m in arr.OfType<Dictionary<string, object>>())
            {
                if (Str(m, "visibility") != "list") continue;
                string slug = Str(m, "slug");
                res.Add(new[] { slug, Str(m, "display_name").Length > 0 ? Str(m, "display_name") : slug });
            }
            return res;
        }

        static List<string[]> AgyModels()
        {
            var res = new List<string[]>();
            string exe = Which("gemini");
            if (exe == null) return res;
            string o = RunQuick(exe, new[] { "models" }, 15000);
            foreach (var line in o.Split('\n'))
            {
                string w = line.Trim().TrimStart('*', '-', '•', ' ').Split(' ')[0];
                if (w.Length > 0 && char.IsLetterOrDigit(w[0]) && w.Contains("-") && !res.Any(x => x[0] == w)) res.Add(new[] { w, w });
            }
            return res;
        }

        static List<string[]> FetchModels(string p, string key)
        {
            var js = Store.Js();
            if (p == "claude")
            {
                var d = js.DeserializeObject(HttpGet("https://api.anthropic.com/v1/models?limit=100", new Dictionary<string, string> { { "x-api-key", key }, { "anthropic-version", "2026-10-21" } })) as Dictionary<string, object>;
                return ((object[])d["data"]).OfType<Dictionary<string, object>>().Select(m => new[] { Str(m, "id"), Str(m, "display_name").Length > 0 ? Str(m, "display_name") : Str(m, "id") }).ToList();
            }
            if (p == "gpt")
            {
                var d = js.DeserializeObject(HttpGet("https://api.openai.com/v1/models", new Dictionary<string, string> { { "Authorization", "Bearer " + key } })) as Dictionary<string, object>;
                var bad = new[] { "audio", "realtime", "transcribe", "tts", "image", "search", "embedding" };
                return ((object[])d["data"]).OfType<Dictionary<string, object>>().Select(m => Str(m, "id"))
                    .Where(id => (id.StartsWith("gpt-") || id.StartsWith("o1") || id.StartsWith("o3") || id.StartsWith("o4") || id.StartsWith("chatgpt")) && !bad.Any(id.Contains))
                    .OrderByDescending(x => x, StringComparer.Ordinal).Select(id => new[] { id, id }).ToList();
            }
            var g = js.DeserializeObject(HttpGet("https://generativelanguage.googleapis.com/v1beta/models?pageSize=200", new Dictionary<string, string> { { "x-goog-api-key", key } })) as Dictionary<string, object>;
            var res = new List<string[]>();
            foreach (var m in ((object[])g["models"]).OfType<Dictionary<string, object>>())
            {
                var methods = m.ContainsKey("supportedGenerationMethods") ? m["supportedGenerationMethods"] as object[] : null;
                string name = Str(m, "name");
                if (methods == null || !methods.Contains("generateContent") || !name.Contains("gemini")) continue;
                string id = name.Contains("/") ? name.Substring(name.IndexOf('/') + 1) : name;
                res.Add(new[] { id, Str(m, "displayName").Length > 0 ? Str(m, "displayName") : id });
            }
            return res;
        }

        static string HttpGet(string url, Dictionary<string, string> headers)
        {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.Timeout = 20000;
            foreach (var h in headers) r.Headers[h.Key] = h.Value;
            using (var resp = r.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v; return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString() : "";
        }

        // ------------------------------------------------------------- chat
        public void Cancel()
        {
            cancelled = true;
            var p = proc; if (p != null) { try { if (!p.HasExited) p.Kill(); } catch { } }
            var r = req; if (r != null) { try { r.Abort(); } catch { } }
        }

        /// Sends a user message. Callbacks run on the UI thread.
        /// onEvent("thinking"|"delta", text); onDone(error or null, action: "", "login", "key", "update").
        public void Send(string text, List<string> images, Action<string, string> onEvent, Action<string, string> onDone)
        {
            if (Busy) return;
            string p = Store.S.AiProvider, method = MethodFor(p), model = ModelFor(p);
            Busy = true; cancelled = false;
            var user = new AiMessage { Role = "user", Content = text, Provider = p, Model = model, Method = method, Images = images != null && images.Count > 0 ? new List<string>(images) : null };
            Chat.Messages.Add(user);
            var history = Chat.Messages.ToList();
            string session;
            Chat.Sessions.TryGetValue(p, out session);
            // A CLI session only continues if the previous assistant turn came from that same CLI session.
            var prevAsst = history.Take(history.Count - 1).LastOrDefault(m => m.Role == "assistant");
            if (method != "cli" || prevAsst == null || prevAsst.Provider != p || prevAsst.Method != "cli") session = "";
            string sys = (Store.S.SystemPrompt ?? "").Trim();
            string name = (Store.S.UserName ?? "").Trim();
            if (name.Length > 0) sys += "\nThe user's name is " + name + ".";

            var th = new Thread(() =>
            {
                var reply = new StringBuilder();
                string err = null, action = "", newSession = null;
                List<Dictionary<string, object>> blocks = null;
                Action<string> delta = t => { reply.Append(t); ui.BeginInvoke(new Action(() => onEvent("delta", t))); };
                Action thinking = () => ui.BeginInvoke(new Action(() => onEvent("thinking", "")));
                try
                {
                    if (method == "key")
                    {
                        string key = KeyFor(p);
                        if (key.Length == 0) { err = "No API key saved for " + Label(p) + "."; action = "key"; }
                        else if (p == "claude") blocks = ChatClaudeKey(key, model, sys, history, delta, thinking, ref err, ref action);
                        else if (p == "gpt") ChatGptKey(key, model, sys, history, delta, ref err, ref action);
                        else ChatGeminiKey(key, model, sys, history, delta, thinking, ref err, ref action);
                    }
                    else ChatCli(p, model, sys, history, session, delta, thinking, ref newSession, ref err, ref action);
                }
                catch (Exception ex) { if (!cancelled) err = ex.Message; }
                if (cancelled) { err = null; action = "cancelled"; }
                ui.BeginInvoke(new Action(() =>
                {
                    proc = null; req = null; Busy = false;
                    if (newSession != null) Chat.Sessions[p] = newSession;
                    if (err == null && reply.Length > 0)
                        Chat.Messages.Add(new AiMessage { Role = "assistant", Content = reply.ToString(), Provider = p, Model = model, Method = method, Blocks = blocks });
                    else if (Chat.Messages.Count > 0 && Chat.Messages[Chat.Messages.Count - 1] == user && reply.Length == 0)
                        Chat.Messages.RemoveAt(Chat.Messages.Count - 1);
                    SaveChat();
                    onDone(err, action);
                }));
            });
            th.IsBackground = true;
            th.Start();
        }

        // ---- HTTP (API key)
        IEnumerable<Dictionary<string, object>> Sse(string url, Dictionary<string, string> headers, object body, Action<WebHeaderCollection> onHeaders)
        {
            var js = Store.Js();
            var r = (HttpWebRequest)WebRequest.Create(url);
            req = r;
            r.Method = "POST"; r.ContentType = "application/json"; r.Timeout = 60000; r.ReadWriteTimeout = 300000; r.AllowReadStreamBuffering = false;
            foreach (var h in headers) r.Headers[h.Key] = h.Value;
            var bytes = Encoding.UTF8.GetBytes(js.Serialize(body));
            using (var s = r.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
            using (var resp = (HttpWebResponse)r.GetResponse())
            {
                if (onHeaders != null) onHeaders(resp.Headers);
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string line;
                    while (!cancelled && (line = sr.ReadLine()) != null)
                    {
                        if (!line.StartsWith("data:")) continue;
                        string payload = line.Substring(5).Trim();
                        if (payload == "[DONE]") yield break;
                        Dictionary<string, object> ev = null;
                        try { ev = js.DeserializeObject(payload) as Dictionary<string, object>; } catch { }
                        if (ev != null) yield return ev;
                    }
                }
            }
        }

        static string HttpError(WebException e, out int code)
        {
            code = 0;
            var resp = e.Response as HttpWebResponse;
            if (resp == null) return "Network error: " + e.Message;
            code = (int)resp.StatusCode;
            string msg = "";
            try
            {
                using (var sr = new StreamReader(resp.GetResponseStream())) msg = sr.ReadToEnd();
                var o = Store.Js().DeserializeObject(msg);
                var arr = o as object[];
                var d = (arr != null && arr.Length > 0 ? arr[0] : o) as Dictionary<string, object>;
                var er = d != null && d.ContainsKey("error") ? d["error"] as Dictionary<string, object> : null;
                msg = er != null ? Str(er, "message") : "";
            }
            catch { }
            return "HTTP " + code + (msg.Length > 0 ? ": " + msg : "");
        }

        static readonly HashSet<string> FallbackModels = new HashSet<string> { "claude-opus-5-5", "claude-sonnet-5-5", "claude-fable-5-1", "claude-opus-5" };
        static readonly HashSet<string> EffortModels = new HashSet<string> { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-1", "claude-fable-5", "claude-opus-5", "claude-sonnet-5", "claude-opus-4-8", "claude-opus-4-7" };

        // Anthropic-style content: image blocks first, then the text (the shape the CLI's stream-json takes too).
        static object ClaudeUserContent(AiMessage m, bool images)
        {
            if (!images || !m.HasImages) return m.Images != null ? TextOr(m, "[image]") : m.Content;
            var list = new List<object>();
            foreach (var f in m.LiveImages())
                list.Add(new Dictionary<string, object> { { "type", "image" }, { "source", new Dictionary<string, object> { { "type", "base64" }, { "media_type", Mime(f) }, { "data", B64(f) } } } });
            if (!string.IsNullOrWhiteSpace(m.Content)) list.Add(new Dictionary<string, object> { { "type", "text" }, { "text", m.Content } });
            return list;
        }

        List<Dictionary<string, object>> ChatClaudeKey(string key, string model, string sys, List<AiMessage> history, Action<string> delta, Action thinking, ref string err, ref string action)
        {
            var withImg = ImageTurns(history);
            var msgs = history.Select(m => (object)new Dictionary<string, object>
            {
                { "role", m.Role },
                { "content", m.Role == "assistant" && m.Blocks != null && m.Provider == "claude" && m.Model == model ? (object)m.Blocks : ClaudeUserContent(m, withImg.Contains(m)) }
            }).ToList();
            bool fb = FallbackModels.Contains(model);
            var body = new Dictionary<string, object> { { "model", model }, { "max_tokens", 16000 }, { "stream", true }, { "system", sys }, { "messages", msgs } };
            if (EffortModels.Contains(model)) body["output_config"] = new Dictionary<string, object> { { "effort", Store.S.Effort } };
            if (fb) body["fallbacks"] = "default";
            var headers = new Dictionary<string, string> { { "x-api-key", key }, { "anthropic-version", "2026-10-21" } };
            if (fb) headers["anthropic-beta"] = "server-side-fallback-2026-07-01";
            var blocks = new SortedDictionary<int, Dictionary<string, object>>();
            string stop = null;
            try
            {
                foreach (var ev in Sse("https://api.anthropic.com/v1/messages", headers, body, h => SaveKeyLimits("claude", h)))
                {
                    string t = Str(ev, "type");
                    if (t == "content_block_start")
                    {
                        var cb = ev["content_block"] as Dictionary<string, object>;
                        if (cb != null) blocks[Convert.ToInt32(ev["index"])] = new Dictionary<string, object>(cb);
                    }
                    else if (t == "content_block_delta")
                    {
                        Dictionary<string, object> b; var d = ev["delta"] as Dictionary<string, object>;
                        if (d == null || !blocks.TryGetValue(Convert.ToInt32(ev["index"]), out b)) continue;
                        string dt = Str(d, "type");
                        if (dt == "text_delta") { b["text"] = Str(b, "text") + Str(d, "text"); delta(Str(d, "text")); }
                        else if (dt == "thinking_delta") { b["thinking"] = Str(b, "thinking") + Str(d, "thinking"); thinking(); }
                        else if (dt == "signature_delta") b["signature"] = Str(d, "signature");
                    }
                    else if (t == "message_delta")
                    {
                        var d = ev["delta"] as Dictionary<string, object>;
                        if (d != null && d.ContainsKey("stop_reason") && d["stop_reason"] != null) stop = d["stop_reason"].ToString();
                    }
                    else if (t == "error") { var e = ev["error"] as Dictionary<string, object>; err = e != null ? Str(e, "message") : "API error"; }
                }
            }
            catch (WebException e) { if (!cancelled) { int code; err = HttpError(e, out code); if (code == 401 || code == 403) action = "key"; } }
            if (stop == "refusal") { err = "The model declined this request."; return null; }
            if (stop == "max_tokens") delta(" …");
            return blocks.Values.ToList();
        }

        void ChatGptKey(string key, string model, string sys, List<AiMessage> history, Action<string> delta, ref string err, ref string action)
        {
            var msgs = new List<object> { new Dictionary<string, object> { { "role", "system" }, { "content", sys } } };
            var withImg = ImageTurns(history);
            msgs.AddRange(history.Select(m =>
            {
                object content = m.Images != null ? TextOr(m, "[image]") : m.Content;
                if (withImg.Contains(m))
                {
                    var parts = new List<object>();
                    if (!string.IsNullOrWhiteSpace(m.Content)) parts.Add(new Dictionary<string, object> { { "type", "text" }, { "text", m.Content } });
                    foreach (var f in m.LiveImages())
                        parts.Add(new Dictionary<string, object> { { "type", "image_url" }, { "image_url", new Dictionary<string, object> { { "url", "data:" + Mime(f) + ";base64," + B64(f) } } } });
                    content = parts;
                }
                return (object)new Dictionary<string, object> { { "role", m.Role }, { "content", content } };
            }));
            var body = new Dictionary<string, object> { { "model", model }, { "stream", true }, { "messages", msgs } };
            try
            {
                foreach (var ev in Sse("https://api.openai.com/v1/chat/completions", new Dictionary<string, string> { { "Authorization", "Bearer " + key } }, body, h => SaveKeyLimits("gpt", h)))
                {
                    var ch = ev.ContainsKey("choices") ? ev["choices"] as object[] : null;
                    if (ch == null) continue;
                    foreach (var c in ch.OfType<Dictionary<string, object>>())
                    {
                        var d = c.ContainsKey("delta") ? c["delta"] as Dictionary<string, object> : null;
                        string t = Str(d, "content");
                        if (t.Length > 0) delta(t);
                    }
                }
            }
            catch (WebException e) { if (!cancelled) { int code; err = HttpError(e, out code); if (code == 401 || code == 403) action = "key"; } }
        }

        static object[] GeminiParts(AiMessage m, bool images)
        {
            var parts = new List<object>();
            if (images)
                foreach (var f in m.LiveImages())
                    parts.Add(new Dictionary<string, object> { { "inline_data", new Dictionary<string, object> { { "mime_type", Mime(f) }, { "data", B64(f) } } } });
            string t = m.Images != null ? (images && m.HasImages ? m.Content : TextOr(m, "[image]")) : m.Content;
            if (!string.IsNullOrWhiteSpace(t) || parts.Count == 0) parts.Add(new Dictionary<string, object> { { "text", t ?? "" } });
            return parts.ToArray();
        }

        void ChatGeminiKey(string key, string model, string sys, List<AiMessage> history, Action<string> delta, Action thinking, ref string err, ref string action)
        {
            var withImg = ImageTurns(history);
            var body = new Dictionary<string, object>
            {
                { "systemInstruction", new Dictionary<string, object> { { "parts", new object[] { new Dictionary<string, object> { { "text", sys } } } } } },
                { "contents", history.Select(m => (object)new Dictionary<string, object> { { "role", m.Role == "assistant" ? "model" : "user" }, { "parts", GeminiParts(m, withImg.Contains(m)) } }).ToList() }
            };
            string url = "https://generativelanguage.googleapis.com/v1beta/models/" + Uri.EscapeDataString(model) + ":streamGenerateContent?alt=sse";
            try
            {
                foreach (var ev in Sse(url, new Dictionary<string, string> { { "x-goog-api-key", key } }, body, null))
                {
                    var cands = ev.ContainsKey("candidates") ? ev["candidates"] as object[] : null;
                    if (cands == null) continue;
                    foreach (var c in cands.OfType<Dictionary<string, object>>())
                    {
                        var content = c.ContainsKey("content") ? c["content"] as Dictionary<string, object> : null;
                        var parts = content != null && content.ContainsKey("parts") ? content["parts"] as object[] : null;
                        if (parts == null) continue;
                        foreach (var part in parts.OfType<Dictionary<string, object>>())
                        {
                            if (part.ContainsKey("thought") && Convert.ToBoolean(part["thought"])) thinking();
                            else if (Str(part, "text").Length > 0) delta(Str(part, "text"));
                        }
                    }
                }
            }
            catch (WebException e) { if (!cancelled) { int code; err = HttpError(e, out code); if (code == 401 || code == 403) action = "key"; } }
        }

        void SaveKeyLimits(string p, WebHeaderCollection h)
        {
            var info = new UsageInfo { At = Now() };
            var rows = p == "claude"
                ? new[] { new[] { "Request", "anthropic-ratelimit-requests" }, new[] { "Token input", "anthropic-ratelimit-input-tokens" }, new[] { "Token output", "anthropic-ratelimit-output-tokens" } }
                : new[] { new[] { "Request", "x-ratelimit-%-requests" }, new[] { "Token", "x-ratelimit-%-tokens" } };
            foreach (var r in rows)
            {
                string lim = p == "claude" ? h[r[1] + "-limit"] : h[r[1].Replace("%", "limit")];
                string left = p == "claude" ? h[r[1] + "-remaining"] : h[r[1].Replace("%", "remaining")];
                long l, rem;
                if (!long.TryParse(lim, out l) || !long.TryParse(left, out rem) || l <= 0) continue;
                info.Windows.Add(new UsageWindow { Label = r[0] + " / min", Percent = 100.0 * (1 - (double)rem / l), Detail = rem.ToString("N0", K.EN) + " of " + l.ToString("N0", K.EN) + " left" });
            }
            if (info.Windows.Count > 0) lock (keyLimits) keyLimits[p] = info;
        }

        // ---- CLI (browser sign-in)
        static string TranscriptPrompt(List<AiMessage> msgs, string session)
        {
            var lastMsg = msgs[msgs.Count - 1];
            string last = lastMsg.HasImages ? TextOr(lastMsg, ImageOnlyPrompt) : lastMsg.Content;
            if (!string.IsNullOrEmpty(session) || msgs.Count == 1) return last;
            var sb = new StringBuilder("Conversation so far:");
            foreach (var m in msgs.Take(msgs.Count - 1).Skip(Math.Max(0, msgs.Count - 21)))
                sb.Append("\n\n[" + (m.Role == "user" ? "User" : "Assistant") + "]\n" + (m.Images != null ? "[image attached] " : "") + m.Content);
            sb.Append("\n---\nReply to the user's latest message:\n" + last);
            string s = sb.ToString();
            return s.Length > 24000 ? s.Substring(s.Length - 24000) : s;
        }

        void ChatCli(string p, string model, string sys, List<AiMessage> msgs, string session, Action<string> delta, Action thinking, ref string newSession, ref string err, ref string action)
        {
            string exe = Which(p);
            if (exe == null) { err = "The " + CliName(p) + " CLI isn't installed."; action = "login"; return; }
            string prompt = TranscriptPrompt(msgs, session);
            var images = msgs[msgs.Count - 1].LiveImages();
            var args = new List<string>();
            bool stdinPrompt = false;
            if (p == "claude")
            {
                args.AddRange(new[] { "-p", "--output-format", "stream-json", "--verbose", "--include-partial-messages", "--tools", "", "--strict-mcp-config", "--setting-sources", "", "--system-prompt", sys });
                // photos go in as base64 image blocks of a stream-json user message
                if (images.Count > 0)
                {
                    args.AddRange(new[] { "--input-format", "stream-json" });
                    var content = new List<object>();
                    foreach (var f in images)
                        content.Add(new Dictionary<string, object> { { "type", "image" }, { "source", new Dictionary<string, object> { { "type", "base64" }, { "media_type", Mime(f) }, { "data", B64(f) } } } });
                    content.Add(new Dictionary<string, object> { { "type", "text" }, { "text", prompt } });
                    prompt = Store.Js().Serialize(new Dictionary<string, object> { { "type", "user" }, { "message", new Dictionary<string, object> { { "role", "user" }, { "content", content } } } }) + "\n";
                }
                if (!string.IsNullOrEmpty(model)) { args.Add("--model"); args.Add(model); }
                if (!string.IsNullOrEmpty(session)) { args.Add("--resume"); args.Add(session); }
                stdinPrompt = true;
            }
            else if (p == "gpt")
            {
                var common = new List<string> { "--json", "--skip-git-repo-check", "--ignore-user-config", "-c", "sandbox_mode=\"read-only\"", "-c", "approval_policy=\"never\"" };
                if (!string.IsNullOrEmpty(model)) { common.Add("-m"); common.Add(model); }
                args.Add("exec");
                if (!string.IsNullOrEmpty(session)) { args.Add("resume"); args.AddRange(common); args.Add(session); }
                else args.AddRange(common);
                foreach (var f in images) { args.Add("--image"); args.Add(f); }
                args.Add("--");   // --image takes several values: end them before the prompt
                args.Add(prompt);
            }
            else
            {
                // Antigravity's print mode only takes text: point its file tools at the saved photos
                if (images.Count > 0)
                {
                    prompt += "\n\nAttached image" + (images.Count > 1 ? "s" : "") + " (open with your file viewing tool before answering):\n" + string.Join("\n", images);
                    args.AddRange(new[] { "--add-dir", ImgDir });
                }
                args.AddRange(new[] { "-p", prompt, "--output-format", "stream-json" });
                if (!string.IsNullOrEmpty(model)) { args.Add("--model"); args.Add(model); }
                if (!string.IsNullOrEmpty(session)) { args.Add("--conversation"); args.Add(session); }
            }

            var psi = MakePsi(exe, args);
            var pr = Process.Start(psi);
            proc = pr;
            var errText = new StringBuilder();
            pr.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errText) errText.AppendLine(e.Data); };
            pr.BeginErrorReadLine();
            if (stdinPrompt) { var w = new StreamWriter(pr.StandardInput.BaseStream, new UTF8Encoding(false)); w.Write(prompt); w.Close(); }
            else pr.StandardInput.Close();

            var js = Store.Js();
            bool gotText = false; string cliErr = null;
            string line;
            while ((line = pr.StandardOutput.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line[0] != '{') continue;
                Dictionary<string, object> ev;
                try { ev = js.DeserializeObject(line) as Dictionary<string, object>; } catch { continue; }
                if (ev == null) continue;
                if (p == "claude")
                {
                    string t = Str(ev, "type");
                    if (t == "system" && Str(ev, "subtype") == "init" && Str(ev, "session_id").Length > 0) newSession = Str(ev, "session_id");
                    else if (t == "stream_event")
                    {
                        var e = ev["event"] as Dictionary<string, object>;
                        if (e != null && Str(e, "type") == "content_block_delta")
                        {
                            var d = e["delta"] as Dictionary<string, object>;
                            if (Str(d, "type") == "text_delta") { gotText = true; delta(Str(d, "text")); }
                            else if (Str(d, "type") == "thinking_delta") thinking();
                        }
                    }
                    else if (t == "result" && ev.ContainsKey("is_error") && Convert.ToBoolean(ev["is_error"]))
                        cliErr = Str(ev, "result").Length > 0 ? Str(ev, "result") : "Claude returned an error";
                }
                else if (p == "gpt")
                {
                    string t = Str(ev, "type");
                    if (t == "thread.started" && Str(ev, "thread_id").Length > 0) newSession = Str(ev, "thread_id");
                    else if (t == "item.started" || t == "item.updated" || t == "item.completed")
                    {
                        var item = ev.ContainsKey("item") ? ev["item"] as Dictionary<string, object> : null;
                        string kind = Str(item, "type").Length > 0 ? Str(item, "type") : Str(item, "item_type");
                        if (kind == "reasoning") thinking();
                        else if ((kind == "agent_message" || kind == "assistant_message") && t == "item.completed" && Str(item, "text").Length > 0)
                        { delta((gotText ? "\n\n" : "") + Str(item, "text")); gotText = true; }
                    }
                    else if (t == "error" || t == "turn.failed")
                    {
                        string msg = Str(ev, "message");
                        if (msg.Length == 0 && ev.ContainsKey("error")) msg = Str(ev["error"] as Dictionary<string, object>, "message");
                        try { var inner = js.DeserializeObject(msg) as Dictionary<string, object>; if (inner != null && inner.ContainsKey("error")) msg = Str(inner["error"] as Dictionary<string, object>, "message"); } catch { }
                        cliErr = msg.Length > 0 ? msg : "Codex error";
                    }
                }
                else
                {
                    string t = Str(ev, "event");
                    if (t == "init" && Str(ev, "conversation_id").Length > 0) newSession = Str(ev, "conversation_id");
                    else if (t == "step_update")
                    {
                        var st = ev["step_update"] as Dictionary<string, object>;
                        if (Str(st, "step_type") == "agent_response" && Str(st, "text_delta").Length > 0) { gotText = true; delta(Str(st, "text_delta")); }
                    }
                    else if (t == "result")
                    {
                        var res = ev["result"] as Dictionary<string, object>;
                        string stt = Str(res, "status");
                        if (stt.Length > 0 && stt != "SUCCESS") cliErr = Str(res, "error").Length > 0 ? Str(res, "error") : "Antigravity: " + stt.ToLowerInvariant();
                        else if (!gotText && Str(res, "response").Length > 0) { gotText = true; delta(Str(res, "response")); }
                    }
                }
            }
            pr.WaitForExit(5000);
            int code = pr.HasExited ? pr.ExitCode : 0;
            string stderr; lock (errText) stderr = errText.ToString();
            if (cancelled) return;
            if (p == "gemini" && (cliErr + stderr).ToLowerInvariant().Contains("authenticat"))
            {
                try { File.Delete(AgyMarker); } catch { }
                err = "Not signed in to Antigravity."; action = "login"; return;
            }
            if (cliErr != null) { CliError(cliErr, ref err, ref action); return; }
            if (code != 0 && !gotText)
            {
                var lines = stderr.Split('\n').Where(l => l.Trim().Length > 0 && !l.Contains("codex_models_manager")).ToList();
                CliError(lines.Count > 0 ? string.Join("\n", lines.Skip(Math.Max(0, lines.Count - 6))) : CliName(p) + " exited with status " + code, ref err, ref action);
            }
        }

        static void CliError(string text, ref string err, ref string action)
        {
            string low = text.ToLowerInvariant();
            action = new[] { "newer version", "or newer is required", "please upgrade", "claude update" }.Any(low.Contains) ? "update"
                : new[] { "not logged in", "log in", "login", "unauthori", "authenticat" }.Any(low.Contains) ? "login" : "";
            err = text.Trim().Length > 600 ? text.Trim().Substring(0, 600) : text.Trim();
        }

        // ------------------------------------------------------------- usage
        static double Now() { return (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds; }
        static double Epoch(object v)
        {
            if (v == null) return 0;
            double d;
            if (double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, K.EN, out d)) return d > 1e11 ? d / 1000 : d;
            DateTime dt;
            if (DateTime.TryParse(v.ToString(), K.EN, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out dt))
                return (dt - new DateTime(1970, 1, 1)).TotalSeconds;
            return 0;
        }

        public void Usage(string p, Action<UsageInfo> done)
        {
            string method = MethodFor(p);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var o = new UsageInfo();
                o.Url = p == "claude" ? (method == "cli" ? "https://claude.ai/settings/usage" : "https://console.anthropic.com/usage")
                      : p == "gpt" ? (method == "cli" ? "https://chatgpt.com/codex/settings/usage" : "https://platform.openai.com/usage")
                      : "https://aistudio.google.com/usage";
                try
                {
                    if (p == "claude" && method == "cli") ClaudePlan(o);
                    else if (p == "gpt" && method == "cli") CodexUsage(o);
                    else if (method == "key" && p != "gemini")
                    {
                        UsageInfo saved; lock (keyLimits) keyLimits.TryGetValue(p, out saved);
                        if (saved != null) { o.Windows = saved.Windows; o.At = saved.At; }
                        o.Note = "API rate limits from your last reply. Spending is on the usage page.";
                    }
                    else o.Note = "Google doesn't report the remaining quota. See the usage page.";
                }
                catch (Exception ex) { o.Note = ex.Message; }
                ui.BeginInvoke(new Action(() => done(o)));
            });
        }

        void ClaudePlan(UsageInfo o)
        {
            Dictionary<string, object> oauth = null;
            try
            {
                var c = Store.Js().DeserializeObject(File.ReadAllText(Path.Combine(Home, ".claude", ".credentials.json"))) as Dictionary<string, object>;
                oauth = c != null && c.ContainsKey("claudeAiOauth") ? c["claudeAiOauth"] as Dictionary<string, object> : null;
            }
            catch { }
            string plan = Str(oauth, "subscriptionType");
            o.Plan = plan.Length > 0 ? char.ToUpper(plan[0]) + plan.Substring(1) : "";
            if (claudePlanCache != null && (DateTime.Now - claudePlanAt).TotalSeconds < 300) { o.Windows = claudePlanCache.Windows; o.At = claudePlanCache.At; return; }
            string token = Str(oauth, "accessToken");
            if (token.Length > 0 && Epoch(oauth.ContainsKey("expiresAt") ? oauth["expiresAt"] : null) > Now() && (DateTime.Now - claudePlanTried).TotalSeconds > 120)
            {
                claudePlanTried = DateTime.Now;
                try
                {
                    var d = Store.Js().DeserializeObject(HttpGet("https://api.anthropic.com/api/oauth/usage", new Dictionary<string, string> { { "Authorization", "Bearer " + token }, { "anthropic-beta", "oauth-2025-04-20" } })) as Dictionary<string, object>;
                    var wins = new List<UsageWindow>();
                    foreach (var kv in new[] { new[] { "five_hour", "5-hour" }, new[] { "seven_day", "Week" }, new[] { "seven_day_opus", "Week · Opus" }, new[] { "seven_day_sonnet", "Week · Sonnet" } })
                    {
                        var w = d != null && d.ContainsKey(kv[0]) ? d[kv[0]] as Dictionary<string, object> : null;
                        if (w != null && w.ContainsKey("utilization") && w["utilization"] != null)
                            wins.Add(new UsageWindow { Label = kv[1], Percent = Convert.ToDouble(w["utilization"]), ResetsAt = Epoch(w.ContainsKey("resets_at") ? w["resets_at"] : null) });
                    }
                    claudePlanCache = new UsageInfo { Windows = wins, At = Now() }; claudePlanAt = DateTime.Now;
                    o.Windows = wins; o.At = Now();
                    return;
                }
                catch { }
            }
            if (claudePlanCache != null) { o.Windows = claudePlanCache.Windows; o.At = claudePlanCache.At; o.Note = "Couldn't refresh; showing the last check."; }
            else o.Note = "Not available right now. Try again in a few minutes, or open the usage page.";
        }

        static void CodexUsage(UsageInfo o)
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Home, ".codex");
            string dir = Path.Combine(home, "sessions");
            if (!Directory.Exists(dir)) { o.Note = "Send a message to see your limits."; return; }
            var files = Directory.GetFiles(dir, "rollout-*.jsonl", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTime).Take(10);
            foreach (var f in files)
            {
                string last = null;
                try { foreach (var l in File.ReadLines(f)) if (l.Contains("\"rate_limits\"")) last = l; } catch { continue; }
                if (last == null) continue;
                try
                {
                    var ev = Store.Js().DeserializeObject(last) as Dictionary<string, object>;
                    var rl = (ev["payload"] as Dictionary<string, object>)["rate_limits"] as Dictionary<string, object>;
                    string plan = Str(rl, "plan_type");
                    o.Plan = plan.Length > 0 ? char.ToUpper(plan[0]) + plan.Substring(1) : "";
                    foreach (var k in new[] { "primary", "secondary" })
                    {
                        var w = rl.ContainsKey(k) ? rl[k] as Dictionary<string, object> : null;
                        if (w == null) continue;
                        int mins = 0; int.TryParse(Str(w, "window_minutes"), out mins);
                        string label = mins == 300 ? "5-hour" : mins == 1440 ? "Day" : mins == 10080 ? "Week" : mins == 43200 ? "Month" : mins < 1440 ? Math.Round(mins / 60.0) + "-hour" : Math.Round(mins / 1440.0) + "-day";
                        double pct = 0; double.TryParse(Str(w, "used_percent"), System.Globalization.NumberStyles.Float, K.EN, out pct);
                        o.Windows.Add(new UsageWindow { Label = label, Percent = pct, ResetsAt = Epoch(w.ContainsKey("resets_at") ? w["resets_at"] : null) });
                    }
                    if (Str(rl, "rate_limit_reached_type").Length > 0) o.Note = "Limit reached.";
                    o.At = Epoch(ev.ContainsKey("timestamp") ? ev["timestamp"] : null);
                    return;
                }
                catch { }
            }
            o.Note = "Send a message to see your limits.";
        }
    }
}
