using ChurchReport.Models;
using ChurchReport.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Threading.Tasks;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// QR Code 控制器
    /// 處理各類 QR Code 掃描功能 (課程簽到退、主日、小組、個人)
    /// </summary>
    public class QrCodeController : BaseChurchController
    {
        #region 建構函式

        public QrCodeController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService,
            IToolUtilityProvider toolUtilityProvider)
            : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider)
        {
        }

        #endregion

        #region 教會課程 QR CODE (簽到、簽退、報名)

        /// <summary>
        /// 教會課程 QR Code 掃描頁面
        /// 用於課程簽到、簽退與報名
        /// </summary>
        /// <param name="QrCodeId">QR Code 識別碼</param>
        /// <param name="QrCodeViewPatameter">頁面參數</param>
        [Route("/QrCode/CourseView/{QrCodeViewPatameter}")]
        public IActionResult QrCodeView(string QrCodeId, string QrCodeViewPatameter)
        {
            try
            {
                SetupQrCodeViewBag();

                // 儲存 QR Code ID
                InMemoryContext.ListManager.QrCodeId = QrCodeId;

                // 傳遞參數給頁面
                TempData["Proponent"] = QrCodeViewPatameter;
                TempData["QrCodeId"] = QrCodeId;
                TempData["ClassName"] = " ";

                return View();
            }
            catch (Exception e)
            {
                return HandleError(e, "QrCodeView");
            }
        }

        /// <summary>
        /// 取得課程 QR Code 掃描後的資訊
        /// </summary>
        [HttpPost]
        public IActionResult QrCodeGetLineId(
            string DisplayName,
            string UserLineId,
            string GroupId,
            string RoomId,
            string ViewType)
        {
            try
            {
                SetupLineContext(UserLineId, GroupId, RoomId, ViewType);

                QrCodeUtility qrCodeUtility = new QrCodeUtility();

                string className = "";
                string userName = "";
                string classIndex = "";
                string onboardType = "";

                // 解析 QR Code 並處理簽到/簽退
                qrCodeUtility.SetupQrCodeIdString(
                    InMemoryContext.ListManager.QrCodeId,
                    DisplayName,
                    UserLineId,
                    ref className,
                    ref userName,
                    ref classIndex,
                    ref onboardType);

                return Json(new
                {
                    result = onboardType,
                    classname = className,
                    username = userName,
                    classindex = classIndex,
                    onboardtype = onboardType
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "QrCodeGetLineId");
            }
        }

        #endregion

        #region 問卷調查 QR CODE

        /// <summary>
        /// 問卷調查 QR Code 頁面
        /// </summary>
        [Route("/QrCode/PollView/{PollQrCodeViewPatameter}")]
        public IActionResult PollQrCodeView(string QrCodeId, string PollQrCodeViewPatameter)
        {
            try
            {
                SetupQrCodeViewBag();

                InMemoryContext.ListManager.QrCodeId = QrCodeId;

                TempData["Proponent"] = PollQrCodeViewPatameter;
                TempData["QrCodeId"] = QrCodeId;
                TempData["ClassName"] = " ";

                PollManager pollManager = new PollManager();
                return View(pollManager.SetDisplayFlag(QrCodeId));
            }
            catch (Exception e)
            {
                return HandleError(e, "PollQrCodeView");
            }
        }

        /// <summary>
        /// 取得問卷 QR Code 掃描者資訊
        /// </summary>
        [HttpPost]
        public IActionResult PollQrCodeGetLineId(
            string DisplayName,
            string UserLineId,
            string GroupId,
            string RoomId,
            string ViewType)
        {
            try
            {
                SetupLineContext(UserLineId, GroupId, RoomId, ViewType);

                // 取得掃描者全名
                string userName = GetUserFullName(UserLineId);

                PollManager pollManager = new PollManager();
                string className = pollManager.GetClassName(InMemoryContext.ListManager.QrCodeId);

                return Json(new
                {
                    result = "",
                    classname = className,
                    username = userName,
                    classindex = "",
                    onboardtype = ""
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "PollQrCodeGetLineId");
            }
        }

        /// <summary>
        /// 儲存問卷回答
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SavePoll(PollModel aPollModel)
        {
            try
            {
                PollManager pollManager = new PollManager();

                return await pollManager.SavePoll(
                    aPollModel,
                    InMemoryContext.ListManager.QrCodeId,
                    InMemoryContext.LineBindingViewModel.LineUserId);
            }
            catch (Exception e)
            {
                return HandleError(e, "SavePoll");
            }
        }

        #endregion

        #region 小組聚會 QR CODE

        /// <summary>
        /// 小組聚會 QR Code 簽到退頁面
        /// </summary>
        [Route("/QrCode/SmallGroupView/{QrCodeViewPatameter}")]
        public IActionResult SmallGroupQrCodeView(string QrCodeId, string QrCodeViewPatameter)
        {
            try
            {
                SetupQrCodeViewBag();

                InMemoryContext.ListManager.QrCodeId = QrCodeId;

                TempData["Proponent"] = QrCodeViewPatameter;
                TempData["QrCodeId"] = QrCodeId;
                TempData["ClassName"] = " ";

                return View();
            }
            catch (Exception e)
            {
                return HandleError(e, "SmallGroupQrCodeView");
            }
        }

        /// <summary>
        /// 取得小組 QR Code 掃描資訊
        /// </summary>
        [HttpPost]
        public IActionResult SmallGroupQrCodeGetLineId(
            string DisplayName,
            string UserLineId,
            string GroupId,
            string RoomId,
            string ViewType)
        {
            try
            {
                SetupLineContext(UserLineId, GroupId, RoomId, ViewType);

                SmallGroupQrCodeUtility qrCodeUtility = new SmallGroupQrCodeUtility();

                string smallGroupName = "";
                string userName = "";
                string onboardType = "";

                qrCodeUtility.SetupQrCodeIdString(
                    InMemoryContext.ListManager.QrCodeId,
                    DisplayName,
                    UserLineId,
                    ref smallGroupName,
                    ref userName,
                    ref onboardType);

                return Json(new
                {
                    result = onboardType,
                    smallgroupname = smallGroupName,
                    username = userName,
                    onboardtype = onboardType
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "SmallGroupQrCodeGetLineId");
            }
        }

        #endregion

        #region 主日 QR CODE

        /// <summary>
        /// 主日 QR Code 簽到退頁面
        /// </summary>
        [Route("/QrCode/SundayView/{QrCodeViewPatameter}")]
        public IActionResult SundayQrCodeView(string QrCodeId, string QrCodeViewPatameter)
        {
            try
            {
                SetupQrCodeViewBag();

                InMemoryContext.ListManager.QrCodeId = QrCodeId;

                TempData["Proponent"] = QrCodeViewPatameter;
                TempData["QrCodeId"] = QrCodeId;
                TempData["ClassName"] = " ";

                return View();
            }
            catch (Exception e)
            {
                return HandleError(e, "SundayQrCodeView");
            }
        }

        /// <summary>
        /// 取得主日 QR Code 掃描資訊
        /// </summary>
        [HttpPost]
        public IActionResult SundayQrCodeGetLineId(
            string DisplayName,
            string UserLineId,
            string GroupId,
            string RoomId,
            string ViewType)
        {
            try
            {
                SetupLineContext(UserLineId, GroupId, RoomId, ViewType);

                SundayQrCodeUtility qrCodeUtility = new SundayQrCodeUtility();

                string sundayName = "";
                string categoryName = "";
                string userName = "";
                string onboardType = "";

                qrCodeUtility.SetupQrCodeIdString(
                    InMemoryContext.ListManager.QrCodeId,
                    DisplayName,
                    UserLineId,
                    ref sundayName,
                    ref categoryName,
                    ref userName,
                    ref onboardType);

                return Json(new
                {
                    result = onboardType,
                    sundayname = sundayName,
                    categoryname = categoryName,
                    username = userName,
                    onboardtype = onboardType
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "SundayQrCodeGetLineId");
            }
        }

        #endregion

        #region 工作人員掃描聯絡人 QR CODE

        /// <summary>
        /// 工作人員主動掃描聯絡人 QR Code 頁面
        /// </summary>
        [Route("/QrCode/PersonalView/{QrCodeViewPatameter}")]
        public IActionResult PersonalQrCodeView(string QrCodeId, string QrCodeViewPatameter)
        {
            try
            {
                SetupQrCodeViewBag();

                InMemoryContext.ListManager.QrCodeId = QrCodeId;

                TempData["Proponent"] = QrCodeViewPatameter;
                TempData["QrCodeId"] = QrCodeId;
                TempData["ClassName"] = " ";

                return View();
            }
            catch (Exception e)
            {
                return HandleError(e, "PersonalQrCodeView");
            }
        }

        /// <summary>
        /// 取得個人 QR Code 掃描資訊
        /// </summary>
        [HttpPost]
        public IActionResult PersonalQrCodeGetLineId(
            string DisplayName,
            string UserLineId,
            string GroupId,
            string RoomId,
            string ViewType)
        {
            try
            {
                SetupLineContext(UserLineId, GroupId, RoomId, ViewType);

                PersonalQrCodeUtility qrCodeUtility = new PersonalQrCodeUtility();

                string sundayName = "";
                string categoryName = "";
                string userName = "";
                string onboardType = "";

                qrCodeUtility.SetupQrCodeIdString(
                    InMemoryContext.ListManager.QrCodeId,
                    DisplayName,
                    UserLineId,
                    ref sundayName,
                    ref categoryName,
                    ref userName,
                    ref onboardType);

                return Json(new
                {
                    result = onboardType,
                    sundayname = sundayName,
                    categoryname = categoryName,
                    username = userName,
                    onboardtype = onboardType
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "PersonalQrCodeGetLineId");
            }
        }

        #endregion

        #region 私有輔助方法

        /// <summary>
        /// 設定 QR Code 頁面的 ViewBag
        /// </summary>
        private void SetupQrCodeViewBag()
        {
            ViewBag.LoginType = "小組長";
            ViewBag.LoginFullName = "耶穌";
            ViewBag.FeeType = "有繳費點名";
            ViewBag.FeeDataListCount = "繳費與點名尚無資料";
            ViewBag.HappyType = "沒幸福小組名單";
            ViewBag.MultiGroupIndex = "SingleMultiGroupView";
            ViewBag.DisplayNavigation = "不顯示牧養回報項目";
            ViewBag.UserType = "行政同工";
        }

        /// <summary>
        /// 設定 LINE 上下文資訊
        /// </summary>
        private void SetupLineContext(string userLineId, string groupId, string roomId, string viewType)
        {
            InMemoryContext.LineBindingViewModel.LineUserId = userLineId;
            InMemoryContext.AppointmentsListManager.LineUserId = userLineId;

            InMemoryContext.LineBindingViewModel.RoomId = roomId;
            InMemoryContext.AppointmentsListManager.RoomId = roomId;

            InMemoryContext.LineBindingViewModel.GroupId = groupId;
            InMemoryContext.AppointmentsListManager.GroupId = groupId;

            InMemoryContext.LineBindingViewModel.ViewType = viewType;
            InMemoryContext.AppointmentsListManager.ViewType = viewType;

            // 設定顯示 ID
            if (!string.IsNullOrEmpty(groupId))
                InMemoryContext.LineBindingViewModel.DisplayId = groupId;
            else if (!string.IsNullOrEmpty(roomId))
                InMemoryContext.LineBindingViewModel.DisplayId = roomId;
            else
                InMemoryContext.LineBindingViewModel.DisplayId = userLineId;

            // 設定行事曆 ViewBag
            ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "單純行事曆";
            ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "不顯示牧養回報項目";
            ViewBag.UserType = InMemoryContext.ListManager.UserType = 
                InMemoryContext.AppointmentsListManager.UserType;
        }

        /// <summary>
        /// 取得使用者全名
        /// </summary>
        private string GetUserFullName(string userLineId)
        {
            var contact = ToolUtility.RetrieveContactEntityByLineUserId(userLineId);
            if (contact != null)
            {
                return ToolUtility.GetEntityStringAttribute(ref contact, "fullname");
            }
            return "";
        }

        #endregion
    }
}
