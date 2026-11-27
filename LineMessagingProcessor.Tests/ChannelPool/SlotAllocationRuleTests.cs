/*
檔案整體說明
目的與定位：以真實純規則保護 F3 名額池排序、產品隔離、正式身分與不可變輸出的離線契約，雙宿主測試不連 D365 或 LINE。
主要功能與流程：每案建立自己的合成 GUID 與快照，透過 Choose 斷言最低優先序、可用狀態、池滿及重複身分拒絕；不依賴顯示名稱合併，也不以建置錯誤冒充 RED。
使用邊界與維護：測試沒有外部連線、計時器或可釋放資源；資料只存活測試方法，來源集合被替換後既有結果仍不變。任何未驗證的尾端資料不能因前面已選到名額而略過，並由故障案例驗證 fail closed。
*/
using System;
using LineMessagingProcessor.Core.ChannelPool;
using Xunit;
namespace LineMessagingProcessor.Tests.ChannelPool;

/// <summary>真實純值規則的離線驗收，不以 mock 重寫選取邏輯或宣稱資料庫交易保證。</summary>
public sealed class SlotAllocationRuleTests
{
    /// <summary>亂序候選包含不同頻道和編號；決定性斷言先選較低池順序，再選較低編號。</summary>
    [Fact]
    public void Choose_PrefersLowestPoolOrderThenLowestSlot()
    {
        var first = Slot(1, 7); var lower = new ChannelSlotSnapshot(first.ChannelId, Guid.NewGuid(), PoolProduct.GoodShepherd2, 1, 2, ChannelStatus.Operating, SlotStatus.Available);
        var selected = SlotAllocationRule.Choose(PoolProduct.GoodShepherd2, new[] { Slot(2, 3), first, lower });
        Assert.Equal(lower.SlotId, selected.SlotId);
    }
    /// <summary>混入停用、登錄及已滿頻道；只有營運中資料可選，不能因排序較低而選不安全狀態。</summary>
    [Fact]
    public void Choose_IgnoresNonOperatingChannels()
    {
        var chosen = Slot(9, 1);
        Assert.Equal(chosen.SlotId, SlotAllocationRule.Choose(PoolProduct.GoodShepherd2,
            new[] { Slot(1, 1, ChannelStatus.Disabled), Slot(2, 1, ChannelStatus.Registered), Slot(3, 1, ChannelStatus.Full), chosen }).SlotId);
    }
    /// <summary>同集合包含兩產品；產品精確字串邊界優先於池順序，不容其他池的較低順序搶占。</summary>
    [Fact]
    public void Choose_OnlyOwnProductChannels()
    {
        var own = Slot(8, 1, product: PoolProduct.GoodButler);
        Assert.Equal(own.SlotId, SlotAllocationRule.Choose(PoolProduct.GoodButler, new[] { Slot(1, 1), own }).SlotId);
    }
    /// <summary>保留、清理中及已配發都不是可用；回 PoolFull，不能拿非空正式 ID 當成成功。</summary>
    [Fact]
    public void Choose_NoAvailable_ReturnsPoolFull()
    {
        Assert.Same(SlotAllocation.PoolFull, SlotAllocationRule.Choose(PoolProduct.GoodShepherd2,
            new[] { Slot(1, 1, status: SlotStatus.Cleaning), Slot(2, 1, status: SlotStatus.Allocated), Slot(3, 1, status: SlotStatus.Reserved) }));
    }
    /// <summary>重複正式 Slot ID 放在已找到候選之後，仍必須拒絕整份集合，不得提早返回或靜默去重。</summary>
    [Fact]
    public void Choose_DuplicateIdentity_RefusesWholeCandidate()
    {
        var row = Slot(1, 1);
        Assert.Throws<ArgumentException>(() => SlotAllocationRule.Choose(PoolProduct.GoodShepherd2, new[] { row, row }));
    }
    /// <summary>正式空 ID、重複頻道編號及近似產品值不能被當成合法來源；每案要求具體參數拒絕。</summary>
    [Fact]
    public void Choose_InvalidIdentityAndProduct_Refused()
    {
        var row = Slot(1, 1);
        var duplicateNumber = new ChannelSlotSnapshot(row.ChannelId, Guid.NewGuid(), row.Product, 1, 1, row.ChannelStatus, row.SlotStatus);
        Assert.Throws<ArgumentException>(() => SlotAllocationRule.Choose(row.Product, new[] { row, duplicateNumber }));
        Assert.Throws<ArgumentException>(() => SlotAllocationRule.Choose("goodshepherd2", new[] { row }));
        Assert.Throws<ArgumentException>(() => SlotAllocationRule.Choose(row.Product,
            new[] { new ChannelSlotSnapshot(Guid.Empty, Guid.NewGuid(), row.Product, 1, 1, row.ChannelStatus, row.SlotStatus) }));
    }
    /// <summary>同順序且同編號的不同頻道皆合法；改變 caller 陣列不會改寫已選結果或共享池滿值。</summary>
    [Fact]
    public void Choose_DetachedResult_PreservesDistinctChannels()
    {
        var rows = new[] { Slot(1, 1), Slot(1, 1) };
        var expected = rows[0].ChannelId.CompareTo(rows[1].ChannelId) < 0 ? rows[0] : rows[1];
        var result = SlotAllocationRule.Choose(PoolProduct.GoodShepherd2, rows);
        rows[0] = Slot(9, 1);
        Assert.Equal(expected.SlotId, result.SlotId);
        Assert.False(result.IsPoolFull);
        Assert.True(SlotAllocation.PoolFull.IsPoolFull);
    }
    /// <summary>每次建立獨立正式身分的合成名額，不讀取產品資料或配置任何外部資源。</summary>
    private static ChannelSlotSnapshot Slot(int order, int number, ChannelStatus channel = ChannelStatus.Operating,
        SlotStatus status = SlotStatus.Available, string product = PoolProduct.GoodShepherd2) =>
        new ChannelSlotSnapshot(Guid.NewGuid(), Guid.NewGuid(), product, order, number, channel, status);
}
