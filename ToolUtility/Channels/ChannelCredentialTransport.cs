/*
檔案整體說明
目的與定位：F3 提供 F5 將來可組合的有界傳輸委派，不建立收件人、訊息、推播網址或推播出口。
主要功能與流程：以目錄憑證執行一次委派；第一次 401 釋放回應並失效再解析一次，第二次 401 結束失敗且告警一次，不會進行第三次傳輸。
使用邊界與維護：委派必須尊重十秒操作 token、每次建立獨立 request 並消費所需本文；回傳的 HttpResponseMessage 所有權移交此類別釋放。不可在委派內 Dispose 目錄、保存憑證或使用使用者 Session。僅 caller 明確取消不告警，所有資源在返回前排空；不建立背景重試工作。
*/
#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core;
namespace ToolUtilityNameSpace.Channels;

/// <summary>不含憑證或回應本文的單次傳輸摘要；業務層仍負責檢查成功狀態。</summary>
public sealed record ChannelTransportResult(bool Succeeded, HttpStatusCode StatusCode);

/// <summary>以同一十秒 operation lease 執行至多兩次 transport delegate，不實作訊息或 F5 推播出口。</summary>
public sealed class ChannelCredentialTransport
{
    private readonly DataverseChannelDirectory directory;
    /// <summary>借入固定目錄；不得比目錄先釋放共享 SDK、key 或診斷 owner。</summary>
    public ChannelCredentialTransport(DataverseChannelDirectory directory) => this.directory = directory ?? throw new ArgumentNullException(nameof(directory));

    /// <summary>
    /// response 所有權移交此 wrapper，會在每次結果判斷前 Dispose；第一次 401 只刷新一次，第二次即停用及告警。
    /// send 必須每次建立全新 request/body、不得保存憑證，且必須 await 完整傳輸；callback fault 保留原例外。
    /// </summary>
    public async Task<ChannelTransportResult> SendAsync(string channelKey,
        Func<LineChannelCredential, CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);
        using var operation = directory.Begin(cancellationToken);
        try
        {
            var entry = await directory.ResolveAsync(channelKey, operation.Token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Channel is not available in the fixed product directory.");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                operation.Token.ThrowIfCancellationRequested();
                HttpResponseMessage response;
                try { response = await send(entry.Credential, operation.Token).ConfigureAwait(false); }
                catch (Exception exception) { directory.Failure(exception, "ChannelTransport.Send", operation); throw; }
                if (response == null) throw new InvalidOperationException("Channel transport returned no response.");
                var status = response.StatusCode;
                response.Dispose();
                if (status != HttpStatusCode.Unauthorized)
                    return new ChannelTransportResult((int)status is >= 200 and < 300, status);
                if (attempt == 0)
                {
                    directory.Invalidate(entry.Generation);
                    entry = await directory.ResolveAsync(channelKey, operation.Token).ConfigureAwait(false)
                        ?? throw new KeyNotFoundException("Channel disappeared during credential refresh.");
                    continue;
                }
                await directory.DisableAsync(entry, new ChannelHealthResult(false, 0, 0, directory.UtcNow), operation).ConfigureAwait(false);
                // 第二次 401 是本 wrapper 定義的終端結果；告警已由 DisableAsync 發出，不再重拋造成第二筆診斷。
                return new ChannelTransportResult(false, HttpStatusCode.Unauthorized);
            }
            throw new InvalidOperationException("Channel transport exceeded its retry bound.");
        }
        catch (Exception exception)
        {
            directory.Failure(exception, "ChannelTransport.TerminalFailure", operation);
            throw;
        }
    }
}
