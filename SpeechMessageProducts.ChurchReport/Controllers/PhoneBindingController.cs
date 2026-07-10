// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/PhoneBindingController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class PhoneBindingController
// 主要成員：ChangePhoneView、PhoneQrCodeView、PhoneQrCodeGetLineId
// 引用命名空間：ChurchReport.Models、ChurchReport.Tools、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Caching.Memory、System、ToolUtilityNameSpace.ConnectionOperations、ToolUtilityNameSpace.DependencyInjection
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Tools;
using Line.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 手機號碼變更與 QRCode 綁定控制器
    /// </summary>
    public class PhoneBindingController : BaseChurchController
    {
        private readonly LineMessagingClient _lineMessagingClient;

        public PhoneBindingController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool,
            LineMessagingClient lineMessagingClient)
            : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool)
        {
            _lineMessagingClient = lineMessagingClient ?? throw new ArgumentNullException(nameof(lineMessagingClient));
        }

        #region 單獨換手機號碼
        [HttpGet]
        [Route("/Home/ChangePhoneView/{LineIdLoginViewPatameter}")]
        [Route("/Phone/ChangePhoneView/{LineIdLoginViewPatameter}")]
        public IActionResult ChangePhoneView(string LineIdLoginViewPatameter)
        {
            try
            {
                var images = new System.Collections.Generic.List<string>();
                images.Add(Url.Content("~/assets/images/church-001.jpg"));
                images.Add(Url.Content("~/assets/images/church-002.jpg"));

                InMemoryContext.LineBindingViewModel.Images = images;
                TempData["Proponent"] = LineIdLoginViewPatameter;
                ViewBag.LiffId = LineIdLoginViewPatameter; // 同步 ViewBag，確保 View 可靠讀取 LIFF ID

                return View(InMemoryContext.LineBindingViewModel);
            }
            catch (System.Exception e)
            {
                return HandleError(e, "ChangePhoneView");
            }
        }
        #endregion

        #region QRcode 換手機號碼
        [HttpGet]
        [Route("/Phone/PhoneQrCodeView/{QrCodeViewPatameter}")]
        public IActionResult PhoneQrCodeView(string QrCodeId, string QrCodeViewPatameter)
        {
            try
            {
                // 控制 Navigation 下拉項目
                ViewBag.LoginType = "小組長";
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = InMemoryContext.ListManager.UserType = InMemoryContext.AppointmentsListManager.UserType;

                InMemoryContext.ListManager.QrCodeId = QrCodeId;

                TempData["Proponent"] = QrCodeViewPatameter;
                ViewBag.LiffId = QrCodeViewPatameter; // 同步 ViewBag，確保 View 可靠讀取 LIFF ID
                TempData["QrCodeId"] = QrCodeId;
                TempData["ClassName"] = " ";

                return View();
            }
            catch (System.Exception e)
            {
                return HandleError(e, "PhoneQrCodeView");
            }
        }

        [HttpPost]
        [Route("/Phone/PhoneQrCodeGetLineId")]
        public IActionResult PhoneQrCodeGetLineId(string DisplayName, string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                // 把LineUserId放在伺服器端了
                InMemoryContext.LineBindingViewModel.LineUserId = InMemoryContext.AppointmentsListManager.LineUserId = UserLineId;
                InMemoryContext.LineBindingViewModel.RoomId = InMemoryContext.AppointmentsListManager.RoomId = RoomId;
                InMemoryContext.LineBindingViewModel.GroupId = InMemoryContext.AppointmentsListManager.GroupId = GroupId;
                InMemoryContext.LineBindingViewModel.ViewType = InMemoryContext.AppointmentsListManager.ViewType = ViewType;

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

                // 控制 Navigation 下拉項目
                ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "單純行事曆";
                ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = InMemoryContext.ListManager.UserType = InMemoryContext.AppointmentsListManager.UserType;

                TempData["ClassName"] = "從相信到堅信";

                // 使用 QrCodeUtility 處理 QR Code 邏輯
                QrCodeUtility aQrCodeUtility = new QrCodeUtility(_lineMessagingClient);

                string UserName = "";
                string OnboardType = "";
                string ClassIndex = "";
                string ClassName = "";

                // 主日掃描後的相關設定 (補充缺少的兩個參數)
                aQrCodeUtility.SetupQrCodeIdString(
                    InMemoryContext.ListManager.QrCodeId,
                    DisplayName,
                    UserLineId,
                    ref UserName,
                    ref OnboardType,
                    ref ClassIndex,
                    ref ClassName);

                return Json(new { result = OnboardType, username = UserName, onboardtype = OnboardType });
            }
            catch (System.Exception e)
            {
                return HandleError(e, "PhoneQrCodeGetLineId");
            }
        }
        #endregion
    }
}
