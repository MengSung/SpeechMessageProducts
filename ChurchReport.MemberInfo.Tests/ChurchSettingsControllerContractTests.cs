/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P8-03b 教會設定 Area 的 HTTP 授權、教會範圍、輸出編碼、CSRF 與本地化契約。
 *
 * 主要功能與流程：
 * 每個測試從測試輸出目錄尋找方案根目錄，再檢查 ChurchSettings Area 的入口與
 * 對應資源／啟動政策；案例名稱與 08A §5 P8-03b 的十個核定名稱一致。
 * 測試刻意先於產品實作建立，首輪應因 Area 尚不存在而失敗；實作後確認同一組斷言轉綠。
 *
 * 使用邊界與維護：
 * 本檔為離線來源契約測試，不建立 ASP.NET Host、不連線 D365／LINE，也不宣稱實機結果。
 * 測試只讀目前工作樹文字，不保存登入狀態、教會資料或外部資源；尋找不到方案根目錄時明確失敗。
 * 更新契約時須維持測試名稱，並確保檢查的是實際授權與資料範圍，而非只驗證導覽隱藏。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ChurchReport.Areas.ChurchSettings;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;
using SpeechMessageProducts.GoodShepherd.Core.Settings;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 ChurchSettings 三個頁面及其共用安全邊界；本離線測試不啟動網站或外部服務。
/// </summary>
public sealed class ChurchSettingsControllerContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string AreaPath = Path.Combine(
        RepositoryRoot, "SpeechMessageProducts.ChurchReport", "Areas", "ChurchSettings");
    private static readonly string ControllerPath = Path.Combine(AreaPath, "ChurchSettingsController.cs");
    private static readonly string AccessPath = Path.Combine(AreaPath, "ChurchSettingsAccess.cs");

    /// <summary>非管理員提交名詞時必須由伺服器授權拒絕，而不是依賴導覽是否顯示。</summary>
    [Fact]
    public async Task Save_NonAdmin_Forbidden()
    {
        using var fixture = new ControllerFixture(jobTitle: "會計", churchId: null);

        var result = await fixture.Controller.SaveTerm("Role.Member", "zh-TW", "合成文字");

        AssertForbiddenResult(fixture.Controller, result);
    }

    /// <summary>請求夾帶其他教會識別不得覆蓋登入 contact 的伺服器端教會。</summary>
    [Fact]
    public async Task Save_ChurchIdFromRequestIgnored()
    {
        var churchId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var spoofedChurchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        using var fixture = new ControllerFixture(
            jobTitle: "牧養主任",
            churchId,
            allowAuthorizedWrites: true);
        fixture.Controller.HttpContext.Request.QueryString =
            new QueryString("?churchId=" + spoofedChurchId);
        fixture.Controller.HttpContext.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["churchId"] = spoofedChurchId.ToString()
            });

        var result = await fixture.Controller.SaveCategory(
            id: null, "合成類別", 1, null, null, isActive: true);

        result.Should().BeOfType<RedirectToActionResult>();
        fixture.SettingsStore.AddCategoryChurchIds.Should().ContainSingle().Which.Should().Be(churchId);
    }

    /// <summary>含 script 標記的覆寫只能透過 Razor 預設編碼呈現，不得輸出 raw HTML。</summary>
    [Fact]
    public void Save_ScriptInput_EncodedOnOutput()
    {
        var views = Directory.Exists(AreaPath)
            ? Directory.GetFiles(AreaPath, "*.cshtml", SearchOption.AllDirectories)
            : Array.Empty<string>();
        views.Should().NotBeEmpty();
        var executableRazor = string.Join(Environment.NewLine,
            views.Select(path => Regex.Replace(File.ReadAllText(path), @"@\*.*?\*@", string.Empty,
                RegexOptions.Singleline)));
        executableRazor.Should().NotContain("Html.Raw");
        executableRazor.Should().Contain("@(row?.OverrideText ?? string.Empty)");
        executableRazor.Should().Contain("@category.Name");
        HtmlEncoder.Default.Encode("<script>alert(1)</script>")
            .Should().Be("&lt;script&gt;alert(1)&lt;/script&gt;");
    }

    /// <summary>非管理員的 GET 頁面同樣拒絕，避免只保護寫入端點。</summary>
    [Fact]
    public async Task Get_NonAdmin_Forbidden()
    {
        using var fixture = new ControllerFixture(jobTitle: "會計", churchId: null);

        var result = await fixture.Controller.Terms();

        AssertForbiddenResult(fixture.Controller, result);
    }

    /// <summary>沒有有效 account parentcustomerid 的登入 contact 不得取得任何設定頁資料。</summary>
    [Fact]
    public async Task Get_LoginWithoutChurch_Forbidden()
    {
        using var fixture = new ControllerFixture(jobTitle: "牧養主任", churchId: null);

        var result = await fixture.Controller.OfferingCategories();

        AssertForbiddenResult(fixture.Controller, result);
    }

    /// <summary>稽核頁非管理員回傳固定 403 檢視，不觸發 Cookie challenge 或 Location 轉址。</summary>
    [Fact]
    public async Task Audit_NonAdmin_Forbidden()
    {
        using var fixture = new ControllerFixture(jobTitle: "會計", churchId: null);

        var result = await fixture.Controller.Audit();

        AssertForbiddenResult(fixture.Controller, result);
    }

    /// <summary>管理員導覽只由完整伺服器授權 scope 顯示，非管理員不會取得入口。</summary>
    [Fact]
    public async Task Nav_ChurchSettingsLink_OnlyForAdmin()
    {
        using var nonAdmin = new ControllerFixture(jobTitle: "會計", churchId: null);
        using var admin = new ControllerFixture(
            jobTitle: "牧養主任",
            churchId: Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var viewPath = Path.Combine(RepositoryRoot, "SpeechMessageProducts.ChurchReport", "Views", "Shared",
            "Components", "ChurchSettingsNav", "Default.cshtml");

        var hidden = await new ChurchSettingsNavViewComponent(nonAdmin.Access)
            .CanShowAsync(nonAdmin.Controller.User, CancellationToken.None);
        var visible = await new ChurchSettingsNavViewComponent(admin.Access)
            .CanShowAsync(admin.Controller.User, CancellationToken.None);

        hidden.Should().BeFalse();
        visible.Should().BeTrue();
        var view = Read(viewPath);
        view.Should().Contain("if (Model)");
        view.Should().Contain("ChurchSettings.NavigationTitle");
    }

    /// <summary>名詞與類別所有寫入 action 均須由 MVC antiforgery filter 驗證 POST token。</summary>
    [Fact]
    public void Post_WithoutAntiforgery_Rejected()
    {
        var postActions = typeof(ChurchSettingsController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<HttpPostAttribute>() is not null)
            .ToArray();
        postActions.Should().NotBeEmpty();
        postActions.Where(method =>
            method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is null).Should().BeEmpty();
    }

    /// <summary>ChurchSettings 所有使用者可見固定文字皆由該 Area 的資源檔提供。</summary>
    [Fact]
    public void ChurchSettings_QpsPloc_AllVisibleTextFromResource()
    {
        var resourcePath = Path.Combine(AreaPath, "Resources", "ChurchSettingsText.resx");
        File.Exists(resourcePath).Should().BeTrue();
        var resource = XDocument.Load(resourcePath);
        resource.Descendants("data").Select(element => (string?)element.Attribute("name"))
            .Should().Contain(new[]
            {
                "ChurchSettings.NavigationTitle",
                "ChurchSettings.TermsTitle",
                "ChurchSettings.OfferingCategoriesTitle",
                "ChurchSettings.AuditTitle"
            });
        var views = Directory.GetFiles(AreaPath, "*.cshtml", SearchOption.AllDirectories);
        var executableRazor = string.Join(Environment.NewLine,
            views.Select(path => Regex.Replace(File.ReadAllText(path), @"@\*.*?\*@", string.Empty,
                RegexOptions.Singleline)));
        executableRazor.Should().NotContain("教會設定");
        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture =
                System.Globalization.CultureInfo.GetCultureInfo("qps-ploc");
            foreach (var key in resource.Descendants("data")
                         .Select(element => (string?)element.Attribute("name"))
                         .Where(name => name is not null))
            {
                ChurchSettingsText.GetText(key!).Should().StartWith("［").And.EndWith("］");
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    /// <summary>Production 啟動政策必須沿用共用本地化驗證並拒絕 qps-ploc。</summary>
    [Fact]
    public void ChurchSettings_ProductionRejectsQpsPloc()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Localization:AllowPseudoLocale", "true")
            })
            .Build();

        Action validate = () => SpeechMessage.Hosting.StartupGuards
            .ValidateLocalizationConfiguration(configuration, isProduction: true);

        validate.Should().Throw<InvalidOperationException>();
    }

    /// <summary>奉獻類別讀取必須以本次已授權教會為唯一 scope，並以正式 ID 保留列身份。</summary>
    [Fact]
    public async Task OfferingCategories_PageShowsOnlyThisChurch()
    {
        var churchId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var categoryId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        using var fixture = new ControllerFixture(
            jobTitle: "牧養主任",
            churchId,
            categories: new[]
            {
                new OfferingCategory(categoryId, churchId, "custom-11111111", "合成類別", 1, null, null, true)
            });

        var result = await fixture.Controller.OfferingCategories();

        result.Should().BeOfType<ViewResult>().Which.Model
            .Should().BeAssignableTo<IReadOnlyList<OfferingCategory>>()
            .Which.Should().ContainSingle(category => category.Id == categoryId && category.ChurchId == churchId);
        fixture.SettingsStore.ReadCategoryChurchIds.Should().ContainSingle().Which.Should().Be(churchId);
    }

    /// <summary>稽核查詢必須先限定本教會且限制最多 200 筆，再投影安全顯示欄位。</summary>
    [Fact]
    public async Task Audit_PageShowsOnlyThisChurch()
    {
        var churchId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var auditId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        using var fixture = new ControllerFixture(
            jobTitle: "牧養主任",
            churchId,
            auditRecords: new[]
            {
                new ChurchSettingsAuditRecord(
                    auditId, churchId, Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc),
                    "new_name", "舊名稱", "新名稱", "合成管理員")
            });

        var result = await fixture.Controller.Audit();

        result.Should().BeOfType<ViewResult>().Which.Model
            .Should().BeAssignableTo<IReadOnlyList<ChurchSettingsAuditRecord>>()
            .Which.Should().ContainSingle(record => record.Id == auditId
                && record.ChurchId == churchId
                && record.TargetRecordId == Guid.Parse("55555555-5555-5555-5555-555555555555")
                && record.ActorDisplayName == "合成管理員");
        fixture.SettingsStore.ReadAuditRequests.Should().ContainSingle()
            .Which.Should().Be((churchId, 200));
    }

    /// <summary>名詞頁分開呈現系統原文、目前教會覆寫及 catalog 真正解析的預覽，並拒絕外教會覆寫。</summary>
    [Fact]
    public async Task Terms_SystemOverrideAndResolvedPreview_AreDistinct()
    {
        var churchId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var foreignChurchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        using var fixture = new ControllerFixture(
            jobTitle: "牧養主任",
            churchId,
            termOverrides: new[]
            {
                new ChurchSettingsTermOverride(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    churchId, "Role.Member", "zh-TW", "教會覆寫"),
                new ChurchSettingsTermOverride(
                    Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    foreignChurchId, "Role.Member", "zh-TW", "外教會文字")
            });

        var result = await fixture.Controller.Terms();

        var pageModel = result.Should().BeOfType<ViewResult>().Which.Model!;
        var rows = ((System.Collections.IEnumerable)pageModel.GetType()
            .GetProperty("Overrides")!.GetValue(pageModel)!).Cast<object>().ToArray();
        rows.Should().HaveCount(12);
        var member = Assert.Single(rows.Where(row =>
            (string)row.GetType().GetProperty("Key")!.GetValue(row)! == "Role.Member"));
        Assert.Equal("教會覆寫", member.GetType().GetProperty("OverrideText")!.GetValue(member));
        Assert.Equal("小組組員", member.GetType().GetProperty("SystemText")!.GetValue(member));
        Assert.Equal("預覽:Role.Member", member.GetType().GetProperty("Preview")!.GetValue(member));
    }

    private static string Read(string path)
    {
        File.Exists(path).Should().BeTrue($"expected P8-03b implementation file {Path.GetFileName(path)}");
        return File.ReadAllText(path);
    }

    private static void AssertForbiddenResult(ChurchSettingsController controller, IActionResult result)
    {
        controller.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        controller.HttpContext.Response.Headers.ContainsKey("Location").Should().BeFalse();
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("Forbidden");
        view.Model.Should().BeOfType<string>().Which.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// 建立單次測試專用的 MVC 與 CRM 替身；資料只含固定合成身份，不建立網路或持久資源。
    /// </summary>
    private sealed class ControllerFixture : IDisposable
    {
        private readonly ChurchSettingsSdkProxy sdk;

        internal ControllerFixture(
            string jobTitle,
            Guid? churchId,
            IReadOnlyList<OfferingCategory>? categories = null,
            IReadOnlyList<ChurchSettingsAuditRecord>? auditRecords = null,
            bool allowAuthorizedWrites = false,
            IReadOnlyList<ChurchSettingsTermOverride>? termOverrides = null)
        {
            var service = DispatchProxy.Create<IOrganizationService, ChurchSettingsSdkProxy>();
            sdk = (ChurchSettingsSdkProxy)(object)service;
            sdk.ContactJobTitle = jobTitle;
            sdk.ChurchId = churchId;

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("CrmConnection:Organization", "synthetic-test-org")
                })
                .Build();
            var access = new ChurchSettingsAccess(
                service,
                configuration,
                new MemberCultureResolver(new LocalizationOptions(allowPseudoLocale: true)));
            Access = access;
            SettingsStore = new ControllerSettingsStore(categories, auditRecords, termOverrides);
            var settings = new ChurchSettingsService(
                SettingsStore,
                new ControllerCacheInvalidator(allowAuthorizedWrites),
                GoodShepherdTextCatalog.CreateOverrideValidator("這是一個長度剛好十二字的教會"),
                new ControllerClock());
            var textCatalog = DispatchProxy.Create<ITextCatalog, ControllerTextCatalogProxy>();
            var systemTextCatalog = DispatchProxy.Create<ISystemTextCatalog, ControllerSystemTextCatalogProxy>();
            Controller = new ChurchSettingsController(
                access, settings, SettingsStore, SettingsStore, textCatalog, systemTextCatalog)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            new[]
                            {
                                new Claim("church:contactId", ChurchSettingsSdkProxy.ContactId.ToString()),
                                new Claim("church:loginType", "小組長")
                            },
                            authenticationType: "synthetic-test"))
                    }
                }
            };
        }

        internal ChurchSettingsController Controller { get; }
        internal ChurchSettingsAccess Access { get; }
        internal ControllerSettingsStore SettingsStore { get; }

        public void Dispose()
        {
            sdk.ContactJobTitle = null;
            sdk.ChurchId = null;
        }
    }

    /// <summary>只提供本案例登入 contact；任何非預期 CRM 操作均失敗，不模擬外部狀態。</summary>
    public class ChurchSettingsSdkProxy : DispatchProxy
    {
        internal static readonly Guid ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        internal string? ContactJobTitle { get; set; }
        internal Guid? ChurchId { get; set; }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IOrganizationService.Retrieve)
                && args is { Length: >= 3 }
                && args[0] is string entityName)
            {
                if (entityName == "account" && ChurchId is { } accountId)
                {
                    return new Entity("account", accountId)
                    {
                        ["new_defaultlanguage"] = "zh-TW",
                        ["new_enabledlanguages"] = "zh-TW"
                    };
                }
                if (entityName == "contact")
                {
                    var contact = new Entity("contact", ContactId)
                    {
                        ["new_church_jobtitle"] = ContactJobTitle
                    };
                    if (ChurchId is { } churchId)
                        contact["parentcustomerid"] = new EntityReference("account", churchId);
                    return contact;
                }
            }
            throw new InvalidOperationException("Unexpected CRM operation in ChurchSettings authorization test.");
        }
    }

    /// <summary>Controller 未授權案例不讀文字 catalog；意外呼叫明確失敗。</summary>
    public class EmptyTextCatalogProxy : DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("Unauthorized action must not read the text catalog.");
    }

    /// <summary>回傳合成預覽值；測試替身不保存查詢 context 或參數。</summary>
    public class ControllerTextCatalogProxy : DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            "預覽:" + (args is { Length: > 1 } && args[1] is TextKey key ? key.Value : string.Empty);
    }

    /// <summary>提供固定合成系統原文，不執行任何 catalog I/O 或教會覆寫查詢。</summary>
    public class ControllerSystemTextCatalogProxy : DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            args is { Length: > 1 } && args[1] is TextKey key && key.Value == "Role.Member"
                ? "小組組員"
                : "系統原文";
    }

    /// <summary>沒有持久化或外部依賴的核心 store，防止授權測試意外進入資料寫入。</summary>
    internal sealed class ControllerSettingsStore : IChurchSettingsStore, IChurchSettingsTermReader
    {
        private readonly IReadOnlyList<OfferingCategory> categories;
        private readonly IReadOnlyList<ChurchSettingsAuditRecord> auditRecords;
        private readonly IReadOnlyList<ChurchSettingsTermOverride> termOverrides;

        internal ControllerSettingsStore(
            IReadOnlyList<OfferingCategory>? categories = null,
            IReadOnlyList<ChurchSettingsAuditRecord>? auditRecords = null,
            IReadOnlyList<ChurchSettingsTermOverride>? termOverrides = null)
        {
            this.categories = categories ?? Array.Empty<OfferingCategory>();
            this.auditRecords = auditRecords ?? Array.Empty<ChurchSettingsAuditRecord>();
            this.termOverrides = termOverrides ?? Array.Empty<ChurchSettingsTermOverride>();
        }

        internal List<Guid> ReadCategoryChurchIds { get; } = new();
        internal List<(Guid ChurchId, int Maximum)> ReadAuditRequests { get; } = new();
        internal List<Guid> AddCategoryChurchIds { get; } = new();

        public Task UpsertOverrideAsync(ChurchTextOverrideWrite value, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unauthorized action must not write settings.");
        public Task RemoveOverrideAsync(Guid churchId, string key, string culture, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unauthorized action must not write settings.");
        public Task TouchTextChangedOnAsync(Guid churchId, DateTime changedOnUtc, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unauthorized action must not write settings.");
        public Task<IReadOnlyList<OfferingCategory>> ReadCategoriesAsync(Guid churchId, CancellationToken cancellationToken)
        {
            ReadCategoryChurchIds.Add(churchId);
            return Task.FromResult(categories);
        }
        public Task<bool> CategoryCodeExistsAsync(Guid churchId, string code, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<Guid> AddCategoryAsync(Guid churchId, string code, string name, int sortOrder,
            string? accountingCode, int? legacyOptionValue, CancellationToken cancellationToken) =>
            AddCategoryAndRecordChurch(churchId);
        public Task UpdateCategoryAsync(Guid churchId, Guid id, string name, int sortOrder,
            string? accountingCode, int? legacyOptionValue, bool isActive, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unauthorized action must write categories.");
        public Task<IReadOnlyList<ChurchSettingsAuditRecord>> ReadAuditAsync(Guid churchId, int maximum,
            CancellationToken cancellationToken)
        {
            ReadAuditRequests.Add((churchId, maximum));
            return Task.FromResult(auditRecords);
        }
        public Task<IReadOnlyList<ChurchSettingsTermOverride>> ReadTermOverridesAsync(
            Guid churchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChurchSettingsTermOverride>>(
                termOverrides.Where(value => value.ChurchId == churchId).ToArray());

        private Task<Guid> AddCategoryAndRecordChurch(Guid churchId)
        {
            AddCategoryChurchIds.Add(churchId);
            return Task.FromResult(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        }
    }

    /// <summary>授權拒絕案例不執行快取失效；任何意外呼叫表示測試或控制器邊界錯誤。</summary>
    private sealed class ControllerCacheInvalidator : IChurchSettingsCacheInvalidator
    {
        private readonly bool allowAuthorizedWrites;

        internal ControllerCacheInvalidator(bool allowAuthorizedWrites) =>
            this.allowAuthorizedWrites = allowAuthorizedWrites;

        public void Invalidate(TextContext context, DateTime changedOnUtc) =>
            _ = allowAuthorizedWrites
                ? context
                : throw new InvalidOperationException("Unauthorized action must not invalidate settings.");
    }

    /// <summary>未授權案例不讀時鐘；提供固定 UTC 值避免測試依賴牆上時間。</summary>
    private sealed class ControllerClock : IChurchSettingsClock
    {
        public DateTime UtcNow => new(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate SpeechMessageProducts.sln from test output directory.");
    }
}
