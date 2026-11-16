/*
 * 檔案整體說明
 *
 * 目的與定位：承接退役 NewPersonCareWorkflow/PresentRecordClassLibrary 的可達
 * 關懷週報與單一會員出席建立，不搬大型 ToolUtility。共用 P5-05 週報及出席
 * 正式主鍵規則與產品文字；一般週報生成器排除關懷名單，故本檔只補其關懷入口。
 * 主要功能與流程：正式 contact.parentcustomerid 決定教會，主要小組及全部領袖
 * lookup 均驗證同教會；從教會時區求今天及下一個星期日（今天週日則今天）。
 * 舊八個偏移都是七的倍數，主日卻始終用當下日期計算，因此只產生同一週報與
 * 一筆出席，期限為第一輪今天；本檔保留這個結果而不擅自新增八週資料。
 * 缺週報時連同出席一次 ExecuteTransaction；已有週報則只補缺少的出席。
 * 使用邊界與維護：資料庫 Create 主鍵唯一約束處理跨程序競爭，衝突及未知故障
 * 原樣傳外殼，不把失敗當成功；重試查回既有資料後不覆寫內容。歷史多筆衝突、
 * 錯誤正式 ID、lookup、日期或分頁截斷均 fail-closed。最多兩筆候選及單筆交易，
 * 沒有 cache、Session、背景工作、timer、token、LINE client 或取消註冊。
 * SDK 由外殼借入及排空，本服務不 Dispose、不持有它；唯一長存參照是借用時鐘。
 * 所有 Entity 只存在本次同步呼叫，平台負責 SDK 期限，不建立逃逸的逾時 Task。
 * 日期依 metadata UserLocal/DateOnly 以教會午夜 UTC 寫入；缺 metadata 的家長欄位
 * 不猜測新增 schema。離線測試驗映射、回滾、原例外與重試，實機驗收另行處理。
 */
using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;

namespace SpeechMessageProducts.GoodShepherd.Core.Care;

/// <summary>無會員共用狀態的同步關懷建立服務；SDK 與候選完全屬單次執行。</summary>
public sealed class NewPersonPresentRecordService
{
    /// <summary>已部署正式週報表名，不以顯示名稱判斷身份。</summary>
    private const string ReportTable = "new_group_present_weekly_report";
    /// <summary>已部署正式出席表名。</summary>
    private const string AttendanceTable = "new_present_record";
    /// <summary>需驗證同教會的領袖欄位，內容只有固定 metadata 名稱。</summary>
    private static readonly string[] LeaderFields =
    {
        "new_contact_family_leader_list", "new_contact_race_leager_list", "new_contact_list_arealeader",
        "new_contact_list_vice_family_leader", "new_contact_co_race_leager_list"
    };
    /// <summary>借用純時鐘，不保存教會／會員／SDK，不處置外部時鐘 owner。</summary>
    private readonly IChurchClock clock;

    /// <summary>建立無背景資源的正式時鐘；不開啟連線。</summary>
    public NewPersonPresentRecordService() : this(new ChurchClock()) { }

