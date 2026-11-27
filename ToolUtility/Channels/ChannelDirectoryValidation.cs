/*
檔案整體說明
目的與定位：集中 F3 SDK adapter 與目錄共同使用的純資料驗證，避免跨層對產品、正式 ID、revision 或容量有不同解讀。
主要功能與流程：先排除空白及其他產品，才驗本產品的正式主鍵、數字 Channel 鍵、版本與 UTC 時間；同一消費集合的重複 Id／鍵拒絕整份，不以顯示名稱合併。比較候選只使用不含秘密的中繼資料。
使用邊界與維護：所有集合限於產品容量且另有一千零二十四筆硬上限，只有操作區域配置，無 SDK、快取、timer 或可釋放資源。外產品通知由呼叫者提供固定告警入口，本檔不讀其密文或格式化來源內容。純值驗證不是租戶授權，呼叫端仍必須固定部署邊界。
*/
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace ToolUtilityNameSpace.Channels;

/// <summary>純驗證及中繼資料比較；所有失敗訊息固定，不包含來源欄位。</summary>
internal static class ChannelDirectoryValidation
{
    /// <summary>額外安全硬上限，主機不得以設定放寬至無界集合。</summary>
    internal const int HardChannelLimit = 1024;

    /// <summary>精確三產品白名單；不 trim、不忽略大小寫，避免部署值跨產品回落。</summary>
    internal static void Deployment(string product, Guid organizationId, int maximumChannels)
    {
        if (product is not ("GoodShepherd2" or "GoodButler" or "GoodGuild") || organizationId == Guid.Empty)
            throw new ArgumentException("A fixed supported product and organization are required.");
        if (maximumChannels < 1 || maximumChannels > HardChannelLimit)
            throw new ArgumentOutOfRangeException(nameof(maximumChannels), "Channel capacity exceeds the safety bound.");
    }

    /// <summary>只檢本產品資料，先拒絕整體超量再建立線性集合；他產品不存入結果也不讀秘密。</summary>
    internal static ChannelDirectoryRecord[] OwnRows(IReadOnlyList<ChannelDirectoryRecord> rows, string product, int maximumChannels, Action? foreign = null)
    {
        if (rows == null || rows.Count > HardChannelLimit) throw new InvalidOperationException("Invalid bounded channel collection.");
        var own = new List<ChannelDirectoryRecord>();
        var ids = new HashSet<Guid>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row == null) throw new InvalidOperationException("Missing channel record.");
            if (string.IsNullOrWhiteSpace(row.Product)) continue;
            if (!string.Equals(row.Product, product, StringComparison.Ordinal)) { foreign?.Invoke(); continue; }
            if (row.Id == Guid.Empty || !Digits(row.ChannelKey, 20) || !Digits(row.Revision, 20)
                || row.ModifiedOn.Kind != DateTimeKind.Utc || !Enum.IsDefined(row.Status)
                || !ids.Add(row.Id) || !keys.Add(row.ChannelKey))
                throw new InvalidOperationException("Invalid or duplicate authoritative channel identity.");
            if (own.Count == maximumChannels) throw new InvalidOperationException("Product channel capacity exceeded.");
            own.Add(row);
        }
        return own.OrderBy(row => row.Id).ToArray();
    }

    /// <summary>數字鍵與版本不接受空白、控制字元或 Unicode 數字，避免路由別名或日誌注入。</summary>
    internal static bool Digits(string? value, int maximum) => !string.IsNullOrEmpty(value)
        && value.Length <= maximum && value.All(character => character is >= '0' and <= '9');

    /// <summary>滿額仍服務既有租戶，登錄／停用／退役均不能取用憑證或健康 HTTP。</summary>
    internal static bool Serviceable(ChannelDirectoryRecord row) => row.Status is DirectoryChannelStatus.Operating or DirectoryChannelStatus.Full;

    /// <summary>比對完整有序候選的正式 Id／產品／鍵／版本／時間／狀態，不比較或讀取密文。</summary>
    internal static bool Same(IReadOnlyList<ChannelDirectoryRecord> first, IReadOnlyList<ChannelDirectoryRecord> second)
    {
        if (first.Count != second.Count) return false;
        for (var i = 0; i < first.Count; i++)
            if (!SameRow(first[i], second[i])) return false;
        return true;
    }

    /// <summary>單列版本比較也包含路由及狀態，禁止將另一正式 target 的版本當成可互換值。</summary>
    internal static bool SameRow(ChannelDirectoryRecord first, ChannelDirectoryRecord second) => first.Id == second.Id
        && string.Equals(first.Product, second.Product, StringComparison.Ordinal)
        && string.Equals(first.ChannelKey, second.ChannelKey, StringComparison.Ordinal)
        && string.Equals(first.Revision, second.Revision, StringComparison.Ordinal)
        && first.ModifiedOn == second.ModifiedOn && first.Status == second.Status;
}
