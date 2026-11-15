/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P5-05 週報生成活動到 Core SDK adapter 的完整離線契約。
 * WorkflowInvoker 執行真正活動，平台 context／factory 使用合成替身；私有資料庫
 * 以鎖保護查詢與交易候選交換，模擬主鍵唯一及整筆回滾，不改共用 SDK fake。
 *
 * 主要功能與流程：固定單次 UTC 來源，驗證本週／下週、教會時區、同週重跑、
 * 同名異 ID、跨教會拒絕、owner／門徒名單隔離、完整翻頁及 DTD 拒絕。
 * 第二個 Create 故障注入應使資料庫零新增，清除故障後可重試；同作用域並行
 * 允許主鍵衝突拒絕，但成功提交不得重複或留下半張週報。測試只證明模型契約，
 * 不冒充實機 Dataverse 權限、沙箱追蹤、資料庫壓力或部署啟用驗收。
 *
 * 使用邊界與維護：每個 fixture 唯一擁有有限合成資料、Mock 與追蹤；同步 Invoke
 * 排空後 Dispose 清除資料、委派及捕捉參照。沒有真 SDK 連線、憑證、網路、
 * timer 或通知工作；產品借用的 SDK 不得被活動 Dispose。測試先於實作建立，
 * 建置及紅綠執行由主代理統一排程，未執行前不得宣稱已通過。
 */
extern alias Workflows;

using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using Moq;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;
using Xunit;
using WeeklyReportGenerationActivity = Workflows::SpeechMessage.Dynamics.Workflows.WeeklyReports.WeeklyReportGenerationActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>真活動、借入平台使用者 SDK 與原子 Create 模型的生成回歸矩陣。</summary>
public sealed class WeeklyReportGenerationShellTests
{
    /// <summary>同週同名單重跑只保留兩張完整週報，不補已有恰一筆週報的缺席列。</summary>
    [Fact]
    public void WeeklyReport_Generation_SameWeekTwice_NoDuplicateReport()
    {
        using var f = new Fixture();
        f.Run();
        f.Run();
        Assert.Equal(2, f.Db.Rows(Fixture.Report).Length);
        Assert.Equal(4, f.Db.Rows(Fixture.Attendance).Length);
        Assert.Equal(2, f.Db.Transactions);
        Assert.Equal(2, f.Clock.Reads);
        f.Db.RemoveAttendance();
        f.Run();
        Assert.Empty(f.Db.Rows(Fixture.Attendance));
        Assert.Equal(2, f.Db.Transactions);
    }

    /// <summary>舊資料同週已有兩個正式 ID 時拒絕，不默默選一筆或再建第三筆。</summary>
    [Fact]
    public void WeeklyReport_Generation_ExistingDuplicate_FailsClosed()
    {
        using var f = new Fixture();
        foreach (var id in new[] { Guid.NewGuid(), Guid.NewGuid() })
            f.Db.Add(new Entity(Fixture.Report, id)
            {
                ["statecode"] = new OptionSetValue(0),
                ["new_list_group_present_weekly_report"] = f.List.ToEntityReference(),
                ["new_sunday_date"] = Fixture.SundayUtc
            });
        Assert.ThrowsAny<Exception>(() => f.Run());
        Assert.Equal(0, f.Db.Transactions);
        Assert.Equal(2, f.Db.Rows(Fixture.Report).Length);
    }

    /// <summary>兩個姓名完全相同但正式 ID 不同的會友，各自在兩週中保有出席列。</summary>
    [Fact]
    public void WeeklyReport_Generation_SameNameDifferentIds_Preserved()
    {
        using var f = new Fixture();
        f.Run();
        var rows = f.Db.Rows(Fixture.Attendance);
        Assert.Equal(4, rows.Length);
        Assert.All(rows.GroupBy(x => x.GetAttributeValue<EntityReference>("new_group_present_weekly_report_prese").Id),
            group => Assert.Equal(f.Members.Select(x => x.Id).OrderBy(x => x),
                group.Select(x => x.GetAttributeValue<EntityReference>("new_contact_new_present_record").Id).OrderBy(x => x)));
    }

