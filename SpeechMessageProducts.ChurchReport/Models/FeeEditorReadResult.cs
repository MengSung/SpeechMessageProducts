// ============================================================================
// 檔案：ChurchReport/Models/FeeEditorReadResult.cs
// 目的：承載 ORG-CALL-00066 唯讀端點可安全序列化的 request-local 不可變費用編輯器投影。
//
// 安全與生命週期契約：
// - 型別只保存 Package01 DTO 已投影的 allowlisted scalar，絕不保留 CRM Entity、Fee、FeeList、Session、profile、endpoint 或 credential。
// - result 建構時複製 rows 並以 ReadOnlyCollection 公開；呼叫端不能藉由原陣列或 ICollection 寫入改變已授權 response。
// - 所有資料由一次 HTTP request 擁有，沒有 static cache、subscription、timer、stream、client 或其他資源，因此沒有跨 request retention 或 Dispose 責任。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ChurchReport.Models;

/// <summary>
/// 費用編輯器唯讀 endpoint 的單列 scalar projection。
///
/// 此 immutable 型別只表達 `fees.editor.load.by.disciplelesson` 已驗證的 stor-lesson 資料；它不是既有
/// editable <see cref="Fee"/>、不具有付款修改語意，也不能作為 Create、Update、Assign 或 SaveBatch 的
/// 輸入。每個 instance 只屬於目前 response，沒有持有 Entity、transport handle 或可變 session data。
/// </summary>
public sealed class FeeEditorReadRow
{
    /// <summary>stor-lesson 的純值識別碼；null 代表上游未提供，不能用來觸發 CRM 補查。</summary>
    public Guid? StorLessonId { get; init; }

    /// <summary>聯絡人的純值識別碼；只供目前 response 關聯，不能當 shared cache 或授權 key。</summary>
    public Guid? ContactId { get; init; }

    /// <summary>已由 service 精確驗證等於授權 target 的課程識別碼。</summary>
    public Guid DiscipleLessonId { get; init; }

    /// <summary>上游建立時間的 nullable UTC scalar；null 不以其他 request 的值補齊。</summary>
    public DateTimeOffset? CreatedOn { get; init; }

    /// <summary>上游付款日期的 nullable UTC scalar；此值不代表可寫付款狀態。</summary>
    public DateTimeOffset? PayDate { get; init; }

    /// <summary>上游完成狀態的 nullable純值；不能由 browser 或後續 action 覆寫。</summary>
    public bool? CurrentComplete { get; init; }

    /// <summary>僅供本次顯示的聯絡人名稱；不得寫入 static cache 或診斷緩衝。</summary>
    public string? ContactName { get; init; }

    /// <summary>僅供本次顯示的聯絡人電話；不建立跨 request retention。</summary>
    public string? ContactMobile { get; init; }

    /// <summary>由 typed DTO 提供的課程顯示名稱；不得再以 CRM SDK 補查。</summary>
    public string? DiscipleLessonName { get; init; }

    /// <summary>由 connector 正規化的 nullable UTC 開課時間。</summary>
    public DateTimeOffset? ClassStartDate { get; init; }

    /// <summary>由 connector 投影的階段名稱；null 保留來源缺失，而非猜測資料。</summary>
    public string? StageName { get; init; }

    /// <summary>與 stor-lesson 關聯的 nullable費用 scalar；它不是既有 Fee entity 的可寫金額模型。</summary>
    public decimal? FeeAmount { get; init; }
}

/// <summary>
/// 一次完整驗證後才可發布的唯讀費用編輯器結果。
///
/// constructor 會對 rows 逐一複製，讓上游 ProductClient 的 array、list 或後續 test/client mutation 不會
/// 改變 response；公開屬性是 read-only wrapper，無法轉型回可寫 array。result 不含外部 resource，故其
/// 最大生命週期是 controller 回傳 JSON 前後的 managed request scope，沒有 Dispose 或 background cleanup。
/// </summary>
public sealed class FeeEditorReadResult
{
    /// <summary>建立完整、不可變的 response；null rows 不是合法成功結果，避免隱藏 partial response。</summary>
    /// <param name="rows">已完整驗證其 lesson target 的 request-local scalar rows。</param>
    public FeeEditorReadResult(IEnumerable<FeeEditorReadRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var copy = new List<FeeEditorReadRow>();
        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row);
            copy.Add(new FeeEditorReadRow
            {
                StorLessonId = row.StorLessonId,
                ContactId = row.ContactId,
                DiscipleLessonId = row.DiscipleLessonId,
                CreatedOn = row.CreatedOn,
                PayDate = row.PayDate,
                CurrentComplete = row.CurrentComplete,
                ContactName = row.ContactName,
                ContactMobile = row.ContactMobile,
                DiscipleLessonName = row.DiscipleLessonName,
                ClassStartDate = row.ClassStartDate,
                StageName = row.StageName,
                FeeAmount = row.FeeAmount
            });
        }

        Rows = new ReadOnlyCollection<FeeEditorReadRow>(copy);
    }

    /// <summary>已 defensive-copy 的不可寫 rows；每次 service invocation 都建立新 wrapper 與新 row instances。</summary>
    public IReadOnlyList<FeeEditorReadRow> Rows { get; }
}
