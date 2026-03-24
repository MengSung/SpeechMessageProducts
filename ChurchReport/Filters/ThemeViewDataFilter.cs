using ChurchReport.Services.Theme;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace ChurchReport.Filters
{
    /// <summary>
    /// 將 Theme 設定注入到 ViewData，供 Layout 與各頁面統一使用。
    /// </summary>
    public sealed class ThemeViewDataFilter : IActionFilter
    {
        private readonly ThemeSettings _themeSettings;
        private readonly IConfiguration _configuration;

        private static readonly HashSet<string> AllowedThemes = new HashSet<string>(StringComparer.Ordinal)
        {
            "藍色",
            "橘色",
            "綠色",
            "粉紅色"
        };

        public ThemeViewDataFilter(ThemeSettings themeSettings, IConfiguration configuration)
        {
            _themeSettings = themeSettings;
            _configuration = configuration;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (context.Controller is Controller controller)
            {
                var configuredTheme = ResolveThemeName(_configuration["Theme:Current"]);
                controller.ViewData["ThemeName"] = configuredTheme;
                controller.ViewData["ThemeCssClass"] = MapThemeCssClass(configuredTheme);
            }
        }

        private string ResolveThemeName(string configuredTheme)
        {
            var normalizedTheme = configuredTheme?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedTheme) || !AllowedThemes.Contains(normalizedTheme))
            {
                return _themeSettings.CurrentTheme;
            }

            return normalizedTheme;
        }

        private static string MapThemeCssClass(string themeName)
        {
            switch (themeName)
            {
                case "橘色":
                    return "theme-orange";
                case "綠色":
                    return "theme-green";
                case "粉紅色":
                    return "theme-pink";
                case "藍色":
                default:
                    return "theme-blue";
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
