using Microsoft.Data.SqlClient;

namespace SpeechMessage.Dynamics.WebApi.Capacity;

/// <summary>
/// SQL runtime-host slot coordinator 的有界設定。
/// 連線必須指向獨立的 control-plane database，避免誤用 Dynamics 組織資料庫或其他共用資料庫造成跨環境污染；
/// command timeout 與 quarantine 均設硬上限，確保故障時不會產生無界等待或過久的容量凍結。
/// </summary>
public sealed class SqlRuntimeHostSlotCoordinatorOptions
{
    public const string RequiredDatabaseName = "SpeechMessageDynamicsControlPlane";

    public string ConnectionString { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 5;
    public int QuarantineSeconds { get; set; } = 180;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("Dynamics control-plane SQL connection string is required.");
        }

        var builder = new SqlConnectionStringBuilder(ConnectionString);
        // 固定 InitialCatalog 是部署安全邊界，不允許以連線字串把租約資料寫進 MSCRM_CONFIG 或業務資料庫。
        if (!string.Equals(
                builder.InitialCatalog,
                RequiredDatabaseName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Dynamics coordinator must use the standalone {RequiredDatabaseName} database.");
        }

        if (CommandTimeoutSeconds is < 1 or > 30)
        {
            throw new InvalidOperationException("CommandTimeoutSeconds must be between 1 and 30.");
        }

        if (QuarantineSeconds is < 1 or > 3600)
        {
            throw new InvalidOperationException("QuarantineSeconds must be between 1 and 3600.");
        }
    }
}
