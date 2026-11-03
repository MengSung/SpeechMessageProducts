#if PROBE_NAMING
namespace SpeechMessage.RepoQuality.AnalyzerProbe;

/// <summary>
/// 以同一份完整文件來源驗證測試命名例外的邊界，只建置、不執行。
/// 產品或缺少任一測試條件時必須得到 CA1707；只有符合集中規則的測試專案可編譯。
/// </summary>
public static class NamingProbe
{
    /// <summary>刻意保留計畫指定底線格式，讓編譯器診斷成為唯一的命名守門依據。</summary>
    public static void Operation_Condition_ExpectedResult()
    {
    }
}
#endif
