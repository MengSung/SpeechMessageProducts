/*
 * 檔案整體說明
 * 目的與定位：P5-07 課程簽核 CodeActivity 薄外殼，保留舊 InputParameter 字串、
 * 指向 new_stor_lessons 的可省略 InputStorLesson 與 OutputResult 整數 metadata。
 * 主要功能與流程：透過 ActivityShellBase 的當次引數入口 求值參數，課程
 * 簽核優先通知 member→list→leader；Create 含新增通知本人、Update 含更新通知組長。
 * 共用Core分派／查詢／組訊息；舊付款fallback依P5-PROP-006核定不實作，
 * Shadow在零查詢／零副作用下只Trace一次固定「未實作：P5-PROP-006」，不算差異零。
 * 使用邊界與維護：參數僅供業務分派，不提供教會授權。主來源固定平台 PrimaryEntityId，
 * 可省略的來源參考若提供必須相符；不持有求值結果、WF context、SDK、Entity 或 key。
 * 平台 SDK 由外殼借用，Shadow recorder 由外殼 finally 排空／Dispose；共用通知 source
 * 在本次同步排空後釋放，沒有 Session、cache、timer、背景 Task 或固定收件者 fallback。
 * Live依核定在查詢前明確拒絕，真實例外交共用活動失敗邊界保留，
 * 供D365共用彙總告警接手；沙箱不自建notifier或另發管理者LINE。
 * 不讀資料列token或冒稱已送達；舊OutputResult
 * 未設定，維持 WF 預設值。未知訊息不執行，已知訊息的非法參數 fail closed。
 * 驗證方式：真 WorkflowInvoker 離線測試覆蓋分派優先序、不同正式組長／學員 ID、
 * metadata、未知／付款分支與 A/B/A 重用；不代表 D365 啟用或 LINE 實機送達。
 */
using System;
using System.Activities;
using System.Threading;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;

namespace SpeechMessage.Dynamics.Workflows.Lessons;

/// <summary>活動實例只保留 WF expression；資料、參數求值與通知均屬本次同步執行。</summary>
public sealed class StorLessonSigningActivity : ActivityShellBase
{
    /// <summary>必要業務分派字串，保留舊 Contains 語意，不作為教會或資料存取授權。</summary>
    [RequiredArgument]
    [Input("InputParameter")]
    public InArgument<string> InputParameter { get; set; } = new();

    /// <summary>保留舊可省略來源參考及目標表；若提供必須符合平台來源，不能改查其他列。</summary>
    [Input("InputStorLesson")]
    [ReferenceTarget("new_stor_lessons")]
    public InArgument<EntityReference> InputStorLesson { get; set; } = new();

    /// <summary>保留舊輸出宣告；舊活動未設定此值，不將預設零解釋成已傳送成功。</summary>
    [Output("OutputResult")]
    public OutArgument<int> OutputResult { get; set; } = new();

    /// <summary>
    /// 在外殼擁有的當次 context 求值並立即消費，不把引數存入活動欄位。來源參考
    /// 僅做相符驗證，真正授權仍由平台使用者的 SDK 與來源 member 教會決定。
    /// </summary>
    internal override void ExecuteWithArguments(CodeActivityContext context, ActivityInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_stor_lessons" ||
            input.MessageName is not ("ExecuteWorkflow" or "Create" or "Update")) return;
        if (!services.IsShadow)
            throw new InvalidPluginExecutionException("P5-PROP-006: Lesson signing activity Live notifications are not implemented.");
        var parameter = InputParameter.Get(context);
        var target = InputStorLesson.Get(context);
        if (target is not null && (target.LogicalName != "new_stor_lessons" ||
            target.Id == Guid.Empty || target.Id != input.PrimaryEntityId || target.KeyAttributes.Count != 0))
            throw new InvalidPluginExecutionException("Lesson signing activity source is invalid.");
        // 核定缺口不產生假的Create或Push；固定標記不含來源、參數、付款或credential內容。
        if (!LessonSigningDispatch.TryNotifyLeader(input.MessageName, parameter, out _))
        {
            context.GetExtension<ITracingService>().Trace(LessonSigningDispatch.ApprovedGapMarker);
            return;
        }
        PluginServices.Wait(LessonSigningDispatch.NotifyAsync(services.Organization, new LinePushClient(), input.PrimaryEntityId,
            input.OrganizationId, input.MessageName, parameter, services.ShadowPush!, null, CancellationToken.None));
    }

    /// <summary>沒有當次 WF context 就無法安全求值參數，直接拒絕而不猜測參數或通知對象。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services) =>
        throw new InvalidPluginExecutionException("Lesson signing activity requires evaluated workflow arguments.");
}
