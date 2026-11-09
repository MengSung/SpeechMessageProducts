/*
檔案整體說明
目的與定位：把已驗簽 envelope 的 channel 提示轉成真正可信 CRM 組織、教會、頻道及使用者範圍；不沿用舊 ToolUtilityFactory 的程序級身分。
主要功能與流程：只讀 Host 的 LineWebhook:Channels 映射；每筆以 WhoAmI 複核 OrganizationId，再以既有 LineRecipientQueries 讀 ChurchId 指派及加密 token，驗 new_linechannel.new_channelid 等於入站頻道。Contact 同時依 parentcustomerid 與 new_lineid 查詢且拒絕多筆。沿用 ChannelTokenProtector，不另造密碼實作或跨頻道後備。
使用邊界與維護：DI scoped 借用 SDK，生命週期由 queue scope 管理；同步 SDK 有 transport deadline 並於返回前 drain。設定不由事件控制，無 Session、cache、背景工作。解密 key byte[] 在 finally 清除；解密 token 只屬當次結果且 ToString 遮罩，不寫診斷。正式部署須提供明確 OrganizationId、ChurchId、TimeZone；缺項固定拒絕，不猜測預設教會。
*/
using LineMessagingProcessor.Core.Security;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using ToolUtilityNameSpace.LineRecipients;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;

/// <summary>事件範圍解析契約；正式實作必須核對實際組織，不能只包裝 caller 提供的值。</summary>
public interface IWebhookScopeResolver
{
    /// <summary>同一筆事件內完成 CRM 授權與 token 準備；真正故障不降級至其他組織。</summary>
    ResolvedWebhookScope Resolve(string channelId, string sourceId, CancellationToken ct);

    /// <summary>
    /// 同時解析 actor 與群組／聊天室 display source；預設實作保留單一來源的相容行為，
    /// 正式 resolver 必須覆寫以完成兩個 Contact 的獨立 scope 驗證。
    /// </summary>
    ResolvedWebhookScope Resolve(string channelId, string actorSourceId, string displaySourceId, CancellationToken ct) =>
        Resolve(channelId, actorSourceId, ct);
}

/// <summary>僅供單次 dispatch 使用的已驗證資料；不是可共享或記錄的 DTO。</summary>
public sealed class ResolvedWebhookScope
{
    /// <summary>單一來源相容建構式；actor 與 display source 相同。</summary>
    public ResolvedWebhookScope(
        IOrganizationService service,
        Guid organizationId,
        Guid churchId,
        string channelId,
        string sourceId,
        string timeZone,
        string accessToken,
        Entity? contact)
        : this(service, organizationId, churchId, channelId, sourceId, sourceId, timeZone, accessToken, contact, contact)
    {
    }

    /// <summary>
    /// 組合根或離線測試建立範圍，SDK 仍由外層 owner 擁有；actor 與 display source
    /// 分開保存，避免群組／聊天室顯示資料取代真正授權使用者。
    /// </summary>
    public ResolvedWebhookScope(
        IOrganizationService service,
        Guid organizationId,
        Guid churchId,
        string channelId,
        string actorSourceId,
        string displaySourceId,
        string timeZone,
        string accessToken,
        Entity? contact,
        Entity? displayContact)
    {
        Service = service ?? throw new ArgumentNullException(nameof(service));
        if (organizationId == Guid.Empty || churchId == Guid.Empty || !WebhookOptions.IsValidChannelId(channelId) ||
            !ValidSource(actorSourceId) || !ValidSource(displaySourceId) ||
            string.IsNullOrWhiteSpace(timeZone) || string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Incomplete webhook scope.");
        OrganizationId = organizationId;
        ChurchId = churchId;
        ChannelId = channelId;
        SourceId = actorSourceId;
        DisplaySourceId = displaySourceId;
        TimeZone = timeZone;
        AccessToken = accessToken;
        Contact = contact;
        DisplayContact = displayContact;
    }
    /// <summary>已通過組織核對的借用 SDK，由 queue 的 DI scope 釋放。</summary>
    public IOrganizationService Service { get; }
    /// <summary>WhoAmI 的正式組織 ID。</summary>
    public Guid OrganizationId { get; }
    /// <summary>受部署映射及 account 指派核對的教會 ID。</summary>
    public Guid ChurchId { get; }
    /// <summary>已驗簽的供應商頻道 ID。</summary>
    public string ChannelId { get; }
    /// <summary>本次 U／C／R 識別，不能用顯示名稱取代。</summary>
    public string SourceId { get; }
    /// <summary>群組／聊天室的顯示來源正式識別；與 actor source 分開授權與查詢。</summary>
    public string DisplaySourceId { get; }
    /// <summary>本教會明確時區，不從伺服器 local 推測。</summary>
    public string TimeZone { get; }
    /// <summary>當次查詢的 Contact，僅供本次更新，不向結果或診斷暴露。</summary>
    internal Entity? Contact { get; }
    /// <summary>當次查詢的 display Contact，只供舊業務需要的顯示名稱，不作 actor 授權。</summary>
    internal Entity? DisplayContact { get; }
    /// <summary>僅供逐 request Authorization，存活至 dispatch 結束，不設定共用 header。</summary>
    internal string AccessToken { get; }
    /// <summary>只有真正綁定 Contact 才能建立領域 actor；後續核心仍複核綁定。</summary>
    internal WebhookActor Actor() => Contact is null ? throw new InvalidOperationException("A bound actor is required.") : new WebhookActor(OrganizationId, ChannelId, ChurchId, Contact.Id, SourceId, TimeZone);
    /// <summary>委派核心固定長度十六進位格式檢查；授權仍由本 resolver 的組織／教會綁定流程完成。</summary>
    internal static bool ValidSource(string value) => WebhookRecordOperations.IsValidLineIdentity(value);
    /// <summary>不輸出 SDK、token、使用者或 Contact。</summary>
    public override string ToString() => "ResolvedWebhookScope(***)";
}

/// <summary>借用原 Host 組態與 scoped SDK，不建立另一套連線池或信任任何 request 租戶值。</summary>
public sealed class WebhookScopeResolver(IOrganizationService service, IConfiguration configuration) : IWebhookScopeResolver
{
    /// <summary>單一來源相容入口；actor 與 display source 使用同一正式識別。</summary>
    public ResolvedWebhookScope Resolve(string channelId, string sourceId, CancellationToken ct) =>
        Resolve(channelId, sourceId, sourceId, ct);

