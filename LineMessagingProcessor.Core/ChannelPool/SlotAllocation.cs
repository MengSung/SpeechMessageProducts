/*
檔案整體說明
目的與定位：描述純配發規則的不可變選取結果，讓上游區分池滿與一組正式 Channel／Slot 身分，避免以空實體或顯示名稱混淆結果。
主要功能與流程：Choose 在完整驗證候選後建立成功結果；找不到名額時共用唯讀 PoolFull。結果不執行資料庫寫入，成功選取仍須由持有交易列鎖的 Plugin 更新名額、租戶及申請單。
使用邊界與維護：只有純值，靜態池滿實例不含任何產品、租戶、憑證或可變集合，無資源需要 Dispose。成功結果只屬於本次交易，不得快取後跨請求重用；測試驗證池滿不被當成成功，以及成功 ID 來源精確一致。
*/
using System;
namespace LineMessagingProcessor.Core.ChannelPool;

/// <summary>已驗證的單次選取結果；不代表資料庫交易已提交，也不包含租戶或認證物件。</summary>
public sealed class SlotAllocation
{
    /// <summary>建立不可變結果；僅規則能建構成功值，避免 caller 偽造選取。</summary>
    internal SlotAllocation(Guid channelId, Guid slotId, int slotNumber, bool isPoolFull)
    { ChannelId = channelId; SlotId = slotId; SlotNumber = slotNumber; IsPoolFull = isPoolFull; }
    /// <summary>不含租戶資料的共享池滿值；不能修改或當作實際名額發布。</summary>
    public static SlotAllocation PoolFull { get; } = new SlotAllocation(Guid.Empty, Guid.Empty, 0, true);
    /// <summary>選取的正式頻道 ID；池滿時為空。</summary>
    public Guid ChannelId { get; }
    /// <summary>選取的正式名額 ID；池滿時為空。</summary>
    public Guid SlotId { get; }
    /// <summary>選取名額編號，池滿時為零，不作為資料庫 ID 使用。</summary>
    public int SlotNumber { get; }
    /// <summary>表示未選到可用名額；上游應回待配發，而非猜測預設頻道。</summary>
    public bool IsPoolFull { get; }
}
