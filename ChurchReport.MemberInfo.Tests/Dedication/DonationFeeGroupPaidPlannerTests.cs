/*
 * 檔案整體說明
 *
 * 目的與定位：驗證多類別付款按各收費單自己的應付額入帳、重跑不再異動及人工調整不被覆寫，防止把群組總額寫到單一收費單。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationFeeGroupPaidPlannerTests。
 * 測試案例入口包含 NewGroup_EveryFeeIsPaidWithItsOwnAmount_ForAnyCategoryCount、
 * Paid1700For800And900_SplitsIntoEachFee_InsteadOfWritingTotalIntoOne、
 * OldHandlerWroteTotalIntoPrimary_IsCorrected_SiblingIsPaid_AndRerunChangesNothing、
 * ManualAdjustment_IsNeverOverwritten、AmountMismatch_StillPaysEachFeeItsOwnAmount_AndNeverCorrects、
 * SingleFee_KeepsLegacyRule_AndIsIdempotent。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Linq;
using ChurchReport.Tools;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Dedication;

public sealed class DonationFeeGroupPaidPlannerTests
{
    private const string OrderNo = "C20260914050812345";
    private const int PayStatusCardPaid = 100000001;

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void NewGroup_EveryFeeIsPaidWithItsOwnAmount_ForAnyCategoryCount(int count)
    {
        var fees = Enumerable.Range(0, count)
            .Select(index => new DonationFeePaidSnapshot(Guid.NewGuid(), (index + 1) * 100, 0, DonationFeeGroupPaidPlanner.PayStatusNew, ""))
            .ToList();
        var total = fees.Sum(fee => fee.ShouldPay);

        var plan = DonationFeeGroupPaidPlanner.Plan(fees, OrderNo, total);

        plan.AmountMismatch.Should().BeFalse();
        plan.PrimaryNewlyPaid.Should().BeTrue();
        plan.Decisions.Should().HaveCount(count);
        for (var index = 0; index < count; index++)
        {
            plan.Decisions[index].FeeId.Should().Be(fees[index].FeeId);
            plan.Decisions[index].MarkPaid.Should().BeTrue();
            plan.Decisions[index].CorrectAmount.Should().BeFalse();
            plan.Decisions[index].ReallyPaid.Should().Be(fees[index].ShouldPay, "實收金額要等於本單應收，未繳金額才會是 0");
            plan.Decisions[index].BigNumberAmount.Should().Be(fees[index].ShouldPay);
        }

        plan.Decisions.Sum(decision => decision.ReallyPaid).Should().Be(total);
    }

    [Fact]
    public void Paid1700For800And900_SplitsIntoEachFee_InsteadOfWritingTotalIntoOne()
    {
        var fee800 = new DonationFeePaidSnapshot(Guid.NewGuid(), 800, 0, DonationFeeGroupPaidPlanner.PayStatusNew, "");
        var fee900 = new DonationFeePaidSnapshot(Guid.NewGuid(), 900, 0, DonationFeeGroupPaidPlanner.PayStatusNew, "");

        var plan = DonationFeeGroupPaidPlanner.Plan(new[] { fee800, fee900 }, OrderNo, 1700);

        plan.Decisions.Select(decision => decision.ReallyPaid).Should().Equal(800, 900);
        plan.Decisions.Should().OnlyContain(decision => decision.MarkPaid);
    }

    [Fact]
    public void OldHandlerWroteTotalIntoPrimary_IsCorrected_SiblingIsPaid_AndRerunChangesNothing()
    {
        // 2026-09-14 真實測試資料：800 那張被舊程式寫成實收 1,700（未繳 -900），900 那張仍未付款。
        var primary = new DonationFeePaidSnapshot(Guid.NewGuid(), 800, 1700, PayStatusCardPaid, "ReturnUrl => 信用卡訂單編號= " + OrderNo + "，金額:1700");
        var sibling = new DonationFeePaidSnapshot(Guid.NewGuid(), 900, 0, DonationFeeGroupPaidPlanner.PayStatusNew, "");

        var plan = DonationFeeGroupPaidPlanner.Plan(new[] { primary, sibling }, OrderNo, 1700);

        plan.Decisions[0].MarkPaid.Should().BeFalse();
        plan.Decisions[0].CorrectAmount.Should().BeTrue();
        plan.Decisions[0].ReallyPaid.Should().Be(800);
        plan.Decisions[1].MarkPaid.Should().BeTrue();
        plan.Decisions[1].ReallyPaid.Should().Be(900);
        plan.AnyChange.Should().BeTrue();
        plan.PrimaryNewlyPaid.Should().BeFalse("primary 先前已發過成功通知，補齊時不可重發 LINE");

        var rerun = DonationFeeGroupPaidPlanner.Plan(new[]
        {
            primary with { ExistingReallyPaid = 800 },
            sibling with { ExistingReallyPaid = 900, PayStatus = PayStatusCardPaid, PaymentRecords = "信用卡訂單編號= " + OrderNo }
        }, OrderNo, 1700);

        rerun.AnyChange.Should().BeFalse();
    }

    [Fact]
    public void ManualAdjustment_IsNeverOverwritten()
    {
        var adjusted = new DonationFeePaidSnapshot(Guid.NewGuid(), 800, 1000, PayStatusCardPaid, OrderNo);
        var sibling = new DonationFeePaidSnapshot(Guid.NewGuid(), 900, 900, PayStatusCardPaid, OrderNo);

        var plan = DonationFeeGroupPaidPlanner.Plan(new[] { adjusted, sibling }, OrderNo, 1700);

        plan.AnyChange.Should().BeFalse();
    }

    [Fact]
    public void AmountMismatch_StillPaysEachFeeItsOwnAmount_AndNeverCorrects()
    {
        var fee800 = new DonationFeePaidSnapshot(Guid.NewGuid(), 800, 1500, PayStatusCardPaid, OrderNo);
        var fee900 = new DonationFeePaidSnapshot(Guid.NewGuid(), 900, 0, DonationFeeGroupPaidPlanner.PayStatusNew, "");

        var plan = DonationFeeGroupPaidPlanner.Plan(new[] { fee800, fee900 }, OrderNo, 1500);

        plan.AmountMismatch.Should().BeTrue();
        plan.ExpectedTotal.Should().Be(1700);
        plan.Decisions[0].CorrectAmount.Should().BeFalse("金額對不上時不自動校正，交由人工稽核");
        plan.Decisions[1].MarkPaid.Should().BeTrue();
        plan.Decisions[1].ReallyPaid.Should().Be(900);
    }

    [Fact]
    public void SingleFee_KeepsLegacyRule_AndIsIdempotent()
    {
        var single = new DonationFeePaidSnapshot(Guid.NewGuid(), 1000, 0, DonationFeeGroupPaidPlanner.PayStatusNew, "");

        var plan = DonationFeeGroupPaidPlanner.Plan(new[] { single }, OrderNo, 1000);

        plan.Decisions.Should().ContainSingle();
        plan.Decisions[0].MarkPaid.Should().BeTrue();
        plan.Decisions[0].ReallyPaid.Should().Be(1000);
        plan.PrimaryNewlyPaid.Should().BeTrue();
        plan.AmountMismatch.Should().BeFalse();

        var rerun = DonationFeeGroupPaidPlanner.Plan(
            new[] { single with { ExistingReallyPaid = 1000, PayStatus = PayStatusCardPaid, PaymentRecords = OrderNo } },
            OrderNo,
            1000);

        rerun.AnyChange.Should().BeFalse();
    }
}
