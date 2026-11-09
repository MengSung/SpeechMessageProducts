/*
檔案整體說明
目的與定位：移植 LineBotApp.PostbackParsers.cs:15–85、PostbackHandlers.cs:42–190 及 SigningProcessorClass.cs:129–192 的可證明簽核流程。
主要功能與流程：有界解析 key/value，拒絕重複鍵，訊息類別由固定 resx 詞彙辨識。一般簽核先讀 appointment.new_signing_contact 的正式 ID，取代舊姓名比較；只接受同意／退回，條件更新兩個舊 option set。課程簽核來源缺權威 actor 規則，單項延後且不寫入；現金繳費屬階段四，拒絕副作用。
使用邊界與維護：postback 的 EntityId／Signer 不是授權，不能信任顯示名稱；本類先複核 actor 的教會綁定，record signer 必須相符。解析資料只屬目前事件且至多 4096 字元／16 欄，無 static queue、憑證或背景工作。SDK 由 caller 擁有且有 transport deadline，取消前後檢查，原始故障上傳共用告警。
*/
using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;

namespace SpeechMessageProducts.GoodShepherd.Core.Appointments;

/// <summary>具體一般簽核 handler，沒有 legacy 靜態 queue 或以姓名核准的後備。</summary>
public static class WebhookPostbackHandler
{
    /// <summary>完整驗證解析及 actor 後才寫 CRM；有歧義的課程／付款項目各自具名延後。</summary>
    public static WebhookBusinessResult Handle(IOrganizationService sdk, WebhookActor actor, string data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (data is null || data.Length > 4096) throw new ArgumentException("Invalid bounded postback.", nameof(data));
        if (data is "Date" or "Time" or "DateTime") return new WebhookBusinessResult(WebhookBusinessStatus.ReplyPending, data + "SelectionPendingP308");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var segments = data.Split('&');
        if (segments.Length > 16) throw new ArgumentException("Postback field budget exceeded.", nameof(data));
        foreach (var segment in segments)
        {
#if NETFRAMEWORK
            var separator = segment.IndexOf('=');
#else
            var separator = segment.IndexOf('=', StringComparison.Ordinal);
#endif
            if (separator < 1) continue;
            var key = segment.Substring(0, separator);
            if (fields.ContainsKey(key)) throw new ArgumentException("Ambiguous postback fields.", nameof(data));
            fields.Add(key, segment.Substring(separator + 1));
        }
        if (!fields.TryGetValue(Word("Type"), out var type)) return new WebhookBusinessResult(WebhookBusinessStatus.ReplyPending, "UnknownPostbackEchoPendingP308");
        if (type == Word("Cash")) return new WebhookBusinessResult(WebhookBusinessStatus.Deferred, "CashPaymentStageFourExcluded");
        if (type == Word("LessonApproval")) return new WebhookBusinessResult(WebhookBusinessStatus.Deferred, "LessonApprovalAuthoritativeSignerRequired");
        if (type == Word("Enrollment")) return new WebhookBusinessResult(WebhookBusinessStatus.Deferred, "EnrollmentFeeAndAtomicCreateContractRequired");
        if (type != Word("Approval")) return new WebhookBusinessResult(WebhookBusinessStatus.Deferred, "PostbackBusinessContractRequired");
        if (!fields.TryGetValue("EntityId", out var rawId) || !Guid.TryParse(rawId, out var id) || id == Guid.Empty ||
            !fields.TryGetValue(Word("Action"), out var action) || (action != WebhookText.Neutral("Webhook.Word.Consent") && action != WebhookText.Neutral("Webhook.Word.Return")))
            return new WebhookBusinessResult(WebhookBusinessStatus.Rejected, "InvalidApprovalPayload");
        WebhookRecordOperations.ReadActor(sdk, actor, ct);
        var record = WebhookRecordOperations.Read(sdk, "appointment", id, new ColumnSet("new_signing_contact", "versionnumber"), ct);
        var signer = record.GetAttributeValue<EntityReference>("new_signing_contact");
        if (signer is null || signer.LogicalName != "contact" || signer.Id != actor.ContactId)
            return new WebhookBusinessResult(WebhookBusinessStatus.Rejected, "NotAuthoritativeAppointmentSigner");
        var patch = new Entity("appointment", id)
        {
            ["new_signing_status"] = new OptionSetValue(action == WebhookText.Neutral("Webhook.Word.Consent") ? 100000001 : 100000002),
            ["new_update_event"] = new OptionSetValue(100000001)
        };
        WebhookRecordOperations.Update(sdk, record, patch, ct);
        return new WebhookBusinessResult(WebhookBusinessStatus.Applied, "AppointmentApprovalUpdatedReplyPendingP308", recordIds: new[] { id });
    }
    /// <summary>協定中文欄位同樣由正式資源生成，避免繞過 I18n 檢查。</summary>
    private static string Word(string key) => WebhookText.Neutral("Webhook.Postback." + key);
}
