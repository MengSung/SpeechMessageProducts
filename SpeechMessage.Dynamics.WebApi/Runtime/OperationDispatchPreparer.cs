// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/OperationDispatchPreparer.cs
// 目的：在 admission 前同步驗證並建立 versioned canonical bounded dispatch。
// ============================================================================

using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;

// 只將 internal dispatch 安全契約開放給專用測試 assembly，不擴大產品程式的 public API 或 trust boundary。
[assembly: InternalsVisibleTo("SpeechMessage.Dynamics.Tests")]

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 將 caller-owned <see cref="OperationExecutionRequest"/> 同步轉換成 executor-owned
/// <see cref="PreparedOperationDispatch"/>。registry definition 是名稱與型別的唯一信任來源；
/// caller dictionary、CLR runtime type 與 JSON raw text 都不是權威。
/// </summary>
/// <remarks>
/// <see cref="TryPrepare"/> 不含 await、I/O、mutable global cache、runtime/client lookup 或 cancellation registration。
/// 此「prepare-before-first-await」順序是記憶體隔離契約：只有在 public executor 建立 async state machine 之前
/// 完成 scalar 副本，原始 request/dictionary/JsonDocument graph 才可在 queue wait 期間回收。
/// </remarks>
internal sealed class OperationDispatchPreparer
{
    private const byte CanonicalVersion = 1;
    private const byte NullTag = 0;
    private const byte StringTag = 1;
    private const byte GuidTag = 2;
    private const byte DateTimeTag = 3;
    private const byte IntegerTag = 4;
    private const byte DecimalTag = 5;
    private const byte BooleanTag = 6;
    private const byte EnumTag = 7;
    private const int MaximumParameterCount = 32;
    private const int MaximumParameterNameUtf8Bytes = 128;
    private const int MaximumIdempotencyKeyCharacters = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly ArrayPool<byte> _bufferPool;

    /// <summary>
    /// 取得使用共用 ArrayPool 的無狀態 preparer。Shared 只保留 pool 服務，不保留 request、
    /// parameter、buffer 或 hash，因此可安全被多個 executor 並行使用。
    /// </summary>
    internal static OperationDispatchPreparer Shared { get; } = new(ArrayPool<byte>.Shared);

    /// <summary>
    /// 建立 preparer；注入 pool 只用於確定性測試與之後的 buffer 生命週期，
    /// preparer 本身不擁有或 Dispose pool。
    /// </summary>
    internal OperationDispatchPreparer(ArrayPool<byte> bufferPool)
    {
        _bufferPool = bufferPool ?? throw new ArgumentNullException(nameof(bufferPool));
    }

