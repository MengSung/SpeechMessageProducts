using ChurchReport.Tools;
using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;

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
        /// 付款完成返回頁面端點 (post_back_url - 前台通知)
        /// 用戶付款完成後的返回頁面，TSPG會將交易結果透過HTTP POST或GET方式傳送至此
        /// 此為前台通知，持卡人網頁會被重新導向至此
        /// </summary>
        /// <returns>返回頁面</returns>
        [HttpGet("post-back")]
        [HttpPost("post-back")]
        public IActionResult PostBack()
        {
            try
            {
                var notification = new TSPGPaymentNotification();

                // === 基本參數 ===
                notification.S_Mid = GetParam("s_mid");
                notification.RetCode = GetParam("ret_code");
                notification.TxType = GetParam("tx_type");
                notification.OrderNo = GetParam("order_no");
                notification.OrderId = GetParam("order_id") ?? GetParam("order_no");
                notification.RetMsg = GetParam("ret_msg");
                notification.AuthIdResp = GetParam("auth_id_resp");
                notification.State = GetParam("state");
                notification.TransactionId = GetParam("transaction_id");

                // === 前台通知特殊參數 (需事先向台新申請) ===
                notification.First6DigitOfPan = GetParam("first_6_digit_of_pan");
                notification.Last4DigitOfPan = GetParam("last_4_digit_of_pan");
                notification.CarrierId2 = GetParam("carrierId2");

                // === DCC 交易專用參數 (僅DCC交易回傳) ===
                notification.ChAmt = GetDecimalParam("ch_amt");
                notification.ChCurrency = GetParam("ch_currency");
                notification.ExRate = GetDecimalParam("ex_rate");
                notification.MarkupRate = GetDecimalParam("markup_rate");

                // === 其他可能參數 ===
                notification.Hash = GetParam("hash") ?? GetParam("signature");
                notification.Cost = GetDecimalParam("cost") ?? GetDecimalParam("amt") ?? 0;
                notification.ActualCost = GetDecimalParam("actual_cost") ?? notification.Cost;
                notification.PayType = GetParam("pay_type");
                notification.Currency = GetParam("currency") ?? GetParam("cur");
                
                // 判斷是否付款成功 (依據 state 或 ret_code)
                var retCode = (notification.RetCode ?? string.Empty).Trim();
                var isSuccess =
                    string.Equals(notification.State, "1") ||
                    string.Equals(retCode, "00", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(retCode, "0000", StringComparison.OrdinalIgnoreCase);

                // 記錄前台通知資訊
                LogPostBackNotification(notification);

                // 根據狀態決定重導向頁面
                if (isSuccess)
                {
                    return HandleSuccessfulPaymentReturn(notification);
                }
                else
                {
                    return HandleSuccessfulPaymentReturn(notification);

                    //return HandleFailedPaymentReturn(notification);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] 付款返回處理例外: {ex.Message}");
                return Redirect("/payment-error");
            }
        }

        /// <summary>
        /// 付款結果通知端點 (後台通知 - result_url)
        /// 接收來自 TSPG 的付款結果通知 (JSON 格式)
        /// 規格參考：4.9 信用卡授權交易回應後台通知
        /// </summary>
        /// <returns>處理結果</returns>
        [HttpPost("payment-notify")]
        [HttpGet("payment-notify")]
        public async Task<IActionResult> PaymentNotify()
        {
            try
            {
                // 讀取 JSON 請求內容
                string requestBody = null;
                using (var reader = new System.IO.StreamReader(Request.Body, System.Text.Encoding.UTF8))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                System.Diagnostics.Trace.WriteLine($"[TSPG PaymentNotify] 收到後台通知: {requestBody}");

                // 解析 JSON 結構
                dynamic jsonData = Newtonsoft.Json.JsonConvert.DeserializeObject(requestBody);

                // 建立通知物件
                var notification = new TSPGPaymentNotification();

                // === 基本欄位 (外層) ===
                notification.StoreUid = jsonData.ver?.ToString();  // 格式版本
                notification.S_Mid = jsonData.mid?.ToString();     // 特店代號
                
                if (jsonData.s_mid != null)
                {
                    notification.S_Mid = jsonData.s_mid.ToString(); // 子特店代號
                }

                string tid = jsonData.tid?.ToString();          // 端末代號
                int? payType = jsonData.pay_type;        // 付款類別 (1:信用卡)
                int? txType = jsonData.tx_type;            // 交易類別
                notification.TxType = txType?.ToString();

                // === params 參數清單 ===
                var paramsData = jsonData.@params;
                
                if (paramsData != null)
                {
                    // 必要參數
                    notification.RetCode = paramsData.ret_code?.ToString();// 交易結果回應碼
                    notification.RetMsg = paramsData.ret_msg?.ToString();           // 回傳訊息
                    notification.OrderNo = paramsData.order_no?.ToString();         // 訂單號碼
                    notification.OrderId = notification.OrderNo;
                    notification.AuthIdResp = paramsData.auth_id_resp?.ToString();  // 授權碼
                    notification.TransactionId = paramsData.rrn?.ToString();        // 調單號碼 (RRN)

                    // 條件必要參數
                    notification.CarrierId2 = paramsData.carrierId2?.ToString();    // 信用卡載具資訊
                    notification.State = paramsData.order_status?.ToString(); // 訂單狀態碼
   
                    string authType = paramsData.auth_type?.ToString();       // 授權方式 (SSL/3D)
                    notification.Currency = paramsData.cur?.ToString();      // 幣別
            
                    string purchaseDate = paramsData.purchase_date?.ToString();     // 採購日期 (yyyy-MM-dd HH:mm:ss)
                    if (!string.IsNullOrEmpty(purchaseDate) && DateTime.TryParse(purchaseDate, out var parsedDate))
                    {
                        notification.PayTime = parsedDate;
                    }

                    // === 金額相關欄位 ===
                    string txAmtStr = paramsData.tx_amt?.ToString();        // 交易金額
                    if (!string.IsNullOrEmpty(txAmtStr) && decimal.TryParse(txAmtStr, out var txAmt))
                    {
                        notification.Cost = txAmt / 100;  // 金額包含兩位小數，需除以100
                        notification.ActualCost = notification.Cost;
                    }

                    string settleAmtStr = paramsData.settle_amt?.ToString();        // 請款金額
                    string settleSeq = paramsData.settle_seq?.ToString();           // 請款批號
                    string settleDate = paramsData.settle_date?.ToString();         // 請款日期

                    // === 退貨相關欄位 ===
                    string refundTransAmtStr = paramsData.refund_trans_amt?.ToString(); // 退貨金額
                    string refundRrn = paramsData.refund_rrn?.ToString();           // 退貨調單編號
                    string refundAuthIdResp = paramsData.refund_auth_id_resp?.ToString(); // 退貨授權碼
                    string refundDate = paramsData.refund_date?.ToString();         // 退貨日期

                    // === 紅利相關欄位 ===
                    string redeemOrderNo = paramsData.redeem_order_no?.ToString();  // 紅利訂單編號
                    string redeemPt = paramsData.redeem_pt?.ToString();      // 折抵點數
                    string redeemAmtStr = paramsData.redeem_amt?.ToString();        // 折抵金額
                    string postRedeemAmtStr = paramsData.post_redeem_amt?.ToString(); // 實付金額
                    string postRedeemPt = paramsData.post_redeem_pt?.ToString();    // 剩餘點數

                    // === 分期相關欄位 ===
                    string installOrderNo = paramsData.install_order_no?.ToString();    // 分期訂單號碼
                    string installPeriod = paramsData.install_period?.ToString(); // 分期期數
                    string installDownPayStr = paramsData.install_down_pay?.ToString(); // 首期金額
                    string installPayStr = paramsData.install_pay?.ToString();          // 每期金額
                    string installDownPayFeeStr = paramsData.install_down_pay_fee?.ToString(); // 首期手續費
                    string installPayFeeStr = paramsData.install_pay_fee?.ToString();   // 每期手續費

                    // === 卡號資訊 ===
                    notification.First6DigitOfPan = paramsData.first_6_digit_of_pan?.ToString(); // 卡號前6碼
                    notification.Last4DigitOfPan = paramsData.last_4_digit_of_pan?.ToString();   // 卡號後4碼

                    // === DCC 交易專用參數 ===
                    string chAmtStr = paramsData.ch_amt?.ToString();     // DCC 交易金額
                    if (!string.IsNullOrEmpty(chAmtStr) && decimal.TryParse(chAmtStr, out var chAmt))
                    {
                        notification.ChAmt = chAmt;
                    }

                    notification.ChCurrency = paramsData.ch_currency?.ToString();   // 持卡人母國幣別
            
                    string exRateStr = paramsData.ex_rate?.ToString();  // 轉換匯率
                    if (!string.IsNullOrEmpty(exRateStr) && decimal.TryParse(exRateStr, out var exRate))
                    {
                        notification.ExRate = exRate;
                    }

                    string markupRateStr = paramsData.markup_rate?.ToString();      // 貼水費率
                    if (!string.IsNullOrEmpty(markupRateStr) && decimal.TryParse(markupRateStr, out var markupRate))
                    {
                        notification.MarkupRate = markupRate;
                    }
                }

                // 判斷交易是否成功
                bool isSuccess = notification.RetCode == "00";

                // 記錄後台通知詳細資訊
                LogBackendNotification(notification, tid, payType, txType, requestBody);

                // 處理付款通知
                if (isSuccess)
                {
                    // 更新收費單狀態
                    UpdateFeeEntityByOrderNo(notification);
                    
                    System.Diagnostics.Trace.WriteLine($"[TSPG PaymentNotify] 付款成功處理完成 - 訂單: {notification.OrderNo}");
                    
                    // 回應成功 (TSPG 期望的回應格式)
                    return Ok(new { status = "success", message = "通知已接收並處理" });
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine($"[TSPG PaymentNotify] 付款失敗 - 訂單: {notification.OrderNo}, 錯誤: {notification.RetMsg}");
                    
                    // 即使失敗也要回應成功，表示通知已收到
                    return Ok(new { status = "received", message = "付款失敗通知已接收" });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG PaymentNotify] 處理例外: {ex.Message}");
                System.Diagnostics.Trace.WriteLine($"[TSPG PaymentNotify] 例外堆疊: {ex.StackTrace}");
        
                // 回應錯誤，但 TSPG 可能會重試
                return StatusCode(500, new { status = "error", message = "處理錯誤: " + ex.Message });
            }
        }

        /// <summary>
        /// 記錄後台通知詳細資訊
        /// </summary>
        private void LogBackendNotification(TSPGPaymentNotification notification, string tid, int? payType, int? txType, string rawJson)
        {
            var logMessage = $"[TSPG Backend Notification] " +
              $"訂單: {notification.OrderNo}, " +
                $"調單號: {notification.TransactionId}, " +
                $"授權碼: {notification.AuthIdResp}, " +
                $"結果碼: {notification.RetCode}, " +
                $"訊息: {notification.RetMsg}, " +
                $"交易類型: {notification.TxType}, " +
                $"端末: {tid}, " +
              $"付款類別: {payType}";

            if (notification.Cost > 0)
            {
                logMessage += $", 金額: {notification.Cost}";
            }

            if (!string.IsNullOrEmpty(notification.First6DigitOfPan) || !string.IsNullOrEmpty(notification.Last4DigitOfPan))
            {
                logMessage += $", 卡號: {notification.First6DigitOfPan}******{notification.Last4DigitOfPan}";
          }

            if (!string.IsNullOrEmpty(notification.CarrierId2))
          {
                logMessage += $", 載具: {notification.CarrierId2}";
            }

            if (notification.ChAmt.HasValue)
            {
                logMessage += $", DCC金額: {notification.ChAmt} {notification.ChCurrency}, 匯率: {notification.ExRate}, 貼水: {notification.MarkupRate}%";
            }

            System.Diagnostics.Trace.WriteLine(logMessage);
            System.Diagnostics.Trace.WriteLine($"[TSPG Backend Notification] 原始JSON: {rawJson}");
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

        #region 輔助方法

        /// <summary>
        /// 從Request中取得參數值 (支援GET和POST)
        /// </summary>
        private string GetParam(string key)
        {
            // 先嘗試從Form取得 (POST)
            if (Request.Method == "POST" && Request.HasFormContentType && Request.Form.ContainsKey(key))
            {
                return Request.Form[key].ToString();
            }
            
            // 再嘗試從Query取得 (GET)
            if (Request.Query.ContainsKey(key))
            {
                return Request.Query[key].ToString();
            }
            
            return null;
        }

        /// <summary>
        /// 從Request中取得decimal參數值
        /// </summary>
        private decimal? GetDecimalParam(string key)
        {
            var value = GetParam(key);
            if (!string.IsNullOrWhiteSpace(value) && decimal.TryParse(value, out var result))
            {
                return result;
            }
            return null;
        }

        /// <summary>
        /// 記錄前台通知資訊
        /// </summary>
        private void LogPostBackNotification(TSPGPaymentNotification notification)
        {
            var logMessage = $"[TSPG PostBackUrl] " +
                $"訂單: {notification.OrderNo ?? notification.OrderId}, " +
                $"交易號: {notification.TransactionId}, " +
                $"狀態: {notification.State}, " +
                $"結果碼: {notification.RetCode}, " +
                $"交易類型: {notification.TxType}";

            if (!string.IsNullOrEmpty(notification.First6DigitOfPan) || !string.IsNullOrEmpty(notification.Last4DigitOfPan))
            {
                logMessage += $", 卡號: {notification.First6DigitOfPan}******{notification.Last4DigitOfPan}";
            }

            if (!string.IsNullOrEmpty(notification.CarrierId2))
            {
                logMessage += $", 載具: {notification.CarrierId2}";
            }

            if (notification.ChAmt.HasValue)
            {
                logMessage += $", DCC金額: {notification.ChAmt} {notification.ChCurrency}, 匯率: {notification.ExRate}";
            }

            System.Diagnostics.Trace.WriteLine(logMessage);
        }

        /// <summary>
        /// 依據OrderNo更新收費單狀態
        /// </summary>
        private void UpdateFeeEntityByOrderNo(TSPGPaymentNotification notification)
        {
   ToolUtilityClass toolUtility = null;
            try
            {
        var orderNo = notification.OrderNo ?? notification.OrderId;
        if (string.IsNullOrEmpty(orderNo))
        {
     System.Diagnostics.Trace.WriteLine("[TSPG] 訂單編號為空，無法更新收費單");
           return;
      }

           // 使用 ToolUtilityClass 查詢收費單
         toolUtility = new ToolUtilityClass("DYNAMICS365");
       
          // 查詢 new_q_pay_card_order_no 等於 OrderNo 的收費單
       Entity updatedFeeEntity = toolUtility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", orderNo);

     if (updatedFeeEntity == null)
 {
          System.Diagnostics.Trace.WriteLine($"[TSPG] 找不到對應的收費單 - OrderNo: {orderNo}");
         return;
     }

                // 更新付款狀態為已付款 (100000001 = 信用卡已繳費)
  toolUtility.SetOptionSetAttribute(ref updatedFeeEntity, "new_pay_status", 100000001);

            // 更新實收金額 (new_fee_really_paid)
    //var amount = notification.Cost > 0 ? notification.Cost : notification.ActualCost;
           var amount = toolUtility.GetEntityMoneyAttribute(updatedFeeEntity, "new_fee_shoud_pay");

     //待更正: 這邊應該是要設定為 amount 而不是應收金額
    toolUtility.SetEntityMoneyAttribute(ref updatedFeeEntity, "new_fee_really_paid", toolUtility.GetEntityMoneyAttribute(updatedFeeEntity, "new_fee_shoud_pay"));

           // 計算差額 (應收金額 - 實收金額)
     var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(updatedFeeEntity, "new_fee_shoud_pay");
        var differenceFee = shouldPayMoney.Value - amount.Value;
     toolUtility.SetEntityMoneyAttribute(ref updatedFeeEntity, "new_difference_fee_paid", new Money(differenceFee));

     // 設定付款日期
          toolUtility.SetEntityDateTimeAttribute(ref updatedFeeEntity, "new_pay_date", DateTime.Now);

    // 設定付款方式為信用卡
        toolUtility.SetOptionSetAttribute(ref updatedFeeEntity, "new_pay_way", 100000001); // 100000001 = 信用卡

        // 更新收費單說明
                var originalDescription = toolUtility.GetEntityStringAttribute(updatedFeeEntity, "new_description");
          var newDescription = originalDescription + Environment.NewLine +
   $"[TSPG付款成功] 訂單號:{orderNo}, 交易號:{notification.TransactionId}, " +
 $"金額:{amount}, 授權碼:{notification.AuthIdResp}, 時間:{DateTime.Now}";
                toolUtility.SetEntityStringAttribute(ref updatedFeeEntity, "new_description", newDescription);

        // 儲存更新
   toolUtility.UpdateEntity(ref updatedFeeEntity);

                System.Diagnostics.Trace.WriteLine($"[TSPG] 成功更新收費單 - OrderNo: {orderNo}, FeeId: {updatedFeeEntity.Id}");

     // 取得連絡人並發送 LINE 訊息
     SendPaymentNotificationToContact(toolUtility, updatedFeeEntity, notification, amount.Value);
   }
      catch (Exception ex)
{
       System.Diagnostics.Trace.WriteLine($"[TSPG] 更新收費單失敗 - 錯誤: {ex.Message}");
         }
            finally
   {
      // 手動釋放資源
    toolUtility?.Dispose();
            }
        }

   /// <summary>
        /// 發送付款通知訊息給連絡人
        /// </summary>
        private void SendPaymentNotificationToContact(ToolUtilityClass toolUtility, Entity feeEntity, TSPGPaymentNotification notification, decimal amount)
        {
            try
            {
                // 從收費單取得連絡人 Lookup (new_contact_new_fee)
                var contactId = toolUtility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                
                if (contactId == Guid.Empty)
                {
                    System.Diagnostics.Trace.WriteLine("[TSPG] 收費單沒有關聯的連絡人");
                    return;
                }

                // 取得連絡人實體
                Entity contactEntity = toolUtility.RetrieveEntity("contact", contactId);
                
                if (contactEntity == null)
                {
                    System.Diagnostics.Trace.WriteLine($"[TSPG] 找不到連絡人 - ContactId: {contactId}");
                    return;
                }

                // 取得連絡人的 LINE ID
                string lineId = toolUtility.GetEntityStringAttribute(contactEntity, "new_lineid");
                
                if (string.IsNullOrEmpty(lineId))
                {
                    System.Diagnostics.Trace.WriteLine($"[TSPG] 連絡人沒有 LINE ID - ContactId: {contactId}");
                    return;
                }

                // 取得連絡人姓名
                string fullName = toolUtility.GetEntityStringAttribute(contactEntity, "fullname");

                // 組成 LINE 訊息內容
                var orderNo = notification.OrderNo ?? notification.OrderId;
                var messageContent = BuildPaymentSuccessMessage(fullName, orderNo, amount, notification);

                // 發送 LINE 訊息
                SendLineMessage(lineId, messageContent);

                System.Diagnostics.Trace.WriteLine($"[TSPG] 已發送付款通知 LINE 訊息 - ContactId: {contactId}, LineId: {lineId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] 發送 LINE 訊息失敗 - 錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 建立付款成功訊息內容
        /// </summary>
        private string BuildPaymentSuccessMessage(string fullName, string orderNo, decimal amount, TSPGPaymentNotification notification)
        {
            var message = $"【TSPG 付款成功通知】{Environment.NewLine}{Environment.NewLine}";
            message += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            message += $"您的奉獻已成功完成，感謝您的支持！{Environment.NewLine}{Environment.NewLine}";
            message += $"付款資訊：{Environment.NewLine}";
            //message += $"??????????????{Environment.NewLine}";
            message += $"訂單編號：{orderNo}{Environment.NewLine}";
            message += $"付款金額：NT$ {amount:N0}{Environment.NewLine}";
            message += $"付款時間：{DateTime.Now:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
            message += $"付款方式：信用卡{Environment.NewLine}";
            
            if (!string.IsNullOrEmpty(notification.AuthIdResp))
            {
                message += $"授權碼：{notification.AuthIdResp}{Environment.NewLine}";
            }
            
            if (!string.IsNullOrEmpty(notification.TransactionId))
            {
                message += $"交易編號：{notification.TransactionId}{Environment.NewLine}";
            }
            
            //message += $"??????????????{Environment.NewLine}{Environment.NewLine}";
            message += $"願上帝賜福與您！";

            return message;
        }

        /// <summary>
        /// 發送 LINE 訊息
        /// </summary>
        private void SendLineMessage(string lineId, string message)
        {
            try
            {
                // LINE Channel Access Token (從設定檔讀取或使用預設值)
                const string CHANNEL_ACCESS_TOKEN = @"OMjL23DpFRDgphgN7JdzA7uCpv1wb4hXtsGh4FzxP8tHzeMyYOr/ry3BBqaRNJpVUhR6wPHLN4Wa4QiG5i3P5T/Y07swP5OjfCz9DKwTYC7T4mPb8x54pwtcqK1lIdgNm6skdZnu99fBsupEcbZLBAdB04t89/1O/w1cDnyilFU=";

                // 建立 LINE Messaging Client
                var lineMessagingClient = new Line.Messaging.LineMessagingClient(CHANNEL_ACCESS_TOKEN);
                var pushUtility = new PushUtility(lineMessagingClient);

                // 發送訊息 (同步方式)
                pushUtility.SendMessage(lineId, message).Wait();

                System.Diagnostics.Trace.WriteLine($"[TSPG] LINE 訊息已發送 - LineId: {lineId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG] LINE 訊息發送失敗 - 錯誤: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 處理付款成功的返回
        /// </summary>
        private IActionResult HandleSuccessfulPaymentReturn(TSPGPaymentNotification notification)
        {
            System.Diagnostics.Trace.WriteLine($"[TSPG] 付款成功 - 訂單: {notification.OrderNo}, 授權碼: {notification.AuthIdResp}");
            
            // 更新收費單狀態
            UpdateFeeEntityByOrderNo(notification);

            var orderNo = notification.OrderNo ?? notification.OrderId;
            // 使用 ToolUtilityClass 查詢收費單
            ToolUtilityClass toolUtility = new ToolUtilityClass("DYNAMICS365");

            // 查詢 new_q_pay_card_order_no 等於 OrderNo 的收費單
            Entity updatedFeeEntity = toolUtility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", orderNo);

            // 構建成功頁面URL參數
            var orderId = notification.OrderNo ?? notification.OrderId;
            var txnId = notification.TransactionId ?? "";

            //待修正 
            //var amount = notification.Cost.ToString();
            var amount = Convert.ToInt32(toolUtility.GetEntityMoneyAttribute(updatedFeeEntity, "new_fee_shoud_pay").Value).ToString();

            var authCode = notification.AuthIdResp ?? "";
            var txType = notification.TxType ?? "";

            var queryString = $"order_id={Uri.EscapeDataString(orderId)}&transaction_id={Uri.EscapeDataString(txnId)}&amount={amount}&auth_code={Uri.EscapeDataString(authCode)}&tx_type={Uri.EscapeDataString(txType)}";

            // 如果有DCC資訊,也傳遞過去
            if (notification.ChAmt.HasValue)
            {
                queryString += $"&dcc_amount={notification.ChAmt.Value}&dcc_currency={Uri.EscapeDataString(notification.ChCurrency ?? "")}&exchange_rate={notification.ExRate ?? 0}";
            }

            return Redirect($"/payment-success?{queryString}");
        }

        /// <summary>
        /// 處理付款失敗的返回
        /// </summary>
        private IActionResult HandleFailedPaymentReturn(TSPGPaymentNotification notification)
        {
            System.Diagnostics.Trace.WriteLine($"[TSPG] 付款失敗 - 訂單: {notification.OrderNo}, 錯誤: {notification.RetMsg}");
            
            var errorMsg = notification.RetMsg ?? "付款失敗";
            var orderId = notification.OrderNo ?? notification.OrderId ?? "UNKNOWN";
            var retCode = notification.RetCode ?? "";
            
            return Redirect($"/payment-failed?order_id={Uri.EscapeDataString(orderId)}&error={Uri.EscapeDataString(errorMsg)}&ret_code={Uri.EscapeDataString(retCode)}");
        }

        #endregion
    }
}