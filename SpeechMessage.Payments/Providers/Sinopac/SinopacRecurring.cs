/*
 * 檔案整體說明
 *
 * 目的與定位：實作永豐定期扣款建立、RegularQuery 查詢與停止 capability。
 * 建立一律強制 PayTypeSub=REGULAR；查詢以 DeductNum 驗證已扣款期數的 Info 數量及連續期間，
 * 並依主單與每筆扣款的穩定識別建立完整候選集合；所有條件通過後才發布唯讀快照。
 *
 * 主要功能與流程：
 * 定期建立驗證期數、週期與頻率後委派既有一次性 OrderCreate 封包流程。
 * RegularQuery 回應的外層簽章不涵蓋巢狀 Order/Info；巢狀值只視為由 HTTPS 傳輸的資料，
 * 以 profile ShopNo、request MasterOrderNo 與穩定 row identity 交叉核對，絕不虛稱巢狀內容已驗簽。
 * 空白/重複識別、超出有界列數、回應不匹配、拒絕、逾時或傳輸失敗均 fail closed，不發布部分 Charges。
 *
 * 隔離與資源生命週期：
 * profile、候選集合與回應只屬單次操作；每個結果列是 detached immutable value，最終 Charges 為唯讀副本。
 * 建立付款只在單次操作委派給共同 provider，不保存 request 或跨請求狀態。HttpClient 由 Host/DI 擁有，
 * 呼叫端取消傳入 Nonce/Order，request/response 由共同 helper Dispose。測試使用合成資料與記憶體 handler，
 * 不連線永豐、正式金流或組織。
 */
using System.Globalization;
using System.Security.Cryptography;
using Newtonsoft.Json;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Sinopac;

/// <summary>永豐 provider 的 recurring capability partial；不持有跨請求方案或扣款集合。</summary>
internal sealed partial class SinopacPaymentProvider
{
    private const int MaximumRecurringChargeCount = 1000;

    /// <summary>
    /// 以既有 Money-first OrderCreate 建立定期扣款；此入口強制 REGULAR 並複製中繼資料，
    /// 不修改呼叫端 request，也不接受缺少或非正整數排程值的請求。
    /// </summary>
    /// <param name="profile">Gateway 本次解析的永豐 profile。</param>
    /// <param name="request">一般付款模型承載的產品主單及定期排程欄位。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，傳遞至 provider HTTP I/O。</param>
    /// <returns>定期付款頁的中立建立結果；建立頁面不代表已扣款。</returns>
    public async Task<RecurringResult> CreateRecurringAsync(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsMatchingProfile(profile, request.ProfileName) ||
            !IsValidIdentifier(request.ProductOrderId) ||
            !TryGetPositiveScheduleValue(request.Metadata, "DeductTotalNum", out _) ||
            !TryGetPositiveScheduleValue(request.Metadata, "DeductFreq", out _) ||
            !request.Metadata.TryGetValue("PeriodType", out var periodType) ||
            string.IsNullOrWhiteSpace(periodType) ||
            periodType.Length > 16)
        {
            return CreateRecurringFailure(
                request.ProductOrderId,
                PaymentErrorKind.RequestInvalid,
                "RecurringRequestInvalid");
        }

