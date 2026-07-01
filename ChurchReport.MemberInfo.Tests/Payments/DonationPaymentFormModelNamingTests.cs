using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 ChurchReport 的「產品層金流命名」已經切回中性名稱。
///
/// 這個測試不是在測銀行協定，也不是在測永豐、高鉅、台新的 API。
/// 它測的是一條架構邊界：只要一段程式是 ChurchReport 的奉獻表單、
/// 付款流程、MVC Controller、Workflow、Adapter 或測試護欄，它就應該
/// 使用 Payment / DonationPayment 這類中性名稱。
///
/// 為什麼要這麼嚴格：
/// 1. 舊名稱看起來像「所有付款都屬於永豐」，但現在同一條流程會選擇永豐、高鉅或台新。
/// 2. 未來建設公司維修系統、協會會員系統、發票收款系統也會重用共通金流模組；
///    如果共通入口仍帶著某一家 provider 的名稱，後續產品會很難判斷哪些程式可以安全重用。
/// 3. 舊外部 URL 可以用 Route attribute 保留，因為那是瀏覽器或銀行 callback 的契約；
///    但是 C# 類別、介面、方法、變數、檔名不應再保留 provider 形狀的 alias。
///
/// 注意：測試本身需要搜尋舊名稱，所以字串會用片段組合，避免這個測試檔自己被掃描規則誤判。
/// </summary>
public sealed class DonationPaymentFormModelNamingTests
{
    private static readonly string ProviderBrandToken = "Q" + "Pay";
    private static readonly string LegacyModelToken = "Q" + "pay";
    private static readonly string LowerProviderBrandToken = "q" + "pay";

    [Fact]
    public void Donation_payment_form_model_is_the_primary_churchreport_form_state_type()
    {
        var formModelType = Type.GetType("ChurchReport.Models.DonationPaymentFormModel, ChurchReport");
        var legacyModelType = Type.GetType($"ChurchReport.Models.{LegacyModelToken}Model, ChurchReport");

        formModelType.Should().NotBeNull(
            "奉獻付款表單是 ChurchReport 的產品層狀態，應使用 DonationPaymentFormModel 這種中性名稱，" +
            "讓信用卡、ATM、高鉅、台新與未來產品都不需要依附單一 provider 名稱");

        legacyModelType.Should().BeNull(
            "舊表單模型名稱會讓後續維護者誤以為奉獻付款表單只屬於單一 provider；" +
            "同一個 solution 內可以直接更新呼叫端，不需要保留產品層 alias");
    }

    [Fact]
    public void Donation_payment_manager_is_the_only_churchreport_payment_state_manager()
    {
        Type.GetType($"ChurchReport.Models.{LegacyModelToken}Manager, ChurchReport").Should().BeNull(
            "DonationPaymentManager 已經是 ChurchReport 奉獻付款 UI 狀態的主要 manager；" +
            "不應再保留舊 provider 形狀的 manager alias，否則後續新增高鉅、台新或其他產品時會誤用");
    }

