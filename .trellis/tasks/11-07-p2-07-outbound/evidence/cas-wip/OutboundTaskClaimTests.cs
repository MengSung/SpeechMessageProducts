/*
 * 檔案整體說明
 *
 * 目的與定位：以離線合成 SDK 驗證 P2-07 的資料庫 CAS；不是 D365 實機驗收。
 * 主要功能與流程：兩個真實 store 經 Barrier 同時入場後才進資料庫裁決，只有一個
 * 版本可認領；認領的新版本供後續完成，舊 owner 必須落敗。另注入重複身分、故障、
 * 缺版本及取消，檢查查詢欄位、上限、狀態與 UTC 卡住界線。
 * 使用邊界與維護：每案獨有 Moq、Barrier 與資料；await 全部競爭者後才 Dispose。
 * 替身不開連線或保存帳密，store 不擁有 SDK；原始 fault 身分及零額外 I/O 為斷言。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.Outbound;
using Xunit;

namespace ToolUtility.Tests.DataOperations;

/// <summary>真正適配器對假的資料庫版本裁決，預期欄位與錯誤碼不引用產品常數。</summary>
public sealed class OutboundTaskClaimTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>兩個程序同時認領版本 10，僅一個取得版本 11，只有成功者可進入執行路徑。</summary>
    [Fact]
    public async Task Claim_TwoWorkersParallel_OnlyOneExecutes()
    {
        using var barrier = new Barrier(2);
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var row = Row();
        var gate = new object();
        var version = 10;
        sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
        {
            var transaction = Assert.IsType<ExecuteTransactionRequest>(request);
            Assert.True(transaction.ReturnResponses);
            Assert.Equal(2, transaction.Requests.Count);
            var update = Assert.IsType<UpdateRequest>(transaction.Requests[0]);
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
            Assert.Equal("10", update.Target.RowVersion);
            Assert.Equal(100000001, update.Target.GetAttributeValue<OptionSetValue>("new_taskstatus").Value);
            Assert.Equal(Now, update.Target["new_claimedon"]);
            Assert.Equal(row.Id, Assert.IsType<RetrieveRequest>(transaction.Requests[1]).Target.Id);
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            lock (gate)
            {
                if (version != 10) throw Fault(unchecked((int)0x80060882));
                version++;
                row.RowVersion = "11";
                row["new_taskstatus"] = new OptionSetValue(100000001);
                row["new_claimedon"] = Now;
                return ClaimedResponse(row);
            }
        });
        using var first = new D365OutboundTaskStore(sdk.Object);
        using var second = new D365OutboundTaskStore(sdk.Object);
        var candidate = Snapshot(Row());
        // 同一正式 ID；不是各自使用不同列來假造競爭成功。
        candidate = new OutboundTaskRecord(row.Id, "10", candidate.ChurchId, null, candidate.Type,
            candidate.PayloadJson, candidate.IdempotencyKey, candidate.Status, 0, null, null);
        var claims = await Task.WhenAll(new[] { first, second }
            .Select(s => Task.Run(() => s.TryClaim(candidate, Now, CancellationToken.None))));
        var won = Assert.Single(claims.Where(c => c is not null));
        Assert.Equal("11", won!.RowVersion);
        Assert.Single(claims.Where(c => c is null));
        Assert.Equal(11, version);
    }

    /// <summary>卡住 Processing 超過十分鐘可重認領；十分鐘內與尚未到期 Pending 不發寫入。</summary>
    [Fact]
    public void StaleProcessing_Reclaimed()
    {
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var stale = Row();
        stale["new_taskstatus"] = new OptionSetValue(100000001);
        stale["new_claimedon"] = Now.AddMinutes(-11);
        sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest _) =>
        {
            stale.RowVersion = "11"; stale["new_claimedon"] = Now;
            return ClaimedResponse(stale);
        });
        using var store = new D365OutboundTaskStore(sdk.Object);
        Assert.NotNull(store.TryClaim(Snapshot(stale), Now, CancellationToken.None));
        stale["new_claimedon"] = Now.AddMinutes(-10);
        Assert.Null(store.TryClaim(Snapshot(stale), Now, CancellationToken.None));
        var future = Row(); future["new_nextattempton"] = Now.AddSeconds(1);
        Assert.Null(store.TryClaim(Snapshot(future), Now, CancellationToken.None));
        sdk.Verify(s => s.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
    }

    /// <summary>完成同樣受新認領版本保護；舊 owner 的 CAS 衝突回 false，不發無條件 Update。</summary>
    [Fact]
    public void Settle_StaleOwnerCannotOverwriteNewClaim()
    {
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var row = Row(); row["new_taskstatus"] = new OptionSetValue(100000001); row["new_claimedon"] = Now;
        sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
        {
            var update = Assert.IsType<UpdateRequest>(request);
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
            Assert.Equal("10", update.Target.RowVersion);
            Assert.Equal(100000002, update.Target.GetAttributeValue<OptionSetValue>("new_taskstatus").Value);
            Assert.Equal(Now, update.Target["new_completedon"]);
            Assert.Null(update.Target["new_lasterror"]);
            throw Fault(unchecked((int)0x80060882));
        });
        using var store = new D365OutboundTaskStore(sdk.Object);
        Assert.False(store.TrySettle(Snapshot(row), OutboundTaskStatus.Completed, 0, Now, null, null, CancellationToken.None));
        sdk.Verify(s => s.Update(It.IsAny<Entity>()), Times.Never);
    }

    /// <summary>候選完整驗證後才發布；缺版本或同批重複正式主鍵都拒絕，不能靜默去重。</summary>
    /// <param name="corruption">注入版本缺失或同一主鍵重複。</param>
    [Theory]
    [InlineData("duplicate")]
    [InlineData("version")]
    public void Candidates_CorruptPageRejected(string corruption)
    {
        var row = Row();
        if (corruption == "version") row.RowVersion = null;
        var page = new EntityCollection(new List<Entity> { row });
        if (corruption == "duplicate") page.Entities.Add(row);
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(page);
        using var store = new D365OutboundTaskStore(sdk.Object);
        Assert.ThrowsAny<ArgumentException>(() => store.ReadCandidates(Now, 50, CancellationToken.None));
    }

    /// <summary>候選只查到期 Pending 或逾十分鐘 Processing，固定上限與主鍵排序，避免掃全表。</summary>
    [Fact]
    public void Candidates_QueryIsBoundedAndUsesCustomStatus()
    {
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase raw) =>
        {
            var q = Assert.IsType<QueryExpression>(raw);
            Assert.Equal("new_outboundtask", q.EntityName); Assert.Equal(50, q.TopCount);
            Assert.False(q.ColumnSet.AllColumns);
            Assert.Contains("versionnumber", q.ColumnSet.Columns);
            Assert.Equal("new_outboundtaskid", Assert.Single(q.Orders).AttributeName);
            Assert.Equal(LogicalOperator.Or, q.Criteria.FilterOperator);
            var pending = q.Criteria.Filters[0]; var processing = q.Criteria.Filters[1];
            Assert.Equal(100000000, Assert.Single(pending.Conditions).Values[0]);
            Assert.Equal("new_taskstatus", pending.Conditions[0].AttributeName);
            Assert.Equal(LogicalOperator.Or, Assert.Single(pending.Filters).FilterOperator);
            Assert.Equal(100000001, processing.Conditions[0].Values[0]);
            Assert.Equal(ConditionOperator.LessThan, processing.Conditions[1].Operator);
            Assert.Equal(Now.AddMinutes(-10), processing.Conditions[1].Values[0]);
            return new EntityCollection(new List<Entity> { Row() });
        });
        using var store = new D365OutboundTaskStore(sdk.Object);
        Assert.Single(store.ReadCandidates(Now, 50, CancellationToken.None));
    }

    /// <summary>非版本故障保持同一例外且不重試；取消與 Dispose 都在入場前拒絕新 I/O。</summary>
    [Fact]
    public void Claim_OtherFaultCancellationAndDisposeRemainDistinct()
    {
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var failure = Fault(unchecked((int)0x80040216));
        sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Throws(failure);
        using var store = new D365OutboundTaskStore(sdk.Object);
        Assert.Same(failure, Assert.Throws<FaultException<OrganizationServiceFault>>(
            () => store.TryClaim(Snapshot(Row()), Now, CancellationToken.None)));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => store.ReadCandidates(Now, 50, cts.Token));
        store.Dispose();
        Assert.Throws<ObjectDisposedException>(() => store.ReadCandidates(Now, 50, CancellationToken.None));
        sdk.Verify(s => s.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
    }

    /// <summary>合成同一組織的有界正式列；固定教會與來源鍵供獨立判讀，不含真實資料。</summary>
    private static Entity Row()
    {
        var church = Guid.Parse("11111111-1111-1111-1111-111111111111");
        return new Entity("new_outboundtask", Guid.NewGuid())
        {
            RowVersion = "10",
            ["new_accountid"] = new EntityReference("account", church),
            ["new_type"] = new OptionSetValue(100000001),
            ["new_payload"] = "{\"quantity\":2}",
            ["new_idempotencykey"] = "11111111111111111111111111111111-100000001-22222222222222222222222222222222",
            ["new_taskstatus"] = new OptionSetValue(100000000),
            ["new_attempts"] = 0
        };
    }

    /// <summary>以獨立字面 schema 建快照；不藉呼叫產品 mapper 掩蓋錯誤欄位。</summary>
    private static OutboundTaskRecord Snapshot(Entity e) => new(e.Id, e.RowVersion,
        e.GetAttributeValue<EntityReference>("new_accountid").Id, null, OutboundTaskType.QrImageGenerate,
        e.GetAttributeValue<string>("new_payload"), e.GetAttributeValue<string>("new_idempotencykey"),
        (OutboundTaskStatus)e.GetAttributeValue<OptionSetValue>("new_taskstatus").Value, 0,
        e.GetAttributeValue<DateTime?>("new_nextattempton"), e.GetAttributeValue<DateTime?>("new_claimedon"));

    /// <summary>模擬同交易 Update 與 Retrieve 的回應；讀回版本與寫入間不得容許第三者插入。</summary>
    private static ExecuteTransactionResponse ClaimedResponse(Entity row) => new()
    {
        Results = new ParameterCollection
        {
            ["Responses"] = new ExecuteTransactionResponseItemCollection
            {
                new ExecuteTransactionResponseItem { RequestIndex = 0, Response = new UpdateResponse() },
                new ExecuteTransactionResponseItem
                {
                    RequestIndex = 1,
                    Response = new RetrieveResponse { Results = new ParameterCollection { ["Entity"] = row } }
                }
            }
        }
    };

    /// <summary>注入固定官方 SDK fault 碼；不能把其他 SQL／認證錯誤吞成競爭失敗。</summary>
    private static FaultException<OrganizationServiceFault> Fault(int code) =>
        new(new OrganizationServiceFault { ErrorCode = code }, new FaultReason("synthetic"));
}
