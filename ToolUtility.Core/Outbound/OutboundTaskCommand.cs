/*
 * 檔案整體說明
 *
 * 目的與定位：定義 P2 對外待辦入列的雙目標純值邊界，供 GoodShepherd Writer 與
 * D365 store 共用，不引用 Hosting，也不建立連線、快取或背景工作。
 * 主要功能與流程：以已授權教會、來源事件及三種固定工作類型建立不可變命令；
 * payload 在建構時完整複製、驗證及正規序列化，拒絕文字、巢狀物件與超量內容。
 * 同教會／類型／來源事件形成穩定資料庫替代鍵，不以顯示名稱或臨時 draft key 防重。
 * 使用邊界與維護：教會 ID 的格式驗證不是授權；呼叫端須先授權固定組織、產品與
 * 教會。命令不保存 SDK、Session、credential 或輸入字典，生命期由單次操作擁有；
 * 儲存失敗交最終 Host，錯誤不得含 payload 本文。測試在雙宿主離線執行。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace ToolUtilityNameSpace.Outbound;

/// <summary>只允許計畫核定的三種對外待辦；數值對應 Dataverse A.4 選項，不可任意加入推播工作。</summary>
public enum OutboundTaskType
{
    /// <summary>由 net10 處理器產生指定教會的宮格圖片與設定。</summary>
    RichMenuGenerate = 100000000,
    /// <summary>由 net10 處理器以共用編碼器產生 QR 圖片。</summary>
    QrImageGenerate = 100000001,
    /// <summary>由測試／正式金流部署設定決定端點的撥款查詢，不代表授權真實交易。</summary>
    AllotQuery = 100000002
}

/// <summary>保存單次入列所需的純值，建構時凍結 payload，避免 caller mutation 改變已核對的事件。</summary>
public sealed class OutboundTaskCommand
{
    /// <summary>
    /// 驗證已授權範圍的必要識別並建立穩定替代鍵；僅接受 GUID 與明確整數／十進位
    /// 型別，拒絕自由文字與複合物件。失敗發生在任何 SDK 寫入之前。
    /// </summary>
    /// <param name="type">三種核定的待辦類型。</param>
    /// <param name="churchId">伺服器已授權且存在的教會資料庫 ID。</param>
    /// <param name="contactId">選填且已驗證屬於該教會的會友 ID。</param>
    /// <param name="sourceId">權威業務事件或記錄的穩定 ID，不可每次 retry 重新產生。</param>
    /// <param name="payload">最多十六個具固定程式欄位名稱的 ID／數值，不保留原字典。</param>
    public OutboundTaskCommand(OutboundTaskType type, Guid churchId, Guid? contactId,
        Guid sourceId, IReadOnlyDictionary<string, object> payload)
    {
        if (type is not (OutboundTaskType.RichMenuGenerate or OutboundTaskType.QrImageGenerate or OutboundTaskType.AllotQuery))
            throw new ArgumentOutOfRangeException(nameof(type), "Unsupported outbound task type.");
        if (churchId == Guid.Empty) throw new ArgumentException("A database church identity is required.", nameof(churchId));
        if (sourceId == Guid.Empty) throw new ArgumentException("A stable source identity is required.", nameof(sourceId));
        if (contactId == Guid.Empty) throw new ArgumentException("An optional contact identity must not be empty.", nameof(contactId));
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(payload);
#else
        if (payload is null) throw new ArgumentNullException(nameof(payload));
#endif
        if (payload.Count > 16) throw new ArgumentException("Outbound payload exceeds its bounded schema.", nameof(payload));
        var candidate = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var item in payload)
        {
            if (!IsFieldName(item.Key)
                || item.Value is not (Guid or int or long or decimal)
                || item.Value is Guid id && id == Guid.Empty)
                throw new ArgumentException("Outbound payload permits only named identities and numbers.", nameof(payload));
            candidate.Add(item.Key, item.Value);
        }
        var json = Serialize(candidate);
        if (json.Length > 4000) throw new ArgumentException("Outbound payload exceeds its bounded schema.", nameof(payload));
        Type = type;
        ChurchId = churchId;
        ContactId = contactId;
        IdempotencyKey = churchId.ToString("N") + "-" + ((int)type).ToString(CultureInfo.InvariantCulture) + "-" + sourceId.ToString("N");
        PayloadJson = json;
    }

    /// <summary>核定的類型純值；不能由讀者修改為其他處理器。</summary>
    public OutboundTaskType Type { get; }
    /// <summary>已授權教會的資料庫識別；不保存任何教會設定或帳密。</summary>
    public Guid ChurchId { get; }
    /// <summary>選填的正式會友識別；空值與非空 ID 的契約須在重送時保持相同。</summary>
    public Guid? ContactId { get; }
    /// <summary>包含教會、工作類型與來源事件的固定替代鍵，最長不超過 A.4 的二百字元。</summary>
    public string IdempotencyKey { get; } = "";
    /// <summary>最多四千字元且以欄位名稱排序的正規 JSON；不引用原始輸入物件。</summary>
    public string PayloadJson { get; } = "";

    /// <summary>程式欄位名只接受有界 ASCII 識別字，不接受可攜本文、空白或路徑，避免以鍵名夾帶資料。</summary>
    private static bool IsFieldName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 32) return false;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' || i > 0 && c is >= '0' and <= '9')) return false;
        }
        return true;
    }

    /// <summary>
    /// 僅編碼已白名單的封閉文法：ASCII 識別字鍵、GUID 與十進位數字，不處理任意
    /// 字串或 JSON。所有可變內容已驗證，無需逃脫；不把 JSON runtime／facade 帶入
    /// net462 沙箱相依，亦不透過目前文化格式化數值。最多十六欄，buffer 隨操作回收。
    /// </summary>
    private static string Serialize(SortedDictionary<string, object> values)
    {
        var result = new StringBuilder("{");
        foreach (var item in values)
        {
            if (result.Length > 1) result.Append(',');
            result.Append('"').Append(item.Key).Append("\":");
            if (item.Value is Guid id) result.Append('"').Append(id.ToString("D")).Append('"');
            else if (item.Value is decimal amount) result.Append(amount.ToString("G29", CultureInfo.InvariantCulture));
            else result.Append(((IFormattable)item.Value).ToString(null, CultureInfo.InvariantCulture));
        }
        return result.Append('}').ToString();
    }
}

/// <summary>單次已授權入列的儲存邊界；相同事件只取得既有正式 ID，不重設狀態或覆寫 payload。</summary>
public interface IOutboundEnqueueStore
{
    /// <summary>原子建立或讀回同事件列；唯一鍵衝突必須核對不可變內容，不可靜默合併不同資料。</summary>
    /// <param name="command">已驗證、凍結的事件命令。</param>
    /// <param name="cancellationToken">呼叫者取消；已入場同步 I/O 必須排空才歸還其 owner。</param>
    /// <returns>資料庫產生的非空正式待辦 ID，不能是來源事件或臨時索引。</returns>
    Guid CreateOrGet(OutboundTaskCommand command, CancellationToken cancellationToken);
}
