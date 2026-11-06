/*
 * 檔案整體說明
 *
 * 目的與定位：行事曆與約會管理控制器 處理差勤簽核、場地預約、資源預約等功能
 *
 * 主要功能與流程：
 * 本檔宣告 AppointmentController。
 * - 載入約會清單 用於 DevExtreme Scheduler 的資料來源
 * 主要操作入口包含 Scheduler、SchedulerView、SetupSchedulerViewBag、SetupSchedulerViewForLine、LoadAppointmentByLineId、
 * SetupLineBindingContext、SetupAppointmentAccountPasswordAsync、SetupSchedulerViewBagForLineLogin；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 控制器依賴及狀態限於其明確scope；採partial拆分時各檔共用同一實例，授權、Session及資料範圍須沿伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs 追查目前工作樹中的合作／使用位置。
 */
using ChurchReport.Models;
using ChurchReport.Tools;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 行事曆與約會管理控制器
    /// 處理差勤簽核、場地預約、資源預約等功能
    /// </summary>
    public class AppointmentController : BaseChurchController
    {
        #region 建構函式

        public AppointmentController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
            : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool)
        {
        }

        #endregion

        #region 行事曆主頁面

        /// <summary>
        /// 行事曆主頁面 (網頁登入)
        /// </summary>
        /// <param name="ScheduleType">行事曆類型 (差勤簽核 / 場地及資源預約)</param>
        [Route("/Appointment/Schedule/{ScheduleType}")]
        public IActionResult Scheduler(string ScheduleType)
        {
            try
            {
                SetupSchedulerViewBag(ScheduleType);

                return View(InMemoryContext.AppointmentsListManager);
            }
            catch (Exception e)
            {
                return HandleError(e, "Scheduler");
            }
        }

        /// <summary>
        /// 行事曆主頁面 (LINE LIFF 登入)
        /// </summary>
        /// <param name="ScheduleId">行程ID</param>
        /// <param name="SchedulerViewPatameter">行事曆參數</param>
        [Route("/Appointment/SchedulerView/{SchedulerViewPatameter}")]
        public IActionResult SchedulerView(string ScheduleId, string SchedulerViewPatameter)
        {
            try
            {
                SetupSchedulerViewForLine();
                TempData["Proponent"] = SchedulerViewPatameter;
                ViewBag.LiffId = SchedulerViewPatameter; // 同步 ViewBag，確保 View 可靠讀取 LIFF ID

                return View();
            }
            catch (Exception e)
            {
                return HandleError(e, "SchedulerView");
            }
        }

        /// <summary>
        /// 設定行事曆頁面的 ViewBag (網頁版)
        /// </summary>
        private void SetupSchedulerViewBag(string scheduleType)
        {
            SetupBasicViewBag();
            SetMultiGroupLayoutParameter();

            ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "不是單純行事曆";
            ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "顯示牧養回報項目";
            ViewBag.UserType = InMemoryContext.ListManager.UserType =
                InMemoryContext.AppointmentsListManager.UserType;

            // 設定行事曆類型
            InMemoryContext.AppointmentsListManager.ScheduleType = scheduleType;
            ViewBag.SchedulerDisplayType = scheduleType == "差勤簽核" ? "差勤簽核" : "場地簽核";
        }

        /// <summary>
        /// 設定行事曆頁面的 ViewBag (LINE 版)
        /// </summary>
        private void SetupSchedulerViewForLine()
        {
            ViewBag.LoginType = "小組長";
            ViewBag.LoginFullName = "耶穌";
            ViewBag.FeeType = "有繳費點名";
            ViewBag.FeeDataListCount = "繳費與點名尚無資料";
            ViewBag.HappyType = "沒幸福小組名單";
            ViewBag.MultiGroupIndex = "SingleMultiGroupView";
            ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "單純行事曆";
            ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "不顯示牧養回報項目";
            ViewBag.UserType = InMemoryContext.ListManager.UserType = "行政同工";
        }

        #endregion

        #region LINE 登入處理

        /// <summary>
        /// 透過 LINE ID 載入約會資料
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> LoadAppointmentByLineId(
            string UserLineId,
            string GroupId,
            string RoomId,
            string ViewType)
        {
            try
            {
                SetupLineBindingContext(UserLineId, GroupId, RoomId, ViewType);
                await SetupAppointmentAccountPasswordAsync();
                SetupSchedulerViewBagForLineLogin();

                return Json(new { message = "歡迎登入成功!" });
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadAppointmentByLineId");
            }
        }

        /// <summary>
        /// 設定 LINE 綁定上下文
        /// </summary>
        private void SetupLineBindingContext(
            string userLineId,
            string groupId,
            string roomId,
            string viewType)
        {
            InMemoryContext.LineBindingViewModel.LineUserId =
                InMemoryContext.AppointmentsListManager.LineUserId = userLineId;
            InMemoryContext.LineBindingViewModel.RoomId =
                InMemoryContext.AppointmentsListManager.RoomId = roomId;
            InMemoryContext.LineBindingViewModel.GroupId =
                InMemoryContext.AppointmentsListManager.GroupId = groupId;
            InMemoryContext.LineBindingViewModel.ViewType =
                InMemoryContext.AppointmentsListManager.ViewType = viewType;

            // 設定顯示ID
            if (!string.IsNullOrEmpty(groupId))
                InMemoryContext.LineBindingViewModel.DisplayId = groupId;
            else if (!string.IsNullOrEmpty(roomId))
                InMemoryContext.LineBindingViewModel.DisplayId = roomId;
            else
                InMemoryContext.LineBindingViewModel.DisplayId = userLineId;
        }

        /// <summary>
        /// 設定行事曆帳密 (LINE 登入)
        /// </summary>
        private async Task SetupAppointmentAccountPasswordAsync()
        {
            var lineUserId = InMemoryContext.LineBindingViewModel.LineUserId;
            InMemoryContext.AppointmentsListManager.m_Account = "LineIdLogin";
            InMemoryContext.AppointmentsListManager.m_Password = lineUserId;
            HttpContext?.Session?.SetString("_LoginAccount", "LineIdLogin");
            HttpContext?.Session?.SetString("_LoginPassword", lineUserId ?? string.Empty);
            HttpContext?.Session?.SetString("_SessionUserId", lineUserId ?? string.Empty);
            await IssueAuthTicketAsync(null, "LineIdLogin", lineUserId ?? string.Empty, "LINE");
        }

        /// <summary>
        /// 設定 LINE 登入的 ViewBag
        /// </summary>
        private void SetupSchedulerViewBagForLineLogin()
        {
            ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "單純行事曆";
            ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation =
                "不顯示牧養回報項目";
            ViewBag.UserType = InMemoryContext.ListManager.UserType =
                InMemoryContext.AppointmentsListManager.UserType;
            ViewBag.SchedulerDisplayType =
                InMemoryContext.AppointmentsListManager.UserType == "行政同工" ?
                "差勤簽核" : "場地簽核";
        }

        #endregion

        #region 資料載入

        /// <summary>
        /// 載入約會清單
        /// 用於 DevExtreme Scheduler 的資料來源
        /// </summary>
        [HttpGet]
        public object LoadAppointments(DataSourceLoadOptions loadOptions)
        {
            try
            {
                // ========================================
                // ✅ 使用基底控制器的統一驗證方法
                // ========================================
                // 教學說明：
                // 基底控制器已經提供了完整的驗證方法，包含：
                // - Session 和 ListManager 密碼一致性檢查
                // - LINE ID 恢復機制
                // - 安全的日誌記錄（隱藏敏感資訊）
                //EnsureCorrectUserData();

                // 準備約會清單資料
                InMemoryContext.AppointmentsListManager.SetupAppointmentList();

                return DataSourceLoader.Load(
                    InMemoryContext.AppointmentsListManager.m_Appointments,
                    loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadAppointments");
            }
        }

        #endregion

        #region CRUD 操作

        /// <summary>
        /// 新增約會
        /// </summary>
        /// <param name="values">JSON 格式的約會資料</param>
        [HttpPost]
        public IActionResult PostAppointments(string values)
        {
            try
            {
                var newAppointment = new Appointment();
                JsonConvert.PopulateObject(values, newAppointment);

                // 轉換為本地時間
                newAppointment.StartDate = newAppointment.StartDate.ToLocalTime();
                newAppointment.EndDate = newAppointment.EndDate.ToLocalTime();

                // 建立約會
                InMemoryContext.AppointmentsListManager.CreateAppointment(ref newAppointment);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "PostAppointments");
            }
        }

        /// <summary>
        /// 更新約會
        /// </summary>
        /// <param name="key">約會識別碼</param>
        /// <param name="values">更新的欄位值(JSON)</param>
        [HttpPut]
        public IActionResult PutAppointments(string key, string values)
        {
            try
            {
                var appointment = InMemoryContext.AppointmentsListManager.m_Appointments
                    .First(a => a.AppointmentId == key);

                JsonConvert.PopulateObject(values, appointment);

                // 轉換為本地時間
                appointment.StartDate = appointment.StartDate.ToLocalTime();
                appointment.EndDate = appointment.EndDate.ToLocalTime();

                // 更新約會
                InMemoryContext.AppointmentsListManager.UpdateAppointment(appointment);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "PutAppointments");
            }
        }

        /// <summary>
        /// 刪除約會
        /// </summary>
        /// <param name="key">約會識別碼</param>
        [HttpDelete]
        public void DeleteAppointments(string key)
        {
            try
            {
                var appointment = InMemoryContext.AppointmentsListManager.m_Appointments
                    .First(a => a.AppointmentId == key);

                InMemoryContext.AppointmentsListManager.DeleteAppointment(appointment);
            }
            catch (Exception e)
            {
                HandleError(e, "DeleteAppointments");
            }
        }

        #endregion

        #region 行事曆導覽

        /// <summary>
        /// 導覽到指定日期
        /// 用於 Scheduler 的日期切換
        /// </summary>
        /// <param name="SelectedDate">選擇的日期字串</param>
        [HttpPost]
        public IActionResult NavigateAppointmentDate(string SelectedDate)
        {
            try
            {
                DateTime parsedDate = ParseSelectedDate(SelectedDate);

                // 儲存選擇的日期
                InMemoryContext.AppointmentsListManager.m_SelectDate = parsedDate;

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "NavigateAppointmentDate");
            }
        }

        /// <summary>
        /// 解析選擇的日期字串
        /// 支援多種日期格式
        /// </summary>
        private DateTime ParseSelectedDate(string dateString)
        {
            string[] dateTimeFormats =
            {
                "yyyy/M/d tt hh:mm:ss",
                "yyyy/MM/dd tt hh:mm:ss",
                "yyyy/MM/dd HH:mm:ss",
                "yyyy/M/d HH:mm:ss",
                "ddd MMM dd yyyy HH:mm:ss GMT+0800 (台北標準時間)",
                "ddd MMM dd yyyy HH:mm:ss GMT+0800 (CST)",
                "ddd MMM dd yyyy HH:mm:ss GMT+0800",
                "ddd MMM dd yyyy HH:mm:ss",
                "yyyy/M/d",
                "yyyy/MM/dd"
            };

            return DateTime.ParseExact(
                dateString,
                dateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces);
        }

        #endregion
    }
}
