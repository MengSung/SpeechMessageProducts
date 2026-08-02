using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 定義 Package01 fee 日期區間讀取在官方 Worker 邊界上的唯一 SDK-free contract。
/// 此型別只驗證並正規化不可變的 <see cref="WorkerValue"/>；它不保存 Session、認證、端點、
/// CRM SDK client 或任何跨要求狀態。所有大小限制均先完整驗證再接受，絕不截斷頁、列或欄位。
/// </summary>
public static class Package01FeeWorkerContract
{
    /// <summary>官方 Worker 可執行的固定 Package01 fee operation ID。</summary>
    public const string CapabilityOperationId =
        "fee.dedication.retrieve.by.contact.date.range";

    /// <summary>與 server-owned operation definition 完全綁定的不可變 revision。</summary>
    public const string OperationDefinitionRevision =
        "bc1a869cd97c03a1f9a7e9b6ea4ace8dc1ea10b1bd96d5066cf0ec1e6c88f138";

    /// <summary>單一結果最多允許的 page 數。</summary>
    public const int MaximumPageCount = 4;

    /// <summary>單一 page 最多允許的 row 數。</summary>
    public const int MaximumRowsPerPage = 400;

    /// <summary>完整結果最多允許的 row 總數。</summary>
    public const int MaximumTotalRows = 1_600;

    /// <summary>單一 row 的固定欄位數。</summary>
    public const int ResultColumnCount = 10;

    /// <summary>單一 page 的 canonical wire bytes 上限。</summary>
    public const int MaximumCanonicalPageBytes = 64 * 1024;

    /// <summary>完整結果所有 page 的 canonical wire bytes 上限。</summary>
    public const int MaximumCanonicalResultBytes = 256 * 1024;

    /// <summary>
    /// 供 Supervisor 與 Worker 共用的唯一 protocol limits。
    /// 17,604 由 4 個 page item、1,600 個 row item 與 16,000 個 cell item 組成；
    /// singleton 僅含不可變 scalar，不持有 buffer、stream、timer 或 request reference。
    /// </summary>
    public static WorkerProtocolLimits ProtocolLimits { get; } = new WorkerProtocolLimits(
        maximumFrameBytes: WorkerFrameCodec.DefaultMaximumFrameBytes,
        maximumValueDepth: 8,
        maximumObjectMembers: 64,
        maximumArrayItems: 17_604,
        maximumStringUtf8Bytes: 16 * 1024,
        maximumIdentifierUtf8Bytes: 128);

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>
    /// 驗證 Package01 request metadata 與參數，並在交給 CRM adapter 前移除 optional contactName。
    /// contactName 僅接受 String 或 Null；其內容永遠不會出現在回傳 request，因此不能影響 routing、
    /// query、authorization、cache key 或 CRM SDK 呼叫。若 shape、kind、必要欄位、GUID 或日期順序
    /// 不合法，會以 <see cref="WorkerProtocolException"/> fail closed，且不建立外部資源。
    /// </summary>
    /// <param name="request">已由 frame codec 建立的不可變 Worker request snapshot。</param>
    /// <returns>僅保留 contactId、startDate 與 endDate 的執行用 request。</returns>
    public static WorkerRequestV1 ValidateAndNormalizeRequest(WorkerRequestV1 request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        WorkerEnvelopeValidator.ValidateRequest(request, ProtocolLimits);
        if (!string.Equals(
                request.CapabilityOperationId,
                CapabilityOperationId,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.OperationDefinitionRevision,
                OperationDefinitionRevision,
                StringComparison.Ordinal))
        {
            throw InvalidEnvelope("The Package01 fee worker operation identity is invalid.");
        }

        var normalizedParameters = ValidateAndNormalizeParameters(request.Parameters);
        if (ReferenceEquals(normalizedParameters, request.Parameters))
        {
            return request;
        }

        return new WorkerRequestV1(
            request.ProtocolVersion,
            request.ProcessNonce,
            request.RequestId,
            request.ProfileGenerationId,
            request.OperationDefinitionRevision,
            request.CapabilityOperationId,
            request.DeadlineUtcTicks,
            normalizedParameters);
    }

