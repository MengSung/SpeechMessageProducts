/*
 * 檔案整體說明
 *
 * 目的與定位：P5-05 週報統計同步 PostOperation 薄外殼，將平台 SDK 資料轉為
 * WeeklyReportStatisticsRule 純值，統計規則不接觸服務或可變 Entity。
 *
 * 主要功能與流程：驗證 Update、stage40/mode0、正式 Target 與 PreImage，重讀週報
 * saved flag 並按固定資源前綴 StartsWith 決定是否計算。來源教會沿週報名單與名單
 * 小組長的正式 account 歸屬解析；最多一萬筆出席列以最小欄位、有界翻頁完整讀取。
 * 每筆正式列、父週報、名單及會友教會均須一致，重複正式列拒絕；全部候選成功後
 * 才依 Core 結果先補分類、再寫週報最小 patch。N=0 不寫四統計欄位。
 *
 * 使用邊界與維護：相同 saved flag 仍依已儲存值計算，不讓前像 delta 改變舊啟動語意。
 * SDK 由平台借用，查詢、識別集合、contact 純值與候選只存活於單次同步 Execute；
 * 無 static 可變集合、Session、連線、Task、timer 或快取。翻頁最多一百頁、
 * 每頁一百筆，超界或中途失敗不發布任何寫入；Live 寫入交易由同步平台步驟承擔，
 * Shadow 寫入僅進外殼擁有的 recorder，finally 由基底排空。未知 SDK 例外原樣傳播，
 * 身分錯誤走固定業務拒絕。離線外殼測試不代表 Dataverse 授權或實機驗收。
 */
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;

namespace SpeechMessage.Dynamics.Plugins.WeeklyReports;

/// <summary>只負責單次統計取數、驗證及最小寫入；可重用實例不保留 SDK 或候選。</summary>
public sealed class WeeklyReportStatisticsPlugin : PluginShellBase
{
    /// <summary>平台建構入口；統計沿用 Depth&gt;2 返回，沒有建立背景或外部連線。</summary>
    /// <param name="unsecureConfig">明確 Shadow／Live 的註冊設定。</param>
    /// <param name="secureConfig">由基底驗證的註冊安全設定；統計本身不解密。</param>
    public WeeklyReportStatisticsPlugin(string unsecureConfig, string secureConfig)
        : base(unsecureConfig, secureConfig, new DepthPolicy(2)) { }

