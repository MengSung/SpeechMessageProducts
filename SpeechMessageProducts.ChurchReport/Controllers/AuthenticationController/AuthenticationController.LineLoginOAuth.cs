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

                System.Diagnostics.Debug.WriteLine($"[LineCallback] Access Token 前20字: {tokenResponse.access_token.Substring(0, Math.Min(20, tokenResponse.access_token.Length))}...");

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

                using (var httpClient = new HttpClient())
                {
                    var requestData = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("grant_type", "authorization_code"),
                        new KeyValuePair<string, string>("code", code),
                        new KeyValuePair<string, string>("redirect_uri", callbackUrl),
                        new KeyValuePair<string, string>("client_id", channelId),
                        new KeyValuePair<string, string>("client_secret", channelSecret)
                    });

                    var response = await httpClient.PostAsync("https://api.line.me/oauth2/v2.1/token", requestData);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    System.Diagnostics.Debug.WriteLine($"[ExchangeCodeForToken] Response: {responseBody}");

                    if (response.IsSuccessStatusCode)
                    {
                        return JsonSerializer.Deserialize<LineTokenResponse>(responseBody, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExchangeCodeForToken] 錯誤: {response.StatusCode} - {responseBody}");
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
                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                    var response = await httpClient.GetAsync("https://api.line.me/v2/profile");
                    var responseBody = await response.Content.ReadAsStringAsync();

                    System.Diagnostics.Debug.WriteLine($"[GetLineUserProfile] Response: {responseBody}");

                    if (response.IsSuccessStatusCode)
                    {
                        return JsonSerializer.Deserialize<LineUserProfile>(responseBody, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[GetLineUserProfile] 錯誤: {response.StatusCode} - {responseBody}");
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
