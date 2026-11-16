/*
 * 檔案整體說明
 * 目的與定位：P5-10 的新生日／年資 CodeActivity 外殼，保留退役活動在 ExecuteWorkflow、
 * Create、Update 上以 contact 為主要資料的輸入邊界，並將所有資料工作委派給 Core adapter。
 * 主要流程：ActivityShellBase 先解析 Live／Shadow、借用平台 UserId service 及建立 recorder，
 * 本檔只檢查訊息與主要實體，再同步執行 ContactAnniversaryService；沒有自建 LINE、token、
 * 背景 task 或 timer。Shadow 的讀取與 patch 由共用 recorder 收尾，不宣稱網路送達。
 * 使用邊界與維護：活動實例可被 WF 重用，欄位只保存無狀態 adapter；不保存 context、Entity、
 * service 或會友資料。SDK 由平台擁有，recorder 由基底 finally 排空及 Dispose；例外保留原實例。
 */
using System;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Care;

namespace SpeechMessage.Dynamics.Workflows.Care;

/// <summary>生日與年資欄位更新的活動外殼；正式推播尚未由 reading 證實，故不建立通知路徑。</summary>
public sealed class ContactAnniversaryActivity : ActivityShellBase
{
    /// <summary>只在 contact 上的三個舊可達訊息執行；其他平台訊息不產生副作用。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services)
    {
        if (!string.Equals(input.PrimaryEntityName, "contact", StringComparison.Ordinal)) return;
        if (input.MessageName is not ("ExecuteWorkflow" or "Create" or "Update")) return;
        ContactAnniversaryService.Execute(services.Organization, input.PrimaryEntityId);
    }
}

