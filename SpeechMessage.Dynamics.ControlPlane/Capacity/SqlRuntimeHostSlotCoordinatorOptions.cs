using Microsoft.Data.SqlClient;

namespace SpeechMessage.Dynamics.ControlPlane.Capacity;

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

    /// <summary>
    /// 驗證耐久控制平面連線與時間界限。此物件只保留 Gateway 所需的非機密組態；
    /// 若設定嘗試改用 SQL 帳密，必須在任何連線、pool 或背景作業建立前 fail closed，
    /// 避免密碼進入長生命週期 coordinator、記錄或跨世代 runtime 狀態。
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("Dynamics control-plane SQL connection string is required.");
        }

        var builder = new SqlConnectionStringBuilder(ConnectionString);
        // Local Gateway 的控制平面由目前 Windows host identity 擁有；非整合驗證代表
        // 設定可能攜帶 SQL 帳密，故在尚未配置 SqlConnection 前立即拒絕，避免機密被保留。
        if (!builder.IntegratedSecurity)
        {
            throw new InvalidOperationException(
                "Dynamics coordinator must use Windows integrated authentication.");
        }

        // 即使 SqlClient 在整合驗證下忽略 User ID／Password，這些欄位仍存在於 coordinator
        // 長生命週期持有的組態字串；拒絕它們可避免誤植機密在記憶體、例外或診斷流程停留。
        if (!string.IsNullOrWhiteSpace(builder.UserID) ||
            !string.IsNullOrWhiteSpace(builder.Password))
        {
            throw new InvalidOperationException(
                "Dynamics coordinator connection string must not contain SQL credential fields.");
        }
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
