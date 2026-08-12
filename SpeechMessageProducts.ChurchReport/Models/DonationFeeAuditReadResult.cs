// ============================================================================
// 檔案：ChurchReport/Models/DonationFeeAuditReadResult.cs
// 目的：承載奉獻稽核 typed read 的 request-local DTO 投影與經檢查的總額。
//
// 此結果刻意不引用 CRM Entity、DonationPaymentFormModel、Session、Profile、Credential 或
// CancellationToken。所有費用集合在 DonationFeeQueryService 的單次呼叫中建立；結果只在
// 呼叫端 HTTP request 回應期間使用，且以不可變讀取介面公開，避免 A/B 讀取透過既有付款
// 表單模型、cache 或 static 欄位交叉覆寫。沒有需要釋放的外部資源。
// ============================================================================

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;

namespace ChurchReport.Models;

/// <summary>
/// 奉獻稽核依聯絡人讀取所產生的獨立結果。
/// </summary>
/// <remarks>
/// 結果由一次已授權的 typed ProductClient 回應投影而成。<see cref="Fees"/> 的 concrete
/// collection 只由建構時複製的陣列建立，並以不可變的 read-only wrapper 發布，因此呼叫端
/// 不能轉型取得可替換元素的陣列，也不能經由此型別取得 session-owned 付款表單或 CRM SDK
/// 物件；<see cref="TotalAmount"/> 已在發布前以 checked <see cref="Int32"/> 範圍驗證，
/// 溢位會在結果建立前 fail closed。
/// </remarks>
public sealed class DonationFeeAuditReadResult
{
    /// <summary>
    /// 建立 request-local 稽核結果，並複製輸入列以隔離呼叫端日後對集合的修改。
    /// </summary>
    /// <param name="fees">已完成 DTO 投影的費用列；不得為 <see langword="null"/>。</param>
    /// <param name="totalAmount">已驗證位於 <see cref="Int32"/> 範圍內的總額。</param>
    public DonationFeeAuditReadResult(IReadOnlyList<DonationFeeAuditRow> fees, int totalAmount)
    {
        ArgumentNullException.ThrowIfNull(fees);

        var copiedFees = new DonationFeeAuditRow[fees.Count];
        for (var index = 0; index < fees.Count; index++)
        {
            copiedFees[index] = fees[index] ?? throw new ArgumentException(
                "Fee audit rows must not contain null values.",
                nameof(fees));
        }

        // 封裝複製後的陣列而不直接公開：雖然 element 本身是不可變 DTO，直接回傳陣列仍可被
        // 呼叫端轉型並替換索引，造成同一 request 後續 JSON 轉換觀察到非原始投影。ReadOnlyCollection
        // 在此沒有外部資源或長壽命 owner；它只封閉目前結果的可變集合表面並隨 request 釋放。
        Fees = new ReadOnlyCollection<DonationFeeAuditRow>(copiedFees);
        TotalAmount = totalAmount;
    }

    /// <summary>
    /// 僅限本次讀取的不可變費用投影列。集合不連到付款表單、CRM Entity 或共享快取，且不能
    /// 被轉型為陣列後替換元素。
    /// </summary>
    public IReadOnlyList<DonationFeeAuditRow> Fees { get; }

    /// <summary>
    /// 由服務在回傳前以 checked 累加驗證的總金額。
    /// </summary>
    public int TotalAmount { get; }
}

/// <summary>
/// 奉獻稽核 JSON 列的不可變、非 SDK 投影。
/// </summary>
/// <remarks>
/// 本型別不是 <see cref="DonationPaymentFormModel"/> 的子集或替身；它不帶付款流程可變欄位，
/// 也不參考 CRM <c>Entity</c>、Session、profile 或 cancellation state。所有值在 typed DTO
/// mapping 時建立後即不可修改，讓同一 session 中的其他付款流程不能藉由 audit response
/// 改寫或重用資料。
/// </remarks>
public sealed class DonationFeeAuditRow
{
    /// <summary>
    /// 建立單一已核准欄位的稽核列；呼叫端不能在建構後變更任一欄位。
    /// </summary>
    /// <param name="category">奉獻分類顯示文字。</param>
    /// <param name="dedicationDate">建立或認獻日期。</param>
    /// <param name="payDate">付款日期。</param>
    /// <param name="payWay">付款方式顯示文字。</param>
    /// <param name="amount">已映射且位於 Int32 範圍內的金額。</param>
    /// <param name="paidPeriod">已繳期別。</param>
    /// <param name="others">其他說明。</param>
    public DonationFeeAuditRow(
        string category,
        DateTime dedicationDate,
        DateTime payDate,
        string payWay,
        int amount,
        string paidPeriod,
        string others)
    {
        Category = category ?? string.Empty;
        DedicationDate = dedicationDate;
        PayDate = payDate;
        PayWay = payWay ?? string.Empty;
        Amount = amount;
        PaidPeriod = paidPeriod ?? string.Empty;
        Others = others ?? string.Empty;
    }

    /// <summary>奉獻分類。</summary>
    public string Category { get; }

    /// <summary>認獻日期。</summary>
    public DateTime DedicationDate { get; }

    /// <summary>付款日期。</summary>
    public DateTime PayDate { get; }

    /// <summary>付款方式。</summary>
    public string PayWay { get; }

    /// <summary>金額。</summary>
    public int Amount { get; }

    /// <summary>已繳期別。</summary>
    public string PaidPeriod { get; }

    /// <summary>其他說明。</summary>
    public string Others { get; }
}