    /// <summary>
    /// 同步驗證並建立唯一 prepared owner。格式為：version byte，固定順序 routing/template UTF-8 fields，
    /// 顯式 idempotency null/string tag，UInt32 big-endian parameter count，以 <see cref="StringComparer.Ordinal"/>
    /// 排序的 parameter name，以及固定 scalar type tag/value。每個 UTF-8 field 都使用 UInt32 big-endian byte 長度前綴。
    /// </summary>
    /// <param name="request">caller-owned request；方法返回後不得被 prepared owner 參考。</param>
    /// <param name="definition">registry-owned immutable operation definition。</param>
    /// <param name="admissionPlan">已驗證的 immutable capacity plan，提供精確 canonical byte 上限與 timeout。</param>
    /// <param name="prepared">成功時取得由 executor 唯一擁有的 prepared dispatch。</param>
    /// <param name="error">失敗時取得不包含 raw value/body 的 bounded 錯誤。</param>
    /// <returns>只有所有驗證、單次 rent、單次 encode 與 hash 都完成才回傳 true。</returns>
    internal bool TryPrepare(
        OperationExecutionRequest request,
        OperationDefinition definition,
        OrganizationAdmissionPlan admissionPlan,
        out PreparedOperationDispatch? prepared,
        out OperationExecutionResult? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(admissionPlan);
        prepared = null;
        error = null;

        if (string.IsNullOrWhiteSpace(request.ProfileAlias) ||
            string.IsNullOrWhiteSpace(request.WorkloadSubjectId))
        {
            error = Invalid("ProfileAlias and WorkloadSubjectId are required.");
            return false;
        }

        if (!string.Equals(
                request.CapabilityOperationId,
                definition.CapabilityOperationId,
                StringComparison.Ordinal))
        {
            error = Invalid("The request operation does not match the registry definition.");
            return false;
        }

        if (!TryNormalizeIdempotencyKey(request.IdempotencyKey, out var idempotencyKey, out error))
        {
            return false;
        }

        var sourceParameters = request.Parameters;
        if (sourceParameters is null)
        {
            error = Invalid("Parameters are required.");
            return false;
        }

        if (sourceParameters.Count > MaximumParameterCount ||
            sourceParameters.Count > definition.Parameters.Count)
        {
            error = Invalid("The parameter count exceeds the registered operation contract.");
            return false;
        }

        var definitionsByName = new Dictionary<string, OperationParameterDefinition>(StringComparer.Ordinal);
        foreach (var parameterDefinition in definition.Parameters)
        {
            if (!TryGetUtf8ByteCount(parameterDefinition.Name, out var definitionNameBytes) ||
                definitionNameBytes is 0 or > MaximumParameterNameUtf8Bytes ||
                !IsSupportedScalarType(parameterDefinition.Type) ||
                !definitionsByName.TryAdd(parameterDefinition.Name, parameterDefinition))
            {
                error = Invalid("The registry contains an unsupported or duplicate parameter definition.");
                return false;
            }
        }

        foreach (var pair in sourceParameters)
        {
            if (!TryGetUtf8ByteCount(pair.Key, out var nameBytes) ||
                nameBytes is 0 or > MaximumParameterNameUtf8Bytes)
            {
                error = Invalid("The request contains an empty, invalid or oversized parameter name.");
                return false;
            }

            if (!definitionsByName.ContainsKey(pair.Key))
            {
                // 只回顯已通過 strict UTF-8 且最多 128 bytes 的第一個 unknown name，
                // 保留現有診斷契約同時避免無界 string.Join/多名稱錯誤擴大。
                error = Invalid($"Unknown parameter '{pair.Key}'.");
                return false;
            }
        }

        var normalized = new Dictionary<string, object?>(sourceParameters.Count, StringComparer.Ordinal);
        var canonicalParameters = new List<CanonicalParameter>(sourceParameters.Count);
        byte[]? rentedBuffer = null;
        var ownershipTransferred = false;
        try
        {
            foreach (var parameterDefinition in definition.Parameters)
            {
                if (!sourceParameters.TryGetValue(parameterDefinition.Name, out var sourceValue))
                {
                    if (parameterDefinition.Required)
                    {
                        error = Invalid("A required parameter is missing.");
                        return false;
                    }

                    continue;
                }

                if (!TryNormalizeValue(parameterDefinition, sourceValue, out var value, out var typeTag))
                {
                    error = Invalid("A parameter value does not match its registered scalar type.");
                    return false;
                }

                if (value is null && parameterDefinition.Required)
                {
                    error = Invalid("A required parameter cannot be null.");
                    return false;
                }

                normalized.Add(parameterDefinition.Name, value);
                canonicalParameters.Add(new CanonicalParameter(parameterDefinition.Name, typeTag, value));
            }

            canonicalParameters.Sort(static (left, right) =>
                StringComparer.Ordinal.Compare(left.Name, right.Name));

            if (!TryCalculateCanonicalLength(
                    request.ProfileAlias.Trim(),
                    definition,
                    request.WorkloadSubjectId.Trim(),
                    idempotencyKey,
                    canonicalParameters,
                    admissionPlan.MaxDispatchEnvelopeBytes,
                    out var canonicalLength,
                    out error))
            {
                return false;
            }

            // 只在已證明 exact length 不超過 plan 後 rent 一次；失敗路徑不得把半寫入 buffer 留在 pool 外。
            rentedBuffer = _bufferPool.Rent(canonicalLength);
            var written = WriteCanonical(
                rentedBuffer,
                request.ProfileAlias.Trim(),
                definition,
                request.WorkloadSubjectId.Trim(),
                idempotencyKey,
                canonicalParameters);
            if (written != canonicalLength)
            {
                throw new InvalidOperationException("Canonical length calculation and encoding diverged.");
            }

            var hash = Convert.ToHexString(
                    SHA256.HashData(rentedBuffer.AsSpan(0, canonicalLength)))
                .ToLowerInvariant();
            var envelope = new DispatchEnvelope
            {
                ProfileAlias = request.ProfileAlias.Trim(),
                CapabilityOperationId = definition.CapabilityOperationId,
                WorkloadSubjectId = request.WorkloadSubjectId.Trim(),
                TemplateId = definition.TemplateId,
                TemplateHash = definition.TemplateHash,
                IdempotencyKey = idempotencyKey,
                DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(
                    Math.Max(1, admissionPlan.QueueAdmissionTimeoutSeconds + 30)),
                CanonicalEnvelopeBytes = canonicalLength
            };

            prepared = new PreparedOperationDispatch(
                envelope,
                normalized,
                rentedBuffer,
                canonicalLength,
                hash,
                _bufferPool);
            rentedBuffer = null;
            ownershipTransferred = true;
            return true;
        }
        catch (EncoderFallbackException)
        {
            error = Invalid("A string contains invalid Unicode data.");
            return false;
        }
        catch (OverflowException)
        {
            error = TooLarge(admissionPlan.MaxDispatchEnvelopeBytes);
            return false;
        }
        finally
        {
            if (!ownershipTransferred)
            {
                normalized.Clear();
            }
            if (rentedBuffer is not null)
            {
                // 任何 encode/hash/constructor 失敗都遵守同一個 zero-before-return 契約。
                CryptographicOperations.ZeroMemory(rentedBuffer.AsSpan());
                _bufferPool.Return(rentedBuffer, clearArray: false);
            }
        }
    }

