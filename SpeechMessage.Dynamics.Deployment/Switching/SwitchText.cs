/*
檔案整體說明
目的與定位：P7切換計畫固定繁中資源的窄讀取入口，沿本模組SwitchText.resx與既有I18n產生器，不建立第二套資源解析或提高基準。
主要流程：從既有SystemTextTable取得Deployment.Switch階層鍵與具名格式摘要，缺鍵即失敗，不把鍵名當假成功輸出。
資源與隔離：唯一固定唯讀表只含編譯時資源，不含組織／使用者／credential或查詢結果；沒有I/O、訂閱、背景工作或Dispose資源。
失敗與驗證：資源缺失與參數不符向上回報，SwitchCliTests驗五段輸出；I18n generate --check與verify驗唯一鍵、參數及可重生資料。
*/
using SpeechMessage.Localization;
namespace SpeechMessage.Dynamics.Deployment.Switching;

/// <summary>只委派既有不可變系統文字表；固定資源可共享，但本次格式化參數不保存。</summary>
internal sealed class SwitchText
{
    private static readonly SystemTextTable Texts = SystemTextTable.FromRows(SwitchTextData.Cultures, SwitchTextData.Rows);
    /// <summary>固定繁中資源無教會覆寫或執行期文化，鍵只由本組件的固定呼叫點提供。</summary>
    internal string this[string key] => Texts.GetText("zh-TW", new TextKey("Deployment.Switch." + key));
    /// <summary>具名筆數參數只活在本次格式化呼叫，沿唯一formatter驗缺參數／額外參數，不自行Replace。</summary>
    internal static string Summary(int newSteps, int oldSteps, int workflows, int decisions) =>
        Texts.GetFormattedText("zh-TW", new TextKey("Deployment.Switch.Summary"),
            new TextArg("newSteps", newSteps), new TextArg("oldSteps", oldSteps), new TextArg("workflows", workflows), new TextArg("decisions", decisions));
}
