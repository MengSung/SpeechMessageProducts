/*
 * 檔案整體說明
 * 目的與定位：F10金流核心發布ProviderData與Diagnostics前的純值遮罩邊界；供永豐、MyPay等
 * provider共同呼叫，不解析付款授權、不入帳，也不擁有宿主logger或Exception.log。
 * 主要功能與流程：先遮蔽含密碼／授權標頭的機密鍵，再處理Token；八字元以下完整遮蔽，較長Token維持首尾
 * 四字元格式。只有精確ATM付款指示欄位可保留原值；其他值以單次線性掃描遮蔽13–19位數字
 * 卡號，涵蓋空白、連字號、句點、斜線及描述中的多組卡號，保持前六後四顯示契約；有分隔符且
 * 超過19位的疑似合併區段一律完整遮蔽，不能靠附加郵遞區號等數字讓完整卡號漏出。
 * 使用邊界與維護：輸入字典與非null字串由呼叫端擁有，每次回傳獨立字典；本檔只持有固定
 * 欄位名稱，不保存原值、使用者、profile或跨操作快取。最多保留本次輸入與輸出字串生命期，
 * 無I/O、連線、timer、背景工作或Dispose責任。掃描O(n)，固定19字元stack暫存；只有發現
 * 卡號才建立輸出builder，不用正則回溯或全域鎖。大小寫重複鍵由ToDictionary拒絕，不能覆寫。
 * 驗證由PaymentDiagnosticsSanitizerTests與真Sinopac回呼parser的合成診斷輸出完成；不把它
 * 宣稱為網站日誌、銀行實機或產品最終入帳驗收。ATM欄位例外是明確付款資料契約，不是授權。
 */
using System.Text;

namespace SpeechMessage.Payments.Diagnostics;

/// <summary>
/// 金流核心的無狀態診斷遮罩。輸入保留在單次呼叫，輸出為獨立副本；不把可記錄的診斷字典
/// 與明確供後續銀行查詢的ProviderTransactionId混用，也不負責宿主的存取授權與日誌生命期。
/// </summary>
public static class PaymentDiagnosticsSanitizer
{
    private static readonly string[] SecretKeyFragments =
    [
        "signature", "hash", "storekey", "storeiv", "secret", "xkey", "cvv", "cvc",
        "password", "passwd", "pwd", "authorization"
    ];

    private static readonly HashSet<string> ExactSecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "key", "iv", "a1", "a2", "b1", "b2"
    };

    /// <summary>
    /// 在發布前將本次provider欄位轉成獨立遮罩副本。保留非機密業務欄位與精確ATM付款指示；
    /// 機密／Token檢查優先，任意欄位名稱嵌入ATM片段不能繞過。大小寫重複鍵拒絕而非覆寫。
    /// </summary>
    /// <param name="values">本次操作擁有且值非null的provider欄位；本方法不修改或跨呼叫保存。</param>
    /// <returns>由呼叫端擁有的獨立診斷字典；輸出只有原契約允許的遮罩片段。</returns>
    /// <exception cref="ArgumentNullException">字典或欄位值為null時拒絕，不發布不完整結果。</exception>
    /// <exception cref="ArgumentException">大小寫不敏感鍵重複時拒絕，不靜默合併。</exception>
    public static IReadOnlyDictionary<string, string> Sanitize(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.ToDictionary(
            pair => pair.Key,
            pair => SanitizeValue(pair.Key, pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 以機密→Token→精確ATM例外→卡號的順序處理單值；順序不得改成ATM片段優先，否則可用
    /// 偽裝鍵繞過遮罩。短Token沒有安全的首尾顯示區間，必須完整遮蔽；長Token沿既有格式。
    /// </summary>
    private static string SanitizeValue(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (IsSecretKey(key)) return "***";
        if (IsTokenKey(key)) return value.Length <= 8 ? "***" : $"{value[..4]}...{value[^4..]}";
        if (IsSafeBankAccountKey(key)) return value;
        return MaskCardNumbers(value);
    }

    /// <summary>Token鍵採大小寫不敏感比對；長度只決定遮罩形式，不能決定是否略過遮罩。</summary>
    private static bool IsTokenKey(string key) => key.Contains("token", StringComparison.OrdinalIgnoreCase);

    /// <summary>固定機密鍵及既有片段一律完整遮蔽；只讀名稱集合不保存任何provider值。</summary>
    private static bool IsSecretKey(string key)
    {
        return ExactSecretKeys.Contains(key) ||
            SecretKeyFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
            (key.Contains("key", StringComparison.OrdinalIgnoreCase) && !IsTokenKey(key));
    }

    /// <summary>
    /// 只容許既有ATM付款指示的精確別名；忽略欄位名稱的底線與連字號，但不接受任意前後綴。
    /// 此例外不判定字串的帳戶所有權，也不能套用到Token或金鑰欄位。
    /// </summary>
    private static bool IsSafeBankAccountKey(string key)
    {
        var normalizedKey = key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        return string.Equals(normalizedKey, "atmpayno", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedKey, "virtualaccount", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 單向掃描所有數字及空白／連字號／句點／斜線區段；13–19位卡號只保留前六後四。
    /// 每個字元最多掃描一次，固定19字元scratch不隨輸入增加，且未命中時直接回原字串。
    /// 超過19位的純業務數字不截取部分誤當卡號；有分隔符的過長候選完整遮蔽，避免把卡號與
    /// 相鄰數字合併後誤判為非卡號。輸出只由本次區域builder擁有。
    /// </summary>
    private static string MaskCardNumbers(string value)
    {
        Span<char> digits = stackalloc char[19];
        StringBuilder? output = null;
        var copyFrom = 0;
        var position = 0;
        while (position < value.Length)
        {
            if (!char.IsDigit(value[position]))
            {
                position++;
                continue;
            }

            var start = position;
            var count = 0;
            var digitsEnd = position;
            var gapAfterDigit = false;
            var hasInternalSeparator = false;
            while (position < value.Length)
            {
                var character = value[position];
                if (char.IsDigit(character))
                {
                    hasInternalSeparator |= gapAfterDigit;
                    gapAfterDigit = false;
                    if (count < digits.Length) digits[count] = character;
                    count++;
                    digitsEnd = position + 1;
                }
                else if (character is '-' or '.' or '/' || char.IsWhiteSpace(character)) gapAfterDigit = true;
                else break;
                position++;
            }

            if (count < 13 || (count > 19 && !hasInternalSeparator)) continue;
            output ??= new StringBuilder(value.Length);
            output.Append(value, copyFrom, start - copyFrom);
            if (count > 19) output.Append("***");
            else
            {
                output.Append(digits[..6]);
                output.Append("******");
                output.Append(digits.Slice(count - 4, 4));
            }
            copyFrom = digitsEnd;
        }
        if (output is null) return value;
        output.Append(value, copyFrom, value.Length - copyFrom);
        return output.ToString();
    }
}
