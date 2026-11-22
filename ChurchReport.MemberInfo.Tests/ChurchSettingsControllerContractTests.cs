/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P8-03b 教會設定 Area 的授權、教會範圍、檢視發佈／探索、
 * 覆寫快照載入順序、輸出編碼、CSRF 與本地化契約。
 *
 * 主要功能與流程：
 * 每個測試從測試輸出目錄尋找方案根目錄，並以合成登入者、真實記憶體覆寫快取、
 * MVC Razor view engine 及 dotnet msbuild 的 Content 項目評估，檢查 Area 的實際執行契約。
 * MSBuild 子程序設有逾時與終止路徑，輸出只解析專案項目，不啟動 Web Host 或外部連線。
 *
 * 使用邊界與維護：
 * 本檔為離線整合契約測試，不連線 D365／LINE，也不宣稱實機結果。每個 fixture 僅保存
 * 固定合成身份和本案例資料；測試 owner 釋放 MVC provider、MSBuild 子程序及非同步快取，
 * 不保留跨案例登入、教會 scope 或來源工作。更新契約時須維持核定測試名稱，並驗證實際
 * 發佈項目、Area view discovery 與 Terms GET 的覆寫結果，而非只檢查設定文字或屬性。
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ChurchReport.Areas.ChurchSettings;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Localization;
using SpeechMessage.Tenancy;
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

    /// <summary>
    /// 保護 GET 執行順序：從全新、尚未載入的教會覆寫快取呼叫 Terms action，
    /// action 必須先載入完整授權 scope，再讓正式 catalog 解析並顯示該教會覆寫文字。
    /// 離線來源只回傳固定合成列；決定性斷言是 GET 回傳頁面、預覽為覆寫值且來源恰讀一次。
    /// </summary>
    [Fact]
    public async Task Terms_Get_LoadsOverrideSnapshotBeforePreview()
    {
        var churchId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var source = new TermsPreviewOverrideSource(churchId, "載入後的教會覆寫");
        await using var cache = new ChurchTextOverrideCache(
            source,
            GoodShepherdTextCatalog.CreateOverrideValidator("這是一個長度剛好十二字的教會"),
            static (_, _) => { });
        var catalog = new GoodShepherdTextCatalog(cache);
        using var fixture = new ControllerFixture(
            jobTitle: "牧養主任",
            churchId,
            overrideCache: cache,
            textCatalog: catalog,
            systemTextCatalog: catalog);

        var result = await fixture.Controller.Terms();

        var pageModel = result.Should().BeOfType<ViewResult>().Which.Model!;
        var rows = ((System.Collections.IEnumerable)pageModel.GetType()
            .GetProperty("Overrides")!.GetValue(pageModel)!).Cast<object>().ToArray();
        var member = Assert.Single(rows.Where(row =>
            (string)row.GetType().GetProperty("Key")!.GetValue(row)! == "Role.Member"));
        Assert.Equal("載入後的教會覆寫",
            member.GetType().GetProperty("Preview")!.GetValue(member));
        source.Reads.Should().Be(1);
    }

    /// <summary>
    /// 保護發佈契約：以 MSBuild 對正式 ChurchReport 專案執行 Content 項目評估，
    /// 並驗證 Area 下實際展開的 Terms.cshtml 帶有發佈複製中繼資料，而非比對 csproj 文字。
    /// dotnet 子程序只載入專案，不啟動網站或連外；測試 owner 在逾時時終止整個程序樹。
    /// </summary>
    [Fact]
    public async Task Publish_AreaRazorViews_IncludedInPublishOutput()
    {
        var projectPath = Path.Combine(
            RepositoryRoot, "SpeechMessageProducts.ChurchReport", "SpeechMessageProducts.ChurchReport.csproj");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-getItem:Content");
        startInfo.ArgumentList.Add("-property:Configuration=Release");

        using var process = Process.Start(startInfo);
        process.Should().NotBeNull("the installed dotnet SDK must evaluate the Web project");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var outputTask = ReadBoundedProcessOutputAsync(process!.StandardOutput, process, timeout.Token);
        var errorTask = ReadBoundedProcessOutputAsync(process.StandardError, process, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("MSBuild Content evaluation exceeded its two-minute test budget.");
        }

        var output = await outputTask;
        var error = await errorTask;
        process.ExitCode.Should().Be(0, error);
        using var evaluation = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 8 });
        var content = evaluation.RootElement.GetProperty("Items").GetProperty("Content");
        var areaView = content.EnumerateArray().SingleOrDefault(item =>
            item.GetProperty("Identity").GetString()?
                .Replace('\\', '/')
                .Equals("Areas/ChurchSettings/Views/ChurchSettings/Terms.cshtml",
                    StringComparison.OrdinalIgnoreCase) == true);

        areaView.ValueKind.Should().Be(JsonValueKind.Object,
            "the evaluated project must contain the actual ChurchSettings Terms view");
        areaView.GetProperty("CopyToPublishDirectory").GetString()
            .Should().Be("PreserveNewest");
    }

    /// <summary>
    /// 以固定字元預算排空子程序輸出；超量即終止整個程序樹，避免建置診斷無界保留記憶體。
    /// 呼叫端同時排空 stdout／stderr，並在逾時或結束時等待程序及兩個讀取工作完成。
    /// </summary>
    private static async Task<string> ReadBoundedProcessOutputAsync(
        StreamReader reader,
        Process process,
        CancellationToken cancellationToken)
    {
        const int maximumCharacters = 16 * 1024 * 1024;
        var buffer = new char[8192];
        var output = new StringBuilder(capacity: 64 * 1024);
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0) return output.ToString();
            if (output.Length > maximumCharacters - count)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                }
                throw new InvalidDataException("MSBuild output exceeded the bounded test budget.");
            }
            output.Append(buffer, 0, count);
        }
    }

    /// <summary>
    /// 保護 MVC Area view discovery：使用正式 ChurchReport compiled application part、
    /// Area route values 與 CompositeViewEngine 查找 Terms，斷言命中的實際 Razor view 路徑。
    /// 測試只建立短生命週期離線 service provider，不執行 Host、不建立外部連線。
    /// </summary>
    [Fact]
    public void Area_Views_ResolvedUnderChurchSettingsArea()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        using var diagnosticListener = new DiagnosticListener("ChurchSettingsViewDiscoveryTest");
        services.AddSingleton(diagnosticListener);
        services.AddSingleton<DiagnosticSource>(diagnosticListener);
        services.AddControllersWithViews()
            .AddApplicationPart(typeof(ChurchSettingsController).Assembly);
        using var provider = services.BuildServiceProvider();
        var controllerAction = provider.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Single(descriptor =>
                descriptor.ControllerTypeInfo.AsType() == typeof(ChurchSettingsController)
                && descriptor.ActionName == nameof(ChurchSettingsController.Terms));
        controllerAction.RouteValues.Should().ContainKey("area")
            .WhoseValue.Should().Be("ChurchSettings");
        var routeData = new RouteData();
        routeData.Values["area"] = controllerAction.RouteValues["area"];
        routeData.Values["controller"] = controllerAction.ControllerName;
        routeData.Values["action"] = controllerAction.ActionName;
        var actionContext = new ActionContext(
            new DefaultHttpContext { RequestServices = provider },
            routeData,
            controllerAction);
        var engine = provider.GetRequiredService<ICompositeViewEngine>();

        var result = engine.FindView(actionContext, "Terms", isMainPage: false);

        result.Success.Should().BeTrue(
            "the compiled Area view must be discoverable from the ChurchSettings area route");
        result.View.Should().NotBeNull();
        ((RazorView)result.View!).RazorPage.Path.Replace('\\', '/')
            .Should().Be("/Areas/ChurchSettings/Views/ChurchSettings/Terms.cshtml");
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

    /// <summary>
    /// 保護奉獻類別停用契約：未勾選 checkbox 時仍須送出明確 false，避免 MVC 將缺值
    /// 綁定成 action 的 true 預設值。以實際 Razor 表單欄位順序做離線回歸檢查。
    /// </summary>
    [Fact]
    public void OfferingCategories_UncheckedActiveFlag_SubmitsFalseValue()
    {
        var viewPath = Path.Combine(AreaPath, "Views", "ChurchSettings", "OfferingCategories.cshtml");
        var view = Read(viewPath);
        const string checkbox = "<input name=\"isActive\" type=\"checkbox\" value=\"true\"";
        const string hiddenFalse = "<input name=\"isActive\" type=\"hidden\" value=\"false\" />";

        var checkboxIndex = view.IndexOf(checkbox, StringComparison.Ordinal);
        var hiddenFalseIndex = view.IndexOf(hiddenFalse, StringComparison.Ordinal);

        checkboxIndex.Should().BeGreaterThanOrEqualTo(0);
        hiddenFalseIndex.Should().BeGreaterThan(checkboxIndex,
            "checked=true must precede the fallback false value for MVC's first-value binding");
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

    /// <summary>Controller 必須宣告實際 View 所屬 Area，否則 Razor 只搜尋非 Area 位置。</summary>
    [Fact]
    public void Controller_DeclaresChurchSettingsArea()
    {
        var area = typeof(ChurchSettingsController).GetCustomAttribute<AreaAttribute>();

        area.Should().NotBeNull();
        area!.RouteValue.Should().Be("ChurchSettings");
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

    /// <summary>
    /// 使用正式 DI 容器驗證 scoped filter 的建構接點；不建立 Host 或外部連線，
    /// 避免直接 new 的測試掩蓋 Development ValidateOnBuild 無法使用非公開建構子的失敗。
    /// provider 與 scope 均由本案同步釋放，合成 access 仍由 fixture 擁有。
    /// </summary>
    [Fact]
    public void CultureFilter_DependencyInjection_CanConstructScopedFilter()
    {
        using var fixture = new ControllerFixture(jobTitle: "牧養主任", churchId: null);
        var services = new ServiceCollection();
        services.AddScoped<ChurchSettingsAccess>(_ => fixture.Access);
        services.AddScoped<ChurchSettingsCultureFilter>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ChurchSettingsCultureFilter>().Should().NotBeNull();
    }

    /// <summary>
    /// 保護 ChurchSettings 一次請求內的語系套用與還原：管理員 A 使用 qps-ploc 後，
    /// 管理員 B 的下一個 zh-TW request 必須維持中文資源且 ambient culture 回復原值。
    /// 透過同一個 resource filter 與合成 CRM 回應執行，不啟動 Host 或外部連線。
    /// </summary>
    [Fact]
    public async Task CultureFilter_QpsPlocThenZhTw_UsesRequestCultureAndRestoresAmbientState()
    {
        using var fixture = new ControllerFixture(
            jobTitle: "牧養主任",
            churchId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            preferredLanguage: "qps-ploc",
            enabledCultures: "zh-TW,qps-ploc");
        var filter = new ChurchSettingsCultureFilter(fixture.Access);
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        var previousUiCulture = System.Globalization.CultureInfo.CurrentUICulture;

        var pseudoText = await RenderCultureAsync(filter, fixture.Controller.HttpContext);
        fixture.SetPreferredLanguage("zh-TW");
        var zhTwContext = new DefaultHttpContext { User = fixture.Controller.HttpContext.User };
        var zhTwText = await RenderCultureAsync(filter, zhTwContext);

        pseudoText.Should().StartWith("［").And.EndWith("］");
        zhTwText.Should().Be("教會名詞");
        System.Globalization.CultureInfo.CurrentCulture.Should().Be(previousCulture);
        System.Globalization.CultureInfo.CurrentUICulture.Should().Be(previousUiCulture);
    }

    /// <summary>
    /// 403 不可因權限不足而丟失已解析的登入者語系；只有教會／語系無法解析時才回退繁中。
    /// 使用非管理員與無教會兩種合成 CRM 狀態，並斷言 resource filter 不建立授權 scope。
    /// </summary>
    [Fact]
    public async Task CultureFilter_ForbiddenUsesMemberCulture_AndFallsBackWhenUnavailable()
    {
        using var forbiddenFixture = new ControllerFixture(
            jobTitle: "會計",
            churchId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            preferredLanguage: "qps-ploc",
            enabledCultures: "zh-TW,qps-ploc");
        var filter = new ChurchSettingsCultureFilter(forbiddenFixture.Access);
        string? forbiddenText = null;
        await filter.InvokeAsync(forbiddenFixture.Controller.HttpContext, () =>
        {
            forbiddenText = ChurchSettingsText.GetText("ChurchSettings.Forbidden");
            forbiddenFixture.Controller.HttpContext.Items[ChurchSettingsRequestResolution.ItemKey]
                .Should().BeOfType<ChurchSettingsRequestResolution>().Which.Scope.Should().BeNull();
            return Task.CompletedTask;
        });

        using var missingChurchFixture = new ControllerFixture(
            jobTitle: "牧養主任",
            churchId: null,
            preferredLanguage: "qps-ploc",
            enabledCultures: "zh-TW,qps-ploc");
        string? fallbackText = null;
        await new ChurchSettingsCultureFilter(missingChurchFixture.Access)
            .InvokeAsync(missingChurchFixture.Controller.HttpContext, () =>
            {
                fallbackText = ChurchSettingsText.GetText("ChurchSettings.Forbidden");
                return Task.CompletedTask;
            });

        forbiddenText.Should().StartWith("［").And.EndWith("］");
        fallbackText.Should().Be("沒有權限檢視此頁面。");
    }

    private static async Task<string> RenderCultureAsync(
        ChurchSettingsCultureFilter filter,
        HttpContext httpContext)
    {
        string? text = null;
        await filter.InvokeAsync(httpContext, () =>
        {
            text = ChurchSettingsText.GetText("ChurchSettings.TermsTitle");
            return Task.CompletedTask;
        });
        return text ?? throw new InvalidOperationException("The synthetic request did not render its resource text.");
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
            IReadOnlyList<ChurchSettingsTermOverride>? termOverrides = null,
            ChurchTextOverrideCache? overrideCache = null,
            ITextCatalog? textCatalog = null,
            ISystemTextCatalog? systemTextCatalog = null,
            string preferredLanguage = "zh-TW",
            string enabledCultures = "zh-TW")
        {
            var service = DispatchProxy.Create<IOrganizationService, ChurchSettingsSdkProxy>();
            sdk = (ChurchSettingsSdkProxy)(object)service;
            sdk.ContactJobTitle = jobTitle;
            sdk.ChurchId = churchId;
            sdk.PreferredLanguage = preferredLanguage;
            sdk.EnabledCultures = enabledCultures;

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("CrmConnection:Organization", "synthetic-test-org")
                })
                .Build();
            var access = new ChurchSettingsAccess(
                service,
                configuration,
                new MemberCultureResolver(new LocalizationOptions(allowPseudoLocale: true)),
                overrideCache);
            Access = access;
            SettingsStore = new ControllerSettingsStore(categories, auditRecords, termOverrides);
            var settings = new ChurchSettingsService(
                SettingsStore,
                new ControllerCacheInvalidator(allowAuthorizedWrites),
                GoodShepherdTextCatalog.CreateOverrideValidator("這是一個長度剛好十二字的教會"),
                new ControllerClock());
            textCatalog ??= DispatchProxy.Create<ITextCatalog, ControllerTextCatalogProxy>();
            systemTextCatalog ??= DispatchProxy.Create<ISystemTextCatalog, ControllerSystemTextCatalogProxy>();
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
        internal void SetPreferredLanguage(string culture) => sdk.PreferredLanguage = culture;

        public void Dispose()
        {
            sdk.ContactJobTitle = null;
            sdk.ChurchId = null;
        }
    }

    /// <summary>
    /// 只為 Terms GET 提供一筆固定合成覆寫並計數；不保存 TextContext、使用者或外部資源，
    /// 讓未先載入快照的 catalog 呼叫以正式 cache 契約明確失敗。
    /// </summary>
    private sealed class TermsPreviewOverrideSource(Guid churchId, string text) : IChurchTextOverrideSource
    {
        internal int Reads { get; private set; }

        public Task<IReadOnlyList<ChurchTextOverride>> ReadAsync(
            TextContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            IReadOnlyList<ChurchTextOverride> rows =
            [
                new ChurchTextOverride(
                    Guid.Parse("77777777-7777-7777-7777-777777777777"),
                    churchId,
                    new TextKey("Role.Member"),
                    "zh-TW",
                    text)
            ];
            return Task.FromResult(rows);
        }
    }

    /// <summary>只提供本案例登入 contact；任何非預期 CRM 操作均失敗，不模擬外部狀態。</summary>
    public class ChurchSettingsSdkProxy : DispatchProxy
    {
        internal static readonly Guid ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        internal string? ContactJobTitle { get; set; }
        internal Guid? ChurchId { get; set; }
        internal string PreferredLanguage { get; set; } = "zh-TW";
        internal string EnabledCultures { get; set; } = "zh-TW";

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
                        ["new_enabledlanguages"] = EnabledCultures
                    };
                }
                if (entityName == "contact")
                {
                    var contact = new Entity("contact", ContactId)
                    {
                        ["new_church_jobtitle"] = ContactJobTitle,
                        ["new_preferredlanguage"] = PreferredLanguage
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
