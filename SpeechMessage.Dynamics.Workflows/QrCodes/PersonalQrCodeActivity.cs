/*
 * 檔案整體說明
 * 目的與定位：P5-09 舊 PersonalQrCodeWorkflow 的薄 Workflow 活動入口，保留必要
 * InputParameter 字串與未設定的 OutputResult 整數；模式及例外交共用 ActivityShellBase。
 * 主要流程：僅 contact 的 ExecuteWorkflow／Create／Update 讀平台主要正式 ID，
 * 由 Core 驗 contact.parentcustomerid 為 account 後入列單張圖片待辦，不畫圖或建附件。
 * 使用邊界與維護：不消費輸入、不設定輸出、不新增深度限制；不保留姓名、來源 Entity、
 * SDK、context、使用者或教會狀態。WF 只快取引數運算式，所有讀取與候選屬當次同步呼叫。
 * 平台 SDK 只借用，不由本檔 Dispose；Core store 清除借用參照，基底 finally 排空並
 * Dispose Shadow recorder。沒有 token 讀取、推播、Session、快取、timer 或背景工作。
 * 缺正式歸屬先拒絕，SDK 未知例外原樣交平台；以離線真 WF／CAS 驗證，不宣稱已產圖。
 */
using System.Activities;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;

namespace SpeechMessage.Dynamics.Workflows.QrCodes;

/// <summary>個人 QR 的相容活動外殼，來源只取平台 contact，不接受 caller 指定教會。</summary>
public sealed class PersonalQrCodeActivity : ActivityShellBase
{
    /// <summary>保持舊必要字串宣告，WF 驗證綁定但規則不消費其值。</summary>
    [RequiredArgument]
    [Input("InputParameter")]
    public InArgument<string> InputParameter { get; set; } = new();
    /// <summary>保持未賦值整數輸出；不能回報附件數或捏造已完成圖片。</summary>
    [Output("OutputResult")]
    public OutArgument<int> OutputResult { get; set; } = new();

    /// <summary>精確三訊息及 contact 表才委派；當次授權 SDK 不保存到活動欄位。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "contact" || input.MessageName is not ("ExecuteWorkflow" or "Create" or "Update")) return;
        _ = QrImageTaskService.EnqueuePersonal(services.Organization, input.PrimaryEntityId);
    }
}
