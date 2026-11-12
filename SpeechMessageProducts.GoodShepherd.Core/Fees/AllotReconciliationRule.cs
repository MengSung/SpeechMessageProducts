/*
 * 檔案整體說明
 *
 * 目的與定位：定義 GoodShepherd Core P4-03 永豐撥款對帳所需的純值模型及決定性規則。
 * 它由 AllotQueryHandler 在已授權教會資料讀取後呼叫，不讀取時鐘、Dataverse、設定檔、
 * 訂單以外的身分資料，也不決定教會或 payment profile；付款與費用金額均以 TWD Money
 * 表示主要貨幣單位，日期以 DateTime 的年月日欄位承載以兼容 net462。
 *
 * 主要功能與流程：ResolvePayType 將核定付款方式及信用卡訂單前綴映射到 C/M/A；
 * ResolveWindow 以呼叫端提供的教會今日計算固定日期窗；Match 按舊批次順序檢查有效
 * 訂單號並保留最後符合明細，回傳撥款金額扣除手續費、手續費、撥款日期與批次編號。
 * 集合建構時複製並唯讀包裝，避免呼叫端之後修改本次操作使用的批次或明細快照。
 *
 * 使用邊界與維護：本檔不含 I/O、全域或靜態可變資料、快取、工作、計時器或可 Dispose
 * 資源；值物件僅隨呼叫端持有。不得把空白訂單號當成相符，不得從伺服器本機時鐘推算
 * churchToday，不得以 DateOnly 破壞 net462 編譯，也不得把永豐分數再次當作主要貨幣值。
 * 以雙目標 AllotReconciliationRuleTests 的固定合成案例、§4A 空白值及金額斷言驗證；
 * 規則錯誤原樣交給呼叫端，離線測試不代表已查詢永豐或更新實際 Dataverse。
 */
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SpeechMessage.Localization;

namespace SpeechMessageProducts.GoodShepherd.Core.Fees;

/// <summary>收費單中對撥款比對有用的伺服器讀取欄位純值，不包含任意 SDK Entity。</summary>
public sealed class FeeAllotSnapshot
{
    /// <summary>建立一次對帳操作專用、不可變的收費單比對輸入。</summary>
    /// <param name="feeId">Dataverse new_fee 的正式資料列 ID。</param>
    /// <param name="payWay">new_pay_way OptionSet 數值。</param>
    /// <param name="payDate">new_pay_date 的日曆日期；null 表示欄位未設定。</param>
    /// <param name="cardOrderNo">信用卡付款訂單號；缺少時以空字串表示，不得作為有效比對值。</param>
    /// <param name="paidCardOrderNo">信用卡已付款訂單號；缺少時以空字串表示。</param>
    /// <param name="atmOrderNo">ATM 付款訂單號；缺少時以空字串表示。</param>
    /// <param name="paidAtmOrderNo">ATM 已付款訂單號；缺少時以空字串表示。</param>
    /// <exception cref="ArgumentException">feeId 為空 GUID。</exception>
    public FeeAllotSnapshot(
        Guid feeId,
        int payWay,
        DateTime? payDate,
        string cardOrderNo,
        string paidCardOrderNo,
        string atmOrderNo,
        string paidAtmOrderNo)
    {
        if (feeId == Guid.Empty)
        {
            throw new ArgumentException("A fee identity is required.", nameof(feeId));
        }

        FeeId = feeId;
        PayWay = payWay;
        PayDate = payDate?.Date;
        CardOrderNo = cardOrderNo ?? string.Empty;
        PaidCardOrderNo = paidCardOrderNo ?? string.Empty;
        AtmOrderNo = atmOrderNo ?? string.Empty;
        PaidAtmOrderNo = paidAtmOrderNo ?? string.Empty;
    }

    /// <summary>取得 new_fee 的正式資料列 ID，作為單筆讀取及條件更新的穩定身分。</summary>
    public Guid FeeId { get; }

    /// <summary>取得 Dataverse 付款方式選項值；未支援值由純規則映射為不查詢。</summary>
    public int PayWay { get; }

    /// <summary>取得收費日的日曆日期，不作 UTC 或伺服器時區轉換。</summary>
    public DateTime? PayDate { get; }

    /// <summary>取得原信用卡訂單號；空白值永遠不參與相等比對。</summary>
    public string CardOrderNo { get; }

