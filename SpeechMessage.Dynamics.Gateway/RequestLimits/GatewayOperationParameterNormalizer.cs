// ============================================================================
// 檔案：SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayOperationParameterNormalizer.cs
// 用途：在 Gateway authorization 後、RequestGuard/executor 前，將 P7.3 特殊資源 JSON 收斂為封閉 pure value。
//
// 信任與生命週期邊界：
// 1. 此 normalizer 不信任 cloned JsonElement 的 nested object；只接受明確 operation 的固定欄位與型別。
// 2. 每次呼叫建立新的 parameter dictionary 與 image defensive copy，不保存 JsonElement、HttpContext、stream、
//    principal、profile、credential、cache、timer、subscription、lease 或 connector client。
// 3. base64 decode 暫存陣列只存在同步方法 scope；建構封閉 DTO 後立即清零，DTO 自己另有 defensive copy。
// ============================================================================

using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Gateway.RequestLimits;

/// <summary>
/// 將已通過 Gateway authorization 的特殊資源 JSON parameter 正規化為 executor 可安全持有的封閉值。
/// 此類別是無狀態靜態 pure boundary：不執行 RequestGuard、profile resolution、admission、Data8 或背景工作；
/// 失敗只讓 endpoint 回傳固定 400，因而不會將原始 body、base64、media type 或 parser 細節外洩。
/// </summary>
internal static class GatewayOperationParameterNormalizer
{
    private const int MaximumImagePayloadBytes = 32 * 1024;
    private const int MaximumBase64ImageCharacters = ((MaximumImagePayloadBytes + 2) / 3) * 4;
    private const string UtcSundayDateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>
    /// 複製 parameter map 並針對 P7.3 image write 轉換唯一允許的 nested JSON object。其他 operation 保留既有
    /// scalar JsonElement 正規化責任給 executor；本方法絕不擴張 caller 可指定的 entity、field、endpoint、
    /// profile 或 metadata schema。輸出 dictionary 是此方法的唯一 caller-visible allocation，呼叫端必須只在
    /// 當前 request 交給 RequestGuard/executor，不能放入 singleton/cache/queue。
    /// </summary>
    /// <param name="capabilityOperationId">已由 authorization 產生的 canonical capability ID，非 caller authority。</param>
    /// <param name="source">body reader 複製的 bounded parameter map；可能含 short-lived JsonElement。</param>
    /// <param name="parameters">成功時不再含 image raw JsonElement 的新 parameter map。</param>
    /// <returns>封閉形狀與 base64 基礎格式都符合時為 <see langword="true"/>。</returns>
    public static bool TryNormalize(
        string capabilityOperationId,
        IReadOnlyDictionary<string, object?>? source,
        out IReadOnlyDictionary<string, object?> parameters)
    {
        parameters = null!;
        if (string.IsNullOrWhiteSpace(capabilityOperationId))
        {
            return false;
        }

        var copy = source is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(source, StringComparer.Ordinal);

        if (capabilityOperationId is OperationIds.MemberInfoContactUpdateImage or
            OperationIds.NewPersonContactUpdateImage)
        {
            if (!copy.TryGetValue("imagePayload", out var rawPayload) ||
                !TryNormalizeImagePayload(rawPayload, out var imagePayload))
            {
                return false;
            }

            copy["imagePayload"] = imagePayload;
        }

        if (string.Equals(
                capabilityOperationId,
                OperationIds.MetadataOptionSetByAttribute,
                StringComparison.Ordinal))
        {
            if (!copy.TryGetValue("target", out var rawTarget) ||
                !TryNormalizeMetadataTarget(rawTarget, out var target))
            {
                return false;
            }

            copy["target"] = target;
        }

        if (string.Equals(
                capabilityOperationId,
                OperationIds.StatsMeetingRetrieveBySunday,
                StringComparison.Ordinal))
        {
            if (copy.Count != 1 ||
                !copy.TryGetValue("sundayDate", out var rawSundayDate) ||
                !TryNormalizeUtcSundayDate(rawSundayDate, out var sundayDate))
            {
                return false;
            }

            copy["sundayDate"] = sundayDate;
        }

        parameters = copy;
        return true;
    }

    /// <summary>
    /// 只接受 exact <c>{ imageBytes: base64-string, mediaKind: "Png"|"Jpeg" }</c> JSON object。未知欄位、
    /// duplicate、null、array、number、非 canonical enum、空白或過長 base64 均 fail closed。這裡只驗證
    /// wire shape/byte ceiling；ImageSharp decoder、實際格式、dimension 與 pixels 仍由 executor 在 admission
    /// 前驗證，形成不依賴單一層的防禦。decoded buffer 是此方法唯一 owner，DTO copy 完後一定清零。
    /// </summary>
    /// <param name="source">body reader 產生的單一 parameter value。</param>
    /// <param name="imagePayload">成功時的封閉 defensive-copy payload。</param>
    /// <returns>物件 shape、base64 與 enum 均符合時為 <see langword="true"/>。</returns>
    private static bool TryNormalizeImagePayload(object? source, out ContactImageResponseData? imagePayload)
    {
        imagePayload = null;
        if (source is not JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            return false;
        }

        string? base64 = null;
        ContactImageMediaKind? mediaKind = null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                return false;
            }

