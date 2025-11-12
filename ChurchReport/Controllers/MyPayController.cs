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
        /// ========================================
        /// 金流伺服器回呼端點 (Server-to-Server Callback)
        /// ========================================
        /// 
        /// 【端點資訊】
        /// - HTTP Method: POST
        /// - Route: /api/MyPay/MyPayNotify
        /// - Content-Type: application/x-www-form-urlencoded
        /// 
        /// 【處理流程】
        /// 1. 接收金流平台回傳的交易資料
        /// 2. 驗證資料完整性與有效性
        /// 3. 判斷交易成功或失敗
        /// 4. 查詢並更新 CRM 收費單狀態
        /// 5. 發送 LINE 通知給使用者
        /// 6. 回傳 "8888" 確認接收（避免金流平台重送）
        /// 
        /// 【回傳資訊類型】
        /// - 交易完成回傳資訊（即時交易，如信用卡）
        /// - 非即時交易回傳資訊（虛擬帳號、超商代碼）
        /// - 訂單確認回傳資訊（定期定額、分期付款）
        /// 
        /// 【錯誤處理原則】
        /// - 任何錯誤都回傳 "8888" 避免金流平台持續重送
        /// - 所有異常都記錄到日誌供後續追蹤
        /// - LINE 通知失敗不影響主流程繼續執行
        /// 
        /// 【參考文檔】
        /// - 高鋸金流官方規格文檔
        /// - 附錄一：PFN（支付工具）參數表
        /// - 附錄二：PRC（交易回傳碼）定義
        /// 
        /// </summary>
        /// <param name="returnModel">金流回傳的資料模型，包含完整交易資訊</param>
        /// <returns>HTTP 200 OK，內容為 "8888" 表示已成功接收並處理</returns>
        [HttpPost("MyPayNotify")]
        public async Task<IActionResult> PaymentNotify([FromForm] MyPayReturnModel returnModel)
        {
            // ========================================
            // 步驟 0：記錄初始接收資訊
            // ========================================
            _logger.LogInformation($"[MyPay回傳] 收到金流回傳，OrderID: {returnModel?.order_id}, UID: {returnModel?.uid}, PRC: {returnModel?.prc}");

            ToolUtilityClass utility = null;

            try
            {
                // ========================================
                // 步驟 1：基本檢查 - 驗證回傳物件存在
                // ========================================
                if (returnModel == null)
                {
                    _logger.LogWarning("[MyPay回傳] 回傳資料為空");
                    return BadRequest("回傳資料為空");
                }

                // ========================================
                // 步驟 2：記錄完整回傳資訊（用於除錯）
                // ========================================
                LogFullReturnData(returnModel);

                // ========================================
                // 步驟 3：驗證必要欄位完整性
                // ========================================
                // 根據高鋸金流官方規格驗證必要欄位
                // - uid: 交易流水號（必要）
                // - key: 交易驗證碼（必要）
                // - prc: 交易回傳碼（必要）
                // - order_id: 訂單編號（必要）
                _logger.LogInformation("[MyPay回傳] 開始驗證欄位...");
                var validation = returnModel.ValidateAllFields();

                // 記錄驗證結果等級
                _logger.LogInformation($"[MyPay回傳] 驗證等級: {validation.Level}");

                // 記錄警告訊息（非致命錯誤，但需要注意）
                if (validation.Warnings != null && validation.Warnings.Any())
                {
                    _logger.LogInformation($"[MyPay回傳] 資料驗證警告 ({validation.Warnings.Count}): {string.Join(", ", validation.Warnings)}");
                }

                // 檢查是否有致命錯誤
                if (!validation.IsValid)
                {
                    _logger.LogWarning($"[MyPay回傳] 資料驗證失敗 ({validation.Errors.Count}): {string.Join(", ", validation.Errors)}");
                    // 即使驗證失敗，仍回傳 8888 避免金流平台持續重送
                    return Ok("8888");
                }

                _logger.LogInformation("[MyPay回傳] 欄位驗證通過");

                // ========================================
                // 步驟 4：解析交易狀態（成功/失敗）
                // ========================================
                // 根據 PRC 代碼判斷交易是否成功
                // 成功代碼：250（付款成功）、290（交易成功但資訊不符）、600（結帳完成）
                bool isSuccess = IsSuccessfulPaymentStatus(returnModel.prc);
                _logger.LogInformation($"[MyPay回傳] 交易狀態判定: PRC={returnModel.prc}, IsSuccess={isSuccess}");

                // ========================================
                // 步驟 5：查詢對應的 CRM 收費單
                // ========================================
                utility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
                
                // 使用訂單編號查詢收費單
                // 注意：高鋸金流使用 new_q_pay_order_number 欄位
                Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_order_number", returnModel.order_id);
                
                // 如果找不到收費單，記錄警告並結束處理
                if (feeEntity == null)
                {
                    _logger.LogWarning($"[MyPay回傳] 找不到對應收費單 - OrderId: {returnModel.order_id}");
                    return Ok("8888"); // 仍回傳成功避免重送
                }

                _logger.LogInformation($"[MyPay回傳] 找到收費單 - FeeId: {feeEntity.Id}");

                // ========================================
                // 步驟 6：判斷收費單類型
                // ========================================
                // 根據收費單欄位判斷是奉獻、課程繳費或其他類型
                // 不同類型會發送不同格式的 LINE 通知
                FeeType feeType = DetermineFeeType(utility, feeEntity);
                _logger.LogInformation($"[MyPay回傳] 收費單類型: {feeType}");

                // ========================================
                // 步驟 7：取得連絡人資訊
                // ========================================
                // 從收費單關聯的連絡人取得 LINE ID 用於後續通知
                var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                Entity contactEntity = null;
                string fullName = "會友"; // 預設名稱
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

                // ========================================
                // 步驟 8：更新 CRM 收費單狀態與資訊
                // ========================================
                // 根據交易結果更新收費單
                // - 成功：更新付款狀態、實付金額、付款日期等
                // - 失敗：記錄失敗原因到描述欄位
                UpdateFeeEntityWithMyPayReturn(utility, feeEntity, returnModel, isSuccess);
                utility.UpdateEntity(ref feeEntity);
                _logger.LogInformation($"[MyPay回傳] 收費單已更新 - FeeId: {feeEntity.Id}");

                // ========================================
                // 步驟 9：發送 LINE 通知給使用者
                // ========================================
                // 無論成功或失敗都發送通知
                // 根據收費單類型（奉獻/課程/其他）發送不同格式的訊息
                if (!string.IsNullOrWhiteSpace(lineId))
                {
                    try
                    {
                        if (isSuccess)
                        {
                            // 發送成功通知
                            SendLineNotificationByType(utility, feeEntity, returnModel, fullName, feeType, contactEntity);
                            _logger.LogInformation($"[MyPay回傳] LINE成功通知已發送 - OrderId: {returnModel.order_id}");
                        }
                        else
                        {
                            // 發送失敗通知
                            SendLineFailureNotificationByType(utility, feeEntity, returnModel, fullName, feeType, contactEntity);
                            _logger.LogInformation($"[MyPay回傳] LINE失敗通知已發送 - OrderId: {returnModel.order_id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // LINE 通知失敗不影響主流程
                        _logger.LogError(ex, $"[MyPay回傳] 發送LINE通知失敗 - OrderId: {returnModel.order_id}");
                    }
                }
                else
                {
                    _logger.LogWarning($"[MyPay回傳] LINE ID為空，無法發送通知 - OrderId: {returnModel.order_id}");
                }

                // ========================================
                // 步驟 10：回傳確認接收代碼 "8888"
                // ========================================
                // 高鋸金流規定必須回傳 "8888" 表示已成功接收
                // 否則金流平台會持續重送通知
                _logger.LogInformation($"[MyPay回傳] 處理完成 - OrderId: {returnModel.order_id}");
                return Ok("8888");
            }
            catch (Exception ex)
            {
                // ========================================
                // 異常處理：記錄錯誤但仍回傳成功
                // ========================================
                // 發生任何異常都回傳 "8888" 避免金流平台無限重送
                // 錯誤資訊會記錄到日誌供後續追蹤處理
                _logger.LogError(ex, $"[MyPay回傳] 處理異常 - OrderId: {returnModel?.order_id}");
                return Ok("8888");
            }
            finally
            {
                // ========================================
                // 資源清理：釋放資料庫連線
                // ========================================
                utility?.Dispose();
            }
        }

        #endregion

        #region LINE 訊息建立

        // ========================================================================================================
        // 【LINE 訊息建立區塊】
        // 
        // 本區塊負責生成各種類型的 LINE 通知訊息，包括：
        // 1. 奉獻類型訊息（成功/失敗）
        // 2. 課程繳費訊息（成功/失敗）
        // 3. 一般繳費訊息（成功/失敗）
        // 4. LINE 訊息發送功能
        //
        // 【設計原則】
        // - 訊息格式統一且易讀
        // - 包含完整交易資訊
        // - 根據不同類型客製化內容
        // - 失敗訊息提供明確的後續處理建議
        // ========================================================================================================

        #region 奉獻類型訊息建立

        /// <summary>
        /// ========================================
        /// 建立奉獻成功訊息
        /// ========================================
        /// 
        /// 【功能說明】
        /// 根據奉獻類型和付款資訊生成完整的成功通知訊息
        /// 
        /// 【訊息內容】
        /// - 感謝詞與問候語
        /// - 奉獻類別（十一奉獻、感恩奉獻等）
        /// - 訂單與交易編號
        /// - 付款金額與時間
        /// - 祝福語
        /// 
        /// 【使用時機】
        /// 當會友完成奉獻付款，且交易狀態為成功時發送
        /// 
        /// </summary>
        /// <param name="fullName">會友全名（用於個人化問候）</param>
        /// <param name="orderId">訂單編號（由系統產生的唯一識別碼）</param>
        /// <param name="transactionId">交易編號（金流平台回傳的交易流水號 uid）</param>
        /// <param name="amount">付款金額（已完成的實際付款金額）</param>
        /// <param name="dedicationCategory">奉獻類別名稱（例如：十一奉獻、感恩奉獻）</param>
        /// <param name="paymentTime">付款時間（交易完成的日期時間）</param>
        /// <returns>格式化的 LINE 訊息字串，可直接用於發送</returns>
        private string BuildDedicationSuccessMessage(
            string fullName, 
            string orderId, 
            string transactionId, 
            decimal amount, 
            string dedicationCategory, 
            DateTime paymentTime)
        {
            // 訊息標題
            var msg = $"【金流付款成功通知】{Environment.NewLine}{Environment.NewLine}";
            
            // 問候語與感謝詞
            msg += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            msg += $"您的奉獻已成功完成，感謝您的支持！{Environment.NewLine}{Environment.NewLine}";
            
            // 付款資訊區塊
            msg += $"付款資訊：{Environment.NewLine}";
            msg += $"姓名：{fullName}{Environment.NewLine}";
            msg += $"奉獻類別：{dedicationCategory}{Environment.NewLine}";
            msg += $"訂單編號：{orderId}{Environment.NewLine}";
            
            // 交易編號（選填，如有則顯示）
            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";
            
            // 金額與時間資訊
            msg += $"付款金額：NT$ {amount:N0}{Environment.NewLine}";
            msg += $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
            msg += $"付款方式：信用卡{Environment.NewLine}{Environment.NewLine}";
            
            // 祝福語
            msg += $"願上帝賜福與您！";
            
            return msg;
        }

        /// <summary>
        /// ========================================
        /// 建立奉獻失敗訊息
        /// ========================================
        /// 
        /// 【功能說明】
        /// 根據奉獻類型和失敗原因生成完整的失敗通知訊息
        /// 
        /// 【訊息內容】
        /// - 道歉與安慰
        /// - 失敗原因說明
        /// - 奉獻類別與訂單資訊
        /// - 應付金額（尚未完成的金額）
        /// - 後續處理建議
        /// 
        /// 【使用時機】
        /// 當會友奉獻付款失敗時發送，協助會友了解原因並提供解決方案
        /// 
        /// </summary>
        /// <param name="fullName">會友全名</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號（如果有的話）</param>
        /// <param name="amount">應付金額（原本應該支付的金額）</param>
        /// <param name="dedicationCategory">奉獻類別名稱</param>
        /// <param name="paymentTime">嘗試付款時間</param>
        /// <param name="statusMessage">失敗原因訊息（由系統解析 PRC 代碼而來）</param>
        /// <returns>格式化的 LINE 訊息字串</returns>
        private string BuildDedicationFailureMessage(
            string fullName, 
            string orderId, 
            string transactionId, 
            decimal amount, 
            string dedicationCategory, 
            DateTime paymentTime, 
            string statusMessage)
        {
            // 訊息標題
            var msg = $"【金流付款失敗通知】{Environment.NewLine}{Environment.NewLine}";
            
            // 問侯語與道歉
            msg += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            msg += $"很抱歉，您的奉獻付款未能完成。{Environment.NewLine}{Environment.NewLine}";
            
            // 失敗原因說明
            msg += $"失敗原因：{statusMessage}{Environment.NewLine}{Environment.NewLine}";
            
            // 付款資訊區塊
            msg += $"付款資訊：{Environment.NewLine}";
            msg += $"姓名：{fullName}{Environment.NewLine}";
            msg += $"奉獻類別：{dedicationCategory}{Environment.NewLine}";
            msg += $"訂單編號：{orderId}{Environment.NewLine}";
            
            // 交易編號（選填）
            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";
            
            // 應付金額與嘗試時間
            msg += $"應付金額：NT$ {amount:N0}{Environment.NewLine}";
            msg += $"嘗試時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}";
            
            // 後續處理建議
            msg += $"您可以：{Environment.NewLine}";
            msg += $"1. 重新嘗試付款{Environment.NewLine}";
            msg += $"2. 更換其他信用卡{Environment.NewLine}";
            msg += $"3. 聯繫教會辦公室尋求協助{Environment.NewLine}{Environment.NewLine}";
            
            // 結尾安慰語
            msg += $"如有任何問題，請隨時與我們聯繫。";
            
            return msg;
        }

        #endregion

        #region 課程繳費類型訊息建立

        /// <summary>
        /// ========================================
        /// 建立課程繳費成功訊息
        /// ========================================
        /// 
        /// 【功能說明】
        /// 根據課程資訊和付款細節生成完整的成功通知訊息
        /// 
        /// 【訊息內容】
        /// - 課程基本資訊（名稱、時間、地點）
        /// - 繳費成功確認
        /// - 付款金額與時間
        /// - 期待語
        /// 
        /// 【使用時機】
        /// 當會友完成課程報名繳費，且交易成功時發送
        /// 
        /// </summary>
        /// <param name="fullName">會友全名</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號</param>
        /// <param name="amount">繳費金額（實際支付的課程費用）</param>
        /// <param name="courseName">課程名稱（完整課程名稱）</param>
        /// <param name="courseSchedule">上課時間（課程時段說明）</param>
        /// <param name="courseLocation">上課地點（教室或場地位置）</param>
        /// <param name="paymentTime">付款時間</param>
        /// <returns>格式化的 LINE 訊息字串</returns>
        private string BuildCoursePaymentSuccessMessage(
            string fullName, 
            string orderId, 
            string transactionId, 
            decimal amount, 
            string courseName, 
            string courseSchedule, 
            string courseLocation, 
            DateTime paymentTime)
        {
            // 訊息標題
            var msg = $"【金流付款成功通知】{Environment.NewLine}{Environment.NewLine}";
            
            // 問候語與成功確認
            msg += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            msg += $"您的課程繳費已成功完成！{Environment.NewLine}{Environment.NewLine}";
            
            // 課程資訊區塊
            msg += $"課程資訊：{Environment.NewLine}";
            msg += $"姓名：{fullName}{Environment.NewLine}";
            msg += $"課程名稱：{courseName}{Environment.NewLine}";
            
            // 上課時間（選填）
            if (!string.IsNullOrWhiteSpace(courseSchedule)) 
                msg += $"上課時間：{courseSchedule}{Environment.NewLine}";
            
            // 上課地點（選填）
            if (!string.IsNullOrWhiteSpace(courseLocation)) 
                msg += $"上課地點：{courseLocation}{Environment.NewLine}";
            
            // 付款資訊區塊
            msg += $"{Environment.NewLine}付款資訊：{Environment.NewLine}";
            msg += $"訂單編號：{orderId}{Environment.NewLine}";
            
            // 交易編號（選填）
            if (!string.IsNullOrWhiteSpace(transactionId)) msg += $"交易編號：{transactionId}{Environment.NewLine}";
            
            // 金額與時間
            msg += $"繳費金額：NT$ {amount:N0}{Environment.NewLine}";
            msg += $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
            msg += $"付款方式：信用卡{Environment.NewLine}{Environment.NewLine}";
            
            // 期待語
            msg += $"期待在課程中與您相見！";
            
            return msg;
        }

        /// <summary>
        /// ========================================
        /// 建立課程繳費失敗訊息
        /// ========================================
        /// 
        /// 【功能說明】
        /// 根據課程資訊和失敗原因生成完整的失敗通知訊息
        /// 
        /// 【訊息內容】
        /// - 課程基本資訊
        /// - 失敗原因說明
        /// - 應繳金額資訊
        /// - 後續處理建議
        /// 
        /// 【使用時機】
        /// 當會友課程繳費失敗時發送
        /// 
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
        private string BuildCoursePaymentFailureMessage(
            string fullName, 
            string orderId, 
            string transactionId, 
            decimal amount, 
            string courseName, 
            string courseSchedule, 
            string courseLocation, 
            DateTime paymentTime, 
            string statusMessage)
        {
            // 訊息標題
            var msg = $"【金流付款失敗通知】{Environment.NewLine}{Environment.NewLine}";
            
            // 問候語與道歉
            msg += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            msg += $"很抱歉，您的課程繳費未能完成。{Environment.NewLine}{Environment.NewLine}";
            
            // 失敗原因
            msg += $"失敗原因：{statusMessage}{Environment.NewLine}{Environment.NewLine}";
            
            // 課程資訊區塊
            msg += $"課程資訊：{Environment.NewLine}";
            msg += $"姓名：{fullName}{Environment.NewLine}";
            msg += $"課程名稱：{courseName}{Environment.NewLine}";
            
            // 交易編號（選填）
            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";
            
            // 應繳金額與嘗試時間
            msg += $"應繳金額：NT$ {amount:N0}{Environment.NewLine}";
            msg += $"嘗試時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}";
            
            // 後續處理建議
            msg += $"您可以：{Environment.NewLine}";
            msg += $"1. 重新嘗試付款{Environment.NewLine}";
            msg += $"2. 更換其他信用卡{Environment.NewLine}";
            msg += $"3. 聯繫教會辦公室尋求協助{Environment.NewLine}{Environment.NewLine}";
            
            // 結尾語
            msg += $"如有任何問題，請隨時與我們聯繫。";
            
            return msg;
        }

        #endregion

        #region 一般繳費類型訊息建立

        /// <summary>
        /// ========================================
        /// 建立一般繳費成功訊息
        /// ========================================
        /// 
        /// 【功能說明】
        /// 適用於非奉獻、非課程的一般繳費項目
        /// 提供基本但完整的付款成功資訊
        /// 
        /// 【訊息內容】
        /// - 付款成功確認
        /// - 項目名稱
        /// - 訂單與交易編號
        /// - 付款金額與時間
        /// - 感謝語
        /// 
        /// 【使用時機】
        /// 當會友完成一般性繪費（如活動費用、其他雜費）時發送
        /// 
        /// </summary>
        /// <param name="fullName">會友全名</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號</param>
        /// <param name="amount">付款金額</param>
        /// <param name="itemName">項目名稱（繳費項目的說明）</param>
        /// <param name="paymentTime">付款時間</param>
        /// <returns>格式化的 LINE 訊息字串</returns>
        private string BuildGeneralPaymentSuccessMessage(
            string fullName, 
            string orderId, 
            string transactionId, 
            decimal amount, 
            string itemName, 
            DateTime paymentTime)
        {
            // 訊息標題
            var msg = $"【金流付款成功通知】{Environment.NewLine}{Environment.NewLine}";
            
            // 問候語與成功確認
            msg += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            msg += $"您的付款已成功完成！{Environment.NewLine}{Environment.NewLine}";
            
            // 付款資訊區塊
            msg += $"付款資訊：{Environment.NewLine}";
            msg += $"姓名：{fullName}{Environment.NewLine}";
            msg += $"項目：{itemName}{Environment.NewLine}";
            msg += $"訂單編號：{orderId}{Environment.NewLine}";
            
            // 交易編號（選填）
            if (!string.IsNullOrWhiteSpace(transactionId)) msg += $"交易編號：{transactionId}{Environment.NewLine}";
            
            // 金額與時間
            msg += $"付款金額：NT$ {amount:N0}{Environment.NewLine}";
            msg += $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
            msg += $"付款方式：信用卡{Environment.NewLine}{Environment.NewLine}";
            
            // 感謝語
            msg += $"感謝您的支持！";
            
            return msg;
        }

        /// <summary>
        /// ========================================
        /// 建立一般繳費失敗訊息
        /// ========================================
        /// 
        /// 【功能說明】
        /// 適用於非奉獻、非課程的一般繳費項目失敗通知
        /// 
        /// 【訊息內容】
        /// - 付款失敗說明
        /// - 失敗原因
        /// - 項目與訂單資訊
        /// - 應付金額
        /// - 後續處理建議
        /// 
        /// 【使用時機】
        /// 當會友一般性繪費失敗時發送
        /// 
        /// </summary>
        /// <param name="fullName">會友全名</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號</param>
        /// <param name="amount">應付金額</param>
        /// <param name="itemName">項目名稱</param>
        /// <param name="paymentTime">嘗試時間</param>
        /// <param name="statusMessage">失敗原因訊息</param>
        /// <returns>格式化的 LINE 訊息字串</returns>
        private string BuildGeneralPaymentFailureMessage(
            string fullName, 
            string orderId, 
            string transactionId, 
            decimal amount, 
            string itemName, 
            DateTime paymentTime, 
            string statusMessage)
        {
            // 訊息標題
            var msg = $"【金流付款失敗通知】{Environment.NewLine}{Environment.NewLine}";
            
            // 問候語與道歉
            msg += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            msg += $"很抱歉，您的付款未能完成。{Environment.NewLine}{Environment.NewLine}";
            
            // 失敗原因
            msg += $"失敗原因：{statusMessage}{Environment.NewLine}{Environment.NewLine}";
            
            // 付款資訊區塊
            msg += $"付款資訊：{Environment.NewLine}";
            msg += $"姓名：{fullName}{Environment.NewLine}";
            msg += $"項目：{itemName}{Environment.NewLine}";
            msg += $"訂單編號：{orderId}{Environment.NewLine}";
            
            // 交易編號（選填）
            if (!string.IsNullOrWhiteSpace(transactionId)) msg += $"交易編號：{transactionId}{Environment.NewLine}";
            
            // 應付金額與時間
            msg += $"應付金額：NT$ {amount:N0}{Environment.NewLine}";
            msg += $"嘗試時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}";
            
            // 後續處理建議
            msg += $"您可以：{Environment.NewLine}";
            msg += $"1. 重新嘗試付款{Environment.NewLine}";
            msg += $"2. 更換其他信用卡{Environment.NewLine}";
            msg += $"3. 聯繫教會辦公室尋求協助{Environment.NewLine}{Environment.NewLine}";
            
            // 結尾語
            msg += $"如有任何問題，請隨時與我們聯繫。";
            
            return msg;
        }

        #endregion

        #region LINE 訊息發送功能

        /// <summary>
        /// ========================================
        /// 發送 LINE 訊息
        /// ========================================
        /// 
        /// 【功能說明】
        /// 使用 LINE Messaging API 發送推播訊息給指定用戶
        /// 
        /// 【處理流程】
        /// 1. 建立 LINE Messaging Client
        /// 2. 使用 PushUtility 發送訊息
        /// 3. 等待發送完成
        /// 4. 記錄發送結果
        /// 
        /// 【錯誤處理】
        /// - 發送失敗會抄出例外
        /// - 錯誤會記錄到日誌
        /// - 上層需處理例外情況
        /// 
        /// 【注意事項】
        /// - LINE ID 必須有效且已加入官方帳號好友
        /// - 訊息內容不可超過 LINE 的字數限制
        /// - 發送使用同步等待（.Wait()），注意執行緒阻塞
        /// 
        /// </summary>
        /// <param name="lineId">接收者的 LINE ID（使用者唯一識別碼）</param>
        /// <param name="message">要發送的訊息內容（純文字格式）</param>
        /// <exception cref="Exception">當發送失敗時拋出</exception>
        private void SendLineMessage(string lineId, string message)
        {
            try
            {
                // 建立 LINE Messaging Client（使用預設的 Channel Access Token）
                var lineMessagingClient = new LineMessagingClient(LINE_CHANNEL_ACCESS_TOKEN);
                
                // 建立推播工具
                var pushUtility = new PushUtility(lineMessagingClient);
                
                // 發送訊息並等待完成
                pushUtility.SendMessage(lineId, message).Wait();
                
                // 記錄成功日誌
                _logger.LogInformation($"SendLineMessage: 已發送 - LineId: {lineId}");
            }
            catch (Exception ex)
            {
                // 記錄錯誤日誌
                _logger.LogError(ex, $"SendLineMessage: 發送失敗 - LineId: {lineId}");
                
                // 重新拋出例外供上層處理
                throw;
            }
        }

        #endregion // LINE 訊息發送功能 子區塊結束
        #endregion // LINE 訊息建立

        #region API: 成功頁面

        /// <summary>
        /// 付款成功頁面 (供用戶查看結果)
        /// GET /api/MyPay/success
        /// 舊版成功頁面，較為簡易
        /// </summary>
        /// <param name="order_id">訂單編號</param>
        /// <returns>View 結果</returns>
        [HttpGet("success")]
        public IActionResult PaymentSuccess([FromQuery] string order_id = "")
        {
            ViewBag.OrderId = order_id;
            ViewBag.Message = "訂單已建立，會透過LINE另行通知交易狀態，感謝您的支持。";
            ViewBag.IsSuccess = true;
            return View("PaymentResult");
        }
        #endregion

        #region API: 失敗頁面
        /// <summary>
        /// 付款失敗頁面 (供用戶查看結果)
        /// GET /api/MyPay/failure
        /// 舊版失敗頁面，較為簡易
        /// </summary>
        /// <param name="order_id">訂單編號</param>
        /// <param name="msg">錯誤訊息</param>
        /// <returns>View 結果</returns>
        [HttpGet("failure")]
        public IActionResult PaymentFailure([FromQuery] string order_id = "", [FromQuery] string msg = "")
        {
            ViewBag.OrderId = order_id;
            ViewBag.Message = !string.IsNullOrEmpty(msg) ? $"付款失敗：{msg}" : "付款失敗，請稍後再試或聯繫教會辦公室。";
            ViewBag.IsSuccess = false;
            return View("PaymentResult");
        }
        #endregion

        #region 狀態/文字/CRM更新輔助方法

        // ========================================================================================================
        // 【狀態/文字/CRM更新輔助方法區塊】
        // 
        // 本區塊提供支援金流處理的各種輔助方法，分為以下幾大類：
        // 
        // 1. 【交易狀態判斷】- 判斷交易是否成功
        // 2. 【錯誤訊息處理】- 建立和轉換錯誤訊息
        // 3. 【日誌記錄】- 記錄完整的金流回傳資料
        // 4. 【CRM 資料更新】- 更新收費單狀態與交易資訊
        // 5. 【LINE 通知發送】- 根據類型發送通知訊息
        // 
        // 【設計原則】
        // - 單一職責：每個方法只負責一項特定任務
        // - 錯誤處理：所有方法都包含完整的異常處理和日誌記錄
        // - 可維護性：清晰的命名和完整的註解說明
        // ========================================================================================================

        #region 1. 交易狀態判斷

        /// <summary>
        /// ========================================
        /// 判斷交易是否成功
        /// ========================================
        /// 
        /// 【功能說明】
        /// 根據高鋸金流的 PRC（交易回傳碼）判斷交易是否完成成功
        /// 
        /// 【成功代碼說明】
        /// - 250: 付款成功（最常見的成功代碼，表示即時交易完成）
        /// - 290: 交易成功但資訊不符（交易完成但部分資訊需要核對）
        /// - 600: 結帳完成（購物車結帳流程完成）
        /// 
        /// 【其他狀態代碼】
        /// - 260, 270, 280: 交易成功但尚未付款完成（虛擬帳號、超商代碼等）
        /// - 300: 交易失敗
        /// - 400: 系統錯誤
        /// - 其他: 參考 GetPaymentStatusMessage 方法
        /// 
        /// 【使用時機】
        /// 在收到金流回傳後，第一時間判斷交易結果，決定後續處理流程
        /// 
        /// 【參考文檔】
        /// 高鋸金流官方規格 - 附錄二：PRC（交易回傳碼）定義
        /// 
        /// </summary>
        /// <param name="prc">金流回傳的交易狀態碼（PRC）</param>
        /// <returns>true 表示交易成功，false 表示交易失敗或狀態未知</returns>
        private bool IsSuccessfulPaymentStatus(string prc)
        {
            // 檢查空值
            if (string.IsNullOrWhiteSpace(prc)) return false;

            // 比對成功代碼
            switch (prc)
            {
                case "250": // 付款成功（信用卡即時交易）
                case "290": // 交易成功但資訊不符
                case "600": // 結帳完成
                    return true;

                default: // 其他所有代碼視為失敗或待處理
                    return false;
            }
        }

        #endregion

        #region 2. 錯誤訊息處理

        /// <summary>
        /// ========================================
        /// 建立失敗訊息文字
        /// ========================================
        /// 
        /// 【功能說明】
        /// 根據金流回傳的錯誤資訊，建立使用者友善的失敗訊息
        /// 
        /// 【處理優先順序】
        /// 1. 優先使用 msg 欄位（金流直接回傳的訊息）
        /// 2. 其次使用 errorCode 或 retCode（轉換為友善訊息）
        /// 3. 最後使用預設失敗訊息
        /// 
        /// 【訊息格式】
        /// - 有明確訊息：「付款失敗：{具體原因}」
        /// - 有錯誤代碼：「付款失敗：{友善說明}」或「付款失敗 (錯誤代碼: {code})」
        /// - 無任何資訊：「付款失敗，請稍後再試或聯繫教會辦公室。」
        /// 
        /// 【使用時機】
        /// 當交易失敗時，將技術性的錯誤代碼轉換為一般使用者能理解的文字
        /// 
        /// </summary>
        /// <param name="msg">金流回傳的錯誤訊息文字（可選）</param>
        /// <param name="errorCode">錯誤代碼（可選）</param>
        /// <param name="retCode">回傳代碼（可選）</param>
        /// <returns>格式化的失敗訊息字串，適合顯示給使用者</returns>
        private string BuildFailureMessage(string msg, string errorCode, string retCode)
        {
            var message = "付款失敗";

            // 優先順序 1：使用 msg 欄位
            if (!string.IsNullOrWhiteSpace(msg))
            {
                message = $"付款失敗：{msg}";
            }
            // 優先順序 2：使用錯誤代碼
            else if (!string.IsNullOrWhiteSpace(errorCode) || !string.IsNullOrWhiteSpace(retCode))
            {
                // 嘗試轉換為友善訊息
                string friendly = GetFriendlyErrorMessage(errorCode, retCode);

                if (!string.IsNullOrWhiteSpace(friendly))
                {
                    // 有對應的友善訊息
                    message = $"付款失敗：{friendly}";
                }
                else
                {
                    // 無對應訊息，直接顯示代碼
                    message = $"付款失敗 (錯誤代碼: {errorCode ?? retCode})";
                }
            }
            // 優先順序 3：預設訊息
            else
            {
                message = "付款失敗，請稍後再試或聯繫教會辦公室。";
            }

            return message;
        }

        /// <summary>
        /// ========================================
        /// 取得友善的錯誤訊息
        /// ========================================
        /// 
        /// 【功能說明】
        /// 將銀行或金流系統的錯誤代碼轉換為使用者能理解的中文說明
        /// 
        /// 【支援的錯誤代碼】
        /// - 卡片狀態：被拒絕、過期、遺失/被盜
        /// - 卡片資料：卡號錯誤、CVV 錯誤
        /// - 額度問題：額度不足、超過限額
        /// - 交易限制：交易不被允許
        /// - 系統問題：連線逾時、網路錯誤、系統錯誤
        /// - 使用者操作：交易取消
        /// - 安全驗證：3D 驗證失敗
        /// 
        /// 【代碼來源】
        /// - 信用卡授權回應碼（標準 ISO 8583）
        /// - 金流平台自訂錯誤碼
        /// 
        /// 【擴充說明】
        /// 如需新增錯誤代碼對應，請在 switch 區塊中加入新的 case
        /// 保持訊息簡潔、友善、具有指引性
        /// 
        /// </summary>
        /// <param name="errorCode">錯誤代碼（英文或數字代碼）</param>
        /// <param name="retCode">回傳代碼（備用）</param>
        /// <returns>友善的中文錯誤訊息，如果代碼無對應則回傳 null</returns>
        private string GetFriendlyErrorMessage(string errorCode, string retCode)
        {
            // 優先使用 errorCode，若為空則使用 retCode
            string code = errorCode ?? retCode ?? "";

            // 轉換為大寫進行比對（避免大小寫問題）
            switch (code.ToUpper())
            {
                // ====== 卡片被拒絕 ======
                case "CARD_DECLINED":
                case "51":
                    return "信用卡被拒絕，請確認卡片狀態或聯繫發卡銀行";

                // ====== 額度不足 ======
                case "INSUFFICIENT_FUNDS":
                case "05":
                    return "信用卡額度不足，請使用其他卡片或聯繫發卡銀行";

                // ====== 卡片過期 ======
                case "EXPIRED_CARD":
                case "54":
                    return "信用卡已過期，請使用其他有效卡片";

                // ====== 卡號錯誤 ======
                case "INVALID_CARD":
                case "14":
                    return "信用卡號碼錯誤，請檢查卡號是否正確";

                // ====== CVV 錯誤 ======
                case "INVALID_CVV":
                case "CVV_ERROR":
                    return "安全碼(CVV)錯誤，請重新輸入";

                // ====== 卡片遺失或被盜 ======
                case "CARD_LOST_STOLEN":
                case "43":
                    return "此卡片已被列為遺失或被盜，請聯繫發卡銀行";

                // ====== 交易不被允許 ======
                case "TRANSACTION_NOT_PERMITTED":
                case "57":
                    return "此交易不被允許，請聯繫發卡銀行";

                // ====== 超過限額 ======
                case "EXCEEDED_LIMIT":
                case "61":
                    return "超過信用卡交易限額，請聯繫發卡銀行";

                // ====== 連線逾時或網路錯誤 ======
                case "TIMEOUT":
                case "NETWORK_ERROR":
                    return "連線逾時或網路錯誤，請稍後再試";

                // ====== 系統錯誤 ======
                case "SYSTEM_ERROR":
                case "96":
                    return "系統錯誤，請稍後再試或聯繫客服";

                // ====== 交易取消 ======
                case "CANCELLED":
                case "USER_CANCELLED":
                    return "交易已被取消";

                // ====== 3D 驗證失敗 ======
                case "3D_SECURE_FAILED":
                case "3DS_FAILED":
                    return "3D驗證失敗，請重新進行驗證";

                // ====== 無對應訊息 ======
                default:
                    return null; // 回傳 null 表示無對應的友善訊息
            }
        }

        #endregion

        #region 3. 日誌記錄

        /// <summary>
        /// ========================================
        /// 記錄完整的金流回傳資料
        /// ========================================
        /// 
        /// 【功能說明】
        /// 將金流平台回傳的完整資料記錄到日誌系統
        /// 用於除錯、稽核和問題追蹤
        /// 
        /// 【記錄內容分類】
        /// 1. 核心欄位：uid, key, prc, order_id（交易識別資訊）
        /// 2. 交易資訊：finishtime, cost, actual_cost（金額與時間）
        /// 3. 付款資訊：pfn, cardno, acode（付款方式與授權）
        /// 4. 消費者資訊：user_id（使用者識別）
        /// 5. 自訂參數：echo_0~2（商家自訂資料）
        /// 6. 舊版欄位：state, msg, transaction_id（向下相容）
        /// 
        /// 【日誌格式】
        /// 使用換行符號分隔各類資訊，便於閱讀和搜尋
        /// 
        /// 【使用時機】
        /// 在接收到金流回傳後立即記錄，無論交易成功或失敗
        /// 
        /// 【注意事項】
        /// - 敏感資訊（如完整卡號）已由金流平台做遮罩處理
        /// - 發生記錄錯誤時不影響主流程，僅記錄錯誤
        /// 
        /// </summary>
        /// <param name="model">金流回傳的資料模型</param>
        private void LogFullReturnData(MyPayReturnModel model)
        {
            try
            {
                // 組合完整的日誌資料
                var logData = $"[MyPay完整回傳資料]\n" +
                             $"核心欄位: uid={model.uid}, key={model.key}, prc={model.prc}, order_id={model.order_id}\n" +
                             $"交易資訊: finishtime={model.finishtime}, cost={model.cost}, actual_cost={model.actual_cost}\n" +
                             $"付款資訊: pfn={model.pfn}, cardno={model.cardno}, acode={model.acode}\n" +
                             $"消費者: user_id={model.user_id}\n" +
                             $"自訂參數: echo_0={model.echo_0}, echo_1={model.echo_1}, echo_2={model.echo_2}\n" +
                             $"舊版欄位: state={model.state}, msg={model.msg}, transaction_id={model.transaction_id}";

                // 寫入日誌
                _logger.LogInformation(logData);
            }
            catch (Exception ex)
            {
                // 記錄日誌本身發生錯誤，記錄例外但不中斷流程
                _logger.LogError(ex, "[MyPay回傳] 記錄回傳資料時發生錯誤");
            }
        }

        #endregion

        #region 4. CRM 資料更新

        /// <summary>
        /// ========================================
        /// 更新 CRM 收費單（使用 MyPayReturnModel）
        /// ========================================
        /// 
        /// 【功能說明】
        /// 根據金流回傳結果更新 Dynamics 365 CRM 中的收費單記錄
        /// 包含付款狀態、金額、時間、交易明細等完整資訊
        /// 
        /// 【更新內容】
        /// 
        /// ► 成功交易更新項目：
        /// - new_pay_status: 設為「已繳費」（100000001）
        /// - new_fee_really_paid: 實付金額（與應付金額相同）
        /// - new_difference_fee_paid: 差額（設為 0）
        /// - new_pay_date: 付款日期時間
        /// - new_pay_way: 付款方式（信用卡 = 100000001）
        /// 
        /// ► 成功與失敗都更新的項目：
        /// - new_description: 附加交易明細（完整的金流回傳資訊）
        /// 
        /// 【描述欄位內容結構】
        /// ```
        /// [金流回傳資訊 - 時間戳記]
        /// ====== 核心欄位 ======
        /// 訂單號、交易流水號、驗證碼、狀態碼
        /// ====== 交易資訊 ======
        /// 完成時間、金額、幣別
        /// ====== 付款資訊 ======
        /// 付款方式、卡號、授權碼、卡別、發卡行
        /// ====== 分期/紅利資訊 ======（若有）
        /// 分期期數、紅利點數
        /// ====== 服務商資訊 ======（若有）
        /// 金融服務商名稱與代碼
        /// ====== 定期定額資訊 ======（若有）
        /// 扣款名稱、期數、群組編號
        /// ====== 虛擬帳號資訊 ======（若有）
        /// 銀行代碼、有效期限
        /// ====== 自訂參數 ======（若有）
        /// echo_0 ~ echo_4
        /// ====== 舊版相容欄位 ======
        /// state, msg, transaction_id, store_uid, hash
        /// ```
        /// 
        /// 【錯誤處理】
        /// - 更新失敗會記錄錯誤並拋出例外
        /// - 上層需要處理例外並決定後續流程
        /// 
        /// 【使用時機】
        /// 在驗證金流回傳資料後，無論成功或失敗都需要更新 CRM 記錄
        /// 
        /// </summary>
        /// <param name="toolUtility">CRM 工具類實例</param>
        /// <param name="feeEntity">要更新的收費單實體</param>
        /// <param name="model">金流回傳資料模型</param>
        /// <param name="isSuccess">交易是否成功</param>
        /// <exception cref="Exception">當更新 CRM 失敗時拋出</exception>
        private void UpdateFeeEntityWithMyPayReturn(
            ToolUtilityClass toolUtility, 
            Entity feeEntity, 
            MyPayReturnModel model, 
            bool isSuccess)
        {
            try
            {
                // ========================================
                // 步驟 1：解析付款時間
                // ========================================
                DateTime paymentTime = ParseFinishTime(model.finishtime);

                // ========================================
                // 步驟 2：如果交易成功，更新付款狀態相關欄位
                // ========================================
                if (isSuccess)
                {
                    // 取得應付金額
                    var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");

                    // 更新付款狀態為「已繳費」
                    toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PAYMENT_STATUS_PAID);

                    // 設定實付金額（等於應付金額）
                    toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);

                    // 設定差額為 0（表示全額繳清）
                    toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));

                    // 設定付款日期
                    toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", paymentTime);

                    // 設定付款方式為「信用卡」
                    toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PAYMENT_METHOD_CREDIT_CARD);
                }

                // ========================================
                // 步驟 3：準備描述欄位資料（成功與失敗都需要）
                // ========================================
                
                // 取得原始描述內容
                var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? string.Empty;

                // 轉換付款方式代碼為中文名稱
                var paymentMethodName = GetPaymentMethodName(model.pfn);

                // 轉換交易狀態代碼為中文說明
                var statusMessage = GetPaymentStatusMessage(model.prc);

                // ========================================
                // 步驟 4：建立新的描述內容（附加在原描述之後）
                // ========================================
                var newDescription = originalDescription + Environment.NewLine +
                    $"[金流回傳資訊 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}]" + Environment.NewLine +
                    "====== 核心欄位 ======" + Environment.NewLine +
                    $"訂單號(order_id): {model.order_id}" + Environment.NewLine +
                    $"交易流水號(uid): {model.uid}" + Environment.NewLine +
                    $"交易驗證碼(key): {model.key}" + Environment.NewLine +
                    $"交易狀態碼(prc): {model.prc} ({statusMessage})" + Environment.NewLine +
                    "====== 交易資訊 ======" + Environment.NewLine +
                    $"完成時間: {paymentTime:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                    $"交易金額: {model.cost}" + Environment.NewLine +
                    $"實際金額: {model.actual_cost ?? model.cost}" + Environment.NewLine +
                    $"交易幣別: {model.currency ?? "TWD"}" + Environment.NewLine +
                    "====== 付款資訊 ======" + Environment.NewLine +
                    $"付款方式(pfn): {paymentMethodName}" + Environment.NewLine +
                    $"卡號: {model.cardno}" + Environment.NewLine +
                    $"授權碼: {model.acode}" + Environment.NewLine +
                    $"卡別: {model.card_type}" + Environment.NewLine +
                    $"發卡行: {model.issuing_bank}" + Environment.NewLine +
                    $"發卡行代碼: {model.issuing_bank_uid}" + Environment.NewLine;

                // ========================================
                // 步驟 5：附加選填資訊（若有資料才加入）
                // ========================================

                // 分期資訊
                if (!string.IsNullOrEmpty(model.installment))
                    newDescription += $"分期資訊: {model.installment}" + Environment.NewLine;

                // 紅利資訊
                if (!string.IsNullOrEmpty(model.redeem))
                    newDescription += $"紅利資訊: {model.redeem}" + Environment.NewLine;

                // 服務商資訊
                if (!string.IsNullOrEmpty(model.supplier_name))
                {
                    newDescription += "====== 服務商資訊 ======" + Environment.NewLine +
                                      $"服務商: {model.supplier_name}" + Environment.NewLine +
                                      $"服務商代碼: {model.supplier_code}" + Environment.NewLine;
                }

                // 定期定額資訊
                if (!string.IsNullOrEmpty(model.payment_name) || 
                    !string.IsNullOrEmpty(model.nois) || 
                    !string.IsNullOrEmpty(model.group_id))
                {
                    newDescription += "====== 定期定額資訊 ======" + Environment.NewLine +
                                      $"扣款名稱: {model.payment_name}" + Environment.NewLine +
                                      $"期數: {model.nois}" + Environment.NewLine +
                                      $"群組編號: {model.group_id}" + Environment.NewLine;
                }

                // 虛擬帳號資訊
                if (!string.IsNullOrEmpty(model.bank_id) || 
                    !string.IsNullOrEmpty(model.expired_date))
                {
                    newDescription += "====== 虛擬帳號資訊 ======" + Environment.NewLine +
                                      $"銀行代碼: {model.bank_id}" + Environment.NewLine +
                                      $"有效期限: {model.expired_date}" + Environment.NewLine;
                }

                // 自訂參數
                if (!string.IsNullOrEmpty(model.echo_0) || 
                    !string.IsNullOrEmpty(model.echo_1) || 
                    !string.IsNullOrEmpty(model.echo_2) || 
                    !string.IsNullOrEmpty(model.echo_3) || 
                    !string.IsNullOrEmpty(model.echo_4))
                {
                    newDescription += "====== 自訂參數 ======" + Environment.NewLine +
                                      $"echo_0: {model.echo_0}" + Environment.NewLine +
                                      $"echo_1: {model.echo_1}" + Environment.NewLine +
                                      $"echo_2: {model.echo_2}" + Environment.NewLine +
                                      $"echo_3: {model.echo_3}" + Environment.NewLine +
                                      $"echo_4: {model.echo_4}" + Environment.NewLine;
                }

                // 舊版相容欄位
                newDescription += "====== 舊版相容欄位 ======" + Environment.NewLine +
                                  $"state: {model.state}" + Environment.NewLine +
                                  $"msg: {model.msg}" + Environment.NewLine +
                                  $"transaction_id: {model.transaction_id}" + Environment.NewLine +
                                  $"store_uid: {model.store_uid}" + Environment.NewLine +
                                  $"hash: {model.hash}" + Environment.NewLine;

                // ========================================
                // 步驟 6：更新描述欄位
                // ========================================
                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);

                // 記錄成功日誌
                _logger.LogInformation($"[MyPay回傳] 收費單欄位已更新 - FeeId: {feeEntity.Id}, OrderId: {model.order_id}");
            }
            catch (Exception ex)
            {
                // 記錄錯誤並重新拋出例外
                _logger.LogError(ex, $"[MyPay回傳] 更新收費單失敗 - OrderId: {model.order_id}");
                throw;
            }
        }

        /// <summary>
        /// ========================================
        /// 更新 CRM 收費單（使用個別參數）
        /// ========================================
        /// 
        /// 【功能說明】
        /// 這是舊版的更新方法，使用個別參數而非 MyPayReturnModel
        /// 主要用於向下相容或特殊情況處理
        /// 
        /// 【與 UpdateFeeEntityWithMyPayReturn 的差異】
        /// - 參數為個別欄位而非完整模型
        /// - 記錄的資訊較為精簡
        /// - 缺少進階欄位（分期、紅利、服務商等）
        /// 
        /// 【建議】
        /// 新開發應優先使用 UpdateFeeEntityWithMyPayReturn 方法
        /// 本方法僅保留用於特殊需求或向下相容
        /// 
        /// 【更新內容】
        /// 與 UpdateFeeEntityWithMyPayReturn 相同的成功狀態更新
        /// 但描述欄位內容較為精簡
        /// 
        /// </summary>
        /// <param name="toolUtility">CRM 工具類實例</param>
        /// <param name="feeEntity">要更新的收費單實體</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="uid">交易流水號</param>
        /// <param name="key">交易驗證碼</param>
        /// <param name="cost">交易金額</param>
        /// <param name="actualCost">實際金額</param>
        /// <param name="prc">交易狀態碼</param>
        /// <param name="pfn">付款方式代碼</param>
        /// <param name="paymentTime">付款時間</param>
        /// <param name="cardno">信用卡號（遮罩後）</param>
        /// <param name="acode">授權碼</acode>
        /// <exception cref="Exception">當更新 CRM 失敗時拋出</exception>
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
                // ========================================
                // 步驟 1：更新付款狀態相關欄位（與主方法相同）
                // ========================================
                var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PAYMENT_STATUS_PAID);
                toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);
                toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));
                toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", paymentTime);
                toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PAYMENT_METHOD_CREDIT_CARD);

                // ========================================
                // 步驟 2：建立精簡版描述內容
                // ========================================
                DateTime transTime = ParseFinishTime(paymentTime.ToString("yyyyMMddHHmmss"));
                var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? string.Empty;

                // 建立精簡的描述內容（不包含進階資訊）
                var newDescription = originalDescription + Environment.NewLine +
                    $"[金流回傳資訊 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}]" + Environment.NewLine +
                    "====== 核心欄位 ======" + Environment.NewLine +
                    $"訂單號(order_id): {orderId}" + Environment.NewLine +
                    $"交易流水號(uid): {uid}" + Environment.NewLine +
                    $"交易驗證碼(key): {key}" + Environment.NewLine +
                    $"交易狀態碼(prc): {prc} ({GetPaymentStatusMessage(prc)})" + Environment.NewLine +
                    "====== 交易資訊 ======" + Environment.NewLine +
                    $"完成時間: {transTime:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                    $"交易金額: {cost}" + Environment.NewLine +
                    $"實際金額: {actualCost ?? cost}" + Environment.NewLine +
                    $"交易幣別: TWD" + Environment.NewLine +
                    "====== 付款資訊 ======" + Environment.NewLine +
                    $"付款方式(pfn): {pfn}" + Environment.NewLine +
                    $"卡號: {cardno}" + Environment.NewLine +
                    $"授權碼: {acode}" + Environment.NewLine;

                // ========================================
                // 步驟 3：更新描述欄位
                // ========================================
                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);
                _logger.LogInformation($"[MyPay回傳] 收費單欄位已更新 - FeeId: {feeEntity.Id}, OrderId: {orderId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 更新收費單失敗 - OrderId: {orderId}");
                throw;
            }
        }

        #endregion

        #region 5.  LINE 通知發送

        // ========================================================================================================
        // 【LINE 通知發送區塊】
        // 
        // 本區塊負責發送各類 LINE 通知，包括：
        // 1. 付款成功通知
        // 2. 付款失敗通知
        // 3. 通用的訊息發送方法
        // 
        // 【設計原則】
        // - 單一職責：每個方法只負責一項特定任務
        // - 錯誤處理：所有方法都包含完整的異常處理和日誌記錄
        // - 可維護性：清晰的命名和完整的註解說明
        // ========================================================================================================

        #region 發送 LINE 通知

        /// <summary>
        /// ========================================
        /// 發送付款通知（使用個別參數）
        /// ========================================
        /// 
        /// 【功能說明】
        /// 舊版的通知發送方法，使用個別參數而非 MyPayReturnModel
        /// 根據收費單類型決定訊息格式，並發送 LINE 通知
        /// 
        /// 【處理流程】
        /// 1. 從收費單取得連絡人資訊
        /// 2. 檢查連絡人是否有 LINE ID
        /// 3. 根據收費單類型建立對應格式的訊息
        /// 4. 發送 LINE 訊息
        /// 
        /// 【支援的收費單類型】
        /// - Dedication（奉獻）：使用奉獻專用訊息格式
        /// - Course（課程）：使用課程繳費訊息格式，包含課程時間地點
        /// - Other（其他）：使用一般繳費訊息格式
        /// 
        /// 【錯誤處理】
        /// - 找不到連絡人：直接返回，不發送訊息
        /// - 沒有 LINE ID：直接返回，不發送訊息
        /// - 發送失敗：記錄錯誤但不影響主流程
        /// 
        /// 【使用時機】
        /// 當使用個別參數（非 MyPayReturnModel）進行處理時使用
        /// 
        /// </summary>
        /// <param name="utility">CRM 工具類實例</param>
        /// <param name="feeEntity">收費單實體</param>
        /// <param name="orderId">訂單編號</param>
        /// <param name="transactionId">交易編號</param>
        /// <param name="cost">交易金額字串</param>
        /// <param name="fullName">連絡人姓名</param>
        /// <param name="itemName">繳費項目名稱</param>
        /// <param name="feeType">收費單類型（奉獻/課程/其他）</param>
        /// <param name="amount">金額數值</param>
        /// <param name="contactEntity">連絡人實體（可選，若為 null 會重新查詢）</param>
        private void SendPaymentNotificationByType(
            ToolUtilityClass utility, 
            Entity feeEntity, 
            string orderId, 
            string transactionId, 
            string cost, 
            string fullName, 
            string itemName, 
            FeeType feeType, decimal amount, 
            Entity contactEntity)
        {
            try
            {
                // ========================================
                // 步驟 1：取得連絡人資訊
                // ========================================
                var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                if (contactId == Guid.Empty) return; // 找不到連絡人，直接返回

                // 如果沒有提供連絡人實體，則查詢
                if (contactEntity == null)
                {
                    contactEntity = utility.RetrieveEntity("contact", contactId);
                }

                if (contactEntity == null) return; // 找不到連絡人，直接返回

                // ========================================
                // 步驟 2：取得 LINE ID
                // ========================================
                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId)) return; // 沒有 LINE ID，直接返回

                // ========================================
                // 步驟 3：根據收費單類型建立訊息
                // ========================================
                string message;

                if (feeType == FeeType.Dedication)
                {
                    // 奉獻類型：使用奉獻專用格式
                    message = BuildDedicationSuccessMessage(
                        fullName, 
                        orderId, 
                        transactionId, 
                        amount, 
                        itemName,  // itemName 作為奉獻類別
                        DateTime.Now
                    );
                }
                else if (feeType == FeeType.Course)
                {
                    // 課程類型：取得課程額外資訊
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? "";
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? "";

                    message = BuildCoursePaymentSuccessMessage(
                        fullName, 
                        orderId, 
                        transactionId, 
                        amount, 
                        itemName,  // itemName 作為課程名稱
                        courseSchedule, 
                        courseLocation, 
                        DateTime.Now
                    );
                }
                else
                {
                    // 其他類型：使用一般格式
                    message = BuildGeneralPaymentSuccessMessage(
                        fullName, 
                        orderId, 
                        transactionId, 
                        amount, 
                        itemName, 
                        DateTime.Now
                    );
                }

                // ========================================
                // 步驟 4：發送 LINE 訊息
                // ========================================
                SendLineMessage(lineId, message);
            }
            catch (Exception ex)
            {
                // 發送失敗記錄錯誤，但不拋出例外（避免影響主流程）
                _logger.LogError(ex, $"SendNotification: 發送 LINE失敗 - OrderId: {orderId}");
            }
        }

        /// <summary>
        /// ========================================
        /// 發送 LINE 成功通知（使用 MyPayReturnModel）
        /// ========================================
        /// 
        /// 【功能說明】
        /// 根據金流回傳的完整資料和收費單類型，發送付款成功的 LINE 通知
        /// 這是主要使用的通知發送方法
        /// 
        /// 【處理流程】
        /// 1. 檢查連絡人實體是否存在
        /// 2. 取得 LINE ID
        /// 3. 解析付款金額（優先使用 actual_cost）
        /// 4. 解析付款時間
        /// 5. 根據收費單類型建立對應訊息
        /// 6. 發送 LINE 訊息
        /// 
        /// 【金額解析優先順序】
        /// 1. actual_cost（實際金額，匯率轉換後）
        /// 2. cost（交易金額，原始幣別）
        /// 3. 0（若都無法解析）
        /// 
        /// 【訊息類型】
        /// - 奉獻：包含奉獻類别（十一、感恩等）
        /// - 課程：包含課程名稱、時間、地點
        /// - 其他：包含項目名稱
        /// 
        /// 【錯誤處理】
        /// - 沒有連絡人：直接返回
        /// - 沒有 LINE ID：直接返回
        /// - 發送失敗：記錄錯誤並重新拋出例外
        /// 
        /// 【使用時機】
        /// 當交易成功且有 MyPayReturnModel 完整資料時使用
        /// 
        /// </summary>
        /// <param name="utility">CRM 工具類實例</param>
        /// <param name="feeEntity">收費單實體</param>
        /// <param name="model">金流回傳資料模型</param>
        /// <param name="fullName">連絡人姓名</param>
        /// <param name="feeType">收費單類型</param>
        /// <param name="contactEntity">連絡人實體</param>
        /// <exception cref="Exception">當發送失敗時拋出</exception>
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
                // ========================================
                // 步驟 1：檢查連絡人實體
                // ========================================
                if (contactEntity == null) return;

                // ========================================
                // 步驟 2：取得 LINE ID
                // ========================================
                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId)) return;

                // ========================================
                // 步驟 3：解析付款金額（優先順序：actual_cost > cost）
                // ========================================
                decimal amount = 0m;
                if (!string.IsNullOrEmpty(model.actual_cost) && 
                    decimal.TryParse(model.actual_cost, out var parsedActual))
                {
                    amount = parsedActual;
                }
                else if (!string.IsNullOrEmpty(model.cost) && 
                         decimal.TryParse(model.cost, out var parsedCost))
                {
                    amount = parsedCost;
                }

                // ========================================
                // 步驟 4：解析付款時間
                // ========================================
                DateTime paymentTime = ParseFinishTime(model.finishtime);

                // ========================================
                // 步驟 5：根據收費單類型建立訊息
                // ========================================
                string message;

                if (feeType == FeeType.Dedication)
                {
                    // 奉獻類型：取得奉獻類別名稱
                    int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                    string dedicationCategory = GetDedicationCategoryName(categoryValue);

                    message = BuildDedicationSuccessMessage(
                        fullName, 
                        model.order_id, 
                        model.uid, 
                        amount, 
                        dedicationCategory, 
                        paymentTime
                    );
                }
                else if (feeType == FeeType.Course)
                {
                    // 課程類型：取得課程完整資訊
                    string courseName = GetCourseName(utility, feeEntity);
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? string.Empty;
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? string.Empty;

                    message = BuildCoursePaymentSuccessMessage(
                        fullName, 
                        model.order_id, 
                        model.uid, 
                        amount, 
                        courseName, 
                        courseSchedule, 
                        courseLocation, 
                        paymentTime
                    );
                }
                else
                {
                    // 其他類型：使用收費單名稱
                    string itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";

                    message = BuildGeneralPaymentSuccessMessage(
                        fullName, 
                        model.order_id, 
                        model.uid, 
                        amount, 
                        itemName, 
                        paymentTime
                    );
                }

                // ========================================
                // 步驟 6：發送 LINE 訊息
                // ========================================
                SendLineMessage(lineId, message);
            }
            catch (Exception ex)
            {
                // 記錄錯誤並重新拋出例外
                _logger.LogError(ex, $"[MyPay回傳] 發送LINE通知失敗 - OrderId: {model?.order_id}");
                throw;
            }
        }

        /// <summary>
        /// ========================================
        /// 發送 LINE 失敗通知（使用 MyPayReturnModel）
        /// ========================================
        /// 
        /// 【功能說明】
        /// 根據金流回傳的失敗資料和收費單類型，發送付款失敗的 LINE 通知
        /// 協助使用者了解失敗原因並提供後續處理建議
        /// 
        /// 【處理流程】
        /// 1. 檢查連絡人實體是否存在
        /// 2. 取得 LINE ID
        /// 3. 解析應付金額（優先順序：CRM > actual_cost > cost）
        /// 4. 解析嘗試付款時間
        /// 5. 獲取失敗狀態訊息
        /// 6. 根據收費單類型建立對應的失敗訊息
        /// 7. 發送 LINE 訊息
        /// 
        /// 【金額解析優先順序】
        /// 1. CRM 中的應付金額（new_fee_shoud_pay）- 最準確
        /// 2. actual_cost（金流回傳的實際金額）
        /// 3. cost（金流回傳的交易金額）
        /// 4. 0（若都無法解析）
        /// 
        /// 【訊息內容特色】
        /// - 明確說明失敗原因（由 PRC 代碼轉換而來）
        /// - 提供後續處理建議（重試、換卡、聯繫辦公室）
        /// - 包含完整的訂單與應付金額資訊
        /// 
        /// 【錯誤處理】
        /// - 沒有連絡人：直接返回
        /// - 沒有 LINE ID：直接返回
        /// - 發送失敗：記錄錯誤並重新拋出例外
        /// 
        /// 【使用時機】
        /// 當交易失敗且有 MyPayReturnModel 完整資料時使用
        /// 
        /// </summary>
        /// <param name="utility">CRM 工具類實例</param>
        /// <param name="feeEntity">收費單實體</param>
        /// <param name="model">金流回傳資料模型</param>
        /// <param name="fullName">連絡人姓名</param>
        /// <param name="feeType">收費單類型</param>
        /// <param name="contactEntity">連絡人實體</param>
        /// <exception cref="Exception">當發送失敗時拋出</exception>
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
                // ========================================
                // 步驟 1：檢查連絡人實體
                // ========================================
                if (contactEntity == null) return;

                // ========================================
                // 步驟 2：取得 LINE ID
                // ========================================
                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId)) return;

                // ========================================
                // 步驟 3：解析應付金額（優先使用 CRM 中的金額）
                // ========================================
                decimal amount = 0m;

                // 優先使用 CRM 中記錄的應付金額（最準確）
                var shouldPayMoney = utility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                if (shouldPayMoney != null && shouldPayMoney.Value > 0)
                {
                    amount = shouldPayMoney.Value;
                }
                // 其次使用金流回傳的實際金額
                else if (!string.IsNullOrWhiteSpace(model.actual_cost) && 
                         decimal.TryParse(model.actual_cost, out var parsedActual))
                {
                    amount = parsedActual;
                }
                // 最後使用金流回傳的交易金額
                else if (!string.IsNullOrWhiteSpace(model.cost) && 
                         decimal.TryParse(model.cost, out var parsedCost))
                {
                    amount = parsedCost;
                }

                // ========================================
                // 步驟 4：解析嘗試付款時間
                // ========================================
                DateTime paymentTime = ParseFinishTime(model.finishtime);

                // ========================================
                // 步驟 5：獲取失敗狀態訊息
                // ========================================
                string statusMessage = GetPaymentStatusMessage(model.prc);

                // ========================================
                // 步驟 6：根據收費單類型建立失敗訊息
                // ========================================
                string message;

                if (feeType == FeeType.Dedication)
                {
                    // 奉獻類型失敗訊息
                    int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                    string dedicationCategory = GetDedicationCategoryName(categoryValue);

                    message = BuildDedicationFailureMessage(
                        fullName, 
                        model.order_id, 
                        model.uid, 
                        amount, 
                        dedicationCategory, 
                        paymentTime, 
                        statusMessage
                    );
                }
                else if (feeType == FeeType.Course)
                {
                    // 課程類型失敗訊息
                    string courseName = GetCourseName(utility, feeEntity);
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? string.Empty;
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? string.Empty;

                    message = BuildCoursePaymentFailureMessage(
                        fullName, 
                        model.order_id, 
                        model.uid, 
                        amount, 
                        courseName, 
                        courseSchedule, 
                        courseLocation, 
                        paymentTime, 
                        statusMessage
                    );
                }
                else
                {
                    // 其他類型失敗訊息
                    string itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";

                    message = BuildGeneralPaymentFailureMessage(
                        fullName, 
                        model.order_id, 
                        model.uid, 
                        amount, 
                        itemName, 
                        paymentTime, 
                        statusMessage
                    );
                }

                // ========================================
                // 步驟 7：發送 LINE 訊息
                // ========================================
                SendLineMessage(lineId, message);
            }
            catch (Exception ex)
            {
                // 記錄錯誤並重新拋出例外
                _logger.LogError(ex, $"[MyPay回傳] 發送LINE失敗通知失敗 - OrderId: {model?.order_id}");
                throw;
            }
        }

        #endregion // LINE 通知發送

        #region 收費單類型與狀態判斷

        /// <summary>
        /// 收費單類型列舉
        /// 用於區分不同類型的繳費項目，以便發送對應格式的通知
        /// </summary>
        private enum FeeType
        {
            /// <summary>
            /// 奉獻類型（十一奉獻、感恩奉獻等）
            /// </summary>
            Dedication,

            /// <summary>
            /// 課程類型（課程報名繳費、研習費用等）
            /// </summary>
            Course,

            /// <summary>
            /// 其他類型（一般性繪費項目）
            /// </summary>
            Other
        }

        /// <summary>
        /// ========================================
        /// 取得交易狀態訊息
        /// ========================================
        /// 
        /// 【功能說明】
        /// 將高鋸金流的 PRC（交易回傳碼）轉換為中文說明文字
        /// 
        /// 【支援的狀態碼】
        /// - 成功類：250（付款成功）、290（交易成功但資訊不符）、600（結帳完成）
        /// - 待完成類：260（超商代碼）、270（虛擬帳號）、280（WebATM）
        /// - 其他類：取消、退款、失敗、系統錯誤等
        /// 
        /// 【參考文檔】
        /// 高鋸金流官方規格 - 附錄二：PRC（交易回傳碼）定義
        /// 
        /// </summary>
        /// <param name="prc">交易回傳碼</param>
        /// <returns>對應的中文狀態說明，未知代碼則回傳「未知狀態碼：{prc}」</returns>
        private string GetPaymentStatusMessage(string prc)
        {
            if (string.IsNullOrWhiteSpace(prc)) return "付款狀態未知";

            switch (prc)
            {
                // ====== 資料相關 ======
                case "100": return "資料錯誤 - MYPAYLINK收到資料，但是格式或資料錯誤";
                case "200": return "資料正確 - MYPAYLINK收到正確資料，會接續下一步交易";

                // ====== 交易成功類 ======
                case "220": return "取消成功";
                case "230": return "退款成功";
                case "250": return "付款成功";
                case "290": return "交易成功但資訊不符";
                case "600": return "結帳完成";

                // ====== 交易成功但待完成類 ======
                case "260": return "交易成功，尚未付款完成(超商代碼)";
                case "265": return "訂單綁定";
                case "270": return "交易成功，尚未付款完成(虛擬帳號)";
                case "275": return "交易成功，待審核(無卡分期)";
                case "280": return "交易成功，尚未付款完成(WebATM)";

                // ====== 交易失敗類 ======
                case "300": return "交易失敗";
                case "380": return "逾期交易";

                // ====== 系統錯誤 ======
                case "400": return "系統錯誤";

                // ====== 其他狀態 ======
                case "A0001": return "交易待確認";
                case "A0002": return "放棄交易";
                case "B200": return "執行成功";
                case "B500": return "執行失敗";

                // ====== 未知狀態 ======
                default: return $"未知狀態碼：{prc}";
            }
        }

        /// <summary>
        /// ========================================
        /// 解析完成時間字串
        /// ========================================
        /// 
        /// 【功能說明】
        /// 將高鋸金流回傳的時間字串（格式：yyyyMMddHHmmss）解析為 DateTime 物件
        /// 
        /// 【時間格式】
        /// - 輸入格式：yyyyMMddHHmmss（14 位數字）
        /// - 例如：20240315143025 表示 2024年3月15日 14:30:25
        /// 
        /// 【錯誤處理】
        /// - 若字串為空或長度不符，回傳當前時間
        /// - 若解析失敗，記錄錯誤並回傳當前時間
        /// 
        /// </summary>
        /// <param name="finishtime">完成時間字串（yyyyMMddHHmmss 格式）</param>
        /// <returns>解析後的 DateTime 物件，失敗則回傳 DateTime.Now</returns>
        private DateTime ParseFinishTime(string finishtime)
        {
            // 檢查字串是否符合長度要求（14 位）
            if (string.IsNullOrWhiteSpace(finishtime) || finishtime.Length != 14)
            {
                return DateTime.Now;
            }

            try
            {
                // 解析年月日時分秒
                int year = int.Parse(finishtime.Substring(0, 4));    // 西元年（4位）
                int month = int.Parse(finishtime.Substring(4, 2));   // 月份（2位）
                int day = int.Parse(finishtime.Substring(6, 2));     // 日期（2位）
                int hour = int.Parse(finishtime.Substring(8, 2));    // 小時（2位）
                int minute = int.Parse(finishtime.Substring(10, 2)); // 分鐘（2位）
                int second = int.Parse(finishtime.Substring(12, 2)); // 秒數（2位）

                // 建立 DateTime 物件
                return new DateTime(year, month, day, hour, minute, second);
            }
            catch (Exception ex)
            {
                // 解析失敗，記錄錯誤並回傳當前時間
                _logger.LogError(ex, $"ParseFinishTime:解析時間失敗 - FinishTime: {finishtime}");
                return DateTime.Now;
            }
        }

        /// <summary>
        /// ========================================
        /// 取得付款方式名稱
        /// ========================================
        /// 
        /// 【功能說明】
        /// 將高鋸金流的 PFN（支付工具代碼）轉換為中文名稱
        /// 
        /// 【支援的付款方式】
        /// - 1 / CREDITCARD: 信用卡
        /// - 6 / E_COLLECTION: 虛擬帳號
        /// - 3 / CSTORECODE: 超商代碼
        /// - 8 / CREDITCARD_INSTALLMENT: 信用卡分期
        /// 
        /// 【參考文檔】
        /// 高鋸金流官方規格 - 附錄一：PFN（支付工具）參數表
        /// 
        /// </summary>
        /// <param name="pfn">支付工具代碼（數字或英文代碼）</param>
        /// <returns>對應的中文支付方式名稱，未知代碼則回傳「支付工具 {pfn}」</returns>
        private string GetPaymentMethodName(string pfn)
        {
            if (string.IsNullOrWhiteSpace(pfn)) return "未知支付工具";

            // 轉換為大寫進行比對
            string k = pfn.ToUpper();

            switch (k)
            {
                // 信用卡
                case "1":
                case "CREDITCARD":
                    return "信用卡";

                // 虛擬帳號
                case "6":
                case "E_COLLECTION":
                    return "虛擬帳號";

                // 超商代碼
                case "3":
                case "CSTORECODE":
                    return "超商代碼";

                // 信用卡分期
                case "8":
                case "CREDITCARD_INSTALLMENT":
                    return "信用卡分期";

                // 未知支付工具
                default:
                    return $"支付工具 {pfn}";
            }
        }

        /// <summary>
        /// ========================================
        /// 判斷收費單類型
        /// ========================================
        /// 
        /// 【功能說明】
        /// 根據收費單的欄位內容判斷其類型（奉獻/課程/其他）
        /// 
        /// 【判斷邏輯】
        /// 1. 檢查是否有關聯課程（new_course_id）→ 課程類型
        /// 2. 檢查收費單名稱是否包含課程關鍵字 → 課程類型
        /// 3. 檢查課程名稱欄位是否有值 → 課程類型
        /// 4. 檢查奉獻類別代碼範圍（100000000~100000019）→ 奉獻類型
        /// 5. 預設為奉獻類型
        /// 
        /// 【課程關鍵字】
        /// - 課程、報名、學費、培訓、研習
        /// 
        /// 【奉獻類別代碼範圍】
        /// - 100000000 ~ 100000019：各類奉獻（十一、感恩、建堂等）
        /// 
        /// </summary>
        /// <param name="utility">CRM 工具類實例</param>
        /// <param name="feeEntity">收費單實體</param>
        /// <returns>收費單類型（Dedication/Course/Other）</returns>
        private FeeType DetermineFeeType(ToolUtilityClass utility, Entity feeEntity)
        {
            try
            {
                // ========================================
                // 判斷 1：檢查課程關聯
                // ========================================
                var courseId = utility.GetEntityLookupAttribute(feeEntity, "new_course_id");
                if (courseId != Guid.Empty)
                {
                    return FeeType.Course; // 有關聯課程，判定為課程類型
                }

                // ========================================
                // 判斷 2：檢查收費單名稱是否包含課程關鍵字
                // ========================================
                string feeName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? string.Empty;
                if (feeName.Contains("課程") || 
                    feeName.Contains("報名") || 
                    feeName.Contains("學費") || 
                    feeName.Contains("培訓") || 
                    feeName.Contains("研習"))
                {
                    return FeeType.Course; // 名稱包含課程關鍵字，判定為課程類型
                }

                // ========================================
                // 判斷 3：檢查課程名稱欄位
                // ========================================
                string courseName = utility.GetEntityStringAttribute(feeEntity, "new_course_name");
                if (!string.IsNullOrWhiteSpace(courseName))
                {
                    return FeeType.Course; // 有課程名稱，判定為課程類型
                }

                // ========================================
                // 判斷 4：檢查奉獻類別代碼
                // ========================================
                int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                if (categoryValue >= 100000000 && categoryValue <= 100000019)
                {
                    return FeeType.Dedication; // 在奉獻類別代碼範圍內，判定為奉獻類型
                }

                // ========================================
                // 預設判斷：奉獻類型
                // ========================================
                // 若無法明確判斷為課程，則預設為奉獻類型
                return FeeType.Dedication;
            }
            catch (Exception ex)
            {
                // 發生錯誤時記錄日誌，並預設為奉獻類型
                _logger.LogError(ex, "DetermineFeeType例外，預設奉獻");
                return FeeType.Dedication;
            }
        }

        /// <summary>
        /// ========================================
        /// 取得課程名稱
        /// ========================================
        /// 
        /// 【功能說明】
        /// 從收費單取得對應的課程名稱
        /// 
        /// 【取得順序】
        /// 1. 透過課程關聯（new_course_id）查詢課程實體的名稱
        /// 2. 使用收費單的課程名稱欄位（new_course_name）
        /// 3. 使用收費單本身的名稱（new_name）
        /// 4. 預設回傳「課程」
        /// 
        /// 【使用時機】
        /// 建立課程繳費相關的 LINE 通知訊息時使用
        /// 
        /// </summary>
        /// <param name="utility">CRM 工具類實例</param>
        /// <param name="feeEntity">收費單實體</param>
        /// <returns>課程名稱字串，若無法取得則回傳「課程」</returns>
        private string GetCourseName(ToolUtilityClass utility, Entity feeEntity)
        {
            try
            {
                // ========================================
                // 方法 1：從課程實體查詢
                // ========================================
                var courseId = utility.GetEntityLookupAttribute(feeEntity, "new_course_id");
                if (courseId != Guid.Empty)
                {
                    // 查詢課程實體
                    var courseEntity = utility.RetrieveEntity("new_course", courseId);
                    if (courseEntity != null)
                    {
                        // 取得課程名稱
                        var name = utility.GetEntityStringAttribute(courseEntity, "new_name");
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return name; // 成功取得課程實體的名稱
                        }
                    }
                }

                // ========================================
                // 方法 2：從收費單的課程名稱欄位
                // ========================================
                var courseNameField = utility.GetEntityStringAttribute(feeEntity, "new_course_name");
                if (!string.IsNullOrWhiteSpace(courseNameField))
                {
                    return courseNameField; // 使用收費單記錄的課程名稱
                }

                // ========================================
                // 方法 3：從收費單名稱
                // ========================================
                return utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "課程";
            }
            catch (Exception ex)
            {
                // 發生錯誤時記錄日誌，並回傳預設值
                _logger.LogError(ex, "GetCourseName例外");
                return "課程";
            }
        }

        /// <summary>
        /// ========================================
        /// 取得奉獻類別名稱
        /// ========================================
        /// 
        /// 【功能說明】
        /// 將 CRM 中的奉獻類別代碼轉換為中文名稱
        /// 
        /// 【支援的奉獻類別】
        /// - 100000010: 主日奉獻
        /// - 100000000: 十一奉獻
        /// - 100000002: 感恩奉獻
        /// - 100000006: 建堂奉獻
        /// - 100000007: 宣教奉獻
        /// - 100000019: 愛心奉獻
        /// - 100000008: 特別獻金
        /// - 其他: 奉獻（預設）
        /// 
        /// 【使用時機】
        /// 建立奉獻相關的 LINE 通知訊息時使用
        /// 
        /// </summary>
        /// <param name="categoryValue">奉獻類別代碼（OptionSet 值）</param>
        /// <returns>對應的中文奉獻類別名稱，未知代碼則回傳「奉獻」</returns>
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
                default: return "奉獻"; // 預設類別名稱
            }
        }

        #endregion // 收費單類型與狀態判斷
        #endregion // 收費單類型與狀態判斷 (duplicate)
        #endregion // 狀態/文字/CRM更新輔助方法
    } // MyPayController 類別結束

    /// <summary>
    /// 金流回傳模型擴充方法
    /// 提供 MyPayReturnModel 的擴充驗證和處理方法
    /// </summary>
    public static class MyPayReturnModelExtensions
    {
        /// <summary>
        /// ========================================
        /// 驗證所有欄位完整性
        /// ========================================
        /// 
        /// 【功能說明】
        /// 驗證 MyPay 交易回傳模型的所有必要欄位
        /// 包含核心欄位及其格式、長度、關聯性等
        /// 
        /// 【驗證規則】
        /// 1. uid、key、prc、order_id 為必填，且不可為空字串
        /// 2. uid、key 長度必須為 32 字元
        /// 3. prc 參數需符合預期的成功或失敗代碼
        /// 4. order_id 必須是已知的訂單格式
        /// 
        /// 【回傳結果】
        /// - 驗證通過：返回 ValidationResult，IsValid=true
        /// - 驗證不通過：返回 ValidationResult，包含錯誤訊息
        /// 
        /// 【使用時機】
        /// 當接收到金流回傳資料後，第一時間驗證所有必要欄位
        /// 
        /// </summary>
        /// <param name="model">MyPay 交易回傳模型</param>
        /// <returns>驗證結果，包含是否通過驗證的標誌及錯誤訊息</returns>
        public static ValidationResult ValidateAllFields(this MyPayReturnModel model)
        {
            var result = new ValidationResult();

            try
            {
                // 1. uid 不得為空且長度為 32
                if (string.IsNullOrWhiteSpace(model.uid) || model.uid.Length != 32)
                {
                    result.IsValid = false;
                    result.Errors.Add("uid 格式錯誤");
                }

                // 2. key 不得為空且長度為 32
                if (string.IsNullOrWhiteSpace(model.key) || model.key.Length != 32)
                {
                    result.IsValid = false;
                    result.Errors.Add("key 格式錯誤");
                }

                // 3. prc 需為已知的成功或失敗代碼
                if (!new [] {"250", "290", "600", "300", "400", "260", "270", "280"}.Contains(model.prc))
                {
                    result.IsValid = false;
                    result.Errors.Add("prc 狀態碼不在預期範圍內");
                }

                // 4. order_id 不得為空
                if (string.IsNullOrWhiteSpace(model.order_id))
                {
                    result.IsValid = false;
                    result.Errors.Add("order_id 為必填欄位");
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"驗證過程中發生錯誤：{ex.Message}");
            }

            // 注意：ValidationResult.Level 可能為唯讀屬性，僅設定 IsValid 與 Errors
            return result;
        }
    }
}