/*
 * 檔案整體說明
 * 目的與定位：P5-07設備規則的SDK協調器，供活動借用當次使用者權限的服務；
 * 只依正式課程記錄的教會與會友關聯更新該會友new_equipment_status，不信任名稱授權。
 * 主要流程：驗來源列→會友歸屬，完整翻頁本人課程，逐列驗正式ID／歸屬及完成欄位，
 * 已完成才讀課程階名；完整候選交純規則驗重複ID，再單次Update。未知階名回零階，
 * SDK、翻頁或驗證失敗均在Update前拋出，不發布部分計算結果。
 * 使用邊界與維護：SDK只借用不Dispose；每次最多4096筆、每頁500筆、9頁且cookie
 * 最多8192字。課程值及ID集合只保留本次同步呼叫，無跨教會cache／static可變狀態、
 * 背景Task、timer或連線owner。平台控制SDK逾時／取消；本服務同步排空後才返回。
 * Shadow由外殼傳入recorder，查詢仍受平台權限，更新只記安全投影，不保留token。
 */
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Resources;

namespace SpeechMessageProducts.GoodShepherd.Core.Lessons;

/// <summary>無實例狀態的SDK協調；完整候選驗證成功後才發出一個會友更新。</summary>
public static class LessonEquipmentService
{
    /// <summary>固定編譯時產品文字表，不存教會覆寫或使用者資料。</summary>
    private static readonly SystemTextTable Texts = SystemTextTable.FromRows(GoodShepherdTextData.Cultures, GoodShepherdTextData.Rows);

    /// <summary>依正式來源教會驗證本人及所有課程，完整判斷最高完成階後更新會友。</summary>
    /// <param name="sdk">當次平台權限服務；借用到同步呼叫返回。</param>
    /// <param name="recordId">平台主要課程記錄ID，不接受顯示名稱。</param>
    /// <exception cref="InvalidOperationException">錯配、欄位型別錯誤、身分重複或容量超限。</exception>
    public static void Update(IOrganizationService sdk, Guid recordId)
    {
#if NETFRAMEWORK
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
#else
        ArgumentNullException.ThrowIfNull(sdk);
#endif
        var source = Read(sdk, "new_stor_lessons", recordId, "new_contact_new_stor_lessons", "new_account_new_stor_lessons");
        var contactId = Lookup(source, "new_contact_new_stor_lessons", "contact");
        var churchId = Lookup(source, "new_account_new_stor_lessons", "account");
        var contact = Read(sdk, "contact", contactId, "parentcustomerid");
        if (Lookup(contact, "parentcustomerid", "account") != churchId) throw Invalid();
        var candidates = new List<LessonCompletion>();
        var identities = new HashSet<Guid>();
        var query = new QueryExpression("new_stor_lessons")
        {
            ColumnSet = new ColumnSet("new_contact_new_stor_lessons", "new_account_new_stor_lessons", "new_current_complete", "new_new_disciple_lessons_new_stor_les"),
            PageInfo = new PagingInfo { Count = 500, PageNumber = 1 }
        };
        query.Criteria.AddCondition("new_contact_new_stor_lessons", ConditionOperator.Equal, contactId);
        query.AddOrder("new_stor_lessonsid", OrderType.Ascending);
        for (;;)
        {
            var page = sdk.RetrieveMultiple(query) ?? throw Invalid();
            if (page.Entities.Count > 500 || page.PagingCookie?.Length > 8192) throw Invalid();
            foreach (var row in page.Entities)
            {
                if (row is null || row.LogicalName != "new_stor_lessons" || row.Id == Guid.Empty ||
                    candidates.Count >= 4096 || !identities.Add(row.Id) ||
                    Lookup(row, "new_contact_new_stor_lessons", "contact") != contactId ||
                    Lookup(row, "new_account_new_stor_lessons", "account") != churchId) throw Invalid();
                var complete = false;
                if (row.Attributes.TryGetValue("new_current_complete", out var value) && value is not null)
                    complete = value is bool flag ? flag : throw Invalid();
                string? stage = null;
                if (complete)
                {
                    var lesson = Read(sdk, "new_disciple_lessons", Lookup(row, "new_new_disciple_lessons_new_stor_les", "new_disciple_lessons"), "new_now_stage_name");
                    if (lesson.Attributes.TryGetValue("new_now_stage_name", out value) && value is not null)
                        stage = value is string text && text.Length <= 200 ? text : throw Invalid();
                }
                candidates.Add(new LessonCompletion(row.Id, complete, stage));
            }
            if (!page.MoreRecords) break;
            if (query.PageInfo.PageNumber >= 9 || page.Entities.Count == 0 || string.IsNullOrEmpty(page.PagingCookie) || page.PagingCookie == query.PageInfo.PagingCookie) throw Invalid();
            query.PageInfo.PageNumber++; query.PageInfo.PagingCookie = page.PagingCookie;
        }
        // 階名是舊資料庫值而非顯示翻譯，固定讀原zh-TW，不隨會友語系改變比較結果。
        var stages = Texts.GetText("zh-TW", new TextKey("Lessons.EquipmentStages")).Split('|');
        var result = LessonEquipmentRule.Evaluate(candidates, stages);
        sdk.Update(new Entity("contact", contactId) { ["new_equipment_status"] = result });
    }

    /// <summary>最小欄位讀取且驗指定實體與正式ID；不保留SDK或Entity至下一次呼叫。</summary>
    private static Entity Read(IOrganizationService sdk, string table, Guid id, params string[] columns)
    {
        if (id == Guid.Empty) throw Invalid();
        var row = sdk.Retrieve(table, id, new ColumnSet(columns));
        if (row is null || row.Id != id || row.LogicalName != table) throw Invalid();
        return row;
    }
    /// <summary>只接受metadata已確認的非空正式lookup，不用Name或alternate key補洞。</summary>
    private static Guid Lookup(Entity row, string field, string table) =>
        row.Attributes.TryGetValue(field, out var value) && value is EntityReference reference &&
        reference.LogicalName == table && reference.Id != Guid.Empty && reference.KeyAttributes.Count == 0
            ? reference.Id : throw Invalid();
    /// <summary>固定錯誤不回顯課程、個資或平台查詢本文；實際SDK原錯誤保持向外傳遞。</summary>
    private static InvalidOperationException Invalid() => new("Invalid lesson equipment scope or result.");
}
