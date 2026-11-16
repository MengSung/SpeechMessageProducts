/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P5-10 新朋友關懷出席建立的可達舊行為，採離線合成 SDK，
 * 不執行舊 DLL、不連 Dataverse 或 LINE，也不讀取任何機密與實際會員資料。
 * 主要功能與流程：固定 UTC 及教會時區，驗證八次舊迴圈實際只建立同一週報與
 * 一筆會員出席、既有週報補列、重跑零寫入、同名不同 ID 保留及正式主鍵隔離。
 * 故障注入包含跨教會領袖、重複週報、交易失敗與未知例外；決定性斷言要求
 * 失敗前零發布、原例外不變，且修復後重試可成功。
 * 使用邊界與維護：每例獨占記憶體 SDK 與候選；替身在鎖內先驗交易再發布，
 * 模擬資料庫主鍵唯一性與整筆回滾，不能當成正式 D365 交易／效能驗收。
 * 沒有連線、背景工作、timer 或需 Dispose 的資源，記錄最長保留至案例結束。
 */
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Care;
using SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Care;

/// <summary>以有限記憶體資料庫保護關懷建立、隔離及失敗重試契約。</summary>
public sealed class NewPersonPresentRecordTests
{
    /// <summary>舊偏移迴圈收斂至同一主日；驗完整欄位與第二次執行不再建立。</summary>
    [Fact]
    public void Care_PresentRecord_MatchesReachableLegacy_AndRerunDoesNotWrite()
    {
        var sdk = new CareSdk();
        var service = new NewPersonPresentRecordService(new FixedClock());
        service.Execute(sdk, sdk.Contact.Id);
        var report = Assert.Single(sdk.Rows.Where(row => row.LogicalName == CareSdk.ReportTable));
        var attendance = Assert.Single(sdk.Rows.Where(row => row.LogicalName == CareSdk.AttendanceTable));
        Assert.Equal(WeeklyReportGenerationRule.ReportId(sdk.Church.Id, sdk.List.Id, new DateTime(2026, 11, 22)), report.Id);
        Assert.Equal(WeeklyReportGenerationRule.AttendanceId(report.Id, sdk.Contact.Id), attendance.Id);
        Assert.Equal(new DateTime(2026, 11, 21, 16, 0, 0, DateTimeKind.Utc), attendance["new_sunday_date"]);
        Assert.Equal(new DateTime(2026, 11, 15, 16, 0, 0, DateTimeKind.Utc), attendance["new_care_expire_date"]);
        Assert.Equal(false, attendance["new_not_display"]);
        Assert.Equal("未填", attendance["new_name"]);
        Assert.Equal(0, attendance["new_sunday_present_this_week"]);
        Assert.Equal(0d, attendance["new_small_group_rate"]);
        Assert.Equal("Synthetic", attendance["new_cell_hpone"]);
        Assert.Equal(sdk.Contact["ownerid"], attendance["ownerid"]);
        Assert.Equal(sdk.Leader["ownerid"], report["ownerid"]);
        Assert.Single(sdk.Transactions);
        service.Execute(sdk, sdk.Contact.Id);
        Assert.Single(sdk.Transactions);
    }

    /// <summary>既有隨機正式週報 ID 不重建；兩位同名 contact 各有獨立出席。</summary>
    [Fact]
    public void Care_PresentRecord_ExistingReport_SameNameContactsRemainSeparate()
    {
        var sdk = new CareSdk();
        var report = sdk.ExistingReport();
        sdk.Rows.Add(report);
        var service = new NewPersonPresentRecordService(new FixedClock());
        service.Execute(sdk, sdk.Contact.Id);
        var second = new Entity("contact", Guid.NewGuid());
        foreach (var attribute in sdk.Contact.Attributes) second[attribute.Key] = attribute.Value;
        sdk.Rows.Add(second);
        service.Execute(sdk, second.Id);
        Assert.Single(sdk.Rows.Where(row => row.LogicalName == CareSdk.ReportTable));
        var attendance = sdk.Rows.Where(row => row.LogicalName == CareSdk.AttendanceTable).ToArray();
        Assert.Equal(2, attendance.Length);
        Assert.NotEqual(attendance[0].Id, attendance[1].Id);
        Assert.All(attendance, row => Assert.Equal(report.Id,
            row.GetAttributeValue<EntityReference>("new_group_present_weekly_report_prese").Id));
    }