    /// <summary>驗證 1-128 個 RFC 3986 unreserved ASCII 字元的冪等鍵；null 保留為顯式 null tag。</summary>
    private static bool TryNormalizeIdempotencyKey(
        string? source,
        out string? normalized,
        out OperationExecutionResult? error)
    {
        normalized = source;
        error = null;
        if (source is null)
        {
            return true;
        }

        if (source.Length is < 1 or > MaximumIdempotencyKeyCharacters ||
            source.Any(static character =>
                !((character >= 'a' && character <= 'z') ||
                  (character >= 'A' && character <= 'Z') ||
                  (character >= '0' && character <= '9') ||
                  character is '-' or '.' or '_' or '~')))
        {
            error = Invalid("IdempotencyKey must be a 1-128 character URL-safe value.");
            return false;
        }

        return true;
    }

    /// <summary>只允許可形成 bounded immutable scalar 副本的 registry type；array/object 留待後續獨立設計。</summary>
    private static bool IsSupportedScalarType(string type)
        => type is "string" or "guid" or "date-time" or "integer" or "decimal" or "boolean" or "enum";

    /// <summary>
    /// 不使用 <c>Convert</c>、serializer 或 <c>JsonElement.GetRawText()</c>。JSON object/array 對任何 scalar definition
    /// 都立即拒絕，避免深層/巨大 backing graph 進入 admission。
    /// </summary>
    private static bool TryNormalizeValue(
        OperationParameterDefinition definition,
        object? source,
        out object? normalized,
        out byte typeTag)
    {
        normalized = null;
        typeTag = NullTag;
        if (source is null || source is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
        {
            return true;
        }

        switch (definition.Type)
        {
            case "string":
                typeTag = StringTag;
                return TryGetString(source, out normalized);
            case "enum":
                typeTag = EnumTag;
                return TryGetString(source, out normalized);
            case "guid":
                typeTag = GuidTag;
                return TryGetGuid(source, out normalized);
            case "date-time":
                typeTag = DateTimeTag;
                return TryGetDateTime(source, out normalized);
            case "integer":
                typeTag = IntegerTag;
                return TryGetInteger(source, out normalized);
            case "decimal":
                typeTag = DecimalTag;
                return TryGetDecimal(source, out normalized);
            case "boolean":
                typeTag = BooleanTag;
                return TryGetBoolean(source, out normalized);
            default:
                return false;
        }
    }

    /// <summary>只接受 native string 或 JSON string，不將 number/object/array 轉成顯示文字。</summary>
    private static bool TryGetString(object source, out object? value)
    {
        value = source switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null
        };
        return value is string;
    }

