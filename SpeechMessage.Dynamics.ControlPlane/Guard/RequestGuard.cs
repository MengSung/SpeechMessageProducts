using System.Collections.Immutable;
using System.Text.RegularExpressions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ControlPlane.Guard;

/// <summary>
/// 在任何 Dynamics runtime 資源建立前驗證 operation request 的安全邊界。建構時複製 operation
/// 白名單為 immutable 集合，執行時只讀取 request，絕不配置或保留 Connector、Credential、
/// Session、Permit、Timer、Task 或背景工作，因此拒絕路徑具有零資源所有權。
/// </summary>
public sealed class RequestGuard : IRequestGuard
{
    private const int MaximumProfileAliasLength = 128;
    private static readonly ImmutableHashSet<string> ReservedParameterNames =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "organizationId", "connectorKind", "credential", "endpoint", "fetchXml");
    private static readonly Regex SafeAliasPattern = new(
        "^[A-Za-z0-9._-]{1,128}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly ImmutableHashSet<string> _registeredOperationIds;

    /// <summary>
    /// 建立 RequestGuard 並複製 operation allowlist。輸入 enumeration 在建構完成後不再被保留，
    /// 防止部署程式或測試後續修改可變集合而讓不同請求在同一 Session/Process 得到不同授權。
    /// </summary>
    public RequestGuard(IEnumerable<string> registeredOperationIds)
    {
        ArgumentNullException.ThrowIfNull(registeredOperationIds);
        _registeredOperationIds = registeredOperationIds
            .Where(static operationId => !string.IsNullOrWhiteSpace(operationId))
            .Select(static operationId => operationId.Trim())
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 檢查請求而不建立任何 Dynamics 資源。檢查保留參數優先於 Resolver，確保呼叫端不能利用
    /// 表面合法的 Alias 夾帶 endpoint、Credential、Connector、OrganizationId 或 FetchXML 來
    /// 改變部署端 routing；origin 目前不分支，但保留顯式參數以強制三種主機模式共用本防線。
    /// </summary>
    public RequestGuardResult Inspect(OperationExecutionRequest request, RequestOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = origin;

        if (!IsValidAlias(request.ProfileAlias))
        {
            return RequestGuardResult.Reject("request.invalid-profile-alias");
        }

        if (request.Parameters.Keys.Any(ReservedParameterNames.Contains))
        {
            return RequestGuardResult.Reject("request.reserved-parameter");
        }

        if (string.IsNullOrWhiteSpace(request.CapabilityOperationId) ||
            !_registeredOperationIds.Contains(request.CapabilityOperationId.Trim()))
        {
            return RequestGuardResult.Reject("operation.not-registered");
        }

        return RequestGuardResult.Allow();
    }

    /// <summary>確認 Alias 是可作為 immutable Profile key 的有界識別字，而非 URI 或任意 payload。</summary>
    private static bool IsValidAlias(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumProfileAliasLength &&
           string.Equals(value, value.Trim(), StringComparison.Ordinal) && SafeAliasPattern.IsMatch(value);
}
