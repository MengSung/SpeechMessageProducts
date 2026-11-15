/*
 * 檔案整體說明
 *
 * 目的與定位：提供 P5 自訂活動共用的 CodeActivity 外殼，集中模式解析、借用
 * 平台服務、可選的舊入口防遞迴政策、規則例外及影子意圖收尾。WF 可快取活動實例，
 * 因此欄位只包含固定資源、不可變政策與 InArgument 宣告，不能保存執行值。
 *
 * 主要功能與流程：每次 Execute 評估 ExecutionMode，未提供或空白使用 Live，
 * 明確 Shadow 才建立兩個 recorder，其他值固定拒絕而不回顯原輸入。由平台
 * extension 取得 context／tracing／factory，以本次 UserId 借用 service；
 * 建立 ActivityInput 與 PluginServices 後呼叫 internal ExecuteWithArguments；需要引數的模組
 * 在當次 context 求值並立即轉成純值，預設仍委派原規則入口。
 * 業務拒絕比照 Plugin 使用固定資源及事件 ID；未知例外保留原實例重新拋出。
 *
 * 資源與隔離：context、規則輸入、service 與 recorder 都只屬本次同步呼叫；
 * 不把它們寫入活動欄位、static、快取、Session 或背景工作。SDK 與 tracing
 * 由平台擁有，外殼不得 Dispose。兩個 Shadow recorder 由本次外殼唯一擁有，
 * finally 先輸出有界安全投影，再清除並 Dispose；追蹤失敗不得覆蓋規則原錯誤。
 * 成功規則的輸出失敗則向平台回報，不能假稱已完成影子記錄。
 * 本檔不開啟網路、計時器、取消註冊或通知背景工作；規則工作須同步排空。
 * 離線 probe 保護模式、例外與 A／B／A 隔離，實機追蹤可用性另由部署驗收。
 */
using System;
using System.Activities;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;

namespace SpeechMessage.Dynamics.Workflows.Infrastructure;

/// <summary>
/// 可由平台重用的活動共用外殼；每次執行僅建立區域服務及輸入。
/// 衍生活動只宣告 WF 參數與規則，不能保存已評估參數或平台執行 context。
/// </summary>
public abstract class ActivityShellBase : CodeActivity
{
    /// <summary>固定且無使用者狀態的文字資源，與 Plugin 的業務拒絕格式一致。</summary>
    private readonly SystemTextTable texts = new();
    /// <summary>依實際舊入口指定的不可變政策；未指定時不虛構共用深度門檻。</summary>
    private readonly DepthPolicy? depthPolicy;

    /// <summary>
    /// 建立只有固定政策的活動。各模組依盤點選擇政策；null 表示本外殼不加深度限制，
    /// 不得為效能或測試便利改變舊入口的遞迴條件。
    /// </summary>
    /// <param name="depth">可省略的舊入口防遞迴政策，不包含執行狀態。</param>
    private protected ActivityShellBase(DepthPolicy? depth = null) => depthPolicy = depth;

    /// <summary>
    /// WF 輸入宣告，僅在本次 Execute 評估；省略或空白為 Live，Shadow 僅記錄意圖。
    /// 此屬性保存運算式而非前次執行值，不是授權、組織或 credential 的來源。
    /// </summary>
    [Input("ExecutionMode")]
    public InArgument<string> ExecutionMode { get; set; } = new();

