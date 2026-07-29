// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/WebApiServiceCollectionExtensionsTests.cs
// 目的：驗證 Dynamics HTTP client 的 DI 與 transport handler 不保留 session 狀態。
// ============================================================================

using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.DependencyInjection;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

public sealed class WebApiServiceCollectionExtensionsTests
{
    /// <summary>
    /// 證明 Multi-Profile DI 只註冊 Runtime Manager／Factory／Admission Registry，
    /// 不建立可被所有 Alias 共用的全域 Client、Transport 或 Token Provider；如此 crm82 與 crm91
    /// 只能由各自 Generation 擁有連線與身分狀態，而相同 Organization 只共享容量權威。
    /// </summary>
    [Fact]
    public async Task Multi_profile_registration_uses_manager_without_global_mutable_client_state()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSpeechMessageDynamicsProfiles(
        [
            CreateProfileDefinition(
                "crm82",
                "8.2",
                Guid.Parse("82828282-8282-8282-8282-828282828282")),
            CreateProfileDefinition(
                "crm91",
                "9.1",
                Guid.Parse("91919191-9191-9191-9191-919191919191"))
        ]);

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var manager = provider.GetRequiredService<IDynamicsProfileRuntimeManager>();

        provider.GetRequiredService<IProfileExecutionLeaseProvider>().Should().BeSameAs(manager);
        provider.GetRequiredService<IDynamicsOperationExecutor>()
            .Should().BeOfType<ProfileRoutedOperationExecutor>();
        provider.GetService<IDynamicsWebApiClient>().Should().BeNull();
        provider.GetService<IDynamicsHttpTransport>().Should().BeNull();
        provider.GetService<IAdfsOAuthTokenProvider>().Should().BeNull();
    }

    [Fact]
    public void Adfs_token_client_uses_a_non_session_primary_handler()
    {
        var capture = new PrimaryHandlerCapture();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(capture);
        services.AddSpeechMessageDynamicsWebApi(ConfigureValidAdfsOptions);

        using var serviceProvider = services.BuildServiceProvider();
        _ = serviceProvider.GetRequiredService<IOptions<DynamicsWebApiOptions>>().Value;
        using var client = serviceProvider.GetRequiredService<IHttpClientFactory>()
            .CreateClient("dynamics-adfs-token");

        capture.PrimaryHandler.Should().BeOfType<SocketsHttpHandler>();
        var handler = (SocketsHttpHandler)capture.PrimaryHandler!;
        handler.UseCookies.Should().BeFalse();
        handler.AllowAutoRedirect.Should().BeFalse();
        handler.UseProxy.Should().BeFalse();
        handler.AutomaticDecompression.Should().Be(DecompressionMethods.None);
        handler.PreAuthenticate.Should().BeFalse();
        handler.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void DynamicsHttpTransport_owned_handler_uses_a_non_session_policy()
    {
        using var transport = new DynamicsHttpTransport(
            Options.Create(CreateValidAdfsOptions()),
            new DictionarySecretResolver(new Dictionary<string, string>()),
            NullLogger<DynamicsHttpTransport>.Instance);

        var handler = GetOwnedHandler(transport);
        handler.UseCookies.Should().BeFalse();
        handler.AllowAutoRedirect.Should().BeFalse();
        handler.UseProxy.Should().BeFalse();
        handler.AutomaticDecompression.Should().Be(DecompressionMethods.None);
        handler.PreAuthenticate.Should().BeFalse();
    }

    private static void ConfigureValidAdfsOptions(DynamicsWebApiOptions options)
    {
        options.OrganizationWebApiBaseUri = "https://crm.example.test/api/data/v9.1/";
        options.AuthMode = DynamicsAuthMode.AdfsOAuth;
        options.CredentialReferenceName = "ADFS_TOKEN";
        options.Admission = new OrganizationAdmissionOptions
        {
            ExpectedOrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111")
        };
    }

    private static DynamicsWebApiOptions CreateValidAdfsOptions()
    {
        var options = new DynamicsWebApiOptions();
        ConfigureValidAdfsOptions(options);
        return options;
    }

    /// <summary>
    /// 建立不解析 Secret、不連真實 CRM 的測試 Profile Definition；Alias、版本、Organization 與 Namespace
    /// 都是固定測試資料，讓 DI 測試只驗證 ownership graph，不啟動 Runtime 或 Host Slot。
    /// </summary>
    private static DynamicsProfileDefinition CreateProfileDefinition(
        string alias,
        string ceVersion,
        Guid organizationId)
        => new(
            alias,
            new DynamicsWebApiOptions
            {
                OrganizationWebApiBaseUri =
                    $"https://{alias}.example.test/api/data/v{ceVersion}/",
                CeVersion = ceVersion,
                MaxConnectionsPerServer = 1,
                Admission = new OrganizationAdmissionOptions
                {
                    ExpectedOrganizationId = organizationId,
                    AggregateMaxInFlight = 2,
                    MaximumRuntimeHosts = 2,
                    AdmissionNamespaceId = alias + "-admission",
                    LeaseNamespaceId = alias + "-lease",
                    RequireDurableHostCoordinator = false
                }
            },
            warmUpOnActivation: false);

    private static SocketsHttpHandler GetOwnedHandler(DynamicsHttpTransport transport)
    {
        var clientField = typeof(DynamicsHttpTransport).GetField(
            "_httpClient",
            BindingFlags.Instance | BindingFlags.NonPublic);
        clientField.Should().NotBeNull();
        var client = clientField!.GetValue(transport) as HttpClient;
        client.Should().NotBeNull();

        var handlerField = typeof(HttpMessageInvoker).GetField(
            "_handler",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? typeof(HttpMessageInvoker).GetField(
                "handler",
                BindingFlags.Instance | BindingFlags.NonPublic);
        handlerField.Should().NotBeNull();
        var handler = handlerField!.GetValue(client) as SocketsHttpHandler;
        handler.Should().NotBeNull();
        return handler!;
    }

    private sealed class PrimaryHandlerCapture : IHttpMessageHandlerBuilderFilter
    {
        public HttpMessageHandler? PrimaryHandler { get; private set; }

        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
            => builder =>
            {
                next(builder);

                if (string.Equals(builder.Name, "dynamics-adfs-token", StringComparison.Ordinal))
                {
                    PrimaryHandler = builder.PrimaryHandler;
                }
            };
    }
}
