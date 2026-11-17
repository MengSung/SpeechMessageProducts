/*
 * 檔案整體說明
 * 目的與定位：P5-12 外送 letter 的 Shadow 資料 adapter，從正式活動解出單人收件者、
 * 寄件者所屬教會及自由文字，委派 OutboundLetterRule 記錄完整意圖；亦供Host Live代送；Live使用同一組字規則與狀態patch。
 * 主要流程：先讀 directioncode，入站或已終止活動不繼續；外送缺狀態即拒絕，不把
 * 未知狀態推定 Open。外送只接受一個 contact
 * 收件 party 與一個 account／contact 寄件 party。教會由寄件者 account 或正式 contact
 * 的 parentcustomerid 決定，絕不由收件者反推；systemuser 等未具權威關聯時封閉失敗。
 * 寄件者與頻道重讀後，規則完成意圖才產生假設送達的狀態候選；業務拒絕則產生失敗
 * 候選。經唯讀 metadata 確認主鍵 activityid、state/status 完成1/4、取消2/5，以及
 * category／subcategory 各250字；以固定 SMV 類別識別本模組失敗、subcategory 存字典
 * 原因，不覆蓋原 description。既有非本模組 category／subcategory 先拒絕、不搶用。
 * 資源與隔離：Shadow借用recording SDK且Update只記意圖；Host Live借用當筆已授權SDK。
 * Shadow只查頻道身分；Live沿LineRecipientQueries讀加密token，source在await排空
 * 後 Dispose 清除參照。文字、Entity、教會設定只留本次；無 Session、快取、Timer
 * 或背景工作。缺權威來源、多收件者、媒體型別等未承接範圍明確拒絕，不冒稱相容。
 */
using System;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using ToolUtilityNameSpace.LineRecipients;

namespace SpeechMessageProducts.GoodShepherd.Core.Messaging;

/// <summary>外殼Shadow及Host單筆Live共用資料組合；來源教會必須獨立核對。</summary>
public static class OutboundLetterRecords
{
    /// <summary>
    /// 讀正式活動並產生推播／狀態意圖；必須借入當次 recording SDK，不得傳真寫入服務。
    /// 來源欄位不是 caller 路由；收件者由共用通知入口另外核對，不把影子結果當送達。
    /// </summary>
    /// <param name="sdk">當次外殼擁有的 recording 服務，借用至 Task 排空。</param>
    /// <param name="activityId">平台提供的 activityid。</param>
    /// <param name="organizationId">平台已驗組織身分，不能由來源記錄或 request 代入。</param>
    /// <param name="sink">同次外殼的有界摘要 sink。</param>
    /// <param name="ct">caller 取消；同步 SDK 前後檢查，transport 期限由平台掌握。</param>
    public static Task RecordAsync(IOrganizationService sdk, Guid activityId, Guid organizationId,
        IShadowPushSink sink, CancellationToken ct)
    {
#if NETFRAMEWORK
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
        if (sink is null) throw new ArgumentNullException(nameof(sink));
#else
        ArgumentNullException.ThrowIfNull(sdk);
        ArgumentNullException.ThrowIfNull(sink);
#endif
        return ProcessAsync(sdk, activityId, organizationId, sink, null, null, null, ct);
    }

    /// <summary>Host單筆代送；來源重驗後才解密，沿既有規則組字及回寫，retryable錯誤不回寫終止狀態。</summary>
    public static Task SendAsync(IOrganizationService sdk, ILineMemberPushClient push, Guid activityId, Guid organizationId,
        Guid expectedChurch, byte[] key, CancellationToken ct) => ProcessAsync(sdk, activityId, organizationId, null, push, key, expectedChurch, ct);

