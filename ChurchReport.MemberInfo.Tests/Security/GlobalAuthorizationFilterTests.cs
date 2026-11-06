/*
 * 檔案整體說明
 *
 * 目的與定位：驗證匿名端點、受保護端點、一般未登入轉址及AJAX未登入401的授權filter契約。
 *
 * 主要功能與流程：
 * 本檔宣告 GlobalAuthorizationFilterTests、Fakes。
 * 測試案例入口包含 AnonAction、SecureAction、Config、MakeContext、Unauthenticated_SecureAction_RedirectsToLogin、
 * Unauthenticated_Ajax_Returns401、Authenticated_SecureAction_IsAllowed、
 * AnonymousAction_IsAllowed_EvenWhenUnauthenticated；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using ChurchReport.Filters;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security
{
    public class GlobalAuthorizationFilterTests
    {
        private class Fakes
        {
            [AllowAnonymous]
            public void AnonAction()
            {
            }

            public void SecureAction()
            {
            }
        }

        private static IConfiguration Config(bool? enforce, bool? sessionFallback = false)
        {
            var dict = new Dictionary<string, string>();
            if (enforce.HasValue)
            {
                dict["Security:EnforceGlobalAuthorization"] = enforce.Value.ToString();
            }

            if (sessionFallback.HasValue)
            {
                dict["Security:AllowSessionIdentityFallback"] = sessionFallback.Value.ToString();
            }

            return new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
        }

        private static AuthorizationFilterContext MakeContext(bool authenticated, bool allowAnonymous, bool ajax)
        {
            var http = new DefaultHttpContext();
            http.User = authenticated
                ? new ClaimsPrincipal(new ClaimsIdentity("cookie"))
                : new ClaimsPrincipal(new ClaimsIdentity());

            if (ajax)
            {
                http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
            }

            var method = typeof(Fakes).GetMethod(allowAnonymous ? nameof(Fakes.AnonAction) : nameof(Fakes.SecureAction));
            var descriptor = new ControllerActionDescriptor
            {
                MethodInfo = method!,
                ControllerTypeInfo = typeof(Fakes).GetTypeInfo()
            };
            var actionContext = new ActionContext(http, new RouteData(), descriptor);
            return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
        }

        [Fact]
        public async Task Unauthenticated_SecureAction_RedirectsToLogin()
        {
            var context = MakeContext(authenticated: false, allowAnonymous: false, ajax: false);

            await new GlobalAuthorizationFilter(Config(true)).OnAuthorizationAsync(context);

            context.Result.Should().BeOfType<RedirectToActionResult>();
        }

        [Fact]
        public async Task Unauthenticated_Ajax_Returns401()
        {
            var context = MakeContext(authenticated: false, allowAnonymous: false, ajax: true);

            await new GlobalAuthorizationFilter(Config(true)).OnAuthorizationAsync(context);

            context.Result.Should().BeOfType<StatusCodeResult>()
                .Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        [Fact]
        public async Task Authenticated_SecureAction_IsAllowed()
        {
            var context = MakeContext(authenticated: true, allowAnonymous: false, ajax: false);

            await new GlobalAuthorizationFilter(Config(true)).OnAuthorizationAsync(context);

            context.Result.Should().BeNull();
        }

        [Fact]
        public async Task AnonymousAction_IsAllowed_EvenWhenUnauthenticated()
        {
            var context = MakeContext(authenticated: false, allowAnonymous: true, ajax: false);

            await new GlobalAuthorizationFilter(Config(true)).OnAuthorizationAsync(context);

            context.Result.Should().BeNull();
        }

        [Fact]
        public async Task EnforcementDisabled_AllowsEverything()
        {
            var context = MakeContext(authenticated: false, allowAnonymous: false, ajax: false);

            await new GlobalAuthorizationFilter(Config(false)).OnAuthorizationAsync(context);

            context.Result.Should().BeNull();
        }
    }
}
