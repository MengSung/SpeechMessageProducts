// ============================================================================
// 檔案：SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs
// 目的：僅在 DEBUG 組態提供最小、無持久化的 Local Gateway／ADFS 診斷資訊。
//
// 安全設計：
// 1. 所有回應強制 private, no-store；不得讓瀏覽器、Proxy 或 shared cache 保存診斷訊號。
// 2. OAuth state 只存在目前 HTTP Session，callback 讀取後立即移除，所有成功與失敗路徑均為一次性消費。
// 3. authorization code、access token、refresh token、上游 body、endpoint、client ID 與 Session ID 都不寫檔、不寫 log、不回顯。
// 4. HttpClient、handler、request、response、content、stream、buffer 由單一 action scope 確定性擁有並釋放。
// 5. 診斷失敗只回傳固定 category／status，避免 exception 或部署細節成為資料外洩通道。
// ============================================================================

using System.Buffers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ChurchReport.Security;

namespace ChurchReport.Controllers
{
#if DEBUG
    /// <summary>
    /// DEBUG-only 診斷控制器。這不是一般應用程式 API，也不是 token broker；它只協助已登入的本機開發者確認
    /// authorization-code callback 與 WhoAmI 是否可達。每個 action 都維持最小回應，禁止以診斷便利性換取
    /// token、Session、部署設定或上游 response 的 retention。Release 編譯不包含這個型別。
    /// </summary>
    [Authorize(Policy = DiagnosticsOperatorAuthorization.PolicyName)]
    [Route("diagnostics")]
    public sealed class DiagnosticsController : Controller
    {
        private const string AdfsOAuthStateSessionKey = "Diagnostics.AdfsOAuth.State";
        private const string AdfsOAuthStateIssuedAtSessionKey = "Diagnostics.AdfsOAuth.IssuedAtUtcTicks";
        private const int MaxTokenResponseBytes = 32 * 1024;
        private const int MaxWhoAmIResponseBytes = 32 * 1024;
        private static readonly TimeSpan OAuthStateLifetime = TimeSpan.FromMinutes(5);
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        /// <summary>
        /// 建立 controller。建構式只保留不可變設定 root，不解析秘密、不建立 HTTP client、socket、timer、
        /// cache 或 Session state；這些資源只會在對應 action 的短生命週期 scope 中建立。
        /// </summary>
        /// <param name="configuration">已由 Host 建立的部署設定；action 不會把其中任何值回傳給 caller。</param>
        /// <param name="httpClientFactory">Host-owned named client factory；擁有 handler/socket pool 並在 Host 停止時統一清理。</param>
        public DiagnosticsController(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        /// <summary>
        /// 回傳最小診斷入口狀態。結果刻意不含使用者名稱、Session ID、authority、resource、client ID 或 URL。
        /// response headers 由本 action 的唯一 owner 設定為不可儲存，沒有額外資源或 cleanup 責任。
        /// </summary>
        [HttpGet("")]
        public IActionResult Index()
        {
            ApplyNoStoreHeaders();
            return Json(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["stage"] = "diagnostics",
                ["adfsAuthorizeAvailable"] = true,
                ["sessionAvailable"] = HttpContext.Session.IsAvailable
            });
        }

