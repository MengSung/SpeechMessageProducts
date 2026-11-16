/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P6-02 ChurchReport 只共用週報計算旗標與分類規則的來源接入契約，
 * 不把依登入者即時建立週報的產品流程換成整批產生服務。
 *
 * 主要功能與流程：Roslyn 讀取當前工作樹產品原始碼的字串字面值，拒絕六處固定旗標
 * 留下副本，並確認三種 QR 工具有共用 accessor 呼叫；另拒絕週報工具內三個舊分類的比較。
 * 註解及建置產物不屬正式程式，不能當成違規或用來製造假綠燈。
 *
 * 使用邊界與維護：只讀離線來源，不建立網站、CRM、LINE 或讀秘密；來源文字、語法樹
 * 與路徑僅由當次案例持有，檔案讀完即關閉，沒有快取、背景工作或需 drain 的 owner。
 * 這是來源委派證據，分類結果由 Core 雙目標測試與原黃金驗證，不代表頁面或資料庫實機。
 */
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.WeeklyReports;

/// <summary>以正式來源語法節點檢查共用規則接入，避免註解或檔名誤判為產品行為。</summary>
public sealed class WeeklyReportSharedRuleTests
{
    /// <summary>
    /// 原六處正式字面值必須移除，三個消費工具都要委派固定資源 accessor；
    /// 若遺漏任何一處會列出檔案而失敗，不改動原旗標文字或期待 D365 寫入值。
    /// </summary>
    [Fact]
    public void SavedFlag_Source_UsesSharedCalculationPrefix()
    {
        var product = ProductRoot();
        foreach (var file in Directory.EnumerateFiles(product, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Replace('\\', '/').Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                && !file.Replace('\\', '/').Contains("/bin/", StringComparison.OrdinalIgnoreCase)))
        {
            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            Assert.False(syntax.DescendantNodes().OfType<LiteralExpressionSyntax>().Any(literal =>
                literal.IsKind(SyntaxKind.StringLiteralExpression) && literal.Token.ValueText == "計算出席率"), file);
        }
        foreach (var name in new[] { "PersonalQrCodeUtility", "SmallGroupQrCodeUtility", "SundayQrCodeUtility" })
        {
            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(product, "Tools", name + ".cs"))).GetRoot();
            Assert.Contains(syntax.DescendantNodes().OfType<InvocationExpressionSyntax>(), call =>
                call.Expression is MemberAccessExpressionSyntax member &&
                member.Expression.ToString() == "WeeklyReportStatisticsRule" &&
                member.Name.Identifier.ValueText == "CalculationPrefix");
        }
    }

    /// <summary>
    /// 注入原三值比較的來源就是紅燈；改委派或移除無呼叫方法後才綠。
    /// 合法保留的分類顯示 switch 不屬比較，不因字面數字仍在檔案而錯誤要求刪產品文字。
    /// </summary>
    [Fact]
    public void WeeklyReportProcessor_Source_NoLocalClassificationRule()
    {
        var file = Path.Combine(ProductRoot(), "Tools/WeeklyReportProcessor.cs");
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
        Assert.DoesNotContain(syntax.DescendantNodes().OfType<IfStatementSyntax>(), branch =>
        {
            var numbers = branch.Condition.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>()
                .Where(literal => literal.IsKind(SyntaxKind.NumericLiteralExpression))
                .Select(literal => literal.Token.ValueText).ToHashSet(StringComparer.Ordinal);
            return numbers.Contains("100000004") && numbers.Contains("100000000") && numbers.Contains("100000007");
        });
    }

    /// <summary>只向上找當前測試的方案根，不讀父工作樹；找不到正式產品目錄時明確失敗。</summary>
    private static string ProductRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
                return Path.Combine(directory.FullName, "SpeechMessageProducts.ChurchReport");
        throw new DirectoryNotFoundException("Current solution was not found.");
    }
}
