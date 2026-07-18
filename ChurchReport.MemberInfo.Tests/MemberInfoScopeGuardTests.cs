// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class MemberInfoScopeGuardTests
// 主要成員：Church_AllowsAnyNonEmpty、Shepherd_AllowsInList、Shepherd_DeniesNotInList、DeniesMissingId、DeniesNoAccess
// 引用命名空間：System、System.Collections.Generic、Xunit、FluentAssertions、ChurchReport.Services.MemberInfo
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using ChurchReport.Services.MemberInfo;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoScopeGuardTests
{
    private static readonly HashSet<string> Shepherd = new(StringComparer.OrdinalIgnoreCase)
    {
        "11111111-1111-1111-1111-111111111111",
        "22222222-2222-2222-2222-222222222222",
    };

    [Fact]
    public void Church_AllowsAnyNonEmpty() =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.Church, Shepherd,
            "99999999-9999-9999-9999-999999999999").Should().BeTrue();

    [Fact]
    public void Shepherd_AllowsInList() =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.ShepherdList, Shepherd,
            "22222222-2222-2222-2222-222222222222").Should().BeTrue();

    [Fact]
    public void Shepherd_DeniesNotInList() =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.ShepherdList, Shepherd,
            "99999999-9999-9999-9999-999999999999").Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeniesMissingId(string? requested) =>
        MemberInfoScopeGuard.IsContactAllowed(MemberInfoAccess.Church, Shepherd, requested!).Should().BeFalse();

    [Fact]
    public void DeniesNoAccess() =>
        MemberInfoScopeGuard.IsContactAllowed(null, Shepherd,
            "11111111-1111-1111-1111-111111111111").Should().BeFalse();
}
