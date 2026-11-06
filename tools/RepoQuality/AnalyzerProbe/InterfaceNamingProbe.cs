/*
 * 檔案整體說明
 *
 * 目的與定位：本檔整合下列相關契約：IInterfaceNamingProbe：刻意使用跨語言保留字的公開介面，僅供編譯器證明 net48 已載入 Recommended 規則。
 *
 * 主要功能與流程：
 * 本檔宣告 IInterfaceNamingProbe。
 * - 此故障注入必須保持 Get 原名，預期編譯失敗且只有 CA1716，不執行或發布此介面。
 * 資料／選項欄位包含 IInterfaceNamingProbe，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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
