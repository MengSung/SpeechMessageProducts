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
/// 單筆 stor lesson（上課紀錄）讀取結果。此型別只承載由 Package01 connector 已投影的
/// scalar，供單一產品 request 在畫面或服務中短暫使用；不得回填 CRM Entity、alias wrapper、
/// endpoint、credential 或另一個 profile 的可變資料，也不擁有連線、timer 或其他待釋放資源。
/// </summary>
public sealed class StorLessonRecordDto
{
    /// <summary>
    /// stor-lesson 記錄的純值識別碼。這是 connector 已驗證的 GUID，不是可讓呼叫端改選
    /// Organization、profile 或 owner 的路由輸入；null 表示上游沒有可安全公開的記錄識別。
    /// </summary>
    public Guid? StorLessonId { get; init; }

    /// <summary>
    /// 關聯聯絡人的純值識別碼。它只屬於目前 response 的資料投影，不能作為 shared cache key
    /// 或跨 request session 狀態；授權仍由 controller/service 的 server-derived 邊界負責。
    /// </summary>
    public Guid? ContactId { get; init; }

    /// <summary>
    /// 關聯門徒課程的純值識別碼。nullable 使資料缺失能被明確表達，禁止以額外 SDK 查詢猜測、
    /// 補齊或保留 CRM Entity，避免延長 connector lease 之外的資源生命週期。
    /// </summary>
    public Guid? DiscipleLessonId { get; init; }

    /// <summary>
    /// 上課紀錄建立時間的 UTC 純值。時間正規化由 connector 完成；此 DTO 不保存時區 metadata，
    /// 呼叫端僅可在自己的 request 顯示層進行格式化。
    /// </summary>
    public DateTimeOffset? CreatedOn { get; init; }

    /// <summary>
    /// 上課紀錄付款日期的 UTC 純值。null 保留來源未填狀態，不得以目前時間或前一筆 response
    /// 的日期補值，避免跨使用者結果混用。
    /// </summary>
    public DateTimeOffset? PayDate { get; init; }

    /// <summary>
    /// 上課紀錄是否已完成。此 nullable scalar 由同一 operation response 提供，不是可以由 UI
    /// 或呼叫端覆寫的授權、流程或 session 狀態。
    /// </summary>
    public bool? CurrentComplete { get; init; }

    /// <summary>
    /// 關聯聯絡人的顯示名稱。此資料只供當前回應顯示，禁止放入未依完整隔離邊界分割的 static
    /// cache、診斷緩衝或背景工作。
    /// </summary>
    public string? ContactName { get; init; }

    /// <summary>
    /// 關聯聯絡人的顯示電話。null 保留資料缺失；ProductClient 不記錄、遮罩或重用它，避免 PII
    /// 在跨 request 記憶體中延長生命週期。
    /// </summary>
    public string? ContactMobile { get; init; }

    /// <summary>
    /// 關聯門徒課程的顯示名稱。它由受限 lesson link 投影，不得由產品端重新呼叫 CRM SDK 補查。
    /// </summary>
    public string? DiscipleLessonName { get; init; }

    /// <summary>
    /// 關聯門徒課程的開課 UTC 時間。connector 已將 CRM 日期轉為 <see cref="DateTimeOffset"/>；
    /// null 表示來源沒有值，不能以 local time、預設日期或別的 request 資料猜測替換。
    /// </summary>
    public DateTimeOffset? ClassStartDate { get; init; }

    /// <summary>
    /// 關聯門徒課程的目前階段名稱。字串已通過 connector 的 byte budget，僅隨當前不可變 DTO
    /// 傳遞，不保留 CRM alias、Entity 或可變 session 資料。
    /// </summary>
    public string? StageName { get; init; }

    /// <summary>
    /// 與此上課紀錄關聯的費用純值。這個 nullable scalar 不附帶金融 Entity 或 transport resource，
    /// 後續流程若需寫入必須由其 own capability 另行取得授權與 deterministic cleanup。
    /// </summary>
    public decimal? FeeAmount { get; init; }
}
