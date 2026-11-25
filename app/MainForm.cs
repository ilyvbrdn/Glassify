using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Glassify
{
    /// The main window: fully see-through and borderless, with Spotify's web player inside.
    /// Only the page's panels are drawn, so they float over whatever is behind the window.
    public class MainForm : Form
    {
        // Glass levels (4-5): see-through window. Solid levels (1-3): an ordinary opaque window (cheaper to draw).
        readonly WebView2 _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Program.Glass ? Color.Transparent : Color.FromArgb(18, 16, 22) };
        string _css = "", _js = "";
        bool _shuttingDown, _shutdownDone;
        static readonly string StateFile = Path.Combine(Program.Data, "window.txt");

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (Program.Glass) cp.ExStyle |= 0x00200000; // WS_EX_NOREDIRECTIONBITMAP: no window surface, so empty areas are see-through
                return cp;
            }
        }

        public MainForm()
        {
            Text = "Glassify";
            BackColor = Color.FromArgb(18, 16, 22);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(820, 520);
            try { using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico")) Icon = new Icon(s); } catch { }
            RestoreBounds_();
            Controls.Add(_web);
            Load += async (s, e) => { if (!_loadedEarly) { _loadedEarly = true; await Init(); } };
            HandleCreated += (s, e) => { try { File.WriteAllText(Path.Combine(Program.Data, "main.hwnd"), Handle.ToInt64().ToString()); } catch { } };
            FormClosing += async (s, e) =>
            {
                if (_shutdownDone) return;
                // Hide instantly, then let the browser engine shut down cleanly so your login is saved to disk.
                e.Cancel = true;
                if (_shuttingDown) return;
                _shuttingDown = true;
                try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
                SaveBounds();
                Hide();
                Mini.Close();
                int pid = 0;
                try { pid = (int)(_web.CoreWebView2?.BrowserProcessId ?? 0); } catch { }
                try { _web.Dispose(); } catch { }
                if (pid != 0)
                {
                    try { var p = Process.GetProcessById(pid); await Task.Run(() => p.WaitForExit(4000)); } catch { }
                }
                _shutdownDone = true;
                Close();
            };
            Resize += (s, e) => { if (WindowState == FormWindowState.Maximized) MaximizedBounds = Screen.FromControl(this).WorkingArea; };
        }

        async Task Init()
        {
            var cssFile = Path.Combine(Program.Home, "theme.css");
            _css = File.Exists(cssFile) ? File.ReadAllText(cssFile) : "";
            _js = PageScript.Build(Program.Json.Serialize(_css));

            var args = "--autoplay-policy=no-user-gesture-required --disable-features=msSmartScreenProtection,msEdgeShoppingAssistant,msWebOOUI,msPdfOOUI,Translate";
            if (File.Exists(Path.Combine(Program.Data, "debug"))) args += " --remote-debugging-port=9334";   // local-only inspection, off by default
            var opts = new CoreWebView2EnvironmentOptions(args);
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.Data, "webview"), opts);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsNonClientRegionSupportEnabled = false;  // no drag regions: they intercepted scrolling and clicks
            await core.AddScriptToExecuteOnDocumentCreatedAsync(_js);
            core.DOMContentLoaded += async (s, e) =>
            {
                // tell the page the app's version and current options (the settings panel shows them)
                var init = "window.__msVersion=" + Program.Json.Serialize(Program.Version) + ";window.__msOpts={game:" + B(Opts.GameMode) + ",ghost:" + B(Opts.Ghost) +
                           ",toast:" + B(Opts.Toast) + ",auto:" + B(Opts.AutoStart) + ",startmini:" + B(Opts.StartMini) + "};";
                try { await core.ExecuteScriptAsync(init); } catch { }
                try { await core.ExecuteScriptAsync(_js); } catch { }
                if (Updates.LatestTag != null) ShowUpdate(Updates.LatestTag, Updates.LatestUrl, Updates.CanAutoUpdate);
            };
            // Crash recovery: if Spotify's page crashes or freezes, reload it (your song resumes);
            // if the whole browser engine dies, restart Glassify.
            core.ProcessFailed += (s, e) =>
            {
                Program.Log("engine problem: " + e.ProcessFailedKind);
                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited) Program.Restart();
                        else if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited ||
                                 e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive) core.Reload();
                    }
                    catch (Exception ex) { Program.Log("recovery: " + ex.Message); }
                }));
            };
            core.WebMessageReceived += (s, e) => { try { OnMessage(e.TryGetWebMessageAsString()); } catch { } };
            core.DocumentTitleChanged += (s, e) => Text = core.DocumentTitle;
            // Sign-in popups (Facebook/Google/Apple) get their own normal window so they can hand the login back.
            core.NewWindowRequested += async (s, e) =>
            {
                var deferral = e.GetDeferral();
                try
                {
                    var popup = new Form { Text = "Sign in", Width = 520, Height = 720, StartPosition = FormStartPosition.CenterScreen, Icon = Icon, BackColor = Color.White };
                    var pv = new WebView2 { Dock = DockStyle.Fill };
                    popup.Controls.Add(pv);
                    popup.Show(this);
                    await pv.EnsureCoreWebView2Async(env);
                    pv.CoreWebView2.WindowCloseRequested += (s2, e2) => popup.Close();
                    pv.CoreWebView2.DocumentTitleChanged += (s2, e2) => popup.Text = pv.CoreWebView2.DocumentTitle;
                    e.NewWindow = pv.CoreWebView2;
                    e.Handled = true;
                }
                finally { deferral.Complete(); }
            };
            // Full speed while you're looking at it; low priority only while minimized (so games come first).
            Resize += (s, e) => { ApplyPower(); OnMinimized(); };
            core.Navigate("https://open.spotify.com/");
            Updates.Start();
            if (_startHidden) { ApplyPower(); Mini.ShowOnly(); }
        }

        // Started with Windows + "start in mini player": keep the main window hidden from the very first moment.
        bool _startHidden = Program.StartedWithWindows && Opts.StartMini;
        protected override void SetVisibleCore(bool value)
        {
            if (_startHidden && value && !IsHandleCreated) { CreateHandle(); _hiddenToMini = true; value = false; Load_(); }
            base.SetVisibleCore(value);
        }
        bool _loadedEarly;
        void Load_() { if (_loadedEarly) return; _loadedEarly = true; BeginInvoke(new Action(async () => await Init())); }

        public void HideToMini()
        {
            _hiddenToMini = true;
            Hide(); ApplyPower(); Mini.ShowOnly();
        }

        public void RefreshPower() => ApplyPower();
        static string B(bool b) => b ? "true" : "false";

        public void ShowUpdate(string tag, string url, bool auto) => Run("window.__ms && window.__ms.showUpdate(" + Program.Json.Serialize(tag) + "," + Program.Json.Serialize(url) + "," + B(auto) + ")");

        public void HideUpdate() => Run("window.__ms && window.__ms.hideUpdate && window.__ms.hideUpdate()");

        /// One-click update didn't work: say so on the button and open the download page instead.
        public void UpdateFailed(string url)
        {
            Run("window.__ms && window.__ms.updateFailed && window.__ms.updateFailed()");
            if (url != null && url.StartsWith("https://github.com/" + Program.UpdateRepo + "/")) try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        void Run(string js)
        {
            try { BeginInvoke(new Action(async () => { try { await _web.CoreWebView2.ExecuteScriptAsync(js); } catch { } })); } catch { }
        }

        void OnMessage(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (msg == "mini") { Mini.Toggle(); return; }
            if (msg.StartsWith("power:"))
            {
                // e.g. "power:eco=1,lvl=2,mini=0"
                foreach (var kv in msg.Substring(6).Split(','))
                {
                    var p = kv.Split('=');
                    if (p.Length != 2) continue;
                    if (p[0] == "eco") Eco = p[1] == "1";
                    else if (p[0] == "lvl" && int.TryParse(p[1], out var m)) { MemLevel = m; Program.SaveLevel(m); }
                    else if (p[0] == "mini") MiniOnMinimize = p[1] == "1";
                }
                ApplyPower();
                Mini.SetEco(Eco || MemLevel <= 2);
                return;
            }
            if (msg == "restart") { Program.Restart(); return; }
            if (msg == "update") { System.Threading.ThreadPool.QueueUserWorkItem(_ => Updates.Apply()); return; }
            if (msg.StartsWith("opts:")) { Opts.Apply(msg.Substring(5)); return; }
            if (msg.StartsWith("track:"))
            {
                // song changed: show the little popup if it's switched on and you're not looking at Glassify
                if (!Opts.Toast || (Visible && GetForegroundWindow() == Handle)) return;
                try
                {
                    var o = Program.Json.DeserializeObject(msg.Substring(6));
                    Mini.ShowToast(Program.Str(o, "t"), Program.Str(o, "a"), Program.Str(o, "art"));
                }
                catch { }
                return;
            }
            if (msg.StartsWith("open:"))
            {
                // only ever opens this app's own GitHub release pages
                var url = msg.Substring(5);
                if (url.StartsWith("https://github.com/" + Program.UpdateRepo + "/")) try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                return;
            }
            if (msg == "min") { WindowState = FormWindowState.Minimized; return; }
            if (msg == "max") { MaximizedBounds = Screen.FromControl(this).WorkingArea; WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; return; }
            if (msg == "close") { Close(); return; }
            if (msg == "drag")
            {
                if (WindowState == FormWindowState.Maximized) return;
                ReleaseCapture();
                SendMessage(Handle, 0x0112 /*WM_SYSCOMMAND*/, (IntPtr)0xF012 /*SC_MOVE|HTCAPTION*/, IntPtr.Zero);
                return;
            }
            if (msg.StartsWith("size:") && int.TryParse(msg.Substring(5), out int edge) && WindowState == FormWindowState.Normal)
            {
                ReleaseCapture();
                SendMessage(Handle, 0x0112, (IntPtr)(0xF000 + edge) /*SC_SIZE + WMSZ_*/, IntPtr.Zero);
            }
        }

        /// Eco mode (from the gear menu): always low priority + memory saver. Otherwise only while minimized.
        public static volatile bool Eco, MiniOnMinimize;
        public static volatile int MemLevel = Program.Level;   // 5 Glass .. 1 Minimal
        void ApplyPower()
        {
            bool away = WindowState == FormWindowState.Minimized || !Visible || GameMode.Active;
            bool lowCpu = Eco || MemLevel <= 2 || away;
            bool lowMem = Eco || MemLevel <= 2 || away;
            SetPriority(lowCpu ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal);
            try { _web.CoreWebView2.MemoryUsageTargetLevel = lowMem ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal; } catch { }
            UpdateTray();
        }

        // While the main window is hidden (minimized to the mini player, game mode, started in the mini player) there's no
        // taskbar button, so a tray icon by the clock shows Glassify is still running: click to open, right-click for more.
        NotifyIcon _tray;
        void UpdateTray()
        {
            bool want = !Visible && !_shuttingDown;
            if (want && _tray == null)
            {
                var menu = new ContextMenuStrip();
                menu.Items.Add("Open Glassify", null, (s, e) => BringBack());
                menu.Items.Add("Show / hide mini player", null, (s, e) => Mini.Toggle());
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Quit Glassify", null, (s, e) => Close());
                _tray = new NotifyIcon { Icon = Icon, Text = "Glassify (click to open)", ContextMenuStrip = menu };
                _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) BringBack(); };
                _tray.BalloonTipClicked += (s, e) => BringBack();
            }
            if (_tray == null || _tray.Visible == want) return;
            _tray.Visible = want;
            // the first time ever, say where Glassify went
            var flag = Path.Combine(Program.Data, "tray-tip.txt");
            if (want && !File.Exists(flag))
            {
                try { File.WriteAllText(flag, "shown"); } catch { }
                _tray.ShowBalloonTip(6000, "Glassify is still running", "It's in the mini player and here by the clock. Click this icon to open Glassify, or right-click to quit.", ToolTipIcon.None);
            }
        }

        // "Minimize to mini player": hide the main window completely and show only the mini player.
        bool _hiddenToMini;
        void OnMinimized()
        {
            if (WindowState != FormWindowState.Minimized) { _hiddenToMini = false; return; }
            if (!MiniOnMinimize || _hiddenToMini) return;
            _hiddenToMini = true;
            BeginInvoke(new Action(() => { Hide(); ApplyPower(); Mini.ShowOnly(); }));
        }

        ProcessPriorityClass _prio = ProcessPriorityClass.Normal;
        void SetPriority(ProcessPriorityClass p)
        {
            if (p == _prio) return;
            _prio = p;
            try { Process.GetCurrentProcess().PriorityClass = p; } catch { }
            // The page's own processes (browser, renderer, GPU, audio) all belong to the WebView2 browser process.
            // WebView2 lists its own processes (browser, renderer, GPU, audio...) instantly.
            try
            {
                foreach (CoreWebView2ProcessInfo info in _web.CoreWebView2.Environment.GetProcessInfos())
                    try { Process.GetProcessById(info.ProcessId).PriorityClass = p; } catch { }
            }
            catch { }
        }

        public Task<string> Eval(string js)
        {
            var tcs = new TaskCompletionSource<string>();
            try
            {
                BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        if (_web.CoreWebView2 == null) { tcs.TrySetResult(null); return; }
                        var raw = await _web.CoreWebView2.ExecuteScriptAsync(js);
                        tcs.TrySetResult(raw == null || raw == "null" ? null : Program.Json.DeserializeObject(raw) as string ?? raw);
                    }
                    catch { tcs.TrySetResult(null); }
                }));
            }
            catch { tcs.TrySetResult(null); }
            return tcs.Task;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.WM_BRINGBACK) { BringBack(); return; }
            base.WndProc(ref m);
            // Shown by something other than Glassify (e.g. another tool restoring a window hidden to the mini player):
            // WinForms still thinks it is hidden, which would keep low priority and ignore maximize. Resync.
            if (m.Msg == 0x0047 /* WM_WINDOWPOSCHANGED */ && !Visible && IsHandleCreated
                && (Marshal.ReadInt32(m.LParam, IntPtr.Size * 2 + 16) & 0x0040 /* SWP_SHOWWINDOW */) != 0)
                BeginInvoke(new Action(() => { if (!Visible) BringBack(activate: false); }));
        }

        public void BringBack() => BringBack(true);
        public void BringBack(bool activate)
        {
            _hiddenToMini = false;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (activate) Activate();
            ApplyPower();
        }

        void RestoreBounds_()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            Bounds = new Rectangle(wa.X + (wa.Width - 1200) / 2, wa.Y + (wa.Height - 780) / 2, 1200, 780);
            try
            {
                var p = File.ReadAllText(StateFile).Split(',');
                var r = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
                foreach (var s in Screen.AllScreens) if (s.WorkingArea.IntersectsWith(r)) { Bounds = r; break; }
                if (p.Length > 4 && p[4] == "max") WindowState = FormWindowState.Maximized;
            }
            catch { }
        }

        void SaveBounds()
        {
            try
            {
                var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                File.WriteAllText(StateFile, string.Join(",", b.X, b.Y, b.Width, b.Height, WindowState == FormWindowState.Maximized ? "max" : "normal"));
            }
            catch { }
        }

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    }
}
