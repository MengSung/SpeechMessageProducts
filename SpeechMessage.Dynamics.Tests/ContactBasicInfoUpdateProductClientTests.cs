// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/ContactBasicInfoUpdateProductClientTests.cs
// 目的：以純記憶體 executor 驗證 P7.2 contact basic-info ProductClient 的封閉契約。
//
// 生命週期與隔離邊界：
// 1. 測試替身只記錄 defensive-copied request scalar，不建立 HTTP、Data8 client、credential、token、
//    timer 或背景工作；測試完成後不保留跨案例可重用的 session state。
// 2. ProductClient 必須固定使用 memberinfo.contact.update.basic.info，且不得接受 caller 選擇
//    ConnectorKind、CE version、Organization、endpoint 或任意 CRM payload。
// 3. 錯配 response branch 必須 fail closed；任何未投影資料不能轉換成產品結果。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Guard;
using SpeechMessage.Dynamics.Embedded;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.Gateway;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 P7.2 產品端 contact basic-info typed client 與 Gateway／Embedded 共用的 executor contract。
/// </summary>
public sealed class ContactBasicInfoUpdateProductClientTests
{
    private static readonly Guid ContactId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");

    /// <summary>
    /// 保護 typed client 只建立固定 operation 與四個核准 scalar。故障注入是 executor 回傳已確認的
    /// changed/read-back branch；決定性斷言包含 request defensive copy 與產品 enum projection。
    /// </summary>
    [Fact]
    public async Task Update_async_builds_a_closed_request_and_maps_changed_response()
    {
        var executor = new RecordingExecutor(_ => OperationExecutionResult.Success(
            OperationResponseData.ForContactBasicInfoUpdate(
                OperationIds.MemberInfoContactUpdateBasicInfo,
                "9.1",
                ContactBasicInfoUpdateDisposition.Changed,
                ContactBasicInfoUpdateCorrelationCategory.ReadBackConfirmed)));
        var client = new Package02ContactBasicInfoUpdateClient(
            executor,
            NullLogger<Package02ContactBasicInfoUpdateClient>.Instance);

        var result = await client.UpdateAsync(new ContactBasicInfoUpdateRequest
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-local",
            ContactId = ContactId,
            Phone = " 0900-000-001 ",
            Address = " P7.2 fixture address ",
            IdempotencyKey = "p72-contact-001"
        });

