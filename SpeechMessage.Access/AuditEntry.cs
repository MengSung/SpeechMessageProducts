/*
 * 檔案整體說明
 * 目的與定位：F8 共用修改紀錄的不可變輸入值，保留共通層1.3九參數建構式，另以EventId保護永久跨程序事件防重契約；供IAuditLog消費而非直接序列化到日誌。
 * 主要功能與流程：伺服器業務owner交付單一欄位修改事實的持久事件識別、資料表／正式紀錄與欄位、前後值、操作者與租戶、關聯碼及原始時刻；重送沿用事件及原內容，DataverseAuditLog驗證、遮罩後以租戶／事件唯一鍵建立候選。
 * 使用邊界與維護：這是純資料，不執行授權、連線、背景工作或清理；主鍵與關聯碼不是授權證明。Before 與 After 可能含秘密，呼叫端只能保留到目前操作結束，不可寫入診斷、快取或跨請求狀態。
 * 失敗與驗證：長度、正式非空主鍵、時刻及身分關係由寫入服務拒絕不合法候選；RecordId 不是 new_auditentry 的主鍵，也不代表稽核與業務資料共用交易。
 */
namespace SpeechMessage.Access;

/// <summary>
/// 單次修改的不可變候選；字串尚未經安全遮罩，不可將整筆記錄輸出至診斷。
/// 服務只在目前呼叫內使用候選，並以伺服器重新讀取的身分關係決定是否允許寫入。
/// </summary>
/// <param name="Entity">被修改資料的開發者擁有實體識別碼，最多 128 字元。</param>
/// <param name="RecordId">被修改資料的正式非空主鍵，儲存為 D 格式字串而非稽核主鍵。</param>
/// <param name="Field">被修改欄位識別碼，最多 128 字元；敏感欄位只能儲存遮罩。</param>
/// <param name="Before">修改前的可空值，最多 4000 字元；尚未遮罩，禁止進入一般診斷。</param>
/// <param name="After">修改後的可空值，最多 4000 字元；與前值採相同安全策略。</param>
/// <param name="ActorContactId">需重新確認有效且掛在指定租戶下的操作者 Contact 主鍵。</param>
/// <param name="TenantId">需重新確認有效且 new_istenant 為租戶的 Account 主鍵。</param>
/// <param name="CorrelationId">目前操作的有界關聯碼，最多 128 字元；不得使用秘密或個資。</param>
/// <param name="At">呼叫端提供的明確時刻，服務驗證 Dataverse 日期範圍後以 UTC 寫入。</param>
public sealed record AuditEntry(
    string Entity,
    Guid RecordId,
    string Field,
    string? Before,
    string? After,
    Guid ActorContactId,
    Guid TenantId,
    string CorrelationId,
    DateTimeOffset At)
{
    /// <summary>
    /// 伺服器業務 owner 為單一欄位修改事實持久保存的權威事件識別；同一事件重送必須沿用，
    /// 不同事實即使內容相同亦須不同。不得使用瀏覽器提供值、請求重試 token 或臨時 draft key。
    /// 這不是 Dataverse 指派的稽核資料列主鍵；空值由寫入服務在借用資源前拒絕。
    /// </summary>
    public Guid EventId { get; init; }
}
