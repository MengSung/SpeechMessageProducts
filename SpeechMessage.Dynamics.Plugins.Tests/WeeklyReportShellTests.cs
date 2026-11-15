/*
 * 檔案整體說明
 *
 * 目的與定位：以真正週報 Plugin 外殼及單案假 SDK 保護 P5-05 的 Shadow 契約。
 * 本檔不連接 Dataverse 或 LINE，不把本機意圖、合成正式識別或無效封包當實機證據。
 *
 * 主要功能與流程：驗證統計從當下已儲存旗標啟動、出席列分母與缺值相容性；
 * 通知保留 null／空字串差異、四角色同人去重與正式名單組長決定的教會邊界。
 * 故障注入包含缺 PreImage、錯教會、未綁定、重複出席身分及原始 SDK 例外。
 * 決定性斷言檢查安全影子投影、零真寫入、穩定 retry key 與跨操作無殘留。
 *
 * 使用邊界與維護：fixture 是假 SDK、資料字典及 trace 的唯一 owner；Execute 同步
 * 排空後 Dispose 清除回呼、集合與追蹤。無效加密封包可揭露 Shadow 誤解密；
 * 不建立 HTTP、timer、queue、秘密或全域可變狀態。紅綠執行由主代理統一安排，
 * 本檔存在不表示測試已執行或部分信任、打包與部署關卡已通過。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using SpeechMessage.Dynamics.Plugins.WeeklyReports;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>以真入口與有界合成 SDK 驗證週報外殼接線，不複製純規則的完整測試矩陣。</summary>
public sealed class WeeklyReportShellTests
{
    /// <summary>Target 不帶命令仍須以 Retrieve 計算；前像相同命令也不能阻止已儲存的 StartsWith。</summary>
    [Fact]
    public void WeeklyReport_Statistics_FromPreImageDelta()
    {
        using var f = new Fixture();
        f.Report["new_saved_flag"] = "計算出席率-now";
        var context = f.Context(false);
        context.PreEntityImages["PreImage"]["new_saved_flag"] = "計算出席率-now";
        f.AddAttendance(1, 1, 0);
        f.AddAttendance(1, 0, 1);
        f.Run(Statistics(), context);
        var patch = Assert.Single(f.Intents().Where(x => (string?)x["entity"] == Fixture.ReportTable));
        Assert.Equal(1, (int)patch["fields"]!["new_sunday_present_number"]!);
        Assert.Equal(1, (int)patch["fields"]!["new_small_group_number"]!);
        Assert.Equal(0.5d, (double)patch["fields"]!["new_sunday_present_rate"]!);
        Assert.Equal(0.5d, (double)patch["fields"]!["new_small_group_rate"]!);
        Assert.Null(patch["fields"]!["new_small_group_member_number"]);
        Assert.Empty(f.Service.WriteRecords);
        Assert.True(f.QueryCount > 0);
    }

    /// <summary>同一正式會友擔任四角色只記一次；不同正式人即使同名仍各保留一個意圖。</summary>
    [Fact]
    public void WeeklyReport_Notification_PushesOncePerRecipient()
    {
        using var f = new Fixture();
        f.Run(Notification(), f.Context(true));
        var pushes = f.Pushes();
        Assert.Equal(new[] { f.ContactId.ToString("D"), f.SecondContactId.ToString("D") },
            pushes.Select(x => (string?)x["contactId"]));
        Assert.All(pushes, x => Assert.Equal(1, (int)x["messageCount"]!));
        Assert.NotEqual((string?)pushes[0]["retryKey"], (string?)pushes[1]["retryKey"]);
        Assert.Empty(f.Service.WriteRecords);
    }

    /// <summary>三個正式入口消費 Core 共用六案，逐欄核對安全投影及推播內容摘要，禁止額外效果。</summary>
    [Fact]
    public void WeeklyReport_ShadowMode_MatchesExpected()
    {
        WeeklyReportGoldenRunner.RunAll();
    }

    /// <summary>深度二仍計算、Update 深度三停止；通知 Create 深度三放行但沿用舊無推播行為。</summary>
    [Fact]
    public void WeeklyReport_DepthPolicy_MatchesLegacy()
    {
        using var f = new Fixture();
        f.Run(Statistics(), f.Context(false, 2));
        Assert.Single(f.Intents());
        var reads = f.ReadCount;
        f.Run(Statistics(), f.Context(false, 3));
        Assert.Equal(reads, f.ReadCount);
        Assert.Empty(f.Lines());
        f.Run(Notification(), f.Context(true, 2));
        Assert.Equal(2, f.Pushes().Length);
        reads = f.ReadCount;
        f.Run(Notification(), f.Context(true, 3));
        Assert.Equal(reads, f.ReadCount);
        var create = f.Context(true, 3);
        create.MessageName = "Create";
        f.Run(Notification(), create);
        Assert.Single(f.Lines());
        Assert.Empty(f.Pushes());
    }

    /// <summary>真外殼不得吞掉或重建 SDK 故障；兩入口都重拋同一實例且零真寫入。</summary>
    [Fact]
    public void WeeklyReport_UnexpectedError_Rethrown()
    {
        using var f = new Fixture();
        var expected = new InvalidOperationException("synthetic SDK failure");
        f.Service.RetrieveHandler = (_, _, _) => throw expected;
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => f.Run(Statistics(), f.Context(false))));
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => f.Run(Notification(), f.Context(true))));
        Assert.Empty(f.Service.WriteRecords);
    }

    /// <summary>來源教會只能從名單組長正式 contact 推導；收件者換教會時不發該人且不讀其頻道。</summary>
    [Fact]
    public void WeeklyReport_RecipientOtherChurch_NoPush()
    {
        using var f = new Fixture();
        f.SecondMember["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
        f.Run(Notification(), f.Context(true));
        Assert.Equal(f.ContactId.ToString("D"), (string?)Assert.Single(f.Pushes())["contactId"]);
        Assert.Equal(1, f.ChannelReads);
        Assert.Empty(f.Service.WriteRecords);
    }

    /// <summary>未綁定是正常拒絕，不讀該人頻道、不解密、不丟例外；其他合法收件者仍獨立處理。</summary>
    [Fact]
    public void WeeklyReport_NotBound_NoPushNoThrow()
    {
        using var f = new Fixture();
        f.Member.Attributes.Remove("new_lineid");
        f.Run(Notification(), f.Context(true));
        Assert.Equal(f.SecondContactId.ToString("D"), (string?)Assert.Single(f.Pushes())["contactId"]);
        Assert.Equal(1, f.ChannelReads);
    }

    /// <summary>同一已驗證組織、週報、平台事件與收件者重跑有相同鍵；不以每輪隨機數或文字當事件。</summary>
    [Fact]
    public void WeeklyReport_RetryKey_IsStableForSameEvent()
    {
        using var f = new Fixture();
        var shell = Notification();
        var context = f.Context(true);
        f.Run(shell, context);
        var first = f.Pushes().Select(x => (string?)x["retryKey"]).ToArray();
        f.Run(shell, context);
        Assert.Equal(first, f.Pushes().Select(x => (string?)x["retryKey"]));
    }

    /// <summary>兩外殼缺圖一律拒絕，不能拿 Target 或查詢回應冒充必要的 PreImage。</summary>
    [Fact]
    public void WeeklyReport_MissingPreImage_FailsClosed()
    {
        using var f = new Fixture();
        foreach (var notification in new[] { false, true })
        {
            var context = f.Context(notification);
            context.PreEntityImages.Clear();
            Assert.Throws<InvalidPluginExecutionException>(() =>
                f.Run(notification ? Notification() : Statistics(), context));
            Assert.Empty(f.Intents());
        }
        Assert.Empty(f.Service.WriteRecords);
    }

    /// <summary>缺分類先補 100000000、零分母不寫四欄；有分類但缺整數時忠實累加 -9999。</summary>
    [Fact]
    public void WeeklyReport_Statistics_MissingValuesAndZeroDenominator_Preserved()
    {
        using var f = new Fixture();
        var contact = f.AddAttendance(null, 1, 1);
        f.Run(Statistics(), f.Context(false));
        var intents = f.Intents();
        Assert.Equal(contact.ToString("D"), (string?)intents[0]["id"]);
        Assert.Equal(100000000, (int)intents[0]["fields"]!["customertypecode"]!);
        var fields = intents[1]["fields"]!;
        Assert.Null(fields["new_sunday_present_number"]);
        Assert.Null(fields["new_small_group_number"]);
        Assert.Null(fields["new_sunday_present_rate"]);
        Assert.Null(fields["new_small_group_rate"]);
        f.Attendance.Clear();
        f.AddAttendance(1, null, 1);
        f.Run(Statistics(), f.Context(false));
        fields = Assert.Single(f.Intents())["fields"]!;
        Assert.Equal(-9999, (int)fields["new_sunday_present_number"]!);
        Assert.Equal(-9999d, (double)fields["new_sunday_present_rate"]!);
    }

    /// <summary>通知 null 到空字串仍有意圖，同值無意圖；不可用 IsNullOrEmpty 合併兩種狀態。</summary>
    [Fact]
    public void WeeklyReport_Notification_NullAndEmptyRemainDifferent()
    {
        using var f = new Fixture();
        f.Report["new_paster_reply"] = "";
        var context = f.Context(true);
        context.PreEntityImages["PreImage"].Attributes.Remove("new_paster_reply");
        f.Run(Notification(), context);
        Assert.Equal(2, f.Pushes().Length);
        context.PreEntityImages["PreImage"]["new_paster_reply"] = "";
        f.Run(Notification(), context);
        Assert.Empty(f.Pushes());
    }

    /// <summary>假 SDK 提早拒絕重複正式出席 ID；外殼須原樣傳播，不能補分類或發布週報寫入。</summary>
    [Fact]
    public void WeeklyReport_Statistics_DuplicateRecordRejectedBeforeWrites()
    {
        using var f = new Fixture();
        f.AddAttendance(null, 1, 1);
        f.Attendance.Add(f.Attendance[0]);
        var failure = Assert.Throws<InvalidOperationException>(() => f.Run(Statistics(), f.Context(false)));
        Assert.Equal("Query result identity conflict", failure.Message);
        Assert.Empty(f.Intents());
        Assert.Empty(f.Service.WriteRecords);
    }

    /// <summary>同一外殼並行 A／B 後再 A，各追蹤只屬當案；SDK 在 Dispose 後排空且拒絕再讀。</summary>
    [Fact]
    public async Task WeeklyReport_SharedShell_IsolatesChurchesAndDrains()
    {
        using var a = new Fixture();
        using var b = new Fixture();
        var shell = Notification();
        await Task.WhenAll(Task.Run(() => a.Run(shell, a.Context(true))),
            Task.Run(() => b.Run(shell, b.Context(true))));
        a.Run(shell, a.Context(true));
        Assert.DoesNotContain(b.ContactId.ToString("D"), string.Join("", a.Trace.Entries), StringComparison.Ordinal);
        Assert.DoesNotContain(a.ContactId.ToString("D"), string.Join("", b.Trace.Entries), StringComparison.Ordinal);
        Assert.Equal(2, a.Pushes().Length);
        Assert.Equal(2, b.Pushes().Length);
        a.Dispose();
        Assert.Equal(0, a.Service.EntityCount);
        Assert.Empty(a.Service.WriteRecords);
        Assert.Throws<ObjectDisposedException>(() => a.Service.Retrieve("contact", a.ContactId, new ColumnSet()));
    }

    /// <summary>只建立不可變設定；真服務與本案輸入均在 Execute 中借用。</summary>
    private static WeeklyReportStatisticsPlugin Statistics() => new("{\"mode\":\"Shadow\"}", "");
    /// <summary>Shadow 不需要 secure key；預設通知策略只能記錄本次推播摘要。</summary>
    private static WeeklyReportNotificationPlugin Notification() => new("{\"mode\":\"Shadow\"}", "");

    /// <summary>單案有限合成資料的唯一 owner；不模擬實機授權或資料庫交易。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>metadata 明列的週報表名，非依名稱猜測主鍵。</summary>
        internal const string ReportTable = "new_group_present_weekly_report";
        private readonly Dictionary<Guid, Entity> rows = new();
        private readonly Guid reportId = Guid.NewGuid();
        private readonly Guid listId = Guid.NewGuid();
        private readonly Guid churchId = Guid.NewGuid();
        private readonly Guid channelId = Guid.NewGuid();
        private readonly Guid leaderId = Guid.NewGuid();
        private readonly Guid organizationId = Guid.NewGuid();
        /// <summary>正式合成收件者主鍵；與組長分開以驗證來源教會不來自收件者。</summary>
        internal Guid ContactId { get; } = Guid.NewGuid();
        /// <summary>另一個同名、不同正式 ID 的收件者。</summary>
        internal Guid SecondContactId { get; } = Guid.NewGuid();
        /// <summary>本案週報保存值；測試可顯式注入空值與啟動旗標。</summary>
        internal Entity Report => rows[reportId];
        /// <summary>收件者列，供錯教會及未綁定故障注入。</summary>
        internal Entity Member => rows[ContactId];
        /// <summary>非來源組長的另一位收件者，供獨立教會錯配注入，不改變來源教會鏈。</summary>
        internal Entity SecondMember => rows[SecondContactId];
        /// <summary>有界案例出席集合；不是共享或正式快取。</summary>
        internal List<Entity> Attendance { get; } = new();
        /// <summary>借給真外殼的 SDK，回呼與資料均由 fixture 清除。</summary>
        internal FakeOrganizationService Service { get; } = new();
        /// <summary>每輪 Run 清空的安全追蹤。</summary>
        internal FakeTracingService Trace { get; } = new();
        /// <summary>同步讀取次數，保護深度跳過不作 I/O。</summary>
        internal int ReadCount { get; private set; }
        /// <summary>統計查詢次數，避免只驗空結果而漏跑統計。</summary>
        internal int QueryCount { get; private set; }
        /// <summary>正式頻道讀取次數，保護拒絕先於解密。</summary>
        internal int ChannelReads { get; private set; }
        /// <summary>建立最小合法資料；所有 Retrieve 都拒絕 AllColumns 與原週報 token 欄位。</summary>
        internal Fixture()
        {
            rows[reportId] = new Entity(ReportTable, reportId)
            {
                ["new_saved_flag"] = "計算出席率", ["statecode"] = new OptionSetValue(0),
                ["new_paster_reply"] = "合成回應", ["new_sunday_date"] = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc),
                ["new_list_group_present_weekly_report"] = new EntityReference("list", listId)
            };
            rows[listId] = new Entity("list", listId)
            {
                ["new_contact_family_leader_list"] = new EntityReference("contact", ContactId),
                ["new_contact_list_vice_family_leader"] = new EntityReference("contact", SecondContactId),
                ["new_contact_race_leager_list"] = new EntityReference("contact", ContactId),
                ["new_contact_co_race_leager_list"] = new EntityReference("contact", ContactId),
                ["new_contact_list_arealeader"] = new EntityReference("contact", leaderId) { Name = "區牧" }
            };
            rows[leaderId] = new Entity("contact", leaderId) { ["parentcustomerid"] = new EntityReference("account", churchId) };
            foreach (var id in new[] { ContactId, SecondContactId })
                rows[id] = new Entity("contact", id)
                {
                    ["fullname"] = "同名", ["parentcustomerid"] = new EntityReference("account", churchId),
                    ["new_lineid"] = "U" + new string(id == ContactId ? 'a' : 'b', 32), ["new_preferredlanguage"] = "zh-TW"
                };
            rows[churchId] = new Entity("account", churchId)
            {
                ["name"] = "合成教會", ["new_timezone"] = "Asia/Taipei", ["new_defaultlanguage"] = "zh-TW",
                ["new_enabledlanguages"] = "zh-TW", ["new_linechannelid"] = new EntityReference("new_linechannel", channelId)
            };
            rows[channelId] = new Entity("new_linechannel", channelId) { ["new_accesstokenencrypted"] = "invalid-envelope" };
            Service.RetrieveHandler = (table, id, columns) =>
            {
                Assert.False(columns.AllColumns);
                Assert.DoesNotContain("new_channel_access_token", columns.Columns);
                ReadCount++;
                if (table == "new_linechannel") ChannelReads++;
                var source = rows[id];
                Assert.Equal(table, source.LogicalName);
                var result = new Entity(table, id);
                foreach (var field in columns.Columns)
                    if (source.Contains(field)) result[field] = source[field];
                return result;
            };
            Service.RetrieveMultipleHandler = query =>
            {
                QueryCount++;
                var typed = Assert.IsType<QueryExpression>(query);
                Assert.Equal("new_present_record", typed.EntityName);
                Assert.False(typed.ColumnSet.AllColumns);
                Assert.InRange(typed.PageInfo.Count, 1, 5000);
                Assert.Contains(typed.Criteria.Conditions, c =>
                    c.AttributeName == "new_group_present_weekly_report_prese" && Equals(c.Values[0], reportId));
                return new EntityCollection(Attendance.ToArray());
            };
        }
        /// <summary>加入不同正式出席列及 contact；分類與整數 null 代表 SDK 缺欄位。</summary>
        internal Guid AddAttendance(int? classification, int? sunday, int? group)
        {
            var contact = Guid.NewGuid();
            rows[contact] = new Entity("contact", contact) { ["parentcustomerid"] = new EntityReference("account", churchId) };
            if (classification.HasValue) rows[contact]["customertypecode"] = new OptionSetValue(classification.Value);
            var row = new Entity("new_present_record", Guid.NewGuid())
            {
                ["statecode"] = new OptionSetValue(0), ["new_contact_new_present_record"] = new EntityReference("contact", contact),
                ["new_group_present_weekly_report_prese"] = new EntityReference(ReportTable, reportId),
                ["new_list_new_present_record"] = new EntityReference("list", listId)
            };
            if (sunday.HasValue) row["new_sunday_present_this_week"] = sunday.Value;
            if (group.HasValue) row["new_group_present_this_week"] = group.Value;
            Attendance.Add(row);
            return contact;
        }
        /// <summary>建立真平台輸入形狀；別名固定 PreImage，統計同步、通知非同步。</summary>
        internal RemoteExecutionContext Context(bool notification, int depth = 1)
        {
            var context = new RemoteExecutionContext
            {
                MessageName = "Update", PrimaryEntityName = ReportTable, PrimaryEntityId = reportId,
                OrganizationId = organizationId, OrganizationName = "synthetic", UserId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid(), Stage = 40, Mode = notification ? 1 : 0, Depth = depth
            };
            context.InputParameters["Target"] = new Entity(ReportTable, reportId) { ["new_paster_reply"] = Report["new_paster_reply"] };
            context.PreEntityImages["PreImage"] = new Entity(ReportTable, reportId) { ["new_paster_reply"] = "先前回應", ["new_saved_flag"] = "old" };
            return context;
        }
        /// <summary>每次執行借用當次 provider 與 factory，返回前所有產品工作已排空。</summary>
        internal void Run(IPlugin plugin, RemoteExecutionContext context)
        {
            Trace.Entries.Clear();
            plugin.Execute(new FakeServiceProvider().Add<ITracingService>(Trace)
                .Add<IPluginExecutionContext>(context)
                .Add<IOrganizationServiceFactory>(new Factory(Service, context.UserId)));
        }
        /// <summary>只解析版本化安全追蹤；錯誤型別追蹤不是影子意圖。</summary>
        internal JObject[] Lines() => Trace.Entries.Where(x => x.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
            .Select(x => JObject.Parse(x.Substring("SMV-SHADOW|1|".Length))).ToArray();
        /// <summary>只傳回已發布操作，讓零寫入拒絕不會被 end 記錄誤判。</summary>
        internal JObject[] Intents() => Lines().Where(x => x["operation"] is not null).ToArray();
        /// <summary>推播安全摘要，不含可變產品資料、原文或憑證。</summary>
        internal JObject[] Pushes() => Intents().Where(x => (string?)x["operation"] == "Push").ToArray();
        /// <summary>先排空 SDK 回呼與 audit，再清除本案列與 trace；可重複 Dispose。</summary>
        public void Dispose() { Service.Dispose(); Attendance.Clear(); rows.Clear(); Trace.Entries.Clear(); }
        /// <summary>只借給當次平台使用者的 SDK factory，不具全域服務或提升權限能力。</summary>
        private sealed class Factory(IOrganizationService service, Guid user) : IOrganizationServiceFactory
        {
            /// <summary>驗證請求使用者與本次 context 相同；平台 SDK 由 fixture owner 處置。</summary>
            public IOrganizationService CreateOrganizationService(Guid? userId) => userId == user
                ? service : throw new InvalidOperationException("Unexpected service user.");
        }
    }
}