    /// <summary>
    /// 核對組織及教會後分別查 actor 與 display source；多筆綁定、缺設定或外頻道
    /// 均拒絕，原故障交 queue。display source 不得用來替代 actor 的授權 Contact。
    /// </summary>
    public ResolvedWebhookScope Resolve(string channelId, string actorSourceId, string displaySourceId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!WebhookOptions.IsValidChannelId(channelId) ||
            !ResolvedWebhookScope.ValidSource(actorSourceId) ||
            !ResolvedWebhookScope.ValidSource(displaySourceId))
            throw new InvalidOperationException("Invalid webhook scope input.");
        var mapping = configuration.GetSection("LineWebhook:Channels:" + channelId);
        var organization = mapping["Organization"];
        if (string.IsNullOrWhiteSpace(organization) || organization.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') ||
            !Guid.TryParse(mapping["OrganizationId"], out var orgId) || orgId == Guid.Empty ||
            !Guid.TryParse(mapping["ChurchId"], out var churchId) || churchId == Guid.Empty || string.IsNullOrWhiteSpace(mapping["TimeZone"]))
            throw new InvalidOperationException("Webhook dispatch mapping is incomplete.");
        var who = service.Execute(new WhoAmIRequest()) as WhoAmIResponse;
        ct.ThrowIfCancellationRequested();
        if (who is null || who.OrganizationId != orgId) throw new InvalidOperationException("The CRM organization does not match the webhook deployment.");
        using var source = new LineRecipientQueries(service);
        var church = source.ReadChannel(churchId, ct);
        var channel = WebhookRecordOperations.Read(service, "new_linechannel", church.ChannelId, new ColumnSet("new_channelid"), ct);
        if (channel.GetAttributeValue<string>("new_channelid") != channelId) throw new InvalidOperationException("The channel is not assigned to this church.");
        var contact = ReadBoundContact(churchId, actorSourceId, ct);
        var displayContact = string.Equals(actorSourceId, displaySourceId, StringComparison.Ordinal)
            ? contact
            : ReadBoundContact(churchId, displaySourceId, ct);
        var keyPath = "Deployment:Organizations:" + organization + ":TokenKey";
        var key = Convert.FromBase64String(configuration[keyPath] ?? throw new InvalidOperationException("The organization token key is unavailable."));
        try
        {
            return new ResolvedWebhookScope(
                service,
                orgId,
                churchId,
                channelId,
                actorSourceId,
                displaySourceId,
                mapping["TimeZone"]!,
                ChannelTokenProtector.Unprotect(church.EncryptedToken, key),
                contact,
                displayContact);
        }
        finally { Array.Clear(key); }
    }

    /// <summary>以完整 church＋LINE ID 條件讀取唯一 Contact；空集合代表尚未綁定，不猜測身份。</summary>
    private Entity? ReadBoundContact(Guid churchId, string sourceId, CancellationToken ct)
    {
        var query = new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet("new_lineid", "parentcustomerid", "fullname", "customertypecode", "versionnumber")
        };
        query.Criteria.AddCondition("parentcustomerid", ConditionOperator.Equal, churchId);
        query.Criteria.AddCondition("new_lineid", ConditionOperator.Equal, sourceId);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var contacts = WebhookRecordOperations.Query(service, query, ct);
        if (contacts.Entities.Count > 1) throw new InvalidOperationException("Conflicting LINE bindings require remediation.");
        var contact = contacts.Entities.SingleOrDefault();
        if (contact is not null)
        {
            WebhookRecordOperations.RequireLookup(contact, "parentcustomerid", "account", churchId);
            if (!string.Equals(contact.GetAttributeValue<string>("new_lineid"), sourceId, StringComparison.Ordinal))
                throw new InvalidOperationException("The Contact binding does not match.");
        }
        return contact;
    }
}
