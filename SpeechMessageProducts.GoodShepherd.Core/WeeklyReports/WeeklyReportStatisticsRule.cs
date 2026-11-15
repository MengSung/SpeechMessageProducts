/*
 * 檔案整體說明
 *
 * 目的與定位：提供 P5-05 週報統計的雙目標純規則，由外殼完成教會授權、資料查詢與
 * PreImage 合併後呼叫。不保存 SDK Entity、服務、身分、token 或任何跨操作可變狀態。
 *
 * 主要功能與流程：比較 prior/current 旗標，只在新計算要求時處理最多一萬筆正式出席列。
 * 所有 record ID 必須唯一；相同 contact 的不同 record 仍分別計算。有效分類排除三個
 * 舊值，缺分類回傳有序且不重複的 contact 修補意圖；缺整數沿用 -9999。成功後一次
 * 發布不可變結果，零分母四欄為 null，表示不得覆寫既值。旗標採外殼提供的事件 GUID。
 * CalculationPrefix 固定讀取產品 zh-TW 資源原文；與通知 accessor 共用唯讀生成資源表，
 * 此表容量僅隨版控資源增加，不含教會覆寫、scope 或任何請求資料。
 *
 * 使用邊界與維護：外殼須固定以 zh-TW 資源提供計算前綴，並保證每列已核對來源教會；
 * 本檔不以 GUID 形式推定授權。重複列、預算或 Int32 溢位直接拒絕，不發布部分意圖。
 * 與舊碼的明列差異為相同旗標不再重算、最小寫入建議及溢位拒絕。集合與 HashSet 僅
 * 存活於同步呼叫，輸出由呼叫端持有，沒有快取、背景工作、I/O 或需 Dispose 的資源。
 * 寫入順序、條件更新與真實例外告警由外殼負責；雙目標純測試不代替資料庫驗證。
 */
using System;
using System.Collections.Generic;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Resources;

namespace SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;

/// <summary>
/// 一筆已經外殼核對教會的不可變出席純值；正式 record 身分與 contact 身分分開，
/// 不把同會友的不同出席記錄合併。缺分類及缺整數均保留為 null，交規則明確處理。
/// </summary>
public sealed class WeeklyReportAttendanceRow
{
    /// <summary>建立單次統計使用的純值；不存原 Entity，也不接受缺失的正式識別。</summary>
    /// <param name="recordId">出席列的非空正式資料庫 ID。</param>
    /// <param name="contactId">該列所指會友的非空正式 ID，教會歸屬由外殼先驗證。</param>
    /// <param name="isActive">出席列 statecode 存在且為 0；非 contact 的狀態。</param>
    /// <param name="classification">會友 customertypecode；null 代表需回填舊預設分類。</param>
    /// <param name="sunday">主日本週 Int32；null 保留缺值以重現 -9999。</param>
    /// <param name="group">小組本週 Int32；null 保留缺值以重現 -9999。</param>
    /// <exception cref="ArgumentException">任一正式識別為空，不能以臨時鍵補齊。</exception>
    public WeeklyReportAttendanceRow(Guid recordId, Guid contactId, bool isActive, int? classification, int? sunday, int? group)
    {
        if (recordId == Guid.Empty) throw new ArgumentException("A record identity is required.", nameof(recordId));
        if (contactId == Guid.Empty) throw new ArgumentException("A contact identity is required.", nameof(contactId));
        RecordId = recordId;
        ContactId = contactId;
        IsActive = isActive;
        Classification = classification;
        Sunday = sunday;
        Group = group;
    }

    /// <summary>正式出席列識別，統計集合內不得重複，不得以姓名或 contact ID 取代。</summary>
    public Guid RecordId { get; }
    /// <summary>已由外殼核對教會的正式會友識別；不同出席列可共用此值。</summary>
    public Guid ContactId { get; }
    /// <summary>僅表示出席列已啟用；規則不額外檢查會友啟用狀態，以保留統計舊義。</summary>
    public bool IsActive { get; }
    /// <summary>會友分類原始選項；缺值不默認成可計算分類。</summary>
    public int? Classification { get; }
    /// <summary>主日本週原始整數；不夾成布林，不在 DTO 將 null 改成零。</summary>
    public int? Sunday { get; }
    /// <summary>小組本週原始整數；不夾成布林，不在 DTO 將 null 改成零。</summary>
    public int? Group { get; }
}

/// <summary>
/// 完整計算完成後的不可變更新建議。四個統計欄位同為 null 時，外殼必須省略四欄；
/// 分類修補須先於週報更新執行，失敗不得宣稱已完成旗標或統計。
/// </summary>
public sealed class WeeklyReportStatisticsResult
{
    /// <summary>只由規則發布已完成候選；複製待補分類清單，不引用外部可變集合。</summary>
    internal WeeklyReportStatisticsResult(int eligibleCount, int? sundayCount, int? groupCount,
        double? sundayRate, double? groupRate, string? nextSavedFlag, List<Guid> contacts)
    {
        EligibleCount = eligibleCount;
        SundayCount = sundayCount;
        GroupCount = groupCount;
        SundayRate = sundayRate;
        GroupRate = groupRate;
        NextSavedFlag = nextSavedFlag;
        ContactIdsNeedingClassification = new List<Guid>(contacts).AsReadOnly();
    }

