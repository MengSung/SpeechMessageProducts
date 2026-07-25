// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/DiagnosticsController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class DiagnosticsController
// 主要成員：Index、GetSessionInfo、GetIdentityAudit、GetPerformanceInfo、ResetAudit、GetCacheHeaders、AdfsTokenProbe
// 引用命名空間：Microsoft.AspNetCore.Authorization、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Configuration、System、System.Collections.Generic、System.Diagnostics、System.IO、System.Linq、System.Net.Http、System.Net.Http.Headers、System.Text.Json、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

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
    /// 提供診斷端點，用於檢查 Session、效能、身份審計與 ADFS token 探測
    ///
    /// 安全性:
    /// - 僅在 DEBUG 模式下可用
    /// - 僅允許已登入使用者存取
    /// - 生產環境不應包含此控制器
    ///
    /// 使用方式:
    /// - GET /diagnostics/session - 查看當前 Session 資訊
    /// - GET /diagnostics/identity-audit - 查看身份審計追蹤資料
    /// - GET /diagnostics/performance - 查看效能統計
    /// - POST /diagnostics/reset-audit - 重設身份審計資料
    /// - GET /diagnostics/adfs-token-probe - ADFS token + WhoAmI（結果寫 Logs）
    /// </summary>
#if DEBUG
    [Authorize]
    [Route("diagnostics")]
    public class DiagnosticsController : Controller
    {
        private readonly IConfiguration _configuration;

        public DiagnosticsController(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// 首頁：診斷工具總覽
        /// </summary>
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
                    new { Endpoint = "/diagnostics/cache-headers", Description = "測試快取標頭設定" },
                    new { Endpoint = "/diagnostics/adfs-token-probe", Description = "ADFS token + WhoAmI 探測（結果寫入 Logs）" }
                }
            };

            return Json(diagnosticsInfo);
        }

        /// <summary>
        /// 查看當前 Session 資訊
        /// </summary>
        [HttpGet("session")]
        public IActionResult GetSessionInfo()
        {
            var sessionId = HttpContext.Session.Id;
            var sessionKeys = new List<string>();
            var sessionData = new Dictionary<string, string>();

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
        /// </summary>
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
        /// </summary>
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
        /// </summary>
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
        /// ADFS OAuth token + Web API WhoAmI 探測（DEBUG only）。
        ///
        /// 保姆級教學：
        /// 1. 請先在 ChurchReport 登入成功（此 action 需要 [Authorize]）。
        /// 2. 瀏覽器直接開啟：/diagnostics/adfs-token-probe
        /// 3. 畫面會顯示 JSON；同時寫入 Logs/adfs-token-probe-latest.json
        /// 4. 密碼不會寫進結果檔。
        /// 5. 目的：用 VS IIS Express 身分探測 jesus IFD，你不必手動跑 PowerShell。
        /// </summary>
        [HttpGet("adfs-token-probe")]
        public async Task<IActionResult> AdfsTokenProbe()
        {
            var authority = _configuration["DynamicsAccess:Embedded:AuthorityUri"]
                ?? "https://speechmessagests.speechmessage.com.tw/adfs";
            var resource = _configuration["DynamicsAccess:Embedded:ResourceUri"]
                ?? "https://jesus.speechmessage.com.tw/";
            var clientId = _configuration["DynamicsAccess:Embedded:ClientId"]
                ?? "2ad88395-b77d-4561-9441-d0e40824f9bc";
            var whoAmI = (_configuration["DynamicsAccess:Embedded:OrganizationWebApiBaseUri"]
                ?? "https://jesus.speechmessage.com.tw/api/data/v8.2/").TrimEnd('/') + "/WhoAmI";

            var userName = _configuration["CrmConnection:Username"] ?? string.Empty;
            var password = _configuration["CrmConnection:Password"] ?? string.Empty;

            var result = new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["stage"] = "init",
                ["serverTime"] = DateTime.Now.ToString("o"),
                ["processUser"] = Environment.UserName,
                ["authority"] = authority,
                ["resource"] = resource,
                ["clientId"] = clientId,
                ["whoAmI"] = whoAmI,
                ["username"] = userName,
                ["passwordPresent"] = !string.IsNullOrEmpty(password)
            };

            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
            {
                result["stage"] = "credentials";
                result["error"] = "CrmConnection Username/Password missing in appsettings.";
                await WriteProbeResultAsync(result).ConfigureAwait(false);
                return Json(result);
            }

            var tokenUrl = authority.TrimEnd('/') + "/oauth2/token";
            result["tokenUrl"] = tokenUrl;

            try
            {
                using var http = new HttpClient(new SocketsHttpHandler
                {
                    UseCookies = false,
                    AllowAutoRedirect = false,
                    UseProxy = false
                })
                {
                    Timeout = TimeSpan.FromSeconds(30)
                };

                using var tokenContent = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["resource"] = resource,
                    ["grant_type"] = "password",
                    ["username"] = userName,
                    ["password"] = password
                });

                using var tokenResponse = await http.PostAsync(tokenUrl, tokenContent).ConfigureAwait(false);
                var tokenBody = await tokenResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                result["tokenHttpStatus"] = (int)tokenResponse.StatusCode;

                if (!tokenResponse.IsSuccessStatusCode)
                {
                    result["stage"] = "token";
                    result["error"] = "ADFS token failed HTTP " + (int)tokenResponse.StatusCode;
                    result["bodyPreview"] = tokenBody.Length <= 400 ? tokenBody : tokenBody.Substring(0, 400);
                    await WriteProbeResultAsync(result).ConfigureAwait(false);
                    return Json(result);
                }

                using var tokenDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(tokenBody) ? "{}" : tokenBody);
                if (!tokenDoc.RootElement.TryGetProperty("access_token", out var accessNode) ||
                    accessNode.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(accessNode.GetString()))
                {
                    result["stage"] = "token";
                    result["error"] = "ADFS response missing access_token.";
                    result["bodyPreview"] = tokenBody.Length <= 400 ? tokenBody : tokenBody.Substring(0, 400);
                    await WriteProbeResultAsync(result).ConfigureAwait(false);
                    return Json(result);
                }

                var accessToken = accessNode.GetString()!;
                result["tokenAcquired"] = true;
                if (tokenDoc.RootElement.TryGetProperty("expires_in", out var expNode))
                {
                    result["expiresIn"] = expNode.ToString();
                }

                using var whoRequest = new HttpRequestMessage(HttpMethod.Get, whoAmI);
                whoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                whoRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                whoRequest.Headers.TryAddWithoutValidation("OData-Version", "4.0");
                whoRequest.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");

                using var whoResponse = await http.SendAsync(whoRequest).ConfigureAwait(false);
                var whoBody = await whoResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                result["whoAmIHttpStatus"] = (int)whoResponse.StatusCode;
                result["stage"] = "whoami";

                if (!whoResponse.IsSuccessStatusCode)
                {
                    result["ok"] = false;
                    result["error"] = "WhoAmI failed HTTP " + (int)whoResponse.StatusCode;
                    result["bodyPreview"] = whoBody.Length <= 400 ? whoBody : whoBody.Substring(0, 400);
                    if (whoResponse.Headers.Location is not null)
                    {
                        result["location"] = whoResponse.Headers.Location.ToString();
                    }

                    await WriteProbeResultAsync(result).ConfigureAwait(false);
                    return Json(result);
                }

                result["ok"] = true;
                result["whoAmIBody"] = whoBody;
                result["nextStep"] = "Set DynamicsAccess:Package01FeeReadsEnabled=true and retest fee list Returned=56";
                await WriteProbeResultAsync(result).ConfigureAwait(false);
                return Json(result);
            }
            catch (Exception ex)
            {
                result["stage"] = "exception";
                result["error"] = ex.GetType().Name + ": " + ex.Message;
                if (ex.InnerException is not null)
                {
                    result["innerError"] = ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;
                }

                await WriteProbeResultAsync(result).ConfigureAwait(false);
                return Json(result);
            }
        }

        /// <summary>
        /// 把探測結果寫到 Logs，方便 Codex 直接讀檔，不必請你複製輸出。
        /// </summary>
        private static async Task WriteProbeResultAsync(IDictionary<string, object?> result)
        {
            try
            {
                var candidates = new List<string>();

                // 1) VS 專案 Logs（內容根附近）
                candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Logs")));
                // 2) 目前工作目錄 Logs
                candidates.Add(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Logs")));
                candidates.Add(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "SpeechMessageProducts.ChurchReport", "Logs")));
                // 3) 輸出目錄 Logs
                candidates.Add(Path.Combine(AppContext.BaseDirectory, "Logs"));

                string? logsDir = null;
                foreach (var candidate in candidates)
                {
                    try
                    {
                        Directory.CreateDirectory(candidate);
                        logsDir = candidate;
                        break;
                    }
                    catch
                    {
                        // try next
                    }
                }

                if (logsDir is null)
                {
                    result["resultFileError"] = "Unable to create Logs directory.";
                    return;
                }

                var path = Path.Combine(logsDir, "adfs-token-probe-latest.json");
                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(path, json).ConfigureAwait(false);
                result["resultFile"] = path;
                Trace.WriteLine("[ADFS-PROBE] wrote " + path + " ok=" + result["ok"] + " stage=" + result["stage"]);
            }
            catch (Exception writeEx)
            {
                result["resultFileError"] = writeEx.Message;
            }
        }

        /// <summary>
        /// 測試快取標頭設定
        /// </summary>
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