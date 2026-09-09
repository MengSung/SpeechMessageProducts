// ============================================================================
// 檔案路徑：SpeechMessageProducts.ChurchReport/Models/Publication/RowPublicationGuard.cs
// 檔案責任：在集合交付給 Razor、JSON serializer、DataSourceLoader、Grid、報表或匯出器前，
//           依呼叫端指定的資料庫唯一 ID 驗證每一列，並建立只屬於目前操作的有界清單容器。
// 安全邊界：本型別沒有 static mutable state，也不保存 Session、HttpContext、identity、credential、
//           CRM client、連線、來源 enumerable 或驗證結果；方法結束後，HashSet 與 List 均可回收，
//           因而不會跨 request、跨使用者、跨租戶保留資料，也不會形成無界 cache 或 lock registry。
// 身份原則：只比較伺服器提供的權威唯一 ID。姓名、電話、顯示文字與內容相似度絕對不參與判斷；
//           不同 ID 即使所有內容完全相同也會保留，同一 ID 重複則 fail closed，不會取第一筆。
// 編碼要求：本檔案必須以 UTF-8 without BOM、CRLF、final CRLF 儲存。
// ============================================================================
using System;
using System.Collections.Generic;

namespace ChurchReport.Models;

/// <summary>
/// 提供不保存呼叫端狀態的資料列發布前驗證，讓各產品可在實際 consumer boundary
/// 以資料庫唯一 ID 阻止重複列、缺少身份與無界集合進入 UI。
/// </summary>
internal static class RowPublicationGuard
{
    /// <summary>
    /// 目前 ChurchReport 完整週報 consumer 的預設安全容量；產品可在未來 consumer
    /// contract 以明確設定覆寫，但不得用無界集合取代上限。
    /// </summary>
    internal const int DefaultMaximumRowCount = 10000;

    /// <summary>
    /// 單次列舉來源資料並驗證 stable ID，不複製資料列或保存來源參考。
    /// </summary>
    /// <typeparam name="TRow">目前 consumer 所使用的資料列型別。</typeparam>
    /// <param name="rows">已完成授權與 scope 檢查、只供目前操作列舉的資料。</param>
    /// <param name="stableIdSelector">只讀取資料庫／權威來源唯一 ID 的 selector。</param>
    /// <param name="consumerName">不含個資的固定 consumer 名稱。</param>
    /// <param name="maximumRowCount">允許一次發布的最大列數。</param>
    /// <param name="identityName">權威 ID 欄位名稱。</param>
    /// <param name="comparer">權威資料來源定義的 ID 比較規則。</param>
    /// <exception cref="InvalidOperationException">列、ID、唯一性或容量不符合契約時擲出。</exception>
    internal static void ValidateRows<TRow>(
        IEnumerable<TRow> rows,
        Func<TRow, string> stableIdSelector,
        string consumerName,
        int maximumRowCount,
        string identityName = "PresentRecordId",
        IEqualityComparer<string> comparer = null)
        where TRow : class
    {
        ValidateCore(
            rows,
            stableIdSelector,
            consumerName,
            maximumRowCount,
            identityName,
            comparer,
            validatedRows: null);
    }

