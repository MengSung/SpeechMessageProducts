using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ChurchReport.ViewModel;

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
            // 模擬上傳資料
            System.Threading.Thread.Sleep(2000);

            if (aGalleryViewModel.Account == "mhu")
            {
                return Json(new { status = "1", message = "登入成功!" });
            }
            else
            {
                return Json(new { status = "2", message = "密碼錯誤!" });
            }
        }

    }
}
