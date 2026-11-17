/*
 * 檔案整體說明
 * 目的與定位：P5-12 外送 letter 的非同步 PostOperation 薄外殼，承接退役
 * LineMessagePlugIn 的外送方向，不承接其入站關鍵字、群組廣播或 LINE Notify。
 * 主要流程：僅接letter Create／Update；沿用深度二且Create例外。Live只入列代送，
 * 從寄件者推導來源教會，入站／終止略過，不查key或推播；Shadow委派Core讀活動，
 * 記單人推播及狀態候選，無真寫入、解密或網路，不冒稱已實際送達。
 * 資源與隔離：外殼只保存不可變設定，SDK、輸入、source 與 recorder 都屬單次
 * Execute；共用 PluginShellBase 擁有排空與 Dispose。本檔不保留 Session、使用者、
 * 憑證、快取、Timer 或背景工作；同步 Wait 結束後才讓平台回收借用服務。
 * 未預期例外原樣重拋，平台追蹤只記安全型別；實機追蹤依 D5-19 延後。
 */
using System.Threading;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using System;

namespace SpeechMessage.Dynamics.Plugins.Messaging;

/// <summary>Live只入列的外送外殼；Host負責重讀與推播，Shadow維持原意圖。</summary>
public sealed class OutboundLetterPlugin : PluginShellBase
{
    /// <summary>沿用退役入口的 Depth&gt;2 且非 Create 才停止，不自行放寬平台模式。</summary>
    public OutboundLetterPlugin(string unsecureConfig, string secureConfig)
        : base(unsecureConfig, secureConfig, new DepthPolicy(2, DepthException.Create)) { }

    /// <summary>驗證觸發範圍後，Live只讀來源及入列；Shadow 同步等待一次規則完全排空。</summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "letter" || input.MessageName is not ("Create" or "Update")) return;
        if (input.Stage != 40 || input.Mode != 1)
            throw new InvalidPluginExecutionException("Outbound letter requires asynchronous PostOperation.");
        if (!services.IsShadow)
        {
            MemberNotifyRelay.Enqueue(services.Organization, MemberNotifyRelayKind.OutboundLetter, input.PrimaryEntityId,
                input.MessageName == "Create" ? 2 : 3, 0, Guid.Empty);
            return;
        }
        PluginServices.Wait(OutboundLetterRecords.RecordAsync(services.Organization, input.PrimaryEntityId,
            input.OrganizationId, services.ShadowPush!, CancellationToken.None));
    }
}
