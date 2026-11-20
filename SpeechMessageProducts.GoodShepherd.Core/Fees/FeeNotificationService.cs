/*
 * 檔案整體說明
 * 目的與定位：繳費通知的Shadow及Host代送共用組合，兩模式使用相同收件者與組字。
 * Shadow只記摘要、絕不讀token；Live由Host借入key，重驗來源教會後才推播。
 * 主要流程：以正式 fee→contact→account 決定來源教會，交 MemberNotificationService
 * 重讀收件者並驗綁定／歸屬，再讀來源教會語系、名稱與正式頻道關聯；使用共用入口
 * 組字、server sender 及安全摘要。Shadow頻道查詢完全不投影token；Live沿共用LineRecipientQueries讀加密封包。
 * 使用邊界與維護：SDK 只借用當次平台服務；收件source為呼叫區域owner，只有Live讀加密封包，
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

/// <summary>收費通知的Shadow與Host共用規則；所有資料只活於單一同步準備與排空。</summary>
public static class FeeNotificationService
{
    /// <summary>
    /// 從平台來源讀取一次收費通知，使用正式 feeId 原樣作重試鍵。
    /// Shadow不解密或推播；活動Live只入列，實際推播由NotifyRouteAsync負責。
    /// </summary>
    /// <param name="sdk">平台授權服務，只借用，不 Dispose。</param>
    /// <param name="feeId">來源 new_fee 正式主鍵。</param>
    /// <param name="organizationId">平台組織主鍵，用於完整文字 scope。</param>
    /// <param name="sink">外殼當次擁有且有界的影子 sink。</param>
    /// <param name="ct">呼叫端取消；同步 SDK 前後檢查，不宣稱可中止已進入的呼叫。</param>
    public static Task RecordAsync(IOrganizationService sdk, Guid feeId, Guid organizationId,
        IShadowPushSink sink, CancellationToken ct)
    {
#if NETFRAMEWORK
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
        if (sink is null) throw new ArgumentNullException(nameof(sink));
#else
        ArgumentNullException.ThrowIfNull(sdk);
        ArgumentNullException.ThrowIfNull(sink);
#endif
        return NotifyCoreAsync(sdk, new NoDelivery(), feeId, organizationId, sink, null, null, null, "GoodShepherd", ct);
    }

    /// <summary>已解析route的Live入口；route固定0，來源教會重驗後才讀頻道並以feeId推播。回傳原傳輸結果，key由Host擁有及清零。</summary>
    public static Task<LinePushResult?> NotifyRouteAsync(IOrganizationService sdk, ILineMemberPushClient push, Guid feeId,
        Guid organizationId, int route, Guid expectedChurch, byte[] key, CancellationToken ct)
        => NotifyRouteAsync(sdk, push, feeId, organizationId, route, expectedChurch, key, null, "GoodShepherd", ct);

    /// <summary>
    /// 由 Host 傳入已註冊的產品 catalog 與固定文字產品鍵；來源教會仍由正式 fee→contact→account 關聯重驗。
    /// 文字產品值不是授權依據，只用來選擇本 Host 的覆寫快照分區；預設舊入口維持 GoodShepherd。
    /// </summary>
    /// <param name="sdk">Host 借用且在單次 relay 完成前持有的 SDK。</param>
    /// <param name="push">本次通知所用的推播 client。</param>
    /// <param name="feeId">已存在的收費單正式主鍵。</param>
    /// <param name="organizationId">由 Host WhoAmI 取得的正式組織主鍵。</param>
    /// <param name="route">封閉且目前固定為零的路由。</param>
    /// <param name="expectedChurch">relay 工作列的教會範圍，須與來源重讀結果相符。</param>
    /// <param name="key">Host 借用的解密 key，不由本服務清零。</param>
    /// <param name="catalog">本 Host 的產品文字 catalog；null 時保留舊空覆寫行為。</param>
    /// <param name="textProduct">Host 固定的文字快照產品分區。</param>
    /// <param name="ct">單筆操作取消權杖。</param>
    /// <returns>既有推播分類結果；正常拒絕仍回 null 或原分類。</returns>
    public static Task<LinePushResult?> NotifyRouteAsync(IOrganizationService sdk, ILineMemberPushClient push, Guid feeId,
        Guid organizationId, int route, Guid expectedChurch, byte[] key, ITextCatalog? catalog, string textProduct,
        CancellationToken ct)
    {
#if NETFRAMEWORK
        if (route != 0) throw new ArgumentOutOfRangeException(nameof(route));
#else
        ArgumentOutOfRangeException.ThrowIfNotEqual(route, 0);
#endif
        if (key is null || key.Length != 64) throw new ArgumentException("A token key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(textProduct)) throw new ArgumentException("A fixed text product is required.", nameof(textProduct));
        return NotifyCoreAsync(sdk, push, feeId, organizationId, null, key, expectedChurch, catalog, textProduct, ct);
    }

    /// <summary>兩模式共用收件者、組字及重試鍵；當次source在傳輸排空後釋放，來源不符不讀token。</summary>
    private static async Task<LinePushResult?> NotifyCoreAsync(IOrganizationService sdk, ILineMemberPushClient push, Guid feeId,
        Guid organizationId, IShadowPushSink? sink, byte[]? key, Guid? expectedChurch, ITextCatalog? suppliedCatalog,
        string textProduct, CancellationToken ct)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(sdk);
#else
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
#endif
        if (feeId == Guid.Empty || organizationId == Guid.Empty)
            throw new ArgumentException("A fee and organization identity are required.");
        var fee = Read(sdk, "new_fee", feeId, ct, "new_contact_new_fee", "new_pay_date",
            "new_category", "new_pay_way", "new_others", "new_fee_really_paid");
        var contactId = Lookup(fee, "new_contact_new_fee", "contact");
        var contact = Read(sdk, "contact", contactId, ct, "parentcustomerid", "fullname");
        var churchId = Lookup(contact, "parentcustomerid", "account");
        if (expectedChurch.HasValue && churchId != expectedChurch.Value) return null;
        using var source = new NotificationSource(sdk, key is not null);
        var catalog = suppliedCatalog ?? new GoodShepherdTextCatalog(new NoOverrides());
        var notifications = new MemberNotificationService(push,
            new MemberCultureResolver(new LocalizationOptions()), catalog);
        var scope = new TextContext(churchId, "zh-TW", "zh-TW", organizationId.ToString("D"), textProduct);
        System.Collections.Generic.IReadOnlyList<ISendMessage> Compose(TextContext context)
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
        }
        return sink is not null
            ? await notifications.NotifyComposedAsync(source, scope, contactId, feeId, "Fees.FeeNotify", Compose, sink, ct).ConfigureAwait(false)
            : await notifications.NotifyComposedAsync(source, scope, contactId, feeId, "Fees.FeeNotify", Compose, key!, ct).ConfigureAwait(false);
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

    /// <summary>單次收件來源；Shadow不讀token，Live沿共用adapter讀加密封包，唯一owner在await排空後清參照。</summary>
    private sealed class NotificationSource(IOrganizationService sdk, bool live) : ILineRecipientSource, IDisposable
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

        /// <summary>兩模式共用語系與時區；Shadow不讀加密token，Live才借用共用adapter。</summary>
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
            if (live)
            {
                using var query = new LineRecipientQueries(service);
                return query.ReadChannel(id, ct);
            }
            return new ChurchChannelRecord(id, channelId, channelId, Text(row, "name", "", 160), "", locales);
        }

        /// <summary>清理後一律拒絕新入口，避免把上次來源服務借給後續通知。</summary>
        private IOrganizationService GetService() =>
            service ?? throw new ObjectDisposedException(nameof(NotificationSource));

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