    /// <summary>正式 parentaccount 不一致的領袖不得觸發任何交易。</summary>
    [Fact]
    public void Care_PresentRecord_CrossChurchLeader_FailsBeforeWrite()
    {
        var sdk = new CareSdk();
        sdk.Leader["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => new NewPersonPresentRecordService(new FixedClock()).Execute(sdk, sdk.Contact.Id));
        Assert.Empty(sdk.Transactions);
    }

    /// <summary>同集合兩個不同正式週報 ID 仍屬衝突，不任取一筆也不覆蓋。</summary>
    [Fact]
    public void Care_PresentRecord_DuplicateWeeklyReports_FailsBeforeWrite()
    {
        var sdk = new CareSdk();
        sdk.Rows.Add(sdk.ExistingReport());
        sdk.Rows.Add(sdk.ExistingReport());
        Assert.Throws<InvalidOperationException>(() => new NewPersonPresentRecordService(new FixedClock()).Execute(sdk, sdk.Contact.Id));
        Assert.Empty(sdk.Transactions);
    }

    /// <summary>注入交易原例外，驗證沒有部分週報且恢復後能用同事件正式 ID 重試。</summary>
    [Fact]
    public void Care_PresentRecord_UnknownFailurePreserved_TransactionRollbackAndRetry()
    {
        var failure = new InvalidOperationException("Synthetic transport failure.");
        var sdk = new CareSdk { Failure = failure };
        var service = new NewPersonPresentRecordService(new FixedClock());
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => service.Execute(sdk, sdk.Contact.Id)));
        Assert.DoesNotContain(sdk.Rows, row => row.LogicalName == CareSdk.ReportTable || row.LogicalName == CareSdk.AttendanceTable);
        sdk.Failure = null;
        service.Execute(sdk, sdk.Contact.Id);
        Assert.Single(sdk.Rows.Where(row => row.LogicalName == CareSdk.ReportTable));
        Assert.Single(sdk.Rows.Where(row => row.LogicalName == CareSdk.AttendanceTable));
    }

    /// <summary>一般名單不產生關懷副作用；教會來源缺失仍須在任何寫入前拒絕。</summary>
    [Fact]
    public void Care_PresentRecord_NonCareList_NoWrite_AndMissingChurchRejected()
    {
        var sdk = new CareSdk();
        sdk.List["listname"] = "Regular group";
        var service = new NewPersonPresentRecordService(new FixedClock());
        service.Execute(sdk, sdk.Contact.Id);
        Assert.Empty(sdk.Transactions);
        sdk.Contact.Attributes.Remove("parentcustomerid");
        Assert.Throws<InvalidOperationException>(() => service.Execute(sdk, sdk.Contact.Id));
        Assert.Empty(sdk.Transactions);
    }

    /// <summary>兩筆正式出席具有相同 contact／週報時拒絕，不因主鍵不同而忽略衝突。</summary>
    [Fact]
    public void Care_PresentRecord_DuplicateAttendance_FailsWithoutFurtherWrite()
    {
        var sdk = new CareSdk();
        var service = new NewPersonPresentRecordService(new FixedClock());
        service.Execute(sdk, sdk.Contact.Id);
        var original = Assert.Single(sdk.Rows.Where(row => row.LogicalName == CareSdk.AttendanceTable));
        var duplicate = new Entity(CareSdk.AttendanceTable, Guid.NewGuid());
        foreach (var attribute in original.Attributes) duplicate[attribute.Key] = attribute.Value;
        sdk.Rows.Add(duplicate);
        Assert.Throws<InvalidOperationException>(() => service.Execute(sdk, sdk.Contact.Id));
        Assert.Single(sdk.Transactions);
    }

    /// <summary>
    /// 有界 barrier 強制兩個執行都查無出席才競爭建立；資料庫主鍵只能接受一筆完整
    /// 交易，失敗者保留衝突且重試零新增。barrier 由測試 using owner 確定處置，
    /// Task.WhenAll 確定排空兩個呼叫，不留下執行中的 SDK 工作。
    /// </summary>
    [Fact]
    public async Task Care_PresentRecord_ConcurrentSameEvent_PrimaryKeyConflictThenRetryDrains()
    {
        using var barrier = new Barrier(2);
        var sdk = new CareSdk { QueryBarrier = barrier };
        var service = new NewPersonPresentRecordService(new FixedClock());
        var errors = await Task.WhenAll(
            Task.Run(() => Record.Exception(() => service.Execute(sdk, sdk.Contact.Id))),
            Task.Run(() => Record.Exception(() => service.Execute(sdk, sdk.Contact.Id))));
        Assert.Single(errors.Where(error => error is null));
        Assert.IsType<InvalidOperationException>(Assert.Single(errors.Where(error => error is not null)));
        Assert.Single(sdk.Transactions);
        Assert.Single(sdk.Rows.Where(row => row.LogicalName == CareSdk.ReportTable));
        Assert.Single(sdk.Rows.Where(row => row.LogicalName == CareSdk.AttendanceTable));
        sdk.QueryBarrier = null;
        service.Execute(sdk, sdk.Contact.Id);
        Assert.Single(sdk.Transactions);
    }

    /// <summary>固定在台北星期一、UTC 仍星期日，實際時區轉換由正式 ChurchClock 執行。</summary>
    private sealed class FixedClock : IChurchClock
    {
        /// <summary>單一固定 UTC 瞬間，避免讀兩次日期跨界。</summary>
        public DateTimeOffset UtcNow => new(2026, 11, 15, 16, 30, 0, TimeSpan.Zero);
        /// <summary>借正式無狀態轉換，不模擬時區答案。</summary>
        public DateTime ToChurchTime(DateTime utcNow, string timeZone) => new ChurchClock().ToChurchTime(utcNow, timeZone);
        /// <summary>固定瞬間在正式教會時區的日期。</summary>
        public DateTime TodayInChurch(string timeZone) => ToChurchTime(UtcNow.UtcDateTime, timeZone).Date;
        /// <summary>借正式反向轉換，保護 UserLocal 日期的 UTC 存儲。</summary>
        public DateTime ToUtc(LocalDateTimeOfChurch local) => new ChurchClock().ToUtc(local);
    }

    /// <summary>測試專用 SDK owner，所有資料只屬本例，沒有外部資源或背景生命週期。</summary>
    private sealed class CareSdk : IOrganizationService
    {
        /// <summary>替身資料庫的單一交易鎖，僅由當例 owner 持有，不是產品冪等保證。</summary>
        private readonly object gate = new();
        /// <summary>正式週報資料表名稱。</summary>
        public const string ReportTable = "new_group_present_weekly_report";
        /// <summary>正式出席資料表名稱。</summary>
        public const string AttendanceTable = "new_present_record";
        /// <summary>建立互相一致的教會、主要小組與同教會領袖，owner 特意採不同 team。</summary>
        public CareSdk()
        {
            Church = new Entity("account", Guid.NewGuid()) { ["new_timezone"] = "Asia/Taipei" };
            Leader = new Entity("contact", Guid.NewGuid())
            {
                ["parentcustomerid"] = Church.ToEntityReference(),
                ["ownerid"] = new EntityReference("team", Guid.NewGuid())
            };
            List = new Entity("list", Guid.NewGuid())
            {
                ["listname"] = "關懷", ["new_contact_family_leader_list"] = Leader.ToEntityReference(),
                ["new_group_place"] = "Synthetic place", ["new_group_time"] = "Synthetic time"
            };
            Contact = new Entity("contact", Guid.NewGuid())
            {
                ["fullname"] = "Same name", ["parentcustomerid"] = Church.ToEntityReference(),
                ["new_cell_list_contact"] = List.ToEntityReference(), ["mobilephone"] = "Synthetic",
                ["ownerid"] = new EntityReference("team", Guid.NewGuid())
            };
            Rows.AddRange(new[] { Church, Leader, List, Contact });
        }
        /// <summary>當例正式教會。</summary>
        public Entity Church { get; }
        /// <summary>當例組長，允許注入跨教會錯誤。</summary>
        public Entity Leader { get; }
        /// <summary>當例主要小組。</summary>
        public Entity List { get; }
        /// <summary>當例目標會員。</summary>
        public Entity Contact { get; }
        /// <summary>有限測試資料庫，無 static 或跨案例共用。</summary>
        public List<Entity> Rows { get; } = new();
        /// <summary>已成功的交易，用於零寫入及重跑斷言。</summary>
        public List<ExecuteTransactionRequest> Transactions { get; } = new();
        /// <summary>在交易發布前注入原始故障，null 表示恢復。</summary>
        public Exception? Failure { get; set; }
        /// <summary>由個別測試 using owner 擁有的有界競爭注入；替身不負責 Dispose。</summary>
        public Barrier? QueryBarrier { get; set; }
        /// <summary>建立已存在且非 deterministic 的正式週報，模擬歷史資料。</summary>
        public Entity ExistingReport() => new(ReportTable, Guid.NewGuid())
        {
            ["new_list_group_present_weekly_report"] = List.ToEntityReference(),
            ["new_sunday_date"] = new DateTime(2026, 11, 21, 16, 0, 0, DateTimeKind.Utc)
        };
        /// <summary>只讀取當例正式主鍵；不存在時原樣失敗。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            lock (gate) return Rows.Single(row => row.LogicalName == entityName && row.Id == id);
        }
        /// <summary>實際依條件篩選，不讓錯誤日期或作用域查詢仍無條件命中。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);
            Entity[] result;
            lock (gate)
                result = Rows.Where(row => row.LogicalName == expression.EntityName &&
                    expression.Criteria.Conditions.All(condition => Matches(row, condition))).ToArray();
            if (expression.EntityName == AttendanceTable && QueryBarrier is Barrier barrier)
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)), "Concurrent query did not drain.");
            return new EntityCollection(result);
        }
        /// <summary>白名單相等及日期半開區間，未知條件明確使測試失敗。</summary>
        private static bool Matches(Entity row, ConditionExpression condition)
        {
            row.Attributes.TryGetValue(condition.AttributeName, out var value);
            if (value is EntityReference reference) value = reference.Id;
            var expected = condition.Values[0];
            return condition.Operator switch
            {
                ConditionOperator.Equal => Equals(value, expected),
                ConditionOperator.GreaterEqual => value is DateTime start && start >= (DateTime)expected,
                ConditionOperator.LessThan => value is DateTime end && end < (DateTime)expected,
                _ => throw new InvalidOperationException("Unexpected query condition.")
            };
        }
        /// <summary>先驗全部主鍵及故障，再一次發布候選，模擬整筆交易回滾。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            var transaction = Assert.IsType<ExecuteTransactionRequest>(request);
            lock (gate)
            {
                if (Failure is not null) throw Failure;
                var creates = transaction.Requests.Select(item => Assert.IsType<CreateRequest>(item).Target).ToArray();
                Assert.Equal(creates.Length, creates.Select(row => row.Id).Distinct().Count());
                if (creates.Any(row => Rows.Any(existing => existing.Id == row.Id)))
                    throw new InvalidOperationException("Synthetic database primary key conflict.");
                Rows.AddRange(creates);
                Transactions.Add(transaction);
            }
            return new ExecuteTransactionResponse();
        }
        /// <summary>禁止繞過交易直接 Create。</summary>
        public Guid Create(Entity entity) => throw new InvalidOperationException("Unexpected direct create.");
        /// <summary>建立服務不得覆寫已存在的出席內容。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException("Unexpected update.");
        /// <summary>本服務不刪除任何歷史資料。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException("Unexpected delete.");
        /// <summary>本服務不修改名單成員關係。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected associate.");
        /// <summary>本服務不移除名單成員關係。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new InvalidOperationException("Unexpected disassociate.");
    }
}
