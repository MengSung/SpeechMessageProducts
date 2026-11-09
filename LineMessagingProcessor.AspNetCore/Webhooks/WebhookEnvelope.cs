/*
檔案整體說明
目的與定位：定義供背景處理使用的 detached 不可變事件，只含 channel 識別與單一已解析事件，不保留 HTTP 或部署憑證。
主要功能與流程：建構時驗證 A.8 channel／event ID 長度及 ASCII 邊界、type 與重送形狀，複製 JsonElement 以切斷原始 body／JsonDocument 的資源所有權。預設字串表示不輸出內容。
使用邊界與維護：本值不代表完成授權，只有驗簽成功的 adapter 可移交 queue。Event 保存 provider 事件資料（含事件本身 replyToken），不是 channel secret 或 access token；不得序列化作診斷。HTTP adapter 將事件限制於一 MB 以內，成功後由有界 queue 接管；排隊保留上限由 queue owner 依容量與分區界定，每筆執行及停止排空各最多三十秒，結束立即解除參照。本型別不自行計時或宣稱排隊等待也只有三十秒，不能作跨租戶 cache。
*/
using System.Text.Json;

namespace LineMessagingProcessor.AspNetCore.Webhooks;

/// <summary>單一已驗證頻道的不可變事件快照；JSON clone 獨立於讀取 HTTP 的文件，不能攜帶部署憑證或資源。</summary>
public sealed class WebhookEnvelope
{
    /// <summary>伺服器驗簽已確認的 LINE 頻道 ID；與事件 ID 共同構成 SQL 唯一鍵，禁止跨頻道後備。</summary>
    public string ChannelId { get; }
    /// <summary>provider 的穩定事件 ID，最多六十四個安全 ASCII 字元；不是使用者或資料庫業務記錄身分。</summary>
    public string WebhookEventId { get; }
    /// <summary>provider 指示的重送旗標，只屬本筆投遞，不可用來跳過驗簽或授權。</summary>
    public bool IsRedelivery { get; }
    /// <summary>獨立 clone 的唯讀事件 JSON，供後續 dispatcher 解析；不含原始 body 根或 HTTP 物件。</summary>
    public JsonElement Event { get; }

    /// <summary>先完成整筆形狀與識別檢查再複製發布；失敗固定錯誤不回顯 body 或識別原值。</summary>
    /// <param name="channelId">由受信任驗簽器核對的數字頻道，最多二十位。</param>
    /// <param name="parsedEvent">本次 JSON 文件內單筆已解析事件；此方法不驗簽，呼叫端必須先完成。</param>
    public WebhookEnvelope(string channelId, JsonElement parsedEvent)
    {
        if (!WebhookOptions.IsValidChannelId(channelId) || parsedEvent.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Invalid webhook event boundary.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in parsedEvent.EnumerateObject())
            if (!names.Add(property.Name)) throw new ArgumentException("Ambiguous webhook event.");
        if (!parsedEvent.TryGetProperty("webhookEventId", out var id) || id.ValueKind != JsonValueKind.String ||
            id.GetString() is not { Length: > 0 and <= 64 } value ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.') ||
            !parsedEvent.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
            type.GetString() is not { Length: > 0 and <= 64 })
            throw new ArgumentException("Invalid webhook event identity.");
        if (parsedEvent.TryGetProperty("deliveryContext", out var delivery))
        {
            if (delivery.ValueKind != JsonValueKind.Object ||
                !delivery.TryGetProperty("isRedelivery", out var redelivery) ||
                redelivery.ValueKind is not JsonValueKind.True and not JsonValueKind.False ||
                delivery.EnumerateObject().Count(p => p.NameEquals("isRedelivery")) != 1)
                throw new ArgumentException("Invalid webhook delivery context.");
            IsRedelivery = redelivery.GetBoolean();
        }
        ChannelId = channelId;
        WebhookEventId = value;
        Event = parsedEvent.Clone();
    }
}
