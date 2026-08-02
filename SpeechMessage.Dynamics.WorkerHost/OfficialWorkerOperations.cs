using System.Collections.Generic;
using System.Collections.ObjectModel;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 定義官方 CRM Worker 唯一允許執行的封閉操作與其精確 registry revision。
/// 此類別只保存不可變常數，不保存 Profile、Credential、Token、Session、Client 或 request 狀態。
/// </summary>
public static class OfficialWorkerOperations
{
    /// <summary>取得官方 SDK 身分驗證操作的 capability ID。</summary>
    public const string RuntimeHealthWhoAmI = "runtime.health.whoami";

    /// <summary>取得 WhoAmI 與 Abstractions registry 完全一致的 revision。</summary>
    public const string RuntimeHealthWhoAmIRevision =
        "9de3542216ab579be01d2a47599642a0a218576fec94de24043b2c2bd0bed427";

    /// <summary>取得連線池驗證操作的 capability ID。</summary>
    public const string RuntimePoolValidateConnection =
        "runtime.pool.validate.connection";

    /// <summary>取得連線池驗證操作與 Abstractions registry 完全一致的 revision。</summary>
    public const string RuntimePoolValidateConnectionRevision =
        "f795e1995cb30b28018411affd11f43fe84e3caf1515b5735dc413eb5ecdf40f";

    /// <summary>
    /// 建立每個 Worker session 使用的不可變操作版本表。Session 只接受表內完全相符的
    /// operation ID 與 revision，避免部署期間執行到不同版的模板或參數契約。
    /// </summary>
    /// <returns>採 ordinal 比較且不含任何執行期狀態的唯讀版本表。</returns>
    public static IReadOnlyDictionary<string, string> CreateRevisionMap()
    {
        var revisions = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            [RuntimeHealthWhoAmI] = RuntimeHealthWhoAmIRevision,
            [RuntimePoolValidateConnection] = RuntimePoolValidateConnectionRevision
        };

        return new ReadOnlyDictionary<string, string>(revisions);
    }

    /// <summary>
    /// 驗證官方 Worker 的兩個身分型操作。WhoAmI 不接受參數；連線池驗證只接受一個
    /// 非空白且已正規化的 logicalProfileId。驗證不保存字串、不建立快取，也不把 Profile
    /// 當成 Worker、Credential 或 Session key。
    /// </summary>
    /// <param name="request">已通過 frame 與 revision 驗證的 bounded request。</param>
    /// <returns>符合封閉身分操作契約時為 true；其餘操作與參數形狀皆為 false。</returns>
    public static bool IsSupportedIdentityRequest(WorkerRequestV1 request)
    {
        return request is not null && IsSupportedIdentityOperation(
            request.CapabilityOperationId,
            request.Parameters);
    }

    /// <summary>
    /// 驗證 identity-operation ID 與 bounded typed parameter snapshot。Supervisor 與 Worker
    /// 共用此單一契約，避免兩端各自維護 allowlist 後發生 revision／parameter shape 漂移。
    /// 此方法不保存 dictionary，也不建立 Profile、Credential、Token、Session 或 client state。
    /// </summary>
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
                System.StringComparison.Ordinal))
        {
            return parameters.Count == 0;
        }

        if (!string.Equals(
                capabilityOperationId,
                RuntimePoolValidateConnection,
                System.StringComparison.Ordinal) ||
            !parameters.TryGetValue("logicalProfileId", out var logicalProfileId) ||
            logicalProfileId.Kind != WorkerValueKind.String ||
            logicalProfileId.Scalar is not { Length: > 0 } value)
        {
            return false;
        }

        return value.Length <= 128 &&
            string.Equals(value, value.Trim(), System.StringComparison.Ordinal);
    }

    /// <summary>
    /// 在列舉或複製 caller parameter collection 前，以 operation ID 與 Count 完成固定成本的
    /// fail-closed shape 檢查。這個前置 gate 防止未經 ControlPlane preparer 的呼叫端利用大型或
    /// 具副作用的 dictionary 造成不必要配置；它不讀取值，也不保存 collection reference。
    /// </summary>
    /// <param name="capabilityOperationId">待驗證的封閉 capability ID。</param>
    /// <param name="parameterCount">caller collection 已公開的 bounded member count。</param>
    /// <returns>該 identity operation 的精確參數數量正確時為 true。</returns>
    public static bool IsSupportedIdentityParameterCount(
        string capabilityOperationId,
        int parameterCount)
    {
        return string.Equals(
                   capabilityOperationId,
                   RuntimeHealthWhoAmI,
                   System.StringComparison.Ordinal)
               ? parameterCount == 0
               : string.Equals(
                       capabilityOperationId,
                       RuntimePoolValidateConnection,
                       System.StringComparison.Ordinal) &&
                   parameterCount == 1;
    }
}
