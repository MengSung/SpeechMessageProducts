// ============================================================================
// 檔案：SpeechMessageProducts.ChurchReport/Security/MemberInfoServerAssignmentEvidenceSource.cs
// 用途：將 P7 request scope 的 subject 轉交給固定 ProductClient，並建立 MemberInfo target authorization evidence。
//
// 此檔案刻意不引用 Controller、Session、HttpContext、ClaimsPrincipal、InMemoryContext、legacy ListManager、
// CRM SDK、Entity、connector、lease、cache 或 feature gate。所有外部 I/O 與資源 cleanup 均由 ProductClient
// 下方的 executor/connector owner 管理；本 source 只在單一 await 範圍保存 immutable scalar 與 DTO。
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;

namespace ChurchReport.Security;

/// <summary>
/// 從固定、伺服器擁有的 assignment operation 建立 MemberInfo target authorization scope。
/// 唯一 identity input 是已驗證的 <see cref="P7GatewayRequestScope"/>；ProfileAlias 與 WorkloadSubjectId 在建構時由
/// deployment/service composition 固定。此類別不快取任何 evidence、request 或 response，不重試、不 fallback 至
/// legacy 資料面，且不擁有 connector/lease/stream/取消註冊，因此 A/B 使用者不會交換授權或資源狀態。
/// </summary>
internal sealed class MemberInfoServerAssignmentEvidenceSource
{
    private readonly IMemberInfoAuthorizationAssignmentReadClient _assignmentReadClient;
    private readonly string _profileAlias;
    private readonly string _workloadSubjectId;

    /// <summary>
    /// 建立使用 deployment-owned routing 的 source。
    /// routing scalar 在此複製後不再變更；真正的 byte-bound 與 profile admission 仍由 ProductClient 在每個 request
    /// 的 connector I/O 前再次驗證。建構子不會建立 client、連線、lease、cache、timer 或背景工作。
    /// </summary>
    /// <param name="assignmentReadClient">無狀態、只執行固定 subject operation 的 typed ProductClient。</param>
    /// <param name="profileAlias">由 deployment composition 選定的 profile alias。</param>
    /// <param name="workloadSubjectId">由 ChurchReport 服務固定的 workload isolation scalar。</param>
    internal MemberInfoServerAssignmentEvidenceSource(
        IMemberInfoAuthorizationAssignmentReadClient assignmentReadClient,
        string profileAlias,
        string workloadSubjectId)
    {
        _assignmentReadClient = assignmentReadClient ?? throw new ArgumentNullException(nameof(assignmentReadClient));
        if (string.IsNullOrWhiteSpace(profileAlias))
        {
            throw new ArgumentException("ProfileAlias is required.", nameof(profileAlias));
        }

        if (string.IsNullOrWhiteSpace(workloadSubjectId))
        {
            throw new ArgumentException("WorkloadSubjectId is required.", nameof(workloadSubjectId));
        }

        _profileAlias = new string(profileAlias.Trim().AsSpan());
        _workloadSubjectId = new string(workloadSubjectId.Trim().AsSpan());
    }

    /// <summary>
    /// 以目前 request scope 的 subject 取得 server assignment evidence，並交給既有 target scope resolver 作最終 admission。
    /// null scope、typed-client fault、malformed evidence、subject mismatch、未知 mode、duplicate 或 overflow 均不會
    /// 觸發 legacy fallback，而是產生去識別化 fail-closed resolution。取消例外會原樣傳播，讓外層 request 與 executor
    /// 維持既有取消／faulted-lease cleanup 順序；此方法不吞掉 cancellation 或自行重試。
    /// </summary>
    /// <param name="requestScope">唯一可作為 identity authority 的 immutable P7 scope。</param>
    /// <param name="cancellationToken">由 ASP.NET Core request 擁有並原樣傳給 ProductClient 的 token。</param>
    /// <returns>已防禦性複製的 target scope，或不洩漏 upstream detail 的 fail-closed 分類。</returns>
    internal async Task<MemberInfoTargetAuthorizationResolution> ResolveAsync(
        P7GatewayRequestScope? requestScope,
        CancellationToken cancellationToken = default)
    {
        if (requestScope is null)
        {
            return MemberInfoTargetAuthorizationScopeResolver.TryCreate(null, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var assignment = await _assignmentReadClient.ResolveBySubjectAsync(
                new MemberInfoAuthorizationAssignmentReadRequest
                {
                    ProfileAlias = _profileAlias,
                    WorkloadSubjectId = _workloadSubjectId,
                    SubjectContactId = requestScope.SubjectContactId
                },
                cancellationToken).ConfigureAwait(false);

            var evidence = TryMapEvidence(assignment);
            return MemberInfoTargetAuthorizationScopeResolver.TryCreate(requestScope, evidence);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 不發佈原始 connector/CRM 例外，亦不退回 Session/ListManager；source unavailable 是唯一安全分類。
            return MemberInfoTargetAuthorizationScopeResolver.TryCreate(requestScope, null);
        }
    }

    /// <summary>
    /// 將 typed ProductClient result 映射為 assembly-internal target evidence。
    /// 只接受兩個封閉 mode；未知 enum 或 null result 回傳 null，由 resolver 以 SourceUnavailable fail closed。
    /// Subject／list 完整性、上限、重複與 Church-wide 空集合規則會再次由既有 resolver 驗證，因此這裡不會將任何
    /// 來自上游的可變 collection、CRM entity 或授權推論直接發布給 consumer。
    /// </summary>
    /// <param name="assignment">ProductClient 於目前 request 取得的 immutable DTO。</param>
    /// <returns>可交給 resolver 的 internal evidence，或 null。</returns>
    private static MemberInfoTargetAuthorizationEvidence? TryMapEvidence(
        MemberInfoAuthorizationAssignmentReadResult? assignment)
    {
        if (assignment is null)
        {
            return null;
        }

        var accessMode = assignment.AccessMode switch
        {
            MemberInfoAuthorizationAssignmentAccessMode.ChurchWide => MemberInfoTargetAccessMode.ChurchWide,
            MemberInfoAuthorizationAssignmentAccessMode.AssignedLists => MemberInfoTargetAccessMode.AssignedLists,
            _ => (MemberInfoTargetAccessMode?)null
        };

        return accessMode.HasValue
            ? MemberInfoTargetAuthorizationEvidence.Create(
                assignment.SubjectContactId,
                accessMode.Value,
                assignment.AssignedListIds,
                assignmentEvidenceComplete: true)
            : null;
    }
}
