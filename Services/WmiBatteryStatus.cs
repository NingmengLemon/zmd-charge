namespace EndfieldCharge.Services;

/// <summary>
/// Win32_Battery.BatteryStatus 的语义映射。
///
/// 值本身来自 WMI 文档：
///   1 放电 | 2 接 AC 但不一定在充 | 3 已充满 | 4 低 | 5 危急
///   6 充电中 | 7 充电且高 | 8 充电且低 | 9 充电且危急 | 10 未定义 | 11 部分充电
///
/// 刻意放在平台无关处、且是纯函数：这些是"数据语义"而不是平台 API，
/// 放在 Windows 专有的读取器里会连带沾上 [SupportedOSPlatform("windows")]，
/// 让单测在任何平台上都触发 CA1416。状态码 2/3/11 是"接了电但没在充"，
/// 不能被当成 Charging，提醒逻辑依赖这个区别。
/// </summary>
internal static class WmiBatteryStatus
{
    internal static bool IsAcOnline(ushort status) => status is 2 or 3 or 6 or 7 or 8 or 9 or 11;

    internal static bool IsCharging(ushort status) => status is 6 or 7 or 8 or 9;
}