    /// <summary>共用正式活動讀取、來源教會推導與狀態patch；所有借用物件僅存活本次await。</summary>
    private static async Task ProcessAsync(IOrganizationService sdk, Guid activityId, Guid organizationId,
        IShadowPushSink? sink, ILineMemberPushClient? push, byte[]? key, Guid? expectedChurch, CancellationToken ct)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(sdk);
#else
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
#endif
        if (activityId == Guid.Empty || organizationId == Guid.Empty)
            throw new ArgumentException("A formal letter and organization identity are required.");
        var row = WebhookRecordOperations.Read(sdk, "letter", activityId,
            new ColumnSet("directioncode", "description", "from", "to", "statecode", "new_message_category",
                "category", "subcategory"), ct);
        if (row.GetAttributeValue<bool?>("directioncode") != true) return;
        var state = row.GetAttributeValue<OptionSetValue>("statecode")
            ?? throw new InvalidOperationException("A formal letter state is required.");
        if (state.Value != 0) return;
        var category = row.GetAttributeValue<string>("category");
        var subcategory = row.GetAttributeValue<string>("subcategory");
        if ((!string.IsNullOrEmpty(category) && !IsOwnedCategory(category)) ||
            (!string.IsNullOrEmpty(subcategory) && !IsOwnedCategory(category)))
            throw new InvalidOperationException("Letter status fields are owned by another process.");
        if (row.GetAttributeValue<OptionSetValue>("new_message_category") is { Value: not 100000000 })
            throw new InvalidOperationException("Outbound letter media is outside the migrated text contract.");
        var recipient = Party(row, "to");
        if (recipient.LogicalName != "contact")
            throw new InvalidOperationException("A single formal contact recipient is required.");
        var sender = Party(row, "from");
        Guid churchId;
        if (sender.LogicalName == "account") churchId = sender.Id;
        else if (sender.LogicalName == "contact")
        {
            var contact = WebhookRecordOperations.Read(sdk, "contact", sender.Id, new ColumnSet("parentcustomerid"), ct);
            churchId = Lookup(contact, "parentcustomerid", "account").Id;
        }
        else throw new InvalidOperationException("The sender has no proven source church relationship.");

        if (expectedChurch.HasValue && churchId != expectedChurch.Value) return;
        using var source = new NotificationSource(sdk, key is not null);
        var catalog = new GoodShepherdTextCatalog(new NoOverrides());
        var notifications = new MemberNotificationService(push ?? new NoDelivery(),
            new MemberCultureResolver(new LocalizationOptions()), catalog);
        var context = new TextContext(churchId, "zh-TW", "zh-TW", organizationId.ToString("D"), "GoodShepherd");
        var rule = new OutboundLetterRule(notifications);
        var outcome = sink is not null
            ? await rule.RecordAsync(true, source, context, activityId, recipient.Id, row.GetAttributeValue<string>("description") ?? "", sink, ct).ConfigureAwait(false)
            : await rule.SendRelayAsync(source, context, activityId, recipient.Id, row.GetAttributeValue<string>("description") ?? "", key!, ct).ConfigureAwait(false);
        var patch = new Entity("letter", activityId);
        if (outcome.Status is OutboundLetterStatus.IntentRecorded or OutboundLetterStatus.Completed)
        {
            // 這是「若供應商接受」的完整寫入意圖，外殼只會把它寫入 recording SDK。
            patch["statecode"] = new OptionSetValue(1); patch["statuscode"] = new OptionSetValue(4);
            patch["category"] = "SMV:Messaging.Completed"; patch["subcategory"] = null;
        }
        else if (outcome.Status == OutboundLetterStatus.Failed)
        {
            patch["statecode"] = new OptionSetValue(2); patch["statuscode"] = new OptionSetValue(5);
            patch["category"] = "SMV:" + outcome.FailureKey;
            var culture = source.Culture ?? "zh-TW";
            patch["subcategory"] = catalog.GetText(context.WithCultures(culture, culture), new TextKey(outcome.FailureKey!));
        }
        else return;
        ct.ThrowIfCancellationRequested();
        sdk.Update(patch);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>只承認自己明列的固定狀態，不讓任意 SMV 前綴取得他人欄位所有權。</summary>
    private static bool IsOwnedCategory(string? category) => category is "SMV:Messaging.Completed" or
        "SMV:Messaging.Failure.NotBound" or "SMV:Messaging.Failure.ChurchMismatch" or "SMV:Messaging.Failure.Blocked";

