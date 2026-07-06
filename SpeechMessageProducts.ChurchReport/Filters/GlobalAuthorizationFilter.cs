using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;

namespace ChurchReport.Filters
{
    public sealed class GlobalAuthorizationFilter : IAsyncAuthorizationFilter
    {
        private readonly IConfiguration _configuration;

        public GlobalAuthorizationFilter(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var enforce = _configuration.GetValue<bool?>("Security:EnforceGlobalAuthorization") ?? true;
            if (!enforce || AllowsAnonymous(context) || IsAuthenticated(context.HttpContext))
            {
                return Task.CompletedTask;
            }

            var allowSessionFallback = _configuration.GetValue<bool?>("Security:AllowSessionIdentityFallback") ?? true;
            if (allowSessionFallback && HasServerSessionIdentity(context.HttpContext))
            {
                return Task.CompletedTask;
            }

            context.Result = IsAjax(context.HttpContext.Request)
                ? new StatusCodeResult(StatusCodes.Status401Unauthorized)
                : new RedirectToActionResult("Login", "Authentication", null);

            return Task.CompletedTask;
        }

        private static bool AllowsAnonymous(AuthorizationFilterContext context)
        {
            if (context.ActionDescriptor is ControllerActionDescriptor descriptor)
            {
                var methodAllowsAnonymous = descriptor.MethodInfo.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any();
                var controllerAllowsAnonymous = descriptor.ControllerTypeInfo.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any();

                if (methodAllowsAnonymous || controllerAllowsAnonymous)
                {
                    return true;
                }
            }

            return context.Filters.OfType<IAllowAnonymousFilter>().Any();
        }

        private static bool IsAuthenticated(HttpContext httpContext)
        {
            return httpContext.User?.Identity?.IsAuthenticated == true;
        }

        private static bool HasServerSessionIdentity(HttpContext httpContext)
        {
            try
            {
                return !string.IsNullOrEmpty(httpContext.Session.GetString("_SessionUserId"))
                    || !string.IsNullOrEmpty(httpContext.Session.GetString("_LoginPassword"));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAjax(HttpRequest request)
        {
            if (string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        }
    }
}