        /// <summary>
        /// 顯示不含部署資料的 authorize preview；只有明確 <c>go=1</c> 才建立 session-owned state 並導向 ADFS。
        /// preview 不配置 state，避免單純 GET 造成 Session retention；真正導向時 state 只保存在同一 Session，並由
        /// callback 的 read-and-remove 路徑唯一消費。state 使用隨機 256-bit 值，不進入 log、response 或檔案。
        /// </summary>
        /// <param name="go">只有值為 1 或 true 才執行 authorization redirect。</param>
        [HttpGet("adfs-authorize")]
        public Task<IActionResult> AdfsAuthorize(string? go = null)
        {
            ApplyNoStoreHeaders();
            var shouldGo = string.Equals(go, "1", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(go, "true", StringComparison.OrdinalIgnoreCase);

            if (!shouldGo)
            {
                return Task.FromResult<IActionResult>(Json(new Dictionary<string, object?>
                {
                    ["ok"] = true,
                    ["stage"] = "authorize-preview",
                    ["redirectStarted"] = false
                }));
            }

            var stateBytes = RandomNumberGenerator.GetBytes(32);
            string state = string.Empty;
            try
            {
                state = Convert.ToHexString(stateBytes);
                HttpContext.Session.SetString(AdfsOAuthStateSessionKey, state);
                HttpContext.Session.SetString(
                    AdfsOAuthStateIssuedAtSessionKey,
                    DateTimeOffset.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
                var authorizeUrl = BuildAuthorizeUrl(state);
                return Task.FromResult<IActionResult>(Redirect(authorizeUrl));
            }
            finally
            {
                // 隨機 byte[] 可確定清零；state 字串無法可靠清零，但離開 action 後沒有欄位、cache、logging 或
                // response reference 保留它。Session 中 state／issued-at 由 callback 的 read-and-remove 唯一擁有。
                CryptographicOperations.ZeroMemory(stateBytes);
                state = string.Empty;
            }
        }

        /// <summary>
        /// 消費一次性的 authorization-code callback。方法一開始先讀取並移除 expected state，因此 error、mismatch、
        /// missing code、上游失敗與成功皆不能重播。token 與 WhoAmI response 均只在 action scope 內使用；成功也不
        /// 建立 refresh-token persistence 或跨 request cache。
        /// </summary>
        /// <param name="code">ADFS authorization code；只用於本次 token exchange。</param>
        /// <param name="state">回傳 state；只與剛移除的 session state 比較。</param>
        /// <param name="error">ADFS error 指示；不回顯其原始內容。</param>
        /// <param name="errorDescription">保留路由相容性，但不讀取或輸出其內容。</param>
        [HttpGet("adfs-callback")]
        public async Task<IActionResult> AdfsCallback(
            string? code,
            string? state,
            string? error,
            string? errorDescription)
        {
            ApplyNoStoreHeaders();
            _ = errorDescription;

            // State 的 read-and-remove 必須早於任何 early return、HTTP client 或設定解析。把 expectedState 移出
            // Session 後，無論比較結果如何，該 nonce 都沒有第二個 consumer。
            var expectedState = HttpContext.Session.GetString(AdfsOAuthStateSessionKey);
            var issuedAtTicks = HttpContext.Session.GetString(AdfsOAuthStateIssuedAtSessionKey);
            HttpContext.Session.Remove(AdfsOAuthStateSessionKey);
            HttpContext.Session.Remove(AdfsOAuthStateIssuedAtSessionKey);

            if (string.IsNullOrWhiteSpace(state) ||
                string.IsNullOrWhiteSpace(expectedState) ||
                !FixedTimeEquals(state, expectedState) ||
                !IsFreshOAuthState(issuedAtTicks))
            {
                return DiagnosticError("Invalid or missing OAuth state.");
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                return DiagnosticError("adfs-error");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return DiagnosticError("missing-code");
            }

            try
            {
                // wrapper 只屬於目前 callback 並由 using 釋放；底層 handler/socket pool 由 IHttpClientFactory/Host 擁有，
                // 不保存 Session、principal、authorization code 或 token，也避免每 callback 建立新連線池的效能抖動。
                using var http = _httpClientFactory.CreateClient(
                    DiagnosticsOperatorAuthorization.DiagnosticsHttpClientName);
                var token = await ExchangeAuthorizationCodeAsync(http, code).ConfigureAwait(false);
                try
                {
                    var whoAmIOk = await CallWhoAmIAsync(http, token).ConfigureAwait(false);
                    return Json(new Dictionary<string, object?>
                    {
                        ["ok"] = whoAmIOk,
                        ["stage"] = whoAmIOk ? "whoami" : "whoami-failed"
                    });
                }
                finally
                {
                    // token 僅有 action-local string reference；不寫 Session、static cache、檔案或 response。
                    token = string.Empty;
                }
            }
            catch (DiagnosticUpstreamException)
            {
                return DiagnosticError("upstream-error");
            }
            catch (HttpRequestException)
            {
                return DiagnosticError("upstream-error");
            }
            catch (OperationCanceledException)
            {
                return DiagnosticError("upstream-error");
            }
            catch (JsonException)
            {
                return DiagnosticError("upstream-error");
            }
            catch (InvalidOperationException)
            {
                // 包含缺少或無效部署設定；不把設定值或例外訊息回傳。
                return DiagnosticError("upstream-error");
            }
            catch (ArgumentException)
            {
                return DiagnosticError("upstream-error");
            }
            catch (FormatException)
            {
                return DiagnosticError("upstream-error");
            }
        }

        /// <summary>
        /// 回傳最小 Session 可用性而非 Session identifier。Session ID 是 server-side correlation/security boundary，
        /// 即使 DEBUG 與已授權使用者也不能變成 API contract。
        /// </summary>
        [HttpGet("session")]
        public IActionResult GetSessionInfo()
        {
            ApplyNoStoreHeaders();
            return Json(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["stage"] = "session",
                ["available"] = HttpContext.Session.IsAvailable
            });
        }

        /// <summary>
        /// 回傳 process 層級、無身份資料的效能快照。<see cref="Process"/> 是本 action 唯一 owner，使用 using
        /// 確定釋放 OS handle；結果禁止包含 process user、command line、endpoint、Session 或 token。
        /// </summary>
        [HttpGet("performance")]
        public IActionResult GetPerformanceInfo()
        {
            ApplyNoStoreHeaders();
            using var process = Process.GetCurrentProcess();
            return Json(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["stage"] = "performance",
                ["workingSetMb"] = process.WorkingSet64 / 1024 / 1024,
                ["privateMemoryMb"] = process.PrivateMemorySize64 / 1024 / 1024,
                ["threadCount"] = process.Threads.Count
            });
        }

