// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/FeeReads/Package01FeeReadClient.cs
// 目的：把 Package 1 fee/lesson 操作編成 registry 請求，並解析回 DTO。
//
// 保母教學：
// 1. 這裡依賴 IDynamicsOperationExecutor，不直接碰 WebApi。
// 2. Gateway 模式：executor 會是 HTTP 轉發器。
// 3. Embedded 模式：executor 會是本機受控執行器。
// 4. 解析時要容忍 OData 的 money 物件、lookup _value、formatted value 註解欄位。
// 5. 產品只能傳 typed 參數；禁止 raw FetchXML。
// ============================================================================

using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.Models;

namespace SpeechMessage.Dynamics.ProductClient.FeeReads;

/// <summary>
/// Package 1 fee/lesson 讀取預設實作。
/// </summary>
public sealed class Package01FeeReadClient : IPackage01FeeReadClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDynamicsOperationExecutor _executor;
    private readonly ILogger<Package01FeeReadClient> _logger;

    public Package01FeeReadClient(
        IDynamicsOperationExecutor executor,
        ILogger<Package01FeeReadClient> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactDateRangeAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid contactId,
        DateTime startDate,
        DateTime endDate,
        string? contactName = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["contactId"] = contactId,
            ["startDate"] = startDate,
            ["endDate"] = endDate
        };
        if (!string.IsNullOrWhiteSpace(contactName))
        {
            parameters["contactName"] = contactName;
        }

        return ExecuteAndParseFeesAsync(
            profileAlias,
            workloadSubjectId,
            OperationIds.FeeDedicationRetrieveByContactDateRange,
            parameters,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid contactId,
        string? contactName = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["contactId"] = contactId
        };
        if (!string.IsNullOrWhiteSpace(contactName))
        {
            parameters["contactName"] = contactName;
        }

        return ExecuteAndParseFeesAsync(
            profileAlias,
            workloadSubjectId,
            OperationIds.FeeDedicationRetrieveByContact,
            parameters,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FeeRecordDto>> RetrieveFeesByDedicationPeriodAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid dedicationBookingId,
        string paidPeriod,
        string? dedicationBookingName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paidPeriod))
        {
            throw new ArgumentException("paidPeriod is required.", nameof(paidPeriod));
        }

        var parameters = new Dictionary<string, object?>
        {
            ["dedicationBookingId"] = dedicationBookingId,
            ["paidPeriod"] = paidPeriod
        };
        if (!string.IsNullOrWhiteSpace(dedicationBookingName))
        {
            parameters["dedicationBookingName"] = dedicationBookingName;
        }

        return ExecuteAndParseFeesAsync(
            profileAlias,
            workloadSubjectId,
            OperationIds.FeesRetrieveByDedicationPeriod,
            parameters,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveFeeEditorRowsByDiscipleLessonAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid discipleLessonId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["discipleLessonId"] = discipleLessonId
        };

        return ExecuteAndParseStorLessonsAsync(
            profileAlias,
            workloadSubjectId,
            OperationIds.FeesEditorLoadByDiscipleLesson,
            parameters,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByContactAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid contactId,
        string? contactName = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["contactId"] = contactId
        };
        if (!string.IsNullOrWhiteSpace(contactName))
        {
            parameters["contactName"] = contactName;
        }

        return ExecuteAndParseStorLessonsAsync(
            profileAlias,
            workloadSubjectId,
            OperationIds.LessonsStorRetrieveByContact,
            parameters,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByDiscipleLessonAsync(
        string profileAlias,
        string workloadSubjectId,
        Guid discipleLessonId,
        string? lessonName = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["discipleLessonId"] = discipleLessonId
        };
        if (!string.IsNullOrWhiteSpace(lessonName))
        {
            parameters["lessonName"] = lessonName;
        }

        return ExecuteAndParseStorLessonsAsync(
            profileAlias,
            workloadSubjectId,
            OperationIds.LessonsStorRetrieveByDiscipleLesson,
            parameters,
            cancellationToken);
    }

    private async Task<IReadOnlyList<FeeRecordDto>> ExecuteAndParseFeesAsync(
        string profileAlias,
        string workloadSubjectId,
        string capabilityOperationId,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(
            profileAlias,
            workloadSubjectId,
            capabilityOperationId,
            parameters,
            cancellationToken).ConfigureAwait(false);

        var rows = ParseFeeRecords(result.Data);
        _logger.LogInformation(
            "Package01 fee-read {OperationId} returned {Count} rows for profile {ProfileAlias}",
            capabilityOperationId,
            rows.Count,
            profileAlias);
        return rows;
    }

    private async Task<IReadOnlyList<StorLessonRecordDto>> ExecuteAndParseStorLessonsAsync(
        string profileAlias,
        string workloadSubjectId,
        string capabilityOperationId,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(
            profileAlias,
            workloadSubjectId,
            capabilityOperationId,
            parameters,
            cancellationToken).ConfigureAwait(false);

        var rows = ParseStorLessonRecords(result.Data);
        _logger.LogInformation(
            "Package01 stor-lesson-read {OperationId} returned {Count} rows for profile {ProfileAlias}",
            capabilityOperationId,
            rows.Count,
            profileAlias);
        return rows;
    }

    private async Task<OperationExecutionResult> ExecuteAsync(
        string profileAlias,
        string workloadSubjectId,
        string capabilityOperationId,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileAlias))
        {
            throw new ArgumentException("profileAlias is required.", nameof(profileAlias));
        }

        if (string.IsNullOrWhiteSpace(workloadSubjectId))
        {
            throw new ArgumentException("workloadSubjectId is required.", nameof(workloadSubjectId));
        }

        var result = await _executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = profileAlias.Trim(),
            CapabilityOperationId = capabilityOperationId,
            WorkloadSubjectId = workloadSubjectId.Trim(),
            Parameters = parameters
        }, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Package 1 read failed: {result.ErrorCode} - {result.ErrorMessage}");
        }

        return result;
    }

    /// <summary>
    /// 解析 fee 結果。
    /// 支援：
    /// 1) 直接 OData { value: [...] }
    /// 2) 包一層 { operationId, data: { value: [...] } }
    /// </summary>
    public static IReadOnlyList<FeeRecordDto> ParseFeeRecords(object? data)
    {
        if (!TryGetValueArray(data, out var valueArray))
        {
            return Array.Empty<FeeRecordDto>();
        }

        return valueArray.EnumerateArray().Select(MapFeeRow).ToArray();
    }

    /// <summary>
    /// 解析 stor-lesson 結果。
    /// </summary>
    public static IReadOnlyList<StorLessonRecordDto> ParseStorLessonRecords(object? data)
    {
        if (!TryGetValueArray(data, out var valueArray))
        {
            return Array.Empty<StorLessonRecordDto>();
        }

        return valueArray.EnumerateArray().Select(MapStorLessonRow).ToArray();
    }

    private static bool TryGetValueArray(object? data, out JsonElement valueArray)
    {
        valueArray = default;
        if (data is null)
        {
            return false;
        }

        JsonElement root;
        if (data is JsonElement element)
        {
            root = element;
        }
        else
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            root = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("data", out var nested))
        {
            root = nested;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("value", out var arr) &&
            arr.ValueKind == JsonValueKind.Array)
        {
            valueArray = arr;
            return true;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            valueArray = root;
            return true;
        }

        return false;
    }

    private static FeeRecordDto MapFeeRow(JsonElement row)
    {
        return new FeeRecordDto
        {
            FeeId = ReadGuid(row, "new_feeid"),
            Name = ReadString(row, "new_name"),
            CreatedOn = ReadDate(row, "createdon"),
            PayDate = ReadDate(row, "new_pay_date"),
            Amount = ReadMoney(row, "new_fee_really_paid"),
            PayWayOption = ReadInt(row, "new_pay_way"),
            PayWayLabel = ReadFormatted(row, "new_pay_way") ?? ReadString(row, "new_pay_way"),
            CategoryLabel = ReadFormatted(row, "new_category") ?? ReadString(row, "new_category"),
            Others = ReadString(row, "new_others"),
            PaidPeriod = ReadString(row, "new_paid_period")
        };
    }

    private static StorLessonRecordDto MapStorLessonRow(JsonElement row)
    {
        return new StorLessonRecordDto
        {
            StorLessonId = ReadGuid(row, "new_stor_lessonsid"),
            ContactId = ReadGuid(row, "_new_contact_new_stor_lessons_value")
                        ?? ReadGuid(row, "new_contact_new_stor_lessons"),
            DiscipleLessonId = ReadGuid(row, "_new_new_disciple_lessons_new_stor_les_value")
                               ?? ReadGuid(row, "new_new_disciple_lessons_new_stor_les"),
            CreatedOn = ReadDate(row, "createdon"),
            PayDate = ReadDate(row, "new_pay_date"),
            CurrentComplete = ReadBool(row, "new_current_complete"),
            ContactName = ReadString(row, "contact.fullname")
                          ?? ReadFormatted(row, "new_contact_new_stor_lessons")
                          ?? ReadString(row, "new_contact_new_stor_lessons"),
            ContactMobile = ReadString(row, "contact.mobilephone")
                            ?? ReadString(row, "mobilephone"),
            DiscipleLessonName = ReadFormatted(row, "new_new_disciple_lessons_new_stor_les")
                                 ?? ReadString(row, "new_new_disciple_lessons_new_stor_les"),
            FeeAmount = ReadNullableMoney(row, "new_fee")
        };
    }

    private static string? ReadString(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => prop.ToString()
        };
    }

    private static string? ReadFormatted(JsonElement row, string logicalName)
    {
        var key = logicalName + "@OData.Community.Display.V1.FormattedValue";
        return ReadString(row, key);
    }

    private static Guid? ReadGuid(JsonElement row, string name)
    {
        var text = ReadString(row, name);
        return Guid.TryParse(text, out var id) ? id : null;
    }

    private static DateTimeOffset? ReadDate(JsonElement row, string name)
    {
        var text = ReadString(row, name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return dto;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
        {
            return new DateTimeOffset(dt);
        }

        return null;
    }

    private static int? ReadInt(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var prop))
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var n))
        {
            return n;
        }

        var text = prop.ToString();
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool? ReadBool(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(prop.GetString(), out var b) => b,
            _ => null
        };
    }

    private static decimal ReadMoney(JsonElement row, string name)
        => ReadNullableMoney(row, name) ?? 0m;

    private static decimal? ReadNullableMoney(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var prop))
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var number))
        {
            return number;
        }

        if (prop.ValueKind == JsonValueKind.Object)
        {
            if (prop.TryGetProperty("Value", out var value) && value.TryGetDecimal(out var nested))
            {
                return nested;
            }

            if (prop.TryGetProperty("value", out var value2) && value2.TryGetDecimal(out var nested2))
            {
                return nested2;
            }
        }

        var text = prop.ToString();
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}