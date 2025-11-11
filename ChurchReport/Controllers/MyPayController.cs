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
using System.Collections.Generic;
using System.Linq;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 金流 PayPage 回傳處理控制器
    /// 負責處理高鋸金流 (MyPay) 的各種回傳通知，包括：
    /// - 接收金流回傳資料 (MyPayReturn)
    /// - 顯示成功結果頁面 (success)
    /// - 顯示失敗結果頁面 (failure)
    /// 所有處理邏輯都已整理為清晰的區塊，並補充詳細說明註解。
    /// </summary>
    [Route("api/[controller]")]
    public class MyPayController : Controller
    {
        #region 常數定義

        /// <summary>
        /// LINE 推播的存取權杖，用於發送通知訊息
        /// </summary>
        private const string LINE_CHANNEL_ACCESS_TOKEN = @"OMjL23DpFRDgphgN7JdzA7uCpv1wb4hXtsGh4FzxP8tHzeMyYOr/ry3BBqaRNJpVUhR6wPHLN4Wa4QiG5i3P5T/Y07swP5OjfCz9DKwTYC7T4mPb8x54pwtcqK1lIdgNm6skdZnu99fBsupEcbZLBAdB04t89/1O/w1cDnyilFU=";

        /// <summary>
        /// Dynamics 365 CRM 連線名稱，用於資料庫操作
        /// </summary>
        private const string DYNAMICS_CONNECTION_NAME = "DYNAMICS365";

        /// <summary>
        /// 付款狀態：信用卡已繳費，對應 CRM 中的 new_pay_status 欄位值
        /// </summary>
        private const int PAYMENT_STATUS_PAID = 100000001;

        /// <summary>
        /// 付款方式：信用卡，對應 CRM 中的 new_pay_way 欄位值
        /// </summary>
        private const int PAYMENT_METHOD_CREDIT_CARD = 100000001;

        #endregion

        #region 私有欄位

        /// <summary>
        /// 日誌記錄器，用於記錄處理過程和錯誤資訊
        /// </summary>
        private readonly ILogger<MyPayController> _logger;

        #endregion

        #region 建構函式

        /// <summary>
        /// MyPayController 建構函式
        /// 注入日誌記錄器以便記錄處理過程
        /// </summary>
        /// <param name="logger">日誌記錄器實例</param>
        public MyPayController(ILogger<MyPayController> logger)
        {
            _logger = logger;
        }

        #endregion

        #region API: MyPay 回傳

        /// <summary>
        /// 金流伺服器回呼端點。處理後需回傳字串 "8888" 代表已接收。
        /// 處理『交易完成回傳資訊』、『非即時交易回傳資訊』、『訂單確認回傳資訊`
        /// 此方法為金流平台的主要通知入口點，負責驗證資料、更新 CRM 並發送 LINE 通知。
        /// </summary>
        /// <param name="returnModel">金流回傳的資料模型</param>
        /// <returns>HTTP 回應，成功時返回 "8888"</returns>
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
                _logger.LogInformation("[MyPay回傳] 開始驗證欄位...");
                var validation = returnModel.ValidateAllFields();

                // 記錄驗證等級
                _logger.LogInformation($"[MyPay回傳] 驗證等級: {validation.Level}");

                // 記錄警告訊息（非致命錯誤）
                if (validation.Warnings != null && validation.Warnings.Any())
                {
                    _logger.LogInformation($"[MyPay回傳] 資料驗證警告 ({validation.Warnings.Count}): {string.Join(", ", validation.Warnings)}");
                }

                // 檢查驗證結果
                if (!validation.IsValid)
                {
                    _logger.LogWarning($"[MyPay回傳] 資料驗證失敗 ({validation.Errors.Count}): {string.Join(", ", validation.Errors)}");
                    // 仍回傳8888避免金流平台重送
                    return Ok("8888");
                }

                _logger.LogInformation("[MyPay回傳] 欄位驗證通過");

                // 4. 解析交易狀態
                bool isSuccess = IsSuccessfulPaymentStatus(returnModel.prc);
                _logger.LogInformation($"[MyPay回傳] 交易狀態判定: PRC={returnModel.prc}, IsSuccess={isSuccess}");

                // 5. 查詢收費單
                utility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
                Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_order_number", returnModel.order_id);

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
        /// 根據奉獻類型和付款資訊生成完整的成功通知訊息
        /// </summary>
        /// <param name="fullName">會友全名</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號</param>
        /// <param name="amount">付款金額</param>
        /// <param name="dedicationCategory">奉獻類別</param>
        /// <param name="paymentTime">付款時間</param>
        /// <returns>格式化的 LINE 訊息字串</returns>
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
        /// 根據奉獻類型和失敗原因生成完整的失敗通知訊息
        /// </summary>
        /// <param name="fullName">會友全名</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號</param>
        /// <param name="amount">應付金額</param>
        /// <param name="dedicationCategory">奉獻類別</param>
        /// <param name="paymentTime">嘗試時間</param>
        /// <param name="statusMessage">失敗原因訊息</param>
        /// <returns>格式化的 LINE 訊息字串</returns>
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
        /// 根據課程資訊和付款細節生成完整的成功通知訊息
        /// </summary>
        /// <param name="fullName">會友全名</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號</param>
        /// <param name="amount">繳費金額</param>
        /// <param name="courseName">課程名稱</param>
        /// <param name="courseSchedule">上課時間</param>
        /// <param name="courseLocation">上課地點</param>
        /// <param name="paymentTime">付款時間</param>
        /// <returns>格式化的 LINE 訊息字串</returns>
        private string BuildCoursePaymentSuccessMessage(string fullName, string orderId, string transactionId, decimal amount, string courseName, string courseSchedule, string courseLocation, DateTime paymentTime)
        {
            var msg = $"【金流付款成功通知】{Environment.NewLine}{Environment.NewLine}" +
                      $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}" +
                      $"您的課程繳費已成功完成！{Environment.NewLine}{Environment.NewLine}" +
                      $"課程資訊：{Environment.NewLine}" +
                      $"姓名：{fullName}{Environment.NewLine}" +
                      $"課程名稱：{courseName}{Environment.NewLine}";

            if (!string.IsNullOrWhiteSpace(courseSchedule))
                msg += $"上課時間：{courseSchedule}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(courseLocation))
                msg += $"上課地點：{courseLocation}{Environment.NewLine}";

            msg += $"{Environment.NewLine}付款資訊：{Environment.NewLine}" +
                   $"訂單編號：{orderId}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";
            msg += $"繳費金額：NT$ {amount:N0}{Environment.NewLine}" +
                   $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}" +
                   $"付款方式：信用卡{Environment.NewLine}{Environment.NewLine}期待在課程中與您相見！";
            return msg;
        }

        /// <summary>
        /// 建立課程繳費失敗訊息
        /// 根據課程資訊和失敗原因生成完整的失敗通知訊息
        /// </summary>
        /// <param name="fullName">會友全名</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號</param>
        /// <param name="amount">應繳金額</param>
        /// <param name="courseName">課程名稱</param>
        /// <param name="courseSchedule">上課時間</param>
        /// <param name="courseLocation">上課地點</param>
        /// <param name="paymentTime">嘗試時間</param>
        /// <param name="statusMessage">失敗原因訊息</param>
        /// <returns>格式化的 LINE 訊息字串</returns>
        private string BuildCoursePaymentFailureMessage(string fullName, string orderId, string transactionId, decimal amount, string courseName, string courseSchedule, string courseLocation, DateTime paymentTime, string statusMessage)
        {
            var msg = $"【金流付款失敗通知】{Environment.NewLine}{Environment.NewLine}" +
                      $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}" +
                      $"很抱歉，您的課程繳費未能完成。{Environment.NewLine}{Environment.NewLine}" +
                      $"失敗原因：{statusMessage}{Environment.NewLine}{Environment.NewLine}" +
                      $"課程資訊：{Environment.NewLine}" +
                      $"姓名：{fullName}{Environment.NewLine}" +
                      $"課程名稱：{courseName}{Environment.NewLine}";

            if (!string.IsNullOrWhiteSpace(courseSchedule))
                msg += $"上課時間：{courseSchedule}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(courseLocation))
                msg += $"上課地點：{courseLocation}{Environment.NewLine}";

            msg += $"{Environment.NewLine}付款資訊：{Environment.NewLine}" +
                   $"訂單編號：{orderId}{Environment.NewLine}";
            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";
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
        /// 適用於非奉獻、非課程的一般繳費項目
        /// </summary>
        /// <param name="fullName">會友全名</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號</param>
        /// <param name="amount">付款金額</param>
        /// <param name="itemName">項目名稱</param>
        /// <param name="paymentTime">付款時間</param>
        /// <returns>格式化的 LINE 訊息字串</returns>
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
        /// 適用於非奉獻、非課程的一般繳費項目失敗通知
        /// </summary>
        /// <param name="fullName">會友全名</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號</param>
        /// <param name="amount">應付金額</param>
        /// <param name="itemName">項目名稱</param>
        /// <param name="paymentTime">嘗試時間</param>
        /// <param name="statusMessage">失敗原因訊息</param>
        /// <returns>格式化的 LINE 訊息字串</returns>
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
        /// 使用 LINE Messaging API 發送推播訊息給指定用戶
        /// </summary>
        /// <param name="lineId">接收者的 LINE ID</param>
        /// <param name="message">要發送的訊息內容</param>
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

        #region 收費單類型與狀態判斷/輔助

        private enum FeeType { Dedication, Course, Other }

        private string GetPaymentStatusMessage(string prc)
        {
            if (string.IsNullOrWhiteSpace(prc)) return "付款狀態未知";
            switch (prc)
            {
                case "100": return "資料錯誤 - MYPAYLINK收到資料，但是格式或資料錯誤";
                case "200": return "資料正確 - MYPAYLINK收到正確資料，會接續下一步交易";
                case "220": return "取消成功";
                case "230": return "退款成功";
                case "250": return "付款成功";
                case "260": return "交易成功，尚未付款完成 (超商代碼)";
                case "265": return "訂單綁定";
                case "270": return "交易成功，尚未付款完成 (虛擬帳號)";
                case "275": return "交易成功，待審核 (無卡分期)";
                case "280": return "交易成功，尚未付款完成 (儲值/WEBATM)";
                case "290": return "交易成功但資訊不符";
                case "300": return "交易失敗";
                case "380": return "逾期交易";
                case "400": return "系統錯誤訊息";
                case "600": return "結帳完成";
                case "A0001": return "交易待確認";
                case "A0002": return "放棄交易";
                case "B200": return "執行成功";
                case "B500": return "執行失敗";
                default: return $"未知狀態碼：{prc}";
            }
        }

        private DateTime ParseFinishTime(string finishtime)
        {
            if (string.IsNullOrWhiteSpace(finishtime) || finishtime.Length != 14) return DateTime.Now;
            try
            {
                return new DateTime(
                    int.Parse(finishtime.Substring(0, 4)),
                    int.Parse(finishtime.Substring(4, 2)),
                    int.Parse(finishtime.Substring(6, 2)),
                    int.Parse(finishtime.Substring(8, 2)),
                    int.Parse(finishtime.Substring(10, 2)),
                    int.Parse(finishtime.Substring(12, 2)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ParseFinishTime:解析時間失敗 - FinishTime: {finishtime}");
                return DateTime.Now;
            }
        }

        private string GetPaymentMethodName(string pfn)
        {
            if (string.IsNullOrWhiteSpace(pfn)) return "未知支付工具";
            switch (pfn.ToUpper())
            {
                case "1": case "CREDITCARD": return "信用卡";
                case "2": case "WEBATM": return "WebATM";
                case "3": case "CSTORECODE": return "超商代碼";
                default: return $"支付工具 {pfn}";
            }
        }

        private FeeType DetermineFeeType(ToolUtilityClass utility, Entity feeEntity)
        {
            try
            {
                var courseId = utility.GetEntityLookupAttribute(feeEntity, "new_course_id");
                if (courseId != Guid.Empty) return FeeType.Course;
                string feeName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? string.Empty;
                if (feeName.Contains("課程") || feeName.Contains("報名") || feeName.Contains("學費") || feeName.Contains("培訓") || feeName.Contains("研習")) return FeeType.Course;
                string courseName = utility.GetEntityStringAttribute(feeEntity, "new_course_name");
                if (!string.IsNullOrWhiteSpace(courseName)) return FeeType.Course;
                int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                if (categoryValue >= 100000000 && categoryValue <= 100000019) return FeeType.Dedication;
                return FeeType.Dedication; // 預設
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DetermineFeeType例外，預設奉獻");
                return FeeType.Dedication;
            }
        }

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

        #region 收費單更新與 LINE 通知

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

        private bool IsSuccessfulPaymentStatus(string prc)
        {
            if (string.IsNullOrWhiteSpace(prc)) return false;
            return prc == "250" || prc == "290" || prc == "600";
        }

        private void UpdateFeeEntityWithMyPayReturn(ToolUtilityClass toolUtility, Entity feeEntity, MyPayReturnModel model, bool isSuccess)
        {
            try
            {
                DateTime paymentTime = ParseFinishTime(model.finishtime);
                if (isSuccess)
                {
                    var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                    toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PAYMENT_STATUS_PAID);
                    toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);
                    toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));
                    toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", paymentTime);
                    toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PAYMENT_METHOD_CREDIT_CARD);
                }
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
                if (!string.IsNullOrEmpty(model.installment)) newDescription += $"分期資訊: {model.installment}" + Environment.NewLine;
                if (!string.IsNullOrEmpty(model.redeem)) newDescription += $"紅利資訊: {model.redeem}" + Environment.NewLine;
                if (!string.IsNullOrEmpty(model.supplier_name)) newDescription += "====== 服務商資訊 ======" + Environment.NewLine + $"服務商: {model.supplier_name}" + Environment.NewLine + $"服務商代碼: {model.supplier_code}" + Environment.NewLine;
                if (!string.IsNullOrEmpty(model.payment_name)) newDescription += "====== 定期定額資訊 ======" + Environment.NewLine + $"扣款名稱: {model.payment_name}" + Environment.NewLine + $"期數: {model.nois}" + Environment.NewLine + $"群組編號: {model.group_id}" + Environment.NewLine;
                if (!string.IsNullOrEmpty(model.bank_id)) newDescription += "====== 虛擬帳號資訊 ======" + Environment.NewLine + $"銀行代碼: {model.bank_id}" + Environment.NewLine + $"有效期限: {model.expired_date}" + Environment.NewLine;
                if (!string.IsNullOrEmpty(model.echo_0) || !string.IsNullOrEmpty(model.echo_1)) newDescription += "====== 自訂參數 ======" + Environment.NewLine + $"echo_0: {model.echo_0}" + Environment.NewLine + $"echo_1: {model.echo_1}" + Environment.NewLine + $"echo_2: {model.echo_2}" + Environment.NewLine + $"echo_3: {model.echo_3}" + Environment.NewLine + $"echo_4: {model.echo_4}" + Environment.NewLine;
                newDescription += "====== 舊版相容欄位 ======" + Environment.NewLine + $"state: {model.state}" + Environment.NewLine + $"msg: {model.msg}" + Environment.NewLine + $"transaction_id: {model.transaction_id}" + Environment.NewLine + $"store_uid: {model.store_uid}" + Environment.NewLine;
                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);
                _logger.LogInformation($"[MyPay回傳] 收費單欄位已更新 - FeeId: {feeEntity.Id}, OrderId: {model.order_id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 更新收費單失敗 - OrderId: {model.order_id}");
                throw;
            }
        }

        private void SendLineNotificationByType(ToolUtilityClass utility, Entity feeEntity, MyPayReturnModel model, string fullName, FeeType feeType, Entity contactEntity)
        {
            try
            {
                if (contactEntity == null) { _logger.LogWarning("[MyPay回傳] ContactEntity為空，無法發送LINE通知"); return; }
                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId)) { _logger.LogWarning("[MyPay回傳] LINE ID為空"); return; }
                decimal amount = 0m;
                if (!string.IsNullOrEmpty(model.actual_cost) && decimal.TryParse(model.actual_cost, out var actualCost)) amount = actualCost;
                else if (!string.IsNullOrEmpty(model.cost) && decimal.TryParse(model.cost, out var cost)) amount = cost;
                DateTime paymentTime = ParseFinishTime(model.finishtime);
                string message;
                if (feeType == FeeType.Dedication)
                {
                    int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                    string dedicationCategory = GetDedicationCategoryName(categoryValue);
                    message = BuildDedicationSuccessMessage(fullName, model.order_id, model.uid, amount, dedicationCategory, paymentTime);
                }
                else if (feeType == FeeType.Course)
                {
                    string courseName = GetCourseName(utility, feeEntity);
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? "";
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? "";
                    message = BuildCoursePaymentSuccessMessage(fullName, model.order_id, model.uid, amount, courseName, courseSchedule, courseLocation, paymentTime);
                }
                else
                {
                    string itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";
                    message = BuildGeneralPaymentSuccessMessage(fullName, model.order_id, model.uid, amount, itemName, paymentTime);
                }
                SendLineMessage(lineId, message);
                _logger.LogInformation($"[MyPay回傳] LINE通知已發送 - LineId: {lineId}, OrderId: {model.order_id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 發送LINE通知失敗 - OrderId: {model.order_id}");
            }
        }

        private void SendLineFailureNotificationByType(ToolUtilityClass utility, Entity feeEntity, MyPayReturnModel model, string fullName, FeeType feeType, Entity contactEntity)
        {
            try
            {
                if (contactEntity == null) { _logger.LogWarning("[MyPay回傳] ContactEntity為空，無法發送LINE失敗通知"); return; }
                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId)) { _logger.LogWarning("[MyPay回傳] LINE ID為空，無法發送失敗通知"); return; }
                decimal amount = 0m;
                var shouldPayMoney = utility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                if (shouldPayMoney != null && shouldPayMoney.Value > 0) amount = shouldPayMoney.Value;
                else if (!string.IsNullOrWhiteSpace(model.actual_cost) && decimal.TryParse(model.actual_cost, out var actualCost)) amount = actualCost;
                else if (!string.IsNullOrWhiteSpace(model.cost) && decimal.TryParse(model.cost, out var cost)) amount = cost;
                DateTime paymentTime = ParseFinishTime(model.finishtime);
                string statusMessage = GetPaymentStatusMessage(model.prc);
                string message;
                if (feeType == FeeType.Dedication)
                {
                    int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                    string dedicationCategory = GetDedicationCategoryName(categoryValue);
                    message = BuildDedicationFailureMessage(fullName, model.order_id, model.uid, amount, dedicationCategory, paymentTime, statusMessage);
                }
                else if (feeType == FeeType.Course)
                {
                    string courseName = GetCourseName(utility, feeEntity);
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? "";
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? "";
                    message = BuildCoursePaymentFailureMessage(fullName, model.order_id, model.uid, amount, courseName, courseSchedule, courseLocation, paymentTime, statusMessage);
                }
                else
                {
                    string itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";
                    message = BuildGeneralPaymentFailureMessage(fullName, model.order_id, model.uid, amount, itemName, paymentTime, statusMessage);
                }
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
    }

    /// <summary>
    /// MyPayReturnModel 擴充方法保持原功能。
    /// </summary>
    public static class MyPayReturnModelExtensions
    {
        // 保留原有 ValidateAllFields 與 ValidateAndProcess（未改動）。
        public static Models.ValidationResult ValidateAllFields(this MyPayReturnModel model)
        {
            var result = new Models.ValidationResult { IsValid = true };
            if (string.IsNullOrEmpty(model.uid)) { result.Errors.Add("uid (交易流水號) 是必要欄位"); result.IsValid = false; }
            if (string.IsNullOrEmpty(model.key)) { result.Errors.Add("key (交易驗證碼) 是必要欄位"); result.IsValid = false; }
            if (string.IsNullOrEmpty(model.prc)) { result.Errors.Add("prc (交易回傳碼) 是必要欄位"); result.IsValid = false; }
            if (string.IsNullOrEmpty(model.order_id)) { result.Errors.Add("order_id (訂單編號) 是必要欄位"); result.IsValid = false; }
            if (!string.IsNullOrEmpty(model.prc) && IsImmediateTransaction(model.prc))
            {
                if (string.IsNullOrEmpty(model.finishtime)) result.Errors.Add("?? finishtime (交易完成時間) 建議填寫");
                if (string.IsNullOrEmpty(model.cost) && string.IsNullOrEmpty(model.actual_cost)) { result.Errors.Add("cost 或 actual_cost 至少需要一個"); result.IsValid = false; }
                if (string.IsNullOrEmpty(model.pfn)) result.Errors.Add("?? pfn (付費方法) 建議填寫");
            }
            if (!string.IsNullOrEmpty(model.result_content_type) && (model.result_content_type == "E_COLLECTION" || model.result_content_type == "CSTORECODE"))
            {
                if (string.IsNullOrEmpty(model.bank_id)) result.Errors.Add("?? bank_id 虛擬帳號交易建議填寫");
                if (string.IsNullOrEmpty(model.expired_date)) result.Errors.Add("?? expired_date 有效期限建議填寫");
            }
            if (string.IsNullOrEmpty(model.state)) result.Errors.Add("?? state 舊版相容欄位 建議填寫");
            if (string.IsNullOrEmpty(model.transaction_id)) result.Errors.Add("?? transaction_id 舊版相容欄位 建議填寫");
            if (string.IsNullOrEmpty(model.msg)) result.Errors.Add("?? msg 舊版相容欄位 建議填寫");
            if (string.IsNullOrEmpty(model.store_uid)) result.Errors.Add("?? store_uid 舊版相容欄位 建議填寫");
            if (string.IsNullOrEmpty(model.hash)) result.Errors.Add("?? hash 舊版相容欄位 建議填寫");
            return result;
        }

        private static bool IsImmediateTransaction(string prc) => prc == "250" || prc == "290" || prc == "600";

        public static (Models.ValidationResult Validation, MyPayProcessingResult Processing) ValidateAndProcess(this MyPayReturnModel model)
        {
            var validation = model.ValidateAllFields();
            var processing = model.ProcessAllReturnFields();
            return (validation, processing);
        }
    }
}