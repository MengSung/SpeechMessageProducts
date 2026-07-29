using System.Collections.Frozen;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Gateway.Security;

/// <summary>
/// 從 deployment-owned configuration 建立 fail-closed Gateway operation authorizer。
/// Constructor 在 Host 接收流量前一次性驗證 binding：拒絕 wildcard、前後空白、重複 principal／SID／白名單值、
/// 未知 Profile Alias 與未知 Operation ID，然後發布 frozen dictionaries；因此 request 熱路徑只做有限次 O(1) 唯讀查找，
/// 不重新掃描 configuration、不配置 mutable cache、不加鎖，也不接觸 SQL、CRM、Token、Credential 或網路。
/// Singleton service 是所有 binding collection 的唯一 owner；集合只含非秘密字串且沒有 disposable resource、timer、subscription、
/// cancellation registration 或背景 Task，Host disposal 不需額外 cleanup。任何設定歧義都在 Constructor 拋出
/// <see cref="InvalidOperationException"/> 阻止啟動，不能用 last-write-wins 或 request-time fallback 掩蓋。
/// </summary>
public sealed class ConfigurationGatewayOperationAuthorizer : IGatewayOperationAuthorizer
{
    private const int MaximumPrincipalLength = 256;
    private const int MaximumWorkloadSubjectLength = 128;
    private const string WindowsSidPattern = "^S-[0-9]+-[0-9]+(?:-[0-9]+)+$";

    private readonly FrozenDictionary<string, GatewayWorkloadBinding> _bindingsByWindowsSid;
    private readonly FrozenDictionary<string, GatewayWorkloadBinding> _bindingsByPrincipalName;
    private readonly FrozenDictionary<string, string> _canonicalProfileAliases;
    private readonly FrozenDictionary<string, string> _canonicalOperationIds;

    /// <summary>
    /// 建立 configuration-backed singleton authorizer。Profile Alias 由已通過 <c>DynamicsProfileDefinition</c>
    /// 驗證的 catalog 提供，Operation ID 由 <see cref="Package01OperationRegistry"/> 提供；configuration 只能縮小這兩個集合，
    /// 不能創造新 alias／operation。所有 enumerable 都會立即 materialize，之後不保留 configuration child section、
    /// profile collection 或 registry collection 的可變 reference，避免 reload 或並行 request 改寫授權語意。
    /// </summary>
    /// <param name="configuration">Host 啟動時的 deployment configuration snapshot。</param>
    /// <param name="profileAliases">目前 Host 已驗證並準備註冊的 server-owned Profile Alias。</param>
    public ConfigurationGatewayOperationAuthorizer(
        IConfiguration configuration,
        IEnumerable<string> profileAliases)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(profileAliases);

        _canonicalProfileAliases = BuildCanonicalLookup(
            profileAliases,
            "profile alias");
        _canonicalOperationIds = BuildCanonicalLookup(
            Package01OperationRegistry.All.Select(static definition =>
                definition.CapabilityOperationId),
            "operation ID");

