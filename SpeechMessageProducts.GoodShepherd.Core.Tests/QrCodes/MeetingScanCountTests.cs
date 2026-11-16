/*
 * 檔案整體說明
 * 目的與定位：P5-09 掃描統計的離線雙宿主測試，直接執行正式 Core 服務而非測試內重寫規則。
 * 主要功能與流程：依 reading 的獨立三十八對欄位驗證非空列數、首堂簽退特殊拼字、
 * 同名異 ID、權威 meeting、完整分頁及一次最小 patch；注入後頁失敗、身分衝突及超量，
 * 決定性斷言都是失敗前零 Update，SDK 原例外保留，同一實例重試不保留上輪候選。
 * 使用邊界與維護：僅用合成 SDK Entity 與嚴格 IOrganizationService 替身，不連 D365、
 * 不讀秘密、不冒充舊活動實跑。每案例 using 擁有替身並清除列、委派與寫入參照；
 * 正式服務只能借用替身，不得 Dispose。並行測試各自擁有來源，沒有跨案例可變狀態。
 */
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.QrCodes;

/// <summary>保護 reading 計數與核定安全差異；所有故障均在離線邊界注入。</summary>
public sealed class MeetingScanCountTests
{
    /// <summary>獨立閱讀基準，三十八組不取自產品；缺字或錯配會直接改變對應欄位斷言。</summary>
    private static (string Source, string Target)[] ReadingPairs() => new[]
    {
        ("new_sunday_first_qr_on_time", "new_sunday_first_qr_on_number"),
        ("new_sunday_second_qr_on_time", "new_sunday_second_qr_on_number"),
        ("new_saturday_worship_on_time", "new_saturday_worship_on_number"),
        ("new_yongmen_on_time", "new_yongmen_on_number"),
        ("new_prayer_meeting_on_time", "new_prayer_meeting_qr_on_number"),
        ("new_child_on_time", "new_child_on_number"),
        ("new_big_disciple_on_time", "new_big_disciple_qr_on_number"),
        ("new_leadership_small_lecture_on_time", "new_leadership_small_lecture_qr_on_number"),
        ("new_leaders_gather_on_time", "new_leaders_gather_qr_on_number"),
        ("new_sunday_first_qr_off", "new_sunday_first_qr_off_number"),
        ("new_sunday_second_qr_off_time", "new_sunday_second_qr_off_number"),
        ("new_saturday_worship_off_time", "new_saturday_worship_off_number"),
        ("new_yongmen_off_time", "new_yongmen_off_number"),
        ("new_prayer_meeting_off_time", "new_prayer_meeting_qr_off_number"),
        ("new_child_off_time", "new_child_off_number"),
        ("new_big_disciple_off_time", "new_big_disciple_qr_off_number"),
        ("new_leadership_small_lecture_off_time", "new_leadership_small_lecture_qr_off_number"),
        ("new_leaders_gather_off_time", "new_leaders_gather_qr_off_number"),
        ("new_1_sign_on_time", "new_1_sign_on_number"), ("new_1_sign_off_time", "new_1_sign_off_number"),
        ("new_2_sign_on_time", "new_2_sign_on_number"), ("new_2_sign_off_time", "new_2_sign_off_number"),
        ("new_3_sign_on_time", "new_3_sign_on_number"), ("new_3_sign_off_time", "new_3_sign_off_number"),
        ("new_4_sign_on_time", "new_4_sign_on_number"), ("new_4_sign_off_time", "new_4_sign_off_number"),
        ("new_5_sign_on_time", "new_5_sign_on_number"), ("new_5_sign_off_time", "new_5_sign_off_number"),
        ("new_6_sign_on_time", "new_6_sign_on_number"), ("new_6_sign_off_time", "new_6_sign_off_number"),
        ("new_7_sign_on_time", "new_7_sign_on_number"), ("new_7_sign_off_time", "new_7_sign_off_number"),
        ("new_8_sign_on_time", "new_8_sign_on_number"), ("new_8_sign_off_time", "new_8_sign_off_number"),
        ("new_9_sign_on_time", "new_9_sign_on_number"), ("new_9_sign_off_time", "new_9_sign_off_number"),
        ("new_10_sign_on_time", "new_10_sign_on_number"), ("new_10_sign_off_time", "new_10_sign_off_number")
    };

