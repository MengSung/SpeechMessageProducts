namespace SpeechMessage.Localization;

/// <summary>
/// 僅解析編譯時系統文字的共用介面，net48 與 net10 使用同一份契約。
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
