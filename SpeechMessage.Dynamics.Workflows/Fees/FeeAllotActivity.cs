/*
 * 檔案整體說明
 *
 * 目的與定位：P5-11 撥款查詢的新版 CodeActivity 外殼，承接退役 AllotWorkflow
 * 與 AllotTlsWorkflow 的觸發方向。活動只保存 WF 引數宣告，平台服務、來源識別、
 * 教會授權及待辦建立均限於一次 Execute。
 *
 * 主要功能與流程：接受 new_fee 的 ExecuteWorkflow／Create／Update 來源，忽略不可
 * 作為授權的輸入文字，委派 FeeAllotTaskService 解析正式 fee 關聯並建立每日待辦；
 * 未來付款日正常結束且不入列。Shadow 使用 RecordingOrganizationService，Live 不
 * 啟用新步驟，離線測試不冒充實機付款查詢。
 *
 * 使用邊界與維護：ActivityShellBase 管理 context、服務與 recorder 的排空及 Dispose；
 * 活動實例不保存 SDK、Entity、Session、token、Timer 或 Task。待辦唯一性由資料庫
 * 替代鍵負責，不以程序鎖或活動欄位保存狀態。
 */
using System.Activities;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Fees;
using SpeechMessage.Localization;

namespace SpeechMessage.Dynamics.Workflows.Fees;

/// <summary>保留舊收費活動入口並建立每日 AllotQuery 待辦。</summary>
public sealed class FeeAllotActivity : ActivityShellBase
{
    /// <summary>保留退役活動的輸入名稱；不作為教會或 fee 授權來源。</summary>
    [RequiredArgument]
    [Input("InputParameter")]
    public InArgument<string> InputParameter { get; set; } = new();

    /// <summary>保留舊整數輸出；入列不代表付款查詢完成，因此不賦值。</summary>
    [Output("OutputResult")]
    public OutArgument<int> OutputResult { get; set; } = new();

    /// <summary>只接受核定來源事件並委派 Core，輸入字串不參與資料存取。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_fee"
            || input.MessageName is not ("ExecuteWorkflow" or "Create" or "Update"))
            return;

        _ = FeeAllotTaskService.Enqueue(services.Organization, input.PrimaryEntityId, new ChurchClock());
    }
}
