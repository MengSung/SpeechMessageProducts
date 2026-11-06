/*
 * 檔案整體說明
 *
 * 目的與定位：保留小組週報儲存及日期更新的舊HTTP入口，將提交候選交既有小組回報模型；日期與小組ID共同決定資料範圍，不能將前次畫面狀態直接當成目前提交依據。
 *
 * 主要功能與流程：
 * 本檔宣告 SmallGroupReportController。
 * 主要操作入口包含 SaveSmallGroup、UpdateDate。
 *
 * 使用邊界與維護：
 * 控制器依賴及狀態限於其明確scope；採partial拆分時各檔共用同一實例，授權、Session及資料範圍須沿伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 * 可沿 SpeechMessageProducts.ChurchReport/Views/FeeManagement/LessonList.cshtml、
 * SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml、
 * SpeechMessageProducts.ChurchReport/Views/Home/MultiGroupView.cshtml、
 * SpeechMessageProducts.ChurchReport/Views/Home/NewPersonFollowUpView.cshtml、
 * SpeechMessageProducts.ChurchReport/Views/Home/PresentFeeListView.cshtml 追查目前工作樹中的合作／使用位置。
 */
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
