namespace SpeechMessage.RepoQuality.AnalyzerProbe;

/// <summary>
/// 以刻意違反時間來源契約的程式證明禁用 API 分析器確實參與建置。
/// 此專案不加入正式方案，也不執行；測試以建置失敗且診斷為 RS0030 作為成功條件，
/// 避免分析器沒有載入時仍將可編譯的程式誤判為守門通過。
/// </summary>
public static class ClockProbe
{
    /// <summary>
    /// 依編譯符號注入單一時間、全域文化或字串比較違規，讓測試辨認每項規則的診斷。
    /// 沒有指定符號時固定讀取當地時間；各分支僅供編譯，不執行或修改程序文化。
    /// 不可移除違規呼叫，否則檢查腳本必須因缺少預期錯誤而失敗。
    /// </summary>
    public static void Trigger()
    {
#if PROBE_TODAY
        _ = System.DateTime.Today;
#elif PROBE_LOCAL_TIME
        _ = default(System.DateTime).ToLocalTime();
#elif PROBE_TIME_ZONE
        _ = System.TimeZoneInfo.Local;
#elif PROBE_CULTURE
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
#elif PROBE_UI_CULTURE
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
#elif PROBE_COMPARISON
        _ = string.Compare("first", "second", System.StringComparison.CurrentCulture);
#elif PROBE_SEARCH
        _ = "haystack".IndexOf("needle");
#elif PROBE_ORDINAL_OVERLOAD
        _ = "haystack".Contains("needle");
#elif PROBE_DRAWING
        _ = System.Drawing.Point.Empty;
#else
        _ = System.DateTime.Now;
#endif
    }
}
