// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Dedication/DonationFeeIdListTests.cs
// 檔案責任：鎖定永豐 Param1「收費單 Id 清單」契約：單張格式不變、多張可逐張還原、長度不超過 255、
//           類別上限與 Param1 容量一致，以及 callback 只入帳同會友、同訂單的收費單。
// 測試隔離：每個測試自行產生 Guid，不連線 CRM、金流或 Session，不使用 static 可變資料。
// 編碼要求：本檔案維持 UTF-8 without BOM 與 CRLF。
// ============================================================================
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
        DonationFeeIdList.Parse(first.ToString("N") + "," + first.ToString("N")).Should().Equal(first);
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
