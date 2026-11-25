using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Glassify
{
    /// Automatic game mode: when another app goes fullscreen (a game), switch to the mini player (optionally as a
    /// click-through "ghost"); when it's gone, put everything back the way it was. Checks every 2 seconds, which
    /// costs next to nothing (a couple of window lookups, no polling of the page).
    static class GameMode
    {
        static Timer _timer;
        static int _hits, _misses;
        public static volatile bool Active;
        static bool _hidMain, _showedMini;

        public static void Start() => _timer = new Timer(_ => Check(), null, 4000, 2000);

        static void Check()
        {
            try
            {
                bool full = Opts.GameMode && ForegroundIsFullscreen();
                if (full) { _misses = 0; if (++_hits == 2 && !Active) Enter(); }
                else { _hits = 0; if (++_misses == 2 && Active) Exit(); }
            }
            catch (Exception ex) { Program.Log("gamemode: " + ex.Message); }
        }

        static void Enter()
        {
            Active = true;
            Program.Log("game mode on: " + Describe());
            var w = Program.Window; if (w == null) return;
            w.BeginInvoke(new Action(() =>
            {
                _showedMini = !Mini.IsShown;          // remember whether the mini player was already open
                _hidMain = w.Visible && w.WindowState != System.Windows.Forms.FormWindowState.Minimized;
                if (_hidMain) w.HideToMini(); else w.RefreshPower();
                Mini.ShowOnly();
                if (Opts.Ghost) Mini.SetGhost(true, auto: true);
            }));
        }

        static void Exit()
        {
            Active = false;
            Program.Log("game mode off");
            var w = Program.Window; if (w == null) return;
            w.BeginInvoke(new Action(() =>
            {
                Mini.SetGhost(false, auto: true);
                if (_showedMini) Mini.HideOnly();     // it wasn't open before the game: close it again
                if (_hidMain) w.BringBack(activate: false);
                else w.RefreshPower();
                _hidMain = _showedMini = false;
            }));
        }

        static string Describe()
        {
            try
            {
                var h = GetForegroundWindow(); GetWindowThreadProcessId(h, out uint pid);
                var t = new StringBuilder(120); GetWindowText(h, t, 120);
                SHQueryUserNotificationState(out int st);
                return Process.GetProcessById((int)pid).ProcessName + " '" + t + "' (windows state " + st + ")";
            }
            catch { return "?"; }
        }

        /// A game is running when the active window belongs to another app and is fullscreen. Uses Windows' own
        /// fullscreen signal (the one Focus Assist uses for games) and, for borderless-windowed games, checks the
        /// window covers its whole monitor. The desktop and taskbar don't count.
        static bool ForegroundIsFullscreen()
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == (uint)Process.GetCurrentProcess().Id) return false;
            if (IsBrowserOrPlayer(pid)) return false;   // fullscreen video / browser isn't a game
            // 3 = Direct3D exclusive fullscreen game, 2 = fullscreen app / presentation ("busy")
            if (SHQueryUserNotificationState(out int state) == 0 && state == 3) return true;
            var cls = new StringBuilder(64); GetClassName(h, cls, 64);
            var c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Windows.UI.Core.CoreWindow") return false;
            if (!GetWindowRect(h, out RECT r)) return false;
            var mon = MonitorFromWindow(h, 2);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) return false;
            bool coversMonitor = r.L <= mi.rcMonitor.L && r.T <= mi.rcMonitor.T && r.R >= mi.rcMonitor.R && r.B >= mi.rcMonitor.B;
            if (!coversMonitor) return false;
            // A window covering the monitor is only a game if it has no normal title bar / borders
            // (a maximized app with an auto-hiding taskbar also covers the monitor, but keeps its caption).
            int style = GetWindowLong(h, -16);
            bool hasCaption = (style & 0x00C00000) == 0x00C00000, isMaximized = (style & 0x01000000) != 0;
            return !(hasCaption && isMaximized);
        }
        static readonly string[] NotGames = { "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "arc",
            "vlc", "mpc-hc64", "mpc-hc", "mpc-be64", "potplayermini64", "mpv", "wmplayer", "video.ui", "applicationframehost",
            "microsoft.media.player", "netflix", "powerpnt", "explorer", "msedgewebview2",
            // screenshot / screen-recording overlays cover the whole screen for a moment
            "snippingtool", "screenclippinghost", "screensketch", "sharex", "lightshot", "greenshot", "obs64", "gamebar", "gamebarftserver" };
        static uint _lastPid; static bool _lastNotGame;
        static bool IsBrowserOrPlayer(uint pid)
        {
            if (pid == _lastPid) return _lastNotGame;   // cached: the check runs every 2 s
            string name;
            try { name = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { return false; }
            _lastPid = pid; _lastNotGame = Array.IndexOf(NotGames, name) >= 0;
            return _lastNotGame;
        }
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        // (GetWindowText / GetWindowLong / SHQueryUserNotificationState declared above)
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    }

    /// Checks the Glassify GitHub repo's latest release (once at start-up, then daily) and tells the page
    /// when a newer version exists. Sends nothing about you: a plain anonymous request for public release info.
    /// One-click update: downloads the release zip from this repo only, checks GitHub's SHA-256 fingerprint,
    /// swaps the program files (never the data folder), and restarts. Any failure puts the old files back.
    static class Updates
    {
        static Timer _timer;
        public static string LatestTag, LatestUrl, AssetUrl, AssetSha256;
        static int _busy;
        static string Downloads => "https://github.com/" + Program.UpdateRepo + "/releases/download/";
        // Test hook, only with the data\debug file: read release info from data\update-feed.json instead of GitHub.
        static string TestFeed => File.Exists(Path.Combine(Program.Data, "debug")) && File.Exists(Path.Combine(Program.Data, "update-feed.json")) ? Path.Combine(Program.Data, "update-feed.json") : null;
        public static bool CanAutoUpdate => AssetUrl != null && FolderWritable();

        public static void Start() => _timer = new Timer(_ => Check(), null, TestFeed != null ? 3000 : 20000, 24 * 3600 * 1000);

        static void Check()
        {
            try { Fetch(); }
            catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound) { Clear(); /* no releases */ }
            catch (Exception ex) { Program.Log("update check: " + ex.Message); }
        }

        /// The update that was offered is gone (release removed, or already on the newest): hide the button.
        static void Clear()
        {
            if (LatestTag == null) return;
            LatestTag = LatestUrl = AssetUrl = AssetSha256 = null;
            Program.Window?.HideUpdate();
        }

        /// Reads the latest release from GitHub. True (and the button shown) when it's newer than this copy.
        static bool Fetch()
        {
            {
                string body;
                if (TestFeed != null) body = File.ReadAllText(TestFeed);
                else
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Program.UpdateRepo + "/releases/latest");
                    req.UserAgent = "Glassify-update-check"; req.Accept = "application/vnd.github+json"; req.Timeout = 15000;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream())) body = sr.ReadToEnd();
                }
                var o = Program.Json.DeserializeObject(body);
                var tag = Program.Str(o, "tag_name"); var url = Program.Str(o, "html_url");
                if (tag == null || url == null || !url.StartsWith("https://github.com/" + Program.UpdateRepo + "/")) return false;
                if (!Newer(tag, Program.Version)) { Clear(); return false; }
                // the release's zip: must come from this repo's own release downloads (or the local test feed)
                string asset = null, sha = null;
                if (o is System.Collections.Generic.Dictionary<string, object> d && d.TryGetValue("assets", out var a) && a is System.Collections.IEnumerable list)
                    foreach (var item in list)
                    {
                        var name = Program.Str(item, "name"); var dl = Program.Str(item, "browser_download_url");
                        if (name == null || dl == null || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!(dl.StartsWith(Downloads) || (TestFeed != null && dl.StartsWith("file:///")))) continue;
                        asset = dl;
                        var digest = Program.Str(item, "digest");
                        if (digest != null && digest.StartsWith("sha256:")) sha = digest.Substring(7);
                        break;
                    }
                LatestTag = tag; LatestUrl = url; AssetUrl = asset; AssetSha256 = sha;
                Program.Window?.ShowUpdate(tag, url, CanAutoUpdate);
                return true;
            }
        }

        /// Can Glassify replace its own files here? (Not if someone put it in Program Files.)
        static bool FolderWritable()
        {
            try { var t = Path.Combine(Program.Home, ".write-test"); File.WriteAllText(t, ""); File.Delete(t); return true; }
            catch { return false; }
        }

        /// The update button was clicked: download, verify, swap the files, restart. Runs off the UI thread.
        public static void Apply()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            var work = Path.Combine(Program.Data, "update");
            var swapped = new System.Collections.Generic.List<(string target, string old)>();
            try
            {
                // ask GitHub again first: the release offered at start-up may have been replaced or removed since
                bool newer;
                try { newer = Fetch(); }
                catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound) { newer = false; }
                if (!newer) { Clear(); Program.Log("update: already on the newest version"); Interlocked.Exchange(ref _busy, 0); return; }
                if (AssetUrl == null || !FolderWritable()) throw new InvalidOperationException("can't update in this folder");
                Program.Log("updating to " + LatestTag);
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                var zip = Path.Combine(work, "update.zip");
                Download(AssetUrl, zip);
                if (AssetSha256 != null && !string.Equals(Sha256(zip), AssetSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("the download didn't match GitHub's fingerprint");
                var unpacked = Path.Combine(work, "files");
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, unpacked);
                // the zip holds a "Glassify" folder (or the files directly)
                var src = File.Exists(Path.Combine(unpacked, "Glassify.exe")) ? unpacked
                        : Array.Find(Directory.GetDirectories(unpacked), x => File.Exists(Path.Combine(x, "Glassify.exe")));
                if (src == null) throw new InvalidDataException("the download has no Glassify.exe");

                // Windows lets a running program's files be renamed (not overwritten): move each old file aside as
                // *.old, put the new one in its place. The *.old files are deleted on the next start.
                foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(src.Length).TrimStart('\\');
                    if (rel.StartsWith("data\\", StringComparison.OrdinalIgnoreCase) || rel.Contains("..")) continue;   // never touch your login/settings
                    var target = Path.Combine(Program.Home, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string old = null;
                    if (File.Exists(target))
                    {
                        old = target + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(target, old);
                    }
                    swapped.Add((target, old));
                    File.Copy(file, target);
                }
                Program.Log("updated to " + LatestTag + ", restarting");
                try { Directory.Delete(work, true); } catch { }
                var w = Program.Window;
                w?.BeginInvoke(new Action(async () =>
                {
                    // keep the music going across the restart
                    try { await w.Eval("(()=>{const p=document.querySelector('[data-testid=control-button-playpause]'); if(p&&/pause/i.test(p.getAttribute('aria-label')||'')) localStorage.setItem('ms-resume',String(Date.now())); return ''})()"); } catch { }
                    Program.Restart();
                }));
            }
            catch (Exception ex)
            {
                Program.Log("update failed: " + ex.Message);
                // put every swapped file back the way it was
                for (int i = swapped.Count - 1; i >= 0; i--)
                {
                    var (target, old) = swapped[i];
                    try { if (File.Exists(target)) File.Delete(target); if (old != null) File.Move(old, target); } catch (Exception e2) { Program.Log("rollback: " + e2.Message); }
                }
                try { Directory.Delete(work, true); } catch { }
                Interlocked.Exchange(ref _busy, 0);
                Program.Window?.UpdateFailed(LatestUrl);
            }
        }

        /// On start-up: remove the old files left by the last update.
        public static void Cleanup()
        {
            try
            {
                DeleteOld(Program.Home);
                var work = Path.Combine(Program.Data, "update");
                if (Directory.Exists(work)) Directory.Delete(work, true);
            }
            catch { }
        }
        // program folders only: the data folder (your login; the browser keeps its own *.old files there) is skipped
        static void DeleteOld(string dir)
        {
            foreach (var f in Directory.GetFiles(dir, "*.old")) try { File.Delete(f); } catch { }
            foreach (var d in Directory.GetDirectories(dir))
                if (!string.Equals(d.TrimEnd('\\'), Program.Data.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) DeleteOld(d);
        }

        static void Download(string url, string to)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = WebRequest.Create(url);   // GitHub redirects to its download servers; that's followed automatically
            req.Timeout = 60000;
            if (req is HttpWebRequest h) { h.UserAgent = "Glassify-updater"; h.ReadWriteTimeout = 60000; }
            using (var resp = req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var f = File.Create(to)) s.CopyTo(f);
            if (new FileInfo(to).Length < 10000) throw new InvalidDataException("the download is too small");
        }

        static string Sha256(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var f = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
        }

        static bool Newer(string tag, string current)
        {
            Version Parse(string s) { var t = new StringBuilder(); foreach (var ch in s) if (char.IsDigit(ch) || ch == '.') t.Append(ch); try { return new Version(t.ToString().Trim('.')); } catch { return new Version(0, 0); } }
            return Parse(tag) > Parse(current);
        }
    }
}