    /// <summary>
    /// 單次列舉來源資料，驗證 stable ID 後回傳由目前操作擁有的新清單容器。
    /// </summary>
    /// <typeparam name="TRow">目前 consumer 所使用的資料列型別。</typeparam>
    /// <param name="rows">已完成授權、scope 檢查與必要深複製的候選資料；不得是活的 Session 集合。</param>
    /// <param name="stableIdSelector">只讀取資料庫／權威來源唯一 ID 的 selector；不得讀取姓名或顯示內容。</param>
    /// <param name="consumerName">不含個資的固定 consumer 名稱，只用於可診斷錯誤。</param>
    /// <param name="maximumRowCount">此 consumer 可一次發布的最大列數；必須大於零。</param>
    /// <param name="identityName">權威 ID 欄位名稱，例如 PresentRecordId、ContactId 或 OrderLineId。</param>
    /// <param name="comparer">權威系統定義的 ID 比較規則；未指定時採不改寫內容的 Ordinal 比較。</param>
    /// <returns>順序與所有合法列均保持不變、但清單容器由目前呼叫擁有的唯讀介面。</returns>
    /// <exception cref="ArgumentNullException">來源、selector 或 consumer 名稱為 null 時擲出。</exception>
    /// <exception cref="ArgumentOutOfRangeException">容量上限小於一時擲出。</exception>
    /// <exception cref="InvalidOperationException">列為 null、ID 空白、ID 重複或超過容量時擲出。</exception>
    internal static IReadOnlyList<TRow> ValidateDetachedRows<TRow>(
        IEnumerable<TRow> rows,
        Func<TRow, string> stableIdSelector,
        string consumerName,
        int maximumRowCount,
        string identityName = "PresentRecordId",
        IEqualityComparer<string> comparer = null)
        where TRow : class
    {
        var validatedRows = new List<TRow>();
        ValidateCore(
            rows,
            stableIdSelector,
            consumerName,
            maximumRowCount,
            identityName,
            comparer,
            validatedRows);
        return validatedRows;
    }

    /// <summary>
    /// 集中實作列舉、容量與 stable-ID 檢查；可選的結果容器只由需要 detached list 的呼叫者使用。
    /// </summary>
    /// <remarks>
    /// API 快照已由上游完成深複製時會傳入 null，避免再配置第二份 row-reference List；需要把
    /// lazy enumerable 固化成操作區域清單時才傳入容器。兩條路徑共用相同驗證邏輯，且方法結束
    /// 後不保留 selector、來源或 ID 集合，避免跨 request retention。
    /// </remarks>
    private static void ValidateCore<TRow>(
        IEnumerable<TRow> rows,
        Func<TRow, string> stableIdSelector,
        string consumerName,
        int maximumRowCount,
        string identityName,
        IEqualityComparer<string> comparer,
        List<TRow> validatedRows)
        where TRow : class
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(stableIdSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityName);

        if (maximumRowCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRowCount),
                maximumRowCount,
                "資料發布容量上限必須大於零。");
        }

        // HashSet 與可選結果 List 都只屬於這一次呼叫。逐列檢查容量，可在來源是 lazy
        // enumerable 時於第 N+1 列立即停止，不會先配置超過契約上限的大型集合。
        var stableIds = new HashSet<string>(comparer ?? StringComparer.Ordinal);
        var rowCount = 0;

        foreach (var row in rows)
        {
            if (rowCount >= maximumRowCount)
            {
                throw new InvalidOperationException(
                    $"{consumerName} 超過允許發布的 {maximumRowCount} 列上限，拒絕發布無界集合。");
            }

            if (row == null)
            {
                throw new InvalidOperationException(
                    $"{consumerName} 存在 null 資料列，無法取得 {identityName}，拒絕發布。");
            }

            var stableId = stableIdSelector(row);
            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new InvalidOperationException(
                    $"{consumerName} 存在空白 {identityName}，拒絕發布不具資料庫身份的資料列。");
            }

            // 僅為拒絕使用者輸入或序列化造成的邊界空白而 Trim；實際比較值不做大小寫、
            // Unicode 或格式轉換。ChurchReport 的 GUID 字串由呼叫端明確傳入 OrdinalIgnoreCase，
            // 未來字串主鍵產品則應依其資料庫 collation 提供 comparer。
            var stableIdForComparison = stableId.Trim();
            if (!stableIds.Add(stableIdForComparison))
            {
                throw new InvalidOperationException(
                    $"{consumerName} 發現重複 {identityName} '{stableIdForComparison}'，拒絕發布衝突集合。");
            }

            rowCount++;
            validatedRows?.Add(row);
        }
    }
}
