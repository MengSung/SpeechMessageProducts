/*
 * 檔案整體說明
 * 目的與定位：P5-11 繳費通知的 Shadow 組合服務，專供核定 P5-PROP-006 尚未提供
 * Host 推播的期間使用。只記實際訊息摘要，不能當成已送達或提供 Live fallback。
 * 主要流程：以正式 fee→contact→account 決定來源教會，交 MemberNotificationService
 * 重讀收件者並驗綁定／歸屬，再讀來源教會語系、名稱與正式頻道關聯；使用共用入口
 * 組字、server sender 及安全摘要。頻道查詢完全不投影 token 或加密 token。
 * 使用邊界與維護：SDK 只借用當次平台服務；token-free source 為呼叫區域 owner，
 * await 排空後 Dispose 清 SDK 參照。訊息、context、姓名只留至本次通知結束，
 * 無 static 個資、Session、快取、Timer、queue 或脫離呼叫的工作。任何真失敗原樣
 * 交回外殼共用例外邊界；未綁定／跨教會正常拒絕且不記推播。離線與 CAS 測試
 * 證明的只有 Shadow 行為，實機追蹤依 D5-19 延後。
 */
using System;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using ToolUtilityNameSpace.LineRecipients;

namespace SpeechMessageProducts.GoodShepherd.Core.Fees;

