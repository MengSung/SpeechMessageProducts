using ChurchReport.Tools;
using Line.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using System;
using System.Threading.Tasks;
using ToolUtilityNameSpace;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// TSPG (台新金流) API 控制器
    /// 處理來自台新金流的 Webhook 通知和其他 API 操作
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class TSPGController : ControllerBase
    {
        #region 常數定義

        private const string LINE_CHANNEL_ACCESS_TOKEN = @"OMjL23DpFRDgphgN7JdzA7uCpv1wb4hXtsGh4FzxP8tHzeMyYOr/ry3BBqaRNJpVUhR6wPHLN4Wa4QiG5i3P5T/Y07swP5OjfCz9DKwTYC7T4mPb8x54pwtcqK1lIdgNm6skdZnu99fBsupEcbZLBAdB04t89/1O/w1cDnyilFU=";
        private const string DYNAMICS_CONNECTION_NAME = "DYNAMICS365";
        private const int PAYMENT_STATUS_PAID = 100000001;  // 信用卡已繳費
        private const int PAYMENT_METHOD_CREDIT_CARD = 100000001;  // 信用卡

        #endregion

        #region 私有欄位

        private readonly TSPGWebhookHandler _webhookHandler;

        #endregion

        #region 建構函式

        public TSPGController(TSPGWebhookHandler webhookHandler)
        {
            _webhookHandler = webhookHandler;
        }

        #endregion

        #region Webhook 端點

        /// <summary>
        /// 付款完成返回頁面端點 (post_back_url - 前台通知)
        /// 用戶付款完成後的返回頁面，TSPG會將交易結果透過HTTP POST或GET方式傳送至此
        /// 此為前台通知，持卡人網頁會被重新導向至此
        /// </summary>
        [HttpGet("post-back")]
        [HttpPost("post-back")]
        public IActionResult PostBack()
        {
            try
            {
                var notification = ParsePostBackNotification();
                LogPostBackNotification(notification);

                bool isSuccess = IsPaymentSuccess(notification.RetCode, notification.State);

                return isSuccess
                    ? HandleSuccessfulPaymentReturn(notification)
                    : HandleSuccessfulPaymentReturn(notification); // TODO: 應改為 HandleFailedPaymentReturn
            }
            catch (Exception ex)
            {
                LogError("PostBack", "付款返回處理例外", ex);
                return Redirect("/payment-error");
            }
        }

        /// <summary>
        /// 付款結果通知端點 (後台通知 - result_url)
        /// 接收來自 TSPG 的付款結果通知 (JSON 格式)
        /// 規格參考：4.9 信用卡授權交易回應後台通知
        /// </summary>
        [HttpPost("payment-notify")]
        [HttpGet("payment-notify")]
        public async Task<IActionResult> PaymentNotify()
        {
            string requestBody = null;

            try
            {
                requestBody = await ReadRequestBodyAsync();
                LogInfo("PaymentNotify", $"收到後台通知: {requestBody}");

                var notification = ParseBackendNotification(requestBody);
                bool isSuccess = notification.RetCode == "00";

                if (isSuccess)
                {
                    UpdateFeeEntityByOrderNo(notification);
                    LogInfo("PaymentNotify", $"付款成功處理完成 - 訂單: {notification.OrderNo}");
                    return Ok(new { status = "success", message = "通知已接收並處理" });
                }
                else
                {
                    LogInfo("PaymentNotify", $"付款失敗 - 訂單: {notification.OrderNo}, 錯誤: {notification.RetMsg}");
                    return Ok(new { status = "received", message = "付款失敗通知已接收" });
                }
            }
            catch (Exception ex)
            {
                LogError("PaymentNotify", "處理例外", ex);
                return StatusCode(500, new { status = "error", message = $"處理錯誤: {ex.Message}" });
            }
        }

        #endregion

        #region API 操作端點

        /// <summary>
        /// 建立付款訂單
        /// </summary>
        [HttpPost("create-payment")]
        public IActionResult CreatePayment([FromBody] TSPGPaymentRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var response = TspgToolkit.OrderCreate(request);
                return CreateApiResponse(response);
            }
            catch (Exception ex)
            {
                return HandleApiError("建立付款", ex);
            }
        }

        /// <summary>
        /// 查詢訂單狀態
        /// </summary>
        [HttpGet("query-order/{orderId}")]
        public IActionResult QueryOrder(string orderId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(orderId))
                    return BadRequest(new { success = false, message = "訂單編號不能為空" });

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
                return HandleApiError("查詢訂單", ex);
            }
        }

        /// <summary>
        /// 取消訂單
        /// </summary>
        [HttpPost("cancel-order/{orderId}")]
        public IActionResult CancelOrder(string orderId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(orderId))
                    return BadRequest(new { success = false, message = "訂單編號不能為空" });

                var response = TspgToolkit.CancelOrder(orderId);
                return CreateSimpleApiResponse(response);
            }
            catch (Exception ex)
            {
                return HandleApiError("取消訂單", ex);
            }
        }

        /// <summary>
        /// 申請退款
        /// </summary>
        [HttpPost("refund")]
        public IActionResult Refund([FromBody] TSPGRefundRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var response = TspgToolkit.RefundOrder(request);
                return CreateSimpleApiResponse(response);
            }
            catch (Exception ex)
            {
                return HandleApiError("申請退款", ex);
            }
        }

        /// <summary>
        /// 信用卡請款
        /// </summary>
        [HttpPost("capture/{orderId}")]
        public IActionResult Capture(string orderId, [FromQuery] decimal? amount = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(orderId))
                    return BadRequest(new { success = false, message = "訂單編號不能為空" });

                var response = TspgToolkit.CaptureOrder(orderId, amount);
                return CreateSimpleApiResponse(response);
            }
            catch (Exception ex)
            {
                return HandleApiError("請款", ex);
            }
        }

        /// <summary>
        /// 取得交易記錄
        /// </summary>
        [HttpGet("transaction-history")]
        public IActionResult GetTransactionHistory([FromQuery] string startDate, [FromQuery] string endDate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
                    return BadRequest(new { success = false, message = "開始日期和結束日期不能為空" });

                if (!DateTime.TryParse(startDate, out _) || !DateTime.TryParse(endDate, out _))
                    return BadRequest(new { success = false, message = "日期格式不正確，請使用 YYYY-MM-DD 格式" });

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
                return HandleApiError("取得交易記錄", ex);
            }
        }

        #endregion

        #region 測試與健康檢查

        /// <summary>
        /// API 健康狀態檢查
        /// </summary>
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
        /// </summary>
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

        #region 通知解析方法

        /// <summary>
        /// 解析前台通知參數
        /// </summary>
        private TSPGPaymentNotification ParsePostBackNotification()
        {
            return new TSPGPaymentNotification
            {
                // 基本參數
                S_Mid = GetParam("s_mid"),
                RetCode = GetParam("ret_code"),
                TxType = GetParam("tx_type"),
                OrderNo = GetParam("order_no"),
                OrderId = GetParam("order_id") ?? GetParam("order_no"),
                RetMsg = GetParam("ret_msg"),
                AuthIdResp = GetParam("auth_id_resp"),
                State = GetParam("state"),
                TransactionId = GetParam("transaction_id"),

                // 特殊參數（需事先向台新申請）
                First6DigitOfPan = GetParam("first_6_digit_of_pan"),
                Last4DigitOfPan = GetParam("last_4_digit_of_pan"),
                CarrierId2 = GetParam("carrierId2"),

                // DCC 交易參數
                ChAmt = GetDecimalParam("ch_amt"),
                ChCurrency = GetParam("ch_currency"),
                ExRate = GetDecimalParam("ex_rate"),
                MarkupRate = GetDecimalParam("markup_rate"),

                // 其他參數
                Hash = GetParam("hash") ?? GetParam("signature"),
                Cost = GetDecimalParam("cost") ?? GetDecimalParam("amt") ?? 0,
                ActualCost = GetDecimalParam("actual_cost") ?? (GetDecimalParam("cost") ?? GetDecimalParam("amt") ?? 0),
                PayType = GetParam("pay_type"),
                Currency = GetParam("currency") ?? GetParam("cur")
            };
        }

        /// <summary>
        /// 解析後台通知（JSON 格式）
        /// </summary>
        private TSPGPaymentNotification ParseBackendNotification(string requestBody)
        {
            dynamic jsonData = Newtonsoft.Json.JsonConvert.DeserializeObject(requestBody);
            var notification = new TSPGPaymentNotification();

            // 外層基本欄位
            notification.StoreUid = jsonData.ver?.ToString();
            notification.S_Mid = jsonData.s_mid?.ToString() ?? jsonData.mid?.ToString();
            notification.TxType = jsonData.tx_type?.ToString();

            string tid = jsonData.tid?.ToString();
            int? payType = jsonData.pay_type;
            int? txType = jsonData.tx_type;

            // params 參數清單
            var paramsData = jsonData.@params;
            if (paramsData != null)
            {
                ParseBackendParamsData(notification, paramsData);
            }

            LogBackendNotification(notification, tid, payType, txType, requestBody);
            return notification;
        }

        /// <summary>
        /// 解析後台通知的 params 資料
        /// </summary>
        private void ParseBackendParamsData(TSPGPaymentNotification notification, dynamic paramsData)
        {
            // 必要參數
            notification.RetCode = paramsData.ret_code?.ToString();
            notification.RetMsg = paramsData.ret_msg?.ToString();
            notification.OrderNo = paramsData.order_no?.ToString();
            notification.OrderId = notification.OrderNo;
            notification.AuthIdResp = paramsData.auth_id_resp?.ToString();
            notification.TransactionId = paramsData.rrn?.ToString();

            // 條件參數
            notification.CarrierId2 = paramsData.carrierId2?.ToString();
            notification.State = paramsData.order_status?.ToString();
            notification.Currency = paramsData.cur?.ToString();

            // 日期處理
            string purchaseDate = paramsData.purchase_date?.ToString();
            if (!string.IsNullOrEmpty(purchaseDate) && DateTime.TryParse(purchaseDate, out var parsedDate))
            {
                notification.PayTime = parsedDate;
            }

            // 金額處理
            string txAmtStr = paramsData.tx_amt?.ToString();
            if (!string.IsNullOrEmpty(txAmtStr) && decimal.TryParse(txAmtStr, out var txAmt))
            {
                notification.Cost = txAmt / 100;  // 金額包含兩位小數
                notification.ActualCost = notification.Cost;
            }

            // 卡號資訊
            notification.First6DigitOfPan = paramsData.first_6_digit_of_pan?.ToString();
            notification.Last4DigitOfPan = paramsData.last_4_digit_of_pan?.ToString();

            // DCC 交易參數
            ParseDccParameters(notification, paramsData);
        }

        /// <summary>
        /// 解析 DCC 交易參數
        /// </summary>
        private void ParseDccParameters(TSPGPaymentNotification notification, dynamic paramsData)
        {
            string chAmtStr = paramsData.ch_amt?.ToString();
            if (!string.IsNullOrEmpty(chAmtStr) && decimal.TryParse(chAmtStr, out var chAmt))
            {
                notification.ChAmt = chAmt;
            }

            notification.ChCurrency = paramsData.ch_currency?.ToString();

            string exRateStr = paramsData.ex_rate?.ToString();
            if (!string.IsNullOrEmpty(exRateStr) && decimal.TryParse(exRateStr, out var exRate))
            {
                notification.ExRate = exRate;
            }

            string markupRateStr = paramsData.markup_rate?.ToString();
            if (!string.IsNullOrEmpty(markupRateStr) && decimal.TryParse(markupRateStr, out var markupRate))
            {
                notification.MarkupRate = markupRate;
            }
        }

        /// <summary>
        /// 讀取請求內容
        /// </summary>
        private async Task<string> ReadRequestBodyAsync()
        {
            using (var reader = new System.IO.StreamReader(Request.Body, System.Text.Encoding.UTF8))
            {
                return await reader.ReadToEndAsync();
            }
        }

        #endregion

        #region 參數取得方法

        /// <summary>
        /// 從 Request 中取得參數值（支援 GET 和 POST）
        /// </summary>
        private string GetParam(string key)
        {
            if (Request.Method == "POST" && Request.HasFormContentType && Request.Form.ContainsKey(key))
            {
                return Request.Form[key].ToString();
            }

            if (Request.Query.ContainsKey(key))
            {
                return Request.Query[key].ToString();
            }

            return null;
        }

        /// <summary>
        /// 從 Request 中取得 decimal 參數值
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

        #endregion

        #region 業務邏輯處理

        /// <summary>
        /// 判斷付款是否成功
        /// </summary>
        private bool IsPaymentSuccess(string retCode, string state)
        {
            retCode = (retCode ?? string.Empty).Trim();
            return string.Equals(state, "1") ||
                string.Equals(retCode, "00", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(retCode, "0000", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 更新收費單狀態
        /// </summary>
        private void UpdateFeeEntityByOrderNo(TSPGPaymentNotification notification)
        {
            ToolUtilityClass toolUtility = null;

            try
            {
                var orderNo = notification.OrderNo ?? notification.OrderId;
                if (string.IsNullOrEmpty(orderNo))
                {
                    LogWarning("UpdateFeeEntity", "訂單編號為空，無法更新收費單");
                    return;
                }

                toolUtility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
                Entity feeEntity = toolUtility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", orderNo);

                if (feeEntity == null)
                {
                    LogWarning("UpdateFeeEntity", $"找不到對應的收費單 - OrderNo: {orderNo}");
                    return;
                }

                UpdateFeeEntityFields(toolUtility, feeEntity, notification);
                toolUtility.UpdateEntity(ref feeEntity);

                LogInfo("UpdateFeeEntity", $"成功更新收費單 - OrderNo: {orderNo}, FeeId: {feeEntity.Id}");

                // 發送 LINE 通知
                var amount = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                SendPaymentNotificationToContact(toolUtility, feeEntity, notification, amount.Value);
            }
            catch (Exception ex)
            {
                LogError("UpdateFeeEntity", "更新收費單失敗", ex);
            }
            finally
            {
                toolUtility?.Dispose();
            }
        }

        /// <summary>
        /// 更新收費單欄位
        /// </summary>
        private void UpdateFeeEntityFields(ToolUtilityClass toolUtility, Entity feeEntity, TSPGPaymentNotification notification)
        {
            var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
            var orderNo = notification.OrderNo ?? notification.OrderId;

            // 更新付款狀態
            toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PAYMENT_STATUS_PAID);

            // 更新實收金額（TODO: 應該使用實際金額而非應收金額）
            toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);

            // 計算差額
            toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));

            // 設定付款日期和方式
            toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", DateTime.Now);
            toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PAYMENT_METHOD_CREDIT_CARD);

            // 更新說明
            var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description");
            var newDescription = $"{originalDescription}{Environment.NewLine}" +
                $"[TSPG付款成功] 訂單號:{orderNo}, 交易號:{notification.TransactionId}, " +
                $"金額:{shouldPayMoney}, 授權碼:{notification.AuthIdResp}, 時間:{DateTime.Now}";
            toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);
        }

        /// <summary>
        /// 發送付款通知給連絡人
        /// </summary>
        private void SendPaymentNotificationToContact(ToolUtilityClass toolUtility, Entity feeEntity,
            TSPGPaymentNotification notification, decimal amount)
        {
            try
            {
                var contactId = toolUtility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                if (contactId == Guid.Empty)
                {
                    LogWarning("SendNotification", "收費單沒有關聯的連絡人");
                    return;
                }

                Entity contactEntity = toolUtility.RetrieveEntity("contact", contactId);
                if (contactEntity == null)
                {
                    LogWarning("SendNotification", $"找不到連絡人 - ContactId: {contactId}");
                    return;
                }

                string lineId = toolUtility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrEmpty(lineId))
                {
                    LogWarning("SendNotification", $"連絡人沒有 LINE ID - ContactId: {contactId}");
                    return;
                }

                string fullName = toolUtility.GetEntityStringAttribute(contactEntity, "fullname");
                var orderNo = notification.OrderNo ?? notification.OrderId;
                var message = BuildPaymentSuccessMessage(fullName, orderNo, amount, notification);

                SendLineMessage(lineId, message);
                LogInfo("SendNotification", $"已發送付款通知 LINE 訊息 - ContactId: {contactId}, LineId: {lineId}");
            }
            catch (Exception ex)
            {
                LogError("SendNotification", "發送 LINE 訊息失敗", ex);
            }
        }

        /// <summary>
        /// 建立付款成功訊息
        /// </summary>
        private string BuildPaymentSuccessMessage(string fullName, string orderNo, decimal amount,
            TSPGPaymentNotification notification)
        {
            var message = $"【TSPG 付款成功通知】{Environment.NewLine}{Environment.NewLine}";
            message += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            message += $"您的奉獻已成功完成，感謝您的支持！{Environment.NewLine}{Environment.NewLine}";
            message += $"付款資訊：{Environment.NewLine}";
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

            message += $"{Environment.NewLine}願上帝賜福與您！";
            return message;
        }

        /// <summary>
        /// 發送 LINE 訊息
        /// </summary>
        private void SendLineMessage(string lineId, string message)
        {
            try
            {
                var lineMessagingClient = new LineMessagingClient(LINE_CHANNEL_ACCESS_TOKEN);
                var pushUtility = new PushUtility(lineMessagingClient);
                pushUtility.SendMessage(lineId, message).Wait();
                LogInfo("SendLineMessage", $"LINE 訊息已發送 - LineId: {lineId}");
            }
            catch (Exception ex)
            {
                LogError("SendLineMessage", "LINE 訊息發送失敗", ex);
                throw;
            }
        }

        #endregion

        #region 返回處理方法

        /// <summary>
        /// 處理付款成功的返回
        /// </summary>
        private IActionResult HandleSuccessfulPaymentReturn(TSPGPaymentNotification notification)
        {
            ToolUtilityClass toolUtility = null;
            try
            {
                LogInfo("PaymentReturn", $"付款成功 - 訂單: {notification.OrderNo}, 授權碼: {notification.AuthIdResp}");

                UpdateFeeEntityByOrderNo(notification);

                var orderNo = notification.OrderNo ?? notification.OrderId;
                toolUtility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
                Entity feeEntity = toolUtility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", orderNo);

                var queryString = BuildSuccessQueryString(notification, toolUtility, feeEntity);
                return Redirect($"/payment-success?{queryString}");
            }
            catch (Exception ex)
            {
                LogError("PaymentReturn", "處理付款成功返回失敗", ex);
                return Redirect("/payment-error");
            }
            finally
            {
                toolUtility?.Dispose();
            }
        }

        /// <summary>
        /// 處理付款失敗的返回
        /// </summary>
        private IActionResult HandleFailedPaymentReturn(TSPGPaymentNotification notification)
        {
            LogInfo("PaymentReturn", $"付款失敗 - 訂單: {notification.OrderNo}, 錯誤: {notification.RetMsg}");

            var errorMsg = notification.RetMsg ?? "付款失敗";
            var orderId = notification.OrderNo ?? notification.OrderId ?? "UNKNOWN";
            var retCode = notification.RetCode ?? "";

            return Redirect($"/payment-failed?order_id={Uri.EscapeDataString(orderId)}" +
                $"&error={Uri.EscapeDataString(errorMsg)}" +
                $"&ret_code={Uri.EscapeDataString(retCode)}");
        }

        /// <summary>
        /// 建立成功頁面查詢字串
        /// </summary>
        private string BuildSuccessQueryString(TSPGPaymentNotification notification,
            ToolUtilityClass toolUtility, Entity feeEntity)
        {
            var orderId = notification.OrderNo ?? notification.OrderId;
            var txnId = notification.TransactionId ?? "";
            var amount = Convert.ToInt32(toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay").Value).ToString();
            var authCode = notification.AuthIdResp ?? "";
            var txType = notification.TxType ?? "";

            var queryString = $"order_id={Uri.EscapeDataString(orderId)}" +
                $"&transaction_id={Uri.EscapeDataString(txnId)}" +
                $"&amount={amount}" +
                $"&auth_code={Uri.EscapeDataString(authCode)}" +
                $"&tx_type={Uri.EscapeDataString(txType)}";

            // DCC 資訊
            if (notification.ChAmt.HasValue)
            {
                queryString += $"&dcc_amount={notification.ChAmt.Value}" +
                    $"&dcc_currency={Uri.EscapeDataString(notification.ChCurrency ?? "")}" +
                    $"&exchange_rate={notification.ExRate ?? 0}";
            }

            return queryString;
        }

        #endregion

        #region API 回應輔助方法

        /// <summary>
        /// 建立 API 回應
        /// </summary>
        private IActionResult CreateApiResponse(dynamic response)
        {
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

            return BadRequest(new
            {
                success = false,
                error_code = response.code,
                message = response.msg
            });
        }

        /// <summary>
        /// 建立簡單 API 回應
        /// </summary>
        private IActionResult CreateSimpleApiResponse(dynamic response)
        {
            return Ok(new
            {
                success = response.code == "0000",
                order_id = response.uid,
                message = response.msg
            });
        }

        /// <summary>
        /// 處理 API 錯誤
        /// </summary>
        private IActionResult HandleApiError(string operation, Exception ex)
        {
            LogError("API", $"{operation}失敗", ex);
            return StatusCode(500, new
            {
                success = false,
                message = "系統錯誤，請稍後再試"
            });
        }

        #endregion

        #region 日誌記錄方法

        /// <summary>
        /// 記錄前台通知
        /// </summary>
        private void LogPostBackNotification(TSPGPaymentNotification notification)
        {
            var logMessage = BuildPostBackLogMessage(notification);
            System.Diagnostics.Trace.WriteLine(logMessage);
        }

        /// <summary>
        /// 建立前台通知日誌訊息
        /// </summary>
        private string BuildPostBackLogMessage(TSPGPaymentNotification notification)
        {
            var message = $"[TSPG PostBackUrl] " +
                $"訂單: {notification.OrderNo ?? notification.OrderId}, " +
                $"交易號: {notification.TransactionId}, " +
                $"狀態: {notification.State}, " +
                $"結果碼: {notification.RetCode}, " +
                $"交易類型: {notification.TxType}";

            if (!string.IsNullOrEmpty(notification.First6DigitOfPan) || !string.IsNullOrEmpty(notification.Last4DigitOfPan))
            {
                message += $", 卡號: {notification.First6DigitOfPan}******{notification.Last4DigitOfPan}";
            }

            if (!string.IsNullOrEmpty(notification.CarrierId2))
            {
                message += $", 載具: {notification.CarrierId2}";
            }

            if (notification.ChAmt.HasValue)
            {
                message += $", DCC金額: {notification.ChAmt} {notification.ChCurrency}, 匯率: {notification.ExRate}";
            }

            return message;
        }

        /// <summary>
        /// 記錄後台通知
        /// </summary>
        private void LogBackendNotification(TSPGPaymentNotification notification, string tid,
            int? payType, int? txType, string rawJson)
        {
            var logMessage = BuildBackendLogMessage(notification, tid, payType, txType);
            System.Diagnostics.Trace.WriteLine(logMessage);
            System.Diagnostics.Trace.WriteLine($"[TSPG Backend Notification] 原始JSON: {rawJson}");
        }

        /// <summary>
        /// 建立後台通知日誌訊息
        /// </summary>
        private string BuildBackendLogMessage(TSPGPaymentNotification notification, string tid,
            int? payType, int? txType)
        {
            var message = $"[TSPG Backend Notification] " +
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
                message += $", 金額: {notification.Cost}";
            }

            if (!string.IsNullOrEmpty(notification.First6DigitOfPan) || !string.IsNullOrEmpty(notification.Last4DigitOfPan))
            {
                message += $", 卡號: {notification.First6DigitOfPan}******{notification.Last4DigitOfPan}";
            }

            if (!string.IsNullOrEmpty(notification.CarrierId2))
            {
                message += $", 載具: {notification.CarrierId2}";
            }

            if (notification.ChAmt.HasValue)
            {
                message += $", DCC金額: {notification.ChAmt} {notification.ChCurrency}, " +
                    $"匯率: {notification.ExRate}, 貼水: {notification.MarkupRate}%";
            }

            return message;
        }

        /// <summary>
        /// 記錄資訊
        /// </summary>
        private void LogInfo(string method, string message)
        {
            System.Diagnostics.Trace.WriteLine($"[TSPG {method}] {message}");
        }

        /// <summary>
        /// 記錄警告
        /// </summary>
        private void LogWarning(string method, string message)
        {
            System.Diagnostics.Trace.WriteLine($"[TSPG {method}] 警告: {message}");
        }

        /// <summary>
        /// 記錄錯誤
        /// </summary>
        private void LogError(string method, string message, Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[TSPG {method}] {message}: {ex.Message}");
            if (ex.StackTrace != null)
            {
                System.Diagnostics.Trace.WriteLine($"[TSPG {method}] 堆疊: {ex.StackTrace}");
            }
        }

        #endregion
    }
}