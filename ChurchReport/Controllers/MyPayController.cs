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
        /// - 發送失敗會拋出例外
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

        private bool IsSuccessfulPaymentStatus(string prc)
        {
            if (string.IsNullOrWhiteSpace(prc)) return false;
            switch (prc)
            {
                case "250":
                case "290":
                case "600":
                    return true;
                default:
                    return false;
            }
        }

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

                if (!string.IsNullOrEmpty(model.installment)) newDescription += $"分期資訊: {model.installment}" + Environment.NewLine;
                if (!string.IsNullOrEmpty(model.redeem)) newDescription += $"紅利資訊: {model.redeem}" + Environment.NewLine;

                if (!string.IsNullOrEmpty(model.supplier_name))
                {
                    newDescription += "====== 服務商資訊 ======" + Environment.NewLine +
                                      $"服務商: {model.supplier_name}" + Environment.NewLine +
                                      $"服務商代碼: {model.supplier_code}" + Environment.NewLine;
                }

                if (!string.IsNullOrEmpty(model.payment_name) || !string.IsNullOrEmpty(model.nois) || !string.IsNullOrEmpty(model.group_id))
                {
                    newDescription += "====== 定期定額資訊 ======" + Environment.NewLine +
                                      $"扣款名稱: {model.payment_name}" + Environment.NewLine +
                                      $"期數: {model.nois}" + Environment.NewLine +
                                      $"群組編號: {model.group_id}" + Environment.NewLine;
                }

                if (!string.IsNullOrEmpty(model.bank_id) || !string.IsNullOrEmpty(model.expired_date))
                {
                    newDescription += "====== 虛擬帳號資訊 ======" + Environment.NewLine +
                                      $"銀行代碼: {model.bank_id}" + Environment.NewLine +
                                      $"有效期限: {model.expired_date}" + Environment.NewLine;
                }

                if (!string.IsNullOrEmpty(model.echo_0) || !string.IsNullOrEmpty(model.echo_1) || !string.IsNullOrEmpty(model.echo_2) || !string.IsNullOrEmpty(model.echo_3) || !string.IsNullOrEmpty(model.echo_4))
                {
                    newDescription += "====== 自訂參數 ======" + Environment.NewLine +
                                      $"echo_0: {model.echo_0}" + Environment.NewLine +
                                      $"echo_1: {model.echo_1}" + Environment.NewLine +
                                      $"echo_2: {model.echo_2}" + Environment.NewLine +
                                      $"echo_3: {model.echo_3}" + Environment.NewLine +
                                      $"echo_4: {model.echo_4}" + Environment.NewLine;
                }

                newDescription += "====== 舊版相容欄位 ======" + Environment.NewLine +
                                  $"state: {model.state}" + Environment.NewLine +
                                  $"msg: {model.msg}" + Environment.NewLine +
                                  $"transaction_id: {model.transaction_id}" + Environment.NewLine +
                                  $"store_uid: {model.store_uid}" + Environment.NewLine +
                                  $"hash: {model.hash}" + Environment.NewLine;

                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);
                _logger.LogInformation($"[MyPay回傳] 收費單欄位已更新 - FeeId: {feeEntity.Id}, OrderId: {model.order_id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 更新收費單失敗 - OrderId: {model.order_id}");
                throw;
            }
        }

        private void UpdateFeeEntityForSuccessWithMyPay(ToolUtilityClass toolUtility, Entity feeEntity, string orderId, string uid, string key, string cost, string actualCost, string prc, string pfn, DateTime paymentTime, string cardno, string acode)
        {
            try
            {
                var shouldPayMoney = toolUtility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_status", PAYMENT_STATUS_PAID);
                toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_fee_really_paid", shouldPayMoney);
                toolUtility.SetEntityMoneyAttribute(ref feeEntity, "new_difference_fee_paid", new Money(0));
                toolUtility.SetEntityDateTimeAttribute(ref feeEntity, "new_pay_date", paymentTime);
                toolUtility.SetOptionSetAttribute(ref feeEntity, "new_pay_way", PAYMENT_METHOD_CREDIT_CARD);

                DateTime transTime = ParseFinishTime(paymentTime.ToString("yyyyMMddHHmmss"));
                var originalDescription = toolUtility.GetEntityStringAttribute(feeEntity, "new_description") ?? string.Empty;
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
                    $"交易幣別: {actualCost ?? cost}" + Environment.NewLine +
                    "====== 付款資訊 ======" + Environment.NewLine +
                    $"付款方式(pfn): {pfn}" + Environment.NewLine +
                    $"卡號: {cardno}" + Environment.NewLine +
                    $"授權碼: {acode}" + Environment.NewLine +
                    $"卡別: {""}" + Environment.NewLine +
                    $"發卡行: {""}" + Environment.NewLine +
                    $"發卡行代碼: {""}" + Environment.NewLine;

                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);
                _logger.LogInformation($"[MyPay回傳] 收費單欄位已更新 - FeeId: {feeEntity.Id}, OrderId: {orderId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 更新收費單失敗 - OrderId: {orderId}");
                throw;
            }
        }

        private void SendPaymentNotificationByType(ToolUtilityClass utility, Entity feeEntity, string orderId, string transactionId, string cost, string fullName, string itemName, FeeType feeType, decimal amount, Entity contactEntity)
        {
            try
            {
                var contactId = utility.GetEntityLookupAttribute(feeEntity, "new_contact_new_fee");
                if (contactId == Guid.Empty) return;
                if (contactEntity == null) contactEntity = utility.RetrieveEntity("contact", contactId);
                if (contactEntity == null) return;
                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId)) return;

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendNotification: 發送 LINE失敗 - OrderId: {orderId}");
            }
        }

        private void SendLineNotificationByType(ToolUtilityClass utility, Entity feeEntity, MyPayReturnModel model, string fullName, FeeType feeType, Entity contactEntity)
        {
            try
            {
                if (contactEntity == null) return;
                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId)) return;

                decimal amount = 0m;
                if (!string.IsNullOrEmpty(model.actual_cost) && decimal.TryParse(model.actual_cost, out var parsedActual)) amount = parsedActual;
                else if (!string.IsNullOrEmpty(model.cost) && decimal.TryParse(model.cost, out var parsedCost)) amount = parsedCost;

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
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? string.Empty;
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? string.Empty;
                    message = BuildCoursePaymentSuccessMessage(fullName, model.order_id, model.uid, amount, courseName, courseSchedule, courseLocation, paymentTime);
                }
                else
                {
                    string itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";
                    message = BuildGeneralPaymentSuccessMessage(fullName, model.order_id, model.uid, amount, itemName, paymentTime);
                }

                SendLineMessage(lineId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 發送LINE通知失敗 - OrderId: {model?.order_id}");
                throw;
            }
        }

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
                if (contactEntity == null) return;
                string lineId = utility.GetEntityStringAttribute(contactEntity, "new_lineid");
                if (string.IsNullOrWhiteSpace(lineId)) return;

                decimal amount = 0m;
                var shouldPayMoney = utility.GetEntityMoneyAttribute(feeEntity, "new_fee_shoud_pay");
                if (shouldPayMoney != null && shouldPayMoney.Value > 0) amount = shouldPayMoney.Value;
                else if (!string.IsNullOrWhiteSpace(model.actual_cost) && decimal.TryParse(model.actual_cost, out var parsedActual)) amount = parsedActual;
                else if (!string.IsNullOrWhiteSpace(model.cost) && decimal.TryParse(model.cost, out var parsedCost)) amount = parsedCost;

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
                    string courseSchedule = utility.GetEntityStringAttribute(feeEntity, "new_course_schedule") ?? string.Empty;
                    string courseLocation = utility.GetEntityStringAttribute(feeEntity, "new_course_location") ?? string.Empty;
                    message = BuildCoursePaymentFailureMessage(fullName, model.order_id, model.uid, amount, courseName, courseSchedule, courseLocation, paymentTime, statusMessage);
                }
                else
                {
                    string itemName = utility.GetEntityStringAttribute(feeEntity, "new_name") ?? "繳費";
                    message = BuildGeneralPaymentFailureMessage(fullName, model.order_id, model.uid, amount, itemName, paymentTime, statusMessage);
                }

                SendLineMessage(lineId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[MyPay回傳] 發送LINE失敗通知失敗 - OrderId: {model?.order_id}");
                throw;
            }
        }

        #endregion // 狀態/文字/CRM更新輔助方法 結束

        #region 收費單類型與狀態判斷

        private enum FeeType
        {
            Dedication,
            Course,
            Other
        }

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
                case "260": return "交易成功，尚未付款完成(超商代碼)";
                case "265": return "訂單綁定";
                case "270": return "交易成功，尚未付款完成(虛擬帳號)";
                case "275": return "交易成功，待審核(無卡分期)";
                case "280": return "交易成功，尚未付款完成(WebATM)";
                case "290": return "交易成功但資訊不符";
                case "300": return "交易失敗";
                case "380": return "逾期交易";
                case "400": return "系統錯誤";
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

        private string GetPaymentMethodName(string pfn)
        {
            if (string.IsNullOrWhiteSpace(pfn)) return "未知支付工具";
            string k = pfn.ToUpper();
            switch (k)
            {
                case "1":
                case "CREDITCARD": return "信用卡";
                case "6":
                case "E_COLLECTION": return "虛擬帳號";
                case "3":
                case "CSTORECODE": return "超商代碼";
                case "8":
                case "CREDITCARD_INSTALLMENT": return "信用卡分期";
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
                if (feeName.Contains("課程") || feeName.Contains("報名") || feeName.Contains("學費") || feeName.Contains("培訓") || feeName.Contains("研習"))
                    return FeeType.Course;

                string courseName = utility.GetEntityStringAttribute(feeEntity, "new_course_name");
                if (!string.IsNullOrWhiteSpace(courseName)) return FeeType.Course;

                int categoryValue = utility.GetOptionSetAttribute(feeEntity, "new_category");
                if (categoryValue >= 100000000 && categoryValue <= 100000019) return FeeType.Dedication;

                return FeeType.Dedication;
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
    }

    /// <summary>
    /// 金流回傳模型擴充方法
    /// 提供 MyPayReturnModel 的擴充驗證和處理方法
    /// </summary>
    public static class MyPayReturnModelExtensions
    {
        /// <summary>
        /// 驗證高鋸金流必要欄位（根據官方規格）
        /// 參考：高鋸金流規格.txt - 交易回傳格式
        /// 整合原有的 ProcessAllReturnFields 功能
        /// </summary>
        /// <param name="model">金流回傳模型實例</param>
        /// <returns>驗證結果，包含是否有效、錯誤訊息和警告訊息</returns>
        public static Models.ValidationResult ValidateAllFields(this MyPayReturnModel model)
        {
            var result = new Models.ValidationResult { IsValid = true };

            // ====== 核心必要欄位（所有回傳都需要） ======
            if (string.IsNullOrEmpty(model.uid))
            {
                result.Errors.Add("uid (交易流水號) 是必要欄位");
                result.IsValid = false;
            }

            if (string.IsNullOrEmpty(model.key))
            {
                result.Errors.Add("key (交易驗證碼) 是必要欄位");
                result.IsValid = false;
            }

            if (string.IsNullOrEmpty(model.prc))
            {
                result.Errors.Add("prc (交易回傳碼) 是必要欄位");
                result.IsValid = false;
            }

            if (string.IsNullOrEmpty(model.order_id))
            {
                result.Errors.Add("order_id (訂單編號) 是必要欄位");
                result.IsValid = false;
            }

            // ====== 交易資訊（即時交易回傳需要） ======
            if (!string.IsNullOrEmpty(model.prc) && IsImmediateTransaction(model.prc))
            {
                if (string.IsNullOrEmpty(model.finishtime))
                {
                    // Warnings 尚未在 Models.ValidationResult 中定義，暫時使用 Errors
                    result.Errors.Add("? finishtime (交易完成時間) 建議填寫");
                }

                if (string.IsNullOrEmpty(model.cost) && string.IsNullOrEmpty(model.actual_cost))
                {
                    result.Errors.Add("cost 或 actual_cost 至少需要一個");
                    result.IsValid = false;
                }

                if (string.IsNullOrEmpty(model.pfn))
                {
                    result.Errors.Add("? pfn (付費方法) 建議填寫");
                }
            }

            // ====== 虛擬帳號/超商代碼（非即時交易需要） ======
            if (!string.IsNullOrEmpty(model.result_content_type) &&
                (model.result_content_type == "E_COLLECTION" || model.result_content_type == "CSTORECODE"))
            {
                if (string.IsNullOrEmpty(model.bank_id))
                {
                    result.Errors.Add("? bank_id (銀行代碼) 虛擬帳號交易建議填寫");
                }

                if (string.IsNullOrEmpty(model.expired_date))
                {
                    result.Errors.Add("? expired_date (有效期限) 非即時交易建議填寫");
                }
            }

            // ====== 舊版相容欄位（向下相容，非必要） ======
            // 註：這些欄位是為了相容舊版系統，不應列為必要欄位
            if (string.IsNullOrEmpty(model.state))
            {
                result.Errors.Add("? state 是舊版相容欄位，建議填寫");
            }

            if (string.IsNullOrEmpty(model.transaction_id))
            {
                result.Errors.Add("? transaction_id 是舊版相容欄位，建議填寫");
            }

            if (string.IsNullOrEmpty(model.msg))
            {
                result.Errors.Add("? msg 是舊版相容欄位，建議填寫");
            }

            if (string.IsNullOrEmpty(model.store_uid))
            {
                result.Errors.Add("? store_uid 是舊版相容欄位，建議填寫");
            }

            if (string.IsNullOrEmpty(model.hash))
            {
                result.Errors.Add("? hash 是舊版相容欄位，建議填寫");
            }

            return result;
        }

        /// <summary>
        /// 判斷是否為即時交易
        /// 根據 PRC 代碼判斷交易是否為即時完成類型
        /// </summary>
        /// <param name="prc">交易回傳碼</param>
        /// <returns>true 表示為即時交易，false 表示為非即時交易</returns>
        private static bool IsImmediateTransaction(string prc)
        {
            // 250: 付款成功
            // 290: 交易成功但資訊不符
            // 600: 結帳完成
            return prc == "250" || prc == "290" || prc == "600";
        }

        /// <summary>
        /// 整合驗證與處理 - 一次完成驗證和資料處理
        /// </summary>
        /// <param name="model">金流回傳模型實例</param>
        /// <returns>驗證結果和處理結果的元組</returns>
        public static (Models.ValidationResult Validation, MyPayProcessingResult Processing) ValidateAndProcess(this MyPayReturnModel model)
        {
            // 先驗證
            var validation = model.ValidateAllFields();

            // 再處理
            var processing = model.ProcessAllReturnFields();

            return (validation, processing);
        }
    }
}