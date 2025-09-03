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
        [HttpPost("MyPayReturn")]
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

                // 驗證必要欄位是否存在
                // order_id: 訂單編號，用於識別特定的交易訂單
                // transaction_id: 金流平台產生的交易識別碼
                // hash: 用於驗證資料完整性的雜湊值
                if (string.IsNullOrEmpty(returnModel.order_id) ||
                    string.IsNullOrEmpty(returnModel.transaction_id) ||
                    string.IsNullOrEmpty(returnModel.hash))
                {
                    // 記錄警告訊息，包含訂單編號以便追蹤問題
                    _logger.LogWarning($"回傳資料缺少必要欄位: {returnModel.order_id}");
                    // 回傳 400 Bad Request 狀態碼給金流平台
                    return BadRequest("回傳資料缺少必要欄位");
                }

                // 建立 QPayProcessor 實例來處理回傳
                QPayProcessor qpayProcessor = new QPayProcessor(null); // 注意：這裡需要根據實際 DI 設定調整

                // 1. 驗證 hash 值
                // hash 是金流平台提供的資料完整性驗證碼，用於確保回傳資料未被篡改
                // 透過比對我們計算的 hash 值與金流平台提供的 hash 值來驗證資料真實性
                //if (!qpayProcessor.VerifyMyPayHash(returnModel))
                //{
                //    // 驗證失敗表示資料可能被篡改或來源不可信，記錄警告以便安全稽核
                //    _logger.LogWarning($"回傳資訊驗證失敗: {returnModel.order_id}");
                //    // 回傳 400 Bad Request 拒絕處理，保護系統安全
                //    return BadRequest("驗證失敗");
                //}

                // 2. 處理回傳資訊並更新系統
                bool success = await qpayProcessor.ProcessMyPayReturn(returnModel);

                if (success)
                {
                    _logger.LogInformation($"成功處理回傳: {returnModel.order_id}");

                    // 根據高鉅金流官方文檔要求，成功處理後回傳 "888"
                    // 這讓金流平台知道我們已經成功接收並處理了回調通知
                    return Ok("888");

                    //// 對於 POST 回調，回傳 200 OK 給金流平台
                    //// 根據交易狀態回傳對應的成功/失敗訊息
                    //if (returnModel.state == "1")
                    //{
                    //    // 交易成功，回傳 SUCCESS 給金流平台以確認收到通知
                    //    return Ok("SUCCESS");
                    //}
                    //else
                    //{
                    //    // 交易失敗，但系統已正確處理回傳資訊，仍回傳 FAILED 確認
                    //    return Ok("FAILED");
                    //}
                }
                else
                {
                    // 系統處理回傳資訊時發生錯誤，記錄警告並回傳 500 錯誤
                    // 讓金流平台知道需要重新發送通知
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