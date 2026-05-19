using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using static DisplayDeck.NativeMethods;

namespace DisplayDeck;

/// <summary>해상도 + 주사율.</summary>
public record DisplayMode(int Width, int Height, int Frequency)
{
    public override string ToString() => $"{Width} × {Height} @ {Frequency}Hz";
}

/// <summary>물리 모니터 한 대.</summary>
public class MonitorInfo
{
    /// <summary>예: \\.\DISPLAY1 — Win32 API 에 넘기는 식별자.</summary>
    public required string DeviceName { get; init; }

    /// <summary>모니터 모델명 (중복 시 디바이스 번호가 덧붙는다).</summary>
    public required string FriendlyName { get; set; }

    public bool IsPrimary { get; init; }

    // 가상 데스크톱 상의 화면 영역 (픽셀) — "식별" 오버레이 위치 계산용
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public override string ToString() =>
        IsPrimary ? $"{FriendlyName}  ·  주 모니터" : FriendlyName;
}

public enum ChangeResult
{
    Success,
    RequiresRestart,
    /// <summary>해당 모니터가 지원하지 않는 해상도.</summary>
    Unsupported,
    Failed,
}

/// <summary>모니터 열거 및 실제 Windows 해상도 변경.</summary>
public static class DisplayService
{
    /// <summary>데스크톱에 연결된 모든 모니터를 반환.</summary>
    public static List<MonitorInfo> GetMonitors()
    {
        var names = DisplayConfig.GetMonitorNames();
        var result = new List<MonitorInfo>();

        for (uint i = 0; ; i++)
        {
            var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref adapter, 0))
                break;

            if ((adapter.StateFlags & DisplayDeviceStateFlags.AttachedToDesktop) == 0)
                continue;

            // QueryDisplayConfig 에서 받은 모델명, 없으면 내장 디스플레이로 간주
            string model = names.TryGetValue(adapter.DeviceName, out var found)
                           && !string.IsNullOrWhiteSpace(found)
                ? found
                : "내장 디스플레이";

            // 현재 화면 위치/크기 (식별 오버레이용)
            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            EnumDisplaySettings(adapter.DeviceName, ENUM_CURRENT_SETTINGS, ref dm);

            result.Add(new MonitorInfo
            {
                DeviceName = adapter.DeviceName,
                FriendlyName = model,
                IsPrimary = adapter.StateFlags.HasFlag(DisplayDeviceStateFlags.PrimaryDevice),
                X = dm.dmPositionX,
                Y = dm.dmPositionY,
                Width = (int)dm.dmPelsWidth,
                Height = (int)dm.dmPelsHeight,
            });
        }

        // 같은 모델명이 둘 이상이면 디바이스 번호를 붙여 구분
        var duplicates = result
            .GroupBy(m => m.FriendlyName)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        foreach (var monitor in result.Where(m => duplicates.Contains(m.FriendlyName)))
        {
            string tag = monitor.DeviceName.Replace(@"\\.\DISPLAY", "#");
            monitor.FriendlyName = $"{monitor.FriendlyName} ({tag})";
        }

        return result;
    }

    /// <summary>지정한 모니터의 현재 해상도/주사율.</summary>
    public static DisplayMode? GetCurrentMode(string deviceName)
    {
        var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
            return null;

        return new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency);
    }

    /// <summary>실제 Windows 해상도를 변경. 적용 전 CDS_TEST 로 검증.</summary>
    public static ChangeResult ChangeResolution(string deviceName, int width, int height)
    {
        // 현재 모드를 기준으로 삼아 주사율/색심도를 보존
        var current = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref current))
            return ChangeResult.Failed;

        DEVMODE target = current;

        // 요청한 해상도를 지원하는 모드 중 현재 주사율과 가장 가까운 것을 선택
        var match = EnumerateModes(deviceName)
            .Where(m => m.width == width && m.height == height)
            .OrderByDescending(m => m.freq == current.dmDisplayFrequency)
            .ThenByDescending(m => m.freq)
            .Select(m => (DEVMODE?)m.dm)
            .FirstOrDefault();

        if (match is { } found)
        {
            target = found;
        }
        else
        {
            // 열거 목록에 없어도 일단 시도 — CDS_TEST 가 가부를 알려줌
            target.dmPelsWidth = (uint)width;
            target.dmPelsHeight = (uint)height;
        }

        target.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_BITSPERPEL;

        if (ChangeDisplaySettingsEx(deviceName, ref target, IntPtr.Zero, CDS_TEST, IntPtr.Zero)
            != DISP_CHANGE_SUCCESSFUL)
        {
            return ChangeResult.Unsupported;
        }

        int applied = ChangeDisplaySettingsEx(
            deviceName, ref target, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);

        return applied switch
        {
            DISP_CHANGE_SUCCESSFUL => ChangeResult.Success,
            DISP_CHANGE_RESTART => ChangeResult.RequiresRestart,
            DISP_CHANGE_BADMODE => ChangeResult.Unsupported,
            _ => ChangeResult.Failed,
        };
    }

    private static IEnumerable<(int width, int height, uint freq, DEVMODE dm)> EnumerateModes(
        string deviceName)
    {
        for (int i = 0; ; i++)
        {
            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(deviceName, i, ref dm))
                yield break;

            yield return ((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, dm.dmDisplayFrequency, dm);
        }
    }
}