    /// <summary>正式成員或任何領袖跨教會都在第一筆交易前拒絕，包含副組長等未映射角色。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WeeklyReport_Generation_CrossChurch_FailsBeforeWrites(bool leader)
    {
        using var f = new Fixture();
        var other = new Entity("contact", Guid.NewGuid())
        {
            ["parentcustomerid"] = new EntityReference("account", Guid.NewGuid()),
            ["statecode"] = new OptionSetValue(0)
        };
        f.Db.Add(other);
        if (leader) f.List["new_contact_list_vice_family_leader"] = other.ToEntityReference();
        else f.Members[1]["parentcustomerid"] = other["parentcustomerid"];
        Assert.ThrowsAny<Exception>(() => f.Run());
        Assert.Empty(f.Db.Rows(Fixture.Report));
        Assert.Empty(f.Db.Rows(Fixture.Attendance));
        Assert.Equal(0, f.Db.Transactions);
    }

    /// <summary>第二個 Create 故障不得留週報；同一活動重試成功且不持有前次候選。</summary>
    [Fact]
    public void WeeklyReport_Generation_TransactionSecondCreateFailure_RollsBackAndRetries()
    {
        using var f = new Fixture();
        f.Db.FailAtCreate = 2;
        var failure = Assert.ThrowsAny<Exception>(() => f.Run());
        Assert.Contains("synthetic transaction failure", failure.Message, StringComparison.Ordinal);
        Assert.Empty(f.Db.Rows(Fixture.Report));
        Assert.Empty(f.Db.Rows(Fixture.Attendance));
        f.Db.FailAtCreate = 0;
        f.Run();
        Assert.Equal(2, f.Db.Rows(Fixture.Report).Length);
        Assert.Equal(4, f.Db.Rows(Fixture.Attendance).Length);
    }

    /// <summary>後名單缺組長須拒絕；修為同教會新組長後，不得沿用前張 team owner／門徒名單。</summary>
    [Fact]
    public void WeeklyReport_Generation_OwnerAndDisciple_DoNotCrossLists()
    {
        using var f = new Fixture();
        var other = f.AddList("另一名單");
        other["new_contact_list_arealeader"] = f.Leader.ToEntityReference();
        Assert.Throws<InvalidOperationException>(() => f.Run());
        Assert.Equal(0, f.Db.Transactions);
        var secondLeader = new Entity("contact", Guid.NewGuid())
        { ["parentcustomerid"] = new EntityReference("account", f.ChurchId) };
        f.Db.Add(secondLeader);
        other["new_contact_family_leader_list"] = secondLeader.ToEntityReference();
        f.Run();
        var first = f.Db.Rows(Fixture.Report).Where(x =>
            x.GetAttributeValue<EntityReference>("new_list_group_present_weekly_report").Id == f.List.Id).ToArray();
        Assert.Equal(2, first.Length);
        Assert.All(first, x =>
        {
            Assert.Equal("team", x.GetAttributeValue<EntityReference>("ownerid").LogicalName);
            Assert.Equal(f.DiscipleId, x.GetAttributeValue<EntityReference>("new_deciple_list_group_present_weekly").Id);
        });
        Assert.All(f.Db.Rows(Fixture.Report).Where(x =>
            x.GetAttributeValue<EntityReference>("new_list_group_present_weekly_report").Id == other.Id), x =>
        {
            Assert.False(x.Contains("ownerid"));
            Assert.False(x.Contains("new_deciple_list_group_present_weekly"));
        });
        Assert.All(f.Db.Rows(Fixture.Attendance), x =>
            Assert.Equal("team", x.GetAttributeValue<EntityReference>("ownerid").LogicalName));
    }

