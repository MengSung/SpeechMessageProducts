/*
檔案整體說明
目的與定位：實作 P3-02 預留的 RegisterQueue partial 接縫，只組合本包 bounded queue，不修改其他依賴或 Program。
主要功能與流程：已有明確 queue 註冊時保留組合根選擇；否則從部署嚴格解析容量與分區快照，交獨立入口建立唯一 DI owner 與 Host 轉接。
使用邊界與維護：啟動時讀設定，不保存 provider、不訂閱 reload，不讀 HTTP、憑證或租戶資料。非法設定立即拒絕，正式缺 handler 在 StartingAsync 拒絕。既有替身 Host 不會多建立隱藏 consumer；正式預設是本包 queue，不能 silent noop。驗證以真組合根及啟動拒絕測試完成。
*/
using System.Globalization;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LineMessagingProcessor.AspNetCore.Webhooks;

public static partial class WebhookServiceRegistration
{
    /// <summary>只補 queue 接縫；已明確註冊時不建立第二個 owner，其實作由組合根負責。</summary>
    static partial void RegisterQueue(IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(d => d.ServiceType == typeof(IWebhookEventQueue))) return;
        services.AddBoundedWebhookQueue(new WebhookQueueOptions(
            ReadQueueInteger(configuration, "Capacity", 1000), ReadQueueInteger(configuration, "PartitionCount", 4)));
    }

    /// <summary>只接受無子節點的十進位整數，不依文化猜測值、不輸出原設定。</summary>
    private static int ReadQueueInteger(IConfiguration configuration, string name, int fallback)
    {
        var section = configuration.GetSection("LineWebhook:Queue:" + name);
        if (section.GetChildren().Any()) throw new InvalidOperationException("Invalid webhook queue configuration.");
        if (section.Value is null) return fallback;
        if (!int.TryParse(section.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException("Invalid webhook queue configuration.");
        return value;
    }
}
