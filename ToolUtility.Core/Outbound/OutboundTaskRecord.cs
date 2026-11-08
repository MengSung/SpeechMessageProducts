/*
 * 檔案整體說明
 *
 * 目的與定位：提供 P2-07 輪詢、認領與完成回寫共用的雙目標純值契約。正式待辦 ID
 * 與 RowVersion 是資料庫身分／寫入柵欄，不能用來源事件 ID 或顯示名稱替代。
 * 主要功能與流程：候選快照只攜帶單列不可變值；儲存邊界先驗證全批身分，再回傳
 * 完整清單。認領取得新版本，完成或失敗回寫只能使用該版本，防止舊 owner 覆寫。
 * 使用邊界與維護：字串均有界且不含 credential；payload 是尚待 net10 handler
 * 邊界重新驗證的 JSON，不是已授權本文。SDK／連線不進快照；store 只借用已授權
 * 固定組織 SDK，呼叫端擁有逾時、排空與清理。這裡不建立 Task、timer、快取或連線。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace ToolUtilityNameSpace.Outbound;

/// <summary>對應 A.4 自訂狀態欄；不使用實體平台 statuscode 代替。</summary>
public enum OutboundTaskStatus
{
    /// <summary>等待到期後認領，不能由另一個工作實例直接執行。</summary>
    Pending = 100000000,
    /// <summary>由一次版本化認領擁有，十分鐘後才能交由其他 owner 重認領。</summary>
    Processing = 100000001,
    /// <summary>已完成且不再執行；保留清理由 D365 工作負責。</summary>
    Completed = 100000002,
    /// <summary>終止失敗且不再自動重試，診斷由 Host 共用入口負責。</summary>
    Failed = 100000003
}

/// <summary>單列有界、不可變的資料庫快照；不持有 SDK Entity 或其他可變／可釋放資源。</summary>
public sealed class OutboundTaskRecord
{
    /// <summary>驗證正式身分與版本、閉合選項及有界資料，禁止以缺值補造可發布的正式記錄。</summary>
    /// <param name="id">資料庫主鍵，不是來源事件 ID。</param>
    /// <param name="rowVersion">資料庫正整數版本，供每次寫入 IfRowVersionMatches。</param>
    /// <param name="churchId">從已授權組織讀回的 account ID。</param>
    /// <param name="contactId">選填 contact ID；處理器仍須驗證與教會關係。</param>
    /// <param name="type">三種核定工作類型之一。</param>
    /// <param name="payloadJson">最多四千字元的原始 JSON，執行前還要通過型別白名單。</param>
    /// <param name="idempotencyKey">包含教會、類型及來源 ID 的穩定鍵。</param>
    /// <param name="status">A.4 自訂狀態。</param>
    /// <param name="attempts">已排程的重試數，介於零至五。</param>
    /// <param name="nextAttemptOn">到期 UTC 時間；空值代表可立即處理。</param>
    /// <param name="claimedOn">最近認領 UTC 時間，Processing 不可缺值。</param>
    public OutboundTaskRecord(Guid id, string rowVersion, Guid churchId, Guid? contactId,
        OutboundTaskType type, string payloadJson, string idempotencyKey, OutboundTaskStatus status,
        int attempts, DateTime? nextAttemptOn, DateTime? claimedOn)
    {
        if (id == Guid.Empty || churchId == Guid.Empty || contactId == Guid.Empty)
            throw new ArgumentException("Outbound database identity is invalid.");
        if (string.IsNullOrEmpty(rowVersion) || rowVersion.Length > 20
            || !ulong.TryParse(rowVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version == 0)
            throw new ArgumentException("Outbound row version is invalid.", nameof(rowVersion));
        if (type is not (OutboundTaskType.RichMenuGenerate or OutboundTaskType.QrImageGenerate or OutboundTaskType.AllotQuery)
            || status is not (OutboundTaskStatus.Pending or OutboundTaskStatus.Processing or OutboundTaskStatus.Completed or OutboundTaskStatus.Failed))
            throw new ArgumentException("Outbound option is invalid.");
        var prefix = churchId.ToString("N") + "-" + ((int)type).ToString(CultureInfo.InvariantCulture) + "-";
        if (string.IsNullOrEmpty(idempotencyKey) || !idempotencyKey.StartsWith(prefix, StringComparison.Ordinal)
            || idempotencyKey.Length != prefix.Length + 32
            || !Guid.TryParseExact(idempotencyKey.Substring(prefix.Length), "N", out var source) || source == Guid.Empty)
            throw new ArgumentException("Outbound event identity is invalid.", nameof(idempotencyKey));
        if (string.IsNullOrEmpty(payloadJson) || payloadJson.Length > 4000)
            throw new ArgumentException("Outbound payload is not bounded.", nameof(payloadJson));
        if (attempts < 0 || attempts > 5) throw new ArgumentOutOfRangeException(nameof(attempts));
        if (nextAttemptOn.HasValue && nextAttemptOn.Value.Kind != DateTimeKind.Utc
            || claimedOn.HasValue && claimedOn.Value.Kind != DateTimeKind.Utc
            || status == OutboundTaskStatus.Processing && !claimedOn.HasValue)
            throw new ArgumentException("Outbound timestamps must be UTC and processing requires a claim time.");
        Id = id; RowVersion = rowVersion; ChurchId = churchId; ContactId = contactId; Type = type;
        PayloadJson = payloadJson; IdempotencyKey = idempotencyKey; Status = status;
        Attempts = attempts; NextAttemptOn = nextAttemptOn; ClaimedOn = claimedOn;
    }

    /// <summary>正式資料庫主鍵；同一批不能重複。</summary>
    public Guid Id { get; }
    /// <summary>這份快照的資料庫版本；舊版本不能完成新 owner 的工作。</summary>
    public string RowVersion { get; }
    /// <summary>單列所屬教會；不附帶其他教會設定或認證。</summary>
    public Guid ChurchId { get; }
    /// <summary>選填會友正式 ID，非授權憑據。</summary>
    public Guid? ContactId { get; }
    /// <summary>決定處理器的閉合工作選項。</summary>
    public OutboundTaskType Type { get; }
    /// <summary>原始有界資料；不得記錄或未驗證便當作自由本文執行。</summary>
    public string PayloadJson { get; }
    /// <summary>跨程序唯一事件鍵；完成重試都不可變更。</summary>
    public string IdempotencyKey { get; }
    /// <summary>讀取當時的自訂狀態，不是可變的執行中共享物件。</summary>
    public OutboundTaskStatus Status { get; }
    /// <summary>已排程重試數；第六次失敗不再排程。</summary>
    public int Attempts { get; }
    /// <summary>下一次到期 UTC 時間；只在 Pending 狀態使用。</summary>
    public DateTime? NextAttemptOn { get; }
    /// <summary>認領 UTC 時間，供十分鐘卡住回收判定。</summary>
    public DateTime? ClaimedOn { get; }
}

/// <summary>固定已授權組織的同步儲存邊界；不得把取消視為可以提前釋放仍入場的 SDK。</summary>
public interface IOutboundTaskStore
{
    /// <summary>一次取最多五十筆到期或卡住的完整快照；重複正式 ID 必須拒絕整批。</summary>
    /// <param name="utcNow">已驗證的 UTC 時點，不讀主機當地時間。</param>
    /// <param name="maximum">一至五十筆，不允許無界讀取。</param>
    /// <param name="cancellationToken">入場前與唯讀 I/O 後檢查；呼叫端須排空同步 I/O。</param>
    /// <returns>完整驗證後的唯讀快照，不攜帶 SDK Entity。</returns>
    IReadOnlyList<OutboundTaskRecord> ReadCandidates(DateTime utcNow, int maximum, CancellationToken cancellationToken);

    /// <summary>以候選版本原子認領並讀回新版本；只有版本競爭失敗回空值，其他錯誤保留。</summary>
    /// <param name="candidate">本組織已授權讀取的完整候選，不是 caller 送來的授權範圍。</param>
    /// <param name="utcNow">認領的 UTC 時點，用於到期與十分鐘回收界線。</param>
    /// <param name="cancellationToken">入場前檢查；已成功交易的結果不因後到取消而被隱藏。</param>
    /// <returns>含新版本的本次認領，或不合時機／版本衝突的空值。</returns>
    OutboundTaskRecord? TryClaim(OutboundTaskRecord candidate, DateTime utcNow, CancellationToken cancellationToken);

    /// <summary>以認領版本完成／排重試／終止，競爭失敗回 false，不能覆寫下一個 owner。</summary>
    /// <param name="claim">本組織本次 owner 已取得的新版本。</param>
    /// <param name="status">Pending、Completed 或 Failed，不准無條件重設 Processing。</param>
    /// <param name="attempts">零至五的重試計數；重排只可比本次增加一。</param>
    /// <param name="utcNow">本次完成／重排時點，必須為 UTC。</param>
    /// <param name="nextAttemptOn">Pending 必填未來 UTC；終止狀態必須空白。</param>
    /// <param name="errorCode">至多六十四字元的固定 ASCII 分類，不接受例外原文或 payload。</param>
    /// <param name="cancellationToken">入場前檢查，已確認成功的寫入不隱藏。</param>
    /// <returns>版本相符成功回 true，已被其他 owner 取代回 false。</returns>
    bool TrySettle(OutboundTaskRecord claim, OutboundTaskStatus status, int attempts,
        DateTime utcNow, DateTime? nextAttemptOn, string? errorCode, CancellationToken cancellationToken);
}