    /// <summary>借入可並行時鐘以固定教會時間；不允許以時鐘承載會員或驗證狀態。</summary>
    /// <param name="clock">由呼叫端擁有，保存期限最長至本服務結束。</param>
    public NewPersonPresentRecordService(IChurchClock clock) =>
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// 從正式 contact 建立缺少的關懷週報及出席；一般名單或缺主要小組時無副作用。
    /// 所有讀取及欄位驗證在唯一交易前完成，失敗保留原例外交活動外殼統一處理。
    /// 同一事件重跑不修改既有出席；競爭 Create 衝突須由外殼回報後重試。
    /// </summary>
    /// <param name="service">平台 UserId 權限的借用 SDK，不提升權限。</param>
    /// <param name="contactId">平台主要 contact 正式主鍵，不是 caller 自報教會。</param>
    public void Execute(IOrganizationService service, Guid contactId)
    {
#if NETFRAMEWORK
        if (service is null) throw new ArgumentNullException(nameof(service));
#else
        ArgumentNullException.ThrowIfNull(service);
#endif
        if (contactId == Guid.Empty) throw new ArgumentException("A contact identity is required.", nameof(contactId));
        var contact = Read(service, "contact", contactId,
            new ColumnSet("parentcustomerid", "new_cell_list_contact", "mobilephone", "ownerid"));
        var church = Reference(contact, "parentcustomerid", "account")
            ?? throw Invalid("Contact church is missing.");
        var listReference = Reference(contact, "new_cell_list_contact", "list");
        if (listReference is null) return;
        var list = Read(service, "list", listReference.Id, new ColumnSet(
            "listname", "new_group_place", "new_group_time",
            LeaderFields[0], LeaderFields[1], LeaderFields[2], LeaderFields[3], LeaderFields[4]));
        var listName = Text(list, "listname", 300);
        if (listName.IndexOf(WebhookText.Neutral("WeeklyReport.CareList"), StringComparison.Ordinal) < 0) return;
        var leaders = new Entity?[LeaderFields.Length];
        for (var index = 0; index < LeaderFields.Length; index++)
        {
            var reference = Reference(list, LeaderFields[index], "contact");
            if (reference is null) continue;
            var leader = Read(service, "contact", reference.Id,
                new ColumnSet("parentcustomerid", "ownerid", "new_deciple_group_list_contact"));
            if (Reference(leader, "parentcustomerid", "account")?.Id != church.Id)
                throw Invalid("List leader church conflict.");
            leaders[index] = leader;
        }
        var groupLeader = leaders[0] ?? throw Invalid("A verified group leader is required.");
        var account = Read(service, "account", church.Id, new ColumnSet("new_timezone", "new_defaultlanguage"));
        var zone = Text(account, "new_timezone", 128);
        if (string.IsNullOrWhiteSpace(zone)) throw Invalid("Church time zone is missing.");
        var today = clock.ToChurchTime(clock.UtcNow.UtcDateTime, zone).Date;
        var sunday = today.AddDays((7 - (int)today.DayOfWeek) % 7);
        var utc = clock.ToUtc(new LocalDateTimeOfChurch(sunday, zone));
        var end = clock.ToUtc(new LocalDateTimeOfChurch(sunday.AddDays(1), zone));
        var reports = new QueryExpression(ReportTable)
        {
            ColumnSet = new ColumnSet("new_list_group_present_weekly_report", "new_sunday_date")
        };
        reports.Criteria.AddCondition("new_list_group_present_weekly_report", ConditionOperator.Equal, list.Id);
        reports.Criteria.AddCondition("new_sunday_date", ConditionOperator.GreaterEqual, utc);
        reports.Criteria.AddCondition("new_sunday_date", ConditionOperator.LessThan, end);
        var report = SingleOrNone(service, reports);
        if (report is not null)
        {
            RequireReference(report, "new_list_group_present_weekly_report", "list", list.Id);
            RequireDate(report, utc, end);
        }
        var reportId = report?.Id ?? WeeklyReportGenerationRule.ReportId(church.Id, list.Id, sunday);
        var records = new QueryExpression(AttendanceTable)
        {
            ColumnSet = new ColumnSet("new_contact_new_present_record", "new_group_present_weekly_report_prese",
                "new_list_new_present_record", "new_sunday_date")
        };
        records.Criteria.AddCondition("new_contact_new_present_record", ConditionOperator.Equal, contactId);
        records.Criteria.AddCondition("new_group_present_weekly_report_prese", ConditionOperator.Equal, reportId);
        var existing = SingleOrNone(service, records);
        if (existing is not null)
        {
            if (report is null) throw Invalid("Attendance references a missing weekly report.");
            RequireReference(existing, "new_contact_new_present_record", "contact", contactId);
            RequireReference(existing, "new_group_present_weekly_report_prese", ReportTable, reportId);
            RequireReference(existing, "new_list_new_present_record", "list", list.Id);
            RequireDate(existing, utc, end);
            return;
        }
        var place = Text(list, "new_group_place", 100);
        var time = Text(list, "new_group_time", 100);
        var attendance = new Entity(AttendanceTable, WeeklyReportGenerationRule.AttendanceId(reportId, contactId))
        {
            ["new_name"] = WebhookText.Neutral("WeeklyReport.Unfilled"),
            ["new_contact_new_present_record"] = contact.ToEntityReference(),
            ["new_group_present_weekly_report_prese"] = new EntityReference(ReportTable, reportId),
            ["new_list_new_present_record"] = list.ToEntityReference(),
            ["new_sunday_date"] = utc, ["new_group_date"] = utc,
            ["new_group_place"] = place, ["new_group_time"] = time,
            ["new_sunday_present_this_week"] = 0, ["new_group_present_this_week"] = 0,
            ["new_sunday_rate"] = 0d, ["new_small_group_rate"] = 0d,
            ["new_cell_hpone"] = Text(contact, "mobilephone", 100),
            ["new_care_expire_date"] = clock.ToUtc(new LocalDateTimeOfChurch(today, zone)),
            ["new_not_display"] = false
        };
        MapLeaders(attendance, leaders, "new_groupleader_present_record",
            "new_race_leader_present_record", "new_contact_arealeader_present_record");
        SetOwner(attendance, contact);
        var transaction = new ExecuteTransactionRequest
        {
            Requests = new OrganizationRequestCollection(), ReturnResponses = false
        };
        if (report is null)
        {
            var cultureName = Text(account, "new_defaultlanguage", 128);
            var culture = CultureInfo.GetCultureInfo(string.IsNullOrEmpty(cultureName) ? "zh-TW" : cultureName);
            var name = listName + "-" + sunday.ToString("d", culture);
            if (name.Length > 100) throw Invalid("Weekly report name capacity exceeded.");
            var candidate = new Entity(ReportTable, reportId)
            {
                ["new_name"] = name, ["new_list_group_present_weekly_report"] = list.ToEntityReference(),
                ["new_sunday_date"] = utc, ["new_group_date"] = utc,
                ["new_group_place"] = place, ["new_group_time"] = time,
                ["new_sunday_present_number"] = 0, ["new_small_group_number"] = 0,
                ["new_sunday_present_rate"] = 0d, ["new_small_group_rate"] = 0d
            };
            MapLeaders(candidate, leaders, "new_groupleader_group_present_weekly_",
                "new_group_head_group_present_weekly_r", "new_contact_arealeader_weekly_report");
            var disciple = Reference(groupLeader, "new_deciple_group_list_contact", "list");
            if (disciple is not null) candidate["new_deciple_list_group_present_weekly"] = disciple;
            SetOwner(candidate, groupLeader);
            transaction.Requests.Add(new CreateRequest { Target = candidate });
        }
        transaction.Requests.Add(new CreateRequest { Target = attendance });
        service.Execute(transaction);
    }

