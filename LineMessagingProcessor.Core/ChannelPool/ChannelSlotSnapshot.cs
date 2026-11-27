/*
檔案整體說明
目的與定位：作為名額純規則的不可變輸入，承載正式 Channel、Slot 身分與伺服器讀取的產品、排序及狀態，不帶 SDK 實體參照。
主要功能與流程：呼叫端先完成交易內查詢與授權，再把一筆名額投影為本物件；Choose 會驗證整份集合的正式 ID、唯一性與頻道中繼資料一致性，然後選取最低順序。
使用邊界與維護：本檔只保存純值，不執行授權、資料讀取、清理或發布，也不持有可釋放資源。快照生命期限定當次配發；呼叫端不可用顯示名稱代替 ID，或把舊租戶的快照跨交易重用。離線測試驗證不同 ID 同名仍保留與重複 ID 拒絕。
*/
using System;
namespace LineMessagingProcessor.Core.ChannelPool;

/// <summary>單一正式名額的不可變投影；完整集合仍須通過 Choose 的唯一性驗證，建構不代表授權成功。</summary>
public sealed class ChannelSlotSnapshot
{
    /// <summary>複製已讀取的純值，不保存來源 Entity 或任何連線；正式 ID 與集合一致性由規則統一驗證。</summary>
    public ChannelSlotSnapshot(Guid channelId, Guid slotId, string product, int poolOrder, int slotNumber,
        ChannelStatus channelStatus, SlotStatus slotStatus)
    {
        ChannelId = channelId; SlotId = slotId; Product = product; PoolOrder = poolOrder;
        SlotNumber = slotNumber; ChannelStatus = channelStatus; SlotStatus = slotStatus;
    }
    /// <summary>資料庫 Channel 正式 ID；不能使用名稱或請求 token 代替。</summary>
    public Guid ChannelId { get; }
    /// <summary>資料庫名額正式 ID；同一集合中不得重複或為空。</summary>
    public Guid SlotId { get; }
    /// <summary>伺服器讀取的產品字串，不具有 caller 產品切換權限。</summary>
    public string Product { get; }
    /// <summary>頻道配發優先序，數值越小越優先；同一頻道所有列必須一致。</summary>
    public int PoolOrder { get; }
    /// <summary>一到二十的名額編號，與 Channel ID 組合是資料庫替代鍵。</summary>
    public int SlotNumber { get; }
    /// <summary>頻道的持久狀態，只有營運中可以選取。</summary>
    public ChannelStatus ChannelStatus { get; }
    /// <summary>名額的持久狀態，只有經上游證明乾淨的可用名額可以選取。</summary>
    public SlotStatus SlotStatus { get; }
}
