// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs
// 目的：用 fake HttpMessageHandler 驗證 live WhoAmI 與 fee FetchXML 路徑。
//
// 保母教學：
// - 這些測試不連真實 CRM。
// - 重點是 URL、編碼、錯誤碼與「禁止呼叫端自帶 FetchXML」。
// ============================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
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

        // CredentialReferenceName 的 bearer 是由 AdfsOAuthTokenProvider 解析，不是 client 自己讀。
        var secrets = new DictionarySecretResolver(new Dictionary<string, string>
        {
            ["ADFS_TOKEN"] = "test-access-token"
        });
        var tokenProvider = new AdfsOAuthTokenProvider(
            options,
            secrets,
            NullLogger<AdfsOAuthTokenProvider>.Instance);

        var client = new DynamicsWebApiClient(
            options,
            transport,
            secrets,
            tokenProvider,
            NullLogger<DynamicsWebApiClient>.Instance);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeTrue();
        seen!.Headers.Authorization.Should().NotBeNull();
        seen.Headers.Authorization!.Scheme.Should().Be("Bearer");
        seen.Headers.Authorization.Parameter.Should().Be("test-access-token");
        seen.RequestUri!.AbsoluteUri.Should().Be("https://crm.example.local/org/api/data/v9.1/WhoAmI");
    }

    [Fact]
    public async Task Content_length_over_response_limit_is_rejected_before_body_read()
    {
        var content = new ThrowOnReadContent(contentLength: 2048);
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        }, options => options.MaxResponseBytes = 1024);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        content.ReadAttempted.Should().BeFalse();
    }

    [Fact]
    public async Task Chunked_response_over_limit_is_rejected_and_stream_is_disposed()
    {
        var content = new TrackingStreamContent(new byte[2048]);
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        }, options => options.MaxResponseBytes = 1024);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        content.StreamDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Compressed_response_is_rejected_before_parsing()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        content.Headers.ContentEncoding.Add("gzip");
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        });

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
    }

    [Fact]
    public async Task Upstream_error_body_is_not_buffered_or_exposed()
    {
        var content = new ThrowOnReadContent(contentLength: null);
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = content
        });

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotContain("upstream-sensitive-body");
        content.ReadAttempted.Should().BeFalse();
    }

    [Fact]
    public async Task Token_provider_exception_text_is_not_returned_to_caller()
    {
        const string sensitiveText = "secret-token-fragment-should-never-leave-host";
        var options = Options.Create(new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "9.1",
            AuthMode = DynamicsAuthMode.AdfsOAuth,
            CredentialReferenceName = "ADFS_TOKEN",
            TimeoutSeconds = 10
        });
        var transport = new DynamicsHttpTransport(
            new StubHandler(_ => JsonResponse("{}")),
            NullLogger<DynamicsHttpTransport>.Instance);
        var client = new DynamicsWebApiClient(
            options,
            transport,
            new DictionarySecretResolver(new Dictionary<string, string>()),
            new ThrowingTokenProvider(sensitiveText),
            NullLogger<DynamicsWebApiClient>.Instance);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotContain(sensitiveText);
        result.ErrorMessage.Should().Be("AdfsOAuth token acquisition failed.");
    }

    /// <summary>
    /// 呼叫端取消不是認證失敗；Token provider 觀察到同一 CancellationToken 後，取消必須原樣向上傳播，
    /// 不能被轉換成 Unauthorized，否則上層會錯誤記錄登入失敗並失去要求中止語意。
    /// </summary>
    [Fact]
    public async Task Caller_cancellation_during_token_acquisition_is_propagated()
    {
        var options = Options.Create(new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "9.1",
            AuthMode = DynamicsAuthMode.AdfsOAuth,
            CredentialReferenceName = "ADFS_TOKEN",
            TimeoutSeconds = 10
        });
        var transport = new DynamicsHttpTransport(
            new StubHandler(_ => throw new InvalidOperationException("transport must not run after cancellation")),
            NullLogger<DynamicsHttpTransport>.Instance);
        var client = new DynamicsWebApiClient(
            options,
            transport,
            new DictionarySecretResolver(new Dictionary<string, string>()),
            new CancellationObservingTokenProvider(),
            NullLogger<DynamicsWebApiClient>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = () => client.WhoAmIAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Transport_exception_details_are_not_logged_or_returned()
    {
        const string sensitiveText = "https://crm.example.local/?token=sensitive-fragment";
        var logger = new CapturingLogger<DynamicsWebApiClient>();
        var client = CreateClient(
            _ => throw new HttpRequestException(sensitiveText),
            logger: logger);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotContain(sensitiveText);
        logger.Exception.Should().BeNull("upstream exceptions can contain URLs, credentials, or tokens");
        logger.Message.Should().NotContain(sensitiveText);
    }

    [Fact]
    public async Task Redirect_location_details_are_not_logged_or_returned()
    {
        const string sensitiveLocation = "https://adfs.example.local/adfs/ls/?wctx=sensitive-context";
        var logger = new CapturingLogger<DynamicsWebApiClient>();
        var client = CreateClient(
            _ => new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri(sensitiveLocation) }
            },
            logger: logger);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotContain(sensitiveLocation);
        logger.Message.Should().NotContain(sensitiveLocation);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Retryable_read_honors_bounded_retry_and_disposes_previous_response(
        HttpStatusCode retryStatus)
    {
        var attempts = 0;
        var firstContent = new TrackingDisposeContent("retry-body-must-not-be-read");
        var client = CreateClient(request =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                var retry = new HttpResponseMessage(retryStatus) { Content = firstContent };
                retry.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return retry;
            }

            return JsonResponse("{\"UserId\":\"33333333-3333-3333-3333-333333333333\"}");
        }, options =>
        {
            options.MaxRetryAttempts = 1;
            options.MaxRetryDelaySeconds = 1;
        });

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeTrue();
        attempts.Should().Be(2);
        firstContent.Disposed.Should().BeTrue();
        firstContent.ReadAttempted.Should().BeFalse();
    }

    [Fact]
    public async Task Non_retryable_failure_is_not_replayed()
    {
        var attempts = 0;
        var client = CreateClient(_ =>
        {
            Interlocked.Increment(ref attempts);
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }, options => options.MaxRetryAttempts = 3);

        var result = await client.WhoAmIAsync();

        result.Succeeded.Should().BeFalse();
        attempts.Should().Be(1);
    }

    private static DynamicsWebApiClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        Action<DynamicsWebApiOptions>? configure = null,
        ILogger<DynamicsWebApiClient>? logger = null)
    {
        var configured = new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "8.2",
            AuthMode = DynamicsAuthMode.Windows,
            CredentialSource = DynamicsCredentialSource.HostIdentity,
            TimeoutSeconds = 10
        };
        configure?.Invoke(configured);
        var options = Options.Create(configured);

        var transport = new DynamicsHttpTransport(
            new StubHandler(responder),
            NullLogger<DynamicsHttpTransport>.Instance);

        return new DynamicsWebApiClient(
            options,
            transport,
            new DictionarySecretResolver(new Dictionary<string, string>()),
            new StaticAdfsOAuthTokenProvider("unused-for-windows"),
            logger ?? NullLogger<DynamicsWebApiClient>.Instance);
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

    /// <summary>
    /// 測試用固定 token 提供者。Windows 模式不會被呼叫；AdfsOAuth 直接 bearer 秘密時也不會被呼叫。
    /// </summary>
    private sealed class StaticAdfsOAuthTokenProvider : IAdfsOAuthTokenProvider
    {
        private readonly string _token;

        public StaticAdfsOAuthTokenProvider(string token) => _token = token;

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_token);
    }

    private sealed class ThrowingTokenProvider : IAdfsOAuthTokenProvider
    {
        private readonly string _message;

        public ThrowingTokenProvider(string message) => _message = message;

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromException<string>(new InvalidOperationException(_message));
    }

    /// <summary>
    /// 測試用 provider 只回傳由呼叫端權杖建立的取消 Task，不保存 Token、要求或其他跨測試狀態。
    /// </summary>
    private sealed class CancellationObservingTokenProvider : IAdfsOAuthTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromCanceled<string>(cancellationToken);
    }

    private sealed class ThrowOnReadContent : HttpContent
    {
        private bool _readAttempted;

        public ThrowOnReadContent(long? contentLength)
        {
            Headers.ContentLength = contentLength;
        }

        public bool ReadAttempted => Volatile.Read(ref _readAttempted);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Volatile.Write(ref _readAttempted, true);
            return Task.FromException(new InvalidOperationException("upstream-sensitive-body"));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = Headers.ContentLength ?? 0;
            return Headers.ContentLength.HasValue;
        }
    }

    private sealed class TrackingStreamContent : HttpContent
    {
        private readonly byte[] _bytes;
        private TrackingMemoryStream? _stream;

        public TrackingStreamContent(byte[] bytes) => _bytes = bytes;

        public bool StreamDisposed => _stream?.Disposed == true;

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            _stream = new TrackingMemoryStream(_bytes);
            return Task.FromResult<Stream>(_stream);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(_bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class TrackingMemoryStream : MemoryStream
    {
        public TrackingMemoryStream(byte[] bytes) : base(bytes, writable: false)
        {
        }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingDisposeContent : StringContent
    {
        private int _disposed;
        private int _readAttempted;

        public TrackingDisposeContent(string content) : base(content, Encoding.UTF8, "text/plain")
        {
        }

        public bool Disposed => Volatile.Read(ref _disposed) == 1;
        public bool ReadAttempted => Volatile.Read(ref _readAttempted) == 1;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Volatile.Write(ref _readAttempted, 1);
            return base.SerializeToStreamAsync(stream, context);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Volatile.Write(ref _disposed, 1);
            }

            base.Dispose(disposing);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public Exception? Exception { get; private set; }
        public string Message { get; private set; } = string.Empty;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Exception = exception;
            Message = formatter(state, exception);
        }
    }
}
