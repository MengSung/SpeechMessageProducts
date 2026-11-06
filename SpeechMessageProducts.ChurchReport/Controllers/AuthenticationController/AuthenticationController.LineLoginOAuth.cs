/*
 * 檔案整體說明
 *
 * 目的與定位：認證控制器（LINE Login Server-side OAuth 2.0） 目的：解決電腦版 LINE 無法使用 LIFF 的問題 架構：Server-side OAuth 2.0 流程，完全不依賴 
 * LIFF SDK 流程： 1. LineLoginStart => 重導向至 LINE OAuth 授權頁面 2. LINE OAuth => 用戶授權 => 回呼 LineCallback 3. LineCallback 
 * => 以 code 換取 access_token => 取得 user profile => 登入系統
 *
 * 主要功能與流程：
 * 本檔宣告 AuthenticationController、LineTokenResponse、LineUserProfile。
 * - 開始 LINE Login OAuth 流程 重導向至 LINE 授權頁面
 * - LINE OAuth Callback 處理 LINE OAuth 授權後的回呼
 * - 取得綁定頁面完整 URL（從設定檔讀取 LIFF ID）
 * - 取得 LINE ID 登入頁面完整 URL。 OAuth 發生錯誤或需要重新登入時，優先回到原本的 LINE 登入頁，而不是一般帳密登入頁。
 * 主要操作入口包含 LineLoginStart、LineCallback、ResolveLineLoginCallbackUrl、BuildLineLoginCallbackUrlFromRequest、
 * NormalizeUriPort、GetBindingLiffId、GetLoginLiffId、GetBindingPageUrl；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔與同名控制器的其他partial共用一次請求的依賴及狀態；授權、Session與資料範圍必須沿既有伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/ChurchReportLineBindingNotificationServiceTests.cs、
 * ChurchReport.MemberInfo.Tests/Security/LineLoginOAuthSensitiveLoggingTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Core.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineBinding.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLogin.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        private const string LineLoginCallbackUrlSessionKey = "_LineLoginCallbackUrl";

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
                System.Diagnostics.Debug.WriteLine($"[LineLoginStart] ReturnUrl: {returnUrl}");
                System.Diagnostics.Debug.WriteLine($"[LineLoginStart] LiffId: {liffId}");

                if (!string.IsNullOrEmpty(returnUrl))
                {
                    if (returnUrl == "_BINDING_" || ChurchReport.Security.LocalReturnUrl.IsLocal(returnUrl))
                    {
                        HttpContext.Session.SetString("_OAuthReturnUrl", returnUrl);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[LineLoginStart] Rejected non-local returnUrl: {returnUrl}");
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

                System.Diagnostics.Debug.WriteLine($"[LineLoginStart] ChannelId: {channelId}");
                System.Diagnostics.Debug.WriteLine($"[LineLoginStart] CallbackUrl: {callbackUrl}");

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

                System.Diagnostics.Debug.WriteLine($"[LineLoginStart] 授權 URL: {authUrl}");

                return Redirect(authUrl);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[LineLoginStart] 錯誤: {e.Message}");
                return HandleError(e, "LineLoginStart");
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
                System.Diagnostics.Debug.WriteLine($"[LineCallback] Code: {code?.Substring(0, Math.Min(20, code?.Length ?? 0))}...");
                System.Diagnostics.Debug.WriteLine($"[LineCallback] State: {state}");
                System.Diagnostics.Debug.WriteLine($"[LineCallback] Error: {error}");

                if (!string.IsNullOrEmpty(error))
                {
                    System.Diagnostics.Debug.WriteLine($"[LineCallback] LINE OAuth 錯誤: {error} - {error_description}");
                    return RedirectToAction("Login", new { error = $"LINE 登入失敗: {error_description}" });
                }

                var sessionState = HttpContext.Session.GetString("_LineLoginState");
                if (string.IsNullOrEmpty(sessionState) || sessionState != state)
                {
                    System.Diagnostics.Debug.WriteLine("[LineCallback] State 驗證失敗！可能是 CSRF 攻擊");
                    return RedirectToAction("Login", new { error = "State 驗證失敗，請重新登入" });
                }

                HttpContext.Session.Remove("_LineLoginState");

                var tokenResponse = await ExchangeCodeForToken(code);
                HttpContext.Session.Remove(LineLoginCallbackUrlSessionKey);
                HttpContext.Session.Remove("_LineLoginNonce");

                if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.access_token))
                {
                    System.Diagnostics.Debug.WriteLine("[LineCallback] 取得 Access Token 失敗");
                    return RedirectToAction("Login", new { error = "取得 LINE Access Token 失敗" });
                }

                // 不記錄 token 本文或前綴；即使在 Debug 輸出也不得留下可重播的憑證材料。
                System.Diagnostics.Debug.WriteLine("[LineCallback] Access Token acquired.");

                var userProfile = await GetLineUserProfile(tokenResponse.access_token);
                if (userProfile == null || string.IsNullOrEmpty(userProfile.userId))
                {
                    System.Diagnostics.Debug.WriteLine("[LineCallback] 取得用戶 Profile 失敗");
                    return RedirectToAction("Login", new { error = "取得 LINE 用戶資料失敗" });
                }

                System.Diagnostics.Debug.WriteLine($"[LineCallback] 用戶 ID: {userProfile.userId}");
                System.Diagnostics.Debug.WriteLine($"[LineCallback] 用戶名稱: {userProfile.displayName}");

                InMemoryContext.LineBindingViewModel.LineUserId = userProfile.userId;
                InMemoryContext.LineBindingViewModel.DisplayId = userProfile.userId;

                return await ProcessLineUserLogin(userProfile.userId);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[LineCallback] 錯誤: {e.Message}");
                System.Diagnostics.Debug.WriteLine($"[LineCallback] 堆疊追蹤: {e.StackTrace}");
                return HandleError(e, "LineCallback");
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
                    $"[ResolveLineLoginCallbackUrl] Configured callback ({configuredCallbackUrl}) differs from current request ({requestCallbackUrl}); using current request URL.");
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetBindingLiffId] 讀取設定錯誤: {ex.Message}");
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetLoginLiffId] 讀取設定錯誤: {ex.Message}");
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
        private string GenerateRandomState()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[32];
                rng.GetBytes(bytes);
                return Convert.ToBase64String(bytes)
                    .Replace("+", "-")
                    .Replace("/", "_")
                    .Replace("=", "");
            }
        }

        /// <summary>
        /// 產生隨機 nonce 用於 ID Token 驗證
        /// </summary>
        private string GenerateRandomNonce()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[32];
                rng.GetBytes(bytes);
                return Convert.ToBase64String(bytes)
                    .Replace("+", "-")
                    .Replace("/", "_")
                    .Replace("=", "");
            }
        }

        /// <summary>
        /// 從 DI 取得由 IHttpClientFactory 管理的 HttpClient。
        /// </summary>
        /// <remarks>
        /// ⚠️【資源不變量】此檔案內絕對不可再出現 <c>new HttpClient()</c>。
        ///
        /// 【原本的缺陷】
        /// 原本兩個方法各自使用 <c>using (var httpClient = new HttpClient())</c>。
        /// HttpClient 的 Dispose 並不會立即關閉底層的 TCP 連線，連線會停留在
        /// TIME_WAIT 狀態約 240 秒。每一次 LINE 登入都會建立兩條新連線，
        /// 在登入尖峰時可耗盡通訊埠而出現 SocketException（通訊埠耗盡）。
        /// 反過來若改成靜態 HttpClient，又會因為快取 DNS 而無法感知 api.line.me 的位址變更。
        ///
        /// 【修正後的作法】
        /// 改用 <c>IHttpClientFactory</c>。它會集中管理並輪替 HttpMessageHandler，
        /// 讓連線得以重用（消除通訊埠耗盡），同時定期回收 handler（解決 DNS 陳舊問題）。
        /// 由 factory 取得的 HttpClient 刻意「不」Dispose，這是官方建議的用法。
        /// <c>services.AddHttpClient()</c> 已於 Startup.ConfigureServices 註冊。
        /// </remarks>
        private HttpClient CreateLineApiHttpClient()
        {
            var factory = HttpContext.RequestServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
            if (factory == null)
            {
                throw new InvalidOperationException(
                    "IHttpClientFactory 未註冊。請確認 Startup.ConfigureServices 中的 services.AddHttpClient()。");
            }

            return factory.CreateClient(LineApiHttpClientName);
        }

        /// <summary>
        /// IHttpClientFactory 的具名客戶端名稱，讓 LINE API 呼叫共用同一組連線池。
        /// </summary>
        private const string LineApiHttpClientName = "LineLoginApi";

        /// <summary>
        /// 以授權碼換取 Access Token
        /// </summary>
        private async Task<LineTokenResponse> ExchangeCodeForToken(string code)
        {
            try
            {
                var configuration = HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
                if (configuration == null) return null;

                var channelId = configuration["LineLogin:ChannelId"];
                var channelSecret = configuration["LineLogin:ChannelSecret"];
                var callbackUrl = HttpContext.Session.GetString(LineLoginCallbackUrlSessionKey)
                    ?? ResolveLineLoginCallbackUrl(configuration);

                // ✅ 由 IHttpClientFactory 提供，連線可重用且不可 Dispose（見 CreateLineApiHttpClient 說明）。
                {
                    var httpClient = CreateLineApiHttpClient();
                    using var requestData = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("grant_type", "authorization_code"),
                        new KeyValuePair<string, string>("code", code),
                        new KeyValuePair<string, string>("redirect_uri", callbackUrl),
                        new KeyValuePair<string, string>("client_id", channelId),
                        new KeyValuePair<string, string>("client_secret", channelSecret)
                    });

                    using var response = await httpClient.PostAsync("oauth2/v2.1/token", requestData);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    // OAuth 回應可能包含 access_token、refresh_token 與 id_token；只記錄狀態與長度。
                    System.Diagnostics.Debug.WriteLine(
                        $"[ExchangeCodeForToken] HTTP {(int)response.StatusCode}; responseLength={responseBody?.Length ?? 0}");

                    if (response.IsSuccessStatusCode)
                    {
                        return JsonSerializer.Deserialize<LineTokenResponse>(responseBody, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ExchangeCodeForToken] HTTP error {(int)response.StatusCode}; responseLength={responseBody?.Length ?? 0}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ExchangeCodeForToken] 異常: {ex.Message}");
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
                // ✅ 由 IHttpClientFactory 提供，連線可重用且不可 Dispose（見 CreateLineApiHttpClient 說明）。
                {
                    var httpClient = CreateLineApiHttpClient();

                    // ⚠️【安全不變量】存取權杖必須掛在「單一請求」上，不可寫入 DefaultRequestHeaders。
                    // DefaultRequestHeaders 屬於 HttpClient 實例；一旦有人日後把這個 client 快取起來重用，
                    // 前一位使用者的 Bearer 權杖就會跟著送出，形成跨使用者的身分洩漏。
                    // 用 HttpRequestMessage 攜帶授權標頭可讓這個錯誤在結構上不可能發生。
                    using var request = new HttpRequestMessage(HttpMethod.Get, "v2/profile");
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                    using var response = await httpClient.SendAsync(request);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    // Profile 回應含有 LINE userId/displayName 等個人資料，不寫入原始內容。
                    System.Diagnostics.Debug.WriteLine(
                        $"[GetLineUserProfile] HTTP {(int)response.StatusCode}; responseLength={responseBody?.Length ?? 0}");

                    if (response.IsSuccessStatusCode)
                    {
                        return JsonSerializer.Deserialize<LineUserProfile>(responseBody, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[GetLineUserProfile] HTTP error {(int)response.StatusCode}; responseLength={responseBody?.Length ?? 0}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetLineUserProfile] 異常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 處理 LINE 用戶登入
        /// </summary>
        private async Task<IActionResult> ProcessLineUserLogin(string lineUserId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] 開始處理 LINE 用戶登入: {lineUserId}");

                var returnUrl = HttpContext.Session.GetString("_OAuthReturnUrl");
                if (!string.IsNullOrEmpty(returnUrl))
                {
                    System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] 偵測到自訂 ReturnUrl: {returnUrl}");
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
                        System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] Rejected non-local returnUrl from session: {returnUrl}");
                        returnUrl = null;
                    }

                    if (string.IsNullOrEmpty(returnUrl))
                    {
                        return RedirectToAction("Login");
                    }

                    var service = _organizationService;
                    var returnUrlQuery = new QueryExpression("contact")
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
                    var results = service.RetrieveMultiple(returnUrlQuery);
                    if (results.Entities.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 此 LINE ID 尚未綁定，重導向至綁定頁面");
                        return Redirect(GetBindingPageUrl());
                    }
                    System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] 找到聯絡人: {results.Entities[0].GetAttributeValue<string>("fullname")}");

                    try
                    {
                        var tempReturnUrl = returnUrl;
                        HttpContext.Session.Clear();
                        await HttpContext.Session.CommitAsync();
                        returnUrl = tempReturnUrl;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] 清除 Session 警告: {ex.Message}");
                    }

                    InMemoryContext.LineBindingViewModel.LineUserId = lineUserId;
                    await ProcessLogin(new GalleryViewModel { Account = "", Password = lineUserId });

                    System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] 使用自訂 ReturnUrl 重導向: {returnUrl}/{lineUserId}");
                    return Redirect($"{returnUrl}/{lineUserId}");
                }

                // 一般登入流程（無 returnUrl）
                var service2 = _organizationService;
                Entity foundContact2 = null;
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
                var results2 = service2.RetrieveMultiple(query);
                if (results2.Entities.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 此 LINE ID 尚未綁定，重導向至綁定頁面");
                    return Redirect(GetBindingPageUrl());
                }
                foundContact2 = results2.Entities[0];
                System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] 找到聯絡人: {foundContact2.GetAttributeValue<string>("fullname")}");

                try
                {
                    HttpContext.Session.Clear();
                    await HttpContext.Session.CommitAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] 清除 Session 警告: {ex.Message}");
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

                        System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] DisplayViewType: {displayViewType}, ActiveListId: {activeListId}");

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
                    System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] ProcessLogin 返回類型: {loginResult2?.GetType().Name}");
                    return loginResult2;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] 錯誤: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] 堆疊追蹤: {ex.StackTrace}");
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
            public string token_type { get; set; }
            public string refresh_token { get; set; }
            public int expires_in { get; set; }
            public string scope { get; set; }
            public string id_token { get; set; }
        }

        /// <summary>
        /// LINE User Profile
        /// </summary>
        private class LineUserProfile
        {
            public string userId { get; set; }
            public string displayName { get; set; }
            public string pictureUrl { get; set; }
            public string statusMessage { get; set; }
        }

        #endregion
    }
}
