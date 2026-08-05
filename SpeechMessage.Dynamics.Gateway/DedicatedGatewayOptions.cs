// ============================================================================
// 檔案：SpeechMessage.Dynamics.Gateway/DedicatedGatewayOptions.cs
// 用途：驗證 Dedicated Gateway 唯一允許的 deployment-owned host 模式。
// ============================================================================

using Microsoft.Extensions.Configuration;

namespace SpeechMessage.Dynamics.Gateway;

/// <summary>
/// Dedicated Gateway 的不可變啟動選項。
/// 這不是 request options：只在 composition root 讀取一次，之後不保留 configuration reload token、
/// credential、endpoint 或任何 caller 可覆寫資料。驗證採 fail-closed，防止 Dedicated 模式誤接
/// Official Worker 或 SQL coordinator。
/// </summary>
public sealed class DedicatedGatewayOptions
{
    /// <summary>Dedicated Gateway 固定的 deployment mode 字串。</summary>
    public const string DedicatedDeploymentMode = "DedicatedGateway";

    /// <summary>取得由 host 擁有的唯一 profile alias。</summary>
    public required string ProfileAlias { get; init; }

    /// <summary>
    /// 從設定快照讀取並驗證 Dedicated 選項。
    /// profile alias 僅允許安全識別字元，避免被組成 URL、檔案路徑或動態 connector 選擇；它絕不來自
    /// HTTP request，因此不能跨 Organization 覆寫 host 的 runtime。
    /// </summary>
    public static DedicatedGatewayOptions BindAndValidate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var mode = configuration["DynamicsGateway:DeploymentMode"];
        if (!string.Equals(mode, DedicatedDeploymentMode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "DynamicsGateway:DeploymentMode must be exactly DedicatedGateway for the Dedicated Data8 host.");
        }

        var alias = configuration["DynamicsGateway:Dedicated:ProfileAlias"];
        if (string.IsNullOrWhiteSpace(alias) ||
            !string.Equals(alias, alias.Trim(), StringComparison.Ordinal) ||
            alias.Length > 128 ||
            !alias.All(static character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-'))
        {
            throw new InvalidOperationException(
                "DynamicsGateway:Dedicated:ProfileAlias must be a bounded profile alias.");
        }

        return new DedicatedGatewayOptions { ProfileAlias = alias };
    }
}
