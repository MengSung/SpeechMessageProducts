/*
檔案整體說明
目的與定位：接通舊 Dynamics365ProcessorClass.MessageStore.cs:403–529 的語音附件操作，修正 Bearer、容量與重送身分；不虛構舊 comment-only message 入口已有行為。
主要功能與流程：先複核 authoritative Contact，再經真 HTTP adapter 完整下載，沿用 AttachmentService 的七欄與 Base64／容量驗證。窄 Create adapter 把唯一 annotation Create 原子轉成固定正式主鍵 Upsert，以完整組織／教會／channel／event／contact 隔離重送。P3-08 letter 未接通前 parent 明確為已授權 contact，不假稱建立 letter。
使用邊界與維護：bytes 僅在目前呼叫存活並於 finally 清除；SDK 借入且不 Dispose，wrapper 不保存到 scope 外。所有 HTTP await 完成後才寫入，失敗／取消／格式／超限不得寫 CRM；同步寫入由 Host deadline 與 drain 管理。真正 Exception 沿既有 AttachmentService wrapper 保留 InnerException，交 queue 共用告警。
*/
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using ToolUtilityNameSpace.AttachmentOperations;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;

/// <summary>真正音訊下載到 annotation 的組合 handler，沒有未註冊 sink 或假成功後備。</summary>
public sealed class WebhookAudioHandler(WebhookHttpAdapter http)
{
    /// <summary>以事件正式識別及 Contact 寫入冪等附件；不保留 user token 或 bytes 作後續工作。</summary>
    public async Task<WebhookBusinessResult> HandleAsync(ResolvedWebhookScope scope, string eventId, string messageId, CancellationToken ct)
    {
        var actor = scope.Actor();
        WebhookRecordOperations.ReadActor(scope.Service, actor, ct);
        var bytes = await http.DownloadAudioAsync(scope, messageId, ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var id = WebhookRecordOperations.BusinessId(scope.OrganizationId, scope.ChurchId, "line-audio", scope.ChannelId + "/" + eventId + "/" + actor.ContactId.ToString("N"));
            IOrganizationService writer = new AnnotationUpsertService(scope.Service, actor.ContactId, id, ct);
            var attachment = new AttachmentService(new object(), writer);
            attachment.UploadAttachment(ref writer, "contact", WebhookText.Neutral("Webhook.Audio.Subject"), WebhookText.Neutral("Webhook.Audio.Subject"),
                WebhookText.Neutral("Webhook.Audio.FileName"), "audio/x-m4a", bytes, actor.ContactId);
            ct.ThrowIfCancellationRequested();
            return new WebhookBusinessResult(WebhookBusinessStatus.Applied, "AudioAnnotationStoredContactParentLetterPendingP308", recordIds: new[] { id });
        }
        finally { Array.Clear(bytes); }
    }

    /// <summary>只接受 AttachmentService 的一次 annotation Create；不擁有 SDK，所有其他方法封閉失敗。</summary>
    private sealed class AnnotationUpsertService(IOrganizationService sdk, Guid contactId, Guid annotationId, CancellationToken ct) : IOrganizationService
    {
        /// <summary>沿用共用附件候選，只加資料庫主鍵與原子 Upsert；回應主鍵必須完全相符。</summary>
        public Guid Create(Entity entity)
        {
            if (entity.LogicalName != "annotation") throw new InvalidOperationException("Only annotation creation is permitted.");
            WebhookRecordOperations.RequireLookup(entity, "objectid", "contact", contactId);
            entity.Id = annotationId;
            ct.ThrowIfCancellationRequested();
            var response = sdk.Execute(new UpsertRequest { Target = entity }) as UpsertResponse;
            ct.ThrowIfCancellationRequested();
            if (response?.Target is not { LogicalName: "annotation" } target || target.Id != annotationId)
                throw new InvalidOperationException("Unexpected annotation identity.");
            return annotationId;
        }
        /// <summary>此窄 adapter 不接受任意查詢，防止附件入口擴張權限。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new InvalidOperationException("Annotation adapter operation rejected.");
        /// <summary>此窄 adapter 沒有查詢權限。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new InvalidOperationException("Annotation adapter operation rejected.");
        /// <summary>此窄 adapter 不開放任意 SDK request。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw new InvalidOperationException("Annotation adapter operation rejected.");
        /// <summary>只允許已定義的 Create-to-Upsert，不提供盲目 Update。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException("Annotation adapter operation rejected.");
        /// <summary>不得刪除任何既有資料。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException("Annotation adapter operation rejected.");
        /// <summary>不建立其他關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException("Annotation adapter operation rejected.");
        /// <summary>不移除其他關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException("Annotation adapter operation rejected.");
    }
}
