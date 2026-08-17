// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLogin.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class AuthenticationController
// 主要成員：LineIdLoginView、SaveUserLineId、ProcessLineLogin
// 引用命名空間：ChurchReport.ViewModel、ChurchReport.ViewModels、Microsoft.AspNetCore.Mvc、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、System、System.ServiceModel、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.ViewModel;
using ChurchReport.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.ServiceModel;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 認證控制器（LINE 登入）
    /// </summary>
    public partial class AuthenticationController
    {
        #region LINE 登入

        /// <summary>
        /// LINE ID 登入頁面
        /// 顯示 LINE 登入表單
        /// </summary>
        // 同一個 action 同時掛 /Authentication/ 與 /Home/ 兩個路由，兩者皆「就地渲染」此畫面。
        // 重點：/Home/... 不可再 302 轉址到 /Authentication/...，否則執行 liff.login() 時頁面網址
        // 已變成 /Authentication/...，與 LINE Console 登錄的 LIFF Endpoint URL（/home/...）不符，
        // LINE 會回 400「invalid url ... redirect_uri」。直接在 /home/ 渲染可讓網址與 Endpoint 一致。
        [HttpGet]
        [Route("/Authentication/LineIdLoginView/{LineIdLoginViewPatameter}")]
        [Route("/Home/LineIdLoginView/{LineIdLoginViewPatameter}")]
        public IActionResult LineIdLoginView(string LineIdLoginViewPatameter)
        {
            try
            {
                var images = BuildHeroImages(
                    "~/assets/images/church-001.jpg",
                    "~/assets/images/church-002.jpg"
                );

                InMemoryContext.LineBindingViewModel.Images = images;
                TempData["Proponent"] = LineIdLoginViewPatameter;
                ViewBag.LiffId = LineIdLoginViewPatameter; // 同步設定 ViewBag，確保 View 能可靠讀取

                return View(InMemoryContext.LineBindingViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "LineIdLoginView");
            }
        }

        /// <summary>
        /// 儲存 LINE 使用者 ID 並進入登入流程
        /// </summary>
        [HttpPost]
        [Route("/Authentication/SaveUserLineId")]
        public async Task<IActionResult> SaveUserLineId(string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("========================================");
                System.Diagnostics.Debug.WriteLine("[SaveUserLineId] ===== 開始處理 LINE 登入請求 =====");
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                System.Diagnostics.Debug.WriteLine("[SaveUserLineId] 請求參數:");
                System.Diagnostics.Debug.WriteLine($"  - UserLineId: {UserLineId}");
                System.Diagnostics.Debug.WriteLine($"  - GroupId: {GroupId ?? "(null)"}");
                System.Diagnostics.Debug.WriteLine($"  - RoomId: {RoomId ?? "(null)"}");
                System.Diagnostics.Debug.WriteLine($"  - ViewType: {ViewType ?? "(null)"}");
                System.Diagnostics.Debug.WriteLine("========================================");

                System.Diagnostics.Debug.WriteLine("[SaveUserLineId] 步驟 2: 設定 LINE 相關資訊到 InMemoryContext");

                InMemoryContext.LineBindingViewModel.LineUserId = UserLineId;
                InMemoryContext.LineBindingViewModel.RoomId = RoomId;
                InMemoryContext.LineBindingViewModel.GroupId = GroupId;
                InMemoryContext.LineBindingViewModel.ViewType = ViewType;

                System.Diagnostics.Debug.WriteLine("[SaveUserLineId] 步驟 3: 設定 DisplayId");

                if (!string.IsNullOrEmpty(GroupId))
                {
                    InMemoryContext.LineBindingViewModel.DisplayId = GroupId;
                }
                else if (!string.IsNullOrEmpty(RoomId))
                {
                    InMemoryContext.LineBindingViewModel.DisplayId = RoomId;
                }
                else
                {
                    InMemoryContext.LineBindingViewModel.DisplayId = UserLineId;
                }

                System.Diagnostics.Debug.WriteLine("[SaveUserLineId] 步驟 4: 檢查用戶是否已在資料庫中綁定");

                var service = _organizationService;
                Entity foundContact = null;
                try
                {
                    var query = new QueryExpression("contact")
                    {
                        ColumnSet = new ColumnSet("contactid", "fullname"),
                        Criteria = new FilterExpression
                        {
                            FilterOperator = LogicalOperator.And,
                            Conditions =
                            {
                                new ConditionExpression("new_lineid", ConditionOperator.Equal, UserLineId),
                                new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                            }
                        },
                        TopCount = 1
                    };

                    var results = service.RetrieveMultiple(query);

                    if (results.Entities.Count == 0)
                    {
                        // ? 增強診斷：記錄未匹配的 userId，協助排查 Provider 不同的問題
                        System.Diagnostics.Debug.WriteLine("[SaveUserLineId] ? 此 LINE ID 尚未綁定");
                        System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ? 搜尋的 UserLineId: {UserLineId}");
                        System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ? 若此 userId 與 CRM 中已綁定的 new_lineid 不同，");
                        System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ? 可能是 Mini App Channel 與綁定 Channel 在不同的 LINE Provider 下");

                        return Json(new
                        {
                            DisplayViewType = "尚未綁定",
                            ActiveListId = "",
                            message = "尚未綁定",
                            fullname = ""
                        });
                    }

                    // 找到聯絡人
                    foundContact = results.Entities[0];
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ? 找到聯絡人: {foundContact.GetAttributeValue<string>("fullname")}");
                }
                catch (FaultException<OrganizationServiceFault>)
                {
                    throw;
                }
                catch (TimeoutException)
                {
                    throw;
                }

                // ========================================
                // ? Session Fixation 防護 - LINE 登入前清除舊 Session
                // ========================================
                // 在處理 LINE 登入前，先清除任何可能存在的舊 Session
                // 這確保 A 用戶透過 LINE 登入後，B、C 用戶登入不會看到 A 的網頁
                System.Diagnostics.Debug.WriteLine("[SaveUserLineId] ? 清除舊 Session（防止跨用戶洩漏）");
                try
                {
                    HttpContext.Session.Clear();
                    await HttpContext.Session.CommitAsync();
                    System.Diagnostics.Debug.WriteLine("[SaveUserLineId] ? 舊 Session 已清除");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ?? 清除 Session 警告: {ex.Message}");
                }

                // 有綁定：繼續走登入
                var lineLoginViewModel = new GalleryViewModel
                {
                    Account = "",
                    Password = UserLineId
                };

                InMemoryContext.LineBindingViewModel.LineUserId = UserLineId;

                System.Diagnostics.Debug.WriteLine("[SaveUserLineId] ? 準備執行 LINE 登入流程");
                return await ProcessLogin(lineLoginViewModel);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("========================================");
                System.Diagnostics.Debug.WriteLine("[SaveUserLineId] ??? 發生未預期的錯誤 ???");
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 異常類型: {e.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 異常訊息: {e.Message}");
                System.Diagnostics.Debug.WriteLine("[SaveUserLineId] 堆疊追蹤:");
                System.Diagnostics.Debug.WriteLine(e.StackTrace);
                System.Diagnostics.Debug.WriteLine("========================================\n");

                return HandleError(e, "SaveUserLineId");
            }
        }

        /// <summary>
        /// 處理 LINE 登入
        /// </summary>
        [HttpPost]
        [Route("/Authentication/ProcessLineLogin")]
        public async Task<IActionResult> ProcessLineLogin()
        {
            try
            {
                var lineLoginViewModel = new GalleryViewModel
                {
                    Account = "",
                    Password = InMemoryContext.LineBindingViewModel.LineUserId
                };

                return await ProcessLogin(lineLoginViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "ProcessLineLogin");
            }
        }

        #endregion
    }
}
