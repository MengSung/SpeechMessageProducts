// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs
// 目的：註冊私有 WebApi transport、client 與受控 executor。
//
// 保母教學：
// - 產品不要直接呼叫這個 DI 擴充；請走 Gateway 或 Embedded 入口。
// - transport 是 singleton，對應「一個 profile runtime 一個長壽命 HttpClient」。
// - 這裡不做 per-user session pool。
// ============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.WebApi.DependencyInjection;

/// <summary>
/// WebApi 層 DI 擴充方法。
/// </summary>
public static class WebApiServiceCollectionExtensions
{
    /// <summary>
    /// 註冊私有 Dynamics Web API 連線器與受控操作執行器。
    /// </summary>
    public static IServiceCollection AddSpeechMessageDynamicsWebApi(
        this IServiceCollection services,
        Action<DynamicsWebApiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<DynamicsWebApiOptions>()
            .Configure(configure)
            .Validate(options =>
            {
                // 最小驗證：至少要能推出 ApprovedWebApiRoot，且 auth 形狀合理。
                if (!ApprovedWebApiRootFactory.TryCreate(options, out _, out _))
                {
                    return false;
                }

                if (options.AuthMode == DynamicsAuthMode.Windows &&
                    options.CredentialSource == DynamicsCredentialSource.SecretReference &&
                    (string.IsNullOrWhiteSpace(options.UserNameSecretName) ||
                     string.IsNullOrWhiteSpace(options.PasswordSecretName)))
                {
                    return false;
                }

                if (options.AuthMode == DynamicsAuthMode.AdfsOAuth &&
                    string.IsNullOrWhiteSpace(options.CredentialReferenceName) &&
                    string.IsNullOrWhiteSpace(options.SecretReference))
                {
                    return false;
                }

                return true;
            }, "DynamicsWebApi options failed validation.")
            .ValidateOnStart();

        services.TryAddSingleton<ISecretResolver, EnvironmentSecretResolver>();
        services.AddSingleton<IDynamicsHttpTransport, DynamicsHttpTransport>();
        services.AddSingleton<IDynamicsWebApiClient, DynamicsWebApiClient>();
        services.AddSingleton<IDynamicsOperationExecutor, ControlledOperationExecutor>();
        return services;
    }
}
