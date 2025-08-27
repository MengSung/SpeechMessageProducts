using Microsoft.AspNetCore.Mvc;
using ChurchReport.Models;
using ChurchReport.WebServiceConnector;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 高鉅金流 PayPage 回傳處理控制器
    /// </summary>
    [Route("api/[controller]")]
    public class MyPayController : Controller
    {
        private readonly ILogger<MyPayController> _logger;

        public MyPayController(ILogger<MyPayController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 接收高鉅金流 PayPage 交易完成回傳資訊
        /// POST /api/MyPay/return
        /// </summary>
        /// <param name="returnModel">高鉅金流回傳的表單資料</param>
        /// <returns>處理結果</returns>
        [HttpPost("return")]
        public async Task<IActionResult> PaymentReturn([FromForm] MyPayReturnModel returnModel)
        {
            _logger.LogInformation($"收到高鉅金流回傳，OrderID: {returnModel?.order_id}, 狀態: {returnModel?.state}");

            try
            {
                // 基本參數驗證
                if (returnModel == null)
                {
                    _logger.LogWarning("回傳資料為空");
                    return BadRequest("回傳資料為空");
                }

                if (string.IsNullOrEmpty(returnModel.order_id) || 
                    string.IsNullOrEmpty(returnModel.transaction_id) || 
                    string.IsNullOrEmpty(returnModel.hash))
                {
                    _logger.LogWarning($"回傳資料缺少必要欄位: {returnModel.order_id}");
                    return BadRequest("回傳資料缺少必要欄位");
                }

                // 建立 QPayProcessor 實例來處理回傳
                QPayProcessor qpayProcessor = new QPayProcessor(null); // 注意：這裡需要根據實際 DI 設定調整

                // 1. 驗證 hash 值
                if (!qpayProcessor.VerifyMyPayHash(returnModel))
                {
                    _logger.LogWarning($"回傳資訊驗證失敗: {returnModel.order_id}");
                    return BadRequest("驗證失敗");
                }

                // 2. 處理回傳資訊並更新系統
                bool success = await qpayProcessor.ProcessMyPayReturn(returnModel);

                if (success)
                {
                    _logger.LogInformation($"成功處理回傳: {returnModel.order_id}");
                    
                    // 對於 POST 回調，回傳 200 OK 給金流平台
                    if (returnModel.state == "1")
                    {
                        return Ok("SUCCESS");
                    }
                    else
                    {
                        return Ok("FAILED");
                    }
                }
                else
                {
                    _logger.LogWarning($"處理回傳失敗: {returnModel.order_id}");
                    return StatusCode(500, "處理失敗");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"處理回傳異常: {returnModel?.order_id}");
                return StatusCode(500, "處理異常");
            }
        }

        /// <summary>
        /// 付款成功頁面 (供用戶查看結果)
        /// GET /api/MyPay/success
        /// </summary>
        [HttpGet("success")]
        public IActionResult PaymentSuccess([FromQuery] string order_id = "")
        {
            ViewBag.OrderId = order_id;
            ViewBag.Message = "付款成功！感謝您的奉獻。";
            ViewBag.IsSuccess = true;
            return View("PaymentResult");
        }

        /// <summary>
        /// 付款失敗頁面 (供用戶查看結果)
        /// GET /api/MyPay/failure  
        /// </summary>
        [HttpGet("failure")]
        public IActionResult PaymentFailure([FromQuery] string order_id = "", [FromQuery] string msg = "")
        {
            ViewBag.OrderId = order_id;
            ViewBag.Message = !string.IsNullOrEmpty(msg) ? $"付款失敗：{msg}" : "付款失敗，請稍後再試或聯繫教會辦公室。";
            ViewBag.IsSuccess = false;
            return View("PaymentResult");
        }
    }
}