    /// <summary>
    /// 驗證 Supervisor 與 Worker 共用的 Package01 typed parameter snapshot，並在 optional
    /// <c>contactName</c> 存在時回傳只含 CRM query 所需三欄的新 dictionary。方法不保存 caller
    /// collection，也不建立 Session、Credential、Client 或背景資源；沒有相容性欄位時直接回傳原 snapshot。
    /// </summary>
    /// <param name="parameters">已由 bounded scalar 轉換建立的單次要求參數。</param>
    /// <returns>精確包含 contactId、startDate、endDate 的 validated parameter snapshot。</returns>
    public static IReadOnlyDictionary<string, WorkerValue> ValidateAndNormalizeParameters(
        IReadOnlyDictionary<string, WorkerValue> parameters)
    {
        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        if (parameters.Count is < 3 or > 4 ||
            !parameters.TryGetValue("contactId", out var contactIdValue) ||
            !parameters.TryGetValue("startDate", out var startDateValue) ||
            !parameters.TryGetValue("endDate", out var endDateValue))
        {
            throw InvalidEnvelope("The Package01 fee worker request shape is invalid.");
        }

        foreach (var parameterName in parameters.Keys)
        {
            if (parameterName is not "contactId" and
                not "startDate" and
                not "endDate" and
                not "contactName")
            {
                throw InvalidEnvelope("The Package01 fee worker request contains an unknown member.");
            }

            // Supervisor 可直接使用 parameter-only 入口，因此每個 scalar 仍須先通過與完整
            // request 相同的 wire shape／UTF-8 byte limits；optional contactName 不能因稍後被丟棄
            // 就繞過上限，讓超大 caller string 在 admission 前被保留。
            WorkerEnvelopeValidator.ValidateStandaloneValue(
                parameters[parameterName],
                ProtocolLimits);
        }

        var contactId = ReadRequiredGuid(contactIdValue, "contactId");
        var startDate = ReadRequiredUtcDateTime(startDateValue, "startDate");
        var endDate = ReadRequiredUtcDateTime(endDateValue, "endDate");
        if (startDate > endDate)
        {
            throw InvalidEnvelope("The Package01 fee worker date range is invalid.");
        }

        if (parameters.TryGetValue("contactName", out var contactName) &&
            contactName.Kind is not WorkerValueKind.Null and not WorkerValueKind.String)
        {
            throw InvalidEnvelope("The Package01 fee worker contactName kind is invalid.");
        }

        if (!parameters.ContainsKey("contactName"))
        {
            return parameters;
        }

        // 只在 optional compatibility 欄位存在時配置三欄 dictionary；移除後的 request
        // 是 CRM adapter 唯一可見的 snapshot，避免舊 contactName 成為查詢或權限輸入。
        return new Dictionary<string, WorkerValue>(3, StringComparer.Ordinal)
        {
            ["contactId"] = WorkerValue.FromGuid(contactId),
            ["startDate"] = WorkerValue.FromUtcDateTime(startDate),
            ["endDate"] = WorkerValue.FromUtcDateTime(endDate)
        };
    }

