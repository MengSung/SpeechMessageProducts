// ============================================================================
// 檔案：SpeechMessage.Dynamics.SmokeTests/LiveDynamicsWebApiSmokeTests.cs
// 目的：可選的真實 CE Web API 煙測（預設關閉）。
//
// 保母教學：
// 1. 預設不打外部系統，避免 CI / 無環境機器失敗。
// 2. 只有同時滿足以下條件才會真正外呼：
//    - DYNAMICS_SMOKE_ENABLED=1
//    - DYNAMICS_SMOKE_WEBAPI_ROOT=https://.../api/data/v9.1/
//    - DYNAMICS_SMOKE_CE_VERSION=9.1 或 8.2
// 3. 認證：
//    - HostIdentity（本機 Windows 服務身分）或
//    - SecretReference + 環境變數帳密參考
//    - AdfsOAuth（IFD）：DYNAMICS_SMOKE_AUTH_MODE=AdfsOAuth + Authority/ClientId/password grant
// 4. 只做 WhoAmI 與可選 fee-read（需要 contactId），不寫入 CRM。
// ============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.DependencyInjection;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.SmokeTests;

public sealed class LiveDynamicsWebApiSmokeTests
{
    [Fact]
    public void Live_smoke_is_disabled_by_default()
    {
        Assert.False(IsEnabled());
    }

    [Fact]
    public async Task WhoAmI_live_smoke_when_enabled()
    {
        if (!IsEnabled())
        {
            // 預設略過：這不是失敗，而是保護 CI。
            return;
        }

        var root = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_WEBAPI_ROOT");
        var ce = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_CE_VERSION") ?? "9.1";
        Assert.False(string.IsNullOrWhiteSpace(root), "DYNAMICS_SMOKE_WEBAPI_ROOT is required when smoke is enabled.");

        using var provider = BuildProvider(root!, ce);
        var client = provider.GetRequiredService<IDynamicsWebApiClient>();
        var result = await client.WhoAmIAsync();

        Assert.True(result.Succeeded, result.ErrorMessage ?? "WhoAmI failed.");
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task Fee_date_range_live_smoke_when_contact_provided()
    {
        if (!IsEnabled())
        {
            return;
        }

        var contactRaw = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_CONTACT_ID");
        if (string.IsNullOrWhiteSpace(contactRaw) || !Guid.TryParse(contactRaw, out var contactId))
        {
            // 沒有 contactId 就只跑 WhoAmI，不算失敗。
            return;
        }

        var root = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_WEBAPI_ROOT");
        var ce = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_CE_VERSION") ?? "9.1";
        Assert.False(string.IsNullOrWhiteSpace(root));

        using var provider = BuildProvider(root!, ce);
        var executor = provider.GetRequiredService<IDynamicsOperationExecutor>();

        var start = DateTime.UtcNow.Date.AddDays(-30);
        var end = DateTime.UtcNow.Date;
        var result = await executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_PROFILE_ALIAS") ?? "jesus-prod",
            CapabilityOperationId = OperationIds.FeeDedicationRetrieveByContactDateRange,
            WorkloadSubjectId = "dynamics-smoke",
            Parameters = new Dictionary<string, object?>
            {
                ["contactId"] = contactId,
                ["startDate"] = start,
                ["endDate"] = end
            }
        });

        Assert.True(result.Succeeded, result.ErrorMessage ?? "fee date-range smoke failed.");
    }

    private static bool IsEnabled()
        => string.Equals(
            Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_ENABLED"),
            "1",
            StringComparison.Ordinal);

    private static ServiceProvider BuildProvider(string webApiRoot, string ceVersion)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSpeechMessageDynamicsWebApi(options =>
        {
            options.OrganizationWebApiBaseUri = webApiRoot;
            options.CeVersion = ceVersion;
            options.AuthMode = ParseAuthMode(
                Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_AUTH_MODE"));
            options.CredentialSource = ParseCredentialSource(
                Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_CREDENTIAL_SOURCE"));
            options.UserNameSecretName = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_USERNAME_SECRET");
            options.PasswordSecretName = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_PASSWORD_SECRET");
            options.DomainSecretName = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_DOMAIN_SECRET");
            options.AuthorityUri = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_AUTHORITY");
            options.ResourceUri = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_RESOURCE");
            options.ClientId = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_CLIENT_ID");
            options.CredentialReferenceName = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_BEARER_SECRET");
            options.SecretReference = Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_SECRET_REFERENCE")
                ?? "dynamics-smoke-credential";
            options.AllowLocalDevPasswordGrant =
                string.Equals(Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_ALLOW_PASSWORD_GRANT"), "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Environment.GetEnvironmentVariable("DYNAMICS_SMOKE_ALLOW_PASSWORD_GRANT"), "true", StringComparison.OrdinalIgnoreCase);
            options.TimeoutSeconds = 30;
            options.MaxConnectionsPerServer = 2;
            options.Admission.ExpectedOrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            options.Admission.AggregateMaxInFlight = 4;
            options.Admission.MaximumRuntimeHosts = 2;
            options.Admission.LocalQueueCapacity = 8;
            options.Admission.MaxInFlightAndQueuedPerWorkload = 2;
            options.Admission.AdmissionNamespaceId = "smoke-admission";
            options.Admission.LeaseNamespaceId = "smoke-lease";
            options.Admission.RequireDurableHostCoordinator = false;
        });
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static DynamicsCredentialSource ParseCredentialSource(string? raw)
    {
        if (string.Equals(raw, "SecretReference", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicsCredentialSource.SecretReference;
        }

        return DynamicsCredentialSource.HostIdentity;
    }

    private static DynamicsAuthMode ParseAuthMode(string? raw)
    {
        if (string.Equals(raw, "AdfsOAuth", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicsAuthMode.AdfsOAuth;
        }

        return DynamicsAuthMode.Windows;
    }
}