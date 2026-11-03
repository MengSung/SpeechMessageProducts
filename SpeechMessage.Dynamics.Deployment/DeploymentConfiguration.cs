using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>
/// 建立部署 CLI 唯一的組態來源。密碼與其他憑證只由此程序的 User Secrets provider 供應；
/// 本型別不保存單次連線、解析後憑證或跨命令的 mutable 狀態。
/// </summary>
internal static class DeploymentConfiguration
{
    /// <summary>以固定的部署工具組件識別載入選用 User Secrets，缺少 secrets 檔仍可使用離線指令。</summary>
    /// <param name="assembly">含固定 UserSecretsId 的部署工具組件。</param>
    /// <returns>只含 User Secrets provider 的唯讀組態根。</returns>
    public static IConfigurationRoot Create(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return new ConfigurationBuilder()
            .AddUserSecrets(assembly, optional: true)
            .Build();
    }
}
