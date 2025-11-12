using ChurchReport.Models;
using ChurchReport.Tools;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 奉獻管理控制器
    /// 處理永豐金流(QPay)奉獻相關功能
    /// </summary>
    public class DedicationController : BaseChurchController
    {
        #region 建構函式

        public DedicationController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService)
            : base(httpContextAccessor, memoryCache, paymentService)
        {
        }

        #endregion

        #region 奉獻主頁面 (Line 或網頁登入)

        /// <summary>
        /// 永豐金流奉獻主頁面
        /// 支援 LINE 單獨登入或網頁登入
        /// </summary>
        /// <param name="LineId">LINE 使用者 ID (若從 LINE 進入)</param>
        [Route("/Dedication/QPayView/{LineId}")]
        public IActionResult QPayView(string LineId)
        {
            try
            {
                SetupQPayViewBag();

                // 處理 LINE 登入
                if (!string.IsNullOrEmpty(LineId) && LineId != "網頁登入")
                {
                    SetupUserLineId(LineId, "", "", "");
                }

                return View(InMemoryContext.QpayManager.m_QpayModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "QPayView");
            }
        }

        /// <summary>
        /// 設定奉獻頁面的 ViewBag
        /// </summary>
        private void SetupQPayViewBag()
        {
            if (InMemoryContext.QpayManager.LoginType == "網頁登入")
            {
                // 網頁登入 - 使用完整選單
                SetupBasicViewBag();
                SetMultiGroupLayoutParameter();
            }
            else
            {
                // LINE 登入 - 簡化選單
                ViewBag.LoginType = "小組長";
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                ViewBag.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = "行政同工";
                ViewBag.DedicationType = "奉獻管理";
                ViewBag.DedicationFlag = "奉獻";
                ViewBag.IsAOfficeWorker = InMemoryContext.QpayManager.m_QpayModel.IsAOfficeWorker ? "是的" : "否";
            }
        }

        #endregion

        #region 奉獻交易處理

        /// <summary>
        /// 儲存奉獻交易
        /// 建立奉獻記錄並導向金流頁面
        /// </summary>
        /// <param name="QpayModel">奉獻資料模型</param>
        [HttpPost]
        public async Task<IActionResult> SaveQPayDedication(QpayModel QpayModel)
        {
            try
            {
                return await InMemoryContext.QpayManager.SaveQPayDedication(QpayModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveQPayDedication");
            }
        }

        #endregion

        #region 信用卡管理

        /// <summary>
        /// 載入使用者的信用卡清單
        /// </summary>
        /// <param name="id">使用者ID</param>
        /// <param name="loadOptions">載入選項</param>
        [HttpGet]
        public object LoadCreditCardList(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                var tasks = InMemoryContext.QpayManager.m_QpayModel.CreditCardList;
                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadCreditCardList");
            }
        }

        /// <summary>
        /// 刪除信用卡
        /// </summary>
        /// <param name="key">信用卡 Token</param>
        [HttpDelete]
        public void DeleteCreditCard(string key)
        {
            try
            {
                var creditCard = InMemoryContext.QpayManager.m_QpayModel.CreditCardList
                    .First(a => a.CCToken == key);

                InMemoryContext.QpayManager.DeleteCreditCard(creditCard);
            }
            catch (Exception e)
            {
                HandleError(e, "DeleteCreditCard");
            }
        }

        #endregion

        #region 認獻管理

        /// <summary>
        /// 載入認獻清單
        /// </summary>
        /// <param name="id">使用者ID</param>
        /// <param name="loadOptions">載入選項</param>
        [HttpGet]
        public object LoadDedicationBookingList(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                var tasks = InMemoryContext.QpayManager.m_QpayModel.DedicationBookingList;
                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadDedicationBookingList");
            }
        }

        /// <summary>
        /// 取消認獻
        /// </summary>
        /// <param name="key">認獻實體ID</param>
        [HttpDelete]
        public void DeleteDedicationBooking(string key)
        {
            try
            {
                var dedicationBooking = InMemoryContext.QpayManager.m_QpayModel.DedicationBookingList
                    .First(a => a.EntityId == key);

                InMemoryContext.QpayManager.DeleteDedicationBooking(dedicationBooking);
            }
            catch (Exception e)
            {
                HandleError(e, "DeleteDedicationBooking");
            }
        }

        #endregion

        #region 奉獻收費清單

        /// <summary>
        /// 奉獻收費清單頁面 (LINE 登入)
        /// </summary>
        [Route("/Dedication/DedicationFeeView")]
        public IActionResult DedicationFeeView()
        {
            try
            {
                SetupDedicationFeeViewBag(false);

                return View(InMemoryContext.QpayManager.SetDedicationFeeList(
                    InMemoryContext.LineBindingViewModel.LineUserId));
            }
            catch (Exception e)
            {
                return HandleError(e, "DedicationFeeView");
            }
        }

        /// <summary>
        /// 奉獻收費清單頁面 (網頁登入)
        /// </summary>
        [Route("/Dedication/DedicationFeeViewWeb")]
        public IActionResult DedicationFeeViewWeb()
        {
            try
            {
                SetupDedicationFeeViewBag(true);

                return View(InMemoryContext.QpayManager.SetDedicationFeeList(
                    InMemoryContext.QpayManager.m_Contact));
            }
            catch (Exception e)
            {
                return HandleError(e, "DedicationFeeViewWeb");
            }
        }

        /// <summary>
        /// 設定奉獻收費清單的 ViewBag
        /// </summary>
        /// <param name="isWebLogin">是否為網頁登入</param>
        private void SetupDedicationFeeViewBag(bool isWebLogin)
        {
            if (isWebLogin)
            {
                SetupBasicViewBag();
                SetMultiGroupLayoutParameter();
            }
            else
            {
                ViewBag.LoginType = "小組長";
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                ViewBag.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = "行政同工";
                ViewBag.DedicationType = "奉獻管理";
                ViewBag.IsAOfficeWorker = InMemoryContext.QpayManager.m_QpayModel.IsAOfficeWorker ? "是的" : "否";
            }
        }

        /// <summary>
        /// 更新奉獻收費清單查詢日期
        /// </summary>
        /// <param name="aQpayModel">查詢條件</param>
        [HttpPost]
        public async Task<IActionResult> UpdateDedicationFeeView(QpayModel aQpayModel)
        {
            try
            {
                InMemoryContext.QpayManager.m_QpayModel.QueryStartDate = aQpayModel.QueryStartDate;
                InMemoryContext.QpayManager.m_QpayModel.QueryEndDate = aQpayModel.QueryEndDate;

                return Json(new { status = "1", message = "成功更新查詢日期!" });
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateDedicationFeeView");
            }
        }

        #endregion

        #region 行政人員奉獻管理

        /// <summary>
        /// 行政人員手動輸入奉獻頁面 (LINE)
        /// </summary>
        [Route("/Dedication/KeyInDedicationFeeView")]
        public IActionResult KeyInDedicationFeeView()
        {
            try
            {
                SetupKeyInViewBag(false);

                return View(InMemoryContext.QpayManager.SetDedicationFeeList(
                    InMemoryContext.LineBindingViewModel.LineUserId));
            }
            catch (Exception e)
            {
                return HandleError(e, "KeyInDedicationFeeView");
            }
        }

        /// <summary>
        /// 行政人員手動輸入奉獻頁面 (網頁)
        /// </summary>
        [Route("/Dedication/KeyInDedicationFeeViewWeb")]
        public IActionResult KeyInDedicationFeeViewWeb()
        {
            try
            {
                SetupKeyInViewBag(true);

                return View(InMemoryContext.QpayManager.m_QpayModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "KeyInDedicationFeeViewWeb");
            }
        }

        /// <summary>
        /// 設定手動輸入頁面的 ViewBag
        /// </summary>
        private void SetupKeyInViewBag(bool isWebLogin)
        {
            if (isWebLogin)
            {
                SetupBasicViewBag();
                SetMultiGroupLayoutParameter();
            }
            else
            {
                ViewBag.LoginType = "小組長";
                ViewBag.LoginFullName = "耶穌";
                ViewBag.FeeType = "有繳費點名";
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
                ViewBag.HappyType = "沒幸福小組名單";
                ViewBag.MultiGroupIndex = "SingleMultiGroupView";
                ViewBag.DisplayNavigation = "不顯示牧養回報項目";
                ViewBag.UserType = "行政同工";
                ViewBag.DedicationType = "奉獻管理";
                ViewBag.IsAOfficeWorker = InMemoryContext.QpayManager.m_QpayModel.IsAOfficeWorker ? "是的" : "否";
            }
        }

        /// <summary>
        /// 儲存手動輸入的奉獻記錄
        /// </summary>
        /// <param name="QpayModel">奉獻資料</param>
        [HttpPost]
        public async Task<IActionResult> SaveKeyInDedication(QpayModel QpayModel)
        {
            try
            {
                return await InMemoryContext.QpayManager.SaveKeyInDedication(
                    QpayModel,
                    InMemoryContext.AppointmentsListManager.m_LoginContact);
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveKeyInDedication");
            }
        }

        #endregion

        #region 同名同姓處理

        /// <summary>
        /// 載入同名同姓清單
        /// </summary>
        [HttpGet]
        public object LoadSameNameList(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                var tasks = InMemoryContext.QpayManager.m_QpayModel.SameNameList;
                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadSameNameList");
            }
        }

        /// <summary>
        /// 刪除同名同姓聯絡人
        /// </summary>
        /// <param name="key">聯絡人識別碼</param>
        [HttpDelete]
        public void DeleteSameNameContact(string key)
        {
            try
            {
                var sameNameContact = InMemoryContext.QpayManager.m_QpayModel.SameNameList
                    .First(a => a.SameNameElementId == key);

                InMemoryContext.QpayManager.DeleteSameNameContact(sameNameContact);
            }
            catch (Exception e)
            {
                HandleError(e, "DeleteSameNameContact");
            }
        }

        /// <summary>
        /// 建立新聯絡人
        /// </summary>
        /// <param name="FullName">姓名</param>
        [HttpPost]
        public async Task<IActionResult> CreateContact(string FullName)
        {
            try
            {
                return await InMemoryContext.QpayManager.CreateContact(FullName);
            }
            catch (Exception e)
            {
                return HandleError(e, "CreateContact");
            }
        }

        #endregion

        #region LINE 登入設定

        /// <summary>
        /// 設定 LINE 使用者 ID
        /// 用於 LINE LIFF 奉獻頁面
        /// </summary>
        [HttpPost]
        public IActionResult SetupUserLineId(string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                // 設定 LINE 相關資訊
                InMemoryContext.LineBindingViewModel.LineUserId = UserLineId;
                InMemoryContext.LineBindingViewModel.RoomId = RoomId;
                InMemoryContext.LineBindingViewModel.GroupId = GroupId;
                InMemoryContext.LineBindingViewModel.ViewType = ViewType;

                // 設定顯示 ID
                if (!string.IsNullOrEmpty(GroupId))
                    InMemoryContext.LineBindingViewModel.DisplayId = GroupId;
                else if (!string.IsNullOrEmpty(RoomId))
                    InMemoryContext.LineBindingViewModel.DisplayId = RoomId;
                else
                    InMemoryContext.LineBindingViewModel.DisplayId = UserLineId;

                // 設定奉獻管理器
                InMemoryContext.QpayManager.LoginType = "Line單獨登入";

                // 載入登入使用者資料
                var loginContact = ToolUtility.RetrieveContactByLineId(UserLineId);
                if (loginContact != null)
                {
                    InMemoryContext.QpayManager.SetQpayModel(loginContact);
                }

                return Json(new { status = "1" });
            }
            catch (Exception e)
            {
                return HandleError(e, "SetupUserLineId");
            }
        }

        /// <summary>
        /// 奉獻 LINE 登入頁面
        /// </summary>
        [Route("/Dedication/DediationLineLoginView/{LineIdLoginViewPatameter}")]
        public IActionResult DediationLineLoginView(string LineIdLoginViewPatameter)
        {
            try
            {
                var images = new System.Collections.Generic.List<string>
                {
                    Url.Content("~/assets/images/sunnyvalech.jpg")
                };

                InMemoryContext.LineBindingViewModel.Images = images;
                TempData["Proponent"] = LineIdLoginViewPatameter;

                return View(InMemoryContext.LineBindingViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "DediationLineLoginView");
            }
        }

        #endregion

        #region 付款結果頁面

        /// <summary>
        /// 付款錯誤頁面
        /// </summary>
        [HttpGet]
        public IActionResult PaymentError(
            string title,
            string message,
            string code,
            string details,
            string timestamp)
        {
            ViewBag.ErrorTitle = title;
            ViewBag.ErrorMessage = message;
            ViewBag.ErrorCode = code;
            ViewBag.ErrorDetails = details;
            ViewBag.Timestamp = timestamp;

            return View();
        }

        #endregion
    }
}
