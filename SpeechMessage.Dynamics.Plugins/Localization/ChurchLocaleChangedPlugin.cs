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
 * Live 未由本階段啟用；若將來啟用，SDK deadline、取消與交易由平台控制；store
 * 只借用 SDK，using 清除參照且不 Dispose 平台資源。沒有跨教會快取、Session、
 * timer 或背景工作；非同步步驟指平台排程，不在外殼建立未觀察的 Task。
 */
using System;
using System.Collections.Generic;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk.Query;
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
        : base(unsecureConfig, secureConfig, new DepthPolicy(2)) { }
    /// <summary>PreOperation 驗證或 PostOperation 入列；不混用同步與非同步契約。</summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.MessageName != "Update" || input.PrimaryEntityName != "account") return;
        if (input.PrimaryEntityId == Guid.Empty ||
            !((input.Stage == 20 && input.Mode == 0) || (input.Stage == 40 && input.Mode == 1)))
            throw Invalid();
        var target = input.Target;
        if (target is null || target.LogicalName != "account" || target.Id != input.PrimaryEntityId)
            throw Invalid();
        var before = input.PreImage;
        if (before is null || before.LogicalName != "account" || before.Id != input.PrimaryEntityId)
            throw Invalid();
        var settings = Read(target, before);
        var validated = LocaleChangeRules.Validate(settings.Default, settings.Enabled, settings.Zone, settings.Country);
        if (!string.Equals(settings.Country, validated.CountryCode, StringComparison.Ordinal)) throw Invalid();
        if (input.Stage == 20) return;
        // 代碼來自正式 account；在建立待辦前套用唯一 alias 規則，不清洗業務識別。
        var account = services.Organization.Retrieve("account", input.PrimaryEntityId, new ColumnSet("accountnumber"));
        if (account is null || account.Id != input.PrimaryEntityId || account.LogicalName != "account" ||
            account.GetAttributeValue<string>("accountnumber") is not { Length: > 0 } churchCode)
            throw Invalid();
        try
        {
            foreach (var culture in validated.EnabledLanguages.Split(','))
                _ = RichMenuAlias.Build(churchCode, "main", "home", culture);
        }
        catch (ArgumentException) { throw Invalid(); }
        using var store = new D365OutboundEnqueueStore(services.Organization);
        var sourceId = LocaleChangeRules.SourceId(input.PrimaryEntityId, validated);
        var payload = new Dictionary<string, object> { ["settings"] = sourceId };
        new OutboundTaskWriter(store).Enqueue(OutboundTaskType.RichMenuGenerate,
            input.PrimaryEntityId, null, sourceId, payload, default);
    }
    /// <summary>以本次 Target 為權威補丁，僅未送出的欄位可借用前像，不發布部分設定。</summary>
    private static RawSettings Read(Entity target, Entity before)
    {
        return new RawSettings(
            Text(target, before, "new_defaultlanguage"),
            Text(target, before, "new_enabledlanguages"),
            Text(target, before, "new_timezone"),
            Text(target, before, "new_countrycode"));
    }
    /// <summary>明確清空或錯誤型別必須拒絕；否則平台可能提交未經驗證的值。</summary>
    private static string Text(Entity target, Entity before, string field)
    {
        if (target.Attributes.TryGetValue(field, out var value))
            return value is string current ? current : throw Invalid();
        if (before.Attributes.TryGetValue(field, out value) && value is string previous) return previous;
        throw Invalid();
    }
    /// <summary>固定設定錯誤交共用外殼格式化及附事件 ID，不回顯輸入。</summary>
    private static BusinessRuleException Invalid() =>
        new(new TextKey("Common.Plugin.InvalidConfiguration"));
    /// <summary>本次同步讀取的四欄純值候選，不保存 SDK、資源或跨教會資料。</summary>
    private sealed record RawSettings(string Default, string Enabled, string Zone, string Country);
}
