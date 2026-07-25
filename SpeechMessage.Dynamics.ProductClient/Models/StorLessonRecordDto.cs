// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/Models/StorLessonRecordDto.cs
// 目的：Package 1 stor-lesson 讀取結果的中立 DTO。
//
// 保母教學：
// - 這是「上課紀錄 / 繳費編輯畫面」用的投影，不是 CRM Entity。
// - 產品端不要直接依賴 OData JSON 欄位名稱散落在 controller。
// - DiscipleLessonId / ContactId 可能是 lookup id 或 _xxx_value 欄位，解析層會處理。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.Models;

/// <summary>
/// 單筆 stor lesson（上課紀錄）讀取結果。
/// </summary>
public sealed class StorLessonRecordDto
{
    public Guid? StorLessonId { get; init; }
    public Guid? ContactId { get; init; }
    public Guid? DiscipleLessonId { get; init; }
    public DateTimeOffset? CreatedOn { get; init; }
    public DateTimeOffset? PayDate { get; init; }
    public bool? CurrentComplete { get; init; }
    public string? ContactName { get; init; }
    public string? ContactMobile { get; init; }
    public string? DiscipleLessonName { get; init; }
    public decimal? FeeAmount { get; init; }
}