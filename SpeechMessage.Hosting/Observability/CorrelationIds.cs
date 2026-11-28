/*
檔案整體說明
目的與定位：共用主機F11的關聯編號純值規則，將HTTP、Webhook與排程的追查命名集中，避免各產品自行拼接不受控文字。
主要功能與流程：HTTP僅原樣接受一至六十四個ASCII英數及連字號，其他輸入新生GUID；已驗證provider事件及伺服器工作鍵各自檢查有界ASCII後加固定前綴。回傳值供ILogger scope及既有發送／稽核correlation參數使用。
使用邊界與維護：關聯編號不是授權或使用者身分，不能放秘密／個資。此純函式不儲存request、AsyncLocal、cache或部署狀態，僅返回當次操作的不可變字串；事件與工作規則不同於HTTP六十四字元上限，不能截斷造成碰撞。測試涵蓋來源邊界、無效字元與穩定前綴。
*/
namespace SpeechMessage.Hosting.Observability;

/// <summary>有界且不包含授權的純字串規則；沒有共用可變資料或資源。</summary>
public static class CorrelationIds
{
    /// <summary>HTTP caller可以提供追查提示，但不合法時完整捨棄，不能把原文或其片段送進診斷。</summary>
    public static string ForHttp(string? header) => header is { Length: > 0 and <= 64 } &&
        header.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? header : Guid.NewGuid().ToString("N");

    /// <summary>採provider已驗事件識別；保留底線與句點，不以HTTP正規化損失事件間的區別。</summary>
    public static string ForWebhook(string eventId)
    {
        RequireIdentifier(eventId, 64, false);
        return "wh-" + eventId;
    }

    /// <summary>工作鍵及持久執行識別必須由排程器供給；不使用租戶名稱或HTTP內容。重試同一執行保留相同編號。</summary>
    public static string ForJob(string jobKey, string executionNumber)
    {
        RequireIdentifier(jobKey, 386, true);
        RequireIdentifier(executionNumber, 128, false);
        return "job-" + jobKey + "-" + executionNumber;
    }

    /// <summary>只驗有界ASCII符號，不回顯不合法輸入；格式檢查不代表業務scope已獲授權。</summary>
    private static void RequireIdentifier(string value, int maximum, bool allowColon)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximum ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.' && !(allowColon && c == ':')))
            throw new ArgumentException("A bounded correlation source identifier is required.", nameof(value));
    }
}