        var bindingsByWindowsSid = new Dictionary<string, GatewayWorkloadBinding>(
            StringComparer.OrdinalIgnoreCase);
        var bindingsByPrincipalName = new Dictionary<string, GatewayWorkloadBinding>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var bindingSection in configuration
            .GetSection("DynamicsGateway:WorkloadBindings")
            .GetChildren())
        {
            var windowsSid = ReadOptionalExactValue(
                bindingSection["WindowsSid"],
                $"binding '{bindingSection.Key}' Windows SID",
                MaximumPrincipalLength);
            if (windowsSid is not null &&
                !Regex.IsMatch(
                    windowsSid,
                    WindowsSidPattern,
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100)))
            {
                throw new InvalidOperationException(
                    $"binding '{bindingSection.Key}' contains an invalid Windows SID.");
            }

            var principalName = ReadOptionalExactValue(
                bindingSection["PrincipalName"],
                $"binding '{bindingSection.Key}' principal name",
                MaximumPrincipalLength);
            if (windowsSid is null && principalName is null)
            {
                throw new InvalidOperationException(
                    $"binding '{bindingSection.Key}' requires WindowsSid or PrincipalName.");
            }

            var workloadSubjectId = ReadRequiredExactValue(
                bindingSection["WorkloadSubjectId"],
                $"binding '{bindingSection.Key}' workload subject",
                MaximumWorkloadSubjectLength);
            var allowedAliases = ReadCanonicalList(
                bindingSection.GetSection("ProfileAliases"),
                _canonicalProfileAliases,
                $"binding '{bindingSection.Key}' profile alias");
            var allowedOperationIds = ReadCanonicalList(
                bindingSection.GetSection("CapabilityOperationIds"),
                _canonicalOperationIds,
                $"binding '{bindingSection.Key}' operation");

            var binding = new GatewayWorkloadBinding(
                windowsSid,
                principalName,
                workloadSubjectId,
                allowedAliases,
                allowedOperationIds);

            // 必須在發布 frozen snapshot 前同時檢查 SID 與 name；若任一索引採 last-write-wins，
            // configuration provider 的列舉順序就會決定權限，造成不可審查且可能跨 workload 的身分混用。
            if (windowsSid is not null &&
                !bindingsByWindowsSid.TryAdd(windowsSid, binding))
            {
                throw new InvalidOperationException(
                    $"duplicate Windows SID binding '{windowsSid}' is not allowed.");
            }

            if (principalName is not null &&
                !bindingsByPrincipalName.TryAdd(principalName, binding))
            {
                throw new InvalidOperationException(
                    $"duplicate principal binding '{principalName}' is not allowed.");
            }
        }

        _bindingsByWindowsSid = bindingsByWindowsSid.ToFrozenDictionary(
            StringComparer.OrdinalIgnoreCase);
        _bindingsByPrincipalName = bindingsByPrincipalName.ToFrozenDictionary(
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 依 authentication→principal binding→alias→operation 的固定順序授權 request。
    /// Windows SID mapping 優先於 principal name，可抵抗帳號顯示名稱變更；只有沒有 SID mapping 時才以完整 Name fallback。
    /// 成功前不建立 <c>OperationExecutionRequest</c>、不取得 admission permit、不解析 secret 且不做 outbound I/O；
    /// 失敗一律回傳不含 route 值的 denied result，使 caller 無法利用差異回應列舉有效權限。
    /// </summary>
    public GatewayOperationAuthorization Authorize(
        ClaimsPrincipal principal,
        string profileAlias,
        string capabilityOperationId)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity?.IsAuthenticated != true)
        {
            return GatewayOperationAuthorization.Denied("not-authenticated");
        }

        GatewayWorkloadBinding? binding = null;
        var windowsSid = TryGetAuthenticatedWindowsSid(principal);
        if (windowsSid is not null)
        {
            _bindingsByWindowsSid.TryGetValue(windowsSid, out binding);
        }

        if (binding is null && !string.IsNullOrWhiteSpace(principal.Identity.Name))
        {
            _bindingsByPrincipalName.TryGetValue(principal.Identity.Name, out binding);
        }

        if (binding is null)
        {
            return GatewayOperationAuthorization.Denied("unmapped-principal");
        }

        if (string.IsNullOrWhiteSpace(profileAlias) ||
            !_canonicalProfileAliases.TryGetValue(profileAlias, out var canonicalProfileAlias) ||
            !binding.AllowsProfileAlias(canonicalProfileAlias))
        {
            return GatewayOperationAuthorization.Denied("profile-not-authorized");
        }

        if (string.IsNullOrWhiteSpace(capabilityOperationId) ||
            !_canonicalOperationIds.TryGetValue(
                capabilityOperationId,
                out var canonicalOperationId) ||
            !binding.AllowsCapabilityOperation(canonicalOperationId))
        {
            return GatewayOperationAuthorization.Denied("operation-not-authorized");
        }

        return GatewayOperationAuthorization.Success(
            binding.WorkloadSubjectId,
            canonicalProfileAlias,
            canonicalOperationId);
    }

    /// <summary>
    /// 建立不分大小寫查找、但保留 registry/catalog 原始拼法的 canonical lookup。
    /// 重複值代表上游 catalog 已有歧義，必須在 singleton 發布前停止啟動；回傳 frozen dictionary 後可供所有 request 無鎖共享。
    /// </summary>
    private static FrozenDictionary<string, string> BuildCanonicalLookup(
        IEnumerable<string> values,
        string valueKind)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var exactValue = ReadRequiredExactValue(value, valueKind, MaximumPrincipalLength);
            RejectWildcard(exactValue, valueKind);
            if (!lookup.TryAdd(exactValue, exactValue))
            {
                throw new InvalidOperationException(
                    $"duplicate {valueKind} '{exactValue}' is not allowed.");
            }
        }

        if (lookup.Count == 0)
        {
            throw new InvalidOperationException(
                $"At least one canonical {valueKind} is required.");
        }

        return lookup.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 讀取一個 binding 白名單並立即轉成 server canonical 值。
    /// 空集合、wildcard、重複值與 unknown value 都阻止 Host 啟動，避免未來 registry 擴張後讓舊錯字自動取得新權限。
    /// </summary>
    private static IReadOnlyList<string> ReadCanonicalList(
        IConfigurationSection section,
        IReadOnlyDictionary<string, string> canonicalValues,
        string valueKind)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetChildren())
        {
            var requestedValue = ReadRequiredExactValue(
                child.Value,
                valueKind,
                MaximumPrincipalLength);
            RejectWildcard(requestedValue, valueKind);
            if (!seen.Add(requestedValue))
            {
                throw new InvalidOperationException(
                    $"duplicate {valueKind} '{requestedValue}' is not allowed.");
            }

            if (!canonicalValues.TryGetValue(requestedValue, out var canonicalValue))
            {
                throw new InvalidOperationException(
                    $"unknown {valueKind} '{requestedValue}' is not allowed.");
            }

            values.Add(canonicalValue);
        }

        if (values.Count == 0)
        {
            throw new InvalidOperationException(
                $"{valueKind} list must contain at least one value.");
        }

        return values;
    }

    /// <summary>
    /// 讀取必要的 bounded exact token；前後空白不會被靜默 trim，因為不同 provider 對 key/value 正規化的差異
    /// 可能讓部署者看到的設定與實際授權不一致。此方法不記錄原始值，避免 principal 或 workload identifier 被例外之外的狀態保留。
    /// </summary>
    private static string ReadRequiredExactValue(
        string? value,
        string valueKind,
        int maximumLength)
    {
        var exactValue = ReadOptionalExactValue(value, valueKind, maximumLength);
        return exactValue ?? throw new InvalidOperationException(
            $"{valueKind} is required.");
    }

    /// <summary>
    /// 讀取可選的 bounded exact token；空白值視為未設定，但非空值必須已是 trim 後形狀且不得超長。
    /// 驗證只做常數時間邊界檢查，不配置長生命週期 buffer 或 cache。
    /// </summary>
    private static string? ReadOptionalExactValue(
        string? value,
        string valueKind,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{valueKind} must not contain leading or trailing whitespace.");
        }

        if (value.Length > maximumLength)
        {
            throw new InvalidOperationException(
                $"{valueKind} exceeds the maximum length of {maximumLength}.");
        }

        RejectWildcard(value, valueKind);
        return value;
    }

    /// <summary>
    /// 拒絕 shell／glob 常見的 <c>*</c> 與 <c>?</c> wildcard；授權只允許完整值 equality，
    /// 即使目前 lookup 不解讀 pattern 也要在啟動拒絕，避免未來重構成 pattern matcher 時擴大既有 binding。
    /// </summary>
    private static void RejectWildcard(string value, string valueKind)
    {
        if (value.Contains('*') || value.Contains('?'))
        {
            throw new InvalidOperationException(
                $"wildcard {valueKind} '{value}' is not allowed.");
        }
    }

    /// <summary>
    /// 從 authentication middleware 已驗證的 principal 取得 Windows SID。
    /// 真實 <see cref="WindowsIdentity.User"/> 優先；測試或其他 Windows handler 可提供標準 PrimarySid／Sid claim。
    /// 無 SID 時回傳 null 讓 caller 使用完整 principal name fallback；方法不快取 identity 或 claim collection，避免跨 request 保留身分狀態。
    /// </summary>
    private static string? TryGetAuthenticatedWindowsSid(ClaimsPrincipal principal)
    {
        if (OperatingSystem.IsWindows() &&
            principal.Identity is WindowsIdentity windowsIdentity &&
            windowsIdentity.User is not null)
        {
            return windowsIdentity.User.Value;
        }

        var sidClaim = principal.FindFirst(ClaimTypes.PrimarySid)
            ?? principal.FindFirst(ClaimTypes.Sid);
        if (sidClaim is null || string.IsNullOrWhiteSpace(sidClaim.Value))
        {
            return null;
        }

        var sid = sidClaim.Value.Trim();
        return Regex.IsMatch(
            sid,
            WindowsSidPattern,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100))
            ? sid
            : null;
    }
}

