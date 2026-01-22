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
    /// 架構：Server-side OAuth 2.0 流程，完全不依賴前端 LIFF SDK
    /// 
    /// 流程：
    /// 1. LineLoginStart → 重導向至 LINE OAuth 授權頁面
    /// 2. LINE OAuth → 用戶授權 → 回調 LineCallback
    /// 3. LineCallback → 用 code 換取 access_token → 取得 user profile → 登入系統
    /// </summary>
    public partial class AuthenticationController
    {
        #region LINE Login Server-side OAuth 2.0

        /// <summary>
        /// 開始 LINE Login OAuth 流程
        /// 重導向用戶至 LINE 授權頁面
        /// </summary>
        [HttpGet]
        [Route("/Authentication/LineLoginStart")]
        public IActionResult LineLoginStart()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[LineLoginStart] ========== 開始 LINE Login OAuth 流程 ==========");

                // 從 HttpContext.RequestServices 取得 IConfiguration
                var configuration = HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
                if (configuration == null)
                {
                    return Json(new { success = false, message = "無法取得系統配置" });
                }

                // 從 appsettings.json 讀取配置
                var channelId = configuration["LineLogin:ChannelId"];
                var callbackUrl = configuration["LineLogin:CallbackUrl"];
                var scope = configuration["LineLogin:Scope"] ?? "profile openid";

                if (string.IsNullOrEmpty(channelId))
                {
                    return Json(new { success = false, message = "LINE Login Channel ID 未設定" });
                }

                if (string.IsNullOrEmpty(callbackUrl))
                {
                    return Json(new { success = false, message = "LINE Login Callback URL 未設定" });
                }

                // 生成隨機 state 用於 CSRF 防護
                var state = GenerateRandomState();
                HttpContext.Session.SetString("_LineLoginState", state);

                // 生成隨機 nonce 用於 ID Token 驗證
                var nonce = GenerateRandomNonce();
                HttpContext.Session.SetString("_LineLoginNonce", nonce);

                // 建構 LINE OAuth 授權 URL
                var authUrl = "https://access.line.me/oauth2/v2.1/authorize?" +
                             $"response_type=code" +
                             $"&client_id={Uri.EscapeDataString(channelId)}" +
                             $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
                             $"&state={Uri.EscapeDataString(state)}" +
                             $"&scope={Uri.EscapeDataString(scope)}" +
                             $"&nonce={Uri.EscapeDataString(nonce)}";

                System.Diagnostics.Debug.WriteLine($"[LineLoginStart] 授權 URL: {authUrl}");
                System.Diagnostics.Debug.WriteLine($"[LineLoginStart] State: {state}");
                System.Diagnostics.Debug.WriteLine($"[LineLoginStart] Nonce: {nonce}");

                // 重導向至 LINE OAuth 授權頁面
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
        /// 處理 LINE OAuth 授權後的回調
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

                // 檢查是否有錯誤
                if (!string.IsNullOrEmpty(error))
                {
                    System.Diagnostics.Debug.WriteLine($"[LineCallback] LINE OAuth 錯誤: {error} - {error_description}");
                    return RedirectToAction("Login", new { error = $"LINE 登入失敗: {error_description}" });
                }

                // 驗證 state 防止 CSRF 攻擊
                var sessionState = HttpContext.Session.GetString("_LineLoginState");
                if (string.IsNullOrEmpty(sessionState) || sessionState != state)
                {
                    System.Diagnostics.Debug.WriteLine("[LineCallback] State 驗證失敗！可能是 CSRF 攻擊");
                    return RedirectToAction("Login", new { error = "State 驗證失敗，請重新登入" });
                }

                // 清除 session 中的 state
                HttpContext.Session.Remove("_LineLoginState");

                // 用 code 換取 access_token
                var tokenResponse = await ExchangeCodeForToken(code);
                if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.access_token))
                {
                    System.Diagnostics.Debug.WriteLine("[LineCallback] 取得 Access Token 失敗");
                    return RedirectToAction("Login", new { error = "取得 LINE Access Token 失敗" });
                }

                System.Diagnostics.Debug.WriteLine($"[LineCallback] Access Token: {tokenResponse.access_token.Substring(0, 20)}...");

                // 用 access_token 取得用戶 profile
                var userProfile = await GetLineUserProfile(tokenResponse.access_token);
                if (userProfile == null || string.IsNullOrEmpty(userProfile.userId))
                {
                    System.Diagnostics.Debug.WriteLine("[LineCallback] 取得用戶 Profile 失敗");
                    return RedirectToAction("Login", new { error = "取得 LINE 用戶資料失敗" });
                }

                System.Diagnostics.Debug.WriteLine($"[LineCallback] 用戶 ID: {userProfile.userId}");
                System.Diagnostics.Debug.WriteLine($"[LineCallback] 用戶名稱: {userProfile.displayName}");

                // 更新 InMemoryContext
                InMemoryContext.LineBindingViewModel.LineUserId = userProfile.userId;
                InMemoryContext.LineBindingViewModel.DisplayId = userProfile.userId;

                // 執行登入流程（與原有的 SaveUserLineId 邏輯相同）
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
        /// 生成隨機 state 用於 CSRF 防護
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
        /// 生成隨機 nonce 用於 ID Token 驗證
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
        /// 用授權碼換取 Access Token
        /// </summary>
        private async Task<LineTokenResponse> ExchangeCodeForToken(string code)
        {
            try
            {
                var configuration = HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
                if (configuration == null) return null;

                var channelId = configuration["LineLogin:ChannelId"];
                var channelSecret = configuration["LineLogin:ChannelSecret"];
                var callbackUrl = configuration["LineLogin:CallbackUrl"];

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
        /// 用 Access Token 取得用戶 Profile
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

                // 檢查用戶是否已綁定
                IOrganizationService service = null;
                Entity foundContact = null;
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
                        System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] 該 LINE ID 尚未綁定");
                        // 重導向至綁定頁面
                        return Redirect("/Authentication/LineLiffView/1653819697-YkPyPkr6");
                    }

                    foundContact = results.Entities[0];
                    System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] 找到聯絡人: {foundContact.GetAttributeValue<string>("fullname")}");
                }
                finally
                {
                    ReleaseConnection(service);
                }

                // 清除舊 Session（防止 Session Fixation）
                System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] ? 清除舊 Session（防止跨用戶洩漏）");
                try
                {
                    HttpContext.Session.Clear();
                    await HttpContext.Session.CommitAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] ?? 清除 Session 警告: {ex.Message}");
                }

                // 建立登入 ViewModel
                var lineLoginViewModel = new GalleryViewModel
                {
                    Account = "",
                    Password = lineUserId
                };

                InMemoryContext.LineBindingViewModel.LineUserId = lineUserId;

                // 執行標準登入流程
                System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] ? 呼叫標準登入流程");
                var loginResult = await ProcessLogin(lineLoginViewModel);

                // ========================================
                // ? P0: 處理登入結果並重導向
                // ========================================
                // ProcessLogin 返回 JSON，但在 OAuth 流程中我們需要重導向
                if (loginResult is JsonResult jsonResult)
                {
                    System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] ? 登入成功，解析返回結果");
                    
                    // 從 JSON 結果中取得資料
                    var resultValue = jsonResult.Value;
                    var resultType = resultValue.GetType();
                    
                    // 使用反射取得屬性值
                    var displayViewTypeProperty = resultType.GetProperty("DisplayViewType");
                    var activeListIdProperty = resultType.GetProperty("ActiveListId");
                    var messageProperty = resultType.GetProperty("message");
                    
                    if (displayViewTypeProperty != null && activeListIdProperty != null)
                    {
                        var displayViewType = displayViewTypeProperty.GetValue(resultValue)?.ToString();
                        var activeListId = activeListIdProperty.GetValue(resultValue)?.ToString();
                        var message = messageProperty?.GetValue(resultValue)?.ToString();
                        
                        System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] DisplayViewType: {displayViewType}");
                        System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] ActiveListId: {activeListId}");
                        System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] Message: {message}");
                        
                        // 根據 DisplayViewType 重導向至對應頁面
                        if (displayViewType == "MultiGroupView")
                        {
                            System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] ? 重導向至 MultiGroupView: {activeListId}");
                            return Redirect($"/SmallGroup/MultiGroupView/{activeListId}");
                        }
                        else if (displayViewType == "IntegrateView")
                        {
                            System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] ? 重導向至 IntegrateView: {activeListId}");
                            return Redirect($"/SmallGroup/IntegrateView/{activeListId}");
                        }
                        else if (displayViewType == "HappyGroupView")
                        {
                            System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] ? 重導向至 HappyGroupView");
                            return Redirect("/SmallGroup/HappyGroup");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] ?? 未知的 DisplayViewType: {displayViewType}");
                            return Redirect("/Home/Index");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[ProcessLineUserLogin] ?? 無法從 JSON 結果中取得必要屬性");
                        return Redirect("/Home/Index");
                    }
                }
                else
                {
                    // 如果 ProcessLogin 返回的不是 JSON，直接返回
                    System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] ?? ProcessLogin 返回類型: {loginResult?.GetType().Name}");
                    return loginResult;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] ? 錯誤: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[ProcessLineUserLogin] 堆疊追蹤: {ex.StackTrace}");
                return Redirect("/Authentication/Login?error=" + Uri.EscapeDataString("登入失敗，請稍後再試"));
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
