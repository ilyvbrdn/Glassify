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
            "microsoft.media.player", "netflix", "powerpnt", "explorer", "msedgewebview2" };
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
    static class Updates
    {
        static Timer _timer;
        public static string LatestTag, LatestUrl;

        public static void Start() => _timer = new Timer(_ => Check(), null, 20000, 24 * 3600 * 1000);

        static void Check()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Program.UpdateRepo + "/releases/latest");
                req.UserAgent = "Glassify-update-check"; req.Accept = "application/vnd.github+json"; req.Timeout = 15000;
                string body;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream())) body = sr.ReadToEnd();
                var o = Program.Json.DeserializeObject(body);
                var tag = Program.Str(o, "tag_name"); var url = Program.Str(o, "html_url");
                if (tag == null || url == null || !url.StartsWith("https://github.com/" + Program.UpdateRepo + "/")) return;
                if (Newer(tag, Program.Version))
                {
                    LatestTag = tag; LatestUrl = url;
                    Program.Window?.ShowUpdate(tag, url);
                }
            }
            catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound) { /* no releases yet */ }
            catch (Exception ex) { Program.Log("update check: " + ex.Message); }
        }

        static bool Newer(string tag, string current)
        {
            Version Parse(string s) { var t = new StringBuilder(); foreach (var ch in s) if (char.IsDigit(ch) || ch == '.') t.Append(ch); try { return new Version(t.ToString().Trim('.')); } catch { return new Version(0, 0); } }
            return Parse(tag) > Parse(current);
        }
    }
}
