/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 產品內部 LINE 通知政策，承接正式仍使用的文字、
 * SDK 多則、可靠文字及多播四種入口；共用 legacy workflow 是唯一傳輸接點。
 * 主要功能與流程：best-effort 失敗交既有共用診斷後繼續；可靠文字驗證與
 * RetryKey 原樣交 SendOrThrowAsync；多播先以原 ToolUtility 寫統計再送一次，
 * 空收件集合不產生副作用，故障先上報同一 Exception 再傳遞。
 * 建構模式不支援的方法在進入傳輸 try 前拒絕，不能被 best-effort 吞掉。
 * 使用邊界與維護：只借用同頻道 workflow／CRM 工具，不讀 token、不取得 SDK、
 * HTTP 或 CRM 的所有權，不建立 static 可變狀態、快取、佇列、計時器或背景工作。
 * 收件人與本文只存在一次操作；授權、逾時、是否等待及 client 釋放沿用原入口，
 * 本檔不新增取消或重試，也不宣稱改善原未等待流程。來源代號與內容維持原樣，
 * 由 P6-03 離線黃金、故障與雙頻道交錯測試驗證，不代表真實 LINE 驗收。
 */
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Diagnostics;

namespace ChurchReport.Services;

/// <summary>限定同一頻道的產品失敗政策；不保留操作資料，不接管借入資源。</summary>
internal sealed class ChurchReportLineNotifier
{
    private readonly ILineNotificationWorkflow? notifications;
    private readonly LineLegacySendWorkflow? multicast;
    private readonly ToolUtilityClass? tool;

    /// <summary>借用既有通知流程；呼叫端維持其頻道隔離與資源生命週期。</summary>
    /// <param name="notifications">原頻道的共用通知流程，不能跨頻道重新綁定。</param>
    internal ChurchReportLineNotifier(ILineNotificationWorkflow notifications)
    {
        this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    /// <summary>多播只借用原 legacy 與統計工具；單播不使用此建立路徑。</summary>
    /// <param name="multicast">原頻道的 legacy 傳輸流程。</param>
    /// <param name="tool">原 request／Factory scope 的 CRM 統計工具。</param>
    internal ChurchReportLineNotifier(LineLegacySendWorkflow multicast, ToolUtilityClass tool)
    {
        this.multicast = multicast ?? throw new ArgumentNullException(nameof(multicast));
        this.tool = tool ?? throw new ArgumentNullException(nameof(tool));
    }

    /// <summary>沿舊預設方式建立輕量 adapter；SDK 仍由原呼叫端擁有並於排空後釋放。</summary>
    /// <param name="client">已由產品解析頻道的借用 SDK。</param>
    /// <returns>不持有額外資源 owner 的單播通知器。</returns>
    internal static ChurchReportLineNotifier ForClient(LineMessagingClient client) =>
        new(new LineNotificationWorkflow(new LineMessagingProcessorClass(
            client ?? throw new ArgumentNullException(nameof(client)))));

    /// <summary>原文字 best-effort；本文與來源不改，結果及拋出例外都由同一診斷入口處理。</summary>
    /// <param name="userId">外層已授權的本次收件 ID。</param>
    /// <param name="text">產品已組好的原樣本文。</param>
    /// <returns>已等待傳輸及診斷完成，不向外拋送訊錯誤的作業。</returns>
    internal Task SendTextBestEffortAsync(string userId, string text)
    {
        var workflow = RequireNotifications();
        return SendBestEffortAsync(workflow, userId, LineNotificationContent.TextMessage(text), "ChurchReport.PushUtility");
    }

    /// <summary>原 SDK 多則 best-effort；集合只在當次呼叫借用，順序與訊息型別不變。</summary>
    /// <param name="userId">外層已授權的本次收件 ID。</param>
    /// <param name="messages">呼叫期間不可並行修改的 SDK 訊息清單。</param>
    /// <returns>傳輸已排空且已處理錯誤的作業。</returns>
    internal async Task SendMessagesBestEffortAsync(string userId, IReadOnlyList<ISendMessage> messages)
    {
        var workflow = RequireNotifications();
        try
        {
            // 舊殼的 catch 同時包住內容驗證；空集合或 null 的同步拒絕也須維持 best-effort。
            await SendBestEffortAsync(workflow, userId, LineNotificationContent.SdkMessagesList(messages),
                "ChurchReport.PushUtility.BestEffortSdkMessages");
        }
        catch (Exception exception)
        {
            ExceptionReporting.Report(exception);
        }
    }

    /// <summary>保持舊必要文字的拒絕訊息及參數名稱，RetryKey 不補造、不轉換。</summary>
    /// <param name="userId">本次非空白收件 ID。</param>
    /// <param name="text">本次非空白付款／業務本文。</param>
    /// <param name="retryKey">呼叫端原有來源鍵，可為 null。</param>
    /// <returns>必要通知作業；失敗原樣交最終產品邊界處理。</returns>
    internal async Task SendReliableTextAsync(string userId, string text, string? retryKey)
    {
        var workflow = RequireNotifications();
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("LINE user id is required.", "UserId");
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("LINE message is required.", "Message");
        await workflow.SendOrThrowAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User(userId),
            Content = LineNotificationContent.TextMessage(text),
            RetryKey = retryKey,
            Metadata = new Dictionary<string, string> { ["source"] = "ChurchReport.PushUtility.ReliableText" }
        });
    }

    /// <summary>先寫原統計再單次多播；不去重收件人，空清單零副作用，原例外先上報再傳遞。</summary>
    /// <param name="userIds">產品已授權的當次收件序列，保留原順序與重複值。</param>
    /// <param name="text">原樣統計及多播本文。</param>
    /// <returns>單次 legacy 多播完成的作業；外層仍依原方式等待。</returns>
    internal async Task MulticastTextAsync(IList<string> userIds, string text)
    {
        // 模式檢查必須先於空清單捷徑及傳輸 catch；程式誤用不是可吞掉的送訊故障。
        var statistics = tool ?? throw new InvalidOperationException("This notifier was not constructed for multicast.");
        var workflow = multicast ?? throw new InvalidOperationException("This notifier was not constructed for multicast.");
        try
        {
            if (userIds.Count > 0)
            {
                statistics.CreatePushLineMessage(userIds, "Line推播統計:文字", text);
                await workflow.MultiCastAsync(userIds, new List<ISendMessage> { new TextMessage(text) });
            }
        }
        catch (Exception exception)
        {
            ExceptionReporting.Report(exception);
            throw;
        }
    }

    /// <summary>只取已由單播建構式驗證的借用流程；誤用在外層 try 之前拒絕，不產生傳輸或統計。</summary>
    /// <returns>同頻道既有流程；不轉移其所有權。</returns>
    private ILineNotificationWorkflow RequireNotifications() => notifications
        ?? throw new InvalidOperationException("This notifier was not constructed for unicast.");

    /// <summary>共用當次請求組裝與 best-effort 失敗政策；不存結果或變更既有診斷預設來源。</summary>
    private static async Task SendBestEffortAsync(ILineNotificationWorkflow workflow, string userId, LineNotificationContent content, string source)
    {
        try
        {
            var result = await workflow.SendAsync(new LineNotificationRequest
            {
                Recipient = LineNotificationRecipient.User(userId),
                Content = content,
                Metadata = new Dictionary<string, string> { ["source"] = source }
            });
            if (result.Exception is { } exception) ExceptionReporting.Report(exception);
        }
        catch (Exception exception)
        {
            ExceptionReporting.Report(exception);
        }
    }
}
