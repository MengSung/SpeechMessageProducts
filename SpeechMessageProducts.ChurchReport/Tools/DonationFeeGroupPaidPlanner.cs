/*
 * 檔案整體說明
 *
 * 目的與定位：計算群組逐筆入帳、校正與冪等結果。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationFeePaidSnapshot、DonationFeePaidDecision、DonationFeeGroupPaidPlan、DonationFeeGroupPaidPlanner。
 * - primary（Param1）是否由本次回呼第一次入帳；只有這時才發送付款成功通知，避免重複通知。
 * 主要操作入口包含 DonationFeePaidSnapshot、DonationFeePaidDecision、DonationFeeGroupPaidPlan、Plan。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs 追查目前工作樹中的合作／使用位置。
 */
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
