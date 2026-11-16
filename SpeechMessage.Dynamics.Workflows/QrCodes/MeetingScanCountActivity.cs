/*
 * 檔案整體說明
 * 目的與定位：P5-09 舊 CalculateQrCodeScanNumberWorkflow 的薄活動外殼，保留
 * InputParameter 必要字串及未設定的 OutputResult 整數，不將計數結果誤作輸出。
 * 主要流程：僅 new_meeting_statistics 的 ExecuteWorkflow／Create／Update 使用平台
 * 正式主鍵，當次建立無狀態 MeetingScanCountService，完整讀出席後發布三十八欄 patch。
 * 使用邊界與維護：舊 depth 兩分支相同，故不新增限制；不消費輸入、不中途更新輸出。
 * 活動不保存服務、context、來源、計數、使用者或組織資料；候選與服務只活到同步返回。
 * SDK 由平台擁有且只借用，基底是 Shadow recorder 唯一 owner，finally 輸出安全
 * 有界追蹤後 Dispose。無 token、推播、Session、快取、timer、連線或背景工作。
 * 查詢與未知 SDK 例外原樣交共用邊界；不重試不確定寫入。真 WF／CAS 離線案例
 * 驗完整計數意圖及零真寫入，不代表實機授權或資料庫快照驗收。
 */
using System.Activities;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;

namespace SpeechMessage.Dynamics.Workflows.QrCodes;

/// <summary>舊掃描統計的參數相容入口，無跨執行狀態、無額外深度門檻。</summary>
public sealed class MeetingScanCountActivity : ActivityShellBase
{
    /// <summary>必要字串維持 WF metadata，業務不讀取或拿來指定會議。</summary>
    [RequiredArgument]
    [Input("InputParameter")]
    public InArgument<string> InputParameter { get; set; } = new();
    /// <summary>保持舊未賦值輸出，不回傳人次或建立自訂成功碼。</summary>
    [Output("OutputResult")]
    public OutArgument<int> OutputResult { get; set; } = new();

    /// <summary>只委派精確表與三訊息，服務當次 new 且不保存 SDK 或計數候選。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_meeting_statistics" || input.MessageName is not ("ExecuteWorkflow" or "Create" or "Update")) return;
        new MeetingScanCountService().Execute(services.Organization, input.PrimaryEntityId);
    }
}
