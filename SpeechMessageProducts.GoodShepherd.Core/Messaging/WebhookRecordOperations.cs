/*
檔案整體說明
目的與定位：P3-06 事件專用資料邊界，重用 EntityQueryService 驗證實體與整頁正式 ID，不另造通用查詢引擎。
主要功能與流程：讀取並複核 actor、執行有界完整頁、對已讀記錄使用 rowversion 條件更新。新增資料庫主鍵以完整組織／教會／業務鍵推導；Contact 與附件以正式 ID 原子 Upsert，回應身分不符即拒絕，不以程序鎖冒充唯一性。
使用邊界與維護：每次方法只借入 SDK，查詢 wrapper 以 using 清參照；同步呼叫前後檢查取消，無 Task.Run、隱藏重試、Session 或共用 Entity。Host 擁有連線 deadline 與 drain，原始 SDK Exception 直接上傳共用告警。資料最多一頁 5000 筆，MoreRecords 拒絕發布，所有集合只活於目前事件。
*/
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.EntityOperations;

namespace SpeechMessageProducts.GoodShepherd.Core.Messaging;

/// <summary>事件專用 SDK 接縫，沒有連線、快取或使用者狀態的所有權。</summary>
public static class WebhookRecordOperations
{
    /// <summary>
    /// 驗證 LINE actor、群組或聊天室識別的固定格式。呼叫端仍須另外完成頻道、
    /// 組織、教會及 Contact 綁定授權；格式正確本身不代表呼叫者有權存取資料。
    /// 此純值檢查同時供 net462 Core 與 net10 Host 使用，不保存輸入或建立共用狀態。
    /// </summary>
    public static bool IsValidLineIdentity(string? value)
    {
        if (value is not { Length: 33 } || value[0] is not ('U' or 'C' or 'R'))
            return false;

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if ((character < '0' || character > '9') &&
                (character < 'a' || character > 'f') &&
                (character < 'A' || character > 'F'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 重新讀本人綁定、所屬 account、顯示名稱與角色；呼叫者建立 actor 值物件不能取代
    /// 這個核對。customertypecode 由同工聚會角色白名單消費，不能由訊息本文或路由推導。
    /// </summary>
    public static Entity ReadActor(IOrganizationService sdk, WebhookActor actor, CancellationToken ct)
    {
        var contact = Read(sdk, "contact", actor.ContactId, new ColumnSet(
            "new_lineid",
            "parentcustomerid",
            "new_cell_list_contact",
            "new_church_jobtitle",
            "fullname",
            "customertypecode"), ct);
        RequireLookup(contact, "parentcustomerid", "account", actor.ChurchId);
        if (!string.Equals(contact.GetAttributeValue<string>("new_lineid"), actor.UserId, StringComparison.Ordinal))
            throw new InvalidOperationException("The actor binding changed.");
        return contact;
    }
    /// <summary>重用既有正式 ID 驗證並在同步 I/O 前後處理取消；不讀 AllColumns。</summary>
    public static Entity Read(IOrganizationService sdk, string table, Guid id, ColumnSet columns, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var query = new EntityQueryService(organizationService: sdk);
        var result = query.RetrieveEntity(table, id, columns);
        ct.ThrowIfCancellationRequested(); return result;
    }
    /// <summary>整頁經既有引擎驗證；超頁拒絕，不把前 5000 筆當完整業務結果。</summary>
    public static EntityCollection Query(IOrganizationService sdk, QueryExpression expression, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var query = new EntityQueryService(organizationService: sdk);
        var result = query.RetrieveMultiple(expression);
        ct.ThrowIfCancellationRequested();
        if (result.MoreRecords) throw new InvalidOperationException("The event collection exceeds its complete-page budget.");
        return result;
    }
    /// <summary>只更新明列欄位；缺版本或併行衝突封閉失敗，禁止重試 append 而產生重複文字。</summary>
    public static void Update(IOrganizationService sdk, Entity original, Entity patch, CancellationToken ct)
    {
        if (patch.Id != original.Id || patch.LogicalName != original.LogicalName || string.IsNullOrWhiteSpace(original.RowVersion))
            throw new InvalidOperationException("An exact record and row version are required.");
        patch.RowVersion = original.RowVersion;
        ct.ThrowIfCancellationRequested();
        sdk.Execute(new UpdateRequest { Target = patch, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches });
        ct.ThrowIfCancellationRequested();
    }
    /// <summary>
    /// 以伺服器推導的正式 ID 做原子 Create-or-Update；重送只更新同一筆候選，
    /// 不使用先查再建的程序鎖，也不把顯示名稱或暫存鍵當成唯一性。
    /// </summary>
    public static Guid Upsert(IOrganizationService sdk, Entity candidate, Guid expectedId, CancellationToken ct)
    {
        if (candidate is null || candidate.Id != expectedId || expectedId == Guid.Empty ||
            string.IsNullOrWhiteSpace(candidate.LogicalName))
            throw new InvalidOperationException("An exact upsert identity is required.");
        ct.ThrowIfCancellationRequested();
        var response = sdk.Execute(new UpsertRequest { Target = candidate }) as UpsertResponse;
        ct.ThrowIfCancellationRequested();
        if (response?.Target is not { } target || target.LogicalName != candidate.LogicalName || target.Id != expectedId)
            throw new InvalidOperationException("The upsert response identity does not match.");
        return expectedId;
    }
    /// <summary>只有正式型別及 ID 相符才接受 lookup；Name 永遠不是授權或去重依據。</summary>
    public static void RequireLookup(Entity row, string field, string table, Guid id)
    {
        var value = row.GetAttributeValue<EntityReference>(field);
        if (value is null || value.Id != id || value.LogicalName != table || id == Guid.Empty)
            throw new InvalidOperationException("The record is outside the authorized scope.");
    }
    /// <summary>由伺服器完整範圍與業務鍵產生資料庫主鍵；只用於新增記錄，不拿暫存鍵補足查詢結果的缺 ID。</summary>
    public static Guid BusinessId(Guid organization, Guid church, string kind, string key)
    {
        if (organization == Guid.Empty || church == Guid.Empty || string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(key) || key.Length > 256)
            throw new ArgumentException("A complete bounded business identity is required.");
        var input = Encoding.UTF8.GetBytes(organization.ToString("N", CultureInfo.InvariantCulture) + "/" + church.ToString("N", CultureInfo.InvariantCulture) + "/" + kind + "/" + key);
#if NETFRAMEWORK
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(input);
#else
        var bytes = SHA256.HashData(input);
#endif
        var id = new byte[16]; Array.Copy(bytes, id, id.Length); return new Guid(id);
    }
}
