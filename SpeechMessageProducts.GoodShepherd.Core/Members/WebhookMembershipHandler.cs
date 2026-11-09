/*
檔案整體說明
目的與定位：移植 LineBotApp.FollowEvents.cs:19–132 與 GroupEvents.cs:19–45、106–171 的 Contact 個人／群組資料異動。
主要功能與流程：主機完成 profile HTTP 與 channel／教會驗證後，將純值交本核心；新 contact 設定舊欄位但同時綁定已授權 account，既有個人 follow 設已關注狀態，unfollow 設封鎖狀態。新記錄採完整範圍的固定資料庫主鍵，衝突即拒絕，不以姓名合併。
使用邊界與維護：只借當次 SDK 與 Entity，不保存 profile、LINE ID 或憑證；同步寫入有外層期限、取消檢查及 drain。既有 Contact 必須同時具備同一教會、同一 LINE ID 與有效 rowversion，否則 fail closed，不接管不完整資料。未知 actor 的 unfollow 沿來源不寫資料；不建姓名手機註冊、不造綁定 URL。歡迎 Reply 尚待 P3-08，結果明列 pending。所有欄位候選完整後才寫，真正失敗不轉成功。
*/
using System;
using System.Threading;
using Microsoft.Xrm.Sdk;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;

namespace SpeechMessageProducts.GoodShepherd.Core.Members;

/// <summary>個人與群組 Contact 的具體領域規則，不自行取得 HTTP 或共享 SDK。</summary>
public static class WebhookMembershipHandler
{
    /// <summary>
    /// 移植指定 GroupEvents.cs:106–171 的 Join Contact 異動；群組可由主機讀摘要，
    /// 聊天室因本包沒有已證明摘要契約而使用固定 fallback。既有資料必須精確綁定相同
    /// LINE ID 才能以 rowversion 更新；新資料以完整範圍的正式 ID 原子 Upsert。
    /// Join reply 不在此送出，留給 P3-08。
    /// </summary>
    public static WebhookBusinessResult Join(
        IOrganizationService sdk,
        Guid organization,
        Guid church,
        string sourceId,
        Entity? existing,
        string displayName,
        string pictureUrl,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || church == Guid.Empty ||
            !WebhookRecordOperations.IsValidLineIdentity(sourceId) || sourceId[0] is not ('C' or 'R') ||
            string.IsNullOrWhiteSpace(displayName) || displayName.Length > 256 ||
            pictureUrl is null || pictureUrl.Length > 2048)
            throw new ArgumentException("Invalid group or room join boundary.");

        var isGroup = sourceId[0] == 'C';
        var id = existing?.Id ?? WebhookRecordOperations.BusinessId(organization, church, "line-contact", sourceId);
        if (existing is not null)
        {
            WebhookRecordOperations.RequireLookup(existing, "parentcustomerid", "account", church);
            if (!string.Equals(existing.GetAttributeValue<string>("new_lineid"), sourceId, StringComparison.Ordinal))
                throw new InvalidOperationException("The group or room binding does not match.");
        }

        var patch = new Entity("contact", id)
        {
            ["lastname"] = displayName,
            ["new_line_displayname"] = displayName,
            ["new_line_picture_url"] = pictureUrl,
            ["new_line_type"] = WebhookText.Neutral("Webhook.Word." + (isGroup ? "Group" : "Room")),
            ["new_lineid"] = sourceId,
            ["new_lineid_backup"] = sourceId,
            ["new_line_register"] = false,
            ["new_line_status"] = new OptionSetValue(100000001)
        };

        if (existing is null)
        {
            patch["parentcustomerid"] = new EntityReference("account", church);
            WebhookRecordOperations.Upsert(sdk, patch, id, ct);
        }
        else
        {
            WebhookRecordOperations.Update(sdk, existing, patch, ct);
        }

