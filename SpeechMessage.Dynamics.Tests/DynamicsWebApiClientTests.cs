// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs
// 目的：用 fake HttpMessageHandler 驗證 live WhoAmI 與 fee FetchXML 路徑。
//
// 保母教學：
// - 這些測試不連真實 CRM。
// - 重點是 URL、編碼、錯誤碼與「禁止呼叫端自帶 FetchXML」。
// ============================================================================

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

public sealed class DynamicsWebApiClientTests
{
    [Fact]
    public async Task WhoAmI_calls_approved_root_function()
    {
        HttpRequestMessage? seen = null;
        var client = CreateClient(request =>
        {
            seen = request;
            return JsonResponse("""{"BusinessUnitId":"22222222-2222-2222-2222-222222222222"}""");
        });

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeTrue();
        seen.Should().NotBeNull();
        seen!.Method.Should().Be(HttpMethod.Get);
        seen.RequestUri!.AbsoluteUri.Should().Be("https://crm.example.local/org/api/data/v8.2/WhoAmI");
        seen.Headers.Accept.ToString().Should().Contain("application/json");
    }

    [Fact]
    public async Task Fee_dedication_by_contact_uses_server_owned_fetchxml_and_encodes_guid()
    {
        HttpRequestMessage? seen = null;
        var contactId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var client = CreateClient(request =>
        {
            seen = request;
            return JsonResponse("""{"value":[{"new_feeid":"ffffffff-1111-2222-3333-444444444444"}]}""");
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContact, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?>
            {
                ["contactId"] = contactId,
                ["contactName"] = "O'Brien & Sons"
            });

        result.Succeeded.Should().BeTrue();
        seen.Should().NotBeNull();
        seen!.RequestUri.Should().NotBeNull();
        seen.RequestUri!.AbsolutePath.Should().Be("/org/api/data/v8.2/new_fees");

        var fetchXml = ExtractFetchXml(seen.RequestUri);
        fetchXml.Should().NotBeNullOrWhiteSpace();
        fetchXml.Should().Contain("new_fee");
        fetchXml.Should().Contain("{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}");
        fetchXml.Should().Contain("uiname=\"O&apos;Brien &amp; Sons\"");
        fetchXml.Should().NotContain("{{contactId}}");
    }

    [Fact]
    public async Task Missing_required_fee_parameter_fails_before_http()
    {
        var called = false;
        var client = CreateClient(_ =>
        {
            called = true;
            return JsonResponse("{}");
        });

        Package01OperationRegistry.TryGet(OperationIds.FeeDedicationRetrieveByContactDateRange, out var definition)
            .Should().BeTrue();

        var result = await client.ExecuteRegisteredOperationAsync(
            definition!,
            new Dictionary<string, object?>
            {
                ["contactId"] = Guid.NewGuid().ToString()
            });

        called.Should().BeFalse();
        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.InvalidParameter);
        result.ErrorMessage.Should().Contain("startDate");
    }

    [Fact]
    public async Task Adfs_oauth_sends_bearer_token_from_secret_reference()
    {
        HttpRequestMessage? seen = null;
        var options = Options.Create(new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "9.1",
            AuthMode = DynamicsAuthMode.AdfsOAuth,
            CredentialReferenceName = "ADFS_TOKEN",
            TimeoutSeconds = 10
        });

        var transport = new DynamicsHttpTransport(
            new StubHandler(request =>
            {
                seen = request;
                return JsonResponse("""{"UserId":"33333333-3333-3333-3333-333333333333"}""");
            }),
            NullLogger<DynamicsHttpTransport>.Instance);

        var client = new DynamicsWebApiClient(
            options,
            transport,
            new DictionarySecretResolver(new Dictionary<string, string>
            {
                ["ADFS_TOKEN"] = "test-access-token"
            }),
            NullLogger<DynamicsWebApiClient>.Instance);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeTrue();
        seen!.Headers.Authorization.Should().NotBeNull();
        seen.Headers.Authorization!.Scheme.Should().Be("Bearer");
        seen.Headers.Authorization.Parameter.Should().Be("test-access-token");
        seen.RequestUri!.AbsoluteUri.Should().Be("https://crm.example.local/org/api/data/v9.1/WhoAmI");
    }

    private static DynamicsWebApiClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var options = Options.Create(new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "8.2",
            AuthMode = DynamicsAuthMode.Windows,
            CredentialSource = DynamicsCredentialSource.HostIdentity,
            TimeoutSeconds = 10
        });

        var transport = new DynamicsHttpTransport(
            new StubHandler(responder),
            NullLogger<DynamicsHttpTransport>.Instance);

        return new DynamicsWebApiClient(
            options,
            transport,
            new DictionarySecretResolver(new Dictionary<string, string>()),
            NullLogger<DynamicsWebApiClient>.Instance);
    }

    private static string? ExtractFetchXml(Uri requestUri)
    {
        var query = requestUri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(part[..idx]);
            if (!string.Equals(key, "fetchXml", StringComparison.Ordinal))
            {
                continue;
            }

            return Uri.UnescapeDataString(part[(idx + 1)..]);
        }

        return null;
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}