/*
 * 檔案整體說明
 *
 * 目的與定位：提供排程畫面入口並將顯示交給Scheduler Razor，資料操作由對應SchedulerDataController及約會模型處理；此控制器負責畫面路由，不是約會持久化的第二套實作。
 *
 * 主要功能與流程：
 * 本檔宣告 SchedulerController。
 * 主要操作入口包含 Scheduler。
 *
 * 使用邊界與維護：
 * 控制器依賴及狀態限於其明確scope；採partial拆分時各檔共用同一實例，授權、Session及資料範圍須沿伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

using ChurchReport.Models;

namespace ChurchReport.Controllers
{
    public class SchedulerController : Controller
    {
        // GET: /<controller>/
        public ActionResult Scheduler()
        {
            AppointmentsListManager aAppointmentsList = new AppointmentsListManager();
            return View(aAppointmentsList);
        }
    }
}