    /// <summary>完整取得並驗證當次出席候選後才依純規則發布；正常返回前同步排空所有 SDK 呼叫。</summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.MessageName != "Update" || input.PrimaryEntityName != WeeklyReportShellData.ReportTable) return;
        WeeklyReportShellData.ValidateInput(input, 0);
        var report = WeeklyReportShellData.Retrieve(services.Organization, WeeklyReportShellData.ReportTable,
            input.PrimaryEntityId, new ColumnSet("new_saved_flag", "statecode", "new_list_group_present_weekly_report"));
        var current = WeeklyReportShellData.Text(report, "new_saved_flag", 100);
        var prefix = WeeklyReportStatisticsRule.CalculationPrefix();
        if (current is null || !current.StartsWith(prefix, StringComparison.Ordinal)) return;
        var listId = WeeklyReportShellData.Lookup(report, "new_list_group_present_weekly_report", "list");
        var list = WeeklyReportShellData.Retrieve(services.Organization, "list", listId,
            new ColumnSet("new_contact_family_leader_list"));
        var church = WeeklyReportShellData.Church(services.Organization, list);
        var rows = ReadRows(services.Organization, report, listId, church);
        // PreImage 的存在與正式身分已驗證；啟動權威仍是當下 Retrieve 的命令。
        // Core 的 delta 入口傳 null prior，避免相同前像命令被誤判成不需計算。
        var result = WeeklyReportStatisticsRule.Evaluate(null, current,
            DeterministicGuid.Create("WeeklyReportStatistics", input.OrganizationId, input.PrimaryEntityId, input.CorrelationId),
            rows, prefix);
        foreach (var contact in result.ContactIdsNeedingClassification)
            services.Organization.Update(new Entity("contact", contact) { ["customertypecode"] = new OptionSetValue(100000000) });
        var patch = new Entity(WeeklyReportShellData.ReportTable, input.PrimaryEntityId) { ["new_saved_flag"] = result.NextSavedFlag };
        if (result.EligibleCount > 0)
        {
            patch["new_sunday_present_number"] = result.SundayCount!.Value;
            patch["new_small_group_number"] = result.GroupCount!.Value;
            patch["new_sunday_present_rate"] = result.SundayRate!.Value;
            patch["new_small_group_rate"] = result.GroupRate!.Value;
        }
        services.Organization.Update(patch);
    }

    /// <summary>
    /// 有界完整翻頁，先驗證所有根列及正式聯結才建立純值；不同 record 的同一 contact
    /// 仍分別計分母。只在本操作快取已核對的 contact 欄位，最多一萬個，返回即釋放。
    /// </summary>
    private static List<WeeklyReportAttendanceRow> ReadRows(IOrganizationService sdk, Entity report, Guid listId, Guid church)
    {
        var rows = new List<WeeklyReportAttendanceRow>();
        var seen = new HashSet<Guid>();
        var contacts = new Dictionary<Guid, Entity>();
        var cookies = new HashSet<string>(StringComparer.Ordinal);
        var query = new QueryExpression("new_present_record")
        {
            ColumnSet = new ColumnSet("statecode", "new_contact_new_present_record",
                "new_group_present_weekly_report_prese", "new_list_new_present_record",
                "new_sunday_present_this_week", "new_group_present_this_week"),
            PageInfo = new PagingInfo { Count = 100, PageNumber = 1 }
        };
        query.Criteria.AddCondition("new_group_present_weekly_report_prese", ConditionOperator.Equal, report.Id);
        // 舊查詢的 statecode=0 是父週報條件，不是根列或 contact 的狀態條件。
        var parent = query.AddLink(WeeklyReportShellData.ReportTable,
            "new_group_present_weekly_report_prese", "new_group_present_weekly_reportid");
        parent.LinkCriteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.AddOrder("createdon", OrderType.Descending);
        query.AddOrder("new_present_recordid", OrderType.Ascending);
        for (var page = 1; page <= 100; page++)
        {
            query.PageInfo.PageNumber = page;
            var collection = sdk.RetrieveMultiple(query) ?? throw WeeklyReportShellData.Invalid();
            if (collection.Entities.Count > query.PageInfo.Count) throw WeeklyReportShellData.Invalid();
            foreach (var row in collection.Entities)
            {
                WeeklyReportShellData.RequireIdentity(row, "new_present_record", row.Id);
                if (!seen.Add(row.Id) || seen.Count > WeeklyReportStatisticsRule.MaximumRows ||
                    WeeklyReportShellData.Lookup(row, "new_group_present_weekly_report_prese", WeeklyReportShellData.ReportTable) != report.Id ||
                    WeeklyReportShellData.Lookup(row, "new_list_new_present_record", "list") != listId)
                    throw WeeklyReportShellData.Invalid();
                // 舊碼只為啟用且有 contact 的根列取分類；停用或缺 contact 保持不計，
                // 但它們的正式 record ID 已參與衝突檢查，不可用過濾隱藏重複。
                if (row.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                    !row.Contains("new_contact_new_present_record") || row["new_contact_new_present_record"] is null) continue;
                var contactId = WeeklyReportShellData.Lookup(row, "new_contact_new_present_record", "contact");
                if (!contacts.TryGetValue(contactId, out var contact))
                {
                    contact = WeeklyReportShellData.Retrieve(sdk, "contact", contactId,
                        new ColumnSet("customertypecode", "parentcustomerid"));
                    if (WeeklyReportShellData.Lookup(contact, "parentcustomerid", "account") != church)
                        throw WeeklyReportShellData.Invalid();
                    contacts.Add(contactId, contact);
                }
                rows.Add(new WeeklyReportAttendanceRow(row.Id, contactId, true,
                    contact.GetAttributeValue<OptionSetValue>("customertypecode")?.Value,
                    OptionalInteger(row, "new_sunday_present_this_week"), OptionalInteger(row, "new_group_present_this_week")));
            }
            if (!collection.MoreRecords) return rows;
            if (collection.Entities.Count == 0 || string.IsNullOrEmpty(collection.PagingCookie) ||
                collection.PagingCookie.Length > 16384 || !cookies.Add(collection.PagingCookie))
                throw WeeklyReportShellData.Invalid();
            query.PageInfo.PagingCookie = collection.PagingCookie;
        }
        throw WeeklyReportShellData.Invalid();
    }

    /// <summary>缺整數保留 null 讓 Core 使用 -9999；錯誤型別拒絕，不用轉字串或零掩蓋資料問題。</summary>
    private static int? OptionalInteger(Entity row, string field) =>
        !row.Attributes.TryGetValue(field, out var value) || value is null ? null :
        value is int number ? number : throw WeeklyReportShellData.Invalid();
}

