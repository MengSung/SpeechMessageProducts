// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/DiagnosticsController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式
// 檔案責任：DEBUG 診斷端點（Session / 效能 / ADFS OAuth 授權碼與 token 探測）
// 主要型別：class DiagnosticsController
// 主要成員：Index、AdfsAuthorize、AdfsCallback、AdfsTokenProbe
// 編碼要求：UTF-8 without BOM + CRLF
// ============================================================================
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Configuration;
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
    /// 診斷控制器（僅 DEBUG）。
    /// jesus ADFS 只允許 authorization_code / refresh_token，不允許 password grant。
    /// 本機請先開 /diagnostics/adfs-authorize 完成一次瀏覽器授權。
    /// </summary>
#if DEBUG
    [Authorize]
    [Route("diagnostics")]
    public class DiagnosticsController : Controller
    {
        private const string AdfsOAuthStateSessionKey = "Diagnostics.AdfsOAuth.State";
        private readonly IConfiguration _configuration;

        public DiagnosticsController(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [HttpGet("")]
        public IActionResult Index()
        {
            return Json(new
            {
                ServerTime = DateTime.Now,
                Environment = "DEBUG",
                User = User.Identity?.Name ?? "Anonymous",
                IsAuthenticated = User.Identity?.IsAuthenticated ?? false,
                AvailableEndpoints = new[]
                {
                    new { Endpoint = "/diagnostics/adfs-authorize", Description = "ADFS 授權預覽（先看 JSON）" },
                    new { Endpoint = "/diagnostics/adfs-authorize?go=1", Description = "確認 ClientId 已註冊後才真正導向 ADFS" },
                    new { Endpoint = "/diagnostics/adfs-token-probe", Description = "用 refresh_token / 現有 token 探測 WhoAmI" },
                    new { Endpoint = "/diagnostics/session", Description = "查看當前 Session 資訊" },
                    new { Endpoint = "/diagnostics/performance", Description = "查看效能統計" }
                },
                Note = "jesus ADFS rejects password grant (unsupported_grant_type). Use adfs-authorize first."
            });
        }

        /// <summary>
        /// ADFS authorization_code 入口。
        ///
        /// 保姆級教學：
        /// 1. 預設只顯示「授權預覽 JSON」，不立刻 redirect，避免用未註冊 ClientId 盲導向 ADFS 錯誤頁。
        /// 2. 確認 ClientId / RedirectUri 已在 ADFS 註冊後，再打開：
        ///    /diagnostics/adfs-authorize?go=1
        /// 3. jesus 這台 ADFS 已證實：
        ///    - 不支援 password grant
        ///    - 未註冊 client 時，authorize 會落到 CRM IFD Relying Party 並顯示「發生錯誤」
        /// </summary>
        [HttpGet("adfs-authorize")]
        public async Task<IActionResult> AdfsAuthorize(string? go = null)
        {
            var authority = GetAuthority();
            var resource = GetResource();
            var clientId = GetClientId();
            var redirectUri = GetRedirectUri();
            var state = Guid.NewGuid().ToString("N");
            HttpContext.Session.SetString(AdfsOAuthStateSessionKey, state);

            var authorizeUrl =
                authority.TrimEnd('/') + "/oauth2/authorize" +
                "?response_type=code" +
                "&client_id=" + Uri.EscapeDataString(clientId) +
                "&resource=" + Uri.EscapeDataString(resource) +
                "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
                "&response_mode=query" +
                "&state=" + Uri.EscapeDataString(state);

            var preview = new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["stage"] = "authorize-preview",
                ["serverTime"] = DateTime.Now.ToString("o"),
                ["authority"] = authority,
                ["resource"] = resource,
                ["clientId"] = clientId,
                ["redirectUri"] = redirectUri,
                ["authorizeUrl"] = authorizeUrl,
                ["knownFacts"] = new[]
                {
                    "Password grant is disabled on this ADFS (unsupported_grant_type).",
                    "Previous authorize attempt failed with Relying party = 'Dynamics 365 對外連線 IFD'.",
                    "That RP name is the CRM IFD trust, which usually means OAuth client is not registered/permitted.",
                    "Provisional ClientId 2ad88395-... is a Dynamics Online sample id and is likely NOT registered on this on-prem ADFS."
                },
                ["adfsAdminRequired"] = new
                {
                    goal = "Register a public/native OAuth client on ADFS and permit it for CRM IFD resource",
                    samplePowerShell = new[]
                    {
                        "$clientId = [guid]::NewGuid().Guid",
                        "Add-AdfsClient -Name 'SpeechMessage-ChurchReport-LocalDev' -ClientId $clientId -RedirectUri 'http://localhost:43371/diagnostics/adfs-callback'",
                        "# Then put $clientId into DynamicsAccess:Embedded:ClientId",
                        "# If using Application Group model, also grant permission to CRM Web API / IFD relying party identifier."
                    }
                },
                ["nextStep"] = "After ADFS client is registered, open /diagnostics/adfs-authorize?go=1"
            };

            await WriteProbeResultAsync(preview).ConfigureAwait(false);
            Trace.WriteLine("[ADFS-AUTH] preview authorizeUrl=" + authorizeUrl);

            var shouldGo =
                string.Equals(go, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(go, "true", StringComparison.OrdinalIgnoreCase);

            if (!shouldGo)
            {
                return Json(preview);
            }

            Trace.WriteLine("[ADFS-AUTH] redirect to authorize. redirectUri=" + redirectUri + " clientId=" + clientId);
            return Redirect(authorizeUrl);
        }

        /// <summary>
        /// ADFS authorization_code callback：用 code 換 access/refresh token 並存檔。
        /// </summary>
        [HttpGet("adfs-callback")]
        public async Task<IActionResult> AdfsCallback(string? code, string? state, string? error, string? error_description)
        {
            var result = new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["stage"] = "callback",
                ["serverTime"] = DateTime.Now.ToString("o"),
                ["processUser"] = Environment.UserName
            };

            if (!string.IsNullOrWhiteSpace(error))
            {
                result["error"] = error;
                result["errorDescription"] = error_description;
                await WriteProbeResultAsync(result).ConfigureAwait(false);
                return Json(result);
            }

            var expectedState = HttpContext.Session.GetString(AdfsOAuthStateSessionKey);
            if (string.IsNullOrWhiteSpace(state) ||
                string.IsNullOrWhiteSpace(expectedState) ||
                !string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                result["error"] = "Invalid or missing OAuth state.";
                await WriteProbeResultAsync(result).ConfigureAwait(false);
                return Json(result);
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                result["error"] = "Missing authorization code.";
                await WriteProbeResultAsync(result).ConfigureAwait(false);
                return Json(result);
            }

            var authority = GetAuthority();
            var resource = GetResource();
            var clientId = GetClientId();
            var redirectUri = GetRedirectUri();
            var tokenUrl = authority.TrimEnd('/') + "/oauth2/token";
            result["tokenUrl"] = tokenUrl;
            result["redirectUri"] = redirectUri;
            result["clientId"] = clientId;
            result["resource"] = resource;

            try
            {
                using var http = CreateHttpClient();
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["resource"] = resource
                });

                using var response = await http.PostAsync(tokenUrl, content).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                result["tokenHttpStatus"] = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    result["error"] = "authorization_code exchange failed HTTP " + (int)response.StatusCode;
                    result["bodyPreview"] = TrimBody(body);
                    await WriteProbeResultAsync(result).ConfigureAwait(false);
                    return Json(result);
                }

                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root = doc.RootElement;
                if (!root.TryGetProperty("access_token", out var accessNode) ||
                    accessNode.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(accessNode.GetString()))
                {
                    result["error"] = "Token response missing access_token.";
                    result["bodyPreview"] = TrimBody(body);
                    await WriteProbeResultAsync(result).ConfigureAwait(false);
                    return Json(result);
                }

                var accessToken = accessNode.GetString()!;
                string? refreshToken = null;
                if (root.TryGetProperty("refresh_token", out var refreshNode) &&
                    refreshNode.ValueKind == JsonValueKind.String)
                {
                    refreshToken = refreshNode.GetString();
                }

                var expiresIn = 3600;
                if (root.TryGetProperty("expires_in", out var expNode))
                {
                    if (expNode.ValueKind == JsonValueKind.Number && expNode.TryGetInt32(out var n))
                    {
                        expiresIn = n;
                    }
                    else if (expNode.ValueKind == JsonValueKind.String &&
                             int.TryParse(expNode.GetString(), out var s))
                    {
                        expiresIn = s;
                    }
                }

                var storePath = GetTokenStorePath();
                LocalDevAdfsTokenStore.Save(storePath, new LocalDevAdfsTokenRecord
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn)),
                    AuthorityUri = authority,
                    ResourceUri = resource,
                    ClientId = clientId
                });

                result["ok"] = !string.IsNullOrWhiteSpace(refreshToken) || !string.IsNullOrWhiteSpace(accessToken);
                result["stage"] = "token-saved";
                result["tokenStorePath"] = storePath;
                result["hasRefreshToken"] = !string.IsNullOrWhiteSpace(refreshToken);
                result["expiresIn"] = expiresIn;
                result["nextStep"] = "Open /diagnostics/adfs-token-probe to verify WhoAmI";

                // 立刻 WhoAmI 驗證
                var who = await CallWhoAmIAsync(http, accessToken).ConfigureAwait(false);
                result["whoAmIHttpStatus"] = who.StatusCode;
                result["whoAmIOk"] = who.Ok;
                result["whoAmIBody"] = who.BodyPreview;
                if (who.Ok)
                {
                    result["ok"] = true;
                    result["stage"] = "whoami";
                }

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
        /// 探測：優先用 local token store / refresh_token，再試 WhoAmI。
        /// </summary>
        [HttpGet("adfs-token-probe")]
        public async Task<IActionResult> AdfsTokenProbe()
        {
            var authority = GetAuthority();
            var resource = GetResource();
            var clientId = GetClientId();
            var whoAmI = GetWhoAmIUrl();
            var storePath = GetTokenStorePath();

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
                ["tokenStorePath"] = storePath
            };

            try
            {
                using var http = CreateHttpClient();
                string? accessToken = null;

                if (LocalDevAdfsTokenStore.TryLoad(storePath, out var stored) && stored is not null)
                {
                    result["storeLoaded"] = true;
                    result["storeHasRefreshToken"] = !string.IsNullOrWhiteSpace(stored.RefreshToken);
                    result["storeHasAccessToken"] = !string.IsNullOrWhiteSpace(stored.AccessToken);
                    result["storeExpiresAtUtc"] = stored.AccessTokenExpiresAtUtc?.ToString("o");

                    if (!string.IsNullOrWhiteSpace(stored.AccessToken) &&
                        stored.AccessTokenExpiresAtUtc is not null &&
                        DateTimeOffset.UtcNow < stored.AccessTokenExpiresAtUtc.Value.AddSeconds(-60))
                    {
                        accessToken = stored.AccessToken;
                        result["tokenSource"] = "local-store-access-token";
                    }
                    else if (!string.IsNullOrWhiteSpace(stored.RefreshToken))
                    {
                        var tokenUrl = authority.TrimEnd('/') + "/oauth2/token";
                        result["tokenUrl"] = tokenUrl;
                        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            ["client_id"] = clientId,
                            ["grant_type"] = "refresh_token",
                            ["refresh_token"] = stored.RefreshToken!,
                            ["resource"] = resource
                        });
                        using var tokenResponse = await http.PostAsync(tokenUrl, content).ConfigureAwait(false);
                        var tokenBody = await tokenResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                        result["tokenHttpStatus"] = (int)tokenResponse.StatusCode;
                        if (!tokenResponse.IsSuccessStatusCode)
                        {
                            result["stage"] = "refresh";
                            result["error"] = "refresh_token failed HTTP " + (int)tokenResponse.StatusCode;
                            result["bodyPreview"] = TrimBody(tokenBody);
                            result["hint"] = "Open /diagnostics/adfs-authorize to login again.";
                            await WriteProbeResultAsync(result).ConfigureAwait(false);
                            return Json(result);
                        }

                        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(tokenBody) ? "{}" : tokenBody);
                        var root = doc.RootElement;
                        accessToken = root.GetProperty("access_token").GetString();
                        var expiresIn = 3600;
                        if (root.TryGetProperty("expires_in", out var expNode) &&
                            expNode.ValueKind == JsonValueKind.Number &&
                            expNode.TryGetInt32(out var n))
                        {
                            expiresIn = n;
                        }
                        string? newRefresh = stored.RefreshToken;
                        if (root.TryGetProperty("refresh_token", out var rn) && rn.ValueKind == JsonValueKind.String)
                        {
                            newRefresh = rn.GetString();
                        }

                        LocalDevAdfsTokenStore.Save(storePath, new LocalDevAdfsTokenRecord
                        {
                            AccessToken = accessToken,
                            RefreshToken = newRefresh,
                            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
                            AuthorityUri = authority,
                            ResourceUri = resource,
                            ClientId = clientId
                        });
                        result["tokenSource"] = "refresh_token";
                    }
                }
                else
                {
                    result["storeLoaded"] = false;
                }

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    // password grant 在 jesus 會 unsupported_grant_type；這裡只回指引，不再誤導重試密碼。
                    result["stage"] = "need-authorize";
                    result["error"] = "No local ADFS token. jesus ADFS only supports authorization_code/refresh_token.";
                    result["nextStep"] = "Open /diagnostics/adfs-authorize while logged into ChurchReport.";
                    await WriteProbeResultAsync(result).ConfigureAwait(false);
                    return Json(result);
                }

                var who = await CallWhoAmIAsync(http, accessToken!).ConfigureAwait(false);
                result["stage"] = "whoami";
                result["whoAmIHttpStatus"] = who.StatusCode;
                result["whoAmIBody"] = who.BodyPreview;
                result["ok"] = who.Ok;
                if (!who.Ok)
                {
                    result["error"] = "WhoAmI failed HTTP " + who.StatusCode;
                    result["location"] = who.Location;
                }
                else
                {
                    result["nextStep"] = "Set DynamicsAccess:Package01FeeReadsEnabled=true and retest fee list Returned=56";
                }

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

        [HttpGet("session")]
        public IActionResult GetSessionInfo()
        {
            return Json(new
            {
                SessionId = HttpContext.Session.Id,
                IsAvailable = HttpContext.Session.IsAvailable,
                User = User.Identity?.Name ?? "Anonymous",
                IsAuthenticated = User.Identity?.IsAuthenticated ?? false
            });
        }

        [HttpGet("performance")]
        public IActionResult GetPerformanceInfo()
        {
            var process = Process.GetCurrentProcess();
            return Json(new
            {
                WorkingSetMB = process.WorkingSet64 / 1024 / 1024,
                PrivateMemoryMB = process.PrivateMemorySize64 / 1024 / 1024,
                ThreadCount = process.Threads.Count,
                ServerTime = DateTime.Now
            });
        }

        private string GetAuthority()
            => _configuration["DynamicsAccess:Embedded:AuthorityUri"]
               ?? "https://speechmessagests.speechmessage.com.tw/adfs";

        private string GetResource()
            => _configuration["DynamicsAccess:Embedded:ResourceUri"]
               ?? "https://jesus.speechmessage.com.tw/";

        private string GetClientId()
            => _configuration["DynamicsAccess:Embedded:ClientId"]
               ?? "2ad88395-b77d-4561-9441-d0e40824f9bc";

        private string GetWhoAmIUrl()
            => (_configuration["DynamicsAccess:Embedded:OrganizationWebApiBaseUri"]
                ?? "https://jesus.speechmessage.com.tw/api/data/v8.2/").TrimEnd('/') + "/WhoAmI";

        private string GetRedirectUri()
        {
            var configured = _configuration["DynamicsAccess:Embedded:RedirectUri"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured!;
            }

            // 依目前 IIS Express 實際 host 組 redirect
            var request = HttpContext.Request;
            return $"{request.Scheme}://{request.Host}/diagnostics/adfs-callback";
        }

        private string GetTokenStorePath()
        {
            var configured = _configuration["DynamicsAccess:Embedded:LocalDevTokenStorePath"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured!;
            }

            // 優先寫到專案 Logs，方便 Codex 讀檔
            var projectLogs = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "adfs-local-token.json"));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(projectLogs)!);
                return projectLogs;
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "adfs-local-token.json");
            }
        }

        private static HttpClient CreateHttpClient()
            => new HttpClient(new SocketsHttpHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                UseProxy = false
            })
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

        private async Task<(bool Ok, int StatusCode, string BodyPreview, string? Location)> CallWhoAmIAsync(
            HttpClient http,
            string accessToken)
        {
            using var whoRequest = new HttpRequestMessage(HttpMethod.Get, GetWhoAmIUrl());
            whoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            whoRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            whoRequest.Headers.TryAddWithoutValidation("OData-Version", "4.0");
            whoRequest.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");

            using var whoResponse = await http.SendAsync(whoRequest).ConfigureAwait(false);
            var whoBody = await whoResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (
                whoResponse.IsSuccessStatusCode,
                (int)whoResponse.StatusCode,
                TrimBody(whoBody),
                whoResponse.Headers.Location?.ToString());
        }

        private static string TrimBody(string body)
            => body.Length <= 500 ? body : body.Substring(0, 500);

        private static async Task WriteProbeResultAsync(IDictionary<string, object?> result)
        {
            try
            {
                var candidates = new List<string>
                {
                    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Logs")),
                    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "SpeechMessageProducts.ChurchReport", "Logs")),
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Logs")),
                    Path.Combine(AppContext.BaseDirectory, "Logs")
                };

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
    }
#endif
}
