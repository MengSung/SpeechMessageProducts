/*
檔案整體說明

目的與定位：驗證 ChurchReport 會籍角色 API、空覆寫 provider 與兩個群組宮格 lookup 的 P8-02 相容契約。
本檔包含 08A 指定的三個測試及一個 provider scope 補充測試，使用產品原始碼與 Roslyn 語法樹
作離線驗證，不啟動網站或 D365。

主要功能與流程：確認 API 保留 `data` 的 D365 原始標籤、另提供 value/text/code 投影；保存值
仍以原始標籤為 value，而呈現名稱只供顯示。兩個 lookup 必須使用 valueExpr=`value`、
displayExpr=`text`，失敗備援由伺服器 RoleCatalog 輸出。硬編碼檢查只看可執行字串 token，
避免註解或測試說明造成誤判。

使用邊界與維護：程式碼分析不代表實機 API 或資料庫驗收。角色代碼與顯示文字分離；
未知標籤不得因顯示相似而轉成角色。測試不持有 Session、連線、快取或背景工作。

失敗處理與驗證：每項契約以來源語法或字串 token 的固定斷言判定，缺少相容欄位、錯誤 lookup
欄位或角色字面值回歸時立即失敗。Roslyn syntax tree 僅由單一測試建立並在方法結束釋放託管參考。
*/
using System;
using System.IO;
using System.Linq;
using ChurchReport.MemberInfo.Tests;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using SpeechMessage.Localization;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Personal;

/// <summary>鎖定名單 API 的標籤存檔相容性及 lookup 顯示分離；原始碼檢查完全離線。</summary>
public sealed class MembershipStatusApiTests
{
    private static readonly string[] RoleNames =
    {
        "\u7267\u5e2b\u5e2b\u6bcd", "\u5340\u7267", "\u5c0f\u5340\u9577", "\u5c0f\u7d44\u9577",
        "\u526f\u5c0f\u7d44\u9577", "\u6838\u5fc3\u540c\u5de5", "\u5c0f\u7d44\u7d44\u54e1", "\u672a\u5165\u7d44",
        "\u65b0\u670b\u53cb", "\u5916\u6559\u6703", "\u7d50\u6848"
    };

    /// <summary>讀取 API action 原始碼並斷言同時回傳相容 data 與穩定 value/text/code 欄位。</summary>
    [Fact]
    public void MembershipStatusApi_ReturnsItemsWithValueTextCode()
    {
        var source = ReadProductSource("Controllers", "PersonalController.cs");
        var action = ExtractMethod(source, "GetMembershipStatusList");

        action.Should().Contain("data");
        action.Should().Contain("items");
        action.Should().Contain("value");
        action.Should().Contain("text");
        action.Should().Contain("code");
        action.Should().Contain("RoleCatalog");
    }

    /// <summary>故障注入自訂顯示名稱，檢查 lookup 提交仍使用 D365 value 欄而非 text 或選項整數。</summary>
    [Fact]
    public void MembershipStatusApi_SaveValueStaysD365Label()
    {
        var controller = ReadProductSource("Controllers", "PersonalController.cs");
        var view = ReadProductSource("Views", "Home", "_GeneralGroupGrids.cshtml");
        var action = ExtractMethod(controller, "GetMembershipStatusList");

        action.Should().Contain("value");
        view.Should().Contain(".ValueExpr(\"value\")");
        view.Should().Contain(".DisplayExpr(\"text\")");
        view.Should().NotContain(".ValueExpr(\"text\")");
    }

    /// <summary>以 Roslyn token 故障注入掃描兩個指定產品檔，確保角色中文只由伺服器 catalog 提供。</summary>
    [Fact]
    public void MembershipStatusList_NoHardCodedRoleNames()
    {
        var controller = ReadProductSource("Controllers", "PersonalController.cs");
        var view = ReadProductSource("Views", "Home", "_GeneralGroupGrids.cshtml");

        GetStringTokens(controller).Should().NotContain(RoleNames);
        GetStringTokens(view).Should().NotContain(RoleNames);
    }

    /// <summary>注入 metadata、scope 與呈現失敗契約，確認每個終止 catch 都上報原始例外。</summary>
    [Fact]
    public void MembershipStatusApi_ReportsCaughtFailures()
    {
        var source = ReadProductSource("Controllers", "PersonalController.cs");
        var action = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(item => item.Identifier.ValueText == "GetMembershipStatusList");
        var catches = action.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.CatchClauseSyntax>()
            .ToArray();

        catches.Should().HaveCount(3);
        foreach (var catchClause in catches)
        {
            var exceptionName = catchClause.Declaration?.Identifier.ValueText;
            exceptionName.Should().NotBeNullOrWhiteSpace();
            var catchBody = catchClause.Block.ToFullString();
            catchBody.Should().Contain("ChurchReportLineAdminNotificationService.ReportException");
            catchBody.Should().Contain("nameof(PersonalController) + \".\" + nameof(GetMembershipStatusList)");
            catchBody.Should().Contain(exceptionName);
        }
    }

    /// <summary>以不同教會範圍注入快照請求，斷言空 provider 回傳本次 scope 的空值且不保留覆寫。</summary>
    [Fact]
    public void MembershipStatusApi_EmptyOverrideProviderReturnsScopedEmptySnapshot()
    {
        var providerType = typeof(Startup).Assembly.GetType(
            "ChurchReport.ChurchReportEmptyTextOverrideSnapshotProvider");
        providerType.Should().NotBeNull();

        var provider = (IChurchTextOverrideSnapshotProvider)Activator.CreateInstance(providerType!, nonPublic: true)!;
        var context = new TextContext(Guid.NewGuid(), "zh-TW", "zh-TW", "SyntheticOrg", "ChurchReport");
        var snapshot = provider.GetSnapshot(context);

        snapshot.Context.ChurchId.Should().Be(context.ChurchId);
        snapshot.Context.Organization.Should().Be(context.Organization);
        snapshot.Context.Product.Should().Be(context.Product);
        snapshot.TryGetText("zh-TW", new TextKey("Role.Member"), out _).Should().BeFalse();
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var method = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .SingleOrDefault(item => item.Identifier.ValueText == methodName);
        return method?.ToFullString() ?? string.Empty;
    }

    private static string[] GetStringTokens(string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantTokens()
            .Where(token => token.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken)
            .Select(token => token.ValueText).ToArray();

    private static string ReadProductSource(params string[] relativePath)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root, "SpeechMessageProducts.ChurchReport" }.Concat(relativePath).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("找不到工作樹方案根目錄。");
    }
}
