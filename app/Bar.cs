using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Glassify
{
    /// The bar: the player's second shape (the first is the mini player card). A thin glass player docked to the top
    /// or bottom edge of the main screen. It registers with Windows like the taskbar does, so other windows make room
    /// for it, and it steps aside for fullscreen games. Shows next in queue and your recently played playlists, and its
    /// menu lists your library and searches songs. Ctrl+Alt+B (or the shape buttons) turns one shape into the other.
    static class Bar
    {
        static BarWindow _w;
        public static volatile bool IsShown;

        public static void Show() => Run(w => w.ShowBar());
        public static void Hide() => Run(w => w.HideBar());
        public static void ToggleVisible() => Run(w => { if (w.IsVisible) { w.HideBar(); Program.Window?.PlayerHiddenByUser(); } else w.ShowBar(); });
        /// The bar's own hide button.
        public static void HideByUser() => Run(w => { w.HideBar(); Program.Window?.PlayerHiddenByUser(); });

        /// Ctrl+Alt+B / the shape buttons: turn the mini player into the bar, or back. The player stays on screen.
        public static void Toggle()
        {
            Opts.Bar = !Opts.Bar;
            Opts.Save();
            _ = Program.Eval("window.__msOpts && (window.__msOpts.bar = " + (Opts.Bar ? "true" : "false") + ")");
            if (Opts.Bar) { Mini.HideCard(); Show(); }
            else { Hide(); Mini.ShowCard(); }
        }

        /// Settings changed (shape or top/bottom): if the player is on screen, show it in the chosen shape.
        public static void Sync() => Run(w =>
        {
            if (Opts.Bar) { if (w.IsVisible || Mini.CardShown) { Mini.HideCard(); w.ShowBar(); } }
            else if (w.IsVisible) { w.HideBar(); Mini.ShowCard(); }
        });

        internal static void Remember(bool shown) { IsShown = shown; if (Opts.BarShown != shown) { Opts.BarShown = shown; Opts.Save(); } }

        /// Light look: no glass blur, flat colour, slower song checks.
        static volatile bool _liteNow;
        public static void SetLite(bool lite)
        {
            _liteNow = lite;
            var d = Mini.Disp; if (d == null || _w == null) return;
            d.BeginInvoke(new Action(() => _w?.SetLite(lite)));
        }

        public static void CloseNow() { try { if (_w != null) { bool was = Opts.BarShown; _w.HideBar(); Opts.BarShown = was; Opts.Save(); _w.Close(); } } catch { } }

        static void Run(Action<BarWindow> a)
        {
            var d = Mini.Disp; if (d == null) return;
            d.BeginInvoke(new Action(() =>
            {
                try { if (_w == null) { _w = new BarWindow(); _w.SetLite(_liteNow); } a(_w); }
                catch (Exception ex) { Program.Log("bar: " + ex); }
            }));
        }
    }

    class BarWindow : Window
    {
        const double BarHeight = 48;
        const string Icons = "Segoe Fluent Icons, Segoe MDL2 Assets";
        readonly Image _art = new Image { Stretch = Stretch.UniformToFill, Width = 34, Height = 34 };
        readonly TextBlock _title = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _artist = new TextBlock { FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0xB8, 255, 255, 255)), TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _pos = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Width = 40, TextAlignment = TextAlignment.Right };
        readonly TextBlock _dur = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Width = 40 };
        readonly Border _fill = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Width = 0 };
        readonly Grid _seek = new Grid { Height = 16, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Click to jump", Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly Grid _root;
        readonly StackPanel _song, _recents;
        readonly DockPanel _timeline;
        readonly Border _nextBox;
        readonly Image _nextArt = new Image { Stretch = Stretch.UniformToFill, Width = 30, Height = 30 };
        readonly TextBlock _nextTitle = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _nextArtist = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0xA8, 255, 255, 255)), TextTrimming = TextTrimming.CharacterEllipsis };
        string _nextFor, _nextImg; DateTime _recentAt; bool _hasNext;
        readonly Button _play, _like, _menuBtn;
        readonly Border _artBox, _grip, _recentHost;
        readonly StackPanel _ctl, _btns;
        readonly TextBlock _recentHead;
        string _edge = "", _mon = ""; bool _vertical, _dragging;
        const double SideWidth = 300;
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        string _artUrl; double _posS, _durS; bool _playing, _liked; DateTime _at = DateTime.UtcNow;
        bool _registered, _fullscreenApp;
        IntPtr _hwnd;

        // menu
        readonly Popup _popup;
        readonly TextBox _search = new TextBox { FontSize = 13, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), CaretBrush = Brushes.White, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0) };
        readonly TextBlock _hint = new TextBlock { Text = "Search songs, press Enter", FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
        readonly TextBlock _listHead = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 255, 255, 255)), Margin = new Thickness(14, 10, 14, 6) };
        readonly StackPanel _list = new StackPanel();
        readonly Border _menuCard;
        List<Dictionary<string, object>> _library;
        DateTime _libraryAt;

        public BarWindow()
        {
            Title = "Glassify bar";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false;
            Background = Brushes.Transparent; Height = BarHeight; Width = 800;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            UseLayoutRounding = true;
            WindowChrome.SetWindowChrome(this, new WindowChrome { GlassFrameThickness = new Thickness(-1), CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), UseAeroCaptionButtons = false });

            _root = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x70, 0x20, 0x10, 0x1C)) };

            // ---- the pieces (arranged in a row for top/bottom, in a column for left/right: see BuildLayout) ----
            // grip: drag it to another edge or screen
            _grip = new Border { Width = 18, Height = 30, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = "Drag to move the bar to another edge or screen", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = "⋮⋮", FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            _grip.MouseLeftButtonDown += (s, e) => { e.Handled = true; _dragging = true; CaptureMouse(); };
            MouseMove += (s, e) => { if (_dragging) DragTo(); };
            MouseLeftButtonUp += (s, e) => { if (_dragging) { _dragging = false; ReleaseMouseCapture(); DragDone(); } };
            LostMouseCapture += (s, e) => { if (_dragging) { _dragging = false; DragDone(); } };

            _menuBtn = Btn("", 14, 36, ToggleMenu, "Your library and search");
            _artBox = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(6), Margin = new Thickness(6, 0, 10, 0), Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)), Child = _art, Cursor = Cursors.Hand, ToolTip = "Open Glassify", VerticalAlignment = VerticalAlignment.Center };
            _artBox.MouseLeftButtonUp += (s, e) => Program.FocusExisting();
            _song = new StackPanel { Width = 240, VerticalAlignment = VerticalAlignment.Center };
            _title.Text = "Glassify"; _artist.Text = "Nothing playing";
            _song.Children.Add(_title); _song.Children.Add(_artist);

            _ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
            _ctl.Children.Add(Btn("", 13, 32, () => Click("control-button-skip-back"), "Previous"));
            _play = Btn("", 14, 34, () => { _playing = !_playing; UpdatePlay(); Click("control-button-playpause"); }, "Play/pause", true);
            _ctl.Children.Add(_play);
            _ctl.Children.Add(Btn("", 13, 32, () => Click("control-button-skip-forward"), "Next"));

            // timeline: about as long as the mini player's, not the whole screen
            _timeline = new DockPanel { Width = 320, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            DockPanel.SetDock(_pos, Dock.Left); _timeline.Children.Add(_pos);
            DockPanel.SetDock(_dur, Dock.Right); _timeline.Children.Add(_dur);
            _seek.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255)) });
            _seek.Children.Add(_fill);
            _seek.MouseLeftButtonDown += async (s, e) =>
            {
                double frac = Math.Max(0, Math.Min(1, e.GetPosition(_seek).X / Math.Max(1, _seek.ActualWidth)));
                if (_durS > 0) { _posS = frac * _durS; _at = DateTime.UtcNow; Draw(); }
                try { await Program.Eval("window.__ms && window.__ms.seek(" + frac.ToString(CultureInfo.InvariantCulture) + ")"); } catch { }
                await Task.Delay(400); await Refresh();
            };
            _timeline.Children.Add(_seek);
            _like = Btn("", 12, 30, async () =>
            {
                if (_liked) { Program.FocusExisting(); return; }
                _liked = true; UpdateLike();
                try { await Program.Eval("window.__ms && window.__ms.like()"); } catch { }
                await Task.Delay(600); await Refresh();
            }, "Like (save to Liked Songs)");

            // next in queue (click = skip to it)
            var nextGrid = new Grid();
            nextGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nextGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nextGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var nextLabel = new TextBlock { Text = "NEXT", FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var nextArtBox = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)), Child = _nextArt, VerticalAlignment = VerticalAlignment.Center };
            _nextArt.Clip = new RectangleGeometry(new Rect(0, 0, 30, 30), 4, 4);
            Grid.SetColumn(nextArtBox, 1);
            var nextText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            nextText.Children.Add(_nextTitle); nextText.Children.Add(_nextArtist);
            Grid.SetColumn(nextText, 2);
            nextGrid.Children.Add(nextLabel); nextGrid.Children.Add(nextArtBox); nextGrid.Children.Add(nextText);
            _nextBox = new Border { Width = 250, Margin = new Thickness(14, 0, 0, 0), Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)), Child = nextGrid, Cursor = Cursors.Hand, ToolTip = "Next in queue (click to skip to it)", VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            _nextBox.MouseLeftButtonUp += (s, e) => Click("control-button-skip-forward");

            // recently played: as many tiles as fit
            _recentHead = new TextBlock { Text = "RECENTLY PLAYED", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)), Margin = new Thickness(2, 14, 0, 8) };
            _recents = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
            _recentHost = new Border { Child = _recents, ClipToBounds = true };
            _recentHost.SizeChanged += (s, e) => FitRecents();

            // buttons: turn into the mini player, open Glassify, hide
            _btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _btns.Children.Add(Btn("", 12, 30, () => Bar.Toggle(), "Turn into the mini player (Ctrl+Alt+B)"));
            _btns.Children.Add(Btn("", 12, 30, () => Program.FocusExisting(), "Open the main player"));
            _btns.Children.Add(Btn("", 11, 30, () => Bar.HideByUser(), "Hide the player (Ctrl+Alt+M brings it back)"));

            BuildLayout(false);
            _root.MouseWheel += async (s, e) => { try { await Program.Eval("window.__ms && window.__ms.volume(" + (e.Delta > 0 ? "0.05" : "-0.05") + ")"); } catch { } };
            SizeChanged += (s, e) => { Adapt(); FitRecents(); };
            Content = _root;
            // ---- the menu: search box, then your library or the search results ----
            var searchRow = new Grid { Height = 36, Margin = new Thickness(10, 10, 10, 2) };
            searchRow.Children.Add(new Border { CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) });
            var sIcon = new TextBlock { Text = "", FontFamily = new FontFamily(Icons), FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0), IsHitTestVisible = false };
            var sBox = new Grid { Margin = new Thickness(30, 0, 12, 0) };
            sBox.Children.Add(_hint); sBox.Children.Add(_search);
            searchRow.Children.Add(sIcon); searchRow.Children.Add(sBox);
            _search.TextChanged += (s, e) => { _hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; if (_search.Text.Length == 0) ShowLibrary(); };
            _search.KeyDown += (s, e) => { if (e.Key == Key.Enter && _search.Text.Trim().Length > 0) { e.Handled = true; _ = DoSearch(_search.Text.Trim()); } else if (e.Key == Key.Escape) _popup.IsOpen = false; };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Margin = new Thickness(0, 0, 0, 8) };
            var menu = new DockPanel();
            DockPanel.SetDock(searchRow, Dock.Top); menu.Children.Add(searchRow);
            DockPanel.SetDock(_listHead, Dock.Top); menu.Children.Add(_listHead);
            menu.Children.Add(scroll);
            // (the popup inherits its font from the menu button, which uses the icon font, so set the text font here)
            System.Windows.Documents.TextElement.SetFontFamily(menu, new FontFamily("Segoe UI Variable Text, Segoe UI"));
            _menuCard = new Border { Width = 380, Height = 480, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x16, 0x20)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), BorderThickness = new Thickness(1), Child = menu };
            _popup = new Popup { PlacementTarget = _menuBtn, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = _menuCard, VerticalOffset = 8, PopupAnimation = PopupAnimation.None };

            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(_hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                int v = 1; DwmSetWindowAttribute(_hwnd, 20, ref v, 4);   // dark
                v = _lite ? 1 : 3; DwmSetWindowAttribute(_hwnd, 38, ref v, 4);   // acrylic glass (none in the Light look)
                v = 1; DwmSetWindowAttribute(_hwnd, 33, ref v, 4);       // square corners: it's a screen edge
                SetWindowLong(_hwnd, -20, GetWindowLong(_hwnd, -20) | 0x80);   // no Alt+Tab entry
                HwndSource.FromHwnd(_hwnd).AddHook(Hook);
            };
            _timer.Tick += async (s, e) => await Refresh();
            _seek.SizeChanged += (s, e) => Draw();
            Closed += (s, e) => Unregister();
        }

        // ---------- layout: a row (top/bottom edge) or a column (left/right edge) ----------
        void BuildLayout(bool vertical)
        {
            _vertical = vertical;
            foreach (FrameworkElement el in new FrameworkElement[] { _grip, _menuBtn, _artBox, _song, _ctl, _timeline, _like, _nextBox, _recentHead, _recentHost, _btns })
            {
                if (el.Parent is Panel p) p.Children.Remove(el);
                else if (el.Parent is Decorator d) d.Child = null;
            }
            _root.Children.Clear();
            double art = vertical ? SideWidth - 32 : 34;
            _artBox.Width = _artBox.Height = art; _art.Width = _art.Height = art;
            _artBox.CornerRadius = new CornerRadius(vertical ? 12 : 6);
            _art.Clip = new RectangleGeometry(new Rect(0, 0, art, art), vertical ? 12 : 6, vertical ? 12 : 6);
            _recents.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            _recents.VerticalAlignment = vertical ? VerticalAlignment.Top : VerticalAlignment.Center;
            _title.FontSize = vertical ? 15 : 13; _artist.FontSize = vertical ? 12.5 : 11.5;
            if (!vertical)
            {
                // [ grip | menu | cover + song | controls | timeline | heart | next in queue ][ recent playlists... ][ buttons ]
                var row = new Grid { Margin = new Thickness(4, 0, 8, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                _artBox.Margin = new Thickness(6, 0, 10, 0); _artBox.HorizontalAlignment = HorizontalAlignment.Left;
                _song.Margin = new Thickness(0); _ctl.Margin = new Thickness(8, 0, 4, 0); _ctl.HorizontalAlignment = HorizontalAlignment.Left;
                _timeline.Margin = new Thickness(4, 0, 4, 0); _nextBox.Margin = new Thickness(14, 0, 0, 0); _nextBox.Width = 250;
                foreach (var el in new UIElement[] { _grip, _menuBtn, _artBox, _song, _ctl, _timeline, _like, _nextBox }) left.Children.Add(el);
                row.Children.Add(left);
                _recentHost.Margin = new Thickness(14, 0, 8, 0);
                Grid.SetColumn(_recentHost, 1); row.Children.Add(_recentHost);
                Grid.SetColumn(_btns, 2); row.Children.Add(_btns);
                _root.Children.Add(row);
            }
            else
            {
                // a sidebar: buttons on top, big cover, song, timeline + heart, controls, next in queue, recently played
                var col = new DockPanel { Margin = new Thickness(16, 8, 16, 12), LastChildFill = true };
                var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                DockPanel.SetDock(_grip, Dock.Left); top.Children.Add(_grip);
                DockPanel.SetDock(_menuBtn, Dock.Left); top.Children.Add(_menuBtn);
                DockPanel.SetDock(_btns, Dock.Right); top.Children.Add(_btns);
                top.Children.Add(new Border());
                DockPanel.SetDock(top, Dock.Top); col.Children.Add(top);
                _artBox.Margin = new Thickness(0); _artBox.HorizontalAlignment = HorizontalAlignment.Center;
                DockPanel.SetDock(_artBox, Dock.Top); col.Children.Add(_artBox);
                _song.Margin = new Thickness(0, 12, 0, 4);
                DockPanel.SetDock(_song, Dock.Top); col.Children.Add(_song);
                var tl = new DockPanel { Margin = new Thickness(-8, 2, -6, 0) };
                DockPanel.SetDock(_like, Dock.Right); tl.Children.Add(_like);
                _timeline.Margin = new Thickness(0); tl.Children.Add(_timeline);
                DockPanel.SetDock(tl, Dock.Top); col.Children.Add(tl);
                _ctl.Margin = new Thickness(0, 6, 0, 4); _ctl.HorizontalAlignment = HorizontalAlignment.Center;
                DockPanel.SetDock(_ctl, Dock.Top); col.Children.Add(_ctl);
                _nextBox.Margin = new Thickness(0, 10, 0, 0); _nextBox.Width = double.NaN;
                DockPanel.SetDock(_nextBox, Dock.Top); col.Children.Add(_nextBox);
                DockPanel.SetDock(_recentHead, Dock.Top); col.Children.Add(_recentHead);
                _recentHost.Margin = new Thickness(0);
                col.Children.Add(_recentHost);
                _root.Children.Add(col);
            }
            Adapt(); FitRecents();
        }

        // ---------- moving it: drag the grip toward any edge of any screen ----------
        void DragTo()
        {
            if (!GetCursorPos(out var p)) return;
            var (mon, rc) = MonitorAt(p);
            int dl = p.X - rc.L, dr = rc.R - p.X, dt = p.Y - rc.T, db = rc.B - p.Y;
            int min = Math.Min(Math.Min(dl, dr), Math.Min(dt, db));
            string edge = min == dt ? "top" : min == db ? "bottom" : min == dl ? "left" : "right";
            if (edge == _edge && mon == _mon) return;
            Opts.BarEdge = edge; Opts.BarMonitor = mon;
            ShowBar();
        }
        void DragDone()
        {
            Opts.Save();
            _ = Program.Eval("window.__msOpts && (window.__msOpts.baredge = " + Program.Json.Serialize(Opts.BarEdge) + ", window.__msOpts.barmon = " + Program.Json.Serialize(Opts.BarMonitor) + ")");
        }

        /// The chosen screen (by its Windows device name), or the main screen if it isn't connected any more.
        static (string id, RECT rc) FindMonitor(string id)
        {
            (string, RECT) primary = (null, default), found = (null, default);
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) =>
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(h, ref mi))
                {
                    if ((mi.dwFlags & 1) != 0) primary = (mi.szDevice, mi.rcMonitor);
                    if (!string.IsNullOrEmpty(id) && mi.szDevice == id) found = (mi.szDevice, mi.rcMonitor);
                }
                return true;
            }, IntPtr.Zero);
            return found.Item1 != null ? found : primary;
        }
        static (string id, RECT rc) MonitorAt(POINT p)
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            GetMonitorInfo(MonitorFromPoint(p, 2 /*NEAREST*/), ref mi);
            return (mi.szDevice, mi.rcMonitor);
        }

        /// Connected screens, for the settings panel: [{id, name}]
        public static string MonitorsJson()
        {
            var list = new List<string>(); int n = 0;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) =>
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(h, ref mi))
                {
                    n++;
                    var name = "Screen " + n + ((mi.dwFlags & 1) != 0 ? " (main)" : "") + "  " + (mi.rcMonitor.R - mi.rcMonitor.L) + "×" + (mi.rcMonitor.B - mi.rcMonitor.T);
                    list.Add("{\"id\":" + Program.Json.Serialize(mi.szDevice) + ",\"name\":" + Program.Json.Serialize(name) + "}");
                }
                return true;
            }, IntPtr.Zero);
            return "[" + string.Join(",", list) + "]";
        }

        // ---------- docking (an "app bar", like the taskbar) ----------
        /// Show the bar on the edge and screen from the settings (or move it there if it's showing elsewhere).
        public void ShowBar()
        {
            var edge = Opts.BarEdge; var mon = FindMonitor(Opts.BarMonitor).id ?? "";
            bool moved = edge != _edge || mon != _mon; _edge = edge; _mon = mon;
            bool vertical = edge == "left" || edge == "right";
            if (vertical != _vertical) BuildLayout(vertical);
            // the menu opens away from the edge
            _popup.Placement = edge == "bottom" ? PlacementMode.Top : edge == "left" ? PlacementMode.Right : edge == "right" ? PlacementMode.Left : PlacementMode.Bottom;
            _popup.VerticalOffset = edge == "bottom" ? -8 : edge == "top" ? 8 : 0;
            _popup.HorizontalOffset = edge == "left" ? 8 : edge == "right" ? -8 : 0;
            if (!IsVisible) { Show(); _timer.Start(); _ = Refresh(); }
            if (moved && _registered) Unregister();
            DockToEdge();
            Bar.Remember(true);
            if ((DateTime.UtcNow - _recentAt).TotalSeconds > 30) _ = UpdateRecents();
        }

        public void HideBar()
        {
            _popup.IsOpen = false;
            Bar.Remember(false);
            if (!IsVisible) return;
            Unregister();
            _timer.Stop();
            Hide();
        }

        // ---------- fits any screen, from small laptops to ultra-wides ----------
        // The essentials always fit; the timeline shortens on narrow screens; "next in queue" shows when there's
        // room; the recently played tiles fill whatever space is left (a couple on a laptop, a dozen on an ultra-wide).
        void Adapt()
        {
            if (_vertical)
            {
                _song.Width = double.NaN; _timeline.Width = double.NaN; _timeline.Visibility = Visibility.Visible;
                _nextBox.Visibility = _hasNext ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            double w = ActualWidth; if (w < 100) return;
            _song.Width = w < 1000 ? 170 : 240;
            _timeline.Width = w < 1000 ? 220 : 320;
            _timeline.Visibility = w < 760 ? Visibility.Collapsed : Visibility.Visible;
            _nextBox.Visibility = _hasNext && w >= 1180 ? Visibility.Visible : Visibility.Collapsed;
            _nextBox.Width = w < 1500 ? 210 : 250;
        }

        // recent tiles: across the row (178 wide) or down the column (full width); show as many as fit
        void FitRecents()
        {
            double room = _vertical ? _recentHost.ActualHeight : _recentHost.ActualWidth - 8;
            double step = _vertical ? 44 : 184;
            int fit = (int)Math.Max(0, Math.Floor(room / step));
            for (int i = 0; i < _recents.Children.Count; i++)
            {
                if (_recents.Children[i] is Border tile)
                {
                    tile.Width = _vertical ? double.NaN : 178;
                    tile.Margin = _vertical ? new Thickness(0, 0, 0, 6) : new Thickness(0, 0, 6, 0);
                }
                _recents.Children[i].Visibility = i < fit ? Visibility.Visible : Visibility.Collapsed;
            }
            _recentHead.Visibility = _recents.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        async Task UpdateRecents()
        {
            _recentAt = DateTime.UtcNow;
            var items = await Poll("window.__ms && __ms.recents()", "JSON.stringify(window.__msRecent || null)", 15000);
            if (items == null) return;
            _recents.Children.Clear();
            foreach (var it in items)
            {
                var name = Program.Str(it, "name") ?? ""; var uri = Program.Str(it, "uri"); var img = Program.Str(it, "img");
                if (string.IsNullOrEmpty(uri) || name.Length == 0) continue;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var pic = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) };
                var src = Pic(img, 60);
                if (src != null) pic.Child = new Image { Source = src, Stretch = Stretch.UniformToFill, Clip = new RectangleGeometry(new Rect(0, 0, 30, 30), 5, 5) };
                g.Children.Add(pic);
                var label = new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
                Grid.SetColumn(label, 1); g.Children.Add(label);
                var tile = new Border { Width = 178, Height = 38, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(4), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)), Child = g, Cursor = Cursors.Hand, ToolTip = "Play " + name };
                tile.MouseEnter += (s, e) => tile.Background = new SolidColorBrush(Color.FromArgb(0x2A, 255, 255, 255));
                tile.MouseLeave += (s, e) => tile.Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
                tile.MouseLeftButtonUp += async (s, e) =>
                {
                    _title.Text = name; _artist.Text = "Starting...";
                    await Program.Eval("window.__ms && __ms.playUri(" + Program.Json.Serialize(uri) + "," + Program.Json.Serialize(name) + ")");
                    await PumpFor(9000);
                    await Task.Delay(800); await Refresh();
                };
                _recents.Children.Add(tile);
            }
            FitRecents();
        }

        async Task UpdateNext(string forTrack)
        {
            _nextFor = forTrack;
            var items = await Poll("window.__ms && __ms.upNext()", "JSON.stringify(window.__msNext || null)", 8000);
            if (_nextFor != forTrack || items == null) return;
            if (items.Count == 0) { _hasNext = false; Adapt(); return; }
            var n = items[0];
            _nextTitle.Text = Program.Str(n, "name") ?? ""; _nextArtist.Text = Program.Str(n, "sub") ?? "";
            var img = Program.Str(n, "img");
            if (!string.IsNullOrEmpty(img) && img != _nextImg && img.StartsWith("https://"))
            {
                _nextImg = img;
                try { var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(img); bi.DecodePixelWidth = 60; bi.EndInit(); _nextArt.Source = bi; } catch { }
            }
            _hasNext = _nextTitle.Text.Length > 0; Adapt();
        }

        void DockToEdge()
        {
            if (_hwnd == IntPtr.Zero) return;
            var abd = NewData();
            if (!_registered) { abd.uCallbackMessage = WM_APPBAR; SHAppBarMessage(0 /*ABM_NEW*/, ref abd); _registered = true; }
            // the chosen screen, measured by this thread in the same units SetWindowPos and the shell use here
            var scr = FindMonitor(_mon).rc;
            double dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            int t = (int)Math.Round((_vertical ? SideWidth : BarHeight) * dpi);   // thickness
            abd.uEdge = _edge == "left" ? 0u : _edge == "right" ? 2u : _edge == "bottom" ? 3u : 1u;   // ABE_LEFT/TOP/RIGHT/BOTTOM
            abd.rc = scr;
            if (_edge == "left") abd.rc.R = scr.L + t; else if (_edge == "right") abd.rc.L = scr.R - t;
            else if (_edge == "bottom") abd.rc.T = scr.B - t; else abd.rc.B = scr.T + t;
            SHAppBarMessage(2 /*ABM_QUERYPOS*/, ref abd);           // Windows moves it clear of the taskbar etc.
            if (_edge == "left") abd.rc.R = abd.rc.L + t; else if (_edge == "right") abd.rc.L = abd.rc.R - t;
            else if (_edge == "bottom") abd.rc.T = abd.rc.B - t; else abd.rc.B = abd.rc.T + t;
            SHAppBarMessage(3 /*ABM_SETPOS*/, ref abd);             // reserve the space
            SetWindowPos(_hwnd, _fullscreenApp ? HWND_NOTOPMOST : HWND_TOPMOST, abd.rc.L, abd.rc.T, abd.rc.R - abd.rc.L, abd.rc.B - abd.rc.T, 0x0010 /*NOACTIVATE*/);
        }

        void Unregister()
        {
            if (!_registered || _hwnd == IntPtr.Zero) return;
            var abd = NewData();
            SHAppBarMessage(1 /*ABM_REMOVE*/, ref abd);
            _registered = false;
        }

        APPBARDATA NewData() => new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == WM_APPBAR)
            {
                switch (w.ToInt32())
                {
                    case 1: /*ABN_POSCHANGED*/ if (_registered) DockToEdge(); break;
                    case 2: /*ABN_FULLSCREENAPP: a fullscreen app (a game) opened or closed - step aside for it */
                        _fullscreenApp = l != IntPtr.Zero;
                        SetWindowPos(_hwnd, _fullscreenApp ? HWND_BOTTOM : HWND_TOPMOST, 0, 0, 0, 0, 0x0013 /*NOSIZE|NOMOVE|NOACTIVATE*/);
                        if (_fullscreenApp) _popup.IsOpen = false;
                        break;
                }
            }
            else if (msg == 0x0006 /*WM_ACTIVATE*/ && _registered) { var abd = NewData(); SHAppBarMessage(6 /*ABM_ACTIVATE*/, ref abd); }
            else if (msg == 0x0047 /*WM_WINDOWPOSCHANGED*/ && _registered) { var abd = NewData(); SHAppBarMessage(9 /*ABM_WINDOWPOSCHANGED*/, ref abd); }
            return IntPtr.Zero;
        }

        // ---------- the menu ----------
        void ToggleMenu()
        {
            if (_popup.IsOpen) { _popup.IsOpen = false; return; }
            _popup.IsOpen = true;
            Activate();                       // so you can type in the search box
            // always open on your library, with an empty search box ready for typing
            if (_search.Text.Length > 0) _search.Text = "";   // (clearing it shows the library)
            else ShowLibrary();
            Dispatcher.BeginInvoke(new Action(() => { _search.Focus(); Keyboard.Focus(_search); }), DispatcherPriority.Input);
        }

        async void ShowLibrary()
        {
            _listHead.Text = "Your Library";
            if (_library != null) Fill(_library, false);
            if (_library != null && (DateTime.UtcNow - _libraryAt).TotalSeconds < 60) return;
            if (_library == null) Message("Loading your library...");
            var r = await Poll("window.__ms && __ms.library()", "JSON.stringify(window.__msLib || null)", 20000);
            if (_search.Text.Trim().Length > 0) return;   // you started a search meanwhile
            if (r == null) { if (_library == null) Message("Couldn't load your library. Open Glassify and sign in, then try again."); return; }
            _library = r; _libraryAt = DateTime.UtcNow;
            if (_library.Count == 0) Message("Your library is empty."); else Fill(_library, false);
        }

        async Task DoSearch(string q)
        {
            _listHead.Text = "Songs for \"" + q + "\"";
            Message("Searching...");
            var r = await Poll("window.__ms && __ms.search(" + Program.Json.Serialize(q) + ")", "JSON.stringify(window.__msFind || null)", 12000);
            if (_search.Text.Trim() != q) return;
            if (r == null) { Message("Search didn't work. Check your connection and try again."); return; }
            if (r.Count == 0) Message("No songs found."); else Fill(r, true);
        }

        /// Start something in the page, then check its result every 200 ms.
        async Task<List<Dictionary<string, object>>> Poll(string start, string read, int timeoutMs)
        {
            try
            {
                await Program.Eval(start);
                var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < until)
                {
                    await Task.Delay(200);
                    // run the page's next steps (its own timers are slowed down while Glassify's window is hidden), then read
                    var json = await Program.Eval("(window.__ms && __ms.pump && __ms.pump(), " + read + ")");
                    if (string.IsNullOrEmpty(json) || json == "null") continue;
                    if (!(Program.Json.DeserializeObject(json) is Dictionary<string, object> st)) continue;
                    var state = Program.Str(st, "state");
                    if (state == "error") { Program.Log("bar menu: " + Program.Str(st, "error")); return null; }
                    if (state != "done") continue;
                    var list = new List<Dictionary<string, object>>();
                    if (st.TryGetValue("items", out var it) && it is System.Collections.IEnumerable e)
                        foreach (var x in e) if (x is Dictionary<string, object> d) list.Add(d);
                    return list;
                }
            }
            catch (Exception ex) { Program.Log("bar menu: " + ex.Message); }
            return null;
        }

        /// Keep the page's steps going (e.g. "open this playlist, then press play") until they're done.
        async Task PumpFor(int ms)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                await Task.Delay(200);
                var left = await Program.Eval("window.__ms && __ms.pump ? String(__ms.pump()) : '0'");
                if (left == "0" || left == null) return;
            }
        }

        static ImageSource Pic(string url, int px)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                var bi = new BitmapImage(); bi.BeginInit(); bi.DecodePixelWidth = px;
                if (url.StartsWith("https://")) bi.UriSource = new Uri(url);
                else if (url.StartsWith("data:image/png;base64,") || url.StartsWith("data:image/jpeg;base64,"))
                { bi.StreamSource = new System.IO.MemoryStream(Convert.FromBase64String(url.Substring(url.IndexOf(',') + 1))); bi.CacheOption = BitmapCacheOption.OnLoad; }
                else return null;
                bi.EndInit(); return bi;
            }
            catch { return null; }
        }

        void Message(string text)
        {
            _list.Children.Clear();
            _list.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), Margin = new Thickness(14, 6, 14, 6), TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        }

        void Fill(List<Dictionary<string, object>> items, bool songs)
        {
            _list.Children.Clear();
            foreach (var it in items)
            {
                var name = Program.Str(it, "name") ?? ""; var sub = Program.Str(it, "sub") ?? ""; var img = Program.Str(it, "img");
                var row = new Grid { Height = 52, Background = Brushes.Transparent, Cursor = Cursors.Hand };
                var hover = new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(6, 1, 6, 1), Background = Brushes.Transparent };
                row.Children.Add(hover);
                var g = new Grid { Margin = new Thickness(14, 0, 14, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var pic = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(songs ? 4 : 6), Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
                var src = Pic(img, 80);
                if (src != null) { var im = new Image { Source = src, Stretch = Stretch.UniformToFill }; im.Clip = new RectangleGeometry(new Rect(0, 0, 40, 40), songs ? 4 : 6, songs ? 4 : 6); pic.Child = im; }
                g.Children.Add(pic);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                text.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                if (sub.Length > 0) text.Children.Add(new TextBlock { Text = sub, Foreground = new SolidColorBrush(Color.FromArgb(0xA8, 255, 255, 255)), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(text, 1); g.Children.Add(text);
                row.Children.Add(g);
                row.MouseEnter += (s, e) => hover.Background = new SolidColorBrush(Color.FromArgb(0x1E, 255, 255, 255));
                row.MouseLeave += (s, e) => hover.Background = Brushes.Transparent;
                var item = it;
                row.MouseLeftButtonUp += async (s, e) =>
                {
                    _popup.IsOpen = false;
                    _title.Text = name; _artist.Text = songs ? sub : "Starting...";
                    if (songs) await Program.Eval("window.__ms && __ms.playFound(" + Convert.ToString(item.TryGetValue("i", out var i) ? i : 0, CultureInfo.InvariantCulture) + ")");
                    else { await Program.Eval("window.__ms && __ms.playUri(" + Program.Json.Serialize(Program.Str(item, "uri")) + "," + Program.Json.Serialize(name) + ")"); await PumpFor(9000); }
                    await Task.Delay(800); await Refresh();
                };
                _list.Children.Add(row);
            }
        }

        // ---------- now playing ----------
        bool _busy;
        async Task Refresh()
        {
            if (_busy) { Draw(); return; }
            _busy = true;
            try
            {
                var json = await Program.Eval("window.__ms ? window.__ms.state() : ''");
                if (!string.IsNullOrEmpty(json) && Program.Json.DeserializeObject(json) is Dictionary<string, object> st)
                {
                    var t = Program.Str(st, "t"); if (!string.IsNullOrEmpty(t)) { _title.Text = t; _artist.Text = Program.Str(st, "a"); }
                    // new song: find out what's next (and refresh the recents now and then)
                    var key = t + "|" + Program.Str(st, "a");
                    if (!string.IsNullOrEmpty(t) && key != _nextFor) _ = UpdateNext(key);
                    if ((DateTime.UtcNow - _recentAt).TotalMinutes > 3) _ = UpdateRecents();
                    _playing = st.TryGetValue("playing", out var p) && p is bool pb && pb; UpdatePlay();
                    _liked = st.TryGetValue("liked", out var lk) && lk is bool lb && lb; UpdateLike();
                    var art = Program.Str(st, "art");
                    if (!string.IsNullOrEmpty(art) && art != _artUrl)
                    {
                        _artUrl = art;
                        try { var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(art); bi.DecodePixelWidth = 80; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); _art.Source = bi; } catch { }
                    }
                    Colours(Program.Str(st, "color"), Program.Str(st, "accent"), Program.Str(st, "alpha"), Program.Str(st, "grad") == "True" ? Program.Str(st, "color2") : null, Program.Str(st, "angle"), Program.Str(st, "mid"));
                    var dur = Secs(Program.Str(st, "dur")); if (dur > 0) { _durS = dur; _posS = Secs(Program.Str(st, "pos")); _at = DateTime.UtcNow; }
                }
            }
            catch { }
            finally { _busy = false; }
            Draw();
        }

        void Draw()
        {
            var pos = _posS + (_playing ? (DateTime.UtcNow - _at).TotalSeconds : 0);
            if (_durS > 0) pos = Math.Min(pos, _durS);
            _pos.Text = _durS > 0 ? Time(pos) : ""; _dur.Text = _durS > 0 ? Time(_durS) : "";
            _fill.Width = _durS > 0 && _seek.ActualWidth > 0 ? Math.Max(0, Math.Min(1, pos / _durS)) * _seek.ActualWidth : 0;
        }
        static string Time(double s) { var t = TimeSpan.FromSeconds(Math.Max(0, s)); return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss"); }

        string _lookKey;
        bool _lite;
        public void SetLite(bool lite)
        {
            if (lite == _lite) return;
            _lite = lite; _lookKey = null;
            _timer.Interval = TimeSpan.FromSeconds(lite ? 3 : 1);
            if (_hwnd != IntPtr.Zero) { int v = lite ? 1 : 3; DwmSetWindowAttribute(_hwnd, 38, ref v, 4); }
            if (lite) _root.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x22));   // until the next colour update
        }

        void Colours(string panel, string accent, string alpha, string panel2 = null, string angle = null, string mid = null)
        {
            if (panel == null || accent == null) return;
            var key = panel + accent + alpha + panel2 + angle + "/" + mid + _lite; if (key == _lookKey) return; _lookKey = key;
            try
            {
                double a = 82; double.TryParse(alpha, NumberStyles.Any, CultureInfo.InvariantCulture, out a);
                var pc = (Color)ColorConverter.ConvertFromString(panel); var ac = (Color)ColorConverter.ConvertFromString(accent);
                byte a1 = (byte)Math.Max(0x40, Math.Min(0xF0, a * 2.2));
                if (_lite) _root.Background = new SolidColorBrush(Color.FromRgb(pc.R, pc.G, pc.B));   // Light: flat and solid
                else if (panel2 != null)
                {
                    double.TryParse(angle, NumberStyles.Any, CultureInfo.InvariantCulture, out var ang);
                    var p2 = (Color)ColorConverter.ConvertFromString(panel2);
                    if (!double.TryParse(mid, NumberStyles.Any, CultureInfo.InvariantCulture, out var m)) m = 50;
                    _root.Background = MiniWindow.Gradient(Color.FromArgb(a1, pc.R, pc.G, pc.B), Color.FromArgb(a1, p2.R, p2.G, p2.B), ang, m);
                }
                else _root.Background = new LinearGradientBrush(Color.FromArgb(a1, pc.R, pc.G, pc.B), Color.FromArgb((byte)(a1 / 2), pc.R, pc.G, pc.B), 0);
                _menuCard.Background = new SolidColorBrush(Color.FromRgb((byte)(pc.R * .45 + 14), (byte)(pc.G * .45 + 12), (byte)(pc.B * .45 + 16)));
                _play.Background = new SolidColorBrush(ac);
                _play.Foreground = (0.299 * ac.R + 0.587 * ac.G + 0.114 * ac.B) / 255 > .55 ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)) : Brushes.White;
                _fill.Background = new SolidColorBrush(ac);
            }
            catch { }
        }

        void UpdatePlay() => _play.Content = _playing ? "" : "";
        void UpdateLike() { _like.Content = _liked ? "" : ""; _like.ToolTip = _liked ? "In your Liked Songs (click to open Glassify)" : "Like (save to Liked Songs)"; }

        async void Click(string testId)
        {
            try { await Program.Eval("window.__ms && window.__ms.click('" + testId + "')"); } catch { }
            await Task.Delay(350); await Refresh();
        }

        static double Secs(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            double total = 0;
            foreach (var part in s.Trim().Split(':')) { if (!double.TryParse(part, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return 0; total = total * 60 + v; }
            return total;
        }

        Button Btn(string glyph, double size, double box, Action onClick, string tip, bool filled = false)
        {
            var b = new Button { Content = glyph, FontFamily = new FontFamily(Icons), FontSize = size, Width = box, Height = box, ToolTip = tip, Focusable = false, Cursor = Cursors.Hand,
                Foreground = filled ? new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14)) : Brushes.White, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
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
            b.Template = tpl;
            b.Click += (s, e) => onClick();
            return b;
        }

        const int WM_APPBAR = 0x8000 + 0x4742;   // WM_APP + 'GB'
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2), HWND_BOTTOM = new IntPtr(1);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MONITORINFOEX { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFOEX mi);
        delegate bool MonitorEnum(IntPtr h, IntPtr dc, ref RECT r, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum cb, IntPtr data);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
        [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    }
}
