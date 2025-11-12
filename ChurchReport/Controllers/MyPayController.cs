using Microsoft.AspNetCore.Mvc;
using ChurchReport.Models;
using ChurchReport.Services;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using ToolUtilityNameSpace;
using Microsoft.Xrm.Sdk;
using static ChurchReport.Services.MyPayFeeTypeHelper;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 金流 PayPage 回傳處理控制器
    /// 負責處理高鋸金流 (MyPay) 的各種回傳通知
    /// 
    /// 【重構說明】
    /// 本控制器已重構為精簡版本，將業務邏輯分離到以下服務類別：
    /// - MyPayMessageBuilder: LINE 訊息建立
    /// - MyPayCrmService: CRM 資料更新
    /// - MyPayNotificationService: LINE 通知發送
    /// - MyPayStatusHelper: 狀態判斷與訊息轉換
    /// - MyPayFeeTypeHelper: 收費單類型判斷
    /// - MyPayLogger: 日誌記錄
    /// </summary>
    [Route("api/[controller]")]
    public class MyPayController : Controller
    {
        #region 常數定義

        private const string DYNAMICS_CONNECTION_NAME = "DYNAMICS365";

        #endregion

        #region 私有欄位與服務注入

        private readonly ILogger<MyPayController> _logger;
        private readonly MyPayMessageBuilder _messageBuilder;
        private readonly MyPayCrmService _crmService;
        private readonly MyPayNotificationService _notificationService;
        private readonly MyPayStatusHelper _statusHelper;
        private readonly MyPayFeeTypeHelper _feeTypeHelper;
        private readonly MyPayLogger _myPayLogger;

        #endregion

        #region 建構函式

        /// <summary>
        /// MyPayController 建構函式
        /// 注入所有需要的服務
        /// </summary>
        public MyPayController(
            ILogger<MyPayController> logger,
            MyPayMessageBuilder messageBuilder,
            MyPayCrmService crmService,
            MyPayNotificationService notificationService,
            MyPayStatusHelper statusHelper,
            MyPayFeeTypeHelper feeTypeHelper,
            MyPayLogger myPayLogger)
        {
            _logger = logger;
            _messageBuilder = messageBuilder;
            _crmService = crmService;
            _notificationService = notificationService;
            _statusHelper = statusHelper;
            _feeTypeHelper = feeTypeHelper;
            _myPayLogger = myPayLogger;
        }

        #endregion

        #region API: MyPay 回傳

        /// <summary>
        /// ========================================
        /// 金流伺服器回呼端點 (Server-to-Server Callback)
        /// ========================================
        /// 
        /// 【處理流程】
        /// 1. 接收金流平台回傳的交易資料
        /// 2. 驗證資料完整性與有效性
        /// 3. 判斷交易成功或失敗
        /// 4. 查詢並更新 CRM 收費單狀態
        /// 5. 發送 LINE 通知給使用者
        /// 6. 回傳 "8888" 確認接收（避免金流平台重送）
        /// </summary>
        [HttpPost("MyPayNotify")]
        public async Task<IActionResult> PaymentNotify([FromForm] MyPayReturnModel returnModel)
        {
            _logger.LogInformation($"[MyPay回傳] 收到金流回傳，OrderID: {returnModel?.order_id}, UID: {returnModel?.uid}, PRC: {returnModel?.prc}");

            ToolUtilityClass utility = null;

            try
            {
                // 步驟 1：基本檢查
                if (returnModel == null)
                {
                    _logger.LogWarning("[MyPay回傳] 回傳資料為空");
                    return BadRequest("回傳資料為空");
                }

                // 步驟 2：記錄完整回傳資訊
                _myPayLogger.LogFullReturnData(returnModel);

                // 步驟 3：驗證必要欄位完整性
                _logger.LogInformation("[MyPay回傳] 開始驗證欄位...");
                var validation = returnModel.ValidateAllFields();

                _logger.LogInformation($"[MyPay回傳] 驗證等級: {validation.Level}");

                if (validation.Warnings != null && validation.Warnings.Any())
                {
                    _logger.LogInformation($"[MyPay回傳] 資料驗證警告 ({validation.Warnings.Count}): {string.Join(", ", validation.Warnings)}");
                }

                if (!validation.IsValid)
                {
                    _logger.LogWarning($"[MyPay回傳] 資料驗證失敗 ({validation.Errors.Count}): {string.Join(", ", validation.Errors)}");
                    return Ok("8888");
                }

                _logger.LogInformation("[MyPay回傳] 欄位驗證通過");

                // 步驟 4：解析交易狀態
                bool isSuccess = _statusHelper.IsSuccessfulPaymentStatus(returnModel.prc);
                _logger.LogInformation($"[MyPay回傳] 交易狀態判定: PRC={returnModel.prc}, IsSuccess={isSuccess}");

                // 步驟 5：查詢對應的 CRM 收費單
                utility = new ToolUtilityClass(DYNAMICS_CONNECTION_NAME);
                Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_order_number", returnModel.order_id);

                if (feeEntity == null)
                {
                    _logger.LogWarning($"[MyPay回傳] 找不到對應收費單 - OrderId: {returnModel.order_id}");
                    return Ok("8888");
                }

                _logger.LogInformation($"[MyPay回傳] 找到收費單 - FeeId: {feeEntity.Id}");

                // 步驟 6：判斷收費單類型
                FeeType feeType = _feeTypeHelper.DetermineFeeType(utility, feeEntity);
                _logger.LogInformation($"[MyPay回傳] 收費單類型: {feeType}");

                // 步驟 7：取得連絡人資訊
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

                // 步驟 8：更新 CRM 收費單狀態與資訊
                _crmService.UpdateFeeEntityWithMyPayReturn(utility, feeEntity, returnModel, isSuccess);
                utility.UpdateEntity(ref feeEntity);
                _logger.LogInformation($"[MyPay回傳] 收費單已更新 - FeeId: {feeEntity.Id}");

                // 步驟 9：發送 LINE 通知給使用者
                if (!string.IsNullOrWhiteSpace(lineId))
                {
                    try
                    {
                        if (isSuccess)
                        {
                            _notificationService.SendLineNotificationByType(utility, feeEntity, returnModel, fullName, feeType, contactEntity);
                            _logger.LogInformation($"[MyPay回傳] LINE成功通知已發送 - OrderId: {returnModel.order_id}");
                        }
                        else
                        {
                            _notificationService.SendLineFailureNotificationByType(utility, feeEntity, returnModel, fullName, feeType, contactEntity);
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

                // 步驟 10：回傳確認接收代碼 "8888"
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

        #region API: 成功頁面

        /// <summary>
        /// 付款成功頁面 (供用戶查看結果)
        /// GET /api/MyPay/success
        /// </summary>
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
        /// </summary>
        [HttpGet("failure")]
        public IActionResult PaymentFailure([FromQuery] string order_id = "", [FromQuery] string msg = "")
        {
            ViewBag.OrderId = order_id;
            ViewBag.Message = !string.IsNullOrEmpty(msg) ? $"付款失敗：{msg}" : "付款失敗，請稍後再試或聯繫教會辦公室。";
            ViewBag.IsSuccess = false;
            return View("PaymentResult");
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
        /// 驗證所有欄位完整性
        /// </summary>
        public static ValidationResult ValidateAllFields(this MyPayReturnModel model)
        {
            var result = new ValidationResult();

            try
            {
                if (string.IsNullOrWhiteSpace(model.uid) || model.uid.Length != 32)
                {
                    result.IsValid = false;
                    result.Errors.Add("uid 格式錯誤");
                }

                if (string.IsNullOrWhiteSpace(model.key) || model.key.Length != 32)
                {
                    result.IsValid = false;
                    result.Errors.Add("key 格式錯誤");
                }

                if (!new[] { "250", "290", "600", "300", "400", "260", "270", "280" }.Contains(model.prc))
                {
                    result.IsValid = false;
                    result.Errors.Add("prc 狀態碼不在預期範圍內");
                }

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

            return result;
        }
    }
}