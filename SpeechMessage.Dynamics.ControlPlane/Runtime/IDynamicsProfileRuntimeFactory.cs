// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Runtime/IDynamicsProfileRuntimeFactory.cs
// 目的：定義從不可變 Profile Definition 建立一個隔離 Runtime Generation 的工廠契約。
// ============================================================================

namespace SpeechMessage.Dynamics.ControlPlane.Runtime;

/// <summary>
/// 建立 Profile-isolated Runtime Generation 的工廠。
/// 每次呼叫都必須建立新的 Client、Transport、Token Provider、Handler 與 Cancellation State；
/// 只有 Canonical Organization Admission Registration 可以透過 Registry 共用。
/// 若任一步驟失敗，Factory 必須依 ownership 反向順序清理已建立資源，再將原始例外傳回，不能留下半成品或 Host Slot 引用。
/// </summary>
public interface IDynamicsProfileRuntimeFactory
{
    /// <summary>
    /// 建立一個尚未由 Manager 發布、但已完成設定與 Admission Plan 驗證的 Runtime Generation。
    /// 方法不自動執行 Warm-up；Manager 會在發布前依 Definition 決定是否呼叫 WarmUpAsync。
    /// </summary>
    /// <param name="definition">由部署設定建立、可重複產生深複本的不可變 Profile Definition。</param>
    /// <param name="generation">同一 Alias 內單調遞增且大於零的 Generation 編號。</param>
    /// <param name="cancellationToken">取消尚未發布之建構流程；取消後必須 rollback 所有部分資源。</param>
    Task<IDynamicsProfileRuntime> CreateAsync(
        DynamicsProfileDefinition definition,
        long generation,
        CancellationToken cancellationToken);
}
