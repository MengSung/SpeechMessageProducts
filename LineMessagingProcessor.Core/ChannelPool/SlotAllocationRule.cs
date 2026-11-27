/*
檔案整體說明
目的與定位：提供三產品共用名額選取的純規則，不依賴 D365、主機、時鐘、全域鎖或任何使用者狀態。
主要功能與流程：接收至多一萬筆當次交易純值，先完整驗證正式 ID、Channel／編號唯一性及相同頻道中繼資料，再只選本產品營運中的可用名額；依池順序、編號及正式 ID 穩定排序，沒有候選回 PoolFull。
使用邊界與維護：驗證與選取是線性掃描，雜湊集合有明確上限，只存活本次呼叫；不保存 SDK、個資、憑證或資源。上游仍須持有資料庫列鎖、重驗租戶、預算與安全空名額；本檔不提供授權或跨程序同步。測試保護全集合驗證，不准因提早選到結果而忽略後方衝突。
*/
using System;
using System.Collections.Generic;
namespace LineMessagingProcessor.Core.ChannelPool;

/// <summary>交易內使用的純值選取邊界，無共享可變狀態；上游必須持有資料庫列鎖。</summary>
public static class SlotAllocationRule
{
    /// <summary>完整驗證候選後選最低順序名額；重複正式身分或不一致資料以參數例外拒絕，不做去重修補。</summary>
    public static SlotAllocation Choose(string product, IReadOnlyList<ChannelSlotSnapshot> slots)
    {
        if (!PoolProduct.IsSupported(product)) throw new ArgumentException("Unsupported pool product.", nameof(product));
        // Framework 缺少現代守門 API；兩目標維持相同參數拒絕，不抑制分析器或放寬安全條件。
#if NETFRAMEWORK
        if (slots == null) throw new ArgumentNullException(nameof(slots));
#else
        ArgumentNullException.ThrowIfNull(slots);
#endif
        if (slots.Count > 10000) throw new ArgumentException("Pool snapshot exceeds its bound.", nameof(slots));
        var ids = new HashSet<Guid>();
        var keys = new HashSet<Tuple<Guid, int>>();
        var channels = new Dictionary<Guid, ChannelSlotSnapshot>();
        ChannelSlotSnapshot? best = null;
        foreach (var row in slots)
        {
            if (row == null || row.ChannelId == Guid.Empty || row.SlotId == Guid.Empty ||
                !PoolProduct.IsSupported(row.Product) || row.PoolOrder < 0 || row.SlotNumber < 1 || row.SlotNumber > 20 ||
#if NETFRAMEWORK
                !Enum.IsDefined(typeof(ChannelStatus), row.ChannelStatus) || !Enum.IsDefined(typeof(SlotStatus), row.SlotStatus) ||
#else
                !Enum.IsDefined(row.ChannelStatus) || !Enum.IsDefined(row.SlotStatus) ||
#endif
                !ids.Add(row.SlotId) || !keys.Add(Tuple.Create(row.ChannelId, row.SlotNumber)))
                throw new ArgumentException("Invalid or duplicate pool identity.", nameof(slots));
            if (channels.TryGetValue(row.ChannelId, out var prior))
            {
                if (!string.Equals(prior.Product, row.Product, StringComparison.Ordinal) || prior.PoolOrder != row.PoolOrder || prior.ChannelStatus != row.ChannelStatus)
                    throw new ArgumentException("Inconsistent channel projection.", nameof(slots));
            }
            else channels.Add(row.ChannelId, row);
            if (!string.Equals(row.Product, product, StringComparison.Ordinal) || row.ChannelStatus != ChannelStatus.Operating || row.SlotStatus != SlotStatus.Available) continue;
            if (best == null || row.PoolOrder < best.PoolOrder ||
                (row.PoolOrder == best.PoolOrder && (row.SlotNumber < best.SlotNumber ||
                (row.SlotNumber == best.SlotNumber && row.ChannelId.CompareTo(best.ChannelId) < 0)))) best = row;
        }
        return best == null ? SlotAllocation.PoolFull : new SlotAllocation(best.ChannelId, best.SlotId, best.SlotNumber, false);
    }
}