        return new WebhookBusinessResult(
            WebhookBusinessStatus.Applied,
            isGroup ? "GroupProfileUpdatedWelcomePendingP308" : "RoomProfileUpdatedWelcomePendingP308",
            recordIds: new[] { id });
    }

    /// <summary>
    /// 移植 FollowEvents.cs:44–84、115–132 的 Contact 欄位更新。方法先驗證完整組織、
    /// 教會、LINE 識別與 profile 預算，再核對既有 Contact 的教會及相同 LINE 綁定；
    /// 新增以穩定資料庫 ID 原子 Upsert，既有資料以 rowversion 條件更新。
    /// profile 純值只在本次同步呼叫存活，SDK 由 queue scope 借用；取消或任何驗證、
    /// CRM 失敗都會向上傳播，不以成功結果掩蓋。此流程不執行姓名加手機綁定。
    /// </summary>
    public static WebhookBusinessResult Follow(IOrganizationService sdk, Guid organization, Guid church, string sourceId, Entity? existing, string name, string picture, string status, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || church == Guid.Empty ||
            !WebhookRecordOperations.IsValidLineIdentity(sourceId) ||
            string.IsNullOrWhiteSpace(name) || name.Length > 256 ||
            picture is null || picture.Length > 2048 ||
            status is null || status.Length > 500)
            throw new ArgumentException("Invalid follow identity or profile boundary.");

        var group = sourceId[0] != 'U';
        var id = existing?.Id ?? WebhookRecordOperations.BusinessId(organization, church, "line-contact", sourceId);
        if (existing is not null)
        {
            WebhookRecordOperations.RequireLookup(existing, "parentcustomerid", "account", church);
            if (existing.GetAttributeValue<string>("new_lineid") != sourceId) throw new InvalidOperationException("The contact binding does not match.");
        }
        var patch = new Entity("contact", id)
        {
            ["new_line_displayname"] = name, ["new_line_picture_url"] = picture,
            ["new_line_status_message"] = status,
            ["new_line_type"] = WebhookText.Neutral("Webhook.Word." + (group ? "Group" : "Individual"))
        };
        if (existing is null)
        {
            patch["parentcustomerid"] = new EntityReference("account", church);
            patch["new_lineid"] = sourceId; patch["new_lineid_backup"] = sourceId;
            patch["lastname"] = group ? name : name + "(Line)";
            patch["new_line_register"] = false; patch["customertypecode"] = new OptionSetValue(100000000);
            patch["new_line_status"] = new OptionSetValue(100000001);
            WebhookRecordOperations.Upsert(sdk, patch, id, ct);
        }
        else
        {
            if (group) patch["lastname"] = name;
            else patch["new_line_status"] = new OptionSetValue(100000003);
            WebhookRecordOperations.Update(sdk, existing, patch, ct);
        }
        return new WebhookBusinessResult(WebhookBusinessStatus.Applied, group ? "GroupProfileUpdatedWelcomePendingP308" : "FollowProfileUpdatedWelcomePendingP308", recordIds: new[] { id });
    }
    /// <summary>原來源 FollowEvents.cs:89–109；查無 contact 不寫入，既有則以正式 ID 與版本設封鎖。</summary>
    public static WebhookBusinessResult Unfollow(IOrganizationService sdk, Guid church, string sourceId, Entity? contact, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (church == Guid.Empty || !WebhookRecordOperations.IsValidLineIdentity(sourceId))
            throw new ArgumentException("Invalid unfollow identity boundary.");
        if (contact is null) return new WebhookBusinessResult(WebhookBusinessStatus.Ignored, "UnfollowWithoutContact");
        WebhookRecordOperations.RequireLookup(contact, "parentcustomerid", "account", church);
        if (contact.GetAttributeValue<string>("new_lineid") != sourceId) throw new InvalidOperationException("The contact binding does not match.");
        WebhookRecordOperations.Update(sdk, contact, new Entity("contact", contact.Id) { ["new_line_status"] = new OptionSetValue(100000002) }, ct);
        return new WebhookBusinessResult(WebhookBusinessStatus.Applied, "UnfollowStatusUpdated", recordIds: new[] { contact.Id });
    }

}
