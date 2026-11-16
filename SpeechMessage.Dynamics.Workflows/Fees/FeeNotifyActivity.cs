/*
 * 檔案整體說明
 *
 * 目的與定位：P5-11 繳費通知的新版 CodeActivity 外殼。P5-PROP-006 已核定
 * Live 在查詢任何收件者或 token 前 fail-closed；Shadow 才讀取 fee 的正式會友
 * lookup 並記錄有界通知意圖，避免把未完成的 Host 推播架構誤當成已實作。
 *
 * 主要功能與流程：活動只接受 new_fee 的 ExecuteWorkflow／Create／Update 來源；
 * Live 立即以固定錯誤拒絕，不建立 client、不讀 token、不查收件者。Shadow 沿
 * fee→contact 正式 lookup 取得目標，使用 feeId 作 Retry Key 並寫入安全固定雜湊，
 * 由 ActivityShellBase 在 finally 輸出並 Dispose recorder。
 *
 * 使用邊界與維護：活動實例不保存 context、SDK、Entity、Session、credential、
 * timer 或背景工作；Shadow 的摘要不含訊息原文、LINE ID 或 token。這是影子意圖
 * 記錄，不代表 LINE 實機送達或 Host 已完成。
 */
using System;
using System.Activities;
using System.Security.Cryptography;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Fees;

namespace SpeechMessage.Dynamics.Workflows.Fees;

/// <summary>繳費通知外殼；Live 依核定缺口拒絕，Shadow 記錄安全意圖。</summary>
public sealed class FeeNotifyActivity : ActivityShellBase
{
    /// <summary>保留舊活動輸入名稱；不作為資料授權或訊息內容。</summary>
    [Input("InputParameter")]
    public InArgument<string> InputParameter { get; set; } = new();

    /// <summary>保留舊活動輸出名稱；不把影子記錄解讀成送達結果。</summary>
    [Output("OutputResult")]
    public OutArgument<int> OutputResult { get; set; } = new();

    /// <summary>Live 先拒絕；Shadow 才讀正式 contact lookup 並記錄一次通知意圖。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_fee"
            || input.MessageName is not ("ExecuteWorkflow" or "Create" or "Update"))
            return;
        if (!services.IsShadow)
            throw new InvalidPluginExecutionException(
                "P5-PROP-006: Fee notification Live delivery is not implemented.");

        var contactId = FeeAllotTaskService.ResolveContactId(services.Organization, input.PrimaryEntityId);
        using var hash = SHA256.Create();
        var digest = BitConverter.ToString(hash.ComputeHash(
            System.Text.Encoding.UTF8.GetBytes("Fees.FeeNotify"))).Replace("-", string.Empty).ToLowerInvariant();
        services.ShadowPush!.Record(contactId, "Fees.FeeNotify", input.PrimaryEntityId, 1, digest);
    }
}
