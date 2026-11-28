/*
檔案整體說明
目的與定位：銜接 F3 固定產品目錄與 F5 已確定性釋放 HTTP 回應的純值傳輸結果。
主要功能與流程：adapter 借用 F3 generation owner，首次 401 只失效觀察世代並重新解析一次；
再次 401 停用同版本 Channel 並透過共用診斷 owner 告警。既有 HttpResponseMessage 契約不改。
使用邊界與維護：委派不得保存憑證或另做重試，必須 await 排空後才返回純值。實作只借用
目錄而非擁有其生命週期；來源、Gateway、診斷必須屬於同一固定產品，禁止跨租戶快取請求。
*/
using System;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core.ChannelPool;

namespace LineMessagingProcessor.Core.Gateway;

/// <summary>F3 憑證來源的非同步傳輸接縫；401 刷新責任只屬於具體目錄 owner。</summary>
public interface ILineCredentialExecutor : IChannelCredentialSource
{
    /// <summary>
    /// 在同一有界目錄 operation 內執行至多兩次委派，只在首次 401 刷新。
    /// 不把傳輸逾時當 401，也不在此重試未確定送達的訊息。
    /// </summary>
    Task<LinePushResult> ExecuteAsync(string channelKey,
        Func<LineChannelCredential, CancellationToken, Task<LinePushResult>> send, CancellationToken cancellationToken);
}
