/*
檔案整體說明
目的與定位：F5 的持久批次占用與結果轉移契約，避免多程序或重啟後重送同一事件。
主要功能與流程：以不含 payload 的固定事件／批次鍵原子 Create，既有鍵必須核對完整操作、
內容及收件人雜湊；Claimed 代表不確定而非可接手。完成以 RowVersion CAS，不能 Upsert 覆蓋。
使用邊界與維護：介面只傳不可變純值，不保存收件人、訊息、憑證或 SDK。紀錄由固定產品的
資料庫 owner 持久保留到核定保存政策；Core 不建立快取或背景對帳。正式 ID 與 RowVersion
只能來自資料庫，不能用 retry UUID 或臨時 ID 代替；失敗仍保留 claim 以阻止不確定重送。
*/
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LineMessagingProcessor.Core.Gateway;

/// <summary>已持久化批次的狀態，數值與 F5 schema 固定對應。</summary>
public enum SendRecordOutcome
{
    /// <summary>占用已寫入但尚未確定 HTTP 結果；其他操作不得自動接手。</summary>
    Claimed = 100000000,
    /// <summary>HTTP 2xx 或同鍵 409，僅表示平台接受。</summary>
    Accepted = 100000001,
    /// <summary>平台明確拒絕，不重新使用此事件重送。</summary>
    Failed = 100000002,
    /// <summary>傳輸或最終記錄不確定，需對帳而非換鍵重送。</summary>
    Uncertain = 100000003,
    /// <summary>前置測試租戶或收件人政策拒絕，沒有送出 HTTP。</summary>
    Rejected = 100000004
}

/// <summary>資料庫 claim 的分類，不可把既有 Claimed 當成可重入鎖。</summary>
public enum SendClaimDisposition
{
    /// <summary>本次原子 Create 成功，唯一允許開始 HTTP 的分類。</summary>
    Acquired,
    /// <summary>相同操作與內容已被平台接受，只累加摘要不再傳輸。</summary>
    AlreadyAccepted,
    /// <summary>相同操作已有確定失敗或拒絕終態，不再傳輸。</summary>
    AlreadyFinal,
    /// <summary>其他操作仍在占用或已記不確定，禁止自動重送。</summary>
    Uncertain,
    /// <summary>同一業務事件的不可變內容不同，整案拒絕。</summary>
    PayloadConflict
}

/// <summary>寫入前凍結的批次事實；hash 只供一致性，不能代替伺服器授權。</summary>
/// <param name="Product">固定產品。</param>
/// <param name="ChannelKey">已驗證 Channel。</param>
/// <param name="TenantId">正式租戶 ID。</param>
/// <param name="CorrelationId">伺服器業務事件 ID，最多 128 ASCII 字元。</param>
/// <param name="BatchIndex">0 起算；-1 專用於前置拒絕紀錄。</param>
/// <param name="BatchCount">整案批次數。</param>
/// <param name="BatchKey">產品／Channel／租戶／事件／批次的 SHA256，不含 payload。</param>
/// <param name="OperationHash">整案訊息、完整有序名單、分類及權威品牌的雜湊。</param>
/// <param name="PayloadHash">當批完整 HTTP JSON 雜湊。</param>
/// <param name="RecipientHash">當批有序名單雜湊，不保存 userId。</param>
/// <param name="RetryKey">由 BatchKey 穩定導出的 UUID，重試不得更換。</param>
/// <param name="Category">固定訊息分類。</param>
/// <param name="RecipientCount">當批人數，不表示手機已收到。</param>
/// <param name="MessageCount">當批則數。</param>
/// <param name="SenderNote">固定 sender 省略原因或空值，不含原始名稱。</param>
public sealed record SendBatchIdentity(string Product, string ChannelKey, Guid TenantId, string CorrelationId,
    int BatchIndex, int BatchCount, string BatchKey, string OperationHash, string PayloadHash, string RecipientHash,
    Guid RetryKey, MessageCategory Category, int RecipientCount, int MessageCount, string? SenderNote);

/// <summary>已驗證的資料庫 claim，RowVersion 屬於該正式列，不可由呼叫端補造。</summary>
/// <param name="Identity">完整不可變事實，Gateway 必須與請求相符才可傳輸。</param>
/// <param name="RecordId">資料庫正式主鍵。</param>
/// <param name="RowVersion">Create 後讀回的資料庫版本。</param>
/// <param name="Disposition">只有 Acquired 可開始 HTTP。</param>
public sealed record SendBatchClaim(SendBatchIdentity Identity, Guid RecordId, string RowVersion, SendClaimDisposition Disposition);

/// <summary>只包含安全固定代碼的終態，不攜帶 HTTP 本文、例外或憑證。</summary>
/// <param name="Outcome">不可回到 Claimed 的終態。</param>
/// <param name="ErrorCode">固定拒絕原因或 HTTP 數字代碼，最多 64。</param>
/// <param name="Attempts">HTTP 實際嘗試數，拒絕為 0；兩次有界傳輸各可刷新一次 401，最多 4。</param>
public sealed record SendBatchCompletion(SendRecordOutcome Outcome, string? ErrorCode, int Attempts);

/// <summary>固定產品的資料庫原子 owner；不得用本機鎖或 Upsert 冒充唯一性。</summary>
public interface ISendRecordStore
{
    /// <summary>Create 新 claim 或驗回既有事實；任何故障前不允許 HTTP。</summary>
    Task<SendBatchClaim> ClaimBatchAsync(SendBatchIdentity identity, CancellationToken cancellationToken);
    /// <summary>以 claim 的 RowVersion 完成終態；相同終態可冪等，不同終態不可覆蓋。</summary>
    Task CompleteBatchAsync(SendBatchClaim claim, SendBatchCompletion completion, CancellationToken cancellationToken);
}
