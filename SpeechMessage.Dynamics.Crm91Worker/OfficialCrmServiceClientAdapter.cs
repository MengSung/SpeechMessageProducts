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

[assembly: InternalsVisibleTo("SpeechMessage.Dynamics.Crm91Worker.Tests")]

namespace SpeechMessage.Dynamics.Crm91Worker;

/// <summary>
/// 定義 CE 9.1 adapter 實際需要的最小同步 SDK surface。
/// 此介面只存在於 worker assembly 內，讓 production wrapper 與 worker-only tests 共用同一條
/// Execute／RetrieveMultiple 契約；它不會跨 IPC 暴露，也不保存 Session、caller identity、
/// QueryExpression cache 或跨 request mutable state。唯一 disposable owner 是 generation-local client；
/// OrganizationRequest／Response、QueryExpression、EntityCollection 與 Entity 都是單次方法範圍的 SDK object，
/// 完成 SDK-free 投影後即不再保留。
/// </summary>
internal interface ICrm91SdkClient : IDisposable
{
    /// <summary>取得官方 client 當下 readiness；外層 publication probe 會將 SDK 例外轉成 fail-closed。</summary>
    bool IsReady { get; }

    /// <summary>取得官方 client 已連線組織版本，供 fail-closed CE 9.1 identity 驗證。</summary>
    Version? ConnectedOrgVersion { get; }

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
/// 包裝唯一由 CE 9.1 worker generation 擁有並重複使用的 <see cref="CrmServiceClient"/>。
/// Worker session 依序處理單一在途 operation，因此本型別不以平行呼叫換取吞吐，也不為每次 request
/// 重建 client、連線或 WCF graph。SDK 呼叫為同步且不提供可中斷取消；deadline 到期後由 Supervisor
/// 終止整個 worker process，作為卡住之 SDK/WCF handle 與 unmanaged memory 的最終清理邊界。
/// 本 wrapper 不擁有 Pipe、Stream、Timer、CancellationTokenRegistration 或 background task；那些資源由
/// Worker Host／Supervisor generation 各自在 process boundary 兩側有界管理。
/// </summary>
internal sealed class Crm91SdkClient : ICrm91SdkClient
{
    private CrmServiceClient? _client;

    /// <summary>接管 factory 已建立的單一 CE 9.1 official client。</summary>
    /// <param name="client">尚未被其他 owner 釋放，且只屬於本 worker generation 的官方 client。</param>
    internal Crm91SdkClient(CrmServiceClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// 讀取 SDK readiness；外層 <see cref="OfficialCrmServiceClientAdapter"/> 會攔截 getter 例外，
    /// 避免原始 SDK 診斷穿越 readiness boundary。
    /// </summary>
    public bool IsReady => GetClient().IsReady;

    /// <summary>
    /// 讀取已連線組織版本；釋放後會先以 <see cref="ObjectDisposedException"/> 拒絕，
    /// 不會重新建立 client 或猜測 CE 版本。
    /// </summary>
    public Version? ConnectedOrgVersion => GetClient().ConnectedOrgVersion;

    /// <summary>
    /// 同步執行固定 OrganizationRequest。進入 SDK 後沒有 per-call cancellation hook；
    /// Supervisor timeout 只能停止等待並依回收流程終止 worker process。
    /// </summary>
    /// <param name="request">adapter 建立的固定 request。</param>
    /// <returns>官方 SDK response。</returns>
    public OrganizationResponse Execute(OrganizationRequest request) =>
        GetClient().Execute(request);

    /// <summary>
    /// 同步執行本次 operation 擁有的 QueryExpression；不使用 Task.Run、parallel paging 或跨要求 query cache。
    /// </summary>
    /// <param name="query">固定 server-owned query。</param>
    /// <returns>官方 SDK page。</returns>
    public EntityCollection RetrieveMultiple(QueryExpression query) =>
        GetClient().RetrieveMultiple(query);

    /// <summary>
    /// 以 Interlocked 取走唯一 client owner 並釋放一次；重複呼叫為 no-op，
    /// 確保 WCF/SDK resource 不會因競爭 disposal 被重複使用。
    /// </summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _client, null)?.Dispose();
    }

    /// <summary>
    /// 取得仍由本 generation 擁有的 client；釋放後 fail closed，絕不建立替代連線。
    /// </summary>
    /// <returns>唯一且尚未釋放的 CE 9.1 client。</returns>
    private CrmServiceClient GetClient() =>
        Volatile.Read(ref _client) ??
        throw new ObjectDisposedException(nameof(Crm91SdkClient));
}

/// <summary>
/// 將 CE 9.1 official client 限制在單一 worker process，並依 operation ID 分派固定 WhoAmI
/// 或 Package01 fee query。每個 worker generation 重複使用一個 client，message loop 同時只執行
/// 一個 operation；所有 SDK object 都在方法返回前投影成 bounded <see cref="WorkerValue"/>。
/// Adapter 不保存 caller Session、contactName、QueryExpression、Entity 或跨 request cache。
/// </summary>
internal sealed class OfficialCrmServiceClientAdapter : IOfficialCrmClient
{
    private ICrm91SdkClient? _client;
    private OfficialCrmCredential? _credential;
    private readonly Guid _expectedOrganizationId;
    private readonly string _expectedCeVersion;
    private readonly bool _identityProbeSucceeded;

