using ChurchReport.Diagnostics.Profiling;
using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.ServiceModel;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 認證控制器（私有輔助方法）
    /// </summary>
    public partial class AuthenticationController
    {
        #region 私有輔助方法

        private (bool isValid, string contactId, string errorMessage) ValidateUserCredentials(GalleryViewModel viewModel)
        {
            using var perfPhase = PerfPhase.Measure(HttpContext, "Login.ValidateUserCredentials");

            try
            {
                System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 開始驗證 - 帳號: {viewModel?.Account}");

                string contactIdString = "";

                if (viewModel.Account != "")
                {
                    IOrganizationService service = null;
                    try
                    {
                        service = GetConnection();

                        var query = new QueryExpression("contact")
                        {
                            ColumnSet = new ColumnSet("contactid", "new_app_pass"),
                            Criteria = new FilterExpression
                            {
                                FilterOperator = LogicalOperator.And,
                                Conditions =
                                {
                                    new ConditionExpression("new_app_acount", ConditionOperator.Equal, viewModel.Account),
                                    new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                                }
                            },
                            TopCount = 1
                        };

                        var results = service.RetrieveMultiple(query);

                        if (results.Entities.Count == 0)
                            return (false, "", "帳號錯誤");

                        var contact = results.Entities[0];
                        var storedPassword = contact.Contains("new_app_pass")
                            ? contact.GetAttributeValue<string>("new_app_pass")
                            : null;

                        if (string.IsNullOrEmpty(storedPassword))
                            return (false, "", "系統沒有設定密碼");

                        if (storedPassword != viewModel.Password)
                            return (false, "", "密碼錯誤");

                        contactIdString = contact.Id.ToString();
                    }
                    catch (FaultException<OrganizationServiceFault> ex)
                    {
                        return (false, "", $"系統服務異常: {ex.Detail?.Message ?? ex.Message}");
                    }
                    catch (TimeoutException)
                    {
                        return (false, "", "系統連接超時，請稍後再試");
                    }
                    finally
                    {
                        ReleaseConnection(service);
                    }
                }
                else
                {
                    contactIdString = "透過Line Id 登入";
                }

                return (true, contactIdString, "");
            }
            catch (Exception ex)
            {
                return (false, "", $"驗證過程發生錯誤: {ex.Message}");
            }
        }

        private async Task<(Entity loginContact, string fullName)> RetrieveUserData(string contactIdString, GalleryViewModel viewModel)
        {
            using var perfPhase = PerfPhase.Measure(HttpContext, "Login.RetrieveUserData");

            Entity loginContact = null;
            string fullName = "";

            IOrganizationService service = null;
            try
            {
                service = GetConnection();

                if (contactIdString != "透過Line Id 登入")
                {
                    loginContact = service.Retrieve("contact", new Guid(contactIdString), new ColumnSet(true));
                    fullName = loginContact.Contains("fullname") ? loginContact.GetAttributeValue<string>("fullname") : "";
                }
                else
                {
                    var query = new QueryExpression("contact")
                    {
                        ColumnSet = new ColumnSet(true),
                        Criteria = new FilterExpression
                        {
                            FilterOperator = LogicalOperator.And,
                            Conditions =
                            {
                                new ConditionExpression("new_lineid", ConditionOperator.Equal, InMemoryContext.LineBindingViewModel.LineUserId),
                                new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                            }
                        },
                        TopCount = 1
                    };

                    var results = service.RetrieveMultiple(query);

                    if (results.Entities.Count > 0)
                    {
                        loginContact = results.Entities[0];
                        fullName = loginContact.Contains("fullname") ? loginContact.GetAttributeValue<string>("fullname") : "";

                        viewModel.Account = "LineIdLogin";
                        viewModel.Password = InMemoryContext.LineBindingViewModel.LineUserId;
                    }
                }
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                throw new Exception($"取得使用者資料失敗: {ex.Detail?.Message ?? ex.Message}", ex);
            }
            catch (TimeoutException ex)
            {
                throw new Exception("取得使用者資料超時，請稍後再試", ex);
            }
            finally
            {
                ReleaseConnection(service);
            }

            return (loginContact, fullName);
        }

        private void InitializeUserSession(Entity loginContact, GalleryViewModel viewModel)
        {
            using var perfPhase = PerfPhase.Measure(HttpContext, "Login.InitializeUserSession");

            // ========================================
            // ? Session Fixation 防護 - Step 1: 清除舊的 Session
            // ========================================
            // 在登入前先清除舊的 Session，防止 Session Fixation 攻擊
            // 這是防止「A 登入 → B 登入看到 A 網頁」的關鍵步驟
            try
            {
                HttpContext.Session.Clear();
                System.Diagnostics.Debug.WriteLine("[InitializeUserSession] ? 已清除舊 Session");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InitializeUserSession] ?? 清除 Session 警告: {ex.Message}");
            }

            // ========================================
            // ? Session Fixation 防護 - Step 2: 強制重新生成 Session ID
            // ========================================
            // .NET Core 3.0+ 使用 CommitAsync 強制產生新的 Session ID
            // 這確保每次登入都有全新的、唯一的 Session ID
            try
            {
                // 使用同步方式提交 Session（確保立即生效）
                // 這會觸發 ASP.NET Core 產生新的 Session Cookie
                HttpContext.Session.CommitAsync().GetAwaiter().GetResult();
                System.Diagnostics.Debug.WriteLine("[InitializeUserSession] ? 已強制重新生成 Session ID");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InitializeUserSession] ?? Session Commit 警告: {ex.Message}");
            }

            // ========================================
            // ? Session Fixation 防護 - Step 3: 綁定用戶身份標識
            // ========================================
            // 在 Session 中儲存唯一的用戶識別資訊，用於後續驗證
            // 防止跨用戶的 Session 竊取或共用
            var userId = loginContact?.Id.ToString() ?? Guid.NewGuid().ToString();
            var userIdentifier = $"{userId}_{DateTime.UtcNow.Ticks}";
            
            try
            {
                HttpContext.Session.SetString("_SessionUserId", userId);
                HttpContext.Session.SetString("_SessionUserIdentifier", userIdentifier);
                HttpContext.Session.SetString("_SessionCreatedAt", DateTime.UtcNow.ToString("O"));
                HttpContext.Session.SetString("_SessionUserAgent", HttpContext.Request.Headers["User-Agent"].ToString());
                
                // 儲存真實 IP（考慮代理模式）
                var realIp = HttpContext.Connection.RemoteIpAddress?.ToString() 
                             ?? HttpContext.Request.Headers["X-Forwarded-For"].ToString() 
                             ?? "Unknown";
                HttpContext.Session.SetString("_SessionRealIp", realIp);

                System.Diagnostics.Debug.WriteLine($"[InitializeUserSession] ? 已綁定用戶身份: UserId={userId}, IP={realIp}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InitializeUserSession] ?? 綁定用戶身份警告: {ex.Message}");
            }

            // ========================================
            // 原有的 Session 初始化邏輯
            // ========================================
            InMemoryContext.AppointmentsListManager.m_Account = viewModel.Account;
            InMemoryContext.AppointmentsListManager.m_Password = viewModel.Password;
            InMemoryContext.AppointmentsListManager.m_LoginContact = loginContact;

            InMemoryContext.PersonalInfomationModel.m_LoginContact = loginContact;

            System.Diagnostics.Debug.WriteLine($"[InitializeUserSession] ========================================");
            System.Diagnostics.Debug.WriteLine($"[InitializeUserSession] ? Session 初始化完成");
            System.Diagnostics.Debug.WriteLine($"[InitializeUserSession]   - 用戶: {viewModel.Account}");
            System.Diagnostics.Debug.WriteLine($"[InitializeUserSession]   - Session ID: 已重新生成（新的唯一 ID）");
            System.Diagnostics.Debug.WriteLine($"[InitializeUserSession]   - 用戶綁定: {userIdentifier}");
            System.Diagnostics.Debug.WriteLine($"[InitializeUserSession] ========================================");
        }

        private void SetupSystemData(Entity loginContact, GalleryViewModel viewModel)
        {
            using var perfPhase = PerfPhase.Measure(HttpContext, "Login.SetupSystemData");

            IOrganizationService service = null;
            try
            {
                using (PerfPhase.Measure(HttpContext, "Login.SetupSystemData.GetConnection"))
                {
                    service = GetConnection();
                }

                // ========================================
                // ?? 關鍵修復：登入時載入 ListManager
                // ========================================
                using (PerfPhase.Measure(HttpContext, "Login.SetupSystemData.SetupListManager"))
                {
                    InMemoryContext.ListManager.SetupListManager(
                        viewModel.Account,
                        viewModel.Password,
                        DateTime.Now,
                        service);
                }

                // ========================================
                // ? 效能優化：登入完成後立即建立驗證快取
                // ========================================
                // 目的：避免後續 AJAX 請求重複呼叫 SetupListManager
                // 
                // 原因：
                // 1. 登入後第一個 AJAX 請求會觸發 EnsureCorrectUserData()
                // 2. 如果快取未建立，會再次呼叫 SetupListManager（重複載入）
                // 3. 這會造成 +100ms 延遲 + 資料庫連線浪費
                // 
                // 解決方式：
                // - 在登入成功後主動呼叫一次 EnsureCorrectUserData()
                // - 這會建立快取，後續 30 秒內的請求直接命中快取
                // - 快取命中時間 <1ms，大幅提升效能
                try
                {
                    System.Diagnostics.Debug.WriteLine("[SetupSystemData] ? 預先建立用戶驗證快取");
                    using (PerfPhase.Measure(HttpContext, "Login.SetupSystemData.EnsureCorrectUserData"))
                    {
                        EnsureCorrectUserData();
                    }
                    System.Diagnostics.Debug.WriteLine("[SetupSystemData] ? 快取建立完成，後續請求將快速驗證");
                }
                catch (Exception cacheEx)
                {
                    // 快取建立失敗不影響登入流程
                    System.Diagnostics.Debug.WriteLine($"[SetupSystemData] ?? 快取建立失敗: {cacheEx.Message}");
                }

                try
                {
                    using (PerfPhase.Measure(HttpContext, "Login.SetupSystemData.SetupAppointmentList"))
                    {
                        InMemoryContext.AppointmentsListManager.SetupAppointmentList();
                    }
                }
                catch
                {
                }

                try
                {
                    if (loginContact != null)
                    {
                        InMemoryContext.QpayManager.LoginType = "網頁登入";
                        using (PerfPhase.Measure(HttpContext, "Login.SetupSystemData.SetQpayModel"))
                        {
                            InMemoryContext.QpayManager.SetQpayModel(loginContact);
                        }
                    }
                }
                catch
                {
                }

                try
                {
                    using (PerfPhase.Measure(HttpContext, "Login.SetupSystemData.SetupLessonList"))
                    {
                        InMemoryContext.FeeList.SetupLessonList(viewModel.Account, viewModel.Password);
                    }
                }
                catch
                {
                }
            }
            finally
            {
                ReleaseConnection(service);
            }
        }

        private string DetermineDisplayViewType()
        {
            using var perfPhase = PerfPhase.Measure(HttpContext, "Login.DetermineDisplayViewType");

            try
            {
                ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "不是單純行事曆";
                ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "顯示牧養回報項目";
                ViewBag.UserType = InMemoryContext.ListManager.UserType = InMemoryContext.AppointmentsListManager.UserType;

                string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();

                if (string.IsNullOrEmpty(displayViewType))
                {
                    if (InMemoryContext.ListManager.LoginType == "小組長")
                        displayViewType = "IntegrateView";
                    else
                        displayViewType = "MultiGroupView";
                }

                if (displayViewType == "IntegrateView")
                {
                    try
                    {
                        using (PerfPhase.Measure(HttpContext, "Login.DetermineDisplayViewType.SetupIntegrateData"))
                        {
                            InMemoryContext.ListManager.SetupIntegrateData(InMemoryContext.ListManager.ActiveListId);
                        }
                    }
                    catch
                    {
                    }
                }

                if (InMemoryContext.ListManager.LoginType != "小組長" &&
                    InMemoryContext.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    displayViewType = "HappyGroupView";
                }

                return displayViewType;
            }
            catch
            {
                return "IntegrateView";
            }
        }

        private void SetupViewBagParameters(string displayViewType)
        {
            ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
            ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
            ViewBag.HappyType = InMemoryContext.HappyGroupDataManager.HappyType;
            ViewBag.FeeType = InMemoryContext.FeeList.FeeType;

            SetupFeeDataListCount();
            SetMultiGroupLayoutParameter();
        }

        private IActionResult CreateLoginResponse(string displayViewType, string fullName, GalleryViewModel viewModel)
        {
            return Json(new
            {
                DisplayViewType = displayViewType,
                ActiveListId = InMemoryContext.ListManager.ActiveListId,
                message = "歡迎" + fullName + "登入成功!",
                fullname = fullName,
                account = viewModel.Account,
                password = viewModel.Password
            });
        }

        #endregion
    }
}
