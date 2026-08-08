// ============================================================================
// 檔案：SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs
// 用途：把受控產品 operation 從已解析 Profile 安全地路由到 Data8 generation-owned Connector Pool。
//
// 信任、隔離與生命週期契約：
// 1. 本執行器只接受產品可見的封閉 OperationExecutionRequest；請求不能攜帶 OrganizationId、endpoint、
//    ConnectorKind 或 Credential，這些資訊只由 IProfileResolver 的不可變部署快照提供。
// 2. 執行順序固定為 ProfileResolver -> IConnectorRouter -> Data8ConnectorPool。Pool 是唯一取得與釋放
//    Organization Admission permit、local slot 與 Data8 client 的 owner；本類別不複製、快取或 Dispose 它們。
// 3. 每次 Connector lease 的最長生命週期僅限本次 ExecuteAsync，並以 await using 在成功、取消、逾時、
//    例外或投影失敗時確定性歸還。不存在跨使用者、跨 Profile、跨 Organization 或跨 request 的 Session／
//    connection state 保留，也不建立 CTS、timer、background task 或可變 static state。
// ============================================================================

using System.Globalization;
using System.Text;
using System.Text.Json;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// Data8 的同程序受控 operation executor。
/// 此類別位於 ControlPlane 與 Pool 之間：它將 ProfileResolver 取得的 immutable generation snapshot 交給
/// Router，並只透過 lease 執行 SDK-free ConnectorOperation。它不公開 Data8 client，也不持有任何使用者、
/// credential、token、endpoint、OrganizationId、permit、request 或結果的長生命週期參考，因此可由
/// EmbeddedHostAdapter 與未來 Gateway composition root 共用，而不產生跨租戶或跨世代連線洩漏。
/// </summary>
public sealed class Data8ProfileOperationExecutor : IDynamicsOperationExecutor
{
    private const string ProfileNotFoundErrorCode = "profile.not-found";
    private const string ConnectorNotAvailableErrorCode = "connector.not-available";
    private const string OperationNotSupportedErrorCode = "operation.not-supported";
    private const string InvalidOperationParametersErrorCode = "operation.invalid-parameters";
    private const string ConnectorFailureErrorCode = "connector.operation-failed";
    private const string InvalidResponseErrorCode = "connector.invalid-response";
    private const int MaximumLegacyDisplayNameCharacters = 256;
    private const int MaximumDispatchEnvelopeBytes = 4096;
    private const int MaximumLinePictureUrlBytes = 1024;
    private const int MaximumLineProfileTextBytes = 512;
    private const int MaximumUngroupedSearchBytes = 256;
    private const int MaximumGuidArrayItems = 1000;
    private const int MaximumSliceCDispatchEnvelopeBytes = 64 * 1024;
    private const int MaximumIdempotencyKeyCharacters = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IProfileResolver _profileResolver;
    private readonly IConnectorRouter _connectorRouter;

    /// <summary>
    /// 建立不擁有外部資源的 Data8 executor。Resolver 與 Router 均由 host composition root 擁有；
    /// 它們可能連到可 drain 的 generation registry，但 executor 絕不自行 Dispose，避免一個產品 scope
    /// 提早終止其他 Profile 的 Pool 或 Admission manager。
    /// </summary>
    /// <param name="profileResolver">將固定 ProfileAlias 解析為 immutable generation snapshot 的部署端 resolver。</param>
    /// <param name="connectorRouter">只接受 resolver 輸出的 Data8 Profile 並回傳同 generation Pool 的 router。</param>
    public Data8ProfileOperationExecutor(
        IProfileResolver profileResolver,
        IConnectorRouter connectorRouter)
    {
        _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
        _connectorRouter = connectorRouter ?? throw new ArgumentNullException(nameof(connectorRouter));
    }

