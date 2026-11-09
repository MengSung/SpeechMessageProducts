/*
檔案整體說明

目的與定位：提供 LINE Webhook 的正式 ASP.NET Core 驗簽 adapter，將 HTTP adapter
交付的原始 body bytes 與 signature 交給既有 LineSignature；不解析 JSON、不持有 request。

主要功能與流程：先檢查 body／channel／header 的有界形狀，再向 Tenancy resolver
取得本次 channel secret，複製 ReadOnlyMemory<byte> 成一次性 byte[]，呼叫
Line.Messaging.LineSignature.Verify，最後清零並釋放自有陣列。錯誤簽章回 false；
caller cancellation 保留原取消，設定或 crypto 故障保留原始例外交給 Host diagnostics。

使用邊界與維護：resolver 與 IConfiguration 的 owner 由 DI／Host 管理；本型別只在
單次同步操作內借用 secret，不快取、記錄、序列化或交給 envelope／queue。body copy
最大一 MiB 並在 finally 清零；不建立 timer、task、subscription、connection 或
跨 request 狀態。真正的公開 URL、IIS 與 LINE 平台結果由 P3-09 實機驗收證明。
*/
using System.Security.Cryptography;
using Line.Messaging;
using LineMessagingProcessor.AspNetCore.Webhooks;
using SpeechMessage.Tenancy;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Signatures;

/// <summary>
/// 以 Tenancy 頻道 secret 呼叫既有 LineSignature 的無狀態 Webhook verifier。
/// </summary>
public sealed class LineWebhookSignatureVerifier : IWebhookSignatureVerifier
{
    private readonly IWebhookChannelSecretResolver secretResolver;

    /// <summary>建立借用 resolver 的 verifier，不接管設定來源或其生命週期。</summary>
    /// <param name="secretResolver">固定部署 mapping resolver。</param>
    public LineWebhookSignatureVerifier(IWebhookChannelSecretResolver secretResolver) =>
        this.secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));

    /// <summary>
    /// 只以原始 bytes 驗簽；每次操作的自有 buffer 在返回前清零，禁止跨 request 保留。
    /// </summary>
    /// <param name="channelId">未授權的 route channelId，先經 mapping 判定。</param>
    /// <param name="body">HTTP adapter 借出的原始 body。</param>
    /// <param name="signature">單一 X-Line-Signature header。</param>
    /// <param name="cancellationToken">caller cancellation，於 crypto 前檢查。</param>
    /// <returns>合法 channel、secret 與 HMAC 全部通過時為 true。</returns>
    public ValueTask<bool> VerifyAsync(string channelId, ReadOnlyMemory<byte> body, string signature,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WebhookOptions.IsValidChannelId(channelId) || body.Length > WebhookOptions.MaximumBodyBytes ||
            signature is not { Length: > 0 and <= 256 })
            return ValueTask.FromResult(false);
        if (!secretResolver.TryResolveSecret(channelId, out var secret))
            return ValueTask.FromResult(false);

        var copy = body.ToArray();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(LineSignature.Verify(copy, signature, secret!));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copy);
        }
    }
}
