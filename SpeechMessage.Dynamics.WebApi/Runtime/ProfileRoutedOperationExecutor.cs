// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/ProfileRoutedOperationExecutor.cs
// 目的：把受控 Operation Registry 驗證與 Multi-Profile 合併租約取得串接成可注入的執行器。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// Profile-aware 的受控操作執行器。
/// 此型別不擁有 Runtime、Admission Manager、Client 或 Handler；它只把所有執行委派給
/// <see cref="ControlledOperationExecutor"/>，並由注入的 <see cref="IProfileExecutionLeaseProvider"/>
/// 決定 Alias、Queue、Admission 與 Active Generation。
/// </summary>
public sealed class ProfileRoutedOperationExecutor : IDynamicsOperationExecutor
{
    private readonly ControlledOperationExecutor _inner;

    /// <summary>
    /// 建立 Profile-aware 執行器。Provider 的生命週期由外層 Runtime Manager／DI Container 擁有，
    /// 此執行器不會 Dispose Provider，避免重複回收 Catalog 或 Admission Manager。
    /// </summary>
    public ProfileRoutedOperationExecutor(IProfileExecutionLeaseProvider leaseProvider)
    {
        _inner = new ControlledOperationExecutor(
            leaseProvider ?? throw new ArgumentNullException(nameof(leaseProvider)));
    }

    /// <summary>
    /// 執行一個伺服器端已註冊的受控 Dynamics 操作；所有 Alias、Admission 與 Runtime 選擇
    /// 都由 Provider 在信任邊界內完成，Request 不能提供 CRM URL、Credential 或任意 Transport。
    /// </summary>
    public Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken = default)
        => _inner.ExecuteAsync(request, cancellationToken);
}

/// <summary>
/// 保留舊版單一 Profile 建構方式的相容 Provider。
/// 它把既有固定 Client 與 Admission Manager 包裝成同一個合併租約契約，讓舊測試與已完成路徑
/// 不需重複取得 Admission，也能和 Multi-Profile 路徑共用完全相同的取消與釋放順序。
/// </summary>
internal sealed class FixedProfileExecutionLeaseProvider : IProfileExecutionLeaseProvider
{
    private readonly IDynamicsWebApiClient _client;
    private readonly IOrganizationAdmissionManager _admissionManager;

    /// <summary>
    /// 建立不擁有注入資源的相容 Provider；Client 與 Admission Manager 仍由原本的 Factory／Host 負責回收。
    /// </summary>
    public FixedProfileExecutionLeaseProvider(
        IDynamicsWebApiClient client,
        IOrganizationAdmissionManager admissionManager)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _admissionManager = admissionManager ?? throw new ArgumentNullException(nameof(admissionManager));
    }

    /// <summary>
    /// 解析固定單一 Profile 路徑的 Admission Plan。空白 Alias 會 fail closed；其他 Alias 只代表既有相容入口，
    /// 不會改變 Client、Endpoint、Credential 或 Transport，也不會觸發任何外部 I/O。
    /// </summary>
    public bool TryGetAdmissionPlan(
        string profileAlias,
        out OrganizationAdmissionPlan? admissionPlan)
    {
        admissionPlan = string.IsNullOrWhiteSpace(profileAlias)
            ? null
            : _admissionManager.Plan;
        return admissionPlan is not null;
    }

    /// <summary>
    /// 取得固定 Client 路徑的 Admission Permit，成功後把 Permit 與借用的 Client 包裝成單一合併租約。
    /// Provider 不擁有 Client 或 Admission Manager，只接管本次 Permit；取得失敗時不回傳半完成資源，
    /// 呼叫者取消會傳入 Admission Queue，避免 waiter 或 Semaphore 名額無界保留。
    /// </summary>
    public async Task<ProfileExecutionLeaseAcquireResult> AcquireAsync(
        DispatchEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var admission = await _admissionManager
            .AcquireAsync(envelope, cancellationToken)
            .ConfigureAwait(false);
        if (!admission.Succeeded || admission.Permit is null)
        {
            return ProfileExecutionLeaseAcquireResult.Failure(
                admission.Error ?? OperationExecutionResult.Failure(
                    DynamicsErrorCodes.CapacityRejected,
                    "Admission was rejected."));
        }

        return ProfileExecutionLeaseAcquireResult.Success(
            new FixedExecutionLease(_client, _admissionManager.Plan, admission.Permit));
    }

    /// <summary>
    /// 固定 Client 路徑的合併租約。沒有 Runtime 引用計數可釋放，因此唯一資源是 Admission Permit；
    /// Dispose 仍使用 idempotent 旗標，確保同步／非同步競速時只歸還一次容量。
    /// </summary>
    private sealed class FixedExecutionLease : IProfileExecutionLease
    {
        private readonly IAdmissionPermit _permit;
        private int _disposed;

        /// <summary>建立已取得 Admission Permit 的固定 Client 租約，並接管 Permit 的唯一 ownership。</summary>
        public FixedExecutionLease(
            IDynamicsWebApiClient client,
            OrganizationAdmissionPlan admissionPlan,
            IAdmissionPermit permit)
        {
            Client = client;
            AdmissionPlan = admissionPlan;
            _permit = permit;
        }

        /// <summary>固定相容路徑沒有 Generation Catalog，因此回傳 null，且呼叫端不得以此推導或切換 Profile。</summary>
        public ProfileRuntimeKey? RuntimeKey => null;

        /// <summary>取得由外層 Host 擁有的固定 Client；此 Lease 不 Dispose Client，只限制其使用範圍。</summary>
        public IDynamicsWebApiClient Client { get; }

        /// <summary>取得本次 Permit 的不可變容量計畫，用來套用相同的 timeout 與 Host Slot fencing 規則。</summary>
        public OrganizationAdmissionPlan AdmissionPlan { get; }

        /// <summary>取得 Admission Host Slot 遺失時的取消訊號，外呼與 retry 必須與 caller token 一起觀察。</summary>
        public CancellationToken LeaseLostToken => _permit.LeaseLostToken;

        /// <summary>固定 Client 相容路徑沒有 Generation retirement，因此回傳不可取消的 Token。</summary>
        public CancellationToken RetirementToken => CancellationToken.None;

        /// <summary>
        /// 同步歸還 Admission Permit；Permit 自身的同步路徑必須依既有契約確定性完成 release。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _permit.Dispose();
            }
        }

        /// <summary>
        /// 非同步歸還 Admission Permit 並等待 release 完成，不啟動 fire-and-forget 清理工作。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                await _permit.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
