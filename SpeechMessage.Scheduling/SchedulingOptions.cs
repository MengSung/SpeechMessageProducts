/*
檔案整體說明
目的與定位：單一產品排程部署的不可變設定快照，由受信任組合根提供固定產品、組織及平台SQL連線。
主要功能與流程：核心依產品衍生獨立scheduler schema，不從工作或HTTP接受schema路徑。SQL只用已驗部署連線；workers、停止與重試維持計畫固定值，不以設定放寬。
使用邊界與維護：本物件保留部署連線至Host釋放，不得序列化或寫log；ToString只回固定遮罩。測試停用server的internal旗標只供獨立LocalDB假時鐘驗證，產品入口不可由外部設定讀取該旗標。
*/
using Microsoft.Data.SqlClient;

namespace SpeechMessage.Scheduling;

/// <summary>Host唯一擁有的部署快照；不是租戶請求或作業payload。</summary>
public sealed class SchedulingOptions
{
    /// <summary>固定部署產品。</summary>
    public string Product { get; }
    /// <summary>已由產品組合根驗證的權威組織ID。</summary>
    public Guid OrganizationId { get; }
    /// <summary>只供內部SQL owner建線，不公開為可序列化屬性。</summary>
    internal string ConnectionString { get; }
    /// <summary>固定產品衍生的SQL schema，不接受request或payload指定。</summary>
    internal string SchemaName => "scheduler_" + Product.ToLowerInvariant();
    /// <summary>只有測試朋友組件可延後四worker啟動，正式主機固定true。</summary>
    internal bool StartServer { get; init; } = true;
    /// <summary>拒絕未知產品、空組織、系統庫及不安全TLS；建立專用有界連線池，不立即開SQL。</summary>
    public SchedulingOptions(string product, Guid organizationId, string connectionString)
    {
        if (product is not "GoodShepherd2" and not "GoodButler" and not "GoodGuild")
            throw new ArgumentException("An approved deployment product is required.", nameof(product));
        if (organizationId == Guid.Empty) throw new ArgumentException("An organization is required.", nameof(organizationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var sql = new SqlConnectionStringBuilder(connectionString);
        if (sql.TrustServerCertificate || sql.Encrypt == SqlConnectionEncryptOption.Optional ||
            string.IsNullOrWhiteSpace(sql.InitialCatalog) ||
            new[] { "master", "model", "msdb", "tempdb" }.Contains(sql.InitialCatalog, StringComparer.OrdinalIgnoreCase) ||
            sql.MultipleActiveResultSets)
            throw new ArgumentException("A validated dedicated SQL deployment is required.", nameof(connectionString));
        sql.ApplicationName = "SpeechMessage.Scheduling." + product;
        sql.ConnectTimeout = 5;
        sql.ConnectRetryCount = 0;
        sql.Enlist = false;
        sql.MaxPoolSize = 32;
        sql.MinPoolSize = 0;
        sql.Pooling = true;
        Product = product; OrganizationId = organizationId; ConnectionString = sql.ConnectionString;
    }
    /// <summary>診斷不得曝光連線字串或其中憑證。</summary>
    public override string ToString() => "SchedulingOptions(***)";
}
