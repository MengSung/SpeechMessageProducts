/*
 * 檔案整體說明
 * 目的與定位：P5-10 新朋友關懷活動的相容外殼，保留退役 CodeActivity 的 required
 * InputParameter 與 OutputResult，以及 ExecuteWorkflow／Create／Update 的訊息分類。
 * 主要流程：共用 ActivityShellBase 先完成模式、服務借用、Shadow recorder 與例外邊界；
 * 本檔只驗證主要資料為 contact，將固定分類交給 NewPersonCareRule。舊入口依賴約萬行
 * ToolUtility、建立週報／分組及外部通知，尚未由既有 P5-06／P5-07 產出可安全重用的
 * 單一 Core service，因此本活動不猜測搬移、不中途建立新的連外或 token 路徑。
 * 使用邊界與維護：活動不保存已評估輸入、context、Entity、SDK 或跨執行狀態；Shadow
 * 仍由基底確定清理。此檔目前提供相容註冊與可達分支證據，實際新朋友資料動作須待
 * 對應 P5 規則承接後再接線，不宣稱已完成舊週報／分組副作用。
 */
using System;
using System.Activities;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Care;

namespace SpeechMessage.Dynamics.Workflows.Care;

/// <summary>保留 NewPersonCare 舊活動參數與訊息邊界的相容入口。</summary>
public sealed class NewPersonCareActivity : ActivityShellBase
{
    /// <summary>退役活動的必要字串參數；保留宣告但不作授權或路由來源。</summary>
    [RequiredArgument]
    [Input("InputParameter")]
    public InArgument<string> InputParameter { get; set; } = new();

    /// <summary>退役活動未設定的整數輸出；保留平台相容性，不捏造成功碼。</summary>
    [Output("OutputResult")]
    public OutArgument<int> OutputResult { get; set; } = new();

    /// <summary>只核對舊入口的 contact 與可達訊息分類；不搬入退役 ToolUtility 或外部通知。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services)
    {
        if (!string.Equals(input.PrimaryEntityName, "contact", StringComparison.Ordinal)) return;
        var operation = NewPersonCareRule.Operation(input.MessageName);
        if (operation is NewPersonCareOperation.AssignGroup or NewPersonCareOperation.PresentRecordAndAssignGroup)
            NewPersonCareAssignmentService.Execute(services.Organization, input.PrimaryEntityId);
    }
}
