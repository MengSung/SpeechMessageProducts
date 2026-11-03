#if PROBE_INTERFACE_KEYWORD
namespace SpeechMessage.RepoQuality.AnalyzerProbe;

/// <summary>刻意使用跨語言保留字的公開介面，僅供編譯器證明 net48 已載入 Recommended 規則。</summary>
public interface IInterfaceNamingProbe
{
    /// <summary>此故障注入必須保持 Get 原名，預期編譯失敗且只有 CA1716，不執行或發布此介面。</summary>
    /// <returns>探測不執行，因此沒有實際回傳值。</returns>
    string Get();
}
#endif
