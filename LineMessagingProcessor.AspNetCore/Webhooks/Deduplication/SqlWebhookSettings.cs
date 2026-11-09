/*
檔案整體說明
目的與定位：保存平台 SQL 的可信啟動快照，從 DI 與 HTTP 可變組態隔離連線設定，禁止把 request 值當作資料庫權限。
主要功能與流程：組合根只讀 ConnectionStrings:Platform 一次；解析時驗證純量與資料來源／資料庫，固定五秒連線期限、停用自動連線重試及 ambient enlistment，限制池最多一百條且無最少常駐連線，回池時淘汰壽命超過五分鐘的連線。
使用邊界與維護：只消費組合根提供的連線字串，不自行開啟秘密儲存區、不輸出原連線或解析錯誤內容，不降低部署加密政策。Host 容器唯一擁有本快照，Dispose 清空字串；不訂閱 reload。無效設定延至服務解析拒絕，讓 Host 啟動錯誤經既有共享診斷處理，尚未開始任何 SQL I/O。
*/
using Microsoft.Data.SqlClient;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Deduplication;

/// <summary>單一 Host 的部署快照；任何解析失敗只輸出固定訊息，不回顯來源或憑證。</summary>
internal sealed class SqlWebhookSettings : IDisposable
{
    private string? connectionString;
    private readonly bool invalid;
    private bool disposed;

    /// <summary>在組合期快照化，僅接受純量連線設定；不連線、不保留 IConfiguration。</summary>
    internal SqlWebhookSettings(string? value, bool hasChildren)
    {
        if (hasChildren || string.IsNullOrWhiteSpace(value)) { invalid = true; return; }
        try
        {
            var builder = new SqlConnectionStringBuilder(value)
            {
                ConnectTimeout = 5, ConnectRetryCount = 0, Enlist = false,
                MultipleActiveResultSets = false, MinPoolSize = 0
            };
            builder.MaxPoolSize = Math.Min(builder.MaxPoolSize, 100);
            builder.LoadBalanceTimeout = builder.LoadBalanceTimeout == 0 ? 300 : Math.Min(builder.LoadBalanceTimeout, 300);
            invalid = string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.InitialCatalog);
            if (!invalid) connectionString = builder.ConnectionString;
        }
        catch (ArgumentException) { invalid = true; }
    }

    /// <summary>只給可信 factory 建新連線；缺項或格式錯誤一律在服務啟動前拒絕。</summary>
    internal string ConnectionString
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (invalid || connectionString is null)
                throw new InvalidOperationException("Webhook requires a valid ConnectionStrings:Platform startup snapshot.");
            return connectionString;
        }
    }

    /// <summary>Host 關閉後清除此快照所屬池並移除字串參照；不呼叫 ClearAllPools、不接觸其他部署資料庫。</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (connectionString is not null)
            { using var connection = new SqlConnection(connectionString); SqlConnection.ClearPool(connection); }
        }
        finally { connectionString = null; }
    }
}
