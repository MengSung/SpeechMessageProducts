/*
 * 檔案整體說明
 * 目的與定位：P5-07課程閱讀相容文字的單一資源入口，提供進階診斷、歷史欄位
 * 分派值與簽核Flex原文。正式資料只來自產生器驗證的GoodShepherdText資源表。
 * 主要功能與流程：呼叫端傳固定ASCII資源鍵，使用中性繁體中文取得原文；
 * 保留舊空白、錯字及CRLF，不把業務條件詞依顯示文化翻譯而改變分派。
 * 使用邊界與維護：靜態表只有版本控制內的固定文字，沒有教會覆寫、使用者、
 * SDK、credential、context或可變通知模型。資源大小隨版控有界，生命週期為
 * 組件載入期間，沒有訂閱、timer、stream或Dispose責任。每次返回不可變字串。
 * 缺鍵沿SystemTextTable失敗而不猜後備文字；變更須執行I18nCheck生成與
 * deterministic verify，並由雙目標reading測試確認原文未變，不代表實機驗收。
 */
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Resources;

namespace SpeechMessageProducts.GoodShepherd.Core.Lessons;

/// <summary>只查固定編譯資源的內部入口，不接受或保存任何租戶與請求狀態。</summary>
internal static class LessonText
{
    /// <summary>私有唯讀產品表，不把生成陣列或字典發布給呼叫端。</summary>
    private static readonly SystemTextTable Texts =
        SystemTextTable.FromRows(GoodShepherdTextData.Cultures, GoodShepherdTextData.Rows);

    /// <summary>固定繁中閱讀契約；鍵不存在即拋出，不將未知鍵當作可顯示文字。</summary>
    internal static string Get(string key) => Texts.GetText("zh-TW", new TextKey(key));
}
