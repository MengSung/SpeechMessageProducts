using System.Globalization;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// 將 Package01 Worker 的 SDK-free <c>Array&lt;Page&lt;Row&gt;&gt;</c> 結果投影為產品邊界的
/// <see cref="Package01FeeRecord"/>。此類別不保存任何 mutable/static 集合，也不接觸 CRM SDK、
/// Endpoint、Credential、Token、Session、Pipe 或 Process；每次呼叫只建立一個有上限的 DTO 陣列，
/// 並在回傳前重新執行 Supervisor 端的 operation-specific shape、row、page 與 canonical-byte 驗證。
/// </summary>
internal static class Package01FeeWorkerResponseProjector
{
    /// <summary>
    /// 驗證 operation identity 與完整 WorkerValue 結果，依 page／row 原順序攤平成封閉 response branch。
    /// 驗證完成前不配置 DTO 集合；最大容量由 <see cref="Package01FeeWorkerContract.MaximumTotalRows"/>
    /// 限制，避免 malformed 或 oversized frame 造成部分成功、無界配置或跨要求資料保留。
    /// </summary>
    /// <param name="operationId">已由 registry 與 immutable revision 綁定的 Package01 operation ID。</param>
    /// <param name="ceVersion">目前 Worker generation 的固定 CE 版本。</param>
    /// <param name="result">由同一 request frame 解碼、尚未跨越 Supervisor 邊界的 WorkerValue。</param>
    /// <returns>只含 bounded Package01 fee DTO 的產品安全 response branch。</returns>
    /// <exception cref="WorkerProtocolException">
    /// operation identity、CE version、result shape、scalar 或大小不符合契約時擲出。
    /// </exception>
    public static OperationResponseData Project(
        string operationId,
        string ceVersion,
        WorkerValue result)
    {
        if (!string.Equals(
                operationId,
                Package01FeeWorkerContract.CapabilityOperationId,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(ceVersion))
        {
            throw InvalidEnvelope();
        }

        if (result is null)
        {
            throw InvalidEnvelope();
        }

        Package01FeeWorkerContract.ValidateResult(result);

        var pages = result.Items!;
        var totalRows = 0;
        foreach (var page in pages)
        {
            totalRows = checked(totalRows + page.Items!.Count);
        }

        var records = new List<Package01FeeRecord>(totalRows);
        foreach (var page in pages)
        {
            foreach (var row in page.Items!)
            {
                records.Add(ProjectRow(row.Items!));
            }
        }

        return OperationResponseData.ForPackage01FeeRecords(
            operationId,
            ceVersion,
            records);
    }

    /// <summary>
    /// 依固定十欄 ordinal contract 建立一筆不可變 DTO；呼叫前完整 row shape 已由 shared contract
    /// 驗證，此處仍以 canonical scalar parser fail closed，避免未來 contract 漂移變成未處理例外或錯欄資料。
    /// </summary>
    private static Package01FeeRecord ProjectRow(IReadOnlyList<WorkerValue> cells)
    {
        try
        {
            return new Package01FeeRecord
            {
                FeeId = ReadNullableGuid(cells[0]),
                CreatedOn = ReadNullableUtcDateTime(cells[1]),
                PayDate = ReadNullableUtcDateTime(cells[2]),
                Amount = ReadRequiredDecimal(cells[3]),
                PayWayOption = ReadNullableInt32(cells[4]),
                PayWayLabel = ReadNullableString(cells[5]),
                CategoryLabel = ReadNullableString(cells[6]),
                Others = ReadNullableString(cells[7]),
                PaidPeriod = ReadNullableString(cells[8]),
                Name = ReadNullableString(cells[9])
            };
        }
        catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or
            FormatException or
            IndexOutOfRangeException or
            OverflowException)
        {
            // 不附帶原始 scalar、row 或 inner exception，避免上游資料進入 Gateway error surface。
            throw InvalidEnvelope();
        }
    }

    private static Guid? ReadNullableGuid(WorkerValue value)
    {
        if (value.Kind == WorkerValueKind.Null)
        {
            return null;
        }

        if (value.Kind != WorkerValueKind.Guid ||
            !Guid.TryParseExact(value.Scalar, "N", out var parsed) ||
            parsed == Guid.Empty)
        {
            throw InvalidEnvelope();
        }

        return parsed;
    }

    private static DateTimeOffset? ReadNullableUtcDateTime(WorkerValue value)
    {
        if (value.Kind == WorkerValueKind.Null)
        {
            return null;
        }

        if (value.Kind != WorkerValueKind.UtcDateTime ||
            !long.TryParse(
                value.Scalar,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var ticks) ||
            ticks < DateTime.MinValue.Ticks ||
            ticks > DateTime.MaxValue.Ticks)
        {
            throw InvalidEnvelope();
        }

        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static decimal ReadRequiredDecimal(WorkerValue value)
    {
        if (value.Kind != WorkerValueKind.Decimal ||
            !decimal.TryParse(
                value.Scalar,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            !string.Equals(
                parsed.ToString("G29", CultureInfo.InvariantCulture),
                value.Scalar,
                StringComparison.Ordinal))
        {
            throw InvalidEnvelope();
        }

        return parsed;
    }

    private static int? ReadNullableInt32(WorkerValue value)
    {
        if (value.Kind == WorkerValueKind.Null)
        {
            return null;
        }

        if (value.Kind != WorkerValueKind.Int64 ||
            !long.TryParse(
                value.Scalar,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < int.MinValue ||
            parsed > int.MaxValue)
        {
            throw InvalidEnvelope();
        }

        return (int)parsed;
    }

    private static string? ReadNullableString(WorkerValue value)
    {
        if (value.Kind == WorkerValueKind.Null)
        {
            return null;
        }

        if (value.Kind != WorkerValueKind.String || value.Scalar is null)
        {
            throw InvalidEnvelope();
        }

        return value.Scalar;
    }

    private static WorkerProtocolException InvalidEnvelope() =>
        new(
            WorkerProtocolFailureCategory.InvalidEnvelope,
            "The official worker Package01 result is invalid.");
}
