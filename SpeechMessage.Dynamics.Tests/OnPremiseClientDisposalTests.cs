using System.Reflection;
using System.ServiceModel;
using System.ServiceModel.Description;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Xrm.Sdk.Query;
using PowerPlatform.Dataverse.Client;
using ToolUtilityNameSpace.ConnectionOperations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Data8 <see cref="OnPremiseClient"/> 的資源生命週期合約。
/// 此測試以不建立網路連線的追蹤替身模擬 WCF Channel 與 ChannelFactory，確保
/// 連線池淘汰連線或應用程式停止時，連線層會在同一個擁有者內決定性釋放所有資源。
/// </summary>
public sealed class OnPremiseClientDisposalTests
{
    /// <summary>
    /// 保護「已借出連線在歸還前發生例外」的清理合約。
    /// 模擬 Channel 的 <c>Close</c> 發生失敗，並斷言 Client 仍會 Abort 該 Channel、
    /// 繼續關閉 Factory、將原始清理錯誤回傳給呼叫者，且後續 Dispose 不會再次觸碰
    /// 已釋放物件。這可防止 WCF Handle/Session 在例外路徑殘留。
    /// </summary>
    [Fact]
    public void Dispose_when_channel_close_fails_aborts_channel_closes_factory_and_is_idempotent()
    {
        typeof(IDisposable).IsAssignableFrom(typeof(OnPremiseClient)).Should().BeTrue(
            "OnPremiseClient owns WCF channel and factory resources, so the pool must be able to dispose it deterministically");

        var channel = CreateCommunicationObject(throwOnClose: true, out var channelTracker);
        var factory = CreateCommunicationObject(throwOnClose: false, out var factoryTracker);
        var client = CreateOwnedClient(new NoopOrganizationService(), channel, factory);

        Action dispose = () => ((IDisposable)client).Dispose();

        dispose.Should().Throw<AggregateException>(
            "a Close failure is operationally significant and must not be silently hidden from the pool owner");
        channelTracker.CloseCount.Should().Be(1);
        channelTracker.AbortCount.Should().Be(1, "a faulted WCF channel must be aborted after Close fails");
        factoryTracker.CloseCount.Should().Be(1, "later owned resources must still be cleaned after an earlier failure");
        factoryTracker.AbortCount.Should().Be(0);

        ((IDisposable)client).Dispose();

        channelTracker.CloseCount.Should().Be(1, "the second Dispose must not reuse a released communication object");
        channelTracker.AbortCount.Should().Be(1);
        factoryTracker.CloseCount.Should().Be(1);
    }

    /// <summary>
    /// 保護正常的連線歸還路徑。
    /// 無故障時必須優先 Close Channel 與 Factory，而不是直接 Abort，避免正常池淘汰
    /// 造成不必要的傳輸中斷；此測試同時確認兩個資源的所有權都在 Client 內而非請求
    /// 或靜態狀態中，因此不會跨 Profile 或跨使用者殘留。
    /// </summary>
    [Fact]
    public void Dispose_when_resources_are_healthy_closes_channel_and_factory_once()
    {
        typeof(IDisposable).IsAssignableFrom(typeof(OnPremiseClient)).Should().BeTrue();

        var channel = CreateCommunicationObject(throwOnClose: false, out var channelTracker);
        var factory = CreateCommunicationObject(throwOnClose: false, out var factoryTracker);
        var client = CreateOwnedClient(new NoopOrganizationService(), channel, factory);

        ((IDisposable)client).Dispose();

        channelTracker.CloseCount.Should().Be(1);
        channelTracker.AbortCount.Should().Be(0);
        factoryTracker.CloseCount.Should().Be(1);
        factoryTracker.AbortCount.Should().Be(0);
    }

    /// <summary>
    /// 保護連線池的容量計數不因下游 Client 清理失敗而外洩。
    /// 此測試注入會在 Dispose 時失敗的服務，模擬 WCF 關閉失敗後的真實情境；即使池選擇
    /// 記錄並繼續停止流程，也必須在 finally 歸還已保留的容量槽，否則未來請求會被永久誤判
    /// 為池已滿，形成記憶體之外的資源／可用性洩漏。
    /// </summary>
    [Fact]
    public void Pool_dispose_when_service_dispose_fails_releases_its_owned_capacity_slot()
    {
        var service = new ThrowingDisposableOrganizationService();
        var pool = new CrmConnectionPool(
            new SingleServiceConnectionFactory(service),
            "https://example.invalid/XRMServices/2011/Organization.svc",
            "test-user",
            "test-password",
            minPoolSize: 1,
            maxPoolSize: 1);

        pool.Dispose();

        service.DisposeCount.Should().Be(1);
        GetPrivateCurrentSize(pool).Should().Be(0,
            "a failed client Dispose must not permanently consume a pool capacity slot");
    }