    /// <summary>owner 的正式目標型別直接進 Create，team／systemuser 都不能被強制轉成另一種。</summary>
    [Theory]
    [InlineData("team")]
    [InlineData("systemuser")]
    public void WeeklyReport_Generation_OwnerType_Preserved(string ownerType)
    {
        using var f = new Fixture();
        var ownerId = Guid.NewGuid();
        f.Leader["ownerid"] = new EntityReference(ownerType, ownerId);
        f.Run();
        Assert.All(f.Db.Rows(Fixture.Report).Concat(f.Db.Rows(Fixture.Attendance)), row =>
        {
            var owner = row.GetAttributeValue<EntityReference>("ownerid");
            Assert.Equal(ownerType, owner.LogicalName);
            Assert.Equal(ownerId, owner.Id);
        });
    }

    /// <summary>台北已星期日而 UTC 仍星期六時，午夜需存前日16點，查詢不用 On 運算子。</summary>
    [Fact]
    public void WeeklyReport_WeekBoundary_UsesChurchTimeZone()
    {
        using var f = new Fixture();
        f.Run();
        var report = f.Db.Rows(Fixture.Report).OrderBy(x => (DateTime)x["new_sunday_date"]).First();
        Assert.Equal(Fixture.SundayUtc, report["new_sunday_date"]);
        Assert.Equal(WeeklyReportGenerationRule.ReportId(f.ChurchId, f.List.Id,
            new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Unspecified)), report.Id);
        Assert.Equal(1, f.Clock.Reads);
        Assert.All(f.Db.ReportWindows, window =>
        {
            Assert.Equal(TimeSpan.FromDays(1), window.End - window.Start);
            Assert.Equal(16, window.Start.Hour);
        });
    }

    /// <summary>listmember 與動態 contact 分頁都必須讀到末頁；同正式會員重複不得去重掩蓋。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WeeklyReport_Generation_MembershipPagingAndDuplicateGuard(bool dynamic)
    {
        using var f = new Fixture();
        f.Db.PageSize = 1;
        f.List["type"] = dynamic;
        f.List["query"] = "<fetch distinct='false'><entity name='contact'><all-attributes/></entity></fetch>";
        f.Run();
        Assert.Equal(4, f.Db.Rows(Fixture.Attendance).Length);
        Assert.True(f.Db.MemberPages >= 2);
        using var duplicate = new Fixture();
        duplicate.Db.DuplicateMember = true;
        Assert.ThrowsAny<Exception>(() => duplicate.Run());
        Assert.Equal(0, duplicate.Db.Transactions);
    }

    /// <summary>stored FetchXML 不能讀取 DTD 或非 contact 根表，拒絕時沒有交易。</summary>
    [Theory]
    [InlineData("<!DOCTYPE fetch [<!ENTITY x SYSTEM 'file:///synthetic'>]><fetch><entity name='contact'/></fetch>")]
    [InlineData("<fetch><entity name='account'/></fetch>")]
    [InlineData("<fetch top='1'><entity name='contact'/></fetch>")]
    public void WeeklyReport_Generation_UnsafeFetch_FailsClosed(string xml)
    {
        using var f = new Fixture();
        f.List["type"] = true;
        f.List["query"] = xml;
        Assert.ThrowsAny<Exception>(() => f.Run());
        Assert.Equal(0, f.Db.Transactions);
    }

    /// <summary>所有候選先驗證；第二名單錯教會不可留下第一名單的交易。</summary>
    [Fact]
    public void WeeklyReport_Generation_LaterListFailure_NoEarlierWrites()
    {
        using var f = new Fixture();
        var second = f.AddList("第二名單");
        var outsider = new Entity("contact", Guid.NewGuid())
        {
            ["parentcustomerid"] = new EntityReference("account", Guid.NewGuid())
        };
        f.Db.Add(outsider);
        second["new_contact_list_arealeader"] = outsider.ToEntityReference();
        second["new_contact_family_leader_list"] = f.Leader.ToEntityReference();
        Assert.ThrowsAny<Exception>(() => f.Run());
        Assert.Equal(0, f.Db.Transactions);
    }

    /// <summary>後頁真實讀取故障不回傳部分成員；下一次完整重讀可成功，不沿用前次候選。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WeeklyReport_Generation_LaterPageFailure_CanRetry(bool dynamic)
    {
        using var f = new Fixture();
        f.Db.PageSize = 1;
        f.List["type"] = dynamic;
        f.List["query"] = "<fetch><entity name='contact'/></fetch>";
        f.Db.FailMemberPage = 2;
        Assert.ThrowsAny<Exception>(() => f.Run());
        Assert.Equal(0, f.Db.Transactions);
        Assert.Empty(f.Db.Rows(Fixture.Report));
        f.Db.FailMemberPage = 0;
        f.Run();
        Assert.Equal(4, f.Db.Rows(Fixture.Attendance).Length);
    }

    /// <summary>活動只處理 ExecuteWorkflow，任何深度仍執行；其他訊息連時鐘都不讀。</summary>
    [Fact]
    public void WeeklyReport_Generation_MessageAndDepth_MatchLegacy()
    {
        using var f = new Fixture();
        f.Run(message: "Create", depth: 9);
        Assert.Equal(0, f.Clock.Reads);
        Assert.Equal(0, f.Db.Reads);
        f.Run(depth: 9);
        Assert.Equal(2, f.Db.Transactions);
    }

    /// <summary>真 Shadow recorder 記純 Create 而零真寫入，安全摘要包含同一正式主鍵。</summary>
    [Fact]
    public void WeeklyReport_Generation_Shadow_RecordsWithoutWriting()
    {
        using var f = new Fixture();
        f.Run(mode: "Shadow");
        Assert.Empty(f.Db.Rows(Fixture.Report));
        Assert.Empty(f.Db.Rows(Fixture.Attendance));
        Assert.Equal(0, f.Db.Transactions);
        var lines = f.Trace.Entries.Where(x => x.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
            .Select(x => JObject.Parse(x.Substring("SMV-SHADOW|1|".Length))).ToArray();
        Assert.Equal(6, lines.Count(x => (string?)x["operation"] == "Create"));
        Assert.True((bool)lines.Last()["end"]!);
    }

    /// <summary>Shadow 累計 overlay 超64及 Live 單交易超1000，都須在任何交易前拒絕。</summary>
    [Theory]
    [InlineData("Shadow", 32)]
    [InlineData("Live", 1000)]
    public void WeeklyReport_Generation_OverCapacity_RejectsWholeCandidate(string mode, int members)
    {
        using var f = new Fixture();
        for (var i = f.Members.Count; i < members; i++) f.AddMember();
        Assert.ThrowsAny<Exception>(() => f.Run(mode));
        Assert.Equal(0, f.Db.Transactions);
        Assert.Empty(f.Db.Rows(Fixture.Report));
    }

    /// <summary>同活動同事件並行最多提交每週一筆；資料庫主鍵衝突不留部分出席。</summary>
    [Fact]
    public async Task WeeklyReport_Generation_ConcurrentSameScope_PrimaryKeyPreventsDuplicates()
    {
        using var f = new Fixture();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            try { f.Run(); }
            catch (InvalidOperationException ex) when (ex.Message == "synthetic duplicate primary key") { }
        })));
        Assert.Equal(2, f.Db.Rows(Fixture.Report).Length);
        Assert.Equal(4, f.Db.Rows(Fixture.Attendance).Length);
        Assert.Equal(2, f.Db.Transactions);
    }

    /// <summary>同一活動並行 A／B 再 A，所有 owner／list／會員只屬各自 SDK；Dispose 後模型回零。</summary>
    [Fact]
    public async Task WeeklyReport_Generation_SharedActivity_IsolatesScopesAndDrains()
    {
        using var a = new Fixture();
        using var b = new Fixture();
        var activity = new WeeklyReportGenerationActivity(new TestClock());
        await Task.WhenAll(Task.Run(() => a.Run(sharedActivity: activity)), Task.Run(() => b.Run(sharedActivity: activity)));
        a.Run(sharedActivity: activity);
        Assert.Equal(2, a.Db.Rows(Fixture.Report).Length);
        Assert.Equal(2, b.Db.Rows(Fixture.Report).Length);
        Assert.All(a.Db.Rows(Fixture.Attendance), row =>
            Assert.Contains(row.GetAttributeValue<EntityReference>("new_contact_new_present_record").Id, a.Members.Select(x => x.Id)));
        Assert.All(b.Db.Rows(Fixture.Attendance), row =>
            Assert.Contains(row.GetAttributeValue<EntityReference>("new_contact_new_present_record").Id, b.Members.Select(x => x.Id)));
        a.Dispose();
        b.Dispose();
        Assert.Empty(a.Db.Rows(Fixture.Report));
        Assert.Empty(a.Db.Rows(Fixture.Attendance));
        Assert.Empty(b.Db.Rows(Fixture.Report));
        Assert.Empty(b.Db.Rows(Fixture.Attendance));
        Assert.Empty(a.Db.DynamicIds);
        Assert.Empty(b.Db.DynamicIds);
    }

    /// <summary>單案合成資料與 mock 的唯一 owner；不跨測試共用 DB 或捕捉委派。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>metadata 明列的正式週報邏輯表名。</summary>
        internal const string Report = "new_group_present_weekly_report";
        /// <summary>metadata 明列的正式出席邏輯表名。</summary>
        internal const string Attendance = "new_present_record";
        /// <summary>台北星期日午夜對應 UTC 星期六16點，固定向量不依主機時區。</summary>
        internal static readonly DateTime SundayUtc = new(2026, 11, 14, 16, 0, 0, DateTimeKind.Utc);
        /// <summary>本案正式教會識別，不由活動引數傳入。</summary>
        internal Guid ChurchId { get; } = Guid.NewGuid();
        /// <summary>只屬組長 contact 的門徒名單識別，下一張名單不得繼承。</summary>
        internal Guid DiscipleId { get; } = Guid.NewGuid();
        /// <summary>本案擁有的有限合成交易資料庫。</summary>
        internal AtomicDatabase Db { get; } = new();
        /// <summary>固定 UTC 且可檢查讀值次數的借入時鐘。</summary>
        internal TestClock Clock { get; } = new();
        /// <summary>本案安全追蹤，Invoke 排空後由 Dispose 清空。</summary>
        internal FakeTracingService Trace { get; } = new();
        /// <summary>同名但不同正式 ID 的有限成員，用於故障注入及身份斷言。</summary>
        internal List<Entity> Members { get; } = new();
        /// <summary>本案名單設置列，僅在執行前由案例修改。</summary>
        internal Entity List { get; }
        /// <summary>本案組長設置列，其 owner 與教會是伺服器讀取來源。</summary>
        internal Entity Leader { get; }
        /// <summary>可重用但不持有 SDK 的真正產品活動。</summary>
        private readonly WeeklyReportGenerationActivity activity;

        /// <summary>建立正式形狀的 list、account、leader 及同名成員，全部只屬本案例。</summary>
        internal Fixture()
        {
            Db.Add(new Entity("account", ChurchId)
            {
                ["new_timezone"] = "Asia/Taipei", ["new_defaultlanguage"] = "zh-TW"
            });
            Leader = new Entity("contact", Guid.NewGuid())
            {
                ["parentcustomerid"] = new EntityReference("account", ChurchId),
                ["ownerid"] = new EntityReference("team", Guid.NewGuid()),
                ["new_deciple_group_list_contact"] = new EntityReference("list", DiscipleId)
            };
            Db.Add(Leader);
            List = AddList("合成同名小組");
            List["new_contact_family_leader_list"] = Leader.ToEntityReference();
            AddMember();
            AddMember();
            activity = new WeeklyReportGenerationActivity(Clock);
        }

        /// <summary>新增另一張無共用 owner 狀態的合成名單。</summary>
        internal Entity AddList(string name)
        {
            var row = new Entity("list", Guid.NewGuid())
            {
                ["listname"] = name, ["purpose"] = "小組", ["new_app_named"] = true,
                ["statuscode"] = new OptionSetValue(0), ["type"] = false
            };
            Db.Add(row);
            return row;
        }

        /// <summary>成員另有正式 ID；listmember 永遠使用 entityid 連接而非姓名。</summary>
        internal void AddMember()
        {
            var member = new Entity("contact", Guid.NewGuid())
            {
                ["fullname"] = "同名", ["statecode"] = new OptionSetValue(0),
                ["parentcustomerid"] = new EntityReference("account", ChurchId), ["mobilephone"] = "synthetic"
            };
            Members.Add(member);
            Db.Add(member);
            Db.DynamicIds.Add(member.Id);
            Db.Add(new Entity("listmember", Guid.NewGuid()) { ["listid"] = List.Id, ["entityid"] = member.Id });
        }

        /// <summary>每輪使用本次 UserId 的 factory；Invoker 同步排空，任何提升權限要求立即失敗。</summary>
        internal void Run(string mode = "Live", string message = "ExecuteWorkflow", int depth = 1,
            WeeklyReportGenerationActivity? sharedActivity = null)
        {
            var context = new FakeWorkflowContext(depth: depth, messageName: message);
            context.Mock.SetupGet(x => x.InitiatingUserId).Returns(Guid.NewGuid());
            var factory = new Mock<IOrganizationServiceFactory>(MockBehavior.Strict);
            factory.Setup(x => x.CreateOrganizationService(context.Object.UserId)).Returns(Db);
            var invoker = new WorkflowInvoker(sharedActivity ?? activity);
            invoker.Extensions.Add<IWorkflowContext>(() => context.Object);
            invoker.Extensions.Add<IOrganizationServiceFactory>(() => factory.Object);
            invoker.Extensions.Add<ITracingService>(() => Trace);
            try { invoker.Invoke(new Dictionary<string, object> { ["ExecutionMode"] = mode }, TimeSpan.FromSeconds(30)); }
            finally { factory.Reset(); context.Mock.Reset(); }
        }

        /// <summary>呼叫均已排空後清除資料及追蹤；沒有產品擁有的連線需代為關閉。</summary>
        public void Dispose() { Db.Dispose(); Trace.Entries.Clear(); Members.Clear(); }
    }

    /// <summary>無使用者資料的固定 UTC 來源；計數以 Interlocked 支援同活動並行驗證。</summary>
    private sealed class TestClock : IChurchClock
    {
        /// <summary>正式無狀態轉換器，不使用它的系統時間來源。</summary>
        private readonly ChurchClock clock = new();
        /// <summary>跨並行呼叫的單純測試計數，不含 scope 資料。</summary>
        private int reads;
        /// <summary>讀取計數只用來驗證每次 Generate 恰讀一次 UTC。</summary>
        internal int Reads => Volatile.Read(ref reads);
        /// <summary>一次讀值固定在台北星期日、UTC星期六；不讀伺服器時間。</summary>
        public DateTimeOffset UtcNow
        {
            get { Interlocked.Increment(ref reads); return new DateTimeOffset(2026, 11, 14, 17, 0, 0, TimeSpan.Zero); }
        }
        /// <summary>沿正式轉換以保護真時區行為。</summary>
        public DateTime ToChurchTime(DateTime utc, string ianaTimeZoneId) => clock.ToChurchTime(utc, ianaTimeZoneId);
        /// <summary>若產品使用本入口也仍計入一次來源讀值。</summary>
        public DateTime TodayInChurch(string ianaTimeZoneId) => ToChurchTime(UtcNow.UtcDateTime, ianaTimeZoneId).Date;
        /// <summary>沿正式 DST／Kind 守門，不自行猜時區。</summary>
        public DateTime ToUtc(LocalDateTimeOfChurch local) => clock.ToUtc(local);
    }

    /// <summary>
    /// 只供本檔的有界原子模型；lock 是模型資料庫同步，不是產品冪等機制。
    /// 所有 Create 先放 candidate、驗主鍵後交換；唯一 owner Dispose 清除全部資料。
    /// </summary>
    private sealed class AtomicDatabase : IOrganizationService, IDisposable
    {
        /// <summary>模型資料庫單一提交鎖，只保護本案例資料而不參與產品實作。</summary>
        private readonly object gate = new();
        /// <summary>已提交模型快照，交易僅於整筆驗證成功後交換。</summary>
        private Dictionary<Guid, Entity> rows = new();
        /// <summary>動態名單的有限正式 contact ID 候選。</summary>
        internal List<Guid> DynamicIds { get; } = new();
        /// <summary>查詢真 UTC 起訖記錄，Dispose 清空，不保留產品個資。</summary>
        internal List<(DateTime Start, DateTime End)> ReportWindows { get; } = new();
        /// <summary>成功提交數；故障回滾不能增加。</summary>
        internal int Transactions { get; private set; }
        /// <summary>全部同步讀取次數，保護非 ExecuteWorkflow 的零 I/O。</summary>
        internal int Reads { get; private set; }
        /// <summary>實際讀取成員頁次數，不以結果筆數代替分頁執行證據。</summary>
        internal int MemberPages { get; private set; }
        /// <summary>合成服務頁大小，可設一筆強制產品翻頁。</summary>
        internal int PageSize { get; set; } = 5000;
        /// <summary>交易第幾項注入故障，零表示關閉。</summary>
        internal int FailAtCreate { get; set; }
        /// <summary>成員查詢第幾頁故障，零表示關閉，重試不得沿用先前頁。</summary>
        internal int FailMemberPage { get; set; }
        /// <summary>以不同 listmember ID 重複同 contact，保護成員正式 ID 衝突守門。</summary>
        internal bool DuplicateMember { get; set; }

        /// <summary>只在案例設置階段加入合成正式列。</summary>
        internal void Add(Entity row) { lock (gate) rows.Add(row.Id, row); }
        /// <summary>每次回傳獨立集合及 Entity，讓測試不修改已提交快照。</summary>
        internal Entity[] Rows(string table) { lock (gate) return rows.Values.Where(x => x.LogicalName == table).Select(Copy).ToArray(); }
        /// <summary>模擬既有週報缺席列；下次生成不得自行補齊。</summary>
        internal void RemoveAttendance()
        {
            lock (gate)
                foreach (var id in rows.Values.Where(x => x.LogicalName == Fixture.Attendance).Select(x => x.Id).ToArray())
                    rows.Remove(id);
        }
        /// <summary>驗指定欄位讀取及正式表名，回傳獨立 Entity 容器。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            lock (gate)
            {
                Reads++;
                Assert.False(columnSet.AllColumns);
                var source = rows[id];
                Assert.Equal(entityName, source.LogicalName);
                var result = new Entity(entityName, id);
                foreach (var name in columnSet.Columns) if (source.Contains(name)) result[name] = source[name];
                return result;
            }
        }
        /// <summary>解析產品真查詢條件及頁碼，不固定傳回空集合以掩蓋重複或分頁缺陷。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            lock (gate)
            {
                Reads++;
                Entity[] candidates;
                int page;
                if (query is QueryExpression typed)
                {
                    Assert.False(typed.ColumnSet.AllColumns);
                    page = typed.PageInfo.PageNumber;
                    candidates = rows.Values.Where(x => x.LogicalName == typed.EntityName && Matches(x, typed.Criteria)).Select(Copy).ToArray();
                    if (typed.EntityName == "listmember")
                    {
                        if (page == FailMemberPage) throw new InvalidOperationException("synthetic page failure");
                        MemberPages++;
                        if (DuplicateMember && candidates.Length > 0)
                        {
                            var repeated = Copy(candidates[0]);
                            repeated.Id = Guid.NewGuid();
                            candidates = candidates.Concat(new[] { repeated }).ToArray();
                        }
                    }
                    if (typed.EntityName == Fixture.Report)
                    {
                        Assert.DoesNotContain(typed.Criteria.Conditions, c => c.Operator == ConditionOperator.On);
                        ReportWindows.Add(((DateTime)typed.Criteria.Conditions.Single(c => c.Operator == ConditionOperator.GreaterEqual).Values[0],
                            (DateTime)typed.Criteria.Conditions.Single(c => c.Operator == ConditionOperator.LessThan).Values[0]));
                    }
                }
                else
                {
                    var fetch = XDocument.Parse(Assert.IsType<FetchExpression>(query).Query);
                    Assert.Equal("contact", (string?)fetch.Root!.Element("entity")!.Attribute("name"));
                    Assert.Empty(fetch.Descendants("all-attributes"));
                    Assert.Equal("contactid", (string?)Assert.Single(fetch.Root.Element("entity")!.Elements("attribute")).Attribute("name"));
                    page = (int?)fetch.Root.Attribute("page") ?? 1;
                    if (page == FailMemberPage) throw new InvalidOperationException("synthetic page failure");
                    candidates = DynamicIds.Select(id => Copy(rows[id])).ToArray();
                    MemberPages++;
                }
                var result = new EntityCollection(candidates.Skip((page - 1) * PageSize).Take(PageSize).ToArray())
                {
                    MoreRecords = page * PageSize < candidates.Length,
                    PagingCookie = page.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
                return result;
            }
        }
        /// <summary>整筆候選成功才發布；第二項故障及正式主鍵衝突均不變更既有資料。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            lock (gate)
            {
                var tx = Assert.IsType<ExecuteTransactionRequest>(request);
                Assert.False(tx.ReturnResponses);
                Assert.InRange(tx.Requests.Count, 1, 1000);
                var candidate = new Dictionary<Guid, Entity>(rows);
                var ordinal = 0;
                foreach (var item in tx.Requests)
                {
                    var create = Assert.IsType<CreateRequest>(item);
                    var target = create.Target;
                    Assert.NotEqual(Guid.Empty, target.Id);
                    if (++ordinal == FailAtCreate) throw new InvalidOperationException("synthetic transaction failure");
                    if (candidate.ContainsKey(target.Id)) throw new InvalidOperationException("synthetic duplicate primary key");
                    var detached = Copy(target);
                    detached["statecode"] = new OptionSetValue(0);
                    candidate.Add(detached.Id, detached);
                }
                rows = candidate;
                Transactions++;
                var response = new ExecuteTransactionResponse();
                response.Results["Responses"] = new OrganizationResponseCollection();
                return response;
            }
        }
        /// <summary>只實作本次 adapter 所需條件；未知條件立即失敗避免測試錯誤放行。</summary>
        private static bool Matches(Entity row, FilterExpression filter)
        {
            var values = filter.Conditions.Select(c =>
            {
                row.Attributes.TryGetValue(c.AttributeName, out var raw);
                var value = raw is EntityReference reference ? reference.Id : raw is OptionSetValue option ? option.Value : raw;
                switch (c.Operator)
                {
                    case ConditionOperator.Equal: return Equals(value, c.Values[0]);
                    case ConditionOperator.Null: return value is null;
                    case ConditionOperator.NotLike: return value is string text &&
                        text.IndexOf(((string)c.Values[0]).Trim('%'), StringComparison.Ordinal) < 0;
                    case ConditionOperator.GreaterEqual: return value is DateTime start && start >= (DateTime)c.Values[0];
                    case ConditionOperator.LessThan: return value is DateTime end && end < (DateTime)c.Values[0];
                    default: throw new NotSupportedException("Unsupported synthetic condition.");
                }
            }).Concat(filter.Filters.Select(child => Matches(row, child)));
            return filter.FilterOperator == LogicalOperator.And ? values.All(x => x) : values.Any(x => x);
        }
        /// <summary>深複製可變 SDK 純值，隔離候選、既有資料與測試讀取。</summary>
        private static Entity Copy(Entity source)
        {
            var result = new Entity(source.LogicalName, source.Id);
            foreach (var pair in source.Attributes)
                result[pair.Key] = pair.Value is EntityReference r ? new EntityReference(r.LogicalName, r.Id) { Name = r.Name }
                    : pair.Value is OptionSetValue option ? new OptionSetValue(option.Value) : pair.Value;
            return result;
        }
        /// <summary>單筆寫入不屬產品契約，若錯用即令測試失敗。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException("Use atomic transaction.");
        /// <summary>生成不可更新既有正式記錄。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>生成不可刪除正式記錄。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>生成不可額外建立關聯副作用。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>生成不可解除正式關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>所有 Invoke 排空後清除本案資料；沒有真正連線或非同步 owner。</summary>
        public void Dispose() { lock (gate) { rows.Clear(); DynamicIds.Clear(); ReportWindows.Clear(); } }
    }
}
