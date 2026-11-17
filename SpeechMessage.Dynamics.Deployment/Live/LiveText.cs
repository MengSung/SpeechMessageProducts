/*
檔案整體說明
目的與定位：P7 Live三工具的固定資源讀取入口，文字只由同模組resx生成，不修改共用24鍵表。
主要流程：以唯一SystemTextTable解析完整階層鍵，未知鍵拒絕，不自行Replace或猜測語系。
資源與隔離：表只含編譯時固定資料，不保存組織、SDK、credential或參數，沒有背景資源。
維護驗證：只改resx後重生及verify，CLI測試檢查不可用trace與未檢查項目不被冒稱成功。
*/
using SpeechMessage.Localization;
namespace SpeechMessage.Dynamics.Deployment.Live;
/// <summary>固定繁中表，與教會覆寫及請求狀態完全無關。</summary>
internal static class LiveText
{
    private static readonly SystemTextTable Texts = SystemTextTable.FromRows(LiveTextData.Cultures, LiveTextData.Rows);
    /// <summary>鍵僅來自本組件固定呼叫點，缺鍵即失敗。</summary>
    internal static string Get(string key) => Texts.GetText("zh-TW", new TextKey("Deployment.Live." + key));
}
