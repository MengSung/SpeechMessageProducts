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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
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
    private const int MaximumAuthenticationLookupCharacters = 256;
    private const int MaximumAuthenticationLookupBytes = 256;
    private const int MaximumAuthenticationContactResponseTextBytes = 256;
    private const int MaximumAuthenticationContactRecords = 2;
    private const int MaximumAppNamedMembershipRecords = 32;
    private const int MaximumAppNamedMembershipResponseBytes = 32 * 1024;
    private const int MaximumMemberInfoPresentRecordTextBytes = 512 * 4;
    private const int MaximumMemberInfoPresentRecordFixedBytes = 96;
    private const int MaximumGuidArrayItems = 1000;
    private const int MaximumSliceCDispatchEnvelopeBytes = 64 * 1024;
    private const int MaximumSpecialResourceDispatchEnvelopeBytes = 64 * 1024;
    private const int MaximumImagePayloadBytes = 32 * 1024;
    private const int MaximumImageWidthPixels = 2048;
    private const int MaximumImageHeightPixels = 2048;
    private const long MaximumImagePixels = 2_097_152;
    private const int MaximumIdempotencyKeyCharacters = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IProfileResolver _profileResolver;
    private readonly IConnectorRouter _connectorRouter;
    private readonly MetadataOptionSetCache? _metadataOptionSetCache;

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
        : this(profileResolver, connectorRouter, metadataOptionSetCache: null)
    {
    }

    /// <summary>
    /// 建立可選 runtime-owned metadata cache 的 Data8 executor。cache 只能由同一 host generation composition root
    /// 建立並在 runtime Dispose 時釋放；本 executor 不擁有或 Dispose cache，避免某次 request 或單一 executor 提早
    /// 清除其他同 runtime request 的 immutable metadata。cache 不含 user/session、SDK graph、lease 或 connector client，
    /// 且只有 server-resolved locale 已被 connector result 證實時才會使用。
    /// </summary>
    /// <param name="profileResolver">將固定 ProfileAlias 解析為 immutable generation snapshot 的部署端 resolver。</param>
    /// <param name="connectorRouter">只接受 resolver 輸出的 Data8 Profile 並回傳同 generation Pool 的 router。</param>
    /// <param name="metadataOptionSetCache">可為 null 的 runtime-owned bounded pure-value cache。</param>
    public Data8ProfileOperationExecutor(
        IProfileResolver profileResolver,
        IConnectorRouter connectorRouter,
        MetadataOptionSetCache? metadataOptionSetCache)
    {
        _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
        _connectorRouter = connectorRouter ?? throw new ArgumentNullException(nameof(connectorRouter));
        _metadataOptionSetCache = metadataOptionSetCache;
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
    /// <returns>僅含已登錄封閉純量 response branch 的成功結果，或不含 transport detail 的固定錯誤分類。</returns>
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

        if (TryGetCachedMetadataOptionSet(profile, operation, out var cachedMetadata))
        {
            return Task.FromResult(OperationExecutionResult.Success(cachedMetadata));
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

        return ExecuteOperationAsync(pool, profile, operation, _metadataOptionSetCache, cancellationToken);
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
        MetadataOptionSetCache? metadataOptionSetCache,
        CancellationToken cancellationToken)
    {
        await using var lease = await pool.AcquireAsync(operation, cancellationToken).ConfigureAwait(false);
        var connectorResult = await lease.ExecuteAsync(operation, cancellationToken).ConfigureAwait(false);
        if (!connectorResult.Succeeded)
        {
            // Connector 已明確回報未成功時，該 client 的 transport/session 狀態不能再被證明安全；即使沒有
            // 原始例外也必須在 lease 結束前標成 faulted，讓 Pool dispose 而非放回同 Profile/Generation 的
            // idle queue。這與 timeout、取消及無效 response 的淘汰規則相同，避免下一個 request 重用不確定狀態。
            lease.MarkFaulted();
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

        var projectedData = data!;
        TryStoreMetadataOptionSet(metadataOptionSetCache, profile, operation, connectorResult, projectedData);
        return OperationExecutionResult.Success(projectedData);
    }

    /// <summary>
    /// 在取得 lease 前嘗試讀取 metadata cache。只接受 metadata operation 的封閉 target，並將 profile resolver
    /// 已證實的 alias/generation 交給 cache；不可使用 request alias 外觀、caller locale 或上一個 response 作為
    /// authority。miss 一律回到正常 connector request，故 cache 不可用不會改變正確性或把使用者資料跨 request 保留。
    /// </summary>
    private bool TryGetCachedMetadataOptionSet(
        ResolvedProfile profile,
        ConnectorOperation operation,
        out OperationResponseData? data)
    {
        data = null;
        if (_metadataOptionSetCache is null ||
            !string.Equals(operation.OperationId, OperationIds.MetadataOptionSetByAttribute, StringComparison.Ordinal) ||
            !TryReadMetadataOptionSetTarget(operation.Parameters, out var target) ||
            !_metadataOptionSetCache.TryGet(profile.ProfileAlias, profile.GenerationId, target, out var options) ||
            options is null)
        {
            return false;
        }

        data = OperationResponseData.ForOptionSetOptions(
            operation.OperationId,
            ToCeVersionString(profile.CeVersion),
            options);
        return true;
    }

    /// <summary>
    /// 在 connector result 已通過 closed-union projection 後，才將 metadata pure values 存入 runtime cache。locale
    /// 必須是正的 server-resolved value，且 target 必須和本次 operation 精確相符；任何缺值、invalid response、
    /// cache disposed 或超出容量的狀況都靜默維持 request-local result，絕不重試 connector 或將不完整 key 寫入 cache。
    /// </summary>
    private static void TryStoreMetadataOptionSet(
        MetadataOptionSetCache? metadataOptionSetCache,
        ResolvedProfile profile,
        ConnectorOperation operation,
        ConnectorOperationResult connectorResult,
        OperationResponseData data)
    {
        if (metadataOptionSetCache is null ||
            !string.Equals(operation.OperationId, OperationIds.MetadataOptionSetByAttribute, StringComparison.Ordinal) ||
            connectorResult.ServerResolvedMetadataLocale is not > 0 ||
            data.OptionSetOptions is null ||
            !TryReadMetadataOptionSetTarget(operation.Parameters, out var target))
        {
            return;
        }

        var key = new MetadataOptionSetCacheKey(
            profile.ProfileAlias,
            profile.GenerationId,
            target,
            connectorResult.ServerResolvedMetadataLocale.Value);
        _ = metadataOptionSetCache.Store(key, data.OptionSetOptions);
    }

    /// <summary>
    /// 讀取 executor 已正規化的唯一 metadata target。此 helper 不接受 string、integer 或 caller schema，因此 cache
    /// 與 connector 固定 RetrieveAttribute request 永遠指向相同 allowlisted capability，而非任意 CRM metadata。
    /// </summary>
    private static bool TryReadMetadataOptionSetTarget(
        IReadOnlyDictionary<string, object?> parameters,
        out MetadataOptionSetTarget target)
    {
        target = default;
        if (!parameters.TryGetValue("target", out var value) ||
            value is not MetadataOptionSetTarget candidate ||
            !Enum.IsDefined(candidate))
        {
            return false;
        }

        target = candidate;
        return true;
    }

    /// <summary>
    /// 將 caller-owned request 同步驗證並複製為 connector-owned operation。此步驟必須在第一次 await 前完成，
    /// 因此 admission queue、Pool wait 與 Data8 client 永遠不會保留原始 dictionary、JsonElement backing graph、
    /// legacy display name、endpoint、credential 或其他 caller state。只有明列的 Package01 read、contact basic-info、
    /// LINE profile write、ungrouped commitment function 與 P7.3 已封閉的 image/metadata/meeting capability 可通過；
    /// generic CRUD、QueryBase 與 caller-supplied FetchXML 仍一律 fail closed。
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
        var isMemberInfoPresentRecordRead = string.Equals(
            request.CapabilityOperationId,
            OperationIds.MemberInfoPresentRetrieveByContact,
            StringComparison.Ordinal);
        var isUngroupedCommitmentCount = string.Equals(
            request.CapabilityOperationId,
            OperationIds.MemberInfoContactCountUngroupedCommitment,
            StringComparison.Ordinal);
        var isSliceCOperation = IsSliceCOperation(request.CapabilityOperationId);
        var isSpecialResourceOperation = IsSpecialResourceOperation(request.CapabilityOperationId);
        if ((isContactBasicInfoUpdate || isContactLineProfileUpdate || isMemberInfoPresentRecordRead ||
             isUngroupedCommitmentCount || isSliceCOperation ||
             isSpecialResourceOperation) &&
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

        if ((isSliceCOperation || IsSpecialResourceImageWrite(request.CapabilityOperationId)) &&
            (!IsValidIdempotencyKey(request.IdempotencyKey) ||
             !(isSliceCOperation
                 ? HasValidSliceCParameters(request.CapabilityOperationId, parameters)
                 : HasValidSpecialResourceImagePayload(parameters))))
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
        if ((isContactLineProfileUpdate || isUngroupedCommitmentCount || isSliceCOperation || isSpecialResourceOperation) &&
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
            OperationIds.AuthenticationContactRetrieveByAccount => true,
            OperationIds.AuthenticationContactRetrieveByLineId => true,
            OperationIds.FeeDedicationRetrieveByContact => true,
            OperationIds.FeeDedicationRetrieveByContactDateRange => true,
            OperationIds.FeesRetrieveByDedicationPeriod => true,
            OperationIds.PaymentsDedicationRetrieveByContact => true,
            OperationIds.FeesEditorLoadByDiscipleLesson => true,
            OperationIds.LessonsStorRetrieveByContact => true,
            OperationIds.LessonsStorRetrieveByDiscipleLesson => true,
            OperationIds.ListCatalogRetrieveAppNamed => true,
            OperationIds.ListCatalogRetrieveAppNamedSmallGroups => true,
            OperationIds.ListMembershipRetrieveAppNamedByContact => true,
            OperationIds.MemberInfoContactUpdateBasicInfo => true,
            OperationIds.MemberInfoContactUpdateLineProfile => true,
            OperationIds.MemberInfoContactCountUngroupedCommitment => true,
            OperationIds.MemberInfoPresentRetrieveByContact => true,
            OperationIds.MemberInfoAuthorizationAssignmentResolveBySubject => true,
            OperationIds.ListMembersAddMany => true,
            OperationIds.ListMembersRemoveOne => true,
            OperationIds.ListManagementSmallGroupUpdateFields => true,
            OperationIds.ContactAssignOwner => true,
            OperationIds.NewPersonContactTransferBetweenLists => true,
            OperationIds.MemberInfoContactRetrieveImage => true,
            OperationIds.MemberInfoContactRetrieveImageDisplay => true,
            OperationIds.MemberInfoContactUpdateImage => true,
            OperationIds.NewPersonContactUpdateImage => true,
            OperationIds.MetadataOptionSetByAttribute => true,
            OperationIds.StatsMeetingRetrieveBySunday => true,
            _ => false
        };

    /// <summary>
    /// 判斷 operation 是否屬於 P7.3 的五項特殊資源 contract。這個小型 allowlist 僅用於 CE 9.1、idempotency
    /// 與 bounded envelope 規則選擇；它不啟用 ChurchReport 流量、不建立 cache，也不允許 generic image/metadata/page API。
    /// </summary>
    private static bool IsSpecialResourceOperation(string operationId)
        => operationId is
            OperationIds.MemberInfoContactRetrieveImage or
            OperationIds.MemberInfoContactRetrieveImageDisplay or
            OperationIds.MemberInfoContactUpdateImage or
            OperationIds.NewPersonContactUpdateImage or
            OperationIds.MetadataOptionSetByAttribute or
            OperationIds.StatsMeetingRetrieveBySunday;

    /// <summary>
    /// 判斷 operation 是否為兩個 image write 之一。讀取 image 無需冪等鍵；write 必須要求 caller 的 bounded key，
    /// 但 timeout/ambiguous outcome 仍不得自動重送，後續 CE evidence family 另行處理 reconciliation。
    /// </summary>
    private static bool IsSpecialResourceImageWrite(string operationId)
        => operationId is OperationIds.MemberInfoContactUpdateImage or OperationIds.NewPersonContactUpdateImage;

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
            "image-payload" => TryNormalizeImagePayload(source, out normalized),
            "metadata-optionset-target" => TryNormalizeMetadataOptionSetTarget(source, out normalized),
            _ => false
        };
    }

    /// <summary>
    /// 複製 P7.3 image payload 的閉合 bytes。此 executor 層只處理已由 typed ProductClient 或 Gateway JSON
    /// normalizer 提供的 <see cref="ContactImageResponseData"/>；不接受 Stream、IFormFile、JsonElement/object 或
    /// MIME string。bytes 上限低於 64 KiB Gateway wire cap，並在 connector 層再驗 magic/decoder/dimension/pixels。
    /// </summary>
    private static bool TryNormalizeImagePayload(object? source, out object? normalized)
    {
        normalized = null;
        if (source is not ContactImageResponseData image)
        {
            return false;
        }

        var bytes = image.GetImageBytes();
        if (!IsValidDecodedImage(bytes, image.MediaKind))
        {
            return false;
        }

        normalized = new ContactImageResponseData(bytes, image.MediaKind);
        return true;
    }

    /// <summary>
    /// 只接受封閉 metadata target enum，避免 entity/attribute string、OData path 或 locale 被 caller 當作路由 authority。
    /// 未知 enum 一律在 connector lease 前拒絕；真正 CRM logical name 只在 Data8 helper 的 private allowlist 出現。
    /// </summary>
    private static bool TryNormalizeMetadataOptionSetTarget(object? source, out object? normalized)
    {
        normalized = null;
        if (source is not MetadataOptionSetTarget target || !Enum.IsDefined(target))
        {
            return false;
        }

        normalized = target;
        return true;
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
            OperationIds.AuthenticationContactRetrieveByAccount => MaximumAuthenticationLookupCharacters,
            OperationIds.AuthenticationContactRetrieveByLineId => MaximumAuthenticationLookupCharacters,
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
            OperationIds.AuthenticationContactRetrieveByAccount => MaximumAuthenticationLookupBytes,
            OperationIds.AuthenticationContactRetrieveByLineId => MaximumAuthenticationLookupBytes,
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
    /// 驗證 executor-owned image copy 仍具非空 bytes 與合法 closed media kind。connector 在服務呼叫前還會驗證
    /// signature/decoder/dimension/pixels；這個 pre-admission check 的責任是排除任何 stream/object/空白 payload，
    /// 不讓它們進入 Pool、lease 或 WCF session。
    /// </summary>
    private static bool HasValidSpecialResourceImagePayload(IReadOnlyDictionary<string, object?> parameters)
        => parameters.TryGetValue("imagePayload", out var value) &&
           value is ContactImageResponseData image &&
           IsValidDecodedImage(image.GetImageBytes(), image.MediaKind);

    /// <summary>
    /// 在任何 admission permit、Data8 connector lease 或 CRM 寫入之前，以受控 decoder 驗證 P7.3 的 image
    /// payload。此方法只接受 bounded PNG/JPEG bytes，透過 ImageSharp 的 <c>DetectFormat</c> 與
    /// <c>Identify</c> 讀取真實格式與 dimensions，不信任 MIME、檔名、magic bytes 或呼叫端 enum。所有 decoder 物件與暫時
    /// metadata 都侷限在本同步呼叫範圍，不寫入 static/cache/profile/pool；因此無效或 image-bomb payload
    /// 會在外部 I/O 前 fail closed，不會耗用其他使用者或 profile 的 client、session、permit 或記憶體預算。
    /// </summary>
    /// <param name="bytes">由 typed boundary defensive-copy 取得、且尚未交給 connector 的有限內容。</param>
    /// <param name="declaredMediaKind">封閉 DTO 宣告的 media kind，必須與 decoder 真正格式一致。</param>
    /// <returns>格式、寬高、總像素與 byte 上限都符合 policy 時為 <see langword="true"/>。</returns>
    private static bool IsValidDecodedImage(byte[] bytes, ContactImageMediaKind declaredMediaKind)
    {
        if (bytes is null || bytes.Length is < 1 or > MaximumImagePayloadBytes ||
            !Enum.IsDefined(declaredMediaKind))
        {
            return false;
        }

        try
        {
            var decodedFormat = Image.DetectFormat(bytes);
            var info = Image.Identify(bytes);
            if (info is null || decodedFormat is null ||
                !TryMapDecodedImageFormat(decodedFormat, out var decodedMediaKind) ||
                decodedMediaKind != declaredMediaKind ||
                info.Width is < 1 or > MaximumImageWidthPixels ||
                info.Height is < 1 or > MaximumImageHeightPixels)
            {
                return false;
            }

            return checked((long)info.Width * info.Height) <= MaximumImagePixels;
        }
        catch (UnknownImageFormatException)
        {
            return false;
        }
        catch (InvalidImageContentException)
        {
            return false;
        }
        catch (ImageFormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// 將 decoder 的已驗證格式收斂為 P7.3 wire contract 的封閉 enum。以 format 名稱做大小寫不敏感的固定
    /// 比對，不接受 decoder 的任意 subtype、外掛格式或 MIME；若新格式被加入 ImageSharp，必須先擴充 DTO、
    /// policy 與測試，否則本方法維持 fail closed。
    /// </summary>
    /// <param name="decodedFormat">只在 <see cref="IsValidDecodedImage"/> scope 內存在的 decoder format。</param>
    /// <param name="mediaKind">成功時輸出的封閉 media kind。</param>
    /// <returns>只有 PNG/JPEG 時為 <see langword="true"/>。</returns>
    private static bool TryMapDecodedImageFormat(IImageFormat decodedFormat, out ContactImageMediaKind mediaKind)
    {
        ArgumentNullException.ThrowIfNull(decodedFormat);
        if (string.Equals(decodedFormat.Name, "PNG", StringComparison.OrdinalIgnoreCase))
        {
            mediaKind = ContactImageMediaKind.Png;
            return true;
        }

        if (string.Equals(decodedFormat.Name, "JPEG", StringComparison.OrdinalIgnoreCase))
        {
            mediaKind = ContactImageMediaKind.Jpeg;
            return true;
        }

        mediaKind = default;
        return false;
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
            : IsSpecialResourceOperation(operationId)
                ? MaximumSpecialResourceDispatchEnvelopeBytes
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
                    ContactImageResponseData image => checked(estimatedBytes + image.GetImageBytes().Length + 8),
                    MetadataOptionSetTarget => checked(estimatedBytes + 4),
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
            OperationResponseKind.Package01DedicationBookingRecords =>
                connectorData.DedicationBookingRecords is not null &&
                TryValidateDedicationBookingRecords(connectorData.DedicationBookingRecords, definition),
            OperationResponseKind.AppNamedListCatalogRecords =>
                connectorData.AppNamedListCatalogRecords is not null &&
                TryValidateAppNamedListCatalogRecords(connectorData.AppNamedListCatalogRecords, definition),
            OperationResponseKind.SmallGroupAppNamedListCatalogRecords =>
                connectorData.SmallGroupAppNamedListCatalogRecords is not null &&
                TryValidateSmallGroupAppNamedListCatalogRecords(
                    connectorData.SmallGroupAppNamedListCatalogRecords,
                    definition),
            OperationResponseKind.AppNamedMembershipRecords =>
                HasFixedAppNamedMembershipBounds(definition) &&
                connectorData.AppNamedMembershipRecords is not null &&
                TryValidateAppNamedMembershipRecords(
                    connectorData.AppNamedMembershipRecords,
                    definition),
            OperationResponseKind.AuthenticationContactReadRecords =>
                connectorData.AuthenticationContactReadRecords is not null &&
                TryValidateAuthenticationContactReadRecords(
                    connectorData.AuthenticationContactReadRecords,
                    definition),
            OperationResponseKind.MemberInfoPresentRecordReadRecords =>
                connectorData.MemberInfoPresentRecordReadRecords is not null &&
                TryValidateMemberInfoPresentRecordReadRecords(
                    connectorData.MemberInfoPresentRecordReadRecords,
                    definition),
            OperationResponseKind.MemberInfoAssignmentEvidence =>
                connectorData.MemberInfoAuthorizationAssignmentEvidence is not null &&
                TryValidateMemberInfoAuthorizationAssignmentEvidence(
                    operation,
                    connectorData.MemberInfoAuthorizationAssignmentEvidence,
                    definition),
            OperationResponseKind.ContactBasicInfoUpdate => connectorData.ContactBasicInfoUpdate is not null,
            OperationResponseKind.ContactLineProfileUpdate => connectorData.ContactLineProfileUpdate is not null,
            OperationResponseKind.UngroupedCommitmentCounts => connectorData.UngroupedCommitmentCounts is not null,
            OperationResponseKind.StaticListMembershipMutation => connectorData.StaticListMembershipMutation is not null,
            OperationResponseKind.SmallGroupFixedFieldsMutation => connectorData.SmallGroupFixedFieldsMutation is not null,
            OperationResponseKind.ContactOwnerAssignment => connectorData.ContactOwnerAssignment is not null,
            OperationResponseKind.ContactListTransfer => connectorData.ContactListTransfer is not null,
            OperationResponseKind.ContactImage => connectorData.ContactImage is not null &&
                                                  TryValidateContactImage(connectorData.ContactImage, definition),
            OperationResponseKind.ContactImageUpdate => connectorData.ContactImageUpdate is not null,
            OperationResponseKind.OptionSetOptions => connectorData.OptionSetOptions is not null &&
                                                        TryValidateOptionSetOptions(connectorData.OptionSetOptions, definition),
            OperationResponseKind.MeetingStatistics => connectorData.MeetingStatistics is not null &&
                                                        TryValidateMeetingStatistics(connectorData.MeetingStatistics, definition),
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
    /// 驗證 MemberInfo 指派證據與目前 connector operation 的 subject 精確相符。
    /// 這個檢查位於 lease 尚未釋放的 executor 邊界：若 response 屬於另一個使用者、mode 未定義、Church-wide
    /// 攜帶 list、ID 重複／空白或超過 registry 512 上限，呼叫端會 MarkFaulted 並淘汰 client，而不是只讓上層
    /// adapter 拒絕後將未知 transport/session 狀態放回 pool。方法只使用 request-local scalar/DTO，不保存
    /// evidence、profile、connector、lease、token 或快取資料，也不進行 retry 或 legacy fallback。
    /// </summary>
    /// <param name="operation">目前 lease 已執行、只允許單一 subjectContactId 的固定 operation。</param>
    /// <param name="evidence">connector 投影出的 immutable assignment evidence branch。</param>
    /// <param name="definition">server-owned registry 的固定 page 與 result bound。</param>
    /// <returns>response 可安全發布且 client health 仍可證明時為 <see langword="true"/>。</returns>
    private static bool TryValidateMemberInfoAuthorizationAssignmentEvidence(
        ConnectorOperation operation,
        MemberInfoAuthorizationAssignmentEvidenceResponseData evidence,
        OperationDefinition definition)
    {
        if (operation.Parameters is null ||
            operation.Parameters.Count != 1 ||
            !operation.Parameters.TryGetValue("subjectContactId", out var subjectValue) ||
            subjectValue is not Guid requestedSubjectId ||
            requestedSubjectId == Guid.Empty ||
            evidence.SubjectContactId != requestedSubjectId ||
            !Enum.IsDefined(evidence.AccessMode) ||
            definition.MaximumPageCount != 1 ||
            definition.MaximumResultItemCount != 512 ||
            evidence.AssignedListIds.Count > definition.MaximumResultItemCount)
        {
            return false;
        }

        if (evidence.AccessMode == MemberInfoAuthorizationAssignmentAccessMode.ChurchWide)
        {
            return evidence.AssignedListIds.Count == 0;
        }

        var uniqueListIds = new HashSet<Guid>();
        foreach (var listId in evidence.AssignedListIds)
        {
            if (listId == Guid.Empty || !uniqueListIds.Add(listId))
            {
                return false;
            }
        }

        // 空集合是有效的 zero-assignment response，後續 resolver 會發布空 allowlist，絕不回退到 legacy state。
        return true;
    }

    /// <summary>
    /// 驗證 registry 沒有把 ORG-CALL-00057 的封閉單頁資源邊界放寬。
    ///
    /// Data8 query 與 wire response 都固定為一頁、32 rows、32 KiB；即使未來測試替身、registry edit 或 adapter
    /// 意外提供較大的宣告，本 executor 仍在 lease scope 內 fail closed，而不讓過大結果進入產品或使未驗證 client
    /// 被當作健康。此 helper 只讀 immutable definition，不配置或保留任何 request/profile/session 資源。
    /// </summary>
    /// <param name="definition">目前 operation 的 server-owned immutable registry definition。</param>
    /// <returns>definition 精確維持既定單頁、列數與位元組上限時為 <see langword="true"/>。</returns>
    private static bool HasFixedAppNamedMembershipBounds(OperationDefinition definition)
        => definition.MaximumPageCount == 1 &&
           definition.MaximumPageBytes == MaximumAppNamedMembershipResponseBytes &&
           definition.MaximumCumulativeResponseBytes == MaximumAppNamedMembershipResponseBytes &&
           definition.MaximumResultItemCount == MaximumAppNamedMembershipRecords;

    /// <summary>
    /// 驗證 ORG-CALL-00055／00056 的封閉 authentication contact response branch。
    /// 上游 connector 只能交付最多兩筆 immutable wire record；兩筆是刻意保留 ambiguous 語意的硬上限，不能
    /// 因產品目前只處理一筆而縮成一筆。每筆必須具有非空 contact ID、非空 account locator、非空顯示名稱與
    /// active 狀態，並在嚴格 UTF-8 累積預算內。任何不符都在 lease scope 內回傳 false，使呼叫端 MarkFaulted
    /// 並淘汰健康狀態未知的 client；不會發布部分資料、快取資料或重試另一個 connector。
    /// </summary>
    /// <param name="records">connector 在目前 request scope 投影的 authentication contact records。</param>
    /// <param name="definition">server-owned registry 的固定 operation 定義與回應預算。</param>
    /// <returns>所有記錄皆符合 non-secret、bounded 和 active 契約時為 <see langword="true"/>。</returns>
    private static bool TryValidateAuthenticationContactReadRecords(
        IReadOnlyList<AuthenticationContactReadRecord> records,
        OperationDefinition definition)
    {
        if (records.Count > MaximumAuthenticationContactRecords ||
            records.Count > definition.MaximumResultItemCount)
        {
            return false;
        }

        var bytes = 0;
        foreach (var record in records)
        {
            if (record is null ||
                record.ContactId == Guid.Empty ||
                !record.IsActive ||
                string.IsNullOrWhiteSpace(record.AccountLocator) ||
                string.IsNullOrWhiteSpace(record.DisplayName) ||
                !TryAddFixedBytes(ref bytes, 96, definition.MaximumCumulativeResponseBytes) ||
                !TryAddBoundedAuthenticationContactText(
                    ref bytes,
                    record.AccountLocator,
                    definition.MaximumCumulativeResponseBytes) ||
                !TryAddBoundedAuthenticationContactText(
                    ref bytes,
                    record.DisplayName,
                    definition.MaximumCumulativeResponseBytes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 驗證 ORG-CALL-00026 connector response 在 lease 離開前仍是唯一、有限的純量列。每筆需有非空且唯一的
    /// record GUID 與非空 fullname；fullname 與說明皆受嚴格 UTF-8 限制，日期保持 legacy DateTime 原值而不在 executor
    /// 轉換時區。任何 MoreRecords/schema failure 從 Data8 helper 到達此處前都已中止；本層仍重驗證以防未來 adapter
    /// 或測試替身繞過 helper 後將不受限 response 傳到 ProductClient。
    /// </summary>
    /// <param name="records">目前 request scope 建立的 immutable present-record rows。</param>
    /// <param name="definition">server-owned registry 定義的 result 與 response byte policy。</param>
    /// <returns>全部 rows 符合 unique ID、文字與累積 byte policy 時為 true。</returns>
    private static bool TryValidateMemberInfoPresentRecordReadRecords(
        IReadOnlyList<MemberInfoPresentRecordReadRecord> records,
        OperationDefinition definition)
    {
        if (records.Count > MaximumAppNamedMembershipRecords ||
            records.Count > definition.MaximumResultItemCount)
        {
            return false;
        }

        var identifiers = new HashSet<Guid>();
        var bytes = 0;
        foreach (var record in records)
        {
            if (record is null ||
                record.PresentRecordId == Guid.Empty ||
                !identifiers.Add(record.PresentRecordId) ||
                string.IsNullOrWhiteSpace(record.ContactFullName) ||
                !TryAddFixedBytes(ref bytes, MaximumMemberInfoPresentRecordFixedBytes, definition.MaximumCumulativeResponseBytes) ||
                !TryAddBoundedMemberInfoPresentRecordText(
                    ref bytes,
                    record.ContactFullName,
                    definition.MaximumCumulativeResponseBytes) ||
                !TryAddBoundedMemberInfoPresentRecordText(
                    ref bytes,
                    record.PrayItem,
                    definition.MaximumCumulativeResponseBytes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 對 legacy display text 累積嚴格 UTF-8 bytes。代禱文字可為 null，但 fullname 必須由呼叫端先證明非空；非 null
    /// 文字不可超過 helper/union 的 512 字元與 2 KiB byte 上限，不接受替代字元或未驗證的 object conversion。
    /// </summary>
    /// <param name="total">目前 request-local cumulative response bytes。</param>
    /// <param name="value">nullable text scalar。</param>
    /// <param name="maximumBytes">registry response byte cap。</param>
    /// <returns>值合法且累積值仍在上限內時為 true。</returns>
    private static bool TryAddBoundedMemberInfoPresentRecordText(
        ref int total,
        string? value,
        int maximumBytes)
    {
        if (value is null)
        {
            return true;
        }

        if (value.Length > 512)
        {
            return false;
        }

        try
        {
            var valueBytes = StrictUtf8.GetByteCount(value);
            return valueBytes <= MaximumMemberInfoPresentRecordTextBytes &&
                   TryAddFixedBytes(ref total, valueBytes, maximumBytes);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// 將單一公開 authentication contact scalar 納入嚴格 UTF-8 回應預算。
    /// 此 helper 不會 trim、正規化或保留文字；它只在目前驗證呼叫中計數，以拒絕上游 schema 漂移造成的長字串
    /// retention。無效 UTF-16、空白、超過單欄上限或 cumulative budget 都回傳 false，保持 fail-closed。
    /// </summary>
    /// <param name="total">目前 response 的 request-local 累積 byte 計數。</param>
    /// <param name="value">已 allowlisted 的 account locator 或 display name。</param>
    /// <param name="maximumBytes">registry 宣告的累積回應上限。</param>
    /// <returns>文字與累積大小都符合契約時為 <see langword="true"/>。</returns>
    private static bool TryAddBoundedAuthenticationContactText(
        ref int total,
        string value,
        int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var valueBytes = StrictUtf8.GetByteCount(value);
            return valueBytes <= MaximumAuthenticationContactResponseTextBytes &&
                   TryAddFixedBytes(ref total, valueBytes, maximumBytes);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
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
    /// 驗證認獻單 read branch 在離開 Data8 lease 前仍符合 registry 的筆數與 UTF-8 累積位元組上限。每筆只接受
    /// 有效 GUID 與 allowlisted scalar；此驗證不保留 collection、CRM Entity、profile 或 connector 的參考，
    /// 因此 A/B 交錯呼叫只能取得各自 envelope 的 request-local snapshot。任何不完整或超限回應都回傳
    /// <see langword="false"/>，使上層依既有 fault/eviction 流程 fail closed，而非發布部分認獻資料。
    /// </summary>
    private static bool TryValidateDedicationBookingRecords(
        IReadOnlyList<Package01DedicationBookingRecord> records,
        OperationDefinition definition)
    {
        if (records.Count > definition.MaximumResultItemCount)
        {
            return false;
        }

        var bytes = 0;
        foreach (var record in records)
        {
            if (record is null || record.DedicationBookingId is not { } dedicationBookingId ||
                dedicationBookingId == Guid.Empty ||
                record.DedicationBookingStatusOption != 100000001 ||
                !TryAddFixedBytes(ref bytes, 256, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.DedicationCategoryLabel, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.DedicationBookingStatusLabel, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.TotalStages, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.PaidPeriod, definition.MaximumCumulativeResponseBytes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 驗證 App-named catalog response 在 connector lease 離開前只包含有界的 allowlisted scalar。每一列需具有效
    /// list GUID，日期若存在則必須是零 offset UTC；名稱與 purpose 以嚴格 UTF-8 加入 registry cumulative
    /// budget。任何 null、超限或非 UTC 值都使整個 response 被拒絕，讓上層標記 lease faulted，絕不發布 partial
    /// catalog 或把 CRM Entity、cookie、profile、session、cache 與 transport state 留給下一個請求。
    /// </summary>
    /// <param name="records">connector 在目前 request scope 建立的 immutable catalog rows。</param>
    /// <param name="definition">registry 提供的固定列數與 byte 上限。</param>
    /// <returns>所有列都符合封閉契約與 cumulative budget 時為 <see langword="true"/>。</returns>
    private static bool TryValidateAppNamedListCatalogRecords(
        IReadOnlyList<AppNamedListCatalogRecord> records,
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
                record.ListId == Guid.Empty ||
                (record.LastUsedOn is { Offset: var offset } && offset != TimeSpan.Zero) ||
                (record.Purpose is not null && !string.Equals(record.Purpose, "小組名單", StringComparison.Ordinal)) ||
                !TryAddFixedBytes(ref bytes, 128, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.ListName, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.Purpose, definition.MaximumCumulativeResponseBytes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 驗證 ORG-CALL-00065 的小組 App 點名名單 response 仍符合封閉 branch、row count、UTC、purpose、leader identity
    /// 與 registry cumulative byte budget。executor 對 connector、測試替身與未來 adapter 再次執行此防禦，確保任何
    /// 不可信資料都會在 lease scope 內被拒絕並由外層標記 client faulted，而不是發布已驗證前幾列的 partial response。
    ///
    /// 這裡只巡覽 immutable pure-value record，不保留 CRM Entity、EntityReference、formatted values、paging cookie、
    /// profile、session、cache、stream 或 connector。每一 request 都使用新的 local byte counter，因此 A/B profile 或
    /// workload 的 interleaved response 無法交叉累積、重用或洩漏 leader／名單資料。
    /// </summary>
    /// <param name="records">connector 在目前 request scope 投影後的 immutable 小組目錄 rows。</param>
    /// <param name="definition">server-owned registry 的固定列數與 byte policy。</param>
    /// <returns>所有 rows 都符合完整封閉契約時為 <see langword="true"/>。</returns>
    private static bool TryValidateSmallGroupAppNamedListCatalogRecords(
        IReadOnlyList<SmallGroupAppNamedListCatalogRecord> records,
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
                record.ListId == Guid.Empty ||
                (record.LastUsedOn is { Offset: var offset } && offset != TimeSpan.Zero) ||
                (record.Purpose is not null && !string.Equals(record.Purpose, "小組名單", StringComparison.Ordinal)) ||
                (record.RaceLeaderContactId is { } raceLeaderId && raceLeaderId == Guid.Empty) ||
                (record.FamilyLeaderContactId is { } familyLeaderId && familyLeaderId == Guid.Empty) ||
                !TryAddFixedBytes(ref bytes, 192, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.ListName, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.Purpose, definition.MaximumCumulativeResponseBytes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 驗證 ORG-CALL-00057 的封閉 App-named membership response branch。
    ///
    /// connector、測試替身或未來 adapter 交出的 row 都必須是非空、具有唯一非空 list GUID，且僅以 list name
    /// 的嚴格 UTF-8 scalar bytes 與 GUID 固定成本計入 registry 的 32 KiB 上限。HashSet、計數器與 records
    /// 巡覽都只存在本次 lease scope，沒有 cache、session、profile 或 mutable static state；任一超限、重複、
    /// 無效 UTF-16 或 schema 異常都回傳 false，使 caller 在 lease Dispose 前 MarkFaulted，而非發布 partial data
    /// 或讓不可信 client 回到 pool。
    /// </summary>
    /// <param name="records">connector 已建立、預計交付的 immutable membership pure-value rows。</param>
    /// <param name="definition">registry 宣告的固定列數與 cumulative response byte policy。</param>
    /// <returns>所有 rows 都符合 identity、唯一性與固定 budget 時為 <see langword="true"/>。</returns>
    private static bool TryValidateAppNamedMembershipRecords(
        IReadOnlyList<AppNamedMembershipRecord> records,
        OperationDefinition definition)
    {
        if (records.Count > definition.MaximumResultItemCount)
        {
            return false;
        }

        var listIds = new HashSet<Guid>();
        var bytes = 0;
        foreach (var record in records)
        {
            if (record is null ||
                record.ListId == Guid.Empty ||
                !listIds.Add(record.ListId) ||
                !TryAddFixedBytes(ref bytes, 32, MaximumAppNamedMembershipResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, record.ListName, MaximumAppNamedMembershipResponseBytes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 驗證 image response 不僅符合 registry byte budget，也必須由實際 decoder 識別為與封閉 enum 一致的 PNG/JPEG，
    /// 並符合 dimensions/pixels policy。即使 connector 已有同樣的深度防禦，executor 仍要防禦測試替身、未來 adapter
    /// 或已損毀 connector 回應；bytes getter 只產生本同步驗證用的短生命週期 copy，不保存 mutable array、stream、decoder
    /// 或 image metadata。任何不符皆由外層在 lease scope 標記 faulted，讓未知 session/client 不會回到同 profile/generation pool。
    /// </summary>
    private static bool TryValidateContactImage(ContactImageResponseData image, OperationDefinition definition)
    {
        var bytes = image.GetImageBytes();
        return bytes.Length <= definition.MaximumCumulativeResponseBytes &&
               IsValidDecodedImage(bytes, image.MediaKind);
    }

    /// <summary>
    /// 驗證 metadata records 仍符合 registry 的 row/UTF-8 cumulative budget。collection 是 envelope copy，HashSet
    /// 僅存在此呼叫；不建立 profile/generation cache 或保留 raw metadata/session 物件。
    /// </summary>
    private static bool TryValidateOptionSetOptions(
        IReadOnlyList<OptionSetOptionRecord> options,
        OperationDefinition definition)
    {
        if (options.Count > definition.MaximumResultItemCount)
        {
            return false;
        }

        var bytes = 0;
        foreach (var option in options)
        {
            if (option is null || string.IsNullOrWhiteSpace(option.Label) || option.ConfiguredOrder < 0 ||
                !TryAddFixedBytes(ref bytes, 32, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, option.Label, definition.MaximumCumulativeResponseBytes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 驗證 meeting projection 的 row/identity/name bytes。paging cookie、page EntityCollection 與 query 永遠不在此
    /// collection 中；connector 發現 page failure 或 budget overflow 時不會建立 envelope，因而沒有 partial success。
    /// </summary>
    private static bool TryValidateMeetingStatistics(
        IReadOnlyList<MeetingStatisticRecord> statistics,
        OperationDefinition definition)
    {
        if (statistics.Count > definition.MaximumResultItemCount)
        {
            return false;
        }

        var bytes = 0;
        foreach (var statistic in statistics)
        {
            if (statistic is null || statistic.MeetingStatisticId == Guid.Empty ||
                !TryAddFixedBytes(ref bytes, 64, definition.MaximumCumulativeResponseBytes) ||
                !TryAddUtf8Bytes(ref bytes, statistic.Name, definition.MaximumCumulativeResponseBytes))
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
