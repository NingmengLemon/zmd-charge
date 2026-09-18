using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EndfieldCharge.Services;

/// <summary>
/// Windows 电源 / 电池相关的原生 API。
/// 两条读取路径：
///   1. powrprof!CallNtPowerInformation(SystemBatteryState) —— 主路径，同步、无 WMI 开销，直接给 mWh。
///   2. WMI Win32_Battery —— 兜底，部分机型 powrprof 返回 MaxCapacity=0。
/// 一条监听路径：
///   RegisterPowerSettingNotification + 隐藏消息窗，接收 WM_POWERBROADCAST / PBT_POWERSETTINGCHANGE。
///
/// 所有 P/Invoke 都用 [LibraryImport]（编译期源生成封送代码）而不是 [DllImport]（运行期 IL stub）：
/// 前者没有首次调用的 stub 生成开销，也不依赖运行期反射，是 .NET 7+ 的推荐写法。
/// 代价是所在类型必须 partial、方法必须 partial。
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class PowerNative
{
    // ---------- CallNtPowerInformation ----------

    private const int SystemBatteryStateLevel = 5;

    /// <summary>Windows 电池状态（InformationLevel = SystemBatteryState）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SystemBatteryState
    {
        public byte AcOnLine;
        public byte BatteryPresent;
        public byte Charging;
        public byte Discharging;
        public byte Spare1;
        public byte Spare2;
        public byte Spare3;
        public byte Spare4;

        /// <summary>满充容量，毫瓦时（mWh）。</summary>
        public uint MaxCapacity;

        /// <summary>剩余容量，毫瓦时（mWh）。</summary>
        public uint RemainingCapacity;

        /// <summary>充放电速率，毫瓦（mW）。正=充电，负=放电。</summary>
        public int Rate;

        /// <summary>剩余时间估计，秒。未知时为 0x80000000。</summary>
        public uint EstimatedTime;

        public uint DefaultAlert1;
        public uint DefaultAlert2;
    }

    /// <summary>输出缓冲区直接声明成 out 结构体，由源生成器负责取地址，不用手工 AllocHGlobal。</summary>
    [LibraryImport("powrprof.dll", SetLastError = true)]
    private static partial uint CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        int inputBufferSize,
        out SystemBatteryState outputBuffer,
        int outputBufferSize);

    private static int _batteryFailureLogged;

    /// <summary>读取系统电池状态。返回 false 表示无电池或读取失败。</summary>
    public static bool TryGetBatteryState(out SystemBatteryState state)
    {
        state = default;
        try
        {
            // NTSTATUS：0 = STATUS_SUCCESS
            if (CallNtPowerInformation(
                    SystemBatteryStateLevel, IntPtr.Zero, 0,
                    out var result, Marshal.SizeOf<SystemBatteryState>()) != 0)
                return false;

            state = result;
            return state.BatteryPresent != 0 && state.MaxCapacity > 0;
        }
        catch
        {
            // 这个函数会被 30 秒一次的提醒轮询反复调用，失败时每次记一行会迅速刷爆日志，
            // 所以只在进程内第一次失败时记一次。
            Logger.Once(ref _batteryFailureLogged,
                "powrprof CallNtPowerInformation 调用失败，电池信息退回 WMI 路径");
            return false;
        }
    }

    /// <summary>只取 AC 是否在线（不依赖电池存在）。</summary>
    public static bool TryGetAcOnline(out bool acOnline)
    {
        acOnline = false;
        if (!TryGetBatteryState(out var s))
            return false;
        acOnline = s.AcOnLine != 0;
        return true;
    }

    // ---------- 电源设置通知 ----------

    /// <summary>GUID_ACDC_POWER_SOURCE：交流电 / 电池供电切换。</summary>
    public static readonly Guid GuidAcdcPowerSource = new("5d3e9a59-e9d5-4b00-a6bd-ff34ff516548");

    /// <summary>GUID_BATTERY_PERCENTAGE_REMAINING：电量百分比变化。</summary>
    public static readonly Guid GuidBatteryPercentageRemaining = new("a7ad8041-b45a-4cae-87a3-eecbb468a9e1");

    /// <summary>GUID_POWER_SAVING_STATUS：省电模式开/关。Data: 1=开, 0=关。
    /// 值来自 WinNT.h（E00958C0-C213-4ACE-AC77-FECCED2EEEA5），写错将永远收不到通知。</summary>
    public static readonly Guid GuidPowerSavingStatus = new("e00958c0-c213-4ace-ac77-fecced2eeea5");

    /// <summary>
    /// GUID_ENERGY_SAVER_STATUS：节能模式状态（24H2 / build 26100+ 取代省电模式）。
    /// Data: 0=ENERGY_SAVER_OFF, 1=STANDARD, 2=HIGH_SAVINGS（非 0 即开启）。
    ///
    /// 已知缺陷，不要依赖这条路径：本常量的值没有出处。它不在 Windows SDK 头文件里
    /// （10.0.19041 / 10.0.22621 的 winnt.h 只有 GUID_ENERGY_SAVER_SUBGROUP /
    /// _BATTERY_THRESHOLD / _BRIGHTNESS / _POLICY，整个 Include 树里搜不到
    /// ENERGY_SAVER_STATUS），值本身又是 RFC 4122 的示例 UUID
    /// 550e8400-e29b-41d4-a716-446655440000。因此拿它去调
    /// RegisterPowerSettingNotification 收不到任何通知，节能模式提示在 24H2+ 上实际是死的。
    /// 保留而不删除，是因为删除前需要先确认 24H2+ 上正确的替代方式。
    /// </summary>
    public static readonly Guid GuidEnergySaverStatus = new("550e8400-e29b-41d4-a716-446655440000");

    private static int _saverFailureLogged;

    /// <summary>读取省电/节能模式当前是否开启。
    /// 24H2+：节能模式状态在注册表 EnergySaverState（实测 1=开, 2=关）。
    /// 旧系统：GetSystemPowerStatus.SystemStatusFlag（1=开）。</summary>
    public static bool TryGetPowerSavingStatus(out bool enabled)
    {
        enabled = false;
        try
        {
            if (Environment.OSVersion.Version.Build >= 26100)
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Power");
                if (key?.GetValue("EnergySaverState") is int v)
                {
                    enabled = v == 1;
                    return true;
                }
            }

            if (GetSystemPowerStatus(out var sps))
            {
                enabled = sps.SystemStatusFlag == 1;
                return true;
            }
        }
        catch
        {
            // 同上：这个函数会被 2 秒一次的兜底轮询反复调用，只在首次失败时记一次
            Logger.Once(ref _saverFailureLogged, "读取省电模式状态失败，省电模式提示将不可用");
            return false;
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        /// <summary>1 = 省电模式开启，0 = 关闭。</summary>
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus lpSystemPowerStatus);

    public const int WmPowerBroadcast = 0x0218;
    public const int PbtPowerSettingChange = 0x8013;
    public const int PbtApmPowerStatusChange = 0x000A;
    public const int DeviceNotifyWindowHandle = 0x00000000;

    public const int AcPowerSource = 1;
    public const int DcPowerSource = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct PowerBroadcastSetting
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr RegisterPowerSettingNotification(
        IntPtr hRecipient,
        ref Guid powerSettingGuid,
        int flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterPowerSettingNotification(IntPtr handle);

    // ---------- 隐藏消息窗 ----------

    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// WNDCLASSEXW。字符串字段刻意用 IntPtr 而不是 string：
    /// 一是让整个结构保持 blittable，源生成器不需要为它生成任何封送代码；
    /// 二是字符串生存期变成显式可见的（调用方自己 StringToHGlobalUni / FreeHGlobal），
    /// 不会出现「封送器每次调用偷偷分配一份」这种看不见的开销。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WndClassEx
    {
        public uint CbSize;
        public uint Style;
        public IntPtr LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public IntPtr HInstance;
        public IntPtr HIcon;
        public IntPtr HCursor;
        public IntPtr HbrBackground;
        public IntPtr LpszMenuName;
        public IntPtr LpszClassName;
        public IntPtr HIconSm;
    }

    /// <summary>HWND_MESSAGE：创建一个只收消息、不可见的 message-only 窗口。</summary>
    public static readonly IntPtr HwndMessage = new(-3);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial ushort RegisterClassExW(ref WndClassEx lpwcx);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string? lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    public static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMessageW(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(ref Msg lpMsg);

    [LibraryImport("user32.dll")]
    public static partial IntPtr DispatchMessageW(ref Msg lpMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr HWnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    public const uint WmQuit = 0x0012;
    public const uint WmApp = 0x8000;

    // ---------- 壳通知（托盘图标弹气泡用，可选） ----------

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandleW(string? lpModuleName);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();
}
