/*
 * 檔案整體說明
 * 目的與定位：P5-12 外送 letter 的非同步 PostOperation 薄外殼，承接退役
 * LineMessagePlugIn 的外送方向，不承接其入站關鍵字、群組廣播或 LINE Notify。
 * 主要流程：僅接 letter Create／Update；沿用深度二且 Create 例外。Live 依已核定
 * P5-PROP-006 在任何資料讀取前拒絕；Shadow 委派 Core 讀正式活動及來源教會，
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

namespace SpeechMessage.Dynamics.Plugins.Messaging;

/// <summary>不具有 Live 推播入口的外送影子外殼；正式送訊架構仍受 P5-PROP-006 管制。</summary>
public sealed class OutboundLetterPlugin : PluginShellBase
{
    /// <summary>沿用退役入口的 Depth&gt;2 且非 Create 才停止，不自行放寬平台模式。</summary>
    public OutboundLetterPlugin(string unsecureConfig, string secureConfig)
        : base(unsecureConfig, secureConfig, new DepthPolicy(2, DepthException.Create)) { }

    /// <summary>驗證觸發範圍後，Live 零查詢拒絕；Shadow 同步等待一次規則完全排空。</summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "letter" || input.MessageName is not ("Create" or "Update")) return;
        if (input.Stage != 40 || input.Mode != 1)
            throw new InvalidPluginExecutionException("Outbound letter requires asynchronous PostOperation.");
        if (!services.IsShadow)
            throw new InvalidPluginExecutionException("P5-PROP-006: Outbound letter Live delivery is not implemented.");
        PluginServices.Wait(OutboundLetterRecords.RecordAsync(services.Organization, input.PrimaryEntityId,
            input.OrganizationId, services.ShadowPush!, CancellationToken.None));
    }
}
