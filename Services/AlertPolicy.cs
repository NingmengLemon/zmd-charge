namespace EndfieldCharge.Services;

/// <summary>该弹哪个提醒。</summary>
internal enum AlertKind
{
    LowBattery,
    FullCharge,
}

/// <summary>
/// 提醒判定。抽成纯函数是为了能单测：这段逻辑曾经挂在 HUD 触发上，
/// 而放电过程中根本没有 HUD 事件，导致两个提醒实际上不会弹。
/// </summary>
internal static class AlertPolicy
{
    /// <summary>充满判定的电量阈值（%）。</summary>
    internal const int FullChargePercent = 99;

    /// <summary>
    /// 本轮该弹的提醒；null 表示不弹。
    /// 注意两个条件都用 AcOnline 而不是 Charging：powrprof 的 Charging 表示
    /// "正在充"，电池充满后 Windows 会把它置 0，拿它判断等于永不触发。
    /// </summary>
    internal static AlertKind? Decide(
        BatterySnapshot snap,
        int lowThreshold,
        bool lowEnabled,
        bool fullEnabled,
        bool lowAlreadyNotified,
        bool fullAlreadyNotified)
    {
        if (!snap.HasBattery)
            return null;

        if (fullEnabled && snap.AcOnline && snap.Percent >= FullChargePercent && !fullAlreadyNotified)
            return AlertKind.FullCharge;

        if (lowEnabled && !snap.AcOnline && snap.Percent <= lowThreshold && !lowAlreadyNotified)
            return AlertKind.LowBattery;

        return null;
    }
}
