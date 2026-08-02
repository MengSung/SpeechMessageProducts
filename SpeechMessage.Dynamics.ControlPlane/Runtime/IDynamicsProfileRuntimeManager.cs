// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Runtime/IDynamicsProfileRuntimeManager.cs
// 目的：定義 Local／Central Gateway 共用的多 Profile Runtime Catalog、原子替換與確定性關閉契約。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;

namespace SpeechMessage.Dynamics.ControlPlane.Runtime;

/// <summary>
/// 單一 Runtime Generation 的非秘密診斷快照。
/// 快照只包含不可變 Key、生命週期狀態、執行引用數與 bounded Admission 指標，
/// 不包含 Client、Handler、Token、Credential、Request、User、LINE ID、JWT 或 Session 強引用。
/// </summary>
public sealed class DynamicsProfileRuntimeSnapshot
{
    /// <summary>取得此快照對應的不可變 Runtime Generation Key。</summary>
    public required ProfileRuntimeKey Key { get; init; }

    /// <summary>取得 Active、Draining 或 Disposed 的單向生命週期狀態。</summary>
    public required DynamicsProfileRuntimeState State { get; init; }

    /// <summary>取得尚未釋放的 Execution Lease 數量。</summary>
    public required int ActiveExecutionCount { get; init; }

    /// <summary>取得不含秘密或呼叫者資料的 Admission 即時快照。</summary>
    public required AdmissionMetricsSnapshot Admission { get; init; }
}

/// <summary>
/// Multi-Profile Runtime Manager 的 bounded 診斷快照。
/// Catalog 關閉後 <see cref="Profiles"/> 必須為空，藉此證明 Manager 不再保留 Active／Draining Runtime 強引用。
/// </summary>
public sealed class DynamicsProfileRuntimeManagerSnapshot
{
    /// <summary>取得 Manager 是否已完成所有初始 Generation 發布且仍接受新路由。</summary>
    public required bool IsReady { get; init; }

    /// <summary>取得目前仍由 Manager 擁有的 Active／Draining Runtime 快照。</summary>
    public required IReadOnlyList<DynamicsProfileRuntimeSnapshot> Profiles { get; init; }
}

/// <summary>
/// Local Gateway 與 Central Gateway 共用的 Multi-Profile Runtime Manager 契約。
/// Manager 擁有 Alias Catalog 與 Generation 發布順序；每個 Runtime 擁有自己的 Client／Token／Handler，
/// 相同實體 Organization 只透過 Admission Registry 共用容量，不共用可變連線或身分狀態。
/// </summary>
public interface IDynamicsProfileRuntimeManager :
    IDynamicsOperationExecutor,
    IProfileExecutionLeaseProvider,
    IAsyncDisposable,
    IDisposable
{
    /// <summary>
    /// 建立並驗證所有初始 Runtime Generation，在全部成功前保持 NotReady。
    /// 任一 Factory／Warm-up 失敗時必須回收本次已建立的所有 Runtime，不能發布半套 Catalog。
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 為既有 Alias 建立一個新 Generation，完成可選 Warm-up 後原子發布，接著 drain 舊 Generation。
    /// 同一 Alias 同時最多只能有一個 Active 與一個 Draining；第三個替換必須在呼叫 Factory 前被拒絕。
    /// </summary>
    Task ReplaceAsync(
        DynamicsProfileDefinition definition,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得不含秘密與可變 Runtime 物件的 bounded 快照，供 readiness、測試與生命週期觀測使用。
    /// </summary>
    DynamicsProfileRuntimeManagerSnapshot GetSnapshot();
}
