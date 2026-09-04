// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Middleware/StaticRequestPathHelper.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 StaticRequestPathHelper 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class StaticRequestPathHelper
// 主要成員：IsStaticAssetPath、HasStaticAssetExtension
// 引用命名空間：System、System.Collections.Generic、Microsoft.AspNetCore.Http
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace ChurchReport.Middleware
{
    /// <summary>
    /// Shared static asset path classifier for logging/profiling noise reduction.
    /// This helper must not be used to bypass authentication or session validation.
    /// </summary>
    public static class StaticRequestPathHelper
    {
        private static readonly HashSet<string> StaticFileExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".css",
                ".js",
                ".map",
                ".png",
                ".jpg",
                ".jpeg",
                ".gif",
                ".svg",
                ".ico",
                ".woff",
                ".woff2",
                ".ttf",
                ".eot",
                ".json",
                ".xml",
                ".txt",
                ".pdf",
                ".webp",
                ".avif",
                ".bmp",
                ".tiff"
            };

        private static readonly string[] StaticPathPrefixes =
        {
            "/css/",
            "/js/",
            "/lib/",
            "/assets/",
            "/images/",
            "/img/",
            "/fonts/",
            "/_framework/",
            "/devextreme/",
            "/.well-known/"
        };

        private static readonly string[] ExactStaticPaths =
        {
            "/favicon.ico"
        };

        public static bool IsStaticAssetPath(PathString path)
        {
            var value = path.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            // 靜態分流只接受沒有路徑穿越片段的 URL；否則後續若由動態路由處理，
            // 上游可能已先套用公共長效快取標頭，形成跨使用者回應快取風險。
            if (ContainsPathTraversal(value))
            {
                return false;
            }

            foreach (var exactPath in ExactStaticPaths)
            {
                if (string.Equals(value, exactPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!HasStaticAssetExtension(path))
            {
                return false;
            }

            foreach (var prefix in StaticPathPrefixes)
            {
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 判斷 URL 是否包含明文或百分比編碼的路徑穿越片段。
        /// </summary>
        /// <param name="value">待檢查的 request path。</param>
        /// <returns>含有 <c>.</c>、<c>..</c> 或反斜線分隔片段時回傳 true。</returns>
        /// <remarks>
        /// 先解碼再分段可攔截 <c>%2e%2e</c>、編碼斜線與混合大小寫形式；
        /// 解碼失敗採 fail closed，避免把不明路徑當成可長效快取的靜態資源。
        /// </remarks>
        private static bool ContainsPathTraversal(string value)
        {
            if (value.IndexOf('\\') >= 0)
            {
                return true;
            }

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return true;
            }

            decoded = decoded.Replace('\\', '/');
            var segments = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (segment == "." || segment == "..")
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasStaticAssetExtension(PathString path)
        {
            var value = path.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var queryStart = value.IndexOf('?', StringComparison.Ordinal);
            if (queryStart >= 0)
            {
                value = value.Substring(0, queryStart);
            }

            var lastSlash = value.LastIndexOf('/');
            var lastSegment = lastSlash >= 0 ? value.Substring(lastSlash + 1) : value;
            if (string.IsNullOrWhiteSpace(lastSegment))
            {
                return false;
            }

            var dotIndex = lastSegment.LastIndexOf('.');
            if (dotIndex <= 0 || dotIndex == lastSegment.Length - 1)
            {
                return false;
            }

            var extension = lastSegment.Substring(dotIndex);
            return StaticFileExtensions.Contains(extension);
        }
    }
}
