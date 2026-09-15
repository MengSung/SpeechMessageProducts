// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Dedication/DonationMultiCategoryTests.cs
// 檔案責任：驗證多類別奉獻的純邏輯契約，不連線 CRM、金流、Session 或外部通知服務。
// 測試隔離：每個測試建立自己的表單與 DTO，不使用 static 可變資料，避免測試間或會友資料污染。
// 生命週期：僅配置受測純物件；沒有 stream、timer、subscription、背景工作或連線需要釋放。
// 編碼要求：本檔案維持 UTF-8 without BOM 與 CRLF。
// ============================================================================
using System;
using System.Collections.Generic;
using ChurchReport.Models;
using ChurchReport.Services;
using ChurchReport.Tools;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Dedication;

/// <summary>
/// 鎖定多類別流程跨層共用的純函式行為：Lines 正規化、送出限制、
/// 金流／ATM 文字與 callback 逐張付款決策。CRM 寫入與金流 sandbox 另由整合驗證負責。
/// </summary>
public sealed class DonationMultiCategoryTests
{
    [Fact]
    public void Normalize_UsesTrimmedLines_AndDropsBlankRows()
    {
        var model = new DonationPaymentFormModel
        {
            Lines = new List<DonationLineItemInput>
            {
                new() { Category = " 感恩奉獻 ", Amount = 500, Others = " 為家人感恩 " },
                new() { Category = "", Amount = 0 }
            }
        };

        var lines = DonationLineItemNormalizer.Normalize(model);

        lines.Should().ContainSingle();
        lines[0].Category.Should().Be("感恩奉獻");
        lines[0].Others.Should().Be("為家人感恩");
    }

    [Fact]
    public void Normalize_UsesLegacyFields_WhenLinesAreEmpty()
    {
        var lines = DonationLineItemNormalizer.Normalize(new DonationPaymentFormModel
        {
            Category = "十一奉獻", Amount = 3000, Others = ""
        });

        lines.Should().ContainSingle().Which.Amount.Should().Be(3000);
        lines[0].Category.Should().Be("十一奉獻");
    }

    [Fact]
    public void ValidateDonationForm_AllowsSevenLines_RejectsEight_AndRecurringMultipleLines()
    {
        // 上限 7 來自永豐 Param1 X(255)：7 個 32 碼收費單 Id 加逗號剛好放得下（見 DonationFeeIdList）。
        DonationLineItemNormalizer.MaxLines.Should().Be(7);

        var seven = new DonationPaymentFormModel { PayWay = "信用卡", Lines = new List<DonationLineItemInput>() };
        for (var index = 0; index < 7; index++) seven.Lines.Add(new DonationLineItemInput { Category = "類別" + index, Amount = 1 });
        DonationPaymentSubmissionService.ValidateDonationForm(seven).Should().BeEmpty();

        var eight = new DonationPaymentFormModel { PayWay = "信用卡", Lines = new List<DonationLineItemInput>() };
        for (var index = 0; index < 8; index++) eight.Lines.Add(new DonationLineItemInput { Category = "類別" + index, Amount = 1 });
        DonationPaymentSubmissionService.ValidateDonationForm(eight).Should().Be("一次最多只能奉獻 7 個類別");

        var recurring = new DonationPaymentFormModel
        {
            PayWay = "信用卡定期定額(每個月)",
            Lines = new List<DonationLineItemInput> { new() { Category = "十一奉獻", Amount = 1 }, new() { Category = "感恩奉獻", Amount = 1 } }
        };
        DonationPaymentSubmissionService.ValidateDonationForm(recurring).Should().Be("定期定額一次只能設定一個類別");
    }

    [Fact]
    public void GroupText_SumsLines_AndPreservesAtmLabels()
    {
        IReadOnlyList<DonationLineItemInput> lines = new[]
        {
            new DonationLineItemInput { Category = "十一奉獻", Amount = 3000 },
            new DonationLineItemInput { Category = "感恩奉獻", Amount = 500 }
        };

        DonationFeeGroupText.Total(lines).Should().Be(3500);
        DonationFeeGroupText.CategorySummary(lines).Should().Be("十一奉獻 3,000元、感恩奉獻 500元");
        var atm = DonationFeeGroupText.AtmInfo("測試會友", lines, "123456", "2026/09/24");
        atm.LineMessage.Should().Contain("姓名 : 測試會友").And.Contain("名稱 : 十一奉獻 3,000元、感恩奉獻 500元").And.Contain("金額 : 3500元").And.Contain("帳號     : 123456");
    }

    [Fact]
    public void PaidPlanner_MarksEveryNewGroupFee_WithItsOwnAmount_AndIsIdempotent()
    {
        var fee1 = Guid.NewGuid();
        var fee2 = Guid.NewGuid();
        var plan = DonationFeeGroupPaidPlanner.Plan(new[]
        {
            new DonationFeePaidSnapshot(fee1, 3000, 0, DonationFeeGroupPaidPlanner.PayStatusNew, ""),
            new DonationFeePaidSnapshot(fee2, 500, 0, DonationFeeGroupPaidPlanner.PayStatusNew, "")
        }, "C20260914001", 3500);

        plan.Decisions.Should().OnlyContain(x => x.MarkPaid);
        plan.Decisions.Should().Contain(x => x.FeeId == fee1 && x.ReallyPaid == 3000);
        plan.Decisions.Should().Contain(x => x.FeeId == fee2 && x.ReallyPaid == 500);
        plan.AmountMismatch.Should().BeFalse();

        var repeat = DonationFeeGroupPaidPlanner.Plan(new[]
        {
            new DonationFeePaidSnapshot(fee1, 3000, 3000, 100000001, "C20260914001"),
            new DonationFeePaidSnapshot(fee2, 500, 500, 100000001, "C20260914001")
        }, "C20260914001", 3500);
        repeat.AnyToMark.Should().BeFalse();
    }
}
