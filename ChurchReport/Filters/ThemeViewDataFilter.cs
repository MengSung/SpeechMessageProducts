// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Filters/ThemeViewDataFilter.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 ThemeViewDataFilter 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class ThemeViewDataFilter
// 主要成員：OnActionExecuting、ResolveThemeName、MapThemeCssClass、OnActionExecuted
// 引用命名空間：ChurchReport.Services.Theme、Microsoft.AspNetCore.Mvc、Microsoft.AspNetCore.Mvc.Filters、Microsoft.Extensions.Configuration、System、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
