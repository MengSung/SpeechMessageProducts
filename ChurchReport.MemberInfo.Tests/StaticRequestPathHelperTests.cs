/*
 * 檔案整體說明
 *
 * 目的與定位：本檔承接以下操作邊界：靜態資源分類不得接受路徑穿越片段，避免動態路由在快取標頭分流時被誤判為公共資源。
 *
 * 主要功能與流程：
 * 本檔宣告 StaticRequestPathHelperTests。
 * - 靜態資源分類不得接受路徑穿越片段，避免動態路由在快取標頭分流時被誤判為公共資源。
 * 測試案例入口包含 IsStaticAssetPath_AllowsLegitimateStaticAssets、
 * IsStaticAssetPath_DoesNotAllowDynamicRoutesThatLookStatic、IsStaticAssetPath_rejects_path_traversal_segments、
 * HasStaticAssetExtension_DetectsStaticLookingDynamicRoutes、
 * WebCacheDeceptionMiddleware_BlocksDynamicRouteWithStaticExtension、
 * WebCacheDeceptionMiddleware_PassesLegitimateStaticAsset。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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

    /// <summary>
    /// 靜態資源分類不得接受路徑穿越片段，避免動態路由在快取標頭分流時被誤判為公共資源。
    /// </summary>
    [Theory]
    [InlineData("/css/../Home/Index.css")]
    [InlineData("/assets/./Home/Index.js")]
    [InlineData("/fonts/%2e%2e/Home/Index.woff2")]
    public void IsStaticAssetPath_rejects_path_traversal_segments(string path)
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