            if (string.Equals(property.Name, "imageBytes", StringComparison.Ordinal))
            {
                if (property.Value.ValueKind != JsonValueKind.String || base64 is not null)
                {
                    return false;
                }

                base64 = property.Value.GetString();
                continue;
            }

            if (string.Equals(property.Name, "mediaKind", StringComparison.Ordinal))
            {
                if (property.Value.ValueKind != JsonValueKind.String || mediaKind is not null ||
                    !TryParseImageMediaKind(property.Value.GetString(), out var parsedMediaKind))
                {
                    return false;
                }

                mediaKind = parsedMediaKind;
                continue;
            }

            return false;
        }

        if (names.Count != 2 || string.IsNullOrEmpty(base64) || mediaKind is null ||
            base64.Length > MaximumBase64ImageCharacters || base64.Length % 4 != 0 ||
            base64.Any(char.IsWhiteSpace))
        {
            return false;
        }

        byte[]? decodedBytes = null;
        try
        {
            decodedBytes = Convert.FromBase64String(base64);
            if (decodedBytes.Length is < 1 or > MaximumImagePayloadBytes)
            {
                return false;
            }

            imagePayload = new ContactImageResponseData(decodedBytes, mediaKind.Value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        finally
        {
            if (decodedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(decodedBytes);
            }
        }
    }

    /// <summary>
    /// 將 JSON string 限制為 wire contract 明列的 canonical enum 名稱。禁止數字、大小寫寬鬆比對、MIME、
    /// 副檔名與未來 enum 自動開放；新增格式時必須同時更新 DTO、normalizer、decoder policy 與負向測試。
    /// </summary>
    /// <param name="value">JSON string scalar，不會被保存。</param>
    /// <param name="mediaKind">成功時的封閉 media kind。</param>
    /// <returns>只有 Png 或 Jpeg 時為 <see langword="true"/>。</returns>
    private static bool TryParseImageMediaKind(string? value, out ContactImageMediaKind mediaKind)
    {
        if (string.Equals(value, "Png", StringComparison.Ordinal))
        {
            mediaKind = ContactImageMediaKind.Png;
            return true;
        }

        if (string.Equals(value, "Jpeg", StringComparison.Ordinal))
        {
            mediaKind = ContactImageMediaKind.Jpeg;
            return true;
        }

        mediaKind = default;
        return false;
    }

    /// <summary>
    /// 將唯一允許的 metadata target JSON string 收斂為封閉 enum。拒絕 numeric enum、任意 entity/attribute
    /// text、object、array、null 和大小寫寬鬆解析，讓 connector private switch 仍是 CRM schema 的唯一 authority。
    /// 本方法不讀取 locale/profile/generation，也不建立 metadata cache；那些只能由後續 runtime-owned cache 設計處理。
    /// </summary>
    /// <param name="source">body reader 複製的 target value。</param>
    /// <param name="target">成功時的固定 server target。</param>
    /// <returns>只有完整 canonical target string 時為 <see langword="true"/>。</returns>
    private static bool TryNormalizeMetadataTarget(object? source, out MetadataOptionSetTarget target)
    {
        target = default;
        if (source is not JsonElement { ValueKind: JsonValueKind.String } element)
        {
            return false;
        }

        if (!string.Equals(element.GetString(), "ContactCustomerTypeCode", StringComparison.Ordinal))
        {
            return false;
        }

        target = MetadataOptionSetTarget.ContactCustomerTypeCode;
        return true;
    }

    /// <summary>
    /// 將 weekly meeting capability 唯一允許的日期字串轉成 UTC <see cref="DateTimeOffset"/>。wire contract 刻意只接受
    /// <c>yyyy-MM-ddTHH:mm:ssZ</c>：尾端 <c>Z</c> 排除 caller 指定 offset，精確格式與 canonical round-trip 排除
    /// locale、空白、隱含時區與寬鬆 parser 差異；星期日午夜的限制使 connector 固定 query 不會因任意時間範圍擴張。
    /// 這個 pure conversion 只存活於目前 request 的新 dictionary，未保留 <see cref="JsonElement"/>、profile、endpoint、
    /// schema、principal、cache、timer 或 connector 資源，失敗時由 endpoint 在 admission 前 fail closed。
    /// </summary>
    /// <param name="source">body reader 複製的 <c>sundayDate</c> JSON parameter。</param>
    /// <param name="sundayDate">成功時的 server-validated UTC 星期日午夜值。</param>
    /// <returns>輸入為 canonical UTC 星期日午夜字串時為 <see langword="true"/>。</returns>
    private static bool TryNormalizeUtcSundayDate(object? source, out DateTimeOffset sundayDate)
    {
        sundayDate = default;
        if (source is not JsonElement { ValueKind: JsonValueKind.String } element)
        {
            return false;
        }

        var value = element.GetString();
        if (string.IsNullOrEmpty(value) ||
            !DateTimeOffset.TryParseExact(
                value,
                UtcSundayDateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out sundayDate) ||
            sundayDate.Offset != TimeSpan.Zero ||
            sundayDate.DayOfWeek != DayOfWeek.Sunday ||
            sundayDate.TimeOfDay != TimeSpan.Zero ||
            !string.Equals(
                value,
                sundayDate.ToString(UtcSundayDateFormat, CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            sundayDate = default;
            return false;
        }

        return true;
    }
}
