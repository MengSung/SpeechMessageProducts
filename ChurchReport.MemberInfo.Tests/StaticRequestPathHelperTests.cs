// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/StaticRequestPathHelperTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class StaticRequestPathHelperTests
// 主要成員：IsStaticAssetPath_AllowsLegitimateStaticAssets、IsStaticAssetPath_DoesNotAllowDynamicRoutesThatLookStatic、HasStaticAssetExtension_DetectsStaticLookingDynamicRoutes、WebCacheDeceptionMiddleware_BlocksDynamicRouteWithStaticExtension、WebCacheDeceptionMiddleware_PassesLegitimateStaticAsset
// 引用命名空間：System.Threading.Tasks、ChurchReport.Middleware、FluentAssertions、Microsoft.AspNetCore.Http、Microsoft.Extensions.Logging.Abstractions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Threading.Tasks;
using ChurchReport.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class StaticRequestPathHelperTests
{
    [Theory]
    [InlineData("/css/site.css")]
    [InlineData("/js/site.js")]
    [InlineData("/lib/bootstrap/dist/css/bootstrap.min.css")]
    [InlineData("/assets/images/logo.png")]
    [InlineData("/images/member.jpg")]
    [InlineData("/img/avatar.webp")]
    [InlineData("/fonts/site.woff2")]
    [InlineData("/_framework/blazor.webassembly.js")]
    [InlineData("/favicon.ico")]
    public void IsStaticAssetPath_AllowsLegitimateStaticAssets(string path)
    {
        StaticRequestPathHelper.IsStaticAssetPath(new PathString(path)).Should().BeTrue();
    }

    [Theory]
    [InlineData("/Home/ProcessLogin/fake.css")]
    [InlineData("/FeeManagement/LessonList/fake.js")]
    [InlineData("/Equipment/LoadEquipmentStorLessons/fake.png")]
    [InlineData("/Personal/InfomationView/photo.jpg")]
    [InlineData("/imageserver/photo.png")]
    [InlineData("/css")]
    public void IsStaticAssetPath_DoesNotAllowDynamicRoutesThatLookStatic(string path)
    {
        StaticRequestPathHelper.IsStaticAssetPath(new PathString(path)).Should().BeFalse();
    }

    [Theory]
    [InlineData("/Home/ProcessLogin/fake.css")]
    [InlineData("/FeeManagement/LessonList/fake.js")]
    [InlineData("/Equipment/LoadEquipmentStorLessons/fake.png")]
    public void HasStaticAssetExtension_DetectsStaticLookingDynamicRoutes(string path)
    {
        StaticRequestPathHelper.HasStaticAssetExtension(new PathString(path)).Should().BeTrue();
    }

    [Fact]
    public async Task WebCacheDeceptionMiddleware_BlocksDynamicRouteWithStaticExtension()
    {
        var nextCalled = false;
        var middleware = new WebCacheDeceptionMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<WebCacheDeceptionMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/Home/ProcessLogin/fake.css";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        context.Response.Headers["Cache-Control"].ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task WebCacheDeceptionMiddleware_PassesLegitimateStaticAsset()
    {
        var nextCalled = false;
        var middleware = new WebCacheDeceptionMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<WebCacheDeceptionMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/css/site.css";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