    /// <summary>Party 集合須恰好一筆正式 lookup；不按名稱挑人，也不丟棄重複 party。</summary>
    private static EntityReference Party(Entity row, string field)
    {
        var parties = row.GetAttributeValue<EntityCollection>(field);
        if (parties is null || parties.Entities.Count != 1 || parties.Entities[0].LogicalName != "activityparty")
            throw new InvalidOperationException("A single formal activity party is required.");
        return Lookup(parties.Entities[0], "partyid", null);
    }

    /// <summary>Lookup 只以精確表與正式非空主鍵辨識，Name 與替代鍵不作為權威。</summary>
    private static EntityReference Lookup(Entity row, string field, string? table)
    {
        var value = row.GetAttributeValue<EntityReference>(field);
        if (value is null || value.Id == Guid.Empty || value.KeyAttributes.Count != 0 ||
            (table is not null && value.LogicalName != table))
            throw new InvalidOperationException("A formal source relationship is required.");
        return value;
    }

    /// <summary>單次來源；Shadow只查正式頻道身分，Live才沿共用adapter讀加密token。</summary>
    private sealed class NotificationSource(IOrganizationService sdk, bool live) : ILineRecipientSource, IDisposable
    {
        private IOrganizationService? service = sdk;
        /// <summary>來源教會的顯示文化快照，只活至本次 await 排空，用於拒絕原因。</summary>
        internal string? Culture { get; private set; }
        /// <summary>既有最小 Contact 查詢器在同步讀取後立即清借用參照。</summary>
        public MemberRecipient ReadMember(Guid id, CancellationToken ct)
        {
            using var query = new LineRecipientQueries(GetService());
            return query.ReadMember(id, ct);
        }
        /// <summary>語系及頻道從正式來源account指派核對；Shadow不讀token，Live才借用共用adapter。</summary>
        public ChurchChannelRecord ReadChannel(Guid id, CancellationToken ct)
        {
            var current = GetService();
            var row = WebhookRecordOperations.Read(current, "account", id,
                new ColumnSet("name", "new_linechannelid", "new_defaultlanguage", "new_enabledlanguages"), ct);
            var channel = Lookup(row, "new_linechannelid", "new_linechannel").Id;
            _ = WebhookRecordOperations.Read(current, "new_linechannel", channel, new ColumnSet(false), ct);
            var culture = row.GetAttributeValue<string>("new_defaultlanguage");
            Culture = string.IsNullOrWhiteSpace(culture) ? "zh-TW" : culture;
            var names = row.GetAttributeValue<string>("new_enabledlanguages") ?? Culture;
            if (names.Length > 8255) throw new InvalidOperationException("Church culture settings exceed the budget.");
            var enabled = names.Split(',');
            if (enabled.Length > 64) throw new InvalidOperationException("Church culture count exceeds the budget.");
            for (var index = 0; index < enabled.Length; index++) enabled[index] = enabled[index].Trim();
            if (live)
            {
                using var query = new LineRecipientQueries(current);
                return query.ReadChannel(id, ct);
            }
            return new ChurchChannelRecord(id, channel, channel, row.GetAttributeValue<string>("name") ?? "",
                "", new ChurchLocaleSettings(Culture, enabled));
        }
        /// <summary>清理後拒絕新操作，避免使用前一活動借用的 SDK。</summary>
        private IOrganizationService GetService() => service ?? throw new ObjectDisposedException(nameof(NotificationSource));
        /// <summary>唯一 owner 等待規則完成後清除借用參照，不 Dispose 平台 SDK。</summary>
        public void Dispose() { service = null; Culture = null; }
    }

    /// <summary>任何意外傳輸都是程式錯誤，不能以假成功或 no-op 蓋過影子邊界。</summary>
    private sealed class NoDelivery : ILineMemberPushClient
    {
        /// <summary>不建立 HttpClient 或憑證，意外呼叫直接失敗。</summary>
        public Task<LinePushResult> PushToMemberAsync(MemberRecipient recipient, ChurchChannel channel, Guid church,
            System.Collections.Generic.IReadOnlyList<ISendMessage> messages, Guid retryKey, CancellationToken ct) =>
            throw new InvalidOperationException("Outbound letter Shadow cannot deliver.");
    }

    /// <summary>當次字典以空覆寫使用系統資源，沒有跨教會快取。</summary>
    private sealed class NoOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>依目前完整文字 scope 產生空快照，不保存來源參照。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}
