using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

[assembly: InternalsVisibleTo("SpeechMessage.Dynamics.Crm82Worker.Tests")]

namespace SpeechMessage.Dynamics.Crm82Worker;

/// <summary>
/// 定義 CE 8.2 adapter 實際需要的最小同步 SDK surface。
/// 介面只存在於 worker assembly 內，讓 production wrapper 與 worker-only tests 共用同一條
/// Execute／RetrieveMultiple 契約；它不會跨 IPC 暴露，也不保存 Session、caller identity、
/// QueryExpression cache 或跨 request mutable state。
/// </summary>
internal interface ICrm82SdkClient : IDisposable
{
    /// <summary>取得官方 client 當下 readiness；釋放後不得再回傳可用狀態。</summary>
    bool IsReady { get; }

    /// <summary>取得官方 client 已連線組織版本，供 fail-closed CE 8.2 identity 驗證。</summary>
    Version? ConnectedOrgVersion { get; }

    /// <summary>
    /// 取得 SDK 對目前尚未 ready client 提供的最後一個 exception。呼叫端只能立即投影其型別家族為
    /// 固定安全分類，絕不可保存、記錄或跨 IPC 傳遞 exception／訊息／InnerException，因為它可能含
    /// endpoint、organization 或 authentication 細節。
    /// </summary>
    Exception? LastStartupException { get; }

    /// <summary>
    /// 同步執行固定 server-owned OrganizationRequest；caller 不得提供 generic Execute payload。
    /// </summary>
    /// <param name="request">adapter 建立的固定 SDK request。</param>
    /// <returns>官方 SDK response，僅可在 worker 內投影。</returns>
    OrganizationResponse Execute(OrganizationRequest request);

    /// <summary>
    /// 同步執行 worker-owned QueryExpression；query 生命週期只涵蓋單一 operation 呼叫。
    /// </summary>
    /// <param name="query">已套用固定 entity、欄位、條件、排序與 paging 的查詢。</param>
    /// <returns>官方 SDK page，必須在 worker 內完成型別驗證與 SDK-free 投影。</returns>
    EntityCollection RetrieveMultiple(QueryExpression query);
}

/// <summary>
/// 包裝唯一由 CE 8.2 worker adapter 擁有的 <see cref="CrmServiceClient"/>。
/// wrapper 不建立 cache 或背景工作，只同步轉送必要 SDK 呼叫；Dispose 是 client 的唯一釋放路徑，
/// 由外層 adapter 在 message loop 停止後恰好呼叫一次。
/// </summary>
internal sealed class Crm82SdkClient : ICrm82SdkClient
{
    private CrmServiceClient? _client;

