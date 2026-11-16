/*
 * 檔案整體說明
 *
 * 目的與定位：把退役 NewPersonCare 的 AssignGroupClassLibrary 分支接到 Core 與
 * Dataverse 活動外殼。它只處理正式 contact 的主要小組 lookup、名單分類、分組日期
 * 與小組歷程；不搬入退役大型 ToolUtility，也不把 PresentRecord 的週報建立誤當成
 * 已完成的另一個規則。
 *
 * 主要流程：讀取 contact 的 new_cell_list_contact 與 new_small_group_log，驗證 lookup
 * 指向 list 後讀取 listname，再以教會授權時區取得當地日期，呼叫純值規則建立 patch。
 * 名單名稱含「關懷」寫 new_care_date，否則寫 new_assigne_group_date；歷程已有名單
 * 時不再追加。所有寫入以一次 contact Update 發布。
 *
 * 使用邊界與維護：IOrganizationService 由外殼借用，本服務不 Dispose、不快取、不保存
 * 跨執行 Entity；contact、list、patch 只存在同步堆疊。缺少主要小組、錯誤 logical name、
 * 空 ID 或查回身分不一致均 fail-closed。PresentRecord 分支仍由 NewPersonCareActivity
 * 保留分類證據並明確不猜測搬移；不建立 token、LINE client、背景工作或 timer。
 */
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;

namespace SpeechMessageProducts.GoodShepherd.Core.Care;

/// <summary>承接新朋友分組欄位的同步 Dataverse adapter。</summary>
public static class NewPersonCareAssignmentService
{
    /// <summary>依 contact 的正式主要小組更新分組日期與歷程。</summary>
    public static void Execute(IOrganizationService service, Guid contactId)
    {
#if NETFRAMEWORK
        if (service is null) throw new ArgumentNullException(nameof(service));
#else
        ArgumentNullException.ThrowIfNull(service);
#endif
        if (contactId == Guid.Empty) throw new ArgumentException("A contact identity is required.", nameof(contactId));
        var contact = service.Retrieve("contact", contactId, new ColumnSet("new_cell_list_contact", "new_small_group_log", "parentcustomerid"));
        if (contact is null || contact.Id != contactId) throw new InvalidOperationException("The contact identity was not returned.");
        var listReference = contact.GetAttributeValue<EntityReference>("new_cell_list_contact");
        if (listReference is null || listReference.Id == Guid.Empty || !string.Equals(listReference.LogicalName, "list", StringComparison.Ordinal))
            return;

        var list = service.Retrieve("list", listReference.Id, new ColumnSet("listname"));
        if (list is null || list.Id != listReference.Id) throw new InvalidOperationException("The list identity was not returned.");
        var listName = list.GetAttributeValue<string>("listname") ?? string.Empty;
        if (listName.Length == 0) return;

        var parent = contact.GetAttributeValue<EntityReference>("parentcustomerid");
        if (parent is null || parent.Id == Guid.Empty || !string.Equals(parent.LogicalName, "account", StringComparison.Ordinal))
            throw new InvalidOperationException("The contact must belong to an account church.");
        var church = service.Retrieve("account", parent.Id, new ColumnSet("new_timezone"));
        if (church is null || church.Id != parent.Id) throw new InvalidOperationException("The church identity was not returned.");
        var timeZone = church.GetAttributeValue<string>("new_timezone");
        if (string.IsNullOrWhiteSpace(timeZone)) timeZone = "Asia/Taipei";

        var update = NewPersonCareAssignmentRule.Calculate(listName,
            contact.GetAttributeValue<string>("new_small_group_log"), new ChurchClock().TodayInChurch(timeZone));
        var patch = new Entity("contact", contactId);
        if (update.CareDate.HasValue && contact.GetAttributeValue<DateTime>("new_care_date") != update.CareDate.Value)
            patch["new_care_date"] = update.CareDate.Value;
        if (update.AssignedGroupDate.HasValue && contact.GetAttributeValue<DateTime>("new_assigne_group_date") != update.AssignedGroupDate.Value)
            patch["new_assigne_group_date"] = update.AssignedGroupDate.Value;
        if (update.SmallGroupLog is not null && !string.Equals(contact.GetAttributeValue<string>("new_small_group_log"), update.SmallGroupLog, StringComparison.Ordinal))
            patch["new_small_group_log"] = update.SmallGroupLog;
        if (patch.Attributes.Count != 0) service.Update(patch);
    }
}
