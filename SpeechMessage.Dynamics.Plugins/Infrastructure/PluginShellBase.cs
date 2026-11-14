/*
 * 檔案整體說明
 *
 * 目的與定位：提供所有新 Plugin 共用的 net462 外殼，統一設定解析、防遞迴、
 * Shadow／Live 服務邊界、規則呼叫與例外語意。外殼只保存不可變註冊設定；
 * 每次 Execute 的 context、service、recording、追蹤與結果都由當次操作擁有。
 *
 * 主要功能與流程：Execute 取得 tracing、context 與借用的 Dataverse service，
 * 先套用 DepthPolicy，再建立 PluginInput；Shadow 以 RecordingOrganizationService
 * 與 ShadowPushRecorder 取代寫入／推播，Live 使用真 service；最後呼叫
 * ExecuteRule。業務規則例外轉為帶 CorrelationId 的 InvalidPluginExecutionException；
 * 未預期例外只追蹤型別並原樣重新拋出，不吞錯。
 *
 * 資源與隔離：Dataverse service、ITracingService 與 provider 皆由
 * 平台借用，外殼不 Dispose；Shadow wrapper、sink 與輸入只活在 Execute，finally
 * 清除其候選資料。不得保存 Session、token、credential、可變 static、cache、
 * timer、Task 或背景工作；規則若有非同步工作必須由 PluginServices.Wait 排空。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;

namespace SpeechMessage.Dynamics.Plugins.Infrastructure;

/// <summary>所有新 Plugin 共用的同步沙箱外殼。</summary>
public abstract class PluginShellBase : IPlugin
{
    /// <summary>只保存固定資源，不包含組織、使用者或執行狀態。</summary>
    private readonly SystemTextTable texts = new();
    /// <summary>註冊時完成驗證的不可變設定。</summary>
    private readonly StepConfiguration configuration;
    /// <summary>不可變的舊入口防遞迴政策。</summary>
    private readonly DepthPolicy depthPolicy;

    /// <summary>建立只保存註冊設定的外殼；衍生類別不得把執行狀態寫入欄位。</summary>
    /// <param name="unsecureConfig">非機密模式與選項 JSON。</param>
    /// <param name="secureConfig">Live 需要的安全設定；Shadow 必須空白。</param>
    /// <param name="depth">此舊入口核定的防遞迴政策。</param>
    private protected PluginShellBase(string unsecureConfig, string secureConfig, DepthPolicy depth)
    {
        configuration = new StepConfiguration(unsecureConfig, secureConfig);
        depthPolicy = depth ?? throw new ArgumentNullException(nameof(depth));
    }

    /// <summary>目前的唯讀步驟設定，供衍生外殼建立規則輸入。</summary>
    private protected StepConfiguration Configuration => configuration;

    /// <summary>平台呼叫的唯一入口；所有操作狀態在方法範圍內建立並清理。</summary>
    /// <param name="serviceProvider">本次執行由 Dataverse 提供的服務集合。</param>
    public void Execute(IServiceProvider serviceProvider)
    {
        if (serviceProvider is null) throw new ArgumentNullException(nameof(serviceProvider));
        var tracing = (ITracingService?)serviceProvider.GetService(typeof(ITracingService))
            ?? throw MissingService();
        var context = (IPluginExecutionContext?)serviceProvider.GetService(typeof(IPluginExecutionContext))
            ?? throw MissingService();
        if (depthPolicy.ShouldSkip(context)) return;

        var factory = (IOrganizationServiceFactory?)serviceProvider.GetService(typeof(IOrganizationServiceFactory))
            ?? throw MissingService();
        var realService = factory.CreateOrganizationService(context.UserId)
            ?? throw MissingService();
        RecordingOrganizationService? recording = null;
        ShadowPushRecorder? shadowPush = null;
        var failed = false;
        try
        {
            var input = new PluginInput(context);
            IOrganizationService organization;
            if (configuration.Mode == PluginExecutionMode.Shadow)
            {
                recording = new RecordingOrganizationService(realService);
                shadowPush = new ShadowPushRecorder();
                organization = recording;
            }
            else
            {
                organization = realService;
            }

            var services = new PluginServices(organization, shadowPush, configuration.Mode == PluginExecutionMode.Shadow);
            ExecuteRule(input, services);
        }
        catch (BusinessRuleException exception)
        {
            failed = true;
            var eventId = context.CorrelationId.ToString("D", CultureInfo.InvariantCulture);
            TraceFailure(tracing, exception);
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
                // 部分完成後失敗的意圖也須留下完整性標記；追蹤失敗不得蓋過規則原例外。
                if (recording is not null && shadowPush is not null)
                {
                    try
                    {
                        new ShadowRecordWriter(tracing).Write(recording, shadowPush);
                    }
                    catch (Exception) when (failed)
                    {
                        // 平台追蹤不可用時保留原始失敗。正常流程的追蹤失敗仍向平台回報。
                    }
                }
            }
            finally
            {
                recording?.Dispose();
                shadowPush?.Dispose();
            }
        }
    }

    /// <summary>缺少平台服務時只回傳固定資源，不回顯 provider 或組態內容。</summary>
    private InvalidPluginExecutionException MissingService() =>
        new(texts.GetText("zh-TW", new TextKey("Common.Plugin.MissingService")));

    /// <summary>資源鍵或參數不完整時退回固定拒絕文字，不回顯輸入且仍由呼叫端附上事件 ID。</summary>
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

    /// <summary>只追蹤型別；平台追蹤失效不能替換規則原始例外，也不回顯訊息或堆疊。</summary>
    private static void TraceFailure(ITracingService tracing, Exception exception)
    {
        try
        {
            tracing.Trace("ExceptionType={0}", exception.GetType().FullName);
        }
        catch (Exception)
        {
            // 失敗路徑維持原例外，由平台接手；沙箱不能以檔案或 LINE 建立另一個告警 owner。
        }
    }

    /// <summary>衍生外殼實作的規則入口；輸入型別刻意維持 internal 邊界。</summary>
    internal abstract void ExecuteRule(PluginInput input, PluginServices services);

}
