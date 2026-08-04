using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Connectors.Data8;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Data8 OnPremise client factory 只在已解析的 Data8 Profile 與固定 credential reference 相符時建立
/// 短生命週期 Connector client。所有替身均完全離線；測試保護 WCF service 的唯一 Dispose ownership、WhoAmI
/// 安全 GUID 投影與取消 fail-closed，不會建立真實 credential、network session、timer 或背景工作。
/// </summary>
public sealed class OnPremiseData8ConnectorClientFactoryTests
{
    private static readonly Guid OrganizationId = Guid.Parse("bfb92ead-3705-f011-8143-00155d006608");

    /// <summary>
    /// 保護 credential reference 不相符時 factory 在建立任何 WCF service 前 fail closed。故障注入為已解析但
    /// 指向另一個 reference 的 Data8 Profile；主要斷言是固定例外與 service factory 零呼叫，避免把另一組
    /// 組織的 credential 或 session 意外帶入目前 Pool。
    /// </summary>
    [Fact]
    public async Task Create_async_rejects_a_mismatched_credential_reference_before_creating_a_service()
    {
        var created = 0;
        var factory = new OnPremiseData8ConnectorClientFactory(
            CreateConnectionSettings(),
            _ =>
            {
                Interlocked.Increment(ref created);
                return new FakeOrganizationService(OrganizationId);
            });
        var profile = CreateProfile() with { CredentialReference = "another-reference" };

        var action = async () => await factory.CreateAsync(profile, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        Volatile.Read(ref created).Should().Be(0);
    }

    /// <summary>
    /// 保護 factory 建立的 SDK-free client 只執行 allowlisted WhoAmI，並把三個 GUID 投影為純字串結果。
    /// 故障模型是 service 沒有被 lease owner Dispose；主要斷言是 operation 結果不含 SDK 物件且 client
    /// Dispose 後 fake service 恰好釋放一次，因此 pool drain 不會遺留 WCF channel 或跨 request session。
    /// </summary>
    [Fact]
    public async Task Created_client_executes_whoami_and_disposes_the_owned_service_exactly_once()
    {
        var service = new FakeOrganizationService(OrganizationId);
        var factory = new OnPremiseData8ConnectorClientFactory(CreateConnectionSettings(), _ => service);
        var client = await factory.CreateAsync(CreateProfile(), CancellationToken.None);
        var operation = new ConnectorOperation
        {
            OperationId = "runtime.health.whoami",
            WorkloadSubjectId = "test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5)
        };

        var result = await client.ExecuteAsync(operation, CancellationToken.None);
        await client.DisposeAsync();
        await client.DisposeAsync();

        result.Succeeded.Should().BeTrue();
        result.Values["organizationId"].Should().Be(OrganizationId.ToString("D"));
        service.ExecuteCount.Should().Be(1);
        service.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 建立只在本測試記憶體內存在的 factory 設定。字串內容不是真實 endpoint 或 credential；production factory
    /// 不記錄這些值，且設定 owner 是 host composition root，不會把它傳入 OperationExecutionRequest 或 Pool key。
    /// </summary>
    private static Data8OnPremiseConnectionSettings CreateConnectionSettings()
        => new(
            "churchreport.crmconnection",
            "https://example.invalid/XRMServices/2011/Organization.svc",
            "TEST\\service",
            "not-a-real-password");

    /// <summary>
    /// 建立與 production resolver 輸出同形狀的 immutable Data8 Profile；它只含 credential reference，
    /// 不含密碼、URL 或可變 client，因此測試不會把 secret/session 狀態放入共享資料結構。
    /// </summary>
    private static ResolvedProfile CreateProfile()
        => new(
            "sunnyvalechback",
            "sunnyvalechback",
            OrganizationId,
            CeVersion.Ce91,
            ConnectorKind.Data8,
            "churchreport.crmconnection",
            new ResolvedPoolPolicy(0, 1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), false),
            new ResolvedOperationPolicy(TimeSpan.FromSeconds(5), 0, TimeSpan.FromMilliseconds(1)),
            GenerationId: 1);

    /// <summary>
    /// 離線 IOrganizationService 替身只接受 WhoAmI，並記錄 Execute／Dispose 的精確次數。它不開啟 channel、
    /// handle、timer 或 thread；所有未預期 CRM 呼叫立即失敗，避免測試誤把 generic service 當成未受控通道。
    /// </summary>
    private sealed class FakeOrganizationService : IOrganizationService, IDisposable
    {
        private readonly Guid _organizationId;
        private int _executeCount;
        private int _disposeCount;

        public FakeOrganizationService(Guid organizationId) => _organizationId = organizationId;

        public int ExecuteCount => Volatile.Read(ref _executeCount);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            request.Should().BeOfType<WhoAmIRequest>();
            Interlocked.Increment(ref _executeCount);
            return new WhoAmIResponse
            {
                Results = new ParameterCollection
                {
                    ["UserId"] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    ["BusinessUnitId"] = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    ["OrganizationId"] = _organizationId
                }
            };
        }

        public Guid Create(Entity entity) => throw new NotSupportedException();

        public void Update(Entity entity) => throw new NotSupportedException();

        public void Delete(string entityName, Guid id) => throw new NotSupportedException();

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();

        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
