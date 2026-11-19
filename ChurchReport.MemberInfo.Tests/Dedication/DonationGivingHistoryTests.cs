/*
 * 檔案整體說明
 *
 * 目的與定位：驗證奉獻歷史的常用類別排名、同次提交分組、最近一次奉獻與預設補位，並保護定期奉獻期數推導。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationGivingHistoryTests、DonationRecurringPeriodsTests。
 * 測試案例入口包含 Fee、RankPinned_OrdersByFrequency_ThenFillsWithDefaults、RankPinned_BreaksTiesByMostRecentGift、
 * RankPinned_IgnoresCategoriesNoLongerOffered_AndUsesDefaultsWithoutHistory、
 * RankPinned_FillsFromOfferedCategories_WhenDefaultsAreMissing、
 * FindLastGift_GroupsTheSameSubmission_AndSkipsOlderGifts、FindLastGift_ExcludesRecurringCharges_AndOtherPayWays、
 * FindLastGift_MergesTheSameCategoryWithinOneSubmission；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using ChurchReport.Models;
using ChurchReport.Services;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Dedication;

public class DonationGivingHistoryTests
{
    private static readonly IReadOnlyList<DonationOfferingCategoryOption> Available = new[]
    {
        Option("主日奉獻", "10000000-0000-0000-0000-000000000001"),
        Option("十一奉獻", "10000000-0000-0000-0000-000000000002"),
        Option("感恩奉獻", "10000000-0000-0000-0000-000000000003"),
        Option("建堂奉獻", "10000000-0000-0000-0000-000000000004"),
        Option("宣教奉獻", "10000000-0000-0000-0000-000000000005"),
        Option("愛心奉獻", "10000000-0000-0000-0000-000000000006"),
        Option("特別奉獻", "10000000-0000-0000-0000-000000000007")
    };

    private static readonly DateTime Base = new(2026, 9, 1, 10, 0, 0);

    private static DonationOfferingCategoryOption Option(string name, string id) => new(id, name, true);

    private static DedicationFee Fee(string category, int amount, DateTime created, string payWay = "信用卡", string paidPeriod = "", string others = "")
    {
        var categoryId = Available.FirstOrDefault(option => option.Name == category)?.Id;
        return new DedicationFee
        {
            Category = category,
            CategoryId = Guid.TryParse(categoryId, out var id) ? id : null,
            Amount = amount,
            DedicationDate = created,
            PayDate = created,
            PayWay = payWay,
            PaidPeriod = paidPeriod,
            Others = others
        };
    }

    private static string Id(string name) => Available.Single(option => option.Name == name).Id;

    [Fact]
    public void RankPinned_OrdersByFrequency_ThenFillsWithDefaults()
    {
        var fees = new[]
        {
            Fee("愛心奉獻", 100, Base), Fee("愛心奉獻", 100, Base.AddDays(-10)), Fee("愛心奉獻", 100, Base.AddDays(-20)),
            Fee("十一奉獻", 3000, Base.AddDays(-5)), Fee("十一奉獻", 3000, Base.AddDays(-35))
        };

        DonationGivingHistory.RankPinnedCategories(fees, Available)
            .Should().Equal(Id("愛心奉獻"), Id("十一奉獻"), Id("主日奉獻"), Id("感恩奉獻"));
    }

    [Fact]
    public void RankPinned_BreaksTiesByMostRecentGift()
    {
        var fees = new[] { Fee("主日奉獻", 100, Base.AddDays(-60)), Fee("愛心奉獻", 100, Base.AddDays(-1)) };

        DonationGivingHistory.RankPinnedCategories(fees, Available)
            .Should().Equal(Id("愛心奉獻"), Id("主日奉獻"), Id("十一奉獻"), Id("感恩奉獻"));
    }

    [Fact]
    public void RankPinned_IgnoresCategoriesNoLongerOffered_AndUsesDefaultsWithoutHistory()
    {
        var fees = new[] { Fee("代收代轉", 500, Base), Fee("代收代轉", 500, Base.AddDays(-1)) };

        DonationGivingHistory.RankPinnedCategories(fees, Available)
            .Should().Equal(Id("主日奉獻"), Id("十一奉獻"), Id("感恩奉獻"), Id("建堂奉獻"));
        DonationGivingHistory.RankPinnedCategories(null!, Available)
            .Should().Equal(Id("主日奉獻"), Id("十一奉獻"), Id("感恩奉獻"), Id("建堂奉獻"));
    }

    [Fact]
    public void RankPinned_FillsFromOfferedCategories_WhenDefaultsAreMissing()
    {
        var offered = new[] { Available[0], Available[5] };
        DonationGivingHistory.RankPinnedCategories(null!, offered)
            .Should().Equal(Id("主日奉獻"), Id("愛心奉獻"));
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
        var categoryId = Guid.Parse(Id("十一奉獻"));
        var fees = new[]
        {
            new DedicationFee { CategoryId = categoryId, Category = "十一奉獻", Amount = 1000, DedicationDate = Base, PayDate = Base, PayWay = "信用卡" },
            new DedicationFee { CategoryId = categoryId, Category = "十一奉獻", Amount = 2000, DedicationDate = Base.AddMinutes(1), PayDate = Base.AddMinutes(1), PayWay = "信用卡" }
        };

        var gift = DonationGivingHistory.FindLastGift(fees, Available);

        gift!.Lines.Should().ContainSingle().Which.Amount.Should().Be(3000);
    }

    [Fact]
    public void FindLastGift_KeepsSameNameCategoriesWithDifferentDatabaseIdsSeparate()
    {
        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var fees = new[]
        {
            new DedicationFee { CategoryId = firstId, Category = "重名類別", Amount = 100, DedicationDate = Base,
                PayDate = Base, PayWay = "信用卡" },
            new DedicationFee { CategoryId = secondId, Category = "重名類別", Amount = 200, DedicationDate = Base.AddMinutes(1),
                PayDate = Base.AddMinutes(1), PayWay = "信用卡" }
        };

        var available = new[]
        {
            Option("重名類別", firstId.ToString("D")),
            Option("重名類別", secondId.ToString("D"))
        };
        var gift = DonationGivingHistory.FindLastGift(fees, available);

        gift.Should().NotBeNull();
        gift!.Lines.Should().HaveCount(2);
        gift.Lines.Select(line => line.CategoryId).Should().Equal(firstId, secondId);
        gift.Lines.Select(line => line.Amount).Should().Equal(100, 200);
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
