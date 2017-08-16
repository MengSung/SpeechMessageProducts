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

            //SmallGroupDataList.m_SmallGroupData.members = JsonConvert.DeserializeObject<List<Member>>(json);
            SmallGroupDataList.m_SmallGroupData.members.Clear();
            SmallGroupDataList.m_SmallGroupData.members = JsonConvert.DeserializeObject<List<Member>>(aResult);

            SmallGroupDataList.TransferToMemberInfomationPackage();
            SmallGroupDataList.UploadMemberInfomationPackage();

            return Json(new { status = "1", message = "成功上傳了...." });
        }


        [HttpGet]
        public IActionResult UpdateDate(string SelectedDate)
        {
            SmallGroupDataList.SetupSmallGroupData( DateTime.Parse(SelectedDate));
            //return View(SmallGroupDataList.m_SmallGroupData);
            //return Json(new { status = "1", message = "成功上傳了...." });
            return Ok();

        }


    }
}