        /// <summary>
        /// 設定所有診斷回應的不可儲存策略。這個固定 header 組合在 action 一開始執行，包含 redirect、early error
        /// 與成功路徑；不建立 cache entry、timer 或任何長生命週期 resource。
        /// </summary>
        private void ApplyNoStoreHeaders()
        {
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";
        }

        /// <summary>
        /// 建立只含固定 category 的 JSON 錯誤，避免 ADFS error、exception message、state、code、URL 或 body
        /// 跨 trust boundary。字串是固定常數，沒有可持久化或需要 Dispose 的資料。
        /// </summary>
        private IActionResult DiagnosticError(string category)
            => Json(new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["stage"] = "callback",
                ["error"] = category
            });

        /// <summary>
        /// 建立固定 ADFS authorize URI。authority/resource/client/redirect 都由部署設定決定，不能由 request 覆寫；
        /// URI 只用於 redirect header，不會寫 log、檔案或 JSON response。
        /// </summary>
        private string BuildAuthorizeUrl(string state)
        {
            var authority = GetRequiredSetting("DynamicsAccess:Embedded:AuthorityUri");
            var resource = GetRequiredSetting("DynamicsAccess:Embedded:ResourceUri");
            var clientId = GetRequiredSetting("DynamicsAccess:Embedded:ClientId");
            var redirectUri = GetRequiredRedirectUri();

            return authority.TrimEnd('/') + "/oauth2/authorize" +
                   "?response_type=code" +
                   "&client_id=" + Uri.EscapeDataString(clientId) +
                   "&resource=" + Uri.EscapeDataString(resource) +
                   "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
                   "&response_mode=query" +
                   "&state=" + Uri.EscapeDataString(state);
        }

        /// <summary>
        /// 以 authorization code 取得 action-local access token。response header 與實際 stream 都有硬上限；
        /// parser 直接略過 refresh token，絕不把它建成 managed string 或寫入任何 store。
        /// </summary>
        private async Task<string> ExchangeAuthorizationCodeAsync(HttpClient http, string code)
        {
            var authority = GetRequiredSetting("DynamicsAccess:Embedded:AuthorityUri");
            var resource = GetRequiredSetting("DynamicsAccess:Embedded:ResourceUri");
            var clientId = GetRequiredSetting("DynamicsAccess:Embedded:ClientId");
            var redirectUri = GetRequiredRedirectUri();
            var tokenUrl = authority.TrimEnd('/') + "/oauth2/token";

            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("redirect_uri", redirectUri),
                new KeyValuePair<string, string>("resource", resource)
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
            {
                Content = content
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                HttpContext.RequestAborted).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new DiagnosticUpstreamException();
            }

            var responseBytes = await ReadBoundedContentAsync(
                response.Content,
                MaxTokenResponseBytes,
                HttpContext.RequestAborted).ConfigureAwait(false);
            try
            {
                return ParseAccessToken(responseBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(responseBytes);
            }
        }

