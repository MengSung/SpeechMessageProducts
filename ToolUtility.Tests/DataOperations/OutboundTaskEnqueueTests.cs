/*
 * 檔案整體說明
 *
 * 目的與定位：以合成 IOrganizationService 的原子 Create／唯一鍵衝突保護 P2-07
 * 儲存適配器；兩宿主執行真實 store，不連 D365，不把替身當作實機唯一鍵驗收。
 * 主要功能與流程：兩個 store 同時建立同事件時，資料庫替身只允許一列，產品必須
 * 辨認唯一鍵錯誤並讀回同一正式 ID；既有完成狀態不可重設。分別注入外教會、
 * payload 衝突、重複正式列、無正式 ID 及非唯一鍵 fault，確認整體拒絕且無 Update。
 * 使用邊界與維護：每案專有字典、SDK 替身與 Task，Task.WhenAll 後才檢查結果。
 * 替身沒有連線或 credential，store 不 Dispose 外借 SDK；異常斷言保留同一 fault，
 * 不依產品常數產生預期錯誤碼或查詢欄位，避免測試與實作一起寫錯。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.Outbound;
using Xunit;

namespace ToolUtility.Tests.DataOperations;

/// <summary>驗證真正 store 的唯一鍵衝突分支、最小欄位、原子建立與不覆寫契約。</summary>
public sealed class OutboundTaskEnqueueTests
{
    /// <summary>兩個真實 store 競爭相同事件，僅一筆 Create 成功，兩邊回同一正式 ID。</summary>
    [Fact]
    public async Task Enqueue_TwoStoresParallel_OneDatabaseRowAndUnchangedState()
    {
        var command = Command();
        var rowId = Guid.NewGuid();
        var gate = new object();
        using var entrants = new Barrier(2);
        Entity? saved = null;
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.Create(It.IsAny<Entity>())).Returns((Entity row) =>
        {
            Assert.Equal("new_outboundtask", row.LogicalName);
            Assert.Equal(command.IdempotencyKey, row["new_idempotencykey"]);
            Assert.Equal(100000000, row.GetAttributeValue<OptionSetValue>("new_taskstatus").Value);
            Assert.Equal(0, row.GetAttributeValue<int>("new_attempts"));
            Assert.Equal("account", row.GetAttributeValue<EntityReference>("new_accountid").LogicalName);
            // 兩個產品呼叫都真正入場後才交給合成資料庫裁決，避免排程剛好串行卻冒稱併行證據。
            Assert.True(entrants.SignalAndWait(TimeSpan.FromSeconds(10)));
            lock (gate)
            {
                if (saved is not null) throw Fault(unchecked((int)0x80040237));
                row.Id = rowId;
                // 模擬在第二個程序入場前已完成，retry 不能把它改回 Pending。
                row["new_taskstatus"] = new OptionSetValue(100000002);
                row["new_attempts"] = 3;
                saved = row;
                return rowId;
            }
        });
        sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase raw) =>
        {
            AssertQuery(raw, command);
            lock (gate) return new EntityCollection(new List<Entity> { saved! });
        });
        using var firstStore = new D365OutboundEnqueueStore(sdk.Object);
        using var secondStore = new D365OutboundEnqueueStore(sdk.Object);
        var stores = new[] { firstStore, secondStore };
        var ids = await Task.WhenAll(stores.Select(store => Task.Run(() => store.CreateOrGet(command, CancellationToken.None))));
        Assert.All(ids, id => Assert.Equal(rowId, id));
        Assert.NotNull(saved);
        Assert.Equal(100000002, saved!.GetAttributeValue<OptionSetValue>("new_taskstatus").Value);
        Assert.Equal(3, saved.GetAttributeValue<int>("new_attempts"));
        sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Exactly(2));
        sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
    }

    /// <summary>唯一鍵已存在但不可變欄位／正式 ID 損壞時拒絕，不選第一列掩蓋資料衝突。</summary>
    /// <param name="fault">合成損壞位置；不包含真實業務資料。</param>
    [Theory]
    [InlineData("church")]
    [InlineData("payload")]
    [InlineData("contact")]
    [InlineData("type")]
    [InlineData("missing-id")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    public void Enqueue_ConflictingExistingRow_RejectsWithoutUpdating(string fault)
    {
        var command = Command();
        var row = new Entity("new_outboundtask", Guid.NewGuid())
        {
            ["new_idempotencykey"] = command.IdempotencyKey,
            ["new_type"] = new OptionSetValue(100000001),
            ["new_accountid"] = new EntityReference("account", command.ChurchId),
            ["new_payload"] = command.PayloadJson
        };
        switch (fault)
        {
            case "church": row["new_accountid"] = new EntityReference("account", Guid.NewGuid()); break;
            case "payload": row["new_payload"] = "{}"; break;
            case "contact": row["new_contactid"] = new EntityReference("contact", Guid.NewGuid()); break;
            case "type": row["new_type"] = new OptionSetValue(100000000); break;
            case "missing-id": row.Id = Guid.Empty; break;
        }
        var page = new EntityCollection();
        if (fault != "missing") page.Entities.Add(row);
        if (fault == "duplicate") page.Entities.Add(row);
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.Create(It.IsAny<Entity>())).Throws(Fault(unchecked((int)0x80040237)));
        sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(page);
        using var store = new D365OutboundEnqueueStore(sdk.Object);
        Assert.Throws<InvalidOperationException>(() => store.CreateOrGet(command, CancellationToken.None));
        sdk.Verify(s => s.Update(It.IsAny<Entity>()), Times.Never);
    }

    /// <summary>非唯一鍵 fault 不能變成成功或讀回；預取消也不能先送 Create。</summary>
    [Fact]
    public void Enqueue_OtherFaultAndCancellation_AreNotRetried()
    {
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var failure = Fault(unchecked((int)0x80040216));
        sdk.Setup(s => s.Create(It.IsAny<Entity>())).Throws(failure);
        using var store = new D365OutboundEnqueueStore(sdk.Object);
        Assert.Same(failure, Assert.Throws<FaultException<OrganizationServiceFault>>(
            () => store.CreateOrGet(Command(), CancellationToken.None)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => store.CreateOrGet(Command(), cancellation.Token));
        sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Once);
        sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Never);
    }

    /// <summary>建立回傳空 ID 不能冒充成功；SDK 不確定結果保留為失敗而不自動重送。</summary>
    [Fact]
    public void Enqueue_CreateWithoutIdentity_Rejects()
    {
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.Create(It.IsAny<Entity>())).Returns(Guid.Empty);
        using var store = new D365OutboundEnqueueStore(sdk.Object);
        Assert.Throws<InvalidOperationException>(() => store.CreateOrGet(Command(), CancellationToken.None));
        sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Once);
    }

    /// <summary>Dispose 後拒絕新 I/O 且可重入，但不釋放借用 SDK；連線仍由呼叫端唯一擁有。</summary>
    [Fact]
    public void Enqueue_DisposeRejectsNewCallsWithoutDisposingBorrowedSdk()
    {
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var disposable = sdk.As<IDisposable>();
        using var store = new D365OutboundEnqueueStore(sdk.Object);
        store.Dispose();
        Assert.Throws<ObjectDisposedException>(() => store.CreateOrGet(Command(), CancellationToken.None));
        store.Dispose();
        disposable.Verify(x => x.Dispose(), Times.Never);
        sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Never);
    }

    /// <summary>建立不含自由文字的合成命令；唯一鍵依真實命令產生，查詢 schema 仍用獨立字面值驗證。</summary>
    private static OutboundTaskCommand Command() => new(OutboundTaskType.QrImageGenerate, Guid.NewGuid(), null,
        Guid.NewGuid(), new Dictionary<string, object> { ["quantity"] = 2 });

    /// <summary>依官方固定錯誤碼注入 fault，不使用產品常數以免兩邊共同漂移。</summary>
    private static FaultException<OrganizationServiceFault> Fault(int code) =>
        new(new OrganizationServiceFault { ErrorCode = code }, new FaultReason("synthetic"));

    /// <summary>讀回只允許精確替代鍵、至多兩列與不可變內容欄，不查 AllColumns 或任意教會資料。</summary>
    private static void AssertQuery(QueryBase raw, OutboundTaskCommand command)
    {
        var query = Assert.IsType<QueryExpression>(raw);
        Assert.Equal("new_outboundtask", query.EntityName);
        Assert.Equal(2, query.TopCount);
        Assert.False(query.ColumnSet.AllColumns);
        Assert.Equal(new[] { "new_accountid", "new_contactid", "new_idempotencykey", "new_payload", "new_type" },
            query.ColumnSet.Columns.OrderBy(x => x, StringComparer.Ordinal));
        var condition = Assert.Single(query.Criteria.Conditions);
        Assert.Equal("new_idempotencykey", condition.AttributeName);
        Assert.Equal(ConditionOperator.Equal, condition.Operator);
        Assert.Equal(command.IdempotencyKey, Assert.Single(condition.Values));
    }
}