    /// <summary>
    /// 執行目前可由 Data8 Pool 安全投影的 runtime WhoAmI、Package01 read 與 P7.2 已核准 contact capabilities。
    /// 在任何 await 前，本方法只讀取並投影 request 的有限 scalar，且解析部署端 Profile；返回的非同步路徑
    /// 不捕捉原始 request，避免 queue／pool wait 將 HttpContext、Session 或大型參數圖保留到請求範圍之外。
    /// Profile 不存在、ConnectorKind 非 Data8、Pool generation 未登錄、未知 capability、未登錄參數、型別
    /// 不符或無效日期範圍均 fail closed，並在取得 Permit 或 Client 前結束。對外 legacy 顯示名稱只驗證大小
    /// 與 scalar shape 後丟棄，絕不成為 CRM query、routing 或跨 request retained state。
    /// </summary>
    /// <param name="request">已通過上游 RequestGuard 的封閉產品 operation；不得包含連線或認證資訊。</param>
    /// <param name="cancellationToken">由 request scope 擁有的取消訊號；只向 Pool／Client 傳遞且不被保存。</param>
    /// <returns>僅含安全 WhoAmI、fee 或 stor-lesson 純值投影的成功結果，或不含 transport detail 的固定錯誤分類。</returns>
    public Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_profileResolver.TryResolve(request.ProfileAlias, out var profile, out var resolutionError) ||
            profile is null)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                NormalizeProfileResolutionError(resolutionError),
                "The requested Dynamics profile is unavailable."));
        }

        if (profile.ConnectorKind != ConnectorKind.Data8)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                ConnectorNotAvailableErrorCode,
                "The resolved Dynamics profile does not use the Data8 connector."));
        }

        if (!TryCreateConnectorOperation(request, profile, out var operation, out var errorCode))
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                errorCode,
                "The requested Dynamics operation does not match the approved Data8 contract."));
        }

        // contact basic-info 的空白 phone/address 代表「不覆寫」；沒有欄位需要變更時，
        // 必須在取得 pool lease 前完成 no-change 回應，避免建立 CRM client 或 outbound request。
        if (string.Equals(
                operation.OperationId,
                OperationIds.MemberInfoContactUpdateBasicInfo,
                StringComparison.Ordinal) &&
            !operation.Parameters.ContainsKey("phone") &&
            !operation.Parameters.ContainsKey("address"))
        {
            return Task.FromResult(OperationExecutionResult.Success(
                OperationResponseData.ForContactBasicInfoUpdate(
                    operation.OperationId,
                    ToCeVersionString(profile.CeVersion),
                    ContactBasicInfoUpdateDisposition.NoChange,
                    ContactBasicInfoUpdateCorrelationCategory.NoDispatch)));
        }

        IConnectorPool pool;
        try
        {
            pool = _connectorRouter.Resolve(profile);
        }
        catch (NotSupportedException)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                ConnectorNotAvailableErrorCode,
                "No compatible Data8 connector is available for the resolved profile."));
        }
        catch (KeyNotFoundException)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                ConnectorNotAvailableErrorCode,
                "No active Data8 pool is available for the resolved profile generation."));
        }
        catch (ObjectDisposedException)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                ConnectorNotAvailableErrorCode,
                "The resolved Data8 profile generation is draining."));
        }

        return ExecuteOperationAsync(pool, profile, operation, cancellationToken);
    }

    /// <summary>
    /// 取得單次 lease、執行 connector 並立即投影成封閉回應。
    /// <c>await using</c> 是 Client 與 Permit 的唯一歸還點；若 connector 回傳無效 identity，先標記 faulted
    /// 再退出區塊，使 Pool Dispose 可疑 Client 而非放回 idle queue。此方法不快取結果或例外，因此結果與
    /// 失敗鏈不會延長到後續 Session／Profile。
    /// </summary>
    private static async Task<OperationExecutionResult> ExecuteOperationAsync(
        IConnectorPool pool,
        ResolvedProfile profile,
        ConnectorOperation operation,
        CancellationToken cancellationToken)
    {
        await using var lease = await pool.AcquireAsync(operation, cancellationToken).ConfigureAwait(false);
        var connectorResult = await lease.ExecuteAsync(operation, cancellationToken).ConfigureAwait(false);
        if (!connectorResult.Succeeded)
        {
            return OperationExecutionResult.Failure(
                ConnectorFailureErrorCode,
                "The Data8 connector did not complete the requested operation.");
        }

        if (!TryProjectOperationResponse(operation, profile, connectorResult, out var data))
        {
            // 回應若不符合固定 capability contract，client 健康與 session 狀態均不可證明；必須淘汰。
            lease.MarkFaulted();
            return OperationExecutionResult.Failure(
                InvalidResponseErrorCode,
                "The Data8 connector returned an invalid operation response.");
        }

        return OperationExecutionResult.Success(data);
    }

    /// <summary>
    /// 將 caller-owned request 同步驗證並複製為 connector-owned operation。此步驟必須在第一次 await 前完成，
    /// 因此 admission queue、Pool wait 與 Data8 client 永遠不會保留原始 dictionary、JsonElement backing graph、
    /// legacy display name、endpoint、credential 或其他 caller state。只有明列的 Package01 read、contact basic-info、
    /// LINE profile write 與 ungrouped commitment function 可通過；未實作 action、image/media、generic CRUD、
    /// QueryBase 與 caller-supplied FetchXML 仍一律 fail closed。
    /// </summary>
    /// <param name="request">尚由 caller 擁有的產品/Gateway request；本方法不保存其參考。</param>
    /// <param name="profile">resolver 提供的 immutable Data8 Profile，用於唯一 deadline policy。</param>
    /// <param name="operation">成功時為僅含已複製安全 scalar 的 connector operation。</param>
    /// <param name="errorCode">失敗時為不回顯輸入內容的穩定分類。</param>
    /// <returns>所有 allowlist、registry、名稱、型別、範圍與最大長度條件通過時為 true。</returns>
    private static bool TryCreateConnectorOperation(
        OperationExecutionRequest request,
        ResolvedProfile profile,
        out ConnectorOperation operation,
        out string errorCode)
    {
        operation = null!;
        errorCode = OperationNotSupportedErrorCode;
        if (string.IsNullOrWhiteSpace(request.WorkloadSubjectId) ||
            !Package01OperationRegistry.TryGet(request.CapabilityOperationId, out var definition) ||
            definition is null ||
            !IsData8SupportedOperation(request.CapabilityOperationId))
        {
            return false;
        }

        var isContactBasicInfoUpdate = string.Equals(
            request.CapabilityOperationId,
            OperationIds.MemberInfoContactUpdateBasicInfo,
            StringComparison.Ordinal);
        var isContactLineProfileUpdate = string.Equals(
            request.CapabilityOperationId,
            OperationIds.MemberInfoContactUpdateLineProfile,
            StringComparison.Ordinal);
        var isUngroupedCommitmentCount = string.Equals(
            request.CapabilityOperationId,
            OperationIds.MemberInfoContactCountUngroupedCommitment,
            StringComparison.Ordinal);
        var isSliceCOperation = IsSliceCOperation(request.CapabilityOperationId);
        if ((isContactBasicInfoUpdate || isContactLineProfileUpdate || isUngroupedCommitmentCount || isSliceCOperation) &&
            (profile.CeVersion != CeVersion.Ce91 ||
             request.Parameters is null ||
             (isContactBasicInfoUpdate && request.Parameters.Keys.Any(parameter =>
                 parameter is not "contactId" and not "phone" and not "address"))))
        {
            errorCode = profile.CeVersion == CeVersion.Ce91
                ? InvalidOperationParametersErrorCode
                : OperationNotSupportedErrorCode;
            return false;
        }

        if (!TryCopyValidatedParameters(
                request.Parameters,
                definition,
                out var parameters,
                request.CapabilityOperationId,
                allowBlankOptionalStrings: isContactBasicInfoUpdate || isUngroupedCommitmentCount))
        {
            errorCode = InvalidOperationParametersErrorCode;
            return false;
        }

        if (isContactLineProfileUpdate &&
            (!IsValidIdempotencyKey(request.IdempotencyKey) ||
             !HasValidLineProfileMutation(parameters)))
        {
            errorCode = InvalidOperationParametersErrorCode;
            return false;
        }

        if (isSliceCOperation &&
            (!IsValidIdempotencyKey(request.IdempotencyKey) ||
             !HasValidSliceCParameters(request.CapabilityOperationId, parameters)))
        {
            errorCode = InvalidOperationParametersErrorCode;
            return false;
        }

        if (isUngroupedCommitmentCount && request.IdempotencyKey is not null)
        {
            errorCode = InvalidOperationParametersErrorCode;
            return false;
        }

        var estimatedBytes = 256;
        if ((isContactLineProfileUpdate || isUngroupedCommitmentCount || isSliceCOperation) &&
            !TryEstimateBoundedEnvelopeBytes(
                request.CapabilityOperationId,
                request.WorkloadSubjectId,
                request.IdempotencyKey,
                parameters,
                out estimatedBytes))
        {
            errorCode = InvalidOperationParametersErrorCode;
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        operation = new ConnectorOperation
        {
            OperationId = definition.CapabilityOperationId,
            Parameters = parameters,
            WorkloadSubjectId = request.WorkloadSubjectId.Trim(),
            DeadlineUtc = now.Add(profile.Operation.Timeout),
            // B1 以已複製 scalar 的保守 UTF-8 大小進入 admission；既有 operation 仍維持原 256-byte 基準。
            // caller 不能提供自稱大小，且任何超過 Embedded 4 KiB 上限的 B1 request 會在 Pool 前失敗。
            EstimatedBytes = estimatedBytes
        };
        return true;
    }

    /// <summary>
    /// 判斷 capability 是否屬於已具有 Data8 server-owned template/projection 的最小 allowlist。switch 是唯一
    /// 可稽核 dispatch table；新增 operation 必須同時新增 query、DTO projection、response size test 與 P7 matrix
    /// 進度，不可僅因 registry 已宣告就自動通行。
    /// </summary>
    private static bool IsData8SupportedOperation(string operationId)
        => operationId switch
        {
            OperationIds.RuntimeHealthWhoAmI => true,
            OperationIds.FeeDedicationRetrieveByContact => true,
            OperationIds.FeeDedicationRetrieveByContactDateRange => true,
            OperationIds.FeesRetrieveByDedicationPeriod => true,
            OperationIds.FeesEditorLoadByDiscipleLesson => true,
            OperationIds.LessonsStorRetrieveByContact => true,
            OperationIds.LessonsStorRetrieveByDiscipleLesson => true,
            OperationIds.MemberInfoContactUpdateBasicInfo => true,
            OperationIds.MemberInfoContactUpdateLineProfile => true,
            OperationIds.MemberInfoContactCountUngroupedCommitment => true,
            OperationIds.ListMembersAddMany => true,
            OperationIds.ListMembersRemoveOne => true,
            OperationIds.ListManagementSmallGroupUpdateFields => true,
            OperationIds.ContactAssignOwner => true,
            OperationIds.NewPersonContactTransferBetweenLists => true,
            _ => false
        };

    /// <summary>
    /// Slice C 的固定 list-management operation allowlist。它只表示 executor 已有封閉 schema；
    /// matrix fixture 與 ChurchReport consumer gate 仍在更上層控制，未核准 fixture 不會因此被啟用。
    /// </summary>
    private static bool IsSliceCOperation(string operationId)
        => operationId is
            OperationIds.ListMembersAddMany or
            OperationIds.ListMembersRemoveOne or
            OperationIds.ListManagementSmallGroupUpdateFields or
            OperationIds.ContactAssignOwner or
            OperationIds.NewPersonContactTransferBetweenLists;

    /// <summary>
    /// 依 registry schema 將 request parameters 正規化為新的短生命週期 dictionary。輸入僅接受 primitive CLR
    /// scalar 或 Gateway body reader 複製的 JSON scalar；object、array、unknown name、null required value、
    /// 空 Guid、無時區日期與過長相容顯示名稱均在 Pool 前拒絕。可選 display name 只驗證後丟棄，因 server-owned
    /// QueryExpression 只依 stable Guid/filter 執行，避免名稱注入或同名資料改變查詢語意。
    /// </summary>
    private static bool TryCopyValidatedParameters(
        IReadOnlyDictionary<string, object?>? source,
        OperationDefinition definition,
        out IReadOnlyDictionary<string, object?> parameters,
        string operationId,
        bool allowBlankOptionalStrings = false)
    {
        // contact basic-info 的 optional phone/address 與 B2 optional search 使用 blank-as-omitted；其他 operation
        // 仍要求 non-empty string，避免泛化 no-change／no-filter 語意而放寬既有 read/write contract。
        parameters = null!;
        if (source is null || source.Count > definition.Parameters.Count)
        {
            return false;
        }

        foreach (var suppliedName in source.Keys)
        {
            if (!definition.Parameters.Any(parameter =>
                    string.Equals(parameter.Name, suppliedName, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        var copy = new Dictionary<string, object?>(definition.Parameters.Count, StringComparer.Ordinal);
        foreach (var parameter in definition.Parameters)
        {
            if (!source.TryGetValue(parameter.Name, out var sourceValue))
            {
                if (parameter.Required)
                {
                    return false;
                }

                continue;
            }

            if (!TryNormalizeParameter(
                    parameter,
                    sourceValue,
                    allowBlankOptionalStrings,
                    operationId,
                    out var normalized))
            {
                return false;
            }

            if (parameter.Required && normalized is null)
            {
                return false;
            }

            if (normalized is null)
            {
                // P7.2 contact basic-info 的空白 optional string 是不覆寫，B2 空白 search 是不套用搜尋；
                // 兩者都省略欄位，讓後續 no-change／fixed-query 判斷與 connector template 保持一致。
                continue;
            }

            // contactName、dedicationBookingName 與 lessonName 只保留 legacy API shape；它們沒有 query authority，
            // 也不應延長到 connector 的 async state machine 或 pooled Data8 client。
            if (!IsLegacyDisplayOnlyParameter(parameter.Name))
            {
                copy.Add(parameter.Name, normalized);
            }
        }

        if (copy.TryGetValue("startDate", out var startValue) &&
            copy.TryGetValue("endDate", out var endValue) &&
            startValue is DateTimeOffset startDate &&
            endValue is DateTimeOffset endDate &&
            startDate > endDate)
        {
            return false;
        }

        parameters = copy;
        return true;
    }

    /// <summary>
    /// 判斷 schema 中僅為舊產品呼叫形狀保留、但永不影響 server-owned 查詢的顯示名稱。這些值仍會先經過
    /// 嚴格 scalar/長度驗證，避免未受限字串藉由相容欄位進入 request lifetime；回傳 true 後 caller 必須丟棄。
    /// </summary>
    private static bool IsLegacyDisplayOnlyParameter(string parameterName)
        => parameterName is "contactName" or "dedicationBookingName" or "lessonName";

    /// <summary>
    /// 以 registry 定義的 scalar type 正規化單一參數。每一成功值都是 immutable value type 或新的 trimmed string；
    /// 不保留 JsonElement、原始 JSON text 或可變 caller object。字串上限避免顯示相容欄位造成無界配置，日期
    /// 強制 UTC 以免 host local time 令 Embedded/Dedicated/Central 執行結果漂移。
    /// </summary>
    private static bool TryNormalizeParameter(
        OperationParameterDefinition definition,
        object? source,
        bool allowBlankOptionalStrings,
        string operationId,
        out object? normalized)
    {
        normalized = null;
        return definition.Type switch
        {
            "guid" => TryNormalizeNonEmptyGuid(source, out normalized),
            "date-time" => TryNormalizeUtcDateTime(source, out normalized),
            "string" => TryNormalizeBoundedString(
                source,
                allowBlankOptionalStrings && !definition.Required,
                GetMaximumStringCharacters(operationId, definition.Name),
                GetMaximumStringBytes(operationId, definition.Name),
                out normalized),
            "enum" => TryNormalizeBoundedString(
                source,
                allowBlankAsNoValue: false,
                maximumCharacters: 16,
                maximumBytes: 16,
                out normalized),
            "guid-array" => TryNormalizeGuidArray(source, out normalized),
            _ => false
        };
    }

    /// <summary>
    /// 將 Slice C 的有限 GUID 集合複製成排序後的新陣列。只接受具備有限 Count 的
    /// <see cref="IReadOnlyList{T}"/> 或 JSON array；空值、空 GUID、重複值及超過 1,000 筆一律拒絕。
    /// 排序後的 defensive copy 讓 canonical hash 不受輸入順序影響，也不會把 caller 的 mutable array
    /// 保留到 connector lease 或非同步狀態機。
    /// </summary>
    private static bool TryNormalizeGuidArray(object? source, out object? normalized)
    {
        normalized = null;
        Guid[]? copy = source switch
        {
            IReadOnlyList<Guid> values when values.Count is > 0 and <= MaximumGuidArrayItems
                => CopyGuidArray(values),
            JsonElement { ValueKind: JsonValueKind.Array } element
                => CopyJsonGuidArray(element),
            _ => null
        };

        if (copy is null || copy.Length is 0 or > MaximumGuidArrayItems)
        {
            return false;
        }

        var seen = new HashSet<Guid>();
        foreach (var guid in copy)
        {
            if (guid == Guid.Empty || !seen.Add(guid))
            {
                return false;
            }
        }

        Array.Sort(copy);
        normalized = copy;
        return true;
    }

    /// <summary>複製有限原生 GUID list，避免後續 caller 變更影響 executor-owned operation。</summary>
    private static Guid[] CopyGuidArray(IReadOnlyList<Guid> source)
    {
        var copy = new Guid[source.Count];
        for (var index = 0; index < copy.Length; index++)
        {
            copy[index] = source[index];
        }

        return copy;
    }

    /// <summary>從 JSON array 建立有限 GUID copy；非 string GUID 元素立即拒絕。</summary>
    private static Guid[]? CopyJsonGuidArray(JsonElement source)
    {
        if (source.GetArrayLength() is 0 or > MaximumGuidArrayItems)
        {
            return null;
        }

        var copy = new Guid[source.GetArrayLength()];
        var index = 0;
        foreach (var element in source.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(element.GetString(), out var guid))
            {
                return null;
            }

            copy[index++] = guid;
        }

        return copy;
    }

    /// <summary>
    /// 將原生 Guid 或 JSON/string scalar 正規化為非空 Guid。不能解析、空 GUID、number/object/array 都拒絕，
    /// 因這些值若通過會讓固定 CRM filter 失去明確資料邊界。
    /// </summary>
    private static bool TryNormalizeNonEmptyGuid(object? source, out object? normalized)
    {
        normalized = null;
        if (source is Guid guid && guid != Guid.Empty)
        {
            normalized = guid;
            return true;
        }

        var text = source switch
        {
            string stringValue => stringValue,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(text) || !Guid.TryParse(text, out var parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        normalized = parsed;
        return true;
    }

    /// <summary>
    /// 將原生 DateTimeOffset/DateTime 或帶明確 UTC offset 的 JSON/string scalar 正規化。未指定 Kind 的內部
    /// DateTime 明確視為 UTC；外部字串則必須自行攜帶 Z 或 ±HH:mm，避免 host time zone 在不同部署模式下改變
    /// 相同操作的日期範圍。
    /// </summary>
    private static bool TryNormalizeUtcDateTime(object? source, out object? normalized)
    {
        normalized = null;
        switch (source)
        {
            case DateTimeOffset offset:
                normalized = offset.ToUniversalTime();
                return true;
            case DateTime dateTime:
                var utcDateTime = dateTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                    : dateTime.ToUniversalTime();
                normalized = new DateTimeOffset(utcDateTime).ToUniversalTime();
                return true;
            case string text:
                return TryParseExplicitOffsetUtc(text, out normalized);
            case JsonElement { ValueKind: JsonValueKind.String } element:
                return TryParseExplicitOffsetUtc(element.GetString(), out normalized);
            default:
                return false;
        }
    }

    /// <summary>
    /// 將字串或 JSON string 防禦性 trim 並限制於相容參數的有限長度。空白、無效 UTF-16 或任何非字串 JSON
    /// scalar 都 fail closed；嚴格 UTF-8 byte count 同時保證後續 connector/result accounting 不會替換字元而
    /// 意外放寬大小估計。
    /// </summary>
    private static bool TryNormalizeBoundedString(
        object? source,
        bool allowBlankAsNoValue,
        int maximumCharacters,
        int maximumBytes,
        out object? normalized)
    {
        var text = source switch
        {
            string stringValue => stringValue,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null
        };
        if (text is null)
        {
            // 只有實際 string scalar 才能使用「空白代表不覆寫」；null、number、object 與 array
            // 仍然是格式錯誤，避免把 JSON null 靜默轉成 no-change。
            normalized = null;
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            normalized = null;
            return allowBlankAsNoValue;
        }

        var trimmed = text.Trim();
        try
        {
            if (trimmed.Length > maximumCharacters ||
                StrictUtf8.GetByteCount(trimmed) > maximumBytes)
            {
                normalized = null;
                return false;
            }
        }
        catch (EncoderFallbackException)
        {
            normalized = null;
            return false;
        }

        normalized = trimmed;
        return true;
    }

    /// <summary>
    /// 取得 operation-specific 字元上限。B1 允許 URL 到 1,024 字元、profile 文字到 512 字元；其他既有
    /// operation 保留原本 256 字元限制，避免本次 enum 支援意外擴張 P7.1 read contract。
    /// </summary>
    private static int GetMaximumStringCharacters(string operationId, string parameterName)
        => operationId switch
        {
            OperationIds.MemberInfoContactUpdateLineProfile => parameterName switch
            {
                "pictureUrl" => MaximumLinePictureUrlBytes,
                "statusMessage" or "displayName" => MaximumLineProfileTextBytes,
                _ => 16
            },
            OperationIds.MemberInfoContactCountUngroupedCommitment => MaximumUngroupedSearchBytes,
            _ => MaximumLegacyDisplayNameCharacters
        };

    /// <summary>
    /// 取得嚴格 UTF-8 byte ceiling。此值與 ProductClient、connector template 及 4 KiB admission envelope
    /// 同步；無效 UTF-16 由共同 normalizer fail closed，不會以 replacement character 縮小計算值。
    /// </summary>
    private static int GetMaximumStringBytes(string operationId, string parameterName)
        => operationId switch
        {
            OperationIds.MemberInfoContactUpdateLineProfile => parameterName switch
            {
                "pictureUrl" => MaximumLinePictureUrlBytes,
                "statusMessage" or "displayName" => MaximumLineProfileTextBytes,
                _ => 16
            },
            OperationIds.MemberInfoContactCountUngroupedCommitment => MaximumUngroupedSearchBytes,
            _ => MaximumLegacyDisplayNameCharacters * 4
        };

    /// <summary>
    /// 驗證 B1 三組封閉 mutation。picture/status 只允許 set/clear，display name 只允許 set/preserve；set
    /// 必須附值，clear/preserve 禁止附值。Picture URL 另要求 absolute HTTPS、無 user-info／fragment／自訂 port。
    /// 本方法只巡覽 executor-owned scalar copy，不保留 URI、字串或 caller dictionary。
    /// </summary>
    private static bool HasValidLineProfileMutation(IReadOnlyDictionary<string, object?> parameters)
    {
        if (!HasValidNullableLineMutation(parameters, "pictureMode", "pictureUrl", validateHttpsUrl: true) ||
            !HasValidNullableLineMutation(parameters, "statusMode", "statusMessage", validateHttpsUrl: false) ||
            !parameters.TryGetValue("displayNameMode", out var displayModeValue) ||
            displayModeValue is not string displayMode)
        {
            return false;
        }

        return displayMode switch
        {
            "set" => parameters.TryGetValue("displayName", out var value) && value is string,
            "preserve" => !parameters.ContainsKey("displayName"),
            _ => false
        };
    }

    /// <summary>驗證 picture/status 的 mode/value 配對；只在 picture 分支解析 bounded URI，不發出 URL probe。</summary>
    private static bool HasValidNullableLineMutation(
        IReadOnlyDictionary<string, object?> parameters,
        string modeName,
        string valueName,
        bool validateHttpsUrl)
    {
        if (!parameters.TryGetValue(modeName, out var modeValue) || modeValue is not string mode)
        {
            return false;
        }

        if (string.Equals(mode, "clear", StringComparison.Ordinal))
        {
            return !parameters.ContainsKey(valueName);
        }

        if (!string.Equals(mode, "set", StringComparison.Ordinal) ||
            !parameters.TryGetValue(valueName, out var rawValue) ||
            rawValue is not string value)
        {
            return false;
        }

        return !validateHttpsUrl || IsSafeHttpsPictureUrl(value);
    }

    /// <summary>以無 I/O 的 URI parser 驗證 B1 picture URL；不解析 DNS、不跟隨 redirect，也不保存 Uri。</summary>
    private static bool IsSafeHttpsPictureUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
           !string.IsNullOrWhiteSpace(uri.Host) &&
           string.IsNullOrEmpty(uri.UserInfo) &&
           string.IsNullOrEmpty(uri.Fragment) &&
           uri.IsDefaultPort;

    /// <summary>
    /// 驗證 caller-owned 冪等鍵為 1-128 個 RFC 3986 unreserved ASCII 字元。鍵只參與本次 admission size，
    /// 不進 connector、cache、session 或 log；unknown write outcome 仍由上層 reconciliation 處理而不盲目重送。
    /// </summary>
    private static bool IsValidIdempotencyKey(string? value)
        => !string.IsNullOrEmpty(value) &&
           value.Length <= MaximumIdempotencyKeyCharacters &&
           value.All(static character =>
               (character >= 'a' && character <= 'z') ||
               (character >= 'A' && character <= 'Z') ||
               (character >= '0' && character <= '9') ||
               character is '-' or '.' or '_' or '~');

    /// <summary>
    /// 驗證 Slice C 的跨欄位不變量。small-group 只接受兩個固定 mode；transfer 不允許 source 與 target
    /// 相同，且 weekStartDate 必須已正規化為 UTC Sunday。這些規則在取得 lease 前執行，避免不完整 graph
    /// mutation 進入 connector 或以 request-time fallback 改變版本／profile。
    /// </summary>
    private static bool HasValidSliceCParameters(
        string operationId,
        IReadOnlyDictionary<string, object?> parameters)
    {
        if (string.Equals(operationId, OperationIds.ListManagementSmallGroupUpdateFields, StringComparison.Ordinal))
        {
            return parameters.TryGetValue("mode", out var mode) &&
                   mode is string modeText &&
                   modeText is "change-race-leader" or "change-area-leader";
        }

        if (!string.Equals(operationId, OperationIds.NewPersonContactTransferBetweenLists, StringComparison.Ordinal))
        {
            return true;
        }

        if (!parameters.TryGetValue("targetListId", out var target) || target is not Guid targetId ||
            !parameters.TryGetValue("weekStartDate", out var weekStart) ||
            weekStart is not DateTimeOffset weekStartUtc ||
            weekStartUtc.Offset != TimeSpan.Zero ||
            weekStartUtc.DayOfWeek != DayOfWeek.Sunday)
        {
            return false;
        }

        return !parameters.TryGetValue("sourceListId", out var source) ||
               source is not Guid sourceId ||
               sourceId != targetId;
    }

    /// <summary>
    /// 由已驗證的 bounded scalar 建立保守 envelope 大小。計算包含固定結構、operation、workload、冪等鍵、
    /// parameter name/value；checked 與嚴格 UTF-8 防止 overflow／replacement fallback。超過 4 KiB 即在 Router 前
    /// fail closed，因此 admission 不會低估 B1 對 shared Organization 的成本。
    /// </summary>
    private static bool TryEstimateBoundedEnvelopeBytes(
        string operationId,
        string workloadSubjectId,
        string? idempotencyKey,
        IReadOnlyDictionary<string, object?> parameters,
        out int estimatedBytes)
    {
        estimatedBytes = 256;
        var maximumBytes = IsSliceCOperation(operationId)
            ? MaximumSliceCDispatchEnvelopeBytes
            : MaximumDispatchEnvelopeBytes;
        try
        {
            estimatedBytes = checked(estimatedBytes + StrictUtf8.GetByteCount(operationId));
            estimatedBytes = checked(estimatedBytes + StrictUtf8.GetByteCount(workloadSubjectId.Trim()));
            if (idempotencyKey is not null)
            {
                estimatedBytes = checked(estimatedBytes + StrictUtf8.GetByteCount(idempotencyKey));
            }

            foreach (var parameter in parameters)
            {
                estimatedBytes = checked(estimatedBytes + StrictUtf8.GetByteCount(parameter.Key) + 8);
                estimatedBytes = parameter.Value switch
                {
                    Guid => checked(estimatedBytes + 16),
                    Guid[] guidArray => checked(estimatedBytes + sizeof(int) + checked(guidArray.Length * 16)),
                    IReadOnlyList<Guid> => throw new InvalidOperationException("GUID array must be executor-owned."),
                    DateTimeOffset => checked(estimatedBytes + 16),
                    int => checked(estimatedBytes + 4),
                    string text => checked(estimatedBytes + StrictUtf8.GetByteCount(text)),
                    _ => throw new InvalidOperationException("Unsupported bounded scalar.")
                };
            }

            return estimatedBytes <= maximumBytes;
        }
        catch (EncoderFallbackException)
        {
            estimatedBytes = 0;
            return false;
        }
        catch (OverflowException)
        {
            estimatedBytes = 0;
            return false;
        }
        catch (InvalidOperationException)
        {
            estimatedBytes = 0;
            return false;
        }
    }

    /// <summary>
    /// 驗證外部日期字串擁有明確時區並以 invariant round-trip 規則解析。此方法不把本機 culture/timezone 當作
    /// 隱藏輸入，因此同一 Gateway body 在 Lenovo、Dedicated 與未來 Central Gateway 都會得到同一 UTC filter。
    /// </summary>
    private static bool TryParseExplicitOffsetUtc(string? text, out object? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(text) || !HasExplicitDateTimeOffset(text) ||
            !DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return false;
        }

        normalized = parsed.ToUniversalTime();
        return true;
    }

    /// <summary>
    /// 檢查字串尾端具有 Z/z 或完整 ±HH:mm offset。這是 string date-time 的最小信任邊界；其餘日期有效性
    /// 仍由 invariant parser 驗證，避免只以字尾判斷而接受不完整日期。
    /// </summary>
    private static bool HasExplicitDateTimeOffset(string text)
    {
        if (text.EndsWith('Z') || text.EndsWith('z'))
        {
            return true;
        }

        if (text.Length < 6)
        {
            return false;
        }

        var suffix = text.AsSpan(text.Length - 6);
        return suffix[0] is '+' or '-' &&
               suffix[3] == ':' &&
               char.IsAsciiDigit(suffix[1]) &&
               char.IsAsciiDigit(suffix[2]) &&
               char.IsAsciiDigit(suffix[4]) &&
               char.IsAsciiDigit(suffix[5]);
    }

    /// <summary>
    /// 將已由 connector 投影的結果驗證為目前 capability 對應的封閉 response branch。WhoAmI 保留既有的
    /// OrganizationId cross-check；registry capabilities 則同時比對 operation、CE version、response kind 與對應
    /// branch，Package01 records 另驗證列數與保守 byte budget。任何不符都由 caller 在 lease scope 內
    /// MarkFaulted，避免不可信 client 回池。
    /// </summary>
    private static bool TryProjectOperationResponse(
        ConnectorOperation operation,
        ResolvedProfile profile,
        ConnectorOperationResult result,
        out OperationResponseData? data)
    {
        if (string.Equals(operation.OperationId, OperationIds.RuntimeHealthWhoAmI, StringComparison.Ordinal))
        {
            return TryProjectWhoAmI(operation.OperationId, profile, result, out data);
        }

        data = null;
        if (!Package01OperationRegistry.TryGet(operation.OperationId, out var definition) ||
            definition is null ||
            result.Data is not { } connectorData ||
            !string.Equals(connectorData.OperationId, operation.OperationId, StringComparison.Ordinal) ||
            !string.Equals(connectorData.CeVersion, ToCeVersionString(profile.CeVersion), StringComparison.Ordinal) ||
            connectorData.ResponseKind != definition.ResponseKind)
        {
            return false;
        }

        var isValid = connectorData.ResponseKind switch
        {
            OperationResponseKind.Package01FeeRecords => connectorData.FeeRecords is not null &&
                                                       TryValidateFeeRecords(connectorData.FeeRecords, definition),
            OperationResponseKind.Package01StorLessonRecords => connectorData.StorLessonRecords is not null &&
                                                                 TryValidateStorLessonRecords(connectorData.StorLessonRecords, definition),
            OperationResponseKind.ContactBasicInfoUpdate => connectorData.ContactBasicInfoUpdate is not null,
            OperationResponseKind.ContactLineProfileUpdate => connectorData.ContactLineProfileUpdate is not null,
            OperationResponseKind.UngroupedCommitmentCounts => connectorData.UngroupedCommitmentCounts is not null,
            OperationResponseKind.StaticListMembershipMutation => connectorData.StaticListMembershipMutation is not null,
            OperationResponseKind.SmallGroupFixedFieldsMutation => connectorData.SmallGroupFixedFieldsMutation is not null,
            OperationResponseKind.ContactOwnerAssignment => connectorData.ContactOwnerAssignment is not null,
            OperationResponseKind.ContactListTransfer => connectorData.ContactListTransfer is not null,
            _ => false
        };
        if (!isValid)
        {
            return false;
        }

        data = connectorData;
        return true;
    }

    /// <summary>
    /// 驗證 fee safe records 的列數、GUID 與保守 cumulative byte budget。connector 已在自己的 request scope 將
    /// CRM Entity 投影完畢；此處絕不保留 Entity、formatted-value dictionary 或 raw page，只巡覽封閉 DTO 來
    /// 防禦測試替身、未來 connector 或錯誤 adapter 送入超限資料。任一違規使 lease faulted 而非 partial success。
    /// </summary>
    private static bool TryValidateFeeRecords(
        IReadOnlyList<Package01FeeRecord> records,
        OperationDefinition definition)
    {
        if (records.Count > definition.MaximumResultItemCount)
        {
            return false;
        }

        var bytes = 0;
        foreach (var record in records)
        {
            if (record is null || record.FeeId == Guid.Empty ||
                !TryAddFixedBytes(ref bytes, 256, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.PayWayLabel, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.CategoryLabel, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.Others, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.PaidPeriod, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.Name, definition.MaximumCumulativeResponseBytes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 驗證 stor-lesson safe records 的列數、所有非 null lookup GUID 與 bounded display data。每列先計入一個
    /// 保守固定 JSON/DTO overhead，再加上嚴格 UTF-8 字串 bytes；這故意比最小化配置保守，優先保證 Pool lease
    /// 不會因惡意或失控的上游文字欄位在 request scope 內累積無界記憶體。
    /// </summary>
    private static bool TryValidateStorLessonRecords(
        IReadOnlyList<Package01StorLessonRecord> records,
        OperationDefinition definition)
    {
        if (records.Count > definition.MaximumResultItemCount)
        {
            return false;
        }

        var bytes = 0;
        foreach (var record in records)
        {
            if (record is null ||
                record.StorLessonId == Guid.Empty ||
                record.ContactId == Guid.Empty ||
                record.DiscipleLessonId == Guid.Empty ||
                !TryAddFixedBytes(ref bytes, 256, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.ContactName, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.ContactMobile, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.DiscipleLessonName, definition.MaximumCumulativeResponseBytes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 將固定 DTO structural overhead 加入 cumulative budget。checked 防止整數溢位把超大結果誤判為小；每次
    /// 加總後立即比較 registry cap，使下一頁或下一筆投影不會在已知超限後繼續保留資料。
    /// </summary>
    private static bool TryAddFixedBytes(ref int total, int additionalBytes, int maximumBytes)
    {
        try
        {
            total = checked(total + additionalBytes);
            return total <= maximumBytes;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// 將 nullable display string 的嚴格 UTF-8 byte length 加入本次 response budget。沒有字串時不配置或加總；
    /// 遇到 invalid UTF-16 或整數溢位立即拒絕，避免 replacement character 或 wraparound 破壞結果大小契約。
    /// </summary>
    private static bool TryAddUtf8Bytes(ref int total, string? value, int maximumBytes)
    {
        if (value is null)
        {
            return true;
        }

        try
        {
            total = checked(total + StrictUtf8.GetByteCount(value));
            return total <= maximumBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// 從 SDK-free connector scalar map 建構嚴格的 WhoAmI 投影。
    /// 三個 GUID 都必須存在且有效，且 organizationId 必須與 resolver 的 immutable Profile snapshot 相同；
    /// 此比較是防止 Factory／Client 混接到其他 Organization 的最後防線。投影只建立純值 DTO，絕不回傳
    /// 原始 dictionary、connector client 或 Profile 的 credential／endpoint 資訊。
    /// </summary>
    private static bool TryProjectWhoAmI(
        string operationId,
        ResolvedProfile profile,
        ConnectorOperationResult result,
        out OperationResponseData? data)
    {
        data = null;
        if (!TryReadNonEmptyGuid(result.Values, "userId", out var userId) ||
            !TryReadNonEmptyGuid(result.Values, "businessUnitId", out var businessUnitId) ||
            !TryReadNonEmptyGuid(result.Values, "organizationId", out var organizationId) ||
            organizationId != profile.OrganizationId)
        {
            return false;
        }

        data = OperationResponseData.ForWhoAmI(
            operationId,
            ToCeVersionString(profile.CeVersion),
            new WhoAmIResponseData
            {
                UserId = userId,
                BusinessUnitId = businessUnitId,
                OrganizationId = organizationId
            });
        return true;
    }

    /// <summary>
    /// 讀取固定名稱的非空 GUID，避免寬鬆解析把缺欄、空字串或其他 connector 回應種類誤判為成功。
    /// 任何失敗都只回傳 false，讓 caller 在 lease 還在作用域內標記 faulted 並確定性 dispose Client。
    /// </summary>
    private static bool TryReadNonEmptyGuid(
        IReadOnlyDictionary<string, string?> values,
        string key,
        out Guid value)
    {
        value = Guid.Empty;
        return values.TryGetValue(key, out var scalar) &&
               Guid.TryParse(scalar, out value) &&
               value != Guid.Empty;
    }

    /// <summary>
    /// 將部署端 CE enum 映射為公共回應合約的固定版本字串。未知 enum 代表已損毀或未受支援設定，
    /// 以例外拒絕而非偷偷標記為另一版本，避免跨版本 connector 結果被產品錯誤重用。
    /// </summary>
    private static string ToCeVersionString(CeVersion ceVersion)
        => ceVersion switch
        {
            CeVersion.Ce82 => "8.2",
            CeVersion.Ce91 => "9.1",
            _ => throw new ArgumentOutOfRangeException(nameof(ceVersion), ceVersion, "Unsupported CE version.")
        };

    /// <summary>
    /// 將 resolver 的內部錯誤限制為已審查的安全分類，避免錯誤實作把 endpoint、Organization GUID 或
    /// credential reference 帶入產品回應。未知分類仍使用 profile.not-found，保持 fail-closed 語意。
    /// </summary>
    private static string NormalizeProfileResolutionError(string? resolutionError)
        => string.Equals(resolutionError, ProfileNotFoundErrorCode, StringComparison.Ordinal)
            ? ProfileNotFoundErrorCode
            : ProfileNotFoundErrorCode;
}
