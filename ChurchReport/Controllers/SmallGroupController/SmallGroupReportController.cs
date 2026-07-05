// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/SmallGroupController/SmallGroupReportController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class SmallGroupReportController
// 主要成員：SaveSmallGroup、UpdateDate
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Threading.Tasks、Microsoft.AspNetCore.Mvc、ToolUtilityNameSpace、ChurchReport.Models、Newtonsoft.Json
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860
using ToolUtilityNameSpace;
using ChurchReport.Models;
using Newtonsoft.Json;

namespace ChurchReport.Controllers
{
    public class SmallGroupReportController : Controller
    {
        //[HttpPost]
        //public IActionResult SaveSmallGroup(List<Member> Members)
        //{
        //    //System.Threading.Thread.Sleep(2000);
        //
        //    return Json(new { status = "1", message = "成功上傳了...." });
        //    //return Json(new { status = "2", message = "密碼錯誤...." });
        //}


        [HttpPost]
        public IActionResult SaveSmallGroup(String aResult)
        {
            //string json = @"[
            //    { 'Id':1,'FullName':'吳連碧','Status':'小組長','SmallGroupName':'0201 連碧小組 - 主日出席率:50 % 小組出席率:0 %','SectionName':'0201 連碧小組 - 主日出席率:50 % 小組出席率:0 %','PrayItem':'未填','Sunday':'false','SmallGroup':'true','StateID1':'2','Number1':'4','StateID2':'1','Number2':'2','Picture':'../../ images / employees / 01.png','Shepherd':'null',},
            //    ]";

            ////SmallGroupDataList.m_SmallGroupData.members = JsonConvert.DeserializeObject<List<Member>>(json);
            //m_SmallGroupDataList.m_SmallGroupData.members.Clear();
            //SmallGroupDataList.m_SmallGroupData.members = JsonConvert.DeserializeObject<List<Member>>(aResult);

            //SmallGroupDataList.TransferToMemberInfomationPackage();
            //SmallGroupDataList.UploadMemberInfomationPackage();

            return Json(new { status = "1", message = "成功上傳了...." });
        }


        [HttpGet]
        public IActionResult UpdateDate(string SelectedDate)
        {
            //SmallGroupDataList.SetupSmallGroupData( DateTime.Parse(SelectedDate));
            ////return View(SmallGroupDataList.m_SmallGroupData);
            ////return Json(new { status = "1", message = "成功上傳了...." });
            return Ok();

        }


    }
}
