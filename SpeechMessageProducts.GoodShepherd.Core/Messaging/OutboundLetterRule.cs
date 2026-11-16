/*
 * 檔案整體說明
 * 目的與定位：P5-12 單筆外送 letter 的雙目標通知規則，重用 MemberNotificationService
 * 的收件者／來源教會核對、字典 sender、加密 token 解密及唯一 LINE 傳輸管線。
 * 主要流程：入站立即忽略；外送須有正式活動與 Contact ID、有界非空原文。每次只
 * 建立一個文字訊息，活動 ID 原樣作 Retry Key。Shadow 只記完整意圖，不冒稱送達；
 * 200／同鍵已接受回完成，未綁定／教會不符／永久 4xx 回字典原因，429／5xx 與
 * 傳輸故障明確失敗供系統作業重試。未知例外不攔截，保留原始身分與堆疊。
 * 資源與隔離：本服務只有借用的無狀態通知服務；來源 context 必須由外層從權威
 * 來源關聯核定，不能用收件者本身或路由猜來源教會。金鑰只在同步準備借用，不被
 * 本檔 async 狀態機保存；只等待傳輸 Task 的分類器不保留 SDK、key 或訊息。Host
 * 或平台外殼必須等待 Task 排空再釋放來源；本檔無 Session、快取、Timer、佇列或
 * 背景工作。結果只屬本次操作；狀態回寫由有權限的資料 adapter 負責。
 */
using System;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor.Core;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using ToolUtilityNameSpace.LineRecipients;

namespace SpeechMessageProducts.GoodShepherd.Core.Messaging;

/// <summary>單次外送的業務分類；影子意圖與平台接受刻意分開，不推導成送達回條。</summary>
public enum OutboundLetterStatus
{
    /// <summary>入站資料不執行外送，也不應修改活動完成狀態。</summary>
    Ignored,
    /// <summary>只完成影子意圖；沒有平台推播，不得標記正式活動為已送出。</summary>
    IntentRecorded,
    /// <summary>平台接受或已接受相同重試鍵；不代表收件者已讀。</summary>
    Completed,
    /// <summary>不需重試的業務或永久傳輸拒絕，使用固定 FailureKey 供字典格式化。</summary>
    Failed
}

/// <summary>不持有個資、來源、SDK、憑證或傳輸物件的單次純值結果。</summary>
public sealed class OutboundLetterResult
{
    /// <summary>只由規則建立一致分類，失敗以固定字典鍵而非供應商錯誤本文表示。</summary>
    internal OutboundLetterResult(OutboundLetterStatus status, string? failureKey = null)
    {
        Status = status;
        FailureKey = failureKey;
    }

    /// <summary>實際執行分類；資料 adapter 不能把影子或忽略當成平台成功。</summary>
    public OutboundLetterStatus Status { get; }
    /// <summary>僅失敗時有值的穩定資源鍵；不含活動本文、姓名、token 或 LINE ID。</summary>
    public string? FailureKey { get; }
}

/// <summary>沒有操作狀態的單人外送規則；正式授權與資料欄位解析由呼叫邊界負責。</summary>
public sealed class OutboundLetterRule
{
    private readonly MemberNotificationService notifications;

    /// <summary>借用既有通知組合，不接管其策略、HttpClient 或任何來源連線。</summary>
    public OutboundLetterRule(MemberNotificationService notifications) =>
        this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

