using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ChurchReport.ViewModel;

using ToolUtilityNameSpace;
using ChurchReport.Models;


// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;

namespace ChurchReport.Controllers
{
    public class HomeController : Controller
    {
        ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        public IActionResult Login()
        {
            var images = new List<string>();
            images.Add(Url.Content("~/assets/images/photo-1.jpg"));
            images.Add(Url.Content("~/assets/images/photo-10.jpg"));
            images.Add(Url.Content("~/assets/images/photo-6.jpg"));
            images.Add(Url.Content("~/assets/images/photo-9.jpg"));
            return View(new GalleryViewModel
            {
                Images = images
            });
        }

        [HttpPost]
        public IActionResult ProcessLogin(GalleryViewModel aGalleryViewModel)
        {
            //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

            String ContactIdString = m_ToolUtilityClass.RetrieveContactByAccountNumber(aGalleryViewModel.Account, aGalleryViewModel.Password);

            if (ContactIdString != "密碼錯誤" && ContactIdString != "系統沒有設定密碼" && ContactIdString != "帳號錯誤")
            {
                Guid aContactGuid = new Guid(ContactIdString);

                String FullName = this.m_ToolUtilityClass.RetrieveEntity( "contact", aContactGuid).Attributes["fullname"].ToString();

                return Json(new { status = "1", message = "歡迎" + FullName + "登入成功!", fullname = ContactIdString });
            }
            else
            {
                return Json(new { status = "2", message = ContactIdString, fullname = ContactIdString });
            }
        }



        [Route("/Home/SmallGroupReportView/{ContactIdString}")]
        public ActionResult SmallGroupReportView(String ContactIdString)
        {

            SmallGroupDataList.SetupSmallGroupDate( ContactIdString );

            return View(SmallGroupDataList.m_SmallGroupData);
        }

        [Route("/Home/InputReport/{FullName}")]
        public IActionResult InputReport(String FullName)
        {
            return View((object)FullName);
        }

    }
}
