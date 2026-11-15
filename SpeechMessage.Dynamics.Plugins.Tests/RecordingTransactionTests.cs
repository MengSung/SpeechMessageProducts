/*
 * 檔案整體說明
 *
 * 目的與定位：以離線合成資料保護週報 Shadow Create-only 原子交易白名單，
 * 不連 Dataverse、不發 LINE；預配 GUID 只代表呼叫端提供的正式主鍵，不冒充實機唯一性驗證。
 * 主要功能與流程：有效交易驗證逐筆意圖、索引回應及 overlay 讀回；將非法輸入放在
 * 第二筆，注入未知可變值、混合寫入、偽裝訊息、重複主鍵及容量不足，斷言整批零發布。
 * 另外修改來源、回應及讀回的可變值，證明記錄器不保留呼叫端參照；反覆 Clear 後
 * 重用同一組主鍵驗證 overlay 排空，Dispose 後拒絕使用，安全摘要仍受既有預算約束。
 * 使用邊界與維護：FakeOrganizationService 與 recorder 均由案例 using owner 確定釋放，
 * 沒有共享狀態、非同步或外部資源；所有錯誤斷言先檢查零意圖，再用缺列讀取確認
 * 沒有部分 overlay；缺列會穿透假服務並拋例外，另斷言恰有一次 Retrieve 以辨認來源。
 * 此測試僅證明單次同步 owner 的記憶體原子性，不宣稱資料庫交易驗收。
 */
using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>以真實 recorder 與案例獨占假服務驗證交易發布、白名單、隔離與排空契約。</summary>
public sealed class RecordingTransactionTests
{
    /// <summary>
    /// 合法兩筆同名不同 ID 必須完整保留，回應依輸入索引提供正式 ID；
    /// 來源、回應與讀回修改不可污染 overlay，且真服務連 Execute 都不能被呼叫。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void ValidCreates_PublishDetachedRowsAndTypedResponses(bool? returnResponses)
    {
        using var inner = new FakeOrganizationService();
        using var recorder = new RecordingOrganizationService(inner);
        var first = Row();
        var second = Row();
        var id = first.Id;
        var transaction = Batch(first, second);
        if (returnResponses.HasValue) transaction.ReturnResponses = returnResponses.Value;

        var response = Assert.IsType<ExecuteTransactionResponse>(recorder.Execute(transaction));

        Assert.Equal(new[] { id, second.Id }, recorder.Writes.Select(write => write.Id));
        Assert.All(recorder.Writes, write =>
        {
            Assert.Equal("Create", write.Operation);
            Assert.DoesNotContain("synthetic-secret", write.ProjectionJson, StringComparison.Ordinal);
            Assert.DoesNotContain("same-person", write.ProjectionJson, StringComparison.Ordinal);
        });
        Assert.Equal(2, recorder.TotalCount);
        if (returnResponses == true)
        {
            Assert.Equal(id, Assert.IsType<CreateResponse>(response.Responses[0]).id);
            Assert.Equal(second.Id, Assert.IsType<CreateResponse>(response.Responses[1]).id);
            response.Responses[0].Results["id"] = Guid.NewGuid();
        }
        else Assert.Empty(response.Responses);
        ((Money)first["new_amount"]).Value = 99;
        ((byte[])first["new_blob"])[0] = 99;
        first.Id = Guid.NewGuid();
        transaction.Requests.Clear();
        var read = recorder.Retrieve("task", id, new ColumnSet(true));
        Assert.Equal(7m, ((Money)read["new_amount"]).Value);
        Assert.Equal(1, ((byte[])read["new_blob"])[0]);
        ((Money)read["new_amount"]).Value = 100;
        Assert.Equal(7m, ((Money)recorder.Retrieve("task", id, new ColumnSet(true))["new_amount"]).Value);
        Assert.Equal(second.Id, recorder.Retrieve("task", second.Id, new ColumnSet(true)).Id);
        Assert.Empty(inner.Calls);
    }

