/*
 * 檔案整體說明
 *
 * 目的與定位：雙目標 Core 共用的宮格 alias 純命名函式，供多語產生器與會友換綁使用；
 * 由既有 RichMenuGenerateHandler.BuildAlias 原樣搬移規則，避免兩個宿主各自命名而分歧。
 * 主要功能與流程：接收已由呼叫端授權的教會、組合、頁籤及語系，只將文化尾碼小寫化，
 * qps-ploc 映射 qps；四個元件均須為 LINE 允許的小寫 ASCII，連接後不得超過 32 字元。
 * 使用邊界與維護：不清洗或截斷業務識別，不執行授權、查詢、解密、網路或快取；
 * 非法輸入以固定 ArgumentException 拒絕且不回顯值。只回傳當次不可變字串，沒有資源 owner。
 * 驗證由既有生成器相容案例與雙目標會友換綁測試保護，不把純命名視為 provider 實機成功。
 */
using System;
using System.Linq;
namespace LineMessagingProcessor.Core;
/// <summary>跨宿主共用的純 alias 規則；只格式驗證，不把教會代碼或語言當成授權。</summary>
public static class RichMenuAlias
{
    /// <summary>
    /// 建立固定格式 alias；LINE 只接受小寫 ASCII、數字、底線及連字號。
    /// 文化標籤不分大小寫，僅尾碼轉小寫；教會／組合／頁籤不可清洗或截斷，避免身分碰撞。
    /// 本次呼叫只配置暫時元件陣列與回傳字串，不保存輸入或建立任何 I/O。
    /// </summary>
    /// <param name="churchCode">伺服器讀出的教會代碼，原樣驗證而不轉小寫。</param>
    /// <param name="combination">正式版面組合代碼，不得以顯示名稱代替。</param>
    /// <param name="tab">正式頁籤代碼，原樣驗證。</param>
    /// <param name="culture">政策已決定的語系；偽語系 qps-ploc 使用 qps 尾碼。</param>
    /// <returns>以連字號連接、最多 32 字元的當次不可變 alias。</returns>
    /// <exception cref="ArgumentException">元件空值、非法字元或完整 alias 超長；訊息不包含輸入。</exception>
    public static string Build(string churchCode, string combination, string tab, string culture)
    {
        var suffix = string.Equals(culture, "qps-ploc", StringComparison.OrdinalIgnoreCase)
            ? "qps" : culture?.ToLowerInvariant();
        foreach (var part in new[] { churchCode, combination, tab, suffix })
            if (string.IsNullOrEmpty(part) ||
                part.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')))
                throw new ArgumentException("Rich menu alias components are invalid.");
        var result = string.Join("-", churchCode, combination, tab, suffix);
        if (result.Length > 32) throw new ArgumentException("Rich menu alias exceeds 32 characters.");
        return result;
    }
}
