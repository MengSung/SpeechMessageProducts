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
    /// 金流 PayPage 回傳處理控制器
    /// - 接收金流回傳(MyPayReturn)
    /// - 顯示成功結果(success)
    /// - 顯示失敗結果(failure)
    /// 整理為清晰區塊，補充說明註解。
    /// </summary>
    [Route("api/[controller]")]
    public class MyPayController : Controller
    {
        #region 常數定義
        private const string LINE_CHANNEL_ACCESS_TOKEN = @"OMjL23DpFRDgphgN7JdzA7uCpv1wb4hXtsGh4FzxP8tHzeMyYOr/ry3BBqaRNJpVUhR6wPHLN4Wa4QiG5i3P5T/Y07swP5OjfCz9DKwTYC7T4mPb8x54pwtcqK1lIdgNm6skdZnu99fBsupEcbZLBAdB04t89/1O/w1cDnyilFU="; // 用於 LINE 推播
        private const string DYNAMICS_CONNECTION_NAME = "DYNAMICS365"; // CRM連線名稱
        private const int PAYMENT_STATUS_PAID =100000001; // new_pay_status: 信用卡已繳費
        private const int PAYMENT_METHOD_CREDIT_CARD =100000001; // new_pay_way: 信用卡
        #endregion

        private readonly ILogger<MyPayController> _logger;

        public MyPayController(ILogger<MyPayController> logger)
        {
            _logger = logger;
        }
        #region API: MyPay 回傳
        /// <summary>
        /// 金流伺服器回呼端點。處理後需回傳字串8888代表已接收。
        /// 處理『交易完成回傳資訊』、『非即時交易回傳資訊』、『訂單確認回傳資訊`
        /// </summary>
        [HttpPost("MyPayNotify")]
        public async Task<IActionResult> PaymentNotify([FromForm] MyPayReturnModel returnModel)
        {
            _logger.LogInformation($"[MyPay回傳] 收到金流回傳，OrderID: {returnModel?.order_id}, UID: {returnModel?.uid}, PRC: {returnModel?.prc}");

            ToolUtilityClass utility = null;
            try
            {
                // 1. 基本檢查
                if (returnModel == null)
                {
                    _logger.LogWarning("[MyPay回傳] 回傳資料為空");
                    return BadRequest("回傳資料為空");
                }

                // 2. 記錄完整的回傳資訊
                LogFullReturnData(returnModel);

                // 3. 驗證必要欄位
                var validation = returnModel.ValidateAllFields();
                if (!validation.IsValid)
                {
                    _logger.LogWarning($"[MyPay回傳] 資料驗證失敗: {string.Join(", ", validation.Errors)}");
                    // 仍回傳8888避免金流平台重送
                    return Ok("8888");
                }

                // 4. 解析交易狀態
                bool isSuccess = IsSuccessfulPaymentStatus(returnModel.prc);
                _logger.LogInformation($"[MyPay回傳] 交易狀態判定: PRC={returnModel.prc}, IsSuccess={isSuccess}");

                // 5. 查詢收費單  
                utility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
                // 高踞金流回傳用此欄位
                Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_order_number", returnModel.order_id);
                //Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", returnModel.order_id);

                if (feeEntity == null)
                {
                    _logger.LogWarning($"[MyPay回傳] 找不到對應收費單 - OrderId: {returnModel.order_id}");
                    return Ok("8888"); // 仍回傳成功避免重送
                }

                _logger.LogInformation($"[MyPay回傳] 找到收費單 - FeeId: {feeEntity.Id}");

                // 6. 判斷收費單類型（奉獻 vs 課程）
                FeeType feeType = DetermineFeeType(utility, feeEntity);
                _logger.LogInformation($"[MyPay回傳] 收費單類型: {feeType}");

                // 7. 取得連絡人資訊
                var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                Entity contactEntity = null;
                string fullName = "會友";
                string lineId = null;

                if (contactId != Guid.Empty)
                {
                    contactEntity = utility.RetrieveEntity("contact", contactId);
                    if (contactEntity != null)
                    {
                        fullName = utility.GetEntityStringAttribute(contactEntity, "fullname") ?? "會友";
                        lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                        _logger.LogInformation($"[MyPay回傳] 連絡人: {fullName}, LINE ID: {!string.IsNullOrEmpty(lineId)}");
                    }
                }

                // 8. 更新收費單
                UpdateFeeEntityWithMyPayReturn(utility, feeEntity, returnModel, isSuccess);
                utility.UpdateEntity(ref feeEntity);
                _logger.LogInformation($"[MyPay回傳] 收費單已更新 - FeeId: {feeEntity.Id}");

                // 9. 發送LINE通知（成功或失敗都發送）
                if (!string.IsNullOrWhiteSpace(lineId))
                {
                    try
                    {
                        if (isSuccess)
                        {
                            SendLineNotificationByType(utility, feeEntity, returnModel, fullName, feeType, contactEntity);
                            _logger.LogInformation($"[MyPay回傳] LINE成功通知已發送 - OrderId: {returnModel.order_id}");
                        }
                        else
                        {
                            SendLineFailureNotificationByType(utility, feeEntity, returnModel, fullName, feeType, contactEntity);
                            _logger.LogInformation($"[MyPay回傳] LINE失敗通知已發送 - OrderId: {returnModel.order_id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[MyPay回傳] 發送LINE通知失敗 - OrderId: {returnModel.order_id}");
                        // 不中斷主流程
                    }
                }
                else
                {
                    _logger.LogWarning($"[MyPay回傳] LINE ID為空，無法發送通知 - OrderId: {returnModel.order_id}");
                }

                // 10. 回傳8888確認接收
                _logger.LogInformation($"[MyPay回傳] 處理完成 - OrderId: {returnModel.order_id}");
                return Ok("8888");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 處理異常 - OrderId: {returnModel?.order_id}");
                // 發生異常仍回傳8888避免無限重送
                return Ok("8888");
            }
            finally
            {
                utility?.Dispose();
            }
        }
        #endregion

        #region LINE 訊息建立

        /// <summary>
        /// 建立奉獻成功訊息
        /// </summary>
        private string BuildDedicationSuccessMessage(string fullName, string orderId, string transactionId, decimal amount, string dedicationCategory, DateTime paymentTime)
        {
            var msg = $"【金流付款成功通知】{Environment.NewLine}{Environment.NewLine}" +
                      $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}" +
                      $"您的奉獻已成功完成，感謝您的支持！{Environment.NewLine}{Environment.NewLine}" +
                      $"付款資訊：{Environment.NewLine}" +
                      $"姓名：{fullName}{Environment.NewLine}" +
                      $"奉獻類別：{dedicationCategory}{Environment.NewLine}" +
                      $"訂單編號：{orderId}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";
            msg += $"付款金額：NT$ {amount:N0}{Environment.NewLine}" +
                   $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}" +
                   $"付款方式：信用卡{Environment.NewLine}{Environment.NewLine}願上帝賜福與您！";
            return msg;
        }

        /// <summary>
        /// 建立奉獻失敗訊息
        /// </summary>
        private string BuildDedicationFailureMessage(string fullName, string orderId, string transactionId, decimal amount, string dedicationCategory, DateTime paymentTime, string statusMessage)
        {
            var msg = $"【金流付款失敗通知】{Environment.NewLine}{Environment.NewLine}" +
                      $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}" +
                      $"很抱歉，您的奉獻付款未能完成。{Environment.NewLine}{Environment.NewLine}" +
                      $"失敗原因：{statusMessage}{Environment.NewLine}{Environment.NewLine}" +
                      $"付款資訊：{Environment.NewLine}" +
                      $"姓名：{fullName}{Environment.NewLine}" +
                      $"奉獻類別：{dedicationCategory}{Environment.NewLine}" +
                      $"訂單編號：{orderId}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";
            msg += $"應付金額：NT$ {amount:N0}{Environment.NewLine}" +
                   $"嘗試時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}" +
                   $"您可以：{Environment.NewLine}" +
                   $"1. 重新嘗試付款{Environment.NewLine}" +
                   $"2. 更換其他信用卡{Environment.NewLine}" +
                   $"3. 聯繫教會辦公室尋求協助{Environment.NewLine}{Environment.NewLine}" +
                   $"如有任何問題，請隨時與我們聯繫。";
            return msg;
        }

        /// <summary>
        /// 建立課程繳費成功訊息
        /// </summary>
        private string BuildCoursePaymentSuccessMessage(string fullName, string orderId, string transactionId, decimal amount, string courseName, string courseSchedule, string courseLocation, DateTime paymentTime)
        {
            var msg = $"【金流付款成功通知】{Environment.NewLine}{Environment.NewLine}" +
                      $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}" +
                      $"您的課程繳費已成功完成！{Environment.NewLine}{Environment.NewLine}" +
                      $"課程資訊：{Environment.NewLine}" +
                      $"姓名：{fullName}{Environment.NewLine}" +
                      $"課程名稱：{courseName}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(courseSchedule)) msg += $"上課時間：{courseSchedule}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(courseLocation)) msg += $"上課地點：{courseLocation}{Environment.NewLine}";
            msg += $"{Environment.NewLine}付款資訊：{Environment.NewLine}" +
                   $"訂單編號：{orderId}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(transactionId)) msg += $"交易編號：{transactionId}{Environment.NewLine}";
            msg += $"繳費金額：NT$ {amount:N0}{Environment.NewLine}" +
                   $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}" +
                   $"付款方式：信用卡{Environment.NewLine}{Environment.NewLine}期待在課程中與您相見！";
            return msg;
        }

        /// <summary>
        /// 建立課程繳費失敗訊息
        /// </summary>
        private string BuildCoursePaymentFailureMessage(string fullName, string orderId, string transactionId, decimal amount, string courseName, string courseSchedule, string courseLocation, DateTime paymentTime, string statusMessage)
        {
            var msg = $"【金流付款失敗通知】{Environment.NewLine}{Environment.NewLine}" +
                      $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}" +
                      $"很抱歉，您的課程繳費未能完成。{Environment.NewLine}{Environment.NewLine}" +
                      $"失敗原因：{statusMessage}{Environment.NewLine}{Environment.NewLine}" +
                      $"課程資訊：{Environment.NewLine}" +
                      $"姓名：{fullName}{Environment.NewLine}" +
                      $"課程名稱：{courseName}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(courseSchedule)) msg += $"上課時間：{courseSchedule}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(courseLocation)) msg += $"上課地點：{courseLocation}{Environment.NewLine}";
            msg += $"{Environment.NewLine}付款資訊：{Environment.NewLine}" +
                   $"訂單編號：{orderId}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(transactionId)) msg += $"交易編號：{transactionId}{Environment.NewLine}";
            msg += $"應繳金額：NT$ {amount:N0}{Environment.NewLine}" +
                   $"嘗試時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}" +
                   $"您可以：{Environment.NewLine}" +
                   $"1. 重新嘗試付款{Environment.NewLine}" +
                   $"2. 更換其他信用卡{Environment.NewLine}" +
                   $"3. 聯繫教會辦公室尋求協助{Environment.NewLine}{Environment.NewLine}" +
                   $"如有任何問題，請隨時與我們聯繫。";
            return msg;
        }

        /// <summary>
        /// 建立一般繳費成功訊息
        /// </summary>
        private string BuildGeneralPaymentSuccessMessage(string fullName, string orderId, string transactionId, decimal amount, string itemName, DateTime paymentTime)
        {
            var msg = $"【金流付款成功通知】{Environment.NewLine}{Environment.NewLine}" +
                      $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}" +
                      $"您的付款已成功完成！{Environment.NewLine}{Environment.NewLine}" +
                      $"付款資訊：{Environment.NewLine}" +
                      $"姓名：{fullName}{Environment.NewLine}" +
                      $"項目：{itemName}{Environment.NewLine}" +
                      $"訂單編號：{orderId}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(transactionId)) msg += $"交易編號：{transactionId}{Environment.NewLine}";
            msg += $"付款金額：NT$ {amount:N0}{Environment.NewLine}" +
                   $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}" +
                   $"付款方式：信用卡{Environment.NewLine}{Environment.NewLine}感謝您的支持！";
            return msg;
        }

        /// <summary>
        /// 建立一般繳費失敗訊息
        /// </summary>
        private string BuildGeneralPaymentFailureMessage(string fullName, string orderId, string transactionId, decimal amount, string itemName, DateTime paymentTime, string statusMessage)
        {
            var msg = $"【金流付款失敗通知】{Environment.NewLine}{Environment.NewLine}" +
                      $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}" +
                      $"很抱歉，您的付款未能完成。{Environment.NewLine}{Environment.NewLine}" +
                      $"失敗原因：{statusMessage}{Environment.NewLine}{Environment.NewLine}" +
                      $"付款資訊：{Environment.NewLine}" +
                      $"姓名：{fullName}{Environment.NewLine}" +
                      $"項目：{itemName}{Environment.NewLine}" +
                      $"訂單編號：{orderId}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(transactionId)) msg += $"交易編號：{transactionId}{Environment.NewLine}";
            msg += $"應付金額：NT$ {amount:N0}{Environment.NewLine}" +
                   $"嘗試時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}" +
                   $"您可以：{Environment.NewLine}" +
                   $"1. 重新嘗試付款{Environment.NewLine}" +
                   $"2. 更換其他信用卡{Environment.NewLine}" +
                   $"3. 聯繫教會辦公室尋求協助{Environment.NewLine}{Environment.NewLine}" +
                   $"如有任何問題，請隨時與我們聯繫。";
            return msg;
        }

        /// <summary>
        /// 發送LINE訊息
        /// </summary>
        private void SendLineMessage(string lineId, string message)
        {
            try
            {
                var lineMessagingClient = new LineMessagingClient(LINE_CHANNEL_ACCESS_TOKEN);
                var pushUtility = new PushUtility(lineMessagingClient);
                pushUtility.SendMessage(lineId, message).Wait();
                _logger.LogInformation($"SendLineMessage: 已發送 - LineId: {lineId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendLineMessage: 發送失敗 - LineId: {lineId}");
                throw;
            }
        }

        #endregion

        #region API: 成功頁面

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
        ///付款成功導向頁。顯示資訊並更新 CRM以及發送 LINE 通知。
        /// </summary>
        [HttpGet("success_back")]
        public IActionResult PaymentSuccessBack(
            [FromQuery] string order_id = "",
            [FromQuery] string uid = "",
            [FromQuery] string key = "",
            [FromQuery] string cost = "",
            [FromQuery] string actual_cost = "",
            [FromQuery] string prc = "",
            [FromQuery] string pfn = "",
            [FromQuery] string finishtime = "",
            [FromQuery] string cardno = "",
            [FromQuery] string acode = "",
            [FromQuery] string echo_0 = "",
            [FromQuery] string echo_1 = "",
            [FromQuery] string echo_2 = "",
            [FromQuery] string echo_3 = "",
            [FromQuery] string echo_4 = "")
        {
            ToolUtilityClass utility = null;
            try
            {
                _logger.LogInformation($"進入付款成功頁面 - OrderId: {order_id}, UID: {uid}, Key: {key}, PRC: {prc}, Cost: {cost}, ActualCost: {actual_cost}, PFN: {pfn}, FinishTime: {finishtime}");

                //交易狀態與時間解析
                bool isPaymentSuccess = IsSuccessfulPaymentStatus(prc);
                string paymentStatusMessage = GetPaymentStatusMessage(prc);
                DateTime paymentDateTime = ParseFinishTime(finishtime);

                // 基本 ViewBag（即使後續處理失敗也能顯示）
                ViewBag.OrderId = order_id;
                ViewBag.UID = uid;
                ViewBag.TransactionKey = key;
                ViewBag.Message = isPaymentSuccess ? "付款成功！感謝您的支持。" : paymentStatusMessage;
                ViewBag.IsSuccess = isPaymentSuccess;
                ViewBag.TransactionId = uid;
                ViewBag.Amount = string.IsNullOrWhiteSpace(actual_cost) ? cost : actual_cost;
                ViewBag.PaymentTime = paymentDateTime.ToString("yyyy/MM/dd HH:mm:ss");
                ViewBag.PaymentMethod = GetPaymentMethodName(pfn);
                ViewBag.FeeType = "unknown";
                ViewBag.CardNo = cardno;
                ViewBag.AuthCode = acode;

                if (!isPaymentSuccess)
                {
                    _logger.LogWarning($"PaymentSuccess: 非成功狀態 - OrderId: {order_id}, PRC: {prc}");
                    ViewBag.FullName = "會友";
                    ViewBag.DedicationCategory = "付款";
                    ViewBag.ErrorCode = prc;
                    return View("PaymentResult");
                }

                if (string.IsNullOrWhiteSpace(order_id))
                {
                    _logger.LogWarning("PaymentSuccess: 訂單編號為空");
                    ViewBag.FullName = "會友";
                    ViewBag.DedicationCategory = "付款";
                    return View("PaymentResult");
                }

                //取得 CRM 資料
                utility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
                // 高踞金流回傳用此欄位
                Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_order_number", order_id);
                //Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", order_id);

                if (feeEntity == null)
                {
                    _logger.LogWarning($"PaymentSuccess: 找不到對應收費單 - OrderId: {order_id}");
                    ViewBag.FullName = "會友";
                    ViewBag.DedicationCategory = "付款";
                    return View("PaymentResult");
                }

                _logger.LogInformation($"PaymentSuccess: 找到收費單 - FeeId: {feeEntity.Id}");

                //連絡人
                var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                string fullName = "會友";
                Entity contactEntity = null;
                if (contactId != Guid.Empty)
                {
                    contactEntity = utility.RetrieveEntity("contact", contactId);
                    if (contactEntity != null)
                        fullName = utility.GetEntityStringAttribute(contactEntity, "fullname") ?? "會友";
                }

                // 收費單類型
                FeeType feeType = DetermineFeeType(utility, feeEntity);
                ViewBag.FeeType = feeType.ToString().ToLower();

                // 金額
                var shouldPayMoney = utility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                decimal amount = shouldPayMoney?.Value ??0m;
                if (!string.IsNullOrWhiteSpace(actual_cost) && decimal.TryParse(actual_cost, out var parsedActual))
                    amount = parsedActual;
                else if (!string.IsNullOrWhiteSpace(cost) && decimal.TryParse(cost, out var parsedCost))
                    amount = parsedCost;

                ViewBag.FullName = fullName;
                ViewBag.Amount = amount.ToString("N0");

                string itemName;
                string viewName;
                if (feeType == FeeType.Dedication)
                {
                    int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                    itemName = GetDedicationCategoryName(categoryValue);
                    ViewBag.DedicationCategory = itemName;
                    ViewBag.Message = "付款成功！感謝您的奉獻。";
                    viewName = "PaymentResult";
                }
                else if (feeType == FeeType.Course)
                {
                    itemName = GetCourseName(utility, feeEntity);
                    ViewBag.CourseName = itemName;
                    ViewBag.Message = "付款成功！課程繳費已完成。";
                    viewName = "CoursePaymentResult";
                    ViewBag.CourseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? "";
                    ViewBag.CourseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? "";
                }
                else
                {
                    itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";
                    ViewBag.ItemName = itemName;
                    ViewBag.Message = "付款成功！";
                    viewName = "PaymentResult";
                }

                // 更新收費單並儲存
                UpdateFeeEntityForSuccessWithMyPay(utility, feeEntity, order_id, uid, key, cost, actual_cost, prc, pfn, paymentDateTime, cardno, acode);
                utility.UpdateEntity(ref feeEntity);

                // 發送 LINE 通知
                SendPaymentNotificationByType(utility, feeEntity, order_id, uid, cost, fullName, itemName, feeType, amount, contactEntity);

                return View(viewName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PaymentSuccess: 發生異常 - OrderId: {order_id}, UID: {uid}");

                //仍顯示成功訊息（避免影響用戶體驗）
                ViewBag.OrderId = order_id;
                ViewBag.Message = "付款成功！感謝您的支持。";
                ViewBag.IsSuccess = true;
                ViewBag.TransactionId = uid;
                ViewBag.Amount = cost;
                ViewBag.PaymentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                ViewBag.FullName = "會友";
                ViewBag.DedicationCategory = "付款";
                ViewBag.FeeType = "unknown";
                return View("PaymentResult");
            }
            finally
            {
                utility?.Dispose();
            }
        }
        #endregion

        #region API:失敗頁面

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

        /// <summary>
        ///付款失敗導向頁。顯示錯誤說明並將失敗紀錄寫回 CRM。
        /// </summary>
        [HttpGet("failure_back")]
        public IActionResult PaymentFailureBack(
            [FromQuery] string order_id = "",
            [FromQuery] string msg = "",
            [FromQuery] string error_code = "",
            [FromQuery] string ret_code = "")
        {
            ToolUtilityClass utility = null;
            try
            {
                _logger.LogWarning($"進入付款失敗頁面 - OrderId: {order_id}, ErrorCode: {error_code}, RetCode: {ret_code}, Message: {msg}");

                ViewBag.OrderId = order_id;
                ViewBag.IsSuccess = false;
                ViewBag.PaymentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                ViewBag.ErrorCode = error_code;
                ViewBag.RetCode = ret_code;
                ViewBag.Message = BuildFailureMessage(msg, error_code, ret_code);
                ViewBag.FullName = "會友";
                ViewBag.DedicationCategory = "奉獻";
                ViewBag.Amount = "0";

                if (!string.IsNullOrWhiteSpace(order_id))
                {
                    try
                    {
                        utility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
                        // 高踞金流回傳用此欄位
                        Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_order_number", order_id);
                        //Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", order_id);
                        if (feeEntity != null)
                        {
                            _logger.LogInformation($"PaymentFailure: 找到對應收費? - FeeId: {feeEntity.Id}");

                            var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                            if (contactId != Guid.Empty)
                            {
                                var contactEntity = utility.RetrieveEntity("contact", contactId);
                                if (contactEntity != null)
                                    ViewBag.FullName = utility.GetEntityStringAttribute(contactEntity, "fullname") ?? "會友";
                            }

                            int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                            ViewBag.DedicationCategory = GetDedicationCategoryName(categoryValue);

                            var shouldPayMoney = utility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                            if (shouldPayMoney != null)
                                ViewBag.Amount = shouldPayMoney.Value.ToString("N0");

                            UpdateFeeEntityForFailure(utility, feeEntity, order_id, msg, error_code, ret_code);
                        }
                        else
                        {
                            _logger.LogWarning($"PaymentFailure: 找不到對應收費單 - OrderId: {order_id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"PaymentFailure: 查詢 CRM 發生錯誤 - OrderId: {order_id}");
                    }
                }

                return View("PaymentResult");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PaymentFailure: 發生異常 - OrderId: {order_id}");

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
        #endregion

        #region 狀態/文字/CRM更新輔助方法

        /// <summary>
        /// 判斷是否為成功的交易狀態
        /// </summary>
        private bool IsSuccessfulPaymentStatus(string prc)
        {
            if (string.IsNullOrWhiteSpace(prc)) return false;
            switch (prc)
            {
                case "250": // 付款成功
                case "290": // 交易成功但資訊不符
                case "600": // 結帳完成
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 建立失敗訊息
        /// </summary>
        private string BuildFailureMessage(string msg, string errorCode, string retCode)
        {
            var message = "付款失敗";
            if (!string.IsNullOrWhiteSpace(msg))
            {
                message = $"付款失敗：{msg}";
            }
            else if (!string.IsNullOrWhiteSpace(errorCode) || !string.IsNullOrWhiteSpace(retCode))
            {
                string friendly = GetFriendlyErrorMessage(errorCode, retCode);
                message = !string.IsNullOrWhiteSpace(friendly) ? $"付款失敗：{friendly}" : $"付款失敗 (錯誤代碼: {errorCode ?? retCode})";
            }
            else
            {
                message = "付款失敗，請稍後再試或聯繫教會辦公室。";
            }
            return message;
        }

        /// <summary>
        /// 取得友善的錯誤訊息
        /// </summary>
        private string GetFriendlyErrorMessage(string errorCode, string retCode)
        {
            string code = errorCode ?? retCode ?? "";
            switch ((code ?? string.Empty).ToUpper())
            {
                case "CARD_DECLINED":
                case "51": return "信用卡被拒絕，請確認卡片狀態或聯繫發卡銀行";
                case "INSUFFICIENT_FUNDS":
                case "05": return "信用卡額度不足，請使用其他卡片或聯繫發卡銀行";
                case "EXPIRED_CARD":
                case "54": return "信用卡已過期，請使用其他有效卡片";
                case "INVALID_CARD":
                case "14": return "信用卡號碼錯誤，請檢查卡號是否正確";
                case "INVALID_CVV":
                case "CVV_ERROR": return "安全碼(CVV)錯誤，請重新輸入";
                case "CARD_LOST_STOLEN":
                case "43": return "此卡片已被列為遺失或被盜，請聯繫發卡銀行";
                case "TRANSACTION_NOT_PERMITTED":
                case "57": return "此交易不被允許，請聯繫發卡銀行";
                case "EXCEEDED_LIMIT":
                case "61": return "超過信用卡交易限額，請聯繫發卡銀行";
                case "TIMEOUT":
                case "NETWORK_ERROR": return "連線逾時或網路錯誤，請稍後再試";
                case "SYSTEM_ERROR":
                case "96": return "系統錯誤，請稍後再試或聯繫客服";
                case "CANCELLED":
                case "USER_CANCELLED": return "交易已被取消";
                case "3D_SECURE_FAILED":
                case "3DS_FAILED": return "3D驗證失敗，請重新進行驗證";
                default: return null;
            }
        }

        /// <summary>
        /// 更新收費單（成功_success_back用）
        /// </summary>
        private void UpdateFeeEntityForSuccessWithMyPay(
            ToolUtilityClass toolUtility,
            Entity feeEntity,
            string orderId,
            string uid,
            string key,
            string cost,
            string actualCost,
            string prc,
            string pfn,
            DateTime paymentTime,
            string cardno,
            string acode)
        {
            try
            {
                var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PAYMENT_STATUS_PAID);
                toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);
                toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));
                toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", paymentTime);
                toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PAYMENT_METHOD_CREDIT_CARD);

                var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? string.Empty;
                var paymentMethodName = GetPaymentMethodName(pfn);
                var statusMessage = GetPaymentStatusMessage(prc);
                var newDescription =
                    originalDescription + Environment.NewLine +
                    "[金流付款成功]" + Environment.NewLine +
                    $"訂單號: {orderId}{Environment.NewLine}" +
                    $"交易流水號(UID): {uid}{Environment.NewLine}" +
                    $"交易驗證碼(Key): {key}{Environment.NewLine}" +
                    $"交易狀態(PRC): {prc} ({statusMessage}){Environment.NewLine}" +
                    $"====== 付款方式 ======{Environment.NewLine}" +
                    $"支付工具代碼(PFN): {pfn}{Environment.NewLine}" +
                    $"支付工具名稱: {paymentMethodName}{Environment.NewLine}" +
                    $"付款方式編號: {PAYMENT_METHOD_CREDIT_CARD}{Environment.NewLine}" +
                    $"======================={Environment.NewLine}" +
                    $"交易金額: {cost}{Environment.NewLine}" +
                    $"實際金額: {actualCost ?? cost}{Environment.NewLine}" +
                    $"卡號: {cardno}{Environment.NewLine}" +
                    $"授權碼: {acode}{Environment.NewLine}" +
                    $"付款時間: {paymentTime:yyyy-MM-dd HH:mm:ss}";

                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);
                _logger.LogInformation($"UpdateFeeEntityWithMyPay: 更新完成 - FeeId: {feeEntity.Id}, OrderId: {orderId}, 付款方式: {paymentMethodName}(PFN:{pfn})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"UpdateFeeEntityWithMyPay: 更新收費單失敗 - OrderId: {orderId}");
                throw;
            }
        }

        /// <summary>
        /// 更新收費單（失敗_failure_back用）
        /// </summary>
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
                var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? string.Empty;
                var failureInfo =
                    originalDescription + Environment.NewLine +
                    $"[金流付款失敗] 訂單號: {orderId}, " +
                    $"時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}, " +
                    $"錯誤訊息: {errorMessage ?? "未提供"}, " +
                    $"錯誤代碼: {errorCode ?? retCode ?? "未提供"}";

                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", failureInfo);
                toolUtility.UpdateEntity(ref feeEntity);
                _logger.LogInformation($"UpdateFeeEntityForFailure: 已記錄失敗資訊 - FeeId: {feeEntity.Id}, OrderId: {orderId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"UpdateFeeEntityForFailure: 更新失敗 - OrderId: {orderId}");
            }
        }

        /// <summary>
        /// 發送付款通知（success_back用）
        /// </summary>
        private void SendPaymentNotificationByType(
            ToolUtilityClass utility,
            Entity feeEntity,
            string orderId,
            string transactionId,
            string cost,
            string fullName,
            string itemName,
            FeeType feeType,
            decimal amount,
            Entity contactEntity)
        {
            try
            {
                var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                if (contactId == Guid.Empty)
                {
                    _logger.LogWarning($"SendNotification: 無連絡人 - OrderId: {orderId}");
                    return;
                }

                if (contactEntity == null)
                {
                    contactEntity = utility.RetrieveEntity("contact", contactId);
                    if (contactEntity == null)
                    {
                        _logger.LogWarning($"SendNotification: 找不到連絡人 - ContactId: {contactId}");
                        return;
                    }
                }

                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId))
                {
                    _logger.LogWarning($"SendNotification: 無 LINE ID - ContactId: {contactId}");
                    return;
                }

                string message;
                if (feeType == FeeType.Dedication)
                {
                    message = BuildDedicationSuccessMessage(fullName, orderId, transactionId, amount, itemName, DateTime.Now);
                }
                else if (feeType == FeeType.Course)
                {
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? "";
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? "";
                    message = BuildCoursePaymentSuccessMessage(fullName, orderId, transactionId, amount, itemName, courseSchedule, courseLocation, DateTime.Now);
                }
                else
                {
                    message = BuildGeneralPaymentSuccessMessage(fullName, orderId, transactionId, amount, itemName, DateTime.Now);
                }

                SendLineMessage(lineId, message);
                _logger.LogInformation($"SendNotification: 已發送 LINE 訊息 - OrderId: {orderId}, LineId: {lineId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendNotification: 發送 LINE失敗 - OrderId: {orderId}");
            }
        }

        #endregion

        #region 輔助方法

        /// <summary>
        /// 記錄完整的回傳資料（用於除錯）
        /// </summary>
        private void LogFullReturnData(MyPayReturnModel model)
        {
            try
            {
                var logData = $"[MyPay完整回傳資料]\n" +
                             $"核心欄位: uid={model.uid}, key={model.key}, prc={model.prc}, order_id={model.order_id}\n" +
                             $"交易資訊: finishtime={model.finishtime}, cost={model.cost}, actual_cost={model.actual_cost}\n" +
                             $"付款資訊: pfn={model.pfn}, cardno={model.cardno}, acode={model.acode}\n" +
                             $"消費者: user_id={model.user_id}\n" +
                             $"自訂參數: echo_0={model.echo_0}, echo_1={model.echo_1}, echo_2={model.echo_2}\n" +
                             $"舊版欄位: state={model.state}, msg={model.msg}, transaction_id={model.transaction_id}";

                _logger.LogInformation(logData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MyPay回傳] 記錄回傳資料時發生錯誤");
            }
        }

        /// <summary>
        /// 更新收費單欄位（支援所有金流回傳參數）
        /// </summary>
        private void UpdateFeeEntityWithMyPayReturn(
            ToolUtilityClass toolUtility,
            Entity feeEntity,
            MyPayReturnModel model,
            bool isSuccess)
        {
            try
            {
                // 解析交易時間
                DateTime paymentTime = ParseFinishTime(model.finishtime);

                if (isSuccess)
                {
                    // 成功：更新為已繳費狀態
                    var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                    toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PAYMENT_STATUS_PAID);
                    toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);
                    toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));
                    toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", paymentTime);
                    toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PAYMENT_METHOD_CREDIT_CARD);
                }

                // 組合完整的備註資訊
                var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? string.Empty;
                var paymentMethodName = GetPaymentMethodName(model.pfn);
                var statusMessage = GetPaymentStatusMessage(model.prc);

                var newDescription = originalDescription + Environment.NewLine +
                    $"[金流回傳資訊 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}]" + Environment.NewLine +
                    $"====== 核心欄位 ======" + Environment.NewLine +
                    $"訂單號(order_id): {model.order_id}" + Environment.NewLine +
                    $"交易流水號(uid): {model.uid}" + Environment.NewLine +
                    $"交易驗證碼(key): {model.key}" + Environment.NewLine +
                    $"交易狀態碼(prc): {model.prc} ({statusMessage})" + Environment.NewLine +
                    $"====== 交易資訊 ======" + Environment.NewLine +
                    $"完成時間: {paymentTime:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                    $"交易金額: {model.cost}" + Environment.NewLine +
                    $"實際金額: {model.actual_cost ?? model.cost}" + Environment.NewLine +
                    $"交易幣別: {model.currency ?? "TWD"}" + Environment.NewLine +
                    $"====== 付款資訊 ======" + Environment.NewLine +
                    $"付款方式(pfn): {paymentMethodName}" + Environment.NewLine +
                    $"卡號: {model.cardno}" + Environment.NewLine +
                    $"授權碼: {model.acode}" + Environment.NewLine +
                    $"卡別: {model.card_type}" + Environment.NewLine +
                    $"發卡行: {model.issuing_bank}" + Environment.NewLine +
                    $"發卡行代碼: {model.issuing_bank_uid}" + Environment.NewLine;

                // 如果有分期資訊
                if (!string.IsNullOrEmpty(model.installment))
                {
                    newDescription += $"分期資訊: {model.installment}" + Environment.NewLine;
                }

                // 如果有紅利資訊
                if (!string.IsNullOrEmpty(model.redeem))
                {
                    newDescription += $"紅利資訊: {model.redeem}" + Environment.NewLine;
                }

                // 服務商資訊
                if (!string.IsNullOrEmpty(model.supplier_name))
                {
                    newDescription += $"====== 服務商資訊 ======" + Environment.NewLine +
                                    $"服務商: {model.supplier_name}" + Environment.NewLine +
                                    $"服務商代碼: {model.supplier_code}" + Environment.NewLine;
                }

                // 定期定額資訊
                if (!string.IsNullOrEmpty(model.payment_name))
                {
                    newDescription += $"====== 定期定額資訊 ======" + Environment.NewLine +
                                    $"扣款名稱: {model.payment_name}" + Environment.NewLine +
                                    $"期數: {model.nois}" + Environment.NewLine +
                                    $"群組編號: {model.group_id}" + Environment.NewLine;
                }

                // 虛擬帳號/超商代碼資訊
                if (!string.IsNullOrEmpty(model.bank_id))
                {
                    newDescription += $"====== 虛擬帳號資訊 ======" + Environment.NewLine +
                                    $"銀行代碼: {model.bank_id}" + Environment.NewLine +
                                    $"有效期限: {model.expired_date}" + Environment.NewLine;
                }

                // 自訂參數
                if (!string.IsNullOrEmpty(model.echo_0) || !string.IsNullOrEmpty(model.echo_1))
                {
                    newDescription += $"====== 自訂參數 ======" + Environment.NewLine +
                                    $"echo_0: {model.echo_0}" + Environment.NewLine +
                                    $"echo_1: {model.echo_1}" + Environment.NewLine +
                                    $"echo_2: {model.echo_2}" + Environment.NewLine +
                                    $"echo_3: {model.echo_3}" + Environment.NewLine +
                                    $"echo_4: {model.echo_4}" + Environment.NewLine;
                }

                // 舊版欄位（向下相容）
                newDescription += $"====== 舊版相容欄位 ======" + Environment.NewLine +
                                $"state: {model.state}" + Environment.NewLine +
                                $"msg: {model.msg}" + Environment.NewLine +
                                $"transaction_id: {model.transaction_id}" + Environment.NewLine +
                                $"store_uid: {model.store_uid}" + Environment.NewLine;

                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);

                _logger.LogInformation($"[MyPay回傳] 收費單欄位已更新 - FeeId: {feeEntity.Id}, OrderId: {model.order_id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 更新收費單失敗 - OrderId: {model.order_id}");
                throw;
            }
        }

        /// <summary>
        /// 根據收費單類型發送不同的LINE通知
        /// </summary>
        private void SendLineNotificationByType(
            ToolUtilityClass utility,
            Entity feeEntity,
            MyPayReturnModel model,
            string fullName,
            FeeType feeType,
            Entity contactEntity)
        {
            try
            {
                if (contactEntity == null)
                {
                    _logger.LogWarning($"[MyPay回傳] ContactEntity為空，無法發送LINE通知");
                    return;
                }

                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId))
                {
                    _logger.LogWarning($"[MyPay回傳] LINE ID為空");
                    return;
                }

                // 解析金額
                decimal amount = 0m;
                if (!string.IsNullOrEmpty(model.actual_cost) && decimal.TryParse(model.actual_cost, out var actualCost))
                {
                    amount = actualCost;
                }
                else if (!string.IsNullOrEmpty(model.cost) && decimal.TryParse(model.cost, out var cost))
                {
                    amount = cost;
                }

                // 解析時間
                DateTime paymentTime = ParseFinishTime(model.finishtime);

                string message;
                if (feeType == FeeType.Dedication)
                {
                    // 奉獻類型
                    int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                    string dedicationCategory = GetDedicationCategoryName(categoryValue);
                    message = BuildDedicationSuccessMessage(fullName, model.order_id, model.uid, amount, dedicationCategory, paymentTime);
                }
                else if (feeType == FeeType.Course)
                {
                    // 課程類型
                    string courseName = GetCourseName(utility, feeEntity);
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? "";
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? "";
                    message = BuildCoursePaymentSuccessMessage(fullName, model.order_id, model.uid, amount, courseName, courseSchedule, courseLocation, paymentTime);
                }
                else
                {
                    // 其他類型
                    string itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";
                    message = BuildGeneralPaymentSuccessMessage(fullName, model.order_id, model.uid, amount, itemName, paymentTime);
                }

                // 發送LINE訊息
                SendLineMessage(lineId, message);
                _logger.LogInformation($"[MyPay回傳] LINE通知已發送 - LineId: {lineId}, OrderId: {model.order_id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 發送LINE通知失敗 - OrderId: {model.order_id}");
                throw;
            }
        }

        /// <summary>
        /// 根據收費單類型發送不同的LINE失敗通知
        /// </summary>
        private void SendLineFailureNotificationByType(
            ToolUtilityClass utility,
            Entity feeEntity,
            MyPayReturnModel model,
            string fullName,
            FeeType feeType,
            Entity contactEntity)
        {
            try
            {
                if (contactEntity == null)
                {
                    _logger.LogWarning($"[MyPay回傳] ContactEntity為空，無法發送LINE失敗通知");
                    return;
                }

                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId))
                {
                    _logger.LogWarning($"[MyPay回傳] LINE ID為空，無法發送失敗通知");
                    return;
                }

                // 解析金額
                decimal amount = 0m;
                var shouldPayMoney = utility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                if (shouldPayMoney != null && shouldPayMoney.Value > 0)
                {
                    amount = shouldPayMoney.Value;
                }
                else if (!string.IsNullOrEmpty(model.actual_cost) && decimal.TryParse(model.actual_cost, out var actualCost))
                {
                    amount = actualCost;
                }
                else if (!string.IsNullOrEmpty(model.cost) && decimal.TryParse(model.cost, out var cost))
                {
                    amount = cost;
                }

                // 解析時間
                DateTime paymentTime = ParseFinishTime(model.finishtime);

                // 取得失敗原因
                string statusMessage = GetPaymentStatusMessage(model.prc);

                string message;
                if (feeType == FeeType.Dedication)
                {
                    // 奉獻類型失敗訊息
                    int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                    string dedicationCategory = GetDedicationCategoryName(categoryValue);
                    message = BuildDedicationFailureMessage(fullName, model.order_id, model.uid, amount, dedicationCategory, paymentTime, statusMessage);
                }
                else if (feeType == FeeType.Course)
                {
                    // 課程類型失敗訊息
                    string courseName = GetCourseName(utility, feeEntity);
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? "";
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? "";
                    message = BuildCoursePaymentFailureMessage(fullName, model.order_id, model.uid, amount, courseName, courseSchedule, courseLocation, paymentTime, statusMessage);
                }
                else
                {
                    // 其他類型失敗訊息
                    string itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";
                    message = BuildGeneralPaymentFailureMessage(fullName, model.order_id, model.uid, amount, itemName, paymentTime, statusMessage);
                }

                // 發送LINE訊息
                SendLineMessage(lineId, message);
                _logger.LogInformation($"[MyPay回傳] LINE失敗通知已發送 - LineId: {lineId}, OrderId: {model.order_id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 發送LINE失敗通知失敗 - OrderId: {model.order_id}");
                throw;
            }
        }

        #endregion

        #region 收費單類型與狀態判斷

        /// <summary>
        /// 收費單類型枚舉
        /// </summary>
        private enum FeeType { Dedication, Course, Other }

        /// <summary>
        /// 取得交易狀態訊息
        /// </summary>
        private string GetPaymentStatusMessage(string prc)
        {
            if (string.IsNullOrWhiteSpace(prc)) return "付款狀態未知";
            switch (prc)
            {
                // 100 系列 - 資料錯誤
                case "100": 
                    return "資料錯誤 - MYPAYLINK收到資料，但是格式或資料錯誤";

                // 200 系列 - 處理中/成功狀態
                case "200": 
                    return "資料正確 - MYPAYLINK收到正確資料，會接續下一步交易";
                case "220": 
                    return "取消成功 - 如申請取消，取消訂單狀態為取消成功";
                case "230": 
                    return "退款成功 - 如申請退款，申請退款成功時狀態";
                case "250": 
                    return "付款成功 - 此次交易，消費者付款成功";
                case "260": 
                    return "交易成功，尚未付款完成 - 超商代碼繳費，請等候消費者繳費入帳完成付款或消費者放棄交易";
                case "265": 
                    return "訂單綁定 - 表示訂單編號生效，進入貸款頁面，但尚未註冊";
                case "270": 
                    return "交易成功，尚未付款完成 - 虛擬帳號，請等候消費者繳費入帳";
                case "275": 
                    return "交易成功，待審核（核貸中） - 無卡分期，請等候審查通過";
                case "280": 
                    return "交易成功，尚未付款完成 - 儲值/WEBATM，線上待付款，等待狀態";
                case "290": 
                    return "交易成功但資訊不符 - 交易成功，但資訊不符（包含金額不符、已逾期...等），該類型交易請特別注意";

                // 300 系列 - 失敗狀態
                case "300": 
                    return "交易失敗 - 金流服務商回傳交易失敗或該筆交易超過風險控管限制規則";
                case "380": 
                    return "逾期交易 - 超商代碼或虛擬帳號交易，超過系統設定繳費期限";

                // 400 系列 - 系統錯誤
                case "400": 
                    return "系統錯誤訊息 - 若MYPAY LINK或上游服務商系統異常時";

                // 600 系列 - 完成狀態
                case "600": 
                    return "結帳完成 - 視為付款完成，此狀態為上游服務商確認訂單後的狀態，表示該筆訂單會撥款";

                // A 系列 - 特殊狀態
                case "A0001": 
                    return "交易待確認 - MYPAY LINK與金流服務商發生連線異常，待查詢後確認結果";
                case "A0002": 
                    return "放棄交易 - 畫面導向MYPAY LINK後，消費者即放棄該筆交易，該筆交易視同交易失敗，為最終結果";

                // B 系列 - 執行狀態
                case "B200": 
                    return "執行成功 - 處理成功執行";
                case "B500": 
                    return "執行失敗 - 處理時，資料異常不予以處理";

                default: 
                    return $"未知狀態碼：{prc} - 請參考高鋸金流官方文檔或聯繫客服";
            }
        }

        /// <summary>
        /// 解析交易完成時間
        /// </summary>
        private DateTime ParseFinishTime(string finishtime)
        {
            if (string.IsNullOrWhiteSpace(finishtime) || finishtime.Length != 14) return DateTime.Now;
            try
            {
                int year = int.Parse(finishtime.Substring(0, 4));
                int month = int.Parse(finishtime.Substring(4, 2));
                int day = int.Parse(finishtime.Substring(6, 2));
                int hour = int.Parse(finishtime.Substring(8, 2));
                int minute = int.Parse(finishtime.Substring(10, 2));
                int second = int.Parse(finishtime.Substring(12, 2));
                return new DateTime(year, month, day, hour, minute, second);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ParseFinishTime:解析時間失敗 - FinishTime: {finishtime}");
                return DateTime.Now;
            }
        }

        /// <summary>
        /// 取得付款方式名稱（依據高鋸金流規格 附錄一：PFN（支付工具）參數表）
        /// </summary>
        private string GetPaymentMethodName(string pfn)
        {
            if (string.IsNullOrWhiteSpace(pfn)) return "未知支付工具";
            
            // 將 pfn 轉為大寫以便比對
            string pfnUpper = pfn.ToUpper();
            
            switch (pfnUpper)
            {
                // 0 - 全部支付工具
                case "0":
                case "ALL": 
                    return "全部支付工具";

                // 1 - 信用卡
                case "1":
                case "CREDITCARD": 
                    return "信用卡";

                // 2 - WebATM
                case "2":
                case "WEBATM": 
                    return "WebATM";

                // 3 - 超商代碼
                case "3":
                case "CSTORECODE": 
                    return "超商代碼";

                // 4 - 超商條碼
                case "4":
                case "CSTOREBAR": 
                    return "超商條碼";

                // 5 - 貨到付款
                case "5":
                case "COD": 
                    return "貨到付款";

                // 6 - 虛擬帳號
                case "6":
                case "E_COLLECTION": 
                    return "虛擬帳號";

                // 8 - 分期付款
                case "8":
                case "CREDITCARD_INSTALLMENT": 
                    return "信用卡分期";

                // 10 - 支付寶
                case "10":
                case "ALIPAY": 
                    return "支付寶";

                // 11 - 財付通
                case "11":
                case "TENPAY": 
                    return "財付通";

                // 12 - 銀聯
                case "12":
                case "UNIONPAY": 
                    return "銀聯";

                // 13 - 微信支付
                case "13":
                case "WECHAT": 
                    return "微信支付";

                // 14 - ezPay電子錢包
                case "14":
                case "EZPAY": 
                    return "ezPay電子錢包";

                // 15 - LINE Pay
                case "15":
                case "LINEPAYON": 
                    return "LINE Pay";

                // 16 - 玉山Wallet
                case "16":
                case "ESUNWALLET": 
                    return "玉山Wallet";

                // 17 - Taiwan Pay
                case "17":
                case "TAIWANPAY": 
                    return "Taiwan Pay";

                // 18 - 街口支付(舊)
                case "18":
                case "JKOPAY": 
                    return "街口支付";

                // 19 - 無卡分期
                case "19":
                case "BNPL": 
                    return "無卡分期";

                // 20 - Apple Pay
                case "20":
                case "APPLEPAY": 
                    return "Apple Pay";

                // 21 - Google Pay
                case "21":
                case "GOOGLEPAY": 
                    return "Google Pay";

                // 22 - Samsung Pay
                case "22":
                case "SAMSUNGPAY": 
                    return "Samsung Pay";

                // 23 - 定期定額
                case "23":
                case "CREDITCARD_REGULAR": 
                    return "信用卡定期定額";

                // 24 - 信用卡紅利
                case "24":
                case "C_REDEEM": 
                    return "信用卡紅利";

                // 25 - 定期分期
                case "25":
                case "CREDITCARD_INSTALLMENT_REGULAR": 
                    return "信用卡定期分期";

                // 26 - 悠遊付
                case "26":
                case "EASYWALLETON": 
                    return "悠遊付";

                // 27 - Pi 拍錢包
                case "27":
                case "PION": 
                    return "Pi 拍錢包";

                // 28 - 全盈+PAY
                case "28":
                case "PAYNOW": 
                    return "全盈+PAY";

                // 29 - AFTEE先享後付
                case "29":
                case "AFTEE": 
                    return "AFTEE先享後付";

                // 30 - 7-11取貨付款
                case "30":
                case "C711": 
                    return "7-11取貨付款";

                // 31 - 街口支付
                case "31":
                case "JKOON": 
                    return "街口支付";

                // 32 - 橘子支付
                case "32":
                case "GASHPAY": 
                    return "橘子支付";

                // 33 - 國泰KOKO
                case "33":
                case "KOKO": 
                    return "國泰KOKO";

                // 34 - icash Pay
                case "34":
                case "ICASHPAY": 
                    return "icash Pay";

                // 35 - 台新PAY
                case "35":
                case "TSPAY": 
                    return "台新PAY";

                // 36 - 全家取貨付款
                case "36":
                case "CFAMILY": 
                    return "全家取貨付款";

                // 37 - 萊爾富取貨付款
                case "37":
                case "CHILIFE": 
                    return "萊爾富取貨付款";

                // 38 - OK超商取貨付款
                case "38":
                case "COK": 
                    return "OK超商取貨付款";

                // 39 - 全支付
                case "39":
                case "PXPAY": 
                    return "全支付";

                // 40 - 銀行APP
                case "40":
                case "BANKAPP": 
                    return "銀行APP";

                // 41 - 悠遊卡
                case "41":
                case "EASYCARD": 
                    return "悠遊卡";

                // 42 - 一卡通
                case "42":
                case "IPASS": 
                    return "一卡通";

                // 43 - 信用卡快速結帳
                case "43":
                case "CREDITCARD_FAST": 
                    return "信用卡快速結帳";

                default: 
                    return $"支付工具 {pfn}";
            }
        }

        /// <summary>
        /// 判斷收費單類型
        /// </summary>
        private FeeType DetermineFeeType(ToolUtilityClass utility, Entity feeEntity)
        {
            try
            {
                var courseId = utility.GetEntityLookupAttribute(feeEntity, "new_course_id");
                if (courseId != Guid.Empty)
                {
                    _logger.LogInformation($"DetermineFeeType: 有課程 ID => 課程繳費");
                    return FeeType.Course;
                }

                string feeName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? string.Empty;
                if (feeName.Contains("課程") || feeName.Contains("報名") || feeName.Contains("學費") ||
                    feeName.Contains("培訓") || feeName.Contains("研習"))
                {
                    _logger.LogInformation($"DetermineFeeType: 名稱含課程關鍵字 => 課程繳費");
                    return FeeType.Course;
                }

                string courseName = utility.GetEntityStringAttribute(feeEntity, "new_course_name");
                if (!string.IsNullOrWhiteSpace(courseName))
                {
                    _logger.LogInformation($"DetermineFeeType: 有課程名稱欄位 => 課程繳費");
                    return FeeType.Course;
                }

                int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                if (categoryValue >= 100000000 && categoryValue <= 100000019)
                {
                    _logger.LogInformation($"DetermineFeeType: 類別屬奉獻 => 奉獻");
                    return FeeType.Dedication;
                }

                _logger.LogInformation($"DetermineFeeType: 預設 => 奉獻");
                return FeeType.Dedication;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DetermineFeeType例外，預設奉獻");
                return FeeType.Dedication;
            }
        }

        /// <summary>
        /// 取得課程名稱
        /// </summary>
        private string GetCourseName(ToolUtilityClass utility, Entity feeEntity)
        {
            try
            {
                var courseId = utility.GetEntityLookupAttribute(feeEntity, "new_course_id");
                if (courseId != Guid.Empty)
                {
                    var courseEntity = utility.RetrieveEntity("new_course", courseId);
                    if (courseEntity != null)
                    {
                        var name = utility.GetEntityStringAttribute(courseEntity, "new_name");
                        if (!string.IsNullOrWhiteSpace(name)) return name;
                    }
                }

                var courseNameField = utility.GetEntityStringAttribute(feeEntity, "new_course_name");
                if (!string.IsNullOrWhiteSpace(courseNameField)) return courseNameField;

                return utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "課程";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCourseName例外");
                return "課程";
            }
        }

        /// <summary>
        /// 取得奉獻類別名稱
        /// </summary>
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

        #endregion

    }
}