    /// <summary>
    /// 第二筆故障或請求形狀不合法時，第一筆也不得進入意圖與 overlay；
    /// 測試涵蓋型別／名稱偽裝、未知與混合寫入、額外參數、空身分、替代鍵及相關圖。
    /// </summary>
    [Theory]
    [InlineData("unknown-value")]
    [InlineData("empty-id")]
    [InlineData("keys")]
    [InlineData("related")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("nested")]
    [InlineData("unknown")]
    [InlineData("raw-create")]
    [InlineData("raw-transaction")]
    [InlineData("renamed-create")]
    [InlineData("renamed-transaction")]
    [InlineData("extra-create")]
    [InlineData("extra-transaction")]
    [InlineData("invalid-return")]
    [InlineData("null-target")]
    [InlineData("null-request")]
    public void InvalidSecondOrEnvelope_RejectsWithoutPartialPublication(string fault)
    {
        using var inner = new FakeOrganizationService();
        using var recorder = new RecordingOrganizationService(inner);
        var first = Row();
        var second = Row();
        var transaction = Batch(first, second);
        OrganizationRequest request = transaction;
        switch (fault)
        {
            case "unknown-value": second["new_unknown"] = new object(); break;
            case "empty-id": second.Id = Guid.Empty; break;
            case "keys": second.KeyAttributes["externalid"] = "key"; break;
            case "related":
                second.RelatedEntities.Add(new Relationship("task_contacts"), new EntityCollection(new[] { Row() })); break;
            case "update": transaction.Requests[1] = new UpdateRequest { Target = second }; break;
            case "delete": transaction.Requests[1] = new DeleteRequest { Target = second.ToEntityReference() }; break;
            case "nested": transaction.Requests[1] = Batch(second); break;
            case "unknown": transaction.Requests[1] = new OrganizationRequest("CustomDangerousWrite"); break;
            case "raw-create":
                transaction.Requests[1] = new OrganizationRequest("Create") { ["Target"] = second }; break;
            case "raw-transaction":
                request = new OrganizationRequest("ExecuteTransaction") { ["Requests"] = transaction.Requests }; break;
            case "renamed-create": transaction.Requests[1].RequestName = "Update"; break;
            case "renamed-transaction": transaction.RequestName = "WhoAmI"; break;
            case "extra-create": transaction.Requests[1]["BypassCustomPluginExecution"] = true; break;
            case "extra-transaction": transaction["ContinueOnError"] = true; break;
            case "invalid-return": transaction["ReturnResponses"] = "true"; break;
            case "null-target": transaction.Requests[1]["Target"] = null!; break;
            case "null-request": transaction.Requests[1] = null!; break;
        }

        Assert.Throws<NotSupportedException>(() => recorder.Execute(request));
        Assert.Empty(recorder.Writes);
        Assert.Equal(0, recorder.TotalCount);
        Assert.False(recorder.Overflow);
        Assert.Empty(inner.Calls);
        Assert.Throws<InvalidOperationException>(() => recorder.Retrieve("task", first.Id, new ColumnSet(true)));
        Assert.Equal(1, inner.Calls["Retrieve"]);
    }

    /// <summary>空批次及第 65 筆皆拒絕；合法最大 64 筆仍一次發布且遵守摘要截斷上限。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(65)]
    public void BatchCount_EnforcesBoundedPublication(int count)
    {
        using var inner = new FakeOrganizationService();
        using var recorder = new RecordingOrganizationService(inner);
        var rows = Enumerable.Range(0, count).Select(_ => Row()).ToArray();
        if (count == 64)
        {
            recorder.Execute(Batch(rows));
            Assert.Equal(64, recorder.TotalCount);
            foreach (var row in rows) Assert.Equal(row.Id, recorder.Retrieve("task", row.Id, new ColumnSet(true)).Id);
            Assert.InRange(recorder.Writes.Sum(write => write.ProjectionJson.Length), 1, 16000);
            Assert.True(recorder.Overflow);
        }
        else
        {
            Assert.Throws<NotSupportedException>(() => recorder.Execute(Batch(rows)));
            Assert.Empty(recorder.Writes);
            Assert.Equal(0, recorder.TotalCount);
        }
        Assert.Empty(inner.Calls);
    }