    /// <summary>接管 factory 已建立的單一 CE 8.2 official client。</summary>
    /// <param name="client">尚未被其他 owner 釋放的官方 client。</param>
    internal Crm82SdkClient(CrmServiceClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// 讀取 client readiness；SDK getter 失敗時回傳 false，避免 readiness probe 洩漏原始例外。
    /// </summary>
    public bool IsReady
    {
        get
        {
            var client = Volatile.Read(ref _client);
            if (client is null)
            {
                return false;
            }

            try
            {
                return client.IsReady;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 讀取已連線組織版本；釋放後回傳 null，讓 identity validation fail closed。
    /// </summary>
    public Version? ConnectedOrgVersion => Volatile.Read(ref _client)?.ConnectedOrgVersion;

    /// <summary>
    /// 在 client 尚未 ready 時暫時讀取官方 SDK 提供的最後失敗物件。任何 getter failure 都轉成 null，
    /// 讓上層 fail closed 為 unclassified；本 wrapper 不保存 exception reference，分類完成後 GC 可回收
    /// 它及其可能的 stack graph，避免跨 generation 的 diagnostic retention。
    /// </summary>
    public Exception? LastStartupException
    {
        get
        {
            var client = Volatile.Read(ref _client);
            if (client is null)
            {
                return null;
            }

            try
            {
                return client.LastCrmException;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 同步執行固定 OrganizationRequest；已釋放時在碰觸 SDK 前拒絕。
    /// </summary>
    /// <param name="request">adapter 建立的固定 request。</param>
    /// <returns>官方 SDK response。</returns>
    public OrganizationResponse Execute(OrganizationRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var client = Volatile.Read(ref _client) ??
            throw new ObjectDisposedException(nameof(Crm82SdkClient));
        return client.Execute(request);
    }

    /// <summary>
    /// 同步執行本次 operation 擁有的 QueryExpression；不使用 Task.Run 或 parallel paging。
    /// </summary>
    /// <param name="query">固定 server-owned query。</param>
    /// <returns>官方 SDK page。</returns>
    public EntityCollection RetrieveMultiple(QueryExpression query)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        var client = Volatile.Read(ref _client) ??
            throw new ObjectDisposedException(nameof(Crm82SdkClient));
        return client.RetrieveMultiple(query);
    }

    /// <summary>
    /// 以 Interlocked 取走唯一 client owner 並釋放一次；重複呼叫為 no-op，
    /// 確保 WCF/SDK resource 不會因競爭 disposal 被重複使用。
    /// </summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _client, null)?.Dispose();
    }
}

/// <summary>
/// 將 CE 8.2 official client 限制在單一 worker process，並依 operation ID 分派固定 WhoAmI
/// 或 Package01 fee query。所有 SDK object 都在方法返回前投影成 bounded <see cref="WorkerValue"/>；
/// adapter 不保存 caller Session、contactName、QueryExpression、Entity 或跨 request cache。
/// </summary>
internal sealed class OfficialCrmServiceClientAdapter :
    IOfficialCrmClient,
    IOfficialCrmClientStartupDiagnostics
{
    private ICrm82SdkClient? _client;
    private OfficialCrmCredential? _credential;
    private readonly Guid _expectedOrganizationId;
    private readonly string _expectedCeVersion;
    private readonly bool _identityProbeSucceeded;

    /// <summary>
    /// 接管 factory 建立的 <see cref="CrmServiceClient"/> 與 optional credential，
    /// 並在 publication 前同步完成一次固定 identity probe；失敗只留下 NotReady 狀態，
    /// 最終仍由此 adapter 的 Dispose 決定性釋放 client 與 credential。
    /// </summary>
    /// <param name="client">factory 建立且尚未轉交其他 owner 的官方 client。</param>
    /// <param name="credential">由 worker-local provider 取得、需隨 client 一起釋放的 credential。</param>
    /// <param name="expectedOrganizationId">profile 綁定且不可由 request 改寫的組織 ID。</param>
    /// <param name="expectedCeVersion">必須與 executable package graph 一致的 CE 版本。</param>
    internal OfficialCrmServiceClientAdapter(
        CrmServiceClient client,
        OfficialCrmCredential? credential,
        Guid expectedOrganizationId,
        string expectedCeVersion)
        : this(
            new Crm82SdkClient(client ?? throw new ArgumentNullException(nameof(client))),
            credential,
            expectedOrganizationId,
            expectedCeVersion)
    {
    }

    /// <summary>
    /// 建立可由 worker-only tests 注入同步 SDK 替身的 adapter；此 overload 仍接管 client owner，
    /// 不允許 caller 在 adapter 釋放後繼續使用同一 client。
    /// </summary>
    /// <param name="client">由 adapter 接管唯一 ownership 的同步 SDK client。</param>
    /// <param name="credential">optional worker-owned credential；測試可使用 null。</param>
    /// <param name="expectedOrganizationId">固定 expected organization ID。</param>
    /// <param name="expectedCeVersion">固定 expected CE major/minor。</param>
    internal OfficialCrmServiceClientAdapter(
        ICrm82SdkClient client,
        OfficialCrmCredential? credential,
        Guid expectedOrganizationId,
        string expectedCeVersion)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _credential = credential;
        _expectedOrganizationId = expectedOrganizationId != Guid.Empty
            ? expectedOrganizationId
            : throw new ArgumentException(
                "The expected organization identifier is required.",
                nameof(expectedOrganizationId));
        _expectedCeVersion = expectedCeVersion is "8.2" or "9.1"
            ? expectedCeVersion
            : throw new ArgumentException(
                "The expected CE version is invalid.",
                nameof(expectedCeVersion));
        _identityProbeSucceeded = ProbeIdentity(
            client,
            _expectedOrganizationId,
            _expectedCeVersion);
    }

    /// <summary>
    /// 只有 client owner 尚在、startup identity probe 成功且 SDK readiness 仍有效時才為 true；
    /// getter 不建立連線、query、timer 或 background work。
    /// </summary>
    public bool IsReady => StartupReadiness == OfficialCrmClientStartupReadiness.Ready;

    /// <summary>
    /// 以固定且去識別化的 enum 區分 SDK client 本身未 ready 與 WhoAmI identity probe 未通過。
    /// 這個 getter 只讀取目前 generation-local client 與建構期 immutable probe 結果，不重新驗證、
    /// 不建立任何 CRM request、timer、cache 或跨 profile state；Supervisor 只能據此回報固定 exit code，
    /// 不得將狀態用作 connector fallback、秘密診斷或 request-time routing。
    /// </summary>
    public OfficialCrmClientStartupReadiness StartupReadiness
    {
        get
        {
            var client = Volatile.Read(ref _client);
            if (client is null || !IsClientReady(client))
            {
                return OfficialCrmClientStartupReadiness.SdkClientNotReady;
            }

            return _identityProbeSucceeded
                ? OfficialCrmClientStartupReadiness.Ready
                : OfficialCrmClientStartupReadiness.IdentityProbeNotReady;
        }
    }

    /// <summary>
    /// 將目前 SDK-not-ready detail 立即投影為共用的安全 enum。SDK client 已 ready 或 identity probe
    /// failure 時都回傳 None；這個 property 不會建立連線或重試，且不會把 exception、endpoint、帳密、
    /// token 或 CRM payload 保存到 adapter field、process exit 或 IPC。
    /// </summary>
    public OfficialCrmClientStartupFailureCategory StartupFailureCategory
    {
        get
        {
            var client = Volatile.Read(ref _client);
            return client is null || IsClientReady(client)
                ? OfficialCrmClientStartupFailureCategory.None
                : OfficialCrmClientStartupFailureClassifier.Classify(
                    client.LastStartupException);
        }
    }

    /// <summary>
    /// 同步分派唯一 allowlist operation。方法先確認 adapter 尚未釋放，再驗證 operation；
    /// 因此 dispose 後即使輸入未知 operation 也不能碰觸 SDK。Package01 的 contactName 會在
    /// query operation 內再次由 shared contract 驗證並丟棄，所有結果都保持 SDK-free。
    /// </summary>
    /// <param name="request">已由 Worker session 驗證 nonce、revision 與 deadline 的 request。</param>
    /// <returns>固定 WhoAmI object 或 Package01 Array&lt;Page&lt;Row&gt;&gt;。</returns>
    public WorkerValue Execute(WorkerRequestV1 request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var client = Volatile.Read(ref _client) ??
            throw new ObjectDisposedException(nameof(OfficialCrmServiceClientAdapter));
        if (string.Equals(
                request.CapabilityOperationId,
                Package01FeeWorkerContract.CapabilityOperationId,
                StringComparison.Ordinal))
        {
            return Package01FeeQueryOperation.Execute(client, request);
        }

        if (!OfficialWorkerOperations.IsSupportedIdentityRequest(request))
        {
            throw new InvalidOperationException("The official CRM operation is unsupported.");
        }

        var response = client.Execute(new WhoAmIRequest()) as WhoAmIResponse ??
            throw new InvalidOperationException("The official CRM identity response is invalid.");
        if (!OfficialCrmIdentityValidator.IsValid(
                response.UserId,
                response.BusinessUnitId,
                response.OrganizationId,
                _expectedOrganizationId,
                client.ConnectedOrgVersion,
                _expectedCeVersion))
        {
            throw new InvalidOperationException("The official CRM identity response is invalid.");
        }

        return ProjectIdentity(response);
    }

    /// <summary>
    /// 先以 Interlocked 關閉 admission 並取走唯一 client/credential owner，再依序釋放 client 與
    /// credential。即使 client disposal 失敗，credential 仍在 finally 清除；重複 Dispose 不會
    /// 重複釋放或恢復可執行狀態。
    /// </summary>
    public void Dispose()
    {
        var client = Interlocked.Exchange(ref _client, null);
        var credential = Interlocked.Exchange(ref _credential, null);
        Exception? failure = null;
        try
        {
            client?.Dispose();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            credential?.Dispose();
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    /// <summary>
    /// 在 adapter publication 前以同一 client 執行一次 WhoAmI，並同時驗證 user、business unit、
    /// organization 與 CE version；任何 SDK 例外都轉成 false，不保存 response 或原始錯誤。
    /// </summary>
    /// <param name="client">本 adapter 即將接管的唯一 SDK client。</param>
    /// <param name="expectedOrganizationId">profile 固定的組織 ID。</param>
    /// <param name="expectedCeVersion">worker 固定的 CE major/minor。</param>
    /// <returns>identity 與版本全部符合時為 true，否則為 false。</returns>
    private static bool ProbeIdentity(
        ICrm82SdkClient client,
        Guid expectedOrganizationId,
        string expectedCeVersion)
    {
        if (!IsClientReady(client))
        {
            return false;
        }

        try
        {
            var response = client.Execute(new WhoAmIRequest()) as WhoAmIResponse;
            return response is not null && OfficialCrmIdentityValidator.IsValid(
                response.UserId,
                response.BusinessUnitId,
                response.OrganizationId,
                expectedOrganizationId,
                client.ConnectedOrgVersion,
                expectedCeVersion);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 讀取 CE 8.2 SDK readiness 時把 getter 自身的例外收斂為 false，讓 startup status 不會攜帶
    /// 原始 SDK 細節。這不會建立或重試連線；client 的唯一釋放路徑仍是 adapter Dispose。
    /// </summary>
    private static bool IsClientReady(ICrm82SdkClient client)
    {
        try
        {
            return client.IsReady;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 將 WhoAmIResponse 投影成三個固定 GUID；SDK response 不會離開 worker method scope。
    /// </summary>
    /// <param name="response">已通過完整 identity validation 的 SDK response。</param>
    /// <returns>僅含 userId、businessUnitId、organizationId 的 SDK-free object。</returns>
    private static WorkerValue ProjectIdentity(WhoAmIResponse response) =>
        WorkerValue.FromObject(new Dictionary<string, WorkerValue>(StringComparer.Ordinal)
        {
            ["userId"] = WorkerValue.FromGuid(response.UserId),
            ["businessUnitId"] = WorkerValue.FromGuid(response.BusinessUnitId),
            ["organizationId"] = WorkerValue.FromGuid(response.OrganizationId)
        });
}
