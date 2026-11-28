/*
檔案整體說明
目的與定位：F5 對外的已接受數量、前置拒絕與對帳摘要，不把平台接受冒稱手機送達。
主要功能與流程：Gateway 只在資料庫完成對應狀態後組成結果；批次集合複製到唯讀快照，
既有已接受 claim 可計入數量但不可重送，不確定或失敗批次不得灌入接受總數。
使用邊界與維護：純值結果只保留正式紀錄 ID、數量及 enum，不含 userId、內容、品牌、
SDK、Exception 或 HTTP 物件；由單次呼叫端擁有，不建立 static 狀態或待辦背景工作。
*/
using System;
using System.Collections.Generic;

namespace LineMessagingProcessor.Core.Gateway;

/// <summary>Gateway 的固定前置拒絕分類，不含任意輸入或個資。</summary>
public enum PushRejectedReason
{
    /// <summary>沒有前置拒絕，不代表所有批次均接受。</summary>
    None,
    /// <summary>至少一位收件人不屬於驗證範圍。</summary>
    ForeignRecipient,
    /// <summary>預設安全政策拒絕非測試租戶。</summary>
    NonTestTenant,
    /// <summary>產品、租戶或名額 Channel 路徑不符。</summary>
    ForeignScope,
    /// <summary>同一業務事件的完整操作雜湊不同。</summary>
    PayloadConflict
}

/// <summary>正式資料庫列的當次批次摘要；Accepted 僅表示 LINE 接受。</summary>
/// <param name="RecordId">正式發送紀錄 ID。</param>
/// <param name="RecipientCount">本批名單人數。</param>
/// <param name="Outcome">已持久化的狀態或既有不確定占用。</param>
public sealed record SendBatchSummary(Guid RecordId, int RecipientCount, SendRecordOutcome Outcome);

/// <summary>與 caller／store 可變集合脫鉤的當次結果，不持有任何操作資源。</summary>
public sealed class LinePushGatewayResult
{
    /// <summary>複製已完成摘要後發布；不接受尚在填入的外部集合。</summary>
    internal LinePushGatewayResult(int acceptedRecipientCount, bool requiresReconciliation,
        PushRejectedReason rejectedReason, IEnumerable<SendBatchSummary> batches)
    {
        AcceptedRecipientCount = acceptedRecipientCount;
        RequiresReconciliation = requiresReconciliation;
        RejectedReason = rejectedReason;
        Batches = new List<SendBatchSummary>(batches).AsReadOnly();
    }

    /// <summary>平台已接受的收件人數，包含同鍵先前已接受批次，不保證實際送達。</summary>
    public int AcceptedRecipientCount { get; }
    /// <summary>有未確定的持久 claim 或傳輸結果，必須對帳，不能自動重送。</summary>
    public bool RequiresReconciliation { get; }
    /// <summary>前置拒絕原因；沒有前置拒絕不代表整案成功。</summary>
    public PushRejectedReason RejectedReason { get; }
    /// <summary>完整只讀批次快照，不攜帶名單或訊息。</summary>
    public IReadOnlyList<SendBatchSummary> Batches { get; }
}