    /// <summary>只查最多兩筆以辨識衝突；任何更多頁面皆拒絕，不交付截斷資料。</summary>
    private static Entity? SingleOrNone(IOrganizationService service, QueryExpression query)
    {
        query.TopCount = 2;
        var rows = service.RetrieveMultiple(query) ?? throw Invalid("Missing query response.");
        if (rows.MoreRecords || rows.Entities.Count > 1) throw Invalid("Conflicting business records.");
        if (rows.Entities.Count == 0) return null;
        var row = rows.Entities[0];
        if (row is null || row.Id == Guid.Empty || row.LogicalName != query.EntityName)
            throw Invalid("Invalid query identity.");
        return row;
    }

    /// <summary>正式 Retrieve 必須同表同 ID；任何空結果均不可視為不存在後另建。</summary>
    private static Entity Read(IOrganizationService service, string table, Guid id, ColumnSet columns)
    {
        var row = service.Retrieve(table, id, columns);
        if (row is null || row.Id != id || row.LogicalName != table) throw Invalid("Unexpected retrieved identity.");
        return row;
    }

    /// <summary>lookup 缺值允許由上層決定；錯型別、錯表或空主鍵一律拒絕。</summary>
    private static EntityReference? Reference(Entity row, string field, string table)
    {
        if (!row.Attributes.TryGetValue(field, out var value) || value is null) return null;
        if (value is not EntityReference reference || reference.Id == Guid.Empty || reference.LogicalName != table)
            throw Invalid("Invalid reference scope.");
        return new EntityReference(table, reference.Id);
    }

    /// <summary>查回結果再驗作用域，SDK 查詢條件不能代替結果驗證。</summary>
    private static void RequireReference(Entity row, string field, string table, Guid id)
    {
        if (Reference(row, field, table)?.Id != id) throw Invalid("Stored record scope conflict.");
    }

    /// <summary>日期必須落於已驗證教會主日的 UTC 半開區間，不接受其他週資料。</summary>
    private static void RequireDate(Entity row, DateTime start, DateTime end)
    {
        if (!row.Attributes.TryGetValue("new_sunday_date", out var value) ||
            value is not DateTime date || date < start || date >= end)
            throw Invalid("Stored record date conflict.");
    }

    /// <summary>欄位只允許有界字串；缺值忠實留空，錯型別不猜測轉換。</summary>
    private static string Text(Entity row, string field, int maximum)
    {
        if (!row.Attributes.TryGetValue(field, out var value) || value is null) return string.Empty;
        if (value is not string text || text.Length > maximum) throw Invalid("Invalid or excessive text.");
        return text;
    }

    /// <summary>沿用週報生成器的三個已部署領袖欄位，不寫缺 metadata 的小家長欄位。</summary>
    private static void MapLeaders(Entity row, Entity?[] leaders, string group, string race, string area)
    {
        row[group] = leaders[0]!.ToEntityReference();
        if (leaders[1] is Entity ancestor) row[race] = ancestor.ToEntityReference();
        if (leaders[2] is Entity pastor) row[area] = pastor.ToEntityReference();
    }

    /// <summary>週報 owner 取組長、出席 owner 取會員；保留 team 並在 Create 時原子設定。</summary>
    private static void SetOwner(Entity target, Entity source)
    {
        if (!source.Attributes.TryGetValue("ownerid", out var value) || value is null) return;
        if (value is not EntityReference owner || owner.Id == Guid.Empty ||
            owner.LogicalName != "systemuser" && owner.LogicalName != "team") throw Invalid("Invalid owner.");
        target["ownerid"] = new EntityReference(owner.LogicalName, owner.Id);
    }

    /// <summary>固定診斷不帶會員資料、原輸入或機密；SDK 例外不經本方法包裝。</summary>
    private static InvalidOperationException Invalid(string reason) => new(reason);
}
