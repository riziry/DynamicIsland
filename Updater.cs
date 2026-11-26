// Self-updater: checks the latest GitHub release, downloads it and swaps the exe in place.
// A running exe can't be overwritten on Windows, but it can be renamed: the current exe becomes
// DynamicIsland.exe.old, the new one takes its name, the app restarts and deletes the .old file.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

// Stamped into the exe (Properties → Details) so the updater and release.bat can read a build's version.
[assembly: System.Reflection.AssemblyTitle("Dynamic Island")]
[assembly: System.Reflection.AssemblyProduct("Dynamic Island")]
[assembly: System.Reflection.AssemblyVersion(DynIsland.AppInfo.Version)]
[assembly: System.Reflection.AssemblyFileVersion(DynIsland.AppInfo.Version)]
[assembly: System.Reflection.AssemblyInformationalVersion(DynIsland.AppInfo.Version)]

namespace DynIsland
{
    static class AppInfo
    {
        // Keep in step with the release tag on GitHub (v1.0.0 -> "1.0.0").
        public const string Version = "1.2.0";
        public const string Repo = "riziry/DynamicIsland";
        public const string UserAgent = "DynamicIsland/" + Version + " (Windows)";
    }

    static class Updater
    {
        public enum St { Idle, Checking, UpToDate, Available, Downloading, Installing, Error }

        public static St State = St.Idle;
        public static string LatestVersion = "", PageUrl = "", AssetUrl = "", AssetName = "", Error = "";
        public static double Progress;
        public static DateTime LastCheck = DateTime.MinValue;
        // set by whoever shows the state (the About page); called on a worker thread
        public static Action OnChange;

        static Updater()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288 /*Tls13*/;
        }

        static void Set(St s) { State = s; var h = OnChange; if (h != null) { try { h(); } catch { } } }

        public static bool Busy { get { return State == St.Checking || State == St.Downloading || State == St.Installing; } }

        // ------------------------------------------------------------------ check

        public static void CheckAsync(Action<bool> done)
        {
            if (Busy) return;
            Set(St.Checking);
            new Thread(() =>
            {
                bool newer = false;
                try { newer = Check(); Set(newer ? St.Available : St.UpToDate); }
                catch (Exception ex) { Error = Friendly(ex); Log.Write("update check: " + ex.Message); Set(St.Error); }
                LastCheck = DateTime.Now;
                if (done != null) { try { done(newer); } catch { } }
            }) { IsBackground = true }.Start();
        }

