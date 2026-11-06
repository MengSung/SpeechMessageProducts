/*
 * 檔案整體說明
 *
 * 目的與定位：建立部署 CLI 唯一的組態來源。密碼與其他憑證只由此程序的 User Secrets provider 供應； 本型別不保存單次連線、解析後憑證或跨命令的 mutable 狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 DeploymentConfiguration。
 * - 以固定的部署工具組件識別載入選用 User Secrets，缺少 secrets 檔仍可使用離線指令。
 * 主要操作入口包含 Create。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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
