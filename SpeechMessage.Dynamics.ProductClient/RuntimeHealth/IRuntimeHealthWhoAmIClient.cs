// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/RuntimeHealth/IRuntimeHealthWhoAmIClient.cs
// 目的：定義 ORG-CALL-00003 runtime.health.whoami 的唯一產品端健康身分讀取契約。
//
// 安全與生命週期邊界：
// - 此介面只接受 deployment-owned profile alias 與 server-owned workload subject；兩者不得由 browser、
//   Session、cookie、route、query、body 或呼叫端 CRM locator 作為 routing authority。
// - 回傳值僅含三個 immutable GUID scalar，不含 CRM SDK、HTTP、endpoint、credential、token、connector、
//   raw exception 或 transport response。
// - 實作不擁有 connector、lease、permit、stream、timer、subscription 或 background work；注入 executor 是
//   唯一 transport/cleanup owner，取消必須原樣向下傳遞。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.RuntimeHealth;

/// <summary>
/// 提供固定 <c>runtime.health.whoami</c> capability 的受控 runtime health 身分投影。
/// 這不是通用 CRM identity query，也不接受 caller 選擇 operation、CE version、connector、endpoint、credential
/// 或 Organization。其用途僅是以既有 deployment-owned executor 驗證 profile runtime 的封閉 WhoAmI response；
/// 不遷移 ChurchReport consumer、不啟用 feature gate、不建立 CE evidence，也不改變 P7.5/P8 狀態。
/// </summary>
public interface IRuntimeHealthWhoAmIClient
{
    /// <summary>
    /// 執行唯一固定的 WhoAmI health operation，並回傳本次呼叫的新 immutable GUID snapshot。
    /// 實作必須在 executor I/O 前拒絕空白、無效 UTF-8 或超限的 routing scalar，固定使用零 parameters 與
    /// 零 idempotency key，並確認 operation ID、CE 9.1、response discriminator 與三個 GUID 皆完全相符。
    /// timeout、cancellation、executor failure 或任何 response mismatch 都 fail closed；不得 retry、fallback 到
    /// ToolUtility，或保存 profile/workload/response 到 singleton、cache、Session 或 background state。
    /// </summary>
    /// <param name="profileAlias">由 deployment composition 選定的 profile alias，非 caller-controlled routing 值。</param>
    /// <param name="workloadSubjectId">由 Gateway/host policy 選定的 workload subject，不是登入使用者或 Session 值。</param>
    /// <param name="cancellationToken">目前 request 的取消 token，必須不建立 linked token 而原樣向 executor 傳遞。</param>
    /// <returns>不引用 response/transport graph 的三 GUID 純值 DTO。</returns>
    Task<RuntimeHealthWhoAmIIdentityDto> CheckAsync(
        string profileAlias,
        string workloadSubjectId,
        CancellationToken cancellationToken = default);
}
