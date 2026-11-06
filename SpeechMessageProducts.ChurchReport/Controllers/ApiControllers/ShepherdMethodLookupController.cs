/*
 * 檔案整體說明
 *
 * 目的與定位：將牧養方式選單資料交給前端lookup元件，維持既有查詢與選項型別形狀；端點提供顯示候選，不授予會友或名單的修改權限。
 *
 * 主要功能與流程：
 * 本檔宣告 ShepherdMethodLookupController。
 * 主要操作入口包含 Get、GetType。
 *
 * 使用邊界與維護：
 * 控制器依賴及狀態限於其明確scope；採partial拆分時各檔共用同一實例，授權、Session及資料範圍須沿伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using ChurchReport.Models;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace ChurchReport.Controllers
{
    public class ShepherdMethodLookupController : Controller
    {

        [HttpGet]
        public object Get(DataSourceLoadOptions loadOptions)
        {
            return DataSourceLoader.Load(ShepherdMethodData.ShepherdMethodList, loadOptions);
        }
        [HttpGet]
        public ActionResult GetType(DataSourceLoadOptions loadOptions)
        {
            return Content(JsonConvert.SerializeObject(DataSourceLoader.Load(ShepherdMethodData.ShepherdMethodList, loadOptions)), "application/json");
        }
    }
}
