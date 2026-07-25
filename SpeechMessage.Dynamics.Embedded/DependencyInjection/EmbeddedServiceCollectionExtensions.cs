// ============================================================================
// 檔案：SpeechMessage.Dynamics.Embedded/DependencyInjection/EmbeddedServiceCollectionExtensions.cs
// 目的：讓產品在 Visual Studio / 隔離部署中啟用 Embedded 模式。
//
// 保母教學：
// - 產品若選 Embedded，只應 reference 這個專案 + Abstractions。
// - 絕不要 reference WebApi。
// - Embedded 內部仍走 IDynamicsOperationExecutor，操作集合與 Gateway 相同。
// - 產品 JSON 可用 ExecutionMode 在 Gateway / Embedded 間切換。
// ============================================================================

using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.WebApi.DependencyInjection;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Embedded.DependencyInjection;

/// <summary>
/// Embedded 主機 DI 擴充。
/// </summary>
public static class EmbeddedServiceCollectionExtensions
{
    /// <summary>
    /// 以產品 DynamicsAccess 設定啟用 Embedded 受控操作執行器。
    /// </summary>
    public static IServiceCollection AddSpeechMessageDynamicsEmbedded(
        this IServiceCollection services,
        ProductDynamicsOptions productOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(productOptions);

        if (productOptions.ExecutionMode != DynamicsExecutionMode.Embedded)
        {
            throw new InvalidOperationException(
                "AddSpeechMessageDynamicsEmbedded requires ExecutionMode=Embedded.");
        }

        if (productOptions.Embedded is null)
        {
            throw new InvalidOperationException(
                "Embedded options are required when ExecutionMode=Embedded.");
        }

        var embedded = productOptions.Embedded;
        services.AddSpeechMessageDynamicsWebApi(options =>
        {
            options.OrganizationWebApiBaseUri = embedded.OrganizationWebApiBaseUri;
            options.CeVersion = embedded.CeVersion;
            options.AuthMode = DynamicsAuthMode.Windows;
            options.CredentialSource = DynamicsCredentialSource.HostIdentity;
            options.SecretReference = embedded.SecretReference;
            options.TimeoutSeconds = 30;
        });

        // 之後這裡會加 manifest/registry fail-closed 驗證。
        // 現在先把執行器註冊起來，讓產品可先接契約與本機 live HTTP。
        return services;
    }
}