        static bool Check()
        {
            var d = Store.Js().DeserializeObject(Encoding.UTF8.GetString(Get("https://api.github.com/repos/" + AppInfo.Repo + "/releases/latest", 15000, null))) as Dictionary<string, object>;
            if (d == null) throw new Exception("Unexpected answer from GitHub");
            LatestVersion = Str(d, "tag_name").TrimStart('v', 'V');
            PageUrl = Str(d, "html_url");
            AssetUrl = ""; AssetName = "";
            var assets = d.ContainsKey("assets") ? d["assets"] as object[] : null;
            if (assets != null)
            {
                var list = assets.OfType<Dictionary<string, object>>().ToList();
                // prefer a zip (exe + fonts), else a bare exe
                var pick = list.FirstOrDefault(a => Str(a, "name").EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        ?? list.FirstOrDefault(a => Str(a, "name").EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                if (pick != null) { AssetUrl = Str(pick, "browser_download_url"); AssetName = Str(pick, "name"); }
            }
            return IsNewer(LatestVersion, AppInfo.Version);
        }

        public static bool IsNewer(string latest, string current)
        {
            Version a, b;
            if (!Version.TryParse(Pad(latest), out a) || !Version.TryParse(Pad(current), out b)) return false;
            return a > b;
        }
        static string Pad(string v)
        {
            v = new string((v ?? "").TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
            if (v.Length == 0) return "";
            return v.Contains('.') ? v : v + ".0";
        }

        // ------------------------------------------------------------------ install

        // Downloads and swaps in the new version, then calls restart (on a worker thread).
        public static void InstallAsync(Action restart)
        {
            if (Busy || AssetUrl.Length == 0) return;
            Progress = 0; Set(St.Downloading);
            new Thread(() =>
            {
                try
                {
                    Install();
                    if (restart != null) restart();
                }
                catch (Exception ex) { Error = Friendly(ex); Log.Write("update install: " + ex); Set(St.Error); }
            }) { IsBackground = true }.Start();
        }

        static void Install()
        {
            string work = WorkDir;
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
            Directory.CreateDirectory(work);

            string file = Path.Combine(work, AssetName.Length > 0 ? Path.GetFileName(AssetName) : "update.zip");
            int lastPct = -1;
            File.WriteAllBytes(file, Get(AssetUrl, 120000, p =>
            {
                Progress = p; int pct = (int)(p * 100);
                if (pct != lastPct) { lastPct = pct; Set(St.Downloading); }
            }));
            Set(St.Installing);

            string newExe, newFonts = null;
            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string ex = Path.Combine(work, "x");
                ZipFile.ExtractToDirectory(file, ex);
                newExe = Directory.GetFiles(ex, "DynamicIsland.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (newExe == null) throw new Exception("The release zip has no DynamicIsland.exe");
                string f = Path.Combine(Path.GetDirectoryName(newExe), "fonts");
                if (Directory.Exists(f)) newFonts = f;
            }
            else newExe = file;

            // the download must really be the newer build, or we'd swap in the same version forever
            string got = ExeVersion(newExe);
            if (!IsNewer(got, AppInfo.Version))
                throw new Exception("The v" + LatestVersion + " download contains " + (got.Length > 0 ? "version " + got : "an older build") +
                    ", not v" + LatestVersion + ". Nothing was changed; the release needs a fresh build.");

            string exe = Store.ExePath, dir = Path.GetDirectoryName(exe), old = exe + ".old";
            try { if (File.Exists(old)) File.Delete(old); } catch { old = exe + "." + DateTime.Now.Ticks + ".old"; }
            File.Move(exe, old);
            try { File.Copy(newExe, exe, true); }
            catch { try { File.Move(old, exe); } catch { } throw; }

            // fonts the app has open stay locked; same-size files are skipped, the rest are best effort
            if (newFonts != null)
            {
                string dst = Path.Combine(dir, "fonts");
                Directory.CreateDirectory(dst);
                foreach (var src in Directory.GetFiles(newFonts))
                {
                    string to = Path.Combine(dst, Path.GetFileName(src));
                    try { if (File.Exists(to) && new FileInfo(to).Length == new FileInfo(src).Length) continue; File.Copy(src, to, true); }
                    catch (Exception e) { Log.Write("update font " + Path.GetFileName(src) + ": " + e.Message); }
                }
            }
            try { Directory.Delete(work, true); } catch { }
            Store.S.UpdatedFrom = AppInfo.Version;   // saved by Restart(); the next start confirms the new version
        }

        // Called at startup: removes the exe left behind by the last update.
        public static void CleanupOld()
        {
            try
            {
                string exe = Store.ExePath;
                foreach (var f in Directory.GetFiles(Path.GetDirectoryName(exe), Path.GetFileName(exe) + "*.old"))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }

        // ------------------------------------------------------------------ helpers

        static byte[] Get(string url, int timeout, Action<double> progress)
        {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = AppInfo.UserAgent; r.Timeout = timeout; r.ReadWriteTimeout = timeout;
            r.Accept = url.Contains("api.github.com") ? "application/vnd.github+json" : "*/*";
            r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var resp = r.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                long total = resp.ContentLength, got = 0;
                var buf = new byte[65536]; int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, n); got += n;
                    if (progress != null && total > 0) progress((double)got / total);
                }
                return ms.ToArray();
            }
        }

        // where the download goes while installing (removed afterwards)
        public static string WorkDir { get { return Path.Combine(Path.GetTempPath(), "DynamicIsland-update"); } }

        // version stamped into an exe; builds from before the stamp report "" (or 0.0.0.0)
        public static string ExeVersion(string path)
        {
            try
            {
                var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "";
                return v == "0.0.0.0" ? "" : v;
            }
            catch { return ""; }
        }

        static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }

        static string Friendly(Exception ex)
        {
            var we = ex as WebException;
            if (we != null)
            {
                var hr = we.Response as HttpWebResponse;
                if (hr != null && hr.StatusCode == HttpStatusCode.NotFound) return "No published release found on GitHub yet.";
                if (hr != null && (int)hr.StatusCode == 403) return "GitHub rate limit reached. Try again in a while.";
                return "Couldn't reach GitHub. Check your connection.";
            }
            if (ex is UnauthorizedAccessException) return "No permission to replace the app files. Install it through the setup wizard first.";
            return ex.Message;
        }
    }
}
