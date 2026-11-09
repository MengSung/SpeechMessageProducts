/*
檔案整體說明

目的與定位：提供 P3-08 入站 Webhook letter 的 CRM 寫入邊界，將已授權的 Contact、
教會及事件資料建立成可重送的正式 activity。

主要功能與流程：以 organization／church／letter／webhookEventId 推導穩定資料庫 ID，
先在操作區域建立完整 candidate，再以既有 WebhookRecordOperations.Upsert 原子發布。
candidate 包含 subject、description、LINE ID、regarding contact、scheduledend、
directioncode=false、new_count=1、new_message_category=100000000 與 from activityparty。

使用邊界與維護：只接受 scope 已核對的 actor，不保存 reply token，不建立 outbound
Plugin 呼叫、queue、timer 或 cache；Upsert 的方向欄位讓既有外送流程排除入站資料。
Entity 與 activityparty 僅活到本次同步 SDK 操作完成，真正例外原樣交給 dispatcher 的
Hosting 共用通知入口。穩定 ID 重送更新同一筆，資料庫唯一性由 CRM／Dataverse 擁有。
*/
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;

/// <summary>單筆入站 letter 的 CRM 寫入契約；呼叫端必須等待返回後才釋放 scope。</summary>
public interface IWebhookLetterRecorder
{
    /// <summary>建立或更新本事件的唯一入站 letter。</summary>
    /// <param name="scope">已授權 actor、教會及組織範圍。</param>
    /// <param name="webhookEventId">provider 穩定事件 ID。</param>
    /// <param name="messageKind">固定事件類型代碼。</param>
    /// <param name="description">本次入站內容的 bounded 描述。</param>
    /// <param name="cancellationToken">queue 單事件取消。</param>
    ValueTask<Guid> RecordAsync(
        ResolvedWebhookScope scope,
        string webhookEventId,
        string messageKind,
        string description,
        CancellationToken cancellationToken);
}

/// <summary>使用 Dataverse Upsert 的正式 letter owner；不依賴程序鎖或先查再建。</summary>
public sealed class WebhookLetterRecorder(GoodShepherdTextCatalog catalog) : IWebhookLetterRecorder
{
    /// <summary>固定入站分類值，沿用既有 letter 欄位契約。</summary>
    private const int InboundMessageCategory = 100000000;

    private readonly GoodShepherdTextCatalog catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    /// <summary>建立完整 candidate 後一次 Upsert；不保留 token 或輸入參照。</summary>
    public ValueTask<Guid> RecordAsync(
        ResolvedWebhookScope scope,
        string webhookEventId,
        string messageKind,
        string description,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(webhookEventId) || webhookEventId.Length > 64 ||
            string.IsNullOrWhiteSpace(messageKind) || messageKind.Length > 32 ||
            description is null || description.Length > 5000)
            throw new ArgumentException("The inbound letter boundary is invalid.");
        cancellationToken.ThrowIfCancellationRequested();
        var actor = scope.Actor();
        var id = WebhookRecordOperations.BusinessId(scope.OrganizationId, scope.ChurchId, "letter", webhookEventId);
        var party = new Entity("activityparty")
        {
            ["partyid"] = new EntityReference("contact", actor.ContactId)
        };
        var candidate = new Entity("letter", id)
        {
            ["subject"] = catalog.GetText(scope.TextContext, new TextKey("Webhook.Letter.Subject")),
            ["description"] = description.Length == 0 ? messageKind : description,
            ["new_displayed_lineid"] = actor.UserId,
            ["regardingobjectid"] = new EntityReference("contact", actor.ContactId),
            ["scheduledend"] = DateTime.UtcNow,
            ["directioncode"] = false,
            ["new_count"] = 1,
            ["new_message_category"] = new OptionSetValue(InboundMessageCategory),
            ["from"] = new EntityCollection(new[] { party })
        };
        var published = WebhookRecordOperations.Upsert(scope.Service, candidate, id, cancellationToken);
        return ValueTask.FromResult(published);
    }
}
