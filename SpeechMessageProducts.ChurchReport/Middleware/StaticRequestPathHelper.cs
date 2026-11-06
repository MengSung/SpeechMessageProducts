/*
 * 檔案整體說明
 *
 * 目的與定位：分類已知靜態資產路徑，降低追蹤／剖析噪音並拒絕穿越片段；分類結果不能用來略過認證或Session驗證，也不能把相似前綴的動態路由當公共資產。
 *
 * 主要功能與流程：
 * 本檔宣告 StaticRequestPathHelper。
 * 主要操作入口包含 IsStaticAssetPath、ContainsPathTraversal、HasStaticAssetExtension。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Middleware/IdentityAuditMiddleware.cs、
 * SpeechMessageProducts.ChurchReport/Middleware/MiniAppDetectionMiddleware.cs、
 * SpeechMessageProducts.ChurchReport/Middleware/PerfProfilingMiddleware.cs、
 * SpeechMessageProducts.ChurchReport/Middleware/PerformanceMonitoringMiddleware.cs、
 * SpeechMessageProducts.ChurchReport/Middleware/SessionValidationMiddleware.cs 追查目前工作樹中的合作／使用位置。
 */
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
