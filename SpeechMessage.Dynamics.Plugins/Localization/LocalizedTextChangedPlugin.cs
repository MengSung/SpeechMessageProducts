/*
 * 檔案整體說明
 *
 * 目的與定位：P5-04 教會文字覆寫變更外殼；收到 new_localizedtext 的建立、
 * 更新或刪除後，使 account.new_textchangedon 失效標記前進。沒有舊入口；
 * 深度超過二直接返回，實際註冊限定 PostOperation 非同步。
 * 主要功能與流程：Create 由平台 Target 取得教會，Update 合併 Target 與必要前像，
 * Delete 只讀必要前像。兩個 lookup 都驗證完成後才寫入；換教會同時更新前後兩間。
 * 每次只讀一次 UTC 時鐘，向下取整到秒；不保存覆寫原文或將顯示名稱當識別。
 * 使用邊界與維護：不可變時鐘委派由外殼借用，不捕獲 context；SDK、輸入與寫入均屬
 * 單次 Execute，沒有 static 可變狀態、快取、連線、計時器或背景工作。Shadow
 * 寫入由共用 recorder 截住並在 finally 排空，不接觸真資料；Live 的交易與同步
 * SDK 逾時由平台擁有，未知失敗原樣交共用外殼，不自行重試或推送告警。
 * 驗證：三訊息、換教會、缺前像、UTC 整秒、重用隔離及故障注入由同名外殼測試保護；
 * 離線通過不代表已取得 D365 實機追蹤。
 */
using System;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
namespace SpeechMessage.Dynamics.Plugins.Localization;
/// <summary>將單一文字覆寫事件轉成正式教會的快取版本時間更新。</summary>
public sealed class LocalizedTextChangedPlugin : PluginShellBase
{
    /// <summary>只借用不含組織資料的 UTC 時鐘，執行結果不存入實例。</summary>
    private readonly Func<DateTimeOffset> utcNow;
    /// <summary>平台建構入口；只保存已驗證模式及無狀態 UTC 來源。</summary>
    /// <param name="unsecureConfig">Shadow／Live 設定。</param>
    /// <param name="secureConfig">本模組不使用秘密；Shadow 必須空白。</param>
    public LocalizedTextChangedPlugin(string unsecureConfig, string secureConfig)
        : this(unsecureConfig, secureConfig, () => DateTimeOffset.UtcNow) { }
    /// <summary>測試注入固定 UTC 時鐘，不改動程序時鐘或其他教會設定。</summary>
    internal LocalizedTextChangedPlugin(string unsecureConfig, string secureConfig, Func<DateTimeOffset> utcNow)
        : base(unsecureConfig, secureConfig, new DepthPolicy(2))
    {
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }
    /// <summary>完整驗證本事件的目標及映像，再以單一整秒 UTC 值依序觸及教會。</summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_localizedtext") return;
        if (input.MessageName is not ("Create" or "Update" or "Delete")) return;
        if (input.Stage != 40 || input.Mode != 1 || input.PrimaryEntityId == Guid.Empty) throw Rejected();
        var target = input.Target;
        if (target is null || target.LogicalName != input.PrimaryEntityName || target.Id != input.PrimaryEntityId)
            throw Rejected();
        Guid? previous = null;
        if (input.MessageName != "Create")
        {
            var before = input.PreImage;
            if (before is null || before.LogicalName != target.LogicalName || before.Id != target.Id)
                throw Rejected();
            previous = Church(before);
        }
        var current = input.MessageName == "Delete" ? previous!.Value
            : target.Contains("new_accountid") ? Church(target)
            : previous ?? throw Rejected();
        // 先驗證全部來源，才開始任何寫入；搬移採固定前後順序且同教會只寫一次。
        var instant = utcNow().UtcDateTime;
        var timestamp = new DateTime(instant.Ticks - instant.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        if (previous.HasValue && previous.Value != current) Touch(services, previous.Value, timestamp);
        Touch(services, current, timestamp);
    }
    /// <summary>只接受非空 account lookup；不能用名稱、其他表或空值推測教會。</summary>
    private static Guid Church(Entity row)
    {
        if (!row.Attributes.TryGetValue("new_accountid", out var raw) ||
            raw is not EntityReference { LogicalName: "account" } church || church.Id == Guid.Empty ||
            church.KeyAttributes.Count != 0) throw Rejected();
        return church.Id;
    }
    /// <summary>只更新一欄，服務由當次外殼借用；不保留 Entity 或吞掉平台失敗。</summary>
    private static void Touch(PluginServices services, Guid church, DateTime timestamp) =>
        services.Organization.Update(new Entity("account", church) { ["new_textchangedon"] = timestamp });
    /// <summary>固定業務錯誤鍵不包含原始文字、lookup 或平台資料。</summary>
    private static BusinessRuleException Rejected() => new(new TextKey("Common.Plugin.RuleRejected"));
}
