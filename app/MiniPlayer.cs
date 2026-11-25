using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Glassify
{
    /// Always-on-top glass mini player. Created once (hidden) at startup on its own UI thread;
    /// Toggle just shows/hides it, so it can be called from any thread at any time without races.
    static class Mini
    {
        static MiniWindow _win;
        static Dispatcher _disp;
        static readonly ManualResetEventSlim _ready = new ManualResetEventSlim();

        public static void Init()
        {
            var t = new Thread(() =>
            {
                try
                {
                    _disp = Dispatcher.CurrentDispatcher;
                    _disp.UnhandledException += (s, e) => { Program.Log("mini: " + e.Exception); e.Handled = true; };
                    _win = new MiniWindow();
                }
                catch (Exception ex) { Program.Log("mini init: " + ex); }
                finally { _ready.Set(); }
                Dispatcher.Run();
            }) { IsBackground = true, Name = "MiniPlayer" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        /// Eco mode: check the song every 3 s instead of every second (progress bar still moves smoothly).
        public static void SetEco(bool eco)
        {
            if (!_ready.Wait(3000) || _disp == null) return;
            _disp.BeginInvoke(new Action(() => _win?.SetInterval(eco ? 3000 : 1000)));
        }

        /// Show the mini player (no toggle): used when the main window is hidden to it.
        public static void ShowOnly()
        {
            if (!_ready.Wait(3000) || _disp == null || _win == null) return;
            _disp.BeginInvoke(new Action(() => { try { if (!_win.IsVisible && !_win.Ghost) _win.ShowMini(); } catch (Exception ex) { Program.Log("mini show: " + ex); } }));
        }

        public static void Toggle()
        {
            if (!_ready.Wait(3000) || _disp == null || _win == null) { Program.Log("mini toggle: not ready"); return; }
            _disp.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_win.Ghost) { _ghostManual = false; _win.SetGhost(false); _win.Hide(); }   // ghost showing: Ctrl+Alt+M hides it
                    else if (_win.IsVisible) _win.Hide(); else _win.ShowMini();
                }
                catch (Exception ex) { Program.Log("mini show: " + ex); }
            }));
        }

        public static void Close() { try { _disp?.BeginInvoke(new Action(() => { _win?.Close(); _toast?.Close(); })); } catch { } }

        public static volatile bool IsShown;
        public static void HideOnly()
        {
            if (!_ready.Wait(3000) || _disp == null || _win == null) return;
            _disp.BeginInvoke(new Action(() => { try { if (_win.Ghost) _win.SetGhost(false); _win.Hide(); } catch { } }));
        }

        // ---- ghost: semi-transparent and click-through, so it never blocks a click in your game ----
        static bool _ghostManual;
        public static void SetGhost(bool on, bool auto)
        {
            if (!_ready.Wait(3000) || _disp == null || _win == null) return;
            if (auto && _ghostManual) return;        // you toggled it yourself with Ctrl+Alt+G: leave it be
            _disp.BeginInvoke(new Action(() => _win.SetGhost(on)));
        }
        public static void ToggleGhost()
        {
            if (!_ready.Wait(3000) || _disp == null || _win == null) return;
            _disp.BeginInvoke(new Action(() =>
            {
                bool on = !_win.Ghost;
                _ghostManual = on;                   // manual ghost sticks until you turn it off again
                _win.SetGhost(on);
            }));
        }

        // ---- song-change popup ----
        static ToastWindow _toast;
        public static void ShowToast(string title, string artist, string art)
        {
            if (!_ready.Wait(3000) || _disp == null || string.IsNullOrEmpty(title)) return;
            _disp.BeginInvoke(new Action(() =>
            {
                try { if (_toast == null) _toast = new ToastWindow(); _toast.Pop(title, artist, art); }
                catch (Exception ex) { Program.Log("toast: " + ex.Message); }
            }));
        }
    }

    /// The ghost twin of the mini player: same spot and size, see-through, and every click passes through it.
    /// Shows the cover, song and progress; refreshes itself every 2 seconds.
    class GhostWindow : Window
    {
        readonly Image _art = new Image { Stretch = Stretch.UniformToFill };
        readonly TextBlock _t = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _a = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 255, 255, 255)), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        readonly Border _fill = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Background = Brushes.White };
        readonly Grid _bar = new Grid { Height = 3, Margin = new Thickness(0, 10, 0, 0) };
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        readonly Grid _root;
        double _pos, _dur; bool _playing; DateTime _at = DateTime.UtcNow; string _artUrl;

        public GhostWindow()
        {
            Title = "Glassify ghost"; AllowsTransparency = true; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false; Opacity = 0.62;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            var card = new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x12, 0x12, 0x18)) };
            var g = new Grid { Margin = new Thickness(12) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var artBox = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(12), Child = _art };
            _art.Clip = new RectangleGeometry(new Rect(0, 0, 84, 84), 12, 12);
            g.Children.Add(artBox);
            var sp = new StackPanel { Margin = new Thickness(14, 6, 0, 0) };
            _bar.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)) });
            _bar.Children.Add(_fill);
            sp.Children.Add(_t); sp.Children.Add(_a); sp.Children.Add(_bar);
            sp.Children.Add(new TextBlock { Text = "Ghost mode · Ctrl+Alt+G to turn off", FontSize = 10, Margin = new Thickness(0, 12, 0, 0), Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)) });
            Grid.SetColumn(sp, 1); g.Children.Add(sp);
            card.Child = g;
            Content = _root = new Grid { Children = { card } };
            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                // tool window (no Alt+Tab), click-through, never takes focus
                SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x80 | 0x20 | 0x08000000);
            };
            _timer.Tick += async (s, e) => await Refresh();
            _bar.SizeChanged += (s, e) => Draw();
        }

        public void ShowOver(Window mini, string title, string artist, ImageSource art, Brush accent)
        {
            Left = mini.Left; Top = mini.Top; Width = mini.Width; Height = mini.Height;
            _root.LayoutTransform = new ScaleTransform(mini.Width / 372, mini.Height / 108);
            _t.Text = title; _a.Text = artist; _art.Source = art; if (accent != null) _fill.Background = accent;
            Show();
            _timer.Start(); _ = Refresh();
            IsVisibleChanged -= OnVis; IsVisibleChanged += OnVis;
        }
        void OnVis(object s, DependencyPropertyChangedEventArgs e) { if (!IsVisible) _timer.Stop(); }

        async System.Threading.Tasks.Task Refresh()
        {
            try
            {
                var json = await Program.Eval("window.__ms ? window.__ms.state() : ''");
                if (!string.IsNullOrEmpty(json) && Program.Json.DeserializeObject(json) is Dictionary<string, object> st)
                {
                    var t = Program.Str(st, "t"); if (!string.IsNullOrEmpty(t)) { _t.Text = t; _a.Text = Program.Str(st, "a"); }
                    _playing = st.TryGetValue("playing", out var p) && p is bool pb && pb;
                    var art = Program.Str(st, "art");
                    if (!string.IsNullOrEmpty(art) && art != _artUrl)
                    {
                        _artUrl = art;
                        try { var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(art); bi.DecodePixelWidth = 168; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); _art.Source = bi; } catch { }
                    }
                    double S(string x) { double tot = 0; if (string.IsNullOrEmpty(x)) return 0; foreach (var part in x.Split(':')) { if (!double.TryParse(part, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return 0; tot = tot * 60 + v; } return tot; }
                    var dur = S(Program.Str(st, "dur")); if (dur > 0) { _dur = dur; _pos = S(Program.Str(st, "pos")); _at = DateTime.UtcNow; }
                }
            }
            catch { }
            Draw();
        }
        void Draw()
        {
            if (_dur <= 0 || _bar.ActualWidth <= 0) return;
            var pos = _pos + (_playing ? (DateTime.UtcNow - _at).TotalSeconds : 0);
            _fill.Width = Math.Max(0, Math.Min(1, pos / _dur)) * _bar.ActualWidth;
        }
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    }

    /// Small glass popup with the cover and song name, bottom-right, for 4 seconds. Never takes focus or clicks.
    class ToastWindow : Window
    {
        readonly Image _art = new Image { Stretch = Stretch.UniformToFill, Width = 52, Height = 52 };
        readonly TextBlock _t = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _a = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255)), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        readonly DispatcherTimer _hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        public ToastWindow()
        {
            Title = "Glassify now playing"; Width = 320; Height = 76;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
            Background = Brushes.Transparent; FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            WindowChrome.SetWindowChrome(this, new WindowChrome { GlassFrameThickness = new Thickness(-1), CaptionHeight = 0, ResizeBorderThickness = new Thickness(0) });
            var g = new Grid { Margin = new Thickness(12), Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            _art.Clip = new RectangleGeometry(new Rect(0, 0, 52, 52), 8, 8);
            g.Children.Add(_art);
            var sp = new StackPanel { Margin = new Thickness(12, 4, 0, 0) };
            sp.Children.Add(new TextBlock { Text = "Now playing", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)) });
            sp.Children.Add(_t); sp.Children.Add(_a);
            Grid.SetColumn(sp, 1); g.Children.Add(sp);
            Content = new Border { Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), Child = g };
            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                int v = 1; DwmSetWindowAttribute(hwnd, 20, ref v, 4); v = 3; DwmSetWindowAttribute(hwnd, 38, ref v, 4); v = 2; DwmSetWindowAttribute(hwnd, 33, ref v, 4);
                SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x80 | 0x20 | 0x08000000); // tool window, click-through, never activate
            };
            _hide.Tick += (s, e) => { _hide.Stop(); Hide(); };
        }
        public void Pop(string title, string artist, string art)
        {
            _t.Text = title; _a.Text = artist ?? "";
            try { if (!string.IsNullOrEmpty(art)) { var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(art); bi.DecodePixelWidth = 104; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); _art.Source = bi; } } catch { }
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 16; Top = wa.Top + 16;
            Show();
            _hide.Stop(); _hide.Start();
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    }

    class MiniWindow : Window
    {
        readonly Image _art = new Image { Stretch = Stretch.UniformToFill };
        readonly TextBlock _title = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _artist = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xB8, 255, 255, 255)), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        readonly Border _barFill = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Width = 0 };
        // taller invisible hit area so the 3px bar is easy to click
        readonly Grid _bar = new Grid { Height = 13, Margin = new Thickness(0, 4, 0, -1), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Click to jump" };
        Button _like;
        bool _liked;
        readonly Button _play;
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        string _artUrl;
        double _posS, _durS; bool _playing; DateTime _at = DateTime.UtcNow;
        static readonly string PosFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "mini.json");
        const string Icons = "Segoe Fluent Icons, Segoe MDL2 Assets";

        public MiniWindow()
        {
            Title = "Glassify mini player";
            Width = 372; Height = 108;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            UseLayoutRounding = true;
            WindowChrome.SetWindowChrome(this, new WindowChrome { GlassFrameThickness = new Thickness(-1), CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), UseAeroCaptionButtons = false });

            var root = _root = new Grid { Background = new LinearGradientBrush(Color.FromArgb(0x66, 0xFF, 0x5F, 0xA8), Color.FromArgb(0x14, 0x60, 0x2C, 0x4E), 35) };
            var sheen = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                BorderBrush = new LinearGradientBrush(Color.FromArgb(0x70, 255, 190, 225), Color.FromArgb(0x14, 255, 150, 205), 90), IsHitTestVisible = false };
            var content = new Grid { Margin = new Thickness(12) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var artBox = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)), Cursor = Cursors.Hand, ToolTip = "Open Glassify" };
            _art.Clip = new RectangleGeometry(new Rect(0, 0, 84, 84), 12, 12);
            artBox.Child = _art;
            // Handle the click on press, before the window's drag-to-move grabs the mouse and swallows it.
            artBox.MouseLeftButtonDown += (s, e) => { e.Handled = true; Program.FocusExisting(); };
            content.Children.Add(artBox);

            var right = new Grid { Margin = new Thickness(14, 2, 0, 0) };
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _title.Text = "Glassify"; _artist.Text = "Nothing playing";
            Grid.SetRow(_artist, 1);
            _bar.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255)) });
            _bar.Children.Add(_barFill);
            Grid.SetRow(_bar, 2);
            // click anywhere on the bar to jump there in the song
            _bar.MouseLeftButtonDown += async (s, e) =>
            {
                e.Handled = true;
                double frac = Math.Max(0, Math.Min(1, e.GetPosition(_bar).X / Math.Max(1, _bar.ActualWidth)));
                if (_durS > 0) { _posS = frac * _durS; _at = DateTime.UtcNow; DrawBar(); }
                try { await Program.Eval("window.__ms && window.__ms.seek(" + frac.ToString(CultureInfo.InvariantCulture) + ")"); } catch { }
                await Task.Delay(400); await Refresh();
            };

            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-6, 0, 0, -4) };
            controls.Children.Add(IconButton("", 15, 34, () => Click("control-button-skip-back"), "Previous"));
            _play = IconButton("", 16, 36, () => { _playing = !_playing; UpdatePlay(); Click("control-button-playpause"); }, "Play/pause", true);
            controls.Children.Add(_play);
            controls.Children.Add(IconButton("", 15, 34, () => Click("control-button-skip-forward"), "Next"));
            Grid.SetRow(controls, 3);

            // right side of the controls row: size and open-the-main-player
            var extras = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -4, -2) };
            _like = IconButton("", 13, 28, async () =>
            {
                if (_liked) { Program.FocusExisting(); return; }   // already liked: open the main player to manage it
                _liked = true; UpdateLike();
                try { await Program.Eval("window.__ms && window.__ms.like()"); } catch { }
                await Task.Delay(600); await Refresh();
            }, "Like (save to Liked Songs)");
            extras.Children.Add(_like);
            extras.Children.Add(IconButton("", 12, 28, CycleSize, "Size: small / medium / large"));
            extras.Children.Add(IconButton("", 12, 28, () => Program.FocusExisting(), "Open the main player"));
            Grid.SetRow(extras, 3);

            right.Children.Add(_title); right.Children.Add(_artist); right.Children.Add(_bar); right.Children.Add(controls); right.Children.Add(extras);
            Grid.SetColumn(right, 1);
            content.Children.Add(right);

            var close = IconButton("", 10, 24, Hide, "Hide mini player");
            close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top; close.Margin = new Thickness(0, 6, 6, 0);
            close.Opacity = 0;

            root.Children.Add(content); root.Children.Add(sheen); root.Children.Add(close);
            root.MouseEnter += (s, e) => close.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
            root.MouseLeave += (s, e) => close.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
            root.MouseLeftButtonDown += (s, e) => { try { DragMove(); SavePos(); } catch { } };
            // mouse wheel anywhere on the mini player = volume
            root.MouseWheel += async (s, e) =>
            {
                e.Handled = true;
                try { await Program.Eval("window.__ms && window.__ms.volume(" + (e.Delta > 0 ? "0.05" : "-0.05") + ")"); } catch { }
            };
            Content = root;

            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                int v = 1; DwmSetWindowAttribute(hwnd, 20, ref v, 4);   // dark
                v = 3; DwmSetWindowAttribute(hwnd, 38, ref v, 4);       // acrylic glass
                v = 2; DwmSetWindowAttribute(hwnd, 33, ref v, 4);       // rounded corners
                // no Alt+Tab entry
                SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x80);
            };
            _timer.Tick += async (s, e) => await Refresh();
            IsVisibleChanged += (s, e) => { Mini.IsShown = IsVisible; if (IsVisible) { _timer.Start(); _ = Refresh(); } else _timer.Stop(); };
            _barFill.SizeChanged += (s, e) => { };
            _bar.SizeChanged += (s, e) => DrawBar();
        }

        public void SetInterval(int ms) => _timer.Interval = TimeSpan.FromMilliseconds(ms);

        // ---- ghost mode: see-through-ish and click-through (clicks go to the game underneath) ----
        // Windows won't make the glass (acrylic) mini player click-through, so in ghost mode it swaps itself for a
        // plain see-through twin that clicks pass straight through, and swaps back when ghost mode ends.
        public bool Ghost { get; private set; }
        GhostWindow _ghost;
        public void SetGhost(bool on)
        {
            if (on == Ghost) return;
            Ghost = on;
            if (on)
            {
                if (!IsVisible) ShowMini();
                if (_ghost == null) _ghost = new GhostWindow();
                _ghost.ShowOver(this, _title.Text, _artist.Text, _art.Source, _barFill.Background);
                Hide();
            }
            else
            {
                _ghost?.Hide();
                ShowMini();
            }
        }
        public bool GhostShown => _ghost != null && _ghost.IsVisible;
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);

        void UpdateLike()
        {
            if (_like == null) return;
            _like.Content = _liked ? "" : "";
            _like.ToolTip = _liked ? "In your Liked Songs (click to open Glassify)" : "Like (save to Liked Songs)";
        }

        // ---- size: small / medium / large, remembered ----
        Grid _root;
        double _scale = 1;
        static readonly double[] Sizes = { 0.8, 1.0, 1.3 };
        void ApplyScale()
        {
            _root.LayoutTransform = new ScaleTransform(_scale, _scale);
            Width = Math.Round(372 * _scale); Height = Math.Round(108 * _scale);
        }
        void CycleSize()
        {
            int i = Array.IndexOf(Sizes, _scale);
            _scale = Sizes[(i + 1) % Sizes.Length];
            double right = Left + Width, bottom = Top + Height;   // grow/shrink from the bottom-right corner
            ApplyScale();
            Left = right - Width; Top = bottom - Height;
            SavePos();
        }

        void ApplColoursSafe(string pc, string ac, string alpha)
        {
            double a = 82; double.TryParse(alpha, NumberStyles.Any, CultureInfo.InvariantCulture, out a);
            ApplyColours(pc, ac, a);
        }

        // ---- colours follow your panel / highlight colours from the main player's settings ----
        string _lookKey;
        void ApplyColours(string panel, string accent, double alpha)
        {
            var key = panel + accent + alpha;
            if (key == _lookKey) return;
            _lookKey = key;
            try
            {
                var pc = (Color)ColorConverter.ConvertFromString(panel);
                var ac = (Color)ColorConverter.ConvertFromString(accent);
                byte a1 = (byte)Math.Max(0x40, Math.Min(0xF0, alpha * 2.2)), a2 = (byte)(a1 / 3);
                _root.Background = new LinearGradientBrush(Color.FromArgb(a1, pc.R, pc.G, pc.B), Color.FromArgb(a2, pc.R, pc.G, pc.B), 35);
                _play.Background = new SolidColorBrush(ac);
                double lum = (0.299 * ac.R + 0.587 * ac.G + 0.114 * ac.B) / 255;
                _play.Foreground = lum > .55 ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)) : Brushes.White;
                _barFill.Background = new SolidColorBrush(ac);
            }
            catch { }
        }

        public void ShowMini()
        {
            var wa = SystemParameters.WorkArea;
            try { var sp = File.ReadAllText(PosFile).Split(','); if (sp.Length > 2) { var s = double.Parse(sp[2], CultureInfo.InvariantCulture); if (Array.IndexOf(Sizes, s) >= 0) _scale = s; } } catch { }
            ApplyScale();
            Left = wa.Right - Width - 16; Top = wa.Bottom - Height - 16;
            try
            {
                var p = File.ReadAllText(PosFile).Split(',');
                double x = double.Parse(p[0], CultureInfo.InvariantCulture), y = double.Parse(p[1], CultureInfo.InvariantCulture);
                if (x >= SystemParameters.VirtualScreenLeft && y >= SystemParameters.VirtualScreenTop &&
                    x + Width <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                    y + Height <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight) { Left = x; Top = y; }
            }
            catch { }
            Show();
            // If the remembered spot isn't on any connected monitor any more, snap back to the main screen.
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                GetWindowRect(hwnd, out var rc);
                if (MonitorFromRect(ref rc, 0 /*MONITOR_DEFAULTTONULL*/) == IntPtr.Zero) { Left = wa.Right - Width - 16; Top = wa.Bottom - Height - 16; SavePos(); }
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT r, uint flags);

        void SavePos() { try { File.WriteAllText(PosFile, string.Join(",", Left.ToString(CultureInfo.InvariantCulture), Top.ToString(CultureInfo.InvariantCulture), _scale.ToString(CultureInfo.InvariantCulture))); } catch { } }

        Button IconButton(string glyph, double size, double box, Action onClick, string tip, bool filled = false)
        {
            var b = new Button { Content = glyph, FontFamily = new FontFamily(Icons), FontSize = size, Width = box, Height = box, ToolTip = tip, Focusable = false, Cursor = Cursors.Hand,
                Foreground = filled ? new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14)) : Brushes.White, Margin = new Thickness(2, 0, 2, 0) };
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border), "B");
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(box / 2));
            if (filled) { b.Background = new SolidColorBrush(Color.FromArgb(0xF2, 255, 255, 255)); bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty)); }
            else bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            if (filled) hover.Setters.Add(new Setter(OpacityProperty, 0.85, "B"));
            else hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), "B"));
            tpl.Triggers.Add(hover);
            var press = new Trigger { Property = Button.IsPressedProperty, Value = true };
            press.Setters.Add(new Setter(OpacityProperty, 0.7, "B"));
            tpl.Triggers.Add(press);
            b.Template = tpl;
            b.Click += (s, e) => onClick();
            return b;
        }

        async void Click(string testId)
        {
            try { await Program.Eval("window.__ms && window.__ms.click('" + testId + "')"); } catch { }
            await Task.Delay(350);
            await Refresh();
        }

        bool _busy;
        async Task Refresh()
        {
            if (_busy) { Tick(); return; }
            _busy = true;
            try
            {
                var json = await Program.Eval("window.__ms ? window.__ms.state() : ''");
                if (!string.IsNullOrEmpty(json) && Program.Json.DeserializeObject(json) is Dictionary<string, object> st)
                {
                    var t = Program.Str(st, "t"); var a = Program.Str(st, "a");
                    if (!string.IsNullOrEmpty(t)) { _title.Text = t; _artist.Text = a; }
                    _playing = st.TryGetValue("playing", out var p) && p is bool pb && pb;
                    _liked = st.TryGetValue("liked", out var lk) && lk is bool lb && lb; UpdateLike();
                    UpdatePlay();
                    var art = Program.Str(st, "art");
                    if (!string.IsNullOrEmpty(art) && art != _artUrl)
                    {
                        _artUrl = art;
                        try { var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(art); bi.DecodePixelWidth = 168; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); _art.Source = bi; } catch { }
                    }
                    var pc = Program.Str(st, "color"); var ac = Program.Str(st, "accent");
                    if (pc != null && ac != null) ApplColoursSafe(pc, ac, Program.Str(st, "alpha"));
                    var pos = Secs(Program.Str(st, "pos")); var dur = Secs(Program.Str(st, "dur"));
                    if (dur > 0) { _durS = dur; _posS = pos; _at = DateTime.UtcNow; }
                }
            }
            catch { }
            finally { _busy = false; }
            Tick();
        }

        void Tick() => DrawBar();

        void DrawBar()
        {
            if (_durS <= 0 || _bar.ActualWidth <= 0) { _barFill.Width = 0; return; }
            var pos = _posS + (_playing ? (DateTime.UtcNow - _at).TotalSeconds : 0);
            _barFill.Width = Math.Max(0, Math.Min(1, pos / _durS)) * _bar.ActualWidth;
        }

        void UpdatePlay() => _play.Content = _playing ? "" : "";

        static double Secs(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            double total = 0;
            foreach (var part in s.Trim().Split(':')) { if (!double.TryParse(part, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return 0; total = total * 60 + v; }
            return total;
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    }
}
