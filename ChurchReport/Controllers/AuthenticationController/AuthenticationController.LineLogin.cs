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
        [HttpGet]
        [Route("/Authentication/LineIdLoginView/{LineIdLoginViewPatameter}")]
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

                IOrganizationService service = null;
                try
                {
                    service = GetConnection();

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
                        return Json(new
                        {
                            DisplayViewType = "尚未綁定",
                            ActiveListId = "",
                            message = "尚未綁定",
                            fullname = ""
                        });
                    }

                    // 有綁定：繼續走登入
                }
                catch (FaultException<OrganizationServiceFault>)
                {
                    throw;
                }
                catch (TimeoutException)
                {
                    throw;
                }
                finally
                {
                    ReleaseConnection(service);
                }

                var lineLoginViewModel = new GalleryViewModel
                {
                    Account = "",
                    Password = UserLineId
                };

                InMemoryContext.LineBindingViewModel.LineUserId = UserLineId;

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
