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
        /// 金流伺服器回呼端點。處理後需回傳字串888代表已接收。
        /// </summary>
        [HttpPost("MyPayReturn")]
        public async Task<IActionResult> PaymentReturn([FromForm] MyPayReturnModel returnModel)
        {
            _logger.LogInformation($"收到高鉅金流回傳，OrderID: {returnModel?.order_id}, 狀態: {returnModel?.state}");
            try
            {
                if (returnModel == null)
                {
                    _logger.LogWarning("回傳資料為空");
                    return BadRequest("回傳資料為空");
                }

                // 基本必要欄位檢查（實務上可直接400；此處僅記錄警告以免中斷金流平台重送機制）
                if (string.IsNullOrEmpty(returnModel.order_id) ||
                    string.IsNullOrEmpty(returnModel.transaction_id) ||
                    string.IsNullOrEmpty(returnModel.hash))
                {
                    _logger.LogWarning($"回傳資料缺少必要欄位: {returnModel.order_id}");
                    //return BadRequest("回傳資料缺少必要欄位");
                }

                // 實際驗證與處理交由封裝元件
                var qpayProcessor = new QPayProcessor(null); // TODO:以 DI方式注入設定

                // 建議在此驗證 hash以避免偽造通知
                // if (!qpayProcessor.VerifyMyPayHash(returnModel)) { return BadRequest("驗證失敗"); }

                bool success = await qpayProcessor.ProcessMyPayReturn(returnModel);
                if (success)
                {
                    _logger.LogInformation($"成功處理回傳: {returnModel.order_id}");
                    return Ok("888");
                }

                _logger.LogWarning($"處理回傳失敗: {returnModel.order_id}");
                // 按部分金流規格，仍回888 表示已接收，平台會另有對帳補償
                return Ok("888");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"處理回傳異常: {returnModel?.order_id}");
                return StatusCode(500, "處理異常");
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
                Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", order_id);
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
                        Entity feeEntity = utility.RetrieveEntityByField("new_fee", "new_q_pay_card_order_no", order_id);
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

        #region 狀態/文字 輔助
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

        private string GetPaymentStatusMessage(string prc)
        {
            if (string.IsNullOrWhiteSpace(prc)) return "付款狀態未知";
            switch (prc)
            {
                case "100": return "資料錯誤";
                case "200": return "資料正確，處理中";
                case "220": return "取消成功";
                case "230": return "退款成功";
                case "250": return "付款成功";
                case "260": return "交易成功，尚未付款完成";
                case "265": return "訂單綁定";
                case "270": return "交易成功，尚未付款完成（虛擬帳號）";
                case "275": return "交易成功，待審核（核貸中）";
                case "280": return "交易成功，尚未付款完成（儲值/WEBATM）";
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
            if (string.IsNullOrWhiteSpace(finishtime) || finishtime.Length !=14) return DateTime.Now;
            try
            {
                int year = int.Parse(finishtime.Substring(0,4));
                int month = int.Parse(finishtime.Substring(4,2));
                int day = int.Parse(finishtime.Substring(6,2));
                int hour = int.Parse(finishtime.Substring(8,2));
                int minute = int.Parse(finishtime.Substring(10,2));
                int second = int.Parse(finishtime.Substring(12,2));
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
            if (string.IsNullOrWhiteSpace(pfn)) return "未知";
            switch (pfn)
            {
                case "0":
                case "all": return "全部支付工具";
                case "1":
                case "CREDITCARD": return "信用卡";
                case "3":
                case "CSTORECODE": return "超商代碼";
                case "6":
                case "E_COLLECTION": return "虛擬帳號";
                case "10":
                case "ALIPAY": return "支付寶";
                case "13":
                case "WECHAT": return "微信支付";
                case "15":
                case "LINEPAYON": return "LINE Pay";
                case "20":
                case "APPLEPAY": return "Apple Pay";
                case "21":
                case "GOOGLEPAY": return "Google Pay";
                case "24":
                case "C_REDEEM": return "信用卡紅利";
                case "27":
                case "PION": return "Pi 拍錢包";
                case "31":
                case "JKOON": return "街口支付";
                default: return $"付費方法 {pfn}";
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
        #endregion

        #region 收費單類型與顯示文字
        private enum FeeType { Dedication, Course, Other }

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
                if (feeName.Contains("課程") || feeName.Contains("報名") || feeName.Contains("學費") || feeName.Contains("培訓") || feeName.Contains("研習"))
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
                if (categoryValue >=100000000 && categoryValue <=100000019)
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

        #region CRM 更新/通知
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
                    "[高鉅金流付款成功]" + Environment.NewLine +
                    $"訂單號: {orderId}{Environment.NewLine}" +
                    $"交易流水號(UID): {uid}{Environment.NewLine}" +
                    $"交易驗證碼(Key): {key}{Environment.NewLine}" +
                    $"交易狀態(PRC): {prc} ({statusMessage}){Environment.NewLine}" +
                    $"付款方式(PFN): {paymentMethodName}{Environment.NewLine}" +
                    $"交易金額: {cost}{Environment.NewLine}" +
                    $"實際金額: {actualCost ?? cost}{Environment.NewLine}" +
                    $"卡號: {cardno}{Environment.NewLine}" +
                    $"授權碼: {acode}{Environment.NewLine}" +
                    $"付款時間: {paymentTime:yyyy-MM-dd HH:mm:ss}";

                toolUtility.SetEntityStringAttribute(ref feeEntity, "new_description", newDescription);
                _logger.LogInformation($"UpdateFeeEntityWithMyPay: 更新完成 - FeeId: {feeEntity.Id}, OrderId: {orderId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"UpdateFeeEntityWithMyPay: 更新收費單失敗 - OrderId: {orderId}");
                throw;
            }
        }

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
                    $"[高鉅金流付款失敗] 訂單號: {orderId}, " +
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

        private string BuildDedicationSuccessMessage(string fullName, string orderId, string transactionId, decimal amount, string dedicationCategory, DateTime paymentTime)
        {
            var msg = $"【高鉅金流付款成功通知】{Environment.NewLine}{Environment.NewLine}" +
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

        private string BuildCoursePaymentSuccessMessage(string fullName, string orderId, string transactionId, decimal amount, string courseName, string courseSchedule, string courseLocation, DateTime paymentTime)
        {
            var msg = $"【高鉅金流付款成功通知】{Environment.NewLine}{Environment.NewLine}" +
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

        private string BuildGeneralPaymentSuccessMessage(string fullName, string orderId, string transactionId, decimal amount, string itemName, DateTime paymentTime)
        {
            var msg = $"【高鉅金流付款成功通知】{Environment.NewLine}{Environment.NewLine}" +
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
    }
}