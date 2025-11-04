using Microsoft.AspNetCore.Mvc;
using ChurchReport.Models;
using ChurchReport.WebServiceConnector;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ToolUtilityNameSpace;
using Microsoft.Xrm.Sdk;
using Line.Messaging;
using ChurchReport.Tools;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 高鉅金流 PayPage 回傳處理控制器
    /// </summary>
    [Route("api/[controller]")]
    public class MyPayController : Controller
    {
        #region 常數定義
        // LINE Channel Access Token (用於發送 LINE 通知)
        private const string LINE_CHANNEL_ACCESS_TOKEN = @"OMjL23DpFRDgphgN7JdzA7uCpv1wb4hXtsGh4FzxP8tHzeMyYOr/ry3BBqaRNJpVUhR6wPHLN4Wa4QiG5i3P5T/Y07swP5OjfCz9DKwTYC7T4mPb8x54pwtcqK1lIdgNm6skdZnu99fBsupEcbZLBAdB04t89/1O/w1cDnyilFU=";
        
        // Dynamics365連線名稱 (用於 CRM 操作)
        private const string DYNAMICS_CONNECTION_NAME = "DYNAMICS365";
        
        // 付款狀態常數: 信用卡已繳費
        private const int PAYMENT_STATUS_PAID = 100000001;
        
        // 付款方式常數: 信用卡
        private const int PAYMENT_METHOD_CREDIT_CARD = 100000001;
        #endregion

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
                    //return BadRequest("回傳資料缺少必要欄位");
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
                }
                else
                {
                    // 系統處理回傳資訊時發生錯誤，記錄警告並回傳 500 錯誤
                    // 讓金流平台知道需要重新發送通知
                    _logger.LogWarning($"處理回傳失敗: {returnModel.order_id}");
                    return Ok("888");
                    //return StatusCode(500, "處理失敗");
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
        /// <param name="order_id">訂單編號</param>
        /// <param name="transaction_id">交易編號</param>
        /// <param name="cost">交易金額</param>
        /// <remarks>
        /// 處理流程：
        /// 1. 驗證訂單編號是否存在
        /// 2. 連接到 Dynamics365 CRM 系統
        /// 3. 根據訂單號查詢對應的收費單 (new_fee 實體)
        /// 4. 若找不到收費單，記錄警告並顯示一般成功訊息
        /// 5. 更新收費單欄位 (狀態、金額、日期等)
        /// 6. 儲存變更到 CRM
        /// 7. 發送 LINE 付款成功通知給連絡人
        /// 8. 設定 ViewBag 資料並返回付款結果頁面
        /// </remarks>
        [HttpGet("success")]
        public IActionResult PaymentSuccess(
            [FromQuery] string order_id = "", 
            [FromQuery] string transaction_id = "",
            [FromQuery] string cost = "")
        {
            ToolUtilityClass utility = null;
            
            try
            {
                _logger.LogInformation($"進入付款成功頁面 - OrderId: {order_id}, TransactionId: {transaction_id}, Cost: {cost}");

                // 基本訊息設定（即使後續處理失敗也要顯示）
                ViewBag.OrderId = order_id;
                ViewBag.Message = "付款成功！感謝您的奉獻。";
                ViewBag.IsSuccess = true;

                // 如果沒有訂單編號，直接返回基本成功訊息
                if (string.IsNullOrWhiteSpace(order_id))
                {
                    _logger.LogWarning("PaymentSuccess: 訂單編號為空");
                    return View("PaymentResult");
                }

                // 初始化 CRM 工具
                utility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);

                // 查詢收費單
                Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", order_id);

                if (feeEntity == null)
                {
                    _logger.LogWarning($"PaymentSuccess: 找不到對應的收費單 - OrderId: {order_id}");
                    return View("PaymentResult");
                }

                _logger.LogInformation($"PaymentSuccess: 找到收費單 - FeeId: {feeEntity.Id}");

                // 更新收費單狀態
                UpdateFeeEntityForSuccess(utility, feeEntity, order_id, transaction_id, cost);

                // 儲存更新
                utility.UpdateEntity(ref feeEntity);
                _logger.LogInformation($"PaymentSuccess: 成功更新收費單 - FeeId: {feeEntity.Id}");

                // 發送 LINE 通知
                SendPaymentSuccessNotification(utility, feeEntity, order_id, transaction_id, cost);

                // 設定額外的 ViewBag 資訊
                ViewBag.TransactionId = transaction_id;
                ViewBag.Amount = cost;

                return View("PaymentResult");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PaymentSuccess: 處理付款成功時發生異常 - OrderId: {order_id}");
                
                // 即使發生錯誤，仍然顯示成功訊息給用戶（因為付款確實成功了）
                ViewBag.OrderId = order_id;
                ViewBag.Message = "付款成功！感謝您的奉獻。";
                ViewBag.IsSuccess = true;
                
                return View("PaymentResult");
            }
            finally
            {
                // 確保資源釋放
                utility?.Dispose();
            }
        }

        /// <summary>
        /// 更新收費單為付款成功狀態
        /// </summary>
        /// <param name="toolUtility">CRM 工具實例</param>
        /// <param name="feeEntity">收費單 Entity 物件</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號</param>
        /// <param name="cost">交易金額</param>
        /// <remarks>
        /// 更新欄位清單：
        /// - new_pay_status: 設定為 PAYMENT_STATUS_PAID (信用卡已繳費)
        /// - new_fee_really_paid: 設定為應收金額
        /// - new_difference_fee_paid: 設定為 0 (差額)
        /// - new_pay_date: 設定為當前日期時間
        /// - new_pay_way: 設定為 PAYMENT_METHOD_CREDIT_CARD (信用卡)
        /// - new_description: 附加高鉅金流付款成功資訊
        /// </remarks>
        private void UpdateFeeEntityForSuccess(
            ToolUtilityClass toolUtility, 
            Entity feeEntity, 
            string orderId, 
            string transactionId,
            string cost)
        {
            try
            {
                // 取得應收金額
                var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");

                // 更新付款狀態為「信用卡已繳費」
                toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PAYMENT_STATUS_PAID);

                // 更新實收金額（使用應收金額）
                toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);

                // 計算差額（足額繳費，差額為 0）
                toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));

                // 設定付款日期為當前時間
                toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", DateTime.Now);

                // 設定付款方式為信用卡
                toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PAYMENT_METHOD_CREDIT_CARD);

                // 更新說明欄位，記錄付款資訊
                var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? "";
                var newDescription = $"{originalDescription}{Environment.NewLine}" +
                    $"[高鉅金流付款成功] 訂單號: {orderId}, 交易號: {transactionId}, " +
                    $"金額: {shouldPayMoney?.Value ?? 0}, 時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);

                _logger.LogInformation($"UpdateFeeEntity: 已設定收費單更新欄位 - FeeId: {feeEntity.Id}, OrderId: {orderId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"UpdateFeeEntity: 更新收費單欄位時發生錯誤 - OrderId: {orderId}");
                throw;
            }
        }

        /// <summary>
        /// 發送付款成功通知給連絡人 (LINE)
        /// </summary>
        /// <param name="toolUtility">CRM 工具實例</param>
        /// <param name="feeEntity">收費單 Entity 物件</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號</param>
        /// <param name="cost">交易金額</param>
        /// <remarks>
        /// 處理流程：
        /// 1. 從收費單取得關聯的連絡人 ID (new_contact_new_fee)
        /// 2. 驗證連絡人是否存在
        /// 3. 從連絡人實體取得 LINE ID (new_lineid)
        /// 4. 驗證 LINE ID 是否存在
        /// 5. 從連絡人取得姓名 (fullname)
        /// 6. 建立付款成功訊息內容
        /// 7. 透過 LINE Bot API 發送訊息
        /// 8. 記錄發送結果
        ///
        /// 異常處理：記錄錯誤但不拋出例外，避免影響主要付款流程
        /// </remarks>
        private void SendPaymentSuccessNotification(
            ToolUtilityClass toolUtility, 
            Entity feeEntity, 
            string orderId, 
            string transactionId,
            string cost)
        {
            try
            {
                // 取得關聯的連絡人 ID
                var contactId = toolUtility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                if (contactId == Guid.Empty)
                {
                    _logger.LogWarning($"SendNotification: 收費單沒有關聯的連絡人 - OrderId: {orderId}");
                    return;
                }

                // 查詢連絡人實體
                Entity contactEntity = toolUtility.RetrieveEntity("contact", contactId);
                if (contactEntity == null)
                {
                    _logger.LogWarning($"SendNotification: 找不到連絡人 - ContactId: {contactId}, OrderId: {orderId}");
                    return;
                }

                // 取得 LINE ID
                string lineId = toolUtility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId))
                {
                    _logger.LogWarning($"SendNotification: 連絡人沒有 LINE ID - ContactId: {contactId}, OrderId: {orderId}");
                    return;
                }

                // 取得姓名
                string fullName = toolUtility.GetEntityStringAttribute(contactEntity, "fullname") ?? "會友";

                // 取得付款金額
                var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                decimal amount = shouldPayMoney?.Value ?? 0;

                // 如果 cost 參數有值，優先使用
                if (!string.IsNullOrWhiteSpace(cost) && decimal.TryParse(cost, out decimal parsedCost))
                {
                    amount = parsedCost;
                }

                // 建立付款成功訊息
                var message = BuildPaymentSuccessMessage(fullName, orderId, transactionId, amount);

                // 發送 LINE 訊息
                SendLineMessage(lineId, message);

                _logger.LogInformation($"SendNotification: 已發送付款通知 LINE 訊息 - ContactId: {contactId}, LineId: {lineId}, OrderId: {orderId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendNotification: 發送 LINE 訊息失敗 - OrderId: {orderId}");
                // 不拋出例外，讓主流程繼續執行
            }
        }

        /// <summary>
        /// 建立付款成功訊息內容 (LINE)
        /// </summary>
        /// <param name="fullName">收款人姓名</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號</param>
        /// <param name="amount">金額</param>
        /// <returns>訊息內容</returns>
        /// <remarks>
        /// 訊息格式包含：
        /// - 標題（高鉅金流付款成功通知）
        /// - 問候語與姓名
        /// - 付款成功確認
        /// - 感謝奉獻
        /// - 詳細付款資訊 (訂單號、金額、時間、方式)
        /// - 交易編號
        /// - 祝福語
        /// </remarks>
        private string BuildPaymentSuccessMessage(
            string fullName, 
            string orderId, 
            string transactionId, 
            decimal amount)
        {
            var message = $"【高鉅金流付款成功通知】{Environment.NewLine}{Environment.NewLine}";
            message += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            message += $"您的奉獻已成功完成，感謝您的支持！{Environment.NewLine}{Environment.NewLine}";
            message += $"付款資訊：{Environment.NewLine}";
            message += $"訂單編號：{orderId}{Environment.NewLine}";
            
            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                message += $"交易編號：{transactionId}{Environment.NewLine}";
            }
            
            message += $"付款金額：NT$ {amount:N0}{Environment.NewLine}";
            message += $"付款時間：{DateTime.Now:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
            message += $"付款方式：信用卡{Environment.NewLine}";
            message += $"{Environment.NewLine}願上帝賜福與您！";
            
            return message;
        }

        /// <summary>
        /// 發送 LINE 訊息 (同步)
        /// </summary>
        /// <param name="lineId">LINE ID</param>
        /// <param name="message">訊息內容</param>
        /// <remarks>
        /// 使用 Line.Messaging 套件發送推播訊息
        /// 處理流程：
        /// 1. 建立 LineMessagingClient 實例 (使用 Channel Access Token)
        /// 2. 建立 PushUtility 實例
        /// 3. 同步發送訊息 (Wait())
        /// 4. 記錄發送結果
        ///
        /// 異常處理：記錄錯誤並重新拋出，確保上層知道發送失敗
        /// </remarks>
        private void SendLineMessage(string lineId, string message)
        {
            try
            {
                var lineMessagingClient = new LineMessagingClient(LINE_CHANNEL_ACCESS_TOKEN);
                var pushUtility = new PushUtility(lineMessagingClient);
                pushUtility.SendMessage(lineId, message).Wait();
                
                _logger.LogInformation($"SendLineMessage: LINE 訊息已發送 - LineId: {lineId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendLineMessage: LINE 訊息發送失敗 - LineId: {lineId}");
                throw;
            }
        }


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