    /// <summary>
    /// 在建構完整成功後接管 factory 建立的 <see cref="CrmServiceClient"/> 與 optional credential，
    /// 並在 publication 前同步完成一次固定 identity probe；probe failure 只留下 NotReady 狀態，
    /// 最終仍由此 adapter 的 <see cref="Dispose"/> 決定性釋放 client 與 credential。若參數驗證使 constructor
    /// 拋錯，ownership 尚未提交，factory 的 finally 仍負責釋放原 client／credential。
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
            new Crm91SdkClient(client),
            credential,
            expectedOrganizationId,
            expectedCeVersion)
    {
    }

    /// <summary>
    /// 建立可由 worker-only tests 注入同步 SDK 替身的 adapter；constructor 成功返回後才接管 client owner，
    /// 不允許 caller 在 adapter 釋放後繼續使用同一 client；constructor 拋錯時 caller 仍是 cleanup owner。
    /// </summary>
    /// <param name="client">由 adapter 接管唯一 ownership 的同步 SDK client。</param>
    /// <param name="credential">optional worker-owned credential；測試可使用 null。</param>
    /// <param name="expectedOrganizationId">固定 expected organization ID。</param>
    /// <param name="expectedCeVersion">固定 expected CE major/minor。</param>
    internal OfficialCrmServiceClientAdapter(
        ICrm91SdkClient client,
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
    /// getter 將 SDK readiness 例外轉成 false，且不建立連線、query、timer 或 background work。
    /// </summary>
    public bool IsReady
    {
        get
        {
            var client = Volatile.Read(ref _client);
            return client is not null &&
                _identityProbeSucceeded &&
                IsClientReady(client);
        }
    }

    /// <summary>
    /// 同步分派唯一 allowlist operation。方法先確認 adapter 尚未釋放，再驗證 operation；
    /// 因此 dispose 後即使輸入未知 operation 也不能碰觸 SDK。Package01 的 contactName 會在
    /// query operation 內再次由 shared contract 驗證並丟棄，所有結果都保持 SDK-free。
    /// 一旦進入同步 SDK 呼叫，request cancellation 無法中斷該呼叫；Supervisor 會在有限 deadline
    /// 後停止等待、關閉 admission，並於 graceful drain 失敗時強制終止 worker process。
    /// WhoAmIRequest／Response 與 Package01 query page 都只存活於本次 call stack，投影後不進入 field、cache
    /// 或 callback；adapter field 只保留 generation-local client、credential 與 immutable identity scalar。
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

        // CrmServiceClient 的同步 Execute 沒有可傳入的 CancellationToken。deadline 失效時不嘗試
        // 在同一 process 內重用未知狀態 client；Supervisor 會依有限 drain deadline 強制終止 generation。
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
    /// 先以 Interlocked 關閉 admission 並取走唯一 client／credential owner，再依序釋放 client 與
    /// credential。即使 client disposal 失敗，credential 仍在 finally 清除；重複 Dispose 不會
    /// 重複釋放或恢復可執行狀態。若同步 SDK disposal 卡住，Supervisor 的 process termination
    /// 仍是 WCF channel、handle 與 worker memory 的最終 cleanup boundary。
    /// Pipe stream、reader task、timeout timer 與 cancellation registration 不屬於 adapter，分別由
    /// Worker Host 與 Supervisor generation 在其 own finally／DisposeAsync 流程釋放。
    /// </summary>
    public void Dispose()
    {
        var client = Interlocked.Exchange(ref _client, null);
        var credential = Interlocked.Exchange(ref _credential, null);
        Exception? failure = null;
        try
        {
            // 先關閉可執行狀態再釋放 SDK owner，避免 Dispose 與新 operation 交錯取得 client。
            client?.Dispose();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            // Credential 的清除不可被 client disposal failure 跳過；它是本 generation 的第二個唯一 owner。
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
        ICrm91SdkClient client,
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
            // Readiness 只暴露 fail-closed boolean；不把 SDK exception、endpoint 或認證細節保存或送出 IPC。
            return false;
        }
    }

    /// <summary>
    /// 將 SDK readiness getter 的任何失敗正規化為 false，避免 probe 將原始 SDK 例外當成可用狀態。
    /// </summary>
    /// <param name="client">本 generation 唯一的同步 SDK client。</param>
    /// <returns>getter 明確回傳 true 時才為 true；例外或 false 均 fail closed。</returns>
    private static bool IsClientReady(ICrm91SdkClient client)
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
    /// 將 WhoAmIResponse 投影成三個固定 GUID；SDK response 不會離開 worker method scope，
    /// 投影完成後沒有 field、Task continuation 或 collection 保留它。
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
