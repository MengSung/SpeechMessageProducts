/*
檔案整體說明
目的與定位：FS UI 元件 ledger，記錄方案、產品、版本、實體、元件型別、正式元件 ID
與 XML 指紋，讓 apply 前能偵測並行修改，部分失敗後只重試尚缺的元件。
主要流程：Capture 將一個 detached XML 候選雜湊成不可變純值；VerifyFingerprint 比對
完整穩定身份與內容；ReplanOnlyMissing 以正式 ComponentId 計算待補集合，不依名稱合併。
信任邊界與生命週期：不持有 SDK、檔案 stream、使用者／租戶資料或背景資源；呼叫端
應在讀取現況成功後一次建立候選。任何空 ID、重複 ID 或漂移都 fail closed。
驗證方式：UiSolutionLayerTests 驗證指紋漂移拒絕及部分失敗不重送已完成元件；離線結果
不代表任何組織已匯入或發布 UI。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>一個 UI 正式元件的不可變 ledger 純值。</summary>
internal sealed record UiComponentLedgerEntry(
    string Solution,
    string Product,
    string Version,
    string Entity,
    string ComponentType,
    Guid ComponentId,
    string XmlHash);

/// <summary>UI 元件指紋與部分失敗重規劃的純邏輯。</summary>
internal static class UiComponentLedger
{
    /// <summary>以 UTF-8 SHA-256 建立單一元件指紋，不保存 XML 原文。</summary>
    internal static UiComponentLedgerEntry Capture(
        string solution,
        string product,
        string version,
        string entity,
        string componentType,
        Guid componentId,
        string xml)
    {
        if (string.IsNullOrWhiteSpace(solution) || string.IsNullOrWhiteSpace(product) ||
            string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(entity) ||
            string.IsNullOrWhiteSpace(componentType) || componentId == Guid.Empty || xml is null)
            throw new InvalidDataException("UI component ledger identity is incomplete.");
        return new UiComponentLedgerEntry(solution, product, version, entity, componentType,
            componentId, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))));
    }

    /// <summary>完整比對兩筆相同正式 ID 的 ledger；任何 XML 或範圍漂移都拒絕套用。</summary>
    internal static void VerifyFingerprint(UiComponentLedgerEntry planned, UiComponentLedgerEntry current)
    {
        ArgumentNullException.ThrowIfNull(planned);
        ArgumentNullException.ThrowIfNull(current);
        if (planned.ComponentId != current.ComponentId ||
            planned.Solution != current.Solution || planned.Product != current.Product ||
            planned.Version != current.Version || planned.Entity != current.Entity ||
            planned.ComponentType != current.ComponentType || planned.XmlHash != current.XmlHash)
            throw new InvalidDataException("UI component fingerprint drifted after planning.");
    }

    /// <summary>只回傳現況尚未具備的正式元件；重複或空 ID 會先拒絕而不是靜默合併。</summary>
    internal static IReadOnlyList<UiComponentLedgerEntry> ReplanOnlyMissing(
        IEnumerable<UiComponentLedgerEntry> planned,
        IEnumerable<UiComponentLedgerEntry> current)
    {
        var expected = Unique(planned);
        var present = Unique(current);
        foreach (var item in present.Values)
        {
            if (expected.TryGetValue(item.ComponentId, out var candidate))
                VerifyFingerprint(candidate, item);
        }
        return expected.Values.Where(item => !present.ContainsKey(item.ComponentId)).ToArray();
    }

    /// <summary>正式元件 ID 是唯一身份；相同名稱不同 ID 必須保留為不同元件。</summary>
    private static Dictionary<Guid, UiComponentLedgerEntry> Unique(IEnumerable<UiComponentLedgerEntry> values)
    {
        var result = new Dictionary<Guid, UiComponentLedgerEntry>();
        foreach (var value in values)
            if (value is null || value.ComponentId == Guid.Empty || !result.TryAdd(value.ComponentId, value))
                throw new InvalidDataException("Duplicate or empty UI component ledger identity.");
        return result;
    }
}
