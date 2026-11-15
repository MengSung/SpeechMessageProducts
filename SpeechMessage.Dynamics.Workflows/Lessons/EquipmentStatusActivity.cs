/*
 * 檔案整體說明
 * 目的與定位：P5-07設備狀態的薄自訂活動，沿用舊InputParameter字串及OutputResult整數
 * 宣告，主規則委派Core.Lessons；舊程式沒有使用輸入或設定輸出，新版不憑名稱增加效果。
 * 主要流程：ActivityShellBase以平台UserId借SDK並提供當次ActivityInput；確認來源表及
 * 訊息後同步呼叫LessonEquipmentService。舊Depth兩分支都執行，因此不增設深度限制。
 * 使用邊界與維護：活動欄位只保存WF參數運算式，不保存參數值、SDK、Entity或會友。
 * 查詢及更新候選由Core單次有界持有；SDK由平台擁有，Shadow recorder由外殼finally
 * 排空／Dispose。沒有背景Task、timer、鎖或Session，異常保留交共用外殼處理。
 * 離線真活動驗證雙模式及錯配拒絕，不代表實機追蹤或正式啟用；本階段只停用註冊。
 */
using System.Activities;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;

namespace SpeechMessage.Dynamics.Workflows.Lessons;

/// <summary>設備狀態活動；不以活動引數覆寫平台主要記錄或教會權限。</summary>
public sealed class EquipmentStatusActivity : ActivityShellBase
{
    /// <summary>保留舊必要字串宣告；舊設備規則未使用此值，新版也不保存或當授權。</summary>
    [RequiredArgument]
    [Input("InputParameter")]
    public InArgument<string> InputParameter { get; set; } = new();
    /// <summary>保留舊整數輸出；舊程式未設定，因此維持WF預設值，不宣稱業務成功碼。</summary>
    [Output("OutputResult")]
    public OutArgument<int> OutputResult { get; set; } = new();
    /// <summary>只接受已核對的來源表及舊活動實際訊息，平台SDK僅於同步規則期間借用。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_stor_lessons" || input.MessageName is not ("ExecuteWorkflow" or "Create" or "Update")) return;
        LessonEquipmentService.Update(services.Organization, input.PrimaryEntityId);
    }
}
