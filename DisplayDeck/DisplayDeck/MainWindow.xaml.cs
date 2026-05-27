using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBlock = System.Windows.Controls.TextBlock;

namespace DisplayDeck;

public partial class MainWindow : FluentWindow
{
    // 배치도 상자 색상
    private static readonly Brush BoxNormalBg = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3F));
    private static readonly Brush BoxNormalBorder = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x5A));
    private static readonly Brush BoxSelectedBg = new SolidColorBrush(Color.FromRgb(0x3E, 0x6F, 0xC0));
    private static readonly Brush BoxSelectedBorder = new SolidColorBrush(Color.FromRgb(0x7C, 0xAA, 0xEC));
    private static readonly Brush TextDim = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB0));

    /// <summary>화면에 표시되는 해상도 프리셋 — 추가·삭제 시 즉시 저장된다.</summary>
    private List<Resolution> _presets = new();

    /// <summary>연결된 모니터 목록과 현재 선택된 모니터.</summary>
    private List<MonitorInfo> _monitors = new();
    private MonitorInfo? _selectedMonitor;

    /// <summary>"식별" 클릭 시 각 모니터에 띄운 오버레이 창들.</summary>
    private readonly List<Window> _identifyOverlays = new();

    // 일반(최대화 아님) 상태의 최신 창 크기 — SizeChanged 로 계속 추적해 저장에 쓴다.
    private double _normalWidth;
    private double _normalHeight;

    // 시작 시 최대화 상태로 복원할지 (지난 실행에서 최대화로 닫았으면 true)
    private bool _startMaximized;

    public MainWindow()
    {
        InitializeComponent();

        // 지난 실행에서 저장한 창 크기 / 항상 위 상태를 복원
        var state = AppSettings.Load();
        if (state.WindowWidth >= 1 && state.WindowHeight >= 1)
        {
            Width = state.WindowWidth;
            Height = state.WindowHeight;
        }
        _normalWidth = Width;
        _normalHeight = Height;
        _startMaximized = state.Maximized;
        Topmost = state.AlwaysOnTop;
        TopmostToggle.IsChecked = state.AlwaysOnTop;

        SizeChanged += (_, _) =>
        {
            // 사용자가 드래그로 바꾼 실제 크기를 추적 (최대화 상태는 제외)
            if (WindowState == WindowState.Normal)
            {
                _normalWidth = ActualWidth;
                _normalHeight = ActualHeight;
            }
        };
        Closing += (_, _) => AppSettings.Save(new AppState
        {
            WindowWidth = _normalWidth,
            WindowHeight = _normalHeight,
            Maximized = WindowState == WindowState.Maximized,
            AlwaysOnTop = Topmost,
        });

        _presets = PresetStore.Load();
        _monitors = DisplayService.GetMonitors();
        _selectedMonitor = _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors.FirstOrDefault();
        RebuildPresetButtons();
    }

    private MonitorInfo? CurrentMonitor => _selectedMonitor;

    /// <summary>창이 생성되면 마우스 커서가 있는 모니터의 정중앙으로 이동.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        CenterOnCursorMonitor();

        // 지난 실행에서 최대화 상태로 닫았으면 그대로 최대화 (커서 모니터 기준)
        if (_startMaximized)
            WindowState = WindowState.Maximized;
    }

    private void CenterOnCursorMonitor()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        if (!NativeMethods.GetCursorPos(out var cursor))
            return;

        IntPtr monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return;

        int workLeft = info.rcWork.Left;
        int workTop = info.rcWork.Top;
        int workWidth = info.rcWork.Right - info.rcWork.Left;
        int workHeight = info.rcWork.Bottom - info.rcWork.Top;
        if (workWidth <= 0 || workHeight <= 0)
            return;

        // 1) 먼저 대상 모니터로 이동만 한다(크기 유지) → WPF 가 그 모니터의 DPI 로 다시 렌더링.
        //    이렇게 해야 이후 측정·배치가 올바른 DPI 기준으로 이뤄진다.
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, workLeft, workTop, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        // 2) 대상 모니터 DPI 가 반영된 실제 픽셀 크기를 읽는다.
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return;
        int winWidth = rect.Right - rect.Left;
        int winHeight = rect.Bottom - rect.Top;
        if (winWidth <= 0 || winHeight <= 0)
            return;

        // 3) 작업 영역보다 크면 줄이고, 정중앙에 배치.
        winWidth = Math.Min(winWidth, workWidth);
        winHeight = Math.Min(winHeight, workHeight);
        int x = workLeft + (workWidth - winWidth) / 2;
        int y = workTop + (workHeight - winHeight) / 2;

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, winWidth, winHeight,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>배치도 캔버스가 준비되면 최초 렌더링.</summary>
    private void LayoutCanvas_Loaded(object sender, RoutedEventArgs e)
    {
        RenderLayout();
        RefreshCurrent();
    }

    /// <summary>창 크기 변경으로 캔버스 폭이 바뀌면 배치도를 다시 그린다.</summary>
    private void LayoutCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => RenderLayout();

    /// <summary>"항상 위" 토글 — 창을 다른 창보다 위에 고정.</summary>
    private void Topmost_Changed(object sender, RoutedEventArgs e)
        => Topmost = TopmostToggle.IsChecked == true;

    /// <summary>모니터 목록을 다시 읽어 선택을 유지한 채 배치도를 갱신.</summary>
    private void ReloadMonitors()
    {
        string? selectedName = _selectedMonitor?.DeviceName;
        _monitors = DisplayService.GetMonitors();
        _selectedMonitor = _monitors.FirstOrDefault(m => m.DeviceName == selectedName)
                           ?? _monitors.FirstOrDefault(m => m.IsPrimary)
                           ?? _monitors.FirstOrDefault();
        RenderLayout();
    }

    /// <summary>각 모니터를 실제 위치/비율대로 캔버스에 상자로 그린다.</summary>
    private void RenderLayout()
    {
        LayoutCanvas.Children.Clear();

        double cw = LayoutCanvas.ActualWidth;
        double ch = LayoutCanvas.ActualHeight;
        if (cw < 1 || ch < 1 || _monitors.Count == 0)
            return;

        // 가상 데스크톱 전체 경계
        int minX = _monitors.Min(m => m.X);
        int minY = _monitors.Min(m => m.Y);
        double vw = Math.Max(1, _monitors.Max(m => m.X + m.Width) - minX);
        double vh = Math.Max(1, _monitors.Max(m => m.Y + m.Height) - minY);

        const double pad = 12;
        double scale = Math.Min((cw - 2 * pad) / vw, (ch - 2 * pad) / vh);
        double offX = (cw - vw * scale) / 2;
        double offY = (ch - vh * scale) / 2;

        foreach (var monitor in _monitors)
            LayoutCanvas.Children.Add(CreateMonitorBox(monitor, minX, minY, scale, offX, offY));
    }

    private Border CreateMonitorBox(
        MonitorInfo monitor, int minX, int minY, double scale, double offX, double offY)
    {
        bool selected = ReferenceEquals(monitor, _selectedMonitor);
        double bw = Math.Max(46, monitor.Width * scale);
        double bh = Math.Max(34, monitor.Height * scale);

        var mode = DisplayService.GetCurrentMode(monitor.DeviceName);

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(new UiTextBlock
        {
            Text = monitor.IsPrimary ? "★ " + monitor.FriendlyName : monitor.FriendlyName,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = bw - 10,
        });
        if (mode is not null)
        {
            stack.Children.Add(new UiTextBlock
            {
                Text = $"{mode.Width} × {mode.Height}",
                FontSize = 11,
                Foreground = selected ? Brushes.White : TextDim,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        var box = new Border
        {
            Width = bw,
            Height = bh,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(selected ? 2.5 : 1),
            BorderBrush = selected ? BoxSelectedBorder : BoxNormalBorder,
            Background = selected ? BoxSelectedBg : BoxNormalBg,
            Cursor = Cursors.Hand,
            ClipToBounds = true,
            ToolTip = $"{monitor.FriendlyName} — 클릭하여 선택",
            Child = stack,
        };
        box.MouseLeftButtonUp += (_, _) => SelectMonitor(monitor);

        Canvas.SetLeft(box, offX + (monitor.X - minX) * scale);
        Canvas.SetTop(box, offY + (monitor.Y - minY) * scale);
        return box;
    }

    private void SelectMonitor(MonitorInfo monitor)
    {
        _selectedMonitor = monitor;
        RenderLayout();
        RefreshCurrent();
    }

    /// <summary>각 물리 모니터에 모니터 이름을 큼지막하게 잠깐 띄워 어느 게 어느 건지 알려준다.</summary>
    private void Identify_Click(object sender, RoutedEventArgs e)
    {
        CloseIdentifyOverlays();

        foreach (var monitor in _monitors)
        {
            if (monitor.Width <= 0 || monitor.Height <= 0)
                continue;

            var overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x3E, 0x6F, 0xC0)),
                Content = new UiTextBlock
                {
                    Text = monitor.FriendlyName,
                    FontSize = 60,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            overlay.Show();

            // 픽셀 좌표로 해당 모니터에 올린 뒤(PerMonitorV2 DPI 인식이라 좌표가 정확함)
            // 최대화하여 모니터 전체를 빈틈없이 덮는다.
            IntPtr hwnd = new WindowInteropHelper(overlay).Handle;
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
                monitor.X, monitor.Y, monitor.Width, monitor.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            overlay.WindowState = WindowState.Maximized;

            _identifyOverlays.Add(overlay);
        }

        // 2.5초 후 자동으로 닫힘
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CloseIdentifyOverlays();
        };
        timer.Start();
    }

    private void CloseIdentifyOverlays()
    {
        foreach (var overlay in _identifyOverlays)
            overlay.Close();
        _identifyOverlays.Clear();
    }

    /// <summary>현재 해상도·배율 표시를 갱신하고 일치하는 프리셋을 강조.</summary>
    private void RefreshCurrent()
    {
        var monitor = CurrentMonitor;
        if (monitor is null)
        {
            CurrentText.Text = "모니터 없음";
            DpiCurrentText.Text = "—";
            DpiPanel.Children.Clear();
            return;
        }

        var mode = DisplayService.GetCurrentMode(monitor.DeviceName);
        CurrentText.Text = mode?.ToString() ?? "알 수 없음";
        HighlightActive(mode);
        RenderDpi(monitor);
    }

    /// <summary>선택한 모니터의 현재 배율과 선택 가능한 배율 버튼을 그린다.</summary>
    private void RenderDpi(MonitorInfo monitor)
    {
        DpiPanel.Children.Clear();

        var info = DisplayConfig.GetDpiScaling(monitor.DeviceName);
        if (!info.IsValid)
        {
            DpiCurrentText.Text = "지원 안 함";
            return;
        }

        DpiCurrentText.Text = $"{info.Current}%";

        foreach (int percent in info.Available)
        {
            bool recommended = percent == info.Recommended;
            var button = new UiButton
            {
                Content = recommended ? $"{percent}%  ·  권장" : $"{percent}%",
                Width = recommended ? 122 : 78,
                Height = 38,
                Margin = new Thickness(0, 0, 8, 8),
                FontSize = 14,
                Appearance = percent == info.Current
                    ? ControlAppearance.Success
                    : ControlAppearance.Secondary,
            };
            int target = percent;
            button.Click += (_, _) => ApplyDpi(target);
            DpiPanel.Children.Add(button);
        }
    }

    private void ApplyDpi(int percent)
    {
        var monitor = CurrentMonitor;
        if (monitor is null)
        {
            SetStatus("선택된 모니터가 없습니다.", ok: false);
            return;
        }

        bool ok = DisplayConfig.SetDpiScaling(monitor.DeviceName, percent);
        SetStatus(
            ok ? $"✓ {monitor.FriendlyName} → 배율 {percent}% 적용 완료"
               : $"✗ 배율 {percent}% 적용 실패",
            ok);
        RefreshCurrent();
    }

    /// <summary>현재 해상도와 일치하는 버튼만 Success 색상으로 표시.</summary>
    private void HighlightActive(DisplayMode? mode)
    {
        foreach (var item in PresetPanel.Children.OfType<Grid>())
        {
            var mainButton = (UiButton)item.Children[0];
            bool active = item.Tag is Resolution r
                          && mode is not null
                          && r.Width == mode.Width && r.Height == mode.Height;

            mainButton.Appearance = active
                ? ControlAppearance.Success
                : ControlAppearance.Secondary;
        }
    }

    private void RebuildPresetButtons()
    {
        PresetPanel.Children.Clear();
        foreach (var res in _presets)
            PresetPanel.Children.Add(CreatePresetItem(res));
    }

    /// <summary>해상도 적용 버튼 + 우상단 × 삭제 버튼을 겹쳐 담은 항목.</summary>
    private Grid CreatePresetItem(Resolution res)
    {
        var grid = new Grid
        {
            Width = 168,
            Height = 76,
            Margin = new Thickness(6),
            Tag = res,
        };

        // 해상도 적용 버튼 (항목 전체를 채움)
        var applyButton = new UiButton
        {
            Appearance = ControlAppearance.Secondary,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new UiTextBlock
            {
                Text = res.ToString(),
                TextAlignment = TextAlignment.Center,
                FontSize = 19,
            },
        };
        applyButton.Click += (_, _) => ApplyResolution(res);

        // × 삭제 버튼 (마우스를 올렸을 때만 표시)
        var deleteButton = new UiButton
        {
            Content = "✕",
            FontSize = 13,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Appearance = ControlAppearance.Danger,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -8, -8, 0),
            Visibility = Visibility.Collapsed,
            ToolTip = $"{res} 삭제",
        };
        deleteButton.Click += (_, _) => DeletePreset(res);

        grid.MouseEnter += (_, _) => deleteButton.Visibility = Visibility.Visible;
        grid.MouseLeave += (_, _) => deleteButton.Visibility = Visibility.Collapsed;

        grid.Children.Add(applyButton);
        grid.Children.Add(deleteButton);
        return grid;
    }

    private void DeletePreset(Resolution res)
    {
        if (!_presets.Remove(res))
            return;

        PresetStore.Save(_presets);
        RebuildPresetButtons();
        RefreshCurrent();
        SetStatus($"프리셋 {res} 삭제됨", ok: true);
    }

    private void ApplyResolution(Resolution res)
    {
        var monitor = CurrentMonitor;
        if (monitor is null)
        {
            SetStatus("선택된 모니터가 없습니다.", ok: false);
            return;
        }

        var result = DisplayService.ChangeResolution(monitor.DeviceName, res.Width, res.Height);
        switch (result)
        {
            case ChangeResult.Success:
                SetStatus($"✓ {monitor.FriendlyName} → {res} 적용 완료", ok: true);
                break;
            case ChangeResult.RequiresRestart:
                SetStatus($"⚠ {res} — 적용하려면 재시작이 필요합니다.", ok: false);
                break;
            case ChangeResult.Unsupported:
                SetStatus($"✗ {res} — 이 모니터가 지원하지 않는 해상도입니다.", ok: false);
                break;
            default:
                SetStatus($"✗ {res} 적용 실패", ok: false);
                break;
        }

        ReloadMonitors();
        RefreshCurrent();
    }

    private void AddCustom_Click(object sender, RoutedEventArgs e) => AddPreset();

    private void CustomBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddPreset();
    }

    private void AddPreset()
    {
        if (!int.TryParse(WidthBox.Text.Trim(), out int width)
            || !int.TryParse(HeightBox.Text.Trim(), out int height)
            || width < 320 || height < 240
            || width > 7680 || height > 4320)
        {
            SetStatus("✗ 올바른 해상도를 입력하세요 (예: 1280 × 720).", ok: false);
            return;
        }

        var res = new Resolution(width, height);
        if (_presets.Contains(res))
        {
            SetStatus($"이미 있는 프리셋입니다: {res}", ok: false);
            return;
        }

        _presets.Add(res);
        PresetStore.Sort(_presets);
        PresetStore.Save(_presets);
        WidthBox.Clear();
        HeightBox.Clear();
        RebuildPresetButtons();
        RefreshCurrent();
        SetStatus($"+ 프리셋 {res} 추가됨", ok: true);
    }

    private void SetStatus(string message, bool ok)
    {
        StatusText.Text = message;
        string resourceKey = ok ? "SystemFillColorSuccessBrush" : "SystemFillColorCriticalBrush";
        StatusText.Foreground = TryFindResource(resourceKey) as Brush
                                ?? StatusText.Foreground;
    }
}
