/*
 * 檔案整體說明
 *
 * 目的與定位：保護 webhook 在 JSON 解碼前以 wire 原始 UTF-8 bytes 驗證簽章，避免 Content-Type charset 或正規化改變驗簽內容。 故障注入刻意讓 header
 * charset 與實際 LINE UTF-8 body 不一致；正確結果須按 bytes 驗證並依 UTF-8 解析。
 *
 * 主要功能與流程：
 * 本檔宣告 WebhookRawSignatureTests。
 * - 錯誤 charset 不得在驗簽前重新解碼 body；簽章由測試獨立 HMAC 實作從原始 bytes 產生。
 * - 在原始已簽章內容任一 byte 被竄改後必須拒絕；錯誤須發生在 JSON 事件回傳之前。
 * - 案例擁有 body/header 和 request；內容只有合成事件欄位，不存檔、不連 LINE。
 * - 獨立使用 BCL HMAC 建立合成測試簽章；密鑰與 digest 僅存活於單一測試操作。
 * 測試案例入口包含 Webhook_ValidRawSignature_IgnoresContentCharsetAndParsesUtf8、
 * Webhook_TamperedRawBody_IsRejectedBeforeParsing、Request、Signature。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Line.Messaging.Webhooks;
using Xunit;

namespace Line.Messaging.Tests;

/// <summary>
/// 保護 webhook 在 JSON 解碼前以 wire 原始 UTF-8 bytes 驗證簽章，避免 Content-Type charset 或正規化改變驗簽內容。
/// 故障注入刻意讓 header charset 與實際 LINE UTF-8 body 不一致；正確結果須按 bytes 驗證並依 UTF-8 解析。
/// </summary>
public sealed class WebhookRawSignatureTests
{
    /// <summary>錯誤 charset 不得在驗簽前重新解碼 body；簽章由測試獨立 HMAC 實作從原始 bytes 產生。</summary>
    /// <returns>成功解析空事件清單的已排空 webhook 工作。</returns>
    [Fact]
    public async Task Webhook_ValidRawSignature_IgnoresContentCharsetAndParsesUtf8()
    {
        const string secret = "synthetic-channel-secret";
        var body = Encoding.UTF8.GetBytes("{\"destination\":\"synthetic-bot\",\"events\":[]}");
        using var request = Request(body, Signature(body, secret), "utf-16");

        var events = await request.GetWebhookEventsAsync(secret, "synthetic-bot");

        Assert.Empty(events);
    }

    /// <summary>在原始已簽章內容任一 byte 被竄改後必須拒絕；錯誤須發生在 JSON 事件回傳之前。</summary>
    /// <returns>已完成遭竄改 webhook 的拒絕檢查。</returns>
    [Fact]
    public async Task Webhook_TamperedRawBody_IsRejectedBeforeParsing()
    {
        const string secret = "synthetic-channel-secret";
        var body = Encoding.UTF8.GetBytes("{\"destination\":\"synthetic-bot\",\"events\":[]}");
        var signature = Signature(body, secret);
        body[body.Length - 2] ^= 1;
        using var request = Request(body, signature, "utf-8");

        await Assert.ThrowsAsync<InvalidSignatureException>(() => request.GetWebhookEventsAsync(secret, "synthetic-bot"));
    }

    /// <summary>案例擁有 body/header 和 request；內容只有合成事件欄位，不存檔、不連 LINE。</summary>
    private static HttpRequestMessage Request(byte[] body, string signature, string charset)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.invalid/webhook")
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
        {
            CharSet = charset
        };
        request.Headers.TryAddWithoutValidation("X-Line-Signature", signature);
        return request;
    }

    /// <summary>獨立使用 BCL HMAC 建立合成測試簽章；密鑰與 digest 僅存活於單一測試操作。</summary>
    private static string Signature(byte[] body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }
}
