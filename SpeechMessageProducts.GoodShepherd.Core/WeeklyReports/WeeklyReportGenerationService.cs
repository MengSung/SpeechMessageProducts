/*
 * 檔案整體說明
 *
 * 目的與定位：P5-05 週報生成的同步 SDK adapter，將舊活動的名單、領袖及成員
 * 映射轉成經教會隔離驗證的週報／出席 Create 交易，時間與正式主鍵交純規則計算。
 * 呼叫端只借入平台 UserId 權限的 IOrganizationService，不提升權限、不保存服務。
 *
 * 主要功能與流程：一次讀 UTC，完整有界讀取合格名單；由正式領袖 contact 的
 * parentcustomerid 推導 account，驗所有領袖與成員同教會，讀 new_timezone 後
 * 求本週及下週星期日。UserLocal／DateOnly 儲存教會午夜所對應 UTC，查詢以
 * 當地星期日整日 UTC 半開區間，避免 SDK On 依執行使用者時區誤判。
 * 靜態 listmember 與動態 contact FetchXML 都讀完全部頁面；DTD、非 contact 根、
 * 截斷型 Fetch、重複正式 ID、超預算及後頁故障一律先拒絕，不發布部分候選。
 *
 * 使用邊界與維護：所有名單、會員及交易候選只存活於本次同步 Generate；最多
 * 128 名單、每查詢100頁／10000列、整輪20000個 Create。Live 每交易最多1000項；
 * Shadow 每交易與整輪 overlay 最多64項。全部讀取驗證完成後才逐週報提交，
 * 每週報及其出席共一筆 ExecuteTransaction，不宣稱所有名單全域原子。
 * 正式預配主鍵由資料庫 Create 唯一約束處理跨程序競爭，衝突原例外交外殼，
 * 不以應用鎖或查後建立當唯一性保證。恰一筆既有週報不補列，超過一筆拒絕。
 * 與舊版差異：直接 Create ownerid（保留 team）取代 Create→Assign；不寫快照
 * 缺少的四欄位；修正跨名單 owner／門徒殘留、日期時區及非原子寫入。
 * SDK 由平台擁有，不 Dispose；本檔無背景工作、timer、快取、訂閱或取消註冊。
 * SDK 故障原樣向外殼傳播；本地 XML reader 有 using owner，不解析外部資源。
 * 離線真活動交易模型驗證映射、回滾、重試與隔離，實機權限及效能另行驗收。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;

namespace SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;

/// <summary>
/// 無 SDK／會員狀態的生成 adapter；唯一長存參照是無請求狀態的借入時鐘。
/// 同一服務可並行使用不同平台 SDK，所有業務候選都屬呼叫堆疊。
/// </summary>
public sealed class WeeklyReportGenerationService
{
    /// <summary>metadata 明列的週報實體，不透過顯示名稱猜測表名。</summary>
    private const string ReportTable = "new_group_present_weekly_report";
    /// <summary>metadata 明列的出席實體，正式主鍵由純規則預配。</summary>
    private const string AttendanceTable = "new_present_record";
    /// <summary>單一來源完整集合的最大列數，超量拒絕而非回傳截斷結果。</summary>
    private const int MaximumRows = 10000;
    /// <summary>即使服務端重複宣稱仍有頁面，本輪也必須有限停止並拒絕。</summary>
    private const int MaximumPages = 100;
    /// <summary>整輪待提交 Create 上限，防止候選累積無界保留記憶體。</summary>
    private const int MaximumCreates = 20000;
    /// <summary>固定欄位名稱表，沒有任何會員或教會資料；不得加入快照缺欄位。</summary>
    private static readonly string[] LeaderFields =
    {
        "new_contact_family_leader_list", "new_contact_list_vice_family_leader",
        "new_contact_race_leager_list", "new_contact_co_race_leager_list", "new_contact_list_arealeader"
    };
    /// <summary>名單固定投影欄位，僅保存 metadata 名稱，避免每次查詢重新配置常數陣列。</summary>
    private static readonly string[] ListFields = LeaderFields.Concat(new[]
    {
        "listname", "purpose", "new_app_named", "statuscode", "new_happy_start_date",
        "new_happy_end_date", "type", "query", "new_group_place", "new_group_time"
    }).ToArray();
    /// <summary>只保存無 request 狀態的時鐘，owner 在外部，本服務不處置來源。</summary>
    private readonly IChurchClock clock;

    /// <summary>正式預設只建立無狀態 ChurchClock，不開啟 SDK 連線或保存執行使用者。</summary>
    public WeeklyReportGenerationService() : this(new ChurchClock()) { }

    /// <summary>借入可並行的純時間來源；供主機或固定時間測試使用，不允許保存教會／使用者狀態。</summary>
    /// <param name="clock">由呼叫端擁有的時鐘，最長保留至本 adapter 被釋放。</param>
    public WeeklyReportGenerationService(IChurchClock clock) => this.clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// 先讀取及驗證整輪候選，再按每週報原子提交；任何準備故障均零新增。
    /// 交易執行中後續週報故障時，先前已提交週報仍保留，重試由既有週報判斷跳過。
    /// 同步 SDK 不支援此入口主動中斷，平台負責執行期限；不得另建逾時背景 Task。
    /// </summary>
    /// <param name="service">本次平台 UserId 權限的借用 SDK，由平台擁有及排空。</param>
    /// <param name="organizationId">平台正式組織識別，只供文字範圍，不替代 SDK 授權。</param>
    /// <param name="isShadow">是否使用外殼的 Shadow recorder，決定較小的純 Create 預算。</param>
    public void Generate(IOrganizationService service, Guid organizationId, bool isShadow)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(service);
#else
        if (service is null) throw new ArgumentNullException(nameof(service));
#endif
        if (organizationId == Guid.Empty) throw new ArgumentException("An organization identity is required.", nameof(organizationId));
        var now = clock.UtcNow.UtcDateTime;
        var textCatalog = new GoodShepherdTextCatalog(new EmptyOverrides());
        var fixedTexts = FixedTexts();
        var lists = ListQuery(fixedTexts.GetText("zh-TW", new TextKey("WeeklyReport.HappinessPurpose")));
        var sources = ReadPages(service, lists, 128);
        var plans = new List<ExecuteTransactionRequest>();
        var totalCreates = 0;
        foreach (var source in sources)
        {
            if (!Eligible(source, fixedTexts)) continue;
            // 教會只能由正式小組長鏈建立；其他領袖只驗一致，不可補猜缺失的來源。
            if (Reference(source, LeaderFields[0], "contact") is null) throw Invalid("List group leader is missing.");
            var leaders = new Dictionary<string, Entity>(StringComparer.Ordinal);
            Guid church = Guid.Empty;
            foreach (var field in LeaderFields)
            {
                var reference = Reference(source, field, "contact");
                if (reference is null) continue;
                var person = Read(service, "contact", reference.Id,
                    new ColumnSet("parentcustomerid", "ownerid", "new_deciple_group_list_contact"));
                var parent = Reference(person, "parentcustomerid", "account") ?? throw Invalid("Leader church is missing.");
                if (church != Guid.Empty && parent.Id != church) throw Invalid("Leader church conflict.");
                church = parent.Id;
                leaders.Add(field, person);
            }
            if (church == Guid.Empty) throw Invalid("List church cannot be established.");
            var account = Read(service, "account", church, new ColumnSet("new_timezone", "new_defaultlanguage"));
            var zone = Text(account, "new_timezone", 128);
            if (string.IsNullOrWhiteSpace(zone)) throw Invalid("Church time zone is missing.");
            var cultureName = Text(account, "new_defaultlanguage", 128);
            if (string.IsNullOrEmpty(cultureName)) cultureName = "zh-TW";
            var textContext = new TextContext(church, cultureName, cultureName,
                organizationId.ToString("D", CultureInfo.InvariantCulture), "GoodShepherd");
            var unfilled = textCatalog.GetText(textContext, new TextKey("WeeklyReport.Unfilled"));
            var culture = CultureInfo.GetCultureInfo(textContext.FormatCulture);
            leaders.TryGetValue(LeaderFields[0], out var groupLeader);
            var owner = Owner(groupLeader);
            var disciple = groupLeader is null ? null : Reference(groupLeader, "new_deciple_group_list_contact", "list");
            // 全部成員（包括不建立出席的停用會員）也必須驗教會；不能先忽略停用資料來隱藏跨界。
            var members = Members(service, source, church);
            foreach (var sunday in WeeklyReportGenerationRule.Weeks(now, zone))
            {
                var utc = clock.ToUtc(new LocalDateTimeOfChurch(sunday, zone));
                var end = clock.ToUtc(new LocalDateTimeOfChurch(sunday.AddDays(1), zone));
                var existing = new QueryExpression(ReportTable)
                {
                    ColumnSet = new ColumnSet("new_list_group_present_weekly_report", "new_sunday_date", "statecode")
                };
                existing.Criteria.AddCondition("new_list_group_present_weekly_report", ConditionOperator.Equal, source.Id);
                existing.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                existing.Criteria.AddCondition("new_sunday_date", ConditionOperator.GreaterEqual, utc);
                existing.Criteria.AddCondition("new_sunday_date", ConditionOperator.LessThan, end);
                var found = ReadPages(service, existing, MaximumRows);
                if (found.Count > 1) throw Invalid("Duplicate existing weekly report.");
                if (found.Count == 1) continue;
                var capacity = isShadow ? 64 : 1000;
                if (members.Count + 1 > capacity) throw Invalid("Weekly report transaction capacity exceeded.");
                var reportId = WeeklyReportGenerationRule.ReportId(church, source.Id, sunday);
                var report = Report(source, leaders, reportId, utc, sunday, culture, owner, disciple);
                var transaction = new ExecuteTransactionRequest
                {
                    Requests = new OrganizationRequestCollection(), ReturnResponses = false
                };
                transaction.Requests.Add(new CreateRequest { Target = report });
                foreach (var member in members)
                    transaction.Requests.Add(new CreateRequest
                    {
                        Target = Attendance(source, leaders, member, reportId, utc, unfilled, owner)
                    });
                totalCreates = checked(totalCreates + transaction.Requests.Count);
                if (totalCreates > (isShadow ? 64 : MaximumCreates)) throw Invalid("Weekly report operation capacity exceeded.");
                plans.Add(transaction);
            }
        }
        // 唯一發布點：上方任一名單、後頁、時區、身份或容量驗證失敗都還未呼叫 Execute。
        // 各交易由平台資料庫負責全成功或回滾；不捕捉主鍵衝突成為假成功。
        foreach (var plan in plans) service.Execute(plan);
    }

    /// <summary>建立固定產品分類文字表；不以共用 SystemTextTable 預設表冒充產品資源。</summary>
    private static SystemTextTable FixedTexts() => SystemTextTable.FromRows(
        Resources.GoodShepherdTextData.Cultures, Resources.GoodShepherdTextData.Rows);

    /// <summary>只投影 metadata 存在欄位，固定 app／status／幸福日期及目的排除條件。</summary>
    private static QueryExpression ListQuery(string happiness)
    {
        var query = new QueryExpression("list")
        {
            ColumnSet = new ColumnSet(ListFields)
        };
        query.Criteria.AddCondition("new_app_named", ConditionOperator.Equal, true);
        query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition("new_happy_start_date", ConditionOperator.Null);
        query.Criteria.AddCondition("new_happy_end_date", ConditionOperator.Null);
        query.Criteria.AddCondition("purpose", ConditionOperator.NotLike, "%" + happiness + "%");
        query.AddOrder("listname", OrderType.Descending);
        query.AddOrder("listid", OrderType.Ascending);
        return query;
    }

    /// <summary>讀回值再次核對資格；固定繁中分類詞不能因教會語系改變原資料選取規則。</summary>
    private static bool Eligible(Entity source, SystemTextTable texts)
    {
        var purpose = Text(source, "purpose", 512);
        return source.GetAttributeValue<bool>("new_app_named") &&
            source.GetAttributeValue<OptionSetValue>("statuscode")?.Value == 0 &&
            (!source.Contains("new_happy_start_date") || source["new_happy_start_date"] is null) &&
            (!source.Contains("new_happy_end_date") || source["new_happy_end_date"] is null) &&
            purpose.IndexOf(texts.GetText("zh-TW", new TextKey("WeeklyReport.HappinessPurpose")), StringComparison.Ordinal) < 0 &&
            Text(source, "listname", 300).IndexOf(texts.GetText("zh-TW", new TextKey("WeeklyReport.CareList")), StringComparison.Ordinal) < 0;
    }

    /// <summary>單張名單的正式成員候選，讀完所有頁、驗 ID 及教會後才選 statecode=0。</summary>
    private static List<Entity> Members(IOrganizationService service, Entity list, Guid church)
    {
        List<Entity> rows;
        var dynamic = list.GetAttributeValue<bool>("type");
        if (dynamic) rows = ReadFetch(service, Text(list, "query", 1048576));
        else
        {
            var query = new QueryExpression("listmember") { ColumnSet = new ColumnSet("entityid") };
            query.Criteria.AddCondition("listid", ConditionOperator.Equal, list.Id);
            query.AddOrder("createdon", OrderType.Ascending);
            query.AddOrder("listmemberid", OrderType.Ascending);
            rows = ReadPages(service, query, MaximumRows);
        }
        var identities = new HashSet<Guid>();
        var result = new List<Entity>();
        foreach (var row in rows)
        {
            var id = dynamic ? row.Id : MemberId(row);
            if (id == Guid.Empty || !identities.Add(id)) throw Invalid("Duplicate or empty member identity.");
            var contact = Read(service, "contact", id, new ColumnSet("parentcustomerid", "statecode", "mobilephone"));
            var parent = Reference(contact, "parentcustomerid", "account") ?? throw Invalid("Member church is missing.");
            if (parent.Id != church) throw Invalid("Member church conflict.");
            if (contact.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0) result.Add(contact);
        }
        return result;
    }

    /// <summary>CRM listmember.entityid 可能是 GUID 或 contact lookup；其它型別與錯表均拒絕。</summary>
    private static Guid MemberId(Entity row)
    {
        if (row.Attributes.TryGetValue("entityid", out var value))
        {
            if (value is Guid id) return id;
            if (value is EntityReference reference && reference.LogicalName == "contact") return reference.Id;
        }
        throw Invalid("Invalid list membership identity.");
    }

    /// <summary>
    /// stored FetchXML 屬不可信查詢：禁止 DTD／外部 resolver、aggregate／distinct／top，
    /// 根表只能 contact；保留原 filter／link，將投影收斂為 contactid 並管理 count／page／cookie。
    /// 使用 local XML 文件，不修改資料庫查詢；XML reader 與字串 reader 於本次解析確定釋放。
    /// </summary>
    private static List<Entity> ReadFetch(IOrganizationService service, string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) throw Invalid("Dynamic list query is missing.");
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576,
            MaxCharactersFromEntities = 0
        });
        var document = XDocument.Load(reader);
        var root = document.Root;
        if (root is null || root.Name != "fetch" || root.Elements("entity").Count() != 1 ||
            root.Elements().Count() != 1 || root.Attribute("top") is not null ||
            EnabledFetchOption(root, "aggregate") || EnabledFetchOption(root, "distinct"))
            throw Invalid("Unsupported dynamic list query.");
        var entity = root.Element("entity")!;
        if ((string?)entity.Attribute("name") != "contact")
            throw Invalid("Dynamic list must project contacts.");
        // CRM stored Fetch 常帶 distinct=false／all-attributes；false 不改集合語意，
        // 而來源欄位投影不是成員條件，移除可避免載入不必要個資。link 的 filter／order
        // 保持原樣；只返回 root contactid，再以當次 UserId 讀取必要欄位及驗教會。
        foreach (var projection in entity.Descendants().Where(e => e.Name == "attribute" || e.Name == "all-attributes").ToArray())
            projection.Remove();
        entity.AddFirst(new XElement("attribute", new XAttribute("name", "contactid")));
        if (!entity.Elements("order").Any(a => (string?)a.Attribute("attribute") == "contactid"))
            entity.Add(new XElement("order", new XAttribute("attribute", "contactid")));
        var result = new List<Entity>();
        var seen = new HashSet<Guid>();
        string? cookie = null;
        for (var page = 1; page <= MaximumPages; page++)
        {
            root.SetAttributeValue("count", 5000);
            root.SetAttributeValue("page", page);
            root.SetAttributeValue("paging-cookie", cookie);
            var response = service.RetrieveMultiple(new FetchExpression(document.ToString(SaveOptions.DisableFormatting)))
                ?? throw Invalid("Missing dynamic list page.");
            Append(result, seen, response, "contact", MaximumRows);
            if (!response.MoreRecords) return result;
            cookie = Cookie(response.PagingCookie);
        }
        throw Invalid("Dynamic list paging capacity exceeded.");
    }

    /// <summary>只接受明確的 Fetch 布林表示；true 改變集合／聚合語意，false 可保留，畸形值拒絕。</summary>
    private static bool EnabledFetchOption(XElement root, string name)
    {
        var value = (string?)root.Attribute(name);
        if (value is null || value == "false" || value == "0") return false;
        if (value == "true" || value == "1") return true;
        throw Invalid("Invalid dynamic list option.");
    }

    /// <summary>逐頁讀完整來源、驗正式 ID 不重複；無 cookie 仍以 page 繼續至有界上限。</summary>
    private static List<Entity> ReadPages(IOrganizationService service, QueryExpression query, int maximum)
    {
        var result = new List<Entity>();
        var seen = new HashSet<Guid>();
        query.PageInfo = new PagingInfo { Count = Math.Min(5000, maximum), PageNumber = 1 };
        for (var page = 1; page <= MaximumPages; page++)
        {
            query.PageInfo.PageNumber = page;
            var response = service.RetrieveMultiple(query) ?? throw Invalid("Missing query page.");
            Append(result, seen, response, query.EntityName, maximum);
            if (!response.MoreRecords) return result;
            query.PageInfo.PagingCookie = Cookie(response.PagingCookie);
        }
        throw Invalid("Query paging capacity exceeded.");
    }

    /// <summary>每頁先核對表名、身份及容量；任何衝突不回傳部分資料，也不按姓名合併。</summary>
    private static void Append(List<Entity> target, HashSet<Guid> seen, EntityCollection page, string table, int maximum)
    {
        if (page.Entities.Count > 5000 || page.Entities.Count > maximum - target.Count ||
            page.MoreRecords && page.Entities.Count == 0) throw Invalid("Invalid or excessive query page.");
        foreach (var row in page.Entities)
        {
            if (row is null || row.LogicalName != table || row.Id == Guid.Empty || !seen.Add(row.Id))
                throw Invalid("Invalid or repeated query identity.");
            target.Add(row);
        }
    }

    /// <summary>cookie 最多64Ki字元且僅保留至下次查詢；超限拒絕，無 cookie 可按頁碼繼續。</summary>
    private static string? Cookie(string? value) => value?.Length > 65536
        ? throw Invalid("Paging cookie capacity exceeded.") : value;

    /// <summary>單筆讀取也驗證返回正式表與 ID，防止錯誤 adapter 回覆跨記錄資料。</summary>
    private static Entity Read(IOrganizationService service, string table, Guid id, ColumnSet columns)
    {
        var row = service.Retrieve(table, id, columns);
        if (row is null || row.LogicalName != table || row.Id != id) throw Invalid("Unexpected retrieved identity.");
        return row;
    }

    /// <summary>lookup 只接受明確正式目標；缺值可省略，錯型別／錯目標／空 ID 一律拒絕。</summary>
    private static EntityReference? Reference(Entity entity, string field, string table)
    {
        if (!entity.Attributes.TryGetValue(field, out var value) || value is null) return null;
        if (value is not EntityReference reference || reference.LogicalName != table || reference.Id == Guid.Empty)
            throw Invalid("Invalid reference scope.");
        return new EntityReference(table, reference.Id);
    }

    /// <summary>每張名單重新解析組長 owner，保留 systemuser／team，不沿用前張名單狀態。</summary>
    private static EntityReference? Owner(Entity? leader)
    {
        if (leader is null || !leader.Attributes.TryGetValue("ownerid", out var value) || value is null) return null;
        if (value is not EntityReference owner || owner.Id == Guid.Empty ||
            owner.LogicalName != "systemuser" && owner.LogicalName != "team") throw Invalid("Invalid owner.");
        return new EntityReference(owner.LogicalName, owner.Id);
    }

    /// <summary>以 metadata 字串上限約束輸入與保留量；缺值忠實映射空字串，錯型別不猜轉換。</summary>
    private static string Text(Entity entity, string field, int maximum)
    {
        if (!entity.Attributes.TryGetValue(field, out var value) || value is null) return string.Empty;
        if (value is not string text || text.Length > maximum) throw Invalid("Invalid or excessive field text.");
        return text;
    }

    /// <summary>週報候選忠實映射可用欄位，日期／owner 依核定差異處理，不寫缺 metadata 的家長欄。</summary>
    private static Entity Report(Entity list, Dictionary<string, Entity> leaders, Guid id, DateTime utc,
        DateTime sunday, CultureInfo culture, EntityReference? owner, EntityReference? disciple)
    {
        var name = Text(list, "listname", 300) + "-" + sunday.ToString("d", culture);
        if (name.Length > 100) throw Invalid("Weekly report name capacity exceeded.");
        var row = new Entity(ReportTable, id)
        {
            ["new_name"] = name, ["new_list_group_present_weekly_report"] = list.ToEntityReference(),
            ["new_sunday_date"] = utc, ["new_group_date"] = utc,
            ["new_group_place"] = Text(list, "new_group_place", 100),
            ["new_group_time"] = Text(list, "new_group_time", 100),
            ["new_sunday_present_number"] = 0, ["new_small_group_number"] = 0,
            ["new_sunday_present_rate"] = 0d, ["new_small_group_rate"] = 0d
        };
        MapLeaders(row, leaders, "new_groupleader_group_present_weekly_", "new_group_head_group_present_weekly_r", "new_contact_arealeader_weekly_report");
        if (owner is not null) row["ownerid"] = new EntityReference(owner.LogicalName, owner.Id);
        if (disciple is not null) row["new_deciple_list_group_present_weekly"] = new EntityReference("list", disciple.Id);
        return row;
    }

    /// <summary>每個正式會員獨立出席 ID，保留舊 hpone 拼字與零值型別，不以姓名去重。</summary>
    private static Entity Attendance(Entity list, Dictionary<string, Entity> leaders, Entity member,
        Guid report, DateTime utc, string unfilled, EntityReference? owner)
    {
        if (unfilled.Length > 200) throw Invalid("Attendance name capacity exceeded.");
        var row = new Entity(AttendanceTable, WeeklyReportGenerationRule.AttendanceId(report, member.Id))
        {
            ["new_name"] = unfilled, ["new_contact_new_present_record"] = member.ToEntityReference(),
            ["new_group_present_weekly_report_prese"] = new EntityReference(ReportTable, report),
            ["new_list_new_present_record"] = list.ToEntityReference(),
            ["new_sunday_date"] = utc, ["new_group_date"] = utc,
            ["new_group_place"] = Text(list, "new_group_place", 100),
            ["new_group_time"] = Text(list, "new_group_time", 100),
            ["new_sunday_present_this_week"] = 0, ["new_group_present_this_week"] = 0,
            ["new_sunday_rate"] = 0d, ["new_small_group_rate"] = 0d,
            ["new_cell_hpone"] = Text(member, "mobilephone", 50)
        };
        MapLeaders(row, leaders, "new_groupleader_present_record", "new_race_leader_present_record", "new_contact_arealeader_present_record");
        if (owner is not null) row["ownerid"] = new EntityReference(owner.LogicalName, owner.Id);
        return row;
    }

    /// <summary>只映射已驗證同教會且 metadata 存在的組長、上代與區牧；副／共同上代只驗隔離。</summary>
    private static void MapLeaders(Entity row, Dictionary<string, Entity> leaders, string group, string race, string area)
    {
        if (leaders.TryGetValue(LeaderFields[0], out var leader)) row[group] = leader.ToEntityReference();
        if (leaders.TryGetValue(LeaderFields[2], out var ancestor)) row[race] = ancestor.ToEntityReference();
        if (leaders.TryGetValue(LeaderFields[4], out var pastor)) row[area] = pastor.ToEntityReference();
    }

    /// <summary>固定不含輸入值的診斷，讓外殼保留真實例外與事件，避免帶出個資或 Fetch 本文。</summary>
    private static InvalidOperationException Invalid(string reason) => new(reason);

    /// <summary>本 adapter 僅使用固定產品資源，沒有覆寫快取或跨教會來源；每次 snapshot 只屬當次 context。</summary>
    private sealed class EmptyOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>回傳明確空覆寫；不保存 context、不借用連線、不建立背景讀取。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}