/// <summary>兩週報外殼共用的無狀態 SDK 邊界；不持有資料、服務或任何跨執行可變物件。</summary>
internal static class WeeklyReportShellData
{
    /// <summary>由現有 metadata 確認的週報實體名；主鍵不以呼叫端文字推測。</summary>
    internal const string ReportTable = "new_group_present_weekly_report";

    /// <summary>必要註冊形狀、正式 Target、PreImage 及事件邊界；缺圖不得以 Retrieve 代替。</summary>
    internal static void ValidateInput(PluginInput input, int mode)
    {
        if (input.Stage != 40 || input.Mode != mode || input.PrimaryEntityId == Guid.Empty ||
            input.OrganizationId == Guid.Empty || input.CorrelationId == Guid.Empty) throw Invalid();
        RequireIdentity(input.Target, ReportTable, input.PrimaryEntityId);
        RequireIdentity(input.PreImage, ReportTable, input.PrimaryEntityId);
    }

    /// <summary>只借用本次 SDK，以最小欄位讀指定正式列並驗回應識別，錯表或錯 ID fail closed。</summary>
    internal static Entity Retrieve(IOrganizationService sdk, string table, Guid id, ColumnSet columns)
    {
        var row = sdk.Retrieve(table, id, columns);
        RequireIdentity(row, table, id);
        return row;
    }

    /// <summary>檢查真正資料庫身分；同名、空 ID 或替代鍵不能取代指定列。</summary>
    internal static void RequireIdentity(Entity? row, string table, Guid id)
    {
        if (id == Guid.Empty || row is null || row.Id != id || row.LogicalName != table) throw Invalid();
    }

    /// <summary>lookup 必須為指定表的非空正式 ID；不接受顯示名稱、owner 或 caller 輸入補猜。</summary>
    internal static Guid Lookup(Entity row, string field, string table) =>
        row.Attributes.TryGetValue(field, out var value) && value is EntityReference reference &&
        reference.LogicalName == table && reference.Id != Guid.Empty && reference.KeyAttributes.Count == 0
            ? reference.Id : throw Invalid();

    /// <summary>有界字串保留 null 與空值的區別，錯誤型別不以 ToString 靜默接受。</summary>
    internal static string? Text(Entity row, string field, int maximum) =>
        !row.Attributes.TryGetValue(field, out var value) || value is null ? null :
        value is string text && text.Length <= maximum ? text : throw Invalid();

    /// <summary>唯一來源教會鏈：正式 list 小組長 contact 的 parentcustomerid 必須指向 account。</summary>
    internal static Guid Church(IOrganizationService sdk, Entity list)
    {
        var leader = Retrieve(sdk, "contact", Lookup(list, "new_contact_family_leader_list", "contact"),
            new ColumnSet("parentcustomerid"));
        return Lookup(leader, "parentcustomerid", "account");
    }

    /// <summary>固定業務拒絕交基底格式化事件 ID，不回顯記錄、回應或憑證。</summary>
    internal static BusinessRuleException Invalid() => new(new TextKey("Common.Plugin.InvalidConfiguration"));
}