    /// <summary>取得已付款信用卡訂單號；空白值永遠不參與相等比對。</summary>
    public string PaidCardOrderNo { get; }

    /// <summary>取得原 ATM 訂單號；空白值永遠不參與相等比對。</summary>
    public string AtmOrderNo { get; }

    /// <summary>取得已付款 ATM 訂單號；空白值永遠不參與相等比對。</summary>
    public string PaidAtmOrderNo { get; }
}

/// <summary>一筆永豐撥款明細；金額及手續費已由 provider 轉為主要貨幣單位。</summary>
public sealed class AllotDetail
{
    /// <summary>建立已完成 provider 單位轉換的不可變撥款明細。</summary>
    /// <param name="orderNo">永豐明細訂單號；空或空白不會成為比對依據。</param>
    /// <param name="amount">主要貨幣單位金額，P4-03 永豐回應固定為 TWD。</param>
    /// <param name="fee">主要貨幣單位手續費，P4-03 永豐回應固定為 TWD。</param>
    /// <exception cref="ArgumentException">金額不是有效 TWD Money，或兩金額幣別不同。</exception>
    public AllotDetail(string orderNo, Money amount, Money fee)
    {
        var amountCurrency = amount.Currency;
        var feeCurrency = fee.Currency;
        if (!string.Equals(amountCurrency, "TWD", StringComparison.Ordinal)
            || !string.Equals(feeCurrency, amountCurrency, StringComparison.Ordinal))
        {
            throw new ArgumentException("Allot amounts must use TWD.");
        }

        OrderNo = orderNo ?? string.Empty;
        Amount = amount;
        Fee = fee;
    }

    /// <summary>取得永豐回傳的訂單號；比對時不會 trim 或將空白值視為相等。</summary>
    public string OrderNo { get; }

    /// <summary>取得已由 provider 從分轉成元的撥款金額。</summary>
    public Money Amount { get; }

    /// <summary>取得已由 provider 從分轉成元的撥款手續費。</summary>
    public Money Fee { get; }
}

/// <summary>一個永豐撥款批次及其操作期間唯讀的明細快照。</summary>
public sealed class AllotBatch
{
    /// <summary>建立撥款批次並複製明細，避免後續 caller mutation 改變對帳結果。</summary>
    /// <param name="allotNo">永豐撥款批次編號。</param>
    /// <param name="allotDate">provider 回應的撥款日期；規則只保留其日曆日期。</param>
    /// <param name="details">該批次明細；建構時會複製並以唯讀集合發布。</param>
    /// <exception cref="ArgumentNullException">明細集合為 null。</exception>
    public AllotBatch(string allotNo, DateTime allotDate, IEnumerable<AllotDetail> details)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(details);
#else
        if (details is null)
        {
            throw new ArgumentNullException(nameof(details));
        }
#endif

        AllotNo = allotNo ?? string.Empty;
        AllotDate = allotDate.Date;
        Details = new ReadOnlyCollection<AllotDetail>(details.ToArray());
    }

    /// <summary>取得永豐批次編號。</summary>
    public string AllotNo { get; }

    /// <summary>取得不含時區換算的撥款日日期部分。</summary>
    public DateTime AllotDate { get; }

    /// <summary>取得建立時複製且不可由呼叫端修改的明細集合。</summary>
    public IReadOnlyList<AllotDetail> Details { get; }
}

/// <summary>將對帳規則找到的四個收費單撥款欄位封裝為單次更新純值。</summary>
public sealed class FeeAllotUpdate
{
    /// <summary>建立完整撥款更新值；只接受 TWD，避免在寫入前隱含換匯。</summary>
    /// <param name="allotAmount">撥款金額扣除手續費後的 TWD 金額。</param>
    /// <param name="allotFee">撥款手續費 TWD 金額。</param>
    /// <param name="allotDate">撥款曆日；寫入時會轉成同一天午夜。</param>
    /// <param name="allotNo">永豐撥款編號。</param>
    /// <exception cref="ArgumentException">欄位金額不是有效且一致的 TWD Money。</exception>
    public FeeAllotUpdate(Money allotAmount, Money allotFee, DateTime allotDate, string allotNo)
    {
        var amountCurrency = allotAmount.Currency;
        if (!string.Equals(amountCurrency, "TWD", StringComparison.Ordinal)
            || !string.Equals(allotFee.Currency, amountCurrency, StringComparison.Ordinal))
        {
            throw new ArgumentException("Allot update amounts must use TWD.");
        }

        AllotAmount = allotAmount;
        AllotFee = allotFee;
        AllotDate = allotDate.Date;
        AllotNo = allotNo ?? string.Empty;
    }