    [Fact]
    public void Product_layer_file_names_should_not_contain_provider_brand_names()
    {
        var repositoryRoot = FindRepositoryRoot();
        var allowedPathFragments = new[]
        {
            Path.Combine("SpeechMessage.Payments", "Providers", "Sinopac"),
            Path.Combine("SpeechMessage.Payments.Tests", "Providers", "Sinopac"),
            Path.Combine("ChurchReport", "文件")
        };

        var runtimeRoots = new[]
        {
            Path.Combine(repositoryRoot, "ChurchReport"),
            Path.Combine(repositoryRoot, "SpeechMessage.Payments")
        };

        var offenders = runtimeRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories))
            .Where(path => !ShouldIgnorePath(repositoryRoot, path))
            .Where(path =>
                Path.GetFileName(path).Contains(ProviderBrandToken, StringComparison.Ordinal) ||
                Path.GetFileName(path).Contains(LegacyModelToken, StringComparison.Ordinal) ||
                Path.GetFileName(path).Contains(LowerProviderBrandToken, StringComparison.Ordinal))
            .Where(path =>
            {
                var relative = Path.GetRelativePath(repositoryRoot, path);
                return !allowedPathFragments.Any(fragment =>
                    relative.StartsWith(fragment, StringComparison.OrdinalIgnoreCase));
            })
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .OrderBy(path => path)
            .ToArray();

        offenders.Should().BeEmpty(
            "產品層檔名要說明自己真正的責任，例如 DonationPayment 或 PaymentReturn；" +
            "舊 URL 必須靠 Route attribute 保留，不應靠舊檔名或舊類別名保留");
    }

    [Fact]
    public void Provider_specific_names_are_confined_to_provider_code_or_legacy_route_templates()
    {
        var repositoryRoot = FindRepositoryRoot();
        var allowedProviderFragments = new[]
        {
            Path.Combine("SpeechMessage.Payments", "Providers", "Sinopac"),
            Path.Combine("SpeechMessage.Payments.Tests", "Providers", "Sinopac")
        };

        var runtimeRoots = new[]
        {
            Path.Combine(repositoryRoot, "ChurchReport"),
            Path.Combine(repositoryRoot, "SpeechMessage.Payments")
        };

        var scannedFiles = runtimeRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !ShouldIgnorePath(repositoryRoot, path))
            .ToArray();

        var offenders = scannedFiles
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, lineNumber = index + 1 }))
            .Where(item =>
                item.line.Contains(ProviderBrandToken, StringComparison.Ordinal) ||
                item.line.Contains(LegacyModelToken, StringComparison.Ordinal) ||
                item.line.Contains(LowerProviderBrandToken, StringComparison.Ordinal))
            .Where(item =>
            {
                var relative = Path.GetRelativePath(repositoryRoot, item.path);
                var isProviderCode = allowedProviderFragments.Any(fragment =>
                    relative.StartsWith(fragment, StringComparison.OrdinalIgnoreCase));

                return !isProviderCode && !IsAllowedLegacyRouteTemplate(relative, item.line);
            })
            .Select(item => $"{Path.GetRelativePath(repositoryRoot, item.path)}:{item.lineNumber}:{item.line.Trim()}")
            .OrderBy(line => line)
            .ToArray();

        offenders.Should().BeEmpty(
            "產品中性流程不能再使用 provider 形狀的名稱；剩餘例外只能是 Sinopac provider 協定程式，" +
            "或為了不中斷既有瀏覽器網址與銀行 callback 而保留的 Route template 字串");
    }

    private static bool IsAllowedLegacyRouteTemplate(string relativePath, string line)
    {
        var isLegacyRouteOwner =
            relativePath.Equals(Path.Combine("ChurchReport", "Controllers", "DedicationController.cs"), StringComparison.OrdinalIgnoreCase) ||
            relativePath.Equals(Path.Combine("ChurchReport", "Controllers", "DonationPaymentLoginController.cs"), StringComparison.OrdinalIgnoreCase) ||
            relativePath.Equals(Path.Combine("ChurchReport", "Controllers", "HomeController.cs"), StringComparison.OrdinalIgnoreCase) ||
            relativePath.Equals(Path.Combine("ChurchReport", "Controllers", "PaymentReturnController.cs"), StringComparison.OrdinalIgnoreCase) ||
            relativePath.Equals(Path.Combine("ChurchReport", "WebServiceConnector", "DonationPaymentProcessor", "DonationPaymentProcessor.Core.cs"), StringComparison.OrdinalIgnoreCase) ||
            relativePath.Equals(Path.Combine("ChurchReport", "Startup.cs"), StringComparison.OrdinalIgnoreCase);

        if (!isLegacyRouteOwner)
        {
            return false;
        }

        var trimmed = line.TrimStart();
        var isRouteString =
            trimmed.StartsWith("[Route(", StringComparison.Ordinal) ||
            trimmed.StartsWith("[HttpGet(", StringComparison.Ordinal) ||
            trimmed.StartsWith("[HttpPost(", StringComparison.Ordinal) ||
            trimmed.StartsWith("template:", StringComparison.Ordinal) ||
            line.Contains("[\"QPAY_ORGANIZATION\"]", StringComparison.Ordinal);

        if (!isRouteString)
        {
            return false;
        }

        var legacyRouteTokens = new[]
        {
            "Q" + "PayLogin",
            "Q" + "PayCard",
            "Q" + "PayView"
        };

        return legacyRouteTokens.Any(token => line.Contains(token, StringComparison.Ordinal));
    }

    private static bool ShouldIgnorePath(string repositoryRoot, string path)
    {
        var relative = Path.GetRelativePath(repositoryRoot, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".worktrees", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ChurchReport.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到 ChurchReport.sln，無法判斷 repository root。");
    }
}