    /// <summary>有效出席列數，分母以 record 為單位，不以不同 contact 數目代替。</summary>
    public int EligibleCount { get; }
    /// <summary>主日人數建議值；null 表示不得寫入該欄。</summary>
    public int? SundayCount { get; }
    /// <summary>小組人數建議值；null 表示不得寫入該欄。</summary>
    public int? GroupCount { get; }
    /// <summary>未乘百或捨入的主日 Double 比率；null 表示不得寫入該欄。</summary>
    public double? SundayRate { get; }
    /// <summary>未乘百或捨入的小組 Double 比率；null 表示不得寫入該欄。</summary>
    public double? GroupRate { get; }
    /// <summary>事件 GUID 的 D 格式旗標；null 表示本次不處理計算、不得更新旗標。</summary>
    public string? NextSavedFlag { get; }
    /// <summary>依首次缺分類列排序的唯一 contact IDs；外殼回填 customertypecode=100000000。</summary>
    public IReadOnlyList<Guid> ContactIdsNeedingClassification { get; }
}

/// <summary>不持有請求狀態的同步統計規則，所有候選僅在此次呼叫中建立與驗證。</summary>
public static class WeeklyReportStatisticsRule
{
    /// <summary>
    /// 僅從產品生成資料一次建立唯讀固定資源表，供兩個 accessor 共用；不含教會覆寫，
    /// 不授權或選擇 scope，且不向外公開可變生成陣列。存活至組件卸載，無待釋放資源。
    /// </summary>
    internal static SystemTextTable ProductResourceTable { get; } =
        SystemTextTable.FromRows(GoodShepherdTextData.Cultures, GoodShepherdTextData.Rows);

    /// <summary>單次統計的最大正式列數；外殼必須完成分頁且在超額時拒絕，不得只傳前一萬筆。</summary>
    public const int MaximumRows = 10000;

    /// <summary>
    /// 取得產品 WeeklyReport.CalculatePrefix 的固定 zh-TW 原文，供薄殼辨識舊旗標；
    /// 不套教會覆寫、不受收件者語系影響，也不執行授權或 scope 推導。
    /// </summary>
    /// <returns>產品生成資源的固定計算前綴；缺鍵時沿用文字表例外，不自行補中文字面值。</returns>
    public static string CalculationPrefix() =>
        ProductResourceTable.GetText("zh-TW", new TextKey("WeeklyReport.CalculatePrefix"));

    /// <summary>
    /// 只在 current 以指定前綴開頭且相較 prior 改變時重算；Ordinal 比較不讀程序文化。
    /// 重複 record 即使已停用或分類排除仍拒絕，避免用過濾隱藏資料衝突。全部驗證與
    /// checked 加總成功後才回傳結果；外殼負責穩定事件 ID、授權及所有條件寫入。
    /// </summary>
    /// <param name="priorFlag">PreImage 旗標；null 與空字串保留不同語意。</param>
    /// <param name="currentFlag">合併此次 Target 後的旗標，最多 100 字元。</param>
    /// <param name="eventId">外殼提供的非空事件 GUID；同一事件重試必須相同。</param>
    /// <param name="rows">本操作已驗證教會且完整分頁的出席純值，最多 MaximumRows 筆。</param>
    /// <param name="calculationPrefix">固定 zh-TW 資源 WeeklyReport.CalculatePrefix，不可空或超過 100 字元。</param>
    /// <returns>可空四欄、有效列數、有序分類修補及穩定旗標；不執行任何外部副作用。</returns>
    /// <exception cref="ArgumentException">識別、旗標預算或列身分不符；不回顯輸入資料。</exception>
    /// <exception cref="ArgumentNullException">列集合未提供。</exception>
    /// <exception cref="OverflowException">Int32 加總超界；不採未宣告的環繞計算。</exception>
    public static WeeklyReportStatisticsResult Evaluate(string? priorFlag, string? currentFlag, Guid eventId,
        IReadOnlyList<WeeklyReportAttendanceRow> rows, string calculationPrefix)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(rows);
#else
        if (rows is null) throw new ArgumentNullException(nameof(rows));
#endif
        if (eventId == Guid.Empty) throw new ArgumentException("An event identity is required.", nameof(eventId));
        if (string.IsNullOrEmpty(calculationPrefix) || calculationPrefix.Length > 100)
            throw new ArgumentException("A bounded calculation prefix is required.", nameof(calculationPrefix));
        var rowCount = rows.Count;
        if (priorFlag?.Length > 100 || currentFlag?.Length > 100 || rowCount < 0 || rowCount > MaximumRows)
            throw new ArgumentException("Statistics input exceeds its bounded budget.");
        var contacts = new List<Guid>();
        if (currentFlag is null || !currentFlag.StartsWith(calculationPrefix, StringComparison.Ordinal)
            || string.Equals(priorFlag, currentFlag, StringComparison.Ordinal))
            return new WeeklyReportStatisticsResult(0, null, null, null, null, null, contacts);

        var records = new HashSet<Guid>();
        var missingContacts = new HashSet<Guid>();
        var count = 0;
        var sunday = 0;
        var group = 0;
        // 依已驗證的 Count 做固定次數索引，不讓自訂集合的無界 enumerator 繞過列數預算。
        for (var index = 0; index < rowCount; index++)
        {
            var row = rows[index];
            if (row is null || !records.Add(row.RecordId))
                throw new ArgumentException("Statistics rows require unique formal identities.", nameof(rows));
            if (!row.IsActive) continue;
            if (!row.Classification.HasValue)
            {
                if (missingContacts.Add(row.ContactId)) contacts.Add(row.ContactId);
                continue;
            }
            if (row.Classification is 100000004 or 100000000 or 100000007) continue;
            count++;
            sunday = checked(sunday + (row.Sunday ?? -9999));
            group = checked(group + (row.Group ?? -9999));
        }
        return new WeeklyReportStatisticsResult(count, count > 0 ? sunday : (int?)null,
            count > 0 ? group : (int?)null, count > 0 ? (double)sunday / count : (double?)null,
            count > 0 ? (double)group / count : (double?)null, eventId.ToString("D"), contacts);
    }
}
