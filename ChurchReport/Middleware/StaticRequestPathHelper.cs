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
