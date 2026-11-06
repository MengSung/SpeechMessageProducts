/*
 * 檔案整體說明
 *
 * 目的與定位：以同一份完整文件來源驗證測試命名例外的邊界，只建置、不執行。 產品或缺少任一測試條件時必須得到 CA1707；只有符合集中規則的測試專案可編譯。
 *
 * 主要功能與流程：
 * 本檔宣告 NamingProbe。
 * - 刻意保留計畫指定底線格式，讓編譯器診斷成為唯一的命名守門依據。
 * 主要操作入口包含 Operation_Condition_ExpectedResult。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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
