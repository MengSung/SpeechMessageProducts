/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P2-10 真正 Dataverse adapter 的查詢、資料身分及 lease 清理，
 * 全部使用合成 SDK 列與 Moq 離線服務，不讀 User Secrets、不連實機或發訊。
 * 主要功能與流程：呼叫正式 ReadAsync，記錄實際 QueryExpression 的最小欄位與
 * 嚴格時間條件；成功斷言 detached 正式 ID／UTC，故障斷言原 Exception 與淘汰歸還。
 * 使用邊界與維護：每案獨佔 manager、lease 與來源，不建立真正 client 或 timer，
 * 所有測試 task 均由案例等待完成；不同正式 ID 即使時間相同仍須完整保留，
 * 同一集合重複正式 ID 必須拒絕，不以 Distinct 或字典覆寫隱藏來源衝突。
 */
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Localization;
using Xunit;

namespace ToolUtility.Tests.Localization;

/// <summary>保護最小查詢、完整發布與池化 lease 歸還；所有替身均由單案擁有。</summary>
public sealed class DataverseTextChangeSourceTests
{
    /// <summary>兩個正式 ID 共用時間仍都保留；實際查詢只選改字欄位並使用嚴格大於。</summary>
    [Fact]
    public async Task Read_MinimalColumns_StrictCutoff_AndDetachedRows()
    {
        var since = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
        var a = new Entity("account", Guid.NewGuid()) { ["new_textchangedon"] = since.AddSeconds(1) };
        var b = new Entity("account", Guid.NewGuid()) { ["new_textchangedon"] = since.AddSeconds(1) };
        QueryExpression? captured = null;
        var service = new Mock<IOrganizationService>(MockBehavior.Strict);
        service.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
            .Callback<QueryBase>(q => captured = Assert.IsType<QueryExpression>(q))
            .Returns(new EntityCollection(new[] { a, b }));
        var lease = new Mock<IClientLease>(MockBehavior.Strict);
        lease.SetupGet(x => x.Service).Returns(service.Object);
        lease.Setup(x => x.Dispose());
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(x => x.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
        var result = await new DataverseTextChangeSource(manager.Object).ReadAsync(since, CancellationToken.None);
        Assert.NotNull(captured);
        Assert.False(captured.ColumnSet.AllColumns);
        Assert.Equal(new[] { "new_textchangedon" }, captured.ColumnSet.Columns);
        var condition = Assert.Single(captured.Criteria.Conditions);
        Assert.Equal("new_textchangedon", condition.AttributeName);
        Assert.Equal(ConditionOperator.GreaterThan, condition.Operator);
        Assert.Equal(since, Assert.Single(condition.Values));
        Assert.Equal(new[] { a.Id, b.Id }, result.Select(x => x.ChurchId));
        Assert.All(result, row => Assert.Equal(since.AddSeconds(1), row.ChangedOnUtc));
        lease.Verify(x => x.Dispose(), Times.Once);
        manager.Verify(x => x.Dispose(), Times.Never);
    }

    /// <summary>SDK 真例外必須原物件傳播，連線先標 faulted 再歸還，沒有部分候選。</summary>
    [Fact]
    public async Task Read_Fault_EvictsAndReturnsLease()
    {
        var failure = new InvalidOperationException("synthetic");
        var service = new Mock<IOrganizationService>(MockBehavior.Strict);
        service.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(failure);
        var lease = new Mock<IClientLease>(MockBehavior.Strict);
        lease.SetupGet(x => x.Service).Returns(service.Object);
        var order = new List<string>();
        lease.Setup(x => x.MarkFaulted()).Callback(() => order.Add("fault"));
        lease.Setup(x => x.Dispose()).Callback(() => order.Add("dispose"));
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(x => x.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
        var source = new DataverseTextChangeSource(manager.Object);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.ReadAsync(new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None)));
        Assert.Equal(new[] { "fault", "dispose" }, order);
    }
}
