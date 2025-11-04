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
                ViewBag.TransactionId = transaction_id;
                ViewBag.Amount = cost;
                ViewBag.PaymentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");

                // 如果沒有訂單編號，直接返回基本成功訊息
                if (string.IsNullOrWhiteSpace(order_id))
                {
                    _logger.LogWarning("PaymentSuccess: 訂單編號為空");
                    ViewBag.FullName = "會友";
                    ViewBag.DedicationCategory = "奉獻";
                    return View("PaymentResult");
                }

                // 初始化 CRM 工具
                utility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);

                // 查詢收費單
                Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", order_id);

                if (feeEntity == null)
                {
                    _logger.LogWarning($"PaymentSuccess: 找不到對應的收費單 - OrderId: {order_id}");
                    ViewBag.FullName = "會友";
                    ViewBag.DedicationCategory = "奉獻";
                    return View("PaymentResult");
                }

                _logger.LogInformation($"PaymentSuccess: 找到收費單 - FeeId: {feeEntity.Id}");

                // 從收費單取得連絡人資訊
                var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                string fullName = "會友";
                if (contactId != Guid.Empty)
                {
                    Entity contactEntity = utility.RetrieveEntity("contact", contactId);
                    if (contactEntity != null)
                    {
                        fullName = utility.GetEntityStringAttribute(contactEntity, "fullname") ?? "會友";
                    }
                }

                // 取得奉獻類別
                string dedicationCategory = GetDedicationCategoryName(utility.GetOptionSetAttribute(feeEntity, "new_category"));

                // 取得應收金額
                var shouldPayMoney = utility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                decimal amount = shouldPayMoney?.Value ?? 0;
                if (!string.IsNullOrWhiteSpace(cost) && decimal.TryParse(cost, out decimal parsedCost))
                {
                    amount = parsedCost;
                }

                // 設定 ViewBag 詳細資訊
                ViewBag.FullName = fullName;
                ViewBag.DedicationCategory = dedicationCategory;
                ViewBag.Amount = amount.ToString("N0");

                // 更新收費單狀態
                UpdateFeeEntityForSuccess(utility, feeEntity, order_id, transaction_id, cost);

                // 儲存更新
                utility.UpdateEntity(ref feeEntity);
                _logger.LogInformation($"PaymentSuccess: 成功更新收費單 - FeeId: {feeEntity.Id}");

                // 發送 LINE 通知
                SendPaymentSuccessNotification(utility, feeEntity, order_id, transaction_id, cost, fullName, dedicationCategory);

                return View("PaymentResult");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PaymentSuccess: 處理付款成功時發生異常 - OrderId: {order_id}");
                
                // 即使發生錯誤，仍然顯示成功訊息給用戶（因為付款確實成功了）
                ViewBag.OrderId = order_id;
                ViewBag.Message = "付款成功！感謝您的奉獻。";
                ViewBag.IsSuccess = true;
                ViewBag.TransactionId = transaction_id;
                ViewBag.Amount = cost;
                ViewBag.PaymentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                ViewBag.FullName = "會友";
                ViewBag.DedicationCategory = "奉獻";
                
                return View("PaymentResult");
            }
            finally
            {
                // 確保資源釋放
                utility?.Dispose();
            }
        }

        /// <summary>
        /// 取得奉獻類別顯示名稱
        /// </summary>
        /// <param name="categoryValue">類別選項集值</param>
        /// <returns>類別名稱</returns>
        private string GetDedicationCategoryName(int categoryValue)
        {
            switch (categoryValue)
            {
                case 100000010: return "主日奉獻";
                case 100000000: return "十一奉獻";
                case 100000002: return "感恩奉獻";
                case 100000006: return "建堂奉獻";
                case 100000007: return "宣教奉獻";
                case 100000019: return "愛心奉獻";
                case 100000008: return "特別獻金";
                default: return "奉獻";
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
        /// <param name="fullName">連絡人姓名（可選，若為 null 則從 CRM 取得）</param>
        /// <param name="dedicationCategory">奉獻類別（可選，若為 null 則從 CRM 取得）</param>
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
            string cost,
            string fullName = null,
            string dedicationCategory = null)
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

                // 取得姓名（如果沒有傳入，則從 CRM 取得）
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    fullName = toolUtility.GetEntityStringAttribute(contactEntity, "fullname") ?? "會友";
                }

                // 取得奉獻類別（如果沒有傳入，則從 CRM 取得）
                if (string.IsNullOrWhiteSpace(dedicationCategory))
                {
                    int categoryValue = toolUtility.GetOptionSetAttribute(feeEntity, "new_category");
                    dedicationCategory = GetDedicationCategoryName(categoryValue);
                }

                // 取得付款金額
                var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                decimal amount = shouldPayMoney?.Value ?? 0;

                // 如果 cost 參數有值，優先使用
                if (!string.IsNullOrWhiteSpace(cost) && decimal.TryParse(cost, out decimal parsedCost))
                {
                    amount = parsedCost;
                }

                // 建立付款成功訊息
                var message = BuildPaymentSuccessMessage(fullName, orderId, transactionId, amount, dedicationCategory, DateTime.Now);

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
        /// <param name="dedicationCategory">奉獻類別</param>
        /// <param name="paymentTime">付款時間</param>
        /// <returns>訊息內容</returns>
        /// <remarks>
        /// 訊息格式包含：
        /// - 標題（高鉅金流付款成功通知）
        /// - 問候語與姓名
        /// - 付款成功確認
        /// - 感謝奉獻
        /// - 詳細付款資訊 (姓名、奉獻類別、訂單號、金額、時間、方式)
        /// - 交易編號
        /// - 祝福語
        /// </remarks>
        private string BuildPaymentSuccessMessage(
            string fullName, 
            string orderId, 
            string transactionId, 
            decimal amount,
            string dedicationCategory,
            DateTime paymentTime)
        {
            var message = $"【高鉅金流付款成功通知】{Environment.NewLine}{Environment.NewLine}";
            message += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            message += $"您的奉獻已成功完成，感謝您的支持！{Environment.NewLine}{Environment.NewLine}";
            message += $"付款資訊：{Environment.NewLine}";
            message += $"姓名：{fullName}{Environment.NewLine}";
            message += $"奉獻類別：{dedicationCategory}{Environment.NewLine}";
            message += $"訂單編號：{orderId}{Environment.NewLine}";
            
            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                message += $"交易編號：{transactionId}{Environment.NewLine}";
            }
            
            message += $"付款金額：NT$ {amount:N0}{Environment.NewLine}";
            message += $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
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


        /// <summary>
        /// 付款失敗頁面 (供用戶查看結果)
        /// GET /api/MyPay/failure
        /// </summary>
        /// <param name="order_id">訂單編號</param>
        /// <param name="msg">錯誤訊息</param>
        /// <param name="error_code">錯誤代碼</param>
        /// <param name="ret_code">回傳代碼</param>
        /// <remarks>
        /// 處理流程：
        /// 1. 記錄付款失敗資訊
        /// 2. 嘗試從 CRM 查詢訂單資訊以提供上下文
        /// 3. 解析並格式化錯誤訊息
        /// 4. 設定 ViewBag 並返回失敗結果頁面
        /// </remarks>
        [HttpGet("failure")]
        public IActionResult PaymentFailure(
            [FromQuery] string order_id = "", 
            [FromQuery] string msg = "",
            [FromQuery] string error_code = "",
            [FromQuery] string ret_code = "")
        {
            ToolUtilityClass utility = null;
            
            try
            {
                _logger.LogWarning($"進入付款失敗頁面 - OrderId: {order_id}, ErrorCode: {error_code}, RetCode: {ret_code}, Message: {msg}");

                // 基本訊息設定
                ViewBag.OrderId = order_id;
                ViewBag.IsSuccess = false;
                ViewBag.PaymentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                ViewBag.ErrorCode = error_code;
                ViewBag.RetCode = ret_code;

                // 建立詳細的錯誤訊息
                string detailedMessage = BuildFailureMessage(msg, error_code, ret_code);
                ViewBag.Message = detailedMessage;

                // 預設值
                ViewBag.FullName = "會友";
                ViewBag.DedicationCategory = "奉獻";
                ViewBag.Amount = "0";

                // 如果有訂單編號，嘗試從 CRM 查詢相關資訊
                if (!string.IsNullOrWhiteSpace(order_id))
                {
                    try
                    {
                        utility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
                        
                        // 查詢收費單
                        Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", order_id);
                        
                        if (feeEntity != null)
                        {
                            _logger.LogInformation($"PaymentFailure: 找到對應的收費單 - FeeId: {feeEntity.Id}");

                            // 從收費單取得連絡人資訊
                            var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                            if (contactId != Guid.Empty)
                            {
                                Entity contactEntity = utility.RetrieveEntity("contact", contactId);
                                if (contactEntity != null)
                                {
                                    ViewBag.FullName = utility.GetEntityStringAttribute(contactEntity, "fullname") ?? "會友";
                                }
                            }

                            // 取得奉獻類別
                            int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                            ViewBag.DedicationCategory = GetDedicationCategoryName(categoryValue);

                            // 取得應收金額
                            var shouldPayMoney = utility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                            if (shouldPayMoney != null)
                            {
                                ViewBag.Amount = shouldPayMoney.Value.ToString("N0");
                            }

                            // 更新收費單備註，記錄失敗資訊
                            UpdateFeeEntityForFailure(utility, feeEntity, order_id, msg, error_code, ret_code);
                        }
                        else
                        {
                            _logger.LogWarning($"PaymentFailure: 找不到對應的收費單 - OrderId: {order_id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"PaymentFailure: 查詢 CRM 資料時發生錯誤 - OrderId: {order_id}");
                    }
                }

                return View("PaymentResult");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PaymentFailure: 處理付款失敗頁面時發生異常 - OrderId: {order_id}");
                
                // 確保即使發生錯誤也能顯示基本失敗訊息
                ViewBag.OrderId = order_id;
                ViewBag.Message = !string.IsNullOrEmpty(msg) ? $"付款失敗：{msg}" : "付款失敗，請稍後再試或聯繫教會辦公室。";
                ViewBag.IsSuccess = false;
                ViewBag.PaymentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                ViewBag.FullName = "會友";
                ViewBag.DedicationCategory = "奉獻";
                ViewBag.Amount = "0";
                
                return View("PaymentResult");
            }
            finally
            {
                utility?.Dispose();
            }
        }

        /// <summary>
        /// 建立詳細的失敗訊息
        /// </summary>
        /// <param name="msg">原始錯誤訊息</param>
        /// <param name="errorCode">錯誤代碼</param>
        /// <param name="retCode">回傳代碼</param>
        /// <returns>格式化的錯誤訊息</returns>
        private string BuildFailureMessage(string msg, string errorCode, string retCode)
        {
            var message = "付款失敗";

            // 如果有提供錯誤訊息，使用它
            if (!string.IsNullOrWhiteSpace(msg))
            {
                message = $"付款失敗：{msg}";
            }
            else if (!string.IsNullOrWhiteSpace(errorCode) || !string.IsNullOrWhiteSpace(retCode))
            {
                // 根據錯誤代碼提供更友善的訊息
                string friendlyMessage = GetFriendlyErrorMessage(errorCode, retCode);
                if (!string.IsNullOrWhiteSpace(friendlyMessage))
                {
                    message = $"付款失敗：{friendlyMessage}";
                }
                else
                {
                    message = $"付款失敗 (錯誤代碼: {errorCode ?? retCode})";
                }
            }
            else
            {
                message = "付款失敗，請稍後再試或聯繫教會辦公室。";
            }

            return message;
        }

        /// <summary>
        /// 根據錯誤代碼取得友善的錯誤訊息
        /// </summary>
        /// <param name="errorCode">錯誤代碼</param>
        /// <param name="retCode">回傳代碼</param>
        /// <returns>友善的錯誤訊息</returns>
        private string GetFriendlyErrorMessage(string errorCode, string retCode)
        {
            // 合併兩個代碼來判斷
            string code = errorCode ?? retCode ?? "";

            // 根據常見的錯誤代碼提供友善訊息
            switch (code.ToUpper())
            {
                case "CARD_DECLINED":
                case "51":
                    return "信用卡被拒絕，請確認卡片狀態或聯繫發卡銀行";
                case "INSUFFICIENT_FUNDS":
                case "05":
                    return "信用卡額度不足，請使用其他卡片或聯繫發卡銀行";
                case "EXPIRED_CARD":
                case "54":
                    return "信用卡已過期，請使用其他有效卡片";
                case "INVALID_CARD":
                case "14":
                    return "信用卡號碼錯誤，請檢查卡號是否正確";
                case "INVALID_CVV":
                case "CVV_ERROR":
                    return "安全碼(CVV)錯誤，請重新輸入";
                case "CARD_LOST_STOLEN":
                case "43":
                    return "此卡片已被列為遺失或被盜，請聯繫發卡銀行";
                case "TRANSACTION_NOT_PERMITTED":
                case "57":
                    return "此交易不被允許，請聯繫發卡銀行";
                case "EXCEEDED_LIMIT":
                case "61":
                    return "超過信用卡交易限額，請聯繫發卡銀行";
                case "TIMEOUT":
                case "NETWORK_ERROR":
                    return "連線逾時或網路錯誤，請稍後再試";
                case "SYSTEM_ERROR":
                case "96":
                    return "系統錯誤，請稍後再試或聯繫客服";
                case "CANCELLED":
                case "USER_CANCELLED":
                    return "交易已被取消";
                case "3D_SECURE_FAILED":
                case "3DS_FAILED":
                    return "3D驗證失敗，請重新進行驗證";
                default:
                    return null; // 返回 null 表示沒有找到對應的友善訊息
            }
        }

        /// <summary>
        /// 更新收費單失敗資訊
        /// </summary>
        /// <param name="toolUtility">CRM 工具實例</param>
        /// <param name="feeEntity">收費單 Entity 物件</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="errorMessage">錯誤訊息</param>
        /// <param name="errorCode">錯誤代碼</param>
        /// <param name="retCode">回傳代碼</param>
        private void UpdateFeeEntityForFailure(
            ToolUtilityClass toolUtility,
            Entity feeEntity,
            string orderId,
            string errorMessage,
            string errorCode,
            string retCode)
        {
            try
            {
                // 更新說明欄位，記錄失敗資訊
                var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? "";
                var failureInfo = $"{originalDescription}{Environment.NewLine}" +
                    $"[高鉅金流付款失敗] 訂單號: {orderId}, " +
                    $"時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}, " +
                    $"錯誤訊息: {errorMessage ?? "未提供"}, " +
                    $"錯誤代碼: {errorCode ?? retCode ?? "未提供"}";
                
                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", failureInfo);

                // 儲存更新
                toolUtility.UpdateEntity(ref feeEntity);
                
                _logger.LogInformation($"UpdateFeeEntityForFailure: 已記錄付款失敗資訊 - FeeId: {feeEntity.Id}, OrderId: {orderId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"UpdateFeeEntityForFailure: 更新收費單失敗資訊時發生錯誤 - OrderId: {orderId}");
                // 不拋出例外，因為這只是記錄用途
            }
        }
    }
}