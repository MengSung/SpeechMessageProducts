/*
 * 檔案整體說明
 *
 * 目的與定位：P2-06 net10 舊 arbitrary SDK 傳輸接點，補齊既有 processor 的多播與媒體
 * 內容消費能力；正式 contact／church/channel 授權推播仍由 P2-04 Core 負責，不能以
 * 此相容入口捏造授權身分或從 SDK private 欄位取得 token。
 * 主要功能與流程：多播先建立收件集合與訊息集合的區域副本，再交同一 SDK serializer；
 * 內容下載只借用 SDK，取得 stream 後以 using 包住同步消費者，正常返回或消費失敗皆關閉
 * stream 及其 response。沒有第二套 HTTP、重試、背景佇列、快取、timer 或全域 credential。
 * 使用邊界與維護：SDK/client 由原頻道 owner 擁有且設定有限傳輸期限；此檔不 Dispose
 * 借來的 client、不變更 DefaultRequestHeaders。消費者不得保存 stream 或開未等待工作，
 * 其同步處理須有界；沒有 SDK caller-token API 就不宣稱可取消已開始的下載。資料最多保留
 * 到單次 await／消費完成，假 handler 黃金與故障 Dispose 測試保護生命週期及 wire 不變。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Line.Messaging;

namespace LineMessagingProcessor;

public partial class LineMessagingProcessorClass
{
    /// <summary>
    /// 保留 legacy multicast 的單一 HTTP，不改成逐人 push，也不去重、補造 retry key 或
    /// sender。任意群組／使用者 ID 的授權必須由產品先完成；格式不是伺服器授權證明。
    /// </summary>
    /// <param name="userIds">當次收件序列；複製後才交 SDK，不存到實例。</param>
    /// <param name="messages">當次 SDK 訊息；只複製集合，SDK 在首次 await 前序列化。</param>
    /// <returns>單次 SDK 作業；原例外及既有 Timeout 原樣回到最終產品 owner。</returns>
    public Task MultiCastMessagesAsync(IList<string> userIds, IList<ISendMessage> messages)
    {
        if (userIds == null) throw new ArgumentNullException(nameof(userIds));
        if (messages == null) throw new ArgumentNullException(nameof(messages));
        return _lineMessagingClient.MultiCastMessageAsync(userIds.ToArray(), messages.ToArray());
    }

    /// <summary>
    /// 在唯一 using owner 內交付下載串流，不把尚未關閉的 response 傳回產品或跨 request 保存。
    /// 同步消費者失敗時由 finally 關閉 SDK stream，再保留原始例外；本層不發管理者通知。
    /// </summary>
    /// <param name="messageId">目前已驗證事件的 message ID；不作紀錄或快取。</param>
    /// <param name="consume">有界同步消費者，只能在呼叫期間借用 stream，不得留存或 Dispose 它。</param>
    /// <returns>下載與消費皆完成才結束的作業；最終 owner 負責真正失敗的共用上報。</returns>
    public async Task UseContentAsync(string messageId, Action<Stream> consume)
    {
        if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentException("A message identifier is required.", nameof(messageId));
        if (consume == null) throw new ArgumentNullException(nameof(consume));
        using var content = await _lineMessagingClient.GetContentStreamAsync(messageId).ConfigureAwait(false);
        consume(content);
    }
}
