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
