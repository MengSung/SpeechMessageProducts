/*
 * 檔案整體說明
 *
 * 目的與定位：P5-04 教會語系變更外殼；account 更新的同步步驟只驗證四個設定，
 * 非同步步驟在驗證後建立 RichMenuGenerate 待辦。新入口沒有舊程式，基準固定為 new。
 *
 * 主要功能與流程：PreOperation 由 Target 與四欄 PreImage 合成完整設定，使用
 * LocaleChangeRules 驗證開放語系、預設值、跨目標時區及大寫國碼，失敗在任何
 * 寫入前以 BusinessRuleException 拒絕。PostOperation 重算同一 DeterministicGuid，
 * 以 OutboundTaskWriter 交給資料庫替代鍵原子建立或讀回，payload 只含固定來源 ID。
 *
 * 使用邊界與維護：輸入、候選、store 與 writer 僅屬單次 Execute；Shadow 的
 * Organization 是 recording service，不會寫正式資料，待辦建立意圖由共用投影記錄。
 * Live 未由本階段啟用；若將來啟用，SDK deadline、取消、交易與 dispose 由本次
 * store owner 確定性管理。沒有跨教會快取、Session、背景工作或資源洩漏。
 */
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Outbound;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using ToolUtilityNameSpace.Outbound;
namespace SpeechMessage.Dynamics.Plugins.Localization;
/// <summary>驗證教會四欄語系設定並建立冪等宮格產生待辦。</summary>
public sealed class ChurchLocaleChangedPlugin : PluginShellBase
{
    /// <summary>平台建構入口；設定解析及執行狀態均由共用外殼管理。</summary>
    public ChurchLocaleChangedPlugin(string unsecureConfig, string secureConfig)
        : base(unsecureConfig, secureConfig, new DepthPolicy(1)) { }
    /// <summary>PreOperation 驗證或 PostOperation 入列；不混用同步與非同步契約。</summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.MessageName != "Update" || input.PrimaryEntityName != "account") return;
        if (input.PrimaryEntityId == Guid.Empty || input.Stage is not (20 or 40)) throw Invalid();
        var target = input.Target;
        if (target is null || target.LogicalName != "account" || target.Id != input.PrimaryEntityId)
            throw Invalid();
        var before = input.PreImage;
        if (before is null || before.LogicalName != "account" || before.Id != input.PrimaryEntityId)
            throw Invalid();
        var settings = Read(target, before);
        var validated = LocaleChangeRules.Validate(settings.Default, settings.Enabled, settings.Zone, settings.Country);
        if (input.Stage == 20) return;
        using var store = new D365OutboundEnqueueStore(services.Organization);
        var sourceId = LocaleChangeRules.SourceId(input.PrimaryEntityId, validated);
        var payload = new Dictionary<string, object> { ["settings"] = sourceId };
        new OutboundTaskWriter(store).Enqueue(OutboundTaskType.RichMenuGenerate,
            input.PrimaryEntityId, null, sourceId, payload, default);
    }
    private static RawSettings Read(Entity target, Entity before)
    {
        return new RawSettings(
            Text(target, before, "new_defaultlanguage"),
            Text(target, before, "new_enabledlanguages"),
            Text(target, before, "new_timezone"),
            Text(target, before, "new_countrycode"));
    }
    private static string Text(Entity target, Entity before, string field)
    {
        if (target.Attributes.TryGetValue(field, out var value) && value is string current) return current;
        if (before.Attributes.TryGetValue(field, out value) && value is string previous) return previous;
        throw Invalid();
    }
    private static BusinessRuleException Invalid() =>
        new(new TextKey("Common.Plugin.InvalidConfiguration"));
    private sealed record RawSettings(string Default, string Enabled, string Zone, string Country);
}
