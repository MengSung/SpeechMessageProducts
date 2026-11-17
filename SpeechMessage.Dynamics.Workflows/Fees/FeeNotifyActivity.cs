/*
 * 檔案整體說明
 *
 * 目的與定位：繳費通知CodeActivity外殼；Live只建立無文字的Host代送待辦，
 * 從fee正式會友lookup推導來源教會，不查頻道或key；Shadow記有界通知意圖。
 *
 * 主要功能與流程：活動只接受 new_fee 的 ExecuteWorkflow 來源；舊 Create／Update 無業務效果。
 * Live只入列，不建立client、不讀token；Host重讀來源及組字後推播。Shadow沿
 * fee→contact 正式 lookup 取得目標，使用 feeId 作 Retry Key 並寫入實際訊息安全雜湊，
 * 由 ActivityShellBase 在 finally 輸出並 Dispose recorder。
 *
 * 使用邊界與維護：活動實例不保存 context、SDK、Entity、Session、credential、
 * timer 或背景工作；Shadow 的摘要不含訊息原文、LINE ID 或 token。這是影子意圖
 * 記錄，不代表 LINE 實機送達或 Host 已完成。
 */
using System;
using System.Activities;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Fees;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;

namespace SpeechMessage.Dynamics.Workflows.Fees;

/// <summary>繳費通知外殼；Live只入列代送待辦，Shadow 記錄安全意圖。</summary>
public sealed class FeeNotifyActivity : ActivityShellBase
{
    /// <summary>保留舊活動輸入名稱；不作為資料授權或訊息內容。</summary>
    [RequiredArgument]
    [Input("InputParameter")]
    public InArgument<string> InputParameter { get; set; } = new();

    /// <summary>保留舊活動輸出名稱；不把影子記錄解讀成送達結果。</summary>
    [Output("OutputResult")]
    public OutArgument<int> OutputResult { get; set; } = new();

    /// <summary>Live從來源推導教會後入列；Shadow記一次通知意圖。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_fee"
            || input.MessageName != "ExecuteWorkflow")
            return;
        if (!services.IsShadow)
        {
            MemberNotifyRelay.Enqueue(services.Organization, MemberNotifyRelayKind.FeeNotify, input.PrimaryEntityId, 1, 0,
                input.OperationId != Guid.Empty ? input.OperationId : input.CorrelationId);
            return;
        }

        PluginServices.Wait(FeeNotificationService.RecordAsync(services.Organization,
            RetryKey(input.PrimaryEntityId), input.OrganizationId, services.ShadowPush!, CancellationToken.None));
    }

    /// <summary>通知重試鍵固定使用伺服器提供的收費單正式 ID，不以會友或活動實例取代。</summary>
    internal static Guid RetryKey(Guid feeId) =>
        feeId == Guid.Empty ? throw new ArgumentException("A fee identity is required.", nameof(feeId)) : feeId;

}
