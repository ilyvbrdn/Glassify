using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Glassify
{
    static class Program
    {
        public const string Version = "1.0.8";
        public const string UpdateRepo = "ilyvbrdn/Glassify";

        public static readonly string Home = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string Data = Path.Combine(Home, "data");
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        public static MainForm Window;
        public static bool StartedWithWindows;

        /// Look level chosen in settings (5 Glass .. 2 Light). Read before the window exists, because glass vs
        /// solid is decided when the window is created.
        public static readonly int Level = ReadLevel();
        public static bool Glass => Level >= 4;
        static int ReadLevel() { try { return Math.Max(2, Math.Min(5, int.Parse(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "level.txt")).Trim()))); } catch { return 5; } }
        public static void SaveLevel(int l) { try { File.WriteAllText(Path.Combine(Data, "level.txt"), l.ToString()); } catch { } }

        /// Quick restart to apply a look change: start a fresh copy that waits for this one to close, then close.
        public static void Restart()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Home, "Glassify.exe"), "--after=" + System.Diagnostics.Process.GetCurrentProcess().Id) { UseShellExecute = false }); } catch (Exception ex) { Log("restart: " + ex.Message); return; }
            Window?.Close();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            // Set the display-scaling mode before any window exists. Otherwise the mini player's WPF thread sets it
            // part way through the main window's creation, and the page ends up on a different scale than its window
            // (laid out 125% too big at 125% scaling: right side and player bar cut off).
            try { SetProcessDPIAware(); } catch { }
            Directory.CreateDirectory(Data);
            foreach (var a in args)
            {
                // Started by Restart(): wait for the previous copy to finish closing (it saves your login on the way out).
                if (a.StartsWith("--after=") && int.TryParse(a.Substring(8), out int prev))
                    try { System.Diagnostics.Process.GetProcessById(prev).WaitForExit(15000); } catch { }
                if (a == "--startup") StartedWithWindows = true;
            }
            using (var m = new Mutex(true, "Glassify.App", out bool first))
            {
                if (!first) { FocusOther(); return; }
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log("crash: " + e.ExceptionObject);
                System.Windows.Forms.Application.ThreadException += (s, e) => Log("ui: " + e.Exception);
                System.Windows.Forms.Application.EnableVisualStyles();
                Updates.Cleanup();   // old files left by a one-click update
                Opts.Load();
                Window = new MainForm();
                Mini.Init();
                if (Opts.Bar && Opts.BarShown) Bar.Show();   // the bar was on screen last time: bring it back
                StartHotkeys();
                GameMode.Start();
                System.Windows.Forms.Application.Run(Window);
                Mini.Close();
            }
        }

        /// Runs JS in the Spotify page from any thread; returns the result as a plain string (or null).
        public static Task<string> Eval(string js) => Window?.Eval(js) ?? Task.FromResult<string>(null);

        public static void FocusExisting() => Window?.BeginInvoke(new Action(() => Window.BringBack()));

        static void FocusOther()
        {
            try
            {
                // Ask the running copy to bring its window back (works even when it's hidden to the mini player).
                var h = new IntPtr(long.Parse(File.ReadAllText(Path.Combine(Data, "main.hwnd"))));
                if (h != IntPtr.Zero) { PostMessage(h, WM_BRINGBACK, IntPtr.Zero, IntPtr.Zero); SetForegroundWindow(h); }
            }
            catch { }
        }

        public static string Str(object o, params string[] path)
        {
            foreach (var k in path) { if (!(o is Dictionary<string, object> d) || !d.TryGetValue(k, out o)) return null; }
            return o as string ?? o?.ToString();
        }

        public static void Log(string s) { try { File.AppendAllText(Path.Combine(Data, "app.log"), DateTime.Now + "  " + s + "\r\n"); } catch { } }

        // Global keys (work inside games too):
        //   Ctrl+Alt+M mini player   Ctrl+Alt+G ghost mini player   Ctrl+Alt+L like song
        //   Ctrl+Alt+Up / Down volume   media keys play/pause, next, previous
        public static readonly string[] HotkeyHelp = {
            "Ctrl+Alt+M Â· mini player", "Ctrl+Alt+G Â· ghost mini player (click-through)", "Ctrl+Alt+L Â· like song",
            "Ctrl+Alt+â†‘ / â†“ Â· volume", "Media keys Â· play/pause, next, previous" };
        static void StartHotkeys()
        {
            var t = new Thread(() =>
            {
                const uint CA = 0x0001 | 0x0002 | 0x4000; // Alt + Ctrl, no auto-repeat
                var keys = new (int id, uint mods, uint vk, string name)[] {
                    (1, CA, 0x4D, "Ctrl+Alt+M"), (2, 0x4000, 0xB3, "Play/Pause"), (3, 0x4000, 0xB0, "Next"), (4, 0x4000, 0xB1, "Previous"),
                    (5, 0x0001 | 0x0002, 0x26, "Ctrl+Alt+Up"), (6, 0x0001 | 0x0002, 0x28, "Ctrl+Alt+Down"),
                    (7, CA, 0x4C, "Ctrl+Alt+L"), (8, CA, 0x47, "Ctrl+Alt+G"), (9, CA, 0x42, "Ctrl+Alt+B") };
                foreach (var k in keys)
                {
                    // retry briefly: a previous Glassify may still be releasing its keys while it shuts down
                    bool ok = false;
                    for (int i = 0; i < 20 && !(ok = RegisterHotKey(IntPtr.Zero, k.id, k.mods, k.vk)); i++) Thread.Sleep(500);
                    if (!ok) Log("hotkey " + k.name + " is in use by another app");
                }
                while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (msg.message != 0x0312) continue;
                    switch (msg.wParam.ToInt32())
                    {
                        case 1: Mini.Toggle(); break;
                        case 2: _ = Eval("window.__ms && __ms.click('control-button-playpause')"); break;
                        case 3: _ = Eval("window.__ms && __ms.click('control-button-skip-forward')"); break;
                        case 4: _ = Eval("window.__ms && __ms.click('control-button-skip-back')"); break;
                        case 5: _ = Eval("window.__ms && __ms.volume(+0.1)"); break;
                        case 6: _ = Eval("window.__ms && __ms.volume(-0.1)"); break;
                        case 7: _ = Eval("window.__ms && __ms.like()"); break;
                        case 8: Mini.ToggleGhost(); break;
                        case 9: Bar.Toggle(); break;
                    }
                }
            }) { IsBackground = true, Name = "Hotkeys" };
            t.Start();
        }

        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr h, uint min, uint max);
        public const int WM_BRINGBACK = 0x8000 + 0x4D53; // WM_APP + 'MS'
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    }

    /// App options set from the settings panel, saved so they work before the page loads (start-up, game mode).
    static class Opts
    {
        public static volatile bool GameMode = true, Ghost = true, Toast = false, AutoStart = false, StartMini = false, Bar = false, BarShown = false;
        /// Bar position: "top", "bottom", "left" or "right"; and its screen (Windows device name, "" = main screen).
        public static volatile string BarEdge = "top", BarMonitor = "";
        static string FilePath => Path.Combine(Program.Data, "opts.txt");
        public static void Load()
        {
            try
            {
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var p = line.Split('='); if (p.Length != 2) continue;
                    Set(p[0].Trim(), p[1].Trim());
                }
            }
            catch { }
        }
        static void Set(string k, string s)
        {
            bool v = s == "1";
            switch (k)
            {
                case "game": GameMode = v; break; case "ghost": Ghost = v; break; case "toast": Toast = v; break; case "auto": AutoStart = v; break;
                case "startmini": StartMini = v; break; case "bar": Bar = v; break; case "barshown": BarShown = v; break;
                case "barbottom": if (v) BarEdge = "bottom"; break;   // older setting
                case "baredge": if (s == "top" || s == "bottom" || s == "left" || s == "right") BarEdge = s; break;
                case "barmon": BarMonitor = s ?? ""; break;
            }
        }
        public static void Save()
        {
            try { File.WriteAllText(FilePath, $"game={B(GameMode)}\r\nghost={B(Ghost)}\r\ntoast={B(Toast)}\r\nauto={B(AutoStart)}\r\nstartmini={B(StartMini)}\r\nbar={B(Bar)}\r\nbaredge={BarEdge}\r\nbarmon={BarMonitor}\r\nbarshown={B(BarShown)}\r\n"); } catch { }
        }
        static string B(bool b) => b ? "1" : "0";
        /// From the page: "opts:game=1,ghost=1,toast=0,auto=0,startmini=0"
        public static void Apply(string s)
        {
            foreach (var kv in s.Split(','))
            {
                int i = kv.IndexOf('='); if (i < 1) continue;
                Set(kv.Substring(0, i), kv.Substring(i + 1));
            }
            Save();
            SetAutoStart(AutoStart);
            if (!GameMode || !Ghost) Mini.SetGhost(false, auto: true);
            Glassify.Bar.Sync();
        }

        /// Start with Windows: a normal per-user startup entry (no admin, removable from Task Manager > Startup apps).
        static void SetAutoStart(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (on) k.SetValue("Glassify", "\"" + Path.Combine(Program.Home, "Glassify.exe") + "\" --startup");
                    else if (k.GetValue("Glassify") != null) k.DeleteValue("Glassify");
                }
            }
            catch (Exception ex) { Program.Log("autostart: " + ex.Message); }
        }
    }
}
