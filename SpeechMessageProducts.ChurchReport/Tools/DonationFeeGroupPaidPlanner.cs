// ============================================================================
// AI-繁體中文檔案註解
// 檔案責任：以純函式決定 callback 對「同一次付款」群組內每張收費單的處理（任意數量的類別）。
// 規則：
//   - 單一收費單：沿用舊流程（狀態仍為新建立且付款紀錄未含訂單編號才入帳，實收＝原實收＋本次付款金額）。
//   - 多張收費單：每張只收自己的應收金額；尚未入帳的標記已付款。
//   - 校正：若舊版單張回呼已把「整筆付款」寫進某一張（實收＝付款總額、應收較小），且群組應收合計等於付款總額，
//           就把該張實收校正為自己的應收金額。條件刻意嚴格，避免覆蓋人工調整。
// 隔離與生命週期：只處理不可變快照，不讀寫 CRM、Session、static 可變快取、timer、task 或連線。
// 編碼要求：UTF-8 without BOM、CRLF。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChurchReport.Tools
{
    /// <summary>回呼使用的收費單狀態快照；純資料型別避免攜帶 CRM 可變狀態或會友資料。</summary>
    public sealed record DonationFeePaidSnapshot(
        Guid FeeId,
        int ShouldPay,
        int ExistingReallyPaid,
        int PayStatus,
        string PaymentRecords);

    /// <summary>
    /// 對一張收費單的處理決定。
    /// MarkPaid：本次標記已付款。CorrectAmount：已付款但實收被寫成整筆付款，本次校正為 ReallyPaid。
    /// </summary>
    public sealed record DonationFeePaidDecision(
        Guid FeeId,
        bool MarkPaid,
        int ReallyPaid,
        int BigNumberAmount,
        bool CorrectAmount = false);

    /// <summary>整個群組的處理計畫；Decisions 順序與傳入的收費單順序相同（primary 在第一個）。</summary>
    public sealed record DonationFeeGroupPaidPlan(
        IReadOnlyList<DonationFeePaidDecision> Decisions,
        int ExpectedTotal,
        int PaidAmount,
        bool AmountMismatch)
    {
        public bool AnyToMark => Decisions.Any(decision => decision.MarkPaid);

        public bool AnyToCorrect => Decisions.Any(decision => decision.CorrectAmount);

        /// <summary>本次回呼是否需要寫入任何收費單。</summary>
        public bool AnyChange => AnyToMark || AnyToCorrect;

        /// <summary>primary（Param1）是否由本次回呼第一次入帳；只有這時才發送付款成功通知，避免重複通知。</summary>
        public bool PrimaryNewlyPaid => Decisions.Count > 0 && Decisions[0].MarkPaid;
    }

    /// <summary>計算群組逐筆入帳、校正與冪等結果。</summary>
    public static class DonationFeeGroupPaidPlanner
    {
        public const int PayStatusNew = 100000000;

        public static DonationFeeGroupPaidPlan Plan(
            IReadOnlyList<DonationFeePaidSnapshot> fees,
            string orderNo,
            int paidAmount)
        {
            if (fees == null || fees.Count == 0)
            {
                return new DonationFeeGroupPaidPlan(Array.Empty<DonationFeePaidDecision>(), 0, paidAmount, false);
            }

            var isSingle = fees.Count == 1;
            var expectedTotal = fees.Sum(fee => fee.ShouldPay);
            var amountMismatch = !isSingle && expectedTotal != paidAmount;
            var decisions = new List<DonationFeePaidDecision>(fees.Count);

            foreach (var fee in fees)
            {
                // 狀態與付款紀錄雙重條件：RETURN_URL 與 BACKEND_URL 的重複回呼不會重複入帳。
                var recorded = !string.IsNullOrEmpty(orderNo)
                    && (fee.PaymentRecords ?? string.Empty).Contains(orderNo, StringComparison.Ordinal);
                var alreadyPaid = fee.PayStatus != PayStatusNew || recorded;

                if (isSingle)
                {
                    decisions.Add(new DonationFeePaidDecision(
                        fee.FeeId,
                        !alreadyPaid,
                        fee.ExistingReallyPaid + paidAmount,
                        paidAmount));
                    continue;
                }

                // 多類別：嚴禁把群組總額寫進任何一張收費單。
                var correctAmount = alreadyPaid
                    && !amountMismatch
                    && fee.ExistingReallyPaid == paidAmount
                    && fee.ShouldPay < paidAmount
                    && fee.ExistingReallyPaid != fee.ShouldPay;

                decisions.Add(new DonationFeePaidDecision(
                    fee.FeeId,
                    !alreadyPaid,
                    fee.ShouldPay,
                    fee.ShouldPay,
                    correctAmount));
            }

            return new DonationFeeGroupPaidPlan(decisions, expectedTotal, paidAmount, amountMismatch);
        }
    }
}
