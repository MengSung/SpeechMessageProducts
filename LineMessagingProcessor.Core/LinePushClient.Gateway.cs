/*
檔案整體說明
目的與定位：供 F5 Gateway 使用的 Core 內部接縫，重用既有 SDK 白名單快照與唯一 HTTP owner，
不新增公開推播出口，也不改變既有單人通知或 F3 傳輸契約。
主要功能與流程：先以固定暫時 sender 凍結 caller SDK 模型；Gateway 之後以權威品牌替換候選。
每批以已凍結 JSON、固定 push/multicast 路徑及同一 retry UUID 委派 SendCoreAsync。
使用邊界與維護：所有請求、回應、十秒 CTS 與 linked registration 仍由 SendCoreAsync 的 using
確定性釋放；本檔不持有任何跨呼叫內容、憑證或狀態，也不重試、不查詢資料庫或管理者。
*/
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using Newtonsoft.Json.Linq;

namespace LineMessagingProcessor.Core;

public sealed partial class LinePushClient
{
    /// <summary>在任何 await 前複製 SDK 模型；完整品牌驗證之後才能公開或送出候選。</summary>
    internal static JArray FreezeGatewayInput(IReadOnlyList<ISendMessage> messages) => FreezeMessages(messages, "F5");

    /// <summary>
    /// 只有 Gateway 的私有已驗證候選可使用；每次 HTTP 仍擁有獨立訊息及十秒取消 owner。
    /// multicast 的尾批即使只有一人也維持同一路徑，確保事件及內容重試一致。
    /// </summary>
    internal async Task<LinePushResult> SendGatewayBatchAsync(LineChannelCredential credential,
        string frozenBody, Guid retryKey, bool multicast, CancellationToken cancellationToken) =>
        (await SendCoreAsync(HttpMethod.Post, multicast ? "message/multicast" : "message/push",
            credential, frozenBody, retryKey, null, cancellationToken).ConfigureAwait(false)).Result;
}
