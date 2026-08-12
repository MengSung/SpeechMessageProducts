using System.Text;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Infrastructure;

/// <summary>
/// 驗證 P7.4 離線部署閘門與 drain-first runbook 的固定契約。
/// 測試只讀取目前工作樹的腳本與 Markdown，不建立網路、CRM、SQL、feature flag
/// 或任何部署資源；因此不會保留跨測試、跨使用者或跨租戶的可變狀態。
/// </summary>
public sealed class LegacyGatewayNonOverlapRunbookContractTests
{
    /// <summary>
    /// 驗證腳本只接受六個伺服器選定的 bounded switch，並以去識別化固定分類回報。
    /// 若腳本不存在或缺少任一分類，測試應在 RED 階段失敗，避免把未知覆蓋率當成安全。
    /// </summary>
    [Fact]
    public void Validator_uses_only_bounded_categories_and_sanitized_outcomes()
    {
        var source = ReadRepositoryFile("docs", "scripts", "Test-ChurchReportLegacyGatewayNonOverlap.ps1");

        foreach (var category in new[]
        {
            "Durable",
            "CanonicalBinding",
            "LegacyDrained",
            "LegacyCoverageProven",
            "GatewayReady",
            "RollbackOwner"
        })
        {
            source.Should().Contain(category);
        }

        source.Should().Contain("switch");
        source.Should().NotContain("[switch] $Json");
        source.Should().Contain("legacy-coverage-unproven");
        source.Should().Contain("non-durable-topology");
        source.Should().Contain("sync-overrun");
        source.Should().NotContain("Invoke-WebRequest");
        source.Should().NotContain("Invoke-RestMethod");
        source.Should().NotContain("System.Data.SqlClient");
        source.Should().NotContain("Package01FeeReadsEnabled");
        source.Should().NotContain("WriteAllText");
        source.Should().NotContain("Set-Content");
        source.Should().NotContain("Out-File");
        source.Should().NotContain("New-Item");
        source.Should().NotContain("Remove-Item");
        source.Should().NotContain("CRM");
        source.Should().NotContain("https://");
        source.Should().NotContain("credential");
        source.Should().NotContain("token");
    }

    /// <summary>
    /// 驗證 runbook 強制 stop、drain、read-back、Gateway readiness、單次 smoke、rollback 的順序，
    /// 並明文禁止以 controller 狀態取代 Organization-level durable proof。
    /// </summary>
    [Fact]
    public void Runbook_preserves_drain_first_non_overlap_order_and_no_go_rules()
    {
        var runbook = ReadRepositoryFile("docs", "runbooks", "churchreport-package01-drain-first-non-overlap.md");

        var stop = runbook.IndexOf("## 1. Stop legacy intake", StringComparison.Ordinal);
        var drain = runbook.IndexOf("## 2. Drain", StringComparison.Ordinal);
        var readBack = runbook.IndexOf("## 3. Read-back", StringComparison.Ordinal);
        var readiness = runbook.IndexOf("## 4. Gateway readiness", StringComparison.Ordinal);
        var smoke = runbook.IndexOf("## 5. One smoke", StringComparison.Ordinal);
        var rollback = runbook.IndexOf("## 6. Rollback", StringComparison.Ordinal);

        stop.Should().BeGreaterOrEqualTo(0);
        drain.Should().BeGreaterThan(stop);
        readBack.Should().BeGreaterThan(drain);
        readiness.Should().BeGreaterThan(readBack);
        smoke.Should().BeGreaterThan(readiness);
        rollback.Should().BeGreaterThan(smoke);

        runbook.Should().Contain("controller");
        runbook.Should().Contain("Organization-level");
        runbook.Should().Contain("legacy-coverage-unproven");
        runbook.Should().Contain("sync-overrun");
        runbook.Should().Contain("non-durable topology");
        runbook.Should().Contain("no-go");
    }

    /// <summary>
    /// 讀取工作樹檔案時使用嚴格 UTF-8，並檢查 BOM、LF-only 與 final CRLF，
    /// 使契約測試本身也能阻止文件或腳本產生不可攜的編碼與行尾。
    /// </summary>
    /// <param name="segments">從已驗證工作樹根目錄開始的固定相對路徑區段。</param>
    /// <returns>通過編碼與行尾檢查後的完整 UTF-8 文字。</returns>
    private static string ReadRepositoryFile(params string[] segments)
    {
        var root = FindRepositoryRoot();
        var path = segments.Aggregate(root, Path.Combine);
        var bytes = File.ReadAllBytes(path);
        bytes.Should().NotBeEmpty();
        (bytes.Length >= 3 &&
         bytes[0] == 0xEF &&
         bytes[1] == 0xBB &&
         bytes[2] == 0xBF).Should().BeFalse();
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(bytes);
        text.Should().NotMatchRegex("(?<!\\r)\\n");
        text.Should().EndWith("\r\n");
        return text;
    }

    /// <summary>
    /// 從測試輸出目錄向上尋找工作樹根目錄；不接受呼叫端提供的路徑作為權限或部署權威。
    /// </summary>
    /// <returns>包含方案檔的目前工作樹根目錄。</returns>
    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "SpeechMessageProducts.sln")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("找不到工作樹根目錄。");
    }
}
