/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 ChurchReport 的「產品層金流命名」已經切回中性名稱。 這個測試不是在測銀行協定，也不是在測永豐、高鉅、台新的 API。 它測的是一條架構邊界：只要一段程式是 ChurchReport
 * 的奉獻表單、 付款流程、MVC Controller、Workflow、Adapter 或測試護欄，它就應該 使用 Payment / DonationPayment 這類中性名稱。 為什麼要這麼嚴格： 1.
 * 舊名稱看起來像「所有付款都屬於永豐」，但現在同一條流程會選擇永豐、高鉅或台新。 2. 未來建設公司維修系統、協會會員系統、發票收款系統也會重用共通金流模組； 如果共通入口仍帶著某一家 provider 的名稱，
 * 後續產品會很難判斷哪些程式可以安全重用。 3. 舊外部 URL 可以用 Route attribute 保留，因為那是瀏覽器或銀行 callback 的契約； 但是 C# 類別、介面、方法、變數、檔名不應再保留
 * provider 形狀的 alias。 注意：測試本身需要搜尋舊名稱，所以字串會用片段組合，避免這個測試檔自己被掃描規則誤判。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentFormModelNamingTests。
 * - 保護中性表單模型是唯一產品狀態型別。 在實際產品組件同時查詢新名稱及舊供應商名稱，偵測新型別遺失或舊別名回流；斷言新型別存在而舊型別不存在。
 * - 保護產品付款狀態僅使用中性 manager。 於真實產品組件查詢舊供應商名稱，模擬相容包裝被重建；斷言不存在，避免後續產品誤用供應商入口。
 * - 保護產品與共用金流的檔案命名邊界。 從目前方案根目錄掃描兩個確實存在的來源目錄，偵測供應商形狀的產品檔名；斷言例外範圍外沒有命中，不能漏掃改名後的產品目錄。
 * - 保護供應商字樣僅留在協定實作或已核准的既有路由。 掃描真實方案的執行程式來源，偵測供應商命名回流；斷言所有命中皆位於限定例外，註解與建置產物不作為產品行為。
 * 測試案例入口包含 Donation_payment_form_model_is_the_primary_churchreport_form_state_type、
 * Donation_payment_manager_is_the_only_churchreport_payment_state_manager、
 * Product_layer_file_names_should_not_contain_provider_brand_names、
 * Provider_specific_names_are_confined_to_provider_code_or_legacy_route_templates、IsAllowedLegacyRouteTemplate、
 * IsRuntimeCodeLine、ShouldIgnorePath、FindRepositoryRoot。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 * 可沿 ChurchReport.MemberInfo.Tests/Payments/DonationPaymentManagerNamingTests.cs 追查目前工作樹中的合作／使用位置。
 */
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

    /// <summary>
    /// 保護中性表單模型是唯一產品狀態型別。
    /// 在實際產品組件同時查詢新名稱及舊供應商名稱，偵測新型別遺失或舊別名回流；斷言新型別存在而舊型別不存在。
    /// </summary>
    [Fact]
    public void Donation_payment_form_model_is_the_primary_churchreport_form_state_type()
    {
        var formModelType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Models.DonationPaymentFormModel");
        var legacyModelType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType($"ChurchReport.Models.{LegacyModelToken}Model");

        formModelType.Should().NotBeNull(
            "奉獻付款表單是 ChurchReport 的產品層狀態，應使用 DonationPaymentFormModel 這種中性名稱，" +
            "讓信用卡、ATM、高鉅、台新與未來產品都不需要依附單一 provider 名稱");

        legacyModelType.Should().BeNull(
            "舊表單模型名稱會讓後續維護者誤以為奉獻付款表單只屬於單一 provider；" +
            "同一個 solution 內可以直接更新呼叫端，不需要保留產品層 alias");
    }

    /// <summary>
    /// 保護產品付款狀態僅使用中性 manager。
    /// 於真實產品組件查詢舊供應商名稱，模擬相容包裝被重建；斷言不存在，避免後續產品誤用供應商入口。
    /// </summary>
    /// <remarks>
    /// 與 <see cref="DonationPaymentManagerNamingTests.Legacy_qpay_manager_does_not_remain_as_compatibility_alias"/>
    /// 共同保護相同別名移除契約；此例仍歸屬表單命名套件，依審查指示保留原有案例。
    /// </remarks>
    [Fact]
    public void Donation_payment_manager_is_the_only_churchreport_payment_state_manager()
    {
        typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType($"ChurchReport.Models.{LegacyModelToken}Manager").Should().BeNull(
            "DonationPaymentManager 已經是 ChurchReport 奉獻付款 UI 狀態的主要 manager；" +
            "不應再保留舊 provider 形狀的 manager alias，否則後續新增高鉅、台新或其他產品時會誤用");
    }

    /// <summary>
    /// 保護產品與共用金流的檔案命名邊界。
    /// 從目前方案根目錄掃描兩個確實存在的來源目錄，偵測供應商形狀的產品檔名；斷言例外範圍外沒有命中，不能漏掃改名後的產品目錄。
    /// </summary>
    [Fact]
    public void Product_layer_file_names_should_not_contain_provider_brand_names()
    {
        var repositoryRoot = FindRepositoryRoot();
        var allowedPathFragments = new[]
        {
            Path.Combine("SpeechMessage.Payments", "Providers", "Sinopac"),
            Path.Combine("SpeechMessage.Payments.Tests", "Providers", "Sinopac"),
            Path.Combine("SpeechMessageProducts.ChurchReport", "文件")
        };

        var runtimeRoots = new[]
        {
            Path.Combine(repositoryRoot, "SpeechMessageProducts.ChurchReport"),
            Path.Combine(repositoryRoot, "SpeechMessage.Payments")
        };

        runtimeRoots.Should().OnlyContain(path => Directory.Exists(path), "架構檢查必須掃描目前方案的全部來源目錄");

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

    /// <summary>
    /// 保護供應商字樣僅留在協定實作或已核准的既有路由。
    /// 掃描真實方案的執行程式來源，偵測供應商命名回流；斷言所有命中皆位於限定例外，註解與建置產物不作為產品行為。
    /// </summary>
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
            Path.Combine(repositoryRoot, "SpeechMessageProducts.ChurchReport"),
            Path.Combine(repositoryRoot, "SpeechMessage.Payments")
        };

        runtimeRoots.Should().OnlyContain(path => Directory.Exists(path), "產品改名後仍須完整執行命名邊界檢查");

        var scannedFiles = runtimeRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !ShouldIgnorePath(repositoryRoot, path))
            .ToArray();

        var offenders = scannedFiles
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, lineNumber = index + 1 }))
            .Where(item => IsRuntimeCodeLine(item.line))
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

    /// <summary>
    /// 只允許列名的既有路由擁有者保留相容 URL 字串，避免以一般字串或不同檔案繞過命名邊界。
    /// 路徑與單行文字來自本次來源掃描；回傳是否屬於固定的路由例外，不保留任何掃描狀態。
    /// </summary>
    /// <param name="relativePath">目前方案內的相對來源路徑。</param>
    /// <param name="line">本次檢查的來源文字行。</param>
    /// <returns>僅在檔案、路由語法與相容 URL 皆符合時回傳 true。</returns>
    private static bool IsAllowedLegacyRouteTemplate(string relativePath, string line)
    {
        var isLegacyRouteOwner =
            relativePath.Equals(Path.Combine("SpeechMessageProducts.ChurchReport", "Controllers", "DedicationController.cs"), StringComparison.OrdinalIgnoreCase) ||
            relativePath.Equals(Path.Combine("SpeechMessageProducts.ChurchReport", "Controllers", "DonationPaymentLoginController.cs"), StringComparison.OrdinalIgnoreCase) ||
            relativePath.Equals(Path.Combine("SpeechMessageProducts.ChurchReport", "Controllers", "HomeController.cs"), StringComparison.OrdinalIgnoreCase) ||
            relativePath.Equals(Path.Combine("SpeechMessageProducts.ChurchReport", "Controllers", "PaymentReturnController.cs"), StringComparison.OrdinalIgnoreCase) ||
            relativePath.Equals(Path.Combine("SpeechMessageProducts.ChurchReport", "WebServiceConnector", "DonationPaymentProcessor", "DonationPaymentProcessor.Core.cs"), StringComparison.OrdinalIgnoreCase) ||
            relativePath.Equals(Path.Combine("SpeechMessageProducts.ChurchReport", "Startup.cs"), StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// 將既有純註解行排除於執行程式命名檢查之外；保留實際程式行以偵測供應商名稱回流。
    /// 判斷只依當次文字，不改寫來源檔，也不保存掃描結果。
    /// </summary>
    /// <param name="line">本次掃描的來源行。</param>
    /// <returns>不是既有純註解形式時回傳 true。</returns>
    private static bool IsRuntimeCodeLine(string line)
    {
        var trimmed = line.TrimStart();
        return !trimmed.StartsWith("//", StringComparison.Ordinal) &&
               !trimmed.StartsWith("///", StringComparison.Ordinal) &&
               !trimmed.StartsWith("*", StringComparison.Ordinal) &&
               !trimmed.StartsWith("/*", StringComparison.Ordinal);
    }

    /// <summary>
    /// 排除產物與其他 worktree，確保此架構測試只檢查目前版本的正式來源。
    /// 使用完整目錄片段比較而非模糊字串，以免漏掃名稱包含相同文字的正式檔案。
    /// </summary>
    /// <param name="repositoryRoot">目前方案的根目錄。</param>
    /// <param name="path">本次列舉的檔案或目錄。</param>
    /// <returns>屬於固定排除目錄時回傳 true。</returns>
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

    /// <summary>
    /// 從測試輸出目錄定位最近的目前方案，避免將上層 worktree 的其他版本當作驗證依據。
    /// 找不到明確方案標記時即失敗，不使用目前工作目錄或空集合讓架構檢查假通過。
    /// </summary>
    /// <returns>含 SpeechMessageProducts.sln 的最近上層目錄。</returns>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到 SpeechMessageProducts.sln，無法判斷 repository root。");
    }
}
