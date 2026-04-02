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
            "粉紅色",
            "晨霧紫",
            "月光藍",
            "皇家紫金",
            "勃根地金",
            "陽光黃",
            "行道靛紫",
            "珊瑚橘"
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
                case "晨霧紫":
                    return "theme-mist-purple";
                case "月光藍":
                    return "theme-moon-blue";
                case "皇家紫金":
                    return "theme-royal-purple-gold";
                case "勃根地金":
                    return "theme-burgundy-gold";
                case "陽光黃":
                    return "theme-sunshine-yellow";
                case "珊瑚橘":
                    return "theme-coral-orange";
                case "行道靛紫":
                    return "theme-indigo-purple";
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