    /// <summary>
    /// 驗證 Package01 fee 結果的完整 page/row/cell shape、nullable 規則與所有數量／byte 上限。
    /// malformed shape 以 <see cref="WorkerProtocolFailureCategory.InvalidEnvelope"/> 回報；
    /// page、row、總列數、array item 或 canonical byte overflow 則以
    /// <see cref="WorkerProtocolFailureCategory.EnvelopeLimitExceeded"/> 回報，供 WorkerHost 映射成
    /// 固定 result-too-large 例外。方法不修改輸入，也不產生部分結果。
    /// </summary>
    /// <param name="result">CRM adapter 投影出的 SDK-free Array&lt;Page&gt; 結果。</param>
    public static void ValidateResult(WorkerValue result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (!HasArrayShape(result))
        {
            throw InvalidEnvelope("The Package01 fee worker result root must be an array.");
        }

        var pages = result.Items!;
        if (pages.Count > MaximumPageCount)
        {
            throw LimitExceeded("The Package01 fee worker page limit was exceeded.");
        }

        var totalRows = 0;
        var cumulativeCanonicalBytes = 0;
        foreach (var page in pages)
        {
            if (!HasArrayShape(page))
            {
                throw InvalidEnvelope("The Package01 fee worker page must be an array.");
            }

            var rows = page.Items!;
            if (rows.Count > MaximumRowsPerPage)
            {
                throw LimitExceeded("The Package01 fee worker rows-per-page limit was exceeded.");
            }

            totalRows = checked(totalRows + rows.Count);
            if (totalRows > MaximumTotalRows)
            {
                throw LimitExceeded("The Package01 fee worker total row limit was exceeded.");
            }

            foreach (var row in rows)
            {
                ValidateRow(row);
            }
        }

        // 先完成 operation discriminator／shape 驗證，再套用一般 envelope limits；如此即使
        // malformed object 同時超過 member limit，仍會保留為 protocol failure 而非 result-too-large。
        WorkerEnvelopeValidator.ValidateStandaloneValue(result, ProtocolLimits);

        foreach (var page in pages)
        {
            var pageCanonicalBytes = GetCanonicalByteCount(page);
            if (pageCanonicalBytes > MaximumCanonicalPageBytes)
            {
                throw LimitExceeded("The Package01 fee worker canonical page byte limit was exceeded.");
            }

            cumulativeCanonicalBytes = checked(
                cumulativeCanonicalBytes + pageCanonicalBytes);
            if (cumulativeCanonicalBytes > MaximumCanonicalResultBytes)
            {
                throw LimitExceeded("The Package01 fee worker canonical result byte limit was exceeded.");
            }
        }
    }

    private static Guid ReadRequiredGuid(WorkerValue value, string fieldName)
    {
        if (value.Kind != WorkerValueKind.Guid ||
            !Guid.TryParseExact(value.Scalar, "N", out var parsed) ||
            parsed == Guid.Empty)
        {
            throw InvalidEnvelope($"The Package01 fee worker {fieldName} is invalid.");
        }

        return parsed;
    }