    /// <summary>
    /// 先用同一列的合成 Update 填入 127 筆摘要，再以兩筆交易跨越筆數預算；
    /// 截斷僅影響摘要保留，意圖總數及兩列 overlay 仍須完整發布，最後由 using owner 排空。
    /// </summary>
    [Fact]
    public void SummaryCountOverflow_PublishesAllRowsWithBoundedEvidence()
    {
        using var inner = new FakeOrganizationService();
        using var recorder = new RecordingOrganizationService(inner);
        var existingId = Guid.NewGuid();
        for (var index = 0; index < 127; index++) recorder.Update(new Entity("task", existingId));
        Assert.Equal(127, recorder.Writes.Count);
        Assert.False(recorder.Overflow);
        var first = new Entity("task", Guid.NewGuid());
        var second = new Entity("task", Guid.NewGuid());

        recorder.Execute(Batch(first, second));

        Assert.True(recorder.Overflow);
        Assert.Equal(129, recorder.TotalCount);
        Assert.Equal(128, recorder.Writes.Count);
        Assert.True(recorder.Writes.Sum(write => write.ProjectionJson.Length) <= 16000);
        Assert.Equal(first.Id, recorder.Retrieve("task", first.Id, new ColumnSet(true)).Id);
        Assert.Equal(second.Id, recorder.Retrieve("task", second.Id, new ColumnSet(true)).Id);
        Assert.Empty(inner.Calls);
    }

    /// <summary>
    /// 批內重複、既有列／墓碑衝突及合計容量不足不能新增任何意圖；
    /// 保留既有快照後 Clear 可重用同一主鍵，重複排空再 Dispose 必須回到零基線。
    /// </summary>
    [Theory]
    [InlineData("duplicate")]
    [InlineData("existing")]
    [InlineData("tombstone")]
    [InlineData("capacity")]
    public void OverlayConflict_LeavesBaselineIntactAndDrains(string fault)
    {
        using var inner = new FakeOrganizationService();
        using var recorder = new RecordingOrganizationService(inner);
        var existing = Row();
        var first = Row();
        var second = Row();
        if (fault != "duplicate") recorder.Create(existing);
        if (fault == "existing" || fault == "tombstone") second.Id = existing.Id;
        if (fault == "tombstone") recorder.Delete(existing.LogicalName, existing.Id);
        if (fault == "duplicate") second.Id = first.Id;
        if (fault == "capacity")
            for (var index = 1; index < 63; index++) recorder.Create(Row());
        var baseline = recorder.Writes.ToArray();
        var total = recorder.TotalCount;
        var overflow = recorder.Overflow;

        Assert.Throws<InvalidOperationException>(() => recorder.Execute(Batch(first, second)));

        Assert.Equal(baseline, recorder.Writes);
        Assert.Equal(total, recorder.TotalCount);
        Assert.Equal(overflow, recorder.Overflow);
        Assert.Empty(inner.Calls);
        Assert.Throws<InvalidOperationException>(() => recorder.Retrieve("task", first.Id, new ColumnSet(true)));
        Assert.Equal(1, inner.Calls["Retrieve"]);
        for (var run = 0; run < 3; run++)
        {
            recorder.Clear();
            recorder.Execute(Batch(first));
            Assert.Equal(1, recorder.TotalCount);
            Assert.Equal(first.Id, recorder.Retrieve("task", first.Id, new ColumnSet(true)).Id);
        }
        recorder.Dispose();
        Assert.Empty(recorder.Writes);
        Assert.Equal(0, recorder.TotalCount);
        Assert.False(recorder.Overflow);
        Assert.Throws<ObjectDisposedException>(() => recorder.Execute(Batch(first)));
    }

    /// <summary>建立固定內容但每列正式身分不同的合成資料，以同名記錄保護不得按顯示值去重的契約。</summary>
    private static Entity Row() => new("task", Guid.NewGuid())
    {
        ["new_amount"] = new Money(7m), ["new_blob"] = new byte[] { 1, 2 },
        ["firstname"] = "same-person", ["new_token"] = "synthetic-secret"
    };

    /// <summary>只組裝 typed Create 與必要 Requests；刻意省略 ReturnResponses 以驗證缺省空回應。</summary>
    private static ExecuteTransactionRequest Batch(params Entity[] rows)
    {
        var requests = new OrganizationRequestCollection();
        foreach (var row in rows) requests.Add(new CreateRequest { Target = row });
        return new ExecuteTransactionRequest { Requests = requests };
    }
}