    /// <summary>
    /// 建立只追蹤 Close/Abort 的 WCF 通訊物件替身；不開啟 Socket、Timer 或背景工作。
    /// </summary>
    private static ICommunicationObject CreateCommunicationObject(bool throwOnClose, out TrackingCommunicationObject tracker)
    {
        var proxy = DispatchProxy.Create<ICommunicationObject, TrackingCommunicationObject>();
        tracker = (TrackingCommunicationObject)(object)proxy;
        tracker.ThrowOnClose = throwOnClose;
        return proxy;
    }

    /// <summary>
    /// 透過僅供組件內測試使用的建構子建立 Client，使測試可驗證真正的資源擁有與釋放
    /// 行為，而不需要真實 CRM、帳密、Token、Cookie 或網路工作階段。
    /// </summary>
    private static OnPremiseClient CreateOwnedClient(
        IOrganizationService service,
        ICommunicationObject channel,
        ICommunicationObject factory)
    {
        var constructor = typeof(OnPremiseClient).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(IOrganizationService), typeof(ICommunicationObject), typeof(ICommunicationObject)],
            modifiers: null);

        constructor.Should().NotBeNull(
            "the disposal test seam must keep WCF ownership explicit without exposing a production credential or endpoint API");

        return (OnPremiseClient)constructor!.Invoke([service, channel, factory]);
    }

    /// <summary>
    /// 讀取僅供測試驗證的私有容量計數；不修改任何執行期狀態，因此不會改變連線池行為。
    /// </summary>
    private static int GetPrivateCurrentSize(CrmConnectionPool pool)
    {
        var field = typeof(CrmConnectionPool).GetField("_currentSize", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("capacity ownership is the contract under test");
        return (int)field!.GetValue(pool)!;
    }

    /// <summary>
    /// 動態攔截 WCF 介面以記錄釋放順序；除了本測試需要的狀態、Close、Abort 外，
    /// 其餘呼叫均無副作用，確保測試本身不留下通訊資源。
    /// </summary>
    private class TrackingCommunicationObject : DispatchProxy
    {
        /// <summary>取得或設定 Close 是否模擬失敗。</summary>
        public bool ThrowOnClose { get; set; }

        /// <summary>取得 Close 呼叫次數。</summary>
        public int CloseCount { get; private set; }

        /// <summary>取得 Abort 呼叫次數。</summary>
        public int AbortCount { get; private set; }

        /// <summary>
        /// 攔截 WCF 成員呼叫；只有 State、Close、Abort 可執行，其餘成員為無副作用 no-op，
        /// 以確保測試不建立背景工作、Handle 或真實通訊資源。
        /// </summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_State" => CommunicationState.Opened,
                "Close" => Close(),
                "Abort" => Abort(),
                _ => null
            };
        }

        /// <summary>記錄正常關閉，並在指定情境注入可預期的 WCF 清理失敗。</summary>
        private object? Close()
        {
            CloseCount++;
            if (ThrowOnClose)
            {
                throw new CommunicationException("測試用 Close 失敗。");
            }

            return null;
        }

        /// <summary>記錄強制中止，模擬 Close 失敗後的最後釋放路徑。</summary>
        private object? Abort()
        {
            AbortCount++;
            return null;
        }
    }

    /// <summary>
    /// 不連線的 CRM 服務替身。此替身保護本測試只驗證資源釋放契約，任何意外的 CRM
    /// 操作都會立即失敗，避免測試將請求、身分或連線狀態帶入全域環境。
    /// </summary>
    private class NoopOrganizationService : IOrganizationService
    {
        /// <summary>拒絕關聯操作，證明此替身不會向 CRM 發出要求。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw UnexpectedOperation();

        /// <summary>拒絕建立實體操作，證明此替身不會向 CRM 發出要求。</summary>
        public Guid Create(Entity entity) => throw UnexpectedOperation();

        /// <summary>拒絕刪除實體操作，證明此替身不會向 CRM 發出要求。</summary>
        public void Delete(string entityName, Guid id) => throw UnexpectedOperation();

        /// <summary>拒絕解除關聯操作，證明此替身不會向 CRM 發出要求。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw UnexpectedOperation();

        /// <summary>拒絕一般 Execute 操作，避免測試意外建立連線或工作階段。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw UnexpectedOperation();

        /// <summary>拒絕單筆讀取操作，證明生命週期測試完全離線。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw UnexpectedOperation();

        /// <summary>拒絕多筆讀取操作，證明生命週期測試完全離線。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw UnexpectedOperation();

        /// <summary>拒絕更新操作，證明此替身不會修改 CRM 狀態。</summary>
        public void Update(Entity entity) => throw UnexpectedOperation();

        /// <summary>產生明確錯誤，指出測試邊界之外的 CRM 操作不被允許。</summary>
        private static InvalidOperationException UnexpectedOperation() => new("此生命週期測試不得執行 CRM 操作。");
    }

    /// <summary>
    /// 模擬下游 Client 的清理失敗；不含網路、憑證或背景資源，只用來驗證 Pool 的 finally
    /// 路徑是否仍歸還容量槽。
    /// </summary>
    private sealed class ThrowingDisposableOrganizationService : NoopOrganizationService, IDisposable
    {
        /// <summary>取得 Dispose 已呼叫的次數。</summary>
        public int DisposeCount { get; private set; }

        /// <summary>記錄釋放嘗試後故意失敗，模擬下游 WCF Close/Abort 無法完成的情境。</summary>
        public void Dispose()
        {
            DisposeCount++;
            throw new InvalidOperationException("測試用 Service Dispose 失敗。");
        }
    }

    /// <summary>
    /// 只回傳同一個測試服務的連線建立器。所有未使用的舊式 CRM 方法都立即失敗，避免測試
    /// 擴大成登入或 Discovery 測試，也確保沒有帳密、Token 或工作階段可被保留。
    /// </summary>
    private sealed class SingleServiceConnectionFactory : ICrmConnectionService
    {
        /// <summary>初始化只供本測試使用的服務實體。</summary>
        public SingleServiceConnectionFactory(IOrganizationService service) => Service = service;

        /// <summary>取得可供連線池建立時回傳的固定服務。</summary>
        private IOrganizationService Service { get; }

        /// <summary>回傳固定服務，不建立新的連線。</summary>
        public IOrganizationService CreateOnPremiseClient(string url, string userName, string password) => Service;

        /// <summary>此測試不使用帳密轉換，禁止呼叫以避免任何敏感資料流動。</summary>
        public ClientCredentials GetClientCredentials(string domain, string userName, string password) => throw UnexpectedOperation();

        /// <summary>此測試不使用預設帳密，禁止呼叫以維持離線性。</summary>
        public ClientCredentials GetClientCredentials() => throw UnexpectedOperation();

        /// <summary>此測試不使用舊式 OrganizationService 建立路徑。</summary>
        public IOrganizationService GetOrganizationService(string server, string port, string organization, string domain, string userName, string password) => throw UnexpectedOperation();

        /// <summary>此測試不使用舊式 OrganizationService 設定路徑。</summary>
        public IOrganizationService SetOrganizationService(string server, string port, string organization, string domain, string userName, string password) => throw UnexpectedOperation();

        /// <summary>此測試不使用 Claims 驗證設定路徑。</summary>
        public IOrganizationService SetClaimsBasedAuthenticationOrganizationService(string organization, string server, string domain, string userName, string password) => throw UnexpectedOperation();

        /// <summary>此測試不建立 Federated Proxy，避免外部傳輸資源。</summary>
        public OrganizationServiceProxy SetFederatedOrganizationProxy(string discoveryServiceType, string organization, string server, string port, string baseDiscoveryServiceAddress, string userName, string password, string domain) => throw UnexpectedOperation();

        /// <summary>此測試不使用 Discovery，避免網路與快取狀態。</summary>
        public OrganizationDetailCollection DiscoverOrganizations(IDiscoveryService service) => throw UnexpectedOperation();

        /// <summary>此測試不查找 Organization，避免外部資料相依。</summary>
        public OrganizationDetail FindOrganization(string orgUniqueName, OrganizationDetail[] orgDetails) => throw UnexpectedOperation();

        /// <summary>回傳健康狀態，因本測試只驗證釋放路徑。</summary>
        public bool ValidateConnection(IOrganizationService service) => true;

        /// <summary>此測試不讀取目前使用者，避免 CRM 操作。</summary>
        public Entity GetCurrentUser(IOrganizationService service) => throw UnexpectedOperation();

        /// <summary>此測試不讀取目前使用者識別碼，避免 CRM 操作。</summary>
        public Guid GetCurrentUserId(IOrganizationService service) => throw UnexpectedOperation();

        /// <summary>此測試不讀取 Organization 識別碼，避免 CRM 操作。</summary>
        public Guid GetCurrentOrganizationId(IOrganizationService service) => throw UnexpectedOperation();

        /// <summary>產生明確錯誤，指出呼叫已超出資源釋放測試的安全邊界。</summary>
        private static InvalidOperationException UnexpectedOperation() => new("此生命週期測試不得執行 CRM 操作。");
    }
}
