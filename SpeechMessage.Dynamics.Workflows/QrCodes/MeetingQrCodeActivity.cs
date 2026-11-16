/*
 * 檔案整體說明
 * 目的與定位：P5-09 舊 SundayQrCodeWorkflow 的薄 Workflow 活動入口，沿用共用外殼
 * 的模式、例外及清理契約，保留必要字串 InputParameter 與未設定的整數 OutputResult。
 * 主要流程：僅平台 new_meeting_statistics 的 ExecuteWorkflow／Create／Update 委派
 * Core 驗全部出席→會友→唯一教會，再建立一筆三十八張圖片待辦；不在沙箱繪圖。
 * 使用邊界與維護：舊深度兩分支皆執行，因此不另設 DepthPolicy；不消費輸入字串，
 * 也不賦值輸出。欄位只持 WF 運算式，不保存 context、SDK、來源、使用者或 token。
 * SDK 只在當次同步借用；Shadow recorder 由基底 finally 輸出並 Dispose，Core store
 * 清除借用參照而不 Dispose 平台服務。缺來源或跨教會先拒絕，未知例外原樣重拋，
 * 無推播、Session、cache、timer 或背景工作。離線真 WF／CAS 驗證不代表實機驗收。
 */
using System.Activities;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;

namespace SpeechMessage.Dynamics.Workflows.QrCodes;

/// <summary>保留舊聚會 QR 參數及無深度限制行為，只將平台正式來源委派 Core。</summary>
public sealed class MeetingQrCodeActivity : ActivityShellBase
{
    /// <summary>必要字串運算式保持舊介面；不讀取、不作為資料列或教會授權。</summary>
    [RequiredArgument]
    [Input("InputParameter")]
    public InArgument<string> InputParameter { get; set; } = new();
    /// <summary>保留舊整數輸出但不賦值，不把入列解讀為圖片完成或成功碼。</summary>
    [Output("OutputResult")]
    public OutArgument<int> OutputResult { get; set; } = new();

    /// <summary>精確比對主要表與訊息；其餘零業務 I/O，不讓 caller 輸入改變來源。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_meeting_statistics" || input.MessageName is not ("ExecuteWorkflow" or "Create" or "Update")) return;
        _ = QrImageTaskService.EnqueueMeeting(services.Organization, input.PrimaryEntityId);
    }
}
