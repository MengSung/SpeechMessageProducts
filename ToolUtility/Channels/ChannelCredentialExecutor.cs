/*
檔案整體說明
目的與定位：F5 純值傳輸接縫，借用 F3 固定產品目錄與 generation owner，不擁有 SDK 或 HTTP。
主要功能與流程：同一十秒 operation 內第一次 401 失效觀察世代並重新解析，第二次停用。
HTTP 回應已由 Core 釋放，只檢查純值；傳輸期間 operation 期限或停止取消轉為不確定原例外，
交最終 Gateway 決定一次同鍵重試。真正來源故障依目錄原規則處理，正常 caller 取消不告警。
使用邊界與維護：無快取、背景工作或新連線；using operation 排空並釋放 timer/registration。
主機須先停止 Gateway 再關閉目錄，停止中任何新重試會被 Begin 拒絕，不能把舊憑證帶往其他產品。
*/
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Gateway;

namespace ToolUtilityNameSpace.Channels;

/// <summary>固定產品目錄的純值 adapter，不取得底層來源、金鑰或 HTTP client 所有權。</summary>
public sealed class ChannelCredentialExecutor : ILineCredentialExecutor
{
    private readonly DataverseChannelDirectory directory;

    /// <summary>借入 F3 owner；本物件不得比目錄存活更久，主機需先排空 Gateway。</summary>
    public ChannelCredentialExecutor(DataverseChannelDirectory directory) =>
        this.directory = directory ?? throw new ArgumentNullException(nameof(directory));

    /// <summary>相容來源轉接，不複製或保存秘密。</summary>
    public bool TryGetCredential(string channelKey, out LineChannelCredential? credential) =>
        directory.TryGetCredential(channelKey, out credential);
    /// <summary>
    /// 串行執行 Core 純值委派至多兩次，只有首次 401 刷新；不重試未確定傳輸，
    /// 也不在可恢復的 operation 取消時提前告警。委派不得保存 credential 或自行 new client。
    /// </summary>
    public async Task<LinePushResult> ExecuteAsync(string channelKey,
        Func<LineChannelCredential, CancellationToken, Task<LinePushResult>> send, CancellationToken cancellationToken)
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
                LinePushResult result;
                try { result = await send(entry.Credential, operation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // Core 已排空 HTTP；operation 自己的十秒期限不等於 Gateway caller 的正常取消。
                    // 下一次嘗試必須重新 Begin，不能用已取消的 operation token 重試。
                    return LinePushResult.TransportFailure(exception);
                }
                if (result is null) throw new InvalidOperationException("Channel transport returned no result.");
                if (result.StatusCode != HttpStatusCode.Unauthorized) return result;
                if (attempt == 0)
                {
                    directory.Invalidate(entry.Generation);
                    entry = await directory.ResolveAsync(channelKey, operation.Token).ConfigureAwait(false)
                        ?? throw new KeyNotFoundException("Channel disappeared during credential refresh.");
                    continue;
                }
                await directory.DisableAsync(entry, new ChannelHealthResult(false, 0, 0, directory.UtcNow), operation).ConfigureAwait(false);
                return result; // 相同正式版本的 401 已由 F3 通知，不新造第二筆例外。
            }
            throw new InvalidOperationException("Channel transport exceeded its retry bound.");
        }
        catch (Exception exception)
        {
            directory.Failure(exception, "ChannelCredentialExecutor.Execute", operation);
            throw;
        }
    }
}
