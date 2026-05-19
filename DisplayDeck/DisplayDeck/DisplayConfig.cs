using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace DisplayDeck;

/// <summary>모니터의 현재 배율과 선택 가능한 배율 목록.</summary>
public class DpiScalingInfo
{
    public bool IsValid { get; init; }
    public int Current { get; init; }
    public int Recommended { get; init; }
    public IReadOnlyList<int> Available { get; init; } = new int[0];
}

/// <summary>
/// QueryDisplayConfig API 로 모니터 모델명과 DPI 배율을 조회/변경한다.
/// DPI 배율 변경은 공식 API 가 없어 잘 알려진 비공개 DisplayConfig 타입을 사용한다.
/// </summary>
internal static class DisplayConfig
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const uint DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint DEVICE_INFO_GET_TARGET_NAME = 2;

    // 비공개 타입: 소스 DPI 배율 조회(-3) / 설정(-4)
    private const uint DEVICE_INFO_GET_DPI = unchecked((uint)-3);
    private const uint DEVICE_INFO_SET_DPI = unchecked((uint)-4);

    /// <summary>Windows 가 지원하는 배율(%) 사다리.</summary>
    private static readonly int[] DpiLadder =
        { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathSourceInfo
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathTargetInfo
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public uint refreshRateNumerator;
        public uint refreshRateDenominator;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public PathSourceInfo sourceInfo;
        public PathTargetInfo targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceDeviceName
    {
        public DeviceInfoHeader header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetDeviceName
    {
        public DeviceInfoHeader header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DpiScaleGet
    {
        public DeviceInfoHeader header;
        public int minScaleRel;
        public int curScaleRel;
        public int maxScaleRel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DpiScaleSet
    {
        public DeviceInfoHeader header;
        public int scaleRel;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] PathInfo[] paths,
        ref uint numMode, IntPtr modeArray, IntPtr currentTopology);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetSourceName(ref SourceDeviceName info);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetTargetName(ref TargetDeviceName info);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetDpiScale(ref DpiScaleGet info);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")]
    private static extern int SetDpiScale(ref DpiScaleSet info);

    /// <summary>활성 디스플레이 경로의 소스 정보 (GDI 이름 + adapter/source 식별자).</summary>
    private struct SourceEntry
    {
        public string GdiName;
        public LUID SourceAdapterId;
        public uint SourceId;
        public LUID TargetAdapterId;
        public uint TargetId;
    }

    private static List<SourceEntry> EnumerateSources()
    {
        var list = new List<SourceEntry>();

        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint numPath, out uint numMode) != 0)
            return list;

        var paths = new PathInfo[numPath];

        // 모드 배열은 쓰지 않으므로 넉넉한 버퍼만 넘긴다
        IntPtr modeBuffer = Marshal.AllocHGlobal((int)numMode * 256);
        try
        {
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPath, paths,
                    ref numMode, modeBuffer, IntPtr.Zero) != 0)
                return list;
        }
        finally
        {
            Marshal.FreeHGlobal(modeBuffer);
        }

        for (int i = 0; i < numPath; i++)
        {
            var source = new SourceDeviceName
            {
                header = new DeviceInfoHeader
                {
                    type = DEVICE_INFO_GET_SOURCE_NAME,
                    size = (uint)Marshal.SizeOf<SourceDeviceName>(),
                    adapterId = paths[i].sourceInfo.adapterId,
                    id = paths[i].sourceInfo.id,
                },
            };
            if (GetSourceName(ref source) != 0 || string.IsNullOrEmpty(source.viewGdiDeviceName))
                continue;

            list.Add(new SourceEntry
            {
                GdiName = source.viewGdiDeviceName,
                SourceAdapterId = paths[i].sourceInfo.adapterId,
                SourceId = paths[i].sourceInfo.id,
                TargetAdapterId = paths[i].targetInfo.adapterId,
                TargetId = paths[i].targetInfo.id,
            });
        }

        return list;
    }

    /// <summary>GDI 디바이스 이름(\\.\DISPLAYn) → 모니터 모델명. 모델명이 없으면 빈 문자열.</summary>
    public static Dictionary<string, string> GetMonitorNames()
    {
        var map = new Dictionary<string, string>();

        try
        {
            foreach (var source in EnumerateSources())
            {
                var target = new TargetDeviceName
                {
                    header = new DeviceInfoHeader
                    {
                        type = DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<TargetDeviceName>(),
                        adapterId = source.TargetAdapterId,
                        id = source.TargetId,
                    },
                };
                string name = GetTargetName(ref target) == 0
                    ? target.monitorFriendlyDeviceName?.Trim() ?? ""
                    : "";

                map[source.GdiName] = name;
            }
        }
        catch
        {
            // 조회 실패 시 빈 맵 — 호출 측에서 폴백 이름을 쓴다
        }

        return map;
    }

    /// <summary>지정한 모니터의 현재 배율과 선택 가능한 배율 목록을 조회.</summary>
    public static DpiScalingInfo GetDpiScaling(string gdiDeviceName)
    {
        try
        {
            var source = EnumerateSources().FirstOrDefault(s => s.GdiName == gdiDeviceName);
            if (source.GdiName is null)
                return new DpiScalingInfo();

            var packet = new DpiScaleGet
            {
                header = new DeviceInfoHeader
                {
                    type = DEVICE_INFO_GET_DPI,
                    size = (uint)Marshal.SizeOf<DpiScaleGet>(),
                    adapterId = source.SourceAdapterId,
                    id = source.SourceId,
                },
            };
            if (GetDpiScale(ref packet) != 0)
                return new DpiScalingInfo();

            int cur = packet.curScaleRel;
            if (cur < packet.minScaleRel) cur = packet.minScaleRel;
            if (cur > packet.maxScaleRel) cur = packet.maxScaleRel;

            // 권장 배율은 상대값 0 → 사다리 인덱스 minAbs 에 위치
            int minAbs = System.Math.Abs(packet.minScaleRel);
            int recommendedIdx = minAbs;
            int currentIdx = minAbs + cur;
            int maxIdx = minAbs + packet.maxScaleRel;

            if (recommendedIdx < 0 || maxIdx >= DpiLadder.Length
                || currentIdx < 0 || currentIdx >= DpiLadder.Length)
                return new DpiScalingInfo();

            return new DpiScalingInfo
            {
                IsValid = true,
                Current = DpiLadder[currentIdx],
                Recommended = DpiLadder[recommendedIdx],
                Available = DpiLadder.Take(maxIdx + 1).ToArray(),
            };
        }
        catch
        {
            return new DpiScalingInfo();
        }
    }

    /// <summary>지정한 모니터의 배율(%)을 변경. 성공하면 true.</summary>
    public static bool SetDpiScaling(string gdiDeviceName, int percent)
    {
        try
        {
            var source = EnumerateSources().FirstOrDefault(s => s.GdiName == gdiDeviceName);
            if (source.GdiName is null)
                return false;

            // 현재 GET 으로 권장 배율(상대 0) 기준을 파악
            var get = new DpiScaleGet
            {
                header = new DeviceInfoHeader
                {
                    type = DEVICE_INFO_GET_DPI,
                    size = (uint)Marshal.SizeOf<DpiScaleGet>(),
                    adapterId = source.SourceAdapterId,
                    id = source.SourceId,
                },
            };
            if (GetDpiScale(ref get) != 0)
                return false;

            int targetIdx = System.Array.IndexOf(DpiLadder, percent);
            if (targetIdx < 0)
                return false;

            int minAbs = System.Math.Abs(get.minScaleRel);
            int scaleRel = targetIdx - minAbs;
            if (scaleRel < get.minScaleRel) scaleRel = get.minScaleRel;
            if (scaleRel > get.maxScaleRel) scaleRel = get.maxScaleRel;

            var set = new DpiScaleSet
            {
                header = new DeviceInfoHeader
                {
                    type = DEVICE_INFO_SET_DPI,
                    size = (uint)Marshal.SizeOf<DpiScaleSet>(),
                    adapterId = source.SourceAdapterId,
                    id = source.SourceId,
                },
                scaleRel = scaleRel,
            };
            return SetDpiScale(ref set) == 0;
        }
        catch
        {
            return false;
        }
    }
}
