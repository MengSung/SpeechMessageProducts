using ChurchReport.Tools;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// TSPG (高鉅金流) API 控制器
    /// 處理來自高鉺金流的 Webhook 通知和其他 API 操作
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class TSPGController : ControllerBase
    {
        private readonly TSPGWebhookHandler _webhookHandler;

        public TSPGController(TSPGWebhookHandler webhookHandler)
        {
            _webhookHandler = webhookHandler;
        }

        #region Webhook 端點

        /// <summary>
        /// 付款結果通知端點
        /// 接收來自 TSPG 的付款結果通知
        /// </summary>
        /// <returns>處理結果</returns>
        [HttpPost("payment-notify")]
        [HttpGet("payment-notify")]
        public async Task<IActionResult> PaymentNotify()
        {
            try
            {
                var result = await _webhookHandler.HandlePaymentNotificationAsync(Request);
                
                if (result.IsSuccess)
                {
                    // 記錄成功處理
                    System.Diagnostics.Trace.WriteLine($"[TSPG] 付款通知處理成功 - 訂單: {result.Notification?.OrderId}");
                    return Ok(result.ResponseContent);
                }
                else
                {
                    // 記錄處理失敗
                    System.Diagnostics.Trace.WriteLine($"[TSPG] 付款通知處理失敗 - 錯誤: {result.ErrorMessage}");
                    return BadRequest(result.ResponseContent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] 付款通知處理例外: {ex.Message}");
                return StatusCode(500, "ERROR");
            }
        }

        /// <summary>
        /// 退款結果通知端點
        /// 接收來自 TSPG 的退款結果通知
        /// </summary>
        /// <returns>處理結果</returns>
        [HttpPost("refund-notify")]
        [HttpGet("refund-notify")]
        public async Task<IActionResult> RefundNotify()
        {
            try
            {
                var result = await _webhookHandler.HandleRefundNotificationAsync(Request);
                
                if (result.IsSuccess)
                {
                    // 記錄成功處理
                    System.Diagnostics.Trace.WriteLine($"[TSPG] 退款通知處理成功 - 訂單: {result.Notification?.OrderId}");
                    return Ok(result.ResponseContent);
                }
                else
                {
                    // 記錄處理失敗
                    System.Diagnostics.Trace.WriteLine($"[TSPG] 退款通知處理失敗 - 錯誤: {result.ErrorMessage}");
                    return BadRequest(result.ResponseContent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] 退款通知處理例外: {ex.Message}");
                return StatusCode(500, "ERROR");
            }
        }

        /// <summary>
        /// 付款完成返回頁面端點
        /// 用戶付款完成後的返回頁面
        /// </summary>
        /// <returns>返回頁面</returns>
        [HttpGet("payment-return")]
        [HttpPost("payment-return")]
        public IActionResult PaymentReturn()
        {
            try
            {
                // 讀取返回參數
                var orderId = Request.Query["order_id"].ToString();
                var state = Request.Query["state"].ToString();
                var transactionId = Request.Query["transaction_id"].ToString();

                // 記錄返回資訊
                System.Diagnostics.Trace.WriteLine($"[TSPG] 付款返回 - 訂單: {orderId}, 狀態: {state}, 交易號: {transactionId}");

                // 根據狀態決定重導向頁面
                if (state == "1") // 付款成功
                {
                    return Redirect($"/payment-success?order_id={orderId}&transaction_id={transactionId}");
                }
                else // 付款失敗或取消
                {
                    var errorMsg = Request.Query["retmsg"].ToString();
                    return Redirect($"/payment-failed?order_id={orderId}&error={Uri.EscapeDataString(errorMsg)}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] 付款返回處理例外: {ex.Message}");
                return Redirect("/payment-error");
            }
        }

        #endregion

        #region API 操作端點 (使用 TspgToolkit 靜態方法)

        /// <summary>
        /// 建立付款訂單
        /// </summary>
        /// <param name="request">付款請求</param>
        /// <returns>付款回應</returns>
        [HttpPost("create-payment")]
        public IActionResult CreatePayment([FromBody] TSPGPaymentRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var response = TspgToolkit.OrderCreate(request);
                
                if (response.code == "0000")
                {
                    return Ok(new
                    {
                        success = true,
                        order_id = response.uid,
                        payment_url = response.url,
                        message = response.msg
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        success = false,
                        error_code = response.code,
                        message = response.msg
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] 建立付款失敗: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "系統錯誤，請稍後再試"
                });
            }
        }

        /// <summary>
        /// 查詢訂單狀態
        /// </summary>
        /// <param name="orderId">訂單編號</param>
        /// <returns>查詢結果</returns>
        [HttpGet("query-order/{orderId}")]
        public IActionResult QueryOrder(string orderId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(orderId))
                {
                    return BadRequest(new { success = false, message = "訂單編號不能為空" });
                }

                var response = TspgToolkit.OrderQuery(orderId);
                
                return Ok(new
                {
                    success = response.code == "0000",
                    order_id = response.uid,
                    status_code = response.code,
                    message = response.msg,
                    data = response
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] 查詢訂單失敗: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "查詢失敗，請稍後再試"
                });
            }
        }

        /// <summary>
        /// 取消訂單
        /// </summary>
        /// <param name="orderId">訂單編號</param>
        /// <returns>取消結果</returns>
        [HttpPost("cancel-order/{orderId}")]
        public IActionResult CancelOrder(string orderId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(orderId))
                {
                    return BadRequest(new { success = false, message = "訂單編號不能為空" });
                }

                var response = TspgToolkit.CancelOrder(orderId);
                
                return Ok(new
                {
                    success = response.code == "0000",
                    order_id = response.uid,
                    message = response.msg
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] 取消訂單失敗: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "取消失敗，請稍後再試"
                });
            }
        }

        /// <summary>
        /// 申請退款
        /// </summary>
        /// <param name="request">退款請求</param>
        /// <returns>退款結果</returns>
        [HttpPost("refund")]
        public IActionResult Refund([FromBody] TSPGRefundRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var response = TspgToolkit.RefundOrder(request);
                
                return Ok(new
                {
                    success = response.code == "0000",
                    order_id = response.uid,
                    message = response.msg
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] 申請退款失敗: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "退款申請失敗，請稍後再試"
                });
            }
        }

        /// <summary>
        /// 信用卡請款
        /// </summary>
        /// <param name="orderId">訂單編號</param>
        /// <param name="amount">請款金額 (可選)</param>
        /// <returns>請款結果</returns>
        [HttpPost("capture/{orderId}")]
        public IActionResult Capture(string orderId, [FromQuery] decimal? amount = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(orderId))
                {
                    return BadRequest(new { success = false, message = "訂單編號不能為空" });
                }

                var response = TspgToolkit.CaptureOrder(orderId, amount);
                
                return Ok(new
                {
                    success = response.code == "0000",
                    order_id = response.uid,
                    message = response.msg
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] 請款失敗: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "請款失敗，請稍後再試"
                });
            }
        }

        /// <summary>
        /// 取得交易記錄
        /// </summary>
        /// <param name="startDate">開始日期 (YYYY-MM-DD)</param>
        /// <param name="endDate">結束日期 (YYYY-MM-DD)</param>
        /// <returns>交易記錄</returns>
        [HttpGet("transaction-history")]
        public IActionResult GetTransactionHistory([FromQuery] string startDate, [FromQuery] string endDate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
                {
                    return BadRequest(new { success = false, message = "開始日期和結束日期不能為空" });
                }

                // 驗證日期格式
                if (!DateTime.TryParse(startDate, out _) || !DateTime.TryParse(endDate, out _))
                {
                    return BadRequest(new { success = false, message = "日期格式不正確，請使用 YYYY-MM-DD 格式" });
                }

                var response = TspgToolkit.GetTransactionHistory(startDate, endDate);
                
                return Ok(new
                {
                    success = response.Code == "0000",
                    message = response.Message,
                    start_date = startDate,
                    end_date = endDate,
                    total_count = response.TotalCount,
                    transactions = response.Transactions
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] 取得交易記錄失敗: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "取得交易記錄失敗，請稍後再試"
                });
            }
        }

        #endregion

        #region 測試 / 健康檢查

        /// <summary>
        /// API 健康狀態檢查
        /// </summary>
        /// <returns>健康狀態</returns>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.Now,
                version = "1.0.0",
                service = "TSPG API Controller"
            });
        }

        /// <summary>
        /// 測試 Webhook 端點
        /// 用於測試 Webhook 處理邏輯
        /// </summary>
        /// <returns>測試結果</returns>
        [HttpPost("test-webhook")]
        public IActionResult TestWebhook()
        {
            var testNotification = new TSPGPaymentNotification
            {
                StoreUid = "test_store",
                OrderId = $"TEST_{DateTime.Now:yyyyMMddHHmmss}",
                TransactionId = $"TXN_{DateTime.Now:yyyyMMddHHmmss}",
                State = "1",
                Cost = 100,
                ActualCost = 100,
                Currency = "TWD",
                PayType = "credit",
                UserName = "測試用戶",
                UserEmail = "test@example.com",
                PayTime = DateTime.Now,
                ReturnMessage = "付款成功",
                Hash = "test_hash"
            };
            return Ok(new { success = true, message = "測試 Webhook 資料已建立", test_data = testNotification });
        }

        #endregion
    }
}