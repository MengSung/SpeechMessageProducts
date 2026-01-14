using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 診斷控制器 (Session Bleeding 防護 - 診斷工具)
    /// 
    /// 設計原則:
    /// - Single Responsibility Principle (SRP): 專注於提供診斷資訊
    /// - Open/Closed Principle: 透過 Action 方法擴展，不需修改現有代碼
    /// - Dependency Inversion Principle: 依賴抽象 (Controller base class)
    /// 
    /// 作用:
    /// 提供診斷端點，用於檢查 Session、效能、身份審計等資訊
    /// 
    /// 安全性:
    /// ?? 僅在 DEBUG 模式下可用
    /// ?? 僅允許已登入使用者存取
    /// ?? 生產環境不應包含此控制器
    /// 
    /// 使用方式:
    /// - GET /diagnostics/session - 查看當前 Session 資訊
    /// - GET /diagnostics/identity-audit - 查看身份審計追蹤資料
    /// - GET /diagnostics/performance - 查看效能統計
    /// - POST /diagnostics/reset-audit - 重設身份審計資料
    /// </summary>
#if DEBUG
    [Authorize]
    [Route("diagnostics")]
    public class DiagnosticsController : Controller
    {
        /// <summary>
        /// 首頁：診斷工具總覽
        /// </summary>
        /// <returns>診斷工具總覽頁面</returns>
        [HttpGet("")]
        public IActionResult Index()
        {
            var diagnosticsInfo = new
            {
                ServerTime = DateTime.Now,
                Environment = "DEBUG",
                User = User.Identity?.Name ?? "Anonymous",
                IsAuthenticated = User.Identity?.IsAuthenticated ?? false,
                AvailableEndpoints = new[]
                {
                    new { Endpoint = "/diagnostics/session", Description = "查看當前 Session 資訊" },
                    new { Endpoint = "/diagnostics/identity-audit", Description = "查看身份審計追蹤資料" },
                    new { Endpoint = "/diagnostics/performance", Description = "查看效能統計" },
                    new { Endpoint = "/diagnostics/reset-audit", Description = "重設身份審計資料 (POST)" },
                    new { Endpoint = "/diagnostics/cache-headers", Description = "測試快取標頭設定" }
                }
            };

            return Json(diagnosticsInfo);
        }

        /// <summary>
        /// 查看當前 Session 資訊
        /// 
        /// 功能:
        /// - 顯示 Session ID
        /// - 顯示 Session 中的所有 Key
        /// - 顯示 Session 的值
        /// - 顯示 Session Cookie 設定
        /// 
        /// 用途:
        /// - 診斷 Session 是否正確建立
        /// - 檢查 Session 資料是否正確
        /// - 驗證 Session Bleeding 是否發生
        /// </summary>
        /// <returns>Session 資訊 JSON</returns>
        [HttpGet("session")]
        public IActionResult GetSessionInfo()
        {
            var sessionId = HttpContext.Session.Id;
            var sessionKeys = new List<string>();
            
            // 取得 Session 中的所有 Key (需要反射或手動追蹤)
            // 注意：ASP.NET Core Session 不直接提供 Keys 列舉
            var sessionData = new Dictionary<string, string>();
            
            // 嘗試取得常見的 Session Key
            var commonKeys = new[] 
            { 
                "LoginTimestamp", 
                "CurrentAccount", 
                "CurrentUserId",
                "UserName",
                "UserId"
            };
            
            foreach (var key in commonKeys)
            {
                var value = HttpContext.Session.GetString(key);
                if (value != null)
                {
                    sessionKeys.Add(key);
                    sessionData[key] = value;
                }
            }

            var sessionInfo = new
            {
                SessionId = sessionId,
                IsAvailable = HttpContext.Session.IsAvailable,
                Keys = sessionKeys,
                Data = sessionData,
                User = User.Identity?.Name ?? "Anonymous",
                IsAuthenticated = User.Identity?.IsAuthenticated ?? false,
                RemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                TraceIdentifier = HttpContext.TraceIdentifier,
                CookieSettings = new
                {
                    Name = ".ChurchReport.Session",
                    HttpOnly = true,
                    Secure = true,
                    SameSite = "Strict"
                }
            };

            return Json(sessionInfo);
        }

        /// <summary>
        /// 查看身份審計追蹤資料
        /// 
        /// 功能:
        /// - 顯示當前追蹤的 IP 與使用者對應關係
        /// - 顯示最後活動時間
        /// - 偵測可能的 Session Bleeding
        /// 
        /// 用途:
        /// - 即時監控身份混淆問題
        /// - 診斷 Wi-Fi 環境下的使用者切換
        /// - 驗證防護機制是否有效
        /// </summary>
        /// <returns>身份審計資料 JSON</returns>
        [HttpGet("identity-audit")]
        public IActionResult GetIdentityAudit()
        {
            var trackingData = ChurchReport.Middleware.IdentityAuditMiddleware.GetTrackingSnapshot();
            
            var auditInfo = new
            {
                TotalTrackedIPs = trackingData.Count,
                TrackingData = trackingData.Select(kvp => new
                {
                    IP = kvp.Key,
                    LastUser = kvp.Value.LastUser,
                    LastSeen = kvp.Value.LastSeen,
                    TimeSinceLastSeen = DateTime.UtcNow - kvp.Value.LastSeen
                }).OrderByDescending(x => x.LastSeen),
                CurrentUser = User.Identity?.Name ?? "Anonymous",
                CurrentIP = HttpContext.Connection.RemoteIpAddress?.ToString(),
                ServerTime = DateTime.UtcNow
            };

            return Json(auditInfo);
        }

        /// <summary>
        /// 查看效能統計
        /// 
        /// 功能:
        /// - 顯示當前進程的記憶體使用量
        /// - 顯示執行緒數量
        /// - 顯示運行時間
        /// 
        /// 用途:
        /// - 監控應用程式效能
        /// - 偵測記憶體洩漏
        /// - 診斷效能問題
        /// </summary>
        /// <returns>效能統計 JSON</returns>
        [HttpGet("performance")]
        public IActionResult GetPerformanceInfo()
        {
            var process = Process.GetCurrentProcess();
            
            var performanceInfo = new
            {
                Memory = new
                {
                    WorkingSet = $"{process.WorkingSet64 / 1024 / 1024} MB",
                    PrivateMemory = $"{process.PrivateMemorySize64 / 1024 / 1024} MB",
                    VirtualMemory = $"{process.VirtualMemorySize64 / 1024 / 1024} MB",
                    GCMemory = $"{GC.GetTotalMemory(false) / 1024 / 1024} MB"
                },
                Threads = new
                {
                    Count = process.Threads.Count,
                    ThreadPoolThreads = System.Threading.ThreadPool.ThreadCount
                },
                Runtime = new
                {
                    ProcessorTime = process.TotalProcessorTime,
                    StartTime = process.StartTime,
                    UpTime = DateTime.Now - process.StartTime
                },
                GarbageCollection = new
                {
                    Gen0Collections = GC.CollectionCount(0),
                    Gen1Collections = GC.CollectionCount(1),
                    Gen2Collections = GC.CollectionCount(2)
                },
                ServerTime = DateTime.Now
            };

            return Json(performanceInfo);
        }

        /// <summary>
        /// 重設身份審計資料
        /// 
        /// 功能:
        /// - 清除所有身份審計追蹤資料
        /// - 釋放記憶體
        /// 
        /// 用途:
        /// - 手動清理測試資料
        /// - 重置監控狀態
        /// 
        /// ?? 注意：僅在必要時使用，會清除所有追蹤歷史
        /// </summary>
        /// <returns>重設結果 JSON</returns>
        [HttpPost("reset-audit")]
        [ValidateAntiForgeryToken]
        public IActionResult ResetAudit()
        {
            var removedCount = ChurchReport.Middleware.IdentityAuditMiddleware.CleanupOldTracking(TimeSpan.Zero);
            
            var result = new
            {
                Success = true,
                RemovedCount = removedCount,
                Message = $"已清除 {removedCount} 筆身份審計資料",
                ResetTime = DateTime.UtcNow
            };

            return Json(result);
        }

        /// <summary>
        /// 測試快取標頭設定
        /// 
        /// 功能:
        /// - 檢查回應的快取標頭是否正確
        /// - 驗證 Session Bleeding 防護是否啟用
        /// 
        /// 用途:
        /// - 快速驗證快取設定
        /// - 診斷快取問題
        /// </summary>
        /// <returns>快取標頭資訊 JSON</returns>
        [HttpGet("cache-headers")]
        public IActionResult GetCacheHeaders()
        {
            var headers = HttpContext.Response.Headers;
            
            var cacheInfo = new
            {
                ResponseHeaders = new
                {
                    CacheControl = headers.ContainsKey("Cache-Control") ? headers["Cache-Control"].ToString() : "Not Set",
                    Pragma = headers.ContainsKey("Pragma") ? headers["Pragma"].ToString() : "Not Set",
                    Expires = headers.ContainsKey("Expires") ? headers["Expires"].ToString() : "Not Set",
                    Vary = headers.ContainsKey("Vary") ? headers["Vary"].ToString() : "Not Set"
                },
                ExpectedHeaders = new
                {
                    CacheControl = "no-store, no-cache, must-revalidate, max-age=0",
                    Pragma = "no-cache",
                    Expires = "0 或 -1",
                    Vary = "Cookie"
                },
                ValidationResult = new
                {
                    CacheControlCorrect = headers.ContainsKey("Cache-Control") && 
                                         headers["Cache-Control"].ToString().Contains("no-store"),
                    PragmaCorrect = headers.ContainsKey("Pragma") && 
                                   headers["Pragma"].ToString() == "no-cache",
                    VaryCookieCorrect = headers.ContainsKey("Vary") && 
                                       headers["Vary"].ToString().Contains("Cookie")
                },
                ServerTime = DateTime.Now
            };

            return Json(cacheInfo);
        }
    }
#endif
}
