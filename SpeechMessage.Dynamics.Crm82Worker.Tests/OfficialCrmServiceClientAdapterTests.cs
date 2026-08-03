using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;
using Xunit;

namespace SpeechMessage.Dynamics.Crm82Worker.Tests;

/// <summary>
/// 固定 CE 8.2 adapter 的 operation dispatch、WhoAmI identity 與 dispose ownership。
/// 測試替身不建立真實 CrmServiceClient 或 credential，讓 regression 只驗證 worker-local
/// 同步 SDK 邊界，不會把 SDK object、Session 或連線狀態帶入一般 Dynamics tests。
/// </summary>
public sealed class OfficialCrmServiceClientAdapterTests
{
    private static readonly Guid UserId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BusinessUnitId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OrganizationId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    /// <summary>
    /// 證明 constructor readiness probe 與後續 WhoAmI 都使用同一個 worker-owned client，
    /// 並只輸出三個 bounded GUID，而不回傳 WhoAmIResponse 或其他 SDK object。
    /// </summary>
    [Fact]
    public void Preserves_the_who_am_i_operation_and_projects_only_sdk_free_guids()
    {
        var client = CreateReadyClient();
        using var adapter = CreateAdapter(client);

        var result = adapter.Execute(CreateIdentityRequest());

        Assert.True(adapter.IsReady);
        Assert.Equal(2, client.ExecuteCallCount);
        Assert.Equal(WorkerValueKind.Object, result.Kind);
        Assert.Equal(UserId.ToString("N"), result.Members!["userId"].Scalar);
        Assert.Equal(BusinessUnitId.ToString("N"), result.Members["businessUnitId"].Scalar);
        Assert.Equal(OrganizationId.ToString("N"), result.Members["organizationId"].Scalar);
    }

    /// <summary>
    /// 證明 adapter 能把 Package01 request 交給唯一 query operation；contactName 在 SDK query
    /// 建立前被移除，且 identity probe 之外只發出一次同步 RetrieveMultiple。
    /// </summary>
    [Fact]
    public void Dispatches_package01_to_the_query_operation_without_using_contact_name()
    {
        var request = Package01FeeQueryOperationTests.CreateRequest(includeContactName: true);
        Assert.True(request.Parameters.TryGetValue("contactName", out var contactName));
        Assert.Equal(Package01FeeQueryOperationTests.ContactNameSentinel, contactName!.Scalar);

        var client = CreateReadyClient();
        client.RetrieveMultipleHandler = query =>
        {
            Assert.DoesNotContain(
                Package01FeeQueryOperationTests.ContactNameSentinel,
                query.Criteria.Conditions.SelectMany(condition => condition.Values)
                    .OfType<string>());
            Assert.Empty(query.LinkEntities);
            return new EntityCollection();
        };
        using var adapter = CreateAdapter(client);

        var result = adapter.Execute(request);

        Assert.Equal(WorkerValueKind.Array, result.Kind);
        Assert.Equal(1, client.RetrieveMultipleCallCount);
        Assert.Equal(1, client.ExecuteCallCount);
    }

    /// <summary>
    /// 證明 unknown operation 在任何額外 SDK 呼叫前被拒絕；adapter 不會 fallback 到 WhoAmI、
    /// Web API、Data8、另一個 CE worker 或另一個 credential。
    /// </summary>
    [Fact]
    public void Rejects_an_unknown_operation_without_sdk_fallback()
    {
        var client = CreateReadyClient();
        using var adapter = CreateAdapter(client);
        var request = new WorkerRequestV1(
            WorkerProtocolVersion.Current,
            "0123456789abcdef0123456789abcdef",
            Guid.NewGuid(),
            "profile-generation-0001",
            "unknown-revision",
            "unknown.operation",
            DateTimeOffset.UtcNow.AddMinutes(1).UtcDateTime.Ticks,
            new Dictionary<string, WorkerValue>(StringComparer.Ordinal));

        Assert.Throws<InvalidOperationException>(() => adapter.Execute(request));

        Assert.Equal(1, client.ExecuteCallCount);
        Assert.Equal(0, client.RetrieveMultipleCallCount);
    }

    /// <summary>
    /// 證明 adapter 是 SDK client 的唯一 dispose owner：重複 Dispose 只釋放一次；
    /// admission 關閉後任何 operation 都先得到 ObjectDisposedException，且不再碰 SDK client。
    /// </summary>
    [Fact]
    public void Dispose_is_idempotent_and_all_later_execution_is_rejected()
    {
        var client = CreateReadyClient();
        var adapter = CreateAdapter(client);

        adapter.Dispose();
        adapter.Dispose();

        Assert.False(adapter.IsReady);
        Assert.Equal(1, client.DisposeCallCount);
        Assert.Throws<ObjectDisposedException>(
            () => adapter.Execute(Package01FeeQueryOperationTests.CreateRequest()));
        Assert.Equal(1, client.ExecuteCallCount);
        Assert.Equal(0, client.RetrieveMultipleCallCount);
    }

    /// <summary>建立 identity probe 與執行都會回傳固定有效 WhoAmIResponse 的 client。</summary>
    private static FakeCrm82SdkClient CreateReadyClient()
    {
        return new FakeCrm82SdkClient
        {
            ExecuteHandler = request =>
            {
                Assert.IsType<WhoAmIRequest>(request);
                var response = new WhoAmIResponse();
                response.Results["UserId"] = UserId;
                response.Results["BusinessUnitId"] = BusinessUnitId;
                response.Results["OrganizationId"] = OrganizationId;
                return response;
            }
        };
    }

    /// <summary>建立由單一測試案例擁有且不持有 credential 的 adapter。</summary>
    private static OfficialCrmServiceClientAdapter CreateAdapter(FakeCrm82SdkClient client)
    {
        return new OfficialCrmServiceClientAdapter(
            client,
            credential: null,
            OrganizationId,
            expectedCeVersion: "8.2");
    }

    /// <summary>建立符合固定 revision 與空 parameter shape 的 WhoAmI request。</summary>
    private static WorkerRequestV1 CreateIdentityRequest()
    {
        return new WorkerRequestV1(
            WorkerProtocolVersion.Current,
            "0123456789abcdef0123456789abcdef",
            Guid.NewGuid(),
            "profile-generation-0001",
            OfficialWorkerOperations.RuntimeHealthWhoAmIRevision,
            OfficialWorkerOperations.RuntimeHealthWhoAmI,
            DateTimeOffset.UtcNow.AddMinutes(1).UtcDateTime.Ticks,
            new Dictionary<string, WorkerValue>(StringComparer.Ordinal));
    }
}
