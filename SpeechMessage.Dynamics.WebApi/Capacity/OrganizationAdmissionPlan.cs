// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionPlan.cs
// 目的：把原始 admission 設定驗證成不可變的執行計畫。
//
// 保母教學：
// - 驗證失敗要 fail-closed，不要用「猜一個預設值」硬跑。
// - LocalMaxInFlight 在這裡算出來，之後所有 semaphore 都用它。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.WebApi.Capacity;

/// <summary>
/// 已驗證的 admission 計畫。
/// </summary>
public sealed class OrganizationAdmissionPlan
{
    public required CanonicalOrganizationCapacityKey CanonicalKey { get; init; }
    public required OrganizationAdmissionKey AdmissionKey { get; init; }
    public required RuntimeHostSlotLeaseNamespace LeaseNamespace { get; init; }
    public required int AggregateMaxInFlight { get; init; }
    public required int MaximumRuntimeHosts { get; init; }
    public required int LocalMaxInFlight { get; init; }
    public required int LocalQueueCapacity { get; init; }
    public required int MaxDispatchEnvelopeBytes { get; init; }
    public required int QueueAdmissionTimeoutSeconds { get; init; }
    public required int MaxInFlightAndQueuedPerWorkload { get; init; }
    public required int MaxConnectionsPerServer { get; init; }
    public required TimeSpan RuntimeHostSlotLeaseTtl { get; init; }
    public required TimeSpan RuntimeHostSlotRenewalInterval { get; init; }
    public required TimeSpan RuntimeHostSlotExpiryFence { get; init; }
    public required TimeSpan MaximumOutboundWorkLifetime { get; init; }
    public required TimeSpan ShutdownDrainTimeout { get; init; }
    public required bool RequireDurableHostCoordinator { get; init; }

    public static bool TryCreate(
        DynamicsWebApiOptions webApiOptions,
        OrganizationAdmissionOptions admissionOptions,
        out OrganizationAdmissionPlan? plan,
        out OperationExecutionResult? error)
    {
        plan = null;
        error = null;

        if (webApiOptions is null || admissionOptions is null)
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "Admission options are missing.");
            return false;
        }

        if (!ApprovedWebApiRootFactory.TryCreate(webApiOptions, out var root, out var rootError) || root is null)
        {
            error = rootError ?? OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "Approved Web API root is invalid.");
            return false;
        }

        if (admissionOptions.ExpectedOrganizationId == Guid.Empty)
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "ExpectedOrganizationId is required.");
            return false;
        }

        if (admissionOptions.AggregateMaxInFlight < admissionOptions.MaximumRuntimeHosts ||
            admissionOptions.MaximumRuntimeHosts < 1)
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "AggregateMaxInFlight must be >= MaximumRuntimeHosts >= 1.");
            return false;
        }

        var localMax = admissionOptions.AggregateMaxInFlight / admissionOptions.MaximumRuntimeHosts;
        if (localMax < 1)
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "Derived LocalMaxInFlight must be >= 1.");
            return false;
        }

        if (webApiOptions.MaxConnectionsPerServer < 1 ||
            webApiOptions.MaxConnectionsPerServer > localMax)
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                $"MaxConnectionsPerServer must be between 1 and LocalMaxInFlight ({localMax}).");
            return false;
        }

        if (admissionOptions.LocalQueueCapacity < 0)
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "LocalQueueCapacity must be >= 0.");
            return false;
        }

        if (admissionOptions.MaxDispatchEnvelopeBytes < 256)
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "MaxDispatchEnvelopeBytes is too small.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(admissionOptions.AdmissionNamespaceId) ||
            string.IsNullOrWhiteSpace(admissionOptions.LeaseNamespaceId))
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "AdmissionNamespaceId and LeaseNamespaceId are required.");
            return false;
        }

        var leaseTtl = TimeSpan.FromSeconds(admissionOptions.RuntimeHostSlotLeaseTtlSeconds);
        var renewalInterval = TimeSpan.FromSeconds(admissionOptions.RuntimeHostSlotRenewalIntervalSeconds);
        var expiryFence = TimeSpan.FromSeconds(admissionOptions.RuntimeHostSlotExpiryFenceSeconds);
        var maximumWorkLifetime = TimeSpan.FromSeconds(admissionOptions.MaximumOutboundWorkLifetimeSeconds);
        var shutdownDrainTimeout = TimeSpan.FromSeconds(admissionOptions.ShutdownDrainTimeoutSeconds);
        if (leaseTtl <= renewalInterval + expiryFence + maximumWorkLifetime)
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "RuntimeHostSlotLeaseTtlSeconds must exceed renewal interval plus maximum outbound work lifetime and expiry fence.");
            return false;
        }

        if (shutdownDrainTimeout < maximumWorkLifetime)
        {
            error = OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidConfiguration,
                "ShutdownDrainTimeoutSeconds must be at least MaximumOutboundWorkLifetimeSeconds.");
            return false;
        }

        // 用 OrganizationBaseUri 或 WebApi root 的 origin+base path 正規化。
        var normalizedBase = root.Value.GetLeftPart(UriPartial.Authority)
            + root.Value.AbsolutePath.Replace($"/api/data/{root.ApiVersionSegment}/", "/", StringComparison.OrdinalIgnoreCase);
        if (!normalizedBase.EndsWith('/'))
        {
            normalizedBase += "/";
        }

        plan = new OrganizationAdmissionPlan
        {
            CanonicalKey = new CanonicalOrganizationCapacityKey(
                admissionOptions.ExpectedOrganizationId,
                normalizedBase.ToLowerInvariant()),
            AdmissionKey = new OrganizationAdmissionKey(admissionOptions.AdmissionNamespaceId.Trim()),
            LeaseNamespace = new RuntimeHostSlotLeaseNamespace(admissionOptions.LeaseNamespaceId.Trim()),
            AggregateMaxInFlight = admissionOptions.AggregateMaxInFlight,
            MaximumRuntimeHosts = admissionOptions.MaximumRuntimeHosts,
            LocalMaxInFlight = localMax,
            LocalQueueCapacity = admissionOptions.LocalQueueCapacity,
            MaxDispatchEnvelopeBytes = admissionOptions.MaxDispatchEnvelopeBytes,
            QueueAdmissionTimeoutSeconds = admissionOptions.QueueAdmissionTimeoutSeconds,
            MaxInFlightAndQueuedPerWorkload = Math.Max(1, admissionOptions.MaxInFlightAndQueuedPerWorkload),
            MaxConnectionsPerServer = webApiOptions.MaxConnectionsPerServer,
            RuntimeHostSlotLeaseTtl = leaseTtl,
            RuntimeHostSlotRenewalInterval = renewalInterval,
            RuntimeHostSlotExpiryFence = expiryFence,
            MaximumOutboundWorkLifetime = maximumWorkLifetime,
            ShutdownDrainTimeout = shutdownDrainTimeout,
            RequireDurableHostCoordinator = admissionOptions.RequireDurableHostCoordinator
        };
        return true;
    }
}