    /// <summary>每組有唯一的 1..38 筆期望，完整比對欄位、零增量、最小 patch 及原來源不變。</summary>
    [Fact]
    public void QrCodes_ScanCount_MatchesLegacy()
    {
        using var sdk = new ScanSdk();
        var pairs = ReadingPairs();
        for (var i = 0; i < pairs.Length; i++)
        {
            var row = Row(sdk.Id);
            for (var j = i; j < pairs.Length; j++) row[pairs[j].Source] = new DateTime(2040, 1, 1);
            row["new_sunday_first_qr_off_time"] = new DateTime(2040, 1, 1);
            sdk.Rows.Add(row);
        }
        var sut = new MeetingScanCountService();
        sut.Execute(sdk, sdk.Id);
        sut.Execute(sdk, sdk.Id);
        Assert.Equal(2, sdk.Updates.Count);
        foreach (var patch in sdk.Updates)
        {
            Assert.Equal("new_meeting_statistics", patch.LogicalName);
            Assert.Equal(sdk.Id, patch.Id);
            Assert.Equal(38, patch.Attributes.Count);
            for (var i = 0; i < pairs.Length; i++) Assert.Equal(i + 1, Assert.IsType<int>(patch[pairs[i].Target]));
            Assert.Empty(patch.KeyAttributes);
        }
        Assert.Equal(999, sdk.Meeting["new_sunday_first_qr_on_number"]);
        Assert.False(sdk.Disposed);
    }

    /// <summary>不同正式 ID 即使同名同會員也逐列計數；缺值及 null 不算，false、0 仍是非空。</summary>
    [Fact]
    public void SameNameDifferentIds_AndNonNullSemantics_ArePreserved()
    {
        using var sdk = new ScanSdk();
        var contact = new EntityReference("contact", Guid.NewGuid());
        foreach (var value in new object[] { false, 0 })
        {
            var row = Row(sdk.Id);
            row["new_name"] = "同名"; row["new_contact_new_present_record"] = contact;
            row["new_sunday_first_qr_on_time"] = value;
            row["new_sunday_first_qr_off"] = null;
            row["new_sunday_first_qr_off_time"] = new DateTime(2026, 1, 1);
            sdk.Rows.Add(row);
        }
        new MeetingScanCountService().Execute(sdk, sdk.Id);
        var patch = Assert.Single(sdk.Updates);
        Assert.Equal(2, patch["new_sunday_first_qr_on_number"]);
        Assert.Equal(0, patch["new_sunday_first_qr_off_number"]);
        Assert.All(ReadingPairs().Skip(1), pair => Assert.Equal(0, patch[pair.Target]));
    }

    /// <summary>空集合仍一次覆寫全部零，不能保留 Retrieve 中的舊計數或其他欄位。</summary>
    [Fact]
    public void EmptySet_UpdatesAllZeroCountsOnce()
    {
        using var sdk = new ScanSdk();
        new MeetingScanCountService().Execute(sdk, sdk.Id);
        var patch = Assert.Single(sdk.Updates);
        Assert.Equal(38, patch.Attributes.Count);
        Assert.All(patch.Attributes, field => Assert.Equal(0, Assert.IsType<int>(field.Value)));
    }

    /// <summary>跨頁重複也拒絕；不能用 HashSet 略過重複後發布看似完整計數。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateDatabaseId_RejectsBeforeUpdate(bool crossPage)
    {
        using var sdk = new ScanSdk();
        var row = Row(sdk.Id);
        sdk.Page = q => new EntityCollection(new[] { row, row }.Take(crossPage ? 1 : 2).ToList())
        { MoreRecords = crossPage && q.PageInfo.PageNumber == 1, PagingCookie = "next" };
        Assert.Throws<InvalidOperationException>(() => new MeetingScanCountService().Execute(sdk, sdk.Id));
        Assert.Empty(sdk.Updates);
    }

    /// <summary>空／錯表／主鍵屬性衝突與錯 meeting lookup 都不能由 caller 的參數掩蓋。</summary>
    [Theory]
    [InlineData("empty")]
    [InlineData("table")]
    [InlineData("primary")]
    [InlineData("scope")]
    [InlineData("missingScope")]
    [InlineData("null")]
    public void InvalidAttendanceIdentity_RejectsBeforeUpdate(string fault)
    {
        using var sdk = new ScanSdk();
        var row = Row(sdk.Id);
        switch (fault)
        {
            case "empty": row.Id = Guid.Empty; break;
            case "table": row.LogicalName = "contact"; break;
            case "primary": row["new_present_recordid"] = Guid.NewGuid(); break;
            case "scope": row["new_meeting_statistics_new_present_re"] = new EntityReference("new_meeting_statistics", Guid.NewGuid()); break;
            case "missingScope": row.Attributes.Remove("new_meeting_statistics_new_present_re"); break;
        }
        sdk.Rows.Add(fault == "null" ? null! : row);
        Assert.Throws<InvalidOperationException>(() => new MeetingScanCountService().Execute(sdk, sdk.Id));
        Assert.Empty(sdk.Updates);
    }

