/*
 * 檔案整體說明
 *
 * 目的與定位：保護單筆GUID舊格式、多筆收費單順序往返及永豐欄位長度上限，並確認空值、重複及過量ID整體拒絕。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationFeeIdListTests。
 * 測試案例入口包含 Encode_SingleFee_KeepsLegacyGuidFormat、Encode_MultipleFees_RoundTripsEveryIdInOrder_WithinSinopacLimit、
 * MaxIds_IsSeven_AndDonationLineCapUsesIt、Encode_RejectsEmptyDuplicateBlankOrTooManyIds、
 * Parse_AcceptsStandardFormats_AndRejectsWholeValueWhenAnyPartIsInvalid、
 * SelectGroupMembers_KeepsPrimary_AndOnlySameContactSameOrderSiblings、
 * SelectGroupMembers_PrimaryWithoutContact_ProcessesOnlyPrimary。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Linq;
using ChurchReport.Services;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Dedication;

public sealed class DonationFeeIdListTests
{
    [Fact]
    public void Encode_SingleFee_KeepsLegacyGuidFormat()
    {
        var feeId = Guid.NewGuid();

        var param1 = DonationFeeIdList.Encode(new[] { feeId });

        param1.Should().Be(feeId.ToString());
        new Guid(param1).Should().Be(feeId, "改版前的回呼程式用 new Guid(Param1) 解析單一收費單");
        DonationFeeIdList.Parse(param1).Should().Equal(feeId);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Encode_MultipleFees_RoundTripsEveryIdInOrder_WithinSinopacLimit(int count)
    {
        var feeIds = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

        var param1 = DonationFeeIdList.Encode(feeIds);

        param1.Length.Should().BeLessOrEqualTo(DonationFeeIdList.ProviderParamMaxLength);
        param1.IndexOfAny(new[] { '\'', '"', '%' }).Should().Be(-1, "永豐規格 Param1 不可有單引號、雙引號、百分比");
        DonationFeeIdList.Parse(param1).Should().Equal(feeIds);
    }

    [Fact]
    public void MaxIds_IsSeven_AndDonationLineCapUsesIt()
    {
        DonationFeeIdList.MaxIds.Should().Be(7);
        DonationLineItemNormalizer.MaxLines.Should().Be(DonationFeeIdList.MaxIds);
    }

    [Fact]
    public void Encode_RejectsEmptyDuplicateBlankOrTooManyIds()
    {
        var feeId = Guid.NewGuid();
        var tooMany = Enumerable.Range(0, DonationFeeIdList.MaxIds + 1).Select(_ => Guid.NewGuid()).ToList();

        FluentActions.Invoking(() => DonationFeeIdList.Encode(Array.Empty<Guid>())).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => DonationFeeIdList.Encode(new[] { feeId, feeId })).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => DonationFeeIdList.Encode(new[] { Guid.Empty })).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => DonationFeeIdList.Encode(tooMany)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_AcceptsStandardFormats_AndRejectsWholeValueWhenAnyPartIsInvalid()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        DonationFeeIdList.Parse(" {" + first.ToString("D") + "} , " + second.ToString("N").ToUpperInvariant() + " ")
            .Should().Equal(first, second);
        DonationFeeIdList.Parse(first.ToString("N") + "," + first.ToString("N")).Should().BeEmpty();
        DonationFeeIdList.Parse(first.ToString("N") + ",," + second.ToString("N")).Should().BeEmpty();
        DonationFeeIdList.Parse(string.Join(",", Enumerable.Range(0, DonationFeeIdList.MaxIds + 1).Select(_ => Guid.NewGuid().ToString("N"))))
            .Should().BeEmpty();
        DonationFeeIdList.Parse(first.ToString("N") + ",not-a-guid").Should().BeEmpty();
        DonationFeeIdList.Parse(string.Empty).Should().BeEmpty();
        DonationFeeIdList.Parse(null!).Should().BeEmpty();
    }

    [Fact]
    public void SelectGroupMembers_KeepsPrimary_AndOnlySameContactSameOrderSiblings()
    {
        const string orderNo = "C20260914050812345";
        var contactId = Guid.NewGuid();
        var primary = new DonationFeeParamMember(Guid.NewGuid(), contactId, orderNo);
        var sibling = new DonationFeeParamMember(Guid.NewGuid(), contactId, orderNo);
        var orderNotYetWritten = new DonationFeeParamMember(Guid.NewGuid(), contactId, "");
        var otherContact = new DonationFeeParamMember(Guid.NewGuid(), Guid.NewGuid(), orderNo);
        var otherOrder = new DonationFeeParamMember(Guid.NewGuid(), contactId, "C20260101000000000");

        var accepted = DonationFeeIdList.SelectGroupMembers(
            new[] { primary, sibling, orderNotYetWritten, otherContact, otherOrder, sibling },
            orderNo);

        accepted.Should().Equal(primary.FeeId, sibling.FeeId, orderNotYetWritten.FeeId);
    }

    [Fact]
    public void SelectGroupMembers_PrimaryWithoutContact_ProcessesOnlyPrimary()
    {
        var primary = new DonationFeeParamMember(Guid.NewGuid(), Guid.Empty, "C1");
        var sibling = new DonationFeeParamMember(Guid.NewGuid(), Guid.Empty, "C1");

        DonationFeeIdList.SelectGroupMembers(new[] { primary, sibling }, "C1").Should().Equal(primary.FeeId);
    }
}
