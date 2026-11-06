using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Localization;
using Xunit;

namespace ToolUtility.Tests.DataOperations;

/// <summary>以合成 SDK 頁面保護完整覆寫來源；斷言查詢範圍、正式 ID、完整分頁及 lease 歸還，不宣稱實機整合。</summary>
public sealed class ChurchTextOverrideSourceTests
{
    /// <summary>兩頁同顯示文字但不同資料庫 ID／key 均保留，查詢不得漏教會條件、穩定排序或指定欄位。</summary>
    [Fact]
    public async Task ReadAsync_CompletePages_AreScopedAndDetached()
    {
        var context = Context();
        var first = Row(context, "Common.Diagnostics.Ping");
        var second = Row(context, "Common.Diagnostics.Culture");
        var calls = 0;
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase raw) =>
        {
            var query = Assert.IsType<QueryExpression>(raw);
            Assert.Equal("new_localizedtext", query.EntityName);
            var condition = Assert.Single(query.Criteria.Conditions);
            Assert.Equal("new_accountid", condition.AttributeName);
            Assert.Equal(ConditionOperator.Equal, condition.Operator);
            Assert.Equal(context.ChurchId, Assert.Single(condition.Values));
            Assert.False(query.ColumnSet.AllColumns);
            Assert.Equal(new[] { "new_accountid", "new_culture", "new_key", "new_text" }, query.ColumnSet.Columns.OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal("new_localizedtextid", Assert.Single(query.Orders).AttributeName);
            Assert.Equal(OrderType.Ascending, query.Orders[0].OrderType);
            ++calls;
            Assert.Equal(calls, query.PageInfo.PageNumber);
            Assert.Equal(calls == 1 ? null : "page-2", query.PageInfo.PagingCookie);
            return new EntityCollection(new[] { calls == 1 ? first : second }.ToList())
            { MoreRecords = calls == 1, PagingCookie = calls == 1 ? "page-2" : null };
        });
        var factory = new Factory(context, sdk.Object);
        var rows = await new D365ChurchTextOverrideSource(factory).ReadAsync(context, CancellationToken.None);
        Assert.Equal(2, calls);
        Assert.Equal(1, factory.Disposed);
        Assert.Equal(new[] { first.Id, second.Id }, rows.Select(x => x.Id));
        first["new_text"] = "mutated";
        Assert.All(rows, x => Assert.Equal("same text", x.Text));
        Assert.False(rows is System.Collections.Generic.List<ChurchTextOverride>);
    }

    /// <summary>逐一注入缺 ID、外教會、錯表、重複正式 ID、缺欄位、超 schema 文字，整份候選必須失敗且只歸還一次 lease。</summary>
    /// <param name="fault">固定測試故障代碼，不含真實資料。</param>
    [Theory]
    [InlineData("empty-id")]
    [InlineData("foreign-church")]
    [InlineData("foreign-table")]
    [InlineData("duplicate-id")]
    [InlineData("missing-key")]
    [InlineData("missing-text")]
    [InlineData("wrong-lookup-type")]
    [InlineData("text-budget")]
    [InlineData("culture-budget")]
    [InlineData("key-budget")]
    public async Task ReadAsync_InvalidCandidate_RejectsWholeResultAndDisposes(string fault)
    {
        var context = Context();
        var row = Row(context, "Common.Diagnostics.Ping");
        switch (fault)
        {
            case "empty-id": row.Id = Guid.Empty; break;
            case "foreign-church": row["new_accountid"] = new EntityReference("account", Guid.NewGuid()); break;
            case "foreign-table": row.LogicalName = "account"; break;
            case "missing-key": row.Attributes.Remove("new_key"); break;
            case "missing-text": row.Attributes.Remove("new_text"); break;
            case "wrong-lookup-type": row["new_accountid"] = new EntityReference("contact", context.ChurchId); break;
            case "text-budget": row["new_text"] = new string('x', 2001); break;
            case "culture-budget": row["new_culture"] = new string('x', 17); break;
            case "key-budget": row["new_key"] = "Common." + new string('A', 194); break;
        }
        var page = new EntityCollection();
        page.Entities.Add(row);
        if (fault == "duplicate-id") page.Entities.Add(row);
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(page);
        var factory = new Factory(context, sdk.Object);
        await Assert.ThrowsAnyAsync<Exception>(() => new D365ChurchTextOverrideSource(factory).ReadAsync(context, CancellationToken.None));
        Assert.Equal(1, factory.Disposed);
    }

    /// <summary>組織、產品或教會任一 lease scope 不同，在送 SDK 之前拒絕；語系差異不改授權資料範圍。</summary>
    /// <param name="boundary">要改變的伺服器邊界。</param>
    [Theory]
    [InlineData("organization")]
    [InlineData("product")]
    [InlineData("church")]
    public async Task ReadAsync_ForeignLease_RejectsBeforeIo(string boundary)
    {
        var context = Context();
        var foreign = new TextContext(boundary == "church" ? Guid.NewGuid() : context.ChurchId, "en", "en-US",
            boundary == "organization" ? "Beta" : context.Organization, boundary == "product" ? "Other" : context.Product);
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var factory = new Factory(foreign, sdk.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new D365ChurchTextOverrideSource(factory).ReadAsync(context, CancellationToken.None));
        Assert.Equal(1, factory.Disposed);
        sdk.VerifyNoOtherCalls();
    }

    /// <summary>後頁 SDK 故障必須保留原例外而非回傳第一頁；下一次可重讀且兩次 lease 均完成釋放。</summary>
    [Fact]
    public async Task ReadAsync_LateFailure_PreservesOriginalAndRetries()
    {
        var context = Context();
        var failure = new InvalidOperationException("synthetic sensitive content");
        var calls = 0;
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() =>
        {
            ++calls;
            if (calls == 2) throw failure;
            return new EntityCollection(new[] { Row(context, "Common.Diagnostics.Ping") }.ToList()) { MoreRecords = calls == 1, PagingCookie = "p2" };
        });
        var factory = new Factory(context, sdk.Object);
        var source = new D365ChurchTextOverrideSource(factory);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAsync(context, CancellationToken.None)));
        Assert.Single(await source.ReadAsync(context, CancellationToken.None));
        Assert.Equal(2, factory.Disposed);
    }

    /// <summary>進場前 caller 取消不取得 lease；I/O 入場後取消則等待同步 SDK 回來才歸還，不留下背景操作。</summary>
    [Fact]
    public async Task ReadAsync_Cancellation_DrainsAndReturnsLease()
    {
        var context = Context();
        using var cancellation = new CancellationTokenSource();
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() =>
        {
            cancellation.Cancel();
            return new EntityCollection();
        });
        var factory = new Factory(context, sdk.Object);
        var source = new D365ChurchTextOverrideSource(factory);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAsync(context, cancellation.Token));
        Assert.Equal(1, factory.Opened);
        Assert.Equal(1, factory.Disposed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAsync(context, cancellation.Token));
        Assert.Equal(1, factory.Opened);
    }

    /// <summary>重複續頁 cookie 必須有界拒絕，不進入無限 SDK 查詢。</summary>
    [Fact]
    public async Task ReadAsync_RepeatedCookie_StopsAndDisposes()
    {
        var context = Context();
        var calls = 0;
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() =>
        {
            ++calls;
            return new EntityCollection(new[] { Row(context, "Common.Diagnostics.Ping") }.ToList()) { MoreRecords = true, PagingCookie = "same" };
        });
        var factory = new Factory(context, sdk.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new D365ChurchTextOverrideSource(factory).ReadAsync(context, CancellationToken.None));
        Assert.Equal(2, calls);
        Assert.Equal(1, factory.Disposed);
    }

    /// <summary>每案獨立的合成已授權教會，不保存任何 static 可變身分。</summary>
    private static TextContext Context() => new(Guid.NewGuid(), "zh-TW", "zh-TW", "Alpha", "ChurchReport");

    /// <summary>建立合成正式列；相同顯示文字不等於同資料庫記錄。</summary>
    private static Entity Row(TextContext context, string key) => new("new_localizedtext", Guid.NewGuid())
    {
        ["new_accountid"] = new EntityReference("account", context.ChurchId),
        ["new_key"] = key, ["new_culture"] = "*", ["new_text"] = "same text"
    };

    /// <summary>操作 lease 工廠只供本案 SDK；計數證明每次 Open 恰一次 Dispose，不處置 Moq SDK。</summary>
    private sealed class Factory : IChurchTextOverrideServiceFactory
    {
        private readonly TextContext context;
        private readonly IOrganizationService service;
        internal int Opened;
        internal int Disposed;
        /// <summary>借用本案固定scope與SDK，實際釋放僅計操作lease，工廠不取得SDK所有權。</summary>
        internal Factory(TextContext context, IOrganizationService service) { this.context = context; this.service = service; }
        /// <summary>只建立本案 lease，不取得連線；尊重 caller 取消且計數只在成功入場時增加。</summary>
        public IChurchTextOverrideServiceLease Open(TextContext requested, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ++Opened;
            return new Lease(this);
        }

        /// <summary>只借用本案 SDK，單一操作釋放後清除 owner 引用，以重複 Dispose 亦不能重複計數。</summary>
        private sealed class Lease : IChurchTextOverrideServiceLease
        {
            private Factory? owner;
            /// <summary>綁定單一操作owner；來源讀完或失敗後必須由using解除引用。</summary>
            internal Lease(Factory owner) => this.owner = owner;
            /// <summary>回傳本案工廠的真實範圍，供產品核對foreign lease，不能直接用requested假裝驗證。</summary>
            public TextContext Context => owner!.context;
            /// <summary>只借用本案合成SDK，不將任何連線或服務交給快照。</summary>
            public IOrganizationService Service => owner!.service;
            /// <summary>本案純 SDK 替身沒有實際連線池；淘汰次數由 manager adapter 的獨立測試精確驗證。</summary>
            public void MarkFaulted() { }
            /// <summary>每個lease最多一次歸還；先斷owner參照才計數，重複Dispose不得再增加。</summary>
            public void Dispose() { if (owner is { } value) { owner = null; ++value.Disposed; } }
        }
    }
}
