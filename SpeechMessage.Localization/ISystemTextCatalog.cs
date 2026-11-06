/*
 * 檔案整體說明
 *
 * 目的與定位：僅解析編譯時系統文字的共用介面，net462 與 net10 使用同一份契約。 不讀取教會覆寫、成員語言、身分或連線；選擇語系不代表通過任何授權。
 *
 * 主要功能與流程：
 * 本檔宣告 ISystemTextCatalog。
 * - 未知語系回 zh-TW，缺鍵直接失敗；不以動態快取保留呼叫者語系。
 * 資料／選項欄位包含 ISystemTextCatalog，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Localization/SystemTextTable.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Localization;

/// <summary>
/// 僅解析編譯時系統文字的共用介面，net462 與 net10 使用同一份契約。
/// 不讀取教會覆寫、成員語言、身分或連線；選擇語系不代表通過任何授權。
/// </summary>
public interface ISystemTextCatalog
{
    /// <summary>未知語系回 zh-TW，缺鍵直接失敗；不以動態快取保留呼叫者語系。</summary>
    /// <param name="culture">文字語系，未知或空值均採中性繁體中文。</param>
    /// <param name="key">已驗證的固定鍵；預設值不會命中資源。</param>
    /// <returns>不可變的系統文字，參數保持原樣，格式化留待後續階段。</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">系統字典沒有指定鍵。</exception>
    string GetText(string? culture, TextKey key);
}