    private static DateTimeOffset ReadRequiredUtcDateTime(
        WorkerValue value,
        string fieldName)
    {
        if (value.Kind != WorkerValueKind.UtcDateTime ||
            !long.TryParse(
                value.Scalar,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var ticks) ||
            ticks < DateTime.MinValue.Ticks ||
            ticks > DateTime.MaxValue.Ticks)
        {
            throw InvalidEnvelope($"The Package01 fee worker {fieldName} is invalid.");
        }

        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static void ValidateRow(WorkerValue row)
    {
        if (!HasArrayShape(row) ||
            row.Items!.Count != ResultColumnCount)
        {
            throw InvalidEnvelope("The Package01 fee worker row shape is invalid.");
        }

        var cells = row.Items;
        ValidateNullableNonEmptyGuid(cells[0]);
        ValidateNullableKind(cells[1], WorkerValueKind.UtcDateTime, "createdOn");
        ValidateNullableKind(cells[2], WorkerValueKind.UtcDateTime, "payDate");
        ValidateRequiredKind(cells[3], WorkerValueKind.Decimal, "amount");
        ValidateNullableInt32(cells[4]);
        ValidateNullableKind(cells[5], WorkerValueKind.String, "payWayLabel");
        ValidateNullableKind(cells[6], WorkerValueKind.String, "categoryLabel");
        ValidateNullableKind(cells[7], WorkerValueKind.String, "others");
        ValidateNullableKind(cells[8], WorkerValueKind.String, "paidPeriod");
        ValidateNullableKind(cells[9], WorkerValueKind.String, "name");
    }

    private static void ValidateNullableNonEmptyGuid(WorkerValue value)
    {
        if (HasNullShape(value))
        {
            return;
        }

        if (!HasScalarShape(value, WorkerValueKind.Guid) ||
            !Guid.TryParseExact(value.Scalar, "N", out var parsed) ||
            parsed == Guid.Empty)
        {
            throw InvalidEnvelope("The Package01 fee worker feeId is invalid.");
        }
    }

    private static void ValidateNullableInt32(WorkerValue value)
    {
        if (HasNullShape(value))
        {
            return;
        }

        if (!HasScalarShape(value, WorkerValueKind.Int64) ||
            !long.TryParse(
                value.Scalar,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < int.MinValue ||
            parsed > int.MaxValue)
        {
            throw InvalidEnvelope("The Package01 fee worker payWayOption is invalid.");
        }
    }

    private static void ValidateNullableKind(
        WorkerValue value,
        WorkerValueKind expectedKind,
        string fieldName)
    {
        if (!HasNullShape(value) && !HasScalarShape(value, expectedKind))
        {
            throw InvalidEnvelope($"The Package01 fee worker {fieldName} kind is invalid.");
        }
    }

    private static void ValidateRequiredKind(
        WorkerValue value,
        WorkerValueKind expectedKind,
        string fieldName)
    {
        if (!HasScalarShape(value, expectedKind))
        {
            throw InvalidEnvelope($"The Package01 fee worker {fieldName} kind is invalid.");
        }
    }

    private static bool HasArrayShape(WorkerValue value) =>
        value.Kind == WorkerValueKind.Array &&
        value.Scalar is null &&
        value.Items is not null &&
        value.Members is null;

    private static bool HasNullShape(WorkerValue value) =>
        value.Kind == WorkerValueKind.Null &&
        value.Scalar is null &&
        value.Items is null &&
        value.Members is null;

    private static bool HasScalarShape(WorkerValue value, WorkerValueKind expectedKind) =>
        value.Kind == expectedKind &&
        value.Scalar is not null &&
        value.Items is null &&
        value.Members is null;

    private static int GetCanonicalByteCount(WorkerValue value)
    {
        try
        {
            var byteCount = 1;
            switch (value.Kind)
            {
                case WorkerValueKind.Null:
                    return byteCount;
                case WorkerValueKind.Boolean:
                    return checked(byteCount + 1);
                case WorkerValueKind.Int64:
                case WorkerValueKind.UtcDateTime:
                    return checked(byteCount + sizeof(long));
                case WorkerValueKind.Decimal:
                case WorkerValueKind.String:
                case WorkerValueKind.Guid:
                    return checked(
                        byteCount + sizeof(int) + StrictUtf8.GetByteCount(value.Scalar!));
                case WorkerValueKind.Array:
                    byteCount = checked(byteCount + sizeof(int));
                    foreach (var item in value.Items!)
                    {
                        byteCount = checked(byteCount + GetCanonicalByteCount(item));
                    }

                    return byteCount;
                case WorkerValueKind.Object:
                    byteCount = checked(byteCount + sizeof(int));
                    foreach (var member in value.Members!)
                    {
                        byteCount = checked(
                            byteCount + sizeof(int) + StrictUtf8.GetByteCount(member.Key));
                        byteCount = checked(byteCount + GetCanonicalByteCount(member.Value));
                    }

                    return byteCount;
                default:
                    throw InvalidEnvelope("The Package01 fee worker value kind is invalid.");
            }
        }
        catch (EncoderFallbackException)
        {
            throw InvalidEnvelope("The Package01 fee worker string encoding is invalid.");
        }
        catch (OverflowException)
        {
            throw LimitExceeded("The Package01 fee worker canonical byte limit was exceeded.");
        }
    }

    private static WorkerProtocolException InvalidEnvelope(string message) =>
        new WorkerProtocolException(WorkerProtocolFailureCategory.InvalidEnvelope, message);

    private static WorkerProtocolException LimitExceeded(string message) =>
        new WorkerProtocolException(
            WorkerProtocolFailureCategory.EnvelopeLimitExceeded,
            message);
}
