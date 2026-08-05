using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

namespace SpeechMessage.Dynamics.ControlPlane.Configuration;

/// <summary>
/// 將靜態部署 Profile 設定與目前 Active Official Worker Runtime generation 合併成一份 immutable snapshot。
/// 原始 resolver 只保存設定載入時的 generation；本 wrapper 每次解析都讀取 Manager 的 bounded key，避免
/// Worker replacement 後 Router 繼續取得舊 generation。它不擁有 Manager、Worker、Pipe、Admission 或
/// Credential，故 Dispose 由 Host／DI 的原始 owner 負責且不會重複釋放資源。
/// </summary>
public sealed class OfficialWorkerRuntimeProfileResolver : IProfileResolver
{
    private readonly IProfileResolver _configurationResolver;
    private readonly IActiveProfileGenerationResolver _activeGenerationResolver;

    /// <summary>
    /// 建立 Official Worker 專用 resolver。設定 Profile 的 ConnectorKind／CE version 仍是唯一部署權威；
    /// Active Runtime 只提供目前 generation 與同一個 canonical Organization 身分，不可藉此改變 connector、
    /// endpoint 或 credential reference。
    /// </summary>
    /// <param name="configurationResolver">已完成設定與 CE/Connector 相容性驗證的來源 resolver。</param>
    /// <param name="activeGenerationResolver">只回傳目前 Active generation key 的 Manager seam。</param>
    /// <exception cref="ArgumentNullException">任一 resolver 為 null。</exception>
    public OfficialWorkerRuntimeProfileResolver(
        IProfileResolver configurationResolver,
        IActiveProfileGenerationResolver activeGenerationResolver)
    {
        _configurationResolver = configurationResolver ?? throw new ArgumentNullException(nameof(configurationResolver));
        _activeGenerationResolver = activeGenerationResolver ?? throw new ArgumentNullException(nameof(activeGenerationResolver));
    }

    /// <summary>
    /// 先解析靜態設定，再以 Active Runtime key 更新 generation。Data8、未知 ConnectorKind、未 Ready、
    /// CE 版本不一致、Organization 身分不一致或 Runtime Alias 漂移全部固定失敗；不建立 Pool、Lease、
    /// Worker、Pipe、Credential 或 outbound work，也不退回任何其他 Connector。
    /// </summary>
    /// <param name="profileAlias">已由 server-side authorization 導出的 Profile Alias。</param>
    /// <param name="profile">成功時回傳含目前 generation 的 immutable Profile。</param>
    /// <param name="error">失敗時回傳不包含 endpoint、Organization GUID 或 credential 的固定分類。</param>
    /// <returns>是否可安全取得目前 Active Official Worker generation。</returns>
    public bool TryResolve(string profileAlias, out ResolvedProfile? profile, out string error)
    {
        profile = null;
        error = "profile.not-found";
        if (!_configurationResolver.TryResolve(profileAlias, out var configured, out var configurationError) ||
            configured is null)
        {
            error = NormalizeConfigurationError(configurationError);
            return false;
        }

        if (configured.ConnectorKind is not (ConnectorKind.OfficialCrm82Worker or ConnectorKind.OfficialCrm91Worker))
        {
            error = "profile.connector-not-official-worker";
            return false;
        }

        if (!_activeGenerationResolver.TryGetActiveRuntimeKey(configured.ProfileAlias, out var activeKey))
        {
            error = "profile.runtime-not-ready";
            return false;
        }

        if (!string.Equals(activeKey.ProfileAlias, configured.ProfileAlias, StringComparison.OrdinalIgnoreCase) ||
            activeKey.CanonicalOrganizationKey.ExpectedOrganizationId != configured.OrganizationId)
        {
            error = "profile.runtime-identity-mismatch";
            return false;
        }

        var expectedCeVersion = configured.CeVersion switch
        {
            CeVersion.Ce82 => "8.2",
            CeVersion.Ce91 => "9.1",
            _ => string.Empty
        };
        if (!string.Equals(activeKey.CeVersion, expectedCeVersion, StringComparison.Ordinal))
        {
            error = "profile.runtime-version-incompatible";
            return false;
        }

        profile = configured with { GenerationId = activeKey.Generation };
        error = string.Empty;
        return true;
    }

    /// <summary>把來源 resolver 的錯誤限制成固定安全分類，避免設定細節穿越 Profile boundary。</summary>
    private static string NormalizeConfigurationError(string? error)
        => error switch
        {
            "organization.disabled" => "organization.disabled",
            "profile.connector-incompatible" => "profile.connector-incompatible",
            "credential.unresolvable" => "credential.unresolvable",
            "profile.invalid-policy" => "profile.invalid-policy",
            _ => "profile.not-found"
        };
}
