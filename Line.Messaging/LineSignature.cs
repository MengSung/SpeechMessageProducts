/*
 * 檔案整體說明
 *
 * 目的與定位：與 HTTP 框架無關的原始位元組驗簽入口；無共用 HMAC、密鑰快取或使用者狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 LineSignature。
 * - 驗證原始 request body 的 HMAC-SHA256，不先解碼 JSON、正規化文字或重新序列化。
 * 主要操作入口包含 Verify。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 Line.Messaging.Tests/LineSdkFoundationTests.cs、Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Security.Cryptography;
using System.Text;

namespace Line.Messaging;

/// <summary>與 HTTP 框架無關的原始位元組驗簽入口；無共用 HMAC、密鑰快取或使用者狀態。</summary>
public static class LineSignature
{
    /// <summary>驗證原始 request body 的 HMAC-SHA256，不先解碼 JSON、正規化文字或重新序列化。</summary>
    /// <param name="body">呼叫端擁有的原始內容，方法不修改或保存。</param>
    /// <param name="signatureHeader">X-Line-Signature 的 Base64 SHA256 digest。</param>
    /// <param name="channelSecret">本次已授權 channel 的密鑰；不允許空設定。</param>
    /// <returns>格式不合或 digest 不符回 false；密鑰及 body 的設定錯誤明確拋出。</returns>
    public static bool Verify(byte[] body, string signatureHeader, string channelSecret)
    {
        if (body == null) throw new ArgumentNullException(nameof(body));
        if (string.IsNullOrWhiteSpace(channelSecret)) throw new ArgumentException("Channel secret must not be blank.", nameof(channelSecret));
        // SHA256 的 Base64 恆為 44 字元；先限制長度避免不可信 header 造成任意大小配置。
        // 格式與長度不是秘密，只有相同長度 digest 的比較需要固定時間。
        if (signatureHeader == null || signatureHeader.Length != 44) return false;
        byte[] supplied;
        try { supplied = Convert.FromBase64String(signatureHeader); }
        catch (FormatException) { return false; }
        if (supplied.Length != 32) return false;

        var key = Encoding.UTF8.GetBytes(channelSecret);
        byte[] digest = null;
        try
        {
            // 每次操作獨享密鑰與 HMAC，沒有 static crypto instance。using 先處置 HMAC，
            // finally 再清掉呼叫內產生的密鑰與摘要；不修改或持有借入的原始 body。
            using (var hmac = new HMACSHA256(key)) digest = hmac.ComputeHash(body);
#if NETFRAMEWORK
            var difference = 0;
            for (var index = 0; index < 32; index++) difference |= digest[index] ^ supplied[index];
            return difference == 0;
#else
            return CryptographicOperations.FixedTimeEquals(digest, supplied);
#endif
        }
        finally
        {
            Array.Clear(key, 0, key.Length);
            if (digest != null) Array.Clear(digest, 0, digest.Length);
            Array.Clear(supplied, 0, supplied.Length);
        }
    }
}
