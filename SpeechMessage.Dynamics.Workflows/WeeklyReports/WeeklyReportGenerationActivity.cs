/*
 * 檔案整體說明
 *
 * 目的與定位：P5-05 週報生成的薄自訂活動，將平台 ExecuteWorkflow 委派給
 * GoodShepherd.Core 的 WeeklyReportGenerationService。舊活動沒有任何業務
 * Input／Output，新版只繼承 ActivityShellBase.ExecutionMode，不接受教會、
 * 名單、日期、身分或 credential 引數作為授權來源。
 *
 * 主要功能與流程：共用外殼在每輪取得平台 UserId 的借用 SDK，建立本次
 * ActivityInput 與 Live／Shadow 服務；本活動僅在訊息為 ExecuteWorkflow 時
 * 同步呼叫 Core adapter。舊版 Depth 兩分支都執行，本活動不另設深度閾值。
 * 正式無參數建構使用 ChurchClock；internal 建構僅借入無使用者狀態時鐘，
 * 讓真 WorkflowInvoker 測試固定教會週界，不新增正式活動引數。
 *
 * 使用邊界與維護：活動可由平台重用，欄位只持無 request 狀態的 Core adapter，
 * 不保存 ActivityInput、SDK、Entity、會員、owner 或交易候選。SDK 及 tracing
 * 由平台擁有，活動不得 Dispose；Shadow recorder 由共用外殼 finally 排空及
 * Dispose。本檔沒有背景工作、鎖、timer、cache 或取消註冊，所有交易同步完成。
 * Core／SDK 真正失敗由外殼保留原例外及安全追蹤；不吞錯或自行建立 LINE client。
 * 真活動離線測試保護冪等、交易回滾、同名不同 ID、時區及跨名單／教會隔離，
 * 不代表沙箱、實機權限或部署啟用已驗收。
 */
using System;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;

namespace SpeechMessage.Dynamics.Workflows.WeeklyReports;

/// <summary>只處理 ExecuteWorkflow 的週報生成活動；沒有業務引數及自訂深度跳過。</summary>
public sealed class WeeklyReportGenerationActivity : ActivityShellBase
{
    /// <summary>只持純時鐘依賴的 adapter，所有平台資料均在 ExecuteRule 期間借用。</summary>
    private readonly WeeklyReportGenerationService generation;

    /// <summary>WF 平台建立入口，使用正式 ChurchClock，不建立 SDK 或其他資源 owner。</summary>
    public WeeklyReportGenerationActivity() : this(new ChurchClock()) { }

    /// <summary>離線測試借入無使用者狀態時鐘；不對平台公開時間／教會業務參數。</summary>
    /// <param name="clock">由測試／主機擁有的純時鐘，最長保留至活動釋放。</param>
    internal WeeklyReportGenerationActivity(IChurchClock clock) => generation = new WeeklyReportGenerationService(clock);

    /// <summary>只看本次訊息並同步委派；平台 UserId 已由共用外殼選定，不沿用起始使用者權限。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services)
    {
        if (!string.Equals(input.MessageName, "ExecuteWorkflow", StringComparison.Ordinal)) return;
        generation.Generate(services.Organization, input.OrganizationId, services.IsShadow);
    }
}