        /// <summary>
        /// 對固定 CRM Web API root 的 WhoAmI 發出一次 action-local bearer request。成功與否只回傳布林；
        /// body 仍會在有界讀取後清零，避免 identity JSON、錯誤頁或 token-related metadata 留在記憶體。
        /// </summary>
        private async Task<bool> CallWhoAmIAsync(HttpClient http, string accessToken)
        {
            var webApiRoot = GetRequiredSetting("DynamicsAccess:Embedded:OrganizationWebApiBaseUri");
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                webApiRoot.TrimEnd('/') + "/WhoAmI");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("OData-Version", "4.0");
            request.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");

            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                HttpContext.RequestAborted).ConfigureAwait(false);
            var responseBytes = await ReadBoundedContentAsync(
                response.Content,
                MaxWhoAmIResponseBytes,
                HttpContext.RequestAborted).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(responseBytes);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// 從 HTTP content 讀取最多 <paramref name="maximumBytes"/> 個位元組。caller 是回傳陣列的唯一 owner，
        /// 無論 parser 成功或失敗都必須清零；方法不使用 static buffer 或 MemoryStream，避免跨 request retention。
        /// </summary>
        private static async Task<byte[]> ReadBoundedContentAsync(
            HttpContent content,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
            {
                throw new DiagnosticUpstreamException();
            }

            await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var rented = ArrayPool<byte>.Shared.Rent(maximumBytes + 1);
            try
            {
                var totalRead = 0;
                while (totalRead <= maximumBytes)
                {
                    var read = await stream.ReadAsync(
                        rented.AsMemory(totalRead, maximumBytes + 1 - totalRead),
                        cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        var exact = new byte[totalRead];
                        rented.AsSpan(0, totalRead).CopyTo(exact);
                        return exact;
                    }

                    totalRead += read;
                }

                throw new DiagnosticUpstreamException();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rented);
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        /// <summary>
        /// 從已受大小限制的 UTF-8 JSON 解析 access token。只有 access_token 轉成 action-local string；
        /// refresh_token 與未知欄位直接 Skip，避免額外敏感字串配置。格式不符時映射成固定 upstream failure。
        /// </summary>
        private static string ParseAccessToken(ReadOnlySpan<byte> responseBody)
        {
            var reader = new Utf8JsonReader(responseBody);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                throw new DiagnosticUpstreamException();
            }

            string? accessToken = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new DiagnosticUpstreamException();
                }

                var isAccessToken = reader.ValueTextEquals("access_token"u8);
                if (!reader.Read())
                {
                    throw new DiagnosticUpstreamException();
                }

                if (isAccessToken && reader.TokenType == JsonTokenType.String)
                {
                    accessToken = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }

            return string.IsNullOrWhiteSpace(accessToken)
                ? throw new DiagnosticUpstreamException()
                : accessToken;
        }

        /// <summary>
        /// 使用固定時間比較 session state，避免直接字串比較提供不必要的比對時間訊號。UTF-8 bytes 在 finally 清零；
        /// 此 helper 不保留 caller state，也不建立 cache、timer 或 background work。
        /// </summary>
        private static bool FixedTimeEquals(string suppliedState, string expectedState)
        {
            var suppliedBytes = Encoding.UTF8.GetBytes(suppliedState);
            var expectedBytes = Encoding.UTF8.GetBytes(expectedState);
            try
            {
                return CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(suppliedBytes);
                CryptographicOperations.ZeroMemory(expectedBytes);
            }
        }

        /// <summary>
        /// 驗證 OAuth state 的 server-issued timestamp 仍在五分鐘有效窗內。未解析、過期或超過一分鐘未來偏移皆
        /// fail closed；timestamp 在 callback 一開始已從 Session 移除，不會因驗證失敗延長 retention。
        /// </summary>
        private static bool IsFreshOAuthState(string? issuedAtTicks)
        {
            if (!long.TryParse(
                    issuedAtTicks,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var ticks))
            {
                return false;
            }

            DateTimeOffset issuedAt;
            try
            {
                issuedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            return issuedAt <= now.AddMinutes(1) &&
                   now - issuedAt <= OAuthStateLifetime;
        }

        /// <summary>
        /// 取得非空白的部署固定設定。錯誤不帶出 key 或值給 HTTP response；外層統一映射成固定 upstream category。
        /// </summary>
        private string GetRequiredSetting(string key)
            => !string.IsNullOrWhiteSpace(_configuration[key])
                ? _configuration[key]!.Trim()
                : throw new InvalidOperationException();

        /// <summary>
        /// 取得已註冊的 redirect URI；未設定時使用目前 request host 組成 callback，值只會送往 ADFS redirect request，
        /// 不會寫入 log 或回應。
        /// </summary>
        private string GetRequiredRedirectUri()
        {
            var configured = _configuration["DynamicsAccess:Embedded:RedirectUri"];
            return !string.IsNullOrWhiteSpace(configured)
                ? configured.Trim()
                : $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/diagnostics/adfs-callback";
        }

        /// <summary>
        /// 表示可安全映射為固定 <c>upstream-error</c> 的診斷上游失敗。型別不保存 URL、body、token 或原始 exception，
        /// 因此離開 action 後不會擴大敏感資料生命週期。
        /// </summary>
        private sealed class DiagnosticUpstreamException : Exception
        {
        }
    }
#endif
}