        PaymentCreateRequest detachedRequest;
        try
        {
            var metadata = request.Metadata.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal);
            detachedRequest = request with
            {
                PaymentMethodSubType = "REGULAR",
                Metadata = metadata
            };
        }
        catch (ArgumentException)
        {
            return CreateRecurringFailure(
                request.ProductOrderId,
                PaymentErrorKind.RequestInvalid,
                "RecurringRequestInvalid");
        }

        var result = await CreatePaymentAsync(profile, detachedRequest, cancellationToken);
        return new RecurringResult
        {
            Status = result.Status,
            MasterOrderNo = request.ProductOrderId,
            ProviderOrderRef = result.ProviderOrderRef,
            PaymentPageUrl = result.PaymentPageUrl,
            Error = result.Error
        };
    }

    /// <summary>
    /// 以 RegularQuery 讀取單一定期主單。完成回應 scope 與所有 row identity 檢查前，
    /// 候選列只存在於本次呼叫；任何衝突都回傳空集合。
    /// </summary>
    /// <param name="profile">Gateway 本次解析的永豐 profile。</param>
    /// <param name="request">包含穩定定期主單識別的中立查詢資料。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，傳遞至 Nonce 與 Order I/O。</param>
    /// <returns>完整唯讀的扣款列快照，或不含部分資料的失敗結果。</returns>
    public async Task<RecurringResult> QueryRecurringAsync(
        PaymentMerchantProfile profile,
        RecurringQueryRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsMatchingProfile(profile, request.ProfileName) ||
            !IsValidIdentifier(request.MasterOrderNo))
        {
            return CreateRecurringFailure(
                request.MasterOrderNo,
                PaymentErrorKind.RequestInvalid,
                "RecurringQueryRequestInvalid");
        }

        try
        {
            var shopNo = SinopacRequestMapper.GetRequiredCredential(profile, "ShopNo");
            var response = await SendOrderAsync<
                SinopacRegularQueryRequest,
                SinopacRegularQueryResponse>(
                    profile,
                    new SinopacRegularQueryRequest
                    {
                        ShopNo = shopNo,
                        MasterOrderNo = request.MasterOrderNo
                    },
                    SinopacApiService.RegularQuery,
                    cancellationToken);

            var order = response.Order;
            // RegularQuery 的 Info 描述已扣款成功或失敗子單；DeductNum 與列數相符，且每個期數落在
            // 1..DeductNum，配合下方唯一性檢查才能保證完整集合，避免發布缺期或未扣期資料。
            if (!SinopacStatusMapper.IsSuccessStatus(response.Status) ||
                !string.Equals(response.ShopNo, shopNo, StringComparison.Ordinal) ||
                order is null ||
                !string.Equals(order.MasterOrderNo, request.MasterOrderNo, StringComparison.Ordinal) ||
                order.DeductTotalNum is < 1 or > MaximumRecurringChargeCount ||
                order.DeductNum < 0 ||
                order.DeductNum > order.DeductTotalNum ||
                order.Info is null ||
                order.Info.Count > MaximumRecurringChargeCount ||
                order.Info.Count > order.DeductTotalNum ||
                order.Info.Count != order.DeductNum)
            {
                return CreateRecurringFailure(
                    request.MasterOrderNo,
                    PaymentErrorKind.SignatureInvalid,
                    "RegularQueryResponseMismatch");
            }

            var candidate = new List<RecurringChargeResult>(order.Info.Count);
            var orderNumbers = new HashSet<string>(StringComparer.Ordinal);
            var chargeNumbers = new HashSet<int>();
            foreach (var charge in order.Info)
            {
                if (charge is null ||
                    !IsValidIdentifier(charge.OrderNo) ||
                    charge.RegularNum is < 1 ||
                    charge.RegularNum > order.DeductNum ||
                    !orderNumbers.Add(charge.OrderNo) ||
                    !chargeNumbers.Add(charge.RegularNum))
                {
                    return CreateRecurringFailure(
                        request.MasterOrderNo,
                        PaymentErrorKind.SerializationFailure,
                        "RegularQueryIdentityConflict");
                }

                candidate.Add(new RecurringChargeResult
                {
                    Number = charge.RegularNum,
                    OrderNo = charge.OrderNo,
                    DeductTime = charge.DeductTime,
                    DeductStatus = charge.DeductStatus
                });
            }

            return new RecurringResult
            {
                Status = PaymentStatus.Pending,
                MasterOrderNo = request.MasterOrderNo,
                ProviderOrderRef = order.MasterOrderNo,
                RecurringStatus = order.RegularStatus,
                Charges = Array.AsReadOnly(candidate.ToArray())
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PaymentConfigurationException)
        {
            return CreateRecurringFailure(
                request.MasterOrderNo,
                PaymentErrorKind.ConfigurationInvalid,
                "ConfigurationInvalid");
        }
        catch (SinopacRequestException exception)
        {
            return CreateRecurringFailure(
                request.MasterOrderNo,
                exception.ErrorKind,
                exception.DiagnosticCode);
        }
        catch (SinopacSignatureException)
        {
            return CreateRecurringFailure(
                request.MasterOrderNo,
                PaymentErrorKind.SignatureInvalid,
                "SignatureInvalid");
        }
        catch (OperationCanceledException)
        {
            return CreateRecurringFailure(
                request.MasterOrderNo,
                PaymentErrorKind.NetworkFailure,
                "RegularQueryTimeout");
        }
        catch (Exception exception) when (IsNetworkTransportException(exception))
        {
            return CreateRecurringFailure(
                request.MasterOrderNo,
                PaymentErrorKind.NetworkFailure,
                "RegularQueryTransport");
        }
        catch (Exception exception) when (
            exception is JsonException or CryptographicException or FormatException or OverflowException)
        {
            return CreateRecurringFailure(
                request.MasterOrderNo,
                PaymentErrorKind.SerializationFailure,
                "RegularQueryResponseUnreadable");
        }
    }

    /// <summary>停止定期扣款僅使用 OrderMaintain 固定 E 命令，不接受呼叫端命令欄位。</summary>
    /// <param name="profile">Gateway 本次解析的永豐 profile。</param>
    /// <param name="request">含穩定定期主單識別的中立維護資料。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，傳遞至 provider HTTP I/O。</param>
    /// <returns>銀行確認停止後為 Cancelled。</returns>
    public Task<PaymentStatusResult> StopRecurringAsync(
        PaymentMerchantProfile profile,
        OrderMaintenanceRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteOrderMaintenanceAsync(profile, request, "E", PaymentStatus.Cancelled, cancellationToken);
    }

    private static bool TryGetPositiveScheduleValue(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        out int value)
    {
        value = 0;
        return metadata.TryGetValue(key, out var text) &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value is > 0 and <= MaximumRecurringChargeCount;
    }

    /// <summary>建立不含 provider response、憑證或部分扣款列的定期操作失敗結果。</summary>
    /// <param name="masterOrderNo">本次操作的穩定主單識別。</param>
    /// <param name="kind">標準化錯誤分類。</param>
    /// <param name="code">固定或共同傳輸 helper 產生的安全診斷碼。</param>
    /// <returns>狀態固定為 Failed 且 Charges 為空的結果。</returns>
    private static RecurringResult CreateRecurringFailure(
        string masterOrderNo,
        PaymentErrorKind kind,
        string code)
    {
        return new RecurringResult
        {
            Status = PaymentStatus.Failed,
            MasterOrderNo = masterOrderNo,
            Charges = Array.Empty<RecurringChargeResult>(),
            Error = new PaymentError
            {
                Kind = kind,
                Code = code,
                Message = code
            }
        };
    }
}