    /// <summary>
    /// 已授權 Host 的單人外送入口；不代表目前 D365 外殼核准 Live（P5-PROP-006）。
    /// 準備階段同步借用 source／key，傳輸以原活動 ID 去重，沒有內建第二套重試。
    /// </summary>
    /// <param name="outbound">正式資料庫 directioncode；false 不讀 source。</param>
    /// <param name="source">外層擁有且完整驗證組織範圍的收件來源。</param>
    /// <param name="context">從來源活動權威關聯核定的教會／組織／產品文字範圍。</param>
    /// <param name="activityId">平台提供的正式 activityid，直接作重試鍵。</param>
    /// <param name="contactId">唯一正式 Contact ID；多收件者須在資料邊界拒絕。</param>
    /// <param name="content">自由文字原文，不修剪、不翻譯、不截斷。</param>
    /// <param name="tokenKey">只在同步準備期間借用的解密金鑰，由 caller 清理。</param>
    /// <param name="ct">caller 的正常取消，傳輸內部逾時仍是失敗。</param>
    public Task<OutboundLetterResult> SendAsync(bool outbound, ILineRecipientSource source, TextContext context,
        Guid activityId, Guid contactId, string content, byte[] tokenKey, CancellationToken ct)
    {
        if (!outbound) return Task.FromResult(new OutboundLetterResult(OutboundLetterStatus.Ignored));
        Validate(activityId, contactId, content, ct);
        return ClassifyAsync(notifications.NotifyComposedAsync(source, context, contactId, activityId,
            "Messaging.OutboundLetter", _ => new ISendMessage[] { new TextMessage(string.Empty) { Text = content } },
            tokenKey, ct));
    }

    /// <summary>
    /// Shadow 入口與 Host 使用相同收件者檢查及內容，僅把有界摘要交給當次 sink。
    /// 不解密、不觸網，回 IntentRecorded 而非 Completed；caller 必須等待再 Dispose。
    /// </summary>
    /// <param name="outbound">正式外送方向，入站直接忽略。</param>
    /// <param name="source">單次 token-free SDK adapter，生命週期由外層擁有。</param>
    /// <param name="context">權威來源教會與組織範圍，不由收件者反推。</param>
    /// <param name="activityId">正式活動 ID，同次重送不換鍵。</param>
    /// <param name="contactId">唯一 Contact 正式 ID。</param>
    /// <param name="content">保留空白與換行的原文，最多五千字。</param>
    /// <param name="sink">當次有界影子 owner，不保存原文。</param>
    /// <param name="ct">外層 caller 取消，不能以內部逾時冒充正常取消。</param>
    public Task<OutboundLetterResult> RecordAsync(bool outbound, ILineRecipientSource source, TextContext context,
        Guid activityId, Guid contactId, string content, IShadowPushSink sink, CancellationToken ct)
    {
        if (!outbound) return Task.FromResult(new OutboundLetterResult(OutboundLetterStatus.Ignored));
        Validate(activityId, contactId, content, ct);
        return ClassifyAsync(notifications.NotifyComposedAsync(source, context, contactId, activityId,
            "Messaging.OutboundLetter", _ => new ISendMessage[] { new TextMessage(string.Empty) { Text = content } },
            sink, ct));
    }

    /// <summary>同步驗證正式事件及有界原文，錯誤不回顯內容、不建立部分通知。</summary>
    private static void Validate(Guid activityId, Guid contactId, string content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (activityId == Guid.Empty || contactId == Guid.Empty)
            throw new ArgumentException("A formal activity and contact identity are required.");
        if (string.IsNullOrEmpty(content) || content.Length > 5000)
            throw new ArgumentException("Letter text is outside its bounded budget.");
    }

    /// <summary>
    /// 只等待純傳輸結果，不捕捉 SDK、key 或原文。HTTP 回應已完成時不以遲到取消改寫
    /// 平台分類；真正傳輸故障保留原 inner，不保存或印出其可能含秘密的 Message。
    /// </summary>
    private static async Task<OutboundLetterResult> ClassifyAsync(Task<LinePushResult> pending)
    {
        var result = await pending.ConfigureAwait(false);
        if (result.Success) return new OutboundLetterResult(OutboundLetterStatus.Completed);
        if (result.Reason == "ShadowRecorded") return new OutboundLetterResult(OutboundLetterStatus.IntentRecorded);
        if (result.Reason is "ChurchMismatch" or "NotBound")
            return new OutboundLetterResult(OutboundLetterStatus.Failed, "Messaging.Failure." + result.Reason);
        if (result.Outcome == LinePushOutcome.Retryable)
            throw new InvalidOperationException("Outbound letter delivery requires system-job retry.", result.TransportException);
        if (result.Outcome == LinePushOutcome.Permanent && result.StatusCode is { } code &&
            (int)code is >= 400 and <= 499)
            return new OutboundLetterResult(OutboundLetterStatus.Failed, "Messaging.Failure.Blocked");
        throw new InvalidOperationException("Unexpected outbound letter delivery classification.");
    }
}
