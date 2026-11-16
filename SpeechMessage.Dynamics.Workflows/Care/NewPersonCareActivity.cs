/*
 * 檔案整體說明
 * 目的與定位：P5-10 新朋友關懷活動的相容外殼，保留退役 CodeActivity 的 required
 * InputParameter 與 OutputResult，以及 ExecuteWorkflow／Create／Update 的訊息分類。
 * 主要流程：共用 ActivityShellBase 先完成模式、服務借用、Shadow recorder 與例外邊界；
 * 本檔只驗證主要資料為 contact，將固定分類交給 NewPersonCareRule；ExecuteWorkflow
 * 建立關懷週報與出席，Create 更新分組日期與歷程，Update 依舊序先出席再分組。
 * Core 以正式教會與資料庫主鍵驗證 scope，週報與出席由單筆交易原子建立；
 * 不搬大型 ToolUtility，不建立外部通知或 token 路徑。
 * 使用邊界與維護：活動不保存已評估輸入、context、Entity、SDK 或跨執行狀態；Shadow
 * 仍由基底確定清理。服務只存在當次同步堆疊，不保存前次會員資料。
 * 未知 SDK 例外原樣向上傳遞；reading 與離線 Shadow 證據不代表實機影子成功。
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

    /// <summary>核對正式 contact 與可達訊息後，依舊序同步執行出席及分組 Core；例外不吞掉。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services)
    {
        if (!string.Equals(input.PrimaryEntityName, "contact", StringComparison.Ordinal)) return;
        var operation = NewPersonCareRule.Operation(input.MessageName);
        if (operation is NewPersonCareOperation.PresentRecord or NewPersonCareOperation.PresentRecordAndAssignGroup)
            new NewPersonPresentRecordService().Execute(services.Organization, input.PrimaryEntityId);
        if (operation is NewPersonCareOperation.AssignGroup or NewPersonCareOperation.PresentRecordAndAssignGroup)
            NewPersonCareAssignmentService.Execute(services.Organization, input.PrimaryEntityId);
    }
}
