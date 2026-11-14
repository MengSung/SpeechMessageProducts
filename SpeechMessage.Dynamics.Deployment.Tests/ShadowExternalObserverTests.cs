/*
 * 檔案整體說明
 * 目的與定位：保護舊輪外部既有記錄摘要的範圍、正式身分、完整性及短期保留契約。
 * 主要功能與流程：每案獨占合成SDK與記錄；以相同名稱但不同ID、版本更新、離開
 * 教會範圍及新增列，驗證只計觸發前既有列。SDK只允許精確教會查詢和三個欄位，
 * 所有寫入均丟錯，確保產品不刪除或回復既有列。分頁、重複ID、錯教會與缺版本
 * 的故障注入必須拒絕，不能產生看似零變動的部分報告。
 * 使用邊界與維護：合成替身不連D365、不發LINE，數字不代表實機結果；測試沒有
 * stream、連線或背景資源。observer由using唯一擁有，Dispose後查詢必須拒絕，
 * 不可保留另一案的ID／版本、服務回傳Entity或取消來源。新增型別及所有測試
 * 同步維護UTF-8無BOM、CRLF與最後CRLF，執行聚焦測試及文件守門。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以真observer驗證外部既有列變動，不以回傳固定統計的假observer代替。</summary>
public sealed class ShadowExternalObserverTests
{
    /// <summary>同名不同ID完整保留；一次版本更新及一次離開範圍計兩筆，新建列不算既有列。</summary>
    [Fact]
    public void ExternalObserver_CountsStableExistingIdentitiesAndDetachesSnapshot()
    {
        var service = new ReadService();
        var first = service.Row(1);
        var second = service.Row(1);
        var unchanged = service.Row(1);
        service.Rows.AddRange([first, second, unchanged]);
        using var observer = new ShadowExternalObserver(service, service.Church, [new("new_group", "new_church", "new_groupid")]);
        observer.Capture(CancellationToken.None);
        first["versionnumber"] = 2L;
        service.Rows.Remove(second);
        service.Rows.Add(service.Row(1));
        var change = Assert.Single(observer.Compare(CancellationToken.None));
        Assert.Equal("new_group", change.Entity);
        Assert.Equal(2, change.ChangedExistingCount);
        Assert.Equal(2, service.Reads);
        Assert.Equal(3, service.Rows.Count);
        Assert.Equal(unchanged.Id, service.Rows[1].Id);
    }

    /// <summary>空的明確盤點範圍不查表，回傳無宣告實體；不得靠全組織掃描填入猜測數字。</summary>
    [Fact]
    public void ExternalObserver_ExplicitEmptyScopeHasNoQueries()
    {
        var service = new ReadService();
        using var observer = new ShadowExternalObserver(service, service.Church, []);
        observer.Capture(CancellationToken.None);
        Assert.Empty(observer.Compare(CancellationToken.None));
        Assert.Equal(0, service.Reads);
    }

    /// <summary>兩個owner互不共用快照；同一服務列被改寫不影響另一案先前捕獲的版本。</summary>
    [Fact]
    public void ExternalObserver_IndependentOwnersDoNotShareBaseline()
    {
        var service = new ReadService();
        var row = service.Row(1); service.Rows.Add(row);
        using var first = new ShadowExternalObserver(service, service.Church, [new("new_group", "new_church", "new_groupid")]);
        using var second = new ShadowExternalObserver(service, service.Church, [new("new_group", "new_church", "new_groupid")]);
        first.Capture(CancellationToken.None);
        row["versionnumber"] = 2L;
        second.Capture(CancellationToken.None);
        Assert.Equal(1, Assert.Single(first.Compare(CancellationToken.None)).ChangedExistingCount);
        Assert.Equal(0, Assert.Single(second.Compare(CancellationToken.None)).ChangedExistingCount);
    }

    /// <summary>非完整或越界SDK回應在第一次快照即拒絕；不回傳部分計數或以缺版本代替零。</summary>
    /// <param name="fault">每案注入一項回應契約故障。</param>
    [Theory]
    [InlineData("page")]
    [InlineData("overflow")]
    [InlineData("duplicate")]
    [InlineData("empty-id")]
    [InlineData("wrong-entity")]
    [InlineData("wrong-church")]
    [InlineData("missing-version")]
    [InlineData("wrong-version-type")]
    [InlineData("wrong-primary")]
    public void ExternalObserver_RejectsIncompleteOrCrossChurchResponse(string fault)
    {
        var service = new ReadService { Fault = fault };
        service.Rows.Add(service.Row(1));
        using var observer = new ShadowExternalObserver(service, service.Church, [new("new_group", "new_church", "new_groupid")]);
        Assert.Throws<InvalidDataException>(() => observer.Capture(CancellationToken.None));
    }

    /// <summary>未捕獲、重複捕獲、取消及釋放都不能繼續查詢；取消來源由測試owner釋放。</summary>
    [Fact]
    public void ExternalObserver_EnforcesCaptureCancellationAndDisposalLifetime()
    {
        var service = new ReadService();
        using var observer = new ShadowExternalObserver(service, service.Church, [new("new_group", "new_church", "new_groupid")]);
        Assert.Throws<InvalidOperationException>(() => observer.Compare(CancellationToken.None));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => observer.Capture(cancellation.Token));
        Assert.Equal(0, service.Reads);
        observer.Capture(CancellationToken.None);
        Assert.Throws<InvalidOperationException>(() => observer.Capture(CancellationToken.None));
        observer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => observer.Compare(CancellationToken.None));
    }

    /// <summary>SDK已回應才取消也不能發布部分快照；同一owner解除取消後可重新完整捕獲。</summary>
    [Fact]
    public void ExternalObserver_CancellationAfterReadDoesNotPublishPartialBaseline()
    {
        var service = new ReadService();
        service.Rows.Add(service.Row(1));
        using var observer = new ShadowExternalObserver(service, service.Church, [new("new_group", "new_church", "new_groupid")]);
        using var cancellation = new CancellationTokenSource();
        service.AfterRead = cancellation.Cancel;
        Assert.Throws<OperationCanceledException>(() => observer.Capture(cancellation.Token));
        Assert.Throws<InvalidOperationException>(() => observer.Compare(CancellationToken.None));
        service.AfterRead = null;
        observer.Capture(CancellationToken.None);
        Assert.Equal(0, Assert.Single(observer.Compare(CancellationToken.None)).ChangedExistingCount);
    }

    /// <summary>不完整或重複範圍不能啟動I/O；不能以相同表多個selector重複計算同一資料庫列。</summary>
    [Fact]
    public void ExternalObserver_InvalidOrDuplicateScopesRefuseBeforeIo()
    {
        var service = new ReadService();
        ShadowExternalScope[] scopes = [new("new_group", "new_church", "new_groupid")];
        Assert.Throws<InvalidDataException>(() => new ShadowExternalObserver(service, Guid.Empty, scopes));
        Assert.Throws<InvalidDataException>(() => new ShadowExternalObserver(service, service.Church, [scopes[0], scopes[0]]));
        Assert.Throws<InvalidDataException>(() => new ShadowExternalObserver(service, service.Church,
            [scopes[0] with { ChurchAttribute = "versionnumber" }]));
        Assert.Throws<InvalidDataException>(() => new ShadowExternalObserver(service, service.Church,
            [scopes[0] with { Entity = "../new_group" }]));
        Assert.Equal(0, service.Reads);
    }

    /// <summary>只提供精確範圍讀取；沒有可供產品寫入、清理或全表查詢的後門。</summary>
    private sealed class ReadService : IOrganizationService
    {
        internal readonly Guid Church = Guid.NewGuid();
        internal readonly List<Entity> Rows = [];
        internal int Reads;
        internal string Fault = "";
        internal Action? AfterRead;
        /// <summary>同名不同ID為合法業務列；名稱不在query回傳欄中，不能當作身分。</summary>
        internal Entity Row(long version)
        {
            var id = Guid.NewGuid();
            return new Entity("new_group", id) { ["new_groupid"] = id, ["name"] = "same-name",
                ["versionnumber"] = version, ["new_church"] = new EntityReference("account", Church) };
        }
        /// <summary>核對正式查詢欄位及唯一教會條件；故障只影響回應，不替產品做身分去重。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var exact = Assert.IsType<QueryExpression>(query);
            Assert.Equal("new_group", exact.EntityName);
            Assert.Equal(65, exact.TopCount);
            Assert.False(exact.ColumnSet.AllColumns);
            Assert.Equal("new_church,new_groupid,versionnumber", string.Join(',', exact.ColumnSet.Columns.OrderBy(value => value, StringComparer.Ordinal)));
            var condition = Assert.Single(exact.Criteria.Conditions);
            Assert.Equal("new_church", condition.AttributeName);
            Assert.Equal(ConditionOperator.Equal, condition.Operator);
            Assert.Equal(Church, Assert.Single(condition.Values));
            Reads++;
            var result = new EntityCollection(Rows.ToList());
            if (Fault == "page") result.MoreRecords = true;
            if (Fault == "overflow") for (var i = 0; i < 65; i++) result.Entities.Add(Row(1));
            if (Fault == "duplicate") result.Entities.Add(result.Entities[0]);
            if (Fault == "empty-id") result.Entities[0].Id = Guid.Empty;
            if (Fault == "wrong-entity") result.Entities[0].LogicalName = "account";
            if (Fault == "wrong-church") result.Entities[0]["new_church"] = new EntityReference("account", Guid.NewGuid());
            if (Fault == "missing-version") result.Entities[0].Attributes.Remove("versionnumber");
            if (Fault == "wrong-version-type") result.Entities[0]["versionnumber"] = "1";
            if (Fault == "wrong-primary") result.Entities[0]["new_groupid"] = Guid.NewGuid();
            AfterRead?.Invoke();
            return result;
        }
        /// <summary>禁止取得任意業務列。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new InvalidOperationException();
        /// <summary>observer禁止建立任何遠端列。</summary>
        public Guid Create(Entity entity) => throw new InvalidOperationException();
        /// <summary>observer禁止變更既有列。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException();
        /// <summary>observer禁止清理或刪除既有列。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException();
        /// <summary>observer禁止額外訊息。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw new InvalidOperationException();
        /// <summary>observer禁止建立關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException();
        /// <summary>observer禁止解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException();
    }
}
