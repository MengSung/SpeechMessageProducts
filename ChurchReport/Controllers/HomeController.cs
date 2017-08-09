using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ChurchReport.ViewModel;

using ToolUtilityNameSpace;

namespace ChurchReport.Controllers
{
    public class HomeController : Controller
    {
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
            ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

            String FullName = m_ToolUtilityClass.RetrieveContactByAccountNumber(aGalleryViewModel.Account, aGalleryViewModel.Password);

            if ( FullName != "密碼錯誤" && FullName != "系統沒有設定密碼" && FullName != "帳號錯誤")
            {
                return Json(new { status = "1", message = "登入成功!", fullname = FullName });
            }
            else
            {
                return Json(new { status = "2", message = FullName, fullname = FullName });
            }
        }


        [Route("/Home/InputReport/{FullName}")]
        public IActionResult InputReport(String FullName)
        {
            return View((object)FullName);
        }

    }
}