        result.Disposition.Should().Be(ContactBasicInfoUpdateDisposition.Changed);
        result.CorrelationCategory.Should().Be(ContactBasicInfoUpdateCorrelationCategory.ReadBackConfirmed);
        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.ProfileAlias.Should().Be("crm91");
        executor.LastRequest.WorkloadSubjectId.Should().Be("churchreport-local");
        executor.LastRequest.CapabilityOperationId.Should().Be(OperationIds.MemberInfoContactUpdateBasicInfo);
        executor.LastRequest.IdempotencyKey.Should().Be("p72-contact-001");
        executor.LastRequest.Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["contactId"] = ContactId,
            ["phone"] = "0900-000-001",
            ["address"] = "P7.2 fixture address"
        });
    }

    /// <summary>
    /// 保護空白 phone/address 的既有「不覆寫」語意仍透過同一個 typed operation 傳遞，不能在
    /// ProductClient 私自建立第二條 no-op 或 legacy CRM 路徑。executor 回傳 NoChange 時只投影封閉 enum。
    /// </summary>
    [Fact]
    public async Task Update_async_preserves_no_change_as_a_closed_result()
    {
        var executor = new RecordingExecutor(_ => OperationExecutionResult.Success(
            OperationResponseData.ForContactBasicInfoUpdate(
                OperationIds.MemberInfoContactUpdateBasicInfo,
                "9.1",
                ContactBasicInfoUpdateDisposition.NoChange,
                ContactBasicInfoUpdateCorrelationCategory.NoDispatch)));
        var client = new Package02ContactBasicInfoUpdateClient(
            executor,
            NullLogger<Package02ContactBasicInfoUpdateClient>.Instance);

        var result = await client.UpdateAsync(new ContactBasicInfoUpdateRequest
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-local",
            ContactId = ContactId,
            Phone = "   ",
            Address = "",
            IdempotencyKey = "p72-contact-noop"
        });

        result.Disposition.Should().Be(ContactBasicInfoUpdateDisposition.NoChange);
        result.CorrelationCategory.Should().Be(ContactBasicInfoUpdateCorrelationCategory.NoDispatch);
        executor.LastRequest!.Parameters.Should().ContainSingle()
            .Which.Key.Should().Be("contactId");
    }

    /// <summary>
    /// 保護產品端輸入在 executor 前 fail closed。空 profile、空 workload、空 contact、缺少冪等鍵與
    /// 非 URL-safe 冪等鍵都不得建立 request 或取得任何下游資源。
    /// </summary>
    [Theory]
    [InlineData("", "churchreport-local", "p72-contact-001")]
    [InlineData("crm91", "", "p72-contact-001")]
    [InlineData("crm91", "churchreport-local", "")]
    [InlineData("crm91", "churchreport-local", "contains space")]
    public async Task Update_async_rejects_invalid_routing_or_idempotency_input(
        string profileAlias,
        string workloadSubjectId,
        string idempotencyKey)
    {
        var executor = new RecordingExecutor(_ => throw new InvalidOperationException("must not dispatch"));
        var client = new Package02ContactBasicInfoUpdateClient(
            executor,
            NullLogger<Package02ContactBasicInfoUpdateClient>.Instance);

        var act = () => client.UpdateAsync(new ContactBasicInfoUpdateRequest
        {
            ProfileAlias = profileAlias,
            WorkloadSubjectId = workloadSubjectId,
            ContactId = ContactId,
            Phone = "0900-000-001",
            IdempotencyKey = idempotencyKey
        });

        await act.Should().ThrowAsync<ArgumentException>();
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 ProductClient 不會把其他 capability 的合法 envelope 誤當 contact update 成功。
    /// 故障注入是 WhoAmI branch；決定性斷言為固定 contract exception，且不回傳 raw response。
    /// </summary>
    [Fact]
    public async Task Update_async_rejects_a_mismatched_response_branch()
    {
        var executor = new RecordingExecutor(_ => OperationExecutionResult.Success(
            OperationResponseData.ForWhoAmI(
                OperationIds.RuntimeHealthWhoAmI,
                "9.1",
                new WhoAmIResponseData
                {
                    UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    BusinessUnitId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333")
                })));
        var client = new Package02ContactBasicInfoUpdateClient(
            executor,
            NullLogger<Package02ContactBasicInfoUpdateClient>.Instance);

        var act = () => client.UpdateAsync(CreateValidRequest());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Contact basic-info response does not match the requested operation contract.");
    }

    /// <summary>
    /// 保護 Gateway／Embedded 組合根只註冊一個 stateless typed client，不在註冊階段建立 HTTP、Data8、
    /// credential 或 feature-gated ChurchReport traffic。解析 client 只驗證 composition graph 的封閉形狀。
    /// </summary>
    [Fact]
    public void Package02_registration_is_explicit_and_does_not_create_transport()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDynamicsOperationExecutor, RecordingExecutor>(_ =>
            new RecordingExecutor(_ => OperationExecutionResult.Success(data: null)));
        services.AddSpeechMessageDynamicsPackage02ContactBasicInfoUpdates();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetServices<IPackage02ContactBasicInfoUpdateClient>()
            .Should().ContainSingle()
            .Which.Should().BeOfType<Package02ContactBasicInfoUpdateClient>();
    }

    /// <summary>
    /// 驗證 Lenovo 兩條本機路線使用完全相同的 typed request/result：Embedded 只省略 HTTP hop，Gateway
    /// 只增加封閉 JSON hop；兩者都不能讓 caller 在 request time 選擇 connector、CE version 或 endpoint。
    /// </summary>
    [Fact]
    public async Task Embedded_and_gateway_routes_preserve_the_same_contact_update_contract()
    {
        var embeddedRecording = new RecordingExecutor(_ => OperationExecutionResult.Success(
            OperationResponseData.ForContactBasicInfoUpdate(
                OperationIds.MemberInfoContactUpdateBasicInfo,
                "9.1",
                ContactBasicInfoUpdateDisposition.Changed,
                ContactBasicInfoUpdateCorrelationCategory.ReadBackConfirmed)));
        var embeddedAdapter = new EmbeddedHostAdapter(
            new RequestGuard([OperationIds.MemberInfoContactUpdateBasicInfo]),
            embeddedRecording,
            "crm91");
        var embeddedClient = new Package02ContactBasicInfoUpdateClient(
            embeddedAdapter,
            NullLogger<Package02ContactBasicInfoUpdateClient>.Instance);

        var seenGatewayRequest = new TaskCompletionSource<JsonDocument>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var gatewayHttpClient = new HttpClient(new StubHandler(async request =>
        {
            var json = await request.Content!.ReadAsStringAsync();
            seenGatewayRequest.TrySetResult(JsonDocument.Parse(json));
            var response = OperationExecutionResult.Success(
                OperationResponseData.ForContactBasicInfoUpdate(
                    OperationIds.MemberInfoContactUpdateBasicInfo,
                    "9.1",
                    ContactBasicInfoUpdateDisposition.Changed,
                    ContactBasicInfoUpdateCorrelationCategory.ReadBackConfirmed));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(response),
                    Encoding.UTF8,
                    "application/json")
            };
        }));
        var gatewayExecutor = new GatewayDynamicsOperationExecutor(
            gatewayHttpClient,
            Options.Create(new ProductDynamicsOptions
            {
                ConnectionMode = ConnectionMode.DedicatedGateway,
                ProfileAlias = "crm91",
                Gateway = new GatewayEndpointOptions
                {
                    Endpoint = "https://localhost:7443/",
                    ApiPrefix = "/v1"
                }
            }),
            NullLogger<GatewayDynamicsOperationExecutor>.Instance);
        var gatewayClient = new Package02ContactBasicInfoUpdateClient(
            gatewayExecutor,
            NullLogger<Package02ContactBasicInfoUpdateClient>.Instance);

        var request = new ContactBasicInfoUpdateRequest
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-local",
            ContactId = ContactId,
            Phone = "0900-000-001",
            Address = "P7.2 fixture address",
            IdempotencyKey = "p72-parity-001"
        };

        var embeddedResult = await embeddedClient.UpdateAsync(request);
        var gatewayResult = await gatewayClient.UpdateAsync(request);
        var gatewayBody = await seenGatewayRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));

        embeddedResult.Should().Be(gatewayResult);
        embeddedRecording.LastRequest.Should().NotBeNull();
        embeddedRecording.LastRequest!.Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["contactId"] = ContactId,
            ["phone"] = "0900-000-001",
            ["address"] = "P7.2 fixture address"
        });
        gatewayBody.RootElement.GetProperty("idempotencyKey").GetString()
            .Should().Be("p72-parity-001");
        gatewayBody.RootElement.GetProperty("parameters").GetProperty("contactId").GetGuid()
            .Should().Be(ContactId);
        gatewayBody.RootElement.GetProperty("parameters").GetProperty("phone").GetString()
            .Should().Be("0900-000-001");
        gatewayBody.RootElement.GetProperty("parameters").GetProperty("address").GetString()
            .Should().Be("P7.2 fixture address");
    }

    private static ContactBasicInfoUpdateRequest CreateValidRequest()
        => new()
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-local",
            ContactId = ContactId,
            Phone = "0900-000-001",
            IdempotencyKey = "p72-contact-001"
        };

    /// <summary>
    /// 只保存本次案例的 defensive-copied request 與 call count；Dispose 後不留任何 transport 或 caller graph。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly Func<OperationExecutionRequest, OperationExecutionResult> _handler;

        public RecordingExecutor(Func<OperationExecutionRequest, OperationExecutionResult> handler)
            => _handler = handler;

        public int CallCount { get; private set; }

        public OperationExecutionRequest? LastRequest { get; private set; }

        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastRequest = new OperationExecutionRequest
            {
                ProfileAlias = request.ProfileAlias,
                CapabilityOperationId = request.CapabilityOperationId,
                WorkloadSubjectId = request.WorkloadSubjectId,
                IdempotencyKey = request.IdempotencyKey,
                Parameters = new Dictionary<string, object?>(request.Parameters, StringComparer.Ordinal)
            };
            return Task.FromResult(_handler(LastRequest));
        }
    }

    /// <summary>模擬 Gateway ProductClient 使用的 bounded HTTP handler；每個 response 由呼叫端擁有並 dispose。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _handler(request);
        }
    }
}
