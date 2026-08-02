using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 集中定義官方 CRM Worker 可執行的 operation allowlist、revision 綁定與 operation-specific
/// request/result 邊界。此 static registry 只保存不可變字串與 protocol 規則，不保存 Profile、
/// Credential、Token、Session、CRM client、buffer 或其他跨要求狀態。
/// </summary>
public static class OfficialWorkerOperations
{
    /// <summary>官方 SDK identity probe 的 capability ID。</summary>
    public const string RuntimeHealthWhoAmI = "runtime.health.whoami";

    /// <summary>WhoAmI 與 Abstractions registry 完全一致的固定 revision。</summary>
    public const string RuntimeHealthWhoAmIRevision =
        "9de3542216ab579be01d2a47599642a0a218576fec94de24043b2c2bd0bed427";

    /// <summary>官方 connection validation 的 capability ID。</summary>
    public const string RuntimePoolValidateConnection =
        "runtime.pool.validate.connection";

    /// <summary>Connection validation 與 Abstractions registry 完全一致的固定 revision。</summary>
    public const string RuntimePoolValidateConnectionRevision =
        "f795e1995cb30b28018411affd11f43fe84e3caf1515b5735dc413eb5ecdf40f";

    /// <summary>
    /// 建立本 executable generation 的 ordinal revision map。
    /// 每次呼叫都回傳新的唯讀 dictionary，避免 caller 修改 process-wide allowlist；內容不含
    /// Profile、Credential、Token、Session 或 endpoint，生命週期由單次 composition 擁有。
    /// </summary>
    /// <returns>operation ID 到 immutable revision 的 ordinal 唯讀對照。</returns>
    public static IReadOnlyDictionary<string, string> CreateRevisionMap()
    {
        var revisions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeHealthWhoAmI] = RuntimeHealthWhoAmIRevision,
            [RuntimePoolValidateConnection] = RuntimePoolValidateConnectionRevision,
            [Package01FeeWorkerContract.CapabilityOperationId] =
                Package01FeeWorkerContract.OperationDefinitionRevision
        };

        return new ReadOnlyDictionary<string, string>(revisions);
    }

    /// <summary>
    /// 在 CRM client 執行前完成 operation-specific request 驗證與正規化。
    /// Package01 contactName 只在此邊界驗證後丟棄；identity operation 則要求精確 parameter shape。
    /// 不合法輸入以 protocol failure 結束 Worker session，且不會建立 SDK query 或外部連線狀態。
    /// </summary>
    /// <param name="request">已通過 nonce、deadline、allowlist 與 revision 檢查的 request。</param>
    /// <returns>可安全交給單一 Worker client 的不可變 request snapshot。</returns>
    public static WorkerRequestV1 PrepareRequestForExecution(WorkerRequestV1 request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.Equals(
                request.CapabilityOperationId,
                Package01FeeWorkerContract.CapabilityOperationId,
                StringComparison.Ordinal))
        {
            return Package01FeeWorkerContract.ValidateAndNormalizeRequest(request);
        }

        if (IsSupportedIdentityRequest(request))
        {
            return request;
        }

        throw new WorkerProtocolException(
            WorkerProtocolFailureCategory.InvalidEnvelope,
            "The official Dynamics worker request shape is invalid.");
    }

    /// <summary>
    /// 在 success response serialization 前驗證 operation-specific result contract。
    /// Package01 malformed shape 保留為 protocol failure；任何數量或 canonical byte overflow 轉成
    /// 固定 <see cref="OfficialWorkerResultLimitExceededException"/>，讓 Session 回傳 sanitized
    /// result-too-large，而不是截斷或序列化部分資料。其他 operation 仍由一般 envelope codec 驗證。
    /// </summary>
    /// <param name="capabilityOperationId">已綁定 revision 的 operation ID。</param>
    /// <param name="result">CRM adapter 完整投影出的 SDK-free result。</param>
    public static void ValidateResult(
        string capabilityOperationId,
        WorkerValue result)
    {
        if (capabilityOperationId is null)
        {
            throw new ArgumentNullException(nameof(capabilityOperationId));
        }

        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (!string.Equals(
                capabilityOperationId,
                Package01FeeWorkerContract.CapabilityOperationId,
                StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            Package01FeeWorkerContract.ValidateResult(result);
        }
        catch (WorkerProtocolException exception)
            when (exception.Category == WorkerProtocolFailureCategory.EnvelopeLimitExceeded)
        {
            // 不保留原始 exception，避免 page/row/payload 細節經 inner exception 跨越 IPC；
            // 固定型別是 Session 唯一允許映射為 result-too-large 的訊號。
            throw new OfficialWorkerResultLimitExceededException();
        }
    }

    /// <summary>
    /// 判斷 request 是否為精確支援的 identity operation 與 parameter shape。
    /// 方法只讀取 bounded request snapshot，不建立或保存任何 client、credential 或 Session state。
    /// </summary>
    /// <param name="request">待檢查的 Worker request。</param>
    /// <returns>operation ID 與 parameters 均符合固定 identity contract 時為 true。</returns>
    public static bool IsSupportedIdentityRequest(WorkerRequestV1 request)
    {
        return request is not null && IsSupportedIdentityOperation(
            request.CapabilityOperationId,
            request.Parameters);
    }

    /// <summary>
    /// 驗證 identity operation ID 與 bounded typed parameters；revision 由 Session 的 revision map
    /// 另行綁定。logicalProfileId 只供 connection validation 比對，不會成為 credential 或 Session key。
    /// </summary>
    /// <param name="capabilityOperationId">待驗證的 capability ID。</param>
    /// <param name="parameters">不可變 typed parameter snapshot。</param>
    /// <returns>parameter count、kind、長度與空白規則均符合時為 true。</returns>
    public static bool IsSupportedIdentityOperation(
        string capabilityOperationId,
        IReadOnlyDictionary<string, WorkerValue> parameters)
    {
        if (parameters is null ||
            !IsSupportedIdentityParameterCount(capabilityOperationId, parameters.Count))
        {
            return false;
        }

        if (string.Equals(
                capabilityOperationId,
                RuntimeHealthWhoAmI,
                StringComparison.Ordinal))
        {
            return parameters.Count == 0;
        }

        if (!string.Equals(
                capabilityOperationId,
                RuntimePoolValidateConnection,
                StringComparison.Ordinal) ||
            !parameters.TryGetValue("logicalProfileId", out var logicalProfileId) ||
            logicalProfileId.Kind != WorkerValueKind.String ||
            logicalProfileId.Scalar is not { Length: > 0 } value)
        {
            return false;
        }

        return value.Length <= 128 &&
            string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 以不配置集合的快速路徑檢查 identity operation 的精確 parameter count。
    /// 此 gate 供 ControlPlane preparer 與 Worker 共用，避免未知欄位先進入 SDK adapter。
    /// </summary>
    /// <param name="capabilityOperationId">待檢查的 capability ID。</param>
    /// <param name="parameterCount">caller collection 已受 bounded member limit 約束的數量。</param>
    /// <returns>WhoAmI 為零欄、connection validation 為一欄時為 true。</returns>
    public static bool IsSupportedIdentityParameterCount(
        string capabilityOperationId,
        int parameterCount)
    {
        return string.Equals(
                   capabilityOperationId,
                   RuntimeHealthWhoAmI,
                   StringComparison.Ordinal)
               ? parameterCount == 0
               : string.Equals(
                       capabilityOperationId,
                       RuntimePoolValidateConnection,
                       StringComparison.Ordinal) &&
                   parameterCount == 1;
    }
}