    /// <summary>取得扣除手續費後的撥款金額。</summary>
    public Money AllotAmount { get; }

    /// <summary>取得撥款手續費金額。</summary>
    public Money AllotFee { get; }

    /// <summary>取得撥款日期的日曆日期部分。</summary>
    public DateTime AllotDate { get; }

    /// <summary>取得永豐撥款批次編號。</summary>
    public string AllotNo { get; }
}

/// <summary>永豐撥款查詢的教會日曆日期範圍，供 provider mapper 轉換成其日期 DTO。</summary>
public sealed class AllotQueryWindow
{
    /// <summary>建立日期部分明確且不進行時區換算的查詢區間。</summary>
    /// <param name="fromDate">含起日。</param>
    /// <param name="toDate">含迄日。</param>
    public AllotQueryWindow(DateTime fromDate, DateTime toDate)
    {
        FromDate = fromDate.Date;
        ToDate = toDate.Date;
    }

    /// <summary>取得查詢起日的日曆日期部分。</summary>
    public DateTime FromDate { get; }

    /// <summary>取得查詢迄日的日曆日期部分。</summary>
    public DateTime ToDate { get; }
}

/// <summary>只以呼叫端提供的已授權收費快照及教會今日執行確定性的撥款規則。</summary>
public static class AllotReconciliationRule
{
    /// <summary>
    /// 依付款方式及核定訂單號前綴選擇永豐查詢類型；未知付款方式回 null 表示正常不查詢。
    /// 不依賴呼叫端教會、系統時區或靜態設定。
    /// </summary>
    /// <param name="payWay">Dataverse new_pay_way 選項值。</param>
    /// <param name="cardOrderNo">信用卡訂單號；只有信用卡方式會使用其前綴。</param>
    /// <param name="paidCardOrderNo">已付款信用卡訂單號，僅原訂單號不是 C 前綴時再檢查。</param>
    /// <returns>永豐類型 C、M 或 A；不支援方式為 null。</returns>
    public static string? ResolvePayType(int payWay, string cardOrderNo, string paidCardOrderNo)
    {
        switch (payWay)
        {
            case 100000001:
                return StartsWithCreditPrefix(cardOrderNo) || StartsWithCreditPrefix(paidCardOrderNo)
                    ? "C"
                    : "M";
            case 100000002:
                return "A";
            case 100000007:
                return "M";
            default:
                return null;
        }
    }

    /// <summary>
    /// 以 churchToday 的日期部分計算舊核定窗：有收費日從前一天開始、最多延伸十五天；
    /// 缺收費日時起日明確採 churchToday 減十六天，不使用 legacy placeholder 日期。
    /// </summary>
    /// <param name="payDate">收費單付款日期；null 表示未提供日期。</param>
    /// <param name="churchToday">呼叫端依授權教會 IANA 時區取得的今日日期。</param>
    /// <returns>以日期部分表示且不經 UTC 轉換的查詢範圍。</returns>
    public static AllotQueryWindow ResolveWindow(DateTime? payDate, DateTime churchToday)
    {
        var today = churchToday.Date;
        if (!payDate.HasValue)
        {
            return new AllotQueryWindow(today.AddDays(-16), today);
        }

        var paymentDate = payDate.Value.Date;
        var requestedEnd = paymentDate.AddDays(15);
        return new AllotQueryWindow(paymentDate.AddDays(-1), requestedEnd < today ? requestedEnd : today);
    }

