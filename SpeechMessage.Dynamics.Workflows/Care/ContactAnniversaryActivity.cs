/*
 * 檔案整體說明
 * 目的與定位：P5-10 生日／年資 CodeActivity 外殼，承接退役 HappyBirthdayWorkflow
 * 及 RecursiveWorkflow 的 contact 欄位更新，兩入口以選填 AnniversaryKind 明確分流。
 * 主要流程：ActivityShellBase 解析 Live／Shadow、借用平台 UserId service 並建立 recorder；
 * 本檔於 ExecuteWithArguments 當次求值種類，省略為 Birthday，Onboarding 承接年資；
 * 非白名單值先拒絕且不回顯輸入，任何 SDK 讀取前即完成驗證。只在 contact 的
 * ExecuteWorkflow、Create、Update 委派真正 ContactAnniversaryService，並傳入明確種類，
 * 不允許 Core 的 All 預設造成另一入口欄位被更新。直接 ExecuteRule 測試入口固定生日。
 * 使用邊界與維護：WF 可以重用活動實例，屬性只保存引數運算式，不保存已求值種類、
 * context、Entity、SDK 或會友資料；不建立 token、LINE、Session、task 或 timer。
 * SDK 是平台借用資源，不由本檔 Dispose；recorder 由基底 finally 輸出有界完整投影後
 * 排空及 Dispose。Core 失敗保留原例外，未知輸入固定拒絕；離線真 WF／受限子域測試
 * 保護種類隔離、零真寫入與 owner 清理，不宣稱 Dataverse 實機結果。
 */
using System;
using System.Activities;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Care;

namespace SpeechMessage.Dynamics.Workflows.Care;

/// <summary>生日與年資欄位活動的無狀態外殼；只委派已驗證種類，不新增外部通知路徑。</summary>
public sealed class ContactAnniversaryActivity : ActivityShellBase
{
    /// <summary>
    /// 當次 WF 種類運算式；省略為 Birthday，明確 Onboarding 對應退役 RecursiveWorkflow。
    /// 不接受 All、空字串、大小寫變體或多餘空白，避免設定錯誤擴大欄位範圍。
    /// 此引數不代表使用者、教會或來源授權，也不保存前一次求值結果。
    /// </summary>
    [Input("AnniversaryKind")]
    public InArgument<string> AnniversaryKind { get; set; } = new();

    /// <summary>當次求值後只傳遞不可變列舉值，非法種類在任何 SDK 讀取前 fail-closed。</summary>
    /// <param name="context">本次借用 WF context，求值後不保留參照。</param>
    /// <param name="input">平台驗證的主要實體與訊息；活動參數不能覆寫。</param>
    /// <param name="services">當次借用 SDK 與 Shadow recorder，由基底管理清理。</param>
    internal override void ExecuteWithArguments(CodeActivityContext context, ActivityInput input, PluginServices services)
    {
        var kind = AnniversaryKind?.Get(context) switch
        {
            null or "Birthday" => ContactAnniversaryKind.Birthday,
            "Onboarding" => ContactAnniversaryKind.Onboarding,
            _ => throw new InvalidPluginExecutionException("Care anniversary kind is invalid.")
        };
        ExecuteCore(input, services, kind);
    }

    /// <summary>保留直接規則測試入口，固定承接生日；不藉由活動欄位模擬 WF 求值。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services) =>
        ExecuteCore(input, services, ContactAnniversaryKind.Birthday);

    /// <summary>平台 contact 與三個舊可達訊息才執行，明確限制 Core 寫入種類且不保存借用服務。</summary>
    private static void ExecuteCore(ActivityInput input, PluginServices services, ContactAnniversaryKind kind)
    {
        if (!string.Equals(input.PrimaryEntityName, "contact", StringComparison.Ordinal)) return;
        if (input.MessageName is not ("ExecuteWorkflow" or "Create" or "Update")) return;
        ContactAnniversaryService.Execute(services.Organization, input.PrimaryEntityId, kind: kind);
    }
}