    /// <summary>平台讀回的權威 meeting 必須與請求同表同 ID；失敗時連出席查詢都不得開始。</summary>
    [Theory]
    [InlineData("empty")]
    [InlineData("different")]
    [InlineData("table")]
    [InlineData("null")]
    public void AuthoritativeMeetingRequired_BeforeQuery(string fault)
    {
        using var sdk = new ScanSdk();
        sdk.Meeting = fault switch
        {
            "empty" => new Entity("new_meeting_statistics"),
            "different" => new Entity("new_meeting_statistics", Guid.NewGuid()),
            "table" => new Entity("contact", sdk.Id),
            _ => null!
        };
        Assert.Throws<InvalidOperationException>(() => new MeetingScanCountService().Execute(sdk, sdk.Id));
        Assert.Equal(0, sdk.QueryCalls); Assert.Empty(sdk.Updates);
    }

    /// <summary>後頁真實故障原樣拋出且零寫入；同服務明確重試只計本輪資料。</summary>
    [Fact]
    public void LaterQueryFault_DoesNotPublish_AndRetryIsClean()
    {
        using var sdk = new ScanSdk();
        var error = new InvalidOperationException("synthetic failure");
        sdk.Page = q => q.PageInfo.PageNumber == 1
            ? new EntityCollection(new[] { Row(sdk.Id) }) { MoreRecords = true, PagingCookie = "next" }
            : throw error;
        var sut = new MeetingScanCountService();
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => sut.Execute(sdk, sdk.Id)));
        Assert.Empty(sdk.Updates);
        sdk.Page = null;
        sut.Execute(sdk, sdk.Id);
        Assert.All(Assert.Single(sdk.Updates).Attributes, field => Assert.Equal(0, field.Value));
    }

    /// <summary>cookie 必須原樣續傳；沒有 cookie 則用頁碼繼續，不把合法簡單分頁當截斷。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Paging_ReadsCompleteSetWithStableIdentityOrder(bool withCookie)
    {
        using var sdk = new ScanSdk();
        sdk.Page = q =>
        {
            Assert.Equal(q.PageInfo.PageNumber == 1 || !withCookie ? null : "<cookie synthetic='1' />", q.PageInfo.PagingCookie);
            var row = Row(sdk.Id); row["new_10_sign_off_time"] = new DateTime(2000, 1, 1);
            return new EntityCollection(new[] { row })
            { MoreRecords = q.PageInfo.PageNumber == 1, PagingCookie = withCookie ? "<cookie synthetic='1' />" : null };
        };
        new MeetingScanCountService().Execute(sdk, sdk.Id);
        Assert.Equal(2, sdk.QueryCalls);
        Assert.Equal(2, Assert.Single(sdk.Updates)["new_10_sign_off_number"]);
    }

    /// <summary>列數恰一萬成功，超量整輪拒絕；不回傳上限內的部分統計。</summary>
    [Theory]
    [InlineData(10000, true)]
    [InlineData(10001, false)]
    public void RowBudget_IsBoundedWithoutTruncation(int count, bool success)
    {
        using var sdk = new ScanSdk();
        for (var i = 0; i < count; i++) sdk.Rows.Add(Row(sdk.Id));
        if (success) new MeetingScanCountService().Execute(sdk, sdk.Id);
        else Assert.Throws<InvalidOperationException>(() => new MeetingScanCountService().Execute(sdk, sdk.Id));
        Assert.Equal(success ? 1 : 0, sdk.Updates.Count);
        Assert.InRange(sdk.QueryCalls, 20, 21);
    }

    /// <summary>頁數、空進度、重複／超長 cookie、null 回應與過大單頁都有固定拒絕路徑。</summary>
    [Theory]
    [InlineData("pages")]
    [InlineData("empty")]
    [InlineData("cookie")]
    [InlineData("longCookie")]
    [InlineData("null")]
    [InlineData("oversized")]
    public void MalformedPaging_RejectsBeforePublication(string fault)
    {
        using var sdk = new ScanSdk();
        sdk.Page = _ => fault == "null" ? null! : new EntityCollection(
            Enumerable.Range(0, fault == "empty" ? 0 : fault == "oversized" ? 501 : 1).Select(_ => Row(sdk.Id)).ToList())
        { MoreRecords = true, PagingCookie = fault == "cookie" ? "repeated" : fault == "longCookie" ? new string('c', 32769) : null };
        Assert.Throws<InvalidOperationException>(() => new MeetingScanCountService().Execute(sdk, sdk.Id));
        Assert.Empty(sdk.Updates); Assert.InRange(sdk.QueryCalls, 1, 100);
    }

    /// <summary>Update 回應不確定時保留原 fault、不自動重試；不可聲稱伺服器已回滾。</summary>
    [Fact]
    public void UpdateFault_IsNotRetriedOrReplaced()
    {
        using var sdk = new ScanSdk();
        var error = new InvalidOperationException("synthetic update"); sdk.UpdateError = error;
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => new MeetingScanCountService().Execute(sdk, sdk.Id)));
        Assert.Single(sdk.Updates);
    }

    /// <summary>非法輸入先於任何 SDK I/O；由呼叫端驗證後的服務仍保有原生授權範圍。</summary>
    [Fact]
    public void Arguments_RejectBeforeIo()
    {
        using var sdk = new ScanSdk(); var sut = new MeetingScanCountService();
        Assert.Throws<ArgumentNullException>(() => sut.Execute(null!, sdk.Id));
        Assert.Throws<ArgumentException>(() => sut.Execute(sdk, Guid.Empty));
        Assert.Equal(0, sdk.RetrieveCalls); Assert.Empty(sdk.Updates);
    }

    /// <summary>共享無狀態規則進行一千次 A/B 並行呼叫，每次 SDK 的會議與計數保持隔離，離開 using 清空來源。</summary>
    [Fact]
    public void ConcurrentScopes_AndRepeatedCalls_HaveNoRetainedSdkState()
    {
        var sut = new MeetingScanCountService();
        Parallel.For(0, 1000, i =>
        {
            using var sdk = new ScanSdk();
            for (var j = 0; j < i % 2; j++)
            {
                var row = Row(sdk.Id); row["new_child_off_time"] = 0; sdk.Rows.Add(row);
            }
            sut.Execute(sdk, sdk.Id);
            Assert.Equal(i % 2, Assert.Single(sdk.Updates)["new_child_off_number"]);
            Assert.False(sdk.Disposed);
            sdk.Dispose();
            Assert.Empty(sdk.Rows); Assert.Empty(sdk.Updates); Assert.Null(sdk.Page);
        });
    }

    /// <summary>規則實例存活時，已排空 SDK、來源與 patch 都應可回收，不能被服務欄位跨請求保存。</summary>
    [Fact]
    public void DrainedOperation_DoesNotRetainSdkOrEntities()
    {
        var sut = new MeetingScanCountService();
        var references = ExecuteAndRelease(sut);
        for (var i = 0; i < 3 && references.Any(reference => reference.IsAlive); i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        GC.KeepAlive(sut);
    }

    /// <summary>隔離 JIT 堆疊存活範圍；using 清空替身，回傳弱參照而不把測試自身變成資源保留者。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] ExecuteAndRelease(MeetingScanCountService sut)
    {
        using var sdk = new ScanSdk();
        var row = Row(sdk.Id); sdk.Rows.Add(row);
        sut.Execute(sdk, sdk.Id);
        return new[] { new WeakReference(sdk), new WeakReference(row), new WeakReference(sdk.Meeting), new WeakReference(Assert.Single(sdk.Updates)) };
    }

    /// <summary>建立同範圍正式資料列，不依姓名或測試位置冒充資料庫身分。</summary>
    private static Entity Row(Guid meeting) => new("new_present_record", Guid.NewGuid())
    { ["new_meeting_statistics_new_present_re"] = new EntityReference("new_meeting_statistics", meeting) };

    /// <summary>同步 SDK 邊界替身；只記錄本案例資料，沒有網路、背景工作或非受控資源，owner 用 Dispose 清參照。</summary>
    private sealed class ScanSdk : IOrganizationService, IDisposable
    {
        /// <summary>每個來源獨立正式會議 ID，防止共享規則讀到上一位呼叫者資料。</summary>
        internal Guid Id { get; } = Guid.NewGuid();
        /// <summary>合成權威會議；不相關欄位不得被正式服務直接回寫。</summary>
        internal Entity Meeting { get; set; }
        /// <summary>由案例獨占的出席來源；不交給跨案例快取。</summary>
        internal List<Entity> Rows { get; } = new();
        /// <summary>記錄 Update payload 以驗證單一發布點及不相關欄位未寫入。</summary>
        internal List<Entity> Updates { get; } = new();
        /// <summary>只存本案例同步故障注入委派，Dispose 清除。</summary>
        internal Func<QueryExpression, EntityCollection>? Page { get; set; }
        /// <summary>原始寫入故障實例，產品必須原樣傳播給外殼告警。</summary>
        internal Exception? UpdateError { get; set; }
        /// <summary>查詢呼叫數，證明有界且錯誤後不繼續發布。</summary>
        internal int QueryCalls { get; private set; }
        /// <summary>權威 Retrieve 次數，驗證非法輸入不進 SDK。</summary>
        internal int RetrieveCalls { get; private set; }
        /// <summary>服務不擁有 SDK，Execute 之後不得自行設定此旗標。</summary>
        internal bool Disposed { get; private set; }
        /// <summary>初始化帶既有計數與特殊字元名稱的會議，確保不依名稱拼接查詢。</summary>
        internal ScanSdk() => Meeting = new Entity("new_meeting_statistics", Id)
        { ["name"] = "合成 & <會議>'", ["new_sunday_first_qr_on_number"] = 999 };
        /// <summary>只允許主鍵最小投影，避免不必要載入可變或敏感欄位。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Assert.False(Disposed); RetrieveCalls++;
            Assert.Equal("new_meeting_statistics", entityName); Assert.Equal(Id, id);
            Assert.False(columnSet.AllColumns); Assert.Equal("new_meeting_statisticsid", Assert.Single(columnSet.Columns));
            return Meeting;
        }
        /// <summary>嚴格核對明確 lookup AND、無額外日期／statecode 過濾、四十欄投影與正式 ID 排序。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            Assert.False(Disposed); QueryCalls++;
            var q = Assert.IsType<QueryExpression>(query);
            Assert.Equal("new_present_record", q.EntityName); Assert.False(q.ColumnSet.AllColumns);
            var columns = ReadingPairs().Select(x => x.Source).ToList();
            columns.Add("new_present_recordid"); columns.Add("new_meeting_statistics_new_present_re");
            Assert.Equal(columns.OrderBy(x => x, StringComparer.Ordinal), q.ColumnSet.Columns.OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(LogicalOperator.And, q.Criteria.FilterOperator); Assert.Empty(q.Criteria.Filters);
            var condition = Assert.Single(q.Criteria.Conditions);
            Assert.Equal("new_meeting_statistics_new_present_re", condition.AttributeName);
            Assert.Equal(ConditionOperator.Equal, condition.Operator); Assert.Equal(Id, Assert.Single(condition.Values));
            Assert.Empty(q.LinkEntities); Assert.False(q.Distinct); Assert.Null(q.TopCount);
            var order = Assert.Single(q.Orders); Assert.Equal("new_present_recordid", order.AttributeName);
            Assert.Equal(OrderType.Ascending, order.OrderType); Assert.Equal(500, q.PageInfo.Count);
            if (Page is not null) return Page(q);
            var skip = (q.PageInfo.PageNumber - 1) * 500;
            return new EntityCollection(Rows.Skip(skip).Take(500).ToList())
            { MoreRecords = skip + 500 < Rows.Count, PagingCookie = (skip + 500).ToString(CultureInfo.InvariantCulture) };
        }
        /// <summary>唯一允許的寫入；先記錄嘗試再注入故障，不能把未知回應當成零次寫入。</summary>
        public void Update(Entity entity) { Assert.False(Disposed); Updates.Add(entity); if (UpdateError is not null) throw UpdateError; }
        /// <summary>由案例 owner 排空參照，可重複呼叫；不涉及真實 SDK 的 Dispose 行為。</summary>
        public void Dispose() { Rows.Clear(); Updates.Clear(); Page = null; UpdateError = null; Meeting = null!; Disposed = true; }
        /// <summary>統計服務不得建立資料列；違反即失敗。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>統計服務不得刪除資料列；違反即失敗。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>統計服務不需其他訊息或交易；違反即失敗。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        /// <summary>統計服務不得改變關聯；違反即失敗。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>統計服務不得移除關聯；違反即失敗。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