/// <summary>
/// 在 ASP.NET listener 接受流量前強制 materialize <see cref="IGatewayOperationAuthorizer"/> 的啟動驗證器。
/// Minimal hosting 的測試／部署 configuration provider 可能在 service registration 後才完成合併，因此 authorizer 由 DI factory
/// 讀取最終 <see cref="IConfiguration"/>；本 hosted service 則把原本可能延遲到第一個 request 的 wildcard、重複或未知 binding
/// 錯誤提前為 Host startup failure。Instance 只持有 singleton authorizer reference，不建立 Task、timer、subscription、socket、
/// cancellation registration 或其他資源，停止時沒有 cleanup；唯一目的就是維持「驗證完成後才開始接流量」的安全順序。
/// </summary>
internal sealed class GatewayOperationAuthorizationStartupValidator : IHostedService
{
    private readonly IGatewayOperationAuthorizer _authorizer;

    /// <summary>
    /// 透過 constructor injection 強制建立並完整驗證 singleton authorizer；若設定無效，DI resolution 立即拋出並中止 Host。
    /// </summary>
    public GatewayOperationAuthorizationStartupValidator(IGatewayOperationAuthorizer authorizer)
    {
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
    }

    /// <summary>
    /// Authorizer 已在 constructor 完成所有同步驗證，因此啟動立即完成；讀取欄位避免 validator 被誤改成不 materialize dependency 的空殼。
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _authorizer;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Authorizer 只擁有 immutable 字串集合，沒有非同步工作或 disposable resource；Host 停止時不需額外 cleanup。
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