/// <summary>不具 token 或網路能力的收費影子通知；所有資料只活於單一同步準備與排空。</summary>
public static class FeeNotificationService
{
    /// <summary>
    /// 從平台來源讀取一次收費通知，使用正式 feeId 原樣作重試鍵。
    /// 不提供 Live 多載；外殼必須在查詢前拒絕未核定的 Live 路徑。
    /// </summary>
    /// <param name="sdk">平台授權服務，只借用，不 Dispose。</param>
    /// <param name="feeId">來源 new_fee 正式主鍵。</param>
    /// <param name="organizationId">平台組織主鍵，用於完整文字 scope。</param>
    /// <param name="sink">外殼當次擁有且有界的影子 sink。</param>
    /// <param name="ct">呼叫端取消；同步 SDK 前後檢查，不宣稱可中止已進入的呼叫。</param>
    public static async Task RecordAsync(IOrganizationService sdk, Guid feeId, Guid organizationId,
        IShadowPushSink sink, CancellationToken ct)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(sdk);
        ArgumentNullException.ThrowIfNull(sink);
#else
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
        if (sink is null) throw new ArgumentNullException(nameof(sink));
#endif
        if (feeId == Guid.Empty || organizationId == Guid.Empty)
            throw new ArgumentException("A fee and organization identity are required.");
        var fee = Read(sdk, "new_fee", feeId, ct, "new_contact_new_fee", "new_pay_date",
            "new_category", "new_pay_way", "new_others", "new_fee_really_paid");
        var contactId = Lookup(fee, "new_contact_new_fee", "contact");
        var contact = Read(sdk, "contact", contactId, ct, "parentcustomerid", "fullname");
        var churchId = Lookup(contact, "parentcustomerid", "account");
        using var source = new TokenFreeSource(sdk);
        var catalog = new GoodShepherdTextCatalog(new NoOverrides());
        var notifications = new MemberNotificationService(new NoDelivery(),
            new MemberCultureResolver(new LocalizationOptions()), catalog);
        var scope = new TextContext(churchId, "zh-TW", "zh-TW", organizationId.ToString("D"), "GoodShepherd");
        await notifications.NotifyComposedAsync(source, scope, contactId, feeId, "Fees.FeeNotify", context =>
        {
            // 姓名、金額與日期只在共用入口已重驗收件者後才解讀／組成訊息。
            if (!fee.Attributes.TryGetValue("new_pay_date", out var date) || date is not DateTime raw)
                throw new InvalidOperationException("Fee payment date is unavailable.");
            var utc = raw.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(raw, DateTimeKind.Utc) : raw;
            var timeZone = source.TimeZone ?? throw new InvalidOperationException("Fee church settings are unavailable.");
            if (!fee.Attributes.TryGetValue("new_fee_really_paid", out var money) || money is not Microsoft.Xrm.Sdk.Money amount)
                throw new InvalidOperationException("Fee payment amount is unavailable.");
            var text = FeeNotificationRule.Compose(catalog, context, Text(contact, "fullname", "", 100), utc,
                timeZone, Option(fee, "new_category"), Option(fee, "new_pay_way"),
                Text(fee, "new_others", "", 2000), amount.Value);
            return new ISendMessage[] { new TextMessage(string.Empty) { Text = text } };
        }, sink, ct).ConfigureAwait(false);
    }

    /// <summary>讀取最小欄位並核對回應正式身分；不讓其他列或替代鍵混入本次候選。</summary>
    private static Entity Read(IOrganizationService sdk, string table, Guid id, CancellationToken ct, params string[] columns)
    {
        ct.ThrowIfCancellationRequested();
        var row = sdk.Retrieve(table, id, new ColumnSet(columns));
        ct.ThrowIfCancellationRequested();
        if (row is null || row.LogicalName != table || row.Id != id || row.KeyAttributes.Count != 0)
            throw new InvalidOperationException("Fee notification source identity is invalid.");
        return row;
    }

    /// <summary>正式 lookup 須精確指向資料庫表與非空 ID，不能依名稱推導教會。</summary>
    private static Guid Lookup(Entity row, string field, string table) =>
        row.Attributes.TryGetValue(field, out var value) && value is EntityReference reference &&
        reference.Id != Guid.Empty && reference.LogicalName == table && reference.KeyAttributes.Count == 0
            ? reference.Id : throw new InvalidOperationException("Fee notification scope is unavailable.");

    /// <summary>選填文字可缺省；錯誤型別或超量直接拒絕，不回顯來源內容。</summary>
    private static string Text(Entity row, string field, string fallback, int maximum) =>
        !row.Attributes.TryGetValue(field, out var value) || value is null ? fallback :
        value is string text && text.Length <= maximum ? text :
        throw new InvalidOperationException("Fee notification source text is invalid.");

    /// <summary>舊未填選項走其預設分類，非 OptionSet 型別不得悄悄套預設。</summary>
    private static int Option(Entity row, string field) =>
        !row.Attributes.TryGetValue(field, out var value) || value is null ? 0 :
        value is OptionSetValue option ? option.Value :
        throw new InvalidOperationException("Fee notification option is invalid.");

    /// <summary>單次 token-free 收件來源；唯一 owner 在通知排空後清借用參照。</summary>
    private sealed class TokenFreeSource(IOrganizationService sdk) : ILineRecipientSource, IDisposable
    {
        private IOrganizationService? service = sdk;
        /// <summary>只屬本次來源教會的不可變時區；Dispose 清除，不能跨次套用。</summary>
        internal string? TimeZone { get; private set; }

        /// <summary>借用共用最小欄位讀取器並立即清參照；不觸發其 token 查詢。</summary>
        public MemberRecipient ReadMember(Guid id, CancellationToken ct)
        {
            using var query = new LineRecipientQueries(GetService());
            return query.ReadMember(id, ct);
        }

        /// <summary>只讀語系、名稱、時區與頻道身分；加密 token 欄位完全不讀。</summary>
        public ChurchChannelRecord ReadChannel(Guid id, CancellationToken ct)
        {
            var service = GetService();
            var row = Read(service, "account", id, ct, "name", "new_timezone",
                "new_linechannelid", "new_defaultlanguage", "new_enabledlanguages");
            var channelId = Lookup(row, "new_linechannelid", "new_linechannel");
            _ = Read(service, "new_linechannel", channelId, ct);
            var enabled = Text(row, "new_enabledlanguages", "zh-TW", 8255).Split(',');
            if (enabled.Length > 64) throw new InvalidOperationException("Fee church culture count exceeds its budget.");
            for (var index = 0; index < enabled.Length; index++) enabled[index] = enabled[index].Trim();
            var timeZone = Text(row, "new_timezone", "Asia/Taipei", 128);
            TimeZone = string.IsNullOrWhiteSpace(timeZone) ? "Asia/Taipei" : timeZone;
            var defaultCulture = Text(row, "new_defaultlanguage", "zh-TW", 128);
            var locales = new ChurchLocaleSettings(string.IsNullOrWhiteSpace(defaultCulture) ? "zh-TW" : defaultCulture, enabled);
            return new ChurchChannelRecord(id, channelId, channelId, Text(row, "name", "", 160), "", locales);
        }

        /// <summary>清理後一律拒絕新入口，避免把上次來源服務借給後續通知。</summary>
        private IOrganizationService GetService() =>
            service ?? throw new ObjectDisposedException(nameof(TokenFreeSource));

        /// <summary>本次同步準備已排空，清除 SDK 與教會設定，不 Dispose 平台服務。</summary>
        public void Dispose() { service = null; TimeZone = null; }
    }

    /// <summary>防止 Shadow 意外走入 Live；沒有 HTTP client、連線池或背景資源。</summary>
    private sealed class NoDelivery : ILineMemberPushClient
    {
        /// <summary>任何傳輸皆明確失敗；不能以成功結果掩蓋 Shadow 邊界錯誤。</summary>
        public Task<LinePushResult> PushToMemberAsync(MemberRecipient recipient, ChurchChannel channel, Guid church,
            System.Collections.Generic.IReadOnlyList<ISendMessage> messages, Guid retryKey, CancellationToken ct) =>
            throw new InvalidOperationException("Fee Shadow cannot deliver notifications.");
    }

    /// <summary>每次借用完整 scope 的空覆寫快照，不保留任何教會或個人資料。</summary>
    private sealed class NoOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>只供本次格式化使用的空快照，不建立跨教會 cache。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}
