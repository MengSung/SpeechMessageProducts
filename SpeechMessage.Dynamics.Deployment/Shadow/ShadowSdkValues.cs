/*
 * 檔案整體說明
 *
 * 目的與定位：Deployment 影子情境的單欄純轉換器，將已核定欄位的 JSON 值
 * 依 SDK metadata 轉成獨立 SDK scalar；不查詢、不寫入、不讀取秘密或推斷授權。
 * 主要功能與流程：先驗證支援型別及必要 metadata，再處理 null、字串、數值、
 * 選項、日期與明確 lookup marker。decimal 使用 GetDecimal 並以整數係數／十進位
 * 指數核對原始數值，防止框架靜默捨入。lookup 經 caller 的 alias/entity resolver
 * 核對後只複製正式實體與 GUID，不保留名稱、alternate key 或來源可變參照。
 * 使用邊界與維護：caller 負責欄位存在、可寫性、主鍵限制、組織及別名授權；
 * 本檔不把 metadata 或 alias 當作授權證明。字串最多 65536 字元，數值 token
 * 最多 256 字元，metadata options／targets 亦有界；輸入拒絕使用固定錯誤。
 * 所有候選只存活一次同步呼叫，不保留 JSON、resolver、Session 或共享可變狀態。
 * resolver 由 caller 提供操作區域純函式，真正執行故障保留給外層共用告警；
 * 本檔不建立 task、計時器、連線或 disposable，文件由 caller 釋放。
 * 維護時執行合成 metadata 聚焦測試及 UTF-8／CRLF 位元組檢查，不冒充實機驗收。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>
/// 以明確 metadata 白名單解碼單欄值；不保存輸入或查詢服務，所有 SDK 可變輸出均屬 caller。
/// 欄位授權與存在性必須由呼叫端先完成，不能以此轉換成功代替授權。
/// </summary>
internal static class ShadowSdkValues
{
    /// <summary>
    /// 轉換已核定的單欄 JSON。先驗必要 metadata 才允許 null；lookup 只接受別名 marker，
    /// 並以新的 EntityReference 回傳經 resolver 核對的非空正式身分。
    /// 不支援、超量或錯型別輸入以固定 InvalidDataException 拒絕，不含原始值。
    /// </summary>
    /// <param name="value">目前操作文件中的單欄值；方法不保留 JsonElement。</param>
    /// <param name="metadata">caller 已驗證屬於目標欄位的權威 metadata。</param>
    /// <param name="resolveAlias">操作區域純解析器，依序接受 alias 與 entity，不能從原始 GUID 猜測授權。</param>
    /// <returns>不可變 scalar、null 或由 caller 獨占的 SDK 包裝。</returns>
    internal static object? Convert(JsonElement value, AttributeMetadata metadata,
        Func<string, string, EntityReference> resolveAlias)
    {
        if (metadata is null || resolveAlias is null) throw InvalidValue();
        ValidateMetadata(metadata);
        if (value.ValueKind == JsonValueKind.Null) return null;
        switch (metadata)
        {
            case StringAttributeMetadata text:
                return ReadString(value, text.MaxLength!.Value);
            case MemoAttributeMetadata memo:
                return ReadString(value, memo.MaxLength!.Value);
            case IntegerAttributeMetadata integer:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var intValue) ||
                    intValue < integer.MinValue || intValue > integer.MaxValue) throw InvalidValue();
                return intValue;
            case BigIntAttributeMetadata big:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var longValue) ||
                    longValue < big.MinValue || longValue > big.MaxValue) throw InvalidValue();
                return longValue;
            case DecimalAttributeMetadata number:
                return ReadDecimal(value, number.MinValue!.Value, number.MaxValue!.Value);
            case MoneyAttributeMetadata money:
                return new Money(ReadDecimal(value, MoneyBound(money.MinValue!.Value), MoneyBound(money.MaxValue!.Value)));
            case BooleanAttributeMetadata:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw InvalidValue();
                return value.GetBoolean();
            case EnumAttributeMetadata choice:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var option) ||
                    !choice.OptionSet.Options.Any(item => item.Value == option)) throw InvalidValue();
                return new OptionSetValue(option);
            case LookupAttributeMetadata lookup:
                return ReadLookup(value, lookup, resolveAlias);
            case DateTimeAttributeMetadata date:
                return ReadDate(value, date);
            default:
                throw InvalidValue();
        }
    }

    /// <summary>
    /// 未知型別即使值為 null 仍拒絕；驗證必要範圍、日期行為及完整選項集合，
    /// 不以缺少 metadata 的預設值放寬輸入。合法大 memo 仍受每次輸入容量上限限制。
    /// </summary>
    private static void ValidateMetadata(AttributeMetadata metadata)
    {
        var valid = metadata switch
        {
            StringAttributeMetadata item => item.AttributeType == AttributeTypeCode.String && item.MaxLength is > 0,
            MemoAttributeMetadata item => item.AttributeType == AttributeTypeCode.Memo && item.MaxLength is > 0,
            IntegerAttributeMetadata item => item.AttributeType == AttributeTypeCode.Integer &&
                item.MinValue.HasValue && item.MaxValue.HasValue && item.MinValue <= item.MaxValue,
            BigIntAttributeMetadata item => item.AttributeType == AttributeTypeCode.BigInt &&
                item.MinValue.HasValue && item.MaxValue.HasValue && item.MinValue <= item.MaxValue,
            DecimalAttributeMetadata item => item.AttributeType == AttributeTypeCode.Decimal &&
                item.MinValue.HasValue && item.MaxValue.HasValue && item.MinValue <= item.MaxValue,
            MoneyAttributeMetadata item => item.AttributeType == AttributeTypeCode.Money &&
                item.MinValue.HasValue && item.MaxValue.HasValue && double.IsFinite(item.MinValue.Value) &&
                double.IsFinite(item.MaxValue.Value) && item.MinValue <= item.MaxValue &&
                decimal.TryParse(item.MinValue.Value.ToString("R", CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out _) &&
                decimal.TryParse(item.MaxValue.Value.ToString("R", CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            BooleanAttributeMetadata item => item.AttributeType == AttributeTypeCode.Boolean,
            PicklistAttributeMetadata item => item.AttributeType == AttributeTypeCode.Picklist && ValidOptions(item),
            StateAttributeMetadata item => item.AttributeType == AttributeTypeCode.State && ValidOptions(item),
            StatusAttributeMetadata item => item.AttributeType == AttributeTypeCode.Status && ValidOptions(item),
            LookupAttributeMetadata item => item.AttributeType is AttributeTypeCode.Lookup or AttributeTypeCode.Customer or AttributeTypeCode.Owner &&
                item.Targets is { Length: > 0 and <= 128 } && item.Targets.All(ValidEntity) &&
                item.Targets.Distinct(StringComparer.Ordinal).Count() == item.Targets.Length,
            DateTimeAttributeMetadata item => item.AttributeType == AttributeTypeCode.DateTime &&
                item.DateTimeBehavior?.Value is "UserLocal" or "DateOnly" or "TimeZoneIndependent",
            _ => false
        };
        if (!valid) throw InvalidValue();
    }

    /// <summary>選項必須有界且每個 ID 唯一完整，不能讓不完整或重複 metadata 被當成有效定義。</summary>
    private static bool ValidOptions(EnumAttributeMetadata metadata)
    {
        if (metadata.OptionSet?.Options is not { Count: > 0 and <= 65_536 } options) return false;
        var ids = new HashSet<int>();
        foreach (var item in options)
            if (item?.Value is not int id || !ids.Add(id)) return false;
        return true;
    }

    /// <summary>僅接受 JSON 字串並同時遵守 metadata 及 65536 UTF-16 字元的操作容量。</summary>
    private static string ReadString(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.String) throw InvalidValue();
        var text = value.GetString()!;
        if (text.Length > maximum || text.Length > 65_536) throw InvalidValue();
        return text;
    }

    /// <summary>
    /// SDK Money 的範圍原生為 double；以其 invariant round-trip 表示轉成比較界限，
    /// 不讓輸入金額經 double 轉換。metadata 無法表為 decimal 時已先拒絕。
    /// </summary>
    private static decimal MoneyBound(double value) =>
        decimal.Parse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// GetDecimal 可能靜默捨入，因此以十進位整數正規形比較原始 token 與解析結果。
    /// token 上限先於 BigInteger 配置；不計算巨大十次冪，拒絕超量或非精確可表示值。
    /// </summary>
    private static decimal ReadDecimal(JsonElement value, decimal minimum, decimal maximum)
    {
        if (value.ValueKind != JsonValueKind.Number) throw InvalidValue();
        var raw = value.GetRawText();
        if (raw.Length > 256) throw InvalidValue();
        decimal number;
        try { number = value.GetDecimal(); }
        catch (FormatException) { throw InvalidValue(); }
        if (Canonical(raw) != Canonical(number.ToString(CultureInfo.InvariantCulture)) ||
            number < minimum || number > maximum) throw InvalidValue();
        return number;
    }

    /// <summary>
    /// 正規形以無尾零係數與 scale 表示數值；零不帶 scale，指數只作整數運算。
    /// 因此多餘尾零及科學記號可接受，但非零尾數下溢或捨入無法通過相等檢查。
    /// </summary>
    private static (BigInteger Coefficient, long Scale) Canonical(string raw)
    {
        var exponentAt = raw.IndexOfAny(['e', 'E']);
        var mantissa = exponentAt < 0 ? raw : raw[..exponentAt];
        var exponent = 0;
        if (exponentAt >= 0 && !int.TryParse(raw.AsSpan(exponentAt + 1),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) throw InvalidValue();
        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        long scale = (point < 0 ? 0 : mantissa.Length - point - 1) - (long)exponent;
        var coefficient = BigInteger.Parse(mantissa.Replace(".", "", StringComparison.Ordinal),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (coefficient.IsZero) return (BigInteger.Zero, 0);
        while (coefficient % 10 == 0)
        {
            coefficient /= 10;
            scale--;
        }
        return (coefficient, scale);
    }

    /// <summary>
    /// marker 僅能有一個 ref／alias、一個 entity 及可選的字串 name。
    /// 所有結構與 target 核對先於 resolver；name 被忽略，正式身分以新物件回傳。
    /// </summary>
    private static EntityReference ReadLookup(JsonElement value, LookupAttributeMetadata metadata,
        Func<string, string, EntityReference> resolveAlias)
    {
        if (value.ValueKind != JsonValueKind.Object) throw InvalidValue();
        string? alias = null;
        string? entity = null;
        var properties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (properties.Count == 3 || !properties.Add(property.Name)) throw InvalidValue();
            switch (property.Name)
            {
                case "ref":
                case "alias":
                    if (alias is not null) throw InvalidValue();
                    alias = ReadString(property.Value, 256);
                    break;
                case "entity":
                    entity = ReadString(property.Value, 128);
                    break;
                case "name":
                    _ = ReadString(property.Value, 65_536);
                    break;
                default:
                    throw InvalidValue();
            }
        }
        if (!ShadowProjection.ValidIdentity(alias) || Guid.TryParse(alias, out _) ||
            !ValidEntity(entity) || !metadata.Targets.Contains(entity, StringComparer.Ordinal)) throw InvalidValue();
        var result = resolveAlias(alias!, entity!);
        if (result is null || result.Id == Guid.Empty ||
            !string.Equals(result.LogicalName, entity, StringComparison.Ordinal)) throw InvalidValue();
        return new EntityReference(entity!, result.Id);
    }

    /// <summary>實體為正式 schema 識別字，不能以空白、點或路徑語法擴大 alias 的解析範圍。</summary>
    private static bool ValidEntity(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 128 &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    /// <summary>
    /// 日期只接受明確 ISO 拼字及最多七位小數秒。UserLocal 必須帶 Z／offset 並轉 UTC；
    /// DateOnly 僅日期，TimeZoneIndependent 僅無 offset 的牆上時間，避免猜測主機時區。
    /// </summary>
    private static DateTime ReadDate(JsonElement value, DateTimeAttributeMetadata metadata)
    {
        var text = ReadString(value, 64);
        if (metadata.DateTimeBehavior.Value == "UserLocal")
        {
            var hasZone = text.EndsWith('Z') ||
                (text.Length >= 6 && text[^3] == ':' && text[^6] is '+' or '-');
            if (!hasZone || !DateTimeOffset.TryParseExact(text,
                ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var instant)) throw InvalidValue();
            return instant.UtcDateTime;
        }
        var format = metadata.DateTimeBehavior.Value == "DateOnly"
            ? "yyyy-MM-dd" : "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF";
        if (!DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
            throw InvalidValue();
        return DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
    }

    /// <summary>固定拒絕訊息，避免把原始 JSON、身分、metadata 或 resolver 設定寫進診斷輸出。</summary>
    private static InvalidDataException InvalidValue() => new("Shadow SDK field value is invalid.");
}
