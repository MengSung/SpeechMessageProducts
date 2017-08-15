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
        //   //Thread.Sleep(5000);
        //
        //   string json = @"{
        //     'Name': 'Bad Boys',
        //     'ReleaseDate': '1995-4-7T00:00:00',
        //     'Genres': [
        //       'Action',
        //       'Comedy'
        //     ]
        //   }";
        //
        //   Movie m = JsonConvert.DeserializeObject<Movie>(json);
        //
        //   string name = m.Name;
        //   // Bad Boys
        //   JsonConvert.DeserializeObject()
        //
        //
        //
        //           //JSON字串
        //   string Json = "{ 'Table1': [ { 'id': 1, 'item': '這是第1個項目' }, { 'id': 2, 'item': '這是第2個項目' }, { 'id': 3, 'item': '這是第3個項目' }, { 'id': 4, 'item': '這是第4個項目' } ] }";
        //
        //   //將JSON字串轉為DataSet
        //   DataSet dataSet = JsonConvert.DeserializeObject<DataSet>(Json);
        //
        //
        //
        //
        //
        //
        //   Member aMember = new Member
        //   {
        //       Id = 1,
        //       FullName = aMemberInfomation.Name,
        //       Status = aMemberInfomation.Identity,
        //       SmallGroupName = aMemberInfomation.Group,
        //       SectionName = aMemberInfomation.Group,
        //       PrayItem = aMemberInfomation.Note,
        //       Sunday = aMemberInfomation.SundayPresent,
        //       SmallGroup = aMemberInfomation.SmallGroupPresent,
        //       StateID1 = 2,
        //       Number1 = 4,
        //       StateID2 = 1,
        //       Number2 = 2,
        //       //Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
        //       Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
        //                                                 //Picture = "https://tpehoc.speechmessage.com.tw/image/download.aspx?attribute=entityimage&entity=contact&id=66cd8034-953f-e711-80d9-00155d00640b" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
        //
        //   };
        //


            string json = @"[
                      {
                        'Id': 1,
                        'FullName': '黃仁宏',
                        'Status': '組員',
                        'SmallGroupName': '夢嵩小組',
                        'SectionName': '國仁哥族系',
                        'PrayItem': '要陪讀',
                        'Sunday': true,
                        'SmallGroup': false,
                        'Picture': '../../images/employees/01.png'
                      },
                    ]
            ";

            SmallGroupDataList.m_SmallGroupData.members = JsonConvert.DeserializeObject<List<Member>>(json);

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
