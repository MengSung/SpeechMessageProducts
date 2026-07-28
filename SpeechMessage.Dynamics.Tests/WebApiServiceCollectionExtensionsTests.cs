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
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.DependencyInjection;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

public sealed class WebApiServiceCollectionExtensionsTests
{
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
