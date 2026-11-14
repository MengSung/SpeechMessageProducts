/*
 * 檔案整體說明
 *
 * 目的與定位：提供 GoodShepherd 規則與待辦建立所需的跨 net462／net10.0 穩定 GUID
 * 產生器。它只依固定 purpose 與順序化純值計算，不依賴程序、時間、文化或隨機來源。
 *
 * 主要功能與流程：每個 part 以固定型別標記、長度與嚴格 UTF-8 表示加入 SHA-256 輸入，
 * 再取前 16 bytes，維持既有 byte[6] 高位 5 與 byte[8] variant 標記及 Guid(byte[]) 排列；
 * 此為產品自訂穩定格式，不宣稱為標準 UUID v5。相同輸入必得相同值，不同
 * 型別或順序不會因字串碰撞而誤合併；方法不建立全域快取。
 *
 * 使用邊界與維護：輸入應為不含秘密的正式識別或內容版本。雜湊緩衝區只在本次同步
 * 呼叫內存在，雜湊器在同步範圍內 Dispose，沒有背景工作或連線；這不是密碼學秘密保護，也不能
 * 取代資料庫唯一鍵或授權檢查。
 */
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SpeechMessageProducts.GoodShepherd.Core.Rules;

/// <summary>以固定 purpose 與有序純值產生跨目標一致的 GUID。</summary>
public static class DeterministicGuid
{
    /// <summary>以 UTF-8、Ordinal 及 SHA-256 計算穩定 GUID，不使用時間或隨機狀態。</summary>
    /// <param name="purpose">呼叫端擁有的用途名稱。</param>
    /// <param name="parts">最多 256 個有序純值：字串、Guid、布林、字元、整數、decimal、
    /// DateTime、DateTimeOffset、TimeSpan；拒絕 null、浮點、enum、集合與任意物件。
    /// 字串不修剪或正規化，以 Ordinal 保留原文；不得含秘密或未驗證個資。</param>
    /// <returns>相同輸入在兩個產品目標得到的相同 GUID。</returns>
    public static Guid Create(string purpose, params object[] parts)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(purpose);
#else
        if (purpose is null) throw new ArgumentNullException(nameof(purpose));
#endif
        if (purpose.Length == 0) throw new ArgumentException("A purpose is required.", nameof(purpose));
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(parts);
#else
        if (parts is null) throw new ArgumentNullException(nameof(parts));
#endif

        if (purpose.Length > 4096 || parts.Length > 256)
            throw new ArgumentException("Deterministic input exceeds its bounded budget.");
        var builder = new StringBuilder(purpose.Length + parts.Length * 32);
        AppendPart(builder, "purpose", purpose);
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index] ?? throw new ArgumentNullException(nameof(parts), "A deterministic part cannot be null.");
            AppendValue(builder, part);
        }

        // 嚴格 UTF-8 拒絕孤立 surrogate，不以 replacement character 合併不同輸入。
        var bytes = new UTF8Encoding(false, true).GetBytes(builder.ToString());
#if NET10_0_OR_GREATER
        var digest = SHA256.HashData(bytes);
#else
        byte[] digest;
        using (var sha = SHA256.Create()) digest = sha.ComputeHash(bytes);
#endif
        digest[6] = (byte)((digest[6] & 0x0F) | 0x50);
        digest[8] = (byte)((digest[8] & 0x3F) | 0x80);
        var guidBytes = new byte[16];
        Buffer.BlockCopy(digest, 0, guidBytes, 0, guidBytes.Length);
        return new Guid(guidBytes);
    }

    /// <summary>
    /// 使用固定型別白名單與 invariant 格式；不呼叫未知物件的 ToString。
    /// 整數位寬保留於 tag；decimal G29 去尾零。DateTime 以不帶環境時區的 O 時計值
    /// 加明確 Kind 保存語意；DateTimeOffset O 保留 offset。浮點格式在舊新 runtime
    /// 有相容風險，因此必須由呼叫端先轉成核定 decimal 或固定版本字串。
    /// </summary>
    private static void AppendValue(StringBuilder builder, object value)
    {
        var invariant = CultureInfo.InvariantCulture;
        switch (value)
        {
            case string text: AppendPart(builder, "string", text); break;
            case Guid guid: AppendPart(builder, "guid", guid.ToString("D")); break;
            case bool boolean: AppendPart(builder, "bool", boolean ? "true" : "false"); break;
            case char character: AppendPart(builder, "char", character.ToString()); break;
            case sbyte number: AppendPart(builder, "int8", number.ToString(invariant)); break;
            case byte number: AppendPart(builder, "uint8", number.ToString(invariant)); break;
            case short number: AppendPart(builder, "int16", number.ToString(invariant)); break;
            case ushort number: AppendPart(builder, "uint16", number.ToString(invariant)); break;
            case int number: AppendPart(builder, "int32", number.ToString(invariant)); break;
            case uint number: AppendPart(builder, "uint32", number.ToString(invariant)); break;
            case long number: AppendPart(builder, "int64", number.ToString(invariant)); break;
            case ulong number: AppendPart(builder, "uint64", number.ToString(invariant)); break;
            case decimal number: AppendPart(builder, "decimal", number.ToString("G29", invariant)); break;
            case DateTime time:
                AppendPart(builder, "datetime", DateTime.SpecifyKind(time, DateTimeKind.Unspecified).ToString("O", invariant) + ":" + time.Kind);
                break;
            case DateTimeOffset time: AppendPart(builder, "datetimeoffset", time.ToString("O", invariant)); break;
            case TimeSpan duration: AppendPart(builder, "timespan", duration.ToString("c", invariant)); break;
            default: throw new ArgumentException("Unsupported deterministic part type.", nameof(value));
        }
    }

    /// <summary>加入帶長度的欄位，避免相鄰字串拼接造成邊界歧義。</summary>
    private static void AppendPart(StringBuilder builder, string name, string value)
    {
        if (value.Length > 32768) throw new ArgumentException("Deterministic field exceeds its bounded budget.", nameof(value));
        builder.Append(name.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(name);
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');
    }
}
