using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using QPay.Domain;
using System;
using System.Threading.Tasks;
using ToolUtilityNameSpace;
using ChurchReport.Tools;

namespace ChurchReport.Controllers
{
    [Route("api/[controller]")]
    public class QPayCardController : Controller
    {
        [HttpPost]
        [HttpGet]
        [Route("QPayReturnUrl")]
        public ActionResult QPayReturnUrl(string ShopNo, string PayToken)
        {
            try
            {
                // 記錄請求資訊
                System.Diagnostics.Trace.WriteLine($"[QPayCardController] QPayReturnUrl called at {DateTime.Now}");
                System.Diagnostics.Trace.WriteLine($"  - HTTP Method: {Request.Method}");
                System.Diagnostics.Trace.WriteLine($"  - ShopNo: {ShopNo ?? "(null)"}");
                System.Diagnostics.Trace.WriteLine($"  - PayToken: {PayToken ?? "(null)"}");
                
                // 記錄所有查詢字串參數
                if (Request.QueryString.HasValue)
                {
                    System.Diagnostics.Trace.WriteLine($"  - QueryString: {Request.QueryString.Value}");
                }
                
                // 記錄所有 Form 參數
                if (Request.HasFormContentType && Request.Form != null)
                {
                    foreach (var key in Request.Form.Keys)
                    {
                        System.Diagnostics.Trace.WriteLine($"  - Form[{key}]: {Request.Form[key]}");
                    }
                }
                
                // 參數驗證
                if (string.IsNullOrWhiteSpace(ShopNo) || string.IsNullOrWhiteSpace(PayToken))
                {
                    string errorMsg = $"參數不完整: ShopNo={ShopNo ?? "(null)"}, PayToken={PayToken ?? "(null)"}";
                    System.Diagnostics.Trace.WriteLine($"[QPayCardController] Error: {errorMsg}");
                    
                    // 返回美觀的付款失敗頁面
                    ViewBag.IsSuccess = false;
                    ViewBag.Message = "缺少必要的付款資訊，請重新嘗試或聯繫教會辦公室。";
                    ViewBag.ErrorDetails = errorMsg;
                    ViewBag.OrderId = "";
                    
                    return View("PaymentResult");
                }
                
                using (QPayCardWebhook aQPayCardWebhook = new QPayCardWebhook())
                {
                    return aQPayCardWebhook.QPayReturnUrl(ShopNo, PayToken);
                }
            }
            catch (Exception ex)
            {
                // 記錄詳細錯誤資訊
                string errorDetail = $"ERROR: QPayCardController.QPayReturnUrl{Environment.NewLine}" +
                                   $"Time: {DateTime.Now}{Environment.NewLine}" +
                                   $"ShopNo: {ShopNo ?? "(null)"}{Environment.NewLine}" +
                                   $"PayToken: {PayToken ?? "(null)"}{Environment.NewLine}" +
                                   $"Message: {ex.Message}{Environment.NewLine}" +
                                   $"StackTrace: {ex.StackTrace}{Environment.NewLine}" +
                                   $"InnerException: {ex.InnerException?.Message ?? "(none)"}";
                
                System.Diagnostics.Trace.WriteLine(errorDetail);
                Console.WriteLine(errorDetail);
                
                // 返回美觀的付款失敗頁面
                ViewBag.IsSuccess = false;
                ViewBag.Message = "處理付款結果時發生系統錯誤，請稍後再試或聯繫教會辦公室。";
                ViewBag.ErrorDetails = $"{ex.Message}\n\n{ex.StackTrace}";
                ViewBag.OrderId = "";
                
                return View("PaymentResult");
            }
        }
    }
}