    /// <summary>只接受 Guid 或可回解的 string scalar，正規化後以 16-byte big-endian 儲存。</summary>
    private static bool TryGetGuid(object source, out object? value)
    {
        if (source is Guid guid)
        {
            value = guid;
            return true;
        }

        if (TryGetString(source, out var text) && Guid.TryParse((string)text!, out guid))
        {
            value = guid;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// 將 DateTimeOffset 或 DateTime 正規化為 UTC。CLR <see cref="DateTimeKind.Unspecified"/> 來自已型別化的內部 API 時
    /// 明確當作 UTC，不讀取主機時區；字串則必須自帶 Z 或 <c>±HH:mm</c> offset 才可解析。
    /// </summary>
    private static bool TryGetDateTime(object source, out object? value)
    {
        if (source is DateTimeOffset offset)
        {
            value = offset.ToUniversalTime();
            return true;
        }

        if (source is DateTime dateTime)
        {
            var deterministicDateTime = dateTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                : dateTime.ToUniversalTime();
            value = new DateTimeOffset(deterministicDateTime).ToUniversalTime();
            return true;
        }

        if (TryGetString(source, out var text) &&
            HasExplicitDateTimeOffset((string)text!) &&
            DateTimeOffset.TryParse(
                (string)text!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out offset))
        {
            value = offset.ToUniversalTime();
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// 檢查 date-time string 尾端是 <c>Z</c> 或固定數值 <c>±HH:mm</c>。此步驟只鎖定時區來源，
    /// 完整日期、閣年、時分秒與 offset 範圍仍交由 invariant <see cref="DateTimeOffset.TryParse(string,IFormatProvider,DateTimeStyles,out DateTimeOffset)"/> 驗證。
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

    /// <summary>將有號/無號 bounded integral CLR value 或 JSON Int64 正規化為 Int64，超過範圍即拒絕。</summary>
    private static bool TryGetInteger(object source, out object? value)
    {
        switch (source)
        {
            case sbyte number:
                value = (long)number;
                return true;
            case byte number:
                value = (long)number;
                return true;
            case short number:
                value = (long)number;
                return true;
            case ushort number:
                value = (long)number;
                return true;
            case int number:
                value = (long)number;
                return true;
            case uint number:
                value = (long)number;
                return true;
            case long number:
                value = number;
                return true;
            case ulong number when number <= long.MaxValue:
                value = (long)number;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt64(out var number):
                value = number;
                return true;
            default:
                value = null;
                return false;
        }
    }

    /// <summary>只接受 decimal 或 JSON decimal，不接受 float/double 的非確定 binary rounding。</summary>
    private static bool TryGetDecimal(object source, out object? value)
    {
        if (source is decimal number)
        {
            value = number;
            return true;
        }

        if (source is JsonElement { ValueKind: JsonValueKind.Number } element &&
            element.TryGetDecimal(out number))
        {
            value = number;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>只接受 native bool 或 JSON true/false，不接受字串/0/1 的寬鬆轉換。</summary>
    private static bool TryGetBoolean(object source, out object? value)
    {
        if (source is bool boolean)
        {
            value = boolean;
            return true;
        }

        if (source is JsonElement element &&
            element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// 在 rent 前使用 checked arithmetic 預計與 encoder 完全相同的 byte 數。一旦超過 plan 立即停止，
    /// 避免大值觸發 buffer allocation；因此 rent 後只 encode 一次。
    /// </summary>
    private static bool TryCalculateCanonicalLength(
        string profileAlias,
        OperationDefinition definition,
        string workloadSubjectId,
        string? idempotencyKey,
        IReadOnlyList<CanonicalParameter> parameters,
        int maximumBytes,
        out int length,
        out OperationExecutionResult? error)
    {
        length = 1;
        error = null;
        try
        {
            AddUtf8FieldLength(profileAlias, ref length);
            AddUtf8FieldLength(definition.CapabilityOperationId, ref length);
            AddUtf8FieldLength(workloadSubjectId, ref length);
            AddUtf8FieldLength(definition.TemplateId, ref length);
            AddUtf8FieldLength(definition.TemplateHash, ref length);
            length = checked(length + 1);
            if (idempotencyKey is not null)
            {
                AddUtf8FieldLength(idempotencyKey, ref length);
            }

            length = checked(length + sizeof(uint));
            foreach (var parameter in parameters)
            {
                AddUtf8FieldLength(parameter.Name, ref length);
                length = checked(length + 1);
                switch (parameter.TypeTag)
                {
                    case NullTag:
                        break;
                    case StringTag:
                    case DateTimeTag:
                    case DecimalTag:
                    case EnumTag:
                        AddUtf8FieldLength(GetCanonicalText(parameter), ref length);
                        break;
                    case GuidTag:
                        length = checked(length + 16);
                        break;
                    case IntegerTag:
                        length = checked(length + sizeof(long));
                        break;
                    case BooleanTag:
                        length = checked(length + 1);
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported canonical type tag.");
                }

                if (length > maximumBytes)
                {
                    error = TooLarge(maximumBytes);
                    return false;
                }
            }
        }
        catch (EncoderFallbackException)
        {
            error = Invalid("A string contains invalid Unicode data.");
            return false;
        }
        catch (OverflowException)
        {
            error = TooLarge(maximumBytes);
            return false;
        }

        if (length > maximumBytes)
        {
            error = TooLarge(maximumBytes);
            return false;
        }

        return true;
    }

    /// <summary>增加 UInt32 長度前綴與 strict UTF-8 byte 數，整個運算由 caller 的 checked 路徑保護。</summary>
    private static void AddUtf8FieldLength(string value, ref int length)
        => length = checked(length + sizeof(uint) + StrictUtf8.GetByteCount(value));

    /// <summary>以與預計相同的固定格式寫入唯一 buffer，不使用 MemoryStream 或通用 serializer。</summary>
    private static int WriteCanonical(
        byte[] destination,
        string profileAlias,
        OperationDefinition definition,
        string workloadSubjectId,
        string? idempotencyKey,
        IReadOnlyList<CanonicalParameter> parameters)
    {
        var offset = 0;
        destination[offset++] = CanonicalVersion;
        WriteUtf8Field(destination, ref offset, profileAlias);
        WriteUtf8Field(destination, ref offset, definition.CapabilityOperationId);
        WriteUtf8Field(destination, ref offset, workloadSubjectId);
        WriteUtf8Field(destination, ref offset, definition.TemplateId);
        WriteUtf8Field(destination, ref offset, definition.TemplateHash);
        destination[offset++] = idempotencyKey is null ? NullTag : StringTag;
        if (idempotencyKey is not null)
        {
            WriteUtf8Field(destination, ref offset, idempotencyKey);
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(offset, sizeof(uint)), checked((uint)parameters.Count));
        offset += sizeof(uint);
        foreach (var parameter in parameters)
        {
            WriteUtf8Field(destination, ref offset, parameter.Name);
            destination[offset++] = parameter.TypeTag;
            switch (parameter.TypeTag)
            {
                case NullTag:
                    break;
                case StringTag:
                case DateTimeTag:
                case DecimalTag:
                case EnumTag:
                    WriteUtf8Field(destination, ref offset, GetCanonicalText(parameter));
                    break;
                case GuidTag:
                    ((Guid)parameter.Value!).TryWriteBytes(
                        destination.AsSpan(offset, 16),
                        bigEndian: true,
                        out var bytesWritten);
                    if (bytesWritten != 16)
                    {
                        throw new InvalidOperationException("Guid canonical encoding did not write 16 bytes.");
                    }

                    offset += 16;
                    break;
                case IntegerTag:
                    BinaryPrimitives.WriteInt64BigEndian(destination.AsSpan(offset, sizeof(long)), (long)parameter.Value!);
                    offset += sizeof(long);
                    break;
                case BooleanTag:
                    destination[offset++] = (bool)parameter.Value! ? (byte)1 : (byte)0;
                    break;
                default:
                    throw new InvalidOperationException("Unsupported canonical type tag.");
            }
        }

        return offset;
    }

    /// <summary>寫入 UInt32 big-endian byte 長度與 strict UTF-8 field；不寫入 char count、terminator 或 locale 資訊。</summary>
    private static void WriteUtf8Field(byte[] destination, ref int offset, string value)
    {
        var byteCount = StrictUtf8.GetByteCount(value);
        BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(offset, sizeof(uint)), checked((uint)byteCount));
        offset += sizeof(uint);
        offset += StrictUtf8.GetBytes(value, destination.AsSpan(offset, byteCount));
    }

    /// <summary>將已正規化 scalar 轉成 invariant canonical text，只供文字型 tag 使用。</summary>
    private static string GetCanonicalText(CanonicalParameter parameter)
        => parameter.TypeTag switch
        {
            StringTag or EnumTag => (string)parameter.Value!,
            DateTimeTag => ((DateTimeOffset)parameter.Value!).ToString("O", CultureInfo.InvariantCulture),
            DecimalTag => ((decimal)parameter.Value!).ToString("G29", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("The canonical parameter is not text encoded.")
        };

    /// <summary>安全計算 strict UTF-8 byte 數，無效 surrogate 直接拒絕而不以 replacement character 改變指紋。</summary>
    private static bool TryGetUtf8ByteCount(string? value, out int byteCount)
    {
        byteCount = 0;
        if (value is null)
        {
            return false;
        }

        try
        {
            byteCount = StrictUtf8.GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>建立不包含 parameter value、alias 或內部 exception 的受控驗證失敗。</summary>
    private static OperationExecutionResult Invalid(string message)
        => OperationExecutionResult.Failure(DynamicsErrorCodes.InvalidParameter, message);

    /// <summary>建立只曝露已核准上限的精確 envelope 過大失敗。</summary>
    private static OperationExecutionResult TooLarge(int maximumBytes)
        => OperationExecutionResult.Failure(
            DynamicsErrorCodes.EnvelopeTooLarge,
            $"The canonical dispatch envelope exceeds the configured {maximumBytes}-byte limit.");

    /// <summary>
    /// 只在單次 prepare stack 存活的已正規化參數；列表會在建立 prepared owner 後離開範圍，
    /// 不是 cache 或 queue owner。
    /// </summary>
    private readonly record struct CanonicalParameter(string Name, byte TypeTag, object? Value);
}