    /// <summary>
    /// 平台同步入口；模式、SDK 借用及 recorder 所有權都在本次區域範圍決定，
    /// 不允許衍生活動覆寫以略過隔離、例外或確定性清理。
    /// </summary>
    /// <param name="context">WF 本次執行 context，只能在本次同步規則內借用。</param>
    protected sealed override void Execute(CodeActivityContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        var mode = ExecutionMode?.Get(context);
        var isShadow = string.Equals(mode, "Shadow", StringComparison.Ordinal);
        if (!isShadow && !string.IsNullOrWhiteSpace(mode) && !string.Equals(mode, "Live", StringComparison.Ordinal))
            throw new InvalidPluginExecutionException(texts.GetText("zh-TW", new TextKey("Common.Plugin.InvalidConfiguration")));

        var tracing = context.GetExtension<ITracingService>() ?? throw MissingService();
        var workflow = context.GetExtension<IWorkflowContext>() ?? throw MissingService();
        if (depthPolicy?.ShouldSkip(workflow) == true) return;
        var factory = context.GetExtension<IOrganizationServiceFactory>() ?? throw MissingService();
        var realService = factory.CreateOrganizationService(workflow.UserId) ?? throw MissingService();
        RecordingOrganizationService? recording = null;
        ShadowPushRecorder? shadowPush = null;
        var failed = false;
        try
        {
            var input = new ActivityInput(workflow);
            IOrganizationService organization;
            if (isShadow)
            {
                recording = new RecordingOrganizationService(realService);
                shadowPush = new ShadowPushRecorder();
                organization = recording;
            }
            else
            {
                organization = realService;
            }

            ExecuteWithArguments(context, input, new PluginServices(organization, shadowPush, isShadow));
        }
        catch (BusinessRuleException exception)
        {
            failed = true;
            TraceFailure(tracing, exception);
            var eventId = workflow.CorrelationId.ToString("D", CultureInfo.InvariantCulture);
            throw new InvalidPluginExecutionException(
                FormatBusinessError(exception) + " " +
                texts.GetFormattedText("zh-TW", new TextKey("Common.Plugin.EventId"), new TextArg("eventId", eventId)));
        }
        catch (Exception exception)
        {
            failed = true;
            TraceFailure(tracing, exception);
            throw;
        }
        finally
        {
            try
            {
                if (recording is not null && shadowPush is not null)
                {
                    try
                    {
                        new ShadowRecordWriter(tracing).Write(recording, shadowPush);
                    }
                    catch (Exception) when (failed)
                    {
                        // 規則已失敗時，追蹤不可用不得取代原例外；成功規則的追蹤故障仍向上拋出。
                    }
                }
            }
            finally
            {
                try
                {
                    recording?.Dispose();
                }
                finally
                {
                    shadowPush?.Dispose();
                }
            }
        }
    }

    /// <summary>平台缺少必要 extension 或 service 時，只傳回固定文字，不回顯平台內容。</summary>
    private InvalidPluginExecutionException MissingService() =>
        new(texts.GetText("zh-TW", new TextKey("Common.Plugin.MissingService")));

    /// <summary>
    /// 使用與 Plugin 相同的業務文字契約；未知鍵或缺少具名參數時改用固定拒絕，
    /// 不洩漏鍵值或規則輸入，事件識別由呼叫端統一附加。
    /// </summary>
    private string FormatBusinessError(BusinessRuleException exception)
    {
        try
        {
            return texts.GetFormattedText("zh-TW", exception.TextKey, exception.Arguments);
        }
        catch (KeyNotFoundException)
        {
            return texts.GetText("zh-TW", new TextKey("Common.Plugin.RuleRejected"));
        }
        catch (FormatException)
        {
            return texts.GetText("zh-TW", new TextKey("Common.Plugin.RuleRejected"));
        }
    }

    /// <summary>失敗只追蹤型別；追蹤本身出錯仍保留規則原例外，不新增告警資源 owner。</summary>
    private static void TraceFailure(ITracingService tracing, Exception exception)
    {
        try
        {
            tracing.Trace("ExceptionType={0}", exception.GetType().FullName);
        }
        catch (Exception)
        {
            // 沙箱失敗交回平台；不保存例外、SDK 或使用者內容以建立另一條背景通知路徑。
        }
    }

    /// <summary>
    /// 需要 WF 引數的模組可於本次呼叫覆寫此入口，使用 context 求值後立即轉成純值；
    /// 預設委派既有規則，維持其他活動及 CAS 的原介面與行為。context 只供同步借用，
    /// 不得保存於活動欄位、靜態資料、快取或脫離本次呼叫的工作。
    /// </summary>
    /// <param name="context">平台本次 WF context，求值後不得保留參照。</param>
    /// <param name="input">已擷取的不可變平台識別，活動引數不能覆寫其授權邊界。</param>
    /// <param name="services">本次借用服務，非同步工作仍須同步排空。</param>
    internal virtual void ExecuteWithArguments(CodeActivityContext context, ActivityInput input, PluginServices services) =>
        ExecuteRule(input, services);

    /// <summary>
    /// 模組的同步規則入口，只接受當次不可變識別與借用服務。
    /// 不得把任一輸入或服務保存到活動欄位；非同步工作須以 PluginServices.Wait 排空。
    /// </summary>
    internal abstract void ExecuteRule(ActivityInput input, PluginServices services);
}
