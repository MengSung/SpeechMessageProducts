using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860
using ToolUtilityNameSpace;
using ChurchReport.Models;

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
            //Thread.Sleep(5000);

            return Json(new { status = "1", message = "成功上傳了...." });
        }


        [HttpGet]
        public IActionResult UpdateDate(string SelectedDate)
        {
            SmallGroupDataList.SetupSmallGroupDate(SmallGroupDataList.m_SmallGroupData.SmallGroupLeaderContactId, SelectedDate);
            //return View(SmallGroupDataList.m_SmallGroupData);
            //return Json(new { status = "1", message = "成功上傳了...." });
            return Ok();

        }


    }
}