    /// <summary>
    /// 依 C/M 卡類訂單或 A 類 ATM 訂單尋找批次明細；空白訂單號永不相符，
    /// 多筆相符時保留舊行為的最後一筆。回傳 null 表示無有效訂單可更新。
    /// </summary>
    /// <param name="fee">已由 FeeAllotRecords 讀取並核對 church 範圍的單筆快照。</param>
    /// <param name="payType">ResolvePayType 的 C、M 或 A 結果。</param>
    /// <param name="batches">單次 provider 回應的批次唯讀集合，不能跨操作快取。</param>
    /// <returns>四個撥款回寫值，或在沒有相符訂單時回 null。</returns>
    /// <exception cref="ArgumentNullException">必要純值參數為 null。</exception>
    public static FeeAllotUpdate? Match(FeeAllotSnapshot fee, string payType, IReadOnlyList<AllotBatch> batches)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(fee);
        ArgumentNullException.ThrowIfNull(payType);
        ArgumentNullException.ThrowIfNull(batches);
#else
        if (fee is null)
        {
            throw new ArgumentNullException(nameof(fee));
        }

        if (payType is null)
        {
            throw new ArgumentNullException(nameof(payType));
        }

        if (batches is null)
        {
            throw new ArgumentNullException(nameof(batches));
        }
#endif

        string firstOrder;
        string secondOrder;
        if (string.Equals(payType, "A", StringComparison.Ordinal))
        {
            firstOrder = fee.AtmOrderNo;
            secondOrder = fee.PaidAtmOrderNo;
        }
        else if (string.Equals(payType, "C", StringComparison.Ordinal)
            || string.Equals(payType, "M", StringComparison.Ordinal))
        {
            firstOrder = fee.CardOrderNo;
            secondOrder = fee.PaidCardOrderNo;
        }
        else
        {
            return null;
        }

        FeeAllotUpdate? lastMatch = null;
        foreach (var batch in batches)
        {
            if (batch is null)
            {
                throw new ArgumentException("Allot batches cannot contain null entries.", nameof(batches));
            }

            foreach (var detail in batch.Details)
            {
                if (detail is null)
                {
                    throw new ArgumentException("Allot details cannot contain null entries.", nameof(batches));
                }

                if (!MatchesNonBlank(detail.OrderNo, firstOrder)
                    && !MatchesNonBlank(detail.OrderNo, secondOrder))
                {
                    continue;
                }

                lastMatch = new FeeAllotUpdate(
                    new Money(checked(detail.Amount.Amount - detail.Fee.Amount), detail.Amount.Currency),
                    detail.Fee,
                    batch.AllotDate,
                    batch.AllotNo);
            }
        }

        return lastMatch;
    }

    /// <summary>只接受精確 ordinal 相等且兩側均非空白的訂單號，避免空字串造成誤更新。</summary>
    private static bool MatchesNonBlank(string candidate, string orderNo) =>
        !string.IsNullOrWhiteSpace(candidate)
        && !string.IsNullOrWhiteSpace(orderNo)
        && string.Equals(candidate, orderNo, StringComparison.Ordinal);

    /// <summary>依舊付款流程使用大寫 C 首字母判定信用卡交易類型，不做 trim 或文化轉換。</summary>
    private static bool StartsWithCreditPrefix(string? value) =>
        value is not null && value.Length > 0 && value[0] == 'C';
}

/// <summary>單次教會授權收費查詢結果及其原始列版本；不允許跨操作共用或快取。</summary>
public sealed class FeeAllotRecord
{
    /// <summary>建立只由同組件讀取邊界建立的資料列快照。</summary>
    /// <param name="snapshot">已複製且不含可變 SDK 欄位的比對純值。</param>
    /// <param name="feeEntity">由本次 Dataverse Retrieve 得到的原始 fee 列，僅作同次條件更新。</param>
    /// <param name="timeZoneId">由已核對 parentcustomerid 的教會 account 讀取的 IANA 時區。</param>
    internal FeeAllotRecord(FeeAllotSnapshot snapshot, Microsoft.Xrm.Sdk.Entity feeEntity, string timeZoneId)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        OriginalFeeEntity = feeEntity ?? throw new ArgumentNullException(nameof(feeEntity));
        TimeZoneId = timeZoneId ?? throw new ArgumentNullException(nameof(timeZoneId));
    }

    /// <summary>取得本次操作自己的 fee 比對純值。</summary>
    public FeeAllotSnapshot Snapshot { get; }

    /// <summary>取得依該會友所屬教會讀取的時區；空欄位已使用核定 Taipei fallback。</summary>
    public string TimeZoneId { get; }

    /// <summary>僅提供 FeeAllotRecords 同組件使用的原始 row version，不能發布給外部呼叫端。</summary>
    internal Microsoft.Xrm.Sdk.Entity OriginalFeeEntity { get; }
}
