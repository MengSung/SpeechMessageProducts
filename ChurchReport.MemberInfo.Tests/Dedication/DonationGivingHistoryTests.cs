// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Dedication/DonationGivingHistoryTests.cs
// 所屬區塊：ChurchReport 奉獻付款測試。
// 檔案責任：固定奉獻頁「常用類別」、「同上次奉獻」、「奉獻清單」與定期定額期數解析的規則。
// 主要型別：DonationGivingHistoryTests、DonationRecurringPeriodsTests
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Services;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Dedication;

public class DonationGivingHistoryTests
{
    private static readonly IReadOnlyList<string> Available = new[]
    {
        "主日奉獻", "十一奉獻", "感恩奉獻", "建堂奉獻", "宣教奉獻", "愛心奉獻", "特別奉獻"
    };

    private static readonly DateTime Base = new(2026, 9, 1, 10, 0, 0);

    private static DedicationFee Fee(string category, int amount, DateTime created, string payWay = "信用卡", string paidPeriod = "", string others = "")
    {
        return new DedicationFee
        {
            Category = category,
            Amount = amount,
            DedicationDate = created,
            PayDate = created,
            PayWay = payWay,
            PaidPeriod = paidPeriod,
            Others = others
        };
    }

    [Fact]
    public void RankPinned_OrdersByFrequency_ThenFillsWithDefaults()
    {
        var fees = new[]
        {
            Fee("愛心奉獻", 100, Base), Fee("愛心奉獻", 100, Base.AddDays(-10)), Fee("愛心奉獻", 100, Base.AddDays(-20)),
            Fee("十一奉獻", 3000, Base.AddDays(-5)), Fee("十一奉獻", 3000, Base.AddDays(-35))
        };

        DonationGivingHistory.RankPinnedCategories(fees, Available)
            .Should().Equal("愛心奉獻", "十一奉獻", "感恩奉獻", "建堂奉獻");
    }

    [Fact]
    public void RankPinned_BreaksTiesByMostRecentGift()
    {
        var fees = new[] { Fee("主日奉獻", 100, Base.AddDays(-60)), Fee("愛心奉獻", 100, Base.AddDays(-1)) };

        DonationGivingHistory.RankPinnedCategories(fees, Available)
            .Should().Equal("愛心奉獻", "主日奉獻", "十一奉獻", "感恩奉獻");
    }

    [Fact]
    public void RankPinned_IgnoresCategoriesNoLongerOffered_AndUsesDefaultsWithoutHistory()
    {
        var fees = new[] { Fee("代收代轉", 500, Base), Fee("代收代轉", 500, Base.AddDays(-1)) };

        DonationGivingHistory.RankPinnedCategories(fees, Available)
            .Should().Equal("十一奉獻", "感恩奉獻", "建堂奉獻", "宣教奉獻");
        DonationGivingHistory.RankPinnedCategories(null!, Available)
            .Should().Equal("十一奉獻", "感恩奉獻", "建堂奉獻", "宣教奉獻");
    }

    [Fact]
    public void RankPinned_FillsFromOfferedCategories_WhenDefaultsAreMissing()
    {
        DonationGivingHistory.RankPinnedCategories(null!, new[] { "主日奉獻", "愛心奉獻" })
            .Should().Equal("主日奉獻", "愛心奉獻");
    }

    [Fact]
    public void FindLastGift_GroupsTheSameSubmission_AndSkipsOlderGifts()
    {
        var fees = new[]
        {
            Fee("建堂奉獻", 10000, Base.AddDays(-30)),
            Fee("十一奉獻", 3000, Base),
            Fee("感恩奉獻", 500, Base.AddMinutes(1))
        };

        var gift = DonationGivingHistory.FindLastGift(fees, Available);

        gift.Should().NotBeNull();
        gift!.Lines.Select(line => line.Category).Should().Equal("十一奉獻", "感恩奉獻");
        gift.Lines.Select(line => line.Amount).Should().Equal(3000, 500);
        gift.Total.Should().Be(3500);
        gift.PayWay.Should().Be("信用卡");
    }

    [Fact]
    public void FindLastGift_ExcludesRecurringCharges_AndOtherPayWays()
    {
        var fees = new[]
        {
            Fee("十一奉獻", 1000, Base.AddMinutes(2), paidPeriod: "3"),
            Fee("宣教奉獻", 800, Base.AddMinutes(1), payWay: "ATM轉帳"),
            Fee("感恩奉獻", 500, Base.AddMinutes(-1))
        };

        var gift = DonationGivingHistory.FindLastGift(fees, Available);

        gift!.Lines.Should().ContainSingle().Which.Category.Should().Be("宣教奉獻");
        gift.PayWay.Should().Be("ATM轉帳");
    }

    [Fact]
    public void FindLastGift_MergesTheSameCategoryWithinOneSubmission()
    {
        var fees = new[] { Fee("十一奉獻", 1000, Base), Fee("十一奉獻", 2000, Base.AddMinutes(1)) };

        var gift = DonationGivingHistory.FindLastGift(fees, Available);

        gift!.Lines.Should().ContainSingle().Which.Amount.Should().Be(3000);
    }

    [Fact]
    public void FindLastGift_ReturnsNull_WhenNothingUsable()
    {
        DonationGivingHistory.FindLastGift(Array.Empty<DedicationFee>(), Available).Should().BeNull();
        DonationGivingHistory.FindLastGift(new[] { Fee("代收代轉", 500, Base) }, Available).Should().BeNull();
    }

    [Fact]
    public void ToHistoryRows_SortsNewestFirst()
    {
        var rows = DonationGivingHistory.ToHistoryRows(new[]
        {
            Fee("十一奉獻", 3000, Base.AddDays(-30)),
            Fee("感恩奉獻", 500, Base),
            Fee("建堂奉獻", 10000, Base.AddDays(-10), others: "新據點")
        });

        rows.Select(row => row.Category).Should().Equal("感恩奉獻", "建堂奉獻", "十一奉獻");
        rows[1].Others.Should().Be("新據點");
    }
}

public class DonationRecurringPeriodsTests
{
    [Theory]
    [InlineData("3個月", 3)]
    [InlineData("6個月", 6)]
    [InlineData("12個月", 12)]
    [InlineData("18個月", 18)]
    [InlineData("24個月", 24)]
    [InlineData("12", 12)]
    public void Parse_ReturnsPeriods_ForOfferedValues(string text, int expected)
    {
        DonationRecurringPeriods.Parse(text).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("5個月")]
    [InlineData("36個月")]
    public void Parse_ReturnsZero_ForUnsupportedValues(string? text)
    {
        DonationRecurringPeriods.Parse(text!).Should().Be(0);
    }
}
