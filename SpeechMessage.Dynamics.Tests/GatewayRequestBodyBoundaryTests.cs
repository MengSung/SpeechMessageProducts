using System.Buffers;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.Gateway.RequestLimits;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Gateway inbound operation body 的硬邊界：authentication 與 immutable workload authorization 必須先於任何 body read，
/// 已授權要求才可進入 deployment-owned byte limit 與 JSON shape/depth 驗證；所有拒絕都必須在 executor、admission 與 outbound transport 前完成。
/// 每個 Factory、stream、HTTP content、client 與測試 executor 都由單一案例擁有並確定性 Dispose，不共享 principal、body、buffer、token、session 或背景工作。
/// </summary>
public sealed class GatewayRequestBodyBoundaryTests
{
    private const string PrincipalName = @"SPEECHMESSAGE\RequestBoundary$";
    private const string OperationPath =
        "/v1/organizations/crm82/operations/runtime.health.whoami";

    /// <summary>
    /// 已通過 authentication 但沒有 workload binding 的 caller，即使宣告超限且 body 是 malformed JSON，也必須先得到一致 403；
    /// server 不可讀取 request stream，避免 body parsing、配置 buffer 或錯誤差異形成 principal/alias/operation authorization oracle。
    /// Tracking stream 由測試擁有，Factory 不得 Dispose；executor count 必須維持零，證明拒絕早於 materialization 與 outbound ownership。
    /// </summary>
    [Fact]
    public async Task Authenticated_unmapped_request_is_forbidden_before_any_body_read()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            maxRequestBodyBytes: 64,
            mapped: false,
            useKestrel: false);
        var body = new TrackingReadStream(new byte[65]);

        var response = await SendThroughTestServerAsync(
            factory,
            body,
            declaredContentLength: 65,
            contentType: "text/plain");

        response.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        body.ReadCount.Should().Be(0);
        body.IsDisposed.Should().BeFalse("ASP.NET Core owns the request stream; the reader must not dispose it");
        executor.CallCount.Should().Be(0);
        body.Dispose();
    }

    /// <summary>
    /// 驗證已通過身分與 operation 授權的 caller，若缺少 Content-Type、宣告非 JSON 媒體型別、
    /// 使用未核准的 structured suffix、指定非 UTF-8 charset，或夾帶未知參數，Gateway 必須在讀取
    /// request stream、租用 pooled buffer 與呼叫 executor 之前，以 415 fail closed。這項契約避免
    /// caller 以可解析的 JSON 內容繞過 JSON-only HTTP 邊界，也保證錯誤路徑不延長 body、principal、
    /// token、session 或 transport 資源的存活時間；request stream 仍由 ASP.NET Core 擁有並由測試端釋放。
    /// </summary>
    /// <param name="contentType">要注入的未支援 Content-Type；null 與空字串代表未宣告媒體型別。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("text/plain")]
    [InlineData("application/problem+json")]
    [InlineData("application/json; charset=utf-16")]
    [InlineData("application/json; charset=utf-8; charset=utf-8")]
    [InlineData("application/json; boundary=unexpected")]
    public async Task Authorized_request_with_unsupported_content_type_is_rejected_before_body_read(
        string? contentType)
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            maxRequestBodyBytes: 128,
            mapped: true,
            useKestrel: false);
        var bodyBytes = Encoding.UTF8.GetBytes("{\"parameters\":{}}");
        var body = new TrackingReadStream(bodyBytes);

        var response = await SendThroughTestServerAsync(
            factory,
            body,
            declaredContentLength: bodyBytes.Length,
            contentType: contentType);

        response.Response.StatusCode.Should().Be(StatusCodes.Status415UnsupportedMediaType);
        body.ReadCount.Should().Be(0,
            "unsupported media types must fail before request-body I/O or pooled-buffer ownership begins");
        body.IsDisposed.Should().BeFalse(
            "ASP.NET Core, not the Gateway reader, owns the inbound request stream");
        executor.CallCount.Should().Be(0);
        body.Dispose();
    }

    /// <summary>
    /// 驗證 JSON-only 契約仍接受大小寫不同的 <c>application/json</c>，以及省略或明確宣告
    /// UTF-8 charset 的標準形式。測試必須走真實 TestServer endpoint，確保媒體型別 gate 不會錯誤
    /// 阻擋合法呼叫；body 只在授權與 header 驗證完成後被讀取一次，executor 也只能取得一份已界定
    /// 上限的 DTO，不得保留 HttpContext、request stream、principal、token 或 session reference。
    /// </summary>
    /// <param name="contentType">要驗證的標準 JSON Content-Type 表示法。</param>
    [Theory]
    [InlineData("application/json")]
    [InlineData("APPLICATION/JSON")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/json; charset=UTF-8")]
    [InlineData("application/json; charset=\"utf-8\"")]
    public async Task Authorized_request_with_supported_json_content_type_reaches_executor(
        string contentType)
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            maxRequestBodyBytes: 128,
            mapped: true,
            useKestrel: false);
        var bodyBytes = Encoding.UTF8.GetBytes("{\"parameters\":{}}");
        var body = new TrackingReadStream(bodyBytes);

        var response = await SendThroughTestServerAsync(
            factory,
            body,
            declaredContentLength: bodyBytes.Length,
            contentType: contentType);

        response.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        body.ReadCount.Should().BeGreaterThan(0);
        body.IsDisposed.Should().BeFalse();
        executor.CallCount.Should().Be(1);
        body.Dispose();
    }

    /// <summary>
    /// 保護已通過 HTTP 身分與 JSON media type 的請求仍不可利用 body 夾帶 OrganizationId。
    /// 此案例刻意讓 Gateway 完整讀取受限 body，接著驗證 RequestGuard 在任何 executor、
    /// admission permit、Profile resolver 或 Connector 建立前回傳 400；Tracking stream 的
    /// owner 仍是 ASP.NET Core，測試結束才由呼叫端釋放，避免測試本身製造 stream 泄漏。
    /// </summary>
    [Fact]
    public async Task Reserved_routing_parameter_is_rejected_before_executor_invocation()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            maxRequestBodyBytes: 128,
            mapped: true,
            useKestrel: false);
        var bodyBytes = Encoding.UTF8.GetBytes(
            "{\"parameters\":{\"organizationId\":\"untrusted\"}}");
        var body = new TrackingReadStream(bodyBytes);

        var response = await SendThroughTestServerAsync(
            factory,
            body,
            declaredContentLength: bodyBytes.Length,
            contentType: "application/json");

        response.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        body.ReadCount.Should().BeGreaterThan(0);
        executor.CallCount.Should().Be(0,
            "RequestGuard must reject routing overrides before any Dynamics runtime resource is touched");
        body.Dispose();
    }

    /// <summary>
    /// 已授權要求若 Content-Length 在讀取前已超過 deployment limit，必須直接 413 且完全不觸碰 stream；
    /// 這避免攻擊者用宣告長度迫使配置／讀取 body，也確保 executor、admission permit 與 Dynamics transport
    /// 都不被建立。失敗路徑不得租用 pooled buffer、建立 cancellation registration，或改變 ASP.NET Core
    /// 對 request stream 的唯一 ownership，讓拒絕要求不會造成記憶體、Handle 或生命週期資源滯留。
    /// </summary>
    [Fact]
    public async Task Declared_oversize_is_rejected_without_reading_request_stream()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            maxRequestBodyBytes: 64,
            mapped: true,
            useKestrel: false);
        var body = new TrackingReadStream(new byte[65]);

        var response = await SendThroughTestServerAsync(
            factory,
            body,
            declaredContentLength: 65);

        response.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        body.ReadCount.Should().Be(0);
        body.IsDisposed.Should().BeFalse();
        executor.CallCount.Should().Be(0);
        body.Dispose();
    }

    /// <summary>
    /// 真實 Kestrel HTTP/1.1 必須對 declared 與 chunked 的 limit+1 wire bytes 都回傳 413；
    /// declared path 可由 header precheck 拒絕，chunked path 則只能實際讀到第 limit+1 byte 後拒絕，兩者都不得進 executor。
    /// 測試使用 portable Testing handler 而非機器特定 Windows credential；Factory/HttpClient/HttpContent/stream 各有唯一 owner 並依 using 清理 socket 與 body 資源。
    /// </summary>
    [Fact]
    public async Task Kestrel_http11_rejects_declared_and_chunked_limit_plus_one()
    {
        const int limit = 64;
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            maxRequestBodyBytes: limit,
            mapped: true,
            useKestrel: true);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var address = factory.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .Single();
        var serverUri = new Uri(address, UriKind.Absolute);
        serverUri.Port.Should().NotBe(
            5000,
            "the Kestrel boundary fixture must use an OS-assigned per-case port so another process cannot turn a body-limit assertion into a transport failure");
        client.BaseAddress = serverUri;

        using var declaredContent = new TrackingByteArrayContent(new byte[limit + 1]);
        using (var declaredRequest = CreateHttp11Request(declaredContent))
        using (var declaredResponse = await client.SendAsync(declaredRequest))
        {
            declaredResponse.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        }

        declaredContent.IsDisposed.Should().BeTrue(
            "the request owns and deterministically disposes its HTTP content");

        using var chunkedStream = new NonSeekableReadStream(new byte[limit + 1]);
        var chunkedContent = new StreamContent(chunkedStream);
        using (var chunkedRequest = CreateHttp11Request(chunkedContent))
        {
            chunkedRequest.Headers.TransferEncodingChunked = true;
            using var chunkedResponse = await client.SendAsync(chunkedRequest);
            chunkedResponse.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        }

        chunkedStream.IsDisposed.Should().BeTrue(
            "disposing the request must release the StreamContent and its caller-supplied stream");
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 超過絕對 hard ceiling 的 deployment 設定必須在 listener 接受流量前停止 Host；
    /// 不能由 Kestrel、IIS 或 reader 各自截斷成不同值，否則不同 hosting topology 會形成不一致的記憶體與安全邊界。
    /// 直接驗證正式 Kestrel callback 與 Options validation 共用的 <see cref="GatewayRequestBodyLimitOptions.BindAndValidate"/>
    /// 契約，避免預期 startup failure 經過 top-level <c>app.Run()</c> 時被 TestHost disposal race 遮蔽。
    /// </summary>
    [Fact]
    public void Request_limit_above_hard_ceiling_fails_deployment_validation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsGateway:RequestLimits:MaxRequestBodyBytes"] = "1048577",
                ["DynamicsGateway:RequestLimits:JsonMaxDepth"] = "32"
            })
            .Build();

        var bind = () => GatewayRequestBodyLimitOptions.BindAndValidate(configuration);

        bind.Should().Throw<InvalidOperationException>()
            .WithMessage("*request body*maximum*");
    }

    /// <summary>
    /// 直接 reader 驗證以 UTF-8 wire bytes 而非 UTF-16 char 計算 exact limit；繁體中文與 emoji 會產生多位元組編碼，
    /// exact-limit body 必須成功，且 cloned parameter 在 JsonDocument Dispose、整個 pooled array 清零並 Return 後仍可讀取。
    /// Request stream 保持由 ASP.NET Core/caller 擁有，reader 不得 Dispose；測試在 assertion 後才明確釋放。
    /// </summary>
    [Fact]
    public async Task Reader_accepts_exact_utf8_wire_limit_and_detaches_parameter_values()
    {
        const string json =
            "{\"idempotencyKey\":\"邊界🙂\",\"parameters\":{\"message\":\"繁體中文🙂\"}}";
        var bytes = Encoding.UTF8.GetBytes(json);
        bytes.Length.Should().BeGreaterThan(json.Length);
        var pool = new TrackingArrayPool(bytes.Length + 37);
        var reader = CreateBodyReader(bytes.Length, jsonMaxDepth: 32, pool);
        var body = new TrackingReadStream(bytes);
        var request = CreateHttpRequest(body, declaredContentLength: null);

        var result = await reader.ReadAsync(request, CancellationToken.None);

        result.Status.Should().Be(GatewayOperationRequestBodyReadStatus.Success);
        result.WireByteCount.Should().Be(bytes.Length);
        result.Request.Should().NotBeNull();
        result.Request!.IdempotencyKey.Should().Be("邊界🙂");
        result.Request.Parameters.Should().ContainKey("message");
        var message = result.Request.Parameters!["message"].Should().BeOfType<JsonElement>().Subject;
        message.GetString().Should().Be("繁體中文🙂",
            "the value must be cloned before the pooled source buffer is cleared");
        body.BytesRead.Should().Be(bytes.Length);
        body.IsDisposed.Should().BeFalse();
        AssertBufferReturnedCleared(pool);
        body.Dispose();
    }

    /// <summary>
    /// 未宣告 Content-Length（chunked/unknown-length）的 body 只能讀到 limit+1 個 wire bytes；
    /// 一旦取得偵測 byte 即回傳 413，不可繼續 drain attacker-controlled stream，也不可進入 JSON parser 或 executor。
    /// Rented array 比要求容量更大且預先填入非零值，證明 reader Return 前清除的是整個實際 array，而非只有已讀 prefix。
    /// </summary>
    [Fact]
    public async Task Reader_unknown_length_stops_after_exactly_limit_plus_one_bytes()
    {
        const int limit = 64;
        var pool = new TrackingArrayPool(limit + 29);
        var reader = CreateBodyReader(limit, jsonMaxDepth: 32, pool);
        var body = new TrackingReadStream(Enumerable.Repeat((byte)'x', limit * 2).ToArray());
        var request = CreateHttpRequest(body, declaredContentLength: null);

        var result = await reader.ReadAsync(request, CancellationToken.None);

        result.Status.Should().Be(GatewayOperationRequestBodyReadStatus.PayloadTooLarge);
        result.Request.Should().BeNull();
        result.WireByteCount.Should().Be(limit + 1);
        body.BytesRead.Should().Be(limit + 1);
        body.Position.Should().Be(limit + 1);
        body.IsDisposed.Should().BeFalse();
        AssertBufferReturnedCleared(pool);
        body.Dispose();
    }

    /// <summary>
    /// Malformed JSON、root allowlist 外欄位、case-insensitive duplicate 與超過明確 MaxDepth 都必須收斂成穩定 InvalidJson；
    /// 不可把 parser exception 或部分 DTO 洩漏給 endpoint。每一案例同時驗證 stream ownership 與 error path 的全陣列清零/Return。
    /// </summary>
    [Theory]
    [InlineData("{\"parameters\":", 32)]
    [InlineData("{\"parameters\":{},\"principal\":\"caller\"}", 32)]
    [InlineData("{\"parameters\":{},\"PARAMETERS\":{}}", 32)]
    [InlineData("{\"parameters\":{\"level1\":{\"level2\":{\"level3\":1}}}}", 2)]
    public async Task Reader_returns_controlled_invalid_result_for_rejected_json(
        string json,
        int jsonMaxDepth)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var bodyLimit = bytes.Length + 16;
        var pool = new TrackingArrayPool(bodyLimit + 23);
        var reader = CreateBodyReader(bodyLimit, jsonMaxDepth, pool);
        var body = new TrackingReadStream(bytes);
        var request = CreateHttpRequest(body, bytes.Length);

        var result = await reader.ReadAsync(request, CancellationToken.None);

        result.Status.Should().Be(GatewayOperationRequestBodyReadStatus.InvalidJson);
        result.Request.Should().BeNull();
        result.WireByteCount.Should().Be(bytes.Length);
        body.IsDisposed.Should().BeFalse();
        AssertBufferReturnedCleared(pool);
        body.Dispose();
    }

    /// <summary>
    /// Cancellation 必須保留 OperationCanceledException，不可被轉成 400；即使取消發生在敏感 bytes 已寫入 rented buffer 後，
    /// finally 仍須清零整個實際 array 並只 Return 一次。Reader 不建立 linked token/registration，也不 Dispose caller-owned stream。
    /// </summary>
    [Fact]
    public async Task Reader_propagates_cancellation_after_zeroing_and_returning_buffer()
    {
        var sensitiveBytes = Encoding.UTF8.GetBytes(
            "{\"parameters\":{\"secret\":\"request-only-value\"}");
        using var cancellation = new CancellationTokenSource();
        var pool = new TrackingArrayPool(256);
        var reader = CreateBodyReader(128, jsonMaxDepth: 32, pool);
        var body = new CancelAfterFirstReadStream(sensitiveBytes, cancellation);
        var request = CreateHttpRequest(body, declaredContentLength: null);

        Func<Task> read = async () =>
            await reader.ReadAsync(request, cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
        body.BytesRead.Should().Be(sensitiveBytes.Length);
        body.IsDisposed.Should().BeFalse();
        AssertBufferReturnedCleared(pool);
        body.Dispose();
    }

    /// <summary>
    /// Application reader、Kestrel 與 IIS 必須 materialize 同一個 deployment-owned maximum；
    /// 測試啟動真實 Kestrel listener 後讀取三個 immutable options snapshot，防止 hosting topology 產生不同 admission boundary。
    /// Factory/client 是唯一 listener/socket owner，案例結束時依 await using/using 確定性清理。
    /// </summary>
    [Fact]
    public void Kestrel_iis_and_application_reader_share_the_configured_maximum()
    {
        const int configuredMaximum = 4_321;
        using var factory = CreateFactory(
            new RecordingExecutor(),
            configuredMaximum,
            mapped: true,
            useKestrel: false);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var applicationMaximum = factory.Services
            .GetRequiredService<IOptions<GatewayRequestBodyLimitOptions>>()
            .Value
            .MaxRequestBodyBytes;
        var kestrelMaximum = factory.Services
            .GetRequiredService<IOptions<KestrelServerOptions>>()
            .Value
            .Limits
            .MaxRequestBodySize;
        var iisMaximum = factory.Services
            .GetRequiredService<IOptions<IISServerOptions>>()
            .Value
            .MaxRequestBodySize;

        applicationMaximum.Should().Be(configuredMaximum);
        kestrelMaximum.Should().Be(configuredMaximum);
        iisMaximum.Should().Be(configuredMaximum);
    }

    /// <summary>
    /// 建立隔離 Testing Host。Program 必須選用 configured fake scheme，但 handler 只從 server-side configuration 建立 principal，
    /// 完全忽略 HTTP identity headers；executor 為單一案例記憶體 double，Kestrel 模式由 Factory 唯一擁有 listener 與 socket cleanup。
    /// </summary>
    private static WebApplicationFactory<Program> CreateFactory(
        RecordingExecutor executor,
        int maxRequestBodyBytes,
        bool mapped,
        bool useKestrel)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting(
                "DynamicsGateway:RequestLimits:MaxRequestBodyBytes",
                maxRequestBodyBytes.ToString(CultureInfo.InvariantCulture));
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["DynamicsGateway:AuthenticationScheme"] = TestAuthenticationHandler.SchemeName,
                    ["DynamicsGateway:TestPrincipalName"] = PrincipalName,
                    ["DynamicsGateway:ActiveWorkloadBindingSet"] = "Testing",
                    ["DynamicsGateway:RequestLimits:MaxRequestBodyBytes"] =
                        maxRequestBodyBytes.ToString(CultureInfo.InvariantCulture),
                    // 非 mapping 案例仍需要一個有效且非空的 Testing set，才能把斷言焦點留在 request authentication／authorization；
                    // reserved principal 不會由 handler 建立，configuration snapshot 只屬於此 Factory，沒有 reload subscription 或跨案例 retention。
                    ["DynamicsGateway:WorkloadBindingSets:Testing:0:PrincipalName"] =
                        @"SPEECHMESSAGE\RequestBoundaryBaseline$",
                    ["DynamicsGateway:WorkloadBindingSets:Testing:0:WorkloadSubjectId"] =
                        "request-boundary-baseline-service",
                    ["DynamicsGateway:WorkloadBindingSets:Testing:0:ProfileAliases:0"] = "crm82",
                    ["DynamicsGateway:WorkloadBindingSets:Testing:0:CapabilityOperationIds:0"] =
                        "runtime.health.whoami"
                };
                if (mapped)
                {
                    values["DynamicsGateway:WorkloadBindingSets:Testing:1:PrincipalName"] = PrincipalName;
                    values["DynamicsGateway:WorkloadBindingSets:Testing:1:WorkloadSubjectId"] =
                        "request-boundary-service";
                    values["DynamicsGateway:WorkloadBindingSets:Testing:1:ProfileAliases:0"] = "crm82";
                    values["DynamicsGateway:WorkloadBindingSets:Testing:1:CapabilityOperationIds:0"] =
                        "runtime.health.whoami";
                }

                configuration.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
                services.RemoveAll<IDynamicsOperationExecutor>();
                services.AddSingleton<IDynamicsOperationExecutor>(executor);
            });

            if (useKestrel)
            {
                // WithWebHostBuilder 會回傳衍生 Factory；其 UseKestrel(int) 的 port 僅存在衍生 instance，
                // 但 .NET 10 建立 minimal host 時的 CreateHost delegate 仍由原始 instance 執行，因而遺失該 port。
                // 將 port 0 寫入同一個 IWebHostBuilder 才能跨越 Factory 邊界，交由 OS 配發此測試唯一 listener，
                // 不保留 socket、client、request 或跨測試可變狀態，所有實體仍由外層 await using Factory 統一釋放。
                builder.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
            }
        });

        if (useKestrel)
        {
            // 啟用 Kestrel；實際 listener URL 已在上方由同一個 builder 固定為 port 0，避免衍生 Factory 的 port 設定
            // 被原始 CreateHost delegate 遺失。呼叫端只讀取 IServerAddressesFeature 的實際位址，不共享 listener 狀態。
            factory.UseKestrel();
        }

        return factory;
    }

    /// <summary>
    /// 建立只屬於單一測試案例的 stateless reader；options 不跨案例共享，pool ownership 仍由 caller 保留，reader 不負責 Dispose。
    /// </summary>
    private static GatewayOperationRequestBodyReader CreateBodyReader(
        int maxRequestBodyBytes,
        int jsonMaxDepth,
        ArrayPool<byte> pool)
        => new(
            Options.Create(new GatewayRequestBodyLimitOptions
            {
                MaxRequestBodyBytes = maxRequestBodyBytes,
                JsonMaxDepth = jsonMaxDepth
            }),
            pool);

    /// <summary>
    /// 建立最小 ASP.NET request fixture；HttpContext 只屬於目前測試，body stream 的最終 Dispose owner 仍是 caller。
    /// </summary>
    private static HttpRequest CreateHttpRequest(Stream body, long? declaredContentLength)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = declaredContentLength;
        context.Request.Body = body;
        return context.Request;
    }

    /// <summary>
    /// 驗證單次 request 的 buffer lease 已確定性結束，且 pool 在 Return callback 觀察到整個實際 array 都為零。
    /// </summary>
    private static void AssertBufferReturnedCleared(TrackingArrayPool pool)
    {
        pool.RentCount.Should().Be(1);
        pool.ReturnCount.Should().Be(1);
        pool.WasEntireBufferZeroOnReturn.Should().BeTrue();
    }

    /// <summary>
    /// 直接把 caller-owned tracking stream 掛到 TestServer request，避免 HttpClient 先複製 body 而掩蓋 server 是否讀取；
    /// 回傳的 HttpContext 由 TestServer request lifecycle 擁有，方法不 Dispose request stream，也不保留 Factory 或 stream reference。
    /// </summary>
    /// <param name="factory">擁有 TestServer、DI scope 與 handler lifecycle 的測試 Host。</param>
    /// <param name="body">由呼叫端擁有、用來觀察是否發生任何 body I/O 的 tracking stream。</param>
    /// <param name="declaredContentLength">要放入 request header 的宣告長度；null 代表 chunked／未知長度路徑。</param>
    /// <param name="contentType">要放入 request header 的媒體型別；null 或空白用來驗證缺少宣告時的 fail-closed 行為。</param>
    /// <returns>由 TestServer 完成處理的 request context；其中不得保留超過 request scope 的 body 或 identity state。</returns>
    private static Task<HttpContext> SendThroughTestServerAsync(
        WebApplicationFactory<Program> factory,
        Stream body,
        long? declaredContentLength,
        string? contentType = "application/json")
        => factory.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethod.Post.Method;
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("localhost");
            context.Request.Path = OperationPath;
            context.Request.ContentType = contentType;
            context.Request.ContentLength = declaredContentLength;
            context.Request.Body = body;
        });

    /// <summary>
    /// 建立固定 HTTP/1.1 operation request；content ownership 轉交給 request，caller Dispose request 時會連同 content 一起清理。
    /// </summary>
    private static HttpRequestMessage CreateHttp11Request(HttpContent content)
    {
        content.Headers.ContentType = new("application/json");
        return new HttpRequestMessage(HttpMethod.Post, OperationPath)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = content
        };
    }

    /// <summary>
    /// 記錄通過所有 inbound boundary 的 request；不建立 transport、admission、token、socket 或背景工作，
    /// 因此 <see cref="CallCount"/> 為零可直接證明拒絕發生在 outbound ownership 前。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        /// <summary>取得單一測試案例內的 executor 呼叫次數；實例不跨測試共享。</summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// 同步記錄呼叫並回傳固定成功結果；不保留 request、principal、body、credential、token 或 cancellation registration。
        /// </summary>
        /// <remarks>
        /// 成功資料刻意限制為封閉的 WhoAmI envelope，讓 body-limit 測試只觀察已通過邊界的控制流程，
        /// 不會因匿名 OData-shaped payload 保留 request body、principal 或任何上游延伸欄位。
        /// </remarks>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(OperationExecutionResult.Success(
                OperationResponseData.ForWhoAmI(
                    OperationIds.RuntimeHealthWhoAmI,
                    "9.1",
                    new WhoAmIResponseData())));
        }
    }

    /// <summary>
    /// 由測試 configuration 建立 authenticated principal 的 Testing-only handler；完全不讀 HTTP headers，
    /// request scope 是唯一 owner，handler 不建立 socket、timer、subscription 或背景工作。
    /// </summary>
    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        /// <summary>取得只在本測試 Factory 註冊的 scheme 名稱。</summary>
        public const string SchemeName = "RequestBoundaryTest";

        /// <summary>建立 request-scoped handler；所有 dependency 由 ASP.NET DI 擁有並在 Host disposal 回收。</summary>
        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        /// <summary>
        /// 只信任 server-side fixture principal name；空值回傳 NoResult，且不保留 ClaimsPrincipal 或 request reference。
        /// </summary>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var principalName = Context.RequestServices
                .GetRequiredService<IConfiguration>()["DynamicsGateway:TestPrincipalName"];
            if (string.IsNullOrWhiteSpace(principalName))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, principalName) },
                SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    /// <summary>
    /// 記錄 server 對 request stream 的實際 read 次數與 disposal；底層 byte array 只屬於此測試 instance，
    /// 不建立 unmanaged handle、timer 或背景工作。ASP.NET/reader 不得 Dispose，最終 cleanup 由測試明確執行。
    /// </summary>
    private sealed class TrackingReadStream : MemoryStream
    {
        /// <summary>以 caller-owned bytes 建立可讀 stream；MemoryStream 不複製陣列且不跨案例共享。</summary>
        public TrackingReadStream(byte[] bytes)
            : base(bytes, writable: false)
        {
        }

        /// <summary>取得所有同步/非同步 read 呼叫總數。</summary>
        public int ReadCount { get; private set; }

        /// <summary>取得 reader 實際消耗的總 wire bytes，不包含 EOF read。</summary>
        public int BytesRead { get; private set; }

        /// <summary>取得 stream 是否已被 Dispose，供驗證 request stream ownership。</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>執行同步讀取但不增加非同步計數；此路徑保留 MemoryStream 契約，測試主要以 ReadAsync 觀察 Gateway 行為。</summary>
        public override int Read(byte[] buffer, int offset, int count)
            => base.Read(buffer, offset, count);

        /// <summary>執行 Span 同步讀取且不改變 ownership；方法不保存目的 buffer，也不建立額外配置。</summary>
        public override int Read(Span<byte> buffer)
            => base.Read(buffer);

        /// <summary>記錄每次非同步讀取與實際 byte 數，並同步完成 bounded MemoryStream 讀取；取消在接觸來源資料前 fail fast。</summary>
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            var read = base.Read(buffer.Span);
            BytesRead += read;
            return ValueTask.FromResult(read);
        }

        /// <summary>記錄最終 cleanup 是否發生，再交由基底釋放 stream；此旗標用來證明 Gateway reader 沒有倒置 request stream ownership。</summary>
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 固定回傳單一、刻意大於 requested capacity 且預填非零值的測試 pool；不配置第二個 buffer、不跨案例共享。
    /// Return callback 在 array 重新交給 pool 前同步記錄全陣列是否為零，讓 trailing unused bytes 也納入資料殘留驗證。
    /// </summary>
    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        private readonly byte[] _buffer;

        /// <summary>建立單一 buffer pool；buffer lifetime 與測試案例一致，不會進入 process-wide shared pool。</summary>
        public TrackingArrayPool(int bufferLength)
        {
            _buffer = new byte[bufferLength];
        }

        /// <summary>取得 Rent 呼叫次數。</summary>
        public int RentCount { get; private set; }

        /// <summary>取得 Return 呼叫次數。</summary>
        public int ReturnCount { get; private set; }

        /// <summary>取得 Return 當下整個實際 array 是否全為零。</summary>
        public bool WasEntireBufferZeroOnReturn { get; private set; }

        /// <summary>租出案例唯一 buffer；拒絕過大要求與重疊 lease，並以非零值填滿整段陣列以驗證歸還前確實清零。</summary>
        public override byte[] Rent(int minimumLength)
        {
            if (minimumLength > _buffer.Length)
            {
                throw new InvalidOperationException("Tracking pool buffer is smaller than requested capacity.");
            }

            if (RentCount != ReturnCount)
            {
                throw new InvalidOperationException("Tracking pool does not allow overlapping leases.");
            }

            RentCount++;
            Array.Fill(_buffer, (byte)0xA5);
            return _buffer;
        }

        /// <summary>只接受本 pool 租出的同一陣列，於歸還當下檢查整段是否為零；測試 pool 不重用或 Dispose 該陣列。</summary>
        public override void Return(byte[] array, bool clearArray = false)
        {
            if (!ReferenceEquals(array, _buffer))
            {
                throw new InvalidOperationException("Reader returned a buffer not owned by this tracking pool.");
            }

            WasEntireBufferZeroOnReturn = array.All(static value => value == 0);
            ReturnCount++;
        }
    }

    /// <summary>
    /// 第一次 async read 將敏感 bytes 寫入 destination 後取消 caller-owned token；第二次 read 觀察取消並拋出。
    /// Stream 不建立 registration/background work，且不擁有 CancellationTokenSource；最終 Dispose 仍由測試執行。
    /// </summary>
    private sealed class CancelAfterFirstReadStream : Stream
    {
        private readonly byte[] _bytes;
        private readonly CancellationTokenSource _cancellation;
        private bool _firstReadCompleted;

        /// <summary>建立只讀 cancellation fixture，不複製或跨案例保存輸入 bytes/token source。</summary>
        public CancelAfterFirstReadStream(
            byte[] bytes,
            CancellationTokenSource cancellation)
        {
            _bytes = bytes;
            _cancellation = cancellation;
        }

        /// <summary>取得第一個 read 寫入 destination 的 bytes。</summary>
        public int BytesRead { get; private set; }

        /// <summary>取得 stream 是否由 reader 意外 Dispose。</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>此 fixture 僅支援讀取，讓 reader 可測試取消後的 cleanup；狀態固定且不依賴外部資源。</summary>
        public override bool CanRead => true;

        /// <summary>禁止 seek，確保 Gateway 不依賴回捲或預先取得任意位置。</summary>
        public override bool CanSeek => false;

        /// <summary>禁止寫入，避免測試期間修改 caller-owned 敏感 bytes。</summary>
        public override bool CanWrite => false;

        /// <summary>回報固定來源長度；它只描述測試資料，不轉移 byte array ownership。</summary>
        public override long Length => _bytes.Length;

        /// <summary>以已讀 byte 數表示位置；setter 明確拒絕，避免測試誤用 seek 語意。</summary>
        public override long Position
        {
            get => BytesRead;
            set => throw new NotSupportedException();
        }

        /// <summary>唯讀記憶體 fixture 沒有待 flush 狀態，因此此方法為無副作用的同步完成。</summary>
        public override void Flush()
        {
        }

        /// <summary>禁止同步讀取，強制 production reader 走可取消的 ReadAsync 路徑並驗證其 cleanup。</summary>
        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        /// <summary>第一次複製 bounded bytes 後觸發 caller-owned 取消；後續呼叫觀察 token 並拋出，且不保存目的 memory。</summary>
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_firstReadCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(0);
            }

            var count = Math.Min(buffer.Length, _bytes.Length);
            _bytes.AsMemory(0, count).CopyTo(buffer);
            BytesRead = count;
            _firstReadCompleted = true;
            _cancellation.Cancel();
            return ValueTask.FromResult(count);
        }

        /// <summary>此 non-seekable fixture 不支援定位；呼叫即明確失敗且不變更來源狀態。</summary>
        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        /// <summary>來源長度由建構時固定，禁止執行期擴張或截斷以維持 bounded 測試契約。</summary>
        public override void SetLength(long value)
            => throw new NotSupportedException();

        /// <summary>fixture 為唯讀，禁止任何寫入以避免改變取消與敏感資料清零的測試前提。</summary>
        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        /// <summary>只記錄測試 owner 是否完成 cleanup，再呼叫基底 Dispose；不擁有或 Dispose 外部 CancellationTokenSource。</summary>
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 記錄 HttpRequestMessage 是否透過 ownership chain Dispose declared content；bytes 由基底 ByteArrayContent 唯一持有。
    /// </summary>
    private sealed class TrackingByteArrayContent : ByteArrayContent
    {
        /// <summary>以案例私有 bytes 建立 content。</summary>
        public TrackingByteArrayContent(byte[] content)
            : base(content)
        {
        }

        /// <summary>取得 content 是否已被 owning request Dispose。</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>記錄 request→content ownership chain 是否完成，然後交由 ByteArrayContent 釋放其 managed payload reference。</summary>
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 包裝 caller-owned bytes 的 non-seekable stream，使 HttpClient HTTP/1.1 無法預先計算 Content-Length 而必須使用 chunked framing。
    /// Instance 只持有一個 MemoryStream，Dispose 時確定性回收；沒有額外 buffer、handle 或背景工作。
    /// </summary>
    private sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;

        /// <summary>建立只讀 non-seekable wrapper；輸入 bytes 由 inner stream 在本 instance 生命週期內唯一引用。</summary>
        public NonSeekableReadStream(byte[] bytes)
        {
            _inner = new MemoryStream(bytes, writable: false);
        }

        /// <summary>允許順序讀取 inner stream；狀態固定且可安全由單一 HttpContent owner 使用。</summary>
        public override bool CanRead => true;

        /// <summary>刻意禁止 seek，使 HttpClient 無法從 Length/Position 預先推導 Content-Length，必須採 chunked framing。</summary>
        public override bool CanSeek => false;

        /// <summary>fixture 為唯讀，禁止 request serialization 反向寫入來源 stream。</summary>
        public override bool CanWrite => false;

        /// <summary>取得 stream 是否由 owning StreamContent/request Dispose。</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>刻意拒絕 Length，維持 unknown-length/chunked 信任邊界，不暴露 inner buffer 大小給 HttpClient。</summary>
        public override long Length => throw new NotSupportedException();

        /// <summary>刻意拒絕 Position 讀寫，避免任何元件藉由 seek 語意繞過 chunked body 邊界。</summary>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>唯讀 MemoryStream 沒有待送資料，Flush 為無副作用完成且不轉移 ownership。</summary>
        public override void Flush()
        {
        }

        /// <summary>將同步讀取直接委派給案例私有 inner stream；不保存 caller buffer 或建立額外資源。</summary>
        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, count);

        /// <summary>將可取消非同步讀取委派給 inner stream；取消與 byte 計數語意由基底實作負責，wrapper 不建立 registration。</summary>
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);

        /// <summary>禁止定位，確保測試 body 始終只能向前消耗且不會被重新派送。</summary>
        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        /// <summary>禁止修改來源長度，維持建構時的 bounded byte array 與唯一 inner owner。</summary>
        public override void SetLength(long value)
            => throw new NotSupportedException();

        /// <summary>禁止寫入 caller-owned body，避免測試 wrapper 成為第二個 mutable state owner。</summary>
        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        /// <summary>記錄 disposal，並只在 disposing=true 時由 wrapper 唯一釋放 inner stream；最後交由基底完成 Stream cleanup。</summary>
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
