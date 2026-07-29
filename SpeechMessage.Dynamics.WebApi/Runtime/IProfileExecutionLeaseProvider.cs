// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/IProfileExecutionLeaseProvider.cs
// 目的：定義「先取得 Organization Admission Permit，再取得當下 Active Runtime Lease」的
//       合併租約契約，讓單一 Profile 舊路徑與 Multi-Profile 路由共用同一個受控操作執行器。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 表示一次已同時取得 Organization Admission Permit 與可用 Profile Runtime 的執行租約。
/// 此租約是外呼 Dynamics 前唯一允許持有的資源組合：Admission 控制實體 Organization 的共享容量，
/// Runtime Lease 則固定本次呼叫使用的 Client／Token／Handler Generation，兩者都不保存終端使用者
/// Session、LINE ID、JWT、Cookie、Credential 或 Request Body。
/// </summary>
/// <remarks>
/// 釋放順序是安全契約的一部分：必須先釋放 Runtime Execution Lease，讓該 Generation 的 active count
/// 正確歸零，再釋放 Admission Permit。如此 Runtime drain 不會因 Permit 已提早歸還而與替代流量重疊，
/// 也不會讓仍在執行的工作使用已被回收的 Client 或 Handler。
/// </remarks>
public interface IProfileExecutionLease : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// 取得實際選中的 Runtime Generation Key；單一固定 Client 相容路徑沒有 Runtime Manager，因此可為 null。
    /// 此值只供診斷與測試，不得作為 User／Session Cache Key。
    /// </summary>
    ProfileRuntimeKey? RuntimeKey { get; }

    /// <summary>
    /// 取得本次租約綁定的 Dynamics Client。呼叫端不得把 Client 保存到租約生命週期之外，
    /// 否則 replace-and-drain 將無法證明舊 Generation 已無強引用。
    /// </summary>
    IDynamicsWebApiClient Client { get; }

    /// <summary>
    /// 取得 Admission Permit 所屬的不可變容量計畫，用於計算本次外呼的最長生命週期。
    /// 計畫不含 Token、Credential、Request 或 Session 資料。
    /// </summary>
    OrganizationAdmissionPlan AdmissionPlan { get; }

    /// <summary>
    /// 取得 Runtime Host Slot 遺失或被 fencing 時的取消訊號；外呼必須與 caller cancellation 連結。
    /// </summary>
    CancellationToken LeaseLostToken { get; }

    /// <summary>
    /// 取得 Runtime Generation 因 drain timeout 進入強制退休時的取消訊號。
    /// 固定單一 Client 相容路徑沒有 Generation retirement，因此回傳不可取消的 Token。
    /// </summary>
    CancellationToken RetirementToken { get; }
}

/// <summary>
/// 合併租約取得結果。失敗時只回傳已清理完成的受控錯誤，不把半取得的 Permit、Runtime、
/// Token、Credential 或例外物件交給呼叫端，避免失敗路徑形成容量與記憶體保留。
/// </summary>
public sealed class ProfileExecutionLeaseAcquireResult
{
    /// <summary>取得是否成功；成功時 <see cref="Lease"/> 必定非 null。</summary>
    public bool Succeeded { get; init; }

    /// <summary>取得由呼叫端唯一擁有、必須確定性釋放的合併租約。</summary>
    public IProfileExecutionLease? Lease { get; init; }

    /// <summary>取得已清理所有部分資源後的受控失敗結果。</summary>
    public OperationExecutionResult? Error { get; init; }

    /// <summary>
    /// 建立成功結果並轉移租約 ownership；呼叫端之後必須使用 <c>await using</c> 釋放。
    /// </summary>
    public static ProfileExecutionLeaseAcquireResult Success(IProfileExecutionLease lease)
        => new()
        {
            Succeeded = true,
            Lease = lease ?? throw new ArgumentNullException(nameof(lease))
        };

    /// <summary>
    /// 建立失敗結果。呼叫此方法前，Provider 必須已釋放任何取得到一半的 Admission Permit 或 Runtime Lease。
    /// </summary>
    public static ProfileExecutionLeaseAcquireResult Failure(OperationExecutionResult error)
        => new()
        {
            Succeeded = false,
            Error = error ?? throw new ArgumentNullException(nameof(error))
        };
}

/// <summary>
/// 為受控操作提供 Profile-aware 合併租約。
/// Provider 必須遵守固定順序：先解析已核准 Alias 與 Admission Plan、等待共享 Queue／Permit，
/// 再於排隊完成後取得「當下」Active Runtime Lease；排隊期間禁止保存舊 Runtime 或 Client 引用。
/// </summary>
public interface IProfileExecutionLeaseProvider
{
    /// <summary>
    /// 在任何 Admission、Secret、Factory、Token 或 Transport I/O 之前解析 Alias 的容量計畫。
    /// 未知、尚未 Ready 或已停止的 Alias 回傳 false，讓呼叫端 fail closed。
    /// </summary>
    bool TryGetAdmissionPlan(
        string profileAlias,
        out OrganizationAdmissionPlan? admissionPlan);

    /// <summary>
    /// 依 Dispatch Envelope 取得 Admission Permit，排隊完成後再取得當下 Active Runtime Lease。
    /// 若 Runtime 已交換為不相容的 Canonical Binding、正在停止或拒絕新 Lease，Provider 必須先釋放 Permit，
    /// 再回傳受控 NotReady 錯誤；不得留下 ActivePermits、Semaphore 名額或舊 Runtime 強引用。
    /// </summary>
    Task<ProfileExecutionLeaseAcquireResult> AcquireAsync(
        DispatchEnvelope envelope,
        CancellationToken cancellationToken);
}
