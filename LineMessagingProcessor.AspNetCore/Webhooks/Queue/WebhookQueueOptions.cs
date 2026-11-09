/*
檔案整體說明
目的與定位：保存單一 Host 的有界佇列啟動快照，容量與分區數只來自組合根，不由事件或使用者覆寫。
主要功能與流程：驗證總容量及固定 consumer 數，公開兩秒入場、三十秒事件及排空期限供 queue 與啟動註冊共用。無效部署值必須在接收 HTTP 前拒絕。
使用邊界與維護：純不可變非秘密值，不保存組態 provider、HttpContext、憑證、使用者 map 或資源。總容量同時計入保留發布、buffer 與執行中的事件，不是每個分區各自的額度；選項只活至目前 Host 結束，測試以合成時鐘驗證期限而不改正式政策。
*/
namespace LineMessagingProcessor.AspNetCore.Webhooks.Queue;

/// <summary>有限、不可變的啟動政策；一個 Host 只有一份快照，不訂閱 reload 或保留 request。</summary>
public sealed class WebhookQueueOptions
{
    /// <summary>所有分區合計的保留、buffer 及執行中上限，預設一千筆。</summary>
    public int Capacity { get; }
    /// <summary>固定序列 consumer 數，預設四個，不依使用者數增生工作或字典。</summary>
    public int PartitionCount { get; }
    /// <summary>提交之前等待容量與分區交接的總期限；超過兩秒允許 HTTP 重送。</summary>
    public static TimeSpan EnqueueTimeout => TimeSpan.FromSeconds(2);
    /// <summary>每筆 handler 的獨立執行期限，內部期限屆滿屬真正失敗。</summary>
    public static TimeSpan EventTimeout => TimeSpan.FromSeconds(30);
    /// <summary>完成在途發布之後的正常排空期限，屆滿取消剩餘工作並等待合作清理。</summary>
    public static TimeSpan DrainTimeout => TimeSpan.FromSeconds(30);

    /// <summary>限制總容量與工作數防止錯誤部署無界配置；此快照不執行 I/O 或取得資源。</summary>
    /// <param name="capacity">全域容量，一至十萬筆；記憶體預算仍須依最大 envelope 大小規劃。</param>
    /// <param name="partitionCount">固定處理迴圈，一至六十四個。</param>
    public WebhookQueueOptions(int capacity = 1000, int partitionCount = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 100000);
        ArgumentOutOfRangeException.ThrowIfLessThan(partitionCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(partitionCount, 64);
        Capacity = capacity;
        PartitionCount = partitionCount;
    }
}
