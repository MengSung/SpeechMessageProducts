// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class AuthenticationController、class LineTokenResponse、class LineUserProfile
// 主要成員：LineLoginStart、LineCallback、ResolveLineLoginCallbackUrl、BuildLineLoginCallbackUrlFromRequest、NormalizeUriPort、GetBindingLiffId、GetLoginLiffId、GetBindingPageUrl、GetLineIdLoginPageUrl、GenerateRandomState
// 引用命名空間：ChurchReport.ViewModel、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Configuration、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、System、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Buffers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 認證控制器（LINE Login Server-side OAuth 2.0）
    ///
    /// 目的：解決電腦版 LINE 無法使用 LIFF 的問題
    /// 架構：Server-side OAuth 2.0 流程，完全不依賴 LIFF SDK
    ///
    /// 流程：
    /// 1. LineLoginStart => 重導向至 LINE OAuth 授權頁面
    /// 2. LINE OAuth => 用戶授權 => 回呼 LineCallback
    /// 3. LineCallback => 以 code 換取 access_token => 取得 user profile => 登入系統
    /// </summary>
    public partial class AuthenticationController
    {
        // OAuth state、callback URL、nonce 與 bearer token 都是單一瀏覽器登入流程的 request/Session 邊界資料。
        // Session 僅保存完成 callback 驗證所需的最小值；callback 一開始即 read-and-remove，後續 HTTP、CRM、
        // redirect 或錯誤分支都不得再次從 Session 取得。上游 socket/handler pool 由 DI host 的具名
        // IHttpClientFactory 唯一擁有，action 只 Dispose 短生命週期 wrapper/request/response/stream。
        private const string LineLoginCallbackUrlSessionKey = "_LineLoginCallbackUrl";
        private const string LineLoginStateIssuedAtSessionKey = "_LineLoginStateIssuedAtUtcTicks";
        private const int MaxLineOAuthResponseBytes = 64 * 1024;
        private static readonly TimeSpan LineLoginStateLifetime = TimeSpan.FromMinutes(5);

        #region LINE Login Server-side OAuth 2.0

        /// <summary>
        /// 開始 LINE Login OAuth 流程
        /// 重導向至 LINE 授權頁面
        /// </summary>
        /// <param name="returnUrl">完成後要重導向的目標 URL（可選）</param>
        /// <param name="liffId">呼叫方的 LIFF ID，回呼後可用於導回正確頁面（可選）</param>
        [HttpGet]
        [Route("/Authentication/LineLoginStart")]
        public IActionResult LineLoginStart(string returnUrl = null, string liffId = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[LineLoginStart] ========== 開始 LINE Login OAuth 流程 ==========");

                if (!string.IsNullOrEmpty(returnUrl))
                {
                    if (returnUrl == "_BINDING_" || ChurchReport.Security.LocalReturnUrl.IsLocal(returnUrl))
                    {
                        HttpContext.Session.SetString("_OAuthReturnUrl", returnUrl);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[LineLoginStart] 已拒絕非本機 ReturnUrl。");
                    }
                }

                if (!string.IsNullOrEmpty(liffId))
                {
                    HttpContext.Session.SetString("_OAuthLiffId", liffId);
                }

                var configuration = HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
                if (configuration == null)
                {
                    return Json(new { success = false, message = "無法取得系統設定" });
                }

                var channelId = configuration["LineLogin:ChannelId"];
                var callbackUrl = ResolveLineLoginCallbackUrl(configuration);
                var scope = configuration["LineLogin:Scope"] ?? "profile openid";

                if (string.IsNullOrEmpty(channelId))
                {
                    return Json(new { success = false, message = "LINE Login Channel ID 未設定" });
                }

                if (string.IsNullOrEmpty(callbackUrl))
                {
                    return Json(new { success = false, message = "LINE Login Callback URL 未設定" });
                }

                var state = GenerateRandomState();
                HttpContext.Session.SetString("_LineLoginState", state);
                HttpContext.Session.SetString(
                    LineLoginStateIssuedAtSessionKey,
                    DateTimeOffset.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));

                var nonce = GenerateRandomNonce();
                HttpContext.Session.SetString("_LineLoginNonce", nonce);
                HttpContext.Session.SetString(LineLoginCallbackUrlSessionKey, callbackUrl);

                var authUrl = "https://access.line.me/oauth2/v2.1/authorize?" +
                             $"response_type=code" +
                             $"&client_id={Uri.EscapeDataString(channelId)}" +
                             $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
                             $"&state={Uri.EscapeDataString(state)}" +
                             $"&scope={Uri.EscapeDataString(scope)}" +
                             $"&nonce={Uri.EscapeDataString(nonce)}";

                return Redirect(authUrl);
            }
            catch (Exception)
            {
                System.Diagnostics.Debug.WriteLine("[LineLoginStart] OAuth 啟動失敗。");
                // 共用 HandleError 會記錄完整 Exception 並送出通知；OAuth 邊界禁止讓 configuration、
                // callback 或 framework 例外細節離開 request，因此只回傳固定錯誤分類。
                return Json(new { success = false, message = "LINE 登入目前無法啟動，請稍後再試" });
            }
        }

        /// <summary>
        /// LINE OAuth Callback
        /// 處理 LINE OAuth 授權後的回呼
        /// </summary>
        [HttpGet]
        [Route("/Authentication/LineCallback")]
        public async Task<IActionResult> LineCallback(string code, string state, string error, string error_description)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[LineCallback] ========== LINE OAuth Callback ==========");

                // callback 是 OAuth Session material 的唯一 terminal consumer。必須在 error/mismatch/missing-code 等
                // 所有 early return 之前 read-and-remove，避免 state、nonce 或 callback URL 被下一個 request 重播或長期保留。
                var sessionState = HttpContext.Session.GetString("_LineLoginState");
                var stateIssuedAtTicks = HttpContext.Session.GetString(LineLoginStateIssuedAtSessionKey);
                var callbackUrl = HttpContext.Session.GetString(LineLoginCallbackUrlSessionKey);
                HttpContext.Session.Remove("_LineLoginState");
                HttpContext.Session.Remove(LineLoginStateIssuedAtSessionKey);
                HttpContext.Session.Remove(LineLoginCallbackUrlSessionKey);
                HttpContext.Session.Remove("_LineLoginNonce");

                if (!string.IsNullOrEmpty(error))
                {
                    System.Diagnostics.Debug.WriteLine("[LineCallback] LINE OAuth 回傳固定錯誤分類。");
                    return RedirectToAction("Login", new { error = "LINE 登入失敗，請重新嘗試" });
                }

                if (string.IsNullOrEmpty(sessionState) ||
                    string.IsNullOrEmpty(state) ||
                    string.IsNullOrWhiteSpace(code) ||
                    string.IsNullOrWhiteSpace(callbackUrl) ||
                    !FixedTimeEquals(sessionState, state) ||
                    !IsFreshLineLoginState(stateIssuedAtTicks))
                {
                    System.Diagnostics.Debug.WriteLine("[LineCallback] State 驗證失敗！可能是 CSRF 攻擊");
                    return RedirectToAction("Login", new { error = "State 驗證失敗，請重新登入" });
                }

                var tokenResponse = await ExchangeCodeForToken(code, callbackUrl);

                if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.access_token))
                {
                    System.Diagnostics.Debug.WriteLine("[LineCallback] 取得 Access Token 失敗");
                    return RedirectToAction("Login", new { error = "取得 LINE Access Token 失敗" });
                }

                LineUserProfile userProfile;
                var accessToken = tokenResponse.access_token;
                try
                {
                    userProfile = await GetLineUserProfile(accessToken);
                }
                finally
                {
                    // managed string 無法保證原地清零；立即斷開所有 action-local owner reference，可縮短 bearer token
                    // retention，且 token 從未進入 Session、static cache、log、exception 或 response。
                    tokenResponse.access_token = string.Empty;
                    accessToken = string.Empty;
                }

                if (userProfile == null || string.IsNullOrEmpty(userProfile.userId))
                {
                    System.Diagnostics.Debug.WriteLine("[LineCallback] 取得用戶 Profile 失敗");
                    return RedirectToAction("Login", new { error = "取得 LINE 用戶資料失敗" });
                }

                InMemoryContext.LineBindingViewModel.LineUserId = userProfile.userId;
                InMemoryContext.LineBindingViewModel.DisplayId = userProfile.userId;

                return await ProcessLineUserLogin(userProfile.userId);
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                // 瀏覽器已中斷 request 時不建立新的背景工作或重試；所有 using owner 仍會同步進入 Dispose。
                return StatusCode(499);
            }
            catch (Exception)
            {
                System.Diagnostics.Debug.WriteLine("[LineCallback] OAuth callback 發生未分類錯誤。");
                // 不把原始 Exception 交給會輸出完整內容的共用 HandleError，避免授權碼、token endpoint
                // 或上游 body 經由 exception/inner exception 進入 log 與 LINE 通知。
                return RedirectToAction("Login", new { error = "LINE 登入失敗，請重新嘗試" });
            }
        }

        #endregion

        #region 私有輔助方法

        /// <summary>
        /// Resolve the callback URL used by both LINE authorization and token exchange.
        /// </summary>
        private string ResolveLineLoginCallbackUrl(IConfiguration configuration)
        {
            var configuredCallbackUrl = configuration?["LineLogin:CallbackUrl"]?.Trim();
            var requestCallbackUrl = BuildLineLoginCallbackUrlFromRequest();

            if (string.IsNullOrWhiteSpace(configuredCallbackUrl))
            {
                return requestCallbackUrl;
            }

            if (string.IsNullOrWhiteSpace(requestCallbackUrl))
            {
                return configuredCallbackUrl;
            }

            if (Uri.TryCreate(configuredCallbackUrl, UriKind.Absolute, out var configuredUri) &&
                Uri.TryCreate(requestCallbackUrl, UriKind.Absolute, out var requestUri) &&
                string.Equals(configuredUri.Host, requestUri.Host, StringComparison.OrdinalIgnoreCase) &&
                (NormalizeUriPort(configuredUri) != NormalizeUriPort(requestUri) ||
                 !string.Equals(configuredUri.Scheme, requestUri.Scheme, StringComparison.OrdinalIgnoreCase)))
            {
                System.Diagnostics.Debug.WriteLine(
                    "[ResolveLineLoginCallbackUrl] 設定與目前 request callback 不一致，已採用目前 request URL。");
                return requestCallbackUrl;
            }

            return configuredCallbackUrl;
        }

        private string BuildLineLoginCallbackUrlFromRequest()
        {
            var request = HttpContext?.Request;
            if (request == null || !request.Host.HasValue)
            {
                return null;
            }

            var pathBase = request.PathBase.HasValue ? request.PathBase.Value.TrimEnd('/') : string.Empty;
            return $"{request.Scheme}://{request.Host}{pathBase}/Authentication/LineCallback";
        }

        private static int NormalizeUriPort(Uri uri)
        {
            if (!uri.IsDefaultPort)
            {
                return uri.Port;
            }

            return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80;
        }

        /// <summary>
        /// 取得綁定頁面 LIFF ID（從設定檔讀取）
        /// </summary>
        private string GetBindingLiffId()
        {
            try
            {
                var configuration = HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
                var liffId = configuration?["Liff:BindingLiffId"];

                if (string.IsNullOrEmpty(liffId))
                {
                    System.Diagnostics.Debug.WriteLine("[GetBindingLiffId] 設定檔中找不到 Liff:BindingLiffId，使用預設值");
                    return "1653819697-YkPyPkr6";
                }

                return liffId;
            }
            catch (Exception)
            {
                System.Diagnostics.Debug.WriteLine("[GetBindingLiffId] 讀取設定失敗，使用固定 fallback。");
                return "1653819697-YkPyPkr6";
            }
        }

        /// <summary>
        /// 取得登入頁面 LIFF ID（從設定檔讀取）
        /// </summary>
        private string GetLoginLiffId()
        {
            try
            {
                var configuration = HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
                var liffId = configuration?["Liff:LoginLiffId"];

                if (string.IsNullOrEmpty(liffId))
                {
                    System.Diagnostics.Debug.WriteLine("[GetLoginLiffId] 設定檔中找不到 Liff:LoginLiffId，使用預設值");
                    return "2007621061-Exd9BGv8";
                }

                return liffId;
            }
            catch (Exception)
            {
                System.Diagnostics.Debug.WriteLine("[GetLoginLiffId] 讀取設定失敗，使用固定 fallback。");
                return "2007621061-Exd9BGv8";
            }
        }

        /// <summary>
        /// 取得綁定頁面完整 URL（從設定檔讀取 LIFF ID）
        /// </summary>
        private string GetBindingPageUrl()
        {
            var liffId = GetBindingLiffId();
            return $"https://liff.line.me/{Uri.EscapeDataString(liffId)}";
        }

        /// <summary>
        /// 取得 LINE ID 登入頁面完整 URL。
        /// OAuth 發生錯誤或需要重新登入時，優先回到原本的 LINE 登入頁，而不是一般帳密登入頁。
        /// </summary>
        private string GetLineIdLoginPageUrl()
        {
            var liffId = HttpContext.Session.GetString("_OAuthLiffId");

            if (string.IsNullOrWhiteSpace(liffId))
            {
                liffId = GetLoginLiffId();
            }

            return $"/Home/LineIdLoginView/{Uri.EscapeDataString(liffId)}";
        }

        /// <summary>
        /// 產生隨機 state 用於 CSRF 防護
        /// </summary>
        private static string GenerateRandomState() => GenerateRandomUrlSafeValue();

        /// <summary>
        /// 產生隨機 nonce 用於 ID Token 驗證
        /// </summary>
        private static string GenerateRandomNonce() => GenerateRandomUrlSafeValue();

        /// <summary>
        /// 建立 256-bit URL-safe 隨機值。byte array 是本方法唯一 owner，轉成 managed string 後立即於
        /// finally 清零；不使用 static RNG state、cache 或背景工作，因此平行登入不會共享可變資料。
        /// </summary>
        private static string GenerateRandomUrlSafeValue()
        {
            var bytes = new byte[32];
            try
            {
                RandomNumberGenerator.Fill(bytes);
                return Convert.ToBase64String(bytes)
                    .Replace("+", "-", StringComparison.Ordinal)
                    .Replace("/", "_", StringComparison.Ordinal)
                    .Replace("=", string.Empty, StringComparison.Ordinal);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        /// <summary>
        /// 以 UTF-8 bytes 做固定時間 state 比較。兩個暫存 byte array 均為方法唯一 owner，無論成功或失敗
        /// 都清零；state 長度由 256-bit generator 固定，長度不符直接 fail closed。
        /// </summary>
        private static bool FixedTimeEquals(string expected, string supplied)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
            try
            {
                return expectedBytes.Length == suppliedBytes.Length &&
                       CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expectedBytes);
                CryptographicOperations.ZeroMemory(suppliedBytes);
            }
        }

        /// <summary>
        /// 驗證一次性 state 的簽發時間仍在五分鐘生命週期內。無法解析、超出 DateTimeOffset 範圍、
        /// 未來時間或逾期一律 fail closed；不建立 timer 或 cache，故沒有跨 Session retention 或 cleanup race。
        /// </summary>
        private static bool IsFreshLineLoginState(string issuedAtTicks)
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
            return issuedAt <= now && now - issuedAt <= LineLoginStateLifetime;
        }

        /// <summary>
        /// 以授權碼換取 Access Token
        /// </summary>
        private async Task<LineTokenResponse> ExchangeCodeForToken(string code, string callbackUrl)
        {
            try
            {
                var configuration = HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
                var httpClientFactory = HttpContext.RequestServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
                if (configuration == null || httpClientFactory == null)
                {
                    return null;
                }

                var channelId = configuration["LineLogin:ChannelId"];
                var channelSecret = configuration["LineLogin:ChannelSecret"];
                if (string.IsNullOrWhiteSpace(code) ||
                    string.IsNullOrWhiteSpace(callbackUrl) ||
                    string.IsNullOrWhiteSpace(channelId) ||
                    string.IsNullOrWhiteSpace(channelSecret))
                {
                    return null;
                }

                // factory 擁有 handler/socket pool；action 僅擁有 wrapper。request 是 FormUrlEncodedContent 的
                // 唯一 Dispose owner，response/stream/body 也都在本 scope 內確定釋放或清零。
                using var httpClient = httpClientFactory.CreateClient("line-login-oauth");
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://api.line.me/oauth2/v2.1/token")
                {
                    Content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("grant_type", "authorization_code"),
                        new KeyValuePair<string, string>("code", code),
                        new KeyValuePair<string, string>("redirect_uri", callbackUrl),
                        new KeyValuePair<string, string>("client_id", channelId),
                        new KeyValuePair<string, string>("client_secret", channelSecret)
                    })
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    HttpContext.RequestAborted).ConfigureAwait(false);
                var responseBytes = await ReadBoundedLineOAuthContentAsync(
                    response.Content,
                    HttpContext.RequestAborted).ConfigureAwait(false);
                try
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine("[ExchangeCodeForToken] LINE token endpoint 回傳失敗分類。");
                        return null;
                    }

                    return ParseLineTokenResponse(responseBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(responseBytes);
                }
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                System.Diagnostics.Debug.WriteLine("[ExchangeCodeForToken] LINE token 交換失敗。");
                return null;
            }
        }

        /// <summary>
        /// 以 Access Token 取得用戶 Profile
        /// </summary>
        private async Task<LineUserProfile> GetLineUserProfile(string accessToken)
        {
            try
            {
                var httpClientFactory = HttpContext.RequestServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
                if (httpClientFactory == null || string.IsNullOrWhiteSpace(accessToken))
                {
                    return null;
                }

                // Authorization 僅放在 request header，不修改共享 client defaults；request Dispose 後即釋放
                // bearer token reference，避免同一 handler pool 的下一個 Session 繼承認證資料。
                using var httpClient = httpClientFactory.CreateClient("line-login-oauth");
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.line.me/v2/profile");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    HttpContext.RequestAborted).ConfigureAwait(false);
                var responseBytes = await ReadBoundedLineOAuthContentAsync(
                    response.Content,
                    HttpContext.RequestAborted).ConfigureAwait(false);
                try
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine("[GetLineUserProfile] LINE profile endpoint 回傳失敗分類。");
                        return null;
                    }

                    return ParseLineUserProfile(responseBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(responseBytes);
                }
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                System.Diagnostics.Debug.WriteLine("[GetLineUserProfile] LINE profile 取得失敗。");
                return null;
            }
        }

        /// <summary>
        /// 有界讀取 LINE OAuth JSON。Content-Length 與實際串流量都不得超過 64 KiB；caller 是回傳
        /// exact array 的唯一 owner，必須在解析完成後清零。租用 buffer 與 stream 由本方法在 finally/
        /// await using 確定清理，RequestAborted 則保證瀏覽器中斷後不留下懸掛讀取工作。
        /// </summary>
        private static async Task<byte[]> ReadBoundedLineOAuthContentAsync(
            HttpContent content,
            CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength is long contentLength &&
                contentLength > MaxLineOAuthResponseBytes)
            {
                throw new InvalidOperationException("LINE OAuth response exceeds the configured limit.");
            }

            await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var rented = ArrayPool<byte>.Shared.Rent(MaxLineOAuthResponseBytes + 1);
            try
            {
                var totalRead = 0;
                while (totalRead <= MaxLineOAuthResponseBytes)
                {
                    var read = await stream.ReadAsync(
                        rented.AsMemory(totalRead, MaxLineOAuthResponseBytes + 1 - totalRead),
                        cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        var exact = new byte[totalRead];
                        rented.AsSpan(0, totalRead).CopyTo(exact);
                        return exact;
                    }

                    totalRead += read;
                }

                throw new InvalidOperationException("LINE OAuth response exceeds the configured limit.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rented);
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        /// <summary>
        /// 從已受大小限制的 UTF-8 JSON 只解析 access_token；refresh_token、id_token 與未知欄位直接
        /// Skip，不建立額外 managed string。格式不正確或缺少必要欄位時 fail closed 回傳 null。
        /// </summary>
        private static LineTokenResponse ParseLineTokenResponse(ReadOnlySpan<byte> responseBody)
        {
            var reader = new Utf8JsonReader(responseBody);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            string accessToken = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return null;
                }

                var isAccessToken = reader.ValueTextEquals("access_token");
                if (!reader.Read())
                {
                    return null;
                }

                if (isAccessToken)
                {
                    if (reader.TokenType != JsonTokenType.String || accessToken != null)
                    {
                        return null;
                    }

                    accessToken = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }

            return string.IsNullOrWhiteSpace(accessToken)
                ? null
                : new LineTokenResponse { access_token = accessToken };
        }

        /// <summary>
        /// 從已受大小限制的 profile JSON 只解析後續 CRM 查詢必要的 userId；display name、picture URL
        /// 與 status message 不配置、不記錄，避免個資在 action 之外滯留。重複或缺少 userId 時 fail closed。
        /// </summary>
        private static LineUserProfile ParseLineUserProfile(ReadOnlySpan<byte> responseBody)
        {
            var reader = new Utf8JsonReader(responseBody);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            string userId = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return null;
                }

                var isUserId = reader.ValueTextEquals("userId");
                if (!reader.Read())
                {
                    return null;
                }

                if (isUserId)
                {
                    if (reader.TokenType != JsonTokenType.String || userId != null)
                    {
                        return null;
                    }

                    userId = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }

            return string.IsNullOrWhiteSpace(userId)
                ? null
                : new LineUserProfile { userId = userId };
        }

        /// <summary>
        /// 處理 LINE 用戶登入
        /// </summary>
        private async Task<IActionResult> ProcessLineUserLogin(string lineUserId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 開始處理 LINE 用戶登入。");

                var returnUrl = HttpContext.Session.GetString("_OAuthReturnUrl");
                if (!string.IsNullOrEmpty(returnUrl))
                {
                    System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 偵測到已驗證的本機 ReturnUrl。");
                    HttpContext.Session.Remove("_OAuthReturnUrl");

                    if (returnUrl == "_BINDING_")
                    {
                        System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 偵測到綁定流程，重導向至綁定頁面");
                        TempData["_PendingLineUserId"] = lineUserId;
                        var bindingPageUrl = GetBindingPageUrl();
                        return Redirect(bindingPageUrl);
                    }

                    if (!ChurchReport.Security.LocalReturnUrl.IsLocal(returnUrl))
                    {
                        System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 已拒絕 Session 中的非本機 ReturnUrl。");
                        returnUrl = null;
                    }

                    if (string.IsNullOrEmpty(returnUrl))
                    {
                        return RedirectToAction("Login");
                    }

                    IOrganizationService service = null;
                    try
                    {
                        service = GetConnection();
                        var query = new QueryExpression("contact")
                        {
                            ColumnSet = new ColumnSet("contactid", "fullname", "new_lineid"),
                            Criteria = new FilterExpression
                            {
                                FilterOperator = LogicalOperator.And,
                                Conditions =
                                {
                                    new ConditionExpression("new_lineid", ConditionOperator.Equal, lineUserId),
                                    new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                                }
                            },
                            TopCount = 1
                        };
                        var results = service.RetrieveMultiple(query);
                        if (results.Entities.Count == 0)
                        {
                            System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 此 LINE ID 尚未綁定，重導向至綁定頁面");
                            return Redirect(GetBindingPageUrl());
                        }
                        System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 已找到綁定聯絡人。");
                    }
                    finally
                    {
                        ReleaseConnection(service);
                    }

                    try
                    {
                        var tempReturnUrl = returnUrl;
                        HttpContext.Session.Clear();
                        await HttpContext.Session.CommitAsync();
                        returnUrl = tempReturnUrl;
                    }
                    catch (Exception)
                    {
                        System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] Session 清除或提交失敗。");
                    }

                    InMemoryContext.LineBindingViewModel.LineUserId = lineUserId;
                    await ProcessLogin(new GalleryViewModel { Account = "", Password = lineUserId });

                    System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 使用已驗證的本機 ReturnUrl 重導向。");
                    return Redirect($"{returnUrl}/{lineUserId}");
                }

                // 一般登入流程（無 returnUrl）
                IOrganizationService service2 = null;
                Entity foundContact2 = null;
                try
                {
                    service2 = GetConnection();
                    var query = new QueryExpression("contact")
                    {
                        ColumnSet = new ColumnSet("contactid", "fullname", "new_lineid"),
                        Criteria = new FilterExpression
                        {
                            FilterOperator = LogicalOperator.And,
                            Conditions =
                            {
                                new ConditionExpression("new_lineid", ConditionOperator.Equal, lineUserId),
                                new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                            }
                        },
                        TopCount = 1
                    };
                    var results = service2.RetrieveMultiple(query);
                    if (results.Entities.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 此 LINE ID 尚未綁定，重導向至綁定頁面");
                        return Redirect(GetBindingPageUrl());
                    }
                    foundContact2 = results.Entities[0];
                    System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 已找到綁定聯絡人。");
                }
                finally
                {
                    ReleaseConnection(service2);
                }

                try
                {
                    HttpContext.Session.Clear();
                    await HttpContext.Session.CommitAsync();
                }
                catch (Exception)
                {
                    System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] Session 清除或提交失敗。");
                }

                InMemoryContext.LineBindingViewModel.LineUserId = lineUserId;
                var loginResult2 = await ProcessLogin(new GalleryViewModel { Account = "", Password = lineUserId });

                if (loginResult2 is JsonResult jsonResult)
                {
                    System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 登入成功，解析回傳結果");
                    var resultValue = jsonResult.Value;
                    var resultType = resultValue.GetType();
                    var displayViewTypeProperty = resultType.GetProperty("DisplayViewType");
                    var activeListIdProperty = resultType.GetProperty("ActiveListId");

                    if (displayViewTypeProperty != null && activeListIdProperty != null)
                    {
                        var displayViewType = displayViewTypeProperty.GetValue(resultValue)?.ToString();
                        var activeListId = activeListIdProperty.GetValue(resultValue)?.ToString();

                        System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 已解析固定登入導向分類。");

                        if (displayViewType == "MultiGroupView")
                            return Redirect($"/SmallGroup/MultiGroupView/{activeListId}");
                        else if (displayViewType == "IntegrateView")
                            return Redirect($"/SmallGroup/IntegrateView/{activeListId}");
                        else if (displayViewType == "HappyGroupView")
                            return Redirect("/SmallGroup/HappyGroup");
                        else
                            return Redirect("/Home/Index");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 無法取得 DisplayViewType，重導向至首頁");
                        return Redirect("/Home/Index");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] ProcessLogin 回傳非 JSON 結果。");
                    return loginResult2;
                }
            }
            catch (Exception)
            {
                System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 登入處理失敗。");
                var loginPageUrl = GetLineIdLoginPageUrl();
                var separator = loginPageUrl.Contains("?") ? "&" : "?";
                return Redirect($"{loginPageUrl}{separator}error={Uri.EscapeDataString("登入失敗，請稍後再試")}");
            }
        }

        #endregion

        #region 內部類別

        /// <summary>
        /// LINE Token Response
        /// </summary>
        private class LineTokenResponse
        {
            public string access_token { get; set; }
        }

        /// <summary>
        /// LINE User Profile
        /// </summary>
        private class LineUserProfile
        {
            public string userId { get; set; }
        }

        #endregion
    }